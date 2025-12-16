// Assets/Editor/MeshFactory/Tools/Topology/EdgeBevelTool.cs
// エッジベベルツール - IToolSettings対応版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using MeshFactory.UndoSystem;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    public class EdgeBevelTool : IEditTool
    {
        public string Name => "Bevel";
        public string DisplayName => "Bevel";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeBevelSettings _settings = new EdgeBevelSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private float Amount
        {
            get => _settings.Amount;
            set => _settings.Amount = value;
        }

        private int Segments
        {
            get => _settings.Segments;
            set => _settings.Segments = value;
        }

        private bool Fillet
        {
            get => _settings.Fillet;
            set => _settings.Fillet = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum BevelState { Idle, PendingAction, Beveling }
        private BevelState _state = BevelState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private VertexPair? _hitEdgeOnMouseDown;
        private const float DragThreshold = 4f;

        // ホバー
        private VertexPair? _hoverEdge;

        // ベベル対象
        private List<BevelEdgeInfo> _targetEdges = new List<BevelEdgeInfo>();
        private float _dragAmount;

        // Undo
        private MeshDataSnapshot _snapshotBefore;

        private struct BevelEdgeInfo
        {
            public int V0, V1;
            public int FaceA, FaceB;
            public Vector3 EdgeDir;
            public float EdgeLength;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (Event.current.button != 0)
                return false;

            if (_state != BevelState.Idle)
                return false;

            if (ctx.MeshData == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;
            _hitEdgeOnMouseDown = FindEdgeAtPosition(ctx, mousePos);

            if (_hitEdgeOnMouseDown.HasValue)
            {
                _state = BevelState.PendingAction;
                return false;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            switch (_state)
            {
                case BevelState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitEdgeOnMouseDown.HasValue)
                            StartBevel(ctx);
                        else
                        {
                            _state = BevelState.Idle;
                            return false;
                        }
                    }
                    ctx.Repaint?.Invoke();
                    return true;

                case BevelState.Beveling:
                    UpdateBevel(ctx, mousePos);
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
                case BevelState.Beveling:
                    EndBevel(ctx);
                    handled = true;
                    break;

                case BevelState.PendingAction:
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null || ctx.SelectionState == null) return;

            if (_state == BevelState.Idle || _state == BevelState.PendingAction)
            {
                Vector2 mousePos = Event.current.mousePosition;
                _hoverEdge = FindEdgeAtPosition(ctx, mousePos);
            }
            else
            {
                _hoverEdge = null;
            }

            UnityEditor_Handles.BeginGUI();

            if (_state == BevelState.Beveling)
            {
                UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 1f);
                foreach (var edge in _targetEdges)
                {
                    if (edge.V0 < 0 || edge.V0 >= ctx.MeshData.VertexCount) continue;
                    if (edge.V1 < 0 || edge.V1 >= ctx.MeshData.VertexCount) continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                    DrawThickLine(p0, p1, 4f);
                }

                DrawBevelPreview(ctx);

                GUI.color = Color.white;
                GUI.Label(new Rect(10, 60, 200, 20), $"Amount: {_dragAmount:F3}");
                GUI.Label(new Rect(10, 80, 200, 20), $"Segments: {Segments}");
            }
            else
            {
                if (_hoverEdge.HasValue)
                {
                    int v0 = _hoverEdge.Value.V1, v1 = _hoverEdge.Value.V2;
                    if (v0 >= 0 && v0 < ctx.MeshData.VertexCount &&
                        v1 >= 0 && v1 < ctx.MeshData.VertexCount)
                    {
                        UnityEditor_Handles.color = Color.white;
                        Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                        Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                        DrawThickLine(p0, p1, 5f);
                    }
                }
            }

            UnityEditor_Handles.color = Color.white;
            UnityEditor_Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Edge Bevel Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Drag edge to bevel.\n" +
                "Creates new face between adjacent faces.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Amount", GUILayout.Width(60));
            Amount = EditorGUILayout.FloatField(Amount, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.05", EditorStyles.miniButtonLeft)) Amount = 0.05f;
            if (GUILayout.Button("0.1", EditorStyles.miniButtonMid)) Amount = 0.1f;
            if (GUILayout.Button("0.2", EditorStyles.miniButtonRight)) Amount = 0.2f;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Segments", GUILayout.Width(70));
            Segments = EditorGUILayout.IntSlider(Segments, 1, 10);
            EditorGUILayout.EndHorizontal();

            if (Segments >= 2)
            {
                EditorGUILayout.Space(3);
                Fillet = EditorGUILayout.Toggle("Fillet (Round)", Fillet);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
        }

        public void OnDeactivate(ToolContext ctx) => Reset();

        public void Reset()
        {
            _state = BevelState.Idle;
            _hitEdgeOnMouseDown = null;
            _targetEdges.Clear();
            _snapshotBefore = null;
            _dragAmount = 0f;
        }

        public void OnSelectionChanged(ToolContext ctx) { }

        // ================================================================
        // ベベル処理
        // ================================================================

        private void StartBevel(ToolContext ctx)
        {
            if (_hitEdgeOnMouseDown.HasValue && !ctx.SelectionState.Edges.Contains(_hitEdgeOnMouseDown.Value))
            {
                ctx.SelectionState.Edges.Clear();
                ctx.SelectionState.Edges.Add(_hitEdgeOnMouseDown.Value);
            }

            CollectTargetEdges(ctx);

            if (_targetEdges.Count == 0)
            {
                _state = BevelState.Idle;
                return;
            }

            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                // 【フェーズ1】SelectionStateを渡してEdge選択も保存
                _snapshotBefore = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext, ctx.SelectionState);
            }

            _dragAmount = Amount;
            _state = BevelState.Beveling;
        }

        private void UpdateBevel(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;
            _dragAmount = Mathf.Max(0.001f, Amount + totalDelta.x * 0.002f);
        }

        private void EndBevel(ToolContext ctx)
        {
            if (_dragAmount < 0.001f)
            {
                _snapshotBefore = null;
                return;
            }

            Amount = _dragAmount;
            ExecuteBevel(ctx);

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                // 【フェーズ1】SelectionStateを渡してEdge選択も保存・復元
                var snapshotAfter = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext, ctx.SelectionState);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.VertexEditStack.Record(record, "Bevel Edges");
            }

            _snapshotBefore = null;
        }

        private void ExecuteBevel(ToolContext ctx)
        {
            var meshData = ctx.MeshData;
            float amount = _dragAmount;
            int segments = Segments;
            int matIdx = ctx.CurrentMaterialIndex;
            var orphanCandidates = new HashSet<int>();

            foreach (var edgeInfo in _targetEdges)
            {
                if (edgeInfo.FaceA < 0 || edgeInfo.FaceB < 0)
                    continue;

                int v0 = edgeInfo.V0;
                int v1 = edgeInfo.V1;
                orphanCandidates.Add(v0);
                orphanCandidates.Add(v1);

                var faceA = meshData.Faces[edgeInfo.FaceA];
                var faceB = meshData.Faces[edgeInfo.FaceB];

                Vector3 p0 = meshData.Vertices[v0].Position;
                Vector3 p1 = meshData.Vertices[v1].Position;

                Vector3 offsetA = GetInwardOffset(meshData, faceA, v0, v1);
                Vector3 offsetB = GetInwardOffset(meshData, faceB, v0, v1);

                float stepAmount = amount / segments;
                var newVerticesA = new List<int>();
                var newVerticesB = new List<int>();

                for (int s = 0; s < segments; s++)
                {
                    float t = (s + 1) / (float)segments;

                    Vector3 posA0, posA1, posB0, posB1;

                    if (Fillet && segments >= 2)
                    {
                        float angle = Mathf.PI * 0.5f * t;
                        float cosA = Mathf.Cos(angle);
                        float sinA = Mathf.Sin(angle);

                        posA0 = p0 + offsetA * amount * sinA + offsetB * amount * (1 - cosA);
                        posA1 = p1 + offsetA * amount * sinA + offsetB * amount * (1 - cosA);
                        posB0 = p0 + offsetB * amount * sinA + offsetA * amount * (1 - cosA);
                        posB1 = p1 + offsetB * amount * sinA + offsetA * amount * (1 - cosA);
                    }
                    else
                    {
                        posA0 = p0 + offsetA * stepAmount * (s + 1);
                        posA1 = p1 + offsetA * stepAmount * (s + 1);
                        posB0 = p0 + offsetB * stepAmount * (s + 1);
                        posB1 = p1 + offsetB * stepAmount * (s + 1);
                    }

                    int idxA0 = meshData.VertexCount;
                    meshData.Vertices.Add(new Vertex { Position = posA0 });
                    int idxA1 = meshData.VertexCount;
                    meshData.Vertices.Add(new Vertex { Position = posA1 });

                    newVerticesA.Add(idxA0);
                    newVerticesA.Add(idxA1);

                    if (s == segments - 1)
                    {
                        int idxB0 = meshData.VertexCount;
                        meshData.Vertices.Add(new Vertex { Position = posB0 });
                        int idxB1 = meshData.VertexCount;
                        meshData.Vertices.Add(new Vertex { Position = posB1 });

                        newVerticesB.Add(idxB0);
                        newVerticesB.Add(idxB1);
                    }
                }

                ReplaceFaceVertex(faceA, v0, newVerticesA[newVerticesA.Count - 2]);
                ReplaceFaceVertex(faceA, v1, newVerticesA[newVerticesA.Count - 1]);

                if (newVerticesB.Count >= 2)
                {
                    ReplaceFaceVertex(faceB, v0, newVerticesB[0]);
                    ReplaceFaceVertex(faceB, v1, newVerticesB[1]);
                }

                for (int s = 0; s < segments; s++)
                {
                    int a0 = (s == 0) ? v0 : newVerticesA[(s - 1) * 2];
                    int a1 = (s == 0) ? v1 : newVerticesA[(s - 1) * 2 + 1];
                    int b0 = newVerticesA[s * 2];
                    int b1 = newVerticesA[s * 2 + 1];

                    if (s == segments - 1 && newVerticesB.Count >= 2)
                    {
                        b0 = newVerticesB[0];
                        b1 = newVerticesB[1];
                    }

                    var bevelFace = new Face { MaterialIndex = matIdx };
                    bevelFace.VertexIndices.AddRange(new[] { a0, a1, b1, b0 });
                    bevelFace.UVIndices.AddRange(new[] { a0, a1, b1, b0 });
                    bevelFace.NormalIndices.AddRange(new[] { a0, a1, b1, b0 });
                    meshData.Faces.Add(bevelFace);
                }
            }

            RemoveOrphanVertices(meshData, orphanCandidates);

            ctx.SelectionState?.Edges.Clear();
            ctx.SyncMesh?.Invoke();
        }

        private void ReplaceFaceVertex(Face face, int oldIdx, int newIdx)
        {
            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                if (face.VertexIndices[i] == oldIdx)
                    face.VertexIndices[i] = newIdx;
            }
            for (int i = 0; i < face.UVIndices.Count; i++)
            {
                if (face.UVIndices[i] == oldIdx)
                    face.UVIndices[i] = newIdx;
            }
            for (int i = 0; i < face.NormalIndices.Count; i++)
            {
                if (face.NormalIndices[i] == oldIdx)
                    face.NormalIndices[i] = newIdx;
            }
        }

        private void RemoveOrphanVertices(MeshData meshData, HashSet<int> candidates)
        {
            var usedVertices = new HashSet<int>();
            foreach (var face in meshData.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    usedVertices.Add(vi);
            }

            var toRemove = candidates.Where(v => !usedVertices.Contains(v) && v >= 0 && v < meshData.VertexCount)
                                     .OrderByDescending(v => v)
                                     .ToList();

            foreach (int vertexIdx in toRemove)
            {
                meshData.Vertices.RemoveAt(vertexIdx);

                foreach (var face in meshData.Faces)
                {
                    for (int i = 0; i < face.VertexIndices.Count; i++)
                    {
                        if (face.VertexIndices[i] > vertexIdx)
                            face.VertexIndices[i]--;
                    }
                    for (int i = 0; i < face.UVIndices.Count; i++)
                    {
                        if (face.UVIndices[i] > vertexIdx)
                            face.UVIndices[i]--;
                    }
                    for (int i = 0; i < face.NormalIndices.Count; i++)
                    {
                        if (face.NormalIndices[i] > vertexIdx)
                            face.NormalIndices[i]--;
                    }
                }
            }
        }

        private Vector3 GetInwardOffset(MeshData meshData, Face face, int v0, int v1)
        {
            Vector3 faceNormal = CalculateFaceNormal(meshData, face);
            Vector3 p0 = meshData.Vertices[v0].Position;
            Vector3 p1 = meshData.Vertices[v1].Position;
            Vector3 edgeDir = (p1 - p0).normalized;
            Vector3 inward = Vector3.Cross(faceNormal, edgeDir).normalized;

            Vector3 faceCenter = CalculateFaceCenter(meshData, face);
            Vector3 edgeCenter = (p0 + p1) * 0.5f;
            Vector3 toCenter = (faceCenter - edgeCenter).normalized;

            if (Vector3.Dot(inward, toCenter) < 0)
                inward = -inward;

            return inward;
        }

        private Vector3 CalculateFaceNormal(MeshData meshData, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;

            Vector3 p0 = meshData.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshData.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshData.Vertices[face.VertexIndices[2]].Position;

            return Vector3.Cross(p1 - p0, p2 - p0).normalized;
        }

        private Vector3 CalculateFaceCenter(MeshData meshData, Face face)
        {
            Vector3 center = Vector3.zero;
            foreach (int vi in face.VertexIndices)
                center += meshData.Vertices[vi].Position;
            return center / face.VertexCount;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void CollectTargetEdges(ToolContext ctx)
        {
            _targetEdges.Clear();

            foreach (var edgePair in ctx.SelectionState.Edges)
            {
                int v0 = edgePair.V1;
                int v1 = edgePair.V2;

                if (v0 < 0 || v0 >= ctx.MeshData.VertexCount) continue;
                if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;

                var adjacentFaces = FindAdjacentFaces(ctx.MeshData, v0, v1);

                Vector3 p0 = ctx.MeshData.Vertices[v0].Position;
                Vector3 p1 = ctx.MeshData.Vertices[v1].Position;

                var info = new BevelEdgeInfo
                {
                    V0 = v0,
                    V1 = v1,
                    FaceA = adjacentFaces.Count > 0 ? adjacentFaces[0] : -1,
                    FaceB = adjacentFaces.Count > 1 ? adjacentFaces[1] : -1,
                    EdgeDir = (p1 - p0).normalized,
                    EdgeLength = Vector3.Distance(p0, p1)
                };

                _targetEdges.Add(info);
            }
        }

        private List<int> FindAdjacentFaces(MeshData meshData, int v0, int v1)
        {
            var result = new List<int>();

            for (int i = 0; i < meshData.FaceCount; i++)
            {
                var face = meshData.Faces[i];
                if (face.VertexCount < 3) continue;

                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                    result.Add(i);
            }

            return result;
        }

        private VertexPair? FindEdgeAtPosition(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return null;

            const float threshold = 8f;

            for (int fi = 0; fi < ctx.MeshData.FaceCount; fi++)
            {
                var face = ctx.MeshData.Faces[fi];
                if (face.VertexCount < 3) continue;

                for (int i = 0; i < face.VertexCount; i++)
                {
                    int v0 = face.VertexIndices[i];
                    int v1 = face.VertexIndices[(i + 1) % face.VertexCount];
                    if (v0 < 0 || v1 < 0 || v0 >= ctx.MeshData.VertexCount || v1 >= ctx.MeshData.VertexCount)
                        continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);

                    if (DistancePointToSegment(mousePos, p0, p1) < threshold)
                        return new VertexPair(v0, v1);
                }
            }

            return null;
        }

        private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }

        private void DrawBevelPreview(ToolContext ctx)
        {
            UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);

            foreach (var edge in _targetEdges)
            {
                if (edge.FaceA < 0 || edge.FaceB < 0) continue;

                Vector3 p0 = ctx.MeshData.Vertices[edge.V0].Position;
                Vector3 p1 = ctx.MeshData.Vertices[edge.V1].Position;

                var faceA = ctx.MeshData.Faces[edge.FaceA];
                var faceB = ctx.MeshData.Faces[edge.FaceB];

                Vector3 offsetA = GetInwardOffset(ctx.MeshData, faceA, edge.V0, edge.V1);
                Vector3 offsetB = GetInwardOffset(ctx.MeshData, faceB, edge.V0, edge.V1);

                float stepAmount = _dragAmount / Segments;

                Vector3 newA0 = p0 + offsetA * stepAmount;
                Vector3 newA1 = p1 + offsetA * stepAmount;
                Vector2 sA0 = ctx.WorldToScreen(newA0);
                Vector2 sA1 = ctx.WorldToScreen(newA1);
                DrawThickLine(sA0, sA1, 2f);

                Vector3 newB0 = p0 + offsetB * stepAmount;
                Vector3 newB1 = p1 + offsetB * stepAmount;
                Vector2 sB0 = ctx.WorldToScreen(newB0);
                Vector2 sB1 = ctx.WorldToScreen(newB1);
                DrawThickLine(sB0, sB1, 2f);

                UnityEditor_Handles.color = new Color(1f, 0.6f, 0.2f, 0.6f);
                DrawThickLine(sA0, sB0, 1f);
                DrawThickLine(sA1, sB1, 1f);
                UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
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
