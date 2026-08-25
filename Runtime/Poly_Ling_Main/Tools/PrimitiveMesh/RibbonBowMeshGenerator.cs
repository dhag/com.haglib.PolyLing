// RibbonBowMeshGenerator.cs
// 蝶結びリボンを「梯子（四角形の帯）の集まり」として生成する。Runtime / Editor 共有。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【出力】1つの MeshObject に最大 5 本の梯子を入れる。頂点は梯子どうしで共有しない。
//   LoopP / LoopN … 左右のループ（P = +X 側、N = -X 側）
//   TailP / TailN … 左右のテール
//   Knot          … 中央のノット（短い帯の梯子1本）
//   厚み・断面・波形は付けない。Frill / Pipe へ基準ベルトとして渡すことを前提とする。
//   BuildLoops / BuildTails / BuildKnot が false の部品は積まない。
//   多重リボンは「ループ抜きで1回、ループ付きで1回」と分けて生成し、別ツールで結合する。
//
// 【座標】モデル正面は +Z、左右対称面は YZ 平面（AuthoringFrame 参照）。
//   面は (L[i], L[i+1], R[i+1], R[i]) の順で張る。
//
// 【ループの位相】RibbonLoopTopology で2種類。どちらもロール（ねじり）は持たない。
//   Flip … 実物のリボン。往路（z=+R）→ XZ 平面内の半円で折り返し → 復路（z=-R）
//          の平たい筒にする。R = LoopDepth が往路と復路の間隔の半分。
//          幅方向は鉛直ガイド（RibbonFrameMode.VerticalGuide）で決めるため、
//          面法線は 表(+Z) → 折り返しで外向き → 裏(-Z) と入れ替わる。
//          折り返しは幅方向の軸まわりの曲げなので、帯が潰れない。
//          折り返し区間で Y を動かさないこと。動かすと回転軸が傾き、帯がねじれる。
//   Flat … ループの全長で表が正面を向く（靴紐など）。往路・復路とも +Depth 側へ膨らむ。
//          折り返しが平面内の曲げになるため、折り返し半径が帯の半幅を下回らないよう
//          LoopHeight に下限を掛ける。
//
// 【ループの回転】LoopTilt はループ全体を根元の中点まわりに +Z 軸まわりで回す。
//   Sag は折り返し点を下げるだけなので、折り返しをノットより上へ置くにはこちらを使う。
//   Flip 型の折り返しは Y 一定の XZ 平面内の半円で、その回転軸は ±Y、
//   鉛直ガイドも +Y なので両者が一致している。ループを回すと折り返しの軸だけが
//   傾いてガイドとずれ、帯がねじれる。そのためループの梯子には
//   Rz(sx*Tilt) * (0,1,0) を幅方向ガイドとして渡し、軸とガイドの一致を保つ。
//   Flat 型は FixedNormal（基準法線 +Z）で、Z まわりの回転では基準法線が変わらない。
//
// 【梯子の向き】
//   テールは左右とも 根元 → 先端 で揃える。以前は「1本の帯が中央を通って左右へ抜ける」
//   道筋に合わせて TailN だけ 先端 → 根元 にしていたが、幅関数 TailWidth(s) は
//   s=0 を根元とする前提なので、TailN でだけ先端の幅倍率が逆向きに効いていた。
//   向きを揃えることで、幅・UV の u・Frill / Pipe が読むベルトの向きが左右対称になる。
//   開始タグ・先端三角はテールの根元側へ付く。左右の根元は X が ±RootOffset ぶん
//   離れており（既定 0.09 * RibbonWidth）、図形生成パネルの重複頂点の結合の
//   許容 0.001 より十分大きいので溶接されない。
//
//   ループは LoopN だけ逆走のまま。LoopWidth(s) は Min(s, 1-s) を使う左右対称の関数で
//   向きに依存せず、開始点も上側根元／下側根元と別の点になるため問題がない。
//
// 【梯子タグ】BeltStackDetector の規約に合わせる。
//   開始三角 = (Pstart, L[0], R[0])          … P を含まない辺 (L0,R0) が最初の rung
//   開始タグ = (Pstart, A, B)                … 3辺とも非共有・共有頂点は Pstart の1個だけ
//   終了三角 = (Pend,   R[n-1], L[n-1])      … 縦走査はここで終わり、Pend が終了点になる
//   Pstart = C(0) - T(0) * TipLength、Pend = C(1) + T(1) * TipLength。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Ribbon
{
    public static class RibbonBowMeshGenerator
    {
        /// <summary>部品の根元を左右対称面から離す量（RibbonWidth 比）。</summary>
        private const float RootOffsetXScale = 0.15f;

        /// <summary>テールの根元をループの根元より下へ置く量（RibbonWidth 比）。</summary>
        private const float TailRootDropScale = 0.10f;

        /// <summary>根元の幅を絞る区間（媒介変数 s の割合）。</summary>
        private const float PinchSpan = 0.25f;

        /// <summary>ループの中間制御点を置く X 位置（外側点までの距離に対する比）。</summary>
        private const float LoopArmScale = 0.65f;

        /// <summary>テールの最大開き点の前後に置く制御脚の長さ（その区間の落差に対する比）。</summary>
        private const float ApexHandleScale = 0.40f;

        /// <summary>Flip 型の折り返し半径の下限（RibbonWidth 比）。</summary>
        private const float MinFoldRadiusScale = 0.15f;

        /// <summary>半円を3次ベジエで近似するときの制御脚長（半径比）。</summary>
        private const float CircleKappa = 4f / 3f;

        /// <summary>
        /// 先端三角・タグ三角の最小サイズ。
        /// 図形生成パネルは生成時に許容 0.001 で重複頂点を結合する
        /// （PlayerPrimitiveMeshSubPanel.Generate）。これを下回るとタグが潰れて
        /// 自動検索の起点にならないため、下限を設ける。
        /// </summary>
        private const float MinTagSize = 0.005f;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>蝶結びリボンの梯子群を1つの MeshObject として生成する。</summary>
        public static MeshObject Generate(RibbonBowParams p)
        {
            p = Prepare(p);

            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "Ribbon" : p.MeshName);

            float tipLen = Mathf.Max(MinTagSize, p.RibbonWidth * p.TipLengthScale);
            float tagLen = Mathf.Max(MinTagSize, p.RibbonWidth * p.TagSizeScale);

            var ladders = BuildLadders(p);
            for (int i = 0; i < ladders.Count; i++)
                AppendLadder(mo, ladders[i], p, tipLen, tagLen);

            mo.RecalculateNormals();

            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, p.Pivot);

            return mo;
        }

        /// <summary>
        /// 梯子だけを作る（メッシュ化しない）。段数の表示や検証に使う。
        /// 入力は補正済みでなくてよい（内部で Normalized する）。
        /// </summary>
        public static List<RibbonLadder> BuildLadders(RibbonBowParams p)
        {
            p = Prepare(p);

            float w     = p.RibbonWidth;
            float pinch = p.Loop.RootPinch;
            float taper = p.Tail.Taper;

            float LoopWidth(float s)
            {
                float e = Mathf.Clamp01(Mathf.Min(s, 1f - s) / PinchSpan);
                return w * Mathf.Lerp(pinch, 1f, e);
            }

            float TailWidth(float s) => w * Mathf.Lerp(1f, taper, s);
            float KnotWidth(float s) => p.Knot.Width;

            // 裏返るループだけ鉛直ガイドで幅方向を決める。折り返しで面が入れ替わる。
            RibbonFrameMode loopFrame = p.Loop.Topology == RibbonLoopTopology.Flip
                ? RibbonFrameMode.VerticalGuide
                : RibbonFrameMode.FixedNormal;

            var list = new List<RibbonLadder>(5);

            // テールは左右とも根元 → 先端。TailWidth(s) が s=0 を根元とする前提なので、
            // 片側だけ逆走させると先端の幅倍率が逆向きに効く。
            if (p.BuildTails)
                list.Add(RibbonLadderBuilder.Build(
                    TailCurve(p, -1f), p.TailSegments, TailWidth, "TailN"));

            if (p.BuildLoops)
            {
                list.Add(RibbonLadderBuilder.Build(
                    Reverse(LoopCurve(p, -1f)), p.LoopSegments, LoopWidth,
                    loopFrame, LoopWidthGuide(p, -1f), "LoopN"));

                list.Add(RibbonLadderBuilder.Build(
                    LoopCurve(p, +1f), p.LoopSegments, LoopWidth,
                    loopFrame, LoopWidthGuide(p, +1f), "LoopP"));
            }

            if (p.BuildTails)
                list.Add(RibbonLadderBuilder.Build(
                    TailCurve(p, +1f), p.TailSegments, TailWidth, "TailP"));

            if (p.BuildKnot)
                list.Add(RibbonLadderBuilder.Build(
                    KnotCurve(p), p.KnotSegments, KnotWidth, "Knot"));

            return list;
        }

        /// <summary>
        /// 生成に使う前の補正。RibbonBowParams.Normalized に加えて、
        /// 曲線の作り方に依存する下限をループへ適用する。
        /// Flat は LoopHeight、Flip は LoopDepth（折り返し半径）。
        /// 何度呼んでも結果は変わらない。
        /// </summary>
        private static RibbonBowParams Prepare(RibbonBowParams p)
        {
            p = p.Normalized();

            if (p.Loop.Topology == RibbonLoopTopology.Flat)
            {
                // 平面内の折り返しになるため、折り返し半径が帯の半幅を下回らないようにする。
                float minH = MinLoopHeight(p.RibbonWidth, p.Loop.Width);
                if (p.Loop.Height < minH) p.Loop.Height = minH;
            }
            else
            {
                // 往路と復路が同一面に来ると折り返しが平面内の曲げに戻ってしまう。
                float minR = p.RibbonWidth * MinFoldRadiusScale;
                if (p.Loop.Depth < minR) p.Loop.Depth = minR;
            }

            return p;
        }

        // ================================================================
        // 中心曲線
        // ================================================================

        /// <summary>
        /// 位相に応じたループの中心曲線。組み上げたあと LoopTilt ぶんだけ
        /// 根元の中点を軸中心に +Z 軸まわりへ回す（左右とも正で外側が上がる）。
        /// </summary>
        private static List<RibbonBezier> LoopCurve(in RibbonBowParams p, float sx)
        {
            var segs = p.Loop.Topology == RibbonLoopTopology.Flip
                ? LoopCurveFlip(p, sx)
                : LoopCurveFlat(p, sx);

            float deg = sx * p.Loop.Tilt;
            if (Mathf.Abs(deg) < 1e-4f) return segs;

            // 上側根元と下側根元の中点。ここを固定してループ本体だけを振る。
            var pivot = new Vector3(sx * p.RibbonWidth * RootOffsetXScale, 0f, 0f);

            for (int i = 0; i < segs.Count; i++)
            {
                var b = segs[i];
                segs[i] = new RibbonBezier(
                    RotateZ(b.P0, pivot, deg), RotateZ(b.P1, pivot, deg),
                    RotateZ(b.P2, pivot, deg), RotateZ(b.P3, pivot, deg));
            }

            return segs;
        }

        /// <summary>
        /// ループの梯子に渡す幅方向ガイド。回転していない既定は鉛直 +Y。
        /// Flip 型の折り返しの回転軸と一致させ続けるため、ループと同じ角だけ回す。
        /// FixedNormal（Flat 型）では使われない。
        /// </summary>
        private static Vector3 LoopWidthGuide(in RibbonBowParams p, float sx)
        {
            float r = sx * p.Loop.Tilt * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f);
        }

        /// <summary>pivot を通る +Z 軸まわりに deg 度回す。Z は変えない。</summary>
        private static Vector3 RotateZ(Vector3 v, Vector3 pivot, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            float x = v.x - pivot.x, y = v.y - pivot.y;
            return new Vector3(pivot.x + x * c - y * s, pivot.y + x * s + y * c, v.z);
        }

        /// <summary>
        /// 裏返るループの中心曲線。往路の脚 → 折り返し → 復路の脚 の3セグメント。
        /// sx = +1 で +X 側、-1 で -X 側（X 座標だけ符号を反転する）。
        ///
        /// 折り返しは XZ 平面内の半円で、Y は一定に保つ。
        /// 半円の半径 R = LoopDepth が往路（z=+R）と復路（z=-R）の間隔の半分になる。
        /// この区間で Y を動かすと折り返しの回転軸が傾き、帯がねじれる。
        /// </summary>
        private static List<RibbonBezier> LoopCurveFlip(in RibbonBowParams p, float sx)
        {
            float w     = p.RibbonWidth;
            float rootX = w * RootOffsetXScale;
            float gap   = p.Loop.RootGap;
            float lw    = p.Loop.Width;
            float lh    = p.Loop.Height;
            float sag   = p.Loop.Sag;
            float r     = p.Loop.Depth;   // 折り返し半径（Prepare で下限済み）

            float armX = sx * (rootX + lw * LoopArmScale);
            float apexY = -sag;

            // 半円の中心。外周が sx*(rootX + lw) に来るよう R だけ内側へ置く。
            float foldX = sx * (rootX + lw) - sx * r;

            Vector3 upper = new Vector3(sx * rootX, +gap * 0.5f, 0f);
            Vector3 lower = new Vector3(sx * rootX, -gap * 0.5f, 0f);

            Vector3 e1 = new Vector3(foldX, apexY, +r);   // 折り返しの入口（往路側）
            Vector3 e2 = new Vector3(foldX, apexY, -r);   // 折り返しの出口（復路側）

            var legOut = new RibbonBezier(
                upper,
                new Vector3(armX,                +lh * 0.5f, +r * 0.5f),
                new Vector3(foldX - sx * r * 1.2f, apexY,    +r),
                e1);

            var fold = new RibbonBezier(
                e1,
                new Vector3(foldX + sx * r * CircleKappa, apexY, +r),
                new Vector3(foldX + sx * r * CircleKappa, apexY, -r),
                e2);

            var legBack = new RibbonBezier(
                e2,
                new Vector3(foldX - sx * r * 1.2f, apexY,            -r),
                new Vector3(armX,                  -lh * 0.5f - sag, -r * 0.5f),
                lower);

            return new List<RibbonBezier> { legOut, fold, legBack };
        }

        /// <summary>
        /// 裏返らないループの中心曲線。上側根元 → 外側 → 下側根元 の2セグメント。
        /// sx = +1 で +X 側、-1 で -X 側（X 座標だけ符号を反転する）。
        ///
        /// 外側の折り返し点では、前後の制御点を折り返し点の真上・真下へ置く。
        /// 3点が一直線に並ぶので接線が連続し、折り返しが1サンプル区間へ潰れない。
        /// テールの最大開き点（TailCurve）も同じ手口で作る。
        /// 往路・復路とも +Depth 側へ膨らませる。
        /// </summary>
        private static List<RibbonBezier> LoopCurveFlat(in RibbonBowParams p, float sx)
        {
            float w     = p.RibbonWidth;
            float rootX = w * RootOffsetXScale;
            float gap   = p.Loop.RootGap;
            float lw    = p.Loop.Width;
            float lh    = p.Loop.Height;
            float sag   = p.Loop.Sag;
            float dep   = p.Loop.Depth;

            float armX = sx * (rootX + lw * LoopArmScale);
            float apexX = sx * (rootX + lw);
            float apexY = -sag;
            float fold  = lh * 0.5f;

            Vector3 upper = new Vector3(sx * rootX, +gap * 0.5f, 0f);
            Vector3 lower = new Vector3(sx * rootX, -gap * 0.5f, 0f);
            Vector3 outer = new Vector3(apexX,      apexY,       dep * 0.30f);

            var a = new RibbonBezier(
                upper,
                new Vector3(armX,  +lh * 0.5f,   dep),
                new Vector3(apexX, apexY + fold, dep * 0.50f),
                outer);

            var b = new RibbonBezier(
                outer,
                new Vector3(apexX, apexY - fold,     dep * 0.50f),
                new Vector3(armX,  -lh * 0.5f - sag, dep),
                lower);

            return new List<RibbonBezier> { a, b };
        }

        /// <summary>
        /// 折り返しが帯幅で潰れない最小の LoopHeight。
        ///
        /// 折り返し点での曲率半径は R = 1.5 * f^2 / dx（f = LoopHeight/2 = 制御脚長、
        /// dx = 外側点と中間制御点の X 距離 = (1 - LoopArmScale) * LoopWidth）。
        /// R が帯の半幅を下回ると内側レールが進行方向へ逆走し、面が裏返る。
        /// R >= RibbonWidth/2 を LoopHeight について解いた下限を返す。
        /// Flat 型のみで使う（Flip 型の折り返しは幅方向の軸まわりの曲げになるため）。
        /// </summary>
        public static float MinLoopHeight(float ribbonWidth, float loopWidth)
        {
            float dx = (1f - LoopArmScale) * Mathf.Max(0f, loopWidth);
            return Mathf.Sqrt(Mathf.Max(0f, ribbonWidth * dx * 4f / 3f));
        }

        /// <summary>
        /// テールの中心曲線。根元 → 先端。
        /// TailLength は縦方向の落差、TailSpread は横方向変位（Length 比）。
        ///
        /// Close が 0 のときは従来どおり1セグメントで、外へ開くだけ。
        /// Close が正のときは「開いてから閉じる」形にする。開きが最大になる点 apex
        /// （縦位置 = CloseAt）で2セグメントに割り、apex から先端までに
        /// 横変位を Close の割合だけ中央側へ戻す。
        ///
        /// apex の前後の制御点は apex と同じ X・同じ Z へ置く。3点が鉛直に並ぶので
        /// 接線が連続し、apex が本当に横方向の極値になる
        /// （LoopCurveFlat の外側折り返しと同じ手口）。
        /// </summary>
        private static List<RibbonBezier> TailCurve(in RibbonBowParams p, float sx)
        {
            float w      = p.RibbonWidth;
            float rootX  = w * RootOffsetXScale * 0.6f;
            float rootY  = -(p.Loop.RootGap * 0.5f + w * TailRootDropScale);
            float len    = p.Tail.Length;
            float spread = p.Tail.Spread * len;
            float sag    = p.Tail.Sag;
            float dep    = p.Tail.Depth;
            float close  = p.Tail.Close;

            Vector3 root = new Vector3(sx * rootX, rootY, 0f);

            if (close <= 0f)
            {
                Vector3 tipOpen = new Vector3(sx * (rootX + spread), rootY - len, 0f);

                var open = new RibbonBezier(
                    root,
                    new Vector3(sx * (rootX + spread * 0.10f), rootY - len * (0.35f + sag * 0.25f), dep),
                    new Vector3(sx * (rootX + spread * 0.70f), rootY - len * (0.70f + sag * 0.15f), dep * 0.40f),
                    tipOpen);

                return new List<RibbonBezier> { open };
            }

            float at   = p.Tail.CloseAt;          // Normalized で 0.05..0.95
            float lenA = len * at;                // 根元 → apex の落差
            float lenB = len * (1f - at);         // apex → 先端の落差

            float apexX = sx * (rootX + spread);
            float apexY = rootY - lenA;
            float tipX  = sx * (rootX + spread * (1f - close));
            float tipY  = rootY - len;

            Vector3 apex = new Vector3(apexX, apexY, dep);
            Vector3 tip  = new Vector3(tipX,  tipY,  0f);

            var toApex = new RibbonBezier(
                root,
                new Vector3(sx * (rootX + spread * 0.10f), rootY - lenA * (0.35f + sag * 0.25f), dep),
                new Vector3(apexX, apexY + lenA * ApexHandleScale, dep),
                apex);

            var toTip = new RibbonBezier(
                apex,
                new Vector3(apexX, apexY - lenB * ApexHandleScale, dep),
                new Vector3(Mathf.Lerp(apexX, tipX, 0.70f),
                            apexY - lenB * (0.70f + sag * 0.15f), dep * 0.40f),
                tip);

            return new List<RibbonBezier> { toApex, toTip };
        }

        /// <summary>ノットの中心曲線。下端 → 上端 の1セグメント。幅は KnotWidth 一定。</summary>
        private static List<RibbonBezier> KnotCurve(in RibbonBowParams p)
        {
            float kh = p.Knot.Height;
            float kd = p.Knot.Depth;

            var c = new RibbonBezier(
                new Vector3(0f, -kh * 0.50f, 0f),
                new Vector3(0f, -kh * 0.20f, kd),
                new Vector3(0f, +kh * 0.20f, kd),
                new Vector3(0f, +kh * 0.50f, 0f));

            return new List<RibbonBezier> { c };
        }

        /// <summary>曲線列を逆走させる（各ベジエを反転し、並びも逆にする）。形状は変わらない。</summary>
        private static List<RibbonBezier> Reverse(List<RibbonBezier> segs)
        {
            var r = new List<RibbonBezier>(segs.Count);
            for (int i = segs.Count - 1; i >= 0; i--) r.Add(segs[i].Reversed());
            return r;
        }

        // ================================================================
        // メッシュ化
        // ================================================================

        private static void AppendLadder(
            MeshObject mo, RibbonLadder ladder, in RibbonBowParams p,
            float tipLen, float tagLen)
        {
            if (ladder == null) return;

            int n = ladder.RungCount;
            if (n < 2) return;

            int baseIdx = mo.Vertices.Count;

            // ── rung 頂点（Left, Right の順） ──
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                mo.Vertices.Add(new Vertex(ladder.Left [i], new Vector2(u, 0f)));
                mo.Vertices.Add(new Vertex(ladder.Right[i], new Vector2(u, 1f)));
            }

            // ── 帯の四角形 ──
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = baseIdx + i * 2;
                int r0 = l0 + 1;
                int l1 = l0 + 2;
                int r1 = l0 + 3;
                mo.AddQuad(l0, l1, r1, r0);
            }

            // ── 開始側 ──
            if (p.AddStartTip)
            {
                Vector3 c0 = RungCenter(ladder, 0);
                Vector3 c1 = RungCenter(ladder, 1);
                Vector3 t0 = Direction(c1 - c0, Vector3.up);
                Vector3 b0 = Direction(ladder.Right[0] - ladder.Left[0], Vector3.right);

                Vector3 pStart = c0 - t0 * tipLen;

                int ip = mo.Vertices.Count;
                mo.Vertices.Add(new Vertex(pStart, new Vector2(0f, 0.5f)));
                mo.AddTriangle(ip, baseIdx, baseIdx + 1);

                if (p.AddStartTag)
                {
                    Vector3 tagBase = pStart - t0 * tagLen;

                    int ia = mo.Vertices.Count;
                    mo.Vertices.Add(new Vertex(tagBase + b0 * (tagLen * 0.5f), new Vector2(0f, 1f)));

                    int ib = mo.Vertices.Count;
                    mo.Vertices.Add(new Vertex(tagBase - b0 * (tagLen * 0.5f), new Vector2(0f, 0f)));

                    mo.AddTriangle(ip, ia, ib);
                }
            }

            // ── 終了側 ──
            if (p.AddEndTip)
            {
                Vector3 cN = RungCenter(ladder, n - 1);
                Vector3 cP = RungCenter(ladder, n - 2);
                Vector3 tN = Direction(cN - cP, Vector3.up);

                Vector3 pEnd = cN + tN * tipLen;

                int lLast = baseIdx + (n - 1) * 2;
                int rLast = lLast + 1;

                int ip = mo.Vertices.Count;
                mo.Vertices.Add(new Vertex(pEnd, new Vector2(1f, 0.5f)));
                mo.AddTriangle(ip, rLast, lLast);
            }
        }

        private static Vector3 RungCenter(RibbonLadder ladder, int i)
            => (ladder.Left[i] + ladder.Right[i]) * 0.5f;

        private static Vector3 Direction(Vector3 v, Vector3 fallback)
            => v.sqrMagnitude > 1e-12f ? v.normalized : fallback;
    }
}
