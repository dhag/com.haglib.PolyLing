// Assets/Editor/Poly_Ling/PMX/Export/PMXExportSettings.cs
// PMXエクスポート設定

using System;
using System.Collections.Generic;
using Poly_Ling.MQO;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// エクスポートモード
    /// </summary>
    public enum PMXExportMode
    {
        /// <summary>丸ごとエクスポート</summary>
        Full,
        
        /// <summary>材質の頂点データのみ差し替え</summary>
        PartialReplace
    }

    /// <summary>
    /// PMXエクスポート設定
    /// </summary>
    [Serializable]
    public class PMXExportSettings
    {
        // ================================================================
        // 基本設定
        // ================================================================

        /// <summary>エクスポートモード</summary>
        public PMXExportMode ExportMode = PMXExportMode.Full;

        /// <summary>出力スケール（1.0 = そのまま）</summary>
        public float Scale = 10f; // Unity→PMX: 1/PmxUnityRatio

        /// <summary>Z軸反転（Unity→PMX座標系）</summary>
        public bool FlipZ = true;

        /// <summary>UV V座標反転</summary>
        public bool FlipUV_V = false;

        // ================================================================
        // フル出力モード設定
        // ================================================================

        /// <summary>マテリアルを出力</summary>
        public bool ExportMaterials = true;

        /// <summary>ボーンを出力</summary>
        public bool ExportBones = true;

        /// <summary>モーフを出力</summary>
        public bool ExportMorphs = true;

        /// <summary>剛体を出力</summary>
        public bool ExportBodies = false;

        /// <summary>ジョイントを出力</summary>
        public bool ExportJoints = false;

        /// <summary>テクスチャパスを相対パスで出力</summary>
        public bool UseRelativeTexturePath = true;

        // ================================================================
        // 部分差し替えモード設定
        // ================================================================

        /// <summary>差し替え対象の材質名リスト</summary>
        public List<string> ReplaceMaterialNames = new List<string>();

        /// <summary>元のPMXファイルパス（部分差し替え時）</summary>
        public string SourcePMXPath = "";

        /// <summary>頂点座標を差し替え</summary>
        public bool ReplacePositions = true;

        /// <summary>法線を差し替え</summary>
        public bool ReplaceNormals = true;

        /// <summary>UVを差し替え</summary>
        public bool ReplaceUVs = false;

        /// <summary>ボーンウェイトを差し替え</summary>
        public bool ReplaceBoneWeights = false;

        // ================================================================
        // CSV出力設定
        // ================================================================

        /// <summary>バイナリPMX形式で出力</summary>
        public bool OutputBinaryPMX = true;

        /// <summary>CSV形式でも出力</summary>
        public bool OutputCSV = false;

        /// <summary>小数点以下の桁数</summary>
        public int DecimalPrecision = MQOExportSettings.DefaultDecimalPrecision;

        // ================================================================
        // デフォルト設定
        // ================================================================

        /// <summary>フル出力用デフォルト設定</summary>
        public static PMXExportSettings CreateFullExport()
        {
            return new PMXExportSettings
            {
                ExportMode = PMXExportMode.Full,
                Scale = 10f, // 1/PmxUnityRatio
                FlipZ = true,
                ExportMaterials = true,
                ExportBones = true,
                ExportMorphs = true,
                OutputBinaryPMX = true
            };
        }

        /// <summary>座標系設定から初期化（逆変換スケール）</summary>
        public static PMXExportSettings CreateFromCoordinate(float pmxUnityRatio, bool flipZ)
        {
            return new PMXExportSettings
            {
                ExportMode = PMXExportMode.Full,
                Scale = pmxUnityRatio > 0f ? 1f / pmxUnityRatio : 10f,
                FlipZ = flipZ,
                ExportMaterials = true,
                ExportBones = true,
                ExportMorphs = true,
                OutputBinaryPMX = true
            };
        }

        /// <summary>部分差し替え用デフォルト設定</summary>
        public static PMXExportSettings CreatePartialReplace()
        {
            return new PMXExportSettings
            {
                ExportMode = PMXExportMode.PartialReplace,
                Scale = 10f, // 1/PmxUnityRatio
                FlipZ = true,
                ReplacePositions = true,
                ReplaceNormals = true,
                ReplaceUVs = false,
                ReplaceBoneWeights = false,
                OutputBinaryPMX = true
            };
        }
    }
}
