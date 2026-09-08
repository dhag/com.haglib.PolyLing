// PlayerMcpSandboxSubPanel.cs
// MCP用サンドボックスのサブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/McpSandbox/ に配置
//
// 【何のためのパネルか】
//   図形生成を試作するための、図形生成パネルと同じ形の独立した枠組み。
//   PlayerPrimitiveMeshSubPanel には一切触れずに図形を足せるので、
//   試作が本番側の ShapeKind 列挙や図形グリッドを壊すことがない。
//
// 【本番側と何を共有し、何を分けたか】
//   共有：コマンド経路（PanelCommand → ディスパッチャ）、生成後の後処理
//         （PrimitiveMeshFactory）、プレビュー用カメラ（PrimitivePreviewViewport）。
//         ここを分けると、サンドボックスで動いたものが本番で動く保証を失う。
//   分離：図形の列挙、パラメータ構造体、表示文字列、行ヘルパ。
//         試作でいじる場所はすべてこちら側に閉じている。
//
// 【生成は必ずコマンド経由】
//   プレビューも生成ボタンも同じ CreateMcpCylinderCommand を通す。
//   パネルから Ops を直接叩く経路を作らないのは本番側と同じ理由で、
//   その経路があるとディスパッチャ側の欠陥が検査を素通りするため。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Core;
using static Poly_Ling.Player.McpSandboxTexts;

namespace Poly_Ling.Player
{
    public class PlayerMcpSandboxSubPanel
    {
        /// <summary>
        /// サンドボックスの図形種別。図形を足すときはここへ足し、
        /// PopulateShapeGrid / RebuildSettings / BuildCreateCommand の 3 箇所を追う。
        /// 本番側と違い添字で引く配列は持たないので、並び順に意味はない。
        /// </summary>
        public enum ShapeKind
        {
            None,
            McpCylinder,
        }

        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>コマンド送信。図形生成パネルと同じ経路へ流す。</summary>
        public Action<PanelCommand> SendCommand;

        /// <summary>現在のモデル索引。コマンドに載せる。未配線なら 0。</summary>
        public Func<int> GetModelIndex;

        private int ModelIndex() => GetModelIndex?.Invoke() ?? 0;

        // ---- メイン3Dウインドウへのライブワイヤ（3D連携） ----
        // パネル内のプレビューRTとは別物。RT はこのパネルの中にしか出ないので、
        // 3Dウインドウで生成予定の形と位置を見るにはこちらの提出経路が要る。

        /// <summary>true のとき、生成予定形状の黄色ワイヤをメイン3Dウインドウへ描画する。</summary>
        public bool LiveWireInMainViewport { get; set; }

        /// <summary>
        /// メイン3Dウインドウ（4ビューポート）のカメラかを判定する。
        /// 未設定ならライブワイヤは描画しない（どのカメラにも出さない）。
        /// </summary>
        public Func<Camera, bool> IsMainViewportCamera;

        /// <summary>
        /// 追加先モードが AddToExisting のときの、追加先 MeshContext のワールド行列。
        /// 未設定なら単位行列として扱う。
        /// </summary>
        public Func<Matrix4x4> GetAddTargetWorldMatrix;

        /// <summary>メイン3Dウインドウへ描く黄色ワイヤ用マテリアル。初回描画時に遅延生成する。</summary>
        private Material _liveWireMat;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _sectionEl;
        private VisualElement _shapeGrid;
        private VisualElement _settingsContainer;
        private VisualElement _previewEl;
        private Label         _statusLabel;
        private Button        _createBtn;

        private readonly Dictionary<ShapeKind, Button> _shapeBtns = new Dictionary<ShapeKind, Button>();

        private static readonly Color ShapeBtnOn  = new Color(0.25f, 0.45f, 0.70f);
        private static readonly Color ShapeBtnOff = new Color(0.22f, 0.22f, 0.25f);

        // ================================================================
        // プレビュー
        // ================================================================

        private PrimitivePreviewViewport _preview;
        private Mesh   _wireMesh;
        private bool   _dirty;
        private double _nextGenAllowed;

        private float _previewHeight = 160f;
        private const float PreviewMinHeight = 80f;
        private const float PreviewMaxHeight = 480f;

        private const float DragThreshold = 3f;
        private bool    _mouseDragging;
        private int     _mouseBtn;
        private Vector2 _mouseDownPos;
        private Vector2 _mousePrevPos;

