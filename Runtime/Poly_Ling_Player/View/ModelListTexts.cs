// ModelListTexts.cs
// モデルリストサブパネル用ローカライズ辞書
// Runtime/Poly_Ling_Player/View/ に配置

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Player
{
    public static class ModelListTexts
    {
        public static readonly Dictionary<string, Dictionary<string, string>> Texts = new Dictionary<string, Dictionary<string, string>>
        {
            ["Title"]     = new Dictionary<string, string> { ["en"] = "Model List",   ["ja"] = "モデルリスト" },
            ["NoProject"] = new Dictionary<string, string> { ["en"] = "No project",   ["ja"] = "プロジェクトがありません" },
            ["NoModel"]   = new Dictionary<string, string> { ["en"] = "No models",    ["ja"] = "モデルがありません" },
            ["Rename"]    = new Dictionary<string, string> { ["en"] = "✎ Rename",     ["ja"] = "✎ 名前変更" },
            ["Confirm"]   = new Dictionary<string, string> { ["en"] = "✓",            ["ja"] = "✓" },
            ["Cancel"]    = new Dictionary<string, string> { ["en"] = "✕",            ["ja"] = "✕" },
            ["Delete"]    = new Dictionary<string, string> { ["en"] = "×",            ["ja"] = "×" },
            ["MeshCount"] = new Dictionary<string, string> { ["en"] = "{0} mesh",     ["ja"] = "{0} mesh" },
        };

        public static string T(string key)                    => L.GetFrom(Texts, key);
        public static string T(string key, params object[] a) => L.GetFrom(Texts, key, a);
    }
}
