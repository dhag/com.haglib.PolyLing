// Editor/AvatarBuilder/AvatarRetargetSettings.cs
// ============================================================
// HumanDescription のリターゲット設定8項目を1つにまとめた構造体。
//
// 【役割】
//   AvatarBuildCore.BuildAndSaveAvatar へ渡し、HumanDescription の
//   upperArmTwist / lowerArmTwist / upperLegTwist / lowerLegTwist /
//   armStretch / legStretch / feetSpacing / hasTranslationDoF を決める。
//   Default は Unity の既定値と同じ。
// ============================================================

namespace Poly_Ling.EditorIO
{
    /// <summary>HumanDescription のリターゲット設定8項目。</summary>
    public struct AvatarRetargetSettings
    {
        public float upperArmTwist;
        public float lowerArmTwist;
        public float upperLegTwist;
        public float lowerLegTwist;
        public float armStretch;
        public float legStretch;
        public float feetSpacing;
        public bool hasTranslationDoF;

        /// <summary>Unity 既定値。</summary>
        public static AvatarRetargetSettings Default
        {
            get
            {
                return new AvatarRetargetSettings
                {
                    upperArmTwist = 0.5f,
                    lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f,
                    lowerLegTwist = 0.5f,
                    armStretch = 0.05f,
                    legStretch = 0.05f,
                    feetSpacing = 0.0f,
                    hasTranslationDoF = false
                };
            }
        }
    }
}
