// PlaceObjectReshapeOps.cs
// 藤壺（オブジェクト配置）で置いた部品を、原型の形へ張り直す。MeshObject 1 個ぶんを処理する。
// Runtime/Poly_Ling_Main/Tools/VertexTools/PlaceObjectReshapeTool_/ に配置
//
// 【やること】
//   パーツID（Vertex.PartsId）ごとに、原型の頂点を BEFORE、現在の部品の頂点を AFTER として
//   変換係数を推定し、その係数で原型を変換した位置を部品へ書き戻す。
//   壊れた（ひしゃげた）部品が、原型の形のまま元の位置・向き・大きさへ戻る。
//
// 【対応付け】
//   PlaceObjectMeshGenerator.AppendInstance は配置元の頂点を 0..VertexCount-1 の順で
//   連続して追加するため、パーツ内の並び順 k がそのまま原型の頂点 k に対応する。
//   サブIDは「0..N-1 の順列になっているときだけ」並び順と一致するかを検査する。
//   藤壺を新規オブジェクトとして作った場合は配置元のサブIDがそのまま残り
//   （PlayerPrimitiveMeshSubPanel.AssignGeneratedPartsIds が PlaceObject では何もしない）、
//   順列にならない値も正常なので、その場合は検査しない。
//
// 【整形になる理由】
//   ・Affine は 12 自由度の最小二乗。壊れた高周波成分は残差として落ちるので整形になる。
//   ・ThinPlateSpline は lambda（K 行列の対角）を大きく取るほど平滑化が効き、
//     原型の形へ寄る。lambda が 0 に近いと補間になり、現在の（壊れた）形をなぞるだけで
//     整形にならない。lambda の値はモデルの寸法に依存するため既定値は目安でしかない。
//
// 【座標】原型・部品とも Vertices[v].Position（オブジェクトローカル）だけを扱う。
//   配置時のフレームと倍率は推定した変換の中に現れるため、座標系合わせは不要。
//   BoneTransform / WorldMatrix / BindPose は使わない。
//
// 【読み書きの分離】パーツごとに独立して読み → 書きを行う。
//   1 パーツの書き込みが他パーツの入力に混ざることはない。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Numerics;

namespace Poly_Ling.Ops
{
    /// <summary>変換係数の推定方式。</summary>
    public enum PlaceObjectReshapeMode
    {
        /// <summary>アフィン変換（最小二乗）。</summary>
        Affine = 0,

        /// <summary>薄板スプライン。lambda で平滑化の強さを決める。</summary>
        ThinPlateSpline = 1,
    }

    /// <summary>藤壺の部品を原型の形へ張り直す。</summary>
    public static class PlaceObjectReshapeOps
    {
        /// <summary>推定に必要な最小頂点数。アフィン・薄板スプラインとも 4。</summary>
        public const int MinimumVertexCount = 4;

        // ================================================================
        // パーツの走査
        // ================================================================

        /// <summary>
        /// パーツIDごとの頂点インデックスを、メッシュ内の並び順で集める。
        /// null 頂点は入れない。
        /// </summary>
        public static Dictionary<int, List<int>> CollectParts(MeshObject mo)
        {
            var map = new Dictionary<int, List<int>>();
            if (mo?.Vertices == null) return map;

            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                if (!map.TryGetValue(v.PartsId, out var list))
                {
                    list = new List<int>();
                    map[v.PartsId] = list;
                }
                list.Add(i);
            }
            return map;
        }

        /// <summary>指定した頂点が属するパーツIDを昇順で集める。</summary>
        public static SortedSet<int> CollectPartsIdsOfVertices(MeshObject mo, IEnumerable<int> vertexIndices)
        {
            var ids = new SortedSet<int>();
            if (mo?.Vertices == null || vertexIndices == null) return ids;

            foreach (int i in vertexIndices)
            {
                if (i < 0 || i >= mo.Vertices.Count) continue;
                var v = mo.Vertices[i];
                if (v == null) continue;
                ids.Add(v.PartsId);
            }
            return ids;
        }

