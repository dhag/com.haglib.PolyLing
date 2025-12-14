// Assets/Editor/SimpleMeshFactory.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshData（Vertex/Face）ベース対応版
// DefaultMaterials対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;
using MeshFactory.Model;
using MeshFactory.Localization;




public partial class SimpleMeshFactory : EditorWindow
{
    // ================================================================
    // メッシュコンテキスト
    //   MeshDataにUnityMeshなどを加えたもの。
    // ================================================================
    public class MeshContext
    {
        public string Name
        {
            get => Data?.Name ?? "Untitled";
            set { if (Data != null) Data.Name = value; }
        }
        public Mesh UnityMesh;                      // Unity UnityMesh（表示用）
        public MeshData Data;                       // 新構造データ
        public Vector3[] OriginalPositions;         // 元の頂点位置（リセット用）
        public ExportSettings ExportSettings;       // エクスポート時のトランスフォーム設定

        // マルチマテリアル対応
        public List<Material> Materials = new List<Material>();
        public int CurrentMaterialIndex = 0;

        public MeshContext()
        {
            ExportSettings = new ExportSettings();
            Materials.Add(null); // デフォルトマテリアルスロット（slot 0）
        }

        /// <summary>
        /// 現在選択中のマテリアルを取得
        /// </summary>
        public Material GetCurrentMaterial()
        {
            if (CurrentMaterialIndex >= 0 && CurrentMaterialIndex < Materials.Count)
                return Materials[CurrentMaterialIndex];
            return null;
        }

        /// <summary>
        /// 指定スロットのマテリアルを取得（範囲外ならnull）
        /// </summary>
        public Material GetMaterial(int index)
        {
            if (index >= 0 && index < Materials.Count)
                return Materials[index];
            return null;
        }

        /// <summary>
        /// サブメッシュ数を取得
        /// </summary>
        public int SubMeshCount => Materials.Count;
    }


    // ================================================================
    // モデルコンテキスト（Phase 1: ModelContext導入）
    // ================================================================
    private ModelContext _model = new ModelContext();

    // 後方互換プロパティ（既存コードを壊さない）
    private List<MeshContext> _meshContextList => _model.MeshContextList;
    private int _selectedIndex
    {
        get => _model.SelectedIndex;
        set => _model.SelectedIndex = value;
    }

    private Vector2 _vertexScroll;

    // ================================================================
    // デフォルトマテリアル（新規メッシュ作成時に適用）
    // ================================================================
    private List<Material> _defaultMaterials = new List<Material> { null };
    private int _defaultCurrentMaterialIndex = 0;
    private bool _autoSetDefaultMaterials = true;
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
    private float _rotationY = 0f;
    private float _rotationX = 20f;
    private float _rotationZ = 0f;  // Z軸回転（Ctrl+右ドラッグ）
    private float _cameraDistance = 2f;
    private Vector3 _cameraTarget = Vector3.zero;

    // ============================================================================
    // === メッシュ追加モード ===
    // ============================================================================
    // 
    [SerializeField]
    private bool _addToCurrentMesh = true;  // デフォルトでON

    [SerializeField]
    private bool _autoMergeOnCreate = true;  // 生成時に自動マージ

    [SerializeField]
    private float _autoMergeThreshold = 0.001f;  // マージしきい値



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
    private Vector2 _boxSelectStart;          // 矩形選択開始点
    private Vector2 _boxSelectEnd;            // 矩形選択終了点
    private const float DragThreshold = 4f;   // ドラッグ判定の閾値（ピクセル）

    // 表示設定
    private bool _showWireframe = true;
    private bool _showVertices = true;
    private bool _vertexEditMode = true;  // Show Verticesと連動

    /// <summary>
    /// ツールの状態
    /// </summary>
    // UIフォールドアウト状態
    private bool _foldDisplay = true;
    private bool _foldPrimitive = true;

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

    // WorkPlane表示設定
    private bool _showWorkPlaneGizmo = true;

    // ================================================================
    // Undoシステム統合
    // ================================================================
    private MeshUndoController _undoController;
    // ================================================================
    // Selection System
    // ================================================================
    private SelectionState _selectionState;
    private TopologyCache _meshTopology;
    private SelectionOperations _selectionOps;


