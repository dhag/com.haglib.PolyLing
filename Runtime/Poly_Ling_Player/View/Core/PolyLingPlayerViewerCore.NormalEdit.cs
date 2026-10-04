// PolyLingPlayerViewerCore.NormalEdit.cs
// Player ビューアのコア：法線編集パネルとビューポート操作（InteractionMode.NormalEdit）の配線。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// パネル（PlayerNormalEditSubPanel）がプレビューと確定を持ち、
// ツール（NormalEditToolHandler）はビューポートの入力をパネルの口へ渡すだけ。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>
        /// 法線編集パネルとツールを結ぶ。パネルを Build した直後に呼ぶ。
        /// </summary>
        private void WireNormalEdit()
        {
            var panel = _normalEditSubPanel;
            if (panel == null) return;

            // ── パネル側：プレビューで実体を書き換えるための口 ──
            panel.GetModel = () => ActiveProject?.CurrentModel;
            panel.OnSyncMeshNormals = mc =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null || mc?.MeshObject == null) return;
                if (mc.UnityMesh != null && mc.MeshObject.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    _viewportManager.EnterVertexAttributesChanged(proj, mc, weights: false, uvs: false);
                else
                    _viewportManager.EnterTopologyChanged(proj);
            };
            panel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                _viewportManager.EnterTopologyChanged(proj);
            };
            panel.TryLockForPreview  = TryBeginHostPreview;
            panel.UnlockAfterPreview = EndHostPreview;

            // 法線表示は 4 面とも同じ値にする（左ペインの表示チェックと同じ経路を通す）
            panel.GetShowNormals = () =>
            {
                for (int s = 0; s < 4; s++)
                    if (_viewportManager.GetDisplaySettings(s).ShowNormals) return true;
                return false;
            };
            panel.SetShowNormals = v =>
            {
                for (int s = 0; s < 4; s++)
                {
                    var tog = _layoutRoot?.ViewportDisplayToggles[s, PlayerLayoutRoot.VD_NORMAL];
                    if (tog != null) tog.value = v;   // 値の変化コールバックが表示設定へ書く
                }
            };
            panel.RequestNormalLinesRebuild = () =>
            {
                // 法線の線は event 駆動で作り直される（MeshSceneRenderer.PrepareNormals）。
                // 属性変化として通知すれば次の描画で作り直される。
                var proj = ActiveProject;
                var model = proj?.CurrentModel;
                if (model == null) return;
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject != null)
                        _viewportManager.EnterVertexAttributesChanged(proj, mc, weights: false, uvs: false);
                }
                _activePanel?.MarkDirtyRepaint();
            };

            panel.SetViewportTool = SetNormalEditViewportTool;
            panel.GetViewportTool = () => _interactionMode == InteractionMode.NormalEdit && _normalEditHandler != null
                ? _normalEditHandler.Mode
                : NormalEditToolHandler.SubMode.Select;
            panel.RefreshGizmoFromPanel = () =>
            {
                if (_interactionMode == InteractionMode.NormalEdit) UpdateGizmoOverlay();
            };

            // ── ツール側：ビューポートの入力 ──
            _normalEditHandler = new NormalEditToolHandler
            {
                GetProject          = () => ActiveProject,
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetBrushHit         = (pos, r) => _viewportManager.GetBrushHit(pos, r),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnGizmoRefresh      = UpdateGizmoOverlay,
                OnUpdateBrushCircle = (c, r) => _activePanel?.ShowBrushCircle(c, r),
                OnHideBrushCircle   = () => _activePanel?.HideBrushCircle(),

                GetHandleWorld      = panel.GetHandleWorld,
                OnHandleMoved       = panel.OnHandleMoved,
                OnHandleDragEnd     = () => panel.Refresh(),
                OnDirectionPicked   = panel.OnDirectionPicked,
                BeginLivePreview    = panel.BeginLivePreview,
                PreviewRotate       = panel.PreviewRotate,
                CommitRotate        = panel.CommitRotate,
                PreviewBrush        = panel.PreviewBrush,
                CommitBrush         = panel.CommitBrush,
                CancelLivePreview   = panel.CancelLivePreview,
                GetBrushRadius      = () => panel.BrushRadius,
            };
        }

        /// <summary>
        /// パネルのボタンからビューポート操作を切り替える。
        /// 選択だけのときもモードは NormalEdit にしておき、ルーティングで選択専用にする。
        /// </summary>
        private void SetNormalEditViewportTool(NormalEditToolHandler.SubMode sub)
        {
            if (_normalEditHandler == null) return;
            _normalEditHandler.Deactivate();
            _normalEditHandler.Mode = sub;

            if (_interactionMode == InteractionMode.NormalEdit)
                ApplyNormalEditToolRouting();      // サブモードだけ変わった
            else
                SetInteractionMode(InteractionMode.NormalEdit);

            UpdateGizmoOverlay();
            _activePanel?.MarkDirtyRepaint();
        }

        /// <summary>
        /// NormalEdit モードの入力経路をサブモードに応じて張る。
        ///   Select … MoveToolHandler を選択専用で使う（ギズモ・頂点移動なし）
        ///   それ以外 … NormalEditToolHandler が入力を受ける（選択は変えない）
        /// </summary>
        private void ApplyNormalEditToolRouting()
        {
            if (_normalEditHandler == null) return;

            if (_normalEditHandler.Mode == NormalEditToolHandler.SubMode.Select)
            {
                if (_moveToolHandler != null)
                {
                    _moveToolHandler.SelectOnly           = true;
                    _moveToolHandler.SuppressBuiltinGizmo = true;
                }
                _vertexInteractor?.SetToolHandler(_moveToolHandler);
                _viewportManager?.RegisterActiveToolHandler(null);
                _activePanel?.HideBrushCircle();
                return;
            }

            if (_moveToolHandler != null)
            {
                _moveToolHandler.SelectOnly           = false;
                _moveToolHandler.SuppressBuiltinGizmo = false;
            }
            _vertexInteractor?.SetToolHandler(_normalEditHandler);
            _viewportManager?.RegisterActiveToolHandler((pos, ctx) =>
            {
                _normalEditHandler?.UpdateHover(pos, ctx);
                if (_normalEditHandler?.Mode == NormalEditToolHandler.SubMode.Handle) UpdateGizmoOverlay();
            });
        }
    }
}
