// MQOPartialExportOps.cs
// MQO部分エクスポートのロジック層。Editor依存なし。
// Runtime/Poly_Ling_Main/MQO/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分エクスポートのロジック。
    /// 頂点データ転送（Position / UV / BoneWeight）を担う。
    /// ファイルIO・ダイアログ・_matchHelper はEditor側パネルが持つ。
    /// </summary>
    public class MQOPartialExportOps
    {
        // ================================================================
        // エクスポート実行
        // ================================================================

        /// <summary>
        /// 選択済みペアをMQODocumentに転送する。
        /// modelVertexOffset は呼び出し側で 0 から渡す（出力引数で累積）。
        /// </summary>
        public int ExecuteExport(
            List<PartialMQOEntry>  selectedMQOs,
            List<PartialMeshEntry> selectedModels,
            MQODocument            mqoDocument,
            float exportScale,
            bool  flipZ,
            bool  writeBackPosition,
            bool  writeBackUV,
            bool  writeBackBoneWeight)
        {
            int transferred       = 0;
            int modelVertexOffset = 0;

            foreach (var mqoEntry in selectedMQOs)
            {
                int count = TransferToMQO(
                    mqoEntry, selectedModels, mqoDocument,
                    exportScale, flipZ,
                    writeBackPosition, writeBackUV, writeBackBoneWeight,
                    ref modelVertexOffset);
                transferred += count;
            }

            return transferred;
        }

        // ================================================================
        // 1オブジェクト転送
        // ================================================================

        private int TransferToMQO(
            PartialMQOEntry        mqoEntry,
            List<PartialMeshEntry> modelMeshes,
            MQODocument            mqoDocument,
            float exportScale,
            bool  flipZ,
            bool  writeBackPosition,
            bool  writeBackUV,
            bool  writeBackBoneWeight,
            ref int modelVertexOffset)
        {
            var mqoMeshContext = mqoEntry.MeshContext;
            var mqoMo          = mqoMeshContext?.MeshObject;
            if (mqoMo == null) return 0;

            var mqoDocObj = mqoDocument.Objects.FirstOrDefault(o => o.Name == mqoEntry.Name);
            if (mqoDocObj == null) return 0;

            var usedVertexIndices = BuildUsedVertexSet(mqoMo);

            int transferred = 0;
            int startOffset = modelVertexOffset;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount   = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                // 孤立頂点はスキップ（モデル側オフセットも進めない）
                if (!usedVertexIndices.Contains(vIdx)) continue;

                if (writeBackPosition)
                {
                    Vector3? newPos = GetModelVertexPosition(modelMeshes, modelVertexOffset);
                    if (newPos.HasValue)
                    {
                        Vector3 pos = newPos.Value;
                        if (flipZ)  pos.z = -pos.z;
                        pos /= exportScale;

                        mqoVertex.Position = pos;
                        if (vIdx < mqoDocObj.Vertices.Count)
                            mqoDocObj.Vertices[vIdx].Position = pos;

                        transferred++;
                    }
                }

                modelVertexOffset += uvCount;
            }

            if (writeBackUV)
                WriteBackUVsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);

            if (writeBackBoneWeight)
                WriteBackBoneWeightsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);

            return transferred;
        }

        // ================================================================
        // UV書き戻し
        // ================================================================

        private void WriteBackUVsToMQO(
            PartialMQOEntry        mqoEntry,
            List<PartialMeshEntry> modelMeshes,
            int                    startOffset,
            MQOObject              mqoDocObj,
            HashSet<int>           usedVertexIndices)
        {
            var mqoMo = mqoEntry.MeshContext?.MeshObject;
            if (mqoMo == null) return;

            // 頂点→展開開始オフセット辞書（孤立点除外）
            var vertexToExpandedStart = new Dictionary<int, int>();
            int expandedIdx = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!usedVertexIndices.Contains(vIdx)) continue;
                vertexToExpandedStart[vIdx] = expandedIdx;
                expandedIdx += mqoMo.Vertices[vIdx].UVs.Count > 0
                    ? mqoMo.Vertices[vIdx].UVs.Count : 1;
            }

            int mqoFaceIdx = 0;
            foreach (var mqoDocFace in mqoDocObj.Faces)
            {
                if (mqoDocFace.IsSpecialFace) continue;
                if (mqoDocFace.VertexIndices == null) continue;

                Face meshFace = null;
                while (mqoFaceIdx < mqoMo.FaceCount)
                {
                    var f = mqoMo.Faces[mqoFaceIdx++];
                    if (f.VertexIndices.Count >= 3) { meshFace = f; break; }
                }
                if (meshFace == null) continue;

                if (mqoDocFace.UVs == null || mqoDocFace.UVs.Length != mqoDocFace.VertexIndices.Length)
                    mqoDocFace.UVs = new Vector2[mqoDocFace.VertexIndices.Length];

                for (int i = 0; i < mqoDocFace.VertexIndices.Length && i < meshFace.VertexIndices.Count; i++)
                {
                    int vIdx = mqoDocFace.VertexIndices[i];
                    if (!vertexToExpandedStart.TryGetValue(vIdx, out int localExpStart)) continue;

                    int uvSlot      = (i < meshFace.UVIndices.Count) ? meshFace.UVIndices[i] : 0;
                    int globalOffset = startOffset + localExpStart + uvSlot;
                    Vector2? uv     = GetModelVertexUV(modelMeshes, globalOffset);
                    if (uv.HasValue)
                        mqoDocFace.UVs[i] = uv.Value;
                }
            }
        }

        // ================================================================
        // BoneWeight書き戻し
        // ================================================================

        private void WriteBackBoneWeightsToMQO(
            PartialMQOEntry        mqoEntry,
            List<PartialMeshEntry> modelMeshes,
            int                    startOffset,
            MQOObject              mqoDocObj,
            HashSet<int>           usedVertexIndices)
        {
            var mqoMo = mqoEntry.MeshContext?.MeshObject;
            if (mqoMo == null) return;

            // 既存の特殊面を削除
            mqoDocObj.Faces.RemoveAll(f => f.IsSpecialFace);

            int localOffset = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount && vIdx < mqoDocObj.Vertices.Count; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount   = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                if (!usedVertexIndices.Contains(vIdx)) continue;

                int    globalOffset = startOffset + localOffset;
                Vertex vertexInfo   = GetModelVertexInfo(modelMeshes, globalOffset);

                if (vertexInfo != null)
                {
                    if (vertexInfo.Id != -1)
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForVertexId(vIdx, vertexInfo.Id, 0));

                    if (vertexInfo.HasBoneWeight)
                    {
                        var bwd = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.BoneWeight.Value);
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, bwd, false, 0));
                    }

                    if (vertexInfo.HasMirrorBoneWeight)
                    {
                        var mbwd = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.MirrorBoneWeight.Value);
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, mbwd, true, 0));
                    }
                }

                localOffset += uvCount;
            }
        }

        // ================================================================
        // 展開後インデックスナビゲーター
        // PMXは展開済みのため、展開後インデックスで頂点を参照する。
        // ================================================================

        private static Vector3? GetModelVertexPosition(List<PartialMeshEntry> modelMeshes, int offset)
        {
            var v = NavigateToVertex(modelMeshes, offset);
            return v?.Position;
        }

        private static Vector2? GetModelVertexUV(List<PartialMeshEntry> modelMeshes, int offset)
        {
            // offset は展開後グローバルインデックス（UVスロット込み）
            int currentOffset = 0;
            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;
                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx    = offset - currentOffset;
                    int expandedIdx = 0;

                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var v      = mo.Vertices[vIdx];
                        int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;

                        if (localIdx < expandedIdx + uvCount)
                        {
                            int uvSlot = localIdx - expandedIdx;
                            if (uvSlot < v.UVs.Count)  return v.UVs[uvSlot];
                            if (v.UVs.Count > 0)        return v.UVs[0];
                            return Vector2.zero;
                        }
                        expandedIdx += uvCount;
                    }
                    return null;
                }
                currentOffset += meshVertCount;
            }
            return null;
        }

        private static Vertex GetModelVertexInfo(List<PartialMeshEntry> modelMeshes, int offset)
            => NavigateToVertex(modelMeshes, offset);

        /// <summary>
        /// 展開後グローバルインデックス offset に対応する Vertex を返す。
        /// UV スロット内では同一 Vertex を返す（Position / BoneWeight 共有のため）。
        /// </summary>
        private static Vertex NavigateToVertex(List<PartialMeshEntry> modelMeshes, int offset)
        {
            int currentOffset = 0;
            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;
                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx    = offset - currentOffset;
                    int expandedIdx = 0;

                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var v       = mo.Vertices[vIdx];
                        int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;

                        if (localIdx < expandedIdx + uvCount)
                            return v;

                        expandedIdx += uvCount;
                    }
                    return null;
                }
                currentOffset += meshVertCount;
            }
            return null;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private static HashSet<int> BuildUsedVertexSet(MeshObject mo)
        {
            var used = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (var vi in face.VertexIndices)
                    used.Add(vi);
            return used;
        }
    }
}
