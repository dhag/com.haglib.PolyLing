// TPoseConverter.cs
// Tポーズ変換の統合ユーティリティ
// PMXImporter / MQOImporter / TPosePanelから共通使用

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Core;

namespace Poly_Ling.Ops
{
    // TPoseBackup クラスは Core/Data/TPoseBackup.cs（namespace Poly_Ling.Data）へ移設。
    // 本ファイルは using Poly_Ling.Data 済みのため参照は無改修で解決される。

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
        /// <param name="compensateMirrorSides">
        /// 旧: ミラー側の局所姿勢を書き換える補正の有無。
        /// ミラーの実効ワールドを ComputeWorldMatrices 側で S·H·S として解決するように
        /// したため、現在は未使用（互換のため残置）。
        /// </param>
        public static void ConvertToTPose(
            List<MeshContext> meshContexts,
            HumanoidBoneMapping mapping,
            TPoseBackup backup = null,
            bool compensateMirrorSides = true)
        {
            if (meshContexts == null || mapping == null)
                return;

            // バックアップ取得
            if (backup != null)
                CaptureBackup(meshContexts, backup);

            // ワールド行列を計算（補正前。ミラー側の補正でデルタを取るのに使う）
            var worldBefore = ModelContext.CalculateWorldMatrices(meshContexts);

            // 左右の腕ボーンの回転を補正
            ApplyArmRotationCorrection(meshContexts, worldBefore, mapping, true);   // 左
            ApplyArmRotationCorrection(meshContexts, worldBefore, mapping, false);  // 右

            // ワールド行列を再計算
            var worldMatrices = ModelContext.CalculateWorldMatrices(meshContexts);

            // ミラー側の姿勢補正はここでは行わない。
            // CalculateWorldMatrices / ComputeWorldMatrices が、ミラー側の実効ワールドを
            // 常に S·H·S として返すため、階層のどこを回しても鏡像関係は自動で保たれる。
            _ = compensateMirrorSides;

            foreach (var kv in worldMatrices)
            {
                meshContexts[kv.Key].WorldMatrix = kv.Value;
            }

            // GPU処理で頂点座標をスキニング変換。
            // スキンウェイトが1つも無いモデル（MeshFilter 相当の階層）では、
            // 頂点は親子の変換だけで動くので焼き込みは不要。GPU 経路も通さない。
            if (HasAnySkinWeight(meshContexts))
            {
                BakeSkinnedVertices(meshContexts);
            }
            else
            {
                Debug.Log("[TPoseConverter] スキンウェイトが無いため頂点の焼き込みは省略" +
                          "（階層の姿勢だけを変更）");
            }

            // BindPose を更新するのはスキンドの場合だけにする。
            // スキンが無いモデルで全件を WorldMatrix⁻¹ にすると、SkinningMatrix 経路で
            // 描かれるコンテキスト（ミラー側など）の変換が W·W⁻¹ = 単位 で消える。
            if (HasAnySkinWeight(meshContexts))
            {
                foreach (var kv in worldMatrices)
                {
                    meshContexts[kv.Key].BindPose = kv.Value.inverse;
                }
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
            tempMapping.FixHipsByHierarchy(meshContexts);

            ConvertToTPose(meshContexts, tempMapping, backup);
        }

        // ================================================================
        // 診断
        // ================================================================

        /// <summary>
        /// Tポーズ変換が実際に何をするかを、変換前に人が読める形で返す。
        /// 「押しても反応がない」場合にどこで止まるかを一発で切り分けるためのもの。
        /// </summary>
        public static string Diagnose(List<MeshContext> meshContexts, HumanoidBoneMapping mapping)
        {
            var sb = new System.Text.StringBuilder();

            if (meshContexts == null) return "メッシュリストがありません";
            if (mapping == null || mapping.IsEmpty) return "マッピングが未設定です";

            sb.AppendLine($"マッピング {mapping.Count} 件 / コンテキスト {meshContexts.Count} 件");
            sb.AppendLine($"スキンウェイト: {(HasAnySkinWeight(meshContexts) ? "あり" : "なし")}");

            var worldMatrices = ModelContext.CalculateWorldMatrices(meshContexts);
            sb.AppendLine($"ワールド行列を解決できたコンテキスト: {worldMatrices.Count} / {meshContexts.Count}");

            foreach (bool isLeft in new[] { true, false })
            {
                string side = isLeft ? "左" : "右";

                if (!mapping.GetArmBoneIndices(isLeft, out int upper, out int lower))
                {
                    sb.AppendLine($"{side}: 腕が未マッピング → 何もしない");
                    continue;
                }

                string uName = Name(meshContexts, upper);
                string lName = Name(meshContexts, lower);
                sb.AppendLine($"{side}: UpperArm=[{upper}]{uName} / LowerArm=[{lower}]{lName}");

                if (!worldMatrices.TryGetValue(upper, out var uw))
                {
                    sb.AppendLine($"{side}: UpperArm のワールド行列が未解決 → 何もしない"
                                  + BlockReason(meshContexts, upper, worldMatrices));
                    continue;
                }
                if (!worldMatrices.TryGetValue(lower, out var lw))
                {
                    sb.AppendLine($"{side}: LowerArm のワールド行列が未解決 → 何もしない"
                                  + BlockReason(meshContexts, lower, worldMatrices));
                    continue;
                }

                Vector3 up  = uw.GetColumn(3);
                Vector3 lp  = lw.GetColumn(3);
                Vector3 dir = (lp - up).normalized;
                Vector3 tgt = isLeft ? Vector3.left : Vector3.right;
                float   ang = Vector3.Angle(dir, tgt);

                sb.AppendLine($"{side}: 現在の腕方向 {dir} → 目標 {tgt} / 角度 {ang:F1}°");
                if (ang < 1f) sb.AppendLine($"{side}: 既にTポーズ（1°未満）→ 何もしない");
                else          sb.AppendLine($"{side}: {ang:F1}° 回します");

                if (up == lp)
                    sb.AppendLine($"{side}: UpperArm と LowerArm のワールド位置が同一。方向が定まりません");
            }

            return sb.ToString().TrimEnd();
        }

        private static string Name(List<MeshContext> list, int i)
            => (i >= 0 && i < list.Count) ? (list[i]?.Name ?? "<null>") : "<範囲外>";

        /// <summary>ワールド行列が解決しない原因を、親をさかのぼって特定する。</summary>
        private static string BlockReason(
            List<MeshContext> list, int index, Dictionary<int, Matrix4x4> solved)
        {
            if (index < 0 || index >= list.Count) return "（索引が範囲外）";

            int cur = index, safety = list.Count + 1;
            while (cur >= 0 && cur < list.Count && safety-- > 0)
            {
                var ctx = list[cur];
                if (ctx == null)          return $"（[{cur}] が null）";
                if (ctx.BoneTransform == null)
                    return $"（[{cur}]{ctx.Name} の BoneTransform が null。ここで連鎖が切れます）";
                if (!solved.ContainsKey(cur) && cur != index)
                    return $"（[{cur}]{ctx.Name} が未解決）";
                cur = ctx.HierarchyParentIndex;
            }
            return "（親の循環参照の可能性）";
        }

        /// <summary>スキンウェイトを持つ頂点が1つでもあるか。</summary>
        public static bool HasAnySkinWeight(List<MeshContext> meshContexts)
        {
            if (meshContexts == null) return false;
            foreach (var ctx in meshContexts)
            {
                if (ctx?.MeshObject == null) continue;
                if (ctx.IsSkinned) return true;
            }
            return false;
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
            backup.MorphBasePositions.Clear();
            backup.PhysicsPoses.Clear();

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null) continue;

                // 姿勢は「ボーンかどうか」ではなく BoneTransform の有無で拾う。
                // ボーンを持たない MeshFilter ツリーでは回転するのはメッシュ自身の
                // BoneTransform なので、ここで拾わないと「元の姿勢に戻す」が効かない。
                if (ctx.BoneTransform != null)
                {
                    backup.BoneRotations[i] = ctx.BoneTransform.Rotation;
                    backup.BonePositions[i] = ctx.BoneTransform.Position;
                    backup.BoneUseLocal[i]  = ctx.BoneTransform.UseLocalTransform;
                }

                // WorldMatrix / BindPose も同じ理由で全コンテキスト分を持つ
                // （ConvertToTPose は BindPose を全件書き換えるため）。
                backup.WorldMatrices[i] = ctx.WorldMatrix;
                backup.BindPoses[i]     = ctx.BindPose;

                if (ctx.Type == MeshType.Bone)
                {
                    backup.BonePoses[i] = ctx.BonePoseData?.Clone();
                }
                else if (ctx.MeshObject != null)
                {
                    // メッシュの頂点座標を保存
                    var verts = ctx.MeshObject.Vertices;
                    var positions = new Vector3[verts.Count];
                    for (int v = 0; v < verts.Count; v++)
                        positions[v] = verts[v].Position;
                    backup.VertexPositions[i] = positions;

                    // モーフの基準位置も焼き込みで変わるので控える
                    var mbd = ctx.MorphBaseData;
                    if (mbd != null && mbd.IsValid)
                        backup.MorphBasePositions[i] = (Vector3[])mbd.BasePositions.Clone();

                    // 剛体・ジョイントの位置と回転も焼き込みで変わるので控える
                    var rbd = ctx.MeshObject.RigidBodyData;
                    if (rbd != null)
                        backup.PhysicsPoses[i] = new[] { rbd.Position, rbd.Rotation };
                    var jd = ctx.MeshObject.JointData;
                    if (jd != null)
                        backup.PhysicsPoses[i] = new[] { jd.Position, jd.Rotation };
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

            // ボーンのローカル位置・UseLocalTransform を復元
            foreach (var kv in backup.BonePositions)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                {
                    var ctx = meshContexts[idx];
                    if (ctx?.BoneTransform != null)
                    {
                        if (backup.BoneUseLocal.TryGetValue(idx, out var ul))
                            ctx.BoneTransform.UseLocalTransform = ul;
                        ctx.BoneTransform.Position = kv.Value;
                    }
                }
            }

            // ボーンの BonePoseData（ポーズ層）を復元
            foreach (var kv in backup.BonePoses)
            {
                int idx = kv.Key;
                if (idx >= 0 && idx < meshContexts.Count)
                {
                    var ctx = meshContexts[idx];
                    if (ctx == null) continue;
                    ctx.BonePoseData = kv.Value?.Clone();
                }
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
                    ctx.ReplaceUnityMesh(ctx.MeshObject.ToUnityMesh());
                }
            }

