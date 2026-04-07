// MeshListTexts.cs
// メッシュリストサブパネル用ローカライズ辞書
// Runtime/Poly_Ling_Player/View/ に配置

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Player
{
    public static class MeshListTexts
    {
        public static readonly Dictionary<string, Dictionary<string, string>> Texts = new Dictionary<string, Dictionary<string, string>>
        {
            ["Title"]          = new Dictionary<string, string> { ["en"] = "Mesh List",       ["ja"] = "メッシュリスト" },
            ["SkinnedMesh"]    = new Dictionary<string, string> { ["en"] = "Skinned Mesh",    ["ja"] = "スキンドメッシュ" },
            ["TabDrawable"]    = new Dictionary<string, string> { ["en"] = "Mesh",            ["ja"] = "メッシュ" },
            ["TabBone"]        = new Dictionary<string, string> { ["en"] = "Bone",            ["ja"] = "ボーン" },
            ["TabMorph"]       = new Dictionary<string, string> { ["en"] = "Morph",           ["ja"] = "モーフ" },
            ["Filter"]         = new Dictionary<string, string> { ["en"] = "Filter...",       ["ja"] = "フィルター..." },
            ["ShowInfo"]       = new Dictionary<string, string> { ["en"] = "Info",            ["ja"] = "情報表示" },
            ["ShowMirrorSide"] = new Dictionary<string, string> { ["en"] = "Mirror Side",     ["ja"] = "ミラー側表示" },
            ["Add"]            = new Dictionary<string, string> { ["en"] = "+",               ["ja"] = "+" },
            ["Delete"]         = new Dictionary<string, string> { ["en"] = "Del",             ["ja"] = "削除" },
            ["Duplicate"]      = new Dictionary<string, string> { ["en"] = "Dup",             ["ja"] = "複製" },
            ["Show"]           = new Dictionary<string, string> { ["en"] = "Show",            ["ja"] = "表示" },
            ["Hide"]           = new Dictionary<string, string> { ["en"] = "Hide",            ["ja"] = "非表示" },
            ["MoveUp"]         = new Dictionary<string, string> { ["en"] = "▲",              ["ja"] = "▲" },
            ["MoveDown"]       = new Dictionary<string, string> { ["en"] = "▼",              ["ja"] = "▼" },
            ["Details"]        = new Dictionary<string, string> { ["en"] = "Details",         ["ja"] = "詳細" },
            ["VertexCount"]    = new Dictionary<string, string> { ["en"] = "Vertices: {0}",   ["ja"] = "頂点: {0}" },
            ["FaceCount"]      = new Dictionary<string, string> { ["en"] = "Faces: {0}",      ["ja"] = "面: {0}" },
            ["TriCount"]       = new Dictionary<string, string> { ["en"] = "Tri: {0}",        ["ja"] = "三角形: {0}" },
            ["QuadCount"]      = new Dictionary<string, string> { ["en"] = "Quad: {0}",       ["ja"] = "四角形: {0}" },
            ["NgonCount"]      = new Dictionary<string, string> { ["en"] = "Ngon: {0}",       ["ja"] = "多角形: {0}" },
            ["BoneIndex"]      = new Dictionary<string, string> { ["en"] = "Bone Idx: {0}",   ["ja"] = "ボーンIdx: {0}" },
            ["MasterIndex"]    = new Dictionary<string, string> { ["en"] = "Master Idx: {0}", ["ja"] = "マスターIdx: {0}" },
            ["BonePose"]       = new Dictionary<string, string> { ["en"] = "Bone Pose",       ["ja"] = "ボーンポーズ" },
            ["PoseActive"]     = new Dictionary<string, string> { ["en"] = "Active",          ["ja"] = "アクティブ" },
            ["Position"]       = new Dictionary<string, string> { ["en"] = "Position",        ["ja"] = "位置" },
            ["Rotation"]       = new Dictionary<string, string> { ["en"] = "Rotation",        ["ja"] = "回転" },
            ["Scale"]          = new Dictionary<string, string> { ["en"] = "Scale",           ["ja"] = "スケール" },
            ["ResultPos"]      = new Dictionary<string, string> { ["en"] = "Result Pos: -",   ["ja"] = "結果位置: -" },
            ["ResultRot"]      = new Dictionary<string, string> { ["en"] = "Result Rot: -",   ["ja"] = "結果回転: -" },
            ["BindPose"]       = new Dictionary<string, string> { ["en"] = "Bind Pose",       ["ja"] = "バインドポーズ" },
            ["ResetLayers"]    = new Dictionary<string, string> { ["en"] = "Reset Layers",    ["ja"] = "レイヤーリセット" },
            ["BakePose"]       = new Dictionary<string, string> { ["en"] = "Bake Pose",       ["ja"] = "ポーズベイク" },
            ["Transform"]      = new Dictionary<string, string> { ["en"] = "Transform",       ["ja"] = "トランスフォーム" },
            ["MorphList"]      = new Dictionary<string, string> { ["en"] = "Morph List",      ["ja"] = "モーフリスト" },
            ["MorphToMesh"]    = new Dictionary<string, string> { ["en"] = "Morph→Mesh",      ["ja"] = "モーフ→メッシュ" },
            ["MeshToMorph"]    = new Dictionary<string, string> { ["en"] = "Mesh→Morph",      ["ja"] = "メッシュ→モーフ" },
            ["SourceMesh"]     = new Dictionary<string, string> { ["en"] = "Source:",         ["ja"] = "元メッシュ:" },
            ["Parent"]         = new Dictionary<string, string> { ["en"] = "Parent:",         ["ja"] = "親:" },
            ["MorphPanel"]     = new Dictionary<string, string> { ["en"] = "Panel:",          ["ja"] = "パネル:" },
            ["MorphName"]      = new Dictionary<string, string> { ["en"] = "Name:",           ["ja"] = "名前:" },
            ["CreateMorphSet"] = new Dictionary<string, string> { ["en"] = "Create Set",      ["ja"] = "セット作成" },
            ["SetName"]        = new Dictionary<string, string> { ["en"] = "Set Name:",       ["ja"] = "セット名:" },
            ["SetType"]        = new Dictionary<string, string> { ["en"] = "Type:",           ["ja"] = "種別:" },
            ["TestWeight"]     = new Dictionary<string, string> { ["en"] = "Test Weight",     ["ja"] = "テストウェイト" },
            ["ResetWeight"]    = new Dictionary<string, string> { ["en"] = "Reset",           ["ja"] = "リセット" },
            ["SelectAll"]      = new Dictionary<string, string> { ["en"] = "All",             ["ja"] = "全選択" },
            ["DeselectAll"]    = new Dictionary<string, string> { ["en"] = "None",            ["ja"] = "全解除" },
            ["CountMesh"]      = new Dictionary<string, string> { ["en"] = "Mesh: {0}",       ["ja"] = "メッシュ: {0}" },
            ["CountBone"]      = new Dictionary<string, string> { ["en"] = "Bone: {0}",       ["ja"] = "ボーン: {0}" },
            ["CountMorph"]     = new Dictionary<string, string> { ["en"] = "Morph: {0}",      ["ja"] = "モーフ: {0}" },
            ["MultiSelect"]    = new Dictionary<string, string> { ["en"] = "({0} selected)",  ["ja"] = "({0}個選択)" },
            ["NoSelection"]    = new Dictionary<string, string> { ["en"] = "-",               ["ja"] = "-" },
            ["BoneIdxFmt"]     = new Dictionary<string, string> { ["en"] = "Bone:{0}",        ["ja"] = "Bone:{0}" },
        };

        public static string T(string key)                    => L.GetFrom(Texts, key);
        public static string T(string key, params object[] a) => L.GetFrom(Texts, key, a);
    }
}
