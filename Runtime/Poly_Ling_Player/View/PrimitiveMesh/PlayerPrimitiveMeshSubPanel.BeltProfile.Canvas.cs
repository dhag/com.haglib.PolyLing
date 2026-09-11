// PlayerPrimitiveMeshSubPanel.BeltProfile.Canvas.cs
// 図形生成サブパネル：ベルト断面プロファイルのキャンバス（描画・ポインタ操作・選択・変換ハンドル）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // ビュー・描画
        // ================================================================

        private static Vector2 BeltP2C(BeltProfileEdit ed, Vector2 p, float w, float h)
            => RevolutionProfileEditCore.ProfileToCanvas(p, w, h, ed.Zoom, ed.Offset);

        private static Vector2 BeltC2P(BeltProfileEdit ed, Vector2 c, float w, float h)
            => RevolutionProfileEditCore.CanvasToProfile(c, w, h, ed.Zoom, ed.Offset);

        private static int BeltFind(BeltProfileEdit ed, Vector2 c, float w, float h, float md)
            => RevolutionProfileEditCore.FindClosest(ed.Points, c, w, h, md, ed.Zoom, ed.Offset);

        private static void RefreshBeltCanvas(BeltProfileEdit ed) => ed?.Canvas?.MarkDirtyRepaint();

        private static void UpdateBeltView(BeltProfileEdit ed)
        {
            if (ed?.ViewLayer == null) return;
            ed.ViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            ed.ViewLayer.style.scale     = new Scale(new Vector3(ed.Zoom, ed.Zoom, 1f));
            ed.ViewLayer.style.translate = new Translate(
                new Length(ed.Offset.x), new Length(ed.Offset.y), 0f);
        }

        private static void UpdateBeltBgEl(BeltProfileEdit ed)
        {
            if (ed?.BgEl == null || ed.BgTex == null || ed.Canvas == null) return;
            float cw = ed.Canvas.resolvedStyle.width;
            float ch = ed.Canvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = ed.BgTex.width;
            float bh = ed.BgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            float baseScale = Mathf.Min(cw / RevolutionProfileEditCore.RangeX,
                                        ch / RevolutionProfileEditCore.RangeY);
            float s = (ed.BgScale * baseScale) / bh;

            Vector2 c = RevolutionProfileEditCore.ProfileToCanvas(ed.BgOffset, cw, ch, 1f, Vector2.zero);
            ed.BgEl.style.left   = c.x - bw * 0.5f; ed.BgEl.style.top = c.y - bh * 0.5f;
            ed.BgEl.style.width  = bw; ed.BgEl.style.height = bh;
            ed.BgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            ed.BgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            ed.BgEl.style.opacity = ed.BgAlpha;
            ed.BgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        private static void RefreshBeltPointUI(BeltProfileEdit ed)
        {
            if (ed.PtRow == null) return;
            if (ed.SelectedIndex >= 0 && ed.Points != null && ed.SelectedIndex < ed.Points.Count)
            {
                var pt = ed.Points[ed.SelectedIndex];
                if (ed.PtLabel != null) ed.PtLabel.text = $"Pt {ed.SelectedIndex}  X={pt.x:F3}  Y={pt.y:F3}";
                ed.PtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, -1f, 2f));
                ed.PtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                ed.PtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -1f, 2f));
                ed.PtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                ed.PtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                ed.PtRow.style.display = DisplayStyle.None;
            }
            RefreshBeltAnchorAuto(ed);
        }

        private void DrawBeltProfile(MeshGenerationContext ctx, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null || ed.Points == null || ed.Points.Count == 0) return;

            float w = ed.Canvas.resolvedStyle.width;
            float h = ed.Canvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.28f, 0.28f, 0.33f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float x = -1f; x <= RevolutionProfileEditCore.RangeX; x += 0.5f)
            {
                p2d.MoveTo(BeltP2C(ed, new Vector2(x, -1f), w, h));
                p2d.LineTo(BeltP2C(ed, new Vector2(x,  2f), w, h));
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                p2d.MoveTo(BeltP2C(ed, new Vector2(-1f, y), w, h));
                p2d.LineTo(BeltP2C(ed, new Vector2( 2f, y), w, h));
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.52f, 0.52f, 0.58f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(BeltP2C(ed, new Vector2(0f, -1f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2(0f,  2f), w, h));
            p2d.MoveTo(BeltP2C(ed, new Vector2(-1f, 0f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2( 2f, 0f), w, h));
            p2d.Stroke();

            // x=1 の目印（正規化の基準）
            p2d.strokeColor = new Color(0.9f, 0.6f, 0.2f, 0.8f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(BeltP2C(ed, new Vector2(1f, -1f), w, h));
            p2d.LineTo(BeltP2C(ed, new Vector2(1f,  2f), w, h));
            p2d.Stroke();

            // 参考プロファイル（A/B のもう一方）。編集対象ではないので灰色で薄く描く。
            if (ed.GhostPoints != null && ed.GhostPoints.Count >= 2)
            {
                var ghost = new Color(0.55f, 0.55f, 0.62f, 0.75f);
                int gseg  = ed.ClosedLoop ? ed.GhostPoints.Count : ed.GhostPoints.Count - 1;

                p2d.strokeColor = ghost;
                p2d.lineWidth   = 1f;
                p2d.BeginPath();
                for (int i = 0; i < gseg; i++)
                {
                    int j = (i + 1) % ed.GhostPoints.Count;
                    p2d.MoveTo(BeltP2C(ed, ed.GhostPoints[i], w, h));
                    p2d.LineTo(BeltP2C(ed, ed.GhostPoints[j], w, h));
                }
                p2d.Stroke();

                p2d.fillColor = ghost;
                for (int i = 0; i < ed.GhostPoints.Count; i++)
                    RevFillCircle(p2d, BeltP2C(ed, ed.GhostPoints[i], w, h), 2.5f, 8);
            }

            // 断面ライン（セグメントごとにホバー表示）
            if (ed.Points.Count >= 2)
            {
                int segCount = BeltSegCount(ed);
                for (int i = 0; i < segCount; i++)
                {
                    int  j   = (i + 1) % ed.Points.Count;
                    var  a   = BeltP2C(ed, ed.Points[i], w, h);
                    var  b   = BeltP2C(ed, ed.Points[j], w, h);
                    bool hov = (i == ed.HoverEI);
                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.75f, 0.85f);
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }
            }

            // 点
            for (int i = 0; i < ed.Points.Count; i++)
            {
                bool sel     = ed.Sel.Contains(i);
                bool primary = (i == ed.SelectedIndex);
                p2d.fillColor = primary ? Color.white
                              : sel     ? new Color(1f, 0.85f, 0.2f)
                              :           new Color(0.2f, 0.75f, 0.85f);
                RevFillCircle(p2d, BeltP2C(ed, ed.Points[i], w, h), (sel || primary) ? 5.5f : 3.5f, 10);
            }

            // マーキー
            if (ed.Marquee.Active)
                ed.Marquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー／ハンドル（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (ed.ShowGizmo || ed.Anchor.Mode)
                ed.Anchor.Draw(p2d, BeltP2C(ed, ed.Anchor.Value, w, h));
            if (ed.ShowGizmo && !ed.Anchor.Mode)
                ed.Handle.Draw(p2d, BeltP2C(ed, ed.Anchor.Value, w, h));

            // マグネット半径
            if (ed.Magnet.Enabled && ed.Sel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) centers.Add(BeltP2C(ed, ed.Points[i], w, h));
                float cr = Vector2.Distance(BeltP2C(ed, Vector2.zero, w, h),
                                            BeltP2C(ed, new Vector2(ed.Magnet.Radius, 0f), w, h));
                ed.Magnet.DrawRadius(p2d, centers, cr);
            }
        }

        /// <summary>断面のセグメント数（閉じた断面は点数と同じ）。</summary>
        private static int BeltSegCount(BeltProfileEdit ed)
            => ed.ClosedLoop ? ed.Points.Count : ed.Points.Count - 1;

        // ================================================================
        // ポインタ操作
        // ================================================================

        private void OnBeltProfilePointerDown(PointerDownEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;

            // 中ボタン＝パン
            if (e.button == 2)
            {
                ed.PanDrag        = true;
                ed.PanStart       = e.localPosition;
                ed.PanOffsetStart = ed.Offset;
                ed.Canvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (ed.BgMode && ed.BgTex != null)
            {
                ed.BgDrag              = true;
                ed.BgDragStart         = e.localPosition;
                ed.BgOffsetOnDragStart = ed.BgOffset;
                ed.Canvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureBeltProfile(ed);

            float w  = ed.Canvas.resolvedStyle.width;
            float h  = ed.Canvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード
            if (ed.Anchor.Mode)
            {
                ed.Anchor.Value  = BeltC2P(ed, cp, w, h);
                ed.Anchor.Manual = true;
                ed.AnchorDrag    = true;
                RefreshBeltAnchorFields(ed);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            // 0. ハンドル（回転/拡大縮小。ギズモ表示OFFで無効）
            var hit = ed.ShowGizmo
                ? ed.Handle.HitTest(cp, BeltP2C(ed, ed.Anchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (hit != Canvas2DHandle.HandleType.None)
            {
                BeginBeltHandle(ed, hit, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            // 1. 点ヒット（15px以内）
            int ptIdx = BeltFind(ed, cp, w, h, 15f);
            if (ptIdx >= 0)
            {
                if (e.shiftKey)
                {
                    if (!ed.Sel.Add(ptIdx)) ed.Sel.Remove(ptIdx);
                    ed.SelectedIndex = ed.Sel.Contains(ptIdx) ? ptIdx : BeltPrimary(ed);
                    ed.Canvas.CapturePointer(e.pointerId);
                    RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                    e.StopPropagation(); return;
                }
                if (!ed.Sel.Contains(ptIdx)) { ed.Sel.Clear(); ed.Sel.Add(ptIdx); }
                ed.SelectedIndex = ptIdx;
                BeltBegin(ed);
                BeginBeltDrag(ed, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                e.StopPropagation(); return;
            }

            // 2. 線分ヒット（10px以内）→ 即時挿入＆ドラッグ開始
            int     bestSeg    = -1;
            float   bestDist   = 10f;
            Vector2 insertProf = Vector2.zero;
            int     segCount   = BeltSegCount(ed);
            for (int i = 0; i < segCount; i++)
            {
                int   j = (i + 1) % ed.Points.Count;
                var   a = BeltP2C(ed, ed.Points[i], w, h);
                var   b = BeltP2C(ed, ed.Points[j], w, h);
                float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                if (d < bestDist)
                {
                    bestDist   = d;
                    bestSeg    = i;
                    insertProf = Vector2.Lerp(ed.Points[i], ed.Points[j], t);
                }
            }
            if (bestSeg >= 0)
            {
                int insertIdx = bestSeg + 1;
                BeltBegin(ed);
                ed.Points.Insert(insertIdx, insertProf);
                ed.Sel.Clear(); ed.Sel.Add(insertIdx);
                ed.SelectedIndex = insertIdx;
                ed.HoverEI = -1;
                BeginBeltDrag(ed, cp, w, h);
                ed.Canvas.CapturePointer(e.pointerId);
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                e.StopPropagation(); return;
            }

            // 3. 空領域 → マーキー選択（Shiftで追加）
            ed.MarqueeAdditive = e.shiftKey;
            ed.Marquee.Begin(cp, ed.LassoMode);
            ed.MarqueeDrag = true;
            ed.Canvas.CapturePointer(e.pointerId);
            RefreshBeltCanvas(ed);
            e.StopPropagation();
        }

        private void OnBeltProfilePointerMove(PointerMoveEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;

            float w  = ed.Canvas.resolvedStyle.width;
            float h  = ed.Canvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            if (ed.PanDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Offset = ed.PanOffsetStart + (cp - ed.PanStart);
                UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.BgDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.BgOffset = ed.BgOffsetOnDragStart
                            + (BeltC2P(ed, cp, w, h) - BeltC2P(ed, ed.BgDragStart, w, h));
                UpdateBeltBgEl(ed);
                e.StopPropagation(); return;
            }

            if (ed.AnchorDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Anchor.Value = BeltC2P(ed, cp, w, h);
                RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.HandleDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ApplyBeltHandle(ed, cp, w, h);
                e.StopPropagation(); return;
            }

            if (ed.MarqueeDrag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                ed.Marquee.Update(cp);
                RefreshBeltCanvas(ed);
                e.StopPropagation(); return;
            }

            if (ed.Drag && ed.Canvas.HasPointerCapture(e.pointerId))
            {
                if (ed.Points != null && ed.DragStart.Count > 0)
                {
                    var delta = BeltC2P(ed, cp, w, h) - ed.DragStartCursorProf;
                    foreach (var kv in ed.DragStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= ed.Points.Count) continue;
                        ed.Points[idx] = kv.Value + delta;
                    }
                    foreach (var kv in ed.MagnetStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= ed.Points.Count) continue;
                        ed.Points[idx] = kv.Value + delta * ed.MagnetW[idx];
                    }
                    D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー（ギズモ表示OFF/アンカー設定中は無効）
            var hovType = (ed.Anchor.Mode || !ed.ShowGizmo)
                                         ? Canvas2DHandle.HandleType.None
                                         : ed.Handle.HitTest(cp, BeltP2C(ed, ed.Anchor.Value, w, h));
            if (hovType != ed.Handle.Hovered) { ed.Handle.Hovered = hovType; RefreshBeltCanvas(ed); }

            // 線分ホバー
            int prevHov = ed.HoverEI;
            ed.HoverEI = -1;
            if (ed.Points != null && ed.Points.Count >= 2 && BeltFind(ed, cp, w, h, 15f) < 0)
            {
                int   segCount = BeltSegCount(ed);
                float bestD    = 10f;
                for (int i = 0; i < segCount; i++)
                {
                    int   j = (i + 1) % ed.Points.Count;
                    var   a = BeltP2C(ed, ed.Points[i], w, h);
                    var   b = BeltP2C(ed, ed.Points[j], w, h);
                    float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                    float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                    if (d < bestD) { bestD = d; ed.HoverEI = i; }
                }
            }
            if (ed.HoverEI != prevHov) RefreshBeltCanvas(ed);
        }

        private void OnBeltProfilePointerUp(PointerUpEvent e, BeltProfileEdit ed)
        {
            if (ed?.Canvas == null) return;
            if (!ed.Canvas.HasPointerCapture(e.pointerId)) return;
            ed.Canvas.ReleasePointer(e.pointerId);

            if (ed.MarqueeDrag) { ApplyBeltMarquee(ed); ed.Marquee.End(); ed.MarqueeDrag = false; }
            if (ed.HandleDrag)  EndBeltHandle(ed);

            bool wasDrag = ed.Drag;
            ed.Drag       = false;
            ed.BgDrag     = false;
            ed.PanDrag    = false;
            ed.AnchorDrag = false;
            if (wasDrag) BeltCommit(ed, "断面点編集");
            e.StopPropagation();
        }

        /// <summary>選択集合の代表インデックス（無ければ -1）。</summary>
        private static int BeltPrimary(BeltProfileEdit ed)
        {
            foreach (var i in ed.Sel) return i;
            return -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private static void BeginBeltDrag(BeltProfileEdit ed, Vector2 cp, float w, float h)
        {
            ed.Drag = true;
            ed.DragStart.Clear();
            if (ed.Points != null)
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) ed.DragStart[i] = ed.Points[i];
            ed.DragStartCursorProf = BeltC2P(ed, cp, w, h);

            ed.MagnetStart.Clear(); ed.MagnetW.Clear();
            if (ed.Magnet.Enabled && ed.Points != null && ed.Sel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);
                for (int i = 0; i < ed.Points.Count; i++)
                {
                    if (ed.Sel.Contains(i)) continue;
                    float wt = ed.Magnet.WeightFor(ed.Points[i], sel);
                    if (wt > 0f) { ed.MagnetStart[i] = ed.Points[i]; ed.MagnetW[i] = wt; }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private static void ApplyBeltMarquee(BeltProfileEdit ed)
        {
            float w = ed.Canvas.resolvedStyle.width, h = ed.Canvas.resolvedStyle.height;
            if (!ed.MarqueeAdditive) ed.Sel.Clear();
            if (ed.Points != null)
                for (int i = 0; i < ed.Points.Count; i++)
                    if (ed.Marquee.Contains(BeltP2C(ed, ed.Points[i], w, h))) ed.Sel.Add(i);
            ed.SelectedIndex = ed.Sel.Contains(ed.SelectedIndex) ? ed.SelectedIndex : BeltPrimary(ed);
            RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        // ── ハンドルドラッグ ─────────────────────────────────────────────

        private void BeginBeltHandle(BeltProfileEdit ed, Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            ed.HandleDrag   = true;
            ed.HandleType   = type;
            ed.Handle.Active = type;
            RefreshBeltAnchorAuto(ed);
            BeltBegin(ed);

            ed.HandleAnchorC   = BeltP2C(ed, ed.Anchor.Value, w, h);
            ed.HandlePrevAngle = Canvas2DHandle.AngleDeg(ed.HandleAnchorC, cp);
            ed.HandleTotalDeg  = 0f;

            ed.HandleStart.Clear(); ed.HandleW.Clear();
            if (ed.Points == null) return;

            bool useSel = ed.Sel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);

            for (int i = 0; i < ed.Points.Count; i++)
            {
                float wt;
                if (!useSel)                 wt = 1f;
                else if (ed.Sel.Contains(i)) wt = 1f;
                else wt = ed.Magnet.Enabled ? ed.Magnet.WeightFor(ed.Points[i], sel) : 0f;
                if (wt <= 0f) continue;
                ed.HandleStart[i] = ed.Points[i];
                ed.HandleW[i]     = wt;
            }
        }

        private void ApplyBeltHandle(BeltProfileEdit ed, Vector2 cp, float w, float h)
        {
            if (!ed.HandleDrag || ed.Points == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (ed.HandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(ed.HandleAnchorC, cp);
                ed.HandleTotalDeg += -Mathf.DeltaAngle(ed.HandlePrevAngle, ang);
                ed.HandlePrevAngle = ang;
                deg = ed.HandleTotalDeg;
            }
            else
            {
                ed.Handle.ScaleFactors(ed.HandleType, ed.HandleAnchorC, cp, out sx, out sy);
            }

            var a = ed.Anchor.Value;
            foreach (var kv in ed.HandleStart)
            {
                int i = kv.Key;
                if (i < 0 || i >= ed.Points.Count) continue;
                ed.Points[i] = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, ed.HandleW[i]);
            }
            D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        private void EndBeltHandle(BeltProfileEdit ed)
        {
            if (!ed.HandleDrag) return;
            ed.HandleDrag    = false;
            ed.HandleType    = Canvas2DHandle.HandleType.None;
            ed.Handle.Active = Canvas2DHandle.HandleType.None;
            BeltCommit(ed, "回転/拡大縮小");
        }
    }
}
