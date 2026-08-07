// PlayerPrimitiveMeshSubPanel.Frill.cs
// 図形生成サブパネル：フリル（高度な図形）。
// 基準ベルトの取り込み・自動検索・断面プロファイル編集は
// PlayerPrimitiveMeshSubPanel.BeltProfile.cs の共通部を使う。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Frill;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private FrillParams        _frillP     = FrillParams.Default;
        private List<BeltSnapshot> _frillBelts = new List<BeltSnapshot>();
        private MeshSourcePick     _frillPick  = new MeshSourcePick();
        private BeltSplineOption   _frillSpline = new BeltSplineOption();
        private BeltOrientOption   _frillOrient = new BeltOrientOption();

        private BeltProfileEdit _frillEdit = new BeltProfileEdit
        {
            ClosedLoop     = false,
            DefaultProfile = DefaultFrillProfile,
            UndoStackId    = "PlayerEdit/FrillProfileEdit",
            UndoTitle      = "フリル断面編集",
            BgSectionLabel = "フリル下絵",
            CsvRecentKey   = "Primitive.Frill.ProfileCsv",
            CsvDefaultName = "frill_profile.csv",
        };

        private Label _frillInfoLabel;

        /// <summary>厚み付けの角処理(ベベル)UI 要素。</summary>
        private SolidifyUI _frillSolidUI;

        /// <summary>フリルの既定断面。1ステップぶんの平坦な区間（＝基準ベルトと同一形状）。</summary>
        private static List<Vector2> DefaultFrillProfile()
            => new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f) };

        // ================================================================
        // UI
        // ================================================================

        private void BuildFrillUI(VisualElement c)
        {
            c.Add(SL(T("Frill")));
            c.Add(NF(() => _frillP.MeshName, v => _frillP.MeshName = v));

            // ── 基準ベルト（手動取り込み） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("FrillBase")));

            var hint = new Label(T("FrillBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            c.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_frillBelts);
                RefreshFrillInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(c, _frillPick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            c.Add(autoHint);

            c.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_frillBelts, _frillPick.Current);
                RefreshFrillInfo();
            }));

            // ── 円環の自動検索 ──
            var ringHint = new Label(T("AutoDetectRingsHint"));
            ringHint.style.fontSize     = 10;
            ringHint.style.whiteSpace   = WhiteSpace.Normal;
            ringHint.style.marginBottom = 2;
            c.Add(ringHint);

            c.Add(PlayerIoUiKit.WideBtn(T("AutoDetectRings"), () =>
            {
                AutoDetectRings(_frillBelts, _frillPick.Current);
                RefreshFrillInfo();
            }));

            _frillInfoLabel = new Label(BeltsInfoText(_frillBelts));
            _frillInfoLabel.style.fontSize   = 10;
            _frillInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _frillInfoLabel.style.marginTop  = 2;
            c.Add(_frillInfoLabel);

            // ── 梯子CSV ──
            BuildBeltCsvUI(c, _frillBelts,
                "Primitive.Frill.BeltCsv", "frill_belt.csv", RefreshFrillInfo);

            // ── 梯子の向き ──
            BuildBeltOrientUI(c, _frillOrient);

            // ── 共有レールの接続 ──
            c.Add(TR(T("FrillConnect"), () => _frillP.ConnectShared,
                v => { _frillP.ConnectShared = v; D(); }));

            var connectHint = new Label(T("FrillConnectHint"));
            connectHint.style.fontSize     = 10;
            connectHint.style.whiteSpace   = WhiteSpace.Normal;
            connectHint.style.marginBottom = 2;
            c.Add(connectHint);

            // ── rung 境界の段差 ──
            c.Add(TR(T("FrillRungSeam"),
                () => _frillP.RungSeam == FrillRungSeam.Split,
                v => { _frillP.RungSeam = v ? FrillRungSeam.Split : FrillRungSeam.Merge; D(); }));

            var seamHint = new Label(T("FrillRungSeamHint"));
            seamHint.style.fontSize     = 10;
            seamHint.style.whiteSpace   = WhiteSpace.Normal;
            seamHint.style.marginBottom = 2;
            c.Add(seamHint);

            // ── 厚み付け ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), 0f, 0.5f, () => _frillP.Thickness,
                v => { _frillP.Thickness = v; D(); RefreshFrillSolidVis(); }));

            // 角処理(ベベル)UI は常時生成し、厚み/分割数に応じて表示切替する。
            var frillSolid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), 0, 16, () => _frillP.SegmentsFront,
                               v => { _frillP.SegmentsFront = v; D(); RefreshFrillSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), 0.001f, 0.25f, () => _frillP.EdgeSizeFront,
                               v => { _frillP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), 0, 16, () => _frillP.SegmentsBack,
                               v => { _frillP.SegmentsBack = v; D(); RefreshFrillSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), 0.001f, 0.25f, () => _frillP.EdgeSizeBack,
                               v => { _frillP.EdgeSizeBack = v; D(); }),
                Inward    = TR(T("EdgeInward"), () => _frillP.EdgeInward,
                               v => { _frillP.EdgeInward = v; D(); }),
            };
            _frillSolidUI = frillSolid;
            c.Add(frillSolid.EdgeLabel); c.Add(frillSolid.FrontSeg); c.Add(frillSolid.FrontSize);
            c.Add(frillSolid.BackSeg);   c.Add(frillSolid.BackSize); c.Add(frillSolid.Inward);
            RefreshFrillSolidVis();

            BuildBeltSplineUI(c, _frillSpline);

            BuildBeltProfileEditor(_profileEditorContainer, _frillEdit, T("FrillAxisHint"));
        }

        private void RefreshFrillInfo()
        {
            if (_frillInfoLabel != null) _frillInfoLabel.text = BeltsInfoText(_frillBelts);
        }

        private void RefreshFrillSolidVis()
            => UpdateSolidifyVis(_frillSolidUI, _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack);

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 各基準ベルトのステップに同じ波形を繰り返す。未取込なら空メッシュ。
        /// ConnectShared のときは全梯子を1回で生成して共有レールを溶接し、厚み付けも1回だけ適用する。
        /// </summary>
        private MeshObject GenerateFrillMesh()
        {
            EnsureBeltProfile(_frillEdit);

            if (_frillP.ConnectShared)
            {
                var inputs = new List<FrillBeltInput>();
                foreach (var belt in _frillBelts)
                {
                    if (belt == null || !belt.HasData) continue;
                    inputs.Add(ToFrillInput(
                        ApplyBeltSpline(ApplyBeltOrient(belt, _frillOrient), _frillSpline)));
                }

                var joined = FrillMeshGenerator.Generate(
                    inputs, _frillEdit.Points, true, _frillP.RungSeam, _frillP.MeshName);

                return ApplySolidify(joined,
                    _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack,
                    _frillP.EdgeSizeFront, _frillP.EdgeSizeBack, _frillP.EdgeInward,
                    _frillP.MeshName);
            }

            var single = new List<FrillBeltInput>(1) { null };
            var mo = new MeshObject(_frillP.MeshName);
            foreach (var belt in _frillBelts)
            {
                if (belt == null || !belt.HasData) continue;
                single[0] = ToFrillInput(
                    ApplyBeltSpline(ApplyBeltOrient(belt, _frillOrient), _frillSpline));

                var part = FrillMeshGenerator.Generate(
                    single, _frillEdit.Points, false, _frillP.RungSeam, _frillP.MeshName);
                part = ApplySolidify(part,
                    _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack,
                    _frillP.EdgeSizeFront, _frillP.EdgeSizeBack, _frillP.EdgeInward,
                    _frillP.MeshName);
                AppendMesh(mo, part);
            }
            return mo;
        }

        private static FrillBeltInput ToFrillInput(BeltSnapshot b)
            => new FrillBeltInput
            {
                Left        = b.Left,
                Right       = b.Right,
                Closed      = b.Closed,
                FlipWinding = b.FlipWinding,
            };
    }
}
