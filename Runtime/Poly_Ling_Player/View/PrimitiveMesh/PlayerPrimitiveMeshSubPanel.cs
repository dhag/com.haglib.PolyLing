// PlayerPrimitiveMeshSubPanel.cs
// 図形生成サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Action<MeshObject, string> OnMeshCreated;

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

        // マウス状態（プレビューパネル直接操作用）
        private bool    _mouseDragging;
        private int     _mouseBtn;       // 1=orbit, 2=pan
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

            // プレビューカメラ
            _preview = new PrimitivePreviewViewport();
            _preview.Initialize(sceneRoot);

            // RTリサイズ
            _previewEl.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _preview.Resize(Mathf.Max(1,(int)e.newRect.width), Mathf.Max(1,(int)e.newRect.height));
            });

            // ── マウス操作を _previewEl に直接登録 ──
            // 右ドラッグ: オービット  / 中ドラッグ or Ctrl+左ドラッグ: パン
            _previewEl.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && !e.ctrlKey) return;   // 通常左クリックは無視
                _previewEl.CapturePointer(e.pointerId);
                _mouseDragging = false;
                _mouseBtn      = (e.button == 0) ? 2 : e.button; // Ctrl+左 → パン扱い
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

                if (_mouseBtn == 1) // オービット
                    _preview.Orbit.SimulateOrbit(delta.x, delta.y);
                else                // パン（UIToolkitはY↓なのでY反転）
                    _preview.Orbit.SimulatePan(delta.x, -delta.y);

                e.StopPropagation();
            });
            _previewEl.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_previewEl.HasPointerCapture(e.pointerId)) return;
                _previewEl.ReleasePointer(e.pointerId);
                _mouseDragging = false;
                e.StopPropagation();
            });
            // スクロール: ズーム
            _previewEl.RegisterCallback<WheelEvent>(e =>
            {
                // delta.y: 下=正 → 手前スクロール(ズームイン)=負
                // OnScroll 規約: 正値=ズームイン なので符号反転
                float scroll = -e.delta.y * 0.1f;
                _preview.Orbit.SimulateScroll(scroll);
                e.StopPropagation();
            });

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
                case ShapeKind.Cube:     BuildCubeUI(_settingsContainer);     break;
                case ShapeKind.Sphere:   BuildSphereUI(_settingsContainer);   break;
                case ShapeKind.Cylinder: BuildCylinderUI(_settingsContainer); break;
                case ShapeKind.Capsule:  BuildCapsuleUI(_settingsContainer);  break;
                case ShapeKind.Plane:    BuildPlaneUI(_settingsContainer);    break;
                case ShapeKind.Pyramid:  BuildPyramidUI(_settingsContainer);  break;
                default:
                    var lbl = new Label(T("NotSupported"));
                    lbl.style.color = new StyleColor(new Color(0.8f,0.5f,0.3f));
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    _settingsContainer?.Add(lbl);
                    break;
            }
        }

        private void D() => _dirty = true; // ダーティフラグセット用ショートカット

        private void BuildCubeUI(VisualElement c)
        {
            c.Add(SL(T("Cube")));
            c.Add(NF(() => _cubeP.MeshName, v => _cubeP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("WidthX"),  0.1f,10f, ()=>_cubeP.WidthTop,      v=>{_cubeP.WidthTop=_cubeP.WidthBottom=v; D();}));
            c.Add(SR(T("HeightY"), 0.1f,10f, ()=>_cubeP.Height,         v=>{_cubeP.Height=v; D();}));
            c.Add(SR(T("DepthZ"),  0.1f,10f, ()=>_cubeP.DepthTop,       v=>{_cubeP.DepthTop=_cubeP.DepthBottom=v; D();}));
            c.Add(SL(T("CornerRadius")));
            c.Add(SR(T("CornerRadius"),0f,0.5f, ()=>_cubeP.CornerRadius,v=>{_cubeP.CornerRadius=v; D();}));
            c.Add(SL(T("Subdivisions")));
            c.Add(IR(T("SubdivX"),1,8, ()=>_cubeP.Subdivisions.x, v=>{_cubeP.Subdivisions=new Vector3Int(v,_cubeP.Subdivisions.y,_cubeP.Subdivisions.z); D();}));
            c.Add(IR(T("SubdivY"),1,8, ()=>_cubeP.Subdivisions.y, v=>{_cubeP.Subdivisions=new Vector3Int(_cubeP.Subdivisions.x,v,_cubeP.Subdivisions.z); D();}));
            c.Add(IR(T("SubdivZ"),1,8, ()=>_cubeP.Subdivisions.z, v=>{_cubeP.Subdivisions=new Vector3Int(_cubeP.Subdivisions.x,_cubeP.Subdivisions.y,v); D();}));
            c.Add(CB());
        }

        private void BuildSphereUI(VisualElement c)
        {
            c.Add(SL(T("Sphere")));
            c.Add(NF(() => _sphereP.MeshName, v => _sphereP.MeshName = v));
            c.Add(SR(T("Radius"),0.05f,5f, ()=>_sphereP.Radius, v=>{_sphereP.Radius=v; D();}));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Lateral"),4,64, ()=>_sphereP.LatitudeSegments,  v=>{_sphereP.LatitudeSegments=v; D();}));
            c.Add(IR(T("Radial"), 4,64, ()=>_sphereP.LongitudeSegments, v=>{_sphereP.LongitudeSegments=v; D();}));
            c.Add(TR(T("CubeSphere"), ()=>_sphereP.CubeSphere, v=>{_sphereP.CubeSphere=v; D();}));
            c.Add(CB());
        }

        private void BuildCylinderUI(VisualElement c)
        {
            c.Add(SL(T("Cylinder")));
            c.Add(NF(() => _cylP.MeshName, v => _cylP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),   0f,5f,   ()=>_cylP.RadiusTop,    v=>{_cylP.RadiusTop=v; D();}));
            c.Add(SR(T("RadiusBottom"),0f,5f,   ()=>_cylP.RadiusBottom, v=>{_cylP.RadiusBottom=v; D();}));
            c.Add(SR(T("Height"),      0.1f,10f,()=>_cylP.Height,       v=>{_cylP.Height=v; D();}));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"), 3,48, ()=>_cylP.RadialSegments, v=>{_cylP.RadialSegments=v; D();}));
            c.Add(IR(T("Lateral"),1,16, ()=>_cylP.HeightSegments, v=>{_cylP.HeightSegments=v; D();}));
            c.Add(TR(T("CapTop"),    ()=>_cylP.CapTop,    v=>{_cylP.CapTop=v; D();}));
            c.Add(TR(T("CapBottom"), ()=>_cylP.CapBottom, v=>{_cylP.CapBottom=v; D();}));
            c.Add(CB());
        }

        private void BuildCapsuleUI(VisualElement c)
        {
            c.Add(SL(T("Capsule")));
            c.Add(NF(() => _capsP.MeshName, v => _capsP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"),   0.1f,2f,  ()=>_capsP.RadiusTop,    v=>{_capsP.RadiusTop=v; D();}));
            c.Add(SR(T("RadiusBottom"),0.1f,2f,  ()=>_capsP.RadiusBottom, v=>{_capsP.RadiusBottom=v; D();}));
            c.Add(SR(T("Height"),      0.5f,10f, ()=>_capsP.Height,       v=>{_capsP.Height=v; D();}));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"), 8,48, ()=>_capsP.RadialSegments, v=>{_capsP.RadialSegments=v; D();}));
            c.Add(IR(T("Lateral"),1,16, ()=>_capsP.HeightSegments, v=>{_capsP.HeightSegments=v; D();}));
            c.Add(IR(T("Cap"),    2,16, ()=>_capsP.CapSegments,    v=>{_capsP.CapSegments=v; D();}));
            c.Add(CB());
        }

        private void BuildPlaneUI(VisualElement c)
        {
            c.Add(SL(T("Plane")));
            c.Add(NF(() => _planeP.MeshName, v => _planeP.MeshName = v));
            c.Add(SR(T("Width"), 0.1f,10f, ()=>_planeP.Width,  v=>{_planeP.Width=v; D();}));
            c.Add(SR(T("Height"),0.1f,10f, ()=>_planeP.Height, v=>{_planeP.Height=v; D();}));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Width"), 1,32, ()=>_planeP.WidthSegments,  v=>{_planeP.WidthSegments=v; D();}));
            c.Add(IR(T("Height"),1,32, ()=>_planeP.HeightSegments, v=>{_planeP.HeightSegments=v; D();}));
            var dd = new DropdownField(new List<string>{"XZ","XY","YZ"}, (int)_planeP.Orientation);
            dd.label = T("Orientation"); dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(e => { _planeP.Orientation=(PlaneOrientation)dd.index; D(); });
            c.Add(dd);
            c.Add(TR(T("DoubleSided"), ()=>_planeP.DoubleSided, v=>{_planeP.DoubleSided=v; D();}));
            c.Add(CB());
        }

        private void BuildPyramidUI(VisualElement c)
        {
            c.Add(SL(T("Pyramid")));
            c.Add(NF(() => _pyramidP.MeshName, v => _pyramidP.MeshName = v));
            c.Add(IR(T("Sides"),    3,16,    ()=>_pyramidP.Sides,       v=>{_pyramidP.Sides=v; D();}));
            c.Add(SR(T("BaseRadius"),0.1f,5f,()=>_pyramidP.BaseRadius, v=>{_pyramidP.BaseRadius=v; D();}));
            c.Add(SR(T("Height"),  0.1f,10f, ()=>_pyramidP.Height,     v=>{_pyramidP.Height=v; D();}));
            c.Add(SR(T("ApexOffset"),-1f,1f, ()=>_pyramidP.ApexOffset, v=>{_pyramidP.ApexOffset=v; D();}));
            c.Add(TR(T("CapBottom"), ()=>_pyramidP.CapBottom, v=>{_pyramidP.CapBottom=v; D();}));
            var pr = new VisualElement(); pr.style.flexDirection=FlexDirection.Row; pr.style.marginBottom=4;
            SB(pr,T("Bottom"),()=>{_pyramidP.Pivot=new Vector3(0,-0.5f,0); D();});
            SB(pr,T("Center"),()=>{_pyramidP.Pivot=Vector3.zero; D();});
            SB(pr,T("Top"),   ()=>{_pyramidP.Pivot=new Vector3(0,0.5f,0); D();});
            c.Add(pr);
            c.Add(CB());
        }

        // ================================================================
        // 生成
        // ================================================================

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
                default:                 return null;
            }
        }

        private string Name()
        {
            switch (_current)
            {
                case ShapeKind.Cube:     return _cubeP.MeshName;
                case ShapeKind.Sphere:   return _sphereP.MeshName;
                case ShapeKind.Cylinder: return _cylP.MeshName;
                case ShapeKind.Capsule:  return _capsP.MeshName;
                case ShapeKind.Plane:    return _planeP.MeshName;
                case ShapeKind.Pyramid:  return _pyramidP.MeshName;
                default:                 return _current.ToString();
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
                    var e = a<b?(a,b):(b,a);
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
                    OnMeshCreated?.Invoke(mo, Name());
                }
                catch (Exception ex)
                {
                    _statusLabel.text = $"Error: {ex.Message}";
                    Debug.LogException(ex);
                }
            }) { text = T("Create") };
            btn.style.height = 28; btn.style.marginTop = 6;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new StyleColor(new Color(0.22f,0.48f,0.22f));
            return btn;
        }

        // ================================================================
        // UIヘルパー（短縮名）
        // ================================================================

        private static Label SL(string t, bool bold=false)
        {
            var l = new Label(t);
            l.style.marginTop=bold?2:5; l.style.marginBottom=2;
            l.style.color = bold
                ? new StyleColor(new Color(0.9f,0.9f,0.9f))
                : new StyleColor(new Color(0.65f,0.8f,1f));
            l.style.fontSize = bold?11:10;
            if (bold) l.style.unityFontStyleAndWeight=FontStyle.Bold;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height=1; v.style.marginTop=4; v.style.marginBottom=4;
            v.style.backgroundColor=new StyleColor(new Color(1f,1f,1f,0.08f));
            return v;
        }

        private static VisualElement NF(Func<string> get, Action<string> set)
        {
            var row=new VisualElement(); row.style.flexDirection=FlexDirection.Row; row.style.marginBottom=3;
            row.Add(ML(T("Name")));
            var f=new TextField{value=get()}; f.style.flexGrow=1;
            f.RegisterValueChangedCallback(e=>set(e.newValue));
            row.Add(f); return row;
        }

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row=new VisualElement(); row.style.flexDirection=FlexDirection.Row; row.style.marginBottom=2;
            row.Add(ML(label));
            var sl=new Slider(min,max){value=get()}; sl.style.flexGrow=1;
            var nf=new FloatField{value=get()}; nf.style.width=42;
            sl.RegisterValueChangedCallback(e=>{nf.SetValueWithoutNotify((float)Math.Round(e.newValue,3)); set(e.newValue);});
            nf.RegisterValueChangedCallback(e=>{float v=Mathf.Clamp(e.newValue,min,max); sl.SetValueWithoutNotify(v); set(v);});
            row.Add(sl); row.Add(nf); return row;
        }

        private static VisualElement IR(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var row=new VisualElement(); row.style.flexDirection=FlexDirection.Row; row.style.marginBottom=2;
            row.Add(ML(label));
            var sl=new SliderInt(min,max){value=get()}; sl.style.flexGrow=1;
            var nf=new IntegerField{value=get()}; nf.style.width=36;
            sl.RegisterValueChangedCallback(e=>{nf.SetValueWithoutNotify(e.newValue); set(e.newValue);});
            nf.RegisterValueChangedCallback(e=>{int v=Mathf.Clamp(e.newValue,min,max); sl.SetValueWithoutNotify(v); set(v);});
            row.Add(sl); row.Add(nf); return row;
        }

        private static VisualElement TR(string label, Func<bool> get, Action<bool> set)
        {
            var t=new Toggle(label){value=get()}; t.style.marginBottom=2;
            t.style.color=new StyleColor(new Color(0.85f,0.85f,0.85f));
            t.RegisterValueChangedCallback(e=>set(e.newValue)); return t;
        }

        private static Label ML(string t)
        {
            var l=new Label(t); l.style.width=80;
            l.style.unityTextAlign=TextAnchor.MiddleLeft;
            l.style.color=new StyleColor(new Color(0.85f,0.85f,0.85f));
            l.style.fontSize=10; return l;
        }

        private static void SB(VisualElement p, string t, Action onClick)
        {
            var b=new Button(onClick){text=t}; b.style.flexGrow=1; b.style.marginRight=2;
            b.style.height=18; b.style.fontSize=9; p.Add(b);
        }
    }
}
