// IPlayerGizmoProvider.cs
// ビューポートのギズモ表示データを供給するインターフェース。
// PolyLingPlayerViewerCore.UpdateGizmoOverlay がモードごとに実装を選び、
// 得られた GizmoData をそのまま PlayerViewportPanel へ渡す。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// ギズモ形状の決定を各 ToolHandler 側に持たせるためのインターフェース。
    /// <para>
    /// 実装は自身のギズモ種別（矢印 / ダイヤ / キューブ / リング）と
    /// ホバー軸・ピボット位置を組み立てて返す。表示可否の判定も実装側で行い、
    /// 表示しない場合は false を返す（呼び出し側は HideGizmo する）。
    /// </para>
    /// </summary>
    public interface IPlayerGizmoProvider
    {
        /// <summary>
        /// 現在の状態からギズモ表示データを組み立てる。
        /// 表示すべきギズモが無いときは false を返す。
        /// </summary>
        bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data);
    }
}
