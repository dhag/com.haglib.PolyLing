// VrmAnimationExportTypes.cs
// ============================================================
// VRM アニメーション書き出しの設定・結果（純POCO）
// ============================================================
//
// 【分離規約】規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルには VRM パッケージ（UniGLTF / VrmLib / UniVRM10）の型を持ち込まない。
//
// 【設定がインターフェースに渡らない理由】
//   VrmAnimationExportSettings は骨格を組む側（UnityClipVrmAnimationSource）が
//   消費する。実装側（PolyLing.Vrm10）へ渡す設定は現状 1 つも無い。
//   詳細は IVrmAnimationExporter.cs 冒頭を見ること。
//
// ============================================================

using System;

namespace Poly_Ling.Vrm
{
    /// <summary>
    /// VRM アニメーション書き出し設定。
    /// 骨格を組む側が消費する。
    /// </summary>
    [Serializable]
    public class VrmAnimationExportSettings
    {
        /// <summary>
        /// 骨格の位置と Hips の平行移動に掛ける倍率。
        /// VRM はメートル。モデルの単位がメートルでないときにここで合わせる。
        /// </summary>
        public float Scale = 1.0f;

        /// <summary>サンプリングの毎秒枚数。</summary>
        public float Fps = 30.0f;

        /// <summary>サンプリング開始時刻（秒）。</summary>
        public float StartSec = 0.0f;

        /// <summary>
        /// サンプリング終了時刻（秒）。
        /// StartSec 以下のときはクリップの最終キー時刻を使う。
        /// </summary>
        public float EndSec = 0.0f;

        public VrmAnimationExportSettings Clone()
        {
            return new VrmAnimationExportSettings
            {
                Scale    = Scale,
                Fps      = Fps,
                StartSec = StartSec,
                EndSec   = EndSec,
            };
        }

        public static VrmAnimationExportSettings CreateDefault() => new VrmAnimationExportSettings();
    }

    /// <summary>
    /// VRM アニメーション書き出し結果。Vrm10ExportResult と同じ形にそろえてある。
    /// </summary>
    public class VrmAnimationExportResult
    {
        public bool   Success      { get; set; }
        public string OutputPath   { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>回転トラックを出した Humanoid ボーンの数。</summary>
        public int HumanoidBoneCount { get; set; }

        /// <summary>出力したフレーム数。</summary>
        public int FrameCount { get; set; }

        /// <summary>出力した長さ（秒）。</summary>
        public float DurationSec { get; set; }

        /// <summary>
        /// 出力は行えたが VRMA として不完全な場合の警告（null/空 = 警告なし）。
        /// </summary>
        public string Warning { get; set; }

        public static VrmAnimationExportResult Failed(string message)
        {
            return new VrmAnimationExportResult { Success = false, ErrorMessage = message };
        }
    }
}
