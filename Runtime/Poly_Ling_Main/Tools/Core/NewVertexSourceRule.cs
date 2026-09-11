// NewVertexSourceRule.cs
// 既存メッシュへ新しい頂点を足すときの「ウェイト継承元」と「座標の基準」の規則。
// Runtime/Poly_Ling_Main/Tools/Core/ に配置
//
// 【由来】
//   AddFaceTool.CreateFace の私有メソッドだったものを切り出した。
//   点指定図形（PointDefinedToolHandler）も同じ規則で頂点を足すため、
//   規則を 1 か所に置く。AddFaceTool の挙動は変えていない。
//
// 【規則】（AddFaceTool.CreateFace の「ウェイト継承ルール」と同じ）
//   (A) 既存頂点が 1 つ以上使われている場合
//       生成する形の辺をたどった段数が最小の既存頂点からコピーする。
//       3D の直線距離ではなく、辺をたどった段数で決める。
//   (B) すべて新規点の場合
//       メッシュ内の既存頂点のうち、ワールド空間で最も近いものからコピーする。
//       座標は GPU 値（ctx.GetVertexWorldPosition）を使う。
//   座標の基準も継承元に揃える（RebasePositionToSource）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    public static class NewVertexSourceRule
    {
        /// <summary>
        /// 規則 (A)。生成する多角形の環に沿った段数が最小の既存頂点を返す。
        /// 段数は min(|i-j|, n-|i-j|)。同数のときは点列中で先に現れる方を選ぶ。
        /// 既存頂点が 1 つも無い場合は -1。
        /// </summary>
        public static int FindSourceByRingDistance(bool[] wasExisting, int[] existingIdx, int pointIndex)
        {
            int n = wasExisting.Length;
            if (n == 0) return -1;

            int best = -1;
            int bestStep = int.MaxValue;

            for (int j = 0; j < n; j++)
            {
                if (!wasExisting[j]) continue;

                int diff = Mathf.Abs(pointIndex - j);
                int step = Mathf.Min(diff, n - diff);

                if (step < bestStep)
                {
                    bestStep = step;
                    best = existingIdx[j];
                }
            }

            return best;
        }

        /// <summary>
        /// 規則 (A) を面の並びへ広げたもの。環 1 本ではなく、面の辺をたどった段数で決める。
        /// 環 1 本の面に対しては FindSourceByRingDistance と同じ結果になる。
        ///
        /// 既存頂点を持つ枠から同時に幅優先で広げ、各枠へ最初に届いた既存頂点を返す。
        /// 同じ段数で届く候補が複数あるときは、枠番号の小さい既存頂点から届いた方になる。
        /// </summary>
        /// <param name="slotCount">枠の数。</param>
        /// <param name="faces">枠番号で表した面の並び。</param>
        /// <param name="existingOfSlot">枠ごとの既存頂点番号。新規の枠は -1。</param>
        /// <returns>枠ごとの継承元の既存頂点番号。既存頂点に届かない枠は -1。</returns>
        public static int[] FindSourcesByEdgeSteps(
            int slotCount, IReadOnlyList<int[]> faces, IReadOnlyList<int> existingOfSlot)
        {
            var src = new int[slotCount];
            for (int i = 0; i < slotCount; i++) src[i] = -1;
            if (slotCount == 0 || faces == null || existingOfSlot == null) return src;

            var adj = new List<int>[slotCount];
            for (int i = 0; i < slotCount; i++) adj[i] = new List<int>();
            foreach (var f in faces)
            {
                if (f == null) continue;
                int n = f.Length;
                for (int k = 0; k < n; k++)
                {
                    int a = f[k], b = f[(k + 1) % n];
                    if (a < 0 || b < 0 || a >= slotCount || b >= slotCount || a == b) continue;
                    adj[a].Add(b);
                    adj[b].Add(a);
                }
            }

            var q = new Queue<int>();
            for (int i = 0; i < slotCount; i++)
            {
                if (existingOfSlot[i] < 0) continue;
                src[i] = existingOfSlot[i];
                q.Enqueue(i);
            }

            while (q.Count > 0)
            {
                int v = q.Dequeue();
                foreach (int w in adj[v])
                {
                    if (src[w] >= 0) continue;
                    src[w] = src[v];
                    q.Enqueue(w);
                }
            }

            return src;
        }

        /// <summary>
        /// 規則 (B)。メッシュ内の既存頂点のうちワールド空間で最も近いものを返す。
        /// 比較対象は今回の呼び出しで追加する前の頂点のみ（originalVertexCount 未満）。
        /// 座標は GPU が計算した値（ctx.GetVertexWorldPosition）を優先する。
        /// 頂点が 1 つも無い場合は -1。
        /// </summary>
        public static int FindSourceByWorldDistance(
            ToolContext ctx, MeshObject meshObject, int originalVertexCount, Vector3 newLocalPos)
        {
            if (originalVertexCount <= 0) return -1;

            Matrix4x4 meshMat = ctx.ActiveWorldMatrix;
            Vector3 targetWorld = meshMat.MultiplyPoint3x4(newLocalPos);

            int best = -1;
            float bestSqr = float.MaxValue;

            for (int vi = 0; vi < originalVertexCount && vi < meshObject.Vertices.Count; vi++)
            {
                Vector3 w;
                var gpu = ctx.GetVertexWorldPosition?.Invoke(vi);
                if (gpu.HasValue) w = gpu.Value;
                else               w = meshMat.MultiplyPoint3x4(meshObject.Vertices[vi].Position);

                float sqr = (w - targetWorld).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = vi; }
            }

            return best;
        }

        /// <summary>
        /// localPos（メッシュの WorldMatrix 基準のローカル座標）を、
        /// 継承元頂点と同じ基準のローカル座標へ入れ直す。
        /// ActiveWorldMatrix で一度ワールドへ戻し、継承元の行列の逆で戻す。
        /// 継承元が無い、または継承元が BoneWeight を持たない場合は変換しない。
        /// </summary>
        public static Vector3 RebasePositionToSource(
            ToolContext ctx, MeshObject meshObject, int srcVertex, Vector3 localPos)
        {
            if (srcVertex < 0 || srcVertex >= meshObject.Vertices.Count) return localPos;

            var srcVtx = meshObject.Vertices[srcVertex];
            if (srcVtx == null || !srcVtx.HasBoneWeight) return localPos;

            Vector3 world = ctx.ActiveWorldMatrix.MultiplyPoint3x4(localPos);
            return ctx.ActiveVertexMatrix(srcVertex).inverse.MultiplyPoint3x4(world);
        }
    }
}
