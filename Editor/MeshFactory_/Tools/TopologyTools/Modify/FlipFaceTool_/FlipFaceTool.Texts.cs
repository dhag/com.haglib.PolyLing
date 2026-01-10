// Tools/FlipFaceTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class FlipFaceTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Flip Face Tool", ["ja"] = "面反転", ["hi"] = "めんをひっくりかえす" },
            ["Help"] = new() { ["en"] = "Flip normals of selected faces.\nSelect faces in Face mode.", ["ja"] = "選択した面の法線（表裏）を反転します。\nFaceモードで面を選択してから実行してください。", ["hi"] = "えらんだめんのうらおもてをかえるよ\nめんもーどでめんをえらんでね" },
            ["FlipSelected"] = new() { ["en"] = "Flip Selected Faces", ["ja"] = "選択面を反転", ["hi"] = "えらんだめんをひっくりかえす" },
            ["FlipAll"] = new() { ["en"] = "Flip All Faces", ["ja"] = "全ての面を反転", ["hi"] = "ぜんぶのめんをひっくりかえす" },
            ["NoMesh"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが選択されていません", ["hi"] = "めっしゅがえらばれてないよ" },
            ["NoFaces"] = new() { ["en"] = "No faces selected", ["ja"] = "面が選択されていません", ["hi"] = "めんがえらばれてないよ" },
            ["SwitchToFaceMode"] = new() { ["en"] = "Switch to Face mode (F key)", ["ja"] = "Faceモードに切り替えてください (Fキー)", ["hi"] = "めんもーどにしてね（Fきー）" },
            ["FlippedCount"] = new() { ["en"] = "Flipped {0} faces", ["ja"] = "{0} 面を反転しました", ["hi"] = "{0}めんをひっくりかえしたよ" },
            ["FlippedAllCount"] = new() { ["en"] = "Flipped all {0} faces", ["ja"] = "全 {0} 面を反転しました", ["hi"] = "ぜんぶの{0}めんをひっくりかえしたよ" },
            ["SelectedCount"] = new() { ["en"] = "{0} faces selected", ["ja"] = "{0} 面を選択中", ["hi"] = "{0}めんをえらんでるよ" },
            ["NoFacesExist"] = new() { ["en"] = "No faces exist", ["ja"] = "面がありません", ["hi"] = "めんがないよ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
