// Assets/Editor/UndoSystem/MeshEditor/ModelUndoRecords.cs
// モデル操作のUndo記録（Phase 3）
// - モデル追加/削除/切り替え/名前変更

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;
using MeshFactory.Materials;

namespace MeshFactory.UndoSystem
{
    // ================================================================
    // ModelContextスナップショット
    // ================================================================

    /// <summary>
    /// ModelContext全体のスナップショット
    /// </summary>
    public class ModelContextSnapshot
    {
        public string Name;
        public List<MeshContextSnapshot> MeshSnapshots;
        public int SelectedMeshIndex;

        // マテリアル
        public List<MaterialReferenceSnapshot> MaterialReferences;
        public int CurrentMaterialIndex;
        public List<MaterialReferenceSnapshot> DefaultMaterialReferences;
        public int DefaultCurrentMaterialIndex;
        public bool AutoSetDefaultMaterials;

        /// <summary>
        /// ModelContextからスナップショットを作成
        /// </summary>
        public static ModelContextSnapshot Capture(ModelContext model)
        {
            if (model == null) return null;

            var snapshot = new ModelContextSnapshot
            {
                Name = model.Name,
                MeshSnapshots = new List<MeshContextSnapshot>(),
                SelectedMeshIndex = model.SelectedMeshContextIndex,
                CurrentMaterialIndex = model.CurrentMaterialIndex,
                DefaultCurrentMaterialIndex = model.DefaultCurrentMaterialIndex,
                AutoSetDefaultMaterials = model.AutoSetDefaultMaterials
            };

            // メッシュスナップショット
            if (model.MeshContextList != null)
            {
                foreach (var meshContext in model.MeshContextList)
                {
                    var meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                    if (meshSnapshot != null)
                    {
                        snapshot.MeshSnapshots.Add(meshSnapshot);
                    }
                }
            }

            // マテリアル参照スナップショット
            snapshot.MaterialReferences = CaptureMaterialReferences(model.MaterialReferences);
            snapshot.DefaultMaterialReferences = CaptureMaterialReferences(model.DefaultMaterialReferences);

            return snapshot;
        }

        /// <summary>
        /// スナップショットからModelContextを復元
        /// </summary>
        public ModelContext ToModelContext()
        {
            var model = new ModelContext(Name ?? "Model");

            // メッシュ復元
            if (MeshSnapshots != null)
            {
                foreach (var meshSnapshot in MeshSnapshots)
                {
                    var meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        meshContext.MaterialOwner = model;
                        model.MeshContextList.Add(meshContext);
                    }
                }
            }

            // 選択インデックス
            model.SelectedMeshContextIndex = SelectedMeshIndex;

            // マテリアル復元
            model.MaterialReferences = RestoreMaterialReferences(MaterialReferences);
            model.DefaultMaterialReferences = RestoreMaterialReferences(DefaultMaterialReferences);
            model.CurrentMaterialIndex = CurrentMaterialIndex;
            model.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
            model.AutoSetDefaultMaterials = AutoSetDefaultMaterials;

            return model;
        }

        /// <summary>
        /// 既存のModelContextにスナップショットを適用（置換）
        /// </summary>
        public void ApplyTo(ModelContext model, bool destroyOldResources = true)
        {
            if (model == null) return;

            // 古いリソースを破棄
            if (destroyOldResources)
            {
                model.Clear(true);
            }

            // 名前
            model.Name = Name;

            // メッシュ復元
            model.MeshContextList.Clear();
            if (MeshSnapshots != null)
            {
                foreach (var meshSnapshot in MeshSnapshots)
                {
                    var meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        meshContext.MaterialOwner = model;
                        model.MeshContextList.Add(meshContext);
                    }
                }
            }

            // 選択インデックス
            model.SelectedMeshContextIndex = SelectedMeshIndex;

