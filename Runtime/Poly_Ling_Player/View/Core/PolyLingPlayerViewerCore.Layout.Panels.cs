// PolyLingPlayerViewerCore.Layout.Panels.cs
// Player ビューアのコア：BuildLayout の段（リスト・ブレンド・編集パネル・入出力・図形生成）。
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
        /// <summary>BuildLayout の段：モデル／オブジェクトリスト・スキンW数値・ブレンド・シュリンカー・法線移植・TPS・モデルブレンドのパネル。</summary>
        private void BuildListAndDeformPanels()
        {
            _modelListSubPanel = new ModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            _meshListSubPanel = new MeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);
            // オブジェクトリストのビューポート操作は 3 択（操作なし / 要素選択 / 姿勢調整）。
            // 従来の「ビューポートで選択する」チェック（2 択）は使わない。
            _meshListSubPanel.GetObjectMoveSettings = () => _objectMoveHandler?.GetSettings();
            _meshListSubPanel.OnGizmoRefresh        = UpdateGizmoOverlay;
            _meshListSubPanel.OnViewportOpModeChanged = mode =>
            {
                if (_layoutRoot?.MeshListSection == null) return;
                if (_activeRightSection != _layoutRoot.MeshListSection) return;
                ApplyMeshListViewportOpMode(mode);
            };

            // ObjectMoveTRSPanel は BoneEditorSubPanel に統合済みのため生成不要

            _skinWeightPaintPanel = new PlayerSkinWeightPaintPanel();
            _skinWeightPaintPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintPanel.OnTargetBoneChanged = () => _viewportManager.EnterWeightTargetChanged(ActiveProject);
            _skinWeightPaintPanel.GetToolContext =
                () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinWeightPaintPanel.Build(_layoutRoot.SkinWeightPaintSection);

            _skinWeightNumericSubPanel = new PlayerSkinWeightNumericSubPanel
            {
                GetModel  = () => ActiveProject?.CurrentModel,
                OnRepaint = () => _activePanel?.MarkDirtyRepaint(),
            };
            _skinWeightNumericSubPanel.OnVisualizationTargetChanged =
                () => _viewportManager.EnterWeightTargetChanged(ActiveProject);
            _skinWeightNumericSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinWeightNumericSubPanel.Build(_layoutRoot.SkinWeightNumericSection);

            _blendSubPanel = new PlayerBlendSubPanel();
            _blendSubPanel.OnSyncMeshPositions = mc =>
            {
                // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };
            _blendSubPanel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                _viewportManager.EnterTopologyChanged(proj);
                NotifyPanels(ChangeKind.ListStructure);
            };
            // プレビュー中に法線を再計算した分を GPU へ送る。
            // OnSyncMeshPositions（EnterVerticesMoved/Dragging）は
            // SyncMeshPositionsAndTransform で位置しか送らないため、
            // これを通さないとプレビューの陰影が確定結果と一致しない。
            _blendSubPanel.OnSyncMeshNormals = mc =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null || mc?.MeshObject == null) return;

                if (mc.UnityMesh != null && mc.MeshObject.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    _viewportManager.EnterVertexAttributesChanged(proj, mc, weights: false, uvs: false);
                else
                    _viewportManager.EnterTopologyChanged(proj);
            };
            // プレビュー中にソースを隠す/戻すときの書き戻し。
            // 面は SubmitMeshes が毎フレーム MeshContext.IsVisible を見るので
            // 勝手に消えるが、頂点と辺は GPU 内部の描画フラグで決まる。
            // それを書き戻すのは EnterMeshAttributesChanged だけ。
            _blendSubPanel.OnMeshVisibilityChanged = () =>
            {
                var proj = ActiveProject;
                if (proj == null) return;
                _viewportManager.EnterMeshAttributesChanged(proj);
            };
            _blendSubPanel.OnRepaint          = () => _activePanel?.MarkDirtyRepaint();
            _blendSubPanel.GetUndoController  = () => _editOps?.UndoController;
            _blendSubPanel.GetCommandQueue    = () => _editOps?.CommandQueue;
            // ソースは別モデルから選べる。モデル一覧は IProjectView、
            // 実体の MeshContext は ProjectContext.GetModel から引く。
            _blendSubPanel.GetProjectView     = () => ActiveProject != null
                ? new PlayerProjectView(ActiveProject) : null;
            _blendSubPanel.GetModelContext    = mi =>
                mi >= 0 ? ActiveProject?.GetModel(mi) : null;
            _blendSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _blendSubPanel.Build(_layoutRoot.BlendSection);

            _shrinkSubPanel = new PlayerShrinkSubPanel(Poly_Ling.UI.ShrinkCollisionMode.VertexSegment);
            _shrinkSubPanel.OnSyncMeshPositions = mc =>
            {
                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };
            _shrinkSubPanel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                _viewportManager.EnterTopologyChanged(proj);
                NotifyPanels(ChangeKind.ListStructure);
            };
            _shrinkSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _shrinkSubPanel.GetUndoController = () => _editOps?.UndoController;
            _shrinkSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            // 衝突判定に使うワールド座標は GPU が計算したものだけを参照する。
            _shrinkSubPanel.GetWorldPositions = mc =>
            {
                var model = ActiveProject?.CurrentModel;
                if (model == null) return null;
                return _viewportManager.TryGetMeshWorldPositions(model, mc, out var world) ? world : null;
            };
            // ワールド座標が要るのは衝突計算の直前だけ。毎フレームは呼ばない。
            _shrinkSubPanel.OnRequestUpdateTransform = () => _viewportManager.UpdateTransform();
            _shrinkSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _shrinkSubPanel.Build(_layoutRoot.ShrinkSection);

            // 面方式。頂点方式と同じ配線を、別インスタンス・別セクションに対して行う。
            _shrinkFaceSubPanel = new PlayerShrinkSubPanel(Poly_Ling.UI.ShrinkCollisionMode.FacePair);
            _shrinkFaceSubPanel.OnSyncMeshPositions = mc =>
            {
                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };
            _shrinkFaceSubPanel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                _viewportManager.EnterTopologyChanged(proj);
                NotifyPanels(ChangeKind.ListStructure);
            };
            _shrinkFaceSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _shrinkFaceSubPanel.GetUndoController = () => _editOps?.UndoController;
            _shrinkFaceSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            _shrinkFaceSubPanel.GetWorldPositions = mc =>
            {
                var model = ActiveProject?.CurrentModel;
                if (model == null) return null;
                return _viewportManager.TryGetMeshWorldPositions(model, mc, out var world) ? world : null;
            };
            _shrinkFaceSubPanel.OnRequestUpdateTransform = () => _viewportManager.UpdateTransform();
            _shrinkFaceSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _shrinkFaceSubPanel.Build(_layoutRoot.ShrinkFaceSection);

            _normalTransplantSubPanel = new PlayerNormalTransplantSubPanel();
            // スロット数は変わらないので、法線だけを Unity Mesh へ差し替える。
            // 差し替えられなければメッシュを作り直す。
            _normalTransplantSubPanel.OnSyncMeshNormals = mc =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null || mc?.MeshObject == null) return;

                if (mc.UnityMesh != null && mc.MeshObject.ApplyNormalsToUnityMesh(mc.UnityMesh))
                    _viewportManager.EnterVertexAttributesChanged(proj, mc, weights: false, uvs: false);
                else
                    _viewportManager.EnterTopologyChanged(proj);
            };
            _normalTransplantSubPanel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                _viewportManager.EnterTopologyChanged(proj);
                NotifyPanels(ChangeKind.Attributes);
            };
            _normalTransplantSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _normalTransplantSubPanel.GetUndoController = () => _editOps?.UndoController;
            _normalTransplantSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            // プリズムの構築に使うワールド座標は GPU が計算したものだけを参照する。
            _normalTransplantSubPanel.GetWorldPositions = mc =>
            {
                var model = ActiveProject?.CurrentModel;
                if (model == null) return null;
                return _viewportManager.TryGetMeshWorldPositions(model, mc, out var world) ? world : null;
            };
            // ワールド座標が要るのは法線計算の直前だけ。毎フレームは呼ばない。
            _normalTransplantSubPanel.OnRequestUpdateTransform = () => _viewportManager.UpdateTransform();
            _normalTransplantSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _normalTransplantSubPanel.Build(_layoutRoot.NormalTransplantSection);

            // TPSモーフ。制御点にビューポートの選択頂点を使えるため、
            // パネルごとの「ビューポートで選択する」チェックを差し込む。
            _thinPlateMorphSubPanel = new PlayerThinPlateMorphSubPanel();
            _thinPlateMorphSubPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
            _thinPlateMorphSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _thinPlateMorphSubPanel.Build(_layoutRoot.ThinPlateMorphSection);
            AttachPanelSelectToggle(_layoutRoot.ThinPlateMorphSection, PanelSelectKeyThinPlateMorph);

            _modelBlendSubPanel = new PlayerModelBlendSubPanel();
            _modelBlendSubPanel.SendCommand    = cmd => _commandDispatcher?.Dispatch(cmd);
            _modelBlendSubPanel.GetProjectView = () => ActiveProject != null
                ? new PlayerProjectView(ActiveProject) : null;
            _modelBlendSubPanel.Build(_layoutRoot.ModelBlendSection);
        }

        /// <summary>BuildLayout の段：ボーン・UV・マテリアル・選択辞書・法線・面の表示・オブジェクトグループ・マージ・ブーリアン・モーフ・Tポーズ・Humanoid・揺れもの・VRM 設定・ミラー・Quad減面のパネル。</summary>
        private void BuildEditPanels()
        {
            _boneEditorSubPanel = new PlayerBoneEditorSubPanel();
            _boneEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _boneEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _boneEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _boneEditorSubPanel.SetContext(_panelContext);
            _boneEditorSubPanel.GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0;
            _boneEditorSubPanel.OnFocusCamera     = pos =>
            {
                var orbit = _activeViewport?.Orbit;
                if (orbit != null) { orbit.SetTarget(pos); _activePanel?.MarkDirtyRepaint(); }
            };
            // BoneInputHandler 廃止に伴う ObjectMoveTool 設定共有:
            // サブパネル側のチェックボックスと ObjectMoveHandler 内部の
            // ObjectMoveSettings を同一インスタンスで結びつける。
            _boneEditorSubPanel.GetObjectMoveSettings = () => _objectMoveHandler?.GetSettings();
            _boneEditorSubPanel.OnGizmoRefresh        = UpdateGizmoOverlay;
            _boneEditorSubPanel.RequestBakeObjectScale = BakeObjectScale;
            // ObjectMoveツール用セクションとBoneEditorセクションを統合
            // ObjectMoveTRSSectionは廃止し、BoneEditorSectionを共用する
            _boneEditorSubPanel.Build(_layoutRoot.BoneEditorSection);

            _uvEditorSubPanel = new PlayerUVEditorSubPanel();
            _uvEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _uvEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _uvEditorSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            _uvEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _uvEditorSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _uvEditorSubPanel.Build(_layoutRoot.UVEditorSection);

            _uvUnwrapSubPanel = new PlayerUVUnwrapSubPanel();
            _uvUnwrapSubPanel.GetModel    = () => ActiveProject?.CurrentModel;
            _uvUnwrapSubPanel.SendCommand = cmd => _commandDispatcher?.Dispatch(cmd);
            _uvUnwrapSubPanel.OnRepaint   = () => _activePanel?.MarkDirtyRepaint();
            _uvUnwrapSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _uvUnwrapSubPanel.Build(_layoutRoot.UVUnwrapSection);

            _materialListSubPanel = new PlayerMaterialListSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _materialListSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _materialListSubPanel.Build(_layoutRoot.MaterialListSection);

            _uvzSubPanel = new PlayerUVZSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                SendCommand       = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0,
                GetCameraPosition = () => _viewportManager.GetCurrentToolContext(_activeViewport)?.CameraPosition ?? Vector3.zero,
                GetCameraForward  = () =>
                {
                    var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                    return ctx != null ? (ctx.CameraTarget - ctx.CameraPosition).normalized : Vector3.forward;
                },
                OnEnterUvEditMode = EnterUvEditMode,
                OnExitUvEditMode  = ExitUvEditMode,
            };
            _uvzSubPanel.Build(_layoutRoot.UVZSection);

            _partsSelSetSubPanel = new PlayerPartsSelectionSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _partsSelSetSubPanel.Build(_layoutRoot.PartsSelectionSetSection);

            _normalExcludeSubPanel = new PlayerNormalExcludeSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _normalExcludeSubPanel.Build(_layoutRoot.NormalExcludeSetSection);

            _normalEditSubPanel = new PlayerNormalEditSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _normalEditSubPanel.Build(_layoutRoot.NormalEditSection);

            _faceHideSubPanel = new PlayerFaceHideSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _faceHideSubPanel.Build(_layoutRoot.FaceHideSection);

            _meshSelSetSubPanel = new PlayerMeshSelectionSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _meshSelSetSubPanel.Build(_layoutRoot.MeshSelectionSetSection);

            // オブジェクトグループ。参照の解決がモデルをまたぐので
            // ModelContext ではなく ProjectContext を渡す。
            _objectGroupSubPanel = new PlayerObjectGroupSubPanel
            {
                GetProject  = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _objectGroupSubPanel.Build(_layoutRoot.ObjectGroupSection);

            _mergeMeshesSubPanel = new PlayerMergeMeshesSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _mergeMeshesSubPanel.Build(_layoutRoot.MergeMeshesSection);

            _booleanSubPanel = new PlayerBooleanSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _booleanSubPanel.Build(_layoutRoot.BooleanSection);

            _morphSubPanel = new PlayerMorphSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () =>
                {
                    var model = ActiveProject?.CurrentModel;
                    if (model == null) return null;
                    var ctx = new Poly_Ling.Tools.ToolContext();
                    ctx.Model          = model;
                    ctx.UndoController = _editOps?.UndoController;
                    ctx.SyncMeshContextPositionsOnly = mc =>
                    {
                        // Phase 2a-2c: SyncMeshPositionsAndTransform を EnterVerticesMoved(Dragging) に集約。
                        // Phase 2a-2e: 後続の UpdateTransform は EnterVerticesMoved 内で実行されるため冗長、削除。
                        _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                        _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                        _activePanel?.MarkDirtyRepaint();
                    };
                    ctx.Repaint = () => _activePanel?.MarkDirtyRepaint();
                    return ctx;
                },
            };
            _morphSubPanel.Build(_layoutRoot.MorphSection);

            _morphCreateSubPanel = new PlayerMorphCreateSubPanel
            {
                GetProject          = () => ActiveProject,
                OnRebuildModelList  = RebuildModelList,
                GetUndoController   = () => _editOps?.UndoController,
                SendCommand         = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _morphCreateSubPanel.Build(_layoutRoot.MorphCreateSection);

            _tposeSubPanel = new PlayerTPoseSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _tposeSubPanel.Build(_layoutRoot.TPoseSection);

            _humanoidMappingSubPanel = new PlayerHumanoidMappingSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _humanoidMappingSubPanel.Build(_layoutRoot.HumanoidMappingSection);

            // 揺れもの編集。対象は選択（ボーン優先、無ければ描画オブジェクト）で決まるので
            // ツールコンテキストは要らない。参照の解決はモデル内で閉じる。
            _springBoneSubPanel = new PlayerSpringBoneSubPanel
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),

                // 鎖の強調表示はボーンの線メッシュを作り直して描く。
                // PrepareBones はスロットが dirty のときしか走らないので、
                // 強調表示を書き換えたらここで dirty を立てる。
                OnHighlightChanged = () => _viewportManager?.MarkAllSlotsDirty(),
            };
            _springBoneSubPanel.Build(_layoutRoot.SpringBoneSection);

            // 当たり判定の作成と編集。対象は揺れもの編集と同じく「選択」で決まる。
            _springBoneColliderSubPanel = new PlayerSpringBoneColliderSubPanel
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _springBoneColliderSubPanel.Build(_layoutRoot.SpringBoneColliderSection);

            // マッスル可動域の編集。対象はボーン選択で決まる。
            _humanLimitSubPanel = new PlayerHumanLimitSubPanel
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _humanLimitSubPanel.Build(_layoutRoot.HumanLimitSection);

            // VRM 出力設定。対象はモデル全体（一人称だけメッシュ選択で決まる）。
            _vrmSettingsSubPanel = new PlayerVrmSettingsSubPanel
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vrmSettingsSubPanel.Build(_layoutRoot.VrmSettingsSection);

            _mirrorSubPanel = new PlayerMirrorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _mirrorSubPanel.Build(_layoutRoot.MirrorSection);

            _quadDecimatorSubPanel = new PlayerQuadDecimatorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _quadDecimatorSubPanel.Build(_layoutRoot.QuadDecimatorSection);
        }

        /// <summary>BuildLayout の段：下絵・軸／グリッド・作業フォルダ・キャプチャ・リモートサーバ・ログ・頂点移動・ピボット・スカルプト・詳細選択・入出力。</summary>
        private void BuildIoAndMiscPanels()
        {
            _underlaySubPanel = new PlayerUnderlaySubPanel(_underlay, ApplyAllUnderlays);
            _underlaySubPanel.Build(_layoutRoot.UnderlaySection);

            _gridAxisSubPanel = new PlayerGridAxisSubPanel(
                () => _viewportManager.GetGridSettings(),
                gs => _viewportManager.EnterDisplaySettingsChanged(gs));
            _gridAxisSubPanel.Build(_layoutRoot.GridAxisSection);

            // 作業フォルダ（PLSandbox の根）。コマンドは通さない。
            // リモートから根を書き換えられると境界の意味が消えるため。
            _workFolderSubPanel = new PlayerWorkFolderSubPanel();
            _workFolderSubPanel.Build(_layoutRoot.WorkFolderSection);

            _captureSubPanel = new PlayerCaptureSubPanel
            {
                OnCapture = ExecuteCapture,
            };
            _captureSubPanel.Build(_layoutRoot.CaptureSection);

            _remoteServerSubPanel = new PlayerRemoteServerSubPanel
            {
                GetServer = () => _playerServer,
            };
            _remoteServerSubPanel.Build(_layoutRoot.RemoteServerSection);

            _logSubPanel = new PlayerLogSubPanel();
            _logSubPanel.Build(_layoutRoot.LogSection);

            _vertexMoveSubPanel = new PlayerVertexMoveSubPanel
            {
                GetHandler = () => _moveToolHandler,
            };
            _vertexMoveSubPanel.SetContext(_panelContext);
            _vertexMoveSubPanel.Build(_layoutRoot.VertexMoveSection);

            _pivotSubPanel = new PlayerPivotSubPanel();
            _pivotSubPanel.Build(_layoutRoot.PivotSection);
            _pivotSubPanel.OnPivotToVertexCentroid = () => MovePivotToCentroid(useBones: false);
            _pivotSubPanel.OnPivotToBoneCentroid   = () => MovePivotToCentroid(useBones: true);

            _sculptSubPanel = new PlayerSculptSubPanel
            {
                GetHandler              = () => _sculptHandler,
                GetTempMirror           = () => _tempMirrorController,
                GetTempMirrorOwnerToken = () => (int)InteractionMode.Sculpt,
            };
            _sculptSubPanel.Build(_layoutRoot.SculptSection);
            // 起動時にスライダ範囲・値・詳細設定をハンドラ実値へ同期する。
            _sculptSubPanel.Refresh();

            // 一時ミラーの実体化・解除は自動解除経路からも起きるため、
            // 状態が変わったらボタン表示を持つサブパネルを同期する。
            if (_tempMirrorController != null)
                _tempMirrorController.OnStateChanged += () => _sculptSubPanel?.Refresh();

            _advancedSelectSubPanel = new PlayerAdvancedSelectSubPanel
            {
                GetHandler  = () => _advancedSelectHandler,
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _advancedSelectSubPanel.Build(_layoutRoot.AdvancedSelectSection);

            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            _importSubPanel = new PlayerImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;
            _importSubPanel.OnImportObj = OnImportObj;
            _importSubPanel.OnImportVrm = OnImportVrm;
            AttachPanelSelectToggle(_layoutRoot.ImportSection, PanelSelectKeyImport);

            _exportSubPanel = new PlayerExportSubPanel();
            _exportSubPanel.Build(_layoutRoot.ExportSection);
            // 受け口（Execute*ExportFile）が失敗理由を返せるよう、これらは string を返す。
            // パネル経路では戻り値を使わないのでラムダで捨てる。
            _exportSubPanel.OnExportPmx = (p, s) => OnExportPmx(p, s);
            _exportSubPanel.OnExportMqo = (p, s) => OnExportMqo(p, s);
            _exportSubPanel.OnExportObj = (p, s) => OnExportObj(p, s);
            _exportSubPanel.OnExportVrm = (p, s) => OnExportVrm(p, s);
            AttachPanelSelectToggle(_layoutRoot.ExportSection, PanelSelectKeyExport);

            _projectSaveSubPanel = new PlayerProjectFileSubPanel
            {
                Mode = PlayerProjectFileSubPanel.PanelMode.Save,
            };
            _projectSaveSubPanel.Build(_layoutRoot.ProjectSaveSection);
            _projectSaveSubPanel.OnSave    = p => OnSaveProject(p);
            _projectSaveSubPanel.OnSaveCsv = p => OnSaveCsvProject(p);
            AttachPanelSelectToggle(_layoutRoot.ProjectSaveSection, PanelSelectKeyProjectSave);

            _projectLoadSubPanel = new PlayerProjectFileSubPanel
            {
                Mode = PlayerProjectFileSubPanel.PanelMode.Load,
            };
            _projectLoadSubPanel.Build(_layoutRoot.ProjectLoadSection);
            _projectLoadSubPanel.OnLoad    = p => OnLoadProject(p);
            _projectLoadSubPanel.OnLoadCsv = (p, m) => OnLoadCsvProject(p, m);
            AttachPanelSelectToggle(_layoutRoot.ProjectLoadSection, PanelSelectKeyProjectLoad);

            _partialImportSubPanel = new PlayerPartialImportSubPanel();
            _partialImportSubPanel.Build(_layoutRoot.PartialImportSection);
            _partialImportSubPanel.OnImportDone = OnPartialImportDone;

            _partialExportSubPanel = new PlayerPartialExportSubPanel();
            _partialExportSubPanel.Build(_layoutRoot.PartialExportSection);
        }

        /// <summary>BuildLayout の段：図形生成（通常／3D連携／サンドボックス）・配置ギズモ・MeshFilter→Skinned・種別変換。</summary>
        private void BuildPrimitiveAndSkinPanels()
        {
            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            // 最後に選んだ図形の保存キー。Build 内で読み込むため Build より前に設定する。
            _primitiveSubPanel.MemoryKey = "Primitive";
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            // 生成はコマンドへ流す。モデルへの反映はディスパッチャの受け口が行う。
            _primitiveSubPanel.SendCommand  = cmd => _commandDispatcher?.Dispatch(cmd);
            _primitiveSubPanel.GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0;
            _primitiveSubPanel.GetSelectedMeshObject = () =>
                ActiveProject?.CurrentModel?.ActiveMeshContext?.MeshObject;
            _primitiveSubPanel.GetSelectedFaceIndices = () =>
                ActiveProject?.CurrentModel?.ActiveMeshContext?.SelectedFaces;
            _primitiveSubPanel.GetDrawableMeshList = BuildDrawableMeshList;
            _primitiveSubPanel.GetDrawableMeshEntryList = BuildDrawableMeshEntryList;
            _primitiveSubPanel.GetSubtreeMeshList       = BuildSubtreeMeshList;
            _primitiveSubPanel.GetExistingMeshNames = BuildExistingMeshNames;
            // マテリアル指定ドロップダウンの選択肢。
            _primitiveSubPanel.GetMaterialNames = BuildMaterialNames;
            _primitiveSubPanel.GetUndoController = () => _editOps?.UndoController;
            // 歪み複製（高度な図形）。作業軸を基準に複製＋歪みを行う。
            _primitiveSubPanel.GetDrawableIndexList  = BuildDrawableIndexList;
            // 追加先ドロップダウン（名前欄の差し替え先）の既定選択。
            _primitiveSubPanel.GetFirstSelectedDrawableIndex = () => ActiveProject?.CurrentModel?.ActiveMeshIndex ?? -1;
            _primitiveSubPanel.OnObjectArrayGenerate = SendObjectArrayCommand;
            // 辺から帯面（高度な図形）。選択辺の本数と対象はモデル側から読む。
            WireEdgeRibbonFaceCallbacks(_primitiveSubPanel);
            // 穴つなぎ（ブリッジ）。種の取り込みと実生成は Viewer 側が持つ。
            WireBridgeCallbacks(_primitiveSubPanel);
            AttachPanelSelectToggle(_layoutRoot.PrimitiveSection, PanelSelectKeyPrimitive);

            // 左ペインの「基本図形」「高度な図形」ボタンは廃止した。
            // PrimitiveSection はショートカット（ShowPrimitiveShape）と
            // 「穴つなぎ」ボタンから開く。

            // 検証用の新サブツール。同一クラスの別インスタンスで、既存とは状態を共有しない。
            // 生成結果の扱い (生成コマンド以降) は既存と完全に同じ経路を通す。
            _livePrimitiveSubPanel = new PlayerPrimitiveMeshSubPanel();

            // メイン3Dウインドウへ生成予定形状の黄色ワイヤを描画する（新サブツールのみ）。
            // 配置ギズモのサブモード切替 UI も同フラグで分岐するため、Build より前に立てる。
            _livePrimitiveSubPanel.LiveWireInMainViewport = true;
            _livePrimitiveSubPanel.IsMainViewportCamera =
                cam => _viewportManager != null && _viewportManager.IsViewportCamera(cam);
            _livePrimitiveSubPanel.GetAddTargetWorldMatrix =
                () => ActiveProject?.CurrentModel?.ActiveMeshContext?.WorldMatrix ?? Matrix4x4.identity;

            // 最後に選んだ図形の保存キー。既存インスタンスとは別枠で記憶する。
            _livePrimitiveSubPanel.MemoryKey = "LivePrimitive";

            // 揺れもの用ボーン鎖は、取り付け先ボーンの一覧を出すためにモデルを見る。
            _livePrimitiveSubPanel.GetModelContext = () => ActiveProject?.CurrentModel;

            _livePrimitiveSubPanel.Build(_layoutRoot.LivePrimitiveSection, _sceneRoot);
            _livePrimitiveSubPanel.SendCommand  = cmd => _commandDispatcher?.Dispatch(cmd);
            _livePrimitiveSubPanel.GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0;
            _livePrimitiveSubPanel.GetSelectedMeshObject = () =>
                ActiveProject?.CurrentModel?.ActiveMeshContext?.MeshObject;
            _livePrimitiveSubPanel.GetSelectedFaceIndices = () =>
                ActiveProject?.CurrentModel?.ActiveMeshContext?.SelectedFaces;
            _livePrimitiveSubPanel.GetDrawableMeshList = BuildDrawableMeshList;
            _livePrimitiveSubPanel.GetDrawableMeshEntryList = BuildDrawableMeshEntryList;
            _livePrimitiveSubPanel.GetSubtreeMeshList       = BuildSubtreeMeshList;
            _livePrimitiveSubPanel.GetExistingMeshNames = BuildExistingMeshNames;
            // マテリアル指定ドロップダウンの選択肢。
            _livePrimitiveSubPanel.GetMaterialNames = BuildMaterialNames;
            _livePrimitiveSubPanel.GetUndoController = () => _editOps?.UndoController;
            // 歪み複製（新しい高度）。既存インスタンスとは状態を共有しない。
            _livePrimitiveSubPanel.GetDrawableIndexList  = BuildDrawableIndexList;
            _livePrimitiveSubPanel.GetFirstSelectedDrawableIndex = () => ActiveProject?.CurrentModel?.ActiveMeshIndex ?? -1;
            _livePrimitiveSubPanel.OnObjectArrayGenerate = SendObjectArrayCommand;
            // 辺から帯面（新しい高度）。既存インスタンスと同じ経路を通す。
            WireEdgeRibbonFaceCallbacks(_livePrimitiveSubPanel);
            // 穴つなぎ（ブリッジ）。既存インスタンスと同じ経路を通す。
            WireBridgeCallbacks(_livePrimitiveSubPanel);

            // ── MCP用サンドボックス ────────────────────────────────
            // 専用のインスタンスは持たない。_livePrimitiveSubPanel を
            // ShapeCategory.Sandbox で開く（ShowMcpSandboxPanel）。
            // 配置ギズモ・ライブワイヤ・材質・追加先は 3D連携と同じ配線をそのまま使う。

            // 配置ギズモ。モデルには触れず、サブパネルの TRS だけを読み書きする。
            _primitivePlaceHandler = new PrimitivePlaceToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),

                GetPosition = () => _livePrimitiveSubPanel?.PlacePosition ?? Vector3.zero,
                SetPosition = v => { if (_livePrimitiveSubPanel != null) _livePrimitiveSubPanel.PlacePosition = v; },
                GetRotation = () => _livePrimitiveSubPanel?.PlaceRotation ?? Vector3.zero,
                SetRotation = v => { if (_livePrimitiveSubPanel != null) _livePrimitiveSubPanel.PlaceRotation = v; },
                GetScale    = () => _livePrimitiveSubPanel?.PlaceScale ?? Vector3.one,
                SetScale    = v => { if (_livePrimitiveSubPanel != null) _livePrimitiveSubPanel.PlaceScale = v; },

                // ギズモ中心はワールド座標。AddToExisting のときのみ追加先の WorldMatrix を掛ける。
                GetGizmoWorldCenter = () => LivePrimitiveGizmoCenter(),
                // 同モードでは _worldPos が追加先ローカル空間の値なので、ワールド差分を戻す。
                WorldDeltaToLocal   = d => LivePrimitiveWorldDeltaToLocal(d),

                OnValueChanged = () =>
                {
                    _livePrimitiveSubPanel?.NotifyPlaceTrsChanged();
                    UpdateGizmoOverlay();
                    // 原点マーカーの仮表示は生成位置に追従させる。
                    UpdateBoneOverlay();
                },
            };

            // 配置ギズモ・姿勢仮表示の設定を、ハンドラとサブパネルで共有する。
            _primitivePlaceHandler.Settings   = _primitivePlaceSettings;
            _livePrimitiveSubPanel.PlaceSettings = _primitivePlaceSettings;
            _livePrimitiveSubPanel.OnPlaceOverlayChanged = () =>
            {
                UpdateGizmoOverlay();
                UpdateBoneOverlay();
            };
            _livePrimitiveSubPanel.RefreshPlaceToggles();

            _layoutRoot.LivePrimitiveBtn.clicked += ShowLivePrimitivePanel;
            _layoutRoot.LiveAdvancedPrimitiveBtn.clicked += ShowLiveAdvancedPrimitivePanel;
            _layoutRoot.LiveMechanismPrimitiveBtn.clicked += ShowLiveMechanismPrimitivePanel;
            _layoutRoot.LiveSpringBonePrimitiveBtn.clicked += ShowLiveSpringBonePrimitivePanel;

            if (_layoutRoot.McpSandboxBtn != null)
                _layoutRoot.McpSandboxBtn.clicked += ShowMcpSandboxPanel;

            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;
            _mfToSkinnedSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);

            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _skinKindSubPanel = new PlayerSkinKindSubPanel();
            _skinKindSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinKindSubPanel.Build(_layoutRoot.SkinKindSection);

            _layoutRoot.SkinKindBtn.clicked += ShowSkinKindPanel;
        }
    }
}
