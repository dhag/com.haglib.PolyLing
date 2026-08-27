// WorkAxisGizmoShape.cs
// 作業用ローカル軸 (WorkAxisContext) の六角錐ワイヤ表示を組み立てる。
//
// 従来の矢印ギズモを置き換えるものではなく、追加で描く「軸そのものの形」。
// 矢印は画面固定長 (AxisGizmo.ScreenAxisLength) だが、六角錐は
// WorkAxisContext.Length を使ったワールド長で描く。Y 軸先端が頂点／ボーンへ
// 吸着できるのは、この先端がワールド位置を持つため。
//
// 【軸ごとの長さ】
//   Y は Length そのまま。X / Z はそれぞれ 0.5 倍 / 0.3 倍に縮めて、
//   3本が同じ見た目にならないようにする。底面の半径は3軸共通。
//   Y 軸先端ハンドルの位置は WorkAxisContext.YTip（= Length 基準）で、
//   ここで縮める X / Z の長さとは無関係。
//
// 出力は PlayerViewportPanel.ScreenPolyline[]。GizmoData.ExtraLines へ入れると
// 軸／リングの描画分岐より前に無条件で描かれる（PlayerViewportPanel の
// OnGenerateGizmoOverlay 参照）ため、移動モード（矢印）でも回転モード（リング）でも
// 同じように出せる。座標系は ctx.WorldToScreen が返す系（リングと同じ）。
//
// ヒットテストはここでは行わない。表示専用。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;
using Poly_Ling.Context;

namespace Poly_Ling.Player
{
    /// <summary>作業軸の六角錐ワイヤ（表示専用）。</summary>
    public static class WorkAxisGizmoShape
    {
        // ================================================================
        // 寸法
        // ================================================================

        /// <summary>六角錐の底面半径。Length に対する比。細く見せるため小さめ。</summary>
        public const float RadiusRatio = 0.04f;

        /// <summary>X 軸の長さ倍率（Length に対する比）。</summary>
        public const float LengthRatioX = 0.5f;

        /// <summary>Y 軸の長さ倍率。Y 先端ハンドルの位置と一致させるため 1.0 固定。</summary>
        public const float LengthRatioY = 1.0f;

        /// <summary>Z 軸の長さ倍率（Length に対する比）。</summary>
        public const float LengthRatioZ = 0.3f;

        /// <summary>Y 軸先端マーカーの半径。六角錐の底面半径に対する比。</summary>
        public const float TipMarkerRatio = 2.2f;

        /// <summary>吸着候補マーカーの半径。六角錐の底面半径に対する比。</summary>
        public const float SnapMarkerRatio = 3.0f;

        private const int HexSides = 6;

        private const float LineWidth      = 1.3f;
        private const float LineWidthHi    = 3.0f;

        // ================================================================
        // 色
        // ================================================================

        private static readonly Color XColor   = new Color(0.85f, 0.25f, 0.25f, 0.8f);
        private static readonly Color YColor   = new Color(0.25f, 0.85f, 0.25f, 0.8f);
        private static readonly Color ZColor   = new Color(0.35f, 0.45f, 1f,    0.8f);

        private static readonly Color XColorHi = new Color(1f,    0.45f, 0.45f, 1f);
        private static readonly Color YColorHi = new Color(0.45f, 1f,    0.45f, 1f);
        private static readonly Color ZColorHi = new Color(0.55f, 0.65f, 1f,    1f);

        /// <summary>吸着候補（頂点／ボーン）の強調色。</summary>
        private static readonly Color SnapColor = new Color(1f, 0.9f, 0.25f, 1f);

        /// <summary>変形プレビュー六角柱の色。XYZ のどれとも紛れない中間色にする。</summary>
        private static readonly Color PreviewColor = new Color(1f, 0.75f, 0.35f, 0.9f);

        // ================================================================
        // 変形プレビュー（六角柱）の寸法
        // ================================================================

        /// <summary>プレビュー六角柱の半径。Length に対する比。軸本体より太くして見分ける。</summary>
        public const float PreviewRadiusRatio = 0.07f;

