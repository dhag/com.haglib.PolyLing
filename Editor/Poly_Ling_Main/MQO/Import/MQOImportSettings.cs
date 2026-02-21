// Assets/Editor/Poly_Ling/MQO/Import/MQOImportSettings.cs
// MQOインポート設定
// IToolSettings実装でUndo対応
// v1.1: ImportMode追加

using System;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOインポートモード
    /// </summary>
    public enum MQOImportMode
    {
        /// <summary>新規モデルとして追加（デフォルト）</summary>
        NewModel,
        /// <summary>既存メッシュに追加（マテリアルインデックス補正あり）</summary>
        Append,
        /// <summary>既存メッシュを置換（全削除後にインポート）</summary>
        Replace
    }

    /// <summary>
    /// 法線計算モード
    /// </summary>
    public enum NormalMode
    {
        /// <summary>面法線をそのまま使用（フラットシェーディング、エディット向け）</summary>
        FaceNormal,
        /// <summary>スムージング角度で平均化（スムーズシェーディング）</summary>
        Smooth,
        /// <summary>Unity標準のRecalculateNormalsを使用</summary>
        Unity,
    }

    /// <summary>
    /// MQOインポート設定
    /// </summary>
    [Serializable]
    public class MQOImportSettings : IToolSettings
    {
        // ================================================================
        // インポートモード（v1.2: NewModelがデフォルト）
        // ================================================================

        /// <summary>インポートモード</summary>
        [Tooltip("NewModel: 新規モデルとして追加, Append: 既存メッシュに追加, Replace: 既存メッシュを全削除してからインポート")]
        public MQOImportMode ImportMode = MQOImportMode.NewModel;

        // ================================================================
        // 座標変換設定
        // ================================================================

        /// <summary>スケール（MQO→Unity）デフォルト0.1（1/10）</summary>
        [Tooltip("MQO座標をUnity座標に変換するスケール（デフォルト: 0.1 = 1/10）")]
        public float Scale = 0.1f;

        /// <summary>Z軸反転</summary>
        [Tooltip("Z軸を反転する（MQOは右手系、Unityは左手系）")]
        public bool FlipZ = true;

        /// <summary>UV V座標反転</summary>
        [Tooltip("UV座標のV成分を反転する")]
        public bool FlipUV_V = true;

        // ================================================================
        // インポートオプション
        // ================================================================

        /// <summary>マテリアルをインポート</summary>
        [Tooltip("MQOマテリアルをUnityマテリアルに変換する")]
        public bool ImportMaterials = true;

        /// <summary>非表示オブジェクトをスキップ</summary>
        [Tooltip("MQOで非表示に設定されているオブジェクトをスキップする")]
        public bool SkipHiddenObjects = false;

        /// <summary>空のオブジェクトをスキップ</summary>
        [Tooltip("頂点や面を持たないオブジェクト（グループ用ダミー等）をスキップする")]
        public bool SkipEmptyObjects = false;

        /// <summary>全オブジェクトを統合</summary>
        [Tooltip("全てのオブジェクトを1つのメッシュに統合する")]
        public bool MergeObjects = false;

        /// <summary>MQOファイルからボーンインデックスを読み込まない</summary>
        [Tooltip("MQO特殊面からボーンインデックス情報を読み込まない")]
        public bool SkipMqoBoneIndices = false;

        /// <summary>MQOファイルからウェイトを読み込まない</summary>
        [Tooltip("MQO特殊面からボーンウェイト情報を読み込まない")]
        public bool SkipMqoBoneWeights = false;

        // ================================================================
        // 詳細設定
        // ================================================================

        /// <summary>法線計算モード</summary>
        [Tooltip("FaceNormal: 面法線そのまま（フラット）, Smooth: スムージング")]
        public NormalMode NormalMode = NormalMode.Smooth;

        /// <summary>スムージング角度（度）</summary>
        [Tooltip("法線スムージングの閾値角度（NormalMode=Smoothの時のみ有効）")]
        [Range(0f, 180f)]
        public float SmoothingAngle = 60f;

        // ================================================================
        // ボーンウェイト設定（オプション）
        // ================================================================

        /// <summary>ボーンウェイトCSVファイルパス（空の場合は適用しない）</summary>
        [Tooltip("ボーンウェイト情報を含むCSVファイル（MqoObjectName,VertexID,VertexIndex,Bone0-3,Weight0-3）")]
        public string BoneWeightCSVPath = "";

        /// <summary>ボーンウェイトCSVを使用するか</summary>
        public bool UseBoneWeightCSV => !string.IsNullOrEmpty(BoneWeightCSVPath);

        /// <summary>ボーン定義CSVファイルパス（PmxBone形式）</summary>
        [Tooltip("ボーン定義を含むCSVファイル（PmxBone形式）")]
        public string BoneCSVPath = "";

        /// <summary>ボーンCSVを使用するか</summary>
        public bool UseBoneCSV => !string.IsNullOrEmpty(BoneCSVPath);

        /// <summary>ボーンスケール（PMXボーン座標に適用、デフォルト1.0）</summary>
        [Tooltip("PMXボーン座標に適用するスケール（MQOと同じScaleを使う場合は1.0）")]
        public float BoneScale = 10.0f;//pmx由来のボーン位置をMQOスケールに合わせるためのデフォルト値

        // ================================================================
        // ミラー設定
        // ================================================================

        /// <summary>ミラーをベイク（実体化）</summary>
        [Tooltip("ミラー属性を持つメッシュのミラー側を実体メッシュとして生成する")]
        public bool BakeMirror = true;

        /// <summary>__Armature__からボーンをインポート</summary>
        [Tooltip("MQO内の__Armature__オブジェクト以下をボーン構造としてインポートする")]
        public bool ImportBonesFromArmature = true;

        /// <summary>AポーズをTポーズに変換</summary>
        [Tooltip("腕ボーンを水平に回転させてTポーズに変換する（スキニング頂点も変換）")]
        public bool ConvertToTPose = false;

        // ================================================================
        // 頂点デバッグ設定
        // ================================================================

        /// <summary>頂点デバッグログを出力</summary>
        [Tooltip("メッシュオブジェクトごとの頂点情報をコンソールに出力する")]
        public bool DebugVertexInfo = false;

        /// <summary>同一頂点・近接UV検出時に出力する件数</summary>
        [Tooltip("同一頂点で異なるUVを持つペアの出力件数（近い順）")]
        [Range(1, 100)]
        public int DebugVertexNearUVCount = 10;

        // ================================================================
        // テクスチャ読み込み用
        // ================================================================

        /// <summary>MQOファイルのベースディレクトリ（テクスチャ相対パス解決用）</summary>
        [NonSerialized]
        public string BaseDir = "";

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>デフォルト設定を作成</summary>
        public static MQOImportSettings CreateDefault()
        {
            return new MQOImportSettings();
        }

        /// <summary>MMD互換設定を作成（1/10スケール、Z反転）</summary>
        public static MQOImportSettings CreateMMDCompatible()
        {
            return new MQOImportSettings
            {
                Scale = 0.1f,
                FlipZ = true,
                FlipUV_V = false
            };
        }

        /// <summary>
        /// 座標系設定から初期化
        /// MQO→Unity = coordinateScale / ratio
        /// BoneScale: PMXボーン座標→MQOメッシュ座標の変換（= ratio）
        /// </summary>
        public static MQOImportSettings CreateFromCoordinate(float coordinateScale, bool coordinateFlipZ, float ratio = 10f)
        {
            float mqoToUnity = coordinateScale / ratio;
            return new MQOImportSettings
            {
                Scale = mqoToUnity,
                FlipZ = coordinateFlipZ,
                FlipUV_V = true,
                BoneScale = ratio  // PMXボーン座標→MQO座標の比率
            };
        }

        /// <summary>等倍設定を作成（スケール1:1）</summary>
        public static MQOImportSettings CreateNoScale()
        {
            return new MQOImportSettings
            {
                Scale = 1f,
                FlipZ = true,
                FlipUV_V = false
            };
        }

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new MQOImportSettings
            {
                ImportMode = this.ImportMode,
                Scale = this.Scale,
                FlipZ = this.FlipZ,
                FlipUV_V = this.FlipUV_V,
                ImportMaterials = this.ImportMaterials,
                SkipHiddenObjects = this.SkipHiddenObjects,
                SkipEmptyObjects = this.SkipEmptyObjects,
                MergeObjects = this.MergeObjects,
                SkipMqoBoneIndices = this.SkipMqoBoneIndices,
                SkipMqoBoneWeights = this.SkipMqoBoneWeights,
                NormalMode = this.NormalMode,
                SmoothingAngle = this.SmoothingAngle,
                BoneWeightCSVPath = this.BoneWeightCSVPath,
                BakeMirror = this.BakeMirror,
                ImportBonesFromArmature = this.ImportBonesFromArmature,
                ConvertToTPose = this.ConvertToTPose,
                DebugVertexInfo = this.DebugVertexInfo,
                DebugVertexNearUVCount = this.DebugVertexNearUVCount
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not MQOImportSettings o)
                return true;

            return ImportMode != o.ImportMode ||
                   !Mathf.Approximately(Scale, o.Scale) ||
                   FlipZ != o.FlipZ ||
                   FlipUV_V != o.FlipUV_V ||
                   ImportMaterials != o.ImportMaterials ||
                   SkipHiddenObjects != o.SkipHiddenObjects ||
                   SkipEmptyObjects != o.SkipEmptyObjects ||
                   MergeObjects != o.MergeObjects ||
                   SkipMqoBoneIndices != o.SkipMqoBoneIndices ||
                   SkipMqoBoneWeights != o.SkipMqoBoneWeights ||
                   NormalMode != o.NormalMode ||
                   !Mathf.Approximately(SmoothingAngle, o.SmoothingAngle) ||
                   BoneWeightCSVPath != o.BoneWeightCSVPath ||
                   BakeMirror != o.BakeMirror ||
                   ImportBonesFromArmature != o.ImportBonesFromArmature ||
                   ConvertToTPose != o.ConvertToTPose ||
                   DebugVertexInfo != o.DebugVertexInfo ||
                   DebugVertexNearUVCount != o.DebugVertexNearUVCount;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not MQOImportSettings o)
                return;

            ImportMode = o.ImportMode;
            Scale = o.Scale;
            FlipZ = o.FlipZ;
            FlipUV_V = o.FlipUV_V;
            ImportMaterials = o.ImportMaterials;
            SkipHiddenObjects = o.SkipHiddenObjects;
            SkipEmptyObjects = o.SkipEmptyObjects;
            MergeObjects = o.MergeObjects;
            SkipMqoBoneIndices = o.SkipMqoBoneIndices;
            SkipMqoBoneWeights = o.SkipMqoBoneWeights;
            NormalMode = o.NormalMode;
            SmoothingAngle = o.SmoothingAngle;
            BoneWeightCSVPath = o.BoneWeightCSVPath;
            BakeMirror = o.BakeMirror;
            ImportBonesFromArmature = o.ImportBonesFromArmature;
            ConvertToTPose = o.ConvertToTPose;
            DebugVertexInfo = o.DebugVertexInfo;
            DebugVertexNearUVCount = o.DebugVertexNearUVCount;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>デフォルトにリセット</summary>
        public void Reset()
        {
            var def = CreateDefault();
            CopyFrom(def);
        }
    }
}
