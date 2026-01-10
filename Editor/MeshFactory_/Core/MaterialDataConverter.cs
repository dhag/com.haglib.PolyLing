// Assets/Editor/MeshFactory/Materials/MaterialDataConverter.cs
// Material ⇔ MaterialData 変換ユーティリティ
// シェーダー別パラメータマッピング

using System;
using UnityEngine;
using UnityEditor;

namespace MeshFactory.Materials
{
    /// <summary>
    /// Material ⇔ MaterialData 変換
    /// </summary>
    public static class MaterialDataConverter
    {
        // ================================================================
        // シェーダー名定数
        // ================================================================
        
        private const string SHADER_URP_LIT = "Universal Render Pipeline/Lit";
        private const string SHADER_URP_UNLIT = "Universal Render Pipeline/Unlit";
        private const string SHADER_URP_SIMPLE_LIT = "Universal Render Pipeline/Simple Lit";
        private const string SHADER_STANDARD = "Standard";
        private const string SHADER_UNLIT_COLOR = "Unlit/Color";
        private const string SHADER_UNLIT_TEXTURE = "Unlit/Texture";

        // ================================================================
        // プロパティ名定数（URP）
        // ================================================================
        
        // 共通
        private const string PROP_BASE_COLOR = "_BaseColor";
        private const string PROP_BASE_MAP = "_BaseMap";
        
        // PBR (Lit)
        private const string PROP_METALLIC = "_Metallic";
        private const string PROP_SMOOTHNESS = "_Smoothness";
        private const string PROP_METALLIC_GLOSS_MAP = "_MetallicGlossMap";
        private const string PROP_BUMP_MAP = "_BumpMap";
        private const string PROP_BUMP_SCALE = "_BumpScale";
        private const string PROP_OCCLUSION_MAP = "_OcclusionMap";
        private const string PROP_OCCLUSION_STRENGTH = "_OcclusionStrength";
        
        // Emission
        private const string PROP_EMISSION_COLOR = "_EmissionColor";
        private const string PROP_EMISSION_MAP = "_EmissionMap";
        
        // Surface
        private const string PROP_SURFACE = "_Surface";
        private const string PROP_BLEND = "_Blend";
        private const string PROP_CULL = "_Cull";
        private const string PROP_ALPHA_CLIP = "_AlphaClip";
        private const string PROP_CUTOFF = "_Cutoff";

        // ================================================================
        // プロパティ名定数（Standard / Built-in）
        // ================================================================
        
        private const string PROP_STD_COLOR = "_Color";
        private const string PROP_STD_MAIN_TEX = "_MainTex";
        private const string PROP_STD_GLOSSINESS = "_Glossiness";
        private const string PROP_STD_METALLIC_GLOSS_MAP = "_MetallicGlossMap";

        // ================================================================
        // Material → MaterialData
        // ================================================================
        
        /// <summary>
        /// MaterialからMaterialDataを抽出
        /// </summary>
        public static MaterialData FromMaterial(Material mat)
        {
            if (mat == null)
                return MaterialData.CreateDefault();
            
            var data = new MaterialData
            {
                Name = mat.name,
                ShaderType = DetectShaderType(mat)
            };
            
            switch (data.ShaderType)
            {
                case ShaderType.URPLit:
                    ExtractURPLit(mat, data);
                    break;
                case ShaderType.URPUnlit:
                    ExtractURPUnlit(mat, data);
                    break;
                case ShaderType.URPSimpleLit:
                    ExtractURPSimpleLit(mat, data);
                    break;
                case ShaderType.StandardLit:
                    ExtractStandardLit(mat, data);
                    break;
                case ShaderType.StandardUnlit:
                    ExtractStandardUnlit(mat, data);
                    break;
                default:
                    // Unknown: 基本的なパラメータのみ試行
                    ExtractBasic(mat, data);
                    break;
            }
            
            return data;
        }

        // ================================================================
        // MaterialData → Material
        // ================================================================
        
