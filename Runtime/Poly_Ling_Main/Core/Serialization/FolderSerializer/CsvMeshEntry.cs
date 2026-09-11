// CsvMeshEntry.cs
// CSV メッシュのエントリ（CsvMeshSerializer から分離）。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置（CsvMeshSerializer.cs と同じ名前空間。CsvMeshSerializer.cs から分割）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// メッシュ1個分の読み取り結果
    /// </summary>
    public class CsvMeshEntry
    {
        public int GlobalIndex;
        public MeshContext MeshContext;

        // ================================================================
        // 名前ベース参照（読み込み時に一時格納、後でインデックスに解決）
        // ================================================================
        public bool IsNameBased;
        public string ParentName;
        public string HierarchyParentName;
        public string BakedMirrorSourceName;
        public string MorphParentName;
        /// <summary>頂点ごとのBoneWeight参照ボーン名 [name0,name1,name2,name3]</summary>
        public List<string[]> VertexBoneNames;
        /// <summary>頂点ごとのMirrorBoneWeight参照ボーン名</summary>
        public List<string[]> VertexMirrorBoneNames;

        // ================================================================
        // ミラーペア情報（部分インポート時にペア再構築するため）
        // ================================================================
        /// <summary>ミラーペア相手のメッシュ名（Real側に記録）</summary>
        public string MirrorPeerName;
        /// <summary>ミラーペアの軸（0=X, 1=Y, 2=Z）</summary>
        public int MirrorPeerAxis = -1;
    }
}
