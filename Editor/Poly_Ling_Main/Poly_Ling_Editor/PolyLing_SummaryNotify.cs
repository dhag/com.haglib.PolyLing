// PolyLing_SummaryNotify.cs
// PanelContext生成・LiveProjectView配信・コマンドディスパッチ
// SummaryBuilder不使用。LiveProjectViewが現物ModelContextを直接参照。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using UnityEngine;

public partial class PolyLing
{
    private PanelContext _panelContext;
    private MeshListOps _meshListOps;
    private LiveProjectView _liveProjectView;

    /// <summary>
    /// PanelContextとMeshListOpsを生成する。OnEnableから呼ぶ。
    /// </summary>
    private void InitPanelContext()
    {
        _panelContext = new PanelContext(DispatchPanelCommand);
        _meshListOps = new MeshListOps(_model, _undoController);
        _liveProjectView = new LiveProjectView(_project);
        SetupMeshListOpsCallbacks();
    }

    /// <summary>
    /// MeshListOpsにGPU同期コールバックを設定する。
    /// </summary>
    private void SetupMeshListOpsCallbacks()
    {
        if (_meshListOps == null) return;
        _meshListOps.SyncPositionsOnly = ctx => _toolContext?.SyncMeshContextPositionsOnly?.Invoke(ctx);
        _meshListOps.SyncMesh = () => _toolContext?.SyncMesh?.Invoke();
        _meshListOps.Repaint = () => Repaint();
    }

    /// <summary>
    /// モデル切り替え時にMeshListOpsのコンテキストを更新する。
    /// OnCurrentModelChangedから呼ぶ。
    /// </summary>
    private void UpdateMeshListOpsContext()
    {
        _meshListOps?.SetContext(_model, _undoController);
        SetupMeshListOpsCallbacks();
        NotifyPanels(ChangeKind.ModelSwitch);
    }

    /// <summary>
    /// LiveProjectView経由でPanelContextに通知する。
    /// </summary>
    private void NotifyPanels(ChangeKind kind = ChangeKind.ListStructure)
    {
        if (_project == null || _panelContext == null || _liveProjectView == null) return;

        // リスト構造変更時はLiveMeshViewリストを再構築
        if (kind == ChangeKind.ListStructure || kind == ChangeKind.ModelSwitch)
            _liveProjectView.InvalidateLists();

        _panelContext.Notify(_liveProjectView, kind);
    }

