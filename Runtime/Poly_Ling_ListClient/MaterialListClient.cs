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

        protected override string ClientTypeId => "materialList";

        protected override void BuildPanel(VisualElement host, PanelContext ctx)
        {
            _panel = new PlayerMaterialListSubPanel();
            System.Func<int> modelIndex = () =>
            {
                var p = Project;
                return p == null ? 0 : Mathf.Clamp(p.CurrentModelIndex, 0, Mathf.Max(0, p.ModelCount - 1));
            };
            // 読み取りはクライアントが持つプロジェクトの写しを窓口（IProjectView）で包んで読む。
            _panel.GetView = () => Project != null ? new PlayerProjectView(Project) : null;
            // 色・Metallic・Smoothness のプレビューと確定はツールの窓口のコマンドとしてホストへ送る（M-2）。
            _panel.Surface = new ClientToolSurface(cmd => ctx?.SendCommand(cmd), modelIndex);
            _panel.OnRepaint = () => { };
            _panel.SetCommandContext(ctx, modelIndex);
            _panel.Build(host);
        }

        protected override void OnViewPushed()
        {
            _panel?.SyncEditingSlotToCurrent();
            _panel?.Refresh();
        }
    }
}
