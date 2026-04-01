// PMXPartialExportOps.cs
// PMX部分エクスポートのロジック層。Editor依存なし。
// Runtime/Poly_Ling_Main/PMX/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// モデルメッシュとPMX材質の対応データ
    /// </summary>
    public class MeshMaterialMapping
    {
        public bool Selected = false;

        // モデル側
        public int         DrawableIndex;
        public int         MasterIndex;
        public string      MeshName;
        public int         MeshVertexCount;
        public int         MeshExpandedVertexCount;
        public MeshContext MeshContext;

        // PMX側
        public string      PMXMaterialName;
        public int         PMXVertexStartIndex;
        public int         PMXVertexCount;
        public List<int>   PMXVertexIndices;

        // 照合結果
        public bool IsMatched => MeshExpandedVertexCount == PMXVertexCount;
    }

    /// <summary>
    /// PMX部分エクスポートのロジック。
    /// マッピング構築・頂点データ転送を担う。
    /// ファイルIO・ダイアログ・コンテキスト参照は Editor 側パネルが行う。
    /// </summary>
    public class PMXPartialExportOps
    {
        // ================================================================
        // マッピング構築
        // ================================================================

        /// <summary>
        /// ModelContext と PMXDocument からメッシュ↔材質マッピングを構築する。
        /// </summary>
        public List<MeshMaterialMapping> BuildMappings(ModelContext model, PMXDocument pmxDocument)
        {
            var mappings = new List<MeshMaterialMapping>();
            if (model == null || pmxDocument == null) return mappings;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return mappings;

            var pmxObjectGroups = PMXHelper.GetObjectNameGroups(pmxDocument);

            for (int i = 0; i < drawables.Count; i++)
            {
                var entry = drawables[i];
                var ctx   = entry.Context;
                if (ctx?.MeshObject == null) continue;

                var vertexInfo = PMXHelper.GetVertexInfo(ctx);
                var mapping    = new MeshMaterialMapping
                {
                    DrawableIndex           = i,
                    MasterIndex             = entry.MasterIndex,
                    MeshName                = ctx.Name,
                    MeshVertexCount         = vertexInfo.VertexCount,
                    MeshExpandedVertexCount = vertexInfo.ExpandedVertexCount,
                    MeshContext             = ctx
                };

                // 名前マッチング: 完全一致 → ベース名 → "+" サフィックスの順
                string baseName = ctx.Name;
                if (baseName != null &&
                    (baseName.EndsWith("_L") || baseName.EndsWith("_R")))
                {
                    baseName = baseName.Substring(0, baseName.Length - 2);
                }

                ObjectGroup matchedGroup = null;
                string      matchedKey   = null;

                if (ctx.Name != null && pmxObjectGroups.TryGetValue(ctx.Name, out var g1))
                    { matchedGroup = g1; matchedKey = ctx.Name; }
                else if (baseName != null && pmxObjectGroups.TryGetValue(baseName, out var g2))
                    { matchedGroup = g2; matchedKey = baseName; }
                else if (ctx.Name != null && pmxObjectGroups.TryGetValue(ctx.Name + "+", out var g3))
                    { matchedGroup = g3; matchedKey = ctx.Name + "+"; }
                else if (baseName != null && pmxObjectGroups.TryGetValue(baseName + "+", out var g4))
                    { matchedGroup = g4; matchedKey = baseName + "+"; }

                if (matchedGroup != null)
                {
                    mapping.PMXMaterialName     = matchedKey;
                    mapping.PMXVertexIndices    = matchedGroup.VertexIndices;
                    mapping.PMXVertexCount      = matchedGroup.VertexCount;
                    mapping.PMXVertexStartIndex = matchedGroup.VertexIndices.Count > 0
                        ? matchedGroup.VertexIndices.Min() : 0;
                }

                mappings.Add(mapping);
            }

            Debug.Log($"[PMXPartialExport] Built {mappings.Count} mappings");
            return mappings;
        }

        // ================================================================
        // 転送実行
        // ================================================================

        /// <summary>
        /// 選択済みマッピングの頂点データを PMXDocument に書き込む。
        /// PMXDocument はそのまま編集されるため、呼び出し前にクローンが必要な場合は
        /// 呼び出し側で対処すること（現状は元ファイルロードでリセット）。
        /// </summary>
        public int ExecuteExport(
            IEnumerable<MeshMaterialMapping> mappings,
            PMXDocument pmxDocument,
            float scale,
            bool  flipZ,
            bool  flipUV_V,
            bool  replacePositions,
            bool  replaceNormals,
            bool  replaceUVs,
            bool  replaceBoneWeights)
        {
            int totalTransferred = 0;

            foreach (var mapping in mappings)
            {
                if (!mapping.Selected || !mapping.IsMatched) continue;

                int transferred = TransferMeshToPMX(
                    mapping, pmxDocument,
                    scale, flipZ, flipUV_V,
                    replacePositions, replaceNormals, replaceUVs, replaceBoneWeights);

                totalTransferred += transferred;
            }

            return totalTransferred;
        }

        private int TransferMeshToPMX(
            MeshMaterialMapping mapping,
            PMXDocument         pmxDocument,
            float scale,
            bool  flipZ,
            bool  flipUV_V,
            bool  replacePositions,
            bool  replaceNormals,
            bool  replaceUVs,
            bool  replaceBoneWeights)
        {
            var mo = mapping.MeshContext?.MeshObject;
            if (mo == null) return 0;
            if (mapping.PMXVertexIndices == null || mapping.PMXVertexIndices.Count == 0) return 0;

            int transferred = 0;
            int localIndex  = 0;

            foreach (var vertex in mo.Vertices)
            {
                if (localIndex >= mapping.PMXVertexIndices.Count) break;

                int pmxVertexIndex = mapping.PMXVertexIndices[localIndex];
                if (pmxVertexIndex >= pmxDocument.Vertices.Count) { localIndex++; continue; }

                var pmxVertex = pmxDocument.Vertices[pmxVertexIndex];

                if (replacePositions)
                {
                    Vector3 pos = vertex.Position;
                    if (flipZ) pos.z = -pos.z;
                    pos *= scale;
                    pmxVertex.Position = pos;
                }

                if (replaceNormals)
                {
                    Vector3 normal = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
                    if (flipZ) normal.z = -normal.z;
                    pmxVertex.Normal = normal;
                }

                if (replaceUVs)
                {
                    Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
                    if (flipUV_V) uv.y = 1f - uv.y;
                    pmxVertex.UV = uv;
                }

                if (replaceBoneWeights && vertex.BoneWeight.HasValue)
                {
                    var bw          = vertex.BoneWeight.Value;
                    var boneWeights = new List<PMXBoneWeight>();

                    if (bw.weight0 > 0) boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex0, Weight = bw.weight0 });
                    if (bw.weight1 > 0) boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex1, Weight = bw.weight1 });
                    if (bw.weight2 > 0) boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex2, Weight = bw.weight2 });
                    if (bw.weight3 > 0) boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex3, Weight = bw.weight3 });

                    pmxVertex.BoneWeights = boneWeights.ToArray();
                    pmxVertex.WeightType  = boneWeights.Count switch
                    {
                        1 => 0,  // BDEF1
                        2 => 1,  // BDEF2
                        _ => 2   // BDEF4
                    };
                }

                transferred++;
                localIndex++;
            }

            Debug.Log(
                $"[PMXPartialExport] Transferred '{mapping.MeshName}' → " +
                $"'{mapping.PMXMaterialName}': {transferred} vertices");

            return transferred;
        }
    }
}
