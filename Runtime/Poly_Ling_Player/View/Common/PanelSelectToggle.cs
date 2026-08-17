// PanelSelectToggle.cs
// 右ペインのパネルへ「ビューポートで選択する」チェックを差し込む共通部品。
//
// 【目的】カテゴリ3のパネル（3D操作を無効にして開くパネル）でも、頂点・辺・面を
//   選べるようにするか否かをパネルごとに切り替える。既定は ON。
//
// 【状態】PlayerUiPrefs に "PanelSelect." + key で保存する（端末ローカル）。
//
// 【他パネルへの導入手順】
//   1. サブパネルの Build 直後に AttachPanelSelectToggle(section, key) を1行足す。
//   2. その Show～Panel を ShowRightPanelSelectable(section, btn, key) に差し替える。
//   いずれも PolyLingPlayerViewerCore 側の2手だけで済む。
//
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// パネルごとの「ビューポートで選択する」チェック。既定 ON。
    /// </summary>
    public static class PanelSelectToggle
    {
        /// <summary>永続化キーの接頭辞。</summary>
        private const string KeyPrefix = "PanelSelect.";

        /// <summary>差し込んだトグルの名前。二重 Attach の判定に使う。</summary>
        private const string ToggleName = "panel-select-toggle";

        /// <summary>そのパネルで選択を許可するか。未保存なら true。</summary>
        public static bool IsEnabled(string key)
            => string.IsNullOrEmpty(key) || PlayerUiPrefs.GetBool(KeyPrefix + key, true);

        /// <summary>許可状態を保存する。</summary>
        public static void SetEnabled(string key, bool value)
        {
            if (string.IsNullOrEmpty(key)) return;
            PlayerUiPrefs.SetBool(KeyPrefix + key, value);
        }

        /// <summary>
        /// section の先頭へチェックを差し込む。既に差し込み済みなら何もしない。
        /// onChanged はチェック変更後に呼ぶ（保存はこの中で済ませてある）。
        /// </summary>
        public static Toggle Attach(
            VisualElement section, string key, Action<bool> onChanged,
            string label = "ビューポートで選択する")
        {
            if (section == null || string.IsNullOrEmpty(key)) return null;

            var exist = section.Q<Toggle>(ToggleName);
            if (exist != null) return exist;

            var toggle = new Toggle(label)
            {
                name  = ToggleName,
                value = IsEnabled(key),
            };
            toggle.style.marginLeft   = 2;
            toggle.style.marginBottom = 2;
            toggle.style.fontSize     = 11;

            toggle.RegisterValueChangedCallback(e =>
            {
                SetEnabled(key, e.newValue);
                onChanged?.Invoke(e.newValue);
            });

            section.Insert(0, toggle);
            return toggle;
        }
    }
}
