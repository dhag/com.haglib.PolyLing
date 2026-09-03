// HairStrandMeshGenerator.cs
// 髪の房メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【生成物】
//   房 M 個 × 筒 N 本 = 独立した閉じたチューブ N×M 本を 1 つの MeshObject に入れる。
//   筒 1 本が部品 1 個。部品IDは PartsIdCounter で通し番号を振る。
//   サブIDは生成器では触らない（PrimitiveMeshFactory.AssignPartsIds が生成後に振る）。
//
// 【頂点の並べ替えをしない理由】
//   SortVerticesCanonical は Y 降順に並べ替える。多部品の生成物へ掛けると
//   筒どうしの頂点が入り混じり、サブIDが生成順と対応しなくなる。
//   フリル・パイプも同じ理由で呼んでいない。
//
// 【フレーム】
//   B = 土台の曲面法線、T = dC/dt（差分）、N = B × T。
//   Unity の Vector3.Cross の定義では N × B = T が成り立つ標準の三つ組になり、
//   断面を (x,y) = (N,B) 平面に置いて +T へ掃引する形になる。
//   Frenet を使わないのは、直線部で法線が定まらないことと捻れが累積することによる。
//
// 【断面の法線】
//   断面 2D 曲線の接線を θ の数値微分で求め、外向き法線 (t.y, −t.x) を frame へ載せる。
//   解析微分は断面の冪が 1 未満のとき発散するので使わない。
//   掃引方向の絞り込みによる法線の傾きは無視する。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.HairStrand
{
    public static class HairStrandMeshGenerator
    {
        /// <summary>断面の接線を求めるときの θ の刻み。</summary>
        private const float SectionDelta = 1e-3f;

        /// <summary>これ以下の広がりしかない断面には蓋を張らない。</summary>
        private const float DegenerateExtent = 1e-6f;

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(HairStrandParams p)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "HairStrand" : p.MeshName);

            int strandCount = Mathf.Clamp(p.StrandCount,
                HairStrandParams.StrandCountMin, HairStrandParams.StrandCountMax);
            int lobeCount = Mathf.Clamp(p.LobeCount,
                HairStrandParams.LobeCountMin, HairStrandParams.LobeCountMax);
            int lengthSeg = Mathf.Clamp(p.LengthSegments,
                HairStrandParams.LengthSegmentsMin, HairStrandParams.LengthSegmentsMax);
            int sectionSeg = Mathf.Clamp(p.SectionSegments,
                HairStrandParams.SectionSegmentsMin, HairStrandParams.SectionSegmentsMax);

            float[] weights = NormalizeLobeWidths(p.LobeWidths, lobeCount);
            var parts = new PartsIdCounter();

            for (int m = 0; m < strandCount; m++)
            {
                float u = SlopeFactor(p.SlopeMode, m, strandCount);

                float spanAxial = p.SpanAxial * (1f + p.LenSlope   * u);
                float spanAngle = p.SpanAngle * (1f + p.LenSlope   * u);
                float widthMid  = p.WidthMid  * (1f + p.WidthSlope * u);
                float thickMid  = p.ThickMid  * (1f + p.ThickSlope * u);
                float lift      = p.Lift      * (1f + p.LiftSlope  * u);
                float twist     = p.Twist     * (1f + p.TwistSlope * u);

                float startAxial = p.StartAxial + p.PitchAxial * m;
                float startAngle = p.StartAngle + p.PitchAngle * m;

                BuildStrand(mo, p, weights, lengthSeg, sectionSeg,
                            startAxial, startAngle, spanAxial, spanAngle,
                            widthMid, thickMid, lift, twist, parts);
            }

            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, p.Pivot);

            return mo;
        }

        // ================================================================
        // 房 1 個
        // ================================================================

        private static void BuildStrand(
            MeshObject mo, in HairStrandParams p, float[] weights,
            int lengthSeg, int sectionSeg,
            float startAxial, float startAngle, float spanAxial, float spanAngle,
            float widthMid, float thickMid, float lift, float twist,
            PartsIdCounter parts)
        {
            int rings = lengthSeg + 1;

            var center = new Vector3[rings];
            var baseN  = new Vector3[rings];

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / lengthSeg;
                float axial = startAxial + spanAxial * t;
                float angle = startAngle + spanAngle * t;
                baseN[i]  = BaseNormal(p, axial, angle);
                center[i] = BasePoint(p, axial, angle, lift);
            }

            // 接線。中央差分（両端は片側差分）。
            var tangent = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                Vector3 d;
                if (i == 0)                 d = center[1] - center[0];
                else if (i == rings - 1)    d = center[rings - 1] - center[rings - 2];
                else                        d = center[i + 1] - center[i - 1];

                tangent[i] = d.sqrMagnitude > 1e-16f ? d.normalized : Vector3.zero;
            }

            if (!FillZeroTangents(tangent)) return;   // 中心線が 1 点に潰れている

            // フレーム。B は曲面法線を接線へ直交化して使う。
            var frameN = new Vector3[rings];
            var frameB = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                Vector3 n = Vector3.Cross(baseN[i], tangent[i]);
                if (n.sqrMagnitude <= 1e-16f)
                {
                    // 曲面法線と接線が平行。土台上のパスでは起きないが保険。
                    Vector3 seed = Mathf.Abs(tangent[i].y) < 0.9f ? Vector3.up : Vector3.right;
                    n = Vector3.Cross(seed, tangent[i]);
                    if (n.sqrMagnitude <= 1e-16f) n = Vector3.right;
                }
                frameN[i] = n.normalized;
                frameB[i] = Vector3.Cross(tangent[i], frameN[i]).normalized;

                if (!Mathf.Approximately(twist, 0f))
                {
                    float t = (float)i / lengthSeg;
                    var rot = Quaternion.AngleAxis(twist * t, tangent[i]);
                    frameN[i] = rot * frameN[i];
                    frameB[i] = rot * frameB[i];
                }
            }

            // 幅・厚み
            var widthAll = new float[rings];
            var thickAll = new float[rings];
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / lengthSeg;
                widthAll[i] = Mathf.Max(0f, Profile(p.WidthRoot, widthMid, p.WidthTip,
                                                    p.WidthMidT, p.WidthPowRoot, p.WidthPowTip, t));
                thickAll[i] = Mathf.Max(0f, Profile(p.ThickRoot, thickMid, p.ThickTip,
                                                    p.ThickMidT, p.ThickPowRoot, p.ThickPowTip, t));
            }

            float q = Mathf.Max(p.SectionPower, HairStrandParams.SectionPowerMin);
            float innerRatio = Mathf.Max(0f, p.InnerRatio);

            float accum = 0f;
            for (int k = 0; k < weights.Length; k++)
            {
                float u0 = accum;
                float u1 = accum + weights[k];
                accum = u1;

                float lobeCenter = (u0 + u1) * 0.5f - 0.5f;   // 房の幅を 1 としたときの中心位置
                float lobeShare  = weights[k];

                BuildLobe(mo, center, frameN, frameB, tangent,
                          widthAll, thickAll, lobeCenter, lobeShare,
                          innerRatio, q, lengthSeg, sectionSeg, parts);
            }
        }

        // ================================================================
        // 筒 1 本
        // ================================================================

        private static void BuildLobe(
            MeshObject mo,
            Vector3[] center, Vector3[] frameN, Vector3[] frameB, Vector3[] tangent,
            float[] widthAll, float[] thickAll,
            float lobeCenter, float lobeShare,
            float innerRatio, float q,
            int lengthSeg, int sectionSeg, PartsIdCounter parts)
        {
            int rings = lengthSeg + 1;
            int cols  = sectionSeg + 1;   // 継ぎ目の頂点を複製する
            int start = mo.VertexCount;

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / lengthSeg;

                Vector3 c = center[i] + widthAll[i] * lobeCenter * frameN[i];
                float halfW   = widthAll[i] * lobeShare * 0.5f;
                float halfOut = thickAll[i] * 0.5f;
                float halfIn  = thickAll[i] * 0.5f * innerRatio;

                for (int j = 0; j < cols; j++)
                {
                    float th = 2f * Mathf.PI * j / sectionSeg;

                    Vector2 s  = Section2D(th, halfW, halfOut, halfIn, q);
                    Vector2 n2 = SectionNormal2D(th, halfW, halfOut, halfIn, q);

                    Vector3 pos = c + s.x * frameN[i] + s.y * frameB[i];
                    Vector3 nrm = (n2.x * frameN[i] + n2.y * frameB[i]).normalized;

                    mo.Vertices.Add(new Vertex(pos, new Vector2((float)j / sectionSeg, t), nrm));
                }
            }

            for (int i = 0; i < lengthSeg; i++)
            {
                for (int j = 0; j < sectionSeg; j++)
                {
                    int i0 = start + i * cols + j;
                    mo.AddQuad(i0, i0 + 1, i0 + cols + 1, i0 + cols);
                }
            }

            // 蓋。根元は −T、毛先は +T を向く。
            AddCap(mo, start, cols, sectionSeg, 0,
                   center[0] + widthAll[0] * lobeCenter * frameN[0],
                   -tangent[0], widthAll[0] * lobeShare * 0.5f,
                   thickAll[0] * 0.5f, thickAll[0] * 0.5f * innerRatio, true);

            int last = lengthSeg;
            AddCap(mo, start, cols, sectionSeg, last,
                   center[last] + widthAll[last] * lobeCenter * frameN[last],
                   tangent[last], widthAll[last] * lobeShare * 0.5f,
                   thickAll[last] * 0.5f, thickAll[last] * 0.5f * innerRatio, false);

            PartsIdOps.SetPartsIdRange(mo, start, mo.VertexCount, parts.Take());
        }

        /// <summary>
        /// 端の蓋を張る。側面と法線が違うので頂点は複製する。
        /// 断面が 1 点へ潰れているときは側面の四角形が三角形に縮んで閉じるため、蓋を張らない。
        /// </summary>
        private static void AddCap(
            MeshObject mo, int start, int cols, int sectionSeg, int ringIndex,
            Vector3 capCenter, Vector3 normal,
            float halfW, float halfOut, float halfIn, bool reverse)
        {
            float extent = Mathf.Max(halfW, Mathf.Max(halfOut, halfIn));
            if (extent < DegenerateExtent) return;
            if (normal.sqrMagnitude <= 1e-16f) return;

            Vector3 n = normal.normalized;
            int c0 = mo.VertexCount;

            mo.Vertices.Add(new Vertex(capCenter, new Vector2(0.5f, 0.5f), n));

            int ringStart = start + ringIndex * cols;
            for (int j = 0; j < sectionSeg; j++)
            {
                float th = 2f * Mathf.PI * j / sectionSeg;
                Vector3 pos = mo.Vertices[ringStart + j].Position;
                var uv = new Vector2(0.5f + 0.5f * Mathf.Cos(th), 0.5f + 0.5f * Mathf.Sin(th));
                mo.Vertices.Add(new Vertex(pos, uv, n));
            }

            for (int j = 0; j < sectionSeg; j++)
            {
                int a = c0 + 1 + j;
                int b = c0 + 1 + (j + 1) % sectionSeg;
                if (reverse) mo.AddTriangle(c0, b, a);
                else         mo.AddTriangle(c0, a, b);
            }
        }

        // ================================================================
        // 土台
        // ================================================================

        /// <summary>
        /// 軸から正規直交基底を作る。e1 × e2 = ea が 3 軸とも成り立つ並びにしてある
        /// （Unity の Vector3.Cross の定義で X×Y=Z, Y×Z=X, Z×X=Y）。
        /// </summary>
        private static void AxisBasis(HairBaseAxis axis, out Vector3 ea, out Vector3 e1, out Vector3 e2)
        {
            switch (axis)
            {
                case HairBaseAxis.X:
                    ea = new Vector3(1f, 0f, 0f);
                    e1 = new Vector3(0f, 1f, 0f);
                    e2 = new Vector3(0f, 0f, 1f);
                    break;
                case HairBaseAxis.Y:
                    ea = new Vector3(0f, 1f, 0f);
                    e1 = new Vector3(0f, 0f, 1f);
                    e2 = new Vector3(1f, 0f, 0f);
                    break;
                default:
                    ea = new Vector3(0f, 0f, 1f);
                    e1 = new Vector3(1f, 0f, 0f);
                    e2 = new Vector3(0f, 1f, 0f);
                    break;
            }
        }

        /// <summary>球のときの軸。極は +Y に固定する。</summary>
        private static HairBaseAxis EffectiveAxis(in HairStrandParams p)
            => p.BaseType == HairBaseType.Sphere ? HairBaseAxis.Y : p.Axis;

        /// <summary>土台の曲面法線。円筒は軸に垂直、球は中心からの方向。</summary>
        private static Vector3 BaseNormal(in HairStrandParams p, float axial, float angleDeg)
        {
            AxisBasis(EffectiveAxis(p), out Vector3 ea, out Vector3 e1, out Vector3 e2);

            float f = angleDeg * Mathf.Deg2Rad;
            Vector3 ring = Mathf.Cos(f) * e1 + Mathf.Sin(f) * e2;

            if (p.BaseType == HairBaseType.Cylinder) return ring;

            float a = axial * Mathf.Deg2Rad;   // 球では軸方向の量は赤道からの仰角（度）
            return Mathf.Cos(a) * ring + Mathf.Sin(a) * ea;
        }

        /// <summary>土台面から lift だけ浮かせた点。円筒は無限長として扱う。</summary>
        private static Vector3 BasePoint(in HairStrandParams p, float axial, float angleDeg, float lift)
        {
            Vector3 er = BaseNormal(p, axial, angleDeg);
            float r = p.Radius + lift;

            if (p.BaseType == HairBaseType.Sphere) return r * er;

            AxisBasis(p.Axis, out Vector3 ea, out _, out _);
            return ea * axial + r * er;
        }

        // ================================================================
        // 断面
        // ================================================================

        /// <summary>
        /// 断面 2D。x は幅方向、y は厚み方向。y は外側（+）と内側（−）で半径が違う。
        /// </summary>
        private static Vector2 Section2D(float th, float halfW, float halfOut, float halfIn, float q)
        {
            float s = Mathf.Sin(th);
            float b = s >= 0f ? halfOut : halfIn;
            float m = Mathf.Pow(Mathf.Abs(s), q);
            float y = s >= 0f ? b * m : -b * m;
            return new Vector2(halfW * Mathf.Cos(th), y);
        }

        /// <summary>断面 2D の外向き法線。θ の数値微分から求める。</summary>
        private static Vector2 SectionNormal2D(float th, float halfW, float halfOut, float halfIn, float q)
        {
            Vector2 p1 = Section2D(th - SectionDelta, halfW, halfOut, halfIn, q);
            Vector2 p2 = Section2D(th + SectionDelta, halfW, halfOut, halfIn, q);

            Vector2 tan = p2 - p1;
            var n = new Vector2(tan.y, -tan.x);

            if (n.sqrMagnitude < 1e-20f)
                return new Vector2(Mathf.Cos(th), Mathf.Sin(th));

            return n.normalized;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>
        /// 根元 / 中間 / 末端 の 3 点を中間位置で 2 分割した冪で結ぶ。
        /// t=0 で root、t=tm で mid、t=1 で tip をちょうど通る。
        /// </summary>
        private static float Profile(
            float root, float mid, float tip, float midT, float powRoot, float powTip, float t)
        {
            float tm = Mathf.Clamp(midT, 0.001f, 0.999f);
            float pr = Mathf.Max(powRoot, 0.0001f);
            float pt = Mathf.Max(powTip,  0.0001f);

            if (t <= tm)
            {
                float s = Mathf.Clamp01(t / tm);
                return root + (mid - root) * Mathf.Pow(s, pr);
            }

            float s2 = Mathf.Clamp01((t - tm) / (1f - tm));
            return tip + (mid - tip) * Mathf.Pow(1f - s2, pt);
        }

        /// <summary>房インデックスに対する変化量。房が 1 本のときは 0。</summary>
        private static float SlopeFactor(HairSlopeMode mode, int index, int count)
        {
            if (count <= 1) return 0f;
            float lin = (float)index / (count - 1);
            return mode == HairSlopeMode.Symmetric ? lin * 2f - 1f : lin;
        }

        /// <summary>幅配分を要素数 count に揃え、合計 1 へ正規化する。</summary>
        private static float[] NormalizeLobeWidths(float[] src, int count)
        {
            var a = new float[count];
            float sum = 0f;

            for (int i = 0; i < count; i++)
            {
                float v = (src != null && i < src.Length) ? src[i] : HairStrandParams.LobeWidthMin;
                if (!(v > HairStrandParams.LobeWidthMin)) v = HairStrandParams.LobeWidthMin;
                a[i] = v;
                sum += v;
            }

            if (sum <= 0f)
            {
                float e = 1f / count;
                for (int i = 0; i < count; i++) a[i] = e;
                return a;
            }

            for (int i = 0; i < count; i++) a[i] /= sum;
            return a;
        }

        /// <summary>
        /// 長さ 0 の接線を前後の有効値で埋める。
        /// 1 つも有効な接線が無い（中心線が 1 点に潰れている）ときは false を返す。
        /// </summary>
        private static bool FillZeroTangents(Vector3[] tangent)
        {
            int n = tangent.Length;

            int first = -1;
            for (int i = 0; i < n; i++)
                if (tangent[i].sqrMagnitude > 0f) { first = i; break; }

            if (first < 0) return false;

            for (int i = first - 1; i >= 0; i--) tangent[i] = tangent[i + 1];
            for (int i = first + 1; i < n; i++)
                if (tangent[i].sqrMagnitude <= 0f) tangent[i] = tangent[i - 1];

            return true;
        }
    }
}
