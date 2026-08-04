// MirrorBranchOps.cs
// ミラー分岐（実体側／ミラー側）の共通ロジック。Editor / Runtime 共有。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【移設元】
//   Editor/HierarchyIO/HierarchyExportWindow.cs の private 実装
//   （MirrorPeerIndex / AnalyzeMirrorBranches / AssignBranchSide / MirrorLocalTRS
//     および CreateMeshGameObject 内の親解決規則）。
//
// 【ミラー側の判定】
//   MeshType.MirrorSide  … MirrorPair 方式のミラー側
//   MeshType.BakedMirror … ベイクドミラー方式のミラー側
//   どちらも実頂点を持つため、ミラー分岐内では同じ扱いにする。
//
// 【相方（ピア）の求め方】
//   1) ModelContext.MirrorPairs         … PMX/MQO の BakeMirror=false 経路
//   2) MeshContext.BakedMirrorSourceIndex … PMX の BakeMirror=true 経路 / MQO の両経路
//   PMX の MirrorPair 経路は BakedMirrorSourceIndex を設定しないため両方を見る。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    // ================================================================
    // 実体側 index ↔ ミラー側 index の対応表
    // ================================================================

    /// <summary>
    /// 実体側 index ↔ ミラー側 index の双方向対応表。
    /// MeshContext は Equals を上書きしていないため参照一致で index を引ける。
    /// </summary>
    public sealed class MirrorPeerIndex
    {
        private readonly Dictionary<int, int> _realOfMirror = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _mirrorOfReal = new Dictionary<int, int>();

        /// <summary>登録済みのペア数。</summary>
        public int Count => _realOfMirror.Count;

        public static MirrorPeerIndex Build(ModelContext model)
        {
            var map = new MirrorPeerIndex();
            if (model == null) return map;

            int count = model.MeshContextCount;

            // 1) MirrorPairs（オブジェクト参照から index を引く）
            if (model.MirrorPairs != null && model.MirrorPairs.Count > 0)
            {
                var indexOf = new Dictionary<MeshContext, int>();
                for (int i = 0; i < count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null && !indexOf.ContainsKey(mc)) indexOf[mc] = i;
                }

                foreach (var pair in model.MirrorPairs)
                {
                    if (pair?.Real == null || pair.Mirror == null) continue;
                    if (!indexOf.TryGetValue(pair.Real,   out int r)) continue;
                    if (!indexOf.TryGetValue(pair.Mirror, out int m)) continue;
                    map.Register(r, m);
                }
            }

            // 2) BakedMirrorSourceIndex
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (mc.Type != MeshType.MirrorSide && mc.Type != MeshType.BakedMirror) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= count || src == i) continue;
                if (model.GetMeshContext(src) == null) continue;

                map.Register(src, i);
            }

            return map;
        }

        /// <summary>既に登録済みの側は上書きしない（MirrorPairs を優先する）。</summary>
        private void Register(int realIndex, int mirrorIndex)
        {
            if (!_realOfMirror.ContainsKey(mirrorIndex)) _realOfMirror[mirrorIndex] = realIndex;
            if (!_mirrorOfReal.ContainsKey(realIndex))   _mirrorOfReal[realIndex]   = mirrorIndex;
        }

        /// <summary>ミラー側 index から実体側 index を引く。</summary>
        public bool TryGetReal(int mirrorIndex, out int realIndex)
            => _realOfMirror.TryGetValue(mirrorIndex, out realIndex);

        /// <summary>実体側 index からミラー側 index を引く。</summary>
        public bool TryGetMirror(int realIndex, out int mirrorIndex)
            => _mirrorOfReal.TryGetValue(realIndex, out mirrorIndex);

        /// <summary>実体側として登録されているか。</summary>
        public bool HasMirror(int realIndex) => _mirrorOfReal.ContainsKey(realIndex);

        /// <summary>ミラー側として登録されているか。</summary>
        public bool HasReal(int mirrorIndex) => _realOfMirror.ContainsKey(mirrorIndex);
    }

    // ================================================================
    // ミラー分岐の解析・鏡像化
    // ================================================================

    public static class MirrorBranchOps
    {
        /// <summary>ミラー分岐のミラー側ノードに付ける接尾辞。</summary>
        public const string MirrorBranchSuffix = "+";

        /// <summary>分岐内の所属側: 実体側。</summary>
        public const int SideReal = 0;

        /// <summary>分岐内の所属側: ミラー側。</summary>
        public const int SideMirror = 1;

        /// <summary>
        /// ミラー側のメッシュコンテキストか。
        /// MirrorPair 方式（MirrorSide）とベイクドミラー方式（BakedMirror）の両方を含む。
        /// </summary>
        public static bool IsMirrorSideContext(MeshContext mc)
        {
            if (mc == null) return false;
            return mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror;
        }

        /// <summary>
        /// IsMirrorBranchRoot が立ったノードの配下を走査し、各コンテキストの所属側を返す。
        ///   SideReal(0) = 実体側 / SideMirror(1) = ミラー側
        /// ミラー側コンテキストを自身または祖先に持つノードはミラー側として扱う。
        /// （作業用の無効データがミラー側の下にぶら下がっていてもミラー側に入る）
        /// </summary>
        /// <param name="parentIndices">
        /// Depth から補正した親インデックス配列（MeshHierarchyOps.BuildParentIndicesFromDepth）。
        /// null の場合は MeshContext.HierarchyParentIndex をそのまま使う。
        /// </param>
        public static Dictionary<int, int> AnalyzeMirrorBranches(ModelContext model, int[] parentIndices)
        {
            var result = new Dictionary<int, int>();
            if (model == null) return result;

            int count = model.MeshContextCount;

            // 親 → 子 の索引を先に作る
            var childrenOf = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                int hp = (parentIndices != null && i < parentIndices.Length)
                    ? parentIndices[i]
                    : (model.GetMeshContext(i)?.HierarchyParentIndex ?? -1);
                if (hp < 0) continue;

                if (!childrenOf.TryGetValue(hp, out var list))
                {
                    list = new List<int>();
                    childrenOf[hp] = list;
                }
                list.Add(i);
            }

            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.IsMirrorBranchRoot) continue;

                AssignBranchSide(model, childrenOf, result, i, parentIsMirror: false);
            }

            return result;
        }

        private static void AssignBranchSide(
            ModelContext model, Dictionary<int, List<int>> childrenOf,
            Dictionary<int, int> result, int index, bool parentIsMirror)
        {
            if (result.ContainsKey(index)) return;   // 循環・重複防止

            var mc = model.GetMeshContext(index);
            bool isMirror = parentIsMirror || IsMirrorSideContext(mc);
            result[index] = isMirror ? SideMirror : SideReal;

            if (!childrenOf.TryGetValue(index, out var children)) return;
            foreach (int c in children)
                AssignBranchSide(model, childrenOf, result, c, isMirror);
        }

        // ================================================================
        // 親解決
        // ================================================================

        /// <summary>
        /// ミラー枝での親を解決する。
        ///   1) 階層親のミラー相方がミラー枝に存在すればそれ
        ///   2) 階層親そのものがミラー枝に存在すればそれ（相方の無い共通関節）
        ///   3) それ以外は実体側の階層親
        /// </summary>
        /// <param name="mirror">解決対象がミラー枝側か。</param>
        /// <param name="mirrorNodeExists">index のノードがミラー枝に生成済みかを返す判定。</param>
        /// <param name="resolvedIndex">解決した親の MeshContextList 索引。</param>
        /// <param name="resolvedIsMirrorSide">解決した親がミラー枝側のノードか。</param>
        /// <returns>親が決まれば true。階層親が無ければ false。</returns>
        public static bool TryResolveMirrorParent(
            MirrorPeerIndex peers,
            int hierarchyParentIndex,
            bool mirror,
            Func<int, bool> mirrorNodeExists,
            out int resolvedIndex,
            out bool resolvedIsMirrorSide)
        {
            resolvedIndex        = -1;
            resolvedIsMirrorSide = false;

            if (hierarchyParentIndex < 0) return false;

            if (mirror && mirrorNodeExists != null)
            {
                if (peers != null &&
                    peers.TryGetMirror(hierarchyParentIndex, out int peerIdx) &&
                    mirrorNodeExists(peerIdx))
                {
                    resolvedIndex        = peerIdx;
                    resolvedIsMirrorSide = true;
                    return true;
                }

                if (mirrorNodeExists(hierarchyParentIndex))
                {
                    resolvedIndex        = hierarchyParentIndex;
                    resolvedIsMirrorSide = true;
                    return true;
                }
            }

            resolvedIndex        = hierarchyParentIndex;
            resolvedIsMirrorSide = false;
            return true;
        }

        // ================================================================
        // 鏡像化
        // ================================================================

        /// <summary>
        /// ミラー軸（MirrorAxis: 1=X / 2=Y / 4=Z）の面で位置・回転を鏡像化する。
        /// 位置は該当軸成分を面で反転（面のオフセットは MirrorDistance）。
        /// 回転は該当軸以外の2成分を符号反転。
        /// axisSource には軸・距離の正本（ミラー側なら実体側相方）を渡す。
        /// </summary>
        public static void MirrorLocalTRS(MeshContext axisSource, ref Vector3 pos, ref Vector3 rot)
        {
            if (axisSource == null) return;
            MirrorLocalTRS(axisSource.MirrorAxis, axisSource.MirrorDistance, ref pos, ref rot);
        }

        /// <summary>ミラー軸・距離を直接指定する版。</summary>
        public static void MirrorLocalTRS(int mirrorAxis, float mirrorDistance, ref Vector3 pos, ref Vector3 rot)
        {
            float d = mirrorDistance;

            switch (mirrorAxis)
            {
                case 2:  // Y
                    pos = new Vector3(pos.x, 2f * d - pos.y, pos.z);
                    rot = new Vector3(-rot.x, rot.y, -rot.z);
                    break;
                case 4:  // Z
                    pos = new Vector3(pos.x, pos.y, 2f * d - pos.z);
                    rot = new Vector3(-rot.x, -rot.y, rot.z);
                    break;
                default: // X
                    pos = new Vector3(2f * d - pos.x, pos.y, pos.z);
                    rot = new Vector3(rot.x, -rot.y, -rot.z);
                    break;
            }
        }

        /// <summary>ミラー軸の面で位置のみを鏡像化する。</summary>
        public static Vector3 MirrorPoint(int mirrorAxis, float mirrorDistance, Vector3 pos)
        {
            switch (mirrorAxis)
            {
                case 2:  return new Vector3(pos.x, 2f * mirrorDistance - pos.y, pos.z);
                case 4:  return new Vector3(pos.x, pos.y, 2f * mirrorDistance - pos.z);
                default: return new Vector3(2f * mirrorDistance - pos.x, pos.y, pos.z);
            }
        }
    }
}
