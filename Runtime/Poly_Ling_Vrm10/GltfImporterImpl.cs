// GltfImporterImpl.cs
// ============================================================
// IGltfImporter の UniGLTF 実装（素の glTF：.glb / .gltf）
// ============================================================
//
// 【分離規約】IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属し、VRM パッケージが無い環境では
//   アセンブリごとコンパイル対象から外れる。
//
// 【読み込みの流れ（GameObject / Material を UniGLTF に作らせない）】
//   1. AutoGltfFileParser で GltfData（拡張子で .gltf / GLB を判定）。
//   2. VrmToPolyLingConverter（素の glTF 用コンストラクタ）で ModelContext の材料を組む。
//      軸は Z 反転（Coordinates.Vrm0 → Unity。UniGLTF の glTF 既定と同じ）。
//      VRM 拡張（Humanoid・表情・揺れ・制約・視線・一人称・Meta）は読まない。
//   3. finally で GltfData を破棄する（VrmLib.Model の頂点は GltfData の NativeArray を指す）。
//
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UniGLTF;
using Poly_Ling.Gltf;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    public class GltfImporterImpl : IGltfImporter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLGltfBridge.RegisterImporter(new GltfImporterImpl());
        }

        public bool IsAvailable => true;

        public GltfImportResult Import(string filePath, GltfImportSettings settings)
        {
            if (string.IsNullOrEmpty(filePath))
                return GltfImportResult.Failed("ファイルパスが空です");
            if (!File.Exists(filePath))
                return GltfImportResult.Failed($"ファイルが見つかりません: {filePath}");

            settings = settings ?? GltfImportSettings.CreateDefault();

            // 変換器は VRM と共通。VRM 固有の項目はすべて切る
            // （揺れ・制約の拡張データは VRM 1.0 の X 反転前提なので、Z 反転の素の glTF には使えない）。
            var conv = new Vrm10ImportSettings
            {
                ImportMorphs      = settings.ImportMorphs,
                ImportExpressions = false,
                ImportSpringBones = false,
                ImportConstraints = false,
                ImportMaterials   = settings.ImportMaterials,
                ExtractTextures   = settings.ExtractTextures,
                AllowVrm0         = false,
                TextureFolder     = settings.TextureFolder ?? "",
            };

            GltfData data = null;
            try
            {
                data = new AutoGltfFileParser(filePath).Parse();
                data = MergeBuffersToBin(data, filePath);

                var r = new VrmToPolyLingConverter(data, filePath, conv, VrmLib.Coordinates.Vrm0).Convert();
                if (r == null || !r.Success)
                    return GltfImportResult.Failed(r?.ErrorMessage ?? "変換に失敗しました");

                var result = new GltfImportResult
                {
                    Success       = true,
                    TextureFolder = r.TextureFolder,
                    BoneCount     = r.BoneCount,
                    MeshCount     = r.MeshCount,
                    VertexCount   = r.VertexCount,
                    MorphCount    = r.MorphCount,
                    MaterialCount = r.MaterialCount,
                    TextureCount  = r.TextureCount,
                };
                result.MeshContexts.AddRange(r.MeshContexts);
                result.MaterialReferences.AddRange(r.MaterialReferences);
                result.MorphExpressions.AddRange(r.MorphExpressions);
                result.Warnings.AddRange(r.Warnings);

                foreach (string w in result.Warnings)
                    Debug.LogWarning($"[GltfImporter] {w}");

                Debug.Log(
                    $"[GltfImporter] Import successful: ボーン {result.BoneCount}, メッシュ {result.MeshCount}, " +
                    $"頂点 {result.VertexCount}, モーフ {result.MorphCount}, 材質 {result.MaterialCount}, " +
                    $"テクスチャ {result.TextureCount} ← {filePath}");

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GltfImporter] Import failed: {ex.Message}\n{ex.StackTrace}");
                return GltfImportResult.Failed(ex.Message);
            }
            finally
            {
                data?.Dispose();
            }
        }

        /// <summary>
        /// すべてのバッファを 1 本の BIN にまとめた GltfData を返す。
        ///
        /// 【理由】ModelReader が使う Vrm10ImportData.GetAccessorBytes は
        ///   GLB の BIN チャンク（GltfData.Bin）しか見ない（Vrm10ImportData.cs:159-161）。
        ///   .gltf の外部 .bin、data: URI、複数バッファはそのままでは読めない。
        ///
        /// 【方法】各バッファのバイト列を 4 バイト境界で連結し、bufferView の buffer を 0、
        ///   byteOffset を連結後の位置へ書き換える。画像・疎アクセサも bufferView 経由なので追従する。
        ///   画像の外部 URI はそのまま残し、glTF と同じフォルダから読む（FileSystemStorage）。
        ///   すでに BIN 1 本の GLB なら何もしない。
        ///   作り直した場合、元の GltfData はここで破棄する。
        /// </summary>
        private static GltfData MergeBuffersToBin(GltfData src, string filePath)
        {
            var gltf    = src.GLTF;
            var buffers = gltf.buffers;
            if (buffers == null || buffers.Count == 0) return src;
            if (buffers.Count == 1 && src.Bin.IsCreated && string.IsNullOrEmpty(buffers[0].uri)) return src;

            var offsets = new int[buffers.Count];
            int total = 0;
            var parts = new byte[buffers.Count][];
            for (int i = 0; i < buffers.Count; i++)
            {
                parts[i]   = src.GetBytesFromBuffer(i).ToArray();
                offsets[i] = total;
                total     += (parts[i].Length + 3) & ~3;
            }

            var merged = new byte[total];
            for (int i = 0; i < parts.Length; i++)
                Buffer.BlockCopy(parts[i], 0, merged, offsets[i], parts[i].Length);

            if (gltf.bufferViews != null)
            {
                foreach (var view in gltf.bufferViews)
                {
                    if (view == null || view.buffer < 0 || view.buffer >= offsets.Length) continue;
                    view.byteOffset += offsets[view.buffer];
                    view.buffer      = 0;
                }
            }

            gltf.buffers = new System.Collections.Generic.List<glTFBuffer>
            {
                new glTFBuffer { byteLength = total },
            };

            string dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "";
            var rebuilt = new GltfData(
                filePath,
                string.Empty,
                gltf,
                new System.Collections.Generic.List<GlbChunk>
                {
                    new GlbChunk(),                                    // json（使わない）
                    GlbChunk.CreateBin(new ArraySegment<byte>(merged)),
                },
                new FileSystemStorage(dir),
                new MigrationFlags());

            src.Dispose();
            return rebuilt;
        }
    }
}
