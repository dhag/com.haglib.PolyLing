// IGltfIO.cs
// ============================================================
// glTF 読み書きのインターフェース・アクセサ・未登録時のスタブ
// ============================================================
//
// 【分離規約】IVrm10Exporter.cs 冒頭のコメントを正典とする（GltfTypes.cs 冒頭も参照）。
//   引数・戻り値に UniGLTF の型を出さない。実装は PolyLing.Vrm10 アセンブリにあり、
//   [RuntimeInitializeOnLoadMethod] で PLGltfBridge.Register する。
//   VRM パッケージが無い環境では登録されず、Null 実装（IsAvailable == false）が使われる。
//
// ============================================================

using UnityEngine;

namespace Poly_Ling.Gltf
{
    public interface IGltfImporter
    {
        /// <summary>実際に読み込める実装が登録されているか。</summary>
        bool IsAvailable { get; }

        /// <summary>glTF（.glb / .gltf）を読み込み、ModelContext の材料を返す。</summary>
        GltfImportResult Import(string filePath, GltfImportSettings settings);
    }

    public interface IGltfExporter
    {
        /// <summary>実際に書き出せる実装が登録されているか。</summary>
        bool IsAvailable { get; }

        /// <summary>ModelContext を glTF として書き出す。拡張子で GLB / .gltf を切り替える。</summary>
        GltfExportResult Export(
            Poly_Ling.Context.ModelContext model, string outputPath, GltfExportSettings settings);
    }

    /// <summary>glTF 読み書き実装への Runtime アクセサ。</summary>
    public static class PLGltfBridge
    {
        private static IGltfImporter _importer;
        private static IGltfExporter _exporter;

        public static IGltfImporter Importer => _importer ?? (_importer = new GltfImporterNull());
        public static IGltfExporter Exporter => _exporter ?? (_exporter = new GltfExporterNull());

        public static void RegisterImporter(IGltfImporter impl) { _importer = impl; }
        public static void RegisterExporter(IGltfExporter impl) { _exporter = impl; }
    }

    public class GltfImporterNull : IGltfImporter
    {
        public bool IsAvailable => false;

        public GltfImportResult Import(string filePath, GltfImportSettings settings)
        {
            Debug.LogError(
                "[PolyLing] glTF インポータが利用できません: VRM パッケージ (com.vrmc.vrm / com.vrmc.gltf) が" +
                $"導入されていないか、Play 中でありません ({filePath})");
            return GltfImportResult.Failed("glTF インポータが利用できません");
        }
    }

    public class GltfExporterNull : IGltfExporter
    {
        public bool IsAvailable => false;

        public GltfExportResult Export(
            Poly_Ling.Context.ModelContext model, string outputPath, GltfExportSettings settings)
        {
            Debug.LogError(
                "[PolyLing] glTF エクスポータが利用できません: VRM パッケージ (com.vrmc.vrm / com.vrmc.gltf) が" +
                $"導入されていないか、Play 中でありません ({outputPath})");
            return GltfExportResult.Failed("glTF エクスポータが利用できません");
        }
    }
}