        /// <summary>
        /// MaterialDataからMaterialを生成
        /// </summary>
        public static Material ToMaterial(MaterialData data)
        {
            if (data == null)
                return null;
            
            Shader shader = GetShader(data.ShaderType);
            if (shader == null)
            {
                Debug.LogWarning($"[MaterialDataConverter] Shader not found for {data.ShaderType}, using fallback");
                shader = Shader.Find(SHADER_URP_LIT) ?? Shader.Find(SHADER_STANDARD);
            }
            
            var mat = new Material(shader)
            {
                name = data.Name
            };
            
            switch (data.ShaderType)
            {
                case ShaderType.URPLit:
                    ApplyURPLit(mat, data);
                    break;
                case ShaderType.URPUnlit:
                    ApplyURPUnlit(mat, data);
                    break;
                case ShaderType.URPSimpleLit:
                    ApplyURPSimpleLit(mat, data);
                    break;
                case ShaderType.StandardLit:
                    ApplyStandardLit(mat, data);
                    break;
                case ShaderType.StandardUnlit:
                    ApplyStandardUnlit(mat, data);
                    break;
                default:
                    ApplyBasic(mat, data);
                    break;
            }
            
            return mat;
        }

        // ================================================================
        // シェーダー検出
        // ================================================================
        
        /// <summary>
        /// Materialからシェーダー種別を検出
        /// </summary>
        public static ShaderType DetectShaderType(Material mat)
        {
            if (mat == null || mat.shader == null)
                return ShaderType.Unknown;
            
            string shaderName = mat.shader.name;
            
            if (shaderName == SHADER_URP_LIT)
                return ShaderType.URPLit;
            if (shaderName == SHADER_URP_UNLIT)
                return ShaderType.URPUnlit;
            if (shaderName == SHADER_URP_SIMPLE_LIT)
                return ShaderType.URPSimpleLit;
            if (shaderName == SHADER_STANDARD)
                return ShaderType.StandardLit;
            if (shaderName == SHADER_UNLIT_COLOR || shaderName == SHADER_UNLIT_TEXTURE)
                return ShaderType.StandardUnlit;
            
            return ShaderType.Unknown;
        }
        
        /// <summary>
        /// ShaderTypeからShaderを取得
        /// </summary>
        public static Shader GetShader(ShaderType type)
        {
            return type switch
            {
                ShaderType.URPLit => Shader.Find(SHADER_URP_LIT),
                ShaderType.URPUnlit => Shader.Find(SHADER_URP_UNLIT),
                ShaderType.URPSimpleLit => Shader.Find(SHADER_URP_SIMPLE_LIT),
                ShaderType.StandardLit => Shader.Find(SHADER_STANDARD),
                ShaderType.StandardUnlit => Shader.Find(SHADER_UNLIT_TEXTURE),
                _ => null
            };
        }

        // ================================================================
        // URP Lit 抽出/適用
        // ================================================================
        
        private static void ExtractURPLit(Material mat, MaterialData data)
        {
            // Base
            if (mat.HasProperty(PROP_BASE_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_BASE_COLOR));
            data.BaseMapPath = GetTexturePath(mat, PROP_BASE_MAP);
            
            // PBR
            if (mat.HasProperty(PROP_METALLIC))
                data.Metallic = mat.GetFloat(PROP_METALLIC);
            if (mat.HasProperty(PROP_SMOOTHNESS))
                data.Smoothness = mat.GetFloat(PROP_SMOOTHNESS);
            data.MetallicMapPath = GetTexturePath(mat, PROP_METALLIC_GLOSS_MAP);
            
            // Normal
            data.NormalMapPath = GetTexturePath(mat, PROP_BUMP_MAP);
            if (mat.HasProperty(PROP_BUMP_SCALE))
                data.NormalScale = mat.GetFloat(PROP_BUMP_SCALE);
            
            // Occlusion
            data.OcclusionMapPath = GetTexturePath(mat, PROP_OCCLUSION_MAP);
            if (mat.HasProperty(PROP_OCCLUSION_STRENGTH))
                data.OcclusionStrength = mat.GetFloat(PROP_OCCLUSION_STRENGTH);
            
            // Emission
            data.EmissionEnabled = mat.IsKeywordEnabled("_EMISSION");
            if (mat.HasProperty(PROP_EMISSION_COLOR))
                data.SetEmissionColor(mat.GetColor(PROP_EMISSION_COLOR));
            data.EmissionMapPath = GetTexturePath(mat, PROP_EMISSION_MAP);
            
            // Surface
            ExtractSurfaceSettings(mat, data);
        }
        
