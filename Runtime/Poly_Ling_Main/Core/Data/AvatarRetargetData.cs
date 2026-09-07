// Runtime/Poly_Ling_Main/Core/Data/AvatarRetargetData.cs
// ============================================================
// Humanoid Avatar のリターゲット設定8項目（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   Unity の HumanDescription が持つリターゲット設定
//   （upperArmTwist / lowerArmTwist / upperLegTwist / lowerLegTwist /
//     armStretch / legStretch / feetSpacing / hasTranslationDoF）を
//   モデルレベルで保持する。Avatar 生成の入力になる。
//
// 【なぜ Runtime に置くか】
//   同じ8項目の struct が Editor 側にある（AvatarRetargetSettings.cs）。
//   あちらは AvatarBuildCore へ渡す引数の型で、Editor アセンブリに属するため
//   ModelContext には持たせられない。保存する値はこちらが正本で、
//   Editor 境界（AvatarRetargetSettings.FromData）で写す。
//
// 【null＝未設定】
//   ModelContext.AvatarRetarget が null のときは Avatar 生成側が
//   AvatarRetargetSettings.Default（Unity の既定）を使う。
//   本POCOの初期値も同じ数値にしてあるが、「未設定」と
//   「既定値を明示的に持っている」は別の状態として区別する。
//
// 【値の意味】
//   twist 4項目 … 捩りボーンへ回転を何割配るか（0〜1）。
//   stretch 2項目 … IK で骨をどれだけ伸ばしてよいか（0〜1）。
//   feetSpacing … 両足の間隔の補正。
//   hasTranslationDoF … 移動の自由度を持たせるか。
//
// 【依存】
//   UnityEngine の型を使わない。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// Humanoid Avatar のリターゲット設定8項目（純POCO）。
    /// ModelContext.AvatarRetarget として1つ保持する。null＝未設定。
    /// </summary>
    [Serializable]
    public class AvatarRetargetData
    {
        /// <summary>上腕の捩り配分（0〜1）。</summary>
        public float UpperArmTwist { get; set; } = 0.5f;

        /// <summary>前腕の捩り配分（0〜1）。</summary>
        public float LowerArmTwist { get; set; } = 0.5f;

        /// <summary>大腿の捩り配分（0〜1）。</summary>
        public float UpperLegTwist { get; set; } = 0.5f;

        /// <summary>下腿の捩り配分（0〜1）。</summary>
        public float LowerLegTwist { get; set; } = 0.5f;

        /// <summary>腕の伸び代（0〜1）。</summary>
        public float ArmStretch { get; set; } = 0.05f;

        /// <summary>脚の伸び代（0〜1）。</summary>
        public float LegStretch { get; set; } = 0.05f;

        /// <summary>両足の間隔の補正。</summary>
        public float FeetSpacing { get; set; } = 0f;

        /// <summary>移動の自由度を持たせるか。</summary>
        public bool HasTranslationDoF { get; set; } = false;

        /// <summary>ディープコピー。</summary>
        public AvatarRetargetData Clone()
        {
            return new AvatarRetargetData
            {
                UpperArmTwist     = this.UpperArmTwist,
                LowerArmTwist     = this.LowerArmTwist,
                UpperLegTwist     = this.UpperLegTwist,
                LowerLegTwist     = this.LowerLegTwist,
                ArmStretch        = this.ArmStretch,
                LegStretch        = this.LegStretch,
                FeetSpacing       = this.FeetSpacing,
                HasTranslationDoF = this.HasTranslationDoF,
            };
        }
    }
}
