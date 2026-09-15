// PlayerPrimitiveMeshSubPanel.ShaftHolder.cs
// 図形生成サブパネル：軸受ホルダ（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private ShaftHolderMeshGenerator.Params _shaftHolderP = ShaftHolderMeshGenerator.Params.Default;

        private void BuildShaftHolderUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("ShaftHolder")));
            c.Add(NF(() => _shaftHolderP.MeshName, v => _shaftHolderP.MeshName = v));
            c.Add(GearHint(T("ShaftHolderHint")));

            c.Add(DD(T("ShaftHolderType"),
                new List<string> { T("ShaftHolderRound"), T("ShaftHolderTwoFlat"),
                                   T("ShaftHolderSquare"), T("ShaftHolderBase") },
                () => (int)_shaftHolderP.Type,
                i => { _shaftHolderP.Type = (ShaftHolderType)i; DM(); }));
            c.Add(DD(T("ShaftHolderClampType"),
                new List<string> { T("ShaftHolderSetScrew"), T("ShaftHolderSplit") },
                () => (int)_shaftHolderP.ClampType,
                i => { _shaftHolderP.ClampType = (ShaftHolderClampType)i; RebuildSettings(); D(); }));

            float lo = ShaftHolderMeshGenerator.Params.SizeMin;
            float hi = ShaftHolderMeshGenerator.Params.SizeMax;

            c.Add(SR(T("ShaftHolderBore"), lo, hi,
                () => _shaftHolderP.BoreDiameter, v => { _shaftHolderP.BoreDiameter = v; DM(); }));
            c.Add(SR(T("ShaftHolderBoss"), lo, hi,
                () => _shaftHolderP.BossDiameter, v => { _shaftHolderP.BossDiameter = v; DM(); }));
            c.Add(SR(T("ShaftHolderBossLength"), lo, hi,
                () => _shaftHolderP.BossLength, v => { _shaftHolderP.BossLength = v; DM(); }));
            c.Add(SR(T("ShaftHolderFlangeSize"), lo, hi,
                () => _shaftHolderP.FlangeSize, v => { _shaftHolderP.FlangeSize = v; DM(); }));
            c.Add(SR(T("ShaftHolderFlangeThickness"), lo, hi,
                () => _shaftHolderP.FlangeThickness, v => { _shaftHolderP.FlangeThickness = v; DM(); }));
            c.Add(SR(T("ShaftHolderMountPitch"), lo, hi,
                () => _shaftHolderP.MountPitch, v => { _shaftHolderP.MountPitch = v; DM(); }));
            c.Add(SR(T("ShaftHolderMountHole"), lo, hi,
                () => _shaftHolderP.MountHoleDiameter, v => { _shaftHolderP.MountHoleDiameter = v; DM(); }));
            c.Add(SR(T("ShaftHolderClampHole"), lo, hi,
                () => _shaftHolderP.ClampHoleDiameter, v => { _shaftHolderP.ClampHoleDiameter = v; DM(); }));

            if (_shaftHolderP.ClampType == ShaftHolderClampType.SplitClamp)
                c.Add(SR(T("ShaftHolderSplitWidth"), lo, hi,
                    () => _shaftHolderP.SplitWidth, v => { _shaftHolderP.SplitWidth = v; DM(); }));

            c.Add(IR(T("ShaftHolderSegments"),
                ShaftHolderMeshGenerator.Params.SegmentsMin, ShaftHolderMeshGenerator.Params.SegmentsMax,
                () => _shaftHolderP.Segments, v => { _shaftHolderP.Segments = v; D(); }));

            BuildMechInfo(c, "ShaftHolderDerived");
            BuildMechFooter(c,
                () => _shaftHolderP.Orientation, v => _shaftHolderP.Orientation = v,
                () => _shaftHolderP.FlipFaces,   v => _shaftHolderP.FlipFaces   = v,
                () => _shaftHolderP.Pivot,       v => _shaftHolderP.Pivot       = v);
        }

        private void RefreshShaftHolderInfo()
        {
            var i = ShaftHolderMeshGenerator.GetInfo(_shaftHolderP);
            if (!i.Valid)
            {
                SetMechInfo(T("ShaftHolderInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("ShaftHolderDerivedInfo", i.MountHoleCount, F(i.WallThickness)), true);
            SetMechWarn(null);
        }
    }
}
