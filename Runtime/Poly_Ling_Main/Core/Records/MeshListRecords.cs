// Assets/Editor/Poly_Ling/UndoSystem/MeshListRecords.cs
// メッシュリスト操作用のUndo記録
// v1.3: MeshListUndoContext削除、ModelContext統合
// v1.4: MeshContextSnapshot に選択状態を追加（Phase 1）
// v1.5: MeshContextSnapshot にモーフデータを追加（Phase Morph）
// v1.6: MeshContextSnapshot にBonePoseDataを追加（Phase BonePose）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Selection;

namespace Poly_Ling.UndoSystem
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
    /// Phase 1: 選択状態を追加
    /// Phase Morph: モーフデータを追加
    /// </summary>
    public class MeshContextSnapshot
    {
        public string Name;
        public MeshObject Data;                    // Clone
        public List<string> MaterialPaths;      // マテリアルはアセットパスで保持
        public List<Material> RuntimeMaterials; // ランタイム専用（アセット化されていないマテリアル）
        public int CurrentMaterialIndex;
        public BoneTransform BoneTransform;
        public Vector3[] OriginalPositions;

        // オブジェクト属性
        public MeshType Type;
        public int ParentIndex;
        public int Depth;
        public bool IsVisible;
        public bool IsLocked;
        public bool IsFolding;

        // ミラー設定
        public int MirrorType;
        public int MirrorAxis;
        public float MirrorDistance;
        public int MirrorMaterialOffset;

        // ベイクドミラー設定
        public int BakedMirrorSourceIndex;
        public bool HasBakedMirrorChild;

        // ================================================================
        // 選択状態（Phase 1追加）
        // ================================================================

        /// <summary>選択状態スナップショット</summary>
        public MeshSelectionSnapshot Selection;

        /// <summary>選択セット（Phase 9追加）</summary>
        public List<PartsSelectionSet> PartsSelectionSetList;

        // ================================================================
        // モーフデータ（Phase Morph追加）
        // ================================================================

        /// <summary>モーフ基準データ</summary>
        public MorphBaseData MorphBaseData;

        /// <summary>モーフ親メッシュのマスターインデックス</summary>
        public int MorphParentIndex;

        /// <summary>エクスポートから除外するか</summary>
        public bool ExcludeFromExport;

        /// <summary>アーマチャ生成時ボーン生成スキップ</summary>
        public bool IgnorePoseInArmature;

        // ================================================================
        // BonePoseData（Phase BonePose追加）
        // ================================================================

        /// <summary>ボーンポーズデータ</summary>
        public BonePoseData BonePoseData;

        /// <summary>BindPose（スキニング基準行列）</summary>
        public Matrix4x4 BindPose;

        /// <summary>
        /// MeshContextからスナップショットを作成
        /// </summary>
        public static MeshContextSnapshot Capture(MeshContext meshContext)
        {
            if (meshContext == null) return null;

            MeshContextSnapshot snapshot = new MeshContextSnapshot
            {
                Name = meshContext.Name,
                Data = meshContext.MeshObject?.Clone(),
                MaterialPaths = new List<string>(),
                RuntimeMaterials = new List<Material>(),
                CurrentMaterialIndex = meshContext.CurrentMaterialIndex,
                BoneTransform = meshContext.BoneTransform != null ? new BoneTransform(meshContext.BoneTransform) : null,
                OriginalPositions = meshContext.OriginalPositions != null 
                    ? (Vector3[])meshContext.OriginalPositions.Clone() 
                    : null,
                // オブジェクト属性
                Type = meshContext.Type,
                ParentIndex = meshContext.ParentIndex,
                Depth = meshContext.Depth,
                IsVisible = meshContext.IsVisible,
                IsLocked = meshContext.IsLocked,
                IsFolding = meshContext.IsFolding,
                // ミラー設定
                MirrorType = meshContext.MirrorType,
                MirrorAxis = meshContext.MirrorAxis,
                MirrorDistance = meshContext.MirrorDistance,
                MirrorMaterialOffset = meshContext.MirrorMaterialOffset,
                // ベイクドミラー設定
                BakedMirrorSourceIndex = meshContext.BakedMirrorSourceIndex,
                HasBakedMirrorChild = meshContext.HasBakedMirrorChild,
                // 選択状態（Phase 1追加）
                Selection = meshContext.CaptureSelection(),
                // 選択セット（Phase 9追加）
                PartsSelectionSetList = meshContext.PartsSelectionSetList?.Select(s => s.Clone()).ToList()
                                ?? new List<PartsSelectionSet>(),
                // モーフデータ（Phase Morph追加）
                MorphBaseData = meshContext.MorphBaseData?.Clone(),
                MorphParentIndex = meshContext.MorphParentIndex,
                ExcludeFromExport = meshContext.ExcludeFromExport,
                IgnorePoseInArmature = meshContext.IgnorePoseInArmature,
                // BonePoseData（Phase BonePose追加）
                BonePoseData = meshContext.BonePoseData?.Clone(),
                // BindPose（スキニング基準行列）
                BindPose = meshContext.BindPose
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
                    else if (PLEditorBridge.I.ContainsAsset(mat))
                    {
                        string path = PLEditorBridge.I.GetAssetPath(mat);
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
        public MeshContext ToMeshContext()
        {
            var meshContext = new MeshContext
            {
                MeshObject = Data?.Clone(),
                // 注: Materials はModelContextで管理されるため、ここでは設定しない
                // MaterialOwnerはModelContext.Add()時に設定される
                BoneTransform = BoneTransform != null ? new BoneTransform(BoneTransform) : null,
                OriginalPositions = OriginalPositions != null 
                    ? (Vector3[])OriginalPositions.Clone() 
                    : null,
                // オブジェクト属性
                Type = Type,
                ParentIndex = ParentIndex,
                Depth = Depth,
                IsVisible = IsVisible,
                IsLocked = IsLocked,
                IsFolding = IsFolding,
                // ミラー設定
                MirrorType = MirrorType,
                MirrorAxis = MirrorAxis,
                MirrorDistance = MirrorDistance,
                MirrorMaterialOffset = MirrorMaterialOffset,
                // ベイクドミラー設定
                BakedMirrorSourceIndex = BakedMirrorSourceIndex,
                HasBakedMirrorChild = HasBakedMirrorChild
            };

            // 名前を設定（MeshObjectに反映）
            meshContext.Name = Name;

            // 注: マテリアル情報はModelContextで一元管理されるため、
            // MeshContextSnapshotからのマテリアル復元は行わない
            // MaterialPaths, RuntimeMaterials, CurrentMaterialIndex は
            // ModelContextのスナップショットで管理される

            if (meshContext.MeshObject != null)
            {
                meshContext.UnityMesh = meshContext.MeshObject.ToUnityMeshShared();
                meshContext.UnityMesh.name = Name;
                meshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            // 選択状態を復元（Phase 1追加）
            if (Selection != null)
            {
                meshContext.RestoreSelection(Selection);
            }

            // 選択セットを復元（Phase 9追加）
            meshContext.PartsSelectionSetList = PartsSelectionSetList?.Select(s => s.Clone()).ToList()
                                        ?? new List<PartsSelectionSet>();

            // モーフデータを復元（Phase Morph追加）
            meshContext.MorphBaseData = MorphBaseData?.Clone();
            meshContext.MorphParentIndex = MorphParentIndex;
            meshContext.ExcludeFromExport = ExcludeFromExport;
            meshContext.IgnorePoseInArmature = IgnorePoseInArmature;

            // BonePoseData復元（Phase BonePose追加）
            meshContext.BonePoseData = BonePoseData?.Clone();

            // BindPose復元（スキニング基準行列）
            meshContext.BindPose = BindPose;

            return meshContext;
        }

        /// <summary>
        /// スナップショットのクローンを作成
        /// </summary>
        public MeshContextSnapshot Clone()
        {
            return Capture(ToMeshContext());
        }
    }

    // ============================================================
    // MeshListUndoRecord 基底クラス
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

        /// <summary>変更前のマテリアルリスト</summary>
        public List<Material> OldMaterials;
        
        /// <summary>変更後のマテリアルリスト</summary>
        public List<Material> NewMaterials;
        
        /// <summary>変更前のカレントマテリアルインデックス</summary>
        public int OldCurrentMaterialIndex;
        
        /// <summary>変更後のカレントマテリアルインデックス</summary>
        public int NewCurrentMaterialIndex;

        [Obsolete("Use OldSelectedIndices instead")]
        public int OldSelectedIndex
        {
            get => OldSelectedIndices.Count > 0 ? OldSelectedIndices[0] : -1;
            set { OldSelectedIndices.Clear(); if (value >= 0) OldSelectedIndices.Add(value); }
        }

        [Obsolete("Use NewSelectedIndices instead")]
        public int NewSelectedIndex
        {
            get => NewSelectedIndices.Count > 0 ? NewSelectedIndices[0] : -1;
            set { NewSelectedIndices.Clear(); if (value >= 0) NewSelectedIndices.Add(value); }
        }

        public List<int> OldSelectedIndices = new List<int>();
        public List<int> NewSelectedIndices = new List<int>();
        
        /// <summary>変更前のカメラ状態</summary>
        public CameraSnapshot? OldCameraState;
        
        /// <summary>変更後のカメラ状態</summary>
        public CameraSnapshot? NewCameraState;

        public override void Undo(ModelContext ctx)
        {
            // マテリアルを復元
            if (OldMaterials != null)
            {
                ctx.Materials = new List<Material>(OldMaterials);
                ctx.CurrentMaterialIndex = OldCurrentMaterialIndex;
            }
            
            // 追加されたものを削除（ModelContext API使用: MorphExpression調整あり、選択調整なし）
            foreach (var (index, _) in AddedMeshContexts.OrderByDescending(e => e.Index))
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    var mc = ctx.MeshContextList[index];
                    if (mc.UnityMesh != null) UnityEngine.Object.DestroyImmediate(mc.UnityMesh);
                    ctx.RemoveAt(index, adjustSelection: false);
                }
            }

            // 削除されたものを復元（ModelContext API使用: MorphExpression調整あり、選択調整なし）
            foreach (var (index, snapshot) in RemovedMeshContexts.OrderBy(e => e.Index))
            {
                var mc = snapshot.ToMeshContext();
                mc.ParentModelContext = ctx;
                ctx.Insert(Mathf.Clamp(index, 0, ctx.MeshContextList.Count), mc, adjustSelection: false);
            }

            // 選択状態を復元
            ctx.RestoreSelectionFromIndices(OldSelectedIndices);
            ctx.ValidateSelection();
            
            // カメラ状態を復元
            if (OldCameraState.HasValue)
            {
                ctx.OnCameraRestoreRequested?.Invoke(OldCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            // マテリアルを復元
            if (NewMaterials != null)
            {
                ctx.Materials = new List<Material>(NewMaterials);
                ctx.CurrentMaterialIndex = NewCurrentMaterialIndex;
            }
            
            // 削除されたものを削除（ModelContext API使用: MorphExpression調整あり、選択調整なし）
            foreach (var (index, _) in RemovedMeshContexts.OrderByDescending(e => e.Index))
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    var mc = ctx.MeshContextList[index];
                    if (mc.UnityMesh != null) UnityEngine.Object.DestroyImmediate(mc.UnityMesh);
                    ctx.RemoveAt(index, adjustSelection: false);
                }
            }

            // 追加されたものを復元（ModelContext API使用: MorphExpression調整あり、選択調整なし）
            foreach (var (index, snapshot) in AddedMeshContexts.OrderBy(e => e.Index))
            {
                var mc = snapshot.ToMeshContext();
                mc.ParentModelContext = ctx;
                ctx.Insert(Mathf.Clamp(index, 0, ctx.MeshContextList.Count), mc, adjustSelection: false);
            }

            // 選択状態を復元
            ctx.RestoreSelectionFromIndices(NewSelectedIndices);
            ctx.ValidateSelection();
            
            // カメラ状態を復元
            if (NewCameraState.HasValue)
            {
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
        public List<int> OldSelectedIndices;
        public List<int> NewSelectedIndices;
        public CameraSnapshot? OldCameraState;
        public CameraSnapshot? NewCameraState;

        public MeshSelectionChangeRecord(List<int> oldSelection, List<int> newSelection)
        {
            OldSelectedIndices = new List<int>(oldSelection ?? new List<int>());
            NewSelectedIndices = new List<int>(newSelection ?? new List<int>());
        }

        public MeshSelectionChangeRecord(
            List<int> oldSelection, 
            List<int> newSelection,
            CameraSnapshot? oldCamera,
            CameraSnapshot? newCamera)
        {
            OldSelectedIndices = new List<int>(oldSelection ?? new List<int>());
            NewSelectedIndices = new List<int>(newSelection ?? new List<int>());
            OldCameraState = oldCamera;
            NewCameraState = newCamera;
        }

        public override void Undo(ModelContext ctx)
        {
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] START. OldSelectedIndices={string.Join(",", OldSelectedIndices)}, CurrentIndex={ctx.PrimarySelectedMeshContextIndex}");
            // v2.0: 新API使用
            ctx.RestoreSelectionFromIndices(OldSelectedIndices);
            ctx.ValidateSelection();
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] After ValidateSelection. NewIndex={ctx.PrimarySelectedMeshContextIndex}");
            
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] Before OnCameraRestoreRequested");
            if (OldCameraState.HasValue)
            {
                // Debug.Log($"[MeshSelectionChangeRecord.Undo] Restoring camera: target={OldCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(OldCameraState.Value);
            }
            
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] Before OnListChanged");
            ctx.OnListChanged?.Invoke();
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] After OnListChanged");
            
            // MeshListStackにフォーカスを切り替え（Redo時に正しいスタックで実行されるように）
            ctx.OnFocusMeshListRequested?.Invoke();
            // Debug.Log($"[MeshSelectionChangeRecord.Undo] END");
        }

        public override void Redo(ModelContext ctx)
        {
            // Debug.Log("[MeshSelectionChangeRecord.Redo] *** CALLED ***");
            // v2.0: 新API使用
            ctx.RestoreSelectionFromIndices(NewSelectedIndices);
            ctx.ValidateSelection();
            
            // Debug.Log($"[MeshSelectionChangeRecord.Redo] NewCameraState.HasValue={NewCameraState.HasValue}");
            if (NewCameraState.HasValue)
            {
                // Debug.Log($"[MeshSelectionChangeRecord.Redo] Restoring camera: dist={NewCameraState.Value.CameraDistance}, target={NewCameraState.Value.CameraTarget}");
                ctx.OnCameraRestoreRequested?.Invoke(NewCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え（次のUndo時に正しいスタックで実行されるように）
            ctx.OnFocusMeshListRequested?.Invoke();
        }
    }

    // ============================================================
    // メッシュ属性一括変更レコード
    // ============================================================

    /// <summary>
    /// 複数メッシュの属性変更を一括で記録するレコード
    /// UpdateMeshAttributesコマンドで使用
    /// </summary>
    public class MeshAttributesBatchChangeRecord : MeshListUndoRecord
    {
        /// <summary>変更前の値</summary>
        public List<MeshAttributeChange> OldValues { get; set; }
        
        /// <summary>変更後の値</summary>
        public List<MeshAttributeChange> NewValues { get; set; }

        public MeshAttributesBatchChangeRecord() { }

        public MeshAttributesBatchChangeRecord(List<MeshAttributeChange> oldValues, List<MeshAttributeChange> newValues)
        {
            OldValues = oldValues;
            NewValues = newValues;
        }

        public override void Undo(ModelContext ctx)
        {
            ApplyValues(ctx, OldValues);
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            ApplyValues(ctx, NewValues);
            ctx.OnListChanged?.Invoke();
        }

        private void ApplyValues(ModelContext ctx, List<MeshAttributeChange> values)
        {
            if (ctx == null || values == null) return;

            foreach (var change in values)
            {
                if (change.Index < 0 || change.Index >= ctx.MeshContextCount) continue;
                
                var meshContext = ctx.GetMeshContext(change.Index);
                if (meshContext == null) continue;

                if (change.IsVisible.HasValue) meshContext.IsVisible = change.IsVisible.Value;
                if (change.IsLocked.HasValue) meshContext.IsLocked = change.IsLocked.Value;
                if (change.MirrorType.HasValue) meshContext.MirrorType = change.MirrorType.Value;
                if (change.Name != null) meshContext.Name = change.Name;
                if (change.IgnorePoseInArmature.HasValue) meshContext.IgnorePoseInArmature = change.IgnorePoseInArmature.Value;
            }
        }

        public override string ToString()
        {
            return $"MeshAttributesBatchChange: {NewValues?.Count ?? 0} changes";
        }
    }

    /// <summary>
    /// BonePose変更記録
    /// ボーンのPreBindPose/Layer/Active/BindPose変更をUndo/Redo
    /// </summary>
    public class BonePoseChangeRecord : MeshListUndoRecord
    {
        /// <summary>対象MeshContextのMasterIndex</summary>
        public int MasterIndex;

        /// <summary>変更前のBonePoseDataスナップショット（null = BonePoseData未存在）</summary>
        public BonePoseDataSnapshot? OldSnapshot;

        /// <summary>変更後のBonePoseDataスナップショット</summary>
        public BonePoseDataSnapshot? NewSnapshot;

        /// <summary>変更前のBindPose（BakePose時のみ使用、それ以外はnull）</summary>
        public Matrix4x4? OldBindPose;

        /// <summary>変更後のBindPose（BakePose時のみ使用、それ以外はnull）</summary>
        public Matrix4x4? NewBindPose;

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldSnapshot, OldBindPose);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewSnapshot, NewBindPose);
        }

        private void Apply(ModelContext ctx, BonePoseDataSnapshot? snapshot, Matrix4x4? bindPose)
        {
            if (ctx == null) return;
            if (MasterIndex < 0 || MasterIndex >= ctx.MeshContextCount) return;

            var mc = ctx.GetMeshContext(MasterIndex);
            if (mc == null) return;

            if (snapshot.HasValue)
            {
                if (mc.BonePoseData == null)
                    mc.BonePoseData = new BonePoseData();
                mc.BonePoseData.ApplySnapshot(snapshot);
            }
            else
            {
                mc.BonePoseData = null;
            }

            if (bindPose.HasValue)
                mc.BindPose = bindPose.Value;

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"BonePoseChange: MasterIndex={MasterIndex}";
        }
    }

    /// <summary>
    /// 複数ボーンのBonePoseData変更を一括記録するレコード
    /// </summary>
    public class MultiBonePoseChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public BonePoseDataSnapshot? OldSnapshot;
            public BonePoseDataSnapshot? NewSnapshot;
            public Matrix4x4? OldBindPose;
            public Matrix4x4? NewBindPose;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                ApplyEntry(ctx, e.MasterIndex, e.OldSnapshot, e.OldBindPose);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                ApplyEntry(ctx, e.MasterIndex, e.NewSnapshot, e.NewBindPose);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        private static void ApplyEntry(ModelContext ctx, int masterIndex,
            BonePoseDataSnapshot? snapshot, Matrix4x4? bindPose)
        {
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;
            var mc = ctx.GetMeshContext(masterIndex);
            if (mc == null) return;

            if (snapshot.HasValue)
            {
                if (mc.BonePoseData == null)
                    mc.BonePoseData = new BonePoseData();
                mc.BonePoseData.ApplySnapshot(snapshot);
            }
            else
            {
                mc.BonePoseData = null;
            }

            if (bindPose.HasValue)
                mc.BindPose = bindPose.Value;
        }

        public override string ToString()
        {
            return $"MultiBonePoseChange: {Entries.Count} bones";
        }
    }

    // ============================================================
    // モーフ変換記録（Phase MorphEditor追加）
    // ============================================================

    /// <summary>
    /// 複数MeshContextのBoneTransform一括変更Undo記録（簡易モード用）
    /// </summary>
    public class MultiBoneTransformChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public BoneTransformSnapshot OldSnapshot;
            public BoneTransformSnapshot NewSnapshot;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries) ApplyEntry(ctx, e.MasterIndex, e.OldSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries) ApplyEntry(ctx, e.MasterIndex, e.NewSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        private static void ApplyEntry(ModelContext ctx, int masterIndex, BoneTransformSnapshot snapshot)
        {
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;
            var mc = ctx.GetMeshContext(masterIndex);
            if (mc?.BoneTransform == null) return;
            mc.BoneTransform.ApplySnapshot(snapshot);
        }

        public override string ToString() => $"MultiBoneTransformChange: {Entries.Count} entries";
    }

    /// <summary>
    /// ピボット移動の複合 Undo レコード。
    /// 頂点位置（VertexMove）と BoneTransform（BoneTransformSnapshot）を同時に記録する。
    /// Undo/Redo 時は両方を一緒に復元するため、見た目上ピボットだけが動いたように扱える。
    /// </summary>
    public class PivotMoveRecord : MeshListUndoRecord
    {
        public int MasterIndex;

        // 頂点位置
        public int[]      VertexIndices;
        public Vector3[]  OldVertexPositions;
        public Vector3[]  NewVertexPositions;

        // BoneTransform
        public BoneTransformSnapshot OldBoneTransform;
        public BoneTransformSnapshot NewBoneTransform;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            Apply(ctx, OldVertexPositions, OldBoneTransform);
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            Apply(ctx, NewVertexPositions, NewBoneTransform);
        }

        private void Apply(ModelContext ctx, Vector3[] positions, BoneTransformSnapshot boneSnap)
        {
            if (MasterIndex < 0 || MasterIndex >= ctx.MeshContextCount) return;
            var mc = ctx.GetMeshContext(MasterIndex);
            if (mc?.MeshObject == null) return;

            // 頂点位置を復元
            for (int i = 0; i < VertexIndices.Length; i++)
            {
                int vi = VertexIndices[i];
                if (vi >= 0 && vi < mc.MeshObject.VertexCount)
                    mc.MeshObject.Vertices[vi].Position = positions[i];
            }
            mc.MeshObject.InvalidatePositionCache();
            mc.OriginalPositions = (Vector3[])mc.MeshObject.Positions.Clone();

            // BoneTransform を復元
            if (mc.BoneTransform != null)
                mc.BoneTransform.ApplySnapshot(boneSnap);

            ctx.ComputeWorldMatrices();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString() => $"PivotMove: MasterIndex={MasterIndex}";
    }

    /// Type, MorphBaseData, MorphParentIndex, ExcludeFromExport を保存/復元
    /// </summary>
    public class MorphConversionRecord : MeshListUndoRecord
    {
        /// <summary>対象MeshContextのMasterIndex</summary>
        public int MasterIndex;

        /// <summary>変更前のType</summary>
        public MeshType OldType;
        /// <summary>変更後のType</summary>
        public MeshType NewType;

        /// <summary>変更前のMorphBaseData</summary>
        public MorphBaseData OldMorphBaseData;
        /// <summary>変更後のMorphBaseData</summary>
        public MorphBaseData NewMorphBaseData;

        /// <summary>変更前のMorphParentIndex</summary>
        public int OldMorphParentIndex;
        /// <summary>変更後のMorphParentIndex</summary>
        public int NewMorphParentIndex;

        /// <summary>変更前の名前</summary>
        public string OldName;
        /// <summary>変更後の名前</summary>
        public string NewName;

        /// <summary>変更前のExcludeFromExport</summary>
        public bool OldExcludeFromExport;
        /// <summary>変更後のExcludeFromExport</summary>
        public bool NewExcludeFromExport;

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldType, OldMorphBaseData, OldMorphParentIndex, OldName, OldExcludeFromExport);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewType, NewMorphBaseData, NewMorphParentIndex, NewName, NewExcludeFromExport);
        }

        private void Apply(ModelContext ctx, MeshType type, MorphBaseData morphData,
            int morphParentIndex, string name, bool excludeFromExport)
        {
            if (ctx == null) return;
            if (MasterIndex < 0 || MasterIndex >= ctx.MeshContextCount) return;

            var mc = ctx.GetMeshContext(MasterIndex);
            if (mc == null) return;

            mc.Type = type;
            if (mc.MeshObject != null) mc.MeshObject.Type = type;
            mc.MorphBaseData = morphData?.Clone();
            mc.MorphParentIndex = morphParentIndex;
            mc.Name = name;
            mc.ExcludeFromExport = excludeFromExport;

            ctx.TypedIndices?.Invalidate();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MorphConversion: [{MasterIndex}] {OldType} → {NewType}";
        }
    }

    // ============================================================
    // モーフセット変更記録（Phase MorphEditor追加）
    // ============================================================

    /// <summary>
    /// モーフセットの追加/削除のUndo記録
    /// </summary>
    public class MorphExpressionChangeRecord : MeshListUndoRecord
    {
        /// <summary>追加されたモーフセット（Undo時に削除）</summary>
        public MorphExpression AddExpression;
        /// <summary>追加位置</summary>
        public int AddedIndex;

        /// <summary>削除されたモーフセット（Undo時に復元）</summary>
        public MorphExpression RemovedExpression;
        /// <summary>削除位置</summary>
        public int RemovedIndex;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;

            // 追加されたものを削除
            if (AddExpression != null && AddedIndex >= 0 && AddedIndex < ctx.MorphExpressions.Count)
            {
                ctx.MorphExpressions.RemoveAt(AddedIndex);
            }

            // 削除されたものを復元
            if (RemovedExpression != null)
            {
                int idx = Mathf.Clamp(RemovedIndex, 0, ctx.MorphExpressions.Count);
                ctx.MorphExpressions.Insert(idx, RemovedExpression.Clone());
            }

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;

            // 削除されたものを削除
            if (RemovedExpression != null && RemovedIndex >= 0 && RemovedIndex < ctx.MorphExpressions.Count)
            {
                ctx.MorphExpressions.RemoveAt(RemovedIndex);
            }

            // 追加されたものを追加
            if (AddExpression != null)
            {
                int idx = Mathf.Clamp(AddedIndex, 0, ctx.MorphExpressions.Count);
                ctx.MorphExpressions.Insert(idx, AddExpression.Clone());
            }

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            if (AddExpression != null) return $"MorphExpressionAdd: {AddExpression.Name}";
            if (RemovedExpression != null) return $"MorphExpressionRemove: {RemovedExpression.Name}";
            return "MorphExpressionChange";
        }
    }

    // ============================================================
    // モーフセット編集記録（Phase MorphEditor追加）
    // ============================================================

    /// <summary>
    /// モーフセット内のエントリ/ウェイト/属性変更のUndo記録
    /// Before/Afterスナップショット方式で全変更を網羅
    /// </summary>
    public class MorphExpressionEditRecord : MeshListUndoRecord
    {
        /// <summary>対象セットのインデックス</summary>
        public int SetIndex;

        /// <summary>変更前のスナップショット</summary>
        public MorphExpression OldSnapshot;

        /// <summary>変更後のスナップショット</summary>
        public MorphExpression NewSnapshot;

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldSnapshot);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewSnapshot);
        }

        private void Apply(ModelContext ctx, MorphExpression snapshot)
        {
            if (ctx == null || snapshot == null) return;
            if (SetIndex < 0 || SetIndex >= ctx.MorphExpressions.Count) return;

            ctx.MorphExpressions[SetIndex] = snapshot.Clone();

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MorphExpressionEdit: [{SetIndex}] {OldSnapshot?.Name ?? "?"}";
        }
    }

    // ============================================================
    // モーフセット一括変更記録（CSV読み込み用、Phase MorphEditor追加）
    // ============================================================

    /// <summary>
    /// 全モーフセットリストの一括置換（CSV読み込み等）のUndo記録
    /// </summary>
    public class MorphExpressionListReplaceRecord : MeshListUndoRecord
    {
        /// <summary>変更前の全セットリスト</summary>
        public List<MorphExpression> OldSets;

        /// <summary>変更後の全セットリスト</summary>
        public List<MorphExpression> NewSets;

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldSets);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewSets);
        }

        private void Apply(ModelContext ctx, List<MorphExpression> sets)
        {
            if (ctx == null || sets == null) return;

            ctx.MorphExpressions = sets.Select(s => s.Clone()).ToList();

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MorphExpressionListReplace: {OldSets?.Count ?? 0} → {NewSets?.Count ?? 0} sets";
        }
    }

    // ================================================================
    // MeshFilter → Skinned 変換 Undo レコード
    // ================================================================

    /// <summary>
    /// MeshFilter → Skinned 変換の Undo/Redo レコード。
    /// 変換前後の MeshContextList 全体をディープコピーで保持する。
    /// HierarchyParentIndex など MeshContextSnapshot に含まれないフィールドも
    /// MeshContext.Clone() 経由で保存されるため、完全な復元が可能。
    /// </summary>
    public class MeshFilterToSkinnedRecord : MeshListUndoRecord
    {
        /// <summary>変換前の MeshContextList（ディープコピー）</summary>
        public List<MeshContext> BeforeList;
        /// <summary>変換後の MeshContextList（ディープコピー）</summary>
        public List<MeshContext> AfterList;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null || BeforeList == null) return;
            RestoreList(ctx, BeforeList);
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null || AfterList == null) return;
            RestoreList(ctx, AfterList);
        }

        private static void RestoreList(ModelContext ctx, List<MeshContext> source)
        {
            ctx.MeshContextList.Clear();
            foreach (var mc in source)
            {
                // Clone して追加（Undo/Redo を繰り返しても独立した状態を保つ）
                var clone = CloneMeshContext(mc);
                clone.ParentModelContext = ctx;
                ctx.MeshContextList.Add(clone);
            }
            ctx.InvalidateTypedIndices();
            ctx.ClearMeshSelection();
            ctx.ClearBoneSelection();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        /// <summary>
        /// MeshContext を HierarchyParentIndex を含めてディープコピーする。
        /// </summary>
        public static MeshContext CloneMeshContext(MeshContext src)
        {
            if (src == null) return null;
            var dst = new MeshContext
            {
                Name                   = src.Name,
                MeshObject             = src.MeshObject?.Clone(),
                BoneTransform          = src.BoneTransform != null ? new BoneTransform(src.BoneTransform) : null,
                OriginalPositions      = src.OriginalPositions != null ? (Vector3[])src.OriginalPositions.Clone() : null,
                ParentIndex            = src.ParentIndex,
                HierarchyParentIndex   = src.HierarchyParentIndex,
                Depth                  = src.Depth,
                IsVisible              = src.IsVisible,
                IsLocked               = src.IsLocked,
                IsFolding              = src.IsFolding,
                MirrorType             = src.MirrorType,
                MirrorAxis             = src.MirrorAxis,
                MirrorDistance         = src.MirrorDistance,
                MirrorMaterialOffset   = src.MirrorMaterialOffset,
                BakedMirrorSourceIndex = src.BakedMirrorSourceIndex,
                HasBakedMirrorChild    = src.HasBakedMirrorChild,
                MorphParentIndex       = src.MorphParentIndex,
                ExcludeFromExport      = src.ExcludeFromExport,
                IgnorePoseInArmature   = src.IgnorePoseInArmature,
                BindPose               = src.BindPose,
                BonePoseData           = src.BonePoseData?.Clone(),
                MorphBaseData          = src.MorphBaseData?.Clone(),
            };
            // UnityMesh は MeshObject から再生成
            if (dst.MeshObject != null && dst.MeshObject.VertexCount > 0)
            {
                dst.UnityMesh = dst.MeshObject.ToUnityMesh();
                if (dst.UnityMesh != null) dst.UnityMesh.name = src.Name;
            }
            // マテリアル
            if (src.Materials != null)
                foreach (var m in src.Materials) dst.Materials.Add(m);
            dst.CurrentMaterialIndex = src.CurrentMaterialIndex;
            return dst;
        }

        /// <summary>
        /// ModelContext の MeshContextList 全体をディープコピーしてリストを返す。
        /// </summary>
        public static List<MeshContext> CaptureList(ModelContext model)
        {
            var list = new List<MeshContext>(model.MeshContextCount);
            for (int i = 0; i < model.MeshContextCount; i++)
                list.Add(CloneMeshContext(model.GetMeshContext(i)));
            return list;
        }

        public override string ToString() => "MeshFilter → Skinned 変換";
    }

    // ================================================================
    // モデルブレンド Undo レコード
    // ================================================================

    /// <summary>
    /// モデルブレンド適用の Undo/Redo レコード。
    /// クローンモデルの描画メッシュ頂点位置（before/after）を保持する。
    /// MeshListStack のコンテキストをクローン ModelContext に設定した状態で記録すること。
    /// </summary>
    public class ModelBlendRecord : MeshListUndoRecord
    {
        /// <summary>masterIndex → 適用前の頂点位置配列</summary>
        public Dictionary<int, Vector3[]> BeforePositions;
        /// <summary>masterIndex → 適用後の頂点位置配列</summary>
        public Dictionary<int, Vector3[]> AfterPositions;

        public override void Undo(ModelContext ctx) => Apply(ctx, BeforePositions);
        public override void Redo(ModelContext ctx) => Apply(ctx, AfterPositions);

        private static void Apply(ModelContext ctx, Dictionary<int, Vector3[]> positions)
        {
            if (ctx == null || positions == null) return;
            foreach (var kv in positions)
            {
                var mc = ctx.GetMeshContext(kv.Key);
                if (mc?.MeshObject == null) continue;
                var verts = mc.MeshObject.Vertices;
                var pos   = kv.Value;
                int n     = System.Math.Min(pos.Length, verts.Count);
                for (int i = 0; i < n; i++)
                    verts[i].Position = pos[i];
                mc.MeshObject.InvalidatePositionCache();
                if (mc.UnityMesh != null)
                {
                    var up = mc.MeshObject.Positions;
                    if (up != null && up.Length == mc.UnityMesh.vertexCount)
                        mc.UnityMesh.vertices = up;
                }
            }
            ctx.OnListChanged?.Invoke();
        }

        /// <summary>
        /// ModelContext の全描画メッシュ頂点位置をスナップショットする。
        /// </summary>
        public static Dictionary<int, Vector3[]> CapturePositions(ModelContext model)
        {
            var result = new Dictionary<int, Vector3[]>();
            if (model == null) return result;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                if (mc.Type == MeshType.Bone || mc.Type == MeshType.Morph) continue;
                var pos = new Vector3[mc.MeshObject.VertexCount];
                for (int v = 0; v < pos.Length; v++)
                    pos[v] = mc.MeshObject.Vertices[v].Position;
                result[i] = pos;
            }
            return result;
        }

        public override string ToString() => "モデルブレンド適用";
    }

    // ================================================================
    // モーフ作成 Undo レコード
    // ================================================================

    /// <summary>
    /// モーフ作成操作の Undo/Redo レコード。
    /// MeshContextList 全体 + MorphExpressions リストの両方を保持する。
    /// </summary>
    public class MorphCreateRecord : MeshListUndoRecord
    {
        public List<MeshContext>     BeforeList;
        public List<MeshContext>     AfterList;
        public List<MorphExpression> BeforeExpressions;
        public List<MorphExpression> AfterExpressions;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            RestoreState(ctx, BeforeList, BeforeExpressions);
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            RestoreState(ctx, AfterList, AfterExpressions);
        }

        private static void RestoreState(
            ModelContext ctx,
            List<MeshContext> meshList,
            List<MorphExpression> expressions)
        {
            // MeshContextList を復元
            if (meshList != null)
            {
                ctx.MeshContextList.Clear();
                foreach (var mc in meshList)
                {
                    var clone = MeshFilterToSkinnedRecord.CloneMeshContext(mc);
                    clone.ParentModelContext = ctx;
                    ctx.MeshContextList.Add(clone);
                }
                ctx.InvalidateTypedIndices();
            }

            // MorphExpressions を復元
            if (expressions != null)
            {
                ctx.MorphExpressions.Clear();
                foreach (var e in expressions)
                    ctx.MorphExpressions.Add(e.Clone());
            }

            ctx.ClearMeshSelection();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString() => "モーフ作成";
    }
}
