// Editor/EditorControl/PolyLingEditorControlInstaller.cs
// ============================================================
// PolyLingEditorControlServer の起動・停止を Editor のライフサイクルに結び付ける
// ============================================================
//
// 【なぜ [InitializeOnLoad] なのか】
//   Play Mode の開始・停止要求は PolyLing ウィンドウが開いていなくても
//   受け付ける必要がある。EditorWindow.OnEnable に載せるとウィンドウを
//   開くまで受信できない。
//   既存の PolyLingEditorBridgeImpl.cs:33-41 と同じ配置方針。
//
// 【なぜ AssemblyReloadEvents が要るのか】
//   Play Mode 開始時はドメインリロードが走り、管理オブジェクトが破棄される。
//   リスナを掴んだまま破棄されるとパイプが解放されず、リロード後の再起動で
//   同名パイプの作成に失敗する。beforeAssemblyReload で明示的に停止し、
//   リロード後に走る静的コンストラクタで起動し直す。
//
// ============================================================

using UnityEditor;

namespace Poly_Ling.EditorControl
{
    /// <summary>エディタ起動時・ドメインリロード後に制御サーバを起動する。</summary>
    [InitializeOnLoad]
    public static class PolyLingEditorControlInstaller
    {
        static PolyLingEditorControlInstaller()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            EditorApplication.quitting -= OnQuitting;
            EditorApplication.quitting += OnQuitting;

            // 静的コンストラクタはメインスレッドで走る。
            // Start はここで SynchronizationContext をキャプチャする。
            PolyLingEditorControlServer.Start();
        }

        private static void OnBeforeAssemblyReload()
        {
            PolyLingEditorControlServer.Stop();
        }

        private static void OnQuitting()
        {
            PolyLingEditorControlServer.Stop();
        }
    }
}