        /// <summary>稜線の分割数。曲げ・ねじりで稜線が曲線になるため細かく割る。</summary>
        private const int PreviewSegments = 12;

        /// <summary>断面の輪を描く位置（0..1）。両端と 1/3・2/3。</summary>
        private static readonly float[] PreviewRingT = { 0f, 1f / 3f, 2f / 3f, 1f };

        private const float PreviewLineWidth = 1.6f;

        // ================================================================
        // 組み立て
        // ================================================================

        /// <summary>
        /// 六角錐3本（＋ Y 先端ハンドル、吸着候補マーカー）を組み立てる。
        /// 引数が足りないときは null を返す（呼び出し側は ExtraLines を設定しない）。
        /// </summary>
        /// <param name="wa">作業軸。</param>
        /// <param name="ctx">投影に使うツールコンテキスト。</param>
        /// <param name="highlightAxis">強調する軸（ホバー／ドラッグ中）。</param>
        /// <param name="highlightYTip">Y 先端ハンドルを強調するか。</param>
        /// <param name="tipOverride">
        /// Y 先端ハンドルを描く位置。ドラッグ中はポインタ（または吸着先）へ離れて
        /// 追従するため、軸の先端とは別の位置になる。null なら軸の先端に戻す。
        /// </param>
        /// <param name="snapTarget">吸着候補のワールド座標。無ければ null。</param>
        /// <param name="showYTipHandle">
        /// Y 先端の六角形（移動・吸着ハンドル）を描くか。
        /// 作業軸ツール以外では掴めないので false にする。
        /// </param>
        public static PlayerViewportPanel.ScreenPolyline[] Build(
            WorkAxisContext wa, ToolContext ctx,
            AxisGizmo.AxisType highlightAxis,
            bool highlightYTip,
            Vector3? tipOverride,
            Vector3? snapTarget,
            bool showYTipHandle = true)
        {
            if (wa == null || ctx == null || ctx.WorldToScreenPos == null) return null;

            float len = Mathf.Max(WorkAxisContext.MinLength, wa.Length);
            float r   = len * RadiusRatio;

            Vector3 ax = wa.AxisX, ay = wa.AxisY, az = wa.AxisZ;
            var list = new List<PlayerViewportPanel.ScreenPolyline>(32);

            // 底面の基底は他の2軸。頂点（錐の先）が軸の向きを示す。
            AppendPyramid(list, ctx, wa.Origin, ax, ay, az, len * LengthRatioX, r,
                          highlightAxis == AxisGizmo.AxisType.X ? XColorHi : XColor,
                          highlightAxis == AxisGizmo.AxisType.X);
            AppendPyramid(list, ctx, wa.Origin, ay, az, ax, len * LengthRatioY, r,
                          highlightAxis == AxisGizmo.AxisType.Y ? YColorHi : YColor,
                          highlightAxis == AxisGizmo.AxisType.Y);
            AppendPyramid(list, ctx, wa.Origin, az, ax, ay, len * LengthRatioZ, r,
                          highlightAxis == AxisGizmo.AxisType.Z ? ZColorHi : ZColor,
                          highlightAxis == AxisGizmo.AxisType.Z);

            // Y 先端ハンドル（向き指定の掴み位置）。カメラ正対の六角形。
            // ドラッグ中は軸から離れてポインタへ追従し、離すと軸の先端へ戻る。
            if (showYTipHandle)
                AppendFacingHex(
                    list, ctx, tipOverride ?? wa.YTip, r * TipMarkerRatio,
                    highlightYTip ? YColorHi : YColor,
                    highlightYTip);

            // 吸着候補。ドラッグ中だけ渡される。
            if (snapTarget.HasValue)
                AppendFacingHex(list, ctx, snapTarget.Value, r * SnapMarkerRatio, SnapColor, true);

            return list.Count > 0 ? list.ToArray() : null;
        }

        // ================================================================
        // 変形プレビュー（デフォーマを通した六角柱）
        // ================================================================

