// CsvModelSerializer.ModelCsv.cs
// CSV モデル入出力：model.csv・materials.csv・materialprops.csv・humanoid.export.csv・morphgroups.csv。
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
        // model.csv
        // ================================================================

        /// <summary>
        /// エントリがあれば書き出し、無ければ既存ファイルを削除する。
        /// 削除しないと、ボーンやモーフを持たなくなったモデルを同じフォルダへ
        /// 保存し直したときに古い *.bone.csv / *.morph.csv が残り、
        /// 読込時に index が重なって片方が黙って上書きされる。
        /// </summary>
        private static void WriteOrDeleteMeshCsv(
            string path, List<CsvMeshEntry> entries, string type,
            bool useNameBased, Dictionary<int, string> indexToName)
        {
            if (entries != null && entries.Count > 0)
            {
                CsvMeshSerializer.WriteFile(path, entries, type, useNameBased, indexToName);
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[CsvModelSerializer] 対象が無くなったため削除: {Path.GetFileName(path)}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CsvModelSerializer] 削除に失敗: {Path.GetFileName(path)} - {e.Message}");
            }
        }

        private static void WriteModelCsv(
            string folderPath, ModelContext model,
            List<CsvMeshEntry> meshEntries,
            List<CsvMeshEntry> boneEntries,
            List<CsvMeshEntry> morphEntries,
            List<CsvMeshEntry> pmxPhysicsEntries,
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
            for (int o = 0; o < pmxPhysicsEntries.Count; o++)
            {
                var e = pmxPhysicsEntries[o];
                allEntries.Add((e.GlobalIndex, "pmx_physics", o, e.MeshContext.Type.ToString(), e.MeshContext.Name));
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
            sb.AppendLine("#PolyLing_Materials,version,1.1");
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
            // [v1.1追加] shaderName,baseMapST(4),normalMapST(4),emissionMapST(4),
            //            renderQueueOffset,zWriteOverride,zTest,doubleSidedGI,enableGPUInstancing
            sb.Append($"{prefix},{index},{Esc(d.Name)},{d.ShaderType},{Esc(matRef.AssetPath ?? "")}");
            sb.Append($",{Fc(d.BaseColor, 0)},{Fc(d.BaseColor, 1)},{Fc(d.BaseColor, 2)},{Fc(d.BaseColor, 3)}");
            sb.Append($",{Fl(d.Metallic)},{Fl(d.Smoothness)},{Fl(d.NormalScale)},{Fl(d.OcclusionStrength)}");
            sb.Append($",{d.EmissionEnabled},{Fc(d.EmissionColor, 0)},{Fc(d.EmissionColor, 1)},{Fc(d.EmissionColor, 2)},{Fc(d.EmissionColor, 3)}");
            sb.Append($",{(int)d.Surface},{(int)d.BlendMode},{(int)d.CullMode},{d.AlphaClipEnabled},{Fl(d.AlphaCutoff)}");
            sb.Append($",{Esc(d.BaseMapPath ?? "")},{Esc(d.MetallicMapPath ?? "")},{Esc(d.NormalMapPath ?? "")}");
            sb.Append($",{Esc(d.OcclusionMapPath ?? "")},{Esc(d.EmissionMapPath ?? "")}");
            sb.Append($",{Esc(d.SourceTexturePath ?? "")},{Esc(d.SourceAlphaMapPath ?? "")},{Esc(d.SourceBumpMapPath ?? "")}");
            // --- version 1.1 追加分（末尾追加。旧ファイルは列不足＝既定値で読める）---
            sb.Append($",{Esc(d.ShaderName ?? "")}");
            sb.Append($",{Fc(d.BaseMapST, 0)},{Fc(d.BaseMapST, 1)},{Fc(d.BaseMapST, 2)},{Fc(d.BaseMapST, 3)}");
            sb.Append($",{Fc(d.NormalMapST, 0)},{Fc(d.NormalMapST, 1)},{Fc(d.NormalMapST, 2)},{Fc(d.NormalMapST, 3)}");
            sb.Append($",{Fc(d.EmissionMapST, 0)},{Fc(d.EmissionMapST, 1)},{Fc(d.EmissionMapST, 2)},{Fc(d.EmissionMapST, 3)}");
            sb.Append($",{d.RenderQueueOffset},{d.ZWriteOverride},{d.ZTest},{d.DoubleSidedGI},{d.EnableGPUInstancing}");
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
            // [v1.1追加] shaderName,baseMapST(4),normalMapST(4),emissionMapST(4),
            //            renderQueueOffset,zWriteOverride,zTest,doubleSidedGI,enableGPUInstancing
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

            // --- version 1.1 追加分。旧ファイルは列が足りず、各 P* が既定値を返す ---
            d.ShaderName = NullIfEmpty(Unesc(SafeGet(cols, idx++)));
            d.BaseMapST = ReadST(cols, ref idx);
            d.NormalMapST = ReadST(cols, ref idx);
            d.EmissionMapST = ReadST(cols, ref idx);
            d.RenderQueueOffset = PInt(cols, idx++);
            d.ZWriteOverride = PInt(cols, idx++, -1);
            d.ZTest = PInt(cols, idx++);
            d.DoubleSidedGI = PBool(cols, idx++);
            d.EnableGPUInstancing = PBool(cols, idx++);

            var matRef = new MaterialReference
            {
                AssetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath,
                Data = d
            };

            return matRef;
        }

        /// <summary>ST（tilingX,tilingY,offsetX,offsetY）を4列読む。列不足時は既定 1,1,0,0。</summary>
        private static float[] ReadST(string[] cols, ref int idx)
        {
            float tx = PFl(cols, idx++, 1f);
            float ty = PFl(cols, idx++, 1f);
            float ox = PFl(cols, idx++, 0f);
            float oy = PFl(cols, idx++, 0f);
            return new float[] { tx, ty, ox, oy };
        }

        // ================================================================
        // materialprops.csv（シェーダー固有プロパティ：独立CSV）
        //   materials.csv は位置固定列のため、シェーダーごとに数が変わるプロパティは
        //   列を伸ばさずこちらへ縦持ちで格納する。
        //   行形式: matProp,<scope>,<index>,<name>,<kind>,<x>,<y>,<z>,<w>,<texturePath>
        //     scope … "mat"（MaterialReferences）/ "defaultMat"（DefaultMaterialReferences）
        //     index … 各リスト内の並び順（materials.csv と同じ index）
        //     kind  … MaterialPropertyKind
        //   ファイルが無い＝シェーダー固有プロパティなし（ShaderProperties は null のまま）。
        // ================================================================

        private static void WriteMaterialPropsCsv(string folderPath, ModelContext model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_MaterialProps,version,1.0");

            AppendMaterialPropLines(sb, "mat", model.MaterialReferences);
            AppendMaterialPropLines(sb, "defaultMat", model.DefaultMaterialReferences);

            File.WriteAllText(Path.Combine(folderPath, "materialprops.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void AppendMaterialPropLines(StringBuilder sb, string scope, List<MaterialReference> refs)
        {
            if (refs == null) return;

            for (int i = 0; i < refs.Count; i++)
            {
                var props = refs[i]?.Data?.ShaderProperties;
                if (props == null) continue;

                foreach (var p in props)
                {
                    if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                    sb.AppendLine(
                        $"matProp,{scope},{i},{Esc(p.Name)},{(int)p.Kind}," +
                        $"{Fl(p.X)},{Fl(p.Y)},{Fl(p.Z)},{Fl(p.W)},{Esc(p.TexturePath ?? "")}");
                }
            }
        }

        private static void ReadMaterialPropsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "materialprops.csv");
            if (!File.Exists(path)) return;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var cols = Split(line);
                if (cols.Length < 10 || cols[0] != "matProp") continue;

                string scope = SafeGet(cols, 1);
                int index = PInt(cols, 2, -1);

                var list = (scope == "defaultMat") ? model.DefaultMaterialReferences : model.MaterialReferences;
                if (list == null || index < 0 || index >= list.Count) continue;

                var data = list[index]?.Data;
                if (data == null) continue;

                var prop = new MaterialProperty
                {
                    Name = Unesc(SafeGet(cols, 3)),
                    Kind = (MaterialPropertyKind)PInt(cols, 4),
                    X = PFl(cols, 5),
                    Y = PFl(cols, 6),
                    Z = PFl(cols, 7),
                    W = PFl(cols, 8),
                    TexturePath = NullIfEmpty(Unesc(SafeGet(cols, 9)))
                };
                if (string.IsNullOrEmpty(prop.Name)) continue;

                if (data.ShaderProperties == null)
                    data.ShaderProperties = new List<MaterialProperty>();
                data.ShaderProperties.Add(prop);
            }
        }

        // ================================================================
        // humanoid.export.csv（書き出し専用の派生ファイル）
        //   正本は per-bone（bone.csv の humanBodyBone / humanLimit）で、
        //   ここはそれを AvatarBuilder 向けに度へ直した写し。読み戻さない。
        //   手で直しても無視されるので、名前で派生だと分かるようにしてある。
        // ================================================================

        private static void WriteHumanoidCsv(string folderPath, ModelContext model,
            bool useNameBased = false, Dictionary<int, string> indexToName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Humanoid,version,1.0,derived,write-only");

            var dict = model.HumanoidMapping.ToDictionary();
            foreach (var kvp in dict)
            {
                string row;
                if (useNameBased && indexToName != null)
                {
                    string boneName = indexToName.TryGetValue(kvp.Value, out var n) ? n : "";
                    row = $"{Esc(kvp.Key)},{Esc(boneName)}";
                }
                else
                {
                    row = $"{Esc(kvp.Key)},{kvp.Value}";
                }

                // マッスル可動域（per-bone・A-2）。ラジアン→度で追記（加算互換）。
                //   AvatarBuild(A-1) が読む列: useDefault,minXYZ,maxXYZ,centerXYZ,axisLength
                row += BuildHumanLimitColumnsDeg(model, kvp.Value);

                sb.AppendLine(row);
            }

            File.WriteAllText(
                Path.Combine(folderPath, "humanoid.export.csv"), sb.ToString(), Encoding.UTF8);
        }

        // per-bone HumanLimit（ラジアン）→ humanoid.export.csv の可動域列（度）。
        //   既定値／未保持なら空文字（＝2列のまま・加算互換）。axisLength は角度でないため無変換。
        private static string BuildHumanLimitColumnsDeg(ModelContext model, int boneIndex)
        {
            if (boneIndex < 0) return "";
            var mo = model.GetMeshContext(boneIndex)?.MeshObject;
            var hl = mo?.HumanLimit;
            if (hl == null || hl.UseDefaultValues) return "";

            Vector3 mn = hl.Min * Mathf.Rad2Deg;
            Vector3 mx = hl.Max * Mathf.Rad2Deg;
            Vector3 ce = hl.Center * Mathf.Rad2Deg;

            return $",false," +
                   $"{Fl(mn.x)},{Fl(mn.y)},{Fl(mn.z)}," +
                   $"{Fl(mx.x)},{Fl(mx.y)},{Fl(mx.z)}," +
                   $"{Fl(ce.x)},{Fl(ce.y)},{Fl(ce.z)},{Fl(hl.AxisLength)}";
        }

        // ※#5b（案A）: ReadHumanoidCsv は撤去。humanoid.export.csv は書き出し専用の
        //   派生エクスポート（AvatarBuilder 向け）で、canonical 読込は per-bone（bone.csv）。

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
        // vrmexpressions.csv（VRM 表情の付帯データ：独立CSV）
        //   morphgroups.csv は位置固定列のため、件数が変わるバインドは
        //   列を伸ばさずこちらへ縦持ちで格納する（materialprops.csv と同じ考え方）。
        //   規約は VrmExpressionData.cs 冒頭を正典とする。
        //   行形式:
        //     vrmExpr,<exprIndex>,<isBinary>,<overrideBlink>,<overrideLookAt>,<overrideMouth>
        //     vrmColor,<exprIndex>,<materialName>,<bindType>,<r>,<g>,<b>,<a>
        //     vrmUV,<exprIndex>,<materialName>,<scaleX>,<scaleY>,<offsetX>,<offsetY>
        //     exprIndex … morphgroups.csv の並び（＝ MorphExpressions の索引）
        //   ファイルが無い＝VRM 固有の値なし（MorphExpression.Vrm は null のまま）。
        //   読みは morphgroups.csv の後に行う。
        // ================================================================

        private static void WriteVrmExpressionsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "vrmexpressions.csv");

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_VrmExpressions,version,1.0");

            int written = 0;
            var list = model.MorphExpressions;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var v = list[i]?.Vrm;
                if (v == null) continue;

                sb.AppendLine(
                    $"vrmExpr,{i},{(v.IsBinary ? 1 : 0)},{(int)v.OverrideBlink}," +
                    $"{(int)v.OverrideLookAt},{(int)v.OverrideMouth}");

                if (v.MaterialColorBinds != null)
                {
                    foreach (var b in v.MaterialColorBinds)
                    {
                        if (b == null) continue;
                        var t = b.TargetValue;
                        sb.AppendLine(
                            $"vrmColor,{i},{Esc(b.MaterialName ?? "")},{(int)b.BindType}," +
                            $"{Fl(t.x)},{Fl(t.y)},{Fl(t.z)},{Fl(t.w)}");
                    }
                }

                if (v.TextureTransformBinds != null)
                {
                    foreach (var b in v.TextureTransformBinds)
                    {
                        if (b == null) continue;
                        sb.AppendLine(
                            $"vrmUV,{i},{Esc(b.MaterialName ?? "")}," +
                            $"{Fl(b.Scaling.x)},{Fl(b.Scaling.y)},{Fl(b.Offset.x)},{Fl(b.Offset.y)}");
                    }
                }

                written++;
            }

            // 1 件も無ければファイルを置かない。前回の保存で残った古いファイルは消す
            // （残すと、VRM 固有の値を消したつもりでも読み込みで復活する）。
            if (written == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void ReadVrmExpressionsCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, "vrmexpressions.csv");
            if (!File.Exists(path)) return;

            var list = model.MorphExpressions;
            if (list == null || list.Count == 0) return;

            VrmExpressionData Get(int index)
            {
                if (index < 0 || index >= list.Count || list[index] == null) return null;
                if (list[index].Vrm == null) list[index].Vrm = new VrmExpressionData();
                return list[index].Vrm;
            }

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var cols = Split(line);
                if (cols.Length < 2) continue;

                var v = Get(PInt(cols, 1, -1));
                if (v == null) continue;

                switch (cols[0])
                {
                    case "vrmExpr":
                        v.IsBinary       = PInt(cols, 2) != 0;
                        v.OverrideBlink  = (VrmExpressionOverride)PInt(cols, 3);
                        v.OverrideLookAt = (VrmExpressionOverride)PInt(cols, 4);
                        v.OverrideMouth  = (VrmExpressionOverride)PInt(cols, 5);
                        break;

                    case "vrmColor":
                        v.MaterialColorBinds.Add(new VrmMaterialColorBind
                        {
                            MaterialName = Unesc(SafeGet(cols, 2)),
                            BindType     = (VrmMaterialColorType)PInt(cols, 3),
                            TargetValue  = new Vector4(
                                PFl(cols, 4), PFl(cols, 5), PFl(cols, 6), PFl(cols, 7)),
                        });
                        break;

                    case "vrmUV":
                        v.TextureTransformBinds.Add(new VrmTextureTransformBind
                        {
                            MaterialName = Unesc(SafeGet(cols, 2)),
                            Scaling      = new Vector2(PFl(cols, 3, 1f), PFl(cols, 4, 1f)),
                            Offset       = new Vector2(PFl(cols, 5), PFl(cols, 6)),
                        });
                        break;
                }
            }
        }
    }
}
