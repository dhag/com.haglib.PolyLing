// TriangleRelocateOps.cs
// 三角形の移し替え。三角形 1 枚と四角形 3 枚に囲まれた頂点 V を消し、
// V の周りの三角形を、向かい側の四角形の対角の隅 c の周りの三角形 3 枚へ移す。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【記号】V の周りを巻き方向どおりに並べて T, F1, F2, F3 とする。
//   T  … 三角形。F1 と F3 は T と辺を共有する四角形、F2 は T の向かいの四角形。
//   面 F の V の位置を p とすると「始端 = F[p+1]」「終端 = F[p-1]」で、
//   前の面の終端 = 次の面の始端（Quad4To1Ops と同じ辿り方）。
//   a = F2 の始端（F1 と共有）、b = F2 の終端（F3 と共有）、c = F2 の対角 F2[p+2]。
//   sT = T の始端（F3 と共有）、eT = T の終端（F1 と共有）。
//
// 【手順】（作例 TMP.mqo の 1〜5 番目のオブジェクトと同じ結果になる）
//   1. F2 を対角線 b–a で 2 つに割り、V を含む側を捨てる。
//   2. 残る三角形 (a c b) を、b–a 上の新しい頂点 p（b 寄り）・q（a 寄り）で
//      c から扇状に 3 つに割る：(c b p) (c p q) (c q a)。F2 の面は (c b p) に上書きする。
//   3. F3 の V を p に、F1 の V を q に差し替える。
//   4. T の V を q, p の順に差し替えて四角形 (… eT, q, p, sT …) にする。
//   5. どの面からも使われなくなった V を消す。
//
// 【新しい頂点】位置は b→a の比率 t1（p）・t2（q）。0 < t1 < t2 < 1。
//   UV と法線は F2 が b・a で使っているスロットの値を同じ比率で補間する（法線は正規化）。
//   それ以外（部品ID・ウェイトなど）は近い側の端点（t < 0.5 なら b、そうでなければ a）を写す。
//
// 【不変条件】Face.UVIndices[j] == Face.NormalIndices[j]。
//   既存頂点は元の面で使っていたスロットを引き継ぎ、新しい頂点はスロット 0 だけを持つ。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class TriangleRelocateOps
    {
        /// <summary>既定の比率（b から a へ）。</summary>
        public const float DefaultT1 = 1f / 3f;
        public const float DefaultT2 = 2f / 3f;

        /// <summary>1 頂点ぶんの周りの構造。</summary>
        private struct Fan
        {
            public int T, F1, F2, F3;   // 面索引
            public int A, B, C;         // 頂点索引
        }

        // ================================================================
        // 周りの構造
        // ================================================================

        private static bool BuildFan(MeshObject mo, int v, out Fan fan, out string reason)
        {
            fan = default;

            if (mo == null || v < 0 || v >= mo.Vertices.Count)
            { reason = "頂点が指定されていません"; return false; }

            var faces = new List<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var vi = mo.Faces[fi].VertexIndices;
                int occur = 0;
                for (int j = 0; j < vi.Count; j++) if (vi[j] == v) occur++;
                if (occur == 0) continue;
                if (occur > 1) { reason = "同じ面が指定頂点を複数回参照しています"; return false; }
                faces.Add(fi);
            }

            if (faces.Count == 0) { reason = "指定頂点はどの面にも使われていません"; return false; }
            if (faces.Count != 4) { reason = $"指定頂点を囲む面が 4 枚ではありません（{faces.Count} 枚）"; return false; }

            int tri = -1, quads = 0;
            foreach (int fi in faces)
            {
                int n = mo.Faces[fi].VertexIndices.Count;
                if (n == 3) { if (tri >= 0) { reason = "指定頂点を囲む三角形が 2 枚以上あります"; return false; } tri = fi; }
                else if (n == 4) quads++;
                else { reason = "指定頂点を囲む面に三角形・四角形以外があります"; return false; }
            }
            if (tri < 0 || quads != 3) { reason = "指定頂点を囲む面が三角形 1 枚と四角形 3 枚ではありません"; return false; }

            // 始端 → 面
            var startToFace = new Dictionary<int, int>();
            foreach (int fi in faces)
            {
                int s = Start(mo, fi, v);
                if (startToFace.ContainsKey(s)) { reason = "指定頂点の周りが多様体になっていません"; return false; }
                startToFace[s] = fi;
            }

            // T から巻き方向に辿る
            var order = new List<int>();
            int cur = tri;
            while (true)
            {
                if (order.Contains(cur)) { reason = "指定頂点の周りが多様体になっていません"; return false; }
                order.Add(cur);
                if (!startToFace.TryGetValue(End(mo, cur, v), out int next))
                { reason = "指定頂点の周りが閉じていません（境界の頂点です）"; return false; }
                if (next == tri) break;
                cur = next;
            }
            if (order.Count != 4) { reason = "指定頂点の周りに独立した面のかたまりがあります"; return false; }

            fan.T = order[0]; fan.F1 = order[1]; fan.F2 = order[2]; fan.F3 = order[3];

            var f2 = mo.Faces[fan.F2].VertexIndices;
            int p2 = f2.IndexOf(v);
            fan.A = f2[(p2 + 1) % 4];
            fan.C = f2[(p2 + 2) % 4];
            fan.B = f2[(p2 + 3) % 4];

            var set = new HashSet<int> { v, fan.A, fan.B, fan.C };
            if (set.Count != 4) { reason = "向かいの四角形の頂点が重なっています"; return false; }

            reason = null;
            return true;
        }

        private static int Start(MeshObject mo, int fi, int v)
        {
            var vi = mo.Faces[fi].VertexIndices;
            int p = vi.IndexOf(v);
            return vi[(p + 1) % vi.Count];
        }

        private static int End(MeshObject mo, int fi, int v)
        {
            var vi = mo.Faces[fi].VertexIndices;
            int p = vi.IndexOf(v);
            return vi[(p - 1 + vi.Count) % vi.Count];
        }

        /// <summary>面 fi が頂点 v で使っているスロット。</summary>
        private static int SlotOf(Face f, int v)
        {
            int k = f.VertexIndices.IndexOf(v);
            return (k >= 0 && k < f.UVIndices.Count) ? f.UVIndices[k] : 0;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>1 頂点について実行できるかを返す。メッシュは変更しない。</summary>
        public static bool CanExecute(MeshObject mo, int v, out string reason)
            => BuildFan(mo, v, out _, out reason);

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>1 頂点ぶんを実行する。成功したら true。消えた頂点は常に V 1 つ。</summary>
        public static bool Execute(MeshObject mo, int v, float t1, float t2, out string reason)
        {
            if (!(t1 > 0f && t1 < t2 && t2 < 1f))
            { reason = $"比率は 0 < t1 < t2 < 1 にしてください（t1={t1}, t2={t2}）"; return false; }

            if (!BuildFan(mo, v, out var fan, out reason)) return false;

            var fT  = mo.Faces[fan.T];
            var fF1 = mo.Faces[fan.F1];
            var fF2 = mo.Faces[fan.F2];
            var fF3 = mo.Faces[fan.F3];

            int a = fan.A, b = fan.B, c = fan.C;
            int slotA = SlotOf(fF2, a), slotB = SlotOf(fF2, b), slotC = SlotOf(fF2, c);

            // ── 新しい頂点 p（b 寄り）と q（a 寄り）
            int p = mo.AddVertex(MakeVertex(mo.Vertices[b], slotB, mo.Vertices[a], slotA, t1));
            int q = mo.AddVertex(MakeVertex(mo.Vertices[b], slotB, mo.Vertices[a], slotA, t2));

            // ── 2. F2 を 3 つの三角形に。F2 自身は (c b p) で上書きする。
            int mat = fF2.MaterialIndex;
            var flags = fF2.Flags;
            SetFace(fF2, new[] { c, b, p }, new[] { slotC, slotB, 0 });
            mo.AddFace(NewFace(new[] { c, p, q }, new[] { slotC, 0, 0 }, mat, flags));
            mo.AddFace(NewFace(new[] { c, q, a }, new[] { slotC, 0, slotA }, mat, flags));

            // ── 3. F3 の V → p、F1 の V → q
            Replace(fF3, v, p);
            Replace(fF1, v, q);

            // ── 4. T の V → q, p（eT → q → p → sT の順）
            {
                int k = fT.VertexIndices.IndexOf(v);
                fT.VertexIndices[k] = q;
                fT.UVIndices[k]     = 0;
                fT.NormalIndices[k] = 0;
                fT.VertexIndices.Insert(k + 1, p);
                fT.UVIndices.Insert(k + 1, 0);
                fT.NormalIndices.Insert(k + 1, 0);
            }

            // ── 5. V を消す（どの面からも使われていない）
            mo.RemoveVertices(new[] { v });

            reason = null;
            return true;
        }

        private static Vertex MakeVertex(Vertex vb, int slotB, Vertex va, int slotA, float t)
        {
            var nv = (t < 0.5f ? vb : va).Clone();
            nv.Id = 0;
            nv.Position = Vector3.Lerp(vb.Position, va.Position, t);

            Vector2 uvB = slotB < vb.UVs.Count ? vb.UVs[slotB] : Vector2.zero;
            Vector2 uvA = slotA < va.UVs.Count ? va.UVs[slotA] : Vector2.zero;
            Vector3 nB  = slotB < vb.Normals.Count ? vb.Normals[slotB] : Vector3.up;
            Vector3 nA  = slotA < va.Normals.Count ? va.Normals[slotA] : Vector3.up;
            Vector3 n   = Vector3.Lerp(nB, nA, t);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : nB;

            nv.UVs     = new List<Vector2> { Vector2.Lerp(uvB, uvA, t) };
            nv.Normals = new List<Vector3> { n };
            return nv;
        }

        private static void SetFace(Face f, int[] verts, int[] slots)
        {
            f.VertexIndices = new List<int>(verts);
            f.UVIndices     = new List<int>(slots);
            f.NormalIndices = new List<int>(slots);
        }

        private static Face NewFace(int[] verts, int[] slots, int mat, FaceFlags flags)
        {
            var f = new Face { MaterialIndex = mat, Flags = flags };
            SetFace(f, verts, slots);
            return f;
        }

        private static void Replace(Face f, int from, int to)
        {
            int k = f.VertexIndices.IndexOf(from);
            f.VertexIndices[k] = to;
            f.UVIndices[k]     = 0;
            f.NormalIndices[k] = 0;
        }

        // ================================================================
        // 一括処理（複数頂点）
        // ================================================================

        /// <summary>一括処理の下調べ結果。</summary>
        public struct BatchInfo
        {
            public bool   CanExecute;
            public int    TargetCount;
            public int    SkippedCount;
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない頂点だけを選ぶ。単独で実行できない頂点と、
        /// 同じ面を共有する頂点どうしを落とす（Quad4To1Ops.SelectIndependent と同じ規則）。
        /// </summary>
        public static List<int> SelectIndependent(MeshObject mo, IEnumerable<int> vs, out List<int> skipped)
        {
            var accepted = new List<int>();
            skipped = new List<int>();
            if (mo == null || vs == null) return accepted;

            var candidates = new HashSet<int>();
            foreach (int v in vs)
            {
                if (v < 0 || v >= mo.Vertices.Count || !candidates.Add(v)) continue;
                if (!CanExecute(mo, v, out _)) { candidates.Remove(v); skipped.Add(v); }
            }

            var conflicted = new HashSet<int>();
            foreach (var f in mo.Faces)
            {
                var onFace = new List<int>();
                foreach (int v in f.VertexIndices)
                    if (candidates.Contains(v) && !onFace.Contains(v)) onFace.Add(v);
                if (onFace.Count >= 2) foreach (int v in onFace) conflicted.Add(v);
            }

            foreach (int v in candidates)
            {
                if (conflicted.Contains(v)) skipped.Add(v);
                else                        accepted.Add(v);
            }
            accepted.Sort();
            skipped.Sort();
            return accepted;
        }

        public static BatchInfo InspectMany(MeshObject mo, IEnumerable<int> vs)
        {
            var info = new BatchInfo();
            if (mo == null) { info.Reason = "メッシュがありません"; return info; }

            var targets = SelectIndependent(mo, vs, out var skipped);
            info.TargetCount  = targets.Count;
            info.SkippedCount = skipped.Count;

            if (targets.Count == 0)
            {
                info.Reason = skipped.Count > 0
                    ? "選択頂点が条件（三角形 1 枚と四角形 3 枚に囲まれた頂点）を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return info;
            }
            info.CanExecute = true;
            return info;
        }

        /// <summary>
        /// 複数頂点を実行する。1 つでも処理できたら true。
        /// 各実行で V が 1 つ消えるので、残りの対象の索引を詰め直しながら進める。
        /// 足した頂点は末尾に付くので、残りの対象の索引には影響しない。
        /// </summary>
        public static bool ExecuteMany(MeshObject mo, IEnumerable<int> vs, float t1, float t2,
                                       out int doneCount, out int skippedCount, out string reason)
        {
            doneCount = 0;
            skippedCount = 0;
            reason = null;
            if (mo == null) { reason = "メッシュがありません"; return false; }

            var targets = SelectIndependent(mo, vs, out var skipped);
            skippedCount = skipped.Count;
            if (targets.Count == 0)
            {
                reason = skipped.Count > 0
                    ? "選択頂点が条件（三角形 1 枚と四角形 3 枚に囲まれた頂点）を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return false;
            }

            var pending = new List<int>(targets);
            for (int k = 0; k < pending.Count; k++)
            {
                int v = pending[k];
                if (!Execute(mo, v, t1, t2, out string why)) { skippedCount++; reason = why; continue; }
                doneCount++;
                for (int j = k + 1; j < pending.Count; j++)
                    if (pending[j] > v) pending[j]--;
            }

            if (doneCount == 0) return false;
            reason = null;
            return true;
        }
    }
}
