// ModelEntry.cs
// モデルフォルダ内のメッシュエントリ（CsvModelSerializer から分離）。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置（CsvModelSerializer.cs と同じ名前空間。CsvModelSerializer.cs から分割）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// model.csv内の1エントリ（MeshContextListの並び順管理）
    /// </summary>
    public class ModelEntry
    {
        public int GlobalIndex;
        public string Type;   // "Mesh","Bone","Morph" etc
        public string Name;
        public string File;   // "mesh","bone","morph"
        public int OrderInFile;
        public bool IsNameBased;
    }
}
