// Tools/RotateTool.cs
// 頂点回転ツール
// シンプル実装版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    public partial class RotateTool : IEditTool
    {
        public string Name => "Rotate";
        public string DisplayName => "Rotate";
        public string GetLocalizedDisplayName() => L.Get("Tool_Rotate");
        public IToolSettings Settings => null;

        // 回転設定
        private float _rotX, _rotY, _rotZ;
        private bool _useSnap = false;
        private float _snapAngle = 15f;
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
            if (_isDirty) ApplyRotation(ctx);
            ResetState();
        }

        public void Reset() => ResetState();

        private void ResetState()
        {
            _rotX = _rotY = _rotZ = 0f;
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

            // 回転スライダー
            EditorGUI.BeginChangeCheck();
            float newX = EditorGUILayout.Slider("X", _rotX, -180f, 180f);
            float newY = EditorGUILayout.Slider("Y", _rotY, -180f, 180f);
            float newZ = EditorGUILayout.Slider("Z", _rotZ, -180f, 180f);

            if (EditorGUI.EndChangeCheck())
            {
                if (_useSnap)
                {
                    newX = Mathf.Round(newX / _snapAngle) * _snapAngle;
                    newY = Mathf.Round(newY / _snapAngle) * _snapAngle;
                    newZ = Mathf.Round(newZ / _snapAngle) * _snapAngle;
                }
                _rotX = newX;
                _rotY = newY;
                _rotZ = newZ;
                UpdatePreview();
            }

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            _useSnap = EditorGUILayout.Toggle(T("Snap"), _useSnap, GUILayout.Width(100));
            if (_useSnap) _snapAngle = EditorGUILayout.FloatField(_snapAngle, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Apply")))
            {
                ApplyRotation(_ctx);
                _rotX = _rotY = _rotZ = 0f;
            }
            if (GUILayout.Button(T("Reset")))
            {
                RevertToStart();
                _rotX = _rotY = _rotZ = 0f;
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

            // 回転適用（開始位置から計算）
            Quaternion rot = Quaternion.Euler(_rotX, _rotY, _rotZ);
            foreach (int i in _affected)
            {
                Vector3 offset = _startPositions[i] - _pivot;
                Vector3 rotated = rot * offset;
                var v = _ctx.MeshObject.Vertices[i];
                v.Position = _pivot + rotated;
                _ctx.MeshObject.Vertices[i] = v;
            }

            _isDirty = true;
            _ctx.SyncMesh?.Invoke();
        }

        private void ApplyRotation(ToolContext ctx)
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
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, T("UndoRotate"));
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

            // ピボットマーカー
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(1f, 0.8f, 0.2f);
            UnityEditor_Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0), Vector3.forward, 6f);
            UnityEditor_Handles.EndGUI();

            // 軸
            DrawAxis(ctx, rect, p, Vector3.right, Color.red);
            DrawAxis(ctx, rect, p, Vector3.up, Color.green);
            DrawAxis(ctx, rect, p, Vector3.forward, Color.blue);
        }

        private void DrawAxis(ToolContext ctx, Rect rect, Vector2 origin, Vector3 dir, Color col)
        {
            Vector2 end = ctx.WorldToScreenPos(_pivot + dir * 0.1f, rect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 d = (end - origin).normalized * 40f;

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = col;
            UnityEditor_Handles.DrawAAPolyLine(2f, new Vector3(origin.x, origin.y, 0), new Vector3(origin.x + d.x, origin.y + d.y, 0));
            UnityEditor_Handles.EndGUI();
        }
    }
}
