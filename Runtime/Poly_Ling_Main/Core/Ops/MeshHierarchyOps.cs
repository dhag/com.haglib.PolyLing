// MeshHierarchyOps.cs
// Depth 値からメッシュの親子関係（ParentIndex / HierarchyParentIndex）を解決するヘルパー。
//
// MQO の depth はリスト順に依存する相対値のため、スタックで親を決める。
// ミラー側（MirrorSide / BakedMirror）は実体側の直後に「同じ Depth」で挿入されるため、
// 親候補スタックに対しては push も pop も行わない。
//   pop すると実体側（例: ゆびA1）がスタックから落ち、
//   後続の深い子（ゆびB1）の親がグループ（人_指obj56）に化ける。
//   push すると後続の深い子の親がミラー側に化ける。
// ミラー側の判定は MirrorBranchOps.IsMirrorSideContext に集約している。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// Depth からメッシュ階層を解決するユーティリティ。
    /// </summary>
    public static class MeshHierarchyOps
    {
        /// <summary>
        /// Depth 値から ParentIndex を再計算して各 MeshContext に書き込む（破壊的）。
        /// </summary>
        /// <param name="meshContexts">対象の MeshContext リスト</param>
        /// <param name="indexOffset">グローバルインデックスへのオフセット（ボーン数）</param>
        /// <param name="setHierarchyParent">
        /// true のとき HierarchyParentIndex（GameObject階層の親）にも同じ値を設定する。
        /// </param>
        public static void RecalculateParentIndicesFromDepth(
            IList<MeshContext> meshContexts, int indexOffset = 0, bool setHierarchyParent = true)
        {
            if (meshContexts == null || meshContexts.Count == 0)
                return;

            // スタック: (ローカルインデックス, Depth)
            var parentStack = new Stack<(int index, int depth)>();

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null) continue;

                int currentDepth = ctx.Depth;

                // ミラー側はスタックを一切変更せずに親だけ決める（実体側と同じ親になる）。
                if (MirrorBranchOps.IsMirrorSideContext(ctx))
                {
                    ctx.ParentIndex = FindParent(parentStack, currentDepth, indexOffset);
                    if (setHierarchyParent) ctx.HierarchyParentIndex = ctx.ParentIndex;
                    continue;
                }

                if (currentDepth == 0)
                {
                    // ルートオブジェクト
                    ctx.ParentIndex = -1;
                    if (setHierarchyParent) ctx.HierarchyParentIndex = -1;

                    parentStack.Clear();
                    parentStack.Push((i, currentDepth));
                    continue;
                }

                // 現在の Depth より浅い最も近い親を探す
                while (parentStack.Count > 0 && parentStack.Peek().depth >= currentDepth)
                    parentStack.Pop();

                ctx.ParentIndex = parentStack.Count > 0
                    ? parentStack.Peek().index + indexOffset
                    : -1;

                if (setHierarchyParent) ctx.HierarchyParentIndex = ctx.ParentIndex;

                parentStack.Push((i, currentDepth));
            }
        }

        /// <summary>
        /// モデルを変更せずに、Depth から補正した親インデックス配列を返す（非破壊）。
        /// MeshType.Bone は HierarchyParentIndex をそのまま採用する
        /// （ボーン親は PMX 由来で Depth とは無関係のため）。
        /// </summary>
        public static int[] BuildParentIndicesFromDepth(ModelContext model)
        {
            int count = model?.MeshContextCount ?? 0;
            var result = new int[count];
            if (count == 0) return result;

            var parentStack = new Stack<(int index, int depth)>();

            for (int i = 0; i < count; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx == null)
                {
                    result[i] = -1;
                    continue;
                }

                // ボーンは Depth 由来ではないのでそのまま
                if (ctx.Type == MeshType.Bone)
                {
                    result[i] = ctx.HierarchyParentIndex;
                    continue;
                }

                int currentDepth = ctx.Depth;

                if (MirrorBranchOps.IsMirrorSideContext(ctx))
                {
                    result[i] = FindParent(parentStack, currentDepth, 0);
                    continue;
                }

                if (currentDepth == 0)
                {
                    result[i] = -1;
                    parentStack.Clear();
                    parentStack.Push((i, currentDepth));
                    continue;
                }

                while (parentStack.Count > 0 && parentStack.Peek().depth >= currentDepth)
                    parentStack.Pop();

                result[i] = parentStack.Count > 0 ? parentStack.Peek().index : -1;

                parentStack.Push((i, currentDepth));
            }

            return result;
        }

        /// <summary>
        /// スタックを変更せずに currentDepth より浅い最初の要素を親として返す。
        /// Stack&lt;T&gt; の列挙は LIFO 順（先頭が Peek と同一）。
        /// </summary>
        private static int FindParent(
            Stack<(int index, int depth)> parentStack, int currentDepth, int indexOffset)
        {
            foreach (var e in parentStack)
            {
                if (e.depth < currentDepth)
                    return e.index + indexOffset;
            }
            return -1;
        }
    }
}
