// PlayerPrimitiveMeshSubPanel.Emblem.cs
// 図形生成サブパネル：紋章図形（五芒星・六芒星・クロス・八芒星。高度な図形）。
// 生成そのものは EmblemMeshGenerator（PrimitiveMeshFactory 経由）。ここは諸元 UI だけを持つ。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private EmblemMeshGenerator.EmblemParams _emblemP = EmblemMeshGenerator.EmblemParams.Default;

        private SolidifyUI _emblemSolidUI;

        /// <summary>図形のドロップダウンの並び。EmblemKind の列挙順と一致させる。</summary>
        private static readonly string[] EmblemKindKeys =
            { "EmblemPentagram", "EmblemHexagram", "EmblemCross", "EmblemOctagram" };

        /// <summary>描き方のドロップダウンの並び。EmblemStyle の列挙順と一致させる。</summary>
        private static readonly string[] EmblemStyleKeys =
            { "EmblemStyleFill", "EmblemStyleLine" };

        private void BuildEmblemUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Emblem")));
            c.Add(NF(() => _emblemP.MeshName, v => _emblemP.MeshName = v));
            c.Add(GearHint(T("EmblemHint")));

            var kinds = new List<string>(EmblemKindKeys.Length);
            foreach (var k in EmblemKindKeys) kinds.Add(T(k));
            c.Add(DD(T("EmblemKind"), kinds,
                () => (int)_emblemP.Kind,
                i => { _emblemP.Kind = (EmblemKind)i; RebuildSettings(); D(); }));

            var styles = new List<string>(EmblemStyleKeys.Length);
            foreach (var k in EmblemStyleKeys) styles.Add(T(k));
            c.Add(DD(T("EmblemStyle"), styles,
                () => (int)_emblemP.Style,
                i => { _emblemP.Style = (EmblemStyle)i; RebuildSettings(); D(); }));

            c.Add(SR(T("EmblemSize"), EmblemMeshGenerator.EmblemParams.SizeMin, EmblemMeshGenerator.EmblemParams.SizeMax,
                () => _emblemP.Size, v => { _emblemP.Size = v; D(); }));
            c.Add(SR(T("EmblemRotation"), EmblemMeshGenerator.EmblemParams.RotationMin,
                EmblemMeshGenerator.EmblemParams.RotationMax,
                () => _emblemP.Rotation, v => { _emblemP.Rotation = v; D(); }));

            if (_emblemP.Style == EmblemStyle.Line)
                c.Add(SR(T("EmblemLineWidth"), EmblemMeshGenerator.EmblemParams.LineWidthMin,
                    EmblemMeshGenerator.EmblemParams.LineWidthMax,
                    () => _emblemP.LineWidth, v => { _emblemP.LineWidth = v; D(); }));

            if (_emblemP.Kind == EmblemKind.Cross)
                c.Add(SR(T("EmblemCrossArmWidth"), EmblemMeshGenerator.EmblemParams.CrossArmWidthMin,
                    EmblemMeshGenerator.EmblemParams.CrossArmWidthMax,
                    () => _emblemP.CrossArmWidth, v => { _emblemP.CrossArmWidth = v; D(); }));

            c.Add(SR(T("EmblemCornerRadius"), EmblemMeshGenerator.EmblemParams.CornerRadiusMin,
                EmblemMeshGenerator.EmblemParams.CornerRadiusMax,
                () => _emblemP.CornerRadius, v => { _emblemP.CornerRadius = v; D(); }));
            c.Add(IR(T("Segments"), EmblemMeshGenerator.EmblemParams.SegmentsMin,
                EmblemMeshGenerator.EmblemParams.SegmentsMax,
                () => _emblemP.Segments, v => { _emblemP.Segments = v; D(); }));

            // ── 周囲の円 ──
            c.Add(SL(T("ZodiacRing")));
            c.Add(TR(T("ZodiacRing"),
                () => _emblemP.Ring, v => { _emblemP.Ring = v; RebuildSettings(); D(); }));
            if (_emblemP.Ring)
            {
                c.Add(SR(T("ZodiacRingRadius"), EmblemMeshGenerator.EmblemParams.RingRadiusMin,
                    EmblemMeshGenerator.EmblemParams.RingRadiusMax,
                    () => _emblemP.RingRadius, v => { _emblemP.RingRadius = v; D(); }));
                c.Add(SR(T("ZodiacRingWidth"), EmblemMeshGenerator.EmblemParams.RingWidthMin,
                    EmblemMeshGenerator.EmblemParams.RingWidthMax,
                    () => _emblemP.RingWidth, v => { _emblemP.RingWidth = v; D(); }));
            }

            // ── 厚み付け（立体） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), EmblemMeshGenerator.EmblemParams.ThicknessMin,
                EmblemMeshGenerator.EmblemParams.ThicknessMax,
                () => _emblemP.Thickness, v => { _emblemP.Thickness = v; D(); RefreshEmblemSolidVis(); }));

            var solid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), EmblemMeshGenerator.EmblemParams.EdgeSegmentsMin,
                               EmblemMeshGenerator.EmblemParams.EdgeSegmentsMax, () => _emblemP.SegmentsFront,
                               v => { _emblemP.SegmentsFront = v; D(); RefreshEmblemSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), EmblemMeshGenerator.EmblemParams.EdgeSizeMin,
                               EmblemMeshGenerator.EmblemParams.EdgeSizeMax, () => _emblemP.EdgeSizeFront,
                               v => { _emblemP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), EmblemMeshGenerator.EmblemParams.EdgeSegmentsMin,
                               EmblemMeshGenerator.EmblemParams.EdgeSegmentsMax, () => _emblemP.SegmentsBack,
                               v => { _emblemP.SegmentsBack = v; D(); RefreshEmblemSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), EmblemMeshGenerator.EmblemParams.EdgeSizeMin,
                               EmblemMeshGenerator.EmblemParams.EdgeSizeMax, () => _emblemP.EdgeSizeBack,
                               v => { _emblemP.EdgeSizeBack = v; D(); }),
                Inward    = TR(T("EdgeInward"), () => _emblemP.EdgeInward,
                               v => { _emblemP.EdgeInward = v; D(); }),
            };
            _emblemSolidUI = solid;
            c.Add(solid.EdgeLabel); c.Add(solid.FrontSeg); c.Add(solid.FrontSize);
            c.Add(solid.BackSeg);   c.Add(solid.BackSize); c.Add(solid.Inward);
            c.Add(GearHint(T("EmblemEdgeHint")));
            RefreshEmblemSolidVis();

            // ── 配置面 / 面の向き / ピボット ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(OrientationDD(
                () => _emblemP.Orientation, v => { _emblemP.Orientation = v; D(); }));
            c.Add(TR(T("FlipFaces"),
                () => _emblemP.FlipFaces, v => { _emblemP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _emblemP.Pivot, v => { _emblemP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        private void RefreshEmblemSolidVis()
            => UpdateSolidifyVis(_emblemSolidUI, _emblemP.Thickness, _emblemP.SegmentsFront, _emblemP.SegmentsBack);
    }
}
