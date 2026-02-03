// PMXHelper.cs - PMX ObjectName解析とMeshContext変換ヘルパー
// 材質Memo欄のObjectName対応
// 頂点順序保持を厳守

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// 材質Memo欄から解析されたオブジェクト情報
    /// </summary>
    public class MaterialObjectInfo
    {
        /// <summary>材質名</summary>
        public string MaterialName { get; set; }

        /// <summary>材質インデックス（PMXDocument内）</summary>
        public int MaterialIndex { get; set; }

        /// <summary>オブジェクト名（Memo欄のObjectName値、なければ材質名）</summary>
        public string ObjectName { get; set; }

        /// <summary>ミラーフラグ（Memo欄にIsMirrorがある場合true）</summary>
        public bool IsMirror { get; set; }

        /// <summary>面数</summary>
        public int FaceCount { get; set; }

        /// <summary>面開始インデックス（PMXDocument.Faces内）</summary>
        public int FaceStartIndex { get; set; }
    }

    /// <summary>
    /// ObjectName単位でグループ化されたオブジェクト情報
    /// </summary>
    public class ObjectGroup
    {
        /// <summary>オブジェクト名</summary>
        public string ObjectName { get; set; }

        /// <summary>ベイクされたミラーか</summary>
        public bool IsBakedMirror { get; set; }

        /// <summary>グループに含まれる材質情報（PMX順序を保持）</summary>
        public List<MaterialObjectInfo> Materials { get; } = new List<MaterialObjectInfo>();

        /// <summary>グループが使用するPMX頂点インデックス（順序保持のためList）</summary>
        public List<int> VertexIndices { get; } = new List<int>();

        /// <summary>PMX頂点インデックス → ローカル頂点インデックス</summary>
        public Dictionary<int, int> PmxToLocalIndex { get; } = new Dictionary<int, int>();

        /// <summary>ローカル頂点数</summary>
        public int VertexCount => VertexIndices.Count;

        /// <summary>総面数</summary>
        public int TotalFaceCount => Materials.Sum(m => m.FaceCount);
    }

    /// <summary>
    /// MeshContext頂点情報
    /// </summary>
    public class MeshVertexInfo
    {
        /// <summary>メッシュ名</summary>
        public string MeshName { get; set; }

        /// <summary>頂点数（元頂点数）</summary>
        public int VertexCount { get; set; }

        /// <summary>UV展開後の頂点数</summary>
        public int ExpandedVertexCount { get; set; }

        /// <summary>面数</summary>
        public int FaceCount { get; set; }

        /// <summary>材質数</summary>
        public int MaterialCount { get; set; }
    }

    /// <summary>
    /// PMXヘルパークラス
    /// ObjectName解析、グループ化、頂点情報取得などの共有ユーティリティ
    /// </summary>
    public static class PMXHelper
    {
        // ================================================================
        // Memo欄解析
        // ================================================================

        /// <summary>
        /// 材質Memo欄からObjectNameとIsMirrorを解析
        /// CSV形式: "ObjectName,値" または "IsMirror,ObjectName,値" など順不同
        /// </summary>
        /// <param name="memo">材質のMemo欄</param>
        /// <returns>(ObjectName, IsMirror) タプル。ObjectNameがない場合はnull</returns>
        public static (string objectName, bool isMirror) ParseMaterialMemo(string memo)
        {
            string objectName = null;
            bool isMirror = false;

            if (string.IsNullOrWhiteSpace(memo))
                return (objectName, isMirror);

            // CSV形式でパース
            var parts = memo.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();

                if (part.Equals("IsMirror", StringComparison.OrdinalIgnoreCase))
                {
                    isMirror = true;
                }
                else if (part.Equals("ObjectName", StringComparison.OrdinalIgnoreCase))
                {
                    // 次の要素が値
                    if (i + 1 < parts.Length)
                    {
                        objectName = parts[i + 1].Trim();
                        i++; // 次の要素をスキップ
                    }
                }
            }

            return (objectName, isMirror);
        }

        /// <summary>
        /// ObjectName用のMemo文字列を構築
        /// </summary>
        /// <param name="objectName">オブジェクト名</param>
        /// <param name="isMirror">ミラーフラグ</param>
        /// <returns>Memo文字列</returns>
        public static string BuildMaterialMemo(string objectName, bool isMirror)
        {
            if (string.IsNullOrEmpty(objectName) && !isMirror)
                return "";

            var parts = new List<string>();
            
            if (!string.IsNullOrEmpty(objectName))
                parts.Add($"ObjectName,{objectName}");
            
            if (isMirror)
                parts.Add("IsMirror");

            return string.Join(",", parts);
        }

        // ================================================================
        // ObjectGroupビルド
        // ================================================================

        /// <summary>
        /// PMXDocumentからObjectGroupリストを構築
        /// 材質Memo欄のObjectNameでグループ化
        /// ObjectNameがない材質は材質名をObjectNameとして扱う
        /// 頂点順序は厳密に保持される
        /// </summary>
        /// <param name="document">PMXDocument</param>
        /// <returns>ObjectGroupのリスト（PMXの材質順序に基づく）</returns>
        public static List<ObjectGroup> BuildObjectGroups(PMXDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            // 材質情報を収集
            var materialInfos = new List<MaterialObjectInfo>();
            int faceOffset = 0;

            for (int i = 0; i < document.Materials.Count; i++)
            {
                var mat = document.Materials[i];
                var (objectName, isMirror) = ParseMaterialMemo(mat.Memo);

                // ObjectNameがなければ材質名を使用
                if (string.IsNullOrEmpty(objectName))
                    objectName = mat.Name;

                // IsMirrorフラグがある場合、ObjectNameに"+"を付与（ミラーグループ識別用）
                string groupKey = isMirror ? objectName + "+" : objectName;

                materialInfos.Add(new MaterialObjectInfo
                {
                    MaterialName = mat.Name,
                    MaterialIndex = i,
                    ObjectName = groupKey,
                    IsMirror = isMirror,
                    FaceCount = mat.FaceCount,
                    FaceStartIndex = faceOffset
                });

                faceOffset += mat.FaceCount;
            }

            // ObjectName(+IsMirror識別子)でグループ化（出現順を保持）
            var groupDict = new Dictionary<string, ObjectGroup>();
            var groupOrder = new List<string>();

            foreach (var matInfo in materialInfos)
            {
                if (!groupDict.ContainsKey(matInfo.ObjectName))
                {
                    groupDict[matInfo.ObjectName] = new ObjectGroup
                    {
                        // ミラー側は"+"付きの名前を保持
                        ObjectName = matInfo.ObjectName,
                        IsBakedMirror = matInfo.IsMirror
                    };
                    groupOrder.Add(matInfo.ObjectName);
                }
                groupDict[matInfo.ObjectName].Materials.Add(matInfo);
            }

            // 各グループの頂点インデックスを収集（順序保持）
            foreach (var group in groupDict.Values)
            {
                CollectGroupVertices(document, group);
            }

            // グループを出現順で返す
            return groupOrder.Select(key => groupDict[key]).ToList();
        }

        /// <summary>
        /// グループの頂点インデックスを収集
        /// 使用される頂点の最小〜最大範囲をそのまま切り出す
        /// </summary>
        private static void CollectGroupVertices(PMXDocument document, ObjectGroup group)
        {
            int minIndex = int.MaxValue;
            int maxIndex = int.MinValue;

            // 使用される頂点インデックスの範囲を取得
            foreach (var matInfo in group.Materials)
            {
                for (int fi = 0; fi < matInfo.FaceCount && (matInfo.FaceStartIndex + fi) < document.Faces.Count; fi++)
                {
                    var face = document.Faces[matInfo.FaceStartIndex + fi];
                    minIndex = Math.Min(minIndex, face.VertexIndex1);
                    minIndex = Math.Min(minIndex, face.VertexIndex2);
                    minIndex = Math.Min(minIndex, face.VertexIndex3);
                    maxIndex = Math.Max(maxIndex, face.VertexIndex1);
                    maxIndex = Math.Max(maxIndex, face.VertexIndex2);
                    maxIndex = Math.Max(maxIndex, face.VertexIndex3);
                }
            }

            if (minIndex == int.MaxValue) return;

            // 範囲内の全頂点をそのまま追加（インデックス順）
            for (int pmxIndex = minIndex; pmxIndex <= maxIndex; pmxIndex++)
            {
                int localIndex = group.VertexIndices.Count;
                group.VertexIndices.Add(pmxIndex);
                group.PmxToLocalIndex[pmxIndex] = localIndex;
            }
        }

        // ================================================================
        // 頂点情報取得
        // ================================================================

        /// <summary>
        /// MeshContextの頂点情報を取得
        /// </summary>
        /// <param name="meshContext">対象MeshContext</param>
        /// <returns>頂点情報</returns>
        public static MeshVertexInfo GetVertexInfo(MeshContext meshContext)
        {
            if (meshContext?.MeshObject == null)
                return new MeshVertexInfo { MeshName = meshContext?.Name ?? "Unknown" };

            var meshObject = meshContext.MeshObject;

            // 使用されている材質インデックスを収集
            var usedMaterials = new HashSet<int>();
            foreach (var face in meshObject.Faces)
            {
                usedMaterials.Add(face.MaterialIndex);
            }

            // UV展開後の頂点数を計算
            // （同じ頂点位置でも異なるUVを持つ場合は別頂点として扱う）
            int expandedCount = CalculateExpandedVertexCount(meshObject);

            return new MeshVertexInfo
            {
                MeshName = meshContext.Name ?? meshObject.Name ?? "Unnamed",
                VertexCount = meshObject.VertexCount,
                ExpandedVertexCount = expandedCount,
                FaceCount = meshObject.FaceCount,
                MaterialCount = usedMaterials.Count
            };
        }

        /// <summary>
        /// 複数MeshContextの頂点情報を取得
        /// </summary>
        /// <param name="meshContexts">MeshContextリスト</param>
        /// <param name="excludeMorphs">モーフメッシュを除外するか</param>
        /// <returns>頂点情報のリスト</returns>
        public static List<MeshVertexInfo> GetVertexInfos(
            IEnumerable<MeshContext> meshContexts, 
            bool excludeMorphs = true)
        {
            var results = new List<MeshVertexInfo>();

            foreach (var ctx in meshContexts)
            {
                if (ctx == null) continue;
                if (excludeMorphs && ctx.Type == MeshType.Morph) continue;
                if (ctx.Type == MeshType.Bone) continue;

                results.Add(GetVertexInfo(ctx));
            }

            return results;
        }

        /// <summary>
        /// UV展開後の頂点数を計算
        /// MeshObjectがShared形式（頂点を共有）の場合、
        /// 同じ頂点位置で異なるUVを持つ頂点は分離される
        /// </summary>
        private static int CalculateExpandedVertexCount(MeshObject meshObject)
        {
            // 各頂点インデックスごとに、使用されるUVのセットを収集
            var vertexUVSets = new Dictionary<int, HashSet<Vector2>>();

            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    
                    // 頂点からUVを取得
                    Vector2 uv = Vector2.zero;
                    if (vIdx >= 0 && vIdx < meshObject.Vertices.Count)
                    {
                        var vertex = meshObject.Vertices[vIdx];
                        if (vertex.UVs.Count > 0)
                            uv = vertex.UVs[0];
                    }

                    if (!vertexUVSets.ContainsKey(vIdx))
                        vertexUVSets[vIdx] = new HashSet<Vector2>();
                    
                    vertexUVSets[vIdx].Add(uv);
                }
            }

            // 各頂点のUV種類数の合計が展開後頂点数
            int expandedCount = 0;
            foreach (var uvSet in vertexUVSets.Values)
            {
                expandedCount += uvSet.Count;
            }

            // 未使用頂点も含める場合
            // 使用されていない頂点がある場合は元の頂点数を下限とする
            return Math.Max(expandedCount, meshObject.VertexCount);
        }

        // ================================================================
        // ObjectName頂点範囲
        // ================================================================

        /// <summary>
        /// ObjectNameごとの頂点範囲を計算（PMXPartialExport用）
        /// 材質Memo欄のObjectNameでグルーピングし、各グループの頂点範囲を返す
        /// </summary>
        /// <param name="document">PMXDocument</param>
        /// <returns>ObjectName → (開始インデックス, 頂点数) の辞書</returns>
        public static Dictionary<string, (int startIndex, int count)> GetObjectNameVertexRanges(PMXDocument document)
        {
            var result = new Dictionary<string, (int startIndex, int count)>();
            if (document == null) return result;

            // ObjectGroupを構築（内部でMemo欄パース済み）
            var groups = BuildObjectGroups(document);

            foreach (var group in groups)
            {
                if (group.VertexIndices.Count == 0) continue;

                // グループの頂点範囲（最小〜最大）を計算
                int minIndex = group.VertexIndices.Min();
                int maxIndex = group.VertexIndices.Max();
                int count = maxIndex - minIndex + 1;

                // キーはObjectName（ミラーは末尾に"+"付き）
                string key = group.IsBakedMirror ? group.ObjectName + "+" : group.ObjectName;
                result[key] = (minIndex, count);
            }

            return result;
        }

        /// <summary>
        /// ObjectNameごとの詳細情報を取得（PMXPartialExport用）
        /// </summary>
        /// <param name="document">PMXDocument</param>
        /// <returns>ObjectName → ObjectGroup の辞書</returns>
        public static Dictionary<string, ObjectGroup> GetObjectNameGroups(PMXDocument document)
        {
            var result = new Dictionary<string, ObjectGroup>();
            if (document == null) return result;

            var groups = BuildObjectGroups(document);

            foreach (var group in groups)
            {
                string key = group.IsBakedMirror ? group.ObjectName + "+" : group.ObjectName;
                result[key] = group;
            }

            return result;
        }

        // ================================================================
        // ObjectGroup → MeshContext変換
        // ================================================================

        /// <summary>
        /// ObjectGroupからMeshContextを作成
        /// 頂点順序は厳密に保持
        /// </summary>
        /// <param name="document">PMXDocument</param>
        /// <param name="group">ObjectGroup</param>
        /// <param name="settings">インポート設定（座標変換用）</param>
        /// <returns>MeshContext</returns>
        public static MeshContext ConvertObjectGroupToMeshContext(
            PMXDocument document,
            ObjectGroup group,
            PMXImportSettings settings)
        {
            if (document == null || group == null)
                return null;

            var meshObject = new MeshObject(group.ObjectName);
            meshObject.IsExpanded = true;  // PMXは展開済み形式

            // 頂点を追加（VertexIndicesの順序通り = PMX出現順）
            foreach (int pmxVertexIndex in group.VertexIndices)
            {
                var pmxVert = document.Vertices[pmxVertexIndex];
                var vertex = ConvertPMXVertex(pmxVert, document, settings);
                meshObject.Vertices.Add(vertex);
            }

            // 面を追加（材質順、同一材質内では面の順序を保持）
            foreach (var matInfo in group.Materials)
            {
                for (int fi = 0; fi < matInfo.FaceCount && (matInfo.FaceStartIndex + fi) < document.Faces.Count; fi++)
                {
                    var pmxFace = document.Faces[matInfo.FaceStartIndex + fi];

                    // ローカルインデックスに変換
                    int localV1 = group.PmxToLocalIndex[pmxFace.VertexIndex1];
                    int localV2 = group.PmxToLocalIndex[pmxFace.VertexIndex2];
                    int localV3 = group.PmxToLocalIndex[pmxFace.VertexIndex3];

                    var face = new Face
                    {
                        MaterialIndex = matInfo.MaterialIndex
                    };

                    // Z反転の場合は頂点順序を逆にする
                    if (settings.FlipZ)
                    {
                        face.VertexIndices.Add(localV1);
                        face.VertexIndices.Add(localV3);
                        face.VertexIndices.Add(localV2);
                    }
                    else
                    {
                        face.VertexIndices.Add(localV1);
                        face.VertexIndices.Add(localV2);
                        face.VertexIndices.Add(localV3);
                    }

                    // UV/法線インデックス（頂点と同じ）
                    for (int i = 0; i < 3; i++)
                    {
                        face.UVIndices.Add(0);
                        face.NormalIndices.Add(0);
                    }

                    meshObject.Faces.Add(face);
                }
            }

            // 法線再計算（オプション）
            if (settings.RecalculateNormals)
            {
                meshObject.RecalculateSmoothNormals();
            }

            // スケール適用
            if (Math.Abs(settings.Scale - 1f) > 0.0001f)
            {
                foreach (var vertex in meshObject.Vertices)
                {
                    vertex.Position *= settings.Scale;
                }
            }

            meshObject.CalculateBounds();

            // MeshContext作成
            var meshContext = new MeshContext
            {
                MeshObject = meshObject,
                Type = MeshType.Mesh,
                OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
            };
            meshContext.Name = group.ObjectName;

            // BakedMirrorフラグ設定
            if (group.IsBakedMirror)
            {
                // ミラーグループの場合、BakedMirrorSourceIndexは後で設定される
                // ここではフラグのみ
            }

            return meshContext;
        }

        /// <summary>
        /// PMX頂点をVertexに変換（スケールなし）
        /// </summary>
        private static Vertex ConvertPMXVertex(PMXVertex pmxVert, PMXDocument document, PMXImportSettings settings)
        {
            // 座標変換
            Vector3 pos = ConvertPosition(pmxVert.Position, settings, applyScale: false);
            Vector3 normal = ConvertNormal(pmxVert.Normal, settings);
            Vector2 uv = ConvertUV(pmxVert.UV, settings);

            var vertex = new Vertex(pos, uv, normal);

            // ボーンウェイト
            if (pmxVert.BoneWeights != null && pmxVert.BoneWeights.Length > 0)
            {
                var bw = new BoneWeight();
                for (int i = 0; i < pmxVert.BoneWeights.Length && i < 4; i++)
                {
                    var pmxBw = pmxVert.BoneWeights[i];
                    int boneIndex = pmxBw.BoneIndex >= 0 ? pmxBw.BoneIndex : 0;

                    switch (i)
                    {
                        case 0: bw.boneIndex0 = boneIndex; bw.weight0 = pmxBw.Weight; break;
                        case 1: bw.boneIndex1 = boneIndex; bw.weight1 = pmxBw.Weight; break;
                        case 2: bw.boneIndex2 = boneIndex; bw.weight2 = pmxBw.Weight; break;
                        case 3: bw.boneIndex3 = boneIndex; bw.weight3 = pmxBw.Weight; break;
                    }
                }
                vertex.BoneWeight = bw;
            }

            return vertex;
        }

        // ================================================================
        // MeshContext → PMX変換（エクスポート用）
        // ================================================================

        /// <summary>
        /// MeshContextからPMX頂点・面を構築
        /// 頂点順序は厳密に保持
        /// </summary>
        /// <param name="meshContext">MeshContext</param>
        /// <param name="document">出力先PMXDocument</param>
        /// <param name="boneNameToIndex">ボーン名→インデックスのマッピング</param>
        /// <param name="settings">エクスポート設定</param>
        /// <param name="objectName">ObjectName（Memo欄に設定）</param>
        /// <param name="isMirror">ミラーフラグ</param>
        public static void AppendMeshContextToDocument(
            MeshContext meshContext,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings,
            string objectName = null,
            bool isMirror = false)
        {
            if (meshContext?.MeshObject == null) return;

            var meshObject = meshContext.MeshObject;
            int vertexStartIndex = document.Vertices.Count;

            // 頂点を追加（順序保持）
            foreach (var vertex in meshObject.Vertices)
            {
                var pmxVertex = ConvertVertexToPMX(vertex, boneNameToIndex, settings);
                pmxVertex.Index = document.Vertices.Count;
                document.Vertices.Add(pmxVertex);
            }

            // 面を材質ごとにグループ化して追加
            // 同一材質内の面順序は保持、ただし材質の出現順にソート
            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in meshObject.Faces)
            {
                if (!facesByMaterial.ContainsKey(face.MaterialIndex))
                    facesByMaterial[face.MaterialIndex] = new List<Face>();
                facesByMaterial[face.MaterialIndex].Add(face);
            }

            // 材質インデックス順に処理
            foreach (var matIndex in facesByMaterial.Keys.OrderBy(k => k))
            {
                var faces = facesByMaterial[matIndex];
                string materialName = matIndex < document.Materials.Count 
                    ? document.Materials[matIndex].Name 
                    : $"Material_{matIndex}";

                foreach (var face in faces)
                {
                    if (face.VertexIndices.Count < 3) continue;

                    // 三角形に分割
                    for (int i = 0; i < face.VertexIndices.Count - 2; i++)
                    {
                        int v0 = face.VertexIndices[0] + vertexStartIndex;
                        int v1 = face.VertexIndices[i + 1] + vertexStartIndex;
                        int v2 = face.VertexIndices[i + 2] + vertexStartIndex;

                        var pmxFace = new PMXFace
                        {
                            MaterialName = materialName,
                            MaterialIndex = matIndex,
                            FaceIndex = document.Faces.Count,
                            VertexIndex1 = settings.FlipZ ? v0 : v0,
                            VertexIndex2 = settings.FlipZ ? v2 : v1,
                            VertexIndex3 = settings.FlipZ ? v1 : v2
                        };

                        document.Faces.Add(pmxFace);
                    }
                }

                // 材質のMemo欄にObjectNameを設定
                if (matIndex < document.Materials.Count)
                {
                    string memo = BuildMaterialMemo(objectName ?? meshContext.Name, isMirror);
                    if (!string.IsNullOrEmpty(memo))
                    {
                        var mat = document.Materials[matIndex];
                        // 既存のMemoがある場合はマージ
                        if (!string.IsNullOrEmpty(mat.Memo))
                            mat.Memo = mat.Memo + "," + memo;
                        else
                            mat.Memo = memo;
                    }
                }
            }
        }

        /// <summary>
        /// VertexをPMXVertexに変換
        /// </summary>
        private static PMXVertex ConvertVertexToPMX(
            Vertex vertex, 
            Dictionary<string, int> boneNameToIndex, 
            PMXExportSettings settings)
        {
            // 座標変換
            Vector3 pos = vertex.Position;
            if (settings.FlipZ)
                pos = new Vector3(pos.x * settings.Scale, pos.y * settings.Scale, -pos.z * settings.Scale);
            else
                pos = pos * settings.Scale;

            Vector3 normal = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
            if (settings.FlipZ)
                normal = new Vector3(normal.x, normal.y, -normal.z).normalized;

            Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
            if (settings.FlipUV_V)
                uv.y = 1f - uv.y;

            var pmxVertex = new PMXVertex
            {
                Position = pos,
                Normal = normal,
                UV = uv,
                EdgeScale = 1f,
                WeightType = 0
            };

            // ボーンウェイト
            if (vertex.HasBoneWeight)
            {
                var bw = vertex.BoneWeight.Value;
                var weights = new List<PMXBoneWeight>();

                void AddWeight(int boneIdx, float weight)
                {
                    if (weight <= 0) return;
                    string boneName = GetBoneNameFromIndex(boneIdx, boneNameToIndex);
                    weights.Add(new PMXBoneWeight { BoneName = boneName, BoneIndex = boneIdx, Weight = weight });
                }

                AddWeight(bw.boneIndex0, bw.weight0);
                AddWeight(bw.boneIndex1, bw.weight1);
                AddWeight(bw.boneIndex2, bw.weight2);
                AddWeight(bw.boneIndex3, bw.weight3);

                pmxVertex.BoneWeights = weights.ToArray();
                pmxVertex.WeightType = weights.Count switch
                {
                    1 => 0,  // BDEF1
                    2 => 1,  // BDEF2
                    _ => 2   // BDEF4
                };
            }
            else
            {
                // デフォルト
                string defaultBone = boneNameToIndex.Keys.FirstOrDefault() ?? "";
                pmxVertex.BoneWeights = new[] { new PMXBoneWeight { BoneName = defaultBone, Weight = 1f } };
            }

            return pmxVertex;
        }

        private static string GetBoneNameFromIndex(int boneIndex, Dictionary<string, int> boneNameToIndex)
        {
            foreach (var kvp in boneNameToIndex)
            {
                if (kvp.Value == boneIndex)
                    return kvp.Key;
            }
            return boneNameToIndex.Keys.FirstOrDefault() ?? "";
        }

        // ================================================================
        // 座標変換ヘルパー
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pmxPos, PMXImportSettings settings, bool applyScale = true)
        {
            float scale = applyScale ? settings.Scale : 1f;
            if (settings.FlipZ)
                return new Vector3(pmxPos.x * scale, pmxPos.y * scale, -pmxPos.z * scale);
            return pmxPos * scale;
        }

        private static Vector3 ConvertNormal(Vector3 pmxNormal, PMXImportSettings settings)
        {
            if (settings.FlipZ)
                return new Vector3(pmxNormal.x, pmxNormal.y, -pmxNormal.z).normalized;
            return pmxNormal.normalized;
        }

        private static Vector2 ConvertUV(Vector2 pmxUV, PMXImportSettings settings)
        {
            if (settings.FlipUV_V)
                return new Vector2(pmxUV.x, 1f - pmxUV.y);
            return pmxUV;
        }

        // ================================================================
        // ミラーベイク時の材質順序
        // ================================================================

        /// <summary>
        /// ミラーベイク時の材質出力順序を取得
        /// 仕様：まず実体側の材質を順序通りに、次にミラー材質を順序通りに
        /// </summary>
        /// <param name="groups">ObjectGroupリスト</param>
        /// <returns>（実体グループ, ミラーグループ）のタプル</returns>
        public static (List<ObjectGroup> realGroups, List<ObjectGroup> mirrorGroups) 
            SeparateRealAndMirrorGroups(List<ObjectGroup> groups)
        {
            var realGroups = groups.Where(g => !g.IsBakedMirror).ToList();
            var mirrorGroups = groups.Where(g => g.IsBakedMirror).ToList();
            return (realGroups, mirrorGroups);
        }

        /// <summary>
        /// ミラーグループの元グループを検索
        /// </summary>
        /// <param name="mirrorGroup">ミラーグループ</param>
        /// <param name="realGroups">実体グループリスト</param>
        /// <returns>元グループ（見つからない場合null）</returns>
        public static ObjectGroup FindSourceGroupForMirror(ObjectGroup mirrorGroup, List<ObjectGroup> realGroups)
        {
            if (mirrorGroup == null || !mirrorGroup.IsBakedMirror)
                return null;

            // ObjectNameが一致する実体グループを探す
            return realGroups.FirstOrDefault(g => g.ObjectName == mirrorGroup.ObjectName);
        }
    }
}
