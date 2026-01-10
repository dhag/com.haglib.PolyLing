// Assets/Editor/MeshCreators/NohMaskMeshCreatorWindow_Texts.cs
// FaceMeshメッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class NohMaskMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create FaceMesh", ["ja"] = "FaceMesh作成", ["hi"] = "かおメッシュをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "FaceMesh Parameters", ["ja"] = "FaceMeshパラメータ", ["hi"] = "せってい" },
        ["JsonFiles"] = new() { ["en"] = "JSON Files", ["ja"] = "JSONファイル", ["hi"] = "JSONファイル" },
        ["Transform"] = new() { ["en"] = "Transform", ["ja"] = "変形", ["hi"] = "へんけい" },
        ["Info"] = new() { ["en"] = "Info", ["ja"] = "情報", ["hi"] = "じょうほう" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド - 基本
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },

        // フィールド - JSON
        ["Landmarks"] = new() { ["en"] = "Landmarks JSON", ["ja"] = "ランドマークJSON", ["hi"] = "ランドマーク" },
        ["Triangles"] = new() { ["en"] = "Triangles JSON", ["ja"] = "三角形JSON", ["hi"] = "さんかくけい" },
        ["FaceIndex"] = new() { ["en"] = "Face Index", ["ja"] = "顔インデックス", ["hi"] = "かおばんごう" },

        // フィールド - 変形
        ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール", ["hi"] = "おおきさ" },
        ["DepthScale"] = new() { ["en"] = "Depth Scale", ["ja"] = "奥行きスケール", ["hi"] = "おくゆき" },
        ["FlipFaces"] = new() { ["en"] = "Flip Faces", ["ja"] = "面を反転", ["hi"] = "うらがえす" },

        // 情報
        ["LandmarkCount"] = new() { ["en"] = "Landmarks: {0}", ["ja"] = "ランドマーク数: {0}", ["hi"] = "てんすう: {0}" },
        ["TriangleCount"] = new() { ["en"] = "Triangles: {0}", ["ja"] = "三角形数: {0}", ["hi"] = "めんすう: {0}" },
        ["FaceCount"] = new() { ["en"] = "Faces Detected: {0}", ["ja"] = "検出顔数: {0}", ["hi"] = "かおすう: {0}" },
        ["VertsFaces"] = new() { ["en"] = "Vertices: {0}, Faces: {1}", ["ja"] = "頂点: {0}, 面: {1}", ["hi"] = "てん: {0}, めん: {1}" },

        // メッセージ
        ["SelectJsonFiles"] = new() { ["en"] = "Please select Landmarks and Triangles JSON files.", ["ja"] = "ランドマークと三角形のJSONファイルを選択してください。", ["hi"] = "JSONファイルをえらんでね" },
        ["NotSelected"] = new() { ["en"] = "(Not selected)", ["ja"] = "(未選択)", ["hi"] = "(みせんたく)" },
        ["SelectLandmarks"] = new() { ["en"] = "Select Landmarks JSON", ["ja"] = "ランドマークJSONを選択", ["hi"] = "ランドマークをえらぶ" },
        ["SelectTriangles"] = new() { ["en"] = "Select Triangles JSON", ["ja"] = "三角形JSONを選択", ["hi"] = "さんかくけいをえらぶ" },
        ["DragDropLandmarks"] = new() { ["en"] = "Drag & Drop Landmarks JSON here", ["ja"] = "ランドマークJSONをここにドロップ", ["hi"] = "ここにおとす" },
        ["DragDropTriangles"] = new() { ["en"] = "Drag & Drop Triangles JSON here", ["ja"] = "三角形JSONをここにドロップ", ["hi"] = "ここにおとす" },
    };

    // ================================================================
    // ローカライズヘルパー
    // ================================================================

    /// <summary>テキスト取得</summary>
    private static string T(string key) => L.GetFrom(Texts, key);

    /// <summary>フォーマット付きテキスト取得</summary>
    private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
}