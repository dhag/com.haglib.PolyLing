// PolyLingPlayerViewerCore.Layout.VertexTools.cs
// Player ビューアのコア：BuildLayout の段（選択頂点の位置・トポロジー系ツール）。
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
        /// <summary>BuildLayout の段：選択頂点の位置・トポロジー系ツール（整列・パイプ整列・面に張り付け・藤壺整形・平面化・辺を滑らかに・マージ・分割・穴あけ・溶解・穴頂点数合わせ・辺群ブリッジ・面結合・頂点ID・パーツID・頂点データ転送・選択削除）。</summary>
        private void BuildVertexToolPanels()
        {
            _alignVerticesHandler = new AlignVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            _alignVerticesHandler.SetProject(ActiveProject);
            _alignVerticesHandler.SetUndoController(_editOps?.UndoController);
            _alignVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _alignVerticesSubPanel = new PlayerAlignVerticesSubPanel
            {
                GetH        = () => _alignVerticesHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _alignVerticesSubPanel.Build(_layoutRoot.AlignVerticesSection);

            _pipeAlignHandler = new PipeAlignToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            _pipeAlignHandler.SetProject(ActiveProject);
            _pipeAlignHandler.SetUndoController(_editOps?.UndoController);
            _pipeAlignHandler.SetCommandQueue(_editOps?.CommandQueue);
            _pipeAlignSubPanel = new PlayerPipeAlignSubPanel
            {
                GetH        = () => _pipeAlignHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _pipeAlignSubPanel.Build(_layoutRoot.PipeAlignSection);

            _surfaceSnapHandler = new SurfaceSnapToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            // 張り付け計算に使うワールド座標は GPU が計算したものだけを参照する。
            _surfaceSnapHandler.GetWorldPositions = mc =>
            {
                var model = ActiveProject?.CurrentModel;
                if (model == null) return null;
                return _viewportManager.TryGetMeshWorldPositions(model, mc, out var world) ? world : null;
            };
            // ワールド座標が要るのは計算の直前だけ。毎フレームは呼ばない。
            _surfaceSnapHandler.OnRequestUpdateTransform = () => _viewportManager.UpdateTransform();
            // Poly_Ling_Main 側へビューポート実装を持ち込まないため、値だけを写して渡す。
            _surfaceSnapHandler.GetCamera = kind =>
            {
                PlayerViewport vp;
                switch (kind)
                {
                    case SurfaceSnapCameraKind.Perspective: vp = _viewportManager.PerspectiveViewport; break;
                    case SurfaceSnapCameraKind.Top:         vp = _viewportManager.TopViewport;         break;
                    case SurfaceSnapCameraKind.Front:       vp = _viewportManager.FrontViewport;       break;
                    case SurfaceSnapCameraKind.Side:        vp = _viewportManager.SideViewport;        break;
                    default:                                vp = _activeViewport ?? _viewportManager.PerspectiveViewport; break;
                }
                var cam = vp?.Cam;
                if (cam == null) return null;
                return new SurfaceSnapCamera
                {
                    IsOrthographic = cam.orthographic,
                    Position       = cam.transform.position,
                    Forward        = cam.transform.forward,
                };
            };
            _surfaceSnapHandler.SetProject(ActiveProject);
            _surfaceSnapHandler.SetUndoController(_editOps?.UndoController);
            _surfaceSnapHandler.SetCommandQueue(_editOps?.CommandQueue);
            _surfaceSnapSubPanel = new PlayerSurfaceSnapSubPanel
            {
                GetH        = () => _surfaceSnapHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _surfaceSnapSubPanel.Build(_layoutRoot.SurfaceSnapSection);

            _placeObjectReshapeHandler = new PlaceObjectReshapeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                // 原型はパネルが持つチェック一覧を並び順で結合したもの。
                GetPrototype        = () => _placeObjectReshapeSubPanel?.BuildPrototype(),
            };
            _placeObjectReshapeHandler.SetProject(ActiveProject);
            _placeObjectReshapeHandler.SetUndoController(_editOps?.UndoController);
            _placeObjectReshapeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _placeObjectReshapeSubPanel = new PlayerPlaceObjectReshapeSubPanel
            {
                GetH                     = () => _placeObjectReshapeHandler,
                GetDrawableMeshEntryList = BuildDrawableMeshEntryList,
                GetView                  = () => ActiveProject,
                SendCommand              = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _placeObjectReshapeSubPanel.Build(_layoutRoot.PlaceObjectReshapeSection);

            _planarizeAlongBonesHandler = new PlanarizeAlongBonesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            _planarizeAlongBonesHandler.SetProject(ActiveProject);
            _planarizeAlongBonesHandler.SetUndoController(_editOps?.UndoController);
            _planarizeAlongBonesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _planarizeAlongBonesSubPanel = new PlayerPlanarizeAlongBonesSubPanel
            {
                GetH        = () => _planarizeAlongBonesHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _planarizeAlongBonesSubPanel.Build(_layoutRoot.PlanarizeAlongBonesSection);

            _smoothEdgesHandler = new SmoothEdgesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    // 位置のみの変更なので EnterVerticesMoved(Dragging) の軽量パスを使う。
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnApplyCompleted    = () => NotifyPanels(ChangeKind.Attributes),
            };
            _smoothEdgesHandler.SetProject(ActiveProject);
            _smoothEdgesHandler.SetUndoController(_editOps?.UndoController);
            _smoothEdgesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _smoothEdgesSubPanel = new PlayerSmoothEdgesSubPanel
            {
                GetH        = () => _smoothEdgesHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _smoothEdgesSubPanel.Build(_layoutRoot.SmoothEdgesSection);

            _mergeVerticesHandler = new MergeVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            _mergeVerticesHandler.SetProject(ActiveProject);
            _mergeVerticesHandler.SetUndoController(_editOps?.UndoController);
            _mergeVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _mergeVerticesHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _mergeVerticesSubPanel = new PlayerMergeVerticesSubPanel
            {
                GetH        = () => _mergeVerticesHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _mergeVerticesSubPanel.Build(_layoutRoot.MergeVerticesSection);

            _splitVerticesHandler = new SplitVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
            };
            _splitVerticesHandler.SetProject(ActiveProject);
            _splitVerticesHandler.SetUndoController(_editOps?.UndoController);
            _splitVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _splitVerticesHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _splitVerticesSubPanel = new PlayerSplitVerticesSubPanel
            {
                GetH        = () => _splitVerticesHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _splitVerticesSubPanel.Build(_layoutRoot.SplitVerticesSection);

            _vertexHoleHandler = new VertexHoleToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _vertexHoleHandler.SetProject(ActiveProject);
            _vertexHoleHandler.SetUndoController(_editOps?.UndoController);
            _vertexHoleHandler.SetCommandQueue(_editOps?.CommandQueue);
            _vertexHoleHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _vertexHoleSubPanel = new PlayerVertexHoleSubPanel
            {
                GetH        = () => _vertexHoleHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vertexHoleSubPanel.Build(_layoutRoot.VertexHoleSection);
            AttachPanelSelectToggle(_layoutRoot.VertexHoleSection, PanelSelectKeyVertexHole);

            _vertexDissolveHandler = new VertexDissolveToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _vertexDissolveHandler.SetProject(ActiveProject);
            _vertexDissolveHandler.SetUndoController(_editOps?.UndoController);
            _vertexDissolveHandler.SetCommandQueue(_editOps?.CommandQueue);
            _vertexDissolveHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _vertexDissolveSubPanel = new PlayerVertexDissolveSubPanel
            {
                GetH        = () => _vertexDissolveHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vertexDissolveSubPanel.Build(_layoutRoot.VertexDissolveSection);

            // 穴頂点数合わせ。種の取り込みはブリッジと同じ PickHoleSeeds を通す。
            _holeRingCountHandler = new HoleRingCountToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
                PickHoleSeeds  = PickHoleSeeds,
                GetMeshNameAt  = idx =>
                    ActiveProject?.CurrentModel?.GetMeshContext(idx)?.Name ?? $"#{idx}",
                OnSeedsChanged = UpdateTopologyToolsOverlay,
            };
            _holeRingCountHandler.SetProject(ActiveProject);
            _holeRingCountHandler.SetUndoController(_editOps?.UndoController);
            _holeRingCountHandler.SetCommandQueue(_editOps?.CommandQueue);
            _holeRingCountHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _holeRingCountSubPanel = new PlayerHoleRingCountSubPanel
            {
                GetH      = () => _holeRingCountHandler,
                OnExecute = SendHoleRingCountCommand,
            };
            _holeRingCountSubPanel.Build(_layoutRoot.HoleRingCountSection);
            AttachPanelSelectToggle(_layoutRoot.HoleRingCountSection, PanelSelectKeyHoleRingCount);

            // 辺群ブリッジ。穴つなぎと違い、辺を明示的に拾う（クリック／矩形）。
            // 拾った辺は MeshContext.Selection に書かないので、選択状態とは独立している。
            _edgeBridgeHandler = new EdgeBridgeToolHandler
            {
                GetToolContext  = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint       = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                // ホバー種別の適用先は選択モード権限へ集約する
                // （現モデル全メッシュ / SelectionOps / レンダラ保持分をまとめて書く）。
                ApplyHoverModeToAllMeshes = SetToolSelectModeOverride,

                // 矩形選択。走査規則は MoveToolHandler.CommitBoxSelect の辺走査と同じ。
                GetScreenPositions = () => _viewportManager.GetScreenPositions(),
                GetVertexOffset    = ctxIdx => _viewportManager.GetVertexOffset(ctxIdx),
                IsVertexVisible    = gi => _viewportManager.IsVertexVisible(gi),
                GetViewportHeight  = () => _activeViewport?.Cam?.pixelHeight ?? 0f,

                OnBoxSelectUpdate = (start, end) => _activePanel?.ShowBoxSelect(start, end),
                OnBoxSelectEnd    = () => _activePanel?.HideBoxSelect(),

                // 矩形確定前の背面カリング読み戻しだけを結線する。
                // EnterBoxSelecting / ExitBoxSelecting は [Obsolete] で新規呼出しが
                // 禁じられているため使わない（拾う結果には影響しない）。
                OnReadBackVertexFlags = () => _viewportManager.ReadBackVertexFlags(),

                // 拾いが変わったらオーバーレイとサブパネルを更新する。
                OnPicksChanged = () =>
                {
                    UpdateTopologyToolsOverlay();
                    _edgeBridgeSubPanel?.Refresh();
                },
            };
            _edgeBridgeHandler.SetProject(ActiveProject);
            _edgeBridgeHandler.SetUndoController(_editOps?.UndoController);
            _edgeBridgeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeBridgeHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _edgeBridgeSubPanel = new PlayerEdgeBridgeSubPanel
            {
                GetH      = () => _edgeBridgeHandler,
                OnExecute = SendEdgeBridgeCommand,
            };
            _edgeBridgeSubPanel.Build(_layoutRoot.EdgeBridgeSection);
            AttachPanelSelectToggle(_layoutRoot.EdgeBridgeSection, PanelSelectKeyEdgeBridge);

            _tri4To1Handler = new Tri4To1ToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _tri4To1Handler.SetProject(ActiveProject);
            _tri4To1Handler.SetUndoController(_editOps?.UndoController);
            _tri4To1Handler.SetCommandQueue(_editOps?.CommandQueue);
            _tri4To1Handler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _tri4To1SubPanel = new PlayerTri4To1SubPanel
            {
                GetH        = () => _tri4To1Handler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _tri4To1SubPanel.Build(_layoutRoot.Tri4To1Section);

            _faceMergeHandler = new FaceMergeToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _faceMergeHandler.SetProject(ActiveProject);
            _faceMergeHandler.SetUndoController(_editOps?.UndoController);
            _faceMergeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _faceMergeHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _faceMergeSubPanel = new PlayerFaceMergeSubPanel
            {
                GetH        = () => _faceMergeHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _faceMergeSubPanel.Build(_layoutRoot.FaceMergeSection);

            _quad4To1Handler = new Quad4To1ToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _quad4To1Handler.SetProject(ActiveProject);
            _quad4To1Handler.SetUndoController(_editOps?.UndoController);
            _quad4To1Handler.SetCommandQueue(_editOps?.CommandQueue);
            _quad4To1Handler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _quad4To1SubPanel = new PlayerQuad4To1SubPanel
            {
                GetH        = () => _quad4To1Handler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _quad4To1SubPanel.Build(_layoutRoot.Quad4To1Section);

            _vertexIdSubPanel = new PlayerVertexIdSubPanel
            {
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vertexIdSubPanel.Build(_layoutRoot.VertexIdSection);

            // パーツID / サブID の採番。対象・リファレンスはパネル内のドロップダウンで
            // 選ぶため、ビューポートのオブジェクト選択には依存しない。
            _partsIdSubPanel = new PlayerPartsIdSubPanel
            {
                GetView                  = () => ActiveProject,
                SendCommand              = cmd => _commandDispatcher?.Dispatch(cmd),
                GetDrawableMeshEntryList = BuildDrawableMeshEntryList,
                GetLastResult            = () => _commandDispatcher != null
                                               ? _commandDispatcher.LastPartsIdResult
                                               : default,
                GetLastBoneWeightResult  = () => _commandDispatcher != null
                                               ? _commandDispatcher.LastPartsIdByBoneWeightResult
                                               : default,
                GetLastSplitResult       = () => _lastPartsIdSplitResult,
            };
            _partsIdSubPanel.Build(_layoutRoot.PartsIdSection);

            _vertexTransferSubPanel = new PlayerVertexTransferSubPanel
            {
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vertexTransferSubPanel.Build(_layoutRoot.VertexTransferSection);

            // 選択削除サブツール。InteractionMode を切り替えないため
            // _vertexInteractor.SetToolHandler には登録しない (入力を奪わない)。
            _deleteSelectionHandler = new DeleteSelectionToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            // ここでの SetProject は BuildLayout 時点の値 (モデル未読込なら null)。
            // ActiveProject は _localLoader.Project が読込時に生成されるまで null なので、
            // プロジェクト生成/切替/受信の各経路で必ず再伝播すること
            // (EnsureDrawableMesh / PrepareHandlersForGeneratedMesh / OnMeshDataReceived /
            //  モデル読込完了。EnsureDrawableMesh 内の設計ポイントコメント参照)。
            _deleteSelectionHandler.SetProject(ActiveProject);
            _deleteSelectionHandler.SetUndoController(_editOps?.UndoController);
            _deleteSelectionHandler.SetCommandQueue(_editOps?.CommandQueue);
            _deleteSelectionHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // merge / split と同じ位相変更後処理。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
        }
    }
}