            // モーフの基準位置を復元（頂点数が同じときだけ）
            foreach (var kv in backup.MorphBasePositions)
            {
                int idx = kv.Key;
                if (idx < 0 || idx >= meshContexts.Count) continue;
                var mbd = meshContexts[idx]?.MorphBaseData;
                if (mbd == null || !mbd.IsValid || kv.Value == null) continue;
                if (mbd.BasePositions.Length != kv.Value.Length) continue;
                mbd.BasePositions = (Vector3[])kv.Value.Clone();
            }

            // 剛体・ジョイントの位置と回転を復元
            foreach (var kv in backup.PhysicsPoses)
            {
                int idx = kv.Key;
                if (idx < 0 || idx >= meshContexts.Count || kv.Value == null || kv.Value.Length < 2) continue;
                var mo = meshContexts[idx]?.MeshObject;
                if (mo == null) continue;
                if (mo.RigidBodyData != null) { mo.RigidBodyData.Position = kv.Value[0]; mo.RigidBodyData.Rotation = kv.Value[1]; }
                if (mo.JointData     != null) { mo.JointData.Position     = kv.Value[0]; mo.JointData.Rotation     = kv.Value[1]; }
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
            // Unity 規約ではキャラクタ正面が +Z、右が +X。よって左腕は -X 方向へ伸ばす。
            Vector3 targetDirection = isLeft ? Vector3.left : Vector3.right;

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

                // MeshContext.LocalMatrix は UseLocalTransform が false だと単位を返すため、
                // 立てておかないと書いた回転が ComputeWorldMatrices で無視される。
                // ボーンは元から true だが、MeshFilter のオブジェクトは false のことがある。
                upperArmContext.BoneTransform.UseLocalTransform = true;
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
            // 剛体・ジョイントを関連ボーンの動きで動かす（CPU。行列はリバインド前）。
            // 頂点と同じく、焼き込みのあとは呼び出し元がリバインドするので、ここで
            // 位置と回転を「変形後のワールド値」にしておく必要がある。
            BakePhysicsPoses(meshContexts);

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

                    // worldPositions は GPU が「頂点ごとに選んだ行列」を適用したワールド座標。
                    //   UnifiedCompute.compute:905-916  skinMatrix = Σ _TransformMatrixBuffer[boneIds.k] * weights.k
                    //
                    // 【BoneWeight を持つ頂点】
                    // 焼き込みの後、呼び出し元は必ずボーンをリバインドする（BindPose = WorldMatrix⁻¹）。
                    //   ConvertToTPose 64-67 /
                    //   ObjectMoveTool「スキンごと確定」/ PlayerCommandDispatcher「スキンごと確定」「この姿勢で確定」
                    // これでボーンの SkinningMatrix は単位になるため、頂点は
                    // 「変形後のワールド座標そのもの」を保持していなければならない。
                    // ここで GPU と同じ行列の逆を掛けると往復して打ち消し合い、焼き込みが無効になる。
                    //
                    // 【BoneWeight を持たない頂点】
                    // GPU 側は自メッシュの context 索引を使う（UnifiedBufferManager_Build.cs:358-362）。
                    // リバインドでは変わらないため、従来通り逆行列でローカルへ戻す（結果は恒等）。
                    // 行列の選択規則は UnifiedBufferManager.UpdateTransformMatrices:1513-1515 と同一。
                    // 分類は UnifiedBufferManager.UpdateTransformMatrices と揃える。
                    // ミラー側もスキンが無ければ WorldMatrix 直接。
                    bool usesWorldMatrixDirect =
                        (ctx.Type == MeshType.Mesh ||
                         ctx.Type == MeshType.MirrorSide ||
                         ctx.Type == MeshType.BakedMirror) &&
                        !ctx.IsSkinned;
                    Matrix4x4 unskinnedInv = (usesWorldMatrixDirect ? ctx.WorldMatrix : ctx.SkinningMatrix).inverse;

                    var verts = ctx.MeshObject.Vertices;
                    for (int i = 0; i < vertexCount && (vertexStart + i) < worldPositions.Length; i++)
                    {
                        var vtx = verts[i];
                        if (vtx == null) continue;

                        Vector3 worldPos = worldPositions[vertexStart + i];
                        vtx.Position = vtx.HasBoneWeight
                            ? worldPos
                            : unskinnedInv.MultiplyPoint3x4(worldPos);
                    }

                    // モーフメッシュは基準位置も同じスキンで焼く。
                    // 差分は「頂点位置 − BasePositions」（MorphBaseData.cs）なので、頂点だけ焼くと
                    // 焼き込みで動いた部位の差分に変換前後の位置の差が混ざる。
                    // 行列はまだリバインド前（GPU に渡したものと同じ SkinningMatrix）。
                    RebakeMorphBasePositions(ctx, meshContexts);

                    // UnityMeshを再生成して表示を更新
                    ctx.ReplaceUnityMesh(ctx.MeshObject.ToUnityMesh());

                    bakedMeshCount++;
                    bakedVertexCount += vertexCount;
                }

