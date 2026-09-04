// PlayerVertexDissolveSubPanel.cs
// VertexDissolveTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerVertexDissolveSubPanel
    {
        public Func<VertexDissolveToolHandler> GetH;
        public Func<ProjectContext>  GetView;
        public Action<PanelCommand>  SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 実行時点の選択オブジェクトをコマンドの対象として載せる。
        /// 受け口は照合するだけで選択を書き換えないため、ここで作った並びと
        /// 実行時点の選択が一致していることが前提になる。
        /// </summary>
        private int[] SelectedMasterIndices()
        {
            var sel = GetView?.Invoke()?.CurrentModel?.SelectedDrawableMeshIndices;
            return sel != null ? sel.ToArray() : System.Array.Empty<int>();
        }

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _targetLabel;
        private Label         _statusLabel;
        private Button        _dissolveBtn;

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

            _root.Add(Header("Vertex Dissolve / 頂点溶解"));
            _root.Add(new HelpBox(
                "選択した頂点を消して、その頂点を囲む面を1枚の面に張り替えます。\n" +
                "四角形4枚なら8頂点の面、三角形N枚ならN頂点の面になります。\n" +
                "頂点の周りが閉じていない（境界の）頂点は対象外です。\n" +
                "複数オブジェクト・複数頂点に対応。同じ面を共有する頂点どうしは干渉するため除外します。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            _dissolveBtn = new Button(() =>
            {
                SendCommand?.Invoke(new VertexDissolveCommand(ModelIndex, SelectedMasterIndices()));
                Refresh();
            })
            { text = "頂点溶解 実行" };
            _dissolveBtn.style.height    = 30;
            _dissolveBtn.style.marginTop = 6;
            _root.Add(_dissolveBtn);

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
                _dissolveBtn?.SetEnabled(false);
                return;
            }

            if (_targetLabel != null)
                _targetLabel.text = $"対象: {info.ObjectCount} オブジェクト / {info.TargetCount} 頂点"
                                  + (info.SkippedCount > 0 ? $"　（除外 {info.SkippedCount}）" : "");

            if (_statusLabel != null)
                _statusLabel.text = $"{info.FaceTotal} 面を統合し、合計 {info.RingTotal} 頂点ぶんの面を作ります";

            _dissolveBtn?.SetEnabled(true);
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
