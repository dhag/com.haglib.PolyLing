// FbxScene.cs
// ============================================================
// FBX 読み込み（段 8）の口。以前の UfbxScene（ufbx 0.23.1 ＋ plufbx.dll）と同じ口・同じ値を C# だけで持つ。
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。
//   読み取り FbxBinaryReader / FbxAsciiReader → 表 FbxSceneReader → 評価 FbxTransformEval。
//   座標は右手系・Y 上・前 +Z・単位 m（Unity への X 反転は呼び出し側）。
//   行列は Unity の Matrix4x4（列ベクトル。4 行目は 0, 0, 0, 1）。倍精度で計算して float に丸める。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Fbx
{
    public sealed class FbxScene : IDisposable
    {
        private readonly FbxSceneData _s;

        public int NodeCount      => _s.Nodes.Count;
        public int MeshCount      => _s.Meshes.Count;
        public int MaterialCount  => _s.Materials.Count;
        public int TextureCount   => _s.Textures.Count;
        public int AnimStackCount => _s.Stacks.Count;
        /// <summary>元の上の軸（0=+X 1=-X 2=+Y 3=-Y 4=+Z 5=-Z 6=不明。ufbx_coordinate_axis と同じ）。</summary>
        public int    OriginalAxisUp     => _s.OriginalAxisUp;
        public double OriginalUnitMeters { get; }
        /// <summary>読み込みの警告（読み飛ばした層など）。</summary>
        public IReadOnlyList<string> Warnings => _s.Warnings;

        private FbxScene(FbxSceneData s)
        {
            _s = s;
            // 以前の口（plufbx_scene_info）と同じく μm 単位の整数に丸める
            OriginalUnitMeters = (int)(s.OriginalUnitMeters * 1000000.0 + 0.5) / 1000000.0;
        }

        /// <summary>ファイルを読む。失敗時は null と理由。</summary>
        public static FbxScene Load(string path, out string error)
        {
            error = null;
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception e) { error = $"ファイルを読めません: {e.Message}"; return null; }

            bool ascii = !FbxBinaryReader.IsBinary(data);
            int version;
            var top = ascii ? FbxAsciiReader.Read(data, out version, out error) : FbxBinaryReader.Read(data, out version, out error);
            if (top == null) return null;
            var s = FbxSceneReader.Build(top, ascii, version, Path.GetFullPath(path), out error);
            if (s == null) return null;
            return new FbxScene(s);
        }

        public void Dispose() { }

        // ---- ノード ----

        public struct NodeInfo
        {
            public string    Name;
            public int       Parent;          // -1 = シーンの根
            public int       Mesh;            // -1 = 無し
            public bool      HasSkeleton;     // スケルトン属性（LimbNode など）を持つ
            public int       AttribType;      // ufbx_element_type の値
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
            var n = _s.Nodes[i];
            int bindKind = ExplicitBind(n, out var bind);
            return new NodeInfo
            {
                Name           = n.Name,
                Parent         = n.Parent != null ? n.Parent.TypedId : -1,
                Mesh           = n.Mesh != null ? n.Mesh.TypedId : -1,
                HasSkeleton    = n.Bone != null,
                AttribType     = n.AttribType,
                ExplicitBind   = bindKind,
                IsRoot         = n.IsRoot,
                MaterialSlots  = n.Materials.Count > 0 ? n.Materials.Count : n.Mesh != null ? n.Mesh.Materials.Count : 0,
                ChildCount     = n.Children.Count,
                World          = n.NodeToWorld.ToUnity(),
                Bind           = (bindKind != 0 ? bind : n.NodeToWorld).ToUnity(),
                GeometryToNode = n.GeometryToNode.ToUnity(),
            };
        }

        /// <summary>明示のバインド：クラスタのリンク行列が最優先、無ければバインドポーズ（以前の plufbx の explicit_bind と同じ）。</summary>
        private int ExplicitBind(FbxNodeEl n, out DMat m)
        {
            foreach (var c in _s.Clusters)
                if (c.BoneNode == n) { m = c.BindToWorld; return 1; }
            foreach (var p in _s.Poses)
            {
                if (!p.IsBindPose) continue;
                foreach (var (node, bw) in p.Bones)
                    if (node == n) { m = bw; return 2; }
            }
            m = n.NodeToWorld;
            return 0;
        }

        public int NodeMaterial(int node, int slot)
        {
            var n = _s.Nodes[node];
            var list = n.Materials.Count > 0 ? n.Materials : n.Mesh?.Materials;
            if (list == null || slot < 0 || slot >= list.Count || list[slot] == null) return -1;
            return list[slot].TypedId;
        }

        // ---- メッシュ ----

        public struct MeshInfo
        {
            public int  VertexCount, FaceCount, IndexCount;
            public bool HasNormals, HasUV;
            public int  SkinDeformerCount, ClusterCount, BlendChannelCount;
        }

        public MeshInfo Mesh(int mi)
        {
            var m = _s.Meshes[mi];
            int channels = 0;
            foreach (var b in m.Blends) channels += b.Channels.Count;
            return new MeshInfo
            {
                VertexCount = m.Vertices.Length,
                FaceCount = m.Faces.Length,
                IndexCount = m.VertexIndices.Length,
                HasNormals = m.NormalIndices != null,
                HasUV = m.UvIndices != null,
                SkinDeformerCount = m.Skins.Count,
                ClusterCount = m.Skins.Count > 0 ? m.Skins[0].Clusters.Count : 0,
                BlendChannelCount = channels,
            };
        }

        public float[] MeshPositions(int mi, int vertexCount)
        {
            var m = _s.Meshes[mi];
            var a = new float[vertexCount * 3];
            for (int v = 0; v < m.Vertices.Length && v < vertexCount; v++)
            {
                a[v * 3] = (float)m.Vertices[v].x; a[v * 3 + 1] = (float)m.Vertices[v].y; a[v * 3 + 2] = (float)m.Vertices[v].z;
            }
            return a;
        }

        public void MeshFaces(int mi, int faceCount, out int[] sizes, out int[] mats)
        {
            var m = _s.Meshes[mi];
            sizes = new int[faceCount];
            mats  = new int[faceCount];
            for (int f = 0; f < m.Faces.Length && f < faceCount; f++)
            {
                sizes[f] = m.Faces[f].Count;
                mats[f] = m.FaceMaterial != null ? m.FaceMaterial[f] : 0;
            }
        }

        public void MeshCorners(int mi, int indexCount, out int[] vidx, out float[] nrm, out float[] uv)
        {
            var m = _s.Meshes[mi];
            vidx = new int[indexCount];
            nrm  = new float[indexCount * 3];
            uv   = new float[indexCount * 2];
            for (int i = 0; i < m.VertexIndices.Length && i < indexCount; i++)
            {
                vidx[i] = m.VertexIndices[i];
                if (m.NormalIndices != null)
                {
                    var n = m.NormalValues[m.NormalIndices[i]];
                    nrm[i * 3] = (float)n.x; nrm[i * 3 + 1] = (float)n.y; nrm[i * 3 + 2] = (float)n.z;
                }
                if (m.UvIndices != null)
                {
                    int k = m.UvIndices[i];
                    uv[i * 2] = (float)m.UvValues[k * 2]; uv[i * 2 + 1] = (float)m.UvValues[k * 2 + 1];
                }
            }
        }

        /// <summary>先頭スキンのクラスタ。戻り値はボーンのノード索引（-1 = 無し）。</summary>
        public int MeshCluster(int mi, int c, out Matrix4x4 geometryToBone, out Matrix4x4 bindToWorld)
        {
            var cl = _s.Meshes[mi].Skins[0].Clusters[c];
            geometryToBone = cl.GeometryToBone.ToUnity();
            bindToWorld = cl.BindToWorld.ToUnity();
            return cl.BoneNode != null ? cl.BoneNode.TypedId : -1;
        }

        /// <summary>先頭スキンのウェイト（制御点ごとに重い順 maxPer 個）。戻り値は maxPer を超えていた制御点の数。</summary>
        public int MeshSkinWeights(int mi, int vertexCount, int maxPer, out int[] cluster, out float[] weight)
        {
            var m = _s.Meshes[mi];
            var sk = m.Skins[0];
            cluster = new int[vertexCount * maxPer];
            weight  = new float[vertexCount * maxPer];
            int over = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                int n = 0;
                if (v < sk.Weights.Length)
                {
                    var list = sk.Weights[v];
                    if (list.Count > maxPer) over++;
                    for (int k = 0; k < list.Count && n < maxPer; k++, n++)
                    {
                        cluster[v * maxPer + n] = list[k].cluster;
                        weight[v * maxPer + n] = (float)list[k].weight;
                    }
                }
                for (; n < maxPer; n++) { cluster[v * maxPer + n] = -1; weight[v * maxPer + n] = 0f; }
            }
            return over;
        }

        private FbxChannelEl MeshChannel(int mi, int k)
        {
            foreach (var b in _s.Meshes[mi].Blends)
            {
                if (k < b.Channels.Count) return b.Channels[k];
                k -= b.Channels.Count;
            }
            return null;
        }

        public string MeshBlendName(int mi, int k) => MeshChannel(mi, k)?.Name ?? "";

        public void MeshBlendOffsets(int mi, int k, out int[] vidx, out float[] d3)
        {
            var sh = MeshChannel(mi, k)?.Target;
            int n = sh?.Indexes.Length ?? 0;
            vidx = new int[n];
            d3 = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                vidx[i] = sh.Indexes[i];
                d3[i * 3] = (float)sh.Offsets[i].x; d3[i * 3 + 1] = (float)sh.Offsets[i].y; d3[i * 3 + 2] = (float)sh.Offsets[i].z;
            }
        }

        // ---- 材質・テクスチャ ----

        public string MaterialName(int i) => _s.Materials[i].Name;

        /// <summary>
        /// vals[0..12] = 拡散色 rgb, 拡散係数, 透明色 rgb, 透明係数, 発光色 rgb, 発光係数, バンプ係数
        /// tex[0..3]  = 拡散, 法線マップ, バンプ, 発光 のテクスチャ索引（-1 = 無し）
        /// （ufbx.c 19963 の表の順に引き、後のものが前のものを上書きする。値の無い係数は色が 0 でなければ 1。）
        /// </summary>
        public void MaterialInfo(int i, out float[] vals, out int[] tex)
        {
            var m = _s.Materials[i];
            var diffuse = Map(m, "Diffuse", "DiffuseColor");
            var diffuseF = Map(m, "DiffuseFactor");
            var transp = Map(m, "Transparent", "TransparentColor");
            var transpF = Map(m, "TransparentFactor", "TransparencyFactor");
            var emis = Map(m, "Emissive", "EmissiveColor");
            var emisF = Map(m, "EmissiveFactor");
            var normal = Map(m, "NormalMap");
            var bump = Map(m, "Bump");
            var bumpF = Map(m, "BumpFactor");
            UpdateFactor(ref diffuseF, diffuse);
            UpdateFactor(ref transpF, transp);
            UpdateFactor(ref emisF, emis);
            vals = new float[13];
            vals[0] = (float)diffuse.v.x; vals[1] = (float)diffuse.v.y; vals[2] = (float)diffuse.v.z; vals[3] = (float)diffuseF.v.x;
            vals[4] = (float)transp.v.x; vals[5] = (float)transp.v.y; vals[6] = (float)transp.v.z; vals[7] = (float)transpF.v.x;
            vals[8] = (float)emis.v.x; vals[9] = (float)emis.v.y; vals[10] = (float)emis.v.z; vals[11] = (float)emisF.v.x;
            vals[12] = (float)bumpF.v.x;
            tex = new[] { FileTexture(diffuse.tex), FileTexture(normal.tex), FileTexture(bump.tex), FileTexture(emis.tex) };
        }

        private struct MatMap { public DVec3 v; public bool has; public FbxTextureEl tex; }

        private static MatMap Map(FbxMaterialEl m, params string[] names)
        {
            var r = new MatMap();
            foreach (var name in names)
            {
                var p = m.Props.Find(name);
                if (p != null && !p.IsReference) { r.v = p.Vec3; r.has = true; }
                foreach (var (prop, t) in m.Textures)
                    if (prop == name) { r.tex = t; break; }
            }
            return r;
        }

        private static void UpdateFactor(ref MatMap factor, MatMap color)
        {
            if (factor.has) return;
            factor.v.x = color.has && !(color.v.x == 0.0 && color.v.y == 0.0 && color.v.z == 0.0) ? 1.0 : 0.0;
        }

        private static int FileTexture(FbxTextureEl t)
        {
            if (t == null) return -1;
            if (t.Layered && t.FileTextures.Count > 0) t = t.FileTextures[0];
            return t.TypedId;
        }

        /// <summary>which: 0 = 名前, 1 = filename（読み込んだファイルからの解決済み）, 2 = absolute_filename, 3 = relative_filename</summary>
        public string TextureString(int i, int which)
        {
            var t = _s.Textures[i];
            switch (which)
            {
                case 0: return t.Name;
                case 1: return t.Filename;
                case 2: return t.AbsoluteFilename;
                default: return t.RelativeFilename;
            }
        }

        public void TextureInfo(int i, out int contentSize, out int videoIndex, out float[] st)
        {
            var t = _s.Textures[i];
            contentSize = t.Content.Length;
            videoIndex = t.Video != null ? t.Video.TypedId : -1;
            var sc = t.Props.FindVec3("Scaling", DVec3.One);
            var tr = t.Props.FindVec3("Translation", DVec3.Zero);
            st = new[] { (float)sc.x, (float)sc.y, (float)tr.x, (float)tr.y };
        }

        public byte[] TextureContent(int i, int size)
        {
            var c = _s.Textures[i].Content;
            int n = Math.Min(size, c.Length);
            var b = new byte[n];
            Buffer.BlockCopy(c, 0, b, 0, n);
            return b;
        }

        // ---- アニメーション ----

        public string AnimStackName(int k) => _s.Stacks[k].Name;

        /// <summary>テイク k の始まり・終わり（秒）とシーンの fps。</summary>
        public void AnimStackInfo(int k, out double begin, out double end, out double fps)
        {
            begin = _s.Stacks[k].TimeBegin;
            end = _s.Stacks[k].TimeEnd;
            fps = _s.FramesPerSecond;
        }

        public int    BlendChannelCount       => _s.Channels.Count;
        public string BlendChannelName(int i) => _s.Channels[i].Name;

        /// <summary>チャンネルを持つメッシュの最初のインスタンスのノード索引（-1 = 無し）。</summary>
        public int BlendChannelNode(int i)
        {
            var ch = _s.Channels[i];
            foreach (var m in _s.Meshes)
                foreach (var b in m.Blends)
                    if (b.Channels.Contains(ch)) return m.Instances.Count > 0 ? m.Instances[0].TypedId : -1;
            return -1;
        }

        /// <summary>
        /// テイク k を t0 + f·dt で評価する。world は frames·nodes.Length·12、weights は frames·BlendChannelCount（null 可）。
        /// 戻り値は評価できたフレーム数。
        /// </summary>
        public int AnimSample(int k, double t0, double dt, int frames, int[] nodes, float[] world, float[] weights, out string error)
        {
            error = null;
            var stack = _s.Stacks[k];
            int nCh = _s.Channels.Count;
            // 動くプロパティとレイヤは時刻によらないので先に拾う
            var nodePlans = new FbxTransformEval.NodePlan[_s.Nodes.Count];
            for (int i = 0; i < nodePlans.Length; i++) nodePlans[i] = FbxTransformEval.PlanNode(stack, _s.Nodes[i]);
            FbxTransformEval.PropPlan[][] blendPlans = null;
            if (weights != null)
            {
                blendPlans = new FbxTransformEval.PropPlan[nCh][];
                for (int c = 0; c < nCh; c++) blendPlans[c] = FbxTransformEval.PlanBlend(stack, _s.Channels[c]);
            }
            try
            {
                for (int f = 0; f < frames; f++)
                {
                    double t = t0 + f * dt;
                    foreach (var plan in nodePlans) FbxTransformEval.UpdateNodePlanned(stack, plan, t, _s.KTimeSecond);
                    for (int q = 0; q < nodes.Length; q++)
                    {
                        var m = _s.Nodes[nodes[q]].NodeToWorld;
                        int o = (f * nodes.Length + q) * 12;
                        world[o + 0] = (float)m.m00; world[o + 1] = (float)m.m10; world[o + 2] = (float)m.m20;
                        world[o + 3] = (float)m.m01; world[o + 4] = (float)m.m11; world[o + 5] = (float)m.m21;
                        world[o + 6] = (float)m.m02; world[o + 7] = (float)m.m12; world[o + 8] = (float)m.m22;
                        world[o + 9] = (float)m.m03; world[o + 10] = (float)m.m13; world[o + 11] = (float)m.m23;
                    }
                    if (weights != null)
                        for (int c = 0; c < nCh; c++)
                            weights[f * nCh + c] = (float)FbxTransformEval.BlendWeightPlanned(stack, _s.Channels[c], blendPlans[c], t, _s.KTimeSecond);
                }
            }
            finally
            {
                // 静的な姿勢へ戻す
                foreach (var n in _s.Nodes) FbxTransformEval.UpdateNode(n, n.Props);
            }
            return frames;
        }
    }
}
