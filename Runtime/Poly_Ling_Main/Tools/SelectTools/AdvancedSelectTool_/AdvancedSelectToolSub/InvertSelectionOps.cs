// InvertSelectionOps.cs
// 現在の選択を反転する（クリック非依存）。
// SelectionState.Mode で有効なビットのみ反転し、無効なビットの集合は一切触らない。
// Runtime/Editor 共通。#if UNITY_EDITOR 不使用。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 選択反転。
    ///
    /// 【可視性について】
    /// Poly_Ling.Selection.SelectionOperations は IVisibilityProvider を持つが、
    /// その実体は PolyLingCore が生成する経路にしか存在せず、Player の
    /// AdvancedSelectToolHandler 経路では null になる。よってここでは
    /// MeshObject の面フラグ（FaceFlags.Hidden）を直接見る。
    /// 頂点フラグには非表示に相当するものが無いため、頂点は全件が反転対象。
    /// </summary>
    public static class InvertSelectionOps
    {
        /// <summary>
        /// 有効な選択モードの集合を反転する。
        /// </summary>
        /// <param name="state">反転対象の選択状態</param>
        /// <param name="mesh">対象メッシュ</param>
        /// <param name="topology">辺/面/線の全列挙に使うトポロジーキャッシュ。null なら辺/面/線は反転しない</param>
        /// <returns>いずれかの集合を書き換えたら true</returns>
        public static bool Invert(SelectionState state, MeshObject mesh, TopologyCache topology)
        {
            if (state == null || mesh == null) return false;

            var mode = state.Mode;
            bool changed = false;

            // ── 頂点 ────────────────────────────────────────────────
            if (mode.Has(MeshSelectMode.Vertex))
            {
                var next = new HashSet<int>();
                int count = mesh.VertexCount;
                for (int i = 0; i < count; i++)
                {
                    if (!state.Vertices.Contains(i)) next.Add(i);
                }
                changed |= !state.Vertices.SetEquals(next);
                state.Vertices.Clear();
                state.Vertices.UnionWith(next);
            }

            // ── 辺 ──────────────────────────────────────────────────
            if (mode.Has(MeshSelectMode.Edge) && topology != null)
            {
                var next = new HashSet<VertexPair>();
                foreach (var pair in topology.AllEdgePairs)
                {
                    if (!state.Edges.Contains(pair)) next.Add(pair);
                }
                changed |= !state.Edges.SetEquals(next);
                state.Edges.Clear();
                state.Edges.UnionWith(next);
            }

            // ── 面（非表示面は除外）─────────────────────────────────
            if (mode.Has(MeshSelectMode.Face) && topology != null)
            {
                var next = new HashSet<int>();
                foreach (int idx in topology.RealFaceIndices)
                {
                    if (idx < 0 || idx >= mesh.FaceCount) continue;
                    if (mesh.Faces[idx] != null && mesh.Faces[idx].IsHidden) continue;
                    if (!state.Faces.Contains(idx)) next.Add(idx);
                }
                changed |= !state.Faces.SetEquals(next);
                state.Faces.Clear();
                state.Faces.UnionWith(next);
            }

            // ── 補助線分 ────────────────────────────────────────────
            if (mode.Has(MeshSelectMode.Line) && topology != null)
            {
                var next = new HashSet<int>();
                foreach (int idx in topology.AuxLineIndices)
                {
                    if (idx < 0 || idx >= mesh.FaceCount) continue;
                    if (!state.Lines.Contains(idx)) next.Add(idx);
                }
                changed |= !state.Lines.SetEquals(next);
                state.Lines.Clear();
                state.Lines.UnionWith(next);
            }

            return changed;
        }
    }
}
