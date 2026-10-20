// FbxSceneModel.cs
// ============================================================
// FBX 読み込み（段 8）のシーンの表。FbxSceneReader が組み、FbxScene が口として公開する。
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。形は ufbx 0.23.1 の ufbx_scene の必要な部分。
//   要素の種類（Kind）は ufbx_element_type の値（ufbx.h 694-736）。
//   接続の並び：Dst（自分が親）は (DstProp, SrcProp) の順、Src（自分が子）は (SrcProp, DstProp) の順の安定並べ替え
//   （ufbx.c 18637-18652）。
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Fbx
{
    internal static class FbxKind
    {
        public const int Unknown = 0, Node = 1, Mesh = 2, Light = 3, Camera = 4, Bone = 5, Empty = 6,
            LineCurve = 7, NurbsCurve = 8, NurbsSurface = 9, TrimSurface = 10, TrimBoundary = 11,
            StereoCamera = 13, CameraSwitcher = 14, Marker = 15, LodGroup = 16,
            Skin = 17, Cluster = 18, BlendDeformer = 19, BlendChannel = 20, BlendShape = 21, CacheDeformer = 22,
            Material = 24, Texture = 25, Video = 26, Shader = 27,
            AnimStack = 29, AnimLayer = 30, AnimValue = 31, AnimCurve = 32, Pose = 40;

        public const int FirstAttrib = Mesh, LastAttrib = LodGroup;
    }

    /// <summary>Properties70 / Properties60 の P 1 行（ufbx_prop）。</summary>
    internal sealed class FbxProp
    {
        public string Name;
        public long   Int;
        public readonly double[] Real = new double[4];
        public int    RealCount;
        public string Str = "";
        public bool   HasStr;
        public bool   IsReference;          // 型が Reference（材質の値として使わない。ufbx.c 20041）
        public bool   Animated;
        public bool   Connected;
        public bool   UserDefined;          // フラグに U（ufbx の UFBX_PROP_FLAG_USER_DEFINED）
        public bool   NumberOrInteger;      // 型が UFBX_PROP_NUMBER か UFBX_PROP_INTEGER（ufbx.c 11432-11464）

        public DVec3 Vec3 => new DVec3(Real[0], Real[1], Real[2]);
    }

    /// <summary>要素のプロパティ（自分の値 → テンプレートの既定の順に引く。ufbx.c 11476）。</summary>
    internal sealed class FbxPropSet
    {
        public readonly Dictionary<string, FbxProp> Own = new Dictionary<string, FbxProp>(StringComparer.Ordinal);
        public FbxPropSet Defaults;

        public FbxProp Find(string name)
        {
            for (var p = this; p != null; p = p.Defaults)
                if (p.Own.TryGetValue(name, out var v)) return v;
            return null;
        }

        public double FindReal(string name, double def) { var p = Find(name); return p != null ? p.Real[0] : def; }
        public DVec3  FindVec3(string name, DVec3 def)  { var p = Find(name); return p != null ? p.Vec3 : def; }
        public long   FindInt(string name, long def)    { var p = Find(name); return p != null ? p.Int : def; }

        public long FindEnum(string name, long def, long max)
        {
            var p = Find(name);
            if (p == null) return def;
            return p.Int >= 0 && p.Int <= max ? p.Int : def;
        }
    }

    internal sealed class FbxConn
    {
        public FbxElem Src, Dst;
        public string  SrcProp = "", DstProp = "";
        public bool IsOO => SrcProp.Length == 0 && DstProp.Length == 0;
    }

    internal class FbxElem
    {
        public long       Id;
        public string     Name = "";
        public string     SubType = "";
        public string     Class = "";          // Objects の下のノード名（Model・Geometry …）
        public int        Kind;
        public int        ElementId;
        public int        TypedId;
        public FbxNode    Node;
        public FbxPropSet Props = new FbxPropSet();
        public readonly List<FbxConn> Dst = new List<FbxConn>();   // 自分が親（dst）の接続
        public readonly List<FbxConn> Src = new List<FbxConn>();   // 自分が子（src）の接続
        public readonly List<FbxNodeEl> Instances = new List<FbxNodeEl>();

        /// <summary>prop の名前で、src 側の名前が空の接続（ufbxi_find_dst_connections）。</summary>
        public IEnumerable<FbxConn> DstConns(string prop = "")
        {
            foreach (var c in Dst) if (c.SrcProp.Length == 0 && c.DstProp == prop) yield return c;
        }

        public IEnumerable<FbxConn> SrcConns(string prop = "")
        {
            foreach (var c in Src) if (c.DstProp.Length == 0 && c.SrcProp == prop) yield return c;
        }
    }

    internal sealed class FbxNodeEl : FbxElem
    {
        public bool       IsRoot;
        public FbxNodeEl  Parent;
        public int        Depth;
        public readonly List<FbxNodeEl> Children = new List<FbxNodeEl>();
        public FbxElem    Attrib;
        public int        AttribType;
        public FbxMeshEl  Mesh;
        public FbxElem    Bone;
        public readonly List<FbxMaterialEl> Materials = new List<FbxMaterialEl>();

        // 変換（ufbx.c 22958 ufbxi_update_node）
        public int        InheritMode;          // 0 = NORMAL, 1 = IGNORE_PARENT_SCALE, 2 = COMPONENTWISE_SCALE
        public FbxNodeEl  InheritScaleNode;
        public bool       UseRotationSpace;
        public DQuat      AdjustPreRotation = DQuat.Identity;
        public double     AdjustTranslationScale = 1.0;
        public bool       HasAdjust;
        public DTransform Local = DTransform.Identity;
        public DVec3      InheritScale = DVec3.One;
        public DMat       NodeToParent = DMat.Identity;
        public DMat       NodeToWorld = DMat.Identity;
        public DMat       UnscaledNodeToWorld = DMat.Identity;
        public DMat       GeometryToNode = DMat.Identity;
        public bool       HasGeometryTransform;
    }

    internal struct FbxFace { public int Begin, Count; }

    internal sealed class FbxMeshEl : FbxElem
    {
        public DVec3[]   Vertices = Array.Empty<DVec3>();
        public int[]     VertexIndices = Array.Empty<int>();
        public FbxFace[] Faces = Array.Empty<FbxFace>();
        public int[]     VertexFirstIndex = Array.Empty<int>();
        public (int a, int b)[] Edges;
        public bool[]    EdgeSmoothing, FaceSmoothing;
        public int[]     FaceMaterial;          // null = 無し（材質 0 個）
        public DVec3[]   NormalValues; public int[] NormalIndices;   // null = 無し
        public double[]  UvValues;     public int[] UvIndices;       // 先頭の UV セット（2 個ずつ）
        public readonly List<FbxMaterialEl> Materials = new List<FbxMaterialEl>();
        public readonly List<FbxSkinEl> Skins = new List<FbxSkinEl>();
        public readonly List<FbxBlendDefEl> Blends = new List<FbxBlendDefEl>();
        /// <summary>6.x のメッシュの中のテクスチャの層（材質のプロパティ名、面ごとのテクスチャ番号、AllSame か。ufbx.c 13671-13698）。</summary>
        public readonly List<(string prop, int[] ids, bool allSame)> LegacyTextures = new List<(string, int[], bool)>();
    }

    internal sealed class FbxSkinEl : FbxElem
    {
        public readonly List<FbxClusterEl> Clusters = new List<FbxClusterEl>();
        /// <summary>制御点ごとの (クラスタ番号, 重み)。重い順の安定並べ替え。</summary>
        public List<(int cluster, double weight)>[] Weights;
    }

    internal sealed class FbxClusterEl : FbxElem
    {
        public int[]     Indexes = Array.Empty<int>();
        public double[]  WeightValues = Array.Empty<double>();
        public DMat      MeshNodeToBone;
        public DMat      BindToWorld;
        public DMat      GeometryToBone;
        public FbxNodeEl BoneNode;
    }

    internal sealed class FbxBlendDefEl : FbxElem
    {
        public readonly List<FbxChannelEl> Channels = new List<FbxChannelEl>();
    }

    internal sealed class FbxChannelEl : FbxElem
    {
        public double[] FullWeights = Array.Empty<double>();
        public FbxShapeEl Target;
        public double Weight;
    }

    internal sealed class FbxShapeEl : FbxElem
    {
        public int[]   Indexes = Array.Empty<int>();
        public DVec3[] Offsets = Array.Empty<DVec3>();
    }

    internal sealed class FbxMaterialEl : FbxElem
    {
        /// <summary>(材質のプロパティ名, テクスチャ)。プロパティ名の安定並べ替え（ufbx.c 22520）。</summary>
        public readonly List<(string prop, FbxTextureEl tex)> Textures = new List<(string, FbxTextureEl)>();
    }

    internal sealed class FbxTextureEl : FbxElem
    {
        public bool    Layered;
        public string  AbsoluteFilename = "", RelativeFilename = "", Filename = "";
        public FbxVideoEl Video;
        public byte[]  Content = Array.Empty<byte>();
        public readonly List<FbxTextureEl> FileTextures = new List<FbxTextureEl>();
    }

    internal sealed class FbxVideoEl : FbxElem
    {
        public string AbsoluteFilename = "", RelativeFilename = "", Filename = "";
        public byte[] Content = Array.Empty<byte>();
    }

    internal sealed class FbxPoseEl : FbxElem
    {
        public bool IsBindPose;
        public readonly List<(long id, DMat m)> Raw = new List<(long, DMat)>();
        public readonly List<(FbxNodeEl node, DMat boneToWorld)> Bones = new List<(FbxNodeEl, DMat)>();
    }

    internal sealed class FbxStackEl : FbxElem
    {
        public readonly List<FbxLayerEl> Layers = new List<FbxLayerEl>();
        public double TimeBegin, TimeEnd;
    }

    internal sealed class FbxLayerEl : FbxElem
    {
        public bool   Blended, Additive, ComposeRotation, ComposeScale;
        public double LayerWeight = 1.0;
        /// <summary>(要素, プロパティ名) → 曲線ノード（最初に見つかったもの）。</summary>
        public readonly Dictionary<(FbxElem, string), FbxValueEl> AnimProps = new Dictionary<(FbxElem, string), FbxValueEl>();
    }

    internal sealed class FbxValueEl : FbxElem
    {
        public DVec3 Default;
        public readonly FbxCurveEl[] Curves = new FbxCurveEl[3];
    }

    internal enum FbxInterp { ConstantPrev, ConstantNext, Linear, Cubic }

    internal struct FbxKey
    {
        public double    Time, Value;
        public FbxInterp Interp;
        public float     LeftDx, LeftDy, RightDx, RightDy;
    }

    internal sealed class FbxCurveEl : FbxElem
    {
        public FbxKey[] Keys = Array.Empty<FbxKey>();
        public double MinTime, MaxTime;
        public int PreMode, PostMode;          // 0 = 定数, 1 = 繰り返し, 2 = 鏡, 3 = 傾き, 4 = 相対の繰り返し
        public int PreRepeat = -1, PostRepeat = -1;
    }
}
