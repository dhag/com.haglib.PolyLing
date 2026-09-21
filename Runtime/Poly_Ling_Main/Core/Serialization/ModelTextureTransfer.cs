// ModelTextureTransfer.cs
// モデルのマテリアルが参照するテクスチャ画像を「ファイル名 → 画像バイト列」の一覧で集める／適用する。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【使う所】
//   ・CSV（プロジェクトファイル）の textures フォルダの保存・読み込み（CsvModelSerializer.Texture.cs）
//   ・リモートのヒエラルキー送信で ModelDTO.embeddedTextures に画像を載せる／受け側で戻す
//   どちらも同じ集め方・同じ対応づけ（MaterialData のパスのファイル名で照合）を使う。
//   以前は CsvModelSerializer.Texture.cs にフォルダ直書きで実装されていたものを、中身を変えずに切り出した。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.Serialization
{
    /// <summary>テクスチャ画像の収集と適用。フォルダにもネットワークにも依存しない。</summary>
    public static class ModelTextureTransfer
    {
        /// <summary>集めたテクスチャ 1 枚。</summary>
        public sealed class Entry
        {
            /// <summary>保存名（拡張子付き）。一覧の中で大小文字を無視して一意。</summary>
            public string FileName;

            /// <summary>画像ファイルのバイト列（元ファイルの中身、または PNG）。</summary>
            public byte[] Data;
        }

        // ================================================================
        // 収集
        // ================================================================

        /// <summary>
        /// マテリアルが参照するテクスチャを集める。
        ///   1. MaterialData のパスが指す実ファイル（Assets/... と絶対パス）を読む
        ///   2. パスから読めなかったものは Material の Texture2D を PNG にする
        /// 順序と保存名の決め方は旧 CsvModelSerializer.SaveTextures と同じ。
        /// </summary>
        public static List<Entry> Collect(ModelContext model)
        {
            var entries = new List<Entry>();
            if (model == null) return entries;

            // 読み込み済みの元ファイル（正規化フルパス → 保存名）
            var copiedFiles = new Dictionary<string, string>();
            // PNG にしたテクスチャ（instanceID）
            var encodedTextures = new HashSet<int>();
            // 使用済みの保存名
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void ProcessMaterialList(List<MaterialReference> matRefs)
            {
                if (matRefs == null) return;
                foreach (var matRef in matRefs)
                {
                    if (matRef?.Data == null) continue;
                    var d = matRef.Data;

                    // 1. ファイルパスベース
                    // Unity AssetDBパス（Assets/...）
                    AddFile(entries, d.BaseMapPath, copiedFiles, usedNames, true);
                    AddFile(entries, d.MetallicMapPath, copiedFiles, usedNames, true);
                    AddFile(entries, d.NormalMapPath, copiedFiles, usedNames, true);
                    AddFile(entries, d.OcclusionMapPath, copiedFiles, usedNames, true);
                    AddFile(entries, d.EmissionMapPath, copiedFiles, usedNames, true);

                    // 外部ファイルパス（絶対パス）
                    AddFile(entries, d.SourceTexturePath, copiedFiles, usedNames, false);
                    AddFile(entries, d.SourceAlphaMapPath, copiedFiles, usedNames, false);
                    AddFile(entries, d.SourceBumpMapPath, copiedFiles, usedNames, false);

                    // 2. フォールバック: パスから読めなかったテクスチャを Material から直接抽出
                    AddFromMaterial(entries, matRef, "_BaseMap", d.BaseMapPath, d.SourceTexturePath, copiedFiles, encodedTextures, usedNames);
                    AddFromMaterial(entries, matRef, "_MainTex", d.BaseMapPath, d.SourceTexturePath, copiedFiles, encodedTextures, usedNames);
                    AddFromMaterial(entries, matRef, "_MetallicGlossMap", d.MetallicMapPath, null, copiedFiles, encodedTextures, usedNames);
                    AddFromMaterial(entries, matRef, "_BumpMap", d.NormalMapPath, d.SourceBumpMapPath, copiedFiles, encodedTextures, usedNames);
                    AddFromMaterial(entries, matRef, "_OcclusionMap", d.OcclusionMapPath, null, copiedFiles, encodedTextures, usedNames);
                    AddFromMaterial(entries, matRef, "_EmissionMap", d.EmissionMapPath, null, copiedFiles, encodedTextures, usedNames);
                }
            }

            ProcessMaterialList(model.MaterialReferences);
            ProcessMaterialList(model.DefaultMaterialReferences);
            return entries;
        }

        /// <summary>テクスチャファイルを 1 つ読む。</summary>
        private static void AddFile(
            List<Entry> entries, string texturePath,
            Dictionary<string, string> copiedFiles, HashSet<string> usedNames, bool isAssetPath)
        {
            if (string.IsNullOrEmpty(texturePath)) return;

            string normalizedSource = ResolveFullPath(texturePath, isAssetPath);
            if (normalizedSource == null) return;

            if (copiedFiles.ContainsKey(normalizedSource)) return;
            if (!File.Exists(normalizedSource)) return;

            try
            {
                byte[] data = File.ReadAllBytes(normalizedSource);
                string name = UniqueName(usedNames, Path.GetFileName(normalizedSource));
                entries.Add(new Entry { FileName = name, Data = data });
                copiedFiles[normalizedSource] = name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModelTextureTransfer] Failed to read texture: {normalizedSource} → {e.Message}");
            }
        }

        /// <summary>パスから読めなかった場合、Material の Texture2D を PNG にする。</summary>
        private static void AddFromMaterial(
            List<Entry> entries, MaterialReference matRef, string propertyName,
            string assetPath, string sourcePath,
            Dictionary<string, string> copiedFiles, HashSet<int> encodedTextures, HashSet<string> usedNames)
        {
            // 既にパスベースで読み込み済みならスキップ
            if (IsAlreadyCopied(assetPath, copiedFiles, true) || IsAlreadyCopied(sourcePath, copiedFiles, false))
                return;

            var mat = matRef.Material;
            if (mat == null || !mat.HasProperty(propertyName)) return;

            var tex = mat.GetTexture(propertyName) as Texture2D;
            if (tex == null) return;

            int id = tex.GetInstanceID();
            if (encodedTextures.Contains(id)) return;

            string baseName = !string.IsNullOrEmpty(tex.name) ? tex.name : $"texture_{id}";

            try
            {
                var readable = MakeReadable(tex);
                byte[] pngData = readable.EncodeToPNG();
                if (readable != tex)
                    UnityEngine.Object.DestroyImmediate(readable);

                if (pngData != null && pngData.Length > 0)
                {
                    string name = UniqueName(usedNames, baseName + ".png");
                    entries.Add(new Entry { FileName = name, Data = pngData });
                    encodedTextures.Add(id);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModelTextureTransfer] Failed to encode texture '{tex.name}': {e.Message}");
            }
        }

        private static bool IsAlreadyCopied(string path, Dictionary<string, string> copiedFiles, bool isAssetPath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string resolved = ResolveFullPath(path, isAssetPath);
            return resolved != null && copiedFiles.ContainsKey(resolved);
        }

        /// <summary>Assets/... はプロジェクトルート基準、それ以外はそのまま。正規化できなければ null。</summary>
        private static string ResolveFullPath(string path, bool isAssetPath)
        {
            string resolved = isAssetPath
                ? Path.Combine(Path.GetDirectoryName(Application.dataPath), path)
                : path;
            try { return Path.GetFullPath(resolved); }
            catch { return null; }
        }

        /// <summary>一覧の中で重複しない保存名にする（重複時は _1, _2 … を付ける）。</summary>
        private static string UniqueName(HashSet<string> usedNames, string fileName)
        {
            string name = fileName;
            if (usedNames.Add(name)) return name;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            do
            {
                name = $"{nameWithoutExt}_{counter}{ext}";
                counter++;
            } while (!usedNames.Add(name));
            return name;
        }

        /// <summary>テクスチャを読み取り可能な Texture2D にする。</summary>
        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source.isReadable) return source;

            // RenderTexture を経由して読み取り可能にする
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

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>
        /// 保存名で照合してテクスチャを Material に設定する（旧 CsvModelSerializer.LoadTextures と同じ対応づけ）。
        /// </summary>
        /// <param name="fileNames">使える保存名の一覧（大小文字を無視して照合する）。</param>
        /// <param name="readBytes">保存名から画像バイト列を得る。得られなければ null。</param>
        public static void Apply(ModelContext model, IEnumerable<string> fileNames, Func<string, byte[]> readBytes)
        {
            if (model == null || fileNames == null || readBytes == null) return;

            var names = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return;

            void ProcessMaterialList(List<MaterialReference> matRefs)
            {
                if (matRefs == null) return;
                foreach (var matRef in matRefs)
                {
                    if (matRef?.Data == null) continue;
                    ApplyToMaterial(matRef, names, readBytes);
                }
            }

            ProcessMaterialList(model.MaterialReferences);
            ProcessMaterialList(model.DefaultMaterialReferences);
        }

        /// <summary>埋め込みテクスチャ（ModelDTO.embeddedTextures）を適用する。</summary>
        public static void Apply(ModelContext model, List<EmbeddedTextureDTO> embedded)
        {
            if (model == null || embedded == null || embedded.Count == 0) return;

            var byName = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in embedded)
            {
                if (e == null || string.IsNullOrEmpty(e.fileName) || e.data == null) continue;
                if (!byName.ContainsKey(e.fileName)) byName[e.fileName] = e.data;
            }

            Apply(model, byName.Keys, n => byName.TryGetValue(n, out var d) ? d : null);
        }

        /// <summary>集めたテクスチャを ModelDTO.embeddedTextures の形にする。無ければ null。</summary>
        public static List<EmbeddedTextureDTO> ToEmbedded(List<Entry> entries)
        {
            if (entries == null || entries.Count == 0) return null;

            var list = new List<EmbeddedTextureDTO>(entries.Count);
            foreach (var e in entries)
                list.Add(new EmbeddedTextureDTO { fileName = e.FileName, data = e.Data });
            return list;
        }

        private static void ApplyToMaterial(
            MaterialReference matRef, HashSet<string> names, Func<string, byte[]> readBytes)
        {
            var d = matRef.Data;
            var mat = matRef.Material;
            if (mat == null) return;

            // BaseMap: SourceTexturePath → BaseMapPath の順でファイル名照合
            ApplyTexture(mat, "_BaseMap", "_MainTex", d.SourceTexturePath, d.BaseMapPath, names, readBytes);

            // BumpMap
            ApplyTexture(mat, "_BumpMap", null, d.SourceBumpMapPath, d.NormalMapPath, names, readBytes);

            // MetallicGlossMap
            ApplyTexture(mat, "_MetallicGlossMap", null, null, d.MetallicMapPath, names, readBytes);

            // OcclusionMap
            ApplyTexture(mat, "_OcclusionMap", null, null, d.OcclusionMapPath, names, readBytes);

            // EmissionMap
            ApplyTexture(mat, "_EmissionMap", null, null, d.EmissionMapPath, names, readBytes);
        }

        private static void ApplyTexture(
            Material mat, string primaryProp, string fallbackProp,
            string sourcePath, string assetPath,
            HashSet<string> names, Func<string, byte[]> readBytes)
        {
            // 既にテクスチャが設定されていればスキップ
            if (mat.HasProperty(primaryProp) && mat.GetTexture(primaryProp) != null)
                return;
            if (!string.IsNullOrEmpty(fallbackProp) && mat.HasProperty(fallbackProp) && mat.GetTexture(fallbackProp) != null)
                return;

            // ファイル名で照合
            string name = FindName(sourcePath, names) ?? FindName(assetPath, names);
            if (name == null) return;

            try
            {
                byte[] data = readBytes(name);
                if (data == null) return;

                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    tex.name = Path.GetFileNameWithoutExtension(name);
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
                Debug.LogWarning($"[ModelTextureTransfer] Failed to load texture: {name} → {e.Message}");
            }
        }

        /// <summary>パスのファイル名部分で一覧を検索し、一覧側の表記で返す。</summary>
        private static string FindName(string path, HashSet<string> names)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName)) return null;
            return names.TryGetValue(fileName, out var found) ? found : null;
        }
    }
}