    // スライダー編集用
    private bool _isSliderDragging = false;
    private Vector3[] _sliderDragStartOffsets;

    // カメラドラッグ用
    private bool _isCameraDragging = false;
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
    // ================================================================
    // ウインドウ初期化
    // ================================================================
    [MenuItem("Tools/SimpleMeshFactory")]
    private static void Open()
    {
        var window = GetWindow<SimpleMeshFactory>("SimpleMeshFactory");
        window.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        InitPreview();
        wantsMouseMove = true;
        
        // ローカライゼーション設定を読み込み
        L.LoadSettings();

        // Undoコントローラー初期化
        _undoController = new MeshUndoController("SimpleMeshFactory");
        _undoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        
        // MeshListをUndoコントローラーに設定
        _undoController.SetMeshList(_meshContextList, OnMeshListChanged);

        // ModelContextにWorkPlaneを設定
        _model.WorkPlane = _undoController.WorkPlane;

        // Show Verticesと編集モードを同期
        _vertexEditMode = _showVertices;

        // EditorStateにローカル変数の初期値を設定
        if (_undoController != null)
        {
            _undoController.EditorState.ShowWireframe = _showWireframe;
            _undoController.EditorState.ShowVertices = _showVertices;
            _undoController.EditorState.AddToCurrentMesh = _addToCurrentMesh;
            _undoController.EditorState.AutoMergeOnCreate = _autoMergeOnCreate;
            _undoController.EditorState.AutoMergeThreshold = _autoMergeThreshold;
        }

        // Selection System 初期化（ツールより先に初期化）
        InitializeSelectionSystem();

        // ツール初期化
        InitializeTools();

        // 初期ツール名をEditorStateに設定
        if (_undoController != null && _currentTool != null)
        {
            _undoController.EditorState.CurrentToolName = _currentTool.Name;
        }

        // WorkPlane UIイベントハンドラ設定
        SetupWorkPlaneEventHandlers();

        // ExportSettings UIイベントハンドラ設定
        SetupExportSettingsEventHandlers();
    }

    /// <summary>
    /// Selection System 初期化
    /// </summary>
    private void InitializeSelectionSystem()
    {
        _selectionState = new SelectionState();
        _meshTopology = new TopologyCache();
        _selectionOps = new SelectionOperations(_selectionState, _meshTopology);

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
    }

    /// <summary>
    /// MeshData変更時にトポロジを更新
    /// </summary>
    private void UpdateTopology()
    {
        if (_meshTopology == null)
            return;

        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            _meshTopology.SetMeshData(meshContext.Data);
        }
        else
        {
            _meshTopology.SetMeshData(null);
        }
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



        // WorkPlane UIイベントハンドラ解除
        CleanupWorkPlaneEventHandlers();

        // ExportSettings UIイベントハンドラ解除
        CleanupExportSettingsEventHandlers();

        // Selection System クリーンアップ
        if (_selectionState != null)
        {
            _selectionState.OnSelectionChanged -= SyncSelectionToLegacy;
        }

