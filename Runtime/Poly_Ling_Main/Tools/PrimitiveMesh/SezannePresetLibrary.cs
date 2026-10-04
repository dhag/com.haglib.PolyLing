// SezannePresetLibrary.cs
// セザンヌ（旧称 能面）のプリセット置き場。
// 置き場: <persistentDataPath>/PolyLing/sezane （子フォルダも探す）
//
// 【プリセットになるもの】
//   ・.mqo ファイル
//   ・PolyLing のプロジェクトファイル（先頭行が "#PolyLing,version" の .csv）
//   ファイル内の描画オブジェクトのうち、表示中・面を持つものを 1 件ずつプリセットにする。
//   不可視のものは載せない。マテリアルは無視し、全面を 0 番にそろえる。
//
// 【番号】
//   Index はファイル内の全オブジェクトを先頭から数えた通し番号
//   （プロジェクトはモデル順 → 各モデルのオブジェクト順）。
//   同じファイルを読み直せば同じ番号になる。
//
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.MQO;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.NohMask
{
    /// <summary>セザンヌのプリセット（ファイル内の 1 オブジェクト）。</summary>
    public sealed class SezannePresetEntry
    {
        /// <summary>ファイルのフルパス。</summary>
        public string Path;
        /// <summary>ファイル内の通し番号。</summary>
        public int Index;
        /// <summary>一覧に出す名前（置き場からの相対パス : オブジェクト名）。</summary>
        public string Label;
    }

    /// <summary>セザンヌのプリセットの一覧・読込・保存。</summary>
    public static class SezannePresetLibrary
    {
        /// <summary>プリセット置き場。</summary>
        public static string Folder =>
            System.IO.Path.Combine(Application.persistentDataPath, "PolyLing", "sezane");

        private const string ProjectHeader = "#PolyLing,";

        // 読み込み結果の控え（パス → 更新時刻と中身）。プレビューのたびに読み直さないため。
        private static readonly Dictionary<string, (DateTime stamp, List<(int index, MeshObject mesh)> items)>
            _cache = new Dictionary<string, (DateTime, List<(int, MeshObject)>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>控えを捨てる（一覧の更新時に呼ぶ）。</summary>
        public static void ClearCache() => _cache.Clear();

        // ================================================================
        // 一覧
        // ================================================================

        /// <summary>置き場を子フォルダまで探し、プリセットを並べる。置き場が無ければ作る。</summary>
        public static List<SezannePresetEntry> Scan()
        {
            var list = new List<SezannePresetEntry>();
            string root = Folder;
            try { Directory.CreateDirectory(root); }
            catch (Exception ex)
            {
                Debug.LogError($"[Sezanne] 置き場を作れません: {root} : {ex.Message}");
                return list;
            }

            var files = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    if (IsPresetFile(f)) files.Add(f);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sezanne] 置き場を読めません: {root} : {ex.Message}");
                return list;
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            // ドロップダウンは表示名で選ぶので、同名が出たら通し番号を添えて区別する。
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                string rel = MakeRelative(root, f);
                foreach (var (index, mesh) in LoadFile(f))
                {
                    string label = $"{rel} : {mesh.Name}";
                    if (!used.Add(label))
                    {
                        label = $"{label} (#{index})";
                        used.Add(label);
                    }
                    list.Add(new SezannePresetEntry
                    {
                        Path  = f,
                        Index = index,
                        Label = label,
                    });
                }
            }
            return list;
        }

        // ================================================================
        // 読込
        // ================================================================

        /// <summary>
        /// プリセットの形を複製して返す。見つからなければ null。
        /// 親子関係・ボーンウェイトは外し、全面をマテリアル 0 番にする。
        /// </summary>
        public static MeshObject LoadMesh(string path, int index, string meshName)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var (i, mesh) in LoadFile(path))
            {
                if (i != index) continue;
                var mo = mesh.Clone();
                if (!string.IsNullOrEmpty(meshName)) mo.Name = meshName;
                return mo;
            }
            Debug.LogError($"[Sezanne] プリセットが見つかりません: {path} #{index}");
            return null;
        }

        /// <summary>ファイルからプリセット対象のオブジェクトを取り出す（控えがあれば控えから）。</summary>
        private static List<(int index, MeshObject mesh)> LoadFile(string path)
        {
            var empty = new List<(int, MeshObject)>();
            if (!File.Exists(path)) return empty;

            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(path); }
            catch { return empty; }

            if (_cache.TryGetValue(path, out var c) && c.stamp == stamp) return c.items;

            var contexts = new List<MeshContext>();
            try
            {
                if (IsMqo(path))
                {
                    var settings = new MQOImportSettings
                    {
                        ImportMaterials         = false,
                        ImportBonesFromArmature = false,
                    };
                    var r = MQOImporter.ImportFile(path, settings);
                    if (r != null && r.Success) contexts.AddRange(r.MeshContexts);
                }
                else
                {
                    var project = CsvProjectSerializer.ImportFromFile(path, out _, out _);
                    if (project != null)
                        foreach (var model in project.Models)
                            if (model?.MeshContextList != null) contexts.AddRange(model.MeshContextList);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sezanne] 読込に失敗: {path} : {ex.Message}");
            }

            var items = new List<(int, MeshObject)>();
            for (int i = 0; i < contexts.Count; i++)
            {
                var mc = contexts[i];
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (!mc.IsVisible) continue;
                if (mo.Type != MeshType.Mesh) continue;
                if (mo.FaceCount == 0) continue;
                items.Add((i, Normalize(mo.Clone(), mo.Name)));
            }

            _cache[path] = (stamp, items);
            return items;
        }

        // ================================================================
        // 保存
        // ================================================================

        /// <summary>
        /// 描画オブジェクトの複製を新しいモデルへ入れ、置き場へプロジェクトとして保存する。
        /// 保存先は Folder/&lt;名前&gt;/&lt;名前&gt;.csv。同名があれば _2, _3 … を付ける。
        /// 成功したらプロジェクトファイルのパス、失敗したら null。
        /// </summary>
        public static string Save(MeshObject src, string name)
        {
            if (src == null || src.VertexCount == 0)
            {
                Debug.LogWarning("[Sezanne] 保存する描画オブジェクトがありません。");
                return null;
            }

            string baseName = Sanitize(string.IsNullOrWhiteSpace(name) ? (src.Name ?? "Preset") : name.Trim());
            string root = Folder;
            string stem = baseName;
            for (int n = 2; Directory.Exists(System.IO.Path.Combine(root, stem)); n++)
                stem = $"{baseName}_{n}";

            string projFolder = System.IO.Path.Combine(root, stem);
            string projFile   = System.IO.Path.Combine(projFolder, stem + ".csv");

            var mo = Normalize(src.Clone(), stem);
            var mc = new MeshContext { MeshObject = mo };
            mc.IsVisible = true;

            var model = new ModelContext(stem);
            model.Add(mc);

            var project = new ProjectContext(stem);
            project.Models.Clear();
            project.Models.Add(model);

            if (!CsvProjectSerializer.ExportToFile(projFile, project)) return null;

            Debug.Log($"[Sezanne] プリセットを保存: {projFile}");
            return projFile;
        }

        // ================================================================
        // 下請け
        // ================================================================

        /// <summary>プリセット用に整える：名前・親なし・通常メッシュ・マテリアル 0 番・ボーンウェイトなし。</summary>
        private static MeshObject Normalize(MeshObject mo, string name)
        {
            if (!string.IsNullOrEmpty(name)) mo.Name = name;
            mo.Type = MeshType.Mesh;
            mo.HierarchyParentIndex = -1;
            foreach (var f in mo.Faces)
                if (f != null) f.MaterialIndex = 0;
            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                v.BoneWeight       = null;
                v.MirrorBoneWeight = null;
                mo.Vertices[i] = v;
            }
            return mo;
        }

        private static bool IsMqo(string path) =>
            string.Equals(System.IO.Path.GetExtension(path), ".mqo", StringComparison.OrdinalIgnoreCase);

        private static bool IsPresetFile(string path)
        {
            if (IsMqo(path)) return true;
            if (!string.Equals(System.IO.Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                using (var sr = new StreamReader(path))
                {
                    string first = sr.ReadLine();
                    return first != null && first.TrimStart('\uFEFF').StartsWith(ProjectHeader, StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        private static string MakeRelative(string root, string path)
        {
            string r = root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            string rel = path.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? path.Substring(r.Length) : path;
            // ドロップダウンは '/' を階層の区切りに使うことがあるので '\' にそろえる。
            return rel.Replace('/', '\\');
        }

        private static string Sanitize(string name)
        {
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');
            return string.IsNullOrEmpty(name) ? "Preset" : name;
        }
    }
}
