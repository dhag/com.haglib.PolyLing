// MirrorBranchPlan.cs
// ミラー分岐の索引対応・許容差・出力計画（MirrorBranchOps から分離）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置（MirrorBranchOps.cs と同じ名前空間。MirrorBranchOps.cs から分割）

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

    /// <summary>
    /// ミラー分岐配下で「個別オブジェクトのミラー設定漏れ」をどう扱うか。
    /// </summary>
    public enum MirrorBranchTolerance
    {
        /// <summary>ミラー側コンテキストが実在するノードだけをミラー枝に出す（従来動作）。</summary>
        Strict = 0,

        /// <summary>
        /// 分岐配下の実体側ノードは、ミラー側コンテキストが無くてもミラー枝に出す。
        /// 形状は実体側から鏡像を生成する。既定。
        /// </summary>
        Tolerant = 1,
    }

    // ================================================================
    // ミラー分岐の出力計画
    // ================================================================

    /// <summary>
    /// 分岐解析の結果を「各ノードを実体側／ミラー枝のどちらに出すか」まで
    /// 落とし込んだ表。エクスポートとスキンド変換が同じ表を読む。
    ///
    /// 関節（頂点ゼロのノード）の両側複製は呼び出し側の都合なのでここでは扱わない。
    /// </summary>
    public sealed class MirrorBranchPlan
    {
        public struct Node
        {
            /// <summary>MeshContextList の索引。</summary>
            public int Index;

            /// <summary>実体側の枝に出すか。</summary>
            public bool EmitReal;

            /// <summary>ミラー枝に出すか。</summary>
            public bool EmitMirror;

            /// <summary>
            /// ミラー枝に出す形状を実体側から生成する必要があるか。
            /// false のときは自身が既に鏡像済みのミラー側コンテキスト。
            /// </summary>
            public bool GenerateMirrorShape;

            /// <summary>鏡映に使う軸（1=X / 2=Y / 4=Z）。ノード自身の設定が正本。</summary>
            public int MirrorAxis;

            /// <summary>鏡映に使う距離。ノード自身の設定が正本。</summary>
            public float MirrorDistance;
        }

        private readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();

        /// <summary>従来の所属側テーブル（SideReal / SideMirror）。</summary>
        public Dictionary<int, int> Side { get; internal set; }

        /// <summary>実体側 ↔ ミラー側の対応表。</summary>
        public MirrorPeerIndex Peers { get; internal set; }

        /// <summary>適用した許容モード。</summary>
        public MirrorBranchTolerance Tolerance { get; internal set; }

        internal void Add(Node node) => _nodes[node.Index] = node;

        public bool TryGet(int index, out Node node) => _nodes.TryGetValue(index, out node);

        /// <summary>ミラー枝に出すノードか。</summary>
        public bool EmitsMirror(int index)
            => _nodes.TryGetValue(index, out var n) && n.EmitMirror;

        /// <summary>ミラー枝の形状を実体側から生成する必要があるノードか。</summary>
        public bool GeneratesMirrorShape(int index)
            => _nodes.TryGetValue(index, out var n) && n.EmitMirror && n.GenerateMirrorShape;

        /// <summary>
        /// 実体側から鏡像を生成する必要があるノードを索引の昇順で返す。
        /// スキンド変換はこれを見てミラー側 MeshContext を実体化する。
        /// </summary>
        public List<Node> CollectGeneratedMirrors()
        {
            var list = new List<Node>();
            foreach (var kv in _nodes)
                if (kv.Value.EmitMirror && kv.Value.GenerateMirrorShape) list.Add(kv.Value);
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
            return list;
        }
    }
}
