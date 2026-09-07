// Runtime/Poly_Ling_Main/Core/Data/CoordinateConventionData.cs
// ============================================================
// PMX / MQO の座標規約（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   PMX・MQO と Unity のあいだの倍率と軸反転をモデルレベルで保持する。
//   読み込み・書き出しだけでなく、VMD／統合モーションの位置スケールにも効く
//   （VMDApplier.cs / MotionClipApplier.cs のコメント参照）。
//
// 【なぜ editorstate.csv から分けたか】
//   従来はカメラ角度やナイフツールの設定と同じ editorstate.csv に入っていた。
//   あちらは「捨てても表示が戻るだけ」の表示状態で、こちらは捨てると
//   座標が 10 倍ずれる規約であり、性質が違う。混ぜると取り違えを招く。
//
// 【null＝未設定】
//   ModelContext.CoordinateConvention が null のときは、読み手が持つ既定値
//   （EditorStateContext の初期値）がそのまま使われる。本POCOの初期値も
//   同じ数値にしてあるが、「未設定」と「既定値を明示的に持っている」は
//   別の状態として区別する。
//
// 【軸反転の意味】
//   Unity は左手系。PMX は左手系だがモデルが Z マイナス方向を向くため、
//   既定では X と Z の両方を反転する。MQO は右手系のため X だけ反転する。
//   既定値はこの規約に合わせてある。
//
// 【依存】
//   UnityEngine の型を使わない。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// PMX / MQO の座標規約（純POCO）。
    /// ModelContext.CoordinateConvention として1つ保持する。null＝未設定。
    /// </summary>
    [Serializable]
    public class CoordinateConventionData
    {
        /// <summary>PMX→Unity の倍率。</summary>
        public float PmxUnityRatio { get; set; } = 0.1f;

        /// <summary>PMX の X 軸を反転するか。</summary>
        public bool PmxFlipX { get; set; } = true;

        /// <summary>PMX の Z 軸を反転するか。X と併用で Y 軸 180 度回転になる。</summary>
        public bool PmxFlipZ { get; set; } = true;

        /// <summary>MQO→Unity の倍率。</summary>
        public float MqoUnityRatio { get; set; } = 0.01f;

        /// <summary>MQO の X 軸を反転するか。</summary>
        public bool MqoFlipX { get; set; } = true;

        /// <summary>MQO の Z 軸を反転するか。</summary>
        public bool MqoFlipZ { get; set; } = false;

        /// <summary>ディープコピー。</summary>
        public CoordinateConventionData Clone()
        {
            return new CoordinateConventionData
            {
                PmxUnityRatio = this.PmxUnityRatio,
                PmxFlipX      = this.PmxFlipX,
                PmxFlipZ      = this.PmxFlipZ,
                MqoUnityRatio = this.MqoUnityRatio,
                MqoFlipX      = this.MqoFlipX,
                MqoFlipZ      = this.MqoFlipZ,
            };
        }
    }
}
