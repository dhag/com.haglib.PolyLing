// Tools/RotateTool.cs
// 頂点回転ツール
// 全選択メッシュを対等に処理（primary/secondary区別なし）

using System.Collections.Generic;
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
        // Player ビュー用公開 API
        // ----------------------------------------------------------------
        // エディタ版 RotateTool.EditorUI が直接アクセスしていた private
        // フィールドを Player サブパネルから操作できるよう公開する。
        // ================================================================

        public float RotX { get => _rotX; set { _rotX = value; if (_isDirty) UpdatePreview(); } }
        public float RotY { get => _rotY; set { _rotY = value; if (_isDirty) UpdatePreview(); } }
        public float RotZ { get => _rotZ; set { _rotZ = value; if (_isDirty) UpdatePreview(); } }
        public bool  UseSnap      { get => _useSnap;      set => _useSnap      = value; }
        public float SnapAngle    { get => _snapAngle;    set => _snapAngle    = Mathf.Max(0.1f, value); }
        public bool  UseOriginPivot { get => _useOriginPivot; set { _useOriginPivot = value; UpdatePivot(); if (_isDirty) UpdatePreview(); } }
        public Vector3 PivotPublic  { get => _pivot; }
        public int   GetTotalAffectedCountPublic() { UpdateAffected(); return GetTotalAffectedCount(); }

        /// <summary>スライダー変更後に回転プレビューを更新する。ドラッグ開始を通知する。</summary>
        public void BeginSliderDrag()
        {
            if (!_isSliderDragging)
            {
                _isSliderDragging = true;
                _ctx?.EnterTransformDragging?.Invoke();
            }
        }

        /// <summary>ドラッグ終了・Undo 記録。</summary>
        public void EndSliderDrag()
        {
            if (_ctx != null) ApplyRotation(_ctx);
            ExitSliderDragging();
        }

        /// <summary>回転をリセットして元の位置に戻す。</summary>
        public void RevertPublic() { RevertToStart(); _rotX = _rotY = _rotZ = 0f; }

        /// <summary>コンテキストを手動設定する（スライダーUI から使用）。</summary>
        public void SetContextPublic(ToolContext ctx) { _ctx = ctx; UpdateAffected(); UpdatePivot(); }

        public void OnActivate(ToolContext ctx)
        {
            _ctx = ctx;
            ResetState();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_isSliderDragging)
            {
                _isSliderDragging = false;
                ctx.ExitTransformDragging?.Invoke();
            }
            if (_isDirty) ApplyRotation(ctx);
            ResetState();
        }

        public void Reset() => ResetState();

        private void ResetState()
        {
            _rotX = _rotY = _rotZ = 0f;
            _isDirty = false;
            _isSliderDragging = false;
            _multiMeshAffected.Clear();
            _multiMeshStartPositions.Clear();
        }

        private void ExitSliderDragging()
        {
            if (!_isSliderDragging) return;
            _isSliderDragging = false;
            _ctx?.ExitTransformDragging?.Invoke();
        }

        private void UpdateAffected()
        {
            _multiMeshAffected.Clear();

            var model = _ctx?.Model;
            if (model == null) return;

            foreach (int meshIdx in model.SelectedMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext == null || !meshContext.HasSelection)
                    continue;

                var meshObject = meshContext.MeshObject;
                if (meshObject == null)
                    continue;

                var affected = new HashSet<int>();
                foreach (var v in meshContext.SelectedVertices)
                    affected.Add(v);
                foreach (var edge in meshContext.SelectedEdges)
                {
                    affected.Add(edge.V1);
                    affected.Add(edge.V2);
                }
                foreach (var faceIdx in meshContext.SelectedFaces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                    {
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                            affected.Add(vIdx);
                    }
                }
                foreach (var lineIdx in meshContext.SelectedLines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2)
                        {
                            affected.Add(face.VertexIndices[0]);
                            affected.Add(face.VertexIndices[1]);
                        }
                    }
                }

                if (affected.Count > 0)
                {
                    _multiMeshAffected[meshIdx] = affected;
                }
            }
        }

        private int GetTotalAffectedCount()
        {
            int total = 0;
            foreach (var kv in _multiMeshAffected)
                total += kv.Value.Count;
            return total;
        }

        private void UpdatePivot()
        {
            if (_useOriginPivot)
            {
                _pivot = Vector3.zero;
                return;
            }

            if (GetTotalAffectedCount() == 0)
            {
                _pivot = Vector3.zero;
                return;
            }

            Vector3 sum = Vector3.zero;
            int totalCount = 0;
            var model = _ctx?.Model;

            foreach (var kv in _multiMeshAffected)
            {
                var meshContext = model?.GetMeshContext(kv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                foreach (int i in kv.Value)
                {
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        sum += meshObject.Vertices[i].Position;
                        totalCount++;
                    }
                }
            }

            _pivot = totalCount > 0 ? sum / totalCount : Vector3.zero;
        }

        private void UpdatePreview()
        {
            if (GetTotalAffectedCount() == 0) return;

            var model = _ctx?.Model;
            if (model == null) return;

            // 初回: 全メッシュの開始位置を記録
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
                    {
                        if (i >= 0 && i < meshObject.VertexCount)
                            startPos[i] = meshObject.Vertices[i].Position;
                    }
                    _multiMeshStartPositions[kv.Key] = startPos;
                }
            }

            // 回転適用（開始位置から計算）
            Quaternion rot = Quaternion.Euler(_rotX, _rotY, _rotZ);

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
                        Vector3 rotated = rot * offset;
                        var v = meshObject.Vertices[i];
                        v.Position = _pivot + rotated;
                        meshObject.Vertices[i] = v;
                    }
                }
            }

            _isDirty = true;
            _ctx.SyncMesh?.Invoke();
        }

        private void ApplyRotation(ToolContext ctx)
        {
            if (!_isDirty || _multiMeshStartPositions.Count == 0) return;

            var model = ctx?.Model;
            var allEntries = new List<MeshMoveEntry>();

            foreach (var meshKv in _multiMeshStartPositions)
            {
                var meshContext = model?.GetMeshContext(meshKv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                var indices = new List<int>();
                var oldPos = new List<Vector3>();
                var newPos = new List<Vector3>();

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        Vector3 cur = meshObject.Vertices[i].Position;
                        if (Vector3.Distance(posKv.Value, cur) > 0.0001f)
                        {
                            indices.Add(i);
                            oldPos.Add(posKv.Value);
                            newPos.Add(cur);
                        }
                    }
                }

                if (indices.Count > 0)
                {
                    allEntries.Add(new MeshMoveEntry
                    {
                        MeshContextIndex = meshKv.Key,
                        Indices = indices.ToArray(),
                        OldPositions = oldPos.ToArray(),
                        NewPositions = newPos.ToArray()
                    });

                    if (meshContext.OriginalPositions != null)
                    {
                        foreach (int i in indices)
                        {
                            if (i < meshContext.OriginalPositions.Length)
                                meshContext.OriginalPositions[i] = meshObject.Vertices[i].Position;
                        }
                    }
                }
            }

            if (allEntries.Count > 0 && ctx.UndoController != null)
            {
                ctx.UndoController.FocusVertexEdit();
                var record = new MultiMeshVertexMoveRecord(allEntries.ToArray());
                ctx.UndoController.VertexEditStack.Record(record, T("UndoRotate"));
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
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        var v = meshObject.Vertices[i];
                        v.Position = posKv.Value;
                        meshObject.Vertices[i] = v;
                    }
                }
            }

            _multiMeshStartPositions.Clear();
            _isDirty = false;
            _ctx.SyncMesh?.Invoke();
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _ctx = ctx;
            if (GetTotalAffectedCount() == 0) return;

            var rect = ctx.PreviewRect;
            Vector2 p = ctx.WorldToScreenPos(_pivot, rect, ctx.CameraPosition, ctx.CameraTarget);
            if (!rect.Contains(p)) return;

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(1f, 0.8f, 0.2f);
            UnityEditor_Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0), Vector3.forward, 6f);
            UnityEditor_Handles.EndGUI();

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
