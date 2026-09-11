// PolyLingPlayerViewerCore.Layout.EditTools.cs
// Player ビューアのコア：BuildLayout の段（面追加・回転・作業軸・カメラ・デフォーマ・格子・押し出し等）。
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
        /// <summary>BuildLayout の段：面追加・面反転・回転・作業軸・カメラ調整。</summary>
        private void BuildFaceAndAxisTools()
        {
            _addFaceHandler = new AddFaceToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                // 表裏判定用。GPU が計算済みのワールド座標を渡す（CPU で計算し直さない）。
                GetVertexWorldPosition = vi =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    var mc = m?.ActiveMeshContext;
                    if (m == null || mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (UnityEngine.Vector3?)w : null;
                },
                // 他オブジェクトの頂点への吸着用。任意メッシュのワールド座標を返す。
                // GetVertexWorldPosition は ActiveMeshContext 固定なので他メッシュには使えない。
                GetMeshVertexWorldPosition = (ctxIdx, vi) =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    if (m == null || ctxIdx < 0) return null;
                    var mc = m.GetMeshContext(ctxIdx);
                    if (mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (UnityEngine.Vector3?)w : null;
                },
                // 非選択オブジェクトも対象にした吸着用ホバー。
                // 通常ホバー（GetHoverElement）は選択メッシュしか返さない。
                GetSnapHoverElement = () =>
                    _viewportManager.GetSnapHoverElement(ActiveProject?.CurrentModel),
                // 吸着用ヒットテストの有効化。面追加モードでない間は必ず切る
                // （有効な間はポインタ移動ごとに頂点数ぶんの読み戻しが増えるため）。
                OnSnapHitTestEnabledChanged = on =>
                    _viewportManager.SetSnapHitTestEnabled(
                        on && _interactionMode == InteractionMode.AddFace),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                // Phase 2c-3: 確定点追加時に overlay 再描画を発火する。
                // 確定点の追加はトポロジを実質変更していないが、UIToolkit overlay の
                // 再投影が必要なため EnterTopologyChanged 経由で一括 refresh する。
                OnPointPlaced       = () =>
                {
                    _viewportManager.EnterTopologyChanged(ActiveProject);
                },
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
                EnsureDrawableMesh = () =>
                {
                    // モデル・描画メッシュがなければ空のMeshContextを自動生成する
                    _localLoader.EnsureProject();
                    _moveToolHandler?.SetProject(ActiveProject);
                    _objectMoveHandler?.SetProject(ActiveProject);
                    var proj = ActiveProject;
                    if (proj == null) return false;
                    if (proj.CurrentModel == null && proj.ModelCount > 0)
                        proj.SelectModel(0);
                    ApplySelectMode();  // 実効選択モードを新規アクティブモデルへ適用
                    var model = proj.CurrentModel;
                    if (model == null) return false;

                    // 描画可能メッシュが既にあればそのまま使う
                    if (model.ActiveMeshContext != null) return true;

                    // 空のMeshContextを1つ作成してUNDO記録
                    var emptyMo = new Poly_Ling.Data.MeshObject("New Mesh");
                    var unityMesh = emptyMo.ToUnityMesh();
                    unityMesh.name      = "New Mesh";
                    unityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    var ctx = new Poly_Ling.Data.MeshContext
                    {
                        Name       = "New Mesh",
                        MeshObject = emptyMo,
                        UnityMesh  = unityMesh,
                        IsVisible  = true,
                        ParentModelContext = model,
                    };
                    var oldSelected = model.CaptureAllSelectedIndices();
                    int insertIndex = model.Add(ctx);
                    model.ComputeWorldMatrices();
                    model.SelectMeshContextExclusive(insertIndex);
                    model.SelectMesh(insertIndex);
                    var newSelected = model.CaptureAllSelectedIndices();

                    if (_editOps?.UndoController != null)
                    {
                        _editOps.UndoController.SetModelContext(model);
                        _editOps.UndoController.RecordMeshContextAdd(
                            ctx, insertIndex, oldSelected, newSelected);
                    }

                    // Phase 2a-2b-2 Batch 3: 新規 MeshContext 作成後の RebuildAdapter +
                    // SetSelectionState + UpdateSelectedDrawableMesh を EnterSceneReset に集約。
                    _viewportManager.EnterSceneReset(ActiveProject);
                    _addFaceHandler?.SetProject(ActiveProject);
                    // 【設計ポイント: プロジェクト生成経路では全ハンドラに SetProject 伝播】
                    // EnsureProject はユーザがメッシュを持たない状態で編集ツールを起動したときに
                    // 暗黙に Project を生成する経路。_addFaceHandler だけ再設定していた過去の
                    // 残骸があると、EdgeTopology / Knife / EdgeBevel 等の他トポロジ系ハンドラは
                    // 初期化時の 1 回切りの SetProject(null) のまま取り残され、
                    // GetEnrichedCtx が null model を返してツールが無反応になる。
                    // 同じ症状を他ツールで繰り返さないために、プロジェクト生成/切替/受信経路は
                    // 全トポロジハンドラを漏れなく伝播する (PrepareHandlersForGeneratedMesh,
                    // OnMeshDataReceived 等の他経路も同じ列挙を持つ)。新ハンドラ追加時は
                    // 全伝播箇所に新しい `_xxxHandler?.SetProject(ActiveProject);` を追加すること。
                    _edgeBevelHandler?.SetProject(ActiveProject);
                    _edgeExtrudeHandler?.SetProject(ActiveProject);
                    _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeRibbonFaceHandler?.SetProject(ActiveProject);
                    _edgeTopologyHandler?.SetProject(ActiveProject);
                    _knifeHandler?.SetProject(ActiveProject);
                    _deleteSelectionHandler?.SetProject(ActiveProject);
                    _vertexDissolveHandler?.SetProject(ActiveProject);
                    _tri4To1Handler?.SetProject(ActiveProject);
                    _faceMergeHandler?.SetProject(ActiveProject);
                    _quad4To1Handler?.SetProject(ActiveProject);
                    _edgeBridgeHandler?.SetProject(ActiveProject);
                _holeRingCountHandler?.SetProject(ActiveProject);
                    RebuildModelList();
                    NotifyPanels(ChangeKind.ListStructure);
                    return true;
                },
            };
            _addFaceHandler.SendCommand = DispatchPanelCommand;
            _addFaceHandler.SetProject(ActiveProject);
            _addFaceHandler.SetUndoController(_editOps?.UndoController);
            _addFaceSubPanel = new PlayerAddFaceSubPanel
            {
                GetH = () => _addFaceHandler,

                // 追加先オブジェクト。編集対象は ActiveMeshIndex（＝ SelectedDrawableMeshIndices[0]）
                // なので、切り替えは通常のメッシュ選択と同じ SelectMeshCommand で行う。
                GetMeshEntries     = BuildAddFaceMeshEntries,
                GetActiveMeshIndex = () => ActiveProject?.CurrentModel?.ActiveMeshIndex ?? -1,
                OnSelectMesh       = idx =>
                {
                    if (idx < 0) return;
                    _commandDispatcher?.Dispatch(new SelectMeshCommand(
                        ActiveProject?.CurrentModelIndex ?? 0,
                        MeshCategory.Drawable,
                        new[] { idx }));
                    // 切り替え先メッシュへの選択モード反映は SelectMeshCommand の経路
                    // (SetSelectionState → OnStateInstalled → ApplySelectMode) が行う。
                    _addFaceSubPanel?.Refresh();
                },

                // マテリアルはモデル共通のカレント値。マテリアルリストパネルと同じ値を読み書きする。
                GetMaterialNames        = BuildAddFaceMaterialNames,
                GetCurrentMaterialIndex = () => ActiveProject?.CurrentModel?.CurrentMaterialIndex ?? -1,
                OnSelectMaterial        = idx =>
                {
                    var model = ActiveProject?.CurrentModel;
                    if (model == null || idx < 0 || idx >= model.MaterialCount) return;
                    model.CurrentMaterialIndex = idx;
                },
            };
            _addFaceSubPanel.Build(_layoutRoot.AddFaceSection);
            _flipFaceHandler = new FlipFaceToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。
 _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _flipFaceHandler.SetProject(ActiveProject);
            _flipFaceHandler.SetUndoController(_editOps?.UndoController);
            _flipFaceHandler.SetCommandQueue(_editOps?.CommandQueue);
            _flipFaceSubPanel = new PlayerFlipFaceSubPanel
            {
                GetH        = () => _flipFaceHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _flipFaceSubPanel.Build(_layoutRoot.FlipFaceSection);
            _rotateHandler = new RotateToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetPanelHeight      = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnApplyCompleted    = () => NotifyPanels(ChangeKind.Attributes),
            };
            _rotateHandler.SendCommand = DispatchPanelCommand;
            _rotateHandler.SetProject(ActiveProject);
            _rotateHandler.SetUndoController(_editOps?.UndoController);
            _rotateSubPanel = new PlayerRotateSubPanel { GetH = () => _rotateHandler };
            _rotateSubPanel.Build(_layoutRoot.RotateSection);

            // 作業用ローカル軸。ModelContext.WorkAxis だけを読み書きし、頂点には触れない。
            _workAxisHandler = new WorkAxisToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
                GetWorkAxis    = () => CurrentWorkAxis(),
                SendCommand    = DispatchPanelCommand,
                GetModelIndex  = () => ActiveProject?.CurrentModelIndex ?? 0,
                // 原点 / Y 先端ハンドルの吸着先。頂点は GPU 吸着ヒットテスト、
                // ボーンは MeshContext の WorldMatrix 投影で拾う。
                GetSnapTargetWorld = imguiPos => WorkAxisSnapTargetWorld(imguiPos),
                // 吸着用ヒットテストの有効化。作業軸モードでハンドルを掴んでいる間だけ。
                // 有効な間はポインタ移動ごとに頂点数ぶんの読み戻しが増えるため、
                // 他モードでは必ず切る。
                // 作業軸ツールに加えて、変形モードの作業軸フェーズでも吸着させる。
                // ここを広げないと変形パネル側で頂点／ボーンへ吸着できない。
                OnSnapHitTestEnabledChanged = on =>
                    _viewportManager.SetSnapHitTestEnabled(on && IsWorkAxisEditable()),
                OnValueChanged = () =>
                {
                    _workAxisSubPanel?.Refresh();
                    _deformWorkAxisSubPanel?.Refresh();
                    UpdateGizmoOverlay();
                    // 格子変形の格子フレームは作業軸そのもの。開いていれば追従させる。
                    _latticeHandler?.OnFrameChanged();
                },
            };
            _workAxisSubPanel = new PlayerWorkAxisSubPanel
            {
                GetWorkAxis               = () => CurrentWorkAxis(),
                GetH                      = () => _workAxisHandler,
                SendCommand               = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex             = () => ActiveProject?.CurrentModelIndex ?? 0,
                OnValueChanged            = () =>
                {
                    UpdateGizmoOverlay();
                    // 格子変形の格子フレームは作業軸そのもの。開いていれば追従させる。
                    _latticeHandler?.OnFrameChanged();
                },
                GetSelectionCentroidWorld = () => SelectedVerticesCentroidWorld(),
                // 辞書はプロジェクト単位の 1 個を左ペインと変形パネルで共有する。
                GetLibrary                = () => ActiveProject?.WorkAxes,
                OnLibraryChanged          = () => RefreshWorkAxisLibraryLists(),
            };
            _workAxisSubPanel.Build(_layoutRoot.WorkAxisSection);

            // カメラ調整。ビューポートのカメラパラメータだけを読み書きし、頂点には触れない。
            _cameraHandler = new CameraToolHandler
            {
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight    = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint         = () => _activePanel?.MarkDirtyRepaint(),
                GetActiveViewport = () => _activeViewport,
                GetOrbit          = () => _viewportManager.PerspectiveViewport?.Orbit,
                // 3面は OrthoViewSharedState を共有しているため、代表1台から読み書きすれば連動する。
                GetTri            = () => _viewportManager.FrontViewport?.Ortho,
                // 向き表示は Flip がビューごとに違うため3台とも渡す（0=Top / 1=Front / 2=Side）。
                GetTriViews       = () => new[]
                {
                    _viewportManager.TopViewport  ?.Ortho,
                    _viewportManager.FrontViewport?.Ortho,
                    _viewportManager.SideViewport ?.Ortho,
                },
                OnCameraPhase     = phase => NotifyCameraToolChanged(phase),
                OnValueChanged    = () =>
                {
                    _cameraSubPanel?.Refresh();
                    UpdateGizmoOverlay();
                },
            };
            _cameraSubPanel = new PlayerCameraSubPanel
            {
                GetH       = () => _cameraHandler,
                GetOrbit   = () => _viewportManager.PerspectiveViewport?.Orbit,
                GetTri     = () => _viewportManager.FrontViewport?.Ortho,
                GetTriFlip = idx =>
                {
                    var vp = TriViewportOf(idx);
                    return vp?.Ortho != null && vp.Ortho.Flipped;
                },
                SetTriFlip          = (idx, flipped) => ApplyTriFlip(idx, flipped),
                SetMainOrthographic = ortho => SetMainCameraOrthographic(ortho),
                FlipMainView        = () => FlipMainCameraView(),
                OnMainChanged       = () => NotifyCameraToolChanged(CameraChangePhase.Committed),
                OnTriChanged        = () => NotifyCameraToolChanged(CameraChangePhase.Committed),
                OnGizmoChanged      = () => UpdateGizmoOverlay(),
            };
            _cameraSubPanel.Build(_layoutRoot.CameraSection);
        }

        /// <summary>BuildLayout の段：デフォーマ・格子変形・スケール・辺ベベル・押し出し・辺トポロジー・ナイフ・厚み付け・線分押し出し。</summary>
        private void BuildDeformAndTopologyTools()
        {
            // デフォーマ。作業軸を基準に選択頂点を変形する。数値 / スライダのみ。
            _deformHandler = new DeformToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint      = () =>
                {
                    // 形状プレビューは曲げの合計角などに追従させたいので、
                    // 再描画だけでなくギズモデータの作り直しまで行う。
                    UpdateGizmoOverlay();
                    _activePanel?.MarkDirtyRepaint();
                },
                GetWorkAxis    = () => CurrentWorkAxis(),
                GetModel       = () => ActiveProject?.CurrentModel,
                GetModelIndex  = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand    = DispatchPanelCommand,
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnApplyCompleted = () => NotifyPanels(ChangeKind.Attributes),
                // 回転ハンドルで角度が変わったらスライダへ書き戻す。
                OnParamsChangedByGizmo = () => _deformSubPanel?.Refresh(),
            };
            _deformHandler.SetUndoController(_editOps?.UndoController);

            // 作業軸フェーズでは作業軸ツールと同じギズモを出す。
            _deformHandler.WorkAxisGizmoProvider = _workAxisHandler;
            // フェーズが変わったら入力経路を張り替える。
            _deformHandler.OnPhaseChanged = () =>
            {
                ApplyDeformToolRouting();
                UpdateGizmoOverlay();
            };

            // 変形パネル先頭へ埋め込む作業軸パネル。左ペインのものと同じ結線で、
            // 同じ WorkAxisContext / WorkAxisToolHandler を操作する。
            _deformWorkAxisSubPanel = new PlayerWorkAxisSubPanel
            {
                GetWorkAxis               = () => CurrentWorkAxis(),
                GetH                      = () => _workAxisHandler,
                SendCommand               = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex             = () => ActiveProject?.CurrentModelIndex ?? 0,
                OnValueChanged            = () =>
                {
                    UpdateGizmoOverlay();
                    _latticeHandler?.OnFrameChanged();
                },
                GetSelectionCentroidWorld = () => SelectedVerticesCentroidWorld(),
                // 左ペインと同じ WorkAxisLibrary を指す。片方で登録したら両方の一覧が揃う。
                GetLibrary                = () => ActiveProject?.WorkAxes,
                OnLibraryChanged          = () => RefreshWorkAxisLibraryLists(),
            };

            _deformSubPanel = new PlayerDeformSubPanel
            {
                GetH          = () => _deformHandler,
                WorkAxisPanel = _deformWorkAxisSubPanel,
            };
            _deformSubPanel.Build(_layoutRoot.DeformSection);

            // 格子変形。格子フレームは作業軸。制御点の選択・移動はビューポートで行う。
            _latticeHandler = new LatticeToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
                GetWorkAxis    = () => CurrentWorkAxis(),
                GetModel       = () => ActiveProject?.CurrentModel,
                GetModelIndex  = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand    = DispatchPanelCommand,
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnStateChanged    = () =>
                {
                    _latticeSubPanel?.Refresh();
                    // 配置中はメッシュ頂点、変形中は格子点。入力先を状態に合わせる。
                    ApplyLatticeToolRouting();
                },
                OnRefreshOverlay  = () => { UpdateTopologyToolsOverlay(); UpdateGizmoOverlay(); },
                OnBoxSelectUpdate = (start, end) => _activePanel?.ShowBoxSelect(start, end),
                OnBoxSelectEnd    = () => _activePanel?.HideBoxSelect(),
                OnApplyCompleted  = () => NotifyPanels(ChangeKind.Attributes),
            };
            _latticeHandler.SetUndoController(_editOps?.UndoController);
            _latticeSubPanel = new PlayerLatticeSubPanel { GetH = () => _latticeHandler };
            _latticeSubPanel.Build(_layoutRoot.LatticeSection);

            _scaleHandler = new ScaleToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetPanelHeight      = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnApplyCompleted    = () => NotifyPanels(ChangeKind.Attributes),
            };
            _scaleHandler.SendCommand = DispatchPanelCommand;
            _scaleHandler.SetProject(ActiveProject);
            _scaleHandler.SetUndoController(_editOps?.UndoController);
            _scaleSubPanel = new PlayerScaleSubPanel { GetH = () => _scaleHandler };
            _scaleSubPanel.Build(_layoutRoot.ScaleSection);
            _edgeBevelHandler = new EdgeBevelToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                // 変換の基準に GPU が計算したワールド座標を使う（CPU で計算し直さない）。
                GetVertexWorldPosition = vi =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    var mc = m?.ActiveMeshContext;
                    if (m == null || mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (UnityEngine.Vector3?)w : null;
                },
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    // ベベルはトポロジー変更後も辺/頂点を表示し続けるため EnterTransformDragging を呼ばない
                },
                OnApplyCompleted = () => NotifyPanels(ChangeKind.ListStructure),
            };
            _edgeBevelHandler.SetProject(ActiveProject);
            _edgeBevelHandler.SetUndoController(_editOps?.UndoController);
            _edgeBevelHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeBevelHandler.SendCommand = DispatchPanelCommand;
            _edgeBevelSubPanel = new PlayerEdgeBevelSubPanel { GetH = () => _edgeBevelHandler };
            _edgeBevelSubPanel.Build(_layoutRoot.EdgeBevelSection);
            _edgeExtrudeHandler = new EdgeExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                // 変換の基準に GPU が計算したワールド座標を使う（CPU で計算し直さない）。
                GetVertexWorldPosition = vi =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    var mc = m?.ActiveMeshContext;
                    if (m == null || mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (UnityEngine.Vector3?)w : null;
                },
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                OnSyncMeshPositions = mc =>
                {
                    // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    // 押し出しはトポロジー変更後も辺/頂点を表示し続けるため EnterTransformDragging を呼ばない
                },
                OnApplyCompleted = () => NotifyPanels(ChangeKind.ListStructure),
            };
            _edgeExtrudeHandler.SetProject(ActiveProject);
            _edgeExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _edgeExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeExtrudeHandler.SendCommand = DispatchPanelCommand;
            _edgeExtrudeSubPanel = new PlayerEdgeExtrudeSubPanel { GetH = () => _edgeExtrudeHandler };
            _edgeExtrudeSubPanel.Build(_layoutRoot.EdgeExtrudeSection);
            _faceExtrudeHandler = new FaceExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                // 変換の基準に GPU が計算したワールド座標を使う（CPU で計算し直さない）。
                GetVertexWorldPosition = vi =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    var mc = m?.ActiveMeshContext;
                    if (m == null || mc == null) return null;
                    return _viewportManager.TryGetVertexWorld(m, mc, vi, out var w)
                        ? (UnityEngine.Vector3?)w : null;
                },
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                OnSyncMeshPositions = mc => { // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。
 _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin); // 新アダプターをTransformDraggingモードに
                },
                OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin),
                OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd),
                OnApplyCompleted = () =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _faceExtrudeHandler.SetProject(ActiveProject);
            _faceExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _faceExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _faceExtrudeHandler.SendCommand = DispatchPanelCommand;
            _faceExtrudeSubPanel = new PlayerFaceExtrudeSubPanel { GetH = () => _faceExtrudeHandler };
            _faceExtrudeSubPanel.Build(_layoutRoot.FaceExtrudeSection);
            _edgeTopologyHandler = new EdgeTopologyToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                OnSyncMeshPositions = mc => { // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。
 _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc); },
                // Phase 2c-3: トポロジ確定（Flip/Dissolve/Split 2 点目）時の一括更新。
                // EnterTopologyChanged 経由で overlay refresh も同期実行される。
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _edgeTopologyHandler.SendCommand = DispatchPanelCommand;
            _edgeTopologyHandler.SetProject(ActiveProject);
            _edgeTopologyHandler.SetUndoController(_editOps?.UndoController);
            _edgeTopologyHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeTopologySubPanel = new PlayerEdgeTopologySubPanel { GetH = () => _edgeTopologyHandler };
            // サブパネル上のモード切替 (Flip/Split/Dissolve ドロップダウン) に連動して
            // Selection.Mode (ホバー有効範囲) を切り替える。
            _edgeTopologySubPanel.OnModeChanged = m => ApplySelectionModeForEdgeTopology(m);
            _edgeTopologySubPanel.Build(_layoutRoot.EdgeTopologySection);
            _knifeHandler = new KnifeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                // 段 (開始頂点 → セグメント辺 → 終了頂点) ごとにホバー種別が変わる。
                // ツール固有 override として通知し、適用先は選択モード権限に任せる。
                ApplyHoverModeToAllMeshes = m =>
                {
                    if (_interactionMode != InteractionMode.Knife) return;
                    SetToolSelectModeOverride(m);
                },
                GetFaceCulledMask   = (ctxIdx, faceCount) => _viewportManager.GetFaceCulledMask(ctxIdx, faceCount, _activeViewport),
                // 切断点の比率をスクリーン空間から 3D 空間へ補正するために使う。
                GetVertexClipW      = vi =>
                {
                    var m  = ActiveProject?.CurrentModel;
                    var mc = m?.ActiveMeshContext;
                    if (m == null || mc == null) return null;
                    return _viewportManager.TryGetVertexClipW(m, mc, vi, _activeViewport, out var w)
                        ? (float?)w : null;
                },
                OnClicked           = () =>
                {
                    // クリック点/辺を一瞬強調して自動で消す（AdvSel と共通のフラッシュ状態）。
                    _advSelFlashEdge   = _knifeHandler.LastClickEdge;
                    _advSelFlashVertex = _advSelFlashEdge.HasValue ? -1 : _knifeHandler.LastClickVertex;
                    int gen = ++_advSelFlashGen;
                    _activePanel?.schedule.Execute(() =>
                    {
                        if (_advSelFlashGen == gen)
                        {
                            _advSelFlashVertex = -1;
                            _advSelFlashEdge   = null;
                            UpdateAdvancedSelectOverlay();
                        }
                    }).StartingIn(300);
                    UpdateAdvancedSelectOverlay();
                    _knifeSubPanel?.Refresh();
                },
                OnSyncMeshPositions = mc => { // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。
 _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _knifeHandler.SendCommand = DispatchPanelCommand;
            _knifeHandler.SetProject(ActiveProject);
            _knifeHandler.SetUndoController(_editOps?.UndoController);
            _knifeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _knifeSubPanel = new PlayerKnifeSubPanel { GetH = () => _knifeHandler };
            _knifeSubPanel.Build(_layoutRoot.KnifeSection);

            _solidifyHandler = new SolidifyToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
                // 生成メッシュの追加はコマンドへ流す。厚み付けの結果は図形パラメータから
                // 決まらないので、出来上がったメッシュをそのまま置くコマンドを使う。
                // 頂点は元メッシュのローカル座標で生成済みなので姿勢は入れ直させない。
                OnMeshCreated = (mo, name, pos, rot, scl, ign, mode, target) =>
                    _commandDispatcher?.Dispatch(new AddGeneratedMeshCommand(
                        ActiveProject?.CurrentModelIndex ?? 0, mo, name,
                        new PrimitivePlacement
                        {
                            WorldPosition          = pos,
                            PlaceRotation          = rot,
                            PlaceScale             = scl,
                            BakeRotation           = true,
                            BakeScale              = true,
                            IgnorePoseInArmature   = ign,
                            AddMode                = mode,
                            AddTargetIndex         = target,
                            MaterialIndex          = -1,
                            MergeDuplicateVertices = false,
                        },
                        poseAlreadyBaked: true)),
            };
            _solidifyHandler.SetProject(ActiveProject);
            _solidifyHandler.SetUndoController(_editOps?.UndoController);
            _solidifyHandler.SetCommandQueue(_editOps?.CommandQueue);
            _solidifySubPanel = new PlayerSolidifySubPanel
            {
                GetH = () => _solidifyHandler,
                GetDrawableIndexList          = BuildDrawableIndexList,
                GetFirstSelectedDrawableIndex = () => ActiveProject?.CurrentModel?.ActiveMeshIndex ?? -1,
                GetView                       = () => ActiveProject,
                SendCommand                   = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _solidifySubPanel.Build(_layoutRoot.SolidifySection);

            // 線分押し出し。選択線分から輪郭ループを検出して新しいメッシュを作る。
            // ツールはマウス入力を持たない（LineExtrudeTool.cs:50-52 が全部 => false）。
            _lineExtrudeHandler = new LineExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _lineExtrudeHandler.SetProject(ActiveProject);
            _lineExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _lineExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _lineExtrudeSubPanel = new PlayerLineExtrudeSubPanel
            {
                GetH        = () => _lineExtrudeHandler,
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _lineExtrudeSubPanel.Build(_layoutRoot.LineExtrudeSection);
        }
    }
}
