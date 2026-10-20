// LatticeTowerMeshGenerator.cs
// 鉄塔（ラチス構造の塔）の生成ロジック（Runtime / Editor 共有）
//   ・塔体   LatticeTowerMeshGenerator
//   ・デッキ TowerDeckMeshGenerator（指定オブジェクトを塔の軸上の高さへ置く）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【座標】
//   Y 上向き、地面 y = 0、塔の軸は原点を通る Y 軸。平面形は X 幅・Z 幅の長方形
//   （同じ値なら正方形）。PlanRotation で Y 軸まわりに回す（回転鉄塔は 45°）。
//
// 【脚線（プロファイル）】
//   高さ・X 幅・Z 幅の点列で決める。点の間は直線か、行き過ぎない 3 次曲線
//   （単調 3 次エルミート）でつなぐ。区間ごとに段数と斜材の型を持つ。
//   エッフェル塔・東京タワーのすそ野は、カーブ式から点列を起こして作る（パネル側）。
//
// 【部材】
//   すべて角材（4 面の柱＋両端のふた）。継ぎ目は重ねるだけで、結合はしない。
//   太さは高さに比例して細くなる（根元 1、頂部 TopSizeRatio）。
//
// 【組柱】
//   LegLatticeTop より下の段は、脚 1 本を 4 本の主材と綴り材（ジグザグ）で組んだ柱にする。
//   柱の外側の主材が脚線の角の位置に来るので、上の単材の脚とそのままつながる。
//
// 【面の向き】
//   面は外向きの巻き順で張る（NormalHelper.CalculateFaceNormal がその順で外向き）。
//   法線は最後に RecalculateNormals でフラットに付け直す。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PlaceObject;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>塔の面の斜材の型。</summary>
    public enum TowerBracing
    {
        /// <summary>斜材なし（足元を開けるとき）。</summary>
        None    = 0,
        /// <summary>X 形。</summary>
        X       = 1,
        /// <summary>片斜め（段ごとに向きを交互に変える）。</summary>
        Single  = 2,
        /// <summary>山形（下の両角から上の中央へ）。</summary>
        Chevron = 3,
        /// <summary>菱形（山形と谷形を交互に重ねる）。</summary>
        Diamond = 4,
    }

    /// <summary>腕金の向き。</summary>
    public enum TowerArmAxis
    {
        /// <summary>±X の両側へ出す。</summary>
        X = 0,
        /// <summary>±Z の両側へ出す。</summary>
        Z = 1,
    }

    public static class LatticeTowerMeshGenerator
    {
        [Serializable]
        public struct LatticeTowerParams : IEquatable<LatticeTowerParams>
        {
            public const float HeightMax       = 100f;
            public const float WidthMax        = 50f;
            public const int   PanelsMin       = 1;
            public const int   PanelsMax       = 40;
            public const float PanelRatioMin   = 0.2f;
            public const float PanelRatioMax   = 5f;
            public const float SizeMin         = 0.0005f;
            public const float SizeMax         = 2f;
            public const float TopSizeRatioMin = 0.05f;
            public const float TopSizeRatioMax = 2f;
            public const int   LacingMin       = 1;
            public const int   LacingMax       = 12;
            public const int   ArchSegmentsMin = 2;
            public const int   ArchSegmentsMax = 64;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 脚線 ──
            [PLParam(TextKey = "TowerHeights", Description = "脚線の点の高さ（下から昇順）")]
            public float[] Heights;

            [PLParam(TextKey = "TowerWidthsX", Description = "脚線の点での X 方向の幅（脚の外側どうし）")]
            public float[] WidthsX;

            [PLParam(TextKey = "TowerWidthsZ", Description = "脚線の点での Z 方向の幅（脚の外側どうし）")]
            public float[] WidthsZ;

            [PLParam(TextKey = "TowerPanels", Description = "区間（点 i と i+1 の間）ごとの段数。足りない区間は 1")]
            public int[] Panels;

            [PLParam(TextKey = "TowerBracing", Description = "区間ごとの斜材の型（0=なし 1=X 2=片斜め 3=山形 4=菱形）。足りない区間は X")]
            public int[] Bracing;

            [PLParam(TextKey = "TowerSmooth", Description = "脚線の点の間を曲線でつなぐ（オフで直線）")]
            public bool SmoothProfile;

            [PLParam(TextKey = "TowerPanelRatio", Description = "区間の中で、最上段の段の高さ ÷ 最下段の段の高さ",
                     Min = PanelRatioMin, Max = PanelRatioMax)]
            public float PanelRatio;

            [PLParam(TextKey = "TowerPlanRotation", Description = "平面形の Y 軸まわりの回転（度）。回転鉄塔は 45", Min = -180, Max = 180)]
            public float PlanRotation;

            // ── 部材 ──
            [PLParam(TextKey = "TowerLegSize", Description = "脚（主材）の太さ（根元）", Min = SizeMin, Max = SizeMax)]
            public float LegSize;

            [PLParam(TextKey = "TowerBraceSize", Description = "斜材・水平材の太さ（根元）", Min = SizeMin, Max = SizeMax)]
            public float BraceSize;

            [PLParam(TextKey = "TowerTopSizeRatio", Description = "頂部での太さ ÷ 根元での太さ", Min = TopSizeRatioMin, Max = TopSizeRatioMax)]
            public float TopSizeRatio;

            [PLParam(TextKey = "TowerHorizontals", Description = "各段の境に水平材を入れる（区間の境には常に入る）")]
            public bool Horizontals;

            [PLParam(TextKey = "TowerPlanBracing", Description = "区間の境の水平面に対角の X 材を入れる")]
            public bool PlanBracing;

            // ── 組柱 ──
            [PLParam(TextKey = "TowerLegLatticeTop", Description = "この高さまでの段の脚を組柱にする。0 で組柱なし", Min = 0, Max = HeightMax)]
            public float LegLatticeTop;

            [PLParam(TextKey = "TowerLegColumnWidth", Description = "組柱の幅（根元）", Min = 0, Max = WidthMax)]
            public float LegColumnWidth;

            [PLParam(TextKey = "TowerLegColumnWidthTop", Description = "組柱の幅（組柱の上端）", Min = 0, Max = WidthMax)]
            public float LegColumnWidthTop;

            [PLParam(TextKey = "TowerLacingDivisions", Description = "組柱の綴り材の、1 段あたりの折り返し数", Min = LacingMin, Max = LacingMax, Step = 1)]
            public int LacingDivisions;

            [PLParam(TextKey = "TowerLacingSize", Description = "組柱の綴り材の太さ（根元）", Min = SizeMin, Max = SizeMax)]
            public float LacingSize;

            // ── 足元アーチ ──
            [PLParam(TextKey = "TowerArchHeight", Description = "足元アーチの頂点の高さ。0 でアーチなし", Min = 0, Max = HeightMax)]
            public float ArchHeight;

            [PLParam(TextKey = "TowerArchSpring", Description = "アーチが脚から立ち上がる高さ", Min = 0, Max = HeightMax)]
            public float ArchSpring;

            [PLParam(TextKey = "TowerArchSegments", Description = "アーチの分割数", Min = ArchSegmentsMin, Max = ArchSegmentsMax, Step = 1)]
            public int ArchSegments;

            // ── 腕金 ──
            [PLParam(TextKey = "TowerArmHeights", Description = "腕金の付け根の高さ（上弦の高さ）")]
            public float[] ArmHeights;

            [PLParam(TextKey = "TowerArmLengths", Description = "腕金が塔の面から出る長さ")]
            public float[] ArmLengths;

            [PLParam(TextKey = "TowerArmDepths", Description = "腕金の付け根の上下の幅")]
            public float[] ArmDepths;

            [PLParam(TextKey = "TowerArmAxes", Description = "腕金の向き（0=±X 1=±Z）")]
            public int[] ArmAxes;

            [PLParam(TextKey = "TowerArmHanger", Description = "腕金の先から下げる吊り材の長さ。0 でなし", Min = 0, Max = WidthMax)]
            public float ArmHanger;

            // ── 頂部 ──
            [PLParam(TextKey = "TowerPeakHeight", Description = "頂部の柱（アンテナ）の長さ。0 でなし", Min = 0, Max = HeightMax)]
            public float PeakHeight;

            [PLParam(TextKey = "TowerPeakSize", Description = "頂部の柱の太さ（根元）", Min = SizeMin, Max = SizeMax)]
            public float PeakSize;

            // ── 門形 ──
            [PLParam(TextKey = "TowerGateSpan", Description = "門形の 2 本の塔の軸の間隔。0 で門形にしない", Min = 0, Max = WidthMax)]
            public float GateSpan;

            [PLParam(TextKey = "TowerGateBeamHeights", Description = "門形の梁の上弦の高さ")]
            public float[] GateBeamHeights;

            [PLParam(TextKey = "TowerGateBeamDepth", Description = "門形の梁の上下の幅", Min = 0.001f, Max = WidthMax)]
            public float GateBeamDepth;

            public static LatticeTowerParams Default => new LatticeTowerParams
            {
                MeshName          = "TowerBody",
                Heights           = new[] { 0f, 2.0f, 2.6f, 3.2f, 3.6f },
                WidthsX           = new[] { 1.0f, 0.5f, 0.42f, 0.36f, 0.32f },
                WidthsZ           = new[] { 1.0f, 0.5f, 0.42f, 0.36f, 0.32f },
                Panels            = new[] { 5, 2, 2, 1 },
                Bracing           = new[] { 1, 1, 1, 1 },
                SmoothProfile     = false,
                PanelRatio        = 1f,
                PlanRotation      = 0f,
                LegSize           = 0.05f,
                BraceSize         = 0.025f,
                TopSizeRatio      = 0.6f,
                Horizontals       = true,
                PlanBracing       = true,
                LegLatticeTop     = 0f,
                LegColumnWidth    = 0.15f,
                LegColumnWidthTop = 0.08f,
                LacingDivisions   = 2,
                LacingSize        = 0.012f,
                ArchHeight        = 0f,
                ArchSpring        = 0f,
                ArchSegments      = 12,
                ArmHeights        = new[] { 2.2f, 2.75f, 3.3f },
                ArmLengths        = new[] { 0.6f, 0.7f, 0.6f },
                ArmDepths         = new[] { 0.2f, 0.2f, 0.2f },
                ArmAxes           = new[] { 0, 0, 0 },
                ArmHanger         = 0.15f,
                PeakHeight        = 0.35f,
                PeakSize          = 0.04f,
                GateSpan          = 0f,
                GateBeamHeights   = new float[0],
                GateBeamDepth     = 0.3f,
            };

            public bool Equals(LatticeTowerParams o) =>
                MeshName == o.MeshName &&
                Same(Heights, o.Heights) && Same(WidthsX, o.WidthsX) && Same(WidthsZ, o.WidthsZ) &&
                Same(Panels, o.Panels) && Same(Bracing, o.Bracing) &&
                SmoothProfile == o.SmoothProfile &&
                PanelRatio == o.PanelRatio && PlanRotation == o.PlanRotation &&
                LegSize == o.LegSize && BraceSize == o.BraceSize && TopSizeRatio == o.TopSizeRatio &&
                Horizontals == o.Horizontals && PlanBracing == o.PlanBracing &&
                LegLatticeTop == o.LegLatticeTop && LegColumnWidth == o.LegColumnWidth &&
                LegColumnWidthTop == o.LegColumnWidthTop && LacingDivisions == o.LacingDivisions &&
                LacingSize == o.LacingSize &&
                ArchHeight == o.ArchHeight && ArchSpring == o.ArchSpring && ArchSegments == o.ArchSegments &&
                Same(ArmHeights, o.ArmHeights) && Same(ArmLengths, o.ArmLengths) &&
                Same(ArmDepths, o.ArmDepths) && Same(ArmAxes, o.ArmAxes) && ArmHanger == o.ArmHanger &&
                PeakHeight == o.PeakHeight && PeakSize == o.PeakSize &&
                GateSpan == o.GateSpan && Same(GateBeamHeights, o.GateBeamHeights) &&
                GateBeamDepth == o.GateBeamDepth;

            public override bool Equals(object obj) => obj is LatticeTowerParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;

            /// <summary>配列の中身ごとの複製。パネルで編集するときに元の配列を共有しないため。</summary>
            public LatticeTowerParams DeepCopy()
            {
                var c = this;
                c.Heights         = Copy(Heights);
                c.WidthsX         = Copy(WidthsX);
                c.WidthsZ         = Copy(WidthsZ);
                c.Panels          = Copy(Panels);
                c.Bracing         = Copy(Bracing);
                c.ArmHeights      = Copy(ArmHeights);
                c.ArmLengths      = Copy(ArmLengths);
                c.ArmDepths       = Copy(ArmDepths);
                c.ArmAxes         = Copy(ArmAxes);
                c.GateBeamHeights = Copy(GateBeamHeights);
                return c;
            }

            private static T[] Copy<T>(T[] a) => a == null ? new T[0] : (T[])a.Clone();

            private static bool Same<T>(T[] a, T[] b)
            {
                int na = a?.Length ?? 0, nb = b?.Length ?? 0;
                if (na != nb) return false;
                for (int i = 0; i < na; i++)
                    if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
                return true;
            }
        }

        // ================================================================
        // 脚線
        // ================================================================

        /// <summary>整えた脚線。高さ昇順、点は 2 つ以上。</summary>
        public sealed class Profile
        {
            public float[] H, WX, WZ;
            public int[]   Panels;
            public TowerBracing[] Bracing;
            public bool Smooth;

            public int   Count => H.Length;
            public float Top   => H[H.Length - 1];
            public float Bottom => H[0];

            public float WidthX(float y) => Interp(H, WX, y, Smooth);
            public float WidthZ(float y) => Interp(H, WZ, y, Smooth);
        }

        /// <summary>
        /// パラメータから脚線を整える。点の数は 3 つの配列の短い方に合わせ、高さで並べ替える。
        /// 作れないとき（点が 2 つ未満・高さが全部同じ）は null。
        /// </summary>
        public static Profile BuildProfile(LatticeTowerParams p)
        {
            int n = Math.Min(p.Heights?.Length ?? 0, Math.Min(p.WidthsX?.Length ?? 0, p.WidthsZ?.Length ?? 0));
            if (n < 2) return null;

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var hs = p.Heights;
            Array.Sort(order, (a, b) => hs[a].CompareTo(hs[b]));

            var prof = new Profile
            {
                H = new float[n], WX = new float[n], WZ = new float[n],
                Panels = new int[n - 1], Bracing = new TowerBracing[n - 1],
                Smooth = p.SmoothProfile,
            };
            for (int i = 0; i < n; i++)
            {
                prof.H[i]  = p.Heights[order[i]];
                prof.WX[i] = Mathf.Max(0f, p.WidthsX[order[i]]);
                prof.WZ[i] = Mathf.Max(0f, p.WidthsZ[order[i]]);
            }
            if (prof.Top - prof.Bottom < 1e-5f) return null;

            for (int i = 0; i < n - 1; i++)
            {
                int pn = (p.Panels != null && i < p.Panels.Length) ? p.Panels[i] : 1;
                prof.Panels[i] = Mathf.Clamp(pn, LatticeTowerParams.PanelsMin, LatticeTowerParams.PanelsMax);

                int bk = (p.Bracing != null && i < p.Bracing.Length) ? p.Bracing[i] : (int)TowerBracing.X;
                prof.Bracing[i] = Enum.IsDefined(typeof(TowerBracing), bk) ? (TowerBracing)bk : TowerBracing.X;
            }
            return prof;
        }

        /// <summary>
        /// 区間の両端を含む段の境の高さ。段の高さは区間の中で等比に変わる
        /// （最上段 ÷ 最下段 = ratio）。区間の境は重複させない。
        /// interval[k] は高さ k の直上の段が属する区間。
        /// </summary>
        public static void BuildLevels(Profile prof, float ratio, List<float> levels, List<int> interval)
        {
            levels.Clear();
            interval.Clear();
            ratio = Mathf.Clamp(ratio <= 0f ? 1f : ratio,
                LatticeTowerParams.PanelRatioMin, LatticeTowerParams.PanelRatioMax);

            levels.Add(prof.H[0]);
            for (int i = 0; i < prof.Count - 1; i++)
            {
                int n = prof.Panels[i];
                float y0 = prof.H[i], y1 = prof.H[i + 1];
                float q = n > 1 ? Mathf.Pow(ratio, 1f / (n - 1)) : 1f;

                float sum = 0f;
                for (int m = 0; m < n; m++) sum += Mathf.Pow(q, m);

                float acc = 0f;
                for (int m = 0; m < n; m++)
                {
                    interval.Add(i);
                    acc += Mathf.Pow(q, m);
                    levels.Add(m == n - 1 ? y1 : y0 + (y1 - y0) * acc / sum);
                }
            }
        }

        /// <summary>
        /// 点列 (xs, ys) を x で引く。範囲外は端の値。
        /// smooth は単調 3 次エルミート（Fritsch–Carlson）。点の間で行き過ぎない。
        /// </summary>
        public static float Interp(float[] xs, float[] ys, float x, bool smooth)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];

            int k = 0;
            while (k < n - 2 && x > xs[k + 1]) k++;

            float h = xs[k + 1] - xs[k];
            if (h <= 1e-8f) return ys[k + 1];
            float t = (x - xs[k]) / h;

            if (!smooth || n < 3) return Mathf.Lerp(ys[k], ys[k + 1], t);

            float m0 = Slope(xs, ys, k), m1 = Slope(xs, ys, k + 1);
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * ys[k] + (t3 - 2 * t2 + t) * h * m0
                 + (-2 * t3 + 3 * t2) * ys[k + 1] + (t3 - t2) * h * m1;
        }

        /// <summary>Fritsch–Carlson の接線。隣の傾きの符号が違えば 0。</summary>
        private static float Slope(float[] xs, float[] ys, int i)
        {
            int n = xs.Length;
            float D(int a) => (ys[a + 1] - ys[a]) / Mathf.Max(1e-8f, xs[a + 1] - xs[a]);

            if (i == 0)     return D(0);
            if (i == n - 1) return D(n - 2);

            float d0 = D(i - 1), d1 = D(i);
            if (d0 * d1 <= 0f) return 0f;
            float h0 = xs[i] - xs[i - 1], h1 = xs[i + 1] - xs[i];
            float w1 = 2 * h1 + h0, w2 = h1 + 2 * h0;
            return (w1 + w2) / (w1 / d0 + w2 / d1);
        }

        /// <summary>
        /// すそ野カーブの幅。y = 0 で baseWidth、y = height で topWidth。
        /// curvature が大きいほど足元で急に広がる（0 で直線）。
        /// </summary>
        public static float FlareWidth(float y, float height, float baseWidth, float topWidth, float curvature)
        {
            float t = height > 1e-6f ? Mathf.Clamp01(y / height) : 0f;
            if (Mathf.Abs(curvature) < 1e-4f) return Mathf.Lerp(baseWidth, topWidth, t);
            float e = Mathf.Exp(-curvature);
            float s = (Mathf.Exp(-curvature * t) - e) / (1f - e);   // t=0 で 1、t=1 で 0
            return topWidth + (baseWidth - topWidth) * s;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>角の符号。面 k は角 k と角 k+1 の間。</summary>
        private static readonly float[] CornerSX = { +1f, -1f, -1f, +1f };
        private static readonly float[] CornerSZ = { +1f, +1f, -1f, -1f };

        public static MeshObject Generate(LatticeTowerParams p)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "TowerBody" : p.MeshName);
            var prof = BuildProfile(p);
            if (prof == null) return mo;

            var b = new Builder(mo);

            var centers = new List<float>();
            if (p.GateSpan > 1e-5f) { centers.Add(-p.GateSpan * 0.5f); centers.Add(p.GateSpan * 0.5f); }
            else centers.Add(0f);

            foreach (float cx in centers) BuildBody(b, p, prof, cx);

            if (centers.Count == 2) BuildGateBeams(b, p, prof, centers[0], centers[1]);

            if (Mathf.Abs(p.PlanRotation) > 1e-4f)
            {
                var q = Quaternion.AngleAxis(p.PlanRotation, Vector3.up);
                foreach (var v in mo.Vertices) v.Position = q * v.Position;
            }

            mo.RecalculateNormals();
            return mo;
        }

        /// <summary>太さの倍率。根元 1、頂部 TopSizeRatio。</summary>
        private static float SizeFactor(LatticeTowerParams p, Profile prof, float y)
        {
            float t = Mathf.Clamp01((y - prof.Bottom) / (prof.Top - prof.Bottom));
            float r = Mathf.Clamp(p.TopSizeRatio, LatticeTowerParams.TopSizeRatioMin, LatticeTowerParams.TopSizeRatioMax);
            return Mathf.Lerp(1f, r, t);
        }

        private static Vector3 Corner(Profile prof, float cx, float y, int k)
            => new Vector3(cx + CornerSX[k] * prof.WidthX(y) * 0.5f, y, CornerSZ[k] * prof.WidthZ(y) * 0.5f);

        private static void BuildBody(Builder b, LatticeTowerParams p, Profile prof, float cx)
        {
            var levels   = new List<float>();
            var interval = new List<int>();
            BuildLevels(prof, p.PanelRatio, levels, interval);

            int L = levels.Count;
            var boundary = new HashSet<int>();   // 区間の境の段番号
            {
                int j = 0;
                boundary.Add(0);
                for (int i = 0; i < prof.Count - 1; i++) { j += prof.Panels[i]; boundary.Add(j); }
            }

            float legBase   = Mathf.Max(LatticeTowerParams.SizeMin, p.LegSize);
            float braceBase = Mathf.Max(LatticeTowerParams.SizeMin, p.BraceSize);
            float lacBase   = Mathf.Max(LatticeTowerParams.SizeMin, p.LacingSize);

            for (int j = 0; j < L - 1; j++)
            {
                float y0 = levels[j], y1 = levels[j + 1];
                float ym = (y0 + y1) * 0.5f;
                float f  = SizeFactor(p, prof, ym);
                var bracing = prof.Bracing[interval[j]];

                bool lattice = p.LegLatticeTop > 0f && y1 <= p.LegLatticeTop + 1e-4f
                            && p.LegColumnWidth > 1e-5f;

                // ── 脚
                for (int k = 0; k < 4; k++)
                {
                    var a = Corner(prof, cx, y0, k);
                    var c = Corner(prof, cx, y1, k);
                    if (lattice) BuildLatticeLeg(b, p, prof, a, c, k, legBase * f, lacBase * f);
                    else         b.Bar(a, c, legBase * SizeFactor(p, prof, y0), legBase * SizeFactor(p, prof, y1));
                }

                // ── 面の斜材
                for (int k = 0; k < 4; k++)
                {
                    int k1 = (k + 1) % 4;
                    var a0 = Corner(prof, cx, y0, k);
                    var b0 = Corner(prof, cx, y0, k1);
                    var a1 = Corner(prof, cx, y1, k);
                    var b1 = Corner(prof, cx, y1, k1);
                    float s = braceBase * f;

                    switch (bracing)
                    {
                        case TowerBracing.X:
                            b.Bar(a0, b1, s); b.Bar(b0, a1, s);
                            break;
                        case TowerBracing.Single:
                            if ((j & 1) == 0) b.Bar(a0, b1, s); else b.Bar(b0, a1, s);
                            break;
                        case TowerBracing.Chevron:
                        {
                            var m1 = (a1 + b1) * 0.5f;
                            b.Bar(a0, m1, s); b.Bar(b0, m1, s);
                            break;
                        }
                        case TowerBracing.Diamond:
                            if ((j & 1) == 0)
                            {
                                var m1 = (a1 + b1) * 0.5f;
                                b.Bar(a0, m1, s); b.Bar(b0, m1, s);
                            }
                            else
                            {
                                var m0 = (a0 + b0) * 0.5f;
                                b.Bar(m0, a1, s); b.Bar(m0, b1, s);
                            }
                            break;
                    }
                }

                // ── 段の上の水平材（地面の段には入れない）
                bool needH = p.Horizontals || boundary.Contains(j + 1)
                          || bracing == TowerBracing.Chevron;
                if (needH)
                {
                    float s = braceBase * SizeFactor(p, prof, y1);
                    for (int k = 0; k < 4; k++)
                        b.Bar(Corner(prof, cx, y1, k), Corner(prof, cx, y1, (k + 1) % 4), s);
                }

                // ── 区間の境の水平面の対角材
                if (p.PlanBracing && boundary.Contains(j + 1))
                {
                    float s = braceBase * SizeFactor(p, prof, y1);
                    b.Bar(Corner(prof, cx, y1, 0), Corner(prof, cx, y1, 2), s);
                    b.Bar(Corner(prof, cx, y1, 1), Corner(prof, cx, y1, 3), s);
                }
            }

            BuildArch(b, p, prof, cx, braceBase);
            BuildArms(b, p, prof, cx, braceBase);
            BuildPeak(b, p, prof, cx, braceBase);
        }

        /// <summary>
        /// 組柱 1 段ぶん。外側の主材が脚線の角に来る。内側の 3 本は X・Z の内向きへ柱の幅だけずらす。
        /// 柱の 4 面にジグザグの綴り材、段の上端に柱の水平の帯を入れる。
        /// </summary>
        private static void BuildLatticeLeg(
            Builder b, LatticeTowerParams p, Profile prof,
            Vector3 lo, Vector3 hi, int k, float chordSize, float lacingSize)
        {
            float top = Mathf.Max(p.LegLatticeTop, prof.Bottom + 1e-4f);
            float W(float y)
            {
                float t = Mathf.Clamp01((y - prof.Bottom) / (top - prof.Bottom));
                return Mathf.Max(0f, Mathf.Lerp(p.LegColumnWidth, p.LegColumnWidthTop, t));
            }

            float sx = CornerSX[k], sz = CornerSZ[k];
            Vector3 Off(int c, float w) => new Vector3(
                (c == 1 || c == 3) ? -sx * w : 0f, 0f,
                (c == 2 || c == 3) ? -sz * w : 0f);

            float wl = W(lo.y), wh = W(hi.y);
            var bot = new Vector3[4];
            var tp  = new Vector3[4];
            for (int c = 0; c < 4; c++)
            {
                bot[c] = lo + Off(c, wl);
                tp[c]  = hi + Off(c, wh);
                b.Bar(bot[c], tp[c], chordSize);
            }

            // 柱の 4 面（主材の対）。0-1, 1-3, 3-2, 2-0 の順に周る。
            int[,] faces = { { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 } };
            int div = Mathf.Clamp(p.LacingDivisions, LatticeTowerParams.LacingMin, LatticeTowerParams.LacingMax);

            for (int fi = 0; fi < 4; fi++)
            {
                int ca = faces[fi, 0], cb = faces[fi, 1];
                for (int m = 0; m < div; m++)
                {
                    float t0 = (float)m / div, t1 = (float)(m + 1) / div;
                    var pa = Vector3.Lerp(bot[ca], tp[ca], t0);
                    var pb = Vector3.Lerp(bot[cb], tp[cb], t1);
                    if ((m & 1) == 1)
                    {
                        pa = Vector3.Lerp(bot[cb], tp[cb], t0);
                        pb = Vector3.Lerp(bot[ca], tp[ca], t1);
                    }
                    b.Bar(pa, pb, lacingSize);
                }
                b.Bar(tp[ca], tp[cb], lacingSize);
            }
        }

        /// <summary>足元アーチ。4 面それぞれ、隣り合う脚の間に半正弦の弧を架ける。</summary>
        private static void BuildArch(Builder b, LatticeTowerParams p, Profile prof, float cx, float braceBase)
        {
            if (p.ArchHeight <= 1e-5f || p.ArchHeight <= p.ArchSpring) return;
            int seg = Mathf.Clamp(p.ArchSegments, LatticeTowerParams.ArchSegmentsMin, LatticeTowerParams.ArchSegmentsMax);
            float spring = Mathf.Max(prof.Bottom, p.ArchSpring);

            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                Vector3 prev = Vector3.zero;
                for (int m = 0; m <= seg; m++)
                {
                    float u = (float)m / seg;
                    float y = spring + (p.ArchHeight - spring) * Mathf.Sin(Mathf.PI * u);
                    var pt = Vector3.Lerp(Corner(prof, cx, y, k), Corner(prof, cx, y, k1), u);
                    if (m > 0)
                    {
                        float s = braceBase * 1.5f * SizeFactor(p, prof, (prev.y + pt.y) * 0.5f);
                        b.Bar(prev, pt, s);
                    }
                    prev = pt;
                }
            }
        }

        /// <summary>
        /// 腕金。付け根は塔の面の 2 角（上弦は高さ y、下弦は y − 深さ）、先端は面の中心線上の 1 点。
        /// 上弦・下弦の間にジグザグの斜材、先端から吊り材を下げる。
        /// </summary>
        private static void BuildArms(Builder b, LatticeTowerParams p, Profile prof, float cx, float braceBase)
        {
            int n = p.ArmHeights?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                float y   = p.ArmHeights[i];
                float len = (p.ArmLengths != null && i < p.ArmLengths.Length) ? p.ArmLengths[i] : 0f;
                float dep = (p.ArmDepths  != null && i < p.ArmDepths.Length)  ? p.ArmDepths[i]  : 0.2f;
                int axis  = (p.ArmAxes    != null && i < p.ArmAxes.Length)    ? p.ArmAxes[i]    : 0;
                if (len <= 1e-5f) continue;
                dep = Mathf.Max(0.001f, dep);

                float yb = y - dep;
                float s  = braceBase * SizeFactor(p, prof, y);

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 Root(float yy, float lateral)
                    {
                        if (axis == (int)TowerArmAxis.Z)
                            return new Vector3(cx + lateral * prof.WidthX(yy) * 0.5f, yy, side * prof.WidthZ(yy) * 0.5f);
                        return new Vector3(cx + side * prof.WidthX(yy) * 0.5f, yy, lateral * prof.WidthZ(yy) * 0.5f);
                    }

                    var t1 = Root(y, +1f);  var t2 = Root(y, -1f);
                    var b1 = Root(yb, +1f); var b2 = Root(yb, -1f);

                    Vector3 tip = axis == (int)TowerArmAxis.Z
                        ? new Vector3(cx, y, side * (prof.WidthZ(y) * 0.5f + len))
                        : new Vector3(cx + side * (prof.WidthX(y) * 0.5f + len), y, 0f);

                    b.Bar(t1, tip, s); b.Bar(t2, tip, s);
                    b.Bar(b1, tip, s); b.Bar(b2, tip, s);
                    b.Bar(t1, t2, s);  b.Bar(b1, b2, s);

                    // 上弦と下弦の間のジグザグ（両側の縦の面）
                    int div = Mathf.Max(1, Mathf.RoundToInt(len / dep));
                    for (int m = 0; m < div; m++)
                    {
                        float u0 = (float)m / div, u1 = (float)(m + 1) / div;
                        b.Bar(Vector3.Lerp(t1, tip, u0), Vector3.Lerp(b1, tip, u1), s * 0.8f);
                        b.Bar(Vector3.Lerp(t2, tip, u0), Vector3.Lerp(b2, tip, u1), s * 0.8f);
                    }

                    if (p.ArmHanger > 1e-5f)
                        b.Bar(tip, tip + Vector3.down * p.ArmHanger, s * 0.8f);
                }
            }
        }

        /// <summary>頂部の柱（先細り）と、頂部の 4 角から柱への支え。</summary>
        private static void BuildPeak(Builder b, LatticeTowerParams p, Profile prof, float cx, float braceBase)
        {
            if (p.PeakHeight <= 1e-5f) return;
            float top = prof.Top;
            float ps  = Mathf.Max(LatticeTowerParams.SizeMin, p.PeakSize);

            var p0 = new Vector3(cx, top, 0f);
            var p1 = new Vector3(cx, top + p.PeakHeight, 0f);
            b.Bar(p0, p1, ps, ps * 0.3f);

            float wx = prof.WidthX(top), wz = prof.WidthZ(top);
            float rise = Mathf.Min(p.PeakHeight * 0.3f, Mathf.Max(wx, wz) * 0.6f);
            if (rise <= 1e-5f || Mathf.Max(wx, wz) <= 1e-5f) return;

            var hub = new Vector3(cx, top + rise, 0f);
            float s = braceBase * SizeFactor(p, prof, top);
            for (int k = 0; k < 4; k++) b.Bar(Corner(prof, cx, top, k), hub, s);
        }

        /// <summary>
        /// 門形の梁。左の塔の内側の面から右の塔の内側の面まで、上弦・下弦の 4 本の主材と、
        /// 前後の面の X 材・上下の面の片斜め材・区切りの縦材で組む。
        /// </summary>
        private static void BuildGateBeams(Builder b, LatticeTowerParams p, Profile prof, float cxL, float cxR)
        {
            int n = p.GateBeamHeights?.Length ?? 0;
            float depth = Mathf.Max(0.001f, p.GateBeamDepth);
            float braceBase = Mathf.Max(LatticeTowerParams.SizeMin, p.BraceSize);
            float legBase   = Mathf.Max(LatticeTowerParams.SizeMin, p.LegSize);

            for (int i = 0; i < n; i++)
            {
                float yt = p.GateBeamHeights[i];
                float yb = yt - depth;

                Vector3 P(float x, float y, float sz) => new Vector3(x, y, sz * prof.WidthZ(y) * 0.5f);

                float xl0 = cxL + prof.WidthX(yt) * 0.5f, xr0 = cxR - prof.WidthX(yt) * 0.5f;
                float xl1 = cxL + prof.WidthX(yb) * 0.5f, xr1 = cxR - prof.WidthX(yb) * 0.5f;
                if (xr0 - xl0 <= 1e-4f) continue;

                int div = Mathf.Max(1, Mathf.RoundToInt((xr0 - xl0) / depth));
                float chord = legBase * 0.8f * SizeFactor(p, prof, yt);
                float s     = braceBase * SizeFactor(p, prof, yt);

                Vector3 TopAt(float u, float sz) => P(Mathf.Lerp(xl0, xr0, u), yt, sz);
                Vector3 BotAt(float u, float sz) => P(Mathf.Lerp(xl1, xr1, u), yb, sz);

                for (int z = -1; z <= 1; z += 2)
                {
                    b.Bar(TopAt(0, z), TopAt(1, z), chord);
                    b.Bar(BotAt(0, z), BotAt(1, z), chord);
                }

                for (int m = 0; m <= div; m++)
                {
                    float u = (float)m / div;
                    for (int z = -1; z <= 1; z += 2) b.Bar(TopAt(u, z), BotAt(u, z), s);
                    b.Bar(TopAt(u, -1), TopAt(u, +1), s);
                    b.Bar(BotAt(u, -1), BotAt(u, +1), s);

                    if (m == div) continue;
                    float u1 = (float)(m + 1) / div;
                    for (int z = -1; z <= 1; z += 2)
                    {
                        b.Bar(TopAt(u, z), BotAt(u1, z), s);
                        b.Bar(BotAt(u, z), TopAt(u1, z), s);
                    }
                    b.Bar(TopAt(u, -1), TopAt(u1, +1), s);
                    b.Bar(BotAt(u, +1), BotAt(u1, -1), s);
                }
            }
        }

        // ================================================================
        // 角材
        // ================================================================

        /// <summary>角材を MeshObject へ足す。1 本 = 頂点 8・四角形 6。</summary>
        private sealed class Builder
        {
            private readonly MeshObject _mo;
            public Builder(MeshObject mo) { _mo = mo; }

            public void Bar(Vector3 a, Vector3 b, float size) => Bar(a, b, size, size);

            /// <summary>
            /// a から b への角材。断面は正方形で、一辺は a 側 sizeA・b 側 sizeB。
            /// 断面の向きはワールド X を部材に直交する面へ落とした向きにそろえる
            /// （ほぼ X 向きの部材ではワールド Z を使う）。
            /// </summary>
            public void Bar(Vector3 a, Vector3 b, float sizeA, float sizeB)
            {
                var d = b - a;
                float len = d.magnitude;
                if (len < 1e-6f) return;
                d /= len;

                var u = Vector3.right - d * Vector3.Dot(d, Vector3.right);
                if (u.sqrMagnitude < 1e-4f) u = Vector3.forward - d * Vector3.Dot(d, Vector3.forward);
                u.Normalize();
                var w = Vector3.Cross(d, u);

                float ha = sizeA * 0.5f, hb = sizeB * 0.5f;
                int i0 = _mo.VertexCount;

                // 断面の 4 隅：(-u-w) (+u-w) (+u+w) (-u+w)。u から w へ回る順。
                Add(a + (-u - w) * ha); Add(a + (u - w) * ha); Add(a + (u + w) * ha); Add(a + (-u + w) * ha);
                Add(b + (-u - w) * hb); Add(b + (u - w) * hb); Add(b + (u + w) * hb); Add(b + (-u + w) * hb);

                // w = d × u なので、(q_i, q_i+1, q_i+5, q_i+4) の順が外向き。
                for (int i = 0; i < 4; i++)
                {
                    int n = (i + 1) % 4;
                    _mo.AddQuad(i0 + i, i0 + n, i0 + 4 + n, i0 + 4 + i);
                }
                _mo.AddQuad(i0 + 0, i0 + 3, i0 + 2, i0 + 1);   // a 側のふた（-d 向き）
                _mo.AddQuad(i0 + 4, i0 + 5, i0 + 6, i0 + 7);   // b 側のふた（+d 向き）
            }

            private void Add(Vector3 p) => _mo.Vertices.Add(new Vertex(p, Vector2.zero, Vector3.up));
        }
    }

    // ====================================================================
    // デッキ
    // ====================================================================

    /// <summary>
    /// 指定オブジェクトを塔の軸上の高さへ置く。配置元が複数なら 1 つに結合して置く。
    /// 配置元のパーツ ID・サブ ID はそのまま使う。
    /// </summary>
    public static class TowerDeckMeshGenerator
    {
        [Serializable]
        public struct TowerDeckParams : IEquatable<TowerDeckParams>
        {
            public const float ScaleMin = 0.001f;
            public const float ScaleMax = 100f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "TowerDeckHeight", Description = "置く高さ", Min = -1000, Max = 1000)]
            public float Height;

            [PLParam(TextKey = "Scale", Description = "倍率", Min = ScaleMin, Max = ScaleMax)]
            public float Scale;

            [PLParam(TextKey = "TowerDeckRotationY", Description = "Y 軸まわりの回転（度）", Min = -360, Max = 360)]
            public float RotationY;

            [PLParam(TextKey = "TowerDeckCenter", Description = "配置元の外接箱の XZ 中心を軸に、底を高さに合わせる（オフで配置元の原点を軸上の高さへ）")]
            public bool CenterSource;

            [PLParam(TextKey = "PlaceIncludeChildren", Description = "配置元の子孫も一緒に置く")]
            public bool IncludeChildren;

            public static TowerDeckParams Default => new TowerDeckParams
            {
                MeshName        = "TowerDeck",
                Height          = 1f,
                Scale           = 1f,
                RotationY       = 0f,
                CenterSource    = true,
                IncludeChildren = true,
            };

            public bool Equals(TowerDeckParams o) =>
                MeshName == o.MeshName && Height == o.Height && Scale == o.Scale &&
                RotationY == o.RotationY && CenterSource == o.CenterSource &&
                IncludeChildren == o.IncludeChildren;

            public override bool Equals(object obj) => obj is TowerDeckParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>配置元が無いときは頂点 0 のメッシュ。</summary>
        public static MeshObject Generate(TowerDeckParams p, List<MeshObject> sources)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "TowerDeck" : p.MeshName);
            if (sources == null) return mo;

            var srcs = new List<MeshObject>();
            foreach (var s in sources) if (s != null && s.VertexCount > 0) srcs.Add(s);
            if (srcs.Count == 0) return mo;

            var src = srcs.Count == 1 ? srcs[0].Clone() : MeshObjectAppendOps.Combine(srcs, mo.Name);
            if (src == null || src.VertexCount == 0) return mo;

            if (p.CenterSource)
            {
                var bb = new Bounds(src.Vertices[0].Position, Vector3.zero);
                for (int i = 1; i < src.VertexCount; i++) bb.Encapsulate(src.Vertices[i].Position);
                var shift = new Vector3(-bb.center.x, -bb.min.y, -bb.center.z);
                foreach (var v in src.Vertices) v.Position += shift;
            }

            var q = Quaternion.AngleAxis(p.RotationY, Vector3.up);
            float scale = p.Scale <= 0f ? 1f : p.Scale;
            PlaceObjectMeshGenerator.AppendInstance(
                mo, src, new Vector3(0f, p.Height, 0f),
                q * Vector3.right, Vector3.up, q * Vector3.forward, scale, false, 0);

            mo.RecalculateNormals();
            return mo;
        }
    }
}
