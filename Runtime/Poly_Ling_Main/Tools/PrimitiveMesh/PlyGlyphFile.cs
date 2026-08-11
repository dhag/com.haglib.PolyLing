// PlyGlyphFile.cs
// .plgly v1 リーダ。ヘッダと索引のみ先読みし、グリフ本体は二分探索＋seek で遅延読みする。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.GlyphText
{
    /// <summary>輪郭コマンドの種別。.plgly の type バイトと一致する。</summary>
    public enum PlyCommandType : byte
    {
        Line = 0,
        Quad = 1,
        Cubic = 2,
    }

    /// <summary>
    /// 輪郭を構成する 1 コマンド。終点は常に (X2,Y2)。
    /// Line は (X2,Y2) のみ、Quad は (X1,Y1) が制御点、
    /// Cubic は (X0,Y0)(X1,Y1) が制御点。
    /// </summary>
    public struct PlyCommand
    {
        public PlyCommandType Type;
        public float X0, Y0;
        public float X1, Y1;
        public float X2, Y2;
    }

    /// <summary>1 本の輪郭。終端は暗黙クローズ。</summary>
    public sealed class PlyContour
    {
        public float StartX;
        public float StartY;
        public PlyCommand[] Commands;
    }

    /// <summary>1 文字分のアウトライン。座標はデザイン単位。</summary>
    public sealed class PlyGlyph
    {
        public int CodePoint;
        public float Advance;
        public PlyContour[] Contours;
    }

    /// <summary>
    /// .plgly v1 ファイル。リトルエンディアン固定。
    /// </summary>
    public sealed class PlyGlyphFile
    {
        private const ushort SupportedVersion = 1;

        public string FilePath { get; private set; }
        public string FamilyName { get; private set; }
        public string StyleName { get; private set; }
        public float UnitsPerEm { get; private set; }
        public float Ascent { get; private set; }
        public float Descent { get; private set; }
        public float LineGap { get; private set; }

        public int GlyphCount => _codePoints != null ? _codePoints.Length : 0;

        private int[] _codePoints;
        private uint[] _offsets;
        private long _fileLength;

        private readonly Dictionary<int, PlyGlyph> _cache = new Dictionary<int, PlyGlyph>();

        /// <summary>ヘッダと索引を読み込む。失敗時は null。</summary>
        public static PlyGlyphFile Open(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var f = new PlyGlyphFile();
                f.FilePath = path;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs, Encoding.UTF8, true))
                {
                    f._fileLength = fs.Length;

                    byte[] magic = br.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != (byte)'P' || magic[1] != (byte)'L'
                        || magic[2] != (byte)'G' || magic[3] != (byte)'L')
                    {
                        Debug.LogWarning($"[PlyGlyphFile] マジックが不正です: {path}");
                        return null;
                    }

                    ushort version = br.ReadUInt16();
                    br.ReadUInt16(); // flags (予約)
                    if (version != SupportedVersion)
                    {
                        Debug.LogWarning($"[PlyGlyphFile] 未対応バージョン {version}: {path}");
                        return null;
                    }

                    f.UnitsPerEm = br.ReadSingle();
                    f.Ascent = br.ReadSingle();
                    f.Descent = br.ReadSingle();
                    f.LineGap = br.ReadSingle();

                    int glyphCount = br.ReadInt32();
                    if (glyphCount < 0 || glyphCount > 0x00100000)
                    {
                        Debug.LogWarning($"[PlyGlyphFile] グリフ数が不正です {glyphCount}: {path}");
                        return null;
                    }

                    f.FamilyName = ReadString(br);
                    f.StyleName = ReadString(br);

                    f._codePoints = new int[glyphCount];
                    f._offsets = new uint[glyphCount];
                    for (int i = 0; i < glyphCount; i++)
                    {
                        f._codePoints[i] = br.ReadInt32();
                        f._offsets[i] = br.ReadUInt32();
                    }
                }

                if (f.UnitsPerEm <= 0f)
                {
                    Debug.LogWarning($"[PlyGlyphFile] unitsPerEm が不正です {f.UnitsPerEm}: {path}");
                    return null;
                }

                return f;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlyGlyphFile] 読み込みに失敗しました: {path} : {ex.Message}");
                return null;
            }
        }

        private static string ReadString(BinaryReader br)
        {
            int len = br.ReadInt32();
            if (len <= 0) return string.Empty;
            byte[] b = br.ReadBytes(len);
            return Encoding.UTF8.GetString(b);
        }

        /// <summary>
        /// 指定コードポイント群をまとめて読み込みキャッシュへ入れる。
        /// 文字列 1 本ごとにファイルを開き直さないためのバッチ読み。
        /// </summary>
        public void Preload(IList<int> codePoints)
        {
            if (codePoints == null || codePoints.Count == 0) return;

            var need = new List<int>();
            for (int i = 0; i < codePoints.Count; i++)
            {
                int cp = codePoints[i];
                if (_cache.ContainsKey(cp)) continue;
                if (need.Contains(cp)) continue;
                need.Add(cp);
            }
            if (need.Count == 0) return;

            try
            {
                using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs, Encoding.UTF8, true))
                {
                    for (int i = 0; i < need.Count; i++)
                    {
                        int cp = need[i];
                        int idx = IndexOf(cp);
                        if (idx < 0) { _cache[cp] = null; continue; }
                        _cache[cp] = ReadGlyphAt(fs, br, _offsets[idx], cp);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlyGlyphFile] グリフ読み込みに失敗しました: {FilePath} : {ex.Message}");
            }
        }

        /// <summary>キャッシュにあれば返す。無ければ単発で読み込む。</summary>
        public bool TryGetGlyph(int codePoint, out PlyGlyph glyph)
        {
            if (!_cache.TryGetValue(codePoint, out glyph))
            {
                Preload(new List<int> { codePoint });
                _cache.TryGetValue(codePoint, out glyph);
            }
            return glyph != null;
        }

        /// <summary>索引を二分探索する。無ければ -1。</summary>
        private int IndexOf(int codePoint)
        {
            if (_codePoints == null) return -1;
            int lo = 0, hi = _codePoints.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int v = _codePoints[mid];
                if (v == codePoint) return mid;
                if (v < codePoint) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        private PlyGlyph ReadGlyphAt(FileStream fs, BinaryReader br, uint offset, int expectCodePoint)
        {
            if (offset >= _fileLength)
            {
                Debug.LogWarning($"[PlyGlyphFile] 索引オフセットが範囲外です {offset}: {FilePath}");
                return null;
            }

            fs.Seek(offset, SeekOrigin.Begin);

            var g = new PlyGlyph();
            g.CodePoint = br.ReadInt32();
            g.Advance = br.ReadSingle();
            int contourCount = br.ReadInt32();

            if (g.CodePoint != expectCodePoint || contourCount < 0 || contourCount > 0x10000)
            {
                Debug.LogWarning($"[PlyGlyphFile] グリフレコードが不正です U+{expectCodePoint:X4}: {FilePath}");
                return null;
            }

            g.Contours = new PlyContour[contourCount];
            for (int i = 0; i < contourCount; i++)
            {
                int cmdCount = br.ReadInt32();
                float sx = br.ReadSingle();
                float sy = br.ReadSingle();

                if (cmdCount < 0 || cmdCount > 0x100000)
                {
                    Debug.LogWarning($"[PlyGlyphFile] コマンド数が不正です U+{expectCodePoint:X4}: {FilePath}");
                    return null;
                }

                var c = new PlyContour();
                c.StartX = sx;
                c.StartY = sy;
                c.Commands = new PlyCommand[cmdCount];

                for (int j = 0; j < cmdCount; j++)
                {
                    var cmd = new PlyCommand();
                    cmd.Type = (PlyCommandType)br.ReadByte();
                    switch (cmd.Type)
                    {
                        case PlyCommandType.Line:
                            cmd.X2 = br.ReadSingle();
                            cmd.Y2 = br.ReadSingle();
                            break;
                        case PlyCommandType.Quad:
                            cmd.X1 = br.ReadSingle();
                            cmd.Y1 = br.ReadSingle();
                            cmd.X2 = br.ReadSingle();
                            cmd.Y2 = br.ReadSingle();
                            break;
                        case PlyCommandType.Cubic:
                            cmd.X0 = br.ReadSingle();
                            cmd.Y0 = br.ReadSingle();
                            cmd.X1 = br.ReadSingle();
                            cmd.Y1 = br.ReadSingle();
                            cmd.X2 = br.ReadSingle();
                            cmd.Y2 = br.ReadSingle();
                            break;
                        default:
                            Debug.LogWarning($"[PlyGlyphFile] 未知のコマンド種別 {(byte)cmd.Type} U+{expectCodePoint:X4}: {FilePath}");
                            return null;
                    }
                    c.Commands[j] = cmd;
                }

                g.Contours[i] = c;
            }

            return g;
        }
    }
}
