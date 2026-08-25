// NGonStarMeshGenerator.cs
// スタア（星型）メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【輪郭】外径（頂点）と内径（谷）を等角度で交互に置いた 2N 点。
// 【押し出し】GearDiskBuilder が受け持つ。中心の丸穴もそこで開ける。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class NGonStarMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct NGonStarParams : IEquatable<NGonStarParams>
        {
            public string MeshName;

            /// <summary>星の尖りの数</summary>
            public int Points;
            /// <summary>谷の半径</summary>
            public float InnerRadius;
            /// <summary>尖りの半径</summary>
            public float OuterRadius;
            /// <summary>厚み</summary>
            public float Thickness;

            /// <summary>全体の回転オフセット（度）</summary>
            public float RotationOffset;

            /// <summary>中心の丸穴半径。0 で穴なし。</summary>
            public float BoreRadius;
            /// <summary>穴リングの分割数</summary>
            public int BoreSegments;

            /// <summary>板を置く平面</summary>
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            public Vector3 Pivot;

            public static NGonStarParams Default => new NGonStarParams
            {
                MeshName       = "NGonStar",
                Points         = 5,
                InnerRadius    = 0.3f,
                OuterRadius    = 0.7f,
                Thickness      = 0.2f,
                RotationOffset = 90f,
                BoreRadius     = 0f,
                BoreSegments   = 24,
                Orientation    = PlaneOrientation.XY,
                FlipFaces      = false,
                Pivot          = Vector3.zero,
            };

            public bool Equals(NGonStarParams o) =>
                MeshName == o.MeshName &&
                Points == o.Points &&
                Mathf.Approximately(InnerRadius,    o.InnerRadius)    &&
                Mathf.Approximately(OuterRadius,    o.OuterRadius)    &&
                Mathf.Approximately(Thickness,      o.Thickness)      &&
                Mathf.Approximately(RotationOffset, o.RotationOffset) &&
                Mathf.Approximately(BoreRadius,     o.BoreRadius)     &&
                BoreSegments == o.BoreSegments &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is NGonStarParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(NGonStarParams p)
        {
            var outline = GenerateOutline(p);

            return GearDiskBuilder.Build(
                p.MeshName,
                outline,
                p.Thickness,
                p.BoreRadius,
                p.BoreSegments,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }

        /// <summary>
        /// XY 平面の閉じた輪郭（CCW）を作る。尖り 1 個あたり 2 点。
        /// </summary>
        public static List<Vector2> GenerateOutline(NGonStarParams p)
        {
            int pts = Mathf.Max(3, p.Points);

            float inner = Mathf.Max(0.0001f, p.InnerRadius);
            float outer = Mathf.Max(inner + 0.0001f, p.OuterRadius);

            int corners = pts * 2;
            float step = 360f / corners;

            var outline = new List<Vector2>(corners);

            for (int i = 0; i < corners; i++)
            {
                float a = (i * step + p.RotationOffset) * Mathf.Deg2Rad;
                float r = (i % 2 == 0) ? outer : inner;
                outline.Add(GearDiskBuilder.Polar(r, a));
            }

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }
    }
}
