// PolyLing_SummaryNotify.cs
// PanelContext生成・LiveProjectView配信・コマンドディスパッチ
// SummaryBuilder不使用。LiveProjectViewが現物ModelContextを直接参照。

using System.Linq;
using Poly_Ling.Data;
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
}