        /// <summary>
        /// 作業軸の +Y に沿った六角柱を作り、各点を deformer に通してから描く。
        /// 曲げなら曲がった柱、ねじりならねじれた柱がそのまま出る。
        ///
        /// 【なぜデフォーマを通すか】
        ///   プレビュー用に曲げ・ねじりの式を書き直すと、本体の式を直したときに
        ///   ずれる。同じ IMeshDeformer を通せば表示と実際の変形が必ず一致する。
        ///
        /// 【DeformContext】
        ///   s の範囲は柱そのもの（0 〜 length）を渡す。曲げ・ねじりの
        ///   TotalAngleDeg は「範囲の全長にわたる合計角」なので、柱の端から端で
        ///   ちょうどその角度になる。
        ///
        /// 呼び出し側は deformer に専用インスタンスを渡すこと。DeformApplier が
        /// 使う実体を渡すと Prepare で内部状態を書き換えてしまう。
        ///
        /// 【offsetFromAxis】
        ///   柱の中心線を +X へ半径ぶんずらす。ねじり用。
        ///   ねじりは Y まわりの回転なので、柱の断面が Y 軸を中心にしていると
        ///   各点が自分の円周上を動くだけで、輪の形も柱の外形も変わらない。
        ///   軸から外して置くと柱ごと Y のまわりを回り、ねじれが形として出る。
        ///   曲げは軸上のままでよい（+X がたわみ量そのものなので、ずらすと
        ///   曲率半径が変わって見た目が変わってしまう）。
        /// </summary>
        /// <param name="wa">作業軸。柱はこのローカル空間で作る。</param>
        /// <param name="ctx">投影に使うツールコンテキスト。</param>
        /// <param name="deformer">通すデフォーマ。null なら null を返す。</param>
        /// <param name="length">柱の長さ（ワールド単位）。</param>
        /// <param name="offsetFromAxis">柱の中心線を +X へ半径ぶんずらすか。</param>
        public static PlayerViewportPanel.ScreenPolyline[] BuildDeformedPrism(
            WorkAxisContext wa, ToolContext ctx, IMeshDeformer deformer, float length,
            bool offsetFromAxis = false)
        {
            if (wa == null || ctx == null || deformer == null) return null;
            if (ctx.WorldToScreenPos == null) return null;

            float len = Mathf.Max(WorkAxisContext.MinLength, length);
            float r   = len * PreviewRadiusRatio;
            float ox  = offsetFromAxis ? r : 0f;

            deformer.Prepare(new DeformContext
            {
                SMin        = 0f,
                SMax        = len,
                LocalMin    = new Vector3(ox - r, 0f,  -r),
                LocalMax    = new Vector3(ox + r, len,  r),
                VertexCount = HexSides * (PreviewSegments + 1),
            });

            var list = new List<PlayerViewportPanel.ScreenPolyline>(HexSides + PreviewRingT.Length);

            // 稜線6本。曲げ・ねじりで曲線になるので分割して折れ線にする。
            for (int i = 0; i < HexSides; i++)
            {
                float a  = 2f * Mathf.PI * i / HexSides;
                float cx = ox + Mathf.Cos(a) * r;
                float cz =      Mathf.Sin(a) * r;

                var pts = new Vector2[PreviewSegments + 1];
                for (int k = 0; k <= PreviewSegments; k++)
                {
                    float y = len * k / PreviewSegments;
                    pts[k] = ctx.WorldToScreen(
                        wa.LocalToWorld(deformer.Evaluate(new Vector3(cx, y, cz))));
                }

                list.Add(new PlayerViewportPanel.ScreenPolyline
                {
                    Points = pts,
                    Color  = PreviewColor,
                    Width  = PreviewLineWidth,
                });
            }

            // 断面の輪。ねじれ具合が読み取れるように途中にも入れる。
            foreach (float t in PreviewRingT)
            {
                float y   = len * t;
                var   pts = new Vector2[HexSides + 1];

                for (int i = 0; i < HexSides; i++)
                {
                    float a = 2f * Mathf.PI * i / HexSides;
                    pts[i] = ctx.WorldToScreen(
                        wa.LocalToWorld(deformer.Evaluate(
                            new Vector3(ox + Mathf.Cos(a) * r, y, Mathf.Sin(a) * r))));
                }
                pts[HexSides] = pts[0];

                list.Add(new PlayerViewportPanel.ScreenPolyline
                {
                    Points = pts,
                    Color  = PreviewColor,
                    Width  = PreviewLineWidth,
                });
            }

            return list.ToArray();
        }

