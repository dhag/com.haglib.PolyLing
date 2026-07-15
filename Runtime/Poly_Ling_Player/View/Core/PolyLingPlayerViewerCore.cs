// PolyLingPlayerViewerCore.cs
// PolyLingPlayerViewer のロジック本体（MonoBehaviour 非依存プレーンクラス）
//
// PolyLingPlayerViewer（MonoBehaviour ラッパー）と
// PolyLingPlayerEditorWindow（EditorWindow ラッパー）の両方から使う。
//
// Runtime/Poly_Ling_Player/View/ に配置

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

namespace Poly_Ling.Player
{
    /// <summary>
    /// PolyLingPlayer のロジック本体。MonoBehaviour に依存しないプレーンクラス。
    /// 外部から Initialize / Tick / LateTick / Dispose を呼んでライフサイクルを制御する。
    /// </summary>
    public class PolyLingPlayerViewerCore
    {
        // ================================================================
        // 公開型
        // ================================================================

        /// <summary>リモート起動モード。</summary>
        public enum RemoteMode { None, Client, Server }

        /// <summary>Initialize に渡すリモート設定。</summary>
        public struct RemoteConfig
        {
            public RemoteMode Mode;
            public string     ClientHost;
            public int        ClientPort;
            public bool       ClientAutoConnect;
            public int        ServerPort;
            public bool       ServerAutoStart;

            /// <summary>デフォルト値（None モード）を返す。</summary>
            public static RemoteConfig Default => new RemoteConfig
            {
                Mode             = RemoteMode.None,
                ClientHost       = "127.0.0.1",
                ClientPort       = 8765,
                ClientAutoConnect = true,
                ServerPort       = 8765,
                ServerAutoStart  = true,
            };
        }

        // ================================================================
        // リモート設定（Initialize で設定）
        // ================================================================

        private RemoteMode _remoteMode;

        // 頂点編集のリモート連動フラグ（方向別・既定オフ）。比較検証用に実行時トグル可能。
        public bool SyncServerToClient = true; // サーバでの編集をクライアントへ配信
        public bool SyncClientToServer = true; // クライアントでの編集をサーバへ送信
        private string     _clientHost;
        private int        _clientPort;
        private bool       _clientAutoConnect;
        private int        _serverPort;
        private bool       _serverAutoStart;
        private Transform  _sceneRoot;

        // ================================================================
        // サブシステム
        // ================================================================

        private PolyLingPlayerClient           _client;
        private PolyLingPlayerServer           _playerServer;
        private RemoteProjectReceiver          _receiver;
        private MeshSceneRenderer              _renderer;
        private readonly PlayerLocalLoader     _localLoader    = new PlayerLocalLoader();
        private readonly UndoManager           _undoManager    = UndoManager.CreateNew();
        private          PlayerEditOps         _editOps;
        private VisualElement                  _uiRoot;

        private readonly PlayerViewportManager _viewportManager = new PlayerViewportManager();
        private PlayerLayoutRoot               _layoutRoot;
        private PlayerImportSubPanel           _importSubPanel;
        private PlayerExportSubPanel           _exportSubPanel;
        private PlayerProjectFileSubPanel      _projectFileSubPanel;
        private PlayerPartialImportSubPanel    _partialImportSubPanel;
        private PlayerPartialExportSubPanel    _partialExportSubPanel;
        private PlayerPrimitiveMeshSubPanel    _primitiveSubPanel;
        private MeshFilterToSkinnedSubPanel    _mfToSkinnedSubPanel;
        private PanelContext                   _panelContext;
        private ModelListSubPanel              _modelListSubPanel;
        private MeshListSubPanel               _meshListSubPanel;

        // 頂点インタラクション（Perspective ビューポート専用）
        private SelectionState         _selectionState;
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private enum InteractionMode { None, VertexMove, ObjectMove, PivotOffset, Sculpt, AdvancedSelect, SkinWeightPaint, AddFace, EdgeBevel, EdgeExtrude, FaceExtrude, EdgeTopology, Knife, FlipFace, LineExtrude, Rotate, Scale, SelectOnly }
        private InteractionMode               _interactionMode = InteractionMode.VertexMove;

        private struct OverlayIndicator
        {
            public int     MeshContextIndex;
            public Vector2 ScreenPos;
            public bool    IsBone;
        }
        private readonly System.Collections.Generic.List<OverlayIndicator> _overlayIndicators =
            new System.Collections.Generic.List<OverlayIndicator>();
        private const float OverlayHitRadius = 8f;

        private MoveToolHandler              _moveToolHandler;
        private ObjectMoveToolHandler        _objectMoveHandler;
        private PivotOffsetToolHandler       _pivotOffsetHandler;
        private SculptToolHandler            _sculptHandler;
        private AdvancedSelectToolHandler    _advancedSelectHandler;
        // 接続モードのクリック点フラッシュ強調（頂点インデックス。-1=非表示）。
        private int                          _advSelFlashVertex = -1;
        private int                          _advSelFlashGen    = 0;
        // 辺クリックのフラッシュ強調（辺。null=非表示。頂点フラッシュより優先）。
        private Poly_Ling.Selection.VertexPair? _advSelFlashEdge;
        private SkinWeightPaintToolHandler   _skinWeightPaintHandler;
        private PlayerSkinWeightPaintPanel   _skinWeightPaintPanel;
        private int                          _skinWeightUndoMasterIndex = -1;
        private int                          _uvUndoMasterIndex         = -1;
        private PlayerBlendSubPanel          _blendSubPanel;
        private PlayerModelBlendSubPanel     _modelBlendSubPanel;
        private PlayerBoneEditorSubPanel     _boneEditorSubPanel;
        private PlayerUVEditorSubPanel       _uvEditorSubPanel;
        private PlayerUVUnwrapSubPanel       _uvUnwrapSubPanel;
        private PlayerMaterialListSubPanel   _materialListSubPanel;
        private PlayerUVZSubPanel            _uvzSubPanel;

        // UV編集モード（A方式：UVZ平面メッシュに展開し既存ツールで編集→書き戻し）。
        // 抑止なし・記録済みコマンド再利用のため、生成/書き戻し/破棄は各々Undo記録される。
        private bool             _uvEditModeActive;
        private int              _uvEditUvzMaster   = -1;   // 展開UVZメッシュの master index（末尾追加）
        private int              _uvEditSrcMaster   = -1;   // 書き戻し先（元メッシュ）の master index
        private float            _uvEditUvScale     = 10f;  // 生成と書き戻しで同一を使うこと
        private PlayerViewportPanel _uvEditPrevPanel;
        private PlayerViewport      _uvEditPrevViewport;
        private PlayerPartsSelectionSetSubPanel _partsSelSetSubPanel;
        private PlayerMeshSelectionSetSubPanel  _meshSelSetSubPanel;
        private PlayerMergeMeshesSubPanel    _mergeMeshesSubPanel;
        private PlayerMorphSubPanel          _morphSubPanel;
        private PlayerMorphCreateSubPanel    _morphCreateSubPanel;
        private PlayerTPoseSubPanel          _tposeSubPanel;
        private PlayerHumanoidMappingSubPanel _humanoidMappingSubPanel;
        private PlayerMirrorSubPanel         _mirrorSubPanel;
        private PlayerQuadDecimatorSubPanel  _quadDecimatorSubPanel;
        private PlayerAlignVerticesSubPanel       _alignVerticesSubPanel;
        private AlignVerticesToolHandler          _alignVerticesHandler;
        private PlayerPlanarizeAlongBonesSubPanel _planarizeAlongBonesSubPanel;
        private PlanarizeAlongBonesToolHandler    _planarizeAlongBonesHandler;
        private PlayerMergeVerticesSubPanel       _mergeVerticesSubPanel;
        private MergeVerticesToolHandler          _mergeVerticesHandler;
        private PlayerSplitVerticesSubPanel       _splitVerticesSubPanel;
        private SplitVerticesToolHandler          _splitVerticesHandler;
        private PlayerAddFaceSubPanel             _addFaceSubPanel;
        private AddFaceToolHandler                _addFaceHandler;
        private System.Action _applySelectMode;                 // 選択モード適用（トグル→モデル）。永続復元/モデル選択時に再利用。
        private const string SelectModePrefKey = "LeftPane.SelectMode";
        private PlayerFlipFaceSubPanel            _flipFaceSubPanel;
        private FlipFaceToolHandler               _flipFaceHandler;
        private PlayerRotateSubPanel              _rotateSubPanel;
        private RotateToolHandler                 _rotateHandler;
        private PlayerScaleSubPanel               _scaleSubPanel;
        private ScaleToolHandler                  _scaleHandler;
        private PlayerEdgeBevelSubPanel           _edgeBevelSubPanel;
        private EdgeBevelToolHandler              _edgeBevelHandler;
        private PlayerEdgeExtrudeSubPanel         _edgeExtrudeSubPanel;
        private EdgeExtrudeToolHandler            _edgeExtrudeHandler;
        private PlayerFaceExtrudeSubPanel         _faceExtrudeSubPanel;
        private FaceExtrudeToolHandler            _faceExtrudeHandler;
        private PlayerEdgeTopologySubPanel        _edgeTopologySubPanel;
        private EdgeTopologyToolHandler           _edgeTopologyHandler;
        private PlayerKnifeSubPanel               _knifeSubPanel;
        private KnifeToolHandler                  _knifeHandler;
        private PlayerLineExtrudeSubPanel         _lineExtrudeSubPanel;
        private LineExtrudeToolHandler            _lineExtrudeHandler;
        private PlayerMediaPipeFaceDeformSubPanel _mediaPipeSubPanel;
        private PlayerVMDTestSubPanel        _vmdTestSubPanel;
        private PlayerUnityClipTestSubPanel  _unityClipTestSubPanel;

        // 下絵（3D背面に敷く参照画像）
        private readonly UnderlayConfig      _underlay = new UnderlayConfig();
        private PlayerUnderlaySubPanel       _underlaySubPanel;
        private bool                         _underlayActive;  // 下絵パネル表示中＝左ドラッグでオフセット移動
        private PlayerRemoteServerSubPanel   _remoteServerSubPanel;
        private PlayerVertexMoveSubPanel     _vertexMoveSubPanel;
        private PlayerPivotSubPanel          _pivotSubPanel;
        private PlayerSculptSubPanel         _sculptSubPanel;
        private PlayerAdvancedSelectSubPanel _advancedSelectSubPanel;

        private PlayerViewportPanel    _activePanel;
        private Vector2                _lastMouseScreenPos;
        private PlayerViewport         _activeViewport;

        private PlayerCommandDispatcher _commandDispatcher;

        private readonly List<(VisualElement section, Action refresh)> _sectionRefreshPairs = new();
        private PlayerRemoteFetchFlow   _fetchFlow;

        // フェッチ受信中はメッシュ1件ごとのフル GPU 再構築を抑止する。
        // 完了時の EnterSceneReset で1回だけ再構築する。
        private bool _suppressRebuildDuringFetch;

        private string _status = "未接続";

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
                        bool needsRebuild = lastRecord is MeshListChangeRecord
                                         || lastRecord is MeshAttributesBatchChangeRecord
                                         || lastRecord is MultiMeshVertexSnapshotRecord;
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
                            var firstMc = model.FirstDrawableMeshContext;
                            if (firstMc?.Selection != null)
                            {
                                _selectionOps?.SetSelectionState(firstMc.Selection);
                                _renderer?.SetSelectionState(firstMc.Selection);
                            }
                            _viewportManager.EnterTopologyChanged(ActiveProject);
                            RebuildModelList();
                        }

                        NotifyPanels(ChangeKind.Attributes);
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

