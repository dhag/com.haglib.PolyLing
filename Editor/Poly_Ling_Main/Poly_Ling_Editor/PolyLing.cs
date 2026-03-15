// Assets/Editor/PolyLing.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
// Phase: CommandQueue対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Transforms;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Rendering;
using Poly_Ling.Symmetry;
using Poly_Ling.Commands;
using MeshEditor;
using UnityEngine.UIElements;




public partial class PolyLing : EditorWindow
{   // ================================================================
    // プロジェクトコンテキスト（Phase 0.5: ProjectContext導入）
    // ================================================================
    private ProjectContext _project = new ProjectContext();

    /// <summary>外部からToolContextを参照するための公開プロパティ（RemoteServer等）</summary>
    public ToolContext CurrentToolContext => _toolManager?.toolContext;

    // 後方互換プロパティ（既存コードを壊さない）
    private ModelContext _model => _project.CurrentModel;
    private List<MeshContext> _meshContextList => _model?.MeshContextList;
    
    // v2.0: カテゴリ別選択インデックス
    private int _selectedMeshIndex => _model?.FirstMeshIndex ?? -1;
    private int _selectedBoneIndex => _model?.FirstBoneIndex ?? -1;
    private int _selectedMorphIndex => _model?.FirstMorphIndex ?? -1;

    // アクティブカテゴリに応じた選択インデックス
    private int _selectedIndex
    {
        get
        {
            if (_model == null) return -1;
            return _model.ActiveCategory switch
            {
                ModelContext.SelectionCategory.Mesh => _selectedMeshIndex,
                ModelContext.SelectionCategory.Bone => _selectedBoneIndex,
                ModelContext.SelectionCategory.Morph => _selectedMorphIndex,
                _ => -1
            };
        }
        set
        {
            if (_model == null) return;
            if (value >= 0 && value < _model.Count)
            {
                // v2.0: 同一カテゴリのみクリア（他カテゴリの選択は維持）
                _model.SelectByTypeExclusive(value);
            }
            else if (value < 0)
            {
                // -1の場合: アクティブカテゴリの選択をクリア
                _model.ClearAllCategorySelection();
            }
        }
    }

    private Vector2 _vertexScroll;

    // ================================================================
    // デフォルトマテリアル（後方互換プロパティ → ModelContext に集約）
    // ================================================================
    private List<Material> _defaultMaterials
    {
        get => _model?.DefaultMaterials ?? new List<Material> { null };
        set { if (_model != null) _model.DefaultMaterials = value; }
    }
    private int _defaultCurrentMaterialIndex
    {
        get => _model?.DefaultCurrentMaterialIndex ?? 0;
        set { if (_model != null) _model.DefaultCurrentMaterialIndex = value; }
    }
    private bool _autoSetDefaultMaterials
    {
        get => _model?.AutoSetDefaultMaterials ?? true;
        set { if (_model != null) _model.AutoSetDefaultMaterials = value; }
    }
    /*
    // ================================================================
    // マテリアル管理（後方互換）
    // ================================================================
    // 旧: private Material _registeredMaterial;
    // 新: MeshContext.Materialsに移行。以下は後方互換用プロパティ

    /// <summary>
    /// 登録マテリアル（後方互換）- 選択中メッシュのカレントマテリアルを参照
    /// </summary>
    private Material RegisteredMaterial
    {
        get
        {
            return _model.FirstSelectedMeshContext?.GetCurrentMaterial();
        }
        set
        {
            var meshContext = _model.FirstSelectedMeshContext;
            if (meshContext != null && meshContext.CurrentMaterialIndex >= 0 && meshContext.CurrentMaterialIndex < meshContext.Materials.Count)
            {
                meshContext.Materials[meshContext.CurrentMaterialIndex] = value;
            }
        }

    }
    */
    // ================================================================
    // プレビュー（ViewportCore に統合）
    // ================================================================
    private Poly_Ling.MeshListV2.ViewportCore _viewportCore;

    // _unifiedAdapter は _viewportCore.Adapter へのプロパティ委譲
    private UnifiedSystemAdapter _unifiedAdapter => _viewportCore?.Adapter;

    // ================================================================
    // ViewportPanel用公開アクセサ
    // ================================================================

    /// <summary>ViewportPanelが描画に使用するUnifiedSystemAdapter</summary>
    public UnifiedSystemAdapter SharedUnifiedAdapter => _viewportCore?.Adapter;

    /// <summary>カメラ回転X（deg）</summary>
    public float CameraRotationX { get => _rotationX; set => _rotationX = value; }
    /// <summary>カメラ回転Y（deg）</summary>
    public float CameraRotationY { get => _rotationY; set => _rotationY = value; }
    /// <summary>カメラ回転Z（deg）</summary>
    public float CameraRotationZ { get => _rotationZ; set => _rotationZ = value; }
    /// <summary>カメラ距離</summary>
    public float CameraDistanceValue { get => _cameraDistance; set => _cameraDistance = value; }
    /// <summary>カメラ注目点</summary>
    public Vector3 CameraTargetValue { get => _cameraTarget; set => _cameraTarget = value; }
    /// <summary>カメラFOV</summary>
    public float CameraFOV => _viewportCore?.FOV ?? 30f;

    /// <summary>
    /// ViewportPanelから呼び出す入力処理。
    /// ViewportPanelのIMGUIコンテキスト内で呼ばれ、Event.currentはViewportPanelのイベント。
    /// </summary>
    public void ProcessViewportInput(Rect viewportRect)
    {
        if (_model == null) return;

        // 計算（ComputeWorldMatricesは冪等なので重複呼び出しOK）
        _model.ComputeWorldMatrices();

        var editorState = _undoController?.EditorState;
        bool useWorldTransform = editorState?.ShowWorldTransform ?? false;
        bool useLocalTransform = editorState?.ShowLocalTransform ?? false;
        if (useWorldTransform || useLocalTransform)
        {
            _unifiedAdapter?.UpdateTransform(useWorldTransform);
            if (useWorldTransform)
                _unifiedAdapter?.WritebackTransformedVertices();
        }

        // MeshContext取得
        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext == null && Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive)
            meshContext = _model.FirstSelectedDrawableMeshContext;
        if (meshContext == null) return;

