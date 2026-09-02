// HoleRingCountOps.cs
// 穴（エッジ＝1面だけが使う辺のループ）を構成する頂点数を、指定した数へ合わせる位相計算。
// ブリッジ（BridgeLoopOps）の「2つの穴の頂点数が同じ」制約を外すための前処理。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【方針】
//   足りないとき（現在 < 目標）: 穴の辺のうち最長のものに中点を打つ。これを差分だけ繰り返す。
//     ・辺の長さは 1 手ごとに測り直す。長い辺が 2 回以上割られるのが正しい挙動のため。
//     ・中点を入れると、その辺を使う唯一の面が n+1 角形になる。その面を新頂点から
//       扇状に割り直し、出来る面をすべて四角形か三角形にする（下の ChooseFanCuts）。
//   多いとき（現在 > 目標）: 穴の辺のうち最短のものを潰して 2 頂点を中点へ結合する。
//     ・四角形以上の面に接する辺を優先する。三角形に接する辺を潰すとその面が消え、
//       消えた面が使っていた内部辺が境界化して穴の縁がとげ状に崩れるため。
//     ・優先候補が無いときは全体の最短辺を潰す（面が 1 枚消える）。
//
// 【他ツールとの分担】
//   ・穴のループ化は BridgeLoopOps.OrderBoundaryLoop に委譲する（ブリッジと同じ数え方に揃える）。
//   ・頂点の結合は MeshMergeHelper.MergeVerticesToCentroid に委譲する（面のリマップ・
//     縮退面の削除・頂点の詰め直しまで含む）。ここでは辺の選定と添字の追従だけを持つ。
//
// 【新頂点の属性】スキンドメッシュに追加する頂点には BoneWeight が必須。
//   欠けていると GPU 側がメッシュ自身の context 索引を使い、頂点が別の場所へ飛ぶ。
//   KnifeTool（NCutExecutor.CreateCutVertexAt）と同じく両端から補間して与える。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class HoleRingCountOps
    {
        // ================================================================
        // 設定
        // ================================================================

        /// <summary>分割のしかたの選択。</summary>
        public struct Options
        {
            /// <summary>
            /// 三角形の面へ中点を入れたとき、四角形のまま残さず 2 枚の三角形へ割る。
            /// false なら四角形のまま残す（面数が増えない）。
            /// </summary>
            public bool SplitTriangleIntoTriangles;

            public static Options Default => new Options { SplitTriangleIntoTriangles = true };
        }

        // ================================================================
        // 結果
        // ================================================================

        /// <summary>実行の結果。</summary>
        public class Result
        {
            /// <summary>1 手でも進めたか。</summary>
            public bool Ok;
            /// <summary>目標の頂点数へ到達したか。</summary>
            public bool Reached;
            /// <summary>説明（中断したときはその理由）。</summary>
            public string Message;

            /// <summary>実行前の頂点数。</summary>
            public int StartCount;
            /// <summary>実行後の頂点数。</summary>
            public int FinalCount;
            /// <summary>目標の頂点数。</summary>
            public int DesiredCount;

            /// <summary>割った回数。</summary>
            public int SplitCount;
            /// <summary>潰した回数。</summary>
            public int MergeCount;
            /// <summary>増えた頂点数。</summary>
            public int AddedVertexCount;
            /// <summary>減った頂点数。</summary>
            public int RemovedVertexCount;
            /// <summary>増えた面数。</summary>
            public int AddedFaceCount;
            /// <summary>消えた面数。</summary>
            public int RemovedFaceCount;

            /// <summary>実行後の種頂点。添字が詰め直されるため呼出し側はこれで更新する。</summary>
            public int SeedVertex = -1;
            /// <summary>実行後の進行方向ヒント頂点。失われたときは -1。</summary>
            public int SeedDirectionHint = -1;
        }

        // ================================================================
        // 実行
        //
        // 【頂点数を数えるだけのとき】ここには入口を置かない。
        // BridgeLoopOps.OrderBoundaryLoop(...).Count を直接使う（ブリッジと同じ数え方）。
        // ================================================================

        /// <summary>
        /// 種頂点が属する穴の頂点数を desiredCount に合わせる。
        ///
        /// 途中で進めなくなったときは、そこまでの変更を残したまま Reached=false で返す。
        /// 呼出し側は Undo を記録済みである前提（部分適用は Undo 1 手で戻せる）。
        /// </summary>
        public static Result Execute(
            MeshObject mo, int seedVertex, int directionHint, int desiredCount, Options opt)
        {
            var r = new Result
            {
                SeedVertex        = seedVertex,
                SeedDirectionHint = directionHint,
                DesiredCount      = desiredCount,
            };

            if (mo == null) { r.Message = "メッシュがありません"; return r; }
            if (desiredCount < 3) { r.Message = "目標の頂点数が足りません"; return r; }

            var loop = BridgeLoopOps.OrderBoundaryLoop(mo, seedVertex, directionHint, out string msg);
            if (loop.Count < 3) { r.Message = msg; return r; }

            r.StartCount = loop.Count;
            r.FinalCount = loop.Count;

            if (loop.Count == desiredCount)
            {
                r.Ok      = true;
                r.Reached = true;
                r.Message = "頂点数は既に一致しています";
                return r;
            }

            // ── 足りない: 最長の辺を割る ────────────────────────────────
            if (loop.Count < desiredCount)
            {
                while (loop.Count < desiredCount)
                {
                    if (!SplitLongestEdge(mo, loop, opt, r, out string why))
                    {
                        r.Message = why;
                        break;
                    }
                }
            }
            // ── 多い: 最短の辺を潰す ───────────────────────────────────
            else
            {
                int guard = r.StartCount - desiredCount + 8;   // 数が減らない事故での無限ループ避け
                while (true)
                {
                    // 潰すたびに位相が変わりうるのでループを引き直す。
                    loop = BridgeLoopOps.OrderBoundaryLoop(mo, r.SeedVertex, r.SeedDirectionHint, out msg);
                    if (loop.Count < 3) { r.Message = msg; break; }
                    if (loop.Count <= desiredCount) break;

                    if (--guard < 0) { r.Message = "頂点数が減らないため中断しました"; break; }

                    if (!MergeShortestEdge(mo, loop, r, out string why))
                    {
                        r.Message = why;
                        break;
                    }
                }
            }

            mo.InvalidatePositionCache();

            var finalLoop = BridgeLoopOps.OrderBoundaryLoop(mo, r.SeedVertex, r.SeedDirectionHint, out _);
            r.FinalCount = finalLoop.Count;

            r.Ok      = (r.SplitCount + r.MergeCount) > 0;
            r.Reached = r.FinalCount == desiredCount;

            if (r.Reached)
                r.Message = r.SplitCount > 0
                    ? $"{r.SplitCount} 箇所を割りました（頂点 +{r.AddedVertexCount} / 面 +{r.AddedFaceCount}）"
                    : $"{r.MergeCount} 箇所を潰しました（頂点 -{r.RemovedVertexCount} / 面 -{r.RemovedFaceCount}）";
            else if (string.IsNullOrEmpty(r.Message))
                r.Message = $"{r.StartCount} → {r.FinalCount}（目標 {desiredCount}）で止まりました";
            else
                r.Message = $"{r.StartCount} → {r.FinalCount}（目標 {desiredCount}）で中断: {r.Message}";

            return r;
        }

        // ================================================================
        // 分割（最長の辺に中点を打つ）
        // ================================================================

        /// <summary>
        /// ループ中の最長の辺に中点を打ち、その辺を使う面を割り直す。
        /// loop は中点を挿入した状態へ書き換える（既存頂点の添字は動かない）。
        /// </summary>
        private static bool SplitLongestEdge(
            MeshObject mo, List<int> loop, Options opt, Result r, out string reason)
        {
            int n = loop.Count;

            int   bestI   = -1;
            float bestLen = -1f;
            for (int i = 0; i < n; i++)
            {
                int a = loop[i];
                int b = loop[(i + 1) % n];
                float len = (mo.Vertices[a].Position - mo.Vertices[b].Position).sqrMagnitude;
                if (len > bestLen) { bestLen = len; bestI = i; }
            }

            if (bestI < 0) { reason = "割れる辺がありません"; return false; }

            int va = loop[bestI];
            int vb = loop[(bestI + 1) % n];

            if (!TryFindBoundaryFace(mo, va, vb, out int faceIndex, out int corner))
            {
                reason = $"辺 ({va}, {vb}) を使う面が 1 枚ではありません";
                return false;
            }

            int mid = CreateMidVertex(mo, va, vb);
            SplitFaceAtNewEdgeVertex(mo, faceIndex, corner, mid, opt, r);

            loop.Insert(bestI + 1, mid);
            r.SplitCount++;
            r.AddedVertexCount++;

            reason = null;
            return true;
        }

        /// <summary>
        /// 辺 {v0,v1} を使う面がちょうど 1 枚のとき、その面と、面の中で
        /// 「v0→v1 または v1→v0 の並びが始まる隅」を返す。
        /// </summary>
        private static bool TryFindBoundaryFace(
            MeshObject mo, int v0, int v1, out int faceIndex, out int corner)
        {
            faceIndex = -1;
            corner    = -1;

            int hits = 0;
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                int n = f.VertexIndices.Count;
                if (n < 3) continue;   // 線分は辺を持たない

                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i];
                    int b = f.VertexIndices[(i + 1) % n];
                    if ((a == v0 && b == v1) || (a == v1 && b == v0))
                    {
                        hits++;
                        if (hits > 1) return false;
                        faceIndex = fi;
                        corner    = i;
                    }
                }
            }

            return hits == 1;
        }

        /// <summary>
        /// 面 faceIndex の隅 corner とその次の隅の間に新頂点 mid を差し込み、
        /// 出来る面がすべて四角形か三角形になるよう mid から扇状に割り直す。
        ///
        /// 【UV / 法線】元の面の隅のスロット番号をそのまま引き継ぐ。新頂点は 0。
        /// Face.UVIndices[j] == Face.NormalIndices[j] の不変条件を壊さない。
        /// 【面ID】先頭の面が元の面の ID を引き継ぐ。残りは新規採番。
        /// </summary>
        private static void SplitFaceAtNewEdgeVertex(
            MeshObject mo, int faceIndex, int corner, int mid, Options opt, Result r)
        {
            var orig = mo.Faces[faceIndex];
            int n    = orig.VertexIndices.Count;

            // mid を頂点とする扇の「外周」。mid の直後の隅から一周して mid の直前の隅まで。
            //   ring[0]       = corner+1 の隅（差し込んだ辺の反対端）
            //   ring[n-1]     = corner   の隅（差し込んだ辺のこちら端）
            var ring   = new List<int>(n);
            var ringUV = new List<int>(n);
            var ringNm = new List<int>(n);
            for (int k = 0; k < n; k++)
            {
                int idx = (corner + 1 + k) % n;
                ring.Add(orig.VertexIndices[idx]);
                ringUV.Add(idx < orig.UVIndices.Count     ? orig.UVIndices[idx]     : 0);
                ringNm.Add(idx < orig.NormalIndices.Count ? orig.NormalIndices[idx] : 0);
            }

            // 三角形へ中点を入れると四角形になる。設定に従って 2 枚の三角形へ割るかを決める。
            bool unitStepsOnly = (n == 3) && opt.SplitTriangleIntoTriangles;

            var next = ChooseFanCuts(mo, mid, ring, unitStepsOnly);

            var subs = new List<Face>();
            int cur  = 0;
            while (cur < ring.Count - 1)
            {
                int nx = next[cur];
                if (nx <= cur) break;   // 念のため（ChooseFanCuts は必ず前進する）

                var vi = new List<int> { mid };
                var uv = new List<int> { 0 };
                var nm = new List<int> { 0 };
                for (int k = cur; k <= nx; k++)
                {
                    vi.Add(ring[k]);
                    uv.Add(ringUV[k]);
                    nm.Add(ringNm[k]);
                }

                subs.Add(new Face
                {
                    VertexIndices = vi,
                    UVIndices     = uv,
                    NormalIndices = nm,
                    MaterialIndex = orig.MaterialIndex,
                    Flags         = orig.Flags,
                });

                cur = nx;
            }

            if (subs.Count == 0) return;

            subs[0].Id = orig.Id;               // 面IDを引き継ぐ（トポロジー追跡・モーフ用）
            mo.Faces[faceIndex] = subs[0];
            for (int k = 1; k < subs.Count; k++) mo.AddFace(subs[k]);

            r.AddedFaceCount += subs.Count - 1;
        }

        /// <summary>
        /// 扇の区切りを決める。ring の添字 0 から ring.Count-1 まで、
        /// 1 歩（三角形）または 2 歩（四角形）で進む経路のうち、
        /// 対角線（新頂点から区切り点への線分）の長さの合計が最小のものを選ぶ。
        ///
        /// 戻り値 next[i] は「i の次の区切り」。next[ring.Count-1] は使わない。
        /// unitStepsOnly が true のときは 1 歩だけ（＝すべて三角形）。
        /// </summary>
        private static int[] ChooseFanCuts(
            MeshObject mo, int apex, List<int> ring, bool unitStepsOnly)
        {
            int L    = ring.Count;
            var next = new int[L];
            for (int i = 0; i < L; i++) next[i] = i + 1;

            if (unitStepsOnly || L <= 2) return next;

            // best[i] = i から L-1 へ進むときの対角線長の合計の最小値。
            var best = new float[L];
            for (int i = 0; i < L; i++) best[i] = float.MaxValue;
            best[L - 1] = 0f;
            next[L - 1] = L - 1;

            Vector3 pa = mo.Vertices[apex].Position;

            for (int i = L - 2; i >= 0; i--)
            {
                for (int step = 1; step <= 2; step++)
                {
                    int j = i + step;
                    if (j > L - 1) break;
                    if (best[j] == float.MaxValue) continue;

                    // 終点 L-1 は面の隅であって対角線の足ではない。
                    float add = (j == L - 1)
                        ? 0f
                        : (mo.Vertices[ring[j]].Position - pa).magnitude;

                    float cost = best[j] + add;
                    if (cost < best[i]) { best[i] = cost; next[i] = j; }
                }
            }

            return next;
        }

        /// <summary>
        /// 辺の中点に新頂点を作る。UV / 法線 / ボーンウェイトは両端から補間する。
        /// </summary>
        private static int CreateMidVertex(MeshObject mo, int va, int vb)
        {
            var a = mo.Vertices[va];
            var b = mo.Vertices[vb];

            var v = new Vertex(Vector3.Lerp(a.Position, b.Position, 0.5f));

            // スキンドメッシュではウェイト無しの頂点が別の場所へ飛ぶ（冒頭の注記）。
            v.BoneWeight       = Poly_Ling.UI.SkinWeightOps.LerpNullable(a.BoneWeight,       b.BoneWeight,       0.5f);
            v.MirrorBoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(a.MirrorBoneWeight, b.MirrorBoneWeight, 0.5f);

            if (a.UVs.Count > 0 && b.UVs.Count > 0)
                v.UVs.Add(Vector2.Lerp(a.UVs[0], b.UVs[0], 0.5f));
            if (a.Normals.Count > 0 && b.Normals.Count > 0)
                v.Normals.Add(Vector3.Lerp(a.Normals[0], b.Normals[0], 0.5f).normalized);

            // 部品IDは両端が一致するときだけ引き継ぐ。食い違うなら 0（未設定）のまま。
            if (a.PartsId == b.PartsId) v.PartsId = a.PartsId;

            // 両端が乗っているフラグだけ引き継ぐ。中点も同じ性質になる。
            if (a.HasFlag(VertexFlags.OnMirrorPlane) && b.HasFlag(VertexFlags.OnMirrorPlane))
                v.SetFlag(VertexFlags.OnMirrorPlane);
            if (a.HasFlag(VertexFlags.MirrorGenerated) && b.HasFlag(VertexFlags.MirrorGenerated))
                v.SetFlag(VertexFlags.MirrorGenerated);

            return mo.AddVertex(v);
        }

        // ================================================================
        // 結合（最短の辺を潰す）
        // ================================================================

        /// <summary>
        /// ループ中の辺を 1 本潰して 2 頂点を中点へ結合する。
        /// 四角形以上の面に接する辺を優先し、その中で最短のものを選ぶ。
        /// 優先候補が無いときは全体の最短辺を潰す（接していた三角形が 1 枚消える）。
        /// </summary>
        private static bool MergeShortestEdge(
            MeshObject mo, List<int> loop, Result r, out string reason)
        {
            int n = loop.Count;

            int   bestI         = -1;
            float bestLen       = float.MaxValue;
            bool  bestPreferred = false;

            for (int i = 0; i < n; i++)
            {
                int a = loop[i];
                int b = loop[(i + 1) % n];

                if (!TryFindBoundaryFace(mo, a, b, out int fi, out _)) continue;

                bool  preferred = mo.Faces[fi].VertexIndices.Count >= 4;
                float len       = (mo.Vertices[a].Position - mo.Vertices[b].Position).sqrMagnitude;

                if (bestI >= 0)
                {
                    if (bestPreferred && !preferred) continue;                 // 優先候補が既にある
                    if (preferred == bestPreferred && len >= bestLen) continue; // 同格なら短い方
                }

                bestI         = i;
                bestLen       = len;
                bestPreferred = preferred;
            }

            if (bestI < 0) { reason = "潰せる辺がありません"; return false; }

            int va = loop[bestI];
            int vb = loop[(bestI + 1) % n];
            int drop = Mathf.Max(va, vb);   // MergeVerticesToCentroid は小さい方を残す

            int facesBefore = mo.FaceCount;

            int merged = MeshMergeHelper.MergeVerticesToCentroid(
                mo, new HashSet<int> { va, vb });

            if (merged < 0) { reason = $"頂点 {va}, {vb} の結合に失敗しました"; return false; }

            r.MergeCount++;
            r.RemovedVertexCount++;
            r.RemovedFaceCount += facesBefore - mo.FaceCount;

            // drop が消え、drop より大きい添字が 1 つずつ繰り下がる。
            r.SeedVertex        = RemapAfterDrop(r.SeedVertex,        drop, merged);
            r.SeedDirectionHint = RemapAfterDrop(r.SeedDirectionHint, drop, merged);

            reason = null;
            return true;
        }

        /// <summary>頂点 1 つが消えた後の添字を求める。</summary>
        private static int RemapAfterDrop(int index, int drop, int merged)
        {
            if (index < 0)     return -1;
            if (index == drop) return merged;
            return index > drop ? index - 1 : index;
        }
    }
}
