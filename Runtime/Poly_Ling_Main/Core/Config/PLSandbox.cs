// PLSandbox.cs
// コマンドから届いたパス文字列を実ファイル経路へ変換する唯一の関門。
//
// 【なぜ要るか】
//   コマンド（自動検証・MCP）は任意の文字列を送ってくる。素通しにすると
//   "C:\Windows\System32\..." のような経路をそのまま開いてしまう。
//   ここを通さないと実ファイル経路が得られない形にして、検査を 1 か所に集める。
//
// 【境界の決め方】
//   ・セッションの作業フォルダ 1 つを根にする。設定に保存し、次回以降もそれを使う
//   ・既定値は持たない。未設定のうちはすべて拒否する
//     （利用者が場所を意識しないまま書き込まれるのを避けるため）
//   ・拒否リストではなく許可リスト。根の下以外はすべて拒否する
//
// 【ダイアログ経路は別扱い】
//   利用者がファイルダイアログで選んだパスは「選んだ＝許可の表明」とみなし、
//   そのパスだけを 1 回許可する（AllowOnceFromDialog）。
//   この区別が無いと「パネルからは任意の場所へ保存できるのに、同じ操作を
//   コマンド化した途端に使えなくなる」という矛盾が起きる。
//
// 【保存先】
//   RecentPaths のキー "Sandbox.WorkFolder"。
//   （Application.persistentDataPath/PolyLing/RecentPaths.csv に 1 行増えるだけ。
//     新しい設定ファイルは作らない）
//
// Editor / Player 両対応。#if UNITY_EDITOR 不使用。毎フレーム処理なし。
// Runtime/Poly_Ling_Main/Core/Config/ に配置（RecentPaths と同じ場所）

using System;
using System.Collections.Generic;
using System.IO;

namespace Poly_Ling.Core
{
    /// <summary>コマンド経由のファイル入出力を作業フォルダの下へ閉じ込める。</summary>
    public static class PLSandbox
    {
        /// <summary>作業フォルダを保存する RecentPaths のキー。</summary>
        public const string WorkFolderKey = "Sandbox.WorkFolder";

        /// <summary>
        /// 未設定のときに返す文言。置き場所まで書く。
        /// 設定 UI は左ペインの「その他 → 作業フォルダ」にあり、
        /// 未設定のうちはファイル入出力が一切通らないため、
        /// 理由だけでは利用者がどこへ行けばよいか分からない。
        /// </summary>
        private const string UnsetMessage =
            "作業フォルダが未設定です。左ペインの「その他 → 作業フォルダ」で選んでください";

        private static readonly object _lock = new object();

        // 読み込み済みの作業フォルダ（正規化済み・末尾に区切り無し）。未設定は null。
        private static string _workFolder;
        private static bool   _loaded;

        // ダイアログで確定したパス。次にそのパスを解決するときだけ許す。
        private static readonly HashSet<string> _oneTimeAllowed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 書き込みを許す拡張子。編集ツールが .dll や .cs を書けると
        /// 境界を作った意味が薄れるので、入出力で実際に使うものだけを並べる。
        /// 比較は小文字・先頭のドット込み。
        /// </summary>
        private static readonly HashSet<string> WritableExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".csv", ".json", ".txt",
                ".pmx", ".mqo", ".obj", ".vrm", ".vrma", ".plrf", ".mfproj",
                ".png", ".jpg", ".jpeg",
            };

        // ================================================================
        // 作業フォルダ
        // ================================================================

        /// <summary>
        /// 現在の作業フォルダ。未設定は null。
        /// 初回アクセス時に RecentPaths から 1 回だけ読む。
        /// </summary>
        public static string WorkFolder
        {
            get { EnsureLoaded(); return _workFolder; }
        }

        /// <summary>作業フォルダが決まっているか。</summary>
        public static bool HasWorkFolder => !string.IsNullOrEmpty(WorkFolder);

        /// <summary>
        /// 作業フォルダを設定して保存する。実在するフォルダだけを受ける。
        /// </summary>
        /// <param name="absolutePath">利用者が選んだフォルダ。</param>
        /// <param name="reason">設定できなかった理由。成功時は null。</param>
        public static bool SetWorkFolder(string absolutePath, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(absolutePath))
            { reason = "フォルダが指定されていません"; return false; }

            string full;
            try { full = Path.GetFullPath(absolutePath); }
            catch (Exception ex) { reason = $"フォルダ名を解釈できません: {ex.Message}"; return false; }

            if (!Directory.Exists(full))
            { reason = $"フォルダがありません: {full}"; return false; }

            full = TrimSeparator(full);