        // Undoコントローラー破棄
        if (_undoController != null)
        {
            _undoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            _undoController.Dispose();
            _undoController = null;
        }
    }

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// </summary>
// SimpleMeshFactory.cs の OnUndoRedoPerformed メソッドを以下に置き換え

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// </summary>
    private void OnUndoRedoPerformed()
    {
        // コンテキストからメッシュに反映
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            var ctx = _undoController.MeshContext;

            if (ctx.MeshData != null)
            {
                meshContext.Data = ctx.MeshData.Clone();
                SyncMeshFromData(meshContext);

                if (ctx.MeshData.VertexCount > 0)
                {
                    UpdateOffsetsFromData(meshContext);
                }
            }

            if (ctx.SelectedVertices != null)
            {
                _selectedVertices = new HashSet<int>(ctx.SelectedVertices);
            }

            // マテリアル復元（マルチマテリアル対応）
            if (ctx.Materials != null && ctx.Materials.Count > 0)
            {
                meshContext.Materials = new List<Material>(ctx.Materials);
                meshContext.CurrentMaterialIndex = ctx.CurrentMaterialIndex;
            }
        }

        // デフォルトマテリアル復元
        var ctxForDefault = _undoController.MeshContext;
        if (ctxForDefault.DefaultMaterials != null && ctxForDefault.DefaultMaterials.Count > 0)
        {
            _defaultMaterials = new List<Material>(ctxForDefault.DefaultMaterials);
        }
        _defaultCurrentMaterialIndex = ctxForDefault.DefaultCurrentMaterialIndex;
        _autoSetDefaultMaterials = ctxForDefault.AutoSetDefaultMaterials;

        // EditorStateContextからUI状態を復元
        var editorState = _undoController.EditorState;
        _rotationX = editorState.RotationX;
        _rotationY = editorState.RotationY;
        _cameraDistance = editorState.CameraDistance;
        _cameraTarget = editorState.CameraTarget;
        _showWireframe = editorState.ShowWireframe;
        _showVertices = editorState.ShowVertices;
        _addToCurrentMesh = editorState.AddToCurrentMesh;
        _autoMergeOnCreate = editorState.AutoMergeOnCreate;
        _autoMergeThreshold = editorState.AutoMergeThreshold;

        RestoreToolFromName(editorState.CurrentToolName);

        //ナイフツールの固有設定----
        /*if (_knifeTool != null)
        {
            _knifeTool.knifeProperty.Mode = editorState.knifeProperty.Mode;
            _knifeTool.knifeProperty.EdgeSelect = editorState.knifeProperty.EdgeSelect;
            _knifeTool.knifeProperty.ChainMode = editorState.knifeProperty.ChainMode;
        }
        */
        //// ツール汎用設定の復元
        ApplyToTools(editorState);

            _currentTool?.Reset();
        ResetEditState();

        // SelectionState を復元
        var ctx2 = _undoController.MeshContext;
        if (ctx2.CurrentSelectionSnapshot != null && _selectionState != null)
        {
            // 拡張選択スナップショットから復元（Edge/Face/Lines/Modeを含む完全な復元）
            _selectionState.RestoreFromSnapshot(ctx2.CurrentSelectionSnapshot);
            ctx2.CurrentSelectionSnapshot = null;  // 使用済みなのでクリア

            // _selectedVertices も同期
            _selectedVertices = new HashSet<int>(_selectionState.Vertices);
        }
        else
        {
            // 従来のレガシー同期（Vertexモードのみ）
            SyncSelectionFromLegacy();
        }

        // MeshListContextから選択インデックスを反映
        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        if (_undoController?.MeshListContext != null)
        {
            int newIndex = _undoController.MeshListContext.SelectedIndex;
            if (newIndex != _selectedIndex && newIndex >= -1 && newIndex < _meshContextList.Count)
            {
                _selectedIndex = newIndex;
                var newMeshContext = _model.CurrentMeshContext;
                if (newMeshContext != null)
                {
                    // MeshContextに必要な情報だけを設定
                    _undoController.MeshContext.MeshData = newMeshContext.Data;
                    _undoController.MeshContext.TargetMesh = newMeshContext.UnityMesh;
                    _undoController.MeshContext.OriginalPositions = newMeshContext.OriginalPositions;
                    _undoController.MeshContext.Materials = newMeshContext.Materials != null 
                        ? new List<Material>(newMeshContext.Materials) 
                        : new List<Material>();
                    _undoController.MeshContext.CurrentMaterialIndex = newMeshContext.CurrentMaterialIndex;
                }
            }
        }

        Repaint();
    }

    /// <summary>
    /// MeshListのUndo/Redo後のコールバック
    /// </summary>
    private void OnMeshListChanged()
    {
        // MeshListContextから選択インデックスを取得
        if (_undoController?.MeshListContext != null)
        {
            _selectedIndex = _undoController.MeshListContext.SelectedIndex;
        }

        // 選択中のメッシュコンテキストをMeshContextに設定
        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            // MeshContextに必要な情報だけを設定
            if (_undoController != null)
            {
                _undoController.MeshContext.MeshData = meshContext.Data;
                _undoController.MeshContext.TargetMesh = meshContext.UnityMesh;
                _undoController.MeshContext.OriginalPositions = meshContext.OriginalPositions;
                _undoController.MeshContext.Materials = meshContext.Materials != null 
                    ? new List<Material>(meshContext.Materials) 
                    : new List<Material>();
                _undoController.MeshContext.CurrentMaterialIndex = meshContext.CurrentMaterialIndex;
            }
            
            InitVertexOffsets(updateCamera: false);
        }
        else
        {
            _selectedIndex = _meshContextList.Count > 0 ? 0 : -1;
            var fallbackMeshContext = _model.CurrentMeshContext;
            if (fallbackMeshContext != null)
            {
                if (_undoController != null)
                {
                    _undoController.MeshContext.MeshData = fallbackMeshContext.Data;
                    _undoController.MeshContext.TargetMesh = fallbackMeshContext.UnityMesh;
                    _undoController.MeshContext.OriginalPositions = fallbackMeshContext.OriginalPositions;
                    _undoController.MeshContext.Materials = fallbackMeshContext.Materials != null 
                        ? new List<Material>(fallbackMeshContext.Materials) 
                        : new List<Material>();
                    _undoController.MeshContext.CurrentMaterialIndex = fallbackMeshContext.CurrentMaterialIndex;
                }
                
                InitVertexOffsets(updateCamera: false);
            }
        }

        // 選択クリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();
        
        if (_undoController != null)
        {
            _undoController.MeshContext.SelectedVertices = new HashSet<int>();
        }

        Repaint();
    }



    private void SyncMeshFromData(MeshContext meshContext)
    {
        if (meshContext.Data == null || meshContext.UnityMesh == null)
            return;

        var newMesh = meshContext.Data.ToUnityMesh();
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
    }






    /// <summary>
    /// MeshDataからオフセットを更新
    /// </summary>
    private void UpdateOffsetsFromData(MeshContext meshContext)
    {
        if (meshContext.Data == null || _vertexOffsets == null)
            return;

        int count = Mathf.Min(meshContext.Data.VertexCount, _vertexOffsets.Length);
        for (int i = 0; i < count; i++)
        {
            if (i < meshContext.OriginalPositions.Length)
            {
                _vertexOffsets[i] = meshContext.Data.Vertices[i].Position - meshContext.OriginalPositions[i];
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
        foreach (var meshContext in _meshContextList)
        {
            if (meshContext.UnityMesh != null)
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
            _undoController.HandleKeyboardShortcuts(Event.current);
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
        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            if (!_isCameraDragging)
            {
                BeginCameraDrag();
            }

            Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;

            float panSpeed = _cameraDistance * 0.002f;
            _cameraTarget -= right * e.delta.x * panSpeed;
            _cameraTarget += up * e.delta.y * panSpeed;

            e.Use();
            Repaint();
            return;
        }

        // ホイールでズーム
        if (e.type == EventType.ScrollWheel)
        {
            if (!_isCameraDragging)
            {
                BeginCameraDrag();
            }

            _cameraDistance *= (1f + e.delta.y * 0.05f);
            _cameraDistance = Mathf.Clamp(_cameraDistance, 0.1f, 10f);

            EndCameraDrag();

            e.Use();
            Repaint();
        }
    }

    // ================================================================
    // カメラドラッグのUndo
    // ================================================================

    private void BeginCameraDrag()
    {
        if (_isCameraDragging) return;

        _isCameraDragging = true;
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

            // Undo記録（WorkPlane連動版）
            _undoController.RecordViewChangeWithWorkPlane(
                _cameraStartRotX, _cameraStartRotY, _cameraStartDistance, _cameraStartTarget,
                _rotationX, _rotationY, _cameraDistance, _cameraTarget,
                oldWorkPlane, newWorkPlane);

            _undoController.SetEditorState(
                _rotationX, _rotationY, _cameraDistance, _cameraTarget,
                _showWireframe, _showVertices, _vertexEditMode);
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
                
                EditorGUI.DrawRect(splitterRect, splitterColor);
                
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
                    EditorGUI.DrawRect(new Rect(centerX - 1, y, 2, 2), gripColor);
                }
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