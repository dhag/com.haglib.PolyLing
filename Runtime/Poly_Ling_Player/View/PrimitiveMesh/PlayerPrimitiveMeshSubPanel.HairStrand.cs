// PlayerPrimitiveMeshSubPanel.HairStrand.cs
// 図形生成サブパネル：髪の房（高度な図形）。
// 房 M 個 × 筒 N 本 の独立したチューブを作る。
//
// 【土台による行の出し分け】
//   軸方向の量（開始位置・進み・房の間隔）は、円筒では長さ、球では赤道からの仰角の度数で、
//   同じフィールドでも値域が違う。行ヘルパは生成時に値域を固定するので、
//   長さ用と度数用の行を両方作り、display の切り替えで見せ分ける。
//   円筒の軸の行は球のとき隠す（球の極は +Y 固定）。
//
// 【幅配分】
//   筒の本数を変えると等分で作り直す。個別に触った後でも「等分」で戻せる。
//   行は本数で変わるので専用のコンテナへ入れ、本数の変更時にそこだけ作り直す。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.HairStrand;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private HairStrandParams _hairP = HairStrandParams.Default;

        /// <summary>円筒のときだけ出す行。</summary>
        private readonly List<VisualElement> _hairCylinderRows = new List<VisualElement>();
        /// <summary>球のときだけ出す行。</summary>
        private readonly List<VisualElement> _hairSphereRows = new List<VisualElement>();

        /// <summary>幅配分の行を入れるコンテナ。筒の本数が変わるたびに作り直す。</summary>
        private VisualElement _hairLobeWidthBox;

        // ================================================================
        // UI
        // ================================================================

        private void BuildHairStrandUI(VisualElement c)
        {
            _hairCylinderRows.Clear();
            _hairSphereRows.Clear();

            c.Add(ShapeTitle(T("HairStrand")));
            c.Add(NF(() => _hairP.MeshName, v => _hairP.MeshName = v));
            c.Add(GearHint(T("HairStrandHint")));

            // ── 土台 ──
            c.Add(SL(T("HairBase")));
            c.Add(HairBaseTypeDD());
            AddHairCylinderRow(c, HairAxisDD());

            c.Add(SR(T("HairRadius"),
                HairStrandParams.RadiusMin, HairStrandParams.RadiusMax,
                () => _hairP.Radius, v => { _hairP.Radius = v; D(); }));

            AddHairCylinderRow(c, SR(T("HairStartAxial"),
                HairStrandParams.AxialLenMin, HairStrandParams.AxialLenMax,
                () => _hairP.StartAxial, v => { _hairP.StartAxial = v; D(); }));
            AddHairSphereRow(c, SR(T("HairStartElev"),
                HairStrandParams.AxialDegMin, HairStrandParams.AxialDegMax,
                () => _hairP.StartAxial, v => { _hairP.StartAxial = v; D(); }));

            c.Add(SR(T("HairStartAngle"),
                HairStrandParams.AngleMin, HairStrandParams.AngleMax,
                () => _hairP.StartAngle, v => { _hairP.StartAngle = v; D(); }));

            AddHairCylinderRow(c, SR(T("HairSpanAxial"),
                HairStrandParams.AxialLenMin, HairStrandParams.AxialLenMax,
                () => _hairP.SpanAxial, v => { _hairP.SpanAxial = v; D(); }));
            AddHairSphereRow(c, SR(T("HairSpanElev"),
                HairStrandParams.AxialDegMin, HairStrandParams.AxialDegMax,
                () => _hairP.SpanAxial, v => { _hairP.SpanAxial = v; D(); }));

            c.Add(SR(T("HairSpanAngle"),
                HairStrandParams.AngleMin, HairStrandParams.AngleMax,
                () => _hairP.SpanAngle, v => { _hairP.SpanAngle = v; D(); }));

            c.Add(SR(T("HairLift"),
                HairStrandParams.LiftMin, HairStrandParams.LiftMax,
                () => _hairP.Lift, v => { _hairP.Lift = v; D(); }));

            // ── 房の並び ──
            c.Add(SL(T("HairStrandLayout")));
            c.Add(GearHint(T("HairStrandLayoutHint")));

            c.Add(IR(T("HairStrandCount"),
                HairStrandParams.StrandCountMin, HairStrandParams.StrandCountMax,
                () => _hairP.StrandCount, v => { _hairP.StrandCount = v; D(); }));

            AddHairCylinderRow(c, SR(T("HairPitchAxial"),
                HairStrandParams.AxialLenMin, HairStrandParams.AxialLenMax,
                () => _hairP.PitchAxial, v => { _hairP.PitchAxial = v; D(); }));
            AddHairSphereRow(c, SR(T("HairPitchElev"),
                HairStrandParams.AxialDegMin, HairStrandParams.AxialDegMax,
                () => _hairP.PitchAxial, v => { _hairP.PitchAxial = v; D(); }));

            c.Add(SR(T("HairPitchAngle"),
                HairStrandParams.AngleMin, HairStrandParams.AngleMax,
                () => _hairP.PitchAngle, v => { _hairP.PitchAngle = v; D(); }));

            // ── 筒の分割 ──
            c.Add(SL(T("HairLobe")));
            c.Add(GearHint(T("HairLobeHint")));

            c.Add(IR(T("HairLobeCount"),
                HairStrandParams.LobeCountMin, HairStrandParams.LobeCountMax,
                () => _hairP.LobeCount,
                v =>
                {
                    _hairP.LobeCount  = v;
                    _hairP.LobeWidths = HairStrandParams.EqualLobeWidths(v);
                    RebuildHairLobeWidthRows();
                    D();
                }));

            _hairLobeWidthBox = new VisualElement();
            c.Add(_hairLobeWidthBox);

            var equalRow = new VisualElement();
            equalRow.style.flexDirection = FlexDirection.Row;
            equalRow.style.marginBottom  = 3;
            SB(equalRow, T("HairLobeEqualize"), () =>
            {
                _hairP.LobeWidths = HairStrandParams.EqualLobeWidths(_hairP.LobeCount);
                RebuildHairLobeWidthRows();
                D();
            });
            c.Add(equalRow);

            RebuildHairLobeWidthRows();

            c.Add(IR(T("HairLengthSegments"),
                HairStrandParams.LengthSegmentsMin, HairStrandParams.LengthSegmentsMax,
                () => _hairP.LengthSegments, v => { _hairP.LengthSegments = v; D(); }));
            c.Add(IR(T("HairSectionSegments"),
                HairStrandParams.SectionSegmentsMin, HairStrandParams.SectionSegmentsMax,
                () => _hairP.SectionSegments, v => { _hairP.SectionSegments = v; D(); }));

            // ── 幅 ──
            c.Add(SL(T("HairWidth")));
            c.Add(GearHint(T("HairWidthHint")));

            c.Add(SR(T("HairWidthRoot"),
                HairStrandParams.WidthRootMin, HairStrandParams.WidthMax,
                () => _hairP.WidthRoot, v => { _hairP.WidthRoot = v; D(); }));
            c.Add(SR(T("HairWidthMid"),
                HairStrandParams.WidthMin, HairStrandParams.WidthMax,
                () => _hairP.WidthMid, v => { _hairP.WidthMid = v; D(); }));
            c.Add(SR(T("HairWidthTip"),
                HairStrandParams.WidthMin, HairStrandParams.WidthMax,
                () => _hairP.WidthTip, v => { _hairP.WidthTip = v; D(); }));
            c.Add(SR(T("HairWidthMidT"),
                HairStrandParams.MidTMin, HairStrandParams.MidTMax,
                () => _hairP.WidthMidT, v => { _hairP.WidthMidT = v; D(); }));
            c.Add(SR(T("HairWidthPowRoot"),
                HairStrandParams.PowMin, HairStrandParams.PowMax,
                () => _hairP.WidthPowRoot, v => { _hairP.WidthPowRoot = v; D(); }));
            c.Add(SR(T("HairWidthPowTip"),
                HairStrandParams.PowMin, HairStrandParams.PowMax,
                () => _hairP.WidthPowTip, v => { _hairP.WidthPowTip = v; D(); }));

            // ── 厚み ──
            c.Add(SL(T("HairThick")));

            c.Add(SR(T("HairThickRoot"),
                HairStrandParams.ThickRootMin, HairStrandParams.ThickMax,
                () => _hairP.ThickRoot, v => { _hairP.ThickRoot = v; D(); }));
            c.Add(SR(T("HairThickMid"),
                HairStrandParams.ThickMin, HairStrandParams.ThickMax,
                () => _hairP.ThickMid, v => { _hairP.ThickMid = v; D(); }));
            c.Add(SR(T("HairThickTip"),
                HairStrandParams.ThickMin, HairStrandParams.ThickMax,
                () => _hairP.ThickTip, v => { _hairP.ThickTip = v; D(); }));
            c.Add(SR(T("HairThickMidT"),
                HairStrandParams.MidTMin, HairStrandParams.MidTMax,
                () => _hairP.ThickMidT, v => { _hairP.ThickMidT = v; D(); }));
            c.Add(SR(T("HairThickPowRoot"),
                HairStrandParams.PowMin, HairStrandParams.PowMax,
                () => _hairP.ThickPowRoot, v => { _hairP.ThickPowRoot = v; D(); }));
            c.Add(SR(T("HairThickPowTip"),
                HairStrandParams.PowMin, HairStrandParams.PowMax,
                () => _hairP.ThickPowTip, v => { _hairP.ThickPowTip = v; D(); }));

            // ── 断面 ──
            c.Add(SL(T("HairSection")));
            c.Add(GearHint(T("HairSectionHint")));

            c.Add(SR(T("HairSectionPower"),
                HairStrandParams.SectionPowerMin, HairStrandParams.SectionPowerMax,
                () => _hairP.SectionPower, v => { _hairP.SectionPower = v; D(); }));
            c.Add(SR(T("HairInnerRatio"),
                HairStrandParams.InnerRatioMin, HairStrandParams.InnerRatioMax,
                () => _hairP.InnerRatio, v => { _hairP.InnerRatio = v; D(); }));
            c.Add(SR(T("HairTwist"),
                HairStrandParams.TwistMin, HairStrandParams.TwistMax,
                () => _hairP.Twist, v => { _hairP.Twist = v; D(); }));

            // ── 房ごとの変化 ──
            c.Add(SL(T("HairSlope")));
            c.Add(GearHint(T("HairSlopeHint")));

            c.Add(HairSlopeModeDD());
            c.Add(SR(T("HairLenSlope"),
                HairStrandParams.SlopeMin, HairStrandParams.SlopeMax,
                () => _hairP.LenSlope, v => { _hairP.LenSlope = v; D(); }));
            c.Add(SR(T("HairWidthSlope"),
                HairStrandParams.SlopeMin, HairStrandParams.SlopeMax,
                () => _hairP.WidthSlope, v => { _hairP.WidthSlope = v; D(); }));
            c.Add(SR(T("HairThickSlope"),
                HairStrandParams.SlopeMin, HairStrandParams.SlopeMax,
                () => _hairP.ThickSlope, v => { _hairP.ThickSlope = v; D(); }));
            c.Add(SR(T("HairLiftSlope"),
                HairStrandParams.SlopeMin, HairStrandParams.SlopeMax,
                () => _hairP.LiftSlope, v => { _hairP.LiftSlope = v; D(); }));
            c.Add(SR(T("HairTwistSlope"),
                HairStrandParams.SlopeMin, HairStrandParams.SlopeMax,
                () => _hairP.TwistSlope, v => { _hairP.TwistSlope = v; D(); }));

            // ── 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FlipFaces"),
                () => _hairP.FlipFaces, v => { _hairP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _hairP.Pivot, v => { _hairP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

            RefreshHairBaseMode();
        }

        // ================================================================
        // 行の出し分け
        // ================================================================

        /// <summary>円筒のときだけ出す行を足す。</summary>
        private void AddHairCylinderRow(VisualElement c, VisualElement row)
        {
            c.Add(row);
            _hairCylinderRows.Add(row);
        }

        /// <summary>球のときだけ出す行を足す。</summary>
        private void AddHairSphereRow(VisualElement c, VisualElement row)
        {
            c.Add(row);
            _hairSphereRows.Add(row);
        }

        /// <summary>土台の種類に合わせて行の表示を切り替える。</summary>
        private void RefreshHairBaseMode()
        {
            bool cyl = _hairP.BaseType == HairBaseType.Cylinder;
            foreach (var r in _hairCylinderRows)
                if (r != null) r.style.display = cyl ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var r in _hairSphereRows)
                if (r != null) r.style.display = cyl ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ================================================================
        // 幅配分
        // ================================================================

        /// <summary>幅配分の行を今の本数で作り直す。</summary>
        private void RebuildHairLobeWidthRows()
        {
            if (_hairLobeWidthBox == null) return;

            _hairLobeWidthBox.Clear();

            int n = Mathf.Clamp(_hairP.LobeCount,
                HairStrandParams.LobeCountMin, HairStrandParams.LobeCountMax);

            if (_hairP.LobeWidths == null || _hairP.LobeWidths.Length != n)
                _hairP.LobeWidths = HairStrandParams.EqualLobeWidths(n);

            for (int i = 0; i < n; i++)
            {
                int idx = i;   // ラムダへ渡すために控える
                _hairLobeWidthBox.Add(SR(T("HairLobeWidth", idx + 1),
                    HairStrandParams.LobeWidthMin, HairStrandParams.LobeWidthMax,
                    () => _hairP.LobeWidths[idx],
                    v => { _hairP.LobeWidths[idx] = v; D(); }));
            }
        }

        // ================================================================
        // ドロップダウン
        // ================================================================

        /// <summary>土台の種類（球 / 円筒）。</summary>
        private VisualElement HairBaseTypeDD()
        {
            var dd = new DropdownField(
                new List<string> { T("HairBaseSphere"), T("HairBaseCylinder") },
                (int)_hairP.BaseType);
            dd.label = T("HairBaseType");
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(_ =>
            {
                _hairP.BaseType = (HairBaseType)dd.index;
                RefreshHairBaseMode();
                D();
            });
            return dd;
        }

        /// <summary>円筒の軸（X / Y / Z）。</summary>
        private VisualElement HairAxisDD()
        {
            var dd = new DropdownField(
                new List<string> { "X", "Y", "Z" }, (int)_hairP.Axis);
            dd.label = T("HairBaseAxis");
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(_ => { _hairP.Axis = (HairBaseAxis)dd.index; D(); });
            return dd;
        }

        /// <summary>房ごとの変化のさせ方（片端から / 中央基準）。</summary>
        private VisualElement HairSlopeModeDD()
        {
            var dd = new DropdownField(
                new List<string> { T("HairSlopeLinear"), T("HairSlopeSymmetric") },
                (int)_hairP.SlopeMode);
            dd.label = T("HairSlopeMode");
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(_ => { _hairP.SlopeMode = (HairSlopeMode)dd.index; D(); });
            return dd;
        }
    }
}
