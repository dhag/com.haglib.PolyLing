// PolyLingPlayerViewerCore.UvEditMode.cs
// Player ビューアのコア：UV 編集モード（UVZ 平面に展開 → 編集 → 書き戻し）。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // UV編集モード（A方式：UVZ平面に展開→既存マグネット/彫刻で編集→書き戻し）
        // ================================================================

        private void EnterUvEditMode()
        {
            if (_uvEditModeActive) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0) return;

            int srcMaster = model.SelectedDrawableMeshIndices[0];
            var srcMc     = model.GetMeshContext(srcMaster);
            if (srcMc?.MeshObject == null || srcMc.MeshObject.VertexCount == 0) return;

            int modelIdx = ActiveProject?.CurrentModelIndex ?? 0;
            var toolCtx  = _viewportManager.GetCurrentToolContext(_activeViewport);
            Vector3 camPos = toolCtx?.CameraPosition ?? Vector3.zero;
            Vector3 camFwd = toolCtx != null
                ? (toolCtx.CameraTarget - toolCtx.CameraPosition).normalized
                : Vector3.forward;

            // UVZメッシュ生成（depthScale=0＝完全平面）。model.Add は末尾追加。
            int beforeCount = model.MeshContextCount;
            _commandDispatcher?.Dispatch(new UvToXyzCommand(
                modelIdx, srcMaster, _uvEditUvScale, 0f, camPos, camFwd));
            if (model.MeshContextCount <= beforeCount) return; // 生成失敗
            int uvzMaster = model.MeshContextCount - 1;

            _uvEditModeActive = true;
            _uvEditSrcMaster  = srcMaster;
            _uvEditUvzMaster  = uvzMaster;

            // UVZを単独選択（ツールの編集対象にする）
            model.SelectMeshContextExclusive(uvzMaster);

            // Front 正射影ビューへ切替＋フィット
            _uvEditPrevPanel    = _activePanel;
            _uvEditPrevViewport = _activeViewport;
            var frontPanel = _layoutRoot?.FrontPanel;
            var frontVp    = _viewportManager.FrontViewport;
            if (frontPanel != null && frontVp != null)
            {
                if (_activePanel != null) _vertexInteractor?.Disconnect(_activePanel);
                _activePanel    = frontPanel;
                _activeViewport = frontVp;
                _vertexInteractor?.Connect(_activePanel);

                var uvzMc = model.GetMeshContext(uvzMaster);
                if (uvzMc?.MeshObject != null)
                    frontVp.ResetToMesh(uvzMc.MeshObject.CalculateBounds());
                _viewportManager.EnterCameraChanged(frontVp, CameraChangePhase.Committed);
            }

            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.ListStructure);
        }

        private void ExitUvEditMode()
        {
            if (!_uvEditModeActive) return;
            var model    = ActiveProject?.CurrentModel;
            int modelIdx = ActiveProject?.CurrentModelIndex ?? 0;

            int uvzMaster = _uvEditUvzMaster;
            int srcMaster = _uvEditSrcMaster;

            // 状態を先にクリア（通知での再入防止）
            _uvEditModeActive = false;
            _uvEditUvzMaster  = -1;
            _uvEditSrcMaster  = -1;

            if (model != null && uvzMaster >= 0 && srcMaster >= 0
                && uvzMaster < model.MeshContextCount && srcMaster < model.MeshContextCount)
            {
                // XY→UV 書き戻し（ソース側Undo記録）。src=UVZ, target=元メッシュ。
                _commandDispatcher?.Dispatch(new XyzToUvCommand(
                    modelIdx, uvzMaster, srcMaster, _uvEditUvScale));

                // UVZメッシュ破棄（末尾indexなので他indexはずれない）
                _commandDispatcher?.Dispatch(new DeleteMeshesCommand(
                    modelIdx, new[] { uvzMaster }));

                // 元メッシュを選択へ復元
                if (srcMaster < model.MeshContextCount)
                    model.SelectMeshContextExclusive(srcMaster);
            }

            // ビュー復元
            var prevPanel = _uvEditPrevPanel;
            var prevVp    = _uvEditPrevViewport;
            _uvEditPrevPanel = null; _uvEditPrevViewport = null;
            if (prevPanel != null && prevVp != null)
            {
                if (_activePanel != null) _vertexInteractor?.Disconnect(_activePanel);
                _activePanel    = prevPanel;
                _activeViewport = prevVp;
                _vertexInteractor?.Connect(_activePanel);
            }

            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.ListStructure);
        }
    }
}
