// PlayerPrimitiveMeshSubPanel.LatticeTower.cs
// 図形生成サブパネル：鉄塔（高度な図形）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【作るもの】
//   空の親の下に、塔体（createLatticeTower）とデッキ（createTowerDeck）を子として作る
//   （CreateTowerAssemblyCommand）。並びは 1 件のオブジェクトグループとして残る。
//   共通の配置欄（位置・回転・拡大）は親の姿勢になる。
//
// 【既存の鉄塔】
//   一覧で選んだ鉄塔（塔体・デッキ・親のどれか）へ、選んでいるデッキの設定でデッキを足す
//   （AddTowerDeckCommand）。デッキを選んでいれば、その配置元を選んでいるデッキの配置元へ
//   差し替える（ReplaceObjectGroupSourceCommand）。
//
// 【プリセット】
//   送電用（四角・矩形・えぼし・回転・MC・門形）と、すそ野の広がる塔（東京タワー型・
//   エッフェル型）。すそ野はカーブ式（LatticeTowerMeshGenerator.FlareWidth）から点列を起こす。

using System;
using System.Collections.Generic;
using System.Globalization;
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

        private enum TowerPreset { Square, Rect, Eboshi, Rotated, MC, Gate, Tokyo, Eiffel }

        private static readonly string[] TowerPresetKeys =
            { "TowerPresetSquare", "TowerPresetRect", "TowerPresetEboshi", "TowerPresetRotated",
              "TowerPresetMC", "TowerPresetGate", "TowerPresetTokyo", "TowerPresetEiffel" };

        private static readonly string[] TowerBracingKeys =
            { "TowerBracingNone", "TowerBracingX", "TowerBracingSingle", "TowerBracingChevron", "TowerBracingDiamond" };

        /// <summary>デッキ 1 つ。配置元の選択はデッキごとに持つ。</summary>
        private sealed class TowerDeckPart
        {
            public TowerDeckMeshGenerator.TowerDeckParams Deck = TowerDeckMeshGenerator.TowerDeckParams.Default;
            public MeshSourceMultiPick Pick = new MeshSourceMultiPick();
        }

        private string _ltName = "Tower";
        private LatticeTowerMeshGenerator.LatticeTowerParams _ltP = TowerPresetParams(TowerPreset.Square);
        private readonly List<TowerDeckPart> _ltDecks = new List<TowerDeckPart>();
        private int _ltDeckSel = -1;

        // すそ野カーブ
        private float _ltFlareBase = 2.3f, _ltFlareTop = 0.2f, _ltFlareHeight = 5.4f, _ltFlareCurv = 3.5f;
        private int   _ltFlarePoints = 9;

        // 既存の鉄塔の対象（描画オブジェクトの MasterIndex）
        private int _ltTargetIndex = -1;
        private List<(string Label, int MasterIndex, MeshObject Mesh)> _ltTargetCandidates
            = new List<(string, int, MeshObject)>();

        [UiControl(Ignore = true)]
        private readonly List<Button> _ltDeckButtons = new List<Button>();

        [UiControl("latticeTower.info", Safety = UiSafety.ReadOnly, Description = "鉄塔の状態")]
        private Label _ltInfo;

        private TowerDeckPart SelectedTowerDeck
            => (_ltDeckSel >= 0 && _ltDeckSel < _ltDecks.Count) ? _ltDecks[_ltDeckSel] : null;

        // ================================================================
        // プリセット
        // ================================================================

        private static LatticeTowerMeshGenerator.LatticeTowerParams TowerPresetParams(TowerPreset kind)
        {
            var p = LatticeTowerMeshGenerator.LatticeTowerParams.Default.DeepCopy();
            p.MeshName = "TowerBody";

            switch (kind)
            {
                case TowerPreset.Square:
                    break;   // 既定値が四角鉄塔

                case TowerPreset.Rect:
                    p.Heights = new[] { 0f, 2.2f, 2.7f };
                    p.WidthsX = new[] { 0.9f, 0.75f, 0.75f };
                    p.WidthsZ = new[] { 0.45f, 0.4f, 0.4f };
                    p.Panels  = new[] { 5, 1 };
                    p.Bracing = new[] { (int)TowerBracing.X, (int)TowerBracing.Chevron };
                    p.ArmHeights = new[] { 2.7f }; p.ArmLengths = new[] { 0.45f };
                    p.ArmDepths  = new[] { 0.25f }; p.ArmAxes = new[] { 0 };
                    p.PeakHeight = 0f;
                    break;

                case TowerPreset.Eboshi:
                    p.Heights = new[] { 0f, 1.3f, 2.3f, 2.7f };
                    p.WidthsX = new[] { 0.8f, 0.3f, 1.0f, 1.0f };
                    p.WidthsZ = new[] { 0.5f, 0.3f, 0.35f, 0.35f };
                    p.Panels  = new[] { 4, 3, 1 };
                    p.Bracing = new[] { (int)TowerBracing.X, (int)TowerBracing.X, (int)TowerBracing.Chevron };
                    p.ArmHeights = new[] { 2.7f }; p.ArmLengths = new[] { 0.3f };
                    p.ArmDepths  = new[] { 0.2f }; p.ArmAxes = new[] { 0 };
                    p.PeakHeight = 0f;
                    break;

                case TowerPreset.Rotated:
                    p.Heights = new[] { 0f, 1.5f, 2.4f, 2.7f };
                    p.WidthsX = new[] { 0.9f, 0.35f, 1.1f, 1.1f };
                    p.WidthsZ = new[] { 0.9f, 0.35f, 1.1f, 1.1f };
                    p.Panels  = new[] { 4, 3, 1 };
                    p.Bracing = new[] { (int)TowerBracing.X, (int)TowerBracing.X, (int)TowerBracing.X };
                    p.PlanRotation = 45f;
                    p.ArmHeights = new[] { 2.7f }; p.ArmLengths = new[] { 0.25f };
                    p.ArmDepths  = new[] { 0.2f }; p.ArmAxes = new[] { 0 };
                    p.PeakHeight = 0f;
                    break;

                case TowerPreset.MC:
                    p.Heights = new[] { 0f, 3.8f };
                    p.WidthsX = new[] { 0.36f, 0.24f };
                    p.WidthsZ = new[] { 0.36f, 0.24f };
                    p.Panels  = new[] { 9 };
                    p.Bracing = new[] { (int)TowerBracing.Single };
                    p.LegSize = 0.035f; p.BraceSize = 0.02f;
                    p.ArmHeights = new[] { 2.9f, 3.4f }; p.ArmLengths = new[] { 0.45f, 0.4f };
                    p.ArmDepths  = new[] { 0.18f, 0.18f }; p.ArmAxes = new[] { 0, 0 };
                    p.PeakHeight = 0.6f; p.PeakSize = 0.03f;
                    break;

                case TowerPreset.Gate:
                    p.Heights = new[] { 0f, 2.8f };
                    p.WidthsX = new[] { 0.35f, 0.35f };
                    p.WidthsZ = new[] { 0.35f, 0.35f };
                    p.Panels  = new[] { 7 };
                    p.Bracing = new[] { (int)TowerBracing.X };
                    p.LegSize = 0.035f; p.BraceSize = 0.018f; p.TopSizeRatio = 1f;
                    p.ArmHeights = new float[0]; p.ArmLengths = new float[0];
                    p.ArmDepths  = new float[0]; p.ArmAxes = new int[0];
                    p.PeakHeight = 0f;
                    p.GateSpan = 2.0f; p.GateBeamHeights = new[] { 2.8f, 1.6f }; p.GateBeamDepth = 0.3f;
                    break;

                case TowerPreset.Tokyo:
                    p = FlareTower(p, new[] { 0f, 0.6f, 1.2f, 1.8f, 2.4f, 3.0f, 3.5f, 4.0f },
                                   1.4f, 0.2f, 4.0f, 2.5f);
                    p.LegLatticeTop = 1.8f; p.LegColumnWidth = 0.18f; p.LegColumnWidthTop = 0.08f;
                    p.ArchHeight = 0.45f; p.ArchSpring = 0f;
                    p.PeakHeight = 1.3f; p.PeakSize = 0.06f;
                    break;

                case TowerPreset.Eiffel:
                    p = FlareTower(p, new[] { 0f, 0.9f, 1.5f, 2.2f, 2.9f, 3.6f, 4.3f, 4.9f, 5.4f },
                                   2.3f, 0.2f, 5.4f, 3.5f);
                    p.LegLatticeTop = 2.2f; p.LegColumnWidth = 0.28f; p.LegColumnWidthTop = 0.12f;
                    p.ArchHeight = 0.75f; p.ArchSpring = 0.12f;
                    p.PeakHeight = 0.5f; p.PeakSize = 0.05f;
                    break;
            }
            return p;
        }

        /// <summary>
        /// すそ野の広がる塔。点列の幅はカーブ式から起こし、最下区間は斜材なし（アーチ用に開ける）。
        /// 腕金は付けない。
        /// </summary>
        private static LatticeTowerMeshGenerator.LatticeTowerParams FlareTower(
            LatticeTowerMeshGenerator.LatticeTowerParams p, float[] heights,
            float baseW, float topW, float height, float curvature)
        {
            int n = heights.Length;
            p.Heights = (float[])heights.Clone();
            p.WidthsX = new float[n];
            p.WidthsZ = new float[n];
            for (int i = 0; i < n; i++)
                p.WidthsX[i] = p.WidthsZ[i] =
                    LatticeTowerMeshGenerator.FlareWidth(heights[i], height, baseW, topW, curvature);

            p.Panels  = new int[n - 1];
            p.Bracing = new int[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                p.Panels[i]  = i == 0 ? 1 : 2;
                p.Bracing[i] = i == 0 ? (int)TowerBracing.None : (int)TowerBracing.X;
            }
            p.SmoothProfile = true;
            p.LegSize = 0.03f; p.BraceSize = 0.015f; p.LacingSize = 0.008f; p.TopSizeRatio = 0.4f;
            p.LacingDivisions = 3;
            p.ArmHeights = new float[0]; p.ArmLengths = new float[0];
            p.ArmDepths  = new float[0]; p.ArmAxes = new int[0];
            p.GateSpan = 0f; p.GateBeamHeights = new float[0];
            return p;
        }

        private void ApplyTowerPreset(TowerPreset kind)
        {
            _ltP = TowerPresetParams(kind);
            RebuildSettings();
            DLT();
        }

        /// <summary>すそ野カーブから点列を作り直す。区間の段数・斜材も作り直す。</summary>
        private void ApplyTowerFlare()
        {
            int n = Mathf.Clamp(_ltFlarePoints, 2, 40);
            var hs = new float[n];
            for (int i = 0; i < n; i++) hs[i] = _ltFlareHeight * i / (n - 1);

            var keep = _ltP;
            _ltP = FlareTower(_ltP.DeepCopy(), hs, _ltFlareBase, _ltFlareTop, _ltFlareHeight, _ltFlareCurv);
            // 部材の太さ・組柱・アーチ・頂部は今の値を残す。
            _ltP.LegSize = keep.LegSize; _ltP.BraceSize = keep.BraceSize;
            _ltP.LacingSize = keep.LacingSize; _ltP.TopSizeRatio = keep.TopSizeRatio;
            _ltP.LacingDivisions = keep.LacingDivisions;
            RebuildSettings();
            DLT();
        }

        // ================================================================
        // UI
        // ================================================================

        private void BuildLatticeTowerUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("LatticeTower")));
            c.Add(NF(() => _ltName, v => _ltName = v));
            c.Add(GearHint(T("LatticeTowerHint")));

            // ── プリセット
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerPresets")));
            for (int r = 0; r < 2; r++)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
                for (int k = r * 4; k < r * 4 + 4; k++)
                {
                    var kind = (TowerPreset)k;
                    SB(row, T(TowerPresetKeys[k]), () => ApplyTowerPreset(kind));
                }
                c.Add(row);
            }

            BuildTowerProfileUI(c);
            BuildTowerMembersUI(c);
            BuildTowerArmsUI(c);
            BuildTowerGateUI(c);
            BuildTowerDecksUI(c);
            BuildTowerExistingUI(c);

            _ltInfo = GearHint(string.Empty);
            c.Add(_ltInfo);
            RefreshLatticeTowerInfo();
        }

        // ── 脚線 ──────────────────────────────────────────────────────

        private void BuildTowerProfileUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerProfile")));
            c.Add(GearHint(T("TowerProfileHeader")));

            int n = Math.Min(_ltP.Heights.Length, Math.Min(_ltP.WidthsX.Length, _ltP.WidthsZ.Length));
            var bracingChoices = new List<string>();
            foreach (var k in TowerBracingKeys) bracingChoices.Add(T(k));

            for (int i = n - 1; i >= 0; i--)
            {
                int ii = i;
                var row = LtRow();
                row.Add(LtLabel($"{i + 1}", 16));
                row.Add(LtFF(_ltP.Heights[i], v => { _ltP.Heights[ii] = v; DLT(); }));
                row.Add(LtFF(_ltP.WidthsX[i], v => { _ltP.WidthsX[ii] = Mathf.Max(0f, v); DLT(); }));
                row.Add(LtFF(_ltP.WidthsZ[i], v => { _ltP.WidthsZ[ii] = Mathf.Max(0f, v); DLT(); }));
                if (n > 2) row.Add(SmallRowButton("×", () => RemoveTowerPoint(ii)));
                // 点の数だけ並ぶデータ行。行の入れ物として登録し、検査の対象から外す。
                c.Add(RowTarget.AddRows(row));

                if (i == 0) break;

                // 点 i-1 と i の間の区間（下から i 番目）
                int seg = i - 1;
                var srow = LtRow();
                srow.style.marginLeft = 18;
                srow.Add(LtLabel(T("TowerPanels"), 34));
                srow.Add(LtIF(LtAt(_ltP.Panels, seg, 1), v =>
                {
                    _ltP.Panels = LtSet(_ltP.Panels, seg, Mathf.Clamp(v,
                        LatticeTowerMeshGenerator.LatticeTowerParams.PanelsMin,
                        LatticeTowerMeshGenerator.LatticeTowerParams.PanelsMax), 1);
                    DLT();
                }));
                int bk = Mathf.Clamp(LtAt(_ltP.Bracing, seg, (int)TowerBracing.X), 0, TowerBracingKeys.Length - 1);
                var dd = new DropdownField(bracingChoices, bk);
                dd.style.width = 90; dd.style.fontSize = 10;
                dd.RegisterValueChangedCallback(_ =>
                {
                    _ltP.Bracing = LtSet(_ltP.Bracing, seg, dd.index, (int)TowerBracing.X);
                    DLT();
                });
                srow.Add(dd);
                c.Add(RowTarget.AddRows(srow));
            }

            var add = LtRow();
            SB(add, T("TowerAddPoint"), AddTowerPoint);
            c.Add(add);

            c.Add(TR(T("TowerSmooth"), () => _ltP.SmoothProfile, v => { _ltP.SmoothProfile = v; DLT(); }));
            c.Add(SR(T("TowerPanelRatio"), LatticeTowerMeshGenerator.LatticeTowerParams.PanelRatioMin, 2f,
                () => _ltP.PanelRatio, v => { _ltP.PanelRatio = v; DLT(); }));
            c.Add(SR(T("TowerPlanRotation"), -90f, 90f,
                () => _ltP.PlanRotation, v => { _ltP.PlanRotation = v; DLT(); }));

            // すそ野カーブ
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerFlare")));
            c.Add(SR(T("TowerFlareBase"), 0.05f, 10f, () => _ltFlareBase, v => _ltFlareBase = v));
            c.Add(SR(T("TowerFlareTop"), 0f, 5f, () => _ltFlareTop, v => _ltFlareTop = v));
            c.Add(SR(T("TowerFlareHeight"), 0.1f, 20f, () => _ltFlareHeight, v => _ltFlareHeight = v));
            c.Add(SR(T("TowerFlareCurvature"), 0f, 8f, () => _ltFlareCurv, v => _ltFlareCurv = v));
            c.Add(IR(T("TowerFlarePoints"), 2, 20, () => _ltFlarePoints, v => _ltFlarePoints = v));
            var fr = LtRow();
            SB(fr, T("TowerFlareApply"), ApplyTowerFlare);
            c.Add(fr);
        }

        private void AddTowerPoint()
        {
            int n = _ltP.Heights.Length;
            float top = n > 0 ? _ltP.Heights[n - 1] : 0f;
            float prev = n > 1 ? _ltP.Heights[n - 2] : top - 1f;
            float step = Mathf.Max(0.1f, top - prev);

            _ltP.Heights = LtAppend(_ltP.Heights, top + step);
            _ltP.WidthsX = LtAppend(_ltP.WidthsX, n > 0 ? _ltP.WidthsX[n - 1] : 0.5f);
            _ltP.WidthsZ = LtAppend(_ltP.WidthsZ, n > 0 ? _ltP.WidthsZ[n - 1] : 0.5f);
            _ltP.Panels  = LtSet(_ltP.Panels,  n - 1, 1, 1);
            _ltP.Bracing = LtSet(_ltP.Bracing, n - 1, (int)TowerBracing.X, (int)TowerBracing.X);
            RebuildSettings();
            DLT();
        }

        /// <summary>点 i を消す。消した点の上の区間（i が最上点なら下の区間）の段数・斜材も消す。</summary>
        private void RemoveTowerPoint(int i)
        {
            int n = _ltP.Heights.Length;
            if (n <= 2 || i < 0 || i >= n) return;
            int seg = Mathf.Min(i, n - 2);
            _ltP.Heights = LtRemoveAt(_ltP.Heights, i);
            _ltP.WidthsX = LtRemoveAt(_ltP.WidthsX, i);
            _ltP.WidthsZ = LtRemoveAt(_ltP.WidthsZ, i);
            if (_ltP.Panels  != null && seg < _ltP.Panels.Length)  _ltP.Panels  = LtRemoveAt(_ltP.Panels, seg);
            if (_ltP.Bracing != null && seg < _ltP.Bracing.Length) _ltP.Bracing = LtRemoveAt(_ltP.Bracing, seg);
            RebuildSettings();
            DLT();
        }

        // ── 部材・組柱・アーチ・頂部 ──────────────────────────────────

        private void BuildTowerMembersUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerMembers")));
            c.Add(SR(T("TowerLegSize"), 0.001f, 0.3f, () => _ltP.LegSize, v => { _ltP.LegSize = v; DLT(); }));
            c.Add(SR(T("TowerBraceSize"), 0.001f, 0.2f, () => _ltP.BraceSize, v => { _ltP.BraceSize = v; DLT(); }));
            c.Add(SR(T("TowerTopSizeRatio"), LatticeTowerMeshGenerator.LatticeTowerParams.TopSizeRatioMin, 1.5f,
                () => _ltP.TopSizeRatio, v => { _ltP.TopSizeRatio = v; DLT(); }));
            c.Add(TR(T("TowerHorizontals"), () => _ltP.Horizontals, v => { _ltP.Horizontals = v; DLT(); }));
            c.Add(TR(T("TowerPlanBracing"), () => _ltP.PlanBracing, v => { _ltP.PlanBracing = v; DLT(); }));

            c.Add(PlayerIoUiKit.SectionLabel(T("TowerLatticeLegs")));
            c.Add(SR(T("TowerLegLatticeTop"), 0f, 20f, () => _ltP.LegLatticeTop, v => { _ltP.LegLatticeTop = v; DLT(); }));
            c.Add(SR(T("TowerLegColumnWidth"), 0f, 1f, () => _ltP.LegColumnWidth, v => { _ltP.LegColumnWidth = v; DLT(); }));
            c.Add(SR(T("TowerLegColumnWidthTop"), 0f, 1f, () => _ltP.LegColumnWidthTop, v => { _ltP.LegColumnWidthTop = v; DLT(); }));
            c.Add(IR(T("TowerLacingDivisions"), LatticeTowerMeshGenerator.LatticeTowerParams.LacingMin,
                LatticeTowerMeshGenerator.LatticeTowerParams.LacingMax,
                () => _ltP.LacingDivisions, v => { _ltP.LacingDivisions = v; DLT(); }));
            c.Add(SR(T("TowerLacingSize"), 0.001f, 0.1f, () => _ltP.LacingSize, v => { _ltP.LacingSize = v; DLT(); }));

            c.Add(PlayerIoUiKit.SectionLabel(T("TowerArch")));
            c.Add(SR(T("TowerArchHeight"), 0f, 5f, () => _ltP.ArchHeight, v => { _ltP.ArchHeight = v; DLT(); }));
            c.Add(SR(T("TowerArchSpring"), 0f, 3f, () => _ltP.ArchSpring, v => { _ltP.ArchSpring = v; DLT(); }));
            c.Add(IR(T("TowerArchSegments"), LatticeTowerMeshGenerator.LatticeTowerParams.ArchSegmentsMin, 32,
                () => _ltP.ArchSegments, v => { _ltP.ArchSegments = v; DLT(); }));

            c.Add(PlayerIoUiKit.SectionLabel(T("TowerPeak")));
            c.Add(SR(T("TowerPeakHeight"), 0f, 5f, () => _ltP.PeakHeight, v => { _ltP.PeakHeight = v; DLT(); }));
            c.Add(SR(T("TowerPeakSize"), 0.001f, 0.3f, () => _ltP.PeakSize, v => { _ltP.PeakSize = v; DLT(); }));
        }

        // ── 腕金 ──────────────────────────────────────────────────────

        private void BuildTowerArmsUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerArms")));
            c.Add(GearHint(T("TowerArmHeader")));

            int n = _ltP.ArmHeights?.Length ?? 0;
            var axisChoices = new List<string> { T("TowerArmAxisX"), T("TowerArmAxisZ") };
            for (int i = 0; i < n; i++)
            {
                int ii = i;
                var row = LtRow();
                row.Add(LtLabel($"{i + 1}", 16));
                row.Add(LtFF(_ltP.ArmHeights[i], v => { _ltP.ArmHeights[ii] = v; DLT(); }));
                row.Add(LtFF(LtAt(_ltP.ArmLengths, i, 0.5f), v => { _ltP.ArmLengths = LtSet(_ltP.ArmLengths, ii, Mathf.Max(0f, v), 0.5f); DLT(); }));
                row.Add(LtFF(LtAt(_ltP.ArmDepths, i, 0.2f), v => { _ltP.ArmDepths = LtSet(_ltP.ArmDepths, ii, Mathf.Max(0.001f, v), 0.2f); DLT(); }));
                var dd = new DropdownField(axisChoices, Mathf.Clamp(LtAt(_ltP.ArmAxes, i, 0), 0, 1));
                dd.style.width = 44; dd.style.fontSize = 10;
                dd.RegisterValueChangedCallback(_ => { _ltP.ArmAxes = LtSet(_ltP.ArmAxes, ii, dd.index, 0); DLT(); });
                row.Add(dd);
                row.Add(SmallRowButton("×", () => RemoveTowerArm(ii)));
                c.Add(RowTarget.AddRows(row));
            }

            var add = LtRow();
            SB(add, T("TowerAddArm"), AddTowerArm);
            c.Add(add);
            c.Add(SR(T("TowerArmHanger"), 0f, 1f, () => _ltP.ArmHanger, v => { _ltP.ArmHanger = v; DLT(); }));
        }

        private void AddTowerArm()
        {
            int n = _ltP.ArmHeights?.Length ?? 0;
            float top = _ltP.Heights.Length > 0 ? _ltP.Heights[_ltP.Heights.Length - 1] : 1f;
            _ltP.ArmHeights = LtAppend(_ltP.ArmHeights, top);
            _ltP.ArmLengths = LtSet(_ltP.ArmLengths, n, 0.5f, 0.5f);
            _ltP.ArmDepths  = LtSet(_ltP.ArmDepths,  n, 0.2f, 0.2f);
            _ltP.ArmAxes    = LtSet(_ltP.ArmAxes,    n, 0, 0);
            RebuildSettings();
            DLT();
        }

        private void RemoveTowerArm(int i)
        {
            if (_ltP.ArmHeights == null || i < 0 || i >= _ltP.ArmHeights.Length) return;
            _ltP.ArmHeights = LtRemoveAt(_ltP.ArmHeights, i);
            if (_ltP.ArmLengths != null && i < _ltP.ArmLengths.Length) _ltP.ArmLengths = LtRemoveAt(_ltP.ArmLengths, i);
            if (_ltP.ArmDepths  != null && i < _ltP.ArmDepths.Length)  _ltP.ArmDepths  = LtRemoveAt(_ltP.ArmDepths, i);
            if (_ltP.ArmAxes    != null && i < _ltP.ArmAxes.Length)    _ltP.ArmAxes    = LtRemoveAt(_ltP.ArmAxes, i);
            RebuildSettings();
            DLT();
        }

        // ── 門形 ──────────────────────────────────────────────────────

        private void BuildTowerGateUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerGate")));
            c.Add(SR(T("TowerGateSpan"), 0f, 10f, () => _ltP.GateSpan, v => { _ltP.GateSpan = v; DLT(); }));
            c.Add(SR(T("TowerGateBeamDepth"), 0.01f, 2f, () => _ltP.GateBeamDepth, v => { _ltP.GateBeamDepth = v; DLT(); }));

            int n = _ltP.GateBeamHeights?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                int ii = i;
                var row = LtRow();
                row.Add(LtLabel(T("TowerGateBeamHeight", i + 1), 70));
                row.Add(LtFF(_ltP.GateBeamHeights[i], v => { _ltP.GateBeamHeights[ii] = v; DLT(); }));
                row.Add(SmallRowButton("×", () =>
                {
                    _ltP.GateBeamHeights = LtRemoveAt(_ltP.GateBeamHeights, ii);
                    RebuildSettings(); DLT();
                }));
                c.Add(RowTarget.AddRows(row));
            }
            var add = LtRow();
            SB(add, T("TowerAddBeam"), () =>
            {
                float top = _ltP.Heights.Length > 0 ? _ltP.Heights[_ltP.Heights.Length - 1] : 1f;
                _ltP.GateBeamHeights = LtAppend(_ltP.GateBeamHeights, top);
                RebuildSettings(); DLT();
            });
            c.Add(add);
        }

        // ── デッキ ────────────────────────────────────────────────────

        private void BuildTowerDecksUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerDecks")));

            _ltDeckButtons.Clear();
            if (_ltDecks.Count == 0) c.Add(GearHint(T("TowerNoDeck")));
            for (int i = 0; i < _ltDecks.Count; i++)
            {
                int ii = i;
                var row = LtRow();
                var sel = new Button(() => { _ltDeckSel = ii; RebuildSettings(); }) { text = TowerDeckSummary(ii) };
                sel.style.flexGrow = 1; sel.style.height = 20; sel.style.fontSize = 10;
                sel.style.unityTextAlign = TextAnchor.MiddleLeft;
                _ltDeckButtons.Add(sel);
                row.Add(sel);
                row.Add(SmallRowButton("×", () =>
                {
                    _ltDecks.RemoveAt(ii);
                    if (_ltDeckSel >= _ltDecks.Count) _ltDeckSel = _ltDecks.Count - 1;
                    RebuildSettings(); DLT();
                }));
                c.Add(RowTarget.AddRows(row));
            }

            var add = LtRow();
            SB(add, T("TowerAddDeck"), () =>
            {
                var d = new TowerDeckPart();
                var prof = LatticeTowerMeshGenerator.BuildProfile(_ltP);
                d.Deck.Height = prof != null ? Mathf.Lerp(prof.Bottom, prof.Top, 0.4f) : 1f;
                _ltDecks.Add(d);
                _ltDeckSel = _ltDecks.Count - 1;
                RebuildSettings(); DLT();
            });
            c.Add(add);

            var deck = SelectedTowerDeck;
            if (deck == null) return;

            c.Add(PlayerIoUiKit.SectionLabel(T("TowerDeckSettings", _ltDeckSel + 1)));
            BuildMeshSourceMultiRow(c, deck.Pick, T("PlaceSource"));
            c.Add(TR(T("PlaceIncludeChildren"),
                () => deck.Deck.IncludeChildren, v => { deck.Deck.IncludeChildren = v; DLT(); }));
            c.Add(SR(T("TowerDeckHeight"), 0f, 10f, () => deck.Deck.Height, v => { deck.Deck.Height = v; DLT(); }));
            c.Add(SR(T("Scale"), TowerDeckMeshGenerator.TowerDeckParams.ScaleMin, 5f,
                () => deck.Deck.Scale, v => { deck.Deck.Scale = v; DLT(); }));
            c.Add(SR(T("TowerDeckRotationY"), -180f, 180f,
                () => deck.Deck.RotationY, v => { deck.Deck.RotationY = v; DLT(); }));
            c.Add(TR(T("TowerDeckCenter"),
                () => deck.Deck.CenterSource, v => { deck.Deck.CenterSource = v; DLT(); }));
        }

        private string TowerDeckSummary(int i)
        {
            var d = _ltDecks[i];
            int n = d.Pick.SelectedMasterIndices().Count;
            return string.Format(CultureInfo.InvariantCulture, "{0}. {1}  h={2:0.###} ×{3:0.###}  ({4})",
                i + 1, T("TowerDeck").Text, d.Deck.Height, d.Deck.Scale, n);
        }

        /// <summary>選択中の行を強調する。暗色テーマが背景を塗った後で呼ぶ（RebuildSettings 末尾）。</summary>
        private void RefreshLatticeTowerListColors()
        {
            for (int i = 0; i < _ltDeckButtons.Count; i++)
                _ltDeckButtons[i].style.backgroundColor = i == _ltDeckSel
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
        }

        // ── 既存の鉄塔 ────────────────────────────────────────────────

        private void BuildTowerExistingUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TowerExisting")));
            c.Add(GearHint(T("TowerExistingHint")));

            _ltTargetCandidates = GetDrawableMeshEntryList?.Invoke() ?? new List<(string, int, MeshObject)>();
            var choices = new List<string> { T("PlaceNoSource") };
            int sel = 0;
            for (int i = 0; i < _ltTargetCandidates.Count; i++)
            {
                choices.Add(_ltTargetCandidates[i].Label);
                if (_ltTargetCandidates[i].MasterIndex == _ltTargetIndex) sel = i + 1;
            }
            if (sel == 0) _ltTargetIndex = -1;

            var dd = new DropdownField(T("TowerTarget"), choices, sel);
            dd.RegisterValueChangedCallback(_ =>
            {
                int k = dd.index - 1;
                _ltTargetIndex = (k >= 0 && k < _ltTargetCandidates.Count) ? _ltTargetCandidates[k].MasterIndex : -1;
                RefreshLatticeTowerInfo();
            });
            c.Add(RowTarget.Add("TowerTarget", dd, dd.label));

            var row = LtRow();
            SB(row, T("PlaceRefresh"), RebuildSettings);
            SB(row, T("TowerAddDeckToExisting"), InvokeAddTowerDeck);
            SB(row, T("TowerReplaceDeckSource"), InvokeReplaceTowerDeckSource);
            c.Add(row);
        }

        private void InvokeAddTowerDeck()
        {
            if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }
            if (_ltTargetIndex < 0) { _statusLabel.text = T("TowerNeedTarget"); return; }
            var deck = SelectedTowerDeck;
            var src = deck?.Pick.SelectedMasterIndices();
            if (src == null || src.Count == 0) { _statusLabel.text = T("TowerNeedDeckSource"); return; }

            var prm = deck.Deck;
            prm.MeshName = "";   // 親の名前から付ける（ディスパッチャ側）
            SendCommand(new AddTowerDeckCommand(ModelIndex(), _ltTargetIndex, prm, src.ToArray(), EffectiveMaterialIndex));
            _statusLabel.text = T("TowerDeckAdded");
            RebuildSettings();
        }

        private void InvokeReplaceTowerDeckSource()
        {
            if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }
            if (_ltTargetIndex < 0) { _statusLabel.text = T("TowerNeedTarget"); return; }
            var src = SelectedTowerDeck?.Pick.SelectedMasterIndices();
            if (src == null || src.Count == 0) { _statusLabel.text = T("TowerNeedDeckSource"); return; }

            SendCommand(new ReplaceObjectGroupSourceCommand(ModelIndex(), _ltTargetIndex, src.ToArray()));
            _statusLabel.text = T("TowerDeckReplaced");
        }

        // ================================================================
        // 生成
        // ================================================================

        private string TowerBaseName => string.IsNullOrEmpty(_ltName) ? "Tower" : _ltName;

        private CreateLatticeTowerCommand BuildTowerBodyCommand()
        {
            var prm = _ltP.DeepCopy();
            prm.MeshName = TowerBaseName + "_Body";
            return new CreateLatticeTowerCommand(ModelIndex(), prm,
                MagicCircleElementCodec.ChildPlacement(EffectiveMaterialIndex, _mergeDuplicateVertices));
        }

        private CreateTowerDeckCommand BuildTowerDeckCommand(int i)
        {
            var d = _ltDecks[i];
            var prm = d.Deck;
            prm.MeshName = $"{TowerBaseName}_Deck{i + 1}";
            return new CreateTowerDeckCommand(ModelIndex(), prm, d.Pick.SelectedMasterIndices().ToArray(),
                MagicCircleElementCodec.ChildPlacement(EffectiveMaterialIndex, _mergeDuplicateVertices));
        }

        private bool LatticeTowerReady(out string why)
        {
            why = null;
            if (LatticeTowerMeshGenerator.BuildProfile(_ltP) == null) { why = T("TowerNeedProfile"); return false; }
            for (int i = 0; i < _ltDecks.Count; i++)
            {
                if (_ltDecks[i].Pick.SelectedMasterIndices().Count == 0)
                {
                    why = T("TowerDeckNotReady", i + 1);
                    return false;
                }
            }
            return true;
        }

        private void RefreshLatticeTowerInfo()
        {
            if (_ltInfo == null) return;
            _ltInfo.text = LatticeTowerReady(out string why) ? "" : why;
        }

        /// <summary>値の変更。一覧の要約と情報欄も書き直す。</summary>
        private void DLT()
        {
            D();
            for (int i = 0; i < _ltDeckButtons.Count && i < _ltDecks.Count; i++)
                _ltDeckButtons[i].text = TowerDeckSummary(i);
            RefreshLatticeTowerInfo();
        }

        /// <summary>プレビュー。塔体とデッキを 1 つへ連結し、共通の回転・拡大を掛ける。</summary>
        private MeshObject GenerateLatticeTowerPreview(bool forPreview, bool applyTransform)
        {
            var result = PrimitiveMeshFactory.Build(BuildTowerBodyCommand(), forPreview, ResolvePlaceSources, ResolveBeltSource);
            for (int i = 0; i < _ltDecks.Count; i++)
            {
                if (_ltDecks[i].Pick.SelectedMasterIndices().Count == 0) continue;
                var one = PrimitiveMeshFactory.Build(BuildTowerDeckCommand(i), forPreview, ResolvePlaceSources, ResolveBeltSource);
                if (one == null || one.VertexCount == 0) continue;
                if (result == null) result = one;
                else Poly_Ling.Ops.MeshObjectAppendOps.Append(result, one, copyNormals: true);
            }

            if (result != null && applyTransform)
                PrimitiveMeshTransform.ApplyRotationScale(result, _rotEuler, _scale);
            return result;
        }

        /// <summary>生成ボタン。塔体とデッキを文字列へ直して CreateTowerAssemblyCommand を送る。</summary>
        private void InvokeLatticeTowerGenerate()
        {
            if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }
            if (!LatticeTowerReady(out string why)) { _statusLabel.text = why; return; }

            var elements = new string[1 + _ltDecks.Count];
            elements[0] = MagicCircleElementCodec.Encode(BuildTowerBodyCommand());
            for (int i = 0; i < _ltDecks.Count; i++)
                elements[i + 1] = MagicCircleElementCodec.Encode(BuildTowerDeckCommand(i));

            SendCommand(new CreateTowerAssemblyCommand(
                ModelIndex(), _ltName, _worldPos, _rotEuler, _scale.x, elements));

            _statusLabel.text = T("TowerSent", _ltDecks.Count);
            RefreshMeshNameCandidate();
            RebuildSettings();
        }

        // ================================================================
        // 小さな行の部品
        // ================================================================

        private static VisualElement LtRow()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.marginBottom  = 1;
            return r;
        }

        private static Label LtLabel(string text, float width)
        {
            var l = new Label(text);
            l.style.width = width; l.style.fontSize = 10;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        private static FloatField LtFF(float value, Action<float> set)
        {
            var f = new FloatField { value = value, isDelayed = true };
            f.style.width = 54; f.style.marginLeft = 1; f.style.marginRight = 1;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return f;
        }

        private static IntegerField LtIF(int value, Action<int> set)
        {
            var f = new IntegerField { value = value, isDelayed = true };
            f.style.width = 36; f.style.marginLeft = 1; f.style.marginRight = 1;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return f;
        }

        private static T LtAt<T>(T[] a, int i, T fallback)
            => (a != null && i >= 0 && i < a.Length) ? a[i] : fallback;

        /// <summary>a[i] = v。足りなければ fill で伸ばした新しい配列を返す。</summary>
        private static T[] LtSet<T>(T[] a, int i, T v, T fill)
        {
            int n = Math.Max(a?.Length ?? 0, i + 1);
            var r = new T[n];
            for (int k = 0; k < n; k++) r[k] = (a != null && k < a.Length) ? a[k] : fill;
            if (i >= 0) r[i] = v;
            return r;
        }

        private static T[] LtAppend<T>(T[] a, T v)
        {
            int n = a?.Length ?? 0;
            var r = new T[n + 1];
            if (n > 0) Array.Copy(a, r, n);
            r[n] = v;
            return r;
        }

        private static T[] LtRemoveAt<T>(T[] a, int i)
        {
            var l = new List<T>(a ?? new T[0]);
            if (i >= 0 && i < l.Count) l.RemoveAt(i);
            return l.ToArray();
        }
    }
}
