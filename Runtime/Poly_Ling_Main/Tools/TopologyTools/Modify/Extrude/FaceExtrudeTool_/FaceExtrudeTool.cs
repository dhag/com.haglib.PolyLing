// Assets/Editor/Poly_Ling/Tools/Topology/FaceExtrudeTool.cs
// 面押し出しツール - IToolSettings対応版

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
        public FaceExtrudeSettings.ExtrudeType Type
        {
            get => _settings.Type;
            set => _settings.Type = value;
        }

        public float BevelScale
        {
            get => _settings.BevelScale;
            set => _settings.BevelScale = value;
        }

        public bool IndividualNormals
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

        // ドラッグ中の頂点位置更新用
        private struct FaceDragVertex
        {
            public int     Index;
            public Vector3 BasePos;
            public Vector3 Normal;
            public Vector3 FaceCenter;
        }
        private List<FaceDragVertex> _faceDragVertices = new List<FaceDragVertex>();

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
            if (ctx.CurrentButton != 0)
                return false;

            if (_state != ExtrudeState.Idle)
                return false;

            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;

            // _hitFaceOnMouseDown はハンドラーが PrepareHit() でセット

            if (_hitFaceOnMouseDown >= 0)
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

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
            {
                ctx.SelectionState.Mode |= MeshSelectMode.Face;
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_state == ExtrudeState.Extruding)
                ctx.ExitTransformDragging?.Invoke();
            Reset();
        }

        public void Reset()
        {
            _state = ExtrudeState.Idle;
            _hitFaceOnMouseDown = -1;
            _faceDragVertices.Clear();
            _targetFaces.Clear();
            _snapshotBefore = null;
            _extrudeDistance = 0f;
            _extrudeDirection = Vector3.zero;
        }

        public void OnSelectionChanged(ToolContext ctx)
        {
        }

        // ── UIToolkit hover support ───────────────────────────────────────
        /// <summary>現在ホバー中の面インデックス（-1=なし、UIToolkit オーバーレイ用）</summary>
        public int HoverFace => _hoverFace;

        /// <summary>ハンドラーが GPU ホバー結果からセット。FindFaceAtPosition（CPU・カリング無視）使用禁止。</summary>
        public void SetHoverFace(int faceIdx)
        {
            _hoverFace = (_state == ExtrudeState.Idle || _state == ExtrudeState.PendingAction) ? faceIdx : -1;
        }

        /// <summary>OnLeftDragBegin でハンドラーが GPU ホバー結果から事前にセット。</summary>
        public void PrepareHit(int faceIdx) { _hitFaceOnMouseDown = faceIdx; }

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

            _extrudeDistance = 0f;

            // トポロジーを即時実行し _faceDragVertices を確定させる
            ExecuteExtrude(ctx);  // 内部で ctx.SyncMesh (= NotifyTopologyChanged) を呼ぶ

            _state = ExtrudeState.Extruding;
            ctx.EnterTransformDragging?.Invoke();
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

            var meshObject = ctx.FirstSelectedMeshObject;
            if (meshObject != null)
            {
                foreach (var dv in _faceDragVertices)
                {
                    if (dv.Index < 0 || dv.Index >= meshObject.VertexCount) continue;
                    Vector3 offset  = dv.Normal * _extrudeDistance;
                    Vector3 pos     = dv.BasePos + offset;
                    if (Type == FaceExtrudeSettings.ExtrudeType.Bevel)
                    {
                        Vector3 newCenter = dv.FaceCenter + offset;
                        Vector3 toCenter  = newCenter - pos;
                        pos = pos + toCenter * (1f - BevelScale);
                    }
                    meshObject.Vertices[dv.Index].Position = pos;
                }
            }
            ctx.SyncMeshPositionsOnly?.Invoke();
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
            ctx.ExitTransformDragging?.Invoke();

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                var snapshotAfter = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                {
                    string __dbgDesc = "Extrude Faces";
                    UnityEngine.Debug.Log("[UndoDbg] VertexEdit.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _snapshotBefore = null;
        }

        private void ExecuteExtrude(ToolContext ctx)
        {
            var meshObject = ctx.FirstSelectedMeshObject;

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

            _faceDragVertices.Clear();

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

                    int newIdx = meshObject.VertexCount;
                    var newVertex = new Vertex { Position = newPos };
                    newVertex.UVs.AddRange(oldVertex.UVs);
                    newVertex.Normals.AddRange(oldVertex.Normals);

                    meshObject.Vertices.Add(newVertex);
                    vertexMap[oldVIdx] = newIdx;
                    newVertexIndices.Add(newIdx);

                    _faceDragVertices.Add(new FaceDragVertex
                    {
                        Index      = newIdx,
                        BasePos    = oldVertex.Position,
                        Normal     = normal,
                        FaceCenter = faceInfo.Center,
                    });
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
                var info = CreateFaceInfo(ctx.FirstSelectedMeshObject, faceIdx);
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
                normal = NormalHelper.CalculateFaceNormal(p0, p1, p2);
            }

            return new FaceExtrudeInfo
            {
                FaceIndex = faceIdx,
                VertexIndices = vertIndices,
                Normal = normal,
                Center = center
            };
        }





    }
}