        // カメラ
        float dist = _cameraDistance;
        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
        Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -dist);

        _lastPreviewRect = viewportRect;

        // 入力処理（HandleInputはこのpartial classのメソッド）
        HandleInput(viewportRect, meshContext, camPos, _cameraTarget, dist);
    }
    private Rect _lastPreviewRect;  // 最後に計算されたプレビュー領域（注目点移動で使用）

    // ================================================================
    // マウス操作設定
    // ================================================================
    private Poly_Ling.Input.MouseSettings _mouseSettings = new Poly_Ling.Input.MouseSettings();
    
    // カメラ状態: EditorStateContext を Single Source of Truth として参照
    // RotationZはEditorStateに含まれないためローカルで管理
    private float _rotationZ = 0f;  // Z軸回転（Ctrl+右ドラッグ）
    
    // カメラ状態プロパティ（EditorStateContext への委譲）
    private float _rotationX
    {
        get => _undoController?.EditorState?.RotationX ?? 20f;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.RotationX = value; }
    }
    private float _rotationY
    {
        get => _undoController?.EditorState?.RotationY ?? 0f;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.RotationY = value; }
    }
    private float _cameraDistance
    {
        get => _undoController?.EditorState?.CameraDistance ?? 2f;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.CameraDistance = value; }
    }

    public float _cameraDistanceMin = 0.1f;//カメラ距離の最大値（スクロールホイールで拡大する際の上限）
    public float _cameraDistanceMax = 10f;//カメラ距離の最大値（スクロールホイールで拡大する際の上限）



    private Vector3 _cameraTarget
    {
        get => _undoController?.EditorState?.CameraTarget ?? Vector3.zero;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.CameraTarget = value; }
    }

    // ============================================================================
    // === メッシュ追加モード（EditorStateContext への委譲） ===
    // ============================================================================
    private bool _addToCurrentMesh
    {
        get => _undoController?.EditorState?.AddToCurrentMesh ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.AddToCurrentMesh = value; }
    }

    private bool _autoMergeOnCreate
    {
        get => _undoController?.EditorState?.AutoMergeOnCreate ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.AutoMergeOnCreate = value; }
    }

    private float _autoMergeThreshold
    {
        get => _undoController?.EditorState?.AutoMergeThreshold ?? 0.001f;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.AutoMergeThreshold = value; }
    }

    // ============================================================================
    // === エクスポート設定（EditorStateContext への委譲） ===
    // ============================================================================
    private bool _exportSelectedMeshOnly
    {
        get => _undoController?.EditorState?.ExportSelectedMeshOnly ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ExportSelectedMeshOnly = value; }
    }

    private bool _bakeMirror
    {
        get => _undoController?.EditorState?.BakeMirror ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.BakeMirror = value; }
    }

    private bool _mirrorFlipU
    {
        get => _undoController?.EditorState?.MirrorFlipU ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.MirrorFlipU = value; }
    }

    private bool _bakeBlendShapes
    {
        get => _undoController?.EditorState?.BakeBlendShapes ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.BakeBlendShapes = value; }
    }

    private bool _useNameBasedSave
    {
        get => _undoController?.EditorState?.UseNameBasedSave ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.UseNameBasedSave = value; }
    }

    [SerializeField]
    private bool _saveOnMemoryMaterials = true;  // オンメモリマテリアルをアセットとして保存

    [SerializeField]
    private string _materialSaveFolder = "";  // マテリアル保存先フォルダ（空の場合はデフォルト）

    [SerializeField]
    private bool _overwriteExistingAssets = true;  // 既存アセットを上書きするか



    // ================================================================
    // 頂点編集
    // ================================================================
    private Vector3[] _vertexOffsets;       // 各Vertexのオフセット
    private Vector3[] _groupOffsets;        // グループオフセット（後方互換用、Vertexと1:1）

    // 頂点選択は _selectionState.Vertices を直接参照すること

    // 入力状態（ViewportInputStateに集約）
    private ViewportInputState _inp = new ViewportInputState();

    // 表示設定（EditorStateContext への委譲）
    private bool _showWireframe
    {
        get => _undoController?.EditorState?.ShowWireframe ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowWireframe = value; }
    }
    private bool _showVertices
    {
        get => _undoController?.EditorState?.ShowVertices ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowVertices = value; }
    }
    private bool _showMesh
    {
        get => _undoController?.EditorState?.ShowMesh ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowMesh = value; }
    }
    private bool _vertexEditMode
    {
        get => _undoController?.EditorState?.VertexEditMode ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.VertexEditMode = value; }
    }
    private bool _showSelectedMeshOnly
    {
        get => _undoController?.EditorState?.ShowSelectedMeshOnly ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowSelectedMeshOnly = value; }
    }
    private bool _showVertexIndices
    {
        get => _undoController?.EditorState?.ShowVertexIndices ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowVertexIndices = value; }
    }
    private bool _showUnselectedWireframe
    {
        get => _undoController?.EditorState?.ShowUnselectedWireframe ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowUnselectedWireframe = value; }
    }
    private bool _showUnselectedVertices
    {
        get => _undoController?.EditorState?.ShowUnselectedVertices ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowUnselectedVertices = value; }
    }
    private bool _showBones
    {
        get => _undoController?.EditorState?.ShowBones ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowBones = value; }
    }
    private bool _showUnselectedBones
    {
        get => _undoController?.EditorState?.ShowUnselectedBones ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowUnselectedBones = value; }
    }
    private bool _boneDisplayAlongY
    {
        get => _undoController?.EditorState?.BoneDisplayAlongY ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.BoneDisplayAlongY = value; }
    }
    private bool _showFocusPoint
    {
        get => _undoController?.EditorState?.ShowFocusPoint ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowFocusPoint = value; }
    }
    
    // エクスポート設定（EditorStateContext への委譲）
    private bool _exportAsSkinned
    {
        get => _undoController?.EditorState?.ExportAsSkinned ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ExportAsSkinned = value; }
    }
    private bool _createArmatureMeshesFolder = true; // Armature/Meshesフォルダを作成（EditorStateに含めない）
    private bool _addAnimatorComponent = true; // エクスポート時にAnimatorコンポーネントを追加（デフォルトON）
    private bool _createAvatarOnExport = false; // エクスポート時にHumanoid Avatarも生成（デフォルトOFF）
    
    /// <summary>
    /// ツールの状態
    /// </summary>
    // UIフォールドアウト状態
    private bool _foldDisplay = true;
    private bool _foldPrimitive = true;

    // ペイン幅
    private float _leftPaneWidth = 320f;
    private float _rightPaneWidth = 220f;

    // スプリッター（TwoPaneSplitView参照）
    private TwoPaneSplitView _leftSplitView;
    private TwoPaneSplitView _rightSplitView;
    private const float MinPaneWidth = 150f;
    private const float MaxLeftPaneWidth = 500f;
    private const float MaxRightPaneWidth = 400f;

    private bool _foldSelection = true;
    private bool _foldTools = true;
    //private bool _foldWorkPlane = false;  // WorkPlaneセクション
    private Vector2 _leftPaneScroll;  // 左ペインのスクロール位置

    // WorkPlane表示設定（EditorStateContext への委譲）
    private bool _showWorkPlaneGizmo
    {
        get => _undoController?.EditorState?.ShowWorkPlaneGizmo ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowWorkPlaneGizmo = value; }
    }

    // ================================================================
    // Undoシステム統合
    // ================================================================
    private MeshUndoController _undoController;
    
    // ================================================================
    // コマンドキュー（全操作の順序保証）
    // ================================================================
    private CommandQueue _commandQueue;
    
    // ================================================================
    // Selection System
    // ================================================================
    private SelectionState _selectionState = new SelectionState();
    private TopologyCache _meshTopology;
    private SelectionOperations _selectionOps;
    private UnifiedAdapterVisibilityProvider _visibilityProvider;


    // スライダー編集用
    private bool _isSliderDragging = false;
    private Vector3[] _sliderDragStartOffsets;

    // カメラドラッグ用
    private bool _isCameraDragging = false;
    //private bool _cameraRestoredByRecord = false; // MeshSelectionChangeRecord等からカメラ復元済みフラグ
    private float _cameraStartRotX, _cameraStartRotY, _cameraStartRotZ;
    private float _cameraStartDistance;
    private Vector3 _cameraStartTarget;
    private WorkPlaneSnapshot? _cameraStartWorkPlaneSnapshot;

    /// <summary>
    /// MeshList Undo/Redo実行中フラグ。
    /// OnMeshListChangedで立て、OnUndoRedoPerformedでチェック＆クリア。
    /// MeshList操作後のOnUndoRedoPerformedでMeshObject上書きを防止する。
    /// </summary>
    // (A1統合: _meshListUndoRedoInProgressフラグ廃止 - Dual ModelContext統合により不要)


    // ================================================================
    // 【フェーズ2追加】選択Undo管理
    // （_inp.SelectionSnapshotOnMouseDown, _inp.WorkPlaneSnapshotOnMouseDown,
    //   _inp.TopologyChangedDuringMouseOp は _inp に移動済み）
    // ================================================================




    private SelectionSnapshot _lastSelectionSnapshot;  // Undo用スナップショット


    //  描画キャッシュ
    private MeshDrawCache _drawCache;
    // ================================================================
    // ウインドウ初期化
    // ================================================================
    [MenuItem("Tools/PolyLingEditor(メイン)")]
    private static void Open()
    {
        var window = GetWindow<PolyLing>("PolyLing");
        window.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        // Phase 0.5: ProjectContext は常にデフォルト Model を持つ
        // （ProjectContext コンストラクタで自動作成）
        if (_project == null)
            _project = new ProjectContext();

        // シリアライズ復元時に Models が空の場合、デフォルトモデルを作成
        if (_project.ModelCount == 0)
            _project.AddModel(new ModelContext("Model"));

        InitPreview();
        wantsMouseMove = true;
        // ★Phase2追加: 対称キャッシュ初期化
        InitializeSymmetryCache();

        // ローカライゼーション設定を読み込み
        L.LoadSettings();

        // コマンドキュー初期化（全操作の順序保証）
        _commandQueue = new CommandQueue();
        // _commandQueue.EnableDebugLog = true; // デバッグ時に有効化

        // Undoコントローラー初期化
        _undoController = new MeshUndoController("PolyLing");
        _undoController.SetCommandQueue(_commandQueue); // キューを設定
        _undoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        _undoController.OnProjectUndoRedoPerformed += OnProjectUndoRedoPerformed;  // Project-level Undo/Redo


        // ProjectContext のコールバック設定
        if (_project != null)
        {
            _project.OnCurrentModelChanged += OnCurrentModelChanged;
            _project.OnModelsChanged += OnModelsChanged;
        }

        // ★追加: Undoキュー処理を登録（ConcurrentQueue対応）
        EditorApplication.update += ProcessUndoQueues;

        // A1統合: _modelをUndoコントローラーに直接共有
        _undoController.SetModelContext(_model);
        _model.OnListChanged += OnMeshListChanged;

        // カメラ復元コールバックを設定（_model に直接設定）
        if (_model != null)
        {
            _model.OnCameraRestoreRequested = OnCameraRestoreRequested;
            _model.OnFocusMeshListRequested = () => _undoController.FocusMeshList();
            _model.OnReorderCompleted = () => {
                // ========================================================
                // メッシュ順序変更後の完全再構築処理
                // ========================================================

                // グローバルバッファを完全再構築
                _unifiedAdapter?.SetModelContext(_model);

                // アクティブメッシュをワンショットパイプライン経由で同期
                _unifiedAdapter?.SetActiveMesh(0, _selectedIndex);
                _unifiedAdapter?.RequestNormal();

                // トポロジー更新
                UpdateTopology();
            };
            _model.OnVertexEditStackClearRequested = () => _undoController?.VertexEditStack?.Clear();
        }

        // ModelContextにWorkPlaneを設定
        if (_model != null)
            _model.WorkPlane = _undoController.WorkPlane;

        // ★Phase 1: MeshUndoContext.MaterialOwner を設定（Materials Undo用）
        if (_undoController.MeshUndoContext != null && _model != null)
        {
            _undoController.MeshUndoContext.ParentModelContext = _model;
        }

        // ★Phase 1: 既存 MeshContext にも MaterialOwner を設定
        if (_meshContextList != null && _model != null)
        {
            foreach (var meshContext in _meshContextList)
            {
                if (meshContext != null)
                {
                    meshContext.ParentModelContext = _model;
                }
            }
        }

        // Show Verticesと編集モードを同期
        //_vertexEditMode = _showVertices;

        // Single Source of Truth: EditorStateContext が唯一のデータソースのため、
        // ローカル変数への初期化コピーは不要（プロパティ経由で直接参照）

        // Selection System 初期化（ツールより先に初期化）
        InitializeSelectionSystem();

        // ツール初期化
        InitializeTools();

        // 初期ツール名をEditorStateに設定
        if (_undoController != null && _currentTool != null)
        {
            _undoController.EditorState.CurrentToolName = _currentTool.Name;
        }

        // WorkPlaneContext UIイベントハンドラ設定
        SetupWorkPlaneEventHandlers();

        // BoneTransform UIイベントハンドラ設定
        SetupBoneTransformEventHandlers();

        // ★描画キャッシュ初期化
        InitializeDrawCache();

        _drawCache = new MeshDrawCache();

        // ViewportCore 初期化（PreviewRenderUtility + UnifiedSystemAdapter を統合管理）
        _viewportCore = new Poly_Ling.MeshListV2.ViewportCore();
        if (!_viewportCore.Init(_model))
        {
            Debug.LogError("[PolyLing] Failed to initialize ViewportCore");
            _viewportCore.Dispose();
            _viewportCore = null;
            EditorUtility.DisplayDialog(
                "Initialization Error",
                "Failed to initialize unified rendering system.\nThe editor window will be closed.",
                "OK");
            Close();
            return;
        }
        _viewportCore.Adapter.SetSelectionState(_selectionState);
        if (_selectedIndex >= 0)
            _viewportCore.Adapter.SetActiveMesh(0, _selectedIndex);

        // パネルコンテキスト初期化（LiveProjectView生成）
        InitPanelContext();

        // ViewportCore コールバック設定
        SetupViewportCoreCallbacks();

        // VisibilityProviderを設定（背面カリング対応）
        if (_unifiedAdapter != null && _selectionOps != null)
        {
            _visibilityProvider = new UnifiedAdapterVisibilityProvider(_unifiedAdapter, _selectedIndex);
            
            // 線分と面の頂点取得用デリゲートを設定
            _visibilityProvider.SetGeometryAccessors(
                // 線分インデックス → (v1, v2)
                lineIndex => {
                    var meshObject = _model?.FirstSelectedMeshContext?.MeshObject;
                    if (meshObject != null && lineIndex >= 0 && lineIndex < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIndex];
                        if (face.VertexCount == 2)
                            return (face.VertexIndices[0], face.VertexIndices[1]);
                    }
                    return (-1, -1);
                },
                // 面インデックス → 頂点配列
                faceIndex => {
                    var meshObject = _model?.FirstSelectedMeshContext?.MeshObject;
                    if (meshObject != null && faceIndex >= 0 && faceIndex < meshObject.FaceCount)
                    {
                        return meshObject.Faces[faceIndex].VertexIndices.ToArray();
                    }
                    return null;
                }
            );
            
            _selectionOps.SetVisibilityProvider(_visibilityProvider);
        }

    }

    /// <summary>
    /// Selection System 初期化
    /// </summary>
    private void InitializeSelectionSystem()
    {
        // MeshContext.Selection を正規のデータソースとして使用
        var meshContext = _model?.FirstSelectedMeshContext;
        _selectionState = meshContext?.Selection ?? new SelectionState();
        _meshTopology = new TopologyCache();
        _selectionOps = new SelectionOperations(_selectionState, _meshTopology);
        
        // GPU/CPUヒットテストの閾値を統一（HOVER_LINE_DISTANCE = 18f と同じ）
        _selectionOps.EdgeHitDistance = 18f;

        _selectionState.OnSelectionChanged += OnSelectionChanged;

        _lastSelectionSnapshot = _selectionState.CreateSnapshot();
        UpdateTopology();
    }


    /// <summary>
    /// 選択変更時のコールバック（レンダリング更新）
    /// </summary>
    private void OnSelectionChanged()
    {
        _unifiedAdapter?.RequestNormal();
        Repaint();
    }

    /// <summary>
    /// MeshObject変更時にトポロジを更新
    /// </summary>
    private void UpdateTopology()
    {
        if (_meshTopology == null)
            return;

        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext != null)
        {
            _meshTopology.SetMeshObject(meshContext.MeshObject);
        }
        else
        {
            _meshTopology.SetMeshObject(null);
        }

        // 統合システムにもトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();
    }






    private void OnDisable()
    {
        // PreviewRenderUtility を最初に解放する。
        // 後続処理で例外が発生しても Cleanup() が確実に呼ばれるようにするため先頭に置く。
        _viewportCore?.Dispose();
        _viewportCore = null;

        CleanupMeshes();

        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }

        // ★ミラーリソースのクリーンアップ
        CleanupMirrorResources();

        // ★GPU描画クリーンアップ
        CleanupDrawCache();

        // WorkPlaneContext UIイベントハンドラ解除
        CleanupWorkPlaneEventHandlers();

        // BoneTransform UIイベントハンドラ解除
        CleanupBoneTransformEventHandlers();

        // Selection System クリーンアップ
        if (_selectionState != null)
        {
            _selectionState.OnSelectionChanged -= OnSelectionChanged;
        }

        // ★追加: Undoキュー処理を解除
        EditorApplication.update -= ProcessUndoQueues;

        // コマンドキュークリア
        _commandQueue?.Clear();
        _commandQueue = null;

        // Undoコントローラー破棄
        if (_undoController != null)
        {
            _undoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            _undoController.OnProjectUndoRedoPerformed -= OnProjectUndoRedoPerformed;  // Project-level Undo/Redo
            _undoController.Dispose();
            _undoController = null;
        }
        // ★追加
        _drawCache?.Clear();
        // ProjectContext のコールバック解除
        if (_project != null)
        {
            _project.OnCurrentModelChanged -= OnCurrentModelChanged;
            _project.OnModelsChanged -= OnModelsChanged;
        }
        // A1統合: OnListChangedハンドラ解除
        if (_model != null)
        {
            _model.OnListChanged -= OnMeshListChanged;
        }
    }

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// Phase 4: スタック種別で分岐、マルチメッシュ対等処理
    /// </summary>
    private void OnUndoRedoPerformed()
    {
        var stackType = _undoController.LastUndoRedoStackType;

        // ────────────────────────────────────────
        // VertexEdit Undo/Redo
        // ────────────────────────────────────────
        if (stackType == MeshUndoController.UndoStackType.VertexEdit)
        {
            var ctx = _undoController.MeshUndoContext;

            if (ctx.DirtyMeshIndices.Count > 0)
            {
                // マルチメッシュ操作: PendingMeshMoveEntriesの位置データを適用後、SyncMeshFromData
                int currentIndex = _model.FirstSelectedIndex;

                // PendingMeshMoveEntries がある場合、頂点位置を適用
                if (ctx.PendingMeshMoveEntries != null)
                {
                    foreach (var entry in ctx.PendingMeshMoveEntries)
                    {
                        var mc = _model.GetMeshContext(entry.MeshContextIndex);
                        if (mc?.MeshObject == null) continue;

                        var meshObject = mc.MeshObject;
                        var positions = entry.NewPositions;

                        for (int i = 0; i < entry.Indices.Length; i++)
                        {
                            int idx = entry.Indices[i];
                            if (idx >= 0 && idx < meshObject.VertexCount)
                            {
                                meshObject.Vertices[idx].Position = positions[i];
                            }
                        }
                        meshObject.InvalidatePositionCache();

                        if (mc.OriginalPositions != null)
                        {
                            for (int i = 0; i < entry.Indices.Length; i++)
                            {
                                int idx = entry.Indices[i];
                                if (idx >= 0 && idx < mc.OriginalPositions.Length)
                                {
                                    mc.OriginalPositions[idx] = meshObject.Vertices[idx].Position;
                                }
                            }
                        }
                    }
                    ctx.PendingMeshMoveEntries = null;
                }

                foreach (var idx in ctx.DirtyMeshIndices)
                {
                    var mc = _model.GetMeshContext(idx);
                    if (mc == null) continue;

                    SyncMeshFromData(mc);

                    if (idx == currentIndex)
                    {
                        ctx.MeshObject = mc.MeshObject;
                        ctx.TargetMesh = mc.UnityMesh;
                        ctx.OriginalPositions = mc.OriginalPositions;

                        if (mc.MeshObject != null && mc.MeshObject.VertexCount > 0)
                        {
                            UpdateOffsetsFromData(mc);
                        }
                    }
                }
                ctx.DirtyMeshIndices.Clear();
            }
            else
            {
                // 単一メッシュ操作（VertexMoveRecord等）: 従来のctx.MeshObjectフロー
                var meshContext = _model.FirstSelectedMeshContext;
                if (meshContext != null && ctx.MeshObject != null)
                {
                    var clonedMeshObject = ctx.MeshObject.Clone();
                    meshContext.MeshObject = clonedMeshObject;
                    ctx.MeshObject = clonedMeshObject;
                    SyncMeshFromData(meshContext);

                    if (ctx.MeshObject.VertexCount > 0)
                    {
                        UpdateOffsetsFromData(meshContext);
                    }
                }
            }
        }
        // MirrorPair再同期: VertexEdit Undo/Redo後にReal→Mirrorを再同期
        if (stackType == MeshUndoController.UndoStackType.VertexEdit && _model?.MirrorPairs != null)
        {
            foreach (var pair in _model.MirrorPairs)
            {
                pair.SyncPositions();
                SyncMeshPositionsOnly(pair.Mirror);
            }
        }
        // MeshList Undo/Redo: OnMeshListChangedが処理済み → メッシュ操作不要
        // EditorState / WorkPlane Undo/Redo: メッシュ変更なし → メッシュ操作不要

        // ────────────────────────────────────────
        // 共通処理（全スタック種別で実行）
        // ────────────────────────────────────────

        // デフォルトマテリアル復元
        var ctxForDefault = _undoController.MeshUndoContext;
        if (ctxForDefault.DefaultMaterials != null && ctxForDefault.DefaultMaterials.Count > 0)
        {
            _defaultMaterials = new List<Material>(ctxForDefault.DefaultMaterials);
        }
        _defaultCurrentMaterialIndex = ctxForDefault.DefaultCurrentMaterialIndex;
        _autoSetDefaultMaterials = ctxForDefault.AutoSetDefaultMaterials;

        // カメラ復元フラグのリセット
        //_cameraRestoredByRecord = false;

        // ツール復元
        var editorState = _undoController.EditorState;
        RestoreToolFromName(editorState.CurrentToolName);
        ApplyToTools(editorState);

        _currentTool?.Reset();
        ResetEditState();

        // SelectionState を復元（VertexEdit Undo/Redo時のみSnapshotが存在する）
        var ctx2 = _undoController.MeshUndoContext;
        if (ctx2.CurrentSelectionSnapshot != null && _selectionState != null)
        {
            _selectionState.RestoreFromSnapshot(ctx2.CurrentSelectionSnapshot);
            ctx2.CurrentSelectionSnapshot = null;
        }

        _unifiedAdapter?.RequestNormal();
        Repaint();

        // ミラーキャッシュを無効化
        InvalidateAllSymmetryCaches();
    }

    // ================================================================
    // Undoキュー処理（ConcurrentQueue対応）
    // ================================================================

    /// <summary>
    /// Undoキューを処理（EditorApplication.updateから呼び出し）
    /// 別スレッド/プロセスからRecord()されたデータをスタックに積む
    /// コマンドキューも処理する
    /// </summary>
    private void ProcessUndoQueues()
    {
        // コマンドキューの処理（Undo/Redo含む全操作）
        if (_commandQueue != null && _commandQueue.Count > 0)
        {
            _commandQueue.ProcessAll();
            _unifiedAdapter?.RequestNormal();
            Repaint();
        }

        // UndoManagerのキュー処理（従来の処理）
        int processed = UndoManager.Instance.ProcessAllQueues();

        if (processed > 0)
        {
            // キューが処理されたらUIを更新
            _unifiedAdapter?.RequestNormal();
            Repaint();
        }
    }

    /// <summary>
    /// MeshListのUndo/Redo後のコールバック
    /// </summary>
    private void OnMeshListChanged()
    {
        // WithUndoメソッドが_meshContextListを直接操作するため、
        // TypedIndicesキャッシュを明示的に無効化する
        _model?.InvalidateTypedIndices();
        // Debug.Log($"[OnMeshListChanged] Before: _cameraTarget={_cameraTarget}, _cameraDistance={_cameraDistance}");
        // Debug.Log($"[OnMeshListChanged] Before: _selectedIndex={_selectedIndex}, MeshListContext.FirstSelectedIndex={_undoController?.MeshListContext?.FirstSelectedIndex}");

        // v2.0: _selectedIndex getterがModelContextを直接参照するため、同期不要
        // MeshListContext（= ModelContext）の選択状態はUndo/Redoで自動復元される

        // Debug.Log($"[OnMeshListChanged] After sync: _selectedIndex={_selectedIndex}, CurrentMesh={_model.FirstSelectedMeshContext?.Name}");

        // 選択中のメッシュコンテキストをMeshContextに設定
        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext != null)
        {
            // Debug.Log($"[OnMeshListChanged] Before InitVertexOffsets: _cameraTarget={_cameraTarget}");
            InitVertexOffsets(updateCamera: false);
            // Debug.Log($"[OnMeshListChanged] After InitVertexOffsets: _cameraTarget={_cameraTarget}");
        }
        else
        {
            SetSelectedIndex(_meshContextList.Count > 0 ? 0 : -1);
            var fallbackMeshContext = _model.FirstSelectedMeshContext;
            if (fallbackMeshContext != null)
            {
                InitVertexOffsets(updateCamera: false);
            }
        }

        // _selectionState参照を新メッシュのSelectionに差し替え
        // （イベント解除→新参照設定→イベント登録→レンダラ通知→UndoContext同期）
        SaveSelectionToCurrentMesh();
        LoadSelectionFromCurrentMesh();

        // GPUバッファ再構築（メッシュリスト構成変更のため）
        _unifiedAdapter?.NotifyTopologyChanged();

        // パネル通知（リスト構造変更）
        NotifyPanels(ChangeKind.ListStructure);

        // Debug.Log($"[OnMeshListChanged] After: _cameraTarget={_cameraTarget}, _cameraDistance={_cameraDistance}");
        // Debug.Log($"[OnMeshListChanged] Final: _selectedIndex={_selectedIndex}, CurrentMesh={_model.FirstSelectedMeshContext?.Name}");
        Repaint();
    }
    /*
    /// <summary>
    /// カメラ状態を復元（Undo/Redo時のコールバック）
    /// </summary>
    private void OnCameraRestoreRequested(CameraSnapshot camera)
    {
        // Debug.Log($"[OnCameraRestoreRequested] BEFORE: rotX={_rotationX}, rotY={_rotationY}, dist={_cameraDistance}, target={_cameraTarget}");
        // Debug.Log($"[OnCameraRestoreRequested] RESTORING TO: rotX={camera.RotationX}, rotY={camera.RotationY}, dist={camera.CameraDistance}, target={camera.CameraTarget}");
        _rotationX = camera.RotationX;
        _rotationY = camera.RotationY;
        _cameraDistance = camera.CameraDistance;
        _cameraTarget = camera.CameraTarget;
        _cameraRestoredByRecord = true; // OnUndoRedoPerformedでの上書きを防ぐ
        // Debug.Log($"[OnCameraRestoreRequested] AFTER: rotX={_rotationX}, rotY={_rotationY}, dist={_cameraDistance}, target={_cameraTarget}");
        Repaint();
    }*/



    private void SyncMeshFromData(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null || meshContext.UnityMesh == null)
            return;

        var newMesh = meshContext.MeshObject.ToUnityMeshShared();
        meshContext.UnityMesh.Clear();
        meshContext.UnityMesh.vertices = newMesh.vertices;
        meshContext.UnityMesh.uv = newMesh.uv;
        meshContext.UnityMesh.normals = newMesh.normals;

        // サブメッシュ対応
        meshContext.UnityMesh.subMeshCount = newMesh.subMeshCount;
        for (int i = 0; i < newMesh.subMeshCount; i++)
        {
            meshContext.UnityMesh.SetTriangles(newMesh.GetTriangles(i), i);
        }

        meshContext.UnityMesh.RecalculateBounds();

        DestroyImmediate(newMesh);
        // トポロジキャッシュを無効化
        _meshTopology?.Invalidate();
        // ★追加: エッジキャッシュを無効化
        _drawCache?.InvalidateEdgeCache();
        // ★Phase2追加: 対称表示キャッシュを無効化
        InvalidateSymmetryCache();
        // ★GPUバッファの位置情報を更新
        _unifiedAdapter?.NotifyTransformChanged();
        // ★GPUバッファを再構築（トポロジ変更対応）
        // 頂点数/面数が変わる可能性があるため、常にトポロジ変更として扱う
        _unifiedAdapter?.NotifyTopologyChanged();
        // ★LiveSync: ヒエラルキーへの自動同期（対象メッシュのみ）
        LiveSyncAutoUpdate(meshContext);
    }

    /// <summary>
    /// 軽量版：頂点位置のみ更新（トポロジ不変の場合用）
    /// </summary>
    private void SyncMeshPositionsOnly(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null || meshContext.UnityMesh == null)
            return;

        var meshObject = meshContext.MeshObject;
        var unityMesh = meshContext.UnityMesh;

        // 頂点数が一致しない場合はフルSync
        int vertexCount = meshObject.VertexCount;
        if (vertexCount != unityMesh.vertexCount)
        {
            SyncMeshFromData(meshContext);
            _unifiedAdapter?.NotifyTransformChanged();
            LiveSyncAutoUpdate(meshContext);
            return;
        }

        // 頂点位置配列を構築
        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = meshObject.Vertices[i].Position;
        }

        // 位置のみ更新
        unityMesh.vertices = vertices;
        unityMesh.RecalculateBounds();

        // GPUバッファの位置情報を更新
        _unifiedAdapter?.NotifyTransformChanged();
        // LiveSync: ヒエラルキーへの自動同期（対象メッシュのみ）
        LiveSyncAutoUpdate(meshContext);
    }

    /// <summary>
    /// v2.1: 選択中の全メッシュの頂点位置を同期
    /// </summary>
    private void SyncAllSelectedMeshPositions()
    {
        if (_model == null || _model.SelectedMeshIndices.Count == 0)
        {
            // フォールバック: プライマリメッシュのみ
            SyncMeshPositionsOnly(_model?.FirstSelectedMeshContext);
            return;
        }

        foreach (int meshIdx in _model.SelectedMeshIndices)
        {
            var meshContext = _model.GetMeshContext(meshIdx);
            if (meshContext?.MeshObject == null || meshContext.UnityMesh == null)
                continue;

            var meshObject = meshContext.MeshObject;
            var unityMesh = meshContext.UnityMesh;

            // 頂点数が一致しない場合はフルSync（triangles再構築含む）
            int vertexCount = meshObject.VertexCount;
            if (vertexCount != unityMesh.vertexCount)
            {
                SyncMeshFromData(meshContext);
                continue;
            }

            // 頂点位置配列を構築
            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = meshObject.Vertices[i].Position;
            }

            // 位置のみ更新
            unityMesh.vertices = vertices;
            unityMesh.RecalculateBounds();
        }

        // GPUバッファの位置情報を更新
        _unifiedAdapter?.NotifyTransformChanged();
    }




    /// <summary>
    /// meshContext.MeshObjectからオフセットを更新
    /// </summary>
    private void UpdateOffsetsFromData(MeshContext meshContext)
    {
        if (meshContext.MeshObject == null || _vertexOffsets == null)
            return;

        int count = Mathf.Min(meshContext.MeshObject.VertexCount, _vertexOffsets.Length);
        for (int i = 0; i < count; i++)
        {
            if (i < meshContext.OriginalPositions.Length)
            {
                _vertexOffsets[i] = meshContext.MeshObject.Vertices[i].Position - meshContext.OriginalPositions[i];
            }
        }

        // グループオフセットも更新（Vertexと1:1）
        if (_groupOffsets != null)
        {
            for (int i = 0; i < count && i < _groupOffsets.Length; i++)
            {
                _groupOffsets[i] = _vertexOffsets[i];
            }
        }
    }

    private void InitPreview()
    {
        // ViewportCore が Init() 内で PreviewRenderUtility を管理する
        // OnEnable での ViewportCore.Init() 呼び出しより前に実行されるため、ここでは何もしない
    }

    private void CleanupPreview()
    {
        // ViewportCore.Dispose() が PreviewRenderUtility を破棄する
        // OnDisable での ViewportCore.Dispose() 呼び出し時に処理される
    }

    private void CleanupMeshes()
    {
        if (_meshContextList == null) return;

        foreach (var meshContext in _meshContextList)
        {
            if (meshContext?.UnityMesh != null)
                DestroyImmediate(meshContext.UnityMesh);
        }
        _meshContextList.Clear();
    }

    // ================================================================
    // メインGUI
    // ================================================================
    private void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Row;

        // 左ペイン IMGUIContainer
        var leftContainer = new IMGUIContainer(() =>
        {
            if (_undoController != null)
            {
                if (_undoController.HandleKeyboardShortcuts(Event.current))
                    Repaint();
            }

            Event e = Event.current;
            if (e.type == EventType.MouseUp)
            {
                if (_isSliderDragging) EndSliderDrag();
                if (_isCameraDragging) EndCameraDrag();
            }

            DrawMeshList();
        });
        leftContainer.style.width = _leftPaneWidth;
        leftContainer.style.minWidth = MinPaneWidth;
        leftContainer.style.maxWidth = MaxLeftPaneWidth;

        // 中央ペイン IMGUIContainer
        var centerContainer = new IMGUIContainer(() =>
        {
            Event e = Event.current;
            if (e.type == EventType.MouseUp)
            {
                if (_isCameraDragging) EndCameraDrag();
            }
            HandleScrollWheel();
            DrawPreview();
        });
        centerContainer.style.flexGrow = 1;

        // 右ペイン IMGUIContainer
        var rightContainer = new IMGUIContainer(() =>
        {
            DrawRightPane();
        });
        rightContainer.style.width = _rightPaneWidth;
        rightContainer.style.minWidth = MinPaneWidth;
        rightContainer.style.maxWidth = MaxRightPaneWidth;

        // 右側 TwoPaneSplitView（中央|右）
        _rightSplitView = new TwoPaneSplitView(1, _rightPaneWidth, TwoPaneSplitViewOrientation.Horizontal);
        _rightSplitView.style.flexGrow = 1;
        _rightSplitView.Add(centerContainer);
        _rightSplitView.Add(rightContainer);

        // 左側 TwoPaneSplitView（左|残り）
        _leftSplitView = new TwoPaneSplitView(0, _leftPaneWidth, TwoPaneSplitViewOrientation.Horizontal);
        _leftSplitView.style.flexGrow = 1;
        _leftSplitView.Add(leftContainer);
        _leftSplitView.Add(_rightSplitView);

        root.Add(_leftSplitView);
    }

    private void HandleScrollWheel()
    {
        // ViewportPanelが開いている場合、カメラ操作はパネル側で処理
        if (Poly_Ling.MeshListV2.ViewportPanel.IsOpen) return;

        Event e = Event.current;

        // 中ボタンドラッグで視点XY移動（パン）
        // グループB: ScreenDeltaToWorldDeltaを使用し、マウス移動と画面上の物体移動を一致させる
        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            if (!_isCameraDragging)
            {
                BeginCameraDrag();
            }

            // プレビュー領域が未初期化の場合はスキップ
            if (_lastPreviewRect.height <= 0)
            {
                e.Use();
                return;
            }

            // カメラ位置を計算
            Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
            Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -_cameraDistance);

            // ScreenDeltaToWorldDeltaで物理的に正確な移動量を計算
            // 注目点を動かすと画面上の物体は逆方向に動くので、結果を反転
            Vector3 worldDelta = ScreenDeltaToWorldDelta(e.delta, camPos, _cameraTarget, _cameraDistance, _lastPreviewRect);
            
            // 修飾キー倍率を適用
            float multiplier = _mouseSettings.GetModifierMultiplier(e);

            // デバッグ出力
            float fovRad = CameraFOV * Mathf.Deg2Rad;
            float worldHeightAtDist = 2f * _cameraDistance * Mathf.Tan(fovRad / 2f);
            float pixelToWorld = worldHeightAtDist / _lastPreviewRect.height;
            //Debug.Log($"[CameraPan] delta={e.delta}, worldDelta={worldDelta}, multiplier={multiplier}, " +
            //          $"FOV={CameraFOV}, camDist={_cameraDistance}, " +
             //         $"rectHeight={_lastPreviewRect.height}, pixelToWorld={pixelToWorld}");

            _cameraTarget -= worldDelta * multiplier;

            e.Use();
            Repaint();
            return;
        }

        // ホイールズームはHandleInput（プレビュー領域内）で処理するため、ここでは何もしない
    }

    // ================================================================
    // カメラドラッグのUndo
    // ================================================================

    private void BeginCameraDrag()
    {
        if (_isCameraDragging) return;

        _isCameraDragging = true;
        
        // 更新モード切替: カメラドラッグ中は重い処理を全スキップ
        // （ヒットテスト、頂点フラグ読み戻し、可視性計算、メッシュ再構築等）
        _unifiedAdapter?.EnterCameraDragging();
        
        _cameraStartRotX = _rotationX;
        _cameraStartRotY = _rotationY;
        _cameraStartRotZ = _rotationZ;
        _cameraStartDistance = _cameraDistance;
        _cameraStartTarget = _cameraTarget;

        // WorkPlane連動用：開始時のスナップショットを保存
        var workPlane = _undoController?.WorkPlane;
        if (workPlane != null && workPlane.Mode == WorkPlaneMode.CameraParallel &&
            !workPlane.IsLocked && !workPlane.LockOrientation)
        {
            _cameraStartWorkPlaneSnapshot = workPlane.CreateSnapshot();
        }
        else
        {
            _cameraStartWorkPlaneSnapshot = null;
        }
    }

    private void EndCameraDrag()
    {
        if (!_isCameraDragging) return;
        _isCameraDragging = false;
        
        // 更新モード復帰: 全処理を再開
        _unifiedAdapter?.ExitCameraDragging();

        bool hasChanged =
            !Mathf.Approximately(_cameraStartRotX, _rotationX) ||
            !Mathf.Approximately(_cameraStartRotY, _rotationY) ||
            !Mathf.Approximately(_cameraStartRotZ, _rotationZ) ||
            !Mathf.Approximately(_cameraStartDistance, _cameraDistance) ||
            Vector3.Distance(_cameraStartTarget, _cameraTarget) > 0.0001f;

        if (hasChanged && _undoController != null)
        {
            // WorkPlane連動チェック
            var workPlane = _undoController.WorkPlane;
            WorkPlaneSnapshot? oldWorkPlane = _cameraStartWorkPlaneSnapshot;
            WorkPlaneSnapshot? newWorkPlane = null;

            if (oldWorkPlane.HasValue && workPlane != null)
            {
                // 新しいカメラ姿勢でWorkPlane軸を更新
                Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
                Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -_cameraDistance);

                workPlane.UpdateFromCamera(camPos, _cameraTarget);
                newWorkPlane = workPlane.CreateSnapshot();

                // 変更がない場合はnullに戻す
                if (!oldWorkPlane.Value.IsDifferentFrom(newWorkPlane.Value))
                {
                    oldWorkPlane = null;
                    newWorkPlane = null;
                }
            }

            // Undo記録（キュー経由）
            _commandQueue?.Enqueue(new RecordCameraChangeCommand(
                _undoController,
                _cameraStartRotX, _cameraStartRotY, _cameraStartDistance, _cameraStartTarget,
                _rotationX, _rotationY, _cameraDistance, _cameraTarget,
                oldWorkPlane, newWorkPlane));

            // Single Source of Truth: プロパティ経由でEditorStateを直接参照しているため、
            // SetEditorState呼び出しは不要
        }

        _cameraStartWorkPlaneSnapshot = null;
    }

    // ================================================================
    // Foldout Undo対応ヘルパー
    // ================================================================

    /// <summary>
    /// Undo対応Foldoutを描画
    /// </summary>
    /// <param name="key">Foldoutのキー（一意の識別子）</param>
    /// <param name="label">表示ラベル</param>
    /// <param name="defaultValue">デフォルト値</param>
    /// <returns>現在の開閉状態</returns>
    private bool DrawFoldoutWithUndo(string key, string label, bool defaultValue = true)
    {
        if (_undoController == null)
        {
            // Undo非対応の場合は通常のFoldout
            return EditorGUILayout.Foldout(defaultValue, label, true);
        }

        var editorState = _undoController.EditorState;
        bool currentValue = editorState.GetFoldout(key, defaultValue);

        EditorGUI.BeginChangeCheck();
        bool newValue = EditorGUILayout.Foldout(currentValue, label, true);

        if (EditorGUI.EndChangeCheck() && newValue != currentValue)
        {
            // Undo記録するか判定
            if (editorState.RecordFoldoutChanges)
            {
                _undoController.BeginEditorStateDrag();
                editorState.SetFoldout(key, newValue);
                _undoController.EndEditorStateDrag($"Toggle {label}");
            }
            else
            {
                // Undo記録なしで状態だけ更新
                editorState.SetFoldout(key, newValue);
            }
        }

        return newValue;
    }

    /// <summary>
    /// Foldout Undo記録を有効/無効にする
    /// </summary>
    private void SetRecordFoldoutChanges(bool enabled)
    {
        if (_undoController != null)
        {
            _undoController.EditorState.RecordFoldoutChanges = enabled;
        }
    }

    // ================================================================
    // スプリッター処理
    // ================================================================

    // スプリッター用のコントロールID
    // ================================================================
    // ViewportCore コールバック設定
    // ================================================================

    private void SetupViewportCoreCallbacks()
    {
        if (_viewportCore == null) return;

        // カメラ状態同期: ViewportCore ← PolyLing（描画直前に毎回同期）
        _viewportCore.OnHandleInput = evt =>
        {
            _viewportCore.RotX = _rotationX;
            _viewportCore.RotY = _rotationY;
            _viewportCore.RotZ = _rotationZ;
            _viewportCore.Distance = _cameraDistance;
            _viewportCore.Target = _cameraTarget;
        };

        // 表示用行列デリゲート
        _viewportCore.GetDisplayMatrixDelegate = meshIndex => GetDisplayMatrix(meshIndex);

        // SkinWeightPaint カスタム描画
        _viewportCore.CustomDrawMesh = (preview, ctx, mesh, meshIndex, displayMatrix) =>
        {
            if (!Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive) return false;
            if (_model == null || !_model.SelectedMeshIndices.Contains(meshIndex)) return false;

            Material visMat = Poly_Ling.Tools.SkinWeightPaintTool.GetVisualizationMaterial();
            if (visMat == null) return false;

            int targetBone = Poly_Ling.Tools.SkinWeightPaintTool.VisualizationTargetBone;
            Poly_Ling.Tools.SkinWeightPaintTool.ApplyVisualizationColors(mesh, ctx.MeshObject, targetBone);
            for (int i = 0; i < mesh.subMeshCount; i++)
                preview.DrawMesh(mesh, displayMatrix, visMat, i);
            return true;
        };

        // キャプチャフック
        _viewportCore.OnCapture = result =>
        {
            if (_captureRequested)
            {
                _captureRequested = false;
                CapturePreviewToRemote(result);
            }
        };

        _viewportCore.RequestRepaint = () => Repaint();
    }

    // ================================================================
    // ViewportPanel連携（入力処理+オーバーレイ描画）
    // ================================================================

    /// <summary>
    /// ViewportPanelのOnHandleInputコールバックから呼ばれる。
    /// ViewportPanel上のイベントをPolyLingのHandleInputに委譲。
    /// </summary>
    public void ProcessInputFromViewport(Poly_Ling.MeshListV2.ViewportEvent evt, Poly_Ling.MeshListV2.ViewportCore core)
    {
        if (_model == null) return;

        // meshContext取得（DrawPreviewと同じロジック）
        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext == null && Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive)
            meshContext = _model.FirstSelectedDrawableMeshContext;
        if (meshContext == null) return;

        // カメラ同期: ViewportCore → PolyLing
        _cameraTarget = evt.CameraTarget;
        _cameraDistance = evt.CameraDistance;
        _rotationX = evt.RotX;
        _rotationY = evt.RotY;

        // PolyLingのadapterにもカメラ情報を渡す（ホバーヒットテスト用）
        _unifiedAdapter?.UpdateFrame(
            evt.CameraPos, evt.CameraTarget, evt.CameraFOV,
            evt.Rect, Event.current.mousePosition, _rotationZ);

        // ViewportCoreのカリング設定を同期
        if (_unifiedAdapter != null && core != null)
            _unifiedAdapter.BackfaceCullingEnabled = core.BackfaceCulling;

        // 入力処理
        HandleInput(evt.Rect, meshContext, evt.CameraPos, evt.CameraTarget, evt.CameraDistance);

        // Repaintイベント以外（MouseDrag等）で入力処理が行われた場合のみ後処理
        if (Event.current.type != EventType.Repaint)
        {
            // ViewportCoreのアダプターにも変換変更を通知
            core?.Adapter?.NotifyTransformChanged();

            // ViewportPanelのRepaintを要求
            core?.RequestRepaint?.Invoke();
        }

        // カメラ同期: PolyLing → ViewportCore（HandleInputがカメラを変更した場合）
        if (core != null)
        {
            core.Target = _cameraTarget;
            core.Distance = _cameraDistance;
            core.RotX = _rotationX;
            core.RotY = _rotationY;
        }
    }

    /// <summary>
    /// ViewportPanelのOnDrawOverlayコールバックから呼ばれる。
    /// ツールギズモ、矩形/投げ縄選択オーバーレイを描画。
    /// </summary>
    public void DrawOverlayFromViewport(Poly_Ling.MeshListV2.ViewportEvent evt, Poly_Ling.MeshListV2.ViewportCore core)
    {
        if (_model == null) return;

        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext == null) return;

        // ToolContext更新
        UpdateToolContext(meshContext, evt.Rect, evt.CameraPos, evt.CameraDistance);

        // ツールギズモ描画
        _currentTool?.DrawGizmo(_toolContext);

        // ボーン移動ギズモ描画
        if (_showBones)
            DrawBoneGizmo(evt.Rect, evt.CameraPos, evt.CameraTarget);

        // WorkPlaneギズモ描画
        if (_showWorkPlaneGizmo && _vertexEditMode && _currentTool == _addFaceTool)
            DrawWorkPlaneGizmo(evt.Rect, evt.CameraPos, evt.CameraTarget);

        // 矩形選択オーバーレイ
        if (_inp.EditState == VertexEditState.BoxSelecting)
            DrawBoxSelectOverlay(evt.Rect);

        // 投げ縄選択オーバーレイ
        if (_inp.EditState == VertexEditState.LassoSelecting)
            DrawLassoSelectOverlay(evt.Rect);
    }
}

