// Assets/Editor/UndoSystem/MeshEditor/MeshUndoController.cs
// SimpleMeshEditorに組み込むためのUndoコントローラー
// MeshObject（Vertex/Face）ベースに対応
// Phase: CommandQueue対応版
//
// 【分割先】このファイルから次へ分けてある。
//   MeshUndoController_MeshList.cs  Undo コントローラ：MeshList 操作とプロジェクトレベル Undo 操作の記録。
//   MeshUndoController_Topology.cs  Undo コントローラ：スナップショット・トポロジー変更・面／頂点の追加削除・ビュー変更の記録。
//   MeshUndoController_Vertex.cs    Undo コントローラ：フォーカス切替・エディタ状態・作業平面・頂点ドラッグ・選択変更の記録。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using static Poly_Ling.UndoSystem.KnifeCutOperationRecord;
using Poly_Ling.Selection;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

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
        private readonly UndoManager _undoManager;
        private UndoGroup _mainGroup;
        private UndoStack<MeshUndoContext> _vertexEditStack;
        private UndoStack<EditorStateContext> _editorStateStack;
        private UndoStack<WorkPlaneContext> _workPlaneStack;
        private UndoStack<ModelContext> _meshListStack;
        // Player: 問題 A/B 対応。モデル切替などプロジェクトレベル操作を
        // UndoGroup 下で OperationLog に乗せるための独立スタック。
        private UndoStack<ProjectContext> _projectStack;
        private UndoGroup _subWindowGroup;

        // ====================================================================
        // プロジェクトレベルUndo（ファイル読み込み/新規作成/モデル操作用）
        // ====================================================================
        // 【現在: 方針B（全状態保存方式）】
        //   Stack<ProjectRecord> で操作前後のスナップショットを保存
        //
        // 【方針A移行時の変更】
        //   Stack<Poly_Ling.UndoSystem.ProjectRecord> → Stack<IProjectUndoRecord>
        //   PerformProjectUndo/Redo で record.Undo(ctx)/Redo(ctx) を呼び出し
        //
        // 詳細は ProjectUndoRecords.cs のファイル先頭コメントを参照
        // ====================================================================
        private Stack<Poly_Ling.UndoSystem.ProjectRecord> _projectUndoStack = new Stack<Poly_Ling.UndoSystem.ProjectRecord>();
        private Stack<Poly_Ling.UndoSystem.ProjectRecord> _projectRedoStack = new Stack<Poly_Ling.UndoSystem.ProjectRecord>();

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
        /// <summary>Player 問題 A/B: モデル切替等のプロジェクトレベル Undo 用スタック。</summary>
        public UndoStack<ProjectContext> ProjectStack => _projectStack;
        public UndoGroup SubWindowGroup => _subWindowGroup;

        // ================================================================
        // 記録の一時停止
        // ================================================================

        /// <summary>入れ子の深さ。0 より大きい間は全スタックが記録を捨てる。</summary>
        private int _suspendDepth;

        /// <summary>記録を止めているか。</summary>
        public bool IsRecordingSuspended => _suspendDepth > 0;

        /// <summary>
        /// 全スタックの記録を止める。
        ///
        /// マクロ（オブジェクトグループの複数ステップ実行）のように、
        /// 内側の操作を個別に積まず、外側で 1 件だけ積みたいときに使う。
        /// 必ず ResumeRecording と対で呼ぶこと。入れ子は数える。
        ///
        /// CommandQueue は enqueue 時に同期実行する（CommandQueue.cs:54-70）ので、
        /// 止めている間に積まれるはずだった記録が、あとのフレームへ漏れることはない。
        /// </summary>
        public void SuspendRecording()
        {
            _suspendDepth++;
            if (_suspendDepth == 1) SetRecordingEnabled(false);
        }

        /// <summary>記録を再開する。SuspendRecording と対で呼ぶ。</summary>
        public void ResumeRecording()
        {
            if (_suspendDepth == 0) return;
            _suspendDepth--;
            if (_suspendDepth == 0) SetRecordingEnabled(true);
        }

        private void SetRecordingEnabled(bool enabled)
        {
            if (_vertexEditStack  != null) _vertexEditStack.RecordingEnabled  = enabled;
            if (_editorStateStack != null) _editorStateStack.RecordingEnabled = enabled;
            if (_workPlaneStack   != null) _workPlaneStack.RecordingEnabled   = enabled;
            if (_meshListStack    != null) _meshListStack.RecordingEnabled    = enabled;
            if (_projectStack     != null) _projectStack.RecordingEnabled     = enabled;
        }

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
        /// 直前に実行されたのが Redo であれば true、Undo であれば false。
        /// Player 側 OnUndoRedoPerformed ハンドラで分岐判定に使用。
        /// </summary>
        public bool LastUndoRedoIsRedo { get; private set; } = false;
        
        /// <summary>
        /// プロジェクトUndo実行時のコールバック（復元処理用）
        /// </summary>
        public event Action<Poly_Ling.UndoSystem.ProjectRecord, bool> OnProjectUndoRedoPerformed;

        // === コンストラクタ ===
        public MeshUndoController(string windowId = "MainEditor", UndoManager undoManager = null)
        {
            _undoManager = undoManager ?? UndoManager.Instance;

            // メイングループ作成
            // ★★★ 禁忌: TimestampOnly / FocusThenTimestamp は使用禁止 ★★★
            // DateTime.Now.Ticks は同一フレーム内で同値になり得るため順序不定。
            // 必ず OperationLog を使用すること。
            _mainGroup = new UndoGroup(windowId, "UnityMesh Editor");
            _mainGroup.ResolutionPolicy = UndoResolutionPolicy.OperationLog;

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

            // Projectスタック（ProjectContextを使用）
            // Player 問題 A/B 対応: モデル切替・Add/Remove 等のプロジェクト操作を
            // OperationLog ポリシーの UndoGroup 下で時系列統合するための独立スタック。
            // Context は外部から SetProjectContext() で設定する。
            _projectStack = new UndoStack<ProjectContext>(
                $"{windowId}/ProjectContext",
                "Project",
                null
            );
            _mainGroup.AddChild(_projectStack);

            // MeshContextにWorkPlane参照を設定（選択連動Undo用）
            _meshContext.WorkPlane = _workPlane;

            // EditorStateContextにWorkPlane参照を設定（カメラ連動Undo用）
            _editorStateContext.WorkPlane = _workPlane;

            // サブウインドウグループ
            _subWindowGroup = new UndoGroup($"{windowId}/SubWindows", "Sub Panels");
            _mainGroup.AddChild(_subWindowGroup);

            // イベント購読（スタック種別・Undo/Redo 区別を記録してからInvoke）
            _vertexEditStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.VertexEdit;  LastUndoRedoIsRedo = false; OnUndoRedoPerformed?.Invoke(); };
            _vertexEditStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.VertexEdit;  LastUndoRedoIsRedo = true;  OnUndoRedoPerformed?.Invoke(); };
            _editorStateStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.EditorState; LastUndoRedoIsRedo = false; OnUndoRedoPerformed?.Invoke(); };
            _editorStateStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.EditorState; LastUndoRedoIsRedo = true;  OnUndoRedoPerformed?.Invoke(); };
            _workPlaneStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.WorkPlane;    LastUndoRedoIsRedo = false; OnUndoRedoPerformed?.Invoke(); };
            _workPlaneStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.WorkPlane;    LastUndoRedoIsRedo = true;  OnUndoRedoPerformed?.Invoke(); };
            _meshListStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.MeshList;      LastUndoRedoIsRedo = false; OnUndoRedoPerformed?.Invoke(); };
            _meshListStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.MeshList;      LastUndoRedoIsRedo = true;  OnUndoRedoPerformed?.Invoke(); };
            _projectStack.OnUndoPerformed += _ => { LastUndoRedoStackType = UndoStackType.Project;        LastUndoRedoIsRedo = false; OnUndoRedoPerformed?.Invoke(); };
            _projectStack.OnRedoPerformed += _ => { LastUndoRedoStackType = UndoStackType.Project;        LastUndoRedoIsRedo = true;  OnUndoRedoPerformed?.Invoke(); };

            // グローバルマネージャーに登録（既存があれば削除してから追加）
            var existingChild = _undoManager.FindById(windowId);
            if (existingChild != null)
            {
                _undoManager.RemoveChild(existingChild);
            }
            _undoManager.AddChild(_mainGroup);
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
        /// 対象 MeshContext を明示して設定する。複数メッシュを1件ずつ回す操作は必ずこちらを使う。
        ///
        /// SetMeshObject(MeshObject, Mesh) は書き込み先が
        /// MeshUndoContext.ResolvedMeshContext（既定で先頭の選択メッシュ）になるため、
        /// ループの2件目以降で先頭メッシュの MeshObject を今の対象のもので上書きする。
        /// こちらは ExplicitMeshContext を立ててから書くので、対象自身へ戻るだけになる。
        ///
        /// 使い終わったら ClearTargetMeshContext を呼ぶこと。
        /// </summary>
        public void SetMeshObjectFor(MeshContext targetContext, Mesh targetMesh = null)
        {
            if (targetContext?.MeshObject == null) return;

            _meshContext.ExplicitMeshContext = targetContext;
            _meshContext.MeshObject          = targetContext.MeshObject;
            _meshContext.TargetMesh          = targetMesh ?? targetContext.UnityMesh;
            _meshContext.OriginalPositions   = (Vector3[])targetContext.MeshObject.Positions.Clone();
            _meshContext.SelectedVertices?.Clear();
            _vertexEditStack.Clear();
        }

        /// <summary>明示指定を解除し、既定の解決（先頭の選択メッシュ）へ戻す。</summary>
        public void ClearTargetMeshContext()
        {
            _meshContext.ExplicitMeshContext = null;
        }

        /// <summary>
        /// Undo/Redo後のメッシュ参照同期用。スタックをクリアしない。
        /// </summary>
        public void SyncMeshObjectReference(MeshObject meshObject, Mesh targetMesh = null)
        {
            _meshContext.MeshObject = meshObject;
            if (targetMesh != null)
                _meshContext.TargetMesh = targetMesh;
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
            _undoManager.RemoveChild(_mainGroup);
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
            _undoManager.FocusedChildId = _mainGroup.Id;
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


    }
}