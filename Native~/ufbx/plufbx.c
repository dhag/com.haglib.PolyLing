/* plufbx.c
 * ============================================================
 * PolyLing から ufbx（FBX 読み込み）を呼ぶための薄い C の口。
 * 規則の正本は PolyLing_FBX対応計画.md。
 *
 * ufbx の構造体は大きく入れ子が深いので C# へ直接は渡さない。
 * ここで平らな関数（数・配列の写し）にして公開する。
 *
 * ■ 読み込みの設定（plufbx_load_memory）
 *   ・座標は右手系・Y 上・前 +Z（ufbx_axes_right_handed_y_up）、単位 m へ ufbx が直す
 *     （UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY）。Unity の左手系への X 反転は C# 側。
 *   ・法線の無いメッシュは ufbx が作る（generate_missing_normals）。
 *   ・ファイルは C# が読んだバイト列を渡す（パスの文字コードを C 側で扱わない）。
 *     filename には元のパス（UTF-8）を渡し、テクスチャの相対パスの解決に使わせる。
 *
 * ■ 行列
 *   ufbx_matrix（3x4・列ベクトル）を 12 個の double（列 0, 1, 2, 3 の xyz）で写す。
 *
 * ■ 索引
 *   ノード・メッシュ・材質・テクスチャはシーンの型別の並び（typed_id）の索引。
 * ============================================================ */
#include "ufbx.h"
#include <string.h>
#include <stdlib.h>

#if defined(_WIN32)
#define PLUFBX_API __declspec(dllexport)
#else
#define PLUFBX_API __attribute__((visibility("default")))
#endif

/* ufbx の版（ufbx_pack_version の値）。C# 側はこれが読めることで DLL の有無を確かめる。 */
PLUFBX_API unsigned int plufbx_version(void)
{
    return (unsigned int)ufbx_source_version;
}

static void put_matrix(double *dst, const ufbx_matrix *m)
{
    for (int c = 0; c < 4; c++) {
        dst[c * 3 + 0] = m->cols[c].x;
        dst[c * 3 + 1] = m->cols[c].y;
        dst[c * 3 + 2] = m->cols[c].z;
    }
}

static ufbx_scene *S(void *h) { return (ufbx_scene *)h; }

/* ============================================================
 * シーン
 * ============================================================ */

/* 読み込む。失敗時は NULL を返し、err（UTF-8、NUL 終端）に理由を書く。 */
PLUFBX_API void *plufbx_load_memory(const void *data, long long size, const char *filename_utf8,
                                    char *err, int err_cap)
{
    ufbx_load_opts o;
    memset(&o, 0, sizeof(o));
    o.target_axes = ufbx_axes_right_handed_y_up;
    o.target_unit_meters = 1.0f;
    o.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;
    o.generate_missing_normals = true;
    if (filename_utf8) {
        o.filename.data = filename_utf8;
        o.filename.length = strlen(filename_utf8);
    }
    ufbx_error e;
    ufbx_scene *s = ufbx_load_memory(data, (size_t)size, &o, &e);
    if (!s) {
        if (err && err_cap > 0) ufbx_format_error(err, (size_t)err_cap, &e);
        return NULL;
    }
    return s;
}

PLUFBX_API void plufbx_free(void *h)
{
    if (h) ufbx_free_scene(S(h));
}

/* 数と元の設定。out[0..7] = ノード, メッシュ, 材質, テクスチャ, アニメーション, 元の上の軸, 元の単位（μm 単位の整数）, 0 */
PLUFBX_API void plufbx_scene_info(void *h, int *out)
{
    ufbx_scene *s = S(h);
    out[0] = (int)s->nodes.count;
    out[1] = (int)s->meshes.count;
    out[2] = (int)s->materials.count;
    out[3] = (int)s->textures.count;
    out[4] = (int)s->anim_stacks.count;
    out[5] = (int)s->settings.original_axis_up;
    out[6] = (int)(s->settings.original_unit_meters * 1000000.0 + 0.5);
    out[7] = 0;
}

/* ============================================================
 * ノード
 * ============================================================ */

PLUFBX_API const char *plufbx_node_name(void *h, int i)
{
    return S(h)->nodes.data[i]->name.data;
}

