// PyramidMeshGenerator.cs
// 角錐メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    public static class PyramidMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct PyramidParams : IEquatable<PyramidParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>底面の外接円半径の下限・上限</summary>
            public const float BaseRadiusMin = 0.1f;
            public const float BaseRadiusMax = 5f;

            /// <summary>高さの下限・上限</summary>
            public const float HeightMin = 0.1f;
            public const float HeightMax = 10f;

            /// <summary>底面の辺数の下限・上限</summary>
            public const int SidesMin = 3;
            public const int SidesMax = 16;

            /// <summary>頂点のずらしの下限・上限</summary>
            public const float ApexOffsetMin = -1f;
            public const float ApexOffsetMax = 1f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "BaseRadius", Description = "底面の外接円半径", Min = BaseRadiusMin, Max = BaseRadiusMax)]
            public float BaseRadius;
            [PLParam(TextKey = "Height", Description = "高さ", Min = HeightMin, Max = HeightMax)]
            public float Height;
            [PLParam(TextKey = "Sides", Description = "底面の辺数", Min = SidesMin, Max = SidesMax, Step = 1)]
            public int Sides;
            [PLParam(TextKey = "ApexOffset", Description = "頂点の水平方向のずらし", Min = ApexOffsetMin,
                     Max = ApexOffsetMax)]
            public float ApexOffset;
            [PLParam(TextKey = "CapBottom", Description = "底面にフタを張る")]
            public bool CapBottom;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;
            [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
            public float RotationX;
            [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
            public float RotationY;

            public static PyramidParams Default => new PyramidParams
            {
                MeshName    = "Pyramid",
                BaseRadius  = 0.5f,
                Height      = 1f,
                Sides       = 4,
                ApexOffset  = 0f,
                CapBottom   = true,
                Pivot       = Vector3.zero,
                RotationX   = 20f, RotationY = 30f,
            };

            public bool Equals(PyramidParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(BaseRadius,  o.BaseRadius)  &&
                Mathf.Approximately(Height,      o.Height)      &&
                Sides == o.Sides &&
                Mathf.Approximately(ApexOffset,  o.ApexOffset)  &&
                CapBottom == o.CapBottom &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is PyramidParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(PyramidParams p)
        {
            var md = new MeshObject(p.MeshName);
            float halfH = p.Height * 0.5f;
            Vector3 pivotOffset = new Vector3(0, p.Pivot.y * p.Height, 0);
            Vector3 apex = new Vector3(p.ApexOffset * p.BaseRadius, halfH, 0) - pivotOffset;

            var base3 = new Vector3[p.Sides];
            for (int i = 0; i < p.Sides; i++)
            {
                float a = i * 2f * Mathf.PI / p.Sides;
                base3[i] = new Vector3(Mathf.Cos(a)*p.BaseRadius, -halfH, Mathf.Sin(a)*p.BaseRadius) - pivotOffset;
            }

            // 側面
            for (int i = 0; i < p.Sides; i++)
            {
                int si = md.VertexCount;
                Vector3 p0 = base3[i], p1 = base3[(i+1) % p.Sides];
                // 面は AddTriangle(si, si+2, si+1) = (p0, apex, p1) の順で張る。
                // 宣言法線も同じ順で求めないと外向きの巻き順に対して内向きになる。
                Vector3 n = NormalHelper.CalculateFaceNormal(p0, apex, p1);
                md.Vertices.Add(new Vertex(p0,   new Vector2(0,   0), n));
                md.Vertices.Add(new Vertex(p1,   new Vector2(1,   0), n));
                md.Vertices.Add(new Vertex(apex, new Vector2(0.5f,1), n));
                md.AddTriangle(si, si+2, si+1);
            }

            // 底面キャップ
            if (p.CapBottom)
            {
                int ci = md.VertexCount;
                md.Vertices.Add(new Vertex(new Vector3(0,-halfH,0)-pivotOffset, new Vector2(0.5f,0.5f), Vector3.down));
                for (int i = 0; i < p.Sides; i++)
                {
                    float a = i * 2f * Mathf.PI / p.Sides;
                    Vector2 uv = new Vector2(Mathf.Cos(a)*0.5f+0.5f, Mathf.Sin(a)*0.5f+0.5f);
                    md.Vertices.Add(new Vertex(base3[i], uv, Vector3.down));
                }
                for (int i = 0; i < p.Sides; i++)
                    md.AddTriangle(ci, ci+1+i, ci+1+(i+1)%p.Sides);
            }

            PrimitiveMeshPostProcess.SortVerticesCanonical(md);
            return md;
        }
    }
}
