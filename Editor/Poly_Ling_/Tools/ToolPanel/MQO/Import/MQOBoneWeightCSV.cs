// Assets/Editor/Poly_Ling/MQO/Import/MQOBoneWeightCSV.cs
// MQOインポート用ボーンウェイトCSVパーサー

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// CSVの1行分のボーンウェイトデータ
    /// </summary>
    public class BoneWeightEntry
    {
        public string MqoObjectName;
        public int VertexID;
        public int VertexIndex;
        public string[] BoneNames = new string[4];
        public float[] Weights = new float[4];
    }

    /// <summary>
    /// MQOオブジェクト単位のボーンウェイトデータ
    /// </summary>
    public class MQOObjectBoneWeights
    {
        public string ObjectName;
        public List<BoneWeightEntry> Entries = new List<BoneWeightEntry>();
        
        /// <summary>すべてのVertexIDが0以上か（IDベースでマッチング可能か）</summary>
        public bool AllVertexIDsValid => Entries.All(e => e.VertexID >= 0);
        
        /// <summary>VertexIDからエントリを検索</summary>
        public BoneWeightEntry FindByVertexID(int vertexId)
        {
            return Entries.FirstOrDefault(e => e.VertexID == vertexId);
        }
        
        /// <summary>VertexIndexからエントリを検索</summary>
        public BoneWeightEntry FindByVertexIndex(int vertexIndex)
        {
            return Entries.FirstOrDefault(e => e.VertexIndex == vertexIndex);
        }
    }

    /// <summary>
    /// ボーンウェイトCSV全体のデータ
    /// </summary>
    public class BoneWeightCSVData
    {
        public Dictionary<string, MQOObjectBoneWeights> ObjectWeights = new Dictionary<string, MQOObjectBoneWeights>();
        public HashSet<string> AllBoneNames = new HashSet<string>();
        
        /// <summary>MQOオブジェクト名からボーンウェイトデータを取得</summary>
        public MQOObjectBoneWeights GetObjectWeights(string objectName)
        {
            ObjectWeights.TryGetValue(objectName, out var weights);
            return weights;
        }
    }

    /// <summary>
    /// ボーンウェイトCSVパーサー
    /// </summary>
    public static class MQOBoneWeightCSVParser
    {
        /// <summary>
        /// CSVファイルをパース
        /// </summary>
        public static BoneWeightCSVData ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[MQOBoneWeightCSV] File not found: {filePath}");
                return null;
            }

            string content = File.ReadAllText(filePath);
            return Parse(content);
        }

        /// <summary>
        /// CSV文字列をパース
        /// </summary>
        public static BoneWeightCSVData Parse(string content)
        {
            var result = new BoneWeightCSVData();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2)
            {
                Debug.LogWarning("[MQOBoneWeightCSV] CSV has no data rows");
                return result;
            }

            Debug.Log($"[MQOBoneWeightCSV] Parsing {lines.Length - 1} data rows...");

            // ヘッダー確認（スキップ）
            // MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3
            
            int parseErrors = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var entry = ParseLine(lines[i]);
                if (entry == null)
                {
                    parseErrors++;
                    continue;
                }

                // オブジェクト別に分類
                if (!result.ObjectWeights.TryGetValue(entry.MqoObjectName, out var objectWeights))
                {
                    objectWeights = new MQOObjectBoneWeights { ObjectName = entry.MqoObjectName };
                    result.ObjectWeights[entry.MqoObjectName] = objectWeights;
                }
                objectWeights.Entries.Add(entry);

                // ボーン名を収集
                foreach (var boneName in entry.BoneNames)
                {
                    if (!string.IsNullOrEmpty(boneName))
                    {
                        result.AllBoneNames.Add(boneName);
                    }
                }
            }

            Debug.Log($"[MQOBoneWeightCSV] Parsed: {result.ObjectWeights.Count} objects, {result.AllBoneNames.Count} bones");

            return result;
        }

        private static BoneWeightEntry ParseLine(string line)
        {
            var parts = line.Split(',');
            if (parts.Length < 11)
            {
                return null;
            }

            try
            {
                var entry = new BoneWeightEntry
                {
                    MqoObjectName = parts[0].Trim(),
                    VertexID = int.TryParse(parts[1].Trim(), out int vid) ? vid : -1,
                    VertexIndex = int.TryParse(parts[2].Trim(), out int vidx) ? vidx : -1,
                    BoneNames = new string[]
                    {
                        parts[3].Trim(),
                        parts[4].Trim(),
                        parts[5].Trim(),
                        parts[6].Trim()
                    },
                    Weights = new float[]
                    {
                        float.TryParse(parts[7].Trim(), out float w0) ? w0 : 0f,
                        float.TryParse(parts[8].Trim(), out float w1) ? w1 : 0f,
                        float.TryParse(parts[9].Trim(), out float w2) ? w2 : 0f,
                        float.TryParse(parts[10].Trim(), out float w3) ? w3 : 0f
                    }
                };

                return entry;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQOBoneWeightCSV] Failed to parse line: {line}\n{ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// ボーンウェイト適用ユーティリティ
    /// </summary>
    public static class MQOBoneWeightApplier
    {
        /// <summary>
        /// MeshObjectにボーンウェイトを適用
        /// </summary>
        /// <param name="meshObject">対象のMeshObject</param>
        /// <param name="objectWeights">ボーンウェイトデータ</param>
        /// <param name="boneNameToIndex">ボーン名→インデックスのマッピング</param>
        /// <returns>適用された頂点数</returns>
        /// <summary>
        /// メッシュオブジェクトにボーンウェイトを適用
        /// </summary>
        /// <param name="meshObject">対象のメッシュオブジェクト</param>
        /// <param name="objectWeights">CSVからパースしたウェイトデータ</param>
        /// <param name="boneNameToIndex">ボーン名→インデックスのマッピング</param>
        /// <returns>適用された頂点数</returns>
        public static int ApplyBoneWeights(
            Data.MeshObject meshObject,
            MQOObjectBoneWeights objectWeights,
            Dictionary<string, int> boneNameToIndex)
        {
            if (meshObject == null || objectWeights == null || objectWeights.Entries.Count == 0)
                return 0;

            int appliedCount = 0;
            int skippedCount = 0;
            int unmatchedBoneCount = 0;
            bool useVertexID = objectWeights.AllVertexIDsValid;

            Debug.Log($"[MQOBoneWeight] === Applying to '{meshObject.Name}' ===");
            Debug.Log($"[MQOBoneWeight]   Mode: {(useVertexID ? "VertexID" : "VertexIndex")}, CSV: {objectWeights.Entries.Count}, Mesh: {meshObject.Vertices.Count}");

            for (int i = 0; i < meshObject.Vertices.Count; i++)
            {
                var vertex = meshObject.Vertices[i];
                
                // マッチングするエントリを検索
                BoneWeightEntry entry;
                if (useVertexID)
                {
                    // VertexIDでマッチング
                    entry = objectWeights.FindByVertexID(vertex.Id);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }
                else
                {
                    // VertexIndexでマッチング
                    entry = objectWeights.FindByVertexIndex(i);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }

                // BoneWeightを構築
                var boneWeight = new BoneWeight();
                
                // Bone0
                if (!string.IsNullOrEmpty(entry.BoneNames[0]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[0], out int idx0))
                    {
                        boneWeight.boneIndex0 = idx0;
                        boneWeight.weight0 = entry.Weights[0];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone1
                if (!string.IsNullOrEmpty(entry.BoneNames[1]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[1], out int idx1))
                    {
                        boneWeight.boneIndex1 = idx1;
                        boneWeight.weight1 = entry.Weights[1];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone2
                if (!string.IsNullOrEmpty(entry.BoneNames[2]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[2], out int idx2))
                    {
                        boneWeight.boneIndex2 = idx2;
                        boneWeight.weight2 = entry.Weights[2];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone3
                if (!string.IsNullOrEmpty(entry.BoneNames[3]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[3], out int idx3))
                    {
                        boneWeight.boneIndex3 = idx3;
                        boneWeight.weight3 = entry.Weights[3];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }

                vertex.BoneWeight = boneWeight;
                appliedCount++;
            }

            Debug.Log($"[MQOBoneWeight]   Result: Applied={appliedCount}, Skipped={skippedCount}, UnmatchedBones={unmatchedBoneCount}");
            
            if (skippedCount > 0)
            {
                Debug.LogWarning($"[MQOBoneWeight] '{meshObject.Name}': {skippedCount} vertices had no matching CSV entry");
            }
            if (unmatchedBoneCount > 0)
            {
                Debug.LogWarning($"[MQOBoneWeight] '{meshObject.Name}': {unmatchedBoneCount} bone name lookups failed");
            }

            return appliedCount;
        }

        /// <summary>
        /// MeshObjectにミラー側ボーンウェイトを適用
        /// </summary>
        /// <param name="meshObject">対象のMeshObject</param>
        /// <param name="objectWeights">ミラー側ボーンウェイトデータ（オブジェクト名+"+"）</param>
        /// <param name="boneNameToIndex">ボーン名→インデックスのマッピング</param>
        /// <returns>適用された頂点数</returns>
        public static int ApplyMirrorBoneWeights(
            Data.MeshObject meshObject,
            MQOObjectBoneWeights objectWeights,
            Dictionary<string, int> boneNameToIndex)
        {
            if (meshObject == null || objectWeights == null || objectWeights.Entries.Count == 0)
                return 0;

            int appliedCount = 0;
            int skippedCount = 0;
            int unmatchedBoneCount = 0;
            bool useVertexID = objectWeights.AllVertexIDsValid;

            Debug.Log($"[MQOBoneWeight] === Applying Mirror to '{meshObject.Name}' ===");
            Debug.Log($"[MQOBoneWeight]   Mode: {(useVertexID ? "VertexID" : "VertexIndex")}, CSV: {objectWeights.Entries.Count}, Mesh: {meshObject.Vertices.Count}");

            for (int i = 0; i < meshObject.Vertices.Count; i++)
            {
                var vertex = meshObject.Vertices[i];
                
                // マッチングするエントリを検索
                BoneWeightEntry entry;
                if (useVertexID)
                {
                    entry = objectWeights.FindByVertexID(vertex.Id);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }
                else
                {
                    entry = objectWeights.FindByVertexIndex(i);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }

                // MirrorBoneWeightを構築
                var boneWeight = new BoneWeight();
                
                // Bone0
                if (!string.IsNullOrEmpty(entry.BoneNames[0]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[0], out int idx0))
                    {
                        boneWeight.boneIndex0 = idx0;
                        boneWeight.weight0 = entry.Weights[0];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone1
                if (!string.IsNullOrEmpty(entry.BoneNames[1]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[1], out int idx1))
                    {
                        boneWeight.boneIndex1 = idx1;
                        boneWeight.weight1 = entry.Weights[1];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone2
                if (!string.IsNullOrEmpty(entry.BoneNames[2]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[2], out int idx2))
                    {
                        boneWeight.boneIndex2 = idx2;
                        boneWeight.weight2 = entry.Weights[2];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }
                
                // Bone3
                if (!string.IsNullOrEmpty(entry.BoneNames[3]))
                {
                    if (boneNameToIndex.TryGetValue(entry.BoneNames[3], out int idx3))
                    {
                        boneWeight.boneIndex3 = idx3;
                        boneWeight.weight3 = entry.Weights[3];
                    }
                    else
                    {
                        unmatchedBoneCount++;
                    }
                }

                vertex.MirrorBoneWeight = boneWeight;
                appliedCount++;
            }

            Debug.Log($"[MQOBoneWeight]   Mirror Result: Applied={appliedCount}, Skipped={skippedCount}, UnmatchedBones={unmatchedBoneCount}");
            
            if (skippedCount > 0)
            {
                Debug.LogWarning($"[MQOBoneWeight] Mirror '{meshObject.Name}': {skippedCount} vertices had no matching CSV entry");
            }
            if (unmatchedBoneCount > 0)
            {
                Debug.LogWarning($"[MQOBoneWeight] Mirror '{meshObject.Name}': {unmatchedBoneCount} bone name lookups failed");
            }

            return appliedCount;
        }

        /// <summary>
        /// ボーン名リストからボーン名→インデックスマッピングを作成
        /// </summary>
        public static Dictionary<string, int> CreateBoneNameToIndexMap(IEnumerable<string> boneNames)
        {
            var map = new Dictionary<string, int>();
            int index = 0;
            foreach (var name in boneNames)
            {
                if (!map.ContainsKey(name))
                {
                    map[name] = index++;
                }
            }
            
            Debug.Log($"[MQOBoneWeight] Created bone name mapping: {map.Count} bones");
            
            return map;
        }
    }
}
