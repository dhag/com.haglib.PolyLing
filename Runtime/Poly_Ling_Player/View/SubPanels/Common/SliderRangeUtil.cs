// SliderRangeUtil.cs
// 「スライダ＋テキストボックス＋上下限」という組のための共通ヘルパー。
//
// 【なぜ必要か】
// UIToolkit の Slider は lowValue / highValue を変えると現在値を新レンジへ再クランプする。
// そのため
//   ・値を先に入れてからレンジを広げる → 値は旧レンジで切り詰められたまま残り、
//     つまみの位置とテキストボックスの数字が食い違う
//   ・レンジを片方ずつ入れる → 途中で low > high の反転状態を経由し、そこで値が壊れる
// が起きる。順序を各パネルの書き方に任せると同じ不具合が繰り返し出るため、
// 「union へ広げる → 正確なレンジを入れる → 値を入れる」の順をここに固定する。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>スライダのレンジ・値をまとめて設定するヘルパー。</summary>
    public static class SliderRangeUtil
    {
        /// <summary>上下限が潰れないようにする最小の幅。</summary>
        public const float MinSpan = 0.001f;

        /// <summary>
        /// スライダへレンジと値を設定する。通知は出さない（呼び出し側の再入ガード前提）。
        /// 必ず「レンジ → 値」の順で入れる。
        /// </summary>
        public static void SetRangeAndValue(Slider slider, float min, float max, float value)
        {
            if (slider == null) return;

            if (max < min + MinSpan) max = min + MinSpan;

            // 片方ずつ入れると low > high の反転を経由して値が壊れるため、
            // いったん新旧の和集合まで広げてから正確なレンジへ絞る。
            slider.lowValue  = Mathf.Min(slider.lowValue,  min);
            slider.highValue = Mathf.Max(slider.highValue, max);
            slider.lowValue  = min;
            slider.highValue = max;

            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
        }

        /// <summary>
        /// テキストボックスに入った値を採用できるよう、上下限を必要なぶんだけ広げる。
        /// 範囲外の入力を黙ってクランプすると、上下限が折りたたみの中にあるため
        /// 「入力した数字が勝手に変わる」ように見える。広げる側に倒す。
        /// </summary>
        public static void ExpandToInclude(float value, ref float min, ref float max)
        {
            if (value < min) min = value;
            if (value > max) max = value;
            if (max < min + MinSpan) max = min + MinSpan;
        }
    }
}
