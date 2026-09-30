// PlayerDetachVerticesSubPanel.cs
// 「頂点の分離」サブパネル（UIToolkit）。選択面を切り離す・選択辺で切り開く。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 実行は DetachVerticesCommand（MCP と同じ経路）。見積もりは DetachVerticesOps.CountNewVertices。

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerDetachVerticesSubPanel
    {
        public Func<ModelContext>         GetModel;
        public Func<int>                  GetModelIndex;
        public Func<PanelCommand, string> SendCommand;

        // UI 自動操作の ID は "detachVertices.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("mode", Description = "切れ目の決め方。面を切り離す（選択面と非選択面の境界）／辺で切り開く（選択辺）")]
        private DropdownField _modeDropdown;
        [UiControl("stats", Safety = UiSafety.ReadOnly, Description = "対象のオブジェクト数と、分けると増える頂点の数")]
        private Label _statsLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "頂点の分離を実行する")]
        private Button _runBtn;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;

        private DetachMode Mode => _modeDropdown != null && _modeDropdown.index == 1 ? DetachMode.Edges : DetachMode.Faces;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingBottom = 4;
            _root.style.paddingLeft = _root.style.paddingRight = 4;
            parent.Add(_root);

            var header = new Label("頂点の分離");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 3;
            _root.Add(header);

            _root.Add(new HelpBox(
                "面：選択面を周りの面から切り離します（境界の頂点を複製）。\n" +
                "辺：選択辺に沿って切り開きます。\n" +
                "UV の作業空間の代理に使うと、反映で元の UV の継ぎ目になります。",
                HelpBoxMessageType.Info));

            _modeDropdown = new DropdownField("切れ目",
                new System.Collections.Generic.List<string> { "面を切り離す", "辺で切り開く" }, 0);
            _modeDropdown.RegisterValueChangedCallback(_ => Refresh());
            _root.Add(_modeDropdown);

            _statsLabel = new Label();
            _statsLabel.style.fontSize = 10;
            _root.Add(_statsLabel);

            _runBtn = new Button(Run) { text = "分離を実行" };
            _runBtn.style.height    = 28;
            _runBtn.style.marginTop = 6;
            _root.Add(_runBtn);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(0.9f, 0.85f, 0.4f));
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        public void Refresh()
        {
            if (_root == null) return;
            var model = GetModel?.Invoke();
            int objects = 0, added = 0;
            if (model != null)
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject == null || mc.Selection == null) continue;
                    int n = DetachVerticesOps.CountNewVertices(mc.MeshObject, mc.Selection, Mode);
                    if (n > 0) { objects++; added += n; }
                }
            }
            _statsLabel.text = added > 0
                ? $"対象 {objects} オブジェクト／増える頂点 {added}"
                : (Mode == DetachMode.Faces ? "分離できません（周りの面とつながった面を選択）"
                                            : "分離できません（面の間の辺を選択）");
            _runBtn.SetEnabled(added > 0);
        }

        private void Run()
        {
            var model = GetModel?.Invoke();
            if (model == null) return;
            var targets = model.SelectedDrawableMeshIndices.ToArray();
            string reason = SendCommand?.Invoke(new DetachVerticesCommand(GetModelIndex?.Invoke() ?? 0, targets, Mode));
            _statusLabel.text = reason ?? "分離しました";
            Refresh();
        }
    }
}
