// ModelDTO.Settings.cs
// DTO：エクスポート設定・WorkPlane・エディタ状態。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // エクスポート設定
    // ================================================================

    /// <summary>
    /// エクスポート時のトランスフォーム設定
    /// </summary>
    [Serializable]
    public class BoneTransformDTO
    {
        /// <summary>ローカルトランスフォームを使用するか</summary>
        public bool useLocalTransform;

        /// <summary>SkinnedMeshRendererとしてエクスポートするか</summary>
        public bool exportAsSkinned;

        /// <summary>位置 [x, y, z]</summary>
        public float[] position;

        /// <summary>回転（オイラー角） [x, y, z]</summary>
        public float[] rotation;

        /// <summary>スケール [x, y, z]</summary>
        public float[] scale;

        // === 変換ヘルパー ===

        public Vector3 GetPosition()
        {
            if (position == null || position.Length < 3) return Vector3.zero;
            return new Vector3(position[0], position[1], position[2]);
        }

        public void SetPosition(Vector3 pos)
        {
            position = new float[] { pos.x, pos.y, pos.z };
        }

        public Vector3 GetRotation()
        {
            if (rotation == null || rotation.Length < 3) return Vector3.zero;
            return new Vector3(rotation[0], rotation[1], rotation[2]);
        }

        public void SetRotation(Vector3 rot)
        {
            rotation = new float[] { rot.x, rot.y, rot.z };
        }

        public Vector3 GetScale()
        {
            if (scale == null || scale.Length < 3) return Vector3.one;
            return new Vector3(scale[0], scale[1], scale[2]);
        }

        public void SetScale(Vector3 s)
        {
            scale = new float[] { s.x, s.y, s.z };
        }

        public static BoneTransformDTO CreateDefault()
        {
            return new BoneTransformDTO
            {
                useLocalTransform = false,
                exportAsSkinned = false,
                position = new float[] { 0, 0, 0 },
                rotation = new float[] { 0, 0, 0 },
                scale = new float[] { 1, 1, 1 }
            };
        }
    }

    // ================================================================
    // WorkPlane設定
    // ================================================================

    /// <summary>
    /// WorkPlane設定データ
    /// </summary>
    [Serializable]
    public class WorkPlaneDTO
    {
        /// <summary>モード ("CameraParallel", "WorldXY", "WorldXZ", "WorldYZ", "Custom")</summary>
        public string mode;

        /// <summary>原点 [x, y, z]</summary>
        public float[] origin;

        /// <summary>U軸 [x, y, z]</summary>
        public float[] axisU;

        /// <summary>V軸 [x, y, z]</summary>
        public float[] axisV;

        /// <summary>ロック状態</summary>
        public bool isLocked;

        /// <summary>軸ロック</summary>
        public bool lockOrientation;

        /// <summary>選択時の原点自動更新</summary>
        public bool autoUpdateOriginOnSelection;

        public static WorkPlaneDTO CreateDefault()
        {
            return new WorkPlaneDTO
            {
                mode = "CameraParallel",
                origin = new float[] { 0, 0, 0 },
                axisU = new float[] { 1, 0, 0 },
                axisV = new float[] { 0, 1, 0 },
                isLocked = false,
                lockOrientation = false,
                autoUpdateOriginOnSelection = true
            };
        }
    }

    // ================================================================
    // エディタ状態
    // ================================================================

    /// <summary>
    /// エディタ状態データ
    /// v2.0: 選択メッシュ/選択ボーン/選択頂点モーフに分離
    /// </summary>
    [Serializable]
    public class EditorStateDTO
    {
        /// <summary>カメラ回転X</summary>
        public float rotationX;

        /// <summary>カメラ回転Y</summary>
        public float rotationY;

        /// <summary>カメラ距離</summary>
        public float cameraDistance;

        /// <summary>カメラターゲット [x, y, z]</summary>
        public float[] cameraTarget;

        /// <summary>ワイヤーフレーム表示</summary>
        public bool showWireframe;

        /// <summary>頂点表示</summary>
        public bool showVertices;

        /// <summary>頂点編集モード</summary>
        public bool vertexEditMode;

        /// <summary>現在のツール名</summary>
        public string currentToolName;

        /// <summary>選択中のメッシュインデックス（Mesh, BakedMirror タイプ）</summary>
        public int selectedMeshIndex;

        /// <summary>選択中のボーンインデックス（Bone タイプ）</summary>
        public int selectedBoneIndex = -1;

        /// <summary>選択中の頂点モーフインデックス（Morph タイプ）</summary>
        public int selectedVertexMorphIndex = -1;

        //ナイフツールの固有設定
        /// <summary>ナイフツールのモード</summary>
        public string knifeMode;

        /// <summary>ナイフツールのEdgeSelect</summary>
        public bool knifeEdgeSelect;

        /// <summary>ナイフツールのChainMode</summary>
        public bool knifeChainMode;

        /// <summary>PMX→Unity座標比率（デフォルト0.1、旧0.085）</summary>
        public float pmxUnityRatio = 0.1f;// 0.085f;

        /// <summary>PMX X軸反転（デフォルトtrue）</summary>
        public bool pmxFlipX = true;

        /// <summary>PMX Z軸反転（デフォルトtrue。X と併用で Y軸180°回転）</summary>
        public bool pmxFlipZ = true;

        /// <summary>MQO X軸反転（デフォルトtrue）</summary>
        public bool mqoFlipX = true;

        /// <summary>MQO Z軸反転（デフォルトfalse）</summary>
        public bool mqoFlipZ = false;

        /// <summary>MQO→Unity座標比率（デフォルト0.01）</summary>
        public float mqoUnityRatio = 0.01f;

        /// <summary>ボーン表示</summary>
        public bool showBones = true;

        /// <summary>非選択ボーンも表示</summary>
        public bool showUnselectedBones = false;

        /// <summary>ボーン形状をY軸方向に表示</summary>
        public bool boneDisplayAlongY = false;

        public static EditorStateDTO CreateDefault()
        {
            return new EditorStateDTO
            {
                rotationX = 20f,
                rotationY = 0f,
                cameraDistance = 2f,
                cameraTarget = new float[] { 0, 0, 0 },
                showWireframe = true,
                showVertices = true,
                vertexEditMode = true,
                currentToolName = "Select",
                selectedMeshIndex = -1,
                selectedBoneIndex = -1,
                selectedVertexMorphIndex = -1,
                //ナイフツールの固有設定
                knifeMode = "Cut",
                knifeEdgeSelect = false,
                knifeChainMode = false,
                // 座標系設定
                pmxUnityRatio = 0.1f,// 0.085f,
                pmxFlipX = true,
                pmxFlipZ = true,
                mqoFlipX = true,
                mqoFlipZ = false,
                mqoUnityRatio = 0.01f
            };
        }
    }
}
