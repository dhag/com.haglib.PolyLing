// TempMirrorControls.cs
// 各ツールのサブパネルに置く「一時ミラー」トグルボタンの共通 UI ブロック。
//
// ミラー軸・平面オフセット等のパラメータはこのブロックでは編集しない。
// 左ペインの「一時ミラー」パネルで指定した値（TempMirrorSettings）をそのまま使う。
// ツールごとに同じパラメータ UI を並べると設定が食い違うため、入口は 1 つに保つ。
//
// ボタンは実体化中かどうかで背景色が変わる（ドラッグで範囲指定ボタンと同じ方式）。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 「一時ミラー」トグルボタンを生成する共通ヘルパー。
    /// 生成した要素は呼び出し側が任意のコンテナへ Add する。
    /// </summary>
    public class TempMirrorControls
    {
        private Button _button;
        private Label  _statusLabel;

        private Func<TempMirrorController> _getController;
        private Func<int>                  _getOwnerToken;

        // 実体化中／非実体化のボタン背景色。
        // 非実体化に StyleKeyword.Null を入れると ApplyDarkTheme のインライン背景が外れ、
        // USS 既定の明るい灰色に戻って白文字が読めなくなるため、明示色を入れる。
        private static readonly StyleColor ActiveColor   = new StyleColor(new Color(0.3f, 0.6f, 1.0f, 0.8f));
        private static readonly StyleColor InactiveColor = PlayerLayoutRoot.BtnInactiveColor;

        /// <summary>
        /// 「一時ミラー」ボタンと状態ラベルを含むブロックを作る。
        /// </summary>
        /// <param name="getController">一時ミラーのコントローラ取得。</param>
        /// <param name="getOwnerToken">このツールの識別値（InteractionMode を int 化したもの）。</param>
        public VisualElement Build(
            Func<TempMirrorController> getController,
            Func<int> getOwnerToken)
        {
            _getController = getController;
            _getOwnerToken = getOwnerToken;

            var block = new VisualElement();
            block.style.marginTop    = 4;
            block.style.marginBottom = 4;

            _button = new Button(() =>
            {
                var c = _getController?.Invoke();
                if (c == null) return;
                c.Toggle(_getOwnerToken?.Invoke() ?? -1);
                Refresh();
            });
            _button.text = "一時ミラー";
            _button.style.height   = 24;
            _button.style.fontSize = 10;
            _button.tooltip =
                "選択中のメッシュに反対側の実体を一時的に生やす。"
                + "軸・オフセット等は左ペイン「一時ミラー」の設定を使う。"
                + "他のツールへ移ると自動で解除される。";
            block.Add(_button);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            block.Add(_statusLabel);

            Refresh();
            return block;
        }

        /// <summary>ボタンの表示をコントローラの実状態へ合わせる。</summary>
        public void Refresh()
        {
            if (_button == null) return;

            var c = _getController?.Invoke();
            bool active = c != null && c.IsActive;

            _button.text = active ? "一時ミラー：解除" : "一時ミラー";
            _button.style.backgroundColor = active ? ActiveColor : InactiveColor;

            if (_statusLabel != null)
                _statusLabel.text = c?.LastMessage ?? "";
        }
    }
}
