// PolyLingPlayerViewerCore.CurrentView.cs
// Player ビューアのコア：カレントビュー（_activePanel / _activeViewport）の切り替えと、
// ViewportKind（Poly_Ling.Data）と実際のビューポートとの対応。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【切り替えの入口は 1 つ】
//   ポインタのホバー（VertexInteraction.cs）も setCurrentView も SwitchActivePanel を通す。
//   旧パネルに残る表示（矩形選択・面ホバー・ギズモ・ブラシ円）を消し、
//   頂点インタラクタを新しいパネルへつなぎ替える。
//
// 【ビューを持つコマンド】
//   knifeSimpleCut / edgeExtrude はコマンドの ViewportKind から ToolContext を作る
//   （GetToolContextForView）。画面から確定したときは ActiveViewKind を書き込む。

using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>
        /// カレントビューを切り替える。同じパネルなら何もしない。
        /// </summary>
        private void SwitchActivePanel(PlayerViewportPanel panel, PlayerViewport vp)
        {
            if (panel == null || vp == null) return;
            if (_activePanel == panel) return;

            if (_activePanel != null)
            {
                _activePanel.HideBoxSelect();
                _activePanel.HideFaceHover();
                _activePanel.HideGizmo();
                // ブラシ円は常時表示なので、旧ビューポートに残さない。
                _activePanel.HideBrushCircle();
                _vertexInteractor?.Disconnect(_activePanel);
            }
            _activePanel    = panel;
            _activeViewport = vp;
            _vertexInteractor?.Connect(_activePanel);
        }

        /// <summary>種別に対応するビューポート。</summary>
        private PlayerViewport ViewportOf(ViewportKind kind)
        {
            if (_viewportManager == null) return null;
            switch (kind)
            {
                case ViewportKind.Top:   return _viewportManager.TopViewport;
                case ViewportKind.Front: return _viewportManager.FrontViewport;
                case ViewportKind.Side:  return _viewportManager.SideViewport;
                default:                 return _viewportManager.PerspectiveViewport;
            }
        }

        /// <summary>種別に対応するビューポートパネル。</summary>
        private PlayerViewportPanel PanelOf(ViewportKind kind)
        {
            switch (kind)
            {
                case ViewportKind.Top:   return _layoutRoot?.TopPanel;
                case ViewportKind.Front: return _layoutRoot?.FrontPanel;
                case ViewportKind.Side:  return _layoutRoot?.SidePanel;
                default:                 return _layoutRoot?.PerspectivePanel;
            }
        }

        /// <summary>今のカレントビューの種別。</summary>
        private ViewportKind ActiveViewKind()
        {
            var vp = _activeViewport;
            if (vp != null && _viewportManager != null)
            {
                if (vp == _viewportManager.TopViewport)   return ViewportKind.Top;
                if (vp == _viewportManager.FrontViewport) return ViewportKind.Front;
                if (vp == _viewportManager.SideViewport)  return ViewportKind.Side;
            }
            return ViewportKind.Perspective;
        }

        /// <summary>指定のビューの ToolContext。カレントビューは変えない。</summary>
        private ToolContext GetToolContextForView(ViewportKind kind)
            => _viewportManager?.GetCurrentToolContext(ViewportOf(kind));

        /// <summary>
        /// 編集対象メッシュの頂点の、指定のビューでのクリップ空間 w。
        /// 透視投影で切断点の比率を 3D 空間へ補正するのに使う。
        /// </summary>
        private float? GetActiveMeshVertexClipW(ViewportKind kind, int vertexIndex)
        {
            var m  = ActiveProject?.CurrentModel;
            var mc = m?.ActiveMeshContext;
            if (m == null || mc == null || _viewportManager == null) return null;
            return _viewportManager.TryGetVertexClipW(m, mc, vertexIndex, ViewportOf(kind), out var w)
                ? (float?)w : null;
        }

        /// <summary>setCurrentView の受け口。失敗理由を返す（成功なら null）。</summary>
        private string SetCurrentView(ViewportKind kind, CommandDataBuilder data)
        {
            var vp    = ViewportOf(kind);
            var panel = PanelOf(kind);
            if (vp == null || panel == null) return $"ビュー {kind} がありません";

            var previous = ActiveViewKind();
            SwitchActivePanel(panel, vp);
            _activePanel?.MarkDirtyRepaint();

            data.Text("view", ActiveViewKind().ToString())
                .Text("previous", previous.ToString());
            return null;
        }
    }
}
