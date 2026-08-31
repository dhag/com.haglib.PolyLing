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
        ///
        /// 【分岐ルート自身の扱い】
        ///   分岐フラグが立ったオブジェクトは、そのオブジェクト自身を含めて
        ///   子孫まで枝に入れる。条件分岐は設けない。
        ///
        ///   枝の中の空オブジェクト（頂点なし＝関節）は、そのオブジェクトの
        ///   ミラー設定の有無に関わらず実体側とミラー側の両方へ複製される
        ///   （HierarchyExportWindow の makeMirror が isJoint 単独で成立し、
        ///     MeshFilterToSkinnedConverter の BonePlan も同様）。
        ///   途中の空オブジェクトでミラー設定を忘れていても枝のツリーが
        ///   途切れないようにするための強制であり、意図した挙動。
        ///   分岐ルート自身が空であれば同様に両側へ複製される。
        /// </summary>
        /// <param name="parentIndices">
        /// Depth から補正した親インデックス配列（MeshHierarchyOps.BuildParentIndicesFromDepth）。
        /// null の場合は MeshContext.HierarchyParentIndex をそのまま使う。
        /// </param>
        public static Dictionary<int, int> AnalyzeMirrorBranches(ModelContext model, int[] parentIndices)
            => AnalyzeMirrorBranches(model, parentIndices, null);

        /// <summary>
        /// 実体側 ↔ ミラー側の対応表を外から渡す版。
        /// 呼び出し側が既に MirrorPeerIndex を組んでいる場合の重複構築を避ける。
        /// </summary>
        public static Dictionary<int, int> AnalyzeMirrorBranches(
            ModelContext model, int[] parentIndices, MirrorPeerIndex peers)
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

            // ── 枝の外へ落ちたミラー相方を取り込む ──────────────────────
            //
            // 【なぜ要るか】
            //   ミラー側コンテキストは実体側の「兄弟」として親を解決される
            //   （MeshHierarchyOps.BuildParentIndicesFromDepth は MirrorSide を
            //     スタックへ push しないため、同じ Depth の実体側ではなく
            //     その一段上が親になる）。
            //   したがって分岐ルート自身のミラー相方は必ず枝の外に落ちる。
            //     例）分岐ルート＝左腕 のとき、右腕 の親は 上半身2 になり、
            //         左腕 の子孫を辿る AssignBranchSide では拾えない。
            //   すると右腕はミラー枝に登録されず、TryResolveMirrorParent の
            //   mirrorNodeExists が空振りして、右ひじ以下が実体側の左腕へ
            //   ぶら下がる（左右が混ざる）。
            //
            // 【対処】
            //   枝内の実体側ノードのミラー相方は、親がどこにあってもミラー側として
            //   枝へ編入する。許容モードとは無関係の不具合なので常に行う。
            if (result.Count > 0)
            {
                peers = peers ?? MirrorPeerIndex.Build(model);

                var realsInBranch = new List<int>();
                foreach (var kv in result)
                    if (kv.Value == SideReal) realsInBranch.Add(kv.Key);

                foreach (int realIndex in realsInBranch)
                {
                    if (!peers.TryGetMirror(realIndex, out int mirrorIndex)) continue;
                    if (mirrorIndex < 0 || mirrorIndex >= count) continue;
                    if (result.ContainsKey(mirrorIndex)) continue;

                    AssignBranchSide(model, childrenOf, result, mirrorIndex, parentIsMirror: true);
                }
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
        // 出力計画
        // ================================================================

        /// <summary>
        /// 鏡映に使う軸を解く。0（未設定）は X(1) に倒す。
        /// GetMirrorSymmetryAxis / EnableMirror と同じ規則。
        /// </summary>
        public static int ResolveMirrorAxis(MeshContext mc)
        {
            int axis = mc?.MirrorAxis ?? 0;
            return axis == 0 ? 1 : axis;
        }

        /// <summary>
        /// 分岐解析の結果から、各ノードを実体側／ミラー枝のどちらに出すかを決める。
        ///
        /// 【許容モード（既定）】
        ///   分岐配下の実体側ノードは、ミラー側コンテキストを持っていなくても
        ///   ミラー枝に出す。形状は実体側から鏡像を生成する。
        ///   ミラー側コンテキストを持つノードは従来どおり相方がミラー枝に出るので
        ///   二重にはしない。
        ///
        /// 【軸・距離】
        ///   ノード自身の MirrorAxis / MirrorDistance を正本にする。MirrorType は
        ///   見ない（作業中にミラーを切って戻し忘れても軸・距離は残るため）。
        ///   軸が未設定（0）のときだけ X に倒す。
        /// </summary>
        public static MirrorBranchPlan BuildMirrorBranchPlan(
            ModelContext model, int[] parentIndices,
            MirrorBranchTolerance tolerance,
            MirrorPeerIndex peers = null)
        {
            var plan = new MirrorBranchPlan { Tolerance = tolerance };

            if (model == null)
            {
                plan.Side  = new Dictionary<int, int>();
                plan.Peers = new MirrorPeerIndex();
                return plan;
            }

            peers = peers ?? MirrorPeerIndex.Build(model);
            var side = AnalyzeMirrorBranches(model, parentIndices, peers);

            plan.Side  = side;
            plan.Peers = peers;

            bool tolerant = tolerance == MirrorBranchTolerance.Tolerant;
            int  count    = model.MeshContextCount;

            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                bool inBranch = side.TryGetValue(i, out int s);
                if (!inBranch) s = SideReal;

                bool isMirrorCtx = IsMirrorSideContext(mc);

                // 枝の外はそのまま実体側。枝の中は所属側に従う。
                bool emitReal = !inBranch || s == SideReal;

                // ミラー枝に出す条件:
                //   ・所属側がミラー側（＝ミラー側コンテキスト、従来動作）
                //   ・許容モードで、実体側かつミラー相方を持たない（設定漏れの救済）
                bool emitMirror =
                    inBranch &&
                    (s == SideMirror ||
                     (tolerant && s == SideReal && !peers.HasMirror(i)));

                plan.Add(new MirrorBranchPlan.Node
                {
                    Index               = i,
                    EmitReal            = emitReal,
                    EmitMirror          = emitMirror,
                    GenerateMirrorShape = emitMirror && !isMirrorCtx,
                    MirrorAxis          = ResolveMirrorAxis(mc),
                    MirrorDistance      = mc.MirrorDistance,
                });
            }

            return plan;
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

        // ================================================================
        // ミラー側の姿勢補正
        // ================================================================

        /// <summary>
        /// ミラー軸の面での鏡映行列を作る（p → 面に対する鏡像）。
        /// </summary>
        public static Matrix4x4 MirrorMatrix(int mirrorAxis, float mirrorDistance)
        {
            float d2 = 2f * mirrorDistance;
            switch (mirrorAxis)
            {
                case 2:  // Y
                    return Matrix4x4.Translate(new Vector3(0f, d2, 0f)) *
                           Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
                case 4:  // Z
                    return Matrix4x4.Translate(new Vector3(0f, 0f, d2)) *
                           Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
                default: // X
                    return Matrix4x4.Translate(new Vector3(d2, 0f, 0f)) *
                           Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            }
        }

        /// <summary>
        /// 姿勢変更のあと、ミラー側コンテキストの局所姿勢を鏡像側へ直す。
        ///
        /// 【なぜ要るか】
        ///   このモデル形式では、ミラー側メッシュは実体側と同じ関節の下に
        ///   同じ局所原点でぶら下がり、鏡像は頂点側にだけ入っている。
        ///   関節に回転が無いうちは成立するが、鏡映 S と回転 R は可換でないため
        ///   （S·Ry(θ)·S = Ry(-θ)、S·Rz(θ)·S = Rz(-θ)、S·Rx(θ)·S = Rx(θ)）、
        ///   関節を回した瞬間にミラー側は Y・Z まわりが逆符号のまま動いてしまう。
        ///
        /// 【補正】
        ///   実体側が受けたワールドデルタを Δ = W_after · W_before⁻¹ とすると、
        ///   ミラー側が受けるべきデルタは S·Δ·S。よって目標は
        ///     W_target = S · Δ · S · W_before
        ///   ミラー側は実体側と同じ親の下にいるので、Δ はミラー側自身の
        ///   （誤って乗ってしまった）デルタと同じ値になる。相方を引かずに済む。
        ///   det(S·Δ·S) = det(Δ) > 0 なので、結果は普通の回転として分解できる。
        ///
        /// 【入れ子】
        ///   ミラー側の子は親を直せば付いてくる（W_child = W_parent · L_child は不変）。
        ///   よって祖先にミラー側を持たない「ミラー側の根」だけを対象にする。
        /// </summary>
        /// <param name="meshContexts">対象リスト</param>
        /// <param name="worldBefore">姿勢変更前のワールド行列（索引→行列）</param>
        /// <param name="worldAfter">姿勢変更後のワールド行列（索引→行列）</param>
        /// <returns>補正したコンテキスト数</returns>
        public static int CompensateMirrorSideTransforms(
            List<MeshContext> meshContexts,
            Dictionary<int, Matrix4x4> worldBefore,
            Dictionary<int, Matrix4x4> worldAfter)
        {
            if (meshContexts == null || worldBefore == null || worldAfter == null) return 0;

            int fixedCount = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (!IsMirrorSideContext(mc)) continue;
                if (HasMirrorSideAncestor(meshContexts, i)) continue;   // 根だけ直す
                if (mc.BoneTransform == null) continue;

                if (!worldBefore.TryGetValue(i, out var wBefore)) continue;
                if (!worldAfter.TryGetValue(i, out var wAfter)) continue;

                Matrix4x4 delta = wAfter * wBefore.inverse;
                if (IsNearlyIdentity(delta)) continue;                   // 動いていない

                // ミラー軸は相方（実体側）の設定を優先する
                int   axis = mc.MirrorAxis;
                float dist = mc.MirrorDistance;
                int   src  = mc.BakedMirrorSourceIndex;
                if (src >= 0 && src < meshContexts.Count && meshContexts[src] != null)
                {
                    axis = meshContexts[src].MirrorAxis;
                    dist = meshContexts[src].MirrorDistance;
                }

                Matrix4x4 s        = MirrorMatrix(axis, dist);
                Matrix4x4 target   = s * delta * s * wBefore;

                // 親のワールド（変更後）で割ってローカルへ戻す
                Matrix4x4 parentWorld = Matrix4x4.identity;
                int p = mc.HierarchyParentIndex;
                if (p >= 0 && p < meshContexts.Count && worldAfter.TryGetValue(p, out var pw))
                    parentWorld = pw;

                Matrix4x4 local = parentWorld.inverse * target;

                mc.BoneTransform.Position          = new Vector3(local.m03, local.m13, local.m23);
                mc.BoneTransform.Rotation          = local.rotation.eulerAngles;
                mc.BoneTransform.Scale             = local.lossyScale;
                mc.BoneTransform.UseLocalTransform = true;

                fixedCount++;
            }

            if (fixedCount > 0)
                Debug.Log($"[MirrorBranchOps] ミラー側の姿勢を鏡像化して補正: {fixedCount} 件");

            return fixedCount;
        }

        /// <summary>祖先（自分は含まない）にミラー側がいるか。</summary>
        private static bool HasMirrorSideAncestor(List<MeshContext> meshContexts, int index)
        {
            int cur    = meshContexts[index]?.HierarchyParentIndex ?? -1;
            int safety = meshContexts.Count + 1;

            while (cur >= 0 && cur < meshContexts.Count && safety-- > 0)
            {
                var mc = meshContexts[cur];
                if (mc == null) return false;
                if (IsMirrorSideContext(mc)) return true;
                cur = mc.HierarchyParentIndex;
            }
            return false;
        }

        private static bool IsNearlyIdentity(Matrix4x4 m, float eps = 1e-5f)
        {
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    if (Mathf.Abs(m[r, c] - Matrix4x4.identity[r, c]) > eps) return false;
            return true;
        }

        // ================================================================
        // 生成ミラーの作成
        //
        // MQO のミラーはファイルにミラー側の頂点が無く、実体側から作るしかない。
        // 生成物であることを MirrorGeometryDerived = true で示し、
        // 実効ワールドは S·H·S で解く（ModelContext.ComputeWorldMatrices）。
        //
        // ミラーを解消するときは破棄し、再びミラー化するときはここで作り直す。
        // 元は MQOImporter.CreateBakedMirrorMesh にあったが、
        // 読込時以外（ミラーの有効化）からも呼ぶため Ops へ移した。
        // ================================================================

        public static MeshContext CreateDerivedMirrorContext(MeshContext source, int sourceIndex)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled: true, nameExists: null);

        /// <summary>同名の有無を渡せる版。</summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, Func<string, bool> nameExists)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled: true, nameExists: nameExists);

        /// <summary>
        /// ミラー有効（MirrorType &gt; 0）の判定を省ける版。
        ///
        /// ミラー分岐の許容モードでは「ミラー設定を忘れた／作業中に切って戻し忘れた」
        /// ノードからもミラー側を作る必要がある。MirrorType はユーザーの表示設定に
        /// すぎず、鏡映そのものに必要なのは軸と距離だけなので、判定を外せるようにする。
        /// 軸・距離は source 自身の値を使う（分岐ルートの値では上書きしない）。
        /// </summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, bool requireMirrorEnabled)
            => CreateDerivedMirrorContext(source, sourceIndex, requireMirrorEnabled, nameExists: null);

        /// <summary>
        /// ミラー側の名前は MirrorNameOps.MakeMirrorName に決めさせる。
        ///
        /// 【なぜ直書きしないか】
        ///   「左腕」のミラーは「右腕」であって「左腕+」ではない。
        ///   接尾辞は左右を持たない名前（「腕」→「腕+」）のための逃げ道で、
        ///   左右を持つ名前に付けるのは誤りになる。判断は1箇所に置く。
        ///   nameExists には「その名前が既に使われているか」を渡す（null 可）。
        /// </summary>
        public static MeshContext CreateDerivedMirrorContext(
            MeshContext source, int sourceIndex, bool requireMirrorEnabled,
            Func<string, bool> nameExists)
        {
            if (source == null || source.MeshObject == null)
                return null;
            if (requireMirrorEnabled && !source.IsMirrored)
                return null;

            var srcMeshObj = source.MeshObject;

            if (srcMeshObj.Vertices.Count == 0)
                return null;

            int   axis = ResolveMirrorAxis(source);
            float dist = source.MirrorDistance;

            // スキニング済みか。
            //   スキンド変換（MeshFilterToSkinnedConverter の Phase 4）は、
            //   全メッシュの頂点をワールドへ焼き、BoneTransform を単位に潰し、
            //   MirrorGeometryDerived を無条件で false にする。
            //   ＝ スキンド化した時点でモデル内のミラーは全て PMX 型になる。
            //   変換より後に作るミラーもそれに揃える。
            bool skinned = srcMeshObj.IsSkinnedKind;

            // 頂点・面の鏡像化は BuildMirroredMeshObject に集約している。
            var mirrorMeshObj = BuildMirroredMeshObject(
                srcMeshObj, axis, dist, source.MirrorMaterialOffset,
                MirrorNameOps.MakeMirrorName(source.Name, MirrorBranchSuffix, nameExists));
            if (mirrorMeshObj == null) return null;
            mirrorMeshObj.Type = MeshType.BakedMirror;  // 明示的に設定

            // 姿勢は実体側と同一にする。
            // 生成ミラー（MirrorGeometryDerived=true）では、ミラー側は自前の姿勢を
            // 持たず実効ワールドを ComputeWorldMatrices が S·H·S として算出する。
            // ここで H を実体側と揃えておかないと v_M = S·v_R の不変条件が崩れる。
            //
            // PMX 型（skinned）ではコンバータが実体側の BoneTransform を
            // 単位に潰しているため、ここでのコピーも単位になり副作用は無い。
            // 描画はボーンウェイトで駆動されるので姿勢は使われない。
            if (mirrorMeshObj.BoneTransform == null)
                mirrorMeshObj.BoneTransform = new BoneTransform();
            if (source.BoneTransform != null)
            {
                mirrorMeshObj.BoneTransform.Position          = source.BoneTransform.Position;
                mirrorMeshObj.BoneTransform.Rotation          = source.BoneTransform.Rotation;
                mirrorMeshObj.BoneTransform.Scale             = source.BoneTransform.Scale;
                mirrorMeshObj.BoneTransform.UseLocalTransform = source.BoneTransform.UseLocalTransform;
            }

            // MeshContextを作成
            var mirrorContext = new MeshContext
            {
                MeshObject = mirrorMeshObj,
                Name = mirrorMeshObj.Name,
                Type = MeshType.BakedMirror,
                BakedMirrorSourceIndex = sourceIndex,
                // 実体側の頂点から生成した鏡像。実効ワールドは S·H·S で解決する。
                //
                // ただしスキニング済みメッシュから作った場合は PMX 型にする。
                // スキンド変換がモデル内の全メッシュを PMX 型に変えているので、
                // 変換より後に作るミラーだけ生成ミラーにすると持ち方が混ざる。
                //   ・頂点はコンバータがワールドへ焼いてあり BoneTransform は単位。
                //     ローカル鏡像がそのままモデル中心での鏡像になる。
                //   ・ミラー側は自分の BoneWeight で動くので、実体側の姿勢を
                //     引き写す S·H·S は不要かつ有害（二重変換になる）。
                //   ・RebakeDerivedMirrorVertices が焼いた頂点を上書きしない。
                //   ・DisableMirror が破棄せず独立メッシュとして残す。
                MirrorGeometryDerived = !skinned,
                // 階層情報は元メッシュに合わせる
                ParentIndex = source.ParentIndex,
                // ゲームオブジェクト階層の親も実体側に合わせる。
                //   既定値は -1（＝ルート）なので、設定しないとミラーだけが
                //   ルート直下に置かれ、ワールド行列に親の姿勢が乗らない。
                //   MQO 経路は挿入後に RecalculateParentIndicesFromDepth が
                //   ミラー専用分岐でここを設定するため露見しなかった。
                HierarchyParentIndex = ResolveMirrorHierarchyParent(source, skinned),
                Depth = source.Depth,
                IsVisible = source.IsVisible,
                // ミラー属性はなし（実体化されているため）
                MirrorType = 0,
                MirrorAxis = 1,
                MirrorDistance = 0,
                MirrorMaterialOffset = 0
            };

            // UnityMesh生成
            mirrorContext.UnityMesh = mirrorMeshObj.ToUnityMesh();
            mirrorContext.OriginalPositions = (Vector3[])mirrorMeshObj.Positions.Clone();

            return mirrorContext;
        }

        /// <summary>
        /// ミラー側をぶら下げる親を決める。
        ///
        /// スキンド済みモデルでは描画オブジェクトがボーンの子として並ぶ。
        /// 実体側と同じ親（＝実体側のボーン）に付けると、右のメッシュが左のボーンに
        /// ぶら下がる。左右対のボーン（MirrorBoneIndex）が判っていればそちらへ付ける。
        /// スキンド変換が作ったミラーは全てこの並びになっている。
        /// 対応が無ければ実体側と同じ親に落とす。
        /// </summary>
        private static int ResolveMirrorHierarchyParent(MeshContext source, bool skinned)
        {
            int parent = source.HierarchyParentIndex;
            if (!skinned) return parent;

            var model = source.ParentModelContext;
            if (model == null || parent < 0 || parent >= model.MeshContextCount) return parent;

            var parentCtx = model.GetMeshContext(parent);
            if (parentCtx == null || parentCtx.Type != MeshType.Bone) return parent;

            int peer = parentCtx.MirrorBoneIndex;
            if (peer < 0 || peer >= model.MeshContextCount) return parent;

            return peer;
        }

        /// <summary>
        /// 実体側の MeshObject から鏡像の MeshObject を作る。
        ///
        /// 【添字恒等対応】
        ///   result.Vertices[v] ↔ source.Vertices[v]
        ///   result.Faces[f]    ↔ source.Faces[f]（VertexIndices は逆順）
        ///   ミラー側への位相伝播（ApplyToMirrors）が前提にしている形を必ず満たす。
        ///
        /// 【軸・距離】
        ///   鏡映は MirrorPoint / MirrorNormal（軸＋距離）で行う。
        ///   生成ミラーの実効ワールドは ModelContext.ApplyMirrorConjugate が
        ///   S = MirrorMatrix(axis, distance) の共役 S·H·S で解くため、
        ///   ローカル頂点も同じ S で鏡像化されていなければ v_M = S·v_R が崩れる。
        ///   RebakeDerivedMirrorVertices / RebuildDerivedMirrorGeometry も同じ式。
        ///
        ///   ピボット側で距離を吸収する使い方（エクスポート時の GameObject 生成。
        ///   ローカル姿勢を MirrorLocalTRS で鏡像化して親に付ける）では
        ///   distance に 0 を渡すこと。L' = S_d·L·S_0 となるため、
        ///   頂点に掛かるのは原点まわりの反射 S_0 になる。
        /// </summary>
        /// <param name="materialOffset">ミラー側の面に足すマテリアル番号のオフセット。</param>
        /// <param name="name">生成物の名前。null なら source の名前をそのまま使う。</param>
        public static MeshObject BuildMirroredMeshObject(
            MeshObject source, int mirrorAxis, float mirrorDistance,
            int materialOffset = 0, string name = null)
        {
            if (source == null || source.Vertices == null || source.Vertices.Count == 0)
                return null;

            var result = new MeshObject
            {
                Name = name ?? source.Name
            };

            // 頂点をミラー変換してコピー
            foreach (var srcVertex in source.Vertices)
            {
                var mirrorVertex = new Vertex
                {
                    Id = srcVertex.Id,
                    Position = MirrorPoint(mirrorAxis, mirrorDistance, srcVertex.Position)
                };

                // UVをコピー
                foreach (var uv in srcVertex.UVs)
                {
                    mirrorVertex.UVs.Add(uv);
                }

                // 法線をミラー変換してコピー
                foreach (var normal in srcVertex.Normals)
                {
                    mirrorVertex.Normals.Add(MirrorNormal(mirrorAxis, normal));
                }

                // ボーンウェイト: ミラー側があればミラー側、なければ実体側
                if (srcVertex.HasMirrorBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.MirrorBoneWeight;
                }
                else if (srcVertex.HasBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.BoneWeight;
                }

                result.Vertices.Add(mirrorVertex);
            }

            // 面をコピー（頂点順序を反転して法線方向を維持）
            foreach (var srcFace in source.Faces)
            {
                var mirrorFace = new Face
                {
                    MaterialIndex = srcFace.MaterialIndex + materialOffset,
                };
                if (srcFace.IsHidden)
                    mirrorFace.SetFlag(FaceFlags.Hidden);

                // 頂点順序を反転（法線方向維持のため）
                //
                // UVIndices / NormalIndices は VertexIndices と同じ長さとは限らない。
                //   ・面ごとの法線を持たないメッシュ … NormalIndices.Count == 0
                //   ・UV を持たないメッシュ           … UVIndices.Count == 0
                // これらを VertexCount で回すと IndexOutOfRange で落ち、
                // ミラー生成が MirrorType だけ立てて中断する（リストに何も増えない）。
                // 各リストを自分の長さで独立に反転する。
                for (int i = srcFace.VertexIndices.Count - 1; i >= 0; i--)
                    mirrorFace.VertexIndices.Add(srcFace.VertexIndices[i]);

                for (int i = srcFace.UVIndices.Count - 1; i >= 0; i--)
                    mirrorFace.UVIndices.Add(srcFace.UVIndices[i]);

                for (int i = srcFace.NormalIndices.Count - 1; i >= 0; i--)
                    mirrorFace.NormalIndices.Add(srcFace.NormalIndices[i]);

                result.Faces.Add(mirrorFace);
            }

            result.InvalidatePositionCache();

            // ミラー側は実体側のウェイト（または MirrorBoneWeight）を引き継ぐ。
            // 引き継いだ結果ウェイトを持つなら、生成したミラーも SkinnedMesh 系である。
            // 種別を確定させないと、実体側だけが SkinningMatrix 経路に乗り、
            // ミラー側が WorldMatrix 経路に落ちて左右で位置がずれる。
            result.RecomputeSkinKind();
            return result;
        }


        /// <summary>
        /// 実体側インデックスに対応するミラー側インデックスを列挙して result に足す。
        ///
        /// 対象は次の2系統。
        ///   MirrorPair … pair.Real が実体側のとき pair.Mirror
        ///   ベイクミラー … BakedMirrorSourceIndex が実体側を指すコンテキスト
        ///
        /// 可視・ロックはミラー側へ伝播させる必要があるが、
        /// 姿勢（SyncDerivedMirrorTransforms）と違って自動で追随する経路が無い。
        /// 呼び出し側で実体側と同じ値を書くために使う。
        /// </summary>
        public static void CollectMirrorPeers(ModelContext model, int realIndex, List<int> result)
        {
            if (model == null || result == null) return;
            var list = model.MeshContextList;
            if (list == null || realIndex < 0 || realIndex >= list.Count) return;

            var realCtx = list[realIndex];
            if (realCtx == null) return;

            // MirrorPair
            var pair = model.GetMirrorPair(realCtx);
            if (pair != null && pair.Real == realCtx && pair.Mirror != null)
            {
                int mi = list.IndexOf(pair.Mirror);
                if (mi >= 0 && !result.Contains(mi)) result.Add(mi);
            }

            // ベイクミラー
            for (int i = 0; i < list.Count; i++)
            {
                var mc = list[i];
                if (mc == null || mc.BakedMirrorSourceIndex != realIndex) continue;
                if (!result.Contains(i)) result.Add(i);
            }
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の頂点を、実体側のローカル頂点から取り直す。
        ///
        /// ミラーの実効ワールドは S·H·S で解くので、v_M = S·v_R が
        /// 「ローカル座標で」成り立っている必要がある。実体側のローカル頂点を
        /// 書き換えたら（再局所化など）必ずこれを呼んで整合を取ること。
        ///
        /// 鏡映の軸・距離は ModelContext の実効ワールド算出と同じ値を使う。
        /// </summary>
        /// <returns>取り直したオブジェクト数</returns>
        public static int RebakeDerivedMirrorVertices(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null) return 0;

            int rebaked = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var mirrorMo = mc.MeshObject;
                var realMo   = realCtx?.MeshObject;
                if (mirrorMo?.Vertices == null || realMo?.Vertices == null) continue;
                if (mirrorMo.Vertices.Count != realMo.Vertices.Count) continue;

                int   axis = realCtx.MirrorAxis;
                float dist = realCtx.MirrorDistance;

                for (int v = 0; v < mirrorMo.Vertices.Count; v++)
                {
                    var mv = mirrorMo.Vertices[v];
                    var rv = realMo.Vertices[v];
                    if (mv == null || rv == null) continue;
                    mv.Position = MirrorPoint(axis, dist, rv.Position);
                }
                mirrorMo.InvalidatePositionCache();

                mc.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();
                mc.ApplyVertexPositionsToMesh();

                rebaked++;
            }

            return rebaked;
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の法線とスロットを、実体側から取り直す。
        ///
        /// 【必要な理由】
        /// RebakeDerivedMirrorVertices は位置しか写さない。実体側の法線を編集しても
        /// ミラー側は生成時の法線を保持したままになるため、法線編集の後に本関数を呼ぶ。
        /// ミラー側の面は選択できないので、実体側の結果を反映する以外に手段が無い。
        ///
        /// 【スロット（分割法線）の扱い】
        /// スロットは丸ごと作り直し、実体側と 1:1 の並びに揃える。
        /// 「角度で再計算」「分離」のようにスロット数が変わる操作の後でも整合が取れる。
        /// 面のインデックスは CreateDerivedMirrorContext と同じく逆順で張り直す
        /// （生成時に頂点順を反転しているため）。
        ///
        /// 【対象】
        /// MirrorGeometryDerived = true のみ。false のミラー側（PMX 系）は
        /// ファイル内に実在する独立メッシュで、実体側と頂点や面の並びが対応する
        /// 保証が無いため触らない。
        /// </summary>
        /// <param name="materialCount">
        /// UnityMesh を作り直す際のサブメッシュ数。-1 なら MeshObject 側の既定に従う。
        /// </param>
        /// <returns>取り直したオブジェクト数</returns>
        public static int RebakeDerivedMirrorNormals(
            IList<MeshContext> meshContexts, int materialCount = -1)
        {
            if (meshContexts == null) return 0;

            int rebaked = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var mirrorMo = mc.MeshObject;
                var realMo   = realCtx?.MeshObject;
                if (mirrorMo?.Vertices == null || realMo?.Vertices == null) continue;
                if (mirrorMo.Vertices.Count != realMo.Vertices.Count) continue;
                if (mirrorMo.Faces == null || realMo.Faces == null) continue;
                if (mirrorMo.Faces.Count != realMo.Faces.Count) continue;

                int axis = realCtx.MirrorAxis;

                // --- スロットを実体側と 1:1 で作り直す ---
                // GetOrAddUVNormal は重複をまとめてしまい実体側と番号がずれるため使わない。
                // 実体側の並びをそのまま写し、法線だけ軸で反転する。
                for (int v = 0; v < mirrorMo.Vertices.Count; v++)
                {
                    var mv = mirrorMo.Vertices[v];
                    var rv = realMo.Vertices[v];
                    if (mv == null || rv == null) continue;

                    mv.UVs.Clear();
                    mv.Normals.Clear();

                    for (int s = 0; s < rv.UVs.Count; s++)
                        mv.UVs.Add(rv.UVs[s]);

                    for (int s = 0; s < rv.Normals.Count; s++)
                        mv.Normals.Add(MirrorNormal(axis, rv.Normals[s]));
                }

                // --- 面のインデックスを逆順で張り直す ---
                for (int f = 0; f < mirrorMo.Faces.Count; f++)
                {
                    var mf = mirrorMo.Faces[f];
                    var rf = realMo.Faces[f];
                    if (mf == null || rf == null) continue;

                    int n = rf.VertexCount;
                    if (mf.VertexCount != n) continue;

                    // UV と法線は独立に扱う。以前は「両方そろっていなければ何もしない」
                    // だったため、法線インデックスを持たないメッシュ（MQO 由来で
                    // normalCount==0）では UV の張り直しまで丸ごと飛んでいた。
                    if (rf.UVIndices.Count >= n)
                    {
                        mf.UVIndices.Clear();
                        for (int j = n - 1; j >= 0; j--)
                            mf.UVIndices.Add(rf.UVIndices[j]);
                    }

                    if (rf.NormalIndices.Count >= n)
                    {
                        mf.NormalIndices.Clear();
                        for (int j = n - 1; j >= 0; j--)
                            mf.NormalIndices.Add(rf.NormalIndices[j]);
                    }
                }

                // --- UnityMesh へ反映 ---
                // スロット数が変わっていると法線だけの差し替えが成立しないので作り直す。
                if (mc.UnityMesh == null || !mirrorMo.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    mc.ReplaceUnityMesh(mirrorMo.ToUnityMesh(materialCount));

                rebaked++;
            }

            return rebaked;
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の形状を、実体側から丸ごと作り直す。
        ///
        /// 【必要な理由】
        /// RebakeDerivedMirrorVertices / RebakeDerivedMirrorNormals は
        /// 「頂点数（と面数）が実体側と一致していること」が前提で、頂点や面が
        /// 増減する位相変更には対応できない（両関数とも件数不一致で素通りする）。
        /// 削除を伴うツールを実体側に掛けるとミラー側が古い形状のまま取り残される
        /// ため、位相を変えたら本関数で作り直す。
        ///
        /// 【構築規則】CreateDerivedMirrorContext と同じ。
        ///   ・位置は MirrorPoint（実体側の MirrorAxis / MirrorDistance）
        ///   ・UV はそのまま、法線は MirrorNormal で反転
        ///   ・面は頂点順を反転（法線方向の維持）
        ///   ・マテリアルは実体側 + MirrorMaterialOffset
        ///   ・ボーンウェイトはミラー側があればそれ、無ければ実体側
        ///
        /// 【対象】MirrorGeometryDerived = true のみ。false のミラー側（PMX 系）は
        /// ファイル内に実在する独立メッシュで、実体側と並びが対応する保証が無いため
        /// 触らない。
        /// </summary>
        /// <returns>作り直したオブジェクト数</returns>
        public static int RebuildDerivedMirrorGeometry(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null) return 0;

            int rebuilt = 0;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var realCtx  = meshContexts[src];
                var realMo   = realCtx?.MeshObject;
                var mirrorMo = mc.MeshObject;
                if (realMo?.Vertices == null || mirrorMo == null) continue;

                int   axis      = realCtx.MirrorAxis;
                float dist      = realCtx.MirrorDistance;
                int   matOffset = realCtx.MirrorMaterialOffset;

                // --- 頂点を作り直す ---
                mirrorMo.Vertices.Clear();
                foreach (var rv in realMo.Vertices)
                {
                    if (rv == null) continue;

                    var mv = new Vertex
                    {
                        Id       = rv.Id,
                        Position = MirrorPoint(axis, dist, rv.Position),
                    };

                    for (int s = 0; s < rv.UVs.Count; s++)
                        mv.UVs.Add(rv.UVs[s]);

                    for (int s = 0; s < rv.Normals.Count; s++)
                        mv.Normals.Add(MirrorNormal(axis, rv.Normals[s]));

                    if (rv.HasMirrorBoneWeight)   mv.BoneWeight = rv.MirrorBoneWeight;
                    else if (rv.HasBoneWeight)    mv.BoneWeight = rv.BoneWeight;

                    mirrorMo.Vertices.Add(mv);
                }

                // --- 面を作り直す（頂点順を反転） ---
                mirrorMo.Faces.Clear();
                foreach (var rf in realMo.Faces)
                {
                    if (rf == null) continue;

                    var mf = new Face { MaterialIndex = rf.MaterialIndex + matOffset };
                    if (rf.IsHidden) mf.SetFlag(FaceFlags.Hidden);

                    int n = rf.VertexIndices.Count;
                    for (int j = n - 1; j >= 0; j--)
                    {
                        mf.VertexIndices.Add(rf.VertexIndices[j]);
                        mf.UVIndices.Add(j < rf.UVIndices.Count ? rf.UVIndices[j] : 0);
                        mf.NormalIndices.Add(j < rf.NormalIndices.Count ? rf.NormalIndices[j] : 0);
                    }

                    mirrorMo.Faces.Add(mf);
                }

                mirrorMo.InvalidatePositionCache();

                // 実体側から引き継いだウェイトに合わせて種別を確定させる。
                mirrorMo.RecomputeSkinKind();

                // 消えた頂点・面を指したままの選択を残さない。
                mc.Selection?.ClearAll();

                mc.ReplaceUnityMesh(mirrorMo.ToUnityMesh());
                mc.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();

                rebuilt++;
            }

            return rebuilt;
        }

        /// <summary>
        /// ModelContext 版。形状を作り直したうえで MirrorPair を張り直す。
        /// 位相が変わると MirrorPair の頂点対応表が古くなるため。
        /// </summary>
        /// <returns>作り直したオブジェクト数</returns>
        public static int RebuildDerivedMirrorGeometry(ModelContext model)
        {
            if (model?.MeshContextList == null) return 0;

            int rebuilt = RebuildDerivedMirrorGeometry(model.MeshContextList);
            if (rebuilt == 0) return 0;

            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    if (pair?.Mirror == null || !pair.Mirror.MirrorGeometryDerived) continue;
                    if (!pair.Build())
                        Debug.LogWarning($"[Mirror] ペアの張り直しに失敗しました mirror=\"{pair.Mirror.Name}\"");
                }
            }

            return rebuilt;
        }

        /// <summary>
        /// Undo スナップショットの対象に、実体側とそのミラー側の両方を含めた索引を返す。
        /// 位相変更ツールはミラー側も作り直すため、片側だけ記録すると Undo で食い違う。
        /// </summary>
        public static List<int> CollectMirrorCaptureIndices(
            ModelContext model, IEnumerable<int> realIndices)
        {
            var result = new List<int>();
            if (model == null || realIndices == null) return result;

            foreach (int idx in realIndices)
            {
                if (idx < 0) continue;
                if (!result.Contains(idx)) result.Add(idx);
                CollectMirrorPeers(model, idx, result);
            }

            return result;
        }

        // ================================================================
        // ミラー側への位相変更の伝播（3系統共通）
        //
        // 【RebuildDerivedMirrorGeometry と別に置く理由】
        //   同関数は MirrorGeometryDerived == true だけを対象にする。この値は
        //   「実効ワールドに共役 S·H·S を掛けるか」という描画側の都合で決まり、
        //   MeshFilterToSkinnedConverter は変換時に無条件で false を書く。
        //   PMX 経路も設定しないため false のまま。その結果、
        //   「ユーザーが重みを塗る側のミラー」が丸ごと対象外になっていた。
        //
        //   ミラーの連結そのものの正本は MirrorPairs と BakedMirrorSourceIndex で、
        //   頂点移動の同期（PlayerViewportManager.SyncMeshPositionsAndTransform）も
        //   この2つしか見ていない。ここでも CollectMirrorPeers に一本化する。
        //
        // 【前提: 添字恒等対応】
        //   real.Vertices[v] ↔ mirror.Vertices[v]
        //   real.Faces[f]    ↔ mirror.Faces[f]
        //   mirror.Faces[f].VertexIndices は real 側の逆順
        //
        //   生成ミラー（CreateDerivedMirrorContext）は必ず成立する。スキンド変換は
        //   頂点の並べ替え・増減をせず Position に行列を掛けるだけなので変換後も
        //   保たれる。ファイル由来のミラーは保証が無いため、位相を変える前に
        //   VerifyIdentityCorrespondence で実測し、成立しないペアは触らない。
        //
        // 【伝播のやり方】
        //   ミラー側の頂点・面を実体側から作り直すことはしない。実体側に掛けたのと
        //   同じ位相操作を、同じ添字でミラー側にも掛ける（ApplyToMirrors）。
        //
        //   前提 A が成り立っていれば、両側で同じ面添字・同じ頂点添字を消し、
        //   生き残った面の VertexIndices に同じ再マップを掛けるだけで
        //     mirror'.Faces[f].VertexIndices[j]
        //       = map[ mirror.Faces[f].VertexIndices[j] ]
        //       = map[ real.Faces[f].VertexIndices[n-1-j] ]
        //       = real'.Faces[f].VertexIndices[n-1-j]
        //   となり前提 A がそのまま保たれる。
        //
        //   UVIndices / NormalIndices は頂点内のスロット番号であって頂点添字では
        //   ないため、頂点の削除・再マップの影響を受けない。ミラー側の頂点
        //   オブジェクト（位置・UV・法線・ボーンウェイト）は生き残ったものを
        //   そのまま残すだけなので、実体側から写す作業は一切要らない。
        //
        // 【面内の巻き順の正規化】
        //   面を張り替える操作（Tri4To1 / FaceMerge / VertexDissolve 等）は
        //   面の巻き順に沿って外周を辿るため、ミラー側では逆回りに辿ることになり、
        //   結果の並びが「実体側の逆順」を巡回回転した形になることがある。
        //   幾何としては同じ面だが前提 A の「厳密な逆順」からは外れるため、
        //   操作後に NormalizeMirrorFaceOrder で回転を戻して厳密形に揃える。
        // ================================================================

        /// <summary>
        /// ミラー側への位相変更の伝播計画。位相を変える前に作ること。
        /// </summary>
        public sealed class MirrorRebuildPlan
        {
            public sealed class Entry
            {
                /// <summary>実体側の MeshContextList 索引。</summary>
                public int RealIndex;

                /// <summary>ミラー側の MeshContextList 索引。</summary>
                public int MirrorIndex;

                /// <summary>添字恒等対応が実測で成立したか。false のペアは触らない。</summary>
                public bool Verified;

                /// <summary>成立しなかった理由（Verified == false のとき）。</summary>
                public string RejectReason;
            }

            public readonly List<Entry> Entries = new List<Entry>();

            /// <summary>検証を通ったペア数。</summary>
            public int VerifiedCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Entries.Count; i++) if (Entries[i].Verified) n++;
                    return n;
                }
            }

            /// <summary>検証を落ちたペア数。</summary>
            public int RejectedCount => Entries.Count - VerifiedCount;
        }

        /// <summary>
        /// 実体側の索引からミラー側を引き当て、添字恒等対応の成否を実測して控える。
        /// 位相を変える「前」に呼ぶこと（変更後では対応の検証ができない）。
        /// </summary>
        public static MirrorRebuildPlan CaptureMirrorRebuildPlan(
            ModelContext model, IEnumerable<int> realIndices)
        {
            var plan = new MirrorRebuildPlan();
            if (model?.MeshContextList == null || realIndices == null) return plan;

            var list = model.MeshContextList;
            var seen = new HashSet<int>();

            foreach (int realIndex in realIndices)
            {
                if (realIndex < 0 || realIndex >= list.Count) continue;

                var peers = new List<int>();
                CollectMirrorPeers(model, realIndex, peers);

                foreach (int mirrorIndex in peers)
                {
                    if (mirrorIndex < 0 || mirrorIndex >= list.Count) continue;
                    if (mirrorIndex == realIndex) continue;
                    if (!seen.Add(mirrorIndex)) continue;   // 同じミラーを二重に扱わない

                    var entry = new MirrorRebuildPlan.Entry
                    {
                        RealIndex   = realIndex,
                        MirrorIndex = mirrorIndex,
                    };

                    var realMo   = list[realIndex]?.MeshObject;
                    var mirrorMo = list[mirrorIndex]?.MeshObject;

                    string reason;
                    entry.Verified     = VerifyIdentityCorrespondence(realMo, mirrorMo, out reason);
                    entry.RejectReason = reason;

                    plan.Entries.Add(entry);
                }
            }

            return plan;
        }

        /// <summary>
        /// 実体側とミラー側が添字恒等対応（面内の巻き順は逆順）になっているかを実測する。
        /// </summary>
        private static bool VerifyIdentityCorrespondence(
            MeshObject realMo, MeshObject mirrorMo, out string reason)
        {
            reason = "";

            if (realMo == null)   { reason = "実体側の MeshObject が null";   return false; }
            if (mirrorMo == null) { reason = "ミラー側の MeshObject が null"; return false; }

            if (realMo.Vertices == null || mirrorMo.Vertices == null)
            { reason = "Vertices が null"; return false; }
            if (realMo.Faces == null || mirrorMo.Faces == null)
            { reason = "Faces が null"; return false; }

            if (realMo.Vertices.Count != mirrorMo.Vertices.Count)
            {
                reason = $"頂点数が不一致 real={realMo.Vertices.Count} mirror={mirrorMo.Vertices.Count}";
                return false;
            }
            if (realMo.Faces.Count != mirrorMo.Faces.Count)
            {
                reason = $"面数が不一致 real={realMo.Faces.Count} mirror={mirrorMo.Faces.Count}";
                return false;
            }

            for (int v = 0; v < realMo.Vertices.Count; v++)
            {
                if (realMo.Vertices[v] == null || mirrorMo.Vertices[v] == null)
                { reason = $"頂点 {v} が null"; return false; }
            }

            for (int f = 0; f < realMo.Faces.Count; f++)
            {
                var rf = realMo.Faces[f];
                var mf = mirrorMo.Faces[f];
                if (rf == null || mf == null) { reason = $"面 {f} が null"; return false; }
                if (rf.VertexIndices == null || mf.VertexIndices == null)
                { reason = $"面 {f} の VertexIndices が null"; return false; }

                int n = rf.VertexIndices.Count;
                if (mf.VertexIndices.Count != n)
                {
                    reason = $"面 {f} の頂点数が不一致 real={n} mirror={mf.VertexIndices.Count}";
                    return false;
                }

                for (int j = 0; j < n; j++)
                {
                    if (mf.VertexIndices[j] != rf.VertexIndices[n - 1 - j])
                    {
                        reason = $"面 {f} が逆順恒等でない "
                               + $"(slot {j}: mirror={mf.VertexIndices[j]} real={rf.VertexIndices[n - 1 - j]})";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 実体側に掛けたのと同じ位相操作を、検証を通ったミラー側にも掛ける。
        /// MirrorGeometryDerived は見ない（3系統すべてが対象）。
        ///
        /// plan は位相を変える「前」に CaptureMirrorRebuildPlan で取っておくこと。
        /// 本メソッドは実体側の操作が終わった「後」に呼ぶ。ミラー側はまだ変更前の
        /// 状態なので、操作に渡す添字は変更前のものをそのまま使える。
        /// </summary>
        /// <param name="apply">
        /// (実体側の索引, ミラー側の MeshObject) を受け取り、実体側と同じ操作を
        /// ミラー側に掛ける。成功したら true。
        /// </param>
        /// <returns>更新したミラー側の数</returns>
        public static int ApplyToMirrors(
            ModelContext model,
            MirrorRebuildPlan plan,
            Func<int, MeshObject, bool> apply)
        {
            if (model?.MeshContextList == null || plan == null || apply == null) return 0;

            var list = model.MeshContextList;
            int materialCount = model.MaterialCount;
            int applied = 0;

            foreach (var entry in plan.Entries)
            {
                if (!entry.Verified)
                {
                    Debug.LogWarning(
                        "[Mirror] 添字恒等対応が成立しないためミラー側へ伝播しませんでした。"
                      + $" real=[{entry.RealIndex}]\"{SafeContextName(list, entry.RealIndex)}\""
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\""
                      + $" 理由: {entry.RejectReason}");
                    continue;
                }

                var realCtx   = SafeContext(list, entry.RealIndex);
                var mirrorCtx = SafeContext(list, entry.MirrorIndex);
                var realMo    = realCtx?.MeshObject;
                var mirrorMo  = mirrorCtx?.MeshObject;
                if (realMo == null || mirrorMo == null) continue;

                if (!apply(entry.RealIndex, mirrorMo))
                {
                    Debug.LogError(
                        "[Mirror] ミラー側への操作が失敗しました。左右が食い違ったままです。"
                      + $" real=[{entry.RealIndex}]\"{SafeContextName(list, entry.RealIndex)}\""
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\"");
                    continue;
                }

                // 面を張り替える操作は巻き順に沿って外周を辿るため、ミラー側の結果が
                // 「実体側の逆順」を巡回回転した形になることがある。厳密形へ戻す。
                int rotated = NormalizeMirrorFaceOrder(realMo, mirrorMo, out string normError);
                if (normError != null)
                {
                    Debug.LogError(
                        $"[Mirror] ミラー側の面の並びを揃えられませんでした: {normError}"
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\"");
                }

                // 操作後にもう一度、前提が保たれているかを実測する。
                if (!VerifyIdentityCorrespondence(realMo, mirrorMo, out string afterReason))
                {
                    Debug.LogError(
                        "[Mirror] 操作後に添字恒等対応が崩れました。Undo で戻してください。"
                      + $" mirror=[{entry.MirrorIndex}]\"{SafeContextName(list, entry.MirrorIndex)}\""
                      + $" 理由: {afterReason}");
                }

                if (rotated > 0)
                    Debug.Log($"[Mirror] ミラー側の面 {rotated} 枚の並びを厳密な逆順へ揃えました"
                            + $" mirror=\"{SafeContextName(list, entry.MirrorIndex)}\"");

                mirrorMo.InvalidatePositionCache();

                // 消えた頂点・面を指したままの選択を残さない。
                mirrorCtx.Selection?.ClearAll();

                mirrorCtx.ReplaceUnityMesh(mirrorMo.ToUnityMesh(materialCount));
                mirrorCtx.OriginalPositions = (Vector3[])mirrorMo.Positions.Clone();

                applied++;
            }

            if (applied > 0)
            {
                RebuildAffectedMirrorPairs(model, plan);

                // 実体側の形が変わったので、ミラー側モーフを Real 側モーフから作り直す。
                // 頂点編集系ツール（DeleteSelection / FaceMerge / VertexDissolve /
                // Tri4To1 / FaceMergeCollapse）は全てここを通るため、
                // モーフ同期の共通の合流点になる。
                // 規約は MorphMirrorPolicy.cs を正典とする。
                model.SyncAllMirrorMorphs();
            }

            return applied;
        }

        /// <summary>
        /// ミラー側の各面の並びを「実体側の厳密な逆順」へ回転で揃える。
        /// 巡回回転で一致しない面があれば error に理由を入れる（回転はしない）。
        /// UVIndices / NormalIndices も同じ回転量で揃える（長さが n のときのみ）。
        /// </summary>
        /// <returns>回転した面の数</returns>
        private static int NormalizeMirrorFaceOrder(
            MeshObject realMo, MeshObject mirrorMo, out string error)
        {
            error = null;

            if (realMo?.Faces == null || mirrorMo?.Faces == null)
            {
                error = "Faces が null";
                return 0;
            }
            if (realMo.Faces.Count != mirrorMo.Faces.Count)
            {
                error = $"面数が不一致 real={realMo.Faces.Count} mirror={mirrorMo.Faces.Count}";
                return 0;
            }

            int rotatedCount = 0;

            for (int f = 0; f < realMo.Faces.Count; f++)
            {
                var rf = realMo.Faces[f];
                var mf = mirrorMo.Faces[f];
                if (rf?.VertexIndices == null || mf?.VertexIndices == null)
                {
                    error = $"面 {f} が null";
                    return rotatedCount;
                }

                int n = rf.VertexIndices.Count;
                if (mf.VertexIndices.Count != n)
                {
                    error = $"面 {f} の頂点数が不一致 real={n} mirror={mf.VertexIndices.Count}";
                    return rotatedCount;
                }
                if (n == 0) continue;

                int r = FindReverseRotation(rf.VertexIndices, mf.VertexIndices, n);
                if (r < 0)
                {
                    error = $"面 {f} が巡回回転を含めても逆順一致しません";
                    return rotatedCount;
                }
                if (r == 0) continue;

                RotateLeft(mf.VertexIndices, r, n);
                if (mf.UVIndices     != null && mf.UVIndices.Count     == n) RotateLeft(mf.UVIndices,     r, n);
                if (mf.NormalIndices != null && mf.NormalIndices.Count == n) RotateLeft(mf.NormalIndices, r, n);

                rotatedCount++;
            }

            return rotatedCount;
        }

        /// <summary>
        /// mirror[j] == real[(n-1-j+r) % n] が全 j で成り立つ r を返す。無ければ -1。
        /// </summary>
        private static int FindReverseRotation(List<int> real, List<int> mirror, int n)
        {
            for (int r = 0; r < n; r++)
            {
                bool ok = true;
                for (int j = 0; j < n; j++)
                {
                    int k = ((n - 1 - j + r) % n + n) % n;
                    if (mirror[j] != real[k]) { ok = false; break; }
                }
                if (ok) return r;
            }
            return -1;
        }

        /// <summary>リストを左へ r 個ぶん回転する（先頭が元の r 番目になる）。</summary>
        private static void RotateLeft(List<int> listToRotate, int r, int n)
        {
            if (r <= 0 || n <= 1) return;

            var tmp = new int[n];
            for (int j = 0; j < n; j++) tmp[j] = listToRotate[(j + r) % n];
            for (int j = 0; j < n; j++) listToRotate[j] = tmp[j];
        }

        private static MeshContext SafeContext(IList<MeshContext> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count) return null;
            return list[index];
        }

        private static string SafeContextName(IList<MeshContext> list, int index)
        {
            var mc = SafeContext(list, index);
            return mc?.Name ?? "<範囲外/null>";
        }

        /// <summary>
        /// 作り直したミラー側を含む MirrorPair の対応表を張り直す。
        /// 位相が変わると VertexMap（件数一致が前提）が古くなるため。
        /// </summary>
        private static void RebuildAffectedMirrorPairs(ModelContext model, MirrorRebuildPlan plan)
        {
            if (model?.MirrorPairs == null) return;

            var list = model.MeshContextList;
            if (list == null) return;

            foreach (var entry in plan.Entries)
            {
                if (!entry.Verified) continue;

                var mirrorCtx = SafeContext(list, entry.MirrorIndex);
                if (mirrorCtx == null) continue;

                foreach (var pair in model.MirrorPairs)
                {
                    if (pair == null || pair.Mirror != mirrorCtx) continue;
                    if (!pair.Build())
                        Debug.LogWarning($"[Mirror] ペアの張り直しに失敗しました mirror=\"{mirrorCtx.Name}\"");
                }
            }
        }

        /// <summary>
        /// ミラー軸で法線を反転する。軸コードは MirrorPoint と同じ体系
        /// （2 = Y / 4 = Z / それ以外 = X）。法線は方向なので距離は使わない。
        /// </summary>
        public static Vector3 MirrorNormal(int mirrorAxis, Vector3 normal)
        {
            switch (mirrorAxis)
            {
                case 2:  return new Vector3(normal.x, -normal.y, normal.z);
                case 4:  return new Vector3(normal.x, normal.y, -normal.z);
                default: return new Vector3(-normal.x, normal.y, normal.z);
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
