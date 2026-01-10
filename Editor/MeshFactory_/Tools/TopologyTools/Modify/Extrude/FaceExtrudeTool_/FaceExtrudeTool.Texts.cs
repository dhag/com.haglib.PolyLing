// Tools/FaceExtrudeTool.Texts.cs

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class FaceExtrudeTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Face Extrude Tool", ["ja"] = "面押し出し", ["hi"] = "めんをおしだす" },
            ["Help"] = new() { ["en"] = "Drag face to extrude.\nDrag up to push out, down to push in.", ["ja"] = "面をドラッグして押し出し\n上で押し出し、下で押し込み", ["hi"] = "めんをひっぱっておしだすよ\nうえでだす、したでひっこむ" },
            ["Type"] = new() { ["en"] = "Type", ["ja"] = "タイプ", ["hi"] = "たいぷ" },
            ["BevelSettings"] = new() { ["en"] = "Bevel Settings", ["ja"] = "ベベル設定", ["hi"] = "べべるせってい" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール", ["hi"] = "おおきさ" },
            ["IndividualNormals"] = new() { ["en"] = "Individual Normals", ["ja"] = "個別法線", ["hi"] = "こべつほうせん" },
            ["Distance"] = new() { ["en"] = "Distance", ["ja"] = "距離", ["hi"] = "きょり" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
