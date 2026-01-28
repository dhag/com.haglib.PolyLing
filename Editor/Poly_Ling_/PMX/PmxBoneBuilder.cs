// PmxBoneBuilder.cs - PMXDocumentからUnityボーン階層を構築
// バイナリ読み込み済みのPMXDocumentを使用してUnity Transformを生成

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXDocumentからUnityボーン階層を構築するビルダー
    /// </summary>
    public static class PmxBoneBuilder
    {
        // ================================================================
        // 定数
        // ================================================================

        /// <summary>ローカル軸フラグ (0x0800)</summary>
        private const int FLAG_LOCAL_AXIS = 0x0800;

        /// <summary>軸固定フラグ (0x0400)</summary>
        private const int FLAG_FIXED_AXIS = 0x0400;

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>
        /// PMXDocumentからUnityボーン階層を構築
        /// </summary>
        /// <param name="doc">PMXドキュメント</param>
        /// <param name="rootTransform">ボーン階層のルートとなるTransform</param>
        /// <param name="convertCoordinate">座標系変換を行うか（PMX→Unity、Z反転）</param>
        /// <returns>ボーン名→Transformの辞書</returns>
        public static Dictionary<string, Transform> BuildBoneHierarchy(
            PMXDocument doc,
            Transform rootTransform,
            bool convertCoordinate = true)
        {
            if (doc == null || doc.Bones.Count == 0)
                return new Dictionary<string, Transform>();

            var boneTransforms = new Dictionary<string, Transform>(doc.Bones.Count);
            var boneRotationsModel = new Dictionary<int, Quaternion>(doc.Bones.Count);

            // フェーズ1: 全てのTransformを作成
            for (int i = 0; i < doc.Bones.Count; i++)
            {
                var pmxBone = doc.Bones[i];
                var go = new GameObject(pmxBone.Name);
                boneTransforms[pmxBone.Name] = go.transform;
            }

            // フェーズ2: 親子関係を設定
            for (int i = 0; i < doc.Bones.Count; i++)
            {
                var pmxBone = doc.Bones[i];
                var tf = boneTransforms[pmxBone.Name];

                if (pmxBone.ParentIndex >= 0 && pmxBone.ParentIndex < doc.Bones.Count)
                {
                    var parentBone = doc.Bones[pmxBone.ParentIndex];
                    if (boneTransforms.TryGetValue(parentBone.Name, out var parentTf))
                    {
                        tf.SetParent(parentTf, worldPositionStays: false);
                    }
                    else
                    {
                        tf.SetParent(rootTransform, worldPositionStays: false);
                    }
                }
                else
                {
                    tf.SetParent(rootTransform, worldPositionStays: false);
                }
            }

            // フェーズ3: モデル空間での回転を計算
            for (int i = 0; i < doc.Bones.Count; i++)
            {
                var pmxBone = doc.Bones[i];
                Quaternion rotModel = CalculateModelSpaceRotation(pmxBone, doc, i, convertCoordinate);
                boneRotationsModel[i] = rotModel;
            }

            // フェーズ4: 位置とローカル回転を設定
            for (int i = 0; i < doc.Bones.Count; i++)
            {
                var pmxBone = doc.Bones[i];
                var tf = boneTransforms[pmxBone.Name];

                // 位置計算（親からの相対位置）
                Vector3 posModel = ConvertPosition(pmxBone.Position, convertCoordinate);
                Vector3 parentPosModel = Vector3.zero;

                if (pmxBone.ParentIndex >= 0 && pmxBone.ParentIndex < doc.Bones.Count)
                {
                    var parentBone = doc.Bones[pmxBone.ParentIndex];
                    parentPosModel = ConvertPosition(parentBone.Position, convertCoordinate);
                }

                tf.localPosition = posModel - parentPosModel;

                // ローカル回転計算（親の回転を考慮）
                Quaternion rotModel = boneRotationsModel[i];

                if (pmxBone.ParentIndex >= 0 && boneRotationsModel.TryGetValue(pmxBone.ParentIndex, out var parentRotModel))
                {
                    tf.localRotation = Quaternion.Inverse(parentRotModel) * rotModel;
                }
                else
                {
                    tf.localRotation = rotModel;
                }
            }

            return boneTransforms;
        }

        /// <summary>
        /// BindPosesを計算
        /// </summary>
        /// <param name="meshRoot">メッシュのルートTransform</param>
        /// <param name="bones">ボーンTransform配列（PMXのボーン順）</param>
        /// <returns>BindPose行列配列</returns>
        public static Matrix4x4[] ComputeBindPoses(Transform meshRoot, Transform[] bones)
        {
            var bindPoses = new Matrix4x4[bones.Length];
            var meshRootL2W = meshRoot.localToWorldMatrix;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    bindPoses[i] = bones[i].worldToLocalMatrix * meshRootL2W;
                }
                else
                {
                    bindPoses[i] = Matrix4x4.identity;
                }
            }

            return bindPoses;
        }

        /// <summary>
        /// PMXDocument.BonesからTransform配列を取得（ボーンインデックス順）
        /// </summary>
        public static Transform[] GetBoneArray(PMXDocument doc, Dictionary<string, Transform> boneTransforms)
        {
            var bones = new Transform[doc.Bones.Count];

            for (int i = 0; i < doc.Bones.Count; i++)
            {
                var pmxBone = doc.Bones[i];
                if (boneTransforms.TryGetValue(pmxBone.Name, out var tf))
                {
                    bones[i] = tf;
                }
            }

            return bones;
        }

        // ================================================================
        // 内部処理
        // ================================================================

        /// <summary>
        /// ボーンのモデル空間回転を計算
        /// </summary>
        private static Quaternion CalculateModelSpaceRotation(
            PMXBone pmxBone,
            PMXDocument doc,
            int boneIndex,
            bool convertCoordinate)
        {
            Vector3 localX, localZ;

            bool hasLocalAxis = (pmxBone.Flags & FLAG_LOCAL_AXIS) != 0;

            if (hasLocalAxis)
            {
                // ローカル軸が定義されている場合
                localX = pmxBone.LocalAxisX;
                localZ = pmxBone.LocalAxisZ;
            }
            else
            {
                // ローカル軸が定義されていない場合、デフォルト軸を計算
                localX = CalculateDefaultLocalAxisX(pmxBone, doc, boneIndex);
                localZ = Vector3.forward; // PMX座標系でのZ+（前方向）
            }

            // 座標系変換
            if (convertCoordinate)
            {
                localX = ConvertDirection(localX);
                localZ = ConvertDirection(localZ);
            }

            // 正規化
            localX = localX.normalized;
            localZ = localZ.normalized;

            // Y軸を計算: Y = Z × X
            Vector3 localY = Vector3.Cross(localZ, localX);

            // 数値誤差チェック
            if (localY.sqrMagnitude < 1e-10f)
            {
                // 軸が平行または退化している場合、デフォルトで復元
                Debug.LogWarning($"[PmxBoneBuilder] Bone '{pmxBone.Name}' has degenerate local axis. Using default.");
                localY = Vector3.up;
                localZ = Vector3.Cross(localX, localY).normalized;
                localY = Vector3.Cross(localZ, localX).normalized;
            }
            else
            {
                localY = localY.normalized;
                // Zを直交化
                localZ = Vector3.Cross(localX, localY).normalized;
            }

            // デバッグ: Y軸が下向きの場合に警告
            if (localY.y < -0.5f)
            {
                Debug.LogWarning($"[PmxBoneBuilder] Bone '{pmxBone.Name}' has downward-pointing Y axis (Y.y = {localY.y:F3}). This may cause issues with mirrored animations.");
            }

            // 回転行列からQuaternionを生成
            return CreateRotationFromAxes(localX, localY, localZ);
        }

        /// <summary>
        /// ローカル軸が未定義の場合のデフォルトX軸を計算
        /// （親→自分の方向、または自分→子の方向）
        /// </summary>
        private static Vector3 CalculateDefaultLocalAxisX(PMXBone pmxBone, PMXDocument doc, int boneIndex)
        {
            // 接続先がある場合、その方向をX軸とする
            bool connected = (pmxBone.Flags & 0x0001) != 0;

            if (connected && pmxBone.ConnectBoneIndex >= 0 && pmxBone.ConnectBoneIndex < doc.Bones.Count)
            {
                // 接続先ボーンへの方向
                var connectBone = doc.Bones[pmxBone.ConnectBoneIndex];
                Vector3 direction = connectBone.Position - pmxBone.Position;
                if (direction.sqrMagnitude > 1e-10f)
                {
                    return direction.normalized;
                }
            }
            else if (pmxBone.ConnectOffset.sqrMagnitude > 1e-10f)
            {
                // オフセットが定義されている場合
                return pmxBone.ConnectOffset.normalized;
            }

            // 子ボーンを探す
            for (int i = 0; i < doc.Bones.Count; i++)
            {
                if (doc.Bones[i].ParentIndex == boneIndex)
                {
                    Vector3 direction = doc.Bones[i].Position - pmxBone.Position;
                    if (direction.sqrMagnitude > 1e-10f)
                    {
                        return direction.normalized;
                    }
                }
            }

            // どれも見つからない場合、PMX座標系のX+
            return Vector3.right;
        }

        /// <summary>
        /// 3つの軸からQuaternionを生成
        /// </summary>
        private static Quaternion CreateRotationFromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            return m.rotation;
        }

        /// <summary>
        /// PMX座標系からUnity座標系への位置変換
        /// PMX: X右, Y上, Z奥（前）
        /// Unity: X右, Y上, Z手前
        /// </summary>
        private static Vector3 ConvertPosition(Vector3 pmxPos, bool convert)
        {
            if (!convert) return pmxPos;
            return new Vector3(pmxPos.x, pmxPos.y, -pmxPos.z);
        }

        /// <summary>
        /// PMX座標系からUnity座標系への方向変換（Z反転）
        /// </summary>
        private static Vector3 ConvertDirection(Vector3 pmxDir)
        {
            return new Vector3(pmxDir.x, pmxDir.y, -pmxDir.z);
        }

        // ================================================================
        // デバッグ用
        // ================================================================

        /// <summary>
        /// ボーンのローカル軸情報をデバッグ出力
        /// </summary>
        public static void DebugPrintBoneAxes(PMXDocument doc, bool convertCoordinate = true)
        {
            Debug.Log($"=== PMX Bone Axes Debug ({doc.Bones.Count} bones) ===");

            foreach (var bone in doc.Bones)
            {
                bool hasLocalAxis = (bone.Flags & FLAG_LOCAL_AXIS) != 0;

                if (!hasLocalAxis) continue;

                Vector3 x = bone.LocalAxisX;
                Vector3 z = bone.LocalAxisZ;

                if (convertCoordinate)
                {
                    x = ConvertDirection(x);
                    z = ConvertDirection(z);
                }

                Vector3 y = Vector3.Cross(z, x).normalized;

                Debug.Log($"Bone: {bone.Name}");
                Debug.Log($"  LocalX: ({x.x:F4}, {x.y:F4}, {x.z:F4})");
                Debug.Log($"  LocalZ: ({z.x:F4}, {z.y:F4}, {z.z:F4})");
                Debug.Log($"  LocalY (Z×X): ({y.x:F4}, {y.y:F4}, {y.z:F4})");
                Debug.Log($"  Y.y sign: {(y.y >= 0 ? "+" : "-")} ({y.y:F4})");
            }
        }
    }
}
