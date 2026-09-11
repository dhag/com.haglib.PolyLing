// PlayerLayoutRoot.cs
// UIToolkit による3ペインレイアウト構築。
// このファイルは中央4ビューポート・仕切り・レイアウト永続化を持つ。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【partial の分担】
//   LeftPane.cs         左ペイン上部の常時表示部（状態・Undo・モデル・選択モード）と表示フラグのグリッド
//   LeftPaneButtons.cs  左ペインの通常ボタン（カテゴリ別 Foldout）。ボタンを増やすときはここだけ
//   RightPane.cs        右ペインの各セクション
//   Style.cs            部品生成ヘルパ（ボタン・見出し・Foldout 等）と配色・押下フィードバック
//   SplitMode.cs        中央4画面の分割モード

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // ビューポートパネル公開
        // ================================================================

        public PlayerViewportPanel PerspectivePanel { get; private set; }
        public PlayerViewportPanel TopPanel         { get; private set; }
        public PlayerViewportPanel FrontPanel       { get; private set; }
        public PlayerViewportPanel SidePanel        { get; private set; }

        // 中央ペイン ビューポート操作UI
        public Toggle PerspOrthoToggle { get; private set; }   // Perspective をオルソ表示に切替
        public Button TopFlipBtn       { get; private set; }   // Top ↔ Bottom
        public Button FrontFlipBtn     { get; private set; }   // Front ↔ Back
        public Button SideFlipBtn      { get; private set; }   // Right ↔ Left
        public Label  TopViewLabel     { get; private set; }
        public Label  FrontViewLabel   { get; private set; }
        public Label  SideViewLabel    { get; private set; }
        public Toggle TiltToggleFront  { get; private set; }   // Front/Side を水平45°斜めに
        public Toggle TiltToggleSide   { get; private set; }   // (Front/Side 連動・同じ共有値)

        /// <summary>
        /// 4ビューポート（Perspective / Right / Top / Front）を含む中央領域。
        /// 3面図を含むキャプチャの切り出し範囲に使う。
        /// </summary>
        public VisualElement ViewportArea { get; private set; }

        // ================================================================
        // 上下分割スプリッター・クロスハンドル（連動用）
        // ================================================================

        private TwoPaneSplitView _splitCenter;
        private TwoPaneSplitView _splitPerspSide;
        private TwoPaneSplitView _splitTopFront;
        private TwoPaneSplitView _splitLCR;   // 左ペイン | (中央+右)
        private TwoPaneSplitView _splitCR;    // 中央 | 右ペイン
        private VisualElement    _perspPane;
        private VisualElement    _topPane;
        private VisualElement    _leftPaneEl;   // _splitLCR の左固定ペイン（幅保存用）
        private VisualElement    _rightPaneEl;  // _splitCR の右固定ペイン（幅保存用）
        private float            _lastSyncedHeight = -1f;

        // クロスドラッグ領域
        private VisualElement _crossDragRegion;
        private VisualElement _centerDraglineAnchor;   // _splitCenter 専用 dragline（Build中にキャッシュ）
        private VisualElement _lcrDraglineAnchor;       // _splitLCR 専用 dragline（Build中にキャッシュ）
        private VisualElement _crDraglineAnchor;        // _splitCR 専用 dragline（Build中にキャッシュ）
        private VisualElement _rootRef;
        private float         _dragStartVH;
        private float         _dragStartHW;
        private float         _currentRightW;
        private Vector2       _dragStartPanelPos;
        private bool          _crossDragging;

        // ── レイアウト永続化（端末ローカル: PlayerPrefs）─────────────────
        private const string PrefLeftW       = "PolyLing.Player.Layout.LeftW";
        private const string PrefRightW      = "PolyLing.Player.Layout.RightW";
        private const string PrefCenterRight = "PolyLing.Player.Layout.CenterRightW";
        private const string PrefCenterH     = "PolyLing.Player.Layout.CenterH";
        private const float  DefLeftW   = 200f;
        private const float  DefRightW  = 220f;
        private const float  DefCenterW = 240f;
        private bool         _layoutRestored;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.width         = new StyleLength(new Length(100, LengthUnit.Percent));
            root.style.height        = new StyleLength(new Length(100, LengthUnit.Percent));

            // 保存済みレイアウト（端末ローカル）を読み込む。未保存時は既定値。
            float savedLeftW   = LoadPref(PrefLeftW,       DefLeftW);
            float savedRightW  = LoadPref(PrefRightW,      DefRightW);
            float savedCenterW = LoadPref(PrefCenterRight, DefCenterW);

            _splitLCR = new TwoPaneSplitView(0, savedLeftW, TwoPaneSplitViewOrientation.Horizontal);
            _splitLCR.style.flexGrow = 1;
            root.Add(_splitLCR);

            var leftPaneEl = BuildLeftPane();
            _leftPaneEl = leftPaneEl;
            _splitLCR.Add(leftPaneEl);
            // 子 TwoPaneSplitView を Add する前に自身の dragline-anchor をキャッシュする
            // （後から Q() すると子の anchor を誤って返すため）。
            _lcrDraglineAnchor = _splitLCR.Q(className: "unity-two-pane-split-view__dragline-anchor");

            _splitCR = new TwoPaneSplitView(1, savedRightW, TwoPaneSplitViewOrientation.Horizontal);
            _splitCR.style.flexGrow = 1;
            _splitLCR.Add(_splitCR);

            _splitCenter = new TwoPaneSplitView(1, savedCenterW, TwoPaneSplitViewOrientation.Horizontal);
            _splitCenter.style.flexGrow = 1;
            _splitCR.Add(_splitCenter);
            // _splitCR の dragline-anchor は _splitCenter を Add した後だと混同するため、
            // この時点でキャッシュする。
            _crDraglineAnchor = _splitCR.Q(className: "unity-two-pane-split-view__dragline-anchor");
            // 子 TwoPaneSplitView を追加する前にキャッシュする。
            // 後から Q() すると _splitPerspSide の dragline を誤って返す。
            _centerDraglineAnchor = _splitCenter.Q(className: "unity-two-pane-split-view__dragline-anchor");

            PlayerViewportPanel perspPanel, topPanel, frontPanel, sidePanel;

            _splitPerspSide = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            _splitPerspSide.style.flexGrow = 1;
            _splitCenter.Add(_splitPerspSide);
            PerspOrthoToggle = new Toggle("オルソ") { value = false };
            PerspOrthoToggle.style.fontSize = 10;
            PerspOrthoToggle.style.color    = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            var perspWrap = BuildViewportPane("Perspective", out perspPanel, out _, PerspOrthoToggle);
            _splitPerspSide.Add(perspWrap); PerspectivePanel = perspPanel;
            _perspPane = perspWrap;

            SideFlipBtn    = MakeFlipBtn("反転");
            TiltToggleSide = MakeTiltToggle("斜め45");
            _splitPerspSide.Add(BuildViewportPane("Right", out sidePanel, out var sideLbl, MakeHeaderRow(TiltToggleSide, SideFlipBtn)));
            SidePanel = sidePanel; SideViewLabel = sideLbl;

            _splitTopFront = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            _splitTopFront.style.flexGrow = 1;
            _splitCenter.Add(_splitTopFront);

            TopFlipBtn = MakeFlipBtn("反転");
            var topWrap = BuildViewportPane("TOP", out topPanel, out var topLbl, TopFlipBtn);
            TopViewLabel = topLbl;
            _splitTopFront.Add(topWrap); TopPanel = topPanel;
            _topPane = topWrap;

            FrontFlipBtn    = MakeFlipBtn("反転");
            TiltToggleFront = MakeTiltToggle("斜め45");
            _splitTopFront.Add(BuildViewportPane("Front", out frontPanel, out var frontLbl, MakeHeaderRow(TiltToggleFront, FrontFlipBtn)));
            FrontPanel = frontPanel; FrontViewLabel = frontLbl;

            // 4ビューポートを含む中央領域（キャプチャの切り出し範囲）。
            ViewportArea = _splitCenter;

            var rightPaneEl = BuildRightPane();
            _rightPaneEl = rightPaneEl;
            _splitCR.Add(rightPaneEl);

            SetupVerticalSplitSync();

            _rootRef = root;
            SetupCrossDragRegion(root);
            SetupLayoutPersistence(root);
        }

        // ================================================================
        // レイアウト永続化（端末ローカル: PlayerPrefs）
        // ================================================================

        private void SetupLayoutPersistence(VisualElement root)
        {
            // 外側スプリッター（左幅・右幅）のドラッグ確定で保存。
            if (_lcrDraglineAnchor != null)
                _lcrDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());
            if (_crDraglineAnchor != null)
                _crDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // 中央の左右区切り（標準ドラッグ）の確定で保存。
            if (_centerDraglineAnchor != null)
                _centerDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // 中央の上下区切り（persp/top の縦スプリッター標準ドラッグ）の確定で保存。
            // 各 split は最下層で子に split を持たないため、自身の anchor が取れる。
            var dlPersp = _splitPerspSide?.Q(className: "unity-two-pane-split-view__dragline-anchor");
            if (dlPersp != null) dlPersp.RegisterCallback<PointerUpEvent>(_ => SaveLayout());
            var dlTop = _splitTopFront?.Q(className: "unity-two-pane-split-view__dragline-anchor");
            if (dlTop != null) dlTop.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // レイアウト確定後に中央の左右・上下区切りを復元する（初回のみ）。
            // 中央の左右区切りはカスタムドラッグ機構（_currentRightW + dragline 再配置）、
            // 上下区切りは persp/top の height 同期のため、コンストラクタ初期値だけでは
            // 内部状態が揃わない。resolvedStyle が確定する初回 GeometryChanged で適用する。
            root.RegisterCallback<GeometryChangedEvent>(OnRootFirstGeometry);

            // ウィンドウ破棄時に最終保存。
            root.RegisterCallback<DetachFromPanelEvent>(_ => SaveLayout());
        }

        private void OnRootFirstGeometry(GeometryChangedEvent evt)
        {
            if (_layoutRestored) return;
            float w = _rootRef != null ? _rootRef.resolvedStyle.width : 0f;
            if (float.IsNaN(w) || w <= 0f) return;   // レイアウト未確定
            _layoutRestored = true;
            _rootRef.UnregisterCallback<GeometryChangedEvent>(OnRootFirstGeometry);

            float savedCenterW = LoadPref(PrefCenterRight, DefCenterW);
            ApplyHorizontalSplitWidth(Mathf.Max(50f, savedCenterW));

            float savedCenterH = LoadPref(PrefCenterH, -1f);
            if (savedCenterH > 0f)
                ApplyVerticalSplitHeight(Mathf.Max(50f, savedCenterH));
        }

        private void SaveLayout()
        {
            // resolvedStyle から実寸を取得し、異常値（NaN/0以下）は保存しない。
            // 仕切りを潰した側は 0 になるため、そのまま保存対象外になる。
            if (_leftPaneEl != null)
            {
                float v = _leftPaneEl.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefLeftW, v);
            }
            if (_rightPaneEl != null)
            {
                float v = _rightPaneEl.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefRightW, v);
            }
            if (_splitTopFront != null)
            {
                float v = _splitTopFront.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefCenterRight, v);
            }
            if (_perspPane != null)
            {
                float v = _perspPane.resolvedStyle.height;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefCenterH, v);
            }
            PlayerPrefs.Save();
        }

        private static float LoadPref(string key, float def)
        {
            float v = PlayerPrefs.GetFloat(key, def);
            return (float.IsNaN(v) || v <= 0f) ? def : v;
        }

        // ================================================================
        // クロスドラッグ領域（4分割交差点の同時ドラッグ）
        // ================================================================

        private void SetupCrossDragRegion(VisualElement root)
        {
            _crossDragRegion = new VisualElement();
            _crossDragRegion.style.position        = Position.Absolute;
            _crossDragRegion.style.width           = 16f;
            _crossDragRegion.style.height          = 16f;
            _crossDragRegion.style.backgroundColor = new StyleColor(Color.clear);
            _crossDragRegion.pickingMode           = PickingMode.Position;
            root.Add(_crossDragRegion);

            // _perspPane の右下が交差点座標。両分割の GeometryChanged で追従する。
            _perspPane.RegisterCallback<GeometryChangedEvent>(_ => UpdateCrossRegionPosition());
            _splitCenter.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateCrossRegionPosition();
                if (_crossDragging) ReapplyHorizontalDragline();
            });

            _crossDragRegion.RegisterCallback<PointerDownEvent>(OnCrossPointerDown);
            _crossDragRegion.RegisterCallback<PointerMoveEvent>(OnCrossPointerMove);
            _crossDragRegion.RegisterCallback<PointerUpEvent>(OnCrossPointerUp);
            _crossDragRegion.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                _crossDragging = false;
            });

            // クロスドラッグ中に TwoPaneSplitView が内部で _topPane/_perspPane の
            // style.height を初期値にリセットするのを上書きする。
            // GeometryChangedEvent は TwoPaneSplitView の内部コールバック後に発火するため、
            // ここで _lastSyncedHeight を再適用することで正しい位置に戻る。
            _splitPerspSide.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_crossDragging || _lastSyncedHeight <= 0f) return;
                _perspPane.style.height = _lastSyncedHeight;
                var dl = _splitPerspSide.Q(className: "unity-two-pane-split-view__dragline-anchor");
                if (dl != null) dl.style.top = _lastSyncedHeight;
            });
            _splitTopFront.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_crossDragging || _lastSyncedHeight <= 0f) return;
                _topPane.style.height = _lastSyncedHeight;
                var dl = _splitTopFront.Q(className: "unity-two-pane-split-view__dragline-anchor");
                if (dl != null) dl.style.top = _lastSyncedHeight;
            });
        }

        private void UpdateCrossRegionPosition()
        {
            if (_rootRef == null || _crossDragRegion == null) return;
            var wb = _perspPane.worldBound;
            if (float.IsNaN(wb.xMax) || float.IsNaN(wb.yMax) || wb.xMax <= 0f) return;
            // worldBound（パネル座標）→ root ローカル座標
            var localPos = _rootRef.WorldToLocal(new Vector2(wb.xMax, wb.yMax));
            const float half = 8f;
            _crossDragRegion.style.left = localPos.x - half;
            _crossDragRegion.style.top  = localPos.y - half;
        }

        /// <summary>
        /// 横分割（_splitCenter）の左右列幅を直接設定する。
        /// _splitCenter は fixedPaneIndex=1（右列固定）Horizontal。
        /// 右列（_splitTopFront）と左列（_splitPerspSide）の両方を同フレームで設定することで
        /// 左横線の右端ズレを防ぐ。
        /// </summary>
        private void ApplyHorizontalSplitWidth(float rightW)
        {
            rightW = Mathf.Max(50f, rightW);
            _currentRightW = rightW;
            _splitTopFront.style.width = rightW;
            ReapplyHorizontalDragline();
        }

        private void ReapplyHorizontalDragline()
        {
            if (_currentRightW <= 0f || _centerDraglineAnchor == null) return;
            float containerW = _splitCenter.resolvedStyle.width;
            if (float.IsNaN(containerW) || containerW <= 0f) return;
            _centerDraglineAnchor.style.left = containerW - _currentRightW;
        }

        private void OnCrossPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _crossDragging     = true;
            _dragStartPanelPos = evt.position;
            _dragStartVH       = _perspPane.resolvedStyle.height;
            _dragStartHW       = _splitTopFront.resolvedStyle.width;
            _crossDragRegion.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnCrossPointerMove(PointerMoveEvent evt)
        {
            if (!_crossDragging) return;
            Vector2 delta = (Vector2)evt.position - _dragStartPanelPos;
            // 横を先に適用し、縦を後から上書きする。
            // TwoPaneSplitView は横幅変更時に縦の固定ペイン高を内部リセットするため、
            // 縦を後に適用することで上書きが有効になる。
            ApplyHorizontalSplitWidth(Mathf.Max(50f, _dragStartHW - delta.x));
            ApplyVerticalSplitHeight(Mathf.Max(30f, _dragStartVH + delta.y));
            evt.StopPropagation();
        }

        private void OnCrossPointerUp(PointerUpEvent evt)
        {
            if (!_crossDragging) return;
            _crossDragging = false;
            if (_crossDragRegion.HasPointerCapture(evt.pointerId))
                _crossDragRegion.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
            SaveLayout();   // 交差ドラッグ確定（中央の左右＋上下）を保存
        }

        // ================================================================
        // 上下連動
        // ================================================================

        private void ApplyVerticalSplitHeight(float h)
        {
            _lastSyncedHeight = h;
            _perspPane.style.height = h;
            _topPane.style.height   = h;
            var dlL = _splitPerspSide.Q(className: "unity-two-pane-split-view__dragline-anchor");
            var dlR = _splitTopFront.Q(className:  "unity-two-pane-split-view__dragline-anchor");
            if (dlL != null) dlL.style.top = h;
            if (dlR != null) dlR.style.top = h;
        }

        private void SetupVerticalSplitSync()
        {
            _perspPane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_crossDragging) return;
                float h = _perspPane.resolvedStyle.height;
                if (float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Approximately(h, _lastSyncedHeight)) return;
                ApplyVerticalSplitHeight(h);
            });

            _topPane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_crossDragging) return;
                float h = _topPane.resolvedStyle.height;
                if (float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Approximately(h, _lastSyncedHeight)) return;
                ApplyVerticalSplitHeight(h);
            });
        }

        // ================================================================
        // ビューポートペイン
        // ================================================================

        private VisualElement BuildViewportPane(string label, out PlayerViewportPanel panel, out Label lbl, VisualElement headerRight = null)
        {
            var wrap = new VisualElement();
            wrap.style.flexGrow        = 1;
            wrap.style.flexDirection   = FlexDirection.Column;
            wrap.style.backgroundColor = new StyleColor(Color.white);

            lbl = new Label(label);
            lbl.style.position  = Position.Absolute;
            lbl.style.top       = 4;
            lbl.style.left      = 6;
            lbl.style.color     = new StyleColor(new Color(0.7f, 0.9f, 1f, 0.8f));
            lbl.style.fontSize  = 11;
            lbl.pickingMode     = PickingMode.Ignore;

            panel = new PlayerViewportPanel();
            wrap.Add(panel);
            wrap.Add(lbl);

            // 任意のヘッダ操作UI（オルソトグル／フリップボタン）を右上に絶対配置。
            if (headerRight != null)
            {
                headerRight.style.position = Position.Absolute;
                headerRight.style.top      = 2;
                headerRight.style.right    = 4;
                wrap.Add(headerRight);
            }
            return wrap;
        }

        /// <summary>ビューポート右上に置く小型フリップボタン。</summary>
        private static Button MakeFlipBtn(string text)
        {
            var b = new Button { text = text };
            b.style.fontSize      = 10;
            b.style.height        = 18;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.paddingLeft   = 5;
            b.style.paddingRight  = 5;
            b.style.marginTop     = 0;
            b.style.marginBottom  = 0;
            return b;
        }

        private static Toggle MakeTiltToggle(string label)
        {
            var t = new Toggle(label) { value = false };
            t.style.fontSize     = 10;
            t.style.marginTop    = 0;
            t.style.marginBottom = 0;
            t.style.marginRight  = 4;
            return t;
        }

        private static VisualElement MakeHeaderRow(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            foreach (var c in children) if (c != null) row.Add(c);
            return row;
        }
    }
}
