// MQOExportResult.cs
// MQO エクスポートの結果・統計（MQOExporter から分離）。
// Runtime/Poly_Ling_Main/MQO/Export/ に配置（MQOExporter.cs と同じ名前空間。MQOExporter.cs から分割）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;

namespace Poly_Ling.MQO
{
    // ================================================================
    // 結果クラス
    // ================================================================

    /// <summary>
    /// エクスポート結果
    /// </summary>
    public class MQOExportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FilePath { get; set; }
        public MQOExportStats Stats { get; } = new MQOExportStats();
    }

    /// <summary>
    /// エクスポート統計
    /// </summary>
    public class MQOExportStats
    {
        public int ObjectCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
    }
}
