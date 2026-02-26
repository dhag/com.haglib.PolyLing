// Assets/Editor/PolyLing.SelectionSets.cs
// 選択セット公開API（スクリプトから名前指定で呼び出し用）
// UI描画はPartsSelectionSetPanelに移行済み

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

public partial class PolyLing
{
    // ================================================================
    // 名前で選択セットを呼び出し（スクリプトAPI）
    // ================================================================

    /// <summary>
    /// 名前を指定して選択セットをロード
    /// </summary>
    public bool LoadSelectionSetByName(string setName)
    {
        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext == null)
        {
            Debug.LogWarning("[SelectionSets] No current mesh context.");
            return false;
        }

        var set = meshContext.FindSelectionSetByName(setName);
        if (set == null)
        {
            Debug.LogWarning($"[SelectionSets] Set not found: {setName}");
            return false;
        }

        var snapshot = new SelectionSnapshot
        {
            Mode = set.Mode,
            Vertices = new HashSet<int>(set.Vertices),
            Edges = new HashSet<VertexPair>(set.Edges),
            Faces = new HashSet<int>(set.Faces),
            Lines = new HashSet<int>(set.Lines)
        };
        _selectionState.RestoreFromSnapshot(snapshot);

        Debug.Log($"[SelectionSets] Loaded by name: {setName}");
        Repaint();
        return true;
    }

    /// <summary>
    /// 選択セット名一覧を取得
    /// </summary>
    public List<string> GetSelectionSetNames()
    {
        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext == null)
            return new List<string>();

        var names = new List<string>();
        foreach (var set in meshContext.PartsSelectionSetList)
        {
            names.Add(set.Name);
        }
        return names;
    }
}
