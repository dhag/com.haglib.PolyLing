// PlayerPrimitiveMeshSubPanel.Gear.cs
// 図形生成サブパネル：簡易歯車 / スタア / インボリュート歯車（高度な図形）。
// いずれも XY 平面の閉じた輪郭を押し出した板で、中心に丸穴を開けられる。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private NGonGearMeshGenerator.NGonGearParams _ngonGearP = NGonGearMeshGenerator.NGonGearParams.Default;
        private NGonStarMeshGenerator.NGonStarParams _ngonStarP = NGonStarMeshGenerator.NGonStarParams.Default;
        private InvoluteTrochoidGearMeshGenerator.InvoluteGearParams _involGearP =
            InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.Default;

        /// <summary>簡易歯車の θS（自動計算）表示ラベル。</summary>
        private Label _ngonGearThetaSLabel;

        /// <summary>インボリュート歯車の派生諸元・警告ラベル。</summary>
        private Label _involInfoLabel;
        private Label _involWarnLabel;

        // ================================================================
        // 簡易歯車
        // ================================================================

        private void BuildNGonGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("NGonGear")));
            c.Add(NF(() => _ngonGearP.MeshName, v => _ngonGearP.MeshName = v));

            c.Add(GearHint(T("NGonGearHint")));

            c.Add(IR(T("GearToothCount"), NGonGearMeshGenerator.NGonGearParams.ToothCountMin, NGonGearMeshGenerator.NGonGearParams.ToothCountMax,
                () => _ngonGearP.ToothCount, v => { _ngonGearP.ToothCount = v; D(); RefreshNGonGearThetaS(); }));
            c.Add(SR(T("GearInnerRadius"), NGonGearMeshGenerator.NGonGearParams.InnerRadiusMin, NGonGearMeshGenerator.NGonGearParams.InnerRadiusMax,
                () => _ngonGearP.InnerRadius, v => { _ngonGearP.InnerRadius = v; D(); }));
            c.Add(SR(T("GearOuterRadius"), NGonGearMeshGenerator.NGonGearParams.OuterRadiusMin, NGonGearMeshGenerator.NGonGearParams.OuterRadiusMax,
                () => _ngonGearP.OuterRadius, v => { _ngonGearP.OuterRadius = v; D(); }));
            c.Add(SR(T("Thickness"), NGonGearMeshGenerator.NGonGearParams.ThicknessMin, NGonGearMeshGenerator.NGonGearParams.ThicknessMax,
                () => _ngonGearP.Thickness, v => { _ngonGearP.Thickness = v; D(); }));

            // ── 歯の角度 ──
            c.Add(SL(T("GearAngles")));
            c.Add(SR(T("GearThetaL"), NGonGearMeshGenerator.NGonGearParams.ThetaLMin, NGonGearMeshGenerator.NGonGearParams.ThetaLMax,
                () => _ngonGearP.ThetaL, v => { _ngonGearP.ThetaL = v; D(); RefreshNGonGearThetaS(); }));
            c.Add(SR(T("GearThetaM"), NGonGearMeshGenerator.NGonGearParams.ThetaMMin, NGonGearMeshGenerator.NGonGearParams.ThetaMMax,
                () => _ngonGearP.ThetaM, v => { _ngonGearP.ThetaM = v; D(); RefreshNGonGearThetaS(); }));

            _ngonGearThetaSLabel = GearHint(string.Empty);
            c.Add(_ngonGearThetaSLabel);
            RefreshNGonGearThetaS();

            c.Add(SR(T("GearRotationOffset"), NGonGearMeshGenerator.NGonGearParams.RotationOffsetMin, NGonGearMeshGenerator.NGonGearParams.RotationOffsetMax,
                () => _ngonGearP.RotationOffset, v => { _ngonGearP.RotationOffset = v; D(); }));

            // ── 穴 ──
            c.Add(SL(T("GearBore")));
            c.Add(GearHint(T("GearBoreHint")));
            c.Add(SR(T("GearBoreRadius"), NGonGearMeshGenerator.NGonGearParams.BoreRadiusMin, NGonGearMeshGenerator.NGonGearParams.BoreRadiusMax,
                () => _ngonGearP.BoreRadius, v => { _ngonGearP.BoreRadius = v; D(); }));
            c.Add(IR(T("GearBoreSegments"), GearDiskBuilder.BoreSegmentsMin, GearDiskBuilder.BoreSegmentsMax,
                () => _ngonGearP.BoreSegments, v => { _ngonGearP.BoreSegments = v; D(); }));

            // ── 配置面 / 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(OrientationDD(
                () => _ngonGearP.Orientation, v => { _ngonGearP.Orientation = v; D(); }));
            c.Add(TR(T("FlipFaces"),
                () => _ngonGearP.FlipFaces, v => { _ngonGearP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _ngonGearP.Pivot, v => { _ngonGearP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>θS は 1 歯ぶんの角度から θL + 2θM を引いた残り。値を表示だけする。</summary>
        private void RefreshNGonGearThetaS()
        {
            if (_ngonGearThetaSLabel == null) return;

            float per = NGonGearMeshGenerator.AnglePerTooth(_ngonGearP);
            float s = NGonGearMeshGenerator.ThetaS(_ngonGearP);

            _ngonGearThetaSLabel.text = T("GearThetaSInfo", s.ToString("F2"), per.ToString("F2"));
            _ngonGearThetaSLabel.style.color = s <= 0.001f
                ? new StyleColor(new Color(1f, 0.6f, 0.4f))
                : new StyleColor(new Color(0.75f, 0.75f, 0.75f));
        }

        private MeshObject GenerateNGonGearMesh()
            => NGonGearMeshGenerator.Generate(_ngonGearP);

        // ================================================================
        // スタア
        // ================================================================

        private void BuildNGonStarUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("NGonStar")));
            c.Add(NF(() => _ngonStarP.MeshName, v => _ngonStarP.MeshName = v));

            c.Add(GearHint(T("NGonStarHint")));

            c.Add(IR(T("StarPoints"), NGonStarMeshGenerator.NGonStarParams.PointsMin, NGonStarMeshGenerator.NGonStarParams.PointsMax,
                () => _ngonStarP.Points, v => { _ngonStarP.Points = v; D(); }));
            c.Add(SR(T("StarInnerRadius"), NGonStarMeshGenerator.NGonStarParams.InnerRadiusMin, NGonStarMeshGenerator.NGonStarParams.InnerRadiusMax,
                () => _ngonStarP.InnerRadius, v => { _ngonStarP.InnerRadius = v; D(); }));
            c.Add(SR(T("StarOuterRadius"), NGonStarMeshGenerator.NGonStarParams.OuterRadiusMin, NGonStarMeshGenerator.NGonStarParams.OuterRadiusMax,
                () => _ngonStarP.OuterRadius, v => { _ngonStarP.OuterRadius = v; D(); }));
            c.Add(SR(T("Thickness"), NGonStarMeshGenerator.NGonStarParams.ThicknessMin, NGonStarMeshGenerator.NGonStarParams.ThicknessMax,
                () => _ngonStarP.Thickness, v => { _ngonStarP.Thickness = v; D(); }));
            c.Add(SR(T("GearRotationOffset"), NGonStarMeshGenerator.NGonStarParams.RotationOffsetMin, NGonStarMeshGenerator.NGonStarParams.RotationOffsetMax,
                () => _ngonStarP.RotationOffset, v => { _ngonStarP.RotationOffset = v; D(); }));

            // ── 穴 ──
            c.Add(SL(T("GearBore")));
            c.Add(GearHint(T("GearBoreHint")));
            c.Add(SR(T("GearBoreRadius"), NGonStarMeshGenerator.NGonStarParams.BoreRadiusMin, NGonStarMeshGenerator.NGonStarParams.BoreRadiusMax,
                () => _ngonStarP.BoreRadius, v => { _ngonStarP.BoreRadius = v; D(); }));
            c.Add(IR(T("GearBoreSegments"), GearDiskBuilder.BoreSegmentsMin, GearDiskBuilder.BoreSegmentsMax,
                () => _ngonStarP.BoreSegments, v => { _ngonStarP.BoreSegments = v; D(); }));

            // ── 配置面 / 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(OrientationDD(
                () => _ngonStarP.Orientation, v => { _ngonStarP.Orientation = v; D(); }));
            c.Add(TR(T("FlipFaces"),
                () => _ngonStarP.FlipFaces, v => { _ngonStarP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _ngonStarP.Pivot, v => { _ngonStarP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        private MeshObject GenerateNGonStarMesh()
            => NGonStarMeshGenerator.Generate(_ngonStarP);

        // ================================================================
        // インボリュート歯車
        // ================================================================

        private void BuildInvoluteGearUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("InvoluteGear")));
            c.Add(NF(() => _involGearP.MeshName, v => _involGearP.MeshName = v));

            c.Add(GearHint(T("InvoluteGearHint")));

            c.Add(IR(T("InvToothCount"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothCountMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothCountMax,
                () => _involGearP.ToothCount, v => { _involGearP.ToothCount = v; DI(); }));
            c.Add(SR(T("InvModule"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ModuleMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ModuleMax,
                () => _involGearP.Module, v => { _involGearP.Module = v; DI(); }));
            c.Add(SR(T("InvPressureAngle"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.PressureAngleMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.PressureAngleMax,
                () => _involGearP.PressureAngleDeg, v => { _involGearP.PressureAngleDeg = v; DI(); }));
            c.Add(SR(T("Thickness"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ThicknessMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ThicknessMax,
                () => _involGearP.Thickness, v => { _involGearP.Thickness = v; DI(); }));

            // ── 歯たけ ──
            c.Add(SL(T("GearToothDepth")));
            c.Add(GearHint(T("GearToothDepthHint")));
            c.Add(SR(T("GearAddendumCoef"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothDepthCoefMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothDepthCoefMax,
                () => _involGearP.AddendumCoef, v => { _involGearP.AddendumCoef = v; DI(); }));
            c.Add(SR(T("GearDedendumCoef"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothDepthCoefMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ToothDepthCoefMax,
                () => _involGearP.DedendumCoef, v => { _involGearP.DedendumCoef = v; DI(); }));

            // ── 転位・バックラッシ ──
            c.Add(SL(T("InvCorrection")));
            c.Add(GearHint(T("InvCorrectionHint")));
            c.Add(SR(T("InvProfileShift"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ProfileShiftMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ProfileShiftMax,
                () => _involGearP.ProfileShift, v => { _involGearP.ProfileShift = v; DI(); }));
            c.Add(SR(T("InvBacklash"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.BacklashMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.BacklashMax,
                () => _involGearP.Backlash, v => { _involGearP.Backlash = v; DI(); }));

            // ── 穴 ──
            c.Add(SL(T("GearBore")));
            c.Add(GearHint(T("GearBoreHint")));
            c.Add(SR(T("GearBoreRadius"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.BoreRadiusMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.BoreRadiusMax,
                () => _involGearP.BoreRadius, v => { _involGearP.BoreRadius = v; DI(); }));
            c.Add(IR(T("GearBoreSegments"), GearDiskBuilder.BoreSegmentsMin, GearDiskBuilder.BoreSegmentsMax,
                () => _involGearP.BoreSegments, v => { _involGearP.BoreSegments = v; D(); }));

            // ── 曲線の分割数 ──
            c.Add(SL(T("InvSampling")));
            c.Add(IR(T("InvTrochoidSamples"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.CurveSamplesMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.CurveSamplesMax,
                () => _involGearP.TrochoidSamples, v => { _involGearP.TrochoidSamples = v; D(); }));
            c.Add(IR(T("InvInvoluteSamples"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.CurveSamplesMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.CurveSamplesMax,
                () => _involGearP.InvoluteSamples, v => { _involGearP.InvoluteSamples = v; D(); }));
            c.Add(IR(T("InvTipArcSamples"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ArcSamplesMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ArcSamplesMax,
                () => _involGearP.TipArcSamples, v => { _involGearP.TipArcSamples = v; D(); }));
            c.Add(IR(T("InvRootArcSamples"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ArcSamplesMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.ArcSamplesMax,
                () => _involGearP.RootArcSamples, v => { _involGearP.RootArcSamples = v; D(); }));

            c.Add(SR(T("GearRotationOffset"), InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.RotationOffsetMin, InvoluteTrochoidGearMeshGenerator.InvoluteGearParams.RotationOffsetMax,
                () => _involGearP.RotationOffsetDeg, v => { _involGearP.RotationOffsetDeg = v; D(); }));

            // ── 派生諸元 ──
            c.Add(SL(T("InvDerived")));
            _involInfoLabel = GearHint(string.Empty);
            c.Add(_involInfoLabel);
            _involWarnLabel = GearHint(string.Empty);
            _involWarnLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.4f));
            c.Add(_involWarnLabel);
            RefreshInvoluteInfo();

            // ── 配置面 / 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(OrientationDD(
                () => _involGearP.Orientation, v => { _involGearP.Orientation = v; D(); }));
            c.Add(TR(T("FlipFaces"),
                () => _involGearP.FlipFaces, v => { _involGearP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _involGearP.Pivot, v => { _involGearP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>諸元表示を伴う D()。歯数・モジュール等を変えたときに使う。</summary>
        private void DI()
        {
            D();
            RefreshInvoluteInfo();
        }

        /// <summary>派生諸元と警告を書き直す。</summary>
        private void RefreshInvoluteInfo()
        {
            if (_involInfoLabel == null) return;

            var info = InvoluteTrochoidGearMeshGenerator.GetInfo(_involGearP);

            if (!info.Valid)
            {
                _involInfoLabel.text = T("InvInvalid");
                _involInfoLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.4f));
                if (_involWarnLabel != null) _involWarnLabel.text = string.Empty;
                return;
            }

            _involInfoLabel.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            _involInfoLabel.text = T("InvDerivedInfo",
                info.PitchDiameter.ToString("F4"),
                info.BaseDiameter.ToString("F4"),
                info.TipDiameter.ToString("F4"),
                info.RootDiameter.ToString("F4"),
                info.CircularPitch.ToString("F4"),
                info.ToothThicknessPitch.ToString("F4"),
                info.JoinRadius.ToString("F4"));

            if (_involWarnLabel == null) return;

            var warn = new List<string>();

            if (info.BelowMinToothCount)
                warn.Add(T("InvWarnMinTeeth",
                    _involGearP.PressureAngleDeg.ToString("F1"),
                    _involGearP.ToothCount.ToString(),
                    info.MinToothCountApprox.ToString("F1")));

            if (info.SevereUndercut) warn.Add(T("InvWarnSevereUndercut"));
            else if (info.Undercut)  warn.Add(T("InvWarnUndercut"));

            if (info.BoreTooLarge) warn.Add(T("InvWarnBore"));

            _involWarnLabel.text = warn.Count > 0 ? string.Join("\n", warn) : string.Empty;
        }

        private MeshObject GenerateInvoluteGearMesh()
            => InvoluteTrochoidGearMeshGenerator.Generate(_involGearP);

        // ================================================================
        // 共有 UI 部品
        // ================================================================

        /// <summary>説明文の小さいラベル。</summary>
        private static Label GearHint(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            return l;
        }

        /// <summary>板を置く平面のドロップダウン（平面と同じ XY / XZ / YZ）。</summary>
        private VisualElement OrientationDD(
            System.Func<PlaneOrientation> get, System.Action<PlaneOrientation> set)
        {
            var dd = new DropdownField(
                new List<string> { T("PlaneXY"), T("PlaneXZ"), T("PlaneYZ") }, (int)get());
            dd.label = T("Orientation");
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(_ => set((PlaneOrientation)dd.index));
            return dd;
        }
    }
}
