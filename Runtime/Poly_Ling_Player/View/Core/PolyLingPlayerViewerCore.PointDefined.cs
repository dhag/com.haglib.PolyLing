// PolyLingPlayerViewerCore.PointDefined.cs
// Player ビューアのコア：点指定図形（高度な図形）のハンドラ生成・パネル配線・
// 3D操作モードの切り替え・オーバーレイ。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【3D操作モード】
//   「高度な図形」は PrimitivePlace（配置ギズモ）で開く。点指定図形を選んでいる間だけ
//   PointDefinedPrimitive に切り替え、クリックを PointDefinedToolHandler へ渡す。
//   図形を選び直すたびにパネルが OnShapeSelected を呼ぶ。
//
// 【プレビューの更新契機】
//   点の変化       → ハンドラの OnPointsChanged
//   パラメータ変化 → パネルの OnPointDefinedRequestChanged
//   カメラの変化   → カメラ変更の通知でオーバーレイ更新（UpdatePointDefinedOverlay）が走り、
//                    プレビューを作ったときと視線方向が違えばパネルに作り直させる。
//   毎フレームの監視はしない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private PointDefinedToolHandler _pointDefinedHandler;

        /// <summary>共有できる既存経路の強調色。</summary>
        private static readonly Color PointDefinedPathColor  = new Color(0.25f, 1.00f, 0.35f);
        /// <summary>編集対象の頂点（番号を再利用する）へ吸着した点。</summary>
        private static readonly Color PointDefinedReuseColor = new Color(0.20f, 0.90f, 1.00f);
        /// <summary>他のオブジェクトの頂点へ吸着した点（位置のみ合わせる）。</summary>
        private static readonly Color PointDefinedSnapColor  = new Color(1.00f, 0.35f, 0.90f);
        /// <summary>吸着していない点。</summary>
        private static readonly Color PointDefinedFreeColor  = new Color(1.00f, 0.92f, 0.20f);
        /// <summary>次に置かれる点の候補。</summary>
        private static readonly Color PointDefinedHoverColor = new Color(1.00f, 1.00f, 1.00f, 0.9f);

        // ================================================================
        // 生成・配線
        // ================================================================

        private void BuildPointDefinedHandler()
        {
            _pointDefinedHandler = new PointDefinedToolHandler
            {
                GetProject     = () => ActiveProject,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                GetSnapHoverElement = () => _viewportManager.GetSnapHoverElement(ActiveProject?.CurrentModel),

                // 任意メッシュの頂点について GPU が計算したワールド座標（CPU で計算し直さない）。
                GetMeshVertexWorldPosition = (ctxIdx, vi) =>
                {
                    var m = ActiveProject?.CurrentModel;
                    if (m == null || ctxIdx < 0) return null;
                    var mc = m.GetMeshContext(ctxIdx);
                    if (mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (Vector3?)w : null;
                },

                // 吸着用ヒットテストは点指定モードの間だけ有効にする。
                OnSnapHitTestEnabledChanged = on =>
                    _viewportManager.SetSnapHitTestEnabled(
                        on && _interactionMode == InteractionMode.PointDefinedPrimitive),

                // 編集対象が無いときの空オブジェクト作成は面追加と同じものを使う。
                EnsureDrawableMesh = () =>
                {
                    var ensure = _addFaceHandler?.EnsureDrawableMesh;
                    return ensure != null
                        ? ensure()
                        : ActiveProject?.CurrentModel?.ActiveMeshContext != null;
                },

                GetUndoController = () => _editOps?.UndoController,

                OnPointsChanged = () =>
                {
                    NotifyPointDefinedPanels();
                    UpdateTopologyToolsOverlay();
                },
            };
        }

        /// <summary>図形生成パネル（通常／3D連携）へ点指定図形の口を繋ぐ。</summary>
        private void WirePointDefinedCallbacks(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            panel.OnShapeSelected = kind => OnPrimitiveShapeSelected(panel, kind);

            panel.OnPointDefinedRequestChanged = (mode, p) =>
            {
                _pointDefinedHandler?.SetRequest(mode, p);
                // 分割数を変えた瞬間に共有可能な経路の強調を出し直す（仕様 9.12）。
                UpdateTopologyToolsOverlay();
            };

            panel.BuildPointDefinedPreviewMesh = () => _pointDefinedHandler?.BuildPreviewMesh();
            panel.GetPointDefinedStatus        = () => _pointDefinedHandler?.Status;
            panel.GetPointDefinedPlacedCount   = () => _pointDefinedHandler?.PlacedCount ?? 0;
            panel.PointDefinedClearPoints      = () => _pointDefinedHandler?.ClearPoints();
            panel.PointDefinedRemoveLastPoint  = () => _pointDefinedHandler?.RemoveLastPoint();

            panel.GetPointDefinedSnapUnselected = () => _pointDefinedHandler?.SnapToUnselectedObjects ?? false;
            panel.SetPointDefinedSnapUnselected = on =>
            {
                if (_pointDefinedHandler != null) _pointDefinedHandler.SnapToUnselectedObjects = on;
            };

            panel.BuildPointDefinedCommand = materialIndex =>
            {
                var h = _pointDefinedHandler;
                if (h == null) return (null, "点指定図形ハンドラがありません");
                var cmd = h.BuildCommand(materialIndex, out string reason);
                return (cmd, reason);
            };
        }

        /// <summary>
        /// 図形生成パネルで図形を選び直したとき。3D連携インスタンスを開いている間だけ、
        /// 点指定図形なら PointDefinedPrimitive、それ以外なら PrimitivePlace にする。
        /// </summary>
        private void OnPrimitiveShapeSelected(
            PlayerPrimitiveMeshSubPanel panel, PlayerPrimitiveMeshSubPanel.ShapeKind kind)
        {
            if (panel == null || panel != _livePrimitiveSubPanel) return;
            if (_layoutRoot == null || _activeRightSection != _layoutRoot.LivePrimitiveSection) return;

            bool want = kind == PlayerPrimitiveMeshSubPanel.ShapeKind.PointDefined;
            if (want && _interactionMode != InteractionMode.PointDefinedPrimitive)
                SetInteractionMode(InteractionMode.PointDefinedPrimitive);
            else if (!want && _interactionMode == InteractionMode.PointDefinedPrimitive)
                SetInteractionMode(InteractionMode.PrimitivePlace);

            UpdateTopologyToolsOverlay();
        }

        /// <summary>点・カメラが変わったのでプレビューを作り直させる。</summary>
        private void NotifyPointDefinedPanels()
        {
            _livePrimitiveSubPanel?.NotifyPointDefinedChanged();
            _primitiveSubPanel?.NotifyPointDefinedChanged();
        }

        // ================================================================
        // オーバーレイ
        // ================================================================

        /// <summary>
        /// 指定点・次の点の候補・共有できる既存経路を描く。
        /// 点指定モードでなければ false（呼出し側の後続の分岐へ進む）。
        /// 既存頂点の座標は GPU 値（TryGetVertexWorld）を使う。
        /// </summary>
        private bool UpdatePointDefinedOverlay(PlayerViewportPanel panel, ToolContext ctx)
        {
            if (_interactionMode != InteractionMode.PointDefinedPrimitive) return false;

            var h = _pointDefinedHandler;
            if (h == null) { panel.HideTopoToolOverlay(); return true; }

            // カメラ変更の通知もここを通る。視線が変わっていればプレビューを作り直させる。
            if (h.IsPreviewViewDirectionStale()) NotifyPointDefinedPanels();

            float hgt = ctx.PreviewRect.height;
            Vector2 ToScreen(Vector3 w)
            {
                var sp = ctx.WorldToScreen(w);
                return new Vector2(sp.x, hgt - sp.y);
            }

            var lines  = new List<(Vector2, Vector2, Color)>();
            var points = new List<(Vector2, Color, float)>();

            // ── 共有できる既存経路（分割数が一致したものだけ） ──
            var paths = new List<List<int>>();
            int pathMesh = h.CollectSharedPaths(paths);
            foreach (var path in paths)
            {
                Vector2? prev = null;
                foreach (int vi in path)
                {
                    var w = h.TargetVertexWorld(pathMesh, vi);
                    if (!w.HasValue) { prev = null; continue; }
                    var s = ToScreen(w.Value);
                    if (prev.HasValue) lines.Add((prev.Value, s, PointDefinedPathColor));
                    points.Add((s, PointDefinedPathColor, 3f));
                    prev = s;
                }
            }

            // ── 指定点 ──
            var model = ActiveProject?.CurrentModel;
            var active = model?.ActiveMeshContext;
            int activeIdx = (model != null && active != null) ? model.IndexOf(active) : -1;

            foreach (var pk in h.Picks)
            {
                Vector3 w = pk.WorldPosition;
                if (pk.MeshIndex >= 0 && pk.VertexIndex >= 0)
                {
                    var gw = h.TargetVertexWorld(pk.MeshIndex, pk.VertexIndex);
                    if (gw.HasValue) w = gw.Value;
                }

                Color col = pk.MeshIndex < 0 ? PointDefinedFreeColor
                          : pk.MeshIndex == activeIdx ? PointDefinedReuseColor
                          : PointDefinedSnapColor;
                points.Add((ToScreen(w), col, 4f));
            }

            // ── 次の点の候補 ──
            if (h.HoverPick.HasValue)
                points.Add((ToScreen(h.HoverPick.Value.WorldPosition), PointDefinedHoverColor, 5f));

            if (lines.Count == 0 && points.Count == 0) panel.HideTopoToolOverlay();
            else                                        panel.UpdateTopoToolOverlay(lines, points);
            return true;
        }
    }
}
