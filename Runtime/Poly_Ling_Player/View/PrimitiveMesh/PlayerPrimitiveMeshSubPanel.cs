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

        /// <summary>生成ボタン押下時。(MeshObject, meshName, worldPosition)</summary>
        public Action<MeshObject, string, Vector3> OnMeshCreated;

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
        private List<Vector2>                        _revProfile = null; // null → 初回にDefaultを生成
        private Profile2DParams                      _p2dP    = Profile2DParams.Default;
        private List<Loop>                           _p2dLoops = null;  // null → 初回にDefaultを生成
        private FaceMeshParams                       _nohP    = FaceMeshParams.Default;

        // ワールド生成位置
        private Vector3 _worldPos = Vector3.zero;

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

        // マウス状態
        private bool    _mouseDragging;
        private int     _mouseBtn;
        private Vector2 _mouseDownPos;
        private Vector2 _mousePrevPos;
        private const float DragThreshold = 3f;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, Transform sceneRoot)
        {
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
                    : new StyleColor(StyleKeyword.Null);
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
                case ShapeKind.Cube:     BuildCubeUI(_settingsContainer);          break;
                case ShapeKind.Sphere:   BuildSphereUI(_settingsContainer);        break;
                case ShapeKind.Cylinder: BuildCylinderUI(_settingsContainer);      break;
                case ShapeKind.Capsule:  BuildCapsuleUI(_settingsContainer);       break;
                case ShapeKind.Plane:    BuildPlaneUI(_settingsContainer);         break;
                case ShapeKind.Pyramid:  BuildPyramidUI(_settingsContainer);       break;
                case ShapeKind.Revolution: BuildRevolutionUI(_settingsContainer);  break;
                case ShapeKind.Profile2D:  BuildProfile2DUI(_settingsContainer);   break;
                case ShapeKind.NohMask:    BuildNohMaskUI(_settingsContainer);     break;
                default:
                    var lbl = new Label(T("NotSupported"));
                    lbl.style.color = new StyleColor(new Color(0.8f,0.5f,0.3f));
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    _settingsContainer?.Add(lbl);
                    break;
            }
        }

        private void D() => _dirty = true;

        private void BuildCubeUI(VisualElement c)
        {
            c.Add(SL(T("Cube")));
            c.Add(NF(() => _cubeP.MeshName, v => _cubeP.MeshName = v));

            // 連動オプション
            c.Add(TR(T("LinkWHD"),       () => _cubeP.LinkWHD,       v => { _cubeP.LinkWHD = v; if (v) { _cubeP.LinkTopBottom = true; } D(); }));
            c.Add(TR(T("LinkTopBottom"), () => _cubeP.LinkTopBottom,  v => { if (!_cubeP.LinkWHD) _cubeP.LinkTopBottom = v; D(); }));

            c.Add(SL(T("Size")));
            if (_cubeP.LinkWHD)
            {
                // 3辺連動：Wスライダ1本で代表
                c.Add(SR(T("WidthX"), 0.1f, 10f, () => _cubeP.WidthTop, v =>
                {
                    _cubeP.WidthTop = _cubeP.WidthBottom = _cubeP.DepthTop = _cubeP.DepthBottom = _cubeP.Height = v; D();
                }));
            }
            else
            {
                c.Add(SR(T("WidthX"),  0.1f, 10f, () => _cubeP.WidthTop,  v => { _cubeP.WidthTop  = v; if (_cubeP.LinkTopBottom) _cubeP.WidthBottom  = v; D(); }));
                c.Add(SR(T("HeightY"), 0.1f, 10f, () => _cubeP.Height,    v => { _cubeP.Height = v; D(); }));
                c.Add(SR(T("DepthZ"),  0.1f, 10f, () => _cubeP.DepthTop,  v => { _cubeP.DepthTop  = v; if (_cubeP.LinkTopBottom) _cubeP.DepthBottom  = v; D(); }));
                if (!_cubeP.LinkTopBottom)
                {
                    c.Add(SR("W Bot", 0.1f, 10f, () => _cubeP.WidthBottom,  v => { _cubeP.WidthBottom = v; D(); }));
                    c.Add(SR("D Bot", 0.1f, 10f, () => _cubeP.DepthBottom,  v => { _cubeP.DepthBottom = v; D(); }));
                }
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

            // エッジ丸め
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

        /// <summary>Y軸ピボットスライダ＋下/中央/上ボタン</summary>
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

        /// <summary>X/Y/Z軸ピボットスライダ＋下/中央/上ボタン</summary>
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
            c.Add(SL(T("Revolution")));
            c.Add(NF(() => _revP.MeshName, v => _revP.MeshName = v));
            c.Add(IR(T("RadialSegments"), 3, 64,   () => _revP.RadialSegments, v => { _revP.RadialSegments = v; D(); }));
            c.Add(TR(T("CloseTop"),    () => _revP.CloseTop,    v => { _revP.CloseTop    = v; D(); }));
            c.Add(TR(T("CloseBottom"), () => _revP.CloseBottom, v => { _revP.CloseBottom = v; D(); }));
            c.Add(TR(T("CloseLoop"),   () => _revP.CloseLoop,   v => { _revP.CloseLoop   = v; D(); }));
            c.Add(TR(T("Spiral"),      () => _revP.Spiral,      v => { _revP.Spiral      = v; D(); }));
            if (_revP.Spiral)
            {
                c.Add(IR(T("SpiralTurns"), 1, 10,     () => _revP.SpiralTurns,  v => { _revP.SpiralTurns  = v; D(); }));
                c.Add(SR(T("SpiralPitch"), -2f, 2f,   () => _revP.SpiralPitch,  v => { _revP.SpiralPitch  = v; D(); }));
            }
            c.Add(TR(T("FlipY"), () => _revP.FlipY, v => { _revP.FlipY = v; D(); }));
            c.Add(TR(T("FlipZ"), () => _revP.FlipZ, v => { _revP.FlipZ = v; D(); }));

            // ピボット Y
            BuildPivotY(c,
                () => _revP.Pivot.y, v => { _revP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0));

            // プリセット
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
                }
                D();
            });
            c.Add(presetDd);

            // ドーナツ設定
            if (_revP.CurrentPreset == ProfilePreset.Donut)
            {
                c.Add(SL(T("Donut")));
                c.Add(SR(T("DonutMajorRadius"), 0.2f, 2f,   () => _revP.DonutMajorRadius, v => { _revP.DonutMajorRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("DonutMinorRadius"), 0.05f, 1f,  () => _revP.DonutMinorRadius, v => { _revP.DonutMinorRadius = v; ApplyRevPreset(); }));
                c.Add(IR(T("DonutTubeSegs"),    4, 32,      () => _revP.DonutTubeSegments,v => { _revP.DonutTubeSegments= v; ApplyRevPreset(); }));
            }
            // パイプ設定
            if (_revP.CurrentPreset == ProfilePreset.RoundedPipe)
            {
                c.Add(SL(T("RoundedPipe")));
                c.Add(SR(T("PipeInnerRadius"), 0.05f, 2f,  () => _revP.PipeInnerRadius, v => { _revP.PipeInnerRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeOuterRadius"), 0.06f, 3f,  () => _revP.PipeOuterRadius, v => { _revP.PipeOuterRadius = v; ApplyRevPreset(); }));
                c.Add(SR(T("PipeHeight"),      0.1f,  3f,  () => _revP.PipeHeight,      v => { _revP.PipeHeight      = v; ApplyRevPreset(); }));
                c.Add(SL(T("InnerCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeInnerCornerRadius,  v => { _revP.PipeInnerCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeInnerCornerSegments, v => { _revP.PipeInnerCornerSegments = v; ApplyRevPreset(); }));
                c.Add(SL(T("OuterCorner")));
                c.Add(SR(T("CornerRadius"), 0f, 0.5f, () => _revP.PipeOuterCornerRadius,  v => { _revP.PipeOuterCornerRadius  = v; ApplyRevPreset(); }));
                c.Add(IR(T("CornerSeg"),    1, 16,    () => _revP.PipeOuterCornerSegments, v => { _revP.PipeOuterCornerSegments = v; ApplyRevPreset(); }));
            }

            // プロファイル点リスト（簡易：点数表示 + クリア + デフォルト）
            c.Add(SL(T("ProfileEditor")));
            var profileInfo = new Label($"Points: {_revProfile?.Count ?? 0}");
            profileInfo.style.color  = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            profileInfo.style.fontSize = 10;
            c.Add(profileInfo);

            var clearBtn = new Button(() => { _revProfile = RevolutionProfileGenerator.CreateDefault(); profileInfo.text = $"Points: {_revProfile.Count}"; D(); })
                { text = T("ClearProfile") };
            clearBtn.style.marginBottom = 3;
            c.Add(clearBtn);

            c.Add(CB());
        }

        private void ApplyRevPreset()
        {
            if (_revP.CurrentPreset != ProfilePreset.Custom)
                _revProfile = RevolutionProfileGenerator.CreatePreset(_revP.CurrentPreset, ref _revP);
            D();
        }

        // ================================================================
        // Profile2D UI
        // ================================================================

        private void EnsureP2DLoops()
        {
            if (_p2dLoops != null) return;
            _p2dLoops = new List<Loop>();
            // デフォルト: 正方形外ループ
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
            c.Add(SL(T("Profile2D")));
            c.Add(NF(() => _p2dP.MeshName, v => _p2dP.MeshName = v));

            // ファイルロード（PLEditorBridge 経由）
            c.Add(SL(T("LoadCSV")));
            var csvLabel = new Label(string.IsNullOrEmpty(_p2dP.CsvPath)
                ? T("NoFile")
                : System.IO.Path.GetFileName(_p2dP.CsvPath));
            csvLabel.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            csvLabel.style.fontSize  = 10;
            csvLabel.style.marginBottom = 2;
            c.Add(csvLabel);

            var loadBtn = new Button(() =>
            {
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel(
                    "Open Profile2D CSV", "", "csv");
                if (string.IsNullOrEmpty(path)) return;
                _p2dP.CsvPath = path;
                csvLabel.text = System.IO.Path.GetFileName(path);
                try
                {
                    var lines = System.IO.File.ReadAllLines(path);
                    _p2dLoops = ParseProfile2DCSV(lines);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Profile2D] CSV load failed: {ex.Message}");
                }
                D();
            }) { text = T("LoadCSV") };
            loadBtn.style.marginBottom = 3;
            c.Add(loadBtn);

            c.Add(SR(T("Scale"),     0.01f, 10f, () => _p2dP.Scale,     v => { _p2dP.Scale     = v; D(); }));
            c.Add(SR(T("OffsetX"),   -5f,   5f,  () => _p2dP.Offset.x,  v => { _p2dP.Offset    = new Vector2(v, _p2dP.Offset.y); D(); }));
            c.Add(SR(T("OffsetY"),   -5f,   5f,  () => _p2dP.Offset.y,  v => { _p2dP.Offset    = new Vector2(_p2dP.Offset.x, v); D(); }));
            c.Add(TR(T("FlipY"),               () => _p2dP.FlipY,       v => { _p2dP.FlipY     = v; D(); }));
            c.Add(SR(T("Thickness"), 0f, 2f,    () => _p2dP.Thickness,  v => { _p2dP.Thickness = v; D(); }));

            if (_p2dP.Thickness > 0.001f)
            {
                c.Add(SL(T("EdgeSettings")));
                c.Add(IR(T("FrontSegments"), 0, 16,   () => _p2dP.SegmentsFront, v => { _p2dP.SegmentsFront = v; D(); }));
                if (_p2dP.SegmentsFront > 0)
                    c.Add(SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeFront, v => { _p2dP.EdgeSizeFront = v; D(); }));
                c.Add(IR(T("BackSegments"),  0, 16,   () => _p2dP.SegmentsBack,  v => { _p2dP.SegmentsBack  = v; D(); }));
                if (_p2dP.SegmentsBack > 0)
                    c.Add(SR(T("EdgeSize"), 0.01f, 0.5f, () => _p2dP.EdgeSizeBack,  v => { _p2dP.EdgeSizeBack  = v; D(); }));
                if (_p2dP.SegmentsFront > 0 || _p2dP.SegmentsBack > 0)
                    c.Add(TR(T("EdgeInward"), () => _p2dP.EdgeInward, v => { _p2dP.EdgeInward = v; D(); }));
            }

            // ループ一覧（ Hole 切替のみ）
            c.Add(SL(T("Loops")));
            for (int i = 0; i < _p2dLoops.Count; i++)
            {
                int idx = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom  = 2;
                var lbl = new Label($"Loop {i}  ({_p2dLoops[i].Points.Count}pt)");
                lbl.style.flexGrow = 1;
                lbl.style.color    = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
                lbl.style.fontSize = 10;
                var holeTog = new Toggle(T("IsHole")) { value = _p2dLoops[i].IsHole };
                holeTog.RegisterValueChangedCallback(e => { _p2dLoops[idx].IsHole = e.newValue; D(); });
                var remBtn = new Button(() => { _p2dLoops.RemoveAt(idx); D(); }) { text = T("RemoveLoop") };
                row.Add(lbl); row.Add(holeTog); row.Add(remBtn);
                c.Add(row);
            }

            var addLoopBtn = new Button(() =>
            {
                var loop = new Loop();
                float r2 = 0.2f;
                loop.Points.AddRange(new[]
                {
                    new Vector2(-r2, -r2), new Vector2( r2, -r2),
                    new Vector2( r2,  r2), new Vector2(-r2,  r2),
                });
                _p2dLoops.Add(loop);
                D();
            }) { text = T("AddLoop") };
            addLoopBtn.style.marginBottom = 3;
            c.Add(addLoopBtn);

            c.Add(CB());
        }

        /// <summary>簡易 Profile2D CSV パーサ。フォーマット: x,y[,IsHole] の行がループ区切り。</summary>
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
        // NohMask UI
        // ================================================================

        private void BuildNohMaskUI(VisualElement c)
        {
            c.Add(SL(T("NohMask")));
            c.Add(NF(() => _nohP.MeshName, v => _nohP.MeshName = v));

            // Landmarks JSON ファイル選択
            c.Add(SL(T("Landmarks")));
            var lmLabel = new Label(string.IsNullOrEmpty(_nohP.LandmarksFilePath)
                ? T("NotSelected") : System.IO.Path.GetFileName(_nohP.LandmarksFilePath));
            lmLabel.style.color    = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            lmLabel.style.fontSize = 10;
            lmLabel.style.marginBottom = 2;
            c.Add(lmLabel);

            var lmBtn = new Button(() =>
            {
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel(
                    "Open Landmarks JSON", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                _nohP.LandmarksFilePath = path;
                lmLabel.text = System.IO.Path.GetFileName(path);
                D();
            }) { text = "..." };
            lmBtn.style.marginBottom = 3;
            c.Add(lmBtn);

            // Triangles JSON ファイル選択
            c.Add(SL(T("TrianglesJson")));
            var triLabel = new Label(string.IsNullOrEmpty(_nohP.TrianglesFilePath)
                ? T("NotSelected") : System.IO.Path.GetFileName(_nohP.TrianglesFilePath));
            triLabel.style.color    = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            triLabel.style.fontSize = 10;
            triLabel.style.marginBottom = 2;
            c.Add(triLabel);

            var triBtn = new Button(() =>
            {
                string path = Poly_Ling.EditorBridge.PLEditorBridge.I.OpenFilePanel(
                    "Open Triangles JSON", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                _nohP.TrianglesFilePath = path;
                triLabel.text = System.IO.Path.GetFileName(path);
                D();
            }) { text = "..." };
            triBtn.style.marginBottom = 3;
            c.Add(triBtn);

            c.Add(SR(T("Scale"),      1f,  10f, () => _nohP.Scale,      v => { _nohP.Scale      = v; D(); }));
            c.Add(SR(T("DepthScale"), 0.1f, 5f, () => _nohP.DepthScale, v => { _nohP.DepthScale = v; D(); }));
            c.Add(TR(T("FlipFaces"),           () => _nohP.FlipFaces,   v => { _nohP.FlipFaces  = v; D(); }));
            c.Add(IR(T("FaceIndex"), 0, 10,    () => _nohP.FaceIndex,   v => { _nohP.FaceIndex  = v; D(); }));

            c.Add(CB());
        }

        private MeshObject Generate()
        {
            switch (_current)
            {
                case ShapeKind.Cube:     return CubeMeshGenerator.Generate(_cubeP);
                case ShapeKind.Sphere:   return SphereMeshGenerator.Generate(_sphereP);
                case ShapeKind.Cylinder: return CylinderMeshGenerator.Generate(_cylP);
                case ShapeKind.Capsule:  return CapsuleMeshGenerator.Generate(_capsP);
                case ShapeKind.Plane:    return PlaneMeshGenerator.Generate(_planeP);
                case ShapeKind.Pyramid:  return PyramidMeshGenerator.Generate(_pyramidP);
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
                case ShapeKind.Cube:      return _cubeP.MeshName;
                case ShapeKind.Sphere:    return _sphereP.MeshName;
                case ShapeKind.Cylinder:  return _cylP.MeshName;
                case ShapeKind.Capsule:   return _capsP.MeshName;
                case ShapeKind.Plane:     return _planeP.MeshName;
                case ShapeKind.Pyramid:   return _pyramidP.MeshName;
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
                    OnMeshCreated?.Invoke(mo, Name(), _worldPos);
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
            t.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            t.RegisterValueChangedCallback(e => set(e.newValue)); return t;
        }

        /// <summary>X/Y/Z それぞれ独立したFloatField 3つを1行に並べる</summary>
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
                l.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.75f)); l.style.fontSize = 10;
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
            l.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            l.style.fontSize = 10; return l;
        }

        private static void SB(VisualElement p, string t, Action onClick)
        {
            var b = new Button(onClick) { text = t }; b.style.flexGrow = 1; b.style.marginRight = 2;
            b.style.height = 18; b.style.fontSize = 9; p.Add(b);
        }
    }
}
