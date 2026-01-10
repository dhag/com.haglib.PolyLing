// Assets/Editor/MeshCreators/SphereMeshCreatorWindow.Texts.cs
// スフィアメッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class SphereMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Sphere Mesh", ["ja"] = "スフィアメッシュ作成", ["hi"] = "たまをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Sphere Parameters", ["ja"] = "スフィアパラメータ", ["hi"] = "たまのせってい" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
        ["Radius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
        ["CubeSphere"] = new() { ["en"] = "Cube Sphere", ["ja"] = "キューブスフィア", ["hi"] = "しかくいたま" },
        ["Subdivisions"] = new() { ["en"] = "Subdivisions", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },
        ["LongitudeSegments"] = new() { ["en"] = "Longitude Segments", ["ja"] = "経度分割数", ["hi"] = "たてのぶんかつすう" },
        ["LatitudeSegments"] = new() { ["en"] = "Latitude Segments", ["ja"] = "緯度分割数", ["hi"] = "よこのぶんかつすう" },

        // CubeSphereヘルプ
        ["CubeSphereHelp"] = new()
        {
            ["en"] = "Subdivides each face of a cube and projects it onto a sphere. Creates a uniform mesh without triangle concentration at the poles.",
            ["ja"] = "立方体の各面を細分化して球に投影します。極点に三角形が集中しない均一なメッシュになります。",
            ["hi"] = "しかくをこまかくして たまにうつす。きれいなメッシュになる。"
        },

        // ピボット
        ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
        ["Bottom"] = new() { ["en"] = "Bottom", ["ja"] = "下", ["hi"] = "した" },
        ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },

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
