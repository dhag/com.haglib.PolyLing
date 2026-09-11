// Vrm10ImporterImpl.cs
// ============================================================
// IVrm10Importer の UniVRM 実装
// ============================================================
//
// 【分離規約】IVrm10Importer.cs 冒頭（さらに IVrm10Exporter.cs 冒頭）のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属する。VRM パッケージが無い環境では
//   アセンブリごとコンパイル対象から外れる。
//
// 【登録】
//   [RuntimeInitializeOnLoadMethod] で PLVrm10ImportBridge へ登録する。
//   Play 時にしか走らないため、Editor 拡張（非 Play）からは当面使えない。
//
// ============================================================
// 読み込みの流れ（GameObject / Material を UniVRM に作らせない）
// ============================================================
//
//   1. File.ReadAllBytes → GlbLowLevelParser.Parse → GltfData（Vrm10.cs:59 と同じ）
//   2. Vrm10Data.Parse。VRM 1.0 でなければ null（Vrm10Data.cs:29-35）。
//      AllowVrm0 なら Vrm10Data.Migrate で 1.0 へ移行（同 :44-127）。
//   3. VrmToPolyLingConverter が ModelReader.Read で VrmLib.Model（Unity 座標）を作り、
//      VRM 拡張データと合わせて ModelContext の材料を組む。
//   4. finally で GltfData を破棄する（移行した場合は移行分も）。
//      VrmLib.Model の頂点バッファは GltfData の NativeArray を指すので、
//      変換はすべて破棄前に終える。
//
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    public class Vrm10ImporterImpl : IVrm10Importer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLVrm10ImportBridge.Register(new Vrm10ImporterImpl());
        }

        public bool IsAvailable => true;

        public Vrm10ImportResult Import(string filePath, Vrm10ImportSettings settings)
        {
            if (string.IsNullOrEmpty(filePath))
                return Vrm10ImportResult.Failed("ファイルパスが空です");
            if (!File.Exists(filePath))
                return Vrm10ImportResult.Failed($"ファイルが見つかりません: {filePath}");

            settings = settings ?? Vrm10ImportSettings.CreateDefault();

            GltfData data     = null;
            GltfData migrated = null;

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                data = new GlbLowLevelParser(filePath, bytes).Parse();

                bool fromVrm0 = false;
                var vrm = Vrm10Data.Parse(data);
                if (vrm == null)
                {
                    if (!settings.AllowVrm0)
                        return Vrm10ImportResult.Failed("VRM 1.0 ではありません（VRM 0.x の移行は無効になっています）");

                    migrated = Vrm10Data.Migrate(data, out vrm, out var migration);
                    if (migrated == null || vrm == null)
                        return Vrm10ImportResult.Failed(
                            "VRM として読めません: " + (migration?.Message ?? "不明な理由"));
                    fromVrm0 = true;
                }

                var result = new VrmToPolyLingConverter(vrm, filePath, settings).Convert();
                result.SourceIsVrm0 = fromVrm0;

                foreach (string w in result.Warnings)
                    Debug.LogWarning($"[Vrm10Importer] {w}");

                Debug.Log(
                    $"[Vrm10Importer] Import successful{(fromVrm0 ? "（VRM 0.x から移行）" : "")}: " +
                    $"ボーン {result.BoneCount}, メッシュ {result.MeshCount}, 頂点 {result.VertexCount}, " +
                    $"モーフ {result.MorphCount}, 表情 {result.ExpressionCount}, " +
                    $"humanoid {result.HumanoidBoneCount}, 揺れ {result.SpringCount} チェーン / " +
                    $"コライダー {result.ColliderCount}, ノード制約 {result.ConstraintCount}, " +
                    $"材質 {result.MaterialCount}, テクスチャ {result.TextureCount} ← {filePath}");

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Vrm10Importer] Import failed: {ex.Message}\n{ex.StackTrace}");
                return Vrm10ImportResult.Failed(ex.Message);
            }
            finally
            {
                migrated?.Dispose();
                data?.Dispose();
            }
        }
    }
}
