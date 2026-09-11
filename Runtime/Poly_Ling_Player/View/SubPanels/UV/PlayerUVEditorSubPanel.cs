// PlayerUVEditorSubPanel.cs
// UVエディタサブパネル（Player ビルド用）。
// UVEditPanel の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【分割先】このファイルから次へ分けてある。
//   PlayerUVEditorSubPanel.Canvas.cs   UV エディタ：プレビューのリサイズ・座標変換・ヒットテスト・キャンバス描画・キャンバス入力。
//   PlayerUVEditorSubPanel.Edit.cs     UV エディタ：UV 移動・ハンドルドラッグ・Fit／選択操作・一括変換・アンカー。
//   PlayerUVEditorSubPanel.Helpers.cs  UV エディタ：Undo とヘルパー。
//   UVVertexId.cs                      UV 頂点識別子（PlayerUVEditorSubPanel から分離）。

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
    // UV エディタサブパネル
    // ================================================================

    public partial class PlayerUVEditorSubPanel
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

        private enum Interaction { Idle, Panning, PendingAction, MovingVertex, Marquee, AnchorDrag, HandleDrag }
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

        // 回転/拡大縮小ハンドル（キャンバス上ドラッグ）
        private readonly Canvas2DHandle _uvHandle = new Canvas2DHandle();
        private Canvas2DHandle.HandleType _uvHandleType = Canvas2DHandle.HandleType.None;
        private readonly Dictionary<UVVertexId, Vector2> _uvHandleStart = new Dictionary<UVVertexId, Vector2>();
        private readonly Dictionary<UVVertexId, float>   _uvHandleW     = new Dictionary<UVVertexId, float>();
        private Vector2 _uvHandleAnchorC;
        private float   _uvHandlePrevAngle;
        private float   _uvHandleTotalDeg;

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
    }
}
