// Tools/SelectTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class SelectTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["ClickToSelect"] = new() { ["en"] = "Click to select vertices", ["ja"] = "クリックで頂点を選択", ["hi"] = "クリックしてえらぶ" },
            ["ShiftClick"] = new() { ["en"] = "Shift+Click: Add to selection", ["ja"] = "Shift+クリック: 選択に追加", ["hi"] = "Shift+クリック: ついか" },
            ["DragSelect"] = new() { ["en"] = "Drag: Box select", ["ja"] = "ドラッグ: 矩形選択", ["hi"] = "ドラッグ: しかくでえらぶ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
