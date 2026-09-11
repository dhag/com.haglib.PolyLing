// CsvModelSerializer.Texture.cs
// CSV モデル入出力：テクスチャの保存・読み込みとユーティリティ。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置

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
        /// マテリアルが参照するテクスチャファイルをtexturesフォルダにコピー
        /// </summary>
        private static void SaveTextures(string texturesFolder, ModelContext model)
        {
            // コピー済みファイルを追跡（ソースパス → 保存先ファイル名）
            var copiedFiles = new Dictionary<string, string>();
            // EncodeToPNGで書き出し済みテクスチャを追跡（instanceID → 保存先ファイル名）
            var encodedTextures = new HashSet<int>();

            void ProcessMaterialList(List<MaterialReference> matRefs)
            {
                if (matRefs == null) return;
                foreach (var matRef in matRefs)
                {
                    if (matRef?.Data == null) continue;
                    var d = matRef.Data;

                    // 1. ファイルパスベースのコピー
                    // Unity AssetDBパス（Assets/...）
                    CopyTextureFile(texturesFolder, d.BaseMapPath, copiedFiles, true);
                    CopyTextureFile(texturesFolder, d.MetallicMapPath, copiedFiles, true);
                    CopyTextureFile(texturesFolder, d.NormalMapPath, copiedFiles, true);
                    CopyTextureFile(texturesFolder, d.OcclusionMapPath, copiedFiles, true);
                    CopyTextureFile(texturesFolder, d.EmissionMapPath, copiedFiles, true);

                    // 外部ファイルパス（絶対パス）
                    CopyTextureFile(texturesFolder, d.SourceTexturePath, copiedFiles, false);
                    CopyTextureFile(texturesFolder, d.SourceAlphaMapPath, copiedFiles, false);
                    CopyTextureFile(texturesFolder, d.SourceBumpMapPath, copiedFiles, false);

                    // 2. フォールバック: パスからコピーできなかったテクスチャをMaterialから直接抽出
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_BaseMap", d.BaseMapPath, d.SourceTexturePath, copiedFiles, encodedTextures);
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_MainTex", d.BaseMapPath, d.SourceTexturePath, copiedFiles, encodedTextures);
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_MetallicGlossMap", d.MetallicMapPath, null, copiedFiles, encodedTextures);
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_BumpMap", d.NormalMapPath, d.SourceBumpMapPath, copiedFiles, encodedTextures);
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_OcclusionMap", d.OcclusionMapPath, null, copiedFiles, encodedTextures);
                    ExtractTextureFromMaterial(texturesFolder, matRef, "_EmissionMap", d.EmissionMapPath, null, copiedFiles, encodedTextures);
                }
            }

            ProcessMaterialList(model.MaterialReferences);
            ProcessMaterialList(model.DefaultMaterialReferences);
        }

        /// <summary>
        /// パスからコピーできなかった場合、MaterialのTexture2DをEncodeToPNGで書き出す
        /// </summary>
        private static void ExtractTextureFromMaterial(
            string texturesFolder, MaterialReference matRef, string propertyName,
            string assetPath, string sourcePath,
            Dictionary<string, string> copiedFiles, HashSet<int> encodedTextures)
        {
            // 既にパスベースでコピー済みならスキップ
            if (IsAlreadyCopied(assetPath, copiedFiles, true) || IsAlreadyCopied(sourcePath, copiedFiles, false))
                return;

            // MaterialからTexture2Dを取得
            var mat = matRef.Material;
            if (mat == null || !mat.HasProperty(propertyName)) return;

            var tex = mat.GetTexture(propertyName) as Texture2D;
            if (tex == null) return;

            int id = tex.GetInstanceID();
            if (encodedTextures.Contains(id)) return;

            // ファイル名決定
            string baseName = !string.IsNullOrEmpty(tex.name) ? tex.name : $"texture_{id}";
            string destPath = GetUniqueDestPath(texturesFolder, baseName + ".png");

            try
            {
                // 読み取り可能なTexture2Dにコピー
                var readable = MakeReadable(tex);
                byte[] pngData = readable.EncodeToPNG();
                if (readable != tex)
                    UnityEngine.Object.DestroyImmediate(readable);

                if (pngData != null && pngData.Length > 0)
                {
                    File.WriteAllBytes(destPath, pngData);
                    encodedTextures.Add(id);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CsvModelSerializer] Failed to encode texture '{tex.name}': {e.Message}");
            }
        }

        /// <summary>
        /// 指定パスが既にコピー済みか判定
        /// </summary>
        private static bool IsAlreadyCopied(string path, Dictionary<string, string> copiedFiles, bool isAssetPath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string resolved;
            if (isAssetPath)
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                resolved = Path.Combine(projectRoot, path);
            }
            else
            {
                resolved = path;
            }
            try { resolved = Path.GetFullPath(resolved); } catch { return false; }
            return copiedFiles.ContainsKey(resolved);
        }

        /// <summary>
        /// テクスチャを読み取り可能なTexture2Dに変換
        /// </summary>
        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source.isReadable) return source;

            // RenderTextureを経由して読み取り可能にする
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        /// <summary>
        /// テクスチャファイルを1つコピー
        /// </summary>
        private static void CopyTextureFile(
            string texturesFolder, string texturePath,
            Dictionary<string, string> copiedFiles, bool isAssetPath)
        {
            if (string.IsNullOrEmpty(texturePath)) return;

            // 実ファイルパスを解決
            string sourcePath;
            if (isAssetPath)
            {
                // Assets/... → プロジェクトルート基準の絶対パス
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                sourcePath = Path.Combine(projectRoot, texturePath);
            }
            else
            {
                sourcePath = texturePath;
            }

            // 正規化して重複チェック
            string normalizedSource;
            try { normalizedSource = Path.GetFullPath(sourcePath); }
            catch { return; }

            if (copiedFiles.ContainsKey(normalizedSource)) return;

            if (!File.Exists(normalizedSource)) return;

            // 保存先ファイル名を決定（重複時は連番付加）
            string fileName = Path.GetFileName(normalizedSource);
            string destPath = GetUniqueDestPath(texturesFolder, fileName);

            try
            {
                File.Copy(normalizedSource, destPath, false);
                copiedFiles[normalizedSource] = Path.GetFileName(destPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CsvModelSerializer] Failed to copy texture: {normalizedSource} → {e.Message}");
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
        /// texturesフォルダからテクスチャを読み込み、MaterialReferenceに適用
        /// </summary>
        private static void LoadTextures(string modelFolderPath, ModelContext model)
        {
            string texturesFolder = Path.Combine(modelFolderPath, "textures");
            if (!Directory.Exists(texturesFolder)) return;

            // texturesフォルダ内のファイルをファイル名(小文字)→フルパスで索引化
            var textureFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(texturesFolder))
            {
                string fileName = Path.GetFileName(file);
                if (!textureFiles.ContainsKey(fileName))
                    textureFiles[fileName] = file;
            }

            if (textureFiles.Count == 0) return;

            void ProcessMaterialList(List<MaterialReference> matRefs)
            {
                if (matRefs == null) return;
                foreach (var matRef in matRefs)
                {
                    if (matRef?.Data == null) continue;
                    LoadTextureForMaterial(matRef, textureFiles);
                }
            }

            ProcessMaterialList(model.MaterialReferences);
            ProcessMaterialList(model.DefaultMaterialReferences);
        }

        /// <summary>
        /// 1つのMaterialReferenceに対してtexturesフォルダからテクスチャを適用
        /// </summary>
        private static void LoadTextureForMaterial(
            MaterialReference matRef, Dictionary<string, string> textureFiles)
        {
            var d = matRef.Data;
            var mat = matRef.Material;
            if (mat == null) return;

            // BaseMap: SourceTexturePath → BaseMapPath の順でファイル名照合
            ApplyTextureFromFolder(mat, "_BaseMap", "_MainTex",
                d.SourceTexturePath, d.BaseMapPath, textureFiles);

            // BumpMap
            ApplyTextureFromFolder(mat, "_BumpMap", null,
                d.SourceBumpMapPath, d.NormalMapPath, textureFiles);

            // MetallicGlossMap
            ApplyTextureFromFolder(mat, "_MetallicGlossMap", null,
                null, d.MetallicMapPath, textureFiles);

            // OcclusionMap
            ApplyTextureFromFolder(mat, "_OcclusionMap", null,
                null, d.OcclusionMapPath, textureFiles);

            // EmissionMap
            ApplyTextureFromFolder(mat, "_EmissionMap", null,
                null, d.EmissionMapPath, textureFiles);
        }

        /// <summary>
        /// texturesフォルダからテクスチャを検索してMaterialに適用
        /// </summary>
        private static void ApplyTextureFromFolder(
            Material mat, string primaryProp, string fallbackProp,
            string sourcePath, string assetPath,
            Dictionary<string, string> textureFiles)
        {
            // 既にテクスチャが設定されていればスキップ
            if (mat.HasProperty(primaryProp) && mat.GetTexture(primaryProp) != null)
                return;
            if (!string.IsNullOrEmpty(fallbackProp) && mat.HasProperty(fallbackProp) && mat.GetTexture(fallbackProp) != null)
                return;

            // ファイル名でtexturesフォルダを検索
            string filePath = FindTextureInFolder(sourcePath, textureFiles)
                           ?? FindTextureInFolder(assetPath, textureFiles);

            if (filePath == null) return;

            // File.ReadAllBytesで読み込み
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    tex.name = Path.GetFileNameWithoutExtension(filePath);
                    if (mat.HasProperty(primaryProp))
                        mat.SetTexture(primaryProp, tex);
                    if (!string.IsNullOrEmpty(fallbackProp) && mat.HasProperty(fallbackProp))
                        mat.SetTexture(fallbackProp, tex);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CsvModelSerializer] Failed to load texture: {filePath} → {e.Message}");
            }
        }

        /// <summary>
        /// パスのファイル名部分でtexturesフォルダ内を検索
        /// </summary>
        private static string FindTextureInFolder(string path, Dictionary<string, string> textureFiles)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName)) return null;
            return textureFiles.TryGetValue(fileName, out var found) ? found : null;
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
