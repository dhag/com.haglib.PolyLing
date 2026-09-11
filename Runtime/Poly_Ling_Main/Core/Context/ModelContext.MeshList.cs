// ModelContext.MeshList.cs
// ModelContext：メッシュリスト操作・全体操作・複製・バウンディングボックス・ワールド変換行列。
// Runtime/Poly_Ling_Main/Core/Context/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

namespace Poly_Ling.Context
{
    public partial class ModelContext
    {
        // ================================================================
        // メッシュリスト操作
        // ================================================================

        /// <summary>メッシュを追加</summary>
        /// <returns>追加されたインデックス</returns>
        public int Add(MeshContext meshContext)
        {
            if (meshContext == null)
                throw new ArgumentNullException(nameof(meshContext));

            // 協働編集用の安定ID。未割当(0)のときだけ発行する。
            // Undo/Redo の復元は既にIDを持った実体を挿し戻すので、ここでは変化しない。
            if (meshContext.ObjectId == Poly_Ling.Data.ObjectIdAllocator.Unassigned)
                meshContext.ObjectId = Poly_Ling.Data.ObjectIdAllocator.Next();

            meshContext.ParentModelContext = this;
            MeshContextList.Add(meshContext);
            InvalidateTypedIndices();
            IsDirty = true;
            return MeshContextList.Count - 1;
        }

        /// <summary>メッシュを挿入</summary>
        /// <param name="adjustSelection">選択インデックスを調整するか（Undo/Redo時はfalse）</param>
        public void Insert(int index, MeshContext meshContext, bool adjustSelection = true)
        {
            if (meshContext == null)
                throw new ArgumentNullException(nameof(meshContext));
            if (index < 0 || index > MeshContextList.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            // 複製で作られた新規 MeshContext は ObjectId==0 なのでここで新IDを得る。
            // Undo/Redo の挿し戻しは既存IDを保つ（＝担当や参照が維持される）。
            if (meshContext.ObjectId == Poly_Ling.Data.ObjectIdAllocator.Unassigned)
                meshContext.ObjectId = Poly_Ling.Data.ObjectIdAllocator.Next();

            meshContext.ParentModelContext = this;
            MeshContextList.Insert(index, meshContext);
            InvalidateTypedIndices();
            IsDirty = true;

            // 索引で他要素を指している参照（親・ミラー元・Humanoid 割当・Tポーズ退避）を
            // 挿入分だけ繰り下げる。ここを飛ばすと親子が迷子になり、depth からの
            // 復元に頼らないと階層が壊れたままになる。
            //   ※挿入した meshContext 自身の親・ミラー元も「挿入前の索引」で
            //     設定されている前提で一緒に付け替える（EnableMirror がそう書く）。
            //   ※adjustSelection==false は Undo/Redo の挿し戻し。スナップショットが
            //     既に正しい索引を持っているので、ここで動かすと二重適用になる。
            if (adjustSelection)
                RemapIndexReferences(BuildInsertMap(MeshContextList.Count - 1, index));

            if (adjustSelection)
            {
                // 選択インデックス調整（挿入位置以降は+1）- 各カテゴリ個別に
                SelectedDrawableMeshIndices = AdjustIndicesForInsert(SelectedDrawableMeshIndices, index);
                SelectedBoneIndices = AdjustIndicesForInsert(SelectedBoneIndices, index);
                SelectedMorphIndices = AdjustIndicesForInsert(SelectedMorphIndices, index);
            }

            // モーフエクスプレッションのインデックス調整（常に実行）
            foreach (var set in MorphExpressions)
            {
                set.AdjustIndicesOnInsert(index);
            }
        }

        /// <summary>挿入時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForInsert(List<int> indices, int insertIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
            {
                adjusted.Add(i >= insertIndex ? i + 1 : i);
            }
            return adjusted;
        }

