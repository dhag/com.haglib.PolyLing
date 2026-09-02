// PartsIdAssignOps.cs
// パーツID（Vertex.PartsId）とサブID（Vertex.SubId）の一括採番。
//
// 【頂点IDとの関係】
//   このファイルは Vertex.Id を一切読まないし書かない。
//   頂点IDの診断・修復は VertexIdOps / RepairVertexIdsCommand が持ち、
//   パーツID・サブIDはこのファイルと AssignPartsIdsCommand が持つ。
//   両者は完全に独立して操作できる。混ぜないこと。
//
// 【採番の約束】既存の PartsIdOps と同じ。
//   ・パーツIDは 1 つのメッシュの中で 0 から始まる通し番号。
//   ・サブIDはパーツIDごとのローカル通し番号で、頂点の並び順の先頭から 0,1,2…。
//     サブIDはパーツID依存なので、パーツIDを書いた直後に必ず振り直す。
//   ・どちらも一意性は保証しない。値だけで「未設定」と「先頭パーツ」は見分けられない。
//
// 【2 つの採番方式】
//   ①独立性（Connectivity）
//      面（三角以上）と線（2頂点の Face）でつながっている頂点を 1 パーツにする。
//      パーツ番号はパーツ内の最小頂点インデックスの昇順で 0,1,2…。
//      → 生成順が頂点インデックス順と一致する図形（パイプ・藤壺）は
//        生成時のパーツIDと同じ並びに戻る。
//   ②リファレンスの頂点数（ReferenceVertexCount）
//      「1 パーツの頂点数」を 1 つのリファレンスオブジェクトから取り、
//      対象の頂点列を先頭から等分して 0,1,2… を振る。
//      対象の頂点数がリファレンスの頂点数で割り切れないときは、
//      1 頂点も書き換えずに失敗させる（中途半端な番号を残さないため）。
//
// 【この方式で再現できないもの（既知・確認済み）】
//   ・フリル（融合あり）
//       FrillMeshGenerator はレール行ごとにパーツIDを分けるが、フリルは面で全体が
//       つながっているので①では 1 パーツになる。
//       頂点はレール順に出るため、同一パーツIDの頂点が連続ブロックにならず②でも
//       再現できない（FrillMeshGenerator.cs:224- のパス3 と :381-430 を参照）。
//       フリルへ掛けると生成時のパーツID構造は失われる。
//   ・藤壺（配置元が複数の島を持つ場合）
//       ①では 1 インスタンスが複数パーツへ割れる。②なら配置元をリファレンスに
//       指定すれば正しく割れる。
//
// 【MQO 由来メッシュでの注意】
//   ・MQOImporter は特殊面が無いと PartsId / SubId を 0 のまま読む
//     （MQOImporter.cs:1209-1236）。全頂点が 1 パーツ扱いになるので、
//     読み込み直後はこのツールで振り直す前提。
//   ・MQO の「特殊面」（全頂点インデックスが同じ面）は Faces に入らない
//     （MQOImporter.cs:1303-1304）ので、①の連結判定を汚さない。
//   ・MQO の線（2頂点の面）は Face として入る（MQOImporter.cs:1547-1565）ので、
//     ①では線でつながった頂点も同じパーツになる。
//   ・面にも線にも属さない孤立頂点は MQO では珍しくない。
//     IsolatedVertexPolicy で扱いを選ぶこと。
//   ・パーツID / サブIDは MQO 特殊面の COL(PartsID, SubID, ID) で往復する
//     （MQOExporter.cs:1062-1073 / VertexIdHelper.cs:341-）。負値は uint で
//     書かれて壊れるので、このファイルは 0 以上しか書かない。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>パーツIDの採番方式。</summary>
    public enum PartsIdAssignMode
    {
        /// <summary>面・線のつながり（独立性）で分ける。</summary>
        Connectivity = 0,

        /// <summary>1 パーツの頂点数をリファレンスから取り、先頭から等分する。</summary>
        ReferenceVertexCount = 1,
    }

    /// <summary>面にも線にも属さない頂点の扱い（Connectivity のときだけ効く）。</summary>
    public enum IsolatedVertexPolicy
    {
        /// <summary>孤立頂点をまとめて 1 パーツにする（番号は末尾）。</summary>
        SingleGroup = 0,

        /// <summary>孤立頂点 1 つずつを独立したパーツにする。</summary>
        SeparateParts = 1,
    }

    /// <summary>採番の実行結果。</summary>
    public struct PartsIdAssignResult
    {
        /// <summary>書き込んだか。false なら 1 頂点も変更していない。</summary>
        public bool Success;

        /// <summary>採番したパーツ数。</summary>
        public int PartCount;

        /// <summary>対象の頂点数。</summary>
        public int VertexCount;

        /// <summary>面にも線にも属さない頂点の数。</summary>
        public int IsolatedVertexCount;

        /// <summary>失敗の理由。成功時は空。</summary>
        public string Reason;

        public static PartsIdAssignResult Fail(string reason)
            => new PartsIdAssignResult { Success = false, Reason = reason ?? "" };
    }

    /// <summary>パーツID / サブIDの診断結果。</summary>
    public struct PartsIdReport
    {
        public string MeshName;
        public int    VertexCount;

        /// <summary>現在のパーツIDの種類数。</summary>
        public int CurrentPartCount;

        /// <summary>現在のパーツIDの最小値・最大値。頂点が無ければ 0。</summary>
        public int MinPartsId;
        public int MaxPartsId;

        /// <summary>現在のパーツIDが 0..CurrentPartCount-1 の連番になっているか。</summary>
        public bool PartsIdIsDense;

        /// <summary>面・線のつながりで数えたパーツ数（孤立頂点は 1 つずつ数える）。</summary>
        public int ConnectedComponentCount;

        /// <summary>面にも線にも属さない頂点の数。</summary>
        public int IsolatedVertexCount;

        /// <summary>全パーツでサブIDが並び順どおり 0,1,2… になっているか。</summary>
        public bool SubIdIsSequential;

        public string Summary =>
            $"{MeshName}: 頂点 {VertexCount} / 現在のパーツ {CurrentPartCount} 種"
          + $"（ID {MinPartsId}..{MaxPartsId}{(PartsIdIsDense ? "" : " 抜けあり")}）"
          + $" / つながりで数えると {ConnectedComponentCount} 個"
          + $" / 孤立頂点 {IsolatedVertexCount}"
          + $" / サブID {(SubIdIsSequential ? "整合" : "不整合")}";
    }

    /// <summary>パーツID / サブIDの一括採番。Vertex.Id には触れない。</summary>
    public static class PartsIdAssignOps
    {
        // ================================================================
        // 採番
        // ================================================================

        /// <summary>
        /// 面・線のつながりでパーツIDを振り直し、続けてサブIDを振り直す。
        /// パーツ番号はパーツ内の最小頂点インデックスの昇順で 0 から。
        /// </summary>
        public static PartsIdAssignResult AssignByConnectivity(
            MeshObject mo, IsolatedVertexPolicy isolatedPolicy)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
                return PartsIdAssignResult.Fail("対象メッシュに頂点がありません");

            int n = mo.Vertices.Count;

            var parent = BuildUnionFind(mo, out bool[] hasLink);

            // 代表 → パーツ番号。番号は「そのパーツに属する最小頂点インデックス」の昇順。
            // 頂点を先頭から走査して初出の代表に番号を配るだけで昇順になる。
            var repToPart = new Dictionary<int, int>();
            int nextPart  = 0;

            int isolatedCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (mo.Vertices[i] == null) continue;
                if (!hasLink[i]) isolatedCount++;
            }

            // まとめる方式のときだけ、孤立頂点用の番号を最後に確保する。
            bool poolIsolated = (isolatedPolicy == IsolatedVertexPolicy.SingleGroup)
                             && isolatedCount > 0;

            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                if (poolIsolated && !hasLink[i]) continue;

                int rep = Find(parent, i);
                if (!repToPart.ContainsKey(rep))
                {
                    repToPart[rep] = nextPart;
                    nextPart++;
                }
            }

            int isolatedPart = poolIsolated ? nextPart : -1;
            int partCount    = poolIsolated ? nextPart + 1 : nextPart;

            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                if (poolIsolated && !hasLink[i]) { v.PartsId = isolatedPart; continue; }
                v.PartsId = repToPart[Find(parent, i)];
            }

            PartsIdOps.AssignSubIdByPartsId(mo);

            return new PartsIdAssignResult
            {
                Success             = true,
                PartCount           = partCount,
                VertexCount         = n,
                IsolatedVertexCount = isolatedCount,
                Reason              = "",
            };
        }

        /// <summary>
        /// 1 パーツの頂点数を決め打ちして、頂点列を先頭から等分でパーツIDへ割り振る。
        /// 割り切れないときは 1 頂点も書き換えずに失敗させる。
        /// </summary>
        public static PartsIdAssignResult AssignByVertexCount(MeshObject mo, int perPartVertexCount)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
                return PartsIdAssignResult.Fail("対象メッシュに頂点がありません");

            if (perPartVertexCount <= 0)
                return PartsIdAssignResult.Fail("1 パーツの頂点数が 0 以下です");

            int n = mo.Vertices.Count;
            if (n % perPartVertexCount != 0)
                return PartsIdAssignResult.Fail(
                    $"対象の頂点数 {n} が 1 パーツの頂点数 {perPartVertexCount} で割り切れません"
                  + $"（余り {n % perPartVertexCount}）");

            int partCount = n / perPartVertexCount;

            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                v.PartsId = i / perPartVertexCount;
            }

            PartsIdOps.AssignSubIdByPartsId(mo);

            return new PartsIdAssignResult
            {
                Success             = true,
                PartCount           = partCount,
                VertexCount         = n,
                IsolatedVertexCount = 0,
                Reason              = "",
            };
        }

        /// <summary>
        /// パーツIDはそのままで、サブIDだけをパーツごとに 0,1,2… へ振り直す。
        /// </summary>
        public static PartsIdAssignResult AssignSubIdOnly(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
                return PartsIdAssignResult.Fail("対象メッシュに頂点がありません");

            PartsIdOps.AssignSubIdByPartsId(mo);

            var ids = new HashSet<int>();
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                ids.Add(v.PartsId);
            }

            return new PartsIdAssignResult
            {
                Success     = true,
                PartCount   = ids.Count,
                VertexCount = mo.Vertices.Count,
                Reason      = "",
            };
        }

        /// <summary>
        /// パーツID・サブIDを両方 0 に戻す。Vertex.Id には触れない。
        /// </summary>
        public static PartsIdAssignResult Clear(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0)
                return PartsIdAssignResult.Fail("対象メッシュに頂点がありません");

            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                v.PartsId = 0;
                v.SubId   = 0;
            }

            return new PartsIdAssignResult
            {
                Success     = true,
                PartCount   = 1,
                VertexCount = mo.Vertices.Count,
                Reason      = "",
            };
        }

        // ================================================================
        // 診断
        // ================================================================

        /// <summary>現在のパーツID / サブIDの状態を数える。書き換えはしない。</summary>
        public static PartsIdReport Inspect(MeshObject mo, string meshName)
        {
            var report = new PartsIdReport { MeshName = meshName ?? "(no name)" };
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0) return report;

            int n = mo.Vertices.Count;
            report.VertexCount = n;

            // 現在のパーツID
            var ids = new HashSet<int>();
            int min = int.MaxValue, max = int.MinValue;
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                ids.Add(v.PartsId);
                if (v.PartsId < min) min = v.PartsId;
                if (v.PartsId > max) max = v.PartsId;
            }
            report.CurrentPartCount = ids.Count;
            report.MinPartsId       = (min == int.MaxValue) ? 0 : min;
            report.MaxPartsId       = (max == int.MinValue) ? 0 : max;
            report.PartsIdIsDense   = ids.Count > 0
                                   && report.MinPartsId == 0
                                   && report.MaxPartsId == ids.Count - 1;

            // つながりの数
            var parent = BuildUnionFind(mo, out bool[] hasLink);
            var reps   = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (mo.Vertices[i] == null) continue;
                if (!hasLink[i]) report.IsolatedVertexCount++;
                reps.Add(Find(parent, i));
            }
            report.ConnectedComponentCount = reps.Count;

            // サブIDがパーツごとに並び順どおりか
            report.SubIdIsSequential = true;
            var next = new Dictionary<int, int>();
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                int expect = next.TryGetValue(v.PartsId, out int cur) ? cur : 0;
                if (v.SubId != expect) { report.SubIdIsSequential = false; break; }
                next[v.PartsId] = expect + 1;
            }

            return report;
        }

        // ================================================================
        // つながりの計算
        // ================================================================

        /// <summary>
        /// 面（三角以上）と線（2頂点の Face）で頂点を併合した Union-Find を作る。
        /// hasLink[i] は「頂点 i がいずれかの面・線に現れたか」。
        /// </summary>
        private static int[] BuildUnionFind(MeshObject mo, out bool[] hasLink)
        {
            int n = mo.Vertices.Count;

            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            hasLink = new bool[n];

            if (mo.Faces == null) return parent;

            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null) continue;

                int c = f.VertexIndices.Count;
                if (c < 2) continue;

                // 先頭を軸にして全頂点を併合する。面でも線でも同じ扱いでよい。
                int first = f.VertexIndices[0];
                if (first < 0 || first >= n) continue;
                hasLink[first] = true;

                for (int k = 1; k < c; k++)
                {
                    int vi = f.VertexIndices[k];
                    if (vi < 0 || vi >= n) continue;
                    hasLink[vi] = true;
                    Union(parent, first, vi);
                }
            }

            return parent;
        }

        private static int Find(int[] parent, int x)
        {
            // 経路圧縮。再帰にすると大きなメッシュでスタックを使い切る。
            int root = x;
            while (parent[root] != root) root = parent[root];

            while (parent[x] != root)
            {
                int next = parent[x];
                parent[x] = root;
                x = next;
            }
            return root;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb) return;

            // 小さいインデックスを代表にする。パーツ番号を頂点インデックス順で
            // 決めるので、代表の選び方を固定しておくと結果が決定的になる。
            if (ra < rb) parent[rb] = ra;
            else         parent[ra] = rb;
        }
    }
}
