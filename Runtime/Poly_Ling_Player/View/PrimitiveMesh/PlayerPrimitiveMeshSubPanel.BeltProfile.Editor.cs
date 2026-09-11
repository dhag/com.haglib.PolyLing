// PlayerPrimitiveMeshSubPanel.BeltProfile.Editor.cs
// 図形生成サブパネル：ベルト断面プロファイルエディタの組み立て（CSV・変換・アンカー）、
// 編集 Undo、メッシュとの取り込み／反映。キャンバス操作は BeltProfile.Canvas.cs。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 断面プロファイルエディタ
        // ================================================================

        private static void EnsureBeltProfile(BeltProfileEdit ed)
        {
            if (ed == null) return;
            if (ed.Points == null || ed.Points.Count < 2)
                ed.Points = ed.DefaultProfile != null
                    ? ed.DefaultProfile()
                    : new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f) };
        }

        /// <summary>
        /// 断面プロファイルCSVの読み書きUIを組み立てる。
        /// $closedLoop は書き出すのみで、読込時に ed.ClosedLoop へは反映しない
        /// （フリル=開ループ／パイプ=閉ループが生成器側の前提のため）。
        ///
        /// ペア（A/B）を持つエディタでは 4 列書式（XA,YA,XB,YB）も扱う。
        /// 読込は 4 列CSVなら A/B の両方を差し替え、ペアモードがOFFでもONにする。
        /// 保存はペアモードがONのときだけ 4 列で書く。
        /// </summary>
        private void BuildBeltProfileCsvUI(VisualElement pe, BeltProfileEdit ed)
        {
            if (pe == null || ed == null) return;

            // 3エディタ共通：折り畳みセクションにする（既定 閉）。
            pe = FoldSection(pe, T("ProfileCsvSection"), false);

            if (string.IsNullOrEmpty(ed.CsvPath)) ed.CsvPath = RecentPaths.Get(ed.CsvRecentKey);

            var pathField = new TextField();
            pathField.RegisterValueChangedCallback(e =>
            {
                ed.CsvPath = e.newValue;
                RecentPaths.Set(ed.CsvRecentKey, e.newValue);
            });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadProfileCsv()
            {
                string sel = PlayerIoUiKit.AskLoadPath(T("LoadCSV"), ed.CsvRecentKey, ed.CsvPath, "csv");
                if (string.IsNullOrEmpty(sel)) return;
                pathField.value = sel;
                ed.CsvPath = sel;

                var result = ProfilePointsCsvIO.LoadPair(ed.CsvPath, ed.ClosedLoop);
                if (!result.Success) { SetBeltStatus(result.ErrorMessage); return; }

                var other = ed.PairOther;

                // 4 列CSV（XA,YA,XB,YB）は A/B の両方を差し替える。
                // ペアモードがOFFなら EnablePair で強制的にONにする。
                if (result.HasB && other != null)
                {
                    var edA = ed.IsPairB ? other : ed;
                    var edB = ed.IsPairB ? ed    : other;

                    ed.EnablePair?.Invoke();

                    BeltBegin(edA);
                    edA.Points = result.PointsA;
                    edA.Sel.Clear(); edA.SelectedIndex = -1;
                    BeltCommit(edA, "CSV読込A");

                    BeltBegin(edB);
                    edB.Points = result.PointsB;
                    edB.Sel.Clear(); edB.SelectedIndex = -1;
                    BeltCommit(edB, "CSV読込B");

                    // 読んだパスは A/B の双方へ控える。
                    other.CsvPath = ed.CsvPath;
                    RecentPaths.Set(other.CsvRecentKey, ed.CsvPath);

                    SetBeltStatus($"A {T("ImportedPoints", edA.Points.Count)}"
                                + $" / B {T("ImportedPoints", edB.Points.Count)}");
                    D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                    ed.OnPairLoaded?.Invoke();
                    return;
                }

                BeltBegin(ed);
                ed.Points = result.PointsA;
                ed.Sel.Clear(); ed.SelectedIndex = -1;
                BeltCommit(ed, "CSV読込");

                SetBeltStatus(T("ImportedPoints", ed.Points.Count));
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            }

            pe.Add(PlayerIoUiKit.PathRow(pathField, LoadProfileCsv));
            if (!string.IsNullOrEmpty(ed.CsvPath)) pathField.SetValueWithoutNotify(ed.CsvPath);

            pe.Add(PlayerIoUiKit.WideBtn(T("LoadCSV"), LoadProfileCsv));

            pe.Add(PlayerIoUiKit.WideBtn(T("SaveCSV"), () =>
            {
                EnsureBeltProfile(ed);

                // ペアモードがONのときだけ 4 列書式で書く。OFF なら従来の 2 列。
                var  other = ed.PairOther;
                bool pair  = other != null && ed.PairEnabled != null && ed.PairEnabled();
                if (pair) EnsureBeltProfile(other);

                // パス欄は読込用。保存は毎回ダイアログを出す。
                // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
                string save = SaveDest.AskSavePath(
                    T("SaveCSV"), SaveDest.Keys.ProfileCsv, "",
                    pair ? ed.CsvPairDefaultName : ed.CsvDefaultName, "csv");
                if (string.IsNullOrEmpty(save)) return;
                ed.CsvPath = save;
                pathField.value = ed.CsvPath;

                if (pair)
                {
                    var ptsA = ed.IsPairB ? other.Points : ed.Points;
                    var ptsB = ed.IsPairB ? ed.Points    : other.Points;

                    if (ProfilePointsCsvIO.SavePair(ed.CsvPath, ptsA, ptsB, ed.ClosedLoop))
                        SetBeltStatus($"A {T("ImportedPoints", ptsA.Count)}"
                                    + $" / B {T("ImportedPoints", ptsB.Count)}");
                    return;
                }

                if (ProfilePointsCsvIO.Save(ed.CsvPath, ed.Points, ed.ClosedLoop))
                    SetBeltStatus(T("ImportedPoints", ed.Points.Count));
            }));
        }

        /// <summary>断面プロファイルエディタを組み立てる。</summary>
        private void BuildBeltProfileEditor(VisualElement pe, BeltProfileEdit ed, string axisHint)
        {
            if (pe == null || ed == null) return;

            EnsureBeltProfile(ed);

            // ドラッグ状態をリセット（タブ切替後の再Build時）
            ed.Drag = false; ed.HoverEI = -1; ed.PanDrag = false;
            ed.MarqueeDrag = false; ed.HandleDrag = false; ed.AnchorDrag = false; ed.BgDrag = false;

            pe.Add(SL(T("ProfileEditor")));

            var axisLabel = new Label(axisHint);
            axisLabel.style.fontSize     = 10;
            axisLabel.style.whiteSpace   = WhiteSpace.Normal;
            axisLabel.style.marginBottom = 2;
            pe.Add(axisLabel);

            var canvas = new VisualElement();
            canvas.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            canvas.style.height          = _profileHeight;
            canvas.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.15f));
            canvas.style.marginBottom    = 4;
            canvas.style.borderTopWidth   = canvas.style.borderBottomWidth =
            canvas.style.borderLeftWidth  = canvas.style.borderRightWidth  = 1;
            canvas.style.borderTopColor   = canvas.style.borderBottomColor =
            canvas.style.borderLeftColor  = canvas.style.borderRightColor  =
                new StyleColor(new Color(0.4f, 0.4f, 0.45f));
            canvas.style.overflow        = Overflow.Hidden;
            canvas.pickingMode           = PickingMode.Position;
            ed.Canvas = canvas;

            // 下絵レイヤー（ビューレイヤー配下。断面と同じ view 変換で追従）
            ed.ViewLayer = new VisualElement();
            ed.ViewLayer.style.position = Position.Absolute;
            ed.ViewLayer.style.left = ed.ViewLayer.style.top =
            ed.ViewLayer.style.right = ed.ViewLayer.style.bottom = 0;
            ed.ViewLayer.pickingMode = PickingMode.Ignore;

            ed.BgEl = new VisualElement();
            ed.BgEl.style.position = Position.Absolute;
            ed.BgEl.style.display  = DisplayStyle.None;
            ed.BgEl.pickingMode    = PickingMode.Ignore;
            ed.ViewLayer.Add(ed.BgEl);
            canvas.Add(ed.ViewLayer);

            canvas.generateVisualContent += ctx => DrawBeltProfile(ctx, ed);
            canvas.RegisterCallback<PointerDownEvent>(e => OnBeltProfilePointerDown(e, ed));
            canvas.RegisterCallback<PointerMoveEvent>(e => OnBeltProfilePointerMove(e, ed));
            canvas.RegisterCallback<PointerUpEvent>(e   => OnBeltProfilePointerUp(e, ed));
            canvas.RegisterCallback<WheelEvent>(e =>
            {
                if (ed.BgMode)
                {
                    ed.BgScale = Mathf.Clamp(ed.BgScale * (1f - e.delta.y * 0.05f), 0.1f, 10f);
                    ed.BgScaleSlider?.SetValueWithoutNotify(ed.BgScale);
                    UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                }
                else
                {
                    float w = canvas.resolvedStyle.width, h = canvas.resolvedStyle.height;
                    float oldZoom = ed.Zoom;
                    float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * 0.05f), 0.2f, 8f);
                    if (newZoom != oldZoom)
                    {
                        var   m      = (Vector2)e.localMousePosition;
                        var   center = new Vector2(w * 0.5f, h * 0.5f);
                        float k      = newZoom / oldZoom;
                        ed.Offset = (m - center) * (1f - k) + ed.Offset * k;
                        ed.Zoom   = newZoom;
                        UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
                    }
                }
                e.StopPropagation();
            });
            canvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateBeltBgEl(ed); UpdateBeltView(ed); RefreshBeltCanvas(ed);
            });
            pe.Add(canvas);

            AddProfileResizeHandle(pe, canvas, () => RefreshBeltCanvas(ed));

            // ボタン行: 削除 / リセット / ビュー初期化
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            SB(btnRow, T("DeletePoint"), () =>
            {
                EnsureBeltProfile(ed);
                BeltBegin(ed);
                if (ed.Sel.Count > 0)
                {
                    var idxs = new List<int>(ed.Sel);
                    idxs.Sort(); idxs.Reverse();
                    foreach (var idx in idxs)
                        if (idx >= 0 && idx < ed.Points.Count && ed.Points.Count > 2)
                            ed.Points.RemoveAt(idx);
                    ed.Sel.Clear(); ed.SelectedIndex = -1;
                }
                else
                {
                    int sel = ed.SelectedIndex;
                    RevolutionProfileEditCore.RemovePoint(ed.Points, ref sel);
                    ed.SelectedIndex = sel;
                }
                BeltCommit(ed, "点削除");
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            });
            SB(btnRow, T("ResetProfile"), () =>
            {
                BeltBegin(ed);
                ed.Points = ed.DefaultProfile != null ? ed.DefaultProfile() : ed.Points;
                ed.Sel.Clear(); ed.SelectedIndex = -1;
                BeltCommit(ed, "断面リセット");
                D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
            });
            pe.Add(btnRow);

            // ここから下を1つの大フォールドにまとめ、中の各セクションも個別に折り畳む。
            pe = FoldSection(pe, T("EditTools"), true);

            // ビュー操作行（3エディタ共通の並び：ビュー初期化 / 投げ縄 / ギズモ）
            var viewRow = new VisualElement();
            viewRow.style.flexDirection = FlexDirection.Row;
            viewRow.style.marginBottom  = 3;
            SB(viewRow, T("ResetView"), () =>
            {
                ed.Zoom = 1f; ed.Offset = Vector2.zero;
                UpdateBeltView(ed); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed);
            });
            var lassoToggle = new Toggle(T("LassoMode")) { value = ed.LassoMode };
            lassoToggle.style.marginLeft = 4;
            lassoToggle.RegisterValueChangedCallback(ev => ed.LassoMode = ev.newValue);
            viewRow.Add(lassoToggle);
            var gizmoToggle = new Toggle(T("ShowGizmo")) { value = ed.ShowGizmo };
            gizmoToggle.style.marginLeft = 8;
            gizmoToggle.RegisterValueChangedCallback(ev => { ed.ShowGizmo = ev.newValue; RefreshBeltCanvas(ed); });
            viewRow.Add(gizmoToggle);
            pe.Add(viewRow);

            BuildBeltTransformUI(pe, ed);

            // ── 断面プロファイルCSV ───────────────────────────────────────
            BuildBeltProfileCsvUI(pe, ed);

            // ── メッシュ⇄プロファイル ─────────────────────────────────────
            var beltIoFold = FoldSection(pe, T("MeshProfileIO"), false);
            var beltIoRow  = new VisualElement();
            beltIoRow.style.flexDirection = FlexDirection.Row;
            beltIoRow.style.marginBottom  = 4;
            SB(beltIoRow, T("ImportFromMesh"), () => ImportBeltProfileFromMesh(ed));
            SB(beltIoRow, T("ApplyToMesh"),    () => ApplyBeltProfileToMesh(ed));
            beltIoFold.Add(beltIoRow);

            var beltIoHint = new Label(T("BeltProfileIOHint"));
            beltIoHint.style.fontSize   = 10;
            beltIoHint.style.whiteSpace = WhiteSpace.Normal;
            beltIoFold.Add(beltIoHint);

            // 下絵
            BuildBgSection(pe,
                ed.BgSectionLabel,
                () => ed.BgPath,  v => ed.BgPath  = v,
                () => ed.BgAlpha, v => { ed.BgAlpha = v; UpdateBeltBgEl(ed); },
                () => ed.BgMode,  v => { ed.BgMode  = v; },
                () => ed.BgScale, v => { ed.BgScale = Mathf.Clamp(v, 0.1f, 10f); UpdateBeltBgEl(ed); RefreshBeltCanvas(ed); },
                () => ed.BgOrigin, v => { ed.BgOrigin = v; UpdateBeltBgEl(ed); RefreshBeltCanvas(ed); },
                () => ed.BgTex,
                () =>
                {
                    if (string.IsNullOrEmpty(ed.BgPath)) return;
                    var tex = ed.BgTex;
                    LoadBgTexture(ed.BgPath, ref tex, ed.BgEl);
                    ed.BgTex = tex;
                    ed.BgOffset = Vector2.zero; ed.BgScale = 3f;
                    if (ed.BgTex != null)
                        ed.BgOrigin = new Vector2(ed.BgTex.width * 0.5f, ed.BgTex.height * 0.5f);
                    ed.BgScaleSlider?.SetValueWithoutNotify(1f);
                    SetBgSizeLabel(ed.BgSizeLabel, ed.BgTex);
                    UpdateBeltBgEl(ed);
                },
                () =>
                {
                    ed.BgTex = null;
                    ed.BgEl.style.display = DisplayStyle.None;
                    ed.BgEl.style.backgroundImage = new StyleBackground();
                    SetBgSizeLabel(ed.BgSizeLabel, null);
                },
                out var bgScaleSlider, out var bgSizeLabel);
            ed.BgScaleSlider = bgScaleSlider;
            ed.BgSizeLabel   = bgSizeLabel;

            // 選択点スライダー
            ed.PtRow = new VisualElement(); ed.PtRow.style.marginBottom = 4;
            ed.PtLabel = new Label(""); ed.PtLabel.style.fontSize = 9; ed.PtLabel.style.marginBottom = 1;
            ed.PtRow.Add(ed.PtLabel);
            {
                Slider     xSl = new Slider(-1f, 2f); xSl.style.flexGrow = 1;
                FloatField xFf = new FloatField { value = 0f }; xFf.style.width = 42;
                xSl.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    xFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    ed.Points[ed.SelectedIndex] = new Vector2(e.newValue, ed.Points[ed.SelectedIndex].y);
                    D(); RefreshBeltCanvas(ed);
                });
                xSl.RegisterCallback<PointerDownEvent>(_ => BeltBegin(ed));
                xSl.RegisterCallback<PointerUpEvent>(_ => BeltCommit(ed, "点X編集"));
                xFf.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    BeltBegin(ed);
                    float v = e.newValue;
                    xSl.SetValueWithoutNotify(Mathf.Clamp(v, -1f, 2f));
                    ed.Points[ed.SelectedIndex] = new Vector2(v, ed.Points[ed.SelectedIndex].y);
                    D(); RefreshBeltCanvas(ed);
                    BeltCommit(ed, "点X編集");
                });
                var xRow = new VisualElement(); xRow.style.flexDirection = FlexDirection.Row; xRow.style.marginBottom = 2;
                xRow.Add(ML("X")); xRow.Add(xSl); xRow.Add(xFf);
                ed.PtRow.Add(xRow);
                ed.PtXSlider = xSl; ed.PtXField = xFf;
            }
            {
                Slider     ySl = new Slider(-1f, 2f); ySl.style.flexGrow = 1;
                FloatField yFf = new FloatField { value = 0f }; yFf.style.width = 42;
                ySl.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    yFf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                    ed.Points[ed.SelectedIndex] = new Vector2(ed.Points[ed.SelectedIndex].x, e.newValue);
                    D(); RefreshBeltCanvas(ed);
                });
                ySl.RegisterCallback<PointerDownEvent>(_ => BeltBegin(ed));
                ySl.RegisterCallback<PointerUpEvent>(_ => BeltCommit(ed, "点Y編集"));
                yFf.RegisterValueChangedCallback(e =>
                {
                    if (ed.SelectedIndex < 0 || ed.Points == null || ed.SelectedIndex >= ed.Points.Count) return;
                    BeltBegin(ed);
                    float v = e.newValue;
                    ySl.SetValueWithoutNotify(Mathf.Clamp(v, -1f, 2f));
                    ed.Points[ed.SelectedIndex] = new Vector2(ed.Points[ed.SelectedIndex].x, v);
                    D(); RefreshBeltCanvas(ed);
                    BeltCommit(ed, "点Y編集");
                });
                var yRow = new VisualElement(); yRow.style.flexDirection = FlexDirection.Row; yRow.style.marginBottom = 2;
                yRow.Add(ML("Y")); yRow.Add(ySl); yRow.Add(yFf);
                ed.PtRow.Add(yRow);
                ed.PtYSlider = ySl; ed.PtYField = yFf;
            }
            ed.PtRow.style.display = DisplayStyle.None;
            pe.Add(ed.PtRow);

            RefreshBeltPointUI(ed);
        }

        // ================================================================
        // 変換／マグネット／アンカーUI
        // ================================================================

        private void BuildBeltTransformUI(VisualElement pe, BeltProfileEdit ed)
        {
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",     0f, 0f, out ed.TfMoveX,  out ed.TfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out ed.TfScaleX, out ed.TfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out ed.TfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",       0f, out ed.TfRot));

            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", () => ApplyBeltTransform(ed));
            SB(applyRow, "リセット", () =>
            {
                ed.TfMoveX.value = 0f; ed.TfMoveY.value = 0f;
                ed.TfScaleX.value = 1f; ed.TfScaleY.value = 1f;
                ed.TfRot.value = 0f; ed.TfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット
            var magFold = FoldSection(pe, T("Magnet"), false);
            var magRow = new VisualElement(); magRow.style.flexDirection = FlexDirection.Row; magRow.style.marginBottom = 2;
            var magToggle = new Toggle("有効") { value = ed.Magnet.Enabled }; magToggle.style.marginRight = 6;
            magToggle.RegisterValueChangedCallback(ev => { ed.Magnet.Enabled = ev.newValue; RefreshBeltCanvas(ed); });
            var falloff = new EnumField(ed.Magnet.Falloff); falloff.style.flexGrow = 1;
            falloff.RegisterValueChangedCallback(ev => ed.Magnet.Falloff = (FalloffType)ev.newValue);
            magRow.Add(magToggle); magRow.Add(falloff);
            magFold.Add(magRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 2f, ed.Magnet.Radius, out _, out _,
                () => false, v => { ed.Magnet.Radius = v; RefreshBeltCanvas(ed); }));

            // アンカー
            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            ed.AnchorEnterBtn = new Button(() => SetBeltAnchorMode(ed, true)) { text = "アンカー設定" };
            ed.AnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(ed.AnchorEnterBtn);

            ed.AnchorPanel = new VisualElement(); ed.AnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）");
                lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetBeltAnchorMode(ed, false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); ed.AnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyBeltAnchorPreset(ed, Canvas2DAnchor.Preset.BottomLeft));
                ed.AnchorPanel.Add(presetRow);

                ed.AnchorPanel.Add(BuildAnchorRow("X", -1f, 2f, 0f, out ed.AnchorXSlider, out ed.AnchorXField,
                    () => ed.AnchorSuppress, v => SetBeltAnchorComponent(ed, true, v)));
                ed.AnchorPanel.Add(BuildAnchorRow("Y", -1f, 2f, 0f, out ed.AnchorYSlider, out ed.AnchorYField,
                    () => ed.AnchorSuppress, v => SetBeltAnchorComponent(ed, false, v)));
            }
            anchorFold.Add(ed.AnchorPanel);
            RefreshBeltAnchorModeUI(ed);
            RefreshBeltAnchorFields(ed);
        }

        private void SetBeltAnchorMode(BeltProfileEdit ed, bool on)
        {
            ed.Anchor.Mode = on;
            if (on) RefreshBeltAnchorAuto(ed);
            RefreshBeltAnchorModeUI(ed);
            RefreshBeltCanvas(ed);
        }

        private static void RefreshBeltAnchorModeUI(BeltProfileEdit ed)
        {
            if (ed.AnchorEnterBtn != null) ed.AnchorEnterBtn.style.display = ed.Anchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (ed.AnchorPanel    != null) ed.AnchorPanel.style.display    = ed.Anchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void RefreshBeltAnchorFields(BeltProfileEdit ed)
        {
            ed.AnchorSuppress = true;
            ed.AnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(ed.Anchor.Value.x, -1f, 2f));
            ed.AnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(ed.Anchor.Value.y, -1f, 2f));
            ed.AnchorXField?.SetValueWithoutNotify(ed.Anchor.Value.x);
            ed.AnchorYField?.SetValueWithoutNotify(ed.Anchor.Value.y);
            ed.AnchorSuppress = false;
        }

        private static void RefreshBeltAnchorAuto(BeltProfileEdit ed)
        {
            if (ed.Anchor.Manual) return;
            var pts = SelectedBeltPoints(ed);
            if (pts.Count > 0) ed.Anchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshBeltAnchorFields(ed);
        }

        private void SetBeltAnchorComponent(BeltProfileEdit ed, bool isX, float v)
        {
            var a = ed.Anchor.Value; if (isX) a.x = v; else a.y = v; ed.Anchor.Value = a;
            ed.Anchor.Manual = true;
            RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
        }

        private void ApplyBeltAnchorPreset(BeltProfileEdit ed, Canvas2DAnchor.Preset p)
        {
            ed.Anchor.SetPreset(SelectedBeltPoints(ed), p);
            RefreshBeltAnchorFields(ed); RefreshBeltCanvas(ed);
        }

        /// <summary>選択（無ければ全点）の断面座標リスト。</summary>
        private static List<Vector2> SelectedBeltPoints(BeltProfileEdit ed)
        {
            var pts = new List<Vector2>();
            if (ed.Points == null) return pts;
            if (ed.Sel.Count > 0)
            {
                foreach (var i in ed.Sel)
                    if (i >= 0 && i < ed.Points.Count) pts.Add(ed.Points[i]);
            }
            else pts.AddRange(ed.Points);
            return pts;
        }

        private void ApplyBeltTransform(BeltProfileEdit ed)
        {
            EnsureBeltProfile(ed);
            BeltBegin(ed);
            RefreshBeltAnchorAuto(ed);

            var a = ed.Anchor.Value;
            float mx  = ed.TfMoveX?.value  ?? 0f, my = ed.TfMoveY?.value  ?? 0f;
            float sx  = ed.TfScaleX?.value ?? 1f, sy = ed.TfScaleY?.value ?? 1f;
            float deg = ed.TfRot?.value ?? 0f;
            float saRad = (ed.TfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = ed.Sel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in ed.Sel) if (i >= 0 && i < ed.Points.Count) sel.Add(ed.Points[i]);
            var orig = new List<Vector2>(ed.Points);

            for (int i = 0; i < orig.Count; i++)
            {
                float wt;
                if (!useSel)                wt = 1f;
                else if (ed.Sel.Contains(i)) wt = 1f;
                else wt = ed.Magnet.Enabled ? ed.Magnet.WeightFor(orig[i], sel) : 0f;
                if (wt <= 0f) continue;

                ed.Points[i] = Xform2D(orig[i], a, mx, my, sx, sy, saCos, saSin, deg, wt);
            }

            BeltCommit(ed, "変換適用");
            D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        // ================================================================
        // Undo
        // ================================================================

        private void EnsureBeltUndoStack(BeltProfileEdit ed)
        {
            if (ed.UndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(ed.UndoStackId);   // パネル再生成時の重複ID回避
            ed.UndoCtx   = new BeltProfileUndoContext { Profile = CloneBeltProfile(ed.Points) };
            ed.UndoStack = undo.CreateSubWindowStack(ed.UndoStackId, ed.UndoTitle, ed.UndoCtx);
            ed.UndoStack.OnUndoPerformed += _ => ApplyBeltUndoContext(ed);
            ed.UndoStack.OnRedoPerformed += _ => ApplyBeltUndoContext(ed);
        }

        /// <summary>編集前スナップショットを取得（記録の起点）。</summary>
        private void BeltBegin(BeltProfileEdit ed)
        {
            if (ed.UndoApplying) { ed.EditBefore = null; return; }
            ed.EditBefore = GetUndoController?.Invoke() == null ? null : CloneBeltProfile(ed.Points);
        }

        /// <summary>変化があればサブウィンドウスタックへ記録。</summary>
        private void BeltCommit(BeltProfileEdit ed, string desc)
        {
            var before = ed.EditBefore;
            ed.EditBefore = null;
            if (ed.UndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneBeltProfile(ed.Points);
            if (BeltProfileEquals(before, after)) return;
            EnsureBeltUndoStack(ed);
            if (ed.UndoStack == null) return;
            ed.UndoCtx.Profile = CloneBeltProfile(after);
            ed.UndoStack.Record(new BeltProfileUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(ed.UndoStackId);
        }

        // ================================================================
        // メッシュ⇄断面プロファイル
        // 取り込み元／反映先 = 選択オブジェクト内の2頂点ライン。Z は捨てて XY を使う
        // （回転体・2Dプロファイルと同じ規約）。
        // 断面座標は rung 長で正規化された系なので、取り込み時だけ長辺が 1 になるよう
        // 等方スケールし、AABB の最小角を原点へ寄せる。反映は生データのまま書き出す。
        // ================================================================

        /// <summary>選択オブジェクトの2頂点ラインを断面プロファイルへ取り込む。</summary>
        private void ImportBeltProfileFromMesh(BeltProfileEdit ed)
        {
            if (ed == null) return;

            var mesh = GetSelectedMeshObject?.Invoke();
            if (mesh == null) { SetBeltStatus(T("NoSelectedMesh")); return; }

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);

            List<Vector2> pts = null;
            if (ed.ClosedLoop)
            {
                // 閉ループ断面。複数ループがあれば点数が最多のものを採る。
                var loops = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
                if (loops != null)
                {
                    foreach (var lp in loops)
                    {
                        if (lp?.Points == null || lp.Points.Count < 3) continue;
                        if (pts == null || lp.Points.Count > pts.Count) pts = lp.Points;
                    }
                }
            }
            else
            {
                pts = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
            }

            if (pts == null || pts.Count < 2) { SetBeltStatus(T("NoLinesFound")); return; }

            // 取り込み元を控える。オブジェクトグループが作り直すときに、
            // 同じオブジェクトから同じ読み方で掛け直せるようにするため。
            ed.SourceMasterIndex = ResolveMasterIndexOf(mesh);

            var norm = NormalizeBeltProfile(pts);
            if (norm == null) { SetBeltStatus(T("ProfileDegenerate")); return; }

            BeltBegin(ed);
            ed.Points = norm;
            ed.Sel.Clear(); ed.SelectedIndex = -1;
            BeltCommit(ed, "メッシュ取込");

            SetBeltStatus(T("ImportedPoints", norm.Count));
            D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
        }

        /// <summary>断面プロファイルを2頂点ラインの描画オブジェクトとして反映する（正規化なし）。</summary>
        private void ApplyBeltProfileToMesh(BeltProfileEdit ed)
        {
            if (ed == null) return;

            EnsureBeltProfile(ed);
            if (ed.Points == null || ed.Points.Count < 2) { SetBeltStatus(T("NoLinesFound")); return; }

            string name = string.IsNullOrEmpty(ed.ObjectName) ? "Profile" : ed.ObjectName;
            var mo = LineProfileExtractor.PolylineToLineMesh(ed.Points, name, ed.ClosedLoop);
            if (mo == null || mo.FaceCount == 0) { SetBeltStatus(T("NoLinesFound")); return; }

            ApplyPoseForDirectMeshCreate(mo);

            SetBeltStatus(T("AppliedToMesh", mo.FaceCount));
            SendGeneratedMesh(mo, name);
        }

        /// <summary>
        /// AABB の長辺が 1 になるよう等方スケールし、AABB の最小角を原点へ寄せる。
        /// 実体は LineProfileExtractor.NormalizeToUnitSpan。
        /// 自動検証パネルも同じ規則で取り込むため、規則は 1 箇所に置く。
        /// </summary>
        private static List<Vector2> NormalizeBeltProfile(IReadOnlyList<Vector2> src)
            => LineProfileExtractor.NormalizeToUnitSpan(src);

        // 複数メッシュの連結は Poly_Ling.Ops.MeshObjectAppendOps.Combine へ移設した。

        /// <summary>Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyBeltUndoContext(BeltProfileEdit ed)
        {
            ed.UndoApplying = true;
            try
            {
                ed.Points = CloneBeltProfile(ed.UndoCtx?.Profile);
                ed.Sel.Clear();
                ed.SelectedIndex = -1;
                D();
                RefreshBeltCanvas(ed);
                RefreshBeltPointUI(ed);
            }
            finally { ed.UndoApplying = false; }
        }
    }
}
