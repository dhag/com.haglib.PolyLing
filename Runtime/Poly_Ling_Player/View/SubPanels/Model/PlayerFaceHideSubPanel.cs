// PlayerFaceHideSubPanel.cs
// 面の表示・非表示サブパネル。実体は Face.Flags の FaceFlags.Hidden。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 非表示は編集補助であり、面データは残る（プロジェクト保存にもエクスポートにも出る）。
// メッシュ丸ごとの非表示はオブジェクトリスト側の可視性で行う。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerFaceHideSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        private Label  _warningLabel;
        private Label  _meshNameLabel;
        private Label  _countLabel;
        private Label  _statusLabel;
        private Button _btnHideSelected, _btnHideUnselected;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private MeshContext ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMeshContext;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("面の表示・非表示"));

            var help = new HelpBox(
                "編集の邪魔になる面を一時的に隠す。面データは消えないので保存・書き出しには残る。"
                + "隠した面は選択・ヒットテストの対象から外れる。"
                + "メッシュ丸ごとの非表示はオブジェクトリスト側で行うこと。",
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

            var rowHide = MkRow();
            _btnHideSelected = MkBtn("選択面を隠す",
                () => Send(SetFaceHiddenCommand.Mode.HideSelected),
                "選択している面を隠す。面を選択していないときは何もしない。");
            _btnHideUnselected = MkBtn("選択面以外を隠す",
                () => Send(SetFaceHiddenCommand.Mode.HideUnselected),
                "選択していない面を隠す。面を選択していないときは何もしない。");
            rowHide.Add(_btnHideSelected);
            rowHide.Add(_btnHideUnselected);
            root.Add(rowHide);

            var rowShow = MkRow();
            rowShow.Add(MkBtn("すべて表示",
                () => Send(SetFaceHiddenCommand.Mode.ShowAll),
                "隠した面をすべて元に戻す。"));
            rowShow.Add(MkBtn("表示を反転",
                () => Send(SetFaceHiddenCommand.Mode.InvertHidden),
                "表示中の面を隠し、隠した面を表示に戻す。"));
            root.Add(rowShow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);

            UpdateButtonStates();
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var mc = ActiveMeshContext;
            if (mc?.MeshObject == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text         = "";
                _countLabel.text            = "";
                UpdateButtonStates();
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _meshNameLabel.text = mc.Name ?? "(no name)";

            var mo = mc.MeshObject;
            int total = 0, hidden = 0;
            foreach (var face in mo.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                total++;
                if (face.IsHidden) hidden++;
            }

            int selFaces = mc.Selection?.Faces.Count ?? 0;
            _countLabel.text = $"面 {total}   非表示 {hidden}   選択面 {selFaces}";

            UpdateButtonStates();
        }

        // ================================================================
        // 送信
        // ================================================================

        private void Send(SetFaceHiddenCommand.Mode mode)
        {
            var mc = ActiveMeshContext;
            if (mc?.MeshObject == null) { SetStatus("メッシュが選択されていません"); return; }

            bool needsFaceSelection =
                mode == SetFaceHiddenCommand.Mode.HideSelected ||
                mode == SetFaceHiddenCommand.Mode.HideUnselected;

            if (needsFaceSelection && (mc.Selection?.Faces.Count ?? 0) == 0)
            {
                SetStatus("面を選択してください");
                return;
            }

            SendCommand?.Invoke(new SetFaceHiddenCommand(ModelIndex, mode));
            Refresh();
            SetStatus($"実行: {mode}");
        }

        private void UpdateButtonStates()
        {
            bool hasFaceSel = (ActiveMeshContext?.Selection?.Faces.Count ?? 0) > 0;
            _btnHideSelected?.SetEnabled(hasFaceSel);
            _btnHideUnselected?.SetEnabled(hasFaceSel);
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MkRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button MkBtn(string text, Action onClick, string tooltip)
        {
            var b = new Button(onClick) { text = text, tooltip = tooltip };
            b.style.height      = 22;
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            return b;
        }

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
