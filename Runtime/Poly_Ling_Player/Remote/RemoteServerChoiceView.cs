// RemoteServerChoiceView.cs
// サーバが複数あるときの接続先選択 UI（UI Toolkit）。
// RemoteServerConnector.OnChoicesChanged を受けてボタンを並べ、押された行を Choose に渡す。
// 選択肢が無いときは非表示。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    public sealed class RemoteServerChoiceView : VisualElement
    {
        private readonly RemoteServerConnector _connector;
        private readonly VisualElement         _list;

        public RemoteServerChoiceView(RemoteServerConnector connector)
        {
            _connector = connector;

            style.display      = DisplayStyle.None;
            style.marginTop    = 2;
            style.marginBottom = 4;

            var title = new Label("接続先を選択");
            title.style.fontSize     = 10;
            title.style.marginBottom = 2;
            Add(title);

            _list = new VisualElement();
            Add(_list);

            var refresh = new Button(() => _connector.Begin()) { text = "一覧を取り直す" };
            refresh.style.marginTop = 2;
            Add(refresh);

            _connector.OnChoicesChanged += SetChoices;
            SetChoices(_connector.Choices);
        }

        /// <summary>購読を外す。</summary>
        public void Detach()
        {
            _connector.OnChoicesChanged -= SetChoices;
        }

        private void SetChoices(List<RemoteServerInfo> choices)
        {
            _list.Clear();

            if (choices == null || choices.Count == 0)
            {
                style.display = DisplayStyle.None;
                return;
            }

            foreach (var info in choices)
            {
                var captured = info;
                var btn = new Button(() => _connector.Choose(captured))
                {
                    text = captured.IsMaster ? captured.Label + "  [master]" : captured.Label,
                };
                btn.style.unityTextAlign = TextAnchor.MiddleLeft;
                btn.style.whiteSpace     = WhiteSpace.Normal;
                _list.Add(btn);
            }

            style.display = DisplayStyle.Flex;
        }
    }
}