        // ================================================================
        // 六角錐1本
        // ================================================================

        /// <summary>
        /// origin を底面の中心として axis 方向へ長さ len、底面半径 r の
        /// 六角錐ワイヤを追加する。
        /// 底面の基底 (u, v) は呼び出し側が渡す（axis と直交する他の2軸）。
        /// 折れ線は 底面六角形 + 側稜6本 = 7本。錐の頂点が軸の先端になる。
        /// </summary>
        private static void AppendPyramid(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 origin, Vector3 axis, Vector3 u, Vector3 v,
            float len, float r, Color color, bool highlighted)
        {
            Vector3 apex = origin + axis * len;

            var baseRing = new Vector3[HexSides];
            for (int i = 0; i < HexSides; i++)
            {
                float a = 2f * Mathf.PI * i / HexSides;
                baseRing[i] = origin + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
            }

            AppendClosedPoly(dst, ctx, baseRing, color, highlighted);
            for (int i = 0; i < HexSides; i++)
                AppendSegment(dst, ctx, baseRing[i], apex, color, highlighted);
        }

        // ================================================================
        // 回転ハンドル（円弧＋両端の矢印）
        // ================================================================

        /// <summary>
        /// 回転ハンドルの半径。プレビュー六角柱の半径に対する比。
        /// ねじりハンドルがこれを使う（曲げは軸長基準で別に持つ）。
        /// </summary>
        public const float RotateHandleRadiusRatio = 6.4f;

        /// <summary>円弧の開き角（度）。360 未満にして始点と終点を離し、回すものだと分かるようにする。</summary>
        private const float RotateHandleSweepDeg = 270f;

        private const int RotateHandleSegments = 36;

        /// <summary>矢印の頭の長さ。ハンドル半径に対する比。</summary>
        private const float RotateHandleArrowRatio = 0.28f;

        /// <summary>回転ハンドルの既定色（曲げ・ねじり）。</summary>
        public static readonly Color RotateHandleColor   = new Color(0.95f, 0.85f, 0.4f, 0.9f);

        /// <summary>回転ハンドルの強調色。</summary>
        public static readonly Color RotateHandleColorHi = new Color(1f,    1f,    0.6f, 1f);

        /// <summary>
        /// 軸色。回転デフォーマのように軸ごとにハンドルを出すとき、
        /// どのハンドルがどの軸かを色で示すために使う。
        /// </summary>
        public static Color AxisColor(AxisGizmo.AxisType axis, bool highlighted)
        {
            switch (axis)
            {
                case AxisGizmo.AxisType.X: return highlighted ? XColorHi : XColor;
                case AxisGizmo.AxisType.Y: return highlighted ? YColorHi : YColor;
                case AxisGizmo.AxisType.Z: return highlighted ? ZColorHi : ZColor;
                default:                   return highlighted ? RotateHandleColorHi : RotateHandleColor;
            }
        }

