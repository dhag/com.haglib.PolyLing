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
using Poly_Ling.Tools;
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

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        private void SendCmd(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

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

        private enum Interaction { Idle, Panning, PendingAction, MovingVertex, Marquee, AnchorDrag }
        private Interaction _interaction = Interaction.Idle;

        private Vector2 _mouseDownPos;
        private Vector2 _panStartOffset;

        // 矩形/投げ縄マーキー選択
        private readonly Canvas2DMarquee _marquee = new Canvas2DMarquee();
        private bool _lassoMode;        // true=投げ縄、false=矩形
        private bool _marqueeAdditive;  // Shiftドラッグ=追加

        private readonly HashSet<UVVertexId>              _selected    = new HashSet<UVVertexId>();
        private readonly Dictionary<UVVertexId, Vector2>  _dragStartUVs = new Dictionary<UVVertexId, Vector2>();
        private UVVertexId? _hitUV;
        private UVVertexId? _hovered;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _meshMatLabel;
        private DropdownField _materialDropdown;
        private Label         _warningLabel;
        private Label         _infoLabel;
        private Texture2D     _bgTexture;
        private Label         _statusLabel;
        private VisualElement _canvas;
        private VisualElement _transformSection;

        // プレビューキャンバスの縦サイズ（ドラッグで変更）
        private float _uvCanvasHeight = 300f;
        private bool  _uvResizeDragging;
        private float _uvResizeStartY;
        private float _uvResizeStartHeight;
        private const float UvCanvasMinHeight = 160f;
        private const float UvCanvasMaxHeight = 1000f;
        private FloatField    _moveU, _moveV, _scaleU, _scaleV, _rotateDeg;
        private FloatField    _scaleAxisDeg;   // スケール軸の回転角(°)

        // マグネット（比例編集）
        private readonly Canvas2DMagnet _uvMagnet = new Canvas2DMagnet();
        private readonly Dictionary<UVVertexId, float> _uvMagnetW = new Dictionary<UVVertexId, float>();
        private Slider        _uvMagnetRadius;

        // 回転/拡大縮小アンカー（UV空間 0-1）
        private Vector2       _anchor = new Vector2(0.5f, 0.5f);
        private bool          _anchorManual;   // true=手動固定（重心へ自動追従しない）
        private bool          _anchorMode;     // アンカー設定サブモード
        private Slider        _anchorXSlider, _anchorYSlider;
        private FloatField    _anchorXField,  _anchorYField;
        private Button        _anchorEnterBtn;
        private VisualElement _anchorPanel;
        private bool          _anchorSuppress; // フィールド更新中の通知抑制

        // 頂点が存在するマテリアルのインデックスリスト（MeshContext.GetMaterial用）
        private readonly List<int> _matsWithVerts = new List<int>();
        private int _selectedMatListIndex = 0;

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

            // メッシュ名・マテリアル名ラベル
            _meshMatLabel = new Label();
            _meshMatLabel.style.fontSize    = 10;
            _meshMatLabel.style.marginBottom = 2;
            _meshMatLabel.style.whiteSpace  = WhiteSpace.Normal;
            _root.Add(_meshMatLabel);

            // マテリアル選択ドロップダウン
            _materialDropdown = new DropdownField("Material");
            _materialDropdown.style.fontSize   = 10;
            _materialDropdown.style.marginBottom = 2;
            _materialDropdown.RegisterValueChangedCallback(_ =>
            {
                _selectedMatListIndex = _materialDropdown.index;
                RefreshCanvasBackground(GetMeshContext());
                _canvas?.MarkDirtyRepaint();
            });
            _root.Add(_materialDropdown);

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _root.Add(_warningLabel);

            // 情報ラベル
            _infoLabel = new Label();
            _infoLabel.style.color = new StyleColor(Color.white);
            _infoLabel.style.fontSize    = 10;
            _infoLabel.style.marginBottom = 2;
            _root.Add(_infoLabel);

            // キャンバス
            _canvas = new VisualElement();
            _canvas.style.height          = _uvCanvasHeight;   // 縦サイズはハンドルで可変
            _canvas.style.flexShrink      = 0;
            _canvas.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            _canvas.style.marginBottom    = 4;
            _canvas.style.overflow        = Overflow.Hidden;   // 頂点/線分をキャンバス内にクリップ
            _canvas.generateVisualContent += OnGenerateVisualContent;
            _canvas.RegisterCallback<WheelEvent>(OnCanvasWheel);
            _canvas.RegisterCallback<MouseDownEvent>(OnCanvasMouseDown);
            _canvas.RegisterCallback<MouseMoveEvent>(OnCanvasMouseMove);
            _canvas.RegisterCallback<MouseUpEvent>(OnCanvasMouseUp);
            _canvas.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (_hovered.HasValue) { _hovered = null; _canvas.MarkDirtyRepaint(); }
            });
            _canvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateCanvasBackground();
                _canvas.MarkDirtyRepaint();
            });
            _root.Add(_canvas);

            // 縦リサイズハンドル（ドラッグでプレビュー高さを変更）
            AddUvResizeHandle(_root);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            MkBtn("フィット",      btnRow, FitToUVBounds);
            MkBtn("全選択",        btnRow, SelectAll);
            MkBtn("選択解除",      btnRow, ClearSelection);
            var lassoToggle = new Toggle("投げ縄") { value = false };
            lassoToggle.style.marginLeft = 4;
            lassoToggle.RegisterValueChangedCallback(e => _lassoMode = e.newValue);
            btnRow.Add(lassoToggle);
            _root.Add(btnRow);

            // 変換セクション
            _transformSection = new VisualElement();
            _root.Add(_transformSection);

            _transformSection.Add(SecLabel("UV変換"));

            _transformSection.Add(FR2("移動 U", "V", 0f, 0f,   out _moveU,    out _moveV));
            _transformSection.Add(FR2("スケール U", "V", 1f, 1f, out _scaleU,  out _scaleV));
            _transformSection.Add(FR1("スケール軸 (°)", 0f,      out _scaleAxisDeg));
            _transformSection.Add(FR1("回転 (°)",  0f,          out _rotateDeg));

            // ── 回転/拡大縮小アンカー ──────────────────────────────────
            _transformSection.Add(SecLabel("回転/拡大縮小アンカー"));
            _anchorEnterBtn = new Button(() => SetAnchorMode(true)) { text = "アンカー設定" };
            _anchorEnterBtn.style.height = 22; _anchorEnterBtn.style.fontSize = 10;
            _anchorEnterBtn.style.marginBottom = 2;
            _transformSection.Add(_anchorEnterBtn);

            _anchorPanel = new VisualElement();
            _anchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var adjLbl = new Label("アンカー調整中（キャンバスをドラッグで移動）");
                adjLbl.style.fontSize = 10; adjLbl.style.flexGrow = 1; adjLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var doneBtn = new Button(() => SetAnchorMode(false)) { text = "決定" };
                doneBtn.style.width = 60; doneBtn.style.height = 22; doneBtn.style.fontSize = 10;
                headRow.Add(adjLbl); headRow.Add(doneBtn);
                _anchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                MkBtn("重心", presetRow, () => ApplyAnchorPreset(AnchorPreset.Centroid));
                MkBtn("中心", presetRow, () => ApplyAnchorPreset(AnchorPreset.Center));
                MkBtn("左上", presetRow, () => ApplyAnchorPreset(AnchorPreset.TopLeft));
                MkBtn("左下", presetRow, () => ApplyAnchorPreset(AnchorPreset.BottomLeft));
                _anchorPanel.Add(presetRow);

                _anchorPanel.Add(BuildAnchorRow("X", 0f, out _anchorXSlider, out _anchorXField,
                    v => SetAnchorComponent(true, v)));
                _anchorPanel.Add(BuildAnchorRow("Y", 0f, out _anchorYSlider, out _anchorYField,
                    v => SetAnchorComponent(false, v)));
            }
            _transformSection.Add(_anchorPanel);
            RefreshAnchorModeUI();
            RefreshAnchorFields();

            // マグネット（比例編集）
            _uvMagnet.Radius = 0.15f;
            _transformSection.Add(SecLabel("マグネット（比例編集）"));
            var uvMagRow = new VisualElement(); uvMagRow.style.flexDirection = FlexDirection.Row; uvMagRow.style.marginBottom = 2;
            var uvMagToggle = new Toggle("有効") { value = _uvMagnet.Enabled }; uvMagToggle.style.marginRight = 6;
            uvMagToggle.RegisterValueChangedCallback(ev => { _uvMagnet.Enabled = ev.newValue; _canvas.MarkDirtyRepaint(); });
            var uvFalloff = new EnumField(_uvMagnet.Falloff); uvFalloff.style.flexGrow = 1;
            uvFalloff.RegisterValueChangedCallback(ev => _uvMagnet.Falloff = (FalloffType)ev.newValue);
            uvMagRow.Add(uvMagToggle); uvMagRow.Add(uvFalloff);
            _transformSection.Add(uvMagRow);
            _transformSection.Add(BuildAnchorRow("半径", 0.15f, out _uvMagnetRadius, out _,
                v => { _uvMagnet.Radius = v; _canvas.MarkDirtyRepaint(); }));

            var applyRow = new VisualElement();
            applyRow.style.flexDirection = FlexDirection.Row;
            applyRow.style.marginTop     = 4;
            MkBtn("変換適用",  applyRow, ApplyTransform);
            MkBtn("パラメータリセット", applyRow, ResetParams);
            _transformSection.Add(applyRow);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 4;
            _root.Add(_statusLabel);
        }

        // ================================================================
        // 外部から呼ぶ更新
        // ================================================================

        public void Refresh()
        {
            var mc     = GetMeshContext();
            var mo     = mc?.MeshObject;
            bool hasMesh = mo != null;

            // メッシュ名ラベル
            if (_meshMatLabel != null)
            {
                if (hasMesh)
                {
                    _meshMatLabel.text  = mc.Name ?? "";
                    _meshMatLabel.style.color = new StyleColor(Color.white);
                }
                else
                {
                    _meshMatLabel.text  = "メッシュが未選択です";
                    _meshMatLabel.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
                }
            }

            // 頂点が存在するマテリアルリストを構築してドロップダウン更新
            _matsWithVerts.Clear();
            if (hasMesh) BuildMatsWithVerts(mo, mc);
            if (_materialDropdown != null)
            {
                var choices = new System.Collections.Generic.List<string>();
                foreach (var mi in _matsWithVerts)
                {
                    var mat = mc.GetMaterial(mi);
                    choices.Add(mat != null ? $"[{mi}] {mat.name}" : $"[{mi}]");
                }
                _materialDropdown.choices = choices;
                _materialDropdown.style.display = (_matsWithVerts.Count > 0) ? DisplayStyle.Flex : DisplayStyle.None;
                if (_selectedMatListIndex >= _matsWithVerts.Count) _selectedMatListIndex = 0;
                if (choices.Count > 0)
                    _materialDropdown.SetValueWithoutNotify(choices[_selectedMatListIndex]);
            }

            // 警告ラベル（旧）は非表示に統一
            if (_warningLabel != null)
                _warningLabel.style.display = DisplayStyle.None;

            if (_transformSection != null)
                _transformSection.style.display = hasMesh ? DisplayStyle.Flex : DisplayStyle.None;

            // キャンバス背景
            RefreshCanvasBackground(mc);

            UpdateInfo(mo);
            UpdateCanvasBackground();
            _canvas?.MarkDirtyRepaint();
        }

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
            else if (_interaction == Interaction.Marquee) { ApplyMarqueeSelection(); _marquee.End(); }
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

        // ================================================================
        // UV移動
        // ================================================================

        private void BeginUVMove()
        {
            _dragStartUVs.Clear();
            _uvMagnetW.Clear();
            var mo = GetMeshObject();
            if (mo == null) return;
            var selPos = new List<Vector2>();
            foreach (var id in _selected)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                {
                    _dragStartUVs[id] = v.UVs[id.UVIndex];
                    _uvMagnetW[id]    = 1f;
                    selPos.Add(v.UVs[id.UVIndex]);
                }
            }
            // マグネット影響頂点（非選択で半径内）
            if (_uvMagnet.Enabled && selPos.Count > 0)
            {
                var tested = new HashSet<long>();
                foreach (var face in mo.Faces)
                {
                    if (face == null) continue;
                    if (_matsWithVerts.Count > 0 && face.MaterialIndex != _matsWithVerts[_selectedMatListIndex]) continue;
                    for (int ci = 0; ci < face.VertexCount; ci++)
                    {
                        int vi = face.VertexIndices[ci];
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                        long key = ((long)vi << 32) | (uint)ui;
                        if (!tested.Add(key)) continue;
                        var id = new UVVertexId(vi, ui);
                        if (_dragStartUVs.ContainsKey(id)) continue;
                        var v = mo.Vertices[vi];
                        if (ui < 0 || ui >= v.UVs.Count) continue;
                        float wt = _uvMagnet.WeightFor(v.UVs[ui], selPos);
                        if (wt > 0f) { _dragStartUVs[id] = v.UVs[ui]; _uvMagnetW[id] = wt; }
                    }
                }
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
                {
                    float wt = _uvMagnetW.TryGetValue(id, out var mw) ? mw : 1f;
                    v.UVs[id.UVIndex] = kv.Value + d * wt;
                }
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

            // 変更後UV を収集
            var keys = new List<UVVertexId>(_dragStartUVs.Keys);
            var afterUVs = new Vector2[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var id = keys[i];
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) { afterUVs[i] = _dragStartUVs[id]; continue; }
                var v = mo.Vertices[id.VertexIndex];
                afterUVs[i] = (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    ? v.UVs[id.UVIndex] : _dragStartUVs[id];
            }

            // ドラッグ中の変更を元に戻す（コマンドが AfterUVs を再適用する）
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    v.UVs[id.UVIndex] = kv.Value;
            }

            // コマンド送信
            if (_panelContext != null)
            {
                var mc = GetMeshContext();
                var model = GetModel?.Invoke();
                int masterIdx = mc != null && model != null ? model.IndexOf(mc) : 0;
                int modelIdx  = _getModelIndex?.Invoke() ?? 0;
                var viArr     = new int   [keys.Count];
                var uiArr     = new int   [keys.Count];
                var beforeArr = new Vector2[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    viArr[i]     = keys[i].VertexIndex;
                    uiArr[i]     = keys[i].UVIndex;
                    beforeArr[i] = _dragStartUVs[keys[i]];
                }
                SendCmd(new ApplyUVChangesCommand(modelIdx, masterIdx,
                    viArr, uiArr, beforeArr, afterUVs, $"UV Move {keys.Count}V"));
            }
            else
            {
                // フォールバック（PanelContext 未設定時）
                RecordTopologyChange($"UV Move {keys.Count}V", obj =>
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var id = keys[i];
                        if (id.VertexIndex < 0 || id.VertexIndex >= obj.VertexCount) continue;
                        var v = obj.Vertices[id.VertexIndex];
                        if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                            v.UVs[id.UVIndex] = afterUVs[i];
                    }
                });
            }

            SetStatus($"UV {keys.Count}頂点を移動");
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
            if (!any) { _zoom = 1f; _panOffset = Vector2.zero; UpdateCanvasBackground(); _canvas.MarkDirtyRepaint(); return; }

            var rect = _canvas.contentRect;
            if (rect.width < 1) return;
            Vector2 ctr  = (uvMin + uvMax) * 0.5f;
            float ext    = Mathf.Max((uvMax - uvMin).x, (uvMax - uvMin).y);
            if (ext < 0.0001f) ext = 1f;
            float csize  = Mathf.Min(rect.width, rect.height);
            _zoom        = Mathf.Clamp((csize * 0.9f) / (csize * ext), MinZoom, MaxZoom);
            float sz     = csize * _zoom;
            _panOffset   = new Vector2(-(ctr.x - 0.5f) * sz, (ctr.y - 0.5f) * sz);
            UpdateCanvasBackground();
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
            UpdateInfo(mo); RefreshAnchorAuto(); _canvas.MarkDirtyRepaint();
        }

        private void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            UpdateInfo(GetMeshObject()); RefreshAnchorAuto(); _canvas.MarkDirtyRepaint();
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
            float saDeg = _scaleAxisDeg?.value ?? 0f;

            if (Mathf.Approximately(mu, 0f) && Mathf.Approximately(mv, 0f) &&
                Mathf.Approximately(su, 1f) && Mathf.Approximately(sv, 1f) &&
                Mathf.Approximately(deg, 0f)) { SetStatus("変換パラメータが初期値です"); return; }

            var targets = _selected.Count > 0 ? new HashSet<UVVertexId>(_selected) : CollectAllUVVertices(mo);
            RefreshAnchorAuto();          // 自動モードなら重心に更新
            var pivot   = _anchor;        // 可変アンカーを基準に使用

            // 重みマップ（選択=1、マグネット影響=weight、選択なし=全点1）
            var weights = new Dictionary<UVVertexId, float>();
            if (_selected.Count > 0)
            {
                var selPos = new List<Vector2>();
                foreach (var id in _selected)
                {
                    weights[id] = 1f;
                    if (id.VertexIndex >= 0 && id.VertexIndex < mo.VertexCount)
                    {
                        var vv = mo.Vertices[id.VertexIndex];
                        if (id.UVIndex >= 0 && id.UVIndex < vv.UVs.Count) selPos.Add(vv.UVs[id.UVIndex]);
                    }
                }
                if (_uvMagnet.Enabled && selPos.Count > 0)
                {
                    foreach (var id in CollectAllUVVertices(mo))
                    {
                        if (weights.ContainsKey(id)) continue;
                        if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                        var vv = mo.Vertices[id.VertexIndex];
                        if (id.UVIndex < 0 || id.UVIndex >= vv.UVs.Count) continue;
                        float wt = _uvMagnet.WeightFor(vv.UVs[id.UVIndex], selPos);
                        if (wt > 0f) weights[id] = wt;
                    }
                }
            }
            else
            {
                foreach (var id in targets) weights[id] = 1f;
            }
            targets = new HashSet<UVVertexId>(weights.Keys);

            if (_panelContext != null)
            {
                // コマンド経由：before/after を収集してから送信
                var mc    = GetMeshContext();
                var model = GetModel?.Invoke();
                int masterIdx = mc != null && model != null ? model.IndexOf(mc) : 0;
                int modelIdx  = _getModelIndex?.Invoke() ?? 0;

                var keyList   = new List<UVVertexId>(targets);
                var beforeArr = new Vector2[keyList.Count];
                var afterArr  = new Vector2[keyList.Count];
                var viArr     = new int   [keyList.Count];
                var uiArr     = new int   [keyList.Count];

                for (int i = 0; i < keyList.Count; i++)
                {
                    var id = keyList[i];
                    viArr[i] = id.VertexIndex;
                    uiArr[i] = id.UVIndex;
                    if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) { beforeArr[i] = afterArr[i] = Vector2.zero; continue; }
                    var v = mo.Vertices[id.VertexIndex];
                    beforeArr[i] = (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count) ? v.UVs[id.UVIndex] : Vector2.zero;
                }

                // after を計算（実際には MeshObject に書き込まない）
                var tempAfter = new Dictionary<UVVertexId, Vector2>();
                ApplyUVTransformToDict(mo, weights, mu, mv, su, sv, deg, pivot, saDeg, tempAfter);
                for (int i = 0; i < keyList.Count; i++)
                    afterArr[i] = tempAfter.TryGetValue(keyList[i], out var uv) ? uv : beforeArr[i];

                SendCmd(new ApplyUVChangesCommand(modelIdx, masterIdx,
                    viArr, uiArr, beforeArr, afterArr, "UV Transform"));
            }
            else
            {
                RecordTopologyChange("UV Transform", obj =>
                    ApplyUVTransform(obj, weights, mu, mv, su, sv, deg, pivot, saDeg));
            }

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
            if (_scaleAxisDeg != null) _scaleAxisDeg.value = 0f;
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

        // ================================================================
        // 回転/拡大縮小アンカー
        // ================================================================

        private enum AnchorPreset { Centroid, Center, TopLeft, BottomLeft }

        private VisualElement BuildAnchorRow(string label, float val,
            out Slider slider, out FloatField field, Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(label + ":"); lb.style.width = 16; lb.style.fontSize = 10;
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;

            var sl = new Slider(0f, 1f) { value = Mathf.Clamp01(val) };
            sl.style.flexGrow = 1; sl.style.marginRight = 3;
            var ff = new FloatField { value = val };
            ff.style.color = new StyleColor(Color.black); ff.style.width = 52;

            sl.RegisterValueChangedCallback(e => { if (!_anchorSuppress) onChange(e.newValue); });
            ff.RegisterValueChangedCallback(e => { if (!_anchorSuppress) onChange(e.newValue); });

            row.Add(lb); row.Add(sl); row.Add(ff);
            slider = sl; field = ff;
            return row;
        }

        private void SetAnchorMode(bool on)
        {
            _anchorMode = on;
            if (on) RefreshAnchorAuto();  // 入る時点の重心を初期表示
            RefreshAnchorModeUI();
            _canvas?.MarkDirtyRepaint();
        }

        private void RefreshAnchorModeUI()
        {
            if (_anchorEnterBtn != null) _anchorEnterBtn.style.display = _anchorMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_anchorPanel    != null) _anchorPanel.style.display    = _anchorMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>アンカー数値フィールド/スライダーを _anchor に同期（通知抑制）。</summary>
        private void RefreshAnchorFields()
        {
            _anchorSuppress = true;
            _anchorXSlider?.SetValueWithoutNotify(Mathf.Clamp01(_anchor.x));
            _anchorYSlider?.SetValueWithoutNotify(Mathf.Clamp01(_anchor.y));
            _anchorXField?.SetValueWithoutNotify(_anchor.x);
            _anchorYField?.SetValueWithoutNotify(_anchor.y);
            _anchorSuppress = false;
        }

        /// <summary>手動固定でなければ選択の重心へ追従。</summary>
        private void RefreshAnchorAuto()
        {
            if (_anchorManual) return;
            var mo = GetMeshObject();
            if (mo == null) return;
            var targets = _selected.Count > 0 ? _selected : CollectAllUVVertices(mo);
            _anchor = ComputeUVPivot(mo, targets);
            RefreshAnchorFields();
        }

        private void SetAnchorComponent(bool isX, float v)
        {
            if (isX) _anchor.x = v; else _anchor.y = v;
            _anchorManual = true;
            RefreshAnchorFields();
            _canvas?.MarkDirtyRepaint();
        }

        private void ApplyAnchorPreset(AnchorPreset preset)
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            var targets = _selected.Count > 0 ? _selected : CollectAllUVVertices(mo);

            if (preset == AnchorPreset.Centroid)
            {
                _anchor = ComputeUVPivot(mo, targets);
                _anchorManual = false;   // 重心＝自動追従に戻す
                RefreshAnchorFields();
                _canvas?.MarkDirtyRepaint();
                return;
            }

            // バウンディングから中心/左上/左下
            bool any = false;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var vv = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vv.UVs.Count) continue;
                var uv = vv.UVs[id.UVIndex];
                minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y);
                any = true;
            }
            if (!any) return;

            switch (preset)
            {
                case AnchorPreset.Center:     _anchor = new Vector2((minU + maxU) * 0.5f, (minV + maxV) * 0.5f); break;
                case AnchorPreset.TopLeft:    _anchor = new Vector2(minU, maxV); break;  // UV上=大きいV
                case AnchorPreset.BottomLeft: _anchor = new Vector2(minU, minV); break;
            }
            _anchorManual = true;
            RefreshAnchorFields();
            _canvas?.MarkDirtyRepaint();
        }

        private static void ApplyUVTransform(MeshObject mo, Dictionary<UVVertexId, float> weights,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot, float saDeg)
        {
            float saRad = saDeg * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);
            foreach (var kv in weights)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                v.UVs[id.UVIndex] = CalcTransformedUV(v.UVs[id.UVIndex], pivot, mu, mv, su, sv, deg, saCos, saSin, kv.Value);
            }
        }

        /// <summary>before/after を計算するだけで MeshObject を変更しない。コマンド化用。</summary>
        private static void ApplyUVTransformToDict(MeshObject mo, Dictionary<UVVertexId, float> weights,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot, float saDeg,
            Dictionary<UVVertexId, Vector2> result)
        {
            float saRad = saDeg * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);
            foreach (var kv in weights)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                result[id] = CalcTransformedUV(v.UVs[id.UVIndex], pivot, mu, mv, su, sv, deg, saCos, saSin, kv.Value);
            }
        }

        private static Vector2 CalcTransformedUV(Vector2 uv, Vector2 pivot,
            float mu, float mv, float su, float sv, float deg, float saCos, float saSin, float w)
        {
            float suw = 1f + (su - 1f) * w, svw = 1f + (sv - 1f) * w;
            float degw = deg * w * Mathf.Deg2Rad;
            float cos = Mathf.Cos(degw), sin = Mathf.Sin(degw);
            float dx = uv.x - pivot.x, dy = uv.y - pivot.y;
            // スケール軸フレームへ回転(-φ) → 重み付きスケール → 戻す(+φ)
            float rx =  dx * saCos + dy * saSin;
            float ry = -dx * saSin + dy * saCos;
            rx *= suw; ry *= svw;
            dx = rx * saCos - ry * saSin;
            dy = rx * saSin + ry * saCos;
            // 重み付き全体回転
            float ex = dx * cos - dy * sin;
            float ey = dx * sin + dy * cos;
            uv.x = pivot.x + ex + mu * w;
            uv.y = pivot.y + ey + mv * w;
            return uv;
        }

        // ================================================================
        // Undo
        // ================================================================

        private void RecordTopologyChange(string opName, Action<MeshObject> action)
        {
            var model = GetModel?.Invoke();
            var mc    = model?.FirstDrawableMeshContext;
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

        /// <summary>頂点が存在する（面で参照されている）マテリアルのインデックスリストを構築する</summary>
        private void BuildMatsWithVerts(MeshObject mo, MeshContext mc)
        {
            var seen = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                seen.Add(face.MaterialIndex);
            }
            var sorted = new List<int>(seen);
            sorted.Sort();
            foreach (var mi in sorted)
                _matsWithVerts.Add(mi);
        }

        /// <summary>現在選択中のマテリアルに基づいてキャンバス背景を更新する</summary>
        private void RefreshCanvasBackground(MeshContext mc)
        {
            if (_canvas == null) return;
            bool hasMesh = mc?.MeshObject != null;
            Texture2D tex = null;
            Material mat = null;
            if (hasMesh && _matsWithVerts.Count > 0)
            {
                int matIdx = _matsWithVerts[_selectedMatListIndex];
                mat = mc.GetMaterial(matIdx);
                tex = mat?.mainTexture as Texture2D;
            }
            if (tex != null)
            {
                _bgTexture = tex;
                _canvas.style.backgroundImage = new StyleBackground(tex);
                _canvas.style.backgroundColor = new StyleColor(Color.clear);
            }
            else
            {
                _bgTexture = null;
                _canvas.style.backgroundImage = StyleKeyword.None;
                if (mat != null)
                {
                    Color col = mat.color; col.a = 1f;
                    _canvas.style.backgroundColor = new StyleColor(col);
                }
                else
                {
                    _canvas.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
                }
            }
        }

        private void UpdateCanvasBackground()
        {
            if (_canvas == null) return;
            if (_bgTexture == null) return;
            var rect = _canvas.contentRect;
            if (rect.width < 1f) return;

            float size = Mathf.Min(rect.width, rect.height) * _zoom;
            float cx   = rect.width  * 0.5f + _panOffset.x;
            float cy   = rect.height * 0.5f + _panOffset.y;

            // UV(0,1) = テクスチャ左上 → キャンバス座標
            float left = cx - size * 0.5f;
            float top  = cy - size * 0.5f;

            _canvas.style.backgroundSize =
                new BackgroundSize(new Length(size, LengthUnit.Pixel),
                                   new Length(size, LengthUnit.Pixel));
            _canvas.style.backgroundPositionX =
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(left, LengthUnit.Pixel));
            _canvas.style.backgroundPositionY =
                new BackgroundPosition(BackgroundPositionKeyword.Top,  new Length(top,  LengthUnit.Pixel));
        }

        private MeshContext GetMeshContext() =>
            GetModel?.Invoke()?.FirstDrawableMeshContext;

        private MeshObject GetMeshObject() =>
            GetModel?.Invoke()?.FirstDrawableMeshContext?.MeshObject;

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
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(lb);
            f1 = new FloatField(l1.Split(' ').Length > 1 ? l1.Split(' ')[1] : "") { value = v1 };
            f1.style.color = new StyleColor(Color.black);
            f1.style.flexGrow = 1;
            f2 = new FloatField(l2) { value = v2 };
            f2.style.color = new StyleColor(Color.black);
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
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            field = new FloatField { value = val };
            field.style.color = new StyleColor(Color.black);
            field.style.flexGrow = 1;
            row.Add(lb); row.Add(field);
            return row;
        }
    }
}
