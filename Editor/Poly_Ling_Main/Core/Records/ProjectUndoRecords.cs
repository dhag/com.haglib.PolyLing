// ============================================================================
// ProjectUndoRecords.cs
// プロジェクトレベルのUndo/Redo記録システム
// ============================================================================
//
// 【Undo方針の説明】
//
// ■ 方針B（現在実装済み）: 全状態保存方式
//   - ProjectSnapshot で操作前後の全状態を保存
//   - 1回のUndoで全状態を復元
//   - メリット: 実装がシンプル、確実に元に戻る
//   - デメリット: メモリ使用量が大きい（全モデルのスナップショット）
//
//   【現在の実装フロー】
//   1. 操作前: beforeSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project)
//   2. 操作実行: _project.CreateNewModel() など
//   3. 操作後: afterSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project)
//   4. 記録: _undoController.RecordProjectOperation(ProjectRecord.CreateNewModel(before, after))
//   5. Undo時: snapshot.RestoreToProjectContext(_project) で全状態復元
//
// ■ 方針A（将来実装）: 個別操作記録方式
//   - 各操作を個別のレコードとして記録
//   - CreateModelRecord, DeleteModelRecord, SelectModelRecord など
//   - メリット: メモリ効率が良い、細かい粒度でUndo可能
//   - デメリット: 実装が複雑、操作間の依存関係の管理が必要
//
// ============================================================================
// 【方針Bから方針Aへの移行手順】
// ============================================================================
//
// Step 1: IProjectUndoRecord インターフェースを有効化
//   - 下記のコメントアウトされたインターフェースを有効化
//   - MeshUndoController の Stack<ProjectRecord> を Stack<IProjectUndoRecord> に変更
//
// Step 2: ProjectRecord を IProjectUndoRecord 実装に変更
//   ```csharp
//   public class ProjectRecord : IProjectUndoRecord
//   {
//       public void Undo(ProjectContext ctx) {
//           BeforeSnapshot.RestoreToProjectContext(ctx);
//       }
//       public void Redo(ProjectContext ctx) {
//           AfterSnapshot.RestoreToProjectContext(ctx);
//       }
//   }
//   ```
//   ※ この段階で既存コードは動作を維持
//
// Step 3: 個別操作レコードを段階的に実装
//
//   【CreateModelRecord の実装例】
//   ```csharp
//   public class CreateModelRecord : IProjectUndoRecord
//   {
//       public int CreatedModelIndex;           // 作成されたモデルのインデックス
//       public ModelSnapshot CreatedModelSnapshot;  // 作成されたモデルのスナップショット
//       public int PreviousModelIndex;          // 作成前のカレントモデルインデックス
//       
//       public string Description => $"Create Model '{CreatedModelSnapshot?.Name}'";
//       
//       public void Undo(ProjectContext ctx) {
//           // 作成したモデルを削除
//           ctx.RemoveModelAt(CreatedModelIndex);
//           // 以前のカレントモデルに戻す
//           if (PreviousModelIndex >= 0 && PreviousModelIndex < ctx.ModelCount)
//               ctx.CurrentModelIndex = PreviousModelIndex;
//       }
//       
//       public void Redo(ProjectContext ctx) {
//           // モデルを再作成
//           var model = CreatedModelSnapshot.ToModelContext();
//           ctx.InsertModelAt(CreatedModelIndex, model);
//           ctx.CurrentModelIndex = CreatedModelIndex;
//       }
//   }
//   ```
//
//   【DeleteModelRecord の実装例】
//   ```csharp
//   public class DeleteModelRecord : IProjectUndoRecord
//   {
//       public int DeletedModelIndex;
//       public ModelSnapshot DeletedModelSnapshot;
//       public int PreviousModelIndex;
//       public int NewModelIndex;  // 削除後のカレントインデックス
//       
//       public string Description => $"Delete Model '{DeletedModelSnapshot?.Name}'";
//       
//       public void Undo(ProjectContext ctx) {
//           var model = DeletedModelSnapshot.ToModelContext();
//           ctx.InsertModelAt(DeletedModelIndex, model);
//           ctx.CurrentModelIndex = PreviousModelIndex;
//       }
//       
//       public void Redo(ProjectContext ctx) {
//           ctx.RemoveModelAt(DeletedModelIndex);
//           ctx.CurrentModelIndex = NewModelIndex;
//       }
//   }
//   ```
//
//   【SelectModelRecord の実装例】（軽量）
//   ```csharp
//   public class SelectModelRecord : IProjectUndoRecord
//   {
//       public int OldIndex;
//       public int NewIndex;
//       
//       public string Description => $"Select Model {NewIndex}";
//       
//       public void Undo(ProjectContext ctx) => ctx.CurrentModelIndex = OldIndex;
//       public void Redo(ProjectContext ctx) => ctx.CurrentModelIndex = NewIndex;
//   }
//   ```
//
// Step 4: 呼び出し側の更新
//   - CreateNewModelWithUndo で CreateModelRecord を使用
//   - SelectModelWithUndo で SelectModelRecord を使用（軽量化）
//   - 方針Bの ProjectRecord は複合操作用として残す
//
// Step 5: ProjectContext に必要なメソッドを追加
//   - InsertModelAt(int index, ModelContext model)
//   - RemoveModelAt(int index)
//   ※ 現在は AddModel/RemoveModel のみ
//
// ============================================================================
// 【移行時の注意点】
// ============================================================================
//
// 1. 互換性維持
//    - 方針BのProjectRecordと方針Aの個別レコードは共存可能
//    - IProjectUndoRecord を介して統一的に扱える
//
// 2. 複合操作の扱い
//    - 「NewModelでインポート」= CreateModel + AddMeshes + SetMaterials
//    - 方針Aでは CompositeRecord でまとめるか、方針BのProjectRecordを使用
//
// 3. スナップショットの再利用
//    - ModelSnapshot, MeshContextSnapshot は両方針で共通利用
//    - 方針Aでも部分的なスナップショットとして使用
//
// 4. テスト
//    - 各レコードのUndo/Redoを単体テスト
//    - 複合操作のUndo/Redoを結合テスト
//    - メモリ使用量の比較
//
// ============================================================================
// 【共通基盤（B/A両方で使用）】
// ============================================================================
//
// - ProjectSnapshot: 全状態のスナップショット（方針Bで主に使用、方針Aでも複合操作用）
// - ModelSnapshot: モデル単位のスナップショット（両方針で使用）
// - MaterialReferenceSnapshot: マテリアル参照のスナップショット
// - MeshContextSnapshot: メッシュ単位のスナップショット（MeshListRecords.csで定義）
//
// ============================================================================

