// VrmAnimationExporterImpl.cs
// ============================================================
// IVrmAnimationExporter の UniVRM 実装
// ============================================================
//
// 【分離規約】規約は Poly_Ling.Vrm.IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属する。
//   asmdef の defineConstraints により、VRM パッケージが無い環境では
//   アセンブリごとコンパイル対象から外れる。したがって本体は無傷で動く。
//
// 【登録】
//   [RuntimeInitializeOnLoadMethod] で PLVrmAnimationBridge へ登録する。
//   Play 時にしか走らないため、Editor 拡張（非 Play）からは当面使えない。
//   これは Vrm10ExporterImpl と同じ、承知のうえの制限。
//
// 【なぜ Editor アセンブリを作らないか】
//   UniVRM の VrmAnimationExporter は Runtime クラス
//   （com.vrmc.vrm/Runtime/IO/VrmAnimationExporter.cs:9、VRM10.asmdef に
//   includePlatforms 指定なし）で、GLB のバイト列は ExportingGltfData.ToGlbBytes
//   から得られる。VrmAnimationMenu が Editor にあるのは BVH 読込と
//   ファイルダイアログのためだけなので、書き出し自体に UnityEditor は要らない。
//
// ============================================================
// 出力の流れ（VrmAnimationMenu.cs:66-125 と同じ順序）
// ============================================================
//
//   1. ExportingGltfData を作る。
//   2. VrmAnimationExporter.Prepare(skeletonRoot) で右手系のコピーを作る。
//   3. Export(addFrames) の中で
//        - SetPositionBoneAndParent（Hips の平行移動）
//        - AddRotationBoneAndParent（Humanoid ボーンの回転）
//        - フレームごとに poseFrame → AddFrame
//   4. ToGlbBytes して File.WriteAllBytes。
//
//   AddFrame は「元」の Transform の現在値を読む（VrmAnimationExporter.cs:33-38, 58-63）。
//   コピーではないので、poseFrame は skeletonRoot 側を動かせばよい。
//
// 【出力に含まれないもの】
//   VrmAnimationExporter が書くのは Hips の translation と Humanoid ボーンの
//   rotation だけ（VrmAnimationExporter.cs:102-143）。表情・視線・二次骨は載らない。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    public class VrmAnimationExporterImpl : IVrmAnimationExporter
    {
        // ================================================================
        // 登録
        // ================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLVrmAnimationBridge.Register(new VrmAnimationExporterImpl());
        }

        // ================================================================
        // IVrmAnimationExporter
        // ================================================================

        public bool IsAvailable => true;

        public VrmAnimationExportResult Export(
            GameObject skeletonRoot,
            IReadOnlyDictionary<HumanBodyBones, Transform> humanBones,
            Transform hipsPositionParent,
            IReadOnlyList<float> timesSec,
            Action<int> poseFrame,
            string outputPath)
        {
            if (skeletonRoot == null)
                return VrmAnimationExportResult.Failed("骨格がありません");
            if (humanBones == null || humanBones.Count == 0)
                return VrmAnimationExportResult.Failed("Humanoid ボーンがありません");
            if (timesSec == null || timesSec.Count == 0)
                return VrmAnimationExportResult.Failed("フレームがありません");
            if (poseFrame == null)
                return VrmAnimationExportResult.Failed("姿勢を作る手続きがありません");
            if (string.IsNullOrEmpty(outputPath))
                return VrmAnimationExportResult.Failed("出力パスが空です");

            if (!humanBones.TryGetValue(HumanBodyBones.Hips, out var hips) || hips == null)
                return VrmAnimationExportResult.Failed("Hips がありません");

            Transform positionParent = hipsPositionParent != null
                ? hipsPositionParent
                : skeletonRoot.transform;

            try
            {
                var data = new ExportingGltfData();
                byte[] glb;

                using (var exporter = new VrmAnimationExporter(data, new GltfExportSettings()))
                {
                    exporter.Prepare(skeletonRoot);

                    exporter.Export(vrma =>
                    {
                        vrma.SetPositionBoneAndParent(hips, positionParent);

                        foreach (var kv in humanBones)
                        {
                            if (kv.Value == null) continue;
                            if (kv.Key == HumanBodyBones.LastBone) continue;

                            Transform parent = kv.Value.parent != null
                                ? kv.Value.parent
                                : skeletonRoot.transform;

                            vrma.AddRotationBoneAndParent(kv.Key, kv.Value, parent);
                        }

                        for (int i = 0; i < timesSec.Count; i++)
                        {
                            poseFrame(i);
                            vrma.AddFrame(TimeSpan.FromSeconds(timesSec[i]));
                        }
                    });

                    glb = data.ToGlbBytes();
                }

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(outputPath, glb);

                return new VrmAnimationExportResult
                {
                    Success           = true,
                    OutputPath        = outputPath,
                    HumanoidBoneCount = humanBones.Count,
                    FrameCount        = timesSec.Count,
                    DurationSec       = timesSec[timesSec.Count - 1] - timesSec[0],
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmAnimationExporterImpl] {ex}");
                return VrmAnimationExportResult.Failed(ex.Message);
            }
        }
    }
}
