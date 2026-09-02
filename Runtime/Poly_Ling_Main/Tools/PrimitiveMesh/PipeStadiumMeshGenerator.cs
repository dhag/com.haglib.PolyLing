// PipeStadiumMeshGenerator.cs
// パイプ接続用小判型（手のひらのもと）メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【小判型（StadiumBoxMeshGenerator）との違い】
//   小判型は「半円筒 + 直方体 + 半円筒」で、直線部の分割は長さ方向の等分だった。
//   こちらは半径 R の円を N 個並べ、隣り合う円のあいだを幅 W の矩形で埋めた形にする。
//   外形の輪郭は小判型と同じだが、側面の分割位置が円周分割の X 投影
//   （x = 円の中心 + R·cos φ）になるので、正面・側面から見た面の幅が円柱と一致する。
//   上下のフタは円板 N 枚・すみパッチ・矩形に分かれていて、円板だけを外せる。
//   外した穴の縁は円周分割数ちょうどの環になるので、そのままパイプの端とつなげる。
//
// 【寸法】
//   ピッチ  = 2R + W
//   長さ X  = 2R·N + W·(N-1)   （指定ではなく上の値から決まる）
//   奥行き Z = 2R
//   円 i の中心 x_i = -長さ/2 + R + ピッチ·i、z = 0
//   高さ Y は指定値をそのまま押し出す。
//
// 【構成】
//   側面
//     XZ 平面の輪郭を Y 方向へ押し出す。輪郭は
//       上の直線 z = +R（各円の x_i + R·cos φ と、矩形の幅 W を GapSegments 等分した点）
//       右端の半円（円 N-1 の右半分）
//       下の直線 z = -R（上の直線の逆順）
//       左端の半円（円 0 の左半分）
//     の順に、外向き法線が付く並びで作る。
//
//   上下のフタ
//     円板     … 円 1 個ぶん。中心からの極座標格子（円周 = RadialSegments、半径 = RadialRings）。
//     すみパッチ… 円弧と外周直線 z = ±R のあいだ。円弧の点を Z 方向へ落とした先が外周側の点。
//                 φ = 90° / 270° で幅が 0 になるので 90° ごとの 4 枚に分け、
//                 幅 0 の端の列だけ三角形で張る（面積 0 の面を作らないため）。
//                 端の円（i = 0, N-1）の外向き半分は輪郭そのものなので、内向き 2 枚だけ張る。
//     矩形     … 隣り合う円のあいだ。X は GapSegments、Z は 2·RadialRings 分割。
//                 Z を 2 倍にしてあるので、すみパッチの φ = 0° / 180° の列と点がそろう。
//
// 【フタのモード】
//   Full          … 円板・すみパッチ・矩形をすべて張る
//   None          … 張らない
//   HoleAtCircles … 円板だけ張らない。円の位置に穴が N 個空く（パイプ接続用）
//
// 【手のひらモード】
//   A（上・指がつく側）→ AB → B → BC → C → CD → D（下・手首側）を Y 方向に積む。
//   断面 A の円は N 個、断面 B / C / D は N + 左の本数 + 右の本数 個（親指のぶん）。
//   増えるのは B の 1 か所だけなので、そこは段差になり、y = yB に水平な棚を張る。
//   A の円は B の 左の本数 … 左の本数 + N - 1 と同じ位置に置く（棚の内側の境界が
//   A の端の半円と一致するようにするため、A だけ原点中心にしない）。
//   輪郭の点数が A 区間と BCD 区間で違うので、押し出しは 2 本に分ける。
//   断面 A / B / C は矩形部の幅が W、断面 D だけ幅を GapWidthD に置き換える。
//   半径は変えないので、D では円の位置が内側へ寄って平面部だけが X に縮む。
//   区間 AB / BC はまっすぐ押し出し、区間 CD は幅を W から GapWidthD へ線形に変える。
//   全高は AB + BC + CD で、単段の Height は使わない。
//   上のフタは断面 A、下のフタは断面 D。フタのモードは単段と同じものを使う。
//
//   幅が変わる区間では端の半円が Y 方向に傾くので、法線に傾きを入れる。
//   輪郭の 2 次元法線を (nx, nz)、その点の x の Y 微分を x' とすると、
//   面の法線は (nx, -nx·x', nz) を正規化したものになる
//   （前後の平面は nx = 0 なので傾かない。傾くのは端の半円だけ）。
//   幅を 0 にすると矩形部の点が潰れて断面ごとの点数が変わるため、
//   手のひらモードでは矩形部の幅と D の幅の下限を GapWidthPalmMin にする。
//
//   棚は「断面 B の輪郭の内側」から「断面 A の輪郭の内側」を除いた部分。
//   B の分解のうち、A に属さないものだけを張る：
//     増えた円の円板・すみパッチ、そのあいだの矩形部、
//     および A の端の円の外向きすみパッチ 2 枚。
//   A の端の円の円板と内向きすみパッチは A の内部（形状の中）なので張らない。
//   棚の円板だけは上のフタの指定に従う（すべてふさぐ以外では張らず、親指の穴になる）。
//
// 【面の巻き順】
//   cross(v1 - v0, v2 - v1) が外向きになる向きで張る
//   （CubeMeshGenerator.AddQuadFace と同じ規約）。
//
// 【継ぎ目】
//   パッチ境界の頂点は位置が一致する。結合はパネル側の「重複頂点をマージ」に任せる
//   （小判型・角丸直方体・カプセルと同じ扱い）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>上下のフタの張り方。</summary>
    public enum PipeStadiumCapMode
    {
        /// <summary>すべてふさぐ。</summary>
        Full = 0,
        /// <summary>すべて抜く。</summary>
        None = 1,
        /// <summary>円の部分だけ抜く。パイプをつなぐ穴になる。</summary>
        HoleAtCircles = 2,
    }

    public static class PipeStadiumMeshGenerator
    {
        /// <summary>円の個数の下限・上限。</summary>
        public const int CircleCountMin = 2;
        public const int CircleCountMax = 32;

        /// <summary>円周 360° の分割数の下限・上限。実際には 4 の倍数へ丸める。</summary>
        public const int RadialSegmentsMin = 8;
        public const int RadialSegmentsMax = 64;

        /// <summary>矩形部（幅方向）の分割数の下限・上限。</summary>
        public const int GapSegmentsMin = 1;
        public const int GapSegmentsMax = 32;

        /// <summary>高さ方向の分割数の下限・上限。</summary>
        public const int HeightSegmentsMin = 1;
        public const int HeightSegmentsMax = 32;

        /// <summary>円板の半径方向の分割数の下限・上限。</summary>
        public const int RadialRingsMin = 1;
        public const int RadialRingsMax = 16;

        /// <summary>手のひらモードの区間ごとの分割数の下限・上限。</summary>
        public const int PalmSegmentsMin = 1;
        public const int PalmSegmentsMax = 32;

        /// <summary>親指（B 以降で増える円）の本数の下限・上限。片側ごと。</summary>
        public const int ThumbCountMin = 0;
        public const int ThumbCountMax = 8;

        private const float Eps = 1e-6f;

        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct PipeStadiumParams : IEquatable<PipeStadiumParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>円の半径の下限・上限</summary>
            public const float RadiusMin = 0.02f;
            public const float RadiusMax = 5f;

            /// <summary>矩形部の幅の下限・上限</summary>
            public const float GapWidthMin = 0f;
            public const float GapWidthMax = 5f;

            /// <summary>
            /// 手のひらモードでの矩形部の幅の下限。
            /// 0 を許すと断面ごとに輪郭の点数が変わって区間を張れなくなる。
            /// </summary>
            public const float GapWidthPalmMin = 0.01f;

            /// <summary>手のひらモードの区間の高さの下限・上限</summary>
            public const float PalmHeightMin = 0.02f;
            public const float PalmHeightMax = 10f;

            /// <summary>高さの下限・上限</summary>
            public const float HeightMin = 0.02f;
            public const float HeightMax = 10f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            /// <summary>長さ方向に並べる円の個数</summary>
            [PLParam(TextKey = "PipeStadiumCircleCount", Description = "長さ方向に並べる円の個数",
                     Min = CircleCountMin, Max = CircleCountMax, Step = 1)]
            public int CircleCount;
            /// <summary>円の半径。奥行き Z はこの 2 倍になる。</summary>
            [PLParam(TextKey = "PipeStadiumRadius", Description = "円の半径。奥行き Z はこの 2 倍になる",
                     Min = RadiusMin, Max = RadiusMax)]
            public float Radius;
            /// <summary>隣り合う円のあいだの矩形部の幅。0 で円どうしが接する。</summary>
            [PLParam(TextKey = "PipeStadiumGapWidth", Description = "隣り合う円のあいだの矩形部の幅。0 で円どうしが接する",
                     Min = GapWidthMin, Max = GapWidthMax)]
            public float GapWidth;
            /// <summary>Y 方向の全高</summary>
            [PLParam(TextKey = "PipeStadiumHeight", Description = "高さ", Min = HeightMin, Max = HeightMax)]
            public float Height;

            /// <summary>円周 360° の分割数。4 の倍数へ丸める。</summary>
            [PLParam(TextKey = "PipeStadiumRadialSegments", Description = "円周 360°の分割数。4 の倍数へ丸める",
                     Min = RadialSegmentsMin, Max = RadialSegmentsMax, Step = 1)]
            public int RadialSegments;
            /// <summary>矩形部（幅方向）の分割数</summary>
            [PLParam(TextKey = "PipeStadiumGapSegments", Description = "矩形部（幅方向）の分割数",
                     Min = GapSegmentsMin, Max = GapSegmentsMax, Step = 1)]
            public int GapSegments;
            /// <summary>高さ方向の分割数</summary>
            [PLParam(TextKey = "PipeStadiumHeightSegments", Description = "高さ方向の分割数",
                     Min = HeightSegmentsMin, Max = HeightSegmentsMax, Step = 1)]
            public int HeightSegments;
            /// <summary>円板の半径方向の分割数。矩形部の Z 方向はこの 2 倍になる。</summary>
            [PLParam(TextKey = "PipeStadiumRadialRings", Description = "円板の半径方向の分割数。矩形部の Z 方向はこの 2 倍になる",
                     Min = RadialRingsMin, Max = RadialRingsMax, Step = 1)]
            public int RadialRings;

            /// <summary>A〜D の 4 段を積んだ手のひらのベース形状にする。</summary>
            [PLParam(TextKey = "PipeStadiumPalm", Description = "A〜D の 4 段を積んだ手のひらのベース形状にする。高さ Y の代わりに区間ごとの高さを使う")]
            public bool Palm;

            /// <summary>A と B のあいだの高さ</summary>
            [PLParam(TextKey = "PipeStadiumHeightAB", Description = "A と B のあいだの高さ", Min = PalmHeightMin, Max = PalmHeightMax)]
            public float HeightAB;
            /// <summary>B と C のあいだの高さ</summary>
            [PLParam(TextKey = "PipeStadiumHeightBC", Description = "B と C のあいだの高さ", Min = PalmHeightMin, Max = PalmHeightMax)]
            public float HeightBC;
            /// <summary>C と D のあいだの高さ。この区間で矩形部の幅が変わる。</summary>
            [PLParam(TextKey = "PipeStadiumHeightCD", Description = "C と D のあいだの高さ。この区間で矩形部の幅が変わる", Min = PalmHeightMin, Max = PalmHeightMax)]
            public float HeightCD;

            /// <summary>A と B のあいだの分割数</summary>
            [PLParam(TextKey = "PipeStadiumSegmentsAB", Description = "A と B のあいだの分割数", Min = PalmSegmentsMin, Max = PalmSegmentsMax, Step = 1)]
            public int SegmentsAB;
            /// <summary>B と C のあいだの分割数</summary>
            [PLParam(TextKey = "PipeStadiumSegmentsBC", Description = "B と C のあいだの分割数", Min = PalmSegmentsMin, Max = PalmSegmentsMax, Step = 1)]
            public int SegmentsBC;
            /// <summary>C と D のあいだの分割数</summary>
            [PLParam(TextKey = "PipeStadiumSegmentsCD", Description = "C と D のあいだの分割数", Min = PalmSegmentsMin, Max = PalmSegmentsMax, Step = 1)]
            public int SegmentsCD;

            /// <summary>断面 B 以降で -X 側に増やす円の本数（親指）。</summary>
            [PLParam(TextKey = "PipeStadiumThumbLeft", Description = "断面 B 以降で -X 側に増やす円の本数（親指）",
                     Min = ThumbCountMin, Max = ThumbCountMax, Step = 1)]
            public int ThumbLeft;
            /// <summary>断面 B 以降で +X 側に増やす円の本数（親指）。</summary>
            [PLParam(TextKey = "PipeStadiumThumbRight", Description = "断面 B 以降で +X 側に増やす円の本数（親指）",
                     Min = ThumbCountMin, Max = ThumbCountMax, Step = 1)]
            public int ThumbRight;

            /// <summary>断面 D の矩形部の幅。半径は変えないので平面部だけが X に縮む。</summary>
            [PLParam(TextKey = "PipeStadiumGapWidthD", Description = "断面 D の矩形部の幅。半径は変えないので平面部だけが X に縮む",
                     Min = GapWidthPalmMin, Max = GapWidthMax)]
            public float GapWidthD;

            /// <summary>上のフタの張り方</summary>
            [PLParam(TextKey = "PipeStadiumCapTop", Description = "上のフタ。すべてふさぐ / すべて抜く / 円の部分だけ抜く")]
            public PipeStadiumCapMode CapTopMode;
            /// <summary>下のフタの張り方</summary>
            [PLParam(TextKey = "PipeStadiumCapBottom", Description = "下のフタ。すべてふさぐ / すべて抜く / 円の部分だけ抜く")]
            public PipeStadiumCapMode CapBottomMode;

            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static PipeStadiumParams Default => new PipeStadiumParams
            {
                MeshName       = "PipeStadium",
                CircleCount    = 3,
                Radius         = 0.5f,
                GapWidth       = 0.2f,
                Height         = 0.5f,
                RadialSegments = 16,
                GapSegments    = 1,
                HeightSegments = 2,
                RadialRings    = 2,
                Palm           = false,
                HeightAB       = 0.2f,
                HeightBC       = 0.2f,
                HeightCD       = 0.2f,
                SegmentsAB     = 2,
                SegmentsBC     = 2,
                SegmentsCD     = 2,
                ThumbLeft      = 0,
                ThumbRight     = 1,
                GapWidthD      = 0.05f,
                CapTopMode     = PipeStadiumCapMode.Full,
                CapBottomMode  = PipeStadiumCapMode.Full,
                FlipFaces      = false,
                Pivot          = Vector3.zero,
            };

            public bool Equals(PipeStadiumParams o) =>
                MeshName == o.MeshName &&
                CircleCount == o.CircleCount &&
                Mathf.Approximately(Radius,   o.Radius)   &&
                Mathf.Approximately(GapWidth, o.GapWidth) &&
                Mathf.Approximately(Height,   o.Height)   &&
                RadialSegments == o.RadialSegments &&
                GapSegments    == o.GapSegments    &&
                HeightSegments == o.HeightSegments &&
                RadialRings    == o.RadialRings    &&
                Palm           == o.Palm           &&
                Mathf.Approximately(HeightAB,  o.HeightAB)  &&
                Mathf.Approximately(HeightBC,  o.HeightBC)  &&
                Mathf.Approximately(HeightCD,  o.HeightCD)  &&
                SegmentsAB     == o.SegmentsAB     &&
                SegmentsBC     == o.SegmentsBC     &&
                SegmentsCD     == o.SegmentsCD     &&
                ThumbLeft      == o.ThumbLeft      &&
                ThumbRight     == o.ThumbRight     &&
                Mathf.Approximately(GapWidthD, o.GapWidthD) &&
                CapTopMode     == o.CapTopMode     &&
                CapBottomMode  == o.CapBottomMode  &&
                FlipFaces      == o.FlipFaces      &&
                Pivot          == o.Pivot;

            public override bool Equals(object obj) => obj is PipeStadiumParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 寸法
        // ================================================================

        /// <summary>円周分割数を 4 の倍数へ丸めた値。すみパッチを 90° ごとに切るために要る。</summary>
        public static int NormalizeRadialSegments(int radialSegments)
        {
            int rs = Mathf.Clamp(radialSegments, RadialSegmentsMin, RadialSegmentsMax);
            rs = Mathf.RoundToInt(rs / 4f) * 4;
            return Mathf.Clamp(rs, RadialSegmentsMin, RadialSegmentsMax);
        }

        /// <summary>矩形部の幅の丸め。手のひらモードでは下限が上がる。</summary>
        private static float ClampGap(float g, bool palm) => Mathf.Clamp(
            g,
            palm ? PipeStadiumParams.GapWidthPalmMin : PipeStadiumParams.GapWidthMin,
            PipeStadiumParams.GapWidthMax);

        /// <summary>断面 A（手のひらモードでないときは全体）の X 方向の全長。</summary>
        public static float LengthOf(PipeStadiumParams p)
        {
            int n = Mathf.Clamp(p.CircleCount, CircleCountMin, CircleCountMax);
            float r = Mathf.Max(1e-4f, p.Radius);
            return 2f * r * n + ClampGap(p.GapWidth, p.Palm) * (n - 1);
        }

        /// <summary>断面 B 以降の円の個数（指 ＋ 左右の親指）。</summary>
        public static int WideCircleCount(PipeStadiumParams p)
            => Mathf.Clamp(p.CircleCount, CircleCountMin, CircleCountMax)
             + Mathf.Clamp(p.ThumbLeft,  ThumbCountMin, ThumbCountMax)
             + Mathf.Clamp(p.ThumbRight, ThumbCountMin, ThumbCountMax);

        /// <summary>断面 B / C の X 方向の全長。手のひらモードのときだけ意味を持つ。</summary>
        public static float LengthOfB(PipeStadiumParams p)
        {
            int nb = WideCircleCount(p);
            float r = Mathf.Max(1e-4f, p.Radius);
            return 2f * r * nb + ClampGap(p.GapWidth, true) * (nb - 1);
        }

        /// <summary>断面 D の X 方向の全長。手のひらモードのときだけ意味を持つ。</summary>
        public static float LengthOfD(PipeStadiumParams p)
        {
            int nb = WideCircleCount(p);
            float r = Mathf.Max(1e-4f, p.Radius);
            return 2f * r * nb + ClampGap(p.GapWidthD, true) * (nb - 1);
        }

        /// <summary>Y 方向の全高。手のひらモードでは 3 区間の合計になる。</summary>
        public static float TotalHeightOf(PipeStadiumParams p)
        {
            if (!p.Palm) return Mathf.Max(1e-4f, p.Height);
            return Mathf.Clamp(p.HeightAB, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax)
                 + Mathf.Clamp(p.HeightBC, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax)
                 + Mathf.Clamp(p.HeightCD, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax);
        }

        /// <summary>Z 方向の全奥行き。円の直径そのもの。</summary>
        public static float DepthOf(PipeStadiumParams p) => 2f * Mathf.Max(1e-4f, p.Radius);

        /// <summary>矩形部の幅 g のときの円の中心 X。</summary>
        private static float[] CentersOf(int n, float r, float g)
        {
            float pitch  = 2f * r + g;
            float length = 2f * r * n + g * (n - 1);
            float x0     = -length * 0.5f + r;
            var cx = new float[n];
            for (int i = 0; i < n; i++) cx[i] = x0 + pitch * i;
            return cx;
        }

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(PipeStadiumParams p)
        {
            int   n = Mathf.Clamp(p.CircleCount, CircleCountMin, CircleCountMax);
            float r = Mathf.Max(1e-4f, p.Radius);
            float w = ClampGap(p.GapWidth, p.Palm);

            int rs = NormalizeRadialSegments(p.RadialSegments);
            int gs = Mathf.Clamp(p.GapSegments, GapSegmentsMin, GapSegmentsMax);
            int rr = Mathf.Clamp(p.RadialRings, RadialRingsMin, RadialRingsMax);
            var cir = new Circle(rs);

            string name = string.IsNullOrEmpty(p.MeshName) ? "PipeStadium" : p.MeshName;
            var mo = new MeshObject(name);

            if (!p.Palm) BuildSingle(mo, p, n, r, w, cir, gs, rr);
            else         BuildPalm  (mo, p, n, r, w, cir, gs, rr);

            AssignBoxProjectionUV(mo);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(mo);
            return mo;
        }

        /// <summary>単段。全行で断面が同じなので押し出しは 1 本。</summary>
        private static void BuildSingle(
            MeshObject mo, PipeStadiumParams p, int n, float r, float w, Circle cir, int gs, int rr)
        {
            float h  = Mathf.Max(1e-4f, p.Height);
            int   hs = Mathf.Clamp(p.HeightSegments, HeightSegmentsMin, HeightSegmentsMax);

            var ys    = new List<float>(hs + 1);
            var rings = new List<List<Sample2>>(hs + 1);
            var cx    = CentersOf(n, r, w);
            var ol    = BuildOutline(cx, r, w, cir, gs);
            for (int j = 0; j <= hs; j++) { ys.Add(-h * 0.5f + h * j / hs); rings.Add(ol); }

            AddWall(mo, rings, ys);
            AddCap(mo, cx, r, w, cir, gs, rr,  h * 0.5f, true,  p.CapTopMode);
            AddCap(mo, cx, r, w, cir, gs, rr, -h * 0.5f, false, p.CapBottomMode);
        }

        /// <summary>
        /// 手のひら。A 区間（yB〜yA、円 n 個）と BCD 区間（yD〜yB、円 nb 個）を別々に押し出し、
        /// 段差になる y = yB へ棚を張る。
        /// </summary>
        private static void BuildPalm(
            MeshObject mo, PipeStadiumParams p, int n, float r, float w, Circle cir, int gs, int rr)
        {
            float wD  = ClampGap(p.GapWidthD, true);
            float hAB = Mathf.Clamp(p.HeightAB, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax);
            float hBC = Mathf.Clamp(p.HeightBC, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax);
            float hCD = Mathf.Clamp(p.HeightCD, PipeStadiumParams.PalmHeightMin, PipeStadiumParams.PalmHeightMax);
            int sAB = Mathf.Clamp(p.SegmentsAB, PalmSegmentsMin, PalmSegmentsMax);
            int sBC = Mathf.Clamp(p.SegmentsBC, PalmSegmentsMin, PalmSegmentsMax);
            int sCD = Mathf.Clamp(p.SegmentsCD, PalmSegmentsMin, PalmSegmentsMax);
            int tl  = Mathf.Clamp(p.ThumbLeft,  ThumbCountMin, ThumbCountMax);
            int tr  = Mathf.Clamp(p.ThumbRight, ThumbCountMin, ThumbCountMax);
            int nb  = n + tl + tr;

            float yD = -(hAB + hBC + hCD) * 0.5f;
            float yC = yD + hCD;
            float yB = yC + hBC;
            float yA = yB + hAB;

            // ── BCD 区間（下から上へ）──
            var ysB = new List<float>(sCD + sBC + 1);
            var gwB = new List<float>(sCD + sBC + 1);
            ysB.Add(yD); gwB.Add(wD);
            for (int k = 1; k <= sCD; k++)
            {
                float t = (float)k / sCD;
                ysB.Add(k == sCD ? yC : yD + hCD * t);
                gwB.Add(k == sCD ? w  : wD + (w - wD) * t);
            }
            for (int k = 1; k <= sBC; k++)
            {
                ysB.Add(k == sBC ? yB : yC + hBC * ((float)k / sBC));
                gwB.Add(w);
            }

            var cxsB   = new List<float[]>(ysB.Count);
            var ringsB = new List<List<Sample2>>(ysB.Count);
            for (int j = 0; j < ysB.Count; j++)
            {
                var c = CentersOf(nb, r, gwB[j]);
                cxsB.Add(c);
                ringsB.Add(BuildOutline(c, r, gwB[j], cir, gs));
            }
            AddWall(mo, ringsB, ysB);

            // ── A 区間（下から上へ）──
            // 円は断面 B の tl … tl + n - 1 と同じ値をそのまま使う。式を共有するので
            // 棚の内側の境界（A の端の半円）と B 側の点が一致する。
            float[] cxB = cxsB[cxsB.Count - 1];
            var cxA = new float[n];
            for (int i = 0; i < n; i++) cxA[i] = cxB[tl + i];

            var olA    = BuildOutline(cxA, r, w, cir, gs);
            var ysA    = new List<float>(sAB + 1);
            var ringsA = new List<List<Sample2>>(sAB + 1);
            for (int k = 0; k <= sAB; k++)
            {
                ysA.Add(k == sAB ? yA : yB + hAB * ((float)k / sAB));
                ringsA.Add(olA);
            }
            AddWall(mo, ringsA, ysA);

            // ── 段差の棚 ──
            if (tl > 0 || tr > 0)
                AddShelf(mo, cxB, r, w, cir, gs, rr, yB, n, tl, tr,
                         p.CapTopMode == PipeStadiumCapMode.Full);

            AddCap(mo, cxA,      r, w,  cir, gs, rr, yA, true,  p.CapTopMode);
            AddCap(mo, cxsB[0],  r, wD, cir, gs, rr, yD, false, p.CapBottomMode);
        }

        // ================================================================
        // 輪郭（XZ 平面）
        // ================================================================

        /// <summary>輪郭 1 点。P / N とも (x, z) の 2 成分。</summary>
        private struct Sample2
        {
            public Vector2 P;
            public Vector2 N;
        }

        /// <summary>
        /// 円周分割の向きを持つ表。
        ///
        /// 【なぜ表にするか】
        ///   同じ向きでも、輪郭は負の添字（端の半円）や上半分の添字で、フタは下半分の
        ///   添字で参照する。Mathf.Cos に別々の引数を渡すと値が float で一致せず
        ///   （実測で約 3e-7）、継ぎ目に別頂点ができて境界辺が残る。
        ///   そこで第 1 象限だけを計算し、残りは符号の入れ替えで写して、
        ///   Cos[rs-k] == Cos[k] と Sin[rs-k] == -Sin[k] が厳密に成り立つ表を作る。
        ///   象限の境目（0°/90°/180°/270°）は丸め誤差を残さないよう直接入れる。
        ///   添字は 0 以上 rs 未満へ畳んでから引く。
        /// </summary>
        private sealed class Circle
        {
            public readonly int Segments;
            public readonly int Quarter;
            public readonly float[] Cos;
            public readonly float[] Sin;

            public Circle(int segments)
            {
                Segments = segments;
                Quarter  = segments / 4;
                Cos = new float[segments];
                Sin = new float[segments];

                int q = Quarter;
                for (int k = 1; k < q; k++)
                {
                    float f = 2f * Mathf.PI * k / segments;
                    Cos[k] = Mathf.Cos(f);
                    Sin[k] = Mathf.Sin(f);
                }
                Cos[0] = 1f;  Sin[0] = 0f;
                Cos[q] = 0f;  Sin[q] = 1f;

                for (int k = q + 1; k < 2 * q; k++) { int j = 2 * q - k; Cos[k] = -Cos[j]; Sin[k] =  Sin[j]; }
                Cos[2 * q] = -1f; Sin[2 * q] = 0f;

                for (int k = 2 * q + 1; k < 3 * q; k++) { int j = k - 2 * q; Cos[k] = -Cos[j]; Sin[k] = -Sin[j]; }
                Cos[3 * q] = 0f; Sin[3 * q] = -1f;

                for (int k = 3 * q + 1; k < 4 * q; k++) { int j = 4 * q - k; Cos[k] =  Cos[j]; Sin[k] = -Sin[j]; }
            }

            private int Fold(int k)
            {
                int m = k % Segments;
                return m < 0 ? m + Segments : m;
            }

            public float CosAt(int k) => Cos[Fold(k)];
            public float SinAt(int k) => Sin[Fold(k)];
        }

        /// <summary>
        /// 上の直線 z = +R の X 座標列。左端（円 0 の上）から右端（円 N-1 の上）まで。
        ///
        /// 円 i のぶんは円周分割点を Z 方向へ落とした x = x_i + R·cos φ。
        /// 端の円は内向き 1/4 だけ（外向き半分は端の半円になるので直線には出ない）。
        /// 矩形部は幅 W を GapSegments 分割する。
        ///
        /// 【継ぎ目の点を 1 つに保つ】
        ///   円 i の右端（φ = 0°）と円 i+1 の左端（φ = 180°）は、あいだの矩形部を挟んで
        ///   同じ値になりうる。x_i + R + W と x_(i+1) - R は式が違うので float では一致せず、
        ///   許容値で落とす方法では円の個数が増えるほど誤差が積もって落とし損ねる。
        ///   そこで 2 個目以降の円の φ = 180° は初めから出さない。
        ///   W = 0 のときは矩形部そのものを出さないので、継ぎ目は円 i の右端 1 点になる。
        ///
        ///   矩形部の内部の点は、フタの矩形（AddGrid）と同じ補間式で置いて位置をそろえる。
        /// </summary>
        private static List<float> BuildTopLineX(float[] cx, float r, float w, Circle cir, int gs)
        {
            int n = cx.Length;
            int q = cir.Quarter;
            int rs = cir.Segments;
            var xs = new List<float>(n * (rs / 2 + gs) + 2);

            void Ap(float x)
            {
                if (xs.Count > 0 && Mathf.Abs(xs[xs.Count - 1] - x) < 1e-7f) return;
                xs.Add(x);
            }

            for (int i = 0; i < n; i++)
            {
                int kStart = (i == 0)     ? q : 2 * q;
                int kEnd   = (i == n - 1) ? q : 0;
                for (int k = kStart; k >= kEnd; k--)
                {
                    if (i > 0 && k == 2 * q) continue;
                    Ap(cx[i] + r * cir.CosAt(k));
                }

                if (i < n - 1 && w > Eps)
                {
                    float xl = cx[i] + r, xr = cx[i + 1] - r;
                    for (int j = 1; j <= gs; j++)
                        Ap(j == gs ? xr : xl + (xr - xl) * ((float)j / gs));
                }
            }
            return xs;
        }

        /// <summary>
        /// 閉じた輪郭を作る。外向き法線が付く向き（上の直線を +X へ進む向き）に並べる。
        /// 直線と円弧の継ぎ目は位置が一致するので、重なる点は落とす。
        /// </summary>
        private static List<Sample2> BuildOutline(float[] cx, float r, float w, Circle cir, int gs)
        {
            int n = cx.Length;
            int q = cir.Quarter;
            int rs = cir.Segments;
            var xs = BuildTopLineX(cx, r, w, cir, gs);

            var list = new List<Sample2>(2 * xs.Count + rs + 4);

            void Ap(Vector2 pos, Vector2 nrm)
            {
                if (list.Count > 0 && (list[list.Count - 1].P - pos).sqrMagnitude < 1e-14f) return;
                list.Add(new Sample2 { P = pos, N = nrm });
            }

            // 上の直線 z = +R
            for (int i = 0; i < xs.Count; i++) Ap(new Vector2(xs[i], r), new Vector2(0f, 1f));

            // 右端の半円（円 N-1 の右半分）: +z → +x → -z
            for (int k = q; k >= -q; k--)
            {
                var nn = new Vector2(cir.CosAt(k), cir.SinAt(k));
                Ap(new Vector2(cx[n - 1] + r * nn.x, r * nn.y), nn);
            }

            // 下の直線 z = -R
            for (int i = xs.Count - 1; i >= 0; i--) Ap(new Vector2(xs[i], -r), new Vector2(0f, -1f));

            // 左端の半円（円 0 の左半分）: -z → -x → +z
            for (int k = -q; k >= -3 * q; k--)
            {
                var nn = new Vector2(cir.CosAt(k), cir.SinAt(k));
                Ap(new Vector2(cx[0] + r * nn.x, r * nn.y), nn);
            }

            // 閉じるときの重なりを落とす
            while (list.Count > 1 &&
                   (list[list.Count - 1].P - list[0].P).sqrMagnitude < 1e-14f)
                list.RemoveAt(list.Count - 1);

            return list;
        }

        // ================================================================
        // 側面
        // ================================================================

        /// <summary>
        /// 行ごとの輪郭を下から上へ張る。行の点数は全行で同じであること。
        ///
        /// 幅が変わる区間では端の半円が Y 方向に傾く。輪郭の 2 次元法線を (nx, nz)、
        /// その点の x の Y 微分を x' とすると、面の法線は (nx, -nx·x', nz) になる。
        /// x' は上下の行との中央差分で求める（端の行は片側差分）。
        /// 前後の平面は nx = 0 なので、幅が変わっても法線は ±Z のまま動かない。
        /// </summary>
        private static void AddWall(MeshObject mo, List<List<Sample2>> rings, List<float> ys)
        {
            int rows = rings.Count;
            if (rows < 2) return;
            int n = rings[0].Count;
            if (n < 3) return;
            for (int j = 1; j < rows; j++) if (rings[j].Count != n) return;

            int start = mo.VertexCount;
            for (int j = 0; j < rows; j++)
            {
                float v = (float)j / (rows - 1);
                float y = ys[j];
                int ja = Mathf.Max(0, j - 1), jb = Mathf.Min(rows - 1, j + 1);
                float dy = ys[jb] - ys[ja];
                var ol = rings[j];

                for (int i = 0; i < n; i++)
                {
                    Vector2 pp = ol[i].P, nn = ol[i].N;
                    float slope = (dy > 1e-9f) ? (rings[jb][i].P.x - rings[ja][i].P.x) / dy : 0f;
                    mo.Vertices.Add(new Vertex(
                        new Vector3(pp.x, y, pp.y),
                        new Vector2((float)i / n, v),
                        new Vector3(nn.x, -nn.x * slope, nn.y).normalized));
                }
            }
            for (int j = 0; j < rows - 1; j++)
            {
                int lower = start + j * n;
                int upper = start + (j + 1) * n;
                for (int i = 0; i < n; i++)
                {
                    int i2 = (i + 1) % n;
                    mo.AddQuad(lower + i, lower + i2, upper + i2, upper + i);
                }
            }
        }

        // ================================================================
        // 上下のフタ
        // ================================================================

        private static void AddCap(
            MeshObject mo, float[] cx, float r, float w, Circle cir, int gs, int rr,
            float y, bool up, PipeStadiumCapMode mode)
        {
            if (mode == PipeStadiumCapMode.None) return;

            int n = cx.Length;

            for (int i = 0; i < n; i++)
            {
                // すみパッチ。端の円は内向きの 2 枚だけ張る。
                if (i != n - 1) AddRightCorners(mo, cx[i], r, cir, rr, y, up);
                if (i != 0)     AddLeftCorners (mo, cx[i], r, cir, rr, y, up);

                if (mode == PipeStadiumCapMode.Full)
                    AddDisc(mo, cx[i], r, cir, rr, y, up);
            }

            if (w > Eps)
                for (int i = 0; i < n - 1; i++)
                    AddGapRect(mo, cx[i] + r, cx[i + 1] - r, r, y, up, gs, rr);
        }

        /// <summary>
        /// 段差の棚。断面 B の分解のうち、断面 A に属さないものだけを張る。
        ///
        /// 左の棚（tl &gt; 0）:
        ///   円 0 … tl-1 … 円板（disc が true のときだけ）とすみパッチ
        ///   円 tl        … 内向き（-X 側）のすみパッチだけ。円板と +X 側は A の内部
        ///   矩形部       … 円 0-1 … 円 tl-1 - tl
        /// 右の棚（tr &gt; 0）は左右を入れ替えたもの。
        ///
        /// 棚は上向き（+Y）に張る。
        /// </summary>
        private static void AddShelf(
            MeshObject mo, float[] cx, float r, float w, Circle cir, int gs, int rr,
            float y, int nA, int tl, int tr, bool disc)
        {
            int nb = cx.Length;
            int e  = tl + nA - 1;          // 断面 A の右端に当たる断面 B 側の添字

            if (tl > 0)
            {
                for (int i = 0; i < tl; i++)
                {
                    if (i != 0) AddLeftCorners(mo, cx[i], r, cir, rr, y, true);
                    AddRightCorners(mo, cx[i], r, cir, rr, y, true);
                    if (disc) AddDisc(mo, cx[i], r, cir, rr, y, true);
                }
                AddLeftCorners(mo, cx[tl], r, cir, rr, y, true);

                if (w > Eps)
                    for (int i = 0; i < tl; i++)
                        AddGapRect(mo, cx[i] + r, cx[i + 1] - r, r, y, true, gs, rr);
            }

            if (tr > 0)
            {
                AddRightCorners(mo, cx[e], r, cir, rr, y, true);

                for (int i = e + 1; i < nb; i++)
                {
                    AddLeftCorners(mo, cx[i], r, cir, rr, y, true);
                    if (i != nb - 1) AddRightCorners(mo, cx[i], r, cir, rr, y, true);
                    if (disc) AddDisc(mo, cx[i], r, cir, rr, y, true);
                }

                if (w > Eps)
                    for (int i = e; i < nb - 1; i++)
                        AddGapRect(mo, cx[i] + r, cx[i + 1] - r, r, y, true, gs, rr);
            }
        }

        /// <summary>
        /// +X 側のすみパッチ 2 枚。
        ///   第 1 象限 (0 … q)   : 外周 z = +R、幅 0 は終端側
        ///   第 4 象限 (3q … 4q) : 外周 z = -R、幅 0 は始端側
        /// </summary>
        private static void AddRightCorners(
            MeshObject mo, float cx, float r, Circle cir, int rr, float y, bool up)
        {
            int q = cir.Quarter;
            AddCornerPatch(mo, cx, r, cir, rr, y, up, 0,     q,     r, false);
            AddCornerPatch(mo, cx, r, cir, rr, y, up, 3 * q, 4 * q, -r, true);
        }

        /// <summary>
        /// -X 側のすみパッチ 2 枚。
        ///   第 2 象限 (q … 2q)  : 外周 z = +R、幅 0 は始端側
        ///   第 3 象限 (2q … 3q) : 外周 z = -R、幅 0 は終端側
        /// </summary>
        private static void AddLeftCorners(
            MeshObject mo, float cx, float r, Circle cir, int rr, float y, bool up)
        {
            int q = cir.Quarter;
            AddCornerPatch(mo, cx, r, cir, rr, y, up, q,     2 * q, r, true);
            AddCornerPatch(mo, cx, r, cir, rr, y, up, 2 * q, 3 * q, -r, false);
        }

        /// <summary>隣り合う円のあいだの矩形部 1 枚。Z 方向は 2·rr 分割。</summary>
        private static void AddGapRect(
            MeshObject mo, float xl, float xr, float r, float y, bool up, int gs, int rr)
        {
            Vector3 nrm = up ? Vector3.up : Vector3.down;
            if (up)
                AddGrid(mo,
                    new Vector3(xl, y,  r), new Vector3(xr, y,  r),
                    new Vector3(xr, y, -r), new Vector3(xl, y, -r),
                    nrm, gs, 2 * rr);
            else
                AddGrid(mo,
                    new Vector3(xl, y, -r), new Vector3(xr, y, -r),
                    new Vector3(xr, y,  r), new Vector3(xl, y,  r),
                    nrm, gs, 2 * rr);
        }

        /// <summary>
        /// 円弧と外周直線 z = zOuter のあいだのすみパッチ 1 枚（90° ぶん）。
        ///
        /// 円弧の点 (R·cosφ, R·sinφ) を Z 方向へ落とした (R·cosφ, zOuter) が外周側の点になる。
        /// 半径方向は rr 分割。φ = 90° / 270° の端では円弧と外周が一致して幅が 0 になるので、
        /// その列は 1 頂点にまとめ、隣の列とのあいだは三角形で張る。
        /// pinchAtStart が true のとき幅 0 の端は kA 側、false のとき kB 側。
        /// </summary>
        private static void AddCornerPatch(
            MeshObject mo, float cx, float r, Circle cir, int rr,
            float y, bool up, int kA, int kB, float zOuter, bool pinchAtStart)
        {
            int cols = kB - kA + 1;
            if (cols < 2 || rr < 1) return;

            Vector3 nrm = up ? Vector3.up : Vector3.down;
            int pinch = pinchAtStart ? 0 : cols - 1;

            var idx = new int[rr + 1, cols];
            for (int c = 0; c < cols; c++)
            {
                int   k  = kA + c;
                float px = cx + r * cir.CosAt(k);
                float az = r * cir.SinAt(k);
                float u  = (float)c / (cols - 1);

                if (c == pinch)
                {
                    int vid = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(px, y, zOuter), new Vector2(u, 1f), nrm));
                    for (int t = 0; t <= rr; t++) idx[t, c] = vid;
                }
                else
                {
                    for (int t = 0; t <= rr; t++)
                    {
                        float tt = (float)t / rr;
                        idx[t, c] = mo.VertexCount;
                        mo.Vertices.Add(new Vertex(
                            new Vector3(px, y, az + (zOuter - az) * tt),
                            new Vector2(u, tt), nrm));
                    }
                }
            }

            for (int t = 0; t < rr; t++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = idx[t, c], b = idx[t, c + 1];
                    int d = idx[t + 1, c + 1], e = idx[t + 1, c];

                    if (a == e)
                    {
                        if (up) mo.AddTriangle(a, b, d);
                        else    mo.AddTriangle(a, d, b);
                    }
                    else if (b == d)
                    {
                        if (up) mo.AddTriangle(a, b, e);
                        else    mo.AddTriangle(a, e, b);
                    }
                    else
                    {
                        if (up) mo.AddQuad(a, b, d, e);
                        else    mo.AddQuad(a, e, d, b);
                    }
                }
        }

        /// <summary>
        /// 円板 1 枚。中心を極とする極座標格子（円周 = rs、半径 = rr）。
        /// 最外周は円周分割点そのものなので、すみパッチや端の半円と位置が一致する。
        /// </summary>
        private static void AddDisc(
            MeshObject mo, float cx, float r, Circle cir, int rr, float y, bool up)
        {
            Vector3 nrm = up ? Vector3.up : Vector3.down;
            int rs = cir.Segments;

            int center = mo.VertexCount;
            mo.Vertices.Add(new Vertex(new Vector3(cx, y, 0f), new Vector2(0.5f, 0.5f), nrm));

            int ringStart = mo.VertexCount;
            for (int k = 1; k <= rr; k++)
            {
                float rad = r * k / rr;
                for (int c = 0; c < rs; c++)
                {
                    float co = cir.Cos[c], si = cir.Sin[c];
                    mo.Vertices.Add(new Vertex(
                        new Vector3(cx + rad * co, y, rad * si),
                        new Vector2(0.5f + 0.5f * (rad / r) * co, 0.5f + 0.5f * (rad / r) * si),
                        nrm));
                }
            }

            // 中心の帯
            for (int c = 0; c < rs; c++)
            {
                int c2 = (c + 1) % rs;
                if (up) mo.AddTriangle(center, ringStart + c2, ringStart + c);
                else    mo.AddTriangle(center, ringStart + c,  ringStart + c2);
            }

            // 残りの帯
            for (int k = 0; k < rr - 1; k++)
            {
                int inner = ringStart + k * rs;
                int outer = inner + rs;
                for (int c = 0; c < rs; c++)
                {
                    int c2 = (c + 1) % rs;
                    if (up) mo.AddQuad(inner + c, inner + c2, outer + c2, outer + c);
                    else    mo.AddQuad(inner + c, outer + c,  outer + c2, inner + c2);
                }
            }
        }

        // ================================================================
        // パッチ
        // ================================================================

        /// <summary>
        /// 平面の格子。v0 → v1 が U、v0 → v3 が V。
        /// cross(v1 - v0, v2 - v1) が normal と同じ向きになる並びで渡すこと。
        /// </summary>
        private static void AddGrid(
            MeshObject mo, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector3 normal, int divU, int divV)
        {
            int start = mo.VertexCount;
            for (int iv = 0; iv <= divV; iv++)
            {
                float tv = (float)iv / divV;
                Vector3 lp = Vector3.Lerp(v0, v3, tv);
                Vector3 rp = Vector3.Lerp(v1, v2, tv);
                for (int iu = 0; iu <= divU; iu++)
                {
                    float tu = (float)iu / divU;
                    mo.Vertices.Add(new Vertex(Vector3.Lerp(lp, rp, tu), new Vector2(tu, tv), normal));
                }
            }
            int cols = divU + 1;
            for (int iv = 0; iv < divV; iv++)
                for (int iu = 0; iu < divU; iu++)
                {
                    int i0 = start + iv * cols + iu;
                    mo.AddQuad(i0, i0 + 1, i0 + cols + 1, i0 + cols);
                }
        }

        // ================================================================
        // UV
        // ================================================================

        /// <summary>
        /// 各面 [0,1] のボックス投影。頂点法線の支配軸で 6 面のどれかへ割り当て、
        /// メッシュ AABB で正規化する（小判型・角丸直方体と同じ考え方）。
        /// </summary>
        private static void AssignBoxProjectionUV(MeshObject mo)
        {
            if (mo == null || mo.Vertices.Count == 0) return;

            Vector3 min = mo.Vertices[0].Position, max = min;
            foreach (var v in mo.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            float dx = Mathf.Max(1e-6f, max.x - min.x);
            float dy = Mathf.Max(1e-6f, max.y - min.y);
            float dz = Mathf.Max(1e-6f, max.z - min.z);
            Vector3 center = (min + max) * 0.5f;

            foreach (var v in mo.Vertices)
            {
                Vector3 n = (v.Normals != null && v.Normals.Count > 0)
                    ? v.Normals[0]
                    : (v.Position - center);

                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);

                float nx = (v.Position.x - min.x) / dx;
                float ny = (v.Position.y - min.y) / dy;
                float nz = (v.Position.z - min.z) / dz;

                Vector2 uv;
                if (ay >= ax && ay >= az)
                    uv = (n.y >= 0f) ? new Vector2(nx, 1f - nz) : new Vector2(nx, nz);
                else if (ax >= az)
                    uv = (n.x >= 0f) ? new Vector2(1f - nz, ny) : new Vector2(nz, ny);
                else
                    uv = (n.z >= 0f) ? new Vector2(nx, ny) : new Vector2(1f - nx, ny);

                if (v.UVs.Count == 0) v.UVs.Add(uv);
                else                  v.UVs[0] = uv;
            }
        }
    }
}
