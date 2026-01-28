// Assets/Editor/Poly_Ling/Serialization/ModelDTO.cs
// モデルファイル (.mfmodel) のシリアライズ用データ構造
// Phase7: マルチマテリアル対応版
// Phase8: 選択状態シリアライズ対応（Edge/Face/Line/Mode）
// Phase9: 選択セット対応
// Phase Morph: モーフ基準データ対応

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;

namespace Poly_Ling.Serialization
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

        /// <summary>[廃止] マテリアルリスト（アセットパス）- 読み込み時に警告を出力</summary>
        [System.Obsolete("materialReferencesを使用してください")]
        public List<string> materials = new List<string>();

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int currentMaterialIndex = 0;

        /// <summary>[廃止] デフォルトマテリアルリスト（アセットパス）- 読み込み時に警告を出力</summary>
        [System.Obsolete("defaultMaterialReferencesを使用してください")]
        public List<string> defaultMaterials = new List<string>();

        /// <summary>デフォルトマテリアルインデックス</summary>
        public int defaultCurrentMaterialIndex = 0;

        /// <summary>自動デフォルトマテリアル設定</summary>
        public bool autoSetDefaultMaterials = true;
        
        // ================================================================
        // マテリアル（新形式：パラメータデータ込み）
        // ================================================================
        
        /// <summary>マテリアル参照リスト（パス＋パラメータデータ）</summary>
        public List<MaterialReferenceDTO> materialReferences = new List<MaterialReferenceDTO>();
        
        /// <summary>デフォルトマテリアル参照リスト</summary>
        public List<MaterialReferenceDTO> defaultMaterialReferences = new List<MaterialReferenceDTO>();

        // ================================================================
        // Humanoidボーンマッピング
        // ================================================================

        /// <summary>
        /// Humanoidボーンマッピング
        /// Unity Humanoid名 → ボーンインデックス（MeshContextListのインデックス）
        /// </summary>
        public Dictionary<string, int> humanoidBoneMapping = new Dictionary<string, int>();

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

        // ================================================================
        // 拡張選択状態（Phase 8追加）
        // ================================================================

        /// <summary>選択中のエッジ [[v1,v2], [v1,v2], ...]</summary>
        public List<int[]> selectedEdges = new List<int[]>();

        /// <summary>選択中の面インデックス</summary>
        public List<int> selectedFaces = new List<int>();

        /// <summary>選択中の線分インデックス（2頂点Face）</summary>
        public List<int> selectedLines = new List<int>();

        /// <summary>選択モード ("Vertex", "Edge", "Face", "Line", or combined flags)</summary>
        public string selectMode = "Vertex";

        // ================================================================
        // 選択セット（永続的な名前付き選択）
        // ================================================================

        /// <summary>保存された選択セット</summary>
        public List<SelectionSetDTO> selectionSets = new List<SelectionSetDTO>();

        // ================================================================
        // マテリアル [廃止セクション]
        // マテリアルはModelDTO.materialReferencesで一元管理されます
        // ================================================================

        /// <summary>[廃止] マテリアルリスト - ModelDTO.materialReferencesを使用してください</summary>
        [System.Obsolete("ModelDTO.materialReferencesを使用してください")]
        public List<string> materialPathList = new List<string>();

        /// <summary>[廃止] 現在選択中のマテリアルインデックス</summary>
        [System.Obsolete("ModelDTO.currentMaterialIndexを使用してください")]
        public int currentMaterialIndex = 0;

        // ================================================================
        // 階層情報
        // ================================================================

        /// <summary>メッシュの種類 ("Mesh", "Bone", "Helper", "Group", "Morph")</summary>
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

        /// <summary>折りたたみ状態（MQO互換）</summary>
        public bool isFolding = false;

        // ================================================================
        // ミラー設定
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int mirrorType = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int mirrorAxis = 1;

        /// <summary>ミラー距離</summary>
        public float mirrorDistance = 0f;

        /// <summary>ミラー側マテリアルオフセット（ミラー側マテリアルインデックス = 実体側 + オフセット）</summary>
        public int mirrorMaterialOffset = 0;

        /// <summary>
        /// ベイク元メッシュのインデックス（Type==BakedMirrorの時に使用）
        /// -1 = ベイクミラーではない
        /// </summary>
        public int bakedMirrorSourceIndex = -1;

        // ================================================================
        // モーフ基準データ（Phase: Morph対応）
        // ================================================================

        /// <summary>
        /// モーフ基準データ（モーフ前の位置等を保持）
        /// null = モーフではない通常メッシュ
        /// </summary>
        public MorphBaseDataDTO morphBaseData;

        /// <summary>
        /// エクスポートから除外するか
        /// true: モデルエクスポート時にこのメッシュを出力しない
        /// </summary>
        public bool excludeFromExport = false;
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
        /// <summary>頂点ID（モーフ追跡等に使用）</summary>
        public int id;

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

        /// <summary>
        /// ミラー側ボーンウェイト [i0, i1, i2, i3, w0, w1, w2, w3]
        /// null = ミラーウェイトなし
        /// </summary>
        public float[] mbw;

        /// <summary>頂点フラグ (VertexFlags)</summary>
        public byte f;

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

        public BoneWeight? GetMirrorBoneWeight()
        {
            if (mbw == null || mbw.Length < 8)
                return null;

            return new BoneWeight
            {
                boneIndex0 = (int)mbw[0],
                boneIndex1 = (int)mbw[1],
                boneIndex2 = (int)mbw[2],
                boneIndex3 = (int)mbw[3],
                weight0 = mbw[4],
                weight1 = mbw[5],
                weight2 = mbw[6],
                weight3 = mbw[7]
            };
        }

        public void SetMirrorBoneWeight(BoneWeight? boneWeight)
        {
            if (!boneWeight.HasValue)
            {
                mbw = null;
                return;
            }

            var b = boneWeight.Value;
            mbw = new float[]
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
        /// <summary>面ID（モーフ追跡等に使用）</summary>
        public int id;

        /// <summary>頂点インデックスリスト</summary>
        public List<int> v;

        /// <summary>UVサブインデックスリスト</summary>
        public List<int> uvi;

        /// <summary>法線サブインデックスリスト</summary>
        public List<int> ni;

        /// <summary>マテリアルインデックス（省略時は0）</summary>
        public int? mi;

        /// <summary>面フラグ (FaceFlags)</summary>
        public byte f;
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

    // ================================================================
    // マテリアル参照データ
    // ================================================================

    /// <summary>
    /// マテリアル参照のシリアライズ用データ
    /// アセットパス＋パラメータデータを保持
    /// </summary>
    [Serializable]
    public class MaterialReferenceDTO
    {
        /// <summary>マテリアルアセットのパス（あれば）</summary>
        public string assetPath;
        
        /// <summary>パラメータデータ</summary>
        public MaterialDataDTO data;
        
        public static MaterialReferenceDTO Create(string path = null)
        {
            return new MaterialReferenceDTO
            {
                assetPath = path,
                data = new MaterialDataDTO()
            };
        }
    }

    /// <summary>
    /// マテリアルパラメータのシリアライズ用データ
    /// </summary>
    [Serializable]
    public class MaterialDataDTO
    {
        // 基本情報
        public string name = "New Material";
        public string shaderType = "URPLit";
        
        // ベースカラー
        public float[] baseColor = new float[] { 1f, 1f, 1f, 1f };
        public string baseMapPath;
        
        // ソースパス（インポート元のパス、エクスポート時に使用）
        public string sourceTexturePath;
        public string sourceAlphaMapPath;
        public string sourceBumpMapPath;
        
        // PBRパラメータ
        public float metallic = 0f;
        public float smoothness = 0.5f;
        public string metallicMapPath;
        public string normalMapPath;
        public float normalScale = 1f;
        public string occlusionMapPath;
        public float occlusionStrength = 1f;
        
        // エミッション
        public bool emissionEnabled = false;
        public float[] emissionColor = new float[] { 0f, 0f, 0f, 1f };
        public string emissionMapPath;
        
        // レンダリング設定
        public int surface = 0;        // SurfaceType
        public int blendMode = 0;      // BlendModeType
        public int cullMode = 2;       // CullModeType.Back（表面のみ表示）
        public bool alphaClipEnabled = false;
        public float alphaCutoff = 0.5f;
    }

    // ================================================================
    // モーフ基準データ（Phase: Morph対応）
    // ================================================================

    /// <summary>
    /// モーフ基準データのシリアライズ用構造
    /// メッシュ頂点（モーフ後）と対になる基準位置（モーフ前）を保持
    /// </summary>
    [Serializable]
    public class MorphBaseDataDTO
    {
        /// <summary>モーフ名</summary>
        public string morphName = "";

        /// <summary>モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int panel = 3;

        /// <summary>作成日時（ISO 8601形式）</summary>
        public string createdAt;

        /// <summary>
        /// 基準位置（モーフ前の頂点位置）
        /// 各頂点の [x, y, z] を連続配列として格納
        /// 例: [x0,y0,z0, x1,y1,z1, x2,y2,z2, ...]
        /// </summary>
        public float[] basePositions;

        /// <summary>
        /// 基準法線（オプション）
        /// 各頂点の [x, y, z] を連続配列として格納
        /// null = 法線データなし
        /// </summary>
        public float[] baseNormals;

        /// <summary>
        /// 基準UV（オプション）
        /// 各頂点の [u, v] を連続配列として格納
        /// null = UVデータなし
        /// </summary>
        public float[] baseUVs;

        // ================================================================
        // 変換ヘルパー
        // ================================================================

        /// <summary>頂点数を取得</summary>
        public int GetVertexCount()
        {
            if (basePositions == null) return 0;
            return basePositions.Length / 3;
        }

        /// <summary>指定頂点の基準位置を取得</summary>
        public Vector3 GetBasePosition(int index)
        {
            if (basePositions == null) return Vector3.zero;
            int i = index * 3;
            if (i + 2 >= basePositions.Length) return Vector3.zero;
            return new Vector3(basePositions[i], basePositions[i + 1], basePositions[i + 2]);
        }

        /// <summary>基準位置配列を取得</summary>
        public Vector3[] GetBasePositions()
        {
            if (basePositions == null) return null;
            int count = GetVertexCount();
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 3;
                result[i] = new Vector3(basePositions[idx], basePositions[idx + 1], basePositions[idx + 2]);
            }
            return result;
        }

        /// <summary>基準位置を設定</summary>
        public void SetBasePositions(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
            {
                basePositions = null;
                return;
            }
            basePositions = new float[positions.Length * 3];
            for (int i = 0; i < positions.Length; i++)
            {
                int idx = i * 3;
                basePositions[idx] = positions[i].x;
                basePositions[idx + 1] = positions[i].y;
                basePositions[idx + 2] = positions[i].z;
            }
        }

        /// <summary>基準法線配列を取得</summary>
        public Vector3[] GetBaseNormals()
        {
            if (baseNormals == null) return null;
            int count = baseNormals.Length / 3;
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 3;
                result[i] = new Vector3(baseNormals[idx], baseNormals[idx + 1], baseNormals[idx + 2]);
            }
            return result;
        }

        /// <summary>基準法線を設定</summary>
        public void SetBaseNormals(Vector3[] normals)
        {
            if (normals == null || normals.Length == 0)
            {
                baseNormals = null;
                return;
            }
            baseNormals = new float[normals.Length * 3];
            for (int i = 0; i < normals.Length; i++)
            {
                int idx = i * 3;
                baseNormals[idx] = normals[i].x;
                baseNormals[idx + 1] = normals[i].y;
                baseNormals[idx + 2] = normals[i].z;
            }
        }

        /// <summary>基準UV配列を取得</summary>
        public Vector2[] GetBaseUVs()
        {
            if (baseUVs == null) return null;
            int count = baseUVs.Length / 2;
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 2;
                result[i] = new Vector2(baseUVs[idx], baseUVs[idx + 1]);
            }
            return result;
        }

        /// <summary>基準UVを設定</summary>
        public void SetBaseUVs(Vector2[] uvs)
        {
            if (uvs == null || uvs.Length == 0)
            {
                baseUVs = null;
                return;
            }
            baseUVs = new float[uvs.Length * 2];
            for (int i = 0; i < uvs.Length; i++)
            {
                int idx = i * 2;
                baseUVs[idx] = uvs[i].x;
                baseUVs[idx + 1] = uvs[i].y;
            }
        }

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>デフォルト作成</summary>
        public static MorphBaseDataDTO Create(string morphName = "")
        {
            return new MorphBaseDataDTO
            {
                morphName = morphName,
                panel = 3,
                createdAt = DateTime.Now.ToString("o")
            };
        }
    }
}
