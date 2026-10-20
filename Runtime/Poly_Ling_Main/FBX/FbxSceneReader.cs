// FbxSceneReader.cs
// ============================================================
// FbxNode の木（FbxBinaryReader / FbxAsciiReader）→ シーンの表（FbxSceneModel）
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。ufbx 0.23.1 を
//   target_axes = 右手系・Y 上・前 +Z、target_unit_meters = 1、space_conversion = MODIFY_GEOMETRY、
//   generate_missing_normals = true、path_separator = '\'
// で読んだ結果と同じになるように組む（行番号は ufbx.c）。
//
//   要素の作成 14950-15124、テンプレート 12147-12214、プロパティ 11794-11928、接続 15239-15313 / 18637-18911、
//   ノードの親子と並び 18934-18986、属性と材質 21736-21840、メッシュ 13430-13807 / 12737-12922 / 13135-13213、
//   法線の生成 20366-20405 / 28715-28827 / 32509-32620、スキン 13995-14055 / 21841-21956 / 23526-23622、
//   ブレンドシェイプ 13006-13071 / 21958-22025、材質 19968-20145、テクスチャ 14551-14627 / 21370-21530 / 16486-16649、
//   ポーズ 14648-14690 / 21790-21827、テイク 15697-15726 / 23374-23391、全体の設定 23906-23934、
//   座標と単位 24954-25018 / 23679-23806、ノードの変換 22839-23045、形状の変換 21167-21334。
//
// ■ 6.x（7000 未満。段 9）
//   ID は「型::名前」（バイナリは「名前\0\1型」）の文字列そのもの。根は「Model::Scene」。Properties60（副型の列が無い）。
//   Model がメッシュ・ボーンなどを直接持つ：属性を合成し、ノードの名前の表にないプロパティを属性へ移す（14840-14946）。
//   7100 以下は Geometry（6.x は Model）の中の Shape からブレンドシェイプを合成（13073-13130）。
//   つなぎは名前で引き、ノードに無いプロパティへのつなぎとスキンの変形は属性へ移す（15252-15275・18677-18759）。
//   テイク（Takes）から AnimationStack・Layer・値・曲線を合成。Key は KeyVer ごとの並び（15326-15751）。
//   テクスチャはメッシュの LayerElementTexture から材質へ（13666-13698・22367-22466）。
//   全体の設定は Objects の中、Version5 の FrameRate（14953・15769-15815）。
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly_Ling.Fbx
{
    internal sealed class FbxSceneData
    {
        public FbxNodeEl Root;
        public readonly List<FbxElem>        Elements  = new List<FbxElem>();
        public readonly List<FbxNodeEl>      Nodes     = new List<FbxNodeEl>();
        public readonly List<FbxMeshEl>      Meshes    = new List<FbxMeshEl>();
        public readonly List<FbxMaterialEl>  Materials = new List<FbxMaterialEl>();
        public readonly List<FbxTextureEl>   Textures  = new List<FbxTextureEl>();
        public readonly List<FbxVideoEl>     Videos    = new List<FbxVideoEl>();
        public readonly List<FbxStackEl>     Stacks    = new List<FbxStackEl>();
        public readonly List<FbxLayerEl>     Layers    = new List<FbxLayerEl>();
        public readonly List<FbxValueEl>     Values    = new List<FbxValueEl>();
        public readonly List<FbxCurveEl>     Curves    = new List<FbxCurveEl>();
        public readonly List<FbxChannelEl>   Channels  = new List<FbxChannelEl>();
        public readonly List<FbxShapeEl>     Shapes    = new List<FbxShapeEl>();
        public readonly List<FbxSkinEl>      Skins     = new List<FbxSkinEl>();
        public readonly List<FbxClusterEl>   Clusters  = new List<FbxClusterEl>();
        public readonly List<FbxBlendDefEl>  Blends    = new List<FbxBlendDefEl>();
        public readonly List<FbxPoseEl>      Poses     = new List<FbxPoseEl>();

        public int    OriginalAxisUp = 6;
        public double OriginalUnitMeters;
        public double UnitMeters;
        public double FramesPerSecond;
        public double KTimeSecond = 46186158000.0;
        public double GeometryScale = 1.0;
        public DQuat  RootRotation = DQuat.Identity;
        public readonly List<string> Warnings = new List<string>();
    }

    internal static class FbxSceneReader
    {
        private const char PathSeparator = '\\';

        private static readonly double[] FpsTable =
        {
            30.0, 120.0, 100.0, 60.0, 50.0, 48.0, 30.0, 30.0, (double)29.97f, (double)29.97f,
            25.0, 24.0, 1000.0, (double)23.976f, 24.0, 96.0, 72.0, (double)59.94f,
        };

        public static FbxSceneData Build(List<FbxNode> top, bool ascii, int version, string filename, out string error)
        {
            error = null;
            var s = new FbxSceneData();
            var ctx = new Ctx { S = s, Ascii = ascii, Version = version };
            try
            {
                ctx.Run(top, filename);
            }
            catch (FbxLoadException e) { error = e.Message; return null; }
            return s;
        }

        private sealed class FbxLoadException : Exception { public FbxLoadException(string m) : base(m) { } }

        private sealed class Template { public string Type, SubType; public FbxPropSet Props; }

        private sealed class Ctx
        {
            public FbxSceneData S;
            public bool Ascii;
            public int  Version;
            private readonly List<Template> _templates = new List<Template>();
            private readonly Dictionary<long, FbxElem> _byId = new Dictionary<long, FbxElem>();
            private string _documentUrl = "";
            private string _relativeRoot = "";

            private static void Fail(string m) => throw new FbxLoadException(m);

            // ================================================================
            // 全体
            // ================================================================

            public void Run(List<FbxNode> top, string filename)
            {
                FbxNode Top(string n) => top.FirstOrDefault(x => x.Name == n);

                // 時間の刻み（ufbx.c 12017-12026）
                var hx = Top("FBXHeaderExtension");
                if (hx != null)
                {
                    long headerVer = Long(Child(hx, "FBXHeaderVersion"), 0, 0);
                    var tc = Child(Child(hx, "OtherFlags"), "TCDefinition");
                    if (Version < 8000 && headerVer >= 1004 && tc != null)
                        S.KTimeSecond = Long(tc, 0, 0) == 127 ? 46186158000.0 : 141120000.0;
                    var si = Child(hx, "SceneInfo");
                    if (si != null)
                    {
                        var sp = ReadProps(si);
                        var du = sp.Find("DocumentUrl");
                        if (du != null && du.HasStr) _documentUrl = du.Str;
                    }
                }

                // 根のノード（ufbx.c 15870-15891。7000 未満は「Model::Scene」の名前が ID）
                long rootId = 0;
                if (Version >= 7000)
                {
                    var doc = Child(Top("Documents"), "Document");
                    var rn = Child(doc, "RootNode");
                    if (rn != null) rootId = Long(rn, 0, 0);
                }
                else rootId = Sid(Ascii ? "Model::Scene" : "Scene\u0000\u0001Model");
                S.Root = new FbxNodeEl { Id = rootId, IsRoot = true, Kind = FbxKind.Node };
                AddElement(S.Root);

                var defs = Top("Definitions");
                if (defs?.Children != null)
                    foreach (var o in defs.Children)
                    {
                        if (o.Name != "ObjectType") continue;
                        var t = new Template { Type = Str(o, 0) ?? "" };
                        var pt = Child(o, "PropertyTemplate");
                        if (pt != null)
                        {
                            t.SubType = Str(pt, 0) ?? "";
                            if (t.SubType.Length > 3 && t.SubType.StartsWith("Fbx", StringComparison.Ordinal))
                            {
                                t.SubType = t.SubType.Substring(3);
                                if (t.SubType == "LODGroup") t.SubType = "LodGroup";
                            }
                            t.Props = ReadProps(pt);
                        }
                        _templates.Add(t);
                    }

                var objects = Top("Objects");
                if (objects?.Children != null)
                    foreach (var o in objects.Children) ReadObject(o);

                ReadConnections(Top("Connections"));
                if (Version < 7000) ReadLegacyTakes(Top("Takes"));
                else ReadTakes(Top("Takes"));
                ResolveConnections();
                // 全体の設定：Objects の中（6.x）を読み、最上位にあればそれで上書き（ufbx.c 14953-14956・15918-15922）
                ReadGlobalSettings(Top("GlobalSettings") ?? _objectsGlobalSettings, Child(Top("Version5"), "Settings"));

                _relativeRoot = TrimDelimiters(filename ?? "");
                Finalize();
                SpaceConversion();
                UpdateNodes();
                UpdateClusters();
            }

            private void AddElement(FbxElem e)
            {
                e.ElementId = S.Elements.Count;
                S.Elements.Add(e);
                if (!_byId.ContainsKey(e.Id)) _byId[e.Id] = e;
            }

            // ================================================================
            // 値の取り出し（ufbx.c 7725-7859）
            // ================================================================

            private static FbxNode Child(FbxNode n, string name)
            {
                if (n?.Children == null) return null;
                foreach (var c in n.Children) if (c.Name == name) return c;
                return null;
            }

            private static string Str(FbxNode n, int i) => n != null && i < n.Props.Count ? n.Props[i] as string : null;

            private static bool IsNumber(object v) => v is long || v is int || v is short || v is bool || v is double || v is float;

            /// <summary>.i の値（ufbx の get_val 'L'）。浮動小数は 0 方向へ切り捨て。</summary>
            private static long IntOf(object v)
            {
                switch (v)
                {
                    case long l: return l;
                    case int i: return i;
                    case short s: return s;
                    case bool b: return b ? 1 : 0;
                    case double d: return F64ToI64(d);
                    case float f: return F64ToI64(f);
                    default: return 0;
                }
            }

            private static long F64ToI64(double d)
            {
                if (double.IsNaN(d)) return 0;
                if (d >= 9.2233720368547758e18) return long.MaxValue;
                if (d <= -9.2233720368547758e18) return long.MinValue;
                return (long)d;
            }

            private static double RealOf(object v) => FbxValue.ToDouble(v);

            private static long Long(FbxNode n, int i, long def)
                => n != null && i < n.Props.Count && IsNumber(n.Props[i]) ? IntOf(n.Props[i]) : def;

            private static object Arr(FbxNode n, string name)
            {
                var c = Child(n, name);
                if (c == null || c.Props.Count == 0) return null;
                var v = c.Props[0];
                return v is Array && !(v is byte[]) ? v : null;
            }

            private static double[] Reals(FbxNode n, string name) => FbxValue.ToDoubles(Arr(n, name));

            private static int[] Ints(FbxNode n, string name)
            {
                var a = Arr(n, name);
                switch (a)
                {
                    case null: return null;
                    case int[] i: return i;
                    case long[] l: { var r = new int[l.Length]; for (int k = 0; k < r.Length; k++) r[k] = unchecked((int)l[k]); return r; }
                    case double[] d: { var r = new int[d.Length]; for (int k = 0; k < r.Length; k++) r[k] = F64ToI32(d[k]); return r; }
                    case float[] f: { var r = new int[f.Length]; for (int k = 0; k < r.Length; k++) r[k] = F64ToI32(f[k]); return r; }
                    case bool[] b: { var r = new int[b.Length]; for (int k = 0; k < r.Length; k++) r[k] = b[k] ? 1 : 0; return r; }
                    default: return null;
                }
            }

            private static int F64ToI32(double d)
            {
                if (double.IsNaN(d)) return 0;
                if (d >= 2147483647.0) return int.MaxValue;
                if (d <= -2147483648.0) return int.MinValue;
                return (int)d;
            }

            private static long[] Longs(FbxNode n, string name)
            {
                var a = Arr(n, name);
                switch (a)
                {
                    case null: return null;
                    case long[] l: return l;
                    case int[] i: { var r = new long[i.Length]; for (int k = 0; k < r.Length; k++) r[k] = i[k]; return r; }
                    case double[] d: { var r = new long[d.Length]; for (int k = 0; k < r.Length; k++) r[k] = F64ToI64(d[k]); return r; }
                    case float[] f: { var r = new long[f.Length]; for (int k = 0; k < r.Length; k++) r[k] = F64ToI64(f[k]); return r; }
                    default: return null;
                }
            }

            // ================================================================
            // プロパティ（ufbx.c 11794-11928）
            // ================================================================

            private FbxPropSet ReadProps(FbxNode parent)
            {
                var set = new FbxPropSet();
                // Properties70 が無ければ Properties60（6.x。副型の列が無い。ufbx.c 11794-11800・11902-11912）
                int version = 70;
                var p70 = Child(parent, "Properties70");
                if (p70 == null) { p70 = Child(parent, "Properties60"); version = 60; }
                if (p70?.Children == null) return set;
                foreach (var p in p70.Children)
                {
                    if (p.Props.Count < 2 || !(p.Props[0] is string name)) continue;
                    var prop = new FbxProp { Name = name };
                    string type = p.Props.Count > 1 ? p.Props[1] as string : null;
                    string subtype = version == 70 && p.Props.Count > 2 ? p.Props[2] as string : null;
                    prop.IsReference = type == "Reference" || (type != null && !KnownPropTypes.Contains(type) && subtype == "Reference");
                    string effType = type != null && KnownPropTypes.Contains(type) ? type : subtype != null && KnownPropTypes.Contains(subtype) ? subtype : null;
                    prop.NumberOrInteger = effType != null && NumberOrIntegerTypes.Contains(effType);
                    int fi = version == 70 ? 3 : 2;   // フラグ
                    if (fi < p.Props.Count && p.Props[fi] is string flags && flags.IndexOf('U') >= 0) prop.UserDefined = true;
                    int vi = fi + 1;
                    if (vi < p.Props.Count && IsNumber(p.Props[vi])) prop.Int = IntOf(p.Props[vi]);
                    int rc = 0;
                    while (rc < 4 && vi + rc < p.Props.Count && IsNumber(p.Props[vi + rc]))
                    {
                        prop.Real[rc] = RealOf(p.Props[vi + rc]);
                        rc++;
                    }
                    prop.RealCount = rc;
                    int si = vi;
                    if (si < p.Props.Count && !(p.Props[si] is string)) si++;
                    if (si < p.Props.Count && p.Props[si] is string sv) { prop.Str = sv; prop.HasStr = true; }
                    set.Own[name] = prop;   // 同じ名前は後のものが残る（ufbx.c 11867-11897）
                }
                return set;
            }

            private static readonly HashSet<string> NumberOrIntegerTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "Integer", "int", "enum", "Enum", "Visibility", "Visibility Inheritance", "KTime",
                "Number", "double", "Real", "Float", "Intensity",
            };

            /// <summary>ufbx.c 11432-11464 の型名。</summary>
            private static readonly HashSet<string> KnownPropTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "Boolean", "bool", "Bool", "Integer", "int", "enum", "Enum", "Visibility", "Visibility Inheritance", "KTime",
                "Number", "double", "Real", "Float", "Intensity", "Vector", "Vector3D", "Color", "ColorAndAlpha", "ColorRGB",
                "String", "KString", "object", "DateTime", "Lcl Translation", "Lcl Rotation", "Lcl Scaling", "Distance",
                "Compound", "Blob", "Reference",
            };

            private FbxPropSet FindTemplate(string type, string subType)
            {
                foreach (var t in _templates)
                {
                    if (t.Type != type) continue;
                    if (type != "Material" && type != "Model" && type != "AnimationStack" && type != "AnimationLayer"
                        && t.SubType != subType) return null;
                    return t.Props != null && t.Props.Own.Count > 0 ? t.Props : null;
                }
                return null;
            }

            // ================================================================
            // Objects（ufbx.c 14950-15124）
            // ================================================================

            // ---- ID（7000 未満は「型::名前」の文字列が ID。ufbx.c 14967-14975・12225-12265）----

            private readonly Dictionary<string, long> _sid = new Dictionary<string, long>(StringComparer.Ordinal);
            private long _synthetic = 1L << 61;
            private readonly Dictionary<long, long> _attrOf = new Dictionary<long, long>();   // ノードの ID → 合成した属性の ID（ufbx.c 12332）
            private FbxNode _objectsGlobalSettings;

            /// <summary>文字列の ID（同じ文字列は同じ ID）。</summary>
            private long Sid(string s)
            {
                if (!_sid.TryGetValue(s, out long id)) _sid[s] = id = (1L << 60) + _sid.Count;
                return id;
            }

            private long NewId() => ++_synthetic;

            /// <summary>「型::名前」（ASCII）／「名前\0\1型」（バイナリ）を分ける（ufbx.c 12267-12302）。</summary>
            private void SplitTypeAndName(string tn, out string type, out string name)
            {
                string sep = Ascii ? "::" : "\u0000\u0001";
                int k = tn.IndexOf(sep, StringComparison.Ordinal);
                if (k < 0) { type = ""; name = tn; return; }
                if (Ascii) { type = tn.Substring(0, k); name = tn.Substring(k + 2); }
                else { name = tn.Substring(0, k); type = tn.Substring(k + 2); }
            }

            private static int AttribKindOf(string sub)
            {
                switch (sub)
                {
                    case "Light": return FbxKind.Light;
                    case "Camera": return FbxKind.Camera;
                    case "LimbNode": case "Limb": case "Root": return FbxKind.Bone;
                    case "Null": case "Marker": return FbxKind.Empty;
                    case "CameraStereo": return FbxKind.StereoCamera;
                    case "CameraSwitcher": return FbxKind.CameraSwitcher;
                    case "FKEffector": case "IKEffector": return FbxKind.Marker;
                    case "LodGroup": return FbxKind.LodGroup;
                    default: return FbxKind.Unknown;
                }
            }

            /// <summary>要素の共通の値を入れて表へ足す（ufbxi_push_element）。</summary>
            private void Register(FbxElem e, long id, string name, string sub, string cls, FbxNode o, FbxPropSet props)
            {
                e.Id = id;
                e.Name = name ?? "";
                e.SubType = sub ?? "";
                e.Class = cls ?? "";
                e.Node = o;
                e.Props = props ?? new FbxPropSet();
                AddElement(e);

                switch (e)
                {
                    case FbxNodeEl n: S.Nodes.Add(n); break;
                    case FbxMeshEl m: m.TypedId = S.Meshes.Count; S.Meshes.Add(m); break;
                    case FbxMaterialEl m: m.TypedId = S.Materials.Count; S.Materials.Add(m); break;
                    case FbxTextureEl t: t.TypedId = S.Textures.Count; S.Textures.Add(t); break;
                    case FbxVideoEl v: v.TypedId = S.Videos.Count; S.Videos.Add(v); break;
                    case FbxStackEl st: st.TypedId = S.Stacks.Count; S.Stacks.Add(st); break;
                    case FbxLayerEl l: S.Layers.Add(l); break;
                    case FbxValueEl v: S.Values.Add(v); break;
                    case FbxCurveEl c: S.Curves.Add(c); break;
                    case FbxChannelEl c: c.TypedId = S.Channels.Count; S.Channels.Add(c); break;
                    case FbxShapeEl sh: S.Shapes.Add(sh); break;
                    case FbxSkinEl sk: S.Skins.Add(sk); break;
                    case FbxClusterEl cl: S.Clusters.Add(cl); break;
                    case FbxBlendDefEl b: S.Blends.Add(b); break;
                    case FbxPoseEl p: S.Poses.Add(p); break;
                }
            }

            private void ReadObject(FbxNode o)
            {
                if (o.Name == "GlobalSettings") { _objectsGlobalSettings = o; return; }   // 6.x は Objects の中（ufbx.c 14953-14956）

                long id;
                string tn, sub;
                if (Version >= 7000)
                {
                    if (o.Props.Count < 3 || !(o.Props[0] is long || o.Props[0] is int) || !(o.Props[1] is string t1) || !(o.Props[2] is string s1)) return;
                    id = IntOf(o.Props[0]); tn = t1; sub = s1;
                }
                else
                {
                    if (o.Props.Count < 2 || !(o.Props[0] is string t0) || !(o.Props[1] is string s0)) return;
                    tn = t0; sub = s0; id = Sid(tn);
                }
                if (sub.Length > 3 && sub.StartsWith("Fbx", StringComparison.Ordinal)) sub = sub.Substring(3);
                SplitTypeAndName(tn, out string typeStr, out string name);

                var props = ReadProps(o);
                props.Defaults = FindTemplate(o.Name, sub);

                FbxElem e = null;
                switch (o.Name)
                {
                    case "Model":
                    {
                        if (Version < 7000) ReadSyntheticAttribute(o, id, name, typeStr, sub, props);
                        var n = new FbxNodeEl { Kind = FbxKind.Node };
                        long it = props.FindInt("InheritType", -1);
                        n.InheritMode = it == 0 ? 2 : it == 2 ? 1 : 0;
                        e = n;
                        break;
                    }
                    case "NodeAttribute":
                        e = new FbxElem { Kind = AttribKindOf(sub) };
                        break;
                    case "Geometry":
                        switch (sub)
                        {
                            case "Mesh":
                            {
                                var m = ReadMesh(o);
                                Register(m, id, name, sub, o.Name, o, props);
                                if (Version <= 7100) ReadSyntheticBlendShapes(o, m);   // ufbx.c 13436-13438
                                return;
                            }
                            case "Shape": e = ReadShape(o); if (e == null) return; break;
                            case "NurbsCurve": e = new FbxElem { Kind = FbxKind.NurbsCurve }; break;
                            case "NurbsSurface": e = new FbxElem { Kind = FbxKind.NurbsSurface }; break;
                            case "Line": e = new FbxElem { Kind = FbxKind.LineCurve }; break;
                            case "TrimNurbsSurface": e = new FbxElem { Kind = FbxKind.TrimSurface }; break;
                            case "Boundary": e = new FbxElem { Kind = FbxKind.TrimBoundary }; break;
                            default: e = new FbxElem { Kind = FbxKind.Unknown }; break;
                        }
                        break;
                    case "Deformer":
                        switch (sub)
                        {
                            case "Skin": e = new FbxSkinEl { Kind = FbxKind.Skin }; break;
                            case "Cluster": e = ReadCluster(o); break;
                            case "BlendShape": e = new FbxBlendDefEl { Kind = FbxKind.BlendDeformer }; break;
                            case "BlendShapeChannel":
                                e = new FbxChannelEl { Kind = FbxKind.BlendChannel, FullWeights = Reals(o, "FullWeights") ?? Array.Empty<double>() };
                                break;
                            case "VertexCacheDeformer": e = new FbxElem { Kind = FbxKind.CacheDeformer }; break;
                            default: e = new FbxElem { Kind = FbxKind.Unknown }; break;
                        }
                        break;
                    case "Material": e = new FbxMaterialEl { Kind = FbxKind.Material }; break;
                    case "Texture":
                    {
                        var t = new FbxTextureEl { Kind = FbxKind.Texture };
                        ReadFilenames(o, out t.AbsoluteFilename, out t.RelativeFilename);
                        e = t;
                        break;
                    }
                    case "LayeredTexture": e = new FbxTextureEl { Kind = FbxKind.Texture, Layered = true }; break;
                    case "Video":
                    {
                        var v = new FbxVideoEl { Kind = FbxKind.Video };
                        ReadFilenames(o, out v.AbsoluteFilename, out v.RelativeFilename);
                        v.Content = ReadContent(Child(o, "Content"));
                        e = v;
                        break;
                    }
                    case "AnimationStack": e = new FbxStackEl { Kind = FbxKind.AnimStack }; break;
                    case "AnimationLayer": e = new FbxLayerEl { Kind = FbxKind.AnimLayer }; break;
                    case "AnimationCurveNode": e = new FbxValueEl { Kind = FbxKind.AnimValue }; break;
                    case "AnimationCurve": e = ReadCurve(o); break;
                    case "Pose": e = ReadPose(o, sub); break;
                    case "Collection":
                    case "CollectionExclusive":
                    case "SceneInfo":
                        return;   // 要素を作らない（使わない種類）
                    default: e = new FbxElem { Kind = FbxKind.Unknown }; break;
                }
                Register(e, id, name, sub, o.Name, o, props);
            }

            // ---- 6.x の Model の中の属性（ufbx.c 14840-14946）----

            private static readonly HashSet<string> NodePropNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "AxisLen", "DefaultAttributeIndex", "Freeze", "GeometricRotation", "GeometricScaling", "GeometricTranslation",
                "InheritType", "LODBox", "Lcl Rotation", "Lcl Scaling", "Lcl Translation", "LookAtProperty",
                "MaxDampRangeX", "MaxDampRangeY", "MaxDampRangeZ", "MaxDampStrengthX", "MaxDampStrengthY", "MaxDampStrengthZ",
                "MinDampRangeX", "MinDampRangeY", "MinDampRangeZ", "MinDampStrengthX", "MinDampStrengthY", "MinDampStrengthZ",
                "NegativePercentShapeSupport", "PostRotation", "PreRotation", "PreferedAngleX", "PreferedAngleY", "PreferedAngleZ",
                "QuaternionInterpolate", "RotationActive", "RotationMax", "RotationMaxX", "RotationMaxY", "RotationMaxZ",
                "RotationMin", "RotationMinX", "RotationMinY", "RotationMinZ", "RotationOffset", "RotationOrder", "RotationPivot",
                "RotationSpaceForLimitOnly", "RotationStiffnessX", "RotationStiffnessY", "RotationStiffnessZ",
                "ScalingActive", "ScalingMax", "ScalingMaxX", "ScalingMaxY", "ScalingMaxZ",
                "ScalingMin", "ScalingMinX", "ScalingMinY", "ScalingMinZ", "ScalingOffset", "ScalingPivot", "Show",
                "TranslationActive", "TranslationMax", "TranslationMaxX", "TranslationMaxY", "TranslationMaxZ",
                "TranslationMin", "TranslationMinX", "TranslationMinY", "TranslationMinZ", "UpVectorProperty",
                "Visibility Inheritance", "Visibility", "notes",
            };   // ufbx.c 11641-11714

            private void ReadSyntheticAttribute(FbxNode o, long nodeId, string nodeName, string typeStr, string sub, FbxPropSet props)
            {
                // サブ型が空でも Vertices と PolygonVertexIndex があればメッシュ
                if (sub.Length == 0 && Child(o, "Vertices") != null && Child(o, "PolygonVertexIndex") != null) sub = "Mesh";
                if ((sub.Length == 0 || sub == "Model") && typeStr == "Model") return;   // ただのノード

                long attrId = NewId();
                string attrName = nodeName;
                var nan = Child(o, "NodeAttributeName");
                if (nan != null && Str(nan, 0) is string atn)
                {
                    SplitTypeAndName(atn, out _, out string an);
                    if (an.Length > 0)
                    {
                        attrName = an;
                        long cand = Sid(atn);
                        if (cand != nodeId && !_byId.ContainsKey(cand)) attrId = cand;
                    }
                }
                if (!_attrOf.ContainsKey(nodeId)) _attrOf[nodeId] = attrId;

                // プロパティをノードと属性に分ける（ノードの名前の表にあるもの・利用者の定義はノード）
                var attrProps = new FbxPropSet { Defaults = props.Defaults };
                foreach (var kv in props.Own.ToList())
                {
                    if (NodePropNames.Contains(kv.Key) || kv.Value.UserDefined) continue;
                    attrProps.Own[kv.Key] = kv.Value;
                    props.Own.Remove(kv.Key);
                }

                FbxElem a;
                switch (sub)
                {
                    case "Mesh":
                    {
                        var m = ReadMesh(o);
                        Register(m, attrId, attrName, sub, "Model", o, attrProps);
                        if (Version <= 7100) ReadSyntheticBlendShapes(o, m);
                        _pending.Add((attrId, nodeId, "", ""));
                        return;
                    }
                    case "NurbsCurve":
                        if (Child(o, "KnotVector") == null) return;
                        a = new FbxElem { Kind = FbxKind.NurbsCurve }; break;
                    case "NurbsSurface":
                        if (Child(o, "KnotVectorU") == null || Child(o, "KnotVectorV") == null) return;
                        a = new FbxElem { Kind = FbxKind.NurbsSurface }; break;
                    case "Line":
                        if (Child(o, "Points") == null || Child(o, "PointsIndex") == null) return;
                        a = new FbxElem { Kind = FbxKind.LineCurve }; break;
                    case "TrimNurbsSurface":
                        if (Child(o, "Layer") == null) return;
                        a = new FbxElem { Kind = FbxKind.TrimSurface }; break;
                    case "Boundary": a = new FbxElem { Kind = FbxKind.TrimBoundary }; break;
                    default: a = new FbxElem { Kind = AttribKindOf(sub) }; break;
                }
                Register(a, attrId, attrName, sub, "Model", o, attrProps);
                _pending.Add((attrId, nodeId, "", ""));
            }

            // ---- 7100 以下：Geometry の中の Shape からブレンドシェイプ（ufbx.c 13073-13130）----

            private void ReadSyntheticBlendShapes(FbxNode o, FbxMeshEl mesh)
            {
                if (o.Children == null) return;
                long deformerId = 0;
                foreach (var n in o.Children)
                {
                    if (n.Name != "Shape") continue;
                    string name = Str(n, 0); if (name == null) Fail("Shape の名前がありません");

                    if (deformerId == 0)
                    {
                        deformerId = NewId();
                        Register(new FbxBlendDefEl { Kind = FbxKind.BlendDeformer }, deformerId, name, "", "Deformer", n, null);
                        _pending.Add((deformerId, mesh.Id, "", ""));
                    }

                    long channelId = NewId();
                    var cprops = new FbxPropSet();
                    var dp = new FbxProp { Name = "DeformPercent", NumberOrInteger = true };
                    cprops.Own["DeformPercent"] = dp;
                    var self = mesh.Props.Find(name);
                    if (self != null && self.NumberOrInteger)
                    {
                        dp.Real[0] = self.Real[0];
                        _pending.Add((mesh.Id, channelId, name, "DeformPercent"));
                    }
                    Register(new FbxChannelEl { Kind = FbxKind.BlendChannel }, channelId, name, "", "Deformer", n, cprops);

                    long shapeId = NewId();
                    var sh = ReadShape(n);
                    if (sh != null) Register(sh, shapeId, name, "", "Geometry", n, null);

                    _pending.Add((channelId, deformerId, "", ""));
                    _pending.Add((shapeId, channelId, "", ""));
                }
            }

            private static void ReadFilenames(FbxNode o, out string abs, out string rel)
            {
                abs = Str(Child(o, "FileName"), 0) ?? "";
                abs = Str(Child(o, "Filename"), 0) ?? abs;
                rel = Str(Child(o, "RelativeFileName"), 0) ?? "";
                rel = Str(Child(o, "RelativeFilename"), 0) ?? rel;
            }

            /// <summary>Content（ufbx.c 11760-11792）。バイナリは R の列をつなぐ。ASCII は文字列ごとに base64 を戻してつなぐ。</summary>
            private byte[] ReadContent(FbxNode n)
            {
                if (n == null || n.Props.Count == 0) return Array.Empty<byte>();
                var parts = new List<byte[]>();
                foreach (var v in n.Props)
                {
                    if (v is byte[] b) parts.Add(b);
                    else if (v is string str)
                    {
                        if (!Ascii) { parts.Add(Encoding.UTF8.GetBytes(str)); continue; }
                        var d = DecodeBase64(str);
                        if (d == null)
                        {
                            S.Warnings.Add("埋め込みデータの base64 が壊れています");
                            return Array.Empty<byte>();
                        }
                        parts.Add(d);
                    }
                    else if (Ascii) Fail("埋め込みデータ（Content）に数があります");
                }
                if (parts.Count == 1) return parts[0];
                var all = new byte[parts.Sum(p => p.Length)];
                int o2 = 0;
                foreach (var p in parts) { Buffer.BlockCopy(p, 0, all, o2, p.Length); o2 += p.Length; }
                return all;
            }

            private static byte[] DecodeBase64(string s)
            {
                if (s.Length % 4 != 0) return null;
                try { return Convert.FromBase64String(s); }
                catch (FormatException) { return null; }
            }

            // ---- メッシュ（ufbx.c 13430-13807）----

            private FbxMeshEl ReadMesh(FbxNode o)
            {
                var m = new FbxMeshEl { Kind = FbxKind.Mesh };
                var vn = Child(o, "Vertices");
                if (vn == null) return m;
                var verts = Reals(o, "Vertices") ?? Array.Empty<double>();
                if (verts.Length % 3 != 0) Fail("Vertices の数が 3 の倍数ではありません");
                int nv = verts.Length / 3;
                m.Vertices = new DVec3[nv];
                for (int i = 0; i < nv; i++) m.Vertices[i] = new DVec3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);

                var pviNode = Child(o, "PolygonVertexIndex");
                int[] idx = pviNode != null ? Ints(o, "PolygonVertexIndex") : Array.Empty<int>();
                if (pviNode != null && idx == null) Fail("PolygonVertexIndex が配列ではありません");
                idx = (int[])idx.Clone();
                int ni = idx.Length;
                if (ni > 0 && idx[ni - 1] >= 0) idx[ni - 1] = ~idx[ni - 1];

                // 辺（負の値を戻す前に読む）
                var edges = Ints(o, "Edges");
                if (edges != null)
                {
                    var list = new List<(int, int)>();
                    foreach (int e0 in edges)
                    {
                        int ix = e0;
                        if ((uint)ix >= (uint)ni) continue;
                        int a = ix;
                        if (idx[ix] < 0) { while (ix > 0 && idx[ix - 1] >= 0) ix--; }
                        else ix++;
                        if (ix >= ni) Fail("Edges の値が範囲外です");
                        list.Add((a, ix));
                    }
                    m.Edges = list.ToArray();
                }

                // 面
                var faces = new List<FbxFace>();
                int begin = 0;
                for (int i = 0; i < ni; i++)
                {
                    int v = idx[i];
                    if (v < 0)
                    {
                        v = ~v; idx[i] = v;
                        faces.Add(new FbxFace { Begin = begin, Count = i - begin + 1 });
                        begin = i + 1;
                    }
                    if ((uint)v >= (uint)nv) Fail("面の頂点番号が範囲外です");
                }
                m.VertexIndices = idx;
                m.Faces = faces.ToArray();
                m.VertexFirstIndex = Enumerable.Repeat(-1, nv).ToArray();
                for (int i = 0; i < ni; i++) if (m.VertexFirstIndex[idx[i]] < 0) m.VertexFirstIndex[idx[i]] = i;

                // レイヤ要素
                var uvSets = new List<(long index, double[] values, int[] indices)>();
                if (o.Children != null)
                    foreach (var n in o.Children)
                    {
                        switch (n.Name)
                        {
                            case "LayerElementNormal":
                                if (m.NormalIndices != null) break;
                                if (ReadVertexElement(m, n, "Normals", "NormalsIndex", 3, out var nvals, out var nidx))
                                {
                                    m.NormalValues = new DVec3[nvals.Length / 3];
                                    for (int i = 0; i < m.NormalValues.Length; i++)
                                        m.NormalValues[i] = new DVec3(nvals[i * 3], nvals[i * 3 + 1], nvals[i * 3 + 2]);
                                    m.NormalIndices = nidx;
                                }
                                break;
                            case "LayerElementUV":
                                if (ReadVertexElement(m, n, "UV", "UVIndex", 2, out var uv, out var uvi))
                                    uvSets.Add((Long(n, 0, 0), uv, uvi));
                                break;
                            case "LayerElementSmoothing":
                            {
                                string map = Str(Child(n, "MappingInformationType"), 0) ?? "";
                                if (map == "ByEdge")
                                {
                                    if (m.EdgeSmoothing != null) break;
                                    m.EdgeSmoothing = TruncatedBools(n, "Smoothing", m.Edges?.Length ?? 0);
                                }
                                else if (map == "ByPolygon")
                                {
                                    if (m.FaceSmoothing != null) break;
                                    m.FaceSmoothing = TruncatedBools(n, "Smoothing", m.Faces.Length);
                                }
                                break;
                            }
                            case "LayerElementMaterial":
                            {
                                if (m.FaceMaterial != null) break;
                                string map = Str(Child(n, "MappingInformationType"), 0) ?? "";
                                if (map == "ByPolygon")
                                {
                                    var mats = Ints(n, "Materials");
                                    if (mats == null) { S.Warnings.Add("Materials がありません"); break; }
                                    var fm = new int[m.Faces.Length];
                                    for (int i = 0; i < fm.Length; i++)
                                        fm[i] = i < mats.Length ? mats[i] : mats.Length > 0 ? mats[mats.Length - 1] : 0;
                                    m.FaceMaterial = fm;
                                }
                                else if (map == "AllSame")
                                {
                                    var mats = Ints(n, "Materials");
                                    if (mats == null || mats.Length < 1) Fail("Materials（AllSame）がありません");
                                    m.FaceMaterial = Enumerable.Repeat(mats[0], m.Faces.Length).ToArray();
                                }
                                break;
                            }
                            default:
                            {
                                // 6.x はテクスチャを形状の層に持つ（LayerElementTexture、LayerElement…Textures。ufbx.c 13666-13698）
                                if (!n.Name.StartsWith("LayerElement", StringComparison.Ordinal)) break;
                                string prop = null;
                                if (n.Name.Length > 20 && n.Name.EndsWith("Textures", StringComparison.Ordinal))
                                {
                                    prop = n.Name.Substring(12, n.Name.Length - 20);
                                    if (prop.EndsWith("_", StringComparison.Ordinal)) prop = prop.Substring(0, prop.Length - 1);
                                }
                                else if (n.Name == "LayerElementTexture") prop = "Diffuse";
                                if (prop == null || prop.Length == 0) break;
                                if (!(Str(Child(n, "MappingInformationType"), 0) is string tmap)) break;
                                m.LegacyTextures.Add((prop, Ints(n, "TextureId"), tmap == "AllSame"));
                                break;
                            }
                        }
                    }
                if (m.FaceMaterial == null) m.FaceMaterial = new int[m.Faces.Length];

                if (uvSets.Count > 0)
                {
                    var first = uvSets.Select((u, i) => (u, i)).OrderBy(x => x.u.index).ThenBy(x => x.i).First().u;
                    m.UvValues = first.values;
                    m.UvIndices = first.indices;
                }
                return m;
            }

            private bool[] TruncatedBools(FbxNode n, string name, int count)
            {
                var a = Ints(n, name);
                if (a == null) { S.Warnings.Add($"{name} がありません"); return null; }
                var r = new bool[count];
                for (int i = 0; i < count; i++) r[i] = (i < a.Length ? a[i] : a.Length > 0 ? a[a.Length - 1] : 0) != 0;
                return r;
            }

            /// <summary>ufbx.c 12737-12922。値の配列と、角ごとの値の番号（範囲外は最後の値へ寄せる）。</summary>
            private bool ReadVertexElement(FbxMeshEl m, FbxNode n, string dataName, string indexName, int comps,
                                           out double[] values, out int[] cornerIdx)
            {
                values = Reals(n, dataName);
                cornerIdx = null;
                if (values == null) return false;
                if (values.Length % comps != 0) Fail($"{dataName} の数が {comps} の倍数ではありません");
                int ne = values.Length / comps;
                if (ne == 0) return false;
                int ni = m.VertexIndices.Length;
                var index = Ints(n, indexName);
                string map = Str(Child(n, "MappingInformationType"), 0) ?? "";
                if (map == "ByPolygon")
                {
                    int cnt = index != null ? index.Length : ne;
                    if (cnt == ni) map = "ByPolygonVertex";
                }
                var r = new int[ni];
                if (map == "ByPolygonVertex")
                {
                    for (int i = 0; i < ni; i++) r[i] = index != null ? (i < index.Length ? index[i] : -1) : i;
                }
                else if (map == "ByVertex" || map == "ByVertice")
                {
                    for (int i = 0; i < ni; i++)
                    {
                        int v = m.VertexIndices[i];
                        r[i] = index != null ? ((uint)v < (uint)index.Length ? index[v] : ne - 1) : v;
                    }
                }
                else if (map == "ByPolygon")
                {
                    for (int f = 0; f < m.Faces.Length; f++)
                    {
                        int k = index != null ? (f < index.Length ? index[f] : -1) : f;
                        if ((uint)k >= (uint)ne) k = ne - 1;
                        for (int c = 0; c < m.Faces[f].Count; c++) r[m.Faces[f].Begin + c] = k;
                    }
                }
                else if (map == "AllSame")
                {
                    // 全部 0
                }
                else
                {
                    S.Warnings.Add($"{dataName} の対応づけ「{map}」は使えないので読み飛ばしました");
                    values = null;
                    return false;
                }
                for (int i = 0; i < ni; i++) if ((uint)r[i] >= (uint)ne) r[i] = ne - 1;
                cornerIdx = r;
                return true;
            }

            // ---- ブレンドシェイプの形（ufbx.c 13006-13071）----

            private FbxShapeEl ReadShape(FbxNode o)
            {
                var verts = Reals(o, "Vertices");
                var idx = Ints(o, "Indexes");
                if (verts == null || idx == null) return null;
                if (verts.Length % 3 != 0) Fail("Shape の Vertices の数が 3 の倍数ではありません");
                if (idx.Length != verts.Length / 3) Fail("Shape の Indexes と Vertices の数が合いません");
                if (Child(o, "Normals") != null)
                {
                    var nrm = Reals(o, "Normals");
                    if (nrm == null || nrm.Length != verts.Length) Fail("Shape の Normals と Vertices の数が合いません");
                }
                var sh = new FbxShapeEl { Kind = FbxKind.BlendShape };
                var order = Enumerable.Range(0, idx.Length).ToArray();
                bool sorted = true;
                for (int i = 1; i < idx.Length; i++) if ((uint)idx[i] < (uint)idx[i - 1]) { sorted = false; break; }
                if (!sorted) order = order.OrderBy(i => (uint)idx[i]).ToArray();   // 安定
                sh.Indexes = order.Select(i => idx[i]).ToArray();
                sh.Offsets = order.Select(i => new DVec3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2])).ToArray();
                return sh;
            }

            // ---- スキンのクラスタ（ufbx.c 13995-14055）----

            private FbxClusterEl ReadCluster(FbxNode o)
            {
                var c = new FbxClusterEl { Kind = FbxKind.Cluster };
                var idx = Ints(o, "Indexes");
                var w = Reals(o, "Weights");
                if (idx != null && w != null && idx.Length != w.Length) Fail("クラスタの Indexes と Weights の数が合いません");
                if (idx != null && w != null) { c.Indexes = idx; c.WeightValues = w; }
                var tr = Reals(o, "Transform");
                var tl = Reals(o, "TransformLink");
                if (tr != null && tl != null && tr.Length >= 16 && tl.Length >= 16)
                {
                    c.MeshNodeToBone = DMat.FromColumnMajor16(tr);
                    c.BindToWorld = DMat.FromColumnMajor16(tl);
                }
                return c;
            }

            // ---- 曲線（ufbx.c 14260-14535）----

            private FbxCurveEl ReadCurve(FbxNode o)
            {
                var c = new FbxCurveEl { Kind = FbxKind.AnimCurve };
                ReadExtrapolation(Child(o, "Pre-Extrapolation"), out c.PreMode, out c.PreRepeat);
                ReadExtrapolation(Child(o, "Post-Extrapolation"), out c.PostMode, out c.PostRepeat);

                var times = Longs(o, "KeyTime");
                var values = Reals(o, "KeyValueFloat");
                var flags = Ints(o, "KeyAttrFlags");
                var attrs = AttrFloats(Arr(o, "KeyAttrDataFloat"));
                var refs = Ints(o, "KeyAttrRefCount");
                if (times == null || values == null || flags == null || attrs == null || refs == null)
                    Fail("AnimationCurve のキーの配列が足りません");
                if (times.Length != values.Length || flags.Length != refs.Length || attrs.Length != refs.Length * 4)
                    Fail("AnimationCurve のキーの配列の数が合いません");

                int nk = times.Length;
                var keys = new FbxKey[nk];
                float slopeLeft = 0f, weightLeft = 0.333333f;
                double prevTime = 0.0, nextTime = 0.0;
                int pf = 0, pr = 0, refsLeft = 0;
                if (nk > 0)
                {
                    nextTime = times[0] / S.KTimeSecond;
                    if (pr < refs.Length) refsLeft = refs[pr];
                }
                for (int i = 0; i < nk; i++)
                {
                    if (refsLeft <= 0) Fail("AnimationCurve の KeyAttrRefCount が足りません");
                    ref FbxKey key = ref keys[i];
                    double value = values[i];
                    key.Time = nextTime;
                    key.Value = value;
                    if (i + 1 < nk) nextTime = times[i + 1] / S.KTimeSecond;

                    uint fl = (uint)flags[pf];
                    float slopeRight = attrs[pf * 4 + 0];
                    float weightRight = 0.333333f;
                    float nextSlopeLeft = attrs[pf * 4 + 1];
                    float nextWeightLeft = 0.333333f;
                    if ((fl & (0x1000000u | 0x2000000u)) != 0)
                    {
                        uint packed = (uint)BitConverter.ToInt32(BitConverter.GetBytes(attrs[pf * 4 + 2]), 0);
                        if ((fl & 0x1000000u) != 0) weightRight = (float)(packed & 0xffff) * 0.0001f;
                        if ((fl & 0x2000000u) != 0) nextWeightLeft = (float)(packed >> 16) * 0.0001f;
                    }

                    if ((fl & 0x2u) != 0)
                    {
                        key.Interp = (fl & 0x100u) != 0 ? FbxInterp.ConstantNext : FbxInterp.ConstantPrev;
                        weightRight = nextWeightLeft = 0.333333f;
                        slopeRight = nextSlopeLeft = 0f;
                    }
                    else if ((fl & 0x8u) != 0)
                    {
                        key.Interp = FbxInterp.Cubic;
                        if ((fl & 0x200u) != 0)
                        {
                            double tl = 0.0, tr = 0.0;
                            bool edge = false;
                            if (i > 0 && key.Time > prevTime) tl = (key.Value - values[i - 1]) / (key.Time - prevTime); else edge = true;
                            if (i + 1 < nk && nextTime > key.Time) tr = (values[i + 1] - key.Value) / (nextTime - key.Time); else edge = true;
                            SolveTcb(out slopeLeft, out slopeRight, attrs[pf * 4 + 0], attrs[pf * 4 + 1], attrs[pf * 4 + 2], tl, tr, edge);
                            nextSlopeLeft = 0f;
                            nextWeightLeft = 0.333333f;
                        }
                        else if ((fl & 0x400u) != 0)
                        {
                            // ユーザーの接線：そのまま
                        }
                        else
                        {
                            if (i > 0 && i + 1 < nk && key.Time > prevTime && nextTime > key.Time)
                            {
                                if (Math.Abs(slopeLeft + slopeRight) <= 0.0001f)
                                {
                                    slopeLeft = slopeRight = SolveAuto(prevTime, key.Time, nextTime, values[i - 1], key.Value, values[i + 1],
                                                                       weightLeft, weightRight, slopeRight, fl);
                                }
                                else
                                {
                                    float sl = SolveAuto(prevTime, key.Time, nextTime, values[i - 1], key.Value, values[i + 1],
                                                         weightLeft, weightRight, -slopeLeft, fl);
                                    float sr = SolveAuto(prevTime, key.Time, nextTime, values[i - 1], key.Value, values[i + 1],
                                                         weightLeft, weightRight, slopeRight, fl);
                                    slopeLeft = sl; slopeRight = sr;
                                }
                            }
                            else if (i > 0 && key.Time > prevTime)
                                slopeLeft = slopeRight = SolveAutoLeft(prevTime, key.Time, values[i - 1], key.Value, -slopeLeft, fl);
                            else if (i + 1 < nk && nextTime > key.Time)
                                slopeLeft = slopeRight = SolveAutoRight(key.Time, nextTime, key.Value, values[i + 1], slopeRight, fl);
                            else
                                slopeLeft = slopeRight = 0f;
                        }
                    }
                    else
                    {
                        key.Interp = FbxInterp.Linear;
                        weightRight = 0.333333f;
                        nextWeightLeft = 0.333333f;
                        if (nextTime > key.Time)
                        {
                            double dt = nextTime - key.Time;
                            if (dt > 0.0) slopeRight = nextSlopeLeft = (float)((values[i + 1] - key.Value) / dt);
                            else slopeRight = nextSlopeLeft = 0f;
                        }
                        else slopeRight = nextSlopeLeft = 0f;
                    }

                    if (key.Time > prevTime)
                    {
                        double d = key.Time - prevTime;
                        key.LeftDx = (float)(weightLeft * d);
                        key.LeftDy = key.LeftDx * slopeLeft;
                    }
                    else { key.LeftDx = 0f; key.LeftDy = 0f; }
                    if (nextTime > key.Time)
                    {
                        double d = nextTime - key.Time;
                        key.RightDx = (float)(weightRight * d);
                        key.RightDy = key.RightDx * slopeRight;
                    }
                    else { key.RightDx = 0f; key.RightDy = 0f; }

                    slopeLeft = nextSlopeLeft;
                    weightLeft = nextWeightLeft;
                    prevTime = key.Time;
                    if (--refsLeft == 0)
                    {
                        pf++; pr++;
                        if (pr < refs.Length) refsLeft = refs[pr];
                    }
                }
                c.Keys = keys;
                if (nk > 0) { c.MinTime = keys[0].Time; c.MaxTime = keys[nk - 1].Time; }
                return c;
            }

            /// <summary>KeyAttrDataFloat。バイナリは float[]。ASCII は版 7200 以上なら整数のビット列、未満なら小数（ufbx.c 8180-8187）。</summary>
            private float[] AttrFloats(object a)
            {
                switch (a)
                {
                    case null: return null;
                    case float[] f: return f;
                    case long[] l:
                    {
                        var r = new float[l.Length];
                        for (int i = 0; i < r.Length; i++)
                            r[i] = Version >= 7200 ? BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)l[i])), 0) : (float)l[i];
                        return r;
                    }
                    case double[] d:
                    {
                        var r = new float[d.Length];
                        for (int i = 0; i < r.Length; i++)
                            r[i] = Version >= 7200 ? BitConverter.ToSingle(BitConverter.GetBytes(F64ToI32(d[i])), 0) : (float)d[i];
                        return r;
                    }
                    case int[] n:
                    {
                        var r = new float[n.Length];
                        for (int i = 0; i < r.Length; i++) r[i] = BitConverter.ToSingle(BitConverter.GetBytes(n[i]), 0);
                        return r;
                    }
                    default: return null;
                }
            }

            private static void ReadExtrapolation(FbxNode n, out int mode, out int repeat)
            {
                mode = 0; repeat = -1;
                if (n == null) return;
                var t = Child(n, "Type");
                if (t == null || t.Props.Count == 0 || !IsNumber(t.Props[0])) return;
                switch ((int)IntOf(t.Props[0]))
                {
                    case 'A': mode = 4; break;
                    case 'C': mode = 0; break;
                    case 'K': mode = 3; break;
                    case 'M': mode = 2; break;
                    case 'R': mode = 1; break;
                }
                var r = Child(n, "Repetition");
                if (r != null && r.Props.Count > 0 && IsNumber(r.Props[0]))
                {
                    repeat = (int)IntOf(r.Props[0]);
                    if (repeat < 0) repeat = -1;
                }
            }

            private const double KeyClampThreshold = 0.0;

            private static float SolveAuto(double prevTime, double time, double nextTime, double prevValue, double value, double nextValue,
                                           float weightLeft, float weightRight, float autoBias, uint flags)
            {
                if ((flags & 0x1000u) != 0)
                    if (Math.Min(Math.Abs(prevValue - value), Math.Abs(nextValue - value)) <= KeyClampThreshold) return 0f;
                double slope = (nextValue - prevValue) / (nextTime - prevTime);
                if ((flags & 0x2000u) == 0)
                {
                    double sl = (value - prevValue) / (time - prevTime);
                    double sr = (nextValue - value) / (nextTime - time);
                    double delta = (time - prevTime) / (nextTime - prevTime);
                    slope = slope * 0.5 + (sl * (1.0 - delta) + sr * delta) * 0.5;
                    double bw = Math.Abs(autoBias) / 100.0;
                    if (bw > 0.0001)
                    {
                        double target = autoBias > 0.0 ? sr : sl;
                        double bd = target - slope;
                        slope = slope * (1.0 - bw) + target * bw;
                        double abw = bw - 5.0;
                        if (abw > 0.0)
                        {
                            double sign = Math.Abs(bd) > 0.00001 ? bd : autoBias;
                            sign = sign > 0.0 ? 1.0 : -1.0;
                            slope += abw * abw * sign * 40.0;
                        }
                    }
                }
                if ((flags & 0x4000u) != 0)
                {
                    double ss = slope >= 0.0 ? 1.0 : -1.0;
                    double abs = ss * slope;
                    double rl = weightLeft * (time - prevTime);
                    double rr = weightRight * (nextTime - time);
                    double ml = rl > 0.0 ? ss * (value - prevValue) / rl : 0.0;
                    double mr = rr > 0.0 ? ss * (nextValue - value) / rr : 0.0;
                    if (!(ml > 0.0)) ml = 0.0;
                    if (!(mr > 0.0)) mr = 0.0;
                    if (abs > ml) abs = ml;
                    if (abs > mr) abs = mr;
                    slope = ss * abs;
                }
                return (float)slope;
            }

            private static float SolveAutoLeft(double prevTime, double time, double prevValue, double value, float autoBias, uint flags)
            {
                if ((flags & 0x4000u) != 0) return 0f;
                if ((flags & 0x1000u) != 0 && Math.Abs(prevValue - value) <= KeyClampThreshold) return 0f;
                double slope = (value - prevValue) / (time - prevTime);
                if ((flags & 0x2000u) == 0)
                {
                    double abw = Math.Abs(autoBias) / 100.0 - 5.0;
                    if (abw > 0.0) slope += abw * abw * (autoBias > 0.0 ? 1.0 : -1.0) * 40.0;
                }
                return (float)slope;
            }

            private static float SolveAutoRight(double time, double nextTime, double value, double nextValue, float autoBias, uint flags)
            {
                if ((flags & 0x4000u) != 0) return 0f;
                if ((flags & 0x1000u) != 0 && Math.Abs(nextValue - value) <= KeyClampThreshold) return 0f;
                double slope = (nextValue - value) / (nextTime - time);
                if ((flags & 0x2000u) == 0)
                {
                    double abw = Math.Abs(autoBias) / 100.0 - 5.0;
                    if (abw > 0.0) slope += abw * abw * (autoBias > 0.0 ? 1.0 : -1.0) * 40.0;
                }
                return (float)slope;
            }

            private static void SolveTcb(out float left, out float right, double tension, double continuity, double bias,
                                         double slopeLeft, double slopeRight, bool edge)
            {
                double f = edge ? 1.0 : 0.5;
                double d00 = f * (1.0 - tension) * (1.0 + bias) * (1.0 - continuity);
                double d01 = f * (1.0 - tension) * (1.0 - bias) * (1.0 + continuity);
                double d10 = f * (1.0 - tension) * (1.0 + bias) * (1.0 + continuity);
                double d11 = f * (1.0 - tension) * (1.0 - bias) * (1.0 - continuity);
                left = (float)(d00 * slopeLeft + d01 * slopeRight);
                right = (float)(d10 * slopeLeft + d11 * slopeRight);
            }

            // ---- ポーズ（ufbx.c 14648-14690）----

            private FbxPoseEl ReadPose(FbxNode o, string sub)
            {
                var p = new FbxPoseEl { Kind = FbxKind.Pose, IsBindPose = sub == "BindPose" };
                if (o.Children != null)
                    foreach (var n in o.Children)
                    {
                        if (n.Name != "PoseNode") continue;
                        var nn = Child(n, "Node");
                        long nid;
                        if (Version < 7000)
                        {
                            // 7000 未満は名前で引く（ufbx.c 14662-14666）
                            if (nn == null || nn.Props.Count == 0 || !(nn.Props[0] is string nname)) continue;
                            nid = Sid(nname);
                        }
                        else
                        {
                            if (nn == null || nn.Props.Count == 0 || !(nn.Props[0] is long || nn.Props[0] is int)) continue;
                            nid = IntOf(nn.Props[0]);
                        }
                        var m = Reals(n, "Matrix");
                        if (m == null) continue;
                        if (m.Length < 16) Fail("PoseNode の Matrix が 16 個ありません");
                        p.Raw.Add((nid, DMat.FromColumnMajor16(m)));
                    }
                return p;
            }

            // ================================================================
            // Connections（ufbx.c 15239-15313、18664-18779）
            // ================================================================

            /// <summary>読んだ順の接続（ID と プロパティ名）。合成した要素の接続も含む（ufbx の tmp_connections）。</summary>
            private readonly List<(long src, long dst, string sp, string dp)> _pending = new List<(long, long, string, string)>();

            private void ReadConnections(FbxNode cn)
            {
                if (cn?.Children == null) return;
                foreach (var c in cn.Children)
                {
                    if (c.Props.Count < 1 || !(c.Props[0] is string type)) continue;
                    long src, dst;
                    string sp = "", dp = "";
                    if (Version < 7000)
                    {
                        // 7000 未満は「型::名前」で引く（ufbx.c 15252-15275）
                        bool T(int i) => i < c.Props.Count && c.Props[i] is string;
                        if (type == "OO") { if (!T(1) || !T(2)) continue; src = Sid((string)c.Props[1]); dst = Sid((string)c.Props[2]); }
                        else if (type == "OP") { if (!T(1) || !T(2) || !T(3)) continue; src = Sid((string)c.Props[1]); dst = Sid((string)c.Props[2]); dp = (string)c.Props[3]; }
                        else if (type == "PO") { if (!T(1) || !T(2) || !T(3)) continue; src = Sid((string)c.Props[1]); sp = (string)c.Props[2]; dst = Sid((string)c.Props[3]); }
                        else if (type == "PP") { if (!T(1) || !T(2) || !T(3) || !T(4)) continue; src = Sid((string)c.Props[1]); sp = (string)c.Props[2]; dst = Sid((string)c.Props[3]); dp = (string)c.Props[4]; }
                        else continue;
                    }
                    else
                    {
                        bool L(int i) => i < c.Props.Count && (c.Props[i] is long || c.Props[i] is int);
                        bool S2(int i) => i < c.Props.Count && c.Props[i] is string;
                        if (type == "OO") { if (!L(1) || !L(2)) continue; src = IntOf(c.Props[1]); dst = IntOf(c.Props[2]); }
                        else if (type == "OP") { if (!L(1) || !L(2) || !S2(3)) continue; src = IntOf(c.Props[1]); dst = IntOf(c.Props[2]); dp = (string)c.Props[3]; }
                        else if (type == "PO") { if (!L(1) || !S2(2) || !L(3)) continue; src = IntOf(c.Props[1]); sp = (string)c.Props[2]; dst = IntOf(c.Props[3]); }
                        else if (type == "PP") { if (!L(1) || !S2(2) || !L(3) || !S2(4)) continue; src = IntOf(c.Props[1]); sp = (string)c.Props[2]; dst = IntOf(c.Props[3]); dp = (string)c.Props[4]; }
                        else continue;
                    }
                    _pending.Add((src, dst, sp, dp));
                }
            }

            /// <summary>ノードの ID に合成した属性があればその ID（ufbx.c 18654-18662）。</summary>
            private long AttrIdOf(long nodeId) => _attrOf.TryGetValue(nodeId, out long a) ? a : nodeId;

            /// <summary>接続を要素へつなぐ（ufbx.c 18664-18779、動くプロパティの印は 18781-18911）。</summary>
            private void ResolveConnections()
            {
                var all = new List<FbxConn>();
                foreach (var pc in _pending)
                {
                    long srcId = pc.src, dstId = pc.dst;
                    if (Version < 7000)
                    {
                        // ノードに無いプロパティへの接続は属性へ移す（ufbx.c 18677-18693）
                        if (pc.sp.Length > 0 && !NodePropNames.Contains(pc.sp))
                        {
                            if (!_byId.TryGetValue(srcId, out var se0) || se0.Props.Find(pc.sp) == null) srcId = AttrIdOf(srcId);
                        }
                        if (pc.dp.Length > 0 && !NodePropNames.Contains(pc.dp))
                        {
                            if (!_byId.TryGetValue(dstId, out var de0) || de0.Props.Find(pc.dp) == null) dstId = AttrIdOf(dstId);
                        }
                    }
                    if (!_byId.TryGetValue(srcId, out var se) || !_byId.TryGetValue(dstId, out var de)) continue;
                    if (de == S.Root && !(se is FbxNodeEl)) continue;   // 根へつながる「ノードでない要素」は捨てる（ufbx.c 18699）
                    // 6.x：スキン・キャッシュの変形はノードでなく形状へつなぐ（ufbx.c 18750-18759）
                    if (Version < 7000 && de is FbxNodeEl && (se is FbxSkinEl || se.Kind == FbxKind.CacheDeformer))
                    {
                        if (_byId.TryGetValue(AttrIdOf(dstId), out var ae)) de = ae;
                    }
                    all.Add(new FbxConn { Src = se, Dst = de, SrcProp = pc.sp, DstProp = pc.dp });
                }
                foreach (var c in all) { c.Dst.Dst.Add(c); c.Src.Src.Add(c); }
                foreach (var e in S.Elements)
                {
                    StableSort(e.Dst, (a, b) => { int r = string.CompareOrdinal(a.DstProp, b.DstProp); return r != 0 ? r : string.CompareOrdinal(a.SrcProp, b.SrcProp); });
                    StableSort(e.Src, (a, b) => { int r = string.CompareOrdinal(a.SrcProp, b.SrcProp); return r != 0 ? r : string.CompareOrdinal(a.DstProp, b.DstProp); });
                }

                // 動くプロパティの印（ufbx.c 18781-18911）
                foreach (var e in S.Elements)
                    foreach (var c in e.Dst)
                    {
                        if (c.DstProp.Length == 0) continue;
                        bool anim = c.Src is FbxValueEl;
                        if (!anim && c.SrcProp.Length == 0) continue;
                        bool connected = c.SrcProp.Length > 0;
                        var p = e.Props.Own.TryGetValue(c.DstProp, out var own) ? own : null;
                        if (p == null)
                        {
                            p = new FbxProp { Name = c.DstProp };
                            var tp = e.Props.Defaults?.Find(c.DstProp);
                            if (tp != null) { Array.Copy(tp.Real, p.Real, 4); p.RealCount = tp.RealCount; p.Int = tp.Int; p.Str = tp.Str; p.HasStr = tp.HasStr; p.NumberOrInteger = tp.NumberOrInteger; }
                            else if (e.Props.Defaults == null && anim && c.DstProp == "Lcl Scaling") { p.Real[0] = p.Real[1] = p.Real[2] = 1.0; }
                            e.Props.Own[c.DstProp] = p;
                        }
                        if (connected) p.Connected = true; else p.Animated = true;
                    }
            }

            private static void StableSort<T>(List<T> list, Comparison<T> cmp)
            {
                if (list.Count < 2) return;
                var idx = Enumerable.Range(0, list.Count).ToList();
                var copy = list.ToArray();
                idx.Sort((a, b) => { int r = cmp(copy[a], copy[b]); return r != 0 ? r : a.CompareTo(b); });
                for (int i = 0; i < idx.Count; i++) list[i] = copy[idx[i]];
            }

            // ================================================================
            // Takes（ufbx.c 15697-15726）
            // ================================================================

            private void ReadTakes(FbxNode takes)
            {
                if (takes?.Children == null) return;
                foreach (var t in takes.Children)
                {
                    if (t.Name != "Take" || !(Str(t, 0) is string name)) continue;
                    var stack = S.Stacks.FirstOrDefault(s => s.Name == name);
                    if (stack == null || stack.Props.Own.Count > 0) continue;
                    var lt = Child(t, "LocalTime");
                    var rt = Child(t, "ReferenceTime");
                    if (lt != null && lt.Props.Count >= 2 && IsNumber(lt.Props[0]) && IsNumber(lt.Props[1]))
                    {
                        stack.Props.Own["LocalStart"] = new FbxProp { Name = "LocalStart", Int = IntOf(lt.Props[0]) };
                        stack.Props.Own["LocalStop"]  = new FbxProp { Name = "LocalStop",  Int = IntOf(lt.Props[1]) };
                    }
                    if (rt != null && rt.Props.Count >= 2 && IsNumber(rt.Props[0]) && IsNumber(rt.Props[1]))
                    {
                        stack.Props.Own["ReferenceStart"] = new FbxProp { Name = "ReferenceStart", Int = IntOf(rt.Props[0]) };
                        stack.Props.Own["ReferenceStop"]  = new FbxProp { Name = "ReferenceStop",  Int = IntOf(rt.Props[1]) };
                    }
                }
            }

            // ================================================================
            // 6.x のテイク（ufbx.c 15326-15751）
            // ================================================================

            private void ReadLegacyTakes(FbxNode takes)
            {
                if (takes?.Children == null) return;
                foreach (var t in takes.Children)
                {
                    if (t.Name != "Take") continue;
                    var props = new FbxPropSet();
                    var lt = Child(t, "LocalTime");
                    var rt = Child(t, "ReferenceTime");
                    if (lt != null && lt.Props.Count >= 2 && IsNumber(lt.Props[0]) && IsNumber(lt.Props[1]))
                    {
                        props.Own["LocalStart"] = new FbxProp { Name = "LocalStart", Int = IntOf(lt.Props[0]), NumberOrInteger = true };
                        props.Own["LocalStop"]  = new FbxProp { Name = "LocalStop",  Int = IntOf(lt.Props[1]), NumberOrInteger = true };
                    }
                    if (rt != null && rt.Props.Count >= 2 && IsNumber(rt.Props[0]) && IsNumber(rt.Props[1]))
                    {
                        props.Own["ReferenceStart"] = new FbxProp { Name = "ReferenceStart", Int = IntOf(rt.Props[0]), NumberOrInteger = true };
                        props.Own["ReferenceStop"]  = new FbxProp { Name = "ReferenceStop",  Int = IntOf(rt.Props[1]), NumberOrInteger = true };
                    }
                    foreach (var pr in props.Own.Values) pr.Real[0] = pr.Int;
                    string name = Str(t, 0); if (name == null) Fail("Take の名前がありません");

                    long stackId = NewId(), layerId = NewId();
                    Register(new FbxStackEl { Kind = FbxKind.AnimStack }, stackId, name, "", "Take", t, props);
                    Register(new FbxLayerEl { Kind = FbxKind.AnimLayer }, layerId, "BaseLayer", "", "Take", t, null);
                    _pending.Add((layerId, stackId, "", ""));

                    if (t.Children == null) continue;
                    foreach (var child in t.Children)
                    {
                        if (child.Name != "Model") continue;
                        string typeAndName = Str(child, 0); if (typeAndName == null) Fail("テイクの Model の名前がありません");
                        long target = Sid(typeAndName);
                        if (child.Children == null) continue;
                        foreach (var ch in child.Children)
                        {
                            if (ch.Name != "Channel" || !(Str(ch, 0) is string cname)) continue;
                            ReadTakePropChannel(ch, target, layerId, cname);
                        }
                    }
                }
            }

            private void ReadTakePropChannel(FbxNode node, long target, long layerId, string name)
            {
                if (name == "Transform")
                {
                    // Channel: "Transform" { Channel: "T" / "R" / "S" } → Lcl Translation / Rotation / Scaling
                    if (node.Children == null) return;
                    foreach (var child in node.Children)
                    {
                        if (child.Name != "Channel") continue;
                        string old = Str(child, 0); if (old == null) Fail("テイクの Channel の名前がありません");
                        string nn = old == "T" ? "Lcl Translation" : old == "R" ? "Lcl Rotation" : old == "S" ? "Lcl Scaling" : null;
                        if (nn == null) continue;
                        ReadTakePropChannel(child, target, layerId, nn);
                    }
                    return;
                }

                // Key か Default を持つ Channel を 1〜3 個
                var nodes = new List<(FbxNode n, string name)>();
                if (Child(node, "Key") != null || Child(node, "Default") != null) nodes.Add((node, name));
                else if (node.Children != null)
                    foreach (var child in node.Children)
                    {
                        if (child.Name != "Channel") continue;
                        if (Child(child, "Key") == null && Child(child, "Default") == null) continue;
                        if (!(Str(child, 0) is string cn)) continue;
                        nodes.Add((child, cn));
                        if (nodes.Count == 3) break;
                    }
                if (nodes.Count == 0) return;

                long valueId = NewId();
                var value = new FbxValueEl { Kind = FbxKind.AnimValue };
                Register(value, valueId, name, "", "Channel", node, null);
                _pending.Add((valueId, layerId, "", ""));
                _pending.Add((valueId, target, "", name));

                var def = new DVec3();
                for (int i = 0; i < nodes.Count; i++)
                {
                    double d = def[i];
                    ReadTakeAnimChannel(nodes[i].n, valueId, nodes[i].name, ref d);
                    def[i] = d;
                }
                value.Default = def;
            }

            private void ReadTakeAnimChannel(FbxNode node, long valueId, string name, ref double def)
            {
                var dn = Child(node, "Default");
                if (dn != null && dn.Props.Count > 0 && IsNumber(dn.Props[0])) def = RealOf(dn.Props[0]);

                var keys = Reals(node, "Key");
                if (keys == null) return;

                long curveId = NewId();
                var c = new FbxCurveEl { Kind = FbxKind.AnimCurve };
                ReadExtrapolation(Child(node, "Pre-Extrapolation"), out c.PreMode, out c.PreRepeat);
                ReadExtrapolation(Child(node, "Post-Extrapolation"), out c.PostMode, out c.PostRepeat);
                Register(c, curveId, name, "", "Channel", node, null);
                _pending.Add((curveId, valueId, "", name));

                long keyVer = 0;
                var kv = Child(node, "KeyVer");
                if (kv != null && kv.Props.Count > 0 && IsNumber(kv.Props[0])) keyVer = IntOf(kv.Props[0]);
                if (keyVer <= 0) keyVer = 4005;

                var kc = Child(node, "KeyCount");
                if (kc == null || kc.Props.Count == 0 || !IsNumber(kc.Props[0])) Fail("テイクの Channel に KeyCount がありません");
                long nkL = IntOf(kc.Props[0]);
                if (nkL < 0 || nkL > int.MaxValue) Fail("テイクの Channel の KeyCount が範囲外です");
                int nk = (int)nkL;

                char Ch(double v) => v >= 0.0 && v <= 127.0 ? (char)(int)v : '\0';
                int p = 0, end = keys.Length;
                void Need(int n) { if (end - p < n) Fail("テイクの Key が足りません"); }

                var ks = new FbxKey[nk];
                float slopeLeft = 0f, weightLeft = 0.333333f;
                double nextTime = 0.0, nextValue = 0.0, prevTime = 0.0;
                if (nk > 0)
                {
                    Need(2);
                    nextTime = keys[0] / S.KTimeSecond;
                    nextValue = keys[1];
                }
                for (int i = 0; i < nk; i++)
                {
                    ref FbxKey key = ref ks[i];
                    Need(3);
                    key.Time = nextTime;
                    key.Value = nextValue;
                    char mode = Ch(keys[p + 2]);
                    p += 3;

                    float slopeRight = 0f, weightRight = 0.333333f, nextSlopeLeft = 0f, nextWeightLeft = 0.333333f;
                    bool autoSlope = false;

                    if (mode == 'U')
                    {
                        key.Interp = FbxInterp.Cubic;
                        Need(1);
                        char slopeMode = Ch(keys[p]);
                        p += 1;
                        int numWeights = 1;
                        if (slopeMode == 's' || slopeMode == 'b')
                        {
                            Need(2);
                            slopeRight = (float)keys[p];
                            nextSlopeLeft = (float)keys[p + 1];
                            p += 2;
                            if (keyVer == 4003) numWeights = 0;
                        }
                        else if (slopeMode == 'a')
                        {
                            autoSlope = true;
                            if (keyVer <= 4004) numWeights = 0;
                        }
                        else if (slopeMode == 'p' || slopeMode == 'q')
                        {
                            autoSlope = true;
                            Need(2);
                            p += 2;
                            numWeights = keyVer <= 4004 ? 1 : 2;
                        }
                        else if (slopeMode == 't')
                        {
                            autoSlope = true;
                            Need(3);
                            p += 3;
                            numWeights = 0;
                        }
                        else if (slopeMode == 'd')
                        {
                            autoSlope = true;
                            Need(1);
                            p += 1;
                        }
                        else Fail("テイクの Key の傾きの種類が不明です");

                        for (; numWeights > 0; numWeights--)
                        {
                            Need(1);
                            char wm = Ch(keys[p]);
                            p += 1;
                            if (wm == 'n') { }
                            else if (wm == 'a')
                            {
                                Need(2);
                                weightRight = (float)keys[p];
                                nextWeightLeft = (float)keys[p + 1];
                                p += 2;
                            }
                            else if (wm == 'l') { Need(1); nextWeightLeft = (float)keys[p]; p += 1; }
                            else if (wm == 'r') { Need(1); weightRight = (float)keys[p]; p += 1; }
                            else if (wm == 'c') { }
                            else Fail("テイクの Key の重みの種類が不明です");
                        }
                    }
                    else if (mode == 'L')
                    {
                        key.Interp = FbxInterp.Linear;
                    }
                    else if (mode == 'C')
                    {
                        if (keyVer >= 4004)
                        {
                            Need(1);
                            key.Interp = Ch(keys[p]) == 'n' ? FbxInterp.ConstantNext : FbxInterp.ConstantPrev;
                            p += 1;
                        }
                        else key.Interp = FbxInterp.ConstantPrev;
                    }
                    else Fail("テイクの Key の補間の種類が不明です");

                    if (i + 1 < nk)
                    {
                        Need(2);
                        nextTime = keys[p] / S.KTimeSecond;
                        nextValue = keys[p + 1];
                    }

                    if (autoSlope)
                    {
                        if (i > 0)
                            slopeLeft = slopeRight = SolveAuto(prevTime, key.Time, nextTime, ks[i - 1].Value, key.Value, nextValue,
                                                               weightLeft, weightRight, 0f, 0x4000u | 0x2000u);
                        else
                            slopeLeft = slopeRight = 0f;
                    }

                    if (key.Interp == FbxInterp.Linear)
                    {
                        if (nextTime > key.Time) slopeRight = nextSlopeLeft = (float)((nextValue - key.Value) / (nextTime - key.Time));
                        else slopeRight = nextSlopeLeft = 0f;
                    }

                    if (key.Time > prevTime)
                    {
                        double dl = key.Time - prevTime;
                        key.LeftDx = (float)(weightLeft * dl);
                        key.LeftDy = key.LeftDx * slopeLeft;
                    }
                    else { key.LeftDx = 0f; key.LeftDy = 0f; }
                    if (nextTime > key.Time)
                    {
                        double dr = nextTime - key.Time;
                        key.RightDx = (float)(weightRight * dr);
                        key.RightDy = key.RightDx * slopeRight;
                    }
                    else { key.RightDx = 0f; key.RightDy = 0f; }

                    slopeLeft = nextSlopeLeft;
                    weightLeft = nextWeightLeft;
                    prevTime = key.Time;
                }
                if (p != end) Fail("テイクの Key の数が KeyCount と合いません");
                c.Keys = ks;
                if (nk > 0) { c.MinTime = ks[0].Time; c.MaxTime = ks[nk - 1].Time; }
            }

            // ================================================================
            // 全体の設定（ufbx.c 23906-23934）
            // ================================================================

            private int _upAxis = 6, _frontAxis = 6, _rightAxis = 6;

            private static int FindAxis(FbxPropSet p, string axisName, string signName)
            {
                long axis = p.FindInt(axisName, 3);
                long sign = p.FindInt(signName, 2);
                if (axis < 0 || axis > 2) return 6;
                return (int)(axis * 2 + (sign > 0 ? 0 : 1));
            }

            private void ReadGlobalSettings(FbxNode gs, FbxNode legacySettings)
            {
                var p = gs != null ? ReadProps(gs) : new FbxPropSet();
                // Version5 の Settings の FrameRate（ufbx.c 15769-15815）。設定に同じ名前があればそちらが残る
                var fr = Child(legacySettings, "FrameRate");
                if (fr != null && fr.Props.Count > 0)
                {
                    double fps = 0.0;
                    if (IsNumber(fr.Props[0])) fps = RealOf(fr.Props[0]);
                    else if (fr.Props[0] is string fs && double.TryParse(fs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pv)) fps = pv;
                    if (fps > 0.0)
                    {
                        if (!p.Own.ContainsKey("CustomFrameRate")) { var cp = new FbxProp { Name = "CustomFrameRate", NumberOrInteger = true }; cp.Real[0] = fps; cp.Int = F64ToI64(fps); p.Own["CustomFrameRate"] = cp; }
                        if (!p.Own.ContainsKey("TimeMode")) { var tp = new FbxProp { Name = "TimeMode", NumberOrInteger = true, Int = 14 }; tp.Real[0] = 14; p.Own["TimeMode"] = tp; }
                    }
                }
                _upAxis = FindAxis(p, "UpAxis", "UpAxisSign");
                _frontAxis = FindAxis(p, "FrontAxis", "FrontAxisSign");
                _rightAxis = FindAxis(p, "CoordAxis", "CoordAxisSign");
                long oup = p.FindInt("OriginalUpAxis", 3);
                long ous = p.FindInt("OriginalUpAxisSign", 2);
                S.OriginalAxisUp = oup < 0 || oup > 2 ? 6 : (int)(oup * 2 + (ous > 0 ? 0 : 1));
                double usf = p.FindReal("UnitScaleFactor", 1.0);
                double ousf = p.FindReal("OriginalUnitScaleFactor", usf);
                S.UnitMeters = RoundIfNear(usf * 0.01);
                S.OriginalUnitMeters = RoundIfNear(ousf * 0.01);
                long tm = p.FindEnum("TimeMode", 11, 17);
                S.FramesPerSecond = FpsTable[tm];
                if (tm == 14) S.FramesPerSecond = p.FindReal("CustomFrameRate", 24.0);
            }

            private static readonly double[] Pow10Targets =
            {
                0, 1e-8, 1e-7, 1e-6, 1e-5, 1e-4, 1e-3, 1e-2, 1e-1, 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9,
            };

            private static double RoundIfNear(double v)
            {
                foreach (double t in Pow10Targets)
                {
                    double err = Math.Max(Math.Abs(t) * 9.5367431640625e-7, 7.52316384526264005e-37);
                    if (t - err <= v && v <= t + err) return t;
                }
                return v;
            }

            // ================================================================
            // まとめ（ufbx.c 21644-22300 の必要な部分）
            // ================================================================

            private void Finalize()
            {
                // ノードの親・深さ・並び（ufbx.c 18934-18986）
                var nodes = new List<FbxNodeEl> { S.Root };
                nodes.AddRange(S.Nodes);
                foreach (var n in nodes)
                {
                    if (n.Parent == null && n != S.Root) n.Parent = S.Root;
                    foreach (var c in n.DstConns())
                        if (c.Src is FbxNodeEl child) child.Parent = n;
                }
                foreach (var n in nodes)
                {
                    int d = 0;
                    var seen = new HashSet<FbxNodeEl>();
                    for (var p = n.Parent; p != null; p = p.Parent)
                    {
                        if (!seen.Add(p)) Fail("ノードの親子が循環しています");
                        d++;
                    }
                    n.Depth = d;
                }
                var sorted = nodes.Select((n, i) => (n, i))
                                  .OrderBy(x => x.n.Depth)
                                  .ThenBy(x => x.n.Parent != null ? x.n.Parent.ElementId : -1)
                                  .ThenBy(x => x.n.ElementId)
                                  .Select(x => x.n).ToList();
                S.Nodes.Clear();
                S.Nodes.AddRange(sorted);
                for (int i = 0; i < S.Nodes.Count; i++) S.Nodes[i].TypedId = i;
                foreach (var n in S.Nodes) n.Parent?.Children.Add(n);

                // 属性の利用者（instances）
                foreach (var e in S.Elements)
                {
                    if (e.Kind < FbxKind.FirstAttrib || e.Kind > FbxKind.LastAttrib) continue;
                    foreach (var c in e.SrcConns())
                        if (c.Dst is FbxNodeEl n && !e.Instances.Contains(n)) e.Instances.Add(n);
                }

                // 継承・属性・材質（ufbx.c 21736-21790）
                foreach (var n in S.Nodes)
                {
                    if (n.IsRoot) continue;
                    var parent = n.Parent;
                    if (parent != null)
                    {
                        if (n.InheritMode == 2) n.InheritScaleNode = parent;
                        else if (n.InheritMode == 1) n.InheritScaleNode = parent.InheritScaleNode;
                    }
                    foreach (var c in n.DstConns())
                    {
                        var a = c.Src;
                        if (a.Kind < FbxKind.FirstAttrib || a.Kind > FbxKind.LastAttrib) continue;
                        if (n.Attrib == null) { n.Attrib = a; n.AttribType = a.Kind; }
                        if (a is FbxMeshEl me) n.Mesh = me;
                        if (a.Kind == FbxKind.Bone) n.Bone = a;
                    }
                    foreach (var c in n.DstConns())
                        if (c.Src is FbxMaterialEl mat) n.Materials.Add(mat);
                }

                // ポーズ（ufbx.c 21790-21827）
                foreach (var p in S.Poses)
                {
                    foreach (var (id, m) in p.Raw)
                    {
                        if (!_byId.TryGetValue(id, out var el) || !(el is FbxNodeEl node)) continue;
                        p.Bones.Add((node, m));
                        if (p.IsBindPose)
                            foreach (var c in node.SrcConns())
                                if (c.Dst is FbxClusterEl cl && cl.BindToWorld.AllZero) cl.BindToWorld = m;
                    }
                    var stable = p.Bones.Select((b, i) => (b, i)).OrderBy(x => x.b.node.TypedId).ThenBy(x => x.i).Select(x => x.b).ToList();
                    p.Bones.Clear();
                    p.Bones.AddRange(stable);
                }

                // スキン（ufbx.c 21841-21956）
                foreach (var cl in S.Clusters)
                    foreach (var c in cl.DstConns())
                        if (c.Src is FbxNodeEl bn) { cl.BoneNode = bn; break; }
                foreach (var sk in S.Skins)
                {
                    foreach (var c in sk.DstConns())
                        if (c.Src is FbxClusterEl cl && !sk.Clusters.Contains(cl)) sk.Clusters.Add(cl);
                    sk.Clusters.RemoveAll(cl => cl.BoneNode == null);
                    int nv = 0;
                    foreach (var c in sk.SrcConns())
                    {
                        if (c.Dst is FbxMeshEl me) nv = Math.Max(nv, me.Vertices.Length);
                        else if (c.Dst is FbxNodeEl nd && nd.Mesh != null) nv = Math.Max(nv, nd.Mesh.Vertices.Length);
                    }
                    sk.Weights = new List<(int, double)>[nv];
                    for (int v = 0; v < nv; v++) sk.Weights[v] = new List<(int, double)>();
                    for (int ci = 0; ci < sk.Clusters.Count; ci++)
                    {
                        var cl = sk.Clusters[ci];
                        for (int k = 0; k < cl.Indexes.Length; k++)
                        {
                            int v = cl.Indexes[k];
                            if ((uint)v < (uint)nv) sk.Weights[v].Add((ci, cl.WeightValues[k]));
                        }
                    }
                    foreach (var list in sk.Weights)
                    {
                        if (list.Count < 2) continue;
                        var st = list.Select((w, i) => (w, i)).OrderByDescending(x => x.w.Item2).ThenBy(x => x.i).Select(x => x.w).ToList();
                        list.Clear(); list.AddRange(st);
                    }
                }

                // ブレンドシェイプ（ufbx.c 21958-22025）
                bool blenderBinary = false;   // 書き出し元の判定は 8.5 の記録どおり使わない（必要になったら足す）
                foreach (var b in S.Blends)
                    foreach (var c in b.DstConns())
                        if (c.Src is FbxChannelEl ch && !b.Channels.Contains(ch)) b.Channels.Add(ch);
                foreach (var ch in S.Channels)
                {
                    var keys = new List<(FbxShapeEl shape, double w, int i)>();
                    int k = 0;
                    foreach (var c in ch.DstConns())
                    {
                        if (!(c.Src is FbxShapeEl sh)) continue;
                        double w = !blenderBinary && k < ch.FullWeights.Length ? ch.FullWeights[k] / 100.0 : 1.0;
                        keys.Add((sh, w, k));
                        k++;
                    }
                    var sortedKeys = keys.OrderBy(x => x.w).ThenBy(x => x.i).ToList();
                    ch.Target = sortedKeys.Count > 0 ? sortedKeys[sortedKeys.Count - 1].shape : null;
                    ch.Weight = ch.Props.FindReal("DeformPercent", 0.0) * 0.01;
                }

                // メッシュ（法線の生成・材質・変形）
                foreach (var m in S.Meshes)
                {
                    if (m.NormalIndices == null) GenerateNormals(m);

                    foreach (var c in m.DstConns()) if (c.Src is FbxMaterialEl mat) m.Materials.Add(mat);
                    if (m.Materials.Count == 0 && m.Instances.Count > 0)
                        foreach (var c in m.Instances[0].DstConns()) if (c.Src is FbxMaterialEl mat) m.Materials.Add(mat);
                    if (m.Materials.Count > 0)
                        foreach (var inst in m.Instances)
                            if (inst.Materials.Count < m.Materials.Count)
                                for (int i = inst.Materials.Count; i < m.Materials.Count; i++) inst.Materials.Add(m.Materials[i]);
                    if (m.Materials.Count == 0) m.FaceMaterial = null;
                    else if (m.Materials.Count == 1) m.FaceMaterial = new int[m.Faces.Length];
                    else for (int i = 0; i < m.FaceMaterial.Length; i++) if ((uint)m.FaceMaterial[i] >= (uint)m.Materials.Count) m.FaceMaterial[i] = 0;

                    // 7000 未満はノードにつながる変形も探す（ufbx.c 21839・22139-22142・19034-19068）
                    var holders = new List<FbxElem> { m };
                    if (Version < 7000 && m.Instances.Count > 0) holders.Add(m.Instances[0]);
                    foreach (var hd in holders)
                        foreach (var c in hd.DstConns())
                        {
                            if (c.Src is FbxSkinEl sk && !m.Skins.Contains(sk)) m.Skins.Add(sk);
                            if (c.Src is FbxBlendDefEl bd && !m.Blends.Contains(bd)) m.Blends.Add(bd);
                        }
                }

                // テクスチャ・動画（ufbx.c 21440-21530、20878-21009、22484-22520）
                foreach (var v in S.Videos)
                    ResolveFilenames(ref v.Filename, v.AbsoluteFilename, ref v.RelativeFilename);
                foreach (var v in S.Videos)
                {
                    if (v.Content.Length > 0 || v.AbsoluteFilename.Length == 0) continue;
                    var other = S.Videos.Where(o => o.Content.Length > 0 && o.AbsoluteFilename == v.AbsoluteFilename)
                                        .OrderBy(o => o.AbsoluteFilename, StringComparer.Ordinal).FirstOrDefault();
                    if (other != null) v.Content = other.Content;
                }
                foreach (var t in S.Textures)
                {
                    foreach (var c in t.DstConns()) if (c.Src is FbxVideoEl v) { t.Video = v; break; }
                    if (t.Video != null) t.Content = t.Video.Content;
                    if (!t.Layered) ResolveFilenames(ref t.Filename, t.AbsoluteFilename, ref t.RelativeFilename);
                }
                foreach (var t in S.Textures) FileTextures(t, new HashSet<FbxTextureEl>());
                foreach (var mat in S.Materials)
                    foreach (var c in mat.Dst)
                        if (c.SrcProp.Length == 0 && c.Src is FbxTextureEl tex) mat.Textures.Add((c.DstProp, tex));
                LegacyMaterialTextures();
                foreach (var mat in S.Materials)
                {
                    var st = mat.Textures.Select((x, i) => (x, i)).OrderBy(y => y.x.prop, StringComparer.Ordinal).ThenBy(y => y.i).Select(y => y.x).ToList();
                    mat.Textures.Clear(); mat.Textures.AddRange(st);
                }

                // テイク（ufbx.c 22179-22300、23374-23391）
                foreach (var st in S.Stacks)
                    foreach (var c in st.DstConns())
                        if (c.Src is FbxLayerEl l && !st.Layers.Contains(l)) st.Layers.Add(l);
                foreach (var l in S.Layers)
                {
                    foreach (var c in l.DstConns())
                    {
                        if (!(c.Src is FbxValueEl v)) continue;
                        foreach (var ac in v.Src)
                            if (ac.SrcProp.Length == 0 && ac.DstProp.Length > 0 && !l.AnimProps.ContainsKey((ac.Dst, ac.DstProp)))
                                l.AnimProps[(ac.Dst, ac.DstProp)] = v;
                    }
                    switch (l.Props.FindInt("BlendMode", 0))
                    {
                        case 0: l.Blended = true; l.Additive = true; break;
                        case 1: l.Blended = false; l.Additive = false; break;
                        case 2: l.Blended = true; l.Additive = false; break;
                        default: l.Blended = false; l.Additive = false; break;
                    }
                    var wp = l.Props.Find("Weight");
                    if (wp != null)
                    {
                        l.LayerWeight = wp.Real[0] / 100.0;
                        if (l.LayerWeight < 0.0) l.LayerWeight = 0.0;
                        if (l.LayerWeight > 0.99999f) l.LayerWeight = 1.0;
                    }
                    l.ComposeRotation = l.Props.FindInt("RotationAccumulationMode", 0) == 0;
                    l.ComposeScale = l.Props.FindInt("ScaleAccumulationMode", 0) == 0;
                }
                foreach (var v in S.Values)
                {
                    var d = v.Default;   // 6.x はテイクの Default（ufbx.c 22270）
                    d.x = v.Props.FindReal("X", d.x); d.x = v.Props.FindReal("d|X", d.x);
                    d.y = v.Props.FindReal("Y", d.y); d.y = v.Props.FindReal("d|Y", d.y);
                    d.z = v.Props.FindReal("Z", d.z); d.z = v.Props.FindReal("d|Z", d.z);
                    foreach (var c in v.Dst)
                    {
                        if (!(c.Src is FbxCurveEl cv) || c.SrcProp.Length != 0) continue;
                        int index = 0;
                        if (c.DstProp == "Y" || c.DstProp == "d|Y") index = 1;
                        if (c.DstProp == "Z" || c.DstProp == "d|Z") index = 2;
                        var p = v.Props.Find(c.DstProp);
                        if (p != null) d[index] = p.Real[0];
                        v.Curves[index] = cv;
                    }
                    v.Default = d;
                }
                foreach (var st in S.Stacks)
                {
                    var b = st.Props.Find("LocalStart");
                    var e = st.Props.Find("LocalStop");
                    if (b == null || e == null) { b = st.Props.Find("ReferenceStart"); e = st.Props.Find("ReferenceStop"); }
                    if (b != null && e != null)
                    {
                        st.TimeBegin = b.Int / S.KTimeSecond;
                        st.TimeEnd = e.Int / S.KTimeSecond;
                    }
                }
            }

            /// <summary>6.x：メッシュの層のテクスチャを材質へ（ufbx.c 22367-22466）。テクスチャを持たない材質だけ。</summary>
            private void LegacyMaterialTextures()
            {
                foreach (var m in S.Meshes)
                {
                    if (m.LegacyTextures.Count == 0 || m.Materials.Count == 0) continue;
                    int nm = m.Materials.Count;
                    var textures = new List<FbxTextureEl>();
                    foreach (var c in m.DstConns()) if (c.Src is FbxTextureEl t) textures.Add(t);
                    if (m.Instances.Count > 0) foreach (var c in m.Instances[0].DstConns()) if (c.Src is FbxTextureEl t) textures.Add(t);

                    var list = new List<(int mat, int tex, string prop)>();
                    foreach (var lt in m.LegacyTextures)
                    {
                        if (lt.allSame)
                        {
                            int tid = lt.ids != null && lt.ids.Length > 0 ? lt.ids[0] : 0;
                            if (tid >= 0 && tid < textures.Count)
                                for (int i = 0; i < nm; i++) list.Add((i, tid, lt.prop));
                        }
                        else if (m.FaceMaterial != null)
                        {
                            int nf = Math.Min(lt.ids?.Length ?? 0, m.Faces.Length);
                            int pm = -1, pt = -1;
                            for (int i = 0; i < nf; i++)
                            {
                                int tid = lt.ids[i], mid = m.FaceMaterial[i];
                                if (tid < 0 || tid >= textures.Count) continue;
                                if (mid < 0 || mid >= nm) continue;
                                if (mid == pm && tid == pt) continue;
                                pm = mid; pt = tid;
                                list.Add((mid, tid, lt.prop));
                            }
                        }
                    }
                    var sorted = list.Select((x, i) => (x, i)).OrderBy(y => y.x.mat).ThenBy(y => y.x.tex)
                                     .ThenBy(y => y.x.prop, StringComparer.Ordinal).ThenBy(y => y.i).Select(y => y.x).ToList();
                    int k = 0;
                    while (k < sorted.Count)
                    {
                        int mid = sorted[k].mat;
                        var texs = new List<(string, FbxTextureEl)>();
                        int prevTex = -1; string prevProp = null;
                        for (; k < sorted.Count && sorted[k].mat == mid; k++)
                        {
                            if (sorted[k].tex == prevTex && sorted[k].prop == prevProp) continue;
                            prevTex = sorted[k].tex; prevProp = sorted[k].prop;
                            texs.Add((sorted[k].prop, textures[sorted[k].tex]));
                        }
                        var mat = m.Materials[mid];
                        if (mat != null && mat.Textures.Count == 0 && texs.Count > 0) mat.Textures.AddRange(texs);
                    }
                }
            }

            private static void FileTextures(FbxTextureEl t, HashSet<FbxTextureEl> visiting)
            {
                if (t.FileTextures.Count > 0 || !visiting.Add(t)) return;
                if (!t.Layered) { t.FileTextures.Add(t); return; }
                foreach (var c in t.DstConns())
                {
                    if (!(c.Src is FbxTextureEl layer)) continue;
                    FileTextures(layer, visiting);
                    foreach (var f in layer.FileTextures) if (!t.FileTextures.Contains(f)) t.FileTextures.Add(f);
                }
            }

            // ---- ファイル名（ufbx.c 16486-16649、21370-21453）----

            private static string TrimDelimiters(string s)
            {
                int len = s.Length;
                for (; len > 0; len--)
                {
                    char c = s[len - 1];
                    if (c == '/' || c == PathSeparator) { len--; break; }
                }
                return s.Substring(0, len);
            }

            private void ResolveFilenames(ref string filename, string absolute, ref string relative)
            {
                if (relative.Length == 0) relative = AbsoluteToRelative(_documentUrl, absolute) ?? relative;
                filename = ResolveRelative(relative);
            }

            private static int NextSegment(string s, int begin, int len)
            {
                for (int i = begin; i < len; i++) if (s[i] == '/' || s[i] == '\\') return i;
                return len;
            }

            private static string AbsoluteToRelative(string rel, string src)
            {
                if (rel.Length == 0 || src.Length == 0 || rel[0] != src[0]) return null;
                int relLen = rel.Length;
                while (relLen > 0 && rel[relLen - 1] != '/' && rel[relLen - 1] != '\\') relLen--;
                if (relLen == 0) return null;
                char sep = rel[relLen - 1];
                var sb = new StringBuilder();
                int rb = 0, sbg = 0;
                while (rb < relLen && sbg < src.Length)
                {
                    int re = NextSegment(rel, rb, relLen), se = NextSegment(src, sbg, src.Length);
                    if (re - rb != se - sbg || string.CompareOrdinal(rel, rb, src, sbg, se - sbg) != 0) break;
                    rb = re + 1; sbg = se + 1;
                }
                while (rb < relLen) { int re = NextSegment(rel, rb, relLen); sb.Append("..").Append(sep); rb = re + 1; }
                while (sbg < src.Length)
                {
                    int se = NextSegment(src, sbg, src.Length);
                    sb.Append(src, sbg, se - sbg);
                    if (se < src.Length) sb.Append(sep);
                    sbg = se + 1;
                }
                return sb.ToString();
            }

            private string ResolveRelative(string src)
            {
                int start = 0;
                while (start < src.Length && (src[start] == '/' || src[start] == '\\')) start++;
                src = src.Substring(start);
                if (src.Length == 0) return "";
                string prefix = _relativeRoot;
                int pl = prefix.Length;
                bool abs = src.Length > 2 && src[1] == ':' && (src[2] == '\\' || src[2] == '/');
                if (abs) pl = 0;
                while (pl > 0 && src.Length >= 3 && src[0] == '.' && src[1] == '.' && (src[2] == '/' || src[2] == '\\'))
                {
                    int ps = pl;
                    while (ps > 0 && !(prefix[ps - 1] == '/' || prefix[ps - 1] == '\\')) ps--;
                    int partLen = pl - ps;
                    if (partLen == 2 && prefix[ps] == '.' && prefix[ps + 1] == '.') break;
                    pl = ps > 0 ? ps - 1 : 0;
                    if (partLen == 1 && prefix[ps] == '.') continue;
                    src = src.Substring(3);
                }
                var sb = new StringBuilder();
                if (pl > 0) sb.Append(prefix, 0, pl).Append(PathSeparator);
                foreach (char c in src) sb.Append(c == '/' || c == '\\' ? PathSeparator : c);
                return sb.ToString();
            }

            // ---- 法線の生成（ufbx.c 20366-20405、28715-28827、32509-32620）----

            private static void GenerateNormals(FbxMeshEl m)
            {
                int ni = m.VertexIndices.Length;
                var twin = new int[ni];
                var edge = new int[ni];
                var face = new int[ni];
                var prev = new int[ni];
                var next = new int[ni];
                for (int i = 0; i < ni; i++) { twin[i] = -1; edge[i] = -1; }

                var groups = new Dictionary<(int, int), List<int>>();
                for (int f = 0; f < m.Faces.Length; f++)
                {
                    var fc = m.Faces[f];
                    for (int p = 0; p < fc.Count; p++)
                    {
                        int i = fc.Begin + p;
                        int va = m.VertexIndices[i], vb = m.VertexIndices[fc.Begin + (p + 1) % fc.Count];
                        if (vb < va) { int t = va; va = vb; vb = t; }
                        face[i] = f;
                        if (!groups.TryGetValue((va, vb), out var g)) groups[(va, vb)] = g = new List<int>();
                        g.Add(i);
                    }
                }
                if (m.Edges != null)
                    for (int ei = 0; ei < m.Edges.Length; ei++)
                    {
                        int va = m.VertexIndices[m.Edges[ei].a], vb = m.VertexIndices[m.Edges[ei].b];
                        if (vb < va) { int t = va; va = vb; vb = t; }
                        if (groups.TryGetValue((va, vb), out var g)) foreach (int i in g) edge[i] = ei;
                    }
                foreach (var g in groups.Values)
                    if (g.Count == 2) { twin[g[0]] = g[1]; twin[g[1]] = g[0]; }
                foreach (var fc in m.Faces)
                    for (int k = 0; k < fc.Count; k++)
                    {
                        prev[fc.Begin + k] = fc.Begin + (k + fc.Count - 1) % fc.Count;
                        next[fc.Begin + k] = fc.Begin + (k + 1) % fc.Count;
                    }

                bool Smooth(int i)
                {
                    if (m.EdgeSmoothing != null && edge[i] >= 0 && edge[i] < m.EdgeSmoothing.Length && m.EdgeSmoothing[edge[i]]) return true;
                    if (m.FaceSmoothing != null)
                    {
                        if (m.FaceSmoothing[face[i]]) return true;
                        if (twin[i] >= 0 && m.FaceSmoothing[face[twin[i]]]) return true;
                    }
                    return false;
                }

                var nidx = new int[ni];
                for (int i = 0; i < ni; i++) nidx[i] = -1;
                int nextIndex = 0;
                for (int v = 0; v < m.Vertices.Length; v++)
                {
                    int orig = m.VertexFirstIndex[v];
                    if (orig < 0) continue;
                    int start = orig, cur = orig;
                    for (int guard = 0; guard <= ni; guard++)
                    {
                        int pv = twin[cur] >= 0 ? next[twin[cur]] : -1;
                        if (!Smooth(cur)) start = cur;
                        if (pv < 0) { start = cur; break; }
                        if (pv == orig) break;
                        cur = pv;
                    }
                    nidx[start] = nextIndex++;
                    int n = start;
                    for (int guard = 0; guard <= ni; guard++)
                    {
                        n = twin[prev[n]];
                        if (n < 0 || n == start) break;
                        if (!Smooth(n)) ++nextIndex;
                        nidx[n] = nextIndex - 1;
                    }
                }
                for (int i = 0; i < ni; i++) if (nidx[i] < 0) nidx[i] = nextIndex++;

                var normals = new DVec3[nextIndex];
                foreach (var fc in m.Faces)
                {
                    var fn = FaceNormal(m, fc);
                    for (int k = 0; k < fc.Count; k++) normals[nidx[fc.Begin + k]] += fn;
                }
                for (int i = 0; i < normals.Length; i++)
                {
                    double len = normals[i].Length;
                    if (len > 0.0) normals[i] = new DVec3(normals[i].x / len, normals[i].y / len, normals[i].z / len);
                }
                m.NormalValues = normals;
                m.NormalIndices = nidx;
            }

            private static DVec3 FaceNormal(FbxMeshEl m, FbxFace f)
            {
                DVec3 P(int k) => m.Vertices[m.VertexIndices[f.Begin + k]];
                if (f.Count < 3) return DVec3.Zero;
                if (f.Count == 3) return DVec3.Cross(P(1) - P(0), P(2) - P(0));
                if (f.Count == 4) return DVec3.Cross(P(2) - P(0), P(3) - P(1));
                var r = DVec3.Zero;
                for (int i = 0; i < f.Count; i++)
                {
                    var a = P(i); var b = P(i + 1 < f.Count ? i + 1 : 0);
                    r.x += (a.y - b.y) * (a.z + b.z);
                    r.y += (a.z - b.z) * (a.x + b.x);
                    r.z += (a.x - b.x) * (a.y + b.y);
                }
                return r;
            }

            // ================================================================
            // 座標・単位の変換（ufbx.c 24954-25018、23679-23806、21167-21334）
            // ================================================================

            private void SpaceConversion()
            {
                // 軸の行列
                DMat axis = default;
                bool valid = _upAxis < 6 && _frontAxis < 6 && _rightAxis < 6
                          && (_upAxis >> 1) != (_frontAxis >> 1) && (_upAxis >> 1) != (_rightAxis >> 1) && (_frontAxis >> 1) != (_rightAxis >> 1);
                const int dstRight = 0, dstUp = 2, dstFront = 4;
                if (valid && !(_rightAxis == dstRight && _upAxis == dstUp && _frontAxis == dstFront))
                {
                    void Set(int src, int dst)
                    {
                        double v = ((src ^ dst) & 1) == 0 ? 1.0 : -1.0;
                        int c = src >> 1, r = dst >> 1;
                        switch (c * 3 + r)
                        {
                            case 0: axis.m00 = v; break; case 1: axis.m10 = v; break; case 2: axis.m20 = v; break;
                            case 3: axis.m01 = v; break; case 4: axis.m11 = v; break; case 5: axis.m21 = v; break;
                            case 6: axis.m02 = v; break; case 7: axis.m12 = v; break; case 8: axis.m22 = v; break;
                        }
                    }
                    Set(_rightAxis, dstRight);
                    Set(_upAxis, dstUp);
                    Set(_frontAxis, dstFront);
                }

                // 単位
                double unitScale = 1.0;
                if (S.UnitMeters > 0.0)
                {
                    double ratio = RoundIfNear(S.UnitMeters / RoundIfNear(1.0));
                    if (ratio != 1.0) unitScale = ratio;
                }

                var root = axis.AllZero ? DTransform.Identity : FbxMath.ToTransform(axis);
                root.S = new DVec3(root.S.x * unitScale, root.S.y * unitScale, root.S.z * unitScale);
                double rootScale = Math.Min(root.S.x, Math.Min(root.S.y, root.S.z));
                S.GeometryScale = rootScale;
                S.RootRotation = root.Q;

                foreach (var n in S.Nodes)
                {
                    if (n.IsRoot) continue;
                    if (n.Depth <= 1) n.AdjustPreRotation = root.Q;
                    n.AdjustTranslationScale = rootScale;
                    n.HasAdjust = true;
                }

                if (rootScale != 1.0)
                {
                    foreach (var sh in S.Shapes)
                        for (int i = 0; i < sh.Offsets.Length; i++) sh.Offsets[i] = Scale(sh.Offsets[i], rootScale);
                    foreach (var m in S.Meshes)
                        for (int i = 0; i < m.Vertices.Length; i++) m.Vertices[i] = Scale(m.Vertices[i], rootScale);
                }
            }

            private static DVec3 Scale(DVec3 v, double s) => new DVec3(v.x * s, v.y * s, v.z * s);

            // ================================================================
            // ノードの変換（ufbx.c 22958-23045）
            // ================================================================

            private void UpdateNodes()
            {
                foreach (var n in S.Nodes)
                {
                    if (!n.IsRoot)
                    {
                        bool active = n.Props.FindInt("RotationActive", 1) != 0;
                        bool limitOnly = n.Props.FindInt("RotationSpaceForLimitOnly", 0) != 0;
                        n.UseRotationSpace = active && !limitOnly;
                    }
                    FbxTransformEval.UpdateNode(n, n.Props);
                }
            }

            // ================================================================
            // クラスタとポーズの変換（ufbx.c 23526-23622）
            // ================================================================

            private void UpdateClusters()
            {
                var w = FbxMath.ToMatrix(new DTransform { T = DVec3.Zero, Q = S.RootRotation, S = DVec3.One });
                double ts = S.GeometryScale;
                foreach (var cl in S.Clusters)
                {
                    cl.GeometryToBone = cl.MeshNodeToBone;
                    cl.BindToWorld = DMat.Mul(w, cl.BindToWorld);
                    cl.BindToWorld.m03 *= ts; cl.BindToWorld.m13 *= ts; cl.BindToWorld.m23 *= ts;
                }
                foreach (var p in S.Poses)
                    for (int i = 0; i < p.Bones.Count; i++)
                    {
                        var m = DMat.Mul(w, p.Bones[i].boneToWorld);
                        m.m03 *= ts; m.m13 *= ts; m.m23 *= ts;
                        p.Bones[i] = (p.Bones[i].node, m);
                    }
                foreach (var cl in S.Clusters)
                {
                    FbxSkinEl skin = null;
                    foreach (var c in cl.SrcConns()) if (c.Dst is FbxSkinEl sk) { skin = sk; break; }
                    if (skin == null) continue;
                    FbxNodeEl node = null;
                    foreach (var c in skin.SrcConns()) if (c.Dst is FbxNodeEl nd) { node = nd; break; }
                    if (node == null)
                        foreach (var c in skin.SrcConns())
                            if (c.Dst is FbxMeshEl me) { if (me.Instances.Count > 0) node = me.Instances[0]; break; }
                    if (node == null) continue;
                    if (cl.MeshNodeToBone.AllZero)
                        cl.MeshNodeToBone = DMat.Mul(DMat.Invert(cl.BindToWorld), node.NodeToWorld);
                    else if (S.GeometryScale != 1.0)
                    {
                        cl.MeshNodeToBone.m03 *= S.GeometryScale; cl.MeshNodeToBone.m13 *= S.GeometryScale; cl.MeshNodeToBone.m23 *= S.GeometryScale;
                    }
                    cl.GeometryToBone = node.HasGeometryTransform ? DMat.Mul(cl.MeshNodeToBone, node.GeometryToNode) : cl.MeshNodeToBone;
                }
            }
        }
    }
}
