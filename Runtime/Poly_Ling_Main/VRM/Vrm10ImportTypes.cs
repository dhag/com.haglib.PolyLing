// Vrm10ImportTypes.cs
// ============================================================
// VRM 1.0 インポートの設定・結果（純POCO）
// ============================================================
//
// 【分離規約】IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルには VRM パッケージ（UniGLTF / VrmLib / UniVRM10）の型を持ち込まない。
//
// 【依存】
//   PolyLing の型のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using Poly_Ling.Data;
using Poly_Ling.Materials;

namespace Poly_Ling.Vrm
{
    /// <summary>VRM 1.0 インポート設定。</summary>
    [Serializable]
    public class Vrm10ImportSettings
    {
        /// <summary>ブレンドシェイプをモーフとして読み込むか。false なら表情も読まない。</summary>
        [PLParam(Description = "ブレンドシェイプをモーフとして読み込む。切ると表情も読まない")]
        public bool ImportMorphs = true;

        /// <summary>VRM の表情をモーフエクスプレッションとして読み込むか。</summary>
        [PLParam(Description = "VRM の表情をモーフエクスプレッションとして読み込む")]
        public bool ImportExpressions = true;

        /// <summary>揺れもの（VRMC_springBone）を読み込むか。</summary>
        [PLParam(Description = "揺れもの（スプリングボーン）を読み込む")]
        public bool ImportSpringBones = true;

        /// <summary>ノード制約（VRMC_node_constraint）を読み込むか。</summary>
        [PLParam(Description = "ノード制約を読み込む")]
        public bool ImportConstraints = true;

        /// <summary>材質を読み込むか。</summary>
        [PLParam(Description = "材質を読み込む")]
        public bool ImportMaterials = true;

        /// <summary>
        /// 埋め込みテクスチャをファイルへ書き出して材質に使うか。
        /// 書き出さない場合、材質のテクスチャは付かない。
        /// </summary>
        [PLParam(Description = "埋め込みテクスチャをファイルへ書き出して材質に使う")]
        public bool ExtractTextures = true;

        /// <summary>VRM 0.x を 1.0 へ移行して読み込むか。</summary>
        [PLParam(Description = "VRM 0.x を 1.0 へ移行して読み込む")]
        public bool AllowVrm0 = true;

        /// <summary>
        /// テクスチャの書き出し先フォルダ。空なら DefaultTextureFolder の場所。
        /// 受け口（コマンド経路）が関門を通して決めるので、外から送るものではない。
        /// </summary>
        [NonSerialized]
        [PLParam(Ignore = true)]
        public string TextureFolder = "";

        /// <summary>既定のテクスチャ書き出し先（VRM と同じフォルダの「VRM名_textures」）。</summary>
        public static string DefaultTextureFolder(string vrmPath)
        {
            if (string.IsNullOrEmpty(vrmPath)) return "";
            string dir  = Path.GetDirectoryName(vrmPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(vrmPath);
            return Path.Combine(dir, name + "_textures");
        }

        public static Vrm10ImportSettings CreateDefault() => new Vrm10ImportSettings();

        public Vrm10ImportSettings Clone()
        {
            return new Vrm10ImportSettings
            {
                ImportMorphs      = this.ImportMorphs,
                ImportExpressions = this.ImportExpressions,
                ImportSpringBones = this.ImportSpringBones,
                ImportConstraints = this.ImportConstraints,
                ImportMaterials   = this.ImportMaterials,
                ExtractTextures   = this.ExtractTextures,
                AllowVrm0         = this.AllowVrm0,
                TextureFolder     = this.TextureFolder,
            };
        }
    }

    /// <summary>
    /// VRM 1.0 インポート結果。ModelContext の材料を持つ。
    /// MeshContexts の並びがそのまま MeshContextList の並びになる
    /// （ボーン → 描画オブジェクト → モーフ の順）。
    /// </summary>
    public class Vrm10ImportResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>VRM 0.x から移行して読んだか。</summary>
        public bool SourceIsVrm0 { get; set; }

        /// <summary>テクスチャを書き出したフォルダ（書き出していなければ空）。</summary>
        public string TextureFolder { get; set; } = "";

        // ---- ModelContext の材料 ----------------------------------------

        public List<MeshContext>       MeshContexts       { get; } = new List<MeshContext>();
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();
        public List<MorphExpression>   MorphExpressions   { get; } = new List<MorphExpression>();

        /// <summary>スプリングボーンのコライダーグループ名（モデルレベル）。</summary>
        public List<string> SpringBoneColliderGroupNames { get; } = new List<string>();

        public VrmMetaData   VrmMeta   { get; set; }
        public VrmLookAtData VrmLookAt { get; set; }

        // ---- 集計 --------------------------------------------------------

        public int BoneCount          { get; set; }
        public int MeshCount          { get; set; }
        public int VertexCount        { get; set; }
        public int MorphCount         { get; set; }
        public int ExpressionCount    { get; set; }
        public int HumanoidBoneCount  { get; set; }
        public int SpringCount        { get; set; }
        public int ColliderCount      { get; set; }
        public int ConstraintCount    { get; set; }
        public int MaterialCount      { get; set; }
        public int TextureCount       { get; set; }

        /// <summary>警告（呼び出し側がログへ流す）。</summary>
        public List<string> Warnings { get; } = new List<string>();

        public static Vrm10ImportResult Failed(string message)
            => new Vrm10ImportResult { Success = false, ErrorMessage = message };
    }
}
