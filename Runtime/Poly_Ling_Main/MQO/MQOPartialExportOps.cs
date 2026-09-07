// MQOPartialExportOps.cs
// MQO部分エクスポートのロジック層。Editor依存なし。
// Runtime/Poly_Ling_Main/MQO/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

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
            AxisFlip flip,
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
                    exportScale, flip,
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
            AxisFlip flip,
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

            // 展開順と孤立判定は MeshExpansion が唯一の実装（MeshExpansion.cs 冒頭の指示）。
            var usedVertexIndices = MeshExpansion.BuildNonIsolatedSet(mqoMo);

            int transferred = 0;
            int startOffset = modelVertexOffset;
            int localCount  = 0;

            if (writeBackPosition)
            {
                MeshExpansion.Enumerate(mqoMo, (vIdx, uvIdx, expIdx) =>
                {
                    localCount = expIdx + 1;
                    if (uvIdx != 0) return;   // 位置はスロット間で共有

                    Vector3? newPos = GetModelVertexPosition(modelMeshes, startOffset + expIdx);
                    if (!newPos.HasValue) return;

                    // 軸反転はインポートと同じ規則（自己逆元）。スケールは逆数。
                    Vector3 pos = AxisFlipOps.Position(flip, newPos.Value);
                    pos /= exportScale;

                    mqoMo.Vertices[vIdx].Position = pos;
                    if (vIdx < mqoDocObj.Vertices.Count)
                        mqoDocObj.Vertices[vIdx].Position = pos;

                    transferred++;
                }, usedVertexIndices);
            }
            else
            {
                localCount = MeshExpansion.CountExpanded(mqoMo, usedVertexIndices);
            }

            modelVertexOffset += localCount;

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

            // 頂点→展開開始オフセット辞書。展開順は MeshExpansion に一本化。
            var vertexToExpandedStart = new Dictionary<int, int>();
            MeshExpansion.Enumerate(mqoMo, (vIdx, uvIdx, expIdx) =>
            {
                if (uvIdx == 0) vertexToExpandedStart[vIdx] = expIdx;
            }, usedVertexIndices);

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

            MeshExpansion.Enumerate(mqoMo, (vIdx, uvIdx, expIdx) =>
            {
                if (uvIdx != 0) return;                          // 特殊面は頂点ごとに 1 枚
                if (vIdx >= mqoDocObj.Vertices.Count) return;

                int    globalOffset = startOffset + expIdx;
                Vertex vertexInfo   = GetModelVertexInfo(modelMeshes, globalOffset);

                if (vertexInfo != null)
                {
                    if (vertexInfo.Id != -1 || vertexInfo.SubId != 0 || vertexInfo.PartsId != 0)
                        mqoDocObj.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForVertexId(
                                vIdx, vertexInfo.Id, vertexInfo.SubId, vertexInfo.PartsId, 0));

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
            }, usedVertexIndices);
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
            var hit = NavigateToSlot(modelMeshes, offset);
            if (hit.vertex == null) return null;

            var v = hit.vertex;
            if (hit.uvIdx < v.UVs.Count) return v.UVs[hit.uvIdx];
            if (v.UVs.Count > 0)         return v.UVs[0];
            return Vector2.zero;
        }

        private static Vertex GetModelVertexInfo(List<PartialMeshEntry> modelMeshes, int offset)
            => NavigateToVertex(modelMeshes, offset);

        /// <summary>
        /// 展開後グローバルインデックス offset に対応する Vertex を返す。
        /// UV スロット内では同一 Vertex を返す（Position / BoneWeight 共有のため）。
        /// </summary>
        private static Vertex NavigateToVertex(List<PartialMeshEntry> modelMeshes, int offset)
            => NavigateToSlot(modelMeshes, offset).vertex;

        /// <summary>
        /// 展開後グローバルインデックス offset を (Vertex, UVスロット) へ解く。
        /// 走査は MeshExpansion に一本化してある（MeshExpansion.cs 冒頭の指示）。
        /// </summary>
        private static (Vertex vertex, int uvIdx) NavigateToSlot(
            List<PartialMeshEntry> modelMeshes, int offset)
        {
            int currentOffset = 0;
            foreach (var model in modelMeshes)
            {
                var mo = model.Context?.MeshObject;
                if (mo == null) continue;

                int meshVertCount = model.ExpandedVertexCount;
                if (offset < currentOffset + meshVertCount)
                {
                    int localIdx = offset - currentOffset;

                    Vertex found     = null;
                    int    foundSlot = 0;

                    MeshExpansion.Enumerate(mo, (vIdx, uvIdx, expIdx) =>
                    {
                        if (found != null || expIdx != localIdx) return;
                        found     = mo.Vertices[vIdx];
                        foundSlot = uvIdx;
                    });

                    return (found, foundSlot);
                }
                currentOffset += meshVertCount;
            }
            return (null, 0);
        }

    }
}
