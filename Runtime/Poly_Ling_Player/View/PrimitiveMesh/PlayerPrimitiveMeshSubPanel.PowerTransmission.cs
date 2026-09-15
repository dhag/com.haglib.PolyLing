// PlayerPrimitiveMeshSubPanel.PowerTransmission.cs
// 図形生成サブパネル：台形ねじ・平行歯面スプライン（機構部品B）。
// どちらも「軸」と「かみ合うナット」を 1 つの図形で切り替える形なので、同じファイルに置く。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private TrapezoidalThreadMeshGenerator.Params _trapP = TrapezoidalThreadMeshGenerator.Params.Default;
        private SplineMeshGenerator.Params            _splineP = SplineMeshGenerator.Params.Default;

        // ================================================================
        // 台形ねじ
        // ================================================================

        private void BuildTrapezoidalThreadUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("TrapezoidalThread")));
            c.Add(NF(() => _trapP.MeshName, v => _trapP.MeshName = v));
            c.Add(GearHint(T("TrapHint")));

            c.Add(DD(T("TrapPart"),
                new List<string> { T("TrapScrew"), T("TrapNut") },
                () => (int)_trapP.Part,
                i => { _trapP.Part = (TrapezoidalThreadPart)i; RebuildSettings(); D(); }));

            float lo = TrapezoidalThreadMeshGenerator.Params.SizeMin;
            float hi = TrapezoidalThreadMeshGenerator.Params.SizeMax;

            c.Add(SR(T("TrapMajorDiameter"), lo, hi,
                () => _trapP.MajorDiameter, v => { _trapP.MajorDiameter = v; DM(); }));
            c.Add(SR(T("TrapMinorDiameter"), lo, hi,
                () => _trapP.MinorDiameter, v => { _trapP.MinorDiameter = v; DM(); }));
            c.Add(SR(T("TrapPitch"), lo, hi,
                () => _trapP.Pitch, v => { _trapP.Pitch = v; DM(); }));
            c.Add(IR(T("TrapStarts"),
                TrapezoidalThreadMeshGenerator.Params.StartsMin,
                TrapezoidalThreadMeshGenerator.Params.StartsMax,
                () => _trapP.Starts, v => { _trapP.Starts = v; DM(); }));
            c.Add(SR(T("TrapLength"), lo, hi,
                () => _trapP.Length, v => { _trapP.Length = v; DM(); }));
            c.Add(SR(T("TrapCrestFlat"),
                TrapezoidalThreadMeshGenerator.Params.FlatMin, TrapezoidalThreadMeshGenerator.Params.FlatMax,
                () => _trapP.CrestFlatRatio, v => { _trapP.CrestFlatRatio = v; DM(); }));
            c.Add(SR(T("TrapRootFlat"),
                TrapezoidalThreadMeshGenerator.Params.FlatMin, TrapezoidalThreadMeshGenerator.Params.FlatMax,
                () => _trapP.RootFlatRatio, v => { _trapP.RootFlatRatio = v; DM(); }));
            c.Add(TR(T("TrapRightHand"),
                () => _trapP.RightHand, v => { _trapP.RightHand = v; D(); }));

            if (_trapP.Part == TrapezoidalThreadPart.Nut)
            {
                c.Add(SL(T("TrapNutSection")));
                c.Add(DD(T("TrapNutOuterType"),
                    new List<string> { T("ThreadOuterHex"), T("ThreadOuterRound"), T("ThreadOuterSquare") },
                    () => (int)_trapP.NutOuterType,
                    i => { _trapP.NutOuterType = (ThreadNutOuterType)i; DM(); }));
                c.Add(SR(T("TrapNutOuterDiameter"), lo, hi,
                    () => _trapP.NutOuterDiameter, v => { _trapP.NutOuterDiameter = v; DM(); }));
                c.Add(SR(T("TrapClearance"), 0f, hi,
                    () => _trapP.Clearance, v => { _trapP.Clearance = v; DM(); }));
            }

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("TrapRadialSegments"),
                TrapezoidalThreadMeshGenerator.Params.SegmentsMin,
                TrapezoidalThreadMeshGenerator.Params.SegmentsMax,
                () => _trapP.RadialSegments, v => { _trapP.RadialSegments = v; D(); }));
            c.Add(IR(T("TrapSamplesPerPitch"),
                TrapezoidalThreadMeshGenerator.Params.SamplesMin,
                TrapezoidalThreadMeshGenerator.Params.SamplesMax,
                () => _trapP.SamplesPerPitch, v => { _trapP.SamplesPerPitch = v; D(); }));

            BuildMechInfo(c, "TrapDerived");
            BuildMechFooter(c,
                () => _trapP.Orientation, v => _trapP.Orientation = v,
                () => _trapP.FlipFaces,   v => _trapP.FlipFaces   = v,
                () => _trapP.Pivot,       v => _trapP.Pivot       = v);
        }

        private void RefreshTrapezoidalThreadInfo()
        {
            var i = TrapezoidalThreadMeshGenerator.GetInfo(_trapP);
            if (!i.Valid)
            {
                SetMechInfo(T("TrapInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("TrapDerivedInfo", F(i.Lead), i.Turns.ToString("F2"),
                          i.IncludedAngleDeg.ToString("F2")), true);
            SetMechWarn(null);
        }

        // ================================================================
        // スプライン
        // ================================================================

        private void BuildSplineUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Spline")));
            c.Add(NF(() => _splineP.MeshName, v => _splineP.MeshName = v));
            c.Add(GearHint(T("SplineHint")));

            c.Add(DD(T("SplinePart"),
                new List<string> { T("SplineShaft"), T("SplineNut") },
                () => (int)_splineP.Part,
                i => { _splineP.Part = (SplinePart)i; RebuildSettings(); D(); }));

            float lo = SplineMeshGenerator.Params.SizeMin;
            float hi = SplineMeshGenerator.Params.SizeMax;

            c.Add(IR(T("SplineTeeth"),
                SplineMeshGenerator.Params.TeethMin, SplineMeshGenerator.Params.TeethMax,
                () => _splineP.Teeth, v => { _splineP.Teeth = v; DM(); }));
            c.Add(SR(T("SplineMajorDiameter"), lo, hi,
                () => _splineP.MajorDiameter, v => { _splineP.MajorDiameter = v; DM(); }));
            c.Add(SR(T("SplineMinorDiameter"), lo, hi,
                () => _splineP.MinorDiameter, v => { _splineP.MinorDiameter = v; DM(); }));
            c.Add(SR(T("SplineLength"), lo, hi,
                () => _splineP.Length, v => { _splineP.Length = v; DM(); }));
            c.Add(SR(T("SplineToothFlat"),
                SplineMeshGenerator.Params.FlatMin, SplineMeshGenerator.Params.FlatMax,
                () => _splineP.ToothFlatRatio, v => { _splineP.ToothFlatRatio = v; DM(); }));
            c.Add(SR(T("SplineRootFlat"),
                SplineMeshGenerator.Params.FlatMin, SplineMeshGenerator.Params.FlatMax,
                () => _splineP.RootFlatRatio, v => { _splineP.RootFlatRatio = v; DM(); }));
            c.Add(SR(T("SplineChamfer"), 0f, hi,
                () => _splineP.Chamfer, v => { _splineP.Chamfer = v; DM(); }));

            if (_splineP.Part == SplinePart.Nut)
            {
                c.Add(SL(T("SplineNutSection")));
                c.Add(DD(T("SplineNutOuterType"),
                    new List<string> { T("ThreadOuterRound"), T("ThreadOuterHex"), T("ThreadOuterSquare") },
                    () => (int)_splineP.NutOuterType,
                    i => { _splineP.NutOuterType = (SplineNutOuterType)i; DM(); }));
                c.Add(SR(T("SplineNutOuterDiameter"), lo, hi,
                    () => _splineP.NutOuterDiameter, v => { _splineP.NutOuterDiameter = v; DM(); }));
                c.Add(SR(T("SplineClearance"), 0f, hi,
                    () => _splineP.Clearance, v => { _splineP.Clearance = v; DM(); }));
            }

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("SplineSegmentsPerTooth"),
                SplineMeshGenerator.Params.SegmentsPerToothMin,
                SplineMeshGenerator.Params.SegmentsPerToothMax,
                () => _splineP.SegmentsPerTooth, v => { _splineP.SegmentsPerTooth = v; D(); }));

            BuildMechInfo(c, "SplineDerived");
            BuildMechFooter(c,
                () => _splineP.Orientation, v => _splineP.Orientation = v,
                () => _splineP.FlipFaces,   v => _splineP.FlipFaces   = v,
                () => _splineP.Pivot,       v => _splineP.Pivot       = v);
        }

        private void RefreshSplineInfo()
        {
            var i = SplineMeshGenerator.GetInfo(_splineP);
            if (!i.Valid)
            {
                SetMechInfo(T("SplineInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("SplineDerivedInfo", i.AngularPitchDeg.ToString("F2"), F(i.ToothHeight)), true);

            var w = new List<string>();
            if (i.ChamferClamped) w.Add(T("SplineWarnChamfer"));
            SetMechWarn(w);
        }
    }
}
