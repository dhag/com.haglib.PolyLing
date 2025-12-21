// Assets/Editor/UndoSystem/MeshEditor/MeshListRecords.cs
// メッシュリスト操作用のUndo記録
// v1.3: MeshListUndoContext削除、ModelContext統合

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Model;

namespace MeshFactory.UndoSystem
{
    // ============================================================
    // カメラスナップショット
    // ============================================================

    /// <summary>
    /// カメラ状態のスナップショット
    /// </summary>
    public struct CameraSnapshot
    {
        public float RotationX;
        public float RotationY;
        public float CameraDistance;
        public Vector3 CameraTarget;
    }

    // ============================================================
    // MeshContextスナップショット
    // ============================================================

    /// <summary>
    /// MeshContextの完全なスナップショット
    /// HideFlags.HideAndDontSave のオブジェクトを適切に処理
    /// </summary>
    public class MeshContextSnapshot
    {
        public string Name;
        public MeshData Data;                    // Clone
        public List<string> MaterialPaths;      // マテリアルはアセットパスで保持
        public List<Material> RuntimeMaterials; // ランタイム専用（アセット化されていないマテリアル）
        public int CurrentMaterialIndex;
        public ExportSettings ExportSettings;
        public Vector3[] OriginalPositions;

        // オブジェクト属性
        public SimpleMeshFactory.MeshType Type;
        public int ParentIndex;
        public int Depth;
        public bool IsVisible;
        public bool IsLocked;

        // ミラー設定
        public int MirrorType;
        public int MirrorAxis;
        public float MirrorDistance;

