// PolyLing_CommandHandlers_Parts.cs
// パーツ選択辞書ハンドラ
// DispatchPanelCommand から呼ばれる private メソッド群

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

public partial class PolyLing
{
    // ================================================================
    // パーツ選択辞書
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
                Mode     = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges    = new HashSet<VertexPair>(set.Edges),
                Faces    = new HashSet<int>(set.Faces),
                Lines    = new HashSet<int>(set.Lines)
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
}
