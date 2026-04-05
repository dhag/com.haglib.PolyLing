// LiteModelListSubPanel.cs
// PolyLing Lite 用モデルリストサブパネル。
// Poly_Ling.Player.ModelListSubPanel に委譲し、Build/SetContext の同一インタフェースを提供する。
// ランタイム版（PlayerViewer）と同じパターンで利用できる。
//
// Editor/Poly_Ling_Lite/SubPanels/ に配置

using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Player;

namespace Poly_Ling.Lite
{
    public class LiteModelListSubPanel
    {
        private readonly ModelListSubPanel _inner = new ModelListSubPanel();

        public void Build(VisualElement parent)
        {
            _inner.Build(parent);
        }

        public void SetContext(PanelContext ctx)
        {
            _inner.SetContext(ctx);
        }
    }
}
