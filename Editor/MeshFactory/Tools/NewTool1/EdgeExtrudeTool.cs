// Tools/EdgeExtrudeTool.cs
// 面張りツール - MoveToolと完全に同じ動作パターン

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 面張り（Extrude）ツール
    /// MoveToolと同じ動作パターン：
    /// - 選択済みエッジをドラッグ → 押し出し
    /// - 未選択エッジをドラッグ → そのエッジを選択して押し出し
    /// - 空白ドラッグ → SimpleMeshFactory側で矩形選択
    /// </summary>
    public class EdgeExtrudeTool : IEditTool
    {
        public string Name => "Extrude";

        // === 状態（MoveToolと同じ構造） ===
        private enum ExtrudeState
        {
            Idle,
            PendingAction,
            Extruding
        }
        private ExtrudeState _state = ExtrudeState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private VertexPair? _hitEdgeOnMouseDown;
        private int _hitLineOnMouseDown = -1;
        private const float DragThreshold = 4f;

        // ホバー
        private VertexPair? _hoverEdge;
        private int _hoverLine = -1;

        // 押し出し
        private Vector3 _extrudeDirection;
        private float _extrudeDistance;
        private List<Vector3> _previewNewPositions = new List<Vector3>();

        // 押し出し対象
        private List<EdgeInfo> _targetEdges = new List<EdgeInfo>();
        private List<int> _targetLines = new List<int>();

        // 設定
        private ExtrudeMode _mode = ExtrudeMode.ViewPlane;
        private bool _snapToAxis = false;

        // Undo
        private MeshDataSnapshot _snapshotBefore;

        public enum ExtrudeMode
        {
            ViewPlane,
            Normal,
            Free
        }

        private struct EdgeInfo
        {
            public int V0, V1;
            public int? AdjacentFace;
        }

        // ================================================================
        // IEditTool 実装（MoveToolと同じ構造）
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // 左クリックのみ
            if (Event.current.button != 0)
                return false;

            if (_state != ExtrudeState.Idle)
                return false;

            if (ctx.MeshData == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;

            // エッジ/ラインのヒットテスト（MoveToolの頂点ヒットテストと同等）
            _hitEdgeOnMouseDown = FindEdgeAtPosition(ctx, mousePos);
            _hitLineOnMouseDown = FindLineAtPosition(ctx, mousePos);

            // エッジまたはラインがヒットした場合のみ移動準備
            if (_hitEdgeOnMouseDown.HasValue || _hitLineOnMouseDown >= 0)
            {
                _state = ExtrudeState.PendingAction;
                return false;  // SimpleMeshFactory側でクリック選択処理を行わせる
            }

            return false;  // 空白クリック、SimpleMeshFactory側で矩形選択等を処理
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            switch (_state)
            {
                case ExtrudeState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitEdgeOnMouseDown.HasValue || _hitLineOnMouseDown >= 0)
                        {
                            StartExtrude(ctx);
                        }
                        // 空白からのドラッグは矩形選択（メイン側で処理）
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
                    // クリック（ドラッグなし）はSimpleMeshFactory側で選択処理
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

            // ホバー検出（ドラッグ中でない場合のみ）
            if (_state == ExtrudeState.Idle || _state == ExtrudeState.PendingAction)
            {
                Vector2 mousePos = Event.current.mousePosition;
                _hoverEdge = FindEdgeAtPosition(ctx, mousePos);
                _hoverLine = FindLineAtPosition(ctx, mousePos);
            }
            else
            {
                _hoverEdge = null;
                _hoverLine = -1;
            }

            Handles.BeginGUI();

            if (_state == ExtrudeState.Extruding)
            {
                // C: 押し出し中 - オレンジで描画
                Handles.color = new Color(1f, 0.5f, 0f, 1f);

                foreach (var e in _targetEdges)
                {
                    if (e.V0 < 0 || e.V0 >= ctx.MeshData.VertexCount) continue;
                    if (e.V1 < 0 || e.V1 >= ctx.MeshData.VertexCount) continue;
                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[e.V0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[e.V1].Position);
                    DrawThickLine(p0, p1, 4f);
                }

                foreach (int li in _targetLines)
                {
                    if (li < 0 || li >= ctx.MeshData.FaceCount) continue;
                    var f = ctx.MeshData.Faces[li];
                    if (f.VertexCount != 2) continue;
                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[f.VertexIndices[0]].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[f.VertexIndices[1]].Position);
                    DrawThickLine(p0, p1, 4f);
                }

                DrawExtrudePreview(ctx);
                GUI.color = Color.white;
                GUI.Label(new Rect(10, 60, 200, 20), $"Distance: {_extrudeDistance:F3}");
            }
            else
            {
                // B: ホバー中のエッジ - 白（選択済みかどうか関係なく）
                Handles.color = Color.white;

                if (_hoverEdge.HasValue)
                {
                    int v0 = _hoverEdge.Value.V1, v1 = _hoverEdge.Value.V2;
                    if (v0 >= 0 && v0 < ctx.MeshData.VertexCount &&
                        v1 >= 0 && v1 < ctx.MeshData.VertexCount)
                    {
                        Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                        Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                        DrawThickLine(p0, p1, 5f);
                    }
                }

                if (_hoverLine >= 0 && _hoverLine < ctx.MeshData.FaceCount)
                {
                    var face = ctx.MeshData.Faces[_hoverLine];
                    if (face.VertexCount == 2)
                    {
                        int v0 = face.VertexIndices[0], v1 = face.VertexIndices[1];
                        if (v0 >= 0 && v0 < ctx.MeshData.VertexCount &&
                            v1 >= 0 && v1 < ctx.MeshData.VertexCount)
                        {
                            Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                            Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                            DrawThickLine(p0, p1, 5f);
                        }
                    }
                }

                // A: 選択中（通常）- 描画しない（SimpleMeshFactory側のシアンがそのまま表示）
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Extrude Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Drag edge to extrude.\n" +
                "Use Select/AdvancedSelect for multi-selection.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            _mode = (ExtrudeMode)EditorGUILayout.EnumPopup("Mode", _mode);
            _snapToAxis = EditorGUILayout.Toggle("Snap to Axis", _snapToAxis);

            EditorGUILayout.Space(5);

            int edgeCount = 0, lineCount = 0;
            // 選択数表示は現在のコンテキストがないので省略
            EditorGUILayout.LabelField($"Selected edges will be extruded", EditorStyles.miniLabel);
        }

        public void OnActivate(ToolContext ctx)
        {
            // エッジ選択モードを自動ON
            if (ctx.SelectionState != null)
            {
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = ExtrudeState.Idle;
            _hitEdgeOnMouseDown = null;
            _hitLineOnMouseDown = -1;
            _previewNewPositions.Clear();
            _targetEdges.Clear();
            _targetLines.Clear();
            _snapshotBefore = null;
            _extrudeDistance = 0f;
        }

        public void OnSelectionChanged(ToolContext ctx)
        {
            // 特に処理なし（DrawGizmoで毎回SelectionStateを参照）
        }

        // ================================================================
        // 押し出し処理（MoveToolのStartVertexMoveと同等の構造）
        // ================================================================

        private void StartExtrude(ToolContext ctx)
        {
            // MoveToolのStartVertexMoveと同じパターン：
            // 未選択をドラッグした場合、その要素のみ選択
            bool selectionChanged = false;

            if (_hitEdgeOnMouseDown.HasValue)
            {
                var edge = _hitEdgeOnMouseDown.Value;
                if (!ctx.SelectionState.Edges.Contains(edge))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Edges.Add(edge);
                    selectionChanged = true;
                }
            }

            if (_hitLineOnMouseDown >= 0)
            {
                if (!ctx.SelectionState.Lines.Contains(_hitLineOnMouseDown))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Lines.Add(_hitLineOnMouseDown);
                    selectionChanged = true;
                }
            }

            // 選択をローカルにコピー
            CollectTargetEdges(ctx);

            if (_targetEdges.Count == 0 && _targetLines.Count == 0)
            {
                _state = ExtrudeState.Idle;
                return;
            }

            // Undoスナップショット
            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                _snapshotBefore = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            }

            // 押し出し方向を初期化
            _extrudeDirection = (_mode == ExtrudeMode.Normal)
                ? CalculateExtrudeDirection(ctx)
                : Vector3.up;
            _extrudeDistance = 0f;

            // プレビュー初期化
            InitializePreviewPositions(ctx);

            _state = ExtrudeState.Extruding;
        }

        private void UpdateExtrude(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;

            switch (_mode)
            {
                case ExtrudeMode.ViewPlane:
                    Vector3 worldDelta = ScreenDeltaToWorldDelta(ctx, totalDelta);
                    if (worldDelta.magnitude > 0.001f)
                    {
                        _extrudeDirection = worldDelta.normalized;
                        _extrudeDistance = worldDelta.magnitude;
                    }
                    break;

                case ExtrudeMode.Normal:
                    Vector2 normalScreen = WorldDirToScreenDir(ctx, _extrudeDirection);
                    if (normalScreen.magnitude > 0.001f)
                    {
                        normalScreen.Normalize();
                        _extrudeDistance = Vector2.Dot(totalDelta, normalScreen) * 0.01f;
                    }
                    break;

                case ExtrudeMode.Free:
                    _extrudeDirection = ScreenDeltaToWorldDelta(ctx, totalDelta);
                    _extrudeDistance = _extrudeDirection.magnitude;
                    if (_extrudeDistance > 0.001f)
                        _extrudeDirection.Normalize();
                    break;
            }

            if (_snapToAxis)
                _extrudeDirection = SnapToAxis(_extrudeDirection);

            UpdatePreviewPositions(ctx);
        }

        private void EndExtrude(ToolContext ctx)
        {
            if (Mathf.Abs(_extrudeDistance) < 0.001f)
            {
                _snapshotBefore = null;
                return;
            }

            ExecuteExtrude(ctx);

            // Undo記録
            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                var snapshotAfter = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter);
                ctx.UndoController.VertexEditStack.Record(record, "Extrude Edges");
            }

            _snapshotBefore = null;
        }

        private void ExecuteExtrude(ToolContext ctx)
        {
            Vector3 offset = _extrudeDirection * _extrudeDistance;
            var meshData = ctx.MeshData;
            var vertexRemap = new Dictionary<int, int>();

            // 全頂点を収集
            var allVertices = new HashSet<int>();
            foreach (var edge in _targetEdges)
            {
                if (edge.V0 >= 0 && edge.V0 < meshData.VertexCount) allVertices.Add(edge.V0);
                if (edge.V1 >= 0 && edge.V1 < meshData.VertexCount) allVertices.Add(edge.V1);
            }
            foreach (int lineIdx in _targetLines)
            {
                if (lineIdx < 0 || lineIdx >= meshData.FaceCount) continue;
                var face = meshData.Faces[lineIdx];
                if (face.VertexCount != 2) continue;
                if (face.VertexIndices[0] >= 0) allVertices.Add(face.VertexIndices[0]);
                if (face.VertexIndices[1] >= 0) allVertices.Add(face.VertexIndices[1]);
            }

            if (allVertices.Count == 0) return;

            // 新頂点を作成
            foreach (int vIdx in allVertices)
            {
                var oldV = meshData.Vertices[vIdx];
                var newV = new Vertex { Position = oldV.Position + offset };
                newV.UVs.AddRange(oldV.UVs);
                newV.Normals.AddRange(oldV.Normals);
                vertexRemap[vIdx] = meshData.VertexCount;
                meshData.Vertices.Add(newV);
            }

            int matIdx = ctx.CurrentMaterialIndex;
            var newEdges = new List<VertexPair>();

            // エッジから四角形面を作成
            foreach (var edge in _targetEdges)
            {
                if (!vertexRemap.TryGetValue(edge.V0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(edge.V1, out int nv1)) continue;

                var f = new Face { MaterialIndex = matIdx };

                // 隣接面の巻き方向を確認して、側面の頂点順序を決定
                bool reverseWinding = false;
                if (edge.AdjacentFace.HasValue && edge.AdjacentFace.Value < meshData.FaceCount)
                {
                    var adjFace = meshData.Faces[edge.AdjacentFace.Value];
                    int idxV0 = adjFace.VertexIndices.IndexOf(edge.V0);
                    int idxV1 = adjFace.VertexIndices.IndexOf(edge.V1);
                    if (idxV0 >= 0 && idxV1 >= 0)
                    {
                        // 隣接面でV0の次がV1なら、側面は逆向きにする
                        reverseWinding = (idxV1 == (idxV0 + 1) % adjFace.VertexCount);
                    }
                }

                if (reverseWinding)
                {
                    // V0 → nv0 → nv1 → V1
                    f.VertexIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                    f.UVIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                    f.NormalIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                }
                else
                {
                    // V0 → V1 → nv1 → nv0
                    f.VertexIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                    f.UVIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                    f.NormalIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                }
                meshData.Faces.Add(f);
                newEdges.Add(new VertexPair(nv0, nv1));
            }

            // ラインを四角形に変換
            foreach (int lineIdx in _targetLines)
            {
                var line = meshData.Faces[lineIdx];
                int v0 = line.VertexIndices[0], v1 = line.VertexIndices[1];
                if (!vertexRemap.TryGetValue(v0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(v1, out int nv1)) continue;

                line.VertexIndices.Clear();
                line.UVIndices.Clear();
                line.NormalIndices.Clear();
                line.MaterialIndex = matIdx;

                // 側面は常に同じ向き: v0 → v1 → nv1 → nv0
                line.VertexIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                line.UVIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                line.NormalIndices.AddRange(new[] { v0, v1, nv1, nv0 });
            }

            // 選択を新しいエッジに更新（連続操作用）
            ctx.SelectionState.Edges.Clear();
            ctx.SelectionState.Lines.Clear();
            foreach (var e in newEdges)
                ctx.SelectionState.Edges.Add(e);

            ctx.SyncMesh?.Invoke();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void CollectTargetEdges(ToolContext ctx)
        {
            _targetEdges.Clear();
            _targetLines.Clear();

            foreach (var ep in ctx.SelectionState.Edges)
            {
                _targetEdges.Add(new EdgeInfo
                {
                    V0 = ep.V1,
                    V1 = ep.V2,
                    AdjacentFace = FindAdjacentFace(ctx.MeshData, ep.V1, ep.V2)
                });
            }

            foreach (int idx in ctx.SelectionState.Lines)
            {
                if (idx >= 0 && idx < ctx.MeshData.FaceCount &&
                    ctx.MeshData.Faces[idx].VertexCount == 2)
                {
                    _targetLines.Add(idx);
                }
            }
        }

        private int? FindAdjacentFace(MeshData md, int v0, int v1)
        {
            for (int i = 0; i < md.FaceCount; i++)
            {
                var f = md.Faces[i];
                if (f.VertexCount >= 3 && f.VertexIndices.Contains(v0) && f.VertexIndices.Contains(v1))
                    return i;
            }
            return null;
        }

        private VertexPair? FindEdgeAtPosition(ToolContext ctx, Vector2 mousePos)
        {
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

        private int FindLineAtPosition(ToolContext ctx, Vector2 mousePos)
        {
            const float threshold = 8f;
            for (int fi = 0; fi < ctx.MeshData.FaceCount; fi++)
            {
                var face = ctx.MeshData.Faces[fi];
                if (face.VertexCount != 2) continue;

                int v0 = face.VertexIndices[0], v1 = face.VertexIndices[1];
                if (v0 < 0 || v1 < 0 || v0 >= ctx.MeshData.VertexCount || v1 >= ctx.MeshData.VertexCount)
                    continue;

                Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);

                if (DistancePointToSegment(mousePos, p0, p1) < threshold)
                    return fi;
            }
            return -1;
        }

        private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }

        private void InitializePreviewPositions(ToolContext ctx)
        {
            _previewNewPositions.Clear();
            foreach (var e in _targetEdges)
            {
                _previewNewPositions.Add(ctx.MeshData.Vertices[e.V0].Position);
                _previewNewPositions.Add(ctx.MeshData.Vertices[e.V1].Position);
            }
            foreach (int li in _targetLines)
            {
                var f = ctx.MeshData.Faces[li];
                _previewNewPositions.Add(ctx.MeshData.Vertices[f.VertexIndices[0]].Position);
                _previewNewPositions.Add(ctx.MeshData.Vertices[f.VertexIndices[1]].Position);
            }
        }

        private void UpdatePreviewPositions(ToolContext ctx)
        {
            Vector3 offset = _extrudeDirection * _extrudeDistance;
            int idx = 0;
            foreach (var e in _targetEdges)
            {
                if (idx < _previewNewPositions.Count)
                    _previewNewPositions[idx++] = ctx.MeshData.Vertices[e.V0].Position + offset;
                if (idx < _previewNewPositions.Count)
                    _previewNewPositions[idx++] = ctx.MeshData.Vertices[e.V1].Position + offset;
            }
            foreach (int li in _targetLines)
            {
                var f = ctx.MeshData.Faces[li];
                if (idx < _previewNewPositions.Count)
                    _previewNewPositions[idx++] = ctx.MeshData.Vertices[f.VertexIndices[0]].Position + offset;
                if (idx < _previewNewPositions.Count)
                    _previewNewPositions[idx++] = ctx.MeshData.Vertices[f.VertexIndices[1]].Position + offset;
            }
        }

        private void DrawExtrudePreview(ToolContext ctx)
        {
            int idx = 0;

            // 新エッジ（緑）
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            for (int i = 0; i < _targetEdges.Count && idx + 1 < _previewNewPositions.Count; i++)
            {
                Vector2 np0 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                Vector2 np1 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                DrawThickLine(np0, np1, 2f);
            }
            for (int i = 0; i < _targetLines.Count && idx + 1 < _previewNewPositions.Count; i++)
            {
                Vector2 np0 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                Vector2 np1 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                DrawThickLine(np0, np1, 2f);
            }

            // 接続線（オレンジ）
            Handles.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            idx = 0;
            foreach (var e in _targetEdges)
            {
                if (idx + 1 >= _previewNewPositions.Count) break;
                Vector2 o0 = ctx.WorldToScreen(ctx.MeshData.Vertices[e.V0].Position);
                Vector2 o1 = ctx.WorldToScreen(ctx.MeshData.Vertices[e.V1].Position);
                Vector2 n0 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                Vector2 n1 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                DrawThickLine(o0, n0, 1.5f);
                DrawThickLine(o1, n1, 1.5f);
            }
            foreach (int li in _targetLines)
            {
                if (idx + 1 >= _previewNewPositions.Count) break;
                var f = ctx.MeshData.Faces[li];
                Vector2 o0 = ctx.WorldToScreen(ctx.MeshData.Vertices[f.VertexIndices[0]].Position);
                Vector2 o1 = ctx.WorldToScreen(ctx.MeshData.Vertices[f.VertexIndices[1]].Position);
                Vector2 n0 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                Vector2 n1 = ctx.WorldToScreen(_previewNewPositions[idx++]);
                DrawThickLine(o0, n0, 1.5f);
                DrawThickLine(o1, n1, 1.5f);
            }
        }

        private Vector3 CalculateExtrudeDirection(ToolContext ctx)
        {
            Vector3 avgNormal = Vector3.zero;
            int count = 0;
            foreach (var e in _targetEdges)
            {
                if (e.AdjacentFace.HasValue)
                {
                    avgNormal += CalculateFaceNormal(ctx.MeshData, e.AdjacentFace.Value);
                    count++;
                }
            }
            if (count > 0 && avgNormal.magnitude > 0.001f)
                return avgNormal.normalized;

            if (_targetEdges.Count > 0)
            {
                var e = _targetEdges[0];
                Vector3 edgeDir = (ctx.MeshData.Vertices[e.V1].Position - ctx.MeshData.Vertices[e.V0].Position).normalized;
                Vector3 perp = Vector3.Cross(edgeDir, Vector3.up);
                if (perp.magnitude < 0.001f) perp = Vector3.Cross(edgeDir, Vector3.forward);
                return perp.normalized;
            }
            return Vector3.up;
        }

        private Vector3 CalculateFaceNormal(MeshData md, int fi)
        {
            var f = md.Faces[fi];
            if (f.VertexCount < 3) return Vector3.up;
            Vector3 p0 = md.Vertices[f.VertexIndices[0]].Position;
            Vector3 p1 = md.Vertices[f.VertexIndices[1]].Position;
            Vector3 p2 = md.Vertices[f.VertexIndices[2]].Position;
            return Vector3.Cross(p1 - p0, p2 - p0).normalized;
        }

        private Vector3 GetSelectionCenter(ToolContext ctx)
        {
            Vector3 c = Vector3.zero;
            int n = 0;
            foreach (var e in _targetEdges)
            {
                c += ctx.MeshData.Vertices[e.V0].Position + ctx.MeshData.Vertices[e.V1].Position;
                n += 2;
            }
            return n > 0 ? c / n : Vector3.zero;
        }

        private Vector2 WorldDirToScreenDir(ToolContext ctx, Vector3 wd)
        {
            Vector3 c = GetSelectionCenter(ctx);
            return ctx.WorldToScreen(c + wd) - ctx.WorldToScreen(c);
        }

        private Vector3 ScreenDeltaToWorldDelta(ToolContext ctx, Vector2 sd)
        {
            if (ctx.ScreenDeltaToWorldDelta != null)
                return ctx.ScreenDeltaToWorldDelta(sd, ctx.CameraPosition, ctx.CameraTarget, ctx.CameraDistance, ctx.PreviewRect);
            float s = ctx.CameraDistance * 0.001f;
            return new Vector3(sd.x * s, -sd.y * s, 0f);
        }

        private Vector3 SnapToAxis(Vector3 d)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(d.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(d.y), 0);
            return new Vector3(0, 0, Mathf.Sign(d.z));
        }

        private void DrawThickLine(Vector2 p0, Vector2 p1, float t)
        {
            Vector2 d = (p1 - p0);
            if (d.magnitude < 0.001f) return;
            d.Normalize();
            Vector2 perp = new Vector2(-d.y, d.x) * t * 0.5f;
            Handles.DrawAAConvexPolygon(p0 - perp, p0 + perp, p1 + perp, p1 - perp);
        }
    }
}