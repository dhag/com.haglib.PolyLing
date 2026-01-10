// Tools/AddFaceTool.Texts.cs
// 面追加ツール - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class AddFaceTool
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // タイトル
            ["Title"] = new() { ["en"] = "Add Face Tool", ["ja"] = "面追加ツール", ["hi"] = "めんついかどうぐ" },

            // モード
            ["FaceType"] = new() { ["en"] = "Face Type", ["ja"] = "面タイプ", ["hi"] = "めんのしゅるい" },
            ["Mode_Line"] = new() { ["en"] = "Line (2)", ["ja"] = "線分 (2)", ["hi"] = "せん (2)" },
            ["Mode_Triangle"] = new() { ["en"] = "Triangle (3)", ["ja"] = "三角形 (3)", ["hi"] = "さんかく (3)" },
            ["Mode_Quad"] = new() { ["en"] = "Quad (4)", ["ja"] = "四角形 (4)", ["hi"] = "しかく (4)" },

            // オプション
            ["ContinuousLine"] = new() { ["en"] = "Continuous Line", ["ja"] = "連続線分", ["hi"] = "つづけてせん" },
            ["ContinuousHint"] = new() { ["en"] = "  ↳ Click to continue from last point", ["ja"] = "  ↳ クリックで続きを描画", ["hi"] = "  ↳ クリックでつづきをかく" },

            // 進捗表示
            ["Progress"] = new() { ["en"] = "Points: {0} / {1}", ["ja"] = "点: {0} / {1}", ["hi"] = "てん: {0} / {1}" },
            ["ClickToContinue"] = new() { ["en"] = "Click to add next line segment", ["ja"] = "クリックで次の線分を追加", ["hi"] = "クリックでつぎのせんをたす" },

            // 配置済み点
            ["PlacedPoints"] = new() { ["en"] = "Placed Points:", ["ja"] = "配置済み:", ["hi"] = "おいたてん:" },
            ["PointExisting"] = new() { ["en"] = "  {0}: V{1}", ["ja"] = "  {0}: V{1}", ["hi"] = "  {0}: V{1}" },
            ["PointNew"] = new() { ["en"] = "  {0}: NEW", ["ja"] = "  {0}: 新規", ["hi"] = "  {0}: あたらしい" },

            // ボタン
            ["ClearPoints"] = new() { ["en"] = "Clear Points", ["ja"] = "点をクリア", ["hi"] = "てんをけす" },

            // ギズモ
            ["Start"] = new() { ["en"] = "START", ["ja"] = "開始", ["hi"] = "はじめ" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        private static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>フォーマット付きテキスト取得</summary>
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);

        /// <summary>モード名配列（UI用）</summary>
        private static string[] LocalizedModeNames => new[]
        {
            T("Mode_Line"),
            T("Mode_Triangle"),
            T("Mode_Quad")
        };
    }
}