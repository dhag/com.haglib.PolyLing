// Assets/Editor/Poly_Ling/MQO/Export/MQOExporter.cs
// MQOエクスポーター
// Phase 5: ModelContext対応

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
    /// MQOエクスポーター
    /// </summary>
    public static class MQOExporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ModelContextをMQOファイルに出力（推奨）
        /// Phase 5: ModelContext.Materialsを使用
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            ModelContext model,
            MQOExportSettings settings = null)
        {
            if (model == null)
            {
                return new MQOExportResult
                {
                    Success = false,
                    ErrorMessage = "Model is null"
                };
            }

            return ExportFile(filePath, model.MeshContextList, model.Materials, settings, model.MaterialReferences);
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（マテリアルリスト指定）
        /// Phase 5: グローバルマテリアルリストを明示的に指定
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            IList<Material> materials,
            MQOExportSettings settings = null,
            IList<Poly_Ling.Materials.MaterialReference> materialRefs = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（マテリアルリストとMaterialReferencesを渡す）
                var document = ConvertToDocument(meshContexts, materials, settings, result.Stats, materialRefs);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（後方互換）
        /// 注意: MeshContext.Materialsを使用（Modelが設定されていればModelContext.Materialsに委譲）
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            MQOExportSettings settings = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（後方互換モード）
                var document = ConvertToDocumentLegacy(meshContexts, settings, result.Stats);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 単一のMeshContextをMQOファイルに出力
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            MeshContext meshContext,
            MQOExportSettings settings = null)
        {
            return ExportFile(filePath, new[] { meshContext }, settings);
        }

        // ================================================================
        // ドキュメント変換
        // ================================================================

        /// <summary>
        /// MQOドキュメント変換（Phase 5: グローバルマテリアルリスト対応）
        /// </summary>
        private static MQODocument ConvertToDocument(
            IList<MeshContext> meshContexts,
            IList<Material> materials,
            MQOExportSettings settings,
            MQOExportStats stats,
            IList<Poly_Ling.Materials.MaterialReference> materialRefs = null)
        {
            var document = new MQODocument
            {
                Version = 1.1m,
            };

            // デフォルトシーン情報
            document.Scene = CreateDefaultScene();

            // 使用されているマテリアルインデックスを収集
            var usedMaterialIndices = new HashSet<int>();
            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;
                foreach (var face in mc.MeshObject.Faces)
                {
                    if (face.MaterialIndex >= 0)
                    {
                        usedMaterialIndices.Add(face.MaterialIndex);
                    }
                }
            }

            // マテリアル設定（グローバルマテリアルリストから）
            // oldIndex -> newIndex のマッピング
            var materialIndexMap = new Dictionary<int, int>();
            
            if (settings.ExportMaterials && materials != null && materials.Count > 0)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    var mat = materials[i];
                    var matRef = (materialRefs != null && i < materialRefs.Count) ? materialRefs[i] : null;
                    
                    // 未使用ミラーマテリアル除外チェック
                    if (settings.ExcludeUnusedMirrorMaterials)
                    {
                        string matName = mat?.name ?? matRef?.Data?.Name ?? "";
                        bool isMirrorMaterial = matName.EndsWith("+");
                        bool isUsed = usedMaterialIndices.Contains(i);
                        
                        if (isMirrorMaterial && !isUsed)
                        {
                            // 未使用のミラーマテリアルはスキップ
                            continue;
                        }
                    }
                    
                    // マッピングを記録
                    materialIndexMap[i] = document.Materials.Count;
                    
                    if (mat != null)
                    {
                        document.Materials.Add(ConvertMaterial(mat, settings.TextureFolder, matRef));
                    }
                    else
                    {
                        // nullマテリアルの場合はデフォルトを追加
                        document.Materials.Add(new MQOMaterial
                        {
                            Name = $"Material{i}",
                            Color = Color.white,
                            Diffuse = 0.8f,
                            Ambient = 0.6f,
                        });
                    }
                }
            }

            // デフォルトマテリアルがない場合は追加
            if (document.Materials.Count == 0)
            {
                document.Materials.Add(new MQOMaterial
                {
                    Name = "Default",
                    Color = Color.white,
                    Diffuse = 0.8f,
                    Ambient = 0.6f,
                });
            }

            // オブジェクト変換（Phase 5: マテリアルインデックスマップを渡す）
            int materialCount = document.Materials.Count;
            
            if (settings.MergeObjects)
            {
                // 全メッシュを統合
                var merged = MergeMeshContexts(meshContexts, "MergedObject");
                var mqoObj = ConvertObject(merged, materialCount, settings, stats, materialIndexMap);
                if (mqoObj != null)
                {
                    document.Objects.Add(mqoObj);
                }
            }
            else
            {
                // 個別にエクスポート
                foreach (var mc in meshContexts)
                {
                    if (mc == null) continue;

                    // ベイクミラースキップ（SkipBakedMirrorがtrueかつType=BakedMirrorの場合）
                    if (settings.SkipBakedMirror && mc.Type == MeshType.BakedMirror)
                    {
                        continue;
                    }

                    // 空オブジェクトスキップ（SkipEmptyObjectsがtrueかつMeshObjectがnullまたは空の場合）
                    if (settings.SkipEmptyObjects)
                    {
                        if (mc.MeshObject == null ||
                            (mc.MeshObject.VertexCount == 0 && mc.MeshObject.FaceCount == 0))
                        {
                            continue;
                        }
                    }

                    var mqoObj = ConvertObject(mc, materialCount, settings, stats, materialIndexMap);
                    if (mqoObj != null)
                    {
                        document.Objects.Add(mqoObj);
                    }
                }
            }

            return document;
        }

        /// <summary>
        /// MQOドキュメント変換（後方互換: MeshContext.Materialsを使用）
        /// </summary>
        private static MQODocument ConvertToDocumentLegacy(
            IList<MeshContext> meshContexts,
            MQOExportSettings settings,
            MQOExportStats stats)
        {
            var document = new MQODocument
            {
                Version = 1.1m,
            };

            // デフォルトシーン情報
            document.Scene = CreateDefaultScene();

            // マテリアル収集（MeshContext.Materialsから）
            var materialMap = new Dictionary<Material, int>();
            if (settings.ExportMaterials)
            {
                foreach (var mc in meshContexts)
                {
                    if (mc?.Materials == null) continue;
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !materialMap.ContainsKey(mat))
                        {
                            materialMap[mat] = document.Materials.Count;
                            document.Materials.Add(ConvertMaterial(mat, settings.TextureFolder));
                        }
                    }
                }
            }

            // デフォルトマテリアルがない場合は追加
            if (document.Materials.Count == 0)
            {
                document.Materials.Add(new MQOMaterial
                {
                    Name = "Default",
                    Color = Color.white,
                    Diffuse = 0.8f,
                    Ambient = 0.6f,
                });
            }

            // オブジェクト変換（Legacy版もマテリアル数を渡す）
            int materialCount = document.Materials.Count;
            
            if (settings.MergeObjects)
            {
                // 全メッシュを統合
                var merged = MergeMeshContexts(meshContexts, "MergedObject");
                var mqoObj = ConvertObject(merged, materialCount, settings, stats);
                if (mqoObj != null)
                {
                    document.Objects.Add(mqoObj);
                }
            }
            else
            {
                // 個別にエクスポート
                foreach (var mc in meshContexts)
                {
                    if (mc == null) continue;

                    // ベイクミラースキップ（SkipBakedMirrorがtrueかつType=BakedMirrorの場合）
                    if (settings.SkipBakedMirror && mc.Type == MeshType.BakedMirror)
                    {
                        continue;
                    }

                    // 空オブジェクトスキップ（SkipEmptyObjectsがtrueかつMeshObjectがnullまたは空の場合）
                    if (settings.SkipEmptyObjects)
                    {
                        if (mc.MeshObject == null || 
                            (mc.MeshObject.VertexCount == 0 && mc.MeshObject.FaceCount == 0))
                        {
                            continue;
                        }
                    }

                    var mqoObj = ConvertObject(mc, materialCount, settings, stats);
                    if (mqoObj != null)
                    {
                        document.Objects.Add(mqoObj);
                    }
                }
            }

            return document;
        }

        private static MQOScene CreateDefaultScene()
        {
            var scene = new MQOScene();
            scene.Attributes.Add(new MQOAttribute("pos", 0, 0, 1500));
            scene.Attributes.Add(new MQOAttribute("lookat", 0, 0, 0));
            scene.Attributes.Add(new MQOAttribute("head", -0.5236f));
            scene.Attributes.Add(new MQOAttribute("pich", 0.5236f));
            scene.Attributes.Add(new MQOAttribute("ortho", 0));
            scene.Attributes.Add(new MQOAttribute("zoom2", 5));
            scene.Attributes.Add(new MQOAttribute("amb", 0.25f, 0.25f, 0.25f));
            return scene;
        }

        private static MQOMaterial ConvertMaterial(Material mat, string textureFolder, Poly_Ling.Materials.MaterialReference matRef = null)
        {
            var mqoMat = new MQOMaterial
            {
                Name = mat.name,
                Color = mat.HasProperty("_Color") ? mat.color : Color.white,
                Diffuse = 0.8f,
                Ambient = 0.6f,
                Specular = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor").grayscale : 0f,
                Power = mat.HasProperty("_Shininess") ? mat.GetFloat("_Shininess") : 5f,
            };

            // ソーステクスチャパスを優先的に使用（インポート時のパスを保持）
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceTexturePath))
            {
                mqoMat.TexturePath = matRef.Data.SourceTexturePath;
            }
            // フォールバック: 現在のテクスチャから生成
            else if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
            {
                string texName = mat.mainTexture.name;
                // 拡張子がなければ.pngを追加
                if (!texName.Contains("."))
                {
                    texName += ".png";
                }
                // フォルダパスを付加
                if (!string.IsNullOrEmpty(textureFolder))
                {
                    // フォルダパスの末尾にスラッシュがなければ追加
                    string folder = textureFolder;
                    if (!folder.EndsWith("/") && !folder.EndsWith("\\"))
                    {
                        folder += "/";
                    }
                    texName = folder + texName;
                }
                mqoMat.TexturePath = texName;
            }

            // バンプマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceBumpMapPath))
            {
                mqoMat.BumpMapPath = matRef.Data.SourceBumpMapPath;
            }

            // アルファマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceAlphaMapPath))
            {
                mqoMat.AlphaMapPath = matRef.Data.SourceAlphaMapPath;
            }

            return mqoMat;
        }

        /// <summary>
        /// MeshContextをMQOObjectに変換
        /// Phase 5: materialMapの代わりにmaterialCountを使用
        /// </summary>
        private static MQOObject ConvertObject(
            MeshContext meshContext,
            int materialCount,
            MQOExportSettings settings,
            MQOExportStats stats,
            Dictionary<int, int> materialIndexMap = null)
        {
            var meshObject = meshContext.MeshObject;
            
            // 空オブジェクトでも属性は出力する（階層構造保持のため）
            var mqoObj = new MQOObject
            {
                Name = meshContext.Name ?? "Object",
            };

            // 属性設定（オブジェクト属性を保持する場合）
            if (settings.PreserveObjectAttributes)
            {
                // depth（階層深度）
                if (meshContext.Depth > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("depth", meshContext.Depth));
                }
                
                // folding（折りたたみ状態）
                if (meshContext.IsFolding)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("folding", 1));
                }
                
                // visible（表示状態）
                // MQOのvisible: 15=表示, 0=非表示
                int visibleValue = meshContext.IsVisible ? 15 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("visible", visibleValue));
                
                // locking（ロック状態）
                int lockingValue = meshContext.IsLocked ? 1 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("locking", lockingValue));
                
                // mirror（ミラー設定）
                if (meshContext.MirrorType > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("mirror", meshContext.MirrorType));
                    mqoObj.Attributes.Add(new MQOAttribute("mirror_axis", meshContext.MirrorAxis));
                    if (meshContext.MirrorDistance != 0f)
                    {
                        mqoObj.Attributes.Add(new MQOAttribute("mirror_dis", meshContext.MirrorDistance));
                    }
                }
            }
            else
            {
                // デフォルト属性
                mqoObj.Attributes.Add(new MQOAttribute("visible", 15));
                mqoObj.Attributes.Add(new MQOAttribute("locking", 0));
            }
            
            // 共通属性
            mqoObj.Attributes.Add(new MQOAttribute("shading", 1));
            mqoObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            mqoObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
            mqoObj.Attributes.Add(new MQOAttribute("color_type", 0));

            // MeshObjectがある場合のみ頂点・面を出力
            if (meshObject != null)
            {
                // 頂点変換
                foreach (var v in meshObject.Vertices)
                {
                    var mqoVert = new MQOVertex
                    {
                        Position = ConvertPosition(v.Position, settings),
                        Index = mqoObj.Vertices.Count,
                    };
                    mqoObj.Vertices.Add(mqoVert);
                    stats.TotalVertices++;
                }

                // 面変換
                foreach (var face in meshObject.Faces)
                {
                    if (face.VertexIndices == null || face.VertexIndices.Count == 0)
                        continue;

                    // マテリアルインデックスを変換（マッピングがあれば使用）
                    int exportMatIdx = 0;
                    if (face.MaterialIndex >= 0)
                    {
                        if (materialIndexMap != null && materialIndexMap.TryGetValue(face.MaterialIndex, out int mappedIdx))
                        {
                            exportMatIdx = mappedIdx;
                        }
                        else if (face.MaterialIndex < materialCount)
                        {
                            exportMatIdx = face.MaterialIndex;
                        }
                    }

                    var mqoFace = new MQOFace
                    {
                        VertexIndices = face.VertexIndices.ToArray(),
                        MaterialIndex = exportMatIdx,
                    };

                    // UV変換
                    if (face.UVIndices != null && face.UVIndices.Count > 0)
                    {
                        var uvs = new Vector2[face.UVIndices.Count];
                        for (int i = 0; i < face.UVIndices.Count; i++)
                        {
                            int vertIdx = face.VertexIndices[i];
                            int uvIdx = face.UVIndices[i];
                            if (vertIdx >= 0 && vertIdx < meshObject.Vertices.Count)
                            {
                                var vertex = meshObject.Vertices[vertIdx];
                                // UVインデックスが有効範囲内か確認
                                Vector2 uv = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                                    ? vertex.UVs[uvIdx]
                                    : (vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero);
                                uvs[i] = ConvertUV(uv, settings);
                            }
                        }
                        mqoFace.UVs = uvs;
                    }

                    mqoObj.Faces.Add(mqoFace);
                    stats.TotalFaces++;
                }

                // 頂点ID用の特殊面を追加（ID != -1 の頂点のみ）
                // フォーマット: 3 V(idx idx idx) M(0) COL(1 1 vertexId)
                for (int i = 0; i < meshObject.Vertices.Count; i++)
                {
                    var vertex = meshObject.Vertices[i];
                    if (vertex.Id != -1)
                    {
                        var specialFace = new MQOFace
                        {
                            VertexIndices = new int[] { i, i, i },
                            MaterialIndex = 0,
                            VertexColors = new uint[] { 1, 1, (uint)vertex.Id }
                        };
                        mqoObj.Faces.Add(specialFace);
                    }
                }
            }

            return mqoObj;
        }

        /// <summary>
        /// マテリアルインデックスを取得
        /// Phase 5: Face.MaterialIndexはグローバルマテリアルインデックス
        /// </summary>
        private static int GetMaterialIndex(
            MeshContext meshContext,
            int materialIndex,
            Dictionary<Material, int> materialMap)
        {
            // Phase 5以降: Face.MaterialIndexはグローバルインデックス
            // materialMapのサイズ（エクスポートされたマテリアル数）を超えなければそのまま使用
            if (materialIndex >= 0 && materialIndex < materialMap.Count)
            {
                return materialIndex;
            }

            // 範囲外の場合はデフォルト（0）
            return 0;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pos, MQOExportSettings settings)
        {
            // スケール
            pos *= settings.Scale;

            // Y-Z入れ替え（Unity Y-up → MQO Z-up）
            if (settings.SwapYZ)
            {
                pos = new Vector3(pos.x, pos.z, pos.y);
            }

            // Z反転
            if (settings.FlipZ)
            {
                pos.z = -pos.z;
            }

            return pos;
        }

        private static Vector2 ConvertUV(Vector2 uv, MQOExportSettings settings)
        {
            if (settings.FlipUV_V)
            {
                uv.y = 1f - uv.y;
            }
            return uv;
        }

        // ================================================================
        // メッシュ統合
        // ================================================================

        private static MeshContext MergeMeshContexts(
            IList<MeshContext> meshContexts,
            string name)
        {
            var mergedData = new MeshObject(name);
            var mergedMaterials = new List<Material>();

            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;

                int vertexOffset = mergedData.VertexCount;

                // 頂点コピー
                foreach (var v in mc.MeshObject.Vertices)
                {
                    Vector2 uv = v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero;
                    Vector3 normal = v.Normals.Count > 0 ? v.Normals[0] : Vector3.zero;
                    mergedData.AddVertex(v.Position, uv, normal);
                }

                // 面コピー（インデックスオフセット）
                foreach (var face in mc.MeshObject.Faces)
                {
                    var newFace = new Face
                    {
                        MaterialIndex = face.MaterialIndex
                    };
                    foreach (int idx in face.VertexIndices)
                    {
                        newFace.VertexIndices.Add(idx + vertexOffset);
                    }
                    // UVインデックスもコピー（オフセットなし、頂点内インデックスのため）
                    newFace.UVIndices.AddRange(face.UVIndices);
                    newFace.NormalIndices.AddRange(face.NormalIndices);
                    mergedData.AddFace(newFace);
                }

                // マテリアルコピー
                if (mc.Materials != null)
                {
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !mergedMaterials.Contains(mat))
                        {
                            mergedMaterials.Add(mat);
                        }
                    }
                }
            }

            return new MeshContext
            {
                Name = name,
                MeshObject = mergedData,
                Materials = mergedMaterials,
            };
        }

        // ================================================================
        // テキスト生成
        // ================================================================

        private static string GenerateMQOText(MQODocument document, MQOExportSettings settings)
        {
            var sb = new StringBuilder();
            string fmt = $"F{settings.DecimalPrecision}";

            // ヘッダー
            sb.AppendLine("Metasequoia Document");
            sb.AppendLine("Format Text Ver 1.1");
            sb.AppendLine();

            // Scene
            sb.AppendLine("Scene {");
            foreach (var attr in document.Scene.Attributes)
            {
                sb.Append($"\t{attr.Name}");
                foreach (var v in attr.Values)
                {
                    sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("}");

            // Material
            if (document.Materials.Count > 0)
            {
                sb.AppendLine($"Material {document.Materials.Count} {{");
                foreach (var mat in document.Materials)
                {
                    sb.Append($"\t\"{mat.Name}\"");
                    sb.Append($" col({mat.Color.r.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.g.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.b.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.a.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" dif({mat.Diffuse.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" amb({mat.Ambient.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" emi({mat.Emissive.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" spc({mat.Specular.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" power({mat.Power.ToString("F2", CultureInfo.InvariantCulture)})");
                    if (!string.IsNullOrEmpty(mat.TexturePath))
                    {
                        sb.Append($" tex(\"{mat.TexturePath}\")");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("}");
            }

            // Objects
            foreach (var obj in document.Objects)
            {
                sb.AppendLine($"Object \"{obj.Name}\" {{");

                // 属性
                foreach (var attr in obj.Attributes)
                {
                    sb.Append($"\t{attr.Name}");
                    foreach (var v in attr.Values)
                    {
                        if (v == (int)v)
                            sb.Append($" {(int)v}");
                        else
                            sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }

                // 頂点
                sb.AppendLine($"\tvertex {obj.Vertices.Count} {{");
                foreach (var v in obj.Vertices)
                {
                    sb.Append("\t\t");
                    sb.Append(v.Position.x.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.Append(v.Position.y.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.AppendLine(v.Position.z.ToString(fmt, CultureInfo.InvariantCulture));
                }
                sb.AppendLine("\t}");

                // 面
                sb.AppendLine($"\tface {obj.Faces.Count} {{");
                foreach (var face in obj.Faces)
                {
                    sb.Append($"\t\t{face.VertexCount} V(");
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        if (i > 0) sb.Append(" ");
                        sb.Append(face.VertexIndices[i]);
                    }
                    sb.Append(")");

                    if (face.MaterialIndex >= 0)
                    {
                        sb.Append($" M({face.MaterialIndex})");
                    }

                    if (face.UVs != null && face.UVs.Length > 0)
                    {
                        sb.Append(" UV(");
                        for (int i = 0; i < face.UVs.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.UVs[i].x.ToString(fmt, CultureInfo.InvariantCulture));
                            sb.Append(" ");
                            sb.Append(face.UVs[i].y.ToString(fmt, CultureInfo.InvariantCulture));
                        }
                        sb.Append(")");
                    }

                    // COL属性（頂点カラー/頂点ID用）
                    if (face.VertexColors != null && face.VertexColors.Length > 0)
                    {
                        sb.Append(" COL(");
                        for (int i = 0; i < face.VertexColors.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.VertexColors[i]);
                        }
                        sb.Append(")");
                    }

                    sb.AppendLine();
                }
                sb.AppendLine("\t}");

                sb.AppendLine("}");
            }

            sb.AppendLine("Eof");

            return sb.ToString();
        }
    }

    // ================================================================
    // 結果クラス
    // ================================================================

    /// <summary>
    /// エクスポート結果
    /// </summary>
    public class MQOExportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FilePath { get; set; }
        public MQOExportStats Stats { get; } = new MQOExportStats();
    }

    /// <summary>
    /// エクスポート統計
    /// </summary>
    public class MQOExportStats
    {
        public int ObjectCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
    }
}