// Assets/Editor/MeshCreators/PlaneMeshCreatorWindow.Texts.cs
// プレーンメッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class PlaneMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Plane Mesh", ["ja"] = "プレーンメッシュ作成", ["hi"] = "いたをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Plane Parameters", ["ja"] = "プレーンパラメータ", ["hi"] = "いたのせってい" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
        ["Width"] = new() { ["en"] = "Width", ["ja"] = "幅", ["hi"] = "はば" },
        ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["WidthSegments"] = new() { ["en"] = "Width Segments", ["ja"] = "幅分割数", ["hi"] = "はばのぶんかつすう" },
        ["HeightSegments"] = new() { ["en"] = "Height Segments", ["ja"] = "高さ分割数", ["hi"] = "たかさのぶんかつすう" },
        ["Orientation"] = new() { ["en"] = "Orientation", ["ja"] = "向き", ["hi"] = "むき" },
        ["DoubleSided"] = new() { ["en"] = "Double Sided", ["ja"] = "両面", ["hi"] = "りょうめん" },

        // ピボット
        ["PivotX"] = new() { ["en"] = "X", ["ja"] = "X", ["hi"] = "X" },
        ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },
        ["Corner"] = new() { ["en"] = "Corner", ["ja"] = "角", ["hi"] = "かど" },

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
