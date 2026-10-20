// FbxExporter.cs
// ============================================================
// FBX 書き出し（IFbxExporter の実装。FBX SDK を使わない）
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 7。
//
// ■ 流れ
//   1. （モーションのとき）ModelMotionSampler で標本を先に取る（標本化は終わりにポーズを戻す。
//      HierarchyBuilder はレストで組む）。
//   2. HierarchyBuilder で Transform 階層と形状の出どころ（HierarchyBuildResult.MeshSourceOf）を作る。
//      Unity メッシュは作らない（CreateUnityMeshes = false）。設定は glTF 書き出しと同じ。
//      ミラー枝・関節・可視補完・モーフの名前と並びの規則は HierarchyBuilder が正本で、ここでは書き写さない。
//   3. FbxModelWriter で FBX のノード木を組み、FbxBinaryWriter（既定）か FbxAsciiWriter で書く。
//   4. 階層は使い捨て。finally で必ず破棄する。
// ============================================================

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Motion;

namespace Poly_Ling.Fbx
{
    public sealed class FbxExporter : IFbxExporter
    {
        public bool IsAvailable => true;

        public FbxExportResult Export(ModelContext model, string outputPath, FbxExportSettings settings)
            => ExportCore(model, outputPath, settings, null, null);

        public FbxExportResult ExportMotion(ModelContext model, MotionClipDTO clip, string outputPath,
                                            FbxExportSettings settings, FbxMotionExportOptions motion)
        {
            if (model == null) return FbxExportResult.Failed("モデルがありません");
            if (clip == null)  return FbxExportResult.Failed("モーションがありません");
            motion = motion ?? new FbxMotionExportOptions();

            if (!ModelMotionSampler.Sample(model, clip, new ModelMotionSampleOptions
                {
                    Fps               = motion.Fps,
                    StartSec          = motion.StartSec,
                    EndSec            = motion.EndSec,
                    ClipPositionScale = motion.ClipPositionScale,
                    AlignLegs         = motion.AlignLegs,
                    SampleExpressions = true,
                }, out var samples, out string reason))
                return FbxExportResult.Failed(reason);

            return ExportCore(model, outputPath, settings, samples, motion);
        }

        private FbxExportResult ExportCore(ModelContext model, string outputPath, FbxExportSettings settings,
                                           ModelMotionSamples samples, FbxMotionExportOptions motion)
        {
            if (model == null)                    return FbxExportResult.Failed("モデルがありません");
            if (string.IsNullOrEmpty(outputPath)) return FbxExportResult.Failed("出力パスが空です");

            settings = settings ?? FbxExportSettings.CreateDefault();

            HierarchyBuildResult built = null;
            try
            {
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
                    DropEmptySubMeshes        = true,
                    RendererMode              = settings.ExportSkinning
                        ? HierarchyRendererMode.Auto
                        : HierarchyRendererMode.ForceMeshFilter,
                    CreateUnityMeshes         = false,
                };

                var builder = new HierarchyBuilder(buildOptions);
                var result  = new FbxExportResult { OutputPath = outputPath };

                var pre = new HierarchyBuildResult();
                builder.WarnAboutExpectations(model, pre);
                result.Warnings.AddRange(pre.Warnings);

                built = builder.Build(model);
                if (built?.Root == null)
                    return FbxExportResult.Failed("ヒエラルキー生成に失敗しました");
                built.Root.hideFlags = HideFlags.HideAndDontSave;
                result.Warnings.AddRange(built.Warnings);

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string texFolder = string.IsNullOrEmpty(settings.TextureFolder)
                    ? FbxExportSettings.DefaultTextureFolder(outputPath)
                    : settings.TextureFolder;

                bool includeMesh = motion == null || motion.IncludeMesh;
                bool embed = settings.EmbedTextures && !settings.Ascii;

                var writer = new FbxModelWriter(model, texFolder, embed, result, includeMesh);
                writer.Build(built);
                if (samples != null)
                    writer.AddAnimation(samples, string.IsNullOrEmpty(motion?.TakeName) ? "Take 001" : motion.TakeName);

                var doc = writer.Document(settings.Ascii);
                if (settings.Ascii) FbxAsciiWriter.Write(outputPath, doc);
                else                FbxBinaryWriter.Write(outputPath, doc);

                result.Success       = true;
                result.TextureFolder = writer.WroteTextures ? texFolder : "";

                foreach (string w in result.Warnings)
                    Debug.LogWarning($"[FbxExporter] {w}");
                Debug.Log($"[FbxExporter] Export successful: {result.Summary()} → {outputPath}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FbxExporter] Export failed: {ex.Message}\n{ex.StackTrace}");
                return FbxExportResult.Failed(ex.Message);
            }
            finally
            {
                if (built?.Root != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(built.Root);
                    else                       UnityEngine.Object.DestroyImmediate(built.Root);
                }
            }
        }
    }
}
