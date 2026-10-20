// GltfExporterImpl.cs
// ============================================================
// IGltfExporter の UniGLTF 実装（素の glTF：.glb / .gltf）
// ============================================================
//
// 【分離規約】IVrm10Exporter.cs 冒頭のコメントを正典とする。
//
// 【出力の流れ】
//   1. HierarchyBuilder（Runtime）で ModelContext → GameObject 階層 ＋ UnityEngine.Mesh。
//      設定は VRM 書き出し（Vrm10ExporterImpl）と同じ。モーフはブレンドシェイプとして載る。
//   2. UniGLTF の gltfExporter（InverseAxis = Z。UniGLTF の glTF 既定）で glTF 化。
//   3. 拡張子 .gltf なら JSON（BOM なし）＋「ファイル名.bin」、それ以外は GLB。
//
// 【一時オブジェクトの後始末】
//   階層・Mesh は書き出し用の使い捨て。finally で必ず破棄する。
//   gltfExporter が作る複製は gltfExporter.Dispose が消す。
//
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UniGLTF;
using Poly_Ling.Context;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Gltf;

using PLGltfExportSettings = Poly_Ling.Gltf.GltfExportSettings;

namespace Poly_Ling.Vrm10Impl
{
    public class GltfExporterImpl : IGltfExporter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLGltfBridge.RegisterExporter(new GltfExporterImpl());
        }

        public bool IsAvailable => true;

        public GltfExportResult Export(ModelContext model, string outputPath, PLGltfExportSettings settings)
        {
            if (model == null)
                return GltfExportResult.Failed("モデルがありません");
            if (string.IsNullOrEmpty(outputPath))
                return GltfExportResult.Failed("出力パスが空です");

            settings = settings ?? PLGltfExportSettings.CreateDefault();

            HierarchyBuildResult built = null;
            try
            {
                // ------------------------------------------------------------
                // 1. ヒエラルキー生成（Vrm10ExporterImpl と同じ設定）
                // ------------------------------------------------------------
                var buildOptions = new HierarchyBuildOptions
                {
                    CreateArmature            = true,
                    UseBindpose               = true,
                    ExportVisibleOnly         = !settings.ExportInvisibleObjects,
                    IncludeInvisibleAncestors = true,
                    ExportMeshOnly            = false,
                    ExportPhysics             = false,
                    TolerantMirrorBranch      = true,
                    ExportMorphTargets        = settings.ExportMorphTargets,
                    // 空サブメッシュは UniGLTF の書き出しで accessors[-1] を引いて落ちる。
                    DropEmptySubMeshes        = true,
                    RendererMode              = settings.ExportSkinning
                        ? HierarchyRendererMode.Auto
                        : HierarchyRendererMode.ForceMeshFilter,
                };

                var builder = new HierarchyBuilder(buildOptions);

                var pre = new HierarchyBuildResult();
                builder.WarnAboutExpectations(model, pre);
                foreach (string w in pre.Warnings)
                    Debug.LogWarning($"[GltfExporter] {w}");

                built = builder.Build(model);
                if (built?.Root == null)
                    return GltfExportResult.Failed("ヒエラルキー生成に失敗しました");

                built.Root.hideFlags = HideFlags.HideAndDontSave;

                foreach (string w in built.Warnings)
                    Debug.LogWarning($"[GltfExporter] {w}");

                // ------------------------------------------------------------
                // 2. glTF 化
                // ------------------------------------------------------------
                var data = new ExportingGltfData();
                var gltfSettings = new UniGLTF.GltfExportSettings { InverseAxis = Axes.Z };
                using (var exporter = new gltfExporter(data, gltfSettings))
                {
                    exporter.Prepare(built.Root);
                    exporter.Export();
                }

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // ------------------------------------------------------------
                // 3. 書き出し
                // ------------------------------------------------------------
                string binPath = "";
                if (string.Equals(Path.GetExtension(outputPath), ".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    var (json, _) = data.ToGltf(outputPath);
                    File.WriteAllText(outputPath, json, new System.Text.UTF8Encoding(false));
                    binPath = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(outputPath) + ".bin");
                    File.WriteAllBytes(binPath, data.BinBytes.ToArray());
                }
                else
                {
                    File.WriteAllBytes(outputPath, data.ToGlbBytes());
                }

                // ------------------------------------------------------------
                // 4. 結果
                // ------------------------------------------------------------
                int morphTargets = 0;
                foreach (var m in data.Gltf.meshes)
                    if (m?.primitives != null && m.primitives.Count > 0 && m.primitives[0].targets != null)
                        morphTargets += m.primitives[0].targets.Count;

                var result = new GltfExportResult
                {
                    Success          = true,
                    OutputPath       = outputPath,
                    BinPath          = binPath,
                    NodeCount        = data.Gltf.nodes?.Count ?? 0,
                    BoneCount        = built.BoneCount,
                    MeshCount        = data.Gltf.meshes?.Count ?? 0,
                    MaterialCount    = data.Gltf.materials?.Count ?? 0,
                    VertexCount      = CountVertices(built.Root),
                    MorphTargetCount = morphTargets,
                };

                Debug.Log(
                    $"[GltfExporter] Export successful: ノード {result.NodeCount}, ボーン {result.BoneCount}, " +
                    $"メッシュ {result.MeshCount}, 材質 {result.MaterialCount}, 頂点 {result.VertexCount}, " +
                    $"モーフターゲット {result.MorphTargetCount}" +
                    (built.SkippedInvisibleCount > 0
                        ? $", 非表示 {built.SkippedInvisibleCount} メッシュを除外"
                        : "") +
                    $" → {outputPath}");

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GltfExporter] Export failed: {ex.Message}\n{ex.StackTrace}");
                return GltfExportResult.Failed(ex.Message);
            }
            finally
            {
                DestroyHierarchy(built?.Root);
            }
        }

        private static int CountVertices(GameObject root)
        {
            if (root == null) return 0;
            int total = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) total += smr.sharedMesh.vertexCount;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) total += mf.sharedMesh.vertexCount;
            return total;
        }

        /// <summary>書き出し用の階層と Mesh を破棄する（Mesh は GameObject を消しても解放されない）。</summary>
        private static void DestroyHierarchy(GameObject root)
        {
            if (root == null) return;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                DestroyObject(smr.sharedMesh);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                DestroyObject(mf.sharedMesh);
            DestroyObject(root);
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else                       UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
