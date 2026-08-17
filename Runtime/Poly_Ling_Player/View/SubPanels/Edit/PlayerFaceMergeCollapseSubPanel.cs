// PlayerFaceMergeCollapseSubPanel.cs
// FaceMergeCollapseTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerFaceMergeCollapseSubPanel
    {
        public Func<FaceMergeCollapseToolHandler> GetH;

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

            _root.Add(Header("Face Merge Collapse / 面結合（頂点削除）"));
            _root.Add(new HelpBox(
                "選択した辺を挟む2枚の面を1枚に結合します。\n" +
                "辺に接する面が2枚でない場合、2枚が2辺以上を共有している場合は結合しません。\n" +
                "共有頂点2つは、ほかの面が使っていても新しい面から外します（前後の点がつながります）。\n" +
                "外すと面にならない場合は外しません（三角形同士 → 四角形）。\n" +
                "外した頂点は、どの面からも使われなくなったときだけ消えます。\n" +
                "複数オブジェクト・複数辺に対応。同じ面に関わる辺どうしは干渉するため除外します。",
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
            { text = "面結合（頂点削除） 実行" };
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
                    _targetLabel.text = $"選択中: {h.SelectedEdgeCount} 辺  /  除外: {info.SkippedCount} 辺";
                if (_statusLabel != null) _statusLabel.text = info.Reason ?? "";
                _mergeBtn?.SetEnabled(false);
                return;
            }

            if (_targetLabel != null)
                _targetLabel.text = $"対象: {info.ObjectCount} オブジェクト / {info.TargetCount} 辺"
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
