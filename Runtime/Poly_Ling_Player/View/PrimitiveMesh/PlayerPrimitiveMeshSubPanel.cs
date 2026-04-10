// PlayerPrimitiveMeshSubPanel.cs
// 図形生成サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>生成ボタン押下時。(MeshObject, meshName, worldPosition, ignorePoseInArmature)</summary>
        public Action<MeshObject, string, Vector3, bool> OnMeshCreated;

        // ================================================================
        // 図形種別
        // ================================================================

        private enum ShapeKind { Cube, Sphere, Cylinder, Capsule, Plane, Pyramid, Revolution, Profile2D, NohMask }

        private static readonly string[] ShapeKeys =
            { "Cube","Sphere","Cylinder","Capsule","Plane","Pyramid","Revolution","Profile2D","NohMask" };

        // ================================================================
        // パラメータ
        // ================================================================

        private ShapeKind _current = ShapeKind.Cube;
        private CubeMeshGenerator.CubeParams         _cubeP   = CubeMeshGenerator.CubeParams.Default;
        private SphereMeshGenerator.SphereParams     _sphereP = SphereMeshGenerator.SphereParams.Default;
        private CylinderMeshGenerator.CylinderParams _cylP    = CylinderMeshGenerator.CylinderParams.Default;
        private CapsuleMeshGenerator.CapsuleParams   _capsP   = CapsuleMeshGenerator.CapsuleParams.Default;
        private PlaneMeshGenerator.PlaneParams       _planeP  = PlaneMeshGenerator.PlaneParams.Default;
        private PyramidMeshGenerator.PyramidParams   _pyramidP= PyramidMeshGenerator.PyramidParams.Default;
        private RevolutionParams                     _revP    = RevolutionParams.Default;
        private List<Vector2>                        _revProfile = null;
        private Profile2DParams                      _p2dP    = Profile2DParams.Default;
        private List<Loop>                           _p2dLoops = null;
        private FaceMeshParams                       _nohP    = FaceMeshParams.Default;

        // ワールド生成位置
        private Vector3 _worldPos = Vector3.zero;
        private bool    _ignorePoseInArmature = false;

        // ================================================================
        // プレビュー
        // ================================================================

        private PrimitivePreviewViewport _preview;
        private Mesh                     _wireMesh;
        private bool                     _dirty = true;

        // ================================================================
        // UI
        // ================================================================

        private readonly Button[]  _shapeBtns = new Button[9];
        private VisualElement      _settingsContainer;
        private VisualElement      _previewEl;
        private Label              _statusLabel;

        // プレビューマウス状態
        private bool    _mouseDragging;
        private int     _mouseBtn;
        private Vector2 _mouseDownPos;
        private Vector2 _mousePrevPos;
        private const float DragThreshold = 3f;

        // ================================================================
        // Revolution プロファイルエディタ状態
        // ================================================================

        private int           _revSelIdx    = -1;
        private bool          _revDrag      = false;
        private int           _revDragIdx   = -1;
        private int           _revHoverEI   = -1;
        private VisualElement _revCanvas;
        private VisualElement _revPtRow;
        private Label         _revPtLabel;
        private Slider        _revPtXSlider;
        private FloatField    _revPtXField;
        private Slider        _revPtYSlider;
        private FloatField    _revPtYField;
        private string        _revCsvPath   = "";
        // 下絵
        private Texture2D     _revBgTex;
        private VisualElement _revBgEl;
        private string        _revBgPath    = "";
        private Vector2       _revBgOffset  = Vector2.zero;
        private float         _revBgScale   = 1f;
        private float         _revBgAlpha   = 0.4f;
        private bool          _revBgMode    = false; // true=下絵移動モード
        private bool          _revBgDrag    = false;
        private Vector2       _revBgDragStart;
        private Vector2       _revBgOffsetOnDragStart;

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
        private float         _p2dZoom    = 1f;
        private Vector2       _p2dOffset  = Vector2.zero;
        private int           _p2dHoverEL = -1;
        private int           _p2dHoverEI = -1;
        private string        _p2dCsvPath = "";
        // 下絵
        private Texture2D     _p2dBgTex;
        private VisualElement _p2dBgEl;
        private string        _p2dBgPath   = "";
        private Vector2       _p2dBgOffset = Vector2.zero;
        private float         _p2dBgScale  = 1f;
        private float         _p2dBgAlpha  = 0.4f;
        private bool          _p2dBgMode   = false;
        private bool          _p2dBgDrag   = false;
        private Vector2       _p2dBgDragStart;
        private Vector2       _p2dBgOffsetOnDragStart;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, Transform sceneRoot)
        {
            _cubeP.LinkTopBottom = true;
            parent.Clear();

            parent.Add(SL(T("PanelTitle"), bold: true));
            parent.Add(Sep());

            // 9ボタングリッド
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;
            grid.style.marginBottom  = 4;
            parent.Add(grid);
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var btn = new Button(() => Select((ShapeKind)idx)) { text = T(ShapeKeys[idx]) };
                btn.style.width = new StyleLength(new Length(33.3f, LengthUnit.Percent));
                btn.style.height = 26; btn.style.marginBottom = 2; btn.style.fontSize = 10;
                _shapeBtns[i] = btn;
                grid.Add(btn);
            }

            parent.Add(Sep());

            // プレビュー領域
            _previewEl = new VisualElement();
            _previewEl.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _previewEl.style.height          = 200;
            _previewEl.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.16f));
            _previewEl.style.marginBottom    = 4;
            _previewEl.style.backgroundSize  = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            _previewEl.pickingMode           = PickingMode.Position;
            parent.Add(_previewEl);

            _preview = new PrimitivePreviewViewport();
            _preview.Initialize(sceneRoot);

            _previewEl.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _preview.Resize(Mathf.Max(1,(int)e.newRect.width), Mathf.Max(1,(int)e.newRect.height));
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

            parent.Add(Sep());

            // ワールド生成位置
            parent.Add(SL(T("WorldPos")));
            parent.Add(V3F(T("WorldPosX"), T("WorldPosY"), T("WorldPosZ"),
                () => _worldPos.x, v => _worldPos.x = v,
                () => _worldPos.y, v => _worldPos.y = v,
                () => _worldPos.z, v => _worldPos.z = v));

            var ignorePoseToggle = new Toggle(T("IgnorePose")) { value = _ignorePoseInArmature };
            ignorePoseToggle.style.color = new StyleColor(Color.white);
            ignorePoseToggle.RegisterValueChangedCallback(e => _ignorePoseInArmature = e.newValue);
            parent.Add(ignorePoseToggle);

            parent.Add(Sep());

            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);

            parent.Add(Sep());

            _statusLabel = new Label("");
            _statusLabel.style.color     = new StyleColor(new Color(0.7f, 0.9f, 0.7f));
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(_statusLabel);

            Select(ShapeKind.Cube);
        }

        // ================================================================
        // Tick / Dispose
        // ================================================================

        public void Tick()
        {
            if (_preview == null) return;
            if (_dirty)
            {
                _dirty = false;
                try
                {
                    var mo = Generate();
                    _preview.SetMesh(mo);
                    DestroyWire();
                    if (mo != null) _wireMesh = BuildWire(mo);
                }
                catch { }
            }
            _preview.Tick(_wireMesh);
            if (_previewEl != null && _preview.RT != null)
                _previewEl.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_preview.RT));
        }

        public void Dispose()
        {
            _preview?.Dispose();
            _preview = null;
            DestroyWire();
        }

        // ================================================================
        // 図形選択
        // ================================================================

        private void Select(ShapeKind k)
        {
            _current = k;
            for (int i = 0; i < 9; i++)
            {
                if (_shapeBtns[i] == null) continue;
                _shapeBtns[i].style.backgroundColor = (int)k == i
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            }
            RebuildSettings();
            _dirty = true;
        }

        // ================================================================
        // 設定UI
        // ================================================================

        private void RebuildSettings()
        {
            _settingsContainer?.Clear();
            switch (_current)
            {
                case ShapeKind.Cube:       BuildCubeUI(_settingsContainer);       break;
                case ShapeKind.Sphere:     BuildSphereUI(_settingsContainer);     break;
                case ShapeKind.Cylinder:   BuildCylinderUI(_settingsContainer);   break;
                case ShapeKind.Capsule:    BuildCapsuleUI(_settingsContainer);    break;
                case ShapeKind.Plane:      BuildPlaneUI(_settingsContainer);      break;
                case ShapeKind.Pyramid:    BuildPyramidUI(_settingsContainer);    break;
                case ShapeKind.Revolution: BuildRevolutionUI(_settingsContainer); break;
                case ShapeKind.Profile2D:  BuildProfile2DUI(_settingsContainer);  break;
                case ShapeKind.NohMask:    BuildNohMaskUI(_settingsContainer);    break;
                default:
                    var lbl = new Label(T("NotSupported"));
                    lbl.style.color = new StyleColor(new Color(0.8f, 0.5f, 0.3f));
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    _settingsContainer?.Add(lbl);
                    break;
            }
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
        }

        private void D() => _dirty = true;

        private void BuildCubeUI(VisualElement c)
        {
            c.Add(SL(T("Cube")));
            c.Add(NF(() => _cubeP.MeshName, v => _cubeP.MeshName = v));

            c.Add(TR(T("LinkWHD"), () => _cubeP.LinkWHD, v => { _cubeP.LinkWHD = v; D(); }));

            c.Add(SL(T("Size")));
            if (_cubeP.LinkWHD)
            {
                c.Add(SR(T("WidthX"), 0.1f, 10f, () => _cubeP.WidthTop, v =>
                {
                    _cubeP.WidthTop = _cubeP.WidthBottom = _cubeP.DepthTop = _cubeP.DepthBottom = _cubeP.Height = v; D();
                }));
            }
            else
            {
                c.Add(SR(T("WidthX"),  0.1f, 10f, () => _cubeP.WidthTop, v => { _cubeP.WidthTop  = v; _cubeP.WidthBottom  = v; D(); }));
                c.Add(SR(T("HeightY"), 0.1f, 10f, () => _cubeP.Height,   v => { _cubeP.Height = v; D(); }));
                c.Add(SR(T("DepthZ"),  0.1f, 10f, () => _cubeP.DepthTop, v => { _cubeP.DepthTop  = v; _cubeP.DepthBottom  = v; D(); }));
            }

            c.Add(SL(T("CornerRadius")));
            c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _cubeP.CornerRadius, v => { _cubeP.CornerRadius = v; D(); }));
            if (_cubeP.CornerRadius > 0f)
                c.Add(IR(T("CornerSeg"), 1, 8, () => _cubeP.CornerSegments, v => { _cubeP.CornerSegments = v; D(); }));

            c.Add(SL(T("Subdivisions")));
            c.Add(IR(T("SubdivX"), 1, 8, () => _cubeP.Subdivisions.x, v => { _cubeP.Subdivisions = new Vector3Int(v, _cubeP.Subdivisions.y, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivY"), 1, 8, () => _cubeP.Subdivisions.y, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, v, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivZ"), 1, 8, () => _cubeP.Subdivisions.z, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, _cubeP.Subdivisions.y, v); D(); }));

            BuildPivotXYZ(c,
                () => _cubeP.Pivot, v => { _cubeP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            c.Add(CB());
        }

        private void BuildSphereUI(VisualElement c)
        {
            c.Add(SL(T("Sphere")));
            c.Add(NF(() => _sphereP.MeshName, v => _sphereP.MeshName = v));
            c.Add(SR(T("Radius"), 0.05f, 5f, () => _sphereP.Radius, v => { _sphereP.Radius = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Lateral"), 4, 64, () => _sphereP.LatitudeSegments,  v => { _sphereP.LatitudeSegments  = v; D(); }));
            c.Add(IR(T("Radial"),  4, 64, () => _sphereP.LongitudeSegments, v => { _sphereP.LongitudeSegments = v; D(); }));
            c.Add(TR(T("CubeSphere"), () => _sphereP.CubeSphere, v => { _sphereP.CubeSphere = v; D(); }));

            BuildPivotXYZ(c,
                () => _sphereP.Pivot, v => { _sphereP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            c.Add(CB());
        }

        private void BuildCylinderUI(VisualElement c)
        {
            c.Add(SL(T("Cylinder")));
            c.Add(NF(() => _cylP.MeshName, v => _cylP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),    0f,   5f,   () => _cylP.RadiusTop,    v => { _cylP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), 0f,   5f,   () => _cylP.RadiusBottom, v => { _cylP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),       0.1f, 10f,  () => _cylP.Height,       v => { _cylP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),  3, 48, () => _cylP.RadialSegments, v => { _cylP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), 1, 16, () => _cylP.HeightSegments, v => { _cylP.HeightSegments = v; D(); }));
            c.Add(TR(T("CapTop"),    () => _cylP.CapTop,    v => { _cylP.CapTop    = v; D(); }));
            c.Add(TR(T("CapBottom"), () => _cylP.CapBottom, v => { _cylP.CapBottom = v; D(); }));

            float maxEdge = _cylP.Height * 0.5f;
            if (_cylP.CapTop    && _cylP.RadiusTop    > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusTop);
            if (_cylP.CapBottom && _cylP.RadiusBottom > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusBottom);
            if (maxEdge > 0f)
            {
                c.Add(SR(T("EdgeRadius"), 0f, maxEdge, () => _cylP.EdgeRadius, v => { _cylP.EdgeRadius = v; D(); }));
                if (_cylP.EdgeRadius > 0f)
                    c.Add(IR(T("EdgeSeg"), 1, 16, () => _cylP.EdgeSegments, v => { _cylP.EdgeSegments = v; D(); }));
            }

            BuildPivotY(c,
                () => _cylP.Pivot.y, v => { _cylP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            c.Add(CB());
        }

        private void BuildCapsuleUI(VisualElement c)
        {
            c.Add(SL(T("Capsule")));
            c.Add(NF(() => _capsP.MeshName, v => _capsP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),    0.1f, 2f,  () => _capsP.RadiusTop,    v => { _capsP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), 0.1f, 2f,  () => _capsP.RadiusBottom, v => { _capsP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"),       0.5f, 10f, () => _capsP.Height,       v => { _capsP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"),  8, 48, () => _capsP.RadialSegments, v => { _capsP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), 1, 16, () => _capsP.HeightSegments, v => { _capsP.HeightSegments = v; D(); }));
            c.Add(IR(T("Cap"),     2, 16, () => _capsP.CapSegments,    v => { _capsP.CapSegments    = v; D(); }));

            BuildPivotY(c,
                () => _capsP.Pivot.y, v => { _capsP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            // 上球・下球の重心ピボット
            var sphereRow = new VisualElement();
            sphereRow.style.flexDirection = FlexDirection.Row;
            sphereRow.style.marginBottom  = 4;
            SB(sphereRow, T("UpperSphere"), () =>
            {
                float halfH     = _capsP.Height * 0.5f;
                float cylTop    = halfH - _capsP.RadiusTop;
                float normalized = _capsP.Height > 0f ? cylTop / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D();
            });
            SB(sphereRow, T("LowerSphere"), () =>
            {
                float halfH      = _capsP.Height * 0.5f;
                float cylBottom  = -halfH + _capsP.RadiusBottom;
                float normalized = _capsP.Height > 0f ? cylBottom / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D();
            });
            c.Add(sphereRow);

            c.Add(CB());
        }

        private void BuildPlaneUI(VisualElement c)
        {
            c.Add(SL(T("Plane")));
            c.Add(NF(() => _planeP.MeshName, v => _planeP.MeshName = v));
            c.Add(SR(T("Width"),  0.1f, 10f, () => _planeP.Width,  v => { _planeP.Width  = v; D(); }));
            c.Add(SR(T("Height"), 0.1f, 10f, () => _planeP.Height, v => { _planeP.Height = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Width"),  1, 32, () => _planeP.WidthSegments,  v => { _planeP.WidthSegments  = v; D(); }));
            c.Add(IR(T("Height"), 1, 32, () => _planeP.HeightSegments, v => { _planeP.HeightSegments = v; D(); }));
            var dd = new DropdownField(new List<string>{"XZ","XY","YZ"}, (int)_planeP.Orientation);
            dd.label = T("Orientation"); dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(e => { _planeP.Orientation = (PlaneOrientation)dd.index; D(); });
            c.Add(dd);
            c.Add(TR(T("DoubleSided"), () => _planeP.DoubleSided, v => { _planeP.DoubleSided = v; D(); }));

            BuildPivotXYZ(c,
                () => _planeP.Pivot, v => { _planeP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            c.Add(CB());
        }

        private void BuildPyramidUI(VisualElement c)
        {
            c.Add(SL(T("Pyramid")));
            c.Add(NF(() => _pyramidP.MeshName, v => _pyramidP.MeshName = v));
            c.Add(IR(T("Sides"),      3, 16,     () => _pyramidP.Sides,       v => { _pyramidP.Sides       = v; D(); }));
            c.Add(SR(T("BaseRadius"), 0.1f, 5f,  () => _pyramidP.BaseRadius,  v => { _pyramidP.BaseRadius  = v; D(); }));
            c.Add(SR(T("Height"),     0.1f, 10f, () => _pyramidP.Height,      v => { _pyramidP.Height      = v; D(); }));
            c.Add(SR(T("ApexOffset"), -1f,  1f,  () => _pyramidP.ApexOffset,  v => { _pyramidP.ApexOffset  = v; D(); }));
            c.Add(TR(T("CapBottom"),  () => _pyramidP.CapBottom, v => { _pyramidP.CapBottom = v; D(); }));

            BuildPivotXYZ(c,
                () => _pyramidP.Pivot, v => { _pyramidP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            c.Add(CB());
        }

        // ================================================================
        // ピボットUIヘルパー
        // ================================================================

        private void BuildPivotY(VisualElement c,
            Func<float> getY, Action<float> setY,
            Vector3 bottom, Vector3 center, Vector3 top)
        {
            c.Add(SL(T("PivotOffset")));
            c.Add(SR(T("PivotY"), -0.5f, 0.5f, getY, setY));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => setY(bottom.y));
            SB(row, T("Center"), () => setY(center.y));
            SB(row, T("Top"),    () => setY(top.y));
            c.Add(row);
        }

        private void BuildPivotXYZ(VisualElement c,
            Func<Vector3> get, Action<Vector3> set,
            float min, float max,
            Vector3 bottom, Vector3 center, Vector3 top)
        {
            c.Add(SL(T("PivotOffset")));
            c.Add(SR(T("PivotX"), min, max, () => get().x, v => { var p = get(); set(new Vector3(v, p.y, p.z)); }));
            c.Add(SR(T("PivotY"), min, max, () => get().y, v => { var p = get(); set(new Vector3(p.x, v, p.z)); }));
            c.Add(SR(T("PivotZ"), min, max, () => get().z, v => { var p = get(); set(new Vector3(p.x, p.y, v)); }));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => set(bottom));
            SB(row, T("Center"), () => set(center));
            SB(row, T("Top"),    () => set(top));
            c.Add(row);
        }

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

            c.Add(SL(T("Revolution")));
            c.Add(NF(() => _revP.MeshName, v => _revP.MeshName = v));
            c.Add(IR(T("RadialSegments"), 3, 64,   () => _revP.RadialSegments, v => { _revP.RadialSegments = v; D(); }));
            c.Add(TR(T("CloseTop"),    () => _revP.CloseTop,    v => { _revP.CloseTop    = v; D(); }));
            c.Add(TR(T("CloseBottom"), () => _revP.CloseBottom, v => { _revP.CloseBottom = v; D(); }));
            c.Add(TR(T("CloseLoop"),   () => _revP.CloseLoop,   v => { _revP.CloseLoop   = v; D(); RefreshRevCanvas(); }));
            c.Add(TR(T("Spiral"),      () => _revP.Spiral,      v => { _revP.Spiral      = v; D(); }));
            if (_revP.Spiral)
            {
                c.Add(IR(T("SpiralTurns"), 1, 10,   () => _revP.SpiralTurns, v => { _revP.SpiralTurns = v; D(); }));
                c.Add(SR(T("SpiralPitch"), -2f, 2f, () => _revP.SpiralPitch, v => { _revP.SpiralPitch = v; D(); }));
            }
            c.Add(TR(T("FlipY"), () => _revP.FlipY, v => { _revP.FlipY = v; D(); }));
            c.Add(TR(T("FlipZ"), () => _revP.FlipZ, v => { _revP.FlipZ = v; D(); }));

            BuildPivotY(c,
                () => _revP.Pivot.y, v => { _revP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

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
                    _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                    _revSelIdx = -1;
                }
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            c.Add(presetDd);

            if (_revP.CurrentPreset == ProfilePreset.Donut)
            {
                c.Add(SL(T("Donut")));
                c.Add(SR(T("DonutMajorRadius"), 0.2f, 2f,   () => _revP.DonutMajorRadius, v => { _revP.DonutMajorRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("DonutMinorRadius"), 0.05f, 1f,  () => _revP.DonutMinorRadius, v => { _revP.DonutMinorRadius = v; ApplyRevPreset(); }));
                c.Add(IR(T("DonutTubeSegs"),    4, 32,      () => _revP.DonutTubeSegments, v => { _revP.DonutTubeSegments = v; ApplyRevPreset(); }));
            }
            if (_revP.CurrentPreset == ProfilePreset.RoundedPipe)
            {
                c.Add(SL(T("RoundedPipe")));
                c.Add(SR(T("PipeInnerRadius"), 0.05f, 2f, () => _revP.PipeInnerRadius, v => { _revP.PipeInnerRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeOuterRadius"), 0.06f, 3f, () => _revP.PipeOuterRadius, v => { _revP.PipeOuterRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeHeight"),      0.1f,  3f, () => _revP.PipeHeight,      v => { _revP.PipeHeight      = v; ApplyRevPreset(); }));
                c.Add(SL(T("InnerCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeInnerCornerRadius,  v => { _revP.PipeInnerCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeInnerCornerSegments, v => { _revP.PipeInnerCornerSegments = v; ApplyRevPreset(); }));
                c.Add(SL(T("OuterCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeOuterCornerRadius,  v => { _revP.PipeOuterCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeOuterCornerSegments, v => { _revP.PipeOuterCornerSegments = v; ApplyRevPreset(); }));
            }

            // ── プロファイルエディタ ──────────────────────────────────────
            c.Add(SL(T("ProfileEditor")));

            // インタラクティブキャンバス
            _revCanvas = new VisualElement();
            _revCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _revCanvas.style.height          = 260;
            _revCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _revCanvas.style.marginBottom    = 4;
            _revCanvas.style.borderTopWidth  = _revCanvas.style.borderBottomWidth =
            _revCanvas.style.borderLeftWidth = _revCanvas.style.borderRightWidth  = 1;
            _revCanvas.style.borderTopColor  = _revCanvas.style.borderBottomColor =
            _revCanvas.style.borderLeftColor = _revCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _revCanvas.style.overflow        = Overflow.Hidden;
            _revCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー（キャンバス内 Absolute 配置）
            _revBgEl = new VisualElement();
            _revBgEl.style.position = Position.Absolute;
            _revBgEl.style.display  = DisplayStyle.None;
            _revBgEl.pickingMode    = PickingMode.Ignore;
            _revCanvas.Add(_revBgEl);

            _revCanvas.generateVisualContent += OnDrawProfileCanvas;
            _revCanvas.RegisterCallback<PointerDownEvent>(OnRevCanvasPointerDown);
            _revCanvas.RegisterCallback<PointerMoveEvent>(OnRevCanvasPointerMove);
            _revCanvas.RegisterCallback<PointerUpEvent>(OnRevCanvasPointerUp);
            _revCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_revBgMode)
                {
                    _revBgScale = Mathf.Clamp(_revBgScale * (1f - e.delta.y * 0.05f), 0.05f, 10f);
                    UpdateRevBgEl(); RefreshRevCanvas();
                }
                e.StopPropagation();
            });
            c.Add(_revCanvas);

            // ボタン行: 削除 / リセット
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 4;
            SB(btnRow, T("DeletePoint"), () =>
            {
                EnsureRevProfile();
                RevolutionProfileEditCore.RemovePoint(_revProfile, ref _revSelIdx);
                _revP.CurrentPreset = ProfilePreset.Custom;
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            SB(btnRow, T("ClearProfile"), () =>
            {
                EnsureRevProfile();
                RevolutionProfileEditCore.ResetProfile(_revProfile, ref _revSelIdx);
                _revP.CurrentPreset = ProfilePreset.Custom;
                D(); RefreshRevCanvas(); RefreshRevPointUI();
            });
            c.Add(btnRow);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(c,
                T("BgImage"),
                () => _revBgPath, v => _revBgPath = v,
                () => _revBgAlpha, v => { _revBgAlpha = v; UpdateRevBgEl(); },
                () => _revBgMode,  v => { _revBgMode  = v; },
                () => // Load
                {
                    if (string.IsNullOrEmpty(_revBgPath)) return;
                    LoadBgTexture(_revBgPath, ref _revBgTex, _revBgEl);
                    _revBgOffset = Vector2.zero; _revBgScale = 1f;
                    UpdateRevBgEl();
                },
                () => // Clear
                {
                    _revBgTex = null;
                    _revBgEl.style.display = DisplayStyle.None;
                    _revBgEl.style.backgroundImage = new StyleBackground();
                });

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
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    float v = Mathf.Clamp(e.newValue, 0f, 2f);
                    xSl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(v, _revProfile[_revSelIdx].y);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
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
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (_revSelIdx < 0 || _revProfile == null || _revSelIdx >= _revProfile.Count) return;
                    float v = Mathf.Clamp(e.newValue, -1f, 2f);
                    ySl.SetValueWithoutNotify(v);
                    _revProfile[_revSelIdx] = new Vector2(_revProfile[_revSelIdx].x, v);
                    _revP.CurrentPreset = ProfilePreset.Custom; D(); RefreshRevCanvas();
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _revPtRow.Add(yRow);
                _revPtYSlider = ySl; _revPtYField = yFf;
            }
            _revPtRow.style.display = DisplayStyle.None;
            c.Add(_revPtRow);

            // CSV 読み書き
            c.Add(SL(T("LoadCSV")));
            var csvPathField = new TextField { value = _revCsvPath };
            csvPathField.style.marginBottom = 2;
            csvPathField.RegisterValueChangedCallback(e => _revCsvPath = e.newValue);
            c.Add(csvPathField);

            var csvRow = new VisualElement(); csvRow.style.flexDirection = FlexDirection.Row; csvRow.style.marginBottom = 4;
            SB(csvRow, T("LoadCSV"), () =>
            {
                if (string.IsNullOrEmpty(_revCsvPath)) return;
                var result = RevolutionCSVIO.Load(_revCsvPath, _revP);
                if (result.Success)
                {
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
                    _revSelIdx = -1;
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                else
                {
                    Debug.LogWarning($"[Revolution CSV] {result.ErrorMessage}");
                }
            });
            SB(csvRow, T("SaveCSV"), () =>
            {
                if (string.IsNullOrEmpty(_revCsvPath)) return;
                EnsureRevProfile();
                RevolutionCSVIO.Save(_revCsvPath, _revProfile, _revP);
            });
            c.Add(csvRow);

            c.Add(CB());
        }

        private void ApplyRevPreset()
        {
            if (_revP.CurrentPreset != ProfilePreset.Custom)
            {
                _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
                _revSelIdx = -1;
            }
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        // ── キャンバス描画 ────────────────────────────────────────────────

        private void OnDrawProfileCanvas(MeshGenerationContext ctx)
        {
            if (_revProfile == null || _revProfile.Count == 0) return;

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.28f, 0.28f, 0.33f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float x = 0f; x <= RevolutionProfileEditCore.RangeX; x += 0.5f)
            {
                var s = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(x, -1f), w, h);
                var e = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(x,  2f), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                var s = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(0f, y), w, h);
                var e = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(2f, y), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.52f, 0.52f, 0.58f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            var ay0 = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(0f, -1f), w, h);
            var ay1 = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(0f,  2f), w, h);
            p2d.MoveTo(ay0); p2d.LineTo(ay1);
            var ax0 = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(0f, 0f), w, h);
            var ax1 = RevolutionProfileEditCore.ProfileToCanvas(new Vector2(2f, 0f), w, h);
            p2d.MoveTo(ax0); p2d.LineTo(ax1);
            p2d.Stroke();

            // プロファイルライン（セグメントごとにホバー判定）
            if (_revProfile.Count >= 2)
            {
                int segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                for (int i = 0; i < segCount; i++)
                {
                    int  j   = (i + 1) % _revProfile.Count;
                    var  a   = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[i], w, h);
                    var  b   = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[j], w, h);
                    bool hov = (i == _revHoverEI);
                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.75f, 0.85f);
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }
            }

            // 頂点ドット
            for (int i = 0; i < _revProfile.Count; i++)
            {
                bool sel = (i == _revSelIdx);
                var  sp  = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[i], w, h);
                p2d.fillColor = sel ? Color.white : new Color(0.2f, 0.75f, 0.85f);
                float r = sel ? 5.5f : 3.5f;
                RevFillCircle(p2d, sp, r, 10);
            }
        }

        /// <summary>Painter2D でポリゴン近似の塗りつぶし円を描く</summary>
        private static void RevFillCircle(Painter2D p2d, Vector2 center, float radius, int n)
        {
            p2d.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float a  = i * Mathf.PI * 2f / n;
                var   pt = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) p2d.MoveTo(pt); else p2d.LineTo(pt);
            }
            p2d.ClosePath();
            p2d.Fill();
        }

        private void RefreshRevCanvas() => _revCanvas?.MarkDirtyRepaint();

        private void RefreshRevPointUI()
        {
            if (_revPtRow == null) return;
            if (_revSelIdx >= 0 && _revProfile != null && _revSelIdx < _revProfile.Count)
            {
                var pt = _revProfile[_revSelIdx];
                if (_revPtLabel  != null) _revPtLabel.text = $"Pt {_revSelIdx}  R={pt.x:F3}  Y={pt.y:F3}";
                _revPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, 0f,  2f));
                _revPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _revPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -1f, 2f));
                _revPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _revPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _revPtRow.style.display = DisplayStyle.None;
            }
        }

        // ── キャンバスポインターイベント ─────────────────────────────────

        private void OnRevCanvasPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;

            // 下絵移動モード
            if (_revBgMode && _revBgTex != null)
            {
                _revBgDrag              = true;
                _revBgDragStart         = e.localPosition;
                _revBgOffsetOnDragStart = _revBgOffset;
                _revCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureRevProfile();

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 1. 頂点ヒット判定（優先、15px以内）
            int ptIdx = RevolutionProfileEditCore.FindClosest(_revProfile, cp, w, h, 15f);
            if (ptIdx >= 0)
            {
                _revSelIdx  = ptIdx;
                _revDrag    = true;
                _revDragIdx = ptIdx;
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 2. セグメントヒット判定（10px以内）→ 即時挿入＆ドラッグ開始
            int   bestSeg  = -1;
            float bestDist = 10f;
            Vector2 insertProf = Vector2.zero;
            int   segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                int   j  = (i + 1) % _revProfile.Count;
                var   a  = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[i], w, h);
                var   b  = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[j], w, h);
                float t  = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d  = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                if (d < bestDist)
                {
                    bestDist   = d;
                    bestSeg    = i;
                    // 挿入座標をプロファイル空間で計算
                    insertProf = Vector2.Lerp(_revProfile[i], _revProfile[j], t);
                    insertProf.x = Mathf.Max(0f, insertProf.x);
                }
            }
            if (bestSeg >= 0)
            {
                int insertIdx = bestSeg + 1;
                _revProfile.Insert(insertIdx, insertProf);
                _revSelIdx  = insertIdx;
                _revDrag    = true;
                _revDragIdx = insertIdx;
                _revHoverEI = -1;
                _revCanvas.CapturePointer(e.pointerId);
                _revP.CurrentPreset = ProfilePreset.Custom;
                D(); RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 3. 何もヒットしない
            _revSelIdx = -1;
            RefreshRevCanvas(); RefreshRevPointUI();
            e.StopPropagation();
        }

        private void OnRevCanvasPointerMove(PointerMoveEvent e)
        {
            float w  = _revCanvas.resolvedStyle.width;
            float h  = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 下絵移動モード
            if (_revBgDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revBgOffset = _revBgOffsetOnDragStart + (cp - _revBgDragStart);
                UpdateRevBgEl();
                e.StopPropagation(); return;
            }

            if (_revDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                if (_revDragIdx >= 0 && _revProfile != null && _revDragIdx < _revProfile.Count)
                {
                    var pp = RevolutionProfileEditCore.CanvasToProfile(cp, w, h);
                    pp.x = Mathf.Max(0f, pp.x);
                    _revProfile[_revDragIdx] = pp;
                    _revP.CurrentPreset = ProfilePreset.Custom;
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                e.StopPropagation(); return;
            }

            // ホバーエッジ更新（非ドラッグ中）
            int prevHov = _revHoverEI;
            _revHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = RevolutionProfileEditCore.FindClosest(_revProfile, cp, w, h, 15f) >= 0;
            if (!nearPt)
            {
                int   segCount2 = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                float bestD     = 10f;
                for (int i = 0; i < segCount2; i++)
                {
                    int   j = (i + 1) % _revProfile.Count;
                    var   a = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[i], w, h);
                    var   b = RevolutionProfileEditCore.ProfileToCanvas(_revProfile[j], w, h);
                    float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                    float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                    if (d < bestD) { bestD = d; _revHoverEI = i; }
                }
            }

            if (_revHoverEI != prevHov) RefreshRevCanvas();
        }

        private void OnRevCanvasPointerUp(PointerUpEvent e)
        {
            if (!_revCanvas.HasPointerCapture(e.pointerId)) return;
            _revCanvas.ReleasePointer(e.pointerId);
            _revDrag    = false;
            _revBgDrag  = false;
            e.StopPropagation();
        }

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
            _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);

            c.Add(SL(T("Profile2D")));
            c.Add(NF(() => _p2dP.MeshName, v => _p2dP.MeshName = v));

            // ヘルプ
            var helpLbl = new Label(T("P2dEditorHelp"));
            helpLbl.style.fontSize = 9;
            helpLbl.style.color = new StyleColor(new Color(0.6f, 0.7f, 0.6f));
            helpLbl.style.marginBottom = 3;
            helpLbl.style.whiteSpace = WhiteSpace.Normal;
            c.Add(helpLbl);

            // ── 2D キャンバス ──────────────────────────────────────────────
            _p2dCanvas = new VisualElement();
            _p2dCanvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            _p2dCanvas.style.height          = 260;
            _p2dCanvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            _p2dCanvas.style.marginBottom    = 4;
            _p2dCanvas.style.borderTopWidth  = _p2dCanvas.style.borderBottomWidth =
            _p2dCanvas.style.borderLeftWidth = _p2dCanvas.style.borderRightWidth  = 1;
            _p2dCanvas.style.borderTopColor  = _p2dCanvas.style.borderBottomColor =
            _p2dCanvas.style.borderLeftColor = _p2dCanvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            _p2dCanvas.style.overflow        = Overflow.Hidden;
            _p2dCanvas.pickingMode           = PickingMode.Position;

            // 下絵レイヤー
            _p2dBgEl = new VisualElement();
            _p2dBgEl.style.position = Position.Absolute;
            _p2dBgEl.style.display  = DisplayStyle.None;
            _p2dBgEl.pickingMode    = PickingMode.Ignore;
            _p2dCanvas.Add(_p2dBgEl);

            _p2dCanvas.generateVisualContent += OnDrawP2dCanvas;
            _p2dCanvas.RegisterCallback<PointerDownEvent>(OnP2dPointerDown);
            _p2dCanvas.RegisterCallback<PointerMoveEvent>(OnP2dPointerMove);
            _p2dCanvas.RegisterCallback<PointerUpEvent>(OnP2dPointerUp);
            _p2dCanvas.RegisterCallback<WheelEvent>(e =>
            {
                if (_p2dBgMode)
                {
                    _p2dBgScale = Mathf.Clamp(_p2dBgScale * (1f - e.delta.y * 0.05f), 0.05f, 10f);
                    UpdateP2dBgEl(); RefreshP2dCanvas();
                }
                else
                {
                    _p2dZoom = Mathf.Clamp(_p2dZoom - e.delta.y * 0.05f, 0.2f, 5f);
                    RefreshP2dCanvas();
                }
                e.StopPropagation();
            });
            c.Add(_p2dCanvas);

            // ── 下絵セクション ─────────────────────────────────────────────
            BuildBgSection(c,
                T("BgImage"),
                () => _p2dBgPath, v => _p2dBgPath = v,
                () => _p2dBgAlpha, v => { _p2dBgAlpha = v; UpdateP2dBgEl(); },
                () => _p2dBgMode,  v => { _p2dBgMode  = v; },
                () =>
                {
                    if (string.IsNullOrEmpty(_p2dBgPath)) return;
                    LoadBgTexture(_p2dBgPath, ref _p2dBgTex, _p2dBgEl);
                    _p2dBgOffset = Vector2.zero; _p2dBgScale = 1f;
                    UpdateP2dBgEl();
                },
                () =>
                {
                    _p2dBgTex = null;
                    _p2dBgEl.style.display = DisplayStyle.None;
                    _p2dBgEl.style.backgroundImage = new StyleBackground();
                });

            // ── ループ操作ボタン行 ────────────────────────────────────────
            var loopBtnRow = new VisualElement(); loopBtnRow.style.flexDirection = FlexDirection.Row; loopBtnRow.style.marginBottom = 3;
            SB(loopBtnRow, "◀", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop - 1 + _p2dLoops.Count) % _p2dLoops.Count;
                _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, "▶", () =>
            {
                if (_p2dLoops.Count == 0) return;
                _p2dSelLoop = (_p2dSelLoop + 1) % _p2dLoops.Count;
                _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("DeletePoint"), () =>
            {
                if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                var lp = _p2dLoops[_p2dSelLoop];
                if (_p2dSelPt < 0 || _p2dSelPt >= lp.Points.Count || lp.Points.Count <= 3) return;
                lp.Points.RemoveAt(_p2dSelPt);
                _p2dSelPt = Mathf.Clamp(_p2dSelPt, 0, lp.Points.Count - 1);
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("AddLoop"), () =>
            {
                var lp = new Loop(); float r2 = 0.2f;
                lp.Points.AddRange(new[] {
                    new Vector2(-r2,-r2), new Vector2(r2,-r2),
                    new Vector2(r2, r2),  new Vector2(-r2, r2) });
                _p2dLoops.Add(lp);
                _p2dSelLoop = _p2dLoops.Count - 1; _p2dSelPt = -1;
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            SB(loopBtnRow, T("RemoveLoop"), () =>
            {
                if (_p2dLoops.Count <= 1 || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return;
                _p2dLoops.RemoveAt(_p2dSelLoop);
                _p2dSelLoop = Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);
                _p2dSelPt = -1; D(); RefreshP2dCanvas(); RefreshP2dPointUI();
            });
            c.Add(loopBtnRow);

            // ── ループ一覧（穴フラグ切替） ─────────────────────────────────
            c.Add(SL(T("Loops")));
            for (int i = 0; i < _p2dLoops.Count; i++)
            {
                int li = i;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
                var selBtn = new Button(() =>
                {
                    _p2dSelLoop = li; _p2dSelPt = -1; RefreshP2dCanvas(); RefreshP2dPointUI();
                })
                { text = $"Loop {i} ({_p2dLoops[i].Points.Count}pt)" };
                selBtn.style.flexGrow = 1; selBtn.style.fontSize = 9; selBtn.style.height = 18;
                selBtn.style.backgroundColor = (i == _p2dSelLoop)
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                var holeTog = new Toggle(T("IsHole")) { value = _p2dLoops[i].IsHole };
                holeTog.RegisterValueChangedCallback(e => { _p2dLoops[li].IsHole = e.newValue; D(); RefreshP2dCanvas(); });
                row.Add(selBtn); row.Add(holeTog);
                c.Add(row);
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
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    xSl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(e.newValue, lp.Points[_p2dSelPt].y);
                    D(); RefreshP2dCanvas();
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
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (!P2dGetSelPt(out var lp, out _)) return;
                    ySl.SetValueWithoutNotify(e.newValue); lp.Points[_p2dSelPt] = new Vector2(lp.Points[_p2dSelPt].x, e.newValue);
                    D(); RefreshP2dCanvas();
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                _p2dPtRow.Add(yRow);
                _p2dPtYSlider = ySl; _p2dPtYField = yFf;
            }
            _p2dPtRow.style.display = DisplayStyle.None;
            c.Add(_p2dPtRow);

            // ── CSV ────────────────────────────────────────────────────────
            c.Add(SL(T("LoadCSV")));
            var csvTf = new TextField { value = _p2dCsvPath }; csvTf.style.marginBottom = 2;
            csvTf.RegisterValueChangedCallback(e => _p2dCsvPath = e.newValue);
            c.Add(csvTf);
            var csvRow = new VisualElement(); csvRow.style.flexDirection = FlexDirection.Row; csvRow.style.marginBottom = 4;
            SB(csvRow, T("LoadCSV"), () =>
            {
                if (string.IsNullOrEmpty(_p2dCsvPath)) return;
                try
                {
                    var lines = System.IO.File.ReadAllLines(_p2dCsvPath);
                    var loaded = ParseProfile2DCSV(lines);
                    if (loaded != null) { _p2dLoops = loaded; _p2dSelLoop = 0; _p2dSelPt = -1; D(); RebuildSettings(); }
                }
                catch (System.Exception ex) { Debug.LogWarning($"[P2D CSV] {ex.Message}"); }
            });
            SB(csvRow, T("SaveCSV"), () =>
            {
                if (string.IsNullOrEmpty(_p2dCsvPath) || _p2dLoops == null) return;
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
            });
            c.Add(csvRow);

            // ── パラメータ ────────────────────────────────────────────────
            c.Add(SL(T("Scale")));
            c.Add(SR(T("Scale"),   0.01f, 10f, () => _p2dP.Scale,    v => { _p2dP.Scale    = v; D(); }));
            c.Add(SR(T("OffsetX"), -5f,   5f,  () => _p2dP.Offset.x, v => { _p2dP.Offset = new Vector2(v, _p2dP.Offset.y); D(); }));
            c.Add(SR(T("OffsetY"), -5f,   5f,  () => _p2dP.Offset.y, v => { _p2dP.Offset = new Vector2(_p2dP.Offset.x, v); D(); }));
            c.Add(TR(T("FlipY"),               () => _p2dP.FlipY,    v => { _p2dP.FlipY    = v; D(); }));
            c.Add(SR(T("Thickness"), 0f, 2f,   () => _p2dP.Thickness, v => { _p2dP.Thickness = v; D(); }));

            if (_p2dP.Thickness > 0.001f)
            {
                c.Add(SL(T("EdgeSettings")));
                c.Add(IR(T("FrontSegments"), 0, 16, () => _p2dP.SegmentsFront, v => { _p2dP.SegmentsFront = v; D(); }));
                if (_p2dP.SegmentsFront > 0)
                    c.Add(SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeFront, v => { _p2dP.EdgeSizeFront = v; D(); }));
                c.Add(IR(T("BackSegments"),  0, 16, () => _p2dP.SegmentsBack, v => { _p2dP.SegmentsBack = v; D(); }));
                if (_p2dP.SegmentsBack > 0)
                    c.Add(SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeBack, v => { _p2dP.EdgeSizeBack = v; D(); }));
                if (_p2dP.SegmentsFront > 0 || _p2dP.SegmentsBack > 0)
                    c.Add(TR(T("EdgeInward"), () => _p2dP.EdgeInward, v => { _p2dP.EdgeInward = v; D(); }));
            }

            c.Add(CB());
        }

        // ── P2D 座標変換 ──────────────────────────────────────────────────

        private Vector2 P2dWorldToCanvas(Vector2 world, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2(w * 0.5f + world.x * scale + _p2dOffset.x,
                               h * 0.5f - world.y * scale + _p2dOffset.y);
        }

        private Vector2 P2dCanvasToWorld(Vector2 canvas, float w, float h)
        {
            float scale = Mathf.Min(w, h) * 0.4f * _p2dZoom;
            return new Vector2( (canvas.x - w * 0.5f - _p2dOffset.x) / scale,
                               -(canvas.y - h * 0.5f - _p2dOffset.y) / scale);
        }

        /// <summary>点→セグメント最近傍距離と位置を返す</summary>
        private static float P2dDistToSeg(Vector2 pt, Vector2 a, Vector2 b, out Vector2 closest)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(pt - a, ab) / ab.sqrMagnitude);
            closest = a + ab * t;
            return Vector2.Distance(pt, closest);
        }

        // ── P2D キャンバス描画 ────────────────────────────────────────────

        private void OnDrawP2dCanvas(MeshGenerationContext ctx)
        {
            if (_p2dLoops == null || _p2dLoops.Count == 0) return;
            float w = _p2dCanvas.resolvedStyle.width;
            float h = _p2dCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.27f, 0.27f, 0.32f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float gx = -5f; gx <= 5f; gx += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(gx, -5f), w, h);
                var e = P2dWorldToCanvas(new Vector2(gx,  5f), w, h);
                if (s.x >= 0 && s.x <= w) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            for (float gy = -5f; gy <= 5f; gy += 0.5f)
            {
                var s = P2dWorldToCanvas(new Vector2(-5f, gy), w, h);
                var e = P2dWorldToCanvas(new Vector2( 5f, gy), w, h);
                if (s.y >= 0 && s.y <= h) { p2d.MoveTo(s); p2d.LineTo(e); }
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.5f, 0.5f, 0.55f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(-5f, 0f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2( 5f, 0f), w, h));
            p2d.MoveTo(P2dWorldToCanvas(new Vector2(0f, -5f), w, h));
            p2d.LineTo(P2dWorldToCanvas(new Vector2(0f,  5f), w, h));
            p2d.Stroke();

            // ループ描画
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;

                Color lineColor = (li == _p2dSelLoop) ? Color.yellow
                                : lp.IsHole           ? new Color(1f, 0.3f, 0.3f)
                                :                       new Color(0.2f, 0.75f, 0.85f);

                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int   nxt = (ei + 1) % lp.Points.Count;
                    var   a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var   b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    bool  hov = (li == _p2dHoverEL && ei == _p2dHoverEI);

                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : lineColor;
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }

                // 頂点ドット
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    bool sel = (li == _p2dSelLoop && pi == _p2dSelPt);
                    var  sp  = P2dWorldToCanvas(lp.Points[pi], w, h);
                    p2d.fillColor = sel ? Color.white : lineColor;
                    float r = sel ? 5.5f : 3.5f;
                    RevFillCircle(p2d, sp, r, 10);
                }
            }
        }

        private void RefreshP2dCanvas() => _p2dCanvas?.MarkDirtyRepaint();

        private bool P2dGetSelPt(out Loop loop, out Vector2 pt)
        {
            loop = null; pt = Vector2.zero;
            if (_p2dLoops == null || _p2dSelLoop < 0 || _p2dSelLoop >= _p2dLoops.Count) return false;
            loop = _p2dLoops[_p2dSelLoop];
            if (_p2dSelPt < 0 || _p2dSelPt >= loop.Points.Count) return false;
            pt = loop.Points[_p2dSelPt];
            return true;
        }

        private void RefreshP2dPointUI()
        {
            if (_p2dPtRow == null) return;
            if (P2dGetSelPt(out _, out var pt))
            {
                _p2dPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, -5f, 5f));
                _p2dPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _p2dPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -5f, 5f));
                _p2dPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _p2dPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _p2dPtRow.style.display = DisplayStyle.None;
            }
        }

        // ── P2D ポインターイベント ────────────────────────────────────────

        private void OnP2dPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;

            // 下絵移動モード
            if (_p2dBgMode && _p2dBgTex != null)
            {
                _p2dBgDrag              = true;
                _p2dBgDragStart         = e.localPosition;
                _p2dBgOffsetOnDragStart = _p2dBgOffset;
                _p2dCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureP2DLoops();
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 1. 頂点ヒット判定（全ループ、15px以内）
            int   bestL = -1, bestP = -1;
            float bestD = 15f;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                for (int pi = 0; pi < lp.Points.Count; pi++)
                {
                    float d = Vector2.Distance(cp, P2dWorldToCanvas(lp.Points[pi], w, h));
                    if (d < bestD) { bestD = d; bestL = li; bestP = pi; }
                }
            }
            if (bestL >= 0)
            {
                _p2dSelLoop = bestL; _p2dSelPt = bestP;
                _p2dDrag = true; _p2dDragIdx = bestP;
                _p2dCanvas.CapturePointer(e.pointerId);
                RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 2. エッジヒット判定（10px以内）→ 即時挿入
            int   edgeL = -1, edgeI = -1;
            float edgeD = 10f;
            Vector2 insertWorld = Vector2.zero;
            for (int li = 0; li < _p2dLoops.Count; li++)
            {
                var lp = _p2dLoops[li];
                if (lp.Points.Count < 2) continue;
                for (int ei = 0; ei < lp.Points.Count; ei++)
                {
                    int nxt = (ei + 1) % lp.Points.Count;
                    var a   = P2dWorldToCanvas(lp.Points[ei],  w, h);
                    var b   = P2dWorldToCanvas(lp.Points[nxt], w, h);
                    float d = P2dDistToSeg(cp, a, b, out var closest);
                    if (d < edgeD)
                    {
                        edgeD  = d; edgeL  = li; edgeI  = ei;
                        insertWorld = P2dCanvasToWorld(closest, w, h);
                    }
                }
            }
            if (edgeL >= 0)
            {
                int insertIdx = edgeI + 1;
                _p2dLoops[edgeL].Points.Insert(insertIdx, insertWorld);
                _p2dSelLoop = edgeL; _p2dSelPt = insertIdx;
                _p2dDrag = true; _p2dDragIdx = insertIdx;
                _p2dCanvas.CapturePointer(e.pointerId);
                D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                e.StopPropagation(); return;
            }

            // 3. 何もヒットしない
            _p2dSelPt = -1;
            RefreshP2dCanvas(); RefreshP2dPointUI();
            e.StopPropagation();
        }

        private void OnP2dPointerMove(PointerMoveEvent e)
        {
            float w  = _p2dCanvas.resolvedStyle.width;
            float h  = _p2dCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 下絵移動モード
            if (_p2dBgDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                _p2dBgOffset = _p2dBgOffsetOnDragStart + (cp - _p2dBgDragStart);
                UpdateP2dBgEl();
                e.StopPropagation(); return;
            }

            if (_p2dDrag && _p2dCanvas.HasPointerCapture(e.pointerId))
            {
                if (_p2dSelLoop >= 0 && _p2dSelLoop < _p2dLoops.Count &&
                    _p2dDragIdx >= 0 && _p2dDragIdx < _p2dLoops[_p2dSelLoop].Points.Count)
                {
                    _p2dLoops[_p2dSelLoop].Points[_p2dDragIdx] = P2dCanvasToWorld(cp, w, h);
                    D(); RefreshP2dCanvas(); RefreshP2dPointUI();
                }
                e.StopPropagation(); return;
            }

            // ホバーエッジ更新（非ドラッグ中）
            int   prevEL = _p2dHoverEL, prevEI = _p2dHoverEI;
            _p2dHoverEL = -1; _p2dHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = false;
            foreach (var lp in _p2dLoops)
                foreach (var pt in lp.Points)
                    if (Vector2.Distance(cp, P2dWorldToCanvas(pt, w, h)) < 15f) { nearPt = true; break; }

            if (!nearPt)
            {
                float bestD = 10f;
                for (int li = 0; li < _p2dLoops.Count; li++)
                {
                    var lp = _p2dLoops[li];
                    if (lp.Points.Count < 2) continue;
                    for (int ei = 0; ei < lp.Points.Count; ei++)
                    {
                        int nxt = (ei + 1) % lp.Points.Count;
                        float d = P2dDistToSeg(cp,
                            P2dWorldToCanvas(lp.Points[ei],  w, h),
                            P2dWorldToCanvas(lp.Points[nxt], w, h), out _);
                        if (d < bestD) { bestD = d; _p2dHoverEL = li; _p2dHoverEI = ei; }
                    }
                }
            }

            if (_p2dHoverEL != prevEL || _p2dHoverEI != prevEI)
                RefreshP2dCanvas();
        }

        private void OnP2dPointerUp(PointerUpEvent e)
        {
            if (!_p2dCanvas.HasPointerCapture(e.pointerId)) return;
            _p2dCanvas.ReleasePointer(e.pointerId);
            _p2dDrag   = false;
            _p2dBgDrag = false;
            e.StopPropagation();
        }

        // ================================================================
        // 下絵ヘルパー（Rev/P2d 共用）
        // ================================================================

        /// <summary>下絵セクションUIを構築する共通メソッド</summary>
        private void BuildBgSection(VisualElement c, string sectionLabel,
            Func<string> getPath, Action<string> setPath,
            Func<float> getAlpha, Action<float> setAlpha,
            Func<bool>  getMode,  Action<bool>  setMode,
            Action onLoad, Action onClear)
        {
            c.Add(SL(sectionLabel));

            // パス入力 + Browseボタン行
            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.marginBottom  = 2;

            var pathField = new TextField { value = getPath() };
            pathField.style.flexGrow = 1;
            pathField.style.marginRight = 2;
            pathField.RegisterValueChangedCallback(e => setPath(e.newValue));

            var browseBtn = new Button(() =>
            {
                string dir  = string.IsNullOrEmpty(getPath())
                    ? UnityEngine.Application.dataPath
                    : System.IO.Path.GetDirectoryName(getPath());
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel("Select Image", dir, "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path))
                {
                    pathField.value = path;
                    setPath(path);
                    onLoad();
                }
            }) { text = "..." };
            browseBtn.style.width = 28;

            pathRow.Add(pathField);
            pathRow.Add(browseBtn);
            c.Add(pathRow);

            // Load / Clear ボタン行
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            SB(row, T("BgLoad"),  onLoad);
            SB(row, T("BgClear"), onClear);
            c.Add(row);

            // アルファスライダー
            c.Add(SR(T("BgAlpha"), 0f, 1f, getAlpha, v => setAlpha(v)));

            // 移動モードトグル
            var modeRow = new VisualElement(); modeRow.style.flexDirection = FlexDirection.Row; modeRow.style.marginBottom = 4;
            var modeToggle = new Toggle(T("BgMoveMode")) { value = getMode() };
            modeToggle.style.flexGrow = 1;
            modeToggle.RegisterValueChangedCallback(e => setMode(e.newValue));
            modeRow.Add(modeToggle);
            c.Add(modeRow);
        }

        /// <summary>テクスチャをファイルパスから読み込んでBgElに設定</summary>
        private static void LoadBgTexture(string path, ref Texture2D tex, VisualElement bgEl)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (tex == null)
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Debug.LogWarning($"[BgImage] LoadImage failed: {path}");
                    return;
                }
                bgEl.style.backgroundImage = new StyleBackground(tex);
                bgEl.style.display         = DisplayStyle.Flex;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BgImage] Load failed: {ex.Message}");
            }
        }

        /// <summary>Rev 下絵 VisualElement の位置・サイズ・アルファを更新</summary>
        private void UpdateRevBgEl()
        {
            if (_revBgEl == null || _revBgTex == null) return;
            float cw = _revCanvas.resolvedStyle.width;
            float ch = _revCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _revBgTex.width  * _revBgScale;
            float bh = _revBgTex.height * _revBgScale;
            float cx = (cw - bw) * 0.5f + _revBgOffset.x;
            float cy = (ch - bh) * 0.5f + _revBgOffset.y;
            _revBgEl.style.left   = cx; _revBgEl.style.top    = cy;
            _revBgEl.style.width  = bw; _revBgEl.style.height = bh;
            _revBgEl.style.opacity = _revBgAlpha;
            _revBgEl.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));
        }

        /// <summary>P2D 下絵 VisualElement の位置・サイズ・アルファを更新</summary>
        private void UpdateP2dBgEl()
        {
            if (_p2dBgEl == null || _p2dBgTex == null) return;
            float cw = _p2dCanvas.resolvedStyle.width;
            float ch = _p2dCanvas.resolvedStyle.height;
            if (cw <= 0 || ch <= 0) return;
            float bw = _p2dBgTex.width  * _p2dBgScale;
            float bh = _p2dBgTex.height * _p2dBgScale;
            float cx = (cw - bw) * 0.5f + _p2dBgOffset.x;
            float cy = (ch - bh) * 0.5f + _p2dBgOffset.y;
            _p2dBgEl.style.left   = cx; _p2dBgEl.style.top    = cy;
            _p2dBgEl.style.width  = bw; _p2dBgEl.style.height = bh;
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

        // ================================================================
        // NohMask UI（変更なし）
        // ================================================================

        private void BuildNohMaskUI(VisualElement c)
        {
            c.Add(SL(T("NohMask")));
            c.Add(NF(() => _nohP.MeshName, v => _nohP.MeshName = v));

            c.Add(SL(T("Landmarks")));
            var lmLabel = new Label(string.IsNullOrEmpty(_nohP.LandmarksFilePath)
                ? T("NotSelected") : System.IO.Path.GetFileName(_nohP.LandmarksFilePath));
            lmLabel.style.fontSize = 10; lmLabel.style.marginBottom = 2;
            c.Add(lmLabel);
            var lmBtn = new Button(() =>
            {
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel("Open Landmarks JSON", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                _nohP.LandmarksFilePath = path; lmLabel.text = System.IO.Path.GetFileName(path); D();
            }) { text = "..." };
            lmBtn.style.marginBottom = 3; c.Add(lmBtn);

            c.Add(SL(T("TrianglesJson")));
            var triLabel = new Label(string.IsNullOrEmpty(_nohP.TrianglesFilePath)
                ? T("NotSelected") : System.IO.Path.GetFileName(_nohP.TrianglesFilePath));
            triLabel.style.fontSize = 10; triLabel.style.marginBottom = 2;
            c.Add(triLabel);
            var triBtn = new Button(() =>
            {
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel("Open Triangles JSON", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                _nohP.TrianglesFilePath = path; triLabel.text = System.IO.Path.GetFileName(path); D();
            }) { text = "..." };
            triBtn.style.marginBottom = 3; c.Add(triBtn);

            c.Add(SR(T("Scale"),      1f,  10f, () => _nohP.Scale,      v => { _nohP.Scale      = v; D(); }));
            c.Add(SR(T("DepthScale"), 0.1f, 5f, () => _nohP.DepthScale, v => { _nohP.DepthScale = v; D(); }));
            c.Add(TR(T("FlipFaces"),           () => _nohP.FlipFaces,   v => { _nohP.FlipFaces  = v; D(); }));
            c.Add(IR(T("FaceIndex"), 0, 10,    () => _nohP.FaceIndex,   v => { _nohP.FaceIndex  = v; D(); }));

            c.Add(CB());
        }

        // ================================================================
        // 生成
        // ================================================================

        private MeshObject Generate()
        {
            switch (_current)
            {
                case ShapeKind.Cube:      return CubeMeshGenerator.Generate(_cubeP);
                case ShapeKind.Sphere:    return SphereMeshGenerator.Generate(_sphereP);
                case ShapeKind.Cylinder:  return CylinderMeshGenerator.Generate(_cylP);
                case ShapeKind.Capsule:   return CapsuleMeshGenerator.Generate(_capsP);
                case ShapeKind.Plane:     return PlaneMeshGenerator.Generate(_planeP);
                case ShapeKind.Pyramid:   return PyramidMeshGenerator.Generate(_pyramidP);
                case ShapeKind.Revolution:
                    EnsureRevProfile();
                    return RevolutionMeshGenerator.Generate(_revProfile, _revP);
                case ShapeKind.Profile2D:
                    EnsureP2DLoops();
                    return Profile2DExtrudeMeshGenerator.Generate(_p2dLoops, _p2dP.MeshName,
                        new Profile2DGenerateParams
                        {
                            Scale          = _p2dP.Scale,
                            Offset         = _p2dP.Offset,
                            FlipY          = _p2dP.FlipY,
                            Thickness      = _p2dP.Thickness,
                            SegmentsFront  = _p2dP.SegmentsFront,
                            SegmentsBack   = _p2dP.SegmentsBack,
                            EdgeSizeFront  = _p2dP.EdgeSizeFront,
                            EdgeSizeBack   = _p2dP.EdgeSizeBack,
                            EdgeInward     = _p2dP.EdgeInward,
                        });
                case ShapeKind.NohMask:
                    return NohMaskMeshGenerator.GenerateFromFiles(_nohP);
                default: return null;
            }
        }

        private string Name()
        {
            switch (_current)
            {
                case ShapeKind.Cube:       return _cubeP.MeshName;
                case ShapeKind.Sphere:     return _sphereP.MeshName;
                case ShapeKind.Cylinder:   return _cylP.MeshName;
                case ShapeKind.Capsule:    return _capsP.MeshName;
                case ShapeKind.Plane:      return _planeP.MeshName;
                case ShapeKind.Pyramid:    return _pyramidP.MeshName;
                case ShapeKind.Revolution: return _revP.MeshName;
                case ShapeKind.Profile2D:  return _p2dP.MeshName;
                case ShapeKind.NohMask:    return _nohP.MeshName;
                default:                   return _current.ToString();
            }
        }

        // ================================================================
        // ワイヤーフレームMesh生成
        // ================================================================

        private static Mesh BuildWire(MeshObject mo)
        {
            var verts = new Vector3[mo.VertexCount];
            for (int i = 0; i < verts.Length; i++) verts[i] = mo.Vertices[i].Position;
            var set = new HashSet<(int,int)>();
            var idx = new List<int>();
            foreach (var f in mo.Faces)
            {
                int n = f.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i], b = f.VertexIndices[(i+1)%n];
                    var e = a < b ? (a,b) : (b,a);
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

        // ================================================================
        // 生成ボタン
        // ================================================================

        private VisualElement CB()
        {
            var btn = new Button(() =>
            {
                try
                {
                    var mo = Generate();
                    if (mo == null) { _statusLabel.text = "生成失敗"; return; }
                    _statusLabel.text = T("VertsFaces", mo.VertexCount, mo.FaceCount);
                    OnMeshCreated?.Invoke(mo, Name(), _worldPos, _ignorePoseInArmature);
                }
                catch (Exception ex)
                {
                    _statusLabel.text = $"Error: {ex.Message}";
                    Debug.LogException(ex);
                }
            }) { text = T("Create") };
            btn.style.height = 28; btn.style.marginTop = 6;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new StyleColor(new Color(0.22f, 0.48f, 0.22f));
            return btn;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

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

        private static VisualElement NF(Func<string> get, Action<string> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            row.Add(ML(T("Name")));
            var f = new TextField { value = get() }; f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(f); return row;
        }

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
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
    }
}
