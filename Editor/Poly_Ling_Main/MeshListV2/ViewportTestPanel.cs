// ViewportTestPanel.cs
// IProjectView経由で現物ModelContextを取得し、3Dビューポートを描画
// 描画は RemoteViewportCore に委譲（頂点・辺なし）

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Remote;

namespace Poly_Ling.MeshListV2
{
    public class ViewportTestPanel : EditorWindow
    {
        private const string UxmlPkg = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/MeshListV2/ViewportTestPanel.uxml";
        private const string UxmlAst = "Assets/Editor/Poly_Ling_Main/MeshListV2/ViewportTestPanel.uxml";

        private IProjectView _view;
        private LiveProjectView _liveView;
        private RemoteViewportCore _viewport;
        private Label _statusLabel;

        public static ViewportTestPanel Open(IProjectView view)
        {
            var w = GetWindow<ViewportTestPanel>();
            w.titleContent = new GUIContent("Viewport Test");
            w.minSize = new Vector2(320, 240);
            w._view = view;
            w._liveView = view as LiveProjectView;
            w.Show();
            return w;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var vt = Load<VisualTreeAsset>(UxmlPkg, UxmlAst);
            if (vt != null) vt.CloneTree(root);
            else { root.Add(new Label("UXML not found")); return; }

            _statusLabel = root.Q<Label>("status-label");
            var container = root.Q<IMGUIContainer>("preview-container");
            if (container != null) container.onGUIHandler = OnPreviewGUI;

            _viewport = new RemoteViewportCore();
            _viewport.RequestRepaint = Repaint;
            _viewport.Init();

            UpdateStatus();
        }

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            _viewport?.Dispose();
            _viewport = null;
        }

        private void OnPreviewGUI()
        {
            if (_viewport == null || _liveView == null) return;

            var model = _liveView.ProjectContext?.CurrentModel;
            if (model == null) return;

            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 10 || rect.height < 10) return;

            _viewport.Draw(rect, model);
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            if (_liveView == null)
            {
                _statusLabel.text = $"ERROR: 現物ではありません ({_view?.GetType().Name ?? "null"})";
                _statusLabel.style.color = new Color(0.8f, 0.3f, 0.3f);
            }
            else
            {
                _statusLabel.text = "Live viewport";
                _statusLabel.style.color = new Color(0.5f, 0.8f, 0.5f);
            }
        }

        private static T Load<T>(string pkg, string ast) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(pkg) ?? AssetDatabase.LoadAssetAtPath<T>(ast);
        }
    }
}
