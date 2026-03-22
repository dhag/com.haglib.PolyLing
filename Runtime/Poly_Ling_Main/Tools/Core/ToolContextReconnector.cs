// Assets/Editor/Poly_Ling_Main/Tools/Core/ToolContextReconnector.cs
// メインパネル再起動時に、開いているパネルへToolContextを再配布する

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Tools
{
    public static class ToolContextReconnector
    {
        /// <summary>
        /// IToolContextReceiverを実装している全ウィンドウを探し、再接続する。
        /// Editor環境ではEditorBridge経由でEditorWindowを探索する。
        /// </summary>
        public static int ReconnectAll(ToolContext ctx)
        {
            int count = 0;
            foreach (var receiver in PLEditorBridge.I.FindAllToolContextReceivers())
            {
                receiver.SetContext(ctx);
                count++;
            }
            if (count > 0)
                Debug.Log($"[ToolContextReconnector] Reconnected {count} panel(s)");
            return count;
        }

        /// <summary>
        /// IPanelContextReceiverを実装している全ウィンドウを探し、PanelContextを再配布する。
        /// </summary>
        public static int ReconnectAllPanelContexts(PanelContext ctx)
        {
            int count = 0;
            foreach (var receiver in PLEditorBridge.I.FindAllPanelContextReceivers())
            {
                receiver.SetContext(ctx);
                count++;
            }
            if (count > 0)
                Debug.Log($"[ToolContextReconnector] Reconnected {count} panel(s) with PanelContext");
            return count;
        }
    }
}
