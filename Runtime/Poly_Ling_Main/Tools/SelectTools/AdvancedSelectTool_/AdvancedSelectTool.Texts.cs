// Tools/AdvancedSelectTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
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
            ["UvNormalCount"] = new() { ["en"] = "UV/Nrm Count", ["ja"] = "UV/法線数", ["hi"] = "UVとほうせんのかず" },
            ["NearAxis"] = new() { ["en"] = "Near Axis", ["ja"] = "軸近傍", ["hi"] = "じくのちかく" },
            ["UvNormalThreshold"] = new() { ["en"] = "Count Threshold", ["ja"] = "データ数しきい値", ["hi"] = "かずのしきいち" },
            ["AxisDistanceThreshold"] = new() { ["en"] = "Distance Threshold", ["ja"] = "距離しきい値", ["hi"] = "きょりのしきいち" },
            ["Axis"] = new() { ["en"] = "Axis", ["ja"] = "軸", ["hi"] = "じく" },
            ["LimitToSelection"] = new() { ["en"] = "Within current selection", ["ja"] = "選択中の頂点内から", ["hi"] = "えらんでるてんのなかから" },
            ["Execute"] = new() { ["en"] = "Execute", ["ja"] = "実行", ["hi"] = "じっこう" },
            ["InvertSelection"] = new() { ["en"] = "Invert Selection", ["ja"] = "現在の選択を反転", ["hi"] = "いまのせんたくをはんてん" },
            ["BoundaryEdgeGroup"] = new() { ["en"] = "Edge Group", ["ja"] = "エッジ群", ["hi"] = "エッジのかたまり" },
            ["BoundaryEdgeInSelection"] = new() { ["en"] = "Edge In Sel", ["ja"] = "選択内エッジ", ["hi"] = "えらんだなかのエッジ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
