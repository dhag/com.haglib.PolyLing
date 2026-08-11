// Tools/SmoothEdgesTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class SmoothEdgesTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Smooth Edges", ["ja"] = "辺を滑らかに", ["hi"] = "へんをなめらかに" },
            ["Help"] = new()
            {
                ["en"] = "Relaxes vertices along the selected edges / lines.\nOnly the selected chain is used as neighbors.",
                ["ja"] = "選択した辺・線分に沿って頂点を滑らかにします。\n隣接は選択したチェーンだけを辿ります。",
                ["hi"] = "えらんだへん・せんぶんにそってなめらかにします。\nとなりはえらんだつながりだけをみます。"
            },
            ["Segments"] = new() { ["en"] = "Segments: {0}", ["ja"] = "辺・線分: {0} 本", ["hi"] = "へん・せんぶん: {0} ほん" },
            ["ChainVertices"] = new() { ["en"] = "Chain vertices: {0}", ["ja"] = "チェーン頂点: {0}", ["hi"] = "つながりのてん: {0}" },
            ["Endpoints"] = new() { ["en"] = "Endpoints: {0}", ["ja"] = "端点: {0}", ["hi"] = "はしのてん: {0}" },
            ["MovableVertices"] = new() { ["en"] = "Movable: {0}", ["ja"] = "移動対象: {0} 頂点", ["hi"] = "うごくてん: {0}" },
            ["NoSelection"] = new() { ["en"] = "Select edges or lines", ["ja"] = "辺または線分を選択してください", ["hi"] = "へんかせんぶんをえらんでね" },
            ["Strength"] = new() { ["en"] = "Strength", ["ja"] = "強度", ["hi"] = "つよさ" },
            ["Iterations"] = new() { ["en"] = "Iterations", ["ja"] = "反復回数", ["hi"] = "くりかえし" },
            ["FixEndpoints"] = new() { ["en"] = "Fix start / end points", ["ja"] = "開始点・終了点を固定", ["hi"] = "はじめとおわりをうごかさない" },
            ["AxisLock"] = new() { ["en"] = "Axis lock:", ["ja"] = "軸ロック:", ["hi"] = "じくのロック:" },
            ["Execute"] = new() { ["en"] = "Smooth", ["ja"] = "平滑化実行", ["hi"] = "なめらかにする" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
