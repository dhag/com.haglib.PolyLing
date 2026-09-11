// PMXExportResult.cs
// PMX エクスポートの結果（PMXExporter から分離）。
// Runtime/Poly_Ling_Main/PMX/ に配置（PMXExporter.cs と同じ名前空間。PMXExporter.cs から分割）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXエクスポート結果
    /// </summary>
    public class PMXExportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string OutputPath { get; set; }

        // 統計情報
        public int VertexCount { get; set; }
        public int FaceCount { get; set; }
        public int MaterialCount { get; set; }
        public int BoneCount { get; set; }
        public int MorphCount { get; set; }
    }
}
