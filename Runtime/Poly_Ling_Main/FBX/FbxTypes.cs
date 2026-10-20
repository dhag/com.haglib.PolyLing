// FbxTypes.cs
// ============================================================
// FBX 読み書きの設定・結果（純POCO）
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md。入口は PLFbxBridge.cs。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using Poly_Ling.Data;
using Poly_Ling.Materials;

namespace Poly_Ling.Fbx
{
    /// <summary>FBX 読み込み設定。</summary>
    [Serializable]
    public class FbxImportSettings
    {
        [PLParam(Description = "ブレンドシェイプをモーフとして読み込む")]
        public bool ImportMorphs = true;

        [PLParam(Description = "材質を読み込む")]
        public bool ImportMaterials = true;

        [PLParam(Description = "テクスチャ（埋め込み・外部ファイル）をフォルダへ書き出して材質に使う")]
        public bool ExtractTextures = true;

        /// <summary>
        /// テクスチャの書き出し先フォルダ。空なら DefaultTextureFolder の場所。
        /// 受け口（コマンド経路）が関門を通して決めるので、外から送るものではない。
        /// </summary>
        [NonSerialized]
        [PLParam(Ignore = true)]
        public string TextureFolder = "";

        /// <summary>既定のテクスチャ書き出し先（FBX と同じフォルダの「ファイル名_textures」）。</summary>
        public static string DefaultTextureFolder(string fbxPath) => FbxExportSettings.DefaultTextureFolder(fbxPath);

        public static FbxImportSettings CreateDefault() => new FbxImportSettings();

        public FbxImportSettings Clone()
        {
            return new FbxImportSettings
            {
                ImportMorphs    = this.ImportMorphs,
                ImportMaterials = this.ImportMaterials,
                ExtractTextures = this.ExtractTextures,
                TextureFolder   = this.TextureFolder,
            };
        }
    }

