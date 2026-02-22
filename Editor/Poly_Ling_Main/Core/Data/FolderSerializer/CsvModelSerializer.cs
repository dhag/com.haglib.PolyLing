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
        public static void SaveModel(
            string modelFolderPath,
            ModelContext model,
            EditorStateDTO editorState = null,
            WorkPlaneContext workPlane = null)
        {
            if (model == null) return;
            Directory.CreateDirectory(modelFolderPath);

            string modelName = SanitizeFileName(model.Name ?? "Model");

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

            // mesh/bone/morph CSV
            string meshFile = $"{modelName}.mesh.csv";
            string boneFile = $"{modelName}.bone.csv";
            string morphFile = $"{modelName}.morph.csv";

            if (meshEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, meshFile), meshEntries, "mesh");
            if (boneEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, boneFile), boneEntries, "bone");
            if (morphEntries.Count > 0)
                CsvMeshSerializer.WriteFile(Path.Combine(modelFolderPath, morphFile), morphEntries, "morph");

            // model.csv (順序マスター)
            WriteModelCsv(modelFolderPath, model, meshEntries, boneEntries, morphEntries);

            // materials.csv
            WriteMaterialsCsv(modelFolderPath, model);

            // humanoid.csv
            if (model.HumanoidMapping != null && !model.HumanoidMapping.IsEmpty)
                WriteHumanoidCsv(modelFolderPath, model);

            // morphgroups.csv
            if (model.MorphExpressions != null && model.MorphExpressions.Count > 0)
                WriteMorphGroupsCsv(modelFolderPath, model);

            // editorstate.csv
            if (editorState != null)
                WriteEditorStateCsv(modelFolderPath, editorState);

            // workplane.csv
            if (workPlane != null)
                WriteWorkPlaneCsv(modelFolderPath, workPlane);

            // textures フォルダ作成
            Directory.CreateDirectory(Path.Combine(modelFolderPath, "textures"));
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
            var lookup = new Dictionary<int, MeshContext>();
            foreach (var kvp in ownMeshEntries)
            {
                foreach (var me in kvp.Value)
                {
                    lookup[me.GlobalIndex] = me.MeshContext;
                }
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

            // materials.csv
            ReadMaterialsCsv(modelFolderPath, model);

            // humanoid.csv
            ReadHumanoidCsv(modelFolderPath, model);

            // morphgroups.csv
            ReadMorphGroupsCsv(modelFolderPath, model);

            // editorstate.csv
            string esPath = Path.Combine(modelFolderPath, "editorstate.csv");
            if (File.Exists(esPath))
                editorState = ReadEditorStateCsv(esPath);

            // workplane.csv
            string wpPath = Path.Combine(modelFolderPath, "workplane.csv");
            if (File.Exists(wpPath))
                workPlane = ReadWorkPlaneCsv(wpPath);

            return model;
        }

        // ================================================================
        // model.csv
        // ================================================================

        private static void WriteModelCsv(
            string folderPath, ModelContext model,
            List<CsvMeshEntry> meshEntries,
            List<CsvMeshEntry> boneEntries,
            List<CsvMeshEntry> morphEntries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Model,version,1.0");
            sb.AppendLine($"name,{Esc(model.Name)}");
            sb.AppendLine($"meshCount,{model.MeshContextCount}");

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
                sb.AppendLine($"entry,{e.globalIndex},{e.type},{Esc(e.name)},{e.file},{e.orderInFile}");
            }

            File.WriteAllText(Path.Combine(folderPath, "model.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static (string modelName, List<ModelEntry> entries) ReadModelCsv(string path)
        {
            string modelName = "Model";
            var entries = new List<ModelEntry>();

            if (!File.Exists(path)) return (modelName, entries);

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length == 0 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "name":
                        modelName = cols.Length > 1 ? Unesc(cols[1]) : "Model";
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

        private static void WriteHumanoidCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Humanoid,version,1.0");

            var dict = model.HumanoidMapping.ToDictionary();
            foreach (var kvp in dict)
            {
                sb.AppendLine($"{Esc(kvp.Key)},{kvp.Value}");
            }

            File.WriteAllText(Path.Combine(folderPath, "humanoid.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadHumanoidCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "humanoid.csv");
            if (!File.Exists(path)) return;

            var dict = new Dictionary<string, int>();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length >= 2)
                {
                    string boneName = Unesc(cols[0]);
                    int boneIndex = PInt(cols, 1, -1);
                    if (boneIndex >= 0)
                        dict[boneName] = boneIndex;
                }
            }

            if (dict.Count > 0)
                model.HumanoidMapping.FromDictionary(dict);
        }

        // ================================================================
        // morphgroups.csv
        // ================================================================

        private static void WriteMorphGroupsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MorphGroups,version,1.0");

            foreach (var me in model.MorphExpressions)
            {
                // name,nameEnglish,panel,type,entryCount,meshIndex0:weight0,...
                sb.Append($"{Esc(me.Name)},{Esc(me.NameEnglish)},{me.Panel},{(int)me.Type},{me.MeshEntries.Count}");
                foreach (var entry in me.MeshEntries)
                {
                    sb.Append($",{entry.MeshIndex}:{Fl(entry.Weight)}");
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
                        int meshIndex = int.TryParse(parts[0], out var mi) ? mi : 0;
                        float weight = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1f;
                        me.MeshEntries.Add(new MorphMeshEntry(meshIndex, weight));
                    }
                }

                model.MorphExpressions.Add(me);
            }
        }

        // ================================================================
        // editorstate.csv
        // ================================================================

        private static void WriteEditorStateCsv(string folderPath, EditorStateDTO es)
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
            sb.AppendLine($"selectedMeshIndex,{es.selectedMeshIndex}");
            sb.AppendLine($"selectedBoneIndex,{es.selectedBoneIndex}");
            sb.AppendLine($"selectedVertexMorphIndex,{es.selectedVertexMorphIndex}");
            sb.AppendLine($"coordinateScale,{Fl(es.coordinateScale)}");
            sb.AppendLine($"pmxFlipZ,{es.pmxFlipZ}");
            sb.AppendLine($"mqoFlipZ,{es.mqoFlipZ}");
            sb.AppendLine($"mqoPmxRatio,{Fl(es.mqoPmxRatio)}");
            sb.AppendLine($"showBones,{es.showBones}");
            sb.AppendLine($"showUnselectedBones,{es.showUnselectedBones}");
            sb.AppendLine($"boneDisplayAlongY,{es.boneDisplayAlongY}");

            File.WriteAllText(Path.Combine(folderPath, "editorstate.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static EditorStateDTO ReadEditorStateCsv(string path)
        {
            var es = EditorStateDTO.CreateDefault();
            if (!File.Exists(path)) return es;

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
                    case "coordinateScale": es.coordinateScale = PFl(cols, 1, 0.085f); break;
                    case "pmxFlipZ": es.pmxFlipZ = PBool(cols, 1); break;
                    case "mqoFlipZ": es.mqoFlipZ = PBool(cols, 1, true); break;
                    case "mqoPmxRatio": es.mqoPmxRatio = PFl(cols, 1, 10f); break;
                    case "showBones": es.showBones = PBool(cols, 1, true); break;
                    case "showUnselectedBones": es.showUnselectedBones = PBool(cols, 1); break;
                    case "boneDisplayAlongY": es.boneDisplayAlongY = PBool(cols, 1); break;
                }
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

            foreach (var type in new[] { "mesh", "bone", "morph" })
            {
                var csvFiles = Directory.GetFiles(folderPath, $"*.{type}.csv");
                foreach (var csvFile in csvFiles)
                {
                    result.AddRange(CsvMeshSerializer.ReadFile(csvFile));
                }
            }

            return result;
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
