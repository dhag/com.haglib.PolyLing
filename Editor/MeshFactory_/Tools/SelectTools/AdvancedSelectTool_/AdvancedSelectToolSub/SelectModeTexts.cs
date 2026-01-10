// Assets/Editor/MeshFactory/Tools/Selection/Modes/SelectModeTexts.cs
// 選択モード用共通ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 選択モード用共通ローカライズ辞書
    /// </summary>
    public static class SelectModeTexts
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // ================================================================
            // ShortestPathSelectMode
            // ================================================================
            ["ShortestPathHelp"] = new()
            {
                ["en"] = "Click two vertices for shortest path.\n• Vertex: path vertices\n• Edge: path edges\n• Face: adjacent faces",
                ["ja"] = "2つの頂点をクリックして最短経路を選択\n• 頂点: 経路上の頂点\n• エッジ: 経路上のエッジ\n• 面: 隣接する面",
                ["hi"] = "2つのてんをクリックしてさいたんきょり\n• てん: みちのてん\n• エッジ: みちのエッジ\n• めん: となりのめん"
            },
            ["FirstVertex"] = new() { ["en"] = "First vertex: {0}", ["ja"] = "始点: {0}", ["hi"] = "さいしょのてん: {0}" },
            ["ClearFirstPoint"] = new() { ["en"] = "Clear First Point", ["ja"] = "始点をクリア", ["hi"] = "さいしょのてんをけす" },

            // ================================================================
            // BeltSelectMode
            // ================================================================
            ["BeltHelp"] = new()
            {
                ["en"] = "Click edge to select quad belt.\n• Vertex: belt vertices\n• Edge: ladder rungs (horizontal)\n• Face: belt faces",
                ["ja"] = "エッジをクリックしてベルトを選択\n• 頂点: ベルト上の頂点\n• エッジ: 横方向エッジ\n• 面: ベルト上の面",
                ["hi"] = "エッジをクリックしてベルトをえらぶ\n• てん: ベルトのてん\n• エッジ: よこのエッジ\n• めん: ベルトのめん"
            },

            // ================================================================
            // EdgeLoopSelectMode
            // ================================================================
            ["EdgeLoopHelp"] = new()
            {
                ["en"] = "Click edge to select edge loop.\n• Vertex: loop vertices\n• Edge: loop edges\n• Face: adjacent faces",
                ["ja"] = "エッジをクリックしてエッジループを選択\n• 頂点: ループ上の頂点\n• エッジ: ループ上のエッジ\n• 面: 隣接する面",
                ["hi"] = "エッジをクリックしてエッジループをえらぶ\n• てん: ループのてん\n• エッジ: ループのエッジ\n• めん: となりのめん"
            },

            // ================================================================
            // ConnectedSelectMode
            // ================================================================
            ["ConnectedHelp"] = new()
            {
                ["en"] = "Click element to select all connected.\nOutput: All enabled modes (V/E/F/L)",
                ["ja"] = "要素をクリックして接続領域を選択\n出力: 有効な全モード（頂点/エッジ/面/線）",
                ["hi"] = "クリックしてつながってるのをぜんぶえらぶ\nしゅつりょく: ぜんぶのモード"
            },
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
