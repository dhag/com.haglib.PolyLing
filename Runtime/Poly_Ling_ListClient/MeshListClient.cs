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

        protected override string ClientTypeId => "meshList";

        protected override void BuildPanel(VisualElement host, PanelContext ctx)
        {
            _panel = new MeshListSubPanel();
            _panel.Build(host);
            _panel.SetContext(ctx);
            // 協働編集: 担当者の取得／解放は「自分の名前」が要る。
            // register で送るものと同一の実効名を渡す（不一致だと自分の担当を解放できない）。
            _panel.LocalUserName = UserName;
        }

        protected override void OnViewPushed()
        {
            // 再接続や名前変更に追随させる
            if (_panel != null) _panel.LocalUserName = UserName;
        }

        protected override void OnTeardown()
        {
            _panel?.Detach();
        }
    }
}
