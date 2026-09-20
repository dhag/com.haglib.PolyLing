// LineGroupOps.cs
// 線分群（MeshObject.LineGroups）と 2 頂点の面（線分）を連携させる操作の置き場。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【連携の規則】
//   線分群の隣り合う 2 点の間には、2 頂点の面が 1 枚ある（閉じていれば終点→始点も）。
//   線分群は順序の正典、2 頂点の面はその表示と選択の担い手。
//   面や頂点が消えたら、線分群はその区間で切る（前後をつながない）。
//
// 【書き始めの規則（AddSegment）】
//   線分の始点が既存の線分群の端点で、extendAtEndpoint が true なら、その群を伸ばす（A）。
//   それ以外は新しい群を作り、始点が既存の群の頂点なら ParentVertex に入れる（B）。
//   呼び出し側に B を選ぶ手段が無い場合は A（extendAtEndpoint = true）で呼ぶ。
//   閉じた群は伸ばさない。

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class LineGroupOps
    {
        // ================================================================
        // 複製・復元（Undo 用）
        // ================================================================

        /// <summary>線分群の一覧をディープコピーする。null は空の一覧として扱う。</summary>
        public static List<LineGroup> CloneList(MeshObject mo)
        {
            var src = mo?.LineGroups;
            var list = new List<LineGroup>(src?.Count ?? 0);
            if (src != null)
                foreach (var g in src)
                    if (g != null) list.Add(g.Clone());
            return list;
        }

        /// <summary>控えた一覧を書き戻す（控えは複製してから入れる）。</summary>
        public static void RestoreList(MeshObject mo, List<LineGroup> saved)
        {
            if (mo == null || saved == null) return;
            var list = new List<LineGroup>(saved.Count);
            foreach (var g in saved)
                if (g != null) list.Add(g.Clone());
            mo.LineGroups = list;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 頂点索引の並びから線分群を作って末尾に足す。頂点 ID の控えも埋める。
        /// 面は作らない（呼び出し側が同じ並びで 2 頂点の面を張ること）。
        /// </summary>
        /// <returns>足した群の索引。2 点未満なら -1。</returns>
        public static int AddGroup(MeshObject mo, IReadOnlyList<int> order, bool closed, string name = null)
        {
            if (mo == null || order == null || order.Count < 2) return -1;
            mo.LineGroups ??= new List<LineGroup>();

            var g = new LineGroup(string.IsNullOrEmpty(name) ? NextName(mo) : name)
            {
                Closed = closed && order.Count >= 3,
            };
            foreach (int vi in order)
            {
                g.Order.Add(vi);
                g.OrderVertexIds.Add(VertexIdOf(mo, vi));
            }
            mo.LineGroups.Add(g);
            return mo.LineGroups.Count - 1;
        }

        /// <summary>
        /// 線分 a→b を 1 本足したことを線分群へ反映する（面は呼び出し側が足してある前提）。
        /// </summary>
        /// <param name="chainGroupIndex">
        /// 続けて描いている折れ線の群の索引（無ければ -1）。その群の端点が a なら、
        /// extendAtEndpoint によらず伸ばす（同じ折れ線の続きのため）。
        /// </param>
        /// <param name="extendAtEndpoint">a が既存の群の端点ならその群を伸ばすか。</param>
        /// <returns>線分を入れた群の索引。入れなかったら -1。</returns>
        public static int AddSegment(
            MeshObject mo, int a, int b, int chainGroupIndex, bool extendAtEndpoint)
        {
            if (mo == null || a < 0 || b < 0 || a == b) return -1;
            mo.LineGroups ??= new List<LineGroup>();
            var list = mo.LineGroups;

            if (chainGroupIndex >= 0 && chainGroupIndex < list.Count
                && TryExtend(mo, list[chainGroupIndex], a, b))
                return chainGroupIndex;

            if (extendAtEndpoint)
            {
                for (int gi = 0; gi < list.Count; gi++)
                    if (TryExtend(mo, list[gi], a, b)) return gi;
            }

            int created = AddGroup(mo, new[] { a, b }, false);
            if (created >= 0)
            {
                for (int gi = 0; gi < list.Count; gi++)
                {
                    if (gi == created) continue;
                    var og = list[gi];
                    if (og?.Order == null || !og.Order.Contains(a)) continue;
                    list[created].ParentVertex   = a;
                    list[created].ParentVertexId = VertexIdOf(mo, a);
                    break;
                }
            }
            return created;
        }

        /// <summary>
        /// 開いた群の端点が a なら b を足す。b がもう一方の端点で 3 点以上なら閉じる。
        /// </summary>
        private static bool TryExtend(MeshObject mo, LineGroup g, int a, int b)
        {
            if (g?.Order == null || g.Closed || g.Order.Count < 2) return false;
            EnsureIds(g);

            if (g.EndVertex == a)
            {
                if (b == g.StartVertex)
                {
                    if (g.Order.Count < 3) return false;
                    g.Closed = true;
                    return true;
                }
                g.Order.Add(b);
                g.OrderVertexIds.Add(VertexIdOf(mo, b));
                InsertDefaultHandle(g, g.Order.Count - 1);
                return true;
            }
            if (g.StartVertex == a)
            {
                if (b == g.EndVertex)
                {
                    if (g.Order.Count < 3) return false;
                    g.Closed = true;
                    return true;
                }
                g.Order.Insert(0, b);
                g.OrderVertexIds.Insert(0, VertexIdOf(mo, b));
                InsertDefaultHandle(g, 0);
                return true;
            }
            return false;
        }

        // ================================================================
        // 削除への追随
        // ================================================================

        /// <summary>
        /// 頂点の旧索引→新索引の表で線分群を付け替える。
        /// 消えた点（-1）のところで群を切る（前後の点をつながない。そこに面は無いため）。
        /// 複数の旧索引が 1 つへ落ちた（マージ）ときは、続けて同じ点になったものを 1 つに畳む。
        /// 2 点未満になった断片は捨てる。親頂点が消えた群は親なしに戻す。
        /// </summary>
        public static void ApplyVertexMap(MeshObject mo, int[] map)
        {
            var list = mo?.LineGroups;
            if (list == null || list.Count == 0 || map == null) return;

            var result = new List<LineGroup>(list.Count);
            foreach (var g in list)
            {
                if (g?.Order == null) continue;
                EnsureIds(g);

                int n = g.Order.Count;
                var mapped = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int ov = g.Order[k];
                    mapped[k] = (ov >= 0 && ov < map.Length) ? map[ov] : -1;
                }

                int pv = g.ParentVertex;
                int np = (pv >= 0 && pv < map.Length) ? map[pv] : -1;

                var pieces = Split(g, mapped, null);
                foreach (var p in pieces)
                {
                    if (p.ParentVertex >= 0) p.ParentVertex = np;
                    if (p.ParentVertex < 0) p.ParentVertexId = 0;
                    result.Add(p);
                }
            }
            mo.LineGroups = result;
        }

        /// <summary>
        /// 面を消す直前に呼ぶ。消える 2 頂点の面のうち、残る面に同じ組が無いものの区間で
        /// 線分群を切る。頂点索引はまだ変わっていない前提。
        /// </summary>
        public static void SplitAtRemovedFaces(MeshObject mo, ICollection<int> killFaces)
        {
            var list = mo?.LineGroups;
            if (list == null || list.Count == 0 || killFaces == null || killFaces.Count == 0) return;

            var kill = new HashSet<int>(killFaces);
            var removed = new HashSet<long>();
            foreach (int fi in kill)
            {
                if (fi < 0 || fi >= mo.Faces.Count) continue;
                var f = mo.Faces[fi];
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                removed.Add(PairKey(f.VertexIndices[0], f.VertexIndices[1]));
            }
            if (removed.Count == 0) return;

            // 同じ組の 2 頂点の面が残るなら、その区間は切らない。
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                if (kill.Contains(fi)) continue;
                var f = mo.Faces[fi];
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                removed.Remove(PairKey(f.VertexIndices[0], f.VertexIndices[1]));
            }
            if (removed.Count == 0) return;

            var result = new List<LineGroup>(list.Count);
            foreach (var g in list)
            {
                if (g?.Order == null) continue;
                EnsureIds(g);
                var pieces = Split(g, g.Order.ToArray(), removed);
                result.AddRange(pieces);
            }
            mo.LineGroups = result;
        }

        /// <summary>
        /// ミラーのベイク（MirrorBaker.BakeMirrorInPlace）用。元の線分群から、ベイク後の
        /// 元側と鏡像側の線分群を作る。oldToNew は 2N 長（[0,N) 元側、[N,2N) 鏡像側）の表。
        /// 鏡像側が元側と同じ頂点列（向きを問わない）になったもの（対称面上の群）は足さない。
        /// </summary>
        public static List<LineGroup> BuildMirrorBaked(List<LineGroup> src, int[] oldToNew, int n,
            UnityEngine.Vector3 planeNormal)
        {
            var result = new List<LineGroup>();
            if (src == null || src.Count == 0 || oldToNew == null || oldToNew.Length < 2 * n) return result;

            var mapA = new int[n];
            var mapB = new int[n];
            for (int i = 0; i < n; i++) { mapA[i] = oldToNew[i]; mapB[i] = oldToNew[i + n]; }

            var tmp = new MeshObject();
            tmp.LineGroups = CloneListOf(src);
            ApplyVertexMap(tmp, mapA);
            var originals = tmp.LineGroups;

            tmp.LineGroups = CloneListOf(src);
            var pn = planeNormal.normalized;
            MirrorHandles(tmp.LineGroups, v => v - 2f * UnityEngine.Vector3.Dot(v, pn) * pn);
            ApplyVertexMap(tmp, mapB);
            var mirrored = tmp.LineGroups;

            result.AddRange(originals);
            foreach (var m in mirrored)
            {
                bool dup = false;
                foreach (var o in originals)
                    if (SameOrder(o, m)) { dup = true; break; }
                if (dup) continue;
                m.Name = m.Name + "_M";
                result.Add(m);
            }
            return result;
        }

        private static List<LineGroup> CloneListOf(List<LineGroup> src)
        {
            var list = new List<LineGroup>(src.Count);
            foreach (var g in src) if (g != null) list.Add(g.Clone());
            return list;
        }

        /// <summary>
        /// ハンドルのずれを鏡映する（mirrorVector は向きのベクトルを鏡映する関数）。
        /// 頂点を鏡映した群に使う。点の並びは変えないので入り・出はそのまま。
        /// </summary>
        public static void MirrorHandles(List<LineGroup> groups, System.Func<UnityEngine.Vector3, UnityEngine.Vector3> mirrorVector)
        {
            if (groups == null || mirrorVector == null) return;
            foreach (var g in groups)
            {
                if (g?.PointHandles == null) continue;
                foreach (var h in g.PointHandles)
                {
                    if (h == null) continue;
                    h.InOffset  = mirrorVector(h.InOffset);
                    h.OutOffset = mirrorVector(h.OutOffset);
                }
            }
        }

        /// <summary>同じ頂点列か（向き違いも同じとみなす）。</summary>
        private static bool SameOrder(LineGroup a, LineGroup b)
        {
            if (a?.Order == null || b?.Order == null || a.Order.Count != b.Order.Count) return false;
            int n = a.Order.Count;
            bool fwd = true, rev = true;
            for (int i = 0; i < n; i++)
            {
                if (a.Order[i] != b.Order[i]) fwd = false;
                if (a.Order[i] != b.Order[n - 1 - i]) rev = false;
            }
            return fwd || rev;
        }

        /// <summary>
        /// 面を丸ごと入れ替えた後に呼ぶ。対応する 2 頂点の面が無い区間で線分群を切る。
        /// 頂点索引は変わっていない前提（面だけを入れ替えた経路用）。
        /// </summary>
        public static void ReconcileWithFaces(MeshObject mo)
        {
            var list = mo?.LineGroups;
            if (list == null || list.Count == 0) return;

            var present = new HashSet<long>();
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                present.Add(PairKey(f.VertexIndices[0], f.VertexIndices[1]));
            }

            var missing = new HashSet<long>();
            foreach (var g in list)
            {
                if (g?.Order == null) continue;
                int n = g.Order.Count;
                int segs = g.Closed ? n : n - 1;
                for (int k = 0; k < segs; k++)
                {
                    long key = PairKey(g.Order[k], g.Order[(k + 1) % n]);
                    if (!present.Contains(key)) missing.Add(key);
                }
            }
            if (missing.Count == 0) return;

            var result = new List<LineGroup>(list.Count);
            foreach (var g in list)
            {
                if (g?.Order == null) continue;
                EnsureIds(g);
                result.AddRange(Split(g, g.Order.ToArray(), missing));
            }
            mo.LineGroups = result;
        }

        /// <summary>
        /// 群を切り分ける。mapped[k] は k 番目の点の新しい頂点索引（-1 = 消えた）。
        /// cutPairs に含まれる組（旧索引）の区間でも切る。
        /// 先頭の断片は元の群の名前と親を引き継ぎ、残りは名前に番号を付け親なしにする。
        /// </summary>
        private static List<LineGroup> Split(LineGroup g, int[] mapped, HashSet<long> cutPairs)
        {
            int n = mapped.Length;
            var pieces = new List<LineGroup>();
            if (n == 0) return pieces;

            bool Cut(int i, int j)
                => cutPairs != null && cutPairs.Contains(PairKey(g.Order[i], g.Order[j]));

            // 閉じた群の切れ目を探す（点の消滅か区間の切断）。
            int start = 0;
            bool closedIntact = false;
            if (g.Closed)
            {
                int brk = -1;
                for (int i = 0; i < n; i++)
                {
                    if (mapped[i] < 0 || Cut(i, (i + 1) % n)) { brk = i; break; }
                }
                if (brk < 0) closedIntact = true;
                else start = (brk + 1) % n;
            }

            var cur = new List<int>();   // 元の位置
            void Flush()
            {
                // 畳み込み（同じ新索引が続くものを 1 つに）
                var order = new List<int>();
                var ids   = new List<int>();
                bool withHandles = g.HasHandles;
                var handles = new List<LinePointHandle>();
                var usedGroups = new HashSet<int>();
                foreach (int k in cur)
                {
                    int v = mapped[k];
                    if (order.Count > 0 && order[order.Count - 1] == v) continue;
                    order.Add(v);
                    ids.Add(k < g.OrderVertexIds.Count ? g.OrderVertexIds[k] : 0);
                    if (withHandles)
                    {
                        var h = g.PointHandles[k]?.Clone() ?? LinePointHandle.CreateDefault();
                        handles.Add(h);
                        if (h.InConstraint.Length  == HandleLength.Group) usedGroups.Add(h.InConstraint.LengthGroupId);
                        if (h.OutConstraint.Length == HandleLength.Group) usedGroups.Add(h.OutConstraint.LengthGroupId);
                    }
                }
                bool whole = cur.Count == n;   // 切れ目の無い断片（群そのもの）
                cur.Clear();
                if (order.Count < 2) return;

                var p = new LineGroup(pieces.Count == 0 ? g.Name : $"{g.Name}_{pieces.Count}")
                {
                    Closed         = false,
                    ParentVertex   = pieces.Count == 0 ? g.ParentVertex   : -1,
                    ParentVertexId = pieces.Count == 0 ? g.ParentVertexId : 0,
                };
                p.Order          = order;
                p.OrderVertexIds = ids;
                p.PointHandles   = handles;
                // 長さの組は、この断片で使われているものだけを持たせる（群そのものなら全部）。
                p.LengthGroups   = new List<LineLengthGroup>();
                if (g.LengthGroups != null)
                    foreach (var lg in g.LengthGroups)
                        if (lg != null && (whole || usedGroups.Contains(lg.Id))) p.LengthGroups.Add(lg.Clone());
                pieces.Add(p);
            }

            int prev = -1;
            for (int s = 0; s < n; s++)
            {
                int k = (start + s) % n;
                if (mapped[k] < 0) { Flush(); prev = -1; continue; }
                if (prev >= 0 && Cut(prev, k)) Flush();
                cur.Add(k);
                prev = k;
            }
            Flush();

            if (closedIntact && pieces.Count == 1)
            {
                var p = pieces[0];
                // 畳み込みで始点と終点が同じになったら末尾を落とす。
                if (p.Order.Count >= 2 && p.Order[0] == p.Order[p.Order.Count - 1])
                {
                    p.Order.RemoveAt(p.Order.Count - 1);
                    p.OrderVertexIds.RemoveAt(p.OrderVertexIds.Count - 1);
                    if (p.PointHandles.Count > 0) p.PointHandles.RemoveAt(p.PointHandles.Count - 1);
                }
                p.Closed = p.Order.Count >= 3;
            }
            return pieces;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>向きを問わない頂点の組のキー。</summary>
        public static long PairKey(int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static int VertexIdOf(MeshObject mo, int vi)
            => (mo?.Vertices != null && vi >= 0 && vi < mo.Vertices.Count) ? mo.Vertices[vi].Id : 0;

        /// <summary>控えの数を Order に揃える（足りない分は 0）。ハンドルを持つ群はハンドルも揃える。</summary>
        private static void EnsureIds(LineGroup g)
        {
            g.OrderVertexIds ??= new List<int>();
            while (g.OrderVertexIds.Count < g.Order.Count) g.OrderVertexIds.Add(0);
            if (g.OrderVertexIds.Count > g.Order.Count)
                g.OrderVertexIds.RemoveRange(g.Order.Count, g.OrderVertexIds.Count - g.Order.Count);

            g.PointHandles ??= new List<LinePointHandle>();
            g.LengthGroups ??= new List<LineLengthGroup>();
            if (g.PointHandles.Count > 0)
            {
                while (g.PointHandles.Count < g.Order.Count) g.PointHandles.Add(LinePointHandle.CreateDefault());
                if (g.PointHandles.Count > g.Order.Count)
                    g.PointHandles.RemoveRange(g.Order.Count, g.PointHandles.Count - g.Order.Count);
            }
        }

        /// <summary>ハンドルを持つ群なら、位置 index に既定のハンドルを入れる（Order へ点を足すのと同時に呼ぶ）。</summary>
        private static void InsertDefaultHandle(LineGroup g, int index)
        {
            if (g.PointHandles == null || g.PointHandles.Count == 0) return;
            g.PointHandles.Insert(index, LinePointHandle.CreateDefault());
        }

        /// <summary>既存の名前と重ならない "Line{番号}"。</summary>
        private static string NextName(MeshObject mo)
        {
            var used = new HashSet<string>();
            if (mo?.LineGroups != null)
                foreach (var g in mo.LineGroups) if (g != null) used.Add(g.Name);
            for (int i = 0; ; i++)
            {
                string s = $"Line{i}";
                if (!used.Contains(s)) return s;
            }
        }
    }
}
