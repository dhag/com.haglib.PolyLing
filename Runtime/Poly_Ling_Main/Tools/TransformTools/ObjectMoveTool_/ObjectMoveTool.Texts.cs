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
            ["PickBones"]        = new() { ["en"] = "Pick Bones",            ["ja"] = "ボーン",                  ["hi"] = "ほね" },
            ["PickMeshesNoSkin"] = new() { ["en"] = "Pick Meshes (no skin)", ["ja"] = "スキンドでないメッシュ",  ["hi"] = "すきんなしのめっしゅ" },
            ["PickMeshesSkinned"]= new() { ["en"] = "Pick Meshes (skinned)", ["ja"] = "スキンドメッシュ",         ["hi"] = "すきんあめっしゅ" },
            ["MoveModeA"]        = new() { ["en"] = "Move bone only (skin fixed)", ["ja"] = "ボーンだけ動かす（スキン固定）", ["hi"] = "ほねだけ" },
            ["MoveModeB"]        = new() { ["en"] = "Move & bake skin",           ["ja"] = "スキンごと動かして確定",       ["hi"] = "すきんごとやく" },
            ["MoveModeLabel"]    = new() { ["en"] = "Move mode",                  ["ja"] = "移動モード",                 ["hi"] = "うごかしかた" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
