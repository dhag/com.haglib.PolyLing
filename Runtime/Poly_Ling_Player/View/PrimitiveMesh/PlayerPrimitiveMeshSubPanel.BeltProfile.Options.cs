// PlayerPrimitiveMeshSubPanel.BeltProfile.Options.cs
// 図形生成サブパネル：ベルトの向き補正・スプライン・厚み付けの UI と適用、
// 取り込み元オブジェクトの選択行。
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
        // スプライン分割
        // ================================================================

        /// <summary>スプライン分割の設定UIを組み立てる。</summary>
        /// <summary>梯子の向き補正UIを組み立てる。</summary>
        private void BuildBeltOrientUI(VisualElement c, BeltOrientOption opt)
        {
            if (c == null || opt == null) return;

            c.Add(PlayerIoUiKit.SectionLabel(T("BeltOrient")));

            var hint = new Label(T("BeltOrientHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            c.Add(TR(T("BeltSwapSides"),    () => opt.SwapSides,    v => { opt.SwapSides    = v; D(); }));
            c.Add(TR(T("BeltReverseOrder"), () => opt.ReverseOrder, v => { opt.ReverseOrder = v; D(); }));
        }

        /// <summary>
        /// 梯子の向き補正を適用したベルトを返す。取り込み済みデータは変更しない。
        /// 左右入替・rung順反転はどちらも巻き順の意味を反転させるため、
        /// ステップ法線を元メッシュと同じ向きに保つには FlipWinding も同時に反転する必要がある。
        /// 両方ONなら2回反転して元に戻る。
        ///
        /// 左右入替では段の Left/Right の意味も入れ替わるため、段番号を反転させる。
        /// これをしないと、隣り合う段が共有するレールの補間パラメータ t が食い違い、
        /// 共有レールが溶接されなくなる。
        /// </summary>
        private static BeltSnapshot ApplyBeltOrient(BeltSnapshot belt, BeltOrientOption opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (opt == null || opt.IsIdentity) return belt;

            var left  = new List<Vector3>(belt.Left);
            var right = new List<Vector3>(belt.Right);
            var start = belt.StartPoint;
            var end   = belt.EndPoint;
            bool flip = belt.FlipWinding;
            int  rowCount = Mathf.Max(1, belt.RowCount);
            int  rowIndex = Mathf.Clamp(belt.RowIndex, 0, rowCount - 1);

            if (opt.SwapSides)
            {
                var tmp = left; left = right; right = tmp;
                flip = !flip;
                rowIndex = rowCount - 1 - rowIndex;
            }

            if (opt.ReverseOrder)
            {
                left.Reverse();
                right.Reverse();
                var tmp = start; start = end; end = tmp;
                flip = !flip;
            }

            return new BeltSnapshot
            {
                Left        = left,
                Right       = right,
                Closed      = belt.Closed,
                FlipWinding = flip,
                HeightScale = belt.HeightScale,
                StartPoint  = start,
                EndPoint    = end,
                GroupId     = belt.GroupId,
                RowIndex    = rowIndex,
                RowCount    = rowCount,
            };
        }

        private void BuildBeltSplineUI(VisualElement c, BeltSplineOption opt)
        {
            c.Add(PlayerIoUiKit.Divider());

            // 既定は閉じる。使うときだけ開く項目のため。
            var fold = new Foldout { text = T("BeltSpline"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            c.Add(fold);

            var hint = new Label(T("BeltSplineHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            f.Add(hint);

            f.Add(TR(T("BeltSplineEnable"), () => opt.Enabled,  v => { opt.Enabled  = v; D(); }));
            f.Add(IR(T("BeltSplineSegs"), BeltSplineOptions.SegmentsMin, BeltSplineOptions.SegmentsMax, () => opt.Segments,  v => { opt.Segments  = v; D(); }));
            f.Add(TR(T("BeltSplineUseFirst"), () => opt.UseFirst, v => { opt.UseFirst = v; D(); }));
            f.Add(TR(T("BeltSplineUseLast"),  () => opt.UseLast,  v => { opt.UseLast  = v; D(); }));
            f.Add(IR(T("BeltSplineTrimStart"), BeltSplineOptions.TrimMin, BeltSplineOptions.TrimMax, () => opt.TrimStart, v => { opt.TrimStart = v; D(); }));
            f.Add(IR(T("BeltSplineTrimEnd"), BeltSplineOptions.TrimMin, BeltSplineOptions.TrimMax, () => opt.TrimEnd,   v => { opt.TrimEnd   = v; D(); }));
        }

        /// <summary>
        /// スプライン分割を適用したベルトを返す。無効・閉じた梯子・補間不能なら元をそのまま返す。
        /// 取り込み済みデータは変更しない。
        /// </summary>
        private static BeltSnapshot ApplyBeltSpline(BeltSnapshot belt, BeltSplineOption opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (opt == null || !opt.Enabled)   return belt;
            if (belt.Closed)                   return belt;

            if (!BeltSplineSubdivider.Subdivide(
                    belt.Left, belt.Right, belt.StartPoint, belt.EndPoint,
                    opt.Segments, opt.UseFirst, opt.UseLast, opt.TrimStart, opt.TrimEnd,
                    out var left, out var right))
                return belt;

            return new BeltSnapshot
            {
                Left        = left,
                Right       = right,
                Closed      = false,
                FlipWinding = belt.FlipWinding,
                HeightScale = belt.HeightScale,
                StartPoint  = belt.StartPoint,
                EndPoint    = belt.EndPoint,
                GroupId     = belt.GroupId,
                RowIndex    = belt.RowIndex,
                RowCount    = belt.RowCount,
            };
        }

        // ================================================================
        // 生成ユーティリティ
        // ================================================================

        // src の頂点・面を dst へ連結する処理は Poly_Ling.Ops.MeshObjectAppendOps.Append へ移設した。

        // ================================================================
        // 厚み付け（ソリッド化）共通部：フリル／パイプで共用
        // ================================================================

        /// <summary>角処理(ベベル)UI 要素。厚み/分割数に応じて表示切替するため保持する。</summary>
        private sealed class SolidifyUI
        {
            public VisualElement EdgeLabel, FrontSeg, FrontSize, BackSeg, BackSize, Inward;
        }

        /// <summary>角処理(ベベル)UI の表示を厚み/分割数に応じて更新する。</summary>
        private static void UpdateSolidifyVis(SolidifyUI ui, float thickness, int segFront, int segBack)
        {
            if (ui == null || ui.EdgeLabel == null) return;
            bool thick = thickness > 0.001f;
            ui.EdgeLabel.style.display = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.FrontSeg.style.display  = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.FrontSize.style.display = (thick && segFront > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            ui.BackSeg.style.display   = thick ? DisplayStyle.Flex : DisplayStyle.None;
            ui.BackSize.style.display  = (thick && segBack > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            ui.Inward.style.display    = (thick && (segFront > 0 || segBack > 0)) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 面群全体を厚み付けした立体へ差し替える。厚みが 0 または生成失敗時は part をそのまま返す。
        /// 面群が閉じている場合は孤立エッジが無いため側面は生成されず、外殻と内殻の中空になる。
        /// </summary>
        private static MeshObject ApplySolidify(
            MeshObject part, float thickness, int segFront, int segBack,
            float edgeFront, float edgeBack, bool edgeInward, string meshName)
        {
            if (part == null || part.FaceCount == 0 || thickness <= 0.0001f) return part;

            var faces = new List<int>(part.FaceCount);
            for (int i = 0; i < part.FaceCount; i++) faces.Add(i);

            var r = FaceGroupSolidifier.Build(part, faces, new FaceGroupSolidifier.Params
            {
                Thickness     = thickness,
                SegmentsFront = segFront,
                SegmentsBack  = segBack,
                EdgeSizeFront = edgeFront,
                EdgeSizeBack  = edgeBack,
                EdgeInward    = edgeInward,
            }, meshName);

            return r.Ok ? r.Mesh : part;
        }

        // ================================================================
        // 対象オブジェクト選択（自動検索・配置元で共用）
        // ================================================================

        /// <summary>描画オブジェクト選択の状態。</summary>
        private sealed class MeshSourcePick
        {
            public List<(string Label, MeshObject Mesh)> Candidates = new List<(string, MeshObject)>();
            public int           Index = -1;
            public DropdownField Dropdown;

            public MeshObject Current =>
                (Index >= 0 && Index < Candidates.Count) ? Candidates[Index].Mesh : null;
        }

        /// <summary>描画オブジェクトのドロップダウンと再取得ボタンを組み立てる。</summary>
        private void BuildMeshSourceRow(VisualElement c, MeshSourcePick pick, string sectionLabel)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(sectionLabel));

            pick.Dropdown = new DropdownField(new List<string> { T("PlaceNoSource") }, 0);
            pick.Dropdown.RegisterValueChangedCallback(_ =>
            {
                pick.Index = pick.Dropdown.index - 1;   // 先頭は「(未選択)」
                D();
            });
            c.Add(pick.Dropdown);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            SB(row, T("PlaceRefresh"), () => RefreshMeshSourcePick(pick));
            c.Add(row);

            RefreshMeshSourcePick(pick);
        }

        private void RefreshMeshSourcePick(MeshSourcePick pick)
        {
            pick.Candidates = GetDrawableMeshList?.Invoke() ?? new List<(string, MeshObject)>();

            var choices = new List<string> { T("PlaceNoSource") };
            foreach (var e in pick.Candidates) choices.Add(e.Label);

            if (pick.Index >= pick.Candidates.Count) pick.Index = -1;

            if (pick.Dropdown != null)
            {
                pick.Dropdown.choices = choices;
                pick.Dropdown.index   = pick.Index + 1;
            }
        }

        // ================================================================
        // 対象オブジェクト複数選択（配置の配置元で使用）
        // ================================================================

        // MeshSourceMultiPick は Runtime/Poly_Ling_Player/View/Common/MeshSourceMultiPick.cs へ移設した。
        // 一覧の組み立て・再取得は下の 2 メソッドがパネル側の依存を持つため、ここに残す。

        /// <summary>描画オブジェクトのチェックボックス一覧と再取得ボタンを組み立てる。</summary>
        private void BuildMeshSourceMultiRow(VisualElement c, MeshSourceMultiPick pick, string sectionLabel)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(sectionLabel));

            pick.ListContainer = new VisualElement();
            pick.ListContainer.style.marginBottom = 2;
            c.Add(pick.ListContainer);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            SB(row, T("PlaceRefresh"), () => RefreshMeshSourceMultiPick(pick));
            c.Add(row);

            RefreshMeshSourceMultiPick(pick);
        }

        private void RefreshMeshSourceMultiPick(MeshSourceMultiPick pick)
        {
            pick.Candidates = GetDrawableMeshEntryList?.Invoke()
                              ?? new List<(string, int, MeshObject)>();

            // 一覧から消えたラベルの選択は捨てる。
            var alive = new HashSet<string>();
            foreach (var e in pick.Candidates) alive.Add(e.Label);
            pick.SelectedLabels.RemoveWhere(l => !alive.Contains(l));

            if (pick.ListContainer == null) return;
            pick.ListContainer.Clear();

            if (pick.Candidates.Count == 0)
            {
                var empty = new Label(T("PlaceNoSource"));
                empty.style.fontSize = 10;
                pick.ListContainer.Add(empty);
                return;
            }

            foreach (var e in pick.Candidates)
            {
                string label = e.Label;
                var tog = new Toggle(label) { value = pick.SelectedLabels.Contains(label) };
                tog.style.fontSize = 10;
                tog.RegisterValueChangedCallback(ev =>
                {
                    if (ev.newValue) pick.SelectedLabels.Add(label);
                    else             pick.SelectedLabels.Remove(label);
                    D();
                });
                pick.ListContainer.Add(tog);
            }
        }
    }
}
