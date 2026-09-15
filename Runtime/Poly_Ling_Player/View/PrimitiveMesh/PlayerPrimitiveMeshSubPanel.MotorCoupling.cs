// PlayerPrimitiveMeshSubPanel.MotorCoupling.cs
// 図形生成サブパネル：軸継手（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private MotorCouplingMeshGenerator.Params _motorCouplingP = MotorCouplingMeshGenerator.Params.Default;

        private void BuildMotorCouplingUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("MotorCoupling")));
            c.Add(NF(() => _motorCouplingP.MeshName, v => _motorCouplingP.MeshName = v));
            c.Add(GearHint(T("MotorCouplingHint")));

            c.Add(DD(T("MotorCouplingType"),
                new List<string> { T("CouplingRigid"), T("CouplingClamp"), T("CouplingJaw"),
                                   T("CouplingOldham"), T("CouplingBellows") },
                () => (int)_motorCouplingP.Type,
                i => { _motorCouplingP.Type = (MotorCouplingType)i; RebuildSettings(); D(); }));

            float lo = MotorCouplingMeshGenerator.Params.SizeMin;
            float hi = MotorCouplingMeshGenerator.Params.SizeMax;

            c.Add(SR(T("CouplingInputBore"), lo, hi,
                () => _motorCouplingP.InputBore, v => { _motorCouplingP.InputBore = v; DM(); }));
            c.Add(SR(T("CouplingOutputBore"), lo, hi,
                () => _motorCouplingP.OutputBore, v => { _motorCouplingP.OutputBore = v; DM(); }));
            c.Add(SR(T("CouplingOuterDiameter"), lo, hi,
                () => _motorCouplingP.OuterDiameter, v => { _motorCouplingP.OuterDiameter = v; DM(); }));
            c.Add(SR(T("CouplingLength"), lo, hi,
                () => _motorCouplingP.Length, v => { _motorCouplingP.Length = v; DM(); }));
            c.Add(SR(T("CouplingCenterGap"), 0f, hi,
                () => _motorCouplingP.CenterGap, v => { _motorCouplingP.CenterGap = v; DM(); }));

            if (_motorCouplingP.Type == MotorCouplingType.Clamp)
                c.Add(SR(T("CouplingClampHole"), lo, hi,
                    () => _motorCouplingP.ClampHoleDiameter,
                    v => { _motorCouplingP.ClampHoleDiameter = v; DM(); }));

            if (_motorCouplingP.Type == MotorCouplingType.Jaw)
                c.Add(IR(T("CouplingJawCount"),
                    MotorCouplingMeshGenerator.Params.JawCountMin, MotorCouplingMeshGenerator.Params.JawCountMax,
                    () => _motorCouplingP.JawCount, v => { _motorCouplingP.JawCount = v; D(); }));

            // 爪の厚みはジョーとオルダムで共用する（同じ行を 2 度足さない）。
            if (_motorCouplingP.Type == MotorCouplingType.Jaw ||
                _motorCouplingP.Type == MotorCouplingType.Oldham)
                c.Add(SR(T("CouplingJawDepth"), lo, hi,
                    () => _motorCouplingP.JawDepth, v => { _motorCouplingP.JawDepth = v; DM(); }));

            if (_motorCouplingP.Type == MotorCouplingType.Bellows)
            {
                c.Add(IR(T("CouplingBellowsCount"),
                    MotorCouplingMeshGenerator.Params.BellowsCountMin,
                    MotorCouplingMeshGenerator.Params.BellowsCountMax,
                    () => _motorCouplingP.BellowsCount, v => { _motorCouplingP.BellowsCount = v; D(); }));
                c.Add(SR(T("CouplingBellowsDepth"), lo, hi,
                    () => _motorCouplingP.BellowsDepth, v => { _motorCouplingP.BellowsDepth = v; DM(); }));
            }

            c.Add(IR(T("CouplingSegments"),
                MotorCouplingMeshGenerator.Params.SegmentsMin, MotorCouplingMeshGenerator.Params.SegmentsMax,
                () => _motorCouplingP.Segments, v => { _motorCouplingP.Segments = v; D(); }));

            BuildMechInfo(c, "CouplingDerived");
            BuildMechFooter(c,
                () => _motorCouplingP.Orientation, v => _motorCouplingP.Orientation = v,
                () => _motorCouplingP.FlipFaces,   v => _motorCouplingP.FlipFaces   = v,
                () => _motorCouplingP.Pivot,       v => _motorCouplingP.Pivot       = v);
        }

        private void RefreshMotorCouplingInfo()
        {
            var i = MotorCouplingMeshGenerator.GetInfo(_motorCouplingP);
            if (!i.Valid)
            {
                SetMechInfo(T("CouplingInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("CouplingDerivedInfo", F(i.InputWall), F(i.OutputWall), i.FlexibleElements), true);
            SetMechWarn(null);
        }
    }
}
