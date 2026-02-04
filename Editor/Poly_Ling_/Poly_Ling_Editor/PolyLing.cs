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




public partial class PolyLing : EditorWindow
{   // ================================================================
    // プロジェクトコンテキスト（Phase 0.5: ProjectContext導入）
    // ================================================================
    private ProjectContext _project = new ProjectContext();

    // 後方互換プロパティ（既存コードを壊さない）
    private ModelContext _model => _project.CurrentModel;
    private List<MeshContext> _meshContextList => _model?.MeshContextList;
    
    // v2.0: カテゴリ別選択インデックス
    private int _selectedMeshIndex => _model?.PrimarySelectedMeshIndex ?? -1;
    private int _selectedBoneIndex => _model?.PrimarySelectedBoneIndex ?? -1;
    private int _selectedMorphIndex => _model?.PrimarySelectedMorphIndex ?? -1;

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
            if (_model != null && value >= 0 && value < _model.Count)
            {
                // v2.0: 同一カテゴリのみクリア（他カテゴリの選択は維持）
                _model.SelectByTypeExclusive(value);
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
            return _model.CurrentMeshContext?.GetCurrentMaterial();
        }
        set
        {
            var meshContext = _model.CurrentMeshContext;
            if (meshContext != null && meshContext.CurrentMaterialIndex >= 0 && meshContext.CurrentMaterialIndex < meshContext.Materials.Count)
            {
                meshContext.Materials[meshContext.CurrentMaterialIndex] = value;
            }
        }

    }
    */
    // ================================================================
    // プレビュー
    // ================================================================
    private PreviewRenderUtility _preview;
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

    // 頂点選択
    private HashSet<int> _selectedVertices = new HashSet<int>();

    // 編集状態（共通の選択処理用）
    private enum VertexEditState
    {
        Idle,              // 待機
        PendingAction,     // MouseDown後、ドラッグかクリックか判定中
        BoxSelecting       // 矩形選択中
    }
    private VertexEditState _editState = VertexEditState.Idle;

    // マウス操作用
    private Vector2 _mouseDownScreenPos;      // MouseDown時のスクリーン座標
    private int _hitVertexOnMouseDown = -1;   // MouseDown時にヒットした頂点（-1なら空白）
    private HitResult _hitResultOnMouseDown;  // MouseDown時のヒットテスト結果（新選択システム用）
    private int _hitMeshIndexOnMouseDown = -1; // MouseDown時にヒットしたメッシュインデックス（v2.1複数メッシュ対応）
    private Vector2 _boxSelectStart;          // 矩形選択開始点
    private Vector2 _boxSelectEnd;            // 矩形選択終了点
    private const float DragThreshold = 0f;   // ドラッグ判定の閾値（ピクセル）

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
        get => _undoController?.EditorState?.ShowVertexIndices ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowVertexIndices = value; }
    }
    private bool _showUnselectedWireframe
    {
        get => _undoController?.EditorState?.ShowUnselectedWireframe ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowUnselectedWireframe = value; }
    }
    private bool _showUnselectedVertices
    {
        get => _undoController?.EditorState?.ShowUnselectedVertices ?? true;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowUnselectedVertices = value; }
    }
    private bool _showBones
    {
        get => _undoController?.EditorState?.ShowBones ?? false;
        set { if (_undoController?.EditorState != null) _undoController.EditorState.ShowBones = value; }
    }
    private HashSet<int> _foldedBoneRoots = new HashSet<int>();  // 折りたたまれたボーンルートのインデックス
    
    // タイプ別折りたたみフラグ
    private HashSet<MeshType> _foldedTypes = new HashSet<MeshType> { MeshType.Morph, MeshType.RigidBody, MeshType.RigidBodyJoint, MeshType.Helper, MeshType.Group };
    
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
    private bool _foldMaterials = false;  // 材質パネル（デフォルト閉じ）

    // ペイン幅
    private float _leftPaneWidth = 320f;
    private float _rightPaneWidth = 220f;

    // スプリッター
    private bool _isDraggingLeftSplitter = false;
    private bool _isDraggingRightSplitter = false;
    private Rect _leftSplitterRect;
    private Rect _rightSplitterRect;
    private const float SplitterWidth = 6f;
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
    private SelectionState _selectionState;
    private TopologyCache _meshTopology;
    private SelectionOperations _selectionOps;
    private UnifiedAdapterVisibilityProvider _visibilityProvider;


    // スライダー編集用
    private bool _isSliderDragging = false;
    private Vector3[] _sliderDragStartOffsets;

    // カメラドラッグ用
    private bool _isCameraDragging = false;
    private bool _cameraRestoredByRecord = false; // MeshSelectionChangeRecord等からカメラ復元済みフラグ
    private float _cameraStartRotX, _cameraStartRotY, _cameraStartRotZ;
    private float _cameraStartDistance;
    private Vector3 _cameraStartTarget;
    private WorkPlaneSnapshot? _cameraStartWorkPlaneSnapshot;


    // ================================================================
    // 【フェーズ2追加】選択Undo管理
    // ================================================================

    /// <summary>
    /// マウス操作開始時の選択スナップショット
    /// </summary>
    private SelectionSnapshot _selectionSnapshotOnMouseDown;

    /// <summary>
    /// マウス操作開始時のレガシー選択
    /// </summary>
    private HashSet<int> _legacySelectionOnMouseDown;

    /// <summary>
    /// マウス操作開始時のWorkPlaneスナップショット
    /// </summary>
    private WorkPlaneSnapshot? _workPlaneSnapshotOnMouseDown;

    /// <summary>
    /// このマウス操作中にトポロジー変更があったか
    /// </summary>
    private bool _topologyChangedDuringMouseOperation;




    private SelectionSnapshot _lastSelectionSnapshot;  // Undo用スナップショット


    //  描画キャッシュ
    private MeshDrawCache _drawCache;
    // ================================================================
    // ウインドウ初期化
    // ================================================================
    [MenuItem("Tools/PolyLing")]
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

        // MeshListをUndoコントローラーに設定
        _undoController.SetMeshList(_meshContextList, OnMeshListChanged);

        // カメラ復元コールバックを設定
        if (_undoController.MeshListContext != null)
        {
            _undoController.MeshListContext.OnCameraRestoreRequested = OnCameraRestoreRequested;
            _undoController.MeshListContext.OnFocusMeshListRequested = () => _undoController.FocusMeshList();
            _undoController.MeshListContext.OnReorderCompleted = () => {
                // ========================================================
                // メッシュ順序変更後の完全再構築処理
                // ========================================================
                // 
                // メッシュリストの順序変更は、グローバルバッファ（頂点リスト、面リスト等）の
                // 完全再構築が必要。SetModelContextがこれを行う。
                // 
                // 【重要】以下の処理は必ずこの順序で実行すること：
                // 1. _selectedIndex を ModelContext から同期
                // 2. SetModelContext でグローバルバッファを完全再構築
                // 3. SetActiveMesh で選択メッシュを設定
                //    ※SetModelContextの後に呼ぶこと！
                //    ※SetActiveMeshはBufferManagerのマッピングテーブルを使うため、
                //      バッファ再構築前に呼ぶと古いマッピングで変換されてしまう
                // 4. MeshUndoContext を再設定（参照が変わるため）
                // ========================================================

                // 1. _selectedIndex は ModelContext.ActiveCategory に応じて自動取得される
                // v2.0: _selectedIndex getterがModelContextを直接参照するため、同期不要
                Debug.Log($"[OnReorderCompleted] _selectedIndex={_selectedIndex}, CurrentMesh={_model?.CurrentMeshContext?.Name}");

                // 2. グローバルバッファを完全再構築
                _unifiedAdapter?.SetModelContext(_model);

                // 3. 選択メッシュを設定（バッファ再構築後に呼ぶこと！）
                // SetActiveMeshはContextIndex→UnifiedMeshIndex変換にBufferManagerの
                // マッピングテーブルを使用する。SetModelContextでバッファが再構築されると
                // マッピングテーブルも新しくなるため、必ず再構築後に呼ぶ必要がある。
                _unifiedAdapter?.SetActiveMesh(0, _selectedIndex);
                _unifiedAdapter?.BufferManager?.UpdateAllSelectionFlags();

                // 4. MeshUndoContextを再設定（UnityMeshが再構築されるため参照が変わる）
                var meshContext = _model?.CurrentMeshContext;
                if (meshContext != null && _undoController != null)
                {
                    _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
                    _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
                    _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
                }

                // 5. トポロジー更新（SelectMeshAtIndexと同じ処理）
                // SetModelContextだけでは不十分。UpdateTopologyも呼ぶ必要がある。
                UpdateTopology();
            };
            _undoController.MeshListContext.OnVertexEditStackClearRequested = () => _undoController?.VertexEditStack?.Clear();
        }

        // ModelContextにWorkPlaneを設定
        if (_model != null)
            _model.WorkPlane = _undoController.WorkPlane;

        // ★Phase 1: MeshUndoContext.MaterialOwner を設定（Materials Undo用）
        if (_undoController.MeshUndoContext != null && _model != null)
        {
            _undoController.MeshUndoContext.MaterialOwner = _model;
        }

        // ★Phase 1: 既存 MeshContext にも MaterialOwner を設定
        if (_meshContextList != null && _model != null)
        {
            foreach (var meshContext in _meshContextList)
            {
                if (meshContext != null)
                {
                    meshContext.MaterialOwner = _model;
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

        // 統合システム初期化（失敗時はウィンドウを閉じる）
        if (!InitializeUnifiedSystem())
        {
            Close();
            return;
        }

        // VisibilityProviderを設定（背面カリング対応）
        if (_unifiedAdapter != null && _selectionOps != null)
        {
            _visibilityProvider = new UnifiedAdapterVisibilityProvider(_unifiedAdapter, _selectedIndex);
            
            // 線分と面の頂点取得用デリゲートを設定
            _visibilityProvider.SetGeometryAccessors(
                // 線分インデックス → (v1, v2)
                lineIndex => {
                    var meshObject = _model?.CurrentMeshContext?.MeshObject;
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
                    var meshObject = _model?.CurrentMeshContext?.MeshObject;
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
        _selectionState = new SelectionState();
        _meshTopology = new TopologyCache();
        _selectionOps = new SelectionOperations(_selectionState, _meshTopology);
        
        // GPU/CPUヒットテストの閾値を統一（HOVER_LINE_DISTANCE = 18f と同じ）
        _selectionOps.EdgeHitDistance = 18f;

        _selectionState.OnSelectionChanged += SyncSelectionToLegacy;

        // Undo/Redo時の選択状態復元
        if (_undoController != null)
        {
            // _undoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        _lastSelectionSnapshot = _selectionState.CreateSnapshot();
        UpdateTopology();
    }


    /// <summary>
    /// 新システム → 既存システムへの同期（移行期間用）
    /// </summary>
    private void SyncSelectionToLegacy()
    {
        // Vertex選択のみ同期（現状の機能を維持）
        _selectedVertices.Clear();
        foreach (var v in _selectionState.Vertices)
        {
            _selectedVertices.Add(v);
        }
        Repaint();
    }

    /// <summary>
    /// 既存システム → 新システムへの同期
    /// </summary>
    private void SyncSelectionFromLegacy()
    {
        _selectionState.Vertices.Clear();
        foreach (var v in _selectedVertices)
        {
            _selectionState.Vertices.Add(v);
        }
        // 統合システムに選択変更を通知
        _unifiedAdapter?.NotifySelectionChanged();
    }

    /// <summary>
    /// MeshObject変更時にトポロジを更新
    /// </summary>
    private void UpdateTopology()
    {
        if (_meshTopology == null)
            return;

        var meshContext = _model.CurrentMeshContext;
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
        CleanupPreview();
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
            _selectionState.OnSelectionChanged -= SyncSelectionToLegacy;
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
        // OnDisable() に追加
        CleanupUnifiedSystem();     // SimpleMeshFactory_UnifiedSystem.cs に定義済み

    }

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// </summary>
// PolyLing.cs の OnUndoRedoPerformed メソッドを以下に置き換え

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// </summary>
    private void OnUndoRedoPerformed()
    {
        Debug.Log($"[OnUndoRedoPerformed] === START === _selectedIndex={_selectedIndex}, MeshListContext.PrimarySelectedMeshContextIndex={_undoController?.MeshListContext?.PrimarySelectedMeshContextIndex}");
        Debug.Log($"[OnUndoRedoPerformed] _selectedVertices.Count={_selectedVertices.Count}, ctx.SelectedVertices?.Count={_undoController?.MeshUndoContext?.SelectedVertices?.Count}");
        
        // コンテキストからメッシュに反映
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            var ctx = _undoController.MeshUndoContext;

            if (ctx.MeshObject != null)
            {
                // Clone()で新しいインスタンスを作成
                var clonedMeshObject = ctx.MeshObject.Clone();
                meshContext.MeshObject = clonedMeshObject;
                
                // ctx.MeshObjectも同期（次の操作記録で正しいMeshObjectを参照するため）
                ctx.MeshObject = clonedMeshObject;
                
                SyncMeshFromData(meshContext);

                if (ctx.MeshObject.VertexCount > 0)
                {
                    UpdateOffsetsFromData(meshContext);
                }
            }

            // 注意: ctx.SelectedVertices は SelectionRecord の Undo 時にのみ更新される
            // VertexMoveRecord の Undo 時は更新されないため、ここで無条件に反映すると
            // 古い値で上書きしてしまう。選択の復元は SelectionSnapshot 経由でのみ行う。
            // (以前のコード: _selectedVertices = new HashSet<int>(ctx.SelectedVertices);)

            // マテリアル復元は ModelContext に集約済み
            // TODO: Undo Record からの Materials 復元を実装

            // カリングA 設定をGPUレンダラーに復元
        }

        // デフォルトマテリアル復元
        var ctxForDefault = _undoController.MeshUndoContext;
        if (ctxForDefault.DefaultMaterials != null && ctxForDefault.DefaultMaterials.Count > 0)
        {
            _defaultMaterials = new List<Material>(ctxForDefault.DefaultMaterials);
        }
        _defaultCurrentMaterialIndex = ctxForDefault.DefaultCurrentMaterialIndex;
        _autoSetDefaultMaterials = ctxForDefault.AutoSetDefaultMaterials;

        // Single Source of Truth: EditorStateContext が唯一のデータソースのため、
        // ローカル変数への復元コピーは不要（プロパティ経由で直接参照）
        // カメラ復元フラグのリセットのみ実行
        _cameraRestoredByRecord = false;

        // ツール復元
        var editorState = _undoController.EditorState;
        RestoreToolFromName(editorState.CurrentToolName);

        //ナイフツールの固有設定----
        /*if (_knifeTool != null)
        {
            _knifeTool.knifeProperty.Mode = editorStateDTO.knifeProperty.Mode;
            _knifeTool.knifeProperty.EdgeSelect = editorStateDTO.knifeProperty.EdgeSelect;
            _knifeTool.knifeProperty.ChainMode = editorStateDTO.knifeProperty.ChainMode;
        }
        */
        //// ツール汎用設定の復元
        ApplyToTools(editorState);

        _currentTool?.Reset();
        ResetEditState();

        // SelectionState を復元
        var ctx2 = _undoController.MeshUndoContext;
        Debug.Log($"[OnUndoRedoPerformed] SelectionSnapshot check: ctx2.CurrentSelectionSnapshot={ctx2.CurrentSelectionSnapshot != null}, ctx2.SelectedVertices.Count={ctx2.SelectedVertices?.Count ?? -1}");
        if (ctx2.CurrentSelectionSnapshot != null && _selectionState != null)
        {
            Debug.Log($"[OnUndoRedoPerformed] Restoring from SelectionSnapshot. Snapshot.Vertices.Count={ctx2.CurrentSelectionSnapshot.Vertices?.Count ?? -1}");
            // 拡張選択スナップショットから復元（Edge/Face/Lines/Modeを含む完全な復元）
            _selectionState.RestoreFromSnapshot(ctx2.CurrentSelectionSnapshot);
            ctx2.CurrentSelectionSnapshot = null;  // 使用済みなのでクリア

            // _selectedVertices も同期
            _selectedVertices = new HashSet<int>(_selectionState.Vertices);
            Debug.Log($"[OnUndoRedoPerformed] After restore: _selectedVertices.Count={_selectedVertices.Count}");
        }
        else
        {
            // SelectionSnapshotがない場合は選択状態を変更しない
            // VertexMoveRecord等のUndo時は選択状態は維持される
            Debug.Log($"[OnUndoRedoPerformed] No SelectionSnapshot, keeping current selection state. _selectedVertices.Count={_selectedVertices.Count}");
        }

        // MeshListContextからの選択インデックス反映は不要
        // MeshSelectionChangeRecord.Undo/Redo() が OnListChanged?.Invoke() を呼び出し、
        // OnMeshListChanged() で _selectedIndex が正しく更新される。
        // ここで再度チェックすると、VertexMoveRecord等のUndo時に古い状態を参照してしまう。

        Debug.Log($"[OnUndoRedoPerformed] === END === _selectedIndex={_selectedIndex}, _selectedVertices.Count={_selectedVertices.Count}");
        Repaint();

        // ミラーキャッシュを無効化（頂点位置変更でも正しく更新されるように）
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
            Repaint();
        }

        // UndoManagerのキュー処理（従来の処理）
        int processed = UndoManager.Instance.ProcessAllQueues();

        if (processed > 0)
        {
            // キューが処理されたらUIを更新
            Repaint();
        }
    }

    /// <summary>
    /// MeshListのUndo/Redo後のコールバック
    /// </summary>
    private void OnMeshListChanged()
    {
        Debug.Log($"[OnMeshListChanged] Before: _cameraTarget={_cameraTarget}, _cameraDistance={_cameraDistance}");
        Debug.Log($"[OnMeshListChanged] Before: _selectedIndex={_selectedIndex}, MeshListContext.PrimarySelectedMeshContextIndex={_undoController?.MeshListContext?.PrimarySelectedMeshContextIndex}");

        // v2.0: _selectedIndex getterがModelContextを直接参照するため、同期不要
        // MeshListContext（= ModelContext）の選択状態はUndo/Redoで自動復元される

        Debug.Log($"[OnMeshListChanged] After sync: _selectedIndex={_selectedIndex}, CurrentMesh={_model.CurrentMeshContext?.Name}");

        // 選択中のメッシュコンテキストをMeshContextに設定
        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            // MeshContextに必要な情報だけを設定
            if (_undoController != null)
            {
                _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
                _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
                _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
                // Materials は ModelContext に集約済み
            }

            Debug.Log($"[OnMeshListChanged] Before InitVertexOffsets: _cameraTarget={_cameraTarget}");
            InitVertexOffsets(updateCamera: false);
            Debug.Log($"[OnMeshListChanged] After InitVertexOffsets: _cameraTarget={_cameraTarget}");
        }
        else
        {
            _selectedIndex = _meshContextList.Count > 0 ? 0 : -1;
            var fallbackMeshContext = _model.CurrentMeshContext;
            if (fallbackMeshContext != null)
            {
                if (_undoController != null)
                {
                    _undoController.MeshUndoContext.MeshObject = fallbackMeshContext.MeshObject;
                    _undoController.MeshUndoContext.TargetMesh = fallbackMeshContext.UnityMesh;
                    _undoController.MeshUndoContext.OriginalPositions = fallbackMeshContext.OriginalPositions;
                    // Materials は ModelContext に集約済み
                }

                InitVertexOffsets(updateCamera: false);
            }
        }

        // 選択クリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        if (_undoController != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();
        }

        Debug.Log($"[OnMeshListChanged] After: _cameraTarget={_cameraTarget}, _cameraDistance={_cameraDistance}");
        Debug.Log($"[OnMeshListChanged] Final: _selectedIndex={_selectedIndex}, CurrentMesh={_model.CurrentMeshContext?.Name}");
        Repaint();
    }
    /*
    /// <summary>
    /// カメラ状態を復元（Undo/Redo時のコールバック）
    /// </summary>
    private void OnCameraRestoreRequested(CameraSnapshot camera)
    {
        Debug.Log($"[OnCameraRestoreRequested] BEFORE: rotX={_rotationX}, rotY={_rotationY}, dist={_cameraDistance}, target={_cameraTarget}");
        Debug.Log($"[OnCameraRestoreRequested] RESTORING TO: rotX={camera.RotationX}, rotY={camera.RotationY}, dist={camera.CameraDistance}, target={camera.CameraTarget}");
        _rotationX = camera.RotationX;
        _rotationY = camera.RotationY;
        _cameraDistance = camera.CameraDistance;
        _cameraTarget = camera.CameraTarget;
        _cameraRestoredByRecord = true; // OnUndoRedoPerformedでの上書きを防ぐ
        Debug.Log($"[OnCameraRestoreRequested] AFTER: rotX={_rotationX}, rotY={_rotationY}, dist={_cameraDistance}, target={_cameraTarget}");
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
        NotifyUnifiedTransformChanged();
        // ★GPUバッファを再構築（トポロジ変更対応）
        // 頂点数/面数が変わる可能性があるため、常にトポロジ変更として扱う
        //_unifiedAdapter?.NotifyTopologyChanged();
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

        // 頂点位置配列を構築
        int vertexCount = meshObject.VertexCount;
        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = meshObject.Vertices[i].Position;
        }

        // 位置のみ更新
        unityMesh.vertices = vertices;
        unityMesh.RecalculateBounds();

        // GPUバッファの位置情報を更新
        NotifyUnifiedTransformChanged();
    }

    /// <summary>
    /// v2.1: 選択中の全メッシュの頂点位置を同期
    /// </summary>
    private void SyncAllSelectedMeshPositions()
    {
        if (_model == null || _model.SelectedMeshIndices.Count == 0)
        {
            // フォールバック: プライマリメッシュのみ
            SyncMeshPositionsOnly(_model?.CurrentMeshContext);
            return;
        }

        foreach (int meshIdx in _model.SelectedMeshIndices)
        {
            var meshContext = _model.GetMeshContext(meshIdx);
            if (meshContext?.MeshObject == null || meshContext.UnityMesh == null)
                continue;

            var meshObject = meshContext.MeshObject;
            var unityMesh = meshContext.UnityMesh;

            // 頂点位置配列を構築
            int vertexCount = meshObject.VertexCount;
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
        NotifyUnifiedTransformChanged();
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
        _preview = new PreviewRenderUtility();
        _preview.cameraFieldOfView = 30f;
        _preview.camera.nearClipPlane = 0.01f;
        _preview.camera.farClipPlane = 100f;
    }

    private void CleanupPreview()
    {
        if (_preview != null)
        {
            _preview.Cleanup();
            _preview = null;
        }
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
    private void OnGUI()
    {
        // Undoショートカット処理
        if (_undoController != null)
        {
            Event e_ = Event.current;
            if (e_.type == EventType.KeyDown)
            {
                Debug.Log($"[PolyLing.OnGUI] KeyDown before HandleKeyboardShortcuts: keyCode={e_.keyCode}, ctrl={e_.control || e_.command}, shift={e_.shift}, used={e_.type == EventType.Used}");
            }
            
            if (_undoController.HandleKeyboardShortcuts(Event.current))
            {
                // Undo/Redo後にUIを再描画
                var parentModel = _undoController?.MeshUndoContext?.MaterialOwner;
                Debug.Log($"[PolyLing.OnGUI] After Undo/Redo: _model.MaterialCount={_model?.Materials?.Count ?? 0}, MeshUndoContext.Materials.Count={_undoController?.MeshUndoContext?.Materials?.Count ?? 0}");
                Debug.Log($"[PolyLing.OnGUI] _model==MaterialOwner? {ReferenceEquals(_model, parentModel)}, _model.Hash={_model?.GetHashCode()}, MaterialOwner.Hash={parentModel?.GetHashCode()}");
                Repaint();
            }
        }

        // グローバルなドラッグ終了検出
        Event e = Event.current;
        if (e.type == EventType.MouseUp)
        {
            if (_isSliderDragging)
            {
                EndSliderDrag();
            }
            if (_isCameraDragging)
            {
                EndCameraDrag();
            }
            // スプリッタードラッグ終了
            _isDraggingLeftSplitter = false;
            _isDraggingRightSplitter = false;
        }

        HandleScrollWheel();

        // スプリッターのドラッグ処理
        HandleSplitterDrag(e);

        EditorGUILayout.BeginHorizontal();

        // 左ペイン：メッシュリスト
        DrawMeshList();

        // 左スプリッター
        DrawSplitter(ref _leftSplitterRect, true);

        // 中央ペイン：プレビュー
        DrawPreview();

        // 右スプリッター
        DrawSplitter(ref _rightSplitterRect, false);

        // 右ペイン：頂点編集
        DrawVertexEditor();

        EditorGUILayout.EndHorizontal();

        // カーソル変更
        UpdateSplitterCursor();
    }

    private void HandleScrollWheel()
    {
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
            float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad;
            float worldHeightAtDist = 2f * _cameraDistance * Mathf.Tan(fovRad / 2f);
            float pixelToWorld = worldHeightAtDist / _lastPreviewRect.height;
            Debug.Log($"[CameraPan] delta={e.delta}, worldDelta={worldDelta}, multiplier={multiplier}, " +
                      $"FOV={_preview.cameraFieldOfView}, camDist={_cameraDistance}, " +
                      $"rectHeight={_lastPreviewRect.height}, pixelToWorld={pixelToWorld}");

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
        
        // =====================================================================
        // 【重要】カメラ操作中の最適化フラグ - 絶対にコメントアウトしないこと！
        // =====================================================================
        // これらのフラグを無効化すると、カメラドラッグ中に以下の重い処理が
        // 毎フレーム実行され、深刻なパフォーマンス低下を引き起こす：
        // - ヒットテスト（不要なGPU計算）
        // - 頂点フラグ読み戻し（GPU→CPU転送）
        // - 可視性計算（ComputeShader実行）
        // - 非選択メッシュの描画（大量の頂点処理）
        // - メッシュ再構築（毎フレームList生成+全頂点走査）
        // 
        // デバッグ時も個別にテストすること。一括コメントアウト禁止！
        // =====================================================================
        if (_unifiedAdapter != null)
        {
            _unifiedAdapter.SkipHitTest = true;
            _unifiedAdapter.SkipVertexFlagsReadback = true;
            _unifiedAdapter.SkipGpuVisibilityCompute = true;
            _unifiedAdapter.SkipUnselectedWireframe = true;
            _unifiedAdapter.SkipUnselectedVertices = true;
            _unifiedAdapter.SkipMeshRebuild = true;  // カメラ操作中はメッシュ再構築もスキップ
        }
        
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
        
        // ヒットテストを再開
        if (_unifiedAdapter != null)
        {
            _unifiedAdapter.SkipHitTest = false;
            _unifiedAdapter.SkipVertexFlagsReadback = false;
            _unifiedAdapter.SkipGpuVisibilityCompute = false;
            _unifiedAdapter.SkipUnselectedWireframe = false;
            _unifiedAdapter.SkipUnselectedVertices = false;
            _unifiedAdapter.SkipMeshRebuild = false;
        }

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
    private int _leftSplitterControlId;
    private int _rightSplitterControlId;

    /// <summary>
    /// スプリッターを描画（イベント処理込み）
    /// </summary>
    private void DrawSplitter(ref Rect splitterRect, bool isLeftSplitter)
    {
        // コントロールIDを取得
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (isLeftSplitter)
            _leftSplitterControlId = controlId;
        else
            _rightSplitterControlId = controlId;

        // スプリッター領域を確保
        splitterRect = GUILayoutUtility.GetRect(
            SplitterWidth, SplitterWidth,
            GUILayout.ExpandHeight(true));

        // イベント処理
        Event e = Event.current;

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (e.button == 0 && splitterRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    if (isLeftSplitter)
                        _isDraggingLeftSplitter = true;
                    else
                        _isDraggingRightSplitter = true;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    if (isLeftSplitter)
                    {
                        _leftPaneWidth += e.delta.x;
                        _leftPaneWidth = Mathf.Clamp(_leftPaneWidth, MinPaneWidth, MaxLeftPaneWidth);
                    }
                    else
                    {
                        _rightPaneWidth -= e.delta.x;
                        _rightPaneWidth = Mathf.Clamp(_rightPaneWidth, MinPaneWidth, MaxRightPaneWidth);
                    }
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    if (isLeftSplitter)
                        _isDraggingLeftSplitter = false;
                    else
                        _isDraggingRightSplitter = false;
                    e.Use();
                }
                break;

            case EventType.Repaint:
                UnityEditor_Handles.BeginGUI();
                // 背景色
                bool isDragging = GUIUtility.hotControl == controlId;
                bool isHovering = splitterRect.Contains(e.mousePosition);

                Color splitterColor;
                if (isDragging)
                {
                    splitterColor = new Color(0.4f, 0.6f, 1f, 0.8f);  // ドラッグ中：青
                }
                else if (isHovering)
                {
                    splitterColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);  // ホバー：グレー
                }
                else
                {
                    splitterColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);  // 通常：薄いグレー
                }
                UnityEditor_Handles.DrawRect(splitterRect, splitterColor);//?

                // 中央にグリップを描画
                float centerX = splitterRect.x + splitterRect.width / 2;
                float gripHeight = Mathf.Min(splitterRect.height * 0.3f, 60f);
                float gripTop = splitterRect.y + (splitterRect.height - gripHeight) / 2;

                Color gripColor = isDragging || isHovering
                    ? new Color(0.7f, 0.7f, 0.7f, 0.8f)
                    : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                for (int i = 0; i < 3; i++)
                {
                    float y = gripTop + i * (gripHeight / 2);
                    UnityEditor_Handles.DrawRect(new Rect(centerX - 1, y, 2, 2), gripColor);//?
                }
                UnityEditor_Handles.EndGUI();
                break;
        }

        // カーソル変更
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
    }

    /// <summary>
    /// スプリッターのドラッグを処理（未使用、DrawSplitter内で処理）
    /// </summary>
    private void HandleSplitterDrag(Event e)
    {
        // DrawSplitter内で処理するため、ここでは何もしない
    }

    /// <summary>
    /// スプリッター上でのカーソル変更（未使用、DrawSplitter内で処理）
    /// </summary>
    private void UpdateSplitterCursor()
    {
        // DrawSplitter内で処理するため、ここでは何もしない
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