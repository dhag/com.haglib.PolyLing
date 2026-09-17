// PlayerPrimitiveMeshSubPanel.RotorBlade.cs
// 図形生成サブパネル：プロペラ・ファン（高度な図形）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【生成経路】
//   パネルからメッシュを直接作らない。BuildCreateCommand（.Command.cs）で
//   コマンドを組み、PrimitiveMeshFactory が作る。プレビューも同じ経路を通る。
//
// 【UI の組み方】
//   諸元の行は行ヘルパ（SR / IR / TR / DD）だけで組む。生の DropdownField を
//   作って足すと UiDynamicControls へ載らず、MCP から触れなくなる。

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private RotorBladeMeshGenerator.Params _rotorBladeP = RotorBladeMeshGenerator.Params.Default;

        /// <summary>
        /// 用途メニューの表示順。列挙の数値は Args に int で書かれるので並べ替えず、
        /// 表示の番号とはこの配列で対応させる。
        /// </summary>
        private static readonly RotorBladeType[] RotorTypeMenuOrder =
        {
            RotorBladeType.AircraftPropeller, RotorBladeType.AxialFan,
            RotorBladeType.MarineScrew,       RotorBladeType.DuctedJetFan,
        };

        private void BuildRotorBladeUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("RotorBlade")));
            c.Add(NF(() => _rotorBladeP.MeshName, v => _rotorBladeP.MeshName = v));
            c.Add(GearHint(T("RotorBladeHint")));

            // 用途を変えると諸元がその用途の既定値へ入れ替わる。
            // 名前・姿勢・向き・面の反転は利用者が決めた値なので引き継ぐ。
            c.Add(DD(T("RotorBladeTypeLabel"),
                new List<string> { T("RotorAircraftProp"), T("RotorAxialFan"),
                                   T("RotorMarineScrew"), T("RotorJetFan") },
                () => System.Array.IndexOf(RotorTypeMenuOrder, _rotorBladeP.Type),
                i =>
                {
                    if (i < 0 || i >= RotorTypeMenuOrder.Length) return;
                    var name        = _rotorBladeP.MeshName;
                    var pivot       = _rotorBladeP.Pivot;
                    var orientation = _rotorBladeP.Orientation;
                    var flip        = _rotorBladeP.FlipFaces;

                    _rotorBladeP = RotorBladeMeshGenerator.Params.Preset(RotorTypeMenuOrder[i]);

                    _rotorBladeP.MeshName    = name;
                    _rotorBladeP.Pivot       = pivot;
                    _rotorBladeP.Orientation = orientation;
                    _rotorBladeP.FlipFaces   = flip;

                    RebuildSettings();
                    D();
                }));

            c.Add(IR(T("RotorBladeCount"),
                RotorBladeMeshGenerator.Params.BladeCountMin, RotorBladeMeshGenerator.Params.BladeCountMax,
                () => _rotorBladeP.BladeCount, v => { _rotorBladeP.BladeCount = v; D(); }));

            c.Add(SR(T("RotorDiameter"), RotorBladeMeshGenerator.Params.SizeMin, RotorBladeMeshGenerator.Params.SizeMax,
                () => _rotorBladeP.RotorDiameter, v => { _rotorBladeP.RotorDiameter = v; DM(); }));
            c.Add(SR(T("RotorHubDiameter"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                () => _rotorBladeP.HubDiameter, v => { _rotorBladeP.HubDiameter = v; DM(); }));
            c.Add(SR(T("RotorHubLength"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                () => _rotorBladeP.HubLength, v => { _rotorBladeP.HubLength = v; DM(); }));
            c.Add(SR(T("RotorShaftBore"), 0f, RotorBladeMeshGenerator.Params.PartMax,
                () => _rotorBladeP.ShaftBore, v => { _rotorBladeP.ShaftBore = v; DM(); }));

            c.Add(SL(T("RotorBladePlanform")));
            c.Add(SR(T("RotorRootChord"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                () => _rotorBladeP.RootChord, v => { _rotorBladeP.RootChord = v; DM(); }));
            c.Add(SR(T("RotorMaxChord"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                () => _rotorBladeP.MaxChord, v => { _rotorBladeP.MaxChord = v; DM(); }));
            c.Add(SR(T("RotorMaxChordPosition"), 0.05f, 0.95f,
                () => _rotorBladeP.MaxChordPosition, v => { _rotorBladeP.MaxChordPosition = v; DM(); }));
            c.Add(SR(T("RotorChordReference"), 0f, 1f,
                () => _rotorBladeP.ChordReference, v => { _rotorBladeP.ChordReference = v; D(); }));
            c.Add(SR(T("RotorTipApexPosition"), 0.05f, 0.95f,
                () => _rotorBladeP.TipApexPosition, v => { _rotorBladeP.TipApexPosition = v; D(); }));
            c.Add(SR(T("RotorTipCapShape"), 1.1f, 5f,
                () => _rotorBladeP.TipCapShape, v => { _rotorBladeP.TipCapShape = v; DM(); }));

            c.Add(SL(T("RotorBladeSection")));
            c.Add(SR(T("RotorRootPitch"), -80f, 80f,
                () => _rotorBladeP.RootPitchDeg, v => { _rotorBladeP.RootPitchDeg = v; D(); }));
            c.Add(SR(T("RotorTipPitch"), -80f, 80f,
                () => _rotorBladeP.TipPitchDeg, v => { _rotorBladeP.TipPitchDeg = v; D(); }));
            c.Add(SR(T("RotorThickness"), 0.01f, 0.4f,
                () => _rotorBladeP.ThicknessRatio, v => { _rotorBladeP.ThicknessRatio = v; D(); }));
            c.Add(SR(T("RotorCamber"), 0f, 0.3f,
                () => _rotorBladeP.CamberRatio, v => { _rotorBladeP.CamberRatio = v; D(); }));
            c.Add(SR(T("RotorSkew"), -70f, 70f,
                () => _rotorBladeP.SkewDeg, v => { _rotorBladeP.SkewDeg = v; D(); }));
            c.Add(SR(T("RotorRake"), -45f, 45f,
                () => _rotorBladeP.RakeDeg, v => { _rotorBladeP.RakeDeg = v; D(); }));

            c.Add(TR(T("RotorShowDuct"),
                () => _rotorBladeP.ShowDuct, v => { _rotorBladeP.ShowDuct = v; RebuildSettings(); D(); }));
            if (_rotorBladeP.ShowDuct)
            {
                c.Add(SR(T("RotorDuctThickness"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                    () => _rotorBladeP.DuctThickness, v => { _rotorBladeP.DuctThickness = v; DM(); }));
                c.Add(SR(T("RotorDuctLength"), RotorBladeMeshGenerator.Params.PartMin, RotorBladeMeshGenerator.Params.PartMax,
                    () => _rotorBladeP.DuctLength, v => { _rotorBladeP.DuctLength = v; DM(); }));
            }

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("RotorSpanSegments"),
                RotorBladeMeshGenerator.Params.SpanSegmentsMin, RotorBladeMeshGenerator.Params.SpanSegmentsMax,
                () => _rotorBladeP.SpanSegments, v => { _rotorBladeP.SpanSegments = v; D(); }));
            c.Add(IR(T("RotorChordSegments"),
                RotorBladeMeshGenerator.Params.ChordSegmentsMin, RotorBladeMeshGenerator.Params.ChordSegmentsMax,
                () => _rotorBladeP.ChordSegments, v => { _rotorBladeP.ChordSegments = v; D(); }));

            BuildMechInfo(c, "RotorDerived");
            BuildMechFooter(c,
                () => _rotorBladeP.Orientation, v => _rotorBladeP.Orientation = v,
                () => _rotorBladeP.FlipFaces,   v => _rotorBladeP.FlipFaces   = v,
                () => _rotorBladeP.Pivot,       v => _rotorBladeP.Pivot       = v);
        }

        private void RefreshRotorBladeInfo()
        {
            var i = RotorBladeMeshGenerator.GetInfo(_rotorBladeP);
            if (!i.Valid)
            {
                SetMechInfo(T("RotorInvalid"), false);
                SetMechWarn(null);
                return;
            }
            SetMechInfo(T("RotorDerivedInfo", F(i.Solidity), F(i.TipClearance)), true);
            SetMechWarn(null);
        }
    }
}
