// PlayerFaceMergeSubPanel.cs
// FaceMergeTool（面結合（辺指定））の Player 版サブパネル（UIToolkit）。
// 旧「面結合（頂点削除）」は「頂点を削除する」チェックボックス（既定 ON）へ統合した。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerFaceMergeSubPanel
    {
        public Func<FaceMergeToolHandler> GetH;
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

        // UI 自動操作の ID は "faceMerge.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("stats.target", Safety = UiSafety.ReadOnly, Description = "対象のオブジェクト数・辺数と除外数")]
        private Label         _targetLabel;
        [UiControl("stats.status", Safety = UiSafety.ReadOnly, Description = "消える面・頂点の数、または実行できない理由")]
        private Label         _statusLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "選択辺を挟む 2 面を 1 面に結合する")]
        private Button        _mergeBtn;
        [UiControl("deleteVertices", Description = "共有頂点 2 つを、ほかの面が使っていても新しい面から外す")]
        private Toggle        _deleteVerticesToggle;

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

            _root.Add(Header("Face Merge (Edge) / 面結合（辺指定）"));
            _root.Add(new HelpBox(
                "選択した辺を挟む2枚の面を1枚に結合します。\n" +
                "辺に接する面が2枚でない場合、2枚が2辺以上を共有している場合は結合しません。\n" +
                "「頂点を削除する」ON：共有頂点2つを、ほかの面が使っていても新しい面から外します。" +
                "外した頂点は、どの面からも使われなくなったときだけ消えます。\n" +
                "「頂点を削除する」OFF：共有頂点のうち、ほかの面が使っていないものだけを外して消します" +
                "（OFF でも、どの面からも使われなくなる頂点は消えます）。\n" +
                "どちらも、外すと面にならない場合は外しません（三角形同士 → 四角形）。\n" +
                "複数オブジェクト・複数辺に対応。同じ面に関わる辺どうしは干渉するため除外します。",
                HelpBoxMessageType.Info));

            _deleteVerticesToggle = new Toggle("頂点を削除する")
            {
                value = GetH?.Invoke()?.DeleteVertices ?? true,
            };
            _deleteVerticesToggle.style.marginTop = 4;
            _deleteVerticesToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.DeleteVertices = e.newValue;
                Refresh();
            });
            _root.Add(_deleteVerticesToggle);

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            _mergeBtn = new Button(() =>
            {
                bool deleteVertices = GetH?.Invoke()?.DeleteVertices ?? true;
                SendCommand?.Invoke(new FaceMergeCommand(ModelIndex, SelectedMasterIndices(), deleteVertices));
                Refresh();
            })
            { text = "面結合 実行" };
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

            _deleteVerticesToggle?.SetValueWithoutNotify(h.DeleteVertices);
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
