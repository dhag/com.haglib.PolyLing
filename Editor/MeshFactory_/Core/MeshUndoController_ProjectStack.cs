// ================================================================
// Phase 3: MeshUndoController拡張
// 以下のコードをMeshUndoController.csに追加してください
// ================================================================

// ----------------------------------------------------------------
// 1. フィールド追加（クラス冒頭、_subWindowGroup の後）
// ----------------------------------------------------------------

/*
        // === ProjectStack（モデル操作用） ===
        private UndoStack<ProjectContext> _projectStack;
        private ProjectContext _projectContext;
*/

// ----------------------------------------------------------------
// 2. プロパティ追加
// ----------------------------------------------------------------

/*
        public UndoStack<ProjectContext> ProjectStack => _projectStack;
        public ProjectContext ProjectContext => _projectContext;
*/

// ----------------------------------------------------------------
// 3. コンストラクタ内に追加（_subWindowGroup作成後）
// ----------------------------------------------------------------

/*
            // ProjectStack（モデル操作用）
            _projectContext = new ProjectContext();
            _projectStack = new UndoStack<ProjectContext>(
                $"{windowId}/Project",
                "Project",
                _projectContext
            );
            _mainGroup.AddChild(_projectStack);

            // ProjectStackイベント購読
            _projectStack.OnUndoPerformed += _ => OnUndoRedoPerformed?.Invoke();
            _projectStack.OnRedoPerformed += _ => OnUndoRedoPerformed?.Invoke();
*/

// ----------------------------------------------------------------
// 4. SetProjectContextメソッドを追加
// ----------------------------------------------------------------

/*
        /// <summary>
        /// ProjectContextを設定
        /// </summary>
        public void SetProjectContext(ProjectContext projectContext)
        {
            _projectContext = projectContext;
            _projectStack = new UndoStack<ProjectContext>(
                _projectStack.Id,
                _projectStack.DisplayName,
                _projectContext
            );
        }
*/

// ----------------------------------------------------------------
// 5. RecordModelOperation メソッドを追加
// ----------------------------------------------------------------

/*
        /// <summary>
        /// モデル操作を記録
        /// </summary>
        public void RecordModelOperation(ModelOperationRecord record, string description)
        {
            if (_projectStack == null || record == null) return;
            _projectStack.Record(record, description);
        }

        /// <summary>
        /// モデル追加を記録
        /// </summary>
        public void RecordModelAdd(
            int addedIndex,
            ModelContext addedModel,
            int oldModelIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera)
        {
            var record = ModelOperationRecord.CreateAdd(
                addedIndex, addedModel, oldModelIndex, oldCamera, newCamera);
            RecordModelOperation(record, $"Add Model: {addedModel?.Name}");
        }

        /// <summary>
        /// モデル削除を記録
        /// </summary>
        public void RecordModelRemove(
            int removedIndex,
            ModelContext removedModel,
            int newModelIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera)
        {
            var record = ModelOperationRecord.CreateRemove(
                removedIndex, removedModel, newModelIndex, oldCamera, newCamera);
            RecordModelOperation(record, $"Remove Model: {removedModel?.Name}");
        }

        /// <summary>
        /// モデル切り替えを記録
        /// </summary>
        public void RecordModelSwitch(
            int oldIndex,
            int newIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera,
            MeshSelectionSnapshot oldSelection = null,
            MeshSelectionSnapshot newSelection = null)
        {
            var record = ModelOperationRecord.CreateSwitch(
                oldIndex, newIndex, oldCamera, newCamera, oldSelection, newSelection);
            RecordModelOperation(record, $"Switch Model: {oldIndex} -> {newIndex}");
        }

        /// <summary>
        /// モデル名変更を記録
        /// </summary>
        public void RecordModelRename(int modelIndex, string oldName, string newName)
        {
            var record = ModelOperationRecord.CreateRename(modelIndex, oldName, newName);
            RecordModelOperation(record, $"Rename Model: {oldName} -> {newName}");
        }

        /// <summary>
        /// ProjectStackにフォーカス
        /// </summary>
        public void FocusProjectStack()
        {
            _mainGroup.FocusedChildId = _projectStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }
*/

// ================================================================
// using追加（ファイル冒頭）
// ================================================================

/*
using MeshFactory.Model;
*/
