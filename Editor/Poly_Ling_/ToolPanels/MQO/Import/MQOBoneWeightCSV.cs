// Assets/Editor/Poly_Ling/MQO/Import/MQOBoneWeightCSV.cs
// =====================================================================
// MQOインポート用ボーンウェイトCSVパーサー
// 
// 【機能】
// - ボーンウェイトCSVファイルのパース
// - MeshObjectへのボーンウェイト適用
// 
// 【CSVフォーマット】
// ヘッダー: MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3
// 
// 【列の意味】（BoneWeightCSVSchemaで定義）
// - MqoObjectName (0): MQOオブジェクト名、ミラー側は末尾に"+"を付与
// - VertexID (1): 頂点ID（オブジェクト間で同一頂点を識別）、-1で未設定
// - VertexIndex (2): 頂点インデックス（オブジェクト内の順序）
// - Bone0-3 (3-6): ボーン名（最大4本）
// - Weight0-3 (7-10): 各ボーンのウェイト値（0.0-1.0）
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.MQO.CSV;

namespace Poly_Ling.MQO
{
    // =========================================================================
    // データクラス
    // =========================================================================

    /// <summary>
    /// CSVの1行分のボーンウェイトデータ
    /// </summary>
    public class BoneWeightEntry
    {
        /// <summary>MQOオブジェクト名</summary>
        public string MqoObjectName;
        
        /// <summary>頂点ID（-1で未設定）</summary>
        public int VertexID;
        
        /// <summary>頂点インデックス</summary>
        public int VertexIndex;
        
        /// <summary>ボーン名（4本）</summary>
        public string[] BoneNames = new string[4];
        
        /// <summary>ウェイト値（4本）</summary>
        public float[] Weights = new float[4];
    }

    /// <summary>
    /// MQOオブジェクト単位のボーンウェイトデータ
    /// </summary>
    public class MQOObjectBoneWeights
    {
        /// <summary>オブジェクト名</summary>
        public string ObjectName;
        
        /// <summary>エントリリスト</summary>
        public List<BoneWeightEntry> Entries = new List<BoneWeightEntry>();
        
        /// <summary>
        /// すべてのVertexIDが0以上か（IDベースでマッチング可能か）
        /// </summary>
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
        /// <summary>オブジェクト名 → ボーンウェイトデータ</summary>
        public Dictionary<string, MQOObjectBoneWeights> ObjectWeights = new Dictionary<string, MQOObjectBoneWeights>();
        
        /// <summary>使用されているすべてのボーン名</summary>
        public HashSet<string> AllBoneNames = new HashSet<string>();
        
        /// <summary>MQOオブジェクト名からボーンウェイトデータを取得</summary>
        public MQOObjectBoneWeights GetObjectWeights(string objectName)
        {
            ObjectWeights.TryGetValue(objectName, out var weights);
            return weights;
        }
    }

    // =========================================================================
    // パーサー
    // =========================================================================

    /// <summary>
    /// ボーンウェイトCSVパーサー
    /// CSVファイルをパースしてBoneWeightCSVDataを生成
    /// </summary>
    public static class MQOBoneWeightCSVParser
    {
        // スキーマ定義（列位置の宣言的定義）
        private static readonly BoneWeightCSVSchema _schema = new BoneWeightCSVSchema();

