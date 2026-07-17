// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool_BeltLoop.cs
// ナイフツール - BeltLoop モード（一意分割）。
// 辺を1回クリックすると、その辺を含むベルト全体を切断する。
//   - 円筒: 一周する閉ループカット。
//   - 三角形に挟まれた梯子: 三角形終端まで走る帯を切断。
// 等分割オフ: クリック位置の比率をベルト全周へ一意伝播して1本。
// 等分割オン: 分割数 N で等分（N-1 本）。

using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool
    {
        private VertexPair _beltHoverEdge;
        private bool       _hasBeltHover;

        // ================================================================
        // クリック / ホバー
        // ================================================================

        private bool HandleBeltClick(ToolContext ctx, Vector2 mousePos)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return false;

            var e = ResolveEdge(ctx, mousePos);
            if (!e.HasValue) return false;

            float ratio = ComputeClickRatio(ctx, mo, e.Value, mousePos);
            var plan = BeltCutResolver.Resolve(mo, e.Value, ratio, e.Value.V1);
            if (!plan.Ok || plan.FaceCuts.Count == 0)
            {
                LastError = T("ErrBeltUnreachable");
                ctx.Repaint?.Invoke();
                return true;
            }

            if (EqualDivide)
                NCutExecutor.Execute(ctx, mo, plan, Divisions);   // N 等分
            else
                LadderCutExecutor.Execute(ctx, mo, plan);         // クリック比率で1本

            ctx.NotifyTopologyChanged?.Invoke();

            LastError = "";
            _hasBeltHover = false;
            _preview.Clear();
            ctx.Repaint?.Invoke();
            return true;
        }

        private void UpdateBeltHover(ToolContext ctx, Vector2 mousePos)
        {
            _preview.Clear();
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return;

            var e = ResolveEdge(ctx, mousePos);
            _hasBeltHover = e.HasValue;
            if (!e.HasValue) return;
            _beltHoverEdge = e.Value;

            float ratio = ComputeClickRatio(ctx, mo, e.Value, mousePos);
            var plan = BeltCutResolver.Resolve(mo, e.Value, ratio, e.Value.V1);
            if (!plan.Ok) return;

            if (EqualDivide)
            {
                // ベルト範囲（rung 群）を強調してプレビュー。
                foreach (var rung in plan.Rungs)
                    _preview.Lines.Add((mo.Vertices[rung.V1].Position, mo.Vertices[rung.V2].Position));
            }
            else
            {
                // 実カット線（クリック比率）をプレビュー。
                foreach (var cut in plan.FaceCuts)
                {
                    bool aV = cut.A.Kind == LadderAnchorKind.Vertex;
                    bool bV = cut.B.Kind == LadderAnchorKind.Vertex;
                    Vector3 pa = aV ? mo.Vertices[cut.A.VertexIndex].Position : RungCutPoint(mo, plan, cut.A.RungEdge);
                    Vector3 pb = bV ? mo.Vertices[cut.B.VertexIndex].Position : RungCutPoint(mo, plan, cut.B.RungEdge);
                    _preview.Lines.Add((pa, pb));
                }
            }
        }

        /// <summary>rung 上の切断点（RungParams のアンカー＋比率）を返す。</summary>
        private Vector3 RungCutPoint(MeshObject mo, LadderCutPlan plan, VertexPair rung)
        {
            float t = 0.5f;
            if (plan.RungParams.TryGetValue(rung, out var rp))
                t = (rp.AnchorVertex == rung.V2) ? (1f - rp.Ratio) : rp.Ratio;
            return Vector3.Lerp(mo.Vertices[rung.V1].Position, mo.Vertices[rung.V2].Position, t);
        }
    }
}