/* 明示のバインド姿勢。スキンクラスタのリンク行列が最優先、無ければバインドポーズ。
 * 戻り値 1 = クラスタ、2 = バインドポーズ、0 = 無し。 */
static int explicit_bind(ufbx_scene *s, ufbx_node *n, ufbx_matrix *out)
{
    for (size_t i = 0; i < s->skin_clusters.count; i++) {
        ufbx_skin_cluster *c = s->skin_clusters.data[i];
        if (c->bone_node == n) { *out = c->bind_to_world; return 1; }
    }
    for (size_t i = 0; i < s->poses.count; i++) {
        ufbx_pose *p = s->poses.data[i];
        if (!p->is_bind_pose) continue;
        for (size_t k = 0; k < p->bone_poses.count; k++) {
            if (p->bone_poses.data[k].bone_node == n) { *out = p->bone_poses.data[k].bone_to_world; return 2; }
        }
    }
    return 0;
}

/* ints[0..7] = 親（-1 = シーンの根）, メッシュ索引（-1 = 無し）, スケルトン属性あり, 属性の種類,
 *              明示のバインド（0/1/2）, シーンの根か, 材質スロット数（インスタンス側優先）, 子の数
 * world[12] = node_to_world、bind[12] = 明示のバインド（無ければ node_to_world）、geo[12] = geometry_to_node */
PLUFBX_API void plufbx_node_info(void *h, int i, int *ints, double *world, double *bind, double *geo)
{
    ufbx_scene *s = S(h);
    ufbx_node *n = s->nodes.data[i];
    ints[0] = n->parent ? (int)n->parent->typed_id : -1;
    ints[1] = n->mesh ? (int)n->mesh->typed_id : -1;
    ints[2] = n->bone ? 1 : 0;
    ints[3] = (int)n->attrib_type;
    ufbx_matrix b = n->node_to_world;
    ints[4] = explicit_bind(s, n, &b);
    ints[5] = n->is_root ? 1 : 0;
    ints[6] = n->materials.count ? (int)n->materials.count : (n->mesh ? (int)n->mesh->materials.count : 0);
    ints[7] = (int)n->children.count;
    put_matrix(world, &n->node_to_world);
    put_matrix(bind, &b);
    put_matrix(geo, &n->geometry_to_node);
}

/* ノードの材質スロット → シーンの材質索引（-1 = 無し）。インスタンス側の材質を優先する。 */
PLUFBX_API int plufbx_node_material(void *h, int i, int slot)
{
    ufbx_node *n = S(h)->nodes.data[i];
    ufbx_material_list *l = n->materials.count ? &n->materials : (n->mesh ? &n->mesh->materials : NULL);
    if (!l || slot < 0 || (size_t)slot >= l->count || !l->data[slot]) return -1;
    return (int)l->data[slot]->typed_id;
}

/* ============================================================
 * メッシュ
 * ============================================================ */

static size_t blend_channel_count(ufbx_mesh *m)
{
    size_t n = 0;
    for (size_t d = 0; d < m->blend_deformers.count; d++) n += m->blend_deformers.data[d]->channels.count;
    return n;
}

static ufbx_blend_channel *blend_channel(ufbx_mesh *m, int k)
{
    for (size_t d = 0; d < m->blend_deformers.count; d++) {
        ufbx_blend_deformer *bd = m->blend_deformers.data[d];
        if ((size_t)k < bd->channels.count) return bd->channels.data[k];
        k -= (int)bd->channels.count;
    }
    return NULL;
}

/* out[0..7] = 制御点, 面, 角, 法線あり, UV あり, スキン変形の数, 先頭スキンのクラスタ数, ブレンドシェイプの数 */
PLUFBX_API void plufbx_mesh_info(void *h, int mi, int *out)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    out[0] = (int)m->num_vertices;
    out[1] = (int)m->num_faces;
    out[2] = (int)m->num_indices;
    out[3] = m->vertex_normal.exists ? 1 : 0;
    out[4] = m->vertex_uv.exists ? 1 : 0;
    out[5] = (int)m->skin_deformers.count;
    out[6] = m->skin_deformers.count ? (int)m->skin_deformers.data[0]->clusters.count : 0;
    out[7] = (int)blend_channel_count(m);
}