        /// <summary>
        /// CSVファイルをパース
        /// </summary>
        /// <param name="filePath">CSVファイルパス</param>
        /// <returns>パース結果、失敗時はnull</returns>
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
        /// <param name="content">CSV文字列</param>
        /// <returns>パース結果</returns>
        public static BoneWeightCSVData Parse(string content)
        {
            var result = new BoneWeightCSVData();

            // ステップ1: CSVHelperで行リストに変換
            var rows = CSVHelper.ParseString(content);

            if (rows.Count < 2) // ヘッダー + 最低1行のデータ
            {
                Debug.LogWarning("[MQOBoneWeightCSV] CSV has no data rows");
                return result;
            }

            Debug.Log($"[MQOBoneWeightCSV] Parsing {rows.Count - 1} data rows...");

            // ステップ2: 各行をパース
            int parseErrors = 0;
            bool isFirstRow = true;

            foreach (var row in rows)
            {
                // ヘッダー行をスキップ（最初の列が"MqoObjectName"ならヘッダー）
                if (isFirstRow)
                {
                    isFirstRow = false;
                    if (row[0] == "MqoObjectName")
                        continue;
                }

                // コメント行スキップ
                if (CSVHelper.IsCommentLine(row.OriginalLine))
                    continue;

                // 列数チェック（スキーマで定義された最低列数）
                if (row.FieldCount < _schema.MinimumFieldCount)
                {
                    parseErrors++;
                    continue;
                }

                // ステップ3: スキーマを使ってエントリに変換
                var entry = ParseRow(row);
                if (entry == null)
                {
                    parseErrors++;
                    continue;
                }

                // ステップ4: オブジェクト別にグループ化
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

        /// <summary>
        /// 1行をパースしてBoneWeightEntryを生成
        /// スキーマの列定義を使用して意味のある列アクセス
        /// </summary>
        private static BoneWeightEntry ParseRow(CSVRow row)
        {
            try
            {
                // スキーマの列定義を使ってデータ取得
                var entry = new BoneWeightEntry
                {
                    // 列0: オブジェクト名
                    MqoObjectName = row.Get(_schema.MqoObjectName),
                    
                    // 列1: 頂点ID（-1で未設定）
                    VertexID = row.GetInt(_schema.VertexId, -1),
                    
                    // 列2: 頂点インデックス
                    VertexIndex = row.GetInt(_schema.VertexIndex, -1),
                    
                    // 列3-6: ボーン名（4本）
                    BoneNames = _schema.GetBoneNames(row),
                    
                    // 列7-10: ウェイト値（4本）
                    Weights = _schema.GetWeights(row)
                };

                return entry;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MQOBoneWeightCSV] Failed to parse line: {row.OriginalLine}\n{ex.Message}");
                return null;
            }
        }
    }

    // =========================================================================
    // 適用ユーティリティ
    // =========================================================================

    /// <summary>
    /// ボーンウェイト適用ユーティリティ
    /// パースしたCSVデータをMeshObjectに適用
    /// </summary>
    public static class MQOBoneWeightApplier
    {
        /// <summary>
        /// MeshObjectにボーンウェイトを適用
        /// </summary>
        /// <param name="meshObject">対象のMeshObject</param>
        /// <param name="objectWeights">CSVからパースしたウェイトデータ</param>
        /// <param name="boneNameToIndex">ボーン名→インデックスのマッピング</param>
        /// <returns>適用された頂点数</returns>
        /// <remarks>
        /// マッチングモード:
        /// - AllVertexIDsValid=true: VertexIDでマッチング（オブジェクト間で頂点を共有可能）
        /// - AllVertexIDsValid=false: VertexIndexでマッチング（オブジェクト内の順序で対応）
        /// </remarks>
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

            // マッチングモード判定
            bool useVertexID = objectWeights.AllVertexIDsValid;

            Debug.Log($"[MQOBoneWeight] === Applying to '{meshObject.Name}' ===");
            Debug.Log($"[MQOBoneWeight]   Mode: {(useVertexID ? "VertexID" : "VertexIndex")}, CSV: {objectWeights.Entries.Count}, Mesh: {meshObject.Vertices.Count}");

            // 各頂点を処理
            for (int i = 0; i < meshObject.Vertices.Count; i++)
            {
                var vertex = meshObject.Vertices[i];
                
                // マッチングするエントリを検索
                BoneWeightEntry entry;
                if (useVertexID)
                {
                    // VertexIDでマッチング（オブジェクト間共有対応）
                    entry = objectWeights.FindByVertexID(vertex.Id);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }
                else
                {
                    // VertexIndexでマッチング（順序ベース）
                    entry = objectWeights.FindByVertexIndex(i);
                    if (entry == null)
                    {
                        skippedCount++;
                        continue;
                    }
                }

                // BoneWeightを構築（最大4ボーン）
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
        /// <param name="boneNames">ボーン名の列挙</param>
        /// <returns>ボーン名 → インデックス の辞書</returns>
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
