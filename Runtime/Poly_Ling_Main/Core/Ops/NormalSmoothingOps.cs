// NormalSmoothingOps.cs
// スムージング角（メタセコイアの facet 相当）による頂点法線の再構築。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【この実装が従来と違う点】
//   1. 面法線を Newell 法で求める（先頭3頂点固定ではないので N 角形でも安定）。
//   2. 「辺を共有する隣接面」だけを連結対象にし、角度が閾値以内の面同士を
//      union-find で連結してスムージンググループを作る。
//      頂点に触れているだけで辺を共有しない面は平均に入らない。
//   3. グループごとに別々の法線を持てるよう、UV/法線スロットを割り当て直す。
//      これにより「同じUVだが法線が違う」= ハードエッジを表現できる。
//
// 【不変条件（厳守）】
//   ・Vertex.UVs.Count == Vertex.Normals.Count
//   ・Face.UVIndices[j] == Face.NormalIndices[j]
//   スロット確保は Vertex.GetOrAddUVNormal のみを使う。
//   GetOrAddUV / GetOrAddNormal / AddUV / AddNormal は呼ばない。
//   処理の最後に ValidateSlotInvariant で検証し、違反があれば LogError を出す
//   （黙って EnsureNormalSlots で繕わない）。
//
// 【副作用】
//   ハードエッジの分だけスロットが増えるため、展開頂点数
//   （MQOVertexExpandHelper.CalculateExpandedVertexCount / GPU 展開バッファ /
//   Unity Mesh 頂点数）が従来より増える。従来の挙動が必要な場合は
//   呼び出し側で従来経路（NormalMode.Smooth など）を選ぶこと。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// 面法線を平均する際の重み付け方式。
    /// Uniform 以外は面ごとに係数を掛けてから合算する。
    /// </summary>
    public enum NormalWeightMode
    {
        /// <summary>均等（面ごとの重み1）。</summary>
        Uniform,
        /// <summary>コーナー角（その頂点での面の開き角）で重み付け。</summary>
        CornerAngle,
        /// <summary>面積で重み付け。</summary>
        FaceArea,
        /// <summary>コーナー角 × 面積で重み付け。</summary>
        AngleAndArea,
    }

    public static class NormalSmoothingOps
    {
        /// <summary>PolyLing が MQO へ書き出す既定の facet 値。</summary>
        public const float DefaultFacetAngle = 59.5f;

        /// <summary>スロット同一視のトレランス（Vertex.GetOrAddUVNormal と同じ既定値）。</summary>
        private const float SlotTolerance = 0.0001f;

        // ================================================================
        // 面法線（Newell 法）
        // ================================================================

        /// <summary>
        /// Newell 法で面法線を求める。向きの符号は
        /// NormalHelper.CalculateFaceNormal(p0,p1,p2) と一致する。
        /// 縮退面は Vector3.up を返す。
        /// </summary>
        public static Vector3 CalculateFaceNormalNewell(MeshObject mesh, Face face)
        {
            if (mesh == null || face == null || face.VertexCount < 3)
                return Vector3.up;

            int n = face.VertexCount;
            float nx = 0f, ny = 0f, nz = 0f;

            for (int i = 0; i < n; i++)
            {
                int ia = face.VertexIndices[i];
                int ib = face.VertexIndices[(i + 1) % n];

                if (ia < 0 || ia >= mesh.Vertices.Count) return Vector3.up;
                if (ib < 0 || ib >= mesh.Vertices.Count) return Vector3.up;

                Vector3 p = mesh.Vertices[ia].Position;
                Vector3 q = mesh.Vertices[ib].Position;

                nx += (p.y - q.y) * (p.z + q.z);
                ny += (p.z - q.z) * (p.x + q.x);
                nz += (p.x - q.x) * (p.y + q.y);
            }

            Vector3 normal = new Vector3(nx, ny, nz);
            if (normal.sqrMagnitude < 1e-16f)
                return Vector3.up;

            return normal.normalized;
        }

        // ================================================================
        // スムージング本体
        // ================================================================

        /// <summary>
        /// 面コーナーごとの UV を外部から受け取って法線とスロットを再構築する。
        /// MQO フルインポート用（スロット未確保の状態で呼ぶ）。
        /// </summary>
        /// <param name="faceCornerUVs">
        /// MeshObject.Faces と同じ添字。null 要素は対象外（補助線など）。
        /// </param>
        public static void ApplyFacetSmoothing(
            MeshObject mesh,
            IReadOnlyList<Vector2[]> faceCornerUVs,
            float smoothingAngleDeg,
            bool flatShading,
            string debugContext = null,
            NormalWeightMode weightMode = NormalWeightMode.Uniform)
        {
            if (mesh == null) return;
            Rebuild(mesh, faceCornerUVs, smoothingAngleDeg, flatShading, debugContext, weightMode);
        }

        /// <summary>
        /// 既存スロットから面コーナーの UV を吸い出したうえで、法線とスロットを
        /// 再構築する。部分インポートなど、既に UV が割り当て済みの場合に使う。
        /// </summary>
        public static void ApplyFacetSmoothing(
            MeshObject mesh,
            float smoothingAngleDeg,
            bool flatShading,
            string debugContext = null,
            NormalWeightMode weightMode = NormalWeightMode.Uniform)
        {
            if (mesh == null) return;

            var cornerUVs = CaptureCornerUVs(mesh);
            Rebuild(mesh, cornerUVs, smoothingAngleDeg, flatShading, debugContext, weightMode);
        }

        /// <summary>
        /// 現在の UVIndices が指すスロットから、面コーナーごとの UV を取り出す。
        /// 3頂点未満の面は null（対象外）。
        /// </summary>
        public static Vector2[][] CaptureCornerUVs(MeshObject mesh)
        {
            if (mesh == null) return new Vector2[0][];

            var result = new Vector2[mesh.Faces.Count][];

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3)
                {
                    result[fi] = null;
                    continue;
                }

                var uvs = new Vector2[face.VertexCount];
                for (int j = 0; j < face.VertexCount; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= mesh.Vertices.Count)
                    {
                        uvs[j] = Vector2.zero;
                        continue;
                    }

                    var vertex = mesh.Vertices[vIdx];
                    int uvIdx = (j < face.UVIndices.Count) ? face.UVIndices[j] : 0;

                    uvs[j] = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                        ? vertex.UVs[uvIdx]
                        : Vector2.zero;
                }

                result[fi] = uvs;
            }

            return result;
        }

        // ================================================================
        // 内部実装
        // ================================================================

        private static void Rebuild(
            MeshObject mesh,
            IReadOnlyList<Vector2[]> faceCornerUVs,
            float smoothingAngleDeg,
            bool flatShading,
            string debugContext,
            NormalWeightMode weightMode)
        {
            int faceCount = mesh.Faces.Count;
            if (faceCount == 0)
            {
                NormalizeSlotCounts(mesh);
                return;
            }

            // --- 対象面の判定とコーナー番号の割り当て ---
            var isTarget = new bool[faceCount];
            var cornerOffset = new int[faceCount];
            int totalCorners = 0;

            for (int fi = 0; fi < faceCount; fi++)
            {
                var face = mesh.Faces[fi];
                bool hasUV = faceCornerUVs != null
                             && fi < faceCornerUVs.Count
                             && faceCornerUVs[fi] != null;

                isTarget[fi] = face != null && face.VertexCount >= 3 && hasUV;

                cornerOffset[fi] = totalCorners;
                if (isTarget[fi])
                    totalCorners += face.VertexCount;
            }

            if (totalCorners == 0)
            {
                NormalizeSlotCounts(mesh);
                ValidateSlotInvariant(mesh, debugContext);
                return;
            }

            // --- 面法線 ---
            var faceNormals = new Vector3[faceCount];
            for (int fi = 0; fi < faceCount; fi++)
            {
                faceNormals[fi] = isTarget[fi]
                    ? CalculateFaceNormalNewell(mesh, mesh.Faces[fi])
                    : Vector3.up;
            }

            // --- コーナー → 面 の逆引き ---
            var cornerFace = new int[totalCorners];
            for (int fi = 0; fi < faceCount; fi++)
            {
                if (!isTarget[fi]) continue;
                var face = mesh.Faces[fi];
                for (int j = 0; j < face.VertexCount; j++)
                    cornerFace[cornerOffset[fi] + j] = fi;
            }

            // --- スムージンググループ（辺共有の隣接面のみ連結） ---
            var parent = new int[totalCorners];
            for (int i = 0; i < totalCorners; i++) parent[i] = i;

            if (!flatShading)
            {
                float cosThreshold = Mathf.Cos(Mathf.Clamp(smoothingAngleDeg, 0f, 180f) * Mathf.Deg2Rad);
                BuildSmoothingGroups(mesh, isTarget, cornerOffset, faceNormals, cosThreshold, parent);
            }

            // --- グループごとの平均法線（コーナー単位の重みを掛けて合算） ---
            var groupSum = new Vector3[totalCorners];
            for (int c = 0; c < totalCorners; c++)
            {
                int fi = cornerFace[c];
                int corner = c - cornerOffset[fi];
                float w = CornerWeight(mesh, mesh.Faces[fi], corner, weightMode);
                groupSum[Find(parent, c)] += faceNormals[fi] * w;
            }

            // --- スロットを作り直す ---
            foreach (var vertex in mesh.Vertices)
            {
                vertex.UVs.Clear();
                vertex.Normals.Clear();
            }

            for (int fi = 0; fi < faceCount; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null) continue;

                if (!isTarget[fi])
                {
                    // 補助線など。UV/法線の添字は一致だけ保証する。
                    for (int j = 0; j < face.NormalIndices.Count && j < face.UVIndices.Count; j++)
                        face.NormalIndices[j] = face.UVIndices[j];
                    continue;
                }

                var cornerUVs = faceCornerUVs[fi];

                while (face.UVIndices.Count < face.VertexCount) face.UVIndices.Add(0);
                while (face.NormalIndices.Count < face.VertexCount) face.NormalIndices.Add(0);

                for (int j = 0; j < face.VertexCount; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= mesh.Vertices.Count)
                    {
                        face.UVIndices[j] = 0;
                        face.NormalIndices[j] = 0;
                        continue;
                    }

                    Vector2 uv = (j < cornerUVs.Length) ? cornerUVs[j] : Vector2.zero;

                    int root = Find(parent, cornerOffset[fi] + j);
                    Vector3 normal = groupSum[root];
                    normal = (normal.sqrMagnitude < 1e-12f)
                        ? faceNormals[fi]
                        : normal.normalized;

                    // 不変条件を保つ唯一の追加口
                    int slot = mesh.Vertices[vIdx].GetOrAddUVNormal(uv, normal, SlotTolerance);

                    face.UVIndices[j] = slot;
                    face.NormalIndices[j] = slot;
                }
            }

            NormalizeSlotCounts(mesh);
            ValidateSlotInvariant(mesh, debugContext);
        }

        /// <summary>
        /// 辺（頂点インデックス対）を共有する面同士を走査し、面法線の成す角が
        /// 閾値以内なら、その辺の両端でコーナーを連結する。
        /// </summary>
        private static void BuildSmoothingGroups(
            MeshObject mesh,
            bool[] isTarget,
            int[] cornerOffset,
            Vector3[] faceNormals,
            float cosThreshold,
            int[] parent)
        {
            // 辺キー → その辺を持つ (面, 低位側コーナー, 高位側コーナー)
            var edgeMap = new Dictionary<long, List<EdgeRef>>();

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                if (!isTarget[fi]) continue;

                var face = mesh.Faces[fi];
                int n = face.VertexCount;

                for (int j = 0; j < n; j++)
                {
                    int k = (j + 1) % n;
                    int va = face.VertexIndices[j];
                    int vb = face.VertexIndices[k];

                    if (va == vb) continue;
                    if (va < 0 || vb < 0) continue;

                    int lo, hi, cornerLo, cornerHi;
                    if (va < vb) { lo = va; hi = vb; cornerLo = j; cornerHi = k; }
                    else { lo = vb; hi = va; cornerLo = k; cornerHi = j; }

                    long key = ((long)lo << 32) | (uint)hi;

                    if (!edgeMap.TryGetValue(key, out var list))
                    {
                        list = new List<EdgeRef>(2);
                        edgeMap[key] = list;
                    }

                    list.Add(new EdgeRef
                    {
                        Face = fi,
                        CornerLo = cornerOffset[fi] + cornerLo,
                        CornerHi = cornerOffset[fi] + cornerHi
                    });
                }
            }

            foreach (var kvp in edgeMap)
            {
                var list = kvp.Value;
                if (list.Count < 2) continue;

                // 非多様体（3面以上が同じ辺を共有）でも全ペアを見る
                for (int a = 0; a < list.Count; a++)
                {
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        float dot = Vector3.Dot(faceNormals[list[a].Face], faceNormals[list[b].Face]);
                        if (dot < cosThreshold) continue;

                        Union(parent, list[a].CornerLo, list[b].CornerLo);
                        Union(parent, list[a].CornerHi, list[b].CornerHi);
                    }
                }
            }
        }

        private struct EdgeRef
        {
            public int Face;
            public int CornerLo;
            public int CornerHi;
        }

        /// <summary>
        /// 面コーナーの重みを求める。Uniform は 1、CornerAngle はその頂点での
        /// 面の開き角（ラジアン）、FaceArea は面積、AngleAndArea は両者の積。
        /// 退化して 0 以下になる場合は極小値でクランプする（重み全ゼロを避ける）。
        /// </summary>
        public static float CornerWeight(
            MeshObject mesh, Face face, int corner, NormalWeightMode mode)
        {
            if (mode == NormalWeightMode.Uniform) return 1f;
            if (mesh == null || face == null || face.VertexCount < 3) return 1f;

            float w = 1f;

            if (mode == NormalWeightMode.CornerAngle || mode == NormalWeightMode.AngleAndArea)
                w *= CornerAngle(mesh, face, corner);

            if (mode == NormalWeightMode.FaceArea || mode == NormalWeightMode.AngleAndArea)
                w *= FaceArea(mesh, face);

            return w > 1e-8f ? w : 1e-8f;
        }

        /// <summary>面 face のコーナー corner における開き角（ラジアン）。</summary>
        public static float CornerAngle(MeshObject mesh, Face face, int corner)
        {
            int n = face.VertexCount;
            if (n < 3) return 0f;

            int ic = face.VertexIndices[corner];
            int ip = face.VertexIndices[(corner - 1 + n) % n];
            int inx = face.VertexIndices[(corner + 1) % n];

            if (ic < 0 || ic >= mesh.Vertices.Count) return 0f;
            if (ip < 0 || ip >= mesh.Vertices.Count) return 0f;
            if (inx < 0 || inx >= mesh.Vertices.Count) return 0f;

            Vector3 a = mesh.Vertices[ip].Position - mesh.Vertices[ic].Position;
            Vector3 b = mesh.Vertices[inx].Position - mesh.Vertices[ic].Position;

            if (a.sqrMagnitude < 1e-16f || b.sqrMagnitude < 1e-16f) return 0f;

            float cos = Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f);
            return Mathf.Acos(cos);
        }

        /// <summary>N角形の面積（Newell ベクトルの長さの半分）。</summary>
        public static float FaceArea(MeshObject mesh, Face face)
        {
            int n = face.VertexCount;
            if (n < 3) return 0f;

            float nx = 0f, ny = 0f, nz = 0f;
            for (int i = 0; i < n; i++)
            {
                int ia = face.VertexIndices[i];
                int ib = face.VertexIndices[(i + 1) % n];
                if (ia < 0 || ia >= mesh.Vertices.Count) return 0f;
                if (ib < 0 || ib >= mesh.Vertices.Count) return 0f;

                Vector3 p = mesh.Vertices[ia].Position;
                Vector3 q = mesh.Vertices[ib].Position;

                nx += (p.y - q.y) * (p.z + q.z);
                ny += (p.z - q.z) * (p.x + q.x);
                nz += (p.x - q.x) * (p.y + q.y);
            }

            return new Vector3(nx, ny, nz).magnitude * 0.5f;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb) return;

            if (ra < rb) parent[rb] = ra;
            else parent[ra] = rb;
        }

        // ================================================================
        // 不変条件ユーティリティ
        // ================================================================

        /// <summary>
        /// UV スロット数と法線スロット数を揃える。
        /// UV スロットが無い頂点（補助線のみ・孤立頂点）は法線も持たせない。
        /// </summary>
        public static void NormalizeSlotCounts(MeshObject mesh)
        {
            if (mesh == null) return;

            foreach (var vertex in mesh.Vertices)
            {
                if (vertex == null) continue;

                if (vertex.UVs.Count == 0)
                {
                    vertex.Normals.Clear();
                    continue;
                }

                vertex.EnsureNormalSlots();
            }
        }

        /// <summary>
        /// 不変条件（UVs.Count == Normals.Count / UVIndices[j] == NormalIndices[j]）を検証する。
        /// 違反件数を返し、1件以上なら LogError を出す。自動修復はしない。
        /// </summary>
        public static int ValidateSlotInvariant(MeshObject mesh, string context = null)
        {
            if (mesh == null) return 0;

            int vertexViolations = 0;
            int faceViolations = 0;
            int rangeViolations = 0;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];
                if (vertex == null) continue;
                if (vertex.UVs.Count != vertex.Normals.Count)
                    vertexViolations++;
            }

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null) continue;

                int n = Mathf.Min(face.UVIndices.Count, face.NormalIndices.Count);
                for (int j = 0; j < n; j++)
                {
                    if (face.UVIndices[j] != face.NormalIndices[j])
                        faceViolations++;
                }

                if (face.VertexCount < 3) continue;

                for (int j = 0; j < face.VertexCount && j < face.UVIndices.Count; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= mesh.Vertices.Count) continue;

                    int slot = face.UVIndices[j];
                    if (slot < 0 || slot >= mesh.Vertices[vIdx].UVs.Count)
                        rangeViolations++;
                }
            }

            int total = vertexViolations + faceViolations + rangeViolations;
            if (total > 0)
            {
                string name = string.IsNullOrEmpty(context) ? (mesh.Name ?? "?") : context;
                Debug.LogError(
                    $"[NormalSmoothingOps] スロット不変条件違反 mesh=\"{name}\" " +
                    $"vertex(UV!=Normal)={vertexViolations} face(UVIdx!=NormalIdx)={faceViolations} " +
                    $"slotOutOfRange={rangeViolations}");
            }

            return total;
        }
    }
}
