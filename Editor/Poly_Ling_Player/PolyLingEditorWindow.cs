// PolyLingEditorWindow.cs
// プレイヤービューを EditorWindow として開く。
// ロジック本体は PolyLingPlayerViewerCore に委譲する。
//
// Editor/Poly_Ling_Player/ に配置

using UnityEditor;
using UnityEngine.UIElements;
using Poly_Ling.Player;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Editor.Player
{
    public class PolyLingEditorWindow : EditorWindow
    {
        // ================================================================
        // 静的インスタンス（PolyLingAssetEditorWindow からのデータ参照用）
        // ================================================================

        public static PolyLingEditorWindow Instance { get; private set; }

        // ================================================================
        // Core
        // ================================================================

        private PolyLingPlayerViewerCore _core;

        /// <summary>他ウィンドウからデータ参照用に公開。</summary>
        public PolyLingPlayerViewerCore Core => _core;

        [MenuItem("PolyLing/PolyLingEditorWindow")]
        public static void Open()
        {
            GetWindow<PolyLingEditorWindow>("PolyLing Player");
        }

        private void OnEnable()
        {
            Instance = this;

            // ここで PolyLingPlayerBridge を登録すると、AssetDatabase 系が空実装の
            // ブリッジで上書きされ、Hierarchy Export のアセット化が無言で失敗する。
            // Editor 実装（PolyLingEditorBridgeImpl）をそのまま使う。

            EditorApplication.update += OnEditorUpdate;
        }

        private void CreateGUI()
        {
            _core = new PolyLingPlayerViewerCore();
            _core.Initialize(
                rootVisualElement,
                null,
                PolyLingPlayerViewerCore.RemoteConfig.Default);

            PolyLingAssetEditorWindow.Open();
        }

        private void OnEditorUpdate()
        {
            //_core?.Tick();/// <summary>この関数は利用してはならない。厳守せよ</summary>
            //_core?.LateTick();/// <summary>この関数は利用してはならない。厳守せよ</summary>

            // ファイルダイアログ表示中は再描画しない。
            // ネイティブのモーダル中もこの update は回り続けるため、ここで Repaint すると
            // UIToolkit が保留イベントを処理し直し、ダイアログを開かせたクリックが
            // 再配送されて同じダイアログが二重に開く。
            if (FileDialogGuard.IsOpen) return;

            Repaint();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            EditorApplication.update -= OnEditorUpdate;
            _core?.Dispose();
            _core = null;
        }
    }
}
