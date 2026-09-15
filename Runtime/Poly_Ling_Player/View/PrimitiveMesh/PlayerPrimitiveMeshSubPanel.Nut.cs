// PlayerPrimitiveMeshSubPanel.Nut.cs
// 図形生成サブパネル：ナット（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【生成経路】
//   パネルからメッシュを直接作らない。BuildCreateCommand（.Command.cs）で
//   コマンドを組み、PrimitiveMeshFactory が作る。

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private NutMeshGenerator.NutParams _nutP = NutMeshGenerator.NutParams.Default;

        private void BuildNutUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Nut")));
            c.Add(NF(() => _nutP.MeshName, v => _nutP.MeshName = v));
            c.Add(GearHint(T("NutHint")));

            c.Add(SL(T("NutBody")));
            c.Add(DD(T("NutOuterType"),
                new List<string> { T("NutOuterHex"), T("NutOuterSquare"), T("NutOuterRound") },
                () => (int)_nutP.OuterType, i => { _nutP.OuterType = (NutOuterType)i; DM(); }));
            c.Add(SR(T("NutOuterDiameter"),
                NutMeshGenerator.NutParams.DiameterMin, NutMeshGenerator.NutParams.DiameterMax,
                () => _nutP.OuterDiameter, v => { _nutP.OuterDiameter = v; DM(); }));
            c.Add(SR(T("NutHeight"),
                NutMeshGenerator.NutParams.HeightMin, NutMeshGenerator.NutParams.HeightMax,
                () => _nutP.Height, v => { _nutP.Height = v; DM(); }));
            c.Add(SR(T("NutChamfer"),
                NutMeshGenerator.NutParams.ChamferMin, NutMeshGenerator.NutParams.ChamferMax,
                () => _nutP.Chamfer, v => { _nutP.Chamfer = v; DM(); }));

            c.Add(SL(T("NutInternalThread")));
            c.Add(SR(T("NutThreadMajorDiameter"),
                NutMeshGenerator.NutParams.DiameterMin, NutMeshGenerator.NutParams.DiameterMax,
                () => _nutP.ThreadMajorDiameter, v => { _nutP.ThreadMajorDiameter = v; DM(); }));
            c.Add(SR(T("NutThreadMinorDiameter"),
                NutMeshGenerator.NutParams.DiameterMin, NutMeshGenerator.NutParams.DiameterMax,
                () => _nutP.ThreadMinorDiameter, v => { _nutP.ThreadMinorDiameter = v; DM(); }));
            c.Add(SR(T("NutPitch"),
                NutMeshGenerator.NutParams.PitchMin, NutMeshGenerator.NutParams.PitchMax,
                () => _nutP.Pitch, v => { _nutP.Pitch = v; DM(); }));
            c.Add(TR(T("NutRightHand"), () => _nutP.RightHand, v => { _nutP.RightHand = v; D(); }));

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("NutRadialSegments"),
                NutMeshGenerator.NutParams.RadialSegmentsMin, NutMeshGenerator.NutParams.RadialSegmentsMax,
                () => _nutP.RadialSegments, v => { _nutP.RadialSegments = v; D(); }));
            c.Add(IR(T("NutSamplesPerPitch"),
                NutMeshGenerator.NutParams.SamplesPerPitchMin, NutMeshGenerator.NutParams.SamplesPerPitchMax,
                () => _nutP.SamplesPerPitch, v => { _nutP.SamplesPerPitch = v; D(); }));

            BuildMechInfo(c, "NutDerived");
            BuildMechFooter(c,
                () => _nutP.Orientation, v => _nutP.Orientation = v,
                () => _nutP.FlipFaces,   v => _nutP.FlipFaces   = v,
                () => _nutP.Pivot,       v => _nutP.Pivot       = v);
        }

        private void RefreshNutInfo()
        {
            var info = NutMeshGenerator.GetInfo(_nutP);
            if (!info.Valid)
            {
                SetMechInfo(T("NutInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("NutDerivedInfo", info.ThreadTurns.ToString("F2")), true);

            var warnings = new List<string>();
            if (info.ChamferClamped) warnings.Add(T("NutWarnChamfer"));
            SetMechWarn(warnings);
        }
    }
}
