// PipeSmoothOps.cs
// パイプ列に沿った重み付き平均スムージング。MeshObject 1 個ぶんを処理する。
// Runtime/Poly_Ling_Main/Tools/VertexTools/PipeAlignTool_/ に配置
//
// 【やること】
//   パーツID（Vertex.PartsId）の昇順に並べたパイプ列 p = 0..N-1 について、
//   パイプ p の j 番目の頂点を、近傍パイプの同じ j 番目の頂点の重み付き平均で置き換える。
//   対称化ではないので X の反転はしない。1 段の頂点数 M と先端フラグも使わない。
//
// 【前提】窓に入るパーツ同士の頂点数が同じで、頂点の並び順が対応していること。
//   一致しなければそのメッシュは 1 頂点も書き換えない。
//
// 【重み】個数は奇数。中央が自分自身。例「1,2,4,2,1」なら
//   p-2, p-1, p, p+1, p+2 を 1:2:4:2:1 で混ぜる。
//
// 【端の扱い】窓がパイプ列の外へ出るとき
//   ・Skip    … そのパイプは飛ばす（スムージングしない）
//   ・Partial … 範囲内の重みだけを使い、その総和で正規化する
//   「端」はパイプ列全体の端を指す。対象を絞っても、窓の入力には対象外のパーツも使う。
//
// 【読み書きの分離】書き込みの前に全頂点位置を控え、平均の入力は必ず控えた側から読む。
//   そのため結果が処理順に依存しない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>窓がパイプ列の外へ出るときの扱い。</summary>
    public enum PipeSmoothEdgeMode
    {
        /// <summary>端のパイプはスムージングしない。</summary>
        Skip = 0,

        /// <summary>範囲内の重みだけで平均する。</summary>
        Partial = 1,
    }

    /// <summary>パイプ列に沿った重み付き平均スムージング。</summary>
    public static class PipeSmoothOps
    {
        // ================================================================
        // 入力の読み取り
        // ================================================================

        /// <summary>
        /// 重みの並びを読む。区切りはカンマまたは空白。改行も区切りとして扱う。
        /// 個数は奇数、値は 0 以上、総和は 0 より大きいこと。
        /// </summary>
        public static bool ParseWeights(string text, out List<float> weights, out string reason)
        {
            weights = new List<float>();
            reason  = null;

            if (string.IsNullOrEmpty(text))
            {
                reason = "重みが指定されていません";
                return false;
            }

            var tokens = text.Split(new[] { ',', ' ', '\t', '\r', '\n', '、' },
                                    System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var t in tokens)
            {
                if (!float.TryParse(t.Trim(), out float w))
                {
                    reason = $"重み「{t}」を数値として読めません";
                    return false;
                }
                if (w < 0f)
                {
                    reason = $"重み「{t}」が負です";
                    return false;
                }
                weights.Add(w);
            }

            if (weights.Count == 0)
            {
                reason = "重みが指定されていません";
                return false;
            }

            if ((weights.Count & 1) == 0)
            {
                reason = $"重みの個数は奇数にしてください（今は {weights.Count} 個）";
                return false;
            }

            float sum = 0f;
            foreach (float w in weights) sum += w;
            if (sum <= 0f)
            {
                reason = "重みの合計が 0 です";
                return false;
            }

            return true;
        }

        /// <summary>
        /// スムージング対象のパーツIDを読む。「5,6,7」「5-7」の併記を受ける。
        /// 空欄なら全パーツが対象（ids が null になる）。
        /// </summary>
        public static bool ParseTargets(string text, out HashSet<int> ids, out string reason)
        {
            ids    = null;
            reason = null;

            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return true;

            var set = new HashSet<int>();
            var tokens = text.Split(new[] { ',', ' ', '\t', '\r', '\n', '、' },
                                    System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                string t = raw.Trim();
                int dash = t.IndexOf('-', 1);   // 先頭の '-' は負値の符号として残す

                if (dash > 0)
                {
                    string a = t.Substring(0, dash).Trim();
                    string b = t.Substring(dash + 1).Trim();
                    if (!int.TryParse(a, out int from) || !int.TryParse(b, out int to))
                    {
                        reason = $"対象「{t}」を範囲として読めません";
                        return false;
                    }
                    if (to < from)
                    {
                        reason = $"対象「{t}」の範囲が逆です";
                        return false;
                    }
                    for (int i = from; i <= to; i++) set.Add(i);
                }
                else
                {
                    if (!int.TryParse(t, out int id))
                    {
                        reason = $"対象「{t}」を数値として読めません";
                        return false;
                    }
                    set.Add(id);
                }
            }

            if (set.Count == 0)
            {
                reason = "対象パーツを読み取れませんでした";
                return false;
            }

            ids = set;
            return true;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// メッシュ 1 個ぶんをスムージングする。
        /// 検算に落ちた場合は 1 頂点も書き換えずに false を返す。
        /// </summary>
        /// <param name="mo">対象メッシュ。</param>
        /// <param name="weights">重みの並び（個数は奇数）。</param>
        /// <param name="targetIds">対象のパーツID。null なら全パーツ。</param>
        /// <param name="edgeMode">窓が外へ出るときの扱い。</param>
        /// <param name="smoothedCount">実際にスムージングしたパーツ数。</param>
        /// <param name="movedCount">実際に位置が変わった頂点数。</param>
        /// <param name="reason">失敗した理由。成功時は null。</param>
        public static bool Execute(
            MeshObject mo, IReadOnlyList<float> weights, HashSet<int> targetIds,
            PipeSmoothEdgeMode edgeMode,
            out int smoothedCount, out int movedCount, out string reason)
        {
            smoothedCount = 0;
            movedCount    = 0;
            reason        = null;

            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
            {
                reason = "頂点がありません";
                return false;
            }

            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                if (mo.Vertices[i] == null)
                {
                    reason = $"頂点 {i} が空です";
                    return false;
                }
            }

            if (weights == null || weights.Count == 0 || (weights.Count & 1) == 0)
            {
                reason = "重みの個数は奇数にしてください";
                return false;
            }

            var groups = PipeAlignOps.BuildGroups(mo);
            int n = groups.Count;
            if (n == 0)
            {
                reason = "パーツがありません";
                return false;
            }

            int half = (weights.Count - 1) / 2;

            // 対象パーツの並び位置を集める。
            var targetSlots = new List<int>();
            for (int p = 0; p < n; p++)
            {
                if (targetIds != null && !targetIds.Contains(groups[p].PartsId)) continue;
                targetSlots.Add(p);
            }

            if (targetIds != null)
            {
                foreach (int id in targetIds)
                {
                    bool found = false;
                    foreach (var g in groups) { if (g.PartsId == id) { found = true; break; } }
                    if (!found)
                    {
                        reason = $"パーツID {id} がこのオブジェクトにありません";
                        return false;
                    }
                }
            }

            if (targetSlots.Count == 0)
            {
                reason = "対象パーツがありません";
                return false;
            }

            // 実際に平均を掛けるパーツと、その窓を決める。
            var plans = new List<SmoothPlan>();
            foreach (int p in targetSlots)
            {
                if (edgeMode == PipeSmoothEdgeMode.Skip
                    && (p - half < 0 || p + half > n - 1))
                    continue;

                var plan = new SmoothPlan { Slot = p, Offsets = new List<int>(), Weights = new List<float>() };

                float sum = 0f;
                for (int d = -half; d <= half; d++)
                {
                    int q = p + d;
                    if (q < 0 || q >= n) continue;

                    float w = weights[d + half];
                    if (w <= 0f) continue;

                    plan.Offsets.Add(q);
                    plan.Weights.Add(w);
                    sum += w;
                }

                if (sum <= 0f) continue;

                plan.WeightSum = sum;
                plans.Add(plan);
            }

            if (plans.Count == 0)
            {
                reason = "スムージングできるパーツがありません（端の扱いを見直してください）";
                return false;
            }

            // 窓に入るパーツ同士の頂点数一致を、書き換え前に全部見る。
            foreach (var plan in plans)
            {
                int vc = groups[plan.Slot].Indices.Count;
                foreach (int q in plan.Offsets)
                {
                    if (groups[q].Indices.Count != vc)
                    {
                        reason = $"パーツID {groups[plan.Slot].PartsId} と {groups[q].PartsId} の"
                               + $"頂点数が一致しません（{vc} / {groups[q].Indices.Count}）";
                        return false;
                    }
                }
            }

            // ここから書き換え。
            var orig = new Vector3[mo.Vertices.Count];
            for (int i = 0; i < orig.Length; i++) orig[i] = mo.Vertices[i].Position;

            foreach (var plan in plans)
            {
                var dst = groups[plan.Slot];
                int vc  = dst.Indices.Count;

                for (int j = 0; j < vc; j++)
                {
                    Vector3 acc = Vector3.zero;
                    for (int t = 0; t < plan.Offsets.Count; t++)
                        acc += orig[groups[plan.Offsets[t]].Indices[j]] * plan.Weights[t];

                    Vector3 pos = acc / plan.WeightSum;

                    var v = mo.Vertices[dst.Indices[j]];
                    if (v.Position == pos) continue;
                    v.Position = pos;
                    movedCount++;
                }

                smoothedCount++;
            }

            return true;
        }

        /// <summary>1 パーツぶんの平均計画。</summary>
        private sealed class SmoothPlan
        {
            /// <summary>書き込み先パーツのパイプ列内の位置。</summary>
            public int Slot;

            /// <summary>平均に使うパーツのパイプ列内の位置。</summary>
            public List<int> Offsets;

            /// <summary>Offsets と同じ並びの重み。</summary>
            public List<float> Weights;

            /// <summary>Weights の総和。</summary>
            public float WeightSum;
        }
    }
}
