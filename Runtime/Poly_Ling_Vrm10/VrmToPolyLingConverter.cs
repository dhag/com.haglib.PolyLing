// VrmToPolyLingConverter.cs
// ============================================================
// VRM（VrmLib.Model ＋ VRM 拡張データ）→ ModelContext の材料
// ============================================================
//
// 【分離規約】IVrm10Exporter.cs / IVrm10Importer.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属し、VRM パッケージへの依存はここに閉じる。
//
// ============================================================
// 座標系
// ============================================================
//
//   ModelReader.Read が VrmLib.Model を Unity 座標へ直して返す
//   （ModelReader.cs:128-133 → Model.cs:381-386, 402-512）。
//   位置・法線・モーフ・ノード行列・逆バインド行列の X 反転、三角形の反転、
//   UV の V 反転まで済んでいる。書き出しの ConvertCoordinate(Vrm1) の逆変換なので、
//   ここでは軸を触らない。
//   VRM 拡張データ（揺れ・視線・制約）は JSON の値のままなので、
//   UniVRM の Vrm10Importer と同じ X 反転をここで行う。
//
// ============================================================
// ModelContext の並び
// ============================================================
//
//   ボーン → 描画オブジェクト → モーフ の順（PMXImporter と同じ）。
//   ・ボーン     … メッシュを持たないノード ＋ 実スキンの関節ノード。DFS 順。
//                  ただし書き出し側が作る入れ物「Armature」は条件付きで読み飛ばす
//                  （DetectContainerToSkip）。
//                  BoneTransform は親ボーンからのローカル、親がボーンでなければ
//                  ルートからの変換（HierarchyImportWindow.cs:577-594 と同じ規則）。
//                  BindPose はワールド行列の逆（同 :398-404）。
//   ・描画オブジェクト … メッシュを持つノード 1 つにつき 1 つ。
//        実スキン   … 頂点を「関節ワールド × 逆バインド」でルート空間へベイク
//                      （同 :706-728 と同じ考え方）。親なし・変換なし。
//        それ以外   … 頂点はノードのローカル。BoneTransform はノードのローカル変換、
//                      親はノードの親。ModelReader がモーフだけのメッシュに付ける
//                      仮スキン（GltfIndex = -1、ModelReader.cs:77-122）もこちら。
//   ・モーフ     … モーフターゲットごとに、基底メッシュの複製へ差分を足したもの
//                  （PMXImporter.Morph.cs:291-329 と同じ形）。
//
// ============================================================
// 頂点と面
// ============================================================
//
//   glTF の頂点 1 つを Vertex 1 つにする（統合しない）。面は三角形で、
//   UV / 法線の索引は 0、材質は glTF の材質索引（PMXImporter.Bone.cs:640-718 と同じ）。
//   統合しないのでモーフ差分の頂点番号がそのまま一致する。
//
// ============================================================
// 表情（PMX のグループモーフと同じ規則：PMXImporter.Morph.cs:129-225）
// ============================================================
//
//   ・同名のモーフターゲットをまとめて仮の Vertex 表情にする。
//   ・VRM の表情は Group にし、MorphTargetBinds を重み付きで展開する。
//     子の名前は GroupChildren に残す。
//   ・どの VRM 表情にも使われない仮表情は Vertex 表情として残す。
//     使われたものは Group に吸収して消す。
//   ・プリセットは Name と NameEnglish にプリセット名を入れる。
//     書き出しの ResolvePreset が NameEnglish で同じプリセットへ戻す
//     （Vrm10SceneAssembler.cs の ResolvePreset）。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Vrm;

using VrmExt    = UniGLTF.Extensions.VRMC_vrm;
using SpringExt = UniGLTF.Extensions.VRMC_springBone;
using MToonExt  = UniGLTF.Extensions.VRMC_materials_mtoon;

namespace Poly_Ling.Vrm10Impl
{
    internal sealed class VrmToPolyLingConverter
    {
        private readonly Vrm10Data           _vrm;
        private readonly GltfData            _data;
        private readonly string              _filePath;
        private readonly Vrm10ImportSettings _settings;
        private readonly Vrm10ImportResult   _result = new Vrm10ImportResult();

        private VrmLib.Model _model;

        // glTF ノード索引 ↔ VrmLib.Node（ModelReader は glTF の並びで Nodes を作る：ModelReader.cs:30-35）
        private readonly Dictionary<VrmLib.Node, int> _nodeIndex = new Dictionary<VrmLib.Node, int>();

        // glTF ノード索引 → MeshContext 索引
        private readonly Dictionary<int, int> _boneCtxByNode = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _meshCtxByNode = new Dictionary<int, int>();

        // 実スキンの関節ノード（メッシュを持っていてもボーンにする）
        private readonly HashSet<VrmLib.Node> _jointNodes = new HashSet<VrmLib.Node>();

        // 読み飛ばす入れ物ノード（書き出し側が作る「Armature」。DetectContainerToSkip）。無ければ null。
        private VrmLib.Node _skippedContainer;

        // (glTF ノード索引, ターゲット索引) → モーフ MeshContext 索引
        private readonly Dictionary<(int node, int target), int> _morphCtx = new Dictionary<(int, int), int>();
        // (glTF ノード索引, ターゲット索引) → ターゲット名
        private readonly Dictionary<(int node, int target), string> _morphName = new Dictionary<(int, int), string>();

        // 材質
        private readonly List<string> _materialNames = new List<string>();   // glTF 材質索引 → 名前
        private int _defaultMaterialIndex = -1;

        // 画像（glTF image 索引 → 書き出したファイル / バイト列）
        private readonly Dictionary<int, string> _imagePath  = new Dictionary<int, string>();
        private readonly Dictionary<int, byte[]> _imageBytes = new Dictionary<int, byte[]>();
        private string _textureFolder;

        // モーフの作成待ち（描画オブジェクトを全部並べてから索引を振るため）
        private sealed class PendingMorph
        {
            public MeshContext BaseCtx;
            public int         NodeIndex;
            public int         TargetIndex;
            public string      Name;
            public Vector3[]   Deltas;
        }
        private readonly List<PendingMorph> _pendingMorphs = new List<PendingMorph>();

        public VrmToPolyLingConverter(Vrm10Data vrm, string filePath, Vrm10ImportSettings settings)
        {
            _vrm      = vrm;
            _data     = vrm.Data;
            _filePath = filePath;
            _settings = settings ?? Vrm10ImportSettings.CreateDefault();
        }

        // ================================================================
        // エントリ
        // ================================================================

        public Vrm10ImportResult Convert()
        {
            // Unity 座標の VrmLib.Model（ノード行列・頂点・モーフ・逆バインド行列を変換済み）
            _model = ModelReader.Read(_data);
            for (int i = 0; i < _model.Nodes.Count; i++)
                _nodeIndex[_model.Nodes[i]] = i;

            foreach (var mg in _model.MeshGroups)
            {
                var skin = mg?.Skin;
                if (!IsRealSkin(skin)) continue;
                foreach (var j in skin.Joints)
                    if (j != null) _jointNodes.Add(j);
            }

            DetectContainerToSkip();

            PrepareTextureFolder();
            BuildMaterials();
            BuildBones();
            BuildMeshes();
            FinalizeMaterials();
            BuildMorphContexts();
            FinalizeUnityMeshes();

            BuildHumanoid();
            BuildMeta();
            BuildLookAt();
            BuildFirstPerson();

            if (_settings.ImportMorphs && _settings.ImportExpressions)
                BuildExpressions();
            else if (_settings.ImportMorphs)
                BuildExpressions(onlyTargets: true);

            if (_settings.ImportSpringBones) BuildSpringBones();
            if (_settings.ImportConstraints) BuildConstraints();
            else WarnIfConstraintsPresent();

            SkinKindOps.RecomputeAll(_result.MeshContexts);

            _result.MaterialCount = _result.MaterialReferences.Count;
            _result.TextureFolder = _imagePath.Count > 0 ? _textureFolder : "";
            _result.TextureCount  = _imagePath.Count;
            _result.Success = true;
            return _result;
        }

        // ================================================================
        // 共通
        // ================================================================

        /// <summary>glTF 由来の実スキンか（ModelReader が作る仮スキンは GltfIndex = -1）。</summary>
        private static bool IsRealSkin(VrmLib.Skin skin)
            => skin != null && skin.GltfIndex != -1 && skin.Joints != null && skin.Joints.Count > 0;

        private bool IsBoneNode(VrmLib.Node n)
            => n != null && n != _skippedContainer && (n.MeshGroup == null || _jointNodes.Contains(n));

        /// <summary>
        /// 最上位か。読み飛ばした入れ物ノード（DetectContainerToSkip）の直下も最上位として扱う。
        /// 入れ物は単位変換なので、直下のノードのワールド行列はそのまま最上位の変換になる。
        /// </summary>
        private bool IsTopLevel(VrmLib.Node n)
            => n?.Parent == null || n.Parent == _model.Root
            || (_skippedContainer != null && n.Parent == _skippedContainer);

        private int NodeIndexOf(VrmLib.Node n)
            => (n != null && _nodeIndex.TryGetValue(n, out int i)) ? i : -1;

        /// <summary>
        /// ノード索引 → 付帯先の MeshContext 索引（ボーン優先、無ければ描画オブジェクト）。
        /// 揺れ・制約の付帯先はノードであればよい（MeshObject.cs の SpringBone 節）。
        /// </summary>
        private int CtxOfNode(int nodeIndex)
        {
            if (_boneCtxByNode.TryGetValue(nodeIndex, out int b)) return b;
            if (_meshCtxByNode.TryGetValue(nodeIndex, out int m)) return m;
            return -1;
        }

