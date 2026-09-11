// MQOImportResult.cs
// MQO インポートの結果・統計（MQOImporter から分離）。
// Runtime/Poly_Ling_Main/MQO/Import/ に配置（MQOImporter.cs と同じ名前空間。MQOImporter.cs から分割）

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.EditorBridge;
using Poly_Ling.PMX;
using Poly_Ling.Symmetry;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOインポート結果
    /// </summary>
    public class MQOImportResult
    {
        /// <summary>成功したか</summary>
        public bool Success { get; set; }

        /// <summary>エラーメッセージ</summary>
        public string ErrorMessage { get; set; }

        /// <summary>インポートされたMeshContextリスト</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>インポートされたボーンMeshContextリスト</summary>
        public List<MeshContext> BoneMeshContexts { get; } = new List<MeshContext>();

        /// <summary>インポートされたマテリアル参照リスト（正式形式）</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>
        /// インポートされたマテリアルリスト（MaterialReferencesから導出）
        /// </summary>
        /// <remarks>新規コードではMaterialReferencesを使用してください</remarks>
        public List<Material> Materials
        {
            get
            {
                var list = new List<Material>();
                foreach (var matRef in MaterialReferences)
                {
                    list.Add(matRef?.Material);
                }
                return list;
            }
        }

        /// <summary>マテリアル数</summary>
        public int MaterialCount => MaterialReferences.Count;

        /// <summary>
        /// ミラー側マテリアルのオフセット
        /// ミラー側マテリアルインデックス = 実体側インデックス + MirrorMaterialOffset
        /// </summary>
        public int MirrorMaterialOffset { get; set; } = 0;

        /// <summary>元のMQOドキュメント</summary>
        public MQODocument Document { get; set; }

        /// <summary>インポート統計</summary>
        public MQOImportStats Stats { get; } = new MQOImportStats();

        /// <summary>構築されたMirrorPairリスト</summary>
        public List<MirrorPair> MirrorPairs { get; } = new List<MirrorPair>();

        /// <summary>
        /// 全MeshContextの面のMaterialIndexにオフセットを加算
        /// Appendモードで既存マテリアルがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存マテリアル数）</param>
        public void ApplyMaterialIndexOffset(int offset)
        {
            if (offset <= 0) return;

            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                foreach (var face in meshContext.MeshObject.Faces)
                {
                    if (face.MaterialIndex >= 0)
                    {
                        face.MaterialIndex += offset;
                    }
                }
            }

            Debug.Log($"[MQOImportResult] Applied material index offset: +{offset}");
        }

        /// <summary>
        /// ボーンMeshContextsの親インデックスにオフセットを加算
        /// メッシュの後にボーンを追加する場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（メッシュ数）</param>
        public void ApplyBoneParentIndexOffset(int offset)
        {
            if (offset <= 0) return;

            foreach (var boneCtx in BoneMeshContexts)
            {
                if (boneCtx == null) continue;

                // 親インデックスがある場合のみオフセット。
                // ParentIndex は HierarchyParentIndex と同じ入れ物なので 1 回だけ足す。
                if (boneCtx.HierarchyParentIndex >= 0)
                {
                    boneCtx.HierarchyParentIndex += offset;
                }
            }

            Debug.Log($"[MQOImportResult] Applied bone parent index offset: +{offset}");
        }

        /// <summary>
        /// 全MeshContextのBoneWeightインデックスにオフセットを加算
        /// メッシュの後にボーンを追加する場合、BoneWeightのboneIndexを調整
        /// </summary>
        /// <param name="offset">加算するオフセット（メッシュ数）</param>
        public void ApplyBoneWeightIndexOffset(int offset)
        {
            if (offset <= 0) return;

            int adjustedVertices = 0;
            int adjustedMirrorVertices = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                foreach (var vertex in meshContext.MeshObject.Vertices)
                {
                    // 実体側BoneWeight
                    if (vertex.BoneWeight.HasValue)
                    {
                        var bw = vertex.BoneWeight.Value;
                        bw.boneIndex0 += offset;
                        bw.boneIndex1 += offset;
                        bw.boneIndex2 += offset;
                        bw.boneIndex3 += offset;
                        vertex.BoneWeight = bw;
                        adjustedVertices++;
                    }

                    // ミラー側BoneWeight
                    if (vertex.MirrorBoneWeight.HasValue)
                    {
                        var mbw = vertex.MirrorBoneWeight.Value;
                        mbw.boneIndex0 += offset;
                        mbw.boneIndex1 += offset;
                        mbw.boneIndex2 += offset;
                        mbw.boneIndex3 += offset;
                        vertex.MirrorBoneWeight = mbw;
                        adjustedMirrorVertices++;
                    }
                }
            }

            Debug.Log($"[MQOImportResult] Applied bone weight index offset: +{offset} to {adjustedVertices} vertices, {adjustedMirrorVertices} mirror vertices");
        }
    }

    /// <summary>
    /// インポート統計情報
    /// </summary>
    public class MQOImportStats
    {
        public int ObjectCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
        public int BoneCount { get; set; }
        public int SkippedSpecialFaces { get; set; }
    }
}
