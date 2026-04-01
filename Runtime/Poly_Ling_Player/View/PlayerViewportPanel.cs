// PlayerViewportPanel.cs
// UIToolkit VisualElement として RenderTexture を表示し、
// UIToolkit ポインターイベントを IMouseEventSource として公開する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// RenderTexture を背景に描画する UIToolkit VisualElement。
    /// UIToolkit のポインターイベントを受け取り、<see cref="IMouseEventSource"/> として公開する。
    /// <see cref="PlayerViewport"/> を所有する。
    /// </summary>
    public class PlayerViewportPanel : VisualElement, IMouseEventSource
    {
        // ================================================================
        // IMouseEventSource 実装
        // ================================================================

        public event Action<int, Vector2, ModifierKeys> OnButtonDown;
        public event Action<int, Vector2, ModifierKeys> OnButtonUp;
        public event Action<int, Vector2, ModifierKeys> OnClick;
        public event Action<int, Vector2, ModifierKeys> OnDragBegin;
        public event Action<int, Vector2, Vector2, ModifierKeys> OnDrag;
        public event Action<int, Vector2, ModifierKeys> OnDragEnd;
        public event Action<float, ModifierKeys> OnScroll;

        public bool IsAnyDragging => _state[0] == BtnState.Dragging
                                  || _state[1] == BtnState.Dragging
                                  || _state[2] == BtnState.Dragging;
        public bool IsDragging(int btn) => btn >= 0 && btn < 3 && _state[btn] == BtnState.Dragging;

        /// <summary>ポインター移動時（ドラッグ中でなくても毎フレーム通知）。(screenPos, mods)</summary>
        public event Action<Vector2, ModifierKeys> OnPointerMoved;

        /// <summary>
        /// ポインターがこのパネル（RenderTexture領域）内を移動したときに発火する。
        /// 引数は UIToolkit のパネルローカル座標（Y=0が上）のまま渡す。
        ///
        /// 【用途】
        ///   UnifiedSystemAdapter.UpdateHoverOnly(localMousePos, rect) に直接渡すため。
        ///   UpdateHoverOnly は内部でカメラパラメータを使い回すので、
        ///   UpdateFrame で設定済みのパラメータと座標系を合わせる必要がある。
        ///   UIToolkit の localPosition はパネル左上原点(Y↓)なのでそのまま渡す。
        ///
        /// 【発火条件】
        ///   UIToolkit の PointerMoveEvent はこのパネル内にポインターがある時だけ
        ///   発火する。ボタン・ラベル等の別パネル上では発火しないので、
        ///   「RenderTexture内限定」は自然に保証される。
        /// </summary>
        public event Action<Vector2> OnPointerHover;

        // ================================================================
        // 矩形選択オーバーレイ
        // ================================================================

        private readonly VisualElement _boxOverlay;

        // ================================================================
        // ブラシ円オーバーレイ（スカルプトツール用）
        // ================================================================

        private readonly VisualElement _brushCircleOverlay;

        // ================================================================
        // 詳細選択プレビューオーバーレイ
        // ================================================================

        private readonly VisualElement _advSelOverlay;
        // スクリーン座標（Y=0下）で渡し、描画時に変換する
        private List<Vector2>         _advSelPreviewPts    = new List<Vector2>();
        private List<(Vector2, Vector2)> _advSelPreviewLines = new List<(Vector2, Vector2)>();
        private bool                  _advSelAddMode;

        // ================================================================
        // ボーンワイヤフレームオーバーレイ
        // ================================================================

        private readonly VisualElement _boneOverlay;

        public struct BoneWireData
        {
            public Vector2[] ScreenPos;   // ボーン位置スクリーン座標（Y=0下）
            public bool[]    IsSelected;
        }
        private BoneWireData _boneWireData;
        private Vector2 _brushCircleCenter;
        private float   _brushCircleRadius;
        private Color   _brushCircleColor = new Color(0.5f, 0.8f, 1f, 0.7f);
        private bool    _brushCircleVisible;

        // ================================================================
        // 面ホバーオーバーレイ（generateVisualContent による多角形描画）
        // ================================================================

        // 現在ホバー中の面の頂点スクリーン座標（Y=0が下）。
        // null のとき非表示。UpdateFaceHover() で更新する。
        private UnityEngine.Vector2[] _hoverFaceScreenPts;
        private List<UnityEngine.Vector2[]> _selectedFacesScreenPts;

        private readonly VisualElement _faceOverlay;
        private readonly VisualElement _gizmoOverlay;
        private GizmoData _gizmoData;

        public struct GizmoData
        {
            public bool HasGizmo;
            public Vector2 Origin, XEnd, YEnd, ZEnd;
            public Poly_Ling.Tools.AxisGizmo.AxisType HoveredAxis;
            public Poly_Ling.Tools.AxisGizmo.AxisType DraggingAxis;
            /// <summary>オブジェクト移動用ダイヤ型スタイル。true=ダイヤ、false=矢印（頂点移動）。</summary>
            public bool IsDiamondStyle;
            /// <summary>ピボット位置のダイヤ型ギズモ。</summary>
            public bool HasPivotGizmo;
            public Vector2 PivotOrigin, PivotXEnd, PivotYEnd, PivotZEnd;
        }

        public void UpdateGizmo(GizmoData d)
        { _gizmoData = d; _gizmoOverlay.MarkDirtyRepaint(); }
        public void HideGizmo()
        { _gizmoData = default; _gizmoOverlay.MarkDirtyRepaint(); }

        /// <summary>
        /// 面ホバーオーバーレイを更新する。
        /// screenPts はビューポートスクリーン座標（Y=0が下）の頂点リスト。
        /// null を渡すと非表示になる。
        ///
        /// 【座標変換】
        ///   Camera.WorldToScreenPoint は Y=0 が下。
        ///   UIToolkit の panel local は Y=0 が上なので Y 反転して渡す。
        /// </summary>
        public void ShowFaceHover(UnityEngine.Vector2[] screenPts)
        {
            _hoverFaceScreenPts = screenPts;
            _faceOverlay.MarkDirtyRepaint();
        }

        /// <summary>面ホバーオーバーレイを非表示にする。</summary>
        public void HideFaceHover()
        {
            _hoverFaceScreenPts = null;
            _faceOverlay.MarkDirtyRepaint();
        }

        /// <summary>選択面オーバーレイを表示/更新する。</summary>
        public void ShowSelectedFaces(List<UnityEngine.Vector2[]> faces)
        {
            _selectedFacesScreenPts = faces;
            _faceOverlay.MarkDirtyRepaint();
        }

        /// <summary>選択面オーバーレイを非表示にする。</summary>
        public void HideSelectedFaces()
        {
            _selectedFacesScreenPts = null;
            _faceOverlay.MarkDirtyRepaint();
        }

        /// <summary>
        /// 矩形選択オーバーレイを表示/更新する。
        /// start/end はビューポートスクリーン座標（Y=0が下）。
        /// </summary>
        public void ShowBoxSelect(Vector2 start, Vector2 end)
        {
            float panelH = resolvedStyle.height;
            float x0 = Mathf.Min(start.x, end.x);
            float x1 = Mathf.Max(start.x, end.x);
            // スクリーン座標(Y=0が下) → panel local(Y=0が上)
            float y0 = panelH - Mathf.Max(start.y, end.y);
            float y1 = panelH - Mathf.Min(start.y, end.y);
            _boxOverlay.style.left    = x0;
            _boxOverlay.style.top     = y0;
            _boxOverlay.style.width   = x1 - x0;
            _boxOverlay.style.height  = y1 - y0;
            _boxOverlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>矩形選択オーバーレイを非表示にする。</summary>
        public void HideBoxSelect()
        {
            _boxOverlay.style.display = DisplayStyle.None;
        }

        // ================================================================
        // ブラシ円オーバーレイ（スカルプトツール用）
        // ================================================================

        /// <summary>
        /// ブラシ円を表示する。
        /// center はスクリーン座標（Y=0 が下）、radius はピクセル。
        /// </summary>
        public void ShowBrushCircle(Vector2 center, float radius)
            => ShowBrushCircle(center, radius, new Color(0.5f, 0.8f, 1f, 0.7f));

        /// <summary>
        /// ブラシ円を指定カラーで表示する。
        /// center はスクリーン座標（Y=0 が下）、radius はピクセル。
        /// </summary>
        public void ShowBrushCircle(Vector2 center, float radius, Color color)
        {
            float panelH = resolvedStyle.height;
            _brushCircleCenter  = new Vector2(center.x, panelH - center.y);
            _brushCircleRadius  = radius;
            _brushCircleColor   = color;
            _brushCircleVisible = true;
            _brushCircleOverlay?.MarkDirtyRepaint();
        }

        /// <summary>ブラシ円を非表示にする。</summary>
        public void HideBrushCircle()
        {
            _brushCircleVisible = false;
            _brushCircleOverlay?.MarkDirtyRepaint();
        }

        // ================================================================
        // 詳細選択プレビューオーバーレイ（AdvancedSelectTool 用）
        // ================================================================

        /// <summary>
        /// 詳細選択のプレビュー（頂点□・辺線）を更新する。
        /// pts: 頂点スクリーン座標リスト（Y=0下）
        /// lines: 辺スクリーン座標ペアリスト（Y=0下）
        /// addMode: true=緑(追加)、false=赤(除外)
        /// </summary>
        public void UpdateAdvSelPreview(
            List<Vector2> pts, List<(Vector2, Vector2)> lines, bool addMode)
        {
            _advSelPreviewPts   = pts   ?? new List<Vector2>();
            _advSelPreviewLines = lines ?? new List<(Vector2, Vector2)>();
            _advSelAddMode      = addMode;
            _advSelOverlay?.MarkDirtyRepaint();
        }

        /// <summary>詳細選択プレビューを非表示にする。</summary>
        public void HideAdvSelPreview()
        {
            _advSelPreviewPts.Clear();
            _advSelPreviewLines.Clear();
            _advSelOverlay?.MarkDirtyRepaint();
        }

        // ================================================================
        // ボーンワイヤフレームオーバーレイ
        // ================================================================

        /// <summary>ボーンの位置をスクリーン座標（Y=0下）で更新して描画する。</summary>
        public void UpdateBoneWire(Vector2[] screenPosYDown, bool[] isSelected)
        {
            _boneWireData = new BoneWireData { ScreenPos = screenPosYDown, IsSelected = isSelected };
            _boneOverlay?.MarkDirtyRepaint();
        }

        /// <summary>ボーンワイヤフレームを非表示にする。</summary>
        public void HideBoneWire()
        {
            _boneWireData = default;
            _boneOverlay?.MarkDirtyRepaint();
        }

        // ================================================================
        // 設定
        // ================================================================

        public float DragThreshold = 4f;

        // ================================================================
        // プロパティ
        // ================================================================

        public PlayerViewport Viewport { get; private set; }

        // ================================================================
        // 内部状態
        // ================================================================

        private enum BtnState { Idle, Pressed, Dragging }

        private readonly BtnState[] _state       = new BtnState[3];
        private readonly Vector2[]  _downPos      = new Vector2[3];
        private readonly Vector2[]  _prevDragPos  = new Vector2[3];

        // ================================================================
        // コンストラクタ
        // ================================================================

        public PlayerViewportPanel()
        {
            style.flexGrow        = 1;
            style.overflow        = Overflow.Hidden;
            style.backgroundSize  = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureLost);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // 矩形選択オーバーレイ（初期非表示）
            _boxOverlay = new VisualElement();
            _boxOverlay.style.position        = Position.Absolute;
            _boxOverlay.style.display         = DisplayStyle.None;
            _boxOverlay.style.borderTopWidth  = 1;
            _boxOverlay.style.borderLeftWidth = 1;
            _boxOverlay.style.borderRightWidth= 1;
            _boxOverlay.style.borderBottomWidth=1;
            _boxOverlay.style.borderTopColor  = new StyleColor(new Color(1f, 1f, 0.3f, 0.9f));
            _boxOverlay.style.borderLeftColor = new StyleColor(new Color(1f, 1f, 0.3f, 0.9f));
            _boxOverlay.style.borderRightColor= new StyleColor(new Color(1f, 1f, 0.3f, 0.9f));
            _boxOverlay.style.borderBottomColor=new StyleColor(new Color(1f, 1f, 0.3f, 0.9f));
            _boxOverlay.style.backgroundColor = new StyleColor(new Color(1f, 1f, 0.3f, 0.08f));
            _boxOverlay.pickingMode           = PickingMode.Ignore;
            Add(_boxOverlay);

            // 面ホバーオーバーレイ（generateVisualContent による多角形描画）
            _faceOverlay = new VisualElement();
            _faceOverlay.style.position  = Position.Absolute;
            _faceOverlay.style.left      = 0;
            _faceOverlay.style.top       = 0;
            _faceOverlay.style.right     = 0;
            _faceOverlay.style.bottom    = 0;
            _faceOverlay.pickingMode     = PickingMode.Ignore;
            _faceOverlay.generateVisualContent += OnGenerateFaceOverlay;
            Add(_faceOverlay);
            _gizmoOverlay = new VisualElement();
            _gizmoOverlay.style.position = Position.Absolute;
            _gizmoOverlay.style.left = _gizmoOverlay.style.top =
            _gizmoOverlay.style.right = _gizmoOverlay.style.bottom = 0;
            _gizmoOverlay.pickingMode = PickingMode.Ignore;
            _gizmoOverlay.generateVisualContent += OnGenerateGizmoOverlay;
            Add(_gizmoOverlay);

            // ブラシ円オーバーレイ（スカルプトツール用）
            _brushCircleOverlay = new VisualElement();
            _brushCircleOverlay.style.position = Position.Absolute;
            _brushCircleOverlay.style.left = _brushCircleOverlay.style.top =
            _brushCircleOverlay.style.right = _brushCircleOverlay.style.bottom = 0;
            _brushCircleOverlay.pickingMode = PickingMode.Ignore;
            _brushCircleOverlay.generateVisualContent += OnGenerateBrushCircle;
            Add(_brushCircleOverlay);

            // 詳細選択プレビューオーバーレイ
            _advSelOverlay = new VisualElement();
            _advSelOverlay.style.position = Position.Absolute;
            _advSelOverlay.style.left = _advSelOverlay.style.top =
            _advSelOverlay.style.right = _advSelOverlay.style.bottom = 0;
            _advSelOverlay.pickingMode = PickingMode.Ignore;
            _advSelOverlay.generateVisualContent += OnGenerateAdvSelOverlay;
            Add(_advSelOverlay);

            // ボーンワイヤフレームオーバーレイ
            _boneOverlay = new VisualElement();
            _boneOverlay.style.position = Position.Absolute;
            _boneOverlay.style.left = _boneOverlay.style.top =
            _boneOverlay.style.right = _boneOverlay.style.bottom = 0;
            _boneOverlay.pickingMode = PickingMode.Ignore;
            _boneOverlay.generateVisualContent += OnGenerateBoneOverlay;
            Add(_boneOverlay);
        }

        // ================================================================
        // Viewport 接続
        // ================================================================

        public void SetViewport(PlayerViewport viewport)
        {
            // 旧 Viewport の接続を解除
            Viewport?.DisconnectSource(this);

            Viewport = viewport;

            if (viewport != null)
            {
                // RenderTexture を背景に設定
                RefreshBackground();
                // コントローラーをこのパネルのイベントに接続
                viewport.ConnectSource(this);
            }
        }

        // ================================================================
        // RenderTexture 背景更新
        // ================================================================

        private void RefreshBackground()
        {
            if (Viewport?.RT != null)
                style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(Viewport.RT));
        }

        // ================================================================
        // ジオメトリ変更（パネルリサイズ時）
        // ================================================================

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (Viewport == null) return;
            int w = Mathf.Max(1, Mathf.RoundToInt(resolvedStyle.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(resolvedStyle.height));
            Viewport.Resize(w, h);
            RefreshBackground();
        }

        // ================================================================
        // ポインターイベント処理
        // ================================================================

        private void OnPointerDown(PointerDownEvent evt)
        {
            int btn = evt.button;
            if (btn < 0 || btn >= 3) return;

            // 全ボタンがIdle（最初のボタン押下）のときのみキャプチャ取得。
            // 既にキャプチャ済みの場合は取得不要。複数ボタン同時押しでも1回だけ呼ぶ。
            bool anyActive = _state[0] != BtnState.Idle
                          || _state[1] != BtnState.Idle
                          || _state[2] != BtnState.Idle;
            if (!anyActive)
                this.CapturePointer(evt.pointerId);

            Vector2 pos  = ToViewportCoord(evt.localPosition);
            var     mods = GetMods(evt);

            _state[btn]   = BtnState.Pressed;
            _downPos[btn] = pos;
            OnButtonDown?.Invoke(btn, pos, mods);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pos  = ToViewportCoord(evt.localPosition);
            var     mods = GetMods(evt);

            MousePosition = pos;  // CaptureLost時の座標保持

            // ホバー通知（ドラッグ中も含め常に発火）
            OnPointerMoved?.Invoke(pos, mods);

            // UpdateHoverOnly 用にパネルローカル座標(Y=0が上)をそのまま渡す。
            // ToViewportCoord() によるY反転は行わない。
            // UpdateHoverOnly が期待する座標系が UIToolkit と同じ左上原点のため。
            OnPointerHover?.Invoke(evt.localPosition);

            for (int btn = 0; btn < 3; btn++)
            {
                if (_state[btn] == BtnState.Idle) continue;

                if (_state[btn] == BtnState.Pressed)
                {
                    float moved = Vector2.Distance(pos, _downPos[btn]);
                    if (moved > DragThreshold)
                    {
                        _state[btn]       = BtnState.Dragging;
                        _prevDragPos[btn] = _downPos[btn];
                        OnDragBegin?.Invoke(btn, _downPos[btn], mods);
                    }
                }

                if (_state[btn] == BtnState.Dragging)
                {
                    Vector2 delta     = pos - _prevDragPos[btn];
                    _prevDragPos[btn] = pos;
                    if (delta.sqrMagnitude > 0f)
                        OnDrag?.Invoke(btn, pos, delta, mods);
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            int btn = evt.button;
            if (btn < 0 || btn >= 3) return;

            Vector2 pos       = ToViewportCoord(evt.localPosition);
            var     mods      = GetMods(evt);
            var     prevState = _state[btn];
            _state[btn] = BtnState.Idle;

            OnButtonUp?.Invoke(btn, pos, mods);

            if (prevState == BtnState.Pressed)
                OnClick?.Invoke(btn, pos, mods);
            else if (prevState == BtnState.Dragging)
                OnDragEnd?.Invoke(btn, pos, mods);

            // 全ボタンがIdleになったときにキャプチャ解放。
            bool anyActive = _state[0] != BtnState.Idle
                          || _state[1] != BtnState.Idle
                          || _state[2] != BtnState.Idle;
            if (!anyActive && this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
        }

        /// <summary>
        /// PointerCapture が外部から強制解除された場合（フォーカス喪失等）に
        /// 全ボタン状態をリセットしてDragEndを発火する。
        /// </summary>
        private void OnPointerCaptureLost(PointerCaptureOutEvent evt)
        {
            var mods = new ModifierKeys();
            for (int btn = 0; btn < 3; btn++)
            {
                if (_state[btn] == BtnState.Idle) continue;
                var prev = _state[btn];
                _state[btn] = BtnState.Idle;
                if (prev == BtnState.Dragging)
                    OnDragEnd?.Invoke(btn, MousePosition, mods);
            }
        }

        // キャプチャロスト時の座標用（最後のポインター座標を保持）
        private Vector2 MousePosition;

        private void OnWheel(WheelEvent evt)
        {
            // UIToolkit WheelEvent.delta.y: 下スクロール=正
            // PlayerMouseDispatcher に合わせ: 手前スクロール（上）=正
            float scroll = -evt.delta.y * 0.1f;
            var   mods   = new ModifierKeys
            {
                Shift = evt.shiftKey,
                Ctrl  = evt.ctrlKey,
                Alt   = evt.altKey,
            };
            OnScroll?.Invoke(scroll, mods);
            evt.StopPropagation();
        }

        // ================================================================
        // 座標変換：panel-local（Y=0 が上）→ viewport-screen（Y=0 が下）
        // ================================================================

        /// <summary>
        /// UIToolkit local座標（Y=0が上）をビューポートスクリーン座標（Y=0が下）に変換する。
        /// Camera.WorldToScreenPoint の出力と同じ空間になる。
        /// </summary>
        private Vector2 ToViewportCoord(Vector2 local)
        {
            float h = resolvedStyle.height;
            return new Vector2(local.x, h - local.y);
        }

        private void OnGenerateBrushCircle(MeshGenerationContext ctx)
        {
            if (!_brushCircleVisible || _brushCircleRadius <= 0f) return;
            var painter = ctx.painter2D;
            painter.strokeColor = _brushCircleColor;
            painter.lineWidth   = 1.5f;
            painter.BeginPath();
            const int segments = 48;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                var pt = _brushCircleCenter + new Vector2(
                    Mathf.Cos(angle) * _brushCircleRadius,
                    Mathf.Sin(angle) * _brushCircleRadius);
                if (i == 0) painter.MoveTo(pt);
                else        painter.LineTo(pt);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void OnGenerateBoneOverlay(MeshGenerationContext ctx)
        {
            var data = _boneWireData;
            if (data.ScreenPos == null || data.ScreenPos.Length == 0) return;
            float panelH = resolvedStyle.height;
            var painter  = ctx.painter2D;
            const float r = 5f;

            for (int i = 0; i < data.ScreenPos.Length; i++)
            {
                bool sel = data.IsSelected != null && i < data.IsSelected.Length && data.IsSelected[i];
                Color col = sel ? new Color(1f, 0.6f, 0.1f, 0.9f) : new Color(0.2f, 0.8f, 1f, 0.8f);
                float px  = data.ScreenPos[i].x;
                float py  = panelH - data.ScreenPos[i].y; // Y=0下→Y=0上変換

                // 菱形（ボーン形状の簡易表示）
                painter.strokeColor = col;
                painter.lineWidth   = sel ? 2f : 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(px,     py - r));
                painter.LineTo(new Vector2(px + r, py));
                painter.LineTo(new Vector2(px,     py + r));
                painter.LineTo(new Vector2(px - r, py));
                painter.ClosePath();
                painter.Stroke();

                if (sel)
                {
                    painter.fillColor = new Color(1f, 0.6f, 0.1f, 0.3f);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(px,     py - r));
                    painter.LineTo(new Vector2(px + r, py));
                    painter.LineTo(new Vector2(px,     py + r));
                    painter.LineTo(new Vector2(px - r, py));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        private void OnGenerateAdvSelOverlay(MeshGenerationContext ctx)
        {
            if (_advSelPreviewPts.Count == 0 && _advSelPreviewLines.Count == 0) return;
            float panelH = resolvedStyle.height;
            Color col = _advSelAddMode ? new Color(0.1f, 1f, 0.3f, 0.85f)
                                       : new Color(1f, 0.3f, 0.2f, 0.85f);
            var painter = ctx.painter2D;

            // 辺（線）
            if (_advSelPreviewLines.Count > 0)
            {
                painter.strokeColor = col;
                painter.lineWidth   = 2.5f;
                foreach (var (a, b) in _advSelPreviewLines)
                {
                    var pa = new Vector2(a.x, panelH - a.y);
                    var pb = new Vector2(b.x, panelH - b.y);
                    painter.BeginPath();
                    painter.MoveTo(pa);
                    painter.LineTo(pb);
                    painter.Stroke();
                }
            }

            // 頂点（小さい正方形）
            if (_advSelPreviewPts.Count > 0)
            {
                painter.fillColor = col;
                const float halfSz = 4f;
                foreach (var pt in _advSelPreviewPts)
                {
                    var p = new Vector2(pt.x, panelH - pt.y);
                    painter.BeginPath();
                    painter.MoveTo(p + new Vector2(-halfSz, -halfSz));
                    painter.LineTo(p + new Vector2( halfSz, -halfSz));
                    painter.LineTo(p + new Vector2( halfSz,  halfSz));
                    painter.LineTo(p + new Vector2(-halfSz,  halfSz));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        private void OnGenerateGizmoOverlay(MeshGenerationContext ctx)
        {
            if (!_gizmoData.HasGizmo) return;
            var at = _gizmoData.HoveredAxis; var dt = _gizmoData.DraggingAxis;

            if (_gizmoData.IsDiamondStyle)
            {
                // オブジェクト移動: 軸線 + 先端ダイヤ + 中心ダイヤ
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.XEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X)
                    ?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f));
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.YEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y)
                    ?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f));
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.ZEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z)
                    ?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f));
                DrawGizmoDiamond(ctx, _gizmoData.XEnd, 7f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X)
                    ?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f));
                DrawGizmoDiamond(ctx, _gizmoData.YEnd, 7f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y)
                    ?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f));
                DrawGizmoDiamond(ctx, _gizmoData.ZEnd, 7f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z)
                    ?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f));
                bool ch=(at==Poly_Ling.Tools.AxisGizmo.AxisType.Center||dt==Poly_Ling.Tools.AxisGizmo.AxisType.Center);
                DrawGizmoDiamond(ctx, _gizmoData.Origin, 9f,
                    ch?new Color(1f,1f,1f,.9f):new Color(.8f,.8f,.8f,.6f));
            }
            else
            {
                // 頂点移動: 矢印
                DrawGizmoAxis(ctx, _gizmoData.Origin, _gizmoData.XEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X)
                    ?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f));
                DrawGizmoAxis(ctx, _gizmoData.Origin, _gizmoData.YEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y)
                    ?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f));
                DrawGizmoAxis(ctx, _gizmoData.Origin, _gizmoData.ZEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z)
                    ?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f));
                bool ch=(at==Poly_Ling.Tools.AxisGizmo.AxisType.Center||dt==Poly_Ling.Tools.AxisGizmo.AxisType.Center);
                DrawGizmoCenterHandle(ctx,_gizmoData.Origin,8f,ch?new Color(1f,1f,1f,.9f):new Color(.8f,.8f,.8f,.6f));
            }

            // ピボット位置のダイヤ型ギズモ
            if (_gizmoData.HasPivotGizmo)
            {
                DrawGizmoDiamond(ctx, _gizmoData.PivotOrigin, 9f, new Color(1f, 1f, 0.2f, 0.9f));
            }
        }

        /// <summary>軸線のみ描画（ダイヤスタイル用）。</summary>
        private static void DrawGizmoAxisLine(MeshGenerationContext ctx, Vector2 from, Vector2 to, Color col)
        {
            Vector2 d = (to - from).normalized, p = new Vector2(-d.y, d.x) * 1.5f;
            var m = ctx.Allocate(4, 6); var v = new Vertex[4];
            v[0]=new Vertex{position=new Vector3(from.x-p.x,from.y-p.y,Vertex.nearZ),tint=col};
            v[1]=new Vertex{position=new Vector3(from.x+p.x,from.y+p.y,Vertex.nearZ),tint=col};
            v[2]=new Vertex{position=new Vector3(to.x+p.x,to.y+p.y,Vertex.nearZ),tint=col};
            v[3]=new Vertex{position=new Vector3(to.x-p.x,to.y-p.y,Vertex.nearZ),tint=col};
            m.SetAllVertices(v); m.SetAllIndices(new ushort[]{0,2,1,0,3,2});
        }

        /// <summary>塗りつぶしダイヤ（菱形）を描画する。</summary>
        private static void DrawGizmoDiamond(MeshGenerationContext ctx, Vector2 c, float r, Color col)
        {
            // 上・右・下・左の4頂点
            var m = ctx.Allocate(4, 6); var v = new Vertex[4];
            v[0]=new Vertex{position=new Vector3(c.x,    c.y-r,   Vertex.nearZ),tint=col}; // 上
            v[1]=new Vertex{position=new Vector3(c.x+r,  c.y,     Vertex.nearZ),tint=col}; // 右
            v[2]=new Vertex{position=new Vector3(c.x,    c.y+r,   Vertex.nearZ),tint=col}; // 下
            v[3]=new Vertex{position=new Vector3(c.x-r,  c.y,     Vertex.nearZ),tint=col}; // 左
            m.SetAllVertices(v); m.SetAllIndices(new ushort[]{0,1,2,0,2,3});
        }
        private static void DrawGizmoAxis(MeshGenerationContext ctx,Vector2 from,Vector2 to,Color col)
        {
            Vector2 d=(to-from).normalized,p=new Vector2(-d.y,d.x)*1.5f;
            var m=ctx.Allocate(4,6); var v=new Vertex[4];
            v[0]=new Vertex{position=new Vector3(from.x-p.x,from.y-p.y,Vertex.nearZ),tint=col};
            v[1]=new Vertex{position=new Vector3(from.x+p.x,from.y+p.y,Vertex.nearZ),tint=col};
            v[2]=new Vertex{position=new Vector3(to.x+p.x,to.y+p.y,Vertex.nearZ),tint=col};
            v[3]=new Vertex{position=new Vector3(to.x-p.x,to.y-p.y,Vertex.nearZ),tint=col};
            m.SetAllVertices(v);m.SetAllIndices(new ushort[]{0,2,1,0,3,2});
            float hs=10f; Vector2 p2=new Vector2(-d.y,d.x)*hs*.5f,bc=to-d*hs;
            var m2=ctx.Allocate(3,3); var v2=new Vertex[3];
            v2[0]=new Vertex{position=new Vector3(to.x,to.y,Vertex.nearZ),tint=col};
            v2[1]=new Vertex{position=new Vector3(bc.x-p2.x,bc.y-p2.y,Vertex.nearZ),tint=col};
            v2[2]=new Vertex{position=new Vector3(bc.x+p2.x,bc.y+p2.y,Vertex.nearZ),tint=col};
            m2.SetAllVertices(v2);m2.SetAllIndices(new ushort[]{0,2,1});
        }
        private static void DrawGizmoCenterHandle(MeshGenerationContext ctx,Vector2 c,float h,Color col)
        {
            var m=ctx.Allocate(4,6); var v=new Vertex[4];
            v[0]=new Vertex{position=new Vector3(c.x-h,c.y-h,Vertex.nearZ),tint=col};
            v[1]=new Vertex{position=new Vector3(c.x+h,c.y-h,Vertex.nearZ),tint=col};
            v[2]=new Vertex{position=new Vector3(c.x+h,c.y+h,Vertex.nearZ),tint=col};
            v[3]=new Vertex{position=new Vector3(c.x-h,c.y+h,Vertex.nearZ),tint=col};
            m.SetAllVertices(v);m.SetAllIndices(new ushort[]{0,1,2,0,2,3});
        }

        private static ModifierKeys GetMods(PointerEventBase<PointerDownEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };

        // ================================================================
        // 面ホバーオーバーレイ描画
        // ================================================================

        /// <summary>
        /// generateVisualContent コールバック。
        /// _hoverFaceScreenPts（スクリーン座標 Y=0が下）を panel local（Y=0が上）に変換して描画。
        /// ポリゴンをファンで三角形分割して塗りつぶし + 枠線を描く。
        /// </summary>
        private void OnGenerateFaceOverlay(MeshGenerationContext ctx)
        {
            float panelH = resolvedStyle.height;

            // 選択面（青系）
            var selFaces = _selectedFacesScreenPts;
            if (selFaces != null)
            {
                var selColor = new UnityEngine.Color(0.2f, 0.5f, 1f, 0.3f);
                foreach (var pts in selFaces)
                    DrawFacePolygon(ctx, pts, panelH, selColor);
            }

            // ホバー面（オレンジ）
            var hoverPts = _hoverFaceScreenPts;
            if (hoverPts != null && hoverPts.Length >= 3)
                DrawFacePolygon(ctx, hoverPts, panelH, new UnityEngine.Color(1f, 0.6f, 0.1f, 0.25f));
        }

        private static void DrawFacePolygon(MeshGenerationContext ctx,
            UnityEngine.Vector2[] pts, float panelH, UnityEngine.Color color)
        {
            if (pts == null || pts.Length < 3) return;
            int triCount = pts.Length - 2;
            var localPts = new Vertex[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                localPts[i] = new Vertex
                {
                    position = new UnityEngine.Vector3(pts[i].x, panelH - pts[i].y, Vertex.nearZ),
                    tint     = color,
                };
            var mesh = ctx.Allocate(localPts.Length, triCount * 3);
            mesh.SetAllVertices(localPts);
            var indices = new ushort[triCount * 3];
            for (int t = 0; t < triCount; t++)
            {
                indices[t * 3]     = 0;
                indices[t * 3 + 1] = (ushort)(t + 1);
                indices[t * 3 + 2] = (ushort)(t + 2);
            }
            mesh.SetAllIndices(indices);
        }

        private static ModifierKeys GetMods(PointerEventBase<PointerMoveEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };

        private static ModifierKeys GetMods(PointerEventBase<PointerUpEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };
    }
}
