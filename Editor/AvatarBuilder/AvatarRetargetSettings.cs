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

using Poly_Ling.Data;

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

        /// <summary>
        /// モデルが持つリターゲット設定（Runtime の純POCO）から作る。
        /// null（未設定）なら Unity 既定を返す。
        ///
        /// 【なぜ写すか】
        ///   本 struct は Editor アセンブリに属するため ModelContext には
        ///   持たせられない。保存する値は Poly_Ling.Data.AvatarRetargetData が
        ///   正本で、Avatar 生成の入口でこちらへ写す。
        /// </summary>
        public static AvatarRetargetSettings FromData(AvatarRetargetData data)
        {
            if (data == null) return Default;

            return new AvatarRetargetSettings
            {
                upperArmTwist = data.UpperArmTwist,
                lowerArmTwist = data.LowerArmTwist,
                upperLegTwist = data.UpperLegTwist,
                lowerLegTwist = data.LowerLegTwist,
                armStretch = data.ArmStretch,
                legStretch = data.LegStretch,
                feetSpacing = data.FeetSpacing,
                hasTranslationDoF = data.HasTranslationDoF
            };
        }

        /// <summary>
        /// HumanDescription が持つ 8 項目から Runtime の純POCOを作る。
        /// 既存 Avatar の取り込み（HierarchyImportWindow）で使う。
        /// </summary>
        public static AvatarRetargetData ToData(UnityEngine.HumanDescription desc)
        {
            return new AvatarRetargetData
            {
                UpperArmTwist = desc.upperArmTwist,
                LowerArmTwist = desc.lowerArmTwist,
                UpperLegTwist = desc.upperLegTwist,
                LowerLegTwist = desc.lowerLegTwist,
                ArmStretch = desc.armStretch,
                LegStretch = desc.legStretch,
                FeetSpacing = desc.feetSpacing,
                HasTranslationDoF = desc.hasTranslationDoF
            };
        }

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
