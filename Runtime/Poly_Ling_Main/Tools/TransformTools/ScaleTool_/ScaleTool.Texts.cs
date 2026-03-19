// Tools/ScaleTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class ScaleTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Scale", ["ja"] = "拡大縮小", ["hi"] = "かくだいしゅくしょう" },
            ["Uniform"] = new() { ["en"] = "Uniform", ["ja"] = "均一", ["hi"] = "きんいつ" },
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用", ["hi"] = "てきよう" },
            ["Reset"] = new() { ["en"] = "Reset", ["ja"] = "リセット", ["hi"] = "りせっと" },
            ["TargetVertices"] = new() { ["en"] = "Target: {0} vertices", ["ja"] = "対象: {0} 頂点", ["hi"] = "たいしょう: {0} ちょうてん" },
            ["Pivot"] = new() { ["en"] = "Pivot", ["ja"] = "ピボット", ["hi"] = "ぴぼっと" },
            ["UseOrigin"] = new() { ["en"] = "Use Origin", ["ja"] = "原点を使用", ["hi"] = "げんてんをつかう" },
            ["UndoScale"] = new() { ["en"] = "Scale Vertices", ["ja"] = "頂点を拡大縮小", ["hi"] = "ちょうてんをかくだいしゅくしょう" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
