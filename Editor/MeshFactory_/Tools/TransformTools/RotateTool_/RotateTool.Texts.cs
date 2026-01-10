// Tools/RotateTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class RotateTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Rotate", ["ja"] = "回転", ["hi"] = "かいてん" },
            ["Snap"] = new() { ["en"] = "Snap", ["ja"] = "スナップ", ["hi"] = "すなっぷ" },
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用", ["hi"] = "てきよう" },
            ["Reset"] = new() { ["en"] = "Reset", ["ja"] = "リセット", ["hi"] = "りせっと" },
            ["TargetVertices"] = new() { ["en"] = "Target: {0} vertices", ["ja"] = "対象: {0} 頂点", ["hi"] = "たいしょう: {0} ちょうてん" },
            ["Pivot"] = new() { ["en"] = "Pivot", ["ja"] = "ピボット", ["hi"] = "ぴぼっと" },
            ["UseOrigin"] = new() { ["en"] = "Use Origin", ["ja"] = "原点を使用", ["hi"] = "げんてんをつかう" },
            ["UndoRotate"] = new() { ["en"] = "Rotate Vertices", ["ja"] = "頂点を回転", ["hi"] = "ちょうてんをかいてん" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
