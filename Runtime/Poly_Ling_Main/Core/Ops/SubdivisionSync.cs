// SubdivisionSync.cs
// サブディビジョンの親（ケージ）と子（滑らかな結果）のつながりを、実行時に保つ。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【つながりの正本は ObjectGroup】
//   subdivideMesh コマンドが作ったオブジェクトグループの項目が、
//   親（cageMasterIndex の ObjectId）・回数（level）・子（出力先）を持つ。
//   保存・転送・Undo はグループの既存の仕組みに乗る。ここが持つのは
//   計算済みのステンシルの控えだけで、保存しない。
//
// 【いつ動くか】
//   RefreshModel … GPU バッファを作り直す入口（MeshSceneRenderer.RebuildAdapter）の先頭。
//                  面構成の署名が変わっていれば子を作り直し、変わっていなければ
//                  位置だけ書き直す。面の追加・削除・Undo・読み込みはすべてここを通る。
//   SyncFromCage … 頂点ドラッグ中の軽量同期（PlayerViewportManager.SyncMeshPositionsAndTransform）。
//                  ミラー側の同期と同じ位置。面構成は変えず、位置だけを書く。
//
// 【姿勢】子は親の子として置き、ローカルは単位。親を動かすと階層の計算
//   （ModelContext.ComputeWorldMatrices）で子もそのまま動く。ここでは姿勢に触れない。

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class SubdivisionSync
    {
        /// <summary>サブディビジョンの項目の action 名（SubdivideMeshCommand）。</summary>
        public const string Action   = "subdivideMesh";
        /// <summary>親を指す引数のキー（ObjectGroupStep.MeshRefIds のキー）。</summary>
        public const string CageKey  = "cageMasterIndex";
        /// <summary>回数の引数のキー。</summary>
        public const string LevelKey = "level";

        /// <summary>親 1 つと子 1 つのつながり。</summary>
        public sealed class Link
        {
            public ObjectGroup     Group;
            public ObjectGroupStep Step;
            public MeshContext     Cage;
            public MeshContext     Child;
            public int             Level;
        }

        /// <summary>子ごとの計算済みの控え。</summary>
        private sealed class Cache
        {
            public MeshContext        Cage;
            public int                Level;
            public ulong              Signature;
            public SubdivisionStencil Stencil;
        }

        private static readonly ConditionalWeakTable<ModelContext, List<Link>> _links
            = new ConditionalWeakTable<ModelContext, List<Link>>();

        private static readonly ConditionalWeakTable<MeshContext, Cache> _cache
            = new ConditionalWeakTable<MeshContext, Cache>();

        // ================================================================
        // つながりを引く
        // ================================================================

        /// <summary>モデルのグループから、親と子のつながりを全部引く。</summary>
        public static List<Link> CollectLinks(ModelContext model)
        {
            var list = new List<Link>();
            if (model?.ObjectGroups == null || model.MeshContextList == null) return list;

            foreach (var g in model.ObjectGroups)
            {
                if (g?.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable) continue;
                    if (!string.Equals(s.Action, Action, System.StringComparison.Ordinal)) continue;

                    var cageIds = s.GetMeshRefIds(CageKey);
                    ulong cageId = cageIds.Count > 0 ? cageIds[0] : 0UL;
                    ulong childId = s.FirstOutputId;
                    if (cageId == 0UL || childId == 0UL) continue;

                    var cage  = FindById(model, cageId);
                    var child = FindById(model, childId);
                    if (cage?.MeshObject == null || child?.MeshObject == null) continue;

                    int level = SubdivisionOps.LevelMin;
                    if (int.TryParse(s.GetArg(LevelKey, "1"), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int lv))
                        level = Mathf.Clamp(lv, SubdivisionOps.LevelMin, SubdivisionOps.LevelMax);

                    list.Add(new Link { Group = g, Step = s, Cage = cage, Child = child, Level = level });
                }
            }
            return list;
        }

        /// <summary>この描画オブジェクトを親に持つつながり（無ければ null）。</summary>
        public static Link FindByCage(ModelContext model, MeshContext cage)
        {
            if (cage == null) return null;
            foreach (var l in CollectLinks(model)) if (ReferenceEquals(l.Cage, cage)) return l;
            return null;
        }

        /// <summary>この描画オブジェクトを子に持つつながり（無ければ null）。</summary>
        public static Link FindByChild(ModelContext model, MeshContext child)
        {
            if (child == null) return null;
            foreach (var l in CollectLinks(model)) if (ReferenceEquals(l.Child, child)) return l;
            return null;
        }

        private static MeshContext FindById(ModelContext model, ulong id)
        {
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ObjectId == id) return list[i];
            return null;
        }

        // ================================================================
        // 控え
        // ================================================================

        /// <summary>コマンドが作った直後の結果を控える。直後の RefreshModel で作り直さないため。</summary>
        public static void Prime(MeshContext child, MeshContext cage, int level, SubdivisionStencil stencil)
        {
            if (child == null || cage?.MeshObject == null || stencil == null) return;
            _cache.Remove(child);
            _cache.Add(child, new Cache
            {
                Cage      = cage,
                Level     = level,
                Signature = SubdivisionOps.TopologySignature(cage.MeshObject),
                Stencil   = stencil,
            });
        }

        /// <summary>子の控えを捨てる（解除したとき）。</summary>
        public static void Forget(MeshContext child)
        {
            if (child != null) _cache.Remove(child);
        }

        // ================================================================
        // GPU バッファを作り直す入口から
        // ================================================================

        /// <summary>
        /// 全部の子を親に合わせる。面構成が変わっていれば作り直し、
        /// 変わっていなければ位置と法線だけ書き直す。
        /// </summary>
        public static void RefreshModel(ModelContext model)
        {
            if (model == null) return;

            var links = CollectLinks(model);
            _links.Remove(model);
            _links.Add(model, links);
            if (links.Count == 0) return;

            int materialCount = model.MaterialCount;

            foreach (var l in links)
            {
                // 子は親の編集にいつも追随しているので、「ソースが変わったのに出力が古い」
                // 状態は残らない。ダイジェストを持たせると親を動かすたびに要更新と出るため、
                // 空（ObjectGroupOps.IsStale が「変わっていない」と扱う値）にしておく。
                if (!string.IsNullOrEmpty(l.Group.SourceDigest)) l.Group.SourceDigest = "";

                ulong sig = SubdivisionOps.TopologySignature(l.Cage.MeshObject);
                _cache.TryGetValue(l.Child, out var c);

                bool first = c == null;
                bool rebuild = first
                            || !ReferenceEquals(c.Cage, l.Cage)
                            || c.Level != l.Level
                            || c.Signature != sig
                            || c.Stencil.OutputCount != l.Child.MeshObject.VertexCount;

                if (rebuild)
                {
                    if (!SubdivisionOps.TryBuild(l.Cage.MeshObject, l.Level,
                            out var result, out var stencil, out string err))
                    {
                        Debug.LogWarning($"[Subdivision] {l.Child.Name}: 作り直せません: {err}");
                        continue;
                    }
                    SubdivisionOps.ReplaceContents(l.Child.MeshObject, result);
                    _cache.Remove(l.Child);
                    _cache.Add(l.Child, new Cache
                    {
                        Cage = l.Cage, Level = l.Level, Signature = sig, Stencil = stencil,
                    });
                }
                else
                {
                    var pos = c.Stencil.Apply(CagePositions(l.Cage));
                    if (pos == null) continue;
                    var verts = l.Child.MeshObject.Vertices;
                    for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
                    l.Child.MeshObject.RecalculateSmoothNormals();
                    l.Child.MeshObject.InvalidatePositionCache();
                }

                // 面は UnityMesh から描かれる。中身・法線を変えたので作り直す。
                // 描画提出と同じフレームで走り得る入口なので遅延破棄にする。
                var mesh = l.Child.MeshObject.ToUnityMesh(materialCount);
                mesh.name = l.Child.Name;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                l.Child.ReplaceUnityMeshDeferred(mesh);

                // 親の面は塗らない（子が親の内側にあるため、塗ると子が隠れる）。
                // 保存しない表示設定なので、つながりを最初に見たときに立てる。
                if (first) l.Cage.HideFaceShading = true;
            }
        }

        // ================================================================
        // 頂点ドラッグ中
        // ================================================================

        /// <summary>
        /// 親の頂点を動かしている最中に、子の位置を書き直す。
        /// 面構成が変わっていそうなとき（頂点数が控えと違う）は何もしない
        /// （作り直しは RefreshModel が受け持つ）。
        /// </summary>
        /// <param name="upload">位置を書き直した子を GPU へ上げる口。</param>
        public static void SyncFromCage(ModelContext model, MeshContext cage, System.Action<MeshContext> upload)
        {
            if (model == null || cage?.MeshObject == null) return;
            if (!_links.TryGetValue(model, out var links) || links == null) return;

            foreach (var l in links)
            {
                if (!ReferenceEquals(l.Cage, cage)) continue;
                if (!_cache.TryGetValue(l.Child, out var c)) continue;
                if (c.Stencil.InputCount != cage.MeshObject.VertexCount) continue;
                if (c.Stencil.OutputCount != l.Child.MeshObject.VertexCount) continue;

                var pos = c.Stencil.Apply(CagePositions(cage));
                if (pos == null) continue;
                var verts = l.Child.MeshObject.Vertices;
                for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
                l.Child.MeshObject.InvalidatePositionCache();
                upload?.Invoke(l.Child);
            }
        }

        /// <summary>親の頂点位置。モーフのプレビュー等の作業中オフセットがあれば足す。</summary>
        private static Vector3[] CagePositions(MeshContext cage)
        {
            var verts = cage.MeshObject.Vertices;
            var p = new Vector3[verts.Count];
            var wp = cage.WorkingPositions;
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = verts[i].Position;
                if (wp != null && i < wp.Length) p[i] += wp[i];
            }
            return p;
        }
    }
}
