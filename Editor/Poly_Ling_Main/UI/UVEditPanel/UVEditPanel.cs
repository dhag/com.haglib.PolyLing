// Assets/Editor/Poly_Ling_Main/UI/UVEditPanel/UVEditPanel.cs
// UV編集パネル（UIToolkit）
// 既存UVの2D表示・頂点選択移動・一括変換操作

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;

namespace Poly_Ling.UI
{
    /// <summary>
    /// UVEditPanel設定
    /// </summary>
    [Serializable]
    public class UVEditPanelSettings : IToolSettings
    {
        public float MoveU = 0f;
        public float MoveV = 0f;
        public float ScaleU = 1f;
        public float ScaleV = 1f;
        public float RotateDeg = 0f;

        public IToolSettings Clone()
        {
            return new UVEditPanelSettings
            {
                MoveU = this.MoveU,
                MoveV = this.MoveV,
                ScaleU = this.ScaleU,
                ScaleV = this.ScaleV,
                RotateDeg = this.RotateDeg,
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not UVEditPanelSettings o) return true;
            return !Mathf.Approximately(MoveU, o.MoveU) ||
                   !Mathf.Approximately(MoveV, o.MoveV) ||
                   !Mathf.Approximately(ScaleU, o.ScaleU) ||
                   !Mathf.Approximately(ScaleV, o.ScaleV) ||
                   !Mathf.Approximately(RotateDeg, o.RotateDeg);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not UVEditPanelSettings o) return;
            MoveU = o.MoveU;
            MoveV = o.MoveV;
            ScaleU = o.ScaleU;
            ScaleV = o.ScaleV;
            RotateDeg = o.RotateDeg;
        }
    }

    // ================================================================
    // UV頂点識別子
    // ================================================================

    /// <summary>
    /// UV頂点の識別子（頂点インデックス + UVサブインデックス）
    /// </summary>
    public readonly struct UVVertexId : IEquatable<UVVertexId>
    {
        public readonly int VertexIndex;
        public readonly int UVIndex;

        public UVVertexId(int vertexIndex, int uvIndex)
        {
            VertexIndex = vertexIndex;
            UVIndex = uvIndex;
        }

        public bool Equals(UVVertexId other) =>
            VertexIndex == other.VertexIndex && UVIndex == other.UVIndex;

        public override bool Equals(object obj) =>
            obj is UVVertexId other && Equals(other);

        public override int GetHashCode() =>
            (VertexIndex << 16) ^ UVIndex;

        public static bool operator ==(UVVertexId a, UVVertexId b) => a.Equals(b);
        public static bool operator !=(UVVertexId a, UVVertexId b) => !a.Equals(b);

        public override string ToString() => $"V{VertexIndex}:UV{UVIndex}";
    }

    /// <summary>
    /// UV編集パネル
    /// 選択メッシュの既存UVを2Dビューで表示・頂点選択移動・一括変換
    /// </summary>
    public class UVEditPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "UVEditPanel";
        public override string Title => "UV Edit";

        private UVEditPanelSettings _settings = new UVEditPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => "UVエディタ";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVEditPanel/UVEditPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVEditPanel/UVEditPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVEditPanel/UVEditPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVEditPanel/UVEditPanel.uss";

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _infoLabel, _statusLabel;
        private VisualElement _canvas;
        private VisualElement _transformSection;
        private FloatField _moveU, _moveV, _scaleU, _scaleV, _rotateDeg;

        // ================================================================
        // キャンバスナビゲーション
        // ================================================================

        private Vector2 _panOffset = Vector2.zero;
        private float _zoom = 1f;

        // ================================================================
        // マウス操作状態
        // ================================================================

        private enum InteractionState
        {
            Idle,
            Panning,           // 中ボタン/Alt+左 パン
            PendingAction,     // 左クリック直後（ドラッグ閾値待ち）
            MovingVertex       // UV頂点ドラッグ移動中
        }
        private InteractionState _state = InteractionState.Idle;

        private Vector2 _mouseDownPos;
        private Vector2 _panStartOffset;

        /// <summary>ドラッグ開始閾値（ピクセル）- MoveToolと同値</summary>
        private const float DragThreshold = 4f;

        // ================================================================
        // UV頂点選択
        // ================================================================

        private HashSet<UVVertexId> _selectedUVVertices = new HashSet<UVVertexId>();
        private const float HitRadius = 8f;
        private UVVertexId? _hitUVVertex;

        // ================================================================
        // ドラッグ移動
        // ================================================================

        /// <summary>ドラッグ開始時のUV座標バックアップ</summary>
        private Dictionary<UVVertexId, Vector2> _dragStartUVs = new Dictionary<UVVertexId, Vector2>();

        // ================================================================
        // ホバー
        // ================================================================

        private UVVertexId? _hoveredUVVertex;

        // ================================================================
        // 描画定数
        // ================================================================

        private static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color GridBorderColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color WireColor = new Color(0.6f, 0.8f, 1.0f, 0.8f);
        private static readonly Color WireSelectedColor = new Color(1.0f, 0.5f, 0.2f, 1.0f);
        private static readonly Color VertexColor = new Color(0.4f, 0.7f, 1.0f, 1.0f);
        private static readonly Color VertexSelectedColor = new Color(1.0f, 0.3f, 0.1f, 1.0f);
        private static readonly Color VertexHoverColor = new Color(1.0f, 0.8f, 0.2f, 1.0f);

        private const float VertexDotRadius = 2.5f;
        private const float VertexSelectedDotRadius = 4f;
        private const float MinZoom = 0.1f;
        private const float MaxZoom = 20f;
        private const float ZoomSpeed = 0.1f;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<UVEditPanel>();
            panel.titleContent = new GUIContent("UVエディタ");
            panel.minSize = new Vector2(320, 400);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _infoLabel = root.Q<Label>("info-label");
            _statusLabel = root.Q<Label>("status-label");
            _transformSection = root.Q<VisualElement>("transform-section");

            _moveU = root.Q<FloatField>("move-u");
            _moveV = root.Q<FloatField>("move-v");
            _scaleU = root.Q<FloatField>("scale-u");
            _scaleV = root.Q<FloatField>("scale-v");
            _rotateDeg = root.Q<FloatField>("rotate-deg");

            // キャンバス
            _canvas = root.Q<VisualElement>("uv-canvas");
            if (_canvas != null)
            {
                _canvas.generateVisualContent += OnGenerateVisualContent;
                _canvas.RegisterCallback<WheelEvent>(OnCanvasWheel);
                _canvas.RegisterCallback<MouseDownEvent>(OnCanvasMouseDown);
                _canvas.RegisterCallback<MouseMoveEvent>(OnCanvasMouseMove);
                _canvas.RegisterCallback<MouseUpEvent>(OnCanvasMouseUp);
                _canvas.RegisterCallback<MouseLeaveEvent>(OnCanvasMouseLeave);
            }

            // ボタン
            root.Q<Button>("btn-fit")?.RegisterCallback<ClickEvent>(_ => FitToUVBounds());
            root.Q<Button>("btn-apply")?.RegisterCallback<ClickEvent>(_ => ApplyTransform());
            root.Q<Button>("btn-reset-params")?.RegisterCallback<ClickEvent>(_ => ResetParams());

            RefreshAll();
        }

        // ================================================================
        // コンテキスト変更
        // ================================================================

        protected override void OnContextSet()
        {
            _selectedUVVertices.Clear();
            RefreshAll();
        }

        protected override void OnUndoRedoPerformed()
        {
            base.OnUndoRedoPerformed();
            RefreshAll();
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            if (_canvas == null) return;

            var meshObj = FirstSelectedMeshObject;

            if (_warningLabel != null)
            {
                if (_context == null)
                {
                    _warningLabel.text = "コンテキスト未設定";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else if (meshObj == null)
                {
                    _warningLabel.text = "メッシュが選択されていません";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _warningLabel.style.display = DisplayStyle.None;
                }
            }

            if (_transformSection != null)
                _transformSection.style.display = meshObj != null ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateInfo();
            _canvas?.MarkDirtyRepaint();
        }

        private void UpdateInfo()
        {
            if (_infoLabel == null) return;

            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null)
            {
                _infoLabel.text = "";
                return;
            }

            int vertCount = meshObj.VertexCount;
            int faceCount = meshObj.FaceCount;
            int uvCount = 0;
            foreach (var v in meshObj.Vertices)
                uvCount += v.UVs.Count;

            string meshName = FirstSelectedMeshContext?.Name ?? "?";
            string selInfo = _selectedUVVertices.Count > 0
                ? $"  Sel:{_selectedUVVertices.Count}"
                : "";
            _infoLabel.text = $"{meshName}  V:{vertCount} F:{faceCount} UV:{uvCount}{selInfo}";
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private Vector2 UVToCanvas(Vector2 uv, Rect rect)
        {
            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            float cx = rect.width * 0.5f + _panOffset.x;
            float cy = rect.height * 0.5f + _panOffset.y;

            float x = cx + (uv.x - 0.5f) * size;
            float y = cy - (uv.y - 0.5f) * size;
            return new Vector2(x, y);
        }

        private Vector2 CanvasToUV(Vector2 pixel, Rect rect)
        {
            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            if (size < 0.001f) return new Vector2(0.5f, 0.5f);

            float cx = rect.width * 0.5f + _panOffset.x;
            float cy = rect.height * 0.5f + _panOffset.y;

            float u = (pixel.x - cx) / size + 0.5f;
            float v = -(pixel.y - cy) / size + 0.5f;
            return new Vector2(u, v);
        }

        // ================================================================
        // UV頂点ヒットテスト
        // ================================================================

        private UVVertexId? HitTestUVVertex(Vector2 canvasMousePos)
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null) return null;

            var rect = _canvas.contentRect;
            if (rect.width < 1 || rect.height < 1) return null;

            float bestDistSq = HitRadius * HitRadius;
            UVVertexId? bestHit = null;
            var tested = new HashSet<long>();

            foreach (var face in meshObj.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= meshObj.VertexCount) continue;

                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    long key = ((long)vi << 32) | (uint)uvIdx;
                    if (!tested.Add(key)) continue;

                    var vertex = meshObj.Vertices[vi];
                    if (uvIdx < 0 || uvIdx >= vertex.UVs.Count) continue;

                    Vector2 canvasPos = UVToCanvas(vertex.UVs[uvIdx], rect);
                    float distSq = (canvasPos - canvasMousePos).sqrMagnitude;

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestHit = new UVVertexId(vi, uvIdx);
                    }
                }
            }

            return bestHit;
        }

        // ================================================================
        // キャンバス描画
        // ================================================================

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            var rect = _canvas.contentRect;
            if (rect.width < 1 || rect.height < 1) return;

            DrawGrid(painter, rect);
            DrawUVWireframe(painter, rect);
        }

        private void DrawGrid(Painter2D painter, Rect rect)
        {
            painter.strokeColor = GridColor;
            painter.lineWidth = 1f;
            for (int i = 1; i <= 3; i++)
            {
                float t = i * 0.25f;

                var a = UVToCanvas(new Vector2(0, t), rect);
                var b = UVToCanvas(new Vector2(1, t), rect);
                painter.BeginPath();
                painter.MoveTo(a);
                painter.LineTo(b);
                painter.Stroke();

                a = UVToCanvas(new Vector2(t, 0), rect);
                b = UVToCanvas(new Vector2(t, 1), rect);
                painter.BeginPath();
                painter.MoveTo(a);
                painter.LineTo(b);
                painter.Stroke();
            }

            var p00 = UVToCanvas(new Vector2(0, 0), rect);
            var p10 = UVToCanvas(new Vector2(1, 0), rect);
            var p11 = UVToCanvas(new Vector2(1, 1), rect);
            var p01 = UVToCanvas(new Vector2(0, 1), rect);

            painter.strokeColor = GridBorderColor;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(p00);
            painter.LineTo(p10);
            painter.LineTo(p11);
            painter.LineTo(p01);
            painter.ClosePath();
            painter.Stroke();
        }

        private void DrawUVWireframe(Painter2D painter, Rect rect)
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null) return;

            var vertices = meshObj.Vertices;
            var faces = meshObj.Faces;
            if (vertices == null || faces == null) return;

            // ---- 面ワイヤフレーム ----
            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                if (face == null || face.VertexCount < 3) continue;

                bool faceHasSelection = false;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    if (_selectedUVVertices.Contains(new UVVertexId(vi, uvIdx)))
                    {
                        faceHasSelection = true;
                        break;
                    }
                }

                painter.strokeColor = faceHasSelection ? WireSelectedColor : WireColor;
                painter.lineWidth = faceHasSelection ? 1.5f : 1f;

                painter.BeginPath();
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= vertices.Count) continue;

                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    var vertex = vertices[vi];
                    Vector2 uv = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                        ? vertex.UVs[uvIdx]
                        : Vector2.zero;

                    var pos = UVToCanvas(uv, rect);
                    if (ci == 0)
                        painter.MoveTo(pos);
                    else
                        painter.LineTo(pos);
                }
                painter.ClosePath();
                painter.Stroke();
            }

            // ---- 頂点ドット ----
            var drawnUVs = new HashSet<long>();

            for (int fi = 0; fi < faces.Count; fi++)
            {
                var face = faces[fi];
                if (face == null) continue;

                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= vertices.Count) continue;

                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    long key = ((long)vi << 32) | (uint)uvIdx;
                    if (!drawnUVs.Add(key)) continue;

                    var vertex = vertices[vi];
                    Vector2 uv = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                        ? vertex.UVs[uvIdx]
                        : Vector2.zero;

                    var id = new UVVertexId(vi, uvIdx);
                    bool isSelected = _selectedUVVertices.Contains(id);
                    bool isHovered = _hoveredUVVertex.HasValue && _hoveredUVVertex.Value == id;

                    Color color;
                    float radius;
                    if (isSelected)
                    {
                        color = VertexSelectedColor;
                        radius = VertexSelectedDotRadius;
                    }
                    else if (isHovered)
                    {
                        color = VertexHoverColor;
                        radius = VertexSelectedDotRadius;
                    }
                    else
                    {
                        color = VertexColor;
                        radius = VertexDotRadius;
                    }

                    var pos = UVToCanvas(uv, rect);
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.Arc(pos, radius, 0f, 360f);
                    painter.Fill();
                }
            }
        }

        // ================================================================
        // キャンバス入力: ズーム
        // ================================================================

        private void OnCanvasWheel(WheelEvent evt)
        {
            var rect = _canvas.contentRect;
            var mousePos = evt.localMousePosition;

            var uvBefore = CanvasToUV(mousePos, rect);

            float delta = -evt.delta.y * ZoomSpeed;
            _zoom = Mathf.Clamp(_zoom * (1f + delta), MinZoom, MaxZoom);

            var posAfter = UVToCanvas(uvBefore, rect);
            _panOffset += mousePos - posAfter;

            _canvas.MarkDirtyRepaint();
            evt.StopPropagation();
        }

        // ================================================================
        // キャンバス入力: マウスダウン
        // ================================================================

        private void OnCanvasMouseDown(MouseDownEvent evt)
        {
            // ---- パン: 中ボタン or Alt+左 ----
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _state = InteractionState.Panning;
                _mouseDownPos = evt.localMousePosition;
                _panStartOffset = _panOffset;
                _canvas.CaptureMouse();
                evt.StopPropagation();
                return;
            }

            // ---- 左ボタン: 選択 / 移動準備 ----
            if (evt.button == 0)
            {
                _mouseDownPos = evt.localMousePosition;
                _hitUVVertex = HitTestUVVertex(evt.localMousePosition);

                if (_hitUVVertex.HasValue)
                {
                    var hit = _hitUVVertex.Value;

                    if (!_selectedUVVertices.Contains(hit))
                    {
                        // 未選択頂点をクリック → 即座に単一選択
                        _selectedUVVertices.Clear();
                        _selectedUVVertices.Add(hit);
                        UpdateInfo();
                        _canvas.MarkDirtyRepaint();
                    }
                    // 既に選択中のものは → ドラッグ移動に備えてそのまま

                    _state = InteractionState.PendingAction;
                }
                else
                {
                    // 空クリック → 選択解除
                    if (_selectedUVVertices.Count > 0)
                    {
                        _selectedUVVertices.Clear();
                        UpdateInfo();
                        _canvas.MarkDirtyRepaint();
                    }
                    _state = InteractionState.Idle;
                }

                _canvas.CaptureMouse();
                evt.StopPropagation();
            }
        }

        // ================================================================
        // キャンバス入力: マウスムーブ
        // ================================================================

        private void OnCanvasMouseMove(MouseMoveEvent evt)
        {
            var mousePos = evt.localMousePosition;

            switch (_state)
            {
                case InteractionState.Panning:
                    _panOffset = _panStartOffset + (mousePos - _mouseDownPos);
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case InteractionState.PendingAction:
                    float dragDist = Vector2.Distance(mousePos, _mouseDownPos);
                    if (dragDist > DragThreshold)
                    {
                        BeginUVMove();
                        ApplyUVMove(mousePos);
                        _canvas.MarkDirtyRepaint();
                    }
                    evt.StopPropagation();
                    return;

                case InteractionState.MovingVertex:
                    ApplyUVMove(mousePos);
                    _canvas.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;

                case InteractionState.Idle:
                    // ホバー更新
                    var newHover = HitTestUVVertex(mousePos);
                    bool changed = (newHover.HasValue != _hoveredUVVertex.HasValue) ||
                                   (newHover.HasValue && _hoveredUVVertex.HasValue &&
                                    newHover.Value != _hoveredUVVertex.Value);
                    if (changed)
                    {
                        _hoveredUVVertex = newHover;
                        _canvas.MarkDirtyRepaint();
                    }
                    return;
            }
        }

        // ================================================================
        // キャンバス入力: マウスアップ
        // ================================================================

        private void OnCanvasMouseUp(MouseUpEvent evt)
        {
            switch (_state)
            {
                case InteractionState.Panning:
                    break;

                case InteractionState.PendingAction:
                    // ドラッグ閾値に達しなかった → クリック確定（選択は既にMouseDownで処理済み）
                    break;

                case InteractionState.MovingVertex:
                    EndUVMove();
                    break;
            }

            _state = InteractionState.Idle;

            if (_canvas.HasMouseCapture())
                _canvas.ReleaseMouse();

            evt.StopPropagation();
        }

        private void OnCanvasMouseLeave(MouseLeaveEvent evt)
        {
            if (_hoveredUVVertex.HasValue)
            {
                _hoveredUVVertex = null;
                _canvas.MarkDirtyRepaint();
            }
        }

        // ================================================================
        // UV頂点移動
        // ================================================================

        /// <summary>
        /// ドラッグ移動開始: 選択中UV頂点の座標をバックアップ
        /// </summary>
        private void BeginUVMove()
        {
            _dragStartUVs.Clear();
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null) return;

            foreach (var id in _selectedUVVertices)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;

                _dragStartUVs[id] = vertex.UVs[id.UVIndex];
            }

            _state = InteractionState.MovingVertex;
        }

        /// <summary>
        /// ドラッグ中: マウスダウン位置からのUVデルタを全選択頂点に適用
        /// </summary>
        private void ApplyUVMove(Vector2 currentCanvasPos)
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null || _dragStartUVs.Count == 0) return;

            var rect = _canvas.contentRect;

            Vector2 uvStart = CanvasToUV(_mouseDownPos, rect);
            Vector2 uvCurrent = CanvasToUV(currentCanvasPos, rect);
            Vector2 uvDelta = uvCurrent - uvStart;

            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                Vector2 originalUV = kv.Value;

                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;

                vertex.UVs[id.UVIndex] = originalUV + uvDelta;
            }
        }

        /// <summary>
        /// ドラッグ終了: Undo記録
        /// </summary>
        private void EndUVMove()
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null || _dragStartUVs.Count == 0)
            {
                _dragStartUVs.Clear();
                return;
            }

            // 移動量チェック
            bool hasMoved = false;
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;

                if (Vector2.Distance(vertex.UVs[id.UVIndex], kv.Value) > 0.0001f)
                {
                    hasMoved = true;
                    break;
                }
            }

            if (!hasMoved)
            {
                _dragStartUVs.Clear();
                return;
            }

            // 現在値を退避
            var movedUVs = new Dictionary<UVVertexId, Vector2>();
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;
                movedUVs[id] = vertex.UVs[id.UVIndex];
            }

            // 元に戻してからRecordTopologyChangeで記録
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;
                vertex.UVs[id.UVIndex] = kv.Value;
            }

            RecordTopologyChange($"UV Move {movedUVs.Count} Vertices", (obj) =>
            {
                foreach (var kv in movedUVs)
                {
                    var id = kv.Key;
                    if (id.VertexIndex < 0 || id.VertexIndex >= obj.VertexCount) continue;
                    var vertex = obj.Vertices[id.VertexIndex];
                    if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;
                    vertex.UVs[id.UVIndex] = kv.Value;
                }
            });

            SetStatus($"UV {movedUVs.Count}頂点を移動");
            _dragStartUVs.Clear();
        }

        // ================================================================
        // Fit
        // ================================================================

        private void FitToUVBounds()
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null) return;

            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 uvMax = new Vector2(float.MinValue, float.MinValue);
            bool hasAnyUV = false;

            foreach (var face in meshObj.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= meshObj.VertexCount) continue;

                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    var vertex = meshObj.Vertices[vi];
                    if (uvIdx < 0 || uvIdx >= vertex.UVs.Count) continue;

                    Vector2 uv = vertex.UVs[uvIdx];
                    uvMin = Vector2.Min(uvMin, uv);
                    uvMax = Vector2.Max(uvMax, uv);
                    hasAnyUV = true;
                }
            }

            if (!hasAnyUV)
            {
                _zoom = 1f;
                _panOffset = Vector2.zero;
                _canvas?.MarkDirtyRepaint();
                return;
            }

            var rect = _canvas.contentRect;
            if (rect.width < 1 || rect.height < 1) return;

            Vector2 uvCenter = (uvMin + uvMax) * 0.5f;
            Vector2 uvSize = uvMax - uvMin;
            float maxExtent = Mathf.Max(uvSize.x, uvSize.y);
            if (maxExtent < 0.0001f) maxExtent = 1f;

            float canvasSize = Mathf.Min(rect.width, rect.height);
            _zoom = (canvasSize * 0.9f) / (canvasSize * maxExtent);
            _zoom = Mathf.Clamp(_zoom, MinZoom, MaxZoom);

            float size = canvasSize * _zoom;
            _panOffset.x = -(uvCenter.x - 0.5f) * size;
            _panOffset.y = (uvCenter.y - 0.5f) * size;

            _canvas.MarkDirtyRepaint();
        }

        // ================================================================
        // 一括変換
        // ================================================================

        private void ApplyTransform()
        {
            if (_context == null || FirstSelectedMeshObject == null) return;

            float mu = _moveU?.value ?? 0f;
            float mv = _moveV?.value ?? 0f;
            float su = _scaleU?.value ?? 1f;
            float sv = _scaleV?.value ?? 1f;
            float deg = _rotateDeg?.value ?? 0f;

            if (Mathf.Approximately(mu, 0f) && Mathf.Approximately(mv, 0f) &&
                Mathf.Approximately(su, 1f) && Mathf.Approximately(sv, 1f) &&
                Mathf.Approximately(deg, 0f))
            {
                SetStatus("変換パラメータが初期値です");
                return;
            }

            var meshObj = FirstSelectedMeshObject;
            HashSet<UVVertexId> targets = _selectedUVVertices.Count > 0
                ? _selectedUVVertices
                : CollectAllUVVertices(meshObj);

            Vector2 pivot = ComputeUVPivot(meshObj, targets);

            RecordTopologyChange("UV Transform", (obj) =>
            {
                ApplyUVTransform(obj, targets, mu, mv, su, sv, deg, pivot);
            });

            SetStatus("UV変換を適用しました");
            RefreshAll();
        }

        private HashSet<UVVertexId> CollectAllUVVertices(MeshObject meshObj)
        {
            var result = new HashSet<UVVertexId>();
            foreach (var face in meshObj.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= meshObj.VertexCount) continue;
                    int uvIdx = (ci < face.UVIndices.Count) ? face.UVIndices[ci] : 0;
                    result.Add(new UVVertexId(vi, uvIdx));
                }
            }
            return result;
        }

        private Vector2 ComputeUVPivot(MeshObject meshObj, HashSet<UVVertexId> targets)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;

            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;

                sum += vertex.UVs[id.UVIndex];
                count++;
            }

            return count > 0 ? sum / count : new Vector2(0.5f, 0.5f);
        }

        private void ApplyUVTransform(MeshObject meshObj, HashSet<UVVertexId> targets,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot)
        {
            float rad = deg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= meshObj.VertexCount) continue;
                var vertex = meshObj.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vertex.UVs.Count) continue;

                Vector2 uv = vertex.UVs[id.UVIndex];

                uv.x = pivot.x + (uv.x - pivot.x) * su;
                uv.y = pivot.y + (uv.y - pivot.y) * sv;

                if (!Mathf.Approximately(deg, 0f))
                {
                    float dx = uv.x - pivot.x;
                    float dy = uv.y - pivot.y;
                    uv.x = pivot.x + dx * cos - dy * sin;
                    uv.y = pivot.y + dx * sin + dy * cos;
                }

                uv.x += mu;
                uv.y += mv;

                vertex.UVs[id.UVIndex] = uv;
            }
        }

        // ================================================================
        // パラメータリセット
        // ================================================================

        private void ResetParams()
        {
            if (_moveU != null) _moveU.value = 0f;
            if (_moveV != null) _moveV.value = 0f;
            if (_scaleU != null) _scaleU.value = 1f;
            if (_scaleV != null) _scaleV.value = 1f;
            if (_rotateDeg != null) _rotateDeg.value = 0f;
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text;
        }

        // ================================================================
        // 外部からの再描画要求
        // ================================================================

        public void Refresh()
        {
            RefreshAll();
        }

        // ================================================================
        // Update（選択メッシュ変更の検知）
        // ================================================================

        private int _lastMeshIndex = -1;

        private void Update()
        {
            if (_context == null || _canvas == null) return;

            int currentMeshIndex = Model?.FirstSelectedIndex ?? -1;
            if (currentMeshIndex != _lastMeshIndex)
            {
                _lastMeshIndex = currentMeshIndex;
                _selectedUVVertices.Clear();
                RefreshAll();
            }
        }
    }
}
