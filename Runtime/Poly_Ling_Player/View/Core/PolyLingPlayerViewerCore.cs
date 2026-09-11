// PolyLingPlayerViewerCore.cs
// PolyLingPlayerViewer のロジック本体（MonoBehaviour 非依存プレーンクラス）
//
// PolyLingPlayerViewer（MonoBehaviour ラッパー）と
// PolyLingPlayerEditorWindow（EditorWindow ラッパー）の両方から使う。
//
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【partial の分担】このファイルは公開型・設定・サブシステムと状態のフィールドだけを持つ。
//   Lifecycle.cs            公開ライフサイクル API・性能ログ
//   VertexInteraction.cs    頂点インタラクションのセットアップ
//   Overlay.cs / Overlay.Tools.cs   オーバーレイ更新（共通・ギズモ / ツール別）
//   Layout.cs               BuildLayout 本体・左ペインの配線・選択モード・表示トグル
//   Layout.Panels.cs / Layout.VertexTools.cs / Layout.EditTools.cs / Layout.TestPanels.cs
//                           BuildLayout の段（サブパネルの生成と配線）
//   Shortcuts.cs            キーボードショートカット
//   Panels.cs               パネル表示切替（Show*Panel）
//   WorkAxis.cs             作業用ローカル軸
//   ViewAids.cs             キャプチャ・カメラ調整・下絵
//   ButtonHighlight.cs      ボタンのアクティブ色・ハイライト
//   ObjectArrayBridge.cs / BridgeExecute.cs   歪み複製・穴つなぎ・辺群ブリッジ
//   GeneratedMesh.cs        生成メッシュの受け口とモデルへの配置
//   Import.cs               読み込みと読込後オプション
//   Tools.cs                ツール切り替え・一時選択・結合・面削除
//   InteractionMode.cs      3D 操作モードの切り替え
//   SelectMode.cs           選択モードの単一権限
//   ModelSync.cs            モデル切り替え・SyncUI・ディスパッチとパネル通知・軌道回転の中心
//   Remote.cs               クライアント／受信イベント・フェッチ
//   UvEditMode.cs           UV 編集モード
//   CreateCommands.cs (.FileIO.cs / .Topology.cs)   生成系コマンドの受け口

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
    public partial class PolyLingPlayerViewerCore
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
        private enum InteractionMode { None, VertexMove, ObjectMove, PivotOffset, Sculpt, AdvancedSelect, SkinWeightPaint, SkinWeightNumeric, AddFace, EdgeBevel, EdgeExtrude, FaceExtrude, EdgeTopology, Knife, FlipFace, Solidify, Rotate, Scale, SelectOnly, PrimitivePlace, WorkAxis, Deform, Lattice, DeleteFace, VertexDissolve, Tri4To1, FaceMerge, Quad4To1, Camera, EdgeBridge, PointDefinedPrimitive }
        private InteractionMode               _interactionMode = InteractionMode.VertexMove;

        // パネルごとの「ビューポートで選択する」チェックの保存キー。
        // 他パネルへ広げるときはここへキーを足し、Build 直後に
        // AttachPanelSelectToggle、Show～Panel を ShowRightPanelSelectable にする。
        // 面追加ツールで非選択オブジェクトの頂点へ吸着したときのプレビュー色。
        // 選択メッシュへの吸着（シアン）と区別するためのマゼンタ。
        private static readonly Color AddFaceUnselectedSnapColor = new Color(1f, 0.35f, 0.9f, 0.95f);

        private const string PanelSelectKeyVertexHole  = "VertexHole";
        private const string PanelSelectKeyHoleRingCount = "HoleRingCount";
        private const string PanelSelectKeyEdgeBridge    = "EdgeBridge";
        private const string PanelSelectKeyThinPlateMorph = "ThinPlateMorph";
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

        /// <summary>
        /// 図形生成パネルの配置ギズモ表示と姿勢仮表示の設定。1 個だけ持ち、
        /// PrimitivePlaceToolHandler と 3D連携サブパネルで共有する。
        /// 「描画オブジェクトの姿勢」の ObjectMoveSettings とは別物で連動しない。
        /// </summary>
        private readonly PrimitivePlaceSettings _primitivePlaceSettings = new PrimitivePlaceSettings();
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
        private PlayerShrinkSubPanel         _shrinkFaceSubPanel;
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
        private PlayerThinPlateMorphSubPanel    _thinPlateMorphSubPanel;
        private PlayerFaceHideSubPanel          _faceHideSubPanel;
        private PlayerMeshSelectionSetSubPanel  _meshSelSetSubPanel;
        private PlayerObjectGroupSubPanel       _objectGroupSubPanel;
        private PlayerMergeMeshesSubPanel    _mergeMeshesSubPanel;
        private PlayerBooleanSubPanel        _booleanSubPanel;
        private PlayerMorphSubPanel          _morphSubPanel;
        private PlayerMorphCreateSubPanel    _morphCreateSubPanel;
        private PlayerTPoseSubPanel          _tposeSubPanel;
        private PlayerHumanoidMappingSubPanel _humanoidMappingSubPanel;

        /// <summary>
        /// 揺れもの編集（VRM SpringBone のオーサリング）。
        /// システムデバッグの「スプリングボーン検証」（_springBoneTestSubPanel）
        /// とは別物で、こちらが通常の編集機能。
        /// </summary>
        private PlayerSpringBoneSubPanel     _springBoneSubPanel;

        /// <summary>
        /// 当たり判定（VRM SpringBone の collider）の作成と編集。
        /// 揺れもの編集はまとまり（グループ）の名前だけを扱う。
        /// </summary>
        private PlayerSpringBoneColliderSubPanel _springBoneColliderSubPanel;

        /// <summary>
        /// Humanoid マッスル可動域（HumanLimit）の編集。
        /// Humanoid 割当があるボーンにだけ効く。
        /// </summary>
        private PlayerHumanLimitSubPanel _humanLimitSubPanel;

        /// <summary>
        /// VRM 出力設定（作者情報・許諾・視線・一人称）。
        /// 出力ごとの上書きは PlayerExportSubPanel 側が持つ。
        /// </summary>
        private PlayerVrmSettingsSubPanel _vrmSettingsSubPanel;

        private PlayerMirrorSubPanel         _mirrorSubPanel;
        private PlayerQuadDecimatorSubPanel  _quadDecimatorSubPanel;
        private PlayerAlignVerticesSubPanel       _alignVerticesSubPanel;
        private AlignVerticesToolHandler          _alignVerticesHandler;
        private PlayerPipeAlignSubPanel           _pipeAlignSubPanel;
        private PipeAlignToolHandler              _pipeAlignHandler;
        private PlayerSurfaceSnapSubPanel         _surfaceSnapSubPanel;
        private SurfaceSnapToolHandler            _surfaceSnapHandler;
        private PlayerPlaceObjectReshapeSubPanel  _placeObjectReshapeSubPanel;
        private PlaceObjectReshapeToolHandler     _placeObjectReshapeHandler;
        private PlayerPlanarizeAlongBonesSubPanel _planarizeAlongBonesSubPanel;
        private PlanarizeAlongBonesToolHandler    _planarizeAlongBonesHandler;
        private PlayerSmoothEdgesSubPanel         _smoothEdgesSubPanel;
        private SmoothEdgesToolHandler            _smoothEdgesHandler;
        private PlayerMergeVerticesSubPanel       _mergeVerticesSubPanel;
        private MergeVerticesToolHandler          _mergeVerticesHandler;
        private PlayerSplitVerticesSubPanel       _splitVerticesSubPanel;
        private PlayerVertexHoleSubPanel          _vertexHoleSubPanel;
        private PlayerVertexDissolveSubPanel      _vertexDissolveSubPanel;
        // 穴頂点数合わせ。ブリッジと同じ種の拾い方・マーカー表示を使う。
        private PlayerHoleRingCountSubPanel       _holeRingCountSubPanel;
        private PlayerRobotBuildTestSubPanel      _robotBuildTestSubPanel;
        private PlayerEdgeBridgeSubPanel          _edgeBridgeSubPanel;
        private EdgeBridgeToolHandler             _edgeBridgeHandler;
        private PlayerTri4To1SubPanel             _tri4To1SubPanel;
        private PlayerFaceMergeSubPanel           _faceMergeSubPanel;
        private PlayerQuad4To1SubPanel            _quad4To1SubPanel;
        // 頂点IDユーティリティ。ID を使う突き合わせ操作の前段で状態を確認・修復する。
        private PlayerVertexIdSubPanel           _vertexIdSubPanel;
        private PlayerPartsIdSubPanel            _partsIdSubPanel;
        // モデル間頂点データ転送。メッシュのペアを明示して 1 対 1 で転送する。
        private PlayerVertexTransferSubPanel     _vertexTransferSubPanel;
        private SplitVerticesToolHandler          _splitVerticesHandler;
        private VertexHoleToolHandler             _vertexHoleHandler;
        private EdgeRibbonFaceToolHandler        _edgeRibbonFaceHandler;
        private VertexDissolveToolHandler         _vertexDissolveHandler;
        private HoleRingCountToolHandler          _holeRingCountHandler;
        private Tri4To1ToolHandler                _tri4To1Handler;
        private FaceMergeToolHandler              _faceMergeHandler;
        private Quad4To1ToolHandler               _quad4To1Handler;
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

        // ================================================================
        // 「選んだ要素の頂点も選択する」（辺／面／線分ごと）
        //
        // MoveToolHandler.ExpandLinkedVertices が、辺／面／線分の選択を構成頂点へ
        // 展開するかどうかを種別単位で決める。ここに入る MeshSelectMode は
        // 「どの種別を頂点へ展開するか」の集合であって、選択モード（何を選べるか）
        // とは別物。Edge / Face / Line の 3 ビットだけを使う。
        //
        // 書き込みは ReadExpandToVertexKindsFromToggles() だけが行う。
        // 保存は SelectModePrefKey とは別キーにする（4bit の意味を変えないため）。
        // ================================================================
        private MeshSelectMode _expandToVertexKinds =
            MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line;
        private const string ExpandToVertexPrefKey = "LeftPane.ExpandToVertexKinds";
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
        private PlayerLineExtrudeSubPanel         _lineExtrudeSubPanel;
        private LineExtrudeToolHandler            _lineExtrudeHandler;
        private PlayerMediaPipeFaceDeformSubPanel _mediaPipeSubPanel;
        private PlayerVMDTestSubPanel        _vmdTestSubPanel;
        private PlayerCommandSchemaSubPanel  _commandSchemaSubPanel;
        private PlayerOriginTestSubPanel     _originTestSubPanel;
        private PlayerSkinTestSubPanel       _skinTestSubPanel;
        private PlayerSpringBoneTestSubPanel _springBoneTestSubPanel;
        private PlayerFrillSkirtTestSubPanel _frillSkirtTestSubPanel;
        private PlayerSpringSkinScenarioSubPanel _springSkinScenarioSubPanel;
        private PlayerSpringSkinPipeScenarioSubPanel _springSkinPipeScenarioSubPanel;
        private PlayerPipeHairTestSubPanel   _pipeHairTestSubPanel;
        private PlayerBarnacleTestSubPanel   _barnacleTestSubPanel;
        private PlayerRevolutionTestSubPanel _revolutionTestSubPanel;
        private PlayerProfile2DTestSubPanel  _profile2DTestSubPanel;
        private PlayerPmxToMqoTestSubPanel   _pmxToMqoTestSubPanel;
        private PlayerMqoToPmxTestSubPanel   _mqoToPmxTestSubPanel;
        private PlayerUnityClipTestSubPanel  _unityClipTestSubPanel;
        private PlayerUnityClipToVrmaSubPanel _unityClipToVrmaSubPanel;
        private PlayerVmdToVrmaSubPanel     _vmdToVrmaSubPanel;
        private PlayerMotionClipTestSubPanel _motionClipTestSubPanel;

        // 下絵（3D背面に敷く参照画像）
        private readonly UnderlayConfig      _underlay = new UnderlayConfig();
        private PlayerUnderlaySubPanel       _underlaySubPanel;
        private bool                         _underlayActive;  // 下絵パネル表示中＝左ドラッグでオフセット移動

        // 軸 / グリッド平面（4面共通）
        private PlayerGridAxisSubPanel       _gridAxisSubPanel;
        private PlayerWorkFolderSubPanel     _workFolderSubPanel;

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
    }
}
