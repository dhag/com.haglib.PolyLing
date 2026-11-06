// PLProjectLayout.cs
// 作業フォルダの中で PolyLing が前提にする置き場所の名前（データフォルダ・サンドボックス）。
// 保存先: Application.persistentDataPath/PolyLing/ProjectLayout.json
//   - 書いた項目だけが既定値を上書きする（JsonUtility.FromJsonOverwrite）。無ければ既定値
//   - 読み込みは初回アクセス時に 1 回。テキスト編集後は Reload で読み直す
//   - 形の誤り・空の値・区切り文字を含む名前は警告を出して既定値に戻す
//
// 【MCP サーバとの関係】
//   PolyLingMcpServer にも同じ名前を持つ設定がある（exe のフォルダの ProjectLayout.json）。
//   使う場所が同じというだけで別の設定であり、互いに読み合わない。
//
// 【属性の説明文】
//   PLParam などの説明文には定数しか書けないので、説明文に {layout:項目名} を書き、
//   PLParamAttribute.Description の読み出しで Expand により置き換える。
//
// Editor/Player 両対応（#if UNITY_EDITOR 不使用、毎フレーム処理なし）。
// Runtime/Poly_Ling_Main/Core/Config/ に配置（RecentPaths と同じ場所）

using System;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Core
{
    public static class PLProjectLayout
    {
        [Serializable]
        private sealed class Data
        {
            /// <summary>作業フォルダ直下の、コマンドから直下に新規作成させないフォルダ。</summary>
            public string DataFolder = "データとツール";

            /// <summary>シナリオ・ランタイムの生成物の置き場（DataFolder の中の名前）。</summary>
            public string ScenarioSandboxFolder = "サンドボックス_シナリオ用";

            /// <summary>デバッグ作業の置き場（作業フォルダ直下の名前）。</summary>
            public string DebugSandboxFolder = "SandBox";
        }

        public const string FileName = "ProjectLayout.json";

        /// <summary>説明文の目印の頭（例 "{layout:DataFolder}"）。</summary>
        public const string MarkPrefix = "{layout:";

        private static Data _data;
        private static readonly object _lock = new object();

        private static string Dir      => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FilePath => Path.Combine(Dir, FileName);

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>作業フォルダ直下の、コマンドから直下に新規作成させないフォルダの名前。</summary>
        public static string DataFolder => Get().DataFolder;

        /// <summary>シナリオ・ランタイムの生成物の置き場（DataFolder の中の名前）。</summary>
        public static string ScenarioSandboxFolder => Get().ScenarioSandboxFolder;

        /// <summary>デバッグ作業の置き場（作業フォルダ直下の名前）。</summary>
        public static string DebugSandboxFolder => Get().DebugSandboxFolder;

        /// <summary>text の {layout:項目名} を今の値に置き換える。目印が無ければ text をそのまま返す。</summary>
        public static string Expand(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(MarkPrefix, StringComparison.Ordinal) < 0) return text;

            var d = Get();
            return text
                .Replace(MarkPrefix + nameof(Data.DataFolder) + "}", d.DataFolder)
                .Replace(MarkPrefix + nameof(Data.ScenarioSandboxFolder) + "}", d.ScenarioSandboxFolder)
                .Replace(MarkPrefix + nameof(Data.DebugSandboxFolder) + "}", d.DebugSandboxFolder);
        }

        /// <summary>ファイルを読み直す（テキスト編集後に反映したい場合）。</summary>
        public static void Reload()
        {
            lock (_lock) { _data = null; }
            Get();
        }

        // ================================================================
        // 読込
        // ================================================================

        /// <summary>メインスレッドで先に読んでおく（persistentDataPath をメインスレッドで引くため）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preload() => Get();

        private static Data Get()
        {
            var d = _data;
            if (d != null) return d;
            lock (_lock)
            {
                if (_data != null) return _data;
                var loaded = Load(out bool cache);
                if (cache) _data = loaded;
                return loaded;
            }
        }

        /// <param name="cache">
        /// false のときは今回だけ既定値を返す（メインスレッド以外で persistentDataPath を引けなかったとき）。
        /// 覚えてしまうと、後でメインスレッドから読めるようになっても既定値のままになる。
        /// </param>
        private static Data Load(out bool cache)
        {
            cache = true;
            var data = new Data();
            string path;
            try { path = FilePath; }
            catch (Exception)
            {
                cache = false;
                return data;
            }

            try
            {
                if (!File.Exists(path)) return data;
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PLProjectLayout] {path} を読めないので既定値を使います: {e.Message}");
                return new Data();
            }

            if (!IsFolderName(data.DataFolder) || !IsFolderName(data.ScenarioSandboxFolder) || !IsFolderName(data.DebugSandboxFolder))
            {
                Debug.LogWarning($"[PLProjectLayout] {path} の値はフォルダ名 1 つ（空でなく、区切り文字を含まない）で書いてください。既定値を使います");
                return new Data();
            }

            return data;
        }

        private static bool IsFolderName(string s)
            => !string.IsNullOrWhiteSpace(s)
               && s.IndexOfAny(new[] { '/', '\\', ':' }) < 0
               && s != "." && s != "..";
    }
}
