// Assets/Editor/Poly_Ling/Materials/MaterialDataConverter.cs
// Material ⇔ MaterialData 変換ユーティリティ
// シェーダー別パラメータマッピング

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;

namespace Poly_Ling.Materials
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
        // MToon は RP ごとにシェーダーが分かれる（https://vrm.dev/en/univrm1/material/）。
        //   Built-in : VRM10/MToon10
        //   URP      : VRM10/Universal Render Pipeline/MToon10
        // VRM 0.x 版 MToon（VRM/MToon）に URP 対応は無く、UniVRM 側では unlit へ
        // フォールバックする。URP で使うには VRM-1.0 へマイグレートすること。
        private const string SHADER_MTOON10_URP = "VRM10/Universal Render Pipeline/MToon10";
        private const string SHADER_MTOON10 = "VRM10/MToon10";
        private const string SHADER_MTOON0X = "VRM/MToon";

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
        private const string PROP_QUEUE_OFFSET = "_QueueOffset";
        private const string PROP_ZWRITE = "_ZWrite";
        private const string PROP_ZTEST = "_ZTest";

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
                ShaderType = DetectShaderType(mat),
                ShaderName = mat.shader != null ? mat.shader.name : null
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
                case ShaderType.MToon:
                case ShaderType.Custom:
                    // 共通コアで拾えるものだけ型付きで取り、残りは汎用プロパティで受ける
                    ExtractBasic(mat, data);
                    ExtractSurfaceSettings(mat, data);
                    break;
                default:
                    // Unknown: 基本的なパラメータのみ試行
                    ExtractBasic(mat, data);
                    break;
            }
            
            // 種別によらない共通コア拡張（ST／描画順／深度／GI／インスタンシング）
            ExtractCoreExtras(mat, data);
            
            // 共通コアに無いシェーダー固有プロパティを汎用に取り込む
            ExtractShaderProperties(mat, data);
            
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
            
            Shader shader = ResolveShader(data);
            if (shader == null)
            {
                Debug.LogWarning($"[MaterialDataConverter] Shader not found for {data.ShaderType} ({data.ShaderName}), using fallback");
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
                case ShaderType.MToon:
                case ShaderType.Custom:
                    ApplyBasic(mat, data);
                    ApplySurfaceSettings(mat, data);
                    break;
                default:
                    ApplyBasic(mat, data);
                    break;
            }
            
            // シェーダー固有プロパティ（共通コアより先に流し、コア側の確定値が勝つようにする）
            ApplyShaderProperties(mat, data);
            
            // 種別によらない共通コア拡張。
            // ApplySurfaceSettings が決めた renderQueue / _ZWrite を上書きし得るため、必ず最後に呼ぶ。
            ApplyCoreExtras(mat, data);
            
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
            if (shaderName == SHADER_MTOON10_URP || shaderName == SHADER_MTOON10 || shaderName == SHADER_MTOON0X)
                return ShaderType.MToon;
            
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
                // URP版 → Built-in版 → 0.x版 の固定順。RP は見ない。
                ShaderType.MToon => Shader.Find(SHADER_MTOON10_URP)
                                 ?? Shader.Find(SHADER_MTOON10)
                                 ?? Shader.Find(SHADER_MTOON0X),
                _ => null
            };
        }
        
        /// <summary>
        /// MaterialData からシェーダーを解決する。
        ///
        /// 解決順：
        ///   Custom            … ShaderName のみで解決する（種別に既定シェーダーが無いため）。
        ///   Custom 以外       … ShaderType から解決し、見つからない場合のみ ShaderName を試す。
        /// この順序により、UI で ShaderType を切り替えたとき、記録済みの古い ShaderName に
        /// 引きずられて別シェーダーが選ばれることを防ぐ。
        /// </summary>
        public static Shader ResolveShader(MaterialData data)
        {
            if (data == null) return null;
            
            if (data.ShaderType == ShaderType.Custom)
                return string.IsNullOrEmpty(data.ShaderName) ? null : Shader.Find(data.ShaderName);
            
            var shader = GetShader(data.ShaderType);
            if (shader != null) return shader;
            
            return string.IsNullOrEmpty(data.ShaderName) ? null : Shader.Find(data.ShaderName);
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
            
            // AlphaClip（cutout）キーワード
            if (data.AlphaClipEnabled)
                mat.EnableKeyword("_ALPHATEST_ON");
            else
                mat.DisableKeyword("_ALPHATEST_ON");

            // ================================================================
            // 【重要・再発防止注釈】URP 透明のブレンド状態は必ずここで完結させること。
            //
            //   _SURFACE_TYPE_TRANSPARENT キーワードと renderQueue だけでは
            //   URP の半透明は成立しない。_Surface / RenderType(OverrideTag) /
            //   _SrcBlend / _DstBlend / _ZWrite まで設定して初めてアルファがブレンドされる。
            //   これらを省くと材質は不透明ブレンド(SrcBlend=One,DstBlend=Zero,ZWrite=1)の
            //   ままとなり、「透明が抜けない／半透明面が深度ソートで明滅する」不具合になる
            //   （リモートクライアントの再生成材質で発生した実障害）。
            //
            //   正解の参照実装が同リポジトリに既にある：
            //     PMXImporter.SetMaterialTransparent（Poly_Ling_Main/PMX/PMXImporter.cs:1716-）
            //   本メソッドの透明分岐はそれと同一状態を再現する。
            //   ※ keyword + renderQueue のみの旧実装へ戻さないこと。
            // ================================================================
            if (data.Surface == SurfaceType.Transparent)
            {
                if (mat.HasProperty(PROP_SURFACE)) mat.SetFloat(PROP_SURFACE, 1f); // 1=Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                if (mat.HasProperty(PROP_BLEND))       mat.SetFloat(PROP_BLEND, 0f); // 0=Alpha
                if (mat.HasProperty("_SrcBlend"))      mat.SetFloat("_SrcBlend",      (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))      mat.SetFloat("_DstBlend",      (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite"))        mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_Mode"))          mat.SetFloat("_Mode", 3f); // Standard: 3=Transparent
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 3000
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHABLEND_ON");          // Standard 用
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                // 不透明を明示的に戻す（材質再生成で前回の透明状態が残らないように）。
                if (mat.HasProperty(PROP_SURFACE)) mat.SetFloat(PROP_SURFACE, 0f); // 0=Opaque
                mat.SetOverrideTag("RenderType", data.AlphaClipEnabled ? "TransparentCutout" : "Opaque");
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_Mode"))     mat.SetFloat("_Mode", data.AlphaClipEnabled ? 1f : 0f); // 1=Cutout / 0=Opaque
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = data.AlphaClipEnabled
                    ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest   // 2450（cutout）
                    : (int)UnityEngine.Rendering.RenderQueue.Geometry;   // 2000
            }
        }

        // ================================================================
        // 共通コア拡張（ST／描画順／深度／GI／インスタンシング）
        // ================================================================
        
        private static void ExtractCoreExtras(Material mat, MaterialData data)
        {
            // テクスチャST
            if (mat.HasProperty(PROP_BASE_MAP))
                data.BaseMapST = MaterialData.MakeST(mat.GetTextureScale(PROP_BASE_MAP), mat.GetTextureOffset(PROP_BASE_MAP));
            else if (mat.HasProperty(PROP_STD_MAIN_TEX))
                data.BaseMapST = MaterialData.MakeST(mat.GetTextureScale(PROP_STD_MAIN_TEX), mat.GetTextureOffset(PROP_STD_MAIN_TEX));
            
            if (mat.HasProperty(PROP_BUMP_MAP))
                data.NormalMapST = MaterialData.MakeST(mat.GetTextureScale(PROP_BUMP_MAP), mat.GetTextureOffset(PROP_BUMP_MAP));
            
            if (mat.HasProperty(PROP_EMISSION_MAP))
                data.EmissionMapST = MaterialData.MakeST(mat.GetTextureScale(PROP_EMISSION_MAP), mat.GetTextureOffset(PROP_EMISSION_MAP));
            
            // 描画順
            if (mat.HasProperty(PROP_QUEUE_OFFSET))
                data.RenderQueueOffset = Mathf.RoundToInt(mat.GetFloat(PROP_QUEUE_OFFSET));
            
            // 深度
            //   抽出時は実値をそのまま記録する（-1=自動 は「未設定の新規データ」用の初期値であり、
            //   既存マテリアルから作った MaterialData には常に実値が入る）。
            if (mat.HasProperty(PROP_ZWRITE))
                data.ZWriteOverride = mat.GetFloat(PROP_ZWRITE) > 0.5f ? 1 : 0;
            
            if (mat.HasProperty(PROP_ZTEST))
                data.ZTest = Mathf.RoundToInt(mat.GetFloat(PROP_ZTEST));
            
            // GI／インスタンシング
            data.DoubleSidedGI = mat.doubleSidedGI;
            data.EnableGPUInstancing = mat.enableInstancing;
        }
        
        private static void ApplyCoreExtras(Material mat, MaterialData data)
        {
            // テクスチャST
            ApplyST(mat, PROP_BASE_MAP, data.BaseMapST);
            ApplyST(mat, PROP_STD_MAIN_TEX, data.BaseMapST);
            ApplyST(mat, PROP_BUMP_MAP, data.NormalMapST);
            ApplyST(mat, PROP_EMISSION_MAP, data.EmissionMapST);
            
            // 描画順。ApplySurfaceSettings が確定させた renderQueue にオフセットを加算する。
            if (mat.HasProperty(PROP_QUEUE_OFFSET))
                mat.SetFloat(PROP_QUEUE_OFFSET, data.RenderQueueOffset);
            // renderQueue が -1（＝シェーダー既定に従う）のときは加算しない。
            // -1 に足すと「既定に従う」という意味が壊れ、不正なキュー値になる。
            if (data.RenderQueueOffset != 0 && mat.renderQueue >= 0)
                mat.renderQueue = mat.renderQueue + data.RenderQueueOffset;
            
            // 深度。-1（自動）のときはサーフェス設定が決めた値をそのまま残す。
            if (data.ZWriteOverride >= 0 && mat.HasProperty(PROP_ZWRITE))
                mat.SetFloat(PROP_ZWRITE, data.ZWriteOverride > 0 ? 1f : 0f);
            
            if (data.ZTest > 0 && mat.HasProperty(PROP_ZTEST))
                mat.SetFloat(PROP_ZTEST, data.ZTest);
            
            // GI／インスタンシング
            mat.doubleSidedGI = data.DoubleSidedGI;
            mat.enableInstancing = data.EnableGPUInstancing;
        }
        
        private static void ApplyST(Material mat, string propertyName, float[] st)
        {
            if (st == null || st.Length < 4) return;
            if (!mat.HasProperty(propertyName)) return;
            
            mat.SetTextureScale(propertyName, new Vector2(st[0], st[1]));
            mat.SetTextureOffset(propertyName, new Vector2(st[2], st[3]));
        }

        // ================================================================
        // シェーダー固有プロパティ（汎用）
        // ----------------------------------------------------------------
        // 共通コア（型付きフィールド）で持たないプロパティを、名前・型・値で往復させる。
        //
        // 【二重管理禁止】CoreManagedProperties に載っているプロパティ名は汎用側に入れない。
        //   共通コアと汎用の両方に同じ値が入ると、どちらが正か決められず往復で壊れる。
        //   共通コアへフィールドを昇格させたら、必ずこの集合にも名前を追加すること。
        //
        // 【Editor非依存】Shader.GetPropertyCount / GetPropertyName / GetPropertyType /
        //   GetPropertyFlags は UnityEngine 側のAPIであり、UnityEditor に依存しない。
        // ================================================================
        
        private static readonly HashSet<string> CoreManagedProperties = new HashSet<string>
        {
            // ベース
            PROP_BASE_COLOR, PROP_BASE_MAP, PROP_STD_COLOR, PROP_STD_MAIN_TEX,
            // PBR
            PROP_METALLIC, PROP_SMOOTHNESS, PROP_STD_GLOSSINESS, PROP_METALLIC_GLOSS_MAP,
            PROP_BUMP_MAP, PROP_BUMP_SCALE,
            PROP_OCCLUSION_MAP, PROP_OCCLUSION_STRENGTH,
            // エミッション
            PROP_EMISSION_COLOR, PROP_EMISSION_MAP,
            // サーフェス
            PROP_SURFACE, PROP_BLEND, PROP_CULL, PROP_ALPHA_CLIP, PROP_CUTOFF,
            "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_Mode",
            // 描画順・深度
            PROP_QUEUE_OFFSET, PROP_ZWRITE, PROP_ZTEST
        };
        
        private static void ExtractShaderProperties(Material mat, MaterialData data)
        {
            var shader = mat.shader;
            if (shader == null) { data.ShaderProperties = null; return; }
            
            List<MaterialProperty> list = null;
            int count = shader.GetPropertyCount();
            
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                if (string.IsNullOrEmpty(name)) continue;
                if (CoreManagedProperties.Contains(name)) continue;
                
                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) != 0) continue;
                if ((flags & ShaderPropertyFlags.PerRendererData) != 0) continue;
                if ((flags & ShaderPropertyFlags.NonModifiableTextureData) != 0) continue;
                
                var prop = ExtractOneProperty(mat, name, shader.GetPropertyType(i));
                if (prop == null) continue;
                
                if (list == null) list = new List<MaterialProperty>();
                list.Add(prop);
            }
            
            data.ShaderProperties = list;
        }
        
        private static MaterialProperty ExtractOneProperty(Material mat, string name, ShaderPropertyType type)
        {
            if (!mat.HasProperty(name)) return null;
            
            switch (type)
            {
                case ShaderPropertyType.Color:
                    {
                        var c = mat.GetColor(name);
                        return new MaterialProperty(name, MaterialPropertyKind.Color)
                        { X = c.r, Y = c.g, Z = c.b, W = c.a };
                    }
                case ShaderPropertyType.Vector:
                    {
                        var v = mat.GetVector(name);
                        return new MaterialProperty(name, MaterialPropertyKind.Vector)
                        { X = v.x, Y = v.y, Z = v.z, W = v.w };
                    }
                case ShaderPropertyType.Float:
                    return new MaterialProperty(name, MaterialPropertyKind.Float) { X = mat.GetFloat(name) };
                case ShaderPropertyType.Range:
                    return new MaterialProperty(name, MaterialPropertyKind.Range) { X = mat.GetFloat(name) };
                case ShaderPropertyType.Int:
                    return new MaterialProperty(name, MaterialPropertyKind.Int) { X = mat.GetInteger(name) };
                case ShaderPropertyType.Texture:
                    {
                        var scale = mat.GetTextureScale(name);
                        var offset = mat.GetTextureOffset(name);
                        return new MaterialProperty(name, MaterialPropertyKind.Texture)
                        {
                            TexturePath = GetTexturePath(mat, name),
                            X = scale.x,
                            Y = scale.y,
                            Z = offset.x,
                            W = offset.y
                        };
                    }
            }
            return null;
        }
        
        private static void ApplyShaderProperties(Material mat, MaterialData data)
        {
            if (data.ShaderProperties == null) return;
            
            for (int i = 0; i < data.ShaderProperties.Count; i++)
            {
                var prop = data.ShaderProperties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name)) continue;
                if (CoreManagedProperties.Contains(prop.Name)) continue;
                if (!mat.HasProperty(prop.Name)) continue;
                
                switch (prop.Kind)
                {
                    case MaterialPropertyKind.Color:
                        mat.SetColor(prop.Name, new Color(prop.X, prop.Y, prop.Z, prop.W));
                        break;
                    case MaterialPropertyKind.Vector:
                        mat.SetVector(prop.Name, new Vector4(prop.X, prop.Y, prop.Z, prop.W));
                        break;
                    case MaterialPropertyKind.Float:
                    case MaterialPropertyKind.Range:
                        mat.SetFloat(prop.Name, prop.X);
                        break;
                    case MaterialPropertyKind.Int:
                        mat.SetInteger(prop.Name, Mathf.RoundToInt(prop.X));
                        break;
                    case MaterialPropertyKind.Texture:
                        SetTexture(mat, prop.Name, prop.TexturePath);
                        mat.SetTextureScale(prop.Name, new Vector2(prop.X, prop.Y));
                        mat.SetTextureOffset(prop.Name, new Vector2(prop.Z, prop.W));
                        break;
                }
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
            
            return PLEditorBridge.I.GetAssetPath(tex);
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
            
            var tex = PLEditorBridge.I.LoadAssetAtPath<Texture>(path);
            mat.SetTexture(propertyName, tex);
        }
    }
}
