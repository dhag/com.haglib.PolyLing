// Assets/Editor/MeshFactory/MQO/Import/MQOImporter.cs
// MQODocument → MeshObject/MeshUndoContext 変換
// SimpleMeshFactoryのデータ構造に変換

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;

// MeshContextはSimpleMeshFactoryのネストクラス
//using MeshContext = MeshContext;

namespace MeshFactory.MQO
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

        /// <summary>インポートされたマテリアルリスト</summary>
        public List<Material> Materials { get; } = new List<Material>();

        /// <summary>元のMQOドキュメント</summary>
        public MQODocument Document { get; set; }

        /// <summary>インポート統計</summary>
        public MQOImportStats Stats { get; } = new MQOImportStats();
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
        public int SkippedSpecialFaces { get; set; }
    }

    /// <summary>
    /// MQOインポーター
    /// </summary>
    public static class MQOImporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ファイルからインポート
        /// </summary>
        public static MQOImportResult ImportFile(string filePath, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();

            try
            {
                // パース
                var document = MQOParser.ParseFile(filePath);
                result.Document = document;

                // 変換
                ConvertDocument(document, settings, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOImporter] Failed to import: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// 文字列からインポート
        /// </summary>
        public static MQOImportResult ImportFromString(string content, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();

            try
            {
                var document = MQOParser.Parse(content);
                result.Document = document;
                ConvertDocument(document, settings, result);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOImporter] Failed to import: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// MQODocumentからインポート
        /// </summary>
        public static MQOImportResult Import(MQODocument document, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();
            result.Document = document;

            try
            {
                ConvertDocument(document, settings, result);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ================================================================
        // 変換処理
        // ================================================================

        private static void ConvertDocument(MQODocument document, MQOImportSettings settings, MQOImportResult result)
        {
            // マテリアル変換
            if (settings.ImportMaterials)
            {
                foreach (var mqoMat in document.Materials)
                {
                    var mat = ConvertMaterial(mqoMat, settings);
                    result.Materials.Add(mat);
                }
                result.Stats.MaterialCount = result.Materials.Count;
            }

            // オブジェクト変換
            foreach (var mqoObj in document.Objects)
            {
                // 非表示オブジェクトをスキップ
                if (settings.SkipHiddenObjects && !mqoObj.IsVisible)
                    continue;

                var meshContext = ConvertObject(mqoObj, document.Materials, result.Materials, settings, result.Stats);
                if (meshContext != null)
                {
                    result.MeshContexts.Add(meshContext);
                }
            }

            result.Stats.ObjectCount = result.MeshContexts.Count;

            // 統合オプション
            if (settings.MergeObjects && result.MeshContexts.Count > 1)
            {
                var merged = MergeAllMeshContexts(result.MeshContexts, document.FileName ?? "Merged");
                result.MeshContexts.Clear();
                result.MeshContexts.Add(merged);
            }

            // 親子関係を計算（DepthからParentIndexを算出）
            CalculateParentIndices(result.MeshContexts);
        }

        /// <summary>
        /// Depth値から親子関係（ParentIndex）を計算
        /// MQOのDepth値はリスト順序に依存するため、インポート時に親子関係を確定させる
        /// </summary>
        private static void CalculateParentIndices(List<MeshContext> meshContexts)
        {
            if (meshContexts == null || meshContexts.Count == 0)
                return;

            // スタック: (インデックス, Depth) を保持
            // 現在のDepth以下の最も近い親を見つけるために使用
            var parentStack = new Stack<(int index, int depth)>();

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                int currentDepth = ctx.Depth;

                if (currentDepth == 0)
                {
                    // ルートオブジェクト
                    ctx.ParentIndex = -1;
                    parentStack.Clear();
                    parentStack.Push((i, currentDepth));
                }
                else
                {
                    // 現在のDepthより小さいDepthを持つ最も近い親を探す
                    while (parentStack.Count > 0 && parentStack.Peek().depth >= currentDepth)
                    {
                        parentStack.Pop();
                    }

                    if (parentStack.Count > 0)
                    {
                        ctx.ParentIndex = parentStack.Peek().index;
                    }
                    else
                    {
                        // 親が見つからない場合はルート扱い
                        ctx.ParentIndex = -1;
                    }

                    parentStack.Push((i, currentDepth));
                }
            }
        }

        // ================================================================
        // オブジェクト変換
        // ================================================================

        private static MeshContext ConvertObject(
            MQOObject mqoObj,
            List<MQOMaterial> mqoMaterials,
            List<Material> unityMaterials,
            MQOImportSettings settings,
            MQOImportStats stats)
        {
            var meshObject = new MeshObject();

            // 頂点変換
            foreach (var mqoVert in mqoObj.Vertices)
            {
                Vector3 pos = ConvertPosition(mqoVert.Position, settings);
                meshObject.AddVertex(pos);
                stats.TotalVertices++;
            }

            // 面変換
            foreach (var mqoFace in mqoObj.Faces)
            {
                // 特殊面（メタデータ）はスキップ
                if (mqoFace.IsSpecialFace)
                {
                    stats.SkippedSpecialFaces++;
                    continue;
                }

                // 1頂点（点）、2頂点（線）は補助線として扱う
                if (mqoFace.VertexCount < 3)
                {
                    ConvertLine(mqoFace, meshObject, settings);
                    continue;
                }

                // 3頂点以上は面として変換
                ConvertFace(mqoFace, meshObject, settings);
                stats.TotalFaces++;
            }

            // OriginalPositions作成
            var originalPositions = new Vector3[meshObject.VertexCount];
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                originalPositions[i] = meshObject.Vertices[i].Position;
            }

            // MeshContext作成
            var meshContext = new MeshContext
            {
                Name = mqoObj.Name,
                MeshObject = meshObject,
                OriginalPositions = originalPositions,
                Materials = new List<Material>(),
                // オブジェクト属性をコピー
                Depth = mqoObj.Depth,
                IsVisible = mqoObj.IsVisible,
                IsLocked = mqoObj.IsLocked,
                // ミラー設定をコピー
                MirrorType = mqoObj.MirrorMode,
                MirrorAxis = mqoObj.MirrorAxis,
                MirrorDistance = mqoObj.MirrorDistance
            };

            // マテリアル設定
            // Phase 5: マテリアルはMQOImportResultにグローバルリストとして保存される
            // MeshContext.Materialsへの設定は不要（ModelContext.Materialsで管理）
            // 代わりに、ToUnityMeshにマテリアル数を渡してサブメッシュを正しく生成

            // 使用されているマテリアルの最大インデックスを取得
            int maxMaterialIndex = 0;
            foreach (var face in meshObject.Faces)
            {
                if (face.MaterialIndex > maxMaterialIndex)
                    maxMaterialIndex = face.MaterialIndex;
            }
            int materialCount = settings.ImportMaterials && unityMaterials.Count > 0
                ? unityMaterials.Count
                : maxMaterialIndex + 1;

            // メッシュ名を設定
            meshObject.Name = mqoObj.Name;

            // Unity Mesh生成（マテリアル数を渡す）
            meshContext.UnityMesh = meshObject.ToUnityMesh(materialCount);
            /*
            // デバッグ出力（ミラー属性確認用）
            Debug.Log($"[MQOImporter] ConvertObject: {mqoObj.Name}");
            Debug.Log($"  - MeshObject: V={meshObject.VertexCount}, F={meshObject.FaceCount}");
            Debug.Log($"  - MQO Mirror: Mode={mqoObj.MirrorMode}, Axis={mqoObj.MirrorAxis}, Dist={mqoObj.MirrorDistance}");
            Debug.Log($"  - MeshUndoContext: IsMirrored={meshContext.IsMirrored}, MirrorType={meshContext.MirrorType}, MirrorAxis={meshContext.MirrorAxis}");
            Debug.Log($"  - UnityMesh: V={meshContext.UnityMesh?.vertexCount ?? 0}, T={meshContext.UnityMesh?.triangles?.Length ?? 0}");
            */
            return meshContext;
        }

        // ================================================================
        // 面変換
        // ================================================================

        private static void ConvertFace(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            int vertexCount = mqoFace.VertexCount;

            // 頂点インデックス
            var vertexIndices = new List<int>(mqoFace.VertexIndices);

            // UVサブインデックスを計算
            var uvSubIndices = new List<int>();
            for (int i = 0; i < vertexCount; i++)
            {
                int vertIndex = mqoFace.VertexIndices[i];
                Vector2 uv = (mqoFace.UVs != null && i < mqoFace.UVs.Length)
                    ? ConvertUV(mqoFace.UVs[i], settings)
                    : Vector2.zero;

                // 頂点にUVを追加し、サブインデックスを取得
                var vertex = meshObject.Vertices[vertIndex];
                int uvSubIndex = AddOrGetUVIndex(vertex, uv);
                uvSubIndices.Add(uvSubIndex);
            }

            // Face作成
            var face = new Face
            {
                MaterialIndex = mqoFace.MaterialIndex >= 0 ? mqoFace.MaterialIndex : 0
            };

            // 頂点とUVサブインデックスを追加
            for (int i = 0; i < vertexCount; i++)
            {
                face.VertexIndices.Add(vertexIndices[i]);
                face.UVIndices.Add(uvSubIndices[i]);
                face.NormalIndices.Add(0); // 後で計算
            }

            meshObject.Faces.Add(face);

            // 法線計算
            CalculateFaceNormal(face, meshObject);
        }

        private static void ConvertLine(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            if (mqoFace.VertexCount < 2) return;

            // 2頂点の補助線として追加
            var face = new Face
            {
                MaterialIndex = 0
            };

            for (int i = 0; i < mqoFace.VertexCount; i++)
            {
                face.VertexIndices.Add(mqoFace.VertexIndices[i]);
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        private static Material ConvertMaterial(MQOMaterial mqoMat, MQOImportSettings settings)
        {
            // URPシェーダーを優先
            Shader shader = FindBestShader();
            var material = new Material(shader);
            material.name = mqoMat.Name;

            // 色設定
            Color color = mqoMat.Color;
            SetMaterialColor(material, color);

            // その他のプロパティ
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", mqoMat.Specular);

            // TODO: テクスチャ読み込み（パスが相対パスの場合の解決）

            return material;
        }

        private static Shader FindBestShader()
        {
            // 優先順位でシェーダーを探す
            string[] shaderNames = new[]
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "HDRP/Lit",
                "Standard",
                "Unlit/Color"
            };

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                    return shader;
            }

            return Shader.Find("Standard");
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private static Material CreateDefaultMaterial()
        {
            var shader = FindBestShader();
            var material = new Material(shader);
            material.name = "Default";
            SetMaterialColor(material, new Color(0.7f, 0.7f, 0.7f, 1f));
            return material;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 mqoPos, MQOImportSettings settings)
        {
            // MQO座標系 → Unity座標系
            float x = mqoPos.x * settings.Scale;
            float y = mqoPos.y * settings.Scale;
            float z = mqoPos.z * settings.Scale;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z);
        }

        private static Vector2 ConvertUV(Vector2 mqoUV, MQOImportSettings settings)
        {
            // MQOのUVはそのまま使用（必要に応じてV反転）
            if (settings.FlipUV_V)
                return new Vector2(mqoUV.x, 1f - mqoUV.y);
            return mqoUV;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static int AddOrGetUVIndex(Vertex vertex, Vector2 uv)
        {
            // 既存のUVを検索
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                if (Vector2.Distance(vertex.UVs[i], uv) < 0.0001f)
                    return i;
            }

            // 新規追加
            vertex.UVs.Add(uv);
            vertex.Normals.Add(Vector3.zero); // 後で計算
            return vertex.UVs.Count - 1;
        }

        private static void CalculateFaceNormal(Face face, MeshObject meshObject)
        {
            if (face.VertexCount < 3) return;

            // 最初の3頂点から法線計算
            Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;

            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;

            // 各頂点の法線を更新
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vertIndex = face.VertexIndices[i];
                int normalSubIndex = face.NormalIndices[i];

                var vertex = meshObject.Vertices[vertIndex];

                // 法線リストを確保
                while (vertex.Normals.Count <= normalSubIndex)
                    vertex.Normals.Add(Vector3.zero);

                // 法線を蓄積（後でスムージング可能）
                vertex.Normals[normalSubIndex] = normal;
            }
        }

        private static MeshContext MergeAllMeshContexts(List<MeshContext> meshContexts, string name)
        {
            // TODO: 複数MeshContextを1つに統合
            // 現時点では最初のものを返す
            if (meshContexts.Count == 0)
                return null;

            var merged = meshContexts[0];
            merged.Name = name;
            return merged;
        }
    }
}