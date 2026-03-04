// PMXFaceMetaReader.cs
// .plmface.csv からフェースメタ情報を読み込み、MeshContextに適用する。
// MESH行が見つからない・途中で途切れていても、読めた範囲だけ適用する。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>1面分のメタ情報（三角形展開後）</summary>
    public class FaceMetaEntry
    {
        public int FaceId;
        /// <summary>コーナーごとの (localVertexIndex, vertexId, uvSubIndex)</summary>
        public (int localIdx, int vertId, int uvSub)[] Corners = new (int, int, int)[3];
    }

    public static class PMXFaceMetaReader
    {
        /// <summary>
        /// PMXファイルと同ディレクトリの .plmface.csv を読み込む。
        /// ファイルが存在しなければ null を返す。
        /// </summary>
        /// <returns>meshName → FaceMetaEntry リスト のマップ。ファイルなければ null。</returns>
        public static Dictionary<string, List<FaceMetaEntry>> Load(string pmxFilePath)
        {
            string metaPath = Path.ChangeExtension(pmxFilePath, null) + PMXFaceMetaWriter.Extension;
            if (!File.Exists(metaPath)) return null;

            var result = new Dictionary<string, List<FaceMetaEntry>>(StringComparer.Ordinal);
            string currentMesh = null;
            List<FaceMetaEntry> currentList = null;

            foreach (var rawLine in File.ReadLines(metaPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var fields = SplitCSV(line);
                if (fields.Count == 0) continue;

                string tag = fields[0];

                if (tag == "MESH")
                {
                    if (fields.Count < 2) continue;
                    currentMesh = fields[1];
                    if (!result.TryGetValue(currentMesh, out currentList))
                    {
                        currentList = new List<FaceMetaEntry>();
                        result[currentMesh] = currentList;
                    }
                }
                else if (tag == "FACE" && currentList != null)
                {
                    // FACE,face_id, v0idx,v0id,v0uv, v1idx,v1id,v1uv, v2idx,v2id,v2uv
                    if (fields.Count < 11) continue;
                    if (!int.TryParse(fields[1], out int faceId)) continue;

                    var entry = new FaceMetaEntry { FaceId = faceId };
                    bool ok = true;
                    for (int c = 0; c < 3; c++)
                    {
                        int fi = 2 + c * 3;
                        if (!int.TryParse(fields[fi],     out int localIdx) ||
                            !int.TryParse(fields[fi + 1], out int vertId)   ||
                            !int.TryParse(fields[fi + 2], out int uvSub))
                        {
                            ok = false; break;
                        }
                        entry.Corners[c] = (localIdx, vertId, uvSub);
                    }
                    if (ok) currentList.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// フェースメタをMeshContextリストに適用する。
        /// vertexId 一致が取れた頂点に Face.Id と UVIndices を設定する。
        /// </summary>
        public static void Apply(
            Dictionary<string, List<FaceMetaEntry>> meta,
            IList<MeshContext> meshContexts)
        {
            if (meta == null) return;

            foreach (var ctx in meshContexts)
            {
                if (ctx?.MeshObject == null) continue;
                if (!meta.TryGetValue(ctx.Name ?? "", out var entries)) continue;

                var mo = ctx.MeshObject;

                // vertexId → ローカルインデックス の逆引きマップ
                var idToLocal = new Dictionary<int, int>(mo.Vertices.Count);
                for (int i = 0; i < mo.Vertices.Count; i++)
                {
                    int id = mo.Vertices[i].Id;
                    if (id != 0 && !idToLocal.ContainsKey(id))
                        idToLocal[id] = i;
                }

                // faceId → Face の逆引きマップ
                var idToFace = new Dictionary<int, Face>(mo.Faces.Count);
                foreach (var face in mo.Faces)
                {
                    if (face.Id != 0 && !idToFace.ContainsKey(face.Id))
                        idToFace[face.Id] = face;
                }

                foreach (var entry in entries)
                {
                    // Face.Id が一致する面を探す（なければスキップ）
                    if (!idToFace.TryGetValue(entry.FaceId, out var face)) continue;

                    for (int c = 0; c < 3; c++)
                    {
                        var (localIdx, vertId, uvSub) = entry.Corners[c];

                        // vertexId で実際のローカルインデックスを解決
                        int resolvedLocal = localIdx; // fallback
                        if (vertId != 0 && idToLocal.TryGetValue(vertId, out int idLocal))
                            resolvedLocal = idLocal;

                        // Face.VertexIndices のコーナーを特定して UVIndices を設定
                        for (int vi = 0; vi < face.VertexIndices.Count; vi++)
                        {
                            if (face.VertexIndices[vi] == resolvedLocal)
                            {
                                while (face.UVIndices.Count <= vi)
                                    face.UVIndices.Add(0);
                                face.UVIndices[vi] = uvSub;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // ================================================================
        // CSV パース（簡易、クォート対応）
        // ================================================================

        private static List<string> SplitCSV(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuote = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
