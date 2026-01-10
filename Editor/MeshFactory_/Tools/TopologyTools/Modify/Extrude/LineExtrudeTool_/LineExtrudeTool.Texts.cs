// Tools/LineExtrudeTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class LineExtrudeTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Line → CSV Tool", ["ja"] = "ライン→CSV", ["hi"] = "らいん→CSV" },
            ["Help"] = new() { ["en"] = "Select lines that form closed loops.\nClockwise = Outer, Counter-clockwise = Hole\nSave as CSV for Profile2DExtrude.", ["ja"] = "閉じたループを形成するラインを選択\n時計回り=外周、反時計回り=穴\nProfile2DExtrude用CSVとして保存", ["hi"] = "とじたわっかになるせんをえらんでね\nとけいまわり＝そと、ぎゃく＝あな\nCSVでほぞんできるよ" },
            ["SelectedLines"] = new() { ["en"] = "Selected Lines: {0}", ["ja"] = "選択ライン数: {0}", ["hi"] = "えらんだせん: {0}" },
            ["DetectedLoops"] = new() { ["en"] = "Detected Loops: {0}", ["ja"] = "検出ループ数: {0}", ["hi"] = "みつけたわっか: {0}" },
            ["Loops"] = new() { ["en"] = "Loops:", ["ja"] = "ループ:", ["hi"] = "わっか:" },
            ["LoopInfo"] = new() { ["en"] = "Loop {0}: {1} vertices, {2}", ["ja"] = "ループ{0}: {1}頂点, {2}", ["hi"] = "わっか{0}: {1}てん, {2}" },
            ["Outer"] = new() { ["en"] = "Outer (CW)", ["ja"] = "外周 (時計回り)", ["hi"] = "そと（とけいまわり）" },
            ["Hole"] = new() { ["en"] = "Hole (CCW)", ["ja"] = "穴 (反時計回り)", ["hi"] = "あな（ぎゃくまわり）" },
            ["AnalyzeLoops"] = new() { ["en"] = "Analyze Loops", ["ja"] = "ループ解析", ["hi"] = "わっかをしらべる" },
            ["SaveAsCSV"] = new() { ["en"] = "Save as CSV...", ["ja"] = "CSVで保存...", ["hi"] = "CSVでほぞん..." },
            ["SelectMinLines"] = new() { ["en"] = "Select at least 3 connected lines.", ["ja"] = "3本以上の接続ラインを選択してください", ["hi"] = "3ぼんいじょうのせんをえらんでね" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
