// Assets/Editor/UndoSystem/MeshEditor/MeshUndoController.cs
// SimpleMeshEditorに組み込むためのUndoコントローラー
// MeshObject（Vertex/Face）ベースに対応
// Phase: CommandQueue対応版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Model;
using static Poly_Ling.UndoSystem.KnifeCutOperationRecord;
using Poly_Ling.Selection;
using Poly_Ling.Commands;
using MeshEditor;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// メッシュエディタ用Undoコントローラー
    /// SimpleMeshEditorに組み込んで使用
    /// MeshObjectベースの新構造対応
    /// </summary>
    public partial class  MeshUndoController : IDisposable
    {
        // === Undoノード構造 ===
        private UndoGroup _mainGroup;
        private UndoStack<MeshUndoContext> _vertexEditStack;
        private UndoStack<EditorStateContext> _editorStateStack;
        private UndoStack<WorkPlaneContext> _workPlaneStack;
        private UndoStack<ModelContext> _meshListStack;
        private UndoGroup _subWindowGroup;

        // ====================================================================
        // プロジェクトレベルUndo（ファイル読み込み/新規作成/モデル操作用）
        // ====================================================================
        // 【現在: 方針B（全状態保存方式）】
        //   Stack<ProjectRecord> で操作前後のスナップショットを保存
        //
        // 【方針A移行時の変更】
        //   Stack<MeshEditor.ProjectRecord> → Stack<IProjectUndoRecord>
        //   PerformProjectUndo/Redo で record.Undo(ctx)/Redo(ctx) を呼び出し
        //
        // 詳細は ProjectUndoRecords.cs のファイル先頭コメントを参照
        // ====================================================================
        private Stack<MeshEditor.ProjectRecord> _projectUndoStack = new Stack<MeshEditor.ProjectRecord>();
        private Stack<MeshEditor.ProjectRecord> _projectRedoStack = new Stack<MeshEditor.ProjectRecord>();

        // === コンテキスト ===
        private MeshUndoContext _meshContext;
        private EditorStateContext _editorStateContext;
        private WorkPlaneContext _workPlane;
        private ModelContext _modelContext;

        // === 状態追跡（変更検出用） ===
        private Vector3[] _lastVertexPositions;
        private int _dragStartGroupId = -1;
        private bool _isDragging;

        // エディタ状態ドラッグ用
        private bool _isEditorStateDragging = false;
        private EditorStateSnapshot _editorStateStartSnapshot;

        // WorkPlaneドラッグ用
        private bool _isWorkPlaneDragging = false;
        private WorkPlaneSnapshot _workPlaneStartSnapshot;

        // === プロパティ ===
        public MeshUndoContext MeshUndoContext => _meshContext;
        public EditorStateContext EditorState => _editorStateContext;
        public WorkPlaneContext WorkPlane => _workPlane;
        public ModelContext ModelContext => _modelContext;
        
        /// <summary>後方互換: MeshListContext（ModelContextを返す）</summary>
        public ModelContext MeshListContext => _modelContext;

        /// <summary>MeshObjectへの直接アクセス</summary>
        public MeshObject MeshObject => _meshContext?.MeshObject;

        // 後方互換
        public ViewContext ViewContext => _editorStateContext as ViewContext ?? new ViewContext();

        public bool CanUndo => _mainGroup.CanUndo;
        public bool CanRedo => _mainGroup.CanRedo;

        public UndoGroup MainGroup => _mainGroup;
        public UndoStack<MeshUndoContext> VertexEditStack => _vertexEditStack;
        public UndoStack<EditorStateContext> EditorStateStack => _editorStateStack;
        public UndoStack<WorkPlaneContext> WorkPlaneStack => _workPlaneStack;
        public UndoStack<ModelContext> MeshListStack => _meshListStack;
        public UndoGroup SubWindowGroup => _subWindowGroup;

        // === プロジェクトレベルUndo プロパティ ===
        public bool CanUndoProject => _projectUndoStack.Count > 0;
        public bool CanRedoProject => _projectRedoStack.Count > 0;

        // === イベント ===
        public event Action OnUndoRedoPerformed;

        /// <summary>
        /// 直前にUndo/Redoが実行されたスタックの種別
        /// OnUndoRedoPerformedハンドラ内で参照可能
        /// </summary>
        public enum UndoStackType { VertexEdit, EditorState, WorkPlane, MeshList, Project, Unknown }
        public UndoStackType LastUndoRedoStackType { get; private set; } = UndoStackType.Unknown;
        
        /// <summary>
        /// プロジェクトUndo実行時のコールバック（復元処理用）
        /// </summary>
        public event Action<MeshEditor.ProjectRecord, bool> OnProjectUndoRedoPerformed;

        // === コンストラクタ ===
        public MeshUndoController(string windowId = "MainEditor")
        {
            // メイングループ作成
            _mainGroup = new UndoGroup(windowId, "UnityMesh Editor");
            _mainGroup.ResolutionPolicy = UndoResolutionPolicy.TimestampOnly;

            // 頂点編集スタック
            _meshContext = new MeshUndoContext();
            _vertexEditStack = new UndoStack<MeshUndoContext>(
                $"{windowId}/VertexEdit",
                "Vertex Edit",
                _meshContext
            );
            _mainGroup.AddChild(_vertexEditStack);

            // エディタ状態スタック（カメラ、表示、モード統合）
            _editorStateContext = new EditorStateContext();
            _editorStateStack = new UndoStack<EditorStateContext>(
                $"{windowId}/EditorState",
                "Editor State",
                _editorStateContext
            );
            _mainGroup.AddChild(_editorStateStack);

            // WorkPlaneスタック
            _workPlane = new WorkPlaneContext();
            _workPlaneStack = new UndoStack<WorkPlaneContext>(
                $"{windowId}/WorkPlaneContext",
                "Work Plane",
                _workPlane
            );
            _mainGroup.AddChild(_workPlaneStack);

            // MeshListスタック（ModelContextを使用）
            _modelContext = new ModelContext();
            _meshListStack = new UndoStack<ModelContext>(
                $"{windowId}/MeshContextList",
                "UnityMesh List",
                _modelContext
            );
            _mainGroup.AddChild(_meshListStack);

            // MeshContextにWorkPlane参照を設定（選択連動Undo用）
            _meshContext.WorkPlane = _workPlane;

            // EditorStateContextにWorkPlane参照を設定（カメラ連動Undo用）
            _editorStateContext.WorkPlane = _workPlane;

            // サブウインドウグループ
            _subWindowGroup = new UndoGroup($"{windowId}/SubWindows", "Sub Panels");
            _mainGroup.AddChild(_subWindowGroup);

            // イベント購読（スタック種別を記録してからInvoke）
            _vertexEditStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.VertexEdit; OnUndoRedoPerformed?.Invoke(); };
            _vertexEditStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.VertexEdit; OnUndoRedoPerformed?.Invoke(); };
            _editorStateStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.EditorState; OnUndoRedoPerformed?.Invoke(); };
            _editorStateStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.EditorState; OnUndoRedoPerformed?.Invoke(); };
            _workPlaneStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.WorkPlane; OnUndoRedoPerformed?.Invoke(); };
            _workPlaneStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.WorkPlane; OnUndoRedoPerformed?.Invoke(); };
            _meshListStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.MeshList; OnUndoRedoPerformed?.Invoke(); };
            _meshListStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.MeshList; OnUndoRedoPerformed?.Invoke(); };

            // グローバルマネージャーに登録（既存があれば削除してから追加）
            var existingChild = UndoManager.Instance.FindById(windowId);
            if (existingChild != null)
            {
                UndoManager.Instance.RemoveChild(existingChild);
            }
            UndoManager.Instance.AddChild(_mainGroup);
        }

        // === 初期化・クリーンアップ ===

        /// <summary>
        /// メッシュをコンテキストに読み込む
        /// </summary>
        public void LoadMesh(Mesh mesh, Vector3[] originalVertices = null)
        {
            _meshContext.LoadFromMesh(mesh, true);

            // 元の頂点位置を設定
            if (originalVertices != null)
            {
                _meshContext.OriginalPositions = originalVertices;
            }
            // LoadFromMesh内で既にOriginalPositionsは設定される

            _vertexEditStack.Clear();
        }

        /// <summary>
        /// MeshObjectを直接設定
        /// </summary>
        public void SetMeshObject(MeshObject meshObject, Mesh targetMesh = null)
        {
            _meshContext.MeshObject = meshObject;
            _meshContext.TargetMesh = targetMesh;
            _meshContext.OriginalPositions = (Vector3[])meshObject.Positions.Clone();
            _meshContext.SelectedVertices.Clear();
            _vertexEditStack.Clear();
        }

        /// <summary>
        /// エディタ状態を設定
        /// </summary>
        public void SetEditorState(
            float rotX, float rotY, float camDist, Vector3 camTarget,
            bool wireframe, bool showVerts, bool vertexEditMode = false)
        {
            _editorStateContext.RotationX = rotX;
            _editorStateContext.RotationY = rotY;
            _editorStateContext.CameraDistance = camDist;
            _editorStateContext.CameraTarget = camTarget;
            _editorStateContext.ShowWireframe = wireframe;
            _editorStateContext.ShowVertices = showVerts;
            _editorStateContext.VertexEditMode = vertexEditMode;
        }

        /// <summary>
        /// 表示設定をコンテキストに設定（後方互換）
        /// </summary>
        public void SetViewContext(
            float rotX, float rotY, float camDist, Vector3 camTarget,
            bool wireframe, bool showVerts)
        {
            SetEditorState(rotX, rotY, camDist, camTarget, wireframe, showVerts, _editorStateContext.VertexEditMode);
        }

        public void Dispose()
        {
            UndoManager.Instance.RemoveChild(_mainGroup);
        }

        // === フォーカス管理 ===

        /// <summary>
        /// 頂点編集モードにフォーカス
        /// </summary>
        public void FocusVertexEdit()
        {
            _mainGroup.FocusedChildId = _vertexEditStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// エディタ状態（カメラ/表示/モード）にフォーカス
        /// </summary>
        public void FocusEditorState()
        {
            _mainGroup.FocusedChildId = _editorStateStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// 表示モードにフォーカス（後方互換）
        /// </summary>
        public void FocusView()
        {
            FocusEditorState();
        }

        // === エディタ状態の記録（シンプル化） ===

        /// <summary>
        /// エディタ状態ドラッグ開始
        /// </summary>
        public void BeginEditorStateDrag()
        {
            if (_isEditorStateDragging) return;
            _isEditorStateDragging = true;
            _editorStateStartSnapshot = _editorStateContext.Capture();
        }

        /// <summary>
        /// エディタ状態ドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndEditorStateDrag(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging) return;
            _isEditorStateDragging = false;

            EditorStateSnapshot currentSnapshot = _editorStateContext.Capture();
            if (currentSnapshot.IsDifferentFrom(_editorStateStartSnapshot))
            {
                RecordEditorStateChangeInternal(_editorStateStartSnapshot, currentSnapshot, description);
            }
        }

        /// <summary>
        /// エディタ状態変更を記録（内部用・キューから呼ばれる）
        /// </summary>
        internal void RecordEditorStateChangeInternal(
            EditorStateSnapshot before,
            EditorStateSnapshot after,
            string description)
        {
            // Debug.Log($"[RecordEditorStateChangeInternal] Recording to EditorStateStack: {description}. Before Focus={_mainGroup.FocusedChildId}");
            
            _editorStateStack.EndGroup();  // 独立した操作として記録
            EditorStateChangeRecord record = new EditorStateChangeRecord(before, after);
            _editorStateStack.Record(record, description);
            FocusEditorState();
            
            // Debug.Log($"[RecordEditorStateChangeInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// エディタ状態を即座に記録（チェックボックス等）
        /// </summary>
        public void RecordEditorStateChange(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging)
            {
                // ドラッグ中でなければ、前回の状態から記録
                BeginEditorStateDrag();
            }
            EndEditorStateDrag(description);
        }

        // === WorkPlaneの記録 ===

        /// <summary>
        /// WorkPlaneにフォーカス
        /// </summary>
        public void FocusWorkPlane()
        {
            _mainGroup.FocusedChildId = _workPlaneStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// WorkPlaneドラッグ開始
        /// </summary>
        public void BeginWorkPlaneDrag()
        {
            if (_isWorkPlaneDragging) return;
            _isWorkPlaneDragging = true;
            _workPlaneStartSnapshot = _workPlane.CreateSnapshot();
        }

        /// <summary>
        /// WorkPlaneドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndWorkPlaneDrag(string description = "Change WorkPlaneContext")
        {
            if (!_isWorkPlaneDragging) return;
            _isWorkPlaneDragging = false;

            WorkPlaneSnapshot currentSnapshot = _workPlane.CreateSnapshot();
            if (currentSnapshot.IsDifferentFrom(_workPlaneStartSnapshot))
            {
                WorkPlaneChangeRecord record = new WorkPlaneChangeRecord(_workPlaneStartSnapshot, currentSnapshot, description);
                _workPlaneStack.Record(record, description);
                FocusWorkPlane();
            }
        }

        /// <summary>
        /// WorkPlane変更を即座に記録
        /// </summary>
        public void RecordWorkPlaneChange(string description = "Change WorkPlaneContext")
        {
            if (!_isWorkPlaneDragging)
            {
                BeginWorkPlaneDrag();
            }
            EndWorkPlaneDrag(description);
        }

        /// <summary>
        /// WorkPlane変更を記録（スナップショット指定）
        /// </summary>
        public void RecordWorkPlaneChange(WorkPlaneSnapshot before, WorkPlaneSnapshot after, string description = null)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new WorkPlaneChangeRecord(before, after, description);
            _workPlaneStack.Record(record, record.Description);
            FocusWorkPlane();
        }

        // === 頂点編集操作の記録 ===

        /// <summary>
        /// 頂点ドラッグ開始
        /// </summary>
        public void BeginVertexDrag(Vector3[] currentPositions)
        {
            BeginVertexDragInternal(currentPositions);
        }

        /// <summary>
        /// 頂点ドラッグ開始（内部用）
        /// </summary>
        internal void BeginVertexDragInternal(Vector3[] currentPositions)
        {
            if (_isDragging) return;

            // Debug.Log($"[BeginVertexDragInternal] Starting vertex drag. Focus={_mainGroup.FocusedChildId}");

            _isDragging = true;
            _lastVertexPositions = (Vector3[])currentPositions.Clone();
            _dragStartGroupId = _vertexEditStack.BeginGroup("Move Vertices");
            FocusVertexEdit();
            
            // Debug.Log($"[BeginVertexDragInternal] After FocusVertexEdit(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// 頂点ドラッグ開始（MeshObjectから自動取得）
        /// </summary>
        public void BeginVertexDrag()
        {
            BeginVertexDragInternal();
        }

        /// <summary>
        /// 頂点ドラッグ開始（MeshObjectから自動取得・内部用）
        /// </summary>
        internal void BeginVertexDragInternal()
        {
            if (_isDragging) return;
            if (_meshContext?.MeshObject == null) return;

            BeginVertexDragInternal(_meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点ドラッグ終了（キュー経由）
        /// </summary>
        public void EndVertexDrag(int[] movedIndices, Vector3[] newPositions)
        {
            EndVertexDragInternal(movedIndices, newPositions);
        }

        /// <summary>
        /// 頂点ドラッグ終了（内部用）
        /// </summary>
        internal void EndVertexDragInternal(int[] movedIndices, Vector3[] newPositions)
        {
            if (!_isDragging) return;

            // Debug.Log($"[EndVertexDragInternal] Ending vertex drag. movedCount={movedIndices?.Length ?? 0}. Focus={_mainGroup.FocusedChildId}");

            _isDragging = false;

            if (movedIndices != null && movedIndices.Length > 0)
            {
                // 移動した頂点の記録を作成
                var oldPositions = new Vector3[movedIndices.Length];
                var newPos = new Vector3[movedIndices.Length];

                for (int i = 0; i < movedIndices.Length; i++)
                {
                    int idx = movedIndices[i];
                    oldPositions[i] = _lastVertexPositions[idx];
                    newPos[i] = newPositions[idx];
                }

                var record = new VertexMoveRecord(movedIndices, oldPositions, newPos);
                _vertexEditStack.Record(record, "Move Vertices");
                FocusVertexEdit();
                
                // Debug.Log($"[EndVertexDragInternal] Recorded vertex move. Focus={_mainGroup.FocusedChildId}");
            }

            _vertexEditStack.EndGroup();
            _lastVertexPositions = null;
        }

        /// <summary>
        /// 頂点ドラッグ終了（MeshObjectから自動取得）
        /// </summary>
        public void EndVertexDrag(int[] movedIndices)
        {
            EndVertexDragInternal(movedIndices);
        }

        /// <summary>
        /// 頂点ドラッグ終了（MeshObjectから自動取得・内部用）
        /// </summary>
        internal void EndVertexDragInternal(int[] movedIndices)
        {
            if (_meshContext?.MeshObject == null) return;
            EndVertexDragInternal(movedIndices, _meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点グループ移動を記録
        /// </summary>
        public void RecordVertexGroupMove(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            RecordVertexGroupMoveInternal(groups, oldOffsets, newOffsets, originalVertices);
        }

        /// <summary>
        /// 頂点グループ移動を記録（内部用）
        /// </summary>
        internal void RecordVertexGroupMoveInternal(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            var record = new VertexGroupMoveRecord(
                groups,
                (Vector3[])oldOffsets.Clone(),
                (Vector3[])newOffsets.Clone(),
                (Vector3[])originalVertices.Clone()
            );
            _vertexEditStack.Record(record, "Move Vertex Group");
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: Vertex/Face HashSet版）
        /// </summary>
        public void RecordSelectionChange(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            RecordSelectionChangeInternal(oldVertices, newVertices, oldFaces, newFaces);
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: 内部用）
        /// </summary>
        internal void RecordSelectionChangeInternal(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(oldVertices, newVertices, oldFaces, newFaces);
            _vertexEditStack.Record(record, "Change Selection");
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: WorkPlane連動）
        /// </summary>
        public void RecordSelectionChangeWithWorkPlane(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            RecordSelectionChangeWithWorkPlaneInternal(
                oldVertices, newVertices, oldWorkPlane, newWorkPlane, oldFaces, newFaces);
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: WorkPlane連動・内部用）
        /// </summary>
        internal void RecordSelectionChangeWithWorkPlaneInternal(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(
                oldVertices, newVertices,
                oldWorkPlane, newWorkPlane,
                oldFaces, newFaces);
            _vertexEditStack.Record(record, "Change Selection");
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（SelectionSnapshot版 — 推奨）
        /// </summary>
        public void RecordSelectionChange(
            Poly_Ling.Selection.SelectionSnapshot oldSnapshot,
            Poly_Ling.Selection.SelectionSnapshot newSnapshot,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            RecordSelectionChangeInternal(oldSnapshot, newSnapshot, oldWorkPlane, newWorkPlane);
        }

        /// <summary>
        /// 選択状態変更を記録（SelectionSnapshot版・内部用）
        /// </summary>
        internal void RecordSelectionChangeInternal(
            Poly_Ling.Selection.SelectionSnapshot oldSnapshot,
            Poly_Ling.Selection.SelectionSnapshot newSnapshot,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(oldSnapshot, newSnapshot, oldWorkPlane, newWorkPlane);
            string desc = newSnapshot?.Mode.ToString() ?? "Selection";
            _vertexEditStack.Record(record, $"Change {desc} Selection");
            FocusVertexEdit();
        }
        // === スナップショット（トポロジー変更用） ===

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（新形式・後方互換）
        /// 
        /// 【注意】この版ではEdge/Line選択は保存されない
        /// Edge/Line選択も保存したい場合はselectionState付きの版を使用
        /// </summary>
        public MeshObjectSnapshot CaptureMeshObjectSnapshot()
        {
            return MeshObjectSnapshot.Capture(_meshContext);
        }

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（拡張選択対応版オーバーロード）
        /// 
        /// </summary>
        /// <param name="selectionState">
        /// 拡張選択状態（Edge/Line含む）。
        /// 
        /// 【重要】トポロジー変更ツールは必ずこれを渡すこと！
        /// これにより、ベベルや押し出しのUndoで元の選択も復元される。
        /// </param>
        /// <returns>スナップショット</returns>
        public MeshObjectSnapshot CaptureMeshObjectSnapshot(SelectionState selectionState)
        {
            return MeshObjectSnapshot.Capture(_meshContext, selectionState);
        }

        /// <summary>
        /// トポロジー変更を記録（新形式・後方互換）
        /// 
        /// 【注意】この版ではEdge/Line選択はUndo時に復元されない
        /// Edge/Line選択も復元したい場合はselectionState付きの版を使用
        /// </summary>
        public void RecordTopologyChange(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            string description = "Topology Change")
        {
            RecordTopologyChangeInternal(before, after, description);
        }

        /// <summary>
        /// トポロジー変更を記録（内部用）
        /// </summary>
        internal void RecordTopologyChangeInternal(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }


        /// <summary>
        /// トポロジー変更を記録（拡張選択対応版）
        /// 
        /// 【フェーズ1追加】
        /// </summary>
        /// <param name="before">変更前スナップショット（Capture()で取得）</param>
        /// <param name="after">変更後スナップショット（Capture()で取得）</param>
        /// <param name="selectionState">
        /// 拡張選択状態への参照。
        /// 
        /// 【重要】
        /// Edge/Line選択のUndo/Redoに必要。
        /// トポロジー変更ツール（ベベル、押し出し等）は必ずこれを渡すこと！
        /// 
        /// これにより：
        /// - ベベルUndo時 → メッシュが戻り、元のEdge選択も復元
        /// - 押し出しUndo時 → メッシュが戻り、元のFace選択も復元
        /// 
        /// nullの場合は従来動作（Edge/Line選択は復元されない）
        /// </param>
        /// <param name="description">Undo履歴に表示される説明</param>
        public void RecordTopologyChange(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState,
            string description = "Topology Change")
        {
            RecordTopologyChangeInternal(before, after, selectionState, description);
        }

        /// <summary>
        /// トポロジー変更を記録（拡張選択対応版・内部用）
        /// </summary>
        internal void RecordTopologyChangeInternal(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState,
            string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            // selectionStateを渡すことでEdge/Line選択もUndo対象に
            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after, selectionState);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }
 



        /// <summary>
        /// トポロジー変更を記録（後方互換）
        /// </summary>
        public void RecordTopologyChange(MeshSnapshot before, MeshSnapshot after, string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            // 旧形式のスナップショットを新形式に変換して記録
            MeshObjectSnapshot beforeData = new MeshObjectSnapshot();
            before.ApplyTo(_meshContext);
            beforeData = MeshObjectSnapshot.Capture(_meshContext);

            after.ApplyTo(_meshContext);
            MeshObjectSnapshot afterData = MeshObjectSnapshot.Capture(_meshContext);

            MeshSnapshotRecord record = new MeshSnapshotRecord(beforeData, afterData);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }

        // === 面操作の記録（新機能） ===

        /// <summary>
        /// 面追加を記録
        /// </summary>
        public void RecordFaceAdd(Face face, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new FaceAddRecord(face, index);
            _vertexEditStack.Record(record, "Add Face");
            FocusVertexEdit();
        }

        /// <summary>
        /// 面削除を記録
        /// </summary>
        public void RecordFaceDelete(Face face, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new FaceDeleteRecord(face, index);
            _vertexEditStack.Record(record, "Delete Face");
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点追加を記録
        /// </summary>
        public void RecordVertexAdd(Vertex vertex, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new VertexAddRecord(vertex, index);
            _vertexEditStack.Record(record, "Add Vertex");
            FocusVertexEdit();
        }

        /// <summary>
        /// 面追加操作を記録（頂点と面をまとめて1つの操作として）
        /// </summary>
        public void RecordAddFaceOperation(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            
            var record = new AddFaceOperationRecord(face, faceIndex, addedVertices);
            string desc;
            if (face != null)
            {
                desc = addedVertices.Count > 0 
                    ? $"Add Face (+{addedVertices.Count} vertices)" 
                    : "Add Face";
            }
            else
            {
                desc = $"Add {addedVertices.Count} Vertices";
            }
            _vertexEditStack.Record(record, desc);
            FocusVertexEdit();
        }

        /// <summary>
        /// ナイフ切断操作を記録
        /// </summary>
        public void RecordKnifeCut(
            int originalFaceIndex,
            Face originalFace,
            Face newFace1,
            int newFace2Index,
            Face newFace2,
            List<(int Index, Vertex Vertex)> addedVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            
            var record = new KnifeCutOperationRecord(
                originalFaceIndex,
                originalFace,
                newFace1,
                newFace2Index,
                newFace2,
                addedVertices
            );
            _vertexEditStack.Record(record, "Knife Cut");
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点削除操作を記録（スナップショット方式）
        /// </summary>
        public void RecordDeleteVertices(MeshObjectSnapshot before, MeshObjectSnapshot after)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            _vertexEditStack.Record(record, "Delete Vertices");
            FocusVertexEdit();
        }

        /// <summary>
        /// メッシュトポロジー変更を記録（スナップショット方式）
        /// ナイフツールの複数面切断、面マージなど汎用的に使用
        /// </summary>
        public void RecordMeshTopologyChange(MeshObjectSnapshot before, MeshObjectSnapshot after, string description = "UnityMesh Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }
        /// <summary>
        /// トポロジー変更前にスナップショットを取得（後方互換）
        /// 現在のMeshObjectのスナップショットを取得
        /// </summary>
        public MeshSnapshot CaptureSnapshot()
        {
            return MeshSnapshot.Capture(_meshContext);
        }

        // === 表示操作の記録 ===

        /// <summary>
        /// カメラ変更を記録
        /// </summary>
        public void RecordViewChange(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget)
        {
            RecordViewChangeInternal(oldRotX, oldRotY, oldDist, oldTarget,
                newRotX, newRotY, newDist, newTarget);
        }

        /// <summary>
        /// カメラ変更を記録（内部用・キューから呼ばれる）
        /// Capture()を使用して完全なスナップショットを取得し、カメラ値のみ上書き
        /// </summary>
        internal void RecordViewChangeInternal(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget)
        {
            //Debug.Log($"[RecordViewChangeInternal] Recording to EditorStateStack. Before Focus={_mainGroup.FocusedChildId}");
            
            // 完全なスナップショットを取得してカメラ値のみ上書き
            EditorStateSnapshot before = _editorStateContext.Capture();
            before.RotationX = oldRotX;
            before.RotationY = oldRotY;
            before.CameraDistance = oldDist;
            before.CameraTarget = oldTarget;

            EditorStateSnapshot after = _editorStateContext.Capture();
            after.RotationX = newRotX;
            after.RotationY = newRotY;
            after.CameraDistance = newDist;
            after.CameraTarget = newTarget;

            var record = new EditorStateChangeRecord(before, after);
            _editorStateStack.Record(record, "Change View");
            FocusEditorState();
            
            //Debug.Log($"[RecordViewChangeInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// カメラ変更を記録（WorkPlane連動）
        /// </summary>
        public void RecordViewChangeWithWorkPlane(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
        {
            RecordViewChangeWithWorkPlaneInternal(
                oldRotX, oldRotY, oldDist, oldTarget,
                newRotX, newRotY, newDist, newTarget,
                oldWorkPlane, newWorkPlane);
        }

        /// <summary>
        /// カメラ変更を記録（WorkPlane連動・内部用）
        /// CameraParallelモードでカメラ姿勢に連動してWorkPlane軸もUndo/Redoされる
        /// Capture()を使用して完全なスナップショットを取得し、カメラ値のみ上書き
        /// </summary>
        internal void RecordViewChangeWithWorkPlaneInternal(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
        {
            //Debug.Log($"[RecordViewChangeWithWorkPlaneInternal] Recording to EditorStateStack. Before Focus={_mainGroup.FocusedChildId}");
            
            // 完全なスナップショットを取得してカメラ値のみ上書き
            EditorStateSnapshot before = _editorStateContext.Capture();
            before.RotationX = oldRotX;
            before.RotationY = oldRotY;
            before.CameraDistance = oldDist;
            before.CameraTarget = oldTarget;

            EditorStateSnapshot after = _editorStateContext.Capture();
            after.RotationX = newRotX;
            after.RotationY = newRotY;
            after.CameraDistance = newDist;
            after.CameraTarget = newTarget;

            var record = new EditorStateChangeRecord(before, after, oldWorkPlane, newWorkPlane);
            _editorStateStack.Record(record, "Change View");
            FocusEditorState();
            
            //Debug.Log($"[RecordViewChangeWithWorkPlaneInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }

        // === サブウインドウ管理 ===

        /// <summary>
        /// サブウインドウ用のスタックを作成
        /// </summary>
        public UndoStack<TContext> CreateSubWindowStack<TContext>(
            string id,
            string displayName,
            TContext context)
        {
            var stack = new UndoStack<TContext>(id, displayName, context);
            _subWindowGroup.AddChild(stack);
            return stack;
        }

        /// <summary>
        /// サブウインドウ用のスタックを削除
        /// </summary>
        public bool RemoveSubWindowStack(string id)
        {
            var node = _subWindowGroup.FindById(id);
            return node != null && _subWindowGroup.RemoveChild(node);
        }

        /// <summary>
        /// サブウインドウにフォーカス
        /// </summary>
        public void FocusSubWindow(string id)
        {
            _mainGroup.FocusedChildId = _subWindowGroup.Id;
            _subWindowGroup.FocusedChildId = id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        // === Undo/Redo実行 ===

        // コマンドキュー（外部から設定）
        private CommandQueue _commandQueue;
        
        /// <summary>
        /// コマンドキューを設定
        /// </summary>
        public void SetCommandQueue(CommandQueue queue)
        {
            _commandQueue = queue;
        }

        /// <summary>
        /// Undo実行
        /// </summary>
        public bool Undo()
        {
            return UndoInternal();
        }

        /// <summary>
        /// Redo実行
        /// </summary>
        public bool Redo()
        {
            return RedoInternal();
        }

        /// <summary>
        /// Undo実行（内部用・キューから呼ばれる）
        /// </summary>
        internal bool UndoInternal()
        {
            //Debug.Log($"[MeshUndoController.UndoInternal] Before. MainGroup.FocusedChildId={_mainGroup.FocusedChildId}, MeshListStack.Id={_meshListStack.Id}");
            bool result = _mainGroup.PerformUndo();
            //Debug.Log($"[MeshUndoController.UndoInternal] After. MainGroup.FocusedChildId={_mainGroup.FocusedChildId}, Result={result}");
            return result;
        }

        /// <summary>
        /// Redo実行（内部用・キューから呼ばれる）
        /// </summary>
        internal bool RedoInternal()
        {
            //Debug.Log($"[MeshUndoController.RedoInternal] Called. MeshListStack RedoCount={_meshListStack.RedoCount}");
            //Debug.Log($"[MeshUndoController.RedoInternal] MainGroup.FocusedChildId={_mainGroup.FocusedChildId}, MeshListStack.Id={_meshListStack.Id}");
            //Debug.Log($"[MeshUndoController.RedoInternal] VertexEditStack.RedoCount={VertexEditStack.RedoCount}");
            //Debug.Log($"[MeshUndoController.RedoInternal] EditorStateStack.RedoCount={EditorStateStack.RedoCount}");
            bool result = _mainGroup.PerformRedo();
            //Debug.Log($"[MeshUndoController.RedoInternal] Result={result}");
            return result;
        }

        /// <summary>
        /// キーボードショートカット処理
        /// </summary>
        public bool HandleKeyboardShortcuts(Event e)
        {
            if (e.type != EventType.KeyDown)
                return false;

            bool ctrl = e.control || e.command;
            
            // Debug.Log($"[MeshUndoController.HandleKeyboardShortcuts] KeyDown detected: keyCode={e.keyCode}, ctrl={ctrl}, shift={e.shift}");

            if (ctrl && e.keyCode == KeyCode.Z && !e.shift)
            {
                //Debug.Log($"[MeshUndoController.HandleKeyboardShortcuts] Ctrl+Z detected, enqueuing UndoCommand");
                if (_commandQueue != null && CanUndo)
                {
                    _commandQueue.Enqueue(new UndoCommand(this, null));
                    e.Use();
                    return true;
                }
            }

            if ((ctrl && e.keyCode == KeyCode.Y) ||
                (ctrl && e.shift && e.keyCode == KeyCode.Z))
            {
                //Debug.Log($"[MeshUndoController.HandleKeyboardShortcuts] Ctrl+Y or Ctrl+Shift+Z detected, enqueuing RedoCommand");
                if (_commandQueue != null && CanRedo)
                {
                    _commandQueue.Enqueue(new RedoCommand(this, null));
                    e.Use();
                    return true;
                }
            }

            return false;
        }

        // === デバッグ ===

        public string GetDebugInfo()
        {
            var meshInfo = _meshContext?.MeshObject?.GetDebugInfo() ?? "No MeshObject";
            return $"[MeshFactoryUndo]\n" +
                   $"  MeshObject: {meshInfo}\n" +
                   $"  Vertex: {_vertexEditStack.GetDebugInfo()}\n" +
                   $"  EditorState: {_editorStateStack.GetDebugInfo()}\n" +
                   $"  WorkPlaneContext: {_workPlaneStack.GetDebugInfo()}\n" +
                   $"  MeshContextList: {_meshListStack.GetDebugInfo()}\n" +
                   $"  SubWindows: {_subWindowGroup.Children.Count}";
        }

        // ================================================================
        // MeshList操作
        // ================================================================

        /// <summary>
        /// ModelContextを設定（_modelと同一インスタンスを共有）
        /// OnListChangedコールバックは呼び出し側で直接 += すること
        /// </summary>
        /// <param name="modelContext">モデルコンテキストへの参照（_modelと同一）</param>
        public void SetModelContext(ModelContext modelContext)
        {
            _modelContext = modelContext;
            _meshListStack.Context = _modelContext;
        }

        /// <summary>
        /// ModelContextを設定し、OnListChangedコールバックも登録する
        /// モデル切り替え時に使用
        /// </summary>
        public void SetModelContext(ModelContext modelContext, Action onListChanged)
        {
            // 旧コンテキストからコールバック解除
            if (_modelContext != null && onListChanged != null)
            {
                _modelContext.OnListChanged -= onListChanged;
            }

            SetModelContext(modelContext);

            // 新コンテキストにコールバック登録
            if (_modelContext != null && onListChanged != null)
            {
                _modelContext.OnListChanged += onListChanged;
            }
        }

        /// <summary>
        /// MeshListにフォーカス
        /// </summary>
        public void FocusMeshList()
        {
            _mainGroup.FocusedChildId = _meshListStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// メッシュコンテキスト追加を記録
        /// </summary>
        public void RecordMeshContextAdd(
            MeshContext meshContext, 
            int insertIndex, 
            List<int> oldSelectedIndices, 
            List<int> newSelectedIndices,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null)
        {
            var record = new MeshListChangeRecord
            {
                AddedMeshContexts = new List<(int, MeshContextSnapshot)>
                {
                    (insertIndex, MeshContextSnapshot.Capture(meshContext))
                },
                OldSelectedIndices = oldSelectedIndices ?? new List<int>(),
                NewSelectedIndices = newSelectedIndices ?? new List<int>(),
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };

            _meshListStack.Record(record, $"Add UnityMesh: {meshContext.Name}");
            FocusMeshList();
        }

        /// <summary>
        /// メッシュコンテキスト複数追加を記録（バッチ）
        /// </summary>
        public void RecordMeshContextsAdd(
            List<(int Index, MeshContext MeshContext)> addedContexts,
            List<int> oldSelectedIndices,
            List<int> newSelectedIndices,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null,
            List<Material> oldMaterials = null,
            int oldMaterialIndex = 0)
        {
            var record = new MeshListChangeRecord
            {
                AddedMeshContexts = addedContexts
                    .Select(e => (e.Index, MeshContextSnapshot.Capture(e.MeshContext)))
                    .ToList(),
                OldSelectedIndices = oldSelectedIndices ?? new List<int>(),
                NewSelectedIndices = newSelectedIndices ?? new List<int>(),
                OldCameraState = oldCamera,
                NewCameraState = newCamera,
                OldMaterials = oldMaterials != null ? new List<Material>(oldMaterials) : null,
                OldCurrentMaterialIndex = oldMaterialIndex
            };

            string desc = addedContexts.Count == 1
                ? $"Add Mesh: {addedContexts[0].MeshContext.Name}"
                : $"Add {addedContexts.Count} Meshes";

            _meshListStack.Record(record, desc);
            _lastMeshListRecord = record;
            FocusMeshList();
        }

        /// <summary>
        /// 最後に記録した MeshListChangeRecord への参照（Materials 更新用）
        /// </summary>
        private MeshListChangeRecord _lastMeshListRecord;

        /// <summary>
        /// 最後のレコードに NewMaterials を設定
        /// AddMaterialsToModel から呼び出される
        /// </summary>
        public void UpdateLastRecordMaterials(List<Material> newMaterials, int newMaterialIndex)
        {
            if (_lastMeshListRecord != null)
            {
                _lastMeshListRecord.NewMaterials = newMaterials != null ? new List<Material>(newMaterials) : null;
                _lastMeshListRecord.NewCurrentMaterialIndex = newMaterialIndex;
            }
        }

        /// <summary>
        /// MeshListChangeRecord を記録（Materials 対応版）
        /// ReplaceAllMeshContextsWithUndo 等から使用
        /// </summary>
        public void RecordMeshListChange(MeshListChangeRecord record, string description, List<Material> oldMaterials = null, int oldMaterialIndex = 0)
        {
            record.OldMaterials = oldMaterials != null ? new List<Material>(oldMaterials) : null;
            record.OldCurrentMaterialIndex = oldMaterialIndex;
            _meshListStack.Record(record, description);
            _lastMeshListRecord = record;
            FocusMeshList();
        }

        /// <summary>
        /// メッシュコンテキスト削除を記録（複数対応）
        /// </summary>
        public void RecordMeshContextsRemove(
            List<(int Index, MeshContext meshContext)> removedContexts, 
            List<int> oldSelectedIndices, 
            List<int> newSelectedIndices,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedContexts
                    .Select(e => (e.Index, MeshContextSnapshot.Capture(e.meshContext)))
                    .ToList(),
                OldSelectedIndices = oldSelectedIndices ?? new List<int>(),
                NewSelectedIndices = newSelectedIndices ?? new List<int>(),
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };

            string desc = removedContexts.Count == 1 
                ? $"Remove UnityMesh: {removedContexts[0].meshContext.Name}"
                : $"Remove {removedContexts.Count} Meshes";

            _meshListStack.Record(record, desc);
            FocusMeshList();
        }

        /// <summary>
        /// メッシュコンテキスト順序変更を記録
        /// </summary>
        /// <param name="meshContext">移動したメッシュコンテキスト</param>
        /// <param name="oldIndex">移動前のインデックス</param>
        /// <param name="newIndex">移動後のインデックス</param>
        /// <param name="oldSelectedIndex">移動前の選択インデックス</param>
        /// <param name="newSelectedIndex">移動後の選択インデックス</param>
        /// <param name="oldCamera">移動前のカメラ状態（オプション）</param>
        /// <param name="newCamera">移動後のカメラ状態（オプション）</param>
        public void RecordMeshContextReorder(
            MeshContext meshContext, 
            int oldIndex, 
            int newIndex, 
            List<int> oldSelectedIndices, 
            List<int> newSelectedIndices,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(meshContext);

            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = new List<(int, MeshContextSnapshot)> { (oldIndex, snapshot) },
                AddedMeshContexts = new List<(int, MeshContextSnapshot)> { (newIndex, snapshot) },
                OldSelectedIndices = oldSelectedIndices ?? new List<int>(),
                NewSelectedIndices = newSelectedIndices ?? new List<int>(),
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };

            _meshListStack.Record(record, $"Reorder UnityMesh: {meshContext.Name}");
            FocusMeshList();
        }

        /// <summary>
        /// メッシュ選択変更を記録
        /// </summary>
        public void RecordMeshSelectionChange(List<int> oldIndices, List<int> newIndices)
        {
            if (oldIndices != null && newIndices != null && oldIndices.SequenceEqual(newIndices)) return;
            RecordMeshSelectionChangeInternal(oldIndices, newIndices);
        }

        /// <summary>
        /// メッシュ選択変更を記録（内部用）
        /// </summary>
        internal void RecordMeshSelectionChangeInternal(List<int> oldIndices, List<int> newIndices)
        {
            var record = new MeshSelectionChangeRecord(
                oldIndices ?? new List<int>(), 
                newIndices ?? new List<int>());
            _meshListStack.Record(record, "Select Mesh");
            FocusMeshList();
        }

        /// <summary>
        /// メッシュ選択変更を記録（カメラ状態付き）
        /// </summary>
        public void RecordMeshSelectionChange(
            List<int> oldIndices, 
            List<int> newIndices,
            CameraSnapshot? oldCamera,
            CameraSnapshot? newCamera)
        {
            if (oldIndices != null && newIndices != null && oldIndices.SequenceEqual(newIndices)) return;
            RecordMeshSelectionChangeInternal(oldIndices, newIndices, oldCamera, newCamera);
        }

        /// <summary>
        /// メッシュ選択変更を記録（カメラ状態付き・内部用）
        /// </summary>
        internal void RecordMeshSelectionChangeInternal(
            List<int> oldIndices, 
            List<int> newIndices,
            CameraSnapshot? oldCamera,
            CameraSnapshot? newCamera)
        {
            var record = new MeshSelectionChangeRecord(
                oldIndices ?? new List<int>(), 
                newIndices ?? new List<int>(), 
                oldCamera, newCamera);
            _meshListStack.Record(record, "Select Mesh");
            FocusMeshList();
        }

        // ================================================================
        // プロジェクトレベルUndo操作
        // ================================================================

        /// <summary>
        /// プロジェクト操作を記録（ファイル読み込み/新規作成用）
        /// </summary>
        /// <param name="record">プロジェクト操作の記録</param>
        public void RecordProjectOperation(MeshEditor.ProjectRecord record)
        {
            if (record == null) return;

            _projectUndoStack.Push(record);
            _projectRedoStack.Clear();  // 新しい操作でRedoスタックをクリア
        }

        /// <summary>
        /// プロジェクトレベルのUndoを実行
        /// </summary>
        /// <returns>Undo実行されたProjectRecord（復元用）、実行できない場合はnull</returns>
        public MeshEditor.ProjectRecord UndoProject()
        {
            if (_projectUndoStack.Count == 0) return null;

            var record = _projectUndoStack.Pop();
            _projectRedoStack.Push(record);
            return record;
        }

        /// <summary>
        /// プロジェクトレベルのUndoを実行（コールバック呼び出し付き）
        /// </summary>
        /// <returns>Undo成功したか</returns>
        public bool PerformProjectUndo()
        {
            var record = UndoProject();
            if (record == null) return false;

            // コールバックを呼び出し（isRedo = false）
            OnProjectUndoRedoPerformed?.Invoke(record, false);
            return true;
        }

        /// <summary>
        /// プロジェクトレベルのRedoを実行
        /// </summary>
        /// <returns>Redo実行されたProjectRecord（復元用）、実行できない場合はnull</returns>
        public MeshEditor.ProjectRecord RedoProject()
        {
            if (_projectRedoStack.Count == 0) return null;

            var record = _projectRedoStack.Pop();
            _projectUndoStack.Push(record);
            return record;
        }

        /// <summary>
        /// プロジェクトレベルのRedoを実行（コールバック呼び出し付き）
        /// </summary>
        /// <returns>Redo成功したか</returns>
        public bool PerformProjectRedo()
        {
            var record = RedoProject();
            if (record == null) return false;

            // コールバックを呼び出し（isRedo = true）
            OnProjectUndoRedoPerformed?.Invoke(record, true);
            return true;
        }

        /// <summary>
        /// プロジェクトスタックをクリア
        /// </summary>
        public void ClearProjectStack()
        {
            _projectUndoStack.Clear();
            _projectRedoStack.Clear();
        }

        /// <summary>
        /// プロジェクトスタックの状態を取得
        /// </summary>
        public string GetProjectStackDebugInfo()
        {
            return $"ProjectUndo: {_projectUndoStack.Count}, ProjectRedo: {_projectRedoStack.Count}";
        }


    }
}