// MaterialListClient.cs
// マテリアルリスト表示クライアント。現行メインパネルの PlayerMaterialListSubPanel を
// そのまま再利用する。表示対象はサーバの現在モデル。空 GameObject にアタッチして使う。

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Player;

namespace Poly_Ling.ListClient
{
    public sealed class MaterialListClient : ListClientBase
    {
        private PlayerMaterialListSubPanel _panel;

        protected override void BuildPanel(VisualElement host, PanelContext ctx)
        {
            _panel = new PlayerMaterialListSubPanel();
            _panel.GetModel = () =>
            {
                var p = Project;
                if (p == null || p.ModelCount == 0) return null;
                int i = Mathf.Clamp(p.CurrentModelIndex, 0, p.ModelCount - 1);
                return p.Models[i];
            };
            _panel.GetToolContext = () => null;
            _panel.OnRepaint = () => { };
            _panel.SetCommandContext(ctx, () =>
            {
                var p = Project;
                return p == null ? 0 : Mathf.Clamp(p.CurrentModelIndex, 0, Mathf.Max(0, p.ModelCount - 1));
            });
            _panel.Build(host);
        }

        protected override void OnViewPushed()
        {
            _panel?.SyncEditingSlotToCurrent();
            _panel?.Refresh();
        }
    }
}
