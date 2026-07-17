// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"]      = new() { ["en"] = "Knife Tool", ["ja"] = "ナイフ", ["hi"] = "きるどうぐ" },
            ["LadderCut"]  = new() { ["en"] = "Ladder Cut", ["ja"] = "ラダー切断", ["hi"] = "はしごぎり" },
            ["Erase"]      = new() { ["en"] = "Erase", ["ja"] = "辺消去", ["hi"] = "へりけし" },
            ["EqualDivide"] = new() { ["en"] = "Equal Divide", ["ja"] = "等分割",             ["hi"] = "とうぶんかつ" },
            ["BeltLoop"]    = new() { ["en"] = "Belt / Loop",  ["ja"] = "一意分割(ベルト)",     ["hi"] = "ベルトわけ" },

            ["PickBeltEdge"] = new() { ["en"] = "Click an edge to cut its belt/loop.\nESC: Cancel", ["ja"] = "分割する辺をクリック（ベルト/ループ自動）\nESC: キャンセル", ["hi"] = "へりをクリック（ベルト）\nESC: やめる" },
            ["Divisions"]    = new() { ["en"] = "Divisions", ["ja"] = "分割数", ["hi"] = "わけるかず" },
            ["ErrBeltUnreachable"] = new() { ["en"] = "Cannot form a belt from this edge", ["ja"] = "この辺からベルトを構成できません", ["hi"] = "このへりではベルトができません" },

            ["PickStart"]   = new() { ["en"] = "Click start vertex.\nESC: Cancel", ["ja"] = "開始頂点をクリック\nESC: キャンセル", ["hi"] = "はじめのてんをクリック\nESC: やめる" },
            ["PickSegment"] = new() { ["en"] = "Click a segment edge.\nESC: Cancel", ["ja"] = "セグメント辺をクリック\nESC: キャンセル", ["hi"] = "へりをクリック\nESC: やめる" },
            ["PickEnd"]     = new() { ["en"] = "Click end vertex.\nESC: Cancel", ["ja"] = "終了頂点をクリック\nESC: キャンセル", ["hi"] = "おわりのてんをクリック\nESC: やめる" },

            ["ErrSegAdjacent"] = new() { ["en"] = "Segment must not touch the start vertex", ["ja"] = "セグメントは開始頂点に隣接しない辺を選んでください", ["hi"] = "はじめのてんにくっつくへりはだめ" },
            ["ErrSegUnreachable"] = new() { ["en"] = "This edge's belt does not reach the start vertex", ["ja"] = "この辺のベルトが開始頂点に届きません", ["hi"] = "このへりははじめのてんにとどきません" },

            ["HelpErase"]  = new() { ["en"] = "Click a shared edge to erase.", ["ja"] = "共有辺をクリックして消去", ["hi"] = "おなじへりをクリックしてけす" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
