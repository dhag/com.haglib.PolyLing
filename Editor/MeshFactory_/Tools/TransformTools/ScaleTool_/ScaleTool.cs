// Tools/ScaleTool.cs
// 頂点スケールツール
// シンプル実装版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    public partial class ScaleTool : IEditTool
    {
        ScaleSettings _settings = new ScaleSettings();
        public string Name => "Scale";
        public string DisplayName => "Scale";
        public string GetLocalizedDisplayName() => L.Get("Tool_Scale");
        public IToolSettings Settings => _settings;

        // スケール設定
        private float _scaleX = 1f, _scaleY = 1f, _scaleZ = 1f;
        private bool _uniform = true;
        private bool _useOriginPivot = false;

        // 状態
        private Vector3 _pivot;
        private HashSet<int> _affected = new HashSet<int>();
        private Dictionary<int, Vector3> _startPositions = new Dictionary<int, Vector3>();
        private bool _isDirty = false;
        private ToolContext _ctx;

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) { _ctx = ctx; return false; }
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }

        public void OnActivate(ToolContext ctx)
        {
            _ctx = ctx;
            ResetState();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_isDirty) ApplyScale(ctx);
            ResetState();
        }

        public void Reset() => ResetState();

        private void ResetState()
        {
            _scaleX = _scaleY = _scaleZ = 1f;
            _isDirty = false;
            _startPositions.Clear();
            _affected.Clear();
        }

        public void DrawSettingsUI()
        {
            if (_ctx == null) return;

            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            UpdateAffected();
            EditorGUILayout.LabelField(T("TargetVertices", _affected.Count), EditorStyles.miniLabel);

            if (_affected.Count == 0)
            {
                EditorGUILayout.HelpBox("頂点を選択してください", MessageType.Info);
                return;
            }

            // ピボット
            EditorGUI.BeginChangeCheck();
            _useOriginPivot = EditorGUILayout.Toggle(T("UseOrigin"), _useOriginPivot);
            if (EditorGUI.EndChangeCheck())
            {
                UpdatePivot();
                if (_isDirty) UpdatePreview();
            }

            EditorGUILayout.LabelField($"{T("Pivot")}: ({_pivot.x:F2}, {_pivot.y:F2}, {_pivot.z:F2})", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Uniform
            EditorGUI.BeginChangeCheck();
            bool newUniform = EditorGUILayout.Toggle(T("Uniform"), _uniform);
            if (EditorGUI.EndChangeCheck() && newUniform != _uniform)
            {
                _uniform = newUniform;
                if (_uniform) { _scaleY = _scaleZ = _scaleX; }
                UpdatePreview();
            }

            EditorGUILayout.Space(2);

            // スケールスライダー
            EditorGUI.BeginChangeCheck();

            if (_uniform)
            {
                float newScale = EditorGUILayout.Slider("XYZ", _scaleX,_settings.MIN_SCALE_XYZ ,_settings.MAX_SCALE_XYZ);
                if (EditorGUI.EndChangeCheck())
                {
                    _scaleX = _scaleY = _scaleZ = newScale;
                    UpdatePreview();
                }
            }
            else
            {
                float newX = EditorGUILayout.Slider("X", _scaleX, _settings.MIN_SCALE_X, _settings.MAX_SCALE_X);
                float newY = EditorGUILayout.Slider("Y", _scaleY, _settings.MIN_SCALE_Y, _settings.MAX_SCALE_Y);
                float newZ = EditorGUILayout.Slider("Z", _scaleZ, _settings.MIN_SCALE_Z, _settings.MAX_SCALE_Z);
                if (EditorGUI.EndChangeCheck())
                {
                    _scaleX = newX;
                    _scaleY = newY;
                    _scaleZ = newZ;
                    UpdatePreview();
                }
            }

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Apply")))
            {
                ApplyScale(_ctx);
                _scaleX = _scaleY = _scaleZ = 1f;
            }
            if (GUILayout.Button(T("Reset")))
            {
                RevertToStart();
                _scaleX = _scaleY = _scaleZ = 1f;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void UpdateAffected()
        {
            _affected.Clear();
            if (_ctx.SelectionState != null)
            {
                foreach (var v in _ctx.SelectionState.GetAllAffectedVertices(_ctx.MeshObject))
                    _affected.Add(v);
            }
            else if (_ctx.SelectedVertices != null)
            {
                foreach (var v in _ctx.SelectedVertices)
                    _affected.Add(v);
            }
        }

        private void UpdatePivot()
        {
            if (_useOriginPivot)
            {
                _pivot = Vector3.zero;
                return;
            }

            if (_affected.Count == 0 || _ctx.MeshObject == null)
            {
                _pivot = Vector3.zero;
                return;
            }

            Vector3 sum = Vector3.zero;
            foreach (int i in _affected)
                sum += _ctx.MeshObject.Vertices[i].Position;
            _pivot = sum / _affected.Count;
        }

        private void UpdatePreview()
        {
            if (_ctx?.MeshObject == null || _affected.Count == 0) return;

            // 初回: 開始位置を記録
            if (_startPositions.Count == 0)
            {
                UpdatePivot();
                foreach (int i in _affected)
                    _startPositions[i] = _ctx.MeshObject.Vertices[i].Position;
            }

            // スケール適用（開始位置から計算）
            Vector3 scale = new Vector3(_scaleX, _scaleY, _scaleZ);
            foreach (int i in _affected)
            {
                Vector3 offset = _startPositions[i] - _pivot;
                Vector3 scaled = Vector3.Scale(offset, scale);
                var v = _ctx.MeshObject.Vertices[i];
                v.Position = _pivot + scaled;
                _ctx.MeshObject.Vertices[i] = v;
            }

            _isDirty = true;
            _ctx.SyncMesh?.Invoke();
        }

        private void ApplyScale(ToolContext ctx)
        {
            if (!_isDirty || _startPositions.Count == 0) return;

            // Undo記録
            var indices = new List<int>();
            var oldPos = new List<Vector3>();
            var newPos = new List<Vector3>();

            foreach (var kv in _startPositions)
            {
                Vector3 cur = ctx.MeshObject.Vertices[kv.Key].Position;
                if (Vector3.Distance(kv.Value, cur) > 0.0001f)
                {
                    indices.Add(kv.Key);
                    oldPos.Add(kv.Value);
                    newPos.Add(cur);
                }
            }

            if (indices.Count > 0 && ctx.UndoController != null)
            {
                var record = new VertexMoveRecord(indices.ToArray(), oldPos.ToArray(), newPos.ToArray());
                ctx.UndoController.VertexEditStack.Record(record, T("UndoScale"));
            }

            // OriginalPositions更新
            if (ctx.OriginalPositions != null)
            {
                foreach (int i in _affected)
                {
                    if (i < ctx.OriginalPositions.Length)
                        ctx.OriginalPositions[i] = ctx.MeshObject.Vertices[i].Position;
                }
            }

            _startPositions.Clear();
            _isDirty = false;
        }

        private void RevertToStart()
        {
            if (_ctx?.MeshObject == null) return;

            foreach (var kv in _startPositions)
            {
                var v = _ctx.MeshObject.Vertices[kv.Key];
                v.Position = kv.Value;
                _ctx.MeshObject.Vertices[kv.Key] = v;
            }

            _startPositions.Clear();
            _isDirty = false;
            _ctx.SyncMesh?.Invoke();
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _ctx = ctx;
            if (_affected.Count == 0) return;

            var rect = ctx.PreviewRect;
            Vector2 p = ctx.WorldToScreenPos(_pivot, rect, ctx.CameraPosition, ctx.CameraTarget);
            if (!rect.Contains(p)) return;

            // ピボットマーカー（四角）
            float size = 8f;
            Rect r = new Rect(p.x - size / 2, p.y - size / 2, size, size);

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(0.2f, 0.8f, 1f);
            UnityEditor_Handles.DrawSolidRectangleWithOutline(r, new Color(0.2f, 0.8f, 1f, 0.5f), Color.white);
            UnityEditor_Handles.EndGUI();

            // 軸（スケールに応じて長さ変更）
            DrawAxis(ctx, rect, p, Vector3.right, Color.red, _scaleX);
            DrawAxis(ctx, rect, p, Vector3.up, Color.green, _scaleY);
            DrawAxis(ctx, rect, p, Vector3.forward, Color.blue, _scaleZ);
        }

        private void DrawAxis(ToolContext ctx, Rect rect, Vector2 origin, Vector3 dir, Color col, float scale)
        {
            Vector2 end = ctx.WorldToScreenPos(_pivot + dir * 0.1f, rect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 d = (end - origin).normalized * 35f * scale;

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = col;
            UnityEditor_Handles.DrawAAPolyLine(2f + Mathf.Abs(scale - 1f) * 2f,
                new Vector3(origin.x, origin.y, 0),
                new Vector3(origin.x + d.x, origin.y + d.y, 0));
            UnityEditor_Handles.EndGUI();
        }
    }
}
