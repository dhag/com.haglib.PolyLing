// Tools/EdgeTopologyTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class EdgeTopologyTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Edge Topology Tool", ["ja"] = "エッジトポロジ", ["hi"] = "へんへんしゅう" },
            ["Flip"] = new() { ["en"] = "Flip", ["ja"] = "入替", ["hi"] = "いれかえ" },
            ["Split"] = new() { ["en"] = "Split", ["ja"] = "分割", ["hi"] = "わける" },
            ["Dissolve"] = new() { ["en"] = "Dissolve", ["ja"] = "結合", ["hi"] = "くっつける" },
            ["HelpFlip"] = new() { ["en"] = "Click shared edge to flip diagonal\n(requires 2 triangles)", ["ja"] = "共有辺をクリックして対角線を入れ替え\n（2つの三角形が必要）", ["hi"] = "おなじへりをクリックしてななめをかえるよ\n（さんかくが2ついるよ）" },
            ["HelpSplit"] = new() { ["en"] = "Drag from quad vertex to split", ["ja"] = "四角形の対角頂点をドラッグして分割", ["hi"] = "しかくのかどをひっぱってわけるよ" },
            ["HelpDissolve"] = new() { ["en"] = "Click shared edge to merge 2 faces", ["ja"] = "共有辺をクリックして2つの面を結合", ["hi"] = "おなじへりをクリックしてくっつけるよ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
