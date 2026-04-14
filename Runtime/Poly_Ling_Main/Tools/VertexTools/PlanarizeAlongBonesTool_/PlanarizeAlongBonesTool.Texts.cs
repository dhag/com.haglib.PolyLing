// Tools/PlanarizeAlongBonesTool.Texts.cs

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class PlanarizeAlongBonesTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Planarize Along Bones", ["ja"] = "ボーン間平面化", ["hi"] = "ぼーんかんへいめんか" },
            ["Help"] = new() { ["en"] = "Flatten selected vertices onto a plane perpendicular to the A→B bone direction.\nBlend controls how much to flatten.", ["ja"] = "選択頂点をボーンA→B方向に直交する平面に揃えます。\nブレンドで平面化の度合いを調整できます。", ["hi"] = "えらんだてんをぼーんのほうこうにたいらにします。\nぶれんどでどのくらいたいらにするかえらべます。" },
            ["SelectedVertices"] = new() { ["en"] = "Selected: {0} vertices", ["ja"] = "選択中: {0} 頂点", ["hi"] = "せんたくちゅう: {0} てん" },
            ["NeedVertices"] = new() { ["en"] = "Select 1 or more vertices", ["ja"] = "1つ以上の頂点を選択してください", ["hi"] = "1つ以上のてんをえらんでね" },
            ["NoBones"] = new() { ["en"] = "No bones found in model", ["ja"] = "モデルにボーンがありません", ["hi"] = "ぼーんがないよ" },
            ["BoneA"] = new() { ["en"] = "Bone A (plane origin):", ["ja"] = "ボーンA（平面基点）:", ["hi"] = "ぼーんA（きてん）:" },
            ["BoneB"] = new() { ["en"] = "Bone B (direction):", ["ja"] = "ボーンB（方向）:", ["hi"] = "ぼーんB（ほうこう）:" },
            ["SameBoneWarning"] = new() { ["en"] = "Select different bones for A and B", ["ja"] = "AとBに異なるボーンを選択してください", ["hi"] = "AとBはちがうぼーんにしてね" },
            ["PlaneMode"] = new() { ["en"] = "Plane Position:", ["ja"] = "平面位置:", ["hi"] = "へいめんいち:" },
            ["Blend"] = new() { ["en"] = "Blend (0=none, 1=full):", ["ja"] = "ブレンド（0=なし、1=完全）:", ["hi"] = "ぶれんど（0=なし、1=かんぜん）:" },
            ["PreviewInfo"] = new() { ["en"] = "Bone Positions:", ["ja"] = "ボーン位置:", ["hi"] = "ぼーんいち:" },
            ["Distance"] = new() { ["en"] = "Distance", ["ja"] = "距離", ["hi"] = "きょり" },
            ["Execute"] = new() { ["en"] = "Planarize", ["ja"] = "平面化実行", ["hi"] = "へいめんかする" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
