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

        /// <summary>梯子1本ごとの高さ倍率スライダを並べるコンテナ。梯子リストの変化で作り直す。</summary>
        private VisualElement _frillBeltScaleContainer;

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
            c.Add(ShapeTitle(T("Frill")));
            c.Add(NF(() => _frillP.MeshName, v => _frillP.MeshName = v));

            // ── 基準はしご（取り込み〜向きまでを1つのフォールドにまとめる） ──
            c.Add(PlayerIoUiKit.Divider());
            var baseFold = new Foldout { text = T("FrillBase"), value = true };
            baseFold.style.marginBottom = 4;
            var bc = baseFold.contentContainer;
            c.Add(baseFold);

            var hint = new Label(T("FrillBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            bc.Add(hint);

            bc.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_frillBelts);
                RefreshFrillInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(bc, _frillPick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            bc.Add(autoHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_frillBelts, _frillPick.Current);
                RefreshFrillInfo();
            }));

            // ── 円環の自動検索 ──
            var ringHint = new Label(T("AutoDetectRingsHint"));
            ringHint.style.fontSize     = 10;
            ringHint.style.whiteSpace   = WhiteSpace.Normal;
            ringHint.style.marginBottom = 2;
            bc.Add(ringHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("AutoDetectRings"), () =>
            {
                AutoDetectRings(_frillBelts, _frillPick.Current);
                RefreshFrillInfo();
            }));

            _frillInfoLabel = new Label(BeltsInfoText(_frillBelts));
            _frillInfoLabel.style.fontSize   = 10;
            _frillInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _frillInfoLabel.style.marginTop  = 2;
            bc.Add(_frillInfoLabel);

            // ── はしごごとの高さ倍率 ──
            // 全体倍率とは掛け算で合成する。はしごが2本以上あるときだけ出す。
            _frillBeltScaleContainer = new VisualElement();
            bc.Add(_frillBeltScaleContainer);
            RebuildFrillBeltScales();

            // ── はしごCSV ──
            BuildBeltCsvUI(bc, _frillBelts,
                "Primitive.Frill.BeltCsv", "frill_belt.csv", RefreshFrillInfo);

            // ── はしごの向き ──
            BuildBeltOrientUI(bc, _frillOrient);

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

            // ── 面の向き ──
            c.Add(TR(T("FlipFaces"), () => _frillP.FlipFaces,
                v => { _frillP.FlipFaces = v; D(); }));

            // ── 高さ倍率（全体） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("FrillHeightScale"), 0f, 5f, () => _frillP.HeightScale,
                v => { _frillP.HeightScale = v; D(); }));

            var heightHint = new Label(T("FrillHeightScaleHint"));
            heightHint.style.fontSize     = 10;
            heightHint.style.whiteSpace   = WhiteSpace.Normal;
            heightHint.style.marginBottom = 2;
            c.Add(heightHint);

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

            BuildPivotXYZ(c,
                () => _frillP.Pivot, v => { _frillP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

            BuildBeltProfileEditor(_profileEditorContainer, _frillEdit, T("FrillAxisHint"));
        }

        private void RefreshFrillInfo()
        {
            RefreshCreateButtonState();
            if (_frillInfoLabel != null) _frillInfoLabel.text = BeltsInfoText(_frillBelts);
            RebuildFrillBeltScales();
        }

        /// <summary>
        /// 梯子1本ごとの高さ倍率スライダを作り直す。
        /// 梯子の取込・自動検索・CSV読込はすべて RefreshFrillInfo を通るため、ここから呼ばれる。
        /// 梯子が1本以下のときは全体倍率だけで足りるので何も出さない。
        /// </summary>
        private void RebuildFrillBeltScales()
        {
            var box = _frillBeltScaleContainer;
            if (box == null) return;

            box.Clear();

            int count = 0;
            foreach (var b in _frillBelts) if (b != null && b.HasData) count++;
            if (count < 2) return;

            box.Add(PlayerIoUiKit.SectionLabel(T("FrillBeltScales")));

            var hint = new Label(T("FrillBeltScalesHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            box.Add(hint);

            // ラベル欄は 80px 固定でベルト名が入らないため、見出し行とスライダ行に分ける。
            for (int i = 0; i < _frillBelts.Count; i++)
            {
                var belt = _frillBelts[i];
                if (belt == null || !belt.HasData) continue;

                var target = belt;   // クロージャがループ変数を掴まないように控える

                var rowLabel = new Label(T("FrillBeltScaleRow", i + 1, target.RungCount));
                rowLabel.style.fontSize  = 10;
                rowLabel.style.marginTop = 3;
                box.Add(rowLabel);

                box.Add(SR(T("FrillHeightScale"), 0f, 5f,
                    () => target.HeightScale,
                    v => { target.HeightScale = v; D(); }));
            }

            PlayerLayoutRoot.ApplyDarkTheme(box);
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
                        ApplyBeltSpline(ApplyBeltOrient(belt, _frillOrient), _frillSpline),
                        _frillP.HeightScale));
                }

                var joined = FrillMeshGenerator.Generate(
                    inputs, _frillEdit.Points, true, _frillP.RungSeam, _frillP.MeshName);

                var solid = ApplySolidify(joined,
                    _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack,
                    _frillP.EdgeSizeFront, _frillP.EdgeSizeBack, _frillP.EdgeInward,
                    _frillP.MeshName);
                if (_frillP.FlipFaces)
                    Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.FlipFaces(solid);
                Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.ApplyPivotOffset(solid, _frillP.Pivot);
                return solid;
            }

            var single = new List<FrillBeltInput>(1) { null };
            var mo = new MeshObject(_frillP.MeshName);
            foreach (var belt in _frillBelts)
            {
                if (belt == null || !belt.HasData) continue;
                single[0] = ToFrillInput(
                    ApplyBeltSpline(ApplyBeltOrient(belt, _frillOrient), _frillSpline),
                    _frillP.HeightScale);

                var part = FrillMeshGenerator.Generate(
                    single, _frillEdit.Points, false, _frillP.RungSeam, _frillP.MeshName);
                part = ApplySolidify(part,
                    _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack,
                    _frillP.EdgeSizeFront, _frillP.EdgeSizeBack, _frillP.EdgeInward,
                    _frillP.MeshName);
                AppendMesh(mo, part);
            }
            if (_frillP.FlipFaces)
                Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.FlipFaces(mo);
            Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.ApplyPivotOffset(mo, _frillP.Pivot);
            return mo;
        }

        /// <summary>
        /// 生成入力へ変換する。高さ倍率は「全体 × 梯子ごと」の掛け算で合成する。
        /// </summary>
        private static FrillBeltInput ToFrillInput(BeltSnapshot b, float globalHeightScale)
            => new FrillBeltInput
            {
                Left        = b.Left,
                Right       = b.Right,
                Closed      = b.Closed,
                FlipWinding = b.FlipWinding,
                HeightScale = globalHeightScale * b.HeightScale,
            };
    }
}
