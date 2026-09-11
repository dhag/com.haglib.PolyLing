// PlayerUVEditorSubPanel.Canvas.cs
// UV エディタ：プレビューのリサイズ・座標変換・ヒットテスト・キャンバス描画・キャンバス入力。
// Runtime/Poly_Ling_Player/View/SubPanels/UV/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public partial class PlayerUVEditorSubPanel
    {
        // ================================================================
        // プレビュー縦リサイズ
        // ================================================================

        /// <summary>プレビューキャンバス直下に縦リサイズハンドルを追加する。</summary>
        private void AddUvResizeHandle(VisualElement container)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginBottom    = 4;
            handle.style.flexShrink      = 0;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                _uvResizeDragging    = true;
                _uvResizeStartY      = e.position.y;
                _uvResizeStartHeight = _uvCanvasHeight;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_uvResizeDragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - _uvResizeStartY;
                _uvCanvasHeight = Mathf.Clamp(_uvResizeStartHeight + delta, UvCanvasMinHeight, UvCanvasMaxHeight);
                _canvas.style.height = _uvCanvasHeight;
                UpdateCanvasBackground();
                _canvas.MarkDirtyRepaint();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                _uvResizeDragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private Vector2 UVToCanvas(Vector2 uv, Rect rect)
        {
            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            float cx = rect.width * 0.5f + _panOffset.x;
            float cy = rect.height * 0.5f + _panOffset.y;
            return new Vector2(cx + (uv.x - 0.5f) * size,
                               cy - (uv.y - 0.5f) * size);
        }

        private Vector2 CanvasToUV(Vector2 pixel, Rect rect)
        {
            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            if (size < 0.001f) return new Vector2(0.5f, 0.5f);
            float cx = rect.width * 0.5f + _panOffset.x;
            float cy = rect.height * 0.5f + _panOffset.y;
            return new Vector2((pixel.x - cx) / size + 0.5f,
                               -(pixel.y - cy) / size + 0.5f);
        }

        // ================================================================
        // ヒットテスト
        // ================================================================

        private UVVertexId? HitTestUVVertex(Vector2 pos)
        {
            var mo = GetMeshObject();
            if (mo == null) return null;
            var rect = _canvas.contentRect;
            if (rect.width < 1) return null;

            float bestSq = HitRadius * HitRadius;
            UVVertexId? best = null;
            var tested = new HashSet<long>();

            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    long key = ((long)vi << 32) | (uint)ui;
                    if (!tested.Add(key)) continue;
                    var v = mo.Vertices[vi];
                    if (ui < 0 || ui >= v.UVs.Count) continue;
                    float dsq = ((UVToCanvas(v.UVs[ui], rect)) - pos).sqrMagnitude;
                    if (dsq < bestSq) { bestSq = dsq; best = new UVVertexId(vi, ui); }
                }
            }
            return best;
        }

        // ================================================================
        // キャンバス描画
        // ================================================================

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            var rect    = _canvas.contentRect;
            if (rect.width < 1) return;
            DrawGrid(painter, rect);
            DrawUVWireframe(painter, rect);
            DrawAnchorMarker(painter, rect);
            if (!_anchorMode)
                _uvHandle.Draw(painter, UVToCanvas(_anchor, rect));  // 回転/拡大縮小ハンドル
            DrawUVMagnetRadius(painter, rect);
            if (_marquee.Active)
                _marquee.Draw(painter, new Color(1f, 0.85f, 0.2f, 0.9f));
        }

        private void DrawGrid(Painter2D p, Rect rect)
        {
            p.strokeColor = GridColor;
            p.lineWidth   = 1f;
            for (int i = 1; i <= 3; i++)
            {
                float t = i * 0.25f;
                Line(p, UVToCanvas(new Vector2(0, t), rect), UVToCanvas(new Vector2(1, t), rect));
                Line(p, UVToCanvas(new Vector2(t, 0), rect), UVToCanvas(new Vector2(t, 1), rect));
            }
            p.strokeColor = GridBorderColor;
            p.lineWidth   = 1.5f;
            p.BeginPath();
            p.MoveTo(UVToCanvas(Vector2.zero,       rect));
            p.LineTo(UVToCanvas(new Vector2(1, 0),  rect));
            p.LineTo(UVToCanvas(Vector2.one,        rect));
            p.LineTo(UVToCanvas(new Vector2(0, 1),  rect));
            p.ClosePath();
            p.Stroke();
        }

        private void DrawUVWireframe(Painter2D p, Rect rect)
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            var verts = mo.Vertices;
            var faces = mo.Faces;

            // ワイヤフレーム
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                // 選択中マテリアルの面のみ描画
                if (_matsWithVerts.Count > 0 && face.MaterialIndex != _matsWithVerts[_selectedMatListIndex]) continue;

                bool hasSel = false;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    if (_selected.Contains(new UVVertexId(vi, ui))) { hasSel = true; break; }
                }
                p.strokeColor = hasSel ? WireSelectedColor : WireColor;
                p.lineWidth   = hasSel ? 1.5f : 1f;
                p.BeginPath();
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= verts.Count) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    Vector2 uv = (ui >= 0 && ui < verts[vi].UVs.Count) ? verts[vi].UVs[ui] : Vector2.zero;
                    var pt = UVToCanvas(uv, rect);
                    if (ci == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.ClosePath();
                p.Stroke();
            }

            // 頂点ドット
            var drawn = new HashSet<long>();
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                if (face == null) continue;
                if (_matsWithVerts.Count > 0 && face.MaterialIndex != _matsWithVerts[_selectedMatListIndex]) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= verts.Count) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    long key = ((long)vi << 32) | (uint)ui;
                    if (!drawn.Add(key)) continue;
                    Vector2 uv = (ui >= 0 && ui < verts[vi].UVs.Count) ? verts[vi].UVs[ui] : Vector2.zero;
                    var id = new UVVertexId(vi, ui);
                    bool sel  = _selected.Contains(id);
                    bool hov  = _hovered.HasValue && _hovered.Value == id;
                    Color col = sel ? VertexSelColor : (hov ? VertexHoverColor : VertexColor);
                    float r   = sel ? VertexSelDotR : VertexDotR;
                    var pt = UVToCanvas(uv, rect);
                    p.fillColor = col;
                    p.BeginPath();
                    p.Arc(pt, r, 0f, 360f);
                    p.Fill();
                }
            }
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        /// <summary>マグネット影響半径を選択頂点まわりの円で描画する。</summary>
        private void DrawUVMagnetRadius(Painter2D p, Rect rect)
        {
            if (!_uvMagnet.Enabled || _selected.Count == 0) return;
            var mo = GetMeshObject();
            if (mo == null) return;
            var centers = new List<Vector2>();
            foreach (var id in _selected)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    centers.Add(UVToCanvas(v.UVs[id.UVIndex], rect));
            }
            float cr = Vector2.Distance(UVToCanvas(Vector2.zero, rect),
                                        UVToCanvas(new Vector2(_uvMagnet.Radius, 0f), rect));
            _uvMagnet.DrawRadius(p, centers, cr);
        }

        /// <summary>回転/拡大縮小アンカーの十字マーカーを描画する。</summary>
        private void DrawAnchorMarker(Painter2D p, Rect rect)
        {
            var c = UVToCanvas(_anchor, rect);
            float s = 9f;
            var col = _anchorMode ? new Color(1f, 0.35f, 0.85f) : new Color(1f, 0.5f, 0.9f, 0.75f);
            p.strokeColor = col; p.lineWidth = _anchorMode ? 2f : 1.25f;
            Line(p, new Vector2(c.x - s, c.y), new Vector2(c.x + s, c.y));
            Line(p, new Vector2(c.x, c.y - s), new Vector2(c.x, c.y + s));
            p.fillColor = new Color(col.r, col.g, col.b, 0.15f);
            p.BeginPath(); p.Arc(c, s * 0.6f, 0f, 360f); p.Stroke();
        }

        // ================================================================
        // キャンバス入力
        // ================================================================

        private void OnCanvasWheel(WheelEvent evt)
        {
            var rect = _canvas.contentRect;
            var mp   = evt.localMousePosition;
            var uvBefore = CanvasToUV(mp, rect);
            float delta  = -evt.delta.y * ZoomSpeed;
            _zoom = Mathf.Clamp(_zoom * (1f + delta), MinZoom, MaxZoom);
            _panOffset += mp - UVToCanvas(uvBefore, rect);
            UpdateCanvasBackground();
            _canvas.MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnCanvasMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _interaction   = Interaction.Panning;
                _mouseDownPos  = evt.localMousePosition;
                _panStartOffset = _panOffset;
                _canvas.CaptureMouse();
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0 && _anchorMode)
            {
                // アンカー設定モード：ドラッグでアンカーを移動（点編集/マーキーは行わない）
                var rectA = _canvas.contentRect;
                _anchor = CanvasToUV(evt.localMousePosition, rectA);
                _anchorManual = true;
                RefreshAnchorFields();
                _interaction = Interaction.AnchorDrag;
                _canvas.CaptureMouse();
                _canvas.MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0)
            {
                // ハンドルヒット判定（回転/拡大縮小、頂点編集より優先）
                var rectH = _canvas.contentRect;
                var uvHit = _uvHandle.HitTest(evt.localMousePosition, UVToCanvas(_anchor, rectH));
                if (uvHit != Canvas2DHandle.HandleType.None)
                {
                    BeginUVHandle(uvHit, evt.localMousePosition, rectH);
                    _canvas.CaptureMouse();
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }

                _mouseDownPos = evt.localMousePosition;
                _hitUV        = HitTestUVVertex(evt.localMousePosition);

                if (_hitUV.HasValue)
                {
                    if (evt.shiftKey)
                    {
                        // Shift+クリック＝トグル（ドラッグ移動しない）
                        if (!_selected.Add(_hitUV.Value)) _selected.Remove(_hitUV.Value);
                        UpdateInfo(GetMeshObject());
                        RefreshAnchorAuto();
                        _canvas.MarkDirtyRepaint();
                        _interaction = Interaction.Idle;
                    }
                    else
                    {
                        if (!_selected.Contains(_hitUV.Value))
                        {
                            _selected.Clear();
                            _selected.Add(_hitUV.Value);
                            UpdateInfo(GetMeshObject());
                            RefreshAnchorAuto();
                            _canvas.MarkDirtyRepaint();
                        }
                        _interaction = Interaction.PendingAction;
                    }
                }
                else
                {
                    // 空ドラッグ＝矩形/投げ縄マーキー選択（Shiftで追加）
                    _marqueeAdditive = evt.shiftKey;
                    _marquee.Begin(evt.localMousePosition, _lassoMode);
                    _interaction = Interaction.Marquee;
                }
                _canvas.CaptureMouse();
                evt.StopPropagation();
            }
        }

        private void OnCanvasMouseMove(MouseMoveEvent evt)
        {
            var mp = evt.localMousePosition;

            switch (_interaction)
            {
                case Interaction.Panning:
                    _panOffset = _panStartOffset + (mp - _mouseDownPos);
                    UpdateCanvasBackground();
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case Interaction.PendingAction:
                    if (Vector2.Distance(mp, _mouseDownPos) > DragThreshold)
                    {
                        BeginUVMove();
                        ApplyUVMove(mp);
                        _canvas.MarkDirtyRepaint();
                    }
                    evt.StopPropagation();
                    return;

                case Interaction.MovingVertex:
                    ApplyUVMove(mp);
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case Interaction.Marquee:
                    _marquee.Update(mp);
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case Interaction.AnchorDrag:
                    _anchor = CanvasToUV(mp, _canvas.contentRect);
                    _anchorManual = true;
                    RefreshAnchorFields();
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case Interaction.HandleDrag:
                    ApplyUVHandle(mp);
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case Interaction.Idle:
                    // ハンドルホバー
                    var uvHovType = _anchorMode ? Canvas2DHandle.HandleType.None
                                                : _uvHandle.HitTest(mp, UVToCanvas(_anchor, _canvas.contentRect));
                    if (uvHovType != _uvHandle.Hovered) { _uvHandle.Hovered = uvHovType; _canvas.MarkDirtyRepaint(); }
                    if (uvHovType != Canvas2DHandle.HandleType.None)
                    {
                        if (_hovered.HasValue) { _hovered = null; _canvas.MarkDirtyRepaint(); }
                        return;
                    }
                    var newHov = HitTestUVVertex(mp);
                    bool changed = (newHov.HasValue != _hovered.HasValue) ||
                                   (newHov.HasValue && _hovered.HasValue && newHov.Value != _hovered.Value);
                    if (changed) { _hovered = newHov; _canvas.MarkDirtyRepaint(); }
                    return;
            }
        }

        private void OnCanvasMouseUp(MouseUpEvent evt)
        {
            if (_interaction == Interaction.MovingVertex) EndUVMove();
            else if (_interaction == Interaction.Marquee) { ApplyMarqueeSelection(); _marquee.End(); }
            else if (_interaction == Interaction.HandleDrag) EndUVHandle();
            _interaction = Interaction.Idle;
            if (_canvas.HasMouseCapture()) _canvas.ReleaseMouse();
            evt.StopPropagation();
        }

        /// <summary>マーキー内側のUV頂点を選択する（Shift追加でなければ置換）。</summary>
        private void ApplyMarqueeSelection()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            var rect = _canvas.contentRect;
            if (rect.width < 1) return;

            if (!_marqueeAdditive) _selected.Clear();

            var tested = new HashSet<long>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                // 表示中マテリアルのみ対象（ワイヤフレーム描画と整合）
                if (_matsWithVerts.Count > 0 && face.MaterialIndex != _matsWithVerts[_selectedMatListIndex]) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    long key = ((long)vi << 32) | (uint)ui;
                    if (!tested.Add(key)) continue;
                    var v = mo.Vertices[vi];
                    if (ui < 0 || ui >= v.UVs.Count) continue;
                    if (_marquee.Contains(UVToCanvas(v.UVs[ui], rect)))
                        _selected.Add(new UVVertexId(vi, ui));
                }
            }
            UpdateInfo(mo);
            RefreshAnchorAuto();
            _canvas.MarkDirtyRepaint();
        }
    }
}
