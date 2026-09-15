// PlayerPrimitiveMeshSubPanel.BearingUnit.cs
// 図形生成サブパネル：軸受ユニット（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private BearingUnitMeshGenerator.Params _bearingUnitP = BearingUnitMeshGenerator.Params.Default;

        private void BuildBearingUnitUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("BearingUnit")));
            c.Add(NF(() => _bearingUnitP.MeshName, v => _bearingUnitP.MeshName = v));
            c.Add(GearHint(T("BearingHint")));

            c.Add(DD(T("BearingUnitType"),
                new List<string> { T("BearingPillow"), T("BearingFlange2"), T("BearingFlange4") },
                () => (int)_bearingUnitP.Type,
                i => { _bearingUnitP.Type = (BearingUnitType)i; DM(); }));

            float lo = BearingUnitMeshGenerator.Params.SizeMin;
            float hi = BearingUnitMeshGenerator.Params.SizeMax;

            c.Add(SR(T("BearingBore"), lo, hi,
                () => _bearingUnitP.BoreDiameter, v => { _bearingUnitP.BoreDiameter = v; DM(); }));
            c.Add(SR(T("BearingOuter"), lo, hi,
                () => _bearingUnitP.BearingOuterDiameter, v => { _bearingUnitP.BearingOuterDiameter = v; DM(); }));
            c.Add(SR(T("BearingWidth"), lo, hi,
                () => _bearingUnitP.BearingWidth, v => { _bearingUnitP.BearingWidth = v; DM(); }));
            c.Add(SR(T("BearingHousingOuter"), lo, hi,
                () => _bearingUnitP.HousingOuterDiameter, v => { _bearingUnitP.HousingOuterDiameter = v; DM(); }));
            c.Add(SR(T("BearingHousingDepth"), lo, hi,
                () => _bearingUnitP.HousingDepth, v => { _bearingUnitP.HousingDepth = v; DM(); }));
            c.Add(SR(T("BearingMountSpacing"), lo, hi,
                () => _bearingUnitP.MountSpacing, v => { _bearingUnitP.MountSpacing = v; DM(); }));
            c.Add(SR(T("BearingMountHole"), lo, hi,
                () => _bearingUnitP.MountHoleDiameter, v => { _bearingUnitP.MountHoleDiameter = v; DM(); }));
            c.Add(SR(T("BearingMountBoss"), lo, hi,
                () => _bearingUnitP.MountBossDiameter, v => { _bearingUnitP.MountBossDiameter = v; DM(); }));

            c.Add(TR(T("BearingShowBalls"),
                () => _bearingUnitP.ShowBalls, v => { _bearingUnitP.ShowBalls = v; RebuildSettings(); D(); }));
            if (_bearingUnitP.ShowBalls)
            {
                c.Add(IR(T("BearingBallCount"),
                    BearingUnitMeshGenerator.Params.BallCountMin, BearingUnitMeshGenerator.Params.BallCountMax,
                    () => _bearingUnitP.BallCount, v => { _bearingUnitP.BallCount = v; D(); }));
                c.Add(SR(T("BearingBallDiameter"), lo, hi,
                    () => _bearingUnitP.BallDiameter, v => { _bearingUnitP.BallDiameter = v; DM(); }));
            }

            c.Add(IR(T("BearingSegments"),
                BearingUnitMeshGenerator.Params.SegmentsMin, BearingUnitMeshGenerator.Params.SegmentsMax,
                () => _bearingUnitP.Segments, v => { _bearingUnitP.Segments = v; D(); }));

            BuildMechInfo(c, "BearingDerived");
            BuildMechFooter(c,
                () => _bearingUnitP.Orientation, v => _bearingUnitP.Orientation = v,
                () => _bearingUnitP.FlipFaces,   v => _bearingUnitP.FlipFaces   = v,
                () => _bearingUnitP.Pivot,       v => _bearingUnitP.Pivot       = v);
        }

        private void RefreshBearingUnitInfo()
        {
            var i = BearingUnitMeshGenerator.GetInfo(_bearingUnitP);
            if (!i.Valid)
            {
                SetMechInfo(T("BearingInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("BearingDerivedInfo", F(i.BallPitchDiameter)), true);

            var w = new List<string>();
            if (i.BallTooLarge) w.Add(T("BearingWarnBall"));
            SetMechWarn(w);
        }
    }
}