    /// <summary>
    /// FBX 読み込み結果。ModelContext の材料を持つ。
    /// MeshContexts の並びがそのまま MeshContextList の並びになる（ボーン → 描画オブジェクト → モーフ の順）。
    /// </summary>
    public class FbxImportResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }

        public string TextureFolder { get; set; } = "";

        public List<MeshContext>       MeshContexts       { get; } = new List<MeshContext>();
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();
        public List<MorphExpression>   MorphExpressions   { get; } = new List<MorphExpression>();

        public int    NodeCount          { get; set; }
        public int    BoneCount          { get; set; }
        public int    MeshCount          { get; set; }
        public int    SkinnedMeshCount   { get; set; }
        public int    VertexCount        { get; set; }
        public int    FaceCount          { get; set; }
        public int    MorphCount         { get; set; }
        public int    ExpressionCount    { get; set; }
        public int    MaterialCount      { get; set; }
        public int    TextureCount       { get; set; }
        /// <summary>読み飛ばした入れ物ノードの名前（最上位から続く、メッシュ・スケルトン・バインドを持たないノード）。</summary>
        public List<string> SkippedContainers { get; } = new List<string>();
        public int    OriginalAxisUp     { get; set; }
        public double OriginalUnitMeters { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public static FbxImportResult Failed(string message)
            => new FbxImportResult { Success = false, ErrorMessage = message };

        public string Summary()
            => $"ノード {NodeCount}（入れ物として飛ばした {SkippedContainers.Count}） / ボーン {BoneCount} / " +
               $"メッシュ {MeshCount}（スキン {SkinnedMeshCount}） / 頂点 {VertexCount} / 面 {FaceCount} / " +
               $"モーフ {MorphCount}（表情 {ExpressionCount}） / 材質 {MaterialCount} / テクスチャ {TextureCount} / " +
               $"元の単位 {OriginalUnitMeters} m";
    }

    /// <summary>FBX 書き出し設定。</summary>
    [Serializable]
    public class FbxExportSettings
    {
        [PLParam(Description = "スキニング（ボーンウェイト）を書き出す")]
        public bool ExportSkinning = true;

        [PLParam(Description = "モーフをブレンドシェイプとして書き出す")]
        public bool ExportMorphTargets = true;

        [PLParam(Description = "非表示のメッシュも書き出す")]
        public bool ExportInvisibleObjects = false;

        [PLParam(Description = "テクスチャを FBX の中へ埋め込む（バイナリのときだけ効く）。切ると隣のフォルダの画像を参照するだけになる")]
        public bool EmbedTextures = true;

        [PLParam(Description = "ASCII 形式で書く（既定はバイナリ）")]
        public bool Ascii = false;

        /// <summary>
        /// テクスチャの書き出し先フォルダ。空なら DefaultTextureFolder の場所。
        /// 受け口（コマンド経路）が関門を通して決めるので、外から送るものではない。
        /// </summary>
        [NonSerialized]
        [PLParam(Ignore = true)]
        public string TextureFolder = "";

        /// <summary>既定のテクスチャ書き出し先（FBX と同じフォルダの「ファイル名_textures」）。</summary>
        public static string DefaultTextureFolder(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath)) return "";
            string dir  = Path.GetDirectoryName(fbxPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(fbxPath);
            return Path.Combine(dir, name + "_textures");
        }

        public static FbxExportSettings CreateDefault() => new FbxExportSettings();

        public FbxExportSettings Clone()
        {
            return new FbxExportSettings
            {
                ExportSkinning         = this.ExportSkinning,
                ExportMorphTargets     = this.ExportMorphTargets,
                ExportInvisibleObjects = this.ExportInvisibleObjects,
                EmbedTextures          = this.EmbedTextures,
                Ascii                  = this.Ascii,
                TextureFolder          = this.TextureFolder,
            };
        }
    }

    /// <summary>
    /// FBX のモーション書き出し設定（モーションをモデルへ当てて標本化する。ModelMotionSampler）。
    /// 外から送る値はコマンド（ExportMotionToFbxCommand）が持つので PLParam は付けない。
    /// </summary>
    public class FbxMotionExportOptions
    {
        /// <summary>1 秒あたりのフレーム数。0 以下ならクリップの frameRate（それも無ければ 30）。</summary>
        public float Fps;
        /// <summary>区間の開始（秒）。出力では 0 秒になる。</summary>
        public float StartSec;
        /// <summary>区間の終了（秒）。StartSec 以下ならクリップの終端。</summary>
        public float EndSec;
        /// <summary>モーションをモデルへ当てるときの位置の倍率（VMD は PmxUnityRatio）。</summary>
        public float ClipPositionScale = 1f;
        /// <summary>T ポーズ整列を脚（太もも・すね）にも掛けるか。</summary>
        public bool AlignLegs;
        /// <summary>メッシュ（スキン・ブレンドシェイプ・材質）も書くか。切ると骨格とモーションだけ。</summary>
        public bool IncludeMesh = true;
        /// <summary>テイク（FbxAnimStack）の名前。空なら "Take 001"。</summary>
        public string TakeName = "";
    }

    /// <summary>FBX 書き出し結果。</summary>
    public class FbxExportResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }
        public string OutputPath   { get; set; }

        /// <summary>テクスチャを書いたフォルダ。書かなかったら空。</summary>
        public string TextureFolder { get; set; } = "";

        public int NodeCount         { get; set; }
        public int BoneCount         { get; set; }
        public int MeshCount         { get; set; }
        public int ControlPointCount { get; set; }
        public int PolygonCount      { get; set; }
        public int MaterialCount     { get; set; }
        public int TextureCount      { get; set; }
        public int BlendShapeCount   { get; set; }
        public int SkinnedMeshCount  { get; set; }

        // ---- モーション（ExportMotion のときだけ）----
        public bool  HasMotion               { get; set; }
        public int   FrameCount              { get; set; }
        public float DurationSec             { get; set; }
        public int   MatchedTrackCount       { get; set; }
        public bool  RootMotion              { get; set; }
        public int   AnimatedNodeCount       { get; set; }
        /// <summary>FBX のノードを持たない関節（半身モデルの仮想ミラー）の数。曲線を書けない。</summary>
        public int   SkippedVirtualJointCount { get; set; }
        public int   AnimatedBlendShapeCount { get; set; }
        public int   ExpressionTrackCount    { get; set; }
        public int   ExpressionResolvedCount { get; set; }

        public System.Collections.Generic.List<string> Warnings { get; }
            = new System.Collections.Generic.List<string>();

        public static FbxExportResult Failed(string message)
            => new FbxExportResult { Success = false, ErrorMessage = message };

        public string Summary()
            => $"ノード {NodeCount} / ボーン {BoneCount} / メッシュ {MeshCount}（スキン {SkinnedMeshCount}） / " +
               $"制御点 {ControlPointCount} / 面 {PolygonCount} / 材質 {MaterialCount} / テクスチャ {TextureCount} / " +
               $"ブレンドシェイプ {BlendShapeCount}" +
               (HasMotion
                   ? $" / モーション {FrameCount} フレーム {DurationSec:F3} 秒・動くノード {AnimatedNodeCount}" +
                     $"（仮想関節で飛ばした {SkippedVirtualJointCount}）・動くブレンドシェイプ {AnimatedBlendShapeCount}" +
                     $"（表情 {ExpressionResolvedCount}/{ExpressionTrackCount}）・当たったトラック {MatchedTrackCount}・" +
                     $"根の動き {(RootMotion ? "body から" : "なし")}"
                   : "");
    }
}
