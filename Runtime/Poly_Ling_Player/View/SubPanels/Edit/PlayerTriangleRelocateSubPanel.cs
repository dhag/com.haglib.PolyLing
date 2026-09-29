// PlayerTriangleRelocateSubPanel.cs
// TriangleRelocateTool の Player 版サブパネル（UIToolkit）。PlayerQuad4To1SubPanel と同じ構成。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerTriangleRelocateSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface Surface;
        private const string Tool = "triangleRelocate";
        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand>             SendCommand;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private int[] SelectedMasterIndices()
            => GetView?.Invoke()?.CurrentModel?.SelectedDrawableIndices ?? Array.Empty<int>();

        // UI 自動操作の ID は "triangleRelocate.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("stats.target", Safety = UiSafety.ReadOnly, Description = "対象のオブジェクト数・頂点数と除外数")]
        private Label _targetLabel;
        [UiControl("stats.status", Safety = UiSafety.ReadOnly, Description = "実行できない理由")]
        private Label _statusLabel;
        [UiControl("t1", Safety = UiSafety.SafeWrite, Description = "新しい頂点 p の位置（対角線 b→a の比率。0 < t1 < t2 < 1）")]
        private FloatField _t1Field;
        [UiControl("t2", Safety = UiSafety.SafeWrite, Description = "新しい頂点 q の位置（対角線 b→a の比率。0 < t1 < t2 < 1）")]
        private FloatField _t2Field;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "選択頂点の周りの三角形を向かい側へ移し替える")]
        private Button _runBtn;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft =
            _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("三角形の移し替え"));
            _root.Add(new HelpBox(
                "三角形 1 枚と四角形 3 枚に囲まれた頂点を選んで実行します。\n" +
                "三角形の向かいの四角形を対角線で割り、頂点側を捨て、残りを対角線上の 2 点で三角形 3 枚に割ります。\n" +
                "両隣の四角形はその 2 点へつなぎ変え、三角形は四角形に張り替え、選んだ頂点は消えます。\n" +
                "結果として三角形が向かい側の隅へ移ります。\n" +
                "複数オブジェクト・複数頂点に対応。同じ面を共有する頂点どうしは干渉するため除外します。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);
            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            _t1Field = new FloatField("位置 t1") { value = 1f / 3f };
            _t1Field.tooltip = "新しい頂点 p の位置。対角線の端 b から a への比率";
            _root.Add(_t1Field);
            _t2Field = new FloatField("位置 t2") { value = 2f / 3f };
            _t2Field.tooltip = "新しい頂点 q の位置。対角線の端 b から a への比率";
            _root.Add(_t2Field);

            _runBtn = new Button(() =>
            {
                float t1 = _t1Field.value, t2 = _t2Field.value;
                if (!(t1 > 0f && t1 < t2 && t2 < 1f))
                {
                    if (_statusLabel != null) _statusLabel.text = "位置は 0 < t1 < t2 < 1 にしてください";
                    return;
                }
                SendCommand?.Invoke(new TriangleRelocateCommand(ModelIndex, SelectedMasterIndices(), t1, t2));
                Refresh();
            })
            { text = "三角形の移し替え 実行" };
            _runBtn.style.height    = 30;
            _runBtn.style.marginTop = 6;
            _root.Add(_runBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        public void Refresh()
        {
            if (Surface == null) return;

            var info = Surface.GetGroup(Tool, "inspect");
            int skipped = info.Item("skippedCount", 0);

            if (!info.Item("canExecute", false))
            {
                if (_targetLabel != null)
                    _targetLabel.text = $"選択中: {Surface.GetInt(Tool, "selectedVertexCount")} 頂点  /  除外: {skipped} 頂点";
                if (_statusLabel != null) _statusLabel.text = info.Item("reason", "");
                _runBtn?.SetEnabled(false);
                return;
            }

            if (_targetLabel != null)
                _targetLabel.text = $"対象: {info.Item("objectCount", 0)} オブジェクト / {info.Item("targetCount", 0)} 頂点"
                                  + (skipped > 0 ? $"　（除外 {skipped}）" : "");
            if (_statusLabel != null) _statusLabel.text = "";
            _runBtn?.SetEnabled(true);
        }

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
