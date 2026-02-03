// PMXToMeshContext.cs - PMXモデルをMeshContextに変換
// PMX追加仕様対応：材質Memo欄のObjectNameでオブジェクト分割
// 頂点順序の入れ替えは厳禁
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXモデルをMeshContextに変換
    /// PMX追加仕様：材質Memo欄のObjectNameでオブジェクト分割
    /// </summary>
    public static class PMXToMeshContext
    {
        /// <summary>
        /// PMXファイル（バイナリ）を読み込んでMeshContextリストに変換
        /// ObjectNameでオブジェクト分割（PMX追加仕様）
        /// </summary>
        /// <param name="filePath">PMXファイルパス</param>
        /// <param name="splitByObjectName">ObjectNameでオブジェクト分割するか（デフォルトtrue）</param>
        /// <returns>MeshContextのリスト</returns>
        public static List<MeshContext> LoadBinary(string filePath, bool splitByObjectName = true)
        {
            var doc = PMXReader.Load(filePath);
            return Convert(doc, splitByObjectName);
        }

        /// <summary>
        /// PMXDocumentをMeshContextリストに変換
        /// PMX追加仕様：ObjectNameでオブジェクト分割
        /// </summary>
        /// <param name="doc">PMXDocument</param>
        /// <param name="splitByObjectName">ObjectNameでオブジェクト分割するか（デフォルトtrue）</param>
        /// <returns>MeshContextのリスト</returns>
        public static List<MeshContext> Convert(PMXDocument doc, bool splitByObjectName = true)
        {
            if (splitByObjectName)
            {
                // PMX追加仕様：ObjectNameでオブジェクト分割
                // PMXHelper.BuildObjectGroupsを使用
                return ConvertByObjectName(doc);
            }
            else
            {
                // 単一メッシュとして変換（後方互換性）
                var results = new List<MeshContext>();
                var meshContext = ConvertAll(doc);
                meshContext.Name = !string.IsNullOrEmpty(doc.ModelInfo?.Name) 
                    ? doc.ModelInfo.Name 
                    : Path.GetFileNameWithoutExtension(doc.FilePath);
                results.Add(meshContext);
                return results;
            }
        }

        /// <summary>
        /// PMX追加仕様：ObjectNameでオブジェクト分割してMeshContextリストを作成
        /// 頂点順序は厳密に保持される
        /// </summary>
        private static List<MeshContext> ConvertByObjectName(PMXDocument doc)
        {
            var results = new List<MeshContext>();
            
            // PMXHelperを使ってObjectGroupを構築
            var groups = PMXHelper.BuildObjectGroups(doc);
            
            // デフォルトのインポート設定
            var settings = new PMXImportSettings
            {
                FlipZ = true,
                FlipUV_V = false,
                Scale = 1f,
                RecalculateNormals = false
            };
            
            // 各グループをMeshContextに変換
            foreach (var group in groups)
            {
                var meshContext = PMXHelper.ConvertObjectGroupToMeshContext(doc, group, settings);
                if (meshContext != null)
                {
                    results.Add(meshContext);
                }
            }
            
            return results;
        }

        /// <summary>
        /// PMXモデル全体を単一MeshContextに変換
        /// </summary>
        private static MeshContext ConvertAll(PMXDocument doc)
        {
            var mesh = new MeshObject();

            // 頂点
            foreach (var v in doc.Vertices)
            {
                var vertex = new Vertex(ConvertPosition(v.Position), v.UV);
                vertex.Normals.Add(ConvertNormal(v.Normal));
                
                // ボーンウェイトを変換
                if (v.BoneWeights != null && v.BoneWeights.Length > 0)
                {
                    vertex.BoneWeight = ConvertBoneWeight(v.BoneWeights);
                }
                
                mesh.Vertices.Add(vertex);
            }

            // 面（頂点インデックスを反転してUnityの座標系に対応）
            // マテリアル情報も同時に設定
            int faceIdx = 0;
            int currentMaterial = 0;
            int facesInCurrentMaterial = doc.Materials.Count > 0 ? doc.Materials[0].FaceCount : int.MaxValue;
            
            foreach (var f in doc.Faces)
            {
                // マテリアル境界をチェック
                while (currentMaterial < doc.Materials.Count - 1 && faceIdx >= facesInCurrentMaterial)
                {
                    currentMaterial++;
                    facesInCurrentMaterial += doc.Materials[currentMaterial].FaceCount;
                }
                
                // VertexIndex2とVertexIndex3を入れ替えて面を追加（座標系変換）
                var face = new Face(f.VertexIndex1, f.VertexIndex3, f.VertexIndex2, currentMaterial);
                mesh.AddFace(face);
                faceIdx++;
            }

            mesh.CalculateBounds();

            return new MeshContext
            {
                MeshObject = mesh,
                Type = MeshType.Mesh,
                OriginalPositions = mesh.Vertices.ConvertAll(v => v.Position).ToArray()
            };
        }

        // 注：ConvertMaterialRange（旧来のマテリアル単位分割）は削除
        // PMX追加仕様では材質単位ではなくObjectName単位で分割する
        // PMXHelper.ConvertObjectGroupToMeshContextを使用すること

        /// <summary>
        /// PMX座標系（左手系）からUnity座標系（左手系だがZ反転）に変換
        /// PMX: X右, Y上, Z奥
        /// Unity: X右, Y上, Z手前
        /// </summary>
        private static Vector3 ConvertPosition(Vector3 pmxPos)
        {
            // Z軸を反転
            return new Vector3(pmxPos.x, pmxPos.y, -pmxPos.z);
        }

        private static Vector3 ConvertNormal(Vector3 pmxNormal)
        {
            return new Vector3(pmxNormal.x, pmxNormal.y, -pmxNormal.z);
        }

        /// <summary>
        /// PMXボーンウェイトをUnity BoneWeightに変換
        /// </summary>
        private static BoneWeight ConvertBoneWeight(PMXBoneWeight[] pmxWeights)
        {
            var bw = new BoneWeight();
            
            if (pmxWeights.Length > 0)
            {
                bw.boneIndex0 = pmxWeights[0].BoneIndex >= 0 ? pmxWeights[0].BoneIndex : 0;
                bw.weight0 = pmxWeights[0].Weight;
            }
            if (pmxWeights.Length > 1)
            {
                bw.boneIndex1 = pmxWeights[1].BoneIndex >= 0 ? pmxWeights[1].BoneIndex : 0;
                bw.weight1 = pmxWeights[1].Weight;
            }
            if (pmxWeights.Length > 2)
            {
                bw.boneIndex2 = pmxWeights[2].BoneIndex >= 0 ? pmxWeights[2].BoneIndex : 0;
                bw.weight2 = pmxWeights[2].Weight;
            }
            if (pmxWeights.Length > 3)
            {
                bw.boneIndex3 = pmxWeights[3].BoneIndex >= 0 ? pmxWeights[3].BoneIndex : 0;
                bw.weight3 = pmxWeights[3].Weight;
            }
            
            return bw;
        }

        #region Extended Information

        /// <summary>
        /// ボーン情報も含めて変換（将来拡張用）
        /// </summary>
        public static (MeshContext meshContext, List<PMXBone> bones) LoadWithBones(string filePath)
        {
            var doc = PMXReader.Load(filePath);
            var meshContext = ConvertAll(doc);
            meshContext.Name = doc.ModelInfo?.Name ?? Path.GetFileNameWithoutExtension(filePath);
            return (meshContext, doc.Bones);
        }

        /// <summary>
        /// モーフ情報を頂点オフセットとして取得（将来拡張用）
        /// </summary>
        public static Dictionary<string, Vector3[]> GetVertexMorphs(PMXDocument doc)
        {
            var morphs = new Dictionary<string, Vector3[]>();

            foreach (var morph in doc.Morphs)
            {
                if (morph.MorphType != 1)  // 1 = Vertex morph
                    continue;

                var offsets = new Vector3[doc.Vertices.Count];
                foreach (var offset in morph.Offsets)
                {
                    var vertexOffset = offset as PMXVertexMorphOffset;
                    if (vertexOffset != null && vertexOffset.VertexIndex >= 0 && vertexOffset.VertexIndex < offsets.Length)
                    {
                        offsets[vertexOffset.VertexIndex] = ConvertPosition(vertexOffset.Offset);
                    }
                }
                morphs[morph.Name] = offsets;
            }

            return morphs;
        }

        #endregion
    }
}
