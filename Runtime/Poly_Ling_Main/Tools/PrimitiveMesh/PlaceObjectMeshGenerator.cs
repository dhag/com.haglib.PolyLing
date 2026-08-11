// PlaceObjectMeshGenerator.cs
// 基準ベルト（梯子状の四角形群）の rung 中心に、指定オブジェクトを複製配置する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【配置フレーム】rung i ごとに次の系を作る。
//   位置  = (Left[i] + Right[i]) / 2
//   Z 軸  = rung i に隣接する矩形（rung i-1..i と rung i..i+1、端は片側のみ）を
//           それぞれ 2 三角に割った法線の平均（2 または 4 枚）
//   X 軸  = normalize(Right[i] - Left[i]) を Z と直交化
//   Y 軸  = Cross(Z, X)
//   倍率  = rung 長 × userScale（userScale = 1 で従来どおりの等倍）
//
// 元オブジェクトの面構成・UV・マテリアルインデックスはそのまま複製する。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PlaceObject
{
    public static class PlaceObjectMeshGenerator
    {
        /// <summary>
        /// rung ごとに source を複製配置する。基準ベルトまたは配置元が無ければ空メッシュを返す。
        /// 全 rung に同じ source を使う従来版。rung ごとに差し替える版へ委譲する。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            MeshObject source, string meshName, float userScale = 1f)
        {
            int n = (left == null || right == null) ? 0 : Mathf.Min(left.Count, right.Count);
            var sources = new MeshObject[n];
            for (int i = 0; i < n; i++) sources[i] = source;
            return Generate(left, right, beltClosed, flipWinding, sources, meshName, userScale);
        }

        /// <summary>
        /// rung ごとに配置元を差し替えて複製配置する。
        /// sourcesPerRung[i] が null または空メッシュの rung には何も置かない。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            IReadOnlyList<MeshObject> sourcesPerRung, string meshName, float userScale = 1f)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "PlaceObject" : meshName);

            int n = (left == null || right == null) ? 0 : Mathf.Min(left.Count, right.Count);
            if (n < 2) return mo;
            if (sourcesPerRung == null || sourcesPerRung.Count == 0) return mo;

            int segments = beltClosed ? n : n - 1;

            for (int i = 0; i < n; i++)
            {
                var source = (i < sourcesPerRung.Count) ? sourcesPerRung[i] : null;
                if (source == null || source.VertexCount == 0) continue;

                Vector3 center = (left[i] + right[i]) * 0.5f;

                Vector3 axis  = right[i] - left[i];
                float   scale = axis.magnitude;
                if (scale <= 1e-6f) continue;

                Vector3 xDir = axis / scale;
                Vector3 zDir = RungNormal(left, right, beltClosed, flipWinding, segments, i);

                // X を Z と直交化してから、Y を作る
                Vector3 x = xDir - zDir * Vector3.Dot(xDir, zDir);
                if (x.sqrMagnitude <= 1e-10f) continue;
                x = x.normalized;
                Vector3 y = Vector3.Cross(zDir, x);

                AppendInstance(mo, source, center, x, y, zDir, scale * userScale);
            }

            mo.RecalculateNormals();
            return mo;
        }

        // ================================================================
        // 配置フレーム
        // ================================================================

        /// <summary>rung i に隣接する矩形を 2 三角ずつに割り、その法線の平均を返す。</summary>
        private static Vector3 RungNormal(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding, int segments, int i)
        {
            int n = left.Count;
            Vector3 sum = Vector3.zero;

            // rung i-1 → i の矩形
            if (beltClosed)          sum += SegmentNormalSum(left, right, flipWinding, (i - 1 + n) % n);
            else if (i > 0)          sum += SegmentNormalSum(left, right, flipWinding, i - 1);

            // rung i → i+1 の矩形
            if (beltClosed)          sum += SegmentNormalSum(left, right, flipWinding, i % segments);
            else if (i < n - 1)      sum += SegmentNormalSum(left, right, flipWinding, i);

            return (sum.sqrMagnitude > 1e-10f) ? sum.normalized : Vector3.up;
        }

        /// <summary>矩形 1 枚を 2 三角に割った法線の和。</summary>
        private static Vector3 SegmentNormalSum(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right, bool flipWinding, int s)
        {
            int n = left.Count;
            int j = (s + 1) % n;

            Vector3 a0 = left[s], b0 = right[s], b1 = right[j], a1 = left[j];

            // 巻き順: 通常 (a0, b0, b1, a1) / 反転 (a0, a1, b1, b0)
            if (flipWinding)
            {
                return NormalHelper.CalculateFaceNormal(a0, a1, b1)
                     + NormalHelper.CalculateFaceNormal(a0, b1, b0);
            }
            return NormalHelper.CalculateFaceNormal(a0, b0, b1)
                 + NormalHelper.CalculateFaceNormal(a0, b1, a1);
        }

        // ================================================================
        // 複製
        // ================================================================

        private static void AppendInstance(
            MeshObject dst, MeshObject src,
            Vector3 center, Vector3 x, Vector3 y, Vector3 z, float scale)
        {
            int baseIdx = dst.VertexCount;

            for (int v = 0; v < src.VertexCount; v++)
            {
                var sv = src.Vertices[v];
                Vector3 p = sv.Position * scale;
                Vector3 pos = center + x * p.x + y * p.y + z * p.z;

                var nv = new Vertex(pos);
                if (sv.UVs != null)
                {
                    for (int k = 0; k < sv.UVs.Count; k++) nv.UVs.Add(sv.UVs[k]);
                }
                if (nv.UVs.Count == 0) nv.UVs.Add(Vector2.zero);

                dst.Vertices.Add(nv);
            }

            for (int f = 0; f < src.FaceCount; f++)
            {
                var sf = src.Faces[f];
                if (sf?.VertexIndices == null || sf.VertexIndices.Count < 2) continue;

                var nf = new Face { MaterialIndex = sf.MaterialIndex };
                for (int k = 0; k < sf.VertexIndices.Count; k++)
                {
                    nf.VertexIndices.Add(baseIdx + sf.VertexIndices[k]);
                    nf.UVIndices.Add(sf.UVIndices != null && k < sf.UVIndices.Count ? sf.UVIndices[k] : 0);
                    nf.NormalIndices.Add(0);
                }
                dst.AddFace(nf);
            }
        }
    }
}
