// Assets/Editor/Poly_Ling/MQO/Export/MQOBoneWeightCSVWriter.cs
// MQOエクスポート用ボーン/ウェイトCSV出力

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// ボーン/ウェイトCSVライター
    /// </summary>
    public static class MQOBoneWeightCSVWriter
    {
        // ================================================================
        // ボーンCSV出力
        // ================================================================

        /// <summary>
        /// ボーン情報をCSVに出力
        /// </summary>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="model">ModelContext</param>
        /// <returns>出力したボーン数</returns>
        public static int ExportBoneCSV(string filePath, ModelContext model)
        {
            if (model == null || model.MeshContextList == null)
                return 0;

            var boneContexts = new List<MeshContext>();
            foreach (var mc in model.MeshContextList)
            {
                if (mc != null && mc.Type == MeshType.Bone)
                {
                    boneContexts.Add(mc);
                }
            }

            return ExportBoneCSV(filePath, boneContexts, model.MeshContextList);
        }

        /// <summary>
        /// ボーン情報をCSVに出力
        /// PmxBone形式で出力（PmxBoneCSVParserと互換）
        /// </summary>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="boneContexts">ボーンMeshContextのリスト</param>
        /// <param name="allContexts">全MeshContextのリスト（親インデックス解決用）</param>
        /// <returns>出力したボーン数</returns>
        public static int ExportBoneCSV(string filePath, IList<MeshContext> boneContexts, IList<MeshContext> allContexts)
        {
            if (boneContexts == null || boneContexts.Count == 0)
                return 0;

            // ボーンのワールド位置を計算
            var boneWorldPositions = CalculateBoneWorldPositions(allContexts);

            var sb = new StringBuilder();

            // ヘッダー（コメント行）
            sb.AppendLine(";PmxBone,\"ボーン名\",\"英名\",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,\"親ボーン名\"");

            int exportedCount = 0;
            foreach (var mc in boneContexts)
            {
                if (mc == null) continue;

                int boneIndex = allContexts.IndexOf(mc);
                string boneName = mc.Name ?? "";

                // 親ボーン情報（親がボーンタイプの場合のみ）
                int parentIndex = mc.HierarchyParentIndex;
                string parentName = "";
                if (parentIndex >= 0 && parentIndex < allContexts.Count)
                {
                    var parent = allContexts[parentIndex];
                    if (parent != null && parent.Type == MeshType.Bone)
                    {
                        parentName = parent.Name ?? "";
                    }
                }

                // ワールド位置を取得
                Vector3 pos = Vector3.zero;
                if (boneWorldPositions.TryGetValue(boneIndex, out Vector3 worldPos))
                {
                    pos = worldPos;
                }

                // BoneTransformからフラグ情報を取得（デフォルト値を使用）
                int deformHierarchy = 0;
                int physicsAfter = 0;
                int canRotate = 1;
                int canMove = 0;
                int isIK = 0;
                int isVisible = 1;
                int isControllable = 1;

                // PmxBone形式で出力
                // PmxBone,"ボーン名","英名",変形階層,物理後,位置X,位置Y,位置Z,回転,移動,IK,表示,操作,"親ボーン名"
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "PmxBone,\"{0}\",\"\",{1},{2},{3:F6},{4:F6},{5:F6},{6},{7},{8},{9},{10},\"{11}\"",
                    EscapeCSVQuoted(boneName),
                    deformHierarchy,
                    physicsAfter,
                    pos.x, pos.y, pos.z,
                    canRotate,
                    canMove,
                    isIK,
                    isVisible,
                    isControllable,
                    EscapeCSVQuoted(parentName)));

                exportedCount++;
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MQOBoneWeightCSVWriter] Exported {exportedCount} bones to: {filePath}");

            return exportedCount;
        }

        /// <summary>
        /// ボーンのワールド位置を計算
        /// BoneTransform.Positionはローカル位置（親からの相対）なので、
        /// 親をたどってワールド位置を計算する
        /// </summary>
        private static Dictionary<int, Vector3> CalculateBoneWorldPositions(IList<MeshContext> allContexts)
        {
            var worldPositions = new Dictionary<int, Vector3>();

            // まずボーンのみを抽出
            var boneIndices = new List<int>();
            for (int i = 0; i < allContexts.Count; i++)
            {
                if (allContexts[i]?.Type == MeshType.Bone)
                {
                    boneIndices.Add(i);
                }
            }

            // 各ボーンのワールド位置を計算
            foreach (int idx in boneIndices)
            {
                worldPositions[idx] = CalculateBoneWorldPosition(idx, allContexts, worldPositions);
            }

            return worldPositions;
        }

        private static Vector3 CalculateBoneWorldPosition(
            int boneIndex,
            IList<MeshContext> allContexts,
            Dictionary<int, Vector3> cache)
        {
            // キャッシュにあれば返す
            if (cache.TryGetValue(boneIndex, out Vector3 cached))
                return cached;

            var mc = allContexts[boneIndex];
            if (mc == null)
                return Vector3.zero;

            // ローカル位置を取得
            Vector3 localPos = mc.BoneTransform?.Position ?? Vector3.zero;

            // 親がいる場合は親のワールド位置を加算
            int parentIndex = mc.HierarchyParentIndex;
            if (parentIndex >= 0 && parentIndex < allContexts.Count && 
                allContexts[parentIndex]?.Type == MeshType.Bone)
            {
                Vector3 parentWorld = CalculateBoneWorldPosition(parentIndex, allContexts, cache);
                Vector3 worldPos = parentWorld + localPos;
                cache[boneIndex] = worldPos;
                return worldPos;
            }
            else
            {
                // 親がいない場合はローカル位置がワールド位置
                cache[boneIndex] = localPos;
                return localPos;
            }
        }

        // ================================================================
        // ウェイトCSV出力
        // ================================================================

        /// <summary>
        /// ボーンウェイト情報をCSVに出力
        /// インポーターと互換性のある形式で出力
        /// </summary>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="model">ModelContext</param>
        /// <returns>出力した頂点数</returns>
        public static int ExportWeightCSV(string filePath, ModelContext model)
        {
            if (model == null || model.MeshContextList == null)
                return 0;

            // メッシュとボーンを分離
            var meshContexts = new List<MeshContext>();
            var boneContexts = new List<MeshContext>();

            foreach (var mc in model.MeshContextList)
            {
                if (mc == null) continue;
                if (mc.Type == MeshType.Bone)
                    boneContexts.Add(mc);
                else if (mc.Type == MeshType.Mesh && mc.MeshObject != null)
                    meshContexts.Add(mc);
            }

            return ExportWeightCSV(filePath, meshContexts, boneContexts, model.MeshContextList);
        }

        /// <summary>
        /// ボーンウェイト情報をCSVに出力
        /// </summary>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="meshContexts">メッシュMeshContextのリスト</param>
        /// <param name="boneContexts">ボーンMeshContextのリスト</param>
        /// <param name="allContexts">全MeshContextのリスト（インデックス→名前解決用）</param>
        /// <returns>出力した頂点数</returns>
        public static int ExportWeightCSV(
            string filePath,
            IList<MeshContext> meshContexts,
            IList<MeshContext> boneContexts,
            IList<MeshContext> allContexts)
        {
            if (meshContexts == null || meshContexts.Count == 0)
                return 0;

            // ボーンインデックス→名前マップを作成
            var boneIndexToName = new Dictionary<int, string>();
            for (int i = 0; i < allContexts.Count; i++)
            {
                var mc = allContexts[i];
                if (mc != null && mc.Type == MeshType.Bone)
                {
                    boneIndexToName[i] = mc.Name ?? $"Bone{i}";
                }
            }

            var sb = new StringBuilder();

            // ヘッダー（インポーターと互換）
            sb.AppendLine("MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3");

            int exportedCount = 0;

            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;

                var meshObject = mc.MeshObject;
                string objectName = mc.Name ?? "Object";

                for (int vIdx = 0; vIdx < meshObject.VertexCount; vIdx++)
                {
                    var vertex = meshObject.Vertices[vIdx];

                    // VertexID（MQO形式の頂点ID）
                    int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                    // ボーンウェイト取得
                    string[] boneNames = new string[4] { "", "", "", "" };
                    float[] weights = new float[4] { 0, 0, 0, 0 };

                    if (vertex.HasBoneWeight)
                    {
                        var bw = vertex.BoneWeight.Value;

                        // Bone0
                        if (bw.weight0 > 0 && boneIndexToName.TryGetValue(bw.boneIndex0, out string name0))
                        {
                            boneNames[0] = name0;
                            weights[0] = bw.weight0;
                        }

                        // Bone1
                        if (bw.weight1 > 0 && boneIndexToName.TryGetValue(bw.boneIndex1, out string name1))
                        {
                            boneNames[1] = name1;
                            weights[1] = bw.weight1;
                        }

                        // Bone2
                        if (bw.weight2 > 0 && boneIndexToName.TryGetValue(bw.boneIndex2, out string name2))
                        {
                            boneNames[2] = name2;
                            weights[2] = bw.weight2;
                        }

                        // Bone3
                        if (bw.weight3 > 0 && boneIndexToName.TryGetValue(bw.boneIndex3, out string name3))
                        {
                            boneNames[3] = name3;
                            weights[3] = bw.weight3;
                        }
                    }

                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                        EscapeCSV(objectName),
                        vertexId,
                        vIdx,
                        EscapeCSV(boneNames[0]),
                        EscapeCSV(boneNames[1]),
                        EscapeCSV(boneNames[2]),
                        EscapeCSV(boneNames[3]),
                        weights[0],
                        weights[1],
                        weights[2],
                        weights[3]));

                    exportedCount++;
                }

                // ミラーボーンウェイトも出力（存在する場合）
                if (mc.IsMirrored)
                {
                    string mirrorObjectName = objectName + "+";

                    for (int vIdx = 0; vIdx < meshObject.VertexCount; vIdx++)
                    {
                        var vertex = meshObject.Vertices[vIdx];

                        int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                        string[] boneNames = new string[4] { "", "", "", "" };
                        float[] weights = new float[4] { 0, 0, 0, 0 };

                        if (vertex.HasMirrorBoneWeight)
                        {
                            var bw = vertex.MirrorBoneWeight.Value;

                            if (bw.weight0 > 0 && boneIndexToName.TryGetValue(bw.boneIndex0, out string name0))
                            {
                                boneNames[0] = name0;
                                weights[0] = bw.weight0;
                            }
                            if (bw.weight1 > 0 && boneIndexToName.TryGetValue(bw.boneIndex1, out string name1))
                            {
                                boneNames[1] = name1;
                                weights[1] = bw.weight1;
                            }
                            if (bw.weight2 > 0 && boneIndexToName.TryGetValue(bw.boneIndex2, out string name2))
                            {
                                boneNames[2] = name2;
                                weights[2] = bw.weight2;
                            }
                            if (bw.weight3 > 0 && boneIndexToName.TryGetValue(bw.boneIndex3, out string name3))
                            {
                                boneNames[3] = name3;
                                weights[3] = bw.weight3;
                            }
                        }

                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                            EscapeCSV(mirrorObjectName),
                            vertexId,
                            vIdx,
                            EscapeCSV(boneNames[0]),
                            EscapeCSV(boneNames[1]),
                            EscapeCSV(boneNames[2]),
                            EscapeCSV(boneNames[3]),
                            weights[0],
                            weights[1],
                            weights[2],
                            weights[3]));

                        exportedCount++;
                    }
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MQOBoneWeightCSVWriter] Exported {exportedCount} vertex weights to: {filePath}");

            return exportedCount;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// CSVエスケープ（ダブルクォート内用）
        /// </summary>
        private static string EscapeCSVQuoted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // ダブルクォートをエスケープ
            return value.Replace("\"", "\"\"");
        }

        /// <summary>
        /// CSVエスケープ（通常フィールド用）
        /// </summary>
        private static string EscapeCSV(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // カンマ、ダブルクォート、改行を含む場合はエスケープ
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
