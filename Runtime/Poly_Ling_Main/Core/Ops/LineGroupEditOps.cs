// LineGroupEditOps.cs
// 線分群を「頂点・2 頂点の面・線分群」ごと作る／差し替える／消す操作。
// 線分群コマンド（PanelCommand.LineGroup.cs）の実処理。Undo は呼び出し側（受け口）が取る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【連携】線分群の隣り合う 2 点には 2 頂点の面が 1 枚ある（LineGroupOps の規則）。
//   ここでは面と線分群を必ず一緒に作り直す。
//   面を消すときは群を一覧から外してから消す（外さないと RemoveFaces が群を切ってしまう）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class LineGroupEditOps
    {
        /// <summary>点列（ローカル座標）から頂点・線分・線分群を作る。</summary>
        /// <returns>作った群の索引。作れなければ -1。</returns>
        public static int Create(
            MeshObject mo, IReadOnlyList<Vector3> points, bool closed, string name,
            IReadOnlyList<LinePointHandle> handles, out string reason, int startVertex = -1)
        {
            reason = null;
            if (mo == null) { reason = "メッシュがありません"; return -1; }
            if (points == null || points.Count < 2) { reason = "点は 2 個以上要ります"; return -1; }
            if (handles != null && handles.Count > 0 && handles.Count != points.Count)
            { reason = "ハンドルの数が点の数と合いません"; return -1; }
            if (startVertex >= mo.VertexCount) { reason = $"始点の頂点番号が範囲外です: {startVertex}"; return -1; }

            // 始点を既存の頂点にする場合、その頂点を使う（位置は動かさない）。
            // 既存の群の頂点なら、その頂点を親にする（LineGroupOps.AddSegment の B と同じ）。
            var order = new List<int>(points.Count);
            for (int k = 0; k < points.Count; k++)
            {
                if (k == 0 && startVertex >= 0) { order.Add(startVertex); continue; }
                order.Add(mo.Vertices.Count);
                mo.Vertices.Add(NewVertex(points[k]));
            }
            AddSegmentFaces(mo, order, closed && order.Count >= 3);

            bool startInOtherGroup = false;
            if (startVertex >= 0 && mo.LineGroups != null)
                foreach (var og in mo.LineGroups)
                    if (og?.Order != null && og.Order.Contains(startVertex)) { startInOtherGroup = true; break; }

            int gi = LineGroupOps.AddGroup(mo, order, closed, name);
            if (gi < 0) { reason = "線分群を作れませんでした"; return -1; }
            if (startInOtherGroup)
            {
                mo.LineGroups[gi].ParentVertex   = startVertex;
                mo.LineGroups[gi].ParentVertexId = mo.Vertices[startVertex].Id;
            }
            SetHandles(mo.LineGroups[gi], handles);
            LineHandleSolver.Solve(mo, mo.LineGroups[gi]);
            mo.RebuildIdSets();
            return gi;
        }

        /// <summary>
        /// 辺（頂点番号の組）から線分と線分群を作る。頂点は既存のものを使い、増やさない。
        /// 重複した辺・すでに 2 頂点の面がある辺は飛ばす。
        /// つながりは分岐点（次数 3 以上）と端点（次数 1）で区切って 1 群ずつにし、
        /// 全部が次数 2 の輪は閉じた群にする。始点は番号の小さい頂点から取る（同じ入力で同じ結果）。
        /// </summary>
        /// <param name="edgeVertexPairs">v1,v2,v1,v2,... の並び</param>
        /// <param name="groupIndices">作った群の番号を足す先</param>
        /// <param name="lineFaceIndices">足した線分（面）の番号を足す先</param>
        public static bool CreateFromEdges(
            MeshObject mo, IReadOnlyList<int> edgeVertexPairs,
            List<int> groupIndices, List<int> lineFaceIndices, out string reason)
        {
            reason = null;
            if (mo == null) { reason = "メッシュがありません"; return false; }
            if (edgeVertexPairs == null || edgeVertexPairs.Count == 0) { reason = "辺がありません"; return false; }
            if (edgeVertexPairs.Count % 2 != 0) { reason = "辺の頂点番号は 2 個ずつ並べてください"; return false; }

            // すでにある線分
            var existing = new HashSet<long>();
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                existing.Add(LineGroupOps.PairKey(f.VertexIndices[0], f.VertexIndices[1]));
            }

            // 新しく線分にする辺と、その辺だけでの隣接
            var todo = new HashSet<long>();
            var adj = new SortedDictionary<int, List<int>>();
            for (int i = 0; i + 1 < edgeVertexPairs.Count; i += 2)
            {
                int a = edgeVertexPairs[i], b = edgeVertexPairs[i + 1];
                if (a < 0 || b < 0 || a >= mo.VertexCount || b >= mo.VertexCount)
                { reason = $"頂点番号が範囲外です: {a},{b}"; return false; }
                if (a == b) { reason = $"同じ頂点どうしの辺です: {a}"; return false; }
                long key = LineGroupOps.PairKey(a, b);
                if (existing.Contains(key) || !todo.Add(key)) continue;
                AddNeighbor(adj, a, b);
                AddNeighbor(adj, b, a);
            }
            if (todo.Count == 0) { reason = "線分にする辺がありません（すべて線分になっています）"; return false; }
            foreach (var list in adj.Values) list.Sort();

            var used = new HashSet<long>();
            int Degree(int v) => adj[v].Count;
            int NextUnused(int v)
            {
                foreach (int n in adj[v])
                    if (!used.Contains(LineGroupOps.PairKey(v, n))) return n;
                return -1;
            }

            var chains = new List<(List<int> Order, bool Closed)>();

            // 1. 端点・分岐点から歩く（開いた連なり）
            foreach (var kv in adj)
            {
                int s = kv.Key;
                if (Degree(s) == 2) continue;
                foreach (int first in kv.Value)
                {
                    if (used.Contains(LineGroupOps.PairKey(s, first))) continue;
                    var order = new List<int> { s };
                    int cur = s, next = first;
                    while (true)
                    {
                        used.Add(LineGroupOps.PairKey(cur, next));
                        order.Add(next);
                        if (Degree(next) != 2) break;
                        int nn = NextUnused(next);
                        if (nn < 0) break;
                        cur = next; next = nn;
                    }
                    // 分岐点から出て同じ分岐点へ戻った輪は、閉じた群にする（同じ頂点を 2 度持たせない）。
                    bool loop = order.Count >= 4 && order[order.Count - 1] == s;
                    if (loop) order.RemoveAt(order.Count - 1);
                    chains.Add((order, loop));
                }
            }

            // 2. 残りは次数 2 だけの輪（閉じた連なり）
            foreach (var kv in adj)
            {
                int s = kv.Key;
                int first = NextUnused(s);
                if (first < 0) continue;
                var order = new List<int> { s };
                used.Add(LineGroupOps.PairKey(s, first));
                int cur = first;
                while (cur != s)
                {
                    order.Add(cur);
                    int nn = NextUnused(cur);
                    if (nn < 0) break;
                    used.Add(LineGroupOps.PairKey(cur, nn));
                    cur = nn;
                }
                chains.Add((order, cur == s && order.Count >= 3));
            }

            foreach (var (order, closed) in chains)
            {
                int before = mo.Faces.Count;
                AddSegmentFaces(mo, order, closed);
                for (int fi = before; fi < mo.Faces.Count; fi++) lineFaceIndices?.Add(fi);
                int gi = LineGroupOps.AddGroup(mo, order, closed, null);
                if (gi < 0) { reason = "線分群を作れませんでした"; return false; }
                groupIndices?.Add(gi);
            }
            mo.RebuildIdSets();
            return true;
        }

        private static void AddNeighbor(SortedDictionary<int, List<int>> adj, int v, int n)
        {
            if (!adj.TryGetValue(v, out var list)) { list = new List<int>(); adj[v] = list; }
            list.Add(n);
        }

        /// <summary>
        /// 群の点列（と任意でハンドル）を差し替える。点の数が同じなら頂点を動かすだけ。
        /// 増えた点は新しい頂点を作り、減った点の頂点は他から使われていなければ消す。
        /// </summary>
        public static bool SetPoints(
            MeshObject mo, int groupIndex, IReadOnlyList<Vector3> points, bool closed,
            IReadOnlyList<LinePointHandle> handles, out string reason, bool clearHandles = false)
        {
            reason = null;
            if (!TryGetGroup(mo, groupIndex, out var g, out reason)) return false;
            if (points == null || points.Count < 2) { reason = "点は 2 個以上要ります"; return false; }
            if (handles != null && handles.Count > 0 && handles.Count != points.Count)
            { reason = "ハンドルの数が点の数と合いません"; return false; }

            // 群を外してから、その群の線分を消す（外さないと RemoveFaces が群を切る）。
            mo.LineGroups.RemoveAt(groupIndex);
            RemoveSegmentFaces(mo, g);

            // 減った点の頂点を消す（他の面・群から使われていないものだけ）。
            var order = new List<int>(g.Order);
            var oldHandles = g.HasHandles ? new List<LinePointHandle>(g.PointHandles) : null;
            if (points.Count < order.Count)
            {
                var drop = new List<int>();
                for (int k = points.Count; k < order.Count; k++)
                    if (!IsReferenced(mo, order[k], order, points.Count)) drop.Add(order[k]);
                order.RemoveRange(points.Count, order.Count - points.Count);
                if (drop.Count > 0)
                {
                    var map = mo.RemoveVertices(drop);
                    if (map != null)
                    {
                        for (int k = 0; k < order.Count; k++) order[k] = map[order[k]];
                        // 外してある群の親も同じ表で付け替える。
                        int pv = g.ParentVertex;
                        g.ParentVertex = (pv >= 0 && pv < map.Length) ? map[pv] : -1;
                        if (g.ParentVertex < 0) g.ParentVertexId = 0;
                    }
                }
            }

            // 残る点は動かし、増えた点は作る。
            for (int k = 0; k < points.Count; k++)
            {
                if (k < order.Count) mo.Vertices[order[k]].Position = points[k];
                else
                {
                    order.Add(mo.Vertices.Count);
                    mo.Vertices.Add(NewVertex(points[k]));
                }
            }

            AddSegmentFaces(mo, order, closed && order.Count >= 3);

            g.Order = order;
            g.OrderVertexIds = new List<int>(order.Count);
            foreach (int vi in order) g.OrderVertexIds.Add(mo.Vertices[vi].Id);
            g.Closed = closed && order.Count >= 3;

            if (clearHandles) g.PointHandles = new List<LinePointHandle>();
            else if (handles != null && handles.Count > 0)
            {
                SetHandles(g, handles);
                // 点の数が変わらない差し替えでは、拘束は今のものを残す（ずれだけ入れ替える）。
                if (oldHandles != null && oldHandles.Count == g.PointHandles.Count)
                    for (int k = 0; k < g.PointHandles.Count; k++)
                    {
                        if (oldHandles[k] == null) continue;
                        g.PointHandles[k].InConstraint  = oldHandles[k].InConstraint;
                        g.PointHandles[k].OutConstraint = oldHandles[k].OutConstraint;
                    }
            }
            else if (oldHandles != null)
            {
                g.PointHandles = new List<LinePointHandle>(order.Count);
                for (int k = 0; k < order.Count; k++)
                    g.PointHandles.Add(k < oldHandles.Count ? oldHandles[k] : LinePointHandle.CreateDefault());
            }
            else g.PointHandles = new List<LinePointHandle>();

            mo.LineGroups.Insert(Mathf.Min(groupIndex, mo.LineGroups.Count), g);
            LineHandleSolver.Solve(mo, g);
            mo.RebuildIdSets();
            return true;
        }

        /// <summary>群とその線分を消す。deleteVertices なら、他から使われていない頂点も消す。</summary>
        public static bool Delete(MeshObject mo, int groupIndex, bool deleteVertices, out string reason)
        {
            reason = null;
            if (!TryGetGroup(mo, groupIndex, out var g, out reason)) return false;

            mo.LineGroups.RemoveAt(groupIndex);
            RemoveSegmentFaces(mo, g);

            if (deleteVertices)
            {
                var drop = new List<int>();
                foreach (int vi in g.Order)
                    if (!drop.Contains(vi) && !IsReferenced(mo, vi, null, 0)) drop.Add(vi);
                if (drop.Count > 0) mo.RemoveVertices(drop);
            }
            mo.RebuildIdSets();
            return true;
        }

        /// <summary>点のハンドル拘束を変えて解き直す。ハンドルが無い群には既定のハンドルを作る。</summary>
        public static bool SetConstraint(
            MeshObject mo, int groupIndex, int pointIndex, bool isOut, HandleConstraint c, out string reason)
        {
            reason = null;
            if (!TryGetGroup(mo, groupIndex, out var g, out reason)) return false;
            if (pointIndex < 0 || pointIndex >= g.Order.Count) { reason = $"点の番号が範囲外です: {pointIndex}"; return false; }

            if (!g.HasHandles)
            {
                g.PointHandles = new List<LinePointHandle>(g.Order.Count);
                for (int k = 0; k < g.Order.Count; k++) g.PointHandles.Add(LinePointHandle.CreateDefault());
            }
            if (c.Length == HandleLength.Group && g.LengthGroups.Find(x => x != null && x.Id == c.LengthGroupId) == null)
            { reason = $"長さの組 {c.LengthGroupId} がありません"; return false; }

            var h = g.PointHandles[pointIndex];
            if (isOut) h.OutConstraint = c; else h.InConstraint = c;
            LineHandleSolver.Solve(mo, g);
            return true;
        }

        /// <summary>長さの組を作るか値を変えて、解き直す。</summary>
        public static bool SetLengthGroup(MeshObject mo, int groupIndex, int id, float length, out string reason)
        {
            reason = null;
            if (!TryGetGroup(mo, groupIndex, out var g, out reason)) return false;
            if (length < 0f) { reason = "長さは 0 以上にしてください"; return false; }

            var lg = g.LengthGroups.Find(x => x != null && x.Id == id);
            if (lg == null) g.LengthGroups.Add(new LineLengthGroup { Id = id, Length = length });
            else lg.Length = length;
            LineHandleSolver.Solve(mo, g);
            return true;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>
        /// 弦（線分群の区間の 2 頂点の面）の表示を揃える。ハンドルを持つ群の弦は曲線を
        /// 重ね描きで見せるので非表示（FaceFlags.Hidden）にし、持たない群の弦は表示に戻す。
        /// 頂点の表示は 3 頂点以上の面だけで決まるので、群の点は表示・選択できるまま。
        /// </summary>
        public static void SyncChordVisibility(MeshObject mo)
        {
            if (mo?.LineGroups == null) return;

            var hide = new HashSet<long>();
            var show = new HashSet<long>();
            foreach (var g in mo.LineGroups)
            {
                if (g?.Order == null) continue;
                int n = g.Order.Count;
                int segs = g.Closed ? n : n - 1;
                var set = g.HasHandles ? hide : show;
                for (int k = 0; k < segs; k++)
                    set.Add(LineGroupOps.PairKey(g.Order[k], g.Order[(k + 1) % n]));
            }
            if (hide.Count == 0 && show.Count == 0) return;

            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                long key = LineGroupOps.PairKey(f.VertexIndices[0], f.VertexIndices[1]);
                if (hide.Contains(key))      f.SetFlag(FaceFlags.Hidden);
                else if (show.Contains(key)) f.ClearFlag(FaceFlags.Hidden);
            }
        }

        /// <summary>
        /// 群の曲線を折れ線に焼き込む。曲線を分割した点列で点を差し替え、ハンドルと長さの組を捨てる。
        /// </summary>
        public static bool BakeCurve(MeshObject mo, int groupIndex, int segmentsPerSpan, out string reason)
        {
            reason = null;
            if (!TryGetGroup(mo, groupIndex, out var g, out reason)) return false;
            if (!g.HasHandles) { reason = "この線分群はハンドルを持っていません（折れ線です）"; return false; }
            if (segmentsPerSpan < 1) { reason = "分割数は 1 以上にしてください"; return false; }

            var pts = LineCurveSampler.SampleLocal(mo, g, segmentsPerSpan);
            if (!SetPoints(mo, groupIndex, pts, g.Closed, null, out reason, clearHandles: true)) return false;
            mo.LineGroups[groupIndex].LengthGroups = new List<LineLengthGroup>();
            return true;
        }

        private static bool TryGetGroup(MeshObject mo, int groupIndex, out LineGroup g, out string reason)
        {
            g = null; reason = null;
            if (mo?.LineGroups == null || groupIndex < 0 || groupIndex >= mo.LineGroups.Count)
            { reason = $"線分群の番号が範囲外です: {groupIndex}"; return false; }
            g = mo.LineGroups[groupIndex];
            if (g?.Order == null) { reason = "線分群が空です"; return false; }
            return true;
        }

        private static void SetHandles(LineGroup g, IReadOnlyList<LinePointHandle> handles)
        {
            g.PointHandles = new List<LinePointHandle>();
            if (handles == null) return;
            foreach (var h in handles) g.PointHandles.Add(h?.Clone() ?? LinePointHandle.CreateDefault());
        }

        /// <summary>隣り合う点の組ごとに 2 頂点の面を足す（閉じていれば終点→始点も）。</summary>
        private static void AddSegmentFaces(MeshObject mo, List<int> order, bool closed)
        {
            for (int k = 0; k + 1 < order.Count; k++) mo.Faces.Add(NewLineFace(order[k], order[k + 1]));
            if (closed) mo.Faces.Add(NewLineFace(order[order.Count - 1], order[0]));
        }

        /// <summary>群の区間に当たる 2 頂点の面を 1 枚ずつ消す。群は一覧から外してあること。</summary>
        private static void RemoveSegmentFaces(MeshObject mo, LineGroup g)
        {
            var want = new List<long>();
            int n = g.Order.Count;
            int segs = g.Closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
                want.Add(LineGroupOps.PairKey(g.Order[k], g.Order[(k + 1) % n]));

            var kill = new List<int>();
            for (int fi = 0; fi < mo.Faces.Count && want.Count > 0; fi++)
            {
                var f = mo.Faces[fi];
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                long key = LineGroupOps.PairKey(f.VertexIndices[0], f.VertexIndices[1]);
                int w = want.IndexOf(key);
                if (w < 0) continue;
                want.RemoveAt(w);
                kill.Add(fi);
            }
            if (kill.Count > 0) mo.RemoveFaces(kill);
        }

        /// <summary>
        /// 頂点がどこかから使われているか（面・一覧に残っている群）。
        /// keepOrder の先頭 keepCount 個も使われているとみなす（差し替えで残す点）。
        /// </summary>
        private static bool IsReferenced(MeshObject mo, int vi, List<int> keepOrder, int keepCount)
        {
            foreach (var f in mo.Faces)
                if (f?.VertexIndices != null && f.VertexIndices.Contains(vi)) return true;
            if (mo.LineGroups != null)
                foreach (var og in mo.LineGroups)
                    if (og?.Order != null && (og.Order.Contains(vi) || og.ParentVertex == vi)) return true;
            if (keepOrder != null)
                for (int k = 0; k < keepCount && k < keepOrder.Count; k++)
                    if (keepOrder[k] == vi) return true;
            return false;
        }

        private static Vertex NewVertex(Vector3 p)
            => new Vertex(p, Vector2.zero, Vector3.forward);

        private static Face NewLineFace(int a, int b)
        {
            var f = new Face { MaterialIndex = 0 };
            f.VertexIndices.Add(a); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            f.VertexIndices.Add(b); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            return f;
        }
    }
}
