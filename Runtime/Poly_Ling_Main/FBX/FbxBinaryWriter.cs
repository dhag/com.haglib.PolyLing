// FbxBinaryWriter.cs
// ============================================================
// FBX ノード木 → FBX 7.4 バイナリ
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 7。
//
// ■ 形（版 7400。位置・長さは 32 ビット）
//   ヘッダ  "Kaydara FBX Binary  \0" 0x1A 0x00、版（u32）
//   ノード  終了位置（u32）・プロパティ数（u32）・プロパティ列の長さ（u32）・名前の長さ（u8）・名前・
//           プロパティ列・子ノード・（入れ子の区切りを書くとき＝FbxNode.HasBlock）空レコード 13 バイト
//   最上位  ノードを並べ、最後に空レコード
//   フッタ  フッタ ID（16）・16 バイト境界までの 0（境界ちょうどなら 16 個）・0（u32）・版（u32）・
//           0 を 120 バイト・終端の印（16）
//   フッタ ID は FileId・CreationTime と対になる値。FbxModelWriter.FileId / CreationTime と同じく、
//   FBX SDK 2020.3.4 が書いたファイル（SandBox/fbx7/base_male.fbx）の 3 つ組をそのまま使う。
//
// ■ プロパティ
//   型記号は FbxNodeTree.cs 冒頭。文字列・バイト列は長さ（u32）＋本体。
//   配列は要素数（u32）・符号化（u32。0 = 生、1 = zlib）・本体の長さ（u32）・本体。
//   本体が CompressThreshold バイト以上の配列は zlib（Deflate ＋ 2 バイトのヘッダ ＋ Adler-32）にする。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Poly_Ling.Fbx
{
    internal static class FbxBinaryWriter
    {
        public const int Version = 7400;

        private const int CompressThreshold = 128;

        private static readonly byte[] Magic =
            Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\u001a\0");

        /// <summary>FileId・CreationTime と対のフッタ ID（冒頭の説明）。</summary>
        public static readonly byte[] FooterId =
        {
            0xfa, 0xbc, 0xa9, 0x0b, 0xd1, 0xc8, 0xd7, 0x63, 0xb5, 0x75, 0xfb, 0x8c, 0x13, 0xfe, 0x2a, 0x72,
        };

        private static readonly byte[] FooterMagic =
        {
            0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b,
        };

        public static void Write(string path, IList<FbxNode> top)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            using (var w = new BinaryWriter(fs, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write((uint)Version);

                foreach (var n in top) WriteNode(w, n);
                WriteNullRecord(w);

                // フッタ
                w.Write(FooterId);
                long pos = fs.Position;
                long pad = ((pos + 15) & ~15L) - pos;
                if (pad == 0) pad = 16;
                for (long i = 0; i < pad; i++) w.Write((byte)0);
                w.Write((uint)0);
                w.Write((uint)Version);
                w.Write(new byte[120]);
                w.Write(FooterMagic);
            }
        }

        private static void WriteNullRecord(BinaryWriter w) => w.Write(new byte[13]);

        private static void WriteNode(BinaryWriter w, FbxNode n)
        {
            var s = w.BaseStream;
            long start = s.Position;

            w.Write((uint)0);                 // 終了位置（あとで埋める）
            w.Write((uint)n.Props.Count);
            w.Write((uint)0);                 // プロパティ列の長さ（あとで埋める）
            byte[] name = Encoding.UTF8.GetBytes(n.Name);
            if (name.Length > 255) throw new InvalidOperationException($"FBX のノード名が長すぎます: {n.Name}");
            w.Write((byte)name.Length);
            w.Write(name);

            long propStart = s.Position;
            foreach (var p in n.Props) WriteProp(w, p);
            long propEnd = s.Position;

            if (n.Children != null)
                foreach (var c in n.Children) WriteNode(w, c);
            if (n.HasBlock)
                WriteNullRecord(w);

            long end = s.Position;
            if (end > uint.MaxValue) throw new InvalidOperationException("FBX 7.4 の大きさの上限（4 GB）を超えました");

            s.Position = start;
            w.Write((uint)end);
            s.Position = start + 8;
            w.Write((uint)(propEnd - propStart));
            s.Position = end;
        }

        private static void WriteProp(BinaryWriter w, object p)
        {
            switch (p)
            {
                case short v:  w.Write((byte)'Y'); w.Write(v); break;
                case bool v:   w.Write((byte)'C'); w.Write((byte)(v ? 1 : 0)); break;
                case int v:    w.Write((byte)'I'); w.Write(v); break;
                case float v:  w.Write((byte)'F'); w.Write(v); break;
                case double v: w.Write((byte)'D'); w.Write(v); break;
                case long v:   w.Write((byte)'L'); w.Write(v); break;
                case string v:
                {
                    byte[] b = Encoding.UTF8.GetBytes(v);
                    w.Write((byte)'S'); w.Write((uint)b.Length); w.Write(b);
                    break;
                }
                case byte[] v: w.Write((byte)'R'); w.Write((uint)v.Length); w.Write(v); break;
                case float[] v:  WriteArray(w, 'f', v.Length, v.Length * 4, bw => { foreach (var x in v) bw.Write(x); }); break;
                case double[] v: WriteArray(w, 'd', v.Length, v.Length * 8, bw => { foreach (var x in v) bw.Write(x); }); break;
                case long[] v:   WriteArray(w, 'l', v.Length, v.Length * 8, bw => { foreach (var x in v) bw.Write(x); }); break;
                case int[] v:    WriteArray(w, 'i', v.Length, v.Length * 4, bw => { foreach (var x in v) bw.Write(x); }); break;
                case bool[] v:   WriteArray(w, 'b', v.Length, v.Length,     bw => { foreach (var x in v) bw.Write((byte)(x ? 1 : 0)); }); break;
                default:
                    throw new InvalidOperationException($"FBX に書けない型です: {p?.GetType().Name ?? "null"}");
            }
        }

        private static void WriteArray(BinaryWriter w, char code, int count, int rawBytes, Action<BinaryWriter> body)
        {
            byte[] raw;
            using (var ms = new MemoryStream(rawBytes))
            using (var bw = new BinaryWriter(ms))
            {
                body(bw);
                bw.Flush();
                raw = ms.ToArray();
            }

            w.Write((byte)code);
            w.Write((uint)count);
            if (raw.Length >= CompressThreshold)
            {
                byte[] z = Zlib(raw);
                w.Write((uint)1);
                w.Write((uint)z.Length);
                w.Write(z);
            }
            else
            {
                w.Write((uint)0);
                w.Write((uint)raw.Length);
                w.Write(raw);
            }
        }

        /// <summary>zlib（RFC 1950）：ヘッダ 0x78 0x9C、Deflate、Adler-32（ビッグエンディアン）。</summary>
        private static byte[] Zlib(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    ds.Write(raw, 0, raw.Length);

                uint a = 1, b = 0;
                const uint Mod = 65521;
                int i = 0;
                while (i < raw.Length)
                {
                    int n = Math.Min(5552, raw.Length - i);
                    for (int k = 0; k < n; k++) { a += raw[i + k]; b += a; }
                    a %= Mod; b %= Mod;
                    i += n;
                }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }
    }
}
