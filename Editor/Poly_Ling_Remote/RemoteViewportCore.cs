// RemoteViewportCore.cs
// メッシュのみの3Dビューポート描画（頂点・辺なし）
// ViewportTestPanel と RemoteClient で共用

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Model;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// 軽量3Dビューポート：メッシュ描画 + カメラOrbit/Zoom/Pan
    /// UnifiedSystemAdapter非依存（PreviewRenderUtilityのみ使用）
    /// </summary>
    public class RemoteViewportCore : IDisposable
    {
        // ================================================================
        // PreviewRenderUtility
        // ================================================================

        private PreviewRenderUtility _preview;
        private Material _defaultMaterial;

        // ================================================================
        // カメラ状態
        // ================================================================

        public float RotX { get; set; } = 15f;
        public float RotY { get; set; } = -30f;
        public float Distance { get; set; } = 3f;
        public Vector3 Target { get; set; } = Vector3.zero;

        private bool _isDragging;
        private bool _isPanning;

        // ================================================================
        // 再描画要求コールバック
        // ================================================================

        public Action RequestRepaint;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Init()
        {
            if (_preview != null) return;
            _preview = new PreviewRenderUtility();
            _preview.cameraFieldOfView = 30f;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 200f;
        }

        public void Dispose()
        {
            if (_preview != null) { _preview.Cleanup(); _preview = null; }
            if (_defaultMaterial != null) { UnityEngine.Object.DestroyImmediate(_defaultMaterial); _defaultMaterial = null; }
        }

        // ================================================================
        // メイン描画（IMGUIContainerのonGUIHandlerから呼ぶ）
        // ================================================================

        public void Draw(Rect rect, ModelContext model)
        {
            if (_preview == null) Init();

            HandleCameraInput(rect);

            if (Event.current.type != EventType.Repaint) return;
            if (model == null) return;

            Quaternion rot = Quaternion.Euler(RotX, RotY, 0);
            Vector3 camPos = Target + rot * new Vector3(0, 0, -Distance);

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            _preview.camera.transform.position = camPos;
            _preview.camera.transform.rotation = Quaternion.LookRotation(Target - camPos, Vector3.up);

            DrawModel(model);

            _preview.camera.Render();
            Texture result = _preview.EndPreview();
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        // ================================================================
        // モデル描画
        // ================================================================

        private void DrawModel(ModelContext model)
        {
            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            Material defMat = GetDefaultMaterial();

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx == null || ctx.UnityMesh == null || !ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                    if (mat == null) mat = defMat;
                    if (mat == null) continue;
                    _preview.DrawMesh(mesh, Matrix4x4.identity, mat, sub);
                }
            }
        }

        // ================================================================
        // カメラ操作
        // ================================================================

        private void HandleCameraInput(Rect rect)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_isDragging && !_isPanning) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    Distance *= 1f + e.delta.y * 0.05f;
                    Distance = Mathf.Clamp(Distance, 0.05f, 100f);
                    e.Use();
                    RequestRepaint?.Invoke();
                    break;

                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        if (e.button == 0 || e.button == 1) { _isDragging = true; GUIUtility.hotControl = id; e.Use(); }
                        if (e.button == 2) { _isPanning = true; GUIUtility.hotControl = id; e.Use(); }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        RotY += e.delta.x * 0.5f;
                        RotX += e.delta.y * 0.5f;
                        RotX = Mathf.Clamp(RotX, -89f, 89f);
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    if (_isPanning)
                    {
                        Quaternion rot = Quaternion.Euler(RotX, RotY, 0);
                        Vector3 right = rot * Vector3.right;
                        Vector3 up = rot * Vector3.up;
                        float scale = Distance * 0.002f;
                        Target -= right * e.delta.x * scale;
                        Target += up * e.delta.y * scale;
                        e.Use();
                        RequestRepaint?.Invoke();
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
        // マテリアル
        // ================================================================

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
    }
}
