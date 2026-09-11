// PolyLingPlayerViewerCore.Lifecycle.cs
// Player ビューアのコア：公開ライフサイクル API（初期化・毎フレーム・破棄）と性能ログ（CSV）。
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
        // 公開ライフサイクル API
        // ================================================================

        /// <summary>
        /// 初期化。MonoBehaviour の Awake + Start に相当する処理を行う。
        /// uiRoot には EditorWindow.rootVisualElement または UIDocument.rootVisualElement を渡す。
        /// sceneRoot には Camera 等を親付けする Transform（通常はプレイヤーの gameObject.transform）を渡す。
        /// </summary>
        public void Initialize(VisualElement uiRoot, Transform sceneRoot, RemoteConfig config)
        {
            // 統合ログを設置する（メインスレッド捕捉＋Unity ログ取り込み開始）。
            // 以降の Debug.Log/LogWarning/LogError はログパネルへ集約される。
            PlayerLog.Install();

            _sceneRoot          = sceneRoot;
            _remoteMode         = config.Mode;
            _clientHost         = config.ClientHost;
            _clientPort         = config.ClientPort;
            _clientAutoConnect  = config.ClientAutoConnect;
            _serverPort         = config.ServerPort;
            _serverAutoStart    = config.ServerAutoStart;

            // ── リモートモード初期化 ────────────────────────────────────
            switch (_remoteMode)
            {
                case RemoteMode.Client:
                    _client = new PolyLingPlayerClient();
                    _client.Initialize(_clientHost, _clientPort, _clientAutoConnect);
                    break;
                case RemoteMode.Server:
                    _client = null;
                    _playerServer = new PolyLingPlayerServer();
                    // Initialize は BuildLayout 後（_commandDispatcher 確定後）に呼ぶ
                    break;
                default: // None
                    _client = null;
                    break;
            }

            _renderer = new MeshSceneRenderer();
            _receiver = new RemoteProjectReceiver();
            _editOps  = new PlayerEditOps(_undoManager);

            // VertexEdit スタック Undo/Redo 後の復元ハンドラ
            // 頂点移動（PendingMeshMoveEntries）と選択変更（CurrentSelectionSnapshot）を消費する
            _editOps.UndoController.OnUndoRedoPerformed += () =>
            {
                var stackType = _editOps.UndoController.LastUndoRedoStackType;
                UnityEngine.Debug.Log(
                    $"[UndoDbg] OnUndoRedoPerformed stack={stackType} " +
                    $"ActiveProject.Current={ActiveProject?.CurrentModel?.Name ?? "<null>"} " +
                    $"VertexEdit.Undo={_editOps.UndoController.VertexEditStack.UndoCount}/" +
                    $"Redo={_editOps.UndoController.VertexEditStack.RedoCount} " +
                    $"MeshList.Undo={_editOps.UndoController.MeshListStack.UndoCount}/" +
                    $"Redo={_editOps.UndoController.MeshListStack.RedoCount}");

                // ── MeshList（BoneTransform変更・PivotMove等）の復元
                if (stackType == MeshUndoController.UndoStackType.MeshList)
                {
                    var listCtx   = _editOps.UndoController.MeshListContext;
                    var model     = listCtx ?? ActiveProject?.CurrentModel;
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   MeshList branch: listCtx={listCtx?.Name ?? "<null>"}, " +
                        $"effectiveModel={model?.Name ?? "<null>"}");
                    if (model != null)
                    {
                        model.ComputeWorldMatrices();

                        var lastRecord = _editOps.UndoController.MeshListStack.LastExecutedRecord;
                        // MeshReorderChangeRecord は MeshContextList を丸ごと並べ替える。
                        // Player では ModelContext.OnListChanged / OnReorderCompleted に
                        // 購読者が居ないため、ここで再構築しないとシーンもリストも
                        // 古い並びのまま残り、描画がインデックス不整合になる。
                        bool needsRebuild = lastRecord is MeshListChangeRecord
                                         || lastRecord is MeshAttributesBatchChangeRecord
                                         || lastRecord is MultiMeshVertexSnapshotRecord
                                         || lastRecord is MultiMeshTopologySnapshotRecord
                                         || lastRecord is MeshReorderChangeRecord;
                        bool isReorder    = lastRecord is MeshReorderChangeRecord;
                        UnityEngine.Debug.Log(
                            $"[UndoDbg]   lastRecord={lastRecord?.GetType().Name ?? "<null>"}, " +
                            $"needsRebuild={needsRebuild}");
                        if (needsRebuild)
                        {
                            // Phase 2a-2b-2 Batch 3: Undo 適用による丸ごと再構築は EnterUndoApplied 経由。
                            // model は UndoController 由来で ActiveProject.CurrentModel と異なる可能性あり。
                            _viewportManager.EnterUndoApplied(ActiveProject, model);
                            RebuildModelList();
                        }
                        else if (lastRecord is PivotMoveRecord pivotRec)
                        {
                            // PivotMoveRecord は頂点位置も変更するため、
                            // GPU位置バッファを更新してからトランスフォームを適用する
                            var pivotMc = model.GetMeshContext(pivotRec.MasterIndex);
                            if (pivotMc != null)
                                _viewportManager.SyncMeshPositionsAndTransform(pivotMc, model);
                            // PivotMoveRecord ブランチのみ従来の UpdateSelectedDrawableMesh + UpdateTransform を維持。
                            _renderer?.UpdateSelectedDrawableMesh(0, model);
                            _viewportManager.UpdateTransform();
                        }
                        else if (lastRecord is MeshSelectionChangeRecord)
                        {
                            // 選択変更の Undo/Redo: Record.Undo / Redo で ModelContext の
                            // SelectedDrawableMeshIndices / SelectedBoneIndices / SelectedMorphIndices が
                            // RestoreSelectionFromIndices で既に復元済み。画面反映のみ行う。
                            var firstMc = model.ActiveMeshContext;
                            if (firstMc?.Selection != null)
                            {
                                _selectionOps?.SetSelectionState(firstMc.Selection);
                                _renderer?.SetSelectionState(firstMc.Selection);
                            }
                            _viewportManager.EnterTopologyChanged(ActiveProject);
                            RebuildModelList();
                        }

                        // 順序変更の Undo/Redo はツリーの作り直しが要る。
                        // Attributes では CreateTreeRoot が走らず、リスト表示が古いままになる。
                        NotifyPanels(isReorder ? ChangeKind.ListStructure : ChangeKind.Attributes);
                    }
                    return;
                }

                // ── Project (モデル切替等)
                // 問題 A/B 対応: ProjectStack の Record は ProjectContext.CurrentModelIndex を
                // 書き換え済み。ここで UndoController 内部の ModelContext 参照を新モデルに同期し、
                // シーン描画を再構築する。
                if (stackType == MeshUndoController.UndoStackType.Project)
                {
                    var projLast = _editOps.UndoController.ProjectStack.LastExecutedRecord;
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   Project branch: lastRecord={projLast?.GetType().Name ?? "<null>"}, " +
                        $"isRedo={_editOps.UndoController.LastUndoRedoIsRedo}, " +
                        $"CurrentModel={ActiveProject?.CurrentModel?.Name ?? "<null>"}");
                    if (ActiveProject != null)
                    {
                        _editOps.UndoController.SetProjectContext(ActiveProject);
                        _editOps.UndoController.SetModelContext(ActiveProject.CurrentModel);
                        _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
                        _viewportManager.EnterCameraChanged(
                            _viewportManager.PerspectiveViewport,
                            CameraChangePhase.Committed);
                        RebuildModelList();
                        NotifyPanels(ChangeKind.ModelSwitch);
                    }
                    return;
                }

                if (stackType != MeshUndoController.UndoStackType.VertexEdit)
                {
                    UnityEngine.Debug.Log($"[UndoDbg]   skip (stack={stackType} not handled)");
                    return;
                }
                var ctx = _editOps.UndoController.MeshUndoContext;
                if (ctx == null) { UnityEngine.Debug.Log("[UndoDbg]   ctx=null, bail"); return; }
                var targetModel = ctx.ParentModelContext;
                if (targetModel == null) { UnityEngine.Debug.Log("[UndoDbg]   targetModel=null, bail"); return; }
                UnityEngine.Debug.Log(
                    $"[UndoDbg]   VertexEdit branch: targetModel={targetModel.Name}, " +
                    $"sameAsCurrent={ReferenceEquals(targetModel, ActiveProject?.CurrentModel)}");

                // ── 頂点移動の復元
                var pending = ctx.PendingMeshMoveEntries;
                if (pending != null && pending.Length > 0)
                {
                    int totalV = 0; foreach (var e in pending) totalV += e.Indices?.Length ?? 0;
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   restore vertex move: entries={pending.Length}, totalVerts={totalV}");
                    foreach (var entry in pending)
                    {
                        var mc = targetModel.GetMeshContext(entry.MeshContextIndex);
                        if (mc?.MeshObject == null) continue;
                        var mo = mc.MeshObject;
                        for (int i = 0; i < entry.Indices.Length; i++)
                        {
                            int vi = entry.Indices[i];
                            if (vi >= 0 && vi < mo.VertexCount)
                                mo.Vertices[vi].Position = entry.NewPositions[i];
                        }
                        mo.InvalidatePositionCache();
                        _viewportManager.SyncMeshPositionsAndTransform(mc, targetModel);
                    }
                    ctx.PendingMeshMoveEntries = null;
                    // Phase 2a-2e 修正: Undo 経路は「ドラッグ終了」ではないため、
                    // EnterVerticesMoved(DragEnd) を呼ぶと ExitTransformDragging の
                    // dispatch state 遷移と PresentAll(ActiveProject) が実行される。
                    // 後者は ActiveProject.CurrentModel 基準で描画準備するため、
                    // targetModel != ActiveProject.CurrentModel のケースで頂点位置が反映されない。
                    // 元実装の軽量 API 呼出しに戻す。
                    _viewportManager.ExitTransformDragging();
                    _viewportManager.UpdateTransform();
                    _renderer?.UpdateSelectedDrawableMesh(0, targetModel);
                    NotifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 選択状態の復元（複数メッシュ）
                // MultiMeshSelectionChangeRecord は Record 内でメッシュ解決を行わず、
                // MeshContextIndex 付きのエントリ配列をここへ渡してくる。
                // 単一メッシュ用の CurrentSelectionSnapshot とは独立に処理する。
                var selEntries = ctx.PendingSelectionEntries;
                if (selEntries != null && selEntries.Length > 0)
                {
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   restore selection (multi): entries={selEntries.Length}");
                    foreach (var e in selEntries)
                    {
                        var mc = targetModel.GetMeshContext(e.MeshContextIndex);
                        if (mc?.Selection == null || e.New == null) continue;
                        mc.Selection.RestoreFromSnapshot(e.New);
                    }
                    ctx.PendingSelectionEntries = null;

                    // GPU 側の選択フラグは MeshContext ごとに読まれるため、
                    // 先頭メッシュを渡して全体を更新させる。
                    var multiFirstMc = targetModel.ActiveMeshContext;
                    if (multiFirstMc?.Selection != null)
                    {
                        _selectionOps?.SetSelectionState(multiFirstMc.Selection);
                        _renderer?.SetSelectionState(multiFirstMc.Selection);
                    }
                    NotifyPanels(ChangeKind.Selection);
                }

                // ── 選択状態の復元（単一メッシュ）
                // SelectionChangeRecord 経由。復元先は ActiveMeshContext 固定。
                // 記録側も ActiveMeshContext だけを変更する処理に限ること
                // （AdvancedSelectToolHandler / PlayerCommandDispatcher.PartsSetApply）。
                var snapshot = ctx.CurrentSelectionSnapshot;
                if (snapshot != null)
                {
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   restore selection: V={snapshot.Vertices?.Count ?? 0}, " +
                        $"E={snapshot.Edges?.Count ?? 0}");
                    var firstMc = targetModel.ActiveMeshContext;
                    if (firstMc?.Selection != null)
                    {
                        firstMc.Selection.RestoreFromSnapshot(snapshot);
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }
                    ctx.CurrentSelectionSnapshot = null;
                    NotifyPanels(ChangeKind.Selection);
                }

                // ── トポロジー／ボーンウェイト／UV／マテリアル変更の復元
                // MeshSnapshotRecord は ctx.MeshObject をクローンに差し替えるだけで
                // ModelContext 上の実 MeshContext には書き戻さないため、ここで同期する
                if (ctx.MeshObject != null)
                {
                    // 優先度順で対象 MasterIndex を決定
                    int topoMasterIdx = _skinWeightUndoMasterIndex >= 0
                        ? _skinWeightUndoMasterIndex
                        : _uvUndoMasterIndex;

                    // 明示的な MasterIndex がない場合は MeshObject 参照から逆引き
                    if (topoMasterIdx < 0)
                    {
                        for (int mi = 0; mi < targetModel.MeshContextCount; mi++)
                        {
                            var searchMc = targetModel.GetMeshContext(mi);
                            if (searchMc?.MeshObject != null &&
                                ReferenceEquals(searchMc.MeshObject, ctx.MeshObject))
                            { topoMasterIdx = mi; break; }
                        }
                        // 逆引きでも見つからない（既に差し替え後）→ 先頭Drawableにフォールバック
                        if (topoMasterIdx < 0)
                        {
                            var fb = targetModel.ActiveMeshContext;
                            if (fb != null) topoMasterIdx = targetModel.IndexOf(fb);
                        }
                    }

                    if (topoMasterIdx >= 0)
                    {
                        var liveMc = targetModel.GetMeshContext(topoMasterIdx);
                        if (liveMc?.MeshObject != null)
                        {
                            if (!ReferenceEquals(liveMc.MeshObject, ctx.MeshObject))
                            {
                                // 委譲が機能しなかった場合（ActiveCategory != Mesh）
                                // → 頂点数/面数が変わる場合は丸ごと置換、変わらない場合はコピー
                                if (ctx.MeshObject.VertexCount != liveMc.MeshObject.VertexCount ||
                                    ctx.MeshObject.FaceCount   != liveMc.MeshObject.FaceCount)
                                    liveMc.MeshObject = ctx.MeshObject.Clone();
                                else
                                    CopyMeshObjectVertexData(ctx.MeshObject, liveMc.MeshObject);
                            }
                            // 参照が同じ場合（委譲でデータ更新済み）もGPUを再構築する
                            // マテリアル/トポロジ Undo 復元後、テクスチャ表面(UnityMesh)を
                            // MaterialIndex 別サブメッシュで再構築する。EnterUndoApplied は編集用
                            // GPUアダプタのみ再構築するため、これが無いと Undo しても表面の材質が
                            // 戻らない（適用側 ApplyMaterialToFacesCommand と対称の処理）。
                            liveMc.ReplaceUnityMesh(liveMc.MeshObject.ToUnityMesh(targetModel.MaterialCount));
                            _editOps.UndoController.SyncMeshObjectReference(liveMc.MeshObject, liveMc.UnityMesh);
                            // ミラー側は実体側から導出される、という原則を Undo 後も保つ。
                            // スナップショットには実体側しか入っていないため、復元後に
                            // 法線とスロットを取り直す（位置側の RebakeDerivedMirrorVertices と対称）。
                            MirrorBranchOps.RebakeDerivedMirrorNormals(
                                targetModel.MeshContextList, targetModel.MaterialCount);
                            // Phase 2a-2b-2 Batch 3: Undo 適用の GPU 丸ごと再構築は EnterUndoApplied 経由。
                            _viewportManager.EnterUndoApplied(ActiveProject, targetModel);
                            NotifyPanels(ChangeKind.Attributes);
                        }
                    }
                }
            };

            _selectionState = new SelectionState();
            _renderer.SetSelectionState(_selectionState);

            _viewportManager.Initialize(_sceneRoot, _renderer);

            BuildLayout(uiRoot);

            SetupVertexInteraction();

            _commandDispatcher = new PlayerCommandDispatcher(
                () => ActiveProject,
                _renderer,
                _viewportManager,
                _selectionOps,
                NotifyPanels,
                RebuildModelList,
                _editOps?.UndoController,
                _editOps?.CommandQueue);

            // MCP（名前付きパイプ）からの実行入口。RemoteMode に依存しない。
            // 対の解除は Dispose 内。
            PolyLingCommandGateway.Dispatch = cmd => _commandDispatcher.Dispatch(cmd);

            // 生成系コマンドの受け口。実処理は Viewer 側にあるので委譲する。
            WireCreateCommandHandlers();

            // UI 自動操作（パネル・項目の登録と受け口）。サブパネルと
            // _commandDispatcher の両方が揃った後に作る。
            BuildUiAutomation();

            _fetchFlow = new PlayerRemoteFetchFlow(
                _client,
                _receiver,
                _localLoader,
                _viewportManager,
                _renderer,
                _selectionOps,
                NotifyPanels,
                s => _status = s);
            _fetchFlow.OnModelContextReady = model =>
            {
                if (_editOps?.UndoController?.MeshUndoContext != null)
                    _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                // 問題 A/B 対応: ProjectStack の Context も同期。
                if (ActiveProject != null)
                    _editOps?.UndoController?.SetProjectContext(ActiveProject);
            };

            // フェッチ受信中フラグの受け渡し。完了(false)時にモデルリストを1回だけ更新する。
            _fetchFlow.SetFetchActive = active =>
            {
                _suppressRebuildDuringFetch = active;
                if (!active) RebuildModelList();
            };

            // RemoteMode.Server: BuildLayout 後に Initialize（_commandDispatcher 確定後）
            if (_remoteMode == RemoteMode.Server && _playerServer != null)
            {
                _playerServer.Initialize(
                    _serverPort,
                    _serverAutoStart,
                    () =>
                    {
                        var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                        if (ctx != null)
                        {
                            ctx.Project = ActiveProject;
                            ctx.Model   = ActiveProject?.CurrentModel;
                            // リモート受信の位置適用後に GPU 反映・再描画するため配線する。
                            ctx.SyncMesh = () =>
                            {
                                var m = ActiveProject?.CurrentModel;
                                var smc = m?.ActiveMeshContext;
                                if (m != null && smc != null)
                                {
                                    _viewportManager.SyncMeshPositionsAndTransform(smc, m);
                                    _viewportManager.UpdateTransform();
                                }
                            };
                            ctx.Repaint = () => _activePanel?.MarkDirtyRepaint();
                        }
                        return ctx;
                    },
                    cmd => _commandDispatcher != null
                        ? _commandDispatcher.Dispatch(cmd)
                        : CommandResult.Fail("command dispatcher is not ready"));
            }

            // ── ローカルローダー配線 ────────────────────────────────────
            _localLoader.OnStatusChanged = s => _status = s;
            _localLoader.OnLoaded = project =>
            {
                // Phase 2a-2g-3: 冒頭の _renderer.ClearScene() を削除。
                // 行末の EnterSceneReset(clearScene: true) に統合。
                UnityEngine.Debug.Log("[LoadDbg] 01 handler-enter");
                var loadedModel = project.CurrentModel;

                if (_importSubPanel?.AutoScale == true)
                {
                    var list = loadedModel.MeshContextList;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].UnityMesh != null)
                        {
                            // Phase 2a-2d: ResetToMesh → EnterCameraChanged(Reset) に集約。
                            _viewportManager.EnterCameraChanged(
                                _viewportManager.PerspectiveViewport,
                                CameraChangePhase.Reset,
                                list[i].UnityMesh.bounds);
                            break;
                        }
                    }
                }
                UnityEngine.Debug.Log("[LoadDbg] 02 before-SetProject");
                _moveToolHandler?.SetProject(ActiveProject);
                _objectMoveHandler?.SetProject(ActiveProject);
                _pivotOffsetHandler?.SetProject(ActiveProject);
                _sculptHandler?.SetProject(ActiveProject);
                _advancedSelectHandler?.SetProject(ActiveProject);
                _skinWeightPaintHandler?.SetProject(ActiveProject);
                _alignVerticesHandler?.SetProject(ActiveProject);
                _pipeAlignHandler?.SetProject(ActiveProject);
                _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);
                _vertexHoleHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeRibbonFaceHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _solidifyHandler?.SetProject(ActiveProject);
                _deleteSelectionHandler?.SetProject(ActiveProject);
                _vertexDissolveHandler?.SetProject(ActiveProject);
                _tri4To1Handler?.SetProject(ActiveProject);
                _faceMergeHandler?.SetProject(ActiveProject);
                _quad4To1Handler?.SetProject(ActiveProject);
                _edgeBridgeHandler?.SetProject(ActiveProject);
                _holeRingCountHandler?.SetProject(ActiveProject);

                UnityEngine.Debug.Log("[LoadDbg] 03 before-UndoCtx");
                _editOps?.UndoController.SetModelContext(loadedModel);
                // 問題 A/B 対応: ProjectStack (モデル切替用 Undo) の Context も同期する。
                _editOps?.UndoController.SetProjectContext(project);

                UnityEngine.Debug.Log("[LoadDbg] 04 before-ComputeWorldMatrices");
                loadedModel.ComputeWorldMatrices();
                UnityEngine.Debug.Log("[LoadDbg] 05 after-ComputeWorldMatrices");

                // Phase 2a-2b-2 Batch 3: モデル初期選択処理を先に行ってから EnterSceneReset で一括更新。
                UnityEngine.Debug.Log("[LoadDbg] 06 before-Drawables");
                var loadedDrawables = loadedModel.DrawableMeshes;
                if (loadedDrawables != null)
                    foreach (var entry in loadedDrawables)
                    {
                        var mc = entry.Context;
                        if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0 && mc.IsVisible)
                        { loadedModel.SelectMesh(entry.MasterIndex); break; }
                    }

                UnityEngine.Debug.Log("[LoadDbg] 07 before-BoneScan");
                int lNeckIdx = -1, lFirstBone = -1;
                for (int ci = 0; ci < loadedModel.MeshContextCount; ci++)
                {
                    var bmc = loadedModel.GetMeshContext(ci);
                    if (bmc == null || bmc.Type != MeshType.Bone) continue;
                    if (lFirstBone < 0) lFirstBone = ci;
                    string n = bmc.Name ?? "";
                    if (n == "首" || n.ToLower() == "neck") { lNeckIdx = ci; break; }
                }
                int lSelBone = lNeckIdx >= 0 ? lNeckIdx : lFirstBone;
                if (lSelBone >= 0) loadedModel.SelectBone(lSelBone);

                if (_editOps?.UndoController?.MeshUndoContext != null)
                    _editOps.UndoController.MeshUndoContext.ParentModelContext = loadedModel;

                // RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を一括実行。
                // Phase 2a-2g-3: clearScene: true で冒頭の ClearScene 呼出しを統合。
                UnityEngine.Debug.Log("[LoadDbg] 08 before-EnterSceneReset");
                _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
                UnityEngine.Debug.Log("[LoadDbg] 09 before-EnterCameraChanged");
                _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                UnityEngine.Debug.Log("[LoadDbg] 10 after-EnterCameraChanged");

                // UNDO記録: PMX/MQO/CSV 読込によるモデル追加全体を 1 ステップ (ProjectStack) として記録。
                // (問題 E/I: 従来は MeshListStack に RecordMeshContextsAdd していたが、Undo で
                //  モデル内のメッシュが消えるだけで ProjectContext.Models にモデル自体 (空) が
                //  残り、モデルリストに名前だけ残るバグがあった。ModelOperationRecord.CreateAdd は
                //  ModelContextSnapshot にモデル全体を保存し、Undo でモデル自体を削除・
                //  Redo で復元するため、リスト表示も一致する)
                UnityEngine.Debug.Log("[LoadDbg] 11 before-RecordModelAdd");
                if (_editOps?.UndoController != null && loadedModel != null)
                {
                    int __addedIdx = _localLoader?.LastAddedModelIndex ?? project.CurrentModelIndex;
                    int __oldIdx   = _localLoader?.LastPreviousCurrentModelIndex ?? -1;
                    _editOps.UndoController.SetProjectContext(project);
                    _editOps.UndoController.RecordModelAdd(__addedIdx, loadedModel, __oldIdx);
                }

                UnityEngine.Debug.Log("[LoadDbg] 12 before-RebuildModelList");
                RebuildModelList();
                UnityEngine.Debug.Log("[LoadDbg] 13 before-RefreshBoneList");
                _skinWeightPaintPanel?.RefreshBoneList(loadedModel);
                UnityEngine.Debug.Log("[LoadDbg] 14 before-NotifyPanels");
                NotifyPanels(ChangeKind.ModelSwitch);
                _loadDbgSubmitLeft = 12;
                UnityEngine.Debug.Log("[LoadDbg] 15 handler-exit");
            };

            _receiver.OnProjectHeaderReceived += OnProjectHeaderReceived;
            _receiver.OnModelMetaReceived     += OnModelMetaReceived;
            _receiver.OnMeshSummaryReceived   += OnMeshSummaryReceived;
            _receiver.OnMeshDataReceived      += OnMeshDataReceived;

            if (_client != null)
            {
                _client.OnConnected    += OnConnected;
                _client.OnDisconnected += OnDisconnected;
                _client.OnPushReceived += OnPushReceived;
                _client.OnBinaryPushReceived = ApplyRemotePositions;
            }

            // リモートモード（インスペクタ設定）に応じた左ペイン表示の出し分け。
            // _remoteMode はセッション中不変のため、ここで一度だけ適用する。
            ApplyRemoteModeVisibility();

            // 生成系ツール（線押し出し・MediaPipe 顔変形など）が使う
            // ToolContext.AddMeshContext / AddMeshObjectToCurrentMesh を結線する。
            _viewportManager.SetMeshContextSinks(
                AddMeshContextFromTool, AddMeshObjectToCurrentMeshFromTool);

            SetupPerfLog();
        }

        // ================================================================
        // 性能ログ（CSV）
        // ================================================================

        /// <summary>性能ログ記録トグルの保存キー。</summary>
        private const string PerfLogPrefKey = "PerfLog.Enabled";

        /// <summary>
        /// 性能ログの結線。データ取得口を差し込み、左ペインのトグルへ開始／停止を結ぶ。
        /// Initialize の末尾（_editOps / _layoutRoot 確定後）で 1 回だけ呼ぶ。
        /// </summary>
        private void SetupPerfLog()
        {
            PLPerfLog.GetProject        = () => ActiveProject;
            PLPerfLog.GetUndoController = () => _editOps?.UndoController;
            PLPerfLog.GetLogLineCount   = () => PlayerLog.Count;
            PLPerfLog.GetLogTotalAdded  = () => PlayerLog.TotalAdded;

            var toggle = _layoutRoot?.PerfLogToggle;
            if (toggle == null) return;

            toggle.RegisterValueChangedCallback(e =>
            {
                PlayerUiPrefs.SetBool(PerfLogPrefKey, e.newValue);
                ApplyPerfLogEnabled(e.newValue);
            });

            bool on = PlayerUiPrefs.GetBool(PerfLogPrefKey, false);
            toggle.SetValueWithoutNotify(on);
            if (on) ApplyPerfLogEnabled(true);
        }

        /// <summary>性能ログの開始／停止。開始時は出力先をログパネルへ通知する。</summary>
        private void ApplyPerfLogEnabled(bool on)
        {
            if (on)
            {
                PLPerfLog.Start(_uiRoot);
                // 現在のツール状態を初回サンプルへ反映する。
                ReportPerfToolState();
                if (PLPerfLog.IsRunning)
                    PlayerLog.Add("Perf", "性能ログの記録を開始しました: " + PLPerfLog.CurrentPath);
            }
            else
            {
                string path = PLPerfLog.CurrentPath;
                PLPerfLog.Stop();
                if (!string.IsNullOrEmpty(path))
                    PlayerLog.Add("Perf", "性能ログの記録を停止しました: " + path);
            }
        }

        /// <summary>
        /// 現在のツールとサブツールを性能ログへ通知する。
        /// 値が変わったときだけ 1 行出る（PLPerfLog 側で判定）。
        /// </summary>
        private void ReportPerfToolState()
        {
            string sub;
            if (_subToolActive)
                sub = (_moveToolHandler != null &&
                       _moveToolHandler.DragSelectMode == MoveToolHandler.SelectionDragMode.Lasso)
                    ? "Lasso" : "Rect";
            else if (_deleteFaceModeActive)
                sub = "DeleteFace";
            else
                sub = "-";

            PLPerfLog.SetToolState(_interactionMode.ToString(), sub);
        }

        // Phase 2a-2f: 旧 Tick / LateTick / _Tick / _LateTick / PresentAll を削除。
        // これらは全て「毎フレームポーリング禁止」規約に違反する旧 API で、
        // MonoBehaviour.Update / LateUpdate から呼ばれていたが、Phase 2a-2f で
        // 呼出し元を削除したため dead code となり、完全除去した。
        // 代替:
        //   - 計算処理: 各イベント駆動ハンドラ (Enter* 正規入口) に分散
        //   - 描画提出: SubmitDrawForCamera (OnBeginCameraRendering 経由でカメラ毎に呼ばれる)

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// OnRenderObject 経路から呼ばれる。計算処理は一切禁止。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        /// <summary>[LoadDbg] 読込直後の描画到達を数回だけ記録するための残回数。恒久コードではない。</summary>
        private int _loadDbgSubmitLeft = 0;

        public void SubmitDrawForCamera(Camera cam)
        {
            if (_loadDbgSubmitLeft > 0)
            {
                _loadDbgSubmitLeft--;
                UnityEngine.Debug.Log("[LoadDbg] 18 submit-enter");
            }
            _viewportManager?.SubmitForCamera(cam, ActiveProject);
        }

        /// <summary>破棄。OnDestroy 相当。</summary>
        public void Dispose()
        {
            if (_activePanel != null)
                _vertexInteractor?.Disconnect(_activePanel);

            _viewportManager.Dispose();

            _primitiveSubPanel?.Dispose();
            _primitiveSubPanel = null;

            _livePrimitiveSubPanel?.Dispose();
            _livePrimitiveSubPanel = null;

            if (_client != null)
            {
                _client.OnConnected    -= OnConnected;
                _client.OnDisconnected -= OnDisconnected;
                _client.OnPushReceived -= OnPushReceived;
                _client.OnBinaryPushReceived = null;
                _client.Dispose();
            }
            _playerServer?.Dispose();

            if (_receiver != null)
            {
                _receiver.OnProjectHeaderReceived -= OnProjectHeaderReceived;
                _receiver.OnModelMetaReceived     -= OnModelMetaReceived;
                _receiver.OnMeshSummaryReceived   -= OnMeshSummaryReceived;
                _receiver.OnMeshDataReceived      -= OnMeshDataReceived;
            }

            _editOps?.Dispose();
            _editOps = null;
            _renderer?.Dispose();
            _renderer = null;

            PLPerfLog.Stop();
            PLPerfLog.GetProject        = null;
            PLPerfLog.GetUndoController = null;
            PLPerfLog.GetLogLineCount   = null;
            PLPerfLog.GetLogTotalAdded  = null;

            _logSubPanel?.Dispose();
            _logSubPanel = null;
            PlayerLog.Uninstall();

            // 強調枠を root から外し、ScrollView・対象へ登録した通知を外す。
            _uiAutomation?.Dispose();
            _uiAutomation = null;

            // MCP からの実行入口を外す。掴んだままだと破棄済みのディスパッチャを触る。
            PolyLingCommandGateway.Dispatch = null;
        }
    }
}