        /// <summary>ノード索引 → 描画オブジェクトの MeshContext 索引（無ければボーン）。</summary>
        private int RendererCtxOfNode(int nodeIndex)
        {
            if (_meshCtxByNode.TryGetValue(nodeIndex, out int m)) return m;
            if (_boneCtxByNode.TryGetValue(nodeIndex, out int b)) return b;
            return -1;
        }

        private MeshContext Ctx(int index)
            => (index >= 0 && index < _result.MeshContexts.Count) ? _result.MeshContexts[index] : null;

        private static void Decompose(Matrix4x4 m, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos   = m.GetColumn(3);
            rot   = m.rotation;
            scale = m.lossyScale;
        }

        private static bool IsIdentity(Matrix4x4 m)
        {
            var id = Matrix4x4.identity;
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(m[i] - id[i]) > 1e-6f) return false;
            return true;
        }

        private static Vector3 InvertX(float[] f, Vector3 fallback)
        {
            if (f == null || f.Length < 3) return fallback;
            return new Vector3(-f[0], f[1], f[2]);
        }

        // ================================================================
        // 入れ物ノードの読み飛ばし
        // ================================================================

        /// <summary>
        /// 書き出し側が作る入れ物ノード「Armature」（HierarchyBuilder.cs:202-207）を読み飛ばす。
        /// 読み飛ばさないとメッシュを持たないノードとしてボーンになり、書き出すたびに
        /// Armature の下へもう 1 段 Armature が入る（往復でボーンが 1 本ずつ増える）。
        /// HierarchyImportWindow が「Armature」などの入れ物を除外する扱い
        /// （HierarchyImportWindow.cs:417-427）と同じ考え方。
        ///
        /// 【条件（すべて満たすときだけ）】
        ///   ・最上位にあり、名前が「Armature」
        ///   ・メッシュを持たない
        ///   ・スキンの関節ではない
        ///   ・Humanoid・揺れ・制約のどこからも参照されていない
        ///   ・変換が単位行列（直下のノードのワールド行列がそのまま使える）
        ///   視線（lookAt）はノードを参照しない（頭ボーンからのオフセットだけ）。
        ///   一人称はレンダラのノードを参照するので、メッシュを持たないここは対象外。
        /// </summary>
        private void DetectContainerToSkip()
        {
            _skippedContainer = null;

            VrmLib.Node candidate = null;
            foreach (var n in _model.Root.Children)
            {
                if (n == null || n.Name != "Armature") continue;
                candidate = n;
                break;
            }
            if (candidate == null) return;

            if (candidate.MeshGroup != null) return;
            if (_jointNodes.Contains(candidate)) return;
            if (!IsIdentity(candidate.Matrix)) return;

            int ci = NodeIndexOf(candidate);
            if (ci < 0 || CollectReferencedNodes().Contains(ci)) return;

            _skippedContainer = candidate;
        }

        /// <summary>Humanoid・揺れ・制約が参照している glTF ノード索引。</summary>
        private HashSet<int> CollectReferencedNodes()
        {
            var set = new HashSet<int>();

            // Humanoid（VRMC_vrm.humanoid.humanBones の各 node）
            var hb = _vrm.VrmExtension?.Humanoid?.HumanBones;
            if (hb != null)
            {
                foreach (var f in typeof(VrmExt.HumanBones).GetFields())
                {
                    if (f.FieldType != typeof(VrmExt.HumanBone)) continue;
                    var bone = f.GetValue(hb) as VrmExt.HumanBone;
                    if (bone?.Node != null) set.Add(bone.Node.Value);
                }
            }

            // 揺れ（コライダー・ジョイント・center）
            if (SpringExt.GltfDeserializer.TryGet(_data.GLTF.extensions, out SpringExt.VRMC_springBone sb) && sb != null)
            {
                if (sb.Colliders != null)
                    foreach (var c in sb.Colliders)
                        if (c?.Node != null) set.Add(c.Node.Value);

                if (sb.Springs != null)
                {
                    foreach (var s in sb.Springs)
                    {
                        if (s == null) continue;
                        if (s.Center.HasValue) set.Add(s.Center.Value);
                        if (s.Joints == null) continue;
                        foreach (var j in s.Joints)
                            if (j?.Node != null) set.Add(j.Node.Value);
                    }
                }
            }

            // 制約（付いているノードと、制約元）
            var nodes = _data.GLTF.nodes;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!UniGLTF.Extensions.VRMC_node_constraint.GltfDeserializer.TryGet(
                            nodes[i].extensions, out UniGLTF.Extensions.VRMC_node_constraint.VRMC_node_constraint ext)
                        || ext?.Constraint == null)
                        continue;

