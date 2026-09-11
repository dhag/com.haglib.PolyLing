// PMXImporter.Mesh.cs
// PMX インポート：頂点・マテリアル・座標の変換と MaterialGroupInfo の構築。
// Runtime/Poly_Ling_Main/PMX/ に配置

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
    public static partial class PMXImporter
    {
        // ================================================================
        // 頂点変換
        // ================================================================

        /// <summary>
        /// 頂点変換（スケールなし - 法線計算用）
        /// </summary>
        private static Vertex ConvertVertexUnscaled(PMXVertex pmxVert, PMXDocument document, PMXImportSettings settings)
        {
            // 座標変換（スケールなし）
            Vector3 pos = ConvertPositionUnscaled(pmxVert.Position, settings);
            Vector3 normal = ConvertNormal(pmxVert.Normal, settings);
            Vector2 uv = ConvertUV(pmxVert.UV, settings);

            var vertex = new Vertex(pos, uv, normal);

            // BoneWeight設定
            if (pmxVert.BoneWeights != null && pmxVert.BoneWeights.Length > 0)
            {
                var boneWeight = new BoneWeight();

                for (int i = 0; i < pmxVert.BoneWeights.Length && i < 4; i++)
                {
                    var pmxBw = pmxVert.BoneWeights[i];
                    int boneIndex = document.GetBoneIndex(pmxBw.BoneName);
                    if (boneIndex < 0) boneIndex = 0;

                    switch (i)
                    {
                        case 0:
                            boneWeight.boneIndex0 = boneIndex;
                            boneWeight.weight0 = pmxBw.Weight;
                            break;
                        case 1:
                            boneWeight.boneIndex1 = boneIndex;
                            boneWeight.weight1 = pmxBw.Weight;
                            break;
                        case 2:
                            boneWeight.boneIndex2 = boneIndex;
                            boneWeight.weight2 = pmxBw.Weight;
                            break;
                        case 3:
                            boneWeight.boneIndex3 = boneIndex;
                            boneWeight.weight3 = pmxBw.Weight;
                            break;
                    }
                }

                vertex.BoneWeight = boneWeight;
            }

            return vertex;
        }

        /// <summary>
        /// 頂点変換（スケール適用あり）
        /// </summary>
        private static Vertex ConvertVertex(PMXVertex pmxVert, PMXDocument document, PMXImportSettings settings)
        {
            // 座標変換
            Vector3 pos = ConvertPosition(pmxVert.Position, settings);
            Vector3 normal = ConvertNormal(pmxVert.Normal, settings);
            Vector2 uv = ConvertUV(pmxVert.UV, settings);

            var vertex = new Vertex(pos, uv, normal);

            // BoneWeight設定
            if (pmxVert.BoneWeights != null && pmxVert.BoneWeights.Length > 0)
            {
                var boneWeight = new BoneWeight();

                for (int i = 0; i < pmxVert.BoneWeights.Length && i < 4; i++)
                {
                    var pmxBw = pmxVert.BoneWeights[i];
                    int boneIndex = document.GetBoneIndex(pmxBw.BoneName);
                    if (boneIndex < 0) boneIndex = 0;

                    switch (i)
                    {
                        case 0:
                            boneWeight.boneIndex0 = boneIndex;
                            boneWeight.weight0 = pmxBw.Weight;
                            break;
                        case 1:
                            boneWeight.boneIndex1 = boneIndex;
                            boneWeight.weight1 = pmxBw.Weight;
                            break;
                        case 2:
                            boneWeight.boneIndex2 = boneIndex;
                            boneWeight.weight2 = pmxBw.Weight;
                            break;
                        case 3:
                            boneWeight.boneIndex3 = boneIndex;
                            boneWeight.weight3 = pmxBw.Weight;
                            break;
                    }
                }

                vertex.BoneWeight = boneWeight;
            }

            return vertex;
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        private static Material ConvertMaterial(PMXMaterial pmxMat, PMXDocument document, PMXImportSettings settings)
        {
            Shader shader = FindBestShader();
            var material = new Material(shader);
            material.name = pmxMat.Name;

            // 両面描画フラグ（DrawFlags bit0 = 1 → 両面）
            bool doubleSide = (pmxMat.DrawFlags & 0x01) != 0;
            if (doubleSide && material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f); // CullMode.Off

            // 拡散色を設定（アルファ値も含む）
            Color color = pmxMat.Diffuse;
            SetMaterialColor(material, color);

            // アルファ処理（4ケース分岐）
            bool hasLowOpacity = color.a < 1f - 0.001f;
            bool hasTexture = !string.IsNullOrEmpty(pmxMat.TexturePath);

            if (hasLowOpacity && hasTexture)
            {
                // ケース4: 材質不透明度 < 1.0 かつ テクスチャあり（競合）
                if (settings.AlphaConflict == AlphaConflictMode.PreferTransparent)
                {
                    // 半透明ブレンディング優先
                    SetMaterialTransparent(material);
                }
                else
                {
                    // テクスチャアルファ優先（AlphaClip）
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
            {
                // PMXのSpecularPowerを0-1に正規化
                float smoothness = Mathf.Clamp01(pmxMat.SpecularPower / 100f);
                material.SetFloat("_Smoothness", smoothness);
            }

            // テクスチャを設定
            string baseDir = GetBaseDirectory(document.FilePath);

            // メインテクスチャ（BaseMap）
            if (!string.IsNullOrEmpty(pmxMat.TexturePath))
            {
                var texture = LoadTexture(pmxMat.TexturePath, baseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BaseMap", "_MainTex", texture);
                    //Debug.Log($"[PMXImporter] Loaded texture: {pmxMat.TexturePath}");
                }
            }

            // スフィアテクスチャ（使用する場合）
            // TODO: スフィアマップ対応（必要に応じて）

            return material;
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
        /// CSVファイルのディレクトリを取得
        /// </summary>
        private static string GetBaseDirectory(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "";

            return Path.GetDirectoryName(filePath);
        }

        /// <summary>
        /// テクスチャパスを絶対パスに解決（SourceTexturePath保存用）
        /// </summary>
        private static string ResolveTextureFullPath(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath)) return null;

            string normalized = texturePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized)) return normalized;

            if (!string.IsNullOrEmpty(baseDir))
            {
                try { return Path.GetFullPath(Path.Combine(baseDir, normalized)).Replace('\\', '/'); }
                catch { return normalized; }
            }
            return normalized;
        }

        /// <summary>
        /// テクスチャを読み込み
        /// </summary>
        private static Texture2D LoadTexture(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            // パス区切り文字を正規化（\ → /）
            string normalizedPath = texturePath.Replace('\\', '/');
            string normalizedBaseDir = baseDir?.Replace('\\', '/') ?? "";

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
                    fullPath = Path.Combine(normalizedBaseDir, normalizedPath).Replace('\\', '/');
                }
                else
                {
                    fullPath = normalizedPath;
                }
            }

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

            // 1. まずEditorBridge経由でAssetDatabaseから読み込みを試す
            Texture2D texture = null;
            if (isInsideAssets)
            {
                texture = Poly_Ling.EditorBridge.PLEditorBridge.I.LoadAssetAtPath<Texture2D>(assetPath);
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

                string[] guids = Poly_Ling.EditorBridge.PLEditorBridge.I.FindAssets($"t:Texture2D {fileNameWithoutExt}",
                    new[] { searchFolder });
                foreach (var guid in guids)
                {
                    string foundPath = Poly_Ling.EditorBridge.PLEditorBridge.I.GUIDToAssetPath(guid);
                    if (Path.GetFileName(foundPath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        texture = Poly_Ling.EditorBridge.PLEditorBridge.I.LoadAssetAtPath<Texture2D>(foundPath);
                        if (texture != null)
                        {
                            //Debug.Log($"[PMXImporter] Texture found in baseDir: {foundPath}");
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
                        //Debug.Log($"[PMXImporter] Texture loaded from file: {fullPath}");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                        //Debug.LogWarning($"[PMXImporter] Failed to load image data: {fullPath}");
                    }
                }
                catch// (System.Exception e)
                {
                    //Debug.LogWarning($"[PMXImporter] Failed to read texture file: {fullPath} - {e.Message}");
                }
            }

            if (texture == null)
            {
                //Debug.LogWarning($"[PMXImporter] Texture not found: {fullPath} (original: {texturePath})");
            }

            return texture;
        }

        /// <summary>
        /// マテリアルにテクスチャを設定
        /// </summary>
        private static void SetMaterialTexture(Material material, string urpPropertyName, string standardPropertyName, Texture texture)
        {
            if (material.HasProperty(urpPropertyName))
            {
                material.SetTexture(urpPropertyName, texture);
            }
            if (material.HasProperty(standardPropertyName))
            {
                material.SetTexture(standardPropertyName, texture);
            }
        }

        private static Shader FindBestShader()
        {
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

        /// <summary>
        /// 座標変換（スケール適用あり）
        /// </summary>
        public static Vector3 ConvertPosition(Vector3 pmxPos, PMXImportSettings settings)
        {
            return AxisFlipOps.Position(settings.Flip, pmxPos, settings.Scale);
        }

        /// <summary>
        /// 座標変換（スケールなし - 法線計算用）
        /// </summary>
        private static Vector3 ConvertPositionUnscaled(Vector3 pmxPos, PMXImportSettings settings)
        {
            return AxisFlipOps.Position(settings.Flip, pmxPos);
        }

        private static Vector3 ConvertNormal(Vector3 pmxNormal, PMXImportSettings settings)
        {
            return AxisFlipOps.Normal(settings.Flip, pmxNormal);
        }

        private static Vector2 ConvertUV(Vector2 pmxUV, PMXImportSettings settings)
        {
            if (settings.FlipUV_V)
                return new Vector2(pmxUV.x, 1f - pmxUV.y);
            return pmxUV;
        }

        // ================================================================
        // MaterialGroupInfo 構築
        // ================================================================

        /// <summary>
        /// 材質グループから MaterialGroupInfo を構築
        /// </summary>
        private static MaterialGroupInfo BuildMaterialGroupInfo(
            List<string> materialNames,
            Dictionary<string, List<PMXFace>> materialToFaces,
            int meshIndex)
        {
            var info = new MaterialGroupInfo
            {
                MaterialNames = new List<string>(materialNames)
            };

            // グループ内の全面から使用頂点を収集
            foreach (var matName in materialNames)
            {
                if (materialToFaces.TryGetValue(matName, out var faces))
                {
                    foreach (var face in faces)
                    {
                        info.UsedVertexIndices.Add(face.VertexIndex1);
                        info.UsedVertexIndices.Add(face.VertexIndex2);
                        info.UsedVertexIndices.Add(face.VertexIndex3);
                    }
                }
            }

            // PMX頂点インデックス → ローカルインデックス のマッピングを構築
            // ConvertMaterialGroup と同じロジック
            var sortedIndices = info.UsedVertexIndices.OrderBy(x => x).ToList();
            for (int i = 0; i < sortedIndices.Count; i++)
            {
                info.PmxToLocalIndex[sortedIndices[i]] = i;
            }

            return info;
        }
    }
}
