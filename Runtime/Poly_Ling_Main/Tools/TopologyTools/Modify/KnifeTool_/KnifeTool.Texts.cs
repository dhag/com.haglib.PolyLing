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

            ["PickStart"]   = new() { ["en"] = "Click start vertex.\nESC: Cancel", ["ja"] = "開始頂点をクリック\nESC: キャンセル", ["hi"] = "はじめのてんをクリック\nESC: やめる" },
            ["PickSegment"] = new() { ["en"] = "Click a segment edge.\nESC: Cancel", ["ja"] = "セグメント辺をクリック\nESC: キャンセル", ["hi"] = "へりをクリック\nESC: やめる" },
            ["PickEnd"]     = new() { ["en"] = "Click end vertex.\nESC: Cancel", ["ja"] = "終了頂点をクリック\nESC: キャンセル", ["hi"] = "おわりのてんをクリック\nESC: やめる" },

            ["ErrSegAdjacent"] = new() { ["en"] = "Segment must not touch the start vertex", ["ja"] = "セグメントは開始頂点に隣接しない辺を選んでください", ["hi"] = "はじめのてんにくっつくへりはだめ" },

            ["HelpErase"]  = new() { ["en"] = "Click a shared edge to erase.", ["ja"] = "共有辺をクリックして消去", ["hi"] = "おなじへりをクリックしてけす" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
