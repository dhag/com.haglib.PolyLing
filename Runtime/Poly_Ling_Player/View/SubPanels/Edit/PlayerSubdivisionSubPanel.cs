// PlayerSubdivisionSubPanel.cs
// サブディビジョン（細分化曲面）のパネル。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 選択中の描画オブジェクトを親（ケージ）として滑らかな子を作る。
// 子を選んでいるときは、その親についての操作として扱う。
// 3D 操作はそのまま使えるので、パネルを開いたまま親の頂点を編集できる。

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerSubdivisionSubPanel
    {
        public Func<ModelContext> GetModel;
        public Func<int>          GetModelIndex;
        /// <summary>コマンドを流し、結果を返す口（失敗理由を状態欄へ出すため）。</summary>
        public Func<PanelCommand, CommandResult> SendCommand;

        // UI 自動操作の ID は "subdivision.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("target", Safety = UiSafety.ReadOnly, Description = "対象（親）と、子があればその名前")]
        private Label     _targetLabel;
        [UiControl("level", Description = "細分化の回数（1〜4）")]
        private SliderInt _levelSlider;
        [UiControl("create", Safety = UiSafety.SafeWrite, Description = "子を作る。既に子があれば回数を反映して作り直す")]
        private Button    _createButton;
        [UiControl("deleteChild", Description = "解除のとき子を消す。外すと子を通常のメッシュとして残す")]
        private Toggle    _deleteChildToggle;
        [UiControl("release", Safety = UiSafety.Destructive, Description = "サブディビジョンを解除する")]
        private Button    _releaseButton;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "実行できない理由、または実行結果")]
        private Label     _statusLabel;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("サブディビジョン"));

            var help = new Label("選択中のメッシュを親（ケージ）にして、滑らかな子を作ります。" +
                                 "親の頂点を動かす・面を足す・親を移動すると、子が追随します。");
            help.style.whiteSpace   = WhiteSpace.Normal;
            help.style.fontSize     = 10;
            help.style.marginBottom = 6;
            root.Add(help);

            _targetLabel = new Label("対象: なし");
            _targetLabel.style.whiteSpace   = WhiteSpace.Normal;
            _targetLabel.style.marginBottom = 4;
            root.Add(_targetLabel);

            _levelSlider = new SliderInt("回数", SubdivisionOps.LevelMin, SubdivisionOps.LevelMax) { value = 2, showInputField = true };
            _levelSlider.style.marginBottom = 6;
            root.Add(_levelSlider);

            _createButton = new Button(OnCreate) { text = "作成 / 回数を反映" };
            _createButton.style.height       = 28;
            _createButton.style.marginBottom = 8;
            root.Add(_createButton);

            _deleteChildToggle = new Toggle("解除のとき子を消す") { value = true };
            _deleteChildToggle.style.marginBottom = 4;
            root.Add(_deleteChildToggle);

            _releaseButton = new Button(OnRelease) { text = "解除" };
            _releaseButton.style.height       = 24;
            _releaseButton.style.marginBottom = 4;
            root.Add(_releaseButton);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_statusLabel);
        }

        public void Refresh()
        {
            if (_targetLabel == null) return;

            var model = GetModel?.Invoke();
            var mc = ResolveSelected(model, out _);
            if (mc == null)
            {
                _targetLabel.text = "対象: なし（メッシュを 1 つ選んでください）";
                _createButton.SetEnabled(false);
                _releaseButton.SetEnabled(false);
                return;
            }

            var link = SubdivisionSync.FindByCage(model, mc) ?? SubdivisionSync.FindByChild(model, mc);
            if (link != null)
            {
                _targetLabel.text = $"親: {link.Cage.Name}\n子: {link.Child.Name}（回数 {link.Level}）";
                _levelSlider.SetValueWithoutNotify(link.Level);
                _createButton.SetEnabled(true);
                _releaseButton.SetEnabled(true);
            }
            else
            {
                bool canCage = mc.Type == MeshType.Mesh && !mc.IsSkinned;
                _targetLabel.text = canCage
                    ? $"親: {mc.Name}（子はまだありません）"
                    : $"{mc.Name} は親にできません（通常のメッシュでスキンド化していないもの）";
                _createButton.SetEnabled(canCage);
                _releaseButton.SetEnabled(false);
            }
        }

        /// <summary>選択中の描画オブジェクト（複数なら先頭）。</summary>
        private static MeshContext ResolveSelected(ModelContext model, out int masterIndex)
        {
            masterIndex = -1;
            if (model == null) return null;
            masterIndex = model.FirstMeshIndex;
            return masterIndex >= 0 ? model.GetMeshContext(masterIndex) : null;
        }

        private void OnCreate()
        {
            var model = GetModel?.Invoke();
            var mc = ResolveSelected(model, out int idx);
            if (mc == null) { SetStatus("メッシュを 1 つ選んでください"); return; }

            // 子を選んでいるときは、その親を対象にする。
            var link = SubdivisionSync.FindByChild(model, mc);
            int cageIndex = link != null ? model.MeshContextList.IndexOf(link.Cage) : idx;
            int childIndex = link != null ? idx
                           : (SubdivisionSync.FindByCage(model, mc) is var l2 && l2 != null
                                ? model.MeshContextList.IndexOf(l2.Child) : -1);

            var r = SendCommand?.Invoke(new SubdivideMeshCommand(
                GetModelIndex?.Invoke() ?? 0, cageIndex, _levelSlider.value, childIndex));
            SetStatus(r != null && !r.Success ? r.Reason : "");
            Refresh();
        }

        private void OnRelease()
        {
            var model = GetModel?.Invoke();
            var mc = ResolveSelected(model, out int idx);
            if (mc == null) { SetStatus("メッシュを 1 つ選んでください"); return; }

            var r = SendCommand?.Invoke(new ReleaseSubdivisionCommand(
                GetModelIndex?.Invoke() ?? 0, idx, _deleteChildToggle.value));
            SetStatus(r != null && !r.Success ? r.Reason : "");
            Refresh();
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
