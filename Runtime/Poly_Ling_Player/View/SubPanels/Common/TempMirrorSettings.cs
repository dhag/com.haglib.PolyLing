// TempMirrorSettings.cs
// 一時ミラー（ミラー実体化 / 解除）のパラメータを一箇所に集約する共有設定。
//
// これまでパラメータは PlayerMirrorSubPanel の private フィールドに閉じていたため、
// 左ペインの「一時ミラー」パネルからしか値を指定できなかった。
// 各ツール（スカルプト等）の中に置く「一時ミラー」ボタンは同じ値を使う必要があるので、
// 保持場所をここへ移し、パネルとツール内ボタンの双方がここを読み書きする。
//
// 値はセッション中のみ保持する（CSV 等への永続化は行わない）。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 一時ミラーの実体化・解除パラメータ。
    /// 「一時ミラー」パネルと、各ツール内の「一時ミラー」ボタンで共有する。
    /// </summary>
    public static class TempMirrorSettings
    {
        /// <summary>ミラー軸（0:X, 1:Y, 2:Z）。メッシュが MirrorType &gt; 0 のときはメッシュ側の設定が優先される。</summary>
        public static int MirrorAxis = 0;

        /// <summary>境界判定のしきい値</summary>
        public static float Threshold = 0.0001f;

        /// <summary>ミラー平面のオフセット（ローカル座標）</summary>
        public static float PlaneOffset = 0f;

        /// <summary>UV の U を反転するか</summary>
        public static bool FlipU = false;

        /// <summary>境界の決め方</summary>
        public static MirrorBoundaryMode BoundaryMode = MirrorBoundaryMode.Threshold;

        /// <summary>境界頂点をミラー平面へ射影するか</summary>
        public static bool ProjectBoundary = true;

        /// <summary>解除時にどちら側の編集結果を残すか</summary>
        public static WriteBackMode WriteBack = WriteBackMode.OriginalSideOnly;
    }
}
