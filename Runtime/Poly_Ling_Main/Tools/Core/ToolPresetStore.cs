// ToolPresetStore.cs
// ツール・図形生成パラメータのプリセット管理
// 保存先: Application.persistentDataPath/PolyLing/Presets/{key}.json
// Unityプロジェクト横断で共有される

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public static class ToolPresetStore
    {
        // ================================================================
        // データ型
        // ================================================================

        [Serializable]
        public class PresetEntry
        {
            public string Name;
            public string Json;
        }

        [Serializable]
        private class PresetFile
        {
            public List<PresetEntry> Entries = new List<PresetEntry>();
        }

        // ================================================================
        // パス
        // ================================================================

        private static string RootDir =>
            Path.Combine(Application.persistentDataPath, "PolyLing", "Presets");

        private static string FilePath(string key) =>
            Path.Combine(RootDir, SanitizeKey(key) + ".json");

        private static string SanitizeKey(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key;
        }

        // ================================================================
        // 読み書き
        // ================================================================

        public static List<PresetEntry> Load(string key)
        {
            string path = FilePath(key);
            if (!File.Exists(path)) return new List<PresetEntry>();
            try
            {
                string text = File.ReadAllText(path);
                var file = JsonUtility.FromJson<PresetFile>(text);
                return file?.Entries ?? new List<PresetEntry>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ToolPresetStore] Load failed ({key}): {e.Message}");
                return new List<PresetEntry>();
            }
        }

        public static void Save(string key, string name, string json)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            var entries = Load(key);

            int existing = entries.FindIndex(e => e.Name == name);
            if (existing >= 0)
                entries[existing].Json = json;
            else
                entries.Add(new PresetEntry { Name = name, Json = json });

            Write(key, entries);
        }

        public static void Delete(string key, string name)
        {
            var entries = Load(key);
            entries.RemoveAll(e => e.Name == name);
            Write(key, entries);
        }

        public static void Rename(string key, string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            var entries = Load(key);
            var entry = entries.Find(e => e.Name == oldName);
            if (entry == null) return;
            if (entries.Exists(e => e.Name == newName)) return; // 重複禁止
            entry.Name = newName;
            Write(key, entries);
        }

        private static void Write(string key, List<PresetEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                var file = new PresetFile { Entries = entries };
                File.WriteAllText(FilePath(key), JsonUtility.ToJson(file, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ToolPresetStore] Write failed ({key}): {e.Message}");
            }
        }
    }
}
