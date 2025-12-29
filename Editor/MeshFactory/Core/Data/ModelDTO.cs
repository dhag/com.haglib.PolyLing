// Assets/Editor/MeshFactory/Serialization/ModelData.cs
// モデルファイル (.mfmodel) のシリアライズ用データ構造
// Phase7: マルチマテリアル対応版

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshFactory.Serialization
{
    // ================================================================
    // モデルファイル全体
    // ================================================================

    /// <summary>
    /// モデルファイルのルートデータ
    /// </summary>
    [Serializable]
    public class ModelDTO
    {
        /// <summary>モデル名</summary>
        public string name;

        /// <summary>メッシュコンテキストリスト</summary>
        public List<MeshDTO> meshDTOList = new List<MeshDTO>();

        /// <summary>WorkPlane設定</summary>
        public WorkPlaneDTO workPlane;

        /// <summary>エディタ状態</summary>
        public EditorStateDTO editorStateDTO;

        // ================================================================
        // マテリアル（Phase 1: モデル単位に集約）
        // ================================================================

        /// <summary>マテリアルリスト（アセットパス）</summary>
        public List<string> materials = new List<string>();

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int currentMaterialIndex = 0;

        /// <summary>デフォルトマテリアルリスト（アセットパス）</summary>
        public List<string> defaultMaterials = new List<string>();

        /// <summary>デフォルトマテリアルインデックス</summary>
        public int defaultCurrentMaterialIndex = 0;

        /// <summary>自動デフォルトマテリアル設定</summary>
        public bool autoSetDefaultMaterials = true;

        // === ファクトリメソッド ===

        public static ModelDTO Create(string modelName)
        {
            return new ModelDTO
            {
                name = modelName
            };
        }
    }

    // ================================================================
    // メッシュコンテキスト
    // ================================================================

    /// <summary>
    /// 個別メッシュコンテキストのデータ
    /// </summary>
    [Serializable]
    public class MeshDTO
    {
        /// <summary>メッシュ名</summary>
        public string name;

        /// <summary>エクスポート時のトランスフォーム設定</summary>
        public BoneTransformDTO exportSettingsDTO;

        /// <summary>頂点データ</summary>
        public List<VertexDTO> vertices = new List<VertexDTO>();

        /// <summary>面データ</summary>
        public List<FaceDTO> faces = new List<FaceDTO>();

        /// <summary>選択中の頂点インデックス</summary>
        public List<int> selectedVertices = new List<int>();

        /// <summary>マテリアルリスト（アセットパス）</summary>
        public List<string> materialPathList = new List<string>();

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int currentMaterialIndex = 0;

        // ================================================================
        // 階層情報
        // ================================================================

        /// <summary>メッシュの種類 ("Mesh", "Bone", "Helper", "Group")</summary>
        public string type = "Mesh";

        /// <summary>親メッシュのインデックス（-1=ルート）</summary>
        public int parentIndex = -1;

        /// <summary>階層深度（MQO互換、0=ルート）</summary>
        public int depth = 0;

        /// <summary>ゲームオブジェクト階層の親（将来用）</summary>
        public int hierarchyParentIndex = -1;

        /// <summary>可視状態</summary>
        public bool isVisible = true;

        /// <summary>編集禁止（ロック）</summary>
        public bool isLocked = false;

        // ================================================================
        // ミラー設定
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int mirrorType = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int mirrorAxis = 1;

        /// <summary>ミラー距離</summary>
        public float mirrorDistance = 0f;
    }

    // ================================================================
    // 頂点データ
    // ================================================================

    /// <summary>
    /// 頂点データ（効率的な配列形式）
    /// </summary>
    [Serializable]
    public class VertexDTO
    {
        /// <summary>頂点ID（0=未設定、null可能で後方互換）</summary>
        public int? id;

        /// <summary>位置 [x, y, z]</summary>
        public float[] p;

        /// <summary>UV座標リスト [[u,v], [u,v], ...]</summary>
        public List<float[]> uv;

        /// <summary>法線リスト [[x,y,z], [x,y,z], ...]</summary>
        public List<float[]> n;

        /// <summary>
        /// ボーンウェイト [i0, i1, i2, i3, w0, w1, w2, w3]
        /// null = スキニングなし
        /// </summary>
        public float[] bw;

        // === 変換ヘルパー ===

        public Vector3 GetPosition()
        {
            if (p == null || p.Length < 3) return Vector3.zero;
            return new Vector3(p[0], p[1], p[2]);
        }

        public void SetPosition(Vector3 pos)
        {
            p = new float[] { pos.x, pos.y, pos.z };
        }

        public List<Vector2> GetUVs()
        {
            var result = new List<Vector2>();
            if (uv != null)
            {
                foreach (var u in uv)
                {
                    if (u != null && u.Length >= 2)
                        result.Add(new Vector2(u[0], u[1]));
                }
            }
            return result;
        }

        public void SetUVs(List<Vector2> uvs)
        {
            uv = new List<float[]>();
            foreach (var u in uvs)
            {
                uv.Add(new float[] { u.x, u.y });
            }
        }

        public List<Vector3> GetNormals()
        {
            var result = new List<Vector3>();
            if (n != null)
            {
                foreach (var normal in n)
                {
                    if (normal != null && normal.Length >= 3)
                        result.Add(new Vector3(normal[0], normal[1], normal[2]));
                }
            }
            return result;
        }

        public void SetNormals(List<Vector3> normals)
        {
            n = new List<float[]>();
            foreach (var normal in normals)
            {
                n.Add(new float[] { normal.x, normal.y, normal.z });
            }
        }

        public BoneWeight? GetBoneWeight()
        {
            if (bw == null || bw.Length < 8)
                return null;

            return new BoneWeight
            {
                boneIndex0 = (int)bw[0],
                boneIndex1 = (int)bw[1],
                boneIndex2 = (int)bw[2],
                boneIndex3 = (int)bw[3],
                weight0 = bw[4],
                weight1 = bw[5],
                weight2 = bw[6],
                weight3 = bw[7]
            };
        }

        public void SetBoneWeight(BoneWeight? boneWeight)
        {
            if (!boneWeight.HasValue)
            {
                bw = null;
                return;
            }

            var b = boneWeight.Value;
            bw = new float[]
            {
                b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3,
                b.weight0, b.weight1, b.weight2, b.weight3
            };
        }
    }

    // ================================================================
    // 面データ
    // ================================================================

    /// <summary>
    /// 面データ
    /// </summary>
    [Serializable]
    public class FaceDTO
    {
        /// <summary>面ID（0=未設定、null可能で後方互換）</summary>
        public int? id;

        /// <summary>頂点インデックスリスト</summary>
        public List<int> v;

        /// <summary>UVサブインデックスリスト</summary>
        public List<int> uvi;

        /// <summary>法線サブインデックスリスト</summary>
        public List<int> ni;

        /// <summary>マテリアルインデックス（省略時は0）</summary>
        public int? mi;
    }

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

        /// <summary>選択中のメッシュインデックス</summary>
        public int selectedMeshIndex;

        //ナイフツールの固有設定
        /// <summary>ナイフツールのモード</summary>
        public string knifeMode;

        /// <summary>ナイフツールのEdgeSelect</summary>
        public bool knifeEdgeSelect;

        /// <summary>ナイフツールのChainMode</summary>
        public bool knifeChainMode;

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
                //ナイフツールの固有設定
                knifeMode = "Cut",
                knifeEdgeSelect = false,
                knifeChainMode = false
            };
        }
    }
}
