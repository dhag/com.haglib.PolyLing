// PolyLingPlayerViewerCore.Subdivision.cs
// Player ビューアのコア：サブディビジョンのパネル（生成と表示）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// コマンドの実体はディスパッチャ（PlayerCommandDispatcher.Subdivision.cs）にあり、
// ビューア側はパネルを作って開くだけ。

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private PlayerSubdivisionSubPanel _subdivisionSubPanel;

        /// <summary>パネルを作る（BuildEditToolPanels から呼ぶ）。</summary>
        private void BuildSubdivisionPanel()
        {
            _subdivisionSubPanel = new PlayerSubdivisionSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
                SendCommand   = cmd => DispatchHost(cmd),
            };
            _subdivisionSubPanel.Build(_layoutRoot.SubdivisionSection);
        }

        private void ShowSubdivisionPanel()
        {
            // 3D 操作 (InteractionMode) は維持し、右ペインだけ切り替える。
            // 親の頂点を編集しながら子の追随を見るため。
            ShowRightPanel(_layoutRoot?.SubdivisionSection, _layoutRoot?.SubdivisionBtn);
            _subdivisionSubPanel?.Refresh();
        }
    }
}
