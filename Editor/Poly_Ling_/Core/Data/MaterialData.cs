// Assets/Editor/Poly_Ling/Materials/MaterialData.cs
// マテリアルパラメータをデータとして保持
// シリアライズ可能、シェーダー非依存

using System;
using UnityEngine;

namespace Poly_Ling.Materials
{
    /// <summary>
    /// 対応シェーダー種別
    /// </summary>
    public enum ShaderType
    {
        URPLit,           // Universal Render Pipeline/Lit
        URPUnlit,         // Universal Render Pipeline/Unlit
        URPSimpleLit,     // Universal Render Pipeline/Simple Lit
        StandardLit,      // Standard (Built-in)
        StandardUnlit,    // Unlit/Color, Unlit/Texture
        Unknown           // 非対応（フォールバック）
    }

    /// <summary>
    /// サーフェスタイプ
    /// </summary>
    public enum SurfaceType
    {
        Opaque = 0,
        Transparent = 1
    }

    /// <summary>
    /// ブレンドモード
    /// </summary>
    public enum BlendModeType
    {
        Alpha = 0,
        Premultiply = 1,
        Additive = 2,
        Multiply = 3
    }

    /// <summary>
    /// カリングモード（面の表示設定）
    /// 値はUnityEngine.Rendering.CullModeと一致
    /// </summary>
    public enum CullModeType
    {
        /// <summary>両面表示（カリングなし）</summary>
        Off = 0,
        /// <summary>裏面のみ表示（表面をカリング）- 特殊用途</summary>
        Front = 1,
        /// <summary>表面のみ表示（裏面をカリング）- デフォルト、最も一般的</summary>
        Back = 2
    }

    /// <summary>
    /// マテリアルパラメータデータ
    /// 全シェーダー共通の構造（使わないパラメータは無視）
    /// </summary>
    [Serializable]
    public class MaterialData
    {
        // ================================================================
        // 基本情報
        // ================================================================
        
        /// <summary>マテリアル名</summary>
        public string Name = "New Material";
        
        /// <summary>シェーダー種別</summary>
        public ShaderType ShaderType = ShaderType.URPLit;

        // ================================================================
        // ベースカラー（全シェーダー共通）
        // ================================================================
        
        /// <summary>ベースカラー</summary>
        public float[] BaseColor = new float[] { 1f, 1f, 1f, 1f };
        
        /// <summary>ベースマップ（テクスチャ）アセットパス</summary>
        public string BaseMapPath;

        // ================================================================
        // ソースパス（インポート元のパス、エクスポート時に使用）
        // ================================================================

        /// <summary>ソーステクスチャパス（インポート元のファイルパス）</summary>
        public string SourceTexturePath;

        /// <summary>ソースアルファマップパス</summary>
        public string SourceAlphaMapPath;

        /// <summary>ソースバンプマップパス</summary>
        public string SourceBumpMapPath;

        // ================================================================
        // PBRパラメータ（Lit系のみ）
        // ================================================================
        
        /// <summary>メタリック (0-1)</summary>
        public float Metallic = 0f;
        
        /// <summary>スムースネス (0-1)</summary>
        public float Smoothness = 0.5f;
        
        /// <summary>メタリック/スムースネスマップ アセットパス</summary>
        public string MetallicMapPath;
        
        /// <summary>法線マップ アセットパス</summary>
        public string NormalMapPath;
        
        /// <summary>法線マップスケール</summary>
        public float NormalScale = 1f;
        
        /// <summary>オクルージョンマップ アセットパス</summary>
        public string OcclusionMapPath;
        
        /// <summary>オクルージョン強度</summary>
        public float OcclusionStrength = 1f;

        // ================================================================
        // エミッション
        // ================================================================
        
        /// <summary>エミッション有効</summary>
        public bool EmissionEnabled = false;
        
        /// <summary>エミッションカラー</summary>
        public float[] EmissionColor = new float[] { 0f, 0f, 0f, 1f };
        
        /// <summary>エミッションマップ アセットパス</summary>
        public string EmissionMapPath;

        // ================================================================
        // レンダリング設定
        // ================================================================
        
        /// <summary>サーフェスタイプ</summary>
        public SurfaceType Surface = SurfaceType.Opaque;
        
        /// <summary>ブレンドモード（Transparent時）</summary>
        public BlendModeType BlendMode = BlendModeType.Alpha;
        
        /// <summary>カリングモード</summary>
        public CullModeType CullMode = CullModeType.Back;

        /// <summary>アルファカットオフ有効</summary>
        public bool AlphaClipEnabled = true;// false;
        
        /// <summary>アルファカットオフ値 (0-1)</summary>
        public float AlphaCutoff = 0.5f;

        // ================================================================
        // ヘルパーメソッド
        // ================================================================
        
        /// <summary>BaseColorをUnity Colorとして取得</summary>
        public Color GetBaseColor()
        {
            return new Color(
                BaseColor.Length > 0 ? BaseColor[0] : 1f,
                BaseColor.Length > 1 ? BaseColor[1] : 1f,
                BaseColor.Length > 2 ? BaseColor[2] : 1f,
                BaseColor.Length > 3 ? BaseColor[3] : 1f
            );
        }
        
        /// <summary>BaseColorをUnity Colorから設定</summary>
        public void SetBaseColor(Color color)
        {
            BaseColor = new float[] { color.r, color.g, color.b, color.a };
        }
        
        /// <summary>EmissionColorをUnity Colorとして取得</summary>
        public Color GetEmissionColor()
        {
            return new Color(
                EmissionColor.Length > 0 ? EmissionColor[0] : 0f,
                EmissionColor.Length > 1 ? EmissionColor[1] : 0f,
                EmissionColor.Length > 2 ? EmissionColor[2] : 0f,
                EmissionColor.Length > 3 ? EmissionColor[3] : 1f
            );
        }
        
        /// <summary>EmissionColorをUnity Colorから設定</summary>
        public void SetEmissionColor(Color color)
        {
            EmissionColor = new float[] { color.r, color.g, color.b, color.a };
        }
        
        /// <summary>デフォルト値で初期化されたインスタンスを作成</summary>
        public static MaterialData CreateDefault(string name = "New Material")
        {
            return new MaterialData { Name = name };
        }
        
        /// <summary>ディープコピーを作成</summary>
        public MaterialData Clone()
        {
            return new MaterialData
            {
                Name = this.Name,
                ShaderType = this.ShaderType,
                BaseColor = (float[])this.BaseColor.Clone(),
                BaseMapPath = this.BaseMapPath,
                SourceTexturePath = this.SourceTexturePath,
                SourceAlphaMapPath = this.SourceAlphaMapPath,
                SourceBumpMapPath = this.SourceBumpMapPath,
                Metallic = this.Metallic,
                Smoothness = this.Smoothness,
                MetallicMapPath = this.MetallicMapPath,
                NormalMapPath = this.NormalMapPath,
                NormalScale = this.NormalScale,
                OcclusionMapPath = this.OcclusionMapPath,
                OcclusionStrength = this.OcclusionStrength,
                EmissionEnabled = this.EmissionEnabled,
                EmissionColor = (float[])this.EmissionColor.Clone(),
                EmissionMapPath = this.EmissionMapPath,
                Surface = this.Surface,
                BlendMode = this.BlendMode,
                CullMode = this.CullMode,
                AlphaClipEnabled = this.AlphaClipEnabled,
                AlphaCutoff = this.AlphaCutoff
            };
        }
    }
}
