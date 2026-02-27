// Assets/Editor/Poly_Ling/Tools/Panels/VertexToolsPanel.Texts.cs
// 頂点ツールパネル - ローカライズテキスト

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    public partial class VertexToolsPanel
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            // ウィンドウ
            ["WindowTitle"] = new() 
            { 
                ["en"] = "Vertex Tools", 
                ["ja"] = "頂点ツール", 
                ["hi"] = "ちょうてんつーる" 
            },

            // メッセージ
            ["NoMeshSelected"] = new() 
            { 
                ["en"] = "No mesh selected", 
                ["ja"] = "メッシュが選択されていません", 
                ["hi"] = "めっしゅがないよ" 
            },
            ["NeedMoreVertices"] = new() 
            { 
                ["en"] = "Select 2+ vertices", 
                ["ja"] = "2つ以上の頂点を選択", 
                ["hi"] = "2こいじょうえらんでね" 
            },
            ["ToolActive"] = new() 
            { 
                ["en"] = "▼ Tool Settings", 
                ["ja"] = "▼ ツール設定", 
                ["hi"] = "▼ せってい" 
            },

            // 選択情報
            ["SelectedInfo"] = new() 
            { 
                ["en"] = "Selected: {0} / {1} vertices", 
                ["ja"] = "選択中: {0} / {1} 頂点", 
                ["hi"] = "せんたく: {0} / {1} てん" 
            },

            // Align セクション
            ["AlignSection"] = new() 
            { 
                ["en"] = "Align Vertices", 
                ["ja"] = "頂点整列", 
                ["hi"] = "ちょうてんせいれつ" 
            },
            ["AlignHelp"] = new() 
            { 
                ["en"] = "Align selected vertices on X/Y/Z axis", 
                ["ja"] = "選択頂点をX/Y/Z軸に整列", 
                ["hi"] = "えらんだてんをそろえる" 
            },
            ["OpenAlignTool"] = new() 
            { 
                ["en"] = "Open Align Tool", 
                ["ja"] = "整列ツールを開く", 
                ["hi"] = "せいれつつーるをひらく" 
            },

            // Merge セクション
            ["MergeSection"] = new() 
            { 
                ["en"] = "Merge Vertices", 
                ["ja"] = "頂点マージ", 
                ["hi"] = "ちょうてんまーじ" 
            },

            // Planarize セクション
            ["PlanarizeSection"] = new() 
            { 
                ["en"] = "Planarize Along Bones", 
                ["ja"] = "ボーン間平面化", 
                ["hi"] = "ぼーんかんへいめんか" 
            },
            ["MergeHelp"] = new() 
            { 
                ["en"] = "Merge selected vertices into one", 
                ["ja"] = "選択頂点を1つに統合", 
                ["hi"] = "てんをひとつにする" 
            },
            ["OpenMergeTool"] = new() 
            { 
                ["en"] = "Open Merge Tool", 
                ["ja"] = "マージツールを開く", 
                ["hi"] = "まーじつーるをひらく" 
            },
        };

        /// <summary>ローカライズテキスト取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        
        /// <summary>ローカライズテキスト取得（引数付き）</summary>
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);
    }
}
