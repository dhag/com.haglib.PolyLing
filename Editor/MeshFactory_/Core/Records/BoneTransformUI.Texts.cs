// Assets/Editor/MeshCreators/BoneTransformUI.Texts.cs
// BoneTransformUI - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public static partial class BoneTransformUI
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"] = new() { ["en"] = "Export Transform", ["ja"] = "エクスポート変換", ["hi"] = "かきだしへんかん" },
            ["Position"] = new() { ["en"] = "Position", ["ja"] = "位置", ["hi"] = "いち" },
            ["Rotation"] = new() { ["en"] = "Rotation", ["ja"] = "回転", ["hi"] = "かいてん" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール", ["hi"] = "おおきさ" },
            ["FromSelection"] = new() { ["en"] = "From Selection", ["ja"] = "選択から取得", ["hi"] = "せんたくからとる" },
            ["Reset"] = new() { ["en"] = "Reset", ["ja"] = "リセット", ["hi"] = "もどす" },
            ["Default"] = new() { ["en"] = "(Default)", ["ja"] = "(デフォルト)", ["hi"] = "(きほん)" },
            ["EnableLocalTransform"] = new() { ["en"] = "Enable Local Transform", ["ja"] = "ローカル変換を有効化", ["hi"] = "ローカルへんかんをゆうこうか" },
            ["DisableLocalTransform"] = new() { ["en"] = "Disable Local Transform", ["ja"] = "ローカル変換を無効化", ["hi"] = "ローカルへんかんをむこうか" },
            ["ChangePosition"] = new() { ["en"] = "Change Export Position", ["ja"] = "位置を変更", ["hi"] = "いちをへんこう" },
            ["ChangeRotation"] = new() { ["en"] = "Change Export Rotation", ["ja"] = "回転を変更", ["hi"] = "かいてんをへんこう" },
            ["ChangeScale"] = new() { ["en"] = "Change Export Scale", ["ja"] = "スケールを変更", ["hi"] = "おおきさをへんこう" },
            ["ChangeSettings"] = new() { ["en"] = "Change Export Settings", ["ja"] = "設定を変更", ["hi"] = "せっていをへんこう" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        private static string T(string key) => L.GetFrom(Texts, key);
    }
}