    /// <summary>
    /// パネルからのコマンドをディスパッチする。
    /// </summary>
    private void DispatchPanelCommand(PanelCommand cmd)
    {
        if (_model == null || _meshListOps == null) return;

        switch (cmd)
        {
            // --- 選択 ---
            case SelectMeshCommand sel:
                HandleSelectMeshCommand(sel);
                NotifyPanels(ChangeKind.Selection);
                return;

            // --- 属性変更 ---
            case ToggleVisibilityCommand c:
                _meshListOps.ToggleVisibility(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case SetBatchVisibilityCommand c:
                _meshListOps.SetBatchVisibility(c.MasterIndices, c.Visible);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ToggleLockCommand c:
                _meshListOps.ToggleLock(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case CycleMirrorTypeCommand c:
                _meshListOps.CycleMirrorType(c.MasterIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenameMeshCommand c:
                _meshListOps.RenameMesh(c.MasterIndex, c.NewName);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- リスト操作（構造変更） ---
            case AddMeshCommand _:
                var newCtx = _meshListOps.AddNewMesh();
                if (newCtx != null) AddMeshContextWithUndo(newCtx);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case DeleteMeshesCommand c:
                foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                    RemoveMeshContextWithUndo(idx);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case DuplicateMeshesCommand c:
                foreach (int idx in c.MasterIndices)
                    DuplicateMeshContentWithUndo(idx);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- リオーダー（構造変更） ---
            case ReorderMeshesCommand c:
                _meshListOps.ReorderMeshes(c.Category, c.Entries);
                _model?.OnListChanged?.Invoke();
                Repaint();
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- BonePose ---
            case InitBonePoseCommand c:
                _meshListOps.InitBonePose(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case SetBonePoseActiveCommand c:
                _meshListOps.SetBonePoseActive(c.MasterIndices, c.Active);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ResetBonePoseLayersCommand c:
                _meshListOps.ResetBonePoseLayers(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BakePoseToBindPoseCommand c:
                _meshListOps.BakePoseToBindPose(c.MasterIndices);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case SetBonePoseRestValueCommand c:
                _meshListOps.SetBonePoseRestValue(c.MasterIndices, c.TargetField, c.Value);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BeginBonePoseSliderDragCommand c:
                _meshListOps.BeginSliderDrag(c.MasterIndices);
                return; // NotifyPanels不要
            case EndBonePoseSliderDragCommand c:
                _meshListOps.EndSliderDrag(c.Description);
                return; // NotifyPanels不要（Undo記録のみ）

            // --- モーフ変換（構造変更） ---
            case ConvertMeshToMorphCommand c:
                _meshListOps.ConvertMeshToMorph(c.SourceIndex, c.ParentIndex, c.MorphName, c.Panel);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case ConvertMorphToMeshCommand c:
                _meshListOps.ConvertMorphToMesh(c.MasterIndices);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case CreateMorphSetCommand c:
                _meshListOps.CreateMorphSet(c.SetName, c.MorphType, c.MorphIndices);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- モーフプレビュー ---
            case StartMorphPreviewCommand c:
                _meshListOps.StartMorphPreview(c.MorphIndices);
                return; // NotifyPanels不要
            case ApplyMorphPreviewCommand c:
                _meshListOps.ApplyMorphPreview(c.Weight);
                return; // NotifyPanels不要（頂点更新のみ）
            case EndMorphPreviewCommand _:
                _meshListOps.EndMorphPreview();
                _toolContext?.SyncMesh?.Invoke();
                return;

            // --- モーフ全選択/全解除 ---
            case SelectAllMorphsCommand c:
                _meshListOps.SelectAllMorphs(c.AllMorphIndices);
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;
            case DeselectAllMorphsCommand _:
                _meshListOps.DeselectAllMorphs();
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;

            // --- パーツ選択辞書 ---
            case SavePartsSetCommand c:
                HandleSavePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case LoadPartsSetCommand c:
                HandleLoadPartsSet(c, additive: false, subtract: false);
                return;
            case AddPartsSetCommand c:
                HandleLoadPartsSet(new LoadPartsSetCommand(c.ModelIndex, c.SetIndex), additive: true, subtract: false);
                return;
            case SubtractPartsSetCommand c:
                HandleLoadPartsSet(new LoadPartsSetCommand(c.ModelIndex, c.SetIndex), additive: false, subtract: true);
                return;
            case DeletePartsSetCommand c:
                HandleDeletePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenamePartsSetCommand c:
                HandleRenamePartsSet(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ExportPartsSetsCsvCommand _:
                HandleExportPartsSetsCsv();
                return;
            case ImportPartsSetCsvCommand _:
                HandleImportPartsSetCsv();
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- モデルブレンド ---
            case CreateBlendCloneCommand c:
                HandleCreateBlendClone(c);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case ApplyModelBlendCommand c:
                HandleApplyModelBlend(c);
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case PreviewModelBlendCommand c:
                HandlePreviewModelBlend(c);
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
                return;

            // --- モデル操作 ---
            case SwitchModelCommand c:
                _project?.SelectModel(c.TargetModelIndex);
                // SelectModel が OnCurrentModelChanged を発火し UpdateMeshListOpsContext → NotifyPanels まで実行される
                return;
            case RenameModelCommand c:
                var renameTarget = _project?.GetModel(c.ModelIndex);
                if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                {
                    renameTarget.Name = c.NewName;
                    _project?.OnModelsChanged?.Invoke();
                }
                return;
            case DeleteModelCommand c:
                _project?.RemoveModelAt(c.ModelIndex);
                // RemoveModelAt が OnModelsChanged を発火する
                return;

            // --- 選択辞書 ---
            case SaveSelectionDictionaryCommand c:
                HandleSaveSelectionDictionary(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case ApplySelectionDictionaryCommand c:
                HandleApplySelectionDictionary(c);
                _toolContext?.OnMeshSelectionChanged?.Invoke();
                NotifyPanels(ChangeKind.Selection);
                return;
            case DeleteSelectionDictionaryCommand c:
                if (c.SetIndex >= 0 && c.SetIndex < _model.MeshSelectionSets.Count)
                    _model.MeshSelectionSets.RemoveAt(c.SetIndex);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case RenameSelectionDictionaryCommand c:
                HandleRenameSelectionDictionary(c);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- パネル側直接変更後の通知 ---
            case NotifyListStructureChangedCommand _:
                _model?.OnListChanged?.Invoke();
                _toolContext?.SyncMesh?.Invoke();
                Repaint();
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case NotifyDictionaryChangedCommand _:
                NotifyPanels(ChangeKind.Attributes);
                return;

            default:
                Debug.LogWarning($"[PolyLing] Unknown PanelCommand: {cmd.GetType().Name}");
                return;
        }
    }

    private void HandleSelectMeshCommand(SelectMeshCommand cmd)
    {
        if (_model == null) return;
        switch (cmd.Category)
        {
            case MeshCategory.Drawable:
                _model.ClearMeshSelection();
                foreach (int idx in cmd.Indices) _model.AddToMeshSelection(idx);
                break;
            case MeshCategory.Bone:
                _model.ClearBoneSelection();
                foreach (int idx in cmd.Indices) _model.AddToBoneSelection(idx);
                break;
            case MeshCategory.Morph:
                _model.ClearMorphSelection();
                foreach (int idx in cmd.Indices) _model.AddToMorphSelection(idx);
                break;
        }
        _toolContext?.OnMeshSelectionChanged?.Invoke();
    }

    // ================================================================
    // 選択辞書ハンドラ
    // ================================================================

    private void HandleSaveSelectionDictionary(SaveSelectionDictionaryCommand cmd)
    {
        if (_model == null) return;

        var category = cmd.Category switch
        {
            MeshCategory.Drawable => ModelContext.SelectionCategory.Mesh,
            MeshCategory.Bone     => ModelContext.SelectionCategory.Bone,
            MeshCategory.Morph    => ModelContext.SelectionCategory.Morph,
            _                     => ModelContext.SelectionCategory.Mesh
        };

        string name = string.IsNullOrEmpty(cmd.SetName)
            ? _model.GenerateUniqueMeshSelectionSetName("MeshSet")
            : cmd.SetName;
        if (_model.FindMeshSelectionSetByName(name) != null)
            name = _model.GenerateUniqueMeshSelectionSetName(name);

        var set = new MeshSelectionSet(name) { Category = category };
        foreach (var n in cmd.MeshNames)
            if (!string.IsNullOrEmpty(n) && !set.MeshNames.Contains(n))
                set.MeshNames.Add(n);

        _model.MeshSelectionSets.Add(set);
    }

    private void HandleApplySelectionDictionary(ApplySelectionDictionaryCommand cmd)
    {
        if (_model == null) return;
        var sets = _model.MeshSelectionSets;
        if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

        if (cmd.AddToExisting)
            sets[cmd.SetIndex].AddTo(_model);
        else
            sets[cmd.SetIndex].ApplyTo(_model);
    }

    private void HandleRenameSelectionDictionary(RenameSelectionDictionaryCommand cmd)
    {
        if (_model == null) return;
        var sets = _model.MeshSelectionSets;
        if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

        string newName = cmd.NewName;
        // 同名が既存にある場合はユニーク名を生成（自分自身は除外）
        var current = sets[cmd.SetIndex];
        if (_model.FindMeshSelectionSetByName(newName) != null && newName != current.Name)
            newName = _model.GenerateUniqueMeshSelectionSetName(newName);
        current.Name = newName;
    }

    // ================================================================
    // パーツ選択辞書ハンドラ
    // ================================================================

    private void HandleSavePartsSet(SavePartsSetCommand cmd)
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null) return;

        var sel = _selectionState;
        if (sel == null || !sel.HasAnySelection) return;

        string name = string.IsNullOrEmpty(cmd.SetName)
            ? meshCtx.GenerateUniqueSelectionSetName("Selection")
            : cmd.SetName;
        if (meshCtx.FindSelectionSetByName(name) != null)
            name = meshCtx.GenerateUniqueSelectionSetName(name);

        var set = PartsSelectionSet.FromCurrentSelection(
            name, sel.Vertices, sel.Edges, sel.Faces, sel.Lines, sel.Mode);
        meshCtx.PartsSelectionSetList.Add(set);
    }

    private void HandleLoadPartsSet(LoadPartsSetCommand cmd, bool additive, bool subtract)
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null) return;
        var sets = meshCtx.PartsSelectionSetList;
        if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;

        var set = sets[cmd.SetIndex];
        var sel = _selectionState;
        if (sel == null) return;

        if (additive)
        {
            var snap = sel.CreateSnapshot();
            snap.Vertices.UnionWith(set.Vertices);
            snap.Edges.UnionWith(set.Edges);
            snap.Faces.UnionWith(set.Faces);
            snap.Lines.UnionWith(set.Lines);
            sel.RestoreFromSnapshot(snap);
        }
        else if (subtract)
        {
            var snap = sel.CreateSnapshot();
            snap.Vertices.ExceptWith(set.Vertices);
            snap.Edges.ExceptWith(set.Edges);
            snap.Faces.ExceptWith(set.Faces);
            snap.Lines.ExceptWith(set.Lines);
            sel.RestoreFromSnapshot(snap);
        }
        else
        {
            var snap = new SelectionSnapshot
            {
                Mode = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges = new HashSet<VertexPair>(set.Edges),
                Faces = new HashSet<int>(set.Faces),
                Lines = new HashSet<int>(set.Lines)
            };
            sel.RestoreFromSnapshot(snap);
        }
        _toolContext?.Repaint?.Invoke();
        NotifyPanels(ChangeKind.Selection);
    }

    private void HandleDeletePartsSet(DeletePartsSetCommand cmd)
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null) return;
        var sets = meshCtx.PartsSelectionSetList;
        if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;
        sets.RemoveAt(cmd.SetIndex);
    }

    private void HandleRenamePartsSet(RenamePartsSetCommand cmd)
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null) return;
        var sets = meshCtx.PartsSelectionSetList;
        if (cmd.SetIndex < 0 || cmd.SetIndex >= sets.Count) return;
        var set = sets[cmd.SetIndex];
        string newName = cmd.NewName;
        if (meshCtx.FindSelectionSetByName(newName) != null && newName != set.Name)
            newName = meshCtx.GenerateUniqueSelectionSetName(newName);
        set.Name = newName;
    }

    private void HandleExportPartsSetsCsv()
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null || meshCtx.PartsSelectionSetList.Count == 0) return;
        Poly_Ling.UI.PartsSetCsvHelper.ExportSets(meshCtx);
    }

    private void HandleImportPartsSetCsv()
    {
        var meshCtx = _model?.FirstSelectedMeshContext;
        if (meshCtx == null) return;
        Poly_Ling.UI.PartsSetCsvHelper.ImportSet(meshCtx);
    }

    // ================================================================
    // モデルブレンドハンドラ
    // ================================================================

    private void HandleCreateBlendClone(CreateBlendCloneCommand cmd)
    {
        if (_project == null) return;
        var src = _project.GetModel(cmd.ModelIndex);
        if (src == null) return;

        string uniqueName = _project.GenerateUniqueModelName(
            string.IsNullOrEmpty(cmd.CloneNameBase)
                ? src.Name + "_blend"
                : cmd.CloneNameBase);

        var clone = DeepCloneModelContext(src, uniqueName);
        if (clone == null) return;
        _project.AddModel(clone);
    }

    /// <summary>
    /// ModelContext をディープコピーする（BindPose 等 DTO 非保存フィールドを含む）
    /// </summary>
    private static ModelContext DeepCloneModelContext(ModelContext src, string newName)
    {
        var dst = new ModelContext { Name = newName };

        // MeshContext をディープコピー
        for (int i = 0; i < src.MeshContextCount; i++)
        {
            var s = src.GetMeshContext(i);
            if (s == null) continue;

            var meshObj = s.MeshObject?.Clone();
            if (meshObj == null) continue;

            var d = new MeshContext
            {
                Name                  = s.Name,
                MeshObject            = meshObj,
                UnityMesh             = meshObj.ToUnityMesh(),
                OriginalPositions     = (Vector3[])meshObj.Positions.Clone(),
                BoneTransform         = CloneBoneTransform(s.BoneTransform),
                // 階層
                ParentIndex           = s.ParentIndex,
                Depth                 = s.Depth,
                HierarchyParentIndex  = s.HierarchyParentIndex,
                // 表示
                IsVisible             = s.IsVisible,
                IsLocked              = s.IsLocked,
                IsFolding             = s.IsFolding,
                // ミラー
                MirrorType            = s.MirrorType,
                MirrorAxis            = s.MirrorAxis,
                MirrorDistance        = s.MirrorDistance,
                MirrorMaterialOffset  = s.MirrorMaterialOffset,
                // ベイクミラー
                BakedMirrorSourceIndex = s.BakedMirrorSourceIndex,
                HasBakedMirrorChild   = s.HasBakedMirrorChild,
                // モーフ
                MorphParentIndex      = s.MorphParentIndex,
                // ★BindPose（DTOに保存されないため直接コピー必須）
                BindPose              = s.BindPose,
                // BonePoseData
                BonePoseData          = s.BonePoseData?.Clone(),
                // MorphBaseData
                MorphBaseData         = s.MorphBaseData?.Clone(),
            };

            dst.Add(d);
        }

        // Materials
        if (src.MaterialReferences != null)
            foreach (var m in src.MaterialReferences)
                dst.MaterialReferences.Add(m);   // Material参照は共有でよい
        dst.CurrentMaterialIndex = src.CurrentMaterialIndex;

        if (src.DefaultMaterialReferences != null)
            foreach (var m in src.DefaultMaterialReferences)
                dst.DefaultMaterialReferences.Add(m);
        dst.DefaultCurrentMaterialIndex = src.DefaultCurrentMaterialIndex;
        dst.AutoSetDefaultMaterials     = src.AutoSetDefaultMaterials;

        // MirrorPairs（インデックス参照なので再構築）
        if (src.MirrorPairs != null)
        {
            foreach (var sp in src.MirrorPairs)
            {
                int ri = src.IndexOf(sp.Real);
                int mi = src.IndexOf(sp.Mirror);
                if (ri < 0 || mi < 0 || ri >= dst.Count || mi >= dst.Count) continue;
                var pair = new MirrorPair
                {
                    Real   = dst.GetMeshContext(ri),
                    Mirror = dst.GetMeshContext(mi),
                    Axis   = sp.Axis,
                };
                if (pair.Build())
                    dst.MirrorPairs.Add(pair);
            }
        }

        return dst;
    }

    private static BoneTransform CloneBoneTransform(BoneTransform src)
    {
        if (src == null) return new BoneTransform();
        var dst = new BoneTransform();
        dst.CopyFrom(src);
        return dst;
    }

    private void HandleApplyModelBlend(ApplyModelBlendCommand cmd)
    {
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, cmd.RecalcNormals);
    }

    private void HandlePreviewModelBlend(PreviewModelBlendCommand cmd)
    {
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, recalcNormals: false);
    }

    private void ExecuteBlend(int sourceModelIndex, int cloneModelIndex,
        float[] weights, bool[] meshEnabled, bool recalcNormals)
    {
        if (_project == null) return;
        var cloneModel = _project.GetModel(cloneModelIndex);
        if (cloneModel == null) return;

        // ウェイト正規化
        float total = 0f;
        foreach (var w in weights) total += w;
        float[] nw = new float[weights.Length];
        if (total > 0f)
            for (int i = 0; i < weights.Length; i++) nw[i] = weights[i] / total;
        else
        {
            float eq = 1f / weights.Length;
            for (int i = 0; i < weights.Length; i++) nw[i] = eq;
        }

        var cloneDrawables = cloneModel.DrawableMeshes;
        int drawableCount  = cloneDrawables.Count;

        for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
        {
            if (drawIdx < meshEnabled.Length && !meshEnabled[drawIdx]) continue;

            var targetMesh = cloneDrawables[drawIdx].Context?.MeshObject;
            if (targetMesh == null) continue;

            var blended = new Vector3[targetMesh.VertexCount];

            for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length) continue;
                float w = nw[modelIdx];
                if (w <= 0f) continue;

                var srcModel     = _project.GetModel(modelIdx);
                var srcDrawables = srcModel?.DrawableMeshes;
                if (srcDrawables == null || drawIdx >= srcDrawables.Count) continue;

                var srcMesh = srcDrawables[drawIdx].Context?.MeshObject;
                if (srcMesh == null) continue;

                int vCount = Mathf.Min(blended.Length, srcMesh.VertexCount);
                for (int vi = 0; vi < vCount; vi++)
                    blended[vi] += srcMesh.Vertices[vi].Position * w;
            }

            for (int vi = 0; vi < blended.Length; vi++)
                targetMesh.Vertices[vi].Position = blended[vi];

            if (recalcNormals)
                targetMesh.RecalculateSmoothNormals();
        }
    }
}