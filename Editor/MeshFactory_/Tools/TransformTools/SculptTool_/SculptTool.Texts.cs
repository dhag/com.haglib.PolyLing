// Tools/SculptTool.Texts.cs
// スカルプトツール - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class SculptTool
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // タイトル
            ["Title"] = new() { ["en"] = "Sculpt Tool", ["ja"] = "スカルプトツール", ["hi"] = "こねるどうぐ" },

            // モード名
            ["Mode_Draw"] = new() { ["en"] = "Draw", ["ja"] = "盛り上げ", ["hi"] = "もりあげ" },
            ["Mode_Smooth"] = new() { ["en"] = "Smooth", ["ja"] = "なめらか", ["hi"] = "なめらか" },
            ["Mode_Inflate"] = new() { ["en"] = "Inflate", ["ja"] = "膨らみ", ["hi"] = "ふくらみ" },
            ["Mode_Flatten"] = new() { ["en"] = "Flatten", ["ja"] = "平ら", ["hi"] = "たいら" },

            // パラメータ
            ["BrushSize"] = new() { ["en"] = "Brush Size", ["ja"] = "ブラシサイズ", ["hi"] = "ふでのおおきさ" },
            ["Strength"] = new() { ["en"] = "Strength", ["ja"] = "強度", ["hi"] = "つよさ" },
            ["Invert"] = new() { ["en"] = "Invert", ["ja"] = "反転", ["hi"] = "ぎゃく" },

            // ヘルプ
            ["Help_Draw"] = new()
            {
                ["en"] = "Drag to raise/lower surface",
                ["ja"] = "ドラッグで表面を盛り上げ/盛り下げ",
                ["hi"] = "ドラッグでもりあげ/さげる"
            },
            ["Help_Smooth"] = new()
            {
                ["en"] = "Drag to smooth surface",
                ["ja"] = "ドラッグで表面を滑らかにする",
                ["hi"] = "ドラッグでなめらかにする"
            },
            ["Help_Inflate"] = new()
            {
                ["en"] = "Drag to inflate/deflate",
                ["ja"] = "ドラッグで膨らませる/縮ませる",
                ["hi"] = "ドラッグでふくらませる/ちぢめる"
            },
            ["Help_Flatten"] = new()
            {
                ["en"] = "Drag to flatten surface",
                ["ja"] = "ドラッグで表面を平らにする",
                ["hi"] = "ドラッグでたいらにする"
            },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        private static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>モード名配列（UI用）</summary>
        private static string[] ModeNames => new[]
        {
            T("Mode_Draw"),
            T("Mode_Smooth"),
            T("Mode_Inflate"),
            T("Mode_Flatten")
        };

        /// <summary>モード名取得</summary>
        private static string GetModeName(SculptMode mode) => mode switch
        {
            SculptMode.Draw => T("Mode_Draw"),
            SculptMode.Smooth => T("Mode_Smooth"),
            SculptMode.Inflate => T("Mode_Inflate"),
            SculptMode.Flatten => T("Mode_Flatten"),
            _ => ""
        };

        /// <summary>モードヘルプ取得</summary>
        private static string GetModeHelp(SculptMode mode) => mode switch
        {
            SculptMode.Draw => T("Help_Draw"),
            SculptMode.Smooth => T("Help_Smooth"),
            SculptMode.Inflate => T("Help_Inflate"),
            SculptMode.Flatten => T("Help_Flatten"),
            _ => ""
        };
    }
}