/// <summary>
/// UnifiedSystemAdapterをIVisibilityProviderとしてラップ
/// </summary>
internal class UnifiedAdapterVisibilityProvider : Poly_Ling.Rendering.IVisibilityProvider
{
    private readonly Poly_Ling.Core.UnifiedSystemAdapter _adapter;
    private Func<int, (int, int)> _getLineVertices;  // 線分インデックス → (v1, v2)
    private Func<int, int[]> _getFaceVertices;       // 面インデックス → 頂点配列
    public int MeshIndex { get; set; }

    public UnifiedAdapterVisibilityProvider(Poly_Ling.Core.UnifiedSystemAdapter adapter, int meshIndex)
    {
        _adapter = adapter;
        MeshIndex = meshIndex;
    }

    /// <summary>
    /// 線分と面の頂点取得用デリゲートを設定
    /// </summary>
    public void SetGeometryAccessors(Func<int, (int, int)> getLineVertices, Func<int, int[]> getFaceVertices)
    {
        _getLineVertices = getLineVertices;
        _getFaceVertices = getFaceVertices;
    }

    public bool IsVertexVisible(int index)
    {
        if (_adapter == null)
        {
            // Debug removed
            return true;
        }
        if (!_adapter.BackfaceCullingEnabled)
        {
            // Debug removed
            return true;
        }
        bool culled = _adapter.IsVertexCulled(MeshIndex, index);
        if (index < 5) // 最初の数頂点だけログ
        {
            // Debug removed
        }
        return !culled;
    }