                Debug.Log($"[TPoseConverter] Baked {bakedMeshCount} meshes, {bakedVertexCount} vertices using GPU");
            }
        }

        /// <summary>
        /// モーフメッシュの基準位置（MorphBaseData.BasePositions）を、頂点と同じスキン行列で焼く。
        /// ウェイトのある頂点だけが対象（ウェイトの無い頂点は焼き込みで位置が変わらない）。
        /// 行列の組み方は GPU（UnifiedCompute の skinMatrix）・MeshContext.VertexMatrix と同じ
        /// 「Σ weight × list[bone].SkinningMatrix」。ParentModelContext を介さず meshContexts を引くので、
        /// 読み込み途中（モデルへ入る前）でも使える。
        /// </summary>
        private static void RebakeMorphBasePositions(MeshContext ctx, List<MeshContext> meshContexts)
        {
            var mbd = ctx?.MorphBaseData;
            var mo  = ctx?.MeshObject;
            if (mbd == null || !mbd.IsValid || mo == null) return;

            int n = Mathf.Min(mbd.BasePositions.Length, mo.VertexCount);
            for (int i = 0; i < n; i++)
            {
                var vtx = mo.Vertices[i];
                if (vtx == null || !vtx.HasBoneWeight) continue;

                var bw = vtx.BoneWeight.Value;
                Matrix4x4 acc = new Matrix4x4();
                float total = 0f;
                total += AddBone(ref acc, meshContexts, bw.boneIndex0, bw.weight0);
                total += AddBone(ref acc, meshContexts, bw.boneIndex1, bw.weight1);
                total += AddBone(ref acc, meshContexts, bw.boneIndex2, bw.weight2);
                total += AddBone(ref acc, meshContexts, bw.boneIndex3, bw.weight3);
                if (total <= 0f) continue;

                mbd.BasePositions[i] = acc.MultiplyPoint3x4(mbd.BasePositions[i]);
            }
        }

        /// <summary>
        /// 剛体（RigidBodyData）とジョイント（JointData）の位置・回転を、関連ボーンの SkinningMatrix で動かす。
        /// 剛体の関連ボーンは名前（RelatedBoneName）を先に引き、無ければ BoneIndex。
        /// ジョイントは剛体 A の関連ボーン（無ければ剛体 B）に従う。PMX のジョイントの扱いと同じ。
        /// 関連ボーンが引けないものは動かさない。
        /// 回転は Euler（ラジアン）で、適用側（HierarchyPhysicsBuilder）と同じく Quaternion.Euler(度) と解釈する。
        /// </summary>
        private static void BakePhysicsPoses(List<MeshContext> meshContexts)
        {
            if (meshContexts == null) return;

            var boneByName = new Dictionary<string, int>();
            var bodyByName = new Dictionary<string, int>();
            for (int i = 0; i < meshContexts.Count; i++)
            {
                var c = meshContexts[i];
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                if (c.Type == MeshType.Bone && !boneByName.ContainsKey(c.Name)) boneByName[c.Name] = i;
                if (c.MeshObject?.RigidBodyData != null && !bodyByName.ContainsKey(c.Name)) bodyByName[c.Name] = i;
            }

            int BoneOfBody(int bodyIdx)
            {
                if (bodyIdx < 0 || bodyIdx >= meshContexts.Count) return -1;
                var rb = meshContexts[bodyIdx]?.MeshObject?.RigidBodyData;
                if (rb == null) return -1;
                if (!string.IsNullOrEmpty(rb.RelatedBoneName) && boneByName.TryGetValue(rb.RelatedBoneName, out int bi))
                    return bi;
                int b = rb.BoneIndex;
                return (b >= 0 && b < meshContexts.Count && meshContexts[b]?.Type == MeshType.Bone) ? b : -1;
            }

            int BodyIndex(string name, int index)
            {
                if (!string.IsNullOrEmpty(name) && bodyByName.TryGetValue(name, out int bi)) return bi;
                return (index >= 0 && index < meshContexts.Count && meshContexts[index]?.MeshObject?.RigidBodyData != null) ? index : -1;
            }

            // 先にすべての骨の行列を引いてから書く（剛体同士は依存しないが、順序に左右されないように）
            var targets = new List<(int ctxIdx, Matrix4x4 skin)>();
            for (int i = 0; i < meshContexts.Count; i++)
            {
                var mo = meshContexts[i]?.MeshObject;
                if (mo == null) continue;

                int bone = -1;
                if (mo.RigidBodyData != null)
                    bone = BoneOfBody(i);
                else if (mo.JointData != null)
                {
                    var jd = mo.JointData;
                    bone = BoneOfBody(BodyIndex(jd.BodyAName, jd.RigidBodyIndexA));
                    if (bone < 0) bone = BoneOfBody(BodyIndex(jd.BodyBName, jd.RigidBodyIndexB));
                }
                if (bone < 0) continue;

                // 骨が動いていないもの（スキン行列が単位）は書き換えない。
                // 回転を Euler へ戻すと表記（-0.1 と 6.18 など）が変わるため。
                Matrix4x4 skinM = meshContexts[bone].SkinningMatrix;
                if (IsNearIdentity(skinM)) continue;

                targets.Add((i, skinM));
            }

            foreach (var (idx, skin) in targets)
            {
                var mo = meshContexts[idx].MeshObject;
                Quaternion r = skin.rotation;
                if (mo.RigidBodyData != null)
                {
                    var rb = mo.RigidBodyData;
                    rb.Position = skin.MultiplyPoint3x4(rb.Position);
                    rb.Rotation = EulerRad(r * Quaternion.Euler(rb.Rotation * Mathf.Rad2Deg));
                }
                else
                {
                    var jd = mo.JointData;
                    jd.Position = skin.MultiplyPoint3x4(jd.Position);
                    jd.Rotation = EulerRad(r * Quaternion.Euler(jd.Rotation * Mathf.Rad2Deg));
                }
            }
        }

        private static bool IsNearIdentity(Matrix4x4 m)
        {
            const float eps = 1e-6f;
            for (int k = 0; k < 16; k++)
            {
                float id = (k % 5 == 0) ? 1f : 0f;
                if (Mathf.Abs(m[k] - id) > eps) return false;
            }
            return true;
        }

        // 回転を Euler（ラジアン、各軸 -π〜π）へ。PMX の読み込み値と同じ範囲に寄せる。
        private static Vector3 EulerRad(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z)) * Mathf.Deg2Rad;
        }

        private static float AddBone(ref Matrix4x4 acc, List<MeshContext> list, int boneIndex, float weight)
        {
            if (weight == 0f || boneIndex < 0 || boneIndex >= list.Count) return 0f;
            var bone = list[boneIndex];
            if (bone == null) return 0f;
            Matrix4x4 m = bone.SkinningMatrix;
            for (int k = 0; k < 16; k++) acc[k] += m[k] * weight;
            return weight;
        }
    }
}
