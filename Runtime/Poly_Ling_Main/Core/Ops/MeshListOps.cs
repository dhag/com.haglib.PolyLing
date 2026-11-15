// MeshListOps.cs
// モデル内のメッシュを操作するヘルパークラス
// パネルからは直接呼ばない。メインルーチンのコマンドディスパッチから呼ぶ。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Ops
{
    public class MeshListOps
    {
        private ModelContext _model;
        private MeshUndoController _undo;

        // メインルーチンが設定するコールバック（GPU同期用）
        public Action<MeshContext> SyncPositionsOnly { get; set; }
        public Action SyncMesh { get; set; }
        public Action Repaint { get; set; }

        // 形の試し表示の状態（表情の試し表示と同じ足し込み。PolyLing_モーフ整理計画.md 3.6）
        private bool _isShapePreviewActive;
        private readonly ShapeBlendBuffer _shapePreview = new ShapeBlendBuffer();
        private readonly float[] _shapePreviewWeight = new float[1];

        // スライダードラッグ用スナップショット（BonePose）
        private Dictionary<int, BonePoseDataSnapshot> _sliderDragBeforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();

        // スライダードラッグ用スナップショット（BoneTransform）
        private Dictionary<int, BoneTransformSnapshot> _boneTransformSliderSnapshots = new Dictionary<int, BoneTransformSnapshot>();

        public bool IsShapePreviewActive => _isShapePreviewActive;

        public MeshListOps(ModelContext model, MeshUndoController undo)
        {
            _model = model;
            _undo = undo;
        }

        public void SetContext(ModelContext model, MeshUndoController undo)
        {
            EndShapePreview();
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

        /// <summary>
        /// オブジェクトの編集者（担当者）を設定・解放する。
        ///
        /// editorName == "" のとき解放。requesterName が null または空のときは
        /// 権限チェックを行わない（ローカル・ホスト操作扱い）。
        /// force が false のとき、他人が担当中のオブジェクトは変更せずスキップする。
        ///
        /// objectIds を与えた場合、masterIndices と同じ並びで安定IDを照合し、
        /// 一致しないものはスキップする（リスト構造がズレている＝古いビューからの要求）。
        /// </summary>
        /// <returns>実際に変更した件数</returns>
        public int SetObjectEditor(
            int[] masterIndices, string editorName,
            ulong[] objectIds = null, string requesterName = null, bool force = false)
        {
            if (masterIndices == null || masterIndices.Length == 0) return 0;
            editorName = editorName ?? "";

            bool checkPermission = !force && !string.IsNullOrEmpty(requesterName);
            var changes = new List<MeshAttributeChange>();

            for (int i = 0; i < masterIndices.Length; i++)
            {
                int idx = masterIndices[i];
                var ctx = GetMeshContext(idx);
                if (ctx == null) continue;

                // 安定IDの照合（ズレ検出）
                if (objectIds != null && i < objectIds.Length && objectIds[i] != 0UL &&
                    ctx.ObjectId != objectIds[i])
                    continue;

                // 他人が担当中なら触らない
                if (checkPermission && !ctx.IsEditableBy(requesterName)) continue;

                string current = ctx.EditorName ?? "";
                if (current == editorName) continue;

                changes.Add(new MeshAttributeChange { Index = idx, EditorName = editorName });
            }

            if (changes.Count == 0) return 0;

            // 担当者は作業中だけの一時ロック（操作経路統一計画.md L-1）。
            // Undo には積まない。積むと Undo でロックが戻り、作業の履歴にロック操作が混ざる。
            foreach (var change in changes)
            {
                var ctx = GetMeshContext(change.Index);
                if (ctx != null) ctx.EditorName = change.EditorName;
            }
            return changes.Count;
        }

        /// <summary>指定ユーザーが担当している全オブジェクトを解放する（ホスト側の一括解放用）。</summary>
        public int ReleaseAllByEditor(string editorName)
        {
            if (_model == null || string.IsNullOrEmpty(editorName)) return 0;
            var indices = new List<int>();
            for (int i = 0; i < _model.MeshContextCount; i++)
            {
                var ctx = _model.GetMeshContext(i);
                if (ctx != null && ctx.EditorName == editorName) indices.Add(i);
            }
            return SetObjectEditor(indices.ToArray(), "", null, null, force: true);
        }

        public bool CycleMirrorType(int masterIndex)
        {
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null || ctx.IsBakedMirror) return false;
            // なし→分離→結合→なし。3 以上は MeshContext.MirrorType の定義に無く、
            // MQO の mirror 属性へそのまま書き出されてしまうため作らない。
            int next = Poly_Ling.View.MirrorViewUtil.NextType(ctx.MirrorType);
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

        /// <summary>
        /// TreeView 折りたたみ状態を設定 (Undo 記録あり)
        /// </summary>
        public bool SetMeshFolding(int masterIndex, bool isFolding)
        {
            var ctx = GetMeshContext(masterIndex);
            if (ctx == null) return false;
            if (ctx.IsFolding == isFolding) return false; // 変更なし
            return ApplyAttributeChange(new MeshAttributeChange
                { Index = masterIndex, IsFolding = isFolding });
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
            => ReorderMeshes(category, entries, preserveWorldTransform: false);

        /// <summary>
        /// 並べ替えと親の付け替え。
        ///
        /// preserveWorldTransform が true のとき、親が変わった対象の
        /// BoneTransform を組み直してワールド姿勢を保つ。
        /// ComputeWorldMatrices は「親のワールド × 自身のローカル」と積む
        /// （ModelContext.cs:1746-1748）ため、組み直さないと親が付いた瞬間に
        /// 子のワールド位置が親のぶんだけ飛ぶ。
        ///
        /// 既定を false にしてあるのは、この引数を持たない従来の呼び出しの
        /// 挙動を変えないため。コマンド経由（ReorderMeshesCommand）は true で入る。
        /// </summary>
        public bool ReorderMeshes(
            MeshCategory category, ReorderMeshesCommand.ReorderEntry[] entries,
            bool preserveWorldTransform)
        {
            if (_model == null || entries == null || entries.Length == 0) return false;

            // 付け替え前のワールド姿勢を控える。親を書き換える前でなければ意味がない。
            Dictionary<MeshContext, Matrix4x4> beforeWorld = null;
            Dictionary<MeshContext, BoneTransform> beforeTransform = null;
            if (preserveWorldTransform)
            {
                beforeWorld = HierarchyReparentOps.CaptureWorld(_model);

                beforeTransform = new Dictionary<MeshContext, BoneTransform>();
                foreach (var mc in _model.MeshContextList)
                    if (mc?.BoneTransform != null)
                        beforeTransform[mc] = new BoneTransform(mc.BoneTransform);
            }

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
            var preDepthMap = new Dictionary<MeshContext, int>();
            foreach (var mc in _model.MeshContextList) preDepthMap[mc] = mc.Depth;

            // カテゴリのMeshContextマスターインデックス→MeshContext マップ
            var entryMap = new Dictionary<int, ReorderMeshesCommand.ReorderEntry>();
            foreach (var e in entries) entryMap[e.MasterIndex] = e;

            // e.MasterIndex は「並べ替え前」のインデックス。
            // MeshContextList を差し替えた後にこのインデックスで引くと別のオブジェクトを
            // 掴むため、差し替え前にここで実体へ解決しておく。
            var ctxByOldIndex = new Dictionary<int, MeshContext>();
            for (int i = 0; i < preOrderedList.Count; i++)
                ctxByOldIndex[i] = preOrderedList[i];

            MeshContext ResolveOld(int oldIndex)
                => ctxByOldIndex.TryGetValue(oldIndex, out var c) ? c : null;

            var categoryContexts = new HashSet<MeshContext>();
            foreach (var e in entries)
            {
                var ctx = ResolveOld(e.MasterIndex);
                if (ctx != null) categoryContexts.Add(ctx);
            }

            // 新しい順序のMeshContext配列（entriesの順序に従う）
            var newCategoryOrder = new List<MeshContext>();
            foreach (var e in entries)
            {
                var ctx = ResolveOld(e.MasterIndex);
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

            _model.ReplaceMeshContexts(newOrder);

            // 並べ替えで動いた索引参照を付け替える。
            // ここを飛ばすと、entries に入っていない要素（既定でツリーから外れる
            // ミラー側など）が持つ BakedMirrorSourceIndex /
            // ParentIndex が並べ替え前の位置を指したまま残り、別のオブジェクトを
            // ミラー元や親として掴む。
            // 下の entries ループより先に呼ぶこと（明示指定の新しい親を最後に勝たせる）。
            _model.RemapIndexReferencesAfterReorder(preOrderedList);

            // Depth / 親の更新
            // MeshContextList は差し替え済みなので、対象も親も「並べ替え前の
            // インデックス」から実体を引き、位置だけ新リストから求める。
            // HierarchyParentIndex だけ書けば足りる。ParentIndex は同じ入れ物
            // （MeshObject.ParentIndex が HierarchyParentIndex への委譲）なので、
            // 片方だけ書いて食い違う、ということが起きない。
            foreach (var e in entries)
            {
                var ctx = ResolveOld(e.MasterIndex);
                if (ctx == null) continue;
                ctx.Depth = e.NewDepth;
                if (e.NewParentMasterIndex >= 0)
                {
                    var parentCtx = ResolveOld(e.NewParentMasterIndex);
                    int parentIdx = parentCtx != null ? _model.MeshContextList.IndexOf(parentCtx) : -1;
                    ctx.HierarchyParentIndex = parentIdx >= 0 ? parentIdx : -1;
                }
                else
                {
                    ctx.HierarchyParentIndex = -1;
                }
            }

            // ワールド姿勢の維持。
            //   親が変わった対象だけを組み直す。親が同じものまで触ると、
            //   丸め誤差で姿勢がじりじり動く。
            Dictionary<MeshContext, BoneTransform> afterTransform = null;
            if (preserveWorldTransform && beforeWorld != null)
            {
                var moved = new HashSet<MeshContext>();
                foreach (var e in entries)
                {
                    var ctx = ResolveOld(e.MasterIndex);
                    if (ctx == null) continue;

                    preParentMap.TryGetValue(ctx, out var oldParent);
                    MeshContext newParent = null;
                    if (ctx.HierarchyParentIndex >= 0 && ctx.HierarchyParentIndex < _model.MeshContextCount)
                        newParent = _model.GetMeshContext(ctx.HierarchyParentIndex);

                    if (!ReferenceEquals(oldParent, newParent)) moved.Add(ctx);
                }

                if (moved.Count > 0)
                {
                    var warns = new List<string>();
                    HierarchyReparentOps.RestoreWorld(_model, beforeWorld, moved, warns);
                    foreach (string w in warns) Debug.LogWarning("[ReorderMeshes] " + w);

                    afterTransform = new Dictionary<MeshContext, BoneTransform>();
                    foreach (var mc in _model.MeshContextList)
                        if (mc?.BoneTransform != null)
                            afterTransform[mc] = new BoneTransform(mc.BoneTransform);
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
            var newDepthMap = new Dictionary<MeshContext, int>();
            foreach (var mc in _model.MeshContextList) newDepthMap[mc] = mc.Depth;

            if (!ListsEqual(preOrderedList, _model.MeshContextList)
             || !MapsEqual(preParentMap, newParentMap)
             || !DepthMapsEqual(preDepthMap, newDepthMap))
            {
                if (_undo != null)
                {

                    var record = new MeshReorderChangeRecord
                    {
                        OldOrderedList = preOrderedList,
                        NewOrderedList = new List<MeshContext>(_model.MeshContextList),
                        OldParentMap = preParentMap,
                        NewParentMap = newParentMap,
                        OldDepthMap  = preDepthMap,
                        NewDepthMap  = newDepthMap,

                        // 姿勢を組み直したときだけ入れる。組み直していなければ null。
                        OldTransformMap = afterTransform != null ? beforeTransform : null,
                        NewTransformMap = afterTransform,
                    };
                    {
                        string __dbgDesc = "メッシュ順序変更";
                        PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                        _undo.MeshListStack.Record(record, __dbgDesc);
                    }
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
                MeshCategory.Drawable => _model.SelectedDrawableMeshIndices,
                MeshCategory.Bone => _model.SelectedBoneIndices,
                _ => _model.SelectedDrawableMeshIndices
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
                // ポーズを畳む操作なので、バインドはポーズ込みで撮る。
                Poly_Ling.Ops.BindPoseOps.BakeCurrentPoseToBind(ctx);
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
                {
                    string __dbgDesc = "BindPoseにベイク";
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    _undo?.MeshListStack.Record(record, __dbgDesc);
                }
                _undo?.FocusMeshList();
            }
            MarkDirty();
            return true;
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
                {
                    string __dbgDesc = description;
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    _undo.MeshListStack.Record(record, __dbgDesc);
                }
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
                {
                    string __dbgDesc = description;
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    _undo.MeshListStack.Record(record, __dbgDesc);
                }
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
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _undo.MeshListStack.Record(record, __dbgDesc);
            }
            _undo.FocusMeshList();
        }
        // ================================================================

        // ================================================================
        // 形の試し表示（previewShapes / endShapePreview。PolyLing_モーフ整理計画.md 3.6・段 7）
        //   頂点位置は書き換えず、表示用の足し込み（WorkingPositions）に「Σ 重み ×（形 − 基本の形）」を書く
        //   （表情の試し表示・モーション再生と同じ 1 系統。ShapeBlendBuffer）。
        //   始める前に基本の形へ切り替えるのは呼び出し側（PlayerCommandDispatcher）。
        // ================================================================

        /// <summary>形の名前 × 重みで試し表示を始める（前の試し表示は終える）。当たった形の数を返す。</summary>
        public int StartShapePreview(IList<string> shapes, IList<float> weights)
        {
            if (_model == null) return 0;
            EndShapePreview();

            var terms = new List<MorphTargetTerm>();
            if (shapes != null)
                for (int k = 0; k < shapes.Count; k++)
                {
                    var def = ShapeBlendOps.FindShape(_model, shapes[k]);
                    if (def == null) continue;
                    float w = weights != null && k < weights.Count ? weights[k] : 1f;
                    terms.Add(new MorphTargetTerm(MorphTargetKey.Shape(def.Id), w));
                }
            if (terms.Count == 0) return 0;

            _shapePreview.Reset();
            _shapePreview.AddChannel(_model, terms, out _);
            _isShapePreviewActive = true;
            _shapePreviewWeight[0] = 1f;
            _shapePreview.Apply(_model, _shapePreviewWeight, SyncPositionsOnly);
            Repaint?.Invoke();
            return terms.Count;
        }

        /// <summary>形の試し表示を終え、表示を元へ戻す（頂点位置は変えていない）。</summary>
        public void EndShapePreview()
        {
            if (_isShapePreviewActive && _model != null && _shapePreview.EntryCount > 0)
            {
                _shapePreview.Clear(_model, SyncPositionsOnly);
                Repaint?.Invoke();
            }
            _isShapePreviewActive = false;
            _shapePreview.Reset();
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
            if (drawables.Count > 0) _model.SelectMesh(drawables[0].MasterIndex);
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
                if (change.IgnorePoseInArmature.HasValue) old.IgnorePoseInArmature = ctx.IgnorePoseInArmature;
                if (change.IsFolding.HasValue) old.IsFolding = ctx.IsFolding;
                if (change.EditorName != null) old.EditorName = ctx.EditorName ?? "";
                oldValues.Add(old);
                if (change.IsVisible.HasValue) ctx.IsVisible = change.IsVisible.Value;
                if (change.IsLocked.HasValue) ctx.IsLocked = change.IsLocked.Value;
                if (change.MirrorType.HasValue) ctx.MirrorType = change.MirrorType.Value;
                if (change.Name != null) ctx.Name = change.Name;
                if (change.IgnorePoseInArmature.HasValue)
                {
                    ctx.IgnorePoseInArmature = change.IgnorePoseInArmature.Value;
                    if (change.IgnorePoseInArmature.Value && ctx.BoneTransform != null)
                        ctx.BoneTransform.Rotation = Vector3.zero;
                }
                if (change.IsFolding.HasValue) ctx.IsFolding = change.IsFolding.Value;
                if (change.EditorName != null) ctx.EditorName = change.EditorName;
            }
            if (_undo != null && oldValues.Count > 0)
            {
                var record = new MeshAttributesBatchChangeRecord(oldValues, changes);
                {
                    string __dbgDesc = "属性変更";
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    _undo.MeshListStack.Record(record, __dbgDesc);
                }
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
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                _undo.MeshListStack.Record(record, __dbgDesc);
            }
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

        private static bool DepthMapsEqual(Dictionary<MeshContext, int> a, Dictionary<MeshContext, int> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
                if (!b.TryGetValue(kvp.Key, out int val) || kvp.Value != val) return false;
            return true;
        }
    }
}