        /// <summary>
        /// MeshContextからスナップショットを作成
        /// </summary>
        public static MeshContextSnapshot Capture(SimpleMeshFactory.MeshContext meshContext)
        {
            if (meshContext == null) return null;

            var snapshot = new MeshContextSnapshot
            {
                Name = meshContext.Name,
                Data = meshContext.Data?.Clone(),
                MaterialPaths = new List<string>(),
                RuntimeMaterials = new List<Material>(),
                CurrentMaterialIndex = meshContext.CurrentMaterialIndex,
                ExportSettings = meshContext.ExportSettings != null ? new ExportSettings(meshContext.ExportSettings) : null,
                OriginalPositions = meshContext.OriginalPositions != null 
                    ? (Vector3[])meshContext.OriginalPositions.Clone() 
                    : null,
                // オブジェクト属性
                Type = meshContext.Type,
                ParentIndex = meshContext.ParentIndex,
                Depth = meshContext.Depth,
                IsVisible = meshContext.IsVisible,
                IsLocked = meshContext.IsLocked,
                // ミラー設定
                MirrorType = meshContext.MirrorType,
                MirrorAxis = meshContext.MirrorAxis,
                MirrorDistance = meshContext.MirrorDistance
            };

            // マテリアルを安全に保存
            if (meshContext.Materials != null)
            {
                foreach (var mat in meshContext.Materials)
                {
                    if (mat == null)
                    {
                        snapshot.MaterialPaths.Add(null);
                        snapshot.RuntimeMaterials.Add(null);
                    }
                    else if (UnityEditor.AssetDatabase.Contains(mat))
                    {
                        string path = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        snapshot.MaterialPaths.Add(path);
                        snapshot.RuntimeMaterials.Add(null);
                    }
                    else if ((mat.hideFlags & HideFlags.DontSaveInEditor) != 0)
                    {
                        snapshot.MaterialPaths.Add(null);
                        snapshot.RuntimeMaterials.Add(null);
                    }
                    else
                    {
                        snapshot.MaterialPaths.Add(null);
                        snapshot.RuntimeMaterials.Add(mat);
                    }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからMeshContextを復元
        /// </summary>
        public SimpleMeshFactory.MeshContext ToMeshContext()
        {
            var meshContext = new SimpleMeshFactory.MeshContext
            {
                Name = Name,
                Data = Data?.Clone(),
                Materials = new List<Material>(),
                CurrentMaterialIndex = CurrentMaterialIndex,
                ExportSettings = ExportSettings != null ? new ExportSettings(ExportSettings) : null,
                OriginalPositions = OriginalPositions != null 
                    ? (Vector3[])OriginalPositions.Clone() 
                    : null,
                // オブジェクト属性
                Type = Type,
                ParentIndex = ParentIndex,
                Depth = Depth,
                IsVisible = IsVisible,
                IsLocked = IsLocked,
                // ミラー設定
                MirrorType = MirrorType,
                MirrorAxis = MirrorAxis,
                MirrorDistance = MirrorDistance
            };

            // マテリアルを復元
            if (MaterialPaths != null)
            {
                for (int i = 0; i < MaterialPaths.Count; i++)
                {
                    Material mat = null;
                    string path = MaterialPaths[i];

                    if (!string.IsNullOrEmpty(path))
                    {
                        mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                    }
                    else if (RuntimeMaterials != null && i < RuntimeMaterials.Count)
                    {
                        mat = RuntimeMaterials[i];
                    }

                    meshContext.Materials.Add(mat);
                }
            }

            if (meshContext.Materials.Count == 0)
            {
                meshContext.Materials.Add(null);
            }

            if (meshContext.Data != null)
            {
                meshContext.UnityMesh = meshContext.Data.ToUnityMesh();
                meshContext.UnityMesh.name = Name;
                meshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            return meshContext;
        }
    }

    // ============================================================
    // メッシュリスト用Undo記録
    // ============================================================

    /// <summary>
    /// メッシュリスト用Undo記録の基底クラス
    /// </summary>
    public abstract class MeshListUndoRecord : IUndoRecord<ModelContext>
    {
        public UndoOperationInfo Info { get; set; }
        public abstract void Undo(ModelContext context);
        public abstract void Redo(ModelContext context);
    }

    /// <summary>
    /// メッシュリスト変更記録
    /// </summary>
    public class MeshListChangeRecord : MeshListUndoRecord
    {
        public List<(int Index, MeshContextSnapshot Snapshot)> RemovedMeshContexts = new List<(int, MeshContextSnapshot)>();
        public List<(int Index, MeshContextSnapshot Snapshot)> AddedMeshContexts = new List<(int, MeshContextSnapshot)>();

        [Obsolete("Use OldSelectedIndices instead")]
        public int OldSelectedIndex
        {
            get => OldSelectedIndices.Count > 0 ? OldSelectedIndices.Min() : -1;
            set { OldSelectedIndices.Clear(); if (value >= 0) OldSelectedIndices.Add(value); }
        }

        [Obsolete("Use NewSelectedIndices instead")]
        public int NewSelectedIndex
        {
            get => NewSelectedIndices.Count > 0 ? NewSelectedIndices.Min() : -1;
            set { NewSelectedIndices.Clear(); if (value >= 0) NewSelectedIndices.Add(value); }
        }

        public HashSet<int> OldSelectedIndices = new HashSet<int>();
        public HashSet<int> NewSelectedIndices = new HashSet<int>();
        
        /// <summary>変更前のカメラ状態</summary>
        public CameraSnapshot? OldCameraState;
        
        /// <summary>変更後のカメラ状態</summary>
        public CameraSnapshot? NewCameraState;

        public override void Undo(ModelContext ctx)
        {
            // 追加されたものを削除
            foreach (var (index, _) in AddedMeshContexts.OrderByDescending(e => e.Index))
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    var mc = ctx.MeshContextList[index];
                    if (mc.UnityMesh != null) UnityEngine.Object.DestroyImmediate(mc.UnityMesh);
                    ctx.MeshContextList.RemoveAt(index);
                }
            }

            // 削除されたものを復元
            foreach (var (index, snapshot) in RemovedMeshContexts.OrderBy(e => e.Index))
            {
                var mc = snapshot.ToMeshContext();
                ctx.MeshContextList.Insert(Mathf.Clamp(index, 0, ctx.MeshContextList.Count), mc);
            }

            ctx.SelectedIndices = new HashSet<int>(OldSelectedIndices);
            ctx.ValidateSelection();
            
            // カメラ状態を復元
            if (OldCameraState.HasValue)
            {
                Debug.Log($"[MeshListChangeRecord.Undo] Restoring camera: target={OldCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(OldCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            Debug.Log("[MeshListChangeRecord.Redo] *** CALLED ***");
            
            // 削除されたものを削除
            foreach (var (index, _) in RemovedMeshContexts.OrderByDescending(e => e.Index))
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    var mc = ctx.MeshContextList[index];
                    if (mc.UnityMesh != null) UnityEngine.Object.DestroyImmediate(mc.UnityMesh);
                    ctx.MeshContextList.RemoveAt(index);
                }
            }

            // 追加されたものを復元
            foreach (var (index, snapshot) in AddedMeshContexts.OrderBy(e => e.Index))
            {
                var mc = snapshot.ToMeshContext();
                ctx.MeshContextList.Insert(Mathf.Clamp(index, 0, ctx.MeshContextList.Count), mc);
            }

            ctx.SelectedIndices = new HashSet<int>(NewSelectedIndices);
            ctx.ValidateSelection();
            
            // カメラ状態を復元
            if (NewCameraState.HasValue)
            {
                Debug.Log($"[MeshListChangeRecord.Redo] Restoring camera: target={NewCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(NewCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え
            ctx.OnFocusMeshListRequested?.Invoke();
        }
    }

    /// <summary>
    /// メッシュ選択変更記録
    /// </summary>
    public class MeshSelectionChangeRecord : MeshListUndoRecord
    {
        public HashSet<int> OldSelectedIndices;
        public HashSet<int> NewSelectedIndices;
        public CameraSnapshot? OldCameraState;
        public CameraSnapshot? NewCameraState;

        public MeshSelectionChangeRecord(HashSet<int> oldSelection, HashSet<int> newSelection)
        {
            OldSelectedIndices = new HashSet<int>(oldSelection ?? new HashSet<int>());
            NewSelectedIndices = new HashSet<int>(newSelection ?? new HashSet<int>());
        }

        public MeshSelectionChangeRecord(
            HashSet<int> oldSelection, 
            HashSet<int> newSelection,
            CameraSnapshot? oldCamera,
            CameraSnapshot? newCamera)
        {
            OldSelectedIndices = new HashSet<int>(oldSelection ?? new HashSet<int>());
            NewSelectedIndices = new HashSet<int>(newSelection ?? new HashSet<int>());
            OldCameraState = oldCamera;
            NewCameraState = newCamera;
        }

        public override void Undo(ModelContext ctx)
        {
            ctx.SelectedIndices = new HashSet<int>(OldSelectedIndices);
            ctx.ValidateSelection();
            
            Debug.Log($"[MeshSelectionChangeRecord.Undo] Before OnCameraRestoreRequested");
            if (OldCameraState.HasValue)
            {
                Debug.Log($"[MeshSelectionChangeRecord.Undo] Restoring camera: target={OldCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(OldCameraState.Value);
            }
            
            Debug.Log($"[MeshSelectionChangeRecord.Undo] Before OnListChanged");
            ctx.OnListChanged?.Invoke();
            Debug.Log($"[MeshSelectionChangeRecord.Undo] After OnListChanged");
            
            // MeshListStackにフォーカスを切り替え（Redo時に正しいスタックで実行されるように）
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            Debug.Log("[MeshSelectionChangeRecord.Redo] *** CALLED ***");
            ctx.SelectedIndices = new HashSet<int>(NewSelectedIndices);
            ctx.ValidateSelection();
            
            Debug.Log($"[MeshSelectionChangeRecord.Redo] NewCameraState.HasValue={NewCameraState.HasValue}");
            if (NewCameraState.HasValue)
            {
                Debug.Log($"[MeshSelectionChangeRecord.Redo] Restoring camera: dist={NewCameraState.Value.CameraDistance}, target={NewCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(NewCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え（次のUndo時に正しいスタックで実行されるように）
            ctx.OnFocusMeshListRequested?.Invoke();
        }
    }
}
