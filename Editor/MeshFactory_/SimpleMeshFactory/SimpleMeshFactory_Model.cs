// Assets/Editor/SimpleMeshFactory.Model.cs
// モデル管理機能（Phase 2-3）
// - モデル選択UI
// - モデル追加/削除/切り替え
// - Undo対応（Phase 3）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;
using MeshFactory.UndoSystem;
using MeshFactory.Localization;

public partial class SimpleMeshFactory
{
    // ================================================================
    // モデル管理UI
    // ================================================================

    /// <summary>
    /// モデル選択UIを描画
    /// DrawMeshListの先頭で呼び出す
    /// </summary>
    private void DrawModelSelector()
    {
        if (_project == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ヘッダー行
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(L.Get("Model"), EditorStyles.boldLabel, GUILayout.Width(50));

        // モデル選択ドロップダウン
        if (_project.ModelCount > 0)
        {
            string[] modelNames = _project.Models.Select(m => m.Name ?? "Untitled").ToArray();
            int currentIndex = _project.CurrentModelIndex;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(currentIndex, modelNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                SwitchModelWithUndo(newIndex);
            }
        }
        else
        {
            EditorGUILayout.LabelField("(No Model)", EditorStyles.miniLabel);
        }

        // 追加ボタン
        if (GUILayout.Button("+", GUILayout.Width(24)))
        {
            AddNewModelWithUndo();
        }

        // 削除ボタン（モデルが2つ以上ある場合のみ有効）
        using (new EditorGUI.DisabledScope(_project.ModelCount <= 1))
        {
            if (GUILayout.Button("-", GUILayout.Width(24)))
            {
                if (EditorUtility.DisplayDialog(
                    L.Get("DeleteModel"),
                    string.Format(L.Get("DeleteModelConfirm"), _model?.Name ?? ""),
                    L.Get("Delete"),
                    L.Get("Cancel")))
                {
                    RemoveCurrentModelWithUndo();
                }
            }
        }

        EditorGUILayout.EndHorizontal();

        // モデル名編集
        if (_model != null)
        {
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(_model.Name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName))
            {
                RenameModelWithUndo(newName);
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);
    }

    // ================================================================
    // モデル切り替え（選択保存/復元付き）
    // ================================================================

    /// <summary>
    /// モデルを切り替え（Undo対応）
    /// </summary>
    private void SwitchModelWithUndo(int newIndex)
    {
        if (_project == null) return;
        if (newIndex < 0 || newIndex >= _project.ModelCount) return;
        if (newIndex == _project.CurrentModelIndex) return;

        int oldIndex = _project.CurrentModelIndex;

        // ★Phase 1: 現在のメッシュの選択を保存
        SaveSelectionToCurrentMesh();

        // 選択状態スナップショット（Undo用）
        MeshSelectionSnapshot oldSelection = _model?.CurrentMeshContext?.CaptureSelection();

        // カメラ状態を保存
        CameraSnapshot oldCamera = CaptureCameraSnapshot();

        // モデル切り替え
        _project.CurrentModelIndex = newIndex;

        // UndoControllerの参照を更新
        RefreshUndoControllerReferences();

        // 選択インデックスをリセット
        if (_model != null && _model.MeshContextCount > 0)
        {
            _selectedIndex = _model.SelectedMeshContextIndex;
            if (_selectedIndex < 0 || _selectedIndex >= _model.MeshContextCount)
            {
                _selectedIndex = 0;
                _model.SelectedMeshContextIndex = 0;
            }
        }
        else
        {
            _selectedIndex = -1;
        }

        // ★Phase 1: 新しいメッシュの選択を復元
        LoadSelectionFromCurrentMesh();

        // 頂点オフセット初期化
        InitVertexOffsets();

        // カメラをフィット
        CameraSnapshot newCamera = CaptureCameraSnapshot();

        // 選択状態スナップショット（新モデル）
        MeshSelectionSnapshot newSelection = _model?.CurrentMeshContext?.CaptureSelection();

        // MeshContextをUndoControllerにロード
        if (_model?.CurrentMeshContext != null)
        {
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        // ★Phase 3: Undo記録
        RecordModelSwitchUndo(oldIndex, newIndex, oldCamera, newCamera, oldSelection, newSelection);

        Repaint();
    }

    /// <summary>
    /// UndoControllerの参照を現在のモデルに更新
    /// </summary>
    private void RefreshUndoControllerReferences()
    {
        if (_undoController == null || _model == null) return;

        // ModelContextの参照を更新（SetModelContextを使用）
        _undoController.SetModelContext(_model, OnMeshListChanged);

        // MeshUndoContextのMaterialOwnerを更新
        if (_undoController.MeshUndoContext != null)
        {
            _undoController.MeshUndoContext.MaterialOwner = _model;
        }

        // 各MeshContextのMaterialOwnerを更新
        if (_meshContextList != null)
        {
            foreach (var meshContext in _meshContextList)
            {
                meshContext.MaterialOwner = _model;
            }
        }
    }

    // ================================================================
    // モデル追加
    // ================================================================

    /// <summary>
    /// 新規モデルを追加（Undo対応）
    /// </summary>
    private void AddNewModelWithUndo()
    {
        if (_project == null) return;

        // ユニークな名前を生成
        string newName = _project.GenerateUniqueModelName("Model");

        // 新規モデル作成
        var newModel = new ModelContext(newName);

        // デフォルトマテリアルを設定
        if (_model != null && _model.DefaultMaterials != null)
        {
            newModel.DefaultMaterials = new List<Material>(_model.DefaultMaterials);
            newModel.AutoSetDefaultMaterials = _model.AutoSetDefaultMaterials;
        }

        // Undo記録前の状態
        int oldModelIndex = _project.CurrentModelIndex;
        CameraSnapshot oldCamera = CaptureCameraSnapshot();

        // 現在の選択を保存
        SaveSelectionToCurrentMesh();

        // モデル追加
        int newIndex = _project.AddModel(newModel);

        // 新モデルに切り替え
        _project.CurrentModelIndex = newIndex;

        // UndoController更新
        RefreshUndoControllerReferences();

        // 選択リセット
        _selectedIndex = -1;
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        InitVertexOffsets();

        CameraSnapshot newCamera = CaptureCameraSnapshot();

        // ★Phase 3: Undo記録
        RecordModelAddUndo(newIndex, newModel, oldModelIndex, oldCamera, newCamera);

        Repaint();
    }

    // ================================================================
    // モデル削除
    // ================================================================

    /// <summary>
    /// 現在のモデルを削除（Undo対応）
    /// </summary>
    private void RemoveCurrentModelWithUndo()
    {
        if (_project == null || _project.ModelCount <= 1) return;

        int removeIndex = _project.CurrentModelIndex;
        var removedModel = _project.CurrentModel;
        string removedName = removedModel?.Name ?? "Untitled";

        // ★Phase 3: 削除前にスナップショットを作成
        ModelContextSnapshot removedSnapshot = ModelContextSnapshot.Capture(removedModel);

        CameraSnapshot oldCamera = CaptureCameraSnapshot();

        // モデル削除（リソース破棄は行わない - Undo復元用）
        _project.Models.RemoveAt(removeIndex);
        _project.IsDirty = true;

        // カレントインデックス調整
        int newModelIndex = removeIndex >= _project.ModelCount 
            ? _project.ModelCount - 1 
            : removeIndex;
        
        if (newModelIndex >= 0)
        {
            _project.CurrentModelIndex = newModelIndex;
        }

        // UndoController更新
        RefreshUndoControllerReferences();

        // 選択リセット
        if (_model != null && _model.MeshContextCount > 0)
        {
            _selectedIndex = 0;
            _model.SelectedMeshContextIndex = 0;
        }
        else
        {
            _selectedIndex = -1;
        }
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        LoadSelectionFromCurrentMesh();
        InitVertexOffsets();

        if (_model?.CurrentMeshContext != null)
        {
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        CameraSnapshot newCamera = CaptureCameraSnapshot();

        // ★Phase 3: Undo記録
        RecordModelRemoveUndo(removeIndex, removedSnapshot, newModelIndex, oldCamera, newCamera);

        Repaint();
    }

    // ================================================================
    // モデル名変更
    // ================================================================

    /// <summary>
    /// モデル名を変更（Undo対応）
    /// </summary>
    private void RenameModelWithUndo(string newName)
    {
        if (_model == null || string.IsNullOrEmpty(newName)) return;

        string oldName = _model.Name;
        if (oldName == newName) return;

        _model.Name = newName;

        // ★Phase 3: Undo記録
        RecordModelRenameUndo(_project.CurrentModelIndex, oldName, newName);
    }

    // ================================================================
    // Phase 3: Undo記録メソッド
    // ================================================================

    /// <summary>
    /// モデル切り替えのUndo記録
    /// </summary>
    private void RecordModelSwitchUndo(
        int oldIndex, 
        int newIndex, 
        CameraSnapshot oldCamera, 
        CameraSnapshot newCamera,
        MeshSelectionSnapshot oldSelection,
        MeshSelectionSnapshot newSelection)
    {
        if (_undoController == null) return;

        var record = ModelOperationRecord.CreateSwitch(
            oldIndex, newIndex, oldCamera, newCamera, oldSelection, newSelection);

        // MeshListStackに記録（ProjectStackが未実装の場合のフォールバック）
        _undoController.MeshListStack?.Record(
            new ModelOperationRecordWrapper(record, _project), 
            $"Switch Model: {oldIndex} -> {newIndex}");

        _undoController.FocusMeshList();

        Debug.Log($"[RecordModelSwitchUndo] Recorded: {oldIndex} -> {newIndex}");
    }

    /// <summary>
    /// モデル追加のUndo記録
    /// </summary>
    private void RecordModelAddUndo(
        int addedIndex, 
        ModelContext addedModel, 
        int oldModelIndex, 
        CameraSnapshot oldCamera, 
        CameraSnapshot newCamera)
    {
        if (_undoController == null) return;

        var record = ModelOperationRecord.CreateAdd(
            addedIndex, addedModel, oldModelIndex, oldCamera, newCamera);

        _undoController.MeshListStack?.Record(
            new ModelOperationRecordWrapper(record, _project),
            $"Add Model: {addedModel?.Name}");

        _undoController.FocusMeshList();

        Debug.Log($"[RecordModelAddUndo] Recorded: {addedModel?.Name} at {addedIndex}");
    }

    /// <summary>
    /// モデル削除のUndo記録
    /// </summary>
    private void RecordModelRemoveUndo(
        int removedIndex, 
        ModelContextSnapshot removedSnapshot, 
        int newModelIndex, 
        CameraSnapshot oldCamera, 
        CameraSnapshot newCamera)
    {
        if (_undoController == null) return;

        // 直接スナップショットを使用するレコードを作成
        var record = new ModelOperationRecord
        {
            Operation = ModelOperationType.Remove,
            OldModelIndex = removedIndex,
            NewModelIndex = newModelIndex,
            ModelSnapshot = removedSnapshot,
            OldCameraState = oldCamera,
            NewCameraState = newCamera
        };

        _undoController.MeshListStack?.Record(
            new ModelOperationRecordWrapper(record, _project),
            $"Remove Model at {removedIndex}");

        _undoController.FocusMeshList();

        Debug.Log($"[RecordModelRemoveUndo] Recorded: index {removedIndex}");
    }

    /// <summary>
    /// モデル名変更のUndo記録
    /// </summary>
    private void RecordModelRenameUndo(int modelIndex, string oldName, string newName)
    {
        if (_undoController == null) return;

        var record = ModelOperationRecord.CreateRename(modelIndex, oldName, newName);

        _undoController.MeshListStack?.Record(
            new ModelOperationRecordWrapper(record, _project),
            $"Rename Model: {oldName} -> {newName}");

        _undoController.FocusMeshList();

        Debug.Log($"[RecordModelRenameUndo] Recorded: {oldName} -> {newName}");
    }

    // ================================================================
    // カメラ状態ヘルパー
    // ================================================================

    /// <summary>
    /// カメラ状態をキャプチャ
    /// </summary>
    private CameraSnapshot CaptureCameraSnapshot()
    {
        return new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };
    }

    /// <summary>
    /// カメラ状態を復元
    /// </summary>
    private void RestoreCameraSnapshot(CameraSnapshot snapshot)
    {
        _rotationX = snapshot.RotationX;
        _rotationY = snapshot.RotationY;
        _cameraDistance = snapshot.CameraDistance;
        _cameraTarget = snapshot.CameraTarget;
    }

    // ================================================================
    // ProjectContextコールバック
    // ================================================================

    /// <summary>
    /// カレントモデル変更時のコールバック
    /// </summary>
    private void OnCurrentModelChanged(int newIndex)
    {
        Debug.Log($"[OnCurrentModelChanged] Model index changed to {newIndex}");
        RefreshUndoControllerReferences();
        Repaint();
    }

    /// <summary>
    /// モデルリスト変更時のコールバック
    /// </summary>
    private void OnModelsChanged()
    {
        Debug.Log("[OnModelsChanged] Models list changed");
        Repaint();
    }

    /// <summary>
    /// カメラ復元要求（Undo/Redo時）
    /// </summary>
    private void OnCameraRestoreRequested(CameraSnapshot snapshot)
    {
        RestoreCameraSnapshot(snapshot);
        Repaint();
    }

    /// <summary>
    /// 選択状態復元要求（Undo/Redo時）
    /// </summary>
    private void OnSelectionRestoreRequested(MeshSelectionSnapshot snapshot)
    {
        if (snapshot == null) return;

        var meshContext = _model?.CurrentMeshContext;
        if (meshContext != null)
        {
            meshContext.RestoreSelection(snapshot);
            LoadSelectionFromCurrentMesh();
        }
        Repaint();
    }

    /// <summary>
    /// UndoController参照更新要求（Undo/Redo時）
    /// </summary>
    private void OnRefreshUndoControllerRequested()
    {
        RefreshUndoControllerReferences();

        // 選択インデックスを同期
        if (_model != null)
        {
            _selectedIndex = _model.SelectedMeshContextIndex;
            if (_selectedIndex < 0 && _model.MeshContextCount > 0)
            {
                _selectedIndex = 0;
                _model.SelectedMeshContextIndex = 0;
            }
        }

        // MeshContextをUndoControllerにロード
        if (_model?.CurrentMeshContext != null)
        {
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        // 選択状態を復元
        LoadSelectionFromCurrentMesh();

        InitVertexOffsets();
        Repaint();
    }

    // ================================================================
    // コールバック設定/解除
    // ================================================================

    /// <summary>
    /// ProjectContextのコールバックを設定
    /// </summary>
    private void SetupProjectContextCallbacks()
    {
        if (_project == null) return;

        _project.OnCurrentModelChanged += OnCurrentModelChanged;
        _project.OnModelsChanged += OnModelsChanged;
        _project.OnCameraRestoreRequested += OnCameraRestoreRequested;
        _project.OnSelectionRestoreRequested += OnSelectionRestoreRequested;
        _project.OnRefreshUndoControllerRequested += OnRefreshUndoControllerRequested;
    }

    /// <summary>
    /// ProjectContextのコールバックを解除
    /// </summary>
    private void ClearProjectContextCallbacks()
    {
        if (_project == null) return;

        _project.OnCurrentModelChanged -= OnCurrentModelChanged;
        _project.OnModelsChanged -= OnModelsChanged;
        _project.OnCameraRestoreRequested -= OnCameraRestoreRequested;
        _project.OnSelectionRestoreRequested -= OnSelectionRestoreRequested;
        _project.OnRefreshUndoControllerRequested -= OnRefreshUndoControllerRequested;
    }
}

// ================================================================
// ModelOperationRecordのラッパー（MeshListStack用）
// ================================================================

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// ModelOperationRecordをModelContext用UndoStackで使用するためのラッパー
    /// </summary>
    public class ModelOperationRecordWrapper : IUndoRecord<ModelContext>
    {
        public UndoOperationInfo Info { get; set; }

        private readonly ModelOperationRecord _innerRecord;
        private readonly ProjectContext _project;

        public ModelOperationRecordWrapper(ModelOperationRecord record, ProjectContext project)
        {
            _innerRecord = record;
            _project = project;
        }

        public void Undo(ModelContext ctx)
        {
            if (_project == null || _innerRecord == null) return;

            switch (_innerRecord.Operation)
            {
                case ModelOperationType.Add:
                    UndoModelAdd();
                    break;

                case ModelOperationType.Remove:
                    UndoModelRemove();
                    break;

                case ModelOperationType.Switch:
                    UndoModelSwitch();
                    break;

                case ModelOperationType.Rename:
                    UndoModelRename();
                    break;
            }
        }

        public void Redo(ModelContext ctx)
        {
            if (_project == null || _innerRecord == null) return;

            switch (_innerRecord.Operation)
            {
                case ModelOperationType.Add:
                    RedoModelAdd();
                    break;

                case ModelOperationType.Remove:
                    RedoModelRemove();
                    break;

                case ModelOperationType.Switch:
                    RedoModelSwitch();
                    break;

                case ModelOperationType.Rename:
                    RedoModelRename();
                    break;
            }
        }

        // ---- Add ----
        private void UndoModelAdd()
        {
            int addedIndex = _innerRecord.NewModelIndex;
            if (addedIndex >= 0 && addedIndex < _project.ModelCount)
            {
                _project.Models.RemoveAt(addedIndex);
            }

            int oldIndex = _innerRecord.OldModelIndex;
            if (oldIndex >= 0 && oldIndex < _project.ModelCount)
            {
                _project.CurrentModelIndex = oldIndex;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.OldCameraState);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        private void RedoModelAdd()
        {
            if (_innerRecord.ModelSnapshot != null)
            {
                var restoredModel = _innerRecord.ModelSnapshot.ToModelContext();
                int insertIndex = Math.Min(_innerRecord.NewModelIndex, _project.ModelCount);
                _project.Models.Insert(insertIndex, restoredModel);
                _project.CurrentModelIndex = insertIndex;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.NewCameraState);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        // ---- Remove ----
        private void UndoModelRemove()
        {
            if (_innerRecord.ModelSnapshot != null)
            {
                var restoredModel = _innerRecord.ModelSnapshot.ToModelContext();
                int insertIndex = Math.Min(_innerRecord.OldModelIndex, _project.ModelCount);
                _project.Models.Insert(insertIndex, restoredModel);
                _project.CurrentModelIndex = insertIndex;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.OldCameraState);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        private void RedoModelRemove()
        {
            int removeIndex = _innerRecord.OldModelIndex;
            if (removeIndex >= 0 && removeIndex < _project.ModelCount)
            {
                _project.Models.RemoveAt(removeIndex);
            }

            int newIndex = _innerRecord.NewModelIndex;
            if (newIndex >= 0 && newIndex < _project.ModelCount)
            {
                _project.CurrentModelIndex = newIndex;
            }
            else if (_project.ModelCount > 0)
            {
                _project.CurrentModelIndex = _project.ModelCount - 1;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.NewCameraState);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        // ---- Switch ----
        private void UndoModelSwitch()
        {
            int oldIndex = _innerRecord.OldModelIndex;
            if (oldIndex >= 0 && oldIndex < _project.ModelCount)
            {
                _project.CurrentModelIndex = oldIndex;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.OldCameraState);
            _project.OnSelectionRestoreRequested?.Invoke(_innerRecord.OldMeshSelection);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        private void RedoModelSwitch()
        {
            int newIndex = _innerRecord.NewModelIndex;
            if (newIndex >= 0 && newIndex < _project.ModelCount)
            {
                _project.CurrentModelIndex = newIndex;
            }

            _project.OnCameraRestoreRequested?.Invoke(_innerRecord.NewCameraState);
            _project.OnSelectionRestoreRequested?.Invoke(_innerRecord.NewMeshSelection);
            _project.OnRefreshUndoControllerRequested?.Invoke();
            _project.OnModelsChanged?.Invoke();
        }

        // ---- Rename ----
        private void UndoModelRename()
        {
            var model = _project.GetModel(_innerRecord.OldModelIndex);
            if (model != null)
            {
                model.Name = _innerRecord.OldName;
            }
            _project.OnModelsChanged?.Invoke();
        }

        private void RedoModelRename()
        {
            var model = _project.GetModel(_innerRecord.NewModelIndex);
            if (model != null)
            {
                model.Name = _innerRecord.NewName;
            }
            _project.OnModelsChanged?.Invoke();
        }
    }
}
