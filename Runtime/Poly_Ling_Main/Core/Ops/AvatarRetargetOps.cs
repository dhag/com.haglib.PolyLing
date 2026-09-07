// Runtime/Poly_Ling_Main/Core/Ops/AvatarRetargetOps.cs
// ============================================================
// Humanoid Avatar のリターゲット設定のオーサリング処理
// ============================================================
//
// 【なぜ要るか】
//   8項目を編集できるのは Editor の手動窓（ManualHumanoidAvatarWindow）だけで、
//   その値はどこにも残らなかった。PolyLing のモデルから Avatar を作る経路
//   （HierarchyExportWindow → AvatarBuildCore）は既定値の固定だった。
//
// 【値の丸め】
//   twist / stretch の 6 項目は 0〜1 に丸める。Unity の Avatar 画面が
//   その範囲のスライダーで、範囲外の値を入れても意味が無いため。
//   FeetSpacing は範囲が決まっていないので丸めない。
//
// 【null＝未設定】
//   ClearRetarget は null を書き戻す。未設定のとき Avatar 生成側は
//   Unity の既定（AvatarRetargetSettings.Default）を使う。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。UnityEngine の型のみ使う。
//
// ============================================================

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>Avatar リターゲット設定の付け外し。</summary>
    public static class AvatarRetargetOps
    {
        /// <summary>リターゲット設定を書き込む（未設定なら作る）。書けたら true。</summary>
        public static bool SetRetarget(ModelContext model, AvatarRetargetData data)
        {
            if (model == null || data == null) return false;

            model.AvatarRetarget = new AvatarRetargetData
            {
                UpperArmTwist     = Mathf.Clamp01(data.UpperArmTwist),
                LowerArmTwist     = Mathf.Clamp01(data.LowerArmTwist),
                UpperLegTwist     = Mathf.Clamp01(data.UpperLegTwist),
                LowerLegTwist     = Mathf.Clamp01(data.LowerLegTwist),
                ArmStretch        = Mathf.Clamp01(data.ArmStretch),
                LegStretch        = Mathf.Clamp01(data.LegStretch),
                FeetSpacing       = data.FeetSpacing,
                HasTranslationDoF = data.HasTranslationDoF,
            };
            return true;
        }

        /// <summary>リターゲット設定を未設定へ戻す。元から無ければ false。</summary>
        public static bool ClearRetarget(ModelContext model)
        {
            if (model?.AvatarRetarget == null) return false;
            model.AvatarRetarget = null;
            return true;
        }

        /// <summary>
        /// 編集の出発点になる設定。未設定なら Unity の既定値で作る。
        /// 返り値はコピーなので、書き換えても model には影響しない。
        /// </summary>
        public static AvatarRetargetData GetRetargetOrNew(ModelContext model)
        {
            if (model?.AvatarRetarget != null) return model.AvatarRetarget.Clone();
            return new AvatarRetargetData();
        }
    }
}
