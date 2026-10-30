// Assets/Editor/UndoSystem/MeshEditor/ModelUndoRecords.cs
// モデル操作のUndo記録（Phase 3）
// - モデル追加/削除/切り替え/名前変更

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.UndoSystem
{
    // ================================================================
    // ModelContextスナップショット
    // ================================================================

    /// <summary>
    /// ModelContext全体のスナップショット
    /// v2.0: カテゴリ別選択インデックス対応
    /// </summary>
    public class ModelContextSnapshot
    {
        public string Name;
        public List<MeshContextSnapshot> MeshSnapshots;
        
        // カテゴリ別選択インデックス（v2.0）- 選択順序を保持
        public List<int> SelectedDrawableMeshIndices;
        public List<int> SelectedBoneIndices;
        public List<int> SelectedMorphIndices;
        
        // 後方互換用（廃止予定）
        public int SelectedMeshIndex
        {
            get
            {
                // 全カテゴリ中の先頭インデックスを返す
                if (SelectedDrawableMeshIndices != null && SelectedDrawableMeshIndices.Count > 0) return SelectedDrawableMeshIndices[0];
                if (SelectedBoneIndices != null && SelectedBoneIndices.Count > 0) return SelectedBoneIndices[0];
                if (SelectedMorphIndices != null && SelectedMorphIndices.Count > 0) return SelectedMorphIndices[0];
                return -1;
            }
            set
            {
                // 後方互換: 単一値をメッシュカテゴリに設定
                SelectedDrawableMeshIndices = value >= 0 ? new List<int> { value } : new List<int>();
                SelectedBoneIndices = new List<int>();
                SelectedMorphIndices = new List<int>();
            }
        }

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
                // カテゴリ別選択インデックス（v2.0）
                SelectedDrawableMeshIndices = new List<int>(model.SelectedDrawableMeshIndices),
                SelectedBoneIndices = new List<int>(model.SelectedBoneIndices),
                SelectedMorphIndices = new List<int>(model.SelectedMorphIndices),
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

            // メッシュ復元（一覧へ入れる道を通す。段 C）
            model.ReplaceMeshContexts(RestoreMeshContexts());

            // カテゴリ別選択インデックス復元（v2.0）
            if (SelectedDrawableMeshIndices != null)
                model.SelectedDrawableMeshIndices = new List<int>(SelectedDrawableMeshIndices);
            if (SelectedBoneIndices != null)
                model.SelectedBoneIndices = new List<int>(SelectedBoneIndices);
            if (SelectedMorphIndices != null)
                model.SelectedMorphIndices = new List<int>(SelectedMorphIndices);

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

            // メッシュ復元（一覧へ入れる道を通す。段 C）
            model.ReplaceMeshContexts(RestoreMeshContexts());

            // カテゴリ別選択インデックス復元（v2.0）
            model.ClearAllCategorySelection();
            if (SelectedDrawableMeshIndices != null)
                model.SelectedDrawableMeshIndices = new List<int>(SelectedDrawableMeshIndices);
            if (SelectedBoneIndices != null)
                model.SelectedBoneIndices = new List<int>(SelectedBoneIndices);
            if (SelectedMorphIndices != null)
                model.SelectedMorphIndices = new List<int>(SelectedMorphIndices);

            // マテリアル復元
            model.MaterialReferences = RestoreMaterialReferences(MaterialReferences);
            model.DefaultMaterialReferences = RestoreMaterialReferences(DefaultMaterialReferences);
            model.CurrentMaterialIndex = CurrentMaterialIndex;
            model.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
            model.AutoSetDefaultMaterials = AutoSetDefaultMaterials;
        }

        // ヘルパー：メッシュの復元（null は飛ばす）
        private List<MeshContext> RestoreMeshContexts()
        {
            var list = new List<MeshContext>();
            if (MeshSnapshots == null) return list;
            foreach (var meshSnapshot in MeshSnapshots)
            {
                var meshContext = meshSnapshot?.ToMeshContext();
                if (meshContext != null) list.Add(meshContext);
            }
            return list;
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
        //   追加・削除したモデルの実体そのもの（作り直し整備計画 6 章）。Undo・Redo で同じ実体を
        //   外し・入れ直す（ProjectContext.DetachModelAt は Clear しない）。以前は ModelContextSnapshot
        //   （名前・メッシュ・材質・選択だけ）から作り直していたので、オブジェクトグループ・表情・
        //   Humanoid などが落ち、頂点編集の記録・評価器の控えも古い実体を指していた。
        public ModelContext Model;

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
                Model = addedModel,
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
                Model = removedModel,
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

        /// <summary>持っている実体を一覧から外す（Clear しない）。実体で位置を引き、見つからなければ何もしない。</summary>
        private void DetachOwnModel(ProjectContext ctx, int fallbackIndex)
        {
            int index = Model != null ? ctx.IndexOf(Model) : -1;
            if (index < 0) return;
            ctx.DetachModelAt(index);
        }

        /// <summary>持っている実体を index へ入れ直してカレントにする。既に一覧にいれば入れない。</summary>
        private void InsertOwnModel(ProjectContext ctx, int index)
        {
            if (Model == null) return;
            if (ctx.IndexOf(Model) >= 0) return;
            if (index < 0) index = 0;
            if (index > ctx.ModelCount) index = ctx.ModelCount;
            ctx.InsertModel(index, Model);
            ctx.CurrentModelIndex = index;
        }

        public void Undo(ProjectContext ctx)
        {
            if (ctx == null) return;

            switch (Operation)
            {
                case ModelOperationType.Add:
                    // 追加されたモデルを外す（実体は持ったまま。Redo で同じ実体を入れ直す）
                    DetachOwnModel(ctx, NewModelIndex);
                    // 元のモデルに戻す
                    if (OldModelIndex >= 0 && OldModelIndex < ctx.ModelCount)
                    {
                        ctx.CurrentModelIndex = OldModelIndex;
                    }
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(OldCameraState);
                    break;

                case ModelOperationType.Remove:
                    // 削除したモデルを同じ実体のまま入れ直す
                    InsertOwnModel(ctx, OldModelIndex);
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
                    // 追加したモデルを同じ実体のまま入れ直す
                    InsertOwnModel(ctx, NewModelIndex);
                    // カメラ復元
                    ctx.OnCameraRestoreRequested?.Invoke(NewCameraState);
                    break;

                case ModelOperationType.Remove:
                    // モデルを再び外す（実体は持ったまま）
                    DetachOwnModel(ctx, OldModelIndex);
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

    // ================================================================
    // モデル置き換えUndoレコード
    // ================================================================

    /// <summary>
    /// モデルの置き換え（外部から届いたモデル・置換読込で差し替え）の Undo 記録。
    ///
    /// 【実体そのものを持つ】（作り直し整備計画 6 章）
    ///   置き換え前と後の ModelContext をそのまま持ち、Undo・Redo で同じ実体を入れ替える
    ///   （ProjectContext.ReplaceModelAt は外した実体を Clear しない）。以前は ModelDTO で控えて
    ///   戻すたびに組み直していたが、実体が替わるので頂点編集の記録・評価器の控えが古い実体を指し、
    ///   DTO に入らない SourceDocument・PmxModelInfo も落ちていた。
    /// </summary>
    public class ModelReplaceRecord : IUndoRecord<ProjectContext>
    {
        public UndoOperationInfo Info { get; set; }

        /// <summary>置き換えたモデルの位置（今の位置は実体で引き直す。見つからないときに使う）。</summary>
        public int ModelIndex;

        /// <summary>置き換え前のモデル。</summary>
        public ModelContext OldModel;

        /// <summary>置き換え後のモデル。</summary>
        public ModelContext NewModel;

        public ModelReplaceRecord(int modelIndex, ModelContext oldModel, ModelContext newModel)
        {
            ModelIndex = modelIndex;
            OldModel   = oldModel;
            NewModel   = newModel;
        }

        public void Undo(ProjectContext ctx) => Swap(ctx, NewModel, OldModel);

        public void Redo(ProjectContext ctx) => Swap(ctx, OldModel, NewModel);

        private void Swap(ProjectContext ctx, ModelContext from, ModelContext to)
        {
            if (ctx == null || to == null) return;
            int index = from != null ? ctx.IndexOf(from) : -1;
            if (index < 0) index = ModelIndex;
            if (index < 0 || index >= ctx.ModelCount) return;

            ctx.ReplaceModelAt(index, to);
            ctx.CurrentModelIndex = index;
            ctx.OnModelsChanged?.Invoke();
        }
    }
}
