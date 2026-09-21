// CsvModelSerializer.Texture.cs
// CSV モデル入出力：テクスチャの保存・読み込みとユーティリティ。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置
//
// テクスチャの集め方と対応づけは ModelTextureTransfer にある（リモートのヒエラルキー送信と共用）。
// ここは textures フォルダへの書き込みと、フォルダからの読み込みだけを行う。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Serialization.FolderSerializer
{
    public static partial class CsvModelSerializer
    {
        // ================================================================
        // テクスチャ保存
        // ================================================================

        /// <summary>
        /// マテリアルが参照するテクスチャを textures フォルダに書き出す。
        /// </summary>
        private static void SaveTextures(string texturesFolder, ModelContext model)
        {
            foreach (var entry in ModelTextureTransfer.Collect(model))
            {
                // 保存名は一覧の中で一意。フォルダに同名の既存ファイルがあれば連番を付ける。
                string destPath = GetUniqueDestPath(texturesFolder, entry.FileName);
                try
                {
                    File.WriteAllBytes(destPath, entry.Data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CsvModelSerializer] Failed to write texture: {destPath} → {e.Message}");
                }
            }
        }

        /// <summary>
        /// 重複しないファイルパスを取得
        /// </summary>
        private static string GetUniqueDestPath(string folder, string fileName)
        {
            string destPath = Path.Combine(folder, fileName);
            if (!File.Exists(destPath)) return destPath;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            do
            {
                destPath = Path.Combine(folder, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            } while (File.Exists(destPath));
            return destPath;
        }

        // ================================================================
        // テクスチャ読み込み
        // ================================================================

        /// <summary>
        /// textures フォルダからテクスチャを読み込み、MaterialReference に適用
        /// </summary>
        private static void LoadTextures(string modelFolderPath, ModelContext model)
        {
            string texturesFolder = Path.Combine(modelFolderPath, "textures");
            if (!Directory.Exists(texturesFolder)) return;

            // textures フォルダ内のファイルをファイル名→フルパスで索引化
            var textureFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(texturesFolder))
            {
                string fileName = Path.GetFileName(file);
                if (!textureFiles.ContainsKey(fileName))
                    textureFiles[fileName] = file;
            }

            if (textureFiles.Count == 0) return;

            // 照合できたファイルだけを読む。
            ModelTextureTransfer.Apply(model, textureFiles.Keys,
                name => textureFiles.TryGetValue(name, out var path) ? File.ReadAllBytes(path) : null);
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ================================================================
        // 名前→インデックス辞書構築
        // ================================================================

        /// <summary>
        /// ModelContextのMeshContextListから名前→インデックス辞書を構築
        /// </summary>
        private static Dictionary<string, int> BuildNameToIndex(ModelContext model)
        {
            var dict = new Dictionary<string, int>();
            if (model == null) return dict;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                {
                    // 重複名の場合は最初のもの優先
                    if (!dict.ContainsKey(mc.Name))
                        dict[mc.Name] = i;
                }
            }
            return dict;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\"\"", "\"");
            }
            return s;
        }

        private static string Fl(float v) => v.ToString("G9", CultureInfo.InvariantCulture);

        private static string Fc(float[] arr, int idx)
        {
            if (arr == null || idx >= arr.Length) return "0";
            return arr[idx].ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string SafeGet(string[] cols, int idx) => idx < cols.Length ? cols[idx] : "";

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static string[] Split(string line)
        {
            // 簡易CSV分割（CsvMeshSerializerと同じロジック）
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                            else { i++; break; }
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
        }

        private static int PInt(string[] cols, int idx, int def = 0)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return int.TryParse(cols[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static float PFl(string[] cols, int idx, float def = 0f)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return float.TryParse(cols[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool PBool(string[] cols, int idx, bool def = false)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return cols[idx].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   cols[idx].Trim().Equals("True");
        }
    }
}
