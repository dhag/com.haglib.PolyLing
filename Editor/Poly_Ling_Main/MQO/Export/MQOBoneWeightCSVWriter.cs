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

            // ボーンインデックス→名前マップ
            var boneIndexToName = new Dictionary<int, string>();
            for (int i = 0; i < allContexts.Count; i++)
            {
                if (allContexts[i]?.Type == MeshType.Bone)
                    boneIndexToName[i] = allContexts[i].Name ?? "";
            }

            var sb = new StringBuilder();
            string fmt = "F6";

            // ヘッダー（40カラム PMXEditor互換）
            sb.AppendLine(new Poly_Ling.CSV.PmxBoneCSVSchema().GenerateHeaderComment());

            int exportedCount = 0;
            foreach (var mc in boneContexts)
            {
                if (mc == null) continue;

                int boneIndex = allContexts.IndexOf(mc);
                string boneName = mc.Name ?? "";

                // 親ボーン名
                string parentName = "";
                int parentIndex = mc.HierarchyParentIndex;
                if (parentIndex >= 0 && parentIndex < allContexts.Count)
                {
                    var parent = allContexts[parentIndex];
                    if (parent != null && parent.Type == MeshType.Bone)
                        parentName = parent.Name ?? "";
                }

                // ワールド位置
                Vector3 pos = Vector3.zero;
                if (boneWorldPositions.TryGetValue(boneIndex, out Vector3 worldPos))
                    pos = worldPos;

                // IK情報
                int isIK = mc.IsIK ? 1 : 0;
                string ikTargetName = "";
                int ikLoop = 0;
                float ikAngleDeg = 0f;
                if (mc.IsIK)
                {
                    if (mc.IKTargetIndex >= 0 && boneIndexToName.TryGetValue(mc.IKTargetIndex, out string tn))
                        ikTargetName = tn;
                    ikLoop = mc.IKLoopCount;
                    ikAngleDeg = mc.IKLimitAngle * (180f / Mathf.PI);
                }

                // 40カラム PmxBone行
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "PmxBone,\"{0}\",\"\",{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},\"{11}\"," +
                    "{12},\"{13}\",{14},{15},{16}," +   // 表示先
                    "{17},{18},{19},{20},\"{21}\"," +    // 付与
                    "{22},{23},{24},{25}," +             // 軸固定
                    "{26},{27},{28},{29},{30},{31},{32}," + // ローカル軸
                    "{33},{34},\"{35}\",{36},{37}",     // 外部親, IK
                    EscapeCSVQuoted(boneName),           // 1
                    0,                                   // 3: 変形階層
                    0,                                   // 4: 物理後
                    F(pos.x, fmt), F(pos.y, fmt), F(pos.z, fmt),  // 5-7
                    1,                                   // 8: 回転
                    0,                                   // 9: 移動
                    isIK,                                // 10: IK
                    1,                                   // 11: 表示
                    1,                                   // 12: 操作
                    EscapeCSVQuoted(parentName),          // 13
                    0,                                   // 14: 表示先タイプ
                    "",                                  // 15: 表示先ボーン名
                    F(0, fmt), F(0, fmt), F(0, fmt),     // 16-18: 表示先オフセット
                    0, 0, 0,                             // 19-21: 付与フラグ
                    F(0, fmt),                           // 22: 付与率
                    "",                                  // 23: 付与親名
                    0,                                   // 24: 軸制限
                    F(0, fmt), F(0, fmt), F(0, fmt),     // 25-27: 制限軸
                    0,                                   // 28: ローカル軸
                    F(1, fmt), F(0, fmt), F(0, fmt),     // 29-31: ローカルX軸
                    F(0, fmt), F(0, fmt), F(1, fmt),     // 32-34: ローカルZ軸
                    0,                                   // 35: 外部親
                    0,                                   // 36: 外部親Key
                    EscapeCSVQuoted(ikTargetName),        // 37: IKTarget
                    ikLoop,                              // 38: IKLoop
                    F(ikAngleDeg, fmt)                   // 39: IK単位角
                ));

                // IKリンク行
                if (mc.IsIK && mc.IKLinks != null)
                {
                    sb.AppendLine(";PmxIKLink,親ボーン名,Linkボーン名,角度制限(0/1),XL[deg],XH[deg],YL[deg],YH[deg],ZL[deg],ZH[deg]");
                    float r2d = 180f / Mathf.PI;
                    foreach (var lk in mc.IKLinks)
                    {
                        string linkBoneName = "";
                        if (lk.BoneIndex >= 0 && boneIndexToName.TryGetValue(lk.BoneIndex, out string ln))
                            linkBoneName = ln;

                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "PmxIKLink,\"{0}\",\"{1}\",{2},{3},{4},{5},{6},{7},{8}",
                            EscapeCSVQuoted(boneName),
                            EscapeCSVQuoted(linkBoneName),
                            lk.HasLimit ? 1 : 0,
                            F(lk.LimitMin.x * r2d, fmt), F(lk.LimitMax.x * r2d, fmt),
                            F(lk.LimitMin.y * r2d, fmt), F(lk.LimitMax.y * r2d, fmt),
                            F(lk.LimitMin.z * r2d, fmt), F(lk.LimitMax.z * r2d, fmt)));
                    }
                }

                exportedCount++;
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MQOBoneWeightCSVWriter] Exported {exportedCount} bones (40col+IKLink) to: {filePath}");

            return exportedCount;
        }

        private static string F(float v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
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

            // メッシュ、BakedMirror、名前ミラー（+付き）、ボーンを分離
            var meshContexts = new List<MeshContext>();
            var bakedMirrorContexts = new List<MeshContext>();
            var boneContexts = new List<MeshContext>();

            foreach (var mc in model.MeshContextList)
            {
                if (mc == null) continue;
                if (mc.Type == MeshType.Bone)
                    boneContexts.Add(mc);
                else if (mc.Type == MeshType.BakedMirror && mc.MeshObject != null)
                    bakedMirrorContexts.Add(mc);
                else if (mc.Type != MeshType.BakedMirror && mc.MeshObject != null &&
                         !string.IsNullOrEmpty(mc.Name) && mc.Name.EndsWith("+"))
                    bakedMirrorContexts.Add(mc);  // タイプC: 名前末尾+もミラーとして扱う
                else if (mc.MeshObject != null && mc.Type != MeshType.Morph)
                    meshContexts.Add(mc);
            }

            return ExportWeightCSV(filePath, meshContexts, bakedMirrorContexts, boneContexts, model.MeshContextList);
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
            // BakedMirrorなしで呼び出された場合は空リストを渡す
            return ExportWeightCSV(filePath, meshContexts, new List<MeshContext>(), boneContexts, allContexts);
        }

        /// <summary>
        /// ボーンウェイト情報をCSVに出力（BakedMirror対応版）
        /// </summary>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="meshContexts">メッシュMeshContextのリスト</param>
        /// <param name="bakedMirrorContexts">BakedMirror MeshContextのリスト</param>
        /// <param name="boneContexts">ボーンMeshContextのリスト</param>
        /// <param name="allContexts">全MeshContextのリスト（インデックス→名前解決用）</param>
        /// <returns>出力した頂点数</returns>
        public static int ExportWeightCSV(
            string filePath,
            IList<MeshContext> meshContexts,
            IList<MeshContext> bakedMirrorContexts,
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

            // BakedMirror/名前ミラーメッシュのボーンウェイトを出力
            // ミラーの頂点ウェイトは、対応する実体メッシュ名+"+"で出力
            if (bakedMirrorContexts != null)
            {
                foreach (var mc in bakedMirrorContexts)
                {
                    if (mc?.MeshObject == null) continue;

                    // 元のメッシュ名を取得
                    string sourceObjectName = "Object";
                    if (mc.BakedMirrorSourceIndex >= 0 && mc.BakedMirrorSourceIndex < allContexts.Count)
                    {
                        // B: BakedMirrorSourceIndex から取得
                        var sourceMc = allContexts[mc.BakedMirrorSourceIndex];
                        if (sourceMc != null)
                        {
                            sourceObjectName = sourceMc.Name ?? "Object";
                        }
                    }
                    else if (!string.IsNullOrEmpty(mc.Name) && mc.Name.EndsWith("+"))
                    {
                        // C: 名前末尾+を除いたものが実体名
                        sourceObjectName = mc.Name.Substring(0, mc.Name.Length - 1);
                    }
                    
                    // ミラー側として出力（実体メッシュ名+"+"）
                    string mirrorObjectName = sourceObjectName + "+";
                    var meshObject = mc.MeshObject;

                    for (int vIdx = 0; vIdx < meshObject.VertexCount; vIdx++)
                    {
                        var vertex = meshObject.Vertices[vIdx];

                        int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                        string[] boneNames = new string[4] { "", "", "", "" };
                        float[] weights = new float[4] { 0, 0, 0, 0 };

                        // BakedMirrorの頂点のBoneWeightを使用
                        if (vertex.HasBoneWeight)
                        {
                            var bw = vertex.BoneWeight.Value;

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
