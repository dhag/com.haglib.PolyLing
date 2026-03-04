// MeshClipboardHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.UI
{
    public static class MeshClipboardHelper
    {
        public static string SerializeEntriesToCsv(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || entries.Count == 0) return "";
            CsvModelSerializer.EnrichEntriesWithMirrorPeers(entries, model);
            string tempPath = Path.Combine(Path.GetTempPath(), $"polyling_copy_{Guid.NewGuid():N}.csv");
            try
            {
                string fileType = DetermineFileType(entries);
                CsvMeshSerializer.WriteFile(tempPath, entries, fileType);
                return File.ReadAllText(tempPath, System.Text.Encoding.UTF8);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public static List<CsvMeshEntry> DeserializeFromCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return new List<CsvMeshEntry>();
            string tempPath = Path.Combine(Path.GetTempPath(), $"polyling_paste_{Guid.NewGuid():N}.csv");
            try
            {
                File.WriteAllText(tempPath, csv, System.Text.Encoding.UTF8);
                return CsvMeshSerializer.ReadFile(tempPath);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public static void ResolveDuplicateNames(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return;
            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    existingNames.Add(mc.Name);
            }
            foreach (var entry in entries)
            {
                var mc = entry.MeshContext;
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                string baseName = mc.Name;
                string candidate = baseName;
                int suffix = 2;
                while (existingNames.Contains(candidate))
                {
                    candidate = $"{baseName}_{suffix}";
                    suffix++;
                }
                if (candidate != baseName)
                {
                    mc.Name = candidate;
                    if (mc.MeshObject != null) mc.MeshObject.Name = candidate;
                }
                existingNames.Add(mc.Name);
            }
        }

        public static int AddEntriesToModel(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return 0;
            int count = 0;
            foreach (var entry in entries)
            {
                if (entry.MeshContext == null) continue;
                model.Add(entry.MeshContext);
                count++;
            }
            CsvModelSerializer.BuildMirrorPairsFromEntries(entries, model);
            return count;
        }

        public static List<CsvMeshEntry> BuildEntriesFromIndices(
            IEnumerable<int> selectedIndices, ModelContext model)
        {
            var result = new List<CsvMeshEntry>();
            if (selectedIndices == null || model == null) return result;
            foreach (int idx in selectedIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc != null)
                    result.Add(new CsvMeshEntry { GlobalIndex = idx, MeshContext = mc });
            }
            return result;
        }

        private static string DetermineFileType(List<CsvMeshEntry> entries)
        {
            int bone = entries.Count(e => e.MeshContext?.Type == MeshType.Bone);
            int morph = entries.Count(e => e.MeshContext?.Type == MeshType.Morph);
            int mesh = entries.Count - bone - morph;
            if (bone > mesh && bone > morph) return "bone";
            if (morph > mesh && morph > bone) return "morph";
            return "mesh";
        }
    }
}
