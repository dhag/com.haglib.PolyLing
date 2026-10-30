// PipeBeltExtractor.cs
// パイプ（断面の輪を縦に積んだ筒）から梯子（基準ベルト）を取り出す。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 原型: N3GForms Form1tab15.cs の HairToBelt（NCSHAGLIB の髪パイプ生成「←パイプからの取り出し」）。
//
// 【パイプの頂点の並び】原型と同じ前提を置く。
//   1 本ぶんの頂点を番号の昇順に並べたとき
//     先頭 = 始点（根元の扇の要）
//     末尾 = 終点（毛先の扇の要）
//     間   = 断面の頂点 K 個 × 段 n（段 i の断面頂点 k は 1 + i*K + k 番目）
//   NCSHAGLIB の髪パイプ生成（CreatePipe_）と、旧互換の頂点順で作ったパイプがこの並びになる。
//   PMX で読んだときも 1 本ぶんの頂点は連続しているので、この並びのまま残る。
//
// 【1 本ずつに分ける】
//   オブジェクトに何本入っていてもよい。面でつながった頂点を 1 本とみなす。
//   並びが前提に合わない本（頂点数が K×n+2 でない・両端が扇の要でない）は飛ばし、理由を残す。
//
// 【出力の梯子】原型と同じ構成。BeltStackDetector（はしごの自動検索）がそのまま拾える。
//   頂点: 開始タグ 2 点、始点、(左0,右0) … (左m,右m)、終点
//   面  : 開始タグ三角形（タグ 2 点と始点）、開始三角形（始点・左0・右0）、
//         四角形の列、終了三角形（右m・左m・終点）
//   開始タグの 2 点は、始点に対して左0・右0 を点対称に折り返した位置に置く
//   （原型の「開始位置確認用逆三角」）。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>パイプから梯子を取り出すパラメータ。</summary>
    public struct PipeBeltExtractParams
    {
        public const int RingVertexCountMin = 3;
        public const int RingVertexCountMax = 64;
        public const int RungStepMin = 0;
        public const int RungStepMax = 64;

        [PLParam(TextKey = "MeshName", Description = "生成する梯子の描画オブジェクトの名前")]
        public string MeshName;

        [PLParam(TextKey = "PipeBeltRingVertexCount", Description = "パイプの断面 1 輪の頂点数",
                 Min = RingVertexCountMin, Max = RingVertexCountMax, Step = 1)]
        public int RingVertexCount;

        [PLParam(TextKey = "PipeBeltRailA", Description = "梯子の左レールにする断面頂点の番号（輪の中の 0 起点）",
                 Min = 0, Max = RingVertexCountMax - 1, Step = 1)]
        public int RailA;

        [PLParam(TextKey = "PipeBeltRailB", Description = "梯子の右レールにする断面頂点の番号（輪の中の 0 起点）",
                 Min = 0, Max = RingVertexCountMax - 1, Step = 1)]
        public int RailB;

        [PLParam(TextKey = "PipeBeltStartRung", Description = "何段目から取り出すか（0 起点）。残す段を指定したときは使わない",
                 Min = 0, Max = 4096, Step = 1)]
        public int StartRung;

        [PLParam(TextKey = "PipeBeltRungStep", Description = "何段飛ばしで取り出すか。0 で全段。残す段を指定したときは使わない",
                 Min = RungStepMin, Max = RungStepMax, Step = 1)]
        public int RungStep;

        [PLParam(TextKey = "PipeBeltKeepRungs", Description = "残す段の番号（0 起点）。空にすると開始段と飛ばし数で決める")]
        public int[] KeepRungs;

        [PLParam(TextKey = "PipeBeltReverse", Description = "梯子の面の向きを反転する")]
        public bool Reverse;

        public static PipeBeltExtractParams Default => new PipeBeltExtractParams
        {
            MeshName        = "PipeBelt",
            RingVertexCount = 8,
            RailA           = 3,
            RailB           = 4,
            StartRung       = 0,
            RungStep        = 0,
            KeepRungs       = System.Array.Empty<int>(),
            Reverse         = false,
        };
    }

    /// <summary>パイプ → 梯子。</summary>
    public static class PipeBeltExtractor
    {
        /// <summary>
        /// 直前の取り出しの説明（取り出した本数・飛ばした本と理由）。
        /// 表示専用。取り出しのたびに上書きするので、読むのは Build の直後に限る。
        /// </summary>
        public static string LastMessage { get; private set; } = "";

        /// <summary>直前の取り出しで梯子にできた本数。</summary>
        public static int LastBeltCount { get; private set; }

        /// <summary>
        /// 取り込み元の全メッシュから梯子を取り出し、1 つのメッシュにまとめて返す。
        /// 1 本も取り出せなければ null（理由は LastMessage）。
        /// </summary>
        public static MeshObject Build(PipeBeltExtractParams p, IReadOnlyList<MeshObject> sources)
        {
            LastBeltCount = 0;
            var log = new StringBuilder();

            int k = p.RingVertexCount;
            if (k < PipeBeltExtractParams.RingVertexCountMin)
            {
                LastMessage = $"断面の頂点数が小さすぎる（{k}）";
                return null;
            }
            if (p.RailA < 0 || p.RailA >= k || p.RailB < 0 || p.RailB >= k || p.RailA == p.RailB)
            {
                LastMessage = $"レールの頂点番号が不正（A={p.RailA} / B={p.RailB} / 断面の頂点数 {k}）";
                return null;
            }
            if (sources == null || sources.Count == 0)
            {
                LastMessage = "取り込み元のオブジェクトが無い";
                return null;
            }

            var result  = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "PipeBelt" : p.MeshName);
            int skipped = 0;

            for (int si = 0; si < sources.Count; si++)
            {
                var src = sources[si];
                if (src == null) continue;

                var comps = SplitComponents(src);
                for (int ci = 0; ci < comps.Count; ci++)
                {
                    string why = AppendBelt(result, src, comps[ci], p);
                    if (why == null) { LastBeltCount++; continue; }

                    skipped++;
                    if (skipped <= 20)
                        log.Append($"\n  オブジェクト {si} の {ci} 本目（頂点 {comps[ci].Count}）: {why}");
                }
            }

            string head = $"梯子 {LastBeltCount} 本を取り出した。飛ばした本 {skipped}";
            if (skipped > 20) log.Append($"\n  ほか {skipped - 20} 本");
            LastMessage = head + log;

            return LastBeltCount > 0 ? result : null;
        }

        // ================================================================
        // 1 本ずつに分ける
        // ================================================================

        /// <summary>面でつながった頂点の集まりを、頂点番号の昇順で返す。面に使われない頂点は含めない。</summary>
        private static List<List<int>> SplitComponents(MeshObject mo)
        {
            int n = mo.VertexCount;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var used = new bool[n];

            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face?.VertexIndices == null || face.VertexIndices.Count == 0) continue;

                int a = face.VertexIndices[0];
                if (a < 0 || a >= n) continue;
                used[a] = true;

                for (int j = 1; j < face.VertexIndices.Count; j++)
                {
                    int b = face.VertexIndices[j];
                    if (b < 0 || b >= n) continue;
                    used[b] = true;
                    Union(parent, a, b);
                }
            }

            var map    = new Dictionary<int, List<int>>();
            var result = new List<List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!used[i]) continue;
                int r = Find(parent, i);
                if (!map.TryGetValue(r, out var list))
                {
                    list = new List<int>();
                    map[r] = list;
                    result.Add(list);
                }
                list.Add(i);   // i を昇順に回すので、各リストも昇順になる
            }
            return result;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        // ================================================================
        // 1 本 → 梯子
        // ================================================================

        /// <summary>1 本ぶんを梯子にして dst へ足す。足せないときは理由を返す。</summary>
        private static string AppendBelt(MeshObject dst, MeshObject src, List<int> verts, PipeBeltExtractParams p)
        {
            int k     = p.RingVertexCount;
            int inner = verts.Count - 2;
            if (inner < k)       return $"頂点数が断面 1 輪＋2 に満たない（断面の頂点数 {k}）";
            if (inner % k != 0)  return $"頂点数 − 2 が断面の頂点数 {k} で割り切れない";

            int rungs = inner / k;
            int start = verts[0];
            int end   = verts[verts.Count - 1];

            // 両端が扇の要かを確かめる（並びの前提が崩れているものを黙って通さない）。
            if (!IsFanApex(src, start, k)) return "先頭の頂点が根元の扇の要になっていない";
            if (!IsFanApex(src, end,   k)) return "末尾の頂点が毛先の扇の要になっていない";

            // 取り出す段を決める。
            var pick = PickRungs(rungs, p);
            if (pick.Count == 0) return $"取り出す段が無い（段数 {rungs}）";

            Vector3 sp = src.Vertices[start].Position;
            Vector3 ep = src.Vertices[end].Position;

            var left  = new List<Vector3>(pick.Count);
            var right = new List<Vector3>(pick.Count);
            var leftSrc  = new List<Vertex>(pick.Count);
            var rightSrc = new List<Vertex>(pick.Count);
            foreach (int r in pick)
            {
                var lv = src.Vertices[verts[1 + r * k + p.RailA]];
                var rv = src.Vertices[verts[1 + r * k + p.RailB]];
                left .Add(lv.Position); leftSrc .Add(lv);
                right.Add(rv.Position); rightSrc.Add(rv);
            }

            // ── 頂点 ── 原型と同じ並び。
            // 写した頂点は元のパイプの頂点のウェイトを持つ（スキンド化のあとの作り直しで、出力へ書くとき
            // ウェイトの無い頂点は飛ぶため。ModifierSpace.PrepareForOutput）。開始タグは始点のウェイト。
            int baseIdx = dst.VertexCount;
            Vector3 t0 = sp - (left[0]  - sp);   // 開始タグ（始点に対して点対称）
            Vector3 t1 = sp - (right[0] - sp);
            var sv = src.Vertices[start];
            AddVertex(dst, t0, sv);
            AddVertex(dst, t1, sv);
            AddVertex(dst, sp, sv);
            for (int i = 0; i < left.Count; i++)
            {
                AddVertex(dst, left[i],  leftSrc[i]);
                AddVertex(dst, right[i], rightSrc[i]);
            }
            AddVertex(dst, ep, src.Vertices[end]);

            int iT0 = baseIdx, iT1 = baseIdx + 1, iS = baseIdx + 2;
            int L(int i) => baseIdx + 3 + i * 2;
            int R(int i) => baseIdx + 3 + i * 2 + 1;
            int iE = dst.VertexCount - 1;
            int last = left.Count - 1;

            // 逆方向（段 14）：写した頂点を元のパイプの頂点に結び付ける。開始タグは始点・左右から導くので結び付けない。
            Poly_Ling.Ops.ModifierFrameTrace.RecordSource(dst, new[] { iS }, src, start);
            for (int i = 0; i < pick.Count; i++)
            {
                Poly_Ling.Ops.ModifierFrameTrace.RecordSource(dst, new[] { L(i) }, src, verts[1 + pick[i] * k + p.RailA]);
                Poly_Ling.Ops.ModifierFrameTrace.RecordSource(dst, new[] { R(i) }, src, verts[1 + pick[i] * k + p.RailB]);
            }
            Poly_Ling.Ops.ModifierFrameTrace.RecordSource(dst, new[] { iE }, src, end);

            // ── 面 ── 原型（HairToBelt）の向き。Reverse で全部反転する。
            bool rev = p.Reverse;
            AddTri (dst, iT0, iT1, iS, rev);
            AddTri (dst, iS, L(0), R(0), rev);
            for (int i = 0; i < last; i++)
                AddQuad(dst, L(i), L(i + 1), R(i + 1), R(i), rev);
            AddTri (dst, R(last), L(last), iE, rev);

            return null;
        }

        /// <summary>頂点 v が三角形 k 枚だけの扇の要か。</summary>
        private static bool IsFanApex(MeshObject mo, int v, int k)
        {
            int tri = 0;
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face?.VertexIndices == null || !face.VertexIndices.Contains(v)) continue;
                if (face.VertexIndices.Count != 3) return false;
                tri++;
            }
            return tri == k;
        }

        /// <summary>取り出す段の番号を昇順で返す。</summary>
        private static List<int> PickRungs(int rungs, PipeBeltExtractParams p)
        {
            var result = new List<int>();

            if (p.KeepRungs != null && p.KeepRungs.Length > 0)
            {
                var keep = new HashSet<int>(p.KeepRungs);
                for (int i = 0; i < rungs; i++)
                    if (keep.Contains(i)) result.Add(i);
                return result;
            }

            int step = Mathf.Max(0, p.RungStep) + 1;
            for (int i = Mathf.Max(0, p.StartRung); i < rungs; i += step)
                result.Add(i);
            return result;
        }

        /// <summary>頂点を足す。weightFrom があれば、そのウェイト（ミラー側も）を写す。</summary>
        private static void AddVertex(MeshObject mo, Vector3 pos, Vertex weightFrom)
        {
            var v = new Vertex(pos, Vector2.zero);
            if (weightFrom != null)
            {
                v.BoneWeight       = weightFrom.BoneWeight;
                v.MirrorBoneWeight = weightFrom.MirrorBoneWeight;
            }
            mo.Vertices.Add(v);
        }

        private static void AddTri(MeshObject mo, int a, int b, int c, bool reverse)
        {
            var f = new Face();
            f.VertexIndices.Add(a);
            if (reverse) { f.VertexIndices.Add(c); f.VertexIndices.Add(b); }
            else         { f.VertexIndices.Add(b); f.VertexIndices.Add(c); }
            for (int i = 0; i < 3; i++) { f.UVIndices.Add(0); f.NormalIndices.Add(0); }
            mo.AddFace(f);
        }

        private static void AddQuad(MeshObject mo, int a, int b, int c, int d, bool reverse)
        {
            if (reverse) mo.AddQuad(a, d, c, b);
            else         mo.AddQuad(a, b, c, d);
        }
    }
}
