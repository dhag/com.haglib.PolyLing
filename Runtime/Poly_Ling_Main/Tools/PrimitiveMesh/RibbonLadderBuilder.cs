// RibbonLadderBuilder.cs
// 中心曲線 + 幅 から梯子（左右レール）を作る。Runtime / Editor 共有。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【幅方向】2通りある。どちらもロール（長手軸まわりのねじり）は持たない。
//
//   FixedNormal   … 基準法線 N = +Z（モデル正面。AuthoringFrame 参照）に対して
//                   B = normalize(Cross(N, T))。帯の表が常に正面を向く。
//                   T が N とほぼ平行な区間では Cross が消えるため直前の B を引き継ぐ。
//
//   VerticalGuide … ガイド（既定は鉛直 +Y）のうち接線に直交する成分を幅方向にする。
//                   曲線が奥行き方向へ折り返すと、面法線が 表(+Z) → 外向き → 裏(-Z)
//                   と自然に入れ替わる。実物のリボンのループはこちら。
//                   T がガイドとほぼ平行な区間では直前の B を引き継ぐ。
//                   ガイドは呼び出し側から差し替えられる。折り返しの回転軸を傾けた
//                   （＝曲線ごと回した）ときに、ガイドを同じだけ回して一致を保つため。
//
// 【左右の定義】Left = C - B * w/2 、Right = C + B * w/2。
//   面は (L[i], L[i+1], R[i+1], R[i]) の順で張る（RibbonBowMeshGenerator）。
//   法線は Cross(T, B) 側になる。
//
// 【半幅の制限】帯を曲線と同じ平面内で曲げると、曲率半径が半幅を下回ったところで
//   内側レールが進行方向へ逆走し、面が裏返る。
//   逆走した区間は、その両端サンプルの半幅を段階的に縮めて解消する。
//   半幅を要求値より広げることはない。
//   判定は実際のレール位置で行うため、ロール角で帯が曲がりの面から立っている区間
//   （＝折り返しが幅方向の軸まわりの曲げになっていて逆走しない区間）は縮まない。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Ribbon
{
    /// <summary>幅方向の決め方。</summary>
    public enum RibbonFrameMode
    {
        /// <summary>基準法線 +Z に対して B = Cross(N, T)。帯の表が常に正面を向く。</summary>
        FixedNormal = 0,

        /// <summary>ガイド（既定は鉛直 +Y）に最も近い、接線に直交する向きを幅方向にする。</summary>
        VerticalGuide = 1,
    }

    /// <summary>梯子1本ぶんの左右レール。</summary>
    public sealed class RibbonLadder
    {
        /// <summary>部品名（デバッグ・情報表示用）。</summary>
        public string Name = "";

        public readonly List<Vector3> Left  = new List<Vector3>();
        public readonly List<Vector3> Right = new List<Vector3>();

        public int RungCount => Left.Count;
    }

    public static class RibbonLadderBuilder
    {
        /// <summary>FixedNormal で使う基準法線（モデル正面）。</summary>
        private static readonly Vector3 RefNormal = Vector3.forward;

        /// <summary>VerticalGuide で使う幅方向のガイドの既定値（鉛直）。</summary>
        private static readonly Vector3 WidthGuide = Vector3.up;

        /// <summary>逆走した区間の半幅に掛ける係数。</summary>
        private const float ShrinkStep = 0.7f;

        /// <summary>逆走補正の最大反復数。</summary>
        private const int MaxShrinkIterations = 12;

        /// <summary>基準法線モードで生成する。</summary>
        public static RibbonLadder Build(
            IReadOnlyList<RibbonBezier> segs, int segments,
            Func<float, float> widthAt, string name)
            => Build(segs, segments, widthAt, RibbonFrameMode.FixedNormal, name);

        /// <summary>
        /// 連結ベジエを弧長ほぼ等間隔で分割し、各サンプルへ幅を与えて梯子にする。
        /// widthAt は媒介変数 s（0=始点 / 1=終点）に対する帯の幅を返す。
        /// 幅方向ガイドは既定（鉛直 +Y）。
        /// </summary>
        public static RibbonLadder Build(
            IReadOnlyList<RibbonBezier> segs, int segments,
            Func<float, float> widthAt, RibbonFrameMode frame, string name)
            => Build(segs, segments, widthAt, frame, WidthGuide, name);

        /// <summary>
        /// 幅方向ガイドを指定して生成する。VerticalGuide のときだけ使われる。
        /// 曲線ごと回した梯子で、折り返しの回転軸とガイドの一致を保つために使う
        /// （ずれると折り返しで帯がねじれる）。零ベクトルは既定へ落とす。
        /// </summary>
        public static RibbonLadder Build(
            IReadOnlyList<RibbonBezier> segs, int segments,
            Func<float, float> widthAt, RibbonFrameMode frame, Vector3 widthGuide, string name)
        {
            var ladder = new RibbonLadder { Name = name ?? "" };

            if (segs == null || segs.Count == 0 || widthAt == null) return ladder;
            if (segments < 1) segments = 1;

            Vector3 guide = widthGuide.sqrMagnitude > 1e-8f
                ? widthGuide.normalized
                : WidthGuide;

            var pos = new List<Vector3>();
            var tan = new List<Vector3>();
            RibbonCurveSampler.Sample(segs, segments, pos, tan);

            int n = pos.Count;
            if (n < 2) return ladder;

            // ── 幅方向 ──
            var dir = new Vector3[n];
            Vector3 prevB = Vector3.zero;

            for (int i = 0; i < n; i++)
            {
                Vector3 t = tan[i];
                Vector3 b;

                if (frame == RibbonFrameMode.VerticalGuide)
                {
                    // ガイドのうち接線に直交する成分。
                    b = guide - t * Vector3.Dot(guide, t);
                    if (b.sqrMagnitude < 1e-6f)
                    {
                        // 接線がガイドとほぼ平行。直前の幅方向を接線へ直交化して引き継ぐ。
                        b = prevB - t * Vector3.Dot(prevB, t);
                        if (b.sqrMagnitude < 1e-6f) b = Vector3.Cross(RefNormal, t);
                        if (b.sqrMagnitude < 1e-6f) b = guide;
                    }
                }
                else
                {
                    b = Vector3.Cross(RefNormal, t);
                    if (b.sqrMagnitude < 1e-8f)
                    {
                        // 接線が基準法線と平行。直前の幅方向を引き継ぐ（初回はガイド）。
                        b = (prevB.sqrMagnitude > 1e-8f) ? prevB : guide;
                    }
                }

                b = b.normalized;
                prevB  = b;
                dir[i] = b;
            }

            // ── 半幅 ──
            var half = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = i / (float)(n - 1);
                half[i] = Mathf.Max(0f, widthAt(s)) * 0.5f;
            }

            // ── 逆走区間を縮めて解消する ──
            var left  = new Vector3[n];
            var right = new Vector3[n];
            BuildRails(pos, dir, half, left, right);

            for (int iter = 0; iter < MaxShrinkIterations; iter++)
            {
                bool bad = false;

                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 d = pos[i + 1] - pos[i];
                    if (Vector3.Dot(left [i + 1] - left [i], d) > 0f &&
                        Vector3.Dot(right[i + 1] - right[i], d) > 0f) continue;

                    half[i]     *= ShrinkStep;
                    half[i + 1] *= ShrinkStep;
                    bad = true;
                }

                if (!bad) break;
                BuildRails(pos, dir, half, left, right);
            }

            for (int i = 0; i < n; i++)
            {
                ladder.Left .Add(left [i]);
                ladder.Right.Add(right[i]);
            }

            return ladder;
        }

        private static void BuildRails(
            List<Vector3> pos, Vector3[] dir, float[] half,
            Vector3[] left, Vector3[] right)
        {
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 off = dir[i] * half[i];
                left [i] = pos[i] - off;
                right[i] = pos[i] + off;
            }
        }
    }
}
