// PolyLingPlayerViewerCore.VertexInteraction.cs
// Player ビューアのコア：頂点インタラクション（ビューポート入力とハンドラ）のセットアップ。
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
        // 頂点インタラクション セットアップ
        // ================================================================

        private void SetupVertexInteraction()
        {
            _selectionOps = new PlayerSelectionOps(_selectionState);

            // SelectionState は経路ごとに別インスタンスへ差し替わる
            // (EnterSceneReset / SelectMeshCommand / SelectElementsCommand / 高度選択 等)。
            // 差し替え直後に実効選択モードを再適用しないと、新インスタンスの既定値
            // (Vertex|Edge|Face|Line) のままになりチェックボックスが無効化される。
            _selectionOps.OnStateInstalled = _ => ApplySelectMode();

            // 複数オブジェクト選択対応:
            // PlayerSelectionOps はクリック／矩形／投げ縄の書き込み先を
            // 「当たったメッシュの MeshContext.Selection」へ振り分ける。
            // その解決に現在のモデルが要るため結線する。
            // 未設定だと従来どおり単一 SelectionState だけを操作する。
            _selectionOps.GetModel = () => ActiveProject?.CurrentModel;

            _selectionOps.OnSelectionChanged = () =>
            {
                _renderer?.NotifySelectionChanged();
                // 選択変更を可視サブパネルへ反映する（例: マテリアルの「選択面に適用」
                // セクションは Refresh 内で SelectionState.Faces を見て表示可否を決めるため、
                // 選択しただけでは更新されずセクションが出ない問題を防ぐ）。
                foreach (var (section, refresh) in _sectionRefreshPairs)
                    if (section?.style.display == DisplayStyle.Flex) refresh();
            };

            _moveToolHandler = new MoveToolHandler(_selectionOps, ActiveProject)
            {
                // クリック選択はコマンド発行に寄せてある。
                // 送り先は他パネルと同じ DispatchPanelCommand。
                SendCommand = DispatchPanelCommand,

                // Phase 2b-1: 正規入口 EnterVerticesMoved(Dragging, syncMc) 経由に切替。
                // 軽量同期 (SyncMeshPositionsAndTransform + UpdateTransform) + overlay 更新を一元化。
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnRepaint = () => _activePanel?.MarkDirtyRepaint(),

                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                GetToolContext  = () => _viewportManager.GetCurrentToolContext(_activeViewport),

                // 辺／面／線分の選択を構成頂点へ展開するかどうか（左ペインのチェックボックス）。
                GetExpandToVertexKinds = () => _expandToVertexKinds,

                GetScreenPositions = () => _viewportManager.GetScreenPositions(),
                GetVertexOffset    = ctxIdx => _viewportManager.GetVertexOffset(ctxIdx),
                IsVertexVisible    = gi  => _viewportManager.IsVertexVisible(gi),
                GetViewportHeight  = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                GetPanelHeight     = () => _activeViewport?.Cam?.pixelHeight ?? 0f,

                OnBoxSelectUpdate = (start, end) => _activePanel?.ShowBoxSelect(start, end),
                OnBoxSelectEnd    = () => _activePanel?.HideBoxSelect(),

                OnLassoSelectUpdate = points => _activePanel?.ShowLassoSelect(points),
                OnLassoSelectEnd    = () => _activePanel?.HideLassoSelect(),

                OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin),
                OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd),
                // ドラッグ開始時に選択が変わったときだけ呼ばれる。EnterSelectionChanged は
                // UpdateSelectedDrawableMesh → PresentAll まで同期実行するので、
                // TransformDragging へ入る前に選択が GPU へ届く。
                OnCommitSelectionSync    = () => _viewportManager.EnterSelectionChanged(ActiveProject),
                OnEnterBoxSelecting      = () => _viewportManager.EnterBoxSelecting(),
                OnReadBackVertexFlags    = () => _viewportManager.ReadBackVertexFlags(),
                OnExitBoxSelecting       = () => _viewportManager.ExitBoxSelecting(),
                OnRequestNormal          = () => _viewportManager.RequestNormal(),
                // Phase 2a-2d: ClearMouseHover → EnterHoverChanged(None) に集約。
                OnClearMouseHover        = () => _viewportManager.EnterHoverChanged(_activeViewport, Vector2.zero, HoverTargetKind.None),
            };
            _moveToolHandler.SetUndoController(_editOps?.UndoController);
            _viewportManager.RegisterMoveToolHandler(_moveToolHandler);

            // リモート連動: 頂点移動確定時に、フラグとモードに応じて送信/配信する。
            _moveToolHandler.OnVerticesCommitted = mc =>
            {
                UnityEngine.Debug.Log($"[EditSync] commit mc=\"{mc?.Name}\" C2S={SyncClientToServer} S2C={SyncServerToClient} mode={_remoteMode} client={_client!=null} server={_playerServer!=null}");
                if (mc?.MeshObject == null) return;
                // 対象を明示して送る（ObjectId をヘッダに載せる）。
                // これが無いと受信側は先頭描画メッシュへ当ててしまい、
                // 複数人が同時に編集すると全員の変更が同じメッシュに流れ込む。
                int __mi = ActiveProject?.CurrentModelIndex ?? 0;
                if (SyncClientToServer && _remoteMode == RemoteMode.Client && _client != null)
                {
                    _client.SendBinary(RemoteBinarySerializer.SerializePositionsOnly(mc, __mi));
                }
                else if (SyncServerToClient && _remoteMode == RemoteMode.Server && _playerServer != null)
                {
                    _playerServer.BroadcastPositions(mc, __mi);
                }
            };

            // Phase 2b-1 / 2c: overlay 再描画コールバックを配線する。
            // 面ホバー/選択面は Phase 2c で GPU 描画パスに統合されたため配線不要
            // （_FaceFlagsBuffer を見てシェーダが自動追従で塗る）。
            // ギズモ overlay のみ UIToolkit Painter2D で残置、従来どおりコールバック駆動。
            _viewportManager.OnRefreshGizmoOverlay = UpdateGizmoOverlay;
            // Phase 2c-2: ボーン wire は Poly_Ling/Bone3D_Overlay で GPU 描画されるが、
            // UIToolkit 菱形マーカー（_boneWireData）は HitTestOverlayIndicator の
            // クリック当たり判定補助として残置している。
            // 【将来別途検討】3D wire と菱形マーカーが視覚的に重複するため、
            // 3D 表示モード整理時に菱形マーカーの要否を再検討する。
            _viewportManager.OnRefreshBoneOverlay = UpdateBoneOverlay;
            // Phase 2c-3: ツール固有 overlay を各 Enter* 入口末尾から駆動する。
            // 各ハンドラ側は内部状態（ホバー辺、プレビュー点、confirm 済み点等）を保持し、
            // ここで呼ばれる Update*Overlay が現在の視点で再投影して panel.Show*Preview に渡す。
            // Tool が無効なときは Update*Overlay 冒頭の if (_interactionMode != InteractionMode.X) ガードで早期 return。
            _viewportManager.OnRefreshAddFaceOverlay        = UpdateAddFaceOverlay;
            _viewportManager.OnRefreshTopologyToolsOverlay  = UpdateTopologyToolsOverlay;
            _viewportManager.OnRefreshAdvancedSelectOverlay = UpdateAdvancedSelectOverlay;
            // Phase 2a-2b-2 Batch 3: EnterSceneReset から Core の _selectionOps を呼ぶためのブリッジ。
            // これにより ViewportManager が Core の参照を持たずに選択初期化を届けられる。
            _viewportManager.OnSetSelectionState = sel =>
            {
                _selectionOps?.SetSelectionState(sel);
            };
            // モデルロード / トポロジ変更 / Undo 適用では MeshContext と SelectionState が
            // 作り直される。作り直された側へ実効選択モードを再適用する。
            _viewportManager.OnApplySelectMode = ApplySelectMode;

            _objectMoveHandler = new ObjectMoveToolHandler();
            _objectMoveHandler.SendCommand = DispatchPanelCommand;
            _objectMoveHandler.SetProject(ActiveProject);
            _objectMoveHandler.SetUndoController(_editOps?.UndoController);
            _objectMoveHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _objectMoveHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _objectMoveHandler.OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _objectMoveHandler.OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
            _objectMoveHandler.OnMeshSelectionChanged   = () => { };
            // BoneInputHandler 廃止に伴う移植:
            // 選択カテゴリ問わず発火。EnterTopologyChanged + BoneEditor Refresh +
            // NotifyPanels(Selection) を行う。
            _objectMoveHandler.OnSelectionChanged = () =>
            {
                var m = ActiveProject?.CurrentModel;
                PLDiag.SelList(
                    "pick -> " +
                    (m == null
                        ? "model=null"
                        : $"cat={m.ActiveCategory} " +
                          $"mesh=[{string.Join(",", m.SelectedDrawableMeshIndices)}] " +
                          $"bone=[{string.Join(",", m.SelectedBoneIndices)}] " +
                          $"mode={_interactionMode}"));

                _viewportManager.EnterTopologyChanged(ActiveProject);
                _boneEditorSubPanel?.Refresh();
                NotifyPanels(ChangeKind.Selection);
            };
            // 描画メッシュ側に選択カテゴリが切り替わった場合の GPU 側ハイライト更新。
            _objectMoveHandler.OnDrawableMeshSelectionChanged = () =>
            {
                _renderer?.UpdateSelectedDrawableMesh(0, ActiveProject?.CurrentModel);
            };
            _objectMoveHandler.OnSyncBoneTransforms     = () =>
            {
                var proj = ActiveProject;

                if (proj?.CurrentModel != null)

                {

                    proj.CurrentModel.ComputeWorldMatrices();

                    // Phase 2a-2e: ComputeWorldMatrices + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(proj, VerticesMovedPhase.Dragging);

                    // EnterVerticesMoved(Dragging) は syncMc=null のため PresentAll 経路を通り、
                    // GPU の transform 行列(_transformMatrices=WorldMatrix)を更新しない。
                    // 頂点移動(syncMc!=null)経路のみが UpdateTransform を呼ぶため、オブジェクト移動では
                    // WorldMatrix 変更が描画に反映されない。ここで明示的に反映する。
                    _viewportManager.UpdateTransform();

                }
                NotifyPanels(ChangeKind.Attributes);
            };
            _objectMoveHandler.OnSyncMeshPositions = mc =>
            {
                // OriginOnly の自頂点補償を GPU へ反映（PivotOffsetHandler と同じ経路）。
                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };

            // オブジェ矩形 / 投げ縄選択の UI 描画コールバック。
            // MoveToolHandler (頂点) と同じ panel API を使い、見た目を完全統一する。
            // オブジェ選択はピボット 1 点判定でカリング不要なため
            // EnterBoxSelecting / ExitBoxSelecting (GPU カリング関連) は呼ばない。
            _objectMoveHandler.OnBoxSelectUpdate   = (s, e) => _activePanel?.ShowBoxSelect(s, e);
            _objectMoveHandler.OnBoxSelectEnd      = ()     => _activePanel?.HideBoxSelect();
            _objectMoveHandler.OnLassoSelectUpdate = pts    => _activePanel?.ShowLassoSelect(pts);
            _objectMoveHandler.OnLassoSelectEnd    = ()     => _activePanel?.HideLassoSelect();
            // ドラッグ中断・異常終了時の後片付け (両種の描画を確実に消す)
            _objectMoveHandler.OnExitBoxSelecting  = () =>
            {
                _activePanel?.HideBoxSelect();
                _activePanel?.HideLassoSelect();
            };

            _pivotOffsetHandler = new PivotOffsetToolHandler();
            // ドラッグ確定はコマンド発行に寄せてある。送り先は他パネルと同じ。
            _pivotOffsetHandler.SendCommand = DispatchPanelCommand;
            _pivotOffsetHandler.SetProject(ActiveProject);
            _pivotOffsetHandler.SetUndoController(_editOps?.UndoController);
            _pivotOffsetHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _pivotOffsetHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _pivotOffsetHandler.OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _pivotOffsetHandler.OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
            _pivotOffsetHandler.OnSyncBoneTransforms     = () =>
            {
                var proj = ActiveProject;

                if (proj?.CurrentModel != null)

                {

                    proj.CurrentModel.ComputeWorldMatrices();

                    // Phase 2a-2e: ComputeWorldMatrices + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(proj, VerticesMovedPhase.Dragging);

                    // EnterVerticesMoved(Dragging) は syncMc=null で PresentAll 経路を通り GPU の
                    // transform 行列(_transformMatrices=WorldMatrix)を更新しない。ピボットの原点移動
                    // (BoneTransform)を描画へ反映するため明示的に UpdateTransform を呼ぶ。
                    _viewportManager.UpdateTransform();

                }
                NotifyPanels(ChangeKind.Attributes);
            };
            _pivotOffsetHandler.OnSyncMeshPositions = mc =>
            {
                // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };

            _sculptHandler = new SculptToolHandler();
            // ストローク確定はコマンド発行に寄せてある。送り先は他パネルと同じ。
            _sculptHandler.SendCommand = DispatchPanelCommand;
            _sculptHandler.SetProject(ActiveProject);
            _sculptHandler.SetUndoController(_editOps?.UndoController);
            _sculptHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _sculptHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _sculptHandler.OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _sculptHandler.OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
            _sculptHandler.OnSyncMeshPositions = mc =>
            {
                // Phase 2a-2c: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
            };
            _sculptHandler.OnUpdateBrushCircle = (center, radius) =>
                _activePanel?.ShowBrushCircle(center, radius);
            _sculptHandler.OnUpdateRadiusDragMarker = (center, radius) =>
                _activePanel?.ShowBrushCircle(center, radius, new Color(1f, 0.6f, 0.1f, 0.9f), showCenter: true);
            _sculptHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();
            _sculptHandler.GetBrushHit = (pos, r) => _viewportManager.GetBrushHit(pos, r);

            // ツール内「一時ミラー」。ツール横断で 1 つだけ持ち、
            // 実体化したツールから離れたときに SetInteractionMode が解除する。
            _tempMirrorController = new TempMirrorController
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };

            _advancedSelectHandler = new AdvancedSelectToolHandler();
            // クリック選択はコマンド発行に寄せてある。送り先は他パネルと同じ。
            _advancedSelectHandler.SendCommand = DispatchPanelCommand;
            _advancedSelectHandler.SetProject(ActiveProject);
            _advancedSelectHandler.SetSelectionOps(_selectionOps);
            _advancedSelectHandler.SetUndoController(_editOps?.UndoController);
            _advancedSelectHandler.GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            // Belt / EdgeLoop は辺と補助線分、ShortestPath は頂点に絞る。
            // 属性系サブモードは null が来るのでチェックボックスの指定に戻る。
            _advancedSelectHandler.OnRequestSelectModeOverride = m =>
            {
                if (_interactionMode != InteractionMode.AdvancedSelect) return;
                _toolSelectModeOverride = m;
                ApplySelectMode();
            };
            _advancedSelectHandler.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _advancedSelectHandler.OnSelectionChanged = () =>
            {
                _renderer?.NotifySelectionChanged();
                _viewportManager.RequestNormal();

                // 接続／ベルト／辺ループ：クリック点を一瞬強調して自動で消す（選択完了直後のフラッシュ）。
                // 最短は常設始点マーカーを持つため対象外。
                if (_advancedSelectHandler.Mode != Poly_Ling.Tools.AdvancedSelectMode.ShortestPath)
                {
                    _advSelFlashEdge   = _advancedSelectHandler.LastClickEdge;
                    // 辺クリック時は辺を強調するので頂点フラッシュは出さない。
                    _advSelFlashVertex = _advSelFlashEdge.HasValue
                                         ? -1 : _advancedSelectHandler.LastClickVertex;
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
                }

                UpdateAdvancedSelectOverlay();   // 始点／フラッシュマーカーを即時反映
            };
            _advancedSelectHandler.GetHoverElement =
                mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel);

            _skinWeightPaintHandler = new SkinWeightPaintToolHandler();
            _skinWeightPaintHandler.SetProject(ActiveProject);
            _skinWeightPaintHandler.SetUndoController(_editOps?.UndoController);
            _skinWeightPaintHandler.SetCommandQueue(_editOps?.CommandQueue);
            _skinWeightPaintHandler.SendCommand              = DispatchPanelCommand;
            _skinWeightPaintHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintHandler.OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _skinWeightPaintHandler.OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
            _skinWeightPaintHandler.OnSyncMeshPositions = mc =>
            {
                // boneWeights 変更はトポロジ不変。RebuildAdapter を伴う EnterTopologyChanged ではなく、
                // 当該メッシュのウェイトのみ GPU へ部分転送する EnterVertexAttributesChanged を使う。
                var proj = ActiveProject;
                if (proj?.CurrentModel != null)
                {
                    _viewportManager.EnterVertexAttributesChanged(proj, mc, weights: true, uvs: false);
                }
            };
            _skinWeightPaintHandler.OnUpdateBrushCircle = (center, radius, color) =>
                _activePanel?.ShowBrushCircle(center, radius, color);
            _skinWeightPaintHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();
            _skinWeightPaintHandler.GetScreenPositions       = () => _viewportManager.GetScreenPositions();
            _skinWeightPaintHandler.GetVertexOffset          = ctxIdx => _viewportManager.GetVertexOffset(ctxIdx);
            _skinWeightPaintHandler.IsVertexVisible          = gi => _viewportManager.IsVertexVisible(gi);
            _skinWeightPaintHandler.GetViewportHeight        = () => _activeViewport?.Cam?.pixelHeight ?? 0f;
            _skinWeightPaintHandler.IsBackfaceCullingEnabled = () => _renderer?.BackfaceCullingEnabled ?? true;

            _vertexInteractor = new PlayerVertexInteractor(_selectionOps)
            {
                GetHoverHit = () => _viewportManager.GetHoverHit(),
            };
            _vertexInteractor.SetToolHandler(_moveToolHandler);

            _activePanel    = _layoutRoot?.PerspectivePanel;
            _activeViewport = _viewportManager.PerspectiveViewport;

            if (_activePanel != null)
                _vertexInteractor.Connect(_activePanel);

            void ConnectPanelHover(PlayerViewportPanel panel, PlayerViewport vp)
            {
                if (panel == null) return;

                panel.OnPointerMoved += (pos, mods) =>
                {
                    _lastMouseScreenPos = pos;
                    if (_layoutRoot?.BoneEditorSection != null &&
                        _layoutRoot.BoneEditorSection.style.display == DisplayStyle.Flex)
                    {
                        var boneCtx = _viewportManager.GetCurrentToolContext(_activeViewport);
                        _objectMoveHandler?.UpdateHover(pos, boneCtx);
                    }
                };

                panel.OnPointerHover += localPos =>
                {
                    if (_activePanel != panel)
                    {
                        if (_activePanel != null)
                        {
                            _activePanel.HideBoxSelect();
                            _activePanel.HideFaceHover();
                            _activePanel.HideGizmo();
                            // ブラシ円は常時表示なので、旧ビューポートに残さない。
                            _activePanel.HideBrushCircle();
                            _vertexInteractor.Disconnect(_activePanel);
                        }
                        _activePanel    = panel;
                        _activeViewport = vp;
                        _vertexInteractor.Connect(_activePanel);
                    }
                    // Phase 2b-1: 正規入口 EnterHoverChanged 経由。
                    // 入口末尾で面ホバー/ギズモ overlay refresh が発火される。
                    // Phase 2b 以降で HoverTargetKind を現行ツールから取得して渡す。
                    var hoverKind = GetCurrentHoverTargetKind();

                    // kind == None のモードは EnterHoverChanged が NotifyPointerHover を
                    // 呼ばないため、ツールの UpdateHover (= ギズモ軸ホバー / ブラシ円) が
                    // 一度も走らない。スクリーン座標だけで決まる表示を持つモードだけ、
                    // ここで先に更新する。
                    // EnterHoverChanged 末尾の OnRefreshGizmoOverlay が更新後の軸を拾う。
                    if (hoverKind == HoverTargetKind.None) UpdateScreenOnlyHover(vp, localPos);

                    _viewportManager.EnterHoverChanged(vp, localPos, hoverKind);
                };

                // ポインタがビューポートから出たらブラシ円を消す。
                // 出たあとは PointerMove が来ないので、円が置き去りになる。
                panel.OnPointerLeft += () => panel.HideBrushCircle();
            }

            ConnectPanelHover(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport);
            ConnectPanelHover(_layoutRoot?.TopPanel,         _viewportManager.TopViewport);
            ConnectPanelHover(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport);
            ConnectPanelHover(_layoutRoot?.SidePanel,        _viewportManager.SideViewport);

            // BoneEditor サブパネル表示中に ObjectMoveToolHandler へマウスイベントを橋渡し。
            // 旧 BoneInputHandler の後継 (統合)。従来通り InteractionMode が
            // ObjectMove / PivotOffset のときは外す (それぞれ専用経路に任せる)。
            // ObjectMoveToolHandler のピック対象フィルタ (PickBones /
            // PickMeshesNoSkin / PickMeshesSkinned) と MoveWithChildren は
            // PlayerBoneEditorSubPanel のチェックボックスから操作する。
            void ConnectBoneEditorObjectMove(PlayerViewportPanel panel)
            {
                if (panel == null) return;
                panel.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_interactionMode == InteractionMode.ObjectMove || _interactionMode == InteractionMode.PivotOffset) return;
                    _objectMoveHandler?.OnLeftClick(PlayerHitResult.Miss, pos, mods);
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragBegin += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_interactionMode == InteractionMode.ObjectMove || _interactionMode == InteractionMode.PivotOffset) return;
                    _objectMoveHandler?.OnLeftDragBegin(PlayerHitResult.Miss, pos, mods);
                };
                panel.OnDrag += (btn, pos, delta, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_interactionMode == InteractionMode.ObjectMove || _interactionMode == InteractionMode.PivotOffset) return;
                    _objectMoveHandler?.OnLeftDrag(pos, delta, mods);
                    // ObjectMoveTool.ApplyWorldDelta → ctx.SyncBoneTransforms →
                    // ViewerCore 側配線で EnterVerticesMoved(Dragging) が発火するため
                    // ここでの UpdateTransform は不要。
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragEnd += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_interactionMode == InteractionMode.ObjectMove || _interactionMode == InteractionMode.PivotOffset) return;
                    _objectMoveHandler?.OnLeftDragEnd(pos, mods);
                    _boneEditorSubPanel?.Refresh();
                };
            }
            ConnectBoneEditorObjectMove(_layoutRoot?.PerspectivePanel);
            ConnectBoneEditorObjectMove(_layoutRoot?.TopPanel);
            ConnectBoneEditorObjectMove(_layoutRoot?.FrontPanel);
            ConnectBoneEditorObjectMove(_layoutRoot?.SidePanel);

            void ConnectIndicatorInput(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    TrySelectIndicatorAtScreenPos(pos, mods);
                };
            }
            ConnectIndicatorInput(_layoutRoot?.PerspectivePanel);
            ConnectIndicatorInput(_layoutRoot?.TopPanel);
            ConnectIndicatorInput(_layoutRoot?.FrontPanel);
            ConnectIndicatorInput(_layoutRoot?.SidePanel);

            // Escape によるツール操作キャンセル（現状 Knife の進行中切断を破棄）。
            void ConnectCancelKey(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnCancelKey += () =>
                {
                    if (_interactionMode == InteractionMode.Knife)
                    {
                        _knifeHandler?.Cancel();
                        UpdateAdvancedSelectOverlay();
                        _knifeSubPanel?.Refresh();
                    }
                    // 面追加（四角形）で3点配置済みなら三角形として確定する。
                    // 線分モードは描画を終了し、次の描画を始められる状態へ戻す。
                    // FinishAsTriangle が条件を満たさなかったときだけ線分側を見る
                    // （両者はモードが違うので同時には成立しない）。
                    else if (_interactionMode == InteractionMode.AddFace)
                    {
                        if (_addFaceHandler != null &&
                            (_addFaceHandler.FinishAsTriangle() || _addFaceHandler.FinishLineChain()))
                            _addFaceSubPanel?.Refresh();
                    }
                    // 格子変形は進行中のセッションを取消して開始前へ戻す。
                    else if (_interactionMode == InteractionMode.Lattice)
                    {
                        _latticeHandler?.Cancel();
                        UpdateTopologyToolsOverlay();
                    }
                    // 辺群ブリッジは拾った辺を全て捨てる。
                    else if (_interactionMode == InteractionMode.EdgeBridge)
                    {
                        _edgeBridgeHandler?.Cancel();
                        UpdateTopologyToolsOverlay();
                        _edgeBridgeSubPanel?.Refresh();
                    }
                    // 点指定図形は置いた点を全て捨てる（パネルの［点をクリア］と同じ）。
                    // 表示の更新はハンドラの OnPointsChanged が行う。
                    else if (_interactionMode == InteractionMode.PointDefinedPrimitive)
                    {
                        _pointDefinedHandler?.ClearPoints();
                    }
                };
            }
            ConnectCancelKey(_layoutRoot?.PerspectivePanel);
            ConnectCancelKey(_layoutRoot?.TopPanel);
            ConnectCancelKey(_layoutRoot?.FrontPanel);
            ConnectCancelKey(_layoutRoot?.SidePanel);

            // Backspace / Delete による「直前に指定した点」の取り消し（面追加・点指定図形）。
            void ConnectUndoPointKey(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnUndoPointKey += () =>
                {
                    if (_interactionMode == InteractionMode.AddFace)
                    {
                        ExecuteAddFaceRemoveLastPoint();
                        return;
                    }
                    // 点指定図形はパネルの［1 点戻す］と同じ。
                    // 表示の更新はハンドラの OnPointsChanged が行う。
                    if (_interactionMode == InteractionMode.PointDefinedPrimitive)
                        _pointDefinedHandler?.RemoveLastPoint();
                };
            }
            ConnectUndoPointKey(_layoutRoot?.PerspectivePanel);
            ConnectUndoPointKey(_layoutRoot?.TopPanel);
            ConnectUndoPointKey(_layoutRoot?.FrontPanel);
            ConnectUndoPointKey(_layoutRoot?.SidePanel);

            // 面追加（四角形）で3点配置済みのとき、右クリックで三角形として確定する。
            // 線分モードは右クリックで描画を終了する（Escape と同じ扱い）。
            // 右ドラッグはカメラ回転だが、OnClick はドラッグ閾値未満のときだけ発火するため競合しない。
            void ConnectAddFaceRightClick(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 1) return;
                    if (_interactionMode != InteractionMode.AddFace) return;
                    if (_addFaceHandler != null &&
                        (_addFaceHandler.FinishAsTriangle() || _addFaceHandler.FinishLineChain()))
                        _addFaceSubPanel?.Refresh();
                };
            }
            ConnectAddFaceRightClick(_layoutRoot?.PerspectivePanel);
            ConnectAddFaceRightClick(_layoutRoot?.TopPanel);
            ConnectAddFaceRightClick(_layoutRoot?.FrontPanel);
            ConnectAddFaceRightClick(_layoutRoot?.SidePanel);

            void ConnectCameraChanged(PlayerViewport vp)
            {
                if (vp == null) return;
                // Orbit の OnCameraChanged は DragEnd とスクロールの両方で発火するため、
                // DragBegin/DragEnd のペア状態を追跡して Committed と区別する。
                bool orbitDragging = false;
                if (vp.Orbit != null)
                {
                    vp.Orbit.OnCameraDragBegin = () =>
                    {
                        orbitDragging = true;
                        _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragBegin);
                    };
                    vp.Orbit.OnCameraDragging  = () =>
                        _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Dragging);
                    vp.Orbit.OnCameraChanged   = () =>
                    {
                        if (orbitDragging)
                        {
                            orbitDragging = false;
                            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragEnd);
                        }
                        else
                        {
                            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
                        }
                        // ビューポート操作でもカメラ調整パネルの数値を追従させる。
                        _cameraSubPanel?.Refresh();
                    };
                    // 軌道回転の中心。軌道ドラッグ開始時に 1 回だけ評価される。
                    // 選択変更イベントからは呼ばないので、選択しただけでは視点は動かない。
                    vp.Orbit.GetOrbitPivot     = ComputeOrbitPivot;
                }
                if (vp.Ortho != null)
                {
                    vp.Ortho.OnCameraDragBegin = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragBegin);
                    vp.Ortho.OnCameraDragging  = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Dragging);
                    vp.Ortho.OnCameraDragEnd   = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragEnd);
                    vp.Ortho.OnCameraChanged   = () =>
                    {
                        _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
                        // ビューポート操作でもカメラ調整パネルの数値を追従させる。
                        _cameraSubPanel?.Refresh();
                    };
                }
            }

            ConnectCameraChanged(_viewportManager.PerspectiveViewport);
            ConnectCameraChanged(_viewportManager.TopViewport);
            ConnectCameraChanged(_viewportManager.FrontViewport);
            ConnectCameraChanged(_viewportManager.SideViewport);

            // ── Perspective オルソ切替トグル ──────────────────────────
            if (_layoutRoot?.PerspOrthoToggle != null)
            {
                _layoutRoot.PerspOrthoToggle.RegisterValueChangedCallback(evt =>
                {
                    // カメラ調整パネルのトグルと同じ経路に集約する。
                    SetMainCameraOrthographic(evt.newValue);
                });
            }

            // ── Top/Front/Side フリップボタン ─────────────────────────
            // 反転処理そのものを Action として返し、カメラ調整パネルからも同じ経路を
            // 呼べるようにする（ラベル・下絵・再描画の扱いをボタンと一致させる）。
            System.Action<bool> WireFlip(Button btn, PlayerViewport vp, PlayerViewportPanel panel, Label lbl, string normal, string flipped)
            {
                if (vp?.Ortho == null) return null;
                void Apply(bool f)
                {
                    vp.Ortho.Flipped = f;
                    if (lbl != null) lbl.text = f ? flipped : normal;
                    // 反転後の方向に応じた下絵へ差し替え＋再描画。
                    ApplyUnderlayToViewport(vp, panel);
                    _cameraSubPanel?.Refresh();
                }
                if (btn != null) btn.clicked += () => Apply(!vp.Ortho.Flipped);
                return Apply;
            }
            _setTopFlip   = WireFlip(_layoutRoot?.TopFlipBtn,   _viewportManager.TopViewport,   _layoutRoot?.TopPanel,   _layoutRoot?.TopViewLabel,   "TOP",   "BOTTOM");
            _setFrontFlip = WireFlip(_layoutRoot?.FrontFlipBtn, _viewportManager.FrontViewport, _layoutRoot?.FrontPanel, _layoutRoot?.FrontViewLabel, "Front", "Back");
            _setSideFlip  = WireFlip(_layoutRoot?.SideFlipBtn,  _viewportManager.SideViewport,  _layoutRoot?.SidePanel,  _layoutRoot?.SideViewLabel,  "Right", "Left");

            // ── 斜め45°トグル（Front/Side を水平傾斜。共有値のため両トグルは同期） ──
            void WireTilt()
            {
                var front = _viewportManager.FrontViewport;
                var side  = _viewportManager.SideViewport;
                if (front?.Ortho == null && side?.Ortho == null) return;

                void Apply(bool on)
                {
                    float deg = on ? OrthoViewController.DefaultHorizontalTiltDeg : 0f;
                    var rig = Quaternion.Euler(0f, -deg, 0f);
                    // 共有状態なので片方へ設定すれば Top/Front/Side 全てへ反映される。
                    if (front?.Ortho != null) front.Ortho.RigRotation = rig;
                    else if (side?.Ortho != null) side.Ortho.RigRotation = rig;

                    // 2つのトグルを同期（通知なしで反対側を合わせる）。
                    _layoutRoot?.TiltToggleFront?.SetValueWithoutNotify(on);
                    _layoutRoot?.TiltToggleSide ?.SetValueWithoutNotify(on);

                    // 全ビュー再描画（Front を起点に連動 slot も更新される）。
                    var vp = front ?? side;
                    if (vp != null) _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
                }

                _layoutRoot?.TiltToggleFront?.RegisterValueChangedCallback(e => Apply(e.newValue));
                _layoutRoot?.TiltToggleSide ?.RegisterValueChangedCallback(e => Apply(e.newValue));
            }
            WireTilt();

            // ── 下絵オフセット移動（下絵パネル表示中の左ドラッグ） ─────
            void ConnectUnderlayDrag(PlayerViewport vp, PlayerViewportPanel panel)
            {
                if (vp == null || panel == null) return;
                panel.OnDrag += (btn, pos, delta, mods) =>
                {
                    if (!_underlayActive || btn != 0) return;
                    var dir = GetUnderlayDirection(vp);
                    var s   = _underlay.Get(dir);
                    if (s == null || !s.HasImage) return;

                    // delta は viewport座標(Y=0下)。TopLeft は UIToolkit(Y=0上) のためY反転。
                    s.TopLeft += new Vector2(delta.x, -delta.y);
                    panel.SetUnderlay(s.Texture, s.TopLeft, s.ScaleOrigin, s.Scale);
                    _underlaySubPanel?.RefreshFields(dir);
                };
            }
            ConnectUnderlayDrag(_viewportManager.PerspectiveViewport, _layoutRoot?.PerspectivePanel);
            ConnectUnderlayDrag(_viewportManager.TopViewport,        _layoutRoot?.TopPanel);
            ConnectUnderlayDrag(_viewportManager.FrontViewport,      _layoutRoot?.FrontPanel);
            ConnectUnderlayDrag(_viewportManager.SideViewport,       _layoutRoot?.SidePanel);
        }
    }
}
