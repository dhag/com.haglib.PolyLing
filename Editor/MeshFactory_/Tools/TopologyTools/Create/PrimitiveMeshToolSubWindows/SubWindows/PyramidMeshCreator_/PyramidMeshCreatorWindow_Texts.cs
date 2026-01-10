// Assets/Editor/MeshCreators/PyramidMeshCreatorWindow_Texts.cs
// 角錐メッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class PyramidMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Pyramid Mesh", ["ja"] = "角錐メッシュ作成", ["hi"] = "ピラミッドをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Pyramid Parameters", ["ja"] = "角錐パラメータ", ["hi"] = "ピラミッドのせってい" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
        ["Sides"] = new() { ["en"] = "Sides", ["ja"] = "辺数", ["hi"] = "へんのかず" },
        ["BaseRadius"] = new() { ["en"] = "Base Radius", ["ja"] = "底面半径", ["hi"] = "そこのはんけい" },
        ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["ApexOffset"] = new() { ["en"] = "Apex Offset", ["ja"] = "頂点オフセット", ["hi"] = "てっぺんのずれ" },
        ["CapBottom"] = new() { ["en"] = "Cap Bottom", ["ja"] = "底面を閉じる", ["hi"] = "そこをとじる" },

        // ピボット
        ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },
        ["Bottom"] = new() { ["en"] = "Bottom", ["ja"] = "底面", ["hi"] = "そこ" },
        ["Apex"] = new() { ["en"] = "Apex", ["ja"] = "頂点", ["hi"] = "てっぺん" },

        // 情報
        ["VertsFaces"] = new() { ["en"] = "Vertices: {0}, Faces: {1}", ["ja"] = "頂点: {0}, 面: {1}", ["hi"] = "てん: {0}, めん: {1}" },

        // ボタン（基底クラス用）
        ["Create"] = new() { ["en"] = "Create", ["ja"] = "作成", ["hi"] = "つくる" },
        ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },
        ["Undo"] = new() { ["en"] = "Undo", ["ja"] = "元に戻す", ["hi"] = "もどす" },
        ["Redo"] = new() { ["en"] = "Redo", ["ja"] = "やり直し", ["hi"] = "やりなおす" },
    };

    // ================================================================
    // ローカライズヘルパー
    // ================================================================

    /// <summary>テキスト取得</summary>
    private static string T(string key) => L.GetFrom(Texts, key);

    /// <summary>フォーマット付きテキスト取得</summary>
    private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
}
