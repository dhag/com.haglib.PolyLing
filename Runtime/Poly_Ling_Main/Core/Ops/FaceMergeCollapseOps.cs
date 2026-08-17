// FaceMergeCollapseOps.cs
// 選択した辺を挟む2枚の面を結合し、その辺の両端点を新しい面から外して1枚にする。
// 面結合（FaceMergeOps）との違いは共有頂点の扱いだけ。
//   FaceMergeOps         … 他の面が使っていない共有頂点だけを外す
//   FaceMergeCollapseOps … 共有頂点は他の面が使っていても常に外す
// どちらも、外すと3頂点未満になる場合は外さない（三角形同士 → 四角形）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. 選択辺 (a,b) を「辺として」持つ面を探す。ちょうど2枚でなければ何もしない。
//      面の中で a と b が隣り合っていない場合（四角形の対角線など）は数えない。
//   2. その2枚が2本以上の辺を共有している場合は何もしない。
//   3. 各面で共有辺の forward 方向を判定し、非共有経路をつないで1つの多角形にする。
//      （EdgeTopologyTool.MergeFaceVertices と同じ走査。ただし UV/法線スロットも
//        同じ経路で拾い、元の面のものをそのまま引き継ぐ。）
//   4. 共有頂点 a, b を多角形の並びから外す（前後の点が直接つながる）。
//      他の面がまだ使っていても外す。外した結果 3頂点未満になる場合だけ外さない。
//      例: 三角形同士 → 四角形（外すと線分になるので外さない）
//          三角形と四角形 → 三角形 / 四角形同士 → 四角形
//   5. 外した頂点は、どの面からも参照されなくなったときだけ削除する。
//      他の面がまだ使っている頂点はメッシュに残る。
//
// 【不変条件（厳守）】
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   新頂点も新スロットも作らず、元の面の UV/法線スロット番号をそのまま引き継ぐ。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class FaceMergeCollapseOps
    {
        /// <summary>実行可否と結合結果の規模。パネルの表示・ボタン活性に使う。</summary>
        public struct FaceMergeCollapseInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>結合後の面の頂点数。</summary>
            public int ResultVertexCount;
            /// <summary>参照が無くなって消える頂点の数（0〜2）。</summary>
            public int RemovedVertexCount;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>選択辺の周辺を調べ、実行可否を返す。メッシュは変更しない。</summary>
        public static FaceMergeCollapseInfo Inspect(MeshObject mo, VertexPair edge)
        {
            var info = new FaceMergeCollapseInfo();

            if (mo == null || !edge.IsValid)
            {
                info.Reason = "辺が指定されていません";
                return info;
            }

            if (!BuildMerge(mo, edge, out int fa, out int fb, out var ring, out _, out _,
                            out var detached, out string reason))
            {
                info.Reason = reason;
                return info;
            }

            // 外した頂点のうち、結合する2枚以外がどこも使っていないものだけが消える。
            int vanish = 0;
            foreach (int v in detached)
            {
                bool stillUsed = false;
                for (int fi = 0; fi < mo.Faces.Count; fi++)
                {
                    if (fi == fa || fi == fb) continue;
                    if (mo.Faces[fi].VertexIndices.IndexOf(v) < 0) continue;
                    stillUsed = true;
                    break;
                }
                if (!stillUsed) vanish++;
            }

            info.ResultVertexCount  = ring.Count;
            info.RemovedVertexCount = vanish;
            info.CanExecute         = true;
            return info;
        }

        // ================================================================
        // 結合形状の構築（Inspect と Execute の共通処理）
        // ================================================================

        /// <summary>
        /// 選択辺を挟む2面を探し、結合後の多角形と、削除できる共有頂点を求める。
        /// メッシュは変更しない。条件を満たさなければ false。
        ///
        /// ring / ringUV / ringNormal は共有頂点の削除を反映済み。
        /// </summary>
        private static bool BuildMerge(
            MeshObject mo, VertexPair edge,
            out int faceIndexA, out int faceIndexB,
            out List<int> ring, out List<int> ringUV, out List<int> ringNormal,
            out List<int> detachedVertices,
            out string reason)
        {
            faceIndexA     = -1;
            faceIndexB     = -1;
            ring           = new List<int>();
            ringUV         = new List<int>();
            ringNormal     = new List<int>();
            detachedVertices = new List<int>();

            int a = edge.V1;
            int b = edge.V2;

            if (a < 0 || a >= mo.Vertices.Count || b < 0 || b >= mo.Vertices.Count)
            {
                reason = "辺の頂点が範囲外です";
                return false;
            }

            // --- 選択辺を「辺として」持つ面を探す（線分は数えない） ---
            int shareCount = 0;
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                if (f.VertexIndices.Count < 3) continue;
                if (!HasEdge(f, a, b)) continue;

                shareCount++;
                if      (faceIndexA < 0) faceIndexA = fi;
                else if (faceIndexB < 0) faceIndexB = fi;
            }

            if (shareCount < 2)
            {
                reason = "この辺に接している面が2枚ありません";
                return false;
            }
            if (shareCount > 2)
            {
                reason = "この辺を3枚以上の面が共有しています";
                return false;
            }

            var face1 = mo.Faces[faceIndexA];
            var face2 = mo.Faces[faceIndexB];

            // --- 2辺以上を共有していたら何もしない ---
            int sharedEdges = CountSharedEdges(face1, face2);
            if (sharedEdges > 1)
            {
                reason = $"2枚の面が {sharedEdges} 本の辺を共有しているため結合しません";
                return false;
            }

            // --- 非共有経路をつないで1つの多角形にする ---
            if (!AppendPath(face1, a, b, ring, ringUV, ringNormal) ||
                !AppendPath(face2, a, b, ring, ringUV, ringNormal))
            {
                reason = "共有辺の向きを判定できません";
                return false;
            }

            if (ring.Count < 3)
            {
                reason = "結合すると面にならないため結合しません";
                return false;
            }

            var seen = new HashSet<int>();
            foreach (int v in ring)
            {
                if (!seen.Add(v))
                {
                    reason = "結合すると同じ頂点を2回使う面になるため結合しません";
                    return false;
                }
            }

            // --- 共有頂点2つを新しい面から外す ---
            // 他の面が使っていても外す（外すのは面の並びからで、頂点自体の削除は
            // どの面からも参照されなくなったときだけ）。
            // 両方外すと3頂点未満になる場合は、どちらも外さない。
            if (ring.Count - 2 >= 3)
            {
                var kill = new HashSet<int> { a, b };
                for (int j = ring.Count - 1; j >= 0; j--)
                {
                    if (!kill.Contains(ring[j])) continue;
                    ring.RemoveAt(j);
                    ringUV.RemoveAt(j);
                    ringNormal.RemoveAt(j);
                }
                detachedVertices.Add(a);
                detachedVertices.Add(b);
                detachedVertices.Sort();
            }

            reason = null;
            return true;
        }

        /// <summary>面が (a,b) を辺として（隣り合う頂点として）持つか。</summary>
        private static bool HasEdge(Face f, int a, int b)
        {
            var v = f.VertexIndices;
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                int p = v[i];
                int q = v[(i + 1) % n];
                if ((p == a && q == b) || (p == b && q == a)) return true;
            }
            return false;
        }

        /// <summary>2つの面が共有している辺の数。</summary>
        private static int CountSharedEdges(Face f1, Face f2)
        {
            var v1 = f1.VertexIndices;
            int n1 = v1.Count;
            int count = 0;

            for (int i = 0; i < n1; i++)
            {
                int p = v1[i];
                int q = v1[(i + 1) % n1];
                if (HasEdge(f2, p, q)) count++;
            }
            return count;
        }

        /// <summary>
        /// 面の非共有経路（共有辺の終点から始点の直前まで）を ring へ追加する。
        /// 共有辺が面の辺として存在しなければ false。
        /// </summary>
        private static bool AppendPath(
            Face f, int a, int b,
            List<int> ring, List<int> ringUV, List<int> ringNormal)
        {
            var v = f.VertexIndices;
            int n = v.Count;

            int ia = v.IndexOf(a);
            int ib = v.IndexOf(b);
            if (ia < 0 || ib < 0) return false;

            int start, end;
            if      ((ia + 1) % n == ib) { start = ib; end = ia; }
            else if ((ib + 1) % n == ia) { start = ia; end = ib; }
            else return false;

            int cur = start;
            while (cur != end)
            {
                ring.Add(v[cur]);
                ringUV.Add(cur < f.UVIndices.Count ? f.UVIndices[cur] : 0);
                ringNormal.Add(cur < f.NormalIndices.Count ? f.NormalIndices[cur] : 0);
                cur = (cur + 1) % n;
            }
            return true;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 選択辺を挟む2面を1枚に結合する。成功したら true。
        /// removedVertices には削除した頂点インデックス（削除前の番号、昇順）が入る。
        /// </summary>
        public static bool Execute(
            MeshObject mo, VertexPair edge,
            out int resultVertexCount, out List<int> removedVertices, out string reason)
        {
            resultVertexCount = 0;
            removedVertices   = new List<int>();

            if (mo == null || !edge.IsValid)
            {
                reason = "辺が指定されていません";
                return false;
            }

            if (!BuildMerge(mo, edge, out int faceIndexA, out int faceIndexB,
                            out var ring, out var ringUV, out var ringNormal,
                            out var detached, out reason))
                return false;

            // 面インデックスの小さい方を残して上書きする。
            // 面ID / MaterialIndex / Flags は残した方のものが残る。
            int keepIndex   = Mathf.Min(faceIndexA, faceIndexB);
            int removeIndex = Mathf.Max(faceIndexA, faceIndexB);

            var keep = mo.Faces[keepIndex];
            keep.VertexIndices = ring;
            keep.UVIndices     = ringUV;
            keep.NormalIndices = ringNormal;

            mo.Faces.RemoveAt(removeIndex);

            resultVertexCount = ring.Count;

            // 外した共有頂点のうち、どの面からも参照されなくなったものだけを削除する。
            var killed = new List<int>();
            foreach (int v in detached)
            {
                bool stillUsed = false;
                foreach (var face in mo.Faces)
                {
                    if (face.VertexIndices.IndexOf(v) < 0) continue;
                    stillUsed = true;
                    break;
                }
                if (!stillUsed) killed.Add(v);
            }
            killed.Sort();

            if (killed.Count > 0)
            {
                var kill = new HashSet<int>(killed);

                int originalCount = mo.Vertices.Count;
                var indexMap = new int[originalCount];
                int newIndex = 0;
                for (int i = 0; i < originalCount; i++)
                    indexMap[i] = kill.Contains(i) ? -1 : newIndex++;

                foreach (var face in mo.Faces)
                {
                    var vidx = face.VertexIndices;
                    for (int j = 0; j < vidx.Count; j++)
                    {
                        int old = vidx[j];
                        if (old >= 0 && old < originalCount && indexMap[old] >= 0)
                            vidx[j] = indexMap[old];
                    }
                }

                for (int k = killed.Count - 1; k >= 0; k--)
                {
                    if (killed[k] >= 0 && killed[k] < mo.Vertices.Count)
                        mo.Vertices.RemoveAt(killed[k]);
                }

                removedVertices = killed;
                mo.InvalidatePositionCache();
            }

            reason = null;
            return true;
        }

        // ================================================================
        // 一括処理（複数辺）
        // ================================================================

        /// <summary>一括処理の下調べ結果。パネル表示とボタン活性に使う。</summary>
        public struct FaceMergeCollapseBatchInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象の辺数。</summary>
            public int TargetCount;
            /// <summary>条件不一致・干渉で除外した辺数。</summary>
            public int SkippedCount;
            /// <summary>消える面の合計（1辺につき1枚）。</summary>
            public int RemovedFaceTotal;
            /// <summary>消える頂点の合計。</summary>
            public int RemovedVertexTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない辺だけを選び出す。
        ///
        /// 除外の規則:
        ///   1. 単独で結合できない辺。
        ///   2. 結合する2枚のどちらかが他の選択辺と重なる辺（その組は全部除外）。
        /// </summary>
        public static List<VertexPair> SelectIndependent(
            MeshObject mo, IEnumerable<VertexPair> edges, out List<VertexPair> skipped)
        {
            var accepted = new List<VertexPair>();
            skipped      = new List<VertexPair>();

            if (mo == null || edges == null) return accepted;

            var candidates = new List<VertexPair>();
            var seen       = new HashSet<VertexPair>();
            var pairOf     = new Dictionary<VertexPair, int[]>();

            foreach (var e in edges)
            {
                if (!e.IsValid) continue;
                if (!seen.Add(e)) continue;

                if (!BuildMerge(mo, e, out int fa, out int fb, out _, out _, out _, out _, out _))
                {
                    skipped.Add(e);
                    continue;
                }

                pairOf[e] = new[] { fa, fb };
                candidates.Add(e);
            }

            // 同じ面に関わる候補を落とす。
            var owners = new Dictionary<int, List<VertexPair>>();
            foreach (var e in candidates)
            {
                foreach (int fi in pairOf[e])
                {
                    if (!owners.TryGetValue(fi, out var list))
                    {
                        list = new List<VertexPair>();
                        owners[fi] = list;
                    }
                    list.Add(e);
                }
            }

            var conflicted = new HashSet<VertexPair>();
            foreach (var kv in owners)
            {
                if (kv.Value.Count < 2) continue;
                foreach (var e in kv.Value) conflicted.Add(e);
            }

            foreach (var e in candidates)
            {
                if (conflicted.Contains(e)) skipped.Add(e);
                else                        accepted.Add(e);
            }

            return accepted;
        }

        /// <summary>複数辺ぶんの下調べ。メッシュは変更しない。</summary>
        public static FaceMergeCollapseBatchInfo InspectMany(MeshObject mo, IEnumerable<VertexPair> edges)
        {
            var info = new FaceMergeCollapseBatchInfo();

            if (mo == null)
            {
                info.Reason = "メッシュがありません";
                return info;
            }

            var targets = SelectIndependent(mo, edges, out var skipped);

            info.TargetCount  = targets.Count;
            info.SkippedCount = skipped.Count;

            foreach (var e in targets)
            {
                var one = Inspect(mo, e);
                info.RemovedFaceTotal   += 1;
                info.RemovedVertexTotal += one.RemovedVertexCount;
            }

            if (targets.Count == 0)
            {
                info.Reason = skipped.Count > 0
                    ? "選択辺が条件を満たさないか、互いに干渉しています"
                    : "辺を選択してください";
                return info;
            }

            info.CanExecute = true;
            return info;
        }

        /// <summary>
        /// 複数の選択辺で結合する。互いに干渉する辺は処理しない。
        /// 1つでも処理できたら true。
        ///
        /// 頂点を削除すると後続の対象辺の頂点番号がずれるため、削除のたびに
        /// 残りの対象辺を詰め直す。
        /// </summary>
        public static bool ExecuteMany(
            MeshObject mo, IEnumerable<VertexPair> edges,
            out int mergedCount, out int removedFaceCount, out int removedVertexCount,
            out int skippedCount, out string reason)
        {
            mergedCount        = 0;
            removedFaceCount   = 0;
            removedVertexCount = 0;
            skippedCount       = 0;
            reason             = null;

            if (mo == null) { reason = "メッシュがありません"; return false; }

            var targets = SelectIndependent(mo, edges, out var skipped);
            skippedCount = skipped.Count;

            if (targets.Count == 0)
            {
                reason = skipped.Count > 0
                    ? "選択辺が条件を満たさないか、互いに干渉しています"
                    : "辺を選択してください";
                return false;
            }

            var pending = new List<VertexPair>(targets);

            for (int i = 0; i < pending.Count; i++)
            {
                bool ok = Execute(mo, pending[i], out _, out var killed, out string why);
                if (!ok)
                {
                    skippedCount++;
                    reason = why;
                    continue;
                }

                mergedCount      ++;
                removedFaceCount ++;
                removedVertexCount += killed.Count;

                if (killed.Count == 0) continue;

                // 削除した頂点より大きい番号を詰める。
                for (int k = i + 1; k < pending.Count; k++)
                {
                    int v1 = ShiftIndex(pending[k].V1, killed);
                    int v2 = ShiftIndex(pending[k].V2, killed);
                    pending[k] = new VertexPair(v1, v2);
                }
            }

            if (mergedCount == 0) return false;

            reason = null;
            return true;
        }

        /// <summary>削除された頂点（昇順）を踏まえてインデックスを詰める。</summary>
        private static int ShiftIndex(int index, List<int> removedSorted)
        {
            int shift = 0;
            for (int i = 0; i < removedSorted.Count; i++)
            {
                if (removedSorted[i] < index) shift++;
            }
            return index - shift;
        }
    }
}
