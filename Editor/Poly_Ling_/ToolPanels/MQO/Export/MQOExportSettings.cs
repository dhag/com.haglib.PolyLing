// Assets/Editor/Poly_Ling/MQO/Export/MQOExportSettings.cs
// MQOエクスポート設定

using System;
using UnityEngine;

namespace Poly_Ling.MQO
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

        /// <summary>未使用のミラーマテリアルを除外</summary>
        [Tooltip("どのメッシュでも使用されていないミラーマテリアル（+付き）を除外")]
        public bool ExcludeUnusedMirrorMaterials = true;

        /// <summary>空のオブジェクトをスキップ</summary>
        [Tooltip("頂点や面を持たないオブジェクトをスキップ")]
        public bool SkipEmptyObjects = false;

        /// <summary>選択中のメッシュのみエクスポート</summary>
        [Tooltip("選択中のメッシュのみエクスポート（OFFで全メッシュ）")]
        public bool ExportSelectedOnly = false;

        /// <summary>全メッシュを1つのオブジェクトに統合</summary>
        [Tooltip("全メッシュを1つのオブジェクトに統合")]
        public bool MergeObjects = false;

        /// <summary>オブジェクト属性を保持（depth, visible, mirror等）</summary>
        [Tooltip("オブジェクトの階層・表示・ミラー設定を保持")]
        public bool PreserveObjectAttributes = true;

        /// <summary>ベイクミラーを削除</summary>
        [Tooltip("ベイクされたミラーメッシュ（Type=BakedMirror）を出力しない")]
        public bool SkipBakedMirror = true;

        /// <summary>ボーンをMQOに出力</summary>
        [Tooltip("ボーン（Type=Bone）をMQOオブジェクトとして出力（__Armature__の下に配置）")]
        public bool ExportBones = true;

        /// <summary>ローカルトランスフォームを出力</summary>
        [Tooltip("オブジェクトのローカルトランスフォーム（位置・回転・スケール）を出力")]
        public bool ExportLocalTransform = true;

        /// <summary>ボーンウェイトをMQOに埋め込む</summary>
        [Tooltip("ボーンウェイト情報を四角形特殊面としてMQOに埋め込む")]
        public bool EmbedBoneWeightsInMQO = true;

        // ================================================================
        // テクスチャ
        // ================================================================

        /// <summary>テクスチャフォルダパス（マテリアルのtexに付加）</summary>
        [Tooltip("テクスチャファイルのフォルダパス（例: textures/）")]
        public string TextureFolder = "";

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
                ExcludeUnusedMirrorMaterials = this.ExcludeUnusedMirrorMaterials,
                SkipEmptyObjects = this.SkipEmptyObjects,
                ExportSelectedOnly = this.ExportSelectedOnly,
                MergeObjects = this.MergeObjects,
                PreserveObjectAttributes = this.PreserveObjectAttributes,
                SkipBakedMirror = this.SkipBakedMirror,
                ExportBones = this.ExportBones,
                ExportLocalTransform = this.ExportLocalTransform,
                EmbedBoneWeightsInMQO = this.EmbedBoneWeightsInMQO,
                TextureFolder = this.TextureFolder,
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
                   ExcludeUnusedMirrorMaterials != o.ExcludeUnusedMirrorMaterials ||
                   SkipEmptyObjects != o.SkipEmptyObjects ||
                   ExportSelectedOnly != o.ExportSelectedOnly ||
                   MergeObjects != o.MergeObjects ||
                   PreserveObjectAttributes != o.PreserveObjectAttributes ||
                   SkipBakedMirror != o.SkipBakedMirror ||
                   ExportBones != o.ExportBones ||
                   ExportLocalTransform != o.ExportLocalTransform ||
                   EmbedBoneWeightsInMQO != o.EmbedBoneWeightsInMQO ||
                   TextureFolder != o.TextureFolder ||
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
            ExcludeUnusedMirrorMaterials = o.ExcludeUnusedMirrorMaterials;
            SkipEmptyObjects = o.SkipEmptyObjects;
            ExportSelectedOnly = o.ExportSelectedOnly;
            MergeObjects = o.MergeObjects;
            PreserveObjectAttributes = o.PreserveObjectAttributes;
            SkipBakedMirror = o.SkipBakedMirror;
            ExportBones = o.ExportBones;
            ExportLocalTransform = o.ExportLocalTransform;
            EmbedBoneWeightsInMQO = o.EmbedBoneWeightsInMQO;
            TextureFolder = o.TextureFolder;
            DecimalPrecision = o.DecimalPrecision;
            UseShiftJIS = o.UseShiftJIS;
        }
    }
}