        /// <summary>パーツIDの集合を「1,3,5」形式の文字列にする。PipeSmoothOps.ParseTargets で読み戻せる。</summary>
        public static string FormatPartsIds(IEnumerable<int> ids)
        {
            if (ids == null) return "";

            var sb    = new StringBuilder();
            bool first = true;
            foreach (int id in ids)
            {
                if (!first) sb.Append(',');
                sb.Append(id);
                first = false;
            }
            return sb.ToString();
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// メッシュ内の対象パーツを原型の形へ張り直す。
        /// </summary>
        /// <param name="mo">対象メッシュ。頂点位置だけを書き換える。</param>
        /// <param name="prototype">原型の頂点位置。並び順が対応の鍵。</param>
        /// <param name="prototypeSubIds">
        /// 原型の頂点が持つサブID。サブID検査に使う。null なら検査しない。
        /// prototype と同数であること。
        /// </param>
        /// <param name="targetPartsIds">対象のパーツID。null なら全パーツ。</param>
        /// <param name="mode">推定方式。</param>
        /// <param name="lambda">薄板スプラインの平滑化係数。Affine では使わない。</param>
        /// <param name="okParts">張り直したパーツ数。</param>
        /// <param name="movedVertices">位置が変わった頂点数。</param>
        /// <param name="partFailures">飛ばしたパーツの理由。</param>
        /// <param name="reason">1 パーツも処理できなかったときの理由。</param>
        /// <returns>1 パーツ以上を張り直せたら true。</returns>
        public static bool Execute(
            MeshObject mo,
            IReadOnlyList<Vector3> prototype,
            IReadOnlyList<int> prototypeSubIds,
            HashSet<int> targetPartsIds,
            PlaceObjectReshapeMode mode,
            float lambda,
            out int okParts,
            out int movedVertices,
            out List<string> partFailures,
            out string reason)
        {
            okParts       = 0;
            movedVertices = 0;
            partFailures  = new List<string>();
            reason        = null;

            if (mo?.Vertices == null || mo.Vertices.Count == 0)
            {
                reason = "頂点がありません";
                return false;
            }
            if (prototype == null || prototype.Count < MinimumVertexCount)
            {
                reason = $"原型の頂点が {MinimumVertexCount} 個未満です";
                return false;
            }
            if (prototypeSubIds != null && prototypeSubIds.Count != prototype.Count)
            {
                reason = "原型の頂点数とサブID数が違います";
                return false;
            }

            var parts = CollectParts(mo);
            if (parts.Count == 0)
            {
                reason = "パーツがありません";
                return false;
            }

            // パーツIDの昇順で処理する。結果が Dictionary の列挙順に依存しないようにする。
            var partIdList = new List<int>(parts.Keys);
            partIdList.Sort();

            int examined = 0;

            foreach (int pid in partIdList)
            {
                if (targetPartsIds != null && !targetPartsIds.Contains(pid)) continue;
                examined++;

                var indices = parts[pid];
                if (!ReshapeOnePart(mo, prototype, prototypeSubIds, indices, mode, lambda,
                                    out int moved, out string why))
                {
                    partFailures.Add($"パーツ {pid}: {why}");
                    continue;
                }

                okParts++;
                movedVertices += moved;
            }

            if (examined == 0)
            {
                reason = targetPartsIds != null
                    ? "指定したパーツIDがこのオブジェクトにありません"
                    : "パーツがありません";
                return false;
            }
            if (okParts == 0)
            {
                reason = partFailures.Count > 0
                    ? string.Join(" / ", partFailures)
                    : "張り直せるパーツがありません";
                return false;
            }
            return true;
        }

        // ================================================================
        // 1 パーツぶん
        // ================================================================

        private static bool ReshapeOnePart(
            MeshObject mo,
            IReadOnlyList<Vector3> prototype,
            IReadOnlyList<int> prototypeSubIds,
            List<int> indices,
            PlaceObjectReshapeMode mode,
            float lambda,
            out int movedVertices,
            out string reason)
        {
            movedVertices = 0;
            reason        = null;

            int n = indices.Count;
            if (n != prototype.Count)
            {
                reason = $"頂点数が違います（パーツ {n} / 原型 {prototype.Count}）";
                return false;
            }
            if (n < MinimumVertexCount)
            {
                reason = $"頂点が {MinimumVertexCount} 個未満です";
                return false;
            }
            if (!CheckSubIdOrder(mo, prototypeSubIds, indices, out string subReason))
            {
                reason = subReason;
                return false;
            }

            // AFTER = 現在の（壊れているかもしれない）位置。
            var after = new Vector3[n];
            for (int k = 0; k < n; k++) after[k] = mo.Vertices[indices[k]].Position;

            Vector3[] result = new Vector3[n];

            if (mode == PlaceObjectReshapeMode.ThinPlateSpline)
            {
                var tps = new PLThinPlateSpline3D();
                if (!tps.Solve(prototype, after, lambda))
                {
                    reason = "薄板スプラインの係数を求められません（対応点が特異）";
                    return false;
                }
                for (int k = 0; k < n; k++) result[k] = tps.WarpPoint(prototype[k]);
            }
            else
            {
                if (!PLAffineEstimator.TryEstimate(prototype, after, out Matrix4x4 affine))
                {
                    reason = "アフィン変換を推定できません（対応点が同一平面か特異）";
                    return false;
                }
                for (int k = 0; k < n; k++) result[k] = affine.MultiplyPoint3x4(prototype[k]);
            }

            for (int k = 0; k < n; k++)
            {
                var v  = mo.Vertices[indices[k]];
                var np = result[k];
                if (np.x != v.Position.x || np.y != v.Position.y || np.z != v.Position.z)
                    movedVertices++;
                v.Position = np;
            }

            return true;
        }

        /// <summary>
        /// サブIDの並びを検査する。
        /// パーツ側・原型側それぞれのサブIDが 0..N-1 の順列になっているときだけ、
        /// 並び順（0,1,2…）と一致するかを見る。順列でなければ検査しない。
        /// </summary>
        private static bool CheckSubIdOrder(
            MeshObject mo, IReadOnlyList<int> prototypeSubIds, List<int> indices, out string reason)
        {
            reason = null;
            int n  = indices.Count;

            var partSubIds = new int[n];
            for (int k = 0; k < n; k++) partSubIds[k] = mo.Vertices[indices[k]].SubId;

            if (IsPermutationOfRange(partSubIds, n))
            {
                for (int k = 0; k < n; k++)
                {
                    if (partSubIds[k] == k) continue;
                    reason = $"サブIDの並びが頂点の並び順と一致しません（{k} 番目のサブIDが {partSubIds[k]}）";
                    return false;
                }
            }

            if (prototypeSubIds != null)
            {
                var protoSubIds = new int[n];
                for (int k = 0; k < n; k++) protoSubIds[k] = prototypeSubIds[k];

                if (IsPermutationOfRange(protoSubIds, n))
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (protoSubIds[k] == k) continue;
                        reason = $"原型のサブIDの並びが頂点の並び順と一致しません（{k} 番目のサブIDが {protoSubIds[k]}）";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>values が 0..n-1 の順列かどうか。</summary>
        private static bool IsPermutationOfRange(int[] values, int n)
        {
            var seen = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int s = values[k];
                if (s < 0 || s >= n) return false;
                if (seen[s]) return false;
                seen[s] = true;
            }
            return true;
        }
    }
}
