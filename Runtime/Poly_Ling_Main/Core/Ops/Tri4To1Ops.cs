// Tri4To1Ops.cs
// 選択した三角形と、それを囲む三角形3枚（辺を共有する隣接面）を消して、
// 外側の3頂点を結ぶ三角形1枚に張り替える。中点細分割の逆操作にあたる。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   選択面 T = (a, b, c)。辺 (a,b) (b,c) (c,a) をそれぞれ共有する面を1枚ずつ探し、
//   その3枚がすべて三角形であることを確認する。各隣接面で選択面に接しない頂点を
//   d (辺 a-b の向かい) / e (辺 b-c の向かい) / f (辺 c-a の向かい) とすると、
//   4枚の外周は a→d→b→e→c→f→a の六角形になる。外側の3点だけを取った
//   (d, e, f) は元と同じ巻き方向の三角形になるので、これで張り替える。
//
//   1. T の Face を (d, e, f) で上書きする（面ID / MaterialIndex / Flags は T のものが残る）。
//   2. 隣接3面を降順で削除する。
//   3. a, b, c のうち、どの面からも参照されなくなったものだけを削除して
//      頂点インデックスを詰める。他の面がまだ使っているものは残す。
//
// 【不変条件（厳守）】
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   新頂点も新スロットも作らず、d / e / f が隣接面で使っていた UV/法線スロット番号を
//   そのまま引き継ぐ。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class Tri4To1Ops
    {
        /// <summary>実行可否と対象の規模。パネルの表示・ボタン活性に使う。</summary>
        public struct MergeInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>消える面の数（常に3）。</summary>
            public int RemovedFaceCount;
            /// <summary>参照が無くなって消える頂点の数（0〜3）。</summary>
            public int OrphanVertexCount;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>選択面の周辺を調べ、実行可否を返す。メッシュは変更しない。</summary>
        public static MergeInfo Inspect(MeshObject mo, int faceIndex)
        {
            var info = new MergeInfo();

            if (mo == null || faceIndex < 0 || faceIndex >= mo.Faces.Count)
            {
                info.Reason = "面が指定されていません";
                return info;
            }

            if (!BuildPatch(mo, faceIndex, out var neighbors, out var outer, out _, out _, out string reason))
            {
                info.Reason = reason;
                return info;
            }

            info.RemovedFaceCount  = neighbors.Count;
            info.OrphanVertexCount = CountOrphans(mo, faceIndex, neighbors, outer).Count;
            info.CanExecute        = true;
            return info;
        }

        // ================================================================
        // パッチ構築（Inspect と Execute の共通処理）
        // ================================================================

        /// <summary>
        /// 選択面の3辺それぞれの隣接面を求め、外側の3頂点と、その UV/法線スロットを返す。
        /// メッシュは変更しない。条件を満たさなければ false。
        ///
        /// neighbors / outer / outerUV / outerNormal は辺 (a,b) (b,c) (c,a) の順に並ぶ。
        /// </summary>
        private static bool BuildPatch(
            MeshObject mo, int faceIndex,
            out List<int> neighbors, out List<int> outer,
            out List<int> outerUV, out List<int> outerNormal,
            out string reason)
        {
            neighbors   = new List<int>();
            outer       = new List<int>();
            outerUV     = new List<int>();
            outerNormal = new List<int>();

            var f = mo.Faces[faceIndex];
            if (f.VertexIndices.Count != 3)
            {
                reason = "選択した面が三角形ではありません";
                return false;
            }

            int a = f.VertexIndices[0];
            int b = f.VertexIndices[1];
            int c = f.VertexIndices[2];

            if (a == b || b == c || c == a)
            {
                reason = "選択した面が同じ頂点を複数回参照しています";
                return false;
            }

            // 辺 (a,b) (b,c) (c,a) の順に隣接面を求める。
            // この順で外側の頂点を並べると、外周 a→d→b→e→c→f→a の外側だけを
            // 取り出した形になり、元の巻き方向が保たれる。
            int[] e0 = { a, b, c };
            int[] e1 = { b, c, a };

            for (int k = 0; k < 3; k++)
            {
                int v0 = e0[k];
                int v1 = e1[k];

                int found = -1;
                int shareCount = 0;

                for (int gi = 0; gi < mo.Faces.Count; gi++)
                {
                    if (gi == faceIndex) continue;

                    var g = mo.Faces[gi];
                    // 線分（2頂点の面）は隣接面として数えない。
                    if (g.VertexIndices.Count < 3) continue;
                    if (!g.VertexIndices.Contains(v0) || !g.VertexIndices.Contains(v1)) continue;

                    shareCount++;
                    if (found < 0) found = gi;
                }

                if (shareCount == 0)
                {
                    reason = "選択した面が三角形で囲まれていません（境界の辺があります）";
                    return false;
                }
                if (shareCount > 1)
                {
                    reason = "1つの辺を3枚以上の面が共有しています";
                    return false;
                }

                var nf = mo.Faces[found];
                if (nf.VertexIndices.Count != 3)
                {
                    reason = "選択した面を囲む面に三角形でないものがあります";
                    return false;
                }

                // 選択面に接しない頂点（外側の1点）を取り出す。
                int corner = -1;
                for (int j = 0; j < 3; j++)
                {
                    int v = nf.VertexIndices[j];
                    if (v == v0 || v == v1) continue;
                    if (corner >= 0)
                    {
                        corner = -1;
                        break;
                    }
                    corner = j;
                }

                if (corner < 0)
                {
                    reason = "囲む面の頂点が正しく取れません";
                    return false;
                }

                neighbors.Add(found);
                outer.Add(nf.VertexIndices[corner]);
                outerUV.Add(corner < nf.UVIndices.Count ? nf.UVIndices[corner] : 0);
                outerNormal.Add(corner < nf.NormalIndices.Count ? nf.NormalIndices[corner] : 0);
            }

            if (neighbors[0] == neighbors[1] || neighbors[1] == neighbors[2] || neighbors[2] == neighbors[0])
            {
                reason = "同じ面が2つ以上の辺を共有しています";
                return false;
            }

            if (outer[0] == outer[1] || outer[1] == outer[2] || outer[2] == outer[0])
            {
                reason = "外側の3頂点が重複しています";
                return false;
            }

            for (int k = 0; k < 3; k++)
            {
                if (outer[k] == a || outer[k] == b || outer[k] == c)
                {
                    reason = "外側の頂点が選択した面の頂点と重なっています";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 4枚を統合したときに、どの面からも参照されなくなる選択面の頂点を返す。
        /// メッシュは変更しない。
        /// </summary>
        private static List<int> CountOrphans(
            MeshObject mo, int faceIndex, List<int> neighbors, List<int> outer)
        {
            var f = mo.Faces[faceIndex];

            var kill = new HashSet<int>(neighbors);
            kill.Add(faceIndex);

            var used = new HashSet<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                if (kill.Contains(fi)) continue;
                foreach (int v in mo.Faces[fi].VertexIndices) used.Add(v);
            }
            // 選択面は外側の3頂点で張り替えられる。
            foreach (int v in outer) used.Add(v);

            var orphans = new List<int>();
            foreach (int v in f.VertexIndices)
                if (!used.Contains(v) && !orphans.Contains(v)) orphans.Add(v);

            return orphans;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 選択面とそれを囲む三角形3枚を、外側3頂点の三角形1枚に統合する。成功したら true。
        /// </summary>
        public static bool Execute(
            MeshObject mo, int faceIndex,
            out int removedFaceCount, out int removedVertexCount, out string reason)
        {
            removedFaceCount   = 0;
            removedVertexCount = 0;

            if (mo == null || faceIndex < 0 || faceIndex >= mo.Faces.Count)
            {
                reason = "面が指定されていません";
                return false;
            }

            if (!BuildPatch(mo, faceIndex, out var neighbors, out var outer,
                            out var outerUV, out var outerNormal, out reason))
                return false;

            var orphans = CountOrphans(mo, faceIndex, neighbors, outer);

            // 選択面を外側3頂点で上書きする。面ID / MaterialIndex / Flags は残る。
            var target = mo.Faces[faceIndex];
            target.VertexIndices = new List<int>(outer);
            target.UVIndices     = new List<int>(outerUV);
            target.NormalIndices = new List<int>(outerNormal);

            // 囲む3面を降順で削除する（target は Face オブジェクトなので影響を受けない）。
            var removeList = new List<int>(neighbors);
            removeList.Sort();
            for (int k = removeList.Count - 1; k >= 0; k--)
                mo.Faces.RemoveAt(removeList[k]);

            removedFaceCount = removeList.Count;

            // どの面からも参照されなくなった頂点だけを削除し、インデックスを詰める。
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

                removedVertexCount = orphans.Count;
                mo.InvalidatePositionCache();
            }

            reason = null;
            return true;
        }

        // ================================================================
        // 一括処理（複数面）
        // ================================================================

        /// <summary>一括処理の下調べ結果。パネル表示とボタン活性に使う。</summary>
        public struct MergeBatchInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象の面数。</summary>
            public int TargetCount;
            /// <summary>条件不一致・干渉で除外した面数。</summary>
            public int SkippedCount;
            /// <summary>消える面の合計。</summary>
            public int RemovedFaceTotal;
            /// <summary>消える頂点の合計。</summary>
            public int RemovedVertexTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない選択面だけを選び出す。
        ///
        /// 除外の規則:
        ///   1. 単独で Inspect が通らない面。
        ///   2. 4枚組（選択面＋囲む3面）が1枚でも重なる選択面どうし（その組は全部除外）。
        ///      重なったまま両方を統合すると、片方の張り替えがもう片方の削除予定面を
        ///      参照してしまうため。
        /// </summary>
        public static List<int> SelectIndependent(
            MeshObject mo, IEnumerable<int> faceIndices, out List<int> skipped)
        {
            var accepted = new List<int>();
            skipped      = new List<int>();

            if (mo == null || faceIndices == null) return accepted;

            // 重複を落としつつ、単独で実行できる面だけを候補にする。
            var candidates = new List<int>();
            var seen       = new HashSet<int>();
            var patchOf    = new Dictionary<int, List<int>>();

            foreach (int fi in faceIndices)
            {
                if (fi < 0 || fi >= mo.Faces.Count) continue;
                if (!seen.Add(fi)) continue;

                if (!BuildPatch(mo, fi, out var neighbors, out _, out _, out _, out _))
                {
                    skipped.Add(fi);
                    continue;
                }

                var patch = new List<int>(neighbors) { fi };
                patchOf[fi] = patch;
                candidates.Add(fi);
            }

            // 4枚組が重なる候補を落とす。
            var owners = new Dictionary<int, List<int>>();
            foreach (int fi in candidates)
            {
                foreach (int pf in patchOf[fi])
                {
                    if (!owners.TryGetValue(pf, out var list))
                    {
                        list = new List<int>();
                        owners[pf] = list;
                    }
                    list.Add(fi);
                }
            }

            var conflicted = new HashSet<int>();
            foreach (var kv in owners)
            {
                if (kv.Value.Count < 2) continue;
                foreach (int fi in kv.Value) conflicted.Add(fi);
            }

            foreach (int fi in candidates)
            {
                if (conflicted.Contains(fi)) skipped.Add(fi);
                else                         accepted.Add(fi);
            }

            accepted.Sort();
            skipped.Sort();
            return accepted;
        }

        /// <summary>複数面ぶんの下調べ。メッシュは変更しない。</summary>
        public static MergeBatchInfo InspectMany(MeshObject mo, IEnumerable<int> faceIndices)
        {
            var info = new MergeBatchInfo();

            if (mo == null)
            {
                info.Reason = "メッシュがありません";
                return info;
            }

            var targets = SelectIndependent(mo, faceIndices, out var skipped);

            info.TargetCount  = targets.Count;
            info.SkippedCount = skipped.Count;

            foreach (int fi in targets)
            {
                var one = Inspect(mo, fi);
                info.RemovedFaceTotal   += one.RemovedFaceCount;
                info.RemovedVertexTotal += one.OrphanVertexCount;
            }

            if (targets.Count == 0)
            {
                info.Reason = skipped.Count > 0
                    ? "選択面が条件を満たさないか、互いに干渉しています"
                    : "面を選択してください";
                return info;
            }

            info.CanExecute = true;
            return info;
        }

        /// <summary>
        /// 複数の選択面を統合する。互いに干渉する面は処理しない。
        /// 1つでも処理できたら true。
        ///
        /// 面を削除すると後続の面インデックスがずれるため、対象は Face オブジェクトの
        /// 参照で保持し、実行直前に現在のインデックスへ引き直す。
        /// </summary>
        public static bool ExecuteMany(
            MeshObject mo, IEnumerable<int> faceIndices,
            out int mergedCount, out int removedFaceCount, out int removedVertexCount,
            out int skippedCount, out string reason)
        {
            mergedCount        = 0;
            removedFaceCount   = 0;
            removedVertexCount = 0;
            skippedCount       = 0;
            reason             = null;

            if (mo == null) { reason = "メッシュがありません"; return false; }

            var targets = SelectIndependent(mo, faceIndices, out var skipped);
            skippedCount = skipped.Count;

            if (targets.Count == 0)
            {
                reason = skipped.Count > 0
                    ? "選択面が条件を満たさないか、互いに干渉しています"
                    : "面を選択してください";
                return false;
            }

            // インデックスがずれる前に Face オブジェクトを押さえる。
            var faceRefs = new List<Face>();
            foreach (int fi in targets) faceRefs.Add(mo.Faces[fi]);

            foreach (var fref in faceRefs)
            {
                int cur = mo.Faces.IndexOf(fref);
                if (cur < 0)
                {
                    skippedCount++;
                    continue;
                }

                bool ok = Execute(mo, cur, out int removedFaces, out int removedVerts, out string why);
                if (!ok)
                {
                    skippedCount++;
                    reason = why;
                    continue;
                }

                mergedCount        ++;
                removedFaceCount   += removedFaces;
                removedVertexCount += removedVerts;
            }

            if (mergedCount == 0) return false;

            reason = null;
            return true;
        }
    }
}
