// PlayerPrimitiveMeshSubPanel.RodEnd.cs
// 図形生成サブパネル：ロッドエンド（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private RodEndMeshGenerator.Params _rodEndP = RodEndMeshGenerator.Params.Default;

        private void BuildRodEndUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("RodEnd")));
            c.Add(NF(() => _rodEndP.MeshName, v => _rodEndP.MeshName = v));
            c.Add(GearHint(T("RodEndHint")));

            c.Add(DD(T("RodEndType"),
                new List<string> { T("RodEndMale"), T("RodEndFemale"), T("RodEndBallStud") },
                () => (int)_rodEndP.Type,
                i => { _rodEndP.Type = (RodEndType)i; RebuildSettings(); D(); }));

            float lo = RodEndMeshGenerator.Params.SizeMin;
            float hi = RodEndMeshGenerator.Params.SizeMax;

            c.Add(SR(T("RodEndBallDiameter"), lo, hi,
                () => _rodEndP.BallDiameter, v => { _rodEndP.BallDiameter = v; DM(); }));
            if (_rodEndP.Type != RodEndType.BallStudJoint)
                c.Add(SR(T("RodEndBallBore"), lo, hi,
                    () => _rodEndP.BallBore, v => { _rodEndP.BallBore = v; DM(); }));
            c.Add(SR(T("RodEndHousingDiameter"), lo, hi,
                () => _rodEndP.HousingDiameter, v => { _rodEndP.HousingDiameter = v; DM(); }));
            c.Add(SR(T("RodEndHousingWidth"), lo, hi,
                () => _rodEndP.HousingWidth, v => { _rodEndP.HousingWidth = v; DM(); }));
            c.Add(SR(T("RodEndNeckWidth"), lo, hi,
                () => _rodEndP.NeckWidth, v => { _rodEndP.NeckWidth = v; DM(); }));
            c.Add(SR(T("RodEndShankDiameter"), lo, hi,
                () => _rodEndP.ShankDiameter, v => { _rodEndP.ShankDiameter = v; DM(); }));
            c.Add(SR(T("RodEndShankLength"), lo, hi,
                () => _rodEndP.ShankLength, v => { _rodEndP.ShankLength = v; DM(); }));
            c.Add(SR(T("RodEndThreadPitch"), lo, hi,
                () => _rodEndP.ThreadPitch, v => { _rodEndP.ThreadPitch = v; DM(); }));

            if (_rodEndP.Type == RodEndType.BallStudJoint)
                c.Add(SR(T("RodEndStudAngle"),
                    RodEndMeshGenerator.Params.StudAngleMin, RodEndMeshGenerator.Params.StudAngleMax,
                    () => _rodEndP.StudAngleDeg, v => { _rodEndP.StudAngleDeg = v; D(); }));

            c.Add(TR(T("RodEndShowThread"),
                () => _rodEndP.ShowThread, v => { _rodEndP.ShowThread = v; D(); }));
            c.Add(IR(T("RodEndSegments"),
                RodEndMeshGenerator.Params.SegmentsMin, RodEndMeshGenerator.Params.SegmentsMax,
                () => _rodEndP.Segments, v => { _rodEndP.Segments = v; D(); }));

            BuildMechInfo(c, "RodEndDerived");
            BuildMechFooter(c,
                () => _rodEndP.Orientation, v => _rodEndP.Orientation = v,
                () => _rodEndP.FlipFaces,   v => _rodEndP.FlipFaces   = v,
                () => _rodEndP.Pivot,       v => _rodEndP.Pivot       = v);
        }

        private void RefreshRodEndInfo()
        {
            var i = RodEndMeshGenerator.GetInfo(_rodEndP);
            if (!i.Valid)
            {
                SetMechInfo(T("RodEndInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("RodEndDerivedInfo", F(i.MaxTiltDeg), i.ThreadTurns), true);
            SetMechWarn(null);
        }
    }
}
