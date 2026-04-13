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

        [MenuItem("Tools/PolyLing/PolyLingEditorWindow")]
        public static void Open()
        {
            GetWindow<PolyLingEditorWindow>("PolyLing Player");
        }

        private void OnEnable()
        {
            Instance = this;
            PLEditorBridge.Register(new PolyLingPlayerBridge());
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
            _core?.Tick();
            _core?.LateTick();
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
