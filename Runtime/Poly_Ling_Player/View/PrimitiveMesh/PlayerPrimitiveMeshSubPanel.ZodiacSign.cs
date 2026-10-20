// PlayerPrimitiveMeshSubPanel.ZodiacSign.cs
// 図形生成サブパネル：星座マーク（高度な図形）。
// 生成そのものは ZodiacSignMeshGenerator（PrimitiveMeshFactory 経由）。ここは諸元 UI だけを持つ。
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
        private ZodiacSignMeshGenerator.ZodiacSignParams _zodiacP = ZodiacSignMeshGenerator.ZodiacSignParams.Default;

        private SolidifyUI _zodiacSolidUI;

        /// <summary>ドロップダウンの並び。ZodiacSign の列挙順と一致させる。</summary>
        private static readonly string[] ZodiacSignKeys =
        {
            "ZodiacAries", "ZodiacTaurus", "ZodiacGemini", "ZodiacCancer", "ZodiacLeo", "ZodiacVirgo",
            "ZodiacLibra", "ZodiacScorpio", "ZodiacSagittarius", "ZodiacCapricorn", "ZodiacAquarius", "ZodiacPisces",
        };

        private void BuildZodiacSignUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("ZodiacSign")));
            c.Add(NF(() => _zodiacP.MeshName, v => _zodiacP.MeshName = v));
            c.Add(GearHint(T("ZodiacSignHint")));

            var names = new List<string>(ZodiacSignKeys.Length);
            foreach (var k in ZodiacSignKeys) names.Add(T(k));
            c.Add(DD(T("ZodiacSignKind"), names,
                () => (int)_zodiacP.Sign,
                i => { _zodiacP.Sign = (ZodiacSign)i; D(); }));

            c.Add(SR(T("Size"), ZodiacSignMeshGenerator.ZodiacSignParams.SizeMin, ZodiacSignMeshGenerator.ZodiacSignParams.SizeMax,
                () => _zodiacP.Size, v => { _zodiacP.Size = v; D(); }));
            c.Add(SR(T("ZodiacStrokeWidth"), ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMin,
                ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMax,
                () => _zodiacP.StrokeWidth, v => { _zodiacP.StrokeWidth = v; D(); }));
            c.Add(TR(T("ZodiacRoundCaps"),
                () => _zodiacP.RoundCaps, v => { _zodiacP.RoundCaps = v; D(); }));
            c.Add(IR(T("Segments"), ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMin,
                ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMax,
                () => _zodiacP.Segments, v => { _zodiacP.Segments = v; D(); }));

            // ── 周囲の円 ──
            c.Add(SL(T("ZodiacRing")));
            c.Add(TR(T("ZodiacRing"),
                () => _zodiacP.Ring, v => { _zodiacP.Ring = v; RebuildSettings(); D(); }));
            if (_zodiacP.Ring)
            {
                c.Add(SR(T("ZodiacRingRadius"), ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMin,
                    ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMax,
                    () => _zodiacP.RingRadius, v => { _zodiacP.RingRadius = v; D(); }));
                c.Add(SR(T("ZodiacRingWidth"), ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMin,
                    ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMax,
                    () => _zodiacP.RingWidth, v => { _zodiacP.RingWidth = v; D(); }));
            }

            // ── 厚み付け（立体） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), ZodiacSignMeshGenerator.ZodiacSignParams.ThicknessMin,
                ZodiacSignMeshGenerator.ZodiacSignParams.ThicknessMax,
                () => _zodiacP.Thickness, v => { _zodiacP.Thickness = v; D(); RefreshZodiacSolidVis(); }));

            var solid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSegmentsMin,
                               ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSegmentsMax, () => _zodiacP.SegmentsFront,
                               v => { _zodiacP.SegmentsFront = v; D(); RefreshZodiacSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSizeMin,
                               ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSizeMax, () => _zodiacP.EdgeSizeFront,
                               v => { _zodiacP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSegmentsMin,
                               ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSegmentsMax, () => _zodiacP.SegmentsBack,
                               v => { _zodiacP.SegmentsBack = v; D(); RefreshZodiacSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSizeMin,
                               ZodiacSignMeshGenerator.ZodiacSignParams.EdgeSizeMax, () => _zodiacP.EdgeSizeBack,
                               v => { _zodiacP.EdgeSizeBack = v; D(); }),
                Inward    = TR(T("EdgeInward"), () => _zodiacP.EdgeInward,
                               v => { _zodiacP.EdgeInward = v; D(); }),
            };
            _zodiacSolidUI = solid;
            c.Add(solid.EdgeLabel); c.Add(solid.FrontSeg); c.Add(solid.FrontSize);
            c.Add(solid.BackSeg);   c.Add(solid.BackSize); c.Add(solid.Inward);
            c.Add(GearHint(T("ZodiacEdgeHint")));
            RefreshZodiacSolidVis();

            // ── 面の向き / ピボット ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FlipFaces"),
                () => _zodiacP.FlipFaces, v => { _zodiacP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _zodiacP.Pivot, v => { _zodiacP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        private void RefreshZodiacSolidVis()
            => UpdateSolidifyVis(_zodiacSolidUI, _zodiacP.Thickness, _zodiacP.SegmentsFront, _zodiacP.SegmentsBack);
    }
}
