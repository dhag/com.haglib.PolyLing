// PlayerEdgeTriangleSubPanel.cs
// 辺から三角形（EdgeTriangleToolHandler）のサブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerEdgeTriangleSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface Surface;
        private const string Tool = "edgeTriangle";

        // UI 自動操作の ID は "edgeTriangle.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("makeQuad", Safety = UiSafety.SafeWrite, Description = "辺が属する面がちょうど 1 枚の三角形なら、三角形を足さずに四角形にする")]
        private Toggle _makeQuadToggle;
        [UiControl("result", Safety = UiSafety.ReadOnly, Description = "直前の実行結果")]
        private Label  _resultLabel;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft = _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            var header = new Label("辺から三角形");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 4; header.style.marginBottom = 3;
            _root.Add(header);

            _root.Add(new HelpBox(
                "辺の上を押してドラッグすると新しい頂点を引き出し、離すと辺と新しい頂点で三角形を作ります。\n" +
                "頂点は押した位置から出て、カメラと平行な面の上を動きます。ほとんど動かさずに離すと何もしません。\n" +
                "辺がちょうど 1 枚の面に属するときは、その面と表裏をそろえます。" +
                "そうでないとき（線分だけ・2 枚以上）は、今の視点から表が見える向きにします。",
                HelpBoxMessageType.Info));

            _makeQuadToggle = new Toggle("三角形なら四角形にする") { value = true };
            _makeQuadToggle.tooltip = "辺が属する面がちょうど 1 枚の三角形のとき、三角形を足さずに、その三角形へ新しい頂点を差し込んで四角形にする";
            _makeQuadToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "makeQuad", e.newValue));
            _makeQuadToggle.style.marginTop = 4;
            _root.Add(_makeQuadToggle);

            _resultLabel = new Label();
            _resultLabel.style.fontSize = 10;
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.marginTop = 4;
            _root.Add(_resultLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        public void Refresh()
        {
            if (Surface == null) return;
            _makeQuadToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "makeQuad", true));
            if (_resultLabel != null) _resultLabel.text = Surface.Get(Tool, "lastResult", "");
        }
    }
}
