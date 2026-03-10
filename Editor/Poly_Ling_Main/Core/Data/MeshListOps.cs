// MeshListOps.cs
// モデル内のメッシュを操作するヘルパークラス
// パネルからは直接呼ばない。メインルーチンのコマンドディスパッチから呼ぶ。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Data
{
    public class MeshListOps
    {
        private ModelContext _model;
        private MeshUndoController _undo;

        // メインルーチンが設定するコールバック（GPU同期用）
        public Action<MeshContext> SyncPositionsOnly { get; set; }
        public Action SyncMesh { get; set; }
        public Action Repaint { get; set; }

        // モーフプレビュー状態
        private bool _isMorphPreviewActive;
        private Dictionary<int, Vector3[]> _morphPreviewBackups = new Dictionary<int, Vector3[]>();
        private List<(int morphIndex, int baseIndex)> _morphPreviewTargets = new List<(int, int)>();

        // スライダードラッグ用スナップショット（BonePose）
        private Dictionary<int, BonePoseDataSnapshot> _sliderDragBeforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();

        // スライダードラッグ用スナップショット（BoneTransform）
        private Dictionary<int, BoneTransformSnapshot> _boneTransformSliderSnapshots = new Dictionary<int, BoneTransformSnapshot>();

        public bool IsMorphPreviewActive => _isMorphPreviewActive;

        public MeshListOps(ModelContext model, MeshUndoController undo)
        {
            _model = model;
            _undo = undo;
        }

        public void SetContext(ModelContext model, MeshUndoController undo)
        {
            EndMorphPreview();
            _sliderDragBeforeSnapshots.Clear();
            _boneTransformSliderSnapshots.Clear();
            _model = model;
            _undo = undo;
        }

        // ================================================================
        // 属性変更
        // ================================================================

        public bool ToggleVisibility(int masterIndex)
        {
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null) return false;
            return ApplyAttributeChange(new MeshAttributeChange
                { Index = masterIndex, IsVisible = !ctx.IsVisible });
        }

        public bool SetBatchVisibility(int[] masterIndices, bool visible)
        {
            if (masterIndices == null || masterIndices.Length == 0) return false;
            var changes = new List<MeshAttributeChange>();
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx == null || ctx.IsVisible == visible) continue;
                changes.Add(new MeshAttributeChange { Index = idx, IsVisible = visible });
            }
            return ApplyAttributeChanges(changes);
        }

        public bool ToggleLock(int masterIndex)
        {
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null) return false;
            return ApplyAttributeChange(new MeshAttributeChange
                { Index = masterIndex, IsLocked = !ctx.IsLocked });
        }

        public bool CycleMirrorType(int masterIndex)
        {
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null || ctx.IsBakedMirror) return false;
            int next = (ctx.MirrorType + 1) % 4;
            return ApplyAttributeChange(new MeshAttributeChange
                { Index = masterIndex, MirrorType = next });
        }

        public bool RenameMesh(int masterIndex, string newName)
        {
            if (string.IsNullOrEmpty(newName)) return false;
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null) return false;
            return ApplyAttributeChange(new MeshAttributeChange
                { Index = masterIndex, Name = newName });
        }

        // ================================================================
        // リスト操作
        // ================================================================

        public MeshContext AddNewMesh()
        {
            if (_model == null) return null;
            return new MeshContext
            {
                MeshObject = new MeshObject("New Mesh"),
                UnityMesh = new Mesh(),
                OriginalPositions = new Vector3[0]
            };
        }

        // ================================================================
        // リオーダー（D&D/移動/Indent/Outdent）
        // ================================================================

        public bool ReorderMeshes(MeshCategory category, ReorderMeshesCommand.ReorderEntry[] entries)
        {
            if (_model == null || entries == null || entries.Length == 0) return false;

            // 変更前の状態を保存
            var preOrderedList = new List<MeshContext>(_model.MeshContextList);
            var preParentMap = new Dictionary<MeshContext, MeshContext>();
            foreach (var mc in _model.MeshContextList)
            {
                MeshContext parent = null;
                if (mc.HierarchyParentIndex >= 0 && mc.HierarchyParentIndex < _model.MeshContextCount)
                    parent = _model.GetMeshContext(mc.HierarchyParentIndex);
                preParentMap[mc] = parent;
            }

            // カテゴリのMeshContextマスターインデックス→MeshContext マップ
            var entryMap = new Dictionary<int, ReorderMeshesCommand.ReorderEntry>();
            foreach (var e in entries) entryMap[e.MasterIndex] = e;

            var categoryContexts = new HashSet<MeshContext>();
            foreach (var e in entries)
            {
                var ctx = GetMeshContext(e.MasterIndex);
                if (ctx != null) categoryContexts.Add(ctx);
            }

            // 新しい順序のMeshContext配列（entriesの順序に従う）
            var newCategoryOrder = new List<MeshContext>();
            foreach (var e in entries)
            {
                var ctx = GetMeshContext(e.MasterIndex);
                if (ctx != null) newCategoryOrder.Add(ctx);
            }

            // マスターリストを再構築
            var newOrder = new List<MeshContext>();
            int categoryIdx = 0;
            foreach (var mc in _model.MeshContextList)
            {
                if (categoryContexts.Contains(mc))
                {
                    if (categoryIdx < newCategoryOrder.Count)
                    {
                        var newCtx = newCategoryOrder[categoryIdx];
                        if (!newOrder.Contains(newCtx))
                            newOrder.Add(newCtx);
                        categoryIdx++;
                    }
                }
                else
                {
                    newOrder.Add(mc);
                }
            }
            while (categoryIdx < newCategoryOrder.Count)
            {
                var ctx = newCategoryOrder[categoryIdx];
                if (!newOrder.Contains(ctx)) newOrder.Add(ctx);
                categoryIdx++;
            }

            _model.MeshContextList.Clear();
            _model.MeshContextList.AddRange(newOrder);

            // Depth/ParentIndex更新
            foreach (var e in entries)
            {
                var ctx = GetMeshContext(e.MasterIndex);
                if (ctx == null) continue;
                ctx.Depth = e.NewDepth;
                if (e.NewParentMasterIndex >= 0)
                {
                    int parentIdx = _model.MeshContextList.IndexOf(GetMeshContext(e.NewParentMasterIndex));
                    ctx.HierarchyParentIndex = parentIdx >= 0 ? parentIdx : -1;
                }
                else
                {
                    ctx.HierarchyParentIndex = -1;
                }
            }

            // 選択インデックス復元
            RestoreSelectionAfterReorder(category, preOrderedList);

            // Undo記録
            var newParentMap = new Dictionary<MeshContext, MeshContext>();
            foreach (var mc in _model.MeshContextList)
            {
                MeshContext parent = null;
                if (mc.HierarchyParentIndex >= 0 && mc.HierarchyParentIndex < _model.MeshContextCount)
                    parent = _model.GetMeshContext(mc.HierarchyParentIndex);
                newParentMap[mc] = parent;
            }

            if (!ListsEqual(preOrderedList, _model.MeshContextList) || !MapsEqual(preParentMap, newParentMap))
            {
                if (_undo != null)
                {
                    var record = new MeshReorderChangeRecord
                    {
                        OldOrderedList = preOrderedList,
                        NewOrderedList = new List<MeshContext>(_model.MeshContextList),
                        OldParentMap = preParentMap,
                        NewParentMap = newParentMap,
                    };
                    _undo.MeshListStack.Record(record, "メッシュ順序変更");
                    _undo.FocusMeshList();
                }
            }

            _model.TypedIndices?.Invalidate();
            MarkDirty();
            return true;
        }

        private void RestoreSelectionAfterReorder(MeshCategory category, List<MeshContext> preOrderedList)
        {
            // 選択されていたMeshContextを復元
            IEnumerable<int> selectedIndices = category switch
            {
                MeshCategory.Drawable => _model.SelectedMeshIndices,
                MeshCategory.Bone => _model.SelectedBoneIndices,
                MeshCategory.Morph => _model.SelectedMorphIndices,
                _ => _model.SelectedMeshIndices
            };
            var selectedContexts = selectedIndices
                .Where(i => i >= 0 && i < preOrderedList.Count)
                .Select(i => preOrderedList[i])
                .Where(mc => mc != null)
                .ToList();

            switch (category)
            {
                case MeshCategory.Drawable:
                    _model.ClearMeshSelection();
                    foreach (var mc in selectedContexts)
                    {
                        int newIdx = _model.MeshContextList.IndexOf(mc);
                        if (newIdx >= 0) _model.AddToMeshSelection(newIdx);
                    }
                    break;
                case MeshCategory.Bone:
                    _model.ClearBoneSelection();
                    foreach (var mc in selectedContexts)
                    {
                        int newIdx = _model.MeshContextList.IndexOf(mc);
                        if (newIdx >= 0) _model.AddToBoneSelection(newIdx);
                    }
                    break;
                case MeshCategory.Morph:
                    _model.ClearMorphSelection();
                    foreach (var mc in selectedContexts)
                    {
                        int newIdx = _model.MeshContextList.IndexOf(mc);
                        if (newIdx >= 0) _model.AddToMorphSelection(newIdx);
                    }
                    break;
            }
        }

        // ================================================================
        // BonePose操作
        // ================================================================

        public bool InitBonePose(int[] masterIndices)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;
            var before = CaptureBonePoseSnapshots(masterIndices);
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx == null) continue;
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
            }
            var after = CaptureBonePoseSnapshots(masterIndices);
            RecordBonePoseUndo(before, after, "ボーンポーズ初期化");
            MarkDirty();
            return true;
        }

        public bool SetBonePoseActive(int[] masterIndices, bool active)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;
            var before = CaptureBonePoseSnapshots(masterIndices);
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx == null) continue;
                if (active)
                {
                    if (ctx.BonePoseData == null) ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                    ctx.BonePoseData.SetDirty();
                }
                else if (ctx.BonePoseData != null)
                {
                    ctx.BonePoseData.IsActive = false;
                    ctx.BonePoseData.SetDirty();
                }
            }
            var after = CaptureBonePoseSnapshots(masterIndices);
            RecordBonePoseUndo(before, after, active ? "ボーンポーズ有効化" : "ボーンポーズ無効化");
            MarkDirty();
            return true;
        }

        public bool ResetBonePoseLayers(int[] masterIndices)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;
            var targets = FilterWithBonePose(masterIndices);
            if (targets.Count == 0) return false;
            var before = CaptureBonePoseSnapshots(targets);
            foreach (int idx in targets)
                GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
            var after = CaptureBonePoseSnapshots(targets);
            RecordBonePoseUndo(before, after, "全レイヤークリア");
            MarkDirty();
            return true;
        }

        public bool BakePoseToBindPose(int[] masterIndices)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;
            var targets = FilterWithBonePose(masterIndices);
            if (targets.Count == 0) return false;

            var record = new MultiBonePoseChangeRecord();
            foreach (int idx in targets)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BonePoseData == null) continue;
                var beforePose = ctx.BonePoseData.CreateSnapshot();
                Matrix4x4 oldBindPose = ctx.BindPose;
                ctx.BonePoseData.BakeToBindPose(ctx.WorldMatrix);
                ctx.BindPose = ctx.WorldMatrix.inverse;
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = beforePose,
                    NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    OldBindPose = oldBindPose,
                    NewBindPose = ctx.BindPose
                });
            }
            if (record.Entries.Count > 0)
            {
                _undo?.MeshListStack.Record(record, "BindPoseにベイク");
                _undo?.FocusMeshList();
            }
            MarkDirty();
            return true;
        }

        // ================================================================
        // BonePose RestPose値変更
        // ================================================================

        public bool SetBonePoseRestValue(int[] masterIndices, SetBonePoseRestValueCommand.Field field, float value)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;

            // スライダードラッグ中でなければスナップショット取得＆即記録
            bool isSliderDrag = _sliderDragBeforeSnapshots.Count > 0;
            Dictionary<int, BonePoseDataSnapshot?> before = null;
            if (!isSliderDrag)
                before = CaptureBonePoseSnapshots(masterIndices);

            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BonePoseData == null) continue;
                var pose = ctx.BonePoseData;
                ApplyRestField(pose, field, value);
                pose.SetDirty();
            }

            if (!isSliderDrag)
            {
                var after = CaptureBonePoseSnapshots(masterIndices);
                RecordBonePoseUndo(before, after, "ボーンポーズ変更");
            }
            MarkDirty();
            return true;
        }

        private void ApplyRestField(BonePoseData pose, SetBonePoseRestValueCommand.Field field, float value)
        {
            switch (field)
            {
                case SetBonePoseRestValueCommand.Field.PositionX:
                    pose.RestPosition = new Vector3(value, pose.RestPosition.y, pose.RestPosition.z); break;
                case SetBonePoseRestValueCommand.Field.PositionY:
                    pose.RestPosition = new Vector3(pose.RestPosition.x, value, pose.RestPosition.z); break;
                case SetBonePoseRestValueCommand.Field.PositionZ:
                    pose.RestPosition = new Vector3(pose.RestPosition.x, pose.RestPosition.y, value); break;
                case SetBonePoseRestValueCommand.Field.RotationX:
                case SetBonePoseRestValueCommand.Field.RotationY:
                case SetBonePoseRestValueCommand.Field.RotationZ:
                    ApplyRestRotField(pose, field, value); break;
                case SetBonePoseRestValueCommand.Field.ScaleX:
                    pose.RestScale = new Vector3(value, pose.RestScale.y, pose.RestScale.z); break;
                case SetBonePoseRestValueCommand.Field.ScaleY:
                    pose.RestScale = new Vector3(pose.RestScale.x, value, pose.RestScale.z); break;
                case SetBonePoseRestValueCommand.Field.ScaleZ:
                    pose.RestScale = new Vector3(pose.RestScale.x, pose.RestScale.y, value); break;
            }
        }

        private void ApplyRestRotField(BonePoseData pose, SetBonePoseRestValueCommand.Field field, float value)
        {
            var q = pose.RestRotation;
            bool valid = !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0);
            Vector3 euler = valid ? q.eulerAngles : Vector3.zero;

            switch (field)
            {
                case SetBonePoseRestValueCommand.Field.RotationX: euler.x = value; break;
                case SetBonePoseRestValueCommand.Field.RotationY: euler.y = value; break;
                case SetBonePoseRestValueCommand.Field.RotationZ: euler.z = value; break;
            }
            pose.RestRotation = Quaternion.Euler(euler);
        }

        /// <summary>スライダードラッグ開始: 現在のスナップショットを保持</summary>
        public void BeginSliderDrag(int[] masterIndices)
        {
            if (_sliderDragBeforeSnapshots.Count > 0) return; // 既にドラッグ中
            if (_model == null || masterIndices == null) return;
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BonePoseData != null)
                    _sliderDragBeforeSnapshots[idx] = ctx.BonePoseData.CreateSnapshot();
            }
        }

        /// <summary>スライダードラッグ終了: Undo記録コミット</summary>
        public void EndSliderDrag(string description)
        {
            if (_sliderDragBeforeSnapshots.Count == 0) return;
            if (_undo != null && _model != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var kvp in _sliderDragBeforeSnapshots)
                {
                    var ctx = GetMeshContext(kvp.Key);
                    var afterSnapshot = ctx?.BonePoseData?.CreateSnapshot();
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = kvp.Key,
                        OldSnapshot = kvp.Value,
                        NewSnapshot = afterSnapshot,
                    });
                }
                _undo.MeshListStack.Record(record, description);
                _undo.FocusMeshList();
            }
            _sliderDragBeforeSnapshots.Clear();
        }

        // ================================================================
        // BoneTransform値変更（簡易モード用）
        // ================================================================
        public bool SetBoneTransformValue(int[] masterIndices, SetBoneTransformValueCommand.Field field, float value)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;

            bool isSliderDrag = _boneTransformSliderSnapshots.Count > 0;
            var before = isSliderDrag ? null : CaptureTransformSnapshots(masterIndices);

            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BoneTransform == null) continue;
                ctx.BoneTransform.UseLocalTransform = true;
                ApplyTransformField(ctx.BoneTransform, field, value);
            }

            if (!isSliderDrag)
            {
                var after = CaptureTransformSnapshots(masterIndices);
                RecordBoneTransformUndo(before, after, "トランスフォーム変更");
            }
            MarkDirty();
            return true;
        }

        private void ApplyTransformField(BoneTransform bt, SetBoneTransformValueCommand.Field field, float value)
        {
            switch (field)
            {
                case SetBoneTransformValueCommand.Field.PositionX: bt.Position = new Vector3(value, bt.Position.y, bt.Position.z); break;
                case SetBoneTransformValueCommand.Field.PositionY: bt.Position = new Vector3(bt.Position.x, value, bt.Position.z); break;
                case SetBoneTransformValueCommand.Field.PositionZ: bt.Position = new Vector3(bt.Position.x, bt.Position.y, value); break;
                case SetBoneTransformValueCommand.Field.RotationX: bt.Rotation = new Vector3(value, bt.Rotation.y, bt.Rotation.z); break;
                case SetBoneTransformValueCommand.Field.RotationY: bt.Rotation = new Vector3(bt.Rotation.x, value, bt.Rotation.z); break;
                case SetBoneTransformValueCommand.Field.RotationZ: bt.Rotation = new Vector3(bt.Rotation.x, bt.Rotation.y, value); break;
                case SetBoneTransformValueCommand.Field.ScaleX: bt.Scale = new Vector3(value, bt.Scale.y, bt.Scale.z); break;
                case SetBoneTransformValueCommand.Field.ScaleY: bt.Scale = new Vector3(bt.Scale.x, value, bt.Scale.z); break;
                case SetBoneTransformValueCommand.Field.ScaleZ: bt.Scale = new Vector3(bt.Scale.x, bt.Scale.y, value); break;
            }
        }
        public void BeginBoneTransformSliderDrag(int[] masterIndices)
        {
            if (_boneTransformSliderSnapshots.Count > 0) return;
            if (_model == null || masterIndices == null) return;
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BoneTransform != null)
                    _boneTransformSliderSnapshots[idx] = ctx.BoneTransform.CreateSnapshot();
            }
        }

        public void EndBoneTransformSliderDrag(string description)
        {
            if (_boneTransformSliderSnapshots.Count == 0) return;
            if (_undo != null && _model != null)
            {
                var record = new MultiBoneTransformChangeRecord();
                foreach (var kvp in _boneTransformSliderSnapshots)
                {
                    var ctx = GetMeshContext(kvp.Key);
                    if (ctx?.BoneTransform != null)
                        record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                        {
                            MasterIndex = kvp.Key,
                            OldSnapshot = kvp.Value,
                            NewSnapshot = ctx.BoneTransform.CreateSnapshot()
                        });
                }
                _undo.MeshListStack.Record(record, description);
                _undo.FocusMeshList();
            }
            _boneTransformSliderSnapshots.Clear();
        }

        private Dictionary<int, BoneTransformSnapshot> CaptureTransformSnapshots(IEnumerable<int> masterIndices)
        {
            var dict = new Dictionary<int, BoneTransformSnapshot>();
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx?.BoneTransform != null)
                    dict[idx] = ctx.BoneTransform.CreateSnapshot();
            }
            return dict;
        }

        private void RecordBoneTransformUndo(
            Dictionary<int, BoneTransformSnapshot> before,
            Dictionary<int, BoneTransformSnapshot> after,
            string description)
        {
            if (_undo == null || before == null) return;
            var record = new MultiBoneTransformChangeRecord();
            foreach (var kvp in before)
            {
                after.TryGetValue(kvp.Key, out var afterVal);
                record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                    { MasterIndex = kvp.Key, OldSnapshot = kvp.Value, NewSnapshot = afterVal });
            }
            _undo.MeshListStack.Record(record, description);
            _undo.FocusMeshList();
        }
        // ================================================================

        public bool ConvertMeshToMorph(int sourceIndex, int parentIndex, string morphName, int panel)
        {
            if (_model == null) return false;
            var ctx = GetMeshContext(sourceIndex);
            if (ctx == null || ctx.MeshObject == null || ctx.IsMorph) return false;
            if (string.IsNullOrEmpty(morphName)) morphName = ctx.Name;

            var undoRecord = new MorphConversionRecord
            {
                MasterIndex = sourceIndex,
                OldType = ctx.Type, NewType = MeshType.Morph,
                OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                OldMorphParentIndex = ctx.MorphParentIndex,
                OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
            };

            MeshObject baseMeshObject = null;
            if (parentIndex >= 0 && parentIndex < _model.MeshContextCount)
                baseMeshObject = _model.GetMeshContext(parentIndex)?.MeshObject;

            ctx.SetAsMorph(morphName, baseMeshObject);
            ctx.MorphPanel = panel;
            ctx.MorphParentIndex = parentIndex;
            ctx.Type = MeshType.Morph;
            ctx.IsVisible = false;
            if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Morph;
            ctx.ExcludeFromExport = true;

            undoRecord.NewMorphBaseData = ctx.MorphBaseData?.Clone();
            undoRecord.NewMorphParentIndex = ctx.MorphParentIndex;
            undoRecord.NewName = ctx.Name;
            undoRecord.NewExcludeFromExport = ctx.ExcludeFromExport;
            RecordMorphUndo(undoRecord, "メッシュ→モーフ変換");

            _model.RemoveFromSelectionByType(sourceIndex);
            _model.TypedIndices?.Invalidate();
            SelectFirstDrawableIfNeeded();
            MarkDirty();
            return true;
        }

        public bool ConvertMorphToMesh(int[] masterIndices)
        {
            if (_model == null || masterIndices == null || masterIndices.Length == 0) return false;
            var targets = new List<int>();
            foreach (int idx in masterIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx != null && (ctx.IsMorph || ctx.Type == MeshType.Morph))
                    targets.Add(idx);
            }
            if (targets.Count == 0) return false;

            foreach (int idx in targets)
            {
                var ctx = GetMeshContext(idx);
                if (ctx == null) continue;
                var undoRecord = new MorphConversionRecord
                {
                    MasterIndex = idx,
                    OldType = ctx.Type, NewType = MeshType.Mesh,
                    OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                    OldMorphParentIndex = ctx.MorphParentIndex,
                    OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
                    NewMorphBaseData = null, NewMorphParentIndex = -1,
                    NewName = ctx.Name, NewExcludeFromExport = false,
                };
                ctx.ClearMorphData();
                ctx.Type = MeshType.Mesh;
                if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Mesh;
                ctx.ExcludeFromExport = false;
                RecordMorphUndo(undoRecord, $"モーフ→メッシュ: {ctx.Name}");
            }

            foreach (int idx in targets)
                _model.RemoveFromSelectionByType(idx);
            _model.TypedIndices?.Invalidate();
            SelectFirstDrawableIfNeeded();
            MarkDirty();
            return true;
        }

        public bool CreateMorphSet(string setName, int morphType, int[] morphIndices)
        {
            if (_model == null || morphIndices == null || morphIndices.Length == 0) return false;
            if (string.IsNullOrEmpty(setName))
                setName = _model.GenerateUniqueMorphExpressionName("MorphExpression");
            if (_model.FindMorphExpressionByName(setName) != null) return false;

            var set = new MorphExpression(setName, (MorphType)morphType);
            foreach (int idx in morphIndices)
            {
                var ctx = GetMeshContext(idx);
                if (ctx != null && ctx.IsMorph) set.AddMesh(idx);
            }
            if (set.MeshCount == 0) return false;

            var record = new MorphExpressionChangeRecord
            {
                AddExpression = set.Clone(),
                AddedIndex = _model.MorphExpressions.Count,
            };
            RecordMorphUndo(record, $"モーフセット生成: {setName}");
            _model.MorphExpressions.Add(set);
            MarkDirty();
            return true;
        }

        // ================================================================
        // モーフ全選択/全解除
        // ================================================================

        public void SelectAllMorphs(int[] allMorphIndices)
        {
            if (_model == null) return;
            var oldIndices = _model.SelectedMorphIndices.ToArray();
            _model.ClearMorphSelection();
            if (allMorphIndices != null)
                foreach (int idx in allMorphIndices)
                    _model.AddToMorphSelection(idx);
            var newIndices = _model.SelectedMorphIndices.ToArray();
            RecordMorphSelectionUndo(oldIndices, newIndices, "モーフ全選択");
        }

        public void DeselectAllMorphs()
        {
            if (_model == null) return;
            var oldIndices = _model.SelectedMorphIndices.ToArray();
            _model.ClearMorphSelection();
            var newIndices = _model.SelectedMorphIndices.ToArray();
            RecordMorphSelectionUndo(oldIndices, newIndices, "モーフ全解除");
        }

        private void RecordMorphSelectionUndo(int[] oldIndices, int[] newIndices, string description)
        {
            if (_undo == null || oldIndices.SequenceEqual(newIndices)) return;
            var record = new MorphSelectionChangeRecord(oldIndices, newIndices);
            _undo.MeshListStack.Record(record, description);
            _undo.FocusMeshList();
        }

        // ================================================================
        // モーフプレビュー
        // ================================================================

        public void StartMorphPreview(int[] morphIndices)
        {
            if (_model == null) return;
            EndMorphPreview();

            _morphPreviewTargets.Clear();
            _morphPreviewBackups.Clear();

            foreach (int morphIdx in morphIndices)
            {
                var morphCtx = GetMeshContext(morphIdx);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIdx = morphCtx.MorphParentIndex;
                if (baseIdx < 0) baseIdx = FindBaseMeshIndex(morphIdx);
                if (baseIdx < 0) continue;

                var baseCtx = GetMeshContext(baseIdx);
                if (baseCtx?.MeshObject == null) continue;

                if (!_morphPreviewBackups.ContainsKey(baseIdx))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _morphPreviewBackups[baseIdx] = backup;
                }
                _morphPreviewTargets.Add((morphIdx, baseIdx));
            }
            _isMorphPreviewActive = true;
        }

        public void ApplyMorphPreview(float weight)
        {
            if (!_isMorphPreviewActive || _model == null) return;

            foreach (var (baseIndex, backup) in _morphPreviewBackups)
            {
                var baseCtx = GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            foreach (var (morphIndex, baseIndex) in _morphPreviewTargets)
            {
                var morphCtx = GetMeshContext(morphIndex);
                var baseCtx = GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * weight;
            }

            foreach (var baseIndex in _morphPreviewBackups.Keys)
            {
                var baseCtx = GetMeshContext(baseIndex);
                if (baseCtx != null) SyncPositionsOnly?.Invoke(baseCtx);
            }
            Repaint?.Invoke();
        }

        public void EndMorphPreview()
        {
            if (!_isMorphPreviewActive || _morphPreviewBackups.Count == 0)
            {
                _isMorphPreviewActive = false;
                _morphPreviewBackups.Clear();
                _morphPreviewTargets.Clear();
                return;
            }

            if (_model != null)
            {
                foreach (var (baseIndex, backup) in _morphPreviewBackups)
                {
                    var baseCtx = GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    SyncPositionsOnly?.Invoke(baseCtx);
                }
            }
            _isMorphPreviewActive = false;
            _morphPreviewBackups.Clear();
            _morphPreviewTargets.Clear();
            Repaint?.Invoke();
        }

        public int FindBaseMeshIndex(int morphMasterIndex)
        {
            if (_model == null) return -1;
            var morphCtx = GetMeshContext(morphMasterIndex);
            if (morphCtx == null) return -1;
            if (morphCtx.MorphParentIndex >= 0) return morphCtx.MorphParentIndex;

            string morphName = morphCtx.MorphName;
            string meshName = morphCtx.Name;
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < _model.MeshContextCount; i++)
                {
                    var ctx = _model.GetMeshContext(i);
                    if (ctx != null && (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror
                                        || ctx.Type == MeshType.MirrorSide) && ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshContext GetMeshContext(int masterIndex)
        {
            if (_model == null || masterIndex < 0 || masterIndex >= _model.MeshContextCount) return null;
            return _model.GetMeshContext(masterIndex);
        }

        private void MarkDirty() { if (_model != null) _model.IsDirty = true; }

        private void SelectFirstDrawableIfNeeded()
        {
            if (_model == null || _model.HasMeshSelection) return;
            var drawables = _model.TypedIndices.GetEntries(MeshCategory.Drawable);
            if (drawables.Count > 0) _model.SelectDrawable(drawables[0].MasterIndex);
        }

        private bool ApplyAttributeChange(MeshAttributeChange change)
            => ApplyAttributeChanges(new List<MeshAttributeChange> { change });

        private bool ApplyAttributeChanges(List<MeshAttributeChange> changes)
        {
            if (changes == null || changes.Count == 0) return false;
            var oldValues = new List<MeshAttributeChange>();
            foreach (var change in changes)
            {
                var ctx = GetMeshContext(change.Index);
                if (ctx == null) continue;
                var old = new MeshAttributeChange { Index = change.Index };
                if (change.IsVisible.HasValue) old.IsVisible = ctx.IsVisible;
                if (change.IsLocked.HasValue) old.IsLocked = ctx.IsLocked;
                if (change.MirrorType.HasValue) old.MirrorType = ctx.MirrorType;
                if (change.Name != null) old.Name = ctx.Name;
                oldValues.Add(old);
                if (change.IsVisible.HasValue) ctx.IsVisible = change.IsVisible.Value;
                if (change.IsLocked.HasValue) ctx.IsLocked = change.IsLocked.Value;
                if (change.MirrorType.HasValue) ctx.MirrorType = change.MirrorType.Value;
                if (change.Name != null) ctx.Name = change.Name;
            }
            if (_undo != null && oldValues.Count > 0)
            {
                var record = new MeshAttributesBatchChangeRecord(oldValues, changes);
                _undo.MeshListStack.Record(record, "属性変更");
            }
            MarkDirty();
            return oldValues.Count > 0;
        }

        private Dictionary<int, BonePoseDataSnapshot?> CaptureBonePoseSnapshots(IEnumerable<int> masterIndices)
        {
            var dict = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (int idx in masterIndices)
                dict[idx] = GetMeshContext(idx)?.BonePoseData?.CreateSnapshot();
            return dict;
        }

        private List<int> FilterWithBonePose(int[] masterIndices)
        {
            var result = new List<int>();
            foreach (int idx in masterIndices)
                if (GetMeshContext(idx)?.BonePoseData != null) result.Add(idx);
            return result;
        }

        private void RecordBonePoseUndo(
            Dictionary<int, BonePoseDataSnapshot?> before,
            Dictionary<int, BonePoseDataSnapshot?> after,
            string description)
        {
            if (_undo == null) return;
            var record = new MultiBonePoseChangeRecord();
            foreach (var kvp in before)
            {
                after.TryGetValue(kvp.Key, out var afterVal);
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    { MasterIndex = kvp.Key, OldSnapshot = kvp.Value, NewSnapshot = afterVal });
            }
            _undo.MeshListStack.Record(record, description);
            _undo.FocusMeshList();
        }

        private void RecordMorphUndo(MeshListUndoRecord record, string description)
        {
            if (_undo == null) return;
            _undo.MeshListStack.Record(record, description);
            _undo.FocusMeshList();
        }

        private static bool ListsEqual(List<MeshContext> a, List<MeshContext> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        private static bool MapsEqual(Dictionary<MeshContext, MeshContext> a, Dictionary<MeshContext, MeshContext> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
                if (!b.TryGetValue(kvp.Key, out var val) || !ReferenceEquals(kvp.Value, val)) return false;
            return true;
        }
    }
}
