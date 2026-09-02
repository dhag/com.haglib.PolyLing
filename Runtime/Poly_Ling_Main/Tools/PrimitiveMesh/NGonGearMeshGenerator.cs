// NGonGearMeshGenerator.cs
// 簡易歯車（角度指定の台形歯）メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【歯 1 枚の輪郭】角度を 4 区間に割る。
//   θL … 歯先の幅（外径）
//   θM … 下り傾斜（外径 → 内径）
//   θS … 谷の幅（内径。1 歯ぶんの角度から θL + 2θM を引いた残り。自動計算）
//   θM … 上り傾斜（内径 → 外径。次の歯先へ）
//
// 【押し出し】GearDiskBuilder が受け持つ。中心の丸穴もそこで開ける。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class NGonGearMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct NGonGearParams : IEquatable<NGonGearParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 3;
            public const int ToothCountMax = 64;

            /// <summary>歯底円半径の下限・上限</summary>
            public const float InnerRadiusMin = 0.05f;
            public const float InnerRadiusMax = 5f;

            /// <summary>歯先円半径の下限・上限</summary>
            public const float OuterRadiusMin = 0.06f;
            public const float OuterRadiusMax = 6f;

            /// <summary>厚みの下限・上限</summary>
            public const float ThicknessMin = 0f;
            public const float ThicknessMax = 3f;

            /// <summary>歯元角の下限・上限（度）</summary>
            public const float ThetaLMin = 1f;
            public const float ThetaLMax = 30f;

            /// <summary>歯先角の下限・上限（度）</summary>
            public const float ThetaMMin = 1f;
            public const float ThetaMMax = 20f;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            /// <summary>軸穴半径の下限・上限</summary>
            public const float BoreRadiusMin = 0f;
            public const float BoreRadiusMax = 5f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            /// <summary>歯の数</summary>
            [PLParam(TextKey = "GearToothCount", Description = "歯数", Min = ToothCountMin, Max = ToothCountMax,
                     Step = 1)]
            public int ToothCount;
            /// <summary>谷の半径</summary>
            [PLParam(TextKey = "GearInnerRadius", Description = "歯底円半径", Min = InnerRadiusMin,
                     Max = InnerRadiusMax)]
            public float InnerRadius;
            /// <summary>歯先の半径</summary>
            [PLParam(TextKey = "GearOuterRadius", Description = "歯先円半径", Min = OuterRadiusMin,
                     Max = OuterRadiusMax)]
            public float OuterRadius;
            /// <summary>厚み</summary>
            [PLParam(TextKey = "Thickness", Description = "厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;

            /// <summary>歯先の幅（度）</summary>
            [PLParam(TextKey = "GearThetaL", Description = "歯元の開き角（度）", Min = ThetaLMin, Max = ThetaLMax)]
            public float ThetaL;
            /// <summary>傾斜部の幅（度）</summary>
            [PLParam(TextKey = "GearThetaM", Description = "歯先の開き角（度）", Min = ThetaMMin, Max = ThetaMMax)]
            public float ThetaM;

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

            public static NGonGearParams Default => new NGonGearParams
            {
                MeshName       = "NGonGear",
                ToothCount     = 8,
                InnerRadius    = 0.7f,
                OuterRadius    = 1.0f,
                Thickness      = 0.2f,
                ThetaL         = 15f,
                ThetaM         = 5f,
                RotationOffset = 0f,
                BoreRadius     = 0f,
                BoreSegments   = 24,
                Orientation    = PlaneOrientation.XY,
                FlipFaces      = false,
                Pivot          = Vector3.zero,
            };

            public bool Equals(NGonGearParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(InnerRadius,    o.InnerRadius)    &&
                Mathf.Approximately(OuterRadius,    o.OuterRadius)    &&
                Mathf.Approximately(Thickness,      o.Thickness)      &&
                Mathf.Approximately(ThetaL,         o.ThetaL)         &&
                Mathf.Approximately(ThetaM,         o.ThetaM)         &&
                Mathf.Approximately(RotationOffset, o.RotationOffset) &&
                Mathf.Approximately(BoreRadius,     o.BoreRadius)     &&
                BoreSegments == o.BoreSegments &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is NGonGearParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 派生値
        // ================================================================

        /// <summary>1 歯ぶんの角度（度）。</summary>
        public static float AnglePerTooth(in NGonGearParams p)
            => 360f / Mathf.Max(3, p.ToothCount);

        /// <summary>谷の幅 θS（度）。1 歯ぶんの角度から θL + 2θM を引いた残り。</summary>
        public static float ThetaS(in NGonGearParams p)
            => Mathf.Max(0f, AnglePerTooth(p) - (p.ThetaL + 2f * p.ThetaM));

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(NGonGearParams p)
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
        /// XY 平面の閉じた輪郭（CCW）を作る。歯 1 枚あたり 4 点。
        /// </summary>
        public static List<Vector2> GenerateOutline(NGonGearParams p)
        {
            int n = Mathf.Max(3, p.ToothCount);

            float inner = Mathf.Max(0.0001f, p.InnerRadius);
            float outer = Mathf.Max(inner + 0.0001f, p.OuterRadius);

            float perTooth = 360f / n;
            float thetaL = Mathf.Clamp(p.ThetaL, 0.01f, perTooth);
            float thetaM = Mathf.Clamp(p.ThetaM, 0.01f, perTooth);
            float thetaS = Mathf.Max(0f, perTooth - (thetaL + 2f * thetaM));

            var outline = new List<Vector2>(n * 4);
            float a = p.RotationOffset;

            for (int i = 0; i < n; i++)
            {
                float a0 = a;
                float a1 = a0 + thetaL;
                float a2 = a1 + thetaM;
                float a3 = a2 + thetaS;
                a += perTooth;

                outline.Add(GearDiskBuilder.Polar(outer, a0 * Mathf.Deg2Rad));
                outline.Add(GearDiskBuilder.Polar(outer, a1 * Mathf.Deg2Rad));
                outline.Add(GearDiskBuilder.Polar(inner, a2 * Mathf.Deg2Rad));
                outline.Add(GearDiskBuilder.Polar(inner, a3 * Mathf.Deg2Rad));
            }

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }
    }
}
