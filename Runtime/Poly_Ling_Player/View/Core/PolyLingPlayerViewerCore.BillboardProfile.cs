// PolyLingPlayerViewerCore.BillboardProfile.cs
// Player ビューアのコア：線分群の編集（ビルボード上の 2D プロファイル）の
// ハンドラ生成・パネル配線・3D 操作モード・オーバーレイ。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【オーバーレイ】
//   曲線オーバーレイ（UpdateLineCurveOverlay）に相乗りして、対象の線分群の点と
//   Profile のドラッグ中の仮の形・矩形選択の枠を描く。
//   候補（吸着・選択・挿入）・確定前の点・描いている線・選択中の点は、どのサブモードも
//   面追加と同じ描画経路（UpdateAddFacePreview）で描く（UpdateBillboardEditPreview）。
//   既存の点は GPU の値（TryGetVertexWorld）。まだ頂点が無い点（群を作る前の始点・
//   自由曲線の確定前の点・挿入位置）はローカル座標を DisplayWorldMatrix で移す。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private BillboardProfileToolHandler     _billboardProfileHandler;
        private PlayerBillboardProfileSubPanel  _billboardProfileSubPanel;

        /// <summary>ハンドラとパネルを作る（BuildVertexToolPanels から呼ぶ）。</summary>
        private void BuildBillboardProfile()
        {
            _billboardProfileHandler = new BillboardProfileToolHandler
            {
                GetProject     = () => ActiveProject,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetVertexWorld = (m, mc, vi) =>
                    _viewportManager.TryGetVertexWorld(m, mc, vi, out var w) ? (Vector3?)w : null,
                Dispatch  = cmd => DispatchHost(cmd),
                OnChanged = RefreshBillboardProfile,
                // 吸着・選択は面追加と同じ GPU ホバー。ポインタ移動では候補表示だけ更新する。
                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                OnHoverChanged  = UpdateBillboardEditPreview,
                OnModeChanged   = OnBillboardProfileModeChanged,
                // 描画オブジェクトが無い・選ばれていないときは面追加と同じく「New Mesh」を作る。
                EnsureDrawableMesh = () =>
                {
                    var ensure = _addFaceHandler?.EnsureDrawableMesh;
                    return ensure != null
                        ? ensure()
                        : ActiveProject?.CurrentModel?.FirstDrawableMeshContext != null;
                },
            };

            _billboardProfileSubPanel = new PlayerBillboardProfileSubPanel
            {
                Surface = ToolSurface,
                GetTargetInfo = () =>
                {
                    var mc = ActiveProject?.CurrentModel?.ActiveMeshContext;
                    if (mc?.MeshObject == null || mc.Type != MeshType.Mesh) return ("", 0);
                    return (mc.Name, mc.MeshObject.LineGroups?.Count ?? 0);
                },
                OnFinish = () => _billboardProfileHandler?.FinishChain(),
            };
            _billboardProfileSubPanel.Build(_layoutRoot.BillboardProfileSection);
        }

        /// <summary>「線分の追加・編集」ボタン。線分サブモードで開く。</summary>
        private void ShowBillboardProfilePanel()
        {
            var h = _billboardProfileHandler;
            if (h != null && h.Mode != BillboardProfileToolHandler.SubMode.Line)
            {
                h.FinishChain();
                h.Mode = BillboardProfileToolHandler.SubMode.Line;
            }
            ShowCategory1Panel(InteractionMode.BillboardProfile);
            RefreshBillboardProfile();
        }

        /// <summary>サブモードが変わった：ホバー種別（Profile は頂点＋線分）を選び直し、表示を描き直す。</summary>
        private void OnBillboardProfileModeChanged()
        {
            if (_interactionMode == InteractionMode.BillboardProfile)
            {
                _toolSelectModeOverride = ResolveToolSelectModeOverride(_interactionMode);
                ApplySelectMode();
            }
            RefreshBillboardProfile();
        }

        /// <summary>パネルとオーバーレイを更新する。</summary>
        private void RefreshBillboardProfile()
        {
            _billboardProfileSubPanel?.Refresh();
            UpdateLineCurveOverlay();
            UpdateBillboardEditPreview();
        }

        /// <summary>
        /// 候補・確定前の点・描いている線・選択中の点を、面追加と同じ描画経路（UpdateAddFacePreview）で描く。
        ///   候補：頂点・ハンドルに当たればシアン大＋輪、当たらなければ（ポインタ・挿入位置）半透明の黄。
        ///   線：黄。確定前の点：シアンの四角。閉じられる始点・選択中の点：橙の大きな四角＋輪。
        /// 面追加モードの UpdateAddFaceOverlay からも呼ばれる（視点変更時の再投影）。
        /// </summary>
        private void UpdateBillboardEditPreview()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var h = _billboardProfileHandler;
            if (_interactionMode != InteractionMode.BillboardProfile || h == null)
            {
                if (_interactionMode != InteractionMode.AddFace) panel.HideAddFacePreview();
                return;
            }

            var ctx   = _viewportManager.GetCurrentToolContext(_activeViewport);
            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            var mo    = mc?.MeshObject;
            if (ctx == null || mo == null || mc.Type != MeshType.Mesh) { panel.HideAddFacePreview(); return; }

            var p = new BillboardPreview { Ctx = ctx, Model = model, Mc = mc, Mo = mo, PanelH = ctx.PreviewRect.height };
            switch (h.Mode)
            {
                case BillboardProfileToolHandler.SubMode.Line:     BuildLinePreview(h, p);     break;
                case BillboardProfileToolHandler.SubMode.Profile:  BuildProfilePreview(h, p);  break;
                case BillboardProfileToolHandler.SubMode.Freeform: BuildFreeformPreview(h, p); break;
            }
            panel.UpdateAddFacePreview(p.Pts, p.PreviewPts, p.PreviewSnap, p.Lines, p.Highlight, null, p.HighlightSet);
        }

        /// <summary>UpdateAddFacePreview へ渡す一式（面追加と同じ座標：ViewerCore 側で h - y）。</summary>
        private class BillboardPreview
        {
            public ToolContext  Ctx;
            public ModelContext Model;
            public MeshContext  Mc;
            public MeshObject   Mo;
            public float        PanelH;
            public readonly List<Vector2> Pts         = new List<Vector2>();
            public readonly List<Vector2> PreviewPts  = new List<Vector2>();
            public readonly List<bool>    PreviewSnap = new List<bool>();
            public readonly List<(Vector2, Vector2)> Lines = new List<(Vector2, Vector2)>();
            public int Highlight = -1;
            public HashSet<int> HighlightSet;

            /// <summary>WorldToScreen の値（Y=0 上）→ 面追加の描画経路の座標。</summary>
            public Vector2 FromScreen(Vector2 s) => new Vector2(s.x, PanelH - s.y);
            public Vector2 FromWorld(Vector3 w) => FromScreen(Ctx.WorldToScreen(w));
            /// <summary>まだ頂点が無い点（ローカル）。DisplayWorldMatrix で移す。</summary>
            public Vector2 FromLocal(Vector3 l) => FromWorld(Mc.DisplayWorldMatrix.MultiplyPoint3x4(l));
            public void AddPreview(Vector2 pt, bool snapped) { PreviewPts.Add(pt); PreviewSnap.Add(snapped); }
        }

        /// <summary>頂点の表示位置（GPU）。</summary>
        private bool TryVertexPanel(BillboardPreview p, int vi, out Vector2 pt)
        {
            pt = default;
            if (vi < 0 || !_viewportManager.TryGetVertexWorld(p.Model, p.Mc, vi, out var w)) return false;
            pt = p.FromWorld(w);
            return true;
        }

        /// <summary>吸着候補（頂点ならシアン、無ければ編集面上のポインタを黄）。無ければ null。</summary>
        private Vector2? SnapOrPointer(BillboardProfileToolHandler h, BillboardPreview p, out bool snapped)
        {
            snapped = false;
            if (TryVertexPanel(p, h.HoverVertex, out var v)) { snapped = true; return v; }
            if (h.HoverScreen.HasValue
                && BillboardProfileToolHandler.TryScreenToLocal(p.Mc, p.Ctx, h.HoverScreen.Value, out _))
                return p.FromScreen(h.HoverScreen.Value);
            return null;
        }

        // ── Line ──
        private void BuildLinePreview(BillboardProfileToolHandler h, BillboardPreview p)
        {
            var preview = SnapOrPointer(h, p, out bool snapped);
            var mo = p.Mo;

            var chain = h.ChainPoints;
            if (chain.Count > 0 && h.TargetMasterIndex == p.Model.ActiveMeshIndex)
            {
                int gi = h.ChainGroupIndex;
                Vector2 last;
                if (!(gi >= 0 && mo.LineGroups != null && gi < mo.LineGroups.Count
                      && TryVertexPanel(p, mo.LineGroups[gi].EndVertex, out last)))
                    last = p.FromLocal(chain[chain.Count - 1]);
                if (gi < 0) p.Pts.Add(last);                 // 群を作る前の始点（面追加の 1 点目と同じ）
                if (preview.HasValue) p.Lines.Add((last, preview.Value));
            }

            if (h.CloseToStart && h.ChainGroupIndex >= 0 && h.ChainGroupIndex < mo.LineGroups.Count
                && TryVertexPanel(p, mo.LineGroups[h.ChainGroupIndex].StartVertex, out var st))
            {
                p.Pts.Add(st);
                p.Highlight = p.Pts.Count - 1;
            }

            if (preview.HasValue) p.AddPreview(preview.Value, snapped);
        }

        // ── Profile ──
        private void BuildProfilePreview(BillboardProfileToolHandler h, BillboardPreview p)
        {
            var mo = p.Mo;
            if (mo.LineGroups == null) return;

            // 選択中の点（面追加の強調表示）
            p.HighlightSet = new HashSet<int>();
            foreach (var (g, k) in h.SelectedPoints)
            {
                if (g < 0 || g >= mo.LineGroups.Count || k < 0 || k >= mo.LineGroups[g].Order.Count) continue;
                if (!TryVertexPanel(p, mo.LineGroups[g].Order[k], out var s)) continue;
                p.Pts.Add(s);
                p.HighlightSet.Add(p.Pts.Count - 1);
            }

            // 挿入待ちの点（押したまま動かしている間も選択中と同じ強調）
            var pi = h.PendingInsert;
            if (pi.HasValue)
            {
                p.Pts.Add(p.FromLocal(pi.Value.Local));
                p.HighlightSet.Add(p.Pts.Count - 1);
                return;
            }

            // ドラッグ中は候補を出さない（仮の形は曲線オーバーレイが描く）
            if (h.PointDragDelta.HasValue || h.HandleDrag.HasValue || h.Marquee.HasValue) return;

            switch (h.ProfileHoverType)
            {
                case BillboardProfileToolHandler.ProfileHoverKind.Point:
                    if (TryVertexPanel(p, h.ProfileHoverVertex, out var v)) p.AddPreview(v, true);
                    break;

                case BillboardProfileToolHandler.ProfileHoverKind.Handle:
                {
                    var hd = h.ProfileHoverHandle.Value;
                    if (hd.G >= mo.LineGroups.Count) break;
                    var g = mo.LineGroups[hd.G];
                    if (!g.HasHandles || hd.P >= g.Order.Count) break;
                    if (!_viewportManager.TryGetVertexWorld(p.Model, p.Mc, g.Order[hd.P], out var w)) break;
                    var off = hd.IsOut ? g.PointHandles[hd.P].OutOffset : g.PointHandles[hd.P].InOffset;
                    p.AddPreview(p.FromWorld(w + p.Mc.DisplayWorldMatrix.MultiplyVector(off)), true);
                    break;
                }

                case BillboardProfileToolHandler.ProfileHoverKind.Chord:
                {
                    var c = h.ProfileHoverChord.Value;
                    var line = h.ChordScreen(p.Model, p.Mc, p.Ctx, c.G, c.P);
                    if (line != null)
                        for (int i = 1; i < line.Count; i++)
                            p.Lines.Add((p.FromScreen(line[i - 1]), p.FromScreen(line[i])));
                    p.AddPreview(p.FromLocal(c.Local), false);
                    break;
                }
            }
        }

        // ── Freeform ──
        private void BuildFreeformPreview(BillboardProfileToolHandler h, BillboardPreview p)
        {
            var placed = new List<Vector2>();
            foreach (var l in h.FreeformPoints) placed.Add(p.FromLocal(l));
            p.Pts.AddRange(placed);
            for (int i = 1; i < placed.Count; i++) p.Lines.Add((placed[i - 1], placed[i]));

            var stroke = h.FreeformStroke;
            if (stroke.Count > 0)
            {
                // 手描き中：最後の確定点から手描きの軌跡を黄の線で
                Vector2 prev = placed.Count > 0 ? placed[placed.Count - 1] : p.FromLocal(stroke[0]);
                for (int i = 0; i < stroke.Count; i++)
                {
                    var s = p.FromLocal(stroke[i]);
                    p.Lines.Add((prev, s));
                    prev = s;
                }
                return;
            }

            var preview = SnapOrPointer(h, p, out bool snapped);
            if (h.FreeformCloseToStart) p.Highlight = 0;
            if (!preview.HasValue) return;
            if (placed.Count > 0) p.Lines.Add((placed[placed.Count - 1], preview.Value));
            p.AddPreview(preview.Value, snapped);
        }

        /// <summary>線分群の編集モードのとき、対象の点と Profile の仮の形を曲線オーバーレイへ足す。</summary>
        private void AppendBillboardProfileOverlay(
            ModelContext model, ToolContext ctx, ref PlayerViewportPanel.LineCurveData data)
        {
            if (_interactionMode != InteractionMode.BillboardProfile) return;
            var h = _billboardProfileHandler;
            if (h == null || model == null || ctx == null) return;

            var mc = model.ActiveMeshContext;
            var mo = mc?.MeshObject;
            if (mo == null || mc.Type != MeshType.Mesh) return;

            // 対象の線分群の点（GPU の表示位置）
            data.Points = new List<Vector2>();
            var seen = new HashSet<int>();
            if (mo.LineGroups != null)
                foreach (var g in mo.LineGroups)
                {
                    if (g?.Order == null) continue;
                    foreach (int vi in g.Order)
                        if (seen.Add(vi) && _viewportManager.TryGetVertexWorld(model, mc, vi, out var w))
                            data.Points.Add(ctx.WorldToScreen(w));
                }

            // 候補・確定前の点・描いている線・選択中の点は UpdateBillboardEditPreview が
            // 面追加と同じ描画経路で描く。

            AppendProfileEditOverlay(model, mc, mo, ctx, h, ref data);
        }

        /// <summary>
        /// Profile（A）のドラッグ中の仮の形・矩形選択の枠。
        /// 点は GPU の値、動かした量とハンドルのずれは DisplayWorldMatrix で向きだけ移す。
        /// </summary>
        private void AppendProfileEditOverlay(
            ModelContext model, MeshContext mc, MeshObject mo, ToolContext ctx,
            BillboardProfileToolHandler h, ref PlayerViewportPanel.LineCurveData data)
        {
            if (h.Mode != BillboardProfileToolHandler.SubMode.Profile || mo.LineGroups == null) return;
            Matrix4x4 dm = mc.DisplayWorldMatrix;

            // 挿入待ち：その群を点を入れた後の形で描く（点ドラッグ中と同じ描き方）。
            var pi = h.PendingInsert;
            if (pi.HasValue && pi.Value.G < mo.LineGroups.Count)
            {
                var g = mo.LineGroups[pi.Value.G];
                var line = new List<Vector2>();
                Vector2 ins = ctx.WorldToScreen(dm.MultiplyPoint3x4(pi.Value.Local));
                for (int k = 0; k < g.Order.Count; k++)
                {
                    if (k == pi.Value.At) line.Add(ins);
                    if (_viewportManager.TryGetVertexWorld(model, mc, g.Order[k], out var w))
                        line.Add(ctx.WorldToScreen(w));
                }
                if (pi.Value.At >= g.Order.Count) line.Add(ins);
                if (g.Closed && line.Count > 0) line.Add(line[0]);
                data.DragPreview = new List<Vector2[]> { line.ToArray() };
            }

            // 点ドラッグ：選択点を含む群の弦を、動かした後の形で描く。
            var delta = h.PointDragDelta;
            if (delta.HasValue)
            {
                Vector3 dw = dm.MultiplyVector(delta.Value);
                var moved = new HashSet<int>();
                var groups = new HashSet<int>();
                foreach (var (g, p) in h.SelectedPoints)
                {
                    if (g < 0 || g >= mo.LineGroups.Count || p >= mo.LineGroups[g].Order.Count) continue;
                    moved.Add(mo.LineGroups[g].Order[p]);
                    groups.Add(g);
                }
                data.DragPreview = new List<Vector2[]>();
                foreach (int gi in groups)
                {
                    var g = mo.LineGroups[gi];
                    var line = new List<Vector2>();
                    foreach (int vi in g.Order)
                        if (_viewportManager.TryGetVertexWorld(model, mc, vi, out var w))
                            line.Add(ctx.WorldToScreen(moved.Contains(vi) ? w + dw : w));
                    if (g.Closed && line.Count > 0) line.Add(line[0]);
                    data.DragPreview.Add(line.ToArray());
                }
            }

            // ハンドルドラッグ：点 → 新しいハンドル先
            var hd = h.HandleDrag;
            if (hd.HasValue && hd.Value.G < mo.LineGroups.Count)
            {
                var g = mo.LineGroups[hd.Value.G];
                if (hd.Value.P < g.Order.Count
                    && _viewportManager.TryGetVertexWorld(model, mc, g.Order[hd.Value.P], out var w))
                {
                    data.DragPreview = new List<Vector2[]>
                    {
                        new[] { ctx.WorldToScreen(w), ctx.WorldToScreen(w + dm.MultiplyVector(hd.Value.Offset)) },
                    };
                }
            }

            data.Marquee = h.Marquee;
        }
    }
}
