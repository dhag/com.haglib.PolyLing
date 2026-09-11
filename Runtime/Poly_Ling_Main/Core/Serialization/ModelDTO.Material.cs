// ModelDTO.Material.cs
// DTO：マテリアル参照データ。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // マテリアル参照データ
    // ================================================================

    /// <summary>
    /// マテリアル参照のシリアライズ用データ
    /// アセットパス＋パラメータデータを保持
    /// </summary>
    [Serializable]
    public class MaterialReferenceDTO
    {
        /// <summary>マテリアルアセットのパス（あれば）</summary>
        public string assetPath;
        
        /// <summary>パラメータデータ</summary>
        public MaterialDataDTO data;
        
        public static MaterialReferenceDTO Create(string path = null)
        {
            return new MaterialReferenceDTO
            {
                assetPath = path,
                data = new MaterialDataDTO()
            };
        }
    }

    /// <summary>
    /// マテリアルパラメータのシリアライズ用データ
    /// </summary>
    [Serializable]
    public class MaterialDataDTO
    {
        // 基本情報
        public string name = "New Material";
        public string shaderType = "URPLit";
        
        // ベースカラー
        public float[] baseColor = new float[] { 1f, 1f, 1f, 1f };
        public string baseMapPath;
        
        // ソースパス（インポート元のパス、エクスポート時に使用）
        public string sourceTexturePath;
        public string sourceAlphaMapPath;
        public string sourceBumpMapPath;
        
        // PBRパラメータ
        public float metallic = 0f;
        public float smoothness = 0.5f;
        public string metallicMapPath;
        public string normalMapPath;
        public float normalScale = 1f;
        public string occlusionMapPath;
        public float occlusionStrength = 1f;
        
        // エミッション
        public bool emissionEnabled = false;
        public float[] emissionColor = new float[] { 0f, 0f, 0f, 1f };
        public string emissionMapPath;
        
        // レンダリング設定
        public int surface = 0;        // SurfaceType
        public int blendMode = 0;      // BlendModeType
        public int cullMode = 2;       // CullModeType.Back（表面のみ表示）
        public bool alphaClipEnabled = false;
        public float alphaCutoff = 0.5f;

        // ================================================================
        // 共通コア拡張（ST／描画順／深度／GI／インスタンシング）
        // ================================================================

        /// <summary>シェーダー名（Custom時の解決先。空=ShaderTypeから解決）</summary>
        public string shaderName;

        public float[] baseMapST = new float[] { 1f, 1f, 0f, 0f };      // [tilingX,tilingY,offsetX,offsetY]
        public float[] normalMapST = new float[] { 1f, 1f, 0f, 0f };
        public float[] emissionMapST = new float[] { 1f, 1f, 0f, 0f };

        public int renderQueueOffset = 0;
        public int zWriteOverride = -1;     // -1=自動 / 0=Off / 1=On
        public int zTest = 0;               // 0=未指定
        public bool doubleSidedGI = false;
        public bool enableGPUInstancing = false;

        // ================================================================
        // シェーダー固有プロパティ（共通コアに無いもの。null/空=なし）
        // ================================================================

        public List<MaterialPropertyDTO> shaderProperties;
    }

    /// <summary>
    /// シェーダー固有マテリアルプロパティDTO。
    /// POCO は Poly_Ling.Materials.MaterialProperty。変換は ModelSerializer が行う。
    /// </summary>
    [Serializable]
    public class MaterialPropertyDTO
    {
        public string name = "";
        public int kind = 0;                // MaterialPropertyKind
        public float x;
        public float y;
        public float z;
        public float w;
        public string texturePath;
    }
}
