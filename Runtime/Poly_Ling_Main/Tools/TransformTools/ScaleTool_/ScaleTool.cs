// Tools/ScaleTool.cs
// 頂点スケールツール
// 全選択メッシュを対等に処理（primary/secondary区別なし）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    public partial class ScaleTool : IEditTool
    {
        ScaleSettings _settings = new ScaleSettings();
        public string Name => "Scale";
        public string DisplayName => "Scale";
        public string GetLocalizedDisplayName() => L.Get("Tool_Scale");
        public IToolSettings Settings => _settings;

        private float _scaleX = 1f, _scaleY = 1f, _scaleZ = 1f;
        private bool _uniform = true;
        private bool _useOriginPivot = false;

        private Vector3 _pivot;
        private bool _isDirty = false;
        private bool _isSliderDragging = false;
        private ToolContext _ctx;

        // 全選択メッシュの影響頂点と開始位置
        private Dictionary<int, HashSet<int>> _multiMeshAffected = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, Dictionary<int, Vector3>> _multiMeshStartPositions = new Dictionary<int, Dictionary<int, Vector3>>();

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) { _ctx = ctx; return false; }
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }

        // ================================================================
        // Player ビュー用公開 API（エディタ版 ScaleTool.EditorUI 相当）
        // ================================================================

        public float ScaleX { get => _scaleX; set { _scaleX = value; if (_isDirty) UpdatePreview(); } }
        public float ScaleY { get => _scaleY; set { _scaleY = value; if (_isDirty) UpdatePreview(); } }
        public float ScaleZ { get => _scaleZ; set { _scaleZ = value; if (_isDirty) UpdatePreview(); } }
        public bool  UniformScale { get => _uniform; set { _uniform = value; if (_uniform) { _scaleY = _scaleZ = _scaleX; } UpdatePreview(); } }
        public bool  UseOriginPivot { get => _useOriginPivot; set { _useOriginPivot = value; UpdatePivot(); if (_isDirty) UpdatePreview(); } }
        public int   GetTotalAffectedCountPublic() { UpdateAffected(); return GetTotalAffectedCount(); }

        public void BeginSliderDrag()
        {
            if (!_isSliderDragging)
            {
                _isSliderDragging = true;
                _ctx?.EnterTransformDragging?.Invoke();
            }
        }

        public void EndSliderDrag()
        {
            if (_ctx != null) ApplyScale(_ctx);
            ExitSliderDragging();
            _scaleX = _scaleY = _scaleZ = 1f;
        }

        public void RevertPublic() { RevertToStart(); _scaleX = _scaleY = _scaleZ = 1f; }
        public void SetContextPublic(ToolContext ctx) { _ctx = ctx; UpdateAffected(); UpdatePivot(); }

        public void OnActivate(ToolContext ctx) { _ctx = ctx; ResetState(); }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_isSliderDragging) { _isSliderDragging = false; ctx.ExitTransformDragging?.Invoke(); }
            if (_isDirty) ApplyScale(ctx);
            ResetState();
        }

        public void Reset() => ResetState();

        private void ResetState()
        {
            _scaleX = _scaleY = _scaleZ = 1f;
            _isDirty = false;
            _isSliderDragging = false;
            _multiMeshAffected.Clear();
            _multiMeshStartPositions.Clear();
        }

        private void ExitSliderDragging() { if (!_isSliderDragging) return; _isSliderDragging = false; _ctx?.ExitTransformDragging?.Invoke(); }

        private void UpdateAffected()
        {
            _multiMeshAffected.Clear();
            var model = _ctx?.Model;
            if (model == null) return;

            foreach (int meshIdx in model.SelectedMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext == null || !meshContext.HasSelection) continue;
                var meshObject = meshContext.MeshObject;
                if (meshObject == null) continue;

                var affected = new HashSet<int>();
                foreach (var v in meshContext.SelectedVertices) affected.Add(v);
                foreach (var edge in meshContext.SelectedEdges) { affected.Add(edge.V1); affected.Add(edge.V2); }
                foreach (var faceIdx in meshContext.SelectedFaces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices) affected.Add(vIdx);
                }
                foreach (var lineIdx in meshContext.SelectedLines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2) { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                    }
                }
                if (affected.Count > 0) _multiMeshAffected[meshIdx] = affected;
            }
        }

        private int GetTotalAffectedCount()
        {
            int total = 0;
            foreach (var kv in _multiMeshAffected) total += kv.Value.Count;
            return total;
        }

        private void UpdatePivot()
        {
            if (_useOriginPivot) { _pivot = Vector3.zero; return; }
            if (GetTotalAffectedCount() == 0) { _pivot = Vector3.zero; return; }

            Vector3 sum = Vector3.zero; int totalCount = 0;
            var model = _ctx?.Model;
            foreach (var kv in _multiMeshAffected)
            {
                var meshContext = model?.GetMeshContext(kv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;
                foreach (int i in kv.Value)
                {
                    if (i >= 0 && i < meshObject.VertexCount) { sum += meshObject.Vertices[i].Position; totalCount++; }
                }
            }
            _pivot = totalCount > 0 ? sum / totalCount : Vector3.zero;
        }

        private void UpdatePreview()
        {
            if (GetTotalAffectedCount() == 0) return;
            var model = _ctx?.Model;
            if (model == null) return;

            if (_multiMeshStartPositions.Count == 0)
            {
                UpdatePivot();
                foreach (var kv in _multiMeshAffected)
                {
                    var meshContext = model.GetMeshContext(kv.Key);
                    var meshObject = meshContext?.MeshObject;
                    if (meshObject == null) continue;
                    var startPos = new Dictionary<int, Vector3>();
                    foreach (int i in kv.Value)
                        if (i >= 0 && i < meshObject.VertexCount) startPos[i] = meshObject.Vertices[i].Position;
                    _multiMeshStartPositions[kv.Key] = startPos;
                }
            }

            Vector3 scale = new Vector3(_scaleX, _scaleY, _scaleZ);
            foreach (var kv in _multiMeshStartPositions)
            {
                var meshContext = model.GetMeshContext(kv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;
                foreach (var posKv in kv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        Vector3 offset = posKv.Value - _pivot;
                        Vector3 scaled = Vector3.Scale(offset, scale);
                        var v = meshObject.Vertices[i]; v.Position = _pivot + scaled; meshObject.Vertices[i] = v;
                    }
                }
            }
            _isDirty = true;
            _ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void ApplyScale(ToolContext ctx)
        {
            if (!_isDirty || _multiMeshStartPositions.Count == 0) return;
            var model = ctx?.Model;
            var allEntries = new List<MeshMoveEntry>();

            foreach (var meshKv in _multiMeshStartPositions)
            {
                var meshContext = model?.GetMeshContext(meshKv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                var indices = new List<int>(); var oldPos = new List<Vector3>(); var newPos = new List<Vector3>();
                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        Vector3 cur = meshObject.Vertices[i].Position;
                        if (Vector3.Distance(posKv.Value, cur) > 0.0001f) { indices.Add(i); oldPos.Add(posKv.Value); newPos.Add(cur); }
                    }
                }

                if (indices.Count > 0)
                {
                    allEntries.Add(new MeshMoveEntry { MeshContextIndex = meshKv.Key, Indices = indices.ToArray(), OldPositions = oldPos.ToArray(), NewPositions = newPos.ToArray() });
                    if (meshContext.OriginalPositions != null)
                        foreach (int i in indices)
                            if (i < meshContext.OriginalPositions.Length) meshContext.OriginalPositions[i] = meshObject.Vertices[i].Position;
                }
            }

            if (allEntries.Count > 0 && ctx.UndoController != null)
            {
                ctx.UndoController.FocusVertexEdit();
                var record = new MultiMeshVertexMoveRecord(allEntries.ToArray());
                ctx.UndoController.VertexEditStack.Record(record, T("UndoScale"));
            }
            _multiMeshStartPositions.Clear();
            _isDirty = false;
        }

        private void RevertToStart()
        {
            var model = _ctx?.Model;
            foreach (var meshKv in _multiMeshStartPositions)
            {
                var meshContext = model?.GetMeshContext(meshKv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;
                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount) { var v = meshObject.Vertices[i]; v.Position = posKv.Value; meshObject.Vertices[i] = v; }
                }
            }
            _multiMeshStartPositions.Clear();
            _isDirty = false;
            _ctx.SyncMeshPositionsOnly?.Invoke();
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _ctx = ctx;
            if (GetTotalAffectedCount() == 0) return;
            var rect = ctx.PreviewRect;
            Vector2 p = ctx.WorldToScreenPos(_pivot, rect, ctx.CameraPosition, ctx.CameraTarget);
            if (!rect.Contains(p)) return;

            float size = 8f;
            Rect r = new Rect(p.x - size / 2, p.y - size / 2, size, size);
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(0.2f, 0.8f, 1f);
            UnityEditor_Handles.DrawSolidRectangleWithOutline(r, new Color(0.2f, 0.8f, 1f, 0.5f), Color.white);
            UnityEditor_Handles.EndGUI();

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
            UnityEditor_Handles.DrawAAPolyLine(2f + Mathf.Abs(scale - 1f) * 2f, new Vector3(origin.x, origin.y, 0), new Vector3(origin.x + d.x, origin.y + d.y, 0));
            UnityEditor_Handles.EndGUI();
        }
    }
}