/* 制御点の位置（ジオメトリ空間）。xyz[3 * 制御点] */
PLUFBX_API void plufbx_mesh_positions(void *h, int mi, float *xyz)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    for (size_t v = 0; v < m->num_vertices; v++) {
        ufbx_vec3 p = m->vertices.data[v];
        xyz[v * 3 + 0] = (float)p.x; xyz[v * 3 + 1] = (float)p.y; xyz[v * 3 + 2] = (float)p.z;
    }
}

/* 面ごとの角の数と材質スロット。 */
PLUFBX_API void plufbx_mesh_faces(void *h, int mi, int *sizes, int *mats)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    for (size_t f = 0; f < m->num_faces; f++) {
        sizes[f] = (int)m->faces.data[f].num_indices;
        mats[f] = m->face_material.count ? (int)m->face_material.data[f] : 0;
    }
}

/* 角ごとの制御点・法線・UV（先頭の UV セット）。無いものは 0 で埋める。 */
PLUFBX_API void plufbx_mesh_corners(void *h, int mi, int *vidx, float *nrm, float *uv)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    for (size_t i = 0; i < m->num_indices; i++) {
        vidx[i] = (int)m->vertex_indices.data[i];
        if (m->vertex_normal.exists) {
            ufbx_vec3 n = ufbx_get_vertex_vec3(&m->vertex_normal, i);
            nrm[i * 3 + 0] = (float)n.x; nrm[i * 3 + 1] = (float)n.y; nrm[i * 3 + 2] = (float)n.z;
        } else {
            nrm[i * 3 + 0] = 0; nrm[i * 3 + 1] = 0; nrm[i * 3 + 2] = 0;
        }
        if (m->vertex_uv.exists) {
            ufbx_vec2 t = ufbx_get_vertex_vec2(&m->vertex_uv, i);
            uv[i * 2 + 0] = (float)t.x; uv[i * 2 + 1] = (float)t.y;
        } else {
            uv[i * 2 + 0] = 0; uv[i * 2 + 1] = 0;
        }
    }
}

/* 先頭スキンのクラスタ c：戻り値はボーンのノード索引（-1 = 無し）。geometry_to_bone と bind_to_world を写す。 */
PLUFBX_API int plufbx_mesh_cluster(void *h, int mi, int c, double *geo_to_bone, double *bind_to_world)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    ufbx_skin_cluster *cl = m->skin_deformers.data[0]->clusters.data[c];
    put_matrix(geo_to_bone, &cl->geometry_to_bone);
    put_matrix(bind_to_world, &cl->bind_to_world);
    return cl->bone_node ? (int)cl->bone_node->typed_id : -1;
}

/* 先頭スキンのウェイト。制御点ごとに重い順で max_per 個まで（足りない分は cluster = -1, weight = 0）。
 * 戻り値は、ウェイトが max_per を超えていた制御点の数。 */
PLUFBX_API int plufbx_mesh_skin_weights(void *h, int mi, int max_per, int *cluster, float *weight)
{
    ufbx_mesh *m = S(h)->meshes.data[mi];
    ufbx_skin_deformer *sk = m->skin_deformers.data[0];
    int over = 0;
    for (size_t v = 0; v < m->num_vertices; v++) {
        int n = 0;
        if (v < sk->vertices.count) {
            ufbx_skin_vertex sv = sk->vertices.data[v];
            if ((int)sv.num_weights > max_per) over++;
            for (uint32_t k = 0; k < sv.num_weights && n < max_per; k++, n++) {
                ufbx_skin_weight w = sk->weights.data[sv.weight_begin + k];
                cluster[v * max_per + n] = (int)w.cluster_index;
                weight[v * max_per + n] = (float)w.weight;
            }
        }
        for (; n < max_per; n++) { cluster[v * max_per + n] = -1; weight[v * max_per + n] = 0; }
    }
    return over;
}

PLUFBX_API const char *plufbx_mesh_blend_name(void *h, int mi, int k)
{
    ufbx_blend_channel *ch = blend_channel(S(h)->meshes.data[mi], k);
    return ch ? ch->name.data : "";
}

/* ブレンドシェイプの重み 1 の形（target_shape。無ければ最後のキー）。
 * vidx と d3 が NULL なら数だけ返す。 */
