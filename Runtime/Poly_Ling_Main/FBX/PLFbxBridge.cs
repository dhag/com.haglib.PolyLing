// PLFbxBridge.cs
// ============================================================
// FBX 読み書きの入口
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md。
//
// ■ 実装
//   書き出しは FbxExporter（自前。段 7）、読み込みは FbxScene・FbxImporter（自前。段 8。FBX 7.x）。
//   どちらも PolyLing.Runtime にあり、登録の仕組みは無い。
// ============================================================

using UnityEngine;

namespace Poly_Ling.Fbx
{
    /// <summary>FBX 書き出し（実装は FbxExporter）。</summary>
    public interface IFbxExporter
    {
        /// <summary>実際に書き出せるか（自前の実装なので常に true）。</summary>
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

    public static class PLFbxBridge
    {
        private static readonly IFbxExporter _exporter = new FbxExporter();

        public static IFbxExporter Exporter => _exporter;

        /// <summary>FBX の読み書きの状態を 1 行で返す。</summary>
        public static string Describe() => "書き出し PolyLing / 読み込み PolyLing（FBX 7.x）";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogStatus()
        {
            Debug.Log("[PolyLing] FBX: " + Describe());
        }
    }
}
