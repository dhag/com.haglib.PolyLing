// ScenarioLibrary.cs
// 手本のオブジェクトグループ（手順の知識）の置き場。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜモデルの外に置くか】
//   ModelContext.ObjectGroups に入れたものは、モデルと一緒に保存され、
//   Undo で差し替えられ（MeshListRecords）、参照の生存を検査され
//   （ModelInvariantChecker.CheckObjectGroupReferences）、参照先を全て失うと
//   消される（ObjectGroupOps.PurgeMissing）。
//   手本は実体を持たないので、同じリストへ入れるとこの 3 か所すべてに
//   「手本は除く」という例外が要る。置き場を分ければ例外は 1 つも要らない。
//
// 【なぜプロジェクトの外に置くか】
//   手順の知識はプロジェクトをまたいで貯まる。ProjectContext.WorkAxes は
//   プロジェクトファイルと同じフォルダに置く形だが、こちらは
//   DisplaySettings / ParameterLimits と同じく persistentDataPath へ置く。
//
// 【型は分けない】
//   中身は ObjectGroup そのもの。実体付きとの違いは置き場と、
//   参照（MeshRefIds / OutputObjectIds）が埋まっているかどうかだけ。
//   型を分けるとステップ列の器が 2 つになり、ObjectGroupOps.CaptureStep の
//   出力先も 2 つになる。
//
// 【原本を渡さない】
//   Get は複製を返す。呼ぶ側が引数を書き換えても原本は変わらない。
//   書き戻したいときは Register で明示的に登録する。
//
// 【保存先】
//   <persistentDataPath>/PolyLing/scenarios.csv
//   形式は objectgroups.csv と同じ（ObjectGroupCsv が正典）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Serialization;

namespace Poly_Ling.Data
{
    /// <summary>手本のオブジェクトグループの置き場。</summary>
    public static class ScenarioLibrary
    {
        private const string LogTag  = "[ScenarioLibrary]";
        private const string Header  = "PolyLing_Scenarios";

        private static List<ObjectGroup> _items;
        private static readonly object _lock = new object();

        private static string Dir      => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FileName => Path.Combine(Dir, "scenarios.csv");

        /// <summary>保存先の絶対パス（表示・手動バックアップ用）。</summary>
        public static string StorePath => FileName;

        // ================================================================
        // 読み書き
        // ================================================================

        /// <summary>まだ読んでいなければ読む。</summary>
        private static void EnsureLoaded()
        {
            if (_items != null) return;
            lock (_lock)
            {
                if (_items != null) return;
                _items = ReadFile();
            }
        }

        /// <summary>ファイルから読み直す。手で編集したあとに呼ぶ。</summary>
        public static void Reload()
        {
            lock (_lock) { _items = ReadFile(); }
        }

        /// <summary>今の中身をファイルへ書く。</summary>
        public static void Save()
        {
            EnsureLoaded();
            lock (_lock) { WriteFile(_items); }
        }

        private static List<ObjectGroup> ReadFile()
        {
            try
            {
                if (!File.Exists(FileName)) return new List<ObjectGroup>();

                var list = ObjectGroupCsv.Parse(File.ReadAllLines(FileName, Encoding.UTF8));

                // 名前が重なると Find がどちらを返すか決まらない。後ろを落とす。
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var g = list[i];
                    if (g == null || string.IsNullOrEmpty(g.Name) || !seen.Add(g.Name))
                    {
                        Debug.LogWarning($"{LogTag} 名前の無い／重なった手本を読み飛ばしました: 位置 {i}");
                        list.RemoveAt(i);
                    }
                }
                return list;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 読み込みに失敗しました: {e.Message}");
                return new List<ObjectGroup>();
            }
        }

        private static void WriteFile(List<ObjectGroup> list)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FileName, ObjectGroupCsv.Build(list, Header), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 保存に失敗しました: {e.Message}");
            }
        }

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>登録されている手本の数。</summary>
        public static int Count
        {
            get { EnsureLoaded(); return _items.Count; }
        }

        /// <summary>登録順の名前。</summary>
        public static List<string> Names()
        {
            EnsureLoaded();
            var names = new List<string>(_items.Count);
            foreach (var g in _items) names.Add(g.Name);
            return names;
        }

        /// <summary>名前で引いて複製を返す。無ければ null。</summary>
        public static ObjectGroup Get(string name)
        {
            var found = FindInternal(name);
            return found?.Clone();
        }

        /// <summary>この名前の手本があるか。</summary>
        public static bool Contains(string name) => FindInternal(name) != null;

        /// <summary>全件の複製を返す。</summary>
        public static List<ObjectGroup> GetAll()
        {
            EnsureLoaded();
            var list = new List<ObjectGroup>(_items.Count);
            foreach (var g in _items) list.Add(g.Clone());
            return list;
        }

        private static ObjectGroup FindInternal(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Name, name, StringComparison.Ordinal)) return _items[i];
            return null;
        }

        // ================================================================
        // 登録・削除
        // ================================================================

        /// <summary>
        /// 手本を登録する。渡されたものの複製を入れるので、呼ぶ側が
        /// あとで書き換えても登録済みの中身は変わらない。
        /// 登録できたらファイルへ書く。
        /// </summary>
        /// <param name="overwrite">同名があるとき差し替えるか。false なら失敗。</param>
        public static bool Register(ObjectGroup group, bool overwrite, out string error)
        {
            error = null;

            if (group == null)                     { error = "手本がありません";       return false; }
            if (string.IsNullOrEmpty(group.Name))  { error = "手本の名前が空です";     return false; }
            if (!group.IsValid)                    { error = "実行する段に action が入っていません"; return false; }

            EnsureLoaded();
            lock (_lock)
            {
                var copy = group.Clone();
                copy.EnsureElementIds();

                int at = -1;
                for (int i = 0; i < _items.Count; i++)
                    if (string.Equals(_items[i].Name, copy.Name, StringComparison.Ordinal)) { at = i; break; }

                if (at >= 0)
                {
                    if (!overwrite) { error = $"同じ名前の手本が既にあります: {copy.Name}"; return false; }
                    _items[at] = copy;
                }
                else
                {
                    _items.Add(copy);
                }

                WriteFile(_items);
            }
            return true;
        }

        /// <summary>手本を消す。消したらファイルへ書く。</summary>
        public static bool Remove(string name)
        {
            EnsureLoaded();
            lock (_lock)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!string.Equals(_items[i].Name, name, StringComparison.Ordinal)) continue;
                    _items.RemoveAt(i);
                    WriteFile(_items);
                    return true;
                }
            }
            return false;
        }
    }
}
