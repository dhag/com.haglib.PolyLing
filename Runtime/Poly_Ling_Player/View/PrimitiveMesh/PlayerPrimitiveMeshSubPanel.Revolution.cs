// PlayerPrimitiveMeshSubPanel.Revolution.cs
// 図形生成サブパネル：回転体の状態・諸元 UI・プロファイルエディタの組み立て・
// メッシュとの取り込み／反映。キャンバス操作は Revolution.Canvas.cs。
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
        private static RevolutionParams DefaultRevolutionParams()
        { var p = RevolutionParams.Default; p.Pivot = DefaultPivotBottom; return p; }

        private RevolutionParams                     _revP    = DefaultRevolutionParams();
        private List<Vector2>                        _revProfile = null;

        // ── プロファイルの取り込み元（作り直しで掛け直すために控える）
        //    「取り込み(メッシュ→プロファイル)」を押したときだけ埋まる。
        //    点を手で打った／プリセットを使った場合は -1 / Baked のままで、
        //    作り直しでは控えた点列がそのまま使われる。
        private int _revProfileSrcIndex = -1;

        // ================================================================
        // Revolution プロファイルエディタ状態
        // ================================================================

        private int           _revSelIdx    = -1;
        private bool          _revDrag      = false;
        private int           _revDragIdx   = -1;

        // 複数選択・一括移動・マーキー（Phase 2）
        private readonly HashSet<int> _revSel = new HashSet<int>();
        private readonly Dictionary<int, Vector2> _revDragStart = new Dictionary<int, Vector2>();
        private Vector2 _revDragStartCursorProf;
        private readonly Canvas2DMarquee _revMarquee = new Canvas2DMarquee();
        private bool _revLassoMode;
        private bool _revMarqueeAdditive;
        private bool _revMarqueeDrag;

        // 回転/拡大縮小アンカーと変換（Phase B）
        private readonly Canvas2DAnchor _revAnchor = new Canvas2DAnchor();
        private bool          _revAnchorDrag;
        private bool          _revAnchorSuppress;
        // 回転体の断面編集も 2D 断面と同じく、キャンバス上のポインタ操作が主。
        // キャンバスと点の編集欄は項目にしない。
        [UiControl(Ignore = true)]
        private Slider        _revAnchorXSlider;
        [UiControl(Ignore = true)]
        private Slider        _revAnchorYSlider;
        [UiControl(Ignore = true)]
        private FloatField    _revAnchorXField;
        [UiControl(Ignore = true)]
        private FloatField    _revAnchorYField;
        [UiControl(Ignore = true)]
        private Button        _revAnchorEnterBtn;
        [UiControl(Ignore = true)]
        private VisualElement _revAnchorPanel;
        [UiControl(Ignore = true)]
        private FloatField    _revTfMoveX;
        [UiControl(Ignore = true)]
        private FloatField    _revTfMoveY;
        [UiControl(Ignore = true)]
        private FloatField    _revTfScaleX;
        [UiControl(Ignore = true)]
        private FloatField    _revTfScaleY;
        [UiControl(Ignore = true)]
        private FloatField    _revTfRot;
        [UiControl(Ignore = true)]
        private FloatField    _revTfScaleAxis;

        // 回転/拡大縮小ハンドル（キャンバス上ドラッグ）
        private readonly Canvas2DHandle _revHandle = new Canvas2DHandle();
        private bool                    _revHandleDrag;
        private Canvas2DHandle.HandleType _revHandleType = Canvas2DHandle.HandleType.None;
        private readonly Dictionary<int, Vector2> _revHandleStart = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, float>   _revHandleW     = new Dictionary<int, float>();
        private Vector2 _revHandleAnchorC;   // ドラッグ開始時のアンカー(キャンバス座標)
        private float   _revHandlePrevAngle; // 回転累積用の直前角度
        private float   _revHandleTotalDeg;  // 累積回転角(データ空間deg)
        // ギズモ表示トグル（回転体。既定=非表示、メモリ保持・非永続）
        private bool    _revShowGizmo = false;

        // マグネット（比例編集、Phase）
        private readonly Canvas2DMagnet _revMagnet = new Canvas2DMagnet();
        private readonly Dictionary<int, Vector2> _revMagnetStart = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, float>   _revMagnetW     = new Dictionary<int, float>();
        [UiControl(Ignore = true)]
        private Slider        _revMagnetRadius;
        private int           _revHoverEI   = -1;
        [UiControl(Ignore = true)]
        private VisualElement _revCanvas;
        [UiControl(Ignore = true)]
        private VisualElement _revPtRow;
        [UiControl(Ignore = true)]
        private Label         _revPtLabel;
        [UiControl(Ignore = true)]
        private Slider        _revPtXSlider;
        [UiControl(Ignore = true)]
        private FloatField    _revPtXField;
        [UiControl(Ignore = true)]
        private Slider        _revPtYSlider;
        [UiControl(Ignore = true)]
        private FloatField    _revPtYField;
        private string        _revCsvPath   = "";
        private const string  RevCsvKey  = "Primitive.Revolution.Csv";

        // 下絵
        private Texture2D     _revBgTex;
        [UiControl(Ignore = true)]
        private VisualElement _revBgEl;
        private string        _revBgPath    = "";
        private Vector2       _revBgOffset  = Vector2.zero;
        private float         _revBgScale   = 3f;   // 画像高さ(ワールド単位)
        private Vector2       _revBgOrigin  = Vector2.zero; // 拡大縮小の原点（画像px, 既定=中心）
        [UiControl(Ignore = true)]
        private Slider        _revBgScaleSlider;
        [UiControl(Ignore = true)]
        private Label         _revBgSizeLabel;
        private float         _revBgAlpha   = 0.4f;
        private bool          _revBgMode    = false; // true=下絵移動モード
        private bool          _revBgDrag    = false;
        private Vector2       _revBgDragStart;
        private Vector2       _revBgOffsetOnDragStart;

        // プロファイルビュー（ズーム/パン）
        private float         _revZoom      = 1f;
        private Vector2       _revOffset    = Vector2.zero;
        [UiControl(Ignore = true)]
        private VisualElement _revViewLayer;          // 下絵を view 変換で追従させる層
        private bool          _revPanDrag;            // 中ボタンパン中
        private Vector2       _revPanStart;
        private Vector2       _revPanOffsetStart;

        // ================================================================
        // Revolution UI
        // ================================================================

        private void EnsureRevProfile()
        {
            if (_revProfile == null)
                _revProfile = RevolutionProfileGenerator.CreateDefault();
        }

        private void BuildRevolutionUI(VisualElement c)
        {
            EnsureRevProfile();

            // ドラッグ状態をリセット（タブ切替後の再Build時）
            _revDrag = false; _revDragIdx = -1; _revHoverEI = -1;

            c.Add(ShapeTitle(T("Revolution")));
            c.Add(NF(() => _revP.MeshName, v => _revP.MeshName = v));
            c.Add(IR(T("RadialSegments"), RevolutionParams.RadialSegmentsMin, RevolutionParams.RadialSegmentsMax,   () => _revP.RadialSegments, v => { _revP.RadialSegments = v; D(); }));
            c.Add(TR(T("CloseTop"),    () => _revP.CloseTop,    v => { _revP.CloseTop    = v; D(); }));
            c.Add(TR(T("CloseBottom"), () => _revP.CloseBottom, v => { _revP.CloseBottom = v; D(); }));
            c.Add(TR(T("CloseLoop"),   () => _revP.CloseLoop,   v => { _revP.CloseLoop   = v; D(); RefreshRevCanvas(); }));
            c.Add(TR(T("Spiral"),      () => _revP.Spiral,      v => { _revP.Spiral      = v; D(); }));
            if (_revP.Spiral)
            {
                c.Add(IR(T("SpiralTurns"), RevolutionParams.SpiralTurnsMin, RevolutionParams.SpiralTurnsMax,   () => _revP.SpiralTurns, v => { _revP.SpiralTurns = v; D(); }));
                c.Add(SR(T("SpiralPitch"), RevolutionParams.SpiralPitchMin, RevolutionParams.SpiralPitchMax, () => _revP.SpiralPitch, v => { _revP.SpiralPitch = v; D(); }));
            }
            c.Add(TR(T("FlipY"), () => _revP.FlipY, v => { _revP.FlipY = v; D(); }));
            c.Add(TR(T("FlipZ"), () => _revP.FlipZ, v => { _revP.FlipZ = v; D(); }));

            // ── プリセット ──────────────────────────────────────────────
            c.Add(SL(T("Preset")));
            var presetChoices = new List<string>
                { T("Custom"), T("Donut"), T("RoundedPipe"), T("Vase"), T("Goblet"), T("Bell"), T("Hourglass") };
            var presetEnum = new ProfilePreset[]
                { ProfilePreset.Custom, ProfilePreset.Donut, ProfilePreset.RoundedPipe,
                  ProfilePreset.Vase,   ProfilePreset.Goblet, ProfilePreset.Bell, ProfilePreset.Hourglass };
            var presetDd = new DropdownField(null, presetChoices, 0);
            presetDd.style.marginBottom = 3;
            presetDd.RegisterValueChangedCallback(e =>
            {
                int idx = presetChoices.IndexOf(e.newValue);
                if (idx < 0) return;
                _revP.CurrentPreset = presetEnum[idx];
                if (_revP.CurrentPreset != ProfilePreset.Custom)
                {
                    RevBegin();
                    _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                    _revSel.Clear(); _revSelIdx = -1;
                    RevCommit("プリセット適用");
                }
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            c.Add(presetDd);

            if (_revP.CurrentPreset == ProfilePreset.Donut)
            {
                c.Add(SL(T("Donut")));
                c.Add(SR(T("DonutMajorRadius"), RevolutionParams.DonutMajorRadiusMin, RevolutionParams.DonutMajorRadiusMax,   () => _revP.DonutMajorRadius, v => { _revP.DonutMajorRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("DonutMinorRadius"), RevolutionParams.DonutMinorRadiusMin, RevolutionParams.DonutMinorRadiusMax,  () => _revP.DonutMinorRadius, v => { _revP.DonutMinorRadius = v; ApplyRevPreset(); }));
                c.Add(IR(T("DonutTubeSegs"), RevolutionParams.DonutTubeSegmentsMin, RevolutionParams.DonutTubeSegmentsMax,      () => _revP.DonutTubeSegments, v => { _revP.DonutTubeSegments = v; ApplyRevPreset(); }));
            }
            if (_revP.CurrentPreset == ProfilePreset.RoundedPipe)
            {
                c.Add(SL(T("RoundedPipe")));
                c.Add(SR(T("PipeInnerRadius"), RevolutionParams.PipeInnerRadiusMin, RevolutionParams.PipeInnerRadiusMax, () => _revP.PipeInnerRadius, v => { _revP.PipeInnerRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeOuterRadius"), RevolutionParams.PipeOuterRadiusMin, RevolutionParams.PipeOuterRadiusMax, () => _revP.PipeOuterRadius, v => { _revP.PipeOuterRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeHeight"), RevolutionParams.PipeHeightMin, RevolutionParams.PipeHeightMax, () => _revP.PipeHeight,      v => { _revP.PipeHeight      = v; ApplyRevPreset(); }));
                c.Add(SL(T("InnerCorner")));
                c.Add(SR(T("CornerRadius"), RevolutionParams.PipeCornerRadiusMin, RevolutionParams.PipeCornerRadiusMax, () => _revP.PipeInnerCornerRadius,  v => { _revP.PipeInnerCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"), RevolutionParams.PipeCornerSegmentsMin, RevolutionParams.PipeCornerSegmentsMax,    () => _revP.PipeInnerCornerSegments, v => { _revP.PipeInnerCornerSegments = v; ApplyRevPreset(); }));
                c.Add(SL(T("OuterCorner")));
                c.Add(SR(T("CornerRadius"), RevolutionParams.PipeCornerRadiusMin, RevolutionParams.PipeCornerRadiusMax, () => _revP.PipeOuterCornerRadius,  v => { _revP.PipeOuterCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"), RevolutionParams.PipeCornerSegmentsMin, RevolutionParams.PipeCornerSegmentsMax,    () => _revP.PipeOuterCornerSegments, v => { _revP.PipeOuterCornerSegments = v; ApplyRevPreset(); }));
            }

            BuildPivotY(c,
                () => _revP.Pivot.y, v => { _revP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _, out _);

            // プロファイルエディタは「回転体」と「揺れボーン 1本 / 回転体」で
            // 共用する。折れ線の意味も同じ（X が半径方向、Y が高さ）。
            BuildRevolutionProfileEditor(_profileEditorContainer);
        }

        /// <summary>
        /// 折れ線（プロファイル）の編集 UI を組む。
        /// 回転体と、揺れもの用ボーン鎖の「1 本」「回転体」から呼ぶ。
        /// 状態は _revProfile / _revSelIdx / _revZoom / _revOffset を共有する。
        /// </summary>
        private void BuildRevolutionProfileEditor(VisualElement pe)
        {
            pe.Add(SL(T("ProfileEditor")));

            // インタラクティブキャンバス
            _revCanvas = new VisualElement();
            _revCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _revCanvas.style.height          = _profileHeight;
            _revCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _revCanvas.style.marginBottom    = 4;
            _revCanvas.style.borderTopWidth  = _revCanvas.style.borderBottomWidth =
            _revCanvas.style.borderLeftWidth = _revCanvas.style.borderRightWidth  = 1;
            _revCanvas.style.borderTopColor  = _revCanvas.style.borderBottomColor =
            _revCanvas.style.borderLeftColor = _revCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _revCanvas.style.overflow        = Overflow.Hidden;
            _revCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー（ビューレイヤー配下。プロファイルと同じ view 変換で追従）
            _revViewLayer = new VisualElement();
            _revViewLayer.style.position = Position.Absolute;
            _revViewLayer.style.left = _revViewLayer.style.top =
            _revViewLayer.style.right = _revViewLayer.style.bottom = 0;
            _revViewLayer.pickingMode = PickingMode.Ignore;

            _revBgEl = new VisualElement();
            _revBgEl.style.position = Position.Absolute;
            _revBgEl.style.display  = DisplayStyle.None;
            _revBgEl.pickingMode    = PickingMode.Ignore;
            _revViewLayer.Add(_revBgEl);
            _revCanvas.Add(_revViewLayer);

            _revCanvas.generateVisualContent += OnDrawProfileCanvas;
            _revCanvas.RegisterCallback<PointerDownEvent>(OnRevCanvasPointerDown);
            _revCanvas.RegisterCallback<PointerMoveEvent>(OnRevCanvasPointerMove);
            _revCanvas.RegisterCallback<PointerUpEvent>(OnRevCanvasPointerUp);
            _revCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_revBgMode)
                {
                    // サブモード中は下絵スケール。
                    _revBgScale = Mathf.Clamp(_revBgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    _revBgScaleSlider?.SetValueWithoutNotify(_revBgScale);
                    UpdateRevBgEl(); RefreshRevCanvas();
                }
                else
                {
                    // 通常モードはプロファイルビューをカーソル基準でズーム。
                    float w = _revCanvas.resolvedStyle.width, h = _revCanvas.resolvedStyle.height;
                    float oldZoom = _revZoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 8f);
                    if (newZoom != oldZoom)
                    {
                        var m = (Vector2)e.localMousePosition;
                        var center = new Vector2(w * 0.5f, h * 0.5f);
                        float k = newZoom / oldZoom;
                        _revOffset = (m - center) * (1f - k) + _revOffset * k;
                        _revZoom   = newZoom;
                        UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
                    }
                }
                e.StopPropagation();
            });
            _revCanvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateRevBgEl(); UpdateRevView(); RefreshRevCanvas();
            });
            pe.Add(_revCanvas);

            // キャンバス縦リサイズハンドル
            AddProfileResizeHandle(pe, _revCanvas, RefreshRevCanvas);

            // ボタン行: 削除 / リセット
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 4;
            SB(btnRow, T("DeletePoint"), () =>
            {
                EnsureRevProfile();
                RevBegin();
                if (_revSel.Count > 0)
                {
                    // 選択点を一括削除（インデックス降順・最小2点は維持）。
                    var idxs = new List<int>(_revSel);
                    idxs.Sort(); idxs.Reverse();
                    foreach (var idx in idxs)
                        if (idx >= 0 && idx < _revProfile.Count && _revProfile.Count > 2)
                            _revProfile.RemoveAt(idx);
                    _revSel.Clear(); _revSelIdx = -1;
                }
                else
                {
                    RevolutionProfileEditCore.RemovePoint(_revProfile, ref _revSelIdx);
                }
                _revP.CurrentPreset = ProfilePreset.Custom;
                RevCommit("点削除");
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            SB(btnRow, T("ClearProfile"), () =>
            {
                EnsureRevProfile();
                RevBegin();
                RevolutionProfileEditCore.ResetProfile(_revProfile, ref _revSelIdx);
                _revSel.Clear();
                _revP.CurrentPreset = ProfilePreset.Custom;
                RevCommit("プロファイルリセット");
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            pe.Add(btnRow);

            // ここから下を1つの大フォールドにまとめ、中の各セクションも個別に折り畳む。
            pe = FoldSection(pe, T("EditTools"), true);

            // ビュー操作行（3エディタ共通の並び：ビュー初期化 / 投げ縄 / ギズモ）
            var revViewRow = new VisualElement();
            revViewRow.style.flexDirection = FlexDirection.Row;
            revViewRow.style.marginBottom  = 3;
            SB(revViewRow, T("ResetView"), () =>
            {
                _revZoom = 1f; _revOffset = Vector2.zero;
                UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
            });
            var revLassoToggle = new Toggle(T("LassoMode")) { value = _revLassoMode };
            revLassoToggle.style.marginLeft = 4;
            revLassoToggle.RegisterValueChangedCallback(ev => _revLassoMode = ev.newValue);
            revViewRow.Add(revLassoToggle);
            var revGizmoToggle = new Toggle(T("ShowGizmo")) { value = _revShowGizmo };
            revGizmoToggle.style.marginLeft = 8;
            revGizmoToggle.RegisterValueChangedCallback(ev => { _revShowGizmo = ev.newValue; RefreshRevCanvas(); });
            revViewRow.Add(revGizmoToggle);
            pe.Add(revViewRow);

            BuildRevAnchorTransformUI(pe);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(pe,
                T("BgImage"),
                () => _revBgPath, v => _revBgPath = v,
                () => _revBgAlpha, v => { _revBgAlpha = v; UpdateRevBgEl(); },
                () => _revBgMode,  v => { _revBgMode  = v; },
                () => _revBgScale, v => { _revBgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateRevBgEl(); RefreshRevCanvas(); },
                () => _revBgOrigin, v => { _revBgOrigin = v; UpdateRevBgEl(); RefreshRevCanvas(); },
                () => _revBgTex,
                () => // Load
                {
                    if (string.IsNullOrEmpty(_revBgPath)) return;
                    LoadBgTexture(_revBgPath, ref _revBgTex, _revBgEl);
                    _revBgOffset = Vector2.zero; _revBgScale = 3f;
                    if (_revBgTex != null)
                        _revBgOrigin = new Vector2(_revBgTex.width * 0.5f, _revBgTex.height * 0.5f);
                    _revBgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(_revBgSizeLabel, _revBgTex);
                    UpdateRevBgEl();
                },
                () => // Clear
                {
                    _revBgTex = null;
                    _revBgEl.style.display = DisplayStyle.None;
                    _revBgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(_revBgSizeLabel, null);
                },
                out _revBgScaleSlider, out _revBgSizeLabel);

            // 選択点スライダー（Floatフィールド付き、非選択時は非表示）
            _revPtRow = new VisualElement(); _revPtRow.style.marginBottom = 4;
            _revPtLabel = new Label(""); _revPtLabel.style.fontSize = 9; _revPtLabel.style.marginBottom = 1;
            _revPtRow.Add(_revPtLabel);

            // X (radius)
            {
                Slider    xSl = new Slider(0f, 2f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 42;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    _revProfile[_revSelIdx] = new Vector2(e.newValue, _revProfile[_revSelIdx].y);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => RevBegin());
                xSl.RegisterCallback<PointerUpEvent>(_ => RevCommit("点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    RevBegin();
                    float v = Mathf.Clamp(e.newValue, 0f, 2f);
                    xSl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(v, _revProfile[_revSelIdx].y);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                    RevCommit("点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("R (X)")); xRow.Add(xSl); xRow.Add(xFf);
                _revPtRow.Add(xRow);
                _revPtXSlider = xSl; _revPtXField = xFf;
            }
            // Y (height)
            {
                Slider    ySl = new Slider(-1f, 2f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 42;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    _revProfile[_revSelIdx] = new Vector2(_revProfile[_revSelIdx].x, e.newValue);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => RevBegin());
                ySl.RegisterCallback<PointerUpEvent>(_ => RevCommit("点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    RevBegin();
                    float v = Mathf.Clamp(e.newValue, -1f, 2f);
                    ySl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(_revProfile[_revSelIdx].x, v);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                    RevCommit("点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _revPtRow.Add(yRow);
                _revPtYSlider = ySl; _revPtYField = yFf;
            }
            _revPtRow.style.display = DisplayStyle.None;
            pe.Add(_revPtRow);

            // CSV 読み書き
            var revCsvFold = FoldSection(pe, T("ProfileCsvSection"), false);
            var csvPathField = new TextField();
            csvPathField.RegisterValueChangedCallback(e => { _revCsvPath = e.newValue; RecentPaths.Set(RevCsvKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadRevCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), RevCsvKey, _revCsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                _revCsvPath = sel;
                csvPathField.value = _revCsvPath;

                var result = RevolutionCSVIO.Load(_revCsvPath, _revP);
                if (result.Success)
                {
                    RevBegin();
                    _revProfile           = result.Profile;
                    _revP.RadialSegments  = result.RadialSegments;
                    _revP.CloseTop        = result.CloseTop;
                    _revP.CloseBottom     = result.CloseBottom;
                    _revP.CloseLoop       = result.CloseLoop;
                    _revP.Spiral          = result.Spiral;
                    _revP.Pivot           = new Vector3(0, result.PivotY, 0);
                    _revP.SpiralTurns     = result.SpiralTurns;
                    _revP.SpiralPitch     = result.SpiralPitch;
                    _revP.FlipY           = result.FlipY;
                    _revP.FlipZ           = result.FlipZ;
                    _revP.CurrentPreset   = ProfilePreset.Custom;
                    _revSel.Clear(); _revSelIdx = -1;
                    RevCommit("CSV読込");
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                else
                {
                    Debug.LogWarning($"[Revolution CSV] {result.ErrorMessage}");
                }
            }

            revCsvFold.Add(PlayerIoUiKit.PathRow(csvPathField, LoadRevCsv));
            if (string.IsNullOrEmpty(_revCsvPath)) _revCsvPath = RecentPaths.Get(RevCsvKey);
            if (!string.IsNullOrEmpty(_revCsvPath)) csvPathField.SetValueWithoutNotify(_revCsvPath);

            revCsvFold.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadRevCsv));
            revCsvFold.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                // パス欄は読込用。保存は毎回ダイアログを出す。
                // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
                string save = SaveDest.AskSavePath(
                    T("SaveCSV"), SaveDest.Keys.ProfileCsv, "", "revolution.csv", "csv");
                if (string.IsNullOrEmpty(save)) return;
                _revCsvPath = save;
                csvPathField.SetValueWithoutNotify(_revCsvPath);
                RecentPaths.Set(RevCsvKey, _revCsvPath);

                EnsureRevProfile();
                RevolutionCSVIO.Save(_revCsvPath, _revProfile, _revP);
            }));

            // ── メッシュ⇄プロファイル ─────────────────────────────────────
            var revIoFold = FoldSection(pe, T("MeshProfileIO"), false);
            var ioRow = new VisualElement(); ioRow.style.flexDirection = FlexDirection.Row; ioRow.style.marginBottom = 4;
            SB(ioRow, T("ImportFromMesh"), ImportRevolutionFromMesh);
            SB(ioRow, T("ApplyToMesh"),    ApplyRevolutionToMesh);
            revIoFold.Add(ioRow);
        }

        private void ApplyRevPreset()
        {
            if (_revP.CurrentPreset != ProfilePreset.Custom)
            {
                _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                _revSel.Clear(); _revSelIdx = -1;
            }
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>Rev 下絵 VisualElement の位置・原点・スケール・アルファを更新</summary>
        private void UpdateRevBgEl()
        {
            if (_revBgEl == null || _revBgTex == null) return;
            float cw = _revCanvas.resolvedStyle.width;
            float ch = _revCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _revBgTex.width;
            float bh = _revBgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            // ジオメトリ(ProfileToCanvas)と同じ基準倍率（ズーム除く）
            float baseScale = Mathf.Min(cw / RevolutionProfileEditCore.RangeX,
                                        ch / RevolutionProfileEditCore.RangeY);
            // 画像の高さ = _revBgScale ワールド単位（アスペクト維持）
            float s = (_revBgScale * baseScale) / bh;

            // 画像中心をプロファイル点 _revBgOffset に合わせる（zoom=1,offset=0 相当）
            Vector2 c = RevolutionProfileEditCore.ProfileToCanvas(_revBgOffset, cw, ch, 1f, Vector2.zero);
            _revBgEl.style.left   = c.x - bw * 0.5f; _revBgEl.style.top = c.y - bh * 0.5f;
            _revBgEl.style.width  = bw; _revBgEl.style.height = bh;
            _revBgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            _revBgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            _revBgEl.style.opacity = _revBgAlpha;
            _revBgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        // ================================================================
        // メッシュ⇄プロファイル連携（取り込み/反映）
        // 方針: Z を破棄し XY をそのまま扱う（座標変換なし）。
        // 取り込み元 = 選択オブジェクト内の全2頂点ライン（未選択でも対象）。
        // 反映先 = 既存 AddMode ドロップダウンに従う。
        // ================================================================

        /// <summary>選択オブジェクトの全2頂点ラインを Revolution プロファイルへ取り込む。</summary>
        private void ImportRevolutionFromMesh()
        {
            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { _statusLabel.text = T("NoSelectedMesh"); return; }

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);
            var pts       = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
            if (pts == null || pts.Count < 2) { _statusLabel.text = T("NoLinesFound"); return; }

            // 取り込み元を控える。オブジェクトグループが作り直すときに、
            // 同じオブジェクトから同じ読み方で掛け直せるようにするため。
            _revProfileSrcIndex = ResolveMasterIndexOf(mesh);

            RevBegin();
            _revProfile = new List<Vector2>(pts);
            _revSel.Clear(); _revSelIdx  = -1;
            _revP.CurrentPreset = ProfilePreset.Custom;
            RevCommit("メッシュ取込");
            _statusLabel.text = T("ImportedPoints", pts.Count);
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>Revolution プロファイルを2頂点ラインの MeshObject として反映する。</summary>
        private void ApplyRevolutionToMesh()
        {
            EnsureRevProfile();
            if (_revProfile == null || _revProfile.Count < 2) { _statusLabel.text = T("NoLinesFound"); return; }

            var mo = LineProfileExtractor.PolylineToLineMesh(_revProfile, _revP.MeshName, _revP.CloseLoop);
            if (mo == null || mo.FaceCount == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            ApplyPoseForDirectMeshCreate(mo);

            _statusLabel.text = T("AppliedToMesh", mo.FaceCount);
            SendGeneratedMesh(mo, _revP.MeshName);
        }
    }
}
