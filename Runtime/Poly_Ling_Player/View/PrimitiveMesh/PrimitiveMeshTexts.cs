// PrimitiveMeshTexts.cs
// 図形生成パネル用ローカライズ辞書
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Player
{
    public static class PrimitiveMeshTexts
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // パネルタイトル
            ["PanelTitle"]    = new() { ["en"] = "Create Primitive", ["ja"] = "図形生成",       ["hi"] = "かたちをつくる" },

            // ボタンラベル（9個）
            ["Cube"]          = new() { ["en"] = "Cube",        ["ja"] = "直方体",    ["hi"] = "はこ" },
            ["Sphere"]        = new() { ["en"] = "Sphere",      ["ja"] = "球体",      ["hi"] = "たま" },
            ["Cylinder"]      = new() { ["en"] = "Cylinder",    ["ja"] = "円柱",      ["hi"] = "つつ" },
            ["Capsule"]       = new() { ["en"] = "Capsule",     ["ja"] = "カプセル",  ["hi"] = "カプセル" },
            ["Plane"]         = new() { ["en"] = "Plane",       ["ja"] = "平面",      ["hi"] = "ひらたい" },
            ["Pyramid"]       = new() { ["en"] = "Pyramid",     ["ja"] = "角錐",      ["hi"] = "かくすい" },
            ["Revolution"]    = new() { ["en"] = "Revolution",  ["ja"] = "回転体",    ["hi"] = "かいてんたい" },
            ["Profile2D"]     = new() { ["en"] = "Profile2D",   ["ja"] = "2D押し出し",["hi"] = "おしだし" },
            ["NohMask"]       = new() { ["en"] = "NohMask",     ["ja"] = "能面",      ["hi"] = "のうめん" },

            // パラメータセクション
            ["Name"]          = new() { ["en"] = "Name",        ["ja"] = "名前",      ["hi"] = "なまえ" },
            ["Size"]          = new() { ["en"] = "Size",        ["ja"] = "サイズ",    ["hi"] = "おおきさ" },
            ["Segments"]      = new() { ["en"] = "Segments",    ["ja"] = "分割数",    ["hi"] = "ぶんかつ" },
            ["WidthX"]        = new() { ["en"] = "Width (X)",   ["ja"] = "幅 (X)",    ["hi"] = "はば" },
            ["HeightY"]       = new() { ["en"] = "Height (Y)",  ["ja"] = "高さ (Y)",  ["hi"] = "たかさ" },
            ["DepthZ"]        = new() { ["en"] = "Depth (Z)",   ["ja"] = "奥行き (Z)",["hi"] = "おくゆき" },
            ["Radius"]        = new() { ["en"] = "Radius",      ["ja"] = "半径",      ["hi"] = "はんけい" },
            ["RadiusTop"]     = new() { ["en"] = "Radius Top",  ["ja"] = "上部半径",  ["hi"] = "うえのはんけい" },
            ["RadiusBottom"]  = new() { ["en"] = "Radius Bot",  ["ja"] = "下部半径",  ["hi"] = "したのはんけい" },
            ["Height"]        = new() { ["en"] = "Height",      ["ja"] = "高さ",      ["hi"] = "たかさ" },
            ["Radial"]        = new() { ["en"] = "Radial",      ["ja"] = "周方向",    ["hi"] = "まわり" },
            ["Lateral"]       = new() { ["en"] = "Lateral",     ["ja"] = "縦方向",    ["hi"] = "たてほうこう" },
            ["Cap"]           = new() { ["en"] = "Cap Seg",     ["ja"] = "キャップ分割",["hi"] = "ふたぶんかつ" },
            ["CapTop"]        = new() { ["en"] = "Cap Top",     ["ja"] = "上キャップ",["hi"] = "うえのふた" },
            ["CapBottom"]     = new() { ["en"] = "Cap Bottom",  ["ja"] = "下キャップ",["hi"] = "したのふた" },
            ["Width"]         = new() { ["en"] = "Width",       ["ja"] = "幅",        ["hi"] = "はば" },
            ["DoubleSided"]   = new() { ["en"] = "Double Sided",["ja"] = "両面",      ["hi"] = "りょうめん" },
            ["Orientation"]   = new() { ["en"] = "Plane",       ["ja"] = "向き",      ["hi"] = "むき" },
            ["Sides"]         = new() { ["en"] = "Sides",       ["ja"] = "辺数",      ["hi"] = "へんすう" },
            ["BaseRadius"]    = new() { ["en"] = "Base Radius", ["ja"] = "底面半径",  ["hi"] = "そこのはんけい" },
            ["ApexOffset"]    = new() { ["en"] = "Apex Offset", ["ja"] = "頂点ずれ",  ["hi"] = "ちょうてんずれ" },
            ["CornerRadius"]  = new() { ["en"] = "Corner R",    ["ja"] = "角丸半径",  ["hi"] = "かどまる" },
            ["CubeSphere"]    = new() { ["en"] = "CubeSphere",  ["ja"] = "キューブ球",["hi"] = "キューブきゅう" },
            ["Subdivisions"]  = new() { ["en"] = "Subdivisions",["ja"] = "分割数",    ["hi"] = "ぶんかつ" },
            ["SubdivX"]       = new() { ["en"] = "X",           ["ja"] = "X",         ["hi"] = "X" },
            ["SubdivY"]       = new() { ["en"] = "Y",           ["ja"] = "Y",         ["hi"] = "Y" },
            ["SubdivZ"]       = new() { ["en"] = "Z",           ["ja"] = "Z",         ["hi"] = "Z" },

            // ピボット
            ["PivotOffset"]   = new() { ["en"] = "Pivot Offset",["ja"] = "ピボット",  ["hi"] = "ちゅうしん" },
            ["PivotY"]        = new() { ["en"] = "Y",           ["ja"] = "Y",         ["hi"] = "Y" },
            ["Bottom"]        = new() { ["en"] = "Bottom",      ["ja"] = "下",        ["hi"] = "した" },
            ["Center"]        = new() { ["en"] = "Center",      ["ja"] = "中央",      ["hi"] = "まんなか" },
            ["Top"]           = new() { ["en"] = "Top",         ["ja"] = "上",        ["hi"] = "うえ" },

            // 生成ボタン・ステータス
            ["Create"]        = new() { ["en"] = "Create",      ["ja"] = "生成",      ["hi"] = "つくる" },
            ["NotSupported"]  = new() { ["en"] = "Not supported in Player build", ["ja"] = "Playerビルドでは未対応", ["hi"] = "みたいおう" },
            ["VertsFaces"]    = new() { ["en"] = "V:{0}  F:{1}", ["ja"] = "頂点:{0}  面:{1}", ["hi"] = "てん:{0}  めん:{1}" },
        };

        public static string T(string key) => L.GetFrom(Texts, key);
        public static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