        private bool  _resizeDragging;
        private float _resizeStartY;
        private float _resizeStartHeight;

        // ================================================================
        // 状態
        // ================================================================

        private ShapeKind _current = ShapeKind.None;
        private PrimitiveAddMode _addMode = PrimitiveAddMode.NewObject;
        private Vector3 _worldPos = Vector3.zero;

        /// <summary>底面を原点に置く。図形生成パネルの円柱と同じ既定。</summary>
        private static McpCylinderMeshGenerator.McpCylinderParams DefaultCylinderParams()
        {
            var p = McpCylinderMeshGenerator.McpCylinderParams.Default;
            p.Pivot = new Vector3(0f, -0.5f, 0f);
            return p;
        }

        private McpCylinderMeshGenerator.McpCylinderParams _cylP = DefaultCylinderParams();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, Transform sceneRoot)
        {
            if (parent == null) return;

            _sectionEl = parent;
            parent.Clear();

            parent.Add(SL(T("PanelTitle"), bold: true));
            parent.Add(Sep());

            // 図形ボタングリッド
            _shapeGrid = new VisualElement();
            _shapeGrid.style.flexDirection = FlexDirection.Row;
            _shapeGrid.style.flexWrap      = Wrap.Wrap;
            _shapeGrid.style.marginBottom  = 4;
            parent.Add(_shapeGrid);
            PopulateShapeGrid();

            parent.Add(Sep());

            // プレビュー領域
            _previewEl = new VisualElement();
            _previewEl.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _previewEl.style.height          = _previewHeight;
            _previewEl.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.16f));
            _previewEl.style.marginBottom    = 4;
            _previewEl.style.backgroundSize  = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            _previewEl.pickingMode           = PickingMode.Position;
            parent.Add(_previewEl);

            _preview = new PrimitivePreviewViewport();
            _preview.Initialize(sceneRoot);

            _previewEl.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _preview.Resize(Mathf.Max(1, (int)e.newRect.width), Mathf.Max(1, (int)e.newRect.height));
            });

            _previewEl.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && !e.ctrlKey) return;
                _previewEl.CapturePointer(e.pointerId);
                _mouseDragging = false;
                _mouseBtn      = (e.button == 0) ? 2 : e.button;
                _mouseDownPos  = e.localPosition;
                _mousePrevPos  = e.localPosition;
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_previewEl.HasPointerCapture(e.pointerId)) return;
                var cur   = new Vector2(e.localPosition.x, e.localPosition.y);
                var delta = cur - _mousePrevPos;
                _mousePrevPos = cur;
                if (!_mouseDragging && Vector2.Distance(cur, _mouseDownPos) > DragThreshold)
                    _mouseDragging = true;
                if (!_mouseDragging) return;
                if (_mouseBtn == 1) _preview.Orbit.SimulateOrbit(delta.x, delta.y);
                else                _preview.Orbit.SimulatePan(delta.x, -delta.y);
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_previewEl.HasPointerCapture(e.pointerId)) return;
                _previewEl.ReleasePointer(e.pointerId);
                _mouseDragging = false;
                e.StopPropagation();
            });
            _previewEl.RegisterCallback<WheelEvent>(e =>
            {
                _preview.Orbit.SimulateScroll(-e.delta.y * 0.1f);
                e.StopPropagation();
            });

            // プレビュー下端のリサイズハンドル
            var resizeHandle = new VisualElement();
            resizeHandle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            resizeHandle.style.height          = 6;
            resizeHandle.style.marginBottom    = 4;
            resizeHandle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            resizeHandle.pickingMode           = PickingMode.Position;
            parent.Add(resizeHandle);

            resizeHandle.RegisterCallback<PointerDownEvent>(e =>
            {
                resizeHandle.CapturePointer(e.pointerId);
                _resizeDragging    = true;
                _resizeStartY      = e.position.y;
                _resizeStartHeight = _previewHeight;
                e.StopPropagation();
            });
            resizeHandle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_resizeDragging || !resizeHandle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - _resizeStartY;
                _previewHeight = Mathf.Clamp(_resizeStartHeight + delta, PreviewMinHeight, PreviewMaxHeight);
                _previewEl.style.height = _previewHeight; // GeometryChangedEvent → Resize が追従
                e.StopPropagation();
            });
            resizeHandle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!resizeHandle.HasPointerCapture(e.pointerId)) return;
                resizeHandle.ReleasePointer(e.pointerId);
                _resizeDragging = false;
                e.StopPropagation();
            });

            parent.Add(Sep());

            // ステータスラベル（生成ボタンのハンドラが参照するので先に作る）
            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(0.7f, 0.9f, 0.7f));
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;

            parent.Add(CB());
            parent.Add(Sep());

            // 追加先
            var addModeChoices = new List<string>
            {
                T("AddModeNewObj"),
                T("AddModeExisting"),
                T("AddModeNewModel"),
            };
            var addModeDd = new DropdownField(addModeChoices, 0);
            addModeDd.label = T("AddMode");
            addModeDd.style.marginTop    = 4;
            addModeDd.style.marginBottom = 2;
            addModeDd.RegisterValueChangedCallback(e =>
            {
                int i = addModeChoices.IndexOf(e.newValue);
                _addMode = i < 0 ? PrimitiveAddMode.NewObject : (PrimitiveAddMode)i;
            });
            parent.Add(addModeDd);

            // 生成位置
            var poseFold = new Foldout { text = T("WorldPos"), value = false };
            poseFold.style.marginTop    = 2;
            poseFold.style.marginBottom = 2;
            var poseFoldLabel = poseFold.Q<Label>();
            if (poseFoldLabel != null) poseFoldLabel.style.color = new StyleColor(Color.white);
            poseFold.contentContainer.Add(V3F(T("WorldPosX"), T("WorldPosY"), T("WorldPosZ"),
                () => _worldPos.x, v => { _worldPos.x = v; },
                () => _worldPos.y, v => { _worldPos.y = v; },
                () => _worldPos.z, v => { _worldPos.z = v; }));
            parent.Add(poseFold);

            parent.Add(Sep());

            // 個別パラメータ
            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);

            parent.Add(Sep());
            parent.Add(_statusLabel);

            // 図形は自動で選ばない。図形ボタンを押して初めて諸元が出る。
            RebuildSettings();

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        // ================================================================
        // 図形の選択
        // ================================================================

        private void PopulateShapeGrid()
        {
            _shapeGrid.Clear();
            _shapeBtns.Clear();
            AddShapeButton(ShapeKind.McpCylinder, T("McpCylinder"));
            RefreshShapeButtons();
        }

        private void AddShapeButton(ShapeKind kind, string label)
        {
            var btn = new Button(() => Select(kind)) { text = label };
            btn.style.width        = new StyleLength(new Length(33.3f, LengthUnit.Percent));
            btn.style.height       = 26;
            btn.style.marginBottom = 2;
            btn.style.fontSize     = 10;
            _shapeBtns[kind] = btn;
            _shapeGrid.Add(btn);
        }

        private void Select(ShapeKind k)
        {
            _current = k;
            RefreshShapeButtons();
            RebuildSettings();
            _dirty = true;
        }

        private void RefreshShapeButtons()
        {
            foreach (var kv in _shapeBtns)
                kv.Value.style.backgroundColor =
                    new StyleColor(kv.Key == _current ? ShapeBtnOn : ShapeBtnOff);
        }

        private void RebuildSettings()
        {
            if (_settingsContainer == null) return;
            _settingsContainer.Clear();

            switch (_current)
            {
                case ShapeKind.McpCylinder:
                    BuildMcpCylinderUI(_settingsContainer);
                    break;
                default:
                    _settingsContainer.Add(SL(T("SelectShape")));
                    break;
            }

            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
            RefreshCreateButtonState();
        }

        // ================================================================
        // MCP円筒の諸元 UI
        // ================================================================

        private void BuildMcpCylinderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("McpCylinder")));

            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.marginBottom  = 2;
            nameRow.Add(ML(T("MeshName")));
            var nameField = new TextField { value = _cylP.MeshName };
            nameField.style.flexGrow = 1;
            nameField.RegisterValueChangedCallback(e => _cylP.MeshName = e.newValue);
            nameRow.Add(nameField);
            c.Add(nameRow);

            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMax,
                () => _cylP.RadiusTop,    v => { _cylP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"),
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadiusMax,
                () => _cylP.RadiusBottom, v => { _cylP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),
                McpCylinderMeshGenerator.McpCylinderParams.HeightMin,
                McpCylinderMeshGenerator.McpCylinderParams.HeightMax,
                () => _cylP.Height,       v => { _cylP.Height       = v; D(); }));

            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),
                McpCylinderMeshGenerator.McpCylinderParams.RadialSegmentsMin,
                McpCylinderMeshGenerator.McpCylinderParams.RadialSegmentsMax,
                () => _cylP.RadialSegments, v => { _cylP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"),
                McpCylinderMeshGenerator.McpCylinderParams.HeightSegmentsMin,
                McpCylinderMeshGenerator.McpCylinderParams.HeightSegmentsMax,
                () => _cylP.HeightSegments, v => { _cylP.HeightSegments = v; D(); }));

            c.Add(TR(T("CapTop"),    () => _cylP.CapTop,    v => { _cylP.CapTop    = v; DRebuild(); }));
            c.Add(TR(T("CapBottom"), () => _cylP.CapBottom, v => { _cylP.CapBottom = v; DRebuild(); }));

            // 縁の丸めの上限は高さと半径から決まるので、行を出すかどうかも含めて
            // ここで計算する。本番側（BuildCylinderUI）と同じ考え方。
            float maxEdge = _cylP.Height * 0.5f;
            if (_cylP.CapTop    && _cylP.RadiusTop    > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusTop);
            if (_cylP.CapBottom && _cylP.RadiusBottom > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusBottom);
            if (maxEdge > 0f)
            {
                c.Add(SR(T("EdgeRadius"),
                    McpCylinderMeshGenerator.McpCylinderParams.EdgeRadiusMin, maxEdge,
                    () => _cylP.EdgeRadius, v => { _cylP.EdgeRadius = v; D(); }));
                if (_cylP.EdgeRadius > 0f)
                    c.Add(IR(T("EdgeSeg"),
                        McpCylinderMeshGenerator.McpCylinderParams.EdgeSegmentsMin,
                        McpCylinderMeshGenerator.McpCylinderParams.EdgeSegmentsMax,
                        () => _cylP.EdgeSegments, v => { _cylP.EdgeSegments = v; D(); }));
            }

            BuildPivotY(c, () => _cylP.Pivot.y, v => { _cylP.Pivot = new Vector3(0, v, 0); D(); });
        }

        /// <summary>値だけ変わった。再生成を予約する。</summary>
        private void D()
        {
            _dirty = true;
            RefreshCreateButtonState();
        }

        /// <summary>
        /// 行の出方まで変わった（キャップの有無で縁の丸めの行が出入りする）。
        /// 値の変更に加えて諸元 UI を組み直す。
        /// </summary>
        private void DRebuild()
        {
            _dirty = true;
            RebuildSettings();
        }

        // ================================================================
        // 生成
        // ================================================================

        private VisualElement CB()
        {
            var btn = new Button(() =>
            {
                try
                {
                    var cmd = BuildCreateCommand();
                    if (cmd == null) { _statusLabel.text = T("FailNoShape"); return; }
                    if (SendCommand == null) { _statusLabel.text = T("FailNoWire"); return; }

                    SendCommand(cmd);
                    _statusLabel.text = T("Created");
                }
                catch (Exception ex)
                {
                    _statusLabel.text = $"Error: {ex.Message}";
                    Debug.LogException(ex);
                }
            })
            { text = T("Create") };

            btn.style.height    = 28;
            btn.style.marginTop = 6;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _createBtn = btn;
            RefreshCreateButtonState();
            return btn;
        }

        private void RefreshCreateButtonState()
        {
            _createBtn?.SetEnabled(_current != ShapeKind.None);
        }

        /// <summary>現在の図形種別に対応する生成コマンドを組む。</summary>
        private CreatePrimitiveMeshCommand BuildCreateCommand()
        {
            switch (_current)
            {
                case ShapeKind.McpCylinder:
                    return new CreateMcpCylinderCommand(ModelIndex(), _cylP, CurrentPlacement());
                default:
                    return null;
            }
        }

        /// <summary>
        /// 配置の指定。サンドボックスは姿勢のベイクや材質指定を持たないので、
        /// 既定へ生成位置と追加先だけを載せる。
        /// </summary>
        private PrimitivePlacement CurrentPlacement()
        {
            var p = PrimitivePlacement.Default;
            p.WorldPosition = _worldPos;
            p.AddMode       = _addMode;
            return p;
        }

        private MeshObject Generate(bool forPreview)
        {
            var cmd = BuildCreateCommand();
            if (cmd == null) return null;
            // 配置元の解決が要るのは藤壺だけなので resolvePlaceSources は渡さない。
            return PrimitiveMeshFactory.Build(cmd, forPreview, null);
        }

        // ================================================================
        // プレビュー再生成 / カメラレンダー / Dispose
        // ================================================================

        private void Regenerate()
        {
            if (_preview == null) return;
            if (!_dirty) return;

            // 適応スロットル：直前の生成コストに応じて再生成頻度を間引く。
            // スキップ時は _dirty を残すので、最終値は必ず反映される。
            double t0 = Time.realtimeSinceStartupAsDouble;
            if (t0 < _nextGenAllowed) return;
            _dirty = false;
            try
            {
                var mo = Generate(true);
                _preview.SetMesh(mo);
                DestroyWire();
                if (mo != null) _wireMesh = BuildWire(mo);
            }
            catch { }
            double elapsed = Time.realtimeSinceStartupAsDouble - t0;
            _nextGenAllowed = Time.realtimeSinceStartupAsDouble + Math.Max(0.05, elapsed * 3.0);
        }

        private void TickPreview()
        {
            if (_preview == null) return;
            Regenerate();
            _preview.Tick(_wireMesh);
            if (_previewEl != null && _preview.RT != null)
                _previewEl.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_preview.RT));
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            if (cam.cameraType != CameraType.Game) return;
            if (_preview != null && cam == _preview.Cam) return;
            // 非表示のパネルは RT を誰も見ていないので Render しない。
            if (!IsSectionVisible()) return;

            // 先に再生成して _wireMesh を最新化し、そのうえで提出する。
            // この順序により、SubmitLiveWire は提出のみを行えばよい。
            TickPreview();
            SubmitLiveWire(cam);
        }

        // ================================================================
        // メイン3Dウインドウへのライブワイヤ提出（3D連携）
        // ================================================================

        /// <summary>
        /// 生成予定形状の黄色ワイヤをメイン3Dウインドウのカメラへ提出する。
        /// ここでは提出のみを行い、メッシュ再構築は行わない
        /// （呼出し前の TickPreview で _wireMesh は最新化済み）。
        /// </summary>
        private void SubmitLiveWire(Camera cam)
        {
            if (!LiveWireInMainViewport) return;
            if (cam == null || _wireMesh == null) return;
            if (IsMainViewportCamera == null || !IsMainViewportCamera(cam)) return;

            EnsureLiveWireMaterial();
            if (_liveWireMat == null) return;

            var m = LiveWireMatrix();
            Graphics.RenderMesh(
                PLRenderMeshHelper.Make(_liveWireMat, cam, PLRenderMeshHelper.WorldBoundsOf(_wireMesh, m)),
                _wireMesh, 0, m);
        }

        /// <summary>
        /// ライブワイヤの配置行列。回転・拡大は生成側で頂点へ焼き込み済みなので、
        /// ここで扱うのは生成位置だけ。
        /// <para>
        /// NewObject / NewModel: 生成物は親を持たないルートなので Translate(_worldPos)。
        /// </para>
        /// <para>
        /// AddToExisting: 追加先メッシュのローカル空間で頂点へ加算されるため、
        /// 追加先の WorldMatrix を左から掛ける。
        /// </para>
        /// </summary>
        private Matrix4x4 LiveWireMatrix()
        {
            var local = Matrix4x4.Translate(_worldPos);
            if (_addMode != PrimitiveAddMode.AddToExisting) return local;
            var parent = GetAddTargetWorldMatrix?.Invoke() ?? Matrix4x4.identity;
            return parent * local;
        }

        private void EnsureLiveWireMaterial()
        {
            if (_liveWireMat != null) return;

            var sh = Shader.Find("Hidden/Internal-Colored")
                  ?? Shader.Find("Unlit/Color");
            if (sh == null) return;

            _liveWireMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _liveWireMat.SetColor("_Color", new Color(1f, 0.92f, 0.2f, 1f));
            // 常に手前に描く。モデルに埋まっても生成位置が見えるようにする。
            _liveWireMat.SetInt("_ZTest",  (int)CompareFunction.Always);
            _liveWireMat.SetInt("_ZWrite", 0);
        }

        /// <summary>このパネルのセクションが右ペインに表示されているか。</summary>
        private bool IsSectionVisible()
        {
            if (_sectionEl == null) return true;
            return _sectionEl.resolvedStyle.display != DisplayStyle.None;
        }

        private static Mesh BuildWire(MeshObject mo)
        {
            var verts = new Vector3[mo.VertexCount];
            for (int i = 0; i < verts.Length; i++) verts[i] = mo.Vertices[i].Position;
            var set = new HashSet<(int, int)>();
            var idx = new List<int>();
            foreach (var f in mo.Faces)
            {
                int n = f.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i], b = f.VertexIndices[(i + 1) % n];
                    var e = a < b ? (a, b) : (b, a);
                    if (set.Add(e)) { idx.Add(a); idx.Add(b); }
                }
            }
            if (idx.Count == 0) return null;
            var wire = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            wire.vertices = verts;
            wire.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            wire.RecalculateBounds();
            return wire;
        }

        private void DestroyWire()
        {
            if (_wireMesh != null) { UnityEngine.Object.Destroy(_wireMesh); _wireMesh = null; }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _preview?.Dispose();
            _preview = null;
            DestroyWire();
            if (_liveWireMat != null)
            {
                UnityEngine.Object.Destroy(_liveWireMat);
                _liveWireMat = null;
            }
        }

        // ================================================================
        // 行ヘルパ
        // ================================================================
        // 図形生成パネルの同名ヘルパと同じ見た目。共有せずに写しているのは、
        // サンドボックスで行の見た目をいじっても本番側が動かないようにするため。

        private static VisualElement ShapeTitle(string t)
        {
            var box = new VisualElement();
            box.style.marginTop    = 8;
            box.style.marginBottom = 4;

            var rule = new VisualElement();
            rule.style.height          = 2;
            rule.style.marginBottom    = 4;
            rule.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.30f));
            box.Add(rule);

            var l = new Label(t);
            l.style.fontSize = 14;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(new Color(0.75f, 0.88f, 1f));
            box.Add(l);

            return box;
        }

        private static Label SL(string t, bool bold = false)
        {
            var l = new Label(t);
            l.style.marginTop = bold ? 2 : 5; l.style.marginBottom = 2;
            l.style.color = bold
                ? new StyleColor(new Color(0.9f, 0.9f, 0.9f))
                : new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = bold ? 11 : 10;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static Label ML(string t)
        {
            var l = new Label(t); l.style.width = 80;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.fontSize = 10; return l;
        }

        private static void SB(VisualElement p, string t, Action onClick)
        {
            var b = new Button(onClick) { text = t }; b.style.flexGrow = 1; b.style.marginRight = 2;
            b.style.height = 18; b.style.fontSize = 9; p.Add(b);
        }

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
            => SR(label, min, max, get, set, out _, out _);

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set,
            out Slider slOut, out FloatField nfOut)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf);
            slOut = sl; nfOut = nf;
            return row;
        }

        private static VisualElement IR(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new SliderInt(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new IntegerField { value = get() }; nf.style.width = 36;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify(e.newValue); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { int v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
        }

        private static VisualElement TR(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() }; t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => set(e.newValue)); return t;
        }

        private static VisualElement V3F(
            string lx, string ly, string lz,
            Func<float> gx, Action<float> sx,
            Func<float> gy, Action<float> sy,
            Func<float> gz, Action<float> sz)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            void AddFF(string lbl, Func<float> g, Action<float> s)
            {
                var sub = new VisualElement(); sub.style.flexDirection = FlexDirection.Row; sub.style.flexGrow = 1;
                var l = new Label(lbl); l.style.width = 14; l.style.unityTextAlign = TextAnchor.MiddleLeft;
                var f = new FloatField { value = g() }; f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => s(e.newValue));
                sub.Add(l); sub.Add(f); row.Add(sub);
            }
            AddFF(lx, gx, sx); AddFF(ly, gy, sy); AddFF(lz, gz, sz);
            return row;
        }

        private void BuildPivotY(VisualElement c, Func<float> getY, Action<float> setY)
        {
            var fold = new Foldout { text = T("PivotOffset"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;

            f.Add(SR(T("PivotY"),
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                getY, setY, out var ySl, out var yNf));

            void SyncY()
            {
                float v = getY();
                ySl.SetValueWithoutNotify(v);
                yNf.SetValueWithoutNotify((float)Math.Round(v, 3));
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            SB(row, T("Bottom"), () => { setY(-0.5f); SyncY(); });
            SB(row, T("Center"), () => { setY( 0f);   SyncY(); });
            SB(row, T("Top"),    () => { setY( 0.5f); SyncY(); });
            f.Add(row);

            c.Add(fold);
        }
    }
}
