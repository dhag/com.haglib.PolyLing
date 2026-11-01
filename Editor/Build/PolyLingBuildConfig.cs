// Editor/Build/PolyLingBuildConfig.cs
// ============================================================
// ビルド構成（PolyLing_クライアント種別とビルド構成計画.md 段 2）
// ============================================================
//
// 【ファイルの場所と優先順】
//   1. Assets/PolyLingBuild/ビルド構成.json（プロジェクトごとの構成。あればこちら）
//   2. Packages/com.haglib.polyling/Editor/Build/既定ビルド構成.json（パッケージ既定）
//   Assets 側が消されても既定に戻る。MCP サーバ（段 4）も同じ順で同じファイルを読む。
//
// 【形】（キーは小文字始まり。JsonUtility と System.Text.Json の両方で読める平らな形）
//   {
//     "version": 1,
//     "editor": { "name": "editor" },
//     "builds": [
//       { "name": "player", "scenes": [], "exe": "ビルド用/PolyLing.exe", "role": "server", "port": 8772, "user": "" }
//     ]
//   }
//   editor  … Editor の受付。name は MCP から呼ぶときの名前（段 4）。
//             ポートは PolyLingEditorControlServer.Port（8770）固定で、port は書けない（書くと拒否。段 6）。
//             Editor の受付は [InitializeOnLoad] で立ち、unity_play・unity_refresh・unity_prefab_* などは
//             構成を読まずに 8770 へ送る。構成から決めると、構成が検査に落ちている間 Editor へ届かなくなるため。
//   builds  … ビルド構成の並び。build_all はこの順にビルドする。
//     name   … 構成名。editor の name も含めて重複不可。
//     scenes … Assets/... .unity の並び。空なら Build Settings の有効なシーン。
//     exe    … 出力する exe。プロジェクト直下からの相対。構成どうしで重複不可。
//     role   … リモート役割。server / host / collaborator / separate / none。
//              起動時に --polyling-remote=<role> で exe へ渡す（PolyLingLaunchArgs.cs。渡すのは段 4 の player_launch）。
//              本体ビューアのシーンで使えるのは server / separate / none。host / collaborator は
//              リストクライアント（ListClientBase）のシーンで使う。
//     port   … MCP 受付ポート。Editor の 8770 とも他の構成とも重複不可。--polyling-control-port=N で exe へ渡す。
//              受付を立てるのは本体ビューア（PolyLingPlayerViewerCore）だけなので、
//              リストクライアントだけのシーンの構成では、このポートで MCP には届かない（値は書く）。
//     user   … 省略可。--polyling-user=<user> で渡すユーザー名（リストクライアントが使う）。
//              同じ PC の collaborator はホスト名（端末名）と別の名前が要るので、ここに書く。
//
// 【既定の中身】
//   段 2 以前の動きと同じ：Editor 8770、ビルド用/PolyLing.exe 8772・Build Settings の有効なシーン。
//   role の server は、Build Settings の唯一のシーン（サンプルシーンA）で有効な
//   PolyLingPlayerViewer の _remoteMode が Server であることに合わせた。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.EditorControl
{
    /// <summary>ビルド構成ファイル全体。</summary>
    [Serializable]
    public class PolyLingBuildConfigFile
    {
        public int                      version = 1;
        public PolyLingEditorInstance   editor  = new PolyLingEditorInstance();
        public List<PolyLingBuildConfig> builds = new List<PolyLingBuildConfig>();
    }

    /// <summary>
    /// Editor の受付。ポートは PolyLingEditorControlServer.Port（8770）固定で、構成には書かない（段 6）。
    /// port は「書かれていないこと」を確かめるためだけに置く。JsonUtility は無いキーで初期値（-1）のままにするので、
    /// -1 以外なら書かれていた（検査で拒否する）。
    /// </summary>
    [Serializable]
    public class PolyLingEditorInstance
    {
        public string name = "editor";
        public int    port = NotWritten;

        /// <summary>port が書かれていない印。</summary>
        public const int NotWritten = -1;
    }

    /// <summary>ビルド構成 1 つ。</summary>
    [Serializable]
    public class PolyLingBuildConfig
    {
        public string       name;
        public List<string> scenes = new List<string>();
        public string       exe;
        public string       role;
        public int          port;
        public string       user = "";
    }

    /// <summary>ビルド構成ファイルの読み込みと検査。</summary>
    public static class PolyLingBuildConfigLoader
    {
        /// <summary>プロジェクトごとの構成（プロジェクト直下からの相対）。</summary>
        public const string ProjectConfigPath = "Assets/PolyLingBuild/ビルド構成.json";

        /// <summary>パッケージ既定の構成（プロジェクト直下からの相対）。</summary>
        public const string DefaultConfigPath = "Packages/com.haglib.polyling/Editor/Build/既定ビルド構成.json";

        /// <summary>role に書ける値。</summary>
        public static readonly string[] Roles = { "server", "host", "collaborator", "separate", "none" };

        /// <summary>
        /// 構成を読む。Assets 側があればそちら、無ければパッケージ既定。
        /// 読めない・検査に通らないときは false と理由（どのファイルかを含む）。
        /// </summary>
        public static bool TryLoad(out PolyLingBuildConfigFile config, out string sourcePath, out string error)
        {
            config = null;
            error  = null;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string projectFile = Path.Combine(projectRoot, ProjectConfigPath);
            string defaultFile = Path.Combine(projectRoot, DefaultConfigPath);

            sourcePath = File.Exists(projectFile) ? ProjectConfigPath : DefaultConfigPath;
            string full = File.Exists(projectFile) ? projectFile : defaultFile;

            if (!File.Exists(full))
            {
                error = $"ビルド構成ファイルがありません: {ProjectConfigPath} も {DefaultConfigPath} も見つかりません";
                return false;
            }

            string text;
            try { text = File.ReadAllText(full, System.Text.Encoding.UTF8); }
            catch (Exception ex)
            {
                error = $"{sourcePath} を読めません: {ex.Message}";
                return false;
            }

            try { config = JsonUtility.FromJson<PolyLingBuildConfigFile>(text); }
            catch (Exception ex)
            {
                error = $"{sourcePath} の JSON を解釈できません: {ex.Message}";
                return false;
            }
            if (config == null)
            {
                error = $"{sourcePath} が空です";
                return false;
            }

            if (!Validate(config, projectRoot, out string reason))
            {
                error = $"{sourcePath}: {reason}";
                config = null;
                return false;
            }
            return true;
        }

        /// <summary>構成名で引く。大文字小文字は区別しない。</summary>
        public static PolyLingBuildConfig Find(PolyLingBuildConfigFile config, string name)
        {
            if (config?.builds == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (var b in config.builds)
                if (string.Equals(b.name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>構成名の一覧（表示用）。</summary>
        public static string Names(PolyLingBuildConfigFile config)
        {
            var names = new List<string>();
            if (config?.builds != null)
                foreach (var b in config.builds) names.Add(b.name);
            return string.Join(", ", names);
        }

        private static bool Validate(PolyLingBuildConfigFile config, string projectRoot, out string reason)
        {
            reason = null;

            if (config.editor == null || string.IsNullOrWhiteSpace(config.editor.name))
            {
                reason = "editor.name がありません";
                return false;
            }
            if (config.editor.port != PolyLingEditorInstance.NotWritten)
            {
                reason = $"editor.port は書けません（Editor の受付は PolyLingEditorControlServer.Port＝{PolyLingEditorControlServer.Port} 固定）: {config.editor.port}";
                return false;
            }
            if (config.builds == null || config.builds.Count == 0)
            {
                reason = "builds に構成がありません";
                return false;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { config.editor.name.Trim() };
            var ports = new HashSet<int> { PolyLingEditorControlServer.Port };
            var exes  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string root = Path.GetFullPath(projectRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

            for (int i = 0; i < config.builds.Count; i++)
            {
                var b = config.builds[i];
                string at = $"builds[{i}]";
                if (b == null) { reason = $"{at} が空です"; return false; }

                if (string.IsNullOrWhiteSpace(b.name)) { reason = $"{at}.name がありません"; return false; }
                b.name = b.name.Trim();
                at = $"builds[{i}]（{b.name}）";
                if (!names.Add(b.name)) { reason = $"{at}.name が editor か他の構成と重複しています"; return false; }

                if (string.IsNullOrWhiteSpace(b.exe)) { reason = $"{at}.exe がありません"; return false; }
                b.exe = b.exe.Trim().Replace('\\', '/');
                if (Path.IsPathRooted(b.exe)) { reason = $"{at}.exe はプロジェクト直下からの相対で書いてください: {b.exe}"; return false; }
                if (!b.exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { reason = $"{at}.exe が .exe ではありません: {b.exe}"; return false; }
                string exeFull = Path.GetFullPath(Path.Combine(projectRoot, b.exe));
                if (!exeFull.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { reason = $"{at}.exe がプロジェクトの外です: {b.exe}"; return false; }
                if (!exes.Add(exeFull)) { reason = $"{at}.exe が他の構成と重複しています: {b.exe}"; return false; }

                b.role = (b.role ?? string.Empty).Trim().ToLowerInvariant();
                if (Array.IndexOf(Roles, b.role) < 0)
                {
                    reason = $"{at}.role は {string.Join(" / ", Roles)} のどれかです: {b.role}";
                    return false;
                }

                b.user = (b.user ?? string.Empty).Trim();

                if (!IsPort(b.port)) { reason = $"{at}.port が 1〜65535 ではありません: {b.port}"; return false; }
                if (!ports.Add(b.port)) { reason = $"{at}.port が editor か他の構成と重複しています: {b.port}"; return false; }

                if (b.scenes == null) b.scenes = new List<string>();
                for (int s = 0; s < b.scenes.Count; s++)
                {
                    string scene = (b.scenes[s] ?? string.Empty).Trim().Replace('\\', '/');
                    b.scenes[s] = scene;
                    if (!scene.StartsWith("Assets/", StringComparison.Ordinal)
                        || !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        reason = $"{at}.scenes[{s}] は Assets/... .unity で書いてください: {scene}";
                        return false;
                    }
                    if (!File.Exists(Path.Combine(projectRoot, scene)))
                    {
                        reason = $"{at}.scenes[{s}] のシーンがありません: {scene}";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsPort(int port) => port >= 1 && port <= 65535;
    }
}