                    set.Add(i);
                    var con = ext.Constraint;
                    int? src = con.Roll?.Source ?? con.Aim?.Source ?? con.Rotation?.Source;
                    if (src.HasValue) set.Add(src.Value);
                }
            }

            return set;
        }

        // ================================================================
        // テクスチャ
        // ================================================================

        private void PrepareTextureFolder()
        {
            _textureFolder = !string.IsNullOrEmpty(_settings.TextureFolder)
                ? _settings.TextureFolder
                : Vrm10ImportSettings.DefaultTextureFolder(_filePath);
        }

        /// <summary>
        /// glTF テクスチャ索引 → 書き出したファイルパス。書き出しは初回だけ行う。
        /// 画像は元のバイト列のまま書く（再エンコードしない）。
        /// </summary>
        private string ResolveTexture(int textureIndex)
        {
            int image = ImageOfTexture(textureIndex);
            if (image < 0) return null;
            if (_imagePath.TryGetValue(image, out var cached)) return cached;
            if (!_settings.ExtractTextures) return null;

            var got = _data.GetBytesFromImage(image);
            if (!got.HasValue)
            {
                _result.Warnings.Add($"画像 {image} を取り出せません。");
                return null;
            }

            string mime = got.Value.mimeType ?? "";
            string ext  = mime == "image/png"  ? ".png"
                        : mime == "image/jpeg" ? ".jpg"
                        : null;
            if (ext == null)
            {
                _result.Warnings.Add($"画像 {image} は形式 \"{mime}\" のため書き出しません（PNG / JPEG のみ対応）。");
                return null;
            }

            byte[] bytes = got.Value.binary.ToArray();

            string baseName = _data.GLTF.images[image]?.name;
            if (string.IsNullOrEmpty(baseName)) baseName = "image";
            foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');

            Directory.CreateDirectory(_textureFolder);
            string path = Path.Combine(_textureFolder, $"{image:D3}_{baseName}{ext}");
            File.WriteAllBytes(path, bytes);

            _imagePath[image]  = path;
            _imageBytes[image] = bytes;
            return path;
        }

        private int ImageOfTexture(int textureIndex)
        {
            var textures = _data.GLTF.textures;
            if (textures == null || textureIndex < 0 || textureIndex >= textures.Count) return -1;
            var src = textures[textureIndex]?.source;
            if (!src.HasValue) return -1;
            int image = src.Value;
            var images = _data.GLTF.images;
            return (images != null && image >= 0 && image < images.Count) ? image : -1;
        }

        /// <summary>表示用の Texture2D を作る（書き出したバイト列から）。</summary>
        private Texture2D CreateTexture(string path, bool linear)
        {
            if (string.IsNullOrEmpty(path)) return null;

            byte[] bytes = null;
            foreach (var kv in _imagePath)
                if (kv.Value == path) { _imageBytes.TryGetValue(kv.Key, out bytes); break; }
            if (bytes == null) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);
            return tex;
        }

        // ================================================================
        // 材質
        // ================================================================

        /// <summary>MToon のテクスチャ欄 → 線形で読むか（Vrm10MToonTextureImporter.cs:121-149）。</summary>
        private static bool IsLinearMToonTexture(string prop)
            => prop == "_ShadingShiftTex" || prop == "_OutlineWidthTex" || prop == "_UvAnimMaskTex"
            || prop == "_BumpMap";

        private void BuildMaterials()
        {
            var mats = _data.GLTF.materials;
            if (!_settings.ImportMaterials || mats == null || mats.Count == 0)
                return;

            var used = new HashSet<string>();

            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                string name = MakeUnique(string.IsNullOrEmpty(m?.name) ? $"Material_{i}" : m.name, used);
                _materialNames.Add(name);

                var data = new MaterialData { Name = name, ShaderType = ShaderType.URPLit };
                var texProps = new List<(string prop, string path, bool linear)>();

                if (m != null)
                {
                    FillGltfMaterial(m, data, texProps);

                    if (MToonExt.GltfDeserializer.TryGet(m.extensions, out var mtoon))
                        FillMToon(m, mtoon, data, texProps);
                }

                var matRef = new MaterialReference(data);

                // 表示用の材質。テクスチャは本参照が所有する（MaterialReference.AttachRuntimeMaterial）。
                var mat = MaterialDataConverter.ToMaterial(data);
                if (mat != null)
                {
                    var owned = new List<Texture2D>();
                    foreach (var (prop, path, linear) in texProps)
                    {
                        if (!mat.HasProperty(prop)) continue;
                        var tex = CreateTexture(path, linear);
                        if (tex == null) continue;
                        mat.SetTexture(prop, tex);
                        owned.Add(tex);

                        // URP Lit の基本色は _BaseMap。_MainTex も持つシェーダーでは両方に付ける
                        // （ObjImporter.cs:582-586 と同じ）。
                        if (prop == "_BaseMap" && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                    }
                    matRef.AttachRuntimeMaterial(mat, owned);
                }

                _result.MaterialReferences.Add(matRef);
            }
        }

        /// <summary>glTF 標準の材質欄 → MaterialData（URP Lit）。</summary>
        private void FillGltfMaterial(glTFMaterial m, MaterialData data,
            List<(string prop, string path, bool linear)> texProps)
        {
            // 色は glTF が線形、Unity の材質色はガンマで持つ（UniGLTF の読み込みと同じ向き）。
            var pbr = m.pbrMetallicRoughness;
            var f = pbr?.baseColorFactor;
            if (f != null && f.Length >= 4)
                data.SetBaseColor(new Color(f[0], f[1], f[2], f[3]).gamma);

            if (pbr?.baseColorTexture != null)
            {
                string p = ResolveTexture(pbr.baseColorTexture.index);
                if (p != null)
                {
                    data.SourceTexturePath = p;
                    data.BaseMapPath       = p;
                    texProps.Add(("_BaseMap", p, false));
                }
            }

            if (pbr != null)
            {
                data.Metallic   = pbr.metallicFactor;
                data.Smoothness = 1f - pbr.roughnessFactor;
            }

            if (m.normalTexture != null)
            {
                string p = ResolveTexture(m.normalTexture.index);
                if (p != null)
                {
                    data.NormalMapPath     = p;
                    data.SourceBumpMapPath = p;
                    data.NormalScale       = m.normalTexture.scale;
                    texProps.Add(("_BumpMap", p, true));
                }
            }

            var e = m.emissiveFactor;
            bool hasEmissiveColor = e != null && e.Length >= 3 && (e[0] > 0f || e[1] > 0f || e[2] > 0f);
            if (hasEmissiveColor)
                data.SetEmissionColor(new Color(e[0], e[1], e[2], 1f).gamma);

            if (m.emissiveTexture != null)
            {
                string p = ResolveTexture(m.emissiveTexture.index);
                if (p != null)
                {
                    data.EmissionMapPath = p;
                    texProps.Add(("_EmissionMap", p, false));
                    hasEmissiveColor = true;
                }
            }
            data.EmissionEnabled = hasEmissiveColor;

            switch ((m.alphaMode ?? "OPAQUE").ToUpperInvariant())
            {
                case "BLEND":
                    data.Surface          = SurfaceType.Transparent;
                    data.AlphaClipEnabled = false;
                    break;
                case "MASK":
                    data.Surface          = SurfaceType.Opaque;
                    data.AlphaClipEnabled = true;
                    data.AlphaCutoff      = m.alphaCutoff;
                    break;
                default:
                    data.Surface          = SurfaceType.Opaque;
                    data.AlphaClipEnabled = false;
                    break;
            }

            data.CullMode = m.doubleSided ? CullModeType.Off : CullModeType.Back;
        }

        /// <summary>
        /// VRMC_materials_mtoon → MaterialData（ShaderType.MToon ＋ シェーダー固有プロパティ）。
        ///
        /// 【対応表は UniVRM のもの】
        ///   値の名前と中身は BuiltInVrm10MToonMaterialImporter.TryGetAll* が返すものを使う。
        ///   UniVRM が MToon10 材質を作るときと同じ対応表（UrpVrm10MToonMaterialImporter.cs:29-55）。
        ///
        /// 【型付きの欄へ振り分けるもの】
        ///   _Color / _MainTex / _BumpMap / _BumpScale / _EmissionColor / _EmissionMap / _Cutoff は
        ///   ApplyShaderProperties が読まない（MaterialDataConverter.cs:648-663, 743）ので、
        ///   MaterialData の型付きの欄へ入れる。
        /// </summary>
        private void FillMToon(glTFMaterial m, MToonExt.VRMC_materials_mtoon mtoon, MaterialData data,
            List<(string prop, string path, bool linear)> texProps)
        {
            data.ShaderType = ShaderType.MToon;
            data.ShaderName = "VRM10/Universal Render Pipeline/MToon10";

            foreach (var (key, value) in BuiltInVrm10MToonMaterialImporter.TryGetAllFloats(m, mtoon))
            {
                switch (key)
                {
                    case "_Cutoff":    data.AlphaCutoff = value; break;
                    case "_BumpScale": data.NormalScale = value; break;
                    default:
                        data.SetShaderProperty(new MaterialProperty(key, MaterialPropertyKind.Float) { X = value });
                        break;
                }
            }

            foreach (var (key, value) in BuiltInVrm10MToonMaterialImporter.TryGetAllColors(_data, m, mtoon))
            {
                switch (key)
                {
                    case "_Color":         data.SetBaseColor(value); break;
                    case "_EmissionColor":
                        data.SetEmissionColor(value);
                        if (value.r > 0f || value.g > 0f || value.b > 0f) data.EmissionEnabled = true;
                        break;
                    default:
                        data.SetShaderProperty(new MaterialProperty(key, MaterialPropertyKind.Color)
                            { X = value.r, Y = value.g, Z = value.b, W = value.a });
                        break;
                }
            }

            foreach (var (key, value) in BuiltInVrm10MToonMaterialImporter.TryGetAllFloatArrays(m, mtoon))
            {
                data.SetShaderProperty(new MaterialProperty(key, MaterialPropertyKind.Vector)
                    { X = value.x, Y = value.y, Z = value.z, W = value.w });
            }

            // テクスチャ。欄の名前と ST は UniVRM から取り、画像はここで glTF の索引から引く。
            // 基本色・法線・発光は FillGltfMaterial で入れてあるので、ST だけ上書きしない。
            texProps.RemoveAll(t => t.prop == "_BaseMap");
            if (!string.IsNullOrEmpty(data.SourceTexturePath))
                texProps.Add(("_MainTex", data.SourceTexturePath, false));

            foreach (var (key, (_, desc)) in Vrm10MToonTextureImporter.EnumerateAllTextures(_data, m, mtoon))
            {
                if (key == "_MainTex" || key == "_BumpMap" || key == "_EmissionMap") continue;

                int texIndex = MToonTextureIndex(key, mtoon);
                string p = texIndex >= 0 ? ResolveTexture(texIndex) : null;
                if (p == null) continue;

                data.SetShaderProperty(new MaterialProperty(key, MaterialPropertyKind.Texture)
                {
                    X = desc.Scale.x, Y = desc.Scale.y, Z = desc.Offset.x, W = desc.Offset.y,
                    TexturePath = p,
                });
                texProps.Add((key, p, IsLinearMToonTexture(key)));
            }
        }

        /// <summary>
        /// MToon 固有テクスチャの欄名 → glTF テクスチャ索引。
        /// 欄名は MToon10Properties.cs:18-45、元の欄は Vrm10MToonTextureImporter.cs:121-149。
        /// </summary>
        private static int MToonTextureIndex(string key, MToonExt.VRMC_materials_mtoon mtoon)
        {
            int? idx = null;
            switch (key)
            {
                case "_ShadeTex":        idx = mtoon.ShadeMultiplyTexture?.Index;        break;
                case "_ShadingShiftTex": idx = mtoon.ShadingShiftTexture?.Index;         break;
                case "_MatcapTex":       idx = mtoon.MatcapTexture?.Index;               break;
                case "_RimTex":          idx = mtoon.RimMultiplyTexture?.Index;          break;
                case "_OutlineWidthTex": idx = mtoon.OutlineWidthMultiplyTexture?.Index; break;
                case "_UvAnimMaskTex":   idx = mtoon.UvAnimationMaskTexture?.Index;      break;
            }
            return idx ?? -1;
        }

        /// <summary>材質の無いサブメッシュ用の既定材質を必要なときだけ足す。</summary>
        private int DefaultMaterialIndex()
        {
            if (_defaultMaterialIndex >= 0) return _defaultMaterialIndex;

            var used = new HashSet<string>(_materialNames);
            string name = MakeUnique("Default", used);
            var data = new MaterialData { Name = name, ShaderType = ShaderType.URPLit };
            _result.MaterialReferences.Add(new MaterialReference(data));
            _materialNames.Add(name);
            _defaultMaterialIndex = _result.MaterialReferences.Count - 1;
            return _defaultMaterialIndex;
        }

        private void FinalizeMaterials()
        {
            if (_result.MaterialReferences.Count == 0) DefaultMaterialIndex();
        }

        private static string MakeUnique(string baseName, HashSet<string> used)
        {
            if (used.Add(baseName)) return baseName;
            for (int n = 1; ; n++)
            {
                string c = $"{baseName}_{n}";
                if (used.Add(c)) return c;
            }
        }

        // ================================================================
        // ボーン
        // ================================================================

        private void BuildBones()
        {
            foreach (var node in _model.Root.Traverse())
            {
                if (node == _model.Root || !IsBoneNode(node)) continue;

                int ni = NodeIndexOf(node);
                if (ni < 0) continue;

                var parent = node.Parent;
                bool parentIsBone = !IsTopLevel(node) && IsBoneNode(parent);

                Matrix4x4 world = node.Matrix;
                Matrix4x4 local = parentIsBone ? parent.Matrix.inverse * world : world;
                Decompose(local, out var pos, out var rot, out var scale);

                int parentCtx = parentIsBone && _boneCtxByNode.TryGetValue(NodeIndexOf(parent), out int pc) ? pc : -1;

                string name = string.IsNullOrEmpty(node.Name) ? $"Node_{ni}" : node.Name;

                var mo = new MeshObject(name)
                {
                    Type = MeshType.Bone,
                    HierarchyParentIndex = parentCtx,
                };

                var bt = new BoneTransform
                {
                    Position          = pos,
                    Rotation          = rot.eulerAngles,
                    Scale             = scale,
                    UseLocalTransform = true,
                    HasBoneTransform  = true,
                };
                mo.BoneTransform = bt;

                var mc = new MeshContext
                {
                    MeshObject    = mo,
                    Name          = name,
                    Type          = MeshType.Bone,
                    IsVisible     = true,
                    BindPose      = world.inverse,
                    BoneTransform = bt,
                };
                mc.HierarchyParentIndex = parentCtx;
                mc.BonePoseData = new BonePoseData { IsActive = true };

                _boneCtxByNode[ni] = _result.MeshContexts.Count;
                _result.MeshContexts.Add(mc);
            }

            _result.BoneCount = _boneCtxByNode.Count;

            // Depth（ボーン木の深さ。ツリー表示用。HierarchyImportWindow.cs:464-479 と同じ）
            foreach (var kv in _boneCtxByNode)
            {
                var mc = Ctx(kv.Value);
                int depth = 0, cur = mc.HierarchyParentIndex, safety = 256;
                while (cur >= 0 && safety-- > 0) { depth++; cur = Ctx(cur)?.HierarchyParentIndex ?? -1; }
                mc.Depth = depth;
            }
        }

        // ================================================================
        // 描画オブジェクト
        // ================================================================

        private void BuildMeshes()
        {
            foreach (var node in _model.Root.Traverse())
            {
                if (node == _model.Root || node.MeshGroup == null) continue;

                int ni = NodeIndexOf(node);
                if (ni < 0) continue;

                var mg   = node.MeshGroup;
                var skin = mg.Skin;
                bool skinned = IsRealSkin(skin);

                string name = string.IsNullOrEmpty(node.Name) ? $"Mesh_{ni}" : node.Name;
                if (_boneCtxByNode.ContainsKey(ni)) name += "_mesh";

                var mo = new MeshObject(name) { IsTriangulated = true };

                Matrix4x4 bake = Matrix4x4.identity;
                int[] jointToCtx = null;
                if (skinned)
                {
                    bake = SkinBakeMatrix(skin);
                    jointToCtx = new int[skin.Joints.Count];
                    for (int j = 0; j < skin.Joints.Count; j++)
                        jointToCtx[j] = _boneCtxByNode.TryGetValue(NodeIndexOf(skin.Joints[j]), out int bi) ? bi : -1;
                }

                var targetDeltas = new List<Vector3[]>();
                var targetNames  = new List<string>();
                bool hasNormals  = true;

                if (!AppendMeshGroup(mg, mo, skinned, bake, jointToCtx, targetDeltas, targetNames, ref hasNormals))
                    continue;

                if (!hasNormals) mo.RecalculateSmoothNormals();

                var mc = new MeshContext { MeshObject = mo, Name = name, IsVisible = true };

                if (skinned)
                {
                    // ルート空間へベイク済み。親なし・変換なし。
                    mc.HierarchyParentIndex = -1;
                    mc.Depth = 0;
                }
                else
                {
                    ResolveStaticParent(node, ni, mc);
                }

                int ctxIndex = _result.MeshContexts.Count;
                _meshCtxByNode[ni] = ctxIndex;
                _result.MeshContexts.Add(mc);
                _result.MeshCount++;
                _result.VertexCount += mo.VertexCount;

                if (_settings.ImportMorphs)
                {
                    for (int t = 0; t < targetDeltas.Count; t++)
                    {
                        _pendingMorphs.Add(new PendingMorph
                        {
                            BaseCtx     = mc,
                            NodeIndex   = ni,
                            TargetIndex = t,
                            Name        = targetNames[t],
                            Deltas      = targetDeltas[t],
                        });
                    }
                }
            }
        }

        /// <summary>
        /// スキンのベイク行列（関節ワールド × 逆バインド）。休止姿勢ではどの関節でも同じ行列になるので、
        /// 最初の有効な関節で決める（HierarchyImportWindow.cs:714-727 と同じ考え方）。
        /// </summary>
        private static Matrix4x4 SkinBakeMatrix(VrmLib.Skin skin)
        {
            Unity.Collections.NativeArray<Matrix4x4> ibm = default;
            bool hasIbm = skin.InverseMatrices != null && skin.InverseMatrices.Count > 0;
            if (hasIbm) ibm = skin.InverseMatrices.GetSpan<Matrix4x4>();

            for (int j = 0; j < skin.Joints.Count; j++)
            {
                var joint = skin.Joints[j];
                if (joint == null) continue;
                var inv = (hasIbm && j < ibm.Length) ? ibm[j] : Matrix4x4.identity;
                return joint.Matrix * inv;
            }
            return Matrix4x4.identity;
        }

        /// <summary>
        /// MeshGroup の全 Mesh を 1 つの MeshObject へ連結する。
        ///
        /// 【サブメッシュの範囲】
        ///   Mesh が 1 つなら Submesh の Offset / DrawCount がその Mesh の索引バッファ内の範囲。
        ///   Mesh が複数（プリミティブごとに頂点バッファが別）のときは、各 Mesh に
        ///   Submesh が 1 つずつあり、Offset は全 Mesh を通した累計になっている
        ///   （MeshReader.cs:165-213）。この場合は各 Mesh の索引バッファ全体を使う。
        /// </summary>
        private bool AppendMeshGroup(
            VrmLib.MeshGroup mg, MeshObject mo, bool skinned, Matrix4x4 bake, int[] jointToCtx,
            List<Vector3[]> targetDeltas, List<string> targetNames, ref bool hasNormals)
        {
            int totalVertices = 0;
            foreach (var m in mg.Meshes) totalVertices += m?.VertexBuffer?.Count ?? 0;
            if (totalVertices == 0) return false;

            bool multi = mg.Meshes.Count > 1;
            bool warnedJoint = false;

            foreach (var m in mg.Meshes)
            {
                if (m == null || m.VertexBuffer == null) continue;

                if (m.Topology != VrmLib.TopologyType.Triangles)
                {
                    _result.Warnings.Add($"\"{mo.Name}\" に三角形以外のプリミティブがあるため、その部分を読み込みません。");
                    continue;
                }

                int vbase = mo.Vertices.Count;
                var vb    = m.VertexBuffer;
                int count = vb.Count;

                var positions = vb.Positions.GetSpan<Vector3>();

                Unity.Collections.NativeArray<Vector3> normals = default;
                bool hasN = vb.Normals != null && vb.Normals.ComponentType == AccessorValueType.FLOAT
                         && vb.Normals.AccessorType == AccessorVectorType.VEC3;
                if (hasN) normals = vb.Normals.GetSpan<Vector3>();
                else hasNormals = false;

                Unity.Collections.NativeArray<Vector2> uvs = default;
                bool hasUV = vb.TexCoords != null && vb.TexCoords.ComponentType == AccessorValueType.FLOAT
                          && vb.TexCoords.AccessorType == AccessorVectorType.VEC2;
                if (hasUV) uvs = vb.TexCoords.GetSpan<Vector2>();
                else if (vb.TexCoords != null)
                    _result.Warnings.Add($"\"{mo.Name}\" の UV は浮動小数以外の形式のため読み込みません。");

                Unity.Collections.NativeArray<SkinJoints> joints = default;
                Unity.Collections.NativeArray<Vector4>    weights = default;
                bool hasSkin = skinned && vb.Joints != null && vb.Weights != null;
                if (hasSkin)
                {
                    joints  = vb.Joints.GetAsSkinJointsArray();
                    weights = vb.Weights.GetAsVector4Array();
                }

                for (int i = 0; i < count; i++)
                {
                    Vector3 p = positions[i];
                    Vector3 n = hasN ? normals[i] : Vector3.up;
                    Vector2 uv = hasUV ? uvs[i] : Vector2.zero;

                    if (skinned)
                    {
                        p = bake.MultiplyPoint3x4(p);
                        n = bake.MultiplyVector(n).normalized;
                    }

                    var v = new Vertex(p, uv, n);

                    if (hasSkin)
                    {
                        var jw = joints[i];
                        var w  = weights[i];
                        v.BoneWeight = new BoneWeight
                        {
                            boneIndex0 = MapJoint(jw.Joint0, w.x, jointToCtx, mo.Name, ref warnedJoint),
                            boneIndex1 = MapJoint(jw.Joint1, w.y, jointToCtx, mo.Name, ref warnedJoint),
                            boneIndex2 = MapJoint(jw.Joint2, w.z, jointToCtx, mo.Name, ref warnedJoint),
                            boneIndex3 = MapJoint(jw.Joint3, w.w, jointToCtx, mo.Name, ref warnedJoint),
                            weight0 = w.x, weight1 = w.y, weight2 = w.z, weight3 = w.w,
                        };
                    }

                    mo.Vertices.Add(v);
                }

                // 面
                var indices = m.IndexBuffer.GetAsIntArray();
                if (multi || m.Submeshes.Count == 0)
                {
                    int matIndex = (m.Submeshes.Count > 0) ? MaterialIndexOf(m.Submeshes[0].Material) : MaterialIndexOf(null);
                    AppendTriangles(mo, indices, 0, indices.Length, vbase, matIndex);
                }
                else
                {
                    foreach (var sm in m.Submeshes)
                        AppendTriangles(mo, indices, sm.Offset, sm.DrawCount, vbase, MaterialIndexOf(sm.Material));
                }

                // モーフターゲット（全 Mesh で同じ並びを持つ：glTF の規約）
                for (int t = 0; t < m.MorphTargets.Count; t++)
                {
                    var target = m.MorphTargets[t];
                    while (targetDeltas.Count <= t)
                    {
                        targetDeltas.Add(new Vector3[totalVertices]);
                        targetNames.Add(null);
                    }
                    if (string.IsNullOrEmpty(targetNames[t]))
                        targetNames[t] = string.IsNullOrEmpty(target?.Name) ? $"Morph_{t}" : target.Name;

                    var tp = target?.VertexBuffer?.Positions;
                    if (tp == null) continue;
                    var deltas = tp.GetSpan<Vector3>();
                    var dst = targetDeltas[t];
                    for (int i = 0; i < count && i < deltas.Length; i++)
                    {
                        Vector3 d = deltas[i];
                        dst[vbase + i] = skinned ? bake.MultiplyVector(d) : d;
                    }
                }
            }

            return mo.Vertices.Count > 0;
        }

        private static int MapJoint(int joint, float weight, int[] jointToCtx, string meshName, ref bool warned)
        {
            if (jointToCtx != null && joint >= 0 && joint < jointToCtx.Length && jointToCtx[joint] >= 0)
                return jointToCtx[joint];
            if (weight > 0f && !warned)
            {
                warned = true;
                Debug.LogWarning($"[Vrm10Importer] \"{meshName}\" のウェイトがボーンに対応しない関節を指しています。先頭ボーンに付け替えます。");
            }
            return 0;
        }

        private void AppendTriangles(MeshObject mo, Unity.Collections.NativeArray<int> indices,
            int offset, int drawCount, int vbase, int materialIndex)
        {
            int end = Mathf.Min(indices.Length, offset + drawCount);
            for (int t = offset; t + 2 < end; t += 3)
            {
                var face = new Face { MaterialIndex = materialIndex };
                face.VertexIndices.Add(vbase + indices[t]);
                face.VertexIndices.Add(vbase + indices[t + 1]);
                face.VertexIndices.Add(vbase + indices[t + 2]);
                for (int k = 0; k < 3; k++)
                {
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }
                mo.Faces.Add(face);
            }
        }

        private int MaterialIndexOf(int? gltfMaterial)
        {
            if (gltfMaterial.HasValue && gltfMaterial.Value >= 0 && gltfMaterial.Value < _materialNames.Count
                && _settings.ImportMaterials)
                return gltfMaterial.Value;
            return DefaultMaterialIndex();
        }

        /// <summary>
        /// 非スキンの描画オブジェクト：ノードのローカル変換と親を決める。
        /// 親の Depth は描画オブジェクトの親子だけで数える（ボーンは Depth の親子に入らない：
        /// MeshHierarchyOps.cs:102-130）。
        /// </summary>
        private void ResolveStaticParent(VrmLib.Node node, int ni, MeshContext mc)
        {
            int parentCtx = -1;
            Matrix4x4 local;
            int depth = 0;

            if (_boneCtxByNode.TryGetValue(ni, out int selfBone))
            {
                // 自分自身がボーン（関節）でもあるノード：メッシュはそのボーンの子に置く。
                parentCtx = selfBone;
                local = Matrix4x4.identity;
            }
            else if (IsTopLevel(node))
            {
                local = node.Matrix;
            }
            else
            {
                var parent = node.Parent;
                int pni = NodeIndexOf(parent);
                local = parent.Matrix.inverse * node.Matrix;

                if (_meshCtxByNode.TryGetValue(pni, out int pm) && !_boneCtxByNode.ContainsKey(pni))
                {
                    parentCtx = pm;
                    depth = (Ctx(pm)?.Depth ?? 0) + 1;
                }
                else if (_boneCtxByNode.TryGetValue(pni, out int pb))
                {
                    parentCtx = pb;
                }
            }

            Decompose(local, out var pos, out var rot, out var scale);
            mc.BoneTransform.Position = pos;
            mc.BoneTransform.Rotation = rot.eulerAngles;
            mc.BoneTransform.Scale    = scale;
            mc.BoneTransform.UseLocalTransform = !IsIdentity(local);

            mc.HierarchyParentIndex = parentCtx;
            mc.Depth = depth;
        }

        // ================================================================
        // モーフ
        // ================================================================

        private void BuildMorphContexts()
        {
            var usedNames = new HashSet<string>();
            foreach (var mc in _result.MeshContexts)
                if (!string.IsNullOrEmpty(mc?.Name)) usedNames.Add(mc.Name);

            foreach (var pm in _pendingMorphs)
            {
                var baseMc = pm.BaseCtx;
                var morphMc = new MeshContext
                {
                    MeshObject        = baseMc.MeshObject.Clone(),
                    Type              = MeshType.Morph,
                    IsVisible         = false,
                    ExcludeFromExport = true,
                };
                morphMc.MeshObject.Type = MeshType.Morph;

                string name = MakeUnique($"{baseMc.Name}_{pm.Name}", usedNames);
                morphMc.MeshObject.Name = name;
                morphMc.Name = name;

                // 基準位置は差分を足す前に取る（PMXImporter.Morph.cs:303-319 と同じ順）。
                morphMc.SetAsMorph(pm.Name);
                if (morphMc.MorphBaseData != null)
                    morphMc.MorphBaseData.BaseMeshName = baseMc.Name;

                var verts = morphMc.MeshObject.Vertices;
                for (int i = 0; i < verts.Count && i < pm.Deltas.Length; i++)
                    verts[i].Position += pm.Deltas[i];

                int index = _result.MeshContexts.Count;
                _result.MeshContexts.Add(morphMc);
                _morphCtx[(pm.NodeIndex, pm.TargetIndex)]  = index;
                _morphName[(pm.NodeIndex, pm.TargetIndex)] = pm.Name;
                _result.MorphCount++;
            }
        }

        private void FinalizeUnityMeshes()
        {
            int matCount = Mathf.Max(1, _result.MaterialReferences.Count);
            foreach (var mc in _result.MeshContexts)
            {
                if (mc?.MeshObject == null || mc.Type == MeshType.Bone) continue;

                if (mc.Type == MeshType.Morph)
                {
                    mc.UnityMesh = mc.MeshObject.ToUnityMeshShared(matCount);
                    mc.UnityMesh.name = mc.MeshObject.Name;
                    mc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                }
                else
                {
                    mc.UnityMesh = mc.MeshObject.ToUnityMesh(matCount);
                    mc.UnityMesh.name = mc.Name;
                }
            }
        }

        // ================================================================
        // Humanoid
        // ================================================================

        private void BuildHumanoid()
        {
            var hb = _vrm.VrmExtension?.Humanoid?.HumanBones;
            if (hb == null) return;

            // VRM → Unity の対応は Vrm10Importer.cs:106-160 と同じ（親指は段がずれる）。
            var pairs = new (VrmExt.HumanBone bone, HumanBodyBones unity)[]
            {
                (hb.Hips, HumanBodyBones.Hips), (hb.Spine, HumanBodyBones.Spine),
                (hb.Chest, HumanBodyBones.Chest), (hb.UpperChest, HumanBodyBones.UpperChest),
                (hb.Neck, HumanBodyBones.Neck), (hb.Head, HumanBodyBones.Head),
                (hb.LeftEye, HumanBodyBones.LeftEye), (hb.RightEye, HumanBodyBones.RightEye),
                (hb.Jaw, HumanBodyBones.Jaw),
                (hb.LeftUpperLeg, HumanBodyBones.LeftUpperLeg), (hb.LeftLowerLeg, HumanBodyBones.LeftLowerLeg),
                (hb.LeftFoot, HumanBodyBones.LeftFoot), (hb.LeftToes, HumanBodyBones.LeftToes),
                (hb.RightUpperLeg, HumanBodyBones.RightUpperLeg), (hb.RightLowerLeg, HumanBodyBones.RightLowerLeg),
                (hb.RightFoot, HumanBodyBones.RightFoot), (hb.RightToes, HumanBodyBones.RightToes),
                (hb.LeftShoulder, HumanBodyBones.LeftShoulder), (hb.LeftUpperArm, HumanBodyBones.LeftUpperArm),
                (hb.LeftLowerArm, HumanBodyBones.LeftLowerArm), (hb.LeftHand, HumanBodyBones.LeftHand),
                (hb.RightShoulder, HumanBodyBones.RightShoulder), (hb.RightUpperArm, HumanBodyBones.RightUpperArm),
                (hb.RightLowerArm, HumanBodyBones.RightLowerArm), (hb.RightHand, HumanBodyBones.RightHand),
                (hb.LeftThumbMetacarpal, HumanBodyBones.LeftThumbProximal),
                (hb.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate),
                (hb.LeftThumbDistal, HumanBodyBones.LeftThumbDistal),
                (hb.LeftIndexProximal, HumanBodyBones.LeftIndexProximal),
                (hb.LeftIndexIntermediate, HumanBodyBones.LeftIndexIntermediate),
                (hb.LeftIndexDistal, HumanBodyBones.LeftIndexDistal),
                (hb.LeftMiddleProximal, HumanBodyBones.LeftMiddleProximal),
                (hb.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleIntermediate),
                (hb.LeftMiddleDistal, HumanBodyBones.LeftMiddleDistal),
                (hb.LeftRingProximal, HumanBodyBones.LeftRingProximal),
                (hb.LeftRingIntermediate, HumanBodyBones.LeftRingIntermediate),
                (hb.LeftRingDistal, HumanBodyBones.LeftRingDistal),
                (hb.LeftLittleProximal, HumanBodyBones.LeftLittleProximal),
                (hb.LeftLittleIntermediate, HumanBodyBones.LeftLittleIntermediate),
                (hb.LeftLittleDistal, HumanBodyBones.LeftLittleDistal),
                (hb.RightThumbMetacarpal, HumanBodyBones.RightThumbProximal),
                (hb.RightThumbProximal, HumanBodyBones.RightThumbIntermediate),
                (hb.RightThumbDistal, HumanBodyBones.RightThumbDistal),
                (hb.RightIndexProximal, HumanBodyBones.RightIndexProximal),
                (hb.RightIndexIntermediate, HumanBodyBones.RightIndexIntermediate),
                (hb.RightIndexDistal, HumanBodyBones.RightIndexDistal),
                (hb.RightMiddleProximal, HumanBodyBones.RightMiddleProximal),
                (hb.RightMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate),
                (hb.RightMiddleDistal, HumanBodyBones.RightMiddleDistal),
                (hb.RightRingProximal, HumanBodyBones.RightRingProximal),
                (hb.RightRingIntermediate, HumanBodyBones.RightRingIntermediate),
                (hb.RightRingDistal, HumanBodyBones.RightRingDistal),
                (hb.RightLittleProximal, HumanBodyBones.RightLittleProximal),
                (hb.RightLittleIntermediate, HumanBodyBones.RightLittleIntermediate),
                (hb.RightLittleDistal, HumanBodyBones.RightLittleDistal),
            };

            foreach (var (bone, unity) in pairs)
            {
                if (bone?.Node == null) continue;
                int ni = bone.Node.Value;
                if (!_boneCtxByNode.TryGetValue(ni, out int ci))
                {
                    _result.Warnings.Add($"Humanoid の {unity} が指すノード {ni} がボーンになっていません。");
                    continue;
                }

                // 割当表のキーは HumanTrait.BoneName 形式（HumanoidBoneMapping.cs:51, 74）。
                Ctx(ci).MeshObject.HumanBodyBone = HumanTrait.BoneName[(int)unity];
                _result.HumanoidBoneCount++;
            }
        }

        // ================================================================
        // Meta / 視線 / 一人称
        // ================================================================

        private void BuildMeta()
        {
            var src = _vrm.VrmExtension?.Meta;
            if (src == null) return;

            var meta = new VrmMetaData
            {
                Name                 = src.Name ?? "",
                Version              = src.Version ?? "",
                CopyrightInformation = src.CopyrightInformation ?? "",
                ContactInformation   = src.ContactInformation ?? "",
                ThirdPartyLicenses   = src.ThirdPartyLicenses ?? "",
                OtherLicenseUrl      = src.OtherLicenseUrl ?? "",

                ViolentUsage              = src.AllowExcessivelyViolentUsage.GetValueOrDefault(),
                SexualUsage               = src.AllowExcessivelySexualUsage.GetValueOrDefault(),
                PoliticalOrReligiousUsage = src.AllowPoliticalOrReligiousUsage.GetValueOrDefault(),
                AntisocialOrHateUsage     = src.AllowAntisocialOrHateUsage.GetValueOrDefault(),
                Redistribution            = src.AllowRedistribution.GetValueOrDefault(),
            };

            if (src.Authors != null) meta.Authors.AddRange(src.Authors);
            if (src.References != null) meta.References.AddRange(src.References);

            switch (src.AvatarPermission)
            {
                case VrmExt.AvatarPermissionType.onlySeparatelyLicensedPerson:
                    meta.AvatarPermission = VrmAvatarPermission.OnlySeparatelyLicensedPerson; break;
                case VrmExt.AvatarPermissionType.everyone:
                    meta.AvatarPermission = VrmAvatarPermission.Everyone; break;
                default:
                    meta.AvatarPermission = VrmAvatarPermission.OnlyAuthor; break;
            }

            switch (src.CommercialUsage)
            {
                case VrmExt.CommercialUsageType.personalProfit:
                    meta.CommercialUsage = VrmCommercialUsage.PersonalProfit; break;
                case VrmExt.CommercialUsageType.corporation:
                    meta.CommercialUsage = VrmCommercialUsage.Corporation; break;
                default:
                    meta.CommercialUsage = VrmCommercialUsage.PersonalNonProfit; break;
            }

            meta.CreditNotation = (src.CreditNotation == VrmExt.CreditNotationType.unnecessary)
                ? VrmCreditNotation.Unnecessary : VrmCreditNotation.Required;

            switch (src.Modification)
            {
                case VrmExt.ModificationType.allowModification:
                    meta.Modification = VrmModification.AllowModification; break;
                case VrmExt.ModificationType.allowModificationRedistribution:
                    meta.Modification = VrmModification.AllowModificationRedistribution; break;
                default:
                    meta.Modification = VrmModification.Prohibited; break;
            }

            // サムネイル（glTF image 索引）。実体は持たずパスだけ持つ（VrmMetaData.cs 冒頭）。
            if (src.ThumbnailImage.HasValue && _settings.ExtractTextures)
            {
                int image = src.ThumbnailImage.Value;
                string path = ResolveImage(image);
                if (path != null) meta.ThumbnailPath = path;
            }

            _result.VrmMeta = meta;
        }

        /// <summary>glTF image 索引で直接書き出す（サムネイル用）。</summary>
        private string ResolveImage(int image)
        {
            var textures = _data.GLTF.textures;
            if (textures != null)
                for (int t = 0; t < textures.Count; t++)
                    if (textures[t]?.source == image) return ResolveTexture(t);

            // どのテクスチャからも参照されない画像：画像を直接書き出す
            var images = _data.GLTF.images;
            if (images == null || image < 0 || image >= images.Count) return null;
            if (_imagePath.TryGetValue(image, out var cached)) return cached;

            var got = _data.GetBytesFromImage(image);
            if (!got.HasValue) return null;
            string mime = got.Value.mimeType ?? "";
            string ext = mime == "image/png" ? ".png" : mime == "image/jpeg" ? ".jpg" : null;
            if (ext == null) return null;

            byte[] bytes = got.Value.binary.ToArray();
            string baseName = string.IsNullOrEmpty(images[image]?.name) ? "image" : images[image].name;
            foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');

            Directory.CreateDirectory(_textureFolder);
            string path = Path.Combine(_textureFolder, $"{image:D3}_{baseName}{ext}");
            File.WriteAllBytes(path, bytes);
            _imagePath[image]  = path;
            _imageBytes[image] = bytes;
            return path;
        }

        private void BuildLookAt()
        {
            var src = _vrm.VrmExtension?.LookAt;
            if (src == null) return;

            var dst = new VrmLookAtData
            {
                LookAtType = (src.Type == VrmExt.LookAtType.expression) ? VrmLookAtType.Expression : VrmLookAtType.Bone,
            };

            // 右手系 → 左手系（Vrm10Importer.cs:405-408 と同じ X 反転）
            if (src.OffsetFromHeadBone != null && src.OffsetFromHeadBone.Length >= 3)
                dst.OffsetFromHead = InvertX(src.OffsetFromHeadBone, dst.OffsetFromHead);

            dst.HorizontalInner = ToRangeMap(src.RangeMapHorizontalInner, dst.HorizontalInner);
            dst.HorizontalOuter = ToRangeMap(src.RangeMapHorizontalOuter, dst.HorizontalOuter);
            dst.VerticalDown    = ToRangeMap(src.RangeMapVerticalDown,    dst.VerticalDown);
            dst.VerticalUp      = ToRangeMap(src.RangeMapVerticalUp,      dst.VerticalUp);

            _result.VrmLookAt = dst;
        }

        private static VrmLookAtRangeMap ToRangeMap(VrmExt.LookAtRangeMap src, VrmLookAtRangeMap fallback)
        {
            if (src == null) return fallback;
            return new VrmLookAtRangeMap(
                src.InputMaxValue ?? fallback.InputMaxDegrees,
                src.OutputScale   ?? fallback.OutputScale);
        }

        private void BuildFirstPerson()
        {
            var anns = _vrm.VrmExtension?.FirstPerson?.MeshAnnotations;
            if (anns == null) return;

            foreach (var a in anns)
            {
                if (a?.Node == null) continue;
                int ci = RendererCtxOfNode(a.Node.Value);
                var mo = Ctx(ci)?.MeshObject;
                if (mo == null) continue;

                switch (a.Type)
                {
                    case VrmExt.FirstPersonType.both:            mo.VrmFirstPerson = VrmFirstPersonType.Both;            break;
                    case VrmExt.FirstPersonType.thirdPersonOnly: mo.VrmFirstPerson = VrmFirstPersonType.ThirdPersonOnly; break;
                    case VrmExt.FirstPersonType.firstPersonOnly: mo.VrmFirstPerson = VrmFirstPersonType.FirstPersonOnly; break;
                    default:                                     mo.VrmFirstPerson = VrmFirstPersonType.Auto;            break;
                }
            }
        }

        // ================================================================
        // 表情
        // ================================================================

        private void BuildExpressions(bool onlyTargets = false)
        {
            // 同名のモーフターゲットを 1 つの仮表情にまとめる
            var tempByName = new Dictionary<string, MorphExpression>();
            var tempOrder  = new List<MorphExpression>();
            foreach (var kv in _morphCtx)
            {
                string tname = _morphName[kv.Key];
                if (!tempByName.TryGetValue(tname, out var me))
                {
                    me = new MorphExpression(tname, MorphType.Vertex);
                    tempByName[tname] = me;
                    tempOrder.Add(me);
                }
                me.AddMesh(kv.Value, 1f);
            }

            var absorbed = new HashSet<MorphExpression>();
            var groups   = new List<MorphExpression>();

            if (!onlyTargets)
            {
                var ex = _vrm.VrmExtension?.Expressions;
                var preset = ex?.Preset;
                if (preset != null)
                {
                    AddExpression(groups, absorbed, tempByName, "happy",      preset.Happy,      3);
                    AddExpression(groups, absorbed, tempByName, "angry",      preset.Angry,      3);
                    AddExpression(groups, absorbed, tempByName, "sad",        preset.Sad,        3);
                    AddExpression(groups, absorbed, tempByName, "relaxed",    preset.Relaxed,    3);
                    AddExpression(groups, absorbed, tempByName, "surprised",  preset.Surprised,  3);
                    AddExpression(groups, absorbed, tempByName, "aa",         preset.Aa,         2);
                    AddExpression(groups, absorbed, tempByName, "ih",         preset.Ih,         2);
                    AddExpression(groups, absorbed, tempByName, "ou",         preset.Ou,         2);
                    AddExpression(groups, absorbed, tempByName, "ee",         preset.Ee,         2);
                    AddExpression(groups, absorbed, tempByName, "oh",         preset.Oh,         2);
                    AddExpression(groups, absorbed, tempByName, "blink",      preset.Blink,      1);
                    AddExpression(groups, absorbed, tempByName, "blinkLeft",  preset.BlinkLeft,  1);
                    AddExpression(groups, absorbed, tempByName, "blinkRight", preset.BlinkRight, 1);
                    AddExpression(groups, absorbed, tempByName, "lookUp",     preset.LookUp,     1);
                    AddExpression(groups, absorbed, tempByName, "lookDown",   preset.LookDown,   1);
                    AddExpression(groups, absorbed, tempByName, "lookLeft",   preset.LookLeft,   1);
                    AddExpression(groups, absorbed, tempByName, "lookRight",  preset.LookRight,  1);
                    AddExpression(groups, absorbed, tempByName, "neutral",    preset.Neutral,    3);
                }

                if (ex?.Custom != null)
                    foreach (var kv in ex.Custom)
                        AddExpression(groups, absorbed, tempByName, kv.Key, kv.Value, 3, custom: true);
            }

            foreach (var me in tempOrder)
                if (!absorbed.Contains(me)) _result.MorphExpressions.Add(me);
            _result.MorphExpressions.AddRange(groups);

            _result.ExpressionCount = groups.Count;
        }

        private void AddExpression(
            List<MorphExpression> groups, HashSet<MorphExpression> absorbed,
            Dictionary<string, MorphExpression> tempByName,
            string name, VrmExt.Expression src, int panel, bool custom = false)
        {
            if (src == null || string.IsNullOrEmpty(name)) return;

            var me = new MorphExpression(name, MorphType.Group)
            {
                NameEnglish = custom ? "" : name,
                Panel       = panel,
            };

            if (src.MorphTargetBinds != null)
            {
                foreach (var b in src.MorphTargetBinds)
                {
                    if (b?.Node == null || b.Index == null) continue;
                    var key = (b.Node.Value, b.Index.Value);
                    if (!_morphCtx.TryGetValue(key, out int morphIndex))
                    {
                        _result.Warnings.Add($"表情 \"{name}\" のバインド（ノード {key.Item1} / ターゲット {key.Item2}）に対応するモーフがありません。");
                        continue;
                    }

                    float w = b.Weight ?? 1f;
                    string tname = _morphName[key];
                    me.AddMesh(morphIndex, w);

                    if (!me.GroupChildren.Exists(c => c.Name == tname))
                        me.GroupChildren.Add(new MorphGroupChild(tname, w));

                    if (tempByName.TryGetValue(tname, out var temp)) absorbed.Add(temp);
                }
            }

            var vrm = new VrmExpressionData
            {
                IsBinary       = src.IsBinary.GetValueOrDefault(),
                OverrideBlink  = ToOverride(src.OverrideBlink),
                OverrideLookAt = ToOverride(src.OverrideLookAt),
                OverrideMouth  = ToOverride(src.OverrideMouth),
            };

            if (src.MaterialColorBinds != null)
            {
                foreach (var b in src.MaterialColorBinds)
                {
                    string mat = MaterialNameOf(b?.Material);
                    if (mat == null || b.TargetValue == null || b.TargetValue.Length < 4) continue;
                    vrm.MaterialColorBinds.Add(new VrmMaterialColorBind
                    {
                        MaterialName = mat,
                        BindType     = ToColorType(b.Type),
                        TargetValue  = new Vector4(b.TargetValue[0], b.TargetValue[1], b.TargetValue[2], b.TargetValue[3]),
                    });
                }
            }

            if (src.TextureTransformBinds != null)
            {
                foreach (var b in src.TextureTransformBinds)
                {
                    string mat = MaterialNameOf(b?.Material);
                    if (mat == null) continue;
                    var s = (b.Scale  != null && b.Scale.Length  >= 2) ? new Vector2(b.Scale[0],  b.Scale[1])  : Vector2.one;
                    var o = (b.Offset != null && b.Offset.Length >= 2) ? new Vector2(b.Offset[0], b.Offset[1]) : Vector2.zero;
                    // glTF → Unity（ExpressionExtensions.cs:62 と同じ変換）
                    var (us, uo) = TextureTransform.VerticalFlipScaleOffset(s, o);
                    vrm.TextureTransformBinds.Add(new VrmTextureTransformBind
                    {
                        MaterialName = mat,
                        Scaling      = us,
                        Offset       = uo,
                    });
                }
            }

            bool hasVrmValues = vrm.IsBinary || vrm.HasMaterialBinds
                || vrm.OverrideBlink  != VrmExpressionOverride.None
                || vrm.OverrideLookAt != VrmExpressionOverride.None
                || vrm.OverrideMouth  != VrmExpressionOverride.None;
            if (hasVrmValues) me.Vrm = vrm;

            // モーフも材質バインドも無い表情は書き出しでも出ない（Vrm10SceneAssembler.BuildExpressions）。
            if (me.MeshEntries.Count == 0 && !vrm.HasMaterialBinds) return;

            groups.Add(me);
        }

        private string MaterialNameOf(int? gltfMaterial)
        {
            if (!gltfMaterial.HasValue) return null;
            int i = gltfMaterial.Value;
            return (i >= 0 && i < _materialNames.Count) ? _materialNames[i] : null;
        }

        private static VrmExpressionOverride ToOverride(VrmExt.ExpressionOverrideType t)
        {
            switch (t)
            {
                case VrmExt.ExpressionOverrideType.block: return VrmExpressionOverride.Block;
                case VrmExt.ExpressionOverrideType.blend: return VrmExpressionOverride.Blend;
                default:                                  return VrmExpressionOverride.None;
            }
        }

        private static VrmMaterialColorType ToColorType(VrmExt.MaterialColorType t)
        {
            switch (t)
            {
                case VrmExt.MaterialColorType.emissionColor: return VrmMaterialColorType.EmissionColor;
                case VrmExt.MaterialColorType.shadeColor:    return VrmMaterialColorType.ShadeColor;
                case VrmExt.MaterialColorType.matcapColor:   return VrmMaterialColorType.MatcapColor;
                case VrmExt.MaterialColorType.rimColor:      return VrmMaterialColorType.RimColor;
                case VrmExt.MaterialColorType.outlineColor:  return VrmMaterialColorType.OutlineColor;
                default:                                     return VrmMaterialColorType.Color;
            }
        }

        // ================================================================
        // 揺れ（VRMC_springBone）
        // ================================================================

        private void BuildSpringBones()
        {
            if (!SpringExt.GltfDeserializer.TryGet(_data.GLTF.extensions, out SpringExt.VRMC_springBone sb) || sb == null)
                return;

            // ---- コライダー ------------------------------------------------
            var colliders = new List<SpringBoneColliderData>();
            if (sb.Colliders != null)
            {
                foreach (var c in sb.Colliders)
                {
                    SpringBoneColliderData data = null;
                    int ci = (c?.Node != null) ? CtxOfNode(c.Node.Value) : -1;

                    if (c != null && ci >= 0)
                    {
                        data = ToCollider(c);
                        if (data != null)
                        {
                            var mo = Ctx(ci).MeshObject;
                            if (mo.SpringBoneColliders == null) mo.SpringBoneColliders = new List<SpringBoneColliderData>();
                            mo.SpringBoneColliders.Add(data);
                            _result.ColliderCount++;
                        }
                    }
                    else
                    {
                        _result.Warnings.Add("揺れのコライダーが対応するノードを持たないため読み込みません。");
                    }

                    // 索引を保つため、読めなかったものも null で並びに入れる
                    colliders.Add(data);
                }
            }

            // ---- コライダーグループ ------------------------------------------
            if (sb.ColliderGroups != null)
            {
                for (int g = 0; g < sb.ColliderGroups.Count; g++)
                {
                    var grp = sb.ColliderGroups[g];
                    string gname = string.IsNullOrEmpty(grp?.Name) ? $"ColliderGroup_{g}" : grp.Name;
                    _result.SpringBoneColliderGroupNames.Add(gname);

                    if (grp?.Colliders == null) continue;
                    foreach (int c in grp.Colliders)
                    {
                        if (c < 0 || c >= colliders.Count || colliders[c] == null) continue;
                        if (!colliders[c].SpringBoneGroupIndices.Contains(g))
                            colliders[c].SpringBoneGroupIndices.Add(g);
                    }
                }
            }

            // ---- チェーン --------------------------------------------------
            if (sb.Springs == null) return;

            int groupCount = _result.SpringBoneColliderGroupNames.Count;

            foreach (var spring in sb.Springs)
            {
                if (spring?.Joints == null || spring.Joints.Count == 0) continue;

                MeshObject first = null;
                MeshObject prev  = null;
                int prevCtx = -1;

                foreach (var j in spring.Joints)
                {
                    if (j?.Node == null) continue;
                    int ci = CtxOfNode(j.Node.Value);
                    var mo = Ctx(ci)?.MeshObject;
                    if (mo == null) continue;

                    if (mo.SpringBoneJoint != null)
                    {
                        // 仕様違反（1 ノードが複数の揺れに属する）。UniVRM も飛ばす（Vrm10Importer.cs:617-623）。
                        _result.Warnings.Add($"\"{mo.Name}\" は複数の揺れに属しているため、2 つ目以降を読み込みません。");
                        continue;
                    }

                    mo.SpringBoneJoint = ToJoint(j);

                    if (prev != null && Ctx(ci)?.HierarchyParentIndex != prevCtx)
                    {
                        _result.Warnings.Add(
                            $"揺れ \"{spring.Name}\" の \"{mo.Name}\" は直前のジョイント \"{prev.Name}\" の子ではありません。"
                          + "PolyLing のチェーンは階層から導くため、書き出しで形が変わります。");
                    }

                    if (first == null) first = mo;
                    prev = mo;
                    prevCtx = ci;
                }

                if (first == null) continue;

                var chain = new SpringBoneChainData
                {
                    Name = spring.Name ?? "",
                };
                if (spring.Center.HasValue)
                {
                    var cmc = Ctx(CtxOfNode(spring.Center.Value));
                    if (cmc != null) chain.CenterBoneName = cmc.Name;
                }
                if (spring.ColliderGroups != null)
                    foreach (int g in spring.ColliderGroups)
                        if (g >= 0 && g < groupCount) chain.SpringBoneColliderGroupIndices.Add(g);

                first.SpringBoneChainRoot = chain;
                _result.SpringCount++;
            }
        }

        /// <summary>コライダー 1 件（Vrm10Importer.cs:483-538 と同じ解釈・同じ X 反転）。</summary>
        private static SpringBoneColliderData ToCollider(SpringExt.Collider c)
        {
            SpringBoneColliderData d = null;

            if (c.Shape?.Capsule is SpringExt.ColliderShapeCapsule cap)
            {
                d = new SpringBoneColliderData
                {
                    Shape  = SpringBoneColliderShape.Capsule,
                    Offset = InvertX(cap.Offset, Vector3.zero),
                    Tail   = InvertX(cap.Tail, Vector3.zero),
                    Radius = cap.Radius ?? 0f,
                };
            }
            else if (c.Shape?.Sphere is SpringExt.ColliderShapeSphere sph)
            {
                d = new SpringBoneColliderData
                {
                    Shape  = SpringBoneColliderShape.Sphere,
                    Offset = InvertX(sph.Offset, Vector3.zero),
                    Radius = sph.Radius ?? 0f,
                };
            }

            if (UniGLTF.Extensions.VRMC_springBone_extended_collider.GltfDeserializer.TryGet(
                    c.Extensions as glTFExtension, out var ex) && ex?.Shape != null)
            {
                if (ex.Shape.Sphere is UniGLTF.Extensions.VRMC_springBone_extended_collider.ExtendedColliderShapeSphere es)
                {
                    d = new SpringBoneColliderData
                    {
                        Shape  = es.Inside.GetValueOrDefault() ? SpringBoneColliderShape.InsideSphere : SpringBoneColliderShape.Sphere,
                        Offset = InvertX(es.Offset, Vector3.zero),
                        Radius = es.Radius ?? 0f,
                    };
                }
                else if (ex.Shape.Capsule is UniGLTF.Extensions.VRMC_springBone_extended_collider.ExtendedColliderShapeCapsule ec)
                {
                    d = new SpringBoneColliderData
                    {
                        Shape  = ec.Inside.GetValueOrDefault() ? SpringBoneColliderShape.InsideCapsule : SpringBoneColliderShape.Capsule,
                        Offset = InvertX(ec.Offset, Vector3.zero),
                        Tail   = InvertX(ec.Tail, Vector3.zero),
                        Radius = ec.Radius ?? 0f,
                    };
                }
                else if (ex.Shape.Plane is UniGLTF.Extensions.VRMC_springBone_extended_collider.ExtendedColliderShapePlane ep)
                {
                    d = new SpringBoneColliderData
                    {
                        Shape  = SpringBoneColliderShape.Plane,
                        Offset = InvertX(ep.Offset, Vector3.zero),
                        Normal = InvertX(ep.Normal, Vector3.up),
                    };
                }
            }

            return d;
        }

        /// <summary>ジョイント 1 件（既定値は Vrm10Importer.cs:626-630 と同じ）。</summary>
        private static SpringBoneJointData ToJoint(SpringExt.SpringBoneJoint j)
        {
            var d = new SpringBoneJointData
            {
                HitRadius      = j.HitRadius.GetValueOrDefault(0f),
                DragForce      = j.DragForce.GetValueOrDefault(0.5f),
                GravityDir     = (j.GravityDir != null) ? InvertX(j.GravityDir, Vector3.down) : Vector3.down,
                GravityPower   = j.GravityPower.GetValueOrDefault(0f),
                StiffnessForce = j.Stiffness.GetValueOrDefault(1f),
            };

            // 角度制限（Vrm10Importer.cs:632-653 と同じ）。向きは Axes.X で反転（同 :663-671）。
            if (UniGLTF.Extensions.VRMC_springBone_limit.GltfDeserializer.TryGet(
                    j.Extensions as glTFExtension, out var lim) && lim?.Limit != null)
            {
                if (lim.Limit.Cone is UniGLTF.Extensions.VRMC_springBone_limit.ConeLimit cone)
                {
                    d.AngleLimitType = SpringBoneAngleLimitType.Cone;
                    d.LimitRotation  = LimitRotation(cone.Rotation);
                    d.Pitch          = cone.Angle.GetValueOrDefault();
                }
                else if (lim.Limit.Hinge is UniGLTF.Extensions.VRMC_springBone_limit.HingeLimit hinge)
                {
                    d.AngleLimitType = SpringBoneAngleLimitType.Hinge;
                    d.LimitRotation  = LimitRotation(hinge.Rotation);
                    d.Pitch          = hinge.Angle.GetValueOrDefault();
                }
                else if (lim.Limit.Spherical is UniGLTF.Extensions.VRMC_springBone_limit.SphericalLimit sp)
                {
                    d.AngleLimitType = SpringBoneAngleLimitType.Spherical;
                    d.LimitRotation  = LimitRotation(sp.Rotation);
                    d.Pitch          = sp.Pitch.GetValueOrDefault();
                    d.Yaw            = sp.Yaw.GetValueOrDefault();
                }
            }

            return d;
        }

        private static Quaternion LimitRotation(float[] xyzw)
        {
            var q = (xyzw != null && xyzw.Length == 4)
                ? new Quaternion(xyzw[0], xyzw[1], xyzw[2], xyzw[3])
                : Quaternion.identity;
            return Axes.X.Create().InvertQuaternion(q);
        }

        // ================================================================
        // ノード制約（VRMC_node_constraint）
        // ================================================================

        private void BuildConstraints()
        {
            var nodes = _data.GLTF.nodes;
            if (nodes == null) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!UniGLTF.Extensions.VRMC_node_constraint.GltfDeserializer.TryGet(
                        nodes[i].extensions, out UniGLTF.Extensions.VRMC_node_constraint.VRMC_node_constraint ext)
                    || ext?.Constraint == null)
                    continue;

                var target = Ctx(CtxOfNode(i));
                if (target == null) continue;

                var con = ext.Constraint;
                var data = new VrmNodeConstraintData();
                int? source = null;

                if (con.Roll != null)
                {
                    data.Kind     = VrmConstraintKind.Roll;
                    data.Weight   = con.Roll.Weight.GetValueOrDefault(1f);
                    data.RollAxis = ToRollAxis(con.Roll.RollAxis);
                    source        = con.Roll.Source;
                }
                else if (con.Aim != null)
                {
                    data.Kind    = VrmConstraintKind.Aim;
                    data.Weight  = con.Aim.Weight.GetValueOrDefault(1f);
                    // 右手系 → 左手系（Vrm10Importer.cs:731 と同じ）
                    data.AimAxis = ToAimAxis(Vrm10ConstraintUtil.ReverseX(con.Aim.AimAxis));
                    source       = con.Aim.Source;
                }
                else if (con.Rotation != null)
                {
                    data.Kind   = VrmConstraintKind.Rotation;
                    data.Weight = con.Rotation.Weight.GetValueOrDefault(1f);
                    source      = con.Rotation.Source;
                }
                else
                {
                    continue;
                }

                var src = source.HasValue ? Ctx(CtxOfNode(source.Value)) : null;
                if (src == null)
                {
                    _result.Warnings.Add($"\"{target.Name}\" のノード制約は制約元が見つからないため読み込みません。");
                    continue;
                }

                data.SourceName = src.Name;
                target.MeshObject.VrmConstraint = data;
                _result.ConstraintCount++;
            }
        }

        private void WarnIfConstraintsPresent()
        {
            var nodes = _data.GLTF.nodes;
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (UniGLTF.Extensions.VRMC_node_constraint.GltfDeserializer.TryGet(
                        nodes[i].extensions, out UniGLTF.Extensions.VRMC_node_constraint.VRMC_node_constraint _))
                {
                    _result.Warnings.Add("ノード制約を読み込まない設定のため、ファイル内の制約は捨てました。");
                    return;
                }
            }
        }

        private static VrmRollAxis ToRollAxis(UniGLTF.Extensions.VRMC_node_constraint.RollAxis a)
        {
            switch (a)
            {
                case UniGLTF.Extensions.VRMC_node_constraint.RollAxis.Y: return VrmRollAxis.Y;
                case UniGLTF.Extensions.VRMC_node_constraint.RollAxis.Z: return VrmRollAxis.Z;
                default:                                                 return VrmRollAxis.X;
            }
        }

        private static VrmAimAxis ToAimAxis(UniGLTF.Extensions.VRMC_node_constraint.AimAxis a)
        {
            switch (a)
            {
                case UniGLTF.Extensions.VRMC_node_constraint.AimAxis.NegativeX: return VrmAimAxis.NegativeX;
                case UniGLTF.Extensions.VRMC_node_constraint.AimAxis.PositiveY: return VrmAimAxis.PositiveY;
                case UniGLTF.Extensions.VRMC_node_constraint.AimAxis.NegativeY: return VrmAimAxis.NegativeY;
                case UniGLTF.Extensions.VRMC_node_constraint.AimAxis.PositiveZ: return VrmAimAxis.PositiveZ;
                case UniGLTF.Extensions.VRMC_node_constraint.AimAxis.NegativeZ: return VrmAimAxis.NegativeZ;
                default:                                                        return VrmAimAxis.PositiveX;
            }
        }
    }
}
