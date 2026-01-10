// Tools/PivotOffsetTool.Texts.cs
// ピボットオフセット移動ツール - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class PivotOffsetTool
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // タイトル
            ["Title"] = new() { ["en"] = "Pivot Offset Tool", ["ja"] = "ピボットオフセットツール", ["hi"] = "ちゅうしんいどうどうぐ" },

            // ヘルプ
            ["Help"] = new()
            {
                ["en"] = "Drag handle to move Pivot.\n(All vertices move in opposite direction)\n\n・Axis handle: Move along axis\n・Center: Free move",
                ["ja"] = "ハンドルをドラッグするとPivotが移動します。\n（実際には全頂点が逆方向に移動）\n\n・軸ハンドル: その軸方向のみ\n・中央: 自由移動",
                ["hi"] = "ハンドルをうごかすとちゅうしんがいどうするよ。\n（じっさいはてんがぎゃくにうごく）\n\n・じくハンドル: そのじくほうこう\n・ちゅうおう: じゆういどう"
            },

            // 状態表示
            ["Moving"] = new() { ["en"] = "Moving: {0}", ["ja"] = "移動中: {0}", ["hi"] = "いどうちゅう: {0}" },

            // ギズモラベル
            ["Pivot"] = new() { ["en"] = "Pivot", ["ja"] = "ピボット", ["hi"] = "ちゅうしん" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        private static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>フォーマット付きテキスト取得</summary>
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}