        /// <summary>
        /// center を中心に axis まわりの円弧を描き、両端に矢印の頭を付ける。
        /// 回転操作のハンドルであることを形で示すためのもの。
        ///
        /// 矢印を両端に付けるのは、片方向にすると視点が裏へ回ったときに
        /// 実際の回転方向と食い違って見えるため。向きを主張せず「回る」ことだけを示す。
        ///
        /// 戻り値の worldRing はヒットテスト用の円弧のワールド点列（矢印の頭は含まない）。
        /// </summary>
        /// <param name="wa">作業軸。center / axis はこのローカル空間で与える。</param>
        /// <param name="ctx">投影に使うツールコンテキスト。</param>
        /// <param name="centerLocal">円弧の中心（作業軸ローカル）。</param>
        /// <param name="axisLocal">回転軸（作業軸ローカル、正規化済みでなくてよい）。</param>
        /// <param name="radius">円弧の半径（ワールド単位）。</param>
        /// <param name="startAngleDeg">
        /// 円弧の起点をずらす角（度）。現在の変形角を渡すと、ハンドルが
        /// 実際の変形量と同じだけ回って見える。
        /// 基底は v = Cross(n, u) で作ってあるので、ここへ θ を入れることは
        /// Quaternion.AngleAxis(θ, n) を円弧へ適用することと等しい。
        /// </param>
        /// <param name="color">線の色。軸ごとに色を分けたいときに使う。</param>
        /// <param name="highlighted">強調表示するか（線幅が太くなる）。</param>
        /// <param name="worldRing">ヒットテスト用の円弧ワールド点列。</param>
        public static PlayerViewportPanel.ScreenPolyline[] BuildRotateHandle(
            WorkAxisContext wa, ToolContext ctx,
            Vector3 centerLocal, Vector3 axisLocal, float radius,
            float startAngleDeg,
            Color color, bool highlighted, out Vector3[] worldRing)
        {
            worldRing = null;
            if (wa == null || ctx == null || ctx.WorldToScreenPos == null) return null;
            if (axisLocal.sqrMagnitude < 1e-12f || radius <= 0f) return null;

            Vector3 n = axisLocal.normalized;

            // 軸に直交する基底を作る。軸と平行になりにくい種を選ぶ。
            Vector3 seed = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f
                ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(seed, n).normalized;
            Vector3 v = Vector3.Cross(n, u).normalized;

            float sweep = RotateHandleSweepDeg * Mathf.Deg2Rad;
            float start = startAngleDeg * Mathf.Deg2Rad;

            var ring = new Vector3[RotateHandleSegments + 1];
            for (int i = 0; i <= RotateHandleSegments; i++)
            {
                float a = start + sweep * i / RotateHandleSegments;
                ring[i] = centerLocal + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            }
            worldRing = ToWorld(wa, ring);

            float width = highlighted ? LineWidthHi : PreviewLineWidth;

            var list = new List<PlayerViewportPanel.ScreenPolyline>(3);

            var arcPts = new Vector2[ring.Length];
            for (int i = 0; i < ring.Length; i++) arcPts[i] = ctx.WorldToScreen(wa.LocalToWorld(ring[i]));
            list.Add(new PlayerViewportPanel.ScreenPolyline { Points = arcPts, Color = color, Width = width });

            // 両端の矢印。接線方向へ向けた「く」の字を1本の折れ線で描く。
            AppendArrowHead(list, ctx, wa, ring[ring.Length - 1], ring[ring.Length - 2],
                            n, radius * RotateHandleArrowRatio, color, width);
            AppendArrowHead(list, ctx, wa, ring[0], ring[1],
                            n, radius * RotateHandleArrowRatio, color, width);

            return list.ToArray();
        }

