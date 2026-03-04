// MeshDictionaryIOHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.UI
{
    public static class MeshDictionaryIOHelper
    {
        public static bool SaveDictionaryFile(List<MeshSelectionSet> sets, ModelContext model)
        {
            if (sets == null || sets.Count == 0) return false;
            string path = EditorUtility.SaveFilePanel(
                "Save Mesh Selection Dictionary", Application.dataPath,
                $"{model.Name}_MeshDic", "csv");
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                var lines = new List<string>();
                lines.Add("# MeshSelectionDictionary");
                lines.Add($"# model,{model.Name}");
                foreach (var set in sets)
                {
                    lines.Add("");
                    lines.Add($"# set,{set.Name},{set.Category}");
                    foreach (string meshName in set.MeshNames)
                        lines.Add(meshName);
                }
                File.WriteAllLines(path, lines, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshDictionaryIOHelper] Save failed: {ex.Message}");
                return false;
            }
        }

        public static List<MeshSelectionSet> LoadDictionaryFile(ModelContext model)
        {
            string path = EditorUtility.OpenFilePanel(
                "Open Mesh Selection Dictionary", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var fileLines = File.ReadAllLines(path, Encoding.UTF8);
                var loadedSets = new List<MeshSelectionSet>();
                MeshSelectionSet currentSet = null;
                foreach (var line in fileLines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.StartsWith("# set,"))
                    {
                        var parts = trimmed.Substring(6).Split(',');
                        string setName = parts.Length > 0 ? parts[0].Trim() : "MeshSet";
                        var category = ModelContext.SelectionCategory.Mesh;
                        if (parts.Length > 1 &&
                            Enum.TryParse<ModelContext.SelectionCategory>(
                                parts[1].Trim(), out var cat))
                            category = cat;
                        currentSet = new MeshSelectionSet(setName) { Category = category };
                        loadedSets.Add(currentSet);
                    }
                    else if (trimmed.StartsWith("#"))
                    {
                        continue;
                    }
                    else if (currentSet != null)
                    {
                        if (!currentSet.MeshNames.Contains(trimmed))
                            currentSet.MeshNames.Add(trimmed);
                    }
                }
                return loadedSets.Count > 0 ? loadedSets : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshDictionaryIOHelper] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}
