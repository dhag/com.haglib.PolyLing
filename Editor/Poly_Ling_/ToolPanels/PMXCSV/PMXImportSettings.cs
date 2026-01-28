// Assets/Editor/Poly_Ling/PMX/Import/PMXImportSettings.cs
// PMX CSVインポート設定
// IToolSettings実装でUndo対応

using System;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXインポートモード
    /// </summary>
    public enum PMXImportMode
    {
        /// <summary>新規モデルとして追加（デフォルト）</summary>
        NewModel,
        /// <summary>既存メッシュに追加（マテリアルインデックス補正あり）</summary>
        Append,
        /// <summary>既存メッシュを置換（全削除後にインポート）</summary>
        Replace
    }

    /// <summary>
    /// PMXインポート対象（フラグ）
    /// </summary>
    [Flags]
    public enum PMXImportTarget
    {
        /// <summary>なし</summary>
        None = 0,
        /// <summary>メッシュ（頂点・面・マテリアル）</summary>
        Mesh = 1 << 0,
        /// <summary>ボーン</summary>
        Bones = 1 << 1,
        /// <summary>モーフ（将来用）</summary>
        Morphs = 1 << 2,
        /// <summary>剛体（将来用）</summary>
        Bodies = 1 << 3,
        /// <summary>ジョイント（将来用）</summary>
        Joints = 1 << 4,
        
        /// <summary>全て</summary>
        All = Mesh | Bones | Morphs | Bodies | Joints,
        /// <summary>デフォルト（メッシュ＋ボーン）</summary>
        Default = Mesh | Bones,
        /// <summary>ボーンのみ</summary>
        BonesOnly = Bones,
        /// <summary>物理（剛体＋ジョイント）</summary>
        Physics = Bodies | Joints
    }

    /// <summary>
    /// PMXインポート設定
    /// </summary>
    [Serializable]
    public class PMXImportSettings : IToolSettings
    {
        // ================================================================
        // インポートモード（v1.2: NewModelがデフォルト）
        // ================================================================

        /// <summary>インポートモード</summary>
        [Tooltip("NewModel: 新規モデルとして追加, Append: 既存メッシュに追加, Replace: 既存メッシュを全削除してからインポート")]
        public PMXImportMode ImportMode = PMXImportMode.NewModel;

        // ================================================================
        // インポート対象（v1.3追加）
        // ================================================================

        /// <summary>インポート対象フラグ</summary>
        [Tooltip("インポートする対象を選択（複数選択可）")]
        public PMXImportTarget ImportTarget = PMXImportTarget.Default;

        // ヘルパープロパティ
        public bool ShouldImportMesh => (ImportTarget & PMXImportTarget.Mesh) != 0;
        public bool ShouldImportBones => (ImportTarget & PMXImportTarget.Bones) != 0;
        public bool ShouldImportMorphs => (ImportTarget & PMXImportTarget.Morphs) != 0;
        public bool ShouldImportBodies => (ImportTarget & PMXImportTarget.Bodies) != 0;
        public bool ShouldImportJoints => (ImportTarget & PMXImportTarget.Joints) != 0;

        // ================================================================
        // 座標変換設定
        // ================================================================

        /// <summary>スケール（PMX→Unity）デフォルト1.0（等倍）</summary>
        [Tooltip("PMX座標をUnity座標に変換するスケール（デフォルト: 1.0 = 等倍）")]
        public float Scale = 1.0f;

        /// <summary>Z軸反転</summary>
        [Tooltip("Z軸を反転する（PMXは右手系、Unityは左手系）")]
        public bool FlipZ = false;

        /// <summary>UV V座標反転（PMX→Unity変換で通常必要）</summary>
        [Tooltip("UV座標のV成分を反転する（PMXは上が0、Unityは下が0）")]
        public bool FlipUV_V = true;

        // ================================================================
        // インポートオプション
        // ================================================================

        /// <summary>マテリアルをインポート（Mesh読み込み時のみ有効）</summary>
        [Tooltip("PMXマテリアルをUnityマテリアルに変換する")]
        public bool ImportMaterials = true;

        /// <summary>Tポーズに変換（Humanoid Avatar用）</summary>
        [Tooltip("AポーズをTポーズに変換する（Mecanimアニメーション用）")]
        public bool ConvertToTPose = false;

        // ================================================================
        // 詳細設定
        // ================================================================

        /// <summary>法線を再計算</summary>
        [Tooltip("インポート後に法線を再計算する")]
        public bool RecalculateNormals = true;

        /// <summary>スムージング角度（度）</summary>
        [Tooltip("法線スムージングの閾値角度")]
        [Range(0f, 180f)]
        public float SmoothingAngle = 60f;

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>デフォルト設定を作成</summary>
        public static PMXImportSettings CreateDefault()
        {
            return new PMXImportSettings();
        }

        /// <summary>MMD互換設定を作成（1/10スケール、Z反転、UV V反転）</summary>
        public static PMXImportSettings CreateMMDCompatible()
        {
            return new PMXImportSettings
            {
                Scale = 0.1f,
                FlipZ = true,
                FlipUV_V = true
            };
        }

        /// <summary>ボーンのみインポート設定を作成</summary>
        public static PMXImportSettings CreateBonesOnly()
        {
            return new PMXImportSettings
            {
                ImportTarget = PMXImportTarget.BonesOnly,
                ImportMaterials = false
            };
        }

        /// <summary>物理のみインポート設定を作成（将来用）</summary>
        public static PMXImportSettings CreatePhysicsOnly()
        {
            return new PMXImportSettings
            {
                ImportTarget = PMXImportTarget.Physics,
                ImportMaterials = false
            };
        }

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new PMXImportSettings
            {
                ImportMode = this.ImportMode,
                ImportTarget = this.ImportTarget,
                Scale = this.Scale,
                FlipZ = this.FlipZ,
                FlipUV_V = this.FlipUV_V,
                ImportMaterials = this.ImportMaterials,
                ConvertToTPose = this.ConvertToTPose,
                RecalculateNormals = this.RecalculateNormals,
                SmoothingAngle = this.SmoothingAngle
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not PMXImportSettings o)
                return true;

            return ImportMode != o.ImportMode ||
                   ImportTarget != o.ImportTarget ||
                   !Mathf.Approximately(Scale, o.Scale) ||
                   FlipZ != o.FlipZ ||
                   FlipUV_V != o.FlipUV_V ||
                   ImportMaterials != o.ImportMaterials ||
                   ConvertToTPose != o.ConvertToTPose ||
                   RecalculateNormals != o.RecalculateNormals ||
                   !Mathf.Approximately(SmoothingAngle, o.SmoothingAngle);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not PMXImportSettings o)
                return;

            ImportMode = o.ImportMode;
            ImportTarget = o.ImportTarget;
            Scale = o.Scale;
            FlipZ = o.FlipZ;
            FlipUV_V = o.FlipUV_V;
            ImportMaterials = o.ImportMaterials;
            ConvertToTPose = o.ConvertToTPose;
            RecalculateNormals = o.RecalculateNormals;
            SmoothingAngle = o.SmoothingAngle;
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