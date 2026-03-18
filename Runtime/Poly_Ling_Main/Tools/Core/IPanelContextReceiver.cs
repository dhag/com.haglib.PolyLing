// Assets/Editor/Poly_Ling_Main/Tools/Core/IPanelContextReceiver.cs
// PanelContextを受け取れるEditorWindowの最小インターフェース
// メインパネル再起動時の再接続に使用
using Poly_Ling.Context;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// PanelContextを受け取れるEditorWindowが実装するインターフェース。
    /// メインパネル再起動時に開いているパネルへPanelContextを再配布するために使用。
    /// </summary>
    public interface IPanelContextReceiver
    {
        void SetContext(PanelContext ctx);
    }
}