PLUFBX_API int plufbx_mesh_blend_offsets(void *h, int mi, int k, int *vidx, float *d3)
{
    ufbx_blend_channel *ch = blend_channel(S(h)->meshes.data[mi], k);
    if (!ch) return 0;
    ufbx_blend_shape *sh = ch->target_shape;
    if (!sh && ch->keyframes.count) sh = ch->keyframes.data[ch->keyframes.count - 1].shape;
    if (!sh) return 0;
    if (vidx && d3) {
        for (size_t i = 0; i < sh->num_offsets; i++) {
            vidx[i] = (int)sh->offset_vertices.data[i];
            ufbx_vec3 d = sh->position_offsets.data[i];
            d3[i * 3 + 0] = (float)d.x; d3[i * 3 + 1] = (float)d.y; d3[i * 3 + 2] = (float)d.z;
        }
    }
    return (int)sh->num_offsets;
}

/* ============================================================
 * 材質・テクスチャ
 * ============================================================ */

PLUFBX_API const char *plufbx_material_name(void *h, int i)
{
    return S(h)->materials.data[i]->name.data;
}

static int file_texture_of(ufbx_texture *t)
{
    if (!t) return -1;
    if (t->type != UFBX_TEXTURE_FILE && t->file_textures.count) t = t->file_textures.data[0];
    return (int)t->typed_id;
}

/* vals[0..12] = 拡散色 rgb, 拡散係数, 透明色 rgb, 透明係数, 発光色 rgb, 発光係数, バンプ係数
 * tex[0..3]  = 拡散, 法線マップ, バンプ, 発光 のテクスチャ索引（-1 = 無し） */
PLUFBX_API void plufbx_material_info(void *h, int i, float *vals, int *tex)
{
    ufbx_material *m = S(h)->materials.data[i];
    vals[0]  = (float)m->fbx.diffuse_color.value_vec4.x;
    vals[1]  = (float)m->fbx.diffuse_color.value_vec4.y;
    vals[2]  = (float)m->fbx.diffuse_color.value_vec4.z;
    vals[3]  = (float)m->fbx.diffuse_factor.value_real;
    vals[4]  = (float)m->fbx.transparency_color.value_vec4.x;
    vals[5]  = (float)m->fbx.transparency_color.value_vec4.y;
    vals[6]  = (float)m->fbx.transparency_color.value_vec4.z;
    vals[7]  = (float)m->fbx.transparency_factor.value_real;
    vals[8]  = (float)m->fbx.emission_color.value_vec4.x;
    vals[9]  = (float)m->fbx.emission_color.value_vec4.y;
    vals[10] = (float)m->fbx.emission_color.value_vec4.z;
    vals[11] = (float)m->fbx.emission_factor.value_real;
    vals[12] = (float)m->fbx.bump_factor.value_real;
    tex[0] = file_texture_of(m->fbx.diffuse_color.texture);
    tex[1] = file_texture_of(m->fbx.normal_map.texture);
    tex[2] = file_texture_of(m->fbx.bump.texture);
    tex[3] = file_texture_of(m->fbx.emission_color.texture);
}

/* which: 0 = 名前, 1 = filename（読み込んだファイルからの解決済み）, 2 = absolute_filename, 3 = relative_filename */
PLUFBX_API const char *plufbx_texture_string(void *h, int i, int which)
{
    ufbx_texture *t = S(h)->textures.data[i];
    switch (which) {
    case 0: return t->name.data;
    case 1: return t->filename.data;
    case 2: return t->absolute_filename.data;
    default: return t->relative_filename.data;
    }
}

static ufbx_blob texture_content(ufbx_texture *t)
{
    if (t->content.size) return t->content;
    if (t->video && t->video->content.size) return t->video->content;
    ufbx_blob none = { NULL, 0 };
    return none;
}

/* ints[0..2] = 埋め込みの大きさ（バイト）, UV 変換あり, 動画（画像）要素の索引（-1 = 無し）
 * st[0..3]   = テクスチャの Scaling x, y と Translation x, y（FbxTexture::SetScale / SetTranslation の値そのまま） */
