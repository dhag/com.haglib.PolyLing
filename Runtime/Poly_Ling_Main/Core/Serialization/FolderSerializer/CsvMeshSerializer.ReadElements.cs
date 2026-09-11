// CsvMeshSerializer.ReadElements.cs
// CSV メッシュ入出力：頂点・面・選択セット・除外セットの読み込み、CSV ユーティリティ、名前ベース参照の解決。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{
    public static partial class CsvMeshSerializer
    {
        // ================================================================
        // Read: 頂点
        // ================================================================

        private static Vertex ReadVertex(string[] cols)
        {
            // v,id,px,py,pz,flags,bwCount,[bw...],mbwCount,[mbw...],uvCount,[uv...],nrmCount,[nrm...],subId,partsId
            int idx = 1;
            int id = ParseInt(cols, idx++);
            float px = ParseFloat(cols, idx++);
            float py = ParseFloat(cols, idx++);
            float pz = ParseFloat(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var vertex = new Vertex(id, new Vector3(px, py, pz));
            vertex.Flags = (VertexFlags)flags;

            // BoneWeight
            int bwCount = ParseInt(cols, idx++);
            if (bwCount == 8)
            {
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = ParseInt(cols, idx++),
                    boneIndex1 = ParseInt(cols, idx++),
                    boneIndex2 = ParseInt(cols, idx++),
                    boneIndex3 = ParseInt(cols, idx++),
                    weight0 = ParseFloat(cols, idx++),
                    weight1 = ParseFloat(cols, idx++),
                    weight2 = ParseFloat(cols, idx++),
                    weight3 = ParseFloat(cols, idx++)
                };
            }

            // MirrorBoneWeight
            int mbwCount = ParseInt(cols, idx++);
            if (mbwCount == 8)
            {
                vertex.MirrorBoneWeight = new BoneWeight
                {
                    boneIndex0 = ParseInt(cols, idx++),
                    boneIndex1 = ParseInt(cols, idx++),
                    boneIndex2 = ParseInt(cols, idx++),
                    boneIndex3 = ParseInt(cols, idx++),
                    weight0 = ParseFloat(cols, idx++),
                    weight1 = ParseFloat(cols, idx++),
                    weight2 = ParseFloat(cols, idx++),
                    weight3 = ParseFloat(cols, idx++)
                };
            }

            // UVs
            int uvCount = ParseInt(cols, idx++);
            for (int u = 0; u < uvCount; u++)
            {
                float ux = ParseFloat(cols, idx++);
                float uy = ParseFloat(cols, idx++);
                vertex.UVs.Add(new Vector2(ux, uy));
            }

            // Normals
            int nrmCount = ParseInt(cols, idx++);
            for (int n = 0; n < nrmCount; n++)
            {
                float nx = ParseFloat(cols, idx++);
                float ny = ParseFloat(cols, idx++);
                float nz = ParseFloat(cols, idx++);
                vertex.Normals.Add(new Vector3(nx, ny, nz));
            }

            // SubId / PartsId。この 2 列を持たない旧ファイルでは
            // ParseInt が範囲外で 0 を返すため、未設定として復元される。
            vertex.SubId   = ParseInt(cols, idx++);
            vertex.PartsId = ParseInt(cols, idx++);

            return vertex;
        }

        // ================================================================
        // Read: 頂点のオプション位置リスト（制御点位置など）
        //
        // 直前に読んだ頂点（v / vn いずれでも可）へ付与する。
        // 頂点が1つも無い位置に現れた行は無視する。
        // ================================================================

        private static void ReadVertexControlPoints(string[] cols, MeshObject meshObject)
        {
            // vcp,count,x,y,z,...
            if (meshObject == null || meshObject.Vertices.Count == 0) return;

            int count = ParseInt(cols, 1);
            if (count <= 0) return;

            var vertex = meshObject.Vertices[meshObject.Vertices.Count - 1];
            var list = vertex.EnsureControlPoints();

            int idx = 2;
            for (int i = 0; i < count; i++)
            {
                float x = ParseFloat(cols, idx++);
                float y = ParseFloat(cols, idx++);
                float z = ParseFloat(cols, idx++);
                list.Add(new Vector3(x, y, z));
            }
        }

        // ================================================================
        // Read: 頂点（名前ベース）
        // ================================================================

        /// <summary>
        /// 名前ベース頂点行を読み込み。ボーン名は一時的にstring[]で返し、後でインデックス解決する
        /// BoneWeightにはダミー値(0)を設定しておく
        /// </summary>
        private static (Vertex vertex, string[] boneNames, string[] mirrorBoneNames) ReadVertexNameBased(string[] cols)
        {
            // vn,id,px,py,pz,flags,bwCount,[boneName0..3,w0..3],mbwCount,[...],uvCount,[uv...],nrmCount,[nrm...],subId,partsId
            int idx = 1;
            int id = ParseInt(cols, idx++);
            float px = ParseFloat(cols, idx++);
            float py = ParseFloat(cols, idx++);
            float pz = ParseFloat(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var vertex = new Vertex(id, new Vector3(px, py, pz));
            vertex.Flags = (VertexFlags)flags;

            string[] boneNames = null;
            string[] mirrorBoneNames = null;

            // BoneWeight (名前ベース)
            int bwCount = ParseInt(cols, idx++);
            if (bwCount == 8)
            {
                boneNames = new string[4];
                boneNames[0] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[1] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[2] = UnescapeCsv(SafeGet(cols, idx++));
                boneNames[3] = UnescapeCsv(SafeGet(cols, idx++));
                float w0 = ParseFloat(cols, idx++);
                float w1 = ParseFloat(cols, idx++);
                float w2 = ParseFloat(cols, idx++);
                float w3 = ParseFloat(cols, idx++);
                // ダミーインデックスで仮設定（後で名前解決で上書き）
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = 0, boneIndex1 = 0, boneIndex2 = 0, boneIndex3 = 0,
                    weight0 = w0, weight1 = w1, weight2 = w2, weight3 = w3
                };
            }

            // MirrorBoneWeight (名前ベース)
            int mbwCount = ParseInt(cols, idx++);
            if (mbwCount == 8)
            {
                mirrorBoneNames = new string[4];
                mirrorBoneNames[0] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[1] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[2] = UnescapeCsv(SafeGet(cols, idx++));
                mirrorBoneNames[3] = UnescapeCsv(SafeGet(cols, idx++));
                float w0 = ParseFloat(cols, idx++);
                float w1 = ParseFloat(cols, idx++);
                float w2 = ParseFloat(cols, idx++);
                float w3 = ParseFloat(cols, idx++);
                vertex.MirrorBoneWeight = new BoneWeight
                {
                    boneIndex0 = 0, boneIndex1 = 0, boneIndex2 = 0, boneIndex3 = 0,
                    weight0 = w0, weight1 = w1, weight2 = w2, weight3 = w3
                };
            }

            // UVs
            int uvCount = ParseInt(cols, idx++);
            for (int u = 0; u < uvCount; u++)
            {
                float ux = ParseFloat(cols, idx++);
                float uy = ParseFloat(cols, idx++);
                vertex.UVs.Add(new Vector2(ux, uy));
            }

            // Normals
            int nrmCount = ParseInt(cols, idx++);
            for (int n = 0; n < nrmCount; n++)
            {
                float nx = ParseFloat(cols, idx++);
                float ny = ParseFloat(cols, idx++);
                float nz = ParseFloat(cols, idx++);
                vertex.Normals.Add(new Vector3(nx, ny, nz));
            }

            // SubId / PartsId。この 2 列を持たない旧ファイルでは
            // ParseInt が範囲外で 0 を返すため、未設定として復元される。
            vertex.SubId   = ParseInt(cols, idx++);
            vertex.PartsId = ParseInt(cols, idx++);

            return (vertex, boneNames, mirrorBoneNames);
        }

        private static string SafeGet(string[] cols, int idx) => idx < cols.Length ? cols[idx] : "";

        // ================================================================
        // Read: 面
        // ================================================================

        private static Face ReadFace(string[] cols)
        {
            // f,id,materialIndex,flags,vertCount,vi...,uviCount,uvi...,niCount,ni...
            int idx = 1;
            int id = ParseInt(cols, idx++);
            int matIdx = ParseInt(cols, idx++);
            byte flags = (byte)ParseInt(cols, idx++);

            var face = new Face
            {
                Id = id,
                MaterialIndex = matIdx,
                Flags = (FaceFlags)flags
            };

            // Vertex indices
            int vCount = ParseInt(cols, idx++);
            face.VertexIndices = new List<int>(vCount);
            for (int v = 0; v < vCount; v++)
                face.VertexIndices.Add(ParseInt(cols, idx++));

            // UV indices
            int uvCount = ParseInt(cols, idx++);
            face.UVIndices = new List<int>(uvCount);
            for (int u = 0; u < uvCount; u++)
                face.UVIndices.Add(ParseInt(cols, idx++));

            // Normal indices
            int niCount = ParseInt(cols, idx++);
            face.NormalIndices = new List<int>(niCount);
            for (int n = 0; n < niCount; n++)
                face.NormalIndices.Add(ParseInt(cols, idx++));

            return face;
        }

        // ================================================================
        // Read: 選択セット
        // ================================================================

        private static void ReadSelectionSet(string[] cols, MeshContext mc)
        {
            // ss,name,mode,vCount,v...,eCount,e1,e2,...,fCount,f...,lCount,l...
            if (mc.PartsSelectionSetList == null) mc.PartsSelectionSetList = new List<PartsSelectionSet>();

            int idx = 1;
            string name = UnescapeCsv(cols.Length > idx ? cols[idx] : "Set"); idx++;
            string modeStr = cols.Length > idx ? cols[idx] : "Vertex"; idx++;

            var ss = new PartsSelectionSet(name);
            if (Enum.TryParse<MeshSelectMode>(modeStr, out var mode))
                ss.Mode = mode;

            // Vertices
            int vCount = ParseInt(cols, idx++);
            for (int v = 0; v < vCount; v++)
                ss.Vertices.Add(ParseInt(cols, idx++));

            // Edges
            int eCount = ParseInt(cols, idx++);
            for (int e = 0; e < eCount; e++)
            {
                int v1 = ParseInt(cols, idx++);
                int v2 = ParseInt(cols, idx++);
                ss.Edges.Add(new VertexPair(v1, v2));
            }

            // Faces
            int fCount = ParseInt(cols, idx++);
            for (int f = 0; f < fCount; f++)
                ss.Faces.Add(ParseInt(cols, idx++));

            // Lines
            int lCount = ParseInt(cols, idx++);
            for (int l = 0; l < lCount; l++)
                ss.Lines.Add(ParseInt(cols, idx++));

            // 識別子の控え（行末）。列が無ければ ParseInt が 0 を返し、控え無しになる。
            int idCount = ParseInt(cols, idx++);
            for (int k = 0; k < idCount; k++)
            {
                int vi    = ParseInt(cols, idx++);
                int id    = ParseInt(cols, idx++);
                int parts = ParseInt(cols, idx++);
                int sub   = ParseInt(cols, idx++);
                ss.VertexIds[vi] = new VertexIdTriple(id, parts, sub);
            }

            mc.PartsSelectionSetList.Add(ss);
        }

        // ================================================================
        // Read: 法線再計算 除外セット
        // ================================================================

        private static void ReadNormalExcludeSet(string[] cols, MeshObject meshObject)
        {
            // nx,name,mode,vCount,v...,eCount,e1,e2,...,fCount,f...,lCount,l...
            if (meshObject == null) return;
            if (meshObject.NormalRecalcExcludeList == null)
                meshObject.NormalRecalcExcludeList = new List<PartsSelectionSet>();

            int idx = 1;
            string name = UnescapeCsv(cols.Length > idx ? cols[idx] : "Set"); idx++;
            string modeStr = cols.Length > idx ? cols[idx] : "Vertex"; idx++;

            var ss = new PartsSelectionSet(name);
            if (Enum.TryParse<MeshSelectMode>(modeStr, out var mode))
                ss.Mode = mode;

            int vCount = ParseInt(cols, idx++);
            for (int v = 0; v < vCount; v++)
                ss.Vertices.Add(ParseInt(cols, idx++));

            int eCount = ParseInt(cols, idx++);
            for (int e = 0; e < eCount; e++)
            {
                int v1 = ParseInt(cols, idx++);
                int v2 = ParseInt(cols, idx++);
                ss.Edges.Add(new VertexPair(v1, v2));
            }

            int fCount = ParseInt(cols, idx++);
            for (int f = 0; f < fCount; f++)
                ss.Faces.Add(ParseInt(cols, idx++));

            int lCount = ParseInt(cols, idx++);
            for (int l = 0; l < lCount; l++)
                ss.Lines.Add(ParseInt(cols, idx++));

            // 識別子の控え（行末）。ss 行と同じ並び。
            int idCount = ParseInt(cols, idx++);
            for (int k = 0; k < idCount; k++)
            {
                int vi    = ParseInt(cols, idx++);
                int id    = ParseInt(cols, idx++);
                int parts = ParseInt(cols, idx++);
                int sub   = ParseInt(cols, idx++);
                ss.VertexIds[vi] = new VertexIdTriple(id, parts, sub);
            }

            meshObject.NormalRecalcExcludeList.Add(ss);
        }

        // ================================================================
        // CSV ユーティリティ
        // ================================================================

        /// <summary>float → 文字列 (InvariantCulture)</summary>
        private static string F(float v)
        {
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>CSV値のエスケープ（カンマ・改行を含む場合ダブルクォート囲み）</summary>
        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        /// <summary>CSVエスケープ解除</summary>
        private static string UnescapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\"\"", "\"");
            }
            return s;
        }

        /// <summary>CSV行をカンマ分割（ダブルクォート対応）</summary>
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    // クォート内
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(line[i]);
                            i++;
                        }
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

        private static int ParseInt(string[] cols, int idx, int def = 0)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return int.TryParse(cols[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static float ParseFloat(string[] cols, int idx, float def = 0f)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return float.TryParse(cols[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool ParseBool(string[] cols, int idx, bool def = false)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            var s = cols[idx].Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "True") return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "False") return false;
            return def;
        }

        // ================================================================
        // 名前ベース参照の解決
        // ================================================================

        /// <summary>
        /// 名前ベースで読み込んだエントリの参照をインデックスに解決する
        /// </summary>
        /// <param name="entries">読み込んだエントリ一覧</param>
        /// <param name="nameToIndex">名前→インデックスの辞書（呼び出し元で構築）</param>
        public static void ResolveNameReferences(List<CsvMeshEntry> entries, Dictionary<string, int> nameToIndex)
        {
            if (entries == null || nameToIndex == null) return;

            foreach (var entry in entries)
            {
                if (!entry.IsNameBased) continue;
                var mc = entry.MeshContext;
                if (mc == null) continue;

                // ParentIndex
                if (!string.IsNullOrEmpty(entry.ParentName))
                    mc.ParentIndex = LookupIndex(entry.ParentName, nameToIndex);

                // HierarchyParentIndex
                if (!string.IsNullOrEmpty(entry.HierarchyParentName))
                    mc.HierarchyParentIndex = LookupIndex(entry.HierarchyParentName, nameToIndex);

                // BakedMirrorSourceIndex
                if (!string.IsNullOrEmpty(entry.BakedMirrorSourceName))
                    mc.BakedMirrorSourceIndex = LookupIndex(entry.BakedMirrorSourceName, nameToIndex);

                // MorphParentIndex
                if (!string.IsNullOrEmpty(entry.MorphParentName))
                    mc.MorphParentIndex = LookupIndex(entry.MorphParentName, nameToIndex);

                // IK は per-bone 形式（ikRoot/ikLinkBone）で読み込み、
                // 集約 Links / TargetIndex は CsvModelSerializer.Deserialize 末尾の
                // IKChainResolver.RebuildLinksFromPerBone で再構築する（ここでは解決しない）。

                // 頂点 BoneWeight
                if (entry.VertexBoneNames != null)
                {
                    var vertices = mc.MeshObject?.Vertices;
                    if (vertices != null)
                    {
                        for (int vi = 0; vi < entry.VertexBoneNames.Count && vi < vertices.Count; vi++)
                        {
                            var names = entry.VertexBoneNames[vi];
                            if (names == null || !vertices[vi].BoneWeight.HasValue) continue;
                            var bw = vertices[vi].BoneWeight.Value;
                            bw.boneIndex0 = LookupIndex(names[0], nameToIndex);
                            bw.boneIndex1 = LookupIndex(names[1], nameToIndex);
                            bw.boneIndex2 = LookupIndex(names[2], nameToIndex);
                            bw.boneIndex3 = LookupIndex(names[3], nameToIndex);
                            vertices[vi].BoneWeight = bw;
                        }
                    }
                }

                // 頂点 MirrorBoneWeight
                if (entry.VertexMirrorBoneNames != null)
                {
                    var vertices = mc.MeshObject?.Vertices;
                    if (vertices != null)
                    {
                        for (int vi = 0; vi < entry.VertexMirrorBoneNames.Count && vi < vertices.Count; vi++)
                        {
                            var names = entry.VertexMirrorBoneNames[vi];
                            if (names == null || !vertices[vi].MirrorBoneWeight.HasValue) continue;
                            var mbw = vertices[vi].MirrorBoneWeight.Value;
                            mbw.boneIndex0 = LookupIndex(names[0], nameToIndex);
                            mbw.boneIndex1 = LookupIndex(names[1], nameToIndex);
                            mbw.boneIndex2 = LookupIndex(names[2], nameToIndex);
                            mbw.boneIndex3 = LookupIndex(names[3], nameToIndex);
                            vertices[vi].MirrorBoneWeight = mbw;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 名前からインデックスを検索。見つからない場合は-1
        /// </summary>
        private static int LookupIndex(string name, Dictionary<string, int> nameToIndex)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            return nameToIndex.TryGetValue(name, out int idx) ? idx : -1;
        }
    }
}
