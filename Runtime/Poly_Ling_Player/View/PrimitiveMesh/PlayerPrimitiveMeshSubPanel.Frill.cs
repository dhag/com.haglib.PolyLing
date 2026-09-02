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
using Poly_Ling.PrimitiveMesh;

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
        private BeltStackOption    _frillStack  = new BeltStackOption();

        private BeltProfileEdit _frillEdit = new BeltProfileEdit
        {
            ClosedLoop     = false,
            DefaultProfile = DefaultFrillProfile,
            UndoStackId    = "PlayerEdit/FrillProfileEdit",
            UndoTitle      = "フリル断面編集A",
            BgSectionLabel = "フリル下絵A",
            CsvRecentKey   = "Primitive.Frill.ProfileCsv",
            CsvDefaultName = "frill_profile.csv",
            ObjectName     = "FrillProfileA",
        };

        /// <summary>2プロファイルモードの B 側。A とは Undo スタックも下絵もCSVパスも別に持つ。</summary>
        private BeltProfileEdit _frillEditB = new BeltProfileEdit
        {
            ClosedLoop     = false,
            DefaultProfile = DefaultFrillProfile,
            UndoStackId    = "PlayerEdit/FrillProfileEditB",
            UndoTitle      = "フリル断面編集B",
            BgSectionLabel = "フリル下絵B",
            CsvRecentKey   = "Primitive.Frill.ProfileCsvB",
            CsvDefaultName = "frill_profile_b.csv",
            ObjectName     = "FrillProfileB",
        };

        /// <summary>断面プロファイルエディタで B 側を編集中なら true（メモリ保持・非永続）。</summary>
        private bool _frillEditingB;

        private Label _frillInfoLabel;

        /// <summary>上下フリップ行。2プロファイルOFFのときは隠す。</summary>
        private VisualElement _frillFlipRow;
        private VisualElement _frillFlipHint;

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

            // ── 上下方向への探索（取り込み・自動検索・円環検索すべてに効く） ──
            bc.Add(TR(T("BeltStackSearch"), () => _frillStack.Enabled,
                v => { _frillStack.Enabled = v; D(); }));

            var stackHint = new Label(T("BeltStackSearchHint"));
            stackHint.style.fontSize     = 10;
            stackHint.style.whiteSpace   = WhiteSpace.Normal;
            stackHint.style.marginBottom = 2;
            bc.Add(stackHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_frillBelts, _frillStack.Enabled);
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
                AutoDetectBelts(_frillBelts, _frillPick.Current, _frillStack.Enabled);
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
                AutoDetectRings(_frillBelts, _frillPick.Current, _frillStack.Enabled);
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

            // ── 断面プロファイルを2本にする ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FrillTwoProfiles"), () => _frillP.TwoProfiles,
                v => { _frillP.TwoProfiles = v; D(); RefreshFrillProfileVis(); RebuildFrillProfileEditor(); }));

            var twoHint = new Label(T("FrillTwoProfilesHint"));
            twoHint.style.fontSize     = 10;
            twoHint.style.whiteSpace   = WhiteSpace.Normal;
            twoHint.style.marginBottom = 2;
            c.Add(twoHint);

            _frillFlipRow = TR(T("FrillProfileFlip"), () => _frillP.ProfileFlip,
                v => { _frillP.ProfileFlip = v; D(); });
            c.Add(_frillFlipRow);

            var flipHint = new Label(T("FrillProfileFlipHint"));
            flipHint.style.fontSize     = 10;
            flipHint.style.whiteSpace   = WhiteSpace.Normal;
            flipHint.style.marginBottom = 2;
            _frillFlipHint = flipHint;
            c.Add(flipHint);

            RefreshFrillProfileVis();

            // ── 面の向き ──
            c.Add(TR(T("FlipFaces"), () => _frillP.FlipFaces,
                v => { _frillP.FlipFaces = v; D(); }));

            // ── 高さ倍率（全体） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("FrillHeightScale"), FrillParams.HeightScaleMin, FrillParams.HeightScaleMax, () => _frillP.HeightScale,
                v => { _frillP.HeightScale = v; D(); }));

            var heightHint = new Label(T("FrillHeightScaleHint"));
            heightHint.style.fontSize     = 10;
            heightHint.style.whiteSpace   = WhiteSpace.Normal;
            heightHint.style.marginBottom = 2;
            c.Add(heightHint);

            // ── 厚み付け ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), FrillParams.ThicknessMin, FrillParams.ThicknessMax, () => _frillP.Thickness,
                v => { _frillP.Thickness = v; D(); RefreshFrillSolidVis(); }));

            // 角処理(ベベル)UI は常時生成し、厚み/分割数に応じて表示切替する。
            var frillSolid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), FrillParams.EdgeSegmentsMin, FrillParams.EdgeSegmentsMax, () => _frillP.SegmentsFront,
                               v => { _frillP.SegmentsFront = v; D(); RefreshFrillSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), FrillParams.EdgeSizeMin, FrillParams.EdgeSizeMax, () => _frillP.EdgeSizeFront,
                               v => { _frillP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), FrillParams.EdgeSegmentsMin, FrillParams.EdgeSegmentsMax, () => _frillP.SegmentsBack,
                               v => { _frillP.SegmentsBack = v; D(); RefreshFrillSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), FrillParams.EdgeSizeMin, FrillParams.EdgeSizeMax, () => _frillP.EdgeSizeBack,
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
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

            RebuildFrillProfileEditor();
        }

        /// <summary>上下フリップ行の表示切替（2プロファイルON時のみ出す）。</summary>
        private void RefreshFrillProfileVis()
        {
            var d = _frillP.TwoProfiles ? DisplayStyle.Flex : DisplayStyle.None;
            if (_frillFlipRow  != null) _frillFlipRow.style.display  = d;
            if (_frillFlipHint != null) _frillFlipHint.style.display = d;
        }

        /// <summary>
        /// 断面プロファイルエディタを作り直す。
        /// 2プロファイルONのときは A/B 切替行を先頭に置き、編集していない側を灰色で重ねて表示する。
        /// </summary>
        private void RebuildFrillProfileEditor()
        {
            var pe = _profileEditorContainer;
            if (pe == null) return;

            pe.Clear();

            EnsureBeltProfile(_frillEdit);
            EnsureBeltProfile(_frillEditB);

            bool two = _frillP.TwoProfiles;
            if (!two) _frillEditingB = false;

            var active = _frillEditingB ? _frillEditB : _frillEdit;
            var other  = _frillEditingB ? _frillEdit  : _frillEditB;

            _frillEdit .GhostPoints = null;
            _frillEditB.GhostPoints = null;
            if (two) active.GhostPoints = other.Points;

            if (two)
            {
                pe.Add(PlayerIoUiKit.SectionLabel(T("FrillProfileTarget")));

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom  = 2;

                row.Add(FrillProfileTargetBtn(T("FrillProfileA"), false));
                row.Add(FrillProfileTargetBtn(T("FrillProfileB"), true));
                pe.Add(row);

                var ghostHint = new Label(T("FrillProfileGhostHint"));
                ghostHint.style.fontSize     = 10;
                ghostHint.style.whiteSpace   = WhiteSpace.Normal;
                ghostHint.style.marginBottom = 2;
                pe.Add(ghostHint);
            }

            BuildBeltProfileEditor(pe, active, T("FrillAxisHint"));
            PlayerLayoutRoot.ApplyDarkTheme(pe);
        }

        /// <summary>A/B 切替ボタン1個ぶん。</summary>
        private Button FrillProfileTargetBtn(string label, bool isB)
        {
            var btn = new Button(() =>
            {
                if (_frillEditingB == isB) return;
                _frillEditingB = isB;
                RebuildFrillProfileEditor();
            })
            { text = label };

            btn.style.flexGrow = 1;
            btn.style.backgroundColor = (_frillEditingB == isB)
                ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            return btn;
        }

        private void RefreshFrillInfo()
        {
            RefreshCreateButtonState();
            if (_frillInfoLabel != null) _frillInfoLabel.text = BeltsInfoText(_frillBelts);
            RebuildFrillBeltScales();
        }

        /// <summary>
        /// 高さ倍率スライダを作り直す。段グループが1つの単位になる（段ごとには出さない）。
        /// 梯子の取込・自動検索・CSV読込はすべて RefreshFrillInfo を通るため、ここから呼ばれる。
        /// グループが1つ以下のときは全体倍率だけで足りるので何も出さない。
        /// </summary>
        private void RebuildFrillBeltScales()
        {
            var box = _frillBeltScaleContainer;
            if (box == null) return;

            box.Clear();

            // グループ番号ごとにまとめる。出現順を保つため List で持つ。
            var order  = new List<int>();
            var groups = new Dictionary<int, List<BeltSnapshot>>();

            foreach (var b in _frillBelts)
            {
                if (b == null || !b.HasData) continue;

                int gid = b.GroupId;
                if (!groups.TryGetValue(gid, out var list))
                {
                    list = new List<BeltSnapshot>();
                    groups[gid] = list;
                    order.Add(gid);
                }
                list.Add(b);
            }

            if (order.Count < 2) return;

            box.Add(PlayerIoUiKit.SectionLabel(T("FrillBeltScales")));

            var hint = new Label(T("FrillBeltScalesHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            box.Add(hint);

            // ラベル欄は 80px 固定でグループ名が入らないため、見出し行とスライダ行に分ける。
            for (int i = 0; i < order.Count; i++)
            {
                var members = groups[order[i]];   // クロージャがループ変数を掴まないように控える

                int rungs = 0;
                foreach (var b in members) rungs += b.RungCount;

                var rowLabel = new Label(members.Count > 1
                    ? T("FrillGroupScaleRow", i + 1, members.Count, rungs)
                    : T("FrillBeltScaleRow",  i + 1, rungs));
                rowLabel.style.fontSize  = 10;
                rowLabel.style.marginTop = 3;
                box.Add(rowLabel);

                box.Add(SR(T("FrillHeightScale"), FrillParams.HeightScaleMin, FrillParams.HeightScaleMax,
                    () => members[0].HeightScale,
                    v => { foreach (var b in members) b.HeightScale = v; D(); }));
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
            EnsureBeltProfile(_frillEditB);

            // 融合ありはレール行ごと、融合なしは梯子ごとにパーツIDを 0 から連番にする。
            var partsIds = new Poly_Ling.PrimitiveMesh.PartsIdCounter();

            if (_frillP.ConnectShared)
            {
                var inputs = new List<FrillBeltInput>();
                foreach (var belt in _frillBelts)
                {
                    if (belt == null || !belt.HasData) continue;
                    inputs.Add(ToFrillInput(
                        ApplyBeltSpline(ApplyBeltOrient(belt, _frillOrient), _frillSpline),
                        _frillP.HeightScale, _frillP.ProfileFlip));
                }

                var joined = FrillMeshGenerator.Generate(
                    inputs, _frillEdit.Points, _frillEditB.Points, _frillP.TwoProfiles,
                    true, _frillP.RungSeam, _frillP.MeshName, partsIds);

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
                    _frillP.HeightScale, _frillP.ProfileFlip);

                var part = FrillMeshGenerator.Generate(
                    single, _frillEdit.Points, _frillEditB.Points, _frillP.TwoProfiles,
                    false, _frillP.RungSeam, _frillP.MeshName, partsIds);
                part = ApplySolidify(part,
                    _frillP.Thickness, _frillP.SegmentsFront, _frillP.SegmentsBack,
                    _frillP.EdgeSizeFront, _frillP.EdgeSizeBack, _frillP.EdgeInward,
                    _frillP.MeshName);
                Poly_Ling.Ops.MeshObjectAppendOps.Append(mo, part);
            }
            if (_frillP.FlipFaces)
                Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.FlipFaces(mo);
            Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.ApplyPivotOffset(mo, _frillP.Pivot);
            return mo;
        }

        /// <summary>
        /// 生成入力へ変換する。高さ倍率は「全体 × 梯子ごと」の掛け算で合成する。
        /// プロファイル補間パラメータは段番号から決める。
        /// N 段グループの段 r は左レール r/N・右レール (r+1)/N になり、
        /// 隣り合う段が共有するレールでは同じ値になる。
        /// </summary>
        private static FrillBeltInput ToFrillInput(BeltSnapshot b, float globalHeightScale, bool flip)
        {
            int n = Mathf.Max(1, b.RowCount);
            int r = Mathf.Clamp(b.RowIndex, 0, n - 1);

            float t0 = (float)r / n;
            float t1 = (float)(r + 1) / n;

            if (flip) { t0 = 1f - t0; t1 = 1f - t1; }

            return new FrillBeltInput
            {
                Left        = b.Left,
                Right       = b.Right,
                Closed      = b.Closed,
                FlipWinding = b.FlipWinding,
                HeightScale = globalHeightScale * b.HeightScale,
                TLeft       = t0,
                TRight      = t1,
            };
        }
    }
}
