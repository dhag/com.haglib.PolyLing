// PolyLingAssetEditorLayout.cs
// PolyLingAssetEditorWindow 用の3ペインレイアウト構築。
// 左・中央・右ペインに分割し、中央は通常非表示。
// 左右にダミーボタンを配置。
//
// Editor/Poly_Ling_Player/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Editor.Player
{
    public class PolyLingAssetEditorLayout
    {
        // ================================================================
        // 公開要素
        // ================================================================

        /// <summary>左ペイン：ダミーボタン A。</summary>
        public Button LeftDummyBtnA { get; private set; }

        /// <summary>左ペイン：ダミーボタン B。</summary>
        public Button LeftDummyBtnB { get; private set; }

        /// <summary>右ペイン：ダミーボタン A。</summary>
        public Button RightDummyBtnA { get; private set; }

        /// <summary>右ペイン：ダミーボタン B。</summary>
        public Button RightDummyBtnB { get; private set; }

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.width  = new StyleLength(new Length(100, LengthUnit.Percent));
            root.style.height = new StyleLength(new Length(100, LengthUnit.Percent));

            // 左 | (中央+右) の分割
            var splitLCR = new TwoPaneSplitView(0, 200f, TwoPaneSplitViewOrientation.Horizontal);
            splitLCR.style.flexGrow = 1;
            root.Add(splitLCR);

            splitLCR.Add(BuildLeftPane());

            // 中央 | 右 の分割
            var splitCR = new TwoPaneSplitView(1, 220f, TwoPaneSplitViewOrientation.Horizontal);
            splitCR.style.flexGrow = 1;
            splitLCR.Add(splitCR);

            splitCR.Add(BuildCenterPane());
            splitCR.Add(BuildRightPane());
        }

        // ================================================================
        // 各ペイン構築
        // ================================================================

        private VisualElement BuildLeftPane()
        {
            var pane = new VisualElement();
            pane.style.minWidth = 120f;
            pane.style.paddingTop    = 4f;
            pane.style.paddingLeft   = 4f;
            pane.style.paddingRight  = 4f;
            pane.style.paddingBottom = 4f;
            pane.style.flexDirection = FlexDirection.Column;

            LeftDummyBtnA = new Button { text = "[Left] Dummy A" };
            LeftDummyBtnB = new Button { text = "[Left] Dummy B" };
            pane.Add(LeftDummyBtnA);
            pane.Add(LeftDummyBtnB);

            return pane;
        }

        private VisualElement BuildCenterPane()
        {
            var pane = new VisualElement();
            pane.style.flexGrow = 1;

            var label = new Label("(Center — 通常未使用)");
            label.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = 8f;
            pane.Add(label);

            return pane;
        }

        private VisualElement BuildRightPane()
        {
            var pane = new VisualElement();
            pane.style.minWidth = 180f;
            pane.style.paddingTop    = 4f;
            pane.style.paddingLeft   = 4f;
            pane.style.paddingRight  = 4f;
            pane.style.paddingBottom = 4f;
            pane.style.flexDirection = FlexDirection.Column;

            RightDummyBtnA = new Button { text = "[Right] Dummy A" };
            RightDummyBtnB = new Button { text = "[Right] Dummy B" };
            pane.Add(RightDummyBtnA);
            pane.Add(RightDummyBtnB);

            return pane;
        }
    }
}
