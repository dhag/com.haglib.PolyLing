// Tools/EdgeBevelTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class EdgeBevelTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Edge Bevel Tool", ["ja"] = "エッジベベル", ["hi"] = "へりをまるくする" },
            ["Help"] = new() { ["en"] = "Drag edge to bevel.\nCreates new face between adjacent faces.", ["ja"] = "辺をドラッグしてベベル適用\n隣接面の間に新しい面を作成", ["hi"] = "へりをひっぱってまるくするよ\nとなりにあたらしいめんができるよ" },
            ["Amount"] = new() { ["en"] = "Amount", ["ja"] = "量", ["hi"] = "りょう" },
            ["Segments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつ" },
            ["ApplyBevel"] = new() { ["en"] = "Apply Bevel", ["ja"] = "ベベル適用", ["hi"] = "てきよう" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
