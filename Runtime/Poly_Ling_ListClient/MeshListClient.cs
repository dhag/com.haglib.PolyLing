// MeshListClient.cs
// オブジェクトリスト表示クライアント。現行メインパネルの MeshListSubPanel(MeshList V2)を
// そのまま再利用する(スキンドメッシュ詳細トグル・Mesh/Bone/Morph/剛体タブ・TreeView)。
// 空 GameObject にアタッチして使う。描画メッシュ本体は取得しない。

using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.MeshListV2;

namespace Poly_Ling.ListClient
{
    public sealed class MeshListClient : ListClientBase
    {
        private MeshListSubPanel _panel;

        protected override void BuildPanel(VisualElement host, PanelContext ctx)
        {
            _panel = new MeshListSubPanel();
            _panel.Build(host);
            _panel.SetContext(ctx);
        }

        protected override void OnTeardown()
        {
            _panel?.Detach();
        }
    }
}
