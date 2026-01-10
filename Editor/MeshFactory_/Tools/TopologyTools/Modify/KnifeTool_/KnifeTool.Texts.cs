// Tools/KnifeTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Knife Tool", ["ja"] = "ナイフ", ["hi"] = "きるどうぐ" },
            ["Cut"] = new() { ["en"] = "Cut", ["ja"] = "切断", ["hi"] = "きる" },
            ["Vertex"] = new() { ["en"] = "Vertex", ["ja"] = "頂点", ["hi"] = "てん" },
            ["Erase"] = new() { ["en"] = "Erase", ["ja"] = "消去", ["hi"] = "けす" },
            ["EdgeSelect"] = new() { ["en"] = "Edge Select (click 2 points)", ["ja"] = "辺選択（2点クリック）", ["hi"] = "2てんをクリック" },
            ["AutoChain"] = new() { ["en"] = "Auto (continuous)", ["ja"] = "自動（連続）", ["hi"] = "じどう（れんぞく）" },
            ["Bisect"] = new() { ["en"] = "Bisect (center)", ["ja"] = "2等分（中央）", ["hi"] = "まんなか" },
            ["CutPosition"] = new() { ["en"] = "Cut Position", ["ja"] = "切断位置", ["hi"] = "きるいち" },
            ["FirstEdgeSelected"] = new() { ["en"] = "First edge selected", ["ja"] = "最初の辺を選択済み", ["hi"] = "さいしょのへりをえらんだ" },
            ["EdgesToCut"] = new() { ["en"] = "Edges to cut: {0}", ["ja"] = "切断辺数: {0}", ["hi"] = "きるへりのかず: {0}" },
            ["VertexSelected"] = new() { ["en"] = "Vertex selected", ["ja"] = "頂点を選択済み", ["hi"] = "てんをえらんだ" },
            ["HelpCutEdge"] = new() { ["en"] = "Click 2 edges to cut faces.\nESC: Cancel", ["ja"] = "2辺をクリックして面を切断\nESC: キャンセル", ["hi"] = "2つのへりをクリックしてきるよ\nESC: やめる" },
            ["HelpCutDrag"] = new() { ["en"] = "Drag to cut faces.\nShift: Snap to axis", ["ja"] = "ドラッグで面を切断\nShift: 軸スナップ", ["hi"] = "ひっぱってきるよ\nShift: じくにあわせる" },
            ["HelpVertexEdge"] = new() { ["en"] = "Click vertex, then edge.\nESC: Cancel", ["ja"] = "頂点→辺をクリック\nESC: キャンセル", ["hi"] = "てん→へりをクリック\nESC: やめる" },
            ["HelpVertexDrag"] = new() { ["en"] = "Drag from vertex to cut.\nShift: Snap to axis", ["ja"] = "頂点からドラッグで切断\nShift: 軸スナップ", ["hi"] = "てんからひっぱってきるよ\nShift: じくにあわせる" },
            ["HelpErase"] = new() { ["en"] = "Click shared edge to erase.", ["ja"] = "共有辺をクリックして消去", ["hi"] = "おなじへりをクリックしてけす" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
