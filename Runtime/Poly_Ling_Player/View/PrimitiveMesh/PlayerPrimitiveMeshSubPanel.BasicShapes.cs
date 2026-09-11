// PlayerPrimitiveMeshSubPanel.BasicShapes.cs
// 図形生成サブパネル：基本図形（直方体・球・円柱・カプセル・平面・角錐）の既定値と諸元 UI。
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
        private static CubeMeshGenerator.CubeParams DefaultCubeParams()
        { var p = CubeMeshGenerator.CubeParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static SphereMeshGenerator.SphereParams DefaultSphereParams()
        { var p = SphereMeshGenerator.SphereParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static CylinderMeshGenerator.CylinderParams DefaultCylinderParams()
        { var p = CylinderMeshGenerator.CylinderParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static CapsuleMeshGenerator.CapsuleParams DefaultCapsuleParams()
        { var p = CapsuleMeshGenerator.CapsuleParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static PlaneMeshGenerator.PlaneParams DefaultPlaneParams()
        { var p = PlaneMeshGenerator.PlaneParams.Default; p.Pivot = DefaultPivotBottom; return p; }
        private static PyramidMeshGenerator.PyramidParams DefaultPyramidParams()
        { var p = PyramidMeshGenerator.PyramidParams.Default; p.Pivot = DefaultPivotBottom; return p; }

        private CubeMeshGenerator.CubeParams         _cubeP   = DefaultCubeParams();
        private SphereMeshGenerator.SphereParams     _sphereP = DefaultSphereParams();
        private CylinderMeshGenerator.CylinderParams _cylP    = DefaultCylinderParams();
        private CapsuleMeshGenerator.CapsuleParams   _capsP   = DefaultCapsuleParams();
        private PlaneMeshGenerator.PlaneParams       _planeP  = DefaultPlaneParams();
        private PyramidMeshGenerator.PyramidParams   _pyramidP= DefaultPyramidParams();

        private void BuildCubeUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Cube")));
            c.Add(NF(() => _cubeP.MeshName, v => _cubeP.MeshName = v));

            c.Add(TR(T("LinkWHD"), () => _cubeP.LinkWHD, v => { _cubeP.LinkWHD = v; D(); }));

            c.Add(SL(T("Size")));
            if (_cubeP.LinkWHD)
            {
                c.Add(SR(T("WidthX"), CubeMeshGenerator.CubeParams.SizeMin, CubeMeshGenerator.CubeParams.SizeMax, () => _cubeP.WidthTop, v =>
                {
                    _cubeP.WidthTop = _cubeP.WidthBottom = _cubeP.DepthTop = _cubeP.DepthBottom = _cubeP.Height = v; D();
                }));
            }
            else
            {
                c.Add(SR(T("WidthX"), CubeMeshGenerator.CubeParams.SizeMin, CubeMeshGenerator.CubeParams.SizeMax, () => _cubeP.WidthTop, v => { _cubeP.WidthTop  = v; _cubeP.WidthBottom  = v; D(); }));
                c.Add(SR(T("HeightY"), CubeMeshGenerator.CubeParams.SizeMin, CubeMeshGenerator.CubeParams.SizeMax, () => _cubeP.Height,   v => { _cubeP.Height = v; D(); }));
                c.Add(SR(T("DepthZ"), CubeMeshGenerator.CubeParams.SizeMin, CubeMeshGenerator.CubeParams.SizeMax, () => _cubeP.DepthTop, v => { _cubeP.DepthTop  = v; _cubeP.DepthBottom  = v; D(); }));
            }

            c.Add(SL(T("CornerRadius")));
            c.Add(SR(T("CornerRadius"), CubeMeshGenerator.CubeParams.CornerRadiusMin, CubeMeshGenerator.CubeParams.CornerRadiusMax, () => _cubeP.CornerRadius, v => { _cubeP.CornerRadius = v; D(); }));
            if (_cubeP.CornerRadius > 0f)
                c.Add(IR(T("CornerSeg"), CubeMeshGenerator.CubeParams.CornerSegmentsMin, CubeMeshGenerator.CubeParams.CornerSegmentsMax, () => _cubeP.CornerSegments, v => { _cubeP.CornerSegments = v; D(); }));

            c.Add(SL(T("Subdivisions")));
            c.Add(IR(T("SubdivX"), CubeMeshGenerator.CubeParams.SubdivisionsMin, CubeMeshGenerator.CubeParams.SubdivisionsMax, () => _cubeP.Subdivisions.x, v => { _cubeP.Subdivisions = new Vector3Int(v, _cubeP.Subdivisions.y, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivY"), CubeMeshGenerator.CubeParams.SubdivisionsMin, CubeMeshGenerator.CubeParams.SubdivisionsMax, () => _cubeP.Subdivisions.y, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, v, _cubeP.Subdivisions.z); D(); }));
            c.Add(IR(T("SubdivZ"), CubeMeshGenerator.CubeParams.SubdivisionsMin, CubeMeshGenerator.CubeParams.SubdivisionsMax, () => _cubeP.Subdivisions.z, v => { _cubeP.Subdivisions = new Vector3Int(_cubeP.Subdivisions.x, _cubeP.Subdivisions.y, v); D(); }));

            BuildPivotXYZ(c,
                () => _cubeP.Pivot, v => { _cubeP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildSphereUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Sphere")));
            c.Add(NF(() => _sphereP.MeshName, v => _sphereP.MeshName = v));
            c.Add(SR(T("Radius"), SphereMeshGenerator.SphereParams.RadiusMin, SphereMeshGenerator.SphereParams.RadiusMax, () => _sphereP.Radius, v => { _sphereP.Radius = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Lateral"), SphereMeshGenerator.SphereParams.SegmentsMin, SphereMeshGenerator.SphereParams.SegmentsMax, () => _sphereP.LatitudeSegments,  v => { _sphereP.LatitudeSegments  = v; D(); }));
            c.Add(IR(T("Radial"), SphereMeshGenerator.SphereParams.SegmentsMin, SphereMeshGenerator.SphereParams.SegmentsMax, () => _sphereP.LongitudeSegments, v => { _sphereP.LongitudeSegments = v; D(); }));
            c.Add(TR(T("CubeSphere"), () => _sphereP.CubeSphere, v => { _sphereP.CubeSphere = v; D(); }));

            BuildPivotXYZ(c,
                () => _sphereP.Pivot, v => { _sphereP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildCylinderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Cylinder")));
            c.Add(NF(() => _cylP.MeshName, v => _cylP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"), CylinderMeshGenerator.CylinderParams.RadiusMin, CylinderMeshGenerator.CylinderParams.RadiusMax,   () => _cylP.RadiusTop,    v => { _cylP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), CylinderMeshGenerator.CylinderParams.RadiusMin, CylinderMeshGenerator.CylinderParams.RadiusMax,   () => _cylP.RadiusBottom, v => { _cylP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"), CylinderMeshGenerator.CylinderParams.HeightMin, CylinderMeshGenerator.CylinderParams.HeightMax,  () => _cylP.Height,       v => { _cylP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"), CylinderMeshGenerator.CylinderParams.RadialSegmentsMin, CylinderMeshGenerator.CylinderParams.RadialSegmentsMax, () => _cylP.RadialSegments, v => { _cylP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), CylinderMeshGenerator.CylinderParams.HeightSegmentsMin, CylinderMeshGenerator.CylinderParams.HeightSegmentsMax, () => _cylP.HeightSegments, v => { _cylP.HeightSegments = v; D(); }));
            c.Add(TR(T("CapTop"),    () => _cylP.CapTop,    v => { _cylP.CapTop    = v; D(); }));
            c.Add(TR(T("CapBottom"), () => _cylP.CapBottom, v => { _cylP.CapBottom = v; D(); }));

            float maxEdge = _cylP.Height * 0.5f;
            if (_cylP.CapTop    && _cylP.RadiusTop    > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusTop);
            if (_cylP.CapBottom && _cylP.RadiusBottom > 0) maxEdge = Mathf.Min(maxEdge, _cylP.RadiusBottom);
            if (maxEdge > 0f)
            {
                c.Add(SR(T("EdgeRadius"), CylinderMeshGenerator.CylinderParams.EdgeRadiusMin, maxEdge, () => _cylP.EdgeRadius, v => { _cylP.EdgeRadius = v; D(); }));
                if (_cylP.EdgeRadius > 0f)
                    c.Add(IR(T("EdgeSeg"), CylinderMeshGenerator.CylinderParams.EdgeSegmentsMin, CylinderMeshGenerator.CylinderParams.EdgeSegmentsMax, () => _cylP.EdgeSegments, v => { _cylP.EdgeSegments = v; D(); }));
            }

            BuildPivotY(c,
                () => _cylP.Pivot.y, v => { _cylP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _, out _);

        }

        private void BuildCapsuleUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Capsule")));
            c.Add(NF(() => _capsP.MeshName, v => _capsP.MeshName = v));
            c.Add(SL(T("Size")));
            c.Add(SR(T("RadiusTop"), CapsuleMeshGenerator.CapsuleParams.RadiusMin, CapsuleMeshGenerator.CapsuleParams.RadiusMax,  () => _capsP.RadiusTop,    v => { _capsP.RadiusTop    = v; D(); }));
            c.Add(SR(T("RadiusBottom"), CapsuleMeshGenerator.CapsuleParams.RadiusMin, CapsuleMeshGenerator.CapsuleParams.RadiusMax,  () => _capsP.RadiusBottom, v => { _capsP.RadiusBottom = v; D(); }));
            c.Add(SR(T("Height"), CapsuleMeshGenerator.CapsuleParams.HeightMin, CapsuleMeshGenerator.CapsuleParams.HeightMax, () => _capsP.Height,       v => { _capsP.Height       = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Radial"), CapsuleMeshGenerator.CapsuleParams.RadialSegmentsMin, CapsuleMeshGenerator.CapsuleParams.RadialSegmentsMax, () => _capsP.RadialSegments, v => { _capsP.RadialSegments = v; D(); }));
            c.Add(IR(T("Lateral"), CapsuleMeshGenerator.CapsuleParams.HeightSegmentsMin, CapsuleMeshGenerator.CapsuleParams.HeightSegmentsMax, () => _capsP.HeightSegments, v => { _capsP.HeightSegments = v; D(); }));
            c.Add(IR(T("Cap"), CapsuleMeshGenerator.CapsuleParams.CapSegmentsMin, CapsuleMeshGenerator.CapsuleParams.CapSegmentsMax, () => _capsP.CapSegments,    v => { _capsP.CapSegments    = v; D(); }));

            BuildPivotY(c,
                () => _capsP.Pivot.y, v => { _capsP.Pivot = new Vector3(0, v, 0); D(); },
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0),
                out var capsPivotSync, out var capsPivotContent);

            // 上球・下球の重心ピボット
            var sphereRow = new VisualElement();
            sphereRow.style.flexDirection = FlexDirection.Row;
            sphereRow.style.marginBottom  = 4;
            SB(sphereRow, T("UpperSphere"), () =>
            {
                float halfH     = _capsP.Height * 0.5f;
                float cylTop    = halfH - _capsP.RadiusTop;
                float normalized = _capsP.Height > 0f ? cylTop / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D(); capsPivotSync?.Invoke();
            });
            SB(sphereRow, T("LowerSphere"), () =>
            {
                float halfH      = _capsP.Height * 0.5f;
                float cylBottom  = -halfH + _capsP.RadiusBottom;
                float normalized = _capsP.Height > 0f ? cylBottom / _capsP.Height : 0f;
                _capsP.Pivot = new Vector3(0, normalized, 0); D(); capsPivotSync?.Invoke();
            });
            capsPivotContent.Add(sphereRow);

        }

        private void BuildPlaneUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Plane")));
            c.Add(NF(() => _planeP.MeshName, v => _planeP.MeshName = v));
            c.Add(SR(T("Width"), PlaneMeshGenerator.PlaneParams.SizeMin, PlaneMeshGenerator.PlaneParams.SizeMax, () => _planeP.Width,  v => { _planeP.Width  = v; D(); }));
            c.Add(SR(T("Height"), PlaneMeshGenerator.PlaneParams.SizeMin, PlaneMeshGenerator.PlaneParams.SizeMax, () => _planeP.Height, v => { _planeP.Height = v; D(); }));
            c.Add(SL(T("Segments")));
            c.Add(IR(T("Width"), PlaneMeshGenerator.PlaneParams.SegmentsMin, PlaneMeshGenerator.PlaneParams.SegmentsMax, () => _planeP.WidthSegments,  v => { _planeP.WidthSegments  = v; D(); }));
            c.Add(IR(T("Height"), PlaneMeshGenerator.PlaneParams.SegmentsMin, PlaneMeshGenerator.PlaneParams.SegmentsMax, () => _planeP.HeightSegments, v => { _planeP.HeightSegments = v; D(); }));
            var dd = new DropdownField(new List<string>{ T("PlaneXY"), T("PlaneXZ"), T("PlaneYZ") }, (int)_planeP.Orientation);
            dd.label = T("Orientation"); dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(e => { _planeP.Orientation = (PlaneOrientation)dd.index; D(); });
            c.Add(dd);
            c.Add(TR(T("FaceFront"),   () => _planeP.FaceFront,   v => { _planeP.FaceFront   = v; D(); }));
            c.Add(TR(T("DoubleSided"), () => _planeP.DoubleSided, v => { _planeP.DoubleSided = v; D(); }));

            BuildPivotXYZ(c,
                () => _planeP.Pivot, v => { _planeP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }

        private void BuildPyramidUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Pyramid")));
            c.Add(NF(() => _pyramidP.MeshName, v => _pyramidP.MeshName = v));
            c.Add(IR(T("Sides"), PyramidMeshGenerator.PyramidParams.SidesMin, PyramidMeshGenerator.PyramidParams.SidesMax,     () => _pyramidP.Sides,       v => { _pyramidP.Sides       = v; D(); }));
            c.Add(SR(T("BaseRadius"), PyramidMeshGenerator.PyramidParams.BaseRadiusMin, PyramidMeshGenerator.PyramidParams.BaseRadiusMax,  () => _pyramidP.BaseRadius,  v => { _pyramidP.BaseRadius  = v; D(); }));
            c.Add(SR(T("Height"), PyramidMeshGenerator.PyramidParams.HeightMin, PyramidMeshGenerator.PyramidParams.HeightMax, () => _pyramidP.Height,      v => { _pyramidP.Height      = v; D(); }));
            c.Add(SR(T("ApexOffset"), PyramidMeshGenerator.PyramidParams.ApexOffsetMin, PyramidMeshGenerator.PyramidParams.ApexOffsetMax,  () => _pyramidP.ApexOffset,  v => { _pyramidP.ApexOffset  = v; D(); }));
            c.Add(TR(T("CapBottom"),  () => _pyramidP.CapBottom, v => { _pyramidP.CapBottom = v; D(); }));

            BuildPivotXYZ(c,
                () => _pyramidP.Pivot, v => { _pyramidP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

        }
    }
}
