// Assets/Editor/MeshFactory/Tools/Creators/MeshCreatorTexts.cs
// メッシュ生成ウィンドウ基底クラス用の共通ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools.Creators
{
    /// <summary>
    /// メッシュ生成ウィンドウ共通のローカライズ辞書
    /// </summary>
    public static class MeshCreatorTexts
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // セクション
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション", ["hi"] = "オプション" },
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

            // AutoMerge
            ["AutoMergeVertices"] = new() { ["en"] = "Auto Merge Vertices", ["ja"] = "頂点を自動結合", ["hi"] = "てんをじどうけつごう" },
            ["Threshold"] = new() { ["en"] = "Threshold:", ["ja"] = "しきい値:", ["hi"] = "しきいち:" },

            // 情報
            ["VertsFaces"] = new() { ["en"] = "Vertices: {0}, Faces: {1}", ["ja"] = "頂点: {0}, 面: {1}", ["hi"] = "てん: {0}, めん: {1}" },

            // ボタン
            ["Create"] = new() { ["en"] = "Create", ["ja"] = "作成", ["hi"] = "つくる" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },
            ["Undo"] = new() { ["en"] = "Undo", ["ja"] = "元に戻す", ["hi"] = "もどす" },
            ["Redo"] = new() { ["en"] = "Redo", ["ja"] = "やり直し", ["hi"] = "やりなおす" },

            // ログメッセージ
            ["AutoMergedVertices"] = new() { ["en"] = "Auto-merged {0} vertices", ["ja"] = "{0}個の頂点を自動結合しました", ["hi"] = "{0}このてんをけつごう" },
            ["CreatedMesh"] = new() { ["en"] = "Created mesh: {0} (V:{1}, F:{2})", ["ja"] = "メッシュ作成: {0} (頂点:{1}, 面:{2})", ["hi"] = "メッシュつくった: {0} (てん:{1}, めん:{2})" },
            ["FailedToGenerate"] = new() { ["en"] = "Failed to generate mesh data", ["ja"] = "メッシュデータの生成に失敗しました", ["hi"] = "メッシュがつくれません" },
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
