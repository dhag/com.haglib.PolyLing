// PolyLingPlayerViewerCore.ActiveModes.cs
// Viewer 側が持つ一時的な状態を、queryRevisions の activeModes へ渡す。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【何のためか】
//   モーフやブレンドのプレビュー中は、プレビューを終えるまで形状を変える操作を避けるべきである。
//   人工知能がその状態にあることを知る口が無かったので、ここで集める。
//   手本の記録中・実行中はディスパッチャ自身が持つので、ここでは扱わない（PlayerCommandDispatcher.Scene.cs）。
//
// 【足すとき】
//   新しいプレビューや一時的な状態を作ったら、ここへ 1 行足し、
//   QueryRevisionsCommand の activeModes の説明にも名前を足すこと。

using System.Collections.Generic;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private IReadOnlyList<string> CollectActiveModes()
        {
            var modes = new List<string>();
            if (_morphExpressionHandler != null && _morphExpressionHandler.IsPreviewing) modes.Add("morphPreview");
            if (_blendToolHandler       != null && _blendToolHandler.IsPreviewing)       modes.Add("blendPreview");
            return modes;
        }
    }
}
