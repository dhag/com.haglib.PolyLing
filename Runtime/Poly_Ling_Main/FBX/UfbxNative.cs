// UfbxNative.cs
// ============================================================
// FBX 読み込み（ufbx）の C の口 plufbx.dll への P/Invoke
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md。
//
// ■ DLL
//   Runtime/Plugins/x86_64/plufbx.dll（Editor と Windows x64 スタンドアロン）。
//   ソースとビルド手順は Native~/ufbx（plufbx.c・build_msvc.bat）。
//   ufbx の構造体は C# へ直接渡さず、plufbx.c が平らな関数にして公開する。
//   関数の意味（配列の並び・座標の約束）は plufbx.c の各関数のコメントが正本。
//
// ■ UfbxScene
//   読み込んだシーン 1 つ。Dispose で ufbx のシーンを解放する。
//   座標は ufbx が右手系・Y 上・前 +Z・単位 m へ直したもの（Unity への X 反転は呼び出し側）。
//   行列は Unity の Matrix4x4（列ベクトル。4 行目は 0, 0, 0, 1）で返す。
// ============================================================

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Fbx
{
    public static class UfbxNative
    {
        private const string Dll = "plufbx";
        private const CallingConvention Cc = CallingConvention.Cdecl;

        [DllImport(Dll, CallingConvention = Cc)] private static extern uint plufbx_version();

        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern IntPtr plufbx_load_memory(byte[] data, long size, byte[] filenameUtf8, byte[] err, int errCap);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_free(IntPtr s);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_scene_info(IntPtr s, int[] outInts);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_node_name(IntPtr s, int i);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_node_info(IntPtr s, int i, int[] ints, double[] world, double[] bind, double[] geo);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_node_material(IntPtr s, int i, int slot);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_mesh_info(IntPtr s, int mi, int[] outInts);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_mesh_positions(IntPtr s, int mi, float[] xyz);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_mesh_faces(IntPtr s, int mi, int[] sizes, int[] mats);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_mesh_corners(IntPtr s, int mi, int[] vidx, float[] nrm, float[] uv);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_mesh_cluster(IntPtr s, int mi, int c, double[] geoToBone, double[] bindToWorld);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_mesh_skin_weights(IntPtr s, int mi, int maxPer, int[] cluster, float[] weight);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_mesh_blend_name(IntPtr s, int mi, int k);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_mesh_blend_offsets(IntPtr s, int mi, int k, int[] vidx, float[] d3);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_material_name(IntPtr s, int i);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_material_info(IntPtr s, int i, float[] vals, int[] tex);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_texture_string(IntPtr s, int i, int which);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_texture_info(IntPtr s, int i, int[] ints, float[] st);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_texture_content(IntPtr s, int i, byte[] dst, int cap);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_anim_stack_name(IntPtr s, int k);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void   plufbx_anim_stack_info(IntPtr s, int k, double[] outVals);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_blend_channel_count(IntPtr s);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr plufbx_blend_channel_name(IntPtr s, int i);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int    plufbx_blend_channel_node(IntPtr s, int i);
        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern int plufbx_anim_sample(IntPtr s, int k, double t0, double dt, int frames,
                                                      int[] nodes, int nNodes, float[] world, float[] weights,
                                                      byte[] err, int errCap);

        /// <summary>
        /// plufbx.dll を呼べるか確かめ、ufbx の版を返す。
        /// 呼べないとき false と理由（DLL が無い・関数が無い・形式が違う）。
        /// </summary>
        public static bool TryGetVersion(out string version, out string reason)
        {
            version = "";
            reason  = null;
            try
            {
                uint v = plufbx_version();
                // ufbx_pack_version(major, minor, patch) = major*1000000 + minor*1000 + patch（ufbx.h）
                version = $"{v / 1000000u}.{v / 1000u % 1000u}.{v % 1000u}";
                return true;
            }
            catch (DllNotFoundException e)        { reason = "plufbx.dll がありません: " + e.Message; }
            catch (EntryPointNotFoundException e) { reason = "plufbx.dll に plufbx_version がありません: " + e.Message; }
            catch (BadImageFormatException e)     { reason = "plufbx.dll の形式が違います: " + e.Message; }
            return false;
        }

        /// <summary>NUL 終端の UTF-8 → string。</summary>
        internal static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            if (len == 0) return "";
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        /// <summary>ufbx_matrix の 12 個（列 0〜3 の xyz）→ Matrix4x4。</summary>
        internal static Matrix4x4 ToMatrix(double[] m)
        {
            var r = Matrix4x4.identity;
            for (int c = 0; c < 4; c++)
            {
                r[0, c] = (float)m[c * 3 + 0];
                r[1, c] = (float)m[c * 3 + 1];
                r[2, c] = (float)m[c * 3 + 2];
            }
            return r;
        }
    }

    /// <summary>読み込んだ ufbx のシーン（plufbx の口の薄い包み）。</summary>
    public sealed class UfbxScene : IDisposable
    {
        private IntPtr _s;

        public int NodeCount      { get; }
        public int MeshCount      { get; }
        public int MaterialCount  { get; }
        public int TextureCount   { get; }
        public int AnimStackCount { get; }
        /// <summary>元の上の軸（ufbx_coordinate_axis：0=+X 1=-X 2=+Y 3=-Y 4=+Z 5=-Z 6=不明）。</summary>
        public int    OriginalAxisUp      { get; }
        public double OriginalUnitMeters  { get; }

        private UfbxScene(IntPtr s)
        {
            _s = s;
            var info = new int[8];
            UfbxNative.plufbx_scene_info(_s, info);
            NodeCount          = info[0];
            MeshCount          = info[1];
            MaterialCount      = info[2];
            TextureCount       = info[3];
            AnimStackCount     = info[4];
            OriginalAxisUp     = info[5];
            OriginalUnitMeters = info[6] / 1000000.0;
        }

        /// <summary>ファイルを読む。失敗時は null と理由。</summary>
        public static UfbxScene Load(string path, out string error)
        {
            error = null;
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception e) { error = $"ファイルを読めません: {e.Message}"; return null; }

            var err  = new byte[1024];
            var name = Encoding.UTF8.GetBytes(Path.GetFullPath(path) + "\0");
            IntPtr s;
            try { s = UfbxNative.plufbx_load_memory(data, data.LongLength, name, err, err.Length); }
            catch (DllNotFoundException e)        { error = "plufbx.dll がありません: " + e.Message; return null; }
            catch (EntryPointNotFoundException e) { error = "plufbx.dll が古い（読み込みの口がありません）: " + e.Message; return null; }

            if (s == IntPtr.Zero)
            {
                int n = Array.IndexOf(err, (byte)0);
                error = "ufbx: " + Encoding.UTF8.GetString(err, 0, n < 0 ? err.Length : n).Trim();
                return null;
            }
            return new UfbxScene(s);
        }

        public void Dispose()
        {
            if (_s != IntPtr.Zero) { UfbxNative.plufbx_free(_s); _s = IntPtr.Zero; }
        }

        // ---- ノード ----

        public struct NodeInfo
        {
            public string    Name;
            public int       Parent;          // -1 = シーンの根
            public int       Mesh;            // -1 = 無し
            public bool      HasSkeleton;     // スケルトン属性（FbxSkeleton）を持つ
            public int       AttribType;      // ufbx_element_type
            public int       ExplicitBind;    // 1 = クラスタのリンク行列, 2 = バインドポーズ, 0 = 無し
            public bool      IsRoot;          // シーンの根
            public int       MaterialSlots;
            public int       ChildCount;
            public Matrix4x4 World;           // node_to_world
            public Matrix4x4 Bind;            // 明示のバインド（無ければ World）
            public Matrix4x4 GeometryToNode;
        }

        public NodeInfo Node(int i)
        {
            var ints = new int[8];
            var w = new double[12]; var b = new double[12]; var g = new double[12];
            UfbxNative.plufbx_node_info(_s, i, ints, w, b, g);
            return new NodeInfo
            {
                Name           = UfbxNative.Utf8(UfbxNative.plufbx_node_name(_s, i)),
                Parent         = ints[0],
                Mesh           = ints[1],
                HasSkeleton    = ints[2] != 0,
                AttribType     = ints[3],
                ExplicitBind   = ints[4],
                IsRoot         = ints[5] != 0,
                MaterialSlots  = ints[6],
                ChildCount     = ints[7],
                World          = UfbxNative.ToMatrix(w),
                Bind           = UfbxNative.ToMatrix(b),
                GeometryToNode = UfbxNative.ToMatrix(g),
            };
        }

        public int NodeMaterial(int node, int slot) => UfbxNative.plufbx_node_material(_s, node, slot);

        // ---- メッシュ ----

        public struct MeshInfo
        {
            public int  VertexCount, FaceCount, IndexCount;
            public bool HasNormals, HasUV;
            public int  SkinDeformerCount, ClusterCount, BlendChannelCount;
        }

        public MeshInfo Mesh(int mi)
        {
            var o = new int[8];
            UfbxNative.plufbx_mesh_info(_s, mi, o);
            return new MeshInfo
            {
                VertexCount = o[0], FaceCount = o[1], IndexCount = o[2],
                HasNormals = o[3] != 0, HasUV = o[4] != 0,
                SkinDeformerCount = o[5], ClusterCount = o[6], BlendChannelCount = o[7],
            };
        }

        public float[] MeshPositions(int mi, int vertexCount)
        {
            var a = new float[vertexCount * 3];
            UfbxNative.plufbx_mesh_positions(_s, mi, a);
            return a;
        }

        public void MeshFaces(int mi, int faceCount, out int[] sizes, out int[] mats)
        {
            sizes = new int[faceCount];
            mats  = new int[faceCount];
            UfbxNative.plufbx_mesh_faces(_s, mi, sizes, mats);
        }

        public void MeshCorners(int mi, int indexCount, out int[] vidx, out float[] nrm, out float[] uv)
        {
            vidx = new int[indexCount];
            nrm  = new float[indexCount * 3];
            uv   = new float[indexCount * 2];
            UfbxNative.plufbx_mesh_corners(_s, mi, vidx, nrm, uv);
        }

        /// <summary>先頭スキンのクラスタ。戻り値はボーンのノード索引（-1 = 無し）。</summary>
        public int MeshCluster(int mi, int c, out Matrix4x4 geometryToBone, out Matrix4x4 bindToWorld)
        {
            var g = new double[12]; var b = new double[12];
            int node = UfbxNative.plufbx_mesh_cluster(_s, mi, c, g, b);
            geometryToBone = UfbxNative.ToMatrix(g);
            bindToWorld    = UfbxNative.ToMatrix(b);
            return node;
        }

        /// <summary>先頭スキンのウェイト（制御点ごとに重い順 maxPer 個）。戻り値は maxPer を超えていた制御点の数。</summary>
        public int MeshSkinWeights(int mi, int vertexCount, int maxPer, out int[] cluster, out float[] weight)
        {
            cluster = new int[vertexCount * maxPer];
            weight  = new float[vertexCount * maxPer];
            return UfbxNative.plufbx_mesh_skin_weights(_s, mi, maxPer, cluster, weight);
        }

        public string MeshBlendName(int mi, int k) => UfbxNative.Utf8(UfbxNative.plufbx_mesh_blend_name(_s, mi, k));

        public void MeshBlendOffsets(int mi, int k, out int[] vidx, out float[] d3)
        {
            int n = UfbxNative.plufbx_mesh_blend_offsets(_s, mi, k, null, null);
            vidx = new int[n];
            d3   = new float[n * 3];
            if (n > 0) UfbxNative.plufbx_mesh_blend_offsets(_s, mi, k, vidx, d3);
        }

        // ---- 材質・テクスチャ ----

        public string MaterialName(int i) => UfbxNative.Utf8(UfbxNative.plufbx_material_name(_s, i));

        /// <summary>vals[13]・tex[4] の意味は plufbx_material_info のコメント。</summary>
        public void MaterialInfo(int i, out float[] vals, out int[] tex)
        {
            vals = new float[13];
            tex  = new int[4];
            UfbxNative.plufbx_material_info(_s, i, vals, tex);
        }

        /// <summary>which: 0 = 名前, 1 = filename（解決済み）, 2 = absolute_filename, 3 = relative_filename</summary>
        public string TextureString(int i, int which) => UfbxNative.Utf8(UfbxNative.plufbx_texture_string(_s, i, which));

        public void TextureInfo(int i, out int contentSize, out int videoIndex, out float[] st)
        {
            var ints = new int[3];
            st = new float[4];
            UfbxNative.plufbx_texture_info(_s, i, ints, st);
            contentSize = ints[0];
            videoIndex  = ints[2];
        }

        public byte[] TextureContent(int i, int size)
        {
            var b = new byte[size];
            int n = UfbxNative.plufbx_texture_content(_s, i, b, size);
            if (n != size) Array.Resize(ref b, n);
            return b;
        }

        // ---- アニメーション ----

        public string AnimStackName(int k) => UfbxNative.Utf8(UfbxNative.plufbx_anim_stack_name(_s, k));

        /// <summary>テイク k の始まり・終わり（秒）とシーンの fps。</summary>
        public void AnimStackInfo(int k, out double begin, out double end, out double fps)
        {
            var o = new double[3];
            UfbxNative.plufbx_anim_stack_info(_s, k, o);
            begin = o[0]; end = o[1]; fps = o[2];
        }

        public int    BlendChannelCount        => UfbxNative.plufbx_blend_channel_count(_s);
        public string BlendChannelName(int i)  => UfbxNative.Utf8(UfbxNative.plufbx_blend_channel_name(_s, i));
        /// <summary>チャンネルを持つメッシュの最初のインスタンスのノード索引（-1 = 無し）。</summary>
        public int    BlendChannelNode(int i)  => UfbxNative.plufbx_blend_channel_node(_s, i);

        /// <summary>
        /// テイク k を t0 + f·dt で評価する。world は frames·nodes.Length·12、weights は frames·BlendChannelCount（null 可）。
        /// 戻り値は評価できたフレーム数。足りなければ error に理由。
        /// </summary>
        public int AnimSample(int k, double t0, double dt, int frames, int[] nodes,
                              float[] world, float[] weights, out string error)
        {
            var err = new byte[512];
            int n = UfbxNative.plufbx_anim_sample(_s, k, t0, dt, frames, nodes, nodes.Length, world, weights, err, err.Length);
            int z = Array.IndexOf(err, (byte)0);
            error = n < frames ? Encoding.UTF8.GetString(err, 0, z < 0 ? err.Length : z) : null;
            return n;
        }
    }
}