        /// <summary>メッシュを削除</summary>
        /// <param name="adjustSelection">選択インデックスを調整するか（Undo/Redo時はfalse）</param>
        /// <returns>削除成功したか</returns>
        public bool RemoveAt(int index, bool adjustSelection = true)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return false;

            // 付け替え表は削除前に作る（削除された親は祖父へ繰り上げるため、
            // 削除前の親子関係を読む必要がある）。
            // adjustSelection==false は Undo/Redo の巻き戻しなので付け替えない。
            int[] removeMap = adjustSelection ? BuildRemoveMap(index) : null;

            MeshContextList.RemoveAt(index);
            InvalidateTypedIndices();
            IsDirty = true;

            if (removeMap != null) RemapIndexReferences(removeMap);

            if (adjustSelection)
            {
                // 選択インデックス調整 - 各カテゴリ個別に
                SelectedDrawableMeshIndices = AdjustIndicesForRemove(SelectedDrawableMeshIndices, index);
                SelectedBoneIndices = AdjustIndicesForRemove(SelectedBoneIndices, index);
                SelectedMorphIndices = AdjustIndicesForRemove(SelectedMorphIndices, index);

                ValidateSelection();
            }

            // モーフエクスプレッションのインデックス調整（常に実行）
            foreach (var set in MorphExpressions)
            {
                set.AdjustIndicesOnRemove(index);
            }

            return true;
        }

