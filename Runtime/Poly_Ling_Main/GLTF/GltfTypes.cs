// GltfTypes.cs
// ============================================================
// glTF（.glb / .gltf）読み書きの設定・結果（純POCO）
// ============================================================
//
// 【分離規約】VRM パッケージとの依存関係の規約は IVrm10Exporter.cs 冒頭を正典とする。
//   glTF の実装も UniGLTF（com.vrmc.gltf）を使うので同じ規約に従う。
//     ・本ファイルに UniGLTF / VrmLib / UniVRM10 の型を持ち込まない。
//     ・実装は PolyLing.Vrm10 アセンブリに置き、[RuntimeInitializeOnLoadMethod] で
//       PLGltfBridge へ登録する（Play 時のみ）。
//
// 【軸】
//   glTF ↔ Unity は Z 反転（UniGLTF の glTF 既定 GltfExportSettings.InverseAxis = Axes.Z）。
//   PolyLing の Z マイナス向きのモデルが、glTF 仕様の正面（+Z）に対応する。
//   VRM 1.0 の X 反転とは別。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using Poly_Ling.Data;
using Poly_Ling.Materials;

namespace Poly_Ling.Gltf
{
    /// <summary>glTF 読み込み設定。</summary>
    [Serializable]
    public class GltfImportSettings
    {
        [PLParam(Description = "モーフターゲットをモーフとして読み込む")]
        public bool ImportMorphs = true;

        [PLParam(Description = "材質を読み込む")]
        public bool ImportMaterials = true;

        [PLParam(Description = "テクスチャをファイルへ書き出して材質に使う")]
        public bool ExtractTextures = true;

        /// <summary>
        /// テクスチャの書き出し先フォルダ。空なら DefaultTextureFolder の場所。
        /// 受け口（コマンド経路）が関門を通して決めるので、外から送るものではない。
        /// </summary>
        [NonSerialized]
        [PLParam(Ignore = true)]
        public string TextureFolder = "";

        /// <summary>既定のテクスチャ書き出し先（glTF と同じフォルダの「ファイル名_textures」）。</summary>
        public static string DefaultTextureFolder(string gltfPath)
        {
            if (string.IsNullOrEmpty(gltfPath)) return "";
            string dir  = Path.GetDirectoryName(gltfPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(gltfPath);
            return Path.Combine(dir, name + "_textures");
        }

        public static GltfImportSettings CreateDefault() => new GltfImportSettings();

        public GltfImportSettings Clone()
        {
            return new GltfImportSettings
            {
                ImportMorphs    = this.ImportMorphs,
                ImportMaterials = this.ImportMaterials,
                ExtractTextures = this.ExtractTextures,
                TextureFolder   = this.TextureFolder,
            };
        }
    }

    /// <summary>
    /// glTF 読み込み結果。ModelContext の材料を持つ。
    /// MeshContexts の並びがそのまま MeshContextList の並びになる
    /// （ボーン → 描画オブジェクト → モーフ の順）。
    /// </summary>
    public class GltfImportResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }

        public string TextureFolder { get; set; } = "";

        public List<MeshContext>       MeshContexts       { get; } = new List<MeshContext>();
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();
        public List<MorphExpression>   MorphExpressions   { get; } = new List<MorphExpression>();

        public int BoneCount     { get; set; }
        public int MeshCount     { get; set; }
        public int VertexCount   { get; set; }
        public int MorphCount    { get; set; }
        public int MaterialCount { get; set; }
        public int TextureCount  { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public static GltfImportResult Failed(string message)
            => new GltfImportResult { Success = false, ErrorMessage = message };
    }

    /// <summary>glTF 書き出し設定。拡張子 .glb なら GLB、.gltf なら .gltf ＋ .bin。</summary>
    [Serializable]
    public class GltfExportSettings
    {
        [PLParam(Description = "スキニング（ボーンウェイト）を書き出す")]
        public bool ExportSkinning = true;

        [PLParam(Description = "モーフをモーフターゲットとして書き出す")]
        public bool ExportMorphTargets = true;

        [PLParam(Description = "非表示のメッシュも書き出す")]
        public bool ExportInvisibleObjects = false;

        public static GltfExportSettings CreateDefault() => new GltfExportSettings();

        public GltfExportSettings Clone()
        {
            return new GltfExportSettings
            {
                ExportSkinning         = this.ExportSkinning,
                ExportMorphTargets     = this.ExportMorphTargets,
                ExportInvisibleObjects = this.ExportInvisibleObjects,
            };
        }
    }

    /// <summary>glTF 書き出し結果。</summary>
    public class GltfExportResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }
        public string OutputPath   { get; set; }

        /// <summary>.gltf のとき一緒に書いたバッファファイル（.bin）。GLB なら空。</summary>
        public string BinPath      { get; set; } = "";

        public int NodeCount        { get; set; }
        public int BoneCount        { get; set; }
        public int MeshCount        { get; set; }
        public int MaterialCount    { get; set; }
        public int VertexCount      { get; set; }
        public int MorphTargetCount { get; set; }

        public static GltfExportResult Failed(string message)
            => new GltfExportResult { Success = false, ErrorMessage = message };
    }
}