using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Model;
using Poly_Ling.Serialization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshEditor
{
    // ========================================================================
    // 【将来の方針A用】IProjectUndoRecord インターフェース
    // 現在は未使用。方針A移行時にコメントアウトを解除
    // ========================================================================
    /*
    public interface IProjectUndoRecord
    {
        /// <summary>
        /// Undo実行
        /// </summary>
        void Undo(ProjectContext context);
        
        /// <summary>
        /// Redo実行
        /// </summary>
        void Redo(ProjectContext context);
        
        /// <summary>
        /// 操作の説明（UI表示用）
        /// </summary>
        string Description { get; }
    }
    */

    /// <summary>
    /// モデル全体のスナップショット（メッシュリスト + マテリアル + エディタ状態）
    /// v1.1: カテゴリ別選択対応
    /// </summary>
    public class ModelSnapshot
    {
        public string Name;
        public List<MeshContextSnapshot> MeshSnapshots;
        public List<MaterialReferenceSnapshot> MaterialSnapshots;  // マテリアル参照のスナップショット
        
        // カテゴリ別選択インデックス (v1.1追加)
        public int SelectedMeshIndex;      // 選択中のメッシュインデックス (Mesh/BakedMirror)
        public int SelectedBoneIndex;      // 選択中のボーンインデックス
        public int SelectedMorphIndex;     // 選択中の頂点モーフインデックス
        
        public int CurrentMaterialIndex;  // 選択中のマテリアルインデックス
        public WorkPlaneDTO WorkPlane;
        public EditorStateDTO EditorState;

        /// <summary>
        /// メッシュコンテキストリストからスナップショットを作成
        /// </summary>
        public static ModelSnapshot Capture(
            List<MeshContext> meshContextList,
            string name = null,
            WorkPlaneContext workPlaneContext = null,
            EditorStateDTO editorState = null)
        {
            if (meshContextList == null) return null;

            ModelSnapshot snapshot = new ModelSnapshot
            {
                Name = name ?? "Model",
                MeshSnapshots = new List<MeshContextSnapshot>(),
                MaterialSnapshots = new List<MaterialReferenceSnapshot>(),
                EditorState = editorState != null ? CloneEditorState(editorState) : null
            };

            // 各メッシュのスナップショットを作成
            foreach (var meshContext in meshContextList)
            {
                MeshContextSnapshot meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                if (meshSnapshot != null)
                {
                    snapshot.MeshSnapshots.Add(meshSnapshot);
                }
            }

            // WorkPlaneのスナップショット
            if (workPlaneContext != null)
            {
                snapshot.WorkPlane = ModelSerializer.ToWorkPlaneData(workPlaneContext);
            }

            return snapshot;
        }

        /// <summary>
        /// ModelContextからスナップショットを作成（MaterialReferences含む）
        /// </summary>
        public static ModelSnapshot CaptureFromModelContext(ModelContext modelContext, WorkPlaneContext workPlaneContext = null, EditorStateDTO editorState = null)
        {
            if (modelContext == null) return null;

            ModelSnapshot snapshot = new ModelSnapshot
            {
                Name = modelContext.Name,
                MeshSnapshots = new List<MeshContextSnapshot>(),
                MaterialSnapshots = new List<MaterialReferenceSnapshot>(),
                // カテゴリ別選択 (v1.1)
                SelectedMeshIndex = modelContext.FirstMeshIndex,
                SelectedBoneIndex = modelContext.FirstBoneIndex,
                SelectedMorphIndex = modelContext.FirstMorphIndex,
                CurrentMaterialIndex = modelContext.CurrentMaterialIndex,
                EditorState = editorState != null ? CloneEditorState(editorState) : null
            };

            // 各メッシュのスナップショットを作成
            foreach (var meshContext in modelContext.MeshContextList)
            {
                MeshContextSnapshot meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                if (meshSnapshot != null)
                {
                    snapshot.MeshSnapshots.Add(meshSnapshot);
                }
            }

            // マテリアル参照のスナップショットを作成
            foreach (var matRef in modelContext.MaterialReferences)
            {
                snapshot.MaterialSnapshots.Add(MaterialReferenceSnapshot.Capture(matRef));
            }

            // WorkPlaneのスナップショット
            if (workPlaneContext != null)
            {
                snapshot.WorkPlane = ModelSerializer.ToWorkPlaneData(workPlaneContext);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからメッシュコンテキストリストを復元
        /// </summary>
        public List<MeshContext> ToMeshContextList()
        {
            var list = new List<MeshContext>();

            if (MeshSnapshots != null)
            {
                foreach (var meshSnapshot in MeshSnapshots)
                {
                    MeshContext meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        list.Add(meshContext);
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// スナップショットからModelContextを復元
        /// </summary>
        public ModelContext ToModelContext()
        {
            var model = new ModelContext(Name);

            // メッシュを復元
            if (MeshSnapshots != null)
            {
                foreach (var meshSnapshot in MeshSnapshots)
                {
                    MeshContext meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        model.Add(meshContext);
                    }
                }
            }

            // マテリアルを復元
            if (MaterialSnapshots != null && MaterialSnapshots.Count > 0)
            {
                var materialRefs = new List<MaterialReference>();
                foreach (var matSnapshot in MaterialSnapshots)
                {
                    materialRefs.Add(matSnapshot?.ToMaterialReference());
                }
                model.MaterialReferences = materialRefs;
            }

            // カテゴリ別選択状態を復元 (v1.1)
            if (SelectedMeshIndex >= 0) model.SelectDrawable(SelectedMeshIndex);
            if (SelectedBoneIndex >= 0) model.SelectBone(SelectedBoneIndex);
            if (SelectedMorphIndex >= 0) model.SelectMorph(SelectedMorphIndex);
            model.CurrentMaterialIndex = CurrentMaterialIndex;

            return model;
        }

        /// <summary>
        /// WorkPlaneを復元
        /// </summary>
        public void ApplyToWorkPlane(WorkPlaneContext workPlane)
        {
            if (WorkPlane != null && workPlane != null)
            {
                ModelSerializer.ApplyToWorkPlane(WorkPlane, workPlane);
            }
        }

        private static EditorStateDTO CloneEditorState(EditorStateDTO source)
        {
            if (source == null) return null;

            return new EditorStateDTO
            {
                rotationX = source.rotationX,
                rotationY = source.rotationY,
                cameraDistance = source.cameraDistance,
                cameraTarget = source.cameraTarget != null 
                    ? (float[])source.cameraTarget.Clone() 
                    : null,
                showWireframe = source.showWireframe,
                showVertices = source.showVertices,
                vertexEditMode = source.vertexEditMode,
                currentToolName = source.currentToolName,
                // カテゴリ別選択 (v1.1)
                selectedMeshIndex = source.selectedMeshIndex,
                selectedBoneIndex = source.selectedBoneIndex,
                selectedVertexMorphIndex = source.selectedVertexMorphIndex,
                // 座標系設定
                coordinateScale = source.coordinateScale,
                pmxFlipZ = source.pmxFlipZ,
                mqoFlipZ = source.mqoFlipZ,
                mqoPmxRatio = source.mqoPmxRatio
            };
        }
    }

    /// <summary>
    /// マテリアル参照のスナップショット
    /// </summary>
    public class MaterialReferenceSnapshot
    {
        public string Name;
        public string AssetPath;  // マテリアルアセットのパス
        public MaterialData Data;  // マテリアルデータのクローン

        /// <summary>
        /// MaterialReferenceからスナップショットを作成
        /// </summary>
        public static MaterialReferenceSnapshot Capture(MaterialReference matRef)
        {
            if (matRef == null) return null;

            return new MaterialReferenceSnapshot
            {
                Name = matRef.Name,
                AssetPath = matRef.AssetPath,
                Data = matRef.Data?.Clone()
            };
        }

        /// <summary>
        /// スナップショットからMaterialReferenceを復元
        /// </summary>
        public MaterialReference ToMaterialReference()
        {
            // MaterialReference.Clone()と同じ形式で復元
            // Materialはキャッシュなので、AssetPathから自動ロードされる
            return new MaterialReference
            {
                AssetPath = AssetPath,
                Data = Data?.Clone()
            };
        }
    }

    /// <summary>
    /// プロジェクト全体のスナップショット（複数モデル対応）
    /// </summary>
    public class ProjectSnapshot
    {
        public string Version;
        public string Name;
        public string CreatedAt;
        public string ModifiedAt;
        public List<ModelSnapshot> ModelSnapshots;
        public int CurrentModelIndex;  // 選択中のモデルインデックス

        /// <summary>
        /// ProjectContextからスナップショットを作成（複数モデル対応）
        /// </summary>
        public static ProjectSnapshot CaptureFromProjectContext(
            ProjectContext project,
            WorkPlaneContext workPlane = null,
            EditorStateDTO editorState = null)
        {
            if (project == null) return null;

            ProjectSnapshot snapshot = new ProjectSnapshot
            {
                Version = "1.0",
                Name = project.Name ?? "Project",
                CreatedAt = project.CreatedAt.ToString("o"),
                ModifiedAt = project.ModifiedAt.ToString("o"),
                ModelSnapshots = new List<ModelSnapshot>(),
                CurrentModelIndex = project.CurrentModelIndex
            };

            // 各モデルのスナップショットを作成
            foreach (var model in project.Models)
            {
                var modelSnapshot = ModelSnapshot.CaptureFromModelContext(model, workPlane, editorState);
                if (modelSnapshot != null)
                {
                    snapshot.ModelSnapshots.Add(modelSnapshot);
                }
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからProjectContextを復元
        /// </summary>
        public void RestoreToProjectContext(ProjectContext project, Action<ModelContext> onModelRestored = null)
        {
            if (project == null) return;

            // 既存モデルをクリア
            project.Clear(true);

            // 各モデルを復元
            if (ModelSnapshots != null)
            {
                foreach (var modelSnapshot in ModelSnapshots)
                {
                    var model = modelSnapshot?.ToModelContext();
                    if (model != null)
                    {
                        project.AddModel(model);
                        onModelRestored?.Invoke(model);
                    }
                }
            }

            // カレントモデルを設定
            if (CurrentModelIndex >= 0 && CurrentModelIndex < project.ModelCount)
            {
                project.CurrentModelIndex = CurrentModelIndex;
            }
            else if (project.ModelCount > 0)
            {
                project.CurrentModelIndex = 0;
            }
        }

        /// <summary>
        /// プロジェクトデータからスナップショットを作成
        /// </summary>
        public static ProjectSnapshot Capture(ProjectDTO projectDTO)
        {
            if (projectDTO == null) return null;

            ProjectSnapshot snapshot = new ProjectSnapshot
            {
                Version = projectDTO.version,
                Name = projectDTO.name,
                CreatedAt = projectDTO.createdAt,
                ModifiedAt = projectDTO.modifiedAt,
                ModelSnapshots = new List<ModelSnapshot>(),
                CurrentModelIndex = 0
            };

            // 各モデルのスナップショットを作成
            // 注: ProjectDataはシリアライズ用データなので、
            //     実際の使用時はModelContextから直接キャプチャする方が望ましい
            if (projectDTO.models != null)
            {
                foreach (var modelDTO in projectDTO.models)
                {
                    ModelSnapshot modelSnapshot = CaptureFromModelDTO(modelDTO);
                    if (modelSnapshot != null)
                    {
                        snapshot.ModelSnapshots.Add(modelSnapshot);
                    }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 現在のエディタ状態からスナップショットを作成（単一モデル用）
        /// </summary>
        public static ProjectSnapshot CaptureFromEditor(
            List<MeshContext> meshContextList,
            string projectName,
            WorkPlaneContext workPlane = null,
            EditorStateDTO editorStateDTO = null)
        {
            ProjectSnapshot snapshot = new ProjectSnapshot
            {
                Version = "1.0",
                Name = projectName ?? "Project",
                CreatedAt = DateTime.Now.ToString("o"),
                ModifiedAt = DateTime.Now.ToString("o"),
                ModelSnapshots = new List<ModelSnapshot>()
            };

            var modelSnapshot = ModelSnapshot.Capture(
                meshContextList,
                projectName,
                workPlane,
                editorStateDTO);

            if (modelSnapshot != null)
            {
                snapshot.ModelSnapshots.Add(modelSnapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからProjectDataを復元
        /// </summary>
        public ProjectDTO ToProjectData()
        {
            var projectDTO = new ProjectDTO
            {
                version = Version ?? "1.0",
                name = Name ?? "Project",
                createdAt = CreatedAt,
                modifiedAt = ModifiedAt,
                models = new List<ModelDTO>()
            };

            if (ModelSnapshots != null)
            {
                foreach (var modelSnapshot in ModelSnapshots)
                {
                    var modelDTO = ToModelData(modelSnapshot);
                    if (modelDTO != null)
                    {
                        projectDTO.models.Add(modelDTO);
                    }
                }
            }

            return projectDTO;
        }

        /// <summary>
        /// 最初のモデルのメッシュコンテキストリストを復元（単一モデル用）
        /// </summary>
        public List<MeshContext> ToMeshContextList()
        {
            if (ModelSnapshots == null || ModelSnapshots.Count == 0)
            {
                return new List<MeshContext>();
            }

            return ModelSnapshots[0].ToMeshContextList();
        }

        private static ModelSnapshot CaptureFromModelDTO(ModelDTO modelDTO)
        {
            if (modelDTO == null) return null;

            ModelSnapshot snapshot = new ModelSnapshot
            {
                Name = modelDTO.name,
                MeshSnapshots = new List<MeshContextSnapshot>(),
                WorkPlane = modelDTO.workPlane,
                EditorState = modelDTO.editorStateDTO
            };

            // MeshContextDataからMeshContextSnapshotへ変換
            if (modelDTO.meshDTOList != null)
            {
                foreach (var meshContextData in modelDTO.meshDTOList)
                {
                    var meshContext = ModelSerializer.ToMeshContext(meshContextData);
                    if (meshContext != null)
                    {
                        var meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                        if (meshSnapshot != null)
                        {
                            snapshot.MeshSnapshots.Add(meshSnapshot);
                        }
                    }
                }
            }

            return snapshot;
        }

        private static ModelDTO ToModelData(ModelSnapshot modelSnapshot)
        {
            if (modelSnapshot == null) return null;

            var modelDTO = new ModelDTO
            {
                name = modelSnapshot.Name,
                meshDTOList = new List<MeshDTO>(),
                workPlane = modelSnapshot.WorkPlane,
                editorStateDTO = modelSnapshot.EditorState
            };

            if (modelSnapshot.MeshSnapshots != null)
            {
                foreach (var meshSnapshot in modelSnapshot.MeshSnapshots)
                {
                    MeshContext meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        var meshContextData = ModelSerializer.FromMeshContext(meshContext, null);
                        if (meshContextData != null)
                        {
                            modelDTO.meshDTOList.Add(meshContextData);
                        }
                    }
                }
            }

            return modelDTO;
        }
    }

    /// <summary>
    /// メッシュリスト変更の記録（メッシュ追加/削除用）
    /// </summary>
    public class MeshListRecord
    {
        public enum OperationType
        {
            Add,
            Remove,
            Reorder,
            Replace
        }

        public OperationType Operation;
        public int Index;
        public MeshContextSnapshot BeforeSnapshot;  // Remove/Replace時の元データ
        public MeshContextSnapshot AfterSnapshot;   // Add/Replace時の新データ
        public int[] ReorderMap;                    // Reorder時のインデックスマップ

        /// <summary>
        /// メッシュ追加の記録を作成
        /// </summary>
        public static MeshListRecord CreateAdd(int index, MeshContext meshContext)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Add,
                Index = index,
                AfterSnapshot = MeshContextSnapshot.Capture(meshContext)
            };
        }

        /// <summary>
        /// メッシュ削除の記録を作成
        /// </summary>
        public static MeshListRecord CreateRemove(int index, MeshContext meshContext)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Remove,
                Index = index,
                BeforeSnapshot = MeshContextSnapshot.Capture(meshContext)
            };
        }

        /// <summary>
        /// メッシュ置換の記録を作成
        /// </summary>
        public static MeshListRecord CreateReplace(
            int index,
            MeshContext before,
            MeshContext after)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Replace,
                Index = index,
                BeforeSnapshot = MeshContextSnapshot.Capture(before),
                AfterSnapshot = MeshContextSnapshot.Capture(after)
            };
        }

        /// <summary>
        /// メッシュ順序変更の記録を作成
        /// </summary>
        public static MeshListRecord CreateReorder(int[] beforeToAfterMap)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Reorder,
                ReorderMap = (int[])beforeToAfterMap.Clone()
            };
        }
    }

    // ========================================================================
    // ProjectRecord（方針B: 全状態保存方式）
    // ========================================================================
    // 【方針A移行時の変更点】
    // - IProjectUndoRecord インターフェースを実装
    // - Undo()/Redo() メソッドを追加
    // - ProjectFullRestoreRecord にリネーム（方針Aの個別レコードと区別）
    //
    // 【移行後のクラス構成】
    // IProjectUndoRecord (interface)
    //   ├── ProjectFullRestoreRecord (方針B: 全状態復元)
    //   ├── CreateModelRecord (方針A: モデル作成)
    //   ├── DeleteModelRecord (方針A: モデル削除)
    //   ├── SelectModelRecord (方針A: モデル選択)
    //   └── RenameModelRecord (方針A: モデル名変更)
    // ========================================================================

    /// <summary>
    /// プロジェクト操作の記録（読み込み/新規作成/モデル操作用）
    /// 方針B: 操作前後の全状態をスナップショットとして保存
    /// </summary>
    /// <remarks>
    /// 【Undo実行時】BeforeSnapshot を使って復元
    /// 【Redo実行時】AfterSnapshot を使って復元
    /// 
    /// 方針A移行時は IProjectUndoRecord を実装し、
    /// Undo(ctx) { BeforeSnapshot.RestoreToProjectContext(ctx); }
    /// Redo(ctx) { AfterSnapshot.RestoreToProjectContext(ctx); }
    /// として動作させる
    /// </remarks>
    public class ProjectRecord
    {
        public enum OperationType
        {
            New,
            Load,
            Import,
            NewModel,      // 新規モデル作成（複数モデル対応）
            SelectModel    // モデル選択変更
        }

        public OperationType Operation;
        public ProjectSnapshot BeforeSnapshot;
        public ProjectSnapshot AfterSnapshot;
        public string FilePath;  // Load/Import時のファイルパス

        /// <summary>
        /// 新規作成の記録を作成
        /// </summary>
        public static ProjectRecord CreateNew(ProjectSnapshot before, ProjectSnapshot after)
        {
            return new ProjectRecord
            {
                Operation = OperationType.New,
                BeforeSnapshot = before,
                AfterSnapshot = after
            };
        }

        /// <summary>
        /// 読み込みの記録を作成
        /// </summary>
        public static ProjectRecord CreateLoad(ProjectSnapshot before, ProjectSnapshot after, string filePath)
        {
            return new ProjectRecord
            {
                Operation = OperationType.Load,
                BeforeSnapshot = before,
                AfterSnapshot = after,
                FilePath = filePath
            };
        }

        /// <summary>
        /// インポートの記録を作成
        /// </summary>
        public static ProjectRecord CreateImport(ProjectSnapshot before, ProjectSnapshot after, string filePath)
        {
            return new ProjectRecord
            {
                Operation = OperationType.Import,
                BeforeSnapshot = before,
                AfterSnapshot = after,
                FilePath = filePath
            };
        }

        /// <summary>
        /// 新規モデル作成の記録を作成
        /// </summary>
        public static ProjectRecord CreateNewModel(ProjectSnapshot before, ProjectSnapshot after)
        {
            return new ProjectRecord
            {
                Operation = OperationType.NewModel,
                BeforeSnapshot = before,
                AfterSnapshot = after
            };
        }

        /// <summary>
        /// モデル選択変更の記録を作成
        /// </summary>
        public static ProjectRecord CreateSelectModel(ProjectSnapshot before, ProjectSnapshot after)
        {
            return new ProjectRecord
            {
                Operation = OperationType.SelectModel,
                BeforeSnapshot = before,
                AfterSnapshot = after
            };
        }
    }
}