        /// <summary>削除時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForRemove(List<int> indices, int removeIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
            {
                if (i < removeIndex)
                    adjusted.Add(i);
                else if (i > removeIndex)
                    adjusted.Add(i - 1);
                // i == removeIndex の場合は削除されるので追加しない
            }
            return adjusted;
        }

        /// <summary>メッシュを移動（順序変更）</summary>
        /// <returns>移動成功したか</returns>
        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= MeshContextList.Count)
                return false;
            if (toIndex < 0 || toIndex >= MeshContextList.Count)
                return false;
            if (fromIndex == toIndex)
                return false;

            var meshContext = MeshContextList[fromIndex];
            MeshContextList.RemoveAt(fromIndex);
            MeshContextList.Insert(toIndex, meshContext);

            RemapIndexReferences(BuildMoveMap(MeshContextList.Count, fromIndex, toIndex));

            // 選択インデックス調整 - 各カテゴリ個別に
            SelectedDrawableMeshIndices = AdjustIndicesForMove(SelectedDrawableMeshIndices, fromIndex, toIndex);
            SelectedBoneIndices = AdjustIndicesForMove(SelectedBoneIndices, fromIndex, toIndex);
            SelectedMorphIndices = AdjustIndicesForMove(SelectedMorphIndices, fromIndex, toIndex);

            InvalidateTypedIndices();
            IsDirty = true;
            return true;
        }

        /// <summary>移動時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForMove(List<int> indices, int fromIndex, int toIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
            {
                if (i == fromIndex)
                {
                    adjusted.Add(toIndex);
                }
                else if (fromIndex < i && toIndex >= i)
                {
                    adjusted.Add(i - 1);
                }
                else if (fromIndex > i && toIndex <= i)
                {
                    adjusted.Add(i + 1);
                }
                else
                {
                    adjusted.Add(i);
                }
            }
            return adjusted;
        }

        /// <summary>インデックスでメッシュコンテキストを取得</summary>
        public MeshContext GetMeshContext(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return null;
            return MeshContextList[index];
        }

        /// <summary>メッシュコンテキストのインデックスを取得</summary>
        public int IndexOf(MeshContext meshContext)
        {
            return MeshContextList.IndexOf(meshContext);
        }

        // ================================================================
        // 全体操作
        // ================================================================

        /// <summary>全メッシュをクリア</summary>
        /// <param name="destroyMeshes">Unity Meshリソースを破棄するか</param>
        public void Clear(bool destroyMeshes = true)
        {
            if (destroyMeshes)
            {
                foreach (var meshContext in MeshContextList)
                {
                    if (meshContext.UnityMesh != null)
                        UnityEngine.Object.DestroyImmediate(meshContext.UnityMesh);
                }
            }

            MeshContextList.Clear();
            MirrorPairs.Clear();
            SpringBoneColliderGroupNames.Clear();
            SpringBoneFixedDeltaTime = 0f;
            SpringBoneWarmupFrames = 3;
            VrmMeta = null;
            VrmLookAt = null;
            AvatarRetarget = null;
            CoordinateConvention = null;
            ClearSpringBoneHighlight();
            ClearAllCategorySelection();
            InvalidateTypedIndices();
            IsDirty = true;
        }

        /// <summary>新規モデルとしてリセット</summary>
        public void Reset(string name = "Untitled")
        {
            Clear();
            Name = name;
            FilePath = null;
            IsDirty = false;
            WorkPlane?.Reset();
            WorkAxis?.Reset();
            SymmetrySettings?.Reset();
            _humanoidMapping?.ClearAll();
        }

        // ================================================================
        // 複製
        // ================================================================

        /// <summary>指定メッシュコンテキストを複製</summary>
        /// <returns>複製されたメッシュコンテキストのインデックス、失敗時は-1</returns>
        /// <remarks>MeshContext.Clone()が必要。Phase 2以降で実装</remarks>
        public int Duplicate(int index)
        {
            // TODO: MeshContext.Clone()を実装後に有効化
            throw new NotImplementedException("MeshContext.Clone() is required");
        }

        // ================================================================
        // バウンディングボックス
        // ================================================================

        /// <summary>全メッシュのバウンディングボックスを計算</summary>
        public Bounds CalculateBounds()
        {
            if (MeshContextList.Count == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds? combinedBounds = null;

            foreach (var meshContext in MeshContextList)
            {
                if (meshContext.MeshObject == null)
                    continue;

                var meshContextBounds = meshContext.MeshObject.CalculateBounds();

                if (!combinedBounds.HasValue)
                {
                    combinedBounds = meshContextBounds;
                }
                else
                {
                    var bounds = combinedBounds.Value;
                    bounds.Encapsulate(meshContextBounds);
                    combinedBounds = bounds;
                }
            }

            return combinedBounds ?? new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>選択中の全メッシュのバウンディングボックス</summary>
        public Bounds CalculateCurrentBounds()
        {
            Bounds? combined = null;
            foreach (var mc in SelectedMeshContexts)
            {
                if (mc?.MeshObject == null) continue;
                var b = mc.MeshObject.CalculateBounds();
                combined = combined.HasValue
                    ? new Bounds(
                        (combined.Value.center + b.center) * 0.5f,
                        Vector3.Max(combined.Value.max, b.max) - Vector3.Min(combined.Value.min, b.min))
                    : b;
            }
            return combined ?? new Bounds(Vector3.zero, Vector3.one);
        }

        // ================================================================
        // ワールド変換行列計算
        // ================================================================

        /// <summary>
        /// 全MeshContextのワールド変換行列を計算
        /// HierarchyParentIndexに基づいて親子関係を解決し、
        /// 累積変換行列をWorldMatrixに設定する
        /// </summary>
        public void ComputeWorldMatrices()
        {
            if (MeshContextList == null || MeshContextList.Count == 0)
                return;

            // 生成ミラーは自前の姿勢を持たない。実体側から必ず引き写しておく。
            // 値コピーなので、実体側だけを動かす操作（原点CSV読込・姿勢くさび取込・
            // オブジェクト姿勢ツール等）のあとは放っておくとずれる。
            // ワールド行列を組む直前にここで一括同期しておけば、どの経路から
            // 変更されても H_M = H_R が保たれる。
            SyncDerivedMirrorTransforms(MeshContextList);

            // 階層ワールド（鏡像を掛ける前）。子はこちらを親として積む。
            var hierarchyWorld = new Matrix4x4[MeshContextList.Count];
            for (int i = 0; i < hierarchyWorld.Length; i++) hierarchyWorld[i] = Matrix4x4.identity;

            // トポロジカルソートして親から順に処理
            var sortedIndices = TopologicalSortByHierarchy();

            foreach (int index in sortedIndices)
            {
                var ctx = MeshContextList[index];
                if (ctx == null) continue;

                Matrix4x4 localMatrix = ctx.LocalMatrix;
                int parentIndex = ctx.HierarchyParentIndex;

                Matrix4x4 h = (parentIndex >= 0 && parentIndex < MeshContextList.Count)
                    ? hierarchyWorld[parentIndex] * localMatrix   // 親のワールド × 自身のローカル
                    : localMatrix;                                // ルート

                hierarchyWorld[index] = h;

                // ミラー側は実効ワールドを共役 S·H·S にする。
                //
                // ミラー側メッシュの頂点は実体側の素直な鏡像（v_M = S·v_R）として
                // 焼き込まれており、姿勢は持たない（実体側と同じ階層ワールドを取る）。
                // このとき
                //   M_world = (S·H·S)·v_M = (S·H·S)·(S·v_R) = S·H·v_R = S·(R_world)
                // となり、階層のどこを動かしても鏡像関係が保たれる。
                // S の共役なのでミラー面が原点を通るかどうかに依存せず、
                // det(S·H·S) = det(H) > 0 なので面の向きも変わらない。
                //
                // ここで WorldMatrix に入れておくことで、描画・エクスポート・
                // ピッキング・各ツールが無改修で正しい行列を見る。
                ctx.WorldMatrix = (ctx.MirrorGeometryDerived && MirrorBranchOps.IsMirrorSideContext(ctx))
                    ? ApplyMirrorConjugate(h, ctx)
                    : h;

                // 逆行列をキャッシュ
                ctx.WorldMatrixInverse = ctx.WorldMatrix.inverse;
            }
        }

        /// <summary>
        /// 生成ミラー（MirrorGeometryDerived）の姿勢と親を、実体側から引き写す。
        ///
        /// ミラー側は姿勢を持たない設計（実効ワールドは S·H·S で解く）なので、
        /// H_M = H_R でなければならない。BoneTransform は値コピーで作られており
        /// 参照共有ではないため、実体側だけが変わるとずれる。
        /// ワールド行列の算出前に毎回そろえる。
        /// </summary>
        public static void SyncDerivedMirrorTransforms(List<MeshContext> meshContexts)
        {
            if (meshContexts == null) return;

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mc = meshContexts[i];
                if (mc == null || !mc.MirrorGeometryDerived) continue;

                int src = mc.BakedMirrorSourceIndex;
                if (src < 0 || src >= meshContexts.Count) continue;

                var real = meshContexts[src];
                if (real?.BoneTransform == null || mc.BoneTransform == null) continue;

                mc.BoneTransform.Position          = real.BoneTransform.Position;
                mc.BoneTransform.Rotation          = real.BoneTransform.Rotation;
                mc.BoneTransform.Scale             = real.BoneTransform.Scale;
                mc.BoneTransform.UseLocalTransform = real.BoneTransform.UseLocalTransform;

                // 親が違うと H そのものが変わる。実体側の兄弟であるべきなので合わせる。
                mc.HierarchyParentIndex = real.HierarchyParentIndex;
            }
        }

        /// <summary>
        /// ミラー側の実効ワールド行列 S·H·S を返す。
        /// ミラー軸は自身の設定、無ければ焼き込み元（BakedMirrorSourceIndex）の設定を使う。
        /// </summary>
        private Matrix4x4 ApplyMirrorConjugate(Matrix4x4 h, MeshContext ctx)
        {
            int   axis = ctx.MirrorAxis;
            float dist = ctx.MirrorDistance;

            int src = ctx.BakedMirrorSourceIndex;
            if (src >= 0 && src < MeshContextList.Count && MeshContextList[src] != null)
            {
                axis = MeshContextList[src].MirrorAxis;
                dist = MeshContextList[src].MirrorDistance;
            }

            Matrix4x4 s = MirrorBranchOps.MirrorMatrix(axis, dist);
            return s * h * s;
        }

        /// <summary>
        /// 全ボーンのBindPoseを計算（WorldMatrix.inverse）
        /// ComputeWorldMatrices()の後に呼ぶこと
        /// </summary>
        public void ComputeBindPoses()
        {
            if (MeshContextList == null) return;

            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var ctx = MeshContextList[i];
                if (ctx == null || ctx.Type != MeshType.Bone) continue;

                ctx.BindPose = ctx.WorldMatrix.inverse;
            }
        }

        /// <summary>
        /// ワールド行列とBindPoseを一括計算
        /// </summary>
        public void ComputeWorldAndBindPoses()
        {
            ComputeWorldMatrices();
            ComputeBindPoses();
        }

        /// <summary>
        /// スキンドでない（BoneWeight を持たない）DrawableコンテキストのBindPoseを
        /// 現在のWorldMatrix.inverseで更新する。
        ///
        /// RebuildAdapter直前（ComputeWorldMatrices()の後）に呼ぶこと。
        /// これにより UpdateTransform(useWorldTransform:true) が
        ///   worldPos = WorldMatrix * WorldMatrix.inverse * localPos = localPos（初期）
        ///   worldPos = newWorldMatrix * rebuildInverse * localPos（移動後）
        /// と計算され、スキンドメッシュと統一されたパスで扱える。
        /// </summary>
        public void ComputeMeshFilterBindPoses()
        {
            if (MeshContextList == null) return;

            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var ctx = MeshContextList[i];
                if (ctx == null) continue;

                // ボーン・モーフ・剛体・ジョイント・グループは対象外
                var t = ctx.Type;
                if (t == MeshType.Bone     || t == MeshType.Morph       ||
                    t == MeshType.RigidBody || t == MeshType.RigidBodyJoint ||
                    t == MeshType.Group)
                    continue;

                // スキンド頂点を持つ場合はインポート時BindPoseを維持する
                if (ctx.IsSkinned)
                    continue;

                ctx.BindPose = ctx.WorldMatrix.inverse;
            }
        }

        /// <summary>
        /// MeshContextリストからワールド行列を計算（静的メソッド・インポート時用）
        /// HierarchyParentIndexとBoneTransformに基づいて親→子の順で計算
        /// </summary>
        public static Dictionary<int, Matrix4x4> CalculateWorldMatrices(List<MeshContext> meshContexts)
        {
            // ComputeWorldMatrices と同様、生成ミラーの姿勢を実体側からそろえる
            SyncDerivedMirrorTransforms(meshContexts);

            var worldMatrices  = new Dictionary<int, Matrix4x4>();
            var hierarchyWorld = new Dictionary<int, Matrix4x4>();

            int maxIterations = meshContexts.Count;
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool anyAdded = false;

                for (int i = 0; i < meshContexts.Count; i++)
                {
                    if (worldMatrices.ContainsKey(i))
                        continue;

                    var ctx = meshContexts[i];
                    if (ctx?.BoneTransform == null)
                        continue;

                    int parentIndex = ctx.HierarchyParentIndex;
                    Matrix4x4 parentWorld;

                    if (parentIndex < 0)
                    {
                        parentWorld = Matrix4x4.identity;
                    }
                    else if (hierarchyWorld.TryGetValue(parentIndex, out parentWorld))
                    {
                        // 親が計算済み（鏡像を掛ける前の階層ワールドを積む）
                    }
                    else
                    {
                        continue;
                    }

                    Matrix4x4 localMatrix = Matrix4x4.TRS(
                        ctx.BoneTransform.Position,
                        Quaternion.Euler(ctx.BoneTransform.Rotation),
                        ctx.BoneTransform.Scale
                    );

                    // ComputeWorldMatrices と同じ規則。階層ワールドは hierarchyWorld に、
                    // ミラー側の実効ワールド S·H·S は戻り値に入れる。
                    Matrix4x4 h = parentWorld * localMatrix;
                    hierarchyWorld[i] = h;

                    worldMatrices[i] = (ctx.MirrorGeometryDerived && MirrorBranchOps.IsMirrorSideContext(ctx))
                        ? MirrorConjugate(h, ctx, meshContexts)
                        : h;
                    anyAdded = true;
                }

                if (!anyAdded)
                    break;
            }

            return worldMatrices;
        }

        /// <summary>静的版の S·H·S。ApplyMirrorConjugate と同じ規則。</summary>
        private static Matrix4x4 MirrorConjugate(
            Matrix4x4 h, MeshContext ctx, List<MeshContext> meshContexts)
        {
            int   axis = ctx.MirrorAxis;
            float dist = ctx.MirrorDistance;

            int src = ctx.BakedMirrorSourceIndex;
            if (src >= 0 && src < meshContexts.Count && meshContexts[src] != null)
            {
                axis = meshContexts[src].MirrorAxis;
                dist = meshContexts[src].MirrorDistance;
            }

            Matrix4x4 s = MirrorBranchOps.MirrorMatrix(axis, dist);
            return s * h * s;
        }

        /// <summary>
        /// MeshContextリストのBindPoseを一括計算（静的メソッド・インポート時用）
        /// CalculateWorldMatrices + BindPose = inverse を一括実行
        /// </summary>
        public static void ComputeBindPosesFromList(List<MeshContext> meshContexts)
        {
            var worldMatrices = CalculateWorldMatrices(meshContexts);
            foreach (var kv in worldMatrices)
            {
                meshContexts[kv.Key].BindPose = kv.Value.inverse;
            }
        }

        /// <summary>
        /// HierarchyParentIndexに基づいてトポロジカルソート
        /// 親が先に来るようにインデックスを並べ替える
        /// </summary>
        public List<int> TopologicalSortByHierarchy()
        {
            int count = MeshContextList.Count;
            var result = new List<int>(count);
            var visited = new bool[count];
            var inProgress = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (!visited[i])
                {
                    TopologicalSortVisit(i, visited, inProgress, result);
                }
            }

            return result;
        }

        private void TopologicalSortVisit(int index, bool[] visited, bool[] inProgress, List<int> result)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return;

            if (inProgress[index])
            {
                // 循環参照を検出（警告を出して無視）
                Debug.LogWarning($"[ModelContext] Circular hierarchy detected at index {index}");
                return;
            }

            if (visited[index])
                return;

            inProgress[index] = true;

            // 親を先に処理
            var ctx = MeshContextList[index];
            if (ctx != null)
            {
                int parentIndex = ctx.HierarchyParentIndex;
                if (parentIndex >= 0 && parentIndex < MeshContextList.Count && parentIndex != index)
                {
                    TopologicalSortVisit(parentIndex, visited, inProgress, result);
                }
            }

            inProgress[index] = false;
            visited[index] = true;
            result.Add(index);
        }

        /// <summary>
        /// 指定インデックスのMeshContextのワールド行列のみを再計算
        /// 親の行列は既に計算済みである前提
        /// </summary>
        public void ComputeWorldMatrix(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return;

            var ctx = MeshContextList[index];
            if (ctx == null) return;

            Matrix4x4 localMatrix = ctx.LocalMatrix;
            int parentIndex = ctx.HierarchyParentIndex;

            if (parentIndex >= 0 && parentIndex < MeshContextList.Count)
            {
                var parent = MeshContextList[parentIndex];
                ctx.WorldMatrix = parent.WorldMatrix * localMatrix;
            }
            else
            {
                ctx.WorldMatrix = localMatrix;
            }

            ctx.WorldMatrixInverse = ctx.WorldMatrix.inverse;
        }
    }
}
