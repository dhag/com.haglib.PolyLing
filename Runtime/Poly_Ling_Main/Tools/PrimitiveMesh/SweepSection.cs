// SweepSection.cs
// パイプの断面の種類（Maya の Sweep Mesh の Sweep Profiles に合わせる）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 5
//   種類を選ぶと断面を種類の値から作る（PipeParams.SectionKind）。Custom（値 0）は今までどおりコマンドの断面の点列を使う。
//   キーの無い古いデータは Custom として読む。列挙は保存に使うので値を変えない。
//   断面の座標はパイプの決まり（x=0 が左レール、x=1 が右レール。段の長さ＝太さが 1）。軸を帯の中心線に合わせるので中心は (0.5, 0)。
//
// 【Maya との対応】Poly（Circle・Star）・Rectangle・Line・Arc・Wave・Custom。
//   Maya の既定の Sides は資料で読めていない。既定の 8 はこのアプリで決めた値。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Pipe
{
    public enum SweepSectionKind
    {
        [InspectorName("円")]     Circle    = 1,
        [InspectorName("星形")]   Star      = 2,
        [InspectorName("長方形")] Rectangle = 3,
        [InspectorName("線")]     Line      = 4,
        [InspectorName("弧")]     Arc       = 5,
        [InspectorName("波")]     Wave      = 6,
        [InspectorName("カスタム")] Custom  = 0,
    }

    public static class SweepSectionOps
    {
        public const int DefaultSides = 8;

        /// <summary>種類の値から断面を作る。Custom は null。</summary>
        public static List<Vector3> Build(in PipeParams p, out bool closed)
        {
            closed = true;
            var pts = new List<Vector3>();
            int sides = p.SectionSides >= 2 ? p.SectionSides : DefaultSides;
            switch (p.SectionKind)
            {
                case SweepSectionKind.Circle:
                    sides = Mathf.Max(3, sides);
                    for (int i = 0; i < sides; i++)
                    {
                        float a = Mathf.PI * 2f * i / sides;
                        pts.Add(new Vector3(0.5f + 0.5f * Mathf.Cos(a), 0.5f * Mathf.Sin(a), 0f));
                    }
                    return pts;

                case SweepSectionKind.Star:
                {
                    sides = Mathf.Max(3, sides);
                    float inner = p.SectionInner > 0f ? Mathf.Clamp(p.SectionInner, 0.01f, 1f) : 0.5f;
                    for (int i = 0; i < sides * 2; i++)
                    {
                        float a = Mathf.PI * i / sides;
                        float r = 0.5f * (i % 2 == 0 ? 1f : inner);
                        pts.Add(new Vector3(0.5f + r * Mathf.Cos(a), r * Mathf.Sin(a), 0f));
                    }
                    return pts;
                }

                case SweepSectionKind.Rectangle:
                {
                    float h = (p.SectionHeight > 0f ? p.SectionHeight : 1f) * 0.5f;
                    pts.Add(new Vector3(0f, -h, 0f));
                    pts.Add(new Vector3(1f, -h, 0f));
                    pts.Add(new Vector3(1f,  h, 0f));
                    pts.Add(new Vector3(0f,  h, 0f));
                    return pts;
                }

                case SweepSectionKind.Line:
                    closed = false;
                    pts.Add(new Vector3(0f, 0f, 0f));
                    pts.Add(new Vector3(1f, 0f, 0f));
                    return pts;

                case SweepSectionKind.Arc:
                {
                    closed = false;
                    float angle = (p.SectionArcAngle > 0f ? p.SectionArcAngle : 180f) * Mathf.Deg2Rad;
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = Mathf.PI * 0.5f + angle * (i / (float)sides - 0.5f);
                        pts.Add(new Vector3(0.5f + 0.5f * Mathf.Cos(a), 0.5f * Mathf.Sin(a), 0f));
                    }
                    return pts;
                }

                case SweepSectionKind.Wave:
                {
                    closed = false;
                    float cycles = p.SectionCycles > 0f ? p.SectionCycles : 1f;
                    float amp    = p.SectionAmplitude > 0f ? p.SectionAmplitude : 0.25f;
                    for (int i = 0; i <= sides; i++)
                    {
                        float x = i / (float)sides;
                        pts.Add(new Vector3(x, amp * Mathf.Sin(x * cycles * Mathf.PI * 2f), 0f));
                    }
                    return pts;
                }

                default:
                    return null;
            }
        }
    }
}
