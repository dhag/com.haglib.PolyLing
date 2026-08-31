// Vrm10ExporterImpl.cs
// ============================================================
// IVrm10Exporter の UniVRM 実装
// ============================================================
//
// 【分離規約】規約は Poly_Ling.Vrm.IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属する。
//   asmdef の defineConstraints により、VRM パッケージが無い環境では
//   アセンブリごとコンパイル対象から外れる。したがって本体は無傷で動く。
//
// 【登録】
//   [RuntimeInitializeOnLoadMethod] で PLVrm10Bridge へ登録する。
//   Play 時にしか走らないため、Editor 拡張（非 Play）からは当面使えない。
//   これは承知のうえの制限。Editor 対応を足すときは
//   PolyLing.Vrm10.Editor アセンブリを別に作ること（#if UNITY_EDITOR で解決しない）。
//
// 【GameObject について】
//   UniVRM の Vrm10Exporter.Export(root, model, converter, option, meta) は
//   ジオメトリを VrmLib.Model から取るが、ExportVrm が root の非 null を要求する。
//   よって空の GameObject を1つだけ作って渡し、必ず破棄する。
//   ヒエラルキーも SkinnedMeshRenderer も作らない。
//   root に Vrm10Instance を付けていないため、Expression / LookAt / FirstPerson /
//   SpringBone / Constraints は UniVRM 側で自動的にスキップされる。
//
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using Poly_Ling.Context;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    public class Vrm10ExporterImpl : IVrm10Exporter
    {
        // ================================================================
        // 登録
        // ================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLVrm10Bridge.Register(new Vrm10ExporterImpl());
        }

        // ================================================================
        // IVrm10Exporter
        // ================================================================

        public bool IsAvailable => true;

        public Vrm10ExportResult Export(
            ModelContext model, string outputPath, Vrm10ExportSettings settings)
        {
            if (model == null)
                return Vrm10ExportResult.Failed("モデルがありません");
            if (string.IsNullOrEmpty(outputPath))
                return Vrm10ExportResult.Failed("出力パスが空です");

            settings = settings ?? Vrm10ExportSettings.CreateDefault();

            GameObject root = null;

            try
            {
                using (var arrayManager = new NativeArrayManager())
                {
                    // ModelContext → VrmLib.Model（Unity 左手系のまま組む）
                    var vrmModel = PolyLingToVrmLibConverter.Convert(
                        model, settings, arrayManager, out var convertReport);

                    // VRM は右手系。UniVRM の便利関数と同じ手順で系変換する。
                    vrmModel.ConvertCoordinate(VrmLib.Coordinates.Vrm1);

                    var exportSettings = new GltfExportSettings();
                    var meta = BuildMeta(model, settings);

                    // ExportVrm が非 null を要求するだけの空 root。
                    root = new GameObject("__polyling_vrm_export_root__");
                    root.hideFlags = HideFlags.HideAndDontSave;

                    using (var exporter = new Vrm10Exporter(exportSettings))
                    {
                        var option = new VrmLib.ExportArgs();
                        var converter = new ModelExporter();

                        exporter.Export(root, vrmModel, converter, option, meta);

                        byte[] bytes = exporter.Storage.ToGlbBytes();

                        string dir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.WriteAllBytes(outputPath, bytes);

                        int vertexCount = 0;
                        foreach (var group in vrmModel.MeshGroups)
                            foreach (var mesh in group.Meshes)
                                vertexCount += mesh.VertexBuffer?.Count ?? 0;

                        // VRM 1.0 は humanoid を必須とする。空でもファイルは出すが
                        // ビューアは読み込みを拒否するので、必ず警告として返す。
                        string warning = null;
                        if (convertReport.HumanoidBoneCount == 0)
                        {
                            warning = "Humanoid が未割当のため VRM としては不完全です"
                                    + "（glTF としては開けます）";
                        }
                        else if (convertReport.MissingRequiredBones != null &&
                                 convertReport.MissingRequiredBones.Count > 0)
                        {
                            // 必須ボーンが1つでも欠けるとビューアは読み込みを拒否する。
                            // どれが足りないかを出さないと原因追跡ができないので名前を並べる。
                            warning = "Humanoid の必須ボーンが不足しています: "
                                    + string.Join(", ", convertReport.MissingRequiredBones);
                        }

                        if (!string.IsNullOrEmpty(warning))
                            Debug.LogWarning($"[Vrm10Exporter] {warning}: {outputPath}");

                        Debug.Log(
                            $"[Vrm10Exporter] Export successful: {vrmModel.Nodes.Count} nodes, " +
                            $"{vrmModel.MeshGroups.Count} meshes, {vrmModel.Materials.Count} materials, " +
                            $"{vrmModel.Skins.Count} skins, humanoid {convertReport.HumanoidBoneCount} bones, " +
                            $"ミラー枝 {convertReport.MirrorBranchNodes} ノード, " +
                            $"{vertexCount} vertices" +
                            (convertReport.SkippedInvisible > 0
                                ? $", 非表示 {convertReport.SkippedInvisible} メッシュを除外"
                                : "") +
                            $" → {outputPath}");

                        // 配置情報がどこに入っているかの実測。
                        // MeshFilter 経路は頂点がパーツローカルで、配置は
                        // オブジェクトの親子関係と Transform が持つ。
                        // その Transform がどのフィールドにあるかを確定させるための診断。
                        if (convertReport.PlacementDiagnostics != null)
                        {
                            foreach (var line in convertReport.PlacementDiagnostics)
                                Debug.Log($"[Vrm10Exporter][配置診断] {line}");
                        }

                        // Humanoid 割当で落ちたエントリ。原因追跡のため必ず出す。
                        if (convertReport.UnresolvedHumanoid != null &&
                            convertReport.UnresolvedHumanoid.Count > 0)
                        {
                            Debug.Log(
                                $"[Vrm10Exporter] Humanoid 未解決 {convertReport.UnresolvedHumanoid.Count} 件: "
                                + string.Join(" / ", convertReport.UnresolvedHumanoid));
                        }

                        return new Vrm10ExportResult
                        {
                            Success           = true,
                            OutputPath        = outputPath,
                            NodeCount         = vrmModel.Nodes.Count,
                            MeshCount         = vrmModel.MeshGroups.Count,
                            MaterialCount     = vrmModel.Materials.Count,
                            VertexCount       = vertexCount,
                            HumanoidBoneCount = convertReport.HumanoidBoneCount,
                            Warning           = warning,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Vrm10Exporter] Export failed: {ex.Message}\n{ex.StackTrace}");
                return Vrm10ExportResult.Failed(ex.Message);
            }
            finally
            {
                if (root != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                    else                       UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        // ================================================================
        // Meta
        // ================================================================

        /// <summary>
        /// VRM Meta を設定から組む。
        /// Name / Version / Authors は仕様上必須なので、空なら埋める。
        /// </summary>
        private static VRM10ObjectMeta BuildMeta(ModelContext model, Vrm10ExportSettings settings)
        {
            var meta = new VRM10ObjectMeta
            {
                Name    = !string.IsNullOrEmpty(settings.Title) ? settings.Title
                        : (!string.IsNullOrEmpty(model.Name) ? model.Name : "Untitled"),
                Version = !string.IsNullOrEmpty(settings.Version) ? settings.Version : "1.0",
                CopyrightInformation = settings.CopyrightInformation ?? "",
                ContactInformation   = settings.ContactInformation ?? "",
                OtherLicenseUrl      = settings.OtherLicenseUrl ?? "",
            };

            meta.Authors.Clear();
            if (settings.Authors != null)
            {
                foreach (var a in settings.Authors)
                    if (!string.IsNullOrEmpty(a)) meta.Authors.Add(a);
            }
            if (meta.Authors.Count == 0) meta.Authors.Add("Unknown");

            return meta;
        }
    }
}
