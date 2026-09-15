// PlayerPrimitiveMeshSubPanel.MotorBracket.cs
// 図形生成サブパネル：モータ取付ブラケット（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private MotorBracketMeshGenerator.Params _motorBracketP = MotorBracketMeshGenerator.Params.Default;

        private void BuildMotorBracketUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("MotorBracket")));
            c.Add(NF(() => _motorBracketP.MeshName, v => _motorBracketP.MeshName = v));
            c.Add(GearHint(T("MotorBracketHint")));

            c.Add(DD(T("MotorBracketType"),
                new List<string> { T("MotorBracketClamp"), T("MotorBracketFace"),
                                   T("MotorBracketL"), T("MotorBracketU") },
                () => (int)_motorBracketP.Type,
                i => { _motorBracketP.Type = (MotorBracketType)i; DM(); }));

            float lo = MotorBracketMeshGenerator.Params.SizeMin;
            float hi = MotorBracketMeshGenerator.Params.SizeMax;

            c.Add(SR(T("MotorDiameter"), lo, hi,
                () => _motorBracketP.MotorDiameter, v => { _motorBracketP.MotorDiameter = v; DM(); }));
            c.Add(SR(T("MotorWidth"), lo, hi,
                () => _motorBracketP.MotorWidth, v => { _motorBracketP.MotorWidth = v; DM(); }));
            c.Add(SR(T("MotorHeight"), lo, hi,
                () => _motorBracketP.MotorHeight, v => { _motorBracketP.MotorHeight = v; DM(); }));
            c.Add(SR(T("MotorBracketLength"), lo, hi,
                () => _motorBracketP.Length, v => { _motorBracketP.Length = v; DM(); }));
            c.Add(SR(T("MotorBracketThickness"), lo, hi,
                () => _motorBracketP.Thickness, v => { _motorBracketP.Thickness = v; DM(); }));
            c.Add(SR(T("MotorShaftClearance"), lo, hi,
                () => _motorBracketP.ShaftClearance, v => { _motorBracketP.ShaftClearance = v; DM(); }));
            c.Add(SR(T("MotorHolePitchX"), lo, hi,
                () => _motorBracketP.HolePitchX, v => { _motorBracketP.HolePitchX = v; DM(); }));
            c.Add(SR(T("MotorHolePitchY"), lo, hi,
                () => _motorBracketP.HolePitchY, v => { _motorBracketP.HolePitchY = v; DM(); }));
            c.Add(SR(T("MotorMountHole"), lo, hi,
                () => _motorBracketP.MountHoleDiameter, v => { _motorBracketP.MountHoleDiameter = v; DM(); }));
            c.Add(SR(T("MotorBaseHole"), lo, hi,
                () => _motorBracketP.BaseHoleDiameter, v => { _motorBracketP.BaseHoleDiameter = v; DM(); }));

            c.Add(IR(T("MotorBracketSegments"),
                MotorBracketMeshGenerator.Params.SegmentsMin, MotorBracketMeshGenerator.Params.SegmentsMax,
                () => _motorBracketP.Segments, v => { _motorBracketP.Segments = v; D(); }));

            BuildMechInfo(c, "MotorBracketDerived");
            BuildMechFooter(c,
                () => _motorBracketP.Orientation, v => _motorBracketP.Orientation = v,
                () => _motorBracketP.FlipFaces,   v => _motorBracketP.FlipFaces   = v,
                () => _motorBracketP.Pivot,       v => _motorBracketP.Pivot       = v);
        }

        private void RefreshMotorBracketInfo()
        {
            var i = MotorBracketMeshGenerator.GetInfo(_motorBracketP);
            if (!i.Valid)
            {
                SetMechInfo(T("MotorBracketInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("MotorBracketDerivedInfo", i.MotorHoleCount, i.BaseHoleCount), true);
            SetMechWarn(null);
        }
    }
}
