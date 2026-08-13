// PrimitiveShapeMemory.cs
// 図形生成パネルで最後に選んだ図形をカテゴリ別に記憶する。
// 保存先: Application.persistentDataPath/PolyLing/PrimitiveShapeMemory.json
//   { "Entries": [ { "Panel": "Primitive", "Basic": "Sphere", "Advanced": "Pipe" } ] }
// 図形は列挙値の「名前」で保存する（ShapeKind の並び替えで値がずれても壊れないため）。
// 読込は初回1回だけの静的キャッシュ。書込は値が変化したときのみ。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Player
{
    using ShapeKind     = Poly_Ling.Player.PlayerPrimitiveMeshSubPanel.ShapeKind;
    using ShapeCategory = Poly_Ling.Player.PlayerPrimitiveMeshSubPanel.ShapeCategory;

    public static class PrimitiveShapeMemory
    {
        // ================================================================
        // データ型
        // ================================================================

        [Serializable]
        public class Entry
        {
            public string Panel;      // パネル識別子（"Primitive" / "LivePrimitive"）
            public string Basic;      // 基本図形カテゴリで最後に選んだ ShapeKind 名
            public string Advanced;   // 高度な図形カテゴリで最後に選んだ ShapeKind 名
        }

        [Serializable]
        private class MemoryFile
        {
            public List<Entry> Entries = new List<Entry>();
        }

        // ================================================================
        // パス
        // ================================================================

        private static string Dir      => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FilePath => Path.Combine(Dir, "PrimitiveShapeMemory.json");

        // ================================================================
        // 内部状態（初回1回だけ読み込む）
        // ================================================================

        private static List<Entry> _entries;
        private static readonly object _lock = new object();

        private static void EnsureLoaded()
        {
            if (_entries != null) return;
            lock (_lock)
            {
                if (_entries != null) return;
                _entries = ReadFile();
            }
        }

        private static List<Entry> ReadFile()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Entry>();
                var file = JsonUtility.FromJson<MemoryFile>(File.ReadAllText(FilePath));
                return file?.Entries ?? new List<Entry>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PrimitiveShapeMemory] 読込失敗: {e.Message}");
                return new List<Entry>();
            }
        }

        private static Entry Find(string panelKey)
        {
            foreach (var e in _entries)
                if (e != null && e.Panel == panelKey) return e;
            return null;
        }

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>
        /// 記憶している図形を返す。未登録・名前が解析できない場合は null。
        /// </summary>
        public static ShapeKind? Get(string panelKey, ShapeCategory category)
        {
            if (string.IsNullOrEmpty(panelKey)) return null;
            EnsureLoaded();

            var entry = Find(panelKey);
            if (entry == null) return null;

            string name = category == ShapeCategory.Advanced ? entry.Advanced : entry.Basic;
            if (string.IsNullOrEmpty(name)) return null;

            if (!Enum.TryParse(name, out ShapeKind kind)) return null;
            if (!Enum.IsDefined(typeof(ShapeKind), kind)) return null;
            return kind;
        }

        /// <summary>
        /// 図形を記憶してファイルへ書き戻す。値が変わらない場合は書き込まない。
        /// </summary>
        public static void Set(string panelKey, ShapeCategory category, ShapeKind kind)
        {
            if (string.IsNullOrEmpty(panelKey)) return;
            EnsureLoaded();

            lock (_lock)
            {
                var entry = Find(panelKey);
                if (entry == null)
                {
                    entry = new Entry { Panel = panelKey };
                    _entries.Add(entry);
                }

                string name = kind.ToString();
                if (category == ShapeCategory.Advanced)
                {
                    if (entry.Advanced == name) return;
                    entry.Advanced = name;
                }
                else
                {
                    if (entry.Basic == name) return;
                    entry.Basic = name;
                }

                Write();
            }
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var file = new MemoryFile { Entries = _entries };
                File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PrimitiveShapeMemory] 書込失敗: {e.Message}");
            }
        }
    }
}
