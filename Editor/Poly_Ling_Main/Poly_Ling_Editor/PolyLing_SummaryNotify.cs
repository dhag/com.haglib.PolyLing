// PolyLing_SummaryNotify.cs
// PanelContext生成・LiveProjectView配信・コマンドディスパッチ
// SummaryBuilder不使用。LiveProjectViewが現物ModelContextを直接参照。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Remote;
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

        // 開いているパネルへPanelContextを再配布
        ToolContextReconnector.ReconnectAllPanelContexts(_panelContext);

#if UNITY_EDITOR
        // RemoteServerが開いていなければ開いてサーバー開始
        var remote = RemoteServer.FindInstance()
            ?? UnityEditor.EditorWindow.GetWindow<RemoteServer>("Remote Server");
        remote.Show();
        // PanelCommand経由のコマンドを全処理できるよう DispatchPanelCommand を注入
        remote.SetDispatchCommand(DispatchPanelCommand);
        if (!remote.IsRunning)
            remote.StartServer();
#endif
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

            // --- UV操作 ---
            case ApplyUvUnwrapCommand c:
                HandleApplyUvUnwrap(c);
                NotifyPanels(ChangeKind.Attributes);
                return;
            case UvToXyzCommand c:
                HandleUvToXyz(c);
                NotifyPanels(ChangeKind.ListStructure);
                return;
            case XyzToUvCommand c:
                HandleXyzToUv(c);
                NotifyPanels(ChangeKind.Attributes);
                return;

            // --- メッシュマージ ---
            case MergeMeshesCommand c:
                HandleMergeMeshesCommand(c);
                NotifyPanels(ChangeKind.ListStructure);
                return;

            // --- BoneTransform ---
            case SetBoneTransformValueCommand c:
                _meshListOps.SetBoneTransformValue(c.MasterIndices, c.TargetField, c.Value);
                Repaint();
                NotifyPanels(ChangeKind.Attributes);
                return;
            case BeginBoneTransformSliderDragCommand c:
                _meshListOps.BeginBoneTransformSliderDrag(c.MasterIndices);
                return; // NotifyPanels不要
            case EndBoneTransformSliderDragCommand c:
                _meshListOps.EndBoneTransformSliderDrag(c.Description);
                return; // NotifyPanels不要（Undo記録のみ）

            default:
                Debug.LogWarning($"[PolyLing] Unknown PanelCommand: {cmd.GetType().Name}");
                return;
        }
    }

}