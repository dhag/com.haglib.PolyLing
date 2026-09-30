// UvUnwrapOps.cs
// UV展開ユーティリティ
// PolyLing_CommandHandlers_UV.cs から分離（CommandHandlers削除に伴い独立ファイル化）
//
// 【書き込みは角ごと・スロット規約を守る】（MeshBridgeDefault：UVs.Count == Normals.Count、
//   面の UVIndices[k] == NormalIndices[k]）
//   どの投影も「角 → UV」を求め、WriteCornerUVs で書く。
//   (頂点, 新しい UV, 今のスロットの法線) が同じ角を 1 つのスロットにまとめ、
//   今のスロットが空いていれば使い、空いていなければ UV と法線の組を足す。
//   使われなくなったスロットは VertexSlotOps.CompactUnusedSlots で詰める。
//   以前は UVs[0] だけを書いて UVIndices を 0 にしていたため、NormalIndices と食い違っていた。
//
// 【ビューからの投影】画面への投影は Player 側（カメラ・下絵の位置を持つ側）で頂点ごとの UV を求め、
//   ApplyVertexUVs で書く（PolyLingPlayerViewerCore.ViewUvProjection.cs）。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Ops
{
    internal static class UvUnwrapOps
    {
        // ----------------------------------------------------------------
        // UV展開
        // ----------------------------------------------------------------

        public static void UnwrapMesh(MeshObject meshObj, ProjectionType proj,
            float scale, float offsetU, float offsetV)
        {
            if (meshObj.VertexCount == 0 || meshObj.FaceCount == 0) return;

            Bounds bounds = meshObj.CalculateBounds();

            switch (proj)
            {
                case ProjectionType.PlanarXY:
                    UnwrapPlanar(meshObj, bounds, 0, 1, scale, offsetU, offsetV); break;
                case ProjectionType.PlanarXZ:
                    UnwrapPlanar(meshObj, bounds, 0, 2, scale, offsetU, offsetV); break;
                case ProjectionType.PlanarYZ:
                    UnwrapPlanar(meshObj, bounds, 1, 2, scale, offsetU, offsetV); break;
                case ProjectionType.Box:
                    UnwrapBox(meshObj, bounds, scale, offsetU, offsetV); break;
                case ProjectionType.Cylindrical:
                    UnwrapCylindrical(meshObj, bounds, scale, offsetU, offsetV); break;
                case ProjectionType.Spherical:
                    UnwrapSpherical(meshObj, bounds, scale, offsetU, offsetV); break;
                // View は画面が要るので Player 側で求める（ApplyVertexUVs）。
            }
        }

        /// <summary>頂点ごとの UV（uvs[頂点番号]）を、スロット規約を守って書く。</summary>
        public static void ApplyVertexUVs(MeshObject meshObj, Vector2[] uvs)
        {
            if (meshObj == null || uvs == null || uvs.Length < meshObj.VertexCount) return;
            WriteCornerUVs(meshObj, (face, k) => uvs[face.VertexIndices[k]]);
        }

        private static void UnwrapPlanar(MeshObject meshObj, Bounds bounds,
            int axisU, int axisV, float scale, float offsetU, float offsetV)
        {
            Vector3 bMin = bounds.min;
            float sizeU  = bounds.size[axisU]; if (sizeU < 0.0001f) sizeU = 1f;
            float sizeV  = bounds.size[axisV]; if (sizeV < 0.0001f) sizeV = 1f;

            var uvs = new Vector2[meshObj.VertexCount];
            for (int i = 0; i < uvs.Length; i++)
            {
                var p = meshObj.Vertices[i].Position;
                uvs[i] = new Vector2(
                    (p[axisU] - bMin[axisU]) / sizeU * scale + offsetU,
                    (p[axisV] - bMin[axisV]) / sizeV * scale + offsetV);
            }
            ApplyVertexUVs(meshObj, uvs);
        }

        private static void UnwrapBox(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 bMin  = bounds.min;
            Vector3 bSize = bounds.size;

            // 面ごとの投影軸（主軸）。
            var axesOf = new Dictionary<Face, (int u, int v)>();
            foreach (var face in meshObj.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                switch (GetDominantAxis(ComputeFaceNormal(meshObj, face)))
                {
                    case 0:  axesOf[face] = (1, 2); break;
                    case 1:  axesOf[face] = (0, 2); break;
                    default: axesOf[face] = (0, 1); break;
                }
            }

            WriteCornerUVs(meshObj, (face, k) =>
            {
                var (axisU, axisV) = axesOf[face];
                float sizeU = bSize[axisU]; if (sizeU < 0.0001f) sizeU = 1f;
                float sizeV = bSize[axisV]; if (sizeV < 0.0001f) sizeV = 1f;
                var p = meshObj.Vertices[face.VertexIndices[k]].Position;
                return new Vector2(
                    (p[axisU] - bMin[axisU]) / sizeU * scale + offsetU,
                    (p[axisV] - bMin[axisV]) / sizeV * scale + offsetV);
            });
        }

        private static void UnwrapCylindrical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;
            float height   = bounds.size.y; if (height < 0.0001f) height = 1f;
            float bMinY    = bounds.min.y;

            var uvs = new Vector2[meshObj.VertexCount];
            for (int i = 0; i < uvs.Length; i++)
            {
                var p = meshObj.Vertices[i].Position;
                float dx = p.x - center.x;
                float dz = p.z - center.z;
                uvs[i] = new Vector2(
                    ((Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI)) * scale + offsetU,
                    ((p.y - bMinY) / height) * scale + offsetV);
            }
            ApplyVertexUVs(meshObj, uvs);
        }

        private static void UnwrapSpherical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;

            var uvs = new Vector2[meshObj.VertexCount];
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector3 dir = (meshObj.Vertices[i].Position - center).normalized;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                uvs[i] = new Vector2(
                    ((Mathf.Atan2(dir.z, dir.x) + Mathf.PI) / (2f * Mathf.PI)) * scale + offsetU,
                    (Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f) * scale + offsetV);
            }
            ApplyVertexUVs(meshObj, uvs);
        }

        // ----------------------------------------------------------------
        // 角ごとの UV の書き込み（冒頭の【書き込みは角ごと・スロット規約を守る】）
        // ----------------------------------------------------------------

        private readonly struct CornerKey : IEquatable<CornerKey>
        {
            public readonly int     Vi;
            public readonly Vector2 Uv;
            public readonly Vector3 Normal;
            public CornerKey(int vi, Vector2 uv, Vector3 n) { Vi = vi; Uv = uv; Normal = n; }
            public bool Equals(CornerKey o) => Vi == o.Vi && Uv.Equals(o.Uv) && Normal.Equals(o.Normal);
            public override bool Equals(object obj) => obj is CornerKey o && Equals(o);
            public override int GetHashCode()
            {
                unchecked { return (Vi * 397 ^ Uv.GetHashCode()) * 397 ^ Normal.GetHashCode(); }
            }
        }

        /// <summary>
        /// 3 頂点以上の面の全ての角へ uvOf(面, 角) の UV を書く。線分（2 頂点以下）は触らない。
        /// </summary>
        private static void WriteCornerUVs(MeshObject mo, Func<Face, int, Vector2> uvOf)
        {
            int vc = mo.VertexCount;

            // 対象の角を集める。
            var corners = new List<(Face face, int k, CornerKey key, int slot)>();
            var touched = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= vc) continue;
                    touched.Add(vi);
                }
            }

            // 数の食い違いを先に直す（UV と法線を同じ数にそろえる）。
            foreach (int vi in touched)
            {
                var v = mo.Vertices[vi];
                while (v.UVs.Count < v.Normals.Count) v.UVs.Add(v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero);
                v.EnsureNormalSlots();
            }

            var usedBefore = new Dictionary<int, HashSet<int>>();
            var candidates = new Dictionary<CornerKey, List<int>>();
            var order      = new List<CornerKey>();
            foreach (var face in mo.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                for (int k = 0; k < face.VertexCount; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= vc) continue;
                    var v = mo.Vertices[vi];
                    int slot = k < face.UVIndices.Count ? face.UVIndices[k] : 0;
                    bool valid = slot >= 0 && slot < v.UVs.Count;
                    Vector3 n = valid && slot < v.Normals.Count ? v.Normals[slot]
                              : (v.Normals.Count > 0 ? v.Normals[0] : Vector3.up);
                    var key = new CornerKey(vi, uvOf(face, k), n);
                    corners.Add((face, k, key, slot));

                    if (!candidates.TryGetValue(key, out var list))
                    {
                        candidates[key] = list = new List<int>();
                        order.Add(key);
                    }
                    if (valid && !list.Contains(slot)) list.Add(slot);
                    if (valid)
                    {
                        if (!usedBefore.TryGetValue(vi, out var ub)) usedBefore[vi] = ub = new HashSet<int>();
                        ub.Add(slot);
                    }
                }
            }

            // まとまりごとにスロットを決める。今のスロットのうち、まだどのまとまりも取っていないものを使う。
            var assigned = new Dictionary<CornerKey, int>();
            var claimed  = new HashSet<(int vi, int slot)>();
            foreach (var key in order)
            {
                int chosen = -1;
                foreach (int c in candidates[key])
                {
                    if (claimed.Contains((key.Vi, c))) continue;
                    chosen = c;
                    break;
                }
                var v = mo.Vertices[key.Vi];
                if (chosen >= 0)
                {
                    claimed.Add((key.Vi, chosen));
                    v.UVs[chosen] = key.Uv;
                }
                else
                {
                    chosen = v.AddUVNormalSlot(key.Uv, key.Normal);
                    claimed.Add((key.Vi, chosen));
                }
                assigned[key] = chosen;
            }

            foreach (var (face, k, key, _) in corners)
            {
                int slot = assigned[key];
                while (face.UVIndices.Count <= k) face.UVIndices.Add(0);
                face.UVIndices[k] = slot;
                while (face.NormalIndices.Count <= k) face.NormalIndices.Add(0);
                face.NormalIndices[k] = slot;
            }

            VertexSlotOps.CompactUnusedSlots(mo, usedBefore);
        }

        // ----------------------------------------------------------------
        // UVZ展開メッシュ生成
        // ----------------------------------------------------------------

        public static MeshObject BuildUvzMesh(MeshObject src,
            float uvScale, float depthScale, Vector3 camPos, Vector3 camForward)
        {
            var vertexMapping = new Dictionary<(int vi, int uvIdx), int>();
            var newVertices   = new List<Vertex>();

            for (int vIdx = 0; vIdx < src.Vertices.Count; vIdx++)
            {
                var srcVert = src.Vertices[vIdx];
                int uvCount = Mathf.Max(srcVert.UVs.Count, 1);
                float depth = Vector3.Dot(srcVert.Position - camPos, camForward);

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    vertexMapping[(vIdx, uvIdx)] = newVertices.Count;

                    Vector2 uv = uvIdx < srcVert.UVs.Count ? srcVert.UVs[uvIdx] : Vector2.zero;

                    var newVert = new Vertex(new Vector3(
                        uv.x * uvScale, uv.y * uvScale, depth * depthScale));
                    newVert.UVs.Add(uv);
                    newVert.Normals.Add(-camForward);
                    newVertices.Add(newVert);
                }
            }

            var newFaces = new List<Face>();
            foreach (var srcFace in src.Faces)
            {
                if (srcFace == null || srcFace.VertexCount < 2) continue;

                var newFace = new Face();
                newFace.MaterialIndex = srcFace.MaterialIndex;
                newFace.Flags = srcFace.Flags;

                for (int ci = 0; ci < srcFace.VertexCount; ci++)
                {
                    int origVi   = srcFace.VertexIndices[ci];
                    int uvSubIdx = ci < srcFace.UVIndices.Count ? srcFace.UVIndices[ci] : 0;

                    if (!vertexMapping.TryGetValue((origVi, uvSubIdx), out int newVi))
                        if (!vertexMapping.TryGetValue((origVi, 0), out newVi))
                            newVi = 0;

                    newFace.VertexIndices.Add(newVi);
                    newFace.UVIndices.Add(0);
                    newFace.NormalIndices.Add(0);
                }
                newFaces.Add(newFace);
            }

            var newMeshObj = new MeshObject($"{src.Name}_UVZ");
            newMeshObj.Vertices = newVertices;
            newMeshObj.Faces    = newFaces;
            newMeshObj.Type     = MeshType.Mesh;
            // 頂点 ID・面 ID は付けない（AssignMissingIds を勝手に呼ばない規約）。
            // 書き戻し（WritebackXyzToUv）は頂点の並び順で対応を取り、ID を読まない。

            // UV 展開作業用の平面メッシュ。頂点は新規生成でウェイトを持たないため
            // 種別は既定の MeshFilter のままでよい。念のため実データから確認する。
            newMeshObj.RecomputeSkinKind();
            return newMeshObj;
        }

        // ----------------------------------------------------------------
        // XYZ→UV書き戻し
        // ----------------------------------------------------------------

        public static void WritebackXyzToUv(MeshObject src, MeshObject target, float uvScale)
        {
            if (uvScale < 0.001f) uvScale = 1f;

            int srcIdx = 0;
            foreach (var targetVert in target.Vertices)
            {
                int uvCount = Mathf.Max(targetVert.UVs.Count, 1);
                targetVert.UVs.Clear();

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    if (srcIdx < src.Vertices.Count)
                    {
                        var sv = src.Vertices[srcIdx];
                        targetVert.UVs.Add(new Vector2(sv.Position.x / uvScale, sv.Position.y / uvScale));
                        srcIdx++;
                    }
                    else
                    {
                        targetVert.UVs.Add(Vector2.zero);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // 共通ヘルパー
        // ----------------------------------------------------------------

        private static Vector3 ComputeFaceNormal(MeshObject meshObj, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;
            int i0 = face.VertexIndices[0];
            int i1 = face.VertexIndices[1];
            int i2 = face.VertexIndices[2];
            if (i0 < 0 || i0 >= meshObj.VertexCount ||
                i1 < 0 || i1 >= meshObj.VertexCount ||
                i2 < 0 || i2 >= meshObj.VertexCount) return Vector3.up;
            return NormalHelper.CalculateFaceNormal(
                meshObj.Vertices[i0].Position,
                meshObj.Vertices[i1].Position,
                meshObj.Vertices[i2].Position);
        }

        private static int GetDominantAxis(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }
    }
}