            // マテリアル復元
            model.MaterialReferences = RestoreMaterialReferences(MaterialReferences);
            model.DefaultMaterialReferences = RestoreMaterialReferences(DefaultMaterialReferences);
            model.CurrentMaterialIndex = CurrentMaterialIndex;
            model.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
            model.AutoSetDefaultMaterials = AutoSetDefaultMaterials;
        }

        // ヘルパー：マテリアル参照のキャプチャ
        private static List<MaterialReferenceSnapshot> CaptureMaterialReferences(List<MaterialReference> refs)
        {
            if (refs == null) return null;

            var snapshots = new List<MaterialReferenceSnapshot>();
            foreach (var matRef in refs)
            {
                snapshots.Add(MaterialReferenceSnapshot.Capture(matRef));
            }
            return snapshots;
        }

        // ヘルパー：マテリアル参照の復元
        private static List<MaterialReference> RestoreMaterialReferences(List<MaterialReferenceSnapshot> snapshots)
        {
            if (snapshots == null) return new List<MaterialReference>();

            var refs = new List<MaterialReference>();
            foreach (var snapshot in snapshots)
            {
                refs.Add(snapshot?.ToMaterialReference() ?? new MaterialReference());
            }
            return refs;
        }
    }

    /// <summary>
    /// MaterialReferenceのスナップショット
    /// </summary>
    public class MaterialReferenceSnapshot
    {
        public string AssetPath;
        public MaterialData Data;

        public static MaterialReferenceSnapshot Capture(MaterialReference matRef)
        {
            if (matRef == null) return new MaterialReferenceSnapshot();

            return new MaterialReferenceSnapshot
            {
                AssetPath = matRef.AssetPath,
                Data = matRef.Data?.Clone()
            };
        }

        public MaterialReference ToMaterialReference()
        {
            return new MaterialReference
            {
                AssetPath = AssetPath,
                Data = Data?.Clone()
            };
        }
    }

    // ================================================================
    // モデル操作Undoレコード
    // ================================================================

    /// <summary>
    /// モデル操作の種類
    /// </summary>
    public enum ModelOperationType
    {
        Add,
        Remove,
        Switch,
        Rename,
        Reorder
    }

    /// <summary>
    /// モデル操作のUndo記録（ProjectContext用）
    /// </summary>
    public class ModelOperationRecord : IUndoRecord<ProjectContext>
    {
        public UndoOperationInfo Info { get; set; }

        public ModelOperationType Operation;

        // 共通
        public int OldModelIndex;
        public int NewModelIndex;
        public CameraSnapshot OldCameraState;
        public CameraSnapshot NewCameraState;

        // Add/Remove用
        public ModelContextSnapshot ModelSnapshot;

        // Rename用
        public string OldName;
        public string NewName;

        // ★追加: 現在編集中のメッシュの選択状態
        public MeshSelectionSnapshot OldMeshSelection;
        public MeshSelectionSnapshot NewMeshSelection;

        /// <summary>
        /// モデル追加の記録を作成
        /// </summary>
        public static ModelOperationRecord CreateAdd(
            int addedIndex,
            ModelContext addedModel,
            int oldModelIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera)
        {
            return new ModelOperationRecord
            {
                Operation = ModelOperationType.Add,
                NewModelIndex = addedIndex,
                OldModelIndex = oldModelIndex,
                ModelSnapshot = ModelContextSnapshot.Capture(addedModel),
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };
        }

        /// <summary>
        /// モデル削除の記録を作成
        /// </summary>
        public static ModelOperationRecord CreateRemove(
            int removedIndex,
            ModelContext removedModel,
            int newModelIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera)
        {
            return new ModelOperationRecord
            {
                Operation = ModelOperationType.Remove,
                OldModelIndex = removedIndex,
                NewModelIndex = newModelIndex,
                ModelSnapshot = ModelContextSnapshot.Capture(removedModel),
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };
        }

        /// <summary>
        /// モデル切り替えの記録を作成
        /// </summary>
        public static ModelOperationRecord CreateSwitch(
            int oldIndex,
            int newIndex,
            CameraSnapshot oldCamera,
            CameraSnapshot newCamera,
            MeshSelectionSnapshot oldSelection = null,
            MeshSelectionSnapshot newSelection = null)
        {
            return new ModelOperationRecord
            {
                Operation = ModelOperationType.Switch,
                OldModelIndex = oldIndex,
                NewModelIndex = newIndex,
                OldCameraState = oldCamera,
                NewCameraState = newCamera,
                OldMeshSelection = oldSelection,
                NewMeshSelection = newSelection
            };
        }

        /// <summary>
        /// モデル名変更の記録を作成
        /// </summary>
        public static ModelOperationRecord CreateRename(
            int modelIndex,
            string oldName,
            string newName)
        {
            return new ModelOperationRecord
            {
                Operation = ModelOperationType.Rename,
                OldModelIndex = modelIndex,
                NewModelIndex = modelIndex,
                OldName = oldName,
                NewName = newName
            };
        }

        public void Undo(ProjectContext ctx)
        {
            if (ctx == null) return;

            switch (Operation)
            {
                case ModelOperationType.Add:
                    // 追加されたモデルを削除
                    if (NewModelIndex >= 0 && NewModelIndex < ctx.ModelCount)
                    {
                        ctx.RemoveModelAt(NewModelIndex);
                    }
                    // 元のモデルに戻す
                    if (OldModelIndex >= 0 && OldModelIndex < ctx.ModelCount)
                    {
                        ctx.CurrentModelIndex = OldModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(OldCameraState);
                    break;

                case ModelOperationType.Remove:
                    // 削除されたモデルを復元
                    if (ModelSnapshot != null)
                    {
                        var restoredModel = ModelSnapshot.ToModelContext();
                        ctx.InsertModel(OldModelIndex, restoredModel);
                        ctx.CurrentModelIndex = OldModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(OldCameraState);
                    break;

                case ModelOperationType.Switch:
                    // 元のモデルに戻す
                    if (OldModelIndex >= 0 && OldModelIndex < ctx.ModelCount)
                    {
                        ctx.CurrentModelIndex = OldModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(OldCameraState);
                    // 選択状態復元
                    ctx.OnSelectionRestoreRequested?.Invoke(OldMeshSelection);
                    break;

                case ModelOperationType.Rename:
                    // 元の名前に戻す
                    var model = ctx.GetModel(OldModelIndex);
                    if (model != null)
                    {
                        model.Name = OldName;
                    }
                    break;
            }

            ctx.OnModelsChanged?.Invoke();
        }

        public void Redo(ProjectContext ctx)
        {
            if (ctx == null) return;

            switch (Operation)
            {
                case ModelOperationType.Add:
                    // モデルを再追加
                    if (ModelSnapshot != null)
                    {
                        var restoredModel = ModelSnapshot.ToModelContext();
                        ctx.InsertModel(NewModelIndex, restoredModel);
                        ctx.CurrentModelIndex = NewModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(NewCameraState);
                    break;

                case ModelOperationType.Remove:
                    // モデルを再削除
                    if (OldModelIndex >= 0 && OldModelIndex < ctx.ModelCount)
                    {
                        ctx.RemoveModelAt(OldModelIndex);
                    }
                    // 新しいモデルに切り替え
                    if (NewModelIndex >= 0 && NewModelIndex < ctx.ModelCount)
                    {
                        ctx.CurrentModelIndex = NewModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(NewCameraState);
                    break;

                case ModelOperationType.Switch:
                    // 新しいモデルに切り替え
                    if (NewModelIndex >= 0 && NewModelIndex < ctx.ModelCount)
                    {
                        ctx.CurrentModelIndex = NewModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(NewCameraState);
                    // 選択状態復元
                    ctx.OnSelectionRestoreRequested?.Invoke(NewMeshSelection);
                    break;

                case ModelOperationType.Rename:
                    // 新しい名前に変更
                    var model = ctx.GetModel(NewModelIndex);
                    if (model != null)
                    {
                        model.Name = NewName;
                    }
                    break;
            }

            ctx.OnModelsChanged?.Invoke();
        }
    }
}
