// PlayerPrimitiveMeshSubPanel.Profile2D.Canvas.cs
// 図形生成サブパネル：2D押し出しループのキャンバス（描画・ポインタ操作・
// 選択・アンカー・変換ハンドル）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ── P2D 座標変換 ──────────────────────────────────────────────────

        private Vector2 P2dWorldToCanvas(Vector2 world, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2(w * 0.5f + world.x * scale + _p2dOffset.x,
                               h * 0.5f - world.y * scale + _p2dOffset.y);
        }

        private Vector2 P2dCanvasToWorld(Vector2 canvas, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2( (canvas.x - w * 0.5f - _p2dOffset.x) / scale,
                               -(canvas.y - h * 0.5f - _p2dOffset.y) / scale);
        }

        /// <summary>点→セグメント最近傍距離と位置を返す</summary>
        private static float P2dDistToSeg(Vector2 pt, Vector2 a, Vector2 b, out Vector2 closest)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(pt - a, ab) / ab.sqrMagnitude);
            closest = a + ab * t;
            return Vector2.Distance(pt, closest);
        }

        // ── P2D キャンバス描画 ────────────────────────────────────────────

        private void OnDrawP2dCanvas(MeshGenerationContext ctx)
        {
            if (_p2dLoops == null || _p2dLoops.Count == 0) return;
            float w = _p2dCanvas.resolvedStyle.width;
            float h = _p2dCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.27f, 0.27f, 0.32f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float gx = -5f; gx <= 5f; gx += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(gx, -5f), w, h);
                var e = P2dWorldToCanvas(new Vector2(gx,  5f), w, h);
                if (s.x >= 0 && s.x <= w) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            for (float gy = -5f; gy <= 5f; gy += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(-5f, gy), w, h);
                var e = P2dWorldToCanvas(new Vector2( 5f, gy), w, h);
                if (s.y >= 0 && s.y <= h) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.5f, 0.5f, 0.55f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(-5f, 0f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2( 5f, 0f), w, h));
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(0f, -5f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2(0f,  5f), w, h));
            p2d.Stroke();

            // ループ描画
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;

                Color lineColor = (li == _p2dSelLoop) ? Color.yellow
                                : lp.IsHole           ? new Color(1f, 0.3f, 0.3f)
                                :                       new Color(0.2f, 0.75f, 0.85f);

                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int   nxt = (ei + 1) % lp.Points.Count;
                    var   a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var   b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    bool  hov = (li == _p2dHoverEL && ei == _p2dHoverEI);

                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : lineColor;
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }

                // 頂点ドット（頂点表示OFFで抑止）
                if (_p2dShowVerts)
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    bool primary = (li == _p2dSelLoop && pi == _p2dSelPt);
                    bool sel     = _p2dSel.Contains(P2dKey(li, pi));
                    var  sp      = P2dWorldToCanvas(lp.Points[pi], w, h);
                    p2d.fillColor = primary ? Color.white
                                  : sel     ? new Color(1f, 0.85f, 0.2f)
                                  :           lineColor;
                    float r = (sel || primary) ? 5.5f : 3.5f;
                    RevFillCircle(p2d, sp, r, 10);
                }
            }

            // マーキー
            if (_p2dMarquee.Active)
                _p2dMarquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (_p2dShowGizmo || _p2dAnchor.Mode)
                _p2dAnchor.Draw(p2d, P2dWorldToCanvas(_p2dAnchor.Value, w, h));

            // 回転/拡大縮小ハンドル（アンカー設定モード中/ギズモ表示OFFで非表示）
            if (_p2dShowGizmo && !_p2dAnchor.Mode)
                _p2dHandle.Draw(p2d, P2dWorldToCanvas(_p2dAnchor.Value, w, h));

            // マグネット半径（選択点まわり）
            if (_p2dMagnet.Enabled && _p2dSel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        centers.Add(P2dWorldToCanvas(_p2dLoops[li].Points[pi], w, h));
                }
                float cr = Vector2.Distance(P2dWorldToCanvas(Vector2.zero, w, h),
                                            P2dWorldToCanvas(new Vector2(_p2dMagnet.Radius, 0f), w, h));
                _p2dMagnet.DrawRadius(p2d, centers, cr);
            }
        }

        private void RefreshP2dCanvas() => _p2dCanvas?.MarkDirtyRepaint();

        private bool P2dGetSelPt(out Loop loop, out Vector2 pt)
        {
            loop = null; pt = Vector2.zero;
            if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return false;
            loop = _p2dLoops[_p2dSelLoop];
            if (_p2dSelPt < 0 || _p2dSelPt >= loop.Points.Count) return false;
            pt = loop.Points[_p2dSelPt];
            return true;
        }

        private void RefreshP2dPointUI()
        {
            if (_p2dPtRow == null) return;
            if (P2dGetSelPt(out _, out var pt))
            {
                _p2dPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, -5f, 5f));
                _p2dPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _p2dPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -5f, 5f));
                _p2dPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _p2dPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _p2dPtRow.style.display = DisplayStyle.None;
            }
            RefreshP2dAnchorAuto();
        }

        // ── P2D ポインターイベント ────────────────────────────────────────

        private void OnP2dPointerDown(PointerDownEvent e)
        {
            // 中ボタン＝ビューのパン
            if (e.button == 2)
            {
                _p2dPanDrag        = true;
                _p2dPanStart       = e.localPosition;
                _p2dPanOffsetStart = _p2dOffset;
                _p2dCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (_p2dBgMode && _p2dBgTex != null)
            {
                _p2dBgDrag              = true;
                _p2dBgDragStart         = e.localPosition;
                _p2dBgOffsetOnDragStart = _p2dBgOffset;
                _p2dCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureP2DLoops();
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード：ドラッグでアンカー移動
            if (_p2dAnchor.Mode)
            {
                _p2dAnchor.Value = P2dCanvasToWorld(cp, w, h);
                _p2dAnchor.Manual = true;
                _p2dAnchorDrag = true;
                RefreshP2dAnchorFields();
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 0. ハンドルヒット判定（回転/拡大縮小、点編集より優先。ギズモ表示OFFで無効）
            var p2dHit = _p2dShowGizmo
                ? _p2dHandle.HitTest(cp, P2dWorldToCanvas(_p2dAnchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (p2dHit != Canvas2DHandle.HandleType.None)
            {
                BeginP2dHandle(p2dHit, cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 1. 頂点ヒット判定（全ループ、15px以内）
            int   bestL = -1, bestP = -1;
            float bestD = 15f;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    float d = Vector2.Distance(cp, P2dWorldToCanvas(lp.Points[pi], w, h));
                    if (d < bestD) { bestD = d; bestL = li; bestP = pi; }
                }
            }
            if (bestL >= 0)
            {
                long key = P2dKey(bestL, bestP);
                // Ctrl=除外（本体3Dと同じ。ドラッグしない）
                if (e.ctrlKey)
                {
                    bool removed = _p2dSel.Remove(key);
                    if (removed && _p2dSelLoop == bestL && _p2dSelPt == bestP) _p2dSelPt = -1;
                    _p2dCanvas.CapturePointer(e.pointerId);
                    RefreshP2dCanvas(); RefreshP2dPointUI();
                    e.StopPropagation(); return;
                }
                // Shift=追加（トグルではない）／無修飾=別ループなら全点選択・同ループ未選択なら置換
                if (e.shiftKey) _p2dSel.Add(key);
                else if (bestL != _p2dSelLoop) P2dSelectAllInLoop(bestL);   // 別ループ → そのループ全点を選択（一度）
                else if (!_p2dSel.Contains(key)) { _p2dSel.Clear(); _p2dSel.Add(key); }
                _p2dSelLoop = bestL; _p2dSelPt = bestP;
                P2dBegin();
                BeginP2dDrag(cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 2. エッジヒット判定（10px以内）→ 即時挿入
            int   edgeL = -1, edgeI = -1;
            float edgeD = 10f;
            Vector2 insertWorld = Vector2.zero;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;
                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int nxt = (ei + 1) % lp.Points.Count;
                    var a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    float d = P2dDistToSeg(cp, a, b, out var closest);
                    if (d < edgeD)
                    {
                        edgeD  = d; edgeL  = li; edgeI  = ei;
                        insertWorld = P2dCanvasToWorld(closest, w, h);
                    }
                }
            }
            if (edgeL >= 0)
            {
                int insertIdx = edgeI + 1;
                P2dBegin();
                _p2dLoops[edgeL].Points.Insert(insertIdx, insertWorld);
                _p2dSel.Clear(); _p2dSel.Add(P2dKey(edgeL, insertIdx));
                _p2dSelLoop = edgeL; _p2dSelPt = insertIdx;
                BeginP2dDrag(cp, w, h);
                _p2dCanvas.CapturePointer(e.pointerId);
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 3. 空領域 → 矩形/投げ縄マーキー選択（Shift=追加, Ctrl=除外, 無修飾=置換）
            _p2dMarqueeAdditive = e.shiftKey;
            _p2dMarqueeSubtract = e.ctrlKey;
            _p2dMarquee.Begin(cp, _p2dLassoMode);
            _p2dMarqueeDrag = true;
            _p2dCanvas.CapturePointer(e.pointerId);
            RefreshP2dCanvas();
            e.StopPropagation();
        }

        private static long P2dKey(int loop, int pt) => ((long)loop << 32) | (uint)pt;
        private static int  P2dKeyLoop(long k) => (int)(k >> 32);
        private static int  P2dKeyPt(long k)   => (int)(k & 0xffffffff);

        /// <summary>指定ループの全頂点を選択状態にする（主点は先頭）。</summary>
        private void P2dSelectAllInLoop(int li)
        {
            _p2dSel.Clear();
            if (_p2dLoops == null || li < 0 || li >= _p2dLoops.Count) { _p2dSelPt = -1; return; }
            var lp = _p2dLoops[li];
            for (int pi = 0; pi < lp.Points.Count; pi++) _p2dSel.Add(P2dKey(li, pi));
            _p2dSelPt = lp.Points.Count > 0 ? 0 : -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private void BeginP2dDrag(Vector2 cp, float w, float h)
        {
            _p2dDrag = true; _p2dDragIdx = _p2dSelPt;
            _p2dDragStart.Clear();
            foreach (var k in _p2dSel)
            {
                int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                    _p2dDragStart[k] = _p2dLoops[li].Points[pi];
            }
            _p2dDragStartCursorWorld = P2dCanvasToWorld(cp, w, h);

            // マグネット影響点（非選択で半径内）を確定
            _p2dMagnetStart.Clear(); _p2dMagnetW.Clear();
            if (_p2dMagnet.Enabled && _p2dSel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }
                for (int li = 0; li < _p2dLoops.Count; li++)
                {
                    var lp = _p2dLoops[li];
                    for (int pi = 0; pi < lp.Points.Count; pi++)
                    {
                        long key = P2dKey(li, pi);
                        if (_p2dSel.Contains(key)) continue;
                        float wt = _p2dMagnet.WeightFor(lp.Points[pi], sel);
                        if (wt > 0f) { _p2dMagnetStart[key] = lp.Points[pi]; _p2dMagnetW[key] = wt; }
                    }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private void ApplyP2dMarquee()
        {
            float w = _p2dCanvas.resolvedStyle.width, h = _p2dCanvas.resolvedStyle.height;
            if (!_p2dMarqueeAdditive && !_p2dMarqueeSubtract) _p2dSel.Clear();
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                    if (_p2dMarquee.Contains(P2dWorldToCanvas(lp.Points[pi], w, h)))
                    {
                        if (_p2dMarqueeSubtract) _p2dSel.Remove(P2dKey(li, pi));
                        else                     _p2dSel.Add(P2dKey(li, pi));
                    }
            }
            if (!_p2dSel.Contains(P2dKey(_p2dSelLoop, _p2dSelPt)))
            {
                if (_p2dSel.Count > 0) { foreach (var k in _p2dSel) { _p2dSelLoop = P2dKeyLoop(k); _p2dSelPt = P2dKeyPt(k); break; } }
                else _p2dSelPt = -1;
            }
            RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        // ── 回転/拡大縮小アンカー・変換（Profile2D） ──────────────────────

        /// <summary>選択（無ければ全点）のワールド座標リスト。</summary>
        private List<Vector2> SelectedP2dPoints()
        {
            var pts = new List<Vector2>();
            if (_p2dLoops == null) return pts;
            if (_p2dSel.Count > 0)
            {
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        pts.Add(_p2dLoops[li].Points[pi]);
                }
            }
            else
            {
                foreach (var lp in _p2dLoops) pts.AddRange(lp.Points);
            }
            return pts;
        }

        private void BuildP2dAnchorTransformUI(VisualElement pe)
        {
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",   0f, 0f, out _p2dTfMoveX,  out _p2dTfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out _p2dTfScaleX, out _p2dTfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out _p2dTfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",    0f, out _p2dTfRot));
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", ApplyP2dTransform);
            SB(applyRow, "リセット", () =>
            {
                _p2dTfMoveX.value = 0f; _p2dTfMoveY.value = 0f;
                _p2dTfScaleX.value = 1f; _p2dTfScaleY.value = 1f; _p2dTfRot.value = 0f;
                _p2dTfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット（比例編集）
            var magFold = FoldSection(pe, T("Magnet"), false);
            var p2dMagRow = new VisualElement(); p2dMagRow.style.flexDirection = FlexDirection.Row; p2dMagRow.style.marginBottom = 2;
            var p2dMagToggle = new Toggle("有効") { value = _p2dMagnet.Enabled }; p2dMagToggle.style.marginRight = 6;
            p2dMagToggle.RegisterValueChangedCallback(ev => { _p2dMagnet.Enabled = ev.newValue; RefreshP2dCanvas(); });
            var p2dFalloff = new EnumField(_p2dMagnet.Falloff); p2dFalloff.style.flexGrow = 1;
            p2dFalloff.RegisterValueChangedCallback(ev => _p2dMagnet.Falloff = (FalloffType)ev.newValue);
            p2dMagRow.Add(p2dMagToggle); p2dMagRow.Add(p2dFalloff);
            magFold.Add(p2dMagRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 5f, _p2dMagnet.Radius, out _p2dMagnetRadius, out _,
                () => false, v => { _p2dMagnet.Radius = v; RefreshP2dCanvas(); }));

            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            _p2dAnchorEnterBtn = new Button(() => SetP2dAnchorMode(true)) { text = "アンカー設定" };
            _p2dAnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(_p2dAnchorEnterBtn);

            _p2dAnchorPanel = new VisualElement(); _p2dAnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）"); lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetP2dAnchorMode(false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); _p2dAnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyP2dAnchorPreset(Canvas2DAnchor.Preset.BottomLeft));
                _p2dAnchorPanel.Add(presetRow);

                _p2dAnchorPanel.Add(BuildAnchorRow("X", -5f, 5f, 0f, out _p2dAnchorXSlider, out _p2dAnchorXField,
                    () => _p2dAnchorSuppress, v => SetP2dAnchorComponent(true, v)));
                _p2dAnchorPanel.Add(BuildAnchorRow("Y", -5f, 5f, 0f, out _p2dAnchorYSlider, out _p2dAnchorYField,
                    () => _p2dAnchorSuppress, v => SetP2dAnchorComponent(false, v)));
            }
            anchorFold.Add(_p2dAnchorPanel);
            RefreshP2dAnchorModeUI();
            RefreshP2dAnchorFields();
        }

        private void SetP2dAnchorMode(bool on)
        {
            _p2dAnchor.Mode = on;
            if (on) RefreshP2dAnchorAuto();
            RefreshP2dAnchorModeUI();
            RefreshP2dCanvas();
        }
        private void RefreshP2dAnchorModeUI()
        {
            if (_p2dAnchorEnterBtn != null) _p2dAnchorEnterBtn.style.display = _p2dAnchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_p2dAnchorPanel    != null) _p2dAnchorPanel.style.display    = _p2dAnchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void RefreshP2dAnchorFields()
        {
            _p2dAnchorSuppress = true;
            _p2dAnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(_p2dAnchor.Value.x, -5f, 5f));
            _p2dAnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(_p2dAnchor.Value.y, -5f, 5f));
            _p2dAnchorXField?.SetValueWithoutNotify(_p2dAnchor.Value.x);
            _p2dAnchorYField?.SetValueWithoutNotify(_p2dAnchor.Value.y);
            _p2dAnchorSuppress = false;
        }
        private void RefreshP2dAnchorAuto()
        {
            if (_p2dAnchor.Manual) return;
            var pts = SelectedP2dPoints();
            if (pts.Count > 0) _p2dAnchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshP2dAnchorFields();
        }
        private void SetP2dAnchorComponent(bool isX, float v)
        {
            var a = _p2dAnchor.Value; if (isX) a.x = v; else a.y = v; _p2dAnchor.Value = a;
            _p2dAnchor.Manual = true;
            RefreshP2dAnchorFields(); RefreshP2dCanvas();
        }
        private void ApplyP2dAnchorPreset(Canvas2DAnchor.Preset p)
        {
            _p2dAnchor.SetPreset(SelectedP2dPoints(), p);
            RefreshP2dAnchorFields(); RefreshP2dCanvas();
        }

        private void ApplyP2dTransform()
        {
            if (_p2dLoops == null) return;
            P2dBegin();
            RefreshP2dAnchorAuto();
            var a = _p2dAnchor.Value;
            float mx = _p2dTfMoveX?.value ?? 0f, my = _p2dTfMoveY?.value ?? 0f;
            float sx = _p2dTfScaleX?.value ?? 1f, sy = _p2dTfScaleY?.value ?? 1f;
            float deg = _p2dTfRot?.value ?? 0f;
            float saRad = (_p2dTfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = _p2dSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel)
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }

            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    float wt;
                    if (!useSel)                             wt = 1f;
                    else if (_p2dSel.Contains(P2dKey(li, pi))) wt = 1f;
                    else wt = _p2dMagnet.Enabled ? _p2dMagnet.WeightFor(lp.Points[pi], sel) : 0f;
                    if (wt <= 0f) continue;
                    lp.Points[pi] = Xform2D(lp.Points[pi], a, mx, my, sx, sy, saCos, saSin, deg, wt);
                }
            }
            P2dCommit("変換適用");
            D(); RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        // ── 2D押し出し：ハンドルドラッグ（回転/拡大縮小） ─────────────────

        /// <summary>ハンドルドラッグ開始。影響点（選択=1/マグネット=weight、選択なし=全点1）を記録。</summary>
        private void BeginP2dHandle(Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            _p2dHandleDrag = true;
            _p2dHandleType = type;
            _p2dHandle.Active = type;
            RefreshP2dAnchorAuto();
            P2dBegin();

            _p2dHandleAnchorC   = P2dWorldToCanvas(_p2dAnchor.Value, w, h);
            _p2dHandlePrevAngle = Canvas2DHandle.AngleDeg(_p2dHandleAnchorC, cp);
            _p2dHandleTotalDeg  = 0f;

            _p2dHandleStart.Clear(); _p2dHandleW.Clear();
            if (_p2dLoops == null) return;

            bool useSel = _p2dSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel)
                foreach (var k in _p2dSel)
                {
                    int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                    if (li >= 0 && li < _p2dLoops.Count && pi >= 0 && pi < _p2dLoops[li].Points.Count)
                        sel.Add(_p2dLoops[li].Points[pi]);
                }

            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    long key = P2dKey(li, pi);
                    float wt;
                    if (!useSel)                    wt = 1f;
                    else if (_p2dSel.Contains(key)) wt = 1f;
                    else wt = _p2dMagnet.Enabled ? _p2dMagnet.WeightFor(lp.Points[pi], sel) : 0f;
                    if (wt <= 0f) continue;
                    _p2dHandleStart[key] = lp.Points[pi];
                    _p2dHandleW[key]     = wt;
                }
            }
        }

        /// <summary>ハンドルドラッグ中：開始スナップショットへ変換を適用（ライブプレビュー）。</summary>
        private void ApplyP2dHandle(Vector2 cp, float w, float h)
        {
            if (!_p2dHandleDrag || _p2dLoops == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (_p2dHandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(_p2dHandleAnchorC, cp);
                _p2dHandleTotalDeg += -Mathf.DeltaAngle(_p2dHandlePrevAngle, ang);
                _p2dHandlePrevAngle = ang;
                deg = _p2dHandleTotalDeg;
            }
            else
            {
                _p2dHandle.ScaleFactors(_p2dHandleType, _p2dHandleAnchorC, cp, out sx, out sy);
            }

            var a = _p2dAnchor.Value;
            foreach (var kv in _p2dHandleStart)
            {
                int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                if (li < 0 || li >= _p2dLoops.Count) continue;
                if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                _p2dLoops[li].Points[pi] = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, _p2dHandleW[kv.Key]);
            }
            D(); RefreshP2dCanvas(); RefreshP2dPointUI();
        }

        /// <summary>ハンドルドラッグ終了：コミット。</summary>
        private void EndP2dHandle()
        {
            if (!_p2dHandleDrag) return;
            _p2dHandleDrag = false;
            _p2dHandleType = Canvas2DHandle.HandleType.None;
            _p2dHandle.Active = Canvas2DHandle.HandleType.None;
            P2dCommit("回転/拡大縮小");
        }

        private void OnP2dPointerMove(PointerMoveEvent e)
        {
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 中ボタンパン
            if (_p2dPanDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dOffset = _p2dPanOffsetStart + (cp - _p2dPanStart);
                UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // 下絵移動モード
            if (_p2dBgDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dBgOffset = _p2dBgOffsetOnDragStart
                             + (P2dCanvasToWorld(cp, w, h) - P2dCanvasToWorld(_p2dBgDragStart, w, h));
                UpdateP2dBgEl();
                e.StopPropagation(); return;
            }

            // アンカードラッグ
            if (_p2dAnchorDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dAnchor.Value = P2dCanvasToWorld(cp, w, h);
                RefreshP2dAnchorFields(); RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            // ハンドルドラッグ（回転/拡大縮小）
            if (_p2dHandleDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                ApplyP2dHandle(cp, w, h);
                e.StopPropagation(); return;
            }

            // マーキー更新
            if (_p2dMarqueeDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dMarquee.Update(cp);
                RefreshP2dCanvas();
                e.StopPropagation(); return;
            }

            if (_p2dDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                if (_p2dDragStart.Count > 0)
                {
                    // 選択点を一括で delta 移動。
                    var delta = P2dCanvasToWorld(cp, w, h) - _p2dDragStartCursorWorld;
                    foreach (var kv in _p2dDragStart)
                    {
                        int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                        _p2dLoops[li].Points[pi] = kv.Value + delta;
                    }
                    // マグネット: 非選択点を delta×weight で追従
                    foreach (var kv in _p2dMagnetStart)
                    {
                        int li = P2dKeyLoop(kv.Key), pi = P2dKeyPt(kv.Key);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (pi < 0 || pi >= _p2dLoops[li].Points.Count) continue;
                        _p2dLoops[li].Points[pi] = kv.Value + delta * _p2dMagnetW[kv.Key];
                    }
                    D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー更新（非ドラッグ中。ギズモ表示OFF/アンカー設定中は無効）
            var p2dHovType = (_p2dAnchor.Mode || !_p2dShowGizmo)
                                             ? Canvas2DHandle.HandleType.None
                                             : _p2dHandle.HitTest(cp, P2dWorldToCanvas(_p2dAnchor.Value, w, h));
            if (p2dHovType != _p2dHandle.Hovered) { _p2dHandle.Hovered = p2dHovType; RefreshP2dCanvas(); }

            // ホバーエッジ更新（非ドラッグ中）
            int   prevEL = _p2dHoverEL, prevEI = _p2dHoverEI;
            _p2dHoverEL = -1; _p2dHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = false;
            foreach (var lp in _p2dLoops)
                foreach (var pt in lp.Points)
                    if (Vector2.Distance(cp, P2dWorldToCanvas(pt, w, h)) < 15f) { nearPt = true; break; }

            if (!nearPt)
            {
                float bestD = 10f;
                for (int li = 0; li < _p2dLoops.Count; li++)
                {
                    var lp = _p2dLoops[li];
                    if (lp.Points.Count < 2) continue;
                    for (int ei = 0; ei < lp.Points.Count; ei++)
                    {
                        int nxt = (ei + 1) % lp.Points.Count;
                        float d = P2dDistToSeg(cp,
                            P2dWorldToCanvas(lp.Points[ei],  w, h),
                            P2dWorldToCanvas(lp.Points[nxt], w, h), out _);
                        if (d < bestD) { bestD = d; _p2dHoverEL = li; _p2dHoverEI = ei; }
                    }
                }
            }

            if (_p2dHoverEL != prevEL || _p2dHoverEI != prevEI)
                RefreshP2dCanvas();
        }

        private void OnP2dPointerUp(PointerUpEvent e)
        {
            if (!_p2dCanvas.HasPointerCapture(e.pointerId)) return;
            _p2dCanvas.ReleasePointer(e.pointerId);
            if (_p2dMarqueeDrag) { ApplyP2dMarquee(); _p2dMarquee.End(); _p2dMarqueeDrag = false; }
            if (_p2dHandleDrag) EndP2dHandle();
            bool wasP2dDrag = _p2dDrag;
            _p2dDrag   = false;
            _p2dBgDrag = false;
            _p2dPanDrag = false;
            _p2dAnchorDrag = false;
            if (wasP2dDrag) P2dCommit("プロファイル点編集");
            e.StopPropagation();
        }

        /// <summary>下絵レイヤーにプロファイルビューと同じ変換（中心基準ズーム＋パン）を適用。</summary>
        private void UpdateP2dView()
        {
            if (_p2dViewLayer == null) return;
            _p2dViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            _p2dViewLayer.style.scale     = new Scale(new Vector3(_p2dZoom, _p2dZoom, 1f));
            _p2dViewLayer.style.translate = new Translate(
                new Length(_p2dOffset.x), new Length(_p2dOffset.y), 0f);
        }
    }
}
