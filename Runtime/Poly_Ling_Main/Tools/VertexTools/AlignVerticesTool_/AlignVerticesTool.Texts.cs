// Tools/AlignVerticesTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class AlignVerticesTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Align Vertices", ["ja"] = "頂点整列", ["hi"] = "ちょうてんをそろえる" },
            ["Help"] = new() { ["en"] = "Align selected vertices on specified axes.\nAxis with smallest deviation is auto-selected.", ["ja"] = "選択頂点を指定軸上に整列します。\n標準偏差が最小の軸が自動選択されます。", ["hi"] = "えらんだてんをじくにそろえます。\nいちばんそろっているじくがえらばれます。" },
            ["SelectedVertices"] = new() { ["en"] = "Selected: {0} vertices", ["ja"] = "選択中: {0} 頂点", ["hi"] = "せんたくちゅう: {0} てん" },
            ["NeedMoreVertices"] = new() { ["en"] = "Select 2 or more vertices", ["ja"] = "2つ以上の頂点を選択してください", ["hi"] = "2つ以上のてんをえらんでね" },
            ["StdDeviation"] = new() { ["en"] = "Std Deviation:", ["ja"] = "標準偏差:", ["hi"] = "ばらつき:" },
            ["AlignAxis"] = new() { ["en"] = "Align Axis:", ["ja"] = "整列軸:", ["hi"] = "そろえるじく:" },
            ["AutoSelect"] = new() { ["en"] = "Auto Select", ["ja"] = "自動選択", ["hi"] = "じどうでえらぶ" },
            ["AlignMode"] = new() { ["en"] = "Align To:", ["ja"] = "基準:", ["hi"] = "きじゅん:" },
            ["Execute"] = new() { ["en"] = "Align", ["ja"] = "整列実行", ["hi"] = "そろえる" },
            ["AlignTo"] = new() { ["en"] = "-> {0}", ["ja"] = "-> {0}", ["hi"] = "-> {0}" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
