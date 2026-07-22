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

        /// <summary>Escape キー押下（ツール操作のキャンセル用）。</summary>
        public event Action OnCancelKey;

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
        private readonly VisualElement _lassoOverlay;
        private readonly List<Vector2> _lassoPoints = new List<Vector2>();
        private bool _lassoVisible;

        // ================================================================
        // ブラシ円オーバーレイ（スカルプトツール用）
        // ================================================================

        private readonly VisualElement _brushCircleOverlay;

        // ================================================================
        // 詳細選択プレビューオーバーレイ
        // ================================================================

        private readonly VisualElement _advSelOverlay;
        private readonly VisualElement _addFaceOverlay;
        // スクリーン座標（Y=0下）で渡し、描画時に変換する
        private List<Vector2>         _advSelPreviewPts    = new List<Vector2>();
        private List<(Vector2, Vector2)> _advSelPreviewLines = new List<(Vector2, Vector2)>();
        private bool                  _advSelAddMode;
        // 最短モードの始点強調マーカー（スクリーン座標 Y=0下）。null=非表示。
        private Vector2?              _advSelFirstPt;
        // 辺クリックの強調（辺の2端点、スクリーン座標 Y=0下）。null=非表示。
        private (Vector2, Vector2)?  _advSelFirstEdge;

        // 面追加オーバーレイ
        private List<Vector2>            _addFacePts          = new List<Vector2>();
        private List<Vector2>            _addFacePreviewPts   = new List<Vector2>(); // スナップ/通常プレビュー点
        private List<bool>               _addFacePreviewSnap  = new List<bool>();
        private List<(Vector2, Vector2)> _addFaceLines        = new List<(Vector2, Vector2)>();
        private bool                     _addFaceVisible;

        // トポロジーツール（辺ベベル/押し出し/トポロジー/面押し出し）ホバーオーバーレイ
        private readonly VisualElement                        _topoToolOverlay;
        private List<(Vector2 a, Vector2 b, Color col)>      _topoToolLines  = new List<(Vector2, Vector2, Color)>();
        private List<(Vector2 p, Color col, float halfSize)> _topoToolPoints = new List<(Vector2, Color, float)>();
        // 【要確認: Tick 経路に依存した追加 (AI 無断追加)】
        // Split モードのリング (1クリック目マーカー / ホバーリング) を描画するために
        // AI が無断追加したフィールド。データ供給は UpdateTopologyToolsOverlay
        // (Tick 経由毎フレーム実行) からのみ。Phase 2 で UpdateTopologyToolsOverlay を
        // hover イベント駆動に置換する際、リング描画継続要否を確認の上、
        // 不要ならフィールドごと削除予定。
        private List<(Vector2 p, Color col, float radius)>   _topoToolRings  = new List<(Vector2, Color, float)>();
        private bool                                          _topoToolVisible;

        // ================================================================
        // ボーンワイヤフレームオーバーレイ
        // ================================================================

        private readonly VisualElement _boneOverlay;

        // 下絵（3D背面）と RenderTexture 表示用の子要素。
        // z順（下→上）: _underlayImage → _rtImage → 各ツールオーバーレイ。
        // RT はカメラ背景を透明化した場合、非ジオメトリ部が透過して背面の下絵が見える。
        private readonly VisualElement _underlayImage;
        private readonly VisualElement _rtImage;

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
        private bool    _brushCircleShowCenter; // 中心マーカー（半径ドラッグ指定時のみ true）

        // ================================================================
        // 面ホバー / 選択面 overlay (Phase 2c 以降は GPU パス)
        //
        // 元々は generateVisualContent による UIToolkit Painter2D 描画だったが、
        // Phase 2c で UnifiedRenderer の GPU パイプラインに統合（選択面・ホバー面は
        // _FaceFlagsBuffer を見てシェーダが塗る）。本ファイル側は API のみ残す
        // no-op 実装。画面サイズ変更・視点移動・頂点移動すべて GPU 投影で自動追従する。
        // ================================================================

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
            /// <summary>スケール用キューブ型スタイル。true=軸線+先端キューブ（Unity準拠）。</summary>
            public bool IsCubeStyle;
            /// <summary>ピボット位置のダイヤ型ギズモ。</summary>
            public bool HasPivotGizmo;
            public Vector2 PivotOrigin, PivotXEnd, PivotYEnd, PivotZEnd;

            /// <summary>回転リングギズモ。true=3軸リングを描画。</summary>
            public bool IsRingStyle;
            public Vector2[] RingX, RingY, RingZ;
        }

        public void UpdateGizmo(GizmoData d)
        { _gizmoData = d; _gizmoOverlay.MarkDirtyRepaint(); }
        public void HideGizmo()
        { _gizmoData = default; _gizmoOverlay.MarkDirtyRepaint(); }

        /// <summary>
        /// 【Phase 2c 以降 no-op】面ホバー overlay は GPU 描画に移行済み。
        /// 呼出し元残置のため空実装。将来的に API 削除予定。
        /// </summary>
        public void ShowFaceHover(UnityEngine.Vector2[] screenPts) { }

        /// <summary>【Phase 2c 以降 no-op】</summary>
        public void HideFaceHover() { }

        /// <summary>【Phase 2c 以降 no-op】選択面 overlay は GPU 描画に移行済み。</summary>
        public void ShowSelectedFaces(List<UnityEngine.Vector2[]> faces) { }

        /// <summary>【Phase 2c 以降 no-op】</summary>
        public void HideSelectedFaces() { }

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
        // 投げ縄選択オーバーレイ
        // ================================================================

        /// <summary>投げ縄オーバーレイを更新する。</summary>
        public void ShowLassoSelect(System.Collections.Generic.List<Vector2> points)
        {
            _lassoPoints.Clear();
            if (points != null) _lassoPoints.AddRange(points);
            _lassoVisible = true;
            _lassoOverlay?.MarkDirtyRepaint();
        }

        /// <summary>投げ縄オーバーレイを非表示にする。</summary>
        public void HideLassoSelect()
        {
            _lassoVisible = false;
            _lassoPoints.Clear();
            _lassoOverlay?.MarkDirtyRepaint();
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
            => ShowBrushCircle(center, radius, color, showCenter: false);

        /// <summary>
        /// ブラシ円を指定カラーで表示する。showCenter=true のとき中心マーカー（十字）も描画する。
        /// center はスクリーン座標（Y=0 が下）、radius はピクセル。
        /// </summary>
        public void ShowBrushCircle(Vector2 center, float radius, Color color, bool showCenter)
        {
            float panelH = resolvedStyle.height;
            _brushCircleCenter     = new Vector2(center.x, panelH - center.y);
            _brushCircleRadius     = radius;
            _brushCircleColor      = color;
            _brushCircleShowCenter = showCenter;
            _brushCircleVisible    = true;
            _brushCircleOverlay?.MarkDirtyRepaint();
        }

        /// <summary>ブラシ円を非表示にする。</summary>
        public void HideBrushCircle()
        {
            _brushCircleVisible    = false;
            _brushCircleShowCenter = false;
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
        // ================================================================
        // 面追加プレビューオーバーレイ
        // ================================================================

        /// <summary>
        /// 面追加オーバーレイを更新する。
        ///
        /// 【用途】
        /// もともとは AddFace (面追加ツール) 専用の Painter2D オーバーレイだが、
        /// EdgeTopology の Split モードでも同じ API を流用する (共通の見た目 = 確定点
        /// シアン四角 + プレビュー点黄/シアン + 黄色い接続線)。
        /// 将来他のツール (例: 2 点を指定する系) でも同じ API で描けるよう汎用扱いする。
        ///
        /// AddFace / EdgeTopology-Split は InteractionMode が排他なので同時に描画
        /// されることはない。OverlayUpdate 経路 (OnRefreshAddFaceOverlay /
        /// OnRefreshTopologyToolsOverlay) も排他に設計されている。
        ///
        /// 【パラメータ】
        /// pts: 配置済み点（UIToolkit Y、Y=0上）
        /// previewPts: プレビュー点（Y=0上）
        /// previewSnapped: プレビュー点ごとのスナップフラグ
        ///                 (AddFace では頂点スナップ時、Split では対向点候補に乗ったとき true)
        /// lines: 確定済み線＋プレビュー線（Y=0上）
        /// </summary>
        public void UpdateAddFacePreview(
            List<Vector2> pts,
            List<Vector2> previewPts,
            List<bool>    previewSnapped,
            List<(Vector2, Vector2)> lines)
        {
            _addFacePts         = pts         ?? new List<Vector2>();
            _addFacePreviewPts  = previewPts  ?? new List<Vector2>();
            _addFacePreviewSnap = previewSnapped ?? new List<bool>();
            _addFaceLines       = lines       ?? new List<(Vector2, Vector2)>();
            _addFaceVisible     = true;
            _addFaceOverlay?.MarkDirtyRepaint();
        }

        public void HideAddFacePreview()
        {
            _addFaceVisible = false;
            _addFacePts.Clear();
            _addFacePreviewPts.Clear();
            _addFacePreviewSnap.Clear();
            _addFaceLines.Clear();
            _addFaceOverlay?.MarkDirtyRepaint();
        }

        // ----------------------------------------------------------------
        // トポロジーツールホバーオーバーレイ
        // ----------------------------------------------------------------

        /// <summary>
        /// トポロジーツール用ホバーオーバーレイを更新する。
        /// lines: (始点, 終点, 色) のリスト。座標は IMGUI Y（Y=0 上）で渡す。
        /// </summary>
        public void UpdateTopoToolOverlay(List<(Vector2 a, Vector2 b, Color col)> lines)
        {
            UpdateTopoToolOverlay(lines, null, null);
        }

        /// <summary>
        /// トポロジーツール用ホバーオーバーレイを更新する（点マーカー対応版）。
        /// lines: (始点, 終点, 色)。points: (点, 色, halfSize)。座標は IMGUI Y（Y=0 上）。
        /// </summary>
        public void UpdateTopoToolOverlay(
            List<(Vector2 a, Vector2 b, Color col)> lines,
            List<(Vector2 p, Color col, float halfSize)> points)
        {
            UpdateTopoToolOverlay(lines, points, null);
        }

        /// <summary>
        /// 【要確認: Tick 経路依存 (AI 無断追加)】
        /// トポロジーツール用ホバーオーバーレイを更新する（リング対応版）。
        /// lines: (始点, 終点, 色)。points: (点, 色, halfSize)。rings: (点, 色, radius)。座標は IMGUI Y（Y=0 上）。
        /// rings 引数は Split モードのリング描画のために AI が無断追加した。
        /// 呼び出し元は UpdateTopologyToolsOverlay (Tick 経由毎フレーム実行) のみ。
        /// Phase 2 で hover イベント駆動に置換する際、リング継続要否を確認の上、
        /// 不要なら 2 引数版に戻して 3 引数版を削除予定。
        /// </summary>
        public void UpdateTopoToolOverlay(
            List<(Vector2 a, Vector2 b, Color col)> lines,
            List<(Vector2 p, Color col, float halfSize)> points,
            List<(Vector2 p, Color col, float radius)> rings)
        {
            _topoToolLines   = lines  ?? new List<(Vector2, Vector2, Color)>();
            _topoToolPoints  = points ?? new List<(Vector2, Color, float)>();
            _topoToolRings   = rings  ?? new List<(Vector2, Color, float)>();
            _topoToolVisible = _topoToolLines.Count > 0 || _topoToolPoints.Count > 0 || _topoToolRings.Count > 0;
            _topoToolOverlay?.MarkDirtyRepaint();
        }

        public void HideTopoToolOverlay()
        {
            _topoToolVisible = false;
            _topoToolLines.Clear();
            _topoToolPoints.Clear();
            _topoToolRings.Clear();
            _topoToolOverlay?.MarkDirtyRepaint();
        }

        public void UpdateAdvSelPreview(
            List<Vector2> pts, List<(Vector2, Vector2)> lines, bool addMode,
            Vector2? firstPt = null, (Vector2, Vector2)? firstEdge = null)
        {
            _advSelPreviewPts   = pts   ?? new List<Vector2>();
            _advSelPreviewLines = lines ?? new List<(Vector2, Vector2)>();
            _advSelAddMode      = addMode;
            _advSelFirstPt      = firstPt;
            _advSelFirstEdge    = firstEdge;
            _advSelOverlay?.MarkDirtyRepaint();
        }

        /// <summary>詳細選択プレビューを非表示にする。</summary>
        public void HideAdvSelPreview()
        {
            _advSelPreviewPts.Clear();
            _advSelPreviewLines.Clear();
            _advSelFirstPt   = null;
            _advSelFirstEdge = null;
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
            // RT は子要素 _rtImage に移したため、パネル自身の背景はグレー単色にする
            // （下絵・ジオメトリのいずれにも覆われない領域の色。従来のクリア色と同じ）。
            style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f, 1f));

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureLost);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // キーボード（Escape 等）を受け取れるようにする。
            // イベント駆動のためポインタ操作時にフォーカスを取得する。
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            // ── 下絵（最背面・既定非表示） ──────────────────────────────
            _underlayImage = new VisualElement();
            _underlayImage.style.position    = Position.Absolute;
            _underlayImage.style.display     = DisplayStyle.None;
            _underlayImage.pickingMode       = PickingMode.Ignore;
            Add(_underlayImage);

            // ── RenderTexture 表示（下絵の上・ツールオーバーレイの下） ──
            _rtImage = new VisualElement();
            _rtImage.style.position   = Position.Absolute;
            _rtImage.style.left = _rtImage.style.top =
            _rtImage.style.right = _rtImage.style.bottom = 0;
            _rtImage.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
            _rtImage.pickingMode      = PickingMode.Ignore;
            Add(_rtImage);

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

            // 投げ縄選択オーバーレイ（generateVisualContent による折れ線描画）
            _lassoOverlay = new VisualElement();
            _lassoOverlay.style.position = Position.Absolute;
            _lassoOverlay.style.left = _lassoOverlay.style.top =
            _lassoOverlay.style.right = _lassoOverlay.style.bottom = 0;
            _lassoOverlay.pickingMode = PickingMode.Ignore;
            _lassoOverlay.generateVisualContent += OnGenerateLassoOverlay;
            Add(_lassoOverlay);

            // Phase 2c: 面ホバー / 選択面 overlay は GPU パスに移行したため
            // ここに VisualElement を作成しない。ギズモ overlay のみ残る。
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

            // 面追加プレビューオーバーレイ
            _addFaceOverlay = new VisualElement();
            _addFaceOverlay.style.position = Position.Absolute;
            _addFaceOverlay.style.left = _addFaceOverlay.style.top =
            _addFaceOverlay.style.right = _addFaceOverlay.style.bottom = 0;
            _addFaceOverlay.pickingMode = PickingMode.Ignore;
            _addFaceOverlay.generateVisualContent += OnGenerateAddFaceOverlay;
            Add(_addFaceOverlay);

            // トポロジーツールホバーオーバーレイ
            _topoToolOverlay = new VisualElement();
            _topoToolOverlay.style.position = Position.Absolute;
            _topoToolOverlay.style.left = _topoToolOverlay.style.top =
            _topoToolOverlay.style.right = _topoToolOverlay.style.bottom = 0;
            _topoToolOverlay.pickingMode = PickingMode.Ignore;
            _topoToolOverlay.generateVisualContent += OnGenerateTopoToolOverlay;
            Add(_topoToolOverlay);

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
                _rtImage.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(Viewport.RT));
        }

        // ================================================================
        // 下絵（3D背面）
        // ================================================================

        /// <summary>
        /// このパネルに下絵を設定する。RT の背面に配置され、非ジオメトリ部から見える。
        /// topLeft: パネル左上からのpx位置。scaleOrigin: 拡大縮小の原点（要素ローカルpx）。
        /// scale: 2Dスケール（x,y）。
        /// </summary>
        public void SetUnderlay(Texture2D tex, Vector2 topLeft, Vector2 scaleOrigin, Vector2 scale)
        {
            if (tex == null) { ClearUnderlay(); return; }

            _underlayImage.style.display         = DisplayStyle.Flex;
            _underlayImage.style.backgroundImage = new StyleBackground(tex);
            _underlayImage.style.width           = tex.width;
            _underlayImage.style.height          = tex.height;
            _underlayImage.style.left            = topLeft.x;
            _underlayImage.style.top             = topLeft.y;
            _underlayImage.style.transformOrigin = new TransformOrigin(
                new Length(scaleOrigin.x, LengthUnit.Pixel),
                new Length(scaleOrigin.y, LengthUnit.Pixel), 0f);
            _underlayImage.style.scale           = new Scale(new Vector3(scale.x, scale.y, 1f));
        }

        /// <summary>下絵を非表示にする。</summary>
        public void ClearUnderlay()
        {
            _underlayImage.style.display = DisplayStyle.None;
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

            // ビューポート操作時にフォーカスを取得し、Escape 等のキー入力を受けられるようにする。
            Focus();

            Vector2 pos  = ToViewportCoord(evt.localPosition);
            var     mods = GetMods(evt);

            _state[btn]   = BtnState.Pressed;
            _downPos[btn] = pos;
            OnButtonDown?.Invoke(btn, pos, mods);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                OnCancelKey?.Invoke();
                evt.StopPropagation();
            }
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

        private void OnGenerateLassoOverlay(MeshGenerationContext ctx)
        {
            if (!_lassoVisible || _lassoPoints.Count < 2) return;

            var painter = ctx.painter2D;
            Color borderColor = new Color(1f, 1f, 0.3f, 0.9f);
            float panelH = resolvedStyle.height;

            // LassoPoints は GPU Y（Y=0下）。UIToolkit 描画は Y=0上なので反転する。
            painter.strokeColor = borderColor;
            painter.lineWidth   = 1.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(_lassoPoints[0].x, panelH - _lassoPoints[0].y));
            for (int i = 1; i < _lassoPoints.Count; i++)
                painter.LineTo(new Vector2(_lassoPoints[i].x, panelH - _lassoPoints[i].y));
            painter.Stroke();
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

            // 半径ドラッグ指定時: 開始位置（中心）に十字マーカーを描画
            if (_brushCircleShowCenter)
            {
                const float m = 6f;
                painter.BeginPath();
                painter.MoveTo(_brushCircleCenter + new Vector2(-m, 0f));
                painter.LineTo(_brushCircleCenter + new Vector2( m, 0f));
                painter.MoveTo(_brushCircleCenter + new Vector2(0f, -m));
                painter.LineTo(_brushCircleCenter + new Vector2(0f,  m));
                painter.Stroke();
            }
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
            if (_advSelPreviewPts.Count == 0 && _advSelPreviewLines.Count == 0
                && !_advSelFirstPt.HasValue && !_advSelFirstEdge.HasValue) return;
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

            // 最短モードの始点強調マーカー（黄・外リング＋中心塗り）
            if (_advSelFirstPt.HasValue)
            {
                var fp = new Vector2(_advSelFirstPt.Value.x, panelH - _advSelFirstPt.Value.y);
                var hi = new Color(1f, 0.85f, 0.1f, 0.95f);

                // 外リング
                painter.strokeColor = hi;
                painter.lineWidth   = 2.5f;
                painter.BeginPath();
                painter.Arc(fp, 9f, 0f, 360f);
                painter.Stroke();

                // 中心塗り
                painter.fillColor = hi;
                painter.BeginPath();
                painter.Arc(fp, 4f, 0f, 360f);
                painter.Fill();
            }

            // 辺クリックの強調（黄・太線＋端点マーカー）
            if (_advSelFirstEdge.HasValue)
            {
                var hi = new Color(1f, 0.85f, 0.1f, 0.95f);
                var ea = new Vector2(_advSelFirstEdge.Value.Item1.x, panelH - _advSelFirstEdge.Value.Item1.y);
                var eb = new Vector2(_advSelFirstEdge.Value.Item2.x, panelH - _advSelFirstEdge.Value.Item2.y);

                painter.strokeColor = hi;
                painter.lineWidth   = 4f;
                painter.BeginPath();
                painter.MoveTo(ea);
                painter.LineTo(eb);
                painter.Stroke();

                painter.fillColor = hi;
                painter.BeginPath(); painter.Arc(ea, 3.5f, 0f, 360f); painter.Fill();
                painter.BeginPath(); painter.Arc(eb, 3.5f, 0f, 360f); painter.Fill();
            }
        }

        private void OnGenerateAddFaceOverlay(MeshGenerationContext ctx)
        {
            if (!_addFaceVisible) return;
            var painter = ctx.painter2D;
            float panelH = resolvedStyle.height;

            // AdvSel と同じパターン: panelH - pt.y で変換する。
            System.Func<Vector2, Vector2> cv = (p) => new Vector2(p.x, panelH - p.y);

            // 確定済み線（黄色）
            if (_addFaceLines.Count > 0)
            {
                painter.strokeColor = new Color(1f, 0.85f, 0.2f, 0.9f);
                painter.lineWidth = 2f;
                foreach (var (a, b) in _addFaceLines)
                {
                    painter.BeginPath();
                    painter.MoveTo(cv(a));
                    painter.LineTo(cv(b));
                    painter.Stroke();
                }
            }

            // 確定済み点（シアン）
            const float halfSz = 5f;
            foreach (var pt in _addFacePts)
            {
                var p = cv(pt);
                painter.fillColor = new Color(0f, 1f, 1f, 0.95f);
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(-halfSz, -halfSz));
                painter.LineTo(p + new Vector2( halfSz, -halfSz));
                painter.LineTo(p + new Vector2( halfSz,  halfSz));
                painter.LineTo(p + new Vector2(-halfSz,  halfSz));
                painter.ClosePath();
                painter.Fill();
            }

            // プレビュー点（スナップ=シアン大、通常=黄半透明）
            for (int i = 0; i < _addFacePreviewPts.Count; i++)
            {
                var p   = cv(_addFacePreviewPts[i]);
                bool snap = i < _addFacePreviewSnap.Count && _addFacePreviewSnap[i];
                float sz = snap ? 7f : 5f;
                painter.fillColor = snap
                    ? new Color(0f, 1f, 1f, 0.95f)
                    : new Color(1f, 1f, 0f, 0.55f);
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(-sz, -sz));
                painter.LineTo(p + new Vector2( sz, -sz));
                painter.LineTo(p + new Vector2( sz,  sz));
                painter.LineTo(p + new Vector2(-sz,  sz));
                painter.ClosePath();
                painter.Fill();
                if (snap)
                {
                    painter.strokeColor = new Color(0f, 1f, 1f, 0.6f);
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.Arc(p, 12f, 0f, 360f);
                    painter.Stroke();
                }
            }
        }

        private void OnGenerateTopoToolOverlay(MeshGenerationContext ctx)
        {
            if (!_topoToolVisible) return;
            var painter = ctx.painter2D;
            float panelH = resolvedStyle.height;
            // AddFace と同じ座標変換パターン: panelH - pt.y
            System.Func<Vector2, Vector2> cv = (p) => new Vector2(p.x, panelH - p.y);

            foreach (var (a, b, col) in _topoToolLines)
            {
                painter.strokeColor = col;
                painter.lineWidth   = 3f;
                painter.BeginPath();
                painter.MoveTo(cv(a));
                painter.LineTo(cv(b));
                painter.Stroke();
            }

            // 点マーカー（塗りつぶし矩形: AddFace と同じスタイル）
            foreach (var (p, col, hs) in _topoToolPoints)
            {
                var c = cv(p);
                painter.fillColor = col;
                painter.BeginPath();
                painter.MoveTo(c + new Vector2(-hs, -hs));
                painter.LineTo(c + new Vector2( hs, -hs));
                painter.LineTo(c + new Vector2( hs,  hs));
                painter.LineTo(c + new Vector2(-hs,  hs));
                painter.ClosePath();
                painter.Fill();
            }

            // 【要確認: Tick 経路依存 (AI 無断追加)】
            // リング描画 (AddFace のスナップ視覚と同じ円描画)。
            // Split モードのリング (1クリック目マーカー / ホバーリング) のために
            // AI が無断追加した分岐。データ供給は UpdateTopologyToolsOverlay
            // (Tick 経由毎フレーム実行) からのみ。
            // Phase 2 で UpdateTopologyToolsOverlay を hover イベント駆動に
            // 置換する際、リング継続要否を確認の上、不要ならこのブロックも削除予定。
            foreach (var (p, col, radius) in _topoToolRings)
            {
                var c = cv(p);
                painter.strokeColor = col;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.Arc(c, radius, 0f, 360f);
                painter.Stroke();
            }
        }

        private void OnGenerateGizmoOverlay(MeshGenerationContext ctx)
        {
            if (!_gizmoData.HasGizmo) return;
            var at = _gizmoData.HoveredAxis; var dt = _gizmoData.DraggingAxis;

            if (_gizmoData.IsRingStyle)
            {
                bool hx = (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X);
                bool hy = (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y);
                bool hz = (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z);
                DrawGizmoPolyline(ctx, _gizmoData.RingX, hx?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f), hx?2.5f:1.5f);
                DrawGizmoPolyline(ctx, _gizmoData.RingY, hy?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f), hy?2.5f:1.5f);
                DrawGizmoPolyline(ctx, _gizmoData.RingZ, hz?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f), hz?2.5f:1.5f);
                return;
            }

            if (_gizmoData.IsCubeStyle)
            {
                // スケール: 軸線 + 先端キューブ + 中心キューブ（Unity準拠）
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.XEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X)
                    ?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f));
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.YEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y)
                    ?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f));
                DrawGizmoAxisLine(ctx, _gizmoData.Origin, _gizmoData.ZEnd,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z)
                    ?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f));
                DrawGizmoCenterHandle(ctx, _gizmoData.XEnd, 6f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.X||at==Poly_Ling.Tools.AxisGizmo.AxisType.X)
                    ?new Color(1f,.3f,.3f,1f):new Color(.8f,.2f,.2f,.7f));
                DrawGizmoCenterHandle(ctx, _gizmoData.YEnd, 6f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Y||at==Poly_Ling.Tools.AxisGizmo.AxisType.Y)
                    ?new Color(.3f,1f,.3f,1f):new Color(.2f,.8f,.2f,.7f));
                DrawGizmoCenterHandle(ctx, _gizmoData.ZEnd, 6f,
                    (dt==Poly_Ling.Tools.AxisGizmo.AxisType.Z||at==Poly_Ling.Tools.AxisGizmo.AxisType.Z)
                    ?new Color(.3f,.3f,1f,1f):new Color(.2f,.2f,.8f,.7f));
                bool ch=(at==Poly_Ling.Tools.AxisGizmo.AxisType.Center||dt==Poly_Ling.Tools.AxisGizmo.AxisType.Center);
                DrawGizmoCenterHandle(ctx, _gizmoData.Origin, 8f,
                    ch?new Color(1f,1f,1f,.9f):new Color(.8f,.8f,.8f,.6f));
            }
            else if (_gizmoData.IsDiamondStyle)
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

        /// <summary>スクリーン折れ線（リング）を太さ付きで描画する。</summary>
        private static void DrawGizmoPolyline(MeshGenerationContext ctx, Vector2[] pts, Color col, float width)
        {
            if (pts == null || pts.Length < 2) return;
            float hw = width * 0.5f;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 from = pts[i], to = pts[i + 1];
                Vector2 dir = to - from;
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();
                Vector2 p = new Vector2(-dir.y, dir.x) * hw;
                var m = ctx.Allocate(4, 6); var v = new Vertex[4];
                v[0]=new Vertex{position=new Vector3(from.x-p.x,from.y-p.y,Vertex.nearZ),tint=col};
                v[1]=new Vertex{position=new Vector3(from.x+p.x,from.y+p.y,Vertex.nearZ),tint=col};
                v[2]=new Vertex{position=new Vector3(to.x+p.x,to.y+p.y,Vertex.nearZ),tint=col};
                v[3]=new Vertex{position=new Vector3(to.x-p.x,to.y-p.y,Vertex.nearZ),tint=col};
                m.SetAllVertices(v); m.SetAllIndices(new ushort[]{0,2,1,0,3,2});
            }
        }

        /// <summary>軸線のみ描画（ダイヤスタイル用）。</summary>
        private static void DrawGizmoAxisLine(MeshGenerationContext ctx, Vector2 from, Vector2 to, Color col)        {
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

        // Phase 2c: 面ホバー・選択面は GPU 描画パスに統合済み。
        // 旧 OnGenerateFaceOverlay / DrawFacePolygon は削除。

        private static ModifierKeys GetMods(PointerEventBase<PointerMoveEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };

        private static ModifierKeys GetMods(PointerEventBase<PointerUpEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };
    }
}
