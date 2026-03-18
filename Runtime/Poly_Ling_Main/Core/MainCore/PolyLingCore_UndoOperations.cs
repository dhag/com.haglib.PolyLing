// PolyLingCore_UndoOperations.cs
// Undo付きMeshContext操作群
// PolyLing_Tools.cs (IO) から移植

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Tools;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore
    {
        // ================================================================
        // AddMeshContextWithUndo
        // ================================================================

        partial void AddMeshContextWithUndo(MeshContext meshContext)
        {
            if (meshContext == null) return;

            meshContext.ParentModelContext = _model;

            var oldSelected = _model.CaptureAllSelectedIndices();
            var oldCamera   = CaptureCameraSnapshot();

            int insertIndex = _model.Add(meshContext);
            SetSelectedIndex(insertIndex);
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            _undoController?.RecordMeshContextAdd(
                meshContext, insertIndex, oldSelected, newSelected, oldCamera, newCamera);

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // AddMeshContextsWithUndo
        // ================================================================

        partial void AddMeshContextsWithUndo(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null || meshContexts.Count == 0) return;

            var oldSelected    = _model.CaptureAllSelectedIndices();
            var oldMaterials   = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
            var oldMatIndex    = _model?.CurrentMaterialIndex ?? 0;
            var oldCamera      = CaptureCameraSnapshot();

            var added = new List<(int, MeshContext)>();
            foreach (var mc in meshContexts)
            {
                if (mc == null) continue;
                mc.ParentModelContext = _model;
                int idx = _model.Add(mc);
                added.Add((idx, mc));
            }
            if (added.Count == 0) return;

            SetSelectedIndex(_meshContextList.Count - 1);
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            _undoController?.RecordMeshContextsAdd(
                added, oldSelected, newSelected, oldCamera, newCamera, oldMaterials, oldMatIndex);

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // RemoveMeshContextWithUndo
        // ================================================================

        partial void RemoveMeshContextWithUndo(int index)
        {
            if (index < 0 || index >= _meshContextList.Count) return;

            var meshContext = _meshContextList[index];
            var oldSelected = _model.CaptureAllSelectedIndices();
            var oldCamera   = CaptureCameraSnapshot();

            if (meshContext.UnityMesh != null)
                Object.DestroyImmediate(meshContext.UnityMesh);

            _model.RemoveAt(index);

            if (_model.FirstMeshIndex < 0 && _meshContextList.Count > 0)
                SetSelectedIndex(Mathf.Min(index, _meshContextList.Count - 1));

            _selectionState?.ClearAll();
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            _undoController?.RecordMeshContextsRemove(
                new List<(int, MeshContext)> { (index, meshContext) },
                oldSelected, newSelected, oldCamera, newCamera);

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // SelectMeshContentWithUndo
        // ================================================================

        partial void SelectMeshContentWithUndo(int index)
        {
            if (index < -1 || index >= _meshContextList.Count) return;
            if (index == _model.FirstMeshIndex) return;

            SaveSelectionToCurrentMesh();

            var oldSelected = _model.CaptureAllSelectedIndices();
            SetSelectedIndex(index);
            var newSelected = _model.CaptureAllSelectedIndices();

            _undoController?.RecordMeshSelectionChange(oldSelected, newSelected);

            LoadSelectionFromCurrentMesh();

            if (_model.HasValidMeshContextSelection)
            {
                InitVertexOffsets();
                LoadMeshContextToUndoController(_model.FirstSelectedMeshContext);
                UpdateTopology();
            }

            OnRepaintRequired?.Invoke();
            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // DuplicateMeshContentWithUndo
        // ================================================================

        partial void DuplicateMeshContentWithUndo(int index)
        {
            if (index < 0 || index >= _meshContextList.Count) return;

            var original = _meshContextList[index];
            var clone = new MeshContext
            {
                Name               = original.Name + " (Copy)",
                MeshObject         = original.MeshObject?.Clone(),
                UnityMesh          = original.MeshObject?.ToUnityMesh(),
                OriginalPositions  = original.OriginalPositions?.ToArray(),
                BoneTransform      = new BoneTransform
                {
                    UseLocalTransform = original.BoneTransform?.UseLocalTransform ?? false,
                    Position          = original.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = original.BoneTransform?.Rotation ?? Vector3.zero,
                    Scale             = original.BoneTransform?.Scale ?? Vector3.one,
                },
                Materials          = new List<Material>(original.Materials ?? new List<Material> { null }),
                CurrentMaterialIndex = original.CurrentMaterialIndex,
                ParentModelContext = _model,
            };

            int insertIndex = index + 1;
            var oldSelected = _model.CaptureAllSelectedIndices();
            var oldCamera   = CaptureCameraSnapshot();

            _model.Insert(insertIndex, clone);
            SetSelectedIndex(insertIndex);
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            _undoController?.RecordMeshContextAdd(
                clone, insertIndex, oldSelected, newSelected, oldCamera, newCamera);

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // ReorderMeshContentWithUndo
        // ================================================================

        partial void ReorderMeshContentWithUndo(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _meshContextList.Count) return;
            if (toIndex   < 0 || toIndex   >= _meshContextList.Count) return;
            if (fromIndex == toIndex) return;

            var meshContext = _meshContextList[fromIndex];
            var oldSelected = _model.CaptureAllSelectedIndices();

            _model.Move(fromIndex, toIndex);

            var newSelected = _model.CaptureAllSelectedIndices();

            _undoController?.RecordMeshContextReorder(
                meshContext, fromIndex, toIndex, oldSelected, newSelected);

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // UpdateMeshAttributesWithUndo
        // ================================================================

        partial void UpdateMeshAttributesWithUndo(IList<MeshAttributeChange> changes)
        {
            if (changes == null || changes.Count == 0) return;

            var oldValues = new List<MeshAttributeChange>();
            foreach (var change in changes)
            {
                if (change.Index < 0 || change.Index >= _meshContextList.Count) continue;
                var ctx = _meshContextList[change.Index];
                var old = new MeshAttributeChange { Index = change.Index };
                if (change.IsVisible.HasValue)  { old.IsVisible  = ctx.IsVisible;  ctx.IsVisible  = change.IsVisible.Value; }
                if (change.IsLocked.HasValue)   { old.IsLocked   = ctx.IsLocked;   ctx.IsLocked   = change.IsLocked.Value; }
                if (change.MirrorType.HasValue) { old.MirrorType = ctx.MirrorType; ctx.MirrorType = change.MirrorType.Value; }
                if (change.Name != null)        { old.Name       = ctx.Name;       ctx.Name       = change.Name; }
                oldValues.Add(old);
            }

            if (_undoController != null && oldValues.Count > 0)
            {
                var record = new MeshAttributesBatchChangeRecord(oldValues, changes.ToList());
                _undoController.MeshListStack.Record(record, "属性変更");
            }

            _model.IsDirty = true;
            _model?.OnListChanged?.Invoke();
            OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // ClearAllMeshContextsWithUndo
        // ================================================================

        partial void ClearAllMeshContextsWithUndo()
        {
            if (_meshContextList.Count == 0) return;

            var oldSelected  = _model.CaptureAllSelectedIndices();
            var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
            var oldMatIndex  = _model?.CurrentMaterialIndex ?? 0;
            var oldCamera    = CaptureCameraSnapshot();

            var removed = new List<(int, MeshContextSnapshot)>();
            for (int i = 0; i < _meshContextList.Count; i++)
                removed.Add((i, MeshContextSnapshot.Capture(_meshContextList[i])));

            for (int i = _meshContextList.Count - 1; i >= 0; i--)
            {
                if (_meshContextList[i].UnityMesh != null)
                    Object.DestroyImmediate(_meshContextList[i].UnityMesh);
                _model.RemoveAt(i);
            }

            SetSelectedIndex(-1);
            _selectionState?.ClearAll();
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            if (_undoController != null)
            {
                var record = new MeshListChangeRecord
                {
                    RemovedMeshContexts = removed,
                    OldSelectedIndices  = oldSelected,
                    NewSelectedIndices  = newSelected,
                    OldCameraState      = oldCamera,
                    NewCameraState      = newCamera,
                };
                _undoController.RecordMeshListChange(record, "Clear All Meshes", oldMaterials, oldMatIndex);
            }

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // ReplaceAllMeshContextsWithUndo
        // ================================================================

        partial void ReplaceAllMeshContextsWithUndo(IList<MeshContext> newContexts)
        {
            if (newContexts == null || newContexts.Count == 0)
            {
                ClearAllMeshContextsWithUndo();
                return;
            }

            var oldSelected  = _model.CaptureAllSelectedIndices();
            var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
            var oldMatIndex  = _model?.CurrentMaterialIndex ?? 0;
            var oldCamera    = CaptureCameraSnapshot();

            var removed = new List<(int, MeshContextSnapshot)>();
            for (int i = 0; i < _meshContextList.Count; i++)
                removed.Add((i, MeshContextSnapshot.Capture(_meshContextList[i])));

            for (int i = _meshContextList.Count - 1; i >= 0; i--)
            {
                if (_meshContextList[i].UnityMesh != null)
                    Object.DestroyImmediate(_meshContextList[i].UnityMesh);
                _model.RemoveAt(i);
            }

            var added = new List<(int, MeshContextSnapshot)>();
            foreach (var mc in newContexts)
            {
                if (mc == null) continue;
                mc.ParentModelContext = _model;
                int idx = _model.Add(mc);
                added.Add((idx, MeshContextSnapshot.Capture(mc)));
            }

            SetSelectedIndex(_meshContextList.Count > 0 ? 0 : -1);
            _selectionState?.ClearAll();
            InitVertexOffsets();

            var newSelected = _model.CaptureAllSelectedIndices();
            var newCamera   = CaptureCameraSnapshot();

            if (_undoController != null)
            {
                var record = new MeshListChangeRecord
                {
                    RemovedMeshContexts = removed,
                    AddedMeshContexts   = added,
                    OldSelectedIndices  = oldSelected,
                    NewSelectedIndices  = newSelected,
                    OldCameraState      = oldCamera,
                    NewCameraState      = newCamera,
                };
                _undoController.RecordMeshListChange(
                    record, $"Replace All: {newContexts.Count} meshes", oldMaterials, oldMatIndex);
            }

            _model?.OnListChanged?.Invoke();
        }

        // ================================================================
        // CreateNewModelWithUndo / SelectModelWithUndo
        // ================================================================

        partial void CreateNewModelWithUndo_void(string name)
        {
            var before  = ProjectSnapshot.CaptureFromProjectContext(_project);
            var newModel = _project.CreateNewModel(name);
            _toolManager.toolContext.Model = newModel;
            var after = ProjectSnapshot.CaptureFromProjectContext(_project);

            if (_undoController != null && before != null && after != null)
                _undoController.RecordProjectOperation(ProjectRecord.CreateNewModel(before, after));
        }

        partial void SelectModelWithUndo(int index)
        {
            if (_project == null) return;
            int oldIndex = _project.CurrentModelIndex;
            if (oldIndex == index) return;

            var before = ProjectSnapshot.CaptureFromProjectContext(_project);
            if (_project.SelectModel(index))
            {
                _toolManager.toolContext.Model = _project.CurrentModel;
                var after = ProjectSnapshot.CaptureFromProjectContext(_project);
                if (_undoController != null && before != null && after != null)
                    _undoController.RecordProjectOperation(ProjectRecord.CreateSelectModel(before, after));
            }
        }

        // ================================================================
        // Material操作
        // ================================================================

        partial void AddMaterialsToModel(IList<Material> mats)
        {
            if (_model == null || mats == null) return;
            var current = _model.Materials;
            var newList = new List<Material>();
            bool skipExisting = current.Count == 1 && current[0] == null;
            if (!skipExisting) newList.AddRange(current);
            newList.AddRange(mats);
            _model.Materials = newList;
            _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
        }

        partial void AddMaterialRefsToModel(IList<MaterialReference> refs)
        {
            if (_model == null || refs == null) return;
            var current = _model.MaterialReferences;
            var newList = new List<MaterialReference>();
            bool skipExisting = current.Count == 1 && current[0]?.Data?.Name == "New Material";
            if (!skipExisting) newList.AddRange(current);
            newList.AddRange(refs);
            _model.MaterialReferences = newList;
            _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
        }

        partial void ReplaceMaterialsInModel(IList<Material> mats)
        {
            if (_model == null) return;
            var newList = mats != null ? new List<Material>(mats) : new List<Material>();
            if (newList.Count == 0) newList.Add(null);
            _model.Materials = newList;
            _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
        }

        partial void ReplaceMaterialRefsInModel(IList<MaterialReference> refs)
        {
            if (_model == null) return;
            var newList = refs != null ? new List<MaterialReference>(refs) : new List<MaterialReference>();
            if (newList.Count == 0) newList.Add(new MaterialReference());
            _model.MaterialReferences = newList;
            _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
        }

        // ================================================================
        // MeshContext作成コールバック
        // ================================================================

        partial void OnMeshContextCreatedAsNew(MeshObject obj, string name)
        {
            var mc = new MeshContext
            {
                Name              = name,
                MeshObject        = obj,
                UnityMesh         = obj?.ToUnityMesh(),
                OriginalPositions = obj?.Positions != null
                    ? (Vector3[])obj.Positions.Clone()
                    : null,
                ParentModelContext = _model,
            };
            _commandQueue?.Enqueue(new Poly_Ling.Commands.AddMeshContextCommand(mc, AddMeshContextWithUndo));
        }

        partial void OnMeshObjectCreatedAddToCurrent(MeshObject obj, string name)
        {
            var mc = _model?.FirstSelectedMeshContext;
            if (mc == null || obj == null) return;
            if (mc.MeshObject == null)
                mc.MeshObject = new MeshObject(mc.Name);

            int baseVertexIndex = mc.MeshObject.VertexCount;
            foreach (var vertex in obj.Vertices)
                mc.MeshObject.Vertices.Add(new Vertex(vertex.Position));
            foreach (var face in obj.Faces)
            {
                var newFace = new Face();
                newFace.VertexIndices = face.VertexIndices.Select(i => i + baseVertexIndex).ToList();
                newFace.UVIndices    = new System.Collections.Generic.List<int>(face.UVIndices);
                newFace.NormalIndices = new System.Collections.Generic.List<int>(face.NormalIndices);
                newFace.MaterialIndex = face.MaterialIndex;
                mc.MeshObject.Faces.Add(newFace);
            }
            _toolContext?.SyncMesh?.Invoke();
            OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // InitVertexOffsets（暫定実装 → 後でEditor側イベントで通知）
        // ================================================================

        partial void InitVertexOffsets(bool updateCamera)
        {
            // 実処理はEditor側のPolyLing.InitVertexOffsets()が担う
            // Coreは「更新が必要」イベントを発火してEditorに委譲する
            OnVertexOffsetsUpdateRequired?.Invoke(updateCamera);
        }

        public event System.Action<bool> OnVertexOffsetsUpdateRequired;
    }
}
