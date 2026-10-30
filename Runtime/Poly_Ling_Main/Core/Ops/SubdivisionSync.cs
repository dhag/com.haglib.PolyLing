// SubdivisionSync.cs
// サブディビジョンの親（ケージ）と子（滑らかな結果）のつながりを、実行時に保つ。
// 評価器（ModifierEvaluator）に載る、subdivideMesh の処理器。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【つながりの正本は ObjectGroup】
//   subdivideMesh コマンドが作ったオブジェクトグループの項目が、
//   親（cageMasterIndex の ObjectId）・回数（level）・子（出力先）を持つ。
//   保存・転送・Undo はグループの既存の仕組みに乗る。ここが持つのは
//   計算済みのステンシルの控えだけで、保存しない。
//   項目を集めるのは評価器（ModifierEvaluator.CollectEntries）。親は入力のキー cageMasterIndex で引く。
//
// 【いつ動くか】評価器のきっかけ 2 か所から呼ばれる。
//   Refresh  … GPU バッファを作り直す入口（MeshSceneRenderer.RebuildAdapter → ModifierEvaluator.RefreshModel）。
//              面構成の署名が変わっていれば子を作り直し、変わっていなければ
//              位置だけ書き直す。面の追加・削除・Undo・読み込みはすべてここを通る。
//   SyncDrag … 頂点ドラッグ中の軽量同期（PlayerViewportManager.SyncMeshPositionsAndTransform →
//              ModifierEvaluator.SyncFromInput）。SubdivideMeshCommand は FollowsDrag。
//              ミラー側の同期と同じ位置。面構成は変えず、位置だけを書く。
//
// 【面構成の署名】SubdivisionOps.TopologySignature（線分を混ぜない）で判断する。
//   評価器の入力の署名（MeshSignature.Topology）は線分も混ぜるので、作り直すかの判断には使わない。
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

        /// <summary>評価器へ登録する処理器（ModifierEvaluator の静的初期化で登録される）。</summary>
        public static readonly IModifierHandler Handler = new SubdivisionHandler();

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

        private static readonly ConditionalWeakTable<MeshContext, Cache> _cache
            = new ConditionalWeakTable<MeshContext, Cache>();

        // ================================================================
        // つながりを引く
        // ================================================================

        /// <summary>評価器の項目から、つながりを作る。サブディビジョンでない・揃っていなければ null。</summary>
        private static Link ToLink(ModifierEvaluator.Entry e)
        {
            if (e?.Step == null) return null;
            if (!string.Equals(e.Step.Action, Action, System.StringComparison.Ordinal)) return null;

            var cage  = e.Input(CageKey);
            var child = e.FirstOutput;
            if (cage?.MeshObject == null || child?.MeshObject == null) return null;

            int level = SubdivisionOps.LevelMin;
            if (int.TryParse(e.Step.GetArg(LevelKey, "1"), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int lv))
                level = Mathf.Clamp(lv, SubdivisionOps.LevelMin, SubdivisionOps.LevelMax);

            return new Link { Group = e.Group, Step = e.Step, Cage = cage, Child = child, Level = level };
        }

        /// <summary>モデルのグループから、親と子のつながりを全部引く。</summary>
        public static List<Link> CollectLinks(ModelContext model)
        {
            var list = new List<Link>();
            foreach (var e in ModifierEvaluator.CollectEntries(model))
            {
                var l = ToLink(e);
                if (l != null) list.Add(l);
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
            // 評価器の入力の署名も今の値で控える（控えのキーは出力の先頭＝子）。
            ModifierEvaluator.Prime(child, new[] { cage });
        }

        /// <summary>子の控えを捨てる（解除したとき）。</summary>
        public static void Forget(MeshContext child)
        {
            if (child == null) return;
            _cache.Remove(child);
            ModifierEvaluator.Forget(child);
        }

        // ================================================================
        // 処理器
        // ================================================================

        private sealed class SubdivisionHandler : IModifierHandler
        {
            public string Action => SubdivisionSync.Action;

            /// <summary>
            /// 子を親に合わせる。面構成が変わっていれば作り直し、
            /// 変わっていなければ位置と法線だけ書き直す（入力の署名の変化に関わらず毎回）。
            /// </summary>
            public void Refresh(ModelContext model, ModifierEvaluator.Entry entry, bool inputsChanged)
            {
                var l = ToLink(entry);
                if (l == null) return;

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
                        return;
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
                    if (pos == null) return;
                    var verts = l.Child.MeshObject.Vertices;
                    for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
                    l.Child.MeshObject.RecalculateSmoothNormals();
                    l.Child.MeshObject.InvalidatePositionCache();
                }

                // 面は UnityMesh から描かれる。中身・法線を変えたので作り直す。
                // 描画提出と同じフレームで走り得る入口なので遅延破棄にする。
                var mesh = l.Child.MeshObject.ToUnityMesh(model.MaterialCount);
                mesh.name = l.Child.Name;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                l.Child.ReplaceUnityMeshDeferred(mesh);

                // 親の面は塗らない（子が親の内側にあるため、塗ると子が隠れる）。
                // 保存しない表示設定なので、つながりを最初に見たときに立てる。
                if (first) l.Cage.HideFaceShading = true;
            }

            /// <summary>
            /// 親の頂点を動かしている最中に、子の位置を書き直す。
            /// 面構成が変わっていそうなとき（頂点数が控えと違う）は何もしない
            /// （作り直しは Refresh が受け持つ）。
            /// </summary>
            public void SyncDrag(ModelContext model, ModifierEvaluator.Entry entry, MeshContext movedInput,
                                 System.Action<MeshContext> upload)
            {
                var l = ToLink(entry);
                if (l == null || !ReferenceEquals(l.Cage, movedInput)) return;
                if (!_cache.TryGetValue(l.Child, out var c)) return;
                if (c.Stencil.InputCount != l.Cage.MeshObject.VertexCount) return;
                if (c.Stencil.OutputCount != l.Child.MeshObject.VertexCount) return;

                var pos = c.Stencil.Apply(CagePositions(l.Cage));
                if (pos == null) return;
                var verts = l.Child.MeshObject.Vertices;
                for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
                l.Child.MeshObject.InvalidatePositionCache();
                upload?.Invoke(l.Child);
            }

            /// <summary>
            /// 解除（「適用」）：子を普通のメッシュにする。ReleaseSubdivision（子を残す）と同じ後始末。
            /// 控えを捨て、親の面の塗りを戻し、子のロックを外す。
            /// </summary>
            public void Release(ModelContext model, ModifierEvaluator.Entry entry)
            {
                var cage  = entry?.Input(CageKey);
                var child = entry?.FirstOutput;
                if (child != null)
                {
                    Forget(child);
                    child.IsLocked = false;
                }
                if (cage != null) cage.HideFaceShading = false;
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
