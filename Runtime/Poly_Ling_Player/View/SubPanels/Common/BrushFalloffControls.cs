// BrushFalloffControls.cs
// 「距離モード / フォールオフ」の共通 UI ブロック。
//
// マグネット（PlayerVertexMoveSubPanel）・スカルプト（PlayerSculptSubPanel）・
// スキンWペイント（PlayerSkinWeightPaintPanel）でコントロールの種類・ラベル・
// 選択肢がバラバラだったため、マグネットの形に一本化した。
//   ・DropdownField を使う（セグメントボタンではない）
//   ・並びは「距離モード」→「フォールオフ」
//   ・フォールオフは リニア / ガウス / 円 / シャープ の 4 種（FalloffType）
//   ・距離モードは 直線 / リンク距離 の 2 種（DistanceMode）
//
// 減衰の計算自体は Poly_Ling.Tools.FalloffHelper.Calculate に統一されており、
// この UI は値を選ばせるだけ。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 距離モード・フォールオフの DropdownField を生成する共通ヘルパー。
    /// 生成した要素は呼び出し側が任意のコンテナへ Add する。
    /// </summary>
    public class BrushFalloffControls
    {
        /// <summary>フォールオフ選択肢（マグネット基準）。</summary>
        public static readonly string[]      FalloffLabels =
            { "リニア", "ガウス", "円", "シャープ" };
        public static readonly FalloffType[] FalloffValues =
            { FalloffType.Linear, FalloffType.Gaussian, FalloffType.Sphere, FalloffType.Sharp };

        /// <summary>距離モード選択肢（マグネット基準）。</summary>
        public static readonly string[]       DistanceModeLabels =
            { "直線", "リンク距離" };
        public static readonly DistanceMode[] DistanceModeValues =
            { DistanceMode.Euclidean, DistanceMode.Link };

        private DropdownField _falloffDropdown;
        private DropdownField _distanceDropdown;

        private Func<FalloffType>   _getFalloff;
        private Action<FalloffType> _setFalloff;
        private Func<DistanceMode>   _getDistance;
        private Action<DistanceMode> _setDistance;

        /// <summary>距離モードのドロップダウンを作る。</summary>
        public DropdownField BuildDistanceDropdown(
            Func<DistanceMode> get, Action<DistanceMode> set)
        {
            _getDistance = get;
            _setDistance = set;

            _distanceDropdown = new DropdownField(
                "距離モード", new List<string>(DistanceModeLabels), 0);
            _distanceDropdown.style.color        = new StyleColor(Color.white);
            _distanceDropdown.style.marginBottom = 3;
            _distanceDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = Array.IndexOf(DistanceModeLabels, e.newValue);
                if (idx >= 0) _setDistance?.Invoke(DistanceModeValues[idx]);
            });

            SyncDistance();
            return _distanceDropdown;
        }

        /// <summary>フォールオフのドロップダウンを作る。</summary>
        public DropdownField BuildFalloffDropdown(
            Func<FalloffType> get, Action<FalloffType> set)
        {
            _getFalloff = get;
            _setFalloff = set;

            _falloffDropdown = new DropdownField(
                "フォールオフ", new List<string>(FalloffLabels), 1);
            _falloffDropdown.style.color        = new StyleColor(Color.white);
            _falloffDropdown.style.marginBottom = 3;
            _falloffDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = Array.IndexOf(FalloffLabels, e.newValue);
                if (idx >= 0) _setFalloff?.Invoke(FalloffValues[idx]);
            });

            SyncFalloff();
            return _falloffDropdown;
        }

        /// <summary>表示を現在値へ合わせ直す（Refresh から呼ぶ）。</summary>
        public void Sync()
        {
            SyncDistance();
            SyncFalloff();
        }

        private void SyncDistance()
        {
            if (_distanceDropdown == null || _getDistance == null) return;
            int idx = Array.IndexOf(DistanceModeValues, _getDistance());
            _distanceDropdown.SetValueWithoutNotify(
                idx >= 0 ? DistanceModeLabels[idx] : DistanceModeLabels[0]);
        }

        private void SyncFalloff()
        {
            if (_falloffDropdown == null || _getFalloff == null) return;
            int idx = Array.IndexOf(FalloffValues, _getFalloff());
            // 一覧に無い値（Smooth / Root / Constant 等）が入っている場合は
            // 既定のガウスを表示する。値そのものは書き換えない。
            _falloffDropdown.SetValueWithoutNotify(
                idx >= 0 ? FalloffLabels[idx] : FalloffLabels[1]);
        }
    }
}
