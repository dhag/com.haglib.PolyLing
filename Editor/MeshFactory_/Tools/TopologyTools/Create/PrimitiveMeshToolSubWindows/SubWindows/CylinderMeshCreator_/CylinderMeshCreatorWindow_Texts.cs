// Assets/Editor/MeshCreators/CylinderMeshCreatorWindow.Texts.cs
// シリンダーメッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class CylinderMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Cylinder Mesh", ["ja"] = "シリンダーメッシュ作成", ["hi"] = "つつをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Cylinder Parameters", ["ja"] = "シリンダーパラメータ", ["hi"] = "つつのせってい" },
        ["Size"] = new() { ["en"] = "Size", ["ja"] = "サイズ", ["hi"] = "おおきさ" },
        ["Segments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },
        ["EdgeRounding"] = new() { ["en"] = "Edge Rounding", ["ja"] = "角丸め", ["hi"] = "かどまるめ" },
        ["Caps"] = new() { ["en"] = "Caps", ["ja"] = "キャップ", ["hi"] = "ふた" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
        ["RadiusTop"] = new() { ["en"] = "Radius Top", ["ja"] = "上部半径", ["hi"] = "うえのはんけい" },
        ["RadiusBottom"] = new() { ["en"] = "Radius Bottom", ["ja"] = "下部半径", ["hi"] = "したのはんけい" },
        ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["Radial"] = new() { ["en"] = "Radial", ["ja"] = "周方向", ["hi"] = "まわり" },
        ["HeightSeg"] = new() { ["en"] = "Height", ["ja"] = "高さ方向", ["hi"] = "たかさ" },

        // 角丸め
        ["EdgeRadius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
        ["EdgeSegments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },

        // キャップ
        ["CapTop"] = new() { ["en"] = "Top", ["ja"] = "上", ["hi"] = "うえ" },
        ["CapBottom"] = new() { ["en"] = "Bottom", ["ja"] = "下", ["hi"] = "した" },

        // ピボット
        ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["Bottom"] = new() { ["en"] = "Bottom", ["ja"] = "下", ["hi"] = "した" },
        ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },
        ["Top"] = new() { ["en"] = "Top", ["ja"] = "上", ["hi"] = "うえ" },

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
