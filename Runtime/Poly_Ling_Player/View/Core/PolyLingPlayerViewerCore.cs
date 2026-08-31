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
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

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
        private PlayerShortcutController       _shortcutController;

        private readonly PlayerViewportManager _viewportManager = new PlayerViewportManager();
        private PlayerLayoutRoot               _layoutRoot;

        // 左ペインの「法線自動計算」トグルを選択状態から書き戻す間だけ true。
        // SetValueWithoutNotify を使ってもコールバックが走る経路を作らないための保険。
        private bool _isSyncingNormalRecalcToggle;

        // 左ペインの「再計算」ボタンが使うスムージング角。
        // 法線編集パネル（PlayerNormalEditSubPanel）の既定値と同じ値にしてある。
        private const float NormalRecalcDefaultAngleDeg = 59.5f;
        private PlayerImportSubPanel           _importSubPanel;
        private PlayerExportSubPanel           _exportSubPanel;
        // プロジェクト保存 / 読込。押し間違い防止のため別パネル・別セクションに分ける。
        private PlayerProjectFileSubPanel      _projectSaveSubPanel;
        private PlayerProjectFileSubPanel      _projectLoadSubPanel;
        private PlayerPartialImportSubPanel    _partialImportSubPanel;
        private PlayerPartialExportSubPanel    _partialExportSubPanel;
        private PlayerPrimitiveMeshSubPanel    _primitiveSubPanel;
        // 検証用の2つ目のインスタンス。既存 _primitiveSubPanel とは状態を共有しない。
        private PlayerPrimitiveMeshSubPanel    _livePrimitiveSubPanel;
        private MeshFilterToSkinnedSubPanel    _mfToSkinnedSubPanel;
        private PlayerSkinKindSubPanel         _skinKindSubPanel;
        private PanelContext                   _panelContext;
        private ModelListSubPanel              _modelListSubPanel;
        private MeshListSubPanel               _meshListSubPanel;

        // 頂点インタラクション（Perspective ビューポート専用）
        private SelectionState         _selectionState;
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private enum InteractionMode { None, VertexMove, ObjectMove, PivotOffset, Sculpt, AdvancedSelect, SkinWeightPaint, SkinWeightNumeric, AddFace, EdgeBevel, EdgeExtrude, FaceExtrude, EdgeTopology, Knife, FlipFace, Solidify, Rotate, Scale, SelectOnly, PrimitivePlace, WorkAxis, Deform, Lattice, DeleteFace, VertexDissolve, Tri4To1, FaceMerge, Quad4To1, FaceMergeCollapse, Camera }
        private InteractionMode               _interactionMode = InteractionMode.VertexMove;

        // パネルごとの「ビューポートで選択する」チェックの保存キー。
        // 他パネルへ広げるときはここへキーを足し、Build 直後に
        // AttachPanelSelectToggle、Show～Panel を ShowRightPanelSelectable にする。
        // 面追加ツールで非選択オブジェクトの頂点へ吸着したときのプレビュー色。
        // 選択メッシュへの吸着（シアン）と区別するためのマゼンタ。
        private static readonly Color AddFaceUnselectedSnapColor = new Color(1f, 0.35f, 0.9f, 0.95f);

        private const string PanelSelectKeyMeshList    = "MeshList";
        private const string PanelSelectKeyVertexHole  = "VertexHole";
        private const string PanelSelectKeyPrimitive   = "Primitive";
        // PMX/MQO はインポータ・エクスポータそれぞれで1キーを共有する（モード切替で状態を分けない）。
        private const string PanelSelectKeyImport      = "Import";
        private const string PanelSelectKeyExport      = "Export";
        private const string PanelSelectKeyProjectSave = "ProjectSave";
        private const string PanelSelectKeyProjectLoad = "ProjectLoad";

        // ================================================================
        // 一時選択サブツール (ショートカット R = 矩形 / G = 投げ縄。左ペインのボタンも同じ)
        //   進入: 現在の InteractionMode と DragSelectMode を退避し、SelectOnly
        //         (要素ヒットでも常に矩形/投げ縄へ入るモード) へ切り替える。
        //   復帰: 矩形/投げ縄の 1 回の確定、ドラッグに至らないクリック、または Escape。
        // ================================================================
        private bool                                    _subToolActive;
        private InteractionMode                         _subToolPrevMode;
        private MoveToolHandler.SelectionDragMode       _subToolPrevMoveDragMode;
        private ObjectMoveToolHandler.SelectionDragMode _subToolPrevObjectDragMode;
        // 選択モードの退避は持たない。SetInteractionMode が新モードごとに
        // ツール固有 override を決め直すため、復帰時も自動で正しい値になる。

        // ================================================================
        // 面削除モード (ショートカット D。左ペインの「面削除モード」ボタンも同じ)
        //   進入: 現在の InteractionMode を退避し、DeleteFace へ切り替える。
        //   動作: 面のクリックのみ受け付け、クリックされた面を即削除する。
        //         矩形/投げ縄選択と面以外のホバーは無効。
        //   復帰: Escape、または他ツールの選択。
        // ================================================================
        private bool            _deleteFaceModeActive;
        private InteractionMode _deleteFacePrevMode;

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
        private PrimitivePlaceToolHandler    _primitivePlaceHandler;
        // 作業用ローカル軸サブツール。モデルには触れず ModelContext.WorkAxis だけを操作する。
        private WorkAxisToolHandler          _workAxisHandler;
        private PlayerWorkAxisSubPanel       _workAxisSubPanel;
        // 変形パネルの先頭へ埋め込むぶん。左ペインの作業軸ツールとは別インスタンス。
        private PlayerWorkAxisSubPanel       _deformWorkAxisSubPanel;
        // カメラ調整。メインカメラ / 3面カメラのパラメータだけを読み書きし、モデルには触れない。
        private CameraToolHandler            _cameraHandler;
        private PlayerCameraSubPanel         _cameraSubPanel;
        // 3面フリップの適用処理（ビューポートヘッダのボタンとカメラ調整パネルで共有）。
        private System.Action<bool>          _setTopFlip;
        private System.Action<bool>          _setFrontFlip;
        private System.Action<bool>          _setSideFlip;
        // デフォーマ（回転 / 曲げ）。基準は ModelContext.WorkAxis を作業軸パネルと共有する。
        private DeformToolHandler            _deformHandler;
        private PlayerDeformSubPanel         _deformSubPanel;
        private LatticeToolHandler           _latticeHandler;
        private PlayerLatticeSubPanel        _latticeSubPanel;
        private SculptToolHandler            _sculptHandler;
        // ツール内「一時ミラー」の状態。所有権を持つのはこの 1 インスタンスだけで、
        // どのツールが実体化したか (OwnerToken = (int)InteractionMode) を覚える。
        private TempMirrorController         _tempMirrorController;
        private AdvancedSelectToolHandler    _advancedSelectHandler;
        // 接続モードのクリック点フラッシュ強調（頂点インデックス。-1=非表示）。
        private int                          _advSelFlashVertex = -1;
        private int                          _advSelFlashGen    = 0;
        // 辺クリックのフラッシュ強調（辺。null=非表示。頂点フラッシュより優先）。
        private Poly_Ling.Selection.VertexPair? _advSelFlashEdge;
        private SkinWeightPaintToolHandler   _skinWeightPaintHandler;
        private PlayerSkinWeightPaintPanel   _skinWeightPaintPanel;
        private PlayerSkinWeightNumericSubPanel _skinWeightNumericSubPanel;
        private int                          _skinWeightUndoMasterIndex = -1;
        private int                          _uvUndoMasterIndex         = -1;
        private PlayerBlendSubPanel          _blendSubPanel;
        private PlayerShrinkSubPanel         _shrinkSubPanel;
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
        private PlayerNormalExcludeSetSubPanel  _normalExcludeSubPanel;
        private PlayerNormalEditSubPanel        _normalEditSubPanel;
        private PlayerNormalTransplantSubPanel  _normalTransplantSubPanel;
        private PlayerFaceHideSubPanel          _faceHideSubPanel;
        private PlayerMeshSelectionSetSubPanel  _meshSelSetSubPanel;
        private PlayerMergeMeshesSubPanel    _mergeMeshesSubPanel;
        private PlayerBooleanSubPanel        _booleanSubPanel;
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
        private PlayerSmoothEdgesSubPanel         _smoothEdgesSubPanel;
        private SmoothEdgesToolHandler            _smoothEdgesHandler;
        private PlayerMergeVerticesSubPanel       _mergeVerticesSubPanel;
        private MergeVerticesToolHandler          _mergeVerticesHandler;
        private PlayerSplitVerticesSubPanel       _splitVerticesSubPanel;
        private PlayerVertexHoleSubPanel          _vertexHoleSubPanel;
        private PlayerVertexDissolveSubPanel      _vertexDissolveSubPanel;
        private PlayerTri4To1SubPanel             _tri4To1SubPanel;
        private PlayerFaceMergeSubPanel           _faceMergeSubPanel;
        private PlayerQuad4To1SubPanel            _quad4To1SubPanel;
        private PlayerFaceMergeCollapseSubPanel   _faceMergeCollapseSubPanel;
        // 頂点IDユーティリティ。ID を使う突き合わせ操作の前段で状態を確認・修復する。
        private PlayerVertexIdSubPanel           _vertexIdSubPanel;
        // モデル間頂点データ転送。メッシュのペアを明示して 1 対 1 で転送する。
        private PlayerVertexTransferSubPanel     _vertexTransferSubPanel;
        private SplitVerticesToolHandler          _splitVerticesHandler;
        private VertexHoleToolHandler             _vertexHoleHandler;
        private VertexDissolveToolHandler         _vertexDissolveHandler;
        private Tri4To1ToolHandler                _tri4To1Handler;
        private FaceMergeToolHandler              _faceMergeHandler;
        private Quad4To1ToolHandler               _quad4To1Handler;
        private FaceMergeCollapseToolHandler      _faceMergeCollapseHandler;
        // 選択削除サブツール。専用サブパネルは持たない (左ペインのボタンと D キーのみ)。
        private DeleteSelectionToolHandler        _deleteSelectionHandler;
        private PlayerAddFaceSubPanel             _addFaceSubPanel;
        private AddFaceToolHandler                _addFaceHandler;
        // ================================================================
        // 選択モード（頂点/辺/面/線分）の単一権限
        //
        // 【なぜ一箇所に集約するか】
        // SelectionState は経路ごとに別インスタンスへ差し替わり、Mode の既定値は
        // Vertex|Edge|Face|Line。書き込み口が分散していると、ツール脱出・Undo・
        // モデルロード・メッシュ選択のたびに値が巻き戻り、チェックボックスの指定が
        // 効かなくなる。書き込みは ApplySelectMode() だけが行う。
        //
        // 【実効値】_toolSelectModeOverride ?? _userSelectMode
        //   _userSelectMode         … 左ペインのチェックボックス（永続化対象）
        //   _toolSelectModeOverride … ツール固有の絞り込み。null でユーザ指定に従う。
        //                             例: 面追加は常に頂点のみ（チェックボックス無関係）
        // ================================================================
        private MeshSelectMode  _userSelectMode = MeshSelectMode.Vertex;
        private MeshSelectMode? _toolSelectModeOverride;
        // 直近に適用した実効モード。変化した時だけ「無効になった種別の選択」を解除する。
        // 毎回解除すると、モード外の種別を意図的に選ぶツール（高度選択の面/辺同時選択等）
        // の結果まで消えてしまう。
        private MeshSelectMode? _lastAppliedSelectMode;
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
        private PlayerSolidifySubPanel            _solidifySubPanel;
        private SolidifyToolHandler               _solidifyHandler;
        private PlayerMediaPipeFaceDeformSubPanel _mediaPipeSubPanel;
        private PlayerVMDTestSubPanel        _vmdTestSubPanel;
        private PlayerPipelineTestSubPanel   _pipelineTestSubPanel;
        private PlayerOriginTestSubPanel     _originTestSubPanel;
        private PlayerSkinTestSubPanel       _skinTestSubPanel;
        private PlayerUnityClipTestSubPanel  _unityClipTestSubPanel;
        private PlayerMotionClipTestSubPanel _motionClipTestSubPanel;

        // 下絵（3D背面に敷く参照画像）
        private readonly UnderlayConfig      _underlay = new UnderlayConfig();
        private PlayerUnderlaySubPanel       _underlaySubPanel;
        private bool                         _underlayActive;  // 下絵パネル表示中＝左ドラッグでオフセット移動

        // 軸 / グリッド平面（4面共通）
        private PlayerGridAxisSubPanel       _gridAxisSubPanel;

        // 画面キャプチャ（PNG 保存）
        private PlayerCaptureSubPanel        _captureSubPanel;

        private PlayerRemoteServerSubPanel   _remoteServerSubPanel;
        private PlayerLogSubPanel            _logSubPanel;
        private PlayerVertexMoveSubPanel     _vertexMoveSubPanel;
        private PlayerPivotSubPanel          _pivotSubPanel;
        private PlayerSculptSubPanel         _sculptSubPanel;
        private PlayerAdvancedSelectSubPanel _advancedSelectSubPanel;

        private PlayerViewportPanel    _activePanel;
        private Vector2                _lastMouseScreenPos;
        private PlayerViewport         _activeViewport;

        private PlayerCommandDispatcher _commandDispatcher;

        // 左ペイン「回転はローカル原点中心」トグルの状態（既定 ON）。
        // ON でも切り替えた時点では視点を動かさない。ComputeOrbitPivot が
        // OrbitCameraController.GetOrbitPivot 経由で軌道回転時にだけ参照される。
        private bool _orbitAroundLocalOrigin = true;

        // 左ペイン「現在の選択を中心に」釦で確定した固定ピボット（ワールド）。
        // スナップショットであり、押した後に選択や頂点が変わっても動かない。
        // null なら未設定。トグルを ON に戻すと解除される。
        private Vector3? _explicitOrbitPivot;

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
                    cmd => _commandDispatcher?.Dispatch(cmd));
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
                _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _vertexHoleHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _solidifyHandler?.SetProject(ActiveProject);
                _deleteSelectionHandler?.SetProject(ActiveProject);
                _vertexDissolveHandler?.SetProject(ActiveProject);
                _tri4To1Handler?.SetProject(ActiveProject);
                _faceMergeHandler?.SetProject(ActiveProject);
                _quad4To1Handler?.SetProject(ActiveProject);
                _faceMergeCollapseHandler?.SetProject(ActiveProject);

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
        // ツールが生成したメッシュの受け口
        // ================================================================

        /// <summary>
        /// ツールが作った MeshContext を現在のモデルへ追加する。
        /// 名前は既存オブジェクトと衝突しないよう一意化し、選択と Undo は
        /// 図形生成の「新しい描画オブジェクト」と同じ扱いにする。
        /// </summary>
        private void AddMeshContextFromTool(MeshContext ctx)
        {
            if (ctx == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            string baseName = !string.IsNullOrEmpty(ctx.Name) ? ctx.Name
                            : (!string.IsNullOrEmpty(ctx.MeshObject?.Name) ? ctx.MeshObject.Name : "Mesh");
            string name = model.GenerateUniqueMeshName(baseName);

            ctx.Name = name;
            if (ctx.MeshObject != null) ctx.MeshObject.Name = name;
            if (ctx.UnityMesh  != null) ctx.UnityMesh.name  = name;
            ctx.ParentModelContext = model;

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

            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// ツールが作った MeshObject を編集対象メッシュへマージする。
        /// 図形生成の「既存の描画オブジェクトに追加」と同じ経路を通す。
        /// </summary>
        private void AddMeshObjectToCurrentMeshFromTool(MeshObject meshObject, string meshName)
        {
            if (meshObject == null) return;
            var project = ActiveProject;
            if (project?.CurrentModel == null) return;

            PrimitiveMeshAddToExisting(
                project, meshObject,
                string.IsNullOrEmpty(meshName) ? "Mesh" : meshName,
                Vector3.zero, Vector3.zero, Vector3.one, false, -1);
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
        }

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

            // ツール内「一時ミラー」。ツール横断で 1 つだけ持ち、
            // 実体化したツールから離れたときに SetInteractionMode が解除する。
            _tempMirrorController = new TempMirrorController
            {
                GetProject  = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };

            _advancedSelectHandler = new AdvancedSelectToolHandler();
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
                    else if (_interactionMode == InteractionMode.AddFace)
                    {
                        if (_addFaceHandler != null && _addFaceHandler.FinishAsTriangle())
                            _addFaceSubPanel?.Refresh();
                    }
                    // 格子変形は進行中のセッションを取消して開始前へ戻す。
                    else if (_interactionMode == InteractionMode.Lattice)
                    {
                        _latticeHandler?.Cancel();
                        UpdateTopologyToolsOverlay();
                    }
                };
            }
            ConnectCancelKey(_layoutRoot?.PerspectivePanel);
            ConnectCancelKey(_layoutRoot?.TopPanel);
            ConnectCancelKey(_layoutRoot?.FrontPanel);
            ConnectCancelKey(_layoutRoot?.SidePanel);

            // Backspace / Delete による「直前に指定した点」の取り消し（面追加のみ）。
            void ConnectUndoPointKey(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnUndoPointKey += () =>
                {
                    if (_interactionMode != InteractionMode.AddFace) return;
                    if (_addFaceHandler != null && _addFaceHandler.RemoveLastPoint())
                        _addFaceSubPanel?.Refresh();
                };
            }
            ConnectUndoPointKey(_layoutRoot?.PerspectivePanel);
            ConnectUndoPointKey(_layoutRoot?.TopPanel);
            ConnectUndoPointKey(_layoutRoot?.FrontPanel);
            ConnectUndoPointKey(_layoutRoot?.SidePanel);

            // 面追加（四角形）で3点配置済みのとき、右クリックで三角形として確定する。
            // 右ドラッグはカメラ回転だが、OnClick はドラッグ閾値未満のときだけ発火するため競合しない。
            void ConnectAddFaceRightClick(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 1) return;
                    if (_interactionMode != InteractionMode.AddFace) return;
                    if (_addFaceHandler != null && _addFaceHandler.FinishAsTriangle())
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
                _interactionMode == InteractionMode.SkinWeightPaint ||
                _interactionMode == InteractionMode.None)
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
            _overlayIndicators.Clear();

            bool boneEditorOpen = _layoutRoot?.BoneEditorSection?.style.display == DisplayStyle.Flex;
            bool objectMoveMode = _interactionMode == InteractionMode.ObjectMove;
            bool pivotMode      = _interactionMode == InteractionMode.PivotOffset;
            bool show = boneEditorOpen || objectMoveMode || pivotMode;

            var model = ActiveProject?.CurrentModel;
            bool haveModel = model != null && model.MeshContextCount > 0;

            // 全ビューポートのボーンオーバーレイを更新（アクティブ以外も追従させる）。
            // スライダ/TRS 編集ではビューポートに触れないため、従来はアクティブ 1 画面しか
            // 更新されず、他画面のマーカーが取り残されていた（触れると直る症状の原因）。
            UpdateBoneOverlayFor(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport, model, show && haveModel, boneEditorOpen);
            UpdateBoneOverlayFor(_layoutRoot?.TopPanel,         _viewportManager.TopViewport,         model, show && haveModel, boneEditorOpen);
            UpdateBoneOverlayFor(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport,       model, show && haveModel, boneEditorOpen);
            UpdateBoneOverlayFor(_layoutRoot?.SidePanel,        _viewportManager.SideViewport,        model, show && haveModel, boneEditorOpen);
        }

        private void UpdateBoneOverlayFor(
            PlayerViewportPanel panel, PlayerViewport vp,
            ModelContext model, bool show, bool boneEditorOpen)
        {
            if (panel == null) return;
            if (!show) { panel.HideBoneWire(); return; }

            var ctx = _viewportManager.GetCurrentToolContext(vp);
            if (ctx == null) { panel.HideBoneWire(); return; }

            // ヒットテスト用インジケーターはアクティブビューポート基準で構築する。
            bool buildIndicators = ReferenceEquals(vp, _activeViewport);

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
                                    && !mc.IsSkinned;

                bool include = boneEditorOpen ? isBone : (isBone || isNonSkinned);
                if (!include) continue;

                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                sp.y = panelH - sp.y;

                bool isSel = model.SelectedMeshContextIndices.Contains(i);

                positions.Add(sp);
                selected.Add(isSel);

                if (buildIndicators)
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
        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

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

            // PointInfo.Position はローカル座標。ctx（ToToolContext 由来）は Model を持たないので
            // 操作対象メッシュの WorldMatrix は実モデルから解決して適用する。
            var afModel = ActiveProject?.CurrentModel;
            var afMc = afModel?.ActiveMeshContext;
            var afL2W = afMc?.WorldMatrix ?? UnityEngine.Matrix4x4.identity;

            // AdvSel と完全に同じパターン:
            // ViewerCore側で h - sp.y を行い、Panel側で panelH - pt.y を行う。
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.WorldToScreen(afL2W.MultiplyPoint3x4(local));
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 既存頂点を指す点は、GPU が計算済みのワールド座標を使う
            // （PlayerViewportManager.TryGetVertexWorld → GetDisplayPositions）。
            // スキニング規則を CPU 側で計算し直すと GPU の描画位置と食い違い、
            // マーカーだけがずれる。
            // 新規点の Position は AddFaceTool が ActiveWorldToLocal（= WorldMatrix の逆）で
            // 作っているので、こちらは WorldMatrix で往復させる（この往復は閉じている）。
            System.Func<int, UnityEngine.Vector3, UnityEngine.Vector2> vertexToScreen =
                (vertexIndex, fallbackLocal) =>
            {
                if (vertexIndex >= 0 && afModel != null && afMc != null &&
                    _viewportManager.TryGetVertexWorld(afModel, afMc, vertexIndex, out var wp))
                {
                    var spw = ctx.WorldToScreen(wp);
                    return new UnityEngine.Vector2(spw.x, h - spw.y);
                }
                return toScreen(fallbackLocal);
            };

            System.Func<Poly_Ling.Tools.PointInfo, UnityEngine.Vector2> pointToScreen = (pi) =>
                vertexToScreen(pi.IsExistingVertex ? pi.ExistingVertexIndex : -1, pi.Position);

            // プレビュー点。既存頂点にスナップしているときは PreviewVertexIndex が
            // その頂点を指すので、同じく GPU の座標を使う。
            System.Func<UnityEngine.Vector2> previewToScreen = () =>
                vertexToScreen(data.PreviewSnapped ? data.PreviewVertexIndex : -1, data.PreviewPoint);

            // 確定済み点
            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (var p in data.PlacedPoints)
                pts.Add(pointToScreen(p));

            // 線（配置済み点間）
            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            for (int i = 1; i < data.PlacedPoints.Length; i++)
                lines.Add((pointToScreen(data.PlacedPoints[i - 1]), pointToScreen(data.PlacedPoints[i])));

            // 連続線分モード開始点からプレビューへの線
            if (data.ContinuousLineStart.HasValue && data.PreviewValid)
                lines.Add((pointToScreen(data.ContinuousLineStart.Value), previewToScreen()));

            // 最後の確定済み点からプレビューへの線
            if (data.PlacedPoints.Length > 0 && data.PreviewValid)
                lines.Add((pointToScreen(data.PlacedPoints[data.PlacedPoints.Length - 1]), previewToScreen()));

            // プレビュー点
            // 非選択オブジェクトへの吸着は色を変えて区別する（既定のシアンは選択メッシュ用）。
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            var previewCols = new System.Collections.Generic.List<UnityEngine.Color?>();
            if (data.PreviewValid)
            {
                previewPts.Add(previewToScreen());
                previewSnap.Add(data.PreviewSnapped);
                previewCols.Add(data.PreviewSnappedUnselected
                    ? (UnityEngine.Color?)AddFaceUnselectedSnapColor
                    : null);
            }

            // Quad で3点配置済み、ホバーが1点目の既存頂点のときは開始点を強調する。
            int afHighlight = (data.CloseToStart && pts.Count > 0) ? 0 : -1;

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines, afHighlight, previewCols);
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
        /// 【mo の取得: ctx.ActiveMeshObject は使えない】
        /// _viewportManager.GetCurrentToolContext() が返す ToolContext は Model を
        /// 設定しないため、ctx.ActiveMeshObject は常に null を返す罠がある。
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

            // ActiveProject から直接取る (ctx.ActiveMeshObject は上記注意点で null)
            var stMc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            var mo = stMc?.MeshObject;
            if (mo == null) { panel.HideAddFacePreview(); return; }

            // Vertices[].Position はローカル座標なので WorldMatrix を適用してから投影する。
            var stL2W = stMc.WorldMatrix;
            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.WorldToScreen(stL2W.MultiplyPoint3x4(local));
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

            // ── 穴つなぎ（ブリッジ）の種マーカー ─────────────────────
            // ブリッジは専用の InteractionMode を持たず、図形生成パネルを開いた
            // ままの SelectOnly / None / PrimitivePlace で操作する。以降の分岐は
            // どれも該当せず末尾の HideTopoToolOverlay() まで落ちるため、
            // ここで先に横取りする。
            if (UpdateBridgeSeedOverlay(panel, ctx)) return;

            // ── 格子変形 ─────────────────────────────────────────────
            // 格子の線と制御点はメッシュ頂点ではなく作業軸ローカルの制御点を
            // 投影したもの。組み立ては LatticeToolHandler が持つ。
            // 作業軸モードでもセッションが生きている間は描き続ける
            // （格子フレームを動かしている最中に格子が消えないようにする）。
            bool latticeOpen = _latticeHandler != null
                && _latticeHandler.State != LatticeToolHandler.LatticeState.Idle
                && (_interactionMode == InteractionMode.Lattice
                 || _interactionMode == InteractionMode.WorkAxis);

            if (latticeOpen || _interactionMode == InteractionMode.Lattice)
            {
                if (latticeOpen
                    && _latticeHandler.TryBuildOverlay(ctx, out var latLines, out var latPoints))
                    panel.UpdateTopoToolOverlay(latLines, latPoints, null);
                else
                    panel.HideTopoToolOverlay();
                return;
            }

            var mo = ctx.ActiveMeshObject;
            float h = ctx.PreviewRect.height;

            // Vertices[].Position はローカル座標。ctx の操作対象メッシュの WorldMatrix を適用する。
            // LocalToScreen: AddFaceOverlay と同じ変換 (h - sp.y)
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (local) =>
            {
                var sp = ctx.LocalToScreen(local);
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

            // ── 頂点溶解 / 三角形4→1 / 面結合 ─────────────────────────────
            // マウス直下の要素だけを実行可否で色分けする（緑=実行できる、赤=できない）。
            // 実行できるときは、その操作で消える面の外周も同色で描いて影響範囲を示す。
            // 候補を全部塗る方式は採らない（内部の頂点・辺はほぼ全部候補になるため）。
            if ((_interactionMode == InteractionMode.VertexDissolve
              || _interactionMode == InteractionMode.Tri4To1
              || _interactionMode == InteractionMode.FaceMerge
              || _interactionMode == InteractionMode.Quad4To1
              || _interactionMode == InteractionMode.FaceMergeCollapse) && mo != null)
            {
                var points   = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Color, float)>();
                var okColor  = new UnityEngine.Color(0.2f, 1f, 0.35f);
                var ngColor  = new UnityEngine.Color(1f, 0.3f, 0.25f);
                var hoverModel = ActiveProject?.CurrentModel;

                // 面の外周を線として積む。
                void AddFaceOutline(int faceIndex, UnityEngine.Color col)
                {
                    if (faceIndex < 0 || faceIndex >= mo.FaceCount) return;
                    var f = mo.Faces[faceIndex];
                    int n = f.VertexIndices.Count;
                    if (n < 2) return;
                    for (int i = 0; i < n; i++)
                    {
                        int va = f.VertexIndices[i];
                        int vb = f.VertexIndices[(i + 1) % n];
                        if (va < 0 || va >= mo.VertexCount || vb < 0 || vb >= mo.VertexCount) continue;
                        lines.Add((toScreen(mo.Vertices[va].Position),
                                   toScreen(mo.Vertices[vb].Position), col));
                    }
                }

                // (v0,v1) を辺として持つ面を列挙する（2頂点の線分は除く）。
                System.Collections.Generic.List<int> FacesOnEdge(int v0, int v1)
                {
                    var result = new System.Collections.Generic.List<int>();
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var f = mo.Faces[fi];
                        int n = f.VertexIndices.Count;
                        if (n < 3) continue;
                        for (int i = 0; i < n; i++)
                        {
                            int a = f.VertexIndices[i];
                            int b = f.VertexIndices[(i + 1) % n];
                            if ((a == v0 && b == v1) || (a == v1 && b == v0)) { result.Add(fi); break; }
                        }
                    }
                    return result;
                }

                bool sameMesh(PlayerHoverElement e) =>
                    hoverModel != null && e.MeshIndex == hoverModel.ActiveMeshIndex;

                if (_interactionMode == InteractionMode.Quad4To1)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Vertex, hoverModel);
                    int v = elem.VertexIndex;
                    if (elem.Kind == PlayerHoverKind.Vertex && sameMesh(elem)
                        && v >= 0 && v < mo.VertexCount)
                    {
                        var info = Quad4To1Ops.Inspect(mo, v);
                        var col  = info.CanExecute ? okColor : ngColor;

                        // 実行できるときは、1枚に統合される四角形4枚の外周を描く。
                        if (info.CanExecute)
                        {
                            for (int fi = 0; fi < mo.FaceCount; fi++)
                                if (mo.Faces[fi].VertexIndices.Contains(v)) AddFaceOutline(fi, col);
                        }
                        points.Add((toScreen(mo.Vertices[v].Position), col, 7f));
                    }
                }
                else if (_interactionMode == InteractionMode.FaceMergeCollapse)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Edge, hoverModel);
                    int v0 = elem.EdgeV1, v1 = elem.EdgeV2;
                    if (elem.Kind == PlayerHoverKind.Edge && sameMesh(elem)
                        && v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                    {
                        var info = FaceMergeCollapseOps.Inspect(mo, new VertexPair(v0, v1));
                        var col  = info.CanExecute ? okColor : ngColor;

                        if (info.CanExecute)
                            foreach (int g in FacesOnEdge(v0, v1)) AddFaceOutline(g, col);

                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position), col));
                        points.Add((toScreen(mo.Vertices[v0].Position), col, 5f));
                        points.Add((toScreen(mo.Vertices[v1].Position), col, 5f));
                    }
                }
                else if (_interactionMode == InteractionMode.VertexDissolve)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Vertex, hoverModel);
                    int v = elem.VertexIndex;
                    if (elem.Kind == PlayerHoverKind.Vertex && sameMesh(elem)
                        && v >= 0 && v < mo.VertexCount)
                    {
                        var info = VertexDissolveOps.Inspect(mo, v);
                        var col  = info.CanExecute ? okColor : ngColor;

                        // 実行できるときは、1枚に統合される面の外周を描く。
                        if (info.CanExecute)
                        {
                            for (int fi = 0; fi < mo.FaceCount; fi++)
                                if (mo.Faces[fi].VertexIndices.Contains(v)) AddFaceOutline(fi, col);
                        }
                        points.Add((toScreen(mo.Vertices[v].Position), col, 7f));
                    }
                }
                else if (_interactionMode == InteractionMode.Tri4To1)
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Face, hoverModel);
                    int fi0  = elem.FaceIndex;
                    if (elem.Kind == PlayerHoverKind.Face && sameMesh(elem)
                        && fi0 >= 0 && fi0 < mo.FaceCount)
                    {
                        var info = Tri4To1Ops.Inspect(mo, fi0);
                        var col  = info.CanExecute ? okColor : ngColor;

                        AddFaceOutline(fi0, col);

                        // 実行できるときは、一緒に消える囲みの3枚も描く。
                        if (info.CanExecute)
                        {
                            var f = mo.Faces[fi0];
                            int n = f.VertexIndices.Count;
                            for (int i = 0; i < n; i++)
                            {
                                var nb = FacesOnEdge(f.VertexIndices[i], f.VertexIndices[(i + 1) % n]);
                                foreach (int g in nb) if (g != fi0) AddFaceOutline(g, col);
                            }
                        }
                    }
                }
                else
                {
                    var elem = _viewportManager.GetHoverElement(MeshSelectMode.Edge, hoverModel);
                    int v0 = elem.EdgeV1, v1 = elem.EdgeV2;
                    if (elem.Kind == PlayerHoverKind.Edge && sameMesh(elem)
                        && v0 >= 0 && v0 < mo.VertexCount && v1 >= 0 && v1 < mo.VertexCount)
                    {
                        var info = FaceMergeOps.Inspect(mo, new VertexPair(v0, v1));
                        var col  = info.CanExecute ? okColor : ngColor;

                        // 実行できるときは、結合される2枚の外周を描く。
                        if (info.CanExecute)
                            foreach (int g in FacesOnEdge(v0, v1)) AddFaceOutline(g, col);

                        lines.Add((toScreen(mo.Vertices[v0].Position),
                                   toScreen(mo.Vertices[v1].Position), col));
                        points.Add((toScreen(mo.Vertices[v0].Position), col, 5f));
                        points.Add((toScreen(mo.Vertices[v1].Position), col, 5f));
                    }
                }

                panel.UpdateTopoToolOverlay(lines, points);
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
            var ovMc = ovModel?.ActiveMeshContext;
            var mo = ovMc?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            // Vertices[].Position はローカル座標。WorldMatrix 適用後に投影する。
            var ovL2W = ovMc.WorldMatrix;
            System.Func<Vector3, Vector2> ovToScreen =
                (local) => ctx.WorldToScreen(ovL2W.MultiplyPoint3x4(local));

            var pts = new System.Collections.Generic.List<Vector2>();
            var verts = previewCtx.PreviewVertices;
            if (verts != null)
                foreach (int vi in verts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ovToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }
            var path = previewCtx.PreviewPath;
            if (path != null)
                foreach (int vi in path)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ovToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }

            var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
            var edges = previewCtx.PreviewEdges;
            if (edges != null)
                foreach (var e in edges)
                {
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount || e.V2 < 0 || e.V2 >= mo.VertexCount) continue;
                    var s1 = ovToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ovToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }
            if (path != null && path.Count > 1)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    int v1 = path[i], v2 = path[i + 1];
                    if (v1 < 0 || v1 >= mo.VertexCount || v2 < 0 || v2 >= mo.VertexCount) continue;
                    var s1 = ovToScreen(mo.Vertices[v1].Position);
                    var s2 = ovToScreen(mo.Vertices[v2].Position);
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
                    var s1 = ovToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ovToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    firstEdge = (new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y));
                }
            }
            else if (_advSelFlashVertex >= 0)
                emphVertex = _advSelFlashVertex;

            if (emphVertex >= 0 && emphVertex < mo.VertexCount)
            {
                var fsp = ovToScreen(mo.Vertices[emphVertex].Position);
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
            var kfMc = kfModel?.ActiveMeshContext;
            var mo = kfMc?.MeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            var prev = _knifeHandler.GetPreview();
            if (prev == null) { panel.HideAdvSelPreview(); return; }

            // prev.DotWorld / prev.Lines はワールド座標（KnifeTool.VW が GPU 値で構築）。
            // ここで行列を掛けてはならない。掛けると二重変換になる。
            float h = ctx.PreviewRect.height;
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 頂点インデックスで渡される点は GPU の値を直接引く。
            System.Func<int, UnityEngine.Vector2?> vertexToScreen = (vi) =>
            {
                if (vi < 0 || vi >= mo.VertexCount) return null;
                if (!_viewportManager.TryGetVertexWorld(kfModel, kfMc, vi, out var wp)) return null;
                return toScreen(wp);
            };

            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (int vi in prev.DotVertices)
            {
                var p2 = vertexToScreen(vi);
                if (p2.HasValue) pts.Add(p2.Value);
            }
            foreach (var w in prev.DotWorld)
                pts.Add(toScreen(w));

            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            foreach (var seg in prev.Lines)
                lines.Add((toScreen(seg.Item1), toScreen(seg.Item2)));

            // SimpleCut: 画面座標で指定された点/線はそのまま追加（投影不要）。
            foreach (var d in prev.ScreenDots)
                pts.Add(d);
            foreach (var s in prev.ScreenLines)
                lines.Add((s.Item1, s.Item2));

            // クリック点フラッシュ強調（AdvSel と共通：辺＝太線／頂点＝リング）
            Vector2? firstPt = null;
            (Vector2, Vector2)? firstEdge = null;
            if (_advSelFlashEdge.HasValue)
            {
                var e = _advSelFlashEdge.Value;
                var a2 = vertexToScreen(e.V1);
                var b2 = vertexToScreen(e.V2);
                if (a2.HasValue && b2.HasValue) firstEdge = (a2.Value, b2.Value);
            }
            else if (_advSelFlashVertex >= 0)
                firstPt = vertexToScreen(_advSelFlashVertex);

            panel.UpdateAdvSelPreview(pts, lines, prev.PlanValid, firstPt, firstEdge);
        }

        private void UpdateGizmoOverlay()
        {
            // 作業軸はポインタが乗っていないビューポートにも出す。
            // 下のアクティブ側処理は _activePanel が null 等で早期 return する
            // 経路があるため、取り残さないよう先に済ませる。
            UpdateWorkAxisOverlayOnInactivePanels();

            var panel = _activePanel;
            if (panel == null) return;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideGizmo(); return; }

            // ギズモ形状の決定は各 ToolHandler (IPlayerGizmoProvider) が持つ。
            // ここはモードに対応するプロバイダを選んで結果を渡すだけ。
            var provider = GizmoProviderFor(_interactionMode);
            if (provider == null) { panel.HideGizmo(); return; }

            if (provider.TryBuildGizmoData(ctx, out var data))
            {
                if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
                {
                    Debug.Log(
                        $"[GizmoDbg/Draw] mode={_interactionMode} provider={provider.GetType().Name} " +
                        $"origin={data.Origin} xEnd={data.XEnd} yEnd={data.YEnd} zEnd={data.ZEnd} " +
                        $"hover={data.HoveredAxis} drag={data.DraggingAxis} " +
                        $"cube={data.IsCubeStyle} diamond={data.IsDiamondStyle} ring={data.IsRingStyle} " +
                        $"pivot={data.HasPivotGizmo}/{data.PivotOrigin}");
                }
                panel.UpdateGizmo(data);
            }
            else panel.HideGizmo();
        }

        /// <summary>
        /// 作業軸を 3D 画面で編集できる状態か。
        /// 作業軸ツールそのものと、変形モードの作業軸フェーズが該当する。
        /// </summary>
        private bool IsWorkAxisEditable()
        {
            if (_interactionMode == InteractionMode.WorkAxis) return true;

            return _interactionMode == InteractionMode.Deform
                && _deformHandler != null
                && _deformHandler.Phase == DeformToolHandler.DeformPhase.WorkAxis;
        }

        /// <summary>
        /// 変形モードの入力経路をフェーズに応じて張り替える。
        /// SwitchTool と DeformToolHandler.OnPhaseChanged の両方から呼ぶ。
        ///
        /// 作業軸フェーズ … 作業軸ツールと同じ経路。矢印・リング・Y 先端ハンドルを掴める。
        ///                  ビューポートでの頂点選択はできない（作業軸ツールと同じ制約）。
        /// 変形フェーズ   … MoveToolHandler を流用して選択を残しつつ、組み込み移動ギズモを
        ///                  抑制して変形ハンドルへ委譲する（PrimitivePlace と同じ構成）。
        ///                  SelectOnly は使えない。GizmoHitTestOverride が SelectOnly 時に
        ///                  スキップされるため、ハンドルを掴めなくなる。代わりに
        ///                  OnDragStartExtra が常に true を返して頂点移動だけを抑止する。
        ///                  クリック選択と、何も掴んでいない位置からの矩形／投げ縄選択は効く。
        /// </summary>
        private void ApplyDeformToolRouting()
        {
            if (_interactionMode != InteractionMode.Deform) return;

            bool workAxisPhase = _deformHandler != null
                && _deformHandler.Phase == DeformToolHandler.DeformPhase.WorkAxis;

            // フックは毎回張り直す。作業軸フェーズで残すと、掴んでいない
            // ドラッグでも OnDragStartExtra が true を返し続けてしまう。
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SuppressBuiltinGizmo = false;
                _moveToolHandler.GizmoHitTestOverride = null;
                _moveToolHandler.OnDragStartExtra     = null;
                _moveToolHandler.OnToolDragExtra      = null;
                _moveToolHandler.OnToolDragEndExtra   = null;
            }

            if (workAxisPhase)
            {
                _vertexInteractor?.SetToolHandler(_workAxisHandler);
                _viewportManager?.RegisterActiveToolHandler(
                    (pos, ctx) => _workAxisHandler?.UpdateHover(pos, ctx));
                return;
            }

            _vertexInteractor?.SetToolHandler(_moveToolHandler);
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SuppressBuiltinGizmo = true;
                _moveToolHandler.GizmoHitTestOverride = (pos, c) =>
                    _deformHandler != null && _deformHandler.GizmoHitTest(pos, c);
                _moveToolHandler.OnDragStartExtra     = (elem, mods) =>
                {
                    _deformHandler?.BeginGizmoDrag();
                    return true;
                };
                _moveToolHandler.OnToolDragExtra      = (pos, delta, mods) => _deformHandler?.GizmoDrag(pos);
                _moveToolHandler.OnToolDragEndExtra   = (pos, mods) => _deformHandler?.EndGizmoDrag();
            }
            _viewportManager?.RegisterActiveToolHandler(
                (pos, ctx) => _deformHandler?.UpdateHover(pos, ctx));
        }

        /// <summary>
        /// ポインタが乗っていないビューポートの作業軸表示を更新する。
        ///
        /// 【なぜ要るか】
        /// UpdateGizmoOverlay は _activePanel にしか GizmoData を書かず、別の
        /// ビューポートへポインタが移ると前のパネルは HideGizmo される
        /// （OnPointerHover 内）。そのため作業軸が「見ている画面」から消えていた。
        ///
        /// 【何を出すか】
        /// 作業軸モード … 六角錐だけの表示専用データ（TryBuildDisplayOnlyGizmoData）。
        ///                矢印やリングはそのビューポートでは掴めないので出さない。
        /// 変形モード   … アクティブ側と同じもの。変形のギズモは元から操作を
        ///                受けない（UpdateHover とドラッグが空実装）ため、
        ///                掴めるように見えてしまう心配がない。
        /// それ以外     … 隠す。
        /// </summary>
        private void UpdateWorkAxisOverlayOnInactivePanels()
        {
            Apply(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport);
            Apply(_layoutRoot?.TopPanel,         _viewportManager.TopViewport);
            Apply(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport);
            Apply(_layoutRoot?.SidePanel,        _viewportManager.SideViewport);

            void Apply(PlayerViewportPanel p, PlayerViewport vp)
            {
                if (p == null || ReferenceEquals(p, _activePanel)) return;

                var ctx = _viewportManager.GetCurrentToolContext(vp);
                if (ctx == null) { p.HideGizmo(); return; }

                if (_interactionMode == InteractionMode.WorkAxis &&
                    _workAxisHandler != null &&
                    _workAxisHandler.TryBuildDisplayOnlyGizmoData(ctx, out var waData))
                {
                    p.UpdateGizmo(waData);
                    return;
                }

                if (_interactionMode == InteractionMode.Deform &&
                    _deformHandler != null &&
                    _deformHandler.TryBuildGizmoData(ctx, out var dfData))
                {
                    p.UpdateGizmo(dfData);
                    return;
                }

                p.HideGizmo();
            }
        }

        /// <summary>
        /// InteractionMode に対応するギズモ供給元を返す。null はギズモ非表示。
        /// 既定 (頂点移動・選択専用・トポロジ系ツール等) は MoveToolHandler の
        /// 組み込み軸ギズモで、SelectOnly / SuppressBuiltinGizmo のときは
        /// MoveToolHandler 側が false を返して非表示になる。
        /// </summary>
        private IPlayerGizmoProvider GizmoProviderFor(InteractionMode mode)
        {
            switch (mode)
            {
                case InteractionMode.ObjectMove:      return _objectMoveHandler;
                case InteractionMode.PivotOffset:     return _pivotOffsetHandler;
                case InteractionMode.Rotate:          return _rotateHandler;
                case InteractionMode.Scale:           return _scaleHandler;
                case InteractionMode.PrimitivePlace:  return _primitivePlaceHandler;
                case InteractionMode.WorkAxis:        return _workAxisHandler;
                case InteractionMode.Deform:          return _deformHandler;
                case InteractionMode.Lattice:         return _latticeHandler;
                case InteractionMode.Camera:          return _cameraHandler;

                case InteractionMode.Sculpt:
                case InteractionMode.AdvancedSelect:
                case InteractionMode.SkinWeightPaint:
                case InteractionMode.SkinWeightNumeric:
                case InteractionMode.None:            return null;

                default:                              return _moveToolHandler;
            }
        }

        // ================================================================
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _uiRoot = root;

            // 全 TextField のキャレット色を白で一元化する USS を root に一度だけ付与する。
            // 子孫の TextField 全てへカスケードするため、各フィールドでの個別適用は不要。
            var caretSheet = Resources.Load<StyleSheet>("PolyLingCaret");
            if (caretSheet != null) root.styleSheets.Add(caretSheet);

            // 全ボタンの操作フィードバック（ホバー/押下中/押下確定/無効）を root に一括導入する。
            // 個々のボタン生成箇所やサブパネル側の変更は不要。
            PlayerLayoutRoot.InstallButtonFeedback(root);

            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            _panelContext = new PanelContext(DispatchPanelCommand);

            _modelListSubPanel = new ModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            _meshListSubPanel = new MeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);
            AttachPanelSelectToggle(_layoutRoot.MeshListSection, PanelSelectKeyMeshList);

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

            _shrinkSubPanel = new PlayerShrinkSubPanel();
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
                GetH = () => _smoothEdgesHandler,
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
                GetH = () => _vertexHoleHandler,
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
                GetH = () => _vertexDissolveHandler,
            };
            _vertexDissolveSubPanel.Build(_layoutRoot.VertexDissolveSection);

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
                GetH = () => _tri4To1Handler,
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
                GetH = () => _faceMergeHandler,
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
                GetH = () => _quad4To1Handler,
            };
            _quad4To1SubPanel.Build(_layoutRoot.Quad4To1Section);

            _faceMergeCollapseHandler = new FaceMergeCollapseToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _faceMergeCollapseHandler.SetProject(ActiveProject);
            _faceMergeCollapseHandler.SetUndoController(_editOps?.UndoController);
            _faceMergeCollapseHandler.SetCommandQueue(_editOps?.CommandQueue);
            _faceMergeCollapseHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.EnterTopologyChanged(proj);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _faceMergeCollapseSubPanel = new PlayerFaceMergeCollapseSubPanel
            {
                GetH = () => _faceMergeCollapseHandler,
            };
            _faceMergeCollapseSubPanel.Build(_layoutRoot.FaceMergeCollapseSection);

            _vertexIdSubPanel = new PlayerVertexIdSubPanel
            {
                GetView     = () => ActiveProject,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _vertexIdSubPanel.Build(_layoutRoot.VertexIdSection);

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
            // (EnsureDrawableMesh / OnPrimitiveMeshCreated / OnMeshDataReceived /
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
                    // 全トポロジハンドラを漏れなく伝播する (OnPrimitiveMeshCreated,
                    // OnMeshDataReceived 等の他経路も同じ列挙を持つ)。新ハンドラ追加時は
                    // 全伝播箇所に新しい `_xxxHandler?.SetProject(ActiveProject);` を追加すること。
                    _edgeBevelHandler?.SetProject(ActiveProject);
                    _edgeExtrudeHandler?.SetProject(ActiveProject);
                    _faceExtrudeHandler?.SetProject(ActiveProject);
                    _edgeTopologyHandler?.SetProject(ActiveProject);
                    _knifeHandler?.SetProject(ActiveProject);
                    _deleteSelectionHandler?.SetProject(ActiveProject);
                    _vertexDissolveHandler?.SetProject(ActiveProject);
                    _tri4To1Handler?.SetProject(ActiveProject);
                    _faceMergeHandler?.SetProject(ActiveProject);
                    _quad4To1Handler?.SetProject(ActiveProject);
                    _faceMergeCollapseHandler?.SetProject(ActiveProject);
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

            // 作業用ローカル軸。ModelContext.WorkAxis だけを読み書きし、頂点には触れない。
            _workAxisHandler = new WorkAxisToolHandler
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetPanelHeight = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
                GetWorkAxis    = () => CurrentWorkAxis(),
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
                // 生成メッシュの追加は図形生成と同じ経路に流す（UNDO もそちらで記録される）。
                OnMeshCreated = (mo, name, pos, rot, scl, ign, mode, target) =>
                    OnPrimitiveMeshCreated(mo, name, pos, rot, scl, ign, mode, target),
            };
            _solidifyHandler.SetProject(ActiveProject);
            _solidifyHandler.SetUndoController(_editOps?.UndoController);
            _solidifyHandler.SetCommandQueue(_editOps?.CommandQueue);
            _solidifySubPanel = new PlayerSolidifySubPanel
            {
                GetH = () => _solidifyHandler,
                GetDrawableIndexList          = BuildDrawableIndexList,
                GetFirstSelectedDrawableIndex = () => ActiveProject?.CurrentModel?.ActiveMeshIndex ?? -1,
            };
            _solidifySubPanel.Build(_layoutRoot.SolidifySection);

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

            // パイプライン自動検証。パネルが押されたときと同じ PanelCommand を送るので、
            // ディスパッチャ側の欠陥もそのまま検査に掛かる。
            _pipelineTestSubPanel = new PlayerPipelineTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                LoadProjectFolder = LoadProjectFolderForTest,
                SaveProjectFolder = SaveProjectFolderForTest,
                CreateBridge      = CreateBridgeForTest,
                RefreshAfterTopologyChange = () =>
                {
                    _viewportManager.EnterTopologyChanged(ActiveProject);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _pipelineTestSubPanel.Build(_layoutRoot.PipelineTestSection);

            // 原点CSV自動検証。MQO 読込も CSV 適用も実経路（ImportMqoCommand /
            // ApplyObjectOriginsCommand）へ流すので、ディスパッチャ側の欠陥も検査に掛かる。
            _originTestSubPanel = new PlayerOriginTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                ImportMqo     = path => OnImportMqo(path, null),
            };
            _originTestSubPanel.Build(_layoutRoot.OriginTestSection);

            // スキン生成自動検証。原点CSVは使わず、ConvertMeshFilterToSkinnedCommand と
            // ApplyHumanoidMappingCommand を実経路へ流す。
            _skinTestSubPanel = new PlayerSkinTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => _panelContext?.SendCommand(cmd),
                ImportMqo     = path => OnImportMqo(path, null),
            };
            _skinTestSubPanel.Build(_layoutRoot.SkinTestSection);

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

            _motionClipTestSubPanel = new PlayerMotionClipTestSubPanel
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
            _motionClipTestSubPanel.Build(_layoutRoot.MotionClipTestSection);

            _underlaySubPanel = new PlayerUnderlaySubPanel(_underlay, ApplyAllUnderlays);
            _underlaySubPanel.Build(_layoutRoot.UnderlaySection);

            _gridAxisSubPanel = new PlayerGridAxisSubPanel(
                () => _viewportManager.GetGridSettings(),
                gs => _viewportManager.EnterDisplaySettingsChanged(gs));
            _gridAxisSubPanel.Build(_layoutRoot.GridAxisSection);

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
            AttachPanelSelectToggle(_layoutRoot.ImportSection, PanelSelectKeyImport);

            _exportSubPanel = new PlayerExportSubPanel();
            _exportSubPanel.Build(_layoutRoot.ExportSection);
            _exportSubPanel.OnExportPmx = OnExportPmx;
            _exportSubPanel.OnExportMqo = OnExportMqo;
            _exportSubPanel.OnExportObj = OnExportObj;
            _exportSubPanel.OnExportVrm = OnExportVrm;
            AttachPanelSelectToggle(_layoutRoot.ExportSection, PanelSelectKeyExport);

            _projectSaveSubPanel = new PlayerProjectFileSubPanel
            {
                Mode = PlayerProjectFileSubPanel.PanelMode.Save,
            };
            _projectSaveSubPanel.Build(_layoutRoot.ProjectSaveSection);
            _projectSaveSubPanel.OnSave    = OnSaveProject;
            _projectSaveSubPanel.OnSaveCsv = OnSaveCsvProject;
            AttachPanelSelectToggle(_layoutRoot.ProjectSaveSection, PanelSelectKeyProjectSave);

            _projectLoadSubPanel = new PlayerProjectFileSubPanel
            {
                Mode = PlayerProjectFileSubPanel.PanelMode.Load,
            };
            _projectLoadSubPanel.Build(_layoutRoot.ProjectLoadSection);
            _projectLoadSubPanel.OnLoad    = OnLoadProject;
            _projectLoadSubPanel.OnLoadCsv = OnLoadCsvProject;
            AttachPanelSelectToggle(_layoutRoot.ProjectLoadSection, PanelSelectKeyProjectLoad);

            _partialImportSubPanel = new PlayerPartialImportSubPanel();
            _partialImportSubPanel.Build(_layoutRoot.PartialImportSection);
            _partialImportSubPanel.OnImportDone = OnPartialImportDone;

            _partialExportSubPanel = new PlayerPartialExportSubPanel();
            _partialExportSubPanel.Build(_layoutRoot.PartialExportSection);

            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            // 最後に選んだ図形の保存キー。Build 内で読み込むため Build より前に設定する。
            _primitiveSubPanel.MemoryKey = "Primitive";
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            _primitiveSubPanel.OnMeshCreated = (mo, name, pos, rot, scl, ign, mode, target, mat) =>
                OnPrimitiveMeshCreated(mo, name, pos, rot, scl, ign, mode, target, mat);
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
            _primitiveSubPanel.OnObjectArrayGenerate = ExecuteObjectArray;
            // 穴つなぎ（ブリッジ）。種の取り込みと実生成は Viewer 側が持つ。
            WireBridgeCallbacks(_primitiveSubPanel);
            AttachPanelSelectToggle(_layoutRoot.PrimitiveSection, PanelSelectKeyPrimitive);

            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;
            _layoutRoot.AdvancedPrimitiveBtn.clicked += ShowAdvancedPrimitivePanel;

            // 検証用の新サブツール。同一クラスの別インスタンスで、既存とは状態を共有しない。
            // 生成結果の扱い (OnMeshCreated 以降) は既存と完全に同じ経路を通す。
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

            _livePrimitiveSubPanel.Build(_layoutRoot.LivePrimitiveSection, _sceneRoot);
            _livePrimitiveSubPanel.OnMeshCreated = (mo, name, pos, rot, scl, ign, mode, target, mat) =>
                OnPrimitiveMeshCreated(mo, name, pos, rot, scl, ign, mode, target, mat);
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
            _livePrimitiveSubPanel.OnObjectArrayGenerate = ExecuteObjectArray;
            // 穴つなぎ（ブリッジ）。既存インスタンスと同じ経路を通す。
            WireBridgeCallbacks(_livePrimitiveSubPanel);

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
                },
            };

            // 配置ギズモのサブモード切替 UI（サブツール内）。Build 済みのボタンへ
            // 後から配線する。Build 時点の描画は既定 Move で、実モードとの同期は
            // PostBuildButtonColors 直後の RepaintPlaceGizmoButtons() で行う。
            _livePrimitiveSubPanel.GetPlaceGizmoMode = () => _primitivePlaceHandler?.Mode
                                                          ?? PrimitivePlaceToolHandler.PlaceGizmoMode.Move;
            _livePrimitiveSubPanel.SetPlaceGizmoMode = m => SetPlaceGizmoMode(m);

            _layoutRoot.LivePrimitiveBtn.clicked += ShowLivePrimitivePanel;
            _layoutRoot.LiveAdvancedPrimitiveBtn.clicked += ShowLiveAdvancedPrimitivePanel;

            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;
            _mfToSkinnedSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);

            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _skinKindSubPanel = new PlayerSkinKindSubPanel();
            _skinKindSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinKindSubPanel.Build(_layoutRoot.SkinKindSection);

            _layoutRoot.SkinKindBtn.clicked += ShowSkinKindPanel;

            _layoutRoot.BlendBtn.clicked      += ShowBlendPanel;
            _layoutRoot.ShrinkBtn.clicked     += ShowShrinkPanel;
            _layoutRoot.ModelBlendBtn.clicked += ShowModelBlendPanel;
            _layoutRoot.BoneEditorBtn.clicked  += () => { ShowBoneEditorPanel(); _boneEditorSubPanel?.ShowBonesTab(); };
            _layoutRoot.UVEditorBtn.clicked    += ShowUVEditorPanel;
            _layoutRoot.UVUnwrapBtn.clicked    += ShowUVUnwrapPanel;
            _layoutRoot.MaterialListBtn.clicked    += ShowMaterialListPanel;
            _layoutRoot.UVZBtn.clicked             += ShowUVZPanel;
            _layoutRoot.PartsSelectionSetBtn.clicked += ShowPartsSelectionSetPanel;
            _layoutRoot.MeshSelectionSetBtn.clicked  += ShowMeshSelectionSetPanel;
            _layoutRoot.NormalExcludeSetBtn.clicked  += ShowNormalExcludeSetPanel;
            _layoutRoot.NormalEditBtn.clicked        += ShowNormalEditPanel;
            _layoutRoot.NormalTransplantBtn.clicked  += ShowNormalTransplantPanel;
            _layoutRoot.FaceHideBtn.clicked          += ShowFaceHidePanel;
            _layoutRoot.MergeMeshesBtn.clicked     += ShowMergeMeshesPanel;
            _layoutRoot.BooleanBtn.clicked         += ShowBooleanPanel;
            _layoutRoot.TPoseBtn.clicked           += ShowTPosePanel;
            _layoutRoot.HumanoidMappingBtn.clicked += ShowHumanoidMappingPanel;
            _layoutRoot.MirrorBtn.clicked          += ShowMirrorPanel;
            _layoutRoot.QuadDecimatorBtn.clicked   += ShowQuadDecimatorPanel;
            _layoutRoot.AlignVerticesBtn.clicked       += ShowAlignVerticesPanel;
            _layoutRoot.PlanarizeAlongBonesBtn.clicked += ShowPlanarizeAlongBonesPanel;
            _layoutRoot.SmoothEdgesBtn.clicked         += ShowSmoothEdgesPanel;
            _layoutRoot.MergeVerticesBtn.clicked       += ShowMergeVerticesPanel;
            _layoutRoot.SplitVerticesBtn.clicked        += ShowSplitVerticesPanel;
            if (_layoutRoot.VertexHoleBtn != null)
                _layoutRoot.VertexHoleBtn.clicked       += ShowVertexHolePanel;
            if (_layoutRoot.VertexDissolveBtn != null)
                _layoutRoot.VertexDissolveBtn.clicked   += ShowVertexDissolvePanel;
            if (_layoutRoot.Tri4To1Btn != null)
                _layoutRoot.Tri4To1Btn.clicked          += ShowTri4To1Panel;
            if (_layoutRoot.FaceMergeBtn != null)
                _layoutRoot.FaceMergeBtn.clicked        += ShowFaceMergePanel;
            if (_layoutRoot.Quad4To1Btn != null)
                _layoutRoot.Quad4To1Btn.clicked         += ShowQuad4To1Panel;
            if (_layoutRoot.FaceMergeCollapseBtn != null)
                _layoutRoot.FaceMergeCollapseBtn.clicked += ShowFaceMergeCollapsePanel;
            if (_layoutRoot.VertexIdBtn != null)
                _layoutRoot.VertexIdBtn.clicked          += ShowVertexIdPanel;
            if (_layoutRoot.VertexTransferBtn != null)
                _layoutRoot.VertexTransferBtn.clicked    += ShowVertexTransferPanel;
            _layoutRoot.AddFaceBtn.clicked               += ShowAddFacePanel;
            _layoutRoot.FlipFaceBtn.clicked              += ShowFlipFacePanel;
            _layoutRoot.RotateBtn.clicked                += ShowRotatePanel;
            if (_layoutRoot.WorkAxisBtn != null)
                _layoutRoot.WorkAxisBtn.clicked          += ShowWorkAxisPanel;
            if (_layoutRoot.DeformBtn != null)
                _layoutRoot.DeformBtn.clicked            += ShowDeformPanel;
            if (_layoutRoot.LatticeBtn != null)
                _layoutRoot.LatticeBtn.clicked           += ShowLatticePanel;
            _layoutRoot.ScaleBtn.clicked                 += ShowScalePanel;
            _layoutRoot.EdgeBevelBtn.clicked             += ShowEdgeBevelPanel;
            _layoutRoot.EdgeExtrudeBtn.clicked           += ShowEdgeExtrudePanel;
            _layoutRoot.FaceExtrudeBtn.clicked           += ShowFaceExtrudePanel;
            _layoutRoot.EdgeTopologyBtn.clicked          += ShowEdgeTopologyPanel;
            _layoutRoot.KnifeBtn.clicked                 += ShowKnifePanel;
            // 穴つなぎ。図形生成パネルを開いて「ブリッジ」を選択する。
            if (_layoutRoot.BridgeBtn != null)
                _layoutRoot.BridgeBtn.clicked            += () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Bridge);
            _layoutRoot.SolidifyBtn.clicked              += ShowSolidifyPanel;
            _layoutRoot.MediaPipeBtn.clicked        += ShowMediaPipePanel;
            _layoutRoot.VMDTestBtn.clicked          += ShowVMDTestPanel;
            if (_layoutRoot.PipelineTestBtn != null)
                _layoutRoot.PipelineTestBtn.clicked += ShowPipelineTestPanel;
            if (_layoutRoot.OriginTestBtn != null)
                _layoutRoot.OriginTestBtn.clicked += ShowOriginTestPanel;
            if (_layoutRoot.SkinTestBtn != null)
                _layoutRoot.SkinTestBtn.clicked += ShowSkinTestPanel;
            _layoutRoot.UnityClipTestBtn.clicked    += ShowUnityClipTestPanel;
            _layoutRoot.MotionClipTestBtn.clicked   += ShowMotionClipTestPanel;
            _layoutRoot.RemoteServerBtn.clicked     += ShowRemoteServerPanel;
            if (_layoutRoot.LogBtn != null)
                _layoutRoot.LogBtn.clicked          += ShowLogPanel;
            if (_layoutRoot.UnderlayBtn != null)
                _layoutRoot.UnderlayBtn.clicked     += ShowUnderlayPanel;
            if (_layoutRoot.GridAxisBtn != null)
                _layoutRoot.GridAxisBtn.clicked     += ShowGridAxisPanel;
            if (_layoutRoot.CameraBtn != null)
                _layoutRoot.CameraBtn.clicked       += ShowCameraPanel;
            if (_layoutRoot.CaptureBtn != null)
                _layoutRoot.CaptureBtn.clicked      += ShowCapturePanel;
            _layoutRoot.FullExportPmxBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.PMX);
            _layoutRoot.FullExportMqoBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO);
            _layoutRoot.FullExportVrmBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.VRM);
            _layoutRoot.ProjectSaveBtn.clicked     += ShowProjectSavePanel;
            _layoutRoot.ProjectLoadBtn.clicked     += ShowProjectLoadPanel;
            if (_layoutRoot.ObjLoadBtn != null)
                _layoutRoot.ObjLoadBtn.clicked     += () => ShowImportPanel(PlayerImportSubPanel.Mode.OBJ);
            if (_layoutRoot.ObjSaveBtn != null)
                _layoutRoot.ObjSaveBtn.clicked     += () => ShowExportPanel(PlayerExportSubPanel.Mode.OBJ);
            _layoutRoot.PartialImportPmxBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX);
            _layoutRoot.PartialImportMqoBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO);
            _layoutRoot.PartialExportPmxBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX);
            _layoutRoot.PartialExportMqoBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO);

            _layoutRoot.ToolVertexMoveBtn.clicked        += () => ShowCategory1Panel(InteractionMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked        += () => { ShowCategory1Panel(InteractionMode.ObjectMove); _boneEditorSubPanel?.ShowObjectPoseTab(); };
            // 「原点だけ移動」は ObjectMove モードのチェックボックスに一本化したため、
            // ピボット(PivotOffset)ボタンは撤去する（非表示＋クリック無効）。
            if (_layoutRoot.ToolPivotOffsetBtn != null)
                _layoutRoot.ToolPivotOffsetBtn.style.display = DisplayStyle.None;
            _layoutRoot.ToolSculptBtn.clicked            += () => ShowCategory1Panel(InteractionMode.Sculpt);
            _layoutRoot.ToolAdvancedSelBtn.clicked       += () => ShowCategory1Panel(InteractionMode.AdvancedSelect);
            _layoutRoot.ToolSkinWeightPaintBtn.clicked   += () => ShowCategory1Panel(InteractionMode.SkinWeightPaint);
            if (_layoutRoot.SkinWeightNumericBtn != null)
                _layoutRoot.SkinWeightNumericBtn.clicked += () => ShowCategory1Panel(InteractionMode.SkinWeightNumeric);

            // 一時選択サブツール (デバッグ用ボタン。ショートカット R / G と同処理)。
            if (_layoutRoot.SubToolBoxSelectBtn != null)
                _layoutRoot.SubToolBoxSelectBtn.clicked   += () => EnterSelectSubTool(false);
            if (_layoutRoot.SubToolLassoSelectBtn != null)
                _layoutRoot.SubToolLassoSelectBtn.clicked += () => EnterSelectSubTool(true);
            if (_layoutRoot.SubToolDeleteBtn != null)
                _layoutRoot.SubToolDeleteBtn.clicked      += ExecuteDeleteSelection;
            if (_layoutRoot.ToolDeleteFaceBtn != null)
                _layoutRoot.ToolDeleteFaceBtn.clicked    += () =>
                {
                    // 押すたびに進入 / 復帰をトグルする（ボタンだけで抜けられるように）。
                    if (_deleteFaceModeActive) ExitDeleteFaceMode();
                    else                       EnterDeleteFaceMode();
                };

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

            // 「回転はローカル原点中心」。状態を持つだけで、ここでカメラは動かさない。
            // ON に戻したときは釦で確定した固定ピボットを解除し、ローカル原点中心へ戻す。
            if (_layoutRoot.OrbitAroundLocalOriginToggle != null)
                _layoutRoot.OrbitAroundLocalOriginToggle.RegisterValueChangedCallback(e =>
                {
                    _orbitAroundLocalOrigin = e.newValue;
                    if (e.newValue) _explicitOrbitPivot = null;
                });

            // 「現在の選択を中心に」。押した時点の重心をワールド固定点として保持する。
            // ここでもカメラは動かさない（次に回した瞬間から軸として効く）。
            if (_layoutRoot.OrbitCenterToSelectionBtn != null)
                _layoutRoot.OrbitCenterToSelectionBtn.clicked += () =>
                {
                    // 要素（頂点/辺/面/線分）が未選択ならローカル原点（ピボット）へ落とす。
                    var pivot = ComputeElementCentroid() ?? ComputeLocalOriginCentroid();
                    if (!pivot.HasValue)
                    {
                        Debug.LogWarning("[Orbit] 選択がないため回転中心を設定できません。");
                        return;
                    }

                    _explicitOrbitPivot = pivot.Value;

                    // UI 状態と実挙動を一致させる（チェック中なのに効かない状態を作らない）。
                    _orbitAroundLocalOrigin = false;
                    _layoutRoot.OrbitAroundLocalOriginToggle?.SetValueWithoutNotify(false);
                };

            // ── 法線 自動計算 / 手動再計算 ──────────────────────────────
            // 自動計算 ON  = 選択メッシュの PreserveNormals を false にする
            // 自動計算 OFF = 選択メッシュの PreserveNormals を true  にする
            // 既定は OFF（MeshObject.PreserveNormals の既定が true）。
            _layoutRoot.AutoRecalcNormalsToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingNormalRecalcToggle) return;
                var indices = CollectSelectedMeshIndices();
                if (indices.Length == 0) return;
                _commandDispatcher?.Dispatch(new SetPreserveNormalsCommand(
                    ActiveProject?.CurrentModelIndex ?? 0, indices, !e.newValue));
            });

            // 手動再計算。対象は NormalEditCommand 側で選択メッシュに限定される
            // （PlayerCommandDispatcher.CollectSelectedMeshContexts）。
            // 角度は法線編集パネルと同じ既定値を使う。角度を変えて掛けたい場合は
            // 「法線編集」パネルの「角度で再計算」を使うこと。
            // 左ペインの「すべてのオブジェクトを選択」。
            // メッシュリスト側の同名ボタンと同じ処理を呼ぶ。
            if (_layoutRoot.SelectAllObjectsBtn != null)
                _layoutRoot.SelectAllObjectsBtn.clicked += () =>
                    _meshListSubPanel?.SelectAllObjectsFromExternal();

            _layoutRoot.RecalcNormalsBtn.clicked += () =>
            {
                _commandDispatcher?.Dispatch(new NormalEditCommand(
                    ActiveProject?.CurrentModelIndex ?? 0,
                    NormalEditCommand.Op.RecalcByAngle,
                    NormalRecalcDefaultAngleDeg));
            };

            // 選択モード切替（頂点/辺/面/線分・非排他）。
            // トグル → _userSelectMode → ApplySelectMode() の一方向だけ。
            // トグルの値をここ以外から読んではならない（読み口が増えると再び分散する）。

            // 選択モードを端末ローカルに保存（V=1/E=2/F=4/L=8 の 4bit）。PTFS 表示と同じ RecentPaths ストア。
            System.Action saveSelectMode = () =>
            {
                int bits = (_layoutRoot.SelModeVertexToggle.value ? 1 : 0)
                         | (_layoutRoot.SelModeEdgeToggle.value   ? 2 : 0)
                         | (_layoutRoot.SelModeFaceToggle.value   ? 4 : 0)
                         | (_layoutRoot.SelModeLineToggle.value   ? 8 : 0);
                PlayerUiPrefs.SetInt(SelectModePrefKey, bits);
            };

            System.Action onSelModeToggled = () =>
            {
                saveSelectMode();
                ReadUserSelectModeFromToggles();
                ApplySelectMode();
            };

            _layoutRoot.SelModeVertexToggle.RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeEdgeToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeFaceToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeLineToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());

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
                ReadUserSelectModeFromToggles();
                ApplySelectMode();
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

            // ミラー系トグルの従属関係を UI に反映する。
            //
            //   非選Mirror（独立。非選Mesh に従属しないので常に操作可能）
            //     ├ 非選M面
            //     ├ 非選M辺
            //     └ 非選M頂点
            //
            // 親が OFF のとき、子は値を OFF に同期しグレーアウト（無効化）する。
            // 値のクランプ自体は ViewportDisplaySettings.WithMirrorClamped が行うので、
            // ここは「クランプ済みの値を UI に映す」だけ。判定を二重に書かない。
            // 選択Mirror は UI トグルを持たない（選択Mesh に従属）。
            void ApplyMirrorToggleGating(int slot)
            {
                var d = _viewportManager.GetDisplaySettings(slot); // SetDisplaySettings でクランプ済み

                // マスタは独立。SetEnabled を呼ばない（呼ぶと従属が復活する）。
                var unselMirror = _layoutRoot.ViewportDisplayToggles[slot, PlayerLayoutRoot.VD_UNSEL_MIRROR];
                unselMirror?.SetValueWithoutNotify(d.ShowUnselectedMirror);

                void SyncChild(int item, bool value)
                {
                    var t = _layoutRoot.ViewportDisplayToggles[slot, item];
                    if (t == null) return;
                    t.SetValueWithoutNotify(value);
                    t.SetEnabled(d.ShowUnselectedMirror);
                }

                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH, d.ShowUnselectedMirrorMesh);
                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE, d.ShowUnselectedMirrorWireframe);
                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT, d.ShowUnselectedMirrorVertices);
            }

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
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR: ds.ShowUnselectedMirror    = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH: ds.ShowUnselectedMirrorMesh      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE: ds.ShowUnselectedMirrorWireframe = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT: ds.ShowUnselectedMirrorVertices  = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MESH_ORIGIN:   ds.ShowSelectedMeshOrigin   = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MESH_ORIGIN: ds.ShowUnselectedMeshOrigin = e.newValue; break;
                                case PlayerLayoutRoot.VD_MIRROR_MESH_ORIGIN: ds.ShowMirrorMeshOrigin    = e.newValue; break;
                                case PlayerLayoutRoot.VD_NORMAL:             ds.ShowNormals             = e.newValue; break;
                            }
                            // Phase 2a-2g-3: SetDisplaySettings → EnterDisplaySettingsChanged に集約。
                            _viewportManager.EnterDisplaySettingsChanged(slot, ds);
                            // Mesh トグルに応じて Mirror トグルの値・有効状態を更新する。
                            ApplyMirrorToggleGating(slot);
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
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR, ds.ShowUnselectedMirror);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH, ds.ShowUnselectedMirrorMesh);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE, ds.ShowUnselectedMirrorWireframe);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT, ds.ShowUnselectedMirrorVertices);
                SyncTog(PlayerLayoutRoot.VD_SEL_MESH_ORIGIN,   ds.ShowSelectedMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MESH_ORIGIN, ds.ShowUnselectedMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_MIRROR_MESH_ORIGIN, ds.ShowMirrorMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_NORMAL,             ds.ShowNormals);
                // Mesh トグルに応じて Mirror トグルの値・有効状態を初期同期する。
                ApplyMirrorToggleGating(s);
            }

            _layoutRoot.MorphBtn.clicked       += ShowMorphPanel;
            _layoutRoot.MorphCreateBtn.clicked += ShowMorphCreatePanel;

            _layoutRoot.PostBuildButtonColors(_uiRoot);

            // PostBuildButtonColors（ApplyDarkTheme）は全 Button を既定色へ戻すため、
            // 配置ギズモボタン（サブツール内）の着色はこの後で行う。
            RepaintPlaceGizmoButtons();

            // 同じ理由で、Build 時に着色しているセグメント型ボタン
            // （スキンWペイントのモード／フォールオフ、スキンW数値設定の「色」）も
            // ここで塗り直す。これをしないと起動直後だけ選択状態が消える。
            _skinWeightPaintPanel?.RepaintSegmentButtons();
            _skinWeightNumericSubPanel?.RepaintSegmentButtons();

            WireShortcuts();

            _sectionRefreshPairs.Clear();
            _sectionRefreshPairs.Add((_layoutRoot.BoneEditorSection,        () => _boneEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinWeightNumericSection, () => _skinWeightNumericSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVEditorSection,          () => _uvEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVUnwrapSection,          () => _uvUnwrapSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MaterialListSection,      () => _materialListSubPanel?.Refresh()));
            // 図形生成のマテリアル指定ドロップダウン。生成でスロットを作った直後や、
            // マテリアル一覧側でスロットを増減した後に選択肢を追随させる。
            _sectionRefreshPairs.Add((_layoutRoot.PrimitiveSection,          () => _primitiveSubPanel?.RefreshMaterials()));
            _sectionRefreshPairs.Add((_layoutRoot.LivePrimitiveSection,      () => _livePrimitiveSubPanel?.RefreshMaterials()));
            _sectionRefreshPairs.Add((_layoutRoot.UVZSection,               () => _uvzSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PartsSelectionSetSection, () => _partsSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshSelectionSetSection,  () => _meshSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.NormalExcludeSetSection,  () => _normalExcludeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.NormalEditSection,        () => _normalEditSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceHideSection,          () => _faceHideSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MirrorSection,            () => _mirrorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeMeshesSection,       () => _mergeMeshesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.BooleanSection,           () => _booleanSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphSection,             () => _morphSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphCreateSection,       () => _morphCreateSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.TPoseSection,             () => _tposeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.HumanoidMappingSection,   () => _humanoidMappingSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshFilterToSkinnedSection, () => _mfToSkinnedSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinKindSection, () => _skinKindSubPanel?.SetModel(ActiveProject?.CurrentModel)));
            _sectionRefreshPairs.Add((_layoutRoot.QuadDecimatorSection,         () => _quadDecimatorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AlignVerticesSection,         () => _alignVerticesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PlanarizeAlongBonesSection,   () => _planarizeAlongBonesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SmoothEdgesSection,           () => _smoothEdgesSubPanel?.Refresh()));
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
            _sectionRefreshPairs.Add((_layoutRoot.VertexHoleSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _vertexHoleHandler?.Activate(ctx);
                _vertexHoleSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.VertexDissolveSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _vertexDissolveHandler?.Activate(ctx);
                _vertexDissolveSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.Tri4To1Section, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _tri4To1Handler?.Activate(ctx);
                _tri4To1SubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.FaceMergeSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _faceMergeHandler?.Activate(ctx);
                _faceMergeSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.Quad4To1Section, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _quad4To1Handler?.Activate(ctx);
                _quad4To1SubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.FaceMergeCollapseSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _faceMergeCollapseHandler?.Activate(ctx);
                _faceMergeCollapseSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.VertexIdSection,          () => _vertexIdSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VertexTransferSection,    () => _vertexTransferSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AddFaceSection,           () => _addFaceSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FlipFaceSection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _flipFaceHandler?.Activate(ctx); _flipFaceSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.RotateSection,            () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _rotateHandler?.Activate(ctx); _rotateSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.ScaleSection,             () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _scaleHandler?.Activate(ctx); _scaleSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeBevelSection,         () => _edgeBevelSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeExtrudeSection,       () => _edgeExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceExtrudeSection,       () => _faceExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeTopologySection,      () => _edgeTopologySubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.KnifeSection,             () => _knifeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SolidifySection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _solidifyHandler?.Activate(ctx); _solidifySubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.MediaPipeSection,         () => _mediaPipeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VMDTestSection,           () => _vmdTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PipelineTestSection,      () => _pipelineTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.OriginTestSection,        () => _originTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinTestSection,          () => _skinTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UnityClipTestSection,     () => _unityClipTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MotionClipTestSection,    () => _motionClipTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.RemoteServerSection,      () => _remoteServerSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.LogSection,               () => _logSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.CaptureSection,           () => _captureSubPanel?.Refresh()));

            ShowCategory1Panel(InteractionMode.VertexMove);
        }

        // ================================================================
        // キーボードショートカット配線
        //   対応表: デフォルト (ShortcutMap.CreateDefault) + CSV 上書き
        //           (<persistentDataPath>/PolyLing/keymap.csv、あれば起動時読込)。
        //   コマンド実体はここのボタン用処理を流用する (重複させない)。
        // ================================================================

        private void WireShortcuts()
        {
            // 対応表は CSV 優先。CSV に有効行があれば LoadCsv が内部で既定表を
            // 全破棄して置き換えるため、実効割当は「CSV だけ」か「コードの既定表だけ」
            // のどちらかになり、両者が混ざることはない (詳細は ShortcutMap.cs 冒頭)。
            var map = ShortcutMap.CreateDefault();
            int applied = map.LoadCsv(ShortcutMap.DefaultCsvPath);
            if (applied > 0)
                Debug.Log($"[Shortcut] 対応表を CSV で置換: {applied} 件 ({ShortcutMap.DefaultCsvPath})。"
                        + " CSV に無いコマンドはキー割当なし。");
            else
                Debug.Log("[Shortcut] 対応表はコードの既定表 (ShortcutMap.CreateDefault)。"
                        + $" CSV 未使用 (無し / 有効行 0 / 読取失敗): {ShortcutMap.DefaultCsvPath}");

            _shortcutController = new PlayerShortcutController(map);

            // コマンドID → 実行内容。対応するツールボタンと同じ処理を割り当てる。
            _shortcutController.Register(ShortcutMap.CmdUndo, () => _editOps?.PerformUndo());
            _shortcutController.Register(ShortcutMap.CmdRedo, () => _editOps?.PerformRedo());
            _shortcutController.Register(ShortcutMap.CmdToolVertexMove,
                () => ShowCategory1Panel(InteractionMode.VertexMove));
            _shortcutController.Register(ShortcutMap.CmdToolObjectMove,
                () => { ShowCategory1Panel(InteractionMode.ObjectMove); _boneEditorSubPanel?.ShowObjectPoseTab(); });
            _shortcutController.Register(ShortcutMap.CmdToolSculpt,
                () => ShowCategory1Panel(InteractionMode.Sculpt));
            _shortcutController.Register(ShortcutMap.CmdToolAdvSelect,
                () => ShowCategory1Panel(InteractionMode.AdvancedSelect));
            // C : 回転ツール / Q : 拡大縮小ツール。左ペインのボタン押下と同じ処理。
            _shortcutController.Register(ShortcutMap.CmdToolRotate, ShowRotatePanel);
            _shortcutController.Register(ShortcutMap.CmdToolScale,  ShowScalePanel);

            // 一時選択サブツール (R = 矩形 / G = 投げ縄)。1 回の確定・クリック・Esc で復帰。
            _shortcutController.Register(ShortcutMap.CmdSubToolBoxSelect,
                () => EnterSelectSubTool(false));
            _shortcutController.Register(ShortcutMap.CmdSubToolLassoSelect,
                () => EnterSelectSubTool(true));
            // Delete は面追加モードで点が置かれている間だけ「直前の点の取り消し」に使う。
            // それ以外は従来どおり選択削除。
            _shortcutController.Register(ShortcutMap.CmdSubToolDelete, () =>
            {
                if (_interactionMode == InteractionMode.AddFace &&
                    _addFaceHandler != null && _addFaceHandler.RemoveLastPoint())
                {
                    _addFaceSubPanel?.Refresh();
                    return;
                }
                ExecuteDeleteSelection();
            });
            // D : 面削除モード。面クリックで即削除。Escape で直前のツールへ戻る。
            _shortcutController.Register(ShortcutMap.CmdToolDeleteFace,
                EnterDeleteFaceMode);
            // F : 面追加ツール。左ペインの「面追加」ボタンと同じ処理。
            _shortcutController.Register(ShortcutMap.CmdToolAddFace,
                ShowAddFacePanel);
            // Escape は一時選択サブツールと面削除モードの両方の復帰に使う。
            // どちらも進入中でなければ各メソッドが即 return するため順序は問わない。
            _shortcutController.OnEscape = () => { ExitSelectSubTool(); ExitDeleteFaceMode(); };

            // 選択頂点の結合 (Ctrl+J = 距離無視 / Ctrl+Shift+J = しきい値)。
            // モードを変えない即時実行なので、押した瞬間に結合が完了する。
            _shortcutController.Register(ShortcutMap.CmdMergeVerticesCentroid,
                ExecuteMergeSelectedToCentroid);
            _shortcutController.Register(ShortcutMap.CmdMergeVerticesThreshold,
                ExecuteMergeSelectedByThreshold);

            // 右ペインのオブジェクトリストを開く (Ctrl+O)。ボタン押下と同じ処理。
            _shortcutController.Register(ShortcutMap.CmdPanelMeshList,
                ShowMeshListPanel);

            // 図形生成 (2キー連続 P→形状)。サブメニューを開くだけ (生成はしない)。
            _shortcutController.Register(ShortcutMap.CmdShapeCube,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Cube));
            _shortcutController.Register(ShortcutMap.CmdShapeSphere,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Sphere));
            _shortcutController.Register(ShortcutMap.CmdShapeCylinder,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Cylinder));
            _shortcutController.Register(ShortcutMap.CmdShapeCapsule,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Capsule));
            _shortcutController.Register(ShortcutMap.CmdShapePlane,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Plane));
            _shortcutController.Register(ShortcutMap.CmdShapePyramid,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Pyramid));
            _shortcutController.Register(ShortcutMap.CmdShapeRevolution,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Revolution));
            _shortcutController.Register(ShortcutMap.CmdShapeProfile2D,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Profile2D));
            _shortcutController.Register(ShortcutMap.CmdShapeNohMask,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.NohMask));
            _shortcutController.Register(ShortcutMap.CmdShapeFrill,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Frill));
            _shortcutController.Register(ShortcutMap.CmdShapePipe,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Pipe));
            _shortcutController.Register(ShortcutMap.CmdShapePlaceObject,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.PlaceObject));
            _shortcutController.Register(ShortcutMap.CmdShapeObjectArray,
                () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.ObjectArray));

            // 画面キャプチャ (K M / K T / K W)。
            _shortcutController.Register(ShortcutMap.CmdCaptureMain,
                () => ExecuteCapture(CaptureTarget.MainView));
            _shortcutController.Register(ShortcutMap.CmdCaptureTriView,
                () => ExecuteCapture(CaptureTarget.TriView));
            _shortcutController.Register(ShortcutMap.CmdCaptureWindow,
                () => ExecuteCapture(CaptureTarget.Window));

            _shortcutController.Attach(_uiRoot);
        }

        /// <summary>
        /// 図形生成パネル（オブジェクト接地）の配置元候補。
        /// 現在のモデルの描画オブジェクトを (表示名, MeshObject) で列挙する。
        /// </summary>
        private List<(string Label, MeshObject Mesh)> BuildDrawableMeshList()
        {
            var list = new List<(string, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", mc.MeshObject));
            }
            return list;
        }

        /// <summary>
        /// 図形生成パネル（オブジェクト接地）の配置元候補。
        /// BuildDrawableMeshList と同じ並び・同じ表示名に MasterIndex を足したもの。
        /// 子孫の解決に MasterIndex が要るため、配置元だけこちらを使う。
        /// </summary>
        private List<(string Label, int MasterIndex, MeshObject Mesh)> BuildDrawableMeshEntryList()
        {
            var list = new List<(string, int, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", entry.MasterIndex, mc.MeshObject));
            }
            return list;
        }

        /// <summary>
        /// 面追加パネルの「追加先」候補。BuildDrawableMeshEntryList と同じ並び・表示名で、
        /// MeshObject を持たない項目を除いた (表示名, MasterIndex) を返す。
        /// </summary>
        private List<(string Label, int MasterIndex)> BuildAddFaceMeshEntries()
        {
            var list = new List<(string, int)>();
            foreach (var e in BuildDrawableMeshEntryList())
                list.Add((e.Label, e.MasterIndex));
            return list;
        }

        /// <summary>
        /// 面追加パネルの「マテリアル」候補。スロット順の名前を返す。未設定は "(None)"。
        /// マテリアルリストパネルの表示（PlayerMaterialListSubPanel.MatName）と揃える。
        /// </summary>
        private List<string> BuildAddFaceMaterialNames()
        {
            var list = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            for (int i = 0; i < model.MaterialCount; i++)
            {
                var mat = model.GetMaterial(i);
                list.Add(mat != null ? mat.name : "(None)");
            }
            return list;
        }

        /// <summary>
        /// 指定オブジェクトとその子孫を (MasterIndex, MeshObject) でリスト順に列挙する。
        /// 接地の配置元で、ルートを1つチェックすると子孫も配置元に加わるようにするために使う。
        ///
        /// 結合はしない。各メッシュは自分のローカル座標のまま返すので、
        /// 一覧で子を直接チェックしたときとまったく同じ扱いになる
        /// （配置は rung 中心に各メッシュの原点が乗る）。
        ///
        /// 面を持たないもの（グループ用の空オブジェクト等）は返さない。配置しても
        /// 何も出ないうえ、rung ごとの巡回・抽選の母数に入ると空きが出るため。
        ///
        /// 親子は HierarchyParentIndex で判定する
        /// （ワールド行列の組み立てと同じ基準。ModelContext.ComputeWorldMatrices）。
        /// </summary>
        private List<(int MasterIndex, MeshObject Mesh)> BuildSubtreeMeshList(int rootMasterIndex)
        {
            var list  = new List<(int, MeshObject)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;
            if (rootMasterIndex < 0 || rootMasterIndex >= model.MeshContextCount) return list;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (i != rootMasterIndex && !IsDescendantOf(model, i, rootMasterIndex)) continue;

                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (!IsDrawableMeshContext(mc)) continue;
                if (mc.MeshObject.FaceCount == 0) continue;

                list.Add((i, mc.MeshObject));
            }

            return list;
        }

        /// <summary>index が rootIndex の子孫か。HierarchyParentIndex を辿る。</summary>
        private static bool IsDescendantOf(ModelContext model, int index, int rootIndex)
        {
            int guard = 0;
            int cur = index;
            while (guard++ < 4096)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) return false;

                int parent = mc.HierarchyParentIndex;
                if (parent < 0 || parent >= model.MeshContextCount) return false;
                if (parent == rootIndex) return true;

                cur = parent;
            }
            return false;
        }

        /// <summary>描画オブジェクトか（TypedMeshIndices の Drawable と同じ判定）。</summary>
        private static bool IsDrawableMeshContext(MeshContext mc)
        {
            var t = mc.Type;
            return t == MeshType.Mesh || t == MeshType.BakedMirror || t == MeshType.MirrorSide;
        }

        /// <summary>
        /// 描画オブジェクトの (表示名, MasterIndex) 一覧。
        /// 歪み複製パネルが複製元のチェック一覧と出力先ドロップダウンに使う。
        /// 表示名は BuildDrawableMeshList と同じ形にそろえてある。
        /// </summary>
        private List<(string Label, int MasterIndex)> BuildDrawableIndexList()
        {
            var list = new List<(string, int)>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var entry in model.DrawableMeshes)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc?.MeshObject == null) continue;
                list.Add(($"[{entry.MasterIndex}] {mc.Name ?? "?"}", entry.MasterIndex));
            }
            return list;
        }

        /// <summary>
        /// 現在のモデルにある描画オブジェクト名の一覧。図形生成パネルが
        /// 名前欄の非重複候補を作るのに使う。
        /// </summary>
        private List<string> BuildExistingMeshNames()
        {
            var list = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model == null) return list;

            foreach (var mc in model.MeshContextList)
            {
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                list.Add(mc.Name);
            }
            return list;
        }

        /// <summary>
        /// 図形生成パネルのマテリアル指定ドロップダウン用の表示名一覧。
        /// 添字がそのままマテリアルスロット番号になる。
        /// スロットが 1 つも無いモデルでは空リストを返す
        /// （パネル側が「生成時に作成」表示へ切り替える）。
        /// </summary>
        private List<string> BuildMaterialNames()
        {
            var list  = new List<string>();
            var model = ActiveProject?.CurrentModel;
            if (model?.MaterialReferences == null) return list;

            for (int i = 0; i < model.MaterialReferences.Count; i++)
            {
                var matRef = model.MaterialReferences[i];
                string name = string.IsNullOrEmpty(matRef?.Name) ? "(no name)" : matRef.Name;
                list.Add($"[{i}] {name}");
            }
            return list;
        }

        // ================================================================
        // パネル表示切替
        // ================================================================

        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(_layoutRoot?.ImportSection, null, PanelSelectKeyImport);
            _importSubPanel?.SetMode(mode);
        }

        private void ShowPrimitivePanel()
        {
            // 選択許可チェック（既定 ON なら SelectOnly で開く）。
            // 基本図形/高度な図形は同一 PrimitiveSection を共有し、グリッドだけカテゴリで切替える。
            ShowRightPanelSelectable(
                _layoutRoot?.PrimitiveSection, _layoutRoot?.PrimitiveBtn, PanelSelectKeyPrimitive);
            _primitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Basic);
        }

        private void ShowAdvancedPrimitivePanel()
        {
            // 基本図形と同じセクションを開き、カテゴリのみ高度な図形へ切り替える。
            ShowRightPanelSelectable(
                _layoutRoot?.PrimitiveSection, _layoutRoot?.AdvancedPrimitiveBtn, PanelSelectKeyPrimitive);
            _primitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced);
        }

        private void ShowLivePrimitivePanel()
        {
            // カテゴリ 1: 配置ギズモを使うため InteractionMode を強制する。
            // 他パネルを開けばそちらの SetInteractionMode で自然に抜ける
            // (ShowBoneEditorPanel と同じ方式。前モードの復元機構は持たない)。
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LivePrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Basic);
        }

        private void ShowLiveAdvancedPrimitivePanel()
        {
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LiveAdvancedPrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced);
        }

        /// <summary>
        /// 配置ギズモのサブモードを切り替える（サブツール内の3ボタンから呼ぶ）。
        /// パネル表示や InteractionMode は変更しない。
        /// </summary>
        private void SetPlaceGizmoMode(PrimitivePlaceToolHandler.PlaceGizmoMode mode)
        {
            if (_primitivePlaceHandler != null) _primitivePlaceHandler.Mode = mode;
            RepaintPlaceGizmoButtons();
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// 配置ギズモのサブモードボタンの背景色を現在のモードに合わせる。
        /// ボタン本体はサブツール（3D連携インスタンス）側が持つ。
        /// </summary>
        private void RepaintPlaceGizmoButtons()
        {
            _livePrimitiveSubPanel?.RefreshPlaceGizmoButtons();
        }

        /// <summary>
        /// 配置ギズモの中心（ワールド座標）。
        /// NewObject / NewModel は _worldPos がそのままワールド座標になる。
        /// AddToExisting は追加先メッシュのローカル空間なので WorldMatrix を掛ける。
        /// </summary>
        private Vector3 LivePrimitiveGizmoCenter()
        {
            var pos = _livePrimitiveSubPanel?.PlacePosition ?? Vector3.zero;
            if (_livePrimitiveSubPanel == null ||
                _livePrimitiveSubPanel.CurrentAddMode != PrimitiveAddMode.AddToExisting)
                return pos;

            var mc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            if (mc == null) return pos;
            return mc.WorldMatrix.MultiplyPoint3x4(pos);
        }

        /// <summary>
        /// ギズモが返すワールド差分を _worldPos の空間へ戻す。
        /// AddToExisting のときのみ追加先の WorldMatrixInverse を掛ける。
        /// </summary>
        private Vector3 LivePrimitiveWorldDeltaToLocal(Vector3 worldDelta)
        {
            if (_livePrimitiveSubPanel == null ||
                _livePrimitiveSubPanel.CurrentAddMode != PrimitiveAddMode.AddToExisting)
                return worldDelta;

            var mc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            if (mc == null) return worldDelta;
            return mc.WorldMatrixInverse.MultiplyVector(worldDelta);
        }

        // ショートカット (2キー連続) 用: 図形パネルを開き、指定形状のサブメニューを表示する。
        // 形状ボタンのクリック相当で、生成は行わない。
        private void ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind k)
        {
            if (_primitiveSubPanel == null) return;
            bool advanced =
                _primitiveSubPanel.CategoryOf(k) == PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced;
            ShowRightPanelSelectable(
                _layoutRoot?.PrimitiveSection,
                advanced ? _layoutRoot?.AdvancedPrimitiveBtn : _layoutRoot?.PrimitiveBtn,
                PanelSelectKeyPrimitive);
            _primitiveSubPanel.SelectShape(k);
        }

        private void ShowMeshFilterToSkinnedPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshFilterToSkinnedSection, _layoutRoot?.MeshFilterToSkinnedBtn);
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowSkinKindPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SkinKindSection, _layoutRoot?.SkinKindBtn);
            _skinKindSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowBlendPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BlendSection, _layoutRoot?.BlendBtn);
            _blendSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowShrinkPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ShrinkSection, _layoutRoot?.ShrinkBtn);
            _shrinkSubPanel?.SetModel(ActiveProject?.CurrentModel);
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
            var uvMc    = uvModel?.ActiveMeshContext;
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

        private void ShowNormalExcludeSetPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持
            ShowRightPanel(_layoutRoot?.NormalExcludeSetSection, _layoutRoot?.NormalExcludeSetBtn);
            _normalExcludeSubPanel?.Refresh();
        }

        private void ShowNormalEditPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 選択したまま法線を編集するため、選択モードを変えない。
            ShowRightPanel(_layoutRoot?.NormalEditSection, _layoutRoot?.NormalEditBtn);
            _normalEditSubPanel?.Refresh();
        }

        private void ShowNormalTransplantPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.NormalTransplantSection, _layoutRoot?.NormalTransplantBtn);
            _normalTransplantSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowFaceHidePanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 面を選択したまま隠すため、選択モードを変えない。
            ShowRightPanel(_layoutRoot?.FaceHideSection, _layoutRoot?.FaceHideBtn);
            _faceHideSubPanel?.Refresh();
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

        private void ShowBooleanPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BooleanSection, _layoutRoot?.BooleanBtn);
            _booleanSubPanel?.Refresh();
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
            _mirrorSubPanel?.Refresh();
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

        private void ShowSmoothEdgesPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.SmoothEdgesSection, _layoutRoot?.SmoothEdgesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _smoothEdgesHandler?.Activate(ctx);
            _smoothEdgesSubPanel?.Refresh();
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

        // ================================================================
        // 作業用ローカル軸（WorkAxis）
        // ================================================================

        /// <summary>
        /// 現在のモデルの作業軸。モデル未選択なら null。
        /// ModelContext.WorkAxis は既定でインスタンスを持つが、
        /// 旧データから復元した ModelContext は null のことがあるためここで補う。
        /// </summary>
        private Poly_Ling.Context.WorkAxisContext CurrentWorkAxis()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;
            if (model.WorkAxis == null) model.WorkAxis = new Poly_Ling.Context.WorkAxisContext();
            return model.WorkAxis;
        }

        /// <summary>作業軸ハンドルがボーンへ吸着する当たり半径（px）。</summary>
        private const float WorkAxisBoneSnapRadius = 10f;

        /// <summary>
        /// 作業軸ハンドル（原点 / Y 先端）の吸着先ワールド座標。無ければ null。
        ///
        /// 引数はギズモ判定と同じ ctx 系スクリーン座標（ハンドラ側で ToImgui 済み）。
        /// 頂点は選択されていないオブジェクトも対象にしたいため、通常ホバーではなく
        /// 吸着用 GPU ヒットテスト（GetSnapHoverElement）を使う。座標は GPU が計算した
        /// 表示位置（TryGetVertexWorld）から取る。CPU で WorldMatrix を掛け直すと
        /// スキニング済みメッシュで表示とずれる。
        ///
        /// ボーンは GPU の描画要素ではないので、ボーンオーバーレイと同じく
        /// MeshContext.WorldMatrix の平行移動成分を投影して最近傍を採る。
        /// 走査するのは MeshContext の数だけで、頂点は走査しない。
        /// 頂点が取れたときはそちらを優先する。
        /// </summary>
        private Vector3? WorkAxisSnapTargetWorld(Vector2 imguiPos)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            // ---- 頂点（GPU 吸着ヒットテスト） ----
            var elem = _viewportManager.GetSnapHoverElement(model);
            if (elem.Kind == PlayerHoverKind.Vertex && elem.MeshIndex >= 0)
            {
                var vmc = model.GetMeshContext(elem.MeshIndex);
                if (vmc != null &&
                    _viewportManager.TryGetVertexWorld(model, vmc, elem.VertexIndex, out var vw))
                    return vw;
            }

            // ---- ボーン（CPU 最近傍） ----
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) return null;

            float    best  = WorkAxisBoneSnapRadius;
            Vector3? found = null;

            for (int i = 0; i < model.Count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var     wm = mc.WorldMatrix;
                Vector3 wp = new Vector3(wm.m03, wm.m13, wm.m23);

                float d = Vector2.Distance(imguiPos, ctx.WorldToScreen(wp));
                if (d < best) { best = d; found = wp; }
            }

            return found;
        }

        /// <summary>
        /// 選択中の全ドローアブルメッシュにまたがる選択頂点の重心（ワールド座標）。
        /// 選択が無ければ null。
        ///
        /// 座標は GPU が計算した表示位置（PlayerViewportManager.TryGetVertexWorld →
        /// GetDisplayPositions）から取る。CPU 側で WorldMatrix を掛け直すと
        /// スキニング済みメッシュで GPU 表示とずれるため、独自計算はしない。
        /// </summary>
        private Vector3? SelectedVerticesCentroidWorld()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(meshIdx);
                var mo = mc?.MeshObject;
                if (mo == null || !mc.HasSelection) continue;

                foreach (int vi in EnumerateSelectedVertexIndices(mc, mo))
                {
                    if (_viewportManager.TryGetVertexWorld(model, mc, vi, out var w))
                    {
                        sum += w;
                        count++;
                    }
                }
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        /// <summary>
        /// MeshContext の選択（頂点 / 辺 / 面 / 線分）から影響頂点インデックスを列挙する。
        /// 重複は呼び出し側で気にしなくてよいよう HashSet で畳む。
        /// </summary>
        private static HashSet<int> EnumerateSelectedVertexIndices(
            Poly_Ling.Data.MeshContext mc, Poly_Ling.Data.MeshObject mo)
        {
            var set = new HashSet<int>();
            if (mc == null || mo == null) return set;

            if (mc.SelectedVertices != null)
                foreach (int v in mc.SelectedVertices) set.Add(v);

            if (mc.SelectedEdges != null)
                foreach (var e in mc.SelectedEdges) { set.Add(e.V1); set.Add(e.V2); }

            if (mc.SelectedFaces != null)
                foreach (int fi in mc.SelectedFaces)
                    if (fi >= 0 && fi < mo.FaceCount)
                        foreach (int v in mo.Faces[fi].VertexIndices) set.Add(v);

            if (mc.SelectedLines != null)
                foreach (int li in mc.SelectedLines)
                    if (li >= 0 && li < mo.FaceCount)
                    {
                        var f = mo.Faces[li];
                        if (f.VertexCount == 2)
                        {
                            set.Add(f.VertexIndices[0]);
                            set.Add(f.VertexIndices[1]);
                        }
                    }

            return set;
        }

        /// <summary>
        /// 作業軸パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// </summary>
        private void ShowWorkAxisPanel()
        {
            ShowCategory1Panel(InteractionMode.WorkAxis);
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// デフォーマパネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// 作業軸は「作業軸」パネルと共有するため、ここでは編集しない。
        /// </summary>
        private void ShowDeformPanel()
        {
            ShowCategory1Panel(InteractionMode.Deform);
            UpdateGizmoOverlay();
        }

        /// <summary>
        /// 格子変形パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// 格子フレームは「作業軸」パネルと共有するため、ここでは編集しない。
        /// </summary>
        private void ShowLatticePanel()
        {
            ShowCategory1Panel(InteractionMode.Lattice);
            UpdateTopologyToolsOverlay();
            UpdateGizmoOverlay();
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

        private void ShowSolidifyPanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用し、Selection.Mode を
            // Face のみに絞る。厚み付けの実行はサブパネル経由。
            ShowCategory1Panel(InteractionMode.Solidify);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _solidifyHandler?.Activate(ctx);
        }

        private void ShowKnifePanel()
        {
            ShowCategory1Panel(InteractionMode.Knife);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _knifeHandler?.Activate(ctx);
            // Activate は SetInteractionMode の後に走り、段（開始頂点/セグメント辺）を
            // 初期化し得る。初期段に合わせて override を確定させる。
            _knifeHandler?.ApplyHoverSelectionMode();
        }
        private void ShowAddFacePanel()
        {
            ShowCategory1Panel(InteractionMode.AddFace);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _addFaceHandler?.Activate(ctx);
            // 面追加時は頂点ホバーのみ必要（辺・面のホバーは有害）。
            // 絞り込みは ShowCategory1Panel → SetInteractionMode(AddFace) →
            // ResolveToolSelectModeOverride が行うため、ここでは書かない。
        }

        private void ShowSplitVerticesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.SplitVerticesSection, _layoutRoot?.SplitVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _splitVerticesHandler?.Activate(ctx);
            _splitVerticesSubPanel?.Refresh();
        }

        private void ShowVertexHolePanel()
        {
            // 選択許可チェック（既定 ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.VertexHoleSection, _layoutRoot?.VertexHoleBtn, PanelSelectKeyVertexHole);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _vertexHoleHandler?.Activate(ctx);
            _vertexHoleSubPanel?.Refresh();
        }

        // カテゴリ 1（3D 操作と右ペインが一体）。頂点／面／辺のクリックで即実行する
        // 専用モードへ入る。Selection.Mode の固定とフック設定は SetInteractionMode 側。
        private void ShowVertexDissolvePanel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _vertexDissolveHandler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.VertexDissolve);
        }

        private void ShowTri4To1Panel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _tri4To1Handler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.Tri4To1);
        }

        private void ShowFaceMergePanel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceMergeHandler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.FaceMerge);
        }

        private void ShowQuad4To1Panel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _quad4To1Handler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.Quad4To1);
        }

        private void ShowFaceMergeCollapsePanel()
        {
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceMergeCollapseHandler?.Activate(ctx);
            ShowCategory1Panel(InteractionMode.FaceMergeCollapse);
        }

        private void ShowVertexIdPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 診断・修復は選択中オブジェクトに対する即時操作で、ビューポート入力は使わない。
            ShowRightPanel(_layoutRoot?.VertexIdSection, _layoutRoot?.VertexIdBtn);
            _vertexIdSubPanel?.Refresh();
        }

        private void ShowVertexTransferPanel()
        {
            // カテゴリ 3: モデル間の操作でビューポート入力を使わないため 3D 操作は落とす。
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VertexTransferSection, _layoutRoot?.VertexTransferBtn);
            _vertexTransferSubPanel?.Refresh();
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

        private void ShowPipelineTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.PipelineTestSection, _layoutRoot?.PipelineTestBtn);
            _pipelineTestSubPanel?.Refresh();
        }

        private void ShowOriginTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.OriginTestSection, _layoutRoot?.OriginTestBtn);
            _originTestSubPanel?.Refresh();
        }

        private void ShowSkinTestPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SkinTestSection, _layoutRoot?.SkinTestBtn);
            _skinTestSubPanel?.Refresh();
        }

        /// <summary>
        /// 自動検証用のプロジェクトフォルダ読み込み。
        /// 通常の読み込みと同じ経路（CsvProjectSerializer.Import → _localLoader）を通す。
        /// </summary>
        private bool LoadProjectFolderForTest(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;

            var loaded = CsvProjectSerializer.Import(folderPath, out _, out _);
            if (loaded == null) return false;

            _localLoader.Clear();
            foreach (var m in loaded.Models)
                _localLoader.LoadModel(m.FilePath ?? loaded.Name, m);
            AdoptWorkAxisLibrary(loaded);
            return true;
        }

        /// <summary>
        /// 自動検証用のブリッジ生成。UI ボタンと同じ経路
        /// （選択 → 穴A 取り込み → 穴B 取り込み → 生成）を通す。
        /// 頂点は呼び出し側が決めた「エッジ上の 1 頂点」を使う。
        /// </summary>
        private bool CreateBridgeForTest(
            int meshA, int vertexA, int meshB, int vertexB, string name, out string message)
        {
            message = "";

            var model = ActiveProject?.CurrentModel;
            var panel = _primitiveSubPanel;
            if (model == null || panel == null) { message = "モデルかパネルが無い"; return false; }

            panel.ClearBridgeSeeds();
            panel.SetBridgeName(name);

            if (!SelectSingleVertexForTest(model, meshA, vertexA)) { message = "穴A の選択に失敗"; return false; }
            if (!panel.ImportBridgeSeedA()) { message = "穴A 取り込み失敗: " + panel.BridgeSeedInfoA; return false; }

            if (!SelectSingleVertexForTest(model, meshB, vertexB)) { message = "穴B の選択に失敗"; return false; }
            if (!panel.ImportBridgeSeedB()) { message = "穴B 取り込み失敗: " + panel.BridgeSeedInfoB; return false; }

            panel.GenerateBridge();
            return true;
        }

        /// <summary>
        /// 指定メッシュだけを選択し、その頂点を 1 個だけ選択状態にする。
        /// PickBridgeSeeds は穴ごとに 1 つ拾うので複数選択でも通るが、
        /// 検証では拾われる種を一意にしたいので他の選択は全部落とす。
        /// </summary>
        private bool SelectSingleVertexForTest(ModelContext model, int meshIndex, int vertex)
        {
            var mc = model?.GetMeshContext(meshIndex);
            if (mc?.MeshObject == null) return false;
            if (vertex < 0 || vertex >= mc.MeshObject.VertexCount) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
                model.GetMeshContext(i)?.Selection?.ClearAll();

            model.SelectedDrawableMeshIndices = new List<int> { meshIndex };
            mc.Selection.Vertices.Add(vertex);
            return true;
        }

        /// <summary>自動検証用のプロジェクトフォルダ保存。</summary>
        private bool SaveProjectFolderForTest(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            var project = ActiveProject;
            if (project == null) return false;
            return CsvProjectSerializer.Export(folderPath, project);
        }

        private void ShowUnityClipTestPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnityClipTestSection, _layoutRoot?.UnityClipTestBtn);
            _unityClipTestSubPanel?.Refresh();
        }

        private void ShowMotionClipTestPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MotionClipTestSection, _layoutRoot?.MotionClipTestBtn);
            _motionClipTestSubPanel?.Refresh();
        }

        private void ShowRemoteServerPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.RemoteServerSection, _layoutRoot?.RemoteServerBtn);
            _remoteServerSubPanel?.Refresh();
        }

        private void ShowLogPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.LogSection, _layoutRoot?.LogBtn);
            _logSubPanel?.Refresh();
        }

        private void ShowUnderlayPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UnderlaySection, _layoutRoot?.UnderlayBtn);
            _underlayActive = true;   // 左ドラッグでオフセット移動を有効化
        }

        private void ShowGridAxisPanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.GridAxisSection, _layoutRoot?.GridAxisBtn);
            _gridAxisSubPanel?.Refresh();
        }

        // ================================================================
        // 画面キャプチャ
        // ================================================================

        private void ShowCapturePanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.CaptureSection, _layoutRoot?.CaptureBtn);
            _captureSubPanel?.Refresh();
        }

        /// <summary>
        /// 画面キャプチャを実行する。パネルボタンとショートカットの共通入口。
        /// ファイル名・保存フォルダはパネル未表示でも効くよう RecentPaths から読む。
        /// </summary>
        private void ExecuteCapture(CaptureTarget target)
        {
            VisualElement crop = null;
            switch (target)
            {
                case CaptureTarget.MainView: crop = _layoutRoot?.PerspectivePanel; break;
                case CaptureTarget.TriView:  crop = _layoutRoot?.ViewportArea;     break;
                case CaptureTarget.Window:   crop = null;                          break;
            }

            PlayerScreenCapture.Capture(
                crop,
                PlayerCaptureSubPanel.GetFolder(),
                PlayerCaptureSubPanel.GetFileName(),
                (ok, msg) => _captureSubPanel?.SetStatus(ok ? $"保存しました: {msg}" : $"失敗: {msg}"));
        }

        // ================================================================
        // カメラ調整
        // ================================================================

        /// <summary>
        /// カメラ調整パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// ギズモは調整対象と逆側のビューポートに出る（CameraToolHandler 側で判定）。
        /// </summary>
        private void ShowCameraPanel()
        {
            ShowCategory1Panel(InteractionMode.Camera);
            UpdateGizmoOverlay();
        }

        /// <summary>3面ビューポートを index (0=Top / 1=Front / 2=Side) で引く。</summary>
        private PlayerViewport TriViewportOf(int index)
        {
            switch (index)
            {
                case 0:  return _viewportManager.TopViewport;
                case 1:  return _viewportManager.FrontViewport;
                case 2:  return _viewportManager.SideViewport;
                default: return null;
            }
        }

        /// <summary>3面のフリップを適用する。ビューポートヘッダのボタンと同じ経路。</summary>
        private void ApplyTriFlip(int index, bool flipped)
        {
            switch (index)
            {
                case 0: _setTopFlip  ?.Invoke(flipped); break;
                case 1: _setFrontFlip?.Invoke(flipped); break;
                case 2: _setSideFlip ?.Invoke(flipped); break;
            }
        }

        /// <summary>
        /// カメラ調整ツールが変更したカメラを再描画する。
        /// 3面は共有状態のため代表として Front を渡す（連動 slot は
        /// PlayerViewportManager 側で同期される）。
        /// </summary>
        private void NotifyCameraToolChanged(CameraChangePhase phase)
        {
            bool tri = _cameraHandler != null
                && _cameraHandler.TargetKind == CameraToolHandler.CameraTargetKind.Tri;

            var vp = tri ? _viewportManager.FrontViewport
                         : _viewportManager.PerspectiveViewport;
            if (vp == null) return;

            _viewportManager.EnterCameraChanged(vp, phase);
        }

        /// <summary>
        /// メインカメラの正投影切替。ビューポートヘッダのトグルと
        /// カメラ調整パネルのトグルを同じ経路に集約する。
        /// </summary>
        private void SetMainCameraOrthographic(bool ortho)
        {
            var vp = _viewportManager.PerspectiveViewport;
            if (vp?.Orbit == null) return;

            vp.Orbit.Orthographic = ortho;
            _layoutRoot?.PerspOrthoToggle?.SetValueWithoutNotify(ortho);
            // 方向（persp/ortho）に応じた下絵へ差し替え＋再描画。
            ApplyUnderlayToViewport(vp, _layoutRoot?.PerspectivePanel);
            _cameraSubPanel?.Refresh();
        }

        /// <summary>メインカメラの視線を反転する（Target を挟んで反対側へ回り込む）。</summary>
        private void FlipMainCameraView()
        {
            var vp = _viewportManager.PerspectiveViewport;
            if (vp?.Orbit == null) return;

            vp.Orbit.FlipView();
            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
            _cameraSubPanel?.Refresh();
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
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            Button btn;
            switch (mode)
            {
                case PlayerExportSubPanel.Mode.PMX: btn = _layoutRoot?.FullExportPmxBtn; break;
                case PlayerExportSubPanel.Mode.OBJ: btn = _layoutRoot?.ObjSaveBtn;       break;
                case PlayerExportSubPanel.Mode.VRM: btn = _layoutRoot?.FullExportVrmBtn; break;
                default:                            btn = _layoutRoot?.FullExportMqoBtn; break;
            }
            ShowRightPanelSelectable(_layoutRoot?.ExportSection, btn, PanelSelectKeyExport);
            _exportSubPanel?.SetMode(mode);
        }

        private void ShowProjectSavePanel()
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.ProjectSaveSection, _layoutRoot?.ProjectSaveBtn, PanelSelectKeyProjectSave);
            // もう一方のパネルで変更されたパスを取り込む（両者は RecentPaths を共有）。
            _projectSaveSubPanel?.Refresh();
        }

        private void ShowProjectLoadPanel()
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.ProjectLoadSection, _layoutRoot?.ProjectLoadBtn, PanelSelectKeyProjectLoad);
            _projectLoadSubPanel?.Refresh();
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
            // カテゴリ 3 + 選択許可チェック（既定 ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.MeshListSection, _layoutRoot?.MeshListBtn, PanelSelectKeyMeshList);
        }

        private void HideAllRightPanels()
        {
            if (_layoutRoot == null) return;
            void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }
            Hide(_layoutRoot.ModelListSection);
            Hide(_layoutRoot.MeshListSection);
            Hide(_layoutRoot.SkinWeightPaintSection);
            Hide(_layoutRoot.SkinWeightNumericSection);
            Hide(_layoutRoot.VertexMoveSection);
            Hide(_layoutRoot.PivotSection);
            Hide(_layoutRoot.SculptSection);
            Hide(_layoutRoot.AdvancedSelectSection);
            Hide(_layoutRoot.ImportSection);
            Hide(_layoutRoot.ExportSection);
            Hide(_layoutRoot.ProjectSaveSection);
            Hide(_layoutRoot.ProjectLoadSection);
            Hide(_layoutRoot.PartialImportSection);
            Hide(_layoutRoot.PartialExportSection);
            Hide(_layoutRoot.PrimitiveSection);
            Hide(_layoutRoot.LivePrimitiveSection);
            Hide(_layoutRoot.MeshFilterToSkinnedSection);
            Hide(_layoutRoot.SkinKindSection);
            // メッシュブレンドのプレビュー結果は MeshObject に書かれているため、
            // 非表示にするだけでは未確定の形状が残ったままになる。
            _blendSubPanel?.CancelIfActive();
            Hide(_layoutRoot.BlendSection);
            Hide(_layoutRoot.ShrinkSection);
            Hide(_layoutRoot.NormalTransplantSection);
            Hide(_layoutRoot.ModelBlendSection);
            Hide(_layoutRoot.BoneEditorSection);
            Hide(_layoutRoot.UVEditorSection);
            Hide(_layoutRoot.UVUnwrapSection);
            Hide(_layoutRoot.MaterialListSection);
            Hide(_layoutRoot.UVZSection);
            Hide(_layoutRoot.PartsSelectionSetSection);
            Hide(_layoutRoot.MeshSelectionSetSection);
            Hide(_layoutRoot.MergeMeshesSection);
            Hide(_layoutRoot.BooleanSection);
            Hide(_layoutRoot.MorphSection);
            Hide(_layoutRoot.MorphCreateSection);
            Hide(_layoutRoot.TPoseSection);
            Hide(_layoutRoot.HumanoidMappingSection);
            Hide(_layoutRoot.MirrorSection);
            Hide(_layoutRoot.QuadDecimatorSection);
            Hide(_layoutRoot.AlignVerticesSection);
            Hide(_layoutRoot.PlanarizeAlongBonesSection);
            Hide(_layoutRoot.SmoothEdgesSection);
            Hide(_layoutRoot.MergeVerticesSection);
            Hide(_layoutRoot.SplitVerticesSection);
            Hide(_layoutRoot.VertexHoleSection);
            Hide(_layoutRoot.VertexDissolveSection);
            Hide(_layoutRoot.Tri4To1Section);
            Hide(_layoutRoot.FaceMergeSection);
            Hide(_layoutRoot.Quad4To1Section);
            Hide(_layoutRoot.FaceMergeCollapseSection);
            Hide(_layoutRoot.VertexIdSection);
            Hide(_layoutRoot.VertexTransferSection);
            Hide(_layoutRoot.AddFaceSection);
            Hide(_layoutRoot.FlipFaceSection);
            Hide(_layoutRoot.RotateSection);
            Hide(_layoutRoot.WorkAxisSection);
            Hide(_layoutRoot.DeformSection);
            Hide(_layoutRoot.LatticeSection);
            Hide(_layoutRoot.ScaleSection);
            Hide(_layoutRoot.EdgeBevelSection);
            Hide(_layoutRoot.EdgeExtrudeSection);
            Hide(_layoutRoot.FaceExtrudeSection);
            Hide(_layoutRoot.EdgeTopologySection);
            Hide(_layoutRoot.KnifeSection);
            Hide(_layoutRoot.SolidifySection);
            Hide(_layoutRoot.MediaPipeSection);
            Hide(_layoutRoot.VMDTestSection);
            Hide(_layoutRoot.UnityClipTestSection);
            Hide(_layoutRoot.MotionClipTestSection);
            Hide(_layoutRoot.RemoteServerSection);
            Hide(_layoutRoot.LogSection);
            Hide(_layoutRoot.UnderlaySection);
            Hide(_layoutRoot.GridAxisSection);
            Hide(_layoutRoot.CameraSection);
            Hide(_layoutRoot.CaptureSection);
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

        // 非 active 色 (既存)。PlayerLayoutRoot.ApplyDarkTheme が入れる値と同一。
        private static readonly StyleColor InactiveBtnColor         = PlayerLayoutRoot.BtnInactiveColor;
        // InteractionMode のみ active (青)
        private static readonly StyleColor InteractionActiveBtnColor = PlayerLayoutRoot.BtnActiveColor;
        // RightPanel のみ active (緑系)
        private static readonly StyleColor PanelActiveBtnColor       = new StyleColor(new Color(0.3f,  0.75f, 0.4f));
        // 両方 active (α: 混色の青緑)
        private static readonly StyleColor BothActiveBtnColor        = new StyleColor(new Color(0.3f,  0.625f, 0.7f));

        // 旧 _activeBtn を 2 つに分割
        private Button _activeInteractionBtn;   // InteractionMode を示すボタン
        private Button _activePanelBtn;         // 現在開いている RightPanel を示すボタン
        private VisualElement _activeRightSection;  // 現在開いている RightPanel のセクション

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
                case InteractionMode.SkinWeightNumeric: return _layoutRoot.SkinWeightNumericBtn;
                case InteractionMode.DeleteFace:      return _layoutRoot.ToolDeleteFaceBtn;
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
                Add(_layoutRoot.SkinWeightNumericBtn);
                Add(_layoutRoot.ToolDeleteFaceBtn);
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
            _activeRightSection = section;
            SetActivePanelButton(panelBtn);
            PLPerfLog.SetPanel(panelBtn?.text);
        }

        /// <summary>
        /// パネルごとの「ビューポートで選択する」チェックをセクションへ差し込む。
        /// サブパネルの Build 直後に1回だけ呼ぶ。チェックの変更は、そのパネルを
        /// 開いている間だけ InteractionMode へ即時反映する。
        /// </summary>
        private void AttachPanelSelectToggle(VisualElement section, string key)
        {
            PanelSelectToggle.Attach(section, key, on =>
            {
                if (section == null || _activeRightSection != section) return;
                SetInteractionMode(on ? InteractionMode.SelectOnly : InteractionMode.None);
            });
        }

        /// <summary>
        /// カテゴリ3のパネルを、選択許可チェック付きで開く。
        /// ON なら SelectOnly（移動ギズモなしの選択のみ）、OFF なら None（3D操作無効）。
        /// </summary>
        private void ShowRightPanelSelectable(VisualElement section, Button panelBtn, string key)
        {
            SetInteractionMode(
                PanelSelectToggle.IsEnabled(key) ? InteractionMode.SelectOnly : InteractionMode.None);
            ShowRightPanel(section, panelBtn);
        }

        // ================================================================
        // コールバック / イベントハンドラ
        // ================================================================

        /// <summary>
        /// 格子変形モードのビューポート入力先を格子の状態で切り替える。
        ///
        ///   Idle / Placement … MoveToolHandler(SelectOnly) へ流し、メッシュ頂点を選び直せる。
        ///                       選び直した後は「選択フィット」で格子を合わせ直す。
        ///   Deform          … LatticeToolHandler へ流し、格子点を選択・操作する。
        ///
        /// LatticeToolHandler.OnStateChanged からも呼ぶため、格子変形モード以外では何もしない。
        /// </summary>
        private void ApplyLatticeToolRouting()
        {
            if (_interactionMode != InteractionMode.Lattice) return;

            bool deform = _latticeHandler != null
                && _latticeHandler.State == LatticeToolHandler.LatticeState.Deform;

            if (deform)
            {
                if (_moveToolHandler != null) _moveToolHandler.SelectOnly = false;
                _vertexInteractor?.SetToolHandler(_latticeHandler);
                _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _latticeHandler?.UpdateHover(pos, ctx));
                return;
            }

            // 選択専用。組み込み移動ギズモは出さない（InteractionMode.Deform と同じ方式）。
            if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
            _vertexInteractor?.SetToolHandler(_moveToolHandler);
            _viewportManager?.RegisterActiveToolHandler(null);
        }

        // ================================================================
        // 歪み複製
        // ================================================================

        /// <summary>
        /// 歪み複製の「生成」。複製元リストを p.Count 組ぶん複製し、
        /// 各組へ歪みを掛けて出力先へ入れる。元のオブジェクトには触らない。
        ///
        /// 呼び元は図形生成パネル（高度な図形 / 新しい高度）の生成ボタン。
        /// パネルは2インスタンスあるため、状態は引数で受け取る。
        ///
        /// 生成そのものは ObjectArrayGenerator、挿入は ObjectArrayInserter が持つ。
        /// ここは Undo 記録とビュー更新だけを担う。
        /// </summary>
        private void ExecuteObjectArray(PlayerObjectArraySubPanel panel)
        {
            if (panel == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.SetStatus("モデルがありません"); return; }

            var axis = CurrentWorkAxis();
            if (axis == null) { panel.SetStatus("作業軸がありません"); return; }

            var deformer = panel.Deformer;
            if (deformer == null) { panel.SetStatus("歪みが選ばれていません"); return; }

            var p = panel.Params;
            if (p.Count < 1) { panel.SetStatus("組の数は 1 以上にしてください"); return; }

            var sources = ObjectArrayGenerator.BuildSources(model, panel.SelectedMasterIndices());
            if (sources.Count == 0) { panel.SetStatus("複製元が選ばれていません"); return; }

            // 「中に生成」で出力先がルートのときは、新規オブジェクトを1つ作って
            // そこへ入れる。その新規オブジェクトはルート直下なので変換は単位。
            bool insideRoot = p.OutputMode == ObjectArrayOutputMode.Inside
                              && ObjectArrayInserter.ResolveTarget(model, p.TargetMasterIndex) == null;

            Matrix4x4 worldToOutputLocal = insideRoot
                ? Matrix4x4.identity
                : ObjectArrayInserter.GetWorldToOutputLocal(model, p.TargetMasterIndex);

            var pieces = ObjectArrayGenerator.Generate(sources, axis, deformer, p, worldToOutputLocal);
            if (pieces.Count == 0) { panel.SetStatus("生成できませんでした"); return; }

            if (p.OutputMode == ObjectArrayOutputMode.Inside)
                ObjectArrayApplyInside(model, pieces, p, insideRoot, panel);
            else
                ObjectArrayApplyAsChildren(model, pieces, p, panel);
        }

        /// <summary>
        /// モード1: 出力先の子として、生成物ごとに描画オブジェクトを作る。
        /// </summary>
        private void ObjectArrayApplyAsChildren(
            ModelContext model, List<ObjectArrayPiece> pieces,
            ObjectArrayParams p, PlayerObjectArraySubPanel panel)
        {
            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectArrayInserter.InsertAsChildren(
                model, pieces, p.TargetMasterIndex, model.GenerateUniqueMeshName);

            if (added.Count == 0) { panel.SetStatus("生成できませんでした"); return; }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);
            panel.SetStatus($"{added.Count} 個のオブジェクトを生成しました");
        }

        /// <summary>
        /// モード2: 出力先の頂点・面へ統合する。
        /// 出力先がルートのときは統合先が無いので新規オブジェクトを1つ作る。
        /// </summary>
        private void ObjectArrayApplyInside(
            ModelContext model, List<ObjectArrayPiece> pieces,
            ObjectArrayParams p, bool insideRoot, PlayerObjectArraySubPanel panel)
        {
            if (insideRoot)
            {
                // 全部を1つのメッシュへまとめ、新規オブジェクトとしてルートへ置く。
                string baseName = string.IsNullOrEmpty(p.NameBase) ? "ObjectArray" : p.NameBase;
                var combined = ObjectArrayInserter.CombineAll(pieces, baseName);

                var single = new ObjectArrayPiece
                {
                    Mesh          = combined,
                    Name          = baseName,
                    RelativeDepth = 0,
                    CopyIndex     = 0,
                };

                var oldSelected = model.CaptureAllSelectedIndices();

                var added = ObjectArrayInserter.InsertAsChildren(
                    model, new List<ObjectArrayPiece> { single }, -1, model.GenerateUniqueMeshName);

                if (added.Count == 0) { panel.SetStatus("生成できませんでした"); return; }

                model.ComputeWorldMatrices();

                model.ClearMeshSelection();
                foreach (var e in added) model.AddToMeshSelection(e.Index);
                var newSelected = model.CaptureAllSelectedIndices();

                if (_editOps?.UndoController != null)
                {
                    _editOps.UndoController.SetModelContext(model);
                    _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
                }

                PrimitiveMeshFinalize(model);
                panel.SetStatus($"新規オブジェクトへ {pieces.Count} 組ぶんを統合しました");
                return;
            }

            var targetMc = ObjectArrayInserter.ResolveTarget(model, p.TargetMasterIndex);
            if (targetMc?.MeshObject == null) { panel.SetStatus("出力先が見つかりません"); return; }

            // UNDO: 変更前スナップショット（図形生成の AddToExisting と同じ経路）
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            ObjectArrayInserter.AppendInto(targetMc.MeshObject, pieces);

            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Object Array into {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
            panel.SetStatus($"{targetMc.Name} へ {pieces.Count} 個ぶんを統合しました");
        }

        // ================================================================
        // 穴つなぎ（ブリッジ）
        // ================================================================

        /// <summary>
        /// 穴つなぎのコールバックを図形生成サブパネルへ配線する。
        /// 2つのインスタンスとも同じ処理を通す（状態はサブパネル側が個別に持つ）。
        /// </summary>
        private void WireBridgeCallbacks(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            panel.PickBridgeSeeds      = PickBridgeSeeds;
            panel.GetMeshObjectAt      = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.MeshObject;
            panel.GetMeshNameAt        = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.Name ?? $"#{idx}";
            // 頂点をワールドへ出す行列。スキンドは頂点が既にワールド（バインド）空間で、
            // かつ WorldMatrix は親ボーンのワールド行列なので、掛けると位置が飛ぶ。
            // 判定は MeshContext.IsSkinned に集約してある。
            panel.GetMeshWorldMatrixAt = idx =>
                ActiveProject?.CurrentModel?.GetMeshContext(idx)?.VertexToWorldMatrix ?? Matrix4x4.identity;
            // 自動選択の対象。頂点選択ではなく「選択中の描画オブジェクト」を見る。
            // 2つなら別々の物体、1つならその物体内の2つの穴が対象になる。
            panel.GetBridgeAutoMeshIndices = () =>
                ActiveProject?.CurrentModel?.SelectedDrawableMeshIndices;
            panel.OnBridgeGenerate     = ExecuteBridge;
            // 種の取り込み・破棄・図形切替でマーカーを即時更新する。
            // （視点変更・ホバー変更では PlayerViewportManager.RefreshToolOverlays が拾う）
            panel.OnBridgeSeedsChanged = UpdateTopologyToolsOverlay;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、穴（エッジグループ）ごとに種を 1 つ拾う。
        /// 最大 2 件で打ち切る。範囲選択などで 1 つの穴に多数の頂点が入っていても、
        /// その穴からは 1 つだけを採る。
        ///
        /// 【拾えなかったとき】
        /// 空リストではなく Ok=false の要素を 1 つだけ入れて返す。理由をパネルの
        /// 情報欄へそのまま出すため。
        ///
        /// 【並びを固定する理由】
        /// SelectionState.Vertices / Edges は HashSet で列挙順が保証されない。
        /// 同じ選択で毎回同じ種が拾えるよう、頂点番号の昇順に並べ直してから走査する。
        /// </summary>
        private List<PlayerPrimitiveMeshSubPanel.BridgeSeedPick> PickBridgeSeeds()
        {
            var picks = new List<PlayerPrimitiveMeshSubPanel.BridgeSeedPick>();

            PlayerPrimitiveMeshSubPanel.BridgeSeedPick Fail(string message)
                => new PlayerPrimitiveMeshSubPanel.BridgeSeedPick
                {
                    Ok = false, Message = message, MeshIndex = -1, Vertex = -1, DirectionHint = -1,
                };

            var model = ActiveProject?.CurrentModel;
            if (model == null) { picks.Add(Fail("モデルがありません")); return picks; }

            bool sawSelection = false;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                if (picks.Count >= 2) break;

                var mc  = model.GetMeshContext(idx);
                var mo  = mc?.MeshObject;
                var sel = mc?.Selection;
                if (mo == null || sel == null) continue;
                if (sel.Vertices.Count == 0 && sel.Edges.Count == 0) continue;
                sawSelection = true;

                // 穴の表はメッシュごとに 1 回だけ作る（頂点 → エッジグループ番号）。
                var groups = BoundaryEdgeOps.BuildGroups(BoundaryEdgeOps.CollectBoundaryEdges(mo));
                if (groups.Count == 0) continue;

                var groupOf = new Dictionary<int, int>();
                for (int g = 0; g < groups.Count; g++)
                    foreach (var be in groups[g])
                    {
                        if (!groupOf.ContainsKey(be.V1)) groupOf[be.V1] = g;
                        if (!groupOf.ContainsKey(be.V2)) groupOf[be.V2] = g;
                    }

                // 候補（種頂点, 進行方向ヒント）。辺は V1 を種、V2 を方向にする
                // （VertexPair は V1 <= V2 に正規化済みなので毎回同じ向きになる）。
                var cands = new List<(int Vertex, int Hint)>();
                foreach (int v in sel.Vertices) cands.Add((v, -1));
                foreach (var e in sel.Edges)    cands.Add((e.V1, e.V2));
                cands.Sort((a, b) => a.Vertex != b.Vertex
                    ? a.Vertex.CompareTo(b.Vertex)
                    : a.Hint.CompareTo(b.Hint));

                var takenGroups = new HashSet<int>();
                foreach (var c in cands)
                {
                    if (picks.Count >= 2) break;
                    if (c.Vertex < 0 || c.Vertex >= mo.VertexCount) continue;
                    if (!groupOf.TryGetValue(c.Vertex, out int gi)) continue;  // エッジ上にない頂点
                    if (!takenGroups.Add(gi)) continue;                        // その穴は採用済み

                    // 方向ヒントは同じ穴の頂点のときだけ活かす。
                    int hint = -1;
                    if (c.Hint >= 0 && groupOf.TryGetValue(c.Hint, out int gh) && gh == gi)
                        hint = c.Hint;

                    picks.Add(new PlayerPrimitiveMeshSubPanel.BridgeSeedPick
                    {
                        Ok = true, MeshIndex = idx, Vertex = c.Vertex, DirectionHint = hint,
                    });
                }
            }

            if (picks.Count == 0)
                picks.Add(Fail(sawSelection
                    ? "選択はエッジ（1面だけが使う辺）の上にありません"
                    : "エッジ上の頂点または辺を選択してください"));

            return picks;
        }

        // ================================================================
        // 穴つなぎ（ブリッジ）の種マーカー
        // ================================================================

        /// <summary>穴A の種マーカー色。</summary>
        private static readonly Color BridgeSeedColorA = new Color(0.20f, 0.90f, 1.00f);
        /// <summary>穴B の種マーカー色。</summary>
        private static readonly Color BridgeSeedColorB = new Color(1.00f, 0.35f, 0.90f);

        /// <summary>
        /// 穴つなぎのマーカーを描くべきサブパネルを返す。
        /// 図形生成パネルが表示中で、かつ選んでいる図形が「ブリッジ」のときだけ返す。
        /// </summary>
        private PlayerPrimitiveMeshSubPanel ActiveBridgeSeedPanel()
        {
            if (_livePrimitiveSubPanel != null && _livePrimitiveSubPanel.BridgeOverlayActive)
                return _livePrimitiveSubPanel;
            if (_primitiveSubPanel != null && _primitiveSubPanel.BridgeOverlayActive)
                return _primitiveSubPanel;
            return null;
        }

        /// <summary>
        /// 取り込み済みの種 A / B を色分けしてビューポートへ出す。
        /// 描けたら true、対象外なら false（呼出し側が他の分岐へ進む）。
        ///
        /// 【座標】頂点のワールド座標は GPU の値（TryGetVertexWorld → GetDisplayPositions）
        /// を使う。スキニング規則を CPU で計算し直すと描画位置と食い違う。
        /// </summary>
        private bool UpdateBridgeSeedOverlay(PlayerViewportPanel panel, Poly_Ling.Tools.ToolContext ctx)
        {
            var bridgePanel = ActiveBridgeSeedPanel();
            if (bridgePanel == null) return false;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideTopoToolOverlay(); return true; }

            float h = ctx.PreviewRect.height;

            Vector2? SeedToScreen(int meshIndex, int vertex)
            {
                if (meshIndex < 0 || vertex < 0) return null;
                var mc = model.GetMeshContext(meshIndex);
                if (mc?.MeshObject == null) return null;
                if (!_viewportManager.TryGetVertexWorld(model, mc, vertex, out var wp)) return null;
                var sp = ctx.WorldToScreen(wp);
                return new Vector2(sp.x, h - sp.y);
            }

            var lines  = new List<(Vector2, Vector2, Color)>();
            var points = new List<(Vector2, Color, float)>();
            var rings  = new List<(Vector2, Color, float)>();

            void AddSeed(int meshIndex, int vertex, int hint, Color col)
            {
                var p = SeedToScreen(meshIndex, vertex);
                if (!p.HasValue) return;

                points.Add((p.Value, col, 6f));
                rings.Add((p.Value, col, 10f));

                // 辺で取り込んだ種は、進行方向側の頂点への線も同色で引く。
                if (hint < 0) return;
                var q = SeedToScreen(meshIndex, hint);
                if (q.HasValue) lines.Add((p.Value, q.Value, col));
            }

            AddSeed(bridgePanel.BridgeSeedMeshIndexA, bridgePanel.BridgeSeedVertexA,
                    bridgePanel.BridgeSeedDirHintA, BridgeSeedColorA);
            AddSeed(bridgePanel.BridgeSeedMeshIndexB, bridgePanel.BridgeSeedVertexB,
                    bridgePanel.BridgeSeedDirHintB, BridgeSeedColorB);

            if (points.Count == 0) panel.HideTopoToolOverlay();
            else                   panel.UpdateTopoToolOverlay(lines, points, rings);
            return true;
        }

        /// <summary>
        /// 穴つなぎを実行する。書き込み先を決めてから、必要な頂点（位置クローン・中間頂点）を
        /// 作って面を張る。書き込み先の既存頂点はそのまま参照するので穴の縁に直結する。
        /// </summary>
        private void ExecuteBridge(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.SetBridgeStatus("モデルがありません"); return; }

            if (!panel.TryBuildBridgePlan(out var plan, out string planMsg))
            {
                panel.SetBridgeStatus(planMsg);
                return;
            }

            // 行き先は共通の「追加先」に従う。専用トグルは廃止した。
            switch (panel.CurrentAddMode)
            {
                case PrimitiveAddMode.AddToExisting:
                    ExecuteBridgeIntoExisting(model, panel, plan);
                    break;
                case PrimitiveAddMode.NewModel:
                    ExecuteBridgeNewModel(panel, plan);
                    break;
                default:
                    ExecuteBridgeNewObject(model, panel, plan);
                    break;
            }
        }

        /// <summary>
        /// 追加先＝既存の描画オブジェクト。既存頂点をそのまま使う。
        ///
        /// 対象の解決は図形生成の AddToExisting と同じ ResolveAddTargetMeshContext を通す。
        /// 以前はここだけ ModelContext.FirstMeshIndex を直に見ており、
        /// ModelContext.cs の「編集対象は ActiveMeshContext / ActiveMeshIndex を使う」
        /// という規約から外れていた。
        /// </summary>
        private void ExecuteBridgeIntoExisting(
            ModelContext model, PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            var targetMc  = ResolveAddTargetMeshContext(model, panel.CurrentAddTargetIndex);
            int targetIdx = targetMc != null ? model.IndexOf(targetMc) : -1;
            if (targetMc?.MeshObject == null || targetIdx < 0)
            {
                panel.SetBridgeStatus("追加先の描画オブジェクトがありません");
                return;
            }

            // UNDO: 変更前スナップショット（図形生成の AddToExisting と同じ経路）
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            // 書き込み先自身の座標系へ落とす。スキンドなら頂点はワールド空間なので恒等。
            int added = AppendBridgeInto(
                targetMc.MeshObject, targetMc.WorldToVertexMatrix, plan,
                plan.SrcMeshA == targetIdx, plan.SrcMeshB == targetIdx,
                model.GetMeshContext(plan.SrcMeshA)?.MeshObject,
                model.GetMeshContext(plan.SrcMeshB)?.MeshObject);

            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Bridge into {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} / 追加頂点 {added} → {targetMc.Name}");
        }

        /// <summary>
        /// 追加先＝新しい描画オブジェクト。両側とも位置クローンになる。
        /// ResolveBridgeParent が決めた親候補の子として作る。
        /// </summary>
        private void ExecuteBridgeNewObject(
            ModelContext model, PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            int parentIdx = ResolveBridgeParent(model, plan.SrcMeshA, plan.SrcMeshB);
            var parentMc  = parentIdx >= 0 ? model.GetMeshContext(parentIdx) : null;

            // 挿入で索引がずれるので、元メッシュは実体で控える。
            // 計画の SrcMeshA/B は計画を立てた時点の索引で、挿入後は別の要素を指す。
            var srcCtxA = model.GetMeshContext(plan.SrcMeshA);
            var srcCtxB = model.GetMeshContext(plan.SrcMeshB);

            // 頂点をどの空間へ格納するかは「生成物自身」が決める。親ではない。
            //
            //   生成物はウェイトを引き継ぐのでスキンドになる。スキンドの頂点は
            //   ワールド（バインド）空間へ格納するのが約束なので、変換を掛けない。
            //
            //   親を基準にすると誤る。スキンド後の親はボーンで、ボーンは頂点を
            //   持たないため WorldToVertexMatrix は WorldMatrixInverse を返す。
            //   それを掛けると、ボーンのワールド位置ぶん頂点がずれる。
            bool producesSkinned = (srcCtxA?.IsSkinned ?? false) || (srcCtxB?.IsSkinned ?? false);

            Matrix4x4 worldToLocal = producesSkinned
                ? Matrix4x4.identity
                : (parentMc != null ? parentMc.WorldToVertexMatrix : Matrix4x4.identity);

            var mo = new MeshObject(panel.BridgeMeshName);

            AppendBridgeInto(
                mo, worldToLocal, plan, false, false,
                srcCtxA?.MeshObject, srcCtxB?.MeshObject);

            var piece = new ObjectArrayPiece
            {
                Mesh          = mo,
                Name          = panel.BridgeMeshName,
                RelativeDepth = 0,
                CopyIndex     = 0,
            };

            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectArrayInserter.InsertAsChildren(
                model, new List<ObjectArrayPiece> { piece }, parentIdx, model.GenerateUniqueMeshName);

            if (added.Count == 0) { panel.SetBridgeStatus("生成できませんでした"); return; }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);

            // 両端の元メッシュがどちらもミラー実体側なら、生成物にもミラーを付ける。
            // ブリッジだけミラーが無いと、左右で構成が食い違ったまま残る。
            // 送るのは UI の ⇆ と同じコマンド。ここで直に EnableMirror を呼ばない。
            // 索引ではなく実体で持ち回る。コマンドはキュー経由で後から走るため、
            // その間に挿入・削除が挟まると索引はずれ、別の要素にミラーが付く。
            var generatedCtx = added[0].MeshContext;
            if (ShouldMirrorGeneratedBridge(model, srcCtxA, srcCtxB))
            {
                int idxNow = model.IndexOf(generatedCtx);
                if (idxNow >= 0)
                {
                    _panelContext?.SendCommand(new SetMirrorEnabledCommand(
                        ActiveProject?.CurrentModelIndex ?? 0, new[] { idxNow }, true));
                    panel.SetBridgeStatus(
                        $"面 {plan.Result.Faces.Count} → {generatedCtx.Name}（ミラーを付与）");
                    return;
                }
            }

            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} → {generatedCtx.Name}");
        }

        /// <summary>
        /// 生成したブリッジにミラーを付けるべきか。
        /// 穴A・穴Bの元メッシュが両方ともミラーペアの実体側のときだけ true。
        /// 片側だけの場合は左右の対応が決まらないので付けない。
        /// </summary>
        private static bool ShouldMirrorGeneratedBridge(
            ModelContext model, MeshContext mcA, MeshContext mcB)
        {
            if (model?.MirrorPairs == null) return false;
            if (mcA == null || mcB == null) return false;

            bool realA = false, realB = false;
            foreach (var pair in model.MirrorPairs)
            {
                if (pair?.Real == null) continue;
                if (ReferenceEquals(pair.Real, mcA)) realA = true;
                if (ReferenceEquals(pair.Real, mcB)) realB = true;
            }
            return realA && realB;
        }

        /// <summary>
        /// 新規オブジェクトのぶら下げ先。親候補は穴A物体と穴B物体の 2 つ。
        ///
        ///   同一オブジェクト        → それ自身
        ///   子孫関係がある          → ルートに近い側
        ///   子孫関係が無い          → 穴A物体
        ///
        /// 以前は子孫関係が無いとき -1（ルート直下）を返しており、
        /// 生成物がどちらの物体にも付かなかった。
        /// </summary>
        private static int ResolveBridgeParent(ModelContext model, int a, int b)
        {
            if (model == null) return -1;
            if (a < 0) return b;
            if (b < 0) return a;
            if (a == b) return a;

            if (IsBridgeAncestor(model, a, b)) return a;
            if (IsBridgeAncestor(model, b, a)) return b;

            // スキンド済みモデルでは 2 つの穴物体がどちらもボーンの子なので、
            // 互いに祖先にならない。どちらが親側かはボーンの鎖で決まるので、
            // それぞれの親ボーンの祖先関係を見て親側の穴物体を返す。
            //
            // ボーン自身を親にはしない。描画オブジェクトの並びの中に置きたいのと、
            // スキンドは頂点の変換に WorldMatrix を使わない（SkinningMatrix を使う）ため、
            // メッシュの子にしても描画には影響しないため。
            int byBone = ResolveBridgeParentByBoneChain(model, a, b);
            if (byBone >= 0) return byBone;

            return a;   // 決まらないときは穴A物体の子にする
        }

        /// <summary>
        /// 2 つの穴物体それぞれの親ボーンを引き、ボーンの祖先関係で親側を選ぶ。
        /// 決まらなければ -1。
        /// </summary>
        private static int ResolveBridgeParentByBoneChain(ModelContext model, int a, int b)
        {
            int boneA = BoneParentOf(model, a);
            int boneB = BoneParentOf(model, b);
            if (boneA < 0 || boneB < 0 || boneA == boneB) return -1;

            if (IsBridgeAncestor(model, boneA, boneB)) return a;
            if (IsBridgeAncestor(model, boneB, boneA)) return b;
            return -1;
        }

        /// <summary>描画オブジェクトが付いているボーンの索引。ボーンでなければ -1。</summary>
        private static int BoneParentOf(ModelContext model, int meshIndex)
        {
            var mc = model.GetMeshContext(meshIndex);
            if (mc == null) return -1;

            int p = mc.HierarchyParentIndex;
            if (p < 0 || p >= model.MeshContextCount) return -1;
            return model.GetMeshContext(p)?.Type == MeshType.Bone ? p : -1;
        }

        /// <summary>
        /// 追加先＝新しいモデル。両側とも位置クローンになる。
        ///
        /// 生成物は元モデルの頂点位置から作るだけで、既存頂点を参照しない。
        /// そのため他図形と同じ OnPrimitiveMeshCreated / PrimitiveMeshCreateNewModel 経路へ
        /// 流せる。新モデルには親が無いのでルート直下に置かれる。
        /// 座標はワールド空間のまま渡す（worldToLocal に単位行列を使う）。
        /// </summary>
        private void ExecuteBridgeNewModel(
            PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.SetBridgeStatus("モデルがありません"); return; }

            var mo = new MeshObject(panel.BridgeMeshName);

            AppendBridgeInto(
                mo, Matrix4x4.identity, plan, false, false,
                model.GetMeshContext(plan.SrcMeshA)?.MeshObject,
                model.GetMeshContext(plan.SrcMeshB)?.MeshObject);

            if (mo.FaceCount == 0) { panel.SetBridgeStatus("生成できませんでした"); return; }

            OnPrimitiveMeshCreated(
                mo, panel.BridgeMeshName, Vector3.zero, Vector3.zero, Vector3.one,
                false, PrimitiveAddMode.NewModel);

            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} → 新しいモデル");
        }

        /// <summary>ancestor が descendant の祖先か。HierarchyParentIndex を辿る。</summary>
        private static bool IsBridgeAncestor(ModelContext model, int ancestor, int descendant)
        {
            int cur   = descendant;
            int guard = 0;
            while (cur >= 0 && guard++ < 4096)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) return false;
                int p = mc.HierarchyParentIndex;
                if (p == ancestor) return true;
                cur = p;
            }
            return false;
        }

        /// <summary>
        /// 計画にしたがって dst へ頂点と面を足す。戻り値は追加した頂点数。
        /// reuseA / reuseB が true の側は既存頂点インデックスをそのまま使う（穴の縁に直結）。
        /// false の側は位置クローンを作る。中間頂点は常に新規。
        /// </summary>
        private static int AppendBridgeInto(
            MeshObject dst, Matrix4x4 worldToLocal,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan,
            bool reuseA, bool reuseB,
            MeshObject srcA, MeshObject srcB)
        {
            var r = plan.Result;
            int addedCount = 0;

            // 符号化ID → dst の頂点インデックス
            var map = new int[r.InterBase + r.Inter.Count];

            for (int k = 0; k < plan.LoopA.Count; k++)
            {
                if (reuseA) { map[k] = plan.LoopA[k]; continue; }
                map[k] = AddBridgeClone(
                    dst, worldToLocal.MultiplyPoint3x4(plan.WorldA[k]), srcA, plan.LoopA[k]);
                addedCount++;
            }

            for (int k = 0; k < plan.LoopB.Count; k++)
            {
                int id = r.ACount + k;
                if (reuseB) { map[id] = plan.LoopB[k]; continue; }
                map[id] = AddBridgeClone(
                    dst, worldToLocal.MultiplyPoint3x4(plan.WorldB[k]), srcB, plan.LoopB[k]);
                addedCount++;
            }

            // 中間頂点。位置は分割比で内分し、ウェイトも同じ比で補間する。
            for (int k = 0; k < r.Inter.Count; k++)
            {
                var ip = r.Inter[k];
                Vector3 world = Vector3.Lerp(plan.WorldA[ip.AIdx], plan.WorldB[ip.BIdx], ip.T);

                var v = new Poly_Ling.Data.Vertex(worldToLocal.MultiplyPoint3x4(world));

                v.BoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                    BridgeSourceWeight(srcA, plan.LoopA, ip.AIdx, false),
                    BridgeSourceWeight(srcB, plan.LoopB, ip.BIdx, false), ip.T);

                v.MirrorBoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                    BridgeSourceWeight(srcA, plan.LoopA, ip.AIdx, true),
                    BridgeSourceWeight(srcB, plan.LoopB, ip.BIdx, true), ip.T);

                map[r.InterBase + k] = dst.AddVertex(v);
                addedCount++;
            }

            foreach (var f in r.Faces)
            {
                var face = new Face();
                for (int i = 0; i < f.Length; i++) face.VertexIndices.Add(map[f[i]]);

                Vector3 n = BridgeFaceNormal(dst, face.VertexIndices);

                // UV / 法線スロットは同一インデックスで確保する（スロット不変条件）。
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int slot = dst.Vertices[face.VertexIndices[i]].GetOrAddUVNormal(Vector2.zero, n);
                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }

                face.MaterialIndex = 0;
                dst.AddFace(face);
            }

            // ブリッジは別メッシュ間でも張れる。相手のウェイトを引き継いだ結果、
            // 転送先が初めてウェイトを持つことがあるため種別を確認し直す。
            dst.RecomputeSkinKind();

            return addedCount;
        }

        /// <summary>元メッシュの頂点位置クローンを dst へ足す。ウェイトは引き継ぐ。</summary>
        private static int AddBridgeClone(
            MeshObject dst, Vector3 localPos, MeshObject src, int srcVertexIndex)
        {
            var v = new Poly_Ling.Data.Vertex(localPos);

            if (src != null && srcVertexIndex >= 0 && srcVertexIndex < src.Vertices.Count)
            {
                var sv = src.Vertices[srcVertexIndex];
                v.BoneWeight       = sv.BoneWeight;
                v.MirrorBoneWeight = sv.MirrorBoneWeight;
            }

            return dst.AddVertex(v);
        }

        /// <summary>ループ上の頂点のウェイトを取る。取れないときは null。</summary>
        private static BoneWeight? BridgeSourceWeight(
            MeshObject src, List<int> loop, int loopIndex, bool mirror)
        {
            if (src == null || loop == null) return null;
            if (loopIndex < 0 || loopIndex >= loop.Count) return null;

            int vi = loop[loopIndex];
            if (vi < 0 || vi >= src.Vertices.Count) return null;

            return mirror ? src.Vertices[vi].MirrorBoneWeight : src.Vertices[vi].BoneWeight;
        }

        /// <summary>Newell 法の面法線。退化時は Vector3.up を返す。</summary>
        private static Vector3 BridgeFaceNormal(MeshObject mo, List<int> indices)
        {
            Vector3 n = Vector3.zero;
            int c = indices.Count;

            for (int i = 0; i < c; i++)
            {
                Vector3 p0 = mo.Vertices[indices[i]].Position;
                Vector3 p1 = mo.Vertices[indices[(i + 1) % c]].Position;
                n.x += (p0.y - p1.y) * (p0.z + p1.z);
                n.y += (p0.z - p1.z) * (p0.x + p1.x);
                n.z += (p0.x - p1.x) * (p0.y + p1.y);
            }

            return n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
        }

        /// <param name="materialIndex">
        /// 生成面へ割り当てるマテリアルスロット番号。-1 は「指定しない」で、
        /// 生成器が入れた MaterialIndex をそのまま使う（藤壺などの継承組と、
        /// 図形生成以外の経路が該当）。
        /// </param>
        private void OnPrimitiveMeshCreated(
            MeshObject meshObject, string meshName, Vector3 worldPos,
            Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, PrimitiveAddMode addMode,
            int addTargetIndex = -1, int materialIndex = -1)
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
            _vertexHoleHandler?.SetProject(ActiveProject);
            _addFaceHandler?.SetProject(ActiveProject);
            _flipFaceHandler?.SetProject(ActiveProject);
            _rotateHandler?.SetProject(ActiveProject);
            _scaleHandler?.SetProject(ActiveProject);
            _edgeBevelHandler?.SetProject(ActiveProject);
            _edgeExtrudeHandler?.SetProject(ActiveProject);
            _faceExtrudeHandler?.SetProject(ActiveProject);
            _edgeTopologyHandler?.SetProject(ActiveProject);
            _knifeHandler?.SetProject(ActiveProject);
            _solidifyHandler?.SetProject(ActiveProject);
            _deleteSelectionHandler?.SetProject(ActiveProject);
            _vertexDissolveHandler?.SetProject(ActiveProject);
            _tri4To1Handler?.SetProject(ActiveProject);
            _faceMergeHandler?.SetProject(ActiveProject);
            _quad4To1Handler?.SetProject(ActiveProject);
            _faceMergeCollapseHandler?.SetProject(ActiveProject);

            var project = ActiveProject;
            if (project == null) return;
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            ApplySelectMode();  // 実効選択モードを新規アクティブモデルへ適用

            switch (addMode)
            {
                case PrimitiveAddMode.NewObject:
                    PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos,
                        poseRotation, poseScale, ignorePoseInArmature, materialIndex);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    PrimitiveMeshAddToExisting(project, meshObject, meshName, worldPos,
                        poseRotation, poseScale, ignorePoseInArmature, addTargetIndex,
                        materialIndex);
                    break;
                case PrimitiveAddMode.NewModel:
                    PrimitiveMeshCreateNewModel(project, meshObject, meshName, worldPos,
                        poseRotation, poseScale, ignorePoseInArmature, materialIndex);
                    break;
            }
        }

        /// <summary>
        /// 「既存の描画オブジェクトに追加」の追加先を解決する。
        ///
        /// addTargetIndex はパネルの名前欄ドロップダウンが返す MeshContextList
        /// インデックス。-1 や範囲外のときは選択オブジェクトリストの先頭
        /// （ModelContext.ActiveMeshContext）へ落とす。
        /// 穴つなぎもこの 1 箇所を通し、図形生成と同じ対象になるようにする。
        /// </summary>
        private static MeshContext ResolveAddTargetMeshContext(ModelContext model, int addTargetIndex)
        {
            if (model == null) return null;
            if (addTargetIndex >= 0)
            {
                var mc = model.GetMeshContext(addTargetIndex);
                if (mc?.MeshObject != null) return mc;
            }
            return model.ActiveMeshContext;
        }

        /// <summary>
        /// 生成メッシュの全面へマテリアルスロット番号を割り当てる。
        ///
        /// materialIndex が負のときは何もしない。藤壺のように元オブジェクトの
        /// MaterialIndex を引き継ぐ図形と、図形生成以外の経路が該当する。
        ///
        /// スロットが 1 つも無いモデルには 1 つ作る。作成は EnsureDefaultMaterialSlot に
        /// 任せる。ここで AddMaterial を直接呼ぶと、PrimitiveMeshFinalize 内の
        /// EnsureDefaultMaterialSlot が「既に 1 件ある」と判断して何もせず、
        /// 名前 "Default" と描画フォールバックと同じ灰(0.7)が付かなくなる。
        ///
        /// 作ったときだけ true を返すので、呼出し側は「作る前のマテリアル一覧」を
        /// Undo へ渡すこと。
        /// </summary>
        private bool ApplyGeneratedMaterialIndex(
            ModelContext model, MeshObject meshObject, int materialIndex)
        {
            if (model == null || meshObject == null || materialIndex < 0) return false;

            bool added = false;
            if (model.MaterialCount == 0)
            {
                EnsureDefaultMaterialSlot(model);
                added = model.MaterialCount > 0;
            }

            if (model.MaterialCount == 0) return false;

            int slot = Mathf.Clamp(materialIndex, 0, model.MaterialCount - 1);
            foreach (var f in meshObject.Faces)
            {
                if (f == null) continue;
                f.MaterialIndex = slot;
            }
            return added;
        }

        /// <summary>
        /// 図形生成共通: MeshContextを作って返す。
        /// </summary>
        private MeshContext BuildPrimitiveMeshContext(
            MeshObject meshObject, string meshName, Vector3 worldPos,
            Vector3 poseRotation, Vector3 poseScale, bool ignorePoseInArmature)
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

            // 図形生成側でベイクしなかった回転 / スケールは描画オブジェクトの姿勢に入れる。
            bool hasPose = worldPos != Vector3.zero
                        || poseRotation != Vector3.zero
                        || poseScale != Vector3.one;
            if (ctx.BoneTransform != null && hasPose)
            {
                ctx.BoneTransform.UseLocalTransform = true;
                ctx.BoneTransform.Position = worldPos;
                ctx.BoneTransform.Rotation = poseRotation;
                ctx.BoneTransform.Scale    = poseScale;
            }

            ctx.IgnorePoseInArmature = ignorePoseInArmature;
            return ctx;
        }

        /// <summary>
        /// モード1: 新しい描画オブジェクトとして現在のモデルに追加。UNDO対応。
        /// </summary>
        private void PrimitiveMeshCreateNewObject(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int materialIndex = -1)
        {
            var model = project.CurrentModel;
            if (model == null) return;

            // 既存の描画オブジェクトと名前が衝突しないようにしてから作る。
            meshName = model.GenerateUniqueMeshName(meshName);

            // マテリアル割当。スロットを作るのは 0 件のときだけなので、
            // その場合の「作る前の一覧」は必ず空になる。
            // 指定が無いときは Materials に触れない（MaterialReference の実体化を避ける）。
            bool willAddSlot      = materialIndex >= 0 && model.MaterialCount == 0;
            var  oldMaterials     = willAddSlot ? new List<Material>() : null;
            int  oldMaterialIndex = model.CurrentMaterialIndex;
            bool matSlotAdded     = ApplyGeneratedMaterialIndex(model, meshObject, materialIndex);

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos,
                poseRotation, poseScale, ignorePoseInArmature);
            ctx.ParentModelContext = model;

            var oldSelected = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(ctx);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(insertIndex);
            model.SelectMesh(insertIndex);
            var newSelected = model.CaptureAllSelectedIndices();

            // UNDO記録。マテリアルスロットを作った場合だけ、その前後も同じレコードへ入れる。
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected,
                    null, null,
                    matSlotAdded ? oldMaterials : null,
                    oldMaterialIndex,
                    matSlotAdded ? new List<Material>(model.Materials) : null,
                    matSlotAdded ? model.CurrentMaterialIndex : 0);
            }

            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// モード2: 既存の選択中描画オブジェクトに頂点・面をマージ。UNDO対応。
        /// 描画オブジェクトが存在しない場合はモード1にフォールバック。
        /// </summary>
        private void PrimitiveMeshAddToExisting(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int addTargetIndex = -1, int materialIndex = -1)
        {
            var model  = project.CurrentModel;
            if (model == null) return;

            // 追加先はパネルの名前欄ドロップダウンで選んだオブジェクト。
            // -1（未選択・未配線）のときだけ従来どおり選択オブジェクトリストの先頭。
            var targetMc = ResolveAddTargetMeshContext(model, addTargetIndex);
            if (targetMc == null || targetMc.MeshObject == null)
            {
                PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos,
                    poseRotation, poseScale, ignorePoseInArmature, materialIndex);
                return;
            }

            // 既存オブジェクトへのマージでは姿勢を持てないため、
            // ベイクされずに渡ってきた回転 / スケールはここで頂点へ焼き込む。
            var srcObject = meshObject;
            bool hasPose = poseRotation != Vector3.zero || poseScale != Vector3.one;
            if (hasPose)
            {
                srcObject = meshObject.Clone();
                Poly_Ling.PrimitiveMesh.PrimitiveMeshTransform.ApplyRotationScale(
                    srcObject, poseRotation, poseScale);
            }

            // ワールド位置オフセットを頂点に適用
            if (worldPos != Vector3.zero)
            {
                if (ReferenceEquals(srcObject, meshObject)) srcObject = meshObject.Clone();
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

            // マテリアル割当。MeshObjectSnapshot は Materials も保持するので、
            // スロットを作る可能性のあるこの処理は必ず before 捕獲の後に行う。
            // 面を書き換える対象は、マージで実際に読まれる srcObject 側。
            ApplyGeneratedMaterialIndex(model, srcObject, materialIndex);

            // 部品IDは追加先の空き番号へずらす。生成物が内部で複数パーツに分かれている
            // （フリル・パイプ・藤壺）場合も、内部の構成を保ったまま全体を平行移動させる。
            // 直前の ApplyGeneratedMaterialIndex と同じく srcObject を直接書き換える
            // （頂点は下のマージで Clone() されるため、ここで複製する必要はない）。
            Poly_Ling.Ops.PartsIdOps.OffsetPartsId(
                srcObject, Poly_Ling.Ops.PartsIdOps.NextPartsId(targetMc.MeshObject));

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

            // サブIDは連結後の並びで、部品IDごとに 0 から振り直す。
            Poly_Ling.Ops.PartsIdOps.AssignSubIdByPartsId(targetMc.MeshObject);

            // UnityMesh再構築
            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

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
            Vector3 worldPos, Vector3 poseRotation, Vector3 poseScale,
            bool ignorePoseInArmature, int materialIndex = -1)
        {
            var newModel = project.CreateNewModel(meshName);
            if (newModel == null) return;

            // 新規モデルなので通常は衝突しないが、経路を揃えるため同じ一意化を通す。
            meshName = newModel.GenerateUniqueMeshName(meshName);

            // 新規モデルはマテリアルスロットが 0 件なので、指定があれば 1 つ作る。
            // 作る前の一覧は必ず空。指定が無いときは Materials に触れない。
            bool willAddSlot      = materialIndex >= 0 && newModel.MaterialCount == 0;
            var  oldMaterials     = willAddSlot ? new List<Material>() : null;
            int  oldMaterialIndex = newModel.CurrentMaterialIndex;
            bool matSlotAdded     = ApplyGeneratedMaterialIndex(newModel, meshObject, materialIndex);

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos,
                poseRotation, poseScale, ignorePoseInArmature);
            ctx.ParentModelContext = newModel;

            var oldSelected = newModel.CaptureAllSelectedIndices();
            int insertIndex = newModel.Add(ctx);
            newModel.ComputeWorldMatrices();
            newModel.SelectMeshContextExclusive(insertIndex);
            newModel.SelectMesh(insertIndex);
            var newSelected = newModel.CaptureAllSelectedIndices();

            // UNDO記録（新モデル上のメッシュ追加）。
            // マテリアルスロットを作った場合だけ、その前後も同じレコードへ入れる。
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(newModel);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected,
                    null, null,
                    matSlotAdded ? oldMaterials : null,
                    oldMaterialIndex,
                    matSlotAdded ? new List<Material>(newModel.Materials) : null,
                    matSlotAdded ? newModel.CurrentMaterialIndex : 0);
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
                onResult: (model, _) => { _localLoader.LoadModel(filePath, model); UnityEngine.Debug.Log("[LoadDbg] 16 after-LoadModel"); },
                onError:  msg       => _status = $"MQO読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
            UnityEngine.Debug.Log("[LoadDbg] 17 after-Enqueue");
        }

        private void OnImportObj(string filePath, Poly_Ling.OBJ.ObjImportSettings settings)
        {
            var cmd = new ImportObjCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"OBJ読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnExportObj(string outputPath, Poly_Ling.OBJ.ObjExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = Poly_Ling.OBJ.ObjExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                {
                    string mtl = string.IsNullOrEmpty(result.MtlPath)
                        ? ""
                        : $" + {System.IO.Path.GetFileName(result.MtlPath)}";
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}{mtl}");
                }
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
        }

        private void OnExportVrm(string outputPath, Poly_Ling.Vrm.Vrm10ExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = Poly_Ling.Vrm.PLVrm10Bridge.I.Export(model, outputPath, settings);
                if (result.Success)
                {
                    string msg = $"完了: {System.IO.Path.GetFileName(outputPath)} " +
                                 $"({result.MeshCount}メッシュ / {result.VertexCount}頂点 / " +
                                 $"Humanoid {result.HumanoidBoneCount}ボーン)";
                    if (!string.IsNullOrEmpty(result.Warning))
                        msg += "\n警告: " + result.Warning;
                    _exportSubPanel?.SetStatus(msg);
                }
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
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

        private void OnSaveProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectSaveSubPanel?.SetStatus("パスが指定されていません"); return; }
            var project = ActiveProject;
            if (project == null) { _projectSaveSubPanel?.SetStatus("プロジェクトがありません"); return; }
            var dto = ProjectSerializer.FromProjectContext(project);
            if (dto == null) { _projectSaveSubPanel?.SetStatus("シリアライズ失敗"); return; }
            bool ok = ProjectSerializer.Export(path, dto);
            _projectSaveSubPanel?.SetStatus(ok ? "保存完了" : "保存失敗");
        }

        private void OnLoadProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return; }
            var dto = ProjectSerializer.Import(path);
            if (dto == null) { _projectLoadSubPanel?.SetStatus("読込失敗"); return; }
            var loadedProject = ProjectSerializer.ToProjectContext(dto);
            if (loadedProject == null) { _projectLoadSubPanel?.SetStatus("復元失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? dto.name, m);
            AdoptWorkAxisLibrary(loadedProject);
            _projectLoadSubPanel?.SetStatus($"読込完了: {dto.name}");
        }

        // path はCSVプロジェクトファイル（任意名の .csv）。モデルフォルダは同ディレクトリ直下。
        private void OnSaveCsvProject(string path)
        {
            if (string.IsNullOrEmpty(path)) { _projectSaveSubPanel?.SetStatus("パスが指定されていません"); return; }
            var project = ActiveProject;
            if (project == null) { _projectSaveSubPanel?.SetStatus("プロジェクトがありません"); return; }
            bool ok = CsvProjectSerializer.ExportToFile(path, project);
            _projectSaveSubPanel?.SetStatus(ok ? "CSV保存完了" : "保存失敗");
        }

        // path はCSVプロジェクトファイル（任意名の .csv）。
        // マージは指定ファイルと同じフォルダ内のメッシュCSVを対象にする。
        private void OnLoadCsvProject(string path, bool merge)
        {
            if (string.IsNullOrEmpty(path)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return; }
            if (merge)
            {
                string mergeFolder = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(mergeFolder)) { _projectLoadSubPanel?.SetStatus("パスが不正です"); return; }
                MergeCsvFromFolder(mergeFolder);
                return;
            }

            var loadedProject = CsvProjectSerializer.ImportFromFile(path, out _, out _);
            if (loadedProject == null) { _projectLoadSubPanel?.SetStatus("読込失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? loadedProject.Name, m);
            AdoptWorkAxisLibrary(loadedProject);
            _projectLoadSubPanel?.SetStatus($"CSV読込完了: {loadedProject.Name}");
        }

        /// <summary>
        /// 読み込んだプロジェクトの作業軸辞書を、実際に表示されるプロジェクトへ移す。
        ///
        /// 読み込み経路は復元した ProjectContext をそのまま使わず、モデルだけを
        /// _localLoader へ渡して別の ProjectContext を作り直す
        /// （PlayerLocalLoader.FinishLoad）。辞書はモデルにぶら下がっていないので、
        /// ここで明示的に移さないと読み込みのたびに消える。
        /// </summary>
        private void AdoptWorkAxisLibrary(ProjectContext loaded)
        {
            var src = loaded?.WorkAxes;
            var dst = ActiveProject?.WorkAxes;

            // 同一インスタンスなら移す必要はない（Clear して自分を舐めると壊れる）。
            if (src == null || dst == null || ReferenceEquals(src, dst)) return;

            dst.Clear();
            foreach (var name in src.Names)
            {
                if (src.TryGet(name, out var e)) dst.Set(name, e);
            }

            RefreshWorkAxisLibraryLists();
        }

        /// <summary>
        /// 作業軸辞書の一覧を持つパネルをまとめて更新する。
        /// 左ペインと変形パネルは同じ辞書を指すので、片方の変更を両方へ反映させる。
        /// </summary>
        private void RefreshWorkAxisLibraryLists()
        {
            _workAxisSubPanel?.RefreshLibraryList();
            _deformWorkAxisSubPanel?.RefreshLibraryList();
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

            var mc = ctx.ActiveMeshContext;
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

        private void MergeCsvFromFolder(string folderPath)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _projectLoadSubPanel?.SetStatus("モデルがありません"); return; }
            if (string.IsNullOrEmpty(folderPath)) { _projectLoadSubPanel?.SetStatus("パスが指定されていません"); return; }

            var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
            if (entries == null || entries.Count == 0) { _projectLoadSubPanel?.SetStatus("読み込めるデータがありません"); return; }

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

            _projectLoadSubPanel?.SetStatus($"マージ完了: +{added} /{replaced}置換");
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
                    // 一時ミラーのボタン表示を実状態へ合わせるため Refresh が要る。
                    refresh = () => _sculptSubPanel?.Refresh();
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
                case InteractionMode.SkinWeightNumeric:
                    section = _layoutRoot?.SkinWeightNumericSection;
                    btn     = _layoutRoot?.SkinWeightNumericBtn;
                    refresh = () => _skinWeightNumericSubPanel?.Refresh();
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
                case InteractionMode.Solidify:
                    section = _layoutRoot?.SolidifySection;
                    btn     = _layoutRoot?.SolidifyBtn;
                    refresh = () => _solidifySubPanel?.Refresh();
                    break;
                case InteractionMode.Rotate:
                    section = _layoutRoot?.RotateSection;
                    btn     = _layoutRoot?.RotateBtn;
                    refresh = () => _rotateSubPanel?.Refresh();
                    break;
                case InteractionMode.WorkAxis:
                    section = _layoutRoot?.WorkAxisSection;
                    btn     = _layoutRoot?.WorkAxisBtn;
                    refresh = () => _workAxisSubPanel?.Refresh();
                    break;
                case InteractionMode.Camera:
                    section = _layoutRoot?.CameraSection;
                    btn     = _layoutRoot?.CameraBtn;
                    refresh = () => _cameraSubPanel?.Refresh();
                    break;
                case InteractionMode.Deform:
                    section = _layoutRoot?.DeformSection;
                    btn     = _layoutRoot?.DeformBtn;
                    refresh = () => _deformSubPanel?.Refresh();
                    break;
                case InteractionMode.Lattice:
                    section = _layoutRoot?.LatticeSection;
                    btn     = _layoutRoot?.LatticeBtn;
                    refresh = () => _latticeSubPanel?.Refresh();
                    break;
                case InteractionMode.Scale:
                    section = _layoutRoot?.ScaleSection;
                    btn     = _layoutRoot?.ScaleBtn;
                    refresh = () => _scaleSubPanel?.Refresh();
                    break;
                case InteractionMode.VertexDissolve:
                    section = _layoutRoot?.VertexDissolveSection;
                    btn     = _layoutRoot?.VertexDissolveBtn;
                    refresh = () => _vertexDissolveSubPanel?.Refresh();
                    break;
                case InteractionMode.Tri4To1:
                    section = _layoutRoot?.Tri4To1Section;
                    btn     = _layoutRoot?.Tri4To1Btn;
                    refresh = () => _tri4To1SubPanel?.Refresh();
                    break;
                case InteractionMode.FaceMerge:
                    section = _layoutRoot?.FaceMergeSection;
                    btn     = _layoutRoot?.FaceMergeBtn;
                    refresh = () => _faceMergeSubPanel?.Refresh();
                    break;
                case InteractionMode.Quad4To1:
                    section = _layoutRoot?.Quad4To1Section;
                    btn     = _layoutRoot?.Quad4To1Btn;
                    refresh = () => _quad4To1SubPanel?.Refresh();
                    break;
                case InteractionMode.FaceMergeCollapse:
                    section = _layoutRoot?.FaceMergeCollapseSection;
                    btn     = _layoutRoot?.FaceMergeCollapseBtn;
                    refresh = () => _faceMergeCollapseSubPanel?.Refresh();
                    break;
            }

            ShowRightPanel(section, btn);
            refresh?.Invoke();
        }

        // ================================================================
        // 一時選択サブツール
        //   ShowCategory1Panel は使わない。右ペイン表示とボタンハイライトを
        //   通常ツールと同じようには動かさず、入力ハンドラのみ差し替えるため
        //   SetInteractionMode を直接呼ぶ。
        // ================================================================

        /// <summary>
        /// 一時選択サブツールへ入る。lasso = true で投げ縄、false で矩形。
        /// </summary>
        private void EnterSelectSubTool(bool lasso)
        {
            if (_moveToolHandler == null) return;

            // サブツール中の押し替え (R → G / G → R) では復帰先を上書きしない。
            if (!_subToolActive)
            {
                _subToolActive             = true;
                _subToolPrevMode           = _interactionMode;
                _subToolPrevMoveDragMode   = _moveToolHandler.DragSelectMode;
                _subToolPrevObjectDragMode = _objectMoveHandler != null
                    ? _objectMoveHandler.DragSelectMode
                    : ObjectMoveToolHandler.SelectionDragMode.Box;

                // 選択モードの退避は不要。SelectOnly は
                // ResolveToolSelectModeOverride が現在の override をそのまま引き継ぐ。
                SetInteractionMode(InteractionMode.SelectOnly);
            }

            _moveToolHandler.DragSelectMode = lasso
                ? MoveToolHandler.SelectionDragMode.Lasso
                : MoveToolHandler.SelectionDragMode.Box;

            // LassoToggle が両ハンドラを同時に書き換える既存の対称性を保つ。
            if (_objectMoveHandler != null)
                _objectMoveHandler.DragSelectMode = lasso
                    ? ObjectMoveToolHandler.SelectionDragMode.Lasso
                    : ObjectMoveToolHandler.SelectionDragMode.Box;

            _moveToolHandler.OneShotFinished = ExitSelectSubTool;

            // SetInteractionMode より後に DragSelectMode が決まるため、ここで再通知する
            // （矩形／投げ縄の別を性能ログへ残す）。
            ReportPerfToolState();
        }

        /// <summary>
        /// 一時選択サブツールから直前のツールへ戻す。サブツール中でなければ何もしない。
        /// </summary>
        private void ExitSelectSubTool()
        {
            if (!_subToolActive) return;
            _subToolActive = false;

            if (_moveToolHandler != null)
            {
                _moveToolHandler.OneShotFinished = null;
                _moveToolHandler.DragSelectMode  = _subToolPrevMoveDragMode;
            }
            if (_objectMoveHandler != null)
                _objectMoveHandler.DragSelectMode = _subToolPrevObjectDragMode;

            // 復帰先モードの override は SetInteractionMode が決め直すため、
            // 選択モードの復元処理は不要。
            SetInteractionMode(_subToolPrevMode);

            // ドラッグ選択モードのトグル表示を実状態へ戻す
            // (サブツール中に Refresh が走った場合のずれを解消する)。
            _layoutRoot?.LassoToggle?.SetValueWithoutNotify(
                _subToolPrevMoveDragMode == MoveToolHandler.SelectionDragMode.Lasso);
            _vertexMoveSubPanel?.Refresh();
        }

        /// <summary>
        /// 選択削除サブツール。選択中の頂点 / 面 / 線分を削除する。
        ///
        /// 矩形・投げ縄サブツールと違い InteractionMode は一切変更しない。
        /// 削除はマウス操作を伴わない即時実行なので、ドラッグを奪う必要が無く、
        /// SelectOnly へ往復させても SetInteractionMode の脱出/進入処理
        /// (フックの null 化・選択モード override の決め直し・ボタンハイライト・
        ///  ギズモ overlay 更新) が空回りするだけで実利が無い。
        /// モードを触らないので「実行前のツールに戻る」は自動的に満たされる。
        ///
        /// 矩形/投げ縄サブツール待ち (SelectOnly) の最中に呼ばれた場合も、
        /// その待ち状態は解除しない (ドラッグを消費していないため)。
        /// </summary>
        private void ExecuteDeleteSelection()
        {
            _deleteSelectionHandler?.TriggerDelete();
        }

        // ================================================================
        // 選択頂点の結合（即時実行の単発コマンド）
        //   選択削除と同じく InteractionMode は一切変更しない。実行前のツールが
        //   そのまま維持されるため、Ctrl+J 一発で結合が完了する。
        //   頂点マージパネルを開いていなくても動く（ハンドラ側で ToolContext を
        //   その場で組み立てるため）。
        // ================================================================

        /// <summary>距離を見ず、選択頂点を 1 点（重心）へ結合する。</summary>
        private void ExecuteMergeSelectedToCentroid()
        {
            _mergeVerticesHandler?.TriggerMergeToCentroidNow();
            _mergeVerticesSubPanel?.Refresh();
        }

        /// <summary>選択頂点のうち、しきい値以下の距離にあるものを結合する。</summary>
        private void ExecuteMergeSelectedByThreshold()
        {
            _mergeVerticesHandler?.TriggerMergeByThresholdNow();
            _mergeVerticesSubPanel?.Refresh();
        }

        // ================================================================
        // 面削除モード
        //   ShowCategory1Panel は使わない。右ペインは切り替えず、入力挙動と
        //   選択モードだけを差し替えるため SetInteractionMode を直接呼ぶ
        //   (一時選択サブツールと同じ方針)。
        // ================================================================

        /// <summary>
        /// 面削除モードへ入る。既に入っているときは何もしない
        /// (再進入で復帰先が自分自身に上書きされるのを防ぐ)。
        /// </summary>
        private void EnterDeleteFaceMode()
        {
            if (_deleteFaceModeActive) return;

            _deleteFaceModeActive = true;
            _deleteFacePrevMode   = _interactionMode;
            SetInteractionMode(InteractionMode.DeleteFace);
        }

        /// <summary>
        /// 面削除モードから直前のツールへ戻す。モード中でなければ何もしない。
        /// </summary>
        private void ExitDeleteFaceMode()
        {
            if (!_deleteFaceModeActive) return;

            // SetInteractionMode の脱出処理が _deleteFaceModeActive を false にし、
            // フック解除を行う。選択モードは復帰先モードの override が決め直す。
            SetInteractionMode(_deleteFacePrevMode);
        }

        /// <summary>
        /// 面削除モードでのクリック処理 (MoveToolHandler.OnLeftClickExtra)。
        ///
        /// 面以外のヒットは無視する。Selection.Mode を Face に絞ってあるので
        /// 通常は Face か None しか来ないが、念のため型で弾く。
        ///
        /// 修飾キーは無視する。ApplyElementClick が Shift/Ctrl で選択を積んだ後でも、
        /// ここで選択をクリック面 1 枚に差し替えてから削除するため、
        /// 「クリックした面だけが消える」挙動が常に保たれる。
        ///
        /// 削除対象はクリックされたメッシュ。ホバーは選択中の全メッシュから返るため、
        /// アクティブメッシュ (SelectedDrawableMeshIndices の先頭) 以外の面もクリック
        /// し得る。DeleteSelectionTool は選択中の描画オブジェクトを走査するので、
        /// 選択中オブジェクト全部の選択をクリアしてから、クリックされたメッシュに
        /// その面だけを入れて実行する。ActiveMeshIndex は変更しない。
        /// </summary>
        private void OnDeleteFaceClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Face) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            var target = model.GetMeshContext(elem.MeshIndex);
            if (target?.Selection == null) return;

            // 選択をクリック面 1 枚に差し替える (修飾キーによる追加選択を無効化)。
            // 他オブジェクトに選択が残っていると一緒に消えるため、全部クリアする。
            foreach (int idx in model.SelectedDrawableMeshIndices)
                model.GetMeshContext(idx)?.Selection?.ClearAll();
            target.Selection.ClearAll();
            target.Selection.SelectFace(elem.FaceIndex, false);

            ExecuteDeleteSelection();
        }

        // ================================================================
        // 結合ツールのクリック実行 (MoveToolHandler.OnLeftClickExtra)
        //
        // いずれも面削除モードと同じ方針:
        //   ・想定外の要素種別は無視する（Selection.Mode で絞ってあるが念のため）
        //   ・アクティブメッシュ以外は無視する（各 Tool は選択中オブジェクトを
        //     走査するため、別メッシュの要素をそのまま流すと意図しない箇所が変わる）
        //   ・修飾キーは無視し、選択をクリックした要素 1 つに差し替えてから実行する
        // ================================================================

        private void OnVertexDissolveClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Vertex) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectVertex(elem.VertexIndex, false);

            _vertexDissolveHandler?.TriggerDissolve();
            _vertexDissolveSubPanel?.Refresh();
        }

        private void OnTri4To1Clicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Face) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectFace(elem.FaceIndex, false);

            _tri4To1Handler?.TriggerMerge();
            _tri4To1SubPanel?.Refresh();
        }

        private void OnFaceMergeClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Edge) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectEdge(new VertexPair(elem.EdgeV1, elem.EdgeV2), false);

            _faceMergeHandler?.TriggerMerge();
            _faceMergeSubPanel?.Refresh();
        }

        private void OnQuad4To1Clicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Vertex) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectVertex(elem.VertexIndex, false);

            _quad4To1Handler?.TriggerMerge();
            _quad4To1SubPanel?.Refresh();
        }

        private void OnFaceMergeCollapseClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Edge) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectEdge(new VertexPair(elem.EdgeV1, elem.EdgeV2), false);

            _faceMergeCollapseHandler?.TriggerMerge();
            _faceMergeCollapseSubPanel?.Refresh();
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
            // ── ツール内「一時ミラー」の自動解除 ─────────────────────────
            // 実体化したツールから離れたら、そのツールが生やした実体を必ず戻す。
            // ここが全ツール遷移の唯一の合流点なので、各ツール側に後始末を書かない。
            //
            // 一時選択サブツール (R / G → SelectOnly) への往復はツール遷移ではないので
            // 除外する。除外しないと、矩形選択を挟むたびに一時ミラーが消える。
            if (_tempMirrorController != null
                && _tempMirrorController.IsActive
                && _tempMirrorController.OwnerToken != (int)mode
                && !(_subToolActive && mode == InteractionMode.SelectOnly))
            {
                _tempMirrorController.Unbake();
            }

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

            // スキンW数値設定もウェイト可視化を使う（ActivePanel に数値パネルを差す）。
            // 脱出時は可視化を止め、頂点カラーを消してから ActivePanel を外す。
            // 順序が逆だと ActivePanel が null になって対象メッシュを解決できず、
            // 色が残ったままになる。
            if (_interactionMode == InteractionMode.SkinWeightNumeric && mode != InteractionMode.SkinWeightNumeric)
            {
                ClearNumericWeightVisualization();
                SkinWeightPaintTool.SetVisualizationActive(false);
                SkinWeightPaintTool.ActivePanel = null;
            }

            // 【選択モードの復元について】
            // 旧実装はツール脱出のたびに ActiveMeshContext.Selection.Mode へ
            // MeshSelectMode.All を書き戻していた。All はチェックボックスの値ではないため、
            // 「頂点だけチェックしているのに、ツールを一度使うと辺・面までホバー／選択され、
            //  移動対象にもなる」状態が発生していた（＝設定がすぐ巻き戻る症状）。
            // 現在は、この関数の末尾寄りで新モードに対応する override を決め直し、
            // ApplySelectMode() が override 無しならチェックボックス値へ戻す。
            // ここでの個別復元は行わない。

            // 格子変形から脱出するとき、進行中のセッションは取消する。
            // 黙って確定すると、ユーザが意図しない変形が Undo 履歴に残るため。
            //
            // 作業軸へ移るときだけは取消さない。格子フレームは作業軸そのものなので、
            // 「格子全体を動かす」には一度作業軸パネルへ行く必要がある。
            if (_interactionMode == InteractionMode.Lattice
                && mode != InteractionMode.Lattice
                && mode != InteractionMode.WorkAxis)
            {
                _latticeHandler?.Cancel();
                _activePanel?.HideTopoToolOverlay();
                _activePanel?.HideBoxSelect();
            }

            // ---------------------------------------------------------------
            // カテゴリ 1 ツール (EdgeBevel / EdgeExtrude / FaceExtrude /
            // FlipFace / Solidify / Rotate / Scale / PrimitivePlace) は
            // MoveToolHandler の共通選択ロジックを流用している。
            // - EdgeBevel / EdgeExtrude / FaceExtrude はフック (OnDragStartExtra 等) に
            //   ツール固有ドラッグ動作を差し込む
            // - FlipFace / Solidify / Rotate / Scale は選択のみフック不要
            //   (ツール動作はサブパネル経由。将来 Rotate/Scale 用ギズモを追加する場合は
            //    フック利用に移行予定)
            // 脱出時は:
            //   - 全フックを null に戻す (次モードで古いフックが発火しないように)
            // 選択モードの復元は行わない (新モードの override 決定で自動的に戻る)。
            // ---------------------------------------------------------------
            bool leavingSharedSelectionTools =
                   (_interactionMode == InteractionMode.EdgeBevel   && mode != InteractionMode.EdgeBevel)
                || (_interactionMode == InteractionMode.EdgeExtrude && mode != InteractionMode.EdgeExtrude)
                || (_interactionMode == InteractionMode.FaceExtrude && mode != InteractionMode.FaceExtrude)
                || (_interactionMode == InteractionMode.FlipFace    && mode != InteractionMode.FlipFace)
                || (_interactionMode == InteractionMode.Solidify    && mode != InteractionMode.Solidify)
                || (_interactionMode == InteractionMode.Rotate      && mode != InteractionMode.Rotate)
                || (_interactionMode == InteractionMode.Scale       && mode != InteractionMode.Scale)
                // 配置ギズモも Rotate / Scale と同じくフックへ委譲する。
                // 解除し損ねると、次のモードでも OnDragStartExtra が true を返し続けて
                // 頂点移動が一切効かなくなる。
                || (_interactionMode == InteractionMode.PrimitivePlace && mode != InteractionMode.PrimitivePlace)
                // 変形も回転ハンドルをフックへ委譲する。解除し損ねると同じ症状になる。
                || (_interactionMode == InteractionMode.Deform      && mode != InteractionMode.Deform)
                // DeleteFace は OnLeftClickExtra で面クリック削除を発火する。
                // 脱出時のフック解除は同じ処理でよい。
                || (_interactionMode == InteractionMode.DeleteFace  && mode != InteractionMode.DeleteFace)
                // 頂点溶解 / 三角4→1 / 面結合 も OnLeftClickExtra でクリック実行する。
                // 脱出時の処理は面削除と同じでよい。
                || (_interactionMode == InteractionMode.VertexDissolve && mode != InteractionMode.VertexDissolve)
                || (_interactionMode == InteractionMode.Tri4To1        && mode != InteractionMode.Tri4To1)
                || (_interactionMode == InteractionMode.FaceMerge      && mode != InteractionMode.FaceMerge)
                || (_interactionMode == InteractionMode.Quad4To1          && mode != InteractionMode.Quad4To1)
                || (_interactionMode == InteractionMode.FaceMergeCollapse && mode != InteractionMode.FaceMergeCollapse);
            if (leavingSharedSelectionTools)
            {
                if (_moveToolHandler != null)
                {
                    _moveToolHandler.OnLeftClickExtra   = null;
                    _moveToolHandler.OnDragStartExtra   = null;
                    _moveToolHandler.OnToolDragExtra    = null;
                    _moveToolHandler.OnToolDragEndExtra = null;
                }
            }

            // 面削除モードから他モードへ移るとき、進入フラグを下ろす。
            // ツールボタンや他ショートカットで直接抜けた場合もここを通るため、
            // ExitDeleteFaceMode を経由しなくてもフラグが取り残されない。
            if (_interactionMode == InteractionMode.DeleteFace && mode != InteractionMode.DeleteFace)
                _deleteFaceModeActive = false;

            // 変形モードへ入り直すときは作業軸フェーズから始める。
            // 「軸を決める → 変形を掛ける」の順に誘導するため。
            // OnPhaseChanged 経由で ApplyDeformToolRouting が走るのを避けたいので
            // _interactionMode を書き換える前に済ませておく。
            if (mode == InteractionMode.Deform && _interactionMode != InteractionMode.Deform
                && _deformHandler != null)
                _deformHandler.Phase = DeformToolHandler.DeformPhase.WorkAxis;

            _interactionMode = mode;

            ReportPerfToolState();

            // ── 選択モードのツール固有 override をここで一括決定する ──────────
            // 進入時に絞り、脱出時に書き戻す方式は復元漏れ・非対称が起きやすい
            // (旧実装では脱出側が MeshSelectMode.All を書き、チェックボックスの
            //  指定が失われていた)。モードが確定したこの一点だけで決めれば、
            // どの経路から来ても実効モードが一意に定まる。
            // null を返すモードはユーザのチェックボックスに従う。
            _toolSelectModeOverride = ResolveToolSelectModeOverride(mode);
            ApplySelectMode();

            // 吸着用ヒットテスト（メッシュ選択を無視）は面追加モードでトグルが ON の
            // ときだけ有効。有効な間はポインタ移動ごとに追加ディスパッチと
            // 頂点数ぶんの読み戻しが走るため、他モードでは必ず切る。
            _viewportManager?.SetSnapHitTestEnabled(
                mode == InteractionMode.AddFace
                && (_addFaceHandler?.SnapToUnselectedObjects ?? false));

            // SelectOnly は毎回リセットし、下の SelectOnly case でのみ再有効化する
            // （他モードへ移ったら選択専用を確実に解除）。将来ギズモ用フックも同様にリセット。
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SelectOnly           = false;
                _moveToolHandler.SuppressBuiltinGizmo = false;
                _moveToolHandler.GizmoHitTestOverride = null;
                _moveToolHandler.SuppressDragSelect   = false;
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
                case InteractionMode.PrimitivePlace:
                    // 配置ギズモ: 選択は MoveToolHandler を維持し、組み込み移動ギズモを
                    // 抑制、フック経由で PrimitivePlaceToolHandler のギズモへ委譲する
                    // (Rotate / Scale と同じ構成)。
                    //
                    // ハンドラごと _primitivePlaceHandler へ差し替えると、その OnLeftClick が
                    // 空で MoveToolHandler も外れるため、ビューポートでの頂点・辺の選択が
                    // クリック・矩形・投げ縄すべて不能になる。穴つなぎ（ブリッジ）の
                    // 種取り込みは選択を要求する (PickBridgeSeeds) ので、
                    // 差し替え方式のままでは図形パネルを開いた時点で操作不能になる。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _primitivePlaceHandler != null && _primitivePlaceHandler.GizmoHitTest(pos, c);
                        // このツールはモデルへ触れない。ギズモに当たらなかったドラッグでも
                        // true を返して通常の頂点移動を抑止する。クリック選択と、
                        // 何も掴んでいない位置からの矩形／投げ縄選択はそのまま効く。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            _primitivePlaceHandler?.BeginGizmoDrag();
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _primitivePlaceHandler?.GizmoDrag(pos, delta);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) => _primitivePlaceHandler?.EndGizmoDrag();
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _primitivePlaceHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.WorkAxis:
                    // 作業軸ギズモ専用。モデルの選択・頂点操作は行わない。
                    _vertexInteractor?.SetToolHandler(_workAxisHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _workAxisHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Camera:
                    // カメラ調整ギズモ専用。モデルの選択・頂点操作は行わない。
                    _vertexInteractor?.SetToolHandler(_cameraHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _cameraHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Deform:
                    ApplyDeformToolRouting();
                    break;
                case InteractionMode.Lattice:
                    // 未開始・格子配置中はメッシュ頂点の選び直しを許し、
                    // 格子変形中だけ格子点の選択・移動へ切り替える。
                    ApplyLatticeToolRouting();
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
                    break;
                case InteractionMode.FlipFace:
                    // ビューポートでは選択のみ (面の単独選択 / Shift 追加 / 矩形選択)。
                    // 面反転自体はサブパネル経由で実行 (本セッション対象外、別件で修正)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Solidify:
                    // ビューポートでは選択のみ (面の単独選択 / Shift 追加 / 矩形選択)。
                    // 厚み付けの実行はサブパネル経由。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
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
                    break;
                case InteractionMode.DeleteFace:
                    // 面削除モード: 面のクリックのみ受け付け、クリックされた面を即削除する。
                    //   SelectOnly         … 軸ギズモ・要素ドラッグ移動を全て無効化
                    //   SuppressDragSelect … 矩形/投げ縄選択を無効化 (ドラッグ全体が無反応)
                    //   Selection.Mode=Face … 面以外のホバーハイライトとクリック選択を無効化
                    // 削除は OnLeftClickExtra から DeleteSelectionTool 経由で実行するため、
                    // Undo と位相変更通知は既存の選択削除と同じ経路に乗る。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnDeleteFaceClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.VertexDissolve:
                    // 頂点溶解モード: 頂点クリックのみ受け付け、その頂点を即溶かす。
                    // 面削除モードと同じ構成（SelectOnly / SuppressDragSelect /
                    // OnLeftClickExtra / Selection.Mode 固定）。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnVertexDissolveClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Tri4To1:
                    // 三角形4→1モード: 面クリックのみ受け付け、その三角形を即統合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnTri4To1Clicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.FaceMerge:
                    // 面結合モード: 辺クリックのみ受け付け、その辺を挟む2面を即結合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnFaceMergeClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Quad4To1:
                    // 四角形4→1モード: 頂点クリックのみ受け付け、その頂点を即統合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnQuad4To1Clicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.FaceMergeCollapse:
                    // 面結合（頂点削除）モード: 辺クリックのみ受け付け、その辺を即結合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnFaceMergeCollapseClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.EdgeTopology:
                    _vertexInteractor?.SetToolHandler(_edgeTopologyHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeTopologyHandler?.UpdateHover(pos, ctx));
                    // Split → Vertex ホバーのみ、Flip/Dissolve → Edge ホバーのみ
                    // (override は ResolveToolSelectModeOverride が同じ規則で決める)
                    break;
                case InteractionMode.Knife:
                    _vertexInteractor?.SetToolHandler(_knifeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _knifeHandler?.UpdateHover(pos, ctx));
                    // 初期段（開始頂点）＝ Vertex ホバー。
                    // (override は ResolveToolSelectModeOverride が _knifeHandler.HoverSelectMode から決める)
                    break;
                case InteractionMode.SkinWeightNumeric:
                    // 数値入力のみで適用する。ビューポートでは MoveToolHandler の
                    // 選択・矩形選択だけを流用し、組み込み移動ギズモは出さない
                    // (Deform と同型)。選択種別は ResolveToolSelectModeOverride で頂点のみ。
                    if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    _skinWeightNumericSubPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    // ウェイトのヒートマップ可視化はペイントツールの機構を流用する。
                    // ActivePanel は SkinWeightPaintTool.VisualizationTargetBone と
                    // MeshSceneRenderer.CollectWeightVisTargets の参照先。
                    SkinWeightPaintTool.ActivePanel = _skinWeightNumericSubPanel;
                    SkinWeightPaintTool.SetVisualizationActive(true);
                    _viewportManager.EnterWeightTargetChanged(ActiveProject);
                    // Undo 対象は PlayerCommandDispatcher がメッシュごとに
                    // SetMeshObject で差し替えるため、ここでは設定しない。
                    // _skinWeightUndoMasterIndex を残すと Undo 書き戻し先 (:608 付近) が
                    // その 1 メッシュに固定され、多メッシュ編集の Undo が壊れる。
                    // -1 にしておけば MeshObject 参照からの逆引きが働く。
                    _skinWeightUndoMasterIndex = -1;
                    break;
                case InteractionMode.SkinWeightPaint:
                    _vertexInteractor?.SetToolHandler(_skinWeightPaintHandler);
                    SkinWeightPaintTool.ActivePanel = _skinWeightPaintPanel;
                    _skinWeightPaintPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    _skinWeightPaintHandler?.OnActivate();
                    // 【可視化の有効化は OnActivate に任せない】
                    // SkinWeightPaintToolHandler.OnActivate は GetToolContext() が null だと
                    // 無言で return し、その中にある IsVisualizationActive = true に届かない。
                    // 結果ヒートマップが一切出ない状態になっていた。
                    // SkinWeightNumeric 側と同じく、ここで無条件に立てる。
                    SkinWeightPaintTool.SetVisualizationActive(true);
                    // 進入直後に色を焼き込む。これが無いとボーンのドロップダウンを
                    // 触るまで PresentAll が走らず色が出ない。
                    _viewportManager.EnterWeightTargetChanged(ActiveProject);
                    // Undo 対象は SkinWeightPaintTool がストローク中にメッシュごとへ
                    // SetMeshObject で差し替える（ブラシは選択オブジェクト全件をまたぐ）。
                    // _skinWeightUndoMasterIndex を残すと Undo 書き戻し先 (:608 付近) が
                    // 1 メッシュに固定され、複数メッシュの Undo が壊れる。
                    _skinWeightUndoMasterIndex = -1;
                    break;
            }

            // ホバー(頂点ヒットテスト)抑止: ボーン・モーフ系(None)・ボーンエディタ(ObjectMove)・
            // SkinWeightPaint では移動用の頂点ホバーが不要なため抑止する。
            _viewportManager?.SetSuppressHover(
                mode == InteractionMode.None ||
                mode == InteractionMode.ObjectMove ||
                mode == InteractionMode.SkinWeightPaint);

            // InteractionMode ボタンのハイライト (2 系統色の片方)
            UpdateInteractionButtonHighlight();

            // ギズモ overlay をモード切替と同時に更新する。
            // PlayerViewportPanel._gizmoData は UpdateGizmo / HideGizmo でしか書き換わらず、
            // これを呼ぶのは UpdateGizmoOverlay だけ。従来ここで呼んでいなかったため、
            // モード切替後にビューポート上でマウスを動かす (EnterHoverChanged が
            // OnRefreshGizmoOverlay を発火する) まで前モードのギズモが残っていた。
            // _viewportManager は readonly の inline 初期化で null にならず、
            // UpdateGizmoOverlay 自身が _activePanel == null を先頭で弾くため安全。
            UpdateGizmoOverlay();
        }

        // ================================================================
        // 選択モード（頂点/辺/面/線分）の単一権限
        //
        // 書き込みは ApplySelectMode() だけが行う。他の場所から
        // SelectionState.Mode / MeshContext.SelectMode へ代入してはならない。
        // ================================================================

        /// <summary>
        /// 左ペインのチェックボックスから _userSelectMode を読み取る。
        /// 全 OFF は頂点へフォールバックする（何も選べないモードを作らない）。
        /// </summary>
        private void ReadUserSelectModeFromToggles()
        {
            if (_layoutRoot?.SelModeVertexToggle == null) return;

            MeshSelectMode m = MeshSelectMode.None;
            if (_layoutRoot.SelModeVertexToggle.value) m |= MeshSelectMode.Vertex;
            if (_layoutRoot.SelModeEdgeToggle  .value) m |= MeshSelectMode.Edge;
            if (_layoutRoot.SelModeFaceToggle  .value) m |= MeshSelectMode.Face;
            if (_layoutRoot.SelModeLineToggle  .value) m |= MeshSelectMode.Line;
            if (m == MeshSelectMode.None) m = MeshSelectMode.Vertex;

            _userSelectMode = m;
        }

        /// <summary>現在の実効選択モード。ツール固有 override があればそれが優先。</summary>
        private MeshSelectMode EffectiveSelectMode
        {
            get
            {
                var m = _toolSelectModeOverride ?? _userSelectMode;
                return m == MeshSelectMode.None ? MeshSelectMode.Vertex : m;
            }
        }

        /// <summary>
        /// 実効選択モードを、判定側が実際に読む全ての SelectionState へ書き込む。
        ///
        /// 【書き込み先と、その理由】
        ///   1. 現モデルの全 MeshContext.Selection
        ///      … MoveToolHandler.UpdateAffectedVertices が「メッシュごとの」Mode を読む。
        ///        1 個だけに書くとメッシュを切り替えた瞬間に挙動が変わる。
        ///   2. _selectionState
        ///      … 初期化直後や、メッシュ未選択時に判定側が掴んでいる素の SelectionState。
        ///   3. _selectionOps.SelectionState
        ///      … MoveToolHandler / AdvancedSelect のクリック・矩形選択がここの Mode を読む。
        ///   4. _renderer.CurrentSelectionState
        ///      … GPU ホバーの種別絞り込み (UnifiedMeshSystem.ProcessMouseUpdate) が読む実体。
        ///        1〜3 と別インスタンスになり得るため、ここを外すとホバーだけ効かなくなる。
        ///
        /// 【無効種別の選択解除】
        /// 実効モードが「変化したとき」だけ、無効になった種別の選択を解除する。
        /// 例: 辺を選択中にチェックを頂点のみへ変えた、面追加ツールへ入った、など。
        /// 毎回解除しないのは、モード外の種別を意図的に選ぶ操作
        /// （高度選択の頂点/辺/面同時選択など）の結果まで消さないため。
        /// </summary>
        private void ApplySelectMode()
        {
            var m = EffectiveSelectMode;

            bool modeChanged = !_lastAppliedSelectMode.HasValue
                               || _lastAppliedSelectMode.Value != m;
            _lastAppliedSelectMode = m;

            bool released = false;

            var model = ActiveProject?.CurrentModel;
            if (model?.MeshContextList != null)
            {
                foreach (var mc in model.MeshContextList)
                {
                    if (mc?.Selection == null || mc.Type == MeshType.Bone) continue;
                    mc.Selection.Mode = m;
                    if (modeChanged) released |= ReleaseDisabledSelections(mc.Selection, m);
                }
            }

            released |= WriteSelectMode(_selectionState,               m, modeChanged);
            released |= WriteSelectMode(_selectionOps?.SelectionState, m, modeChanged);
            released |= WriteSelectMode(_renderer?.CurrentSelectionState, m, modeChanged);

            // 解除が起きたときだけ GPU 選択フラグとサブパネルを更新する。
            if (released) _selectionOps?.OnSelectionChanged?.Invoke();

            _activePanel?.MarkDirtyRepaint();
        }

        /// <summary>1 個の SelectionState へモードを書き、必要なら無効種別を解除する。</summary>
        private static bool WriteSelectMode(SelectionState sel, MeshSelectMode m, bool release)
        {
            if (sel == null) return false;
            sel.Mode = m;
            return release && ReleaseDisabledSelections(sel, m);
        }

        /// <summary>モードで無効になった種別の選択を解除する。解除したら true。</summary>
        private static bool ReleaseDisabledSelections(SelectionState sel, MeshSelectMode m)
        {
            if (sel == null) return false;

            bool changed = false;
            if (!m.Has(MeshSelectMode.Vertex) && sel.Vertices.Count > 0) { sel.Vertices.Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Edge)   && sel.Edges   .Count > 0) { sel.Edges   .Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Face)   && sel.Faces   .Count > 0) { sel.Faces   .Clear(); changed = true; }
            if (!m.Has(MeshSelectMode.Line)   && sel.Lines   .Count > 0) { sel.Lines   .Clear(); changed = true; }
            return changed;
        }

        /// <summary>
        /// ツール固有 override を設定して即適用する。
        /// ツール内部でクリック対象が切り替わる場合（ナイフの段、EdgeTopology の
        /// サブモード、高度選択のサブモード）にハンドラ側から呼ばれる。
        /// </summary>
        private void SetToolSelectModeOverride(MeshSelectMode mode)
        {
            _toolSelectModeOverride = mode;
            ApplySelectMode();
        }

        /// <summary>
        /// InteractionMode ごとのツール固有選択モードを返す。null はユーザ指定に従う。
        ///
        /// ここが「チェックボックスとは無関係にツールが要求する種別」の唯一の一覧。
        /// 例: 面追加は面を張る点を拾うツールなので、チェックボックスの内容に関わらず頂点のみ。
        /// </summary>
        private MeshSelectMode? ResolveToolSelectModeOverride(InteractionMode mode)
        {
            switch (mode)
            {
                // 面追加: 面を張る頂点だけを拾う。辺・面のホバーは有害。
                case InteractionMode.AddFace:           return MeshSelectMode.Vertex;

                // 辺を対象にするツール
                case InteractionMode.EdgeBevel:         return MeshSelectMode.Edge;
                case InteractionMode.FaceMerge:         return MeshSelectMode.Edge;
                case InteractionMode.FaceMergeCollapse: return MeshSelectMode.Edge;
                case InteractionMode.EdgeExtrude:       return MeshSelectMode.Edge | MeshSelectMode.Line;

                // 面を対象にするツール
                case InteractionMode.FaceExtrude:       return MeshSelectMode.Face;
                case InteractionMode.FlipFace:          return MeshSelectMode.Face;
                case InteractionMode.Solidify:          return MeshSelectMode.Face;
                case InteractionMode.DeleteFace:        return MeshSelectMode.Face;
                case InteractionMode.Tri4To1:           return MeshSelectMode.Face;

                // 頂点を対象にするツール
                case InteractionMode.SkinWeightNumeric: return MeshSelectMode.Vertex;
                case InteractionMode.VertexDissolve:    return MeshSelectMode.Vertex;
                case InteractionMode.Quad4To1:          return MeshSelectMode.Vertex;

                // サブモードで対象が変わるツール
                case InteractionMode.EdgeTopology:
                    return (_edgeTopologyHandler?.ModePublic ?? Poly_Ling.Tools.EdgeTopoMode.Flip)
                               == Poly_Ling.Tools.EdgeTopoMode.Split
                           ? MeshSelectMode.Vertex     // Split は頂点クリックで対角を指定
                           : MeshSelectMode.Edge;      // Flip / Dissolve は辺クリック
                case InteractionMode.Knife:
                    return _knifeHandler?.HoverSelectMode ?? MeshSelectMode.Vertex;
                case InteractionMode.AdvancedSelect:
                    // Belt / EdgeLoop は辺と補助線分、ShortestPath は頂点。
                    // 属性系サブモードは null（ユーザ指定に従う）。
                    return _advancedSelectHandler?.HoverSelectModeOverride;

                // 一時選択サブツール (矩形 / 投げ縄) は、呼び出し元ツールの絞り込みを引き継ぐ。
                // ここでユーザ指定へ戻すと、面ツール中に矩形選択したら頂点が選ばれ、
                // 復帰時にその選択が解除される、という噛み合わない挙動になる。
                case InteractionMode.SelectOnly:        return _toolSelectModeOverride;

                default:
                    // 頂点移動・回転・拡縮・彫刻・格子・オブジェクト移動などは
                    // チェックボックスの指定をそのまま使う。
                    return null;
            }
        }

        /// <summary>
        /// EdgeTopology のサブモード (Flip/Split/Dissolve) 切替に追従して
        /// ツール固有 override を更新する。Split は頂点クリックで対角を指定、
        /// Flip/Dissolve は辺クリックで実行するため、不要な種別のホバーを抑制する。
        /// </summary>
        private void ApplySelectionModeForEdgeTopology(Poly_Ling.Tools.EdgeTopoMode mode)
        {
            if (_interactionMode != InteractionMode.EdgeTopology) return;
            SetToolSelectModeOverride(mode == Poly_Ling.Tools.EdgeTopoMode.Split
                ? MeshSelectMode.Vertex
                : MeshSelectMode.Edge);
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
        /// スキンW数値設定モードで焼き込んだ頂点カラーを消す。
        /// 対象は数値設定パネルと同じく選択中の描画メッシュ全件
        /// （CurrentTargetMesh は常に -1 を返すため、MeshSceneRenderer 側の
        /// CollectWeightVisTargets も SelectedDrawableMeshIndices を使う）。
        /// </summary>
        private void ClearNumericWeightVisualization()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            foreach (var mc in Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model))
                if (mc?.UnityMesh != null) mc.UnityMesh.colors = null;
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

            // ウェイトごと書き戻すため種別も合わせる。
            // src はスナップショット由来で SkinKind を持っているのでそれを正とする。
            dst.SetSkinKind(src.SkinKind);
        }

        /// <summary>
        /// 現在アクティブなツールに応じたホバー対象種別を返す。
        /// Phase 2b-1 暫定実装: ホバーを完全に抑制すべきツール（SkinWeightPaint / Sculpt 等）では
        /// None を、それ以外では Vertex を返す（Vertex は「ホバー有効」の仮値。現状の入口側は
        /// None 判定のみで分岐するため、既存の GPU ホバー優先度 (頂点>辺>面) がそのまま動作する）。
        /// Phase 2b 以降で Edge / Face / Bone / Gizmo の厳密な kind 分岐を実装する。
        /// </summary>
        /// <summary>
        /// 選択中メッシュのローカル拡大縮小を頂点位置へ畳み込む（左ペインのボタン）。
        /// スキップした対象がある場合は理由付きの警告を左ペインの Status に出す。
        /// </summary>
        private string BakeObjectScale()
        {
            var model = ActiveProject?.CurrentModel;
            if (_commandDispatcher == null)
            {
                _status = "拡大縮小をベイク: コマンド未初期化";
                return _status;
            }

            _commandDispatcher.BakeObjectScale(model, out string message);
            _status = message;
            return message;
        }

        /// <summary>
        /// 頂点ホバーが抑止されるモード (HoverTargetKind.None) でも、スクリーン座標だけで
        /// 決まるオーバーレイは更新する。
        /// <para>
        /// 対象は ObjectMove / Camera のギズモ軸ホバーと、Sculpt のブラシ円。
        /// ブラシ円の半径は SculptToolHandler.ScreenRadiusFromWorldRadius がカメラと
        /// PreviewRect だけから求めるため、GPU ヒットテストを必要としない。
        /// </para>
        /// <para>
        /// 実体は PlayerViewportManager.NotifyScreenOnlyHover。どのハンドラへ渡すかは
        /// SetInteractionMode で登録済みの ActiveToolHandler に任せる。
        /// 要素 (頂点/辺/面) のホバーをこの経路へ移すことは禁止（同メソッドの注記参照）。
        /// </para>
        /// </summary>
        private void UpdateScreenOnlyHover(PlayerViewport vp, Vector2 localPos)
        {
            // ホバー種別が None のモードのうち、スクリーン座標だけで決まる表示を
            // 持つものだけ更新する。
            if (_interactionMode != InteractionMode.ObjectMove &&
                _interactionMode != InteractionMode.Camera &&
                _interactionMode != InteractionMode.Sculpt) return;
            _viewportManager.NotifyScreenOnlyHover(vp, localPos);
        }

        private HoverTargetKind GetCurrentHoverTargetKind()
        {
            switch (_interactionMode)
            {
                case InteractionMode.None:
                case InteractionMode.ObjectMove:
                case InteractionMode.SkinWeightPaint:
                case InteractionMode.Sculpt:
                // カメラ調整はモデル要素を触らないため GPU ヒットテストを走らせない。
                case InteractionMode.Camera:
                    return HoverTargetKind.None;
                default:
                    return HoverTargetKind.Vertex;
            }
        }

        /// <summary>
        /// 左ペインの操作対象となる選択メッシュのインデックス配列を返す。
        /// 選択が空のときはアクティブメッシュ 1 個へフォールバックする。
        /// PlayerCommandDispatcher.CollectSelectedMeshContexts と同じ規則。
        /// </summary>
        private int[] CollectSelectedMeshIndices()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return System.Array.Empty<int>();

            var sel = model.SelectedDrawableMeshIndices;
            if (sel != null && sel.Count > 0) return sel.ToArray();

            int active = model.ActiveMeshIndex;
            if (active >= 0 && active < model.MeshContextCount) return new[] { active };

            return System.Array.Empty<int>();
        }

        /// <summary>
        /// 左ペインの「法線自動計算」トグルを選択メッシュの PreserveNormals から書き戻す。
        /// 対象メッシュが全て PreserveNormals == false のときだけ ON。
        /// 混在・対象なしは OFF にする。
        /// </summary>
        private void SyncNormalRecalcToggle()
        {
            var tog = _layoutRoot?.AutoRecalcNormalsToggle;
            if (tog == null) return;

            var model = ActiveProject?.CurrentModel;
            var indices = CollectSelectedMeshIndices();

            bool autoOn = false;
            if (model != null && indices.Length > 0)
            {
                autoOn = true;
                foreach (int idx in indices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject == null || mc.MeshObject.PreserveNormals)
                    {
                        autoOn = false;
                        break;
                    }
                }
            }

            _isSyncingNormalRecalcToggle = true;
            tog.SetValueWithoutNotify(autoOn);
            _isSyncingNormalRecalcToggle = false;
        }

        // ================================================================
        // 軌道回転の中心（ピボット）
        // ================================================================

        /// <summary>
        /// 軌道回転の中心をワールド座標で返す。null なら従来どおり
        /// OrbitCameraController.Target を中心に回る。
        ///
        /// OrbitCameraController.GetOrbitPivot として配線され、軌道ドラッグ
        /// 開始時に 1 回だけ呼ばれる。選択変更イベントからは呼ばれないため、
        /// 選択しただけでは視点は動かない。
        ///
        /// 優先順位:
        ///   1. 「現在の選択を中心に」釦で確定した固定ピボット
        ///   2. 「回転はローカル原点中心」が ON ならローカル原点（ピボット）の重心
        ///   3. どちらでもなければ null
        /// </summary>
        private Vector3? ComputeOrbitPivot()
        {
            if (_explicitOrbitPivot.HasValue) return _explicitOrbitPivot;
            if (_orbitAroundLocalOrigin)      return ComputeLocalOriginCentroid();
            return null;
        }

        /// <summary>
        /// 選択されている要素（頂点 / 辺 / 面 / 線分）のワールド重心。
        /// 1 点も無ければ null。
        ///
        /// 集計規則は MoveToolHandler.UpdateAffectedVertices と同じ。
        /// SelectionState の Vertices / Faces / Lines はメッシュ内ローカル番号
        /// なので、選択メッシュごとにその MeshContext.Selection を見る。
        /// </summary>
        private Vector3? ComputeElementCentroid()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null) continue;

                var sel = mc.Selection;
                if (sel == null) continue;

                var mo = mc.MeshObject;
                var affected = new HashSet<int>();

                foreach (var v  in sel.Vertices) affected.Add(v);
                foreach (var e  in sel.Edges)    { affected.Add(e.V1); affected.Add(e.V2); }
                foreach (var fi in sel.Faces)
                    if (fi >= 0 && fi < mo.FaceCount)
                        foreach (var vi in mo.Faces[fi].VertexIndices)
                            affected.Add(vi);
                foreach (var li in sel.Lines)
                    if (li >= 0 && li < mo.FaceCount)
                    {
                        var face = mo.Faces[li];
                        if (face.VertexCount == 2)
                        { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                    }

                // ローカル頂点をワールド変換してから集計する。スキンド頂点に
                // 実際に適用される行列はメッシュの WorldMatrix ではなくボーンの
                // ブレンドなので、MeshContext.LocalToWorld を使う
                // （MoveToolHandler.UpdateGizmoState と同じ）。
                foreach (int vi in affected)
                    if (vi >= 0 && vi < mo.VertexCount)
                    { sum += mc.LocalToWorld(vi, mo.Vertices[vi].Position); count++; }
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        /// <summary>
        /// 選択オブジェクト（ボーン + 描画メッシュ）のローカル原点の重心。
        /// 1 件も無ければ null。
        ///
        /// ローカル原点 = MeshContext.WorldMatrix の平行移動成分。これは
        /// PivotOffsetTool が言う「ピボット原点」と同じ点であり、
        /// ObjectMoveTool.UpdateGizmoCenter が使う点とも一致する。
        /// </summary>
        private Vector3? ComputeLocalOriginCentroid()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return null;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int idx in model.SelectedBoneIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }
            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                // ボーン選択に既に含まれていれば重複させない
                if (model.SelectedBoneIndices.Contains(idx)) continue;
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }

            return count > 0 ? (Vector3?)(sum / count) : null;
        }

        private void NotifyPanels(ChangeKind kind)
        {
            var project = ActiveProject;
            if (project == null || _panelContext == null) return;
            var view = new PlayerProjectView(project);
            _panelContext.Notify(view, kind);

            // リモートサーバ稼働時、本体の選択/モデル変更を接続クライアントへ配信する。
            // （エディタは Tick を回さないため、この中心経路から通知する）
            if (_remoteMode == RemoteMode.Server && _playerServer != null)
                _playerServer.NotifySelectionChanged();

            if (_interactionMode == InteractionMode.ObjectMove)
                _boneEditorSubPanel?.Refresh();

            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                _blendSubPanel?.OnSelectionChanged();

            // シュリンカーは選択状態を使わないため ModelSwitch だけに追随する
            // （Selection で作り直すとプレビュー中の状態が失われる）。
            if (kind == ChangeKind.ModelSwitch)
                _shrinkSubPanel?.SetModel(ActiveProject?.CurrentModel);

            // 左ペインの「法線自動計算」トグルを選択メッシュの状態へ追随させる。
            SyncNormalRecalcToggle();

            foreach (var (section, refresh) in _sectionRefreshPairs)
                if (section?.style.display == DisplayStyle.Flex) refresh();

            if (_layoutRoot?.ModelBlendSection != null &&
                _layoutRoot.ModelBlendSection.style.display == DisplayStyle.Flex)
                _modelBlendSubPanel?.OnViewChanged(view, kind);

            // 描画準備の再実行（OnRenderObject 経路の Submit 用データ更新）。
            // kind で入口を分ける。EnterTopologyChanged は RebuildAdapter を通り、
            // UnifiedSystemAdapter を Dispose して全メッシュから GPU バッファを
            // 作り直す。選択や属性が変わっただけでこれを呼ぶと、リストを触るたびに
            // 全再構築が走って待たされるため、その2つは EnterSelectionChanged に回す。
            bool __full = kind == ChangeKind.ListStructure || kind == ChangeKind.ModelSwitch;
            // 属性変更は Hidden / Locked を GPU へ書き戻す軽量経路へ回す。
            // RebuildAdapter は伴わない。
            bool __attr = kind == ChangeKind.Attributes;
            // kind.ToString() と三項の文字列選択は引数側で必ず評価される。
            // スイッチをここで見てから呼ぶ。
            if (PLDiag.Enabled && PLDiag.Notify)
            {
                PLDiag.NotifyKind(kind.ToString(),
                    __full ? "EnterTopologyChanged"
                           : (__attr ? "EnterMeshAttributesChanged" : "EnterSelectionChanged"));
            }
            if (__full)
                _viewportManager.EnterTopologyChanged(ActiveProject);
            else if (__attr)
                _viewportManager.EnterMeshAttributesChanged(ActiveProject);
            else
                _viewportManager.EnterSelectionChanged(ActiveProject);

            OnChanged?.Invoke(kind);
        }

        // ================================================================
        // クライアントイベント
        // ================================================================

        private void OnConnected()
        {
            _status = "接続済み";
            // 自タイプをサーバへ登録（list 系と同じ枠組み）。userName 既定は空（名前なし）。
            _client?.RegisterClientType("playerViewer", "");
        }
        private void OnDisconnected() { _status = "切断"; }

        private void OnPushReceived(string json)
        {
            // サーバの実際の push 名に合わせる（旧 mesh_changed/model_changed は発行元なし）。
            // 構造変更（一覧変更）を契機に再フェッチする。
            if (json.Contains("\"event\":\"meshListChanged\""))
                FetchProject();
        }

        /// <summary>
        /// サーバから push された PositionsOnly を適用する（S→C 連動）。
        /// メインスレッドで呼ばれる。
        ///
        /// v2 ヘッダの ObjectId で対象を確定する。
        /// 旧 v1（ObjectId=0）は対象を運べないため、従来どおり選択メッシュへ当てる。
        /// </summary>
        private void ApplyRemotePositions(byte[] data)
        {
            if (data == null) return;
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null || header.Value.MessageType != BinaryMessageType.PositionsOnly) return;

            var h       = header.Value;
            var project = ActiveProject;
            if (project == null) return;

            ModelContext model = null;
            MeshContext  mc    = null;

            if (h.HasTarget)
            {
                // 指定モデル→全モデルの順に安定IDで探す
                if (h.ModelIndex >= 0 && h.ModelIndex < project.ModelCount)
                {
                    model = project.Models[h.ModelIndex];
                    mc    = FindMeshByObjectId(model, h.ObjectId);
                }
                if (mc == null)
                {
                    for (int mi = 0; mi < project.ModelCount; mi++)
                    {
                        var m = project.Models[mi];
                        var found = FindMeshByObjectId(m, h.ObjectId);
                        if (found != null) { model = m; mc = found; break; }
                    }
                }
                if (mc == null) return;   // 未知のオブジェクト。無視する。
            }
            else
            {
                // v1 互換: 対象未指定なので選択メッシュへ当てる
                model = project.CurrentModel;
                mc    = model?.ActiveMeshContext;
            }

            if (mc?.MeshObject == null) return;

            // 頂点数が食い違うなら適用しない（トポロジ変更後の古い更新）
            if (h.VertexCount != (uint)mc.MeshObject.VertexCount) return;

            RemoteBinarySerializer.Deserialize(data, mc.MeshObject);
            _viewportManager.SyncMeshPositionsAndTransform(mc, model);
            _viewportManager.UpdateTransform();
        }

        /// <summary>安定IDで MeshContext を引く。</summary>
        private static MeshContext FindMeshByObjectId(ModelContext model, ulong objectId)
        {
            if (model == null || objectId == 0UL) return null;
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.ObjectId == objectId) return mc;
            }
            return null;
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
                _vertexHoleHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _solidifyHandler?.SetProject(ActiveProject);
                _deleteSelectionHandler?.SetProject(ActiveProject);
                _vertexDissolveHandler?.SetProject(ActiveProject);
                _tri4To1Handler?.SetProject(ActiveProject);
                _faceMergeHandler?.SetProject(ActiveProject);
                _quad4To1Handler?.SetProject(ActiveProject);
                _faceMergeCollapseHandler?.SetProject(ActiveProject);
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
