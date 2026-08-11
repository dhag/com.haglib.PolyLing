// VertexIdOps.cs
// 頂点IDの診断と修復。
//
// 【なぜ必要か】
//   頂点IDはモデル間・オブジェクト間で「同じ頂点」を突き合わせる唯一の手段だが、
//   実運用では信頼できない状態になりやすい:
//     - 他所製の PMX は頂点IDを持たない（PolyLing が書き出した PMX だけが
//       __PLM_ UV モーフ経由で復元される。PMXImporter 参照）
//     - 特殊面を持たない MQO は全頂点が -1 のまま入ってくる（MQOImporter 参照）
//     - 後から追加した頂点だけIDが無い、コピーで重複した、など混在も起きる
//   ID を使う操作の前に「今どういう状態か」を数字で見せ、必要なら直せるようにする。
//
// 【未設定の定義】
//   MeshObject.IsUnsetId（0 と -1 の両方が未設定）に従う。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>1 メッシュ分の頂点ID診断結果。</summary>
    public struct VertexIdReport
    {
        public string MeshName;
        public int    VertexCount;
        /// <summary>未設定（0 / -1）の頂点数。</summary>
        public int    UnsetCount;
        /// <summary>有効IDを持つ頂点数。</summary>
        public int    AssignedCount;
        /// <summary>重複しているIDの種類数。</summary>
        public int    DuplicateIdCount;
        /// <summary>重複に巻き込まれている頂点数（2 個目以降の合計）。</summary>
        public int    DuplicatedVertexCount;

        /// <summary>ID で頂点を一意に引けるか。</summary>
        public bool IsHealthy => VertexCount > 0 && UnsetCount == 0 && DuplicateIdCount == 0;

        /// <summary>ID による突き合わせに使える頂点数（未設定・重複を除く）。</summary>
        public int UsableCount => AssignedCount - DuplicatedVertexCount;

        public string Summary =>
            $"{MeshName}: 頂点 {VertexCount} / 有効ID {AssignedCount} / 未設定 {UnsetCount} / "
          + $"重複 {DuplicateIdCount} 種 ({DuplicatedVertexCount} 頂点)";
    }

    /// <summary>頂点IDの診断・修復。</summary>
    public static class VertexIdOps
    {
        // ================================================================
        // 診断
        // ================================================================

        /// <summary>1 メッシュを診断する。</summary>
        public static VertexIdReport Inspect(MeshContext mc)
        {
            var report = new VertexIdReport { MeshName = mc?.Name ?? "(no name)" };
            var mo = mc?.MeshObject;
            if (mo == null) return report;

            report.VertexCount = mo.VertexCount;

            var seen = new Dictionary<int, int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) { report.UnsetCount++; continue; }

                report.AssignedCount++;
                if (seen.TryGetValue(id, out int n)) seen[id] = n + 1;
                else                                 seen[id] = 1;
            }

            foreach (var kv in seen)
            {
                if (kv.Value <= 1) continue;
                report.DuplicateIdCount++;
                report.DuplicatedVertexCount += kv.Value - 1;
            }

            return report;
        }

        /// <summary>複数メッシュを診断する。</summary>
        public static List<VertexIdReport> Inspect(IEnumerable<MeshContext> meshContexts)
        {
            var list = new List<VertexIdReport>();
            if (meshContexts == null) return list;
            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;
                list.Add(Inspect(mc));
            }
            return list;
        }

        /// <summary>
        /// 2 メッシュ間で、ID による突き合わせがどれだけ成立するかを数える。
        /// ID 転送・ID マッチを実行する前の確認に使う。
        /// </summary>
        /// <returns>(一致した頂点数, 転送元にのみ存在するID数, 転送先にのみ存在するID数)</returns>
        public static (int matched, int srcOnly, int dstOnly) CountIdMatches(MeshContext src, MeshContext dst)
        {
            var srcIds = CollectUsableIds(src);
            var dstIds = CollectUsableIds(dst);

            int matched = 0;
            foreach (var id in srcIds)
                if (dstIds.Contains(id)) matched++;

            return (matched, srcIds.Count - matched, dstIds.Count - matched);
        }

        /// <summary>未設定・重複を除いた、突き合わせに使える ID の集合。</summary>
        public static HashSet<int> CollectUsableIds(MeshContext mc)
        {
            var result = new HashSet<int>();
            var dup    = new HashSet<int>();
            var mo     = mc?.MeshObject;
            if (mo == null) return result;

            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;
                if (!result.Add(id)) dup.Add(id);
            }
            // 重複しているIDは「どの頂点か」を決められないので使えない。
            foreach (var id in dup) result.Remove(id);
            return result;
        }

        // ================================================================
        // 修復
        // ================================================================

        /// <summary>
        /// 未設定（0 / -1）の頂点にだけ新しい ID を割り当てる。
        /// 既にある有効IDは変更しない。
        /// </summary>
        /// <returns>割り当てた頂点数。</returns>
        public static int AssignMissing(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            mo.RebuildIdSets();

            int assigned = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                if (!MeshObject.IsUnsetId(mo.Vertices[i].Id)) continue;
                mo.Vertices[i].Id = mo.GenerateVertexId();
                assigned++;
            }
            return assigned;
        }

        /// <summary>
        /// 重複している ID のうち 2 個目以降に新しい ID を振り直す。
        /// 先頭の 1 個は元の ID を保持するので、既存の対応付けは壊れない。
        /// </summary>
        /// <returns>振り直した頂点数。</returns>
        public static int ResolveDuplicates(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            mo.RebuildIdSets();

            var seen    = new HashSet<int>();
            int changed = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;

                if (seen.Add(id)) continue;   // 初出はそのまま

                int newId = mo.GenerateVertexId();
                mo.Vertices[i].Id = newId;
                seen.Add(newId);
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// 全頂点に 1 から始まる連番 ID を振り直す。
        /// 既存の ID による対応付けは全て失われるので、外部データとの
        /// 突き合わせが不要になった段階でだけ使うこと。
        /// </summary>
        /// <returns>振り直した頂点数。</returns>
        public static int ReassignSequential(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            for (int i = 0; i < mo.VertexCount; i++)
                mo.Vertices[i].Id = i + 1;

            mo.RebuildIdSets();
            return mo.VertexCount;
        }

        /// <summary>
        /// 全頂点の ID を未設定（0）に戻す。
        /// 誤った ID が付いた状態から出直すための操作。
        /// </summary>
        /// <returns>消去した頂点数。</returns>
        public static int ClearAll(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            for (int i = 0; i < mo.VertexCount; i++)
                mo.Vertices[i].Id = 0;

            mo.RebuildIdSets();
            return mo.VertexCount;
        }
    }
}
