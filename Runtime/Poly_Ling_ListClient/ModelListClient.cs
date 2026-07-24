// ModelListClient.cs
// モデルリスト表示クライアント。現行メインパネルの ModelListSubPanel をそのまま再利用する。
// 空 GameObject にアタッチして使う。

using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Player;

namespace Poly_Ling.ListClient
{
    public sealed class ModelListClient : ListClientBase
    {
        private ModelListSubPanel _panel;

        protected override void BuildPanel(VisualElement host, PanelContext ctx)
        {
            _panel = new ModelListSubPanel();
            _panel.Build(host);
            _panel.SetContext(ctx);
        }
    }
}
