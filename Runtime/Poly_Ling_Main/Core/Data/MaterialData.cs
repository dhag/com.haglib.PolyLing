// Assets/Editor/Poly_Ling/Materials/MaterialData.cs
// マテリアルパラメータをデータとして保持
// シリアライズ可能、シェーダー非依存

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Materials
{
    /// <summary>
    /// 対応シェーダー種別
    /// </summary>
    /// <remarks>
    /// 【値の固定】リモート転送（RemoteProgressiveSerializer）で (byte) として送出するため、
    /// 既存メンバの番号は変更しないこと。追加は必ず末尾に行う。
    /// </remarks>
    public enum ShaderType
    {
        URPLit = 0,           // Universal Render Pipeline/Lit
        URPUnlit = 1,         // Universal Render Pipeline/Unlit
        URPSimpleLit = 2,     // Universal Render Pipeline/Simple Lit
        StandardLit = 3,      // Standard (Built-in)
        StandardUnlit = 4,    // Unlit/Color, Unlit/Texture
        Unknown = 5,          // 非対応（フォールバック）
        MToon = 6,            // VRM MToon
        Custom = 7            // ShaderName で解決する任意シェーダー
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

        /// <summary>
        /// シェーダー名（"Universal Render Pipeline/Lit" 等）。
        /// ShaderType == Custom のときの解決先。それ以外では抽出元シェーダー名の記録用で、
        /// ShaderType から解決できない場合のフォールバックにのみ使う。空=未設定。
        /// </summary>
        public string ShaderName;

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
        // テクスチャST（tiling / offset）
        //   [0]=tilingX, [1]=tilingY, [2]=offsetX, [3]=offsetY
        // ================================================================

        /// <summary>ベースマップのST</summary>
        public float[] BaseMapST = new float[] { 1f, 1f, 0f, 0f };

        /// <summary>法線マップのST</summary>
        public float[] NormalMapST = new float[] { 1f, 1f, 0f, 0f };

        /// <summary>エミッションマップのST</summary>
        public float[] EmissionMapST = new float[] { 1f, 1f, 0f, 0f };

        // ================================================================
        // 描画順・深度・GI・インスタンシング
        // ================================================================

        /// <summary>レンダーキューのオフセット（URP _QueueOffset 相当）。</summary>
        public int RenderQueueOffset = 0;

        /// <summary>
        /// ZWrite の上書き（-1=自動／0=Off／1=On）。
        /// 自動のときはサーフェス設定（Opaque/Transparent）に従う既定挙動を壊さない。
        /// </summary>
        public int ZWriteOverride = -1;

        /// <summary>
        /// ZTest（UnityEngine.Rendering.CompareFunction 値。0=未指定＝シェーダー既定のまま）。
        /// </summary>
        public int ZTest = 0;

        /// <summary>両面GI</summary>
        public bool DoubleSidedGI = false;

        /// <summary>GPUインスタンシング有効</summary>
        public bool EnableGPUInstancing = false;

        // ================================================================
        // シェーダー固有プロパティ（共通コアに無いもの）
        //   URP Lit の細目・MToon・独自シェーダのプロパティをここで受ける。
        //   共通コアで型付きに持つプロパティ名は含めない（二重管理禁止）。
        //   null = シェーダー固有値なし。
        // ================================================================

        /// <summary>シェーダー固有プロパティ（null=なし）</summary>
        public List<MaterialProperty> ShaderProperties = null;

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
        
        /// <summary>ST配列をVector4(tilingX,tilingY,offsetX,offsetY)として取得</summary>
        public static Vector4 GetST(float[] st)
        {
            if (st == null || st.Length < 4) return new Vector4(1f, 1f, 0f, 0f);
            return new Vector4(st[0], st[1], st[2], st[3]);
        }

        /// <summary>ST配列を生成</summary>
        public static float[] MakeST(Vector2 tiling, Vector2 offset)
        {
            return new float[] { tiling.x, tiling.y, offset.x, offset.y };
        }

        /// <summary>シェーダー固有プロパティを名前で取得（無ければnull）</summary>
        public MaterialProperty FindShaderProperty(string name)
        {
            if (ShaderProperties == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < ShaderProperties.Count; i++)
            {
                if (ShaderProperties[i] != null && ShaderProperties[i].Name == name)
                    return ShaderProperties[i];
            }
            return null;
        }

        /// <summary>シェーダー固有プロパティを設定（同名があれば置換、無ければ追加）</summary>
        public void SetShaderProperty(MaterialProperty prop)
        {
            if (prop == null || string.IsNullOrEmpty(prop.Name)) return;
            if (ShaderProperties == null) ShaderProperties = new List<MaterialProperty>();

            for (int i = 0; i < ShaderProperties.Count; i++)
            {
                if (ShaderProperties[i] != null && ShaderProperties[i].Name == prop.Name)
                {
                    ShaderProperties[i] = prop;
                    return;
                }
            }
            ShaderProperties.Add(prop);
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
                AlphaCutoff = this.AlphaCutoff,
                ShaderName = this.ShaderName,
                BaseMapST = CloneST(this.BaseMapST),
                NormalMapST = CloneST(this.NormalMapST),
                EmissionMapST = CloneST(this.EmissionMapST),
                RenderQueueOffset = this.RenderQueueOffset,
                ZWriteOverride = this.ZWriteOverride,
                ZTest = this.ZTest,
                DoubleSidedGI = this.DoubleSidedGI,
                EnableGPUInstancing = this.EnableGPUInstancing,
                ShaderProperties = CloneShaderProperties(this.ShaderProperties)
            };
        }

        private static float[] CloneST(float[] src)
        {
            if (src == null || src.Length < 4) return new float[] { 1f, 1f, 0f, 0f };
            return (float[])src.Clone();
        }

        private static List<MaterialProperty> CloneShaderProperties(List<MaterialProperty> src)
        {
            if (src == null) return null;

            var dst = new List<MaterialProperty>(src.Count);
            for (int i = 0; i < src.Count; i++)
                dst.Add(src[i]?.Clone());
            return dst;
        }
    }
}
