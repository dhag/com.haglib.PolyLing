// PlayerDuplicateSelectionSubPanel.cs
// 選択を複製サブパネル。選択している頂点・辺・線分・面だけを別オブジェクトへ写す。
// 実体は DuplicateSelectionCommand（SelectionDuplicateOps が正典）。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 対象は選択中の描画オブジェクトすべて。選択があるものごとに 1 つずつ作る。
// 表示している件数はアクティブなメッシュのものだけ。

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerDuplicateSelectionSubPanel
    {
        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand> SendCommand;

        // UI 自動操作の ID は "duplicateSelection.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "メッシュが選択されていないときの警告（それ以外は非表示）")]
        private Label  _warningLabel;
        [UiControl("meshName", Safety = UiSafety.ReadOnly, Description = "アクティブなメッシュ名")]
        private Label  _meshNameLabel;
        [UiControl("counts", Safety = UiSafety.ReadOnly, Description = "アクティブなメッシュの選択数（頂点・辺・線分・面）")]
        private Label  _countLabel;
        [UiControl("edgesAsLines", Safety = UiSafety.SafeWrite, Description = "辺を線分にする（その辺を含む面や同じ線分を写さないとき、辺を線分として写す）")]
        private Toggle _edgesAsLinesToggle;
        [UiControl("execute", Safety = UiSafety.SafeWrite, Description = "選択を別オブジェクトへ複製する")]
        private Button _btnExecute;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "実行できない理由、または直近の実行")]
        private Label  _statusLabel;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private Poly_Ling.View.IMeshView ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMesh;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("選択を複製"));

            var help = new HelpBox(
                "選択している頂点・辺・線分・面だけを写した新しいオブジェクトを作る。元は変えない。"
                + "面と線分は使っている頂点ごと写す。選択した頂点は単独でも写す。"
                + "選択中のオブジェクトが複数あるときは、選択があるものごとに 1 つずつ作る。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize     = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _countLabel = new Label();
            _countLabel.style.fontSize     = 10;
            _countLabel.style.marginBottom = 4;
            root.Add(_countLabel);

            _edgesAsLinesToggle = new Toggle("辺を線分にする") { value = false };
            _edgesAsLinesToggle.tooltip =
                "オン: 選択した辺のうち、その辺を含む面も同じ線分も写さないものを線分（2 頂点の面）として写す。\n"
              + "オフ: 選択した辺は両端の頂点だけを写す。";
            _edgesAsLinesToggle.style.marginBottom = 4;
            root.Add(_edgesAsLinesToggle);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            _btnExecute = new Button(Execute) { text = "選択を複製", tooltip = "選択を別オブジェクトへ複製する" };
            _btnExecute.style.height   = 22;
            _btnExecute.style.flexGrow = 1;
            row.Add(_btnExecute);
            root.Add(row);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var mc = ActiveMeshContext;
            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text         = "";
                _countLabel.text            = "";
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _meshNameLabel.text = mc.Name ?? "(no name)";
            _countLabel.text =
                $"選択  頂点 {mc.SelectedVertexCount}   辺 {mc.SelectedEdgeCount}"
              + $"   線分 {mc.SelectedLineCount}   面 {mc.SelectedFaceCount}";
        }

        // ================================================================
        // 送信
        // ================================================================

        private void Execute()
        {
            if (ActiveMeshContext == null) { SetStatus("メッシュが選択されていません"); return; }

            bool asLines = _edgesAsLinesToggle?.value ?? false;
            SendCommand?.Invoke(new DuplicateSelectionCommand(ModelIndex, asLines));
            Refresh();
            SetStatus(asLines ? "実行: 選択を複製（辺を線分にする）" : "実行: 選択を複製");
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
