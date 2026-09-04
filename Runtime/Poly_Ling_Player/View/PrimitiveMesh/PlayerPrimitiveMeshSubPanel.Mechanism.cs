// PlayerPrimitiveMeshSubPanel.Mechanism.cs
// 図形生成サブパネル：機構部品（はすば歯車 / 内歯車 / ラック / はすばラック /
// すぐばかさ歯車 / まがりばかさ歯車 / 円筒ウォーム / ウォームホイール）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【生成器はどこにあるか】
//   Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ 配下。
//   歯形は InvoluteTrochoidSection、ラック歯は RackToothSection、
//   立体化は GearLoftBuilder が受け持つ。ここはパラメータを触る UI だけを持つ。
//
// 【生成経路】
//   パネルからメッシュを直接作らない。BuildCreateCommand（.Command.cs）で
//   コマンドを組み、PrimitiveMeshFactory が作る。プレビューも同じ経路を通る。
//
// 【派生諸元の表示】
//   図形は 8 種あるが、いちどに組み立てられる設定 UI は 1 つだけなので、
//   情報ラベルと警告ラベルは 1 組だけ持ち、RefreshMechInfo が現在の図形で振り分ける。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private HelicalGearMeshGenerator.HelicalGearParams _helGearP =
            HelicalGearMeshGenerator.HelicalGearParams.Default;
        private InternalGearMeshGenerator.InternalGearParams _intGearP =
            InternalGearMeshGenerator.InternalGearParams.Default;
        private InvoluteRackMeshGenerator.InvoluteRackParams _rackP =
            InvoluteRackMeshGenerator.InvoluteRackParams.Default;
        private HelicalRackMeshGenerator.HelicalRackParams _helRackP =
            HelicalRackMeshGenerator.HelicalRackParams.Default;
        private StraightBevelGearMeshGenerator.StraightBevelGearParams _strBevelP =
            StraightBevelGearMeshGenerator.StraightBevelGearParams.Default;
        private SpiralBevelGearMeshGenerator.SpiralBevelGearParams _spiBevelP =
            SpiralBevelGearMeshGenerator.SpiralBevelGearParams.Default;
        private CylindricalWormMeshGenerator.CylindricalWormParams _wormP =
            CylindricalWormMeshGenerator.CylindricalWormParams.Default;
        private WormWheelMeshGenerator.WormWheelParams _wheelP =
            WormWheelMeshGenerator.WormWheelParams.Default;

        /// <summary>機構部品の派生諸元ラベル。設定 UI を組み直すたびに差し替わる。</summary>
        private Label _mechInfoLabel;
        /// <summary>機構部品の警告ラベル。</summary>
        private Label _mechWarnLabel;

        private static readonly Color MechInfoColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color MechWarnColor = new Color(1f, 0.6f, 0.4f);

        // ================================================================
        // 共有 UI 部品
        // ================================================================

        /// <summary>派生諸元を変える操作用の D()。値を変えたら諸元表示も書き直す。</summary>
        private void DM()
        {
            D();
            RefreshMechInfo();
        }

        /// <summary>派生諸元と警告の 2 行を置く。</summary>
        private void BuildMechInfo(VisualElement c, string sectionKey)
        {
            c.Add(SL(T(sectionKey)));

            _mechInfoLabel = GearHint(string.Empty);
            c.Add(_mechInfoLabel);

            _mechWarnLabel = GearHint(string.Empty);
            _mechWarnLabel.style.color = new StyleColor(MechWarnColor);
            c.Add(_mechWarnLabel);

            RefreshMechInfo();
        }

        /// <summary>歯たけ係数の 2 行。機構部品すべてで同じ並びにする。</summary>
        private void BuildToothDepthRows(
            VisualElement c,
            float min, float max,
            System.Func<float> getHa, System.Action<float> setHa,
            System.Func<float> getHf, System.Action<float> setHf)
        {
            c.Add(SL(T("GearToothDepth")));
            c.Add(GearHint(T("GearToothDepthHint")));
            c.Add(SR(T("GearAddendumCoef"), min, max, getHa, v => { setHa(v); DM(); }));
            c.Add(SR(T("GearDedendumCoef"), min, max, getHf, v => { setHf(v); DM(); }));
        }

        /// <summary>軸穴の 2 行。</summary>
        private void BuildBoreRows(
            VisualElement c,
            float radiusMin, float radiusMax,
            System.Func<float> getR, System.Action<float> setR,
            System.Func<int> getSeg, System.Action<int> setSeg)
        {
            c.Add(SL(T("GearBore")));
            c.Add(GearHint(T("GearBoreHint")));
            c.Add(SR(T("GearBoreRadius"), radiusMin, radiusMax, getR, v => { setR(v); DM(); }));
            c.Add(IR(T("GearBoreSegments"),
                GearDiskBuilder.BoreSegmentsMin, GearDiskBuilder.BoreSegmentsMax,
                getSeg, v => { setSeg(v); D(); }));
        }

        /// <summary>配置面・面の向き・ピボットの締めくくり。</summary>
        private void BuildMechFooter(
            VisualElement c,
            System.Func<PlaneOrientation> getOri, System.Action<PlaneOrientation> setOri,
            System.Func<bool> getFlip, System.Action<bool> setFlip,
            System.Func<Vector3> getPivot, System.Action<Vector3> setPivot)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(OrientationDD(getOri, v => { setOri(v); D(); }));
            c.Add(TR(T("FlipFaces"), getFlip, v => { setFlip(v); D(); }));

            BuildPivotXYZ(c,
                getPivot, v => { setPivot(v); D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        private void SetMechInfo(string text, bool valid)
        {
            if (_mechInfoLabel == null) return;

            _mechInfoLabel.text = text;
            _mechInfoLabel.style.color = new StyleColor(valid ? MechInfoColor : MechWarnColor);
        }

        private void SetMechWarn(List<string> warnings)
        {
            if (_mechWarnLabel == null) return;

            _mechWarnLabel.text =
                warnings != null && warnings.Count > 0 ? string.Join("\n", warnings) : string.Empty;
        }

        private static string F(float v) => v.ToString("F4");

        // ================================================================
        // はすば歯車
        // ================================================================

        private void BuildHelicalGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("HelicalGear")));
            c.Add(NF(() => _helGearP.MeshName, v => _helGearP.MeshName = v));
            c.Add(GearHint(T("HelicalGearHint")));

            c.Add(IR(T("InvToothCount"),
                HelicalGearMeshGenerator.HelicalGearParams.ToothCountMin,
                HelicalGearMeshGenerator.HelicalGearParams.ToothCountMax,
                () => _helGearP.ToothCount, v => { _helGearP.ToothCount = v; DM(); }));
            c.Add(SR(T("HelNormalModule"),
                HelicalGearMeshGenerator.HelicalGearParams.ModuleMin,
                HelicalGearMeshGenerator.HelicalGearParams.ModuleMax,
                () => _helGearP.NormalModule, v => { _helGearP.NormalModule = v; DM(); }));
            c.Add(SR(T("HelNormalPressureAngle"),
                HelicalGearMeshGenerator.HelicalGearParams.PressureAngleMin,
                HelicalGearMeshGenerator.HelicalGearParams.PressureAngleMax,
                () => _helGearP.NormalPressureAngleDeg,
                v => { _helGearP.NormalPressureAngleDeg = v; DM(); }));

            c.Add(GearHint(T("HelHelixAngleHint")));
            c.Add(SR(T("HelHelixAngle"),
                HelicalGearMeshGenerator.HelicalGearParams.HelixAngleMin,
                HelicalGearMeshGenerator.HelicalGearParams.HelixAngleMax,
                () => _helGearP.HelixAngleDeg, v => { _helGearP.HelixAngleDeg = v; DM(); }));

            c.Add(SR(T("HelFaceWidth"),
                HelicalGearMeshGenerator.HelicalGearParams.ThicknessMin,
                HelicalGearMeshGenerator.HelicalGearParams.ThicknessMax,
                () => _helGearP.Thickness, v => { _helGearP.Thickness = v; DM(); }));

            BuildToothDepthRows(c,
                HelicalGearMeshGenerator.HelicalGearParams.ToothDepthCoefMin,
                HelicalGearMeshGenerator.HelicalGearParams.ToothDepthCoefMax,
                () => _helGearP.AddendumCoef, v => _helGearP.AddendumCoef = v,
                () => _helGearP.DedendumCoef, v => _helGearP.DedendumCoef = v);

            c.Add(SL(T("InvCorrection")));
            c.Add(GearHint(T("InvCorrectionHint")));
            c.Add(SR(T("InvProfileShift"),
                HelicalGearMeshGenerator.HelicalGearParams.ProfileShiftMin,
                HelicalGearMeshGenerator.HelicalGearParams.ProfileShiftMax,
                () => _helGearP.ProfileShift, v => { _helGearP.ProfileShift = v; DM(); }));
            c.Add(SR(T("HelTransverseBacklash"),
                HelicalGearMeshGenerator.HelicalGearParams.BacklashMin,
                HelicalGearMeshGenerator.HelicalGearParams.BacklashMax,
                () => _helGearP.Backlash, v => { _helGearP.Backlash = v; DM(); }));

            BuildBoreRows(c,
                HelicalGearMeshGenerator.HelicalGearParams.BoreRadiusMin,
                HelicalGearMeshGenerator.HelicalGearParams.BoreRadiusMax,
                () => _helGearP.BoreRadius, v => _helGearP.BoreRadius = v,
                () => _helGearP.BoreSegments, v => _helGearP.BoreSegments = v);

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvTrochoidSamples"),
                HelicalGearMeshGenerator.HelicalGearParams.CurveSamplesMin,
                HelicalGearMeshGenerator.HelicalGearParams.CurveSamplesMax,
                () => _helGearP.TrochoidSamples, v => { _helGearP.TrochoidSamples = v; D(); }));
            c.Add(IR(T("InvInvoluteSamples"),
                HelicalGearMeshGenerator.HelicalGearParams.CurveSamplesMin,
                HelicalGearMeshGenerator.HelicalGearParams.CurveSamplesMax,
                () => _helGearP.InvoluteSamples, v => { _helGearP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"),
                HelicalGearMeshGenerator.HelicalGearParams.ArcSamplesMin,
                HelicalGearMeshGenerator.HelicalGearParams.ArcSamplesMax,
                () => _helGearP.TipArcSamples, v => { _helGearP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"),
                HelicalGearMeshGenerator.HelicalGearParams.ArcSamplesMin,
                HelicalGearMeshGenerator.HelicalGearParams.ArcSamplesMax,
                () => _helGearP.RootArcSamples, v => { _helGearP.RootArcSamples = v; D(); }));
            c.Add(IR(T("HelAxialSegments"),
                HelicalGearMeshGenerator.HelicalGearParams.AxialSegmentsMin,
                HelicalGearMeshGenerator.HelicalGearParams.AxialSegmentsMax,
                () => _helGearP.AxialSegments, v => { _helGearP.AxialSegments = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                HelicalGearMeshGenerator.HelicalGearParams.RotationOffsetMin,
                HelicalGearMeshGenerator.HelicalGearParams.RotationOffsetMax,
                () => _helGearP.RotationOffsetDeg, v => { _helGearP.RotationOffsetDeg = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _helGearP.Orientation, v => _helGearP.Orientation = v,
                () => _helGearP.FlipFaces,   v => _helGearP.FlipFaces = v,
                () => _helGearP.Pivot,       v => _helGearP.Pivot = v);
        }

        private void RefreshHelicalGearInfo()
        {
            var info = HelicalGearMeshGenerator.GetInfo(_helGearP);

            if (!info.Valid)
            {
                SetMechInfo(T("HelInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("HelDerivedInfo",
                F(info.TransverseModule), info.TransversePressureAngleDeg.ToString("F2"),
                F(info.PitchDiameter), F(info.BaseDiameter),
                F(info.TipDiameter), F(info.RootDiameter),
                info.TotalTwistDeg.ToString("F2"),
                float.IsInfinity(info.Lead) ? "∞" : F(info.Lead),
                info.VirtualToothCount.ToString("F2")), true);

            var warn = new List<string>();

            if (info.BelowMinToothCount)
                warn.Add(T("HelWarnMinTeeth",
                    _helGearP.NormalPressureAngleDeg.ToString("F1"),
                    info.VirtualToothCount.ToString("F1"),
                    info.MinToothCountApprox.ToString("F1")));

            if (info.SevereUndercut) warn.Add(T("InvWarnSevereUndercut"));
            else if (info.Undercut)  warn.Add(T("InvWarnUndercut"));

            if (info.BoreTooLarge) warn.Add(T("GearWarnBore"));

            SetMechWarn(warn);
        }

        // ================================================================
        // 内歯車
        // ================================================================

        private void BuildInternalGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("InternalGear")));
            c.Add(NF(() => _intGearP.MeshName, v => _intGearP.MeshName = v));
            c.Add(GearHint(T("InternalGearHint")));

            c.Add(IR(T("InvToothCount"),
                InternalGearMeshGenerator.InternalGearParams.ToothCountMin,
                InternalGearMeshGenerator.InternalGearParams.ToothCountMax,
                () => _intGearP.ToothCount, v => { _intGearP.ToothCount = v; DM(); }));
            c.Add(SR(T("InvModule"),
                InternalGearMeshGenerator.InternalGearParams.ModuleMin,
                InternalGearMeshGenerator.InternalGearParams.ModuleMax,
                () => _intGearP.Module, v => { _intGearP.Module = v; DM(); }));
            c.Add(SR(T("InvPressureAngle"),
                InternalGearMeshGenerator.InternalGearParams.PressureAngleMin,
                InternalGearMeshGenerator.InternalGearParams.PressureAngleMax,
                () => _intGearP.PressureAngleDeg, v => { _intGearP.PressureAngleDeg = v; DM(); }));
            c.Add(SR(T("Thickness"),
                InternalGearMeshGenerator.InternalGearParams.ThicknessMin,
                InternalGearMeshGenerator.InternalGearParams.ThicknessMax,
                () => _intGearP.Thickness, v => { _intGearP.Thickness = v; D(); }));

            BuildToothDepthRows(c,
                InternalGearMeshGenerator.InternalGearParams.ToothDepthCoefMin,
                InternalGearMeshGenerator.InternalGearParams.ToothDepthCoefMax,
                () => _intGearP.AddendumCoef, v => _intGearP.AddendumCoef = v,
                () => _intGearP.DedendumCoef, v => _intGearP.DedendumCoef = v);

            c.Add(SR(T("InvBacklash"),
                InternalGearMeshGenerator.InternalGearParams.BacklashMin,
                InternalGearMeshGenerator.InternalGearParams.BacklashMax,
                () => _intGearP.Backlash, v => { _intGearP.Backlash = v; DM(); }));

            c.Add(SR(T("IntRimThickness"),
                InternalGearMeshGenerator.InternalGearParams.RimThicknessMin,
                InternalGearMeshGenerator.InternalGearParams.RimThicknessMax,
                () => _intGearP.RimThickness, v => { _intGearP.RimThickness = v; DM(); }));

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvInvoluteSamples"),
                InternalGearMeshGenerator.InternalGearParams.CurveSamplesMin,
                InternalGearMeshGenerator.InternalGearParams.CurveSamplesMax,
                () => _intGearP.InvoluteSamples, v => { _intGearP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"),
                InternalGearMeshGenerator.InternalGearParams.ArcSamplesMin,
                InternalGearMeshGenerator.InternalGearParams.ArcSamplesMax,
                () => _intGearP.TipArcSamples, v => { _intGearP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"),
                InternalGearMeshGenerator.InternalGearParams.ArcSamplesMin,
                InternalGearMeshGenerator.InternalGearParams.ArcSamplesMax,
                () => _intGearP.RootArcSamples, v => { _intGearP.RootArcSamples = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                InternalGearMeshGenerator.InternalGearParams.RotationOffsetMin,
                InternalGearMeshGenerator.InternalGearParams.RotationOffsetMax,
                () => _intGearP.RotationOffsetDeg, v => { _intGearP.RotationOffsetDeg = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _intGearP.Orientation, v => _intGearP.Orientation = v,
                () => _intGearP.FlipFaces,   v => _intGearP.FlipFaces = v,
                () => _intGearP.Pivot,       v => _intGearP.Pivot = v);
        }

        private void RefreshInternalGearInfo()
        {
            var info = InternalGearMeshGenerator.GetInfo(_intGearP);

            if (!info.Valid)
            {
                SetMechInfo(T("IntInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("IntDerivedInfo",
                F(info.PitchDiameter), F(info.BaseDiameter),
                F(info.TipDiameter), F(info.RootDiameter), F(info.OuterDiameter),
                F(info.CircularPitch), F(info.ToothThicknessPitch)), true);

            var warn = new List<string>();
            if (info.TipBelowBase) warn.Add(T("IntWarnTipBelowBase"));
            SetMechWarn(warn);
        }

        // ================================================================
        // ラック
        // ================================================================

        private void BuildInvoluteRackUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("InvoluteRack")));
            c.Add(NF(() => _rackP.MeshName, v => _rackP.MeshName = v));
            c.Add(GearHint(T("InvoluteRackHint")));

            c.Add(IR(T("RackToothCount"),
                InvoluteRackMeshGenerator.InvoluteRackParams.ToothCountMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.ToothCountMax,
                () => _rackP.ToothCount, v => { _rackP.ToothCount = v; DM(); }));
            c.Add(SR(T("InvModule"),
                InvoluteRackMeshGenerator.InvoluteRackParams.ModuleMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.ModuleMax,
                () => _rackP.Module, v => { _rackP.Module = v; DM(); }));
            c.Add(SR(T("InvPressureAngle"),
                InvoluteRackMeshGenerator.InvoluteRackParams.PressureAngleMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.PressureAngleMax,
                () => _rackP.PressureAngleDeg, v => { _rackP.PressureAngleDeg = v; DM(); }));
            c.Add(SR(T("RackFaceWidth"),
                InvoluteRackMeshGenerator.InvoluteRackParams.FaceWidthMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.FaceWidthMax,
                () => _rackP.FaceWidth, v => { _rackP.FaceWidth = v; D(); }));
            c.Add(SR(T("RackBodyHeight"),
                InvoluteRackMeshGenerator.InvoluteRackParams.BodyHeightMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.BodyHeightMax,
                () => _rackP.BodyHeight, v => { _rackP.BodyHeight = v; DM(); }));

            BuildToothDepthRows(c,
                InvoluteRackMeshGenerator.InvoluteRackParams.ToothDepthCoefMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.ToothDepthCoefMax,
                () => _rackP.AddendumCoef, v => _rackP.AddendumCoef = v,
                () => _rackP.DedendumCoef, v => _rackP.DedendumCoef = v);

            c.Add(SR(T("InvBacklash"),
                InvoluteRackMeshGenerator.InvoluteRackParams.BacklashMin,
                InvoluteRackMeshGenerator.InvoluteRackParams.BacklashMax,
                () => _rackP.Backlash, v => { _rackP.Backlash = v; DM(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _rackP.Orientation, v => _rackP.Orientation = v,
                () => _rackP.FlipFaces,   v => _rackP.FlipFaces = v,
                () => _rackP.Pivot,       v => _rackP.Pivot = v);
        }

        private void RefreshInvoluteRackInfo()
        {
            var info = InvoluteRackMeshGenerator.GetInfo(_rackP);

            if (!info.Valid)
            {
                SetMechInfo(T("RackInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("RackDerivedInfo",
                F(info.Pitch), F(info.Length), F(info.Addendum), F(info.Dedendum),
                F(info.TotalHeight), F(info.ToothThicknessPitchLine),
                F(info.TipWidth), F(info.RootWidth)), true);

            SetMechWarn(null);
        }

        // ================================================================
        // はすばラック
        // ================================================================

        private void BuildHelicalRackUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("HelicalRack")));
            c.Add(NF(() => _helRackP.MeshName, v => _helRackP.MeshName = v));
            c.Add(GearHint(T("HelicalRackHint")));

            c.Add(IR(T("RackToothCount"),
                HelicalRackMeshGenerator.HelicalRackParams.ToothCountMin,
                HelicalRackMeshGenerator.HelicalRackParams.ToothCountMax,
                () => _helRackP.ToothCount, v => { _helRackP.ToothCount = v; DM(); }));
            c.Add(SR(T("HelNormalModule"),
                HelicalRackMeshGenerator.HelicalRackParams.ModuleMin,
                HelicalRackMeshGenerator.HelicalRackParams.ModuleMax,
                () => _helRackP.NormalModule, v => { _helRackP.NormalModule = v; DM(); }));
            c.Add(SR(T("HelNormalPressureAngle"),
                HelicalRackMeshGenerator.HelicalRackParams.PressureAngleMin,
                HelicalRackMeshGenerator.HelicalRackParams.PressureAngleMax,
                () => _helRackP.NormalPressureAngleDeg,
                v => { _helRackP.NormalPressureAngleDeg = v; DM(); }));
            c.Add(SR(T("HelHelixAngle"),
                HelicalRackMeshGenerator.HelicalRackParams.HelixAngleMin,
                HelicalRackMeshGenerator.HelicalRackParams.HelixAngleMax,
                () => _helRackP.HelixAngleDeg, v => { _helRackP.HelixAngleDeg = v; DM(); }));
            c.Add(SR(T("RackFaceWidth"),
                HelicalRackMeshGenerator.HelicalRackParams.FaceWidthMin,
                HelicalRackMeshGenerator.HelicalRackParams.FaceWidthMax,
                () => _helRackP.FaceWidth, v => { _helRackP.FaceWidth = v; DM(); }));
            c.Add(SR(T("RackBodyHeight"),
                HelicalRackMeshGenerator.HelicalRackParams.BodyHeightMin,
                HelicalRackMeshGenerator.HelicalRackParams.BodyHeightMax,
                () => _helRackP.BodyHeight, v => { _helRackP.BodyHeight = v; DM(); }));

            BuildToothDepthRows(c,
                HelicalRackMeshGenerator.HelicalRackParams.ToothDepthCoefMin,
                HelicalRackMeshGenerator.HelicalRackParams.ToothDepthCoefMax,
                () => _helRackP.AddendumCoef, v => _helRackP.AddendumCoef = v,
                () => _helRackP.DedendumCoef, v => _helRackP.DedendumCoef = v);

            c.Add(SR(T("HelTransverseBacklash"),
                HelicalRackMeshGenerator.HelicalRackParams.BacklashMin,
                HelicalRackMeshGenerator.HelicalRackParams.BacklashMax,
                () => _helRackP.Backlash, v => { _helRackP.Backlash = v; DM(); }));

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("RackSamplesPerPitch"),
                HelicalRackMeshGenerator.HelicalRackParams.SamplesPerPitchMin,
                HelicalRackMeshGenerator.HelicalRackParams.SamplesPerPitchMax,
                () => _helRackP.SamplesPerPitch, v => { _helRackP.SamplesPerPitch = v; D(); }));
            c.Add(IR(T("HelAxialSegments"),
                HelicalRackMeshGenerator.HelicalRackParams.FaceSegmentsMin,
                HelicalRackMeshGenerator.HelicalRackParams.FaceSegmentsMax,
                () => _helRackP.FaceSegments, v => { _helRackP.FaceSegments = v; D(); }));

            c.Add(SR(T("RackPhaseOffset"),
                HelicalRackMeshGenerator.HelicalRackParams.PhaseOffsetMin,
                HelicalRackMeshGenerator.HelicalRackParams.PhaseOffsetMax,
                () => _helRackP.PhaseOffset, v => { _helRackP.PhaseOffset = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _helRackP.Orientation, v => _helRackP.Orientation = v,
                () => _helRackP.FlipFaces,   v => _helRackP.FlipFaces = v,
                () => _helRackP.Pivot,       v => _helRackP.Pivot = v);
        }

        private void RefreshHelicalRackInfo()
        {
            var info = HelicalRackMeshGenerator.GetInfo(_helRackP);

            if (!info.Valid)
            {
                SetMechInfo(T("RackInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("HelRackDerivedInfo",
                F(info.TransverseModule), info.TransversePressureAngleDeg.ToString("F2"),
                F(info.TransversePitch), F(info.NormalPitch),
                F(info.Length), F(info.TotalHeight),
                F(info.ToothShiftAcrossFace)), true);

            var warn = new List<string>();
            if (info.ShiftExceedsPitch) warn.Add(T("RackWarnShift"));
            SetMechWarn(warn);
        }

        // ================================================================
        // すぐばかさ歯車
        // ================================================================

        private void BuildStraightBevelGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("StraightBevelGear")));
            c.Add(NF(() => _strBevelP.MeshName, v => _strBevelP.MeshName = v));
            c.Add(GearHint(T("StraightBevelGearHint")));

            c.Add(SL(T("BevPair")));
            c.Add(IR(T("InvToothCount"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothCountMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothCountMax,
                () => _strBevelP.ToothCount, v => { _strBevelP.ToothCount = v; DM(); }));
            c.Add(IR(T("BevMatingToothCount"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothCountMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothCountMax,
                () => _strBevelP.MatingToothCount, v => { _strBevelP.MatingToothCount = v; DM(); }));
            c.Add(SR(T("BevShaftAngle"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ShaftAngleMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ShaftAngleMax,
                () => _strBevelP.ShaftAngleDeg, v => { _strBevelP.ShaftAngleDeg = v; DM(); }));

            c.Add(SR(T("BevModule"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ModuleMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ModuleMax,
                () => _strBevelP.Module, v => { _strBevelP.Module = v; DM(); }));
            c.Add(SR(T("InvPressureAngle"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.PressureAngleMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.PressureAngleMax,
                () => _strBevelP.PressureAngleDeg,
                v => { _strBevelP.PressureAngleDeg = v; DM(); }));
            c.Add(SR(T("BevFaceWidth"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.FaceWidthMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.FaceWidthMax,
                () => _strBevelP.FaceWidth, v => { _strBevelP.FaceWidth = v; DM(); }));

            BuildToothDepthRows(c,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothDepthCoefMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ToothDepthCoefMax,
                () => _strBevelP.AddendumCoef, v => _strBevelP.AddendumCoef = v,
                () => _strBevelP.DedendumCoef, v => _strBevelP.DedendumCoef = v);

            c.Add(SR(T("InvBacklash"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.BacklashMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.BacklashMax,
                () => _strBevelP.Backlash, v => { _strBevelP.Backlash = v; DM(); }));

            BuildBoreRows(c,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.BoreRadiusMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.BoreRadiusMax,
                () => _strBevelP.BoreRadius, v => _strBevelP.BoreRadius = v,
                () => _strBevelP.BoreSegments, v => _strBevelP.BoreSegments = v);

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvTrochoidSamples"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.CurveSamplesMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.CurveSamplesMax,
                () => _strBevelP.TrochoidSamples, v => { _strBevelP.TrochoidSamples = v; D(); }));
            c.Add(IR(T("InvInvoluteSamples"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.CurveSamplesMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.CurveSamplesMax,
                () => _strBevelP.InvoluteSamples, v => { _strBevelP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ArcSamplesMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ArcSamplesMax,
                () => _strBevelP.TipArcSamples, v => { _strBevelP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ArcSamplesMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.ArcSamplesMax,
                () => _strBevelP.RootArcSamples, v => { _strBevelP.RootArcSamples = v; D(); }));
            c.Add(IR(T("BevFaceSegments"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.FaceSegmentsMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.FaceSegmentsMax,
                () => _strBevelP.FaceSegments, v => { _strBevelP.FaceSegments = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                StraightBevelGearMeshGenerator.StraightBevelGearParams.RotationOffsetMin,
                StraightBevelGearMeshGenerator.StraightBevelGearParams.RotationOffsetMax,
                () => _strBevelP.RotationOffsetDeg,
                v => { _strBevelP.RotationOffsetDeg = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _strBevelP.Orientation, v => _strBevelP.Orientation = v,
                () => _strBevelP.FlipFaces,   v => _strBevelP.FlipFaces = v,
                () => _strBevelP.Pivot,       v => _strBevelP.Pivot = v);
        }

        // ================================================================
        // まがりばかさ歯車
        // ================================================================

        private void BuildSpiralBevelGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("SpiralBevelGear")));
            c.Add(NF(() => _spiBevelP.MeshName, v => _spiBevelP.MeshName = v));
            c.Add(GearHint(T("SpiralBevelGearHint")));

            c.Add(SL(T("BevPair")));
            c.Add(IR(T("InvToothCount"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothCountMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothCountMax,
                () => _spiBevelP.ToothCount, v => { _spiBevelP.ToothCount = v; DM(); }));
            c.Add(IR(T("BevMatingToothCount"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothCountMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothCountMax,
                () => _spiBevelP.MatingToothCount, v => { _spiBevelP.MatingToothCount = v; DM(); }));
            c.Add(SR(T("BevShaftAngle"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ShaftAngleMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ShaftAngleMax,
                () => _spiBevelP.ShaftAngleDeg, v => { _spiBevelP.ShaftAngleDeg = v; DM(); }));

            c.Add(GearHint(T("HelHelixAngleHint")));
            c.Add(SR(T("BevSpiralAngle"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.SpiralAngleMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.SpiralAngleMax,
                () => _spiBevelP.SpiralAngleDeg, v => { _spiBevelP.SpiralAngleDeg = v; DM(); }));

            c.Add(SR(T("BevModule"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ModuleMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ModuleMax,
                () => _spiBevelP.Module, v => { _spiBevelP.Module = v; DM(); }));
            c.Add(SR(T("HelNormalPressureAngle"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.PressureAngleMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.PressureAngleMax,
                () => _spiBevelP.NormalPressureAngleDeg,
                v => { _spiBevelP.NormalPressureAngleDeg = v; DM(); }));
            c.Add(SR(T("BevFaceWidth"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.FaceWidthMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.FaceWidthMax,
                () => _spiBevelP.FaceWidth, v => { _spiBevelP.FaceWidth = v; DM(); }));

            BuildToothDepthRows(c,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothDepthCoefMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ToothDepthCoefMax,
                () => _spiBevelP.AddendumCoef, v => _spiBevelP.AddendumCoef = v,
                () => _spiBevelP.DedendumCoef, v => _spiBevelP.DedendumCoef = v);

            c.Add(SR(T("InvBacklash"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.BacklashMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.BacklashMax,
                () => _spiBevelP.Backlash, v => { _spiBevelP.Backlash = v; DM(); }));

            BuildBoreRows(c,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.BoreRadiusMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.BoreRadiusMax,
                () => _spiBevelP.BoreRadius, v => _spiBevelP.BoreRadius = v,
                () => _spiBevelP.BoreSegments, v => _spiBevelP.BoreSegments = v);

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvTrochoidSamples"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.CurveSamplesMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.CurveSamplesMax,
                () => _spiBevelP.TrochoidSamples, v => { _spiBevelP.TrochoidSamples = v; D(); }));
            c.Add(IR(T("InvInvoluteSamples"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.CurveSamplesMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.CurveSamplesMax,
                () => _spiBevelP.InvoluteSamples, v => { _spiBevelP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ArcSamplesMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ArcSamplesMax,
                () => _spiBevelP.TipArcSamples, v => { _spiBevelP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ArcSamplesMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.ArcSamplesMax,
                () => _spiBevelP.RootArcSamples, v => { _spiBevelP.RootArcSamples = v; D(); }));
            c.Add(IR(T("BevFaceSegments"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.FaceSegmentsMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.FaceSegmentsMax,
                () => _spiBevelP.FaceSegments, v => { _spiBevelP.FaceSegments = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.RotationOffsetMin,
                SpiralBevelGearMeshGenerator.SpiralBevelGearParams.RotationOffsetMax,
                () => _spiBevelP.RotationOffsetDeg,
                v => { _spiBevelP.RotationOffsetDeg = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _spiBevelP.Orientation, v => _spiBevelP.Orientation = v,
                () => _spiBevelP.FlipFaces,   v => _spiBevelP.FlipFaces = v,
                () => _spiBevelP.Pivot,       v => _spiBevelP.Pivot = v);
        }

        /// <summary>かさ歯車 2 種の派生諸元。表示する内容は同じなので 1 本にしてある。</summary>
        private void RefreshBevelInfo(BevelGearSection.BevelInfo info, float normalPressureAngleDeg)
        {
            if (!info.Valid)
            {
                SetMechInfo(T("BevInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("BevDerivedInfo",
                info.PitchConeAngleDeg.ToString("F2"),
                info.MatePitchConeAngleDeg.ToString("F2"),
                F(info.ConeDistance), F(info.InnerConeDistance),
                F(info.OuterPitchDiameter), F(info.OuterTipDiameter), F(info.OuterRootDiameter),
                info.VirtualToothCount.ToString("F2"),
                info.FormativeToothCount.ToString("F2"),
                info.TransversePressureAngleDeg.ToString("F2"),
                info.SpiralTwistDeg.ToString("F2")), true);

            var warn = new List<string>();

            if (info.BelowMinToothCount)
                warn.Add(T("BevWarnMinTeeth",
                    normalPressureAngleDeg.ToString("F1"),
                    info.FormativeToothCount.ToString("F1"),
                    info.MinToothCountApprox.ToString("F1")));

            if (info.SevereUndercut) warn.Add(T("InvWarnSevereUndercut"));
            else if (info.Undercut)  warn.Add(T("InvWarnUndercut"));

            if (info.BoreTooLarge) warn.Add(T("GearWarnBore"));

            SetMechWarn(warn);
        }

        // ================================================================
        // 円筒ウォーム
        // ================================================================

        private void BuildCylindricalWormUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("CylindricalWorm")));
            c.Add(NF(() => _wormP.MeshName, v => _wormP.MeshName = v));
            c.Add(GearHint(T("CylindricalWormHint")));

            c.Add(SR(T("WormAxialModule"),
                CylindricalWormMeshGenerator.CylindricalWormParams.ModuleMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.ModuleMax,
                () => _wormP.AxialModule, v => { _wormP.AxialModule = v; DM(); }));
            c.Add(IR(T("WormStarts"),
                CylindricalWormMeshGenerator.CylindricalWormParams.StartsMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.StartsMax,
                () => _wormP.Starts, v => { _wormP.Starts = v; DM(); }));
            c.Add(SR(T("WormDiameterFactor"),
                CylindricalWormMeshGenerator.CylindricalWormParams.DiameterFactorMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.DiameterFactorMax,
                () => _wormP.DiameterFactorQ, v => { _wormP.DiameterFactorQ = v; DM(); }));
            c.Add(SR(T("HelNormalPressureAngle"),
                CylindricalWormMeshGenerator.CylindricalWormParams.PressureAngleMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.PressureAngleMax,
                () => _wormP.NormalPressureAngleDeg,
                v => { _wormP.NormalPressureAngleDeg = v; DM(); }));
            c.Add(TR(T("WormRightHand"),
                () => _wormP.RightHand, v => { _wormP.RightHand = v; D(); }));
            c.Add(SR(T("WormLength"),
                CylindricalWormMeshGenerator.CylindricalWormParams.LengthMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.LengthMax,
                () => _wormP.Length, v => { _wormP.Length = v; DM(); }));

            BuildToothDepthRows(c,
                CylindricalWormMeshGenerator.CylindricalWormParams.ToothDepthCoefMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.ToothDepthCoefMax,
                () => _wormP.AddendumCoef, v => _wormP.AddendumCoef = v,
                () => _wormP.DedendumCoef, v => _wormP.DedendumCoef = v);

            BuildBoreRows(c,
                CylindricalWormMeshGenerator.CylindricalWormParams.BoreRadiusMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.BoreRadiusMax,
                () => _wormP.BoreRadius, v => _wormP.BoreRadius = v,
                () => _wormP.BoreSegments, v => _wormP.BoreSegments = v);

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("WormCircumferentialSegments"),
                CylindricalWormMeshGenerator.CylindricalWormParams.CircumferentialSegmentsMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.CircumferentialSegmentsMax,
                () => _wormP.CircumferentialSegments,
                v => { _wormP.CircumferentialSegments = v; D(); }));
            c.Add(IR(T("WormSamplesPerPitch"),
                CylindricalWormMeshGenerator.CylindricalWormParams.SamplesPerPitchMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.SamplesPerPitchMax,
                () => _wormP.SamplesPerPitch, v => { _wormP.SamplesPerPitch = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                CylindricalWormMeshGenerator.CylindricalWormParams.RotationOffsetMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.RotationOffsetMax,
                () => _wormP.RotationOffsetDeg, v => { _wormP.RotationOffsetDeg = v; D(); }));
            c.Add(SR(T("WormPhaseOffset"),
                CylindricalWormMeshGenerator.CylindricalWormParams.PhaseOffsetMin,
                CylindricalWormMeshGenerator.CylindricalWormParams.PhaseOffsetMax,
                () => _wormP.PhaseOffset, v => { _wormP.PhaseOffset = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _wormP.Orientation, v => _wormP.Orientation = v,
                () => _wormP.FlipFaces,   v => _wormP.FlipFaces = v,
                () => _wormP.Pivot,       v => _wormP.Pivot = v);
        }

        private void RefreshCylindricalWormInfo()
        {
            var info = CylindricalWormMeshGenerator.GetInfo(_wormP);

            if (!info.Valid)
            {
                SetMechInfo(T("WormInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("WormDerivedInfo",
                F(info.PitchDiameter), F(info.TipDiameter), F(info.RootDiameter),
                F(info.AxialPitch), F(info.Lead),
                info.LeadAngleDeg.ToString("F2"),
                info.AxialPressureAngleDeg.ToString("F2"),
                info.ThreadTurns.ToString("F2")), true);

            var warn = new List<string>();
            if (info.BoreTooLarge) warn.Add(T("GearWarnBore"));
            SetMechWarn(warn);
        }

        // ================================================================
        // ウォームホイール
        // ================================================================

        private void BuildWormWheelUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("WormWheel")));
            c.Add(NF(() => _wheelP.MeshName, v => _wheelP.MeshName = v));
            c.Add(GearHint(T("WormWheelHint")));

            c.Add(SL(T("WormPair")));
            c.Add(SR(T("WormAxialModule"),
                WormWheelMeshGenerator.WormWheelParams.ModuleMin,
                WormWheelMeshGenerator.WormWheelParams.ModuleMax,
                () => _wheelP.AxialModule, v => { _wheelP.AxialModule = v; DM(); }));
            c.Add(IR(T("WormStarts"),
                WormWheelMeshGenerator.WormWheelParams.StartsMin,
                WormWheelMeshGenerator.WormWheelParams.StartsMax,
                () => _wheelP.WormStarts, v => { _wheelP.WormStarts = v; DM(); }));
            c.Add(SR(T("WormDiameterFactor"),
                WormWheelMeshGenerator.WormWheelParams.DiameterFactorMin,
                WormWheelMeshGenerator.WormWheelParams.DiameterFactorMax,
                () => _wheelP.WormDiameterFactorQ,
                v => { _wheelP.WormDiameterFactorQ = v; DM(); }));
            c.Add(TR(T("WormRightHand"),
                () => _wheelP.RightHandWorm, v => { _wheelP.RightHandWorm = v; DM(); }));

            c.Add(SL(T("WormWheel")));
            c.Add(IR(T("InvToothCount"),
                WormWheelMeshGenerator.WormWheelParams.ToothCountMin,
                WormWheelMeshGenerator.WormWheelParams.ToothCountMax,
                () => _wheelP.ToothCount, v => { _wheelP.ToothCount = v; DM(); }));
            c.Add(SR(T("HelNormalPressureAngle"),
                WormWheelMeshGenerator.WormWheelParams.PressureAngleMin,
                WormWheelMeshGenerator.WormWheelParams.PressureAngleMax,
                () => _wheelP.NormalPressureAngleDeg,
                v => { _wheelP.NormalPressureAngleDeg = v; DM(); }));
            c.Add(SR(T("WhlFaceWidth"),
                WormWheelMeshGenerator.WormWheelParams.FaceWidthMin,
                WormWheelMeshGenerator.WormWheelParams.FaceWidthMax,
                () => _wheelP.FaceWidth, v => { _wheelP.FaceWidth = v; DM(); }));

            BuildToothDepthRows(c,
                WormWheelMeshGenerator.WormWheelParams.ToothDepthCoefMin,
                WormWheelMeshGenerator.WormWheelParams.ToothDepthCoefMax,
                () => _wheelP.AddendumCoef, v => _wheelP.AddendumCoef = v,
                () => _wheelP.DedendumCoef, v => _wheelP.DedendumCoef = v);

            c.Add(SL(T("InvCorrection")));
            c.Add(GearHint(T("InvCorrectionHint")));
            c.Add(SR(T("InvProfileShift"),
                WormWheelMeshGenerator.WormWheelParams.ProfileShiftMin,
                WormWheelMeshGenerator.WormWheelParams.ProfileShiftMax,
                () => _wheelP.ProfileShift, v => { _wheelP.ProfileShift = v; DM(); }));
            c.Add(SR(T("InvBacklash"),
                WormWheelMeshGenerator.WormWheelParams.BacklashMin,
                WormWheelMeshGenerator.WormWheelParams.BacklashMax,
                () => _wheelP.Backlash, v => { _wheelP.Backlash = v; DM(); }));

            BuildBoreRows(c,
                WormWheelMeshGenerator.WormWheelParams.BoreRadiusMin,
                WormWheelMeshGenerator.WormWheelParams.BoreRadiusMax,
                () => _wheelP.BoreRadius, v => _wheelP.BoreRadius = v,
                () => _wheelP.BoreSegments, v => _wheelP.BoreSegments = v);

            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvTrochoidSamples"),
                WormWheelMeshGenerator.WormWheelParams.CurveSamplesMin,
                WormWheelMeshGenerator.WormWheelParams.CurveSamplesMax,
                () => _wheelP.TrochoidSamples, v => { _wheelP.TrochoidSamples = v; D(); }));
            c.Add(IR(T("InvInvoluteSamples"),
                WormWheelMeshGenerator.WormWheelParams.CurveSamplesMin,
                WormWheelMeshGenerator.WormWheelParams.CurveSamplesMax,
                () => _wheelP.InvoluteSamples, v => { _wheelP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"),
                WormWheelMeshGenerator.WormWheelParams.ArcSamplesMin,
                WormWheelMeshGenerator.WormWheelParams.ArcSamplesMax,
                () => _wheelP.TipArcSamples, v => { _wheelP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"),
                WormWheelMeshGenerator.WormWheelParams.ArcSamplesMin,
                WormWheelMeshGenerator.WormWheelParams.ArcSamplesMax,
                () => _wheelP.RootArcSamples, v => { _wheelP.RootArcSamples = v; D(); }));
            c.Add(IR(T("WhlFaceSegments"),
                WormWheelMeshGenerator.WormWheelParams.FaceSegmentsMin,
                WormWheelMeshGenerator.WormWheelParams.FaceSegmentsMax,
                () => _wheelP.FaceSegments, v => { _wheelP.FaceSegments = v; D(); }));

            c.Add(SR(T("GearRotationOffset"),
                WormWheelMeshGenerator.WormWheelParams.RotationOffsetMin,
                WormWheelMeshGenerator.WormWheelParams.RotationOffsetMax,
                () => _wheelP.RotationOffsetDeg, v => { _wheelP.RotationOffsetDeg = v; D(); }));

            BuildMechInfo(c, "InvDerived");

            BuildMechFooter(c,
                () => _wheelP.Orientation, v => _wheelP.Orientation = v,
                () => _wheelP.FlipFaces,   v => _wheelP.FlipFaces = v,
                () => _wheelP.Pivot,       v => _wheelP.Pivot = v);
        }

        private void RefreshWormWheelInfo()
        {
            var info = WormWheelMeshGenerator.GetInfo(_wheelP);

            if (!info.Valid)
            {
                SetMechInfo(T("WhlInvalid"), false);
                SetMechWarn(null);
                return;
            }

            SetMechInfo(T("WhlDerivedInfo",
                F(info.PitchDiameter), F(info.BaseDiameter),
                F(info.TipDiameter), F(info.RootDiameter),
                F(info.WormPitchDiameter), F(info.CenterDistance),
                F(info.ThroatSurfaceRadius), F(info.RimRadiusAtFaceEdge),
                info.GearRatio.ToString("F2"),
                info.LeadAngleDeg.ToString("F2"),
                info.AxialPressureAngleDeg.ToString("F2"),
                info.TotalTwistDeg.ToString("F2")), true);

            var warn = new List<string>();

            if (info.SevereUndercut) warn.Add(T("InvWarnSevereUndercut"));
            else if (info.Undercut)  warn.Add(T("InvWarnUndercut"));

            if (info.BoreTooLarge) warn.Add(T("GearWarnBore"));

            SetMechWarn(warn);
        }

        // ================================================================
        // 派生諸元の振り分け
        // ================================================================

        /// <summary>
        /// 現在の機構部品の派生諸元を書き直す。
        /// 機構部品以外を選んでいるときは何もしない（ラベルが他図形のものになっている）。
        /// </summary>
        private void RefreshMechInfo()
        {
            if (_mechInfoLabel == null) return;

            switch (_current)
            {
                case ShapeKind.HelicalGear:       RefreshHelicalGearInfo();       break;
                case ShapeKind.InternalGear:      RefreshInternalGearInfo();      break;
                case ShapeKind.InvoluteRack:      RefreshInvoluteRackInfo();      break;
                case ShapeKind.HelicalRack:       RefreshHelicalRackInfo();       break;
                case ShapeKind.CylindricalWorm:   RefreshCylindricalWormInfo();   break;
                case ShapeKind.WormWheel:         RefreshWormWheelInfo();         break;

                case ShapeKind.StraightBevelGear:
                    RefreshBevelInfo(
                        StraightBevelGearMeshGenerator.GetInfo(_strBevelP),
                        _strBevelP.PressureAngleDeg);
                    break;

                case ShapeKind.SpiralBevelGear:
                    RefreshBevelInfo(
                        SpiralBevelGearMeshGenerator.GetInfo(_spiBevelP),
                        _spiBevelP.NormalPressureAngleDeg);
                    break;
            }
        }
    }
}
