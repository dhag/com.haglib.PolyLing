// Assets/Editor/Poly_Ling/Tools/Topology/FaceExtrudeTool.cs
// 面押し出しツール - IToolSettings対応版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面押し出しツール
    /// </summary>
    public partial class FaceExtrudeTool : IEditTool
    {
        public string Name => "Push";
        public string DisplayName => "Push";
        //ublic ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private FaceExtrudeSettings _settings = new FaceExtrudeSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private FaceExtrudeSettings.ExtrudeType Type
        {
            get => _settings.Type;
            set => _settings.Type = value;
        }

        private float BevelScale
        {
            get => _settings.BevelScale;
            set => _settings.BevelScale = value;
        }

        private bool IndividualNormals
        {
            get => _settings.IndividualNormals;
            set => _settings.IndividualNormals = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum ExtrudeState
        {
            Idle,
            PendingAction,
            Extruding
        }
        private ExtrudeState _state = ExtrudeState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private int _hitFaceOnMouseDown = -1;
        private const float DragThreshold = 4f;

        // 押し出し
        private float _extrudeDistance;
        private Vector3 _extrudeDirection;
        private List<Vector3> _previewPositions = new List<Vector3>();

        // ホバー
        private int _hoverFace = -1;

        // 押し出し対象
        private List<FaceExtrudeInfo> _targetFaces = new List<FaceExtrudeInfo>();

        // Undo
        private MeshObjectSnapshot _snapshotBefore;

        private struct FaceExtrudeInfo
        {
            public int FaceIndex;
            public List<int> VertexIndices;
            public Vector3 Normal;
            public Vector3 Center;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (Event.current.button != 0)
                return false;

            if (_state != ExtrudeState.Idle)
                return false;

            if (ctx.MeshObject == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;

            _hitFaceOnMouseDown = FindFaceAtPosition(ctx, mousePos);

            if (_hitFaceOnMouseDown >= 0)
            {
                _state = ExtrudeState.PendingAction;
                return false;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            switch (_state)
            {
                case ExtrudeState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitFaceOnMouseDown >= 0)
                        {
                            StartExtrude(ctx);
                        }
                        else
                        {
                            _state = ExtrudeState.Idle;
                            return false;
                        }
                    }
                    ctx.Repaint?.Invoke();
                    return true;

                case ExtrudeState.Extruding:
                    UpdateExtrude(ctx, mousePos);
                    ctx.Repaint?.Invoke();
                    return true;
            }

            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            bool handled = false;

            switch (_state)
            {
                case ExtrudeState.Extruding:
                    EndExtrude(ctx);
                    handled = true;
                    break;

                case ExtrudeState.PendingAction:
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshObject == null || ctx.SelectionState == null) return;

            if (_state == ExtrudeState.Idle || _state == ExtrudeState.PendingAction)
            {
                Vector2 mousePos = Event.current.mousePosition;
                _hoverFace = FindFaceAtPosition(ctx, mousePos);
            }
            else
            {
                _hoverFace = -1;
            }

            UnityEditor_Handles.BeginGUI();

            if (_state == ExtrudeState.Extruding)
            {
                foreach (var faceInfo in _targetFaces)
                {
                    DrawFaceHighlight(ctx, faceInfo, new Color(1f, 0.5f, 0f, 0.4f));
                }

                DrawExtrudePreview(ctx);

                GUI.color = Color.white;
                GUI.Label(new Rect(10, 60, 200, 20), $"Distance: {_extrudeDistance:F3}");
            }
            else
            {
                if (_hoverFace >= 0 && !ctx.SelectionState.Faces.Contains(_hoverFace))
                {
                    var faceInfo = CreateFaceInfo(ctx.MeshObject, _hoverFace);
                    if (faceInfo.HasValue)
                    {
                        DrawFaceHighlight(ctx, faceInfo.Value, new Color(1f, 1f, 1f, 0.3f));
                    }
                }

                if (_hoverFace >= 0 && ctx.SelectionState.Faces.Contains(_hoverFace))
                {
                    var faceInfo = CreateFaceInfo(ctx.MeshObject, _hoverFace);
                    if (faceInfo.HasValue)
                    {
                        DrawFaceHighlight(ctx, faceInfo.Value, new Color(1f, 1f, 1f, 0.5f));
                    }
                }
            }

            UnityEditor_Handles.color = Color.white;
            UnityEditor_Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            Type = (FaceExtrudeSettings.ExtrudeType)EditorGUILayout.EnumPopup("Type", Type);

            if (Type == FaceExtrudeSettings.ExtrudeType.Bevel)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(T("BevelSettings"), EditorStyles.miniBoldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(T("Scale"), GUILayout.Width(50));
                BevelScale = EditorGUILayout.Slider(BevelScale, 0.01f, 1f);//スライダーの上限下限
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("0.5", EditorStyles.miniButtonLeft)) BevelScale = 0.5f;
                if (GUILayout.Button("0.8", EditorStyles.miniButtonMid)) BevelScale = 0.8f;
                if (GUILayout.Button("1.0", EditorStyles.miniButtonRight)) BevelScale = 1.0f;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            IndividualNormals = EditorGUILayout.Toggle("Individual Normals", IndividualNormals);
        }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
            {
                ctx.SelectionState.Mode |= MeshSelectMode.Face;
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = ExtrudeState.Idle;
            _hitFaceOnMouseDown = -1;
            _previewPositions.Clear();
            _targetFaces.Clear();
            _snapshotBefore = null;
            _extrudeDistance = 0f;
            _extrudeDirection = Vector3.zero;
        }

        public void OnSelectionChanged(ToolContext ctx)
        {
        }

        // ================================================================
        // 押し出し処理
        // ================================================================

        private void StartExtrude(ToolContext ctx)
        {
            if (_hitFaceOnMouseDown >= 0 && !ctx.SelectionState.Faces.Contains(_hitFaceOnMouseDown))
            {
                ctx.SelectionState.Faces.Clear();
                ctx.SelectionState.Faces.Add(_hitFaceOnMouseDown);
            }

            CollectTargetFaces(ctx);

            if (_targetFaces.Count == 0)
            {
                _state = ExtrudeState.Idle;
                return;
            }

            _extrudeDirection = Vector3.zero;
            foreach (var faceInfo in _targetFaces)
                _extrudeDirection += faceInfo.Normal;
            _extrudeDirection = _extrudeDirection.magnitude > 0.001f
                ? _extrudeDirection.normalized
                : Vector3.up;

            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshUndoContext.MeshObject = ctx.MeshObject;
                // 【フェーズ1】SelectionStateを渡してFace選択も保存
                _snapshotBefore = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext, ctx.SelectionState);
            }

            _extrudeDistance = 0f;
            InitializePreview(ctx);

            _state = ExtrudeState.Extruding;
        }

        private void UpdateExtrude(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;

            Vector2 dirScreen = WorldDirToScreenDir(ctx, _extrudeDirection);
            if (dirScreen.magnitude > 0.001f)
            {
                dirScreen.Normalize();
                _extrudeDistance = Vector2.Dot(totalDelta, dirScreen) * 0.01f;
            }
            else
            {
                _extrudeDistance = -totalDelta.y * 0.01f;
            }

            UpdatePreview(ctx);
        }

        private Vector2 WorldDirToScreenDir(ToolContext ctx, Vector3 worldDir)
        {
            if (_targetFaces.Count == 0) return Vector2.up;

            Vector3 center = _targetFaces[0].Center;
            Vector2 screenCenter = ctx.WorldToScreen(center);
            Vector2 screenEnd = ctx.WorldToScreen(center + worldDir);

            return screenEnd - screenCenter;
        }

        private void EndExtrude(ToolContext ctx)
        {
            if (Mathf.Abs(_extrudeDistance) < 0.001f)
            {
                _snapshotBefore = null;
                return;
            }

            ExecuteExtrude(ctx);

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                ctx.UndoController.MeshUndoContext.MeshObject = ctx.MeshObject;
                // 【フェーズ1】SelectionStateを渡してFace選択も保存・復元
                var snapshotAfter = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, "Extrude Faces");
            }

            _snapshotBefore = null;
        }

        private void ExecuteExtrude(ToolContext ctx)
        {
            var meshObject = ctx.MeshObject;

            Vector3 avgNormal = Vector3.zero;
            if (!IndividualNormals)
            {
                foreach (var faceInfo in _targetFaces)
                    avgNormal += faceInfo.Normal;
                avgNormal = avgNormal.magnitude > 0.001f ? avgNormal.normalized : Vector3.up;
            }

            int materialIndex = ctx.CurrentMaterialIndex;
            var newVertexIndices = new List<int>();
            var newFaceIndices = new List<int>();

            foreach (var faceInfo in _targetFaces)
            {
                Vector3 normal = IndividualNormals ? faceInfo.Normal : avgNormal;
                Vector3 offset = normal * _extrudeDistance;
                Vector3 newCenter = faceInfo.Center + offset;

                var vertexMap = new Dictionary<int, int>();

                foreach (int oldVIdx in faceInfo.VertexIndices)
                {
                    if (oldVIdx < 0 || oldVIdx >= meshObject.VertexCount) continue;

                    var oldVertex = meshObject.Vertices[oldVIdx];
                    Vector3 newPos = oldVertex.Position + offset;

                    if (Type == FaceExtrudeSettings.ExtrudeType.Bevel)
                    {
                        Vector3 toCenter = newCenter - newPos;
                        newPos = newPos + toCenter * (1f - BevelScale);
                    }

                    var newVertex = new Vertex { Position = newPos };
                    newVertex.UVs.AddRange(oldVertex.UVs);
                    newVertex.Normals.AddRange(oldVertex.Normals);

                    int newIdx = meshObject.VertexCount;
                    meshObject.Vertices.Add(newVertex);
                    vertexMap[oldVIdx] = newIdx;
                    newVertexIndices.Add(newIdx);
                }

                int vertCount = faceInfo.VertexIndices.Count;
                for (int i = 0; i < vertCount; i++)
                {
                    int v0 = faceInfo.VertexIndices[i];
                    int v1 = faceInfo.VertexIndices[(i + 1) % vertCount];

                    if (!vertexMap.ContainsKey(v0) || !vertexMap.ContainsKey(v1)) continue;

                    int nv0 = vertexMap[v0];
                    int nv1 = vertexMap[v1];

                    var sideFace = new Face { MaterialIndex = materialIndex };
                    sideFace.VertexIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                    sideFace.UVIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                    sideFace.NormalIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                    meshObject.Faces.Add(sideFace);
                }

                var originalFace = meshObject.Faces[faceInfo.FaceIndex];
                int origVertCount = originalFace.VertexIndices.Count;

                while (originalFace.UVIndices.Count < origVertCount)
                    originalFace.UVIndices.Add(originalFace.VertexIndices[originalFace.UVIndices.Count]);
                while (originalFace.NormalIndices.Count < origVertCount)
                    originalFace.NormalIndices.Add(originalFace.VertexIndices[originalFace.NormalIndices.Count]);

                for (int i = 0; i < origVertCount; i++)
                {
                    int oldIdx = originalFace.VertexIndices[i];
                    if (vertexMap.ContainsKey(oldIdx))
                    {
                        int newIdx = vertexMap[oldIdx];
                        originalFace.VertexIndices[i] = newIdx;
                        originalFace.UVIndices[i] = newIdx;
                        originalFace.NormalIndices[i] = newIdx;
                    }
                }

                newFaceIndices.Add(faceInfo.FaceIndex);
            }

            ctx.SelectionState.Faces.Clear();
            foreach (int fIdx in newFaceIndices)
                ctx.SelectionState.Faces.Add(fIdx);

            ctx.SyncMesh?.Invoke();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void CollectTargetFaces(ToolContext ctx)
        {
            _targetFaces.Clear();

            foreach (int faceIdx in ctx.SelectionState.Faces)
            {
                var info = CreateFaceInfo(ctx.MeshObject, faceIdx);
                if (info.HasValue)
                    _targetFaces.Add(info.Value);
            }
        }

        private FaceExtrudeInfo? CreateFaceInfo(MeshObject meshObject, int faceIdx)
        {
            if (faceIdx < 0 || faceIdx >= meshObject.FaceCount)
                return null;

            var face = meshObject.Faces[faceIdx];
            if (face.VertexCount < 3)
                return null;

            var vertIndices = new List<int>(face.VertexIndices);

            Vector3 center = Vector3.zero;
            foreach (int vIdx in vertIndices)
            {
                if (vIdx >= 0 && vIdx < meshObject.VertexCount)
                    center += meshObject.Vertices[vIdx].Position;
            }
            center /= vertIndices.Count;

            Vector3 normal = Vector3.up;
            if (vertIndices.Count >= 3)
            {
                Vector3 p0 = meshObject.Vertices[vertIndices[0]].Position;
                Vector3 p1 = meshObject.Vertices[vertIndices[1]].Position;
                Vector3 p2 = meshObject.Vertices[vertIndices[2]].Position;
                normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            }

            return new FaceExtrudeInfo
            {
                FaceIndex = faceIdx,
                VertexIndices = vertIndices,
                Normal = normal,
                Center = center
            };
        }

        private int FindFaceAtPosition(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshObject == null) return -1;

            for (int fi = 0; fi < ctx.MeshObject.FaceCount; fi++)
            {
                var face = ctx.MeshObject.Faces[fi];
                if (face.VertexCount < 3) continue;

                var screenPoints = new List<Vector2>();
                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < ctx.MeshObject.VertexCount)
                        screenPoints.Add(ctx.WorldToScreen(ctx.MeshObject.Vertices[vIdx].Position));
                }

                if (screenPoints.Count >= 3 && PointInPolygon(mousePos, screenPoints))
                    return fi;
            }

            return -1;
        }

        private bool PointInPolygon(Vector2 p, List<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; j = i++)
            {
                if (((polygon[i].y > p.y) != (polygon[j].y > p.y)) &&
                    (p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private void InitializePreview(ToolContext ctx)
        {
            _previewPositions.Clear();

            foreach (var faceInfo in _targetFaces)
            {
                foreach (int vIdx in faceInfo.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < ctx.MeshObject.VertexCount)
                        _previewPositions.Add(ctx.MeshObject.Vertices[vIdx].Position);
                }
            }
        }

        private void UpdatePreview(ToolContext ctx)
        {
            Vector3 avgNormal = Vector3.zero;
            if (!IndividualNormals)
            {
                foreach (var faceInfo in _targetFaces)
                    avgNormal += faceInfo.Normal;
                avgNormal = avgNormal.magnitude > 0.001f ? avgNormal.normalized : Vector3.up;
            }

            int idx = 0;
            foreach (var faceInfo in _targetFaces)
            {
                Vector3 normal = IndividualNormals ? faceInfo.Normal : avgNormal;
                Vector3 offset = normal * _extrudeDistance;
                Vector3 newCenter = faceInfo.Center + offset;

                foreach (int vIdx in faceInfo.VertexIndices)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshObject.VertexCount || idx >= _previewPositions.Count)
                    {
                        idx++;
                        continue;
                    }

                    Vector3 newPos = ctx.MeshObject.Vertices[vIdx].Position + offset;

                    if (Type == FaceExtrudeSettings.ExtrudeType.Bevel)
                    {
                        Vector3 toCenter = newCenter - newPos;
                        newPos = newPos + toCenter * (1f - BevelScale);
                    }

                    _previewPositions[idx++] = newPos;
                }
            }
        }

        private void DrawExtrudePreview(ToolContext ctx)
        {
            int idx = 0;

            foreach (var faceInfo in _targetFaces)
            {
                int vertCount = faceInfo.VertexIndices.Count;

                UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
                for (int i = 0; i < vertCount; i++)
                {
                    int next = (i + 1) % vertCount;
                    if (idx + i >= _previewPositions.Count || idx + next >= _previewPositions.Count) continue;

                    Vector2 p0 = ctx.WorldToScreen(_previewPositions[idx + i]);
                    Vector2 p1 = ctx.WorldToScreen(_previewPositions[idx + next]);
                    DrawThickLine(p0, p1, 2f);
                }

                UnityEditor_Handles.color = new Color(1f, 0.6f, 0.2f, 0.6f);
                for (int i = 0; i < vertCount; i++)
                {
                    int vIdx = faceInfo.VertexIndices[i];
                    if (vIdx >= ctx.MeshObject.VertexCount || idx + i >= _previewPositions.Count) continue;

                    Vector2 orig = ctx.WorldToScreen(ctx.MeshObject.Vertices[vIdx].Position);
                    Vector2 newPos = ctx.WorldToScreen(_previewPositions[idx + i]);
                    DrawThickLine(orig, newPos, 1.5f);
                }

                idx += vertCount;
            }
        }

        private void DrawFaceHighlight(ToolContext ctx, FaceExtrudeInfo faceInfo, Color color)
        {
            if (faceInfo.VertexIndices.Count < 3) return;

            var screenPoints = new List<Vector2>();
            foreach (int vIdx in faceInfo.VertexIndices)
            {
                if (vIdx >= 0 && vIdx < ctx.MeshObject.VertexCount)
                    screenPoints.Add(ctx.WorldToScreen(ctx.MeshObject.Vertices[vIdx].Position));
            }

            if (screenPoints.Count >= 3)
            {
                UnityEditor_Handles.color = color;
                DrawPolygon(screenPoints);

                UnityEditor_Handles.color = new Color(color.r, color.g, color.b, 0.8f);
                for (int i = 0; i < screenPoints.Count; i++)
                {
                    int next = (i + 1) % screenPoints.Count;
                    DrawThickLine(screenPoints[i], screenPoints[next], 2f);
                }
            }
        }

        private void DrawPolygon(List<Vector2> points)
        {
            if (points.Count < 3) return;

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3[] verts = new Vector3[] { points[0], points[i], points[i + 1] };
                UnityEditor_Handles.DrawAAConvexPolygon(verts);
            }
        }

        private void DrawThickLine(Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 dir = (p1 - p0);
            if (dir.magnitude < 0.001f) return;
            dir.Normalize();

            Vector2 perp = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
            UnityEditor_Handles.DrawAAConvexPolygon(p0 - perp, p0 + perp, p1 + perp, p1 - perp);
        }
    }
}
