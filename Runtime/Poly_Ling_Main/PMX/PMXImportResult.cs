// PMXImportResult.cs
// PMX インポートの結果・統計・マテリアルグループ情報（PMXImporter から分離）。
// Runtime/Poly_Ling_Main/PMX/ に配置（PMXImporter.cs と同じ名前空間。PMXImporter.cs から分割）

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.Core;
using Poly_Ling.Symmetry;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXインポート結果
    /// </summary>
    public class PMXImportResult
    {
        /// <summary>成功したか</summary>
        public bool Success { get; set; }

        /// <summary>エラーメッセージ</summary>
        public string ErrorMessage { get; set; }

        /// <summary>インポートされたMeshContextリスト</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>
        /// 各ボーンのPMXワールド位置（インポート時の初期位置）
        /// CCDIKSolverのSetBonePositionsに渡す用
        /// </summary>
        public Vector3[] BoneWorldPositions { get; set; }

        /// <summary>インポートされたマテリアル参照リスト（正式形式）</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>
        /// インポートされたマテリアルリスト（MaterialReferencesから導出）
        /// </summary>
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

        /// <summary>元のPMXドキュメント</summary>
        public PMXDocument Document { get; set; }

        /// <summary>インポートされたモーフエクスプレッション</summary>
        public List<MorphExpression> MorphExpressions { get; } = new List<MorphExpression>();

        /// <summary>PMX のモデル情報（名前・英語名・コメント）。</summary>
        public PmxModelInfoData ModelInfo { get; set; }

        /// <summary>検出されたミラーペア</summary>
        public List<MirrorPair> MirrorPairs { get; } = new List<MirrorPair>();

        /// <summary>材質グループ情報（モーフ変換で使用）</summary>
        public List<MaterialGroupInfo> MaterialGroupInfos { get; } = new List<MaterialGroupInfo>();

        /// <summary>インポート統計</summary>
        public PMXImportStats Stats { get; } = new PMXImportStats();

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

            Debug.Log($"[PMXImportResult] Applied material index offset: +{offset}");
        }

        /// <summary>
        /// 全MeshContextの頂点のBoneWeightインデックスにオフセットを加算
        /// Appendモードで既存MeshContextがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存MeshContext数）</param>
        public void ApplyBoneWeightIndexOffset(int offset)
        {
            if (offset <= 0) return;

            int convertedCount = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                // ボーンタイプの場合はBoneWeightを持たない
                if (meshContext.Type == MeshType.Bone) continue;

                foreach (var vertex in meshContext.MeshObject.Vertices)
                {
                    if (vertex.BoneWeight.HasValue)
                    {
                        var bw = vertex.BoneWeight.Value;
                        vertex.BoneWeight = new UnityEngine.BoneWeight
                        {
                            boneIndex0 = bw.boneIndex0 + offset,
                            boneIndex1 = bw.weight1 > 0 ? bw.boneIndex1 + offset : bw.boneIndex1,
                            boneIndex2 = bw.weight2 > 0 ? bw.boneIndex2 + offset : bw.boneIndex2,
                            boneIndex3 = bw.weight3 > 0 ? bw.boneIndex3 + offset : bw.boneIndex3,
                            weight0 = bw.weight0,
                            weight1 = bw.weight1,
                            weight2 = bw.weight2,
                            weight3 = bw.weight3
                        };
                        convertedCount++;
                    }
                }
            }

            Debug.Log($"[PMXImportResult] Applied bone weight index offset: +{offset} ({convertedCount} vertices)");
        }

        /// <summary>
        /// ボーンのHierarchyParentIndexにオフセットを加算
        /// Appendモードで既存MeshContextがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存MeshContext数）</param>
        public void ApplyBoneHierarchyOffset(int offset)
        {
            if (offset <= 0) return;

            int convertedCount = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext == null) continue;

                // ボーンの親インデックスにオフセットを適用
                if (meshContext.Type == MeshType.Bone && meshContext.HierarchyParentIndex >= 0)
                {
                    meshContext.HierarchyParentIndex += offset;
                    if (meshContext.MeshObject != null)
                    {
                        meshContext.MeshObject.HierarchyParentIndex = meshContext.HierarchyParentIndex;
                    }
                    convertedCount++;
                }
            }

            Debug.Log($"[PMXImportResult] Applied bone hierarchy offset: +{offset} ({convertedCount} bones)");
        }
    }

    /// <summary>
    /// インポート統計情報
    /// </summary>
    public class PMXImportStats
    {
        public int MeshCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
        public int MaterialGroupCount { get; set; }
        public int BoneCount { get; set; }
        public int MorphCount { get; set; }
    }

    /// <summary>
    /// 材質グループ情報（メッシュ/モーフ分割で共有）
    /// </summary>
    public class MaterialGroupInfo
    {
        /// <summary>グループに含まれる材質名リスト</summary>
        public List<string> MaterialNames { get; set; } = new List<string>();

        /// <summary>グループが使用するPMX頂点インデックス</summary>
        public HashSet<int> UsedVertexIndices { get; set; } = new HashSet<int>();

        /// <summary>PMX頂点インデックス → ローカル頂点インデックス</summary>
        public Dictionary<int, int> PmxToLocalIndex { get; set; } = new Dictionary<int, int>();

        /// <summary>result.MeshContexts 内のインデックス</summary>
        public int MeshContextIndex { get; set; } = -1;

        /// <summary>グループ名（メッシュ名）</summary>
        public string Name { get; set; }
    }
}
