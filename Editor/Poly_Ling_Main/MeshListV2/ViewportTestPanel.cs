// ViewportTestPanel.cs
// IProjectView経由で現物ModelContextを取得し、3Dビューポートを描画
// PreviewRenderUtility使用。カメラ: Orbit/Zoom/Pan

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.MeshListV2
{
    public class ViewportTestPanel : EditorWindow
    {
        private const string UxmlPkg = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/MeshListV2/ViewportTestPanel.uxml";
        private const string UxmlAst = "Assets/Editor/Poly_Ling_Main/MeshListV2/ViewportTestPanel.uxml";

        // ================================================================
        // 状態
        // ================================================================

        private IProjectView _view;
        private LiveProjectView _liveView;
        private PreviewRenderUtility _preview;
        private Material _defaultMaterial;
        private Label _statusLabel;

        // カメラ
        private float _rotX = 15f, _rotY = -30f;
        private float _distance = 3f;
        private Vector3 _target = Vector3.zero;
        private bool _isDragging;
        private bool _isPanning;

        // ================================================================
        // 公開
        // ================================================================

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
            var container = root.Q<IMGUIContainer>("preview-container");
            if (container != null) container.onGUIHandler = OnPreviewGUI;

            InitPreview();
            UpdateStatus();
        }

        private void OnDisable()
        {
            CleanupPreview();
        }

        private void OnDestroy()
        {
            CleanupPreview();
        }

        // ================================================================
        // PreviewRenderUtility
        // ================================================================

        private void InitPreview()
        {
            CleanupPreview();
            _preview = new PreviewRenderUtility();
            _preview.cameraFieldOfView = 30f;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 200f;
        }

        private void CleanupPreview()
        {
            if (_preview != null) { _preview.Cleanup(); _preview = null; }
            if (_defaultMaterial != null) { DestroyImmediate(_defaultMaterial); _defaultMaterial = null; }
        }

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Unlit/Color");
            if (s != null)
            {
                _defaultMaterial = new Material(s);
                _defaultMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
                _defaultMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f));
            }
            return _defaultMaterial;
        }

        // ================================================================
        // 描画
        // ================================================================

        private void OnPreviewGUI()
        {
            if (_preview == null || _liveView == null) return;

            var model = _liveView.ProjectContext?.CurrentModel;
            if (model == null) return;

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 10 || rect.height < 10) return;

            HandleInput(rect);

            if (Event.current.type != EventType.Repaint) return;

            // カメラ設定
            Quaternion rot = Quaternion.Euler(_rotX, _rotY, 0);
            Vector3 camPos = _target + rot * new Vector3(0, 0, -_distance);

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            _preview.camera.transform.position = camPos;
            _preview.camera.transform.rotation = Quaternion.LookRotation(_target - camPos, Vector3.up);

            // 全Drawableメッシュ描画
            DrawModel(model);

            _preview.camera.Render();
            Texture result = _preview.EndPreview();
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        private void DrawModel(ModelContext model)
        {
            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            Material defMat = GetDefaultMaterial();

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx == null || ctx.UnityMesh == null) continue;
                if (!ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                int subCount = mesh.subMeshCount;

                for (int sub = 0; sub < subCount; sub++)
                {
                    Material mat = null;
                    if (sub < model.MaterialCount)
                        mat = model.GetMaterial(sub);
                    if (mat == null)
                        mat = defMat;

                    _preview.DrawMesh(mesh, Matrix4x4.identity, mat, sub);
                }
            }
        }

        // ================================================================
        // カメラ操作
        // ================================================================

        private void HandleInput(Rect rect)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_isDragging && !_isPanning) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    _distance *= 1f + e.delta.y * 0.05f;
                    _distance = Mathf.Clamp(_distance, 0.1f, 100f);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        if (e.button == 0) { _isDragging = true; GUIUtility.hotControl = id; e.Use(); }
                        if (e.button == 2) { _isPanning = true; GUIUtility.hotControl = id; e.Use(); }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        _rotY += e.delta.x * 0.5f;
                        _rotX += e.delta.y * 0.5f;
                        _rotX = Mathf.Clamp(_rotX, -89f, 89f);
                        e.Use();
                        Repaint();
                    }
                    if (_isPanning)
                    {
                        Quaternion rot = Quaternion.Euler(_rotX, _rotY, 0);
                        Vector3 right = rot * Vector3.right;
                        Vector3 up = rot * Vector3.up;
                        float panScale = _distance * 0.002f;
                        _target -= right * e.delta.x * panScale;
                        _target += up * e.delta.y * panScale;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDragging || _isPanning)
                    {
                        _isDragging = false;
                        _isPanning = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // ================================================================
        // ステータス
        // ================================================================

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

        // ================================================================
        // ユーティリティ
        // ================================================================

        private static T Load<T>(string pkg, string ast) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(pkg) ?? AssetDatabase.LoadAssetAtPath<T>(ast);
        }
    }
}