PLUFBX_API void plufbx_texture_info(void *h, int i, int *ints, float *st)
{
    ufbx_texture *t = S(h)->textures.data[i];
    ints[0] = (int)texture_content(t).size;
    ints[1] = t->has_uv_transform ? 1 : 0;
    ints[2] = t->video ? (int)t->video->typed_id : -1;
    ufbx_vec3 one = { 1, 1, 1 }, zero = { 0, 0, 0 };
    ufbx_vec3 sc = ufbx_find_vec3(&t->props, "Scaling", one);
    ufbx_vec3 tr = ufbx_find_vec3(&t->props, "Translation", zero);
    st[0] = (float)sc.x;
    st[1] = (float)sc.y;
    st[2] = (float)tr.x;
    st[3] = (float)tr.y;
}

/* 埋め込みのバイト列を dst へ写す。戻り値は写したバイト数。 */
PLUFBX_API int plufbx_texture_content(void *h, int i, unsigned char *dst, int cap)
{
    ufbx_blob b = texture_content(S(h)->textures.data[i]);
    size_t n = b.size < (size_t)cap ? b.size : (size_t)cap;
    if (n) memcpy(dst, b.data, n);
    return (int)n;
}

/* ============================================================
 * アニメーション（段 5）
 * ============================================================ */

PLUFBX_API const char *plufbx_anim_stack_name(void *h, int k)
{
    return S(h)->anim_stacks.data[k]->name.data;
}

/* テイク k の始まりと終わり（秒）。fps は scene の設定（ufbx_scene_settings.frames_per_second）。 */
PLUFBX_API void plufbx_anim_stack_info(void *h, int k, double *out)
{
    ufbx_scene *s = S(h);
    ufbx_anim_stack *st = s->anim_stacks.data[k];
    out[0] = st->time_begin;
    out[1] = st->time_end;
    out[2] = s->settings.frames_per_second;
}

/* シーンのブレンドシェイプチャンネルの数。 */
PLUFBX_API int plufbx_blend_channel_count(void *h)
{
    return (int)S(h)->blend_channels.count;
}

PLUFBX_API const char *plufbx_blend_channel_name(void *h, int i)
{
    return S(h)->blend_channels.data[i]->name.data;
}

/* チャンネル i を持つメッシュの最初のインスタンスのノード索引（-1 = 無し）。 */
PLUFBX_API int plufbx_blend_channel_node(void *h, int i)
{
    ufbx_scene *s = S(h);
    ufbx_blend_channel *ch = s->blend_channels.data[i];
    for (size_t mi = 0; mi < s->meshes.count; mi++) {
        ufbx_mesh *m = s->meshes.data[mi];
        for (size_t d = 0; d < m->blend_deformers.count; d++) {
            ufbx_blend_deformer *bd = m->blend_deformers.data[d];
            for (size_t c = 0; c < bd->channels.count; c++)
                if (bd->channels.data[c] == ch)
                    return m->instances.count ? (int)m->instances.data[0]->typed_id : -1;
        }
    }
    return -1;
}

/* 1 フレーム分の node_to_world を ufbx_evaluate_transform から組む（全ノードが継承 NORMAL のときだけ使う）。
 * NORMAL は child.node_to_world = parent.node_to_world · child.node_to_parent（ufbx.h の ufbx_inherit_mode）。
 * order は node_depth の昇順に並べたノードの索引。 */
static void compose_worlds(ufbx_scene *s, ufbx_anim *anim, double t, const int *order, ufbx_matrix *w)
{
    for (size_t k = 0; k < s->nodes.count; k++) {
        ufbx_node *n = s->nodes.data[order[k]];
        if (!n->parent) { w[n->typed_id] = n->node_to_world; continue; }
        ufbx_transform tr = ufbx_evaluate_transform(anim, n, t);
        ufbx_matrix local = ufbx_transform_to_matrix(&tr);
        w[n->typed_id] = ufbx_matrix_mul(&w[n->parent->typed_id], &local);
    }
}