    public bool IsLineVisible(int index)
    {
        if (_adapter == null || !_adapter.BackfaceCullingEnabled)
            return true;
        
        // 線分の可視性は両端頂点の少なくとも一方が見えていればtrue
        if (_getLineVertices != null)
        {
            var (v1, v2) = _getLineVertices(index);
            bool v1Visible = !_adapter.IsVertexCulled(MeshIndex, v1);
            bool v2Visible = !_adapter.IsVertexCulled(MeshIndex, v2);
            return v1Visible || v2Visible;
        }
        return true;
    }

    public bool IsFaceVisible(int index)
    {
        if (_adapter == null || !_adapter.BackfaceCullingEnabled)
            return true;
        
        // 面の可視性は少なくとも1つの頂点が見えていればtrue
        if (_getFaceVertices != null)
        {
            var vertices = _getFaceVertices(index);
            if (vertices != null)
            {
                foreach (var v in vertices)
                {
                    if (!_adapter.IsVertexCulled(MeshIndex, v))
                        return true;
                }
                return false;
            }
        }
        return true;
    }

    public float[] GetVertexVisibility()
    {
        // バッチ取得は未実装
        return null;
    }

    public float[] GetLineVisibility()
    {
        return null;
    }

    public float[] GetFaceVisibility()
    {
        return null;
    }
}