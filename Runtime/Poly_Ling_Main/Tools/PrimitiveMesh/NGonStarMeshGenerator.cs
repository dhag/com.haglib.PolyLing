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
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>とがりの数の下限・上限</summary>
            public const int PointsMin = 3;
            public const int PointsMax = 64;

            /// <summary>谷の半径の下限・上限</summary>
            public const float InnerRadiusMin = 0.02f;
            public const float InnerRadiusMax = 5f;

            /// <summary>とがりの半径の下限・上限</summary>
            public const float OuterRadiusMin = 0.03f;
            public const float OuterRadiusMax = 6f;

            /// <summary>厚みの下限・上限</summary>
            public const float ThicknessMin = 0f;
            public const float ThicknessMax = 3f;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            /// <summary>軸穴半径の下限・上限</summary>
            public const float BoreRadiusMin = 0f;
            public const float BoreRadiusMax = 5f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            /// <summary>星の尖りの数</summary>
            [PLParam(TextKey = "StarPoints", Description = "とがりの数", Min = PointsMin, Max = PointsMax, Step = 1)]
            public int Points;
            /// <summary>谷の半径</summary>
            [PLParam(TextKey = "StarInnerRadius", Description = "谷の半径", Min = InnerRadiusMin,
                     Max = InnerRadiusMax)]
            public float InnerRadius;
            /// <summary>尖りの半径</summary>
            [PLParam(TextKey = "StarOuterRadius", Description = "とがりの半径", Min = OuterRadiusMin,
                     Max = OuterRadiusMax)]
            public float OuterRadius;
            /// <summary>厚み</summary>
            [PLParam(TextKey = "Thickness", Description = "厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;

            /// <summary>全体の回転オフセット（度）</summary>
            [PLParam(TextKey = "GearRotationOffset", Description = "全体の回転オフセット（度）", Min = RotationOffsetMin,
                     Max = RotationOffsetMax)]
            public float RotationOffset;

            /// <summary>中心の丸穴半径。0 で穴なし。</summary>
            [PLParam(TextKey = "GearBoreRadius", Description = "軸穴の半径。0 で穴なし", Min = BoreRadiusMin,
                     Max = BoreRadiusMax)]
            public float BoreRadius;
            /// <summary>穴リングの分割数</summary>
            [PLParam(TextKey = "GearBoreSegments", Description = "軸穴の円周分割数",
                     Min = GearDiskBuilder.BoreSegmentsMin, Max = GearDiskBuilder.BoreSegmentsMax, Step = 1)]
            public int BoreSegments;

            /// <summary>板を置く平面</summary>
            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
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
