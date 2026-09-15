// PlayerPrimitiveMeshSubPanel.UniversalJoint.cs
// 図形生成サブパネル：自在継手（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private UniversalJointMeshGenerator.Params _uJointP = UniversalJointMeshGenerator.Params.Default;

        private void BuildUniversalJointUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("UniversalJoint")));
            c.Add(NF(() => _uJointP.MeshName, v => _uJointP.MeshName = v));
            c.Add(GearHint(T("UniversalJointHint")));

            c.Add(DD(T("UniversalJointType"),
                new List<string> { T("UniversalJointSingle"), T("UniversalJointDouble") },
                () => (int)_uJointP.Type,
                i => { _uJointP.Type = (UniversalJointType)i; RebuildSettings(); D(); }));

            float lo = UniversalJointMeshGenerator.Params.SizeMin;
            float hi = UniversalJointMeshGenerator.Params.SizeMax;

            c.Add(SR(T("UniversalJointInputBore"), lo, hi,
                () => _uJointP.InputBore, v => { _uJointP.InputBore = v; DM(); }));
            c.Add(SR(T("UniversalJointOutputBore"), lo, hi,
                () => _uJointP.OutputBore, v => { _uJointP.OutputBore = v; DM(); }));
            c.Add(SR(T("UniversalJointHubDiameter"), lo, hi,
                () => _uJointP.HubDiameter, v => { _uJointP.HubDiameter = v; DM(); }));
            c.Add(SR(T("UniversalJointHubLength"), lo, hi,
                () => _uJointP.HubLength, v => { _uJointP.HubLength = v; DM(); }));
            c.Add(SR(T("UniversalJointForkSpan"), lo, hi,
                () => _uJointP.ForkSpan, v => { _uJointP.ForkSpan = v; DM(); }));
            c.Add(SR(T("UniversalJointForkThickness"), lo, hi,
                () => _uJointP.ForkThickness, v => { _uJointP.ForkThickness = v; DM(); }));
            c.Add(SR(T("UniversalJointCrossDiameter"), lo, hi,
                () => _uJointP.CrossDiameter, v => { _uJointP.CrossDiameter = v; DM(); }));
            c.Add(SR(T("UniversalJointAngle"),
                UniversalJointMeshGenerator.Params.AngleMin, UniversalJointMeshGenerator.Params.AngleMax,
                () => _uJointP.JointAngleDeg, v => { _uJointP.JointAngleDeg = v; D(); }));
            c.Add(SR(T("UniversalJointPhase"),
                UniversalJointMeshGenerator.Params.PhaseMin, UniversalJointMeshGenerator.Params.PhaseMax,
                () => _uJointP.PhaseDeg, v => { _uJointP.PhaseDeg = v; D(); }));

            if (_uJointP.Type == UniversalJointType.DoubleCardan)
                c.Add(SR(T("UniversalJointCenterLength"), lo, hi,
                    () => _uJointP.CenterLength, v => { _uJointP.CenterLength = v; DM(); }));

            c.Add(IR(T("UniversalJointSegments"),
                UniversalJointMeshGenerator.Params.SegmentsMin, UniversalJointMeshGenerator.Params.SegmentsMax,
                () => _uJointP.Segments, v => { _uJointP.Segments = v; D(); }));

            BuildMechInfo(c, "UniversalJointDerived");
            BuildMechFooter(c,
                () => _uJointP.Orientation, v => _uJointP.Orientation = v,
                () => _uJointP.FlipFaces,   v => _uJointP.FlipFaces   = v,
                () => _uJointP.Pivot,       v => _uJointP.Pivot       = v);
        }

        private void RefreshUniversalJointInfo()
        {
            var i = UniversalJointMeshGenerator.GetInfo(_uJointP);
            if (!i.Valid)
            {
                SetMechInfo(T("UniversalJointInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("UniversalJointDerivedInfo", i.CrossCount, F(i.OverallLength)), true);
            SetMechWarn(null);
        }
    }
}
