// Assets/Editor/MeshFactory/MQO/Export/MQOExportSettings.cs
// MQOエクスポート設定

using System;
using UnityEngine;

namespace MeshFactory.MQO
{
    /// <summary>
    /// MQOエクスポート設定
    /// </summary>
    [Serializable]
    public class MQOExportSettings
    {
        // ================================================================
        // 座標系変換
        // ================================================================

        /// <summary>スケール係数</summary>
        [Tooltip("エクスポート時のスケール係数")]
        public float Scale = 10f;

        /// <summary>Y軸とZ軸を入れ替え（Unity Y-up → MQO Z-up）</summary>
        [Tooltip("Y軸とZ軸を入れ替え")]
        public bool SwapYZ = false;

        /// <summary>Z軸反転</summary>
        [Tooltip("Z軸を反転")]
        public bool FlipZ = true;

        /// <summary>UV V座標反転</summary>
        [Tooltip("UV V座標を反転（1-V）")]
        public bool FlipUV_V = true;

        // ================================================================
        // オプション
        // ================================================================

        /// <summary>マテリアルをエクスポート</summary>
        [Tooltip("マテリアル情報をエクスポート")]
        public bool ExportMaterials = true;

        /// <summary>空のオブジェクトをスキップ</summary>
        [Tooltip("頂点や面を持たないオブジェクトをスキップ")]
        public bool SkipEmptyObjects = true;

        /// <summary>選択中のメッシュのみエクスポート</summary>
        [Tooltip("選択中のメッシュのみエクスポート（OFFで全メッシュ）")]
        public bool ExportSelectedOnly = false;

        /// <summary>全メッシュを1つのオブジェクトに統合</summary>
        [Tooltip("全メッシュを1つのオブジェクトに統合")]
        public bool MergeObjects = false;

        // ================================================================
        // 出力形式
        // ================================================================

        /// <summary>小数点以下の桁数</summary>
        [Tooltip("座標・UV等の小数点以下桁数")]
        [Range(1, 8)]
        public int DecimalPrecision = 4;

        /// <summary>Shift-JISエンコード</summary>
        [Tooltip("Shift-JISでエンコード（メタセコイア互換）")]
        public bool UseShiftJIS = true;

        // ================================================================
        // 複製・比較
        // ================================================================

        public MQOExportSettings Clone()
        {
            return new MQOExportSettings
            {
                Scale = this.Scale,
                SwapYZ = this.SwapYZ,
                FlipZ = this.FlipZ,
                FlipUV_V = this.FlipUV_V,
                ExportMaterials = this.ExportMaterials,
                SkipEmptyObjects = this.SkipEmptyObjects,
                ExportSelectedOnly = this.ExportSelectedOnly,
                MergeObjects = this.MergeObjects,
                DecimalPrecision = this.DecimalPrecision,
                UseShiftJIS = this.UseShiftJIS,
            };
        }

        public bool IsDifferentFrom(MQOExportSettings o)
        {
            if (o == null) return true;
            return !Mathf.Approximately(Scale, o.Scale) ||
                   SwapYZ != o.SwapYZ ||
                   FlipZ != o.FlipZ ||
                   FlipUV_V != o.FlipUV_V ||
                   ExportMaterials != o.ExportMaterials ||
                   SkipEmptyObjects != o.SkipEmptyObjects ||
                   ExportSelectedOnly != o.ExportSelectedOnly ||
                   MergeObjects != o.MergeObjects ||
                   DecimalPrecision != o.DecimalPrecision ||
                   UseShiftJIS != o.UseShiftJIS;
        }

        public void CopyFrom(MQOExportSettings o)
        {
            if (o == null) return;
            Scale = o.Scale;
            SwapYZ = o.SwapYZ;
            FlipZ = o.FlipZ;
            FlipUV_V = o.FlipUV_V;
            ExportMaterials = o.ExportMaterials;
            SkipEmptyObjects = o.SkipEmptyObjects;
            ExportSelectedOnly = o.ExportSelectedOnly;
            MergeObjects = o.MergeObjects;
            DecimalPrecision = o.DecimalPrecision;
            UseShiftJIS = o.UseShiftJIS;
        }
    }
}