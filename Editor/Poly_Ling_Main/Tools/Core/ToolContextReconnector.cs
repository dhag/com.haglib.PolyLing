// Assets/Editor/Poly_Ling_Main/Tools/Core/ToolContextReconnector.cs
// メインパネル再起動時に、開いているパネルへToolContextを再配布する

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public static class ToolContextReconnector
    {
        /// <summary>
        /// 指定型のウィンドウが開いていればToolContextを再配布する。
        /// 開いていなければ何もしない。
        /// </summary>
        /// <returns>再接続できたらtrue</returns>
        public static bool Reconnect<T>(ToolContext ctx) where T : EditorWindow, IToolContextReceiver
        {
            var window = Resources.FindObjectsOfTypeAll<T>().FirstOrDefault();
            if (window == null) return false;

            window.SetContext(ctx);
            return true;
        }

        /// <summary>
        /// IToolContextReceiverを実装している全EditorWindowを探し、再接続する。
        /// パネル追加時にこのメソッドを変更する必要がない。
        /// </summary>
        /// <returns>再接続したウィンドウ数</returns>
        public static int ReconnectAll(ToolContext ctx)
        {
            int count = 0;
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                if (window is IToolContextReceiver receiver)
                {
                    receiver.SetContext(ctx);
                    count++;
                }
            }

            if (count > 0)
                Debug.Log($"[ToolContextReconnector] Reconnected {count} panel(s)");

            return count;
        }
    }
}
