// PLFbxBridge.cs
// ============================================================
// FBX 読み書きの実装への Runtime アクセサ
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md。
//
// ■ 分離
//   書き出しは Autodesk FBX SDK（com.autodesk.fbx）を使う。その型は PolyLing.Runtime に持ち込まず、
//   実装は PolyLing.Fbx アセンブリに置いて [RuntimeInitializeOnLoadMethod] で登録する
//   （glTF の PLGltfBridge と同じ形。IVrm10Exporter.cs 冒頭の分離規約）。
//   読み込みは ufbx（UfbxNative）で、PolyLing.Runtime から直接呼ぶ。
//
// ■ 書き出しは段 2 で足した（IFbxExporter）。読み込みの口は段 4 で足す。
// ============================================================

using UnityEngine;

namespace Poly_Ling.Fbx
{
    /// <summary>Autodesk FBX SDK の実装（PolyLing.Fbx）が返す情報。</summary>
    public interface IFbxSdkInfo
    {
        /// <summary>FBX SDK の版（FbxManager.GetVersion）。</summary>
        string Version { get; }
    }

    /// <summary>FBX 書き出し（実装は PolyLing.Fbx の FbxExporterImpl）。</summary>
    public interface IFbxExporter
    {
        /// <summary>実際に書き出せる実装が登録されているか。</summary>
        bool IsAvailable { get; }

        /// <summary>ModelContext を FBX として書き出す。</summary>
        FbxExportResult Export(Poly_Ling.Context.ModelContext model, string outputPath, FbxExportSettings settings);

        /// <summary>
        /// モーションをモデルへ当てて標本化し、モデル（または骨格だけ）とアニメーションを FBX に書く。
        /// モデルの姿勢は終わったら戻す。
        /// </summary>
        FbxExportResult ExportMotion(Poly_Ling.Context.ModelContext model, Poly_Ling.Motion.MotionClipDTO clip,
                                     string outputPath, FbxExportSettings settings, FbxMotionExportOptions motion);
    }

    public class FbxExporterNull : IFbxExporter
    {
        public bool IsAvailable => false;

        public FbxExportResult Export(Poly_Ling.Context.ModelContext model, string outputPath, FbxExportSettings settings)
        {
            Debug.LogError($"[PolyLing] FBX エクスポータが利用できません（PolyLing.Fbx が未登録） ({outputPath})");
            return FbxExportResult.Failed("FBX エクスポータが利用できません");
        }

        public FbxExportResult ExportMotion(Poly_Ling.Context.ModelContext model, Poly_Ling.Motion.MotionClipDTO clip,
                                            string outputPath, FbxExportSettings settings, FbxMotionExportOptions motion)
            => Export(model, outputPath, settings);
    }

    public static class PLFbxBridge
    {
        private static IFbxSdkInfo  _sdk;
        private static IFbxExporter _exporter;

        /// <summary>PolyLing.Fbx の [RuntimeInitializeOnLoadMethod] から呼ばれる。</summary>
        public static void RegisterSdk(IFbxSdkInfo impl) { _sdk = impl; }

        /// <summary>PolyLing.Fbx の [RuntimeInitializeOnLoadMethod] から呼ばれる。</summary>
        public static void RegisterExporter(IFbxExporter impl) { _exporter = impl; }

        public static IFbxExporter Exporter => _exporter ?? (_exporter = new FbxExporterNull());

        /// <summary>
        /// FBX SDK を呼べるか確かめ、版を返す。
        /// 呼べないとき false と理由（実装が未登録・ネイティブ DLL が無い）。
        /// </summary>
        public static bool TryGetSdkVersion(out string version, out string reason)
        {
            version = "";
            reason  = null;
            if (_sdk == null) { reason = "FBX SDK の実装（PolyLing.Fbx）が登録されていません"; return false; }
            try
            {
                version = _sdk.Version ?? "";
                return true;
            }
            catch (System.Exception e)
            {
                // ネイティブ DLL（UnityFbxSdkNative）が無いと型の初期化で例外になる。
                reason = "FBX SDK を呼べません: " + (e.InnerException?.Message ?? e.Message);
                return false;
            }
        }

        /// <summary>FBX SDK と ufbx の状態を 1 行で返す。</summary>
        public static string Describe()
        {
            string sdk  = TryGetSdkVersion(out string sv, out string sr)    ? sv : "なし（" + sr + "）";
            string ufbx = UfbxNative.TryGetVersion(out string uv, out string ur) ? uv : "なし（" + ur + "）";
            return $"FBX SDK {sdk} / ufbx {ufbx}";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogStatus()
        {
            Debug.Log("[PolyLing] FBX: " + Describe());
        }
    }
}