            lock (_lock)
            {
                _workFolder = full;
                _loaded     = true;
                _oneTimeAllowed.Clear();
            }
            RecentPaths.Set(WorkFolderKey, full);
            return true;
        }

        /// <summary>
        /// 作業フォルダの設定を消す。以降はすべて拒否される。
        /// </summary>
        public static void ClearWorkFolder()
        {
            lock (_lock)
            {
                _workFolder = null;
                _loaded     = true;
                _oneTimeAllowed.Clear();
            }
            RecentPaths.Set(WorkFolderKey, "");
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                string saved = RecentPaths.Get(WorkFolderKey, "");
                _workFolder = string.IsNullOrEmpty(saved) ? null : TrimSeparator(saved);
                _loaded     = true;
            }
        }

        // ================================================================
        // ダイアログ経路
        // ================================================================

        /// <summary>
        /// ファイルダイアログで確定したパスを 1 回だけ許可する。
        /// 利用者が選んだこと自体が許可の表明なので、作業フォルダの外でも通す。
        /// 解決に使われた時点で許可は消える。
        /// </summary>
        /// <returns>渡された absolutePath をそのまま返す（呼び出し側で繋げやすくするため）。</returns>
        public static string AllowOnceFromDialog(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;

            string full;
            try { full = Path.GetFullPath(absolutePath); }
            catch (Exception) { return absolutePath; }

            lock (_lock) { _oneTimeAllowed.Add(full); }
            return absolutePath;
        }

        // ================================================================
        // 解決
        // ================================================================

        /// <summary>
        /// 読み込み用に経路を解決する。
        /// </summary>
        /// <param name="path">作業フォルダからの相対経路、またはダイアログで許可済みの絶対経路。</param>
        /// <param name="fullPath">解決した実経路。失敗時は null。</param>
        /// <param name="reason">拒否した理由。成功時は null。</param>
        public static bool TryResolveRead(string path, out string fullPath, out string reason)
            => TryResolve(path, forWrite: false, out fullPath, out reason);

        /// <summary>
        /// 書き込み用に経路を解決する。読み込みに加えて拡張子の許可も見る。
        /// </summary>
        /// <param name="path">作業フォルダからの相対経路、またはダイアログで許可済みの絶対経路。</param>
        /// <param name="fullPath">解決した実経路。失敗時は null。</param>
        /// <param name="reason">拒否した理由。成功時は null。</param>
        public static bool TryResolveWrite(string path, out string fullPath, out string reason)
            => TryResolve(path, forWrite: true, out fullPath, out reason);

        /// <summary>
        /// フォルダとして解決する。書き出し先フォルダの指定に使う。
        /// 拡張子は見ない。
        /// </summary>
        /// <param name="reason">拒否した理由。成功時は null。</param>
        public static bool TryResolveFolder(string path, out string fullPath, out string reason)
            => TryResolve(path, forWrite: false, out fullPath, out reason);

        private static bool TryResolve(string path, bool forWrite, out string fullPath, out string reason)
        {
            fullPath = null;
            reason   = null;

            if (string.IsNullOrWhiteSpace(path))
            { reason = "パスが空です"; return false; }

            // 正規化。ここで ".." と "." と相対を潰す。
            // 相対経路は作業フォルダ基準。作業フォルダが無ければ相対も絶対も解決できない。
            string root = WorkFolder;
            string full;
            try
            {
                full = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : (root == null ? null : Path.GetFullPath(Path.Combine(root, path)));
            }
            catch (Exception ex)
            { reason = $"パスを解釈できません: {ex.Message}"; return false; }

            if (full == null)
            {
                reason = UnsetMessage;
                return false;
            }

            // ダイアログで選んだパスは 1 回だけ許す。使ったら消す。
            bool allowedOnce;
            lock (_lock) { allowedOnce = _oneTimeAllowed.Remove(full); }
            if (allowedOnce)
            {
                if (forWrite && !IsWritableExtension(full, out reason)) return false;
                fullPath = full;
                return true;
            }

            if (root == null)
            {
                reason = UnsetMessage;
                return false;
            }

            if (!IsInside(root, full))
            {
                reason = $"作業フォルダの外は扱えません（作業フォルダ: {root}）";
                return false;
            }

            // リンクは辿らない。辿ると根の外へ抜けられる。
            if (HasReparsePointOnPath(root, full, out string linkPath))
            {
                reason = $"リンクは辿れません: {linkPath}";
                return false;
            }

            if (forWrite && !IsWritableExtension(full, out reason)) return false;

            fullPath = full;
            return true;
        }

        // ================================================================
        // 判定
        // ================================================================

        /// <summary>
        /// full が root の下にあるか。
        /// 区切り文字の境界で見る。単純な前方一致だと
        /// "/root" の許可で "/root-evil" が通ってしまう。
        /// </summary>
        private static bool IsInside(string root, string full)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(full)) return false;

            string r = TrimSeparator(root);
            string f = TrimSeparator(full);

            if (string.Equals(r, f, StringComparison.OrdinalIgnoreCase)) return true;

            string prefix = r + Path.DirectorySeparatorChar;
            return f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWritableExtension(string full, out string reason)
        {
            reason = null;
            string ext = Path.GetExtension(full);
            if (string.IsNullOrEmpty(ext))
            { reason = "拡張子の無いファイルへは書き込めません"; return false; }
            if (WritableExtensions.Contains(ext)) return true;

            reason = $"拡張子 {ext} へは書き込めません";
            return false;
        }

        /// <summary>
        /// root から full までの各段にリパースポイント（シンボリックリンク・
        /// ジャンクション）が無いかを見る。存在しない段は素通しでよい
        /// （これから作るフォルダ・ファイルのため）。
        /// </summary>
        private static bool HasReparsePointOnPath(string root, string full, out string linkPath)
        {
            linkPath = null;
            string r = TrimSeparator(root);

            try
            {
                string cur = TrimSeparator(full);
                while (!string.IsNullOrEmpty(cur) &&
                       !string.Equals(cur, r, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(cur) || Directory.Exists(cur))
                    {
                        var attr = File.GetAttributes(cur);
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                        { linkPath = cur; return true; }
                    }

                    string parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(parent) ||
                        string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase))
                        break;
                    cur = TrimSeparator(parent);
                }
            }
            catch (Exception)
            {
                // 属性を読めないときは判定を諦めて通す。
                // ここで拒否すると、権限の都合で読めないだけの経路まで使えなくなる。
                return false;
            }

            return false;
        }

        private static string TrimSeparator(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
