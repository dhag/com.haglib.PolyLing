// Tools/TransformTools/ObjectMoveTool_/ObjectMoveTool.Texts.cs
// オブジェクト移動ツール - ローカライズ辞書

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class ObjectMoveTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["Title"]            = new() { ["en"] = "Object Move Tool", ["ja"] = "オブジェクト移動ツール", ["hi"] = "おぶじぇくとうごかすどうぐ" },
            ["MoveWithChildren"] = new() { ["en"] = "Move With Children",  ["ja"] = "子を一緒に移動",        ["hi"] = "こどもといっしょにうごかす" },
            ["TargetObjects"]    = new() { ["en"] = "Target: {0} objects", ["ja"] = "移動対象: {0} オブジェクト", ["hi"] = "うごかすもの: {0}こ" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
