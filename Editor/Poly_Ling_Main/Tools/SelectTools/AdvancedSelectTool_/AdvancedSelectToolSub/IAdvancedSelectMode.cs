// Assets/Editor/Poly_Ling/Tools/Selection/IAdvancedSelectMode.cs
// 特殊選択モードのインターフェース

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 特殊選択モードのインターフェース
    /// </summary>
    public interface IAdvancedSelectMode
    {
        /// <summary>
        /// クリック処理
        /// </summary>
        bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode);

        /// <summary>
        /// プレビュー更新
        /// </summary>
        void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode);

        /// <summary>
        /// モードがリセットされるとき呼ばれる
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 特殊選択モードで共有されるコンテキスト
    /// </summary>
    public class AdvancedSelectContext
    {
        // ToolContext から
        public ToolContext ToolCtx { get; set; }

        // プレビュー出力先
        public List<int> PreviewVertices { get; } = new List<int>();
        public List<VertexPair> PreviewEdges { get; } = new List<VertexPair>();
        public List<int> PreviewFaces { get; } = new List<int>();
        public List<int> PreviewLines { get; } = new List<int>();
        public List<int> PreviewPath { get; } = new List<int>();

        // ホバー状態
        public int HoveredVertex { get; set; } = -1;
        public VertexPair? HoveredEdgePair { get; set; }
        public int HoveredFace { get; set; } = -1;
        public int HoveredLine { get; set; } = -1;

        // 設定（Settings経由でアクセス）
        public bool AddToSelection { get; set; } = true;
        public float EdgeLoopThreshold { get; set; } = 0.7f;

        /// <summary>
        /// プレビューをクリア
        /// </summary>
        public void ClearPreview()
        {
            PreviewVertices.Clear();
            PreviewEdges.Clear();
            PreviewFaces.Clear();
            PreviewLines.Clear();
            PreviewPath.Clear();
        }

        /// <summary>
        /// ホバー状態をクリア
        /// </summary>
        public void ClearHover()
        {
            HoveredVertex = -1;
            HoveredEdgePair = null;
            HoveredFace = -1;
            HoveredLine = -1;
        }
    }
}