                // ── 選択状態の復元
                var snapshot = ctx.CurrentSelectionSnapshot;
                if (snapshot != null)
                {
                    UnityEngine.Debug.Log(
                        $"[UndoDbg]   restore selection: V={snapshot.Vertices?.Count ?? 0}, " +
                        $"E={snapshot.Edges?.Count ?? 0}");
                    var firstMc = targetModel.FirstSelectedMeshContext
                                  ?? targetModel.FirstDrawableMeshContext;
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
                            var fb = targetModel.FirstDrawableMeshContext;
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
                            liveMc.UnityMesh = liveMc.MeshObject.ToUnityMesh(targetModel.MaterialCount);
                            _editOps.UndoController.SyncMeshObjectReference(liveMc.MeshObject, liveMc.UnityMesh);
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
                                var smc = m?.FirstDrawableMeshContext;
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
                    cmd => _commandDispatcher?.Dispatch(cmd));
            }

            // ── ローカルローダー配線 ────────────────────────────────────
            _localLoader.OnStatusChanged = s => _status = s;
            _localLoader.OnLoaded = project =>
            {
                // Phase 2a-2g-3: 冒頭の _renderer.ClearScene() を削除。
                // 行末の EnterSceneReset(clearScene: true) に統合。
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
                _moveToolHandler?.SetProject(ActiveProject);
                _objectMoveHandler?.SetProject(ActiveProject);
                _pivotOffsetHandler?.SetProject(ActiveProject);
                _sculptHandler?.SetProject(ActiveProject);
                _advancedSelectHandler?.SetProject(ActiveProject);
                _skinWeightPaintHandler?.SetProject(ActiveProject);
                _alignVerticesHandler?.SetProject(ActiveProject);
                _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);

                _editOps?.UndoController.SetModelContext(loadedModel);
                // 問題 A/B 対応: ProjectStack (モデル切替用 Undo) の Context も同期する。
                _editOps?.UndoController.SetProjectContext(project);

                loadedModel.ComputeWorldMatrices();

                // Phase 2a-2b-2 Batch 3: モデル初期選択処理を先に行ってから EnterSceneReset で一括更新。
                var loadedDrawables = loadedModel.DrawableMeshes;
                if (loadedDrawables != null)
                    foreach (var entry in loadedDrawables)
                    {
                        var mc = entry.Context;
                        if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0 && mc.IsVisible)
                        { loadedModel.SelectMesh(entry.MasterIndex); break; }
                    }

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
                _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
                _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);

                // UNDO記録: PMX/MQO/CSV 読込によるモデル追加全体を 1 ステップ (ProjectStack) として記録。
                // (問題 E/I: 従来は MeshListStack に RecordMeshContextsAdd していたが、Undo で
                //  モデル内のメッシュが消えるだけで ProjectContext.Models にモデル自体 (空) が
                //  残り、モデルリストに名前だけ残るバグがあった。ModelOperationRecord.CreateAdd は
                //  ModelContextSnapshot にモデル全体を保存し、Undo でモデル自体を削除・
                //  Redo で復元するため、リスト表示も一致する)
                if (_editOps?.UndoController != null && loadedModel != null)
                {
                    int __addedIdx = _localLoader?.LastAddedModelIndex ?? project.CurrentModelIndex;
                    int __oldIdx   = _localLoader?.LastPreviousCurrentModelIndex ?? -1;
                    _editOps.UndoController.SetProjectContext(project);
                    _editOps.UndoController.RecordModelAdd(__addedIdx, loadedModel, __oldIdx);
                }

                RebuildModelList();
                _skinWeightPaintPanel?.RefreshMeshList(loadedModel);
                _skinWeightPaintPanel?.RefreshBoneList(loadedModel);
                NotifyPanels(ChangeKind.ModelSwitch);
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
        public void SubmitDrawForCamera(Camera cam)
        {
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
        }

        // ================================================================
        // 頂点インタラクション セットアップ
        // ================================================================

        private void SetupVertexInteraction()
        {
            _selectionOps = new PlayerSelectionOps(_selectionState);

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
                // Phase 2b-1: 正規入口 EnterVerticesMoved(Dragging, syncMc) 経由に切替。
                // 軽量同期 (SyncMeshPositionsAndTransform + UpdateTransform) + overlay 更新を一元化。
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging, mc);
                },
                OnRepaint = () => _activePanel?.MarkDirtyRepaint(),

                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                GetToolContext  = () => _viewportManager.GetCurrentToolContext(_activeViewport),

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
                if (SyncClientToServer && _remoteMode == RemoteMode.Client && _client != null)
                {
                    _client.SendBinary(RemoteBinarySerializer.SerializePositionsOnly(mc.MeshObject));
                }
                else if (SyncServerToClient && _remoteMode == RemoteMode.Server && _playerServer != null)
                {
                    _playerServer.BroadcastPositions(mc.MeshObject);
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

            _objectMoveHandler = new ObjectMoveToolHandler();
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

            _advancedSelectHandler = new AdvancedSelectToolHandler();
            _advancedSelectHandler.SetProject(ActiveProject);
            _advancedSelectHandler.SetSelectionOps(_selectionOps);
            _advancedSelectHandler.SetUndoController(_editOps?.UndoController);
            _advancedSelectHandler.GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport);
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
            _skinWeightPaintHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintHandler.OnEnterTransformDragging = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _skinWeightPaintHandler.OnExitTransformDragging  = () => _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
            _skinWeightPaintHandler.OnSyncMeshPositions = mc =>
            {
                // boneWeights 変更はバッファ全体の再構築が必要
                var proj = ActiveProject;
                if (proj?.CurrentModel != null)
                {
                    // Phase 2a-2b-2: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(proj);
                }
            };
            _skinWeightPaintHandler.OnUpdateBrushCircle = (center, radius, color) =>
                _activePanel?.ShowBrushCircle(center, radius, color);
            _skinWeightPaintHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();

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
                            _vertexInteractor.Disconnect(_activePanel);
                        }
                        _activePanel    = panel;
                        _activeViewport = vp;
                        _vertexInteractor.Connect(_activePanel);
                    }
                    // Phase 2b-1: 正規入口 EnterHoverChanged 経由。
                    // 入口末尾で面ホバー/ギズモ overlay refresh が発火される。
                    // Phase 2b 以降で HoverTargetKind を現行ツールから取得して渡す。
                    _viewportManager.EnterHoverChanged(vp, localPos, GetCurrentHoverTargetKind());
                };
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
                    }
                };
            }
            ConnectCancelKey(_layoutRoot?.PerspectivePanel);
            ConnectCancelKey(_layoutRoot?.TopPanel);
            ConnectCancelKey(_layoutRoot?.FrontPanel);
            ConnectCancelKey(_layoutRoot?.SidePanel);

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
                    };
                }
                if (vp.Ortho != null)
                {
                    vp.Ortho.OnCameraDragBegin = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragBegin);
                    vp.Ortho.OnCameraDragging  = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Dragging);
                    vp.Ortho.OnCameraDragEnd   = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.DragEnd);
                    vp.Ortho.OnCameraChanged   = () => _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
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
                    var vp = _viewportManager.PerspectiveViewport;
                    if (vp?.Orbit == null) return;
                    vp.Orbit.Orthographic = evt.newValue;
                    // 方向（persp/ortho）に応じた下絵へ差し替え＋再描画。
                    ApplyUnderlayToViewport(vp, _layoutRoot?.PerspectivePanel);
                });
            }

            // ── Top/Front/Side フリップボタン ─────────────────────────
            void WireFlip(Button btn, PlayerViewport vp, PlayerViewportPanel panel, Label lbl, string normal, string flipped)
            {
                if (btn == null || vp?.Ortho == null) return;
                btn.clicked += () =>
                {
                    vp.Ortho.Flipped = !vp.Ortho.Flipped;
                    if (lbl != null) lbl.text = vp.Ortho.Flipped ? flipped : normal;
                    // 反転後の方向に応じた下絵へ差し替え＋再描画。
                    ApplyUnderlayToViewport(vp, panel);
                };
            }
            WireFlip(_layoutRoot?.TopFlipBtn,   _viewportManager.TopViewport,   _layoutRoot?.TopPanel,   _layoutRoot?.TopViewLabel,   "TOP",   "BOTTOM");
            WireFlip(_layoutRoot?.FrontFlipBtn, _viewportManager.FrontViewport, _layoutRoot?.FrontPanel, _layoutRoot?.FrontViewLabel, "Front", "Back");
            WireFlip(_layoutRoot?.SideFlipBtn,  _viewportManager.SideViewport,  _layoutRoot?.SidePanel,  _layoutRoot?.SideViewLabel,  "Right", "Left");

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

        // ================================================================
        // オーバーレイ更新
        //
        // ★★★ 【重大規約違反区画】 ★★★
        // 以下の Update*Overlay 関数群は旧 Tick() から毎フレーム呼ばれる想定の
        // 実装であり、「毎フレームポーリング禁止」規約に違反する。
        // Phase 2 で各関数を対応するイベントハンドラへ移植し、ここからは削除する予定。
        // 現在は呼び出し元が _Tick（dead code）のみ。
        //
        //   UpdateFaceHoverOverlay      → Phase 2: 面ホバー変更イベントへ
        //   UpdateSelectedFacesOverlay  → Phase 2: 面選択変更イベントへ
        //   UpdateGizmoOverlay          → Phase 2: 選択/ツール切替イベントへ
        //   UpdateAdvancedSelectOverlay → Phase 2: マウスドラッグイベントへ
        //   UpdateAddFaceOverlay        → Phase 2: AddFace handler hover/click イベントへ
        //   UpdateTopologyToolsOverlay  → Phase 2: topology tool handler hover イベントへ
        //   UpdateBoneOverlay           → Phase 2: ボーンポーズ/選択変更イベントへ
        //
        // 新規コードからこれら関数を呼ぶことは厳禁。
        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        // ================================================================

        private void UpdateFaceHoverOverlay()
        {
            if (_interactionMode == InteractionMode.ObjectMove   ||
                _interactionMode == InteractionMode.PivotOffset  ||
                _interactionMode == InteractionMode.SkinWeightPaint)
            {
                _activePanel?.HideFaceHover();
                return;
            }
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideFaceHover(); return; }
            var pts = _viewportManager.GetHoverFaceScreenPts(_activeViewport, model);
            if (pts == null) panel.HideFaceHover();
            else             panel.ShowFaceHover(pts);
        }

        private void UpdateSelectedFacesOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideSelectedFaces(); return; }
            var faces = _viewportManager.GetSelectedFacesScreenPts(_activeViewport, model);
            if (faces == null) panel.HideSelectedFaces();
            else               panel.ShowSelectedFaces(faces);
        }

        private void UpdateBoneOverlay()
        {
            var panel = _activePanel;
            _overlayIndicators.Clear();

            if (panel == null) return;

            bool boneEditorOpen = _layoutRoot?.BoneEditorSection?.style.display == DisplayStyle.Flex;
            bool objectMoveMode = _interactionMode == InteractionMode.ObjectMove;
            bool pivotMode      = _interactionMode == InteractionMode.PivotOffset;
            if (!boneEditorOpen && !objectMoveMode && !pivotMode)
            {
                panel.HideBoneWire();
                return;
            }

            var model = ActiveProject?.CurrentModel;
            if (model == null || model.MeshContextCount == 0)
            {
                panel.HideBoneWire();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideBoneWire(); return; }

            float panelH    = ctx.PreviewRect.height;
            var positions   = new System.Collections.Generic.List<Vector2>();
            var selected    = new System.Collections.Generic.List<bool>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                bool isBone       = mc.Type == MeshType.Bone;
                bool isNonSkinned = mc.Type == MeshType.Mesh
                                    && mc.MeshObject != null
                                    && !mc.MeshObject.HasBoneWeight;

                bool include = boneEditorOpen
                    ? isBone
                    : (isBone || isNonSkinned);

                if (!include) continue;

                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                sp.y = panelH - sp.y;

                bool isSel = model.SelectedMeshContextIndices.Contains(i);

                positions.Add(sp);
                selected.Add(isSel);

                _overlayIndicators.Add(new OverlayIndicator
                {
                    MeshContextIndex = i,
                    ScreenPos        = sp,
                    IsBone           = isBone,
                });
            }

            if (positions.Count == 0) { panel.HideBoneWire(); return; }

            panel.UpdateBoneWire(positions.ToArray(), selected.ToArray());
        }

        private int HitTestOverlayIndicator(Vector2 screenPos)
        {
            float minDist = OverlayHitRadius;
            int   result  = -1;
            foreach (var ind in _overlayIndicators)
            {
                float d = Vector2.Distance(screenPos, ind.ScreenPos);
                if (d < minDist) { minDist = d; result = ind.MeshContextIndex; }
            }
            return result;
        }

        private bool TrySelectIndicatorAtScreenPos(Vector2 screenPos, ModifierKeys mods)
        {
            if (_interactionMode != InteractionMode.ObjectMove && _interactionMode != InteractionMode.PivotOffset)
                return false;

            int idx = HitTestOverlayIndicator(screenPos);
            if (idx < 0) return false;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return false;

            if (mods.Shift || mods.Ctrl)
                model.ToggleMeshContextSelection(idx);
            else
                model.Select(idx);

            // Phase 2a-2e: UpdateSelectedDrawableMesh + NotifySelectionChanged を
            // EnterTopologyChanged に集約（選択変更扱い）。
            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.Selection);
            _boneEditorSubPanel?.Refresh();
            _activePanel?.MarkDirtyRepaint();
            return true;
        }

        private void UpdateAddFaceOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            if (_interactionMode != InteractionMode.AddFace || _addFaceHandler == null)
            {
                panel.HideAddFacePreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAddFacePreview(); return; }

            var data = _addFaceHandler.GetPreviewData();
            float h = ctx.PreviewRect.height;

            // AdvSel と完全に同じパターン:
            // ViewerCore側で h - sp.y を行い、Panel側で panelH - pt.y を行う。
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 確定済み点
            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (var p in data.PlacedPoints)
                pts.Add(toScreen(p.Position));

            // 線（配置済み点間）
            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            for (int i = 1; i < data.PlacedPoints.Length; i++)
                lines.Add((toScreen(data.PlacedPoints[i - 1].Position), toScreen(data.PlacedPoints[i].Position)));

            // 連続線分モード開始点からプレビューへの線
            if (data.ContinuousLineStart.HasValue && data.PreviewValid)
                lines.Add((toScreen(data.ContinuousLineStart.Value.Position), toScreen(data.PreviewPoint)));

            // 最後の確定済み点からプレビューへの線
            if (data.PlacedPoints.Length > 0 && data.PreviewValid)
                lines.Add((toScreen(data.PlacedPoints[data.PlacedPoints.Length - 1].Position), toScreen(data.PreviewPoint)));

            // プレビュー点
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            if (data.PreviewValid)
            {
                previewPts.Add(toScreen(data.PreviewPoint));
                previewSnap.Add(data.PreviewSnapped);
            }

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines);
        }

        /// <summary>
        /// EdgeTopology の Split モード用オーバーレイ更新。
        ///
        /// 【設計ポイント: AddFace Overlay API の流用】
        /// 描画は AddFace 専用に作られた PlayerViewportPanel.UpdateAddFacePreview を
        /// そのまま借りて行う。AddFace と EdgeTopology-Split は InteractionMode が
        /// 排他なので同時描画の干渉がない。同一の Painter2D overlay を 2 ツールで共用すると
        /// 「確定点 + 候補ハイライト + マウスまでの線分」という汎用 UI が使い回せる。
        ///
        /// AddFace overlay API へのマッピング:
        ///   - pts         : 第 1 頂点 (確定後のみ。AddFace では配置済み点)
        ///   - lines       : 第 1 頂点 → マウス位置 (確定後のみ)
        ///   - previewPts  : ホバー頂点 または マウス位置 (単一プレビュー点)
        ///   - previewSnap : スナップ表示 (シアン大 + リング) の切替フラグ。以下参照
        ///
        /// 【previewSnap の 2 段階ロジック】
        /// AddFace は「頂点にピッタリ合ったとき」だけ snap=true にする。
        /// Split はクリック前後で意味を切り替えた:
        ///   - 第 1 頂点未確定時 (firstValid=false):
        ///       頂点にホバーしていれば無条件 snap=true
        ///       → 「これから開始点になる候補」を常に強調
        ///   - 第 1 頂点確定後 (firstValid=true):
        ///       ホバー頂点が SplitOpponentCandidates に含まれるときだけ snap=true
        ///       → 「対角に取れる頂点 = 有効な第 2 クリック先」だけを強調
        ///
        /// 【mo の取得: ctx.FirstSelectedMeshObject は使えない】
        /// _viewportManager.GetCurrentToolContext() が返す ToolContext は Model を
        /// 設定しないため、ctx.FirstSelectedMeshObject は常に null を返す罠がある。
        /// AddFace overlay は Handler.GetPreviewData() 経由で世界座標を受け取るため
        /// この罠を踏まないが、Split overlay は頂点座標そのものが必要なので
        /// ActiveProject から直接取得する必要がある。同じ手口で他のオーバーレイを
        /// 作るときも、ctx を世界座標変換 (WorldToScreen / PreviewRect) 専用と
        /// 割り切り、データは ActiveProject / Handler から取ること。
        ///
        /// 【Y 座標変換】
        /// ctx.WorldToScreen は UIToolkit Y (Y=0 上) を返すが、AddFace Overlay API は
        /// overlay Y=0 下を期待する。toScreen で (sp.x, h - sp.y) 変換をかける。
        /// マウス位置 (LastHoverScreenPos) は UpdateHover が UIToolkit Y で受け取って
        /// キャッシュしているので、同様に Y 反転が必要。
        /// </summary>
        private void UpdateEdgeTopologySplitOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            if (_edgeTopologyHandler == null
                || _edgeTopologyHandler.ModePublic != Poly_Ling.Tools.EdgeTopoMode.Split)
            {
                panel.HideAddFacePreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAddFacePreview(); return; }

            // ActiveProject から直接取る (ctx.FirstSelectedMeshObject は上記注意点で null)
            var mo = ActiveProject?.CurrentModel?.FirstSelectedMeshContext?.MeshObject;
            if (mo == null) { panel.HideAddFacePreview(); return; }

            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            int firstV    = _edgeTopologyHandler.SplitFirstVertex;
            int hoverV    = _edgeTopologyHandler.SplitHoverVertex;
            var candidates = _edgeTopologyHandler.SplitOpponentCandidates;

            var pts         = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            var lines       = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();

            bool firstValid = firstV >= 0 && firstV < mo.VertexCount;
            bool hoverValid = hoverV >= 0 && hoverV < mo.VertexCount;

            // 確定点: 第 1 頂点
            if (firstValid)
                pts.Add(toScreen(mo.Vertices[firstV].Position));

            // プレビュー点: ホバー頂点があればその位置、なければマウス位置
            // マウス位置は UpdateHover が最後に受け取ったスクリーン座標
            // (UIToolkit Y=0 上) を IMGUI Y に変換して使う。
            UnityEngine.Vector2 previewPoint;
            bool previewSnapped;
            if (hoverValid)
            {
                previewPoint = toScreen(mo.Vertices[hoverV].Position);
                if (!firstValid)
                {
                    // 第 1 頂点未確定時: 頂点にホバーしているなら常にスナップ扱いにする。
                    // (「第 1 頂点に近づいたら大きめのまるで強調」という初期要件)
                    previewSnapped = true;
                }
                else
                {
                    // 第 1 頂点確定後: ホバー頂点が候補集合にあるときだけスナップ扱い
                    // (対向点候補をシアン大 + リングで強調)
                    previewSnapped = candidates != null && candidates.ContainsKey(hoverV);
                }
            }
            else
            {
                var lhp = _edgeTopologyHandler.LastHoverScreenPos;
                previewPoint = new UnityEngine.Vector2(lhp.x, h - lhp.y);
                previewSnapped = false;
            }
            previewPts.Add(previewPoint);
            previewSnap.Add(previewSnapped);

            // 線: 第 1 頂点 → プレビュー点 (確定後のみ)
            if (firstValid)
                lines.Add((toScreen(mo.Vertices[firstV].Position), previewPoint));

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines);
        }

        private void UpdateTopologyToolsOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            // Split モードは AddFace overlay API を流用して描画する (別経路)。
            // UpdateTopologyToolsOverlay が担う TopoToolOverlay (色付き線のみ) では
            // AddFace 相当の「確定点 + 候補ハイライト + マウス線」が描けないため、
            // AddFace 専用の Painter2D 経路 (UpdateAddFacePreview) を共用する。
            bool isEdgeTopo = (_interactionMode == InteractionMode.EdgeTopology && _edgeTopologyHandler != null);
            bool isSplit = isEdgeTopo && _edgeTopologyHandler.ModePublic == Poly_Ling.Tools.EdgeTopoMode.Split;
            if (isSplit)
            {
                UpdateEdgeTopologySplitOverlay();
                // TopoToolOverlay は空にして隠す (Flip/Dissolve 用の残留描画を防ぐ)
                panel.HideTopoToolOverlay();
                return;
            }
            // AddFace overlay は AddFace モード専用なので、Split 以外の EdgeTopology
            // モード (Flip/Dissolve) に入っているときは隠す。AddFace モード自体の
            // 管理は UpdateAddFaceOverlay 側に任せる。
            if (_interactionMode == InteractionMode.EdgeTopology
                && _edgeTopologyHandler != null
                && _edgeTopologyHandler.ModePublic != Poly_Ling.Tools.EdgeTopoMode.Split
                && _interactionMode != InteractionMode.AddFace)
            {
                panel.HideAddFacePreview();
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null)
            {
                panel.HideTopoToolOverlay();
                return;
            }

            var mo = ctx.FirstSelectedMeshObject;
            float h = ctx.PreviewRect.height;

            // WorldToScreen: AddFaceOverlay と同じ変換 (h - sp.y)
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2, UnityEngine.Color)>();

            // ── EdgeBevel ─────────────────────────────────────────────────
            if (_interactionMode == InteractionMode.EdgeBevel && _edgeBevelHandler != null && mo != null)
            {
                var edge = _edgeBevelHandler.HoverEdge;
                if (edge.HasValue)
                {
                    int v0 = edge.Value.V1, v1 = edge.Value.V2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   UnityEngine.Color.white));
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── EdgeExtrude ───────────────────────────────────────────────
            if (_interactionMode == InteractionMode.EdgeExtrude && _edgeExtrudeHandler != null && mo != null)
            {
                var edge = _edgeExtrudeHandler.HoverEdge;
                if (edge.HasValue)
                {
                    int v0 = edge.Value.V1, v1 = edge.Value.V2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   new UnityEngine.Color(0.2f, 0.8f, 1f)));
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── FaceExtrude ───────────────────────────────────────────────
            if (_interactionMode == InteractionMode.FaceExtrude && _faceExtrudeHandler != null && mo != null)
            {
                int fi = _faceExtrudeHandler.HoverFace;
                if (fi >= 0 && fi < mo.FaceCount)
                {
                    var face = mo.Faces[fi];
                    int n = face.VertexIndices.Count;
                    for (int i = 0; i < n; i++)
                    {
                        int va = face.VertexIndices[i];
                        int vb = face.VertexIndices[(i + 1) % n];
                        if (va >= 0 && va < mo.VertexCount && vb >= 0 && vb < mo.VertexCount)
                            lines.Add((toScreen(mo.Vertices[va].Position),
                                       toScreen(mo.Vertices[vb].Position),
                                       new UnityEngine.Color(1f, 1f, 1f, 0.7f)));
                    }
                }
                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            // ── EdgeTopology (Flip / Dissolve) ───────────────────────────
            // Split モードはメソッド冒頭で UpdateEdgeTopologySplitOverlay() に分岐済み。
            // ここに到達するのは Flip/Dissolve モードのみ。辺ホバーを黄色線で示す。
            if (_interactionMode == InteractionMode.EdgeTopology && _edgeTopologyHandler != null && mo != null)
            {
                if (_edgeTopologyHandler.HasHoverEdge)
                {
                    int v0 = _edgeTopologyHandler.HoverEdgeV1;
                    int v1 = _edgeTopologyHandler.HoverEdgeV2;
                    if (v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position),
                                   new UnityEngine.Color(1f, 0.8f, 0.2f)));
                }

                panel.UpdateTopoToolOverlay(lines);
                return;
            }

            panel.HideTopoToolOverlay();
        }

        private void UpdateAdvancedSelectOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            // ナイフ（ラダー切断）は同じプレビューチャネル（点＋線）を流用する。
            if (_interactionMode == InteractionMode.Knife)
            {
                UpdateKnifePreviewInto(panel);
                return;
            }

            if (_interactionMode != InteractionMode.AdvancedSelect || _advancedSelectHandler == null)
            {
                panel.HideAdvSelPreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAdvSelPreview(); return; }

            var previewCtx = _advancedSelectHandler.GetPreviewContext();
            if (previewCtx == null) { panel.HideAdvSelPreview(); return; }

            // ctx（ToToolContext 由来）は Model を持たないため FirstSelectedMeshObject が null。
            // 操作対象メッシュは実モデルから解決する（投影は ctx.WorldToScreen を使用）。
            var ovModel = ActiveProject?.CurrentModel;
            var mo = ovModel?.FirstSelectedMeshContext?.MeshObject
                  ?? ovModel?.FirstDrawableMeshContext?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            var pts = new System.Collections.Generic.List<Vector2>();
            var verts = previewCtx.PreviewVertices;
            if (verts != null)
                foreach (int vi in verts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }
            var path = previewCtx.PreviewPath;
            if (path != null)
                foreach (int vi in path)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }

            var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
            var edges = previewCtx.PreviewEdges;
            if (edges != null)
                foreach (var e in edges)
                {
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount || e.V2 < 0 || e.V2 >= mo.VertexCount) continue;
                    var s1 = ctx.WorldToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }
            if (path != null && path.Count > 1)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    int v1 = path[i], v2 = path[i + 1];
                    if (v1 < 0 || v1 >= mo.VertexCount || v2 < 0 || v2 >= mo.VertexCount) continue;
                    var s1 = ctx.WorldToScreen(mo.Vertices[v1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[v2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }

            // 強調マーカー：最短＝始点、その他＝クリック点／辺のフラッシュ
            Vector2? firstPt = null;
            (Vector2, Vector2)? firstEdge = null;
            int emphVertex = -1;
            if (_advancedSelectHandler.Mode == Poly_Ling.Tools.AdvancedSelectMode.ShortestPath)
                emphVertex = _advancedSelectHandler.GetShortestPathFirstVertex();
            else if (_advSelFlashEdge.HasValue)
            {
                var e = _advSelFlashEdge.Value;
                if (e.V1 >= 0 && e.V1 < mo.VertexCount && e.V2 >= 0 && e.V2 < mo.VertexCount)
                {
                    var s1 = ctx.WorldToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    firstEdge = (new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y));
                }
            }
            else if (_advSelFlashVertex >= 0)
                emphVertex = _advSelFlashVertex;

            if (emphVertex >= 0 && emphVertex < mo.VertexCount)
            {
                var fsp = ctx.WorldToScreen(mo.Vertices[emphVertex].Position);
                firstPt = new Vector2(fsp.x, ctx.PreviewRect.height - fsp.y);
            }

            panel.UpdateAdvSelPreview(pts, lines, _advancedSelectHandler.AddToSelection, firstPt, firstEdge);
        }

        /// <summary>
        /// ナイフ（ラダー切断）のプレビューを AdvSel プレビューチャネルへ流し込む。
        /// 確定済アンカー点・ラング中点・切断線を現在の視点で再投影する。
        /// </summary>
        private void UpdateKnifePreviewInto(PlayerViewportPanel panel)
        {
            if (_knifeHandler == null) { panel.HideAdvSelPreview(); return; }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAdvSelPreview(); return; }

            // ctx（ToToolContext 由来）は Model を持たないため、操作対象メッシュは実モデルから解決する。
            var kfModel = ActiveProject?.CurrentModel;
            var mo = kfModel?.FirstSelectedMeshContext?.MeshObject
                  ?? kfModel?.FirstDrawableMeshContext?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            var prev = _knifeHandler.GetPreview();
            if (prev == null) { panel.HideAdvSelPreview(); return; }

            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (int vi in prev.DotVertices)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                pts.Add(toScreen(mo.Vertices[vi].Position));
            }
            foreach (var w in prev.DotWorld)
                pts.Add(toScreen(w));

            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            foreach (var seg in prev.Lines)
                lines.Add((toScreen(seg.Item1), toScreen(seg.Item2)));

            // クリック点フラッシュ強調（AdvSel と共通：辺＝太線／頂点＝リング）
            Vector2? firstPt = null;
            (Vector2, Vector2)? firstEdge = null;
            if (_advSelFlashEdge.HasValue)
            {
                var e = _advSelFlashEdge.Value;
                if (e.V1 >= 0 && e.V1 < mo.VertexCount && e.V2 >= 0 && e.V2 < mo.VertexCount)
                    firstEdge = (toScreen(mo.Vertices[e.V1].Position), toScreen(mo.Vertices[e.V2].Position));
            }
            else if (_advSelFlashVertex >= 0 && _advSelFlashVertex < mo.VertexCount)
                firstPt = toScreen(mo.Vertices[_advSelFlashVertex].Position);

            panel.UpdateAdvSelPreview(pts, lines, prev.PlanValid, firstPt, firstEdge);
        }

        private void UpdateGizmoOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideGizmo(); return; }

            if (_interactionMode == InteractionMode.ObjectMove)
            {
                if (_objectMoveHandler == null) { panel.HideGizmo(); return; }
                if (_objectMoveHandler.TryGetGizmoScreenPositions(
                        ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
                {
                    _objectMoveHandler.GetPivotScreenPos(out var pivotScreen);
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo      = true,
                        IsDiamondStyle = false,
                        Origin        = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                        HoveredAxis   = hovAxis,
                        HasPivotGizmo = true, PivotOrigin = pivotScreen,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_interactionMode == InteractionMode.PivotOffset)
            {
                if (_pivotOffsetHandler == null) { panel.HideGizmo(); return; }
                if (_pivotOffsetHandler.TryGetGizmoScreenPositions(
                        ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
                {
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo       = true,
                        IsDiamondStyle = true,
                        Origin         = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                        HoveredAxis    = hovAxis,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_interactionMode == InteractionMode.Rotate)
            {
                if (_rotateHandler != null &&
                    _rotateHandler.TryGetGizmoRings(ctx, out var rx, out var ry, out var rz, out var rha))
                {
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo    = true,
                        IsRingStyle = true,
                        RingX = rx, RingY = ry, RingZ = rz,
                        HoveredAxis = rha,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_interactionMode == InteractionMode.Scale)
            {
                if (_scaleHandler != null &&
                    _scaleHandler.TryGetGizmoScreenPositions(
                        ctx, out var so, out var sxe, out var sye, out var sze, out var sha))
                {
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo    = true,
                        Origin      = so, XEnd = sxe, YEnd = sye, ZEnd = sze,
                        HoveredAxis = sha,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_interactionMode == InteractionMode.Sculpt || _interactionMode == InteractionMode.AdvancedSelect ||
                _interactionMode == InteractionMode.SkinWeightPaint)
            {
                panel.HideGizmo();
                return;
            }

            if (_moveToolHandler == null) { panel.HideGizmo(); return; }
            if (_moveToolHandler.TryGetGizmoScreenPositions(
                    ctx, out var o, out var xe, out var ye, out var ze, out var ha))
            {
                panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                    HoveredAxis = ha,
                });
            }
            else panel.HideGizmo();
        }

        // ================================================================
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _uiRoot = root;
            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            _panelContext = new PanelContext(DispatchPanelCommand);

            _modelListSubPanel = new ModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            _meshListSubPanel = new MeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);

            // ObjectMoveTRSPanel は BoneEditorSubPanel に統合済みのため生成不要

            _skinWeightPaintPanel = new PlayerSkinWeightPaintPanel();
            _skinWeightPaintPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintPanel.OnMeshSelectionChanged = () =>
            {
                if (_interactionMode == InteractionMode.SkinWeightPaint)
                    SyncSkinWeightUndoMesh();
                _activePanel?.MarkDirtyRepaint();
            };
            _skinWeightPaintPanel.GetToolContext =
                () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinWeightPaintPanel.Build(_layoutRoot.SkinWeightPaintSection);

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
            _blendSubPanel.OnRepaint          = () => _activePanel?.MarkDirtyRepaint();
            _blendSubPanel.GetUndoController  = () => _editOps?.UndoController;
            _blendSubPanel.GetCommandQueue    = () => _editOps?.CommandQueue;
            _blendSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _blendSubPanel.Build(_layoutRoot.BlendSection);

            _modelBlendSubPanel = new PlayerModelBlendSubPanel();
            _modelBlendSubPanel.SendCommand    = cmd => _commandDispatcher?.Dispatch(cmd);
            _modelBlendSubPanel.GetProjectView = () => ActiveProject != null
                ? new PlayerProjectView(ActiveProject) : null;
            _modelBlendSubPanel.Build(_layoutRoot.ModelBlendSection);

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

            _meshSelSetSubPanel = new PlayerMeshSelectionSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _meshSelSetSubPanel.Build(_layoutRoot.MeshSelectionSetSection);

            _mergeMeshesSubPanel = new PlayerMergeMeshesSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _mergeMeshesSubPanel.Build(_layoutRoot.MergeMeshesSection);

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
                GetH = () => _alignVerticesHandler,
            };
            _alignVerticesSubPanel.Build(_layoutRoot.AlignVerticesSection);

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
                GetH = () => _planarizeAlongBonesHandler,
            };
            _planarizeAlongBonesSubPanel.Build(_layoutRoot.PlanarizeAlongBonesSection);

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
                GetH = () => _mergeVerticesHandler,
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
                GetH = () => _splitVerticesHandler,
            };
            _splitVerticesSubPanel.Build(_layoutRoot.SplitVerticesSection);

            _addFaceHandler = new AddFaceToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetHoverElement     = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
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
                    _applySelectMode?.Invoke();  // 永続選択モードを新規アクティブモデルへ適用
                    var model = proj.CurrentModel;
                    if (model == null) return false;

                    // 描画可能メッシュが既にあればそのまま使う
                    if (model.FirstDrawableMeshContext != null) return true;

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
                    // 全トポロジハンドラを漏れなく伝播する (OnPrimitiveMeshCreated,
                    // OnMeshDataReceived 等の他経路も同じ列挙を持つ)。新ハンドラ追加時は
                    // 全伝播箇所に新しい `_xxxHandler?.SetProject(ActiveProject);` を追加すること。
                    _edgeBevelHandler?.SetProject(ActiveProject);
                    _edgeExtrudeHandler?.SetProject(ActiveProject);
                    _faceExtrudeHandler?.SetProject(ActiveProject);
                    _edgeTopologyHandler?.SetProject(ActiveProject);
                    _knifeHandler?.SetProject(ActiveProject);
                    RebuildModelList();
                    NotifyPanels(ChangeKind.ListStructure);
                    return true;
                },
            };
            _addFaceHandler.SetProject(ActiveProject);
            _addFaceHandler.SetUndoController(_editOps?.UndoController);
            _addFaceSubPanel = new PlayerAddFaceSubPanel
            {
                GetH = () => _addFaceHandler,
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
            _flipFaceSubPanel = new PlayerFlipFaceSubPanel { GetH = () => _flipFaceHandler };
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
            _rotateHandler.SetProject(ActiveProject);
            _rotateHandler.SetUndoController(_editOps?.UndoController);
            _rotateSubPanel = new PlayerRotateSubPanel { GetH = () => _rotateHandler };
            _rotateSubPanel.Build(_layoutRoot.RotateSection);
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
            _scaleHandler.SetProject(ActiveProject);
            _scaleHandler.SetUndoController(_editOps?.UndoController);
            _scaleSubPanel = new PlayerScaleSubPanel { GetH = () => _scaleHandler };
            _scaleSubPanel.Build(_layoutRoot.ScaleSection);
            _edgeBevelHandler = new EdgeBevelToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
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
            _edgeBevelSubPanel = new PlayerEdgeBevelSubPanel { GetH = () => _edgeBevelHandler };
            _edgeBevelSubPanel.Build(_layoutRoot.EdgeBevelSection);
            _edgeExtrudeHandler = new EdgeExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
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
            _edgeExtrudeSubPanel = new PlayerEdgeExtrudeSubPanel { GetH = () => _edgeExtrudeHandler };
            _edgeExtrudeSubPanel.Build(_layoutRoot.EdgeExtrudeSection);
            _faceExtrudeHandler = new FaceExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
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
            _knifeHandler.SetProject(ActiveProject);
            _knifeHandler.SetUndoController(_editOps?.UndoController);
            _knifeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _knifeSubPanel = new PlayerKnifeSubPanel { GetH = () => _knifeHandler };
            _knifeSubPanel.Build(_layoutRoot.KnifeSection);

            _lineExtrudeHandler = new LineExtrudeToolHandler
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
            _lineExtrudeHandler.SetProject(ActiveProject);
            _lineExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _lineExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _lineExtrudeSubPanel = new PlayerLineExtrudeSubPanel { GetH = () => _lineExtrudeHandler };
            _lineExtrudeSubPanel.Build(_layoutRoot.LineExtrudeSection);

            _mediaPipeSubPanel = new PlayerMediaPipeFaceDeformSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _mediaPipeSubPanel.Build(_layoutRoot.MediaPipeSection);

            _vmdTestSubPanel = new PlayerVMDTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
                OnFrameApplied    = () =>
                {
                    _viewportManager.UpdateTransform();
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);
                },
            };
            _vmdTestSubPanel.Build(_layoutRoot.VMDTestSection);

            _unityClipTestSubPanel = new PlayerUnityClipTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
                OnFrameApplied    = () =>
                {
                    _viewportManager.UpdateTransform();
                    _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.Dragging);
                },
            };
            _unityClipTestSubPanel.Build(_layoutRoot.UnityClipTestSection);

            _underlaySubPanel = new PlayerUnderlaySubPanel(_underlay, ApplyAllUnderlays);
            _underlaySubPanel.Build(_layoutRoot.UnderlaySection);

            _remoteServerSubPanel = new PlayerRemoteServerSubPanel
            {
                GetServer = () => _playerServer,
            };
            _remoteServerSubPanel.Build(_layoutRoot.RemoteServerSection);

            _vertexMoveSubPanel = new PlayerVertexMoveSubPanel
            {
                GetHandler = () => _moveToolHandler,
            };
            _vertexMoveSubPanel.Build(_layoutRoot.VertexMoveSection);

            _pivotSubPanel = new PlayerPivotSubPanel();
            _pivotSubPanel.Build(_layoutRoot.PivotSection);
            _pivotSubPanel.OnPivotToVertexCentroid = () => MovePivotToCentroid(useBones: false);
            _pivotSubPanel.OnPivotToBoneCentroid   = () => MovePivotToCentroid(useBones: true);

            _sculptSubPanel = new PlayerSculptSubPanel
            {
                GetHandler = () => _sculptHandler,
            };
            _sculptSubPanel.Build(_layoutRoot.SculptSection);
            // 起動時にスライダ範囲・値・詳細設定をハンドラ実値へ同期する。
            _sculptSubPanel.Refresh();

            _advancedSelectSubPanel = new PlayerAdvancedSelectSubPanel
            {
                GetHandler = () => _advancedSelectHandler,
            };
            _advancedSelectSubPanel.Build(_layoutRoot.AdvancedSelectSection);

            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            _importSubPanel = new PlayerImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;

            _exportSubPanel = new PlayerExportSubPanel();
            _exportSubPanel.Build(_layoutRoot.ExportSection);
            _exportSubPanel.OnExportPmx = OnExportPmx;
            _exportSubPanel.OnExportMqo = OnExportMqo;

            _projectFileSubPanel = new PlayerProjectFileSubPanel();
            _projectFileSubPanel.Build(_layoutRoot.ProjectFileSection);
            _projectFileSubPanel.OnSave     = OnSaveProject;
            _projectFileSubPanel.OnLoad     = OnLoadProject;
            _projectFileSubPanel.OnSaveCsv  = OnSaveCsvProject;
            _projectFileSubPanel.OnLoadCsv  = OnLoadCsvProject;
            _projectFileSubPanel.OnMergeCsv = OnMergeCsvProject;

            _partialImportSubPanel = new PlayerPartialImportSubPanel();
            _partialImportSubPanel.Build(_layoutRoot.PartialImportSection);
            _partialImportSubPanel.OnImportDone = OnPartialImportDone;

            _partialExportSubPanel = new PlayerPartialExportSubPanel();
            _partialExportSubPanel.Build(_layoutRoot.PartialExportSection);

            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            _primitiveSubPanel.OnMeshCreated = (mo, name, pos, ign, mode) => OnPrimitiveMeshCreated(mo, name, pos, ign, mode);
            _primitiveSubPanel.GetSelectedMeshObject = () =>
                ActiveProject?.CurrentModel?.FirstSelectedMeshContext?.MeshObject;
            _primitiveSubPanel.GetUndoController = () => _editOps?.UndoController;

            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;
            _layoutRoot.AdvancedPrimitiveBtn.clicked += ShowAdvancedPrimitivePanel;

            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;
            _mfToSkinnedSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);

            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _layoutRoot.BlendBtn.clicked      += ShowBlendPanel;
            _layoutRoot.ModelBlendBtn.clicked += ShowModelBlendPanel;
            _layoutRoot.BoneEditorBtn.clicked  += ShowBoneEditorPanel;
            _layoutRoot.UVEditorBtn.clicked    += ShowUVEditorPanel;
            _layoutRoot.UVUnwrapBtn.clicked    += ShowUVUnwrapPanel;
            _layoutRoot.MaterialListBtn.clicked    += ShowMaterialListPanel;
            _layoutRoot.UVZBtn.clicked             += ShowUVZPanel;
            _layoutRoot.PartsSelectionSetBtn.clicked += ShowPartsSelectionSetPanel;
            _layoutRoot.MeshSelectionSetBtn.clicked  += ShowMeshSelectionSetPanel;
            _layoutRoot.MergeMeshesBtn.clicked     += ShowMergeMeshesPanel;
            _layoutRoot.TPoseBtn.clicked           += ShowTPosePanel;
            _layoutRoot.HumanoidMappingBtn.clicked += ShowHumanoidMappingPanel;
            _layoutRoot.MirrorBtn.clicked          += ShowMirrorPanel;
            _layoutRoot.QuadDecimatorBtn.clicked   += ShowQuadDecimatorPanel;
            _layoutRoot.AlignVerticesBtn.clicked       += ShowAlignVerticesPanel;
            _layoutRoot.PlanarizeAlongBonesBtn.clicked += ShowPlanarizeAlongBonesPanel;
            _layoutRoot.MergeVerticesBtn.clicked       += ShowMergeVerticesPanel;
            _layoutRoot.SplitVerticesBtn.clicked        += ShowSplitVerticesPanel;
            _layoutRoot.AddFaceBtn.clicked               += ShowAddFacePanel;
            _layoutRoot.FlipFaceBtn.clicked              += ShowFlipFacePanel;
            _layoutRoot.RotateBtn.clicked                += ShowRotatePanel;
            _layoutRoot.ScaleBtn.clicked                 += ShowScalePanel;
            _layoutRoot.EdgeBevelBtn.clicked             += ShowEdgeBevelPanel;
            _layoutRoot.EdgeExtrudeBtn.clicked           += ShowEdgeExtrudePanel;
            _layoutRoot.FaceExtrudeBtn.clicked           += ShowFaceExtrudePanel;
            _layoutRoot.EdgeTopologyBtn.clicked          += ShowEdgeTopologyPanel;
            _layoutRoot.KnifeBtn.clicked                 += ShowKnifePanel;
            _layoutRoot.LineExtrudeBtn.clicked           += ShowLineExtrudePanel;
            _layoutRoot.MediaPipeBtn.clicked        += ShowMediaPipePanel;
            _layoutRoot.VMDTestBtn.clicked          += ShowVMDTestPanel;
            _layoutRoot.UnityClipTestBtn.clicked    += ShowUnityClipTestPanel;
            _layoutRoot.RemoteServerBtn.clicked     += ShowRemoteServerPanel;
            if (_layoutRoot.UnderlayBtn != null)
                _layoutRoot.UnderlayBtn.clicked     += ShowUnderlayPanel;
            _layoutRoot.FullExportMqoBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO);
            _layoutRoot.ProjectFileBtn.clicked      += ShowProjectFilePanel;
            _layoutRoot.PartialImportPmxBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX);
            _layoutRoot.PartialImportMqoBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO);
            _layoutRoot.PartialExportPmxBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX);
            _layoutRoot.PartialExportMqoBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO);

            _layoutRoot.ToolVertexMoveBtn.clicked        += () => ShowCategory1Panel(InteractionMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked        += () => ShowCategory1Panel(InteractionMode.ObjectMove);
            _layoutRoot.ToolPivotOffsetBtn.clicked       += () => ShowCategory1Panel(InteractionMode.PivotOffset);
            _layoutRoot.ToolSculptBtn.clicked            += () => ShowCategory1Panel(InteractionMode.Sculpt);
            _layoutRoot.ToolAdvancedSelBtn.clicked       += () => ShowCategory1Panel(InteractionMode.AdvancedSelect);
            _layoutRoot.ToolSkinWeightPaintBtn.clicked   += () => ShowCategory1Panel(InteractionMode.SkinWeightPaint);

            _layoutRoot.LassoToggle.RegisterValueChangedCallback(e =>
            {
                if (_moveToolHandler != null)
                    _moveToolHandler.DragSelectMode = e.newValue
                        ? MoveToolHandler.SelectionDragMode.Lasso
                        : MoveToolHandler.SelectionDragMode.Box;
                // ObjectMove (BoneEditor 統合先) でも頂点モードと同じ Lasso 切替を共有。
                if (_objectMoveHandler != null)
                    _objectMoveHandler.DragSelectMode = e.newValue
                        ? ObjectMoveToolHandler.SelectionDragMode.Lasso
                        : ObjectMoveToolHandler.SelectionDragMode.Box;
            });

            // 選択モード切替（頂点/辺/面/線分・非排他）→ 現モデル各メッシュの Selection.Mode に反映。
            _applySelectMode = () =>
            {
                var model = ActiveProject?.CurrentModel;
                if (model?.MeshContextList == null) return;
                MeshSelectMode m = MeshSelectMode.None;
                if (_layoutRoot.SelModeVertexToggle.value) m |= MeshSelectMode.Vertex;
                if (_layoutRoot.SelModeEdgeToggle.value)   m |= MeshSelectMode.Edge;
                if (_layoutRoot.SelModeFaceToggle.value)   m |= MeshSelectMode.Face;
                if (_layoutRoot.SelModeLineToggle.value)   m |= MeshSelectMode.Line;
                if (m == MeshSelectMode.None) m = MeshSelectMode.Vertex;  // 全OFFは頂点にフォールバック
                foreach (var mc in model.MeshContextList)
                    if (mc?.Selection != null && mc.Type != MeshType.Bone)
                        mc.Selection.Mode = m;
                _activePanel?.MarkDirtyRepaint();
            };

            // 選択モードを端末ローカルに保存（V=1/E=2/F=4/L=8 の 4bit）。PTFS 表示と同じ RecentPaths ストア。
            System.Action saveSelectMode = () =>
            {
                int bits = (_layoutRoot.SelModeVertexToggle.value ? 1 : 0)
                         | (_layoutRoot.SelModeEdgeToggle.value   ? 2 : 0)
                         | (_layoutRoot.SelModeFaceToggle.value   ? 4 : 0)
                         | (_layoutRoot.SelModeLineToggle.value   ? 8 : 0);
                PlayerUiPrefs.SetInt(SelectModePrefKey, bits);
            };

            _layoutRoot.SelModeVertexToggle.RegisterValueChangedCallback(_ => { saveSelectMode(); _applySelectMode(); });
            _layoutRoot.SelModeEdgeToggle  .RegisterValueChangedCallback(_ => { saveSelectMode(); _applySelectMode(); });
            _layoutRoot.SelModeFaceToggle  .RegisterValueChangedCallback(_ => { saveSelectMode(); _applySelectMode(); });
            _layoutRoot.SelModeLineToggle  .RegisterValueChangedCallback(_ => { saveSelectMode(); _applySelectMode(); });

            // 起動時：保存済み選択モードを復元してトグルへ反映（未保存は既定=頂点のまま）。
            {
                int savedBits = PlayerUiPrefs.GetInt(SelectModePrefKey, -1);
                if (savedBits >= 0)
                {
                    _layoutRoot.SelModeVertexToggle.SetValueWithoutNotify((savedBits & 1) != 0);
                    _layoutRoot.SelModeEdgeToggle  .SetValueWithoutNotify((savedBits & 2) != 0);
                    _layoutRoot.SelModeFaceToggle  .SetValueWithoutNotify((savedBits & 4) != 0);
                    _layoutRoot.SelModeLineToggle  .SetValueWithoutNotify((savedBits & 8) != 0);
                }
                _applySelectMode();
            }

            _layoutRoot.ModelListBtn.clicked += ShowModelListPanel;
            _layoutRoot.MeshListBtn .clicked += ShowMeshListPanel;

            _layoutRoot.ModelSelectDropdown.RegisterValueChangedCallback(e =>
            {
                var project = ActiveProject;
                if (project == null) return;
                var choices = _layoutRoot.ModelSelectDropdown.choices;
                int idx = choices != null ? choices.IndexOf(e.newValue) : -1;
                if (idx < 0 || idx == project.CurrentModelIndex) return;
                SwitchActiveModel(idx);
            });

            _localLoader.OnPmxRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.PMX);
            _localLoader.OnMqoRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.MQO);

            _layoutRoot.ConnectBtn   .clicked += () => _client?.Connect();
            _layoutRoot.DisconnectBtn.clicked += () => _client?.Disconnect();
            _layoutRoot.FetchBtn     .clicked += FetchProject;
            _layoutRoot.UndoBtn      .clicked += () => _editOps?.PerformUndo();
            _layoutRoot.RedoBtn      .clicked += () => _editOps?.PerformRedo();

            _layoutRoot.PerspectivePanel.SetViewport(_viewportManager.PerspectiveViewport);
            _layoutRoot.TopPanel        .SetViewport(_viewportManager.TopViewport);
            _layoutRoot.FrontPanel      .SetViewport(_viewportManager.FrontViewport);
            _layoutRoot.SidePanel       .SetViewport(_viewportManager.SideViewport);

            // 面ごとの表示設定トグルを接続する。
            // ViewportDisplayToggles[slot, item] → _viewportManager の設定を更新。
            for (int s = 0; s < 4; s++)
            {
                for (int i = 0; i < PlayerLayoutRoot.VD_COUNT; i++)
                {
                    int slot = s, item = i;
                    _layoutRoot.ViewportDisplayToggles[slot, item]
                        .RegisterValueChangedCallback(e =>
                        {
                            var ds = _viewportManager.GetDisplaySettings(slot);
                            switch (item)
                            {
                                case PlayerLayoutRoot.VD_CULLING:    ds.BackfaceCulling         = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MESH:   ds.ShowSelectedMesh        = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_WIRE:   ds.ShowSelectedWireframe   = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_VERT:   ds.ShowSelectedVertices    = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_BONE:   ds.ShowSelectedBone        = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MESH: ds.ShowUnselectedMesh      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_WIRE: ds.ShowUnselectedWireframe = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_VERT: ds.ShowUnselectedVertices  = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_BONE:   ds.ShowUnselectedBone      = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MIRROR:   ds.ShowSelectedMirror      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR: ds.ShowUnselectedMirror    = e.newValue; break;
                            }
                            // Phase 2a-2g-3: SetDisplaySettings → EnterDisplaySettingsChanged に集約。
                            _viewportManager.EnterDisplaySettingsChanged(slot, ds);
                        });
                }
            }

            // 起動時：復元済みの表示設定（RecentPaths から復元）でチェックボックスを同期する。
            // トグル初期値は itemDefaults（既定）で作られているため、これをしないと
            // 復元値と UI が食い違う（render は _displaySettings を毎フレーム反映するが UI が既定のまま）。
            for (int s = 0; s < 4; s++)
            {
                var ds = _viewportManager.GetDisplaySettings(s);
                void SyncTog(int item, bool v) => _layoutRoot.ViewportDisplayToggles[s, item]?.SetValueWithoutNotify(v);
                SyncTog(PlayerLayoutRoot.VD_CULLING,      ds.BackfaceCulling);
                SyncTog(PlayerLayoutRoot.VD_SEL_MESH,     ds.ShowSelectedMesh);
                SyncTog(PlayerLayoutRoot.VD_SEL_WIRE,     ds.ShowSelectedWireframe);
                SyncTog(PlayerLayoutRoot.VD_SEL_VERT,     ds.ShowSelectedVertices);
                SyncTog(PlayerLayoutRoot.VD_SEL_BONE,     ds.ShowSelectedBone);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MESH,   ds.ShowUnselectedMesh);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_WIRE,   ds.ShowUnselectedWireframe);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_VERT,   ds.ShowUnselectedVertices);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_BONE,   ds.ShowUnselectedBone);
                SyncTog(PlayerLayoutRoot.VD_SEL_MIRROR,   ds.ShowSelectedMirror);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR, ds.ShowUnselectedMirror);
            }

            _layoutRoot.MorphBtn.clicked       += ShowMorphPanel;
            _layoutRoot.MorphCreateBtn.clicked += ShowMorphCreatePanel;

            _layoutRoot.PostBuildButtonColors(_uiRoot);

            _sectionRefreshPairs.Clear();
            _sectionRefreshPairs.Add((_layoutRoot.BoneEditorSection,        () => _boneEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVEditorSection,          () => _uvEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVUnwrapSection,          () => _uvUnwrapSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MaterialListSection,      () => _materialListSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVZSection,               () => _uvzSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PartsSelectionSetSection, () => _partsSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshSelectionSetSection,  () => _meshSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeMeshesSection,       () => _mergeMeshesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphSection,             () => _morphSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphCreateSection,       () => _morphCreateSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.TPoseSection,             () => _tposeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.HumanoidMappingSection,   () => _humanoidMappingSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.QuadDecimatorSection,         () => _quadDecimatorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AlignVerticesSection,         () => _alignVerticesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PlanarizeAlongBonesSection,   () => _planarizeAlongBonesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
                _mergeVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.SplitVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _splitVerticesHandler?.Activate(ctx);
                _splitVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.AddFaceSection,           () => _addFaceSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FlipFaceSection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _flipFaceHandler?.Activate(ctx); _flipFaceSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.RotateSection,            () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _rotateHandler?.Activate(ctx); _rotateSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.ScaleSection,             () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _scaleHandler?.Activate(ctx); _scaleSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeBevelSection,         () => _edgeBevelSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeExtrudeSection,       () => _edgeExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceExtrudeSection,       () => _faceExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeTopologySection,      () => _edgeTopologySubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.KnifeSection,             () => _knifeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.LineExtrudeSection,       () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _lineExtrudeHandler?.Activate(ctx); _lineExtrudeSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.MediaPipeSection,         () => _mediaPipeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VMDTestSection,           () => _vmdTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UnityClipTestSection,     () => _unityClipTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.RemoteServerSection,      () => _remoteServerSubPanel?.Refresh()));

            ShowCategory1Panel(InteractionMode.VertexMove);
        }

        // ================================================================
        // パネル表示切替
        // ================================================================

        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ImportSection, null);
            _importSubPanel?.SetMode(mode);
        }

        private void ShowPrimitivePanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持
            // 基本図形/高度な図形は同一 PrimitiveSection を共有し、グリッドだけカテゴリで切替える。
            ShowRightPanel(_layoutRoot?.PrimitiveSection, _layoutRoot?.PrimitiveBtn);
            _primitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Basic);
        }

        private void ShowAdvancedPrimitivePanel()
        {
            // 基本図形と同じセクションを開き、カテゴリのみ高度な図形へ切り替える。
            ShowRightPanel(_layoutRoot?.PrimitiveSection, _layoutRoot?.AdvancedPrimitiveBtn);
            _primitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced);
        }

        private void ShowMeshFilterToSkinnedPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshFilterToSkinnedSection, _layoutRoot?.MeshFilterToSkinnedBtn);
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowBlendPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BlendSection, _layoutRoot?.BlendBtn);
            _blendSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowModelBlendPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ModelBlendSection, _layoutRoot?.ModelBlendBtn);
            _modelBlendSubPanel?.Init();
        }

        private void ShowBoneEditorPanel()
        {
            // 案 A: InteractionMode を ObjectMove に強制 + RightPanel ボタンは BoneEditorBtn
            // 結果: ToolObjectMoveBtn が青 (InteractionMode)、BoneEditorBtn が緑 (RightPanel)
            SetInteractionMode(InteractionMode.ObjectMove);
            ShowRightPanel(_layoutRoot?.BoneEditorSection, _layoutRoot?.BoneEditorBtn);
            _boneEditorSubPanel?.Refresh();
        }

        private void ShowUVEditorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVEditorSection, _layoutRoot?.UVEditorBtn);

            // UndoController に対象メッシュを設定（CaptureMeshObjectSnapshot に必要）
            var uvModel = ActiveProject?.CurrentModel;
            var uvMc    = uvModel?.FirstDrawableMeshContext;
            if (uvMc?.MeshObject != null && _editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = uvModel;
                _uvUndoMasterIndex = uvModel.IndexOf(uvMc);
            }

            _uvEditorSubPanel?.Refresh();
        }

        private void ShowUVUnwrapPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVUnwrapSection, _layoutRoot?.UVUnwrapBtn);
            _uvUnwrapSubPanel?.Refresh();
        }

        private void ShowMaterialListPanel()
        {
            // 選択専用: 面を選択してマテリアルを適用できるよう、移動なしの選択のみ有効化する。
            SetInteractionMode(InteractionMode.SelectOnly);
            ShowRightPanel(_layoutRoot?.MaterialListSection, _layoutRoot?.MaterialListBtn);
            _materialListSubPanel?.SyncEditingSlotToCurrent();
            _materialListSubPanel?.Refresh();
        }

        private void ShowUVZPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVZSection, _layoutRoot?.UVZBtn);
            _uvzSubPanel?.Refresh();
        }

        private void ShowPartsSelectionSetPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持
            ShowRightPanel(_layoutRoot?.PartsSelectionSetSection, _layoutRoot?.PartsSelectionSetBtn);
            _partsSelSetSubPanel?.Refresh();
        }

        private void ShowMeshSelectionSetPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshSelectionSetSection, _layoutRoot?.MeshSelectionSetBtn);
            _meshSelSetSubPanel?.Refresh();
        }

        private void ShowMergeMeshesPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MergeMeshesSection, _layoutRoot?.MergeMeshesBtn);
            _mergeMeshesSubPanel?.Refresh();
        }

        private void ShowMorphPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MorphSection, _layoutRoot?.MorphBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            // （MorphExpressionEditRecord/ChangeRecord が正しいモデルを参照するために必要）
            var morphModel = ActiveProject?.CurrentModel;
            if (morphModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphModel);

            _morphSubPanel?.Refresh();
        }

        private void ShowMorphCreatePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MorphCreateSection, _layoutRoot?.MorphCreateBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            var morphCrModel = ActiveProject?.CurrentModel;
            if (morphCrModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphCrModel);

            _morphCreateSubPanel?.Refresh();
        }

        private void ShowTPosePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.TPoseSection, _layoutRoot?.TPoseBtn);
            // MeshListStack のコンテキストを現在のモデルに設定（TPoseUndoRecord が参照するため）
            var tpModel = ActiveProject?.CurrentModel;
            if (tpModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(tpModel);
            _tposeSubPanel?.Refresh();
        }

        private void ShowHumanoidMappingPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.HumanoidMappingSection, _layoutRoot?.HumanoidMappingBtn);
            var hmModel = ActiveProject?.CurrentModel;
            if (hmModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(hmModel);
            _humanoidMappingSubPanel?.Refresh();
        }

        private void ShowMirrorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MirrorSection, _layoutRoot?.MirrorBtn);
        }

        private void ShowQuadDecimatorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.QuadDecimatorSection, _layoutRoot?.QuadDecimatorBtn);
            _quadDecimatorSubPanel?.Refresh();
        }

        private void ShowAlignVerticesPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.AlignVerticesSection, _layoutRoot?.AlignVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _alignVerticesHandler?.Activate(ctx);
            _alignVerticesSubPanel?.Refresh();
        }

        private void ShowPlanarizeAlongBonesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.PlanarizeAlongBonesSection, _layoutRoot?.PlanarizeAlongBonesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _planarizeAlongBonesHandler?.Activate(ctx);
            _planarizeAlongBonesSubPanel?.Refresh();
        }

        private void ShowMergeVerticesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.MergeVerticesSection, _layoutRoot?.MergeVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null)
            {
                _mergeVerticesHandler?.Activate(ctx);
                _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
            }
            _mergeVerticesSubPanel?.Refresh();
        }


        private void ShowFlipFacePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用し、Selection.Mode を
            // Face のみに絞る。反転実行自体はサブパネル経由 (本セッション対象外、別件)。
            ShowCategory1Panel(InteractionMode.FlipFace);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _flipFaceHandler?.Activate(ctx);
        }

        private void ShowRotatePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用。
            // 現状の回転実行はサブパネルのスライダ経由のまま。
            // 将来的には独自形状ギズモ (回転リング) をビューポートに表示し、
            // MoveToolHandler のフック (OnDragStartExtra 等) 経由で回転操作を
            // 実現する予定。
            ShowCategory1Panel(InteractionMode.Rotate);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _rotateHandler?.Activate(ctx);
        }

        private void ShowScalePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用。
            // 現状の拡大縮小実行はサブパネルのスライダ経由のまま。
            // 将来的には独自形状ギズモ (軸端ハンドル等) をビューポートに表示し、
            // MoveToolHandler のフック経由で拡大縮小操作を実現する予定。
            ShowCategory1Panel(InteractionMode.Scale);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _scaleHandler?.Activate(ctx);
        }

        private void ShowEdgeBevelPanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeBevel);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeBevelHandler?.Activate(ctx);
        }

        private void ShowEdgeExtrudePanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeExtrude);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeExtrudeHandler?.Activate(ctx);
        }

        private void ShowFaceExtrudePanel()
        {
            ShowCategory1Panel(InteractionMode.FaceExtrude);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceExtrudeHandler?.Activate(ctx);
        }

        private void ShowEdgeTopologyPanel()
        {
            ShowCategory1Panel(InteractionMode.EdgeTopology);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeTopologyHandler?.Activate(ctx);
        }

        private void ShowLineExtrudePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用し、Selection.Mode を
            // Line のみに絞る。押し出し実行自体はサブパネル経由 (本セッション対象外、別件)。
            ShowCategory1Panel(InteractionMode.LineExtrude);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _lineExtrudeHandler?.Activate(ctx);
        }

        private void ShowKnifePanel()
        {
            ShowCategory1Panel(InteractionMode.Knife);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _knifeHandler?.Activate(ctx);
        }
        private void ShowAddFacePanel()
        {
            ShowCategory1Panel(InteractionMode.AddFace);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _addFaceHandler?.Activate(ctx);
            // 面追加時は頂点ホバーのみ必要。辺・面のホバーは有害なので抑制する。
            var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
            if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.Vertex;
        }

        private void ShowSplitVerticesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.SplitVerticesSection, _layoutRoot?.SplitVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _splitVerticesHandler?.Activate(ctx);
            _splitVerticesSubPanel?.Refresh();
        }

        private void ShowMediaPipePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MediaPipeSection, _layoutRoot?.MediaPipeBtn);
            _mediaPipeSubPanel?.Refresh();
        }

        private void ShowVMDTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VMDTestSection, _layoutRoot?.VMDTestBtn);
            _vmdTestSubPanel?.Refresh();
        }

        private void ShowUnityClipTestPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnityClipTestSection, _layoutRoot?.UnityClipTestBtn);
            _unityClipTestSubPanel?.Refresh();
        }

        private void ShowRemoteServerPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.RemoteServerSection, _layoutRoot?.RemoteServerBtn);
            _remoteServerSubPanel?.Refresh();
        }

        private void ShowUnderlayPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnderlaySection, _layoutRoot?.UnderlayBtn);
            _underlayActive = true;   // 左ドラッグでオフセット移動を有効化
        }

        // ================================================================
        // 下絵（3D背面に敷く参照画像）の適用
        // ================================================================

        /// <summary>ビューポート vp の現在の表示方向に対応する下絵スロットを返す。</summary>
        private UnderlayDirection GetUnderlayDirection(PlayerViewport vp)
        {
            if (vp == _viewportManager.PerspectiveViewport)
                return (vp.Orbit != null && vp.Orbit.Orthographic)
                     ? UnderlayDirection.Ortho : UnderlayDirection.Persp;
            if (vp == _viewportManager.TopViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Bottom : UnderlayDirection.Top;
            if (vp == _viewportManager.FrontViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Back : UnderlayDirection.Front;
            if (vp == _viewportManager.SideViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Left : UnderlayDirection.Right;
            return UnderlayDirection.Persp;
        }

        /// <summary>
        /// 指定ビューへ現在方向の下絵を適用する。画像があればカメラ背景を透明化して
        /// 背面の下絵を見せ、なければ不透明に戻す。最後に再描画を要求する。
        /// </summary>
        private void ApplyUnderlayToViewport(PlayerViewport vp, PlayerViewportPanel panel)
        {
            if (vp == null || panel == null) return;

            var slot = _underlay.Get(GetUnderlayDirection(vp));
            if (slot != null && slot.HasImage)
            {
                panel.SetUnderlay(slot.Texture, slot.TopLeft, slot.ScaleOrigin, slot.Scale);
                vp.SetClearTransparent(true);
            }
            else
            {
                panel.ClearUnderlay();
                vp.SetClearTransparent(false);
            }

            // クリア色の変化を反映するため再描画。
            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
        }

        /// <summary>4ビュー全てへ下絵を再適用する（設定変更時）。</summary>
        private void ApplyAllUnderlays()
        {
            ApplyUnderlayToViewport(_viewportManager.PerspectiveViewport, _layoutRoot?.PerspectivePanel);
            ApplyUnderlayToViewport(_viewportManager.TopViewport,        _layoutRoot?.TopPanel);
            ApplyUnderlayToViewport(_viewportManager.FrontViewport,      _layoutRoot?.FrontPanel);
            ApplyUnderlayToViewport(_viewportManager.SideViewport,       _layoutRoot?.SidePanel);
        }

        private void ShowExportPanel(PlayerExportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            var btn = mode == PlayerExportSubPanel.Mode.PMX
                ? _layoutRoot?.FullExportPmxBtn
                : _layoutRoot?.FullExportMqoBtn;
            ShowRightPanel(_layoutRoot?.ExportSection, btn);
            _exportSubPanel?.SetMode(mode);
        }

        private void ShowProjectFilePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ProjectFileSection, _layoutRoot?.ProjectFileBtn);
        }

        private void ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            var btn = mode == PlayerPartialImportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialImportPmxBtn
                : _layoutRoot?.PartialImportMqoBtn;
            ShowRightPanel(_layoutRoot?.PartialImportSection, btn);
            var model = ActiveProject?.CurrentModel;
            if (model != null) _editOps?.UndoController.SetModelContext(model);
            _partialImportSubPanel?.SetModel(model, _editOps?.UndoController);
            _partialImportSubPanel?.SetMode(mode);
        }

        private void ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            var btn = mode == PlayerPartialExportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialExportPmxBtn
                : _layoutRoot?.PartialExportMqoBtn;
            ShowRightPanel(_layoutRoot?.PartialExportSection, btn);
            var model = ActiveProject?.CurrentModel;
            _partialExportSubPanel?.SetModel(model);
            _partialExportSubPanel?.SetMode(mode);
        }

        private void ShowModelListPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ModelListSection, _layoutRoot?.ModelListBtn);
        }

        private void ShowMeshListPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshListSection, _layoutRoot?.MeshListBtn);
        }

        private void HideAllRightPanels()
        {
            if (_layoutRoot == null) return;
            void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }
            Hide(_layoutRoot.ModelListSection);
            Hide(_layoutRoot.MeshListSection);
            Hide(_layoutRoot.SkinWeightPaintSection);
            Hide(_layoutRoot.VertexMoveSection);
            Hide(_layoutRoot.PivotSection);
            Hide(_layoutRoot.SculptSection);
            Hide(_layoutRoot.AdvancedSelectSection);
            Hide(_layoutRoot.ImportSection);
            Hide(_layoutRoot.ExportSection);
            Hide(_layoutRoot.ProjectFileSection);
            Hide(_layoutRoot.PartialImportSection);
            Hide(_layoutRoot.PartialExportSection);
            Hide(_layoutRoot.PrimitiveSection);
            Hide(_layoutRoot.MeshFilterToSkinnedSection);
            Hide(_layoutRoot.BlendSection);
            Hide(_layoutRoot.ModelBlendSection);
            Hide(_layoutRoot.BoneEditorSection);
            Hide(_layoutRoot.UVEditorSection);
            Hide(_layoutRoot.UVUnwrapSection);
            Hide(_layoutRoot.MaterialListSection);
            Hide(_layoutRoot.UVZSection);
            Hide(_layoutRoot.PartsSelectionSetSection);
            Hide(_layoutRoot.MeshSelectionSetSection);
            Hide(_layoutRoot.MergeMeshesSection);
            Hide(_layoutRoot.MorphSection);
            Hide(_layoutRoot.MorphCreateSection);
            Hide(_layoutRoot.TPoseSection);
            Hide(_layoutRoot.HumanoidMappingSection);
            Hide(_layoutRoot.MirrorSection);
            Hide(_layoutRoot.QuadDecimatorSection);
            Hide(_layoutRoot.AlignVerticesSection);
            Hide(_layoutRoot.PlanarizeAlongBonesSection);
            Hide(_layoutRoot.MergeVerticesSection);
            Hide(_layoutRoot.SplitVerticesSection);
            Hide(_layoutRoot.AddFaceSection);
            Hide(_layoutRoot.FlipFaceSection);
            Hide(_layoutRoot.RotateSection);
            Hide(_layoutRoot.ScaleSection);
            Hide(_layoutRoot.EdgeBevelSection);
            Hide(_layoutRoot.EdgeExtrudeSection);
            Hide(_layoutRoot.FaceExtrudeSection);
            Hide(_layoutRoot.EdgeTopologySection);
            Hide(_layoutRoot.KnifeSection);
            Hide(_layoutRoot.LineExtrudeSection);
            Hide(_layoutRoot.MediaPipeSection);
            Hide(_layoutRoot.VMDTestSection);
            Hide(_layoutRoot.UnityClipTestSection);
            Hide(_layoutRoot.RemoteServerSection);
            Hide(_layoutRoot.UnderlaySection);
            _underlayActive = false;   // 別パネルへ切替時は下絵ドラッグを無効化
        }

        // ================================================================
        // ボタンアクティブ色
        // ================================================================

        // ================================================================
        // ボタンハイライト 2 系統 (段階 2)
        //
        // カテゴリ 1 ボタン (VertexMove 等): InteractionMode と RightPanel の両方を担う
        // カテゴリ 2 ボタン (AlignVertices 等): RightPanel のみ。InteractionMode は維持
        // カテゴリ 3 ボタン (Mirror 等): RightPanel のみ。InteractionMode=None
        //
        // 同一ボタンが両系統 active になる場合は BothActiveBtnColor で表示する。
        // ================================================================

        // 非 active 色 (既存)
        private static readonly StyleColor InactiveBtnColor         = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
        // InteractionMode のみ active (青)
        private static readonly StyleColor InteractionActiveBtnColor = new StyleColor(new Color(0.3f,  0.5f,  1.0f));
        // RightPanel のみ active (緑系)
        private static readonly StyleColor PanelActiveBtnColor       = new StyleColor(new Color(0.3f,  0.75f, 0.4f));
        // 両方 active (α: 混色の青緑)
        private static readonly StyleColor BothActiveBtnColor        = new StyleColor(new Color(0.3f,  0.625f, 0.7f));

        // 旧 _activeBtn を 2 つに分割
        private Button _activeInteractionBtn;   // InteractionMode を示すボタン
        private Button _activePanelBtn;         // 現在開いている RightPanel を示すボタン

        /// <summary>
        /// InteractionMode に対応するボタンを取得。ない (None / 未割当) なら null。
        /// </summary>
        private Button GetButtonForInteractionMode(InteractionMode mode)
        {
            if (_layoutRoot == null) return null;
            switch (mode)
            {
                case InteractionMode.VertexMove:      return _layoutRoot.ToolVertexMoveBtn;
                case InteractionMode.ObjectMove:      return _layoutRoot.ToolObjectMoveBtn;
                case InteractionMode.PivotOffset:     return _layoutRoot.ToolPivotOffsetBtn;
                case InteractionMode.Sculpt:          return _layoutRoot.ToolSculptBtn;
                case InteractionMode.AdvancedSelect:  return _layoutRoot.ToolAdvancedSelBtn;
                case InteractionMode.SkinWeightPaint: return _layoutRoot.ToolSkinWeightPaintBtn;
                // AddFace / EdgeBevel / EdgeExtrude / FaceExtrude / EdgeTopology / Knife
                // はツールボタンを持たない (右ペインから起動) ため null のまま。
                default: return null;
            }
        }

        /// <summary>
        /// 全ツールボタン/パネルボタンの背景色を _activeInteractionBtn / _activePanelBtn
        /// の状態から再計算する。両系統を同時に反映するため単一の経路にまとめる。
        /// </summary>
        private void RepaintButtonHighlights()
        {
            // 候補ボタン集合 (null 安全に列挙)
            var btns = new System.Collections.Generic.List<Button>();
            if (_layoutRoot != null)
            {
                void Add(Button b) { if (b != null) btns.Add(b); }
                // InteractionMode 側
                Add(_layoutRoot.ToolVertexMoveBtn);
                Add(_layoutRoot.ToolObjectMoveBtn);
                Add(_layoutRoot.ToolPivotOffsetBtn);
                Add(_layoutRoot.ToolSculptBtn);
                Add(_layoutRoot.ToolAdvancedSelBtn);
                Add(_layoutRoot.ToolSkinWeightPaintBtn);
                // 現在パネルを示す可能性があるボタンは、_activePanelBtn が非 null のとき
                // それ 1 つだけなので個別列挙は不要 (下の色設定で扱う)
            }

            // InteractionMode ボタンのデフォルト色: _activeInteractionBtn なら青、そうでなければ非 active
            foreach (var b in btns)
            {
                bool isInteraction = (b == _activeInteractionBtn);
                bool isPanel       = (b == _activePanelBtn);
                if (isInteraction && isPanel) b.style.backgroundColor = BothActiveBtnColor;
                else if (isInteraction)       b.style.backgroundColor = InteractionActiveBtnColor;
                else if (isPanel)             b.style.backgroundColor = PanelActiveBtnColor;
                else                          b.style.backgroundColor = InactiveBtnColor;
            }

            // _activePanelBtn が btns 以外 (カテゴリ 3 のパネル専用ボタン等) のとき単独着色
            if (_activePanelBtn != null && !btns.Contains(_activePanelBtn))
            {
                // InteractionMode ボタンと同時 active は別ボタンに分離されているので緑のみ
                _activePanelBtn.style.backgroundColor = PanelActiveBtnColor;
            }
        }

        /// <summary>
        /// InteractionMode ボタンのハイライトを現在の _interactionMode に基づき更新する。
        /// SetInteractionMode 末尾で呼ぶ。
        /// </summary>
        private void UpdateInteractionButtonHighlight()
        {
            // 旧 _activeInteractionBtn が別パネルの _activePanelBtn と重なっていたら、
            // その重なりを外すためにも Repaint で一括処理する。
            _activeInteractionBtn = GetButtonForInteractionMode(_interactionMode);
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel ボタンのハイライトを設定。null で解除。
        /// </summary>
        private void SetActivePanelButton(Button btn)
        {
            // 以前の _activePanelBtn が候補外 (カテゴリ 3 系) の場合、そのボタンだけは
            // 個別に非 active 色へ戻す。
            if (_activePanelBtn != null && _activePanelBtn != btn)
                _activePanelBtn.style.backgroundColor = InactiveBtnColor;
            _activePanelBtn = btn;
            RepaintButtonHighlights();
        }

        /// <summary>
        /// RightPanel の標準切替: HideAllRightPanels → section 表示 → パネルボタンをハイライト。
        /// カテゴリ 1/2/3 共通に使える。SetInteractionMode とは独立。
        /// </summary>
        private void ShowRightPanel(VisualElement section, Button panelBtn)
        {
            HideAllRightPanels();
            if (section != null) section.style.display = DisplayStyle.Flex;
            SetActivePanelButton(panelBtn);
        }

        // ================================================================
        // コールバック / イベントハンドラ
        // ================================================================

        private void OnPrimitiveMeshCreated(
            MeshObject meshObject, string meshName, Vector3 worldPos,
            bool ignorePoseInArmature, PrimitiveAddMode addMode)
        {
            _localLoader.EnsureProject();
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _alignVerticesHandler?.SetProject(ActiveProject);
            _planarizeAlongBonesHandler?.SetProject(ActiveProject);
            _mergeVerticesHandler?.SetProject(ActiveProject);
            _splitVerticesHandler?.SetProject(ActiveProject);
            _addFaceHandler?.SetProject(ActiveProject);
            _flipFaceHandler?.SetProject(ActiveProject);
            _rotateHandler?.SetProject(ActiveProject);
            _scaleHandler?.SetProject(ActiveProject);
            _edgeBevelHandler?.SetProject(ActiveProject);
            _edgeExtrudeHandler?.SetProject(ActiveProject);
            _faceExtrudeHandler?.SetProject(ActiveProject);
            _edgeTopologyHandler?.SetProject(ActiveProject);
            _knifeHandler?.SetProject(ActiveProject);
            _lineExtrudeHandler?.SetProject(ActiveProject);

            var project = ActiveProject;
            if (project == null) return;
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            _applySelectMode?.Invoke();  // 永続選択モードを新規アクティブモデルへ適用

            switch (addMode)
            {
                case PrimitiveAddMode.NewObject:
                    PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    PrimitiveMeshAddToExisting(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
                case PrimitiveAddMode.NewModel:
                    PrimitiveMeshCreateNewModel(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
            }
        }

        /// <summary>
        /// 図形生成共通: MeshContextを作って返す。
        /// </summary>
        private MeshContext BuildPrimitiveMeshContext(
            MeshObject meshObject, string meshName, Vector3 worldPos, bool ignorePoseInArmature)
        {
            var unityMesh = meshObject.ToUnityMesh();
            unityMesh.name      = meshName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            var ctx = new MeshContext
            {
                Name      = meshName,
                MeshObject = meshObject,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };

            if (ctx.BoneTransform != null && worldPos != Vector3.zero)
            {
                ctx.BoneTransform.UseLocalTransform = true;
                ctx.BoneTransform.Position = worldPos;
            }

            ctx.IgnorePoseInArmature = ignorePoseInArmature;
            return ctx;
        }

        /// <summary>
        /// モード1: 新しい描画オブジェクトとして現在のモデルに追加。UNDO対応。
        /// </summary>
        private void PrimitiveMeshCreateNewObject(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var model = project.CurrentModel;
            if (model == null) return;

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos, ignorePoseInArmature);
            ctx.ParentModelContext = model;

            var oldSelected = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(ctx);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(insertIndex);
            model.SelectMesh(insertIndex);
            var newSelected = model.CaptureAllSelectedIndices();

            // UNDO記録
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// モード2: 既存の選択中描画オブジェクトに頂点・面をマージ。UNDO対応。
        /// 描画オブジェクトが存在しない場合はモード1にフォールバック。
        /// </summary>
        private void PrimitiveMeshAddToExisting(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var model  = project.CurrentModel;
            if (model == null) return;

            // 対象MeshContextを選択（なければ新規作成にフォールバック）
            var targetMc = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
            if (targetMc == null || targetMc.MeshObject == null)
            {
                PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                return;
            }

            // ワールド位置オフセットを頂点に適用
            var srcObject = meshObject;
            if (worldPos != Vector3.zero)
            {
                srcObject = meshObject.Clone();
                foreach (var v in srcObject.Vertices)
                    v.Position += worldPos;
            }

            // UNDO: 変更前スナップショット
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            // マージ
            int baseVertIdx = targetMc.MeshObject.VertexCount;
            foreach (var v in srcObject.Vertices)
                targetMc.MeshObject.Vertices.Add(v.Clone());
            foreach (var f in srcObject.Faces)
            {
                var newFace = new Face();
                newFace.VertexIndices  = f.VertexIndices.ConvertAll(i => i + baseVertIdx);
                newFace.UVIndices      = new System.Collections.Generic.List<int>(f.UVIndices);
                newFace.NormalIndices  = new System.Collections.Generic.List<int>(f.NormalIndices);
                newFace.MaterialIndex  = f.MaterialIndex;
                targetMc.MeshObject.Faces.Add(newFace);
            }
            // UnityMesh再構築
            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            if (targetMc.UnityMesh != null)
                UnityEngine.Object.Destroy(targetMc.UnityMesh);
            targetMc.UnityMesh = newUnityMesh;

            // UNDO: 変更後スナップショット記録
            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(before, after, $"Add Primitive to {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// モード3: 新しいモデルを作って描画オブジェクトを追加。UNDO対応（メッシュ追加のみ）。
        /// </summary>
        private void PrimitiveMeshCreateNewModel(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var newModel = project.CreateNewModel(meshName);
            if (newModel == null) return;

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos, ignorePoseInArmature);
            ctx.ParentModelContext = newModel;

            var oldSelected = newModel.CaptureAllSelectedIndices();
            int insertIndex = newModel.Add(ctx);
            newModel.ComputeWorldMatrices();
            newModel.SelectMeshContextExclusive(insertIndex);
            newModel.SelectMesh(insertIndex);
            var newSelected = newModel.CaptureAllSelectedIndices();

            // UNDO記録（新モデル上のメッシュ追加）
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(newModel);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected);
            }

            // ハンドラーを新モデルに切り替え
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);

            PrimitiveMeshFinalize(newModel);
            RebuildModelList();
        }

        /// <summary>
        /// 図形生成後の共通ビュー更新処理。
        /// </summary>
        private void PrimitiveMeshFinalize(ModelContext model)
        {
            // 材質0件のモデルへ基本図形を追加したとき、描画は GetDefaultMaterial()（灰0.7）へ
            // フォールバックするだけで材質リストには何も入らない。マテリアルパネルで編集できるよう、
            // 初回0件時のみ同じ灰の既定スロットを1つ生成する（見た目は変えない）。
            EnsureDefaultMaterialSlot(model);

            // Phase 2a-2b-2 Batch 3: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
            // EnterSceneReset に集約。カメラは別途 NotifyCameraChanged で個別に呼ぶ。
            _viewportManager.EnterSceneReset(ActiveProject);
            _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);

            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
        }

        /// <summary>
        /// 材質0件のモデルに、描画フォールバック(GetDefaultMaterial)と同じ灰(0.7)の
        /// 既定材質スロットを1つ生成する。既に1件以上あれば何もしない。
        /// </summary>
        private void EnsureDefaultMaterialSlot(ModelContext model)
        {
            if (model == null || model.MaterialCount > 0) return;

            model.AddMaterial(null);   // 既定 MaterialData（URPLit）でスロット追加
            var matRef = model.GetMaterialReference(0);
            if (matRef?.Data != null)
            {
                matRef.Data.Name = "Default";
                matRef.Data.SetBaseColor(new Color(0.7f, 0.7f, 0.7f, 1f));
                matRef.InvalidateCache();   // Data から材質を再生成させる
            }
            model.CurrentMaterialIndex = 0;
        }

        private void OnImportPmx(string filePath, PMXImportSettings settings)
        {
            var cmd = new ImportPmxCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"PMX読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnImportMqo(string filePath, MQOImportSettings settings)
        {
            var cmd = new ImportMqoCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"MQO読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnExportPmx(string outputPath, PMXExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = PMXExporter.Export(model, outputPath, settings);
                if (result.Success)
                {
                    AuxiliaryBackupWriter.Save(model, outputPath);
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                }
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
        }

        private void OnExportMqo(string outputPath, MQOExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = MQOExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                {
                    AuxiliaryBackupWriter.Save(model, outputPath);
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                }
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
        }

        private void OnSaveProject()
        {
            var project = ActiveProject;
            if (project == null) { _projectFileSubPanel?.SetStatus("プロジェクトがありません"); return; }
            var dto = ProjectSerializer.FromProjectContext(project);
            if (dto == null) { _projectFileSubPanel?.SetStatus("シリアライズ失敗"); return; }
            bool ok = ProjectSerializer.ExportWithDialog(dto, project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "保存完了" : "キャンセルまたは失敗");
        }

        private void OnLoadProject()
        {
            var dto = ProjectSerializer.ImportWithDialog();
            if (dto == null) { _projectFileSubPanel?.SetStatus("キャンセルまたは失敗"); return; }
            var loadedProject = ProjectSerializer.ToProjectContext(dto);
            if (loadedProject == null) { _projectFileSubPanel?.SetStatus("復元失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? dto.name, m);
            _projectFileSubPanel?.SetStatus($"読込完了: {dto.name}");
        }

        private void OnSaveCsvProject()
        {
            var project = ActiveProject;
            if (project == null) { _projectFileSubPanel?.SetStatus("プロジェクトがありません"); return; }
            bool ok = CsvProjectSerializer.ExportWithDialog(project, defaultName: project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "CSVフォルダ保存完了" : "キャンセルまたは失敗");
        }

        private void OnLoadCsvProject()
        {
            var loadedProject = CsvProjectSerializer.ImportWithDialog(out _, out _);
            if (loadedProject == null) { _projectFileSubPanel?.SetStatus("キャンセルまたは失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? loadedProject.Name, m);
            _projectFileSubPanel?.SetStatus($"CSV読込完了: {loadedProject.Name}");
        }

        // ==== ピボット重心スナップ（頂点のみ移動＋カメラ逆移動で「ピボットが動いた」ように見せる） ====
        // C = 目標重心(world)、P = 現ピボット原点(world)、Δ = C − P。
        // 原点・ボーンは動かさず、当該メッシュの全頂点を −Δ 相当だけローカルにシフトし、
        // カメラ Target を −Δ 動かす（見た目静止・ピボットが重心へ来たように見せる）。
        private void MovePivotToCentroid(bool useBones)
        {
            var model = ActiveProject?.CurrentModel;
            var ctx   = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (model == null || ctx == null) return;

            var mc = ctx.FirstSelectedMeshContext;
            var mo = mc?.MeshObject;
            if (mo == null || mo.VertexCount == 0)
            {
                Debug.LogWarning("[Pivot] 頂点を持つメッシュが選択されていません。");
                return;
            }

            // 目標重心 C（world）
            Vector3 C;
            if (useBones)
            {
                Vector3 sum = Vector3.zero; int nb = 0;
                var bones = ctx.SelectedMeshContexts;
                if (bones != null)
                    foreach (var b in bones)
                        if (b != null && b.Type == MeshType.Bone) { sum += (Vector3)b.WorldMatrix.GetColumn(3); nb++; }
                if (nb == 0) { Debug.LogWarning("[Pivot] ボーンが選択されていません。"); return; }
                C = sum / nb;
            }
            else
            {
                var sel = ctx.SelectedVertices;
                if (sel == null || sel.Count == 0) { Debug.LogWarning("[Pivot] 頂点が選択されていません。"); return; }
                Vector3 sum = Vector3.zero; int nv = 0;
                foreach (int idx in sel)
                {
                    if (idx < 0 || idx >= mo.VertexCount) continue;
                    sum += mc.WorldMatrix.MultiplyPoint3x4(mo.Vertices[idx].Position);
                    nv++;
                }
                if (nv == 0) { Debug.LogWarning("[Pivot] 有効な選択頂点がありません。"); return; }
                C = sum / nv;
            }

            Vector3 P = mc.WorldMatrix.GetColumn(3);        // 現ピボット原点(world)
            Vector3 deltaWorld = C - P;
            if (deltaWorld.sqrMagnitude < 1e-12f) return;    // 既に一致

            // 原点は不変のまま、全頂点を −Δ 相当だけローカルにシフト
            Vector3 localShift = mc.WorldMatrix.inverse.MultiplyVector(-deltaWorld);

            int count = mo.VertexCount;
            var indices = new int[count];
            var oldPos  = new Vector3[count];
            var newPos  = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
                var v = mo.Vertices[i];
                oldPos[i] = v.Position;
                v.Position += localShift;
                mo.Vertices[i] = v;
                newPos[i] = v.Position;
            }

            // Undo 記録
            if (_editOps?.UndoController != null)
            {
                int mcIndex = model.MeshContextList.IndexOf(mc);
                var entry = new MeshMoveEntry
                {
                    MeshContextIndex = mcIndex,
                    Indices = indices,
                    OldPositions = oldPos,
                    NewPositions = newPos
                };
                var record = new MultiMeshVertexMoveRecord(new[] { entry });
                _editOps.UndoController.FocusVertexEdit();
                _editOps.UndoController.VertexEditStack.Record(record, useBones ? "Pivot→ボーン重心" : "Pivot→頂点重心");
            }

            // 同期＋カメラ逆移動（Target を −Δ 動かして見た目静止）
            _viewportManager.SyncMeshPositionsAndTransform(mc, model);
            var orbit = _activeViewport?.Orbit;
            if (orbit != null) orbit.SetTarget(orbit.Target - deltaWorld);
            _activePanel?.MarkDirtyRepaint();
        }

        private void OnMergeCsvProject()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _projectFileSubPanel?.SetStatus("モデルがありません"); return; }

            string folderPath = PLEditorBridge.I.OpenFolderPanel("Add from CSV Folder",
                RecentPaths.Get(CsvProjectSerializer.CsvFolderKey, Application.dataPath), "");
            if (string.IsNullOrEmpty(folderPath)) { _projectFileSubPanel?.SetStatus("キャンセル"); return; }
            RecentPaths.Set(CsvProjectSerializer.CsvFolderKey, folderPath);

            var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
            if (entries == null || entries.Count == 0) { _projectFileSubPanel?.SetStatus("読み込めるデータがありません"); return; }

            int added = 0, replaced = 0;
            var existingNames = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var mc = model.MeshContextList[i];
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    existingNames[mc.Name] = i;
            }
            foreach (var entry in entries)
            {
                if (entry.MeshContext == null) continue;
                string name = entry.MeshContext.Name ?? "";
                entry.MeshContext.ParentModelContext = model;
                if (existingNames.TryGetValue(name, out int existIdx))
                { model.MeshContextList[existIdx] = entry.MeshContext; replaced++; }
                else
                { model.Add(entry.MeshContext); added++; }
            }
            bool hasNameBased = false;
            foreach (var e in entries) { if (e.IsNameBased) { hasNameBased = true; break; } }
            if (hasNameBased)
            {
                var nameToIndex = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < model.MeshContextList.Count; i++)
                {
                    var mc = model.MeshContextList[i];
                    if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToIndex.ContainsKey(mc.Name))
                        nameToIndex[mc.Name] = i;
                }
                CsvMeshSerializer.ResolveNameReferences(entries, nameToIndex);
            }
            CsvModelSerializer.BuildMirrorPairsFromEntries(entries, model);

            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter を EnterSceneReset(clearScene: true) に集約。
            // MergeCsv は selection を変更しないため、EnterSceneReset 内の SetSelectionState は
            // current selection (first mesh) を再セットする形となる。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
            model.OnListChanged?.Invoke();

            _projectFileSubPanel?.SetStatus($"マージ完了: +{added} /{replaced}置換");
            Debug.Log($"[PlayerViewerCore] MergeCsv: added={added}, replaced={replaced}");
        }

        private void OnPartialImportDone(bool topologyChanged)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter + SetSelectionState を
            // EnterSceneReset(clearScene: true) に集約。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
        }

        private void OnMeshFilterToSkinnedComplete()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            // Phase 2a-2b-2 Batch 3: ClearScene + RebuildAdapter + SetSelectionState +
            // UpdateSelectedDrawableMesh を EnterSceneReset(clearScene: true) に集約。
            _viewportManager.EnterSceneReset(ActiveProject, clearScene: true);
            _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            RebuildModelList();
            NotifyPanels(ChangeKind.ModelSwitch);
        }

        // ================================================================
        // プロジェクト
        // ================================================================

        private ProjectContext ActiveProject => _localLoader.Project ?? _receiver?.Project;

        // ================================================================
        // 外部パネル向け公開API
        // ================================================================

        /// <summary>データ変化通知。Editor専用外部パネル等がサブスクライブする。</summary>
        public Action<ChangeKind> OnChanged;

        /// <summary>現在アクティブな ProjectContext を返す。null の場合あり。</summary>
        public ProjectContext GetActiveProject() => ActiveProject;

        /// <summary>外部からコマンドをディスパッチする。</summary>
        public void Dispatch(PanelCommand cmd) => _commandDispatcher?.Dispatch(cmd);

        // ================================================================
        // ツール切り替え
        // ================================================================

        /// <summary>
        /// カテゴリ 1 (3D 操作と右ペインが一体) のパネルを開く共通ヘルパー。
        /// SetInteractionMode + ShowRightPanel + サブパネル Refresh を一括で行う。
        ///
        /// 【設計ポイント: 右ペイン型ツールでも btn 設定が必要】
        /// InteractionMode ボタンを持つツール (VertexMove 等) は GetButtonForInteractionMode
        /// が btn を返すが、右ペインから起動するツール (EdgeBevel / EdgeExtrude / FaceExtrude
        /// / EdgeTopology / Knife / AddFace) はそちらでは null になる。
        /// このため switch 内で自分の `btn = _layoutRoot?.〇〇Btn;` を明示的に割当てないと
        /// `_activePanelBtn` が null のままとなり、右ペインを開いても当該ボタンが緑
        /// ハイライトされない。ボタンを持つツールを追加するときは、ここにも case を
        /// 追加して btn を設定すること (section / refresh と同列)。
        /// </summary>
        private void ShowCategory1Panel(InteractionMode mode)
        {
            SetInteractionMode(mode);

            VisualElement section = null;
            Button btn = null;
            System.Action refresh = null;

            switch (mode)
            {
                case InteractionMode.VertexMove:
                    section = _layoutRoot?.VertexMoveSection;
                    btn     = _layoutRoot?.ToolVertexMoveBtn;
                    refresh = () => _vertexMoveSubPanel?.Refresh();
                    break;
                case InteractionMode.ObjectMove:
                    section = _layoutRoot?.BoneEditorSection;
                    btn     = _layoutRoot?.ToolObjectMoveBtn;
                    refresh = () => _boneEditorSubPanel?.Refresh();
                    break;
                case InteractionMode.PivotOffset:
                    section = _layoutRoot?.PivotSection;
                    btn     = _layoutRoot?.ToolPivotOffsetBtn;
                    break;
                case InteractionMode.Sculpt:
                    section = _layoutRoot?.SculptSection;
                    btn     = _layoutRoot?.ToolSculptBtn;
                    break;
                case InteractionMode.AdvancedSelect:
                    section = _layoutRoot?.AdvancedSelectSection;
                    btn     = _layoutRoot?.ToolAdvancedSelBtn;
                    refresh = () => _advancedSelectSubPanel?.Refresh();
                    break;
                case InteractionMode.SkinWeightPaint:
                    section = _layoutRoot?.SkinWeightPaintSection;
                    btn     = _layoutRoot?.ToolSkinWeightPaintBtn;
                    break;
                case InteractionMode.AddFace:
                    section = _layoutRoot?.AddFaceSection;
                    // AddFace は右ペインから起動するためツールボタンなし → btn = null
                    refresh = () => _addFaceSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeBevel:
                    section = _layoutRoot?.EdgeBevelSection;
                    btn     = _layoutRoot?.EdgeBevelBtn;
                    refresh = () => _edgeBevelSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeExtrude:
                    section = _layoutRoot?.EdgeExtrudeSection;
                    btn     = _layoutRoot?.EdgeExtrudeBtn;
                    refresh = () => _edgeExtrudeSubPanel?.Refresh();
                    break;
                case InteractionMode.FaceExtrude:
                    section = _layoutRoot?.FaceExtrudeSection;
                    btn     = _layoutRoot?.FaceExtrudeBtn;
                    refresh = () => _faceExtrudeSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeTopology:
                    section = _layoutRoot?.EdgeTopologySection;
                    btn     = _layoutRoot?.EdgeTopologyBtn;
                    refresh = () => _edgeTopologySubPanel?.Refresh();
                    break;
                case InteractionMode.Knife:
                    section = _layoutRoot?.KnifeSection;
                    btn     = _layoutRoot?.KnifeBtn;
                    refresh = () => _knifeSubPanel?.Refresh();
                    break;
                case InteractionMode.FlipFace:
                    section = _layoutRoot?.FlipFaceSection;
                    btn     = _layoutRoot?.FlipFaceBtn;
                    refresh = () => _flipFaceSubPanel?.Refresh();
                    break;
                case InteractionMode.LineExtrude:
                    section = _layoutRoot?.LineExtrudeSection;
                    btn     = _layoutRoot?.LineExtrudeBtn;
                    refresh = () => _lineExtrudeSubPanel?.Refresh();
                    break;
                case InteractionMode.Rotate:
                    section = _layoutRoot?.RotateSection;
                    btn     = _layoutRoot?.RotateBtn;
                    refresh = () => _rotateSubPanel?.Refresh();
                    break;
                case InteractionMode.Scale:
                    section = _layoutRoot?.ScaleSection;
                    btn     = _layoutRoot?.ScaleBtn;
                    refresh = () => _scaleSubPanel?.Refresh();
                    break;
            }

            ShowRightPanel(section, btn);
            refresh?.Invoke();
        }

        // ================================================================
        // SetInteractionMode: 3D 操作モード (ビューポートの入力ハンドラ) のみを切り替える。
        // 右ペイン表示やボタンハイライトには関与しない。
        //
        // カテゴリ 1 (3D 操作と右ペインが一体) → ShowRightPanel と組で呼ぶ
        // カテゴリ 2 (3D 操作を維持) → 呼ばない
        // カテゴリ 3 (3D 操作無効) → SetInteractionMode(None) を呼ぶ
        // ================================================================

        private void SetInteractionMode(InteractionMode mode)
        {
            // 旧モードの後始末 (新モードに関係なく必要な処理)
            if (_interactionMode == InteractionMode.Sculpt && mode != InteractionMode.Sculpt)
                _activePanel?.HideBrushCircle();

            if (_interactionMode == InteractionMode.AdvancedSelect && mode != InteractionMode.AdvancedSelect)
                _activePanel?.HideAdvSelPreview();

            if (_interactionMode == InteractionMode.SkinWeightPaint && mode != InteractionMode.SkinWeightPaint)
            {
                _skinWeightPaintHandler?.OnDeactivate();
                SkinWeightPaintTool.ActivePanel = null;
            }

            if (_interactionMode == InteractionMode.AddFace && mode != InteractionMode.AddFace)
            {
                var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
                if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.All;
            }

            // EdgeTopology から脱出するとき、Selection.Mode を All に戻す。
            // Flip/Dissolve は Edge、Split は Vertex に絞っているため、戻さないと
            // 次のモード (VertexMove 等) でホバー範囲が狭いままになる。
            if (_interactionMode == InteractionMode.EdgeTopology && mode != InteractionMode.EdgeTopology)
            {
                var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
                if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.All;
            }

            // Knife から脱出するとき、Selection.Mode を All に戻す。
            // ナイフは段に応じて Vertex/Edge に絞っているため、戻さないと
            // 次のモードでホバー範囲が絞られたままになる。
            if (_interactionMode == InteractionMode.Knife && mode != InteractionMode.Knife)
            {
                var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
                if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.All;
            }

            // ---------------------------------------------------------------
            // カテゴリ 1 ツール (EdgeBevel / EdgeExtrude / FaceExtrude /
            // FlipFace / LineExtrude / Rotate / Scale) は MoveToolHandler の
            // 共通選択ロジックを流用している。
            // - EdgeBevel / EdgeExtrude / FaceExtrude はフック (OnDragStartExtra 等) に
            //   ツール固有ドラッグ動作を差し込む
            // - FlipFace / LineExtrude / Rotate / Scale は選択のみフック不要
            //   (ツール動作はサブパネル経由。将来 Rotate/Scale 用ギズモを追加する場合は
            //    フック利用に移行予定)
            // 脱出時は:
            //   - 全フックを null に戻す (次モードで古いフックが発火しないように)
            //   - Selection.Mode を All に復元 (次モードのホバー範囲が絞られたままに
            //     ならないように。Rotate/Scale は絞っていないので実質空振りだが、
            //     一律復元として統一)
            // ---------------------------------------------------------------
            bool leavingSharedSelectionTools =
                   (_interactionMode == InteractionMode.EdgeBevel   && mode != InteractionMode.EdgeBevel)
                || (_interactionMode == InteractionMode.EdgeExtrude && mode != InteractionMode.EdgeExtrude)
                || (_interactionMode == InteractionMode.FaceExtrude && mode != InteractionMode.FaceExtrude)
                || (_interactionMode == InteractionMode.FlipFace    && mode != InteractionMode.FlipFace)
                || (_interactionMode == InteractionMode.LineExtrude && mode != InteractionMode.LineExtrude)
                || (_interactionMode == InteractionMode.Rotate      && mode != InteractionMode.Rotate)
                || (_interactionMode == InteractionMode.Scale       && mode != InteractionMode.Scale);
            if (leavingSharedSelectionTools)
            {
                if (_moveToolHandler != null)
                {
                    _moveToolHandler.OnLeftClickExtra   = null;
                    _moveToolHandler.OnDragStartExtra   = null;
                    _moveToolHandler.OnToolDragExtra    = null;
                    _moveToolHandler.OnToolDragEndExtra = null;
                }
                var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
                if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.All;
            }

            _interactionMode = mode;

            // SelectOnly は毎回リセットし、下の SelectOnly case でのみ再有効化する
            // （他モードへ移ったら選択専用を確実に解除）。将来ギズモ用フックも同様にリセット。
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SelectOnly           = false;
                _moveToolHandler.SuppressBuiltinGizmo = false;
                _moveToolHandler.GizmoHitTestOverride = null;
            }

            // 新モードの ToolHandler 割当 + ホバーコールバック登録
            switch (mode)
            {
                case InteractionMode.None:
                    // カテゴリ 3: 3D 操作無効 (ビュー回転/パン/ズームのみ)
                    _vertexInteractor?.SetToolHandler(null);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.SelectOnly:
                    // 選択専用: MoveToolHandler の選択/矩形/投げ縄のみ有効化し、移動ギズモ/頂点移動は無効。
                    if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.VertexMove:
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.ObjectMove:
                    _vertexInteractor?.SetToolHandler(_objectMoveHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _objectMoveHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.PivotOffset:
                    _vertexInteractor?.SetToolHandler(_pivotOffsetHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _pivotOffsetHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Sculpt:
                    _vertexInteractor?.SetToolHandler(_sculptHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _sculptHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.AdvancedSelect:
                    _vertexInteractor?.SetToolHandler(_advancedSelectHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _advancedSelectHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.AddFace:
                    _vertexInteractor?.SetToolHandler(_addFaceHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _addFaceHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.EdgeBevel:
                    // MoveToolHandler の選択/矩形選択を流用。
                    // ドラッグ開始フックで EdgeBevel の開始、継続ドラッグで幅調整、
                    // ドラッグ終了で確定 + Undo 記録を行う。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeBevelHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Edge ヒットのみベベル発火。要素なし or 型違いは通常の矩形選択等に任せる
                        if (elem.Kind != PlayerHoverKind.Edge) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと _mouseDownScreenPos が
                        // 画面隅になり量がマウス移動と連動しない）。Handler 側で ToImgui される。
                        _edgeBevelHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _edgeBevelHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _edgeBevelHandler?.OnLeftDragEnd(pos, mods);
                    ApplySelectionModeForInteractionMode(InteractionMode.EdgeBevel);
                    break;
                case InteractionMode.EdgeExtrude:
                    // MoveToolHandler の選択/矩形選択を流用。ドラッグ系ツール (EdgeBevel と同パターン)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeExtrudeHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Edge / Line（2点面）ヒットで押し出し発火。要素なし or 型違いは通常の矩形選択等に任せる
                        if (elem.Kind != PlayerHoverKind.Edge && elem.Kind != PlayerHoverKind.Line) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと画面隅基準になり非連動）。
                        _edgeExtrudeHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _edgeExtrudeHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _edgeExtrudeHandler?.OnLeftDragEnd(pos, mods);
                    ApplySelectionModeForInteractionMode(InteractionMode.EdgeExtrude);
                    break;
                case InteractionMode.FaceExtrude:
                    // MoveToolHandler の選択/矩形選択を流用。ドラッグ系ツール (EdgeBevel と同パターン)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _faceExtrudeHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Face ヒットのみ押し出し発火
                        if (elem.Kind != PlayerHoverKind.Face) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと画面隅基準になり非連動）。
                        _faceExtrudeHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _faceExtrudeHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _faceExtrudeHandler?.OnLeftDragEnd(pos, mods);
                    ApplySelectionModeForInteractionMode(InteractionMode.FaceExtrude);
                    break;
                case InteractionMode.FlipFace:
                    // ビューポートでは選択のみ (面の単独選択 / Shift 追加 / 矩形選択)。
                    // 面反転自体はサブパネル経由で実行 (本セッション対象外、別件で修正)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    ApplySelectionModeForInteractionMode(InteractionMode.FlipFace);
                    break;
                case InteractionMode.LineExtrude:
                    // ビューポートでは選択のみ (ラインの単独選択 / Shift 追加 / 矩形選択)。
                    // 押し出し実行はサブパネル経由 (本セッション対象外、別件で修正)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    ApplySelectionModeForInteractionMode(InteractionMode.LineExtrude);
                    break;
                case InteractionMode.Rotate:
                    // ビューポート・回転リングギズモ: 選択は MoveToolHandler を維持し、
                    // 組み込み移動ギズモを抑制、フック経由で RotateToolHandler のリングへ委譲。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _rotateHandler != null && _rotateHandler.GizmoHitTest(pos, c);
                        // hover残留対策（頂点移動の EnterTransformDragging 修正と同系）:
                        // このギズモドラッグは MoveToolHandler の ToolDragging 経路で処理され、
                        // 組み込み軸ギズモ経路の OnEnterTransformDragging を通らないため、
                        // 従来はドラッグ中も Normal モードのまま毎フレーム GPU ヒットテストが
                        // 走り hover ハイライトがカーソルに追従していた。ここで DragBegin/DragEnd
                        // を明示発火し TransformDragging に入れることで hover を凍結＋開始時クリアする。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            if (_rotateHandler == null || !_rotateHandler.BeginGizmoDrag()) return false;
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _rotateHandler?.GizmoDrag(pos);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) =>
                        {
                            _rotateHandler?.EndGizmoDrag();
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        };
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _rotateHandler?.UpdateHover(pos, ctx));
                    ApplySelectionModeForInteractionMode(InteractionMode.Rotate);
                    break;
                case InteractionMode.Scale:
                    // ビューポート・スケールギズモ: 選択は MoveToolHandler を維持し、
                    // 組み込み移動ギズモを抑制、フック経由で ScaleToolHandler のギズモへ委譲。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _scaleHandler != null && _scaleHandler.GizmoHitTest(pos, c);
                        // hover残留対策（Rotate と同系。詳細は Rotate ケースのコメント参照）:
                        // ToolDragging 経路で TransformDragging に入らず hover が追従するため、
                        // DragBegin/DragEnd を明示発火して hover を凍結＋開始時クリアする。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            if (_scaleHandler == null || !_scaleHandler.BeginGizmoDrag()) return false;
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _scaleHandler?.GizmoDrag(pos);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) =>
                        {
                            _scaleHandler?.EndGizmoDrag();
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        };
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _scaleHandler?.UpdateHover(pos, ctx));
                    ApplySelectionModeForInteractionMode(InteractionMode.Scale);
                    break;
                case InteractionMode.EdgeTopology:
                    _vertexInteractor?.SetToolHandler(_edgeTopologyHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeTopologyHandler?.UpdateHover(pos, ctx));
                    // Split → Vertex ホバーのみ、Flip/Dissolve → Edge ホバーのみ
                    ApplySelectionModeForEdgeTopology(
                        _edgeTopologyHandler?.ModePublic ?? Poly_Ling.Tools.EdgeTopoMode.Flip);
                    break;
                case InteractionMode.Knife:
                    _vertexInteractor?.SetToolHandler(_knifeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _knifeHandler?.UpdateHover(pos, ctx));
                    _knifeHandler?.ApplyHoverSelectionMode();   // 初期段（開始頂点）＝ Vertex ホバー
                    break;
                case InteractionMode.SkinWeightPaint:
                    _vertexInteractor?.SetToolHandler(_skinWeightPaintHandler);
                    SkinWeightPaintTool.ActivePanel = _skinWeightPaintPanel;
                    _skinWeightPaintPanel?.RefreshMeshList(ActiveProject?.CurrentModel);
                    _skinWeightPaintPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    _skinWeightPaintHandler?.OnActivate();
                    // UndoController に対象 MeshObject を設定（スナップショット取得のため必須）
                    SyncSkinWeightUndoMesh();
                    break;
            }

            // InteractionMode ボタンのハイライト (2 系統色の片方)
            UpdateInteractionButtonHighlight();
        }

        /// <summary>
        /// EdgeTopology のサブモード (Flip/Split/Dissolve) に応じてホバー有効範囲
        /// (Selection.Mode) を切り替える。Split は頂点クリックで対角を指定、
        /// Flip/Dissolve は辺クリックで実行するため、それぞれ不要な要素のホバーを
        /// 抑制してユーザ体験を明確にする。
        /// </summary>
        /// <summary>
        /// EdgeTopology のサブモード (Flip/Split/Dissolve) に応じてホバー有効範囲
        /// (Selection.Mode) を切り替える。Split は頂点クリックで対角を指定、
        /// Flip/Dissolve は辺クリックで実行するため、それぞれ不要な要素のホバーを
        /// 抑制してユーザ体験を明確にする。
        ///
        /// 【設計ポイント: サブモード別のホバー絞り込みパターン】
        /// 1 ツールの中でクリック対象が頂点/辺/面と切り替わるとき、
        /// GPU ホバー全種類を流したままにするとユーザが混乱する。
        /// ツール進入時と内部サブモード切替時の両方で Selection.Mode を絞り、
        /// ツール脱出時 (SetInteractionMode の旧モード判定) で MeshSelectMode.All に
        /// 戻すのが安全なパターン。AddFace が Vertex 絞り + 脱出時 All 復元を先例として
        /// 既に実装しており、ここもそれを踏襲している (SetInteractionMode の脱出処理を参照)。
        /// </summary>
        private void ApplySelectionModeForEdgeTopology(Poly_Ling.Tools.EdgeTopoMode mode)
        {
            var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
            if (firstMc == null) return;
            firstMc.Selection.Mode = (mode == Poly_Ling.Tools.EdgeTopoMode.Split)
                ? MeshSelectMode.Vertex
                : MeshSelectMode.Edge;
        }

        /// <summary>
        /// カテゴリ 1 ツール進入時にホバー有効範囲 (Selection.Mode) を絞る。
        /// MoveToolHandler は Selection.Mode を尊重する設計になっているため、
        /// ここで絞るだけで GPU ホバーの要素種・クリック選択・矩形選択いずれも
        /// 対象要素タイプだけに応答するようになる。
        /// 脱出時 (SetInteractionMode の旧モード判定) で MeshSelectMode.All に
        /// 戻すこと (既に脱出処理側で実装済)。
        /// </summary>
        private void ApplySelectionModeForInteractionMode(InteractionMode mode)
        {
            var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
            if (firstMc == null) return;
            switch (mode)
            {
                case InteractionMode.EdgeBevel:    firstMc.Selection.Mode = MeshSelectMode.Edge; break;
                case InteractionMode.EdgeExtrude:  firstMc.Selection.Mode = MeshSelectMode.Edge | MeshSelectMode.Line; break;
                case InteractionMode.FaceExtrude:  firstMc.Selection.Mode = MeshSelectMode.Face; break;
                case InteractionMode.FlipFace:     firstMc.Selection.Mode = MeshSelectMode.Face; break;
                case InteractionMode.LineExtrude:  firstMc.Selection.Mode = MeshSelectMode.Line; break;
                default:
                    // 他のモードはこの関数の責務ではない
                    break;
            }
        }

        // ================================================================
        // モデル切り替え
        // ================================================================

        private void SwitchActiveModel(int index)
        {
            var project = ActiveProject;
            if (project == null) return;
            // 範囲外なら何もしない
            if (index < 0 || index >= project.ModelCount) return;
            if (project.CurrentModelIndex == index) return;

            // 問題: 従来ここで project.SelectModel() + EnterSceneReset を直接行い
            // Undo 記録を伴わない経路だった。SwitchModelCommand ハンドラに統一して
            // Undo 記録 (RecordModelSwitch) + SetModelContext 同期を経由させる。
            _commandDispatcher?.Dispatch(new SwitchModelCommand(index));

            var model = project.CurrentModel;
            if (model == null) return;

            // ツールハンドラへの Project 参照更新 (SwitchModelCommand ハンドラでは扱わない分)。
            _moveToolHandler?.SetProject(project);
            _objectMoveHandler?.SetProject(project);
            _pivotOffsetHandler?.SetProject(project);
            _sculptHandler?.SetProject(project);
            _advancedSelectHandler?.SetProject(project);
            _skinWeightPaintHandler?.SetProject(project);

            _skinWeightPaintPanel?.RefreshMeshList(model);
            _skinWeightPaintPanel?.RefreshBoneList(model);
        }

        // ================================================================
        // SyncUI / RebuildModelList
        // ================================================================

        /// <summary>
        /// ★★★ 【重大規約違反コード】 ★★★
        /// 旧 Tick から毎フレーム呼ばれる UI 同期処理。
        /// 各値（Status, 接続状態, Undo/Redo 可否等）はイベント駆動で更新すべき。
        /// Phase 5: モデル変更・選択変更・接続状態変更・Undo スタック変更等の
        /// 各イベント購読に分解して置き換える予定。
        /// 新規コードからこの関数を呼ぶことは厳禁。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        private void SyncUI()
        {
            if (_layoutRoot == null) return;

            _layoutRoot.StatusLabel.text = $"Status: {_status}";

            bool clientExists = _remoteMode == RemoteMode.Client && _client != null;
            bool serverActive = _remoteMode == RemoteMode.Server && _playerServer != null;
            bool isConnected  = clientExists && _client.IsConnected;

            _layoutRoot.RemoteSection.style.display =
                (clientExists || serverActive) ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.ConnectBtn   .style.display = isConnected ? DisplayStyle.None : DisplayStyle.Flex;
            _layoutRoot.DisconnectBtn.style.display = isConnected ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.FetchBtn.SetEnabled(isConnected);
            _layoutRoot.UndoBtn.SetEnabled(_editOps?.CanUndo ?? false);
            _layoutRoot.RedoBtn.SetEnabled(_editOps?.CanRedo ?? false);
        }

        /// <summary>
        /// リモートモード（インスペクタ設定）に応じて左ペインの表示を出し分ける。
        /// Client 時のみ「サーバと連携」、Server 時のみ「リモートサーバ」を表示。
        /// _remoteMode はセッション中不変のため初期化時に一度だけ適用する。
        /// </summary>
        private void ApplyRemoteModeVisibility()
        {
            if (_layoutRoot == null) return;
            bool isClient = _remoteMode == RemoteMode.Client;
            bool isServer = _remoteMode == RemoteMode.Server;

            if (_layoutRoot.RemoteFoldout != null)
                _layoutRoot.RemoteFoldout.style.display = isClient ? DisplayStyle.Flex : DisplayStyle.None;
            if (_layoutRoot.RemoteServerBtn != null)
                _layoutRoot.RemoteServerBtn.style.display = isServer ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RebuildModelList()
        {
            if (_layoutRoot?.ModelListContainer == null) return;
            _layoutRoot.ModelListContainer.Clear();

            var project = ActiveProject;
            var m = project?.CurrentModel;
            if (m != null)
            {
                var lbl = new Label($"{m.Name}  ({m.Count})");
                lbl.style.color = new StyleColor(Color.white);
                _layoutRoot.ModelListContainer.Add(lbl);
            }

            // ドロップダウン更新
            if (_layoutRoot.ModelSelectDropdown != null && project != null)
            {
                var choices = new List<string>();
                for (int i = 0; i < project.ModelCount; i++)
                {
                    var mdl = project.GetModel(i);
                    choices.Add(mdl?.Name ?? $"Model {i}");
                }
                _layoutRoot.ModelSelectDropdown.choices = choices;
                int cur = project.CurrentModelIndex;
                string curVal = (cur >= 0 && cur < choices.Count) ? choices[cur] : (choices.Count > 0 ? choices[0] : "");
                _layoutRoot.ModelSelectDropdown.SetValueWithoutNotify(curVal);
            }

            NotifyPanels(ChangeKind.ListStructure);
        }

        // ================================================================
        // コマンドディスパッチ / パネル通知
        // ================================================================

        private void DispatchPanelCommand(PanelCommand cmd)
        {
            _commandDispatcher?.Dispatch(cmd);
        }

        /// <summary>
        /// SkinWeightPaint 用 UndoController に現在のターゲットメッシュを設定する。
        /// ツール切り替え時・メッシュドロップダウン変更時に呼ぶ。
        /// </summary>
        private void SyncSkinWeightUndoMesh()
        {
            var swModel = ActiveProject?.CurrentModel;
            if (swModel == null || _editOps?.UndoController == null) return;

            // パネルで選択されたメッシュを優先、なければ FirstDrawableMeshContext
            int targetMasterIdx = _skinWeightPaintPanel?.CurrentTargetMesh ?? -1;
            MeshContext swMc = targetMasterIdx >= 0
                ? swModel.GetMeshContext(targetMasterIdx)
                : swModel.FirstDrawableMeshContext;

            if (swMc?.MeshObject == null) return;
            _editOps.UndoController.SetMeshObject(swMc.MeshObject, swMc.UnityMesh);
            _editOps.UndoController.MeshUndoContext.ParentModelContext = swModel;
            _skinWeightUndoMasterIndex = swModel.IndexOf(swMc);
        }

        /// <summary>
        /// src の頂点データ（Position・Normal・BoneWeight 等）を dst に上書きコピーする。
        /// 頂点数が一致する場合のみ実行。スキンウェイト Undo 書き戻し用。
        /// </summary>
        private static void CopyMeshObjectVertexData(Poly_Ling.Data.MeshObject src, Poly_Ling.Data.MeshObject dst)
        {
            if (src == null || dst == null) return;
            if (src.VertexCount != dst.VertexCount) return;
            for (int i = 0; i < src.VertexCount; i++)
            {
                var sv = src.Vertices[i];
                var dv = dst.Vertices[i];
                dv.Position   = sv.Position;
                dv.BoneWeight = sv.BoneWeight;
                // UV スロットを同期（UV 編集の Undo に必要）
                dv.UVs.Clear();
                foreach (var uv in sv.UVs)
                    dv.UVs.Add(uv);
            }
            dst.InvalidatePositionCache();
        }

        /// <summary>
        /// 現在アクティブなツールに応じたホバー対象種別を返す。
        /// Phase 2b-1 暫定実装: ホバーを完全に抑制すべきツール（SkinWeightPaint / Sculpt 等）では
        /// None を、それ以外では Vertex を返す（Vertex は「ホバー有効」の仮値。現状の入口側は
        /// None 判定のみで分岐するため、既存の GPU ホバー優先度 (頂点>辺>面) がそのまま動作する）。
        /// Phase 2b 以降で Edge / Face / Bone / Gizmo の厳密な kind 分岐を実装する。
        /// </summary>
        private HoverTargetKind GetCurrentHoverTargetKind()
        {
            switch (_interactionMode)
            {
                case InteractionMode.SkinWeightPaint:
                case InteractionMode.Sculpt:
                    return HoverTargetKind.None;
                default:
                    return HoverTargetKind.Vertex;
            }
        }

        private void NotifyPanels(ChangeKind kind)
        {
            var project = ActiveProject;
            if (project == null || _panelContext == null) return;
            var view = new PlayerProjectView(project);
            _panelContext.Notify(view, kind);

            if (_interactionMode == InteractionMode.ObjectMove)
                _boneEditorSubPanel?.Refresh();

            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                _blendSubPanel?.OnSelectionChanged();

            foreach (var (section, refresh) in _sectionRefreshPairs)
                if (section?.style.display == DisplayStyle.Flex) refresh();

            if (_layoutRoot?.ModelBlendSection != null &&
                _layoutRoot.ModelBlendSection.style.display == DisplayStyle.Flex)
                _modelBlendSubPanel?.OnViewChanged(view, kind);

            // Phase 1 event 配線: 選択・トポロジ・モデル切替・属性変更などに
            // 追随して描画準備を再実行する（OnRenderObject 経路の Submit 用データを更新）。
            // Phase 2a-2f: 自己定義 PresentAll() を正規入口 EnterTopologyChanged に置換。
            // kind によらず包括的にバッファ再構築 + 描画キュー準備 + overlay refresh を行う。
            _viewportManager.EnterTopologyChanged(ActiveProject);

            OnChanged?.Invoke(kind);
        }

        // ================================================================
        // クライアントイベント
        // ================================================================

        private void OnConnected()    { _status = "接続済み"; }
        private void OnDisconnected() { _status = "切断"; }

        private void OnPushReceived(string json)
        {
            if (json.Contains("\"event\":\"mesh_changed\"") ||
                json.Contains("\"event\":\"model_changed\""))
                FetchProject();
        }

        /// <summary>
        /// サーバから push された PositionsOnly を、クライアントの選択メッシュへ適用する（S→C 連動）。
        /// メインスレッドで呼ばれる。連動は選択メッシュ1つに限定（PositionsOnly は index を持たない）。
        /// </summary>
        private void ApplyRemotePositions(byte[] data)
        {
            if (data == null) return;
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null || header.Value.MessageType != BinaryMessageType.PositionsOnly) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.FirstDrawableMeshContext;
            if (mc?.MeshObject == null) return;

            RemoteBinarySerializer.Deserialize(data, mc.MeshObject);
            _viewportManager.SyncMeshPositionsAndTransform(mc, model);
            _viewportManager.UpdateTransform();
        }

        // ================================================================
        // 受信イベント
        // ================================================================

        private void OnProjectHeaderReceived(ProjectContext project)
        {
            if (_fetchFlow != null) _fetchFlow.ModelCount = project.ModelCount;
            // Phase 2a-2g-3: ヘッダ受信時の即時シーンクリア。軽量操作として据え置き。
            // EnterSceneReset で置換すると RebuildAdapter + PresentAll まで走って過剰。
            // フェッチ完了時に PlayerRemoteFetchFlow.FetchAllModelsBatch 末尾で
            // EnterSceneReset(clearScene: true) が呼ばれる (設計 Z)。
            #pragma warning disable CS0618
            _viewportManager.ClearScene();
            #pragma warning restore CS0618
            RebuildModelList();
        }

        private void OnModelMetaReceived(int mi, ModelContext model) { }
        private void OnMeshSummaryReceived(int mi, int si, MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, MeshContext mc)
        {
            if (_receiver?.Project == null) return;
            if (mi == 0 && si == 0 && mc.UnityMesh != null)
                // Phase 2a-2d: ResetToMesh → EnterCameraChanged(Reset) に集約。
                _viewportManager.EnterCameraChanged(
                    _viewportManager.PerspectiveViewport,
                    CameraChangePhase.Reset,
                    mc.UnityMesh.bounds);
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _alignVerticesHandler?.SetProject(ActiveProject);
            _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);
            // 受信中はフル GPU 再構築を抑止（完了時 EnterSceneReset で1回だけ行う）。
            if (!_suppressRebuildDuringFetch)
            {
                RebuildModelList();
                NotifyPanels(ChangeKind.ListStructure);
            }
        }

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

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            _fetchFlow?.FetchProject();
        }
    }
}
