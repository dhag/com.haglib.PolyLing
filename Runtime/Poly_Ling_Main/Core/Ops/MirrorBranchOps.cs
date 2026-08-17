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
        {
            if (source == null || source.MeshObject == null || !source.IsMirrored)
                return null;

            var srcMeshObj = source.MeshObject;

            if (srcMeshObj.Vertices.Count == 0)
                return null;
            var axis = source.GetMirrorSymmetryAxis();

            // 新しいMeshObjectを作成
            var mirrorMeshObj = new MeshObject
            {
                Name = source.Name + "_BakedMirror",
                Type = MeshType.BakedMirror  // 明示的に設定
            };

            // 頂点をミラー変換してコピー
            foreach (var srcVertex in srcMeshObj.Vertices)
            {
                var mirrorVertex = new Vertex
                {
                    Id = srcVertex.Id,
                    Position = MirrorLocalPosition(srcVertex.Position, axis)
                };

                // UVをコピー
                foreach (var uv in srcVertex.UVs)
                {
                    mirrorVertex.UVs.Add(uv);
                }

                // 法線をミラー変換してコピー
                foreach (var normal in srcVertex.Normals)
                {
                    mirrorVertex.Normals.Add(MirrorLocalNormal(normal, axis));
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

                mirrorMeshObj.Vertices.Add(mirrorVertex);
            }

            // 面をコピー（頂点順序を反転して法線方向を維持）
            foreach (var srcFace in srcMeshObj.Faces)
            {
                var mirrorFace = new Face
                {
                    MaterialIndex = srcFace.MaterialIndex + source.MirrorMaterialOffset,
                };
                if (srcFace.IsHidden)
                    mirrorFace.SetFlag(FaceFlags.Hidden);

                // 頂点順序を反転（法線方向維持のため）
                for (int i = srcFace.VertexCount - 1; i >= 0; i--)
                {
                    mirrorFace.VertexIndices.Add(srcFace.VertexIndices[i]);
                    mirrorFace.UVIndices.Add(srcFace.UVIndices[i]);
                    mirrorFace.NormalIndices.Add(srcFace.NormalIndices[i]);
                }

                mirrorMeshObj.Faces.Add(mirrorFace);
            }

            // 姿勢は実体側と同一にする。
            // ミラー側は自前の姿勢を持たず、実効ワールドは ComputeWorldMatrices が
            // S·H·S として算出する。ここで H を実体側と揃えておかないと
            // v_M = S·v_R の不変条件が崩れる。
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
                MirrorGeometryDerived = true,
                // 階層情報は元メッシュに合わせる
                ParentIndex = source.ParentIndex,
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

        /// <summary>ローカル位置を軸で鏡像化する。</summary>
        private static Vector3 MirrorLocalPosition(Vector3 pos, Poly_Ling.Symmetry.SymmetryAxis axis)
        {
            switch (axis)
            {
                case Poly_Ling.Symmetry.SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
                default: return new Vector3(-pos.x, pos.y, pos.z);
            }
        }

        /// <summary>ローカル法線を軸で鏡像化する。</summary>
        private static Vector3 MirrorLocalNormal(Vector3 normal, Poly_Ling.Symmetry.SymmetryAxis axis)
        {
            switch (axis)
            {
                case Poly_Ling.Symmetry.SymmetryAxis.X: return new Vector3(-normal.x, normal.y, normal.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Y: return new Vector3(normal.x, -normal.y, normal.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Z: return new Vector3(normal.x, normal.y, -normal.z);
                default: return new Vector3(-normal.x, normal.y, normal.z);
            }
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
                    if (rf.UVIndices.Count < n || rf.NormalIndices.Count < n) continue;

                    mf.UVIndices.Clear();
                    mf.NormalIndices.Clear();

                    for (int j = n - 1; j >= 0; j--)
                    {
                        mf.UVIndices.Add(rf.UVIndices[j]);
                        mf.NormalIndices.Add(rf.NormalIndices[j]);
                    }
                }

                // --- UnityMesh へ反映 ---
                // スロット数が変わっていると法線だけの差し替えが成立しないので作り直す。
                if (mc.UnityMesh == null || !mirrorMo.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    mc.UnityMesh = mirrorMo.ToUnityMesh(materialCount);

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

                // 消えた頂点・面を指したままの選択を残さない。
                mc.Selection?.ClearAll();

                mc.UnityMesh        = mirrorMo.ToUnityMesh();
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
