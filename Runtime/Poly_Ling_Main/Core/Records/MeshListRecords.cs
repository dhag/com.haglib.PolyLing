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
using Poly_Ling.Materials;

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
        // 【写す項目】（作り直し整備計画 段 A）
        //   以前はここに欄を並べて独自に写していた。MeshContextCloneOps と写す項目が食い違い
        //   （こちらは PMXMaterialNames・BoneModelRotation・WorldMatrix を落とし、向こうは
        //   作業軸の値を落としていた）、欄を足すたびに 2 か所へ手で足す形になっていた。
        //   中身は MeshContextCloneOps.Clone(StateSnapshot) の控えに一本化する。
        //   要素の選択だけは CloneOps が写さない（新品と同じにする）ので別に控える。

        /// <summary>中身の控え（MeshContextCloneOps.Clone(StateSnapshot)）。モデルには入れない。</summary>
        public MeshContext State;

        public string Name => State?.Name;
        public ulong ObjectId => State?.ObjectId ?? 0UL;

        public List<string> MaterialPaths;      // マテリアルはアセットパスで保持
        public List<Material> RuntimeMaterials; // ランタイム専用（アセット化されていないマテリアル）
        public int CurrentMaterialIndex;

        /// <summary>選択状態スナップショット</summary>
        public MeshSelectionSnapshot Selection;

        /// <summary>
        /// MeshContextからスナップショットを作成
        /// </summary>
        public static MeshContextSnapshot Capture(MeshContext meshContext)
        {
            if (meshContext == null) return null;

            MeshContextSnapshot snapshot = new MeshContextSnapshot
            {
                State = Poly_Ling.Ops.MeshContextCloneOps.Clone(
                    meshContext, Poly_Ling.Ops.MeshContextCloneKind.StateSnapshot),
                MaterialPaths = new List<string>(),
                RuntimeMaterials = new List<Material>(),
                CurrentMaterialIndex = meshContext.CurrentMaterialIndex,
                // 選択状態（CloneOps は写さない）
                Selection = meshContext.CaptureSelection(),
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
            // 中身は CloneOps の決まりで写す（復元は同一オブジェクトなので ID を維持する）。
            // 注: Materials は ModelContext で管理されるため、ここでは設定しない。
            var meshContext = Poly_Ling.Ops.MeshContextCloneOps.Clone(
                State, Poly_Ling.Ops.MeshContextCloneKind.StateSnapshot) ?? new MeshContext();

            // 注: マテリアル情報はModelContextで一元管理されるため、
            // MeshContextSnapshotからのマテリアル復元は行わない
            // MaterialPaths, RuntimeMaterials, CurrentMaterialIndex は
            // ModelContextのスナップショットで管理される

            if (meshContext.MeshObject != null)
            {
                meshContext.ReplaceUnityMesh(meshContext.MeshObject.ToUnityMeshShared());
                meshContext.UnityMesh.name = Name;
                meshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            // 選択状態を復元（Phase 1追加）
            if (Selection != null)
            {
                meshContext.RestoreSelection(Selection);
            }

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

        /// <summary>
        /// 変更前の材質参照一覧（複製して持つ）。null でなければ OldMaterials より優先する。
        ///
        /// OldMaterials（List&lt;Material&gt;）は ModelContext.Materials の setter を通るので、
        /// 戻すと MaterialReference が作り直され、テクスチャパス等の材質データが失われる。
        /// 材質一覧そのものを増減させる操作（MQO の追加読込など）はこちらを使う。
        /// </summary>
        public List<MaterialReference> OldMaterialRefs;

        /// <summary>変更後の材質参照一覧（複製して持つ）。null でなければ NewMaterials より優先する。</summary>
        public List<MaterialReference> NewMaterialRefs;

        /// <summary>
        /// 削除したオブジェクトに付いていたミラー対（実体・ミラーの索引は削除前の並び）。
        /// Undo で削除分を挿し戻したあと、この索引で組み直す。
        /// </summary>
        public List<(int Real, int Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> RemovedMirrorPairs;

        /// <summary>
        /// 追加したオブジェクトに付いていたミラー対（実体・ミラーの索引は追加後の並び）。
        /// Redo で追加分を挿し戻したあと、この索引で組み直す。
        /// </summary>
        public List<(int Real, int Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> AddedMirrorPairs;

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

        /// <summary>
        /// 削除・挿入で頂点ウェイトの参照先ボーン索引を付け替えたときの、
        /// 付け替え前（Before）と後（After）の控え。付け替えが無ければ null。
        ///
        /// 削除は「消したボーンのウェイトを親へ寄せる」ため逆写像で戻せない
        /// （ModelContext.RemapBoneWeights）。Undo/Redo はこの控えで書き戻す。
        /// </summary>
        public List<BoneWeightBackupEntry> BeforeBoneWeights;
        public List<BoneWeightBackupEntry> AfterBoneWeights;
        
        /// <summary>変更前のカメラ状態</summary>
        public CameraSnapshot? OldCameraState;
        
        /// <summary>変更後のカメラ状態</summary>
        public CameraSnapshot? NewCameraState;

        public override void Undo(ModelContext ctx)
        {
            // マテリアルを復元（材質参照の控えがあればそちらを優先）
            if (OldMaterialRefs != null)
            {
                ctx.MaterialReferences = CloneMaterialRefs(OldMaterialRefs);
                ctx.CurrentMaterialIndex = OldCurrentMaterialIndex;
            }
            else if (OldMaterials != null)
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

            // ミラー対：リストに居なくなったオブジェクトの対を外し、削除分の対を組み直す。
            if (AddedMirrorPairs != null || RemovedMirrorPairs != null)
            {
                PruneMirrorPairs(ctx);
                RebuildMirrorPairs(ctx, RemovedMirrorPairs);
            }

            // 選択状態を復元
            ctx.RestoreSelectionFromIndices(OldSelectedIndices);
            ctx.ValidateSelection();

            // 付け替えで書き換えた頂点ウェイトを戻す（リストを戻した後に行う）。
            BoneWeightBackup.Apply(ctx, BeforeBoneWeights);
            
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
            // マテリアルを復元（材質参照の控えがあればそちらを優先）
            if (NewMaterialRefs != null)
            {
                ctx.MaterialReferences = CloneMaterialRefs(NewMaterialRefs);
                ctx.CurrentMaterialIndex = NewCurrentMaterialIndex;
            }
            else if (NewMaterials != null)
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

            // ミラー対：リストに居なくなったオブジェクトの対を外し、追加分の対を組み直す。
            if (AddedMirrorPairs != null || RemovedMirrorPairs != null)
            {
                PruneMirrorPairs(ctx);
                RebuildMirrorPairs(ctx, AddedMirrorPairs);
            }

            // 選択状態を復元
            ctx.RestoreSelectionFromIndices(NewSelectedIndices);
            ctx.ValidateSelection();

            // 付け替え後の頂点ウェイトへ戻す（リストを戻した後に行う）。
            BoneWeightBackup.Apply(ctx, AfterBoneWeights);
            
            // カメラ状態を復元
            if (NewCameraState.HasValue)
            {
                ctx.OnCameraRestoreRequested?.Invoke(NewCameraState.Value);
            }
            
            ctx.OnListChanged?.Invoke();
            
            // MeshListStackにフォーカスを切り替え
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        /// <summary>材質参照一覧を要素ごとに複製する。</summary>
        public static List<MaterialReference> CloneMaterialRefs(List<MaterialReference> src)
        {
            var list = new List<MaterialReference>(src?.Count ?? 0);
            if (src != null)
                foreach (var r in src) list.Add(r?.Clone());
            return list;
        }

        /// <summary>実体・ミラーのどちらかがリストに居ないミラー対を外す。</summary>
        private static void PruneMirrorPairs(ModelContext ctx)
        {
            if (ctx.MirrorPairs == null) return;
            var alive = new HashSet<MeshContext>(ctx.MeshContextList);
            ctx.MirrorPairs.RemoveAll(p => p == null || !alive.Contains(p.Real) || !alive.Contains(p.Mirror));
        }

        /// <summary>控えた索引の組でミラー対を組み直して足す。組めなかった対は足さない。</summary>
        private static void RebuildMirrorPairs(
            ModelContext ctx,
            List<(int Real, int Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> pairs)
        {
            if (pairs == null || pairs.Count == 0) return;
            ctx.MirrorPairs ??= new List<MirrorPair>();
            var list = ctx.MeshContextList;
            foreach (var (ri, mi, axis) in pairs)
            {
                if (ri < 0 || ri >= list.Count || mi < 0 || mi >= list.Count) continue;
                var pair = new MirrorPair { Real = list[ri], Mirror = list[mi], Axis = axis };
                if (pair.Build(list)) ctx.MirrorPairs.Add(pair);
                else Debug.LogWarning($"[MeshListChangeRecord] ミラー対を組み直せませんでした: '{list[ri]?.Name}' ↔ '{list[mi]?.Name}'\n{pair.BuildLog}");
            }
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
                if (change.IsFolding.HasValue) meshContext.IsFolding = change.IsFolding.Value;
                if (change.EditorName != null) meshContext.EditorName = change.EditorName;
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
    /// バインド連動(スキン固定)ボーン移動の複合 Undo レコード。
    /// BoneTransform（位置）と BindPose を同時に記録し、Undo/Redo で両方を復元する。
    /// 各エントリは変化した側のみ値を持つ（HasValue==false はスキップ）。
    /// </summary>
    public class MultiBoneMoveRebindRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public BoneTransformSnapshot? OldBoneTransform;
            public BoneTransformSnapshot? NewBoneTransform;
            public Matrix4x4? OldBindPose;
            public Matrix4x4? NewBindPose;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                ApplyEntry(ctx, e.MasterIndex, e.OldBoneTransform, e.OldBindPose);
            ctx.ComputeWorldMatrices();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                ApplyEntry(ctx, e.MasterIndex, e.NewBoneTransform, e.NewBindPose);
            ctx.ComputeWorldMatrices();
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        private static void ApplyEntry(ModelContext ctx, int masterIndex,
            BoneTransformSnapshot? boneTransform, Matrix4x4? bindPose)
        {
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;
            var mc = ctx.GetMeshContext(masterIndex);
            if (mc == null) return;

            if (boneTransform.HasValue && mc.BoneTransform != null)
                mc.BoneTransform.ApplySnapshot(boneTransform.Value);

            if (bindPose.HasValue)
                mc.BindPose = bindPose.Value;
        }

        public override string ToString() => $"MultiBoneMoveRebind: {Entries.Count} entries";
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

        // ================================================================
        // 表示ミラー設定
        //   モーフ化のとき親のミラー設定を継承する（規約は MorphMirrorPolicy.cs を正典とする）。
        //   継承で書き換わる値なので、記録しないと Undo で元の設定に戻せない。
        // ================================================================

        /// <summary>変更前の表示ミラー設定 (MirrorType, MirrorAxis, MirrorDistance, MirrorMaterialOffset)</summary>
        public (int Type, int Axis, float Distance, int MaterialOffset) OldMirror;
        /// <summary>変更後の表示ミラー設定</summary>
        public (int Type, int Axis, float Distance, int MaterialOffset) NewMirror;

        /// <summary>ミラー設定を記録・復元の対象にするか（既定 false = 従来どおり触らない）</summary>
        public bool HasMirrorChange;

        // ================================================================
        // MorphExpressions
        //   モーフ化のとき Expression を自動生成するため、リスト全体が変わる。
        //   Expression は名前とエントリ（index + weight）だけの軽い構造なので、
        //   前後をまるごと控えて差し替える（MorphExpressionListReplaceRecord と同じ方式）。
        // ================================================================

        /// <summary>変更前の MorphExpressions</summary>
        public List<MorphExpression> OldExpressions;
        /// <summary>変更後の MorphExpressions</summary>
        public List<MorphExpression> NewExpressions;

        /// <summary>MorphExpressions を記録・復元の対象にするか（既定 false = 従来どおり触らない）</summary>
        public bool HasExpressionChange;

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldType, OldMorphBaseData, OldMorphParentIndex, OldName, OldExcludeFromExport,
                  OldMirror, OldExpressions);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewType, NewMorphBaseData, NewMorphParentIndex, NewName, NewExcludeFromExport,
                  NewMirror, NewExpressions);
        }

        private void Apply(ModelContext ctx, MeshType type, MorphBaseData morphData,
            int morphParentIndex, string name, bool excludeFromExport,
            (int Type, int Axis, float Distance, int MaterialOffset) mirror,
            List<MorphExpression> expressions)
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

            if (HasMirrorChange)
            {
                mc.MirrorType           = mirror.Type;
                mc.MirrorAxis           = mirror.Axis;
                mc.MirrorDistance       = mirror.Distance;
                mc.MirrorMaterialOffset = mirror.MaterialOffset;
                mc.InvalidateSymmetryCache();
            }

            if (HasExpressionChange && expressions != null)
            {
                var restored = new List<MorphExpression>(expressions.Count);
                for (int i = 0; i < expressions.Count; i++)
                    restored.Add(expressions[i]?.Clone());
                ctx.MorphExpressions = restored;
            }

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
    // オブジェクトグループ変更記録
    // ============================================================

    /// <summary>
    /// オブジェクトグループの追加 / 削除 / 差し替えの Undo 記録。
    ///
    /// 【なぜメッシュリストのレコードに混ぜないか】
    ///   グループはメッシュの追加・削除とは別のタイミングで増減する。
    ///   「ちょっと作るだけ」はグループを作らずに描画オブジェクトだけ増えるし、
    ///   グループの解除は描画オブジェクトを残したままグループだけ消す。
    ///   MorphExpressionChangeRecord と同じく独立したレコードにする。
    ///
    /// 【実体ではなく複製を持つ】
    ///   グループは Args と MeshRefIds を可変の辞書で持つ。参照のまま控えると、
    ///   あとで中身を書き換えたときに記録側も一緒に変わる。
    /// </summary>
    public class ObjectGroupChangeRecord : MeshListUndoRecord
    {
        /// <summary>追加されたグループ（Undo 時に削除）。</summary>
        public Poly_Ling.Data.ObjectGroup AddedGroup;
        /// <summary>追加位置。</summary>
        public int AddedIndex = -1;

        /// <summary>削除されたグループ（Undo 時に復元）。</summary>
        public Poly_Ling.Data.ObjectGroup RemovedGroup;
        /// <summary>削除位置。</summary>
        public int RemovedIndex = -1;

        /// <summary>差し替え前のグループ（Undo 時に戻す）。</summary>
        public Poly_Ling.Data.ObjectGroup OldGroup;
        /// <summary>差し替え後のグループ（Redo 時に入れる）。</summary>
        public Poly_Ling.Data.ObjectGroup NewGroup;
        /// <summary>差し替え位置。-1 = 差し替えではない。</summary>
        public int ReplacedIndex = -1;

        public override void Undo(ModelContext ctx)
        {
            if (ctx?.ObjectGroups == null) return;

            if (ReplacedIndex >= 0 && ReplacedIndex < ctx.ObjectGroups.Count && OldGroup != null)
                ctx.ObjectGroups[ReplacedIndex] = OldGroup.Clone();

            if (AddedGroup != null && AddedIndex >= 0 && AddedIndex < ctx.ObjectGroups.Count)
                ctx.ObjectGroups.RemoveAt(AddedIndex);

            if (RemovedGroup != null && RemovedIndex >= 0)
            {
                int idx = Mathf.Clamp(RemovedIndex, 0, ctx.ObjectGroups.Count);
                ctx.ObjectGroups.Insert(idx, RemovedGroup.Clone());
            }

            ctx.IsDirty = true;
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx?.ObjectGroups == null) return;

            if (RemovedGroup != null && RemovedIndex >= 0 && RemovedIndex < ctx.ObjectGroups.Count)
                ctx.ObjectGroups.RemoveAt(RemovedIndex);

            if (AddedGroup != null && AddedIndex >= 0)
            {
                int idx = Mathf.Clamp(AddedIndex, 0, ctx.ObjectGroups.Count);
                ctx.ObjectGroups.Insert(idx, AddedGroup.Clone());
            }

            if (ReplacedIndex >= 0 && ReplacedIndex < ctx.ObjectGroups.Count && NewGroup != null)
                ctx.ObjectGroups[ReplacedIndex] = NewGroup.Clone();

            ctx.IsDirty = true;
            ctx.OnListChanged?.Invoke();
        }

        public override string ToString()
        {
            if (AddedGroup   != null) return $"ObjectGroupAdd: {AddedGroup.Name}";
            if (RemovedGroup != null) return $"ObjectGroupRemove: {RemovedGroup.Name}";
            if (NewGroup     != null) return $"ObjectGroupReplace: {NewGroup.Name}";
            return "ObjectGroupChange";
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

        // ミラー対の控え（実体・ミラーの ObjectId と軸）。
        // 一覧のスナップショットは対を持たないので、Undo・Redo の前後で対の集合と対応表を
        // 作り直すために控える。実体は替えない（段 A）ので、ObjectId から今の実体が引ける。
        // （段 8 では、実体を複製に置き換えていたため対が古い実体を指したまま残り、
        //   揺れ鎖の作り直しがミラー側へ流し込めずに鎖を新しく作っていた。）
        // 記録の時点では対を控えていないので、最初の Undo のときに今（＝後）の対を控え、
        // 最初の Redo のときに今（＝前）の対を控える。前の対が分からない最初の Undo では
        // 後の対のうち両方が前の一覧に居るものを使う。
        private List<(ulong Real, ulong Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> _beforePairIds;
        private List<(ulong Real, ulong Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> _afterPairIds;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null || BeforeList == null) return;
            var current = CaptureMirrorPairIds(ctx);
            if (_afterPairIds == null) _afterPairIds = current;
            RestoreList(ctx, BeforeList, _beforePairIds ?? current);
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null || AfterList == null) return;
            var current = CaptureMirrorPairIds(ctx);
            if (_beforePairIds == null) _beforePairIds = current;
            RestoreList(ctx, AfterList, _afterPairIds ?? current);
        }

        private static List<(ulong Real, ulong Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> CaptureMirrorPairIds(ModelContext ctx)
        {
            var ids = new List<(ulong, ulong, Poly_Ling.Symmetry.SymmetryAxis)>();
            if (ctx?.MirrorPairs == null) return ids;
            foreach (var p in ctx.MirrorPairs)
            {
                ulong r = p?.Real?.ObjectId ?? 0UL;
                ulong m = p?.Mirror?.ObjectId ?? 0UL;
                if (r != 0UL && m != 0UL) ids.Add((r, m, p.Axis));
            }
            return ids;
        }

        private static void RestoreList(
            ModelContext ctx, List<MeshContext> source,
            List<(ulong Real, ulong Mirror, Poly_Ling.Symmetry.SymmetryAxis Axis)> pairIds)
        {
            // オブジェクト・ボーンの選択を ObjectId で控える（段 I）。実体は替えない（段 A）ので、
            // 戻したあとも一覧に居るものは選び直せる。以前は空にしていたので、Undo のあと
            // 出力がオブジェクトとして選ばれず、頂点を選んでも選択の色とギズモが出なかった。
            var meshSelIds = SelectionObjectIds(ctx, ctx.SelectedDrawableMeshIndices);
            var boneSelIds = SelectionObjectIds(ctx, ctx.SelectedBoneIndices);

            RestoreListInPlace(ctx, source);

            // ミラー対を組み直す。描画オブジェクトの実体は替わらないが、Undo の前後で対の集合と
            // 対応表（VertexMap）が変わりうるので、控えた ObjectId の組から作り直す。
            // 片方でも一覧に居ない対は外す。
            ctx.MirrorPairs ??= new List<MirrorPair>();
            ctx.MirrorPairs.Clear();
            if (pairIds != null && pairIds.Count > 0)
            {
                var byId = new Dictionary<ulong, MeshContext>();
                foreach (var mc in ctx.MeshContextList)
                    if (mc != null && mc.ObjectId != 0UL) byId[mc.ObjectId] = mc;
                foreach (var (r, m, axis) in pairIds)
                {
                    if (!byId.TryGetValue(r, out var real) || !byId.TryGetValue(m, out var mirror)) continue;
                    var pair = new MirrorPair { Real = real, Mirror = mirror, Axis = axis };
                    if (pair.Build(ctx.MeshContextList)) ctx.MirrorPairs.Add(pair);
                    else Debug.LogWarning($"[MeshFilterToSkinnedRecord] ミラー対を組み直せませんでした: '{real.Name}' ↔ '{mirror.Name}'\n{pair.BuildLog}");
                }
            }

            ctx.InvalidateTypedIndices();
            // 控えた選択を今の索引で選び直す（並びは控えの順。一覧に居ないものは外れる）。
            // 頂点・面の選択は戻さない（CopyState が空にする。中身が変わると番号が別の頂点を指す）。
            ReselectByObjectIds(ctx, ctx.SelectedDrawableMeshIndices, meshSelIds);
            ReselectByObjectIds(ctx, ctx.SelectedBoneIndices, boneSelIds);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        /// <summary>選択の索引の列を ObjectId の列にする（0 の実体は控えない）。</summary>
        private static List<ulong> SelectionObjectIds(ModelContext ctx, List<int> indices)
        {
            var ids = new List<ulong>();
            if (ctx == null || indices == null) return ids;
            foreach (int i in indices)
            {
                var mc = ctx.GetMeshContext(i);
                if (mc != null && mc.ObjectId != 0UL) ids.Add(mc.ObjectId);
            }
            return ids;
        }

        /// <summary>選択の索引の列を、控えた ObjectId の今の索引で置き換える。</summary>
        private static void ReselectByObjectIds(ModelContext ctx, List<int> indices, List<ulong> ids)
        {
            if (ctx == null || indices == null) return;
            indices.Clear();
            if (ids == null || ids.Count == 0) return;
            var byId = new Dictionary<ulong, int>();
            for (int i = 0; i < ctx.MeshContextCount; i++)
            {
                var mc = ctx.GetMeshContext(i);
                if (mc != null && mc.ObjectId != 0UL && !byId.ContainsKey(mc.ObjectId)) byId[mc.ObjectId] = i;
            }
            foreach (var id in ids)
                if (byId.TryGetValue(id, out int idx) && !indices.Contains(idx)) indices.Add(idx);
        }

        /// <summary>
        /// MeshContext を HierarchyParentIndex を含めてディープコピーする。
        ///
        /// 写す項目は MeshContextCloneOps が持つ。以前ここに書いていた初期化子は
        /// BlendOperation.CloneContext / Player の複製と食い違っており、
        /// フィールドを足すたびに 3 箇所へ手で足す形になっていた。
        ///
        /// 同一オブジェクトの状態複写（Undo/Redo 用）なので
        /// 安定IDと担当者はそのまま引き継ぐ（StateSnapshot）。
        /// ※ 複製(Duplicate)は別オブジェクトなのでこの経路を使わないこと。
        ///    MeshContextCloneKind.NewObject を使う。
        /// </summary>
        public static MeshContext CloneMeshContext(MeshContext src)
        {
            if (src == null) return null;
            var dst = Poly_Ling.Ops.MeshContextCloneOps.Clone(
                src, Poly_Ling.Ops.MeshContextCloneKind.StateSnapshot);

            RebuildUnityMesh(dst);
            // マテリアルは ModelContext 一元管理（ParentModelContext へ委譲）のため
            // ここではコピーしない。復元時に RestoreList が ParentModelContext を設定する。
            return dst;
        }

        /// <summary>UnityMesh を MeshObject から作り直す。頂点が無ければ外す。</summary>
        private static void RebuildUnityMesh(MeshContext mc)
        {
            if (mc.MeshObject != null && mc.MeshObject.VertexCount > 0)
            {
                mc.ReplaceUnityMeshDeferred(mc.MeshObject.ToUnityMesh());
                if (mc.UnityMesh != null) mc.UnityMesh.name = mc.Name;
            }
            else
            {
                mc.ReplaceUnityMeshDeferred(null);
            }
        }

        /// <summary>
        /// 控えた一覧へ戻す。描画オブジェクトの実体は替えない（作り直し整備計画 段 A）。
        ///
        ///   控えの各オブジェクトについて、今の一覧に同じ ObjectId の実体があれば、その実体へ
        ///   中身を写す（MeshContextCloneOps.CopyState）。無ければ控えの複製を足す。
        ///   一覧の並びは控えの順にする。控えに無い実体は一覧から外れる。
        ///
        ///   以前は全部を複製で置き換えていたので、実体の参照を持ち続けている所
        ///   （ミラー対・選択の状態・評価器の控えのグラフ・並び替えの記録・ツールのプレビュー）が
        ///   置き換え前のオブジェクトを指したまま残った。
        /// </summary>
        public static void RestoreListInPlace(ModelContext ctx, List<MeshContext> source)
        {
            var live = new Dictionary<ulong, MeshContext>();
            foreach (var mc in ctx.MeshContextList)
                if (mc != null && mc.ObjectId != 0UL && !live.ContainsKey(mc.ObjectId))
                    live[mc.ObjectId] = mc;

            var restored = new List<MeshContext>(source.Count);
            foreach (var src in source)
            {
                if (src == null) { restored.Add(null); continue; }

                MeshContext dst;
                if (src.ObjectId != 0UL && live.TryGetValue(src.ObjectId, out var existing))
                {
                    live.Remove(src.ObjectId);   // 同じ実体を 2 度使わない
                    Poly_Ling.Ops.MeshContextCloneOps.CopyState(
                        src, existing, Poly_Ling.Ops.MeshContextCloneKind.StateSnapshot);
                    RebuildUnityMesh(existing);
                    dst = existing;
                }
                else
                {
                    dst = CloneMeshContext(src);
                }
                restored.Add(dst);
            }

            ctx.ReplaceMeshContexts(restored);
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
                // 実体を替えずに戻す（段 A。MeshFilterToSkinnedRecord と同じ戻し方）。
                MeshFilterToSkinnedRecord.RestoreListInPlace(ctx, meshList);
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
