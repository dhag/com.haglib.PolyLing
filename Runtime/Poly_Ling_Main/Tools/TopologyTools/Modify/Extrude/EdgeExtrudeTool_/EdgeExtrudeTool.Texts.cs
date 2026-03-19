// Tools/EdgeExtrudeTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class EdgeExtrudeTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Edge Extrude Tool", ["ja"] = "面張り（エッジ押出）", ["hi"] = "へりをおしだす" },
            ["Help"] = new() { ["en"] = "Drag edge to extrude.\nSelect mode below.", ["ja"] = "辺をドラッグして押し出し\n下のモードを選択", ["hi"] = "へりをひっぱっておしだすよ\nしたでモードをえらんでね" },
            ["Mode"] = new() { ["en"] = "Mode", ["ja"] = "モード", ["hi"] = "もーど" },
            ["SnapToAxis"] = new() { ["en"] = "Snap to Axis", ["ja"] = "軸にスナップ", ["hi"] = "じくにあわせる" },
            ["Distance"] = new() { ["en"] = "Distance", ["ja"] = "距離", ["hi"] = "きょり" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