        private static void ApplyURPLit(Material mat, MaterialData data)
        {
            // Base
            mat.SetColor(PROP_BASE_COLOR, data.GetBaseColor());
            SetTexture(mat, PROP_BASE_MAP, data.BaseMapPath);
            
            // PBR
            mat.SetFloat(PROP_METALLIC, data.Metallic);
            mat.SetFloat(PROP_SMOOTHNESS, data.Smoothness);
            SetTexture(mat, PROP_METALLIC_GLOSS_MAP, data.MetallicMapPath);
            
            // Normal
            SetTexture(mat, PROP_BUMP_MAP, data.NormalMapPath);
            mat.SetFloat(PROP_BUMP_SCALE, data.NormalScale);
            if (!string.IsNullOrEmpty(data.NormalMapPath))
                mat.EnableKeyword("_NORMALMAP");
            
            // Occlusion
            SetTexture(mat, PROP_OCCLUSION_MAP, data.OcclusionMapPath);
            mat.SetFloat(PROP_OCCLUSION_STRENGTH, data.OcclusionStrength);
            
            // Emission
            if (data.EmissionEnabled)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(PROP_EMISSION_COLOR, data.GetEmissionColor());
                SetTexture(mat, PROP_EMISSION_MAP, data.EmissionMapPath);
            }
            
            // Surface
            ApplySurfaceSettings(mat, data);
        }

        // ================================================================
        // URP Unlit 抽出/適用
        // ================================================================
        
