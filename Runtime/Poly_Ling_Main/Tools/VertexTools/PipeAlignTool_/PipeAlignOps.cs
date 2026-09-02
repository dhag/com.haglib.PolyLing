// PipeAlignOps.cs
// パイプ群の左右対称化（パイプの整列）の純ロジック。MeshObject 1 個ぶんを処理する。
// Runtime/Poly_Ling_Main/Tools/VertexTools/PipeAlignTool_/ に配置
//
// 【前提とする頂点並び】
//   1 パイプ ＝ 1 パーツID（Vertex.PartsId）。パーツ内の頂点はインデックス昇順で
//     段0の周方向 0..M-1 / 段1の周方向 0..M-1 / … /（開始側の先端）/（終了側の先端）
//   の順に並ぶ。PipeMeshGenerator の生成順（頂点 = 段 i × 断面点 k、先端は末尾へ追加、
//   開始側が先で終了側が後）と一致する。
//
// 【対応規則】
//   ・周方向は k と M-1-k を対にする。段の番号 i は変えない。
//   ・先端頂点は 開始↔開始 / 終了↔終了。
//
// 【2 つのモード】
//   ・自動ペア（Execute）
//       パーツIDの昇順に p = 0..N-1 とし、p と N-1-p を対にする。
//       N が奇数のとき、中央のパイプは自分自身と対称化する。
//   ・手動ペア（ExecuteManualPairs）
//       「コピー元パーツID, コピー先パーツID」を 1 行ずつ列挙して指定する。
//       ID を 1 つだけ書いた行は、そのパーツを自分自身と対称化する。
//       列挙されていないパーツは触らない。
//
// 【ミラー面】ローカル座標の X = 0。コピーは X を反転して書き込む。
//
// 【左右の判定（自動ペアのみ）】パーツIDの昇順が +X 側から始まるか -X 側から
//   始まるかは、最小パーツIDと最大パーツIDのパーツ重心 X を 1 回だけ比べて決める。
//   パイプごとに判定しない。
//
// 【読み書きの分離】書き込みの前に全頂点位置を控え、コピー元は必ず控えた側から読む。
//   そのため「0,8」と「8,0」のように相互に指定しても結果が処理順に依存しない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>コピーの向き。</summary>
    public enum PipeAlignDirection
    {
        /// <summary>+X 側を元にして -X 側へ書き込む。</summary>
        PlusToMinus = 0,

        /// <summary>-X 側を元にして +X 側へ書き込む。</summary>
        MinusToPlus = 1,
    }

    /// <summary>手動ペアの 1 行ぶん。</summary>
    public struct PipePair
    {
        /// <summary>コピー元のパーツID。自己対称化のときは対象そのもの。</summary>
        public int SourceId;

        /// <summary>コピー先のパーツID。自己対称化のときは SourceId と同じ。</summary>
        public int TargetId;

        /// <summary>自分自身で左右対称化する行か。</summary>
        public bool SelfMirror;
    }

    /// <summary>パイプ群の左右対称化。</summary>
    public static class PipeAlignOps
    {
        /// <summary>両端パーツの重心 X の差がこれ未満だと左右の順序を決められない。</summary>
        private const float OrderEpsilon = 1e-6f;

        /// <summary>自己対称化で周方向ペアの左右を決めるときの許容値。</summary>
        private const float SidePairEpsilon = 1e-6f;

        // ================================================================
        // パーツの集約
        // ================================================================

        /// <summary>1 パイプぶん（＝1 パーツID ぶん）の頂点。</summary>
        public sealed class PipeGroup
        {
            /// <summary>このパイプのパーツID。</summary>
            public int PartsId;

            /// <summary>このパーツに属する頂点インデックス（昇順）。</summary>
            public List<int> Indices = new List<int>();

            /// <summary>段数。ResolveRings 後に入る。</summary>
            public int RingCount;

            /// <summary>開始側の先端頂点のインデックス。無ければ -1。</summary>
            public int StartCap = -1;

            /// <summary>終了側の先端頂点のインデックス。無ければ -1。</summary>
            public int EndCap = -1;

            /// <summary>このパーツの重心 X。</summary>
            public float CenterX;
        }

        /// <summary>
        /// パーツID ごとに頂点を集め、パーツIDの昇順で返す。
        /// 頂点インデックスは各グループ内で昇順になる。
        /// </summary>
        public static List<PipeGroup> BuildGroups(MeshObject mo)
        {
            var result = new List<PipeGroup>();
            if (mo == null || mo.Vertices == null) return result;

            var map = new Dictionary<int, PipeGroup>();

            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                if (!map.TryGetValue(v.PartsId, out var g))
                {
                    g = new PipeGroup { PartsId = v.PartsId };
                    map[v.PartsId] = g;
                }
                g.Indices.Add(i);
            }

            var ids = new List<int>(map.Keys);
            ids.Sort();

            foreach (int id in ids)
            {
                var g = map[id];

                double sum = 0.0;
                foreach (int idx in g.Indices) sum += mo.Vertices[idx].Position.x;
                g.CenterX = g.Indices.Count > 0 ? (float)(sum / g.Indices.Count) : 0f;

                result.Add(g);
            }

            return result;
        }

        /// <summary>パーツIDから引ける辞書を作る。</summary>
        public static Dictionary<int, PipeGroup> ToMap(List<PipeGroup> groups)
        {
            var map = new Dictionary<int, PipeGroup>();
            if (groups == null) return map;
            foreach (var g in groups) map[g.PartsId] = g;
            return map;
        }

        // ================================================================
        // 共通の下調べ
        // ================================================================

        /// <summary>空の頂点が混ざっていないか調べる。</summary>
        private static bool CheckVertices(MeshObject mo, out string reason)
        {
            reason = null;

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

            return true;
        }

        /// <summary>
        /// 段数を検算し、段数と先端頂点のインデックスをグループへ入れる。
        /// 1 つでも落ちたら false を返す（呼び出し側はそのメッシュを触らないこと）。
        /// </summary>
        private static bool ResolveRings(
            IEnumerable<PipeGroup> groups, int ringVertexCount, bool capStart, bool capEnd,
            out string reason)
        {
            reason = null;
            int capCount = (capStart ? 1 : 0) + (capEnd ? 1 : 0);

            foreach (var g in groups)
            {
                int ringVerts = g.Indices.Count - capCount;
                if (ringVerts <= 0 || ringVerts % ringVertexCount != 0)
                {
                    reason = $"パーツID {g.PartsId}: 頂点数 {g.Indices.Count} が"
                           + $"「1段 {ringVertexCount} 頂点 × 段数 ＋ 先端 {capCount}」になりません";
                    return false;
                }

                g.RingCount = ringVerts / ringVertexCount;

                int p = ringVerts;
                if (capStart) g.StartCap = g.Indices[p++];
                if (capEnd)   g.EndCap   = g.Indices[p++];
            }

            return true;
        }

        /// <summary>書き込み前の位置を控える。</summary>
        private static Vector3[] CaptureOriginal(MeshObject mo)
        {
            var orig = new Vector3[mo.Vertices.Count];
            for (int i = 0; i < orig.Length; i++) orig[i] = mo.Vertices[i].Position;
            return orig;
        }

        // ================================================================
        // 自動ペア
        // ================================================================

        /// <summary>
        /// メッシュ 1 個ぶんを、パーツIDの昇順の端から順に対にして対称化する。
        /// 検算に落ちた場合は 1 頂点も書き換えずに false を返す。
        /// </summary>
        /// <param name="mo">対象メッシュ。</param>
        /// <param name="ringVertexCount">1 段の頂点数 M。</param>
        /// <param name="capStart">開始側が先端頂点で閉じているか。</param>
        /// <param name="capEnd">終了側が先端頂点で閉じているか。</param>
        /// <param name="direction">コピーの向き。</param>
        /// <param name="pipeCount">見つかったパイプ数 N。</param>
        /// <param name="movedCount">実際に位置が変わった頂点数。</param>
        /// <param name="reason">失敗した理由。成功時は null。</param>
        public static bool Execute(
            MeshObject mo, int ringVertexCount, bool capStart, bool capEnd,
            PipeAlignDirection direction,
            out int pipeCount, out int movedCount, out string reason)
        {
            pipeCount  = 0;
            movedCount = 0;

            if (!CheckVertices(mo, out reason)) return false;

            if (ringVertexCount < 3)
            {
                reason = "1段の頂点数は3以上にしてください";
                return false;
            }

            var groups = BuildGroups(mo);
            int n = groups.Count;
            if (n == 0)
            {
                reason = "パーツがありません";
                return false;
            }

            if (!ResolveRings(groups, ringVertexCount, capStart, capEnd, out reason)) return false;

            // パーツIDの昇順が +X 側から始まるか。1 本だけなら不要。
            bool ascendingIsPlus = false;
            if (n >= 2)
            {
                float d = groups[0].CenterX - groups[n - 1].CenterX;
                if (Mathf.Abs(d) < OrderEpsilon)
                {
                    reason = $"両端パーツ（ID {groups[0].PartsId} と {groups[n - 1].PartsId}）の"
                           + "重心Xが同値のため左右の順序を判定できません";
                    return false;
                }
                ascendingIsPlus = d > 0f;
            }

            // 対になるパイプ同士の頂点数一致を先に全部見る。
            for (int p = 0; p < n / 2; p++)
            {
                var a = groups[p];
                var b = groups[n - 1 - p];
                if (a.Indices.Count != b.Indices.Count || a.RingCount != b.RingCount)
                {
                    reason = $"パーツID {a.PartsId} と {b.PartsId} の頂点数が一致しません"
                           + $"（{a.Indices.Count} / {b.Indices.Count}）";
                    return false;
                }
            }

            // ここから書き換え。
            var  orig             = CaptureOriginal(mo);
            bool srcIsAscendingSide = (direction == PipeAlignDirection.PlusToMinus) == ascendingIsPlus;

            for (int p = 0; p < n / 2; p++)
            {
                var a = groups[p];
                var b = groups[n - 1 - p];
                var src = srcIsAscendingSide ? a : b;
                var dst = srcIsAscendingSide ? b : a;
                movedCount += CopyPair(mo, orig, src, dst, ringVertexCount);
            }

            if ((n & 1) == 1)
                movedCount += SelfMirror(mo, orig, groups[n / 2], ringVertexCount, direction);

            pipeCount = n;
            return true;
        }

        // ================================================================
        // 手動ペア
        // ================================================================

        /// <summary>
        /// ペア指定の文字列を読む。1 行 1 エントリ。区切りはカンマまたは空白。
        /// 空行と '#' で始まる行は読み飛ばす。
        /// 「元ID, 先ID」で片方向コピー、「ID」1 つだけなら自己対称化。
        /// </summary>
        public static bool ParsePairs(string text, out List<PipePair> pairs, out string reason)
        {
            pairs  = new List<PipePair>();
            reason = null;

            if (string.IsNullOrEmpty(text))
            {
                reason = "ペアが指定されていません";
                return false;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li].Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;

                var tokens = line.Split(new[] { ',', ' ', '\t', '、' },
                                        System.StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length == 1)
                {
                    if (!int.TryParse(tokens[0].Trim(), out int id))
                    {
                        reason = $"{li + 1} 行目: 「{line}」を数値として読めません";
                        return false;
                    }
                    pairs.Add(new PipePair { SourceId = id, TargetId = id, SelfMirror = true });
                }
                else if (tokens.Length == 2)
                {
                    if (!int.TryParse(tokens[0].Trim(), out int src)
                     || !int.TryParse(tokens[1].Trim(), out int dst))
                    {
                        reason = $"{li + 1} 行目: 「{line}」を数値として読めません";
                        return false;
                    }
                    if (src == dst)
                    {
                        reason = $"{li + 1} 行目: コピー元と先が同じパーツID {src} です";
                        return false;
                    }
                    pairs.Add(new PipePair { SourceId = src, TargetId = dst, SelfMirror = false });
                }
                else
                {
                    reason = $"{li + 1} 行目: 1 行にはパーツIDを 1 つか 2 つ書いてください";
                    return false;
                }
            }

            if (pairs.Count == 0)
            {
                reason = "ペアが指定されていません";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 指定されたペアだけを対称化する。列挙されていないパーツは触らない。
        /// 検算に落ちた場合は 1 頂点も書き換えずに false を返す。
        /// </summary>
        /// <param name="pipeCount">書き換えたパーツ数。</param>
        public static bool ExecuteManualPairs(
            MeshObject mo, int ringVertexCount, bool capStart, bool capEnd,
            PipeAlignDirection direction, IReadOnlyList<PipePair> pairs,
            out int pipeCount, out int movedCount, out string reason)
        {
            pipeCount  = 0;
            movedCount = 0;

            if (!CheckVertices(mo, out reason)) return false;

            if (ringVertexCount < 3)
            {
                reason = "1段の頂点数は3以上にしてください";
                return false;
            }

            if (pairs == null || pairs.Count == 0)
            {
                reason = "ペアが指定されていません";
                return false;
            }

            var map = ToMap(BuildGroups(mo));
            if (map.Count == 0)
            {
                reason = "パーツがありません";
                return false;
            }

            // 参照されているパーツが実在するか。
            var used = new List<PipeGroup>();
            var seen = new HashSet<int>();
            foreach (var pr in pairs)
            {
                if (!map.ContainsKey(pr.SourceId))
                {
                    reason = $"パーツID {pr.SourceId} がこのオブジェクトにありません";
                    return false;
                }
                if (!map.ContainsKey(pr.TargetId))
                {
                    reason = $"パーツID {pr.TargetId} がこのオブジェクトにありません";
                    return false;
                }
                if (seen.Add(pr.SourceId)) used.Add(map[pr.SourceId]);
                if (seen.Add(pr.TargetId)) used.Add(map[pr.TargetId]);
            }

            // 段数の検算は、参照されているパーツにだけ掛ける。
            if (!ResolveRings(used, ringVertexCount, capStart, capEnd, out reason)) return false;

            // 同じパーツが 2 回以上コピー先になっていないか。
            var targets = new HashSet<int>();
            foreach (var pr in pairs)
            {
                if (!targets.Add(pr.TargetId))
                {
                    reason = $"パーツID {pr.TargetId} が 2 回以上コピー先になっています";
                    return false;
                }
            }

            // ペアの頂点数一致。
            foreach (var pr in pairs)
            {
                if (pr.SelfMirror) continue;

                var s = map[pr.SourceId];
                var d = map[pr.TargetId];
                if (s.Indices.Count != d.Indices.Count || s.RingCount != d.RingCount)
                {
                    reason = $"パーツID {s.PartsId} と {d.PartsId} の頂点数が一致しません"
                           + $"（{s.Indices.Count} / {d.Indices.Count}）";
                    return false;
                }
            }

            // ここから書き換え。
            var orig = CaptureOriginal(mo);

            foreach (var pr in pairs)
            {
                if (pr.SelfMirror)
                    movedCount += SelfMirror(mo, orig, map[pr.SourceId], ringVertexCount, direction);
                else
                    movedCount += CopyPair(mo, orig, map[pr.SourceId], map[pr.TargetId], ringVertexCount);
            }

            pipeCount = targets.Count;
            return true;
        }

        // ================================================================
        // 対になるパイプ間のコピー
        // ================================================================

        private static int CopyPair(MeshObject mo, Vector3[] orig, PipeGroup src, PipeGroup dst, int m)
        {
            int moved = 0;

            for (int i = 0; i < src.RingCount; i++)
            {
                int rowBase = i * m;
                for (int k = 0; k < m; k++)
                {
                    Vector3 pos = orig[src.Indices[rowBase + k]];
                    pos.x = -pos.x;
                    moved += SetPosition(mo, dst.Indices[rowBase + (m - 1 - k)], pos);
                }
            }

            if (src.StartCap >= 0 && dst.StartCap >= 0)
            {
                Vector3 pos = orig[src.StartCap];
                pos.x = -pos.x;
                moved += SetPosition(mo, dst.StartCap, pos);
            }

            if (src.EndCap >= 0 && dst.EndCap >= 0)
            {
                Vector3 pos = orig[src.EndCap];
                pos.x = -pos.x;
                moved += SetPosition(mo, dst.EndCap, pos);
            }

            return moved;
        }

        // ================================================================
        // 自己対称化
        // ================================================================

        /// <summary>
        /// 1 本のパイプを自分自身と対称化する。
        /// 周方向ペア (k, M-1-k) のどちらをコピー元にするかは、
        /// そのパイプの全段にわたる x の平均を比べて決める。
        /// </summary>
        private static int SelfMirror(
            MeshObject mo, Vector3[] orig, PipeGroup g, int m, PipeAlignDirection direction)
        {
            int  moved          = 0;
            bool wantPlusSource = direction == PipeAlignDirection.PlusToMinus;

            for (int k = 0; k < m - 1 - k; k++)
            {
                int kk = m - 1 - k;

                float ax = AverageX(orig, g, k,  m);
                float bx = AverageX(orig, g, kk, m);
                if (Mathf.Abs(ax - bx) < SidePairEpsilon) continue;

                bool kIsPlus = ax > bx;
                int  srcK    = (kIsPlus == wantPlusSource) ? k : kk;
                int  dstK    = (srcK == k) ? kk : k;

                for (int i = 0; i < g.RingCount; i++)
                {
                    int rowBase = i * m;
                    Vector3 pos = orig[g.Indices[rowBase + srcK]];
                    pos.x = -pos.x;
                    moved += SetPosition(mo, g.Indices[rowBase + dstK], pos);
                }
            }

            // M が奇数のとき、k == M-1-k となる周方向インデックスは自分自身が対。
            if ((m & 1) == 1)
            {
                int mid = m / 2;
                for (int i = 0; i < g.RingCount; i++)
                    moved += SetX(mo, g.Indices[i * m + mid], 0f);
            }

            // 先端頂点も自分自身が対。
            if (g.StartCap >= 0) moved += SetX(mo, g.StartCap, 0f);
            if (g.EndCap   >= 0) moved += SetX(mo, g.EndCap,   0f);

            return moved;
        }

        private static float AverageX(Vector3[] orig, PipeGroup g, int k, int m)
        {
            if (g.RingCount <= 0) return 0f;

            double sum = 0.0;
            for (int i = 0; i < g.RingCount; i++)
                sum += orig[g.Indices[i * m + k]].x;

            return (float)(sum / g.RingCount);
        }

        // ================================================================
        // 書き込み
        // ================================================================

        /// <summary>位置を書き込む。実際に変わったら 1、変わらなければ 0 を返す。</summary>
        private static int SetPosition(MeshObject mo, int index, Vector3 pos)
        {
            var v = mo.Vertices[index];
            if (v.Position == pos) return 0;
            v.Position = pos;
            return 1;
        }

        /// <summary>X だけ書き込む。実際に変わったら 1、変わらなければ 0 を返す。</summary>
        private static int SetX(MeshObject mo, int index, float x)
        {
            var v = mo.Vertices[index];
            if (v.Position.x == x) return 0;

            Vector3 pos = v.Position;
            pos.x = x;
            v.Position = pos;
            return 1;
        }
    }
}
