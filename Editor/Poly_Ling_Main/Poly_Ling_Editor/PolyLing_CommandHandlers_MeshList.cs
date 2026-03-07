// PolyLing_CommandHandlers_MeshList.cs
// メッシュ選択・選択辞書ハンドラ
// DispatchPanelCommand から呼ばれる private メソッド群

using Poly_Ling.Data;
using Poly_Ling.Model;

public partial class PolyLing
{
    // ================================================================
    // 選択
    // ================================================================

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
    // 選択辞書
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

        var current = sets[cmd.SetIndex];
        string newName = cmd.NewName;
        if (_model.FindMeshSelectionSetByName(newName) != null && newName != current.Name)
            newName = _model.GenerateUniqueMeshSelectionSetName(newName);
        current.Name = newName;
    }
}
