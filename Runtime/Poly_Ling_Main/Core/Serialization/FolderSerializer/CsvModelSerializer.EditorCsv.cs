// CsvModelSerializer.EditorCsv.cs
// CSV モデル入出力：editorstate・workplane・workaxis・メッシュエントリの読み込み・TPoseBackup。
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

            // 座標規約（pmx* / mqo*）はここには書かない。coordinate.csv が正本。
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
                    // 座標規約（pmx* / mqo*）はここでは読まない。coordinate.csv が正本。
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
        // workaxis.csv（作業用ローカル軸）
        //
        // 値はすべて Unity ワールド座標系のまま書き出す（座標系変換なし）。
        // 回転はクォータニオン x,y,z,w。
        // ================================================================

        private static void WriteWorkAxisCsv(string folderPath, WorkAxisContext wa)
        {
            var o = wa.Origin;
            var r = wa.Rotation;

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_WorkAxis,version,1.0");
            sb.AppendLine($"origin,{Fl(o.x)},{Fl(o.y)},{Fl(o.z)}");
            sb.AppendLine($"rotation,{Fl(r.x)},{Fl(r.y)},{Fl(r.z)},{Fl(r.w)}");
            sb.AppendLine($"isVisible,{wa.IsVisible}");
            sb.AppendLine($"length,{Fl(wa.Length)}");

            File.WriteAllText(Path.Combine(folderPath, "workaxis.csv"), sb.ToString(), Encoding.UTF8);
        }

        private static void ReadWorkAxisCsv(string path, WorkAxisContext wa)
        {
            if (wa == null) return;
            wa.Reset();
            if (!File.Exists(path)) return;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var cols = Split(line);
                if (cols.Length < 2 || cols[0].StartsWith("#")) continue;

                switch (cols[0])
                {
                    case "origin":
                        wa.Origin = new Vector3(PFl(cols, 1), PFl(cols, 2), PFl(cols, 3));
                        break;
                    case "rotation":
                        wa.Rotation = new Quaternion(
                            PFl(cols, 1), PFl(cols, 2), PFl(cols, 3), PFl(cols, 4, 1f));
                        break;
                    case "isVisible":
                        wa.IsVisible = PBool(cols, 1, true);
                        break;
                    case "length":
                        // 行が無い旧ファイルは Reset() の既定値のまま。
                        // 0 以下は下限クランプで使い物にならない長さになるため無視する。
                        {
                            float len = PFl(cols, 1);
                            if (len > 0f) wa.Length = len;
                        }
                        break;
                }
            }
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

            // 1. 命名規約ファイル (*.mesh.csv, *.bone.csv, *.morph.csv, *.pmx_physics.csv)
            foreach (var type in new[] { "mesh", "bone", "morph", "pmx_physics" })
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
                                firstLine.StartsWith("#PolyLing_Morph") ||
                                firstLine.StartsWith("#PolyLing_PmxPhysics"))
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
    }
}
