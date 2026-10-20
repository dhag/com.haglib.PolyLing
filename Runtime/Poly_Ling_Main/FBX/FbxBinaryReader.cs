// FbxBinaryReader.cs
// ============================================================
// FBX 6.x・7.x バイナリ → FbxNode の木
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。
//
// ■ 形（ufbx.c ufbxi_binary_parse_node と同じ読み方）
//   ヘッダ「Kaydara FBX Binary  \0」＋ 0x1A ＋ エンディアン（0 = リトル）＋ 版（u32）。計 27 バイト。
//   ノード：終了位置・プロパティ数・プロパティ列の長さ（版 7500 未満は u32、以上は u64）、名前の長さ（u8）、名前、
//   プロパティ、子ノード。子の終わりは全部 0 の空レコード（13 / 25 バイト）。終了位置 0 のノードで列が終わる。
//   プロパティの型記号：Y(i16) C(u8) I(i32) F(f32) D(f64) L(i64) S(文字列) R(バイト列)
//   f/d/l/i/b（配列：要素数 u32・符号化 u32・圧縮後の長さ u32。符号化 1 は zlib）。
//   C は 0/1 なら bool、それ以外は long（6.x の Key に文字の符号が入る）。配列 b は bool[] で返す（書き出しの FbxNode と同じ型）。
//   ビッグエンディアン（ヘッダのエンディアンが 0 以外）は、版・ノードの長さ・値・配列の要素数と中身を
//   すべてバイト反転して読む（ufbx.c 11196-11208・8765-8840・8971-9070。配列は展開してから反転）。
// ■ 範囲
//   6.x・7.x（6000〜7999）。それ以外は「未対応の版」。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Poly_Ling.Fbx
{
    public static class FbxBinaryReader
    {
        public const string Magic = "Kaydara FBX Binary";

        /// <summary>先頭がバイナリ FBX の印か。</summary>
        public static bool IsBinary(byte[] b)
            => b != null && b.Length >= 27 && Encoding.ASCII.GetString(b, 0, 18) == Magic;

        /// <summary>読む。失敗時は null と理由。</summary>
        public static List<FbxNode> Read(byte[] b, out int version, out string error)
        {
            version = 0;
            error = null;
            if (!IsBinary(b)) { error = "バイナリ FBX ではありません"; return null; }
            bool bigEndian = b[22] != 0;
            version = bigEndian ? (b[23] << 24 | b[24] << 16 | b[25] << 8 | b[26]) : BitConverter.ToInt32(b, 23);
            if (version < 6000 || version >= 8000)
            {
                error = $"未対応の版です（{(bigEndian ? "ビッグエンディアン " : "")}{version}）。読めるのは FBX 6.x と 7.x だけです";
                return null;
            }
            bool wide = version >= 7500;
            var rd = new Src(b, bigEndian);
            var top = new List<FbxNode>();
            long pos = 27;
            try
            {
                while (pos < b.Length)
                {
                    var n = ReadNode(rd, ref pos, wide, FbxArrayNodes.St.Root);
                    if (n == null) break;
                    top.Add(n);
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException || e is InvalidDataException)
            {
                error = $"ファイルが壊れています（位置 {pos}）: {e.Message}";
                return null;
            }
            return top;
        }

        /// <summary>エンディアンを考えて読む。</summary>
        private sealed class Src
        {
            public readonly byte[] B;
            public readonly bool Be;
            public Src(byte[] b, bool be) { B = b; Be = be; }

            private byte[] Swapped(int p, int n)
            {
                var t = new byte[n];
                for (int i = 0; i < n; i++) t[i] = B[p + n - 1 - i];
                return t;
            }

            public short  I16(int p) => Be ? BitConverter.ToInt16(Swapped(p, 2), 0)  : BitConverter.ToInt16(B, p);
            public int    I32(int p) => Be ? BitConverter.ToInt32(Swapped(p, 4), 0)  : BitConverter.ToInt32(B, p);
            public uint   U32(int p) => Be ? BitConverter.ToUInt32(Swapped(p, 4), 0) : BitConverter.ToUInt32(B, p);
            public long   I64(int p) => Be ? BitConverter.ToInt64(Swapped(p, 8), 0)  : BitConverter.ToInt64(B, p);
            public float  F32(int p) => Be ? BitConverter.ToSingle(Swapped(p, 4), 0) : BitConverter.ToSingle(B, p);
            public double F64(int p) => Be ? BitConverter.ToDouble(Swapped(p, 8), 0) : BitConverter.ToDouble(B, p);
        }

        private static FbxNode ReadNode(Src rd, ref long pos, bool wide, FbxArrayNodes.St parent)
        {
            var b = rd.B;
            long end, np;
            int p = checked((int)pos);
            if (wide)
            {
                end = rd.I64(p);
                np  = rd.I64(p + 8);
                p += 24;
            }
            else
            {
                end = rd.U32(p);
                np  = rd.U32(p + 4);
                p += 12;
            }
            int nameLen = b[p++];
            if (end == 0) { pos = p + nameLen; return null; }
            if (end > b.Length || end < p) throw new InvalidDataException("ノードの終了位置が範囲外です");

            var n = new FbxNode(Encoding.UTF8.GetString(b, p, nameLen));
            p += nameLen;
            for (long i = 0; i < np; i++) n.Props.Add(ReadProp(rd, ref p));
            if (FbxArrayNodes.IsNumberArray(parent, n.Name)) CollapseValues(n);
            var state = FbxArrayNodes.Next(parent, n.Name);
            pos = p;
            while (pos < end)
            {
                var c = ReadNode(rd, ref pos, wide, state);
                if (c == null) { n.Block(); break; }
                n.Add(c);
            }
            pos = end;
            return n;
        }

        private static object ReadProp(Src rd, ref int p)
        {
            var b = rd.B;
            char t = (char)b[p++];
            switch (t)
            {
                case 'Y': p += 2; return rd.I16(p - 2);
                case 'C': p += 1; return b[p - 1] <= 1 ? (object)(b[p - 1] != 0) : (long)b[p - 1];   // 0/1 は bool、ほかは文字の符号（6.x の Key の 'U' など。ufbx は値として読む）
                case 'I': p += 4; return rd.I32(p - 4);
                case 'F': p += 4; return rd.F32(p - 4);
                case 'D': p += 8; return rd.F64(p - 8);
                case 'L': p += 8; return rd.I64(p - 8);
                case 'S':
                {
                    int len = rd.I32(p);
                    p += 4;
                    if (len < 0 || p + len > b.Length) throw new InvalidDataException("文字列の長さが範囲外です");
                    string s = Encoding.UTF8.GetString(b, p, len);
                    p += len;
                    return s;
                }
                case 'R':
                {
                    int len = rd.I32(p);
                    p += 4;
                    if (len < 0 || p + len > b.Length) throw new InvalidDataException("バイト列の長さが範囲外です");
                    var r = new byte[len];
                    Buffer.BlockCopy(b, p, r, 0, len);
                    p += len;
                    return r;
                }
                case 'f': case 'd': case 'l': case 'i': case 'b':
                {
                    int count = rd.I32(p);
                    int enc   = rd.I32(p + 4);
                    int clen  = rd.I32(p + 8);
                    p += 12;
                    if (count < 0 || clen < 0 || p + clen > b.Length) throw new InvalidDataException("配列の長さが範囲外です");
                    int elem = t == 'd' || t == 'l' ? 8 : t == 'b' ? 1 : 4;
                    byte[] raw;
                    int rawOff;
                    if (enc == 1)
                    {
                        raw = Inflate(b, p, clen, count * elem);
                        rawOff = 0;
                    }
                    else if (enc == 0)
                    {
                        if (clen < count * elem) throw new InvalidDataException("配列の長さが要素数に足りません");
                        raw = b;
                        rawOff = p;
                    }
                    else throw new InvalidDataException($"配列の符号化 {enc} は未対応です");
                    p += clen;
                    if (rd.Be && elem > 1)
                    {
                        var sw = new byte[count * elem];
                        for (int i = 0; i < count; i++)
                            for (int k = 0; k < elem; k++) sw[i * elem + k] = raw[rawOff + i * elem + elem - 1 - k];
                        raw = sw;
                        rawOff = 0;
                    }
                    switch (t)
                    {
                        case 'f': { var a = new float[count];  Buffer.BlockCopy(raw, rawOff, a, 0, count * 4); return a; }
                        case 'd': { var a = new double[count]; Buffer.BlockCopy(raw, rawOff, a, 0, count * 8); return a; }
                        case 'l': { var a = new long[count];   Buffer.BlockCopy(raw, rawOff, a, 0, count * 8); return a; }
                        case 'i': { var a = new int[count];    Buffer.BlockCopy(raw, rawOff, a, 0, count * 4); return a; }
                        default:
                        {
                            var a = new bool[count];
                            for (int i = 0; i < count; i++) a[i] = raw[rawOff + i] != 0;
                            return a;
                        }
                    }
                }
                default:
                    throw new InvalidDataException($"プロパティの型 '{t}' は未対応です");
            }
        }

        /// <summary>
        /// 配列のノードで、値が配列の型（f/d/l/i/b）でなく数の並びなら 1 つの配列にまとめる（6.x。ufbx.c 8759-8870・9040-9100）。
        /// 文字列が混じれば壊れたファイル。
        /// </summary>
        private static void CollapseValues(FbxNode n)
        {
            if (n.Props.Count == 0) return;
            if (n.Props[0] is Array && !(n.Props[0] is byte[])) return;
            bool anyFloat = false;
            foreach (var v in n.Props)
            {
                if (v is float || v is double) anyFloat = true;
                else if (!(v is short || v is bool || v is int || v is long)) throw new InvalidDataException($"「{n.Name}」の値の並びに数でない値があります");
            }
            if (anyFloat)
            {
                var a = new double[n.Props.Count];
                for (int i = 0; i < a.Length; i++) a[i] = FbxValue.ToDouble(n.Props[i]);
                n.Props.Clear(); n.Props.Add(a);
            }
            else
            {
                var a = new long[n.Props.Count];
                for (int i = 0; i < a.Length; i++) a[i] = FbxValue.ToLong(n.Props[i]);
                n.Props.Clear(); n.Props.Add(a);
            }
        }

        /// <summary>zlib（2 バイトのヘッダ＋ Deflate）を展開する。</summary>
        private static byte[] Inflate(byte[] b, int off, int len, int expect)
        {
            if (len < 2) throw new InvalidDataException("圧縮された配列が短すぎます");
            var dst = new byte[expect];
            using (var ms = new MemoryStream(b, off + 2, len - 2, false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < expect)
                {
                    int n = ds.Read(dst, got, expect - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got != expect) throw new InvalidDataException($"圧縮された配列を展開した長さが違います（{got} / {expect}）");
            }
            return dst;
        }
    }
}
