// MQOImporter.Material.cs
// MQO インポート：マテリアル・座標の変換、ヘルパー、頂点デバッグ出力、ベイクミラー生成。
// Runtime/Poly_Ling_Main/MQO/Import/ に配置

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
    public static partial class MQOImporter
    {
        // ================================================================
        // マテリアル変換
        // ================================================================

        private static MaterialReference ConvertMaterialToRef(MQOMaterial mqoMat, MQOImportSettings settings)
        {
            // URPシェーダーを優先
            Shader shader = FindBestShader();
            var material = new Material(shader);
            material.name = mqoMat.Name;

            // 色設定
            Color color = mqoMat.Color;
            SetMaterialColor(material, color);

            // アルファ処理（4ケース分岐）
            bool hasLowOpacity = color.a < 1f - 0.001f;
            bool hasTexture = !string.IsNullOrEmpty(mqoMat.TexturePath);

            if (hasLowOpacity && hasTexture)
            {
                // ケース4: 材質不透明度 < 1.0 かつ テクスチャあり（競合）
                if (settings.AlphaConflict == AlphaConflictMode.PreferTransparent)
                {
                    SetMaterialTransparent(material);
                }
                else
                {
                    SetMaterialAlphaClip(material, settings.AlphaCutoff);
                }
            }
            else if (hasLowOpacity)
            {
                // ケース1: 材質不透明度 < 1.0、テクスチャなし → Transparent
                SetMaterialTransparent(material);
            }
            else if (hasTexture)
            {
                // ケース2: 不透明度 = 1.0、テクスチャあり → AlphaClip
                SetMaterialAlphaClip(material, settings.AlphaCutoff);
            }
            // ケース3: 不透明度 = 1.0、テクスチャなし → Opaque, AlphaClip=OFF（何もしない）





            // その他のプロパティ
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", mqoMat.Specular);

            // テクスチャ読み込み
            if (!string.IsNullOrEmpty(mqoMat.TexturePath))
            {
                var texture = LoadTexture(mqoMat.TexturePath, settings.BaseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BaseMap", "_MainTex", texture);
                }
            }

            // バンプマップ
            if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
            {
                var texture = LoadTexture(mqoMat.BumpMapPath, settings.BaseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BumpMap", "_BumpMap", texture);
                }
            }

            // MaterialReferenceを作成し、ソースパスを設定
            var matRef = new MaterialReference(material);

            // ソースパスを絶対パスで設定
            if (!string.IsNullOrEmpty(mqoMat.TexturePath))
            {
                matRef.Data.SourceTexturePath = ResolveTexturePath(mqoMat.TexturePath, settings.BaseDir);
            }
            if (!string.IsNullOrEmpty(mqoMat.AlphaMapPath))
            {
                matRef.Data.SourceAlphaMapPath = ResolveTexturePath(mqoMat.AlphaMapPath, settings.BaseDir);
            }
            if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
            {
                matRef.Data.SourceBumpMapPath = ResolveTexturePath(mqoMat.BumpMapPath, settings.BaseDir);
            }

            return matRef;
        }

        /// <summary>
        /// テクスチャパスを解決（相対パスならフルパスに変換）
        /// テクスチャ読み込み用
        /// </summary>
        private static string ResolveTexturePath(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            string normalizedPath = texturePath.Replace("\\", "/");

            // 既にフルパスの場合
            if (Path.IsPathRooted(normalizedPath))
                return normalizedPath;

            // 相対パスの場合、baseDirと結合
            if (!string.IsNullOrEmpty(baseDir))
            {
                return Path.GetFullPath(Path.Combine(baseDir, normalizedPath)).Replace("\\", "/");
            }

            return normalizedPath;
        }

        /// <summary>後方互換用：Material を返す</summary>
        private static Material ConvertMaterial(MQOMaterial mqoMat, MQOImportSettings settings)
        {
            return ConvertMaterialToRef(mqoMat, settings).Material;
        }

        /// <summary>
        /// マテリアルを透過モードに設定（URP/Standard両対応）
        /// </summary>
        private static void SetMaterialTransparent(Material material)
        {
            // URP Lit用設定
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1); // 0=Opaque, 1=Transparent
                material.SetOverrideTag("RenderType", "Transparent");
            }
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0); // 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
            }
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_SrcBlendAlpha"))
            {
                material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            }
            if (material.HasProperty("_DstBlendAlpha"))
            {
                material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0);
            }

            // Standard Shader用設定
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3); // 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent
            }

            // レンダーキュー設定
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // キーワード設定（URP）
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            // Standard Shader用キーワード
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        /// <summary>
        /// マテリアルをアルファクリップモードに設定（URP/Standard両対応）
        /// Opaque + AlphaTest で、しきい値以下のピクセルを切り抜く
        /// </summary>
        private static void SetMaterialAlphaClip(Material material, float cutoff)
        {
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", cutoff);
            }
        }

        /// <summary>
        /// テクスチャを読み込み
        /// Assets内 → AssetDatabase、Assets外 → File.ReadAllBytes
        /// </summary>
        private static Texture2D LoadTexture(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            // パス区切り文字を正規化（\ → /）
            // MQOファイルではバックスラッシュが使われることが多い
            string normalizedPath = texturePath.Replace("\\", "/");
            string normalizedBaseDir = baseDir?.Replace("\\", "/") ?? "";

            Debug.Log($"[MQOImporter] LoadTexture: original='{texturePath}', normalized='{normalizedPath}', baseDir='{normalizedBaseDir}'");

            // 実際のファイルパスを構築
            string fullPath;
            if (Path.IsPathRooted(normalizedPath))
            {
                fullPath = normalizedPath;
            }
            else
            {
                if (!string.IsNullOrEmpty(normalizedBaseDir))
                {
                    fullPath = Path.Combine(normalizedBaseDir, normalizedPath).Replace("\\", "/");
                }
                else
                {
                    fullPath = normalizedPath;
                }
            }

            Debug.Log($"[MQOImporter] LoadTexture: fullPath='{fullPath}'");

            // アセットパスを構築（Assets/から始まる形式）
            string assetPath = fullPath;
            bool isInsideAssets = false;
            if (!assetPath.StartsWith("Assets/"))
            {
                int assetsIdx = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIdx >= 0)
                {
                    assetPath = assetPath.Substring(assetsIdx + 1);
                    isInsideAssets = true;
                }
                else
                {
                    assetsIdx = assetPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIdx >= 0)
                    {
                        assetPath = assetPath.Substring(assetsIdx);
                        isInsideAssets = true;
                    }
                }
            }
            else
            {
                isInsideAssets = true;
            }

            // 1. まずAssetDatabaseから読み込みを試す
            Texture2D texture = null;
            if (isInsideAssets)
            {
                texture = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(assetPath);
            }

            // 2. Assets内の場合のみ、同じbaseDir内でファイル名検索
            if (texture == null && isInsideAssets)
            {
                string fileName = Path.GetFileName(normalizedPath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

                // baseDirをAssets/形式に変換
                string searchFolder = normalizedBaseDir;
                if (!searchFolder.StartsWith("Assets/"))
                {
                    int idx = searchFolder.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        searchFolder = searchFolder.Substring(idx);
                    }
                }

                string[] guids = PLEditorBridge.I.FindAssets($"t:Texture2D {fileNameWithoutExt}",
                    new[] { searchFolder });
                foreach (var guid in guids)
                {
                    string foundPath = PLEditorBridge.I.GUIDToAssetPath(guid);
                    if (Path.GetFileName(foundPath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        texture = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(foundPath);
                        if (texture != null)
                        {
                            Debug.Log($"[MQOImporter] Texture found in baseDir: {foundPath}");
                            break;
                        }
                    }
                }
            }
            // 3. それでも失敗した場合、File.ReadAllBytesで直接読み込み
            if (texture == null && File.Exists(fullPath))
            {
                try
                {
                    byte[] fileData = File.ReadAllBytes(fullPath);
                    texture = new Texture2D(2, 2);
                    if (texture.LoadImage(fileData))
                    {
                        texture.name = Path.GetFileNameWithoutExtension(fullPath);
                        Debug.Log($"[MQOImporter] Texture loaded from file: {fullPath}");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                        Debug.LogWarning($"[MQOImporter] Failed to load image data: {fullPath}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MQOImporter] Failed to read texture file: {fullPath} - {e.Message}");
                }
            }

            if (texture == null)
            {
                Debug.LogWarning($"[MQOImporter] Texture not found: {fullPath} (original: {texturePath})");
            }

            return texture;
        }

        /// <summary>
        /// マテリアルにテクスチャを設定
        /// </summary>
        private static void SetMaterialTexture(Material material, string urpPropertyName, string standardPropertyName, Texture texture)
        {
            if (material == null || texture == null) return;

            if (material.HasProperty(urpPropertyName))
            {
                material.SetTexture(urpPropertyName, texture);
            }
            else if (material.HasProperty(standardPropertyName))
            {
                material.SetTexture(standardPropertyName, texture);
            }
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
            // MQO座標系 → Unity座標系。規則は AxisFlipOps に集約。
            return AxisFlipOps.Position(settings.Flip, mqoPos, settings.Scale);
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
            // FPXと同じ比較方法: (uvl - uv).Length() == 0
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                if ((vertex.UVs[i] - uv).magnitude == 0f)
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

            Vector3 normal = NormalHelper.CalculateFaceNormal(p0, p1, p2);

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

        /// <summary>
        /// 頂点法線をスムージング
        /// 同一位置の頂点の法線を平均化（角度閾値付き）
        /// </summary>
        private static void CalculateSmoothNormals(MeshObject meshObject, float smoothingAngle)
        {
            //Debug.Log($"[MQOImporter normal] Smooth normals calculated (angle={smoothingAngle}°)");
            float cosThreshold = Mathf.Cos(smoothingAngle * Mathf.Deg2Rad);

            // 各面の法線を計算して保持
            var faceNormals = new Vector3[meshObject.FaceCount];
            for (int fi = 0; fi < meshObject.FaceCount; fi++)
            {
                var face = meshObject.Faces[fi];

                if (face.VertexCount < 3)
                {
                    faceNormals[fi] = Vector3.up;
                    continue;
                }


                Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
                Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
                Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;
                faceNormals[fi] = NormalHelper.CalculateFaceNormal(p0, p1, p2);
            }

            // 位置→(面インデックス, 頂点インデックス in 面, NormalSubIndex) のマッピング
            var positionToFaceVerts = new Dictionary<Vector3, List<(int faceIdx, int vertInFace, int normalSubIdx)>>();

            for (int fi = 0; fi < meshObject.FaceCount; fi++)
            {
                var face = meshObject.Faces[fi];
                for (int vi = 0; vi < face.VertexCount; vi++)
                {
                    int vertIdx = face.VertexIndices[vi];
                    int normalSubIdx = face.NormalIndices[vi];
                    Vector3 pos = meshObject.Vertices[vertIdx].Position;

                    // 位置をキーにまとめる（微小誤差を許容するため丸める）
                    Vector3 roundedPos = new Vector3(
                        Mathf.Round(pos.x * 10000f) / 10000f,
                        Mathf.Round(pos.y * 10000f) / 10000f,
                        Mathf.Round(pos.z * 10000f) / 10000f
                    );

                    if (!positionToFaceVerts.ContainsKey(roundedPos))
                        positionToFaceVerts[roundedPos] = new List<(int, int, int)>();

                    positionToFaceVerts[roundedPos].Add((fi, vi, normalSubIdx));
                }
            }

            // 各位置で法線をスムージング
            foreach (var kvp in positionToFaceVerts)
            {
                var faceVerts = kvp.Value;
                if (faceVerts.Count <= 1) continue;

                // 各頂点について、角度閾値内の面法線を平均化
                foreach (var (faceIdx, vertInFace, normalSubIdx) in faceVerts)
                {
                    Vector3 baseFaceNormal = faceNormals[faceIdx];
                    Vector3 smoothedNormal = baseFaceNormal;
                    //Debug.Log($"[MQOImporter] Smooth normals calculated (baseFaceNormal={smoothedNormal}°, positions={vertInFace})");

                    foreach (var (otherFaceIdx, _, _) in faceVerts)
                    {
                        if (otherFaceIdx == faceIdx) continue;

                        Vector3 otherFaceNormal = faceNormals[otherFaceIdx];
                        float dot = Vector3.Dot(baseFaceNormal, otherFaceNormal);

                        // 角度閾値内なら平均に加える
                        if (dot >= cosThreshold)
                        {
                            smoothedNormal += otherFaceNormal;
                        }
                    }

                    smoothedNormal = smoothedNormal.normalized;
                    //Debug.Log($"[MQOImporter] Smooth normals calculated (normal={smoothedNormal}°, positions={vertInFace})");


                    // 頂点の法線を更新
                    int vertIdx = meshObject.Faces[faceIdx].VertexIndices[vertInFace];
                    var vertex = meshObject.Vertices[vertIdx];
                    if (normalSubIdx < vertex.Normals.Count)
                    {
                        vertex.Normals[normalSubIdx] = smoothedNormal;
                    }
                }
            }

            Debug.Log($"[MQOImporter] Smooth normals calculated (angle={smoothingAngle}°, positions={positionToFaceVerts.Count})");
        }

        // ================================================================
        // 頂点デバッグ出力
        // ================================================================

        /// <summary>
        /// 頂点デバッグ情報を出力（オブジェクトごとに1つのログでまとめて出力）
        /// MQOの元データから直接抽出
        /// </summary>
        /// <param name="objectName">オブジェクト名</param>
        /// <param name="mqoObj">MQOオブジェクト（元データ）</param>
        /// <param name="meshObject">変換後のメッシュオブジェクト</param>
        /// <param name="nearUVCount">近接UV出力件数</param>
        private static void OutputVertexDebugInfo(string objectName, MQOObject mqoObj, MeshObject meshObject, int nearUVCount)
        {
            int originalVertexCount = mqoObj.Vertices.Count;

            // 展開時の頂点数。数え方は MeshExpansion に一本化してある。
            int expandedVertexCount = MeshExpansion.CountExpanded(meshObject);

            // MQOの面データから頂点ごとのUVを収集
            // Key: 頂点インデックス, Value: その頂点に割り当てられたUVのリスト
            var vertexUVs = new Dictionary<int, List<Vector2>>();

            foreach (var mqoFace in mqoObj.Faces)
            {
                if (mqoFace.IsSpecialFace) continue;
                if (mqoFace.UVs == null) continue;

                for (int i = 0; i < mqoFace.VertexCount && i < mqoFace.UVs.Length; i++)
                {
                    int vertIndex = mqoFace.VertexIndices[i];
                    Vector2 uv = mqoFace.UVs[i]; // MQOの元UV値（変換前）

                    if (!vertexUVs.ContainsKey(vertIndex))
                    {
                        vertexUVs[vertIndex] = new List<Vector2>();
                    }

                    // 同じUVが既にあるかチェック（完全一致）
                    bool found = false;
                    foreach (var existingUV in vertexUVs[vertIndex])
                    {
                        if (existingUV == uv)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        vertexUVs[vertIndex].Add(uv);
                    }
                }
            }

            // 同一頂点で異なるUVを持つペアを収集
            var nearUVPairs = new List<(int vertIndex, int vertexId, Vector2 uv1, Vector2 uv2, float distance)>();

            foreach (var kvp in vertexUVs)
            {
                int vertIndex = kvp.Key;
                var uvList = kvp.Value;

                if (uvList.Count < 2) continue;

                // 頂点IDを取得（meshObjectから）
                int vertexId = (vertIndex < meshObject.Vertices.Count) ? meshObject.Vertices[vertIndex].Id : 0;

                // 全ペアの距離を計算
                for (int i = 0; i < uvList.Count; i++)
                {
                    for (int j = i + 1; j < uvList.Count; j++)
                    {
                        Vector2 uv1 = uvList[i];
                        Vector2 uv2 = uvList[j];
                        float dist = Vector2.Distance(uv1, uv2);

                        nearUVPairs.Add((vertIndex, vertexId, uv1, uv2, dist));
                    }
                }
            }

            // 距離が近い順にソート
            nearUVPairs.Sort((a, b) => a.distance.CompareTo(b.distance));

            // 1つのログにまとめて出力（コピペしやすいように半角スペース区切り）
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VertexDebug] {objectName}");
            sb.AppendLine($"OriginalVertexCount {originalVertexCount}");
            sb.AppendLine($"ExpandedVertexCount {expandedVertexCount}");
            sb.AppendLine($"NearUVPairCount {nearUVPairs.Count}");

            int outputCount = Math.Min(nearUVCount, nearUVPairs.Count);
            if (outputCount > 0)
            {
                sb.AppendLine("VertIndex VertexID U1 V1 U2 V2 Distance");
                for (int i = 0; i < outputCount; i++)
                {
                    var pair = nearUVPairs[i];
                    // MQOと同じ5桁形式（0.12345）で出力して文字列検索可能に
                    string u1 = pair.uv1.x.ToString("0.00000");
                    string v1 = pair.uv1.y.ToString("0.00000");
                    string u2 = pair.uv2.x.ToString("0.00000");
                    string v2 = pair.uv2.y.ToString("0.00000");
                    sb.AppendLine($"{pair.vertIndex} {pair.vertexId} {u1} {v1} {u2} {v2} {pair.distance:F6}");
                }
            }

            Debug.Log(sb.ToString());
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

        // ================================================================
        // ベイクミラー生成
        // ================================================================

        /// <summary>
        /// ミラー属性を持つメッシュからベイクミラーメッシュを生成
        /// </summary>
        /// <param name="source">ソースメッシュコンテキスト</param>
        /// <param name="sourceIndex">ソースのインデックス</param>
        /// <param name="settings">インポート設定</param>
        /// <returns>ベイクミラーメッシュコンテキスト</returns>
        /// <summary>
        /// ミラー属性を持つメッシュからミラー側 MeshContext を生成する。
        /// 実装は MirrorBranchOps.CreateDerivedMirrorContext にある
        /// （ミラーの有効化からも呼ぶため Ops へ移した）。
        /// </summary>
        private static MeshContext CreateBakedMirrorMesh(
            MeshContext source, int sourceIndex, MQOImportSettings settings,
            Func<string, bool> nameExists = null)
        {
            return Poly_Ling.Ops.MirrorBranchOps.CreateDerivedMirrorContext(
                source, sourceIndex, nameExists);
        }
    }
}
