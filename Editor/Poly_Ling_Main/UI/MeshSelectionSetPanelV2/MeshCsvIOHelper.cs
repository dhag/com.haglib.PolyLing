// MeshCsvIOHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.UI
{
    public static class MeshCsvIOHelper
    {
        public static bool SaveSelectedMeshesToCsv(
            List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || entries.Count == 0) return false;
            string defaultName = entries.Count == 1
                ? entries[0].MeshContext?.Name ?? "mesh" : "meshes";
            string path = EditorUtility.SaveFilePanel(
                "Save Selected Meshes (CSV)", Application.dataPath, defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string csv = MeshClipboardHelper.SerializeEntriesToCsv(entries, model);
                File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshCsvIOHelper] CSV save failed: {ex.Message}");
                return false;
            }
        }

        public static List<CsvMeshEntry> LoadMeshesFromCsv(ModelContext model)
        {
            string path = EditorUtility.OpenFilePanel(
                "Load Meshes (CSV)", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var entries = CsvMeshSerializer.ReadFile(path);
                if (entries == null || entries.Count == 0) return null;
                MeshClipboardHelper.ResolveDuplicateNames(entries, model);
                MeshClipboardHelper.AddEntriesToModel(entries, model);
                return entries;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshCsvIOHelper] CSV load failed: {ex.Message}");
                return null;
            }
        }
    }
}
