// Remote/RemoteFileBundle.cs
// フォルダ一式（相対パス付き複数ファイル）を1フレームで転送するバイナリ束。
//
// 既存の RemoteBinarySerializer.WrapRawFile は「拡張子8バイト＋本体」の
// 単一ファイル専用で相対パスを運べない。プロジェクトファイル（フォルダ形式）は
// project.csv ＋ モデルごとのサブフォルダ ＋ textures/ の複数ファイル構成のため、
// 相対パスを保持できる束形式を別に用意する。
//
// ■ PLRF フォーマット
// [Header 12B]
//   Magic      : 4B "PLRF"
//   Version    : 1B (現在 1)
//   RootKind   : 1B (0=Model, 1=Project)
//   Reserved   : 2B
//   FileCount  : 4B (uint32)
// [BundleName] string (uint16 長 + UTF8)
// [Entry × FileCount]
//   RelPath    : string (uint16 長 + UTF8。区切りは '/' 固定)
//   Length     : 4B (uint32)
//   Bytes      : Length バイト
//
// 受信側はルート直下に <BundleName> フォルダを作って展開する。
// 相対パスは '..'・絶対パス・ドライブ指定を拒否する（ルート外への書込防止）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Remote
{
    /// <summary>フォルダ一式を運ぶバイナリ束（PLRF）。</summary>
    public static class RemoteFileBundle
    {
        /// <summary>"PLRF"（リトルエンディアン uint32）</summary>
        public const uint Magic = 0x46524C50;

        public const byte Version = 1;

        /// <summary>ルートがモデルフォルダ（model.csv を直下に持つ）。</summary>
        public const byte KindModel = 0;

        /// <summary>ルートがプロジェクトフォルダ（モデルフォルダを配下に持つ）。</summary>
        public const byte KindProject = 1;

        /// <summary>1束あたりの上限。誤ったフォルダ指定で巨大転送になるのを防ぐ。</summary>
        private const long MaxTotalBytes = 512L * 1024L * 1024L;

        // ================================================================
        // 判定
        // ================================================================

        /// <summary>先頭4バイトが PLRF かどうか。他のバイナリ push と区別するために使う。</summary>
        public static bool IsBundle(byte[] data)
        {
            if (data == null || data.Length < 12) return false;
            uint magic = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
            return magic == Magic;
        }

        // ================================================================
        // Serialize: フォルダ → byte[]
        // ================================================================

        /// <summary>
        /// rootFolder 配下の全ファイルを再帰的に束ねる。失敗時は null。
        /// </summary>
        public static byte[] Serialize(string rootFolder, string bundleName, byte rootKind, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
            {
                error = "フォルダが存在しません: " + rootFolder;
                return null;
            }

            string root = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                error = "列挙に失敗: " + ex.Message;
                return null;
            }

            long total = 0;
            var entries = new List<KeyValuePair<string, string>>(files.Length);   // relPath, fullPath
            foreach (string full in files)
            {
                string rel = ToRelative(root, full);
                if (rel == null) continue;

                try { total += new FileInfo(full).Length; }
                catch { continue; }

                if (total > MaxTotalBytes)
                {
                    error = $"合計サイズが上限({MaxTotalBytes / (1024 * 1024)}MB)を超えました";
                    return null;
                }

                entries.Add(new KeyValuePair<string, string>(rel, full));
            }

            try
            {
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(Magic);
                    w.Write(Version);
                    w.Write(rootKind);
                    w.Write((ushort)0);              // Reserved
                    w.Write((uint)entries.Count);

                    WriteString(w, bundleName ?? "");

                    foreach (var kv in entries)
                    {
                        byte[] body = File.ReadAllBytes(kv.Value);
                        WriteString(w, kv.Key);
                        w.Write((uint)body.Length);
                        w.Write(body);
                    }

                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                error = "束の作成に失敗: " + ex.Message;
                return null;
            }
        }

        // ================================================================
        // Deserialize: byte[] → フォルダ
        // ================================================================

        /// <summary>
        /// 束を destParent/&lt;BundleName&gt; へ展開する。
        /// 既存の同名フォルダは削除してから作り直す（前回の残骸を混ぜないため）。
        /// </summary>
        /// <param name="folderPath">展開先フォルダ（destParent/BundleName）。</param>
        public static bool Deserialize(
            byte[] data,
            string destParent,
            out string folderPath,
            out byte rootKind,
            out int fileCount,
            out string error)
        {
            folderPath = null;
            rootKind   = KindProject;
            fileCount  = 0;
            error      = null;

            if (!IsBundle(data))
            {
                error = "PLRF ではありません";
                return false;
            }
            if (string.IsNullOrEmpty(destParent))
            {
                error = "展開先が未指定です";
                return false;
            }

            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms))
                {
                    r.ReadUInt32();                   // Magic（IsBundle で確認済み）
                    byte version = r.ReadByte();
                    if (version != Version)
                    {
                        error = $"未対応バージョン: {version}";
                        return false;
                    }

                    rootKind = r.ReadByte();
                    r.ReadUInt16();                   // Reserved
                    uint count = r.ReadUInt32();

                    string bundleName = ReadString(r);
                    bundleName = SanitizeFolderName(bundleName);

                    string target = Path.Combine(destParent, bundleName);

                    // 展開前に作り直す。残骸があると、削除されたモデルが
                    // 書き出し側に残り続ける。
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.CreateDirectory(target);

                    string targetFull = Path.GetFullPath(target);

                    for (uint i = 0; i < count; i++)
                    {
                        string rel = ReadString(r);
                        uint len   = r.ReadUInt32();
                        byte[] body = r.ReadBytes((int)len);

                        if (!IsSafeRelative(rel))
                        {
                            error = "不正な相対パス: " + rel;
                            return false;
                        }

                        string outPath = Path.Combine(targetFull, rel.Replace('/', Path.DirectorySeparatorChar));

                        // 正規化後もルート配下であることを再確認する。
                        string outFull = Path.GetFullPath(outPath);
                        if (!outFull.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
                        {
                            error = "展開先の外を指しています: " + rel;
                            return false;
                        }

                        string dir = Path.GetDirectoryName(outFull);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(outFull, body);
                        fileCount++;
                    }

                    folderPath = targetFull;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "展開に失敗: " + ex.Message;
                return false;
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>ファイル名／フォルダ名として安全な文字列にする。空なら "Project"。</summary>
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Project";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }

            string s = sb.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(s) ? "Project" : s;
        }

        private static string ToRelative(string root, string fullPath)
        {
            string full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

            string rel = full.Substring(root.Length)
                             .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }

        private static bool IsSafeRelative(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            if (rel.IndexOf(':') >= 0) return false;
            if (rel[0] == '/' || rel[0] == '\\') return false;

            foreach (string seg in rel.Split('/'))
            {
                if (seg.Length == 0) return false;
                if (seg == "." || seg == "..") return false;
            }
            return true;
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > ushort.MaxValue)
            {
                Debug.LogWarning("[RemoteFileBundle] 文字列が長すぎるため切り詰めます");
                Array.Resize(ref b, ushort.MaxValue);
            }
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        private static string ReadString(BinaryReader r)
        {
            ushort len = r.ReadUInt16();
            if (len == 0) return "";
            return Encoding.UTF8.GetString(r.ReadBytes(len));
        }
    }
}
