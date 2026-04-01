// PlayerUVEditorSubPanel.cs
// UVエディタサブパネル（Player ビルド用）。
// UVEditPanel の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    // ================================================================
    // UV頂点識別子
    // ================================================================

    public readonly struct UVVertexId : IEquatable<UVVertexId>
    {
        public readonly int VertexIndex;
        public readonly int UVIndex;

        public UVVertexId(int vertexIndex, int uvIndex)
        {
            VertexIndex = vertexIndex;
            UVIndex     = uvIndex;
        }

        public bool Equals(UVVertexId other) =>
            VertexIndex == other.VertexIndex && UVIndex == other.UVIndex;

        public override bool Equals(object obj) =>
            obj is UVVertexId o && Equals(o);

        public override int GetHashCode() => (VertexIndex << 16) ^ UVIndex;

        public static bool operator ==(UVVertexId a, UVVertexId b) => a.Equals(b);
        public static bool operator !=(UVVertexId a, UVVertexId b) => !a.Equals(b);
    }

    // ================================================================
    // UV エディタサブパネル
    // ================================================================

    public class PlayerUVEditorSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext>        GetModel;
        public Func<MeshUndoController>  GetUndoController;
        public Func<CommandQueue>        GetCommandQueue;
        public Action                    OnRepaint;

        // ================================================================
        // 描画定数
        // ================================================================

        private static readonly Color GridColor        = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color GridBorderColor  = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color WireColor        = new Color(0.6f, 0.8f, 1.0f, 0.8f);
        private static readonly Color WireSelectedColor = new Color(1.0f, 0.5f, 0.2f, 1.0f);
        private static readonly Color VertexColor      = new Color(0.4f, 0.7f, 1.0f, 1.0f);
        private static readonly Color VertexSelColor   = new Color(1.0f, 0.3f, 0.1f, 1.0f);
        private static readonly Color VertexHoverColor = new Color(1.0f, 0.8f, 0.2f, 1.0f);

        private const float VertexDotR    = 2.5f;
        private const float VertexSelDotR = 4f;
        private const float MinZoom       = 0.1f;
        private const float MaxZoom       = 20f;
        private const float ZoomSpeed     = 0.1f;
        private const float DragThreshold = 4f;
        private const float HitRadius     = 8f;

        // ================================================================
        // 状態
        // ================================================================

        private Vector2 _panOffset = Vector2.zero;
        private float   _zoom      = 1f;

        private enum Interaction { Idle, Panning, PendingAction, MovingVertex }
        private Interaction _interaction = Interaction.Idle;

        private Vector2 _mouseDownPos;
        private Vector2 _panStartOffset;

        private readonly HashSet<UVVertexId>              _selected    = new HashSet<UVVertexId>();
        private readonly Dictionary<UVVertexId, Vector2>  _dragStartUVs = new Dictionary<UVVertexId, Vector2>();
        private UVVertexId? _hitUV;
        private UVVertexId? _hovered;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private Label         _infoLabel;
        private Label         _statusLabel;
        private VisualElement _canvas;
        private VisualElement _transformSection;
        private FloatField    _moveU, _moveV, _scaleU, _scaleV, _rotateDeg;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.flexGrow    = 1;
            _root.style.paddingLeft = _root.style.paddingRight = 2;
            _root.style.paddingTop  = _root.style.paddingBottom = 2;
            parent.Add(_root);

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _root.Add(_warningLabel);

            // 情報ラベル
            _infoLabel = new Label();
            _infoLabel.style.fontSize    = 10;
            _infoLabel.style.marginBottom = 2;
            _root.Add(_infoLabel);

            // キャンバス
            _canvas = new VisualElement();
            _canvas.style.flexGrow        = 1;
            _canvas.style.minHeight       = 240;
            _canvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f));
            _canvas.style.marginBottom    = 4;
            _canvas.generateVisualContent += OnGenerateVisualContent;
            _canvas.RegisterCallback<WheelEvent>(OnCanvasWheel);
            _canvas.RegisterCallback<MouseDownEvent>(OnCanvasMouseDown);
            _canvas.RegisterCallback<MouseMoveEvent>(OnCanvasMouseMove);
            _canvas.RegisterCallback<MouseUpEvent>(OnCanvasMouseUp);
            _canvas.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (_hovered.HasValue) { _hovered = null; _canvas.MarkDirtyRepaint(); }
            });
            _root.Add(_canvas);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            MkBtn("フィット",      btnRow, FitToUVBounds);
            MkBtn("全選択",        btnRow, SelectAll);
            MkBtn("選択解除",      btnRow, ClearSelection);
            _root.Add(btnRow);

            // 変換セクション
            _transformSection = new VisualElement();
            _root.Add(_transformSection);

            _transformSection.Add(SecLabel("UV変換"));

            _transformSection.Add(FR2("移動 U", "V", 0f, 0f,   out _moveU,    out _moveV));
            _transformSection.Add(FR2("スケール U", "V", 1f, 1f, out _scaleU,  out _scaleV));
            _transformSection.Add(FR1("回転 (°)",  0f,          out _rotateDeg));

            var applyRow = new VisualElement();
            applyRow.style.flexDirection = FlexDirection.Row;
            applyRow.style.marginTop     = 4;
            MkBtn("変換適用",  applyRow, ApplyTransform);
            MkBtn("パラメータリセット", applyRow, ResetParams);
            _transformSection.Add(applyRow);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.gray);
            _statusLabel.style.marginTop = 4;
            _root.Add(_statusLabel);
        }

        // ================================================================
        // 外部から呼ぶ更新
        // ================================================================

        public void Refresh()
        {
            var mo = GetMeshObject();
            bool hasMesh = mo != null;

            if (_warningLabel != null)
            {
                _warningLabel.text = hasMesh ? "" : "メッシュが未選択です";
                _warningLabel.style.display =
                    hasMesh ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_transformSection != null)
                _transformSection.style.display = hasMesh ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateInfo(mo);
            _canvas?.MarkDirtyRepaint();
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

            if (evt.button == 0)
            {
                _mouseDownPos = evt.localMousePosition;
                _hitUV        = HitTestUVVertex(evt.localMousePosition);

                if (_hitUV.HasValue)
                {
                    if (!_selected.Contains(_hitUV.Value))
                    {
                        _selected.Clear();
                        _selected.Add(_hitUV.Value);
                        UpdateInfo(GetMeshObject());
                        _canvas.MarkDirtyRepaint();
                    }
                    _interaction = Interaction.PendingAction;
                }
                else
                {
                    if (_selected.Count > 0) { _selected.Clear(); UpdateInfo(GetMeshObject()); _canvas.MarkDirtyRepaint(); }
                    _interaction = Interaction.Idle;
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

                case Interaction.Idle:
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
            _interaction = Interaction.Idle;
            if (_canvas.HasMouseCapture()) _canvas.ReleaseMouse();
            evt.StopPropagation();
        }

        // ================================================================
        // UV移動
        // ================================================================

        private void BeginUVMove()
        {
            _dragStartUVs.Clear();
            var mo = GetMeshObject();
            if (mo == null) return;
            foreach (var id in _selected)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    _dragStartUVs[id] = v.UVs[id.UVIndex];
            }
            _interaction = Interaction.MovingVertex;
        }

        private void ApplyUVMove(Vector2 currentPos)
        {
            var mo = GetMeshObject();
            if (mo == null || _dragStartUVs.Count == 0) return;
            var rect    = _canvas.contentRect;
            Vector2 d   = CanvasToUV(currentPos, rect) - CanvasToUV(_mouseDownPos, rect);
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    v.UVs[id.UVIndex] = kv.Value + d;
            }
        }

        private void EndUVMove()
        {
            var mo = GetMeshObject();
            if (mo == null || _dragStartUVs.Count == 0) { _dragStartUVs.Clear(); return; }

            bool moved = false;
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count &&
                    Vector2.Distance(v.UVs[id.UVIndex], kv.Value) > 0.0001f)
                { moved = true; break; }
            }

            if (!moved) { _dragStartUVs.Clear(); return; }

            var movedUVs = new Dictionary<UVVertexId, Vector2>();
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    movedUVs[id] = v.UVs[id.UVIndex];
            }

            // 元に戻してからUndo記録
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    v.UVs[id.UVIndex] = kv.Value;
            }

            RecordTopologyChange($"UV Move {movedUVs.Count}V", obj =>
            {
                foreach (var kv in movedUVs)
                {
                    var id = kv.Key;
                    if (id.VertexIndex < 0 || id.VertexIndex >= obj.VertexCount) continue;
                    var v = obj.Vertices[id.VertexIndex];
                    if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                        v.UVs[id.UVIndex] = kv.Value;
                }
            });

            SetStatus($"UV {movedUVs.Count}頂点を移動");
            _dragStartUVs.Clear();
        }

        // ================================================================
        // Fit / 選択操作
        // ================================================================

        private void FitToUVBounds()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 uvMax = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    var v = mo.Vertices[vi];
                    if (ui < 0 || ui >= v.UVs.Count) continue;
                    uvMin = Vector2.Min(uvMin, v.UVs[ui]);
                    uvMax = Vector2.Max(uvMax, v.UVs[ui]);
                    any   = true;
                }
            }
            if (!any) { _zoom = 1f; _panOffset = Vector2.zero; _canvas.MarkDirtyRepaint(); return; }

            var rect = _canvas.contentRect;
            if (rect.width < 1) return;
            Vector2 ctr  = (uvMin + uvMax) * 0.5f;
            float ext    = Mathf.Max((uvMax - uvMin).x, (uvMax - uvMin).y);
            if (ext < 0.0001f) ext = 1f;
            float csize  = Mathf.Min(rect.width, rect.height);
            _zoom        = Mathf.Clamp((csize * 0.9f) / (csize * ext), MinZoom, MaxZoom);
            float sz     = csize * _zoom;
            _panOffset   = new Vector2(-(ctr.x - 0.5f) * sz, (ctr.y - 0.5f) * sz);
            _canvas.MarkDirtyRepaint();
        }

        private void SelectAll()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    _selected.Add(new UVVertexId(vi, ui));
                }
            }
            UpdateInfo(mo); _canvas.MarkDirtyRepaint();
        }

        private void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            UpdateInfo(GetMeshObject()); _canvas.MarkDirtyRepaint();
        }

        // ================================================================
        // 一括変換
        // ================================================================

        private void ApplyTransform()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            float mu  = _moveU?.value    ?? 0f;
            float mv  = _moveV?.value    ?? 0f;
            float su  = _scaleU?.value   ?? 1f;
            float sv  = _scaleV?.value   ?? 1f;
            float deg = _rotateDeg?.value ?? 0f;

            if (Mathf.Approximately(mu, 0f) && Mathf.Approximately(mv, 0f) &&
                Mathf.Approximately(su, 1f) && Mathf.Approximately(sv, 1f) &&
                Mathf.Approximately(deg, 0f)) { SetStatus("変換パラメータが初期値です"); return; }

            var targets = _selected.Count > 0 ? new HashSet<UVVertexId>(_selected) : CollectAllUVVertices(mo);
            var pivot   = ComputeUVPivot(mo, targets);

            RecordTopologyChange("UV Transform", obj =>
                ApplyUVTransform(obj, targets, mu, mv, su, sv, deg, pivot));

            SetStatus("UV変換を適用しました");
            _canvas.MarkDirtyRepaint();
        }

        private void ResetParams()
        {
            if (_moveU    != null) _moveU.value     = 0f;
            if (_moveV    != null) _moveV.value     = 0f;
            if (_scaleU   != null) _scaleU.value    = 1f;
            if (_scaleV   != null) _scaleV.value    = 1f;
            if (_rotateDeg!= null) _rotateDeg.value = 0f;
        }

        private static HashSet<UVVertexId> CollectAllUVVertices(MeshObject mo)
        {
            var r = new HashSet<UVVertexId>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    r.Add(new UVVertexId(vi, ui));
                }
            }
            return r;
        }

        private static Vector2 ComputeUVPivot(MeshObject mo, HashSet<UVVertexId> targets)
        {
            Vector2 sum = Vector2.zero; int cnt = 0;
            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                { sum += v.UVs[id.UVIndex]; cnt++; }
            }
            return cnt > 0 ? sum / cnt : new Vector2(0.5f, 0.5f);
        }

        private static void ApplyUVTransform(MeshObject mo, HashSet<UVVertexId> targets,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot)
        {
            float rad = deg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                Vector2 uv = v.UVs[id.UVIndex];
                uv.x = pivot.x + (uv.x - pivot.x) * su;
                uv.y = pivot.y + (uv.y - pivot.y) * sv;
                if (!Mathf.Approximately(deg, 0f))
                {
                    float dx = uv.x - pivot.x, dy = uv.y - pivot.y;
                    uv.x = pivot.x + dx * cos - dy * sin;
                    uv.y = pivot.y + dx * sin + dy * cos;
                }
                uv.x += mu; uv.y += mv;
                v.UVs[id.UVIndex] = uv;
            }
        }

        // ================================================================
        // Undo
        // ================================================================

        private void RecordTopologyChange(string opName, Action<MeshObject> action)
        {
            var model = GetModel?.Invoke();
            var mc    = model?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null) return;

            var undo   = GetUndoController?.Invoke();
            var before = undo?.CaptureMeshObjectSnapshot();

            action(mc.MeshObject);

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                GetCommandQueue?.Invoke()?.Enqueue(
                    new RecordTopologyChangeCommand(undo, before, after, opName));
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private MeshObject GetMeshObject() =>
            GetModel?.Invoke()?.FirstSelectedMeshContext?.MeshObject;

        private void UpdateInfo(MeshObject mo)
        {
            if (_infoLabel == null) return;
            if (mo == null) { _infoLabel.text = ""; return; }
            int uvCnt = 0;
            foreach (var v in mo.Vertices) uvCnt += v.UVs.Count;
            string sel = _selected.Count > 0 ? $"  Sel:{_selected.Count}" : "";
            _infoLabel.text = $"V:{mo.VertexCount} F:{mo.FaceCount} UV:{uvCnt}{sel}";
        }

        private void SetStatus(string text) { if (_statusLabel != null) _statusLabel.text = text; }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 2;
            return l;
        }

        private static void MkBtn(string text, VisualElement row, Action click)
        {
            var b = new Button(click) { text = text };
            b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 10;
            row.Add(b);
        }

        private static VisualElement FR2(string l1, string l2, float v1, float v2,
            out FloatField f1, out FloatField f2)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(l1.Split(' ')[0] + ":"); lb.style.width = 46; lb.style.fontSize = 10;
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(lb);
            f1 = new FloatField(l1.Split(' ').Length > 1 ? l1.Split(' ')[1] : "") { value = v1 };
            f1.style.flexGrow = 1;
            f2 = new FloatField(l2) { value = v2 };
            f2.style.flexGrow = 1;
            row.Add(f1); row.Add(f2);
            return row;
        }

        private static VisualElement FR1(string label, float val, out FloatField field)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(label); lb.style.width = 60; lb.style.fontSize = 10;
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            field = new FloatField { value = val };
            field.style.flexGrow = 1;
            row.Add(lb); row.Add(field);
            return row;
        }
    }
}
