// Assets/Editor/MeshCreators/CubeMeshCreatorWindow.Texts.cs
// 角丸直方体メッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class CubeMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Rounded Cube Mesh", ["ja"] = "角丸直方体メッシュ作成", ["hi"] = "まるいはこをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Rounded Cube Parameters", ["ja"] = "角丸直方体パラメータ", ["hi"] = "まるいはこのせってい" },
        ["LinkOptions"] = new() { ["en"] = "Link Options", ["ja"] = "連動オプション", ["hi"] = "れんどうオプション" },
        ["SizeLinked"] = new() { ["en"] = "Size (Linked)", ["ja"] = "サイズ（連動）", ["hi"] = "おおきさ（れんどう）" },
        ["SizeTop"] = new() { ["en"] = "Top Size", ["ja"] = "上部サイズ", ["hi"] = "うえのおおきさ" },
        ["SizeBottom"] = new() { ["en"] = "Bottom Size", ["ja"] = "下部サイズ", ["hi"] = "したのおおきさ" },
        ["CornerRounding"] = new() { ["en"] = "Corner Rounding", ["ja"] = "角丸め", ["hi"] = "かどまるめ" },
        ["Corner"] = new() { ["en"] = "Corner", ["ja"] = "角", ["hi"] = "かど" },
        ["Size"] = new() { ["en"] = "Size", ["ja"] = "サイズ", ["hi"] = "おおきさ" },
        ["Subdivisions"] = new() { ["en"] = "Subdivisions", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // 連動オプション
        ["LinkWHD"] = new() { ["en"] = "Link W/H/D (Cube Mode)", ["ja"] = "W/H/D連動（立方体モード）", ["hi"] = "たて・よこ・たかさをそろえる" },
        ["LinkTopBottom"] = new() { ["en"] = "Link Top/Bottom Size", ["ja"] = "上下サイズ連動", ["hi"] = "うえとしたをそろえる" },

        // フィールド
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
        ["WidthX"] = new() { ["en"] = "Width (X)", ["ja"] = "幅 (X)", ["hi"] = "はば (X)" },
        ["HeightY"] = new() { ["en"] = "Height (Y)", ["ja"] = "高さ (Y)", ["hi"] = "たかさ (Y)" },
        ["DepthZ"] = new() { ["en"] = "Depth (Z)", ["ja"] = "奥行き (Z)", ["hi"] = "おくゆき (Z)" },
        ["Width"] = new() { ["en"] = "Width", ["ja"] = "幅", ["hi"] = "はば" },
        ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["Depth"] = new() { ["en"] = "Depth", ["ja"] = "奥行き", ["hi"] = "おくゆき" },

        // 角丸め
        ["CornerRadius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
        ["CornerSegments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },

        // 分割数
        ["SubdivX"] = new() { ["en"] = "X", ["ja"] = "X", ["hi"] = "X" },
        ["SubdivY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["SubdivZ"] = new() { ["en"] = "Z", ["ja"] = "Z", ["hi"] = "Z" },

        // ピボット
        ["PivotX"] = new() { ["en"] = "X", ["ja"] = "X", ["hi"] = "X" },
        ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["PivotZ"] = new() { ["en"] = "Z", ["ja"] = "Z", ["hi"] = "Z" },
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
