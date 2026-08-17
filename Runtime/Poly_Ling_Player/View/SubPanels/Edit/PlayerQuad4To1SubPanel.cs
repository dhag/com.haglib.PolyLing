// PlayerQuad4To1SubPanel.cs
// Quad4To1Tool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerQuad4To1SubPanel
    {
        public Func<Quad4To1ToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _targetLabel;
        private Label         _statusLabel;
        private Button        _mergeBtn;

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

            _root.Add(Header("Quad 4 to 1 / 四角形4→1統合"));
            _root.Add(new HelpBox(
                "選択した頂点を共有する四角形4枚を、四隅を結ぶ四角形1枚に張り替えます。\n" +
                "選択した頂点と、それに接続する4頂点は新しい面から外れます。\n" +
                "四角形が4枚ちょうどでない、または境界の頂点のときは対象外です。\n" +
                "外れた頂点は、どの面からも使われなくなったものだけ消えます。\n" +
                "複数オブジェクト・複数頂点に対応。同じ面を共有する頂点どうしは干渉するため除外します。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            _mergeBtn = new Button(() =>
            {
                GetH?.Invoke()?.TriggerMerge();
                Refresh();
            })
            { text = "四角形4→1 実行" };
            _mergeBtn.style.height    = 30;
            _mergeBtn.style.marginTop = 6;
            _root.Add(_mergeBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            UpdateStats();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateStats()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            var info = h.Inspect();

            if (!info.CanExecute)
            {
                if (_targetLabel != null)
                    _targetLabel.text = $"選択中: {h.SelectedVertexCount} 頂点  /  除外: {info.SkippedCount} 頂点";
                if (_statusLabel != null) _statusLabel.text = info.Reason ?? "";
                _mergeBtn?.SetEnabled(false);
                return;
            }

            if (_targetLabel != null)
                _targetLabel.text = $"対象: {info.ObjectCount} オブジェクト / {info.TargetCount} 頂点"
                                  + (info.SkippedCount > 0 ? $"　（除外 {info.SkippedCount}）" : "");

            if (_statusLabel != null)
                _statusLabel.text = $"{info.RemovedFaceTotal} 面と {info.RemovedVertexTotal} 頂点が消えます";

            _mergeBtn?.SetEnabled(true);
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
