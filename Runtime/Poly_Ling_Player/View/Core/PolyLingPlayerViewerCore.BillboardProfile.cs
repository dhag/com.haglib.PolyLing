// PolyLingPlayerViewerCore.BillboardProfile.cs
// Player ビューアのコア：線分群の編集（ビルボード上の 2D プロファイル）の
// ハンドラ生成・パネル配線・3D 操作モード・オーバーレイ。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【オーバーレイ】曲線オーバーレイ（UpdateLineCurveOverlay）に相乗りして、
//   対象の線分群の点と、描いている折れ線の最後の点からポインタまでの仮の線分を描く。
//   既存の点は GPU の値（TryGetVertexWorld）。群を作る前の始点だけは頂点が無いので、
//   ローカル座標を DisplayWorldMatrix で移す（ハンドルと同じ扱い）。

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

        private void ShowBillboardProfilePanel()
        {
            ShowCategory1Panel(InteractionMode.BillboardProfile);
            RefreshBillboardProfile();
        }

        /// <summary>パネルとオーバーレイを更新する。</summary>
        private void RefreshBillboardProfile()
        {
            _billboardProfileSubPanel?.Refresh();
            UpdateLineCurveOverlay();
        }

        /// <summary>線分群の編集モードのとき、対象の点と仮の線分を曲線オーバーレイへ足す。</summary>
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

            // 仮の線分：描いている折れ線の最後の点 → ポインタ
            var chain = h.ChainPoints;
            if (chain.Count > 0 && h.HoverScreen.HasValue && h.TargetMasterIndex == model.ActiveMeshIndex)
            {
                // 群を作った後は最後の点は頂点なので GPU の値。作る前の始点だけ CPU で移す。
                Vector3 lastWorld;
                int gi = h.ChainGroupIndex;
                if (gi >= 0 && mo.LineGroups != null && gi < mo.LineGroups.Count
                    && _viewportManager.TryGetVertexWorld(model, mc, mo.LineGroups[gi].EndVertex, out var ew))
                    lastWorld = ew;
                else
                    lastWorld = mc.DisplayWorldMatrix.MultiplyPoint3x4(chain[chain.Count - 1]);
                data.Preview = new[] { ctx.WorldToScreen(lastWorld), h.HoverScreen.Value };
            }

            AppendProfileEditOverlay(model, mc, mo, ctx, h, ref data);

            // 自由曲線：確定前の点・手描き・ポインタまで（頂点がまだ無いので DisplayWorldMatrix で移す）
            if (h.Mode == BillboardProfileToolHandler.SubMode.Freeform
                && (h.FreeformPoints.Count > 0 || h.FreeformStroke.Count > 0))
            {
                Matrix4x4 dm = mc.DisplayWorldMatrix;
                var line = new List<Vector2>();
                foreach (var p in h.FreeformPoints) line.Add(ctx.WorldToScreen(dm.MultiplyPoint3x4(p)));
                foreach (var p in h.FreeformStroke) line.Add(ctx.WorldToScreen(dm.MultiplyPoint3x4(p)));
                if (h.FreeformStroke.Count == 0 && h.HoverScreen.HasValue) line.Add(h.HoverScreen.Value);
                data.DragPreview ??= new List<Vector2[]>();
                data.DragPreview.Add(line.ToArray());
                foreach (var p in h.FreeformPoints) data.Points.Add(ctx.WorldToScreen(dm.MultiplyPoint3x4(p)));
            }
        }

        /// <summary>
        /// Profile（A）の選択点・ドラッグ中の仮の形・矩形選択の枠。
        /// 点は GPU の値、動かした量とハンドルのずれは DisplayWorldMatrix で向きだけ移す。
        /// </summary>
        private void AppendProfileEditOverlay(
            ModelContext model, MeshContext mc, MeshObject mo, ToolContext ctx,
            BillboardProfileToolHandler h, ref PlayerViewportPanel.LineCurveData data)
        {
            if (h.Mode != BillboardProfileToolHandler.SubMode.Profile || mo.LineGroups == null) return;
            Matrix4x4 dm = mc.DisplayWorldMatrix;

            data.SelectedPoints = new List<Vector2>();
            foreach (var (g, p) in h.SelectedPoints)
            {
                if (g < 0 || g >= mo.LineGroups.Count || p < 0 || p >= mo.LineGroups[g].Order.Count) continue;
                if (_viewportManager.TryGetVertexWorld(model, mc, mo.LineGroups[g].Order[p], out var w))
                    data.SelectedPoints.Add(ctx.WorldToScreen(w));
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