        /// <summary>
        /// tip に矢印の頭を追加する。prev から tip への向きを進行方向とし、
        /// 回転面（法線 n）の内側で左右に開いた2本を1本の折れ線にする。
        /// </summary>
        private static void AppendArrowHead(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx, WorkAxisContext wa,
            Vector3 tip, Vector3 prev, Vector3 n, float size, Color color, float width)
        {
            Vector3 dir = tip - prev;
            if (dir.sqrMagnitude < 1e-12f) return;
            dir.Normalize();

            Vector3 side = Vector3.Cross(n, dir).normalized;

            Vector3 a = tip - dir * size + side * size * 0.55f;
            Vector3 b = tip - dir * size - side * size * 0.55f;

            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = new[]
                {
                    ctx.WorldToScreen(wa.LocalToWorld(a)),
                    ctx.WorldToScreen(wa.LocalToWorld(tip)),
                    ctx.WorldToScreen(wa.LocalToWorld(b)),
                },
                Color = color,
                Width = width,
            });
        }

        private static Vector3[] ToWorld(WorkAxisContext wa, Vector3[] local)
        {
            var w = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) w[i] = wa.LocalToWorld(local[i]);
            return w;
        }

        // ================================================================
        // 直線ハンドル（軸線＋先端マーカー）
        // ================================================================

        /// <summary>先端マーカーの大きさ。ハンドル長に対する比。</summary>
        private const float LinearTipRatio = 0.12f;

        /// <summary>直線ハンドルの先端形状。</summary>
        public enum LinearTip
        {
            /// <summary>矢じり。移動ハンドル用。</summary>
            Arrow,
            /// <summary>立方体。拡大縮小ハンドル用。</summary>
            Cube,
        }

        /// <summary>
        /// 原点から axisLocal 方向へ length だけ伸びる軸線を描き、先端にマーカーを付ける。
        /// 移動は矢じり、拡大縮小は立方体にして、掴んだときに何が起きるかを形で示す。
        ///
        /// 戻り値の worldLine はヒットテスト用の軸線ワールド点列（先端マーカーは含まない）。
        /// </summary>
        /// <param name="wa">作業軸。centerLocal / axisLocal はこのローカル空間で与える。</param>
        /// <param name="ctx">投影に使うツールコンテキスト。</param>
        /// <param name="centerLocal">軸線の始点（作業軸ローカル）。</param>
        /// <param name="axisLocal">伸ばす向き（作業軸ローカル）。</param>
        /// <param name="length">軸線の長さ（ワールド単位）。</param>
        /// <param name="tip">先端の形状。</param>
        /// <param name="color">線の色。</param>
        /// <param name="highlighted">強調表示するか（線幅が太くなる）。</param>
        /// <param name="worldLine">ヒットテスト用の軸線ワールド点列。</param>
        public static PlayerViewportPanel.ScreenPolyline[] BuildLinearHandle(
            WorkAxisContext wa, ToolContext ctx,
            Vector3 centerLocal, Vector3 axisLocal, float length,
            LinearTip tip, Color color, bool highlighted, out Vector3[] worldLine)
        {
            worldLine = null;
            if (wa == null || ctx == null || ctx.WorldToScreenPos == null) return null;
            if (axisLocal.sqrMagnitude < 1e-12f || length <= 0f) return null;

            Vector3 n   = axisLocal.normalized;
            Vector3 end = centerLocal + n * length;

            worldLine = ToWorld(wa, new[] { centerLocal, end });

            float width = highlighted ? LineWidthHi : PreviewLineWidth;
            float size  = length * LinearTipRatio;

            // 軸に直交する基底。先端マーカーの向きに使う。
            Vector3 seed = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f
                ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(seed, n).normalized;
            Vector3 v = Vector3.Cross(n, u).normalized;

            var list = new List<PlayerViewportPanel.ScreenPolyline>(8);

            // 軸線
            list.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = new[]
                {
                    ctx.WorldToScreen(wa.LocalToWorld(centerLocal)),
                    ctx.WorldToScreen(wa.LocalToWorld(end)),
                },
                Color = color,
                Width = width,
            });

            if (tip == LinearTip.Arrow) AppendArrowTip(list, ctx, wa, end, n, u, v, size, color, width);
            else                        AppendCubeTip (list, ctx, wa, end, n, u, v, size, color, width);

            return list.ToArray();
        }

        /// <summary>先端の矢じり。四角錐のワイヤで描く。</summary>
        private static void AppendArrowTip(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx, WorkAxisContext wa,
            Vector3 apex, Vector3 n, Vector3 u, Vector3 v, float size, Color color, float width)
        {
            Vector3 baseC = apex - n * size;
            float   r     = size * 0.45f;

            var ring = new[]
            {
                baseC + u * r, baseC + v * r, baseC - u * r, baseC - v * r,
            };

            AppendClosedPolyLocal(dst, ctx, wa, ring, color, width);
            foreach (var c in ring) AppendLine(dst, ctx, wa, c, apex, color, width);
        }

        /// <summary>先端の立方体。上下の四角形と側稜4本で描く。</summary>
        private static void AppendCubeTip(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx, WorkAxisContext wa,
            Vector3 end, Vector3 n, Vector3 u, Vector3 v, float size, Color color, float width)
        {
            float h = size * 0.5f;
            Vector3 back  = end - n * size;

            var a = new[] { back + (u + v) * h, back + (u - v) * h, back - (u + v) * h, back - (u - v) * h };
            var b = new[] { end  + (u + v) * h, end  + (u - v) * h, end  - (u + v) * h, end  - (u - v) * h };

            AppendClosedPolyLocal(dst, ctx, wa, a, color, width);
            AppendClosedPolyLocal(dst, ctx, wa, b, color, width);
            for (int i = 0; i < 4; i++) AppendLine(dst, ctx, wa, a[i], b[i], color, width);
        }

        private static void AppendLine(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx, WorkAxisContext wa,
            Vector3 a, Vector3 b, Color color, float width)
        {
            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = new[]
                {
                    ctx.WorldToScreen(wa.LocalToWorld(a)),
                    ctx.WorldToScreen(wa.LocalToWorld(b)),
                },
                Color = color,
                Width = width,
            });
        }

        // ================================================================
        // カメラ正対の六角形マーカー
        // ================================================================

        /// <summary>
        /// center に、視線へ正対する六角形を追加する。
        /// 基底は視線に直交する任意の正規直交対で足りる（AxisGizmo.TryGetScreenBasis と同じ作り方）。
        /// </summary>
        private static void AppendFacingHex(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 center, float radius, Color color, bool highlighted)
        {
            Vector3 fwd = ctx.CameraTarget - ctx.CameraPosition;
            if (fwd.sqrMagnitude < 1e-12f) return;
            fwd.Normalize();

            Vector3 seed = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f
                ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(seed, fwd).normalized;
            Vector3 v = Vector3.Cross(fwd, u).normalized;

            var ring = new Vector3[HexSides];
            for (int i = 0; i < HexSides; i++)
            {
                float a = 2f * Mathf.PI * i / HexSides;
                ring[i] = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            }
            AppendClosedPoly(dst, ctx, ring, color, highlighted);
        }

        // ================================================================
        // 折れ線ヘルパー
        // ================================================================

        /// <summary>
        /// 閉じた折れ線。作業軸ローカル座標を受け取り、線幅を直接指定する。
        /// 上の AppendClosedPoly はワールド座標を受け取る別物なので取り違えないこと。
        /// </summary>
        private static void AppendClosedPolyLocal(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx, WorkAxisContext wa,
            Vector3[] cornersLocal, Color color, float width)
        {
            var pts = new Vector2[cornersLocal.Length + 1];
            for (int i = 0; i < cornersLocal.Length; i++)
                pts[i] = ctx.WorldToScreen(wa.LocalToWorld(cornersLocal[i]));
            pts[cornersLocal.Length] = pts[0];

            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = pts,
                Color  = color,
                Width  = width,
            });
        }

        private static void AppendClosedPoly(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3[] corners, Color color, bool highlighted)
        {
            var pts = new Vector2[corners.Length + 1];
            for (int i = 0; i < corners.Length; i++) pts[i] = ctx.WorldToScreen(corners[i]);
            pts[corners.Length] = pts[0];

            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = pts,
                Color  = color,
                Width  = highlighted ? LineWidthHi : LineWidth,
            });
        }

        private static void AppendSegment(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 a, Vector3 b, Color color, bool highlighted)
        {
            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = new[] { ctx.WorldToScreen(a), ctx.WorldToScreen(b) },
                Color  = color,
                Width  = highlighted ? LineWidthHi : LineWidth,
            });
        }
    }
}
