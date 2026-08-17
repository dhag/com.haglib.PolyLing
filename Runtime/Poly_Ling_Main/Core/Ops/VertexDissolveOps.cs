// VertexDissolveOps.cs
// 選択した1頂点（頂点A）を消して、A を囲む面を1枚の N 角形に張り替える。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. A を含む面を集める。各面は A を1回だけ参照し、3頂点以上であること。
//   2. 面 F の A の位置を p とすると、F は「始端 = V[p+1]」「終端 = V[p-1]」を持つ。
//      巻き方向が揃っていれば「前の面の終端 = 次の面の始端」で数珠つなぎになる。
//      これを辿って A の周りが閉じたファンかを判定する（境界頂点は不可）。
//   3. 各面から V[p+1] … V[p+n-2] の n-2 個の隅を順に取り出して連結すると、
//      A を含まない外周リングになる（四角形4枚なら 2×4 = 8 頂点）。
//   4. ファンの先頭面（面インデックス最小）をリングで上書きし、残りの面を削除する。
//      面 ID / MaterialIndex / Flags は先頭面のものが残る。
//   5. 参照されなくなった A を削除し、残りの面の頂点インデックスを詰め直す。
//
// 【不変条件（厳守）】
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   新頂点も新スロットも作らず、元の面の UV/法線スロット番号をそのまま引き継ぐ。
//   隣り合う面の継ぎ目にあたる隅（前の面の終端 = 次の面の始端）は、リングから
//   落ちる側ではなく残る側のスロットが採用される。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class VertexDissolveOps
    {
        /// <summary>実行可否と対象の規模。パネルの表示・ボタン活性に使う。</summary>
        public struct DissolveInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>頂点A を含む面の数（= 1枚に統合される面の数）。</summary>
            public int FaceCount;
            /// <summary>統合後の面の頂点数。</summary>
            public int RingCount;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        // ================================================================
        // 事前調査
        // ================================================================

        /// <summary>頂点A の周辺を調べ、実行可否を返す。メッシュは変更しない。</summary>
        public static DissolveInfo Inspect(MeshObject mo, int apex)
        {
            var info = new DissolveInfo();

            if (mo == null || apex < 0 || apex >= mo.Vertices.Count)
            {
                info.Reason = "頂点が指定されていません";
                return info;
            }

            if (!BuildFan(mo, apex, out var order, out var ring, out _, out _, out string reason))
            {
                info.Reason = reason;
                return info;
            }

            info.FaceCount  = order.Count;
            info.RingCount  = ring.Count;
            info.CanExecute = true;
            return info;
        }

        // ================================================================
        // ファン構築（Inspect と Execute の共通処理）
        // ================================================================

        /// <summary>
        /// 頂点A を囲む面を巻き方向どおりに並べ、外周リングと UV/法線スロットを作る。
        /// メッシュは変更しない。閉じたファンでなければ false。
        /// </summary>
        private static bool BuildFan(
            MeshObject mo, int apex,
            out List<int> order, out List<int> ring,
            out List<int> ringUV, out List<int> ringNormal,
            out string reason)
        {
            order      = new List<int>();
            ring       = new List<int>();
            ringUV     = new List<int>();
            ringNormal = new List<int>();

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
                if (n < 3)
                {
                    reason = "指定頂点が線分に使われています";
                    return false;
                }

                fanFaces.Add(fi);
            }

            if (fanFaces.Count == 0)
            {
                reason = "指定頂点はどの面にも使われていません";
                return false;
            }
            if (fanFaces.Count < 2)
            {
                reason = "指定頂点を囲む面が足りません";
                return false;
            }

            // --- 始端（A の次の頂点）から面を引ける表を作る ---
            var startToFace = new Dictionary<int, int>();
            foreach (int fi in fanFaces)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;
                int p = f.VertexIndices.IndexOf(apex);
                int start = f.VertexIndices[(p + 1) % n];

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
                int n = f.VertexIndices.Count;
                int p = f.VertexIndices.IndexOf(apex);
                int end = f.VertexIndices[(p - 1 + n) % n];

                if (!startToFace.TryGetValue(end, out int next))
                {
                    reason = "指定頂点の周りが閉じていません（境界の頂点です）";
                    return false;
                }
                if (next == firstFace) break;

                cur = next;
            }

            if (order.Count != fanFaces.Count)
            {
                reason = "指定頂点の周りに独立した面のかたまりがあります";
                return false;
            }

            // --- 外周リングを作る（各面から n-2 個の隅を取り出して連結） ---
            foreach (int fi in order)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;
                int p = f.VertexIndices.IndexOf(apex);

                for (int k = 1; k <= n - 2; k++)
                {
                    int c = (p + k) % n;
                    ring.Add(f.VertexIndices[c]);
                    ringUV.Add(c < f.UVIndices.Count ? f.UVIndices[c] : 0);
                    ringNormal.Add(c < f.NormalIndices.Count ? f.NormalIndices[c] : 0);
                }
            }

            if (ring.Count < 3)
            {
                reason = "統合すると面にならないため実行できません";
                return false;
            }

            var seen = new HashSet<int>();
            foreach (int v in ring)
            {
                if (!seen.Add(v))
                {
                    reason = "統合後の面が同じ頂点を2回使うため実行できません";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 頂点A を消し、A を囲む面を1枚の N 角形に統合する。成功したら true。
        /// </summary>
        public static bool Execute(
            MeshObject mo, int apex,
            out int removedFaceCount, out int ringCount, out string reason)
        {
            removedFaceCount = 0;
            ringCount        = 0;

            if (mo == null || apex < 0 || apex >= mo.Vertices.Count)
            {
                reason = "頂点が指定されていません";
                return false;
            }

            if (!BuildFan(mo, apex, out var order, out var ring,
                          out var ringUV, out var ringNormal, out reason))
                return false;

            // 先頭面（面インデックス最小）をリングで上書きする。
            // 面 ID / MaterialIndex / Flags はこの面のものが残る。
            int baseFaceIndex = order[0];
            var baseFace = mo.Faces[baseFaceIndex];
            baseFace.VertexIndices = ring;
            baseFace.UVIndices     = ringUV;
            baseFace.NormalIndices = ringNormal;

            // 残りの面を降順で削除する。baseFaceIndex は最小なのでずれない。
            var removeList = new List<int>();
            foreach (int fi in order)
                if (fi != baseFaceIndex) removeList.Add(fi);
            removeList.Sort();

            for (int k = removeList.Count - 1; k >= 0; k--)
                mo.Faces.RemoveAt(removeList[k]);

            removedFaceCount = removeList.Count;
            ringCount        = ring.Count;

            // ここで A はどの面からも参照されていないはず。
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                if (mo.Faces[fi].VertexIndices.IndexOf(apex) < 0) continue;
                reason = "内部エラー: 指定頂点の参照が残りました";
                Debug.LogError($"[VertexDissolveOps] 面 {fi} に頂点 {apex} の参照が残っています");
                return false;
            }

            // A を削除し、残存面の頂点インデックスを詰める。
            foreach (var f in mo.Faces)
            {
                var vidx = f.VertexIndices;
                for (int j = 0; j < vidx.Count; j++)
                    if (vidx[j] > apex) vidx[j] = vidx[j] - 1;
            }
            mo.Vertices.RemoveAt(apex);
            mo.InvalidatePositionCache();

            reason = null;
            return true;
        }

        // ================================================================
        // 一括処理（複数頂点）
        // ================================================================

        /// <summary>一括処理の下調べ結果。パネル表示とボタン活性に使う。</summary>
        public struct DissolveBatchInfo
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象の頂点数。</summary>
            public int TargetCount;
            /// <summary>干渉・単独不可で除外した頂点数。</summary>
            public int SkippedCount;
            /// <summary>統合される面の合計。</summary>
            public int FaceTotal;
            /// <summary>作られる面の頂点数の合計。</summary>
            public int RingTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 互いに干渉しない頂点だけを選び出す。
        ///
        /// 除外の規則:
        ///   1. 単独で Inspect が通らない頂点。
        ///   2. 同じ面を共有する選択頂点どうし（その組は全部除外）。
        ///      隣り合う頂点は必ず同じ面を共有するのでこれに含まれる。
        ///      面を共有したまま両方を溶かすと、片方の張り替えが
        ///      もう片方の削除予定頂点を参照してしまうため。
        /// </summary>
        public static List<int> SelectIndependent(
            MeshObject mo, IEnumerable<int> apexes, out List<int> skipped)
        {
            var accepted = new List<int>();
            skipped      = new List<int>();

            if (mo == null || apexes == null) return accepted;

            // 重複を落としつつ、単独で実行できる頂点だけを候補にする。
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

            // 同じ面に2つ以上乗っている候補を落とす。
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
        public static DissolveBatchInfo InspectMany(MeshObject mo, IEnumerable<int> apexes)
        {
            var info = new DissolveBatchInfo();

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
                info.FaceTotal += one.FaceCount;
                info.RingTotal += one.RingCount;
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
        /// 複数頂点を溶かす。互いに干渉する頂点は処理しない。
        /// 1つでも処理できたら true。
        ///
        /// 実行順は「インデックスの降順」。Execute は末尾で apex を削除して
        /// それより大きいインデックスを詰めるので、降順なら残りの対象がずれない。
        /// </summary>
        public static bool ExecuteMany(
            MeshObject mo, IEnumerable<int> apexes,
            out int dissolvedVertexCount, out int removedFaceCount, out int skippedCount,
            out string reason)
        {
            dissolvedVertexCount = 0;
            removedFaceCount     = 0;
            skippedCount         = 0;
            reason               = null;

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

            int okCount = 0;

            for (int k = targets.Count - 1; k >= 0; k--)
            {
                bool ok = Execute(mo, targets[k], out int removed, out _, out string why);
                if (!ok)
                {
                    skippedCount++;
                    reason = why;
                    continue;
                }

                removedFaceCount += removed;
                okCount++;
            }

            if (okCount == 0) return false;

            dissolvedVertexCount = okCount;
            reason               = null;
            return true;
        }
    }
}
