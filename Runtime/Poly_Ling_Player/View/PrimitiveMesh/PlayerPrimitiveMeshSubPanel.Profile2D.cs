// PlayerPrimitiveMeshSubPanel.Profile2D.cs
// 図形生成サブパネル：2D押し出しの状態・諸元 UI・メッシュとの取り込み／反映・CSV。
// キャンバス操作は Profile2D.Canvas.cs。
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
        private Profile2DParams                      _p2dP    = Profile2DParams.Default;
        private List<Loop>                           _p2dLoops = null;
        private int _p2dProfileSrcIndex = -1;
        private const string  P2dCsvKey  = "Primitive.Profile2D.Csv";

        // ── Profile2D キャンバス状態 ──────────────────────────────────────
        private VisualElement _p2dCanvas;
        private VisualElement _p2dPtRow;
        private Slider        _p2dPtXSlider;
        private FloatField    _p2dPtXField;
        private Slider        _p2dPtYSlider;
        private FloatField    _p2dPtYField;
        private int           _p2dSelLoop = 0;
        private int           _p2dSelPt   = -1;
        private bool          _p2dDrag    = false;
        private int           _p2dDragIdx = -1;

        // 複数選択・一括移動・マーキー（Phase 2、キー=((long)loop<<32)|pt）
        private readonly HashSet<long> _p2dSel = new HashSet<long>();
        private readonly Dictionary<long, Vector2> _p2dDragStart = new Dictionary<long, Vector2>();
        private Vector2 _p2dDragStartCursorWorld;
        private readonly Canvas2DMarquee _p2dMarquee = new Canvas2DMarquee();
        private bool _p2dLassoMode;
        private bool _p2dMarqueeAdditive;
        private bool _p2dMarqueeSubtract;
        private bool _p2dMarqueeDrag;
        // ギズモ/頂点の表示トグル（Profile2D。ギズモ既定=非表示／頂点既定=表示、メモリ保持・非永続）
        private bool _p2dShowGizmo = false;
        private bool _p2dShowVerts = true;

        // 回転/拡大縮小アンカーと変換（Phase B）
        private readonly Canvas2DAnchor _p2dAnchor = new Canvas2DAnchor();
        private bool          _p2dAnchorDrag;
        private bool          _p2dAnchorSuppress;
        private Slider        _p2dAnchorXSlider, _p2dAnchorYSlider;
        private FloatField    _p2dAnchorXField,  _p2dAnchorYField;
        private Button        _p2dAnchorEnterBtn;
        private VisualElement _p2dAnchorPanel;
        private FloatField    _p2dTfMoveX, _p2dTfMoveY, _p2dTfScaleX, _p2dTfScaleY, _p2dTfRot;
        private FloatField    _p2dTfScaleAxis;

        // 回転/拡大縮小ハンドル（キャンバス上ドラッグ）
        private readonly Canvas2DHandle _p2dHandle = new Canvas2DHandle();
        private bool                    _p2dHandleDrag;
        private Canvas2DHandle.HandleType _p2dHandleType = Canvas2DHandle.HandleType.None;
        private readonly Dictionary<long, Vector2> _p2dHandleStart = new Dictionary<long, Vector2>();
        private readonly Dictionary<long, float>   _p2dHandleW     = new Dictionary<long, float>();
        private Vector2 _p2dHandleAnchorC;
        private float   _p2dHandlePrevAngle;
        private float   _p2dHandleTotalDeg;
        // 角処理(ベベル)UI 要素（Thickness/Segments に応じて表示切替）
        private VisualElement _p2dEdgeLabel, _p2dEdgeFrontSeg, _p2dEdgeFrontSize, _p2dEdgeBackSeg, _p2dEdgeBackSize, _p2dEdgeInward;

        // マグネット（比例編集、Phase）
        private readonly Canvas2DMagnet _p2dMagnet = new Canvas2DMagnet();
        private readonly Dictionary<long, Vector2> _p2dMagnetStart = new Dictionary<long, Vector2>();
        private readonly Dictionary<long, float>   _p2dMagnetW     = new Dictionary<long, float>();
        private Slider        _p2dMagnetRadius;
        private float         _p2dZoom    = 1f;
        private Vector2       _p2dOffset  = Vector2.zero;
        private VisualElement _p2dViewLayer;
        private bool          _p2dPanDrag;
        private Vector2       _p2dPanStart;
        private Vector2       _p2dPanOffsetStart;
        private int           _p2dHoverEL = -1;
        private int           _p2dHoverEI = -1;
        private string        _p2dCsvPath = "";
        // 下絵
        private Texture2D     _p2dBgTex;
        private VisualElement _p2dBgEl;
        private string        _p2dBgPath   = "";
        private Vector2       _p2dBgOffset = Vector2.zero;
        private float         _p2dBgScale  = 8f;    // 画像高さ(ワールド単位)
        private Vector2       _p2dBgOrigin = Vector2.zero; // 拡大縮小の原点（画像px, 既定=中心）
        private Slider        _p2dBgScaleSlider;
        private Label         _p2dBgSizeLabel;
        private float         _p2dBgAlpha  = 0.4f;
        private bool          _p2dBgMode   = false;
        private bool          _p2dBgDrag   = false;
        private Vector2       _p2dBgDragStart;
        private Vector2       _p2dBgOffsetOnDragStart;

        // ================================================================
        // Profile2D UI（変更なし）
        // ================================================================

        private void EnsureP2DLoops()
        {
            if (_p2dLoops != null) return;
            _p2dLoops = new List<Loop>();
            var outer = new Loop();
            float r = 0.4f;
            outer.Points.AddRange(new[]
            {
                new Vector2(-r, -r), new Vector2( r, -r),
                new Vector2( r,  r), new Vector2(-r,  r),
            });
            _p2dLoops.Add(outer);
        }

        private void BuildProfile2DUI(VisualElement c)
        {
            EnsureP2DLoops();
            _p2dDrag = false; _p2dDragIdx = -1; _p2dHoverEL = -1; _p2dHoverEI = -1;
            _p2dSel.Clear();
            _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);

            c.Add(ShapeTitle(T("Profile2D")));
            c.Add(NF(() => _p2dP.MeshName, v => _p2dP.MeshName = v));

            var pe = _profileEditorContainer;

            // ヘルプ
            var helpLbl = new Label(T("P2dEditorHelp"));
            helpLbl.style.fontSize = 9;
            helpLbl.style.color = new StyleColor(new Color(0.6f, 0.7f, 0.6f));
            helpLbl.style.marginBottom = 3;
            helpLbl.style.whiteSpace = WhiteSpace.Normal;
            pe.Add(helpLbl);

            // ── 2D キャンバス ──────────────────────────────────────────────
            _p2dCanvas = new VisualElement();
            _p2dCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _p2dCanvas.style.height          = _profileHeight;
            _p2dCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _p2dCanvas.style.marginBottom    = 4;
            _p2dCanvas.style.borderTopWidth  = _p2dCanvas.style.borderBottomWidth =
            _p2dCanvas.style.borderLeftWidth = _p2dCanvas.style.borderRightWidth  = 1;
            _p2dCanvas.style.borderTopColor  = _p2dCanvas.style.borderBottomColor =
            _p2dCanvas.style.borderLeftColor = _p2dCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _p2dCanvas.style.overflow        = Overflow.Hidden;
            _p2dCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー（ビューレイヤー配下。プロファイルと同じ view で追従）
            _p2dViewLayer = new VisualElement();
            _p2dViewLayer.style.position = Position.Absolute;
            _p2dViewLayer.style.left = _p2dViewLayer.style.top =
            _p2dViewLayer.style.right = _p2dViewLayer.style.bottom = 0;
            _p2dViewLayer.pickingMode = PickingMode.Ignore;

            _p2dBgEl = new VisualElement();
            _p2dBgEl.style.position = Position.Absolute;
            _p2dBgEl.style.display  = DisplayStyle.None;
            _p2dBgEl.pickingMode    = PickingMode.Ignore;
            _p2dViewLayer.Add(_p2dBgEl);
            _p2dCanvas.Add(_p2dViewLayer);

            _p2dCanvas.generateVisualContent += OnDrawP2dCanvas;
            _p2dCanvas.RegisterCallback<PointerDownEvent>(OnP2dPointerDown);
            _p2dCanvas.RegisterCallback<PointerMoveEvent>(OnP2dPointerMove);
            _p2dCanvas.RegisterCallback<PointerUpEvent>(OnP2dPointerUp);
            _p2dCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_p2dBgMode)
                {
                    _p2dBgScale = Mathf.Clamp(_p2dBgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    _p2dBgScaleSlider?.SetValueWithoutNotify(_p2dBgScale);
                    UpdateP2dBgEl(); RefreshP2dCanvas();
                }
                else
                {
                    // 通常モードはプロファイルビューをカーソル基準でズーム。
                    float w = _p2dCanvas.resolvedStyle.width, h = _p2dCanvas.resolvedStyle.height;
                    float oldZoom = _p2dZoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 5f);
                    if (newZoom != oldZoom)
                    {
                        var m = (Vector2)e.localMousePosition;
                        var center = new Vector2(w * 0.5f, h * 0.5f);
                        float k = newZoom / oldZoom;
                        _p2dOffset = (m - center) * (1f - k) + _p2dOffset * k;
                        _p2dZoom   = newZoom;
                        UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
                    }
                }
                e.StopPropagation();
            });
            _p2dCanvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateP2dBgEl(); UpdateP2dView(); RefreshP2dCanvas();
            });
            pe.Add(_p2dCanvas);

            // キャンバス縦リサイズハンドル
            AddProfileResizeHandle(pe, _p2dCanvas, RefreshP2dCanvas);

            var p2dViewRow = new VisualElement(); p2dViewRow.style.flexDirection = FlexDirection.Row; p2dViewRow.style.marginBottom = 3;
            SB(p2dViewRow, T("ResetView"), () =>
            {
                _p2dZoom = 1f; _p2dOffset = Vector2.zero;
                UpdateP2dView(); UpdateP2dBgEl(); RefreshP2dCanvas();
            });
            var p2dLassoToggle = new Toggle(T("LassoMode")) { value = _p2dLassoMode };
            p2dLassoToggle.style.marginLeft = 4;
            p2dLassoToggle.RegisterValueChangedCallback(ev => _p2dLassoMode = ev.newValue);
            p2dViewRow.Add(p2dLassoToggle);
            var p2dGizmoTog = new Toggle(T("ShowGizmo")) { value = _p2dShowGizmo };
            p2dGizmoTog.style.marginLeft = 8;
            p2dGizmoTog.RegisterValueChangedCallback(ev => { _p2dShowGizmo = ev.newValue; RefreshP2dCanvas(); });
            p2dViewRow.Add(p2dGizmoTog);
            var p2dVertTog = new Toggle(T("ShowVertices")) { value = _p2dShowVerts };
            p2dVertTog.style.marginLeft = 8;
            p2dVertTog.RegisterValueChangedCallback(ev => { _p2dShowVerts = ev.newValue; RefreshP2dCanvas(); });
            p2dViewRow.Add(p2dVertTog);
            pe.Add(p2dViewRow);

            BuildP2dAnchorTransformUI(pe);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(pe,
                T("BgImage"),
                () => _p2dBgPath, v => _p2dBgPath = v,
                () => _p2dBgAlpha, v => { _p2dBgAlpha = v; UpdateP2dBgEl(); },
                () => _p2dBgMode,  v => { _p2dBgMode  = v; },
                () => _p2dBgScale, v => { _p2dBgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateP2dBgEl(); RefreshP2dCanvas(); },
                () => _p2dBgOrigin, v => { _p2dBgOrigin = v; UpdateP2dBgEl(); RefreshP2dCanvas(); },
                () => _p2dBgTex,
                () =>
                {
                    if (string.IsNullOrEmpty(_p2dBgPath)) return;
                    LoadBgTexture(_p2dBgPath, ref _p2dBgTex, _p2dBgEl);
                    _p2dBgOffset = Vector2.zero; _p2dBgScale = 8f;
                    if (_p2dBgTex != null)
                        _p2dBgOrigin = new Vector2(_p2dBgTex.width * 0.5f, _p2dBgTex.height * 0.5f);
                    _p2dBgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(_p2dBgSizeLabel, _p2dBgTex);
                    UpdateP2dBgEl();
                },
                () =>
                {
                    _p2dBgTex = null;
                    _p2dBgEl.style.display = DisplayStyle.None;
                    _p2dBgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(_p2dBgSizeLabel, null);
                },
                out _p2dBgScaleSlider, out _p2dBgSizeLabel);

            // ── ループ（操作ボタン・一覧をまとめる） ──────────────────────
            var loopFold = FoldSection(pe, T("Loops"), true);

            // ── ループ操作ボタン行 ────────────────────────────────────────
            var loopBtnRow = new VisualElement(); loopBtnRow.style.flexDirection = FlexDirection.Row; loopBtnRow.style.marginBottom = 3;
            SB(loopBtnRow, "◀", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop - 1 + _p2dLoops.Count) % _p2dLoops.Count;
                _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, "▶", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop + 1) % _p2dLoops.Count;
                _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("DeletePoint"), () =>
            {
                if (_p2dLoops == null) return;
                P2dBegin();
                if (_p2dSel.Count > 0)
                {
                    // 選択点をループ別にインデックス降順で一括削除（各ループ最小3点を維持）。
                    var byLoop = new Dictionary<int, List<int>>();
                    foreach (var k in _p2dSel)
                    {
                        int li = P2dKeyLoop(k), pi = P2dKeyPt(k);
                        if (li < 0 || li >= _p2dLoops.Count) continue;
                        if (!byLoop.TryGetValue(li, out var list)) { list = new List<int>(); byLoop[li] = list; }
                        list.Add(pi);
                    }
                    foreach (var kv in byLoop)
                    {
                        var lp2 = _p2dLoops[kv.Key];
                        kv.Value.Sort(); kv.Value.Reverse();
                        foreach (var pi in kv.Value)
                            if (pi >= 0 && pi < lp2.Points.Count && lp2.Points.Count > 3)
                                lp2.Points.RemoveAt(pi);
                    }
                    _p2dSel.Clear(); _p2dSelPt = -1;
                }
                else
                {
                    if (_p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                    var lp = _p2dLoops[_p2dSelLoop];
                    if (_p2dSelPt < 0 || _p2dSelPt >= lp.Points.Count || lp.Points.Count <= 3) return;
                    lp.Points.RemoveAt(_p2dSelPt);
                    _p2dSelPt = Mathf.Clamp(_p2dSelPt, 0, lp.Points.Count - 1);
                }
                P2dCommit("点削除");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("AddLoop"), () =>
            {
                if (_p2dLoops == null) return;
                P2dBegin();
                var lp = new Loop(); float r2 = 0.2f;
                lp.Points.AddRange(new[] {
                    new Vector2(-r2,-r2), new Vector2(r2,-r2),
                    new Vector2(r2, r2),  new Vector2(-r2, r2) });
                _p2dLoops.Add(lp);
                _p2dSelLoop = _p2dLoops.Count - 1; P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ追加");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("RemoveLoop"), () =>
            {
                if (_p2dLoops.Count <= 1 || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                P2dBegin();
                _p2dLoops.RemoveAt(_p2dSelLoop);
                _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);
                _p2dSel.Clear(); _p2dSelPt = -1; P2dCommit("ループ削除"); D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            loopFold.Add(loopBtnRow);

            // ── ループ操作ボタン行2（全選択 / 複製） ─────────────────────────
            var loopBtnRow2 = new VisualElement(); loopBtnRow2.style.flexDirection = FlexDirection.Row; loopBtnRow2.style.marginBottom = 3;
            SB(loopBtnRow2, T("SelectAllLoop"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                var lp = _p2dLoops[_p2dSelLoop];
                _p2dSel.Clear();
                for (int pi = 0; pi < lp.Points.Count; pi++) _p2dSel.Add(P2dKey(_p2dSelLoop, pi));
                _p2dSelPt = lp.Points.Count > 0 ? 0 : -1;
                RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow2, T("DuplicateLoop"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                P2dBegin();
                var src = _p2dLoops[_p2dSelLoop];
                var dup = new Loop(src);                    // 点列＋穴フラグを複製
                var ofs = new Vector2(0.1f, 0.1f);          // 少しずらして重なり回避
                for (int pi = 0; pi < dup.Points.Count; pi++) dup.Points[pi] += ofs;
                _p2dLoops.Add(dup);
                _p2dSelLoop = _p2dLoops.Count - 1;          // 複製先を選択
                P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ複製");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow2, T("FlipLoopH"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                var lp = _p2dLoops[_p2dSelLoop];
                if (lp.Points.Count < 2) return;
                P2dBegin();
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    var p = lp.Points[pi];
                    lp.Points[pi] = new Vector2(-p.x, p.y);   // Y軸(x=0)対称
                }
                lp.Points.Reverse();                        // 反転で逆転する巻き順を戻す
                P2dSelectAllInLoop(_p2dSelLoop);
                P2dCommit("ループ左右反転");
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            loopFold.Add(loopBtnRow2);

            // ── ループ一覧（穴フラグ切替） ─────────────────────────────────
            for (int i = 0; i < _p2dLoops.Count; i++)
            {
                int li = i;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
                var selBtn = new Button(() =>
                {
                    _p2dSelLoop = li; _p2dSel.Clear(); _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
                })
                { text = $"Loop {i} ({_p2dLoops[i].Points.Count}pt)" };
                selBtn.style.flexGrow = 1; selBtn.style.fontSize = 9; selBtn.style.height = 18;
                selBtn.style.backgroundColor = (i == _p2dSelLoop)
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                var holeTog = new Toggle(T("IsHole")) { value = _p2dLoops[i].IsHole };
                holeTog.RegisterValueChangedCallback(e => { _p2dLoops[li].IsHole = e.newValue; D(); RefreshP2dCanvas(); });
                row.Add(selBtn); row.Add(holeTog);
                loopFold.Add(row);
            }

            // ── 選択点スライダー ───────────────────────────────────────────
            _p2dPtRow = new VisualElement(); _p2dPtRow.style.marginBottom = 4;
            {
                Slider xSl = new Slider(-5f, 5f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 48;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    lp.Points[_p2dSelPt] = new Vector2(e.newValue, lp.Points[_p2dSelPt].y);
                    D(); RefreshP2dCanvas();
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => P2dBegin());
                xSl.RegisterCallback<PointerUpEvent>(_ => P2dCommit("点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    P2dBegin();
                    xSl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(e.newValue, lp.Points[_p2dSelPt].y);
                    D(); RefreshP2dCanvas();
                    P2dCommit("点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("X")); xRow.Add(xSl); xRow.Add(xFf);
                _p2dPtRow.Add(xRow);
                _p2dPtXSlider = xSl; _p2dPtXField = xFf;
            }
            {
                Slider ySl = new Slider(-5f, 5f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 48;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    lp.Points[_p2dSelPt] = new Vector2(lp.Points[_p2dSelPt].x, e.newValue);
                    D(); RefreshP2dCanvas();
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => P2dBegin());
                ySl.RegisterCallback<PointerUpEvent>(_ => P2dCommit("点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    P2dBegin();
                    ySl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(lp.Points[_p2dSelPt].x, e.newValue);
                    D(); RefreshP2dCanvas();
                    P2dCommit("点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _p2dPtRow.Add(yRow);
                _p2dPtYSlider = ySl; _p2dPtYField = yFf;
            }
            _p2dPtRow.style.display = DisplayStyle.None;
            pe.Add(_p2dPtRow);

            // ── CSV ────────────────────────────────────────────────────────
            var p2dCsvFold = FoldSection(pe, T("ProfileCsvSection"), false);
            var csvTf = new TextField();
            csvTf.RegisterValueChangedCallback(e => { _p2dCsvPath = e.newValue; RecentPaths.Set(P2dCsvKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadP2dCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), P2dCsvKey, _p2dCsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                _p2dCsvPath = sel;
                csvTf.value = _p2dCsvPath;

                try
                {
                    var lines = System.IO.File.ReadAllLines(_p2dCsvPath);
                    var loaded = ParseProfile2DCSV(lines);
                    if (loaded != null) { P2dBegin(); _p2dLoops = loaded; _p2dSelLoop = 0; _p2dSel.Clear(); _p2dSelPt = -1; P2dCommit("CSV読込"); D(); RebuildSettings(); }
                }
                catch (System.Exception ex) { Debug.LogWarning($"[P2D CSV] {ex.Message}"); }
            }

            p2dCsvFold.Add(PlayerIoUiKit.PathRow(csvTf, LoadP2dCsv));
            if (string.IsNullOrEmpty(_p2dCsvPath)) _p2dCsvPath = RecentPaths.Get(P2dCsvKey);
            if (!string.IsNullOrEmpty(_p2dCsvPath)) csvTf.SetValueWithoutNotify(_p2dCsvPath);

            p2dCsvFold.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadP2dCsv));
            p2dCsvFold.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                if (_p2dLoops == null) return;

                // パス欄は読込用。保存は毎回ダイアログを出す。
                // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
                string save = SaveDest.AskSavePath(
                    T("SaveCSV"), SaveDest.Keys.ProfileCsv, "", "profile2d.csv", "csv");
                if (string.IsNullOrEmpty(save)) return;
                _p2dCsvPath = save;
                csvTf.SetValueWithoutNotify(_p2dCsvPath);
                RecentPaths.Set(P2dCsvKey, _p2dCsvPath);
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var lp in _p2dLoops)
                    {
                        if (lp.IsHole && lp.Points.Count > 0)
                            sb.AppendLine($"{lp.Points[0].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[0].y.ToString(System.Globalization.CultureInfo.InvariantCulture)},hole");
                        else if (lp.Points.Count > 0)
                            sb.AppendLine($"{lp.Points[0].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[0].y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        for (int pi = 1; pi < lp.Points.Count; pi++)
                            sb.AppendLine($"{lp.Points[pi].x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lp.Points[pi].y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        sb.AppendLine();
                    }
                    System.IO.File.WriteAllText(_p2dCsvPath, sb.ToString());
                }
                catch (System.Exception ex) { Debug.LogWarning($"[P2D CSV] {ex.Message}"); }
            }));

            // ── メッシュ⇄プロファイル ─────────────────────────────────────
            var p2dIoFold = FoldSection(pe, T("MeshProfileIO"), false);
            var ioRow = new VisualElement(); ioRow.style.flexDirection = FlexDirection.Row; ioRow.style.marginBottom = 4;
            SB(ioRow, T("ImportFromMesh"), ImportProfile2DFromMesh);
            SB(ioRow, T("ApplyToMesh"),    ApplyProfile2DToMesh);
            p2dIoFold.Add(ioRow);

            // ── パラメータ ────────────────────────────────────────────────
            c.Add(SL(T("Scale")));
            c.Add(SR(T("Scale"), Profile2DParams.ScaleMin, Profile2DParams.ScaleMax, () => _p2dP.Scale,    v => { _p2dP.Scale    = v; D(); }));
            c.Add(SR(T("OffsetX"), Profile2DParams.OffsetMin, Profile2DParams.OffsetMax,  () => _p2dP.Offset.x, v => { _p2dP.Offset = new Vector2(v, _p2dP.Offset.y); D(); }));
            c.Add(SR(T("OffsetY"), Profile2DParams.OffsetMin, Profile2DParams.OffsetMax,  () => _p2dP.Offset.y, v => { _p2dP.Offset = new Vector2(_p2dP.Offset.x, v); D(); }));
            c.Add(TR(T("FlipY"),               () => _p2dP.FlipY,    v => { _p2dP.FlipY    = v; D(); }));
            c.Add(TR(T("SymmetryMode"),        () => _p2dP.SymmetryMode, v => { _p2dP.SymmetryMode = v; D(); }));
            c.Add(SR(T("Thickness"), Profile2DParams.ThicknessMin, Profile2DParams.ThicknessMax,   () => _p2dP.Thickness, v => { _p2dP.Thickness = v; D(); UpdateP2dEdgeVis(); }));

            // 角処理(ベベル)UI は常時生成し、Thickness/Segments に応じて表示切替
            // （ビルド時条件生成だと Thickness を後から上げても出ないため）
            _p2dEdgeLabel     = SL(T("EdgeSettings"));
            _p2dEdgeFrontSeg  = IR(T("FrontSegments"), Profile2DParams.EdgeSegmentsMin, Profile2DParams.EdgeSegmentsMax, () => _p2dP.SegmentsFront, v => { _p2dP.SegmentsFront = v; D(); UpdateP2dEdgeVis(); });
            _p2dEdgeFrontSize = SR(T("EdgeSize"), Profile2DParams.EdgeSizeMin, Profile2DParams.EdgeSizeMax, () => _p2dP.EdgeSizeFront, v => { _p2dP.EdgeSizeFront = v; D(); });
            _p2dEdgeBackSeg   = IR(T("BackSegments"), Profile2DParams.EdgeSegmentsMin, Profile2DParams.EdgeSegmentsMax, () => _p2dP.SegmentsBack, v => { _p2dP.SegmentsBack = v; D(); UpdateP2dEdgeVis(); });
            _p2dEdgeBackSize  = SR(T("EdgeSize"), Profile2DParams.EdgeSizeMin, Profile2DParams.EdgeSizeMax, () => _p2dP.EdgeSizeBack, v => { _p2dP.EdgeSizeBack = v; D(); });
            _p2dEdgeInward    = TR(T("EdgeInward"), () => _p2dP.EdgeInward, v => { _p2dP.EdgeInward = v; D(); });
            c.Add(_p2dEdgeLabel); c.Add(_p2dEdgeFrontSeg); c.Add(_p2dEdgeFrontSize);
            c.Add(_p2dEdgeBackSeg); c.Add(_p2dEdgeBackSize); c.Add(_p2dEdgeInward);
            UpdateP2dEdgeVis();

            BuildPivotXYZ(c,
                () => _p2dP.Pivot, v => { _p2dP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>角処理(ベベル)UI の表示を Thickness/Segments に応じて更新する。</summary>
        private void UpdateP2dEdgeVis()
        {
            if (_p2dEdgeLabel == null) return;
            bool thick = _p2dP.Thickness > 0.001f;
            _p2dEdgeLabel.style.display     = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeFrontSeg.style.display  = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeFrontSize.style.display = (thick && _p2dP.SegmentsFront > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeBackSeg.style.display   = thick ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeBackSize.style.display  = (thick && _p2dP.SegmentsBack > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            _p2dEdgeInward.style.display    = (thick && (_p2dP.SegmentsFront > 0 || _p2dP.SegmentsBack > 0)) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>P2D 下絵 VisualElement の位置・原点・スケール・アルファを更新</summary>
        private void UpdateP2dBgEl()
        {
            if (_p2dBgEl == null || _p2dBgTex == null) return;
            float cw = _p2dCanvas.resolvedStyle.width;
            float ch = _p2dCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _p2dBgTex.width;
            float bh = _p2dBgTex.height;
            if (bw < 0.5f || bh < 0.5f) return;

            // ジオメトリと同じワールド→キャンバス基準倍率（ズーム除く。ズームはビューレイヤーが付与）
            float baseScale = Mathf.Min(cw, ch) * 0.4f;
            // 画像の高さ = _p2dBgScale ワールド単位（アスペクト維持）
            float s = (_p2dBgScale * baseScale) / bh;

            // 画像中心をワールド点 _p2dBgOffset に合わせる（P2dWorldToCanvas の zoom=1,offset=0 相当）
            float ccx = cw * 0.5f + _p2dBgOffset.x * baseScale;
            float ccy = ch * 0.5f - _p2dBgOffset.y * baseScale;
            _p2dBgEl.style.left   = ccx - bw * 0.5f; _p2dBgEl.style.top = ccy - bh * 0.5f;
            _p2dBgEl.style.width  = bw; _p2dBgEl.style.height = bh;
            _p2dBgEl.style.transformOrigin = new TransformOrigin(
                new Length(bw * 0.5f, LengthUnit.Pixel), new Length(bh * 0.5f, LengthUnit.Pixel), 0f);
            _p2dBgEl.style.scale   = new Scale(new Vector3(s, s, 1f));
            _p2dBgEl.style.opacity = _p2dBgAlpha;
            _p2dBgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        private static List<Loop> ParseProfile2DCSV(string[] lines)
        {
            var loops = new List<Loop>();
            Loop current = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) { current = null; continue; }
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y)) continue;
                if (current == null)
                {
                    current = new Loop();
                    if (parts.Length >= 3 && parts[2].Trim().ToLower() == "hole") current.IsHole = true;
                    loops.Add(current);
                }
                current.Points.Add(new Vector2(x, y));
            }
            return loops.Count > 0 ? loops : null;
        }

        /// <summary>選択オブジェクトの全2頂点ラインを Profile2D ループへ取り込む。</summary>
        private void ImportProfile2DFromMesh()
        {
            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { _statusLabel.text = T("NoSelectedMesh"); return; }

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);
            var loops     = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
            if (loops == null || loops.Count == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            // 取り込み元を控える（回転体側と同じ理由）。
            _p2dProfileSrcIndex = ResolveMasterIndexOf(mesh);

            P2dBegin();
            _p2dLoops   = loops;
            _p2dSelLoop = 0;
            _p2dSel.Clear();
            _p2dSelPt   = -1;
            P2dCommit("メッシュ取込");
            _statusLabel.text = T("ImportedLoops", loops.Count);
            D(); RebuildSettings();
        }

        /// <summary>Profile2D ループを2頂点ラインの MeshObject として反映する。</summary>
        private void ApplyProfile2DToMesh()
        {
            EnsureP2DLoops();
            if (_p2dLoops == null || _p2dLoops.Count == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            var mo = LineProfileExtractor.LoopsToLineMesh(_p2dLoops, _p2dP.MeshName);
            if (mo == null || mo.FaceCount == 0) { _statusLabel.text = T("NoLinesFound"); return; }

            ApplyPoseForDirectMeshCreate(mo);

            _statusLabel.text = T("AppliedToMesh", mo.FaceCount);
            SendGeneratedMesh(mo, _p2dP.MeshName);
        }
    }
}
