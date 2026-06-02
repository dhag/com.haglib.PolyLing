// Assets/Editor/Poly_Ling/Tools/Topology/EdgeExtrudeTool.cs
// 面張りツール - IToolSettings対応版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面張り（Extrude）ツール
    /// </summary>
    public partial class EdgeExtrudeTool : IEditTool
    {
        public string Name => "Extrude";
        public string DisplayName => "Extrude";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeExtrudeSettings _settings = new EdgeExtrudeSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        public EdgeExtrudeSettings.ExtrudeMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        public bool SnapToAxis
        {
            get => _settings.SnapToAxis;
            set => _settings.SnapToAxis = value;
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
        private VertexPair? _hitEdgeOnMouseDown;
        private int _hitLineOnMouseDown = -1;
        private const float DragThreshold = 4f;

        // ホバー
        private VertexPair? _hoverEdge;
        private int _hoverLine = -1;

        // 押し出し
        private Vector3 _extrudeDirection;
        private float _extrudeDistance;

        // ドラッグ中の頂点位置更新用
        private struct ExtrudeDragVertex { public int Index; public Vector3 BasePos; }
        private List<ExtrudeDragVertex> _extrudeDragVertices = new List<ExtrudeDragVertex>();

        // 押し出し対象
        private List<EdgeInfo> _targetEdges = new List<EdgeInfo>();
        private List<int> _targetLines = new List<int>();

        // Undo
        private MeshObjectSnapshot _snapshotBefore;

        private struct EdgeInfo
        {
            public int V0, V1;
            public int? AdjacentFace;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.CurrentButton != 0)
                return false;

            if (_state != ExtrudeState.Idle)
                return false;

            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;

            // _hitEdgeOnMouseDown はハンドラーが PrepareHit() でセット
            // _hitLineOnMouseDown はハンドラーが PrepareHit() でセット

            if (_hitEdgeOnMouseDown.HasValue || _hitLineOnMouseDown >= 0)
            {
                _state = ExtrudeState.PendingAction;
                // マウスダウン時にスナップショット取得
                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
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
                        if (_hitEdgeOnMouseDown.HasValue || _hitLineOnMouseDown >= 0)
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

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
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
            _extrudeDragVertices.Clear();
            _targetEdges.Clear();
            _targetLines.Clear();
            _snapshotBefore = null;
            _extrudeDistance = 0f;
        }

        public void OnSelectionChanged(ToolContext ctx)
        {
        }

        // ── UIToolkit hover support ───────────────────────────────────────
        /// <summary>現在ホバー中のエッジ（UIToolkit オーバーレイ用）</summary>
        public VertexPair? HoverEdge => _hoverEdge;
        public int HoverLine => _hoverLine;

        /// <summary>ハンドラーが GPU ホバー結果からセット。FindEdgeAtPosition/FindLineAtPosition（CPU・カリング無視）使用禁止。</summary>
        public void SetHoverEdge(VertexPair? edge, int line = -1)
        {
            bool canSet = _state == ExtrudeState.Idle || _state == ExtrudeState.PendingAction;
            _hoverEdge = canSet ? edge : (VertexPair?)null;
            _hoverLine = canSet ? line : -1;
        }

        /// <summary>OnLeftDragBegin でハンドラーが GPU ホバー結果から事前にセット。</summary>
        public void PrepareHit(VertexPair? edge, int line = -1)
        {
            _hitEdgeOnMouseDown = edge;
            _hitLineOnMouseDown = line;
        }

        // ================================================================
        // 押し出し処理
        // ================================================================

        private void StartExtrude(ToolContext ctx)
        {
            if (_hitEdgeOnMouseDown.HasValue)
            {
                var edge = _hitEdgeOnMouseDown.Value;
                if (!ctx.SelectionState.Edges.Contains(edge))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Edges.Add(edge);
                }
            }

            if (_hitLineOnMouseDown >= 0)
            {
                if (!ctx.SelectionState.Lines.Contains(_hitLineOnMouseDown))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Lines.Add(_hitLineOnMouseDown);
                }
            }

            CollectTargetEdges(ctx);

            if (_targetEdges.Count == 0 && _targetLines.Count == 0)
            {
                _state = ExtrudeState.Idle;
                return;
            }

            _extrudeDirection = (Mode == EdgeExtrudeSettings.ExtrudeMode.Normal)
                ? CalculateExtrudeDirection(ctx)
                : Vector3.up;
            _extrudeDistance = 0f;

            // トポロジーを即時実行し _extrudeDragVertices を確定させる
            ExecuteExtrude(ctx);  // 内部で ctx.SyncMesh (= NotifyTopologyChanged) を呼ぶ

            _state = ExtrudeState.Extruding;
            // EnterTransformDragging は使用しない。
            // 押し出しはトポロジー変更後の位置更新であり TransformDragging モードを使うと
            // エッジ/頂点描画が無効化されるため。SyncMeshPositionsOnly で直接更新する。
        }

        private void UpdateExtrude(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;

            switch (Mode)
            {
                case EdgeExtrudeSettings.ExtrudeMode.ViewPlane:
                    Vector3 worldDelta = ScreenDeltaToWorldDelta(ctx, totalDelta);
                    if (worldDelta.magnitude > 0.001f)
                    {
                        _extrudeDirection = worldDelta.normalized;
                        _extrudeDistance = worldDelta.magnitude;
                    }
                    break;

                case EdgeExtrudeSettings.ExtrudeMode.Normal:
                    Vector2 normalScreen = WorldDirToScreenDir(ctx, _extrudeDirection);
                    if (normalScreen.magnitude > 0.001f)
                    {
                        normalScreen.Normalize();
                        _extrudeDistance = Vector2.Dot(totalDelta, normalScreen) * 0.01f;
                    }
                    break;

                case EdgeExtrudeSettings.ExtrudeMode.Free:
                    _extrudeDirection = ScreenDeltaToWorldDelta(ctx, totalDelta);
                    _extrudeDistance = _extrudeDirection.magnitude;
                    if (_extrudeDistance > 0.001f)
                        _extrudeDirection.Normalize();
                    break;
            }

            if (SnapToAxis)
                _extrudeDirection = SnapToAxisDir(_extrudeDirection);

            var meshObject = ctx.FirstSelectedMeshObject;
            if (meshObject != null)
            {
                Vector3 offset = _extrudeDirection * _extrudeDistance;
                foreach (var dv in _extrudeDragVertices)
                {
                    if (dv.Index >= 0 && dv.Index < meshObject.VertexCount)
                        meshObject.Vertices[dv.Index].Position = dv.BasePos + offset;
                }
            }
            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndExtrude(ToolContext ctx)
        {
            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                var snapshotAfter = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                {
                    string __dbgDesc = "Extrude Edges";
                    UnityEngine.Debug.Log("[UndoDbg] VertexEdit.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _snapshotBefore = null;
        }

        private void ExecuteExtrude(ToolContext ctx)
        {
            Vector3 offset = _extrudeDirection * _extrudeDistance;
            var meshObject = ctx.FirstSelectedMeshObject;
            var vertexRemap = new Dictionary<int, int>();

            var allVertices = new HashSet<int>();
            foreach (var edge in _targetEdges)
            {
                if (edge.V0 >= 0 && edge.V0 < meshObject.VertexCount) allVertices.Add(edge.V0);
                if (edge.V1 >= 0 && edge.V1 < meshObject.VertexCount) allVertices.Add(edge.V1);
            }
            foreach (int lineIdx in _targetLines)
            {
                if (lineIdx < 0 || lineIdx >= meshObject.FaceCount) continue;
                var face = meshObject.Faces[lineIdx];
                if (face.VertexCount != 2) continue;
                if (face.VertexIndices[0] >= 0) allVertices.Add(face.VertexIndices[0]);
                if (face.VertexIndices[1] >= 0) allVertices.Add(face.VertexIndices[1]);
            }

            if (allVertices.Count == 0) return;

            _extrudeDragVertices.Clear();
            foreach (int vIdx in allVertices)
            {
                var oldV = meshObject.Vertices[vIdx];
                int newIdx = meshObject.VertexCount;
                var newV = new Vertex { Position = oldV.Position + offset };
                newV.UVs.AddRange(oldV.UVs);
                newV.Normals.AddRange(oldV.Normals);
                vertexRemap[vIdx] = newIdx;
                meshObject.Vertices.Add(newV);
                _extrudeDragVertices.Add(new ExtrudeDragVertex { Index = newIdx, BasePos = oldV.Position });
            }

            int matIdx = ctx.CurrentMaterialIndex;
            var newEdges = new List<VertexPair>();
            var newFaceIndices = new List<int>();

            foreach (var edge in _targetEdges)
            {
                if (!vertexRemap.TryGetValue(edge.V0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(edge.V1, out int nv1)) continue;

                var f = new Face { MaterialIndex = matIdx };

                bool reverseWinding = false;
                if (edge.AdjacentFace.HasValue && edge.AdjacentFace.Value < meshObject.FaceCount)
                {
                    var adjFace = meshObject.Faces[edge.AdjacentFace.Value];
                    int idxV0 = adjFace.VertexIndices.IndexOf(edge.V0);
                    int idxV1 = adjFace.VertexIndices.IndexOf(edge.V1);
                    if (idxV0 >= 0 && idxV1 >= 0)
                    {
                        reverseWinding = (idxV1 == (idxV0 + 1) % adjFace.VertexCount);
                    }
                }

                if (reverseWinding)
                {
                    f.VertexIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                    f.UVIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                    f.NormalIndices.AddRange(new[] { edge.V0, nv0, nv1, edge.V1 });
                }
                else
                {
                    f.VertexIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                    f.UVIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                    f.NormalIndices.AddRange(new[] { edge.V0, edge.V1, nv1, nv0 });
                }
                meshObject.Faces.Add(f);
                newFaceIndices.Add(meshObject.FaceCount - 1);
                newEdges.Add(new VertexPair(nv0, nv1));
            }

            foreach (int lineIdx in _targetLines)
            {
                var line = meshObject.Faces[lineIdx];
                int v0 = line.VertexIndices[0], v1 = line.VertexIndices[1];
                if (!vertexRemap.TryGetValue(v0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(v1, out int nv1)) continue;

                line.VertexIndices.Clear();
                line.UVIndices.Clear();
                line.NormalIndices.Clear();
                line.MaterialIndex = matIdx;

                line.VertexIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                line.UVIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                line.NormalIndices.AddRange(new[] { v0, v1, nv1, nv0 });
                newFaceIndices.Add(lineIdx);
            }

            ctx.SelectionState.Edges.Clear();
            ctx.SelectionState.Lines.Clear();
            ctx.SelectionState.Faces.Clear();
            foreach (var e in newEdges)
                ctx.SelectionState.Edges.Add(e);
            foreach (int fi in newFaceIndices)
                ctx.SelectionState.Faces.Add(fi);

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
                    AdjacentFace = FindAdjacentFace(ctx.FirstSelectedMeshObject, ep.V1, ep.V2)
                });
            }

            foreach (int idx in ctx.SelectionState.Lines)
            {
                if (idx >= 0 && idx < ctx.FirstSelectedMeshObject.FaceCount &&
                    ctx.FirstSelectedMeshObject.Faces[idx].VertexCount == 2)
                {
                    _targetLines.Add(idx);
                }
            }
        }

        private int? FindAdjacentFace(MeshObject md, int v0, int v1)
        {
            for (int i = 0; i < md.FaceCount; i++)
            {
                var f = md.Faces[i];
                if (f.VertexCount >= 3 && f.VertexIndices.Contains(v0) && f.VertexIndices.Contains(v1))
                    return i;
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

        private Vector3 CalculateExtrudeDirection(ToolContext ctx)
        {
            Vector3 avgNormal = Vector3.zero;
            int count = 0;
            foreach (var e in _targetEdges)
            {
                if (e.AdjacentFace.HasValue)
                {
                    avgNormal += CalculateFaceNormal(ctx.FirstSelectedMeshObject, e.AdjacentFace.Value);
                    count++;
                }
            }
            if (count > 0 && avgNormal.magnitude > 0.001f)
                return avgNormal.normalized;

            if (_targetEdges.Count > 0)
            {
                var e = _targetEdges[0];
                Vector3 edgeDir = (ctx.FirstSelectedMeshObject.Vertices[e.V1].Position - ctx.FirstSelectedMeshObject.Vertices[e.V0].Position).normalized;
                Vector3 perp = Vector3.Cross(edgeDir, Vector3.up);
                if (perp.magnitude < 0.001f) perp = Vector3.Cross(edgeDir, Vector3.forward);
                return perp.normalized;
            }
            return Vector3.up;
        }

        private Vector3 CalculateFaceNormal(MeshObject md, int fi)
        {
            var f = md.Faces[fi];
            if (f.VertexCount < 3) return Vector3.up;
            Vector3 p0 = md.Vertices[f.VertexIndices[0]].Position;
            Vector3 p1 = md.Vertices[f.VertexIndices[1]].Position;
            Vector3 p2 = md.Vertices[f.VertexIndices[2]].Position;
            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        private Vector3 GetSelectionCenter(ToolContext ctx)
        {
            Vector3 c = Vector3.zero;
            int n = 0;
            foreach (var e in _targetEdges)
            {
                c += ctx.FirstSelectedMeshObject.Vertices[e.V0].Position + ctx.FirstSelectedMeshObject.Vertices[e.V1].Position;
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

        private Vector3 SnapToAxisDir(Vector3 d)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(d.x), 0, 0);
            if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(d.y), 0);
            return new Vector3(0, 0, Mathf.Sign(d.z));
        }

    }
}
