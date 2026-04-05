// PlayerPivotSubPanel.cs
// ピボットオフセットツール用サブパネル（Player ビルド用）。
// エディタ版 PivotOffsetTool.DrawSettingsUI() と同等の内容を UIToolkit で実装する。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// ピボットオフセットツールのサブパネル。
    /// タイトルとヘルプテキストを表示する。
    /// エディタ版 PivotOffsetTool.DrawSettingsUI() と同等の内容。
    /// </summary>
    public class PlayerPivotSubPanel
    {
        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingTop   = 4;
            root.style.paddingLeft  = 4;
            root.style.paddingRight = 4;
            parent.Add(root);

            var title = new Label("Pivot Offset Tool");
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;

            root.Add(title);

            var help = new HelpBox(
                "ハンドルをドラッグすると Pivot が移動します。\n" +
                "（実際には全頂点が逆方向に移動）\n\n" +
                "・軸ハンドル: その軸方向のみ\n" +
                "・中央: 自由移動",
                HelpBoxMessageType.Info);
            help.style.color = new StyleColor(Color.white);
            help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            root.Add(help);
        }
    }
}
