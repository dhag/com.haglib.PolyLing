// TPoseConverter.cs
// Tポーズ変換の統合ユーティリティ
// PMXImporter / MQOImporter / TPosePanelから共通使用

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Core;

namespace Poly_Ling.Data
{
    /// <summary>
    /// Tポーズ変換前の姿勢バックアップ
    /// </summary>
    public class TPoseBackup
    {
        /// <summary>
        /// ボーン別のローカル回転バックアップ（MeshContextインデックス→Euler角）
        /// </summary>
        public Dictionary<int, Vector3> BoneRotations = new();

        /// <summary>
        /// ボーン別のWorldMatrixバックアップ
        /// </summary>
        public Dictionary<int, Matrix4x4> WorldMatrices = new();

        /// <summary>
        /// ボーン別のBindPoseバックアップ
        /// </summary>
        public Dictionary<int, Matrix4x4> BindPoses = new();

        /// <summary>
        /// メッシュ別の頂点座標バックアップ（MeshContextインデックス→頂点Position配列）
        /// </summary>
        public Dictionary<int, Vector3[]> VertexPositions = new();
    }

    /// <summary>
    /// Tポーズ変換の統合ユーティリティ
    /// </summary>
    public static class TPoseConverter
    {
        // ================================================================
        // メイン: Tポーズに変換
        // ================================================================

        /// <summary>
        /// MeshContextリストをTポーズに変換
        /// HumanoidBoneMappingから腕ボーンを解決する
        /// </summary>
        /// <param name="meshContexts">対象MeshContextリスト</param>
        /// <param name="mapping">Humanoidボーンマッピング（腕ボーンのインデックス解決用）</param>
        /// <param name="backup">バックアップを保存する場合はnon-null。nullならバックアップしない</param>
        public static void ConvertToTPose(
            List<MeshContext> meshContexts,
            HumanoidBoneMapping mapping,
            TPoseBackup backup = null)
        {
            if (meshContexts == null || mapping == null)
                return;

            // バックアップ取得
            if (backup != null)
                CaptureBackup(meshContexts, backup);

            // ワールド行列を計算
            var worldMatrices = ModelContext.CalculateWorldMatrices(meshContexts);

            // 左右の腕ボーンの回転を補正
            ApplyArmRotationCorrection(meshContexts, worldMatrices, mapping, true);   // 左
            ApplyArmRotationCorrection(meshContexts, worldMatrices, mapping, false);  // 右

            // ワールド行列を再計算
            worldMatrices = ModelContext.CalculateWorldMatrices(meshContexts);
            foreach (var kv in worldMatrices)
            {
                meshContexts[kv.Key].WorldMatrix = kv.Value;
            }

            // GPU処理で頂点座標をスキニング変換
            BakeSkinnedVertices(meshContexts);

            // BindPoseを更新
            foreach (var kv in worldMatrices)
            {
                meshContexts[kv.Key].BindPose = kv.Value.inverse;
            }

            Debug.Log("[TPoseConverter] T-Pose conversion completed");
        }

        /// <summary>
        /// MeshContextリストをTポーズに変換（HumanoidBoneMapping未設定時、ボーン名ベース）
        /// インポート時のフォールバック用
        /// </summary>
        /// <param name="meshContexts">対象MeshContextリスト</param>
        /// <param name="backup">バックアップを保存する場合はnon-null</param>
        public static void ConvertToTPoseByBoneNames(
            List<MeshContext> meshContexts,
            TPoseBackup backup = null)
        {
            if (meshContexts == null)
                return;

            // ボーン名→インデックスのマップを作成
            var boneNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx?.Type == MeshType.Bone && !string.IsNullOrEmpty(ctx.Name))
                    boneNameToIndex[ctx.Name] = i;
            }

            // 一時的なHumanoidBoneMappingを作成してボーン名から自動マッピング
            var tempMapping = new HumanoidBoneMapping();
            var boneNames = new List<string>();
            for (int i = 0; i < meshContexts.Count; i++)
            {
                boneNames.Add(meshContexts[i]?.Name ?? "");
            }
            tempMapping.AutoMapFromEmbeddedCSV(boneNames);

