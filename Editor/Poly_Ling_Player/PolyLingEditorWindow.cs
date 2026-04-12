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
        private PolyLingPlayerViewerCore _core;

        [MenuItem("Tools/PolyLing/PolyLingEditorWindow")]
        public static void Open()
        {
            GetWindow<PolyLingEditorWindow>("PolyLing Player");
        }

        private void OnEnable()
        {
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
        }

        private void OnEditorUpdate()
        {
            _core?.Tick();
            _core?.LateTick();
            Repaint();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _core?.Dispose();
            _core = null;
        }
    }
}
