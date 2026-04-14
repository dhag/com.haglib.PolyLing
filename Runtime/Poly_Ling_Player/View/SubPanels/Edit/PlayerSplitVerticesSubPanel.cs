// PlayerSplitVerticesSubPanel.cs
// SplitVerticesTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerSplitVerticesSubPanel
    {
        public Func<SplitVerticesToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _selectedLabel;
        private Label         _splittableLabel;
        private Button        _splitBtn;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Split Vertices / 頂点分割"));
            _root.Add(new HelpBox(
                "選択頂点を面ごとに独立したコピーに分離します。\n2面以上に共有されている頂点が対象です。",
                HelpBoxMessageType.Info));

            _selectedLabel = InfoLabel();
            _root.Add(_selectedLabel);

            _splittableLabel = InfoLabel();
            _root.Add(_splittableLabel);

            _splitBtn = new Button(() => GetH()?.TriggerSplit()) { text = "分割実行" };
            _splitBtn.style.height    = 30;
            _splitBtn.style.marginTop = 6;
            _root.Add(_splitBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            int selCount       = h.SelectedVertexCount;
            int splittableCount = h.GetSplittableCount();

            _selectedLabel.text  = $"選択中: {selCount} 頂点";
            _splittableLabel.text = splittableCount > 0
                ? $"分割対象: {splittableCount} 頂点（2面以上共有）"
                : "分割対象なし（全て単独または未選択）";

            if (_splitBtn != null)
                _splitBtn.SetEnabled(splittableCount > 0);
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
