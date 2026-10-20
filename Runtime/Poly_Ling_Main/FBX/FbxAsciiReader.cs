// FbxAsciiReader.cs
// ============================================================
// FBX 6.x・7.x ASCII → FbxNode の木
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。
//
// ■ 形
//   「; …」は行末までの注釈。ノードは「名前: 値, 値, … { 子 }」。{ } は省略できる。
//   値：文字列 "…"（&quot; は "、&cr; は \r、&lf; は \n）、数（整数は long、小数・指数は double）、
//   裸の語（T・Y・W など。ufbx と同じく先頭の文字の符号を long で返す）。
//   配列「*要素数 { a: v, v, … }」は、全部整数なら long[]、そうでなければ double[]。
//   型はバイナリと違い記号を持たないので、使う側（FbxSceneReader）が FbxValue で数・配列を読み替える。
//   6.x の配列は「Vertices: 1,2,3,…」と値を並べるだけ。ufbx と同じく、親の文脈とノード名で配列と決まるノード
//   （ufbx.c 7974-8082 ufbxi_update_parse_state、8084-8498 ufbxi_is_array_node）は、値の並びを 1 つの配列にまとめる。
//   そのとき文字列は捨て（ufbx.c 10454-10457）、裸の語は先頭の文字の符号にする。
//   文字列の配列（Content・Children）はまとめない。
// ■ 版
//   「FBXHeaderExtension { FBXVersion: n }」。6000〜7999 以外は「未対応の版」。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Poly_Ling.Fbx
{
    public static class FbxAsciiReader
    {
        public static List<FbxNode> Read(byte[] data, out int version, out string error)
        {
            version = 0;
            error = null;
            var r = new Lexer(data);
            var top = new List<FbxNode>();
            try
            {
                while (true)
                {
                    r.SkipSpace();
                    if (r.Eof) break;
                    if (r.Peek == '}') { r.Pos++; continue; }   // 釣り合わない閉じ括弧は読み飛ばす
                    top.Add(ReadNode(r, FbxArrayNodes.St.Root));
                }
            }
            catch (FormatException e)
            {
                error = $"ASCII FBX を読めません（{r.Line} 行目）: {e.Message}";
                return null;
            }

            foreach (var n in top)
            {
                if (n.Name != "FBXHeaderExtension" || n.Children == null) continue;
                foreach (var c in n.Children)
                    if (c.Name == "FBXVersion" && c.Props.Count > 0) version = (int)FbxValue.ToLong(c.Props[0]);
            }
            if (version < 6000 || version >= 8000)
            {
                error = $"未対応の版です（ASCII {version}）。読めるのは FBX 6.x と 7.x だけです";
                return null;
            }
            return top;
        }

        /// <summary>値の並びを 1 つの配列にする（既に 1 つの配列ならそのまま）。</summary>
        private static void CollapseArray(FbxNode n)
        {
            if (n.Props.Count == 1 && n.Props[0] is Array && !(n.Props[0] is byte[])) return;
            var longs = new List<long>();
            List<double> doubles = null;
            foreach (var v in n.Props)
            {
                if (v is long l) { if (doubles != null) doubles.Add(l); else longs.Add(l); }
                else if (v is double d)
                {
                    if (doubles == null)
                    {
                        doubles = new List<double>(Math.Max(16, longs.Count * 2));
                        foreach (var x in longs) doubles.Add(x);
                    }
                    doubles.Add(d);
                }
                else if (v is long[] la) { foreach (var x in la) { if (doubles != null) doubles.Add(x); else longs.Add(x); } }
                else if (v is double[] da)
                {
                    if (doubles == null) { doubles = new List<double>(); foreach (var x in longs) doubles.Add(x); }
                    doubles.AddRange(da);
                }
                // 文字列は捨てる
            }
            n.Props.Clear();
            n.Props.Add(doubles != null ? (object)doubles.ToArray() : longs.ToArray());
        }

        private static FbxNode ReadNode(Lexer r, FbxArrayNodes.St parent)
        {
            string name = r.ReadName();
            r.SkipSpace();
            if (r.Peek != ':') throw new FormatException($"「{name}」の後に ':' がありません");
            r.Pos++;
            var n = new FbxNode(name);

            // 値の並び（行が改まっても ',' で続く）
            bool afterComma = false;
            while (true)
            {
                r.SkipSpaceSameLine();
                if (r.Eof) return n;
                byte c = r.Peek;
                if (c == '\n' || c == '\r') { if (!r.NextLineContinues(n.Props.Count == 0, afterComma)) break; continue; }
                if (c == '{') break;
                if (c == '}') return n;
                if (c == ',') { r.Pos++; afterComma = true; continue; }
                afterComma = false;
                if (c == '*')
                {
                    r.Pos++;
                    r.ReadNumberToken();               // 要素数（読み飛ばす。実際の数は a: の値から）
                    r.SkipSpace();
                    if (r.Peek != '{') throw new FormatException("配列の '{' がありません");
                    r.Pos++;
                    n.Props.Add(ReadArrayBody(r));
                    continue;
                }
                n.Props.Add(r.ReadValue());
            }
            if (FbxArrayNodes.IsNumberArray(parent, name)) CollapseArray(n);
            var state = FbxArrayNodes.Next(parent, name);

            r.SkipSpace();
            if (!r.Eof && r.Peek == '{')
            {
                r.Pos++;
                n.Block();
                while (true)
                {
                    r.SkipSpace();
                    if (r.Eof) throw new FormatException($"「{name}」の '}}' がありません");
                    if (r.Peek == '}') { r.Pos++; break; }
                    n.Add(ReadNode(r, state));
                }
            }
            return n;
        }

        /// <summary>「a: v, v, … }」を読む。</summary>
        private static object ReadArrayBody(Lexer r)
        {
            var longs = new List<long>();
            List<double> doubles = null;
            while (true)
            {
                r.SkipSpace();
                if (r.Eof) throw new FormatException("配列の '}' がありません");
                byte c = r.Peek;
                if (c == '}') { r.Pos++; break; }
                if (c == ',') { r.Pos++; continue; }
                if (c == 'a' && r.At(1) == ':') { r.Pos += 2; continue; }
                var v = r.ReadNumberToken();
                if (v is long l)
                {
                    if (doubles != null) doubles.Add(l); else longs.Add(l);
                }
                else
                {
                    if (doubles == null)
                    {
                        doubles = new List<double>(Math.Max(16, longs.Count * 2));
                        foreach (var x in longs) doubles.Add(x);
                    }
                    doubles.Add((double)v);
                }
            }
            return doubles != null ? (object)doubles.ToArray() : longs.ToArray();
        }

        private sealed class Lexer
        {
            private readonly byte[] _b;
            public int Pos;
            public int Line = 1;

            public Lexer(byte[] b)
            {
                _b = b;
                // UTF-8 の BOM
                if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) Pos = 3;
            }

            public bool Eof => Pos >= _b.Length;
            public byte Peek => _b[Pos];
            public byte At(int k) => Pos + k < _b.Length ? _b[Pos + k] : (byte)0;

            /// <summary>空白・改行・注釈を飛ばす。</summary>
            public void SkipSpace()
            {
                while (Pos < _b.Length)
                {
                    byte c = _b[Pos];
                    if (c == ';') { while (Pos < _b.Length && _b[Pos] != '\n') Pos++; continue; }
                    if (c == '\n') { Line++; Pos++; continue; }
                    if (c == ' ' || c == '\t' || c == '\r') { Pos++; continue; }
                    break;
                }
            }

            /// <summary>同じ行の空白と注釈を飛ばす（改行は残す）。</summary>
            public void SkipSpaceSameLine()
            {
                while (Pos < _b.Length)
                {
                    byte c = _b[Pos];
                    if (c == ';') { while (Pos < _b.Length && _b[Pos] != '\n') Pos++; continue; }
                    if (c == ' ' || c == '\t') { Pos++; continue; }
                    break;
                }
            }

            /// <summary>
            /// 改行の位置で、値の並びが次の行へ続くか（最後に読んだのが ',' か（注釈を挟んでもよい）、次の行が ',' で始まるか、
            /// まだ値が 1 つも無く次の行が値で始まるか。ufbx は改行を区切りに使わない。ufbx.c 10280-10420）。
            /// 続くなら改行を飛ばして true。
            /// </summary>
            public bool NextLineContinues(bool firstValue, bool trailingComma)
            {
                int save = Pos, saveLine = Line;
                SkipSpace();
                if (trailingComma || (!Eof && Peek == ',')) return true;
                if (firstValue && !Eof && StartsValue()) return true;
                Pos = save; Line = saveLine;
                return false;
            }

            /// <summary>今の位置が値で始まるか（「名前:」・'{'・'}' でない）。</summary>
            private bool StartsValue()
            {
                byte c = _b[Pos];
                if (c == '{' || c == '}') return false;
                if (c == '"' || c == '*' || c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9')) return true;
                int k = Pos;
                while (k < _b.Length)
                {
                    byte d = _b[k];
                    if (d == ':' ) return false;   // 名前
                    if (d == ',' || d == ' ' || d == '\t' || d == '\r' || d == '\n' || d == '{' || d == '}' || d == ';') break;
                    k++;
                }
                return true;   // 裸の語
            }

            public string ReadName()
            {
                // 古いファイルの「"名前": { … }」（ufbx.c 9878-9884）
                if (Pos < _b.Length && _b[Pos] == '"') return ReadString();
                int s = Pos;
                while (Pos < _b.Length)
                {
                    byte c = _b[Pos];
                    if (c == ':' || c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '{' || c == '}' || c == ',') break;
                    Pos++;
                }
                if (Pos == s) throw new FormatException($"ノード名がありません（'{(char)_b[Pos]}'）");
                return Encoding.UTF8.GetString(_b, s, Pos - s);
            }

            public object ReadValue()
            {
                byte c = _b[Pos];
                if (c == '"') return ReadString();
                if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9')) return ReadNumberToken();
                // 裸の語（T・Y・W など）
                int s = Pos;
                while (Pos < _b.Length)
                {
                    byte d = _b[Pos];
                    if (d == ',' || d == ' ' || d == '\t' || d == '\r' || d == '\n' || d == '{' || d == '}' || d == ';') break;
                    Pos++;
                }
                if (Pos == s) throw new FormatException($"値を読めません（'{(char)c}'）");
                // 裸の語は先頭の文字の符号（ufbx.c 10509-10529。T → 84）。inf・nan と読めれば数。
                string w = Encoding.UTF8.GetString(_b, s, Pos - s);
                if (w.Length > 1 && ParseInfNan(w, out double inf)) return inf;
                return (long)_b[s];
            }

            /// <summary>inf・nan の綴り（ufbx.c 1545-1598 ufbxi_parse_inf_nan。全体が当てはまるときだけ）。</summary>
            public static bool ParseInfNan(string str, out double result)
            {
                result = 0;
                int p = 0, end = str.Length;
                bool negative = false;
                if (p != end && (str[p] == '+' || str[p] == '-')) negative = str[p++] == '-';
                bool Scan(string w) => end - p >= w.Length && string.Compare(str, p, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) == 0;
                ulong top;
                if (end - p >= 3 && str[p] >= '0' && str[p] <= '9' && str[p + 1] == '.' && str[p + 2] == '#')
                {
                    p += 3;
                    if (Scan("inf")) { p += 3; top = 0x7ff0; }
                    else if (Scan("nan") || Scan("ind")) { p += 3; top = 0x7ff8; }
                    else return false;
                    while (p != end && str[p] >= '0' && str[p] <= '9') p++;
                }
                else if (Scan("nan"))
                {
                    p += 3; top = 0x7ff8;
                    if (p != end && str[p] == '(')
                    {
                        p++;
                        while (p != end && str[p] != ')')
                        {
                            char c = str[p];
                            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
                            p++;
                        }
                        if (p == end) return false;
                        p++;
                    }
                }
                else if (Scan("inf"))
                {
                    p += 3;
                    if (Scan("inity")) p += 5;
                    top = 0x7ff0;
                }
                else return false;
                if (p != end) return false;
                if (negative) top |= 0x8000;
                result = BitConverter.Int64BitsToDouble(unchecked((long)(top << 48)));
                return true;
            }

            private string ReadString()
            {
                Pos++;
                int s = Pos;
                while (Pos < _b.Length && _b[Pos] != '"')
                {
                    if (_b[Pos] == '\n') Line++;
                    Pos++;
                }
                if (Pos >= _b.Length) throw new FormatException("文字列の '\"' が閉じていません");
                string v = Encoding.UTF8.GetString(_b, s, Pos - s);
                Pos++;
                return v.IndexOf('&') >= 0 ? v.Replace("&quot;", "\"").Replace("&cr;", "\r").Replace("&lf;", "\n") : v;
            }

            /// <summary>数を読む。整数なら long、小数点か指数があれば double。1.#INF・-nan(ind) なども数（ufbx.c 9757-9787）。</summary>
            public object ReadNumberToken()
            {
                int s = Pos;
                bool isFloat = false;
                while (Pos < _b.Length)
                {
                    byte c = _b[Pos];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+') { Pos++; continue; }
                    if (c == '.' || c == 'e' || c == 'E') { isFloat = true; Pos++; continue; }
                    break;
                }
                if (Pos == s) throw new FormatException($"数がありません（'{(char)_b[Pos]}'）");
                bool nanLike = false;
                while (Pos < _b.Length)
                {
                    byte c = _b[Pos];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '#' || c == '(' || c == ')') { nanLike = true; Pos++; continue; }
                    break;
                }
                string t = Encoding.ASCII.GetString(_b, s, Pos - s);
                if (nanLike)
                {
                    if (ParseInfNan(t, out double nv)) return nv;
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double fv)) return fv;
                    throw new FormatException($"数を読めません（{t}）");
                }
                if (!isFloat && long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                throw new FormatException($"数を読めません（{t}）");
            }
        }
    }

    /// <summary>
    /// 親の文脈とノード名で、値の並びを配列にまとめるノードかを決める（ufbx.c 7974-8082 ufbxi_update_parse_state、
    /// 8084-8498 ufbxi_is_array_node）。ASCII とバイナリで共用する。
    /// </summary>
    internal static class FbxArrayNodes
    {
        // ---- 文脈（ufbx.c 7974-8082 と同じ。配列の判定に要るものだけ）----
        internal enum St
        {
            Unknown, Root, Objects, Model, Geometry, LegacyModel, Deformer, Pose, PoseNode, Takes, Take, TakeObject, Channel,
            Shape, LegacyLink, LegacyMedia, LegacyVideo, Video, AnimCurve, SelectionNode, LayeredTexture, Thumbnail,
            HeaderExt, SceneInfo, Audio, LeNormal, LeBinormal, LeTangent, LeUV, LeColor, LeVertexCrease, LeEdgeCrease,
            LeSmoothing, LeVisibility, LePolygonGroup, LeHole, LeMaterial, LeOther, GeometryUVInfo, AssociateModel,
        }

        internal static St Next(St parent, string name)
        {
            switch (parent)
            {
                case FbxArrayNodes.St.Root:
                    if (name == "FBXHeaderExtension") return St.HeaderExt;
                    if (name == "Objects") return St.Objects;
                    if (name == "Takes") return St.Takes;
                    if (name == "Model") return St.LegacyModel;
                    if (name == "Media") return St.LegacyMedia;
                    break;
                case St.HeaderExt:
                    if (name == "SceneInfo") return St.SceneInfo;
                    break;
                case St.SceneInfo:
                    if (name == "Thumbnail") return St.Thumbnail;
                    break;
                case St.Objects:
                    switch (name)
                    {
                        case "Model": return St.Model;
                        case "Geometry": return St.Geometry;
                        case "AnimationCurve": return St.AnimCurve;
                        case "Deformer": return St.Deformer;
                        case "Pose": return St.Pose;
                        case "Video": return St.Video;
                        case "LayeredTexture": return St.LayeredTexture;
                        case "SelectionNode": return St.SelectionNode;
                        case "Audio": return St.Audio;
                    }
                    return St.Unknown;
                case St.Model:
                case St.Geometry:
                    if (name.Length > 0 && name[0] == 'L')
                    {
                        switch (name)
                        {
                            case "LayerElementNormal": return St.LeNormal;
                            case "LayerElementBinormal": return St.LeBinormal;
                            case "LayerElementTangent": return St.LeTangent;
                            case "LayerElementUV": return St.LeUV;
                            case "LayerElementColor": return St.LeColor;
                            case "LayerElementVertexCrease": return St.LeVertexCrease;
                            case "LayerElementEdgeCrease": return St.LeEdgeCrease;
                            case "LayerElementSmoothing": return St.LeSmoothing;
                            case "LayerElementVisibility": return St.LeVisibility;
                            case "LayerElementPolygonGroup": return St.LePolygonGroup;
                            case "LayerElementHole": return St.LeHole;
                            case "LayerElementMaterial": return St.LeMaterial;
                        }
                        if (name.StartsWith("LayerElement", StringComparison.Ordinal)) return St.LeOther;
                    }
                    if (name == "Shape") return St.Shape;
                    break;
                case St.Deformer:
                    if (name == "AssociateModel") return St.AssociateModel;
                    break;
                case St.LegacyMedia:
                    if (name == "Video") return St.LegacyVideo;
                    break;
                case St.LegacyVideo:
                    return St.Video;
                case St.LegacyModel:
                    if (name == "GeometryUVInfo") return St.GeometryUVInfo;
                    if (name == "Link") return St.LegacyLink;
                    if (name == "Channel") return St.Channel;
                    if (name == "Shape") return St.Shape;
                    break;
                case St.Pose:
                    if (name == "PoseNode") return St.PoseNode;
                    break;
                case St.Takes:
                    if (name == "Take") return St.Take;
                    break;
                case St.Take:
                    return St.TakeObject;
                case St.TakeObject:
                case St.Channel:
                    if (name == "Channel") return St.Channel;
                    break;
            }
            return St.Unknown;
        }

        /// <summary>
        /// 数の配列として値をまとめるノードか（ufbx.c 8084-8498。値を捨てる '-' の配列と文字列の配列は除く）。
        /// </summary>
        internal static bool IsNumberArray(St parent, string name)
        {
            switch (parent)
            {
                case St.Thumbnail: return name == "ImageData";
                case St.Geometry:
                case St.Model:
                    switch (name)
                    {
                        case "Vertices": case "PolygonVertexIndex": case "Edges": case "Indexes": case "Points":
                        case "KnotVector": case "KnotVectorU": case "KnotVectorV": case "PointsIndex": case "Normals":
                            return true;
                    }
                    return false;
                case St.LegacyModel:
                    return name == "Vertices" || name == "Normals" || name == "Materials" || name == "PolygonVertexIndex";
                case St.AnimCurve:
                    return name == "KeyTime" || name == "KeyValueFloat" || name == "KeyAttrFlags" || name == "KeyAttrDataFloat" || name == "KeyAttrRefCount";
                case St.LayeredTexture: return name == "BlendModes" || name == "Alphas";
                case St.SelectionNode: return name == "VertexIndexArray" || name == "EdgeIndexArray" || name == "PolygonIndexArray";
                case St.LeNormal: return name == "Normals" || name == "NormalsIndex" || name == "NormalsW";
                case St.LeBinormal: return name == "Binormals" || name == "BinormalsIndex" || name == "BinormalsW";
                case St.LeTangent: return name == "Tangents" || name == "TangentsIndex" || name == "TangentsW";
                case St.LeUV: return name == "UV" || name == "UVIndex";
                case St.LeColor: return name == "Colors" || name == "ColorIndex";
                case St.LeVertexCrease: return name == "VertexCrease" || name == "VertexCreaseIndex";
                case St.LeEdgeCrease: return name == "EdgeCrease";
                case St.LeSmoothing: return name == "Smoothing";
                case St.LeVisibility: return name == "Visibility";
                case St.LePolygonGroup: return name == "PolygonGroup";
                case St.LeHole: return name == "Hole";
                case St.LeMaterial: return name == "Materials";
                case St.LeOther: return name == "TextureId";
                case St.GeometryUVInfo: return name == "TextureUV" || name == "TextureUVVerticeIndex";
                case St.Shape: return name == "Indexes" || name == "Vertices" || name == "Normals";
                case St.Deformer:
                    switch (name)
                    {
                        case "Transform": case "TransformLink": case "Indexes": case "Weights": case "BlendWeights": case "FullWeights":
                            return true;
                    }
                    return false;
                case St.LegacyLink:
                    return name == "Transform" || name == "TransformLink" || name == "Indexes" || name == "Weights";
                case St.PoseNode: return name == "Matrix";
                case St.Channel: return name == "Key";
            }
            return false;
        }

    }

    /// <summary>プロパティの値の読み替え（バイナリの型付きの値と ASCII の long / double / string を同じに扱う）。</summary>
    public static class FbxValue
    {
        public static double ToDouble(object v)
        {
            switch (v)
            {
                case double d: return d;
                case float f:  return f;
                case long l:   return l;
                case int i:    return i;
                case short s:  return s;
                case bool b:   return b ? 1 : 0;
                case string t: return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 0;
                default:       return 0;
            }
        }

        public static long ToLong(object v)
        {
            switch (v)
            {
                case long l:   return l;
                case int i:    return i;
                case short s:  return s;
                case bool b:   return b ? 1 : 0;
                case double d: return (long)d;
                case float f:  return (long)f;
                case string t: return long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long x) ? x : 0;
                default:       return 0;
            }
        }

        public static string ToStr(object v) => v as string ?? (v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture));

        public static double[] ToDoubles(object v)
        {
            switch (v)
            {
                case double[] d: return d;
                case float[] f:  { var a = new double[f.Length]; for (int i = 0; i < a.Length; i++) a[i] = f[i]; return a; }
                case long[] l:   { var a = new double[l.Length]; for (int i = 0; i < a.Length; i++) a[i] = l[i]; return a; }
                case int[] n:    { var a = new double[n.Length]; for (int i = 0; i < a.Length; i++) a[i] = n[i]; return a; }
                case bool[] b:   { var a = new double[b.Length]; for (int i = 0; i < a.Length; i++) a[i] = b[i] ? 1 : 0; return a; }
                default:         return null;
            }
        }

        public static float[] ToFloats(object v)
        {
            switch (v)
            {
                case float[] f:  return f;
                case double[] d: { var a = new float[d.Length]; for (int i = 0; i < a.Length; i++) a[i] = (float)d[i]; return a; }
                case long[] l:   { var a = new float[l.Length]; for (int i = 0; i < a.Length; i++) a[i] = l[i]; return a; }
                case int[] n:    { var a = new float[n.Length]; for (int i = 0; i < a.Length; i++) a[i] = n[i]; return a; }
                default:         return null;
            }
        }

        public static int[] ToInts(object v)
        {
            switch (v)
            {
                case int[] n:    return n;
                case long[] l:   { var a = new int[l.Length]; for (int i = 0; i < a.Length; i++) a[i] = (int)l[i]; return a; }
                case double[] d: { var a = new int[d.Length]; for (int i = 0; i < a.Length; i++) a[i] = (int)d[i]; return a; }
                case float[] f:  { var a = new int[f.Length]; for (int i = 0; i < a.Length; i++) a[i] = (int)f[i]; return a; }
                case bool[] b:   { var a = new int[b.Length]; for (int i = 0; i < a.Length; i++) a[i] = b[i] ? 1 : 0; return a; }
                default:         return null;
            }
        }

        public static long[] ToLongs(object v)
        {
            switch (v)
            {
                case long[] l:   return l;
                case int[] n:    { var a = new long[n.Length]; for (int i = 0; i < a.Length; i++) a[i] = n[i]; return a; }
                case double[] d: { var a = new long[d.Length]; for (int i = 0; i < a.Length; i++) a[i] = (long)d[i]; return a; }
                default:         return null;
            }
        }
    }
}
