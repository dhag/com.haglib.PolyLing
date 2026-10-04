// StadiumBoxMeshGenerator.cs
// 小判型（両側面が半円筒の直方体）メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【寸法】
//   R = Depth / 2
//   a = Length / 2 - R                                （直線部の半長さ）
//   b = RoundTopBottom ? Height / 2 - R : Height / 2   （直線部の半高さ）
//   AABB は常に Length × Height × Depth。
//   R が Length/2 を超える指定（上下丸めのときは Height/2 も）は R 側を抑える。
//   抑えた分だけ Depth が縮む。
//
// 【構成】
//   RoundTopBottom = false:
//     XZ 平面の小判型輪郭（長さ 2a の直線 2 本 + 半径 R の半円 2 個）を Y 方向へ押し出す。
//     前後は平面、左右は半円筒（軸 Y）、上下は小判型の平フタ。
//     輪郭は直線と円弧が接するので法線が一致する。側面は 1 枚の連続した壁として張る。
//     平フタは「半円 + 長方形 + 半円」の 3 つに分けて張る。
//     半円は中心を極とする極座標格子（円周方向 = 丸みの分割数、半径方向 = radSeg）。
//     直線側の境界は半径 1 本ぶん（radSeg 分割）が中心を挟んで 2 本並ぶので、
//     長方形の Z 方向は 2·radSeg 分割にして境界の頂点位置をそろえる（T 字接合を作らない）。
//     radSeg は丸みの分割数から決める（(CapSegments + 1) / 2）。
//
//     上下のフタは CapTop / CapBottom で個別に省略できる（円筒と同じ扱い）。
//     両方外すと小判型の筒（側面だけ）になる。
//
//   RoundTopBottom = true:
//     XY 平面の矩形（2a × 2b）を半径 R で全方向へ膨らませた形。
//       前後の平面 2 枚（z = ±R）
//       4 辺の半円筒 4 本（左右は軸 Y、上下は軸 X、いずれも 180°）
//       四隅の 1/4 球 4 個（方位角 90° × 極角 180°）
//     円筒どうしの継ぎ目が 1/4 球になる。
//
// 【半小判型（Half = true）】
//   上の形を原点を通る平面で半分に割り、片側だけを残して切り口を平面で閉じる。
//     HalfAxis = Length : x = 0 で割り、x ≥ 0 側を残す（片端だけ丸い D 字形）。切り口の法線は -X。
//     HalfAxis = Depth  : z = 0 で割り、z ≥ 0 側を残す（背面が丸いかまぼこ形）。切り口の法線は -Z。
//   作り方：割る面の上に頂点の列が乗るよう分割数を倍にして全体を作り、
//   負の側の面と使われなくなった頂点を消してから、切り口を張る。
//   分割数は残った側に対する数として扱う。
//     Length : X の分割数を倍にして作る（残った直線部が LengthSegments 分割になる）。
//     Depth  : 丸みの分割数を倍にして作る（残った 1/4 円が CapSegments 分割になる）。
//   切り口の輪郭の頂点は、残った面の境界の頂点と位置が一致するように張る
//   （結合は他のパッチと同じく「重複頂点をマージ」に任せる）。
//
// 【面の巻き順】
//   cross(v1 - v0, v2 - v1) が外向きになる向きで張る
//   （CubeMeshGenerator.AddQuadFace と同じ規約）。
//
// 【継ぎ目】
//   パッチ境界の頂点は位置が一致する。結合はパネル側の「重複頂点をマージ」に任せる
//   （角丸直方体・カプセルと同じ扱い）。極（1/4 球の ±Z 端）は三角形で張るため
//   面積 0 の面は出ない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>半小判型の割り方。</summary>
    public enum StadiumHalfAxis
    {
        /// <summary>長さ方向で割る（x = 0 で割り、x ≥ 0 側を残す）。</summary>
        Length = 0,
        /// <summary>奥行き方向で割る（z = 0 で割り、z ≥ 0 側を残す）。</summary>
        Depth = 1,
    }

    public static class StadiumBoxMeshGenerator
    {
        /// <summary>半円 180° の分割数の下限・上限。</summary>
        public const int CapSegmentsMin = 2;
        public const int CapSegmentsMax = 64;

        /// <summary>直線部の分割数の下限・上限。</summary>
        public const int LineSegmentsMin = 1;
        public const int LineSegmentsMax = 64;

        private const float Eps = 1e-6f;

        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct StadiumBoxParams : IEquatable<StadiumBoxParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>長さの下限・上限</summary>
            public const float LengthMin = 0.1f;
            public const float LengthMax = 10f;

            /// <summary>高さの下限・上限</summary>
            public const float HeightMin = 0.1f;
            public const float HeightMax = 10f;

            /// <summary>奥行き（＝丸みの直径）の下限・上限</summary>
            public const float DepthMin = 0.02f;
            public const float DepthMax = 10f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            /// <summary>X 方向の全長</summary>
            [PLParam(TextKey = "StadiumLength", Description = "長さ（直線部＋両端の丸み）", Min = LengthMin, Max = LengthMax)]
            public float Length;
            /// <summary>Y 方向の全高</summary>
            [PLParam(TextKey = "StadiumHeight", Description = "高さ", Min = HeightMin, Max = HeightMax)]
            public float Height;
            /// <summary>Z 方向の全奥行き。半円筒の直径になる。</summary>
            [PLParam(TextKey = "StadiumDepth", Description = "奥行き。丸みの直径にあたる", Min = DepthMin, Max = DepthMax)]
            public float Depth;

            /// <summary>上下も半円筒にして、四隅を 1/4 球でつなぐ。</summary>
            [PLParam(TextKey = "StadiumRoundTopBottom", Description = "上下も半円筒にする。四隅は 1/4 球でつながる")]
            public bool RoundTopBottom;

            /// <summary>半小判型にする（小判型を半分に割った片側だけを作り、切り口を平面で閉じる）。</summary>
            [PLParam(TextKey = "StadiumHalf", Description = "半小判型にする。小判型を原点を通る平面で半分に割った片側だけを作り、切り口を平面で閉じる")]
            public bool Half;
            /// <summary>半小判型の割り方。Half = false のときは無視される。</summary>
            [PLParam(TextKey = "StadiumHalfAxis", Description = "半小判型の割り方。Length = 長さ方向で割る（x ≥ 0 側を残す）/ Depth = 奥行き方向で割る（z ≥ 0 側を残す）。Half = false のときは無視される")]
            public StadiumHalfAxis HalfAxis;

            /// <summary>半円 180° の分割数</summary>
            [PLParam(TextKey = "StadiumCapSegments", Description = "半円 180°の分割数", Min = CapSegmentsMin,
                     Max = CapSegmentsMax, Step = 1)]
            public int CapSegments;
            /// <summary>直線部（X 方向）の分割数</summary>
            [PLParam(TextKey = "StadiumLengthSegments", Description = "直線部（長さ方向）の分割数", Min = LineSegmentsMin,
                     Max = LineSegmentsMax, Step = 1)]
            public int LengthSegments;
            /// <summary>直線部（Y 方向）の分割数</summary>
            [PLParam(TextKey = "StadiumHeightSegments", Description = "直線部（高さ方向）の分割数", Min = LineSegmentsMin,
                     Max = LineSegmentsMax, Step = 1)]
            public int HeightSegments;

            /// <summary>上のフタを張る。RoundTopBottom = true のときは無視される。</summary>
            [PLParam(TextKey = "CapTop", Description = "上のフタを張る。上下も丸めるときは無視される")]
            public bool CapTop;
            /// <summary>下のフタを張る。RoundTopBottom = true のときは無視される。</summary>
            [PLParam(TextKey = "CapBottom", Description = "下のフタを張る。上下も丸めるときは無視される")]
            public bool CapBottom;

            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static StadiumBoxParams Default => new StadiumBoxParams
            {
                MeshName       = "StadiumBox",
                Length         = 2f,
                Height         = 1f,
                Depth          = 1f,
                RoundTopBottom = false,
                Half           = false,
                HalfAxis       = StadiumHalfAxis.Length,
                CapSegments    = 12,
                LengthSegments = 2,
                HeightSegments = 2,
                CapTop         = true,
                CapBottom      = true,
                FlipFaces      = false,
                Pivot          = Vector3.zero,
            };

            public bool Equals(StadiumBoxParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(Length, o.Length) &&
                Mathf.Approximately(Height, o.Height) &&
                Mathf.Approximately(Depth,  o.Depth)  &&
                RoundTopBottom == o.RoundTopBottom &&
                Half           == o.Half           &&
                HalfAxis       == o.HalfAxis       &&
                CapSegments    == o.CapSegments    &&
                LengthSegments == o.LengthSegments &&
                HeightSegments == o.HeightSegments &&
                CapTop         == o.CapTop         &&
                CapBottom      == o.CapBottom      &&
                FlipFaces      == o.FlipFaces      &&
                Pivot          == o.Pivot;

            public override bool Equals(object obj) => obj is StadiumBoxParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(StadiumBoxParams p)
        {
            float length = Mathf.Max(1e-4f, p.Length);
            float height = Mathf.Max(1e-4f, p.Height);
            float depth  = Mathf.Max(1e-4f, p.Depth);

            // 半径が半分を超えると直線部が負になるため R 側を抑える。
            float r = depth * 0.5f;
            r = Mathf.Min(r, length * 0.5f);
            if (p.RoundTopBottom) r = Mathf.Min(r, height * 0.5f);

            float a = Mathf.Max(0f, length * 0.5f - r);
            float b = p.RoundTopBottom ? Mathf.Max(0f, height * 0.5f - r) : height * 0.5f;

            int capSeg = Mathf.Clamp(p.CapSegments,    CapSegmentsMin, CapSegmentsMax);
            int lenSeg = Mathf.Clamp(p.LengthSegments, 1, LineSegmentsMax);
            int hSeg   = Mathf.Clamp(p.HeightSegments, 1, LineSegmentsMax);

            string name = string.IsNullOrEmpty(p.MeshName) ? "StadiumBox" : p.MeshName;

            // 半小判型は割る面の上に頂点の列が乗るよう、割られる側の分割数を倍にして全体を作る。
            bool cutX = p.Half && p.HalfAxis == StadiumHalfAxis.Length;
            bool cutZ = p.Half && p.HalfAxis == StadiumHalfAxis.Depth;
            if (cutX) lenSeg *= 2;
            if (cutZ) capSeg *= 2;

            var mo = p.RoundTopBottom
                ? BuildRounded (name, a, b, r, capSeg, lenSeg, hSeg)
                : BuildFlatCaps(name, a, b, r, capSeg, lenSeg, hSeg, p.CapTop, p.CapBottom);

            if (cutX || cutZ)
                CutHalf(mo, cutX, p.RoundTopBottom, a, b, r, capSeg, lenSeg, hSeg);

            AssignBoxProjectionUV(mo);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(mo);
            return mo;
        }

        // ================================================================
        // 上下が平フタ（画像の形）
        // ================================================================

        private static MeshObject BuildFlatCaps(
            string name, float a, float b, float r, int capSeg, int lenSeg, int hSeg,
            bool capTop, bool capBottom)
        {
            var mo = new MeshObject(name);

            var ol = BuildStadiumOutline(a, r, lenSeg, capSeg,
                out int rightArcStart, out int bottomStart, out int leftArcStart);
            int n = ol.Count;
            if (n < 3) return mo;

            // ── 側面 ──
            int wall = mo.VertexCount;
            for (int iy = 0; iy <= hSeg; iy++)
            {
                float y = -b + 2f * b * iy / hSeg;
                float v = (float)iy / hSeg;
                for (int i = 0; i < n; i++)
                {
                    Vector2 pp = ol[i].P, nn = ol[i].N;
                    mo.Vertices.Add(new Vertex(
                        new Vector3(pp.x, y, pp.y),
                        new Vector2((float)i / n, v),
                        new Vector3(nn.x, 0f, nn.y)));
                }
            }
            for (int iy = 0; iy < hSeg; iy++)
            {
                int lower = wall + iy * n;
                int upper = wall + (iy + 1) * n;
                for (int i = 0; i < n; i++)
                {
                    int i2 = (i + 1) % n;
                    mo.AddQuad(lower + i, lower + i2, upper + i2, upper + i);
                }
            }

            // ── 上下のフタ ──
            // CapTop / CapBottom で個別に省略できる。両方外すと側面だけの筒になる。
            // 半径方向の分割数。円周 180° を capSeg 分割するので、半径は概ねその半分にすると
            // フタの面の縦横比がそろう。長方形の Z 分割数はこの 2 倍になる。
            int radSeg = Mathf.Max(1, (capSeg + 1) / 2);

            if (capTop)
                AddStadiumCap(mo, ol, rightArcStart, bottomStart, leftArcStart, a, r,  b, lenSeg, radSeg, true);
            if (capBottom)
                AddStadiumCap(mo, ol, rightArcStart, bottomStart, leftArcStart, a, r, -b, lenSeg, radSeg, false);

            return mo;
        }

        /// <summary>
        /// 小判型のフタを「半円 + 長方形 + 半円」の 3 つに分けて張る。
        ///
        /// 長方形は x = ±a、z = ±r の矩形。Z 方向を 2·radSeg 分割にしてあるので、
        /// 左右の辺の点は z = ±r·k/radSeg（k = 0 … radSeg）となり、
        /// 半円側の直線境界（中心から弧の端へ向かう半径 2 本）と位置が一致する。
        /// 輪郭点は側面と同じ位置なので、重複頂点のマージで側面ともつながる。
        ///
        /// a = 0（直線部なし）のときは長方形の面積が 0 になるので張らない。
        /// 半円 2 枚が背中合わせに並んで円になる。
        /// </summary>
        private static void AddStadiumCap(
            MeshObject mo, List<Sample2> ol,
            int rightArcStart, int bottomStart, int leftArcStart,
            float a, float r, float y, int lenSeg, int radSeg, bool up)
        {
            int n = ol.Count;
            Vector3 nrm = up ? Vector3.up : Vector3.down;

            // ── 長方形 ──
            if (a > Eps)
            {
                if (up)
                    AddGrid(mo,
                        new Vector3(-a, y,  r), new Vector3( a, y,  r),
                        new Vector3( a, y, -r), new Vector3(-a, y, -r),
                        nrm, lenSeg, 2 * radSeg);
                else
                    AddGrid(mo,
                        new Vector3(-a, y, -r), new Vector3( a, y, -r),
                        new Vector3( a, y,  r), new Vector3(-a, y,  r),
                        nrm, lenSeg, 2 * radSeg);
            }

            // ── 右の半円（輪郭 rightArcStart … bottomStart）──
            AddHalfDisc(mo, ol, rightArcStart, bottomStart - rightArcStart + 1,
                new Vector3(a, y, 0f), y, nrm, radSeg, up);

            // ── 左の半円（輪郭 leftArcStart … 末尾 → 先頭へ折り返し）──
            AddHalfDisc(mo, ol, leftArcStart, n - leftArcStart + 1,
                new Vector3(-a, y, 0f), y, nrm, radSeg, up);
        }

        /// <summary>
        /// フタの半円 1 枚。輪郭の start から count 個（添字は輪郭長で巻き戻す）を外周の弧として、
        /// center を極とする極座標格子で塞ぐ。半径方向は radSeg 分割。
        /// 最外周は輪郭の点をそのまま使うので、側面の壁と位置が一致する。
        /// </summary>
        private static void AddHalfDisc(
            MeshObject mo, List<Sample2> ol, int start, int count,
            Vector3 center, float y, Vector3 nrm, int radSeg, bool up)
        {
            int n = ol.Count;
            if (n < 3 || count < 2 || radSeg < 1) return;

            int baseIdx = mo.VertexCount;
            mo.Vertices.Add(new Vertex(center, new Vector2(0.5f, 0.5f), nrm));

            for (int k = 1; k <= radSeg; k++)
            {
                float t = (float)k / radSeg;
                for (int i = 0; i < count; i++)
                {
                    Vector2 pp = ol[(start + i) % n].P;
                    var outer = new Vector3(pp.x, y, pp.y);
                    // 最外周は丸め誤差を入れずに輪郭の点そのものを使う。
                    Vector3 pos = (k == radSeg) ? outer : center + (outer - center) * t;
                    mo.Vertices.Add(new Vertex(pos,
                        new Vector2((float)i / (count - 1), t), nrm));
                }
            }

            // 極の帯
            int ring1 = baseIdx + 1;
            for (int i = 0; i < count - 1; i++)
            {
                if (up) mo.AddTriangle(baseIdx, ring1 + i,     ring1 + i + 1);
                else    mo.AddTriangle(baseIdx, ring1 + i + 1, ring1 + i);
            }

            // 残りの帯
            for (int k = 1; k < radSeg; k++)
            {
                int inner = baseIdx + 1 + (k - 1) * count;
                int outerRow = inner + count;
                for (int i = 0; i < count - 1; i++)
                {
                    if (up) mo.AddQuad(inner + i, outerRow + i,     outerRow + i + 1, inner + i + 1);
                    else    mo.AddQuad(inner + i, inner + i + 1,    outerRow + i + 1, outerRow + i);
                }
            }
        }

        // ================================================================
        // 上下も半円筒（四隅は 1/4 球）
        // ================================================================

        private static MeshObject BuildRounded(
            string name, float a, float b, float r, int capSeg, int lenSeg, int hSeg)
        {
            var mo = new MeshObject(name);

            // ── 前後の平面 ──
            // a または b が 0 のときは面積 0 になるので張らない。
            if (a > Eps && b > Eps)
            {
                AddGrid(mo,
                    new Vector3(-a, -b, r), new Vector3( a, -b, r),
                    new Vector3( a,  b, r), new Vector3(-a,  b, r),
                    Vector3.forward, lenSeg, hSeg);

                AddGrid(mo,
                    new Vector3( a, -b, -r), new Vector3(-a, -b, -r),
                    new Vector3(-a,  b, -r), new Vector3( a,  b, -r),
                    Vector3.back, lenSeg, hSeg);
            }

            // ── 左右の半円筒（軸 Y）──
            if (b > Eps)
            {
                AddHalfCylinder(mo, new Vector3( a, -b, 0f), Vector3.up,   2f * b,
                    Vector3.forward, Vector3.right, r, capSeg, hSeg);
                AddHalfCylinder(mo, new Vector3(-a,  b, 0f), Vector3.down, 2f * b,
                    Vector3.forward, Vector3.left,  r, capSeg, hSeg);
            }

            // ── 上下の半円筒（軸 X）──
            if (a > Eps)
            {
                AddHalfCylinder(mo, new Vector3( a,  b, 0f), Vector3.left,  2f * a,
                    Vector3.forward, Vector3.up,   r, capSeg, lenSeg);
                AddHalfCylinder(mo, new Vector3(-a, -b, 0f), Vector3.right, 2f * a,
                    Vector3.forward, Vector3.down, r, capSeg, lenSeg);
            }

            // ── 四隅の 1/4 球 ──
            // 方位角の分割は極角の半分（90° と 180°）にして、面の縦横比をそろえる。
            int azSeg = Mathf.Max(1, Mathf.RoundToInt(capSeg * 0.5f));
            AddQuarterSphere(mo, new Vector3( a,  b, 0f), Vector3.right, Vector3.up,   r, capSeg, azSeg);
            AddQuarterSphere(mo, new Vector3(-a,  b, 0f), Vector3.left,  Vector3.up,   r, capSeg, azSeg);
            AddQuarterSphere(mo, new Vector3(-a, -b, 0f), Vector3.left,  Vector3.down, r, capSeg, azSeg);
            AddQuarterSphere(mo, new Vector3( a, -b, 0f), Vector3.right, Vector3.down, r, capSeg, azSeg);

            return mo;
        }

        // ================================================================
        // 半小判型（半分に割って切り口を閉じる）
        // ================================================================

        /// <summary>
        /// 原点を通る平面（cutX なら x = 0、そうでなければ z = 0）で割り、
        /// 負の側の面と使われなくなった頂点を消して、切り口を平面で閉じる。
        /// capSeg / lenSeg は全体を作ったときの（倍にした後の）分割数。
        /// どの面も割る面をまたがない（割る面の上に頂点の列がある）ので、
        /// 面の重心の符号だけで残す・消すを決められる。
        /// </summary>
        private static void CutHalf(
            MeshObject mo, bool cutX, bool rounded,
            float a, float b, float r, int capSeg, int lenSeg, int hSeg)
        {
            // ── 負の側の面を消す ──
            var killFaces = new List<int>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var f = mo.Faces[fi];
                if (f == null || f.VertexIndices == null || f.VertexIndices.Count == 0) continue;
                Vector3 c = Vector3.zero;
                foreach (int vi in f.VertexIndices) c += mo.Vertices[vi].Position;
                c /= f.VertexIndices.Count;
                if ((cutX ? c.x : c.z) < 0f) killFaces.Add(fi);
            }
            mo.RemoveFaces(killFaces);

            // ── 使われなくなった頂点を消す ──
            var used = new bool[mo.VertexCount];
            foreach (var f in mo.Faces)
                if (f?.VertexIndices != null)
                    foreach (int vi in f.VertexIndices) used[vi] = true;
            var killVerts = new List<int>();
            for (int i = 0; i < used.Length; i++)
                if (!used[i]) killVerts.Add(i);
            mo.RemoveVertices(killVerts);

            // ── 切り口 ──
            // 半径方向の分割数は平フタと同じ決め方（切り口の境界がフタ・丸みの頂点と一致する）。
            int radSeg = Mathf.Max(1, (capSeg + 1) / 2);

            if (cutX)
            {
                Vector3 nrm = Vector3.left;
                if (!rounded)
                {
                    // 長方形 1 枚（Z は平フタの長方形と同じ 2·radSeg、Y は高さの分割数）
                    AddGridFacing(mo,
                        new Vector3(0f, -b, -r), new Vector3(0f, -b,  r),
                        new Vector3(0f,  b,  r), new Vector3(0f,  b, -r),
                        nrm, 2 * radSeg, hSeg);
                }
                else
                {
                    // 縦向きの小判型（直線部 2b、上下が半径 r の半円）
                    if (b > Eps)
                        AddGridFacing(mo,
                            new Vector3(0f, -b, -r), new Vector3(0f, -b,  r),
                            new Vector3(0f,  b,  r), new Vector3(0f,  b, -r),
                            nrm, 2 * radSeg, hSeg);
                    AddFanFacing(mo, new Vector3(0f,  b, 0f),
                        ArcPoints(new Vector3(0f,  b, 0f), Vector3.forward, Vector3.up,   r, Mathf.PI, capSeg),
                        radSeg, nrm);
                    AddFanFacing(mo, new Vector3(0f, -b, 0f),
                        ArcPoints(new Vector3(0f, -b, 0f), Vector3.forward, Vector3.down, r, Mathf.PI, capSeg),
                        radSeg, nrm);
                }
            }
            else
            {
                Vector3 nrm = Vector3.back;
                float xo = a + r;

                // 中央の長方形
                if (a > Eps && b > Eps)
                    AddGridFacing(mo,
                        new Vector3(-a, -b, 0f), new Vector3( a, -b, 0f),
                        new Vector3( a,  b, 0f), new Vector3(-a,  b, 0f),
                        nrm, lenSeg, hSeg);

                // 左右の帯（丸みの部分。X は radSeg 分割）
                if (b > Eps)
                {
                    AddGridFacing(mo,
                        new Vector3( a, -b, 0f), new Vector3( xo, -b, 0f),
                        new Vector3( xo, b, 0f), new Vector3( a,   b, 0f),
                        nrm, radSeg, hSeg);
                    AddGridFacing(mo,
                        new Vector3(-xo, -b, 0f), new Vector3(-a, -b, 0f),
                        new Vector3(-a,   b, 0f), new Vector3(-xo, b, 0f),
                        nrm, radSeg, hSeg);
                }

                if (rounded)
                {
                    // 上下の帯（Y は radSeg 分割）
                    if (a > Eps)
                    {
                        AddGridFacing(mo,
                            new Vector3(-a, b, 0f),     new Vector3( a, b, 0f),
                            new Vector3( a, b + r, 0f), new Vector3(-a, b + r, 0f),
                            nrm, lenSeg, radSeg);
                        AddGridFacing(mo,
                            new Vector3(-a, -b - r, 0f), new Vector3( a, -b - r, 0f),
                            new Vector3( a, -b, 0f),     new Vector3(-a, -b, 0f),
                            nrm, lenSeg, radSeg);
                    }

                    // 四隅の 1/4 円（弧は 1/4 球の赤道と同じ分割）
                    int azSeg = Mathf.Max(1, Mathf.RoundToInt(capSeg * 0.5f));
                    float hp = Mathf.PI * 0.5f;
                    AddFanFacing(mo, new Vector3( a,  b, 0f),
                        ArcPoints(new Vector3( a,  b, 0f), Vector3.right, Vector3.up,   r, hp, azSeg), radSeg, nrm);
                    AddFanFacing(mo, new Vector3(-a,  b, 0f),
                        ArcPoints(new Vector3(-a,  b, 0f), Vector3.left,  Vector3.up,   r, hp, azSeg), radSeg, nrm);
                    AddFanFacing(mo, new Vector3(-a, -b, 0f),
                        ArcPoints(new Vector3(-a, -b, 0f), Vector3.left,  Vector3.down, r, hp, azSeg), radSeg, nrm);
                    AddFanFacing(mo, new Vector3( a, -b, 0f),
                        ArcPoints(new Vector3( a, -b, 0f), Vector3.right, Vector3.down, r, hp, azSeg), radSeg, nrm);
                }
            }
        }

        /// <summary>
        /// 円弧の点列。center + radius·(e0·cosθ + e1·sinθ)、θ = 0 … angle を seg 分割（seg + 1 点）。
        /// </summary>
        private static List<Vector3> ArcPoints(
            Vector3 center, Vector3 e0, Vector3 e1, float radius, float angle, int seg)
        {
            var list = new List<Vector3>(seg + 1);
            for (int i = 0; i <= seg; i++)
            {
                float t = angle * i / seg;
                list.Add(center + (e0 * Mathf.Cos(t) + e1 * Mathf.Sin(t)) * radius);
            }
            return list;
        }

        /// <summary>
        /// 平面の格子を normal の向きに張る。v0 → v1 が U（divU 分割）、v0 → v3 が V（divV 分割）。
        /// 並びが normal と逆向きなら U と V を入れ替えて張る。
        /// </summary>
        private static void AddGridFacing(
            MeshObject mo, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector3 normal, int divU, int divV)
        {
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v1), normal) < 0f)
                AddGrid(mo, v0, v3, v2, v1, normal, divV, divU);
            else
                AddGrid(mo, v0, v1, v2, v3, normal, divU, divV);
        }

        /// <summary>
        /// 扇形（円弧 arc と中心 center で囲まれた領域）を、center を極とする極座標格子で
        /// normal の向きに張る。半径方向は radSeg 分割。最外周は arc の点そのものを使う。
        /// </summary>
        private static void AddFanFacing(
            MeshObject mo, Vector3 center, List<Vector3> arc, int radSeg, Vector3 normal)
        {
            int count = arc.Count;
            if (count < 2 || radSeg < 1) return;

            bool flip = Vector3.Dot(Vector3.Cross(arc[0] - center, arc[1] - arc[0]), normal) < 0f;

            int baseIdx = mo.VertexCount;
            mo.Vertices.Add(new Vertex(center, new Vector2(0.5f, 0.5f), normal));
            for (int k = 1; k <= radSeg; k++)
            {
                float t = (float)k / radSeg;
                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = (k == radSeg) ? arc[i] : center + (arc[i] - center) * t;
                    mo.Vertices.Add(new Vertex(pos, new Vector2((float)i / (count - 1), t), normal));
                }
            }

            int ring1 = baseIdx + 1;
            for (int i = 0; i < count - 1; i++)
            {
                if (!flip) mo.AddTriangle(baseIdx, ring1 + i,     ring1 + i + 1);
                else       mo.AddTriangle(baseIdx, ring1 + i + 1, ring1 + i);
            }
            for (int k = 1; k < radSeg; k++)
            {
                int inner = baseIdx + 1 + (k - 1) * count;
                int outer = inner + count;
                for (int i = 0; i < count - 1; i++)
                {
                    if (!flip) mo.AddQuad(inner + i, outer + i,     outer + i + 1, inner + i + 1);
                    else       mo.AddQuad(inner + i, inner + i + 1, outer + i + 1, outer + i);
                }
            }
        }

        // ================================================================
        // 小判型の輪郭（XZ 平面）
        // ================================================================

        /// <summary>輪郭 1 点。P / N とも (x, z) の 2 成分。</summary>
        private struct Sample2
        {
            public Vector2 P;
            public Vector2 N;
        }

        /// <summary>
        /// 小判型の閉じた輪郭を作る。外向き法線が付く向き
        /// （+Y から見下ろすと反時計回り）に並べる。
        /// 直線と円弧の継ぎ目は位置も法線も一致するので、重なる点は落とす。
        /// a = 0 のときは直線部が潰れて円になる。
        ///
        /// rightArcStart / bottomStart / leftArcStart は、それぞれ
        /// (a, +r) / (a, -r) / (-a, -r) にあたる点の添字。フタを
        /// 半円 + 長方形 + 半円 に切り分けるために返す。
        /// 重なりを落とした結果その点が直前の点と同一になった場合（a = 0）は、
        /// 落とされずに残っている側の添字を返す。
        /// </summary>
        private static List<Sample2> BuildStadiumOutline(
            float a, float r, int lenSeg, int capSeg,
            out int rightArcStart, out int bottomStart, out int leftArcStart)
        {
            var list = new List<Sample2>(2 * lenSeg + 2 * capSeg);

            // 直前の点と重なるときは足さずに、その直前の点の添字を返す。
            int Append(Vector2 pos, Vector2 nrm)
            {
                if (list.Count > 0 && (list[list.Count - 1].P - pos).sqrMagnitude < 1e-12f)
                    return list.Count - 1;
                list.Add(new Sample2 { P = pos, N = nrm });
                return list.Count - 1;
            }

            // 上の直線 z = +r : x = -a → +a
            for (int j = 0; j < lenSeg; j++)
                Append(new Vector2(-a + 2f * a * j / lenSeg, r), new Vector2(0f, 1f));

            // 右の半円 中心 (a, 0) : +z → +x → -z
            rightArcStart = 0;
            for (int i = 0; i < capSeg; i++)
            {
                float t = Mathf.PI * i / capSeg;
                var nn = new Vector2(Mathf.Sin(t), Mathf.Cos(t));
                int idx = Append(new Vector2(a + r * nn.x, r * nn.y), nn);
                if (i == 0) rightArcStart = idx;
            }

            // 下の直線 z = -r : x = +a → -a
            bottomStart = 0;
            for (int j = 0; j < lenSeg; j++)
            {
                int idx = Append(new Vector2(a - 2f * a * j / lenSeg, -r), new Vector2(0f, -1f));
                if (j == 0) bottomStart = idx;
            }

            // 左の半円 中心 (-a, 0) : -z → -x → +z
            leftArcStart = 0;
            for (int i = 0; i < capSeg; i++)
            {
                float t = Mathf.PI * i / capSeg;
                var nn = new Vector2(-Mathf.Sin(t), -Mathf.Cos(t));
                int idx = Append(new Vector2(-a + r * nn.x, r * nn.y), nn);
                if (i == 0) leftArcStart = idx;
            }

            // 閉じるときの重なりを落とす
            while (list.Count > 1 &&
                   (list[list.Count - 1].P - list[0].P).sqrMagnitude < 1e-12f)
                list.RemoveAt(list.Count - 1);

            return list;
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

        /// <summary>
        /// 180° の半円筒。法線は nStart から nPerp 側へ 180° 回る
        /// （n(θ) = nStart·cosθ + nPerp·sinθ）。
        /// 外向きに張るには axis == cross(nStart, nPerp) であること。
        /// </summary>
        private static void AddHalfCylinder(
            MeshObject mo, Vector3 origin, Vector3 axis, float axisLength,
            Vector3 nStart, Vector3 nPerp, float radius, int capSeg, int axisSeg)
        {
            int start = mo.VertexCount;
            for (int ia = 0; ia <= axisSeg; ia++)
            {
                float s = (float)ia / axisSeg;
                Vector3 c = origin + axis * (axisLength * s);
                for (int it = 0; it <= capSeg; it++)
                {
                    float t = Mathf.PI * it / capSeg;
                    Vector3 nn = nStart * Mathf.Cos(t) + nPerp * Mathf.Sin(t);
                    mo.Vertices.Add(new Vertex(c + nn * radius,
                        new Vector2((float)it / capSeg, s), nn));
                }
            }
            int cols = capSeg + 1;
            for (int ia = 0; ia < axisSeg; ia++)
                for (int it = 0; it < capSeg; it++)
                {
                    int i0 = start + ia * cols + it;
                    mo.AddQuad(i0, i0 + 1, i0 + cols + 1, i0 + cols);
                }
        }

        /// <summary>
        /// 1/4 球。方位角は eX から eY までの 90°、極角は +Z から -Z までの 180°。
        /// ±Z 端は 1 点に潰れるので、その帯だけ三角形で張る。
        /// 極角の分割数は半円筒と同じにしてあり、境界の頂点位置が一致する。
        /// </summary>
        private static void AddQuarterSphere(
            MeshObject mo, Vector3 center, Vector3 eX, Vector3 eY,
            float radius, int polarSeg, int azSeg)
        {
            Vector3 eZ = Vector3.forward;

            // (eX, eY, eZ) が左手系になる並びのときは方位角を逆回しにして巻き順をそろえる。
            bool rev = Vector3.Dot(Vector3.Cross(eX, eY), eZ) < 0f;

            int poleF = mo.VertexCount;
            mo.Vertices.Add(new Vertex(center + eZ * radius, new Vector2(0.5f, 1f), eZ));

            int rowStart = mo.VertexCount;
            int cols = azSeg + 1;
            int rows = polarSeg - 1;                    // 極を除いた緯度の本数
            for (int j = 1; j < polarSeg; j++)
            {
                float ph = Mathf.PI * j / polarSeg;
                float sp = Mathf.Sin(ph), cp = Mathf.Cos(ph);
                for (int i = 0; i <= azSeg; i++)
                {
                    float t = (float)i / azSeg;
                    float ps = Mathf.PI * 0.5f * (rev ? 1f - t : t);
                    Vector3 nn = sp * (Mathf.Cos(ps) * eX + Mathf.Sin(ps) * eY) + cp * eZ;
                    mo.Vertices.Add(new Vertex(center + nn * radius,
                        new Vector2(t, 1f - (float)j / polarSeg), nn));
                }
            }

            int poleB = mo.VertexCount;
            mo.Vertices.Add(new Vertex(center - eZ * radius, new Vector2(0.5f, 0f), -eZ));

            // +Z 極の帯
            for (int i = 0; i < azSeg; i++)
                mo.AddTriangle(poleF, rowStart + i, rowStart + i + 1);

            // 中間の帯
            for (int j = 0; j < rows - 1; j++)
            {
                int cur = rowStart + j * cols;
                int nxt = rowStart + (j + 1) * cols;
                for (int i = 0; i < azSeg; i++)
                    mo.AddQuad(cur + i, nxt + i, nxt + i + 1, cur + i + 1);
            }

            // -Z 極の帯
            int last = rowStart + (rows - 1) * cols;
            for (int i = 0; i < azSeg; i++)
                mo.AddTriangle(last + i, poleB, last + i + 1);
        }

        // ================================================================
        // UV
        // ================================================================

        /// <summary>
        /// 各面 [0,1] のボックス投影。頂点法線の支配軸で 6 面のどれかへ割り当て、
        /// メッシュ AABB で正規化する（角丸直方体と同じ考え方）。
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