/* ノードの索引を node_depth の昇順（同じ深さは索引順）に並べる。 */
static void depth_order(ufbx_scene *s, int *order)
{
    size_t nn = s->nodes.count, k = 0;
    uint32_t maxd = 0;
    for (size_t i = 0; i < nn; i++) if (s->nodes.data[i]->node_depth > maxd) maxd = s->nodes.data[i]->node_depth;
    for (uint32_t d = 0; d <= maxd; d++)
        for (size_t i = 0; i < nn; i++)
            if (s->nodes.data[i]->node_depth == d) order[k++] = (int)i;
}

/* 全ノードが継承 NORMAL か（plufbx_anim_sample が速い経路を使えるか）。 */
PLUFBX_API int plufbx_all_inherit_normal(void *h)
{
    ufbx_scene *s = S(h);
    for (size_t i = 0; i < s->nodes.count; i++)
        if (s->nodes.data[i]->inherit_mode != UFBX_INHERIT_MODE_NORMAL) return 0;
    return 1;
}

/* テイク k を時刻 t0 + f·dt（f = 0..frames-1）で評価し、
 *   world[(f·nNodes + n)·12] … nodes[n] の node_to_world（12 個の float。put_matrix と同じ並び）
 *   weights[f·nChannels + c]  … シーンのブレンドシェイプチャンネル c の重み（0〜1。weights が NULL なら書かない）
 * を写す。戻り値は評価できたフレーム数（失敗したらそこで止まる）。err に理由。 */
PLUFBX_API int plufbx_anim_sample(void *h, int k, double t0, double dt, int frames,
                                  const int *nodes, int nNodes, float *world, float *weights,
                                  char *err, int err_cap)
{
    ufbx_scene *s = S(h);
    ufbx_anim *anim = s->anim_stacks.data[k]->anim;
    size_t nCh = s->blend_channels.count;

    /* 速い経路：全ノードが継承 NORMAL なら、局所変換（ufbx_evaluate_transform）を親から順に掛ける。
     * そうでなければ ufbx_evaluate_scene（継承の種類をすべて ufbx が扱う。メッシュも評価するので遅い）。 */
    if (plufbx_all_inherit_normal(h)) {
        size_t nn = s->nodes.count;
        int *order = (int *)malloc(nn * sizeof(int));
        ufbx_matrix *w = (ufbx_matrix *)malloc(nn * sizeof(ufbx_matrix));
        if (!order || !w) { free(order); free(w); if (err && err_cap > 0) strncpy(err, "out of memory", (size_t)err_cap); return 0; }
        depth_order(s, order);
        for (int f = 0; f < frames; f++) {
            double t = t0 + f * dt;
            compose_worlds(s, anim, t, order, w);
            for (int n = 0; n < nNodes; n++) {
                const ufbx_matrix *m = &w[nodes[n]];
                float *dst = world + ((size_t)f * nNodes + n) * 12;
                for (int c = 0; c < 4; c++) {
                    dst[c * 3 + 0] = (float)m->cols[c].x;
                    dst[c * 3 + 1] = (float)m->cols[c].y;
                    dst[c * 3 + 2] = (float)m->cols[c].z;
                }
            }
            if (weights)
                for (size_t c = 0; c < nCh; c++)
                    weights[(size_t)f * nCh + c] = (float)ufbx_evaluate_blend_weight(anim, s->blend_channels.data[c], t);
        }
        free(order);
        free(w);
        return frames;
    }

    for (int f = 0; f < frames; f++) {
        ufbx_error e;
        ufbx_scene *ev = ufbx_evaluate_scene(s, anim, t0 + f * dt, NULL, &e);
        if (!ev) {
            if (err && err_cap > 0) ufbx_format_error(err, (size_t)err_cap, &e);
            return f;
        }
        for (int n = 0; n < nNodes; n++) {
            const ufbx_matrix *m = &ev->nodes.data[nodes[n]]->node_to_world;
            float *dst = world + ((size_t)f * nNodes + n) * 12;
            for (int c = 0; c < 4; c++) {
                dst[c * 3 + 0] = (float)m->cols[c].x;
                dst[c * 3 + 1] = (float)m->cols[c].y;
                dst[c * 3 + 2] = (float)m->cols[c].z;
            }
        }
        if (weights)
            for (size_t c = 0; c < nCh; c++)
                weights[(size_t)f * nCh + c] = (float)ev->blend_channels.data[c]->weight;
        ufbx_free_scene(ev);
    }
    return frames;
}
