// Tools/AdvancedSelectTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class AdvancedSelectTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Advanced Select Tool", ["ja"] = "詳細選択", ["hi"] = "くわしくえらぶ" },
            ["Connected"] = new() { ["en"] = "Connected", ["ja"] = "接続", ["hi"] = "つながり" },
            ["Belt"] = new() { ["en"] = "Belt", ["ja"] = "ベルト", ["hi"] = "おび" },
            ["EdgeLoop"] = new() { ["en"] = "EdgeLoop", ["ja"] = "辺ループ", ["hi"] = "へんわっか" },
            ["Shortest"] = new() { ["en"] = "Shortest", ["ja"] = "最短", ["hi"] = "さいたん" },
            ["DirectionThreshold"] = new() { ["en"] = "Direction Threshold", ["ja"] = "方向しきい値", ["hi"] = "むきしきいち" },
            ["Action"] = new() { ["en"] = "Action:", ["ja"] = "動作:", ["hi"] = "どうさ:" },
            ["Add"] = new() { ["en"] = "Add", ["ja"] = "追加", ["hi"] = "ついか" },
            ["Remove"] = new() { ["en"] = "Remove", ["ja"] = "削除", ["hi"] = "さくじょ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
