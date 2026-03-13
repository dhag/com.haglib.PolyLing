// ViewportPanel.cs
// 独立3DビューポートEditorWindow
// ViewportCoreを所有し、UIToolkitでトグルバーを提供
// PanelContext経由でモデル変更通知を受信

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.MeshListV2
{
    public class ViewportPanel : EditorWindow
    {
        private const string UxmlPkg = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanelV2/ViewportPanel.uxml";
        private const string UxmlAst = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanelV2/ViewportPanel.uxml";

        // ================================================================
        // 外部参照
        // ================================================================

        private IProjectView _view;
        private LiveProjectView _liveView;
        private PanelContext _panelContext;

        // ================================================================
        // ViewportCore
        // ================================================================

        private ViewportCore _core;

        // ================================================================
        // UI
        // ================================================================

        private Label _statusLabel;
        private IMGUIContainer _previewContainer;

        // ================================================================
        // 公開
        // ================================================================

        public static ViewportPanel Open(IProjectView view, PanelContext panelContext = null)
        {
            var w = GetWindow<ViewportPanel>();
            w.titleContent = new GUIContent("Viewport");
            w.minSize = new Vector2(400, 300);
            w._view = view;
            w._liveView = view as LiveProjectView;
            w.wantsMouseMove = true;

            if (w._panelContext != panelContext)
            {
                if (w._panelContext != null) w._panelContext.OnViewChanged -= w.OnViewChanged;
                w._panelContext = panelContext;
                if (panelContext != null) panelContext.OnViewChanged += w.OnViewChanged;
            }

            w.InitCore();
            w.Show();
            return w;
        }

        public static bool IsOpen => HasOpenInstances<ViewportPanel>();

        public static ViewportPanel Current
        {
            get
            {
                if (!HasOpenInstances<ViewportPanel>()) return null;
                return GetWindow<ViewportPanel>(false, null, false);
            }
        }

        /// <summary>ViewportCoreへのアクセス（カメラ状態読み書き、コールバック設定等）</summary>
        public ViewportCore Core => _core;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var vt = Load<VisualTreeAsset>(UxmlPkg, UxmlAst);
            if (vt != null) vt.CloneTree(root);
            else { root.Add(new Label("UXML not found")); return; }

            _statusLabel = root.Q<Label>("status-label");

            BindToggle(root, "toggle-mesh", true, v => { if (_core != null) _core.ShowMesh = v; });
            BindToggle(root, "toggle-wire", true, v => { if (_core != null) _core.ShowWireframe = v; });
            BindToggle(root, "toggle-verts", false, v => { if (_core != null) _core.ShowVertices = v; });
            BindToggle(root, "toggle-unsel-wire", true, v => { if (_core != null) _core.ShowUnselectedWireframe = v; });
            BindToggle(root, "toggle-unsel-verts", false, v => { if (_core != null) _core.ShowUnselectedVertices = v; });
            BindToggle(root, "toggle-sel-only", false, v => { if (_core != null) _core.ShowSelectedMeshOnly = v; });
            BindToggle(root, "toggle-cull", true, v => { if (_core != null) _core.BackfaceCulling = v; });
            BindToggle(root, "toggle-vidx", false, v => { if (_core != null) _core.ShowVertexIndices = v; });
            BindToggle(root, "toggle-bones", true, v => { if (_core != null) _core.ShowBones = v; });
            BindToggle(root, "toggle-focus", true, v => { if (_core != null) _core.ShowFocusPoint = v; });

            var container = root.Q<IMGUIContainer>("preview-container");
            if (container != null)
            {
                _previewContainer = container;
                container.onGUIHandler = OnPreviewGUI;
            }

            InitCore();
            UpdateStatus();
        }

        private void OnDisable()
        {
            if (_panelContext != null) _panelContext.OnViewChanged -= OnViewChanged;
            _core?.Dispose();
            _core = null;
        }

        private void OnDestroy()
        {
            if (_panelContext != null) _panelContext.OnViewChanged -= OnViewChanged;
            _core?.Dispose();
            _core = null;
        }

        // ================================================================
        // 初期化
        // ================================================================

        private void InitCore()
        {
            if (_liveView == null) return;
            if (_core != null) return;

            var model = _liveView.ProjectContext?.CurrentModel;
            _core = new ViewportCore();
            _core.RequestRepaint = () =>
            {
                Repaint();
                _previewContainer?.MarkDirtyRepaint();
            };
            _core.Init(model);
        }

        // ================================================================
        // PanelContext通知
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_core == null) return;

            switch (kind)
            {
                case ChangeKind.Selection:
                    _core.SyncSelectionState();
                    _core.RequestNormal();
                    Repaint();
                    break;
                case ChangeKind.Attributes:
                    _core.RequestNormal();
                    Repaint();
                    break;
                case ChangeKind.ListStructure:
                    _core.NotifyTopologyChanged();
                    Repaint();
                    break;
                case ChangeKind.ModelSwitch:
                    var model = _liveView?.ProjectContext?.CurrentModel;
                    _core.SetModel(model);
                    Repaint();
                    break;
            }
        }

        // ================================================================
        // 描画
        // ================================================================

        private void OnPreviewGUI()
        {
            if (_core == null || _liveView == null) return;

            // モデル追跡
            var model = _liveView.ProjectContext?.CurrentModel;
            if (model != _core.CurrentModel)
                _core.SetModel(model);

            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 10 || rect.height < 10) return;

            _core.Draw(rect);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void BindToggle(VisualElement root, string name, bool initial, Action<bool> setter)
        {
            var t = root.Q<Toggle>(name);
            if (t == null) return;
            t.SetValueWithoutNotify(initial);
            t.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                _core?.RequestNormal();
                Repaint();
            });
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            _statusLabel.text = (_liveView == null) ? "ERROR: not live" :
                                (_core == null) ? "core init failed" : "";
        }

        private static T Load<T>(string pkg, string ast) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(pkg) ?? AssetDatabase.LoadAssetAtPath<T>(ast);
        }
    }
}
