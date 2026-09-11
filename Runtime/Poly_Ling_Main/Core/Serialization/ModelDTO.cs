// Assets/Editor/Poly_Ling/Serialization/ModelDTO.cs
// モデルファイル (.mfmodel) のシリアライズ用データ構造
// Phase7: マルチマテリアル対応版
// Phase8: 選択状態シリアライズ対応（Edge/Face/Line/Mode）
// Phase9: 選択セット対応
// Phase Morph: モーフ基準データ対応
// Phase BonePose: BonePoseData対応
//
// 【分割先】このファイルから次へ分けてある。
//   ModelDTO.Material.cs  DTO：マテリアル参照データ。
//   ModelDTO.Mesh.cs      DTO：メッシュコンテキスト・頂点・面・MeshMetaDTO・MeshGeoDTO。
//   ModelDTO.Morph.cs     DTO：モーフ基準データとミラーペア。
//   ModelDTO.Rig.cs       DTO：IK／剛体／JOINT・スプリングボーン・VRM 1.0 モデルレベル設定・Avatar リターゲット・座標規約。
//   ModelDTO.Settings.cs  DTO：エクスポート設定・WorkPlane・エディタ状態。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

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

        /// <summary>作業用ローカル軸（回転 / 曲げの基準フレーム）。null=既定値。</summary>
        public WorkAxisDTO workAxis;

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
        //   ※#5b: モデルレベルの humanoidBoneMapping（Dict）は撤去。
        //     Humanoid 割当は per-bone（MeshDTO.humanBodyBone）を正とし、
        //     読込後 HumanoidMappingResolver.RebuildMappingFromPerBone で Dict を再構築する。

        // ================================================================
        // モーフエクスプレッション
        // ================================================================

        /// <summary>
        /// モーフエクスプレッション一覧
        /// 複数メッシュのモーフをグループ化
        /// </summary>
        public List<MorphExpressionDTO> morphExpressions = new List<MorphExpressionDTO>();

        // ================================================================
        // メッシュ選択セット（名前ベース）
        // ================================================================

        /// <summary>メッシュ選択セット</summary>
        public List<MeshSelectionSetDTO> meshSelectionSets = new List<MeshSelectionSetDTO>();

        // ================================================================
        // オブジェクトグループ（入力ソース＋生成パラメータ＋出力先）
        // ================================================================

        /// <summary>
        /// オブジェクトグループ一覧。
        /// 参照は ObjectId で持つので、メッシュの並べ替えでは付け替えが要らない。
        /// </summary>
        public List<ObjectGroupDTO> objectGroups = new List<ObjectGroupDTO>();

        // ================================================================
        // DataStore（コマンドが返した実データの辞書）
        // ================================================================

        /// <summary>
        /// 結果辞書の項目一覧。
        /// 参照は ObjectGroups と同じく ObjectId を併せて持つので、
        /// メッシュの並べ替えでは付け替えが要らない。
        /// </summary>
        public List<PLDataEntryDTO> dataStore = new List<PLDataEntryDTO>();

        // ================================================================
        // MirrorPair（ミラーペア情報）
        // ================================================================

        /// <summary>ミラーペア情報（Real↔Mirror のメッシュインデックスペア）</summary>
        public List<MirrorPairDTO> mirrorPairs = new List<MirrorPairDTO>();

        // ================================================================
        // スプリングボーン・コライダーグループ（モデルレベル：名前のみ）
        // ================================================================

        /// <summary>スプリングボーン・コライダーグループ名リスト（index＝並び順）。</summary>
        public List<string> springBoneColliderGroupNames = new List<string>();

        // ================================================================
        // スプリングボーン・評価設定（モデルレベル）
        // ================================================================

        /// <summary>揺れ評価の固定タイムステップ[秒]。0=実時間。</summary>
        public float springBoneFixedDeltaTime = 0f;

        /// <summary>揺れ評価開始直後の安定化フレーム数。</summary>
        public int springBoneWarmupFrames = 3;

        // ================================================================
        // TPoseバックアップ（Tポーズ変換前の姿勢。CSV/JSON 対称：規約4）
        // ================================================================

        /// <summary>Tポーズ変換前バックアップ（null=バックアップ無し）。</summary>
        public TPoseBackupDTO tPoseBackup;

        // ================================================================
        // VRM 1.0 モデルレベル設定（CSV/JSON 対称：規約4）
        // ================================================================

        /// <summary>VRM メタ情報（null=未設定）。</summary>
        public VrmMetaDTO vrmMeta;

        /// <summary>VRM 視線設定（null=未設定）。</summary>
        public VrmLookAtDTO vrmLookAt;

        // ================================================================
        // Avatar リターゲット設定（CSV/JSON 対称：規約4）
        // ================================================================

        /// <summary>Avatar リターゲット設定8項目（null=未設定）。</summary>
        public AvatarRetargetDTO avatarRetarget;

        // ================================================================
        // PMX / MQO の座標規約（CSV/JSON 対称：規約4）
        // ================================================================

        /// <summary>PMX / MQO の座標規約（null=未設定）。</summary>
        public CoordinateConventionDTO coordinateConvention;

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
    // TPoseバックアップ DTO（純POCO・Unity型非依存）
    //   規約: MeshObject.cs「ボーン付帯データ格納規約」に準拠。
    //   参照は MeshContext index（TPoseBackup 実体が index キーのため）。
    //   行列は 16 要素 row-major、Vector3 は [x,y,z]、頂点列は flat xyz。
    // ================================================================

    [Serializable]
    public class TPoseBackupDTO
    {
        /// <summary>ボーン別ローカル回転（Euler）。</summary>
        public List<TPoseBoneRotDTO> boneRotations = new List<TPoseBoneRotDTO>();

        /// <summary>ボーン別 WorldMatrix。</summary>
        public List<TPoseMatrixDTO> worldMatrices = new List<TPoseMatrixDTO>();

        /// <summary>ボーン別 BindPose。</summary>
        public List<TPoseMatrixDTO> bindPoses = new List<TPoseMatrixDTO>();

        /// <summary>メッシュ別 頂点座標バックアップ。</summary>
        public List<TPoseVtxPosDTO> vertexPositions = new List<TPoseVtxPosDTO>();
    }

    /// <summary>ボーン回転バックアップ1件（index＝MeshContext index、rot=[x,y,z]）。</summary>
    [Serializable]
    public class TPoseBoneRotDTO
    {
        public int index;
        public float[] rot;
    }

    /// <summary>行列バックアップ1件（index＝MeshContext index、m=16 row-major）。</summary>
    [Serializable]
    public class TPoseMatrixDTO
    {
        public int index;
        public float[] m;
    }

    /// <summary>頂点座標バックアップ1件（index＝MeshContext index、p=flat xyz）。</summary>
    [Serializable]
    public class TPoseVtxPosDTO
    {
        public int index;
        public float[] p;
    }
}
