// SubdivisionOps.cs
// 細分化曲面（Catmull-Clark）の計算。親（ケージ）から滑らかな子のメッシュと、
// 「子の頂点＝親の頂点の重み付き和」の表（ステンシル）を作る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【規則】Blender のサブディビジョンサーフェス（Catmull-Clark）と同じ式。
//   面の点   … 面の頂点の平均
//   辺の点   … 2 枚の面に挟まれた辺は (両端 + 両側の面の点) / 4。
//               境界辺（面 1 枚）と 3 枚以上の面が付く辺は中点
//   頂点の点 … 内部の頂点は (Q + 2R + (n-3)P) / n
//               （Q=周りの面の点の平均、R=周りの辺の中点の平均、n=辺の数）。
//               境界の頂点（境界辺がちょうど 2 本）は 3/4 P + 1/8 (両隣の境界頂点)。
//               それ以外（3 枚以上の面が付く辺を持つ・境界辺が 2 本でない）は動かさない
//   三角形・n 角形も同じ式で扱い、出力はすべて四角形になる。
//
// 【UV】面の中で線形に補間する（角は元の UV、辺は 2 角の中点、中心は全角の平均）。
//   UV の継ぎ目は面ごとに持つ UV がそのまま割れ目になる。
//
// 【ステンシル】回数ごとに疎な行列を 1 枚持ち、位置だけの更新はそれを順に掛けて求める。
//   親の面構成が変わらない限り、頂点を動かしても行列は作り直さない。
//
// 【頂点 ID は振らない】子は派生物で、作り直しのたびに中身を入れ替える。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>細分化 1 回ぶんの疎な行列（行＝出力頂点、列＝入力頂点）。</summary>
    public sealed class SubdivisionStencilLevel
    {
        public int   InputCount;
        public int   RowCount;
        public int[] RowStart;   // 長さ RowCount + 1
        public int[] Col;
        public float[] Weight;
    }

    /// <summary>親の頂点位置から子の頂点位置を求める表。回数ぶんの行列を順に掛ける。</summary>
    public sealed class SubdivisionStencil
    {
        public readonly List<SubdivisionStencilLevel> Levels = new List<SubdivisionStencilLevel>();

        /// <summary>親の頂点数（最初の行列の列数）。</summary>
        public int InputCount => Levels.Count > 0 ? Levels[0].InputCount : 0;

        /// <summary>子の頂点数（最後の行列の行数）。</summary>
        public int OutputCount => Levels.Count > 0 ? Levels[Levels.Count - 1].RowCount : 0;

        /// <summary>親の頂点位置から子の頂点位置を求める。数が合わなければ null。</summary>
        public Vector3[] Apply(Vector3[] cagePositions)
        {
            if (cagePositions == null || Levels.Count == 0) return null;
            if (cagePositions.Length != InputCount) return null;

            Vector3[] cur = cagePositions;
            for (int l = 0; l < Levels.Count; l++)
            {
                var lv  = Levels[l];
                var nxt = new Vector3[lv.RowCount];
                for (int r = 0; r < lv.RowCount; r++)
                {
                    Vector3 p = Vector3.zero;
                    int end = lv.RowStart[r + 1];
                    for (int k = lv.RowStart[r]; k < end; k++)
                        p += cur[lv.Col[k]] * lv.Weight[k];
                    nxt[r] = p;
                }
                cur = nxt;
            }
            return cur;
        }
    }

    public static class SubdivisionOps
    {
        public const int LevelMin = 1;
        public const int LevelMax = 4;

        // ================================================================
        // 作業用の面構成（回ごとに作り直す）
        // ================================================================

        private sealed class Work
        {
            public int VertexCount;
            public readonly List<int[]>     Faces = new List<int[]>();
            public readonly List<Vector2[]> Uvs   = new List<Vector2[]>();
            public readonly List<int>       Mats  = new List<int>();
        }

        /// <summary>
        /// 細分化した子のメッシュとステンシルを作る。
        /// 3 頂点未満の面（線分）は対象外。面が 1 枚も無ければ失敗。
        /// </summary>
        public static bool TryBuild(
            MeshObject cage, int level,
            out MeshObject result, out SubdivisionStencil stencil, out string error)
        {
            result = null; stencil = null; error = null;

            if (cage == null) { error = "親のメッシュがありません"; return false; }
            level = Mathf.Clamp(level, LevelMin, LevelMax);

            var w = new Work { VertexCount = cage.VertexCount };
            for (int fi = 0; fi < cage.FaceCount; fi++)
            {
                var f = cage.Faces[fi];
                if (f == null || f.VertexCount < 3) continue;

                int k = f.VertexCount;
                var vs = new int[k];
                var uv = new Vector2[k];
                bool ok = true;
                for (int i = 0; i < k; i++)
                {
                    int vi = f.VertexIndices[i];
                    if (vi < 0 || vi >= cage.VertexCount) { ok = false; break; }
                    vs[i] = vi;
                    var vert = cage.Vertices[vi];
                    int slot = (f.UVIndices != null && i < f.UVIndices.Count) ? f.UVIndices[i] : 0;
                    uv[i] = (vert.UVs != null && slot >= 0 && slot < vert.UVs.Count) ? vert.UVs[slot] : Vector2.zero;
                }
                if (!ok) continue;

                w.Faces.Add(vs);
                w.Uvs.Add(uv);
                w.Mats.Add(f.MaterialIndex);
            }

            if (w.Faces.Count == 0) { error = "細分化できる面（3 頂点以上）がありません"; return false; }

            stencil = new SubdivisionStencil();
            for (int l = 0; l < level; l++)
                w = SubdivideOnce(w, stencil);

            var cagePos = new Vector3[cage.VertexCount];
            for (int i = 0; i < cagePos.Length; i++) cagePos[i] = cage.Vertices[i].Position;
            var pos = stencil.Apply(cagePos);

            result = BuildMeshObject(w, pos, cage.Name);
            return true;
        }

        /// <summary>
        /// 親の面構成・UV・材質の署名。頂点位置は含まない。
        /// 子を作り直すかどうか（位置の書き換えだけで済むか）の判定に使う。
        /// </summary>
        public static ulong TopologySignature(MeshObject cage)
        {
            if (cage == null) return 0UL;

            ulong h = 14695981039346656037UL;
            void Mix(int v) { unchecked { h ^= (uint)v; h *= 1099511628211UL; } }

            Mix(cage.VertexCount);
            for (int fi = 0; fi < cage.FaceCount; fi++)
            {
                var f = cage.Faces[fi];
                if (f == null || f.VertexCount < 3) continue;
                Mix(f.VertexCount);
                Mix(f.MaterialIndex);
                for (int i = 0; i < f.VertexCount; i++)
                {
                    int vi = f.VertexIndices[i];
                    Mix(vi);
                    int slot = (f.UVIndices != null && i < f.UVIndices.Count) ? f.UVIndices[i] : 0;
                    Mix(slot);
                    if (vi >= 0 && vi < cage.VertexCount)
                    {
                        var uvs = cage.Vertices[vi].UVs;
                        if (uvs != null && slot >= 0 && slot < uvs.Count)
                        {
                            Mix(BitConverter.SingleToInt32Bits(uvs[slot].x));
                            Mix(BitConverter.SingleToInt32Bits(uvs[slot].y));
                        }
                    }
                }
            }
            return h;
        }

        // ================================================================
        // 1 回ぶん
        // ================================================================

        private static long EdgeKey(int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static Work SubdivideOnce(Work src, SubdivisionStencil stencil)
        {
            int nv = src.VertexCount;
            int nf = src.Faces.Count;

            // ── 辺と隣接 ──
            var edgeIndex = new Dictionary<long, int>();
            var edgeV0 = new List<int>();
            var edgeV1 = new List<int>();
            var edgeFaces = new List<List<int>>();
            var vertEdges = new List<int>[nv];
            var vertFaces = new List<int>[nv];

            for (int f = 0; f < nf; f++)
            {
                var face = src.Faces[f];
                int k = face.Length;
                for (int i = 0; i < k; i++)
                {
                    int a = face[i], b = face[(i + 1) % k];

                    (vertFaces[a] ??= new List<int>()).Add(f);

                    long key = EdgeKey(a, b);
                    if (!edgeIndex.TryGetValue(key, out int e))
                    {
                        e = edgeV0.Count;
                        edgeIndex[key] = e;
                        edgeV0.Add(a); edgeV1.Add(b);
                        edgeFaces.Add(new List<int>(2));
                        (vertEdges[a] ??= new List<int>()).Add(e);
                        (vertEdges[b] ??= new List<int>()).Add(e);
                    }
                    edgeFaces[e].Add(f);
                }
            }
            int ne = edgeV0.Count;

            // ── 出力の並び：頂点の点（使われている頂点だけ）→ 辺の点 → 面の点 ──
            var vertOut = new int[nv];
            int nvOut = 0;
            for (int v = 0; v < nv; v++) vertOut[v] = vertFaces[v] != null ? nvOut++ : -1;
            int edgeBase = nvOut;
            int faceBase = edgeBase + ne;
            int rowCount = faceBase + nf;

            var rowStart = new int[rowCount + 1];
            var cols = new List<int>(rowCount * 6);
            var wts  = new List<float>(rowCount * 6);
            var acc  = new Dictionary<int, float>();
            int row = 0;

            void Add(int col, float wt)
            {
                acc.TryGetValue(col, out float cur);
                acc[col] = cur + wt;
            }
            void AddFacePoint(int f, float scale)
            {
                var face = src.Faces[f];
                float s = scale / face.Length;
                for (int i = 0; i < face.Length; i++) Add(face[i], s);
            }
            void Emit()
            {
                rowStart[row] = cols.Count;
                foreach (var kv in acc) { cols.Add(kv.Key); wts.Add(kv.Value); }
                acc.Clear();
                row++;
            }

            // 頂点の点
            for (int v = 0; v < nv; v++)
            {
                if (vertOut[v] < 0) continue;

                var es = vertEdges[v];
                var fs = vertFaces[v];
                int nE = es?.Count ?? 0;
                int nF = fs?.Count ?? 0;

                bool nonManifold = false;
                int boundary = 0;
                int b0 = -1, b1 = -1;
                for (int i = 0; i < nE; i++)
                {
                    int e = es[i];
                    int c = edgeFaces[e].Count;
                    if (c > 2) nonManifold = true;
                    else if (c == 1)
                    {
                        int other = edgeV0[e] == v ? edgeV1[e] : edgeV0[e];
                        if (boundary == 0) b0 = other; else if (boundary == 1) b1 = other;
                        boundary++;
                    }
                }

                if (!nonManifold && boundary == 0 && nE >= 3 && nF == nE)
                {
                    float n = nE;
                    Add(v, (n - 3f) / n);
                    float inv2 = 1f / (n * n);
                    for (int i = 0; i < nE; i++)
                    {
                        int e = es[i];
                        int other = edgeV0[e] == v ? edgeV1[e] : edgeV0[e];
                        Add(v, inv2);
                        Add(other, inv2);
                    }
                    for (int i = 0; i < nF; i++) AddFacePoint(fs[i], inv2);
                }
                else if (!nonManifold && boundary == 2)
                {
                    Add(v, 0.75f);
                    Add(b0, 0.125f);
                    Add(b1, 0.125f);
                }
                else
                {
                    Add(v, 1f);
                }
                Emit();
            }

            // 辺の点
            for (int e = 0; e < ne; e++)
            {
                var fs = edgeFaces[e];
                if (fs.Count == 2)
                {
                    Add(edgeV0[e], 0.25f);
                    Add(edgeV1[e], 0.25f);
                    AddFacePoint(fs[0], 0.25f);
                    AddFacePoint(fs[1], 0.25f);
                }
                else
                {
                    Add(edgeV0[e], 0.5f);
                    Add(edgeV1[e], 0.5f);
                }
                Emit();
            }

            // 面の点
            for (int f = 0; f < nf; f++)
            {
                AddFacePoint(f, 1f);
                Emit();
            }
            rowStart[rowCount] = cols.Count;

            stencil.Levels.Add(new SubdivisionStencilLevel
            {
                InputCount = nv,
                RowCount   = rowCount,
                RowStart   = rowStart,
                Col        = cols.ToArray(),
                Weight     = wts.ToArray(),
            });

            // ── 次の面構成 ──
            var dst = new Work { VertexCount = rowCount };
            for (int f = 0; f < nf; f++)
            {
                var face = src.Faces[f];
                var uv   = src.Uvs[f];
                int k    = face.Length;

                Vector2 uvCenter = Vector2.zero;
                for (int i = 0; i < k; i++) uvCenter += uv[i];
                uvCenter /= k;

                int fp = faceBase + f;
                for (int i = 0; i < k; i++)
                {
                    int ip = (i + k - 1) % k;
                    int inx = (i + 1) % k;
                    int vp  = vertOut[face[i]];
                    int ep  = edgeBase + edgeIndex[EdgeKey(face[i], face[inx])];
                    int epp = edgeBase + edgeIndex[EdgeKey(face[ip], face[i])];

                    dst.Faces.Add(new[] { vp, ep, fp, epp });
                    dst.Uvs.Add(new[]
                    {
                        uv[i],
                        (uv[i] + uv[inx]) * 0.5f,
                        uvCenter,
                        (uv[ip] + uv[i]) * 0.5f,
                    });
                    dst.Mats.Add(src.Mats[f]);
                }
            }
            return dst;
        }

        // ================================================================
        // MeshObject へ
        // ================================================================

        private static MeshObject BuildMeshObject(Work w, Vector3[] pos, string name)
        {
            var mo = new MeshObject(name);

            for (int v = 0; v < w.VertexCount; v++)
            {
                var vert = new Vertex(pos[v]);
                vert.UVs.Clear();
                vert.Normals.Clear();
                mo.Vertices.Add(vert);
            }

            for (int f = 0; f < w.Faces.Count; f++)
            {
                var vs = w.Faces[f];
                var uv = w.Uvs[f];
                var face = new Face { MaterialIndex = w.Mats[f] };
                for (int i = 0; i < vs.Length; i++)
                {
                    var vert = mo.Vertices[vs[i]];
                    int slot = vert.UVs.IndexOf(uv[i]);
                    if (slot < 0)
                    {
                        slot = vert.UVs.Count;
                        vert.UVs.Add(uv[i]);
                        vert.Normals.Add(Vector3.up);
                    }
                    face.VertexIndices.Add(vs[i]);
                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }
                mo.Faces.Add(face);
            }

            mo.RecalculateSmoothNormals();
            mo.InvalidatePositionCache();
            return mo;
        }

        /// <summary>
        /// 子の中身（頂点・面）を入れ替える。MeshObject の実体は差し替えない
        /// （型・階層・姿勢など MeshObject に委譲している属性を保つため）。
        /// </summary>
        public static void ReplaceContents(MeshObject dst, MeshObject src)
        {
            if (dst == null || src == null) return;
            dst.Vertices.Clear();
            dst.Faces.Clear();
            dst.Vertices.AddRange(src.Vertices);
            dst.Faces.AddRange(src.Faces);
            dst.LineGroups = new List<LineGroup>();
            dst.RebuildIdSets();
            dst.InvalidatePositionCache();
        }
    }
}
