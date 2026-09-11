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
//
// 【分割先】このファイルから次へ分けてある。
//   MirrorBranchOps.Create.cs     ミラー分岐：生成ミラーの作成。
//   MirrorBranchOps.Propagate.cs  ミラー分岐：ミラー側への位相変更の伝播（3 系統共通）。
//   MirrorBranchPlan.cs           ミラー分岐の索引対応・許容差・出力計画（MirrorBranchOps から分離）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{

    public static partial class MirrorBranchOps
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
    }
}
