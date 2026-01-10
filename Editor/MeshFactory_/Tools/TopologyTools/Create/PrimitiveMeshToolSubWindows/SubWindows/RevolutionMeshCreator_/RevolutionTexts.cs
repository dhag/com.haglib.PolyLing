// Assets/Editor/MeshCreators/Revolution/RevolutionTexts.cs
// 回転体メッシュ生成ウィンドウ - 共通ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Revolution
{
    /// <summary>
    /// 回転体メッシュ作成ウィンドウ用の共通ローカライズ辞書
    /// </summary>
    public static class RevolutionTexts
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // ================================================================
            // ウィンドウ・セクション
            // ================================================================
            ["WindowTitle"] = new() { ["en"] = "Create Revolution Mesh", ["ja"] = "回転体メッシュ作成", ["hi"] = "まわしてつくる" },
            ["Parameters"] = new() { ["en"] = "Revolution Parameters", ["ja"] = "回転体パラメータ", ["hi"] = "かいてんたいのせってい" },
            ["Preview3D"] = new() { ["en"] = "3D Preview", ["ja"] = "3Dプレビュー", ["hi"] = "3Dプレビュー" },
            ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
            ["Preset"] = new() { ["en"] = "Preset", ["ja"] = "プリセット", ["hi"] = "プリセット" },

            // ================================================================
            // 基本パラメータ
            // ================================================================
            ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
            ["RadialSegments"] = new() { ["en"] = "Radial Segments", ["ja"] = "周方向分割数", ["hi"] = "まわりのぶんかつすう" },
            ["CloseTop"] = new() { ["en"] = "Close Top", ["ja"] = "上部を閉じる", ["hi"] = "うえをとじる" },
            ["CloseBottom"] = new() { ["en"] = "Close Bottom", ["ja"] = "下部を閉じる", ["hi"] = "したをとじる" },
            ["CloseLoop"] = new() { ["en"] = "Close Loop", ["ja"] = "ループを閉じる", ["hi"] = "わっかをとじる" },
            ["Spiral"] = new() { ["en"] = "Spiral", ["ja"] = "らせん", ["hi"] = "らせん" },
            ["Turns"] = new() { ["en"] = "Turns", ["ja"] = "回転数", ["hi"] = "かいてんすう" },
            ["Pitch"] = new() { ["en"] = "Pitch", ["ja"] = "ピッチ", ["hi"] = "ピッチ" },
            ["FlipY"] = new() { ["en"] = "Flip Y (180°)", ["ja"] = "Y反転 (180°)", ["hi"] = "Yはんてん" },
            ["FlipZ"] = new() { ["en"] = "Flip Z (180°)", ["ja"] = "Z反転 (180°)", ["hi"] = "Zはんてん" },

            // ================================================================
            // ピボット
            // ================================================================
            ["PivotY"] = new() { ["en"] = "Y", ["ja"] = "Y", ["hi"] = "Y" },
            ["Bottom"] = new() { ["en"] = "Bottom", ["ja"] = "下", ["hi"] = "した" },
            ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },
            ["Top"] = new() { ["en"] = "Top", ["ja"] = "上", ["hi"] = "うえ" },

            // ================================================================
            // ドーナツ設定
            // ================================================================
            ["DonutSettings"] = new() { ["en"] = "Donut Settings", ["ja"] = "ドーナツ設定", ["hi"] = "ドーナツのせってい" },
            ["MajorRadius"] = new() { ["en"] = "Major Radius", ["ja"] = "外径", ["hi"] = "そとのおおきさ" },
            ["MinorRadius"] = new() { ["en"] = "Minor Radius", ["ja"] = "断面半径", ["hi"] = "だんめんのはんけい" },
            ["TubeSegments"] = new() { ["en"] = "Tube Segments", ["ja"] = "チューブ分割数", ["hi"] = "チューブのぶんかつすう" },

            // ================================================================
            // 角丸パイプ設定
            // ================================================================
            ["RoundedPipeSettings"] = new() { ["en"] = "Rounded Pipe Settings", ["ja"] = "角丸パイプ設定", ["hi"] = "まるいパイプのせってい" },
            ["InnerRadius"] = new() { ["en"] = "Inner Radius", ["ja"] = "内径", ["hi"] = "うちがわのおおきさ" },
            ["OuterRadius"] = new() { ["en"] = "Outer Radius", ["ja"] = "外径", ["hi"] = "そとがわのおおきさ" },
            ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
            ["InnerCorner"] = new() { ["en"] = "Inner Corner", ["ja"] = "内側角丸め", ["hi"] = "うちがわのかどまるめ" },
            ["OuterCorner"] = new() { ["en"] = "Outer Corner", ["ja"] = "外側角丸め", ["hi"] = "そとがわのかどまるめ" },
            ["Radius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
            ["Segments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },

            // ================================================================
            // プロファイルエディタ
            // ================================================================
            ["ProfileEditor"] = new() { ["en"] = "Profile Editor (XY Plane)", ["ja"] = "断面エディタ (XY平面)", ["hi"] = "だんめんエディタ" },
            ["AddPoint"] = new() { ["en"] = "Add Point", ["ja"] = "点を追加", ["hi"] = "てんをついか" },
            ["RemovePoint"] = new() { ["en"] = "Remove Point", ["ja"] = "点を削除", ["hi"] = "てんをさくじょ" },
            ["Reset"] = new() { ["en"] = "Reset", ["ja"] = "リセット", ["hi"] = "りせっと" },
            ["LoadCSV"] = new() { ["en"] = "Load CSV...", ["ja"] = "CSVを読み込み...", ["hi"] = "CSVをよみこむ" },
            ["SaveCSV"] = new() { ["en"] = "Save CSV...", ["ja"] = "CSVを保存...", ["hi"] = "CSVをほぞん" },
            ["PointN"] = new() { ["en"] = "Point {0}", ["ja"] = "点 {0}", ["hi"] = "てん {0}" },
            ["RadiusX"] = new() { ["en"] = "Radius (X)", ["ja"] = "半径 (X)", ["hi"] = "はんけい (X)" },
            ["HeightY"] = new() { ["en"] = "Height (Y)", ["ja"] = "高さ (Y)", ["hi"] = "たかさ (Y)" },
            ["ProfileHelp"] = new() { ["en"] = "Click to select, drag to move", ["ja"] = "クリックで選択、ドラッグで移動", ["hi"] = "クリックでえらぶ、ドラッグでうごかす" },

            // Undo説明
            ["UndoAddPoint"] = new() { ["en"] = "Add Profile Point", ["ja"] = "プロファイル点を追加", ["hi"] = "てんをついか" },
            ["UndoRemovePoint"] = new() { ["en"] = "Remove Profile Point", ["ja"] = "プロファイル点を削除", ["hi"] = "てんをさくじょ" },
            ["UndoResetProfile"] = new() { ["en"] = "Reset Profile", ["ja"] = "プロファイルをリセット", ["hi"] = "りせっと" },
            ["UndoMovePoint"] = new() { ["en"] = "Move Profile Point", ["ja"] = "プロファイル点を移動", ["hi"] = "てんをいどう" },
            ["UndoLoadCSV"] = new() { ["en"] = "Load Profile CSV", ["ja"] = "プロファイルCSVを読み込み", ["hi"] = "CSVをよみこむ" },
            ["UndoChangePreset"] = new() { ["en"] = "Change Preset", ["ja"] = "プリセットを変更", ["hi"] = "プリセットをへんこう" },

            // ================================================================
            // CSV関連
            // ================================================================
            ["CSVLoadTitle"] = new() { ["en"] = "Load Profile CSV", ["ja"] = "プロファイルCSVを読み込み", ["hi"] = "CSVをよみこむ" },
            ["CSVSaveTitle"] = new() { ["en"] = "Save Profile CSV", ["ja"] = "プロファイルCSVを保存", ["hi"] = "CSVをほぞん" },
            ["Error"] = new() { ["en"] = "Error", ["ja"] = "エラー", ["hi"] = "エラー" },
            ["OK"] = new() { ["en"] = "OK", ["ja"] = "OK", ["hi"] = "OK" },
            ["CSVLoadError"] = new() { ["en"] = "CSV load error: {0}", ["ja"] = "CSV読み込みエラー: {0}", ["hi"] = "CSVよみこみエラー: {0}" },
            ["CSVSaveError"] = new() { ["en"] = "CSV save error: {0}", ["ja"] = "CSV保存エラー: {0}", ["hi"] = "CSVほぞんエラー: {0}" },
            ["CSVNeedPoints"] = new() { ["en"] = "At least 2 valid profile points required", ["ja"] = "有効なプロファイル点が2つ以上必要です", ["hi"] = "てんが2こいじょうひつよう" },
            ["Cancelled"] = new() { ["en"] = "Cancelled", ["ja"] = "キャンセルされました", ["hi"] = "キャンセル" },

            // ================================================================
            // 情報・ボタン
            // ================================================================
            ["VertsFaces"] = new() { ["en"] = "Vertices: {0}, Faces: {1}", ["ja"] = "頂点: {0}, 面: {1}", ["hi"] = "てん: {0}, めん: {1}" },
            ["Create"] = new() { ["en"] = "Create", ["ja"] = "作成", ["hi"] = "つくる" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },
            ["Undo"] = new() { ["en"] = "Undo", ["ja"] = "元に戻す", ["hi"] = "もどす" },
            ["Redo"] = new() { ["en"] = "Redo", ["ja"] = "やり直し", ["hi"] = "やりなおす" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        public static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>フォーマット付きテキスト取得</summary>
        public static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}