            ConvertToTPose(meshContexts, tempMapping, backup);
        }

        // ================================================================
        // バックアップ / 復元
        // ================================================================

        /// <summary>
        /// 現在の姿勢をバックアップに保存
        /// </summary>
        public static void CaptureBackup(List<MeshContext> meshContexts, TPoseBackup backup)
        {
            if (meshContexts == null || backup == null)
                return;

            backup.BoneRotations.Clear();
            backup.WorldMatrices.Clear();
            backup.BindPoses.Clear();
            backup.VertexPositions.Clear();

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null) continue;

                if (ctx.Type == MeshType.Bone)
                {
                    if (ctx.BoneTransform != null)
                        backup.BoneRotations[i] = ctx.BoneTransform.Rotation;
                    backup.WorldMatrices[i] = ctx.WorldMatrix;
                    backup.BindPoses[i] = ctx.BindPose;
                }
                else if (ctx.MeshObject != null)
                {
                    // メッシュの頂点座標を保存
                    var verts = ctx.MeshObject.Vertices;
                    var positions = new Vector3[verts.Count];
                    for (int v = 0; v < verts.Count; v++)
                        positions[v] = verts[v].Position;
                    backup.VertexPositions[i] = positions;
                }
            }
        }

        /// <summary>
        /// バックアップから姿勢を復元
        /// </summary>
        /// <param name="meshContexts">復元先MeshContextリスト</param>
        /// <param name="backup">復元するバックアップ</param>
        public static void RestoreFromBackup(List<MeshContext> meshContexts, TPoseBackup backup)
        {
            if (meshContexts == null || backup == null)
                return;

            // ボーンの回転・WorldMatrix・BindPoseを復元
            foreach (var kv in backup.BoneRotations)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                {
                    var ctx = meshContexts[idx];
                    if (ctx?.BoneTransform != null)
                        ctx.BoneTransform.Rotation = kv.Value;
                }
            }

            foreach (var kv in backup.WorldMatrices)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                    meshContexts[idx].WorldMatrix = kv.Value;
            }

            foreach (var kv in backup.BindPoses)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                    meshContexts[idx].BindPose = kv.Value;
            }

            // メッシュ頂点座標を復元
            foreach (var kv in backup.VertexPositions)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                {
                    var ctx = meshContexts[idx];
                    if (ctx?.MeshObject == null) continue;

                    var verts = ctx.MeshObject.Vertices;
                    var positions = kv.Value;
                    for (int v = 0; v < verts.Count && v < positions.Length; v++)
                        verts[v].Position = positions[v];

                    // UnityMeshを再生成
                    ctx.UnityMesh = ctx.MeshObject.ToUnityMesh();
                }
            }

            Debug.Log("[TPoseConverter] Restored from backup");
        }

        // ================================================================
        // 腕ボーン回転補正
        // ================================================================

        /// <summary>
        /// 腕ボーンの回転補正を適用
        /// HumanoidBoneMappingから腕ボーンインデックスを解決
        /// </summary>
        private static void ApplyArmRotationCorrection(
            List<MeshContext> meshContexts,
            Dictionary<int, Matrix4x4> worldMatrices,
            HumanoidBoneMapping mapping,
            bool isLeft)
        {
            string sideName = isLeft ? "Left" : "Right";

            // HumanoidBoneMappingから腕ボーンインデックスを取得
            if (!mapping.GetArmBoneIndices(isLeft, out int upperArmIndex, out int lowerArmIndex))
            {
                Debug.LogWarning($"[TPoseConverter] T-Pose: {sideName} arm bones not mapped");
                return;
            }

            // ワールド行列からワールド位置を取得
            if (!worldMatrices.TryGetValue(upperArmIndex, out Matrix4x4 upperArmWorld))
            {
                Debug.LogWarning($"[TPoseConverter] T-Pose: {sideName} UpperArm world matrix not found");
                return;
            }
            if (!worldMatrices.TryGetValue(lowerArmIndex, out Matrix4x4 lowerArmWorld))
            {
                Debug.LogWarning($"[TPoseConverter] T-Pose: {sideName} LowerArm world matrix not found");
                return;
            }

            Vector3 upperArmPos = upperArmWorld.GetColumn(3);
            Vector3 lowerArmPos = lowerArmWorld.GetColumn(3);
            Vector3 currentDirection = (lowerArmPos - upperArmPos).normalized;

            // 目標方向（水平・外向き）
            Vector3 targetDirection = isLeft ? Vector3.right : Vector3.left;

            // 現在の方向と目標方向が近い場合はスキップ
            float angle = Vector3.Angle(currentDirection, targetDirection);
            if (angle < 1f)
            {
                Debug.Log($"[TPoseConverter] T-Pose: {sideName} arm already in T-Pose (angle={angle:F1}°)");
                return;
            }

            // 補正回転を計算・適用
            // correctionはワールド空間での回転。BoneTransform.Rotationはローカル空間のため変換が必要。
            Quaternion correction = Quaternion.FromToRotation(currentDirection, targetDirection);

            var upperArmContext = meshContexts[upperArmIndex];
            if (upperArmContext?.BoneTransform != null)
            {
                Quaternion parentWorldRot = Quaternion.identity;
                int parentIdx = upperArmContext.HierarchyParentIndex;
                if (parentIdx >= 0 && worldMatrices.TryGetValue(parentIdx, out Matrix4x4 parentWorld))
                {
                    parentWorldRot = parentWorld.rotation;
                }

                Quaternion currentLocalRot = Quaternion.Euler(upperArmContext.BoneTransform.Rotation);
                Quaternion newLocalRot = Quaternion.Inverse(parentWorldRot) * correction * parentWorldRot * currentLocalRot;
                Debug.Log($"[TPoseConverter] T-Pose: {sideName} arm correction angle={angle:F1}°, " +
                          $"rotation: {upperArmContext.BoneTransform.Rotation} -> {newLocalRot.eulerAngles}");
                upperArmContext.BoneTransform.Rotation = newLocalRot.eulerAngles;
            }
        }

        // ================================================================
        // スキニング頂点ベイク
        // ================================================================

        /// <summary>
        /// GPU処理を使用してスキンドメッシュの頂点座標をベイク
        /// </summary>
        public static void BakeSkinnedVertices(List<MeshContext> meshContexts)
        {
            using (var bufferManager = new UnifiedBufferManager())
            {
                bufferManager.Initialize();
                bufferManager.BuildFromMeshContexts(meshContexts);
                bufferManager.UpdateTransformMatrices(meshContexts, useWorldTransform: true);
                bufferManager.DispatchTransformVertices(useWorldTransform: true, transformNormals: false, readbackToCPU: true);

                var worldPositions = bufferManager.GetWorldPositions();
                if (worldPositions == null || worldPositions.Length == 0)
                {
                    Debug.LogWarning("[TPoseConverter] Failed to get world positions from GPU");
                    return;
                }

                var meshInfos = bufferManager.MeshInfos;
                if (meshInfos == null)
                {
                    Debug.LogWarning("[TPoseConverter] MeshInfos is null");
                    return;
                }

                // 各メッシュの頂点座標を書き戻し
                int bakedMeshCount = 0;
                int bakedVertexCount = 0;
                for (int ctxIdx = 0; ctxIdx < meshContexts.Count; ctxIdx++)
                {
                    var ctx = meshContexts[ctxIdx];
                    if (ctx?.MeshObject == null) continue;
                    if (ctx.Type == MeshType.Bone) continue;

                    int unifiedMeshIdx = bufferManager.ContextToUnifiedMeshIndex(ctxIdx);
                    if (unifiedMeshIdx < 0 || unifiedMeshIdx >= meshInfos.Length)
                        continue;

                    var meshInfo = meshInfos[unifiedMeshIdx];
                    int vertexStart = (int)meshInfo.VertexStart;
                    int vertexCount = ctx.MeshObject.VertexCount;

                    for (int i = 0; i < vertexCount && (vertexStart + i) < worldPositions.Length; i++)
                    {
                        ctx.MeshObject.Vertices[i].Position = worldPositions[vertexStart + i];
                    }

                    // UnityMeshを再生成して表示を更新
                    ctx.UnityMesh = ctx.MeshObject.ToUnityMesh();

                    bakedMeshCount++;
                    bakedVertexCount += vertexCount;
                }

                Debug.Log($"[TPoseConverter] Baked {bakedMeshCount} meshes, {bakedVertexCount} vertices using GPU");
            }
        }
    }
}
