// Assets/Editor/Poly_Ling/Serialization/FolderSerializer/CsvModelSerializer.cs
// モデルフォルダ内のCSVファイル読み書き
// model.csv, materials.csv, humanoid.csv, morphgroups.csv, editorstate.csv, workplane.csv
// + mesh/bone/morph CSVの振り分け

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// model.csv内の1エントリ（MeshContextListの並び順管理）
    /// </summary>
    public class ModelEntry
    {
        public int GlobalIndex;
        public string Type;   // "Mesh","Bone","Morph" etc
        public string Name;
        public string File;   // "mesh","bone","morph"
        public int OrderInFile;
        public bool IsNameBased;
    }

    /// <summary>
    /// モデルフォルダのCSV読み書き
    /// </summary>
    public static class CsvModelSerializer
    {
        // ================================================================
        // Save: モデルフォルダ一式を書き出す
        // ================================================================

        /// <summary>
        /// ModelContextをフォルダに保存
        /// </summary>
        /// <param name="useNameBased">名前ベース参照モード</param>
        public static void SaveModel(
            string modelFolderPath,
            ModelContext model,
            EditorStateDTO editorState = null,
            WorkPlaneContext workPlane = null,
            bool useNameBased = false)
        {
            if (model == null) return;
            Directory.CreateDirectory(modelFolderPath);

            string modelName = SanitizeFileName(model.Name ?? "Model");

            // 名前ベース用: インデックス→名前辞書を構築
            Dictionary<int, string> indexToName = null;
            if (useNameBased)
            {
                indexToName = new Dictionary<int, string>();
                for (int idx = 0; idx < model.MeshContextCount; idx++)
                {
                    var m = model.GetMeshContext(idx);
                    if (m != null)
                        indexToName[idx] = m.Name ?? $"Unnamed_{idx}";
                }
            }

            // メッシュをタイプ別に分類
            var meshEntries = new List<CsvMeshEntry>();
            var boneEntries = new List<CsvMeshEntry>();
            var morphEntries = new List<CsvMeshEntry>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                var entry = new CsvMeshEntry { GlobalIndex = i, MeshContext = mc };

                switch (mc.Type)
                {
                    case MeshType.Bone:
                        boneEntries.Add(entry);
                        break;
                    case MeshType.Morph:
                        morphEntries.Add(entry);
                        break;
                    default:
                        meshEntries.Add(entry);
                        break;
                }
            }

            // MirrorPair情報をReal側entryに設定 + MirrorSide同梱
            EnrichEntriesWithMirrorPeers(meshEntries, model);

            // mesh/bone/morph CSV
            string meshFile = $"{modelName}.mesh.csv";
            string boneFile = $"{modelName}.bone.csv";
            string morphFile = $"{modelName}.morph.csv";

            if (meshEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, meshFile), meshEntries, "mesh", useNameBased, indexToName);
            if (boneEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, boneFile), boneEntries, "bone", useNameBased, indexToName);
            if (morphEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, morphFile), morphEntries, "morph", useNameBased, indexToName);

            // model.csv (順序マスター)
            WriteModelCsv(modelFolderPath, model, meshEntries, boneEntries, morphEntries, useNameBased);

            // materials.csv
            WriteMaterialsCsv(modelFolderPath, model);

            // humanoid.csv
            if (model.HumanoidMapping != null && !model.HumanoidMapping.IsEmpty)
                WriteHumanoidCsv(modelFolderPath, model, useNameBased, indexToName);

            // morphgroups.csv
            if (model.MorphExpressions != null && model.MorphExpressions.Count > 0)
                WriteMorphGroupsCsv(modelFolderPath, model, useNameBased, indexToName);

            // meshselsets.csv
            if (model.MeshSelectionSets != null && model.MeshSelectionSets.Count > 0)
                WriteMeshSelSetsCsv(modelFolderPath, model);

            // mirrorpairs.csv
            if (model.MirrorPairs != null && model.MirrorPairs.Count > 0)
                WriteMirrorPairsCsv(modelFolderPath, model, useNameBased);

            // editorstate.csv
            if (editorState != null)
                WriteEditorStateCsv(modelFolderPath, editorState, useNameBased, indexToName);

            // workplane.csv
            if (workPlane != null)
                WriteWorkPlaneCsv(modelFolderPath, workPlane);

            // tposebackup.csv
            if (model.TPoseBackup != null)
                WriteTPoseBackupCsv(modelFolderPath, model.TPoseBackup, useNameBased, indexToName);

            // textures フォルダにテクスチャをコピー
            string texturesFolder = Path.Combine(modelFolderPath, "textures");
            Directory.CreateDirectory(texturesFolder);
            SaveTextures(texturesFolder, model);
        }

        // ================================================================
        // Load: モデルフォルダからModelContextを復元
        // ================================================================

        /// <summary>
        /// フォルダからModelContextを復元
        /// </summary>
        public static ModelContext LoadModel(
            string modelFolderPath,
            out EditorStateDTO editorState,
            out WorkPlaneContext workPlane,
            out List<CsvMeshEntry> additionalEntries)
        {
            editorState = null;
            workPlane = null;
            additionalEntries = new List<CsvMeshEntry>();

            if (!Directory.Exists(modelFolderPath)) return null;

            // model.csv 読み込み
            string modelCsvPath = Path.Combine(modelFolderPath, "model.csv");
            var (modelName, entries) = ReadModelCsv(modelCsvPath);

            if (entries == null || entries.Count == 0) return null;

            var model = new ModelContext(modelName);
            string modelPrefix = SanitizeFileName(modelName).ToLowerInvariant() + ".";

            // メッシュファイルを読み込み（自モデル / 追加を分離）
            var ownMeshEntries = new Dictionary<string, List<CsvMeshEntry>>();

            foreach (var type in new[] { "mesh", "bone", "morph" })
            {
                var csvFiles = Directory.GetFiles(modelFolderPath, $"*.{type}.csv");
                var ownList = new List<CsvMeshEntry>();

                foreach (var csvFile in csvFiles)
                {
                    string fileName = Path.GetFileName(csvFile).ToLowerInvariant();
                    var loaded = CsvMeshSerializer.ReadFile(csvFile);

                    if (fileName.StartsWith(modelPrefix))
                    {
                        // 自モデルのファイル
                        ownList.AddRange(loaded);
                    }
                    else
                    {
                        // 他モデルのファイル → 追加エントリ
                        additionalEntries.AddRange(loaded);
                    }
                }

                ownMeshEntries[type] = ownList;
            }

            // model.csv のentry順にMeshContextListを構築
            // entryByName形式かentry形式かで検索方法を変える
            bool hasNameBasedEntries = entries.Exists(e => e.IsNameBased);

            // 全読み込みエントリをフラットリスト化
            var allOwnEntries = new List<CsvMeshEntry>();
            foreach (var kvp in ownMeshEntries)
                allOwnEntries.AddRange(kvp.Value);

            if (hasNameBasedEntries)
            {
                // 名前ベース: model.csv の entryByName 順に名前で検索
                var nameLookup = new Dictionary<string, CsvMeshEntry>();
                foreach (var me in allOwnEntries)
                {
                    if (me.MeshContext != null && !string.IsNullOrEmpty(me.MeshContext.Name))
                    {
                        // 重複名の場合は最初のもの優先
                        if (!nameLookup.ContainsKey(me.MeshContext.Name))
                            nameLookup[me.MeshContext.Name] = me;
                    }
                }

                foreach (var entry in entries)
                {
                    if (nameLookup.TryGetValue(entry.Name, out var found))
                    {
                        model.Add(found.MeshContext);
                    }
                    else
                    {
                        Debug.LogWarning($"[CsvModelSerializer] MeshContext not found for name={entry.Name}");
                    }
                }
            }
            else
            {
                // インデックスベース: 従来通り
                var lookup = new Dictionary<int, MeshContext>();
                foreach (var me in allOwnEntries)
                {
                    lookup[me.GlobalIndex] = me.MeshContext;
                }

                foreach (var entry in entries)
                {
                    if (lookup.TryGetValue(entry.GlobalIndex, out var mc))
                    {
                        model.Add(mc);
                    }
                    else
                    {
                        Debug.LogWarning($"[CsvModelSerializer] MeshContext not found for globalIndex={entry.GlobalIndex} ({entry.Name})");
                    }
                }
            }

            // 名前ベースエントリの参照解決（親/IK/BoneWeight等）
            if (hasNameBasedEntries)
            {
                var nameToIndex = BuildNameToIndex(model);
                CsvMeshSerializer.ResolveNameReferences(allOwnEntries, nameToIndex);
            }

            // materials.csv
            ReadMaterialsCsv(modelFolderPath, model);

            // textures フォルダからテクスチャを復元
            LoadTextures(modelFolderPath, model);

            // humanoid.csv
            ReadHumanoidCsv(modelFolderPath, model);

            // morphgroups.csv
            ReadMorphGroupsCsv(modelFolderPath, model);

            // meshselsets.csv
            ReadMeshSelSetsCsv(modelFolderPath, model);

            // mirrorpairs.csv
            ReadMirrorPairsCsv(modelFolderPath, model);

            // エントリのmirrorPeer情報からもMirrorPairを構築
            // （部分インポート用：mirrorpairs.csvが無い場合でもペアを復元）
            BuildMirrorPairsFromEntries(allOwnEntries, model);

            // editorstate.csv
            string esPath = Path.Combine(modelFolderPath, "editorstate.csv");
            if (File.Exists(esPath))
                editorState = ReadEditorStateCsv(esPath, model);

            // workplane.csv
            string wpPath = Path.Combine(modelFolderPath, "workplane.csv");
            if (File.Exists(wpPath))
                workPlane = ReadWorkPlaneCsv(wpPath);

            // tposebackup.csv
            string tpPath = Path.Combine(modelFolderPath, "tposebackup.csv");
            if (File.Exists(tpPath))
                model.TPoseBackup = ReadTPoseBackupCsv(tpPath, model);

            return model;
        }

        // ================================================================
        // model.csv
        // ================================================================

        private static void WriteModelCsv(
            string folderPath, ModelContext model,
            List<CsvMeshEntry> meshEntries,
            List<CsvMeshEntry> boneEntries,
            List<CsvMeshEntry> morphEntries,
            bool useNameBased = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Model,version,1.0");
            sb.AppendLine($"name,{Esc(model.Name)}");
            sb.AppendLine($"meshCount,{model.MeshContextCount}");
            if (useNameBased)
                sb.AppendLine("nameBasedMode,true");

            // 全エントリをglobalIndex順にマージして出力
            var allEntries = new List<(int globalIndex, string file, int orderInFile, string type, string name)>();

            for (int o = 0; o < meshEntries.Count; o++)
            {
                var e = meshEntries[o];
                allEntries.Add((e.GlobalIndex, "mesh", o, e.MeshContext.Type.ToString(), e.MeshContext.Name));
            }
            for (int o = 0; o < boneEntries.Count; o++)
            {
                var e = boneEntries[o];
                allEntries.Add((e.GlobalIndex, "bone", o, e.MeshContext.Type.ToString(), e.MeshContext.Name));
            }
            for (int o = 0; o < morphEntries.Count; o++)
            {
                var e = morphEntries[o];
                allEntries.Add((e.GlobalIndex, "morph", o, e.MeshContext.Type.ToString(), e.MeshContext.Name));
            }

            // globalIndex順にソート（MeshContextListの復元順序）
            allEntries.Sort((a, b) => a.globalIndex.CompareTo(b.globalIndex));

            foreach (var e in allEntries)
            {
                if (useNameBased)
                {
                    // entryByName,name,type,file,orderInFile
                    sb.AppendLine($"entryByName,{Esc(e.name)},{e.type},{e.file},{e.orderInFile}");
                }
                else
                {
                    sb.AppendLine($"entry,{e.globalIndex},{e.type},{Esc(e.name)},{e.file},{e.orderInFile}");
                }
            }

            File.WriteAllText(Path.Combine(folderPath, "model.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static (string modelName, List<ModelEntry> entries) ReadModelCsv(string path)
        {
            string modelName = "Model";
            var entries = new List<ModelEntry>();

            if (!File.Exists(path)) return (modelName, entries);

            int autoIndex = 0;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length == 0 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "name":
                        modelName = cols.Length > 1 ? Unesc(cols[1]) : "Model";
                        break;
                    case "nameBasedMode":
                        // 読み取り確認用（特別な処理は不要、entryByNameで自動判別）
                        break;
                    case "entry":
                        // entry,globalIndex,type,name,file,orderInFile
                        if (cols.Length >= 6)
                        {
                            entries.Add(new ModelEntry
                            {
                                GlobalIndex = PInt(cols, 1),
                                Type = cols[2],
                                Name = Unesc(cols[3]),
                                File = cols[4],
                                OrderInFile = PInt(cols, 5)
                            });
                        }
                        break;
                    case "entryByName":
                        // entryByName,name,type,file,orderInFile
                        if (cols.Length >= 5)
                        {
                            entries.Add(new ModelEntry
                            {
                                GlobalIndex = autoIndex++,
                                Type = cols[2],
                                Name = Unesc(cols[1]),
                                File = cols[3],
                                OrderInFile = PInt(cols, 4),
                                IsNameBased = true
                            });
                        }
                        break;
                }
            }

            return (modelName, entries);
        }

        // ================================================================
        // materials.csv
        // ================================================================

        private static void WriteMaterialsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Materials,version,1.0");
            sb.AppendLine($"currentIndex,{model.CurrentMaterialIndex}");

            if (model.MaterialReferences != null)
            {
                for (int i = 0; i < model.MaterialReferences.Count; i++)
                {
                    WriteMaterialRefLine(sb, "mat", i, model.MaterialReferences[i]);
                }
            }

            sb.AppendLine($"defaultCurrentIndex,{model.DefaultCurrentMaterialIndex}");
            sb.AppendLine($"autoSetDefault,{model.AutoSetDefaultMaterials}");

            if (model.DefaultMaterialReferences != null)
            {
                for (int i = 0; i < model.DefaultMaterialReferences.Count; i++)
                {
                    WriteMaterialRefLine(sb, "defaultMat", i, model.DefaultMaterialReferences[i]);
                }
            }

            File.WriteAllText(Path.Combine(folderPath, "materials.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void WriteMaterialRefLine(StringBuilder sb, string prefix, int index, MaterialReference matRef)
        {
            if (matRef == null)
            {
                sb.AppendLine($"{prefix},{index}");
                return;
            }

            var d = matRef.Data ?? new MaterialData();
            // prefix,index,name,shaderType,assetPath,r,g,b,a,metallic,smoothness,
            // normalScale,occlusionStrength,emissionEnabled,emR,emG,emB,emA,
            // surface,blendMode,cullMode,alphaClipEnabled,alphaCutoff,
            // baseMapPath,metallicMapPath,normalMapPath,occlusionMapPath,emissionMapPath,
            // sourceTexturePath,sourceAlphaMapPath,sourceBumpMapPath
            sb.Append($"{prefix},{index},{Esc(d.Name)},{d.ShaderType},{Esc(matRef.AssetPath ?? "")}");
            sb.Append($",{Fc(d.BaseColor, 0)},{Fc(d.BaseColor, 1)},{Fc(d.BaseColor, 2)},{Fc(d.BaseColor, 3)}");
            sb.Append($",{Fl(d.Metallic)},{Fl(d.Smoothness)},{Fl(d.NormalScale)},{Fl(d.OcclusionStrength)}");
            sb.Append($",{d.EmissionEnabled},{Fc(d.EmissionColor, 0)},{Fc(d.EmissionColor, 1)},{Fc(d.EmissionColor, 2)},{Fc(d.EmissionColor, 3)}");
            sb.Append($",{(int)d.Surface},{(int)d.BlendMode},{(int)d.CullMode},{d.AlphaClipEnabled},{Fl(d.AlphaCutoff)}");
            sb.Append($",{Esc(d.BaseMapPath ?? "")},{Esc(d.MetallicMapPath ?? "")},{Esc(d.NormalMapPath ?? "")}");
            sb.Append($",{Esc(d.OcclusionMapPath ?? "")},{Esc(d.EmissionMapPath ?? "")}");
            sb.Append($",{Esc(d.SourceTexturePath ?? "")},{Esc(d.SourceAlphaMapPath ?? "")},{Esc(d.SourceBumpMapPath ?? "")}");
            sb.AppendLine();
        }

        private static void ReadMaterialsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "materials.csv");
            if (!File.Exists(path))
            {
                model.MaterialReferences = new List<MaterialReference> { new MaterialReference() };
                return;
            }

            var matRefs = new List<MaterialReference>();
            var defaultMatRefs = new List<MaterialReference>();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length == 0 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "currentIndex":
                        model.CurrentMaterialIndex = PInt(cols, 1);
                        break;
                    case "defaultCurrentIndex":
                        model.DefaultCurrentMaterialIndex = PInt(cols, 1);
                        break;
                    case "autoSetDefault":
                        model.AutoSetDefaultMaterials = PBool(cols, 1, true);
                        break;
                    case "mat":
                        matRefs.Add(ReadMaterialRefLine(cols));
                        break;
                    case "defaultMat":
                        defaultMatRefs.Add(ReadMaterialRefLine(cols));
                        break;
                }
            }

            model.MaterialReferences = matRefs.Count > 0 ? matRefs : new List<MaterialReference> { new MaterialReference() };
            model.DefaultMaterialReferences = defaultMatRefs.Count > 0 ? defaultMatRefs : new List<MaterialReference> { new MaterialReference() };
        }

        private static MaterialReference ReadMaterialRefLine(string[] cols)
        {
            // prefix,index,name,shaderType,assetPath,r,g,b,a,metallic,smoothness,
            // normalScale,occlusionStrength,emissionEnabled,emR,emG,emB,emA,
            // surface,blendMode,cullMode,alphaClipEnabled,alphaCutoff,
            // baseMapPath,metallicMapPath,normalMapPath,occlusionMapPath,emissionMapPath,
            // sourceTexturePath,sourceAlphaMapPath,sourceBumpMapPath
            if (cols.Length < 5) return new MaterialReference();

            int idx = 2; // skip prefix, index
            var d = new MaterialData();
            d.Name = Unesc(SafeGet(cols, idx++));

            string shaderStr = SafeGet(cols, idx++);
            if (Enum.TryParse<ShaderType>(shaderStr, out var st)) d.ShaderType = st;

            string assetPath = Unesc(SafeGet(cols, idx++));

            d.BaseColor = new float[] { PFl(cols, idx++, 1f), PFl(cols, idx++, 1f), PFl(cols, idx++, 1f), PFl(cols, idx++, 1f) };
            d.Metallic = PFl(cols, idx++);
            d.Smoothness = PFl(cols, idx++, 0.5f);
            d.NormalScale = PFl(cols, idx++, 1f);
            d.OcclusionStrength = PFl(cols, idx++, 1f);
            d.EmissionEnabled = PBool(cols, idx++);
            d.EmissionColor = new float[] { PFl(cols, idx++), PFl(cols, idx++), PFl(cols, idx++), PFl(cols, idx++, 1f) };
            d.Surface = (SurfaceType)PInt(cols, idx++);
            d.BlendMode = (BlendModeType)PInt(cols, idx++);
            d.CullMode = (CullModeType)PInt(cols, idx++, 2);
            d.AlphaClipEnabled = PBool(cols, idx++);
            d.AlphaCutoff = PFl(cols, idx++, 0.5f);
            d.BaseMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.MetallicMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.NormalMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.OcclusionMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.EmissionMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.SourceTexturePath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.SourceAlphaMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.SourceBumpMapPath = NullIfEmpty(Unesc(SafeGet(cols, idx++)));

            var matRef = new MaterialReference
            {
                AssetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath,
                Data = d
            };

            return matRef;
        }

        // ================================================================
        // humanoid.csv
        // ================================================================

        private static void WriteHumanoidCsv(string folderPath, ModelContext model,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Humanoid,version,1.0");

            var dict = model.HumanoidMapping.ToDictionary();
            foreach (var kvp in dict)
            {
                if (useNameBased && indexToName != null)
                {
                    string boneName = indexToName.TryGetValue(kvp.Value, out var n) ? n : "";
                    sb.AppendLine($"{Esc(kvp.Key)},{Esc(boneName)}");
                }
                else
                {
                    sb.AppendLine($"{Esc(kvp.Key)},{kvp.Value}");
                }
            }

            File.WriteAllText(Path.Combine(folderPath, "humanoid.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadHumanoidCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "humanoid.csv");
            if (!File.Exists(path)) return;

            var dict = new Dictionary<string, int>();
            var nameDict = new Dictionary<string, string>(); // humanoidBoneName → meshContextName
            bool isNameBased = false;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length >= 2)
                {
                    string boneName = Unesc(cols[0]);
                    string value = cols[1];

                    if (int.TryParse(value, out int boneIndex) && boneIndex >= 0)
                    {
                        // インデックスベース
                        dict[boneName] = boneIndex;
                    }
                    else
                    {
                        // 名前ベース
                        isNameBased = true;
                        nameDict[boneName] = Unesc(value);
                    }
                }
            }

            if (isNameBased && nameDict.Count > 0)
            {
                // 名前→インデックス辞書を構築して解決
                var nameToIndex = BuildNameToIndex(model);
                foreach (var kvp in nameDict)
                {
                    if (nameToIndex.TryGetValue(kvp.Value, out int idx))
                        dict[kvp.Key] = idx;
                }
            }

            if (dict.Count > 0)
                model.HumanoidMapping.FromDictionary(dict);
        }

        // ================================================================
        // morphgroups.csv
        // ================================================================

        private static void WriteMorphGroupsCsv(string folderPath, ModelContext model,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MorphGroups,version,1.0");

            foreach (var me in model.MorphExpressions)
            {
                // name,nameEnglish,panel,type,entryCount,meshRef0:weight0,...
                sb.Append($"{Esc(me.Name)},{Esc(me.NameEnglish)},{me.Panel},{(int)me.Type},{me.MeshEntries.Count}");
                foreach (var entry in me.MeshEntries)
                {
                    if (useNameBased && indexToName != null)
                    {
                        string meshName = indexToName.TryGetValue(entry.MeshIndex, out var n) ? n : "";
                        sb.Append($",{Esc(meshName)}:{Fl(entry.Weight)}");
                    }
                    else
                    {
                        sb.Append($",{entry.MeshIndex}:{Fl(entry.Weight)}");
                    }
                }
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(folderPath, "morphgroups.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadMorphGroupsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "morphgroups.csv");
            if (!File.Exists(path)) return;

            model.MorphExpressions = new List<MorphExpression>();
            bool needsNameResolution = false;
            // 一時格納: (expressionIndex, entryIndex) → meshName
            var nameRefs = new List<(int exprIdx, int entryIdx, string meshName)>();

            int exprIdx = 0;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 5) continue;

                var me = new MorphExpression
                {
                    Name = Unesc(cols[0]),
                    NameEnglish = Unesc(cols[1]),
                    Panel = PInt(cols, 2, 3),
                    Type = (MorphType)PInt(cols, 3, 1)
                };

                int entryCount = PInt(cols, 4);
                for (int i = 0; i < entryCount; i++)
                {
                    int ci = 5 + i;
                    if (ci >= cols.Length) break;
                    var parts = cols[ci].Split(':');
                    if (parts.Length >= 2)
                    {
                        float weight = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1f;

                        if (int.TryParse(parts[0], out var meshIndex))
                        {
                            // インデックスベース
                            me.MeshEntries.Add(new MorphMeshEntry(meshIndex, weight));
                        }
                        else
                        {
                            // 名前ベース → 仮インデックス-1で格納、後で解決
                            me.MeshEntries.Add(new MorphMeshEntry(-1, weight));
                            nameRefs.Add((exprIdx, me.MeshEntries.Count - 1, Unesc(parts[0])));
                            needsNameResolution = true;
                        }
                    }
                }

                model.MorphExpressions.Add(me);
                exprIdx++;
            }

            // 名前解決
            if (needsNameResolution)
            {
                var nameToIndex = BuildNameToIndex(model);
                foreach (var (ei, enti, meshName) in nameRefs)
                {
                    if (ei < model.MorphExpressions.Count && enti < model.MorphExpressions[ei].MeshEntries.Count)
                    {
                        int resolved = nameToIndex.TryGetValue(meshName, out var idx) ? idx : -1;
                        var entry = model.MorphExpressions[ei].MeshEntries[enti];
                        model.MorphExpressions[ei].MeshEntries[enti] = new MorphMeshEntry(resolved, entry.Weight);
                    }
                }
            }
        }

        // ================================================================
        // meshselsets.csv（メッシュ選択セット）
        // ================================================================

        private static void WriteMeshSelSetsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MeshSelSets,version,1.0");

            foreach (var ms in model.MeshSelectionSets)
            {
                // name,category,nameCount,meshName0,meshName1,...
                sb.Append($"{Esc(ms.Name)},{ms.Category},{ms.MeshNames.Count}");
                foreach (var meshName in ms.MeshNames)
                {
                    sb.Append($",{Esc(meshName)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(folderPath, "meshselsets.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadMeshSelSetsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "meshselsets.csv");
            if (!File.Exists(path)) return;

            model.MeshSelectionSets = new List<MeshSelectionSet>();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 3) continue;

                var ms = new MeshSelectionSet(Unesc(cols[0]));

                if (Enum.TryParse<ModelContext.SelectionCategory>(cols[1], out var cat))
                    ms.Category = cat;

                int nameCount = PInt(cols, 2);
                for (int i = 0; i < nameCount; i++)
                {
                    int ci = 3 + i;
                    if (ci >= cols.Length) break;
                    string meshName = Unesc(cols[ci]);
                    if (!string.IsNullOrEmpty(meshName))
                        ms.MeshNames.Add(meshName);
                }

                model.MeshSelectionSets.Add(ms);
            }
        }

        // ================================================================
        // mirrorpairs.csv（ミラーペア情報）
        // ================================================================

        private static void WriteMirrorPairsCsv(string folderPath, ModelContext model,
            bool useNameBased = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MirrorPairs,version,1.0");

            foreach (var pair in model.MirrorPairs)
            {
                if (useNameBased)
                {
                    string realName = pair.Real?.Name ?? "";
                    string mirrorName = pair.Mirror?.Name ?? "";
                    if (string.IsNullOrEmpty(realName) || string.IsNullOrEmpty(mirrorName)) continue;
                    sb.AppendLine($"{Esc(realName)},{Esc(mirrorName)},{(int)pair.Axis}");
                }
                else
                {
                    int realIdx = model.MeshContextList.IndexOf(pair.Real);
                    int mirrorIdx = model.MeshContextList.IndexOf(pair.Mirror);
                    if (realIdx < 0 || mirrorIdx < 0) continue;
                    sb.AppendLine($"{realIdx},{mirrorIdx},{(int)pair.Axis}");
                }
            }

            File.WriteAllText(Path.Combine(folderPath, "mirrorpairs.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadMirrorPairsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "mirrorpairs.csv");
            if (!File.Exists(path)) return;

            model.MirrorPairs = new List<MirrorPair>();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 3) continue;

                int axis = PInt(cols, 2, 0);
                MeshContext realCtx = null;
                MeshContext mirrorCtx = null;

                if (int.TryParse(cols[0], out int realIdx) && int.TryParse(cols[1], out int mirrorIdx))
                {
                    // インデックスベース
                    if (realIdx < 0 || realIdx >= model.Count) continue;
                    if (mirrorIdx < 0 || mirrorIdx >= model.Count) continue;
                    realCtx = model.GetMeshContext(realIdx);
                    mirrorCtx = model.GetMeshContext(mirrorIdx);
                }
                else
                {
                    // 名前ベース
                    string realName = Unesc(cols[0]);
                    string mirrorName = Unesc(cols[1]);
                    var nameToIndex = BuildNameToIndex(model);
                    if (nameToIndex.TryGetValue(realName, out int ri))
                        realCtx = model.GetMeshContext(ri);
                    if (nameToIndex.TryGetValue(mirrorName, out int mi))
                        mirrorCtx = model.GetMeshContext(mi);
                }

                if (realCtx == null || mirrorCtx == null) continue;

                var pair = new MirrorPair
                {
                    Real = realCtx,
                    Mirror = mirrorCtx,
                    Axis = (Poly_Ling.Symmetry.SymmetryAxis)axis
                };
                if (pair.Build())
                {
                    model.MirrorPairs.Add(pair);
                    Debug.Log($"[CsvModelSerializer] Restored MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name}");
                }
                else
                {
                    Debug.LogWarning($"[CsvModelSerializer] Failed to rebuild MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name}: {pair.BuildLog}");
                }
            }
        }

        /// <summary>
        /// エントリリストにMirrorPeer情報を設定し、MirrorSideが未含の場合は追加する。
        /// Copy/Cut/SaveCsv/SaveModelの全エクスポートパスで共通使用する。
        /// </summary>
        public static void EnrichEntriesWithMirrorPeers(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return;

            // 既存エントリのMeshContextセット
            var existingMcs = new HashSet<MeshContext>();
            var mcToEntry = new Dictionary<MeshContext, CsvMeshEntry>();
            foreach (var e in entries)
            {
                if (e.MeshContext != null)
                {
                    existingMcs.Add(e.MeshContext);
                    mcToEntry[e.MeshContext] = e;
                }
            }

            // 追加すべきMirrorSideを収集（ループ中にentriesを変更しないため）
            var toAdd = new List<(int insertAfter, CsvMeshEntry entry)>();
            var handledReals = new HashSet<MeshContext>();

            // (1) MirrorPairsからペア情報を設定
            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    if (pair.Real == null || pair.Mirror == null) continue;
                    if (!mcToEntry.TryGetValue(pair.Real, out var realEntry)) continue;

                    realEntry.MirrorPeerName = pair.Mirror.Name;
                    realEntry.MirrorPeerAxis = (int)pair.Axis;
                    handledReals.Add(pair.Real);

                    if (!existingMcs.Contains(pair.Mirror))
                    {
                        int mirrorGlobalIndex = model.MeshContextList.IndexOf(pair.Mirror);
                        var mirrorEntry = new CsvMeshEntry
                        {
                            GlobalIndex = mirrorGlobalIndex >= 0 ? mirrorGlobalIndex : 0,
                            MeshContext = pair.Mirror
                        };
                        int realPos = entries.IndexOf(realEntry);
                        toAdd.Add((realPos, mirrorEntry));
                        existingMcs.Add(pair.Mirror);
                    }
                }
            }

            // (2) MirrorPairsで処理されなかったmirrorType>0のエントリ → 命名規則で検索
            // モデル全体のMeshContext名→MeshContext辞書
            Dictionary<string, MeshContext> nameToMc = null;

            foreach (var e in entries)
            {
                if (e.MeshContext == null) continue;
                if (handledReals.Contains(e.MeshContext)) continue;
                if (e.MeshContext.MirrorType <= 0) continue;
                if (!string.IsNullOrEmpty(e.MirrorPeerName)) continue; // 既に設定済み

                // 命名規則: Real名+"+" がMirrorSide
                if (nameToMc == null)
                {
                    nameToMc = new Dictionary<string, MeshContext>();
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToMc.ContainsKey(mc.Name))
                            nameToMc[mc.Name] = mc;
                    }
                }

                string mirrorName = e.MeshContext.Name + "+";
                if (!nameToMc.TryGetValue(mirrorName, out var mirrorMc)) continue;

                e.MirrorPeerName = mirrorName;
                e.MirrorPeerAxis = e.MeshContext.MirrorAxis;

                if (!existingMcs.Contains(mirrorMc))
                {
                    int mirrorGlobalIndex = model.MeshContextList.IndexOf(mirrorMc);
                    var mirrorEntry = new CsvMeshEntry
                    {
                        GlobalIndex = mirrorGlobalIndex >= 0 ? mirrorGlobalIndex : 0,
                        MeshContext = mirrorMc
                    };
                    int realPos = entries.IndexOf(e);
                    toAdd.Add((realPos, mirrorEntry));
                    existingMcs.Add(mirrorMc);
                }

                Debug.Log($"[CsvModelSerializer] EnrichMirrorPeer by naming: {e.MeshContext.Name} → {mirrorName}");
            }

            // Real直後に挿入（後ろから挿入してインデックスずれを防止）
            for (int i = toAdd.Count - 1; i >= 0; i--)
            {
                entries.Insert(toAdd[i].insertAfter + 1, toAdd[i].entry);
            }
        }

        /// <summary>
        /// エントリのmirrorPeer情報からMirrorPairを構築してModelContextに追加
        /// 既にペアが存在する場合は重複追加しない
        /// </summary>
        public static void BuildMirrorPairsFromEntries(List<CsvMeshEntry> entries, ModelContext model)
        {
            if (entries == null || model == null) return;
            if (model.MirrorPairs == null)
                model.MirrorPairs = new List<MirrorPair>();

            // 既存ペアのReal名セット（重複防止）
            var existingRealNames = new HashSet<string>();
            foreach (var p in model.MirrorPairs)
            {
                if (p.Real != null)
                    existingRealNames.Add(p.Real.Name);
            }

            // MeshContext名→MeshContext逆引き
            var nameToCtx = new Dictionary<string, MeshContext>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToCtx.ContainsKey(mc.Name))
                    nameToCtx[mc.Name] = mc;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.MirrorPeerName)) continue;
                if (entry.MeshContext == null) continue;

                string realName = entry.MeshContext.Name;
                if (string.IsNullOrEmpty(realName)) continue;
                if (existingRealNames.Contains(realName)) continue;

                if (!nameToCtx.TryGetValue(entry.MirrorPeerName, out var mirrorCtx)) continue;
                if (!nameToCtx.TryGetValue(realName, out var realCtx)) continue;

                var pair = new MirrorPair
                {
                    Real = realCtx,
                    Mirror = mirrorCtx,
                    Axis = (Poly_Ling.Symmetry.SymmetryAxis)entry.MirrorPeerAxis
                };

                if (pair.Build())
                {
                    model.MirrorPairs.Add(pair);
                    existingRealNames.Add(realName);
                    Debug.Log($"[CsvModelSerializer] MirrorPair from entry: {realName} ↔ {entry.MirrorPeerName}");
                }
                else
                {
                    Debug.LogWarning($"[CsvModelSerializer] MirrorPair build failed from entry: {realName} ↔ {entry.MirrorPeerName}: {pair.BuildLog}");
                }
            }
        }

        // ================================================================
        // editorstate.csv
        // ================================================================

        private static void WriteEditorStateCsv(string folderPath, EditorStateDTO es,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_EditorState,version,1.0");
            sb.AppendLine($"rotationX,{Fl(es.rotationX)}");
            sb.AppendLine($"rotationY,{Fl(es.rotationY)}");
            sb.AppendLine($"cameraDistance,{Fl(es.cameraDistance)}");
            if (es.cameraTarget != null && es.cameraTarget.Length >= 3)
                sb.AppendLine($"cameraTarget,{Fl(es.cameraTarget[0])},{Fl(es.cameraTarget[1])},{Fl(es.cameraTarget[2])}");
            sb.AppendLine($"showWireframe,{es.showWireframe}");
            sb.AppendLine($"showVertices,{es.showVertices}");
            sb.AppendLine($"vertexEditMode,{es.vertexEditMode}");
            sb.AppendLine($"currentToolName,{Esc(es.currentToolName ?? "")}");

            if (useNameBased && indexToName != null)
            {
                string meshName = indexToName.TryGetValue(es.selectedMeshIndex, out var mn) ? mn : "";
                string boneName = indexToName.TryGetValue(es.selectedBoneIndex, out var bn) ? bn : "";
                string morphName = indexToName.TryGetValue(es.selectedVertexMorphIndex, out var vmn) ? vmn : "";
                sb.AppendLine($"selectedMeshName,{Esc(meshName)}");
                sb.AppendLine($"selectedBoneName,{Esc(boneName)}");
                sb.AppendLine($"selectedVertexMorphName,{Esc(morphName)}");
            }
            else
            {
                sb.AppendLine($"selectedMeshIndex,{es.selectedMeshIndex}");
                sb.AppendLine($"selectedBoneIndex,{es.selectedBoneIndex}");
                sb.AppendLine($"selectedVertexMorphIndex,{es.selectedVertexMorphIndex}");
            }

            sb.AppendLine($"pmxUnityRatio,{Fl(es.pmxUnityRatio)}");
            sb.AppendLine($"pmxFlipZ,{es.pmxFlipZ}");
            sb.AppendLine($"mqoFlipZ,{es.mqoFlipZ}");
            sb.AppendLine($"mqoUnityRatio,{Fl(es.mqoUnityRatio)}");
            sb.AppendLine($"showBones,{es.showBones}");
            sb.AppendLine($"showUnselectedBones,{es.showUnselectedBones}");
            sb.AppendLine($"boneDisplayAlongY,{es.boneDisplayAlongY}");

            File.WriteAllText(Path.Combine(folderPath, "editorstate.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static EditorStateDTO ReadEditorStateCsv(string path, ModelContext model = null)
        {
            var es = EditorStateDTO.CreateDefault();
            if (!File.Exists(path)) return es;

            // 名前ベース一時格納
            string selectedMeshName = null;
            string selectedBoneName = null;
            string selectedVertexMorphName = null;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length < 2 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "rotationX": es.rotationX = PFl(cols, 1, 20f); break;
                    case "rotationY": es.rotationY = PFl(cols, 1); break;
                    case "cameraDistance": es.cameraDistance = PFl(cols, 1, 2f); break;
                    case "cameraTarget":
                        es.cameraTarget = new float[] { PFl(cols, 1), PFl(cols, 2), PFl(cols, 3) };
                        break;
                    case "showWireframe": es.showWireframe = PBool(cols, 1, true); break;
                    case "showVertices": es.showVertices = PBool(cols, 1, true); break;
                    case "vertexEditMode": es.vertexEditMode = PBool(cols, 1, true); break;
                    case "currentToolName": es.currentToolName = Unesc(cols[1]); break;
                    case "selectedMeshIndex": es.selectedMeshIndex = PInt(cols, 1, -1); break;
                    case "selectedBoneIndex": es.selectedBoneIndex = PInt(cols, 1, -1); break;
                    case "selectedVertexMorphIndex": es.selectedVertexMorphIndex = PInt(cols, 1, -1); break;
                    case "selectedMeshName": selectedMeshName = Unesc(cols[1]); break;
                    case "selectedBoneName": selectedBoneName = Unesc(cols[1]); break;
                    case "selectedVertexMorphName": selectedVertexMorphName = Unesc(cols[1]); break;
                    case "pmxUnityRatio": case "coordinateScale": es.pmxUnityRatio = PFl(cols, 1, 0.1f); break;// 旧coordinateScale互換
                    case "pmxFlipZ": es.pmxFlipZ = PBool(cols, 1); break;
                    case "mqoFlipZ": es.mqoFlipZ = PBool(cols, 1, true); break;
                    case "mqoUnityRatio": es.mqoUnityRatio = PFl(cols, 1, 0.01f); break;
                    case "mqoPmxRatio": float oldRatio = PFl(cols, 1, 10f); es.mqoUnityRatio = oldRatio > 0f ? es.pmxUnityRatio / oldRatio : 0.01f; break; // 旧mqoPmxRatio互換
                    case "showBones": es.showBones = PBool(cols, 1, true); break;
                    case "showUnselectedBones": es.showUnselectedBones = PBool(cols, 1); break;
                    case "boneDisplayAlongY": es.boneDisplayAlongY = PBool(cols, 1); break;
                }
            }

            // 名前ベース解決
            if (model != null && (selectedMeshName != null || selectedBoneName != null || selectedVertexMorphName != null))
            {
                var nameToIndex = BuildNameToIndex(model);
                if (selectedMeshName != null && nameToIndex.TryGetValue(selectedMeshName, out int mi))
                    es.selectedMeshIndex = mi;
                if (selectedBoneName != null && nameToIndex.TryGetValue(selectedBoneName, out int bi))
                    es.selectedBoneIndex = bi;
                if (selectedVertexMorphName != null && nameToIndex.TryGetValue(selectedVertexMorphName, out int vi))
                    es.selectedVertexMorphIndex = vi;
            }

            return es;
        }

        // ================================================================
        // workplane.csv
        // ================================================================

        private static void WriteWorkPlaneCsv(string folderPath, WorkPlaneContext wp)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_WorkPlane,version,1.0");
            sb.AppendLine($"mode,{wp.Mode}");
            sb.AppendLine($"origin,{Fl(wp.Origin.x)},{Fl(wp.Origin.y)},{Fl(wp.Origin.z)}");
            sb.AppendLine($"axisU,{Fl(wp.AxisU.x)},{Fl(wp.AxisU.y)},{Fl(wp.AxisU.z)}");
            sb.AppendLine($"axisV,{Fl(wp.AxisV.x)},{Fl(wp.AxisV.y)},{Fl(wp.AxisV.z)}");
            sb.AppendLine($"isLocked,{wp.IsLocked}");
            sb.AppendLine($"lockOrientation,{wp.LockOrientation}");
            sb.AppendLine($"autoUpdateOriginOnSelection,{wp.AutoUpdateOriginOnSelection}");

            File.WriteAllText(Path.Combine(folderPath, "workplane.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static WorkPlaneContext ReadWorkPlaneCsv(string path)
        {
            var wp = new WorkPlaneContext();
            if (!File.Exists(path)) return wp;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length < 2 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "mode":
                        if (Enum.TryParse<WorkPlaneMode>(cols[1], out var mode))
                            wp.Mode = mode;
                        break;
                    case "origin":
                        wp.Origin = new Vector3(PFl(cols, 1), PFl(cols, 2), PFl(cols, 3));
                        break;
                    case "axisU":
                        wp.AxisU = new Vector3(PFl(cols, 1), PFl(cols, 2), PFl(cols, 3));
                        break;
                    case "axisV":
                        wp.AxisV = new Vector3(PFl(cols, 1), PFl(cols, 2), PFl(cols, 3));
                        break;
                    case "isLocked":
                        wp.IsLocked = PBool(cols, 1);
                        break;
                    case "lockOrientation":
                        wp.LockOrientation = PBool(cols, 1);
                        break;
                    case "autoUpdateOriginOnSelection":
                        wp.AutoUpdateOriginOnSelection = PBool(cols, 1, true);
                        break;
                }
            }

            return wp;
        }

        // ================================================================
        // Load: フォルダ内の全メッシュエントリ読み込み（マージ用）
        // ================================================================

        /// <summary>
        /// フォルダ内の全mesh/bone/morph CSVからエントリを読み込む
        /// model.csvを無視し、ファイル内の順序でそのまま返す
        /// </summary>
        public static List<CsvMeshEntry> LoadAllMeshEntriesFromFolder(string folderPath)
        {
            var result = new List<CsvMeshEntry>();
            if (!Directory.Exists(folderPath)) return result;

            // 指定フォルダ直下を検索
            SearchMeshCsvFiles(folderPath, result);

            return result;
        }

        private static void SearchMeshCsvFiles(string folder, List<CsvMeshEntry> result)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 命名規約ファイル (*.mesh.csv, *.bone.csv, *.morph.csv)
            foreach (var type in new[] { "mesh", "bone", "morph" })
            {
                var csvFiles = Directory.GetFiles(folder, $"*.{type}.csv");
                foreach (var csvFile in csvFiles)
                {
                    found.Add(csvFile);
                    result.AddRange(CsvMeshSerializer.ReadFile(csvFile));
                }
            }

            // 2. 命名規約に合わないCSVファイルをヘッダで判別
            var allCsvFiles = Directory.GetFiles(folder, "*.csv");
            foreach (var csvFile in allCsvFiles)
            {
                if (found.Contains(csvFile)) continue;

                try
                {
                    using (var reader = new StreamReader(csvFile, Encoding.UTF8))
                    {
                        string firstLine = reader.ReadLine();
                        if (firstLine != null)
                        {
                            firstLine = firstLine.TrimStart('\uFEFF').Trim(); // BOM除去
                            if (firstLine.StartsWith("#PolyLing_Mesh") ||
                                firstLine.StartsWith("#PolyLing_Bone") ||
                                firstLine.StartsWith("#PolyLing_Morph"))
                            {
                                result.AddRange(CsvMeshSerializer.ReadFile(csvFile));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CsvModelSerializer] Failed to read CSV header: {csvFile}: {ex.Message}");
                }
            }
        }

        // ================================================================
        // TPoseBackup CSV
        // ================================================================

        private static void WriteTPoseBackupCsv(string folderPath, TPoseBackup backup,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_TPoseBackup,version,1.0");

            // BoneRotations
            foreach (var kvp in backup.BoneRotations)
            {
                var r = kvp.Value;
                if (useNameBased && indexToName != null)
                {
                    string name = indexToName.TryGetValue(kvp.Key, out var n) ? n : "";
                    sb.AppendLine($"boneRotByName,{Esc(name)},{Fl(r.x)},{Fl(r.y)},{Fl(r.z)}");
                }
                else
                {
                    sb.AppendLine($"boneRot,{kvp.Key},{Fl(r.x)},{Fl(r.y)},{Fl(r.z)}");
                }
            }

            // WorldMatrices
            foreach (var kvp in backup.WorldMatrices)
            {
                var m = kvp.Value;
                if (useNameBased && indexToName != null)
                {
                    string name = indexToName.TryGetValue(kvp.Key, out var n) ? n : "";
                    sb.AppendLine($"worldMatByName,{Esc(name)},{Fl(m.m00)},{Fl(m.m01)},{Fl(m.m02)},{Fl(m.m03)},{Fl(m.m10)},{Fl(m.m11)},{Fl(m.m12)},{Fl(m.m13)},{Fl(m.m20)},{Fl(m.m21)},{Fl(m.m22)},{Fl(m.m23)},{Fl(m.m30)},{Fl(m.m31)},{Fl(m.m32)},{Fl(m.m33)}");
                }
                else
                {
                    sb.AppendLine($"worldMat,{kvp.Key},{Fl(m.m00)},{Fl(m.m01)},{Fl(m.m02)},{Fl(m.m03)},{Fl(m.m10)},{Fl(m.m11)},{Fl(m.m12)},{Fl(m.m13)},{Fl(m.m20)},{Fl(m.m21)},{Fl(m.m22)},{Fl(m.m23)},{Fl(m.m30)},{Fl(m.m31)},{Fl(m.m32)},{Fl(m.m33)}");
                }
            }

            // BindPoses
            foreach (var kvp in backup.BindPoses)
            {
                var m = kvp.Value;
                if (useNameBased && indexToName != null)
                {
                    string name = indexToName.TryGetValue(kvp.Key, out var n) ? n : "";
                    sb.AppendLine($"bindPoseByName,{Esc(name)},{Fl(m.m00)},{Fl(m.m01)},{Fl(m.m02)},{Fl(m.m03)},{Fl(m.m10)},{Fl(m.m11)},{Fl(m.m12)},{Fl(m.m13)},{Fl(m.m20)},{Fl(m.m21)},{Fl(m.m22)},{Fl(m.m23)},{Fl(m.m30)},{Fl(m.m31)},{Fl(m.m32)},{Fl(m.m33)}");
                }
                else
                {
                    sb.AppendLine($"bindPose,{kvp.Key},{Fl(m.m00)},{Fl(m.m01)},{Fl(m.m02)},{Fl(m.m03)},{Fl(m.m10)},{Fl(m.m11)},{Fl(m.m12)},{Fl(m.m13)},{Fl(m.m20)},{Fl(m.m21)},{Fl(m.m22)},{Fl(m.m23)},{Fl(m.m30)},{Fl(m.m31)},{Fl(m.m32)},{Fl(m.m33)}");
                }
            }

            // VertexPositions（メッシュインデックスキー）
            foreach (var kvp in backup.VertexPositions)
            {
                var positions = kvp.Value;
                if (useNameBased && indexToName != null)
                {
                    string name = indexToName.TryGetValue(kvp.Key, out var n) ? n : "";
                    sb.Append($"vtxPosByName,{Esc(name)},{positions.Length}");
                }
                else
                {
                    sb.Append($"vtxPos,{kvp.Key},{positions.Length}");
                }
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    sb.Append($",{Fl(p.x)},{Fl(p.y)},{Fl(p.z)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(folderPath, "tposebackup.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static TPoseBackup ReadTPoseBackupCsv(string path, ModelContext model = null)
        {
            var backup = new TPoseBackup();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Dictionary<string, int> nameToIndex = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var cols = Split(line);
                if (cols.Length < 2) continue;

                string key = cols[0];

                switch (key)
                {
                    case "boneRot":
                    {
                        int idx = PInt(cols, 1);
                        backup.BoneRotations[idx] = new Vector3(
                            PFl(cols, 2), PFl(cols, 3), PFl(cols, 4));
                        break;
                    }
                    case "boneRotByName":
                    {
                        if (nameToIndex == null && model != null) nameToIndex = BuildNameToIndex(model);
                        string name = Unesc(cols[1]);
                        int idx = nameToIndex != null && nameToIndex.TryGetValue(name, out var ni) ? ni : -1;
                        if (idx >= 0)
                            backup.BoneRotations[idx] = new Vector3(PFl(cols, 2), PFl(cols, 3), PFl(cols, 4));
                        break;
                    }
                    case "worldMat":
                    {
                        int idx = PInt(cols, 1);
                        backup.WorldMatrices[idx] = ReadMat(cols, 2);
                        break;
                    }
                    case "worldMatByName":
                    {
                        if (nameToIndex == null && model != null) nameToIndex = BuildNameToIndex(model);
                        string name = Unesc(cols[1]);
                        int idx = nameToIndex != null && nameToIndex.TryGetValue(name, out var ni) ? ni : -1;
                        if (idx >= 0)
                            backup.WorldMatrices[idx] = ReadMat(cols, 2);
                        break;
                    }
                    case "bindPose":
                    {
                        int idx = PInt(cols, 1);
                        backup.BindPoses[idx] = ReadMat(cols, 2);
                        break;
                    }
                    case "bindPoseByName":
                    {
                        if (nameToIndex == null && model != null) nameToIndex = BuildNameToIndex(model);
                        string name = Unesc(cols[1]);
                        int idx = nameToIndex != null && nameToIndex.TryGetValue(name, out var ni) ? ni : -1;
                        if (idx >= 0)
                            backup.BindPoses[idx] = ReadMat(cols, 2);
                        break;
                    }
                    case "vtxPos":
                    {
                        int idx = PInt(cols, 1);
                        int count = PInt(cols, 2);
                        var positions = new Vector3[count];
                        int ci = 3;
                        for (int v = 0; v < count; v++)
                        {
                            positions[v] = new Vector3(PFl(cols, ci), PFl(cols, ci + 1), PFl(cols, ci + 2));
                            ci += 3;
                        }
                        backup.VertexPositions[idx] = positions;
                        break;
                    }
                    case "vtxPosByName":
                    {
                        if (nameToIndex == null && model != null) nameToIndex = BuildNameToIndex(model);
                        string name = Unesc(cols[1]);
                        int idx = nameToIndex != null && nameToIndex.TryGetValue(name, out var ni) ? ni : -1;
                        if (idx >= 0)
                        {
                            int count = PInt(cols, 2);
                            var positions = new Vector3[count];
                            int ci = 3;
                            for (int v = 0; v < count; v++)
                            {
                                positions[v] = new Vector3(PFl(cols, ci), PFl(cols, ci + 1), PFl(cols, ci + 2));
                                ci += 3;
                            }
                            backup.VertexPositions[idx] = positions;
                        }
                        break;
                    }
                }
            }

            if (backup.BoneRotations.Count == 0 && backup.VertexPositions.Count == 0)
                return null;

            return backup;
        }

        private static Matrix4x4 ReadMat(string[] cols, int start)
        {
            var m = new Matrix4x4();
            m.m00 = PFl(cols, start);     m.m01 = PFl(cols, start + 1); m.m02 = PFl(cols, start + 2); m.m03 = PFl(cols, start + 3);
            m.m10 = PFl(cols, start + 4); m.m11 = PFl(cols, start + 5); m.m12 = PFl(cols, start + 6); m.m13 = PFl(cols, start + 7);
            m.m20 = PFl(cols, start + 8); m.m21 = PFl(cols, start + 9); m.m22 = PFl(cols, start + 10); m.m23 = PFl(cols, start + 11);
            m.m30 = PFl(cols, start + 12); m.m31 = PFl(cols, start + 13); m.m32 = PFl(cols, start + 14); m.m33 = PFl(cols, start + 15);
            return m;
        }

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
