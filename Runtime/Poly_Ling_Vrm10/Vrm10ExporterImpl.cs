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
// ============================================================
// 出力の流れ
// ============================================================
//
//   1. HierarchyBuilder（Runtime）で ModelContext → GameObject 階層
//      ＋ UnityEngine.Mesh。モーフはブレンドシェイプとして同時に載る。
//   2. Vrm10SceneAssembler で Humanoid / Vrm10Instance / VRM10Object /
//      表情 / スプリングボーンを載せる。
//   3. Vrm10Exporter.Export(settings, root, meta) で GLB バイト列を得る。
//   4. File.WriteAllBytes。
//
//   3 の中で ModelExporter が階層を VrmLib.Model へ変換し、
//   ConvertCoordinate(Vrm1) で右手系へ直し、VRMC_vrm / VRMC_springBone を書く。
//
// 【なぜ VrmLib.Model を自前で組まないのか】
//   Vrm10Exporter.ExportVrm は root に Vrm10Instance が付いている場合にのみ
//   Expression / SpringBone を出す（Vrm10Exporter.cs:310-318）。しかも
//   ノード index は ModelExporter.Nodes[GameObject] からしか引けない
//   （同 :538-547, :802-816）。VrmLib.Model を直に組む旧経路
//   （PolyLingToVrmLibConverter）はこの入口に触れないため、
//   表情とスプリングボーンを構造的に出せなかった。
//
// 【一時オブジェクトの後始末】
//   階層・UnityEngine.Mesh・ScriptableObject はすべて書き出し用の使い捨て。
//   finally で必ず破棄する。放置すると Play を抜けるまで残る。
//
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using Poly_Ling.Context;
using Poly_Ling.HierarchyIO;
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

            HierarchyBuildResult built = null;
            Vrm10SceneAssembler assembler = null;

            try
            {
                // ------------------------------------------------------------
                // 1. ヒエラルキー生成
                //    剛体／JOINT は VRM に対応する概念が無いので出さない。
                //    スキニングの有無は設定に従う（OFF なら MeshFilter で出す）。
                //
                //    settings.ExportNormals / ExportUVs はこの経路では効かない。
                //    ModelExporter.CreateMesh が法線・UV を常に載せるうえ、
                //    MeshWriter.ExportMeshDivided は VertexBuffer.Normals /
                //    TexCoords を null チェックせずに読むため、外すと落ちる。
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

                    // 空サブメッシュを残すと UniVRM 側が落ちる。
                    // ModelExporter.CreateMesh は空サブメッシュを飛ばさず、
                    // 頂点ゼロのプリミティブが MeshExportUtil で
                    // Gltf.accessors[-1] を引いて例外になる。
                    DropEmptySubMeshes        = true,
                    RendererMode              = settings.ExportSkinning
                        ? HierarchyRendererMode.Auto
                        : HierarchyRendererMode.ForceMeshFilter,
                };

                var builder = new HierarchyBuilder(buildOptions);

                var pre = new HierarchyBuildResult();
                builder.WarnAboutExpectations(model, pre);
                foreach (string w in pre.Warnings)
                    Debug.LogWarning($"[Vrm10Exporter] {w}");

                built = builder.Build(model);
                if (built?.Root == null)
                    return Vrm10ExportResult.Failed("ヒエラルキー生成に失敗しました");

                built.Root.hideFlags = HideFlags.HideAndDontSave;

                foreach (string w in built.Warnings)
                    Debug.LogWarning($"[Vrm10Exporter] {w}");

                // ------------------------------------------------------------
                // 2. VRM コンポーネント
                // ------------------------------------------------------------
                var meta = BuildMeta(model, settings);

                assembler = new Vrm10SceneAssembler();
                var report = assembler.Assemble(model, built, meta, settings);

                foreach (string w in report.Warnings)
                    Debug.LogWarning($"[Vrm10Exporter] {w}");

                // ------------------------------------------------------------
                // 3. GLB 化
                //    右手系への変換・VRMC_vrm・VRMC_springBone はこの中で行われる。
                // ------------------------------------------------------------
                byte[] bytes = Vrm10Exporter.Export(
                    new GltfExportSettings(), built.Root, vrmMeta: meta);

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(outputPath, bytes);

                // ------------------------------------------------------------
                // 4. 結果
                // ------------------------------------------------------------
                // VRM 1.0 は humanoid を必須とする。空でもファイルは出すが
                // ビューアは読み込みを拒否するので、必ず警告として返す。
                string warning = null;
                if (report.HumanoidBoneCount == 0)
                {
                    warning = "Humanoid が未割当のため VRM としては不完全です"
                            + "（glTF としては開けます）";
                }
                else if (report.MissingRequiredBones.Count > 0)
                {
                    // 必須ボーンが1つでも欠けるとビューアは読み込みを拒否する。
                    // どれが足りないかを出さないと原因追跡ができないので名前を並べる。
                    warning = "Humanoid の必須ボーンが不足しています: "
                            + string.Join(", ", report.MissingRequiredBones);
                }

                if (!string.IsNullOrEmpty(warning))
                    Debug.LogWarning($"[Vrm10Exporter] {warning}: {outputPath}");

                int meshCount = CountRenderers(built.Root);

                Debug.Log(
                    $"[Vrm10Exporter] Export successful: ノード {built.ExportedNodeCount}, " +
                    $"ボーン {built.BoneCount}, メッシュ {meshCount}, " +
                    $"humanoid {report.HumanoidBoneCount} bones" +
                    (report.SupplementedJointCount > 0
                        ? $"（うち補完 {report.SupplementedJointCount}）"
                        : "") + ", " +
                    $"ブレンドシェイプ {report.MorphShapeCount}, " +
                    $"表情 {report.ExpressionCount}, " +
                    $"揺れ {report.SpringCount} チェーン / コライダー {report.SpringBoneColliderCount}" +
                    (built.SkippedInvisibleCount > 0
                        ? $", 非表示 {built.SkippedInvisibleCount} メッシュを除外"
                        : "") +
                    $" → {outputPath}");

                return new Vrm10ExportResult
                {
                    Success                 = true,
                    OutputPath              = outputPath,
                    NodeCount               = built.ExportedNodeCount + built.BoneCount,
                    MeshCount               = meshCount,
                    MaterialCount           = model.Materials?.Count ?? 0,
                    VertexCount             = CountVertices(built.Root),
                    HumanoidBoneCount       = report.HumanoidBoneCount,
                    SupplementedJointCount  = report.SupplementedJointCount,
                    MorphTargetCount        = report.MorphShapeCount,
                    ExpressionCount         = report.ExpressionCount,
                    SpringCount             = report.SpringCount,
                    SpringBoneColliderCount = report.SpringBoneColliderCount,
                    Warning                 = warning,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Vrm10Exporter] Export failed: {ex.Message}\n{ex.StackTrace}");
                return Vrm10ExportResult.Failed(ex.Message);
            }
            finally
            {
                assembler?.Dispose();
                DestroyHierarchy(built?.Root);
            }
        }

        // ================================================================
        // 集計・後始末
        // ================================================================

        private static int CountRenderers(GameObject root)
        {
            if (root == null) return 0;
            return root.GetComponentsInChildren<Renderer>(true).Length;
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

        /// <summary>
        /// 書き出し用に作った階層と、そこに載っている Mesh を破棄する。
        /// Mesh はネイティブ資源なので、GameObject を消すだけでは解放されない。
        /// </summary>
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
