// MeshUndoController_MeshList.cs
// Undo コントローラ：MeshList 操作とプロジェクトレベル Undo 操作の記録。
// Runtime/Poly_Ling_Main/Core/Misc/ に配置

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
    public partial class MeshUndoController
    {
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
        /// Player 問題 A/B: ProjectStack の Context を設定する。
        /// モデル切替などプロジェクトレベル Undo が復元対象とする ProjectContext。
        /// 起動時および ActiveProject 切替時に呼び出すこと。
        /// </summary>
        public void SetProjectContext(ProjectContext projectContext)
        {
            if (_projectStack != null)
                _projectStack.Context = projectContext;
        }

        /// <summary>
        /// Player 問題 A: モデル切替（CurrentModelIndex の変更）を Undo 記録する。
        /// 既存の ModelOperationRecord.CreateSwitch を使い、_projectStack へ push する。
        /// 呼出し前に SetProjectContext で Context が設定されていること。
        /// </summary>
        public void RecordModelSwitch(int oldIndex, int newIndex)
        {
            if (_projectStack == null) return;
            if (oldIndex == newIndex) return;
            var record = ModelOperationRecord.CreateSwitch(
                oldIndex, newIndex,
                default(CameraSnapshot), default(CameraSnapshot),
                null, null);
            {
                string __dbgDesc = $"Switch Model {oldIndex} -> {newIndex}";
                PLDiag.UndoRecord("Project", __dbgDesc, record);
                _projectStack.Record(record, __dbgDesc);
            }
            FocusProjectStack();
        }

        /// <summary>
        /// Player 問題 E/I: モデル追加 (PMX 読込等) を Undo 記録する。
        /// 既存の ModelOperationRecord.CreateAdd を使い、_projectStack へ push する。
        /// ModelContextSnapshot に追加モデル全体が保存されるため、Undo で
        /// モデル自体 (リスト上のエントリ含む) が除去され、Redo で完全復元される。
        /// 呼出し前に SetProjectContext で Context が設定されていること。
        /// </summary>
        public void RecordModelAdd(int addedIndex, ModelContext addedModel, int oldModelIndex)
        {
            if (_projectStack == null) return;
            if (addedModel == null) return;
            var record = ModelOperationRecord.CreateAdd(
                addedIndex, addedModel, oldModelIndex,
                default(CameraSnapshot), default(CameraSnapshot));
            {
                string __dbgDesc = $"Add Model: {addedModel.Name} (idx={addedIndex}, meshes={addedModel.MeshContextCount})";
                PLDiag.UndoRecord("Project", __dbgDesc, record);
                _projectStack.Record(record, __dbgDesc);
            }
            FocusProjectStack();
        }

        /// <summary>
        /// Player 問題: UndoManager._root は FocusPriority なので FocusedChildId が
        /// 設定されていないと Undo が解決されない。ProjectStack に Record した直後に
        /// _mainGroup と _undoManager の FocusedChildId を設定して Undo を可能にする。
        /// </summary>
        public void FocusProjectStack()
        {
            if (_projectStack == null) return;
            _mainGroup.FocusedChildId = _projectStack.Id;
            _undoManager.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// ModelContextを設定し、OnListChangedコールバックを付け替える
        /// </summary>
        public void SetModelContext(ModelContext modelContext, Action onListChangedCallback)
        {
            // 旧コンテキストからコールバック解除
            if (_modelContext != null && onListChangedCallback != null)
            {
                _modelContext.OnListChanged -= onListChangedCallback;
            }

            _modelContext = modelContext;
            _meshListStack.Context = _modelContext;

            // 新コンテキストにコールバック登録
            if (_modelContext != null && onListChangedCallback != null)
            {
                _modelContext.OnListChanged += onListChangedCallback;
            }
        }

        /// <summary>
        /// MeshListにフォーカス
        /// </summary>
        public void FocusMeshList()
        {
            _mainGroup.FocusedChildId = _meshListStack.Id;
            _undoManager.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// メッシュコンテキスト追加を記録
        /// </summary>
        /// <param name="oldMaterials">
        /// 追加処理でモデルのマテリアルスロットも変えた場合の、変更前の一覧。
        /// null なら Undo でマテリアルを触らない（従来どおり）。
        /// </param>
        /// <param name="newMaterials">
        /// 同じく変更後の一覧。null なら Redo でマテリアルを触らない。
        /// </param>
        public void RecordMeshContextAdd(
            MeshContext meshContext, 
            int insertIndex, 
            List<int> oldSelectedIndices, 
            List<int> newSelectedIndices,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null,
            List<Material> oldMaterials = null,
            int oldMaterialIndex = 0,
            List<Material> newMaterials = null,
            int newMaterialIndex = 0)
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
                NewCameraState = newCamera,
                OldMaterials = oldMaterials != null ? new List<Material>(oldMaterials) : null,
                OldCurrentMaterialIndex = oldMaterialIndex,
                NewMaterials = newMaterials != null ? new List<Material>(newMaterials) : null,
                NewCurrentMaterialIndex = newMaterialIndex
            };

            {
                string __dbgDesc = $"Add UnityMesh: {meshContext.Name}";
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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

            {
                string __dbgDesc = desc;
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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

            {
                string __dbgDesc = desc;
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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

            {
                string __dbgDesc = $"Reorder UnityMesh: {meshContext.Name}";
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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
            {
                string __dbgDesc = "Select Mesh";
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
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
            {
                string __dbgDesc = "Select Mesh";
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _meshListStack.Record(record, __dbgDesc);
            }
            FocusMeshList();
        }

        // ================================================================
        // プロジェクトレベルUndo操作
        // ================================================================

        /// <summary>
        /// プロジェクト操作を記録（ファイル読み込み/新規作成用）
        /// </summary>
        /// <param name="record">プロジェクト操作の記録</param>
        public void RecordProjectOperation(Poly_Ling.UndoSystem.ProjectRecord record)
        {
            if (record == null) return;

            _projectUndoStack.Push(record);
            _projectRedoStack.Clear();  // 新しい操作でRedoスタックをクリア
        }

        /// <summary>
        /// プロジェクトレベルのUndoを実行
        /// </summary>
        /// <returns>Undo実行されたProjectRecord（復元用）、実行できない場合はnull</returns>
        public Poly_Ling.UndoSystem.ProjectRecord UndoProject()
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
        public Poly_Ling.UndoSystem.ProjectRecord RedoProject()
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
