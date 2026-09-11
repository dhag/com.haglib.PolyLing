// Assets/Editor/Poly_Ling/PMX/Export/PMXExportSettings.cs
// PMXエクスポート設定

using System;
using System.Collections.Generic;
using Poly_Ling.MQO;
using Poly_Ling.Ops;
using UnityEngine;
using Poly_Ling.Data;

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
        [PLParam(Description = "エクスポートモード")]
        public PMXExportMode ExportMode = PMXExportMode.Full;

        /// <summary>出力スケール（1.0 = そのまま）</summary>
        [PLParam(Description = "出力スケール。Unity から PMX への倍率", Min = 0.001, Max = 1000)]
        public float Scale = 10f; // Unity→PMX: 1/PmxUnityRatio

        /// <summary>X軸反転（Unity→PMX座標系）</summary>
        [PLParam(Description = "X軸反転（Unity→PMX座標系）")]
        public bool FlipX = true;

        /// <summary>Z軸反転（Unity→PMX座標系）</summary>
        [PLParam(Description = "Z軸反転（Unity→PMX座標系）")]
        public bool FlipZ = true;

        /// <summary>
        /// 軸反転指定。インポート側と同一（S·R·S は自己逆元のため同じ設定が逆変換になる）。
        /// </summary>
        public AxisFlip Flip => new AxisFlip(FlipX, FlipZ);

        /// <summary>UV V座標反転</summary>
        [PLParam(Description = "UV V座標反転")]
        public bool FlipUV_V = true;

        // ================================================================
        // フル出力モード設定
        // ================================================================

        /// <summary>マテリアルを出力</summary>
        [PLParam(Description = "マテリアルを出力")]
        public bool ExportMaterials = true;

        /// <summary>ボーンを出力</summary>
        [PLParam(Description = "ボーンを出力")]
        public bool ExportBones = true;

        /// <summary>モーフを出力</summary>
        [PLParam(Description = "モーフを出力")]
        public bool ExportMorphs = true;

        /// <summary>剛体を出力</summary>
        [PLParam(Description = "剛体を出力")]
        public bool ExportBodies = false;

        /// <summary>ジョイントを出力</summary>
        [PLParam(Description = "ジョイントを出力")]
        public bool ExportJoints = false;

        /// <summary>テクスチャパスを相対パスで出力</summary>
        [PLParam(Description = "テクスチャパスを相対パスで出力")]
        public bool UseRelativeTexturePath = true;

        // ================================================================
        // 部分差し替えモード設定
        // ================================================================

        /// <summary>差し替え対象の材質名リスト</summary>
        // List<string> は Create の対応表に無い。コマンド側が string[] で受け、
        // 受け口でここへ詰め替える。
        [PLParam(Ignore = true)]
        public List<string> ReplaceMaterialNames = new List<string>();

        /// <summary>元のPMXファイルパス（部分差し替え時）</summary>
        // 入力パスはコマンドが持ち、受け口が PLSandbox を通して入れる。
        [PLParam(Ignore = true)]
        public string SourcePMXPath = "";

        /// <summary>頂点座標を差し替え</summary>
        [PLParam(Description = "頂点座標を差し替え")]
        public bool ReplacePositions = true;

        /// <summary>法線を差し替え</summary>
        [PLParam(Description = "法線を差し替え")]
        public bool ReplaceNormals = true;

        /// <summary>UVを差し替え</summary>
        [PLParam(Description = "UVを差し替え")]
        public bool ReplaceUVs = false;

        /// <summary>ボーンウェイトを差し替え</summary>
        [PLParam(Description = "ボーンウェイトを差し替え")]
        public bool ReplaceBoneWeights = false;

        // ================================================================
        // CSV出力設定
        // ================================================================

        /// <summary>バイナリPMX形式で出力</summary>
        [PLParam(Description = "バイナリPMX形式で出力")]
        public bool OutputBinaryPMX = true;

        /// <summary>CSV形式でも出力</summary>
        [PLParam(Description = "CSV形式でも出力")]
        public bool OutputCSV = false;

        /// <summary>フェースメタ (.plmface.csv) を出力</summary>
        [PLParam(Description = "フェースメタ (.plmface.csv) を出力")]
        public bool OutputFaceMeta = true;

        /// <summary>小数点以下の桁数</summary>
        [PLParam(Description = "小数点以下の桁数", Min = 1, Max = 9)]
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
                FlipX = true,
                FlipZ = true,
                ExportMaterials = true,
                ExportBones = true,
                ExportMorphs = true,
                OutputBinaryPMX = true
            };
        }

        /// <summary>座標系設定から初期化（逆変換スケール）</summary>
        public static PMXExportSettings CreateFromCoordinate(float pmxUnityRatio, bool flipZ, bool flipX = true)
        {
            return new PMXExportSettings
            {
                ExportMode = PMXExportMode.Full,
                Scale = pmxUnityRatio > 0f ? 1f / pmxUnityRatio : 10f,
                FlipX = flipX,
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
                FlipX = true,
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