        private static void ExtractURPUnlit(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_BASE_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_BASE_COLOR));
            data.BaseMapPath = GetTexturePath(mat, PROP_BASE_MAP);
            
            ExtractSurfaceSettings(mat, data);
        }
        
        private static void ApplyURPUnlit(Material mat, MaterialData data)
        {
            mat.SetColor(PROP_BASE_COLOR, data.GetBaseColor());
            SetTexture(mat, PROP_BASE_MAP, data.BaseMapPath);
            
            ApplySurfaceSettings(mat, data);
        }

        // ================================================================
        // URP Simple Lit 抽出/適用
        // ================================================================
        
        private static void ExtractURPSimpleLit(Material mat, MaterialData data)
        {
            // Simple Litは基本的にLitと同じプロパティ構造
            ExtractURPLit(mat, data);
        }
        
        private static void ApplyURPSimpleLit(Material mat, MaterialData data)
        {
            ApplyURPLit(mat, data);
        }

        // ================================================================
        // Standard (Built-in) Lit 抽出/適用
        // ================================================================
        
        private static void ExtractStandardLit(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_STD_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_STD_COLOR));
            data.BaseMapPath = GetTexturePath(mat, PROP_STD_MAIN_TEX);
            
            if (mat.HasProperty(PROP_METALLIC))
                data.Metallic = mat.GetFloat(PROP_METALLIC);
            if (mat.HasProperty(PROP_STD_GLOSSINESS))
                data.Smoothness = mat.GetFloat(PROP_STD_GLOSSINESS);
            
            data.NormalMapPath = GetTexturePath(mat, PROP_BUMP_MAP);
            if (mat.HasProperty(PROP_BUMP_SCALE))
                data.NormalScale = mat.GetFloat(PROP_BUMP_SCALE);
            
            data.EmissionEnabled = mat.IsKeywordEnabled("_EMISSION");
            if (mat.HasProperty(PROP_EMISSION_COLOR))
                data.SetEmissionColor(mat.GetColor(PROP_EMISSION_COLOR));
        }
        
        private static void ApplyStandardLit(Material mat, MaterialData data)
        {
            mat.SetColor(PROP_STD_COLOR, data.GetBaseColor());
            SetTexture(mat, PROP_STD_MAIN_TEX, data.BaseMapPath);
            
            mat.SetFloat(PROP_METALLIC, data.Metallic);
            mat.SetFloat(PROP_STD_GLOSSINESS, data.Smoothness);
            
            SetTexture(mat, PROP_BUMP_MAP, data.NormalMapPath);
            mat.SetFloat(PROP_BUMP_SCALE, data.NormalScale);
            
            if (data.EmissionEnabled)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(PROP_EMISSION_COLOR, data.GetEmissionColor());
            }
        }

        // ================================================================
        // Standard Unlit 抽出/適用
        // ================================================================
        
        private static void ExtractStandardUnlit(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_STD_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_STD_COLOR));
            else if (mat.HasProperty(PROP_BASE_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_BASE_COLOR));
            
            data.BaseMapPath = GetTexturePath(mat, PROP_STD_MAIN_TEX);
        }
        
        private static void ApplyStandardUnlit(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_STD_COLOR))
                mat.SetColor(PROP_STD_COLOR, data.GetBaseColor());
            SetTexture(mat, PROP_STD_MAIN_TEX, data.BaseMapPath);
        }

        // ================================================================
        // Basic（Unknown用）
        // ================================================================
        
        private static void ExtractBasic(Material mat, MaterialData data)
        {
            // よく使われるプロパティ名を試行
            if (mat.HasProperty(PROP_BASE_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_BASE_COLOR));
            else if (mat.HasProperty(PROP_STD_COLOR))
                data.SetBaseColor(mat.GetColor(PROP_STD_COLOR));
            
            data.BaseMapPath = GetTexturePath(mat, PROP_BASE_MAP);
            if (string.IsNullOrEmpty(data.BaseMapPath))
                data.BaseMapPath = GetTexturePath(mat, PROP_STD_MAIN_TEX);
        }
        
        private static void ApplyBasic(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_BASE_COLOR))
                mat.SetColor(PROP_BASE_COLOR, data.GetBaseColor());
            else if (mat.HasProperty(PROP_STD_COLOR))
                mat.SetColor(PROP_STD_COLOR, data.GetBaseColor());
            
            if (mat.HasProperty(PROP_BASE_MAP))
                SetTexture(mat, PROP_BASE_MAP, data.BaseMapPath);
            else if (mat.HasProperty(PROP_STD_MAIN_TEX))
                SetTexture(mat, PROP_STD_MAIN_TEX, data.BaseMapPath);
        }

        // ================================================================
        // サーフェス設定（共通）
        // ================================================================
        
        private static void ExtractSurfaceSettings(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_SURFACE))
                data.Surface = (SurfaceType)(int)mat.GetFloat(PROP_SURFACE);
            
            if (mat.HasProperty(PROP_BLEND))
                data.BlendMode = (BlendModeType)(int)mat.GetFloat(PROP_BLEND);
            
            if (mat.HasProperty(PROP_CULL))
                data.CullMode = (CullModeType)(int)mat.GetFloat(PROP_CULL);
            
            if (mat.HasProperty(PROP_ALPHA_CLIP))
                data.AlphaClipEnabled = mat.GetFloat(PROP_ALPHA_CLIP) > 0.5f;
            
            if (mat.HasProperty(PROP_CUTOFF))
                data.AlphaCutoff = mat.GetFloat(PROP_CUTOFF);
        }
        
        private static void ApplySurfaceSettings(Material mat, MaterialData data)
        {
            if (mat.HasProperty(PROP_SURFACE))
                mat.SetFloat(PROP_SURFACE, (float)data.Surface);
            
            if (mat.HasProperty(PROP_BLEND))
                mat.SetFloat(PROP_BLEND, (float)data.BlendMode);
            
            if (mat.HasProperty(PROP_CULL))
                mat.SetFloat(PROP_CULL, (float)data.CullMode);
            
            if (mat.HasProperty(PROP_ALPHA_CLIP))
                mat.SetFloat(PROP_ALPHA_CLIP, data.AlphaClipEnabled ? 1f : 0f);
            
            if (mat.HasProperty(PROP_CUTOFF))
                mat.SetFloat(PROP_CUTOFF, data.AlphaCutoff);
            
            // キーワード設定
            if (data.AlphaClipEnabled)
                mat.EnableKeyword("_ALPHATEST_ON");
            
            if (data.Surface == SurfaceType.Transparent)
            {
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
        }

        // ================================================================
        // テクスチャヘルパー
        // ================================================================
        
        private static string GetTexturePath(Material mat, string propertyName)
        {
            if (!mat.HasProperty(propertyName))
                return null;
            
            var tex = mat.GetTexture(propertyName);
            if (tex == null)
                return null;
            
            return AssetDatabase.GetAssetPath(tex);
        }
        
        private static void SetTexture(Material mat, string propertyName, string path)
        {
            if (!mat.HasProperty(propertyName))
                return;
            
            if (string.IsNullOrEmpty(path))
            {
                mat.SetTexture(propertyName, null);
                return;
            }
            
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            mat.SetTexture(propertyName, tex);
        }
    }
}
