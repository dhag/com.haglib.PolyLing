// Quad4To1Ops.cs
// 選択した1頂点が4枚の四角形に共有されているとき、その頂点と、その頂点に
// 接続する4頂点を新しい面から外し、残る4隅で四角形1枚に張り替える。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【頂点溶解（VertexDissolveOps）との違い】
//   頂点溶解は外周8頂点をそのまま使うので八角形になる。
//   こちらは頂点に接続する4頂点（辺の中点にあたる側）も外して四角形にする。
//
// 【手順】
//   1. 頂点A を含む面を集める。4枚ちょうど・すべて四角形・閉じたファンであること。
//   2. 面 F の A の位置を p とすると「始端 = V[p+1]」「終端 = V[p-1]」。
//      巻き方向が揃っていれば「前の面の終端 = 次の面の始端」でつながる。
//      これを辿って閉じたファンかを判定する（境界頂点は不可）。
//   3. 各面から V[p+1] と V[p+2] を順に取り出すと、外周リングは
//      r1, c1, r2, c2, r3, c3, r4, c4 の並びになる。
//      r が A に接続する頂点、c が A に接続しない隅。奇数番目の c だけを使う。
//   4. ファンの先頭面（面インデックス最小）を四角形 (c1,c2,c3,c4) で上書きし、
//      残り3枚を削除する。面ID / MaterialIndex / Flags は先頭面のものが残る。
//   5. A と r1..r4 のうち、どの面からも参照されなくなったものを削除して
//      頂点インデックスを詰める。他の面がまだ使っている r は残る。
//
// 【不変条件（厳守）】
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   新頂点も新スロットも作らず、隅が元の面で使っていたスロット番号を引き継ぐ。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class Quad4To1Ops
    {
        /// <summary>実行可否と対象の規模。パネルの表示・ボタン活性に使う。</summary>
        public struct QuadMergeInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>消える面の数（常に3）。</summary>
            public int RemovedFaceCount;
            /// <summary>参照が無くなって消える頂点の数（1〜5）。</summary>
            public int RemovedVertexCount;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>頂点A の周辺を調べ、実行可否を返す。メッシュは変更しない。</summary>
        public static QuadMergeInfo Inspect(MeshObject mo, int apex)
        {
            var info = new QuadMergeInfo();

            if (mo == null || apex < 0 || apex >= mo.Vertices.Count)
            {
                info.Reason = "頂点が指定されていません";
                return info;
            }

            if (!BuildFan(mo, apex, out var order, out var corners, out _, out _,
                          out var linked, out string reason))
            {
                info.Reason = reason;
                return info;
            }

            info.RemovedFaceCount   = order.Count - 1;
            info.RemovedVertexCount = CollectOrphans(mo, apex, order, corners, linked).Count;
            info.CanExecute         = true;
            return info;
        }

        // ================================================================
        // ファン構築（Inspect と Execute の共通処理）
        // ================================================================

        /// <summary>
        /// 頂点A を囲む4枚の四角形を巻き方向どおりに並べ、隅4頂点と、
        /// A に接続する4頂点を求める。メッシュは変更しない。
        /// 条件を満たさなければ false。
        /// </summary>
        private static bool BuildFan(
            MeshObject mo, int apex,
            out List<int> order, out List<int> corners,
            out List<int> cornerUV, out List<int> cornerNormal,
            out List<int> linked,
            out string reason)
        {
            order        = new List<int>();
            corners      = new List<int>();
            cornerUV     = new List<int>();
            cornerNormal = new List<int>();
            linked       = new List<int>();

            // --- A を含む面を集める（面インデックス昇順） ---
            var fanFaces = new List<int>();

            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;

                int occur = 0;
                for (int j = 0; j < n; j++)
                    if (f.VertexIndices[j] == apex) occur++;

                if (occur == 0) continue;

                if (occur > 1)
                {
                    reason = "同じ面が指定頂点を複数回参照しています";
                    return false;
                }
                if (n != 4)
                {
                    reason = "指定頂点を囲む面に四角形でないものがあります";
                    return false;
                }

                fanFaces.Add(fi);
            }

            if (fanFaces.Count == 0)
            {
                reason = "指定頂点はどの面にも使われていません";
                return false;
            }
            if (fanFaces.Count != 4)
            {
                reason = $"指定頂点を共有する四角形が4枚ではありません（{fanFaces.Count}枚）";
                return false;
            }

            // --- 始端（A の次の頂点）から面を引ける表を作る ---
            var startToFace = new Dictionary<int, int>();
            foreach (int fi in fanFaces)
            {
                var f = mo.Faces[fi];
                int p = f.VertexIndices.IndexOf(apex);
                int start = f.VertexIndices[(p + 1) % 4];

                if (startToFace.ContainsKey(start))
                {
                    reason = "指定頂点の周りが多様体になっていません";
                    return false;
                }
                startToFace[start] = fi;
            }

            // --- 「前の面の終端 = 次の面の始端」で数珠つなぎに辿る ---
            int firstFace = fanFaces[0];
            var visited   = new HashSet<int>();
            int cur       = firstFace;

            while (true)
            {
                if (!visited.Add(cur))
                {
                    reason = "指定頂点の周りが多様体になっていません";
                    return false;
                }
                order.Add(cur);

                var f = mo.Faces[cur];
                int p = f.VertexIndices.IndexOf(apex);
                int end = f.VertexIndices[(p - 1 + 4) % 4];

                if (!startToFace.TryGetValue(end, out int next))
                {
                    reason = "指定頂点の周りが閉じていません（境界の頂点です）";
                    return false;
                }
                if (next == firstFace) break;

                cur = next;
            }

            if (order.Count != 4)
            {
                reason = "指定頂点の周りに独立した面のかたまりがあります";
                return false;
            }

            // --- 外周リング r1,c1,r2,c2,r3,c3,r4,c4 のうち c だけを取る ---
            foreach (int fi in order)
            {
                var f = mo.Faces[fi];
                int p = f.VertexIndices.IndexOf(apex);

                int rIdx = (p + 1) % 4;   // A に接続する頂点
                int cIdx = (p + 2) % 4;   // A に接続しない隅

                linked.Add(f.VertexIndices[rIdx]);

                corners.Add(f.VertexIndices[cIdx]);
                cornerUV.Add(cIdx < f.UVIndices.Count ? f.UVIndices[cIdx] : 0);
                cornerNormal.Add(cIdx < f.NormalIndices.Count ? f.NormalIndices[cIdx] : 0);
            }

            var seen = new HashSet<int>();
            foreach (int v in corners)
            {
                if (!seen.Add(v))
                {
                    reason = "四隅の頂点が重複しているため実行できません";
                    return false;
                }
            }
            foreach (int v in corners)
            {
                if (v == apex || linked.Contains(v))
                {
                    reason = "四隅の頂点が指定頂点またはその隣と重なっています";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 統合したときに、どの面からも参照されなくなる頂点（A と A に接続する4頂点）を返す。
        /// メッシュは変更しない。
        /// </summary>
        private static List<int> CollectOrphans(
            MeshObject mo, int apex, List<int> order, List<int> corners, List<int> linked)
        {
            var kill = new HashSet<int>(order);

            var used = new HashSet<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                if (kill.Contains(fi)) continue;
                foreach (int v in mo.Faces[fi].VertexIndices) used.Add(v);
            }
            // 先頭面は四隅の四角形で張り替えられる。
            foreach (int v in corners) used.Add(v);

            var orphans = new List<int>();
            if (!used.Contains(apex)) orphans.Add(apex);
            foreach (int v in linked)
                if (!used.Contains(v) && !orphans.Contains(v)) orphans.Add(v);

            return orphans;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 頂点A を共有する4枚の四角形を、四隅の四角形1枚に統合する。成功したら true。
        /// </summary>
        public static bool Execute(
            MeshObject mo, int apex,
            out int removedFaceCount, out List<int> removedVertices, out string reason)
        {
            removedFaceCount = 0;
            removedVertices  = new List<int>();

            if (mo == null || apex < 0 || apex >= mo.Vertices.Count)
            {
                reason = "頂点が指定されていません";
                return false;
            }

            if (!BuildFan(mo, apex, out var order, out var corners,
                          out var cornerUV, out var cornerNormal, out var linked, out reason))
                return false;

            var orphans = CollectOrphans(mo, apex, order, corners, linked);

            // 先頭面（面インデックス最小）を四隅の四角形で上書きする。
            int baseFaceIndex = order[0];
            var baseFace = mo.Faces[baseFaceIndex];
            baseFace.VertexIndices = new List<int>(corners);
            baseFace.UVIndices     = new List<int>(cornerUV);
            baseFace.NormalIndices = new List<int>(cornerNormal);

            // 残り3枚を降順で削除する。baseFaceIndex は最小なのでずれない。
            var removeList = new List<int>();
            foreach (int fi in order)
                if (fi != baseFaceIndex) removeList.Add(fi);
            removeList.Sort();

            for (int k = removeList.Count - 1; k >= 0; k--)
                mo.Faces.RemoveAt(removeList[k]);

            removedFaceCount = removeList.Count;

            // どの面からも参照されなくなった頂点を削除し、インデックスを詰める。
            if (orphans.Count > 0)
            {
                var kill = new HashSet<int>(orphans);

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

                orphans.Sort();
                for (int k = orphans.Count - 1; k >= 0; k--)
                {
                    if (orphans[k] >= 0 && orphans[k] < mo.Vertices.Count)
                        mo.Vertices.RemoveAt(orphans[k]);
                }

                removedVertices = orphans;
                mo.InvalidatePositionCache();
            }

            reason = null;
            return true;
        }

        // ================================================================
        // 一括処理（複数頂点）
        // ================================================================

        /// <summary>一括処理の下調べ結果。パネル表示とボタン活性に使う。</summary>
        public struct QuadMergeBatchInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象の頂点数。</summary>
            public int TargetCount;
            /// <summary>条件不一致・干渉で除外した頂点数。</summary>
            public int SkippedCount;
            /// <summary>消える面の合計。</summary>
            public int RemovedFaceTotal;
            /// <summary>消える頂点の合計。</summary>
            public int RemovedVertexTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない頂点だけを選び出す。
        /// 単独で実行できない頂点と、同じ面を共有する頂点どうしを落とす。
        /// </summary>
        public static List<int> SelectIndependent(
            MeshObject mo, IEnumerable<int> apexes, out List<int> skipped)
        {
            var accepted = new List<int>();
            skipped      = new List<int>();

            if (mo == null || apexes == null) return accepted;

            var candidates = new HashSet<int>();
            foreach (int a in apexes)
            {
                if (a < 0 || a >= mo.Vertices.Count) continue;
                if (!candidates.Add(a)) continue;

                if (!Inspect(mo, a).CanExecute)
                {
                    candidates.Remove(a);
                    skipped.Add(a);
                }
            }

            var conflicted = new HashSet<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var vidx = mo.Faces[fi].VertexIndices;

                var onFace = new List<int>();
                for (int j = 0; j < vidx.Count; j++)
                {
                    int v = vidx[j];
                    if (candidates.Contains(v) && !onFace.Contains(v)) onFace.Add(v);
                }

                if (onFace.Count >= 2)
                    foreach (int v in onFace) conflicted.Add(v);
            }

            foreach (int a in candidates)
            {
                if (conflicted.Contains(a)) skipped.Add(a);
                else                        accepted.Add(a);
            }

            accepted.Sort();
            skipped.Sort();
            return accepted;
        }

        /// <summary>複数頂点ぶんの下調べ。メッシュは変更しない。</summary>
        public static QuadMergeBatchInfo InspectMany(MeshObject mo, IEnumerable<int> apexes)
        {
            var info = new QuadMergeBatchInfo();

            if (mo == null)
            {
                info.Reason = "メッシュがありません";
                return info;
            }

            var targets = SelectIndependent(mo, apexes, out var skipped);

            info.TargetCount  = targets.Count;
            info.SkippedCount = skipped.Count;

            foreach (int a in targets)
            {
                var one = Inspect(mo, a);
                info.RemovedFaceTotal   += one.RemovedFaceCount;
                info.RemovedVertexTotal += one.RemovedVertexCount;
            }

            if (targets.Count == 0)
            {
                info.Reason = skipped.Count > 0
                    ? "選択頂点が条件を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return info;
            }

            info.CanExecute = true;
            return info;
        }

        /// <summary>
        /// 複数頂点を統合する。互いに干渉する頂点は処理しない。
        /// 1つでも処理できたら true。実行順はインデックスの降順。
        /// </summary>
        public static bool ExecuteMany(
            MeshObject mo, IEnumerable<int> apexes,
            out int mergedCount, out int removedFaceCount, out int removedVertexCount,
            out int skippedCount, out string reason)
        {
            mergedCount        = 0;
            removedFaceCount   = 0;
            removedVertexCount = 0;
            skippedCount       = 0;
            reason             = null;

            if (mo == null) { reason = "メッシュがありません"; return false; }

            var targets = SelectIndependent(mo, apexes, out var skipped);
            skippedCount = skipped.Count;

            if (targets.Count == 0)
            {
                reason = skipped.Count > 0
                    ? "選択頂点が条件を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return false;
            }

            // A だけでなく A に接続する頂点も消えるため、単純な降順では後続対象が
            // ずれる。実行のたびに残りの対象インデックスを詰め直す。
            var pending = new List<int>(targets);

            for (int k = 0; k < pending.Count; k++)
            {
                bool ok = Execute(mo, pending[k], out int removedFaces, out var killed, out string why);
                if (!ok)
                {
                    skippedCount++;
                    reason = why;
                    continue;
                }

                mergedCount        ++;
                removedFaceCount   += removedFaces;
                removedVertexCount += killed.Count;

                if (killed.Count == 0) continue;

                for (int j = k + 1; j < pending.Count; j++)
                    pending[j] = ShiftIndex(pending[j], killed);
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
