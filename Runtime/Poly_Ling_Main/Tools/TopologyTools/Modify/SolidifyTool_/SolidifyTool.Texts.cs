// SolidifyTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class SolidifyTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Solidify", ["ja"] = "厚み付け", ["hi"] = "あつみをつける" },
            ["Help"] = new() { ["en"] = "Give thickness to the selected thin faces.\nTwo copies (front / flipped back) are offset by half the thickness and their open edges are bridged.", ["ja"] = "選択した薄い面群に厚みを付けます。\n表裏2枚のコピーを厚みの半分ずつ移動し、孤立エッジを側面でつなぎます。\n元の面はそのまま残ります。", ["hi"] = "えらんだうすいめんをあつくするよ\nもとのめんはのこるよ" },
            ["Thickness"] = new() { ["en"] = "Thickness", ["ja"] = "厚み", ["hi"] = "あつみ" },
            ["AddToExisting"] = new() { ["en"] = "Add to existing mesh", ["ja"] = "既存メッシュに追加", ["hi"] = "いまのめっしゅにたす" },
            ["MeshName"] = new() { ["en"] = "Mesh name", ["ja"] = "メッシュ名", ["hi"] = "めっしゅのなまえ" },
            ["Execute"] = new() { ["en"] = "Solidify", ["ja"] = "厚み付け実行", ["hi"] = "あつみをつける" },
            ["NoMesh"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが選択されていません", ["hi"] = "めっしゅがえらばれてないよ" },
            ["NoFaces"] = new() { ["en"] = "No faces selected", ["ja"] = "面が選択されていません", ["hi"] = "めんがえらばれてないよ" },
            ["SwitchToFaceMode"] = new() { ["en"] = "Switch to Face mode", ["ja"] = "Faceモードに切り替えてください", ["hi"] = "めんもーどにしてね" },
            ["SelectedCount"] = new() { ["en"] = "{0} faces selected", ["ja"] = "{0} 面を選択中", ["hi"] = "{0}めんをえらんでるよ" },
            ["Failed"] = new() { ["en"] = "Failed: {0}", ["ja"] = "失敗しました: {0}", ["hi"] = "できなかったよ: {0}" },
            ["Done"] = new() { ["en"] = "Solidified {0} faces (open edges {1}, side faces {2})", ["ja"] = "{0} 面を厚み付けしました（孤立エッジ {1} / 側面 {2}）", ["hi"] = "{0}めんをあつくしたよ" },
            ["DoneNoBoundary"] = new() { ["en"] = "Solidified {0} faces (no open edge found)", ["ja"] = "{0} 面を厚み付けしました（孤立エッジなし）", ["hi"] = "{0}めんをあつくしたよ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
