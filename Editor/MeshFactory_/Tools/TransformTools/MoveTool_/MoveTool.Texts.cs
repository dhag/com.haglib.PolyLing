// Tools/MoveTool.Texts.cs
// 頂点移動ツール - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    public partial class MoveTool
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // タイトル
            ["Title"] = new() { ["en"] = "Move Tool", ["ja"] = "移動ツール", ["hi"] = "うごかすどうぐ" },

            // マグネット
            ["Magnet"] = new() { ["en"] = "Magnet", ["ja"] = "マグネット", ["hi"] = "じしゃく" },
            ["Enable"] = new() { ["en"] = "Enable", ["ja"] = "有効", ["hi"] = "つかう" },
            ["Radius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
            ["Falloff"] = new() { ["en"] = "Falloff", ["ja"] = "減衰", ["hi"] = "よわまりかた" },

            // ギズモ
            ["Gizmo"] = new() { ["en"] = "Gizmo", ["ja"] = "ギズモ", ["hi"] = "ギズモ" },
            ["OffsetX"] = new() { ["en"] = "Offset X", ["ja"] = "オフセット X", ["hi"] = "ずれ X" },
            ["OffsetY"] = new() { ["en"] = "Offset Y", ["ja"] = "オフセット Y", ["hi"] = "ずれ Y" },

            // 情報表示
            ["TargetVertices"] = new() { ["en"] = "Target: {0} vertices", ["ja"] = "移動対象: {0} 頂点", ["hi"] = "うごかすてん: {0}こ" },
        };

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        /// <summary>テキスト取得</summary>
        private static string T(string key) => L.GetFrom(Texts, key);

        /// <summary>フォーマット付きテキスト取得</summary>
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}