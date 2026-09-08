// McpSandboxTexts.cs
// MCP用サンドボックスの表示文字列。
// Runtime/Poly_Ling_Player/View/McpSandbox/ に配置
//
// 【なぜ PrimitiveMeshTexts と分けるか】
//   サンドボックスは試作の置き場で、ラベルを気軽に書き換えたい。
//   図形生成パネルと辞書を共有すると、その書き換えが本番側の表示を動かす。
//   キー名は PrimitiveMeshTexts と揃えてあるので、育った試作を
//   向こうへ移すときは辞書の行を移すだけで済む。

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Player
{
    public static class McpSandboxTexts
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["PanelTitle"]    = new() { ["en"] = "MCP Sandbox",   ["ja"] = "MCP用サンドボックス", ["hi"] = "MCPのすなば" },
            ["McpCylinder"]   = new() { ["en"] = "MCP Cylinder",  ["ja"] = "MCP円筒",       ["hi"] = "MCPのつつ" },

            ["Size"]          = new() { ["en"] = "Size",          ["ja"] = "サイズ",        ["hi"] = "おおきさ" },
            ["Segments"]      = new() { ["en"] = "Segments",      ["ja"] = "分割数",        ["hi"] = "ぶんかつ" },
            ["MeshName"]      = new() { ["en"] = "Name",          ["ja"] = "名前",          ["hi"] = "なまえ" },
            ["RadiusTop"]     = new() { ["en"] = "Radius Top",    ["ja"] = "上部半径",      ["hi"] = "うえのはんけい" },
            ["RadiusBottom"]  = new() { ["en"] = "Radius Bot",    ["ja"] = "下部半径",      ["hi"] = "したのはんけい" },
            ["Height"]        = new() { ["en"] = "Height",        ["ja"] = "高さ",          ["hi"] = "たかさ" },
            ["Radial"]        = new() { ["en"] = "Radial",        ["ja"] = "周方向",        ["hi"] = "まわり" },
            ["Lateral"]       = new() { ["en"] = "Lateral",       ["ja"] = "縦方向",        ["hi"] = "たてほうこう" },
            ["CapTop"]        = new() { ["en"] = "Cap Top",       ["ja"] = "上キャップ",    ["hi"] = "うえのふた" },
            ["CapBottom"]     = new() { ["en"] = "Cap Bottom",    ["ja"] = "下キャップ",    ["hi"] = "したのふた" },
            ["EdgeRadius"]    = new() { ["en"] = "Edge Radius",   ["ja"] = "エッジ丸め",    ["hi"] = "えっじまる" },
            ["EdgeSeg"]       = new() { ["en"] = "Edge Seg",      ["ja"] = "エッジ分割",    ["hi"] = "えっじぶんかつ" },

            ["PivotOffset"]   = new() { ["en"] = "Pivot",         ["ja"] = "ピボット位置",  ["hi"] = "ちゅうしん" },
            ["PivotY"]        = new() { ["en"] = "Pivot Y",       ["ja"] = "ピボットY",     ["hi"] = "ちゅうしんY" },
            ["Bottom"]        = new() { ["en"] = "Bottom",        ["ja"] = "下",            ["hi"] = "した" },
            ["Center"]        = new() { ["en"] = "Center",        ["ja"] = "中",            ["hi"] = "なか" },
            ["Top"]           = new() { ["en"] = "Top",           ["ja"] = "上",            ["hi"] = "うえ" },

            ["WorldPos"]      = new() { ["en"] = "World Position",["ja"] = "生成位置",      ["hi"] = "つくるばしょ" },
            ["WorldPosX"]     = new() { ["en"] = "X",             ["ja"] = "X",             ["hi"] = "X" },
            ["WorldPosY"]     = new() { ["en"] = "Y",             ["ja"] = "Y",             ["hi"] = "Y" },
            ["WorldPosZ"]     = new() { ["en"] = "Z",             ["ja"] = "Z",             ["hi"] = "Z" },

            ["AddMode"]         = new() { ["en"] = "Add To",         ["ja"] = "追加先",            ["hi"] = "どこへ" },
            ["AddModeNewObj"]   = new() { ["en"] = "New Object",     ["ja"] = "新規オブジェクト",  ["hi"] = "あたらしいもの" },
            ["AddModeExisting"] = new() { ["en"] = "Existing",       ["ja"] = "既存へ追加",        ["hi"] = "いまのものへ" },
            ["AddModeNewModel"] = new() { ["en"] = "New Model",      ["ja"] = "新規モデル",        ["hi"] = "あたらしいモデル" },

            ["SelectShape"]   = new() { ["en"] = "Pick a shape above.", ["ja"] = "上の図形ボタンを押してください。", ["hi"] = "うえのボタンをおしてね" },
            ["Create"]        = new() { ["en"] = "Create",        ["ja"] = "生成",          ["hi"] = "つくる" },
            ["Created"]       = new() { ["en"] = "Created",       ["ja"] = "生成しました",  ["hi"] = "つくったよ" },
            ["FailNoShape"]   = new() { ["en"] = "No shape selected", ["ja"] = "図形が選ばれていません", ["hi"] = "かたちがえらばれてない" },
            ["FailNoWire"]    = new() { ["en"] = "SendCommand is not wired", ["ja"] = "配線が足りません（SendCommand）", ["hi"] = "つながってない" },
        };

        public static string T(string key) => L.GetFrom(Texts, key);
        public static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
