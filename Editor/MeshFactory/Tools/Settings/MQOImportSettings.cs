// Assets/Editor/MeshFactory/MQO/Import/MQOImportSettings.cs
// MQOインポート設定
// IToolSettings実装でUndo対応

using System;
using UnityEngine;
using MeshFactory.Tools;

namespace MeshFactory.MQO
{
    /// <summary>
    /// MQOインポート設定
    /// </summary>
    [Serializable]
    public class MQOImportSettings : IToolSettings
    {
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
        public bool FlipUV_V = false;

        // ================================================================
        // インポートオプション
        // ================================================================

        /// <summary>マテリアルをインポート</summary>
        [Tooltip("MQOマテリアルをUnityマテリアルに変換する")]
        public bool ImportMaterials = true;

        /// <summary>非表示オブジェクトをスキップ</summary>
        [Tooltip("MQOで非表示に設定されているオブジェクトをスキップする")]
        public bool SkipHiddenObjects = true;

        /// <summary>空のオブジェクトをスキップ</summary>
        [Tooltip("頂点や面を持たないオブジェクト（グループ用ダミー等）をスキップする")]
        public bool SkipEmptyObjects = false;

        /// <summary>全オブジェクトを統合</summary>
        [Tooltip("全てのオブジェクトを1つのメッシュに統合する")]
        public bool MergeObjects = false;

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
                Scale = this.Scale,
                FlipZ = this.FlipZ,
                FlipUV_V = this.FlipUV_V,
                ImportMaterials = this.ImportMaterials,
                SkipHiddenObjects = this.SkipHiddenObjects,
                SkipEmptyObjects = this.SkipEmptyObjects,
                MergeObjects = this.MergeObjects,
                RecalculateNormals = this.RecalculateNormals,
                SmoothingAngle = this.SmoothingAngle
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not MQOImportSettings o)
                return true;

            return !Mathf.Approximately(Scale, o.Scale) ||
                   FlipZ != o.FlipZ ||
                   FlipUV_V != o.FlipUV_V ||
                   ImportMaterials != o.ImportMaterials ||
                   SkipHiddenObjects != o.SkipHiddenObjects ||
                   SkipEmptyObjects != o.SkipEmptyObjects ||
                   MergeObjects != o.MergeObjects ||
                   RecalculateNormals != o.RecalculateNormals ||
                   !Mathf.Approximately(SmoothingAngle, o.SmoothingAngle);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not MQOImportSettings o)
                return;

            Scale = o.Scale;
            FlipZ = o.FlipZ;
            FlipUV_V = o.FlipUV_V;
            ImportMaterials = o.ImportMaterials;
            SkipHiddenObjects = o.SkipHiddenObjects;
            SkipEmptyObjects = o.SkipEmptyObjects;
            MergeObjects = o.MergeObjects;
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