// Assets/Editor/MeshFactory/PMX/Import/PMXImportSettings.cs
// PMX CSVインポート設定
// IToolSettings実装でUndo対応

using System;
using UnityEngine;
using MeshFactory.Tools;

namespace MeshFactory.PMX
{
    /// <summary>
    /// PMXインポートモード
    /// </summary>
    public enum PMXImportMode
    {
        /// <summary>既存メッシュに追加</summary>
        Append,
        /// <summary>既存メッシュを置換（全削除後にインポート）</summary>
        Replace
    }

    /// <summary>
    /// PMXインポート設定
    /// </summary>
    [Serializable]
    public class PMXImportSettings : IToolSettings
    {
        // ================================================================
        // インポートモード
        // ================================================================

        /// <summary>インポートモード（追加/置換）</summary>
        [Tooltip("Append: 既存メッシュに追加, Replace: 既存メッシュを全削除してからインポート")]
        public PMXImportMode ImportMode = PMXImportMode.Append;

        // ================================================================
        // 座標変換設定
        // ================================================================

        /// <summary>スケール（PMX→Unity）デフォルト0.1（1/10）</summary>
        [Tooltip("PMX座標をUnity座標に変換するスケール（デフォルト: 0.1 = 1/10）")]
        public float Scale = 0.1f;

        /// <summary>Z軸反転</summary>
        [Tooltip("Z軸を反転する（PMXは右手系、Unityは左手系）")]
        public bool FlipZ = true;

        /// <summary>UV V座標反転（PMX→Unity変換で通常必要）</summary>
        [Tooltip("UV座標のV成分を反転する（PMXは上が0、Unityは下が0）")]
        public bool FlipUV_V = true;

        // ================================================================
        // インポートオプション
        // ================================================================

        /// <summary>マテリアルをインポート</summary>
        [Tooltip("PMXマテリアルをUnityマテリアルに変換する")]
        public bool ImportMaterials = true;

        /// <summary>ボーンをインポート</summary>
        [Tooltip("ボーン情報をインポートする")]
        public bool ImportBones = true;

        /// <summary>モーフをインポート（将来用）</summary>
        [Tooltip("モーフ情報をインポートする（将来実装）")]
        public bool ImportMorphs = false;

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

        /// <summary>等倍設定を作成（スケール1:1）</summary>
        public static PMXImportSettings CreateNoScale()
        {
            return new PMXImportSettings
            {
                Scale = 1f,
                FlipZ = true,
                FlipUV_V = true
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
                Scale = this.Scale,
                FlipZ = this.FlipZ,
                FlipUV_V = this.FlipUV_V,
                ImportMaterials = this.ImportMaterials,
                ImportBones = this.ImportBones,
                ImportMorphs = this.ImportMorphs,
                RecalculateNormals = this.RecalculateNormals,
                SmoothingAngle = this.SmoothingAngle
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not PMXImportSettings o)
                return true;

            return ImportMode != o.ImportMode ||
                   !Mathf.Approximately(Scale, o.Scale) ||
                   FlipZ != o.FlipZ ||
                   FlipUV_V != o.FlipUV_V ||
                   ImportMaterials != o.ImportMaterials ||
                   ImportBones != o.ImportBones ||
                   ImportMorphs != o.ImportMorphs ||
                   RecalculateNormals != o.RecalculateNormals ||
                   !Mathf.Approximately(SmoothingAngle, o.SmoothingAngle);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not PMXImportSettings o)
                return;

            ImportMode = o.ImportMode;
            Scale = o.Scale;
            FlipZ = o.FlipZ;
            FlipUV_V = o.FlipUV_V;
            ImportMaterials = o.ImportMaterials;
            ImportBones = o.ImportBones;
            ImportMorphs = o.ImportMorphs;
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