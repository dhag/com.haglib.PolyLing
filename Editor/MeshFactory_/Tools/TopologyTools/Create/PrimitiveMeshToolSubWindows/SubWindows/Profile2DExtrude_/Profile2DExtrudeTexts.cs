// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DExtrudeTexts.cs
// 2D閉曲線押し出しメッシュ生成ウィンドウ - 共通ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Profile2DExtrude
{
    /// <summary>
    /// 2D押し出しメッシュ作成ウィンドウ用の共通ローカライズ辞書
    /// </summary>
    public static class Profile2DExtrudeTexts
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // ================================================================
            // ウィンドウ・セクション
            // ================================================================
            ["WindowTitle"] = new() { ["en"] = "2D Profile Extrude", ["ja"] = "2Dプロファイル押し出し", ["hi"] = "2Dおしだし" },
            ["Parameters"] = new() { ["en"] = "2D Profile Extrude Parameters", ["ja"] = "2D押し出しパラメータ", ["hi"] = "2Dおしだしのせってい" },
            ["Loops"] = new() { ["en"] = "Loops", ["ja"] = "ループ", ["hi"] = "ループ" },
            ["Preview3D"] = new() { ["en"] = "3D Preview (Right-drag to rotate)", ["ja"] = "3Dプレビュー（右ドラッグで回転）", ["hi"] = "3Dプレビュー（みぎドラッグでかいてん）" },
            ["Editor2D"] = new() { ["en"] = "2D Editor", ["ja"] = "2Dエディタ", ["hi"] = "2Dエディタ" },
            ["Editor2DHelp"] = new() { ["en"] = "Click edge to insert, Drag point to move", ["ja"] = "エッジをクリックで挿入、ドラッグで移動", ["hi"] = "せんをクリックでついか、ドラッグでいどう" },
            ["TransformLoop"] = new() { ["en"] = "Transform Selected Loop", ["ja"] = "選択ループの変形", ["hi"] = "えらんだループをへんけい" },

            // ================================================================
            // 基本パラメータ
            // ================================================================
            ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
            ["CSVFile"] = new() { ["en"] = "CSV File", ["ja"] = "CSVファイル", ["hi"] = "CSVファイル" },
            ["NoFile"] = new() { ["en"] = "<none>", ["ja"] = "<なし>", ["hi"] = "<なし>" },
            ["LoadCSV"] = new() { ["en"] = "Load CSV...", ["ja"] = "CSV読み込み...", ["hi"] = "CSVよみこむ" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール", ["hi"] = "おおきさ" },
            ["Offset"] = new() { ["en"] = "Offset", ["ja"] = "オフセット", ["hi"] = "ずらし" },
            ["FlipY"] = new() { ["en"] = "Flip Y", ["ja"] = "Y反転", ["hi"] = "Yはんてん" },
            ["Thickness"] = new() { ["en"] = "Thickness", ["ja"] = "厚み", ["hi"] = "あつみ" },

            // ================================================================
            // エッジ処理
            // ================================================================
            ["EdgeSettings"] = new() { ["en"] = "Edge (0=None, 1=Bevel, 2+=Round)", ["ja"] = "角処理 (0=なし, 1=ベベル, 2+=ラウンド)", ["hi"] = "かどしょり (0=なし, 1=ななめ, 2+=まるめ)" },
            ["FrontSegments"] = new() { ["en"] = "Front Segments", ["ja"] = "前面分割数", ["hi"] = "まえのぶんかつすう" },
            ["BackSegments"] = new() { ["en"] = "Back Segments", ["ja"] = "背面分割数", ["hi"] = "うしろのぶんかつすう" },
            ["Size"] = new() { ["en"] = "Size", ["ja"] = "サイズ", ["hi"] = "サイズ" },
            ["OutwardKeepFaceSize"] = new() { ["en"] = "Outward (Keep Face Size)", ["ja"] = "外向き（面サイズ維持）", ["hi"] = "そとむき（めんをそのまま）" },

            // ================================================================
            // ループ管理
            // ================================================================
            ["NewLoopSides"] = new() { ["en"] = "New Loop Sides:", ["ja"] = "新規ループの辺数:", ["hi"] = "あたらしいループのへんすう:" },
            ["AddNgon"] = new() { ["en"] = "+ Add {0}-gon", ["ja"] = "+ {0}角形を追加", ["hi"] = "+ {0}かくけいをついか" },
            ["Outer"] = new() { ["en"] = "Outer {0}", ["ja"] = "外側 {0}", ["hi"] = "そとがわ {0}" },
            ["Hole"] = new() { ["en"] = "Hole {0}", ["ja"] = "穴 {0}", ["hi"] = "あな {0}" },
            ["PointCount"] = new() { ["en"] = "({0} pts)", ["ja"] = "({0}点)", ["hi"] = "({0}てん)" },
            ["IsHole"] = new() { ["en"] = "Hole", ["ja"] = "穴", ["hi"] = "あな" },
            ["SaveAsCSV"] = new() { ["en"] = "Save as CSV...", ["ja"] = "CSVとして保存...", ["hi"] = "CSVでほぞん" },

            // ================================================================
            // ループ変形
            // ================================================================
            ["ScaleLabel"] = new() { ["en"] = "Scale:", ["ja"] = "拡縮:", ["hi"] = "かくしゅく:" },
            ["MoveLabel"] = new() { ["en"] = "Move:", ["ja"] = "移動:", ["hi"] = "いどう:" },
            ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },

            // ================================================================
            // 2Dエディタ
            // ================================================================
            ["RemoveSelectedPoint"] = new() { ["en"] = "- Remove Selected Point", ["ja"] = "- 選択点を削除", ["hi"] = "- えらんだてんをさくじょ" },
            ["Del"] = new() { ["en"] = "Del", ["ja"] = "削除", ["hi"] = "さくじょ" },
            ["PointN"] = new() { ["en"] = "Point {0}", ["ja"] = "点 {0}", ["hi"] = "てん {0}" },
            ["PtN"] = new() { ["en"] = "Pt{0}:", ["ja"] = "点{0}:", ["hi"] = "てん{0}:" },

            // ================================================================
            // ボタン
            // ================================================================
            ["ReloadCSV"] = new() { ["en"] = "Reload CSV", ["ja"] = "CSV再読込", ["hi"] = "CSVさいよみこみ" },
            ["ResetToDefault"] = new() { ["en"] = "Reset to Default", ["ja"] = "デフォルトに戻す", ["hi"] = "もとにもどす" },
            ["Create"] = new() { ["en"] = "Create", ["ja"] = "作成", ["hi"] = "つくる" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },

            // ================================================================
            // 情報・メッセージ
            // ================================================================
            ["MeshInfo"] = new() { ["en"] = "Vertices: {0}, Faces: {1} (Tri:{2}, Quad:{3}), Loops: {4}", ["ja"] = "頂点: {0}, 面: {1} (三角:{2}, 四角:{3}), ループ: {4}", ["hi"] = "てん: {0}, めん: {1} (さんかく:{2}, しかく:{3}), ループ: {4}" },
            ["MeshGenerationFailed"] = new() { ["en"] = "Mesh generation failed. Please check input data.", ["ja"] = "メッシュ生成に失敗しました。入力データを確認してください。", ["hi"] = "メッシュがつくれません。データをかくにんしてください。" },

            // ================================================================
            // CSV関連
            // ================================================================
            ["CSVLoadTitle"] = new() { ["en"] = "Load Profile CSV", ["ja"] = "プロファイルCSVを読み込み", ["hi"] = "CSVをよみこむ" },
            ["CSVSaveTitle"] = new() { ["en"] = "Save Loops as CSV", ["ja"] = "ループをCSVとして保存", ["hi"] = "ループをCSVでほぞん" },
            ["Error"] = new() { ["en"] = "Error", ["ja"] = "エラー", ["hi"] = "エラー" },
            ["OK"] = new() { ["en"] = "OK", ["ja"] = "OK", ["hi"] = "OK" },
            ["CSVLoadError"] = new() { ["en"] = "Failed to load CSV: {0}", ["ja"] = "CSV読み込みエラー: {0}", ["hi"] = "CSVよみこみエラー: {0}" },
            ["CSVSaveError"] = new() { ["en"] = "Failed to save: {0}", ["ja"] = "保存エラー: {0}", ["hi"] = "ほぞんエラー: {0}" },
            ["Cancelled"] = new() { ["en"] = "Cancelled", ["ja"] = "キャンセルされました", ["hi"] = "キャンセル" },
            ["NoValidLoops"] = new() { ["en"] = "No valid loops found in CSV", ["ja"] = "有効なループがCSV内に見つかりません", ["hi"] = "ループがみつかりません" },
            ["CSVPathEmpty"] = new() { ["en"] = "CSV path is null or empty", ["ja"] = "CSVパスが空です", ["hi"] = "CSVパスがからです" },
            ["CSVNotFound"] = new() { ["en"] = "CSV file not found: {0}", ["ja"] = "CSVファイルが見つかりません: {0}", ["hi"] = "CSVファイルがみつかりません: {0}" },

            // ================================================================
            // Undo説明
            // ================================================================
            ["UndoAddLoop"] = new() { ["en"] = "Add Loop", ["ja"] = "ループを追加", ["hi"] = "ループをついか" },
            ["UndoDeleteLoop"] = new() { ["en"] = "Delete Loop", ["ja"] = "ループを削除", ["hi"] = "ループをさくじょ" },
            ["UndoToggleHole"] = new() { ["en"] = "Toggle Hole", ["ja"] = "穴フラグ切替", ["hi"] = "あなフラグきりかえ" },
            ["UndoLoadCSV"] = new() { ["en"] = "Load CSV", ["ja"] = "CSV読み込み", ["hi"] = "CSVよみこみ" },
            ["UndoReloadCSV"] = new() { ["en"] = "Reload CSV", ["ja"] = "CSV再読込", ["hi"] = "CSVさいよみこみ" },
            ["UndoResetDefault"] = new() { ["en"] = "Reset to Default", ["ja"] = "デフォルトに戻す", ["hi"] = "もとにもどす" },
            ["UndoScaleLoop"] = new() { ["en"] = "Scale Loop x{0}", ["ja"] = "ループを{0}倍", ["hi"] = "ループを{0}ばい" },
            ["UndoMoveLoop"] = new() { ["en"] = "Move Loop", ["ja"] = "ループを移動", ["hi"] = "ループをいどう" },
            ["UndoCenterLoop"] = new() { ["en"] = "Center Loop", ["ja"] = "ループを中央に", ["hi"] = "ループをまんなかに" },
            ["UndoInsertPoint"] = new() { ["en"] = "Insert Point", ["ja"] = "点を挿入", ["hi"] = "てんをそうにゅう" },
            ["UndoRemovePoint"] = new() { ["en"] = "Remove Point", ["ja"] = "点を削除", ["hi"] = "てんをさくじょ" },
            ["UndoEditPoint"] = new() { ["en"] = "Edit Point", ["ja"] = "点を編集", ["hi"] = "てんをへんしゅう" },
            ["UndoMovePoint"] = new() { ["en"] = "Move Profile Point", ["ja"] = "プロファイル点を移動", ["hi"] = "てんをいどう" },
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
