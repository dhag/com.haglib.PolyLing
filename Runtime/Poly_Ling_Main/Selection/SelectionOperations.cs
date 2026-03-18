// Assets/Editor/Poly_Ling/Selection/SelectionOperations.cs

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Rendering;

namespace Poly_Ling.Selection
{
    /// <summary>
    /// 選択操作を行うクラス
    /// </summary>
    public class SelectionOperations
    {
        private SelectionState _state;
        private TopologyCache _topology;
        private IVisibilityProvider _visibilityProvider;
        
        public float VertexHitRadius { get; set; } = 10f;
        public float EdgeHitDistance { get; set; } = 8f;
        public bool EnableFaceHit { get; set; } = true;

        public SelectionOperations(SelectionState state, TopologyCache topology)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        }

        public void SetState(SelectionState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void SetTopology(TopologyCache topology)
        {
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        }

        /// <summary>
        /// 可視性プロバイダーを設定（カリング対応用）
        /// nullの場合は全て可視として扱う
        /// </summary>
        public void SetVisibilityProvider(IVisibilityProvider provider)
        {
            _visibilityProvider = provider;
        }

        // === ヒットテスト ===

        public int FindVertexAt(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen)
        {
            if (meshObject == null || worldToScreen == null) return -1;

            float minDist = VertexHitRadius;
            int nearest = -1;

            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                    continue;

                Vector2 vScreen = worldToScreen(meshObject.Vertices[i].Position);
                float dist = Vector2.Distance(screenPos, vScreen);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        public VertexPair? FindEdgeAt(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen)
        {
            if (meshObject == null || worldToScreen == null) return null;

            float minDist = EdgeHitDistance;
            VertexPair? nearest = null;

            foreach (var pair in _topology.AllEdgePairs)
            {
                // 可視性チェック：エッジが属する面のいずれかが可視なら可視
                if (_visibilityProvider != null)
                {
                    bool anyVisible = false;
                    foreach (var faceEdge in _topology.GetEdgesAt(pair))
                    {
                        if (_visibilityProvider.IsFaceVisible(faceEdge.FaceIndex))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                    if (!anyVisible) continue;
                }

                Vector2 p1 = worldToScreen(meshObject.Vertices[pair.V1].Position);
                Vector2 p2 = worldToScreen(meshObject.Vertices[pair.V2].Position);
                
                float dist = DistanceToLineSegment(screenPos, p1, p2);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = pair;
                }
            }

            return nearest;
        }

        public int FindLineAt(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen)
        {
            if (meshObject == null || worldToScreen == null) return -1;

            // 補助線分（lineType=1）は常に可視として扱われるため、
            // カリングチェックは不要

            float minDist = EdgeHitDistance;
            int nearest = -1;

            foreach (int faceIdx in _topology.AuxLineIndices)
            {
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount != 2) continue;

                Vector2 p1 = worldToScreen(meshObject.Vertices[face.VertexIndices[0]].Position);
                Vector2 p2 = worldToScreen(meshObject.Vertices[face.VertexIndices[1]].Position);
                
                float dist = DistanceToLineSegment(screenPos, p1, p2);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = faceIdx;
                }
            }

            return nearest;
        }

        public int FindFaceAt(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen,
            Vector3 cameraPosition)
        {
            if (!EnableFaceHit || meshObject == null || worldToScreen == null) return -1;

            int nearest = -1;
            float nearestDepth = float.MaxValue;

            foreach (int faceIdx in _topology.RealFaceIndices)
            {
                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(faceIdx))
                    continue;

                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                var screenPoints = new Vector2[face.VertexCount];
                Vector3 centroid = Vector3.zero;
                
                for (int i = 0; i < face.VertexCount; i++)
                {
                    var worldPos = meshObject.Vertices[face.VertexIndices[i]].Position;
                    screenPoints[i] = worldToScreen(worldPos);
                    centroid += worldPos;
                }
                centroid /= face.VertexCount;

                if (IsPointInPolygon(screenPos, screenPoints))
                {
                    float depth = Vector3.Distance(cameraPosition, centroid);
                    if (depth < nearestDepth)
                    {
                        nearestDepth = depth;
                        nearest = faceIdx;
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// 有効なモードに対してヒットテストを行う（優先順位: 頂点 > エッジ > ライン > 面）
        /// </summary>
        public HitResult FindAtEnabledModes(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen,
            Vector3 cameraPosition)
        {
            var result = HitResult.None;
            var mode = _state.Mode;

            // 優先順位: Vertex > Edge > Line > Face
            
            // 1. Vertex
            if (mode.Has(MeshSelectMode.Vertex))
            {
                int vertexIdx = FindVertexAt(screenPos, meshObject, worldToScreen);
                if (vertexIdx >= 0)
                {
                    result.HitType = MeshSelectMode.Vertex;
                    result.VertexIndex = vertexIdx;
                    return result;
                }
            }

            // 2. Edge
            if (mode.Has(MeshSelectMode.Edge))
            {
                var edgePair = FindEdgeAt(screenPos, meshObject, worldToScreen);
                if (edgePair.HasValue)
                {
                    result.HitType = MeshSelectMode.Edge;
                    result.EdgePair = edgePair;
                    return result;
                }
            }

            // 3. Line
            if (mode.Has(MeshSelectMode.Line))
            {
                int lineIdx = FindLineAt(screenPos, meshObject, worldToScreen);
                if (lineIdx >= 0)
                {
                    result.HitType = MeshSelectMode.Line;
                    result.LineIndex = lineIdx;
                    return result;
                }
            }

            // 4. Face
            if (mode.Has(MeshSelectMode.Face))
            {
                int faceIdx = FindFaceAt(screenPos, meshObject, worldToScreen, cameraPosition);
                if (faceIdx >= 0)
                {
                    result.HitType = MeshSelectMode.Face;
                    result.FaceIndex = faceIdx;
                    return result;
                }
            }

            return result;
        }

        /// <summary>
        /// 後方互換: FindAtCurrentMode
        /// </summary>
        public HitResult FindAtCurrentMode(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen,
            Vector3 cameraPosition)
        {
            return FindAtEnabledModes(screenPos, meshObject, worldToScreen, cameraPosition);
        }

        // === 選択操作 ===

        public bool SelectAt(
            Vector2 screenPos,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen,
            Vector3 cameraPosition,
            bool additive = false)
        {
            var hit = FindAtEnabledModes(screenPos, meshObject, worldToScreen, cameraPosition);
            return ApplyHitResult(hit, additive);
        }

        /// <summary>
        /// ヒット結果を適用（Shift/Ctrl選択対応）
        /// </summary>
        /// <param name="hit">ヒット結果</param>
        /// <param name="shiftHeld">Shift押下: 追加選択（トグル）</param>
        /// <param name="ctrlHeld">Ctrl押下: トグル選択（選択解除）</param>
        /// <returns>選択が変更されたか</returns>
        public bool ApplyHitResult(HitResult hit, bool shiftHeld, bool ctrlHeld)
        {
            // Shift または Ctrl で追加/トグルモード
            bool additive = shiftHeld || ctrlHeld;
            return ApplyHitResult(hit, additive);
        }

        /// <summary>
        /// ヒット結果を適用（混在選択対応）
        /// 
        /// 【Phase 6修正】
        /// 非加算モード（Shift/Ctrl なし）でクリックした場合:
        /// - ヒットした要素の種類以外の選択を全てクリア
        /// - 例: Edgeをクリック → Vertex/Face/Line選択をクリア
        /// これにより、異なるモード間での選択の混在を防ぐ
        /// </summary>
        public bool ApplyHitResult(HitResult hit, bool additive = false)
        {
            // ヒットなしの場合
            if (hit.HitType == MeshSelectMode.None)
            {
                if (!additive)
                {
                    // 非加算モードで空クリック: 全選択をクリア（モードに関係なく）
                    _state.ClearAll();
                    return true;
                }
                return false;
            }

            // ★ Phase 6: 非加算モードでは他モードの選択をクリア
            if (!additive)
            {
                ClearOtherModeSelections(hit.HitType);
            }

            // ヒットした要素の種類に応じて処理
            switch (hit.HitType)
            {
                case MeshSelectMode.Vertex:
                    if (hit.VertexIndex >= 0)
                    {
                        if (additive)
                            return _state.ToggleVertex(hit.VertexIndex);
                        else
                        {
                            bool wasSelected = _state.Vertices.Contains(hit.VertexIndex);
                            _state.Vertices.Clear();
                            _state.Vertices.Add(hit.VertexIndex);
                            return !wasSelected || _state.Vertices.Count != 1;
                        }
                    }
                    break;

                case MeshSelectMode.Edge:
                    if (hit.EdgePair.HasValue)
                    {
                        if (additive)
                            return _state.ToggleEdge(hit.EdgePair.Value);
                        else
                        {
                            bool wasSelected = _state.Edges.Contains(hit.EdgePair.Value);
                            _state.Edges.Clear();
                            _state.Edges.Add(hit.EdgePair.Value);
                            return !wasSelected || _state.Edges.Count != 1;
                        }
                    }
                    break;

                case MeshSelectMode.Face:
                    if (hit.FaceIndex >= 0)
                    {
                        if (additive)
                            return _state.ToggleFace(hit.FaceIndex);
                        else
                        {
                            bool wasSelected = _state.Faces.Contains(hit.FaceIndex);
                            _state.Faces.Clear();
                            _state.Faces.Add(hit.FaceIndex);
                            return !wasSelected || _state.Faces.Count != 1;
                        }
                    }
                    break;

                case MeshSelectMode.Line:
                    if (hit.LineIndex >= 0)
                    {
                        if (additive)
                            return _state.ToggleLine(hit.LineIndex);
                        else
                        {
                            bool wasSelected = _state.Lines.Contains(hit.LineIndex);
                            _state.Lines.Clear();
                            _state.Lines.Add(hit.LineIndex);
                            return !wasSelected || _state.Lines.Count != 1;
                        }
                    }
                    break;
            }

            return false;
        }

        /// <summary>
        /// 指定モード以外の選択をクリア
        /// </summary>
        private void ClearOtherModeSelections(MeshSelectMode keepMode)
        {
            if (keepMode != MeshSelectMode.Vertex)
                _state.Vertices.Clear();
            if (keepMode != MeshSelectMode.Edge)
                _state.Edges.Clear();
            if (keepMode != MeshSelectMode.Face)
                _state.Faces.Clear();
            if (keepMode != MeshSelectMode.Line)
                _state.Lines.Clear();
        }

        // === 矩形選択 ===

        /// <summary>
        /// 矩形内の要素を選択（有効な全モード対応）
        /// </summary>
        public bool SelectInRect(
            Rect screenRect,
            MeshObject meshObject,
            Func<Vector3, Vector2> worldToScreen,
            bool additive = false)
        {
            if (meshObject == null || worldToScreen == null) return false;

            bool changed = false;
            var mode = _state.Mode;

            // 有効な各モードで選択
            if (mode.Has(MeshSelectMode.Vertex))
            {
                changed |= SelectVerticesInRect(screenRect, meshObject, worldToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Edge))
            {
                changed |= SelectEdgesInRect(screenRect, meshObject, worldToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Face))
            {
                changed |= SelectFacesInRect(screenRect, meshObject, worldToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Line))
            {
                changed |= SelectLinesInRect(screenRect, meshObject, worldToScreen, additive);
            }

            return changed;
        }

        /// <summary>
        /// v2.1: 矩形内の要素を選択（頂点インデックスからスクリーン座標を取得するバージョン）
        /// ワールドモード時にGPU変換後の座標を使用するため
        /// </summary>
        public bool SelectInRectByIndex(
            Rect screenRect,
            MeshObject meshObject,
            Func<int, Vector2> vertexIndexToScreen,
            bool additive = false)
        {
            if (meshObject == null || vertexIndexToScreen == null) return false;

            bool changed = false;
            var mode = _state.Mode;

            // 頂点選択のみ対応（エッジ・面・ラインは頂点座標から計算可能）
            if (mode.Has(MeshSelectMode.Vertex))
            {
                changed |= SelectVerticesInRectByIndex(screenRect, meshObject, vertexIndexToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Edge))
            {
                changed |= SelectEdgesInRectByIndex(screenRect, meshObject, vertexIndexToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Face))
            {
                changed |= SelectFacesInRectByIndex(screenRect, meshObject, vertexIndexToScreen, additive);
            }
            if (mode.Has(MeshSelectMode.Line))
            {
                changed |= SelectLinesInRectByIndex(screenRect, meshObject, vertexIndexToScreen, additive);
            }

            return changed;
        }

        private bool SelectVerticesInRectByIndex(Rect rect, MeshObject meshObject, Func<int, Vector2> vertexIndexToScreen, bool additive)
        {
            if (!additive) _state.Vertices.Clear();

            bool changed = false;
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                    continue;

                Vector2 sp = vertexIndexToScreen(i);
                if (rect.Contains(sp))
                {
                    if (_state.Vertices.Add(i))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectEdgesInRectByIndex(Rect rect, MeshObject meshObject, Func<int, Vector2> vertexIndexToScreen, bool additive)
        {
            if (!additive) _state.Edges.Clear();

            bool changed = false;
            foreach (var pair in _topology.AllEdgePairs)
            {
                // 可視性チェック：エッジが属する面のいずれかが可視なら可視
                if (_visibilityProvider != null)
                {
                    bool anyVisible = false;
                    foreach (var faceEdge in _topology.GetEdgesAt(pair))
                    {
                        if (_visibilityProvider.IsFaceVisible(faceEdge.FaceIndex))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                    if (!anyVisible) continue;
                }

                Vector2 sp1 = vertexIndexToScreen(pair.V1);
                Vector2 sp2 = vertexIndexToScreen(pair.V2);

                // 両端点が矩形内
                if (rect.Contains(sp1) && rect.Contains(sp2))
                {
                    if (_state.Edges.Add(pair))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectFacesInRectByIndex(Rect rect, MeshObject meshObject, Func<int, Vector2> vertexIndexToScreen, bool additive)
        {
            if (!additive) _state.Faces.Clear();

            bool changed = false;
            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount < 3) continue;  // 面は3頂点以上

                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(faceIdx))
                    continue;

                // 全頂点が矩形内にあるか
                bool allInRect = true;
                foreach (int vIdx in face.VertexIndices)
                {
                    Vector2 sp = vertexIndexToScreen(vIdx);
                    if (!rect.Contains(sp))
                    {
                        allInRect = false;
                        break;
                    }
                }

                if (allInRect)
                {
                    if (_state.Faces.Add(faceIdx))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectLinesInRectByIndex(Rect rect, MeshObject meshObject, Func<int, Vector2> vertexIndexToScreen, bool additive)
        {
            if (!additive) _state.Lines.Clear();

            bool changed = false;
            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount != 2) continue;  // ラインは2頂点

                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(faceIdx))
                    continue;

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                Vector2 sp1 = vertexIndexToScreen(v1);
                Vector2 sp2 = vertexIndexToScreen(v2);

                // 両端点が矩形内
                if (rect.Contains(sp1) && rect.Contains(sp2))
                {
                    if (_state.Lines.Add(faceIdx))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectVerticesInRect(Rect rect, MeshObject meshObject, Func<Vector3, Vector2> worldToScreen, bool additive)
        {
            if (!additive) _state.Vertices.Clear();

            bool changed = false;
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                    continue;

                Vector2 sp = worldToScreen(meshObject.Vertices[i].Position);
                if (rect.Contains(sp))
                {
                    if (_state.Vertices.Add(i))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectEdgesInRect(Rect rect, MeshObject meshObject, Func<Vector3, Vector2> worldToScreen, bool additive)
        {
            if (!additive) _state.Edges.Clear();

            bool changed = false;
            foreach (var pair in _topology.AllEdgePairs)
            {
                // 可視性チェック：エッジが属する面のいずれかが可視なら可視
                if (_visibilityProvider != null)
                {
                    bool anyVisible = false;
                    foreach (var faceEdge in _topology.GetEdgesAt(pair))
                    {
                        if (_visibilityProvider.IsFaceVisible(faceEdge.FaceIndex))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                    if (!anyVisible) continue;
                }

                Vector2 p1 = worldToScreen(meshObject.Vertices[pair.V1].Position);
                Vector2 p2 = worldToScreen(meshObject.Vertices[pair.V2].Position);
                
                if (rect.Contains(p1) && rect.Contains(p2))
                {
                    if (_state.Edges.Add(pair))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectFacesInRect(Rect rect, MeshObject meshObject, Func<Vector3, Vector2> worldToScreen, bool additive)
        {
            if (!additive) _state.Faces.Clear();

            bool changed = false;
            foreach (int faceIdx in _topology.RealFaceIndices)
            {
                // 可視性チェック
                if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(faceIdx))
                    continue;

                var face = meshObject.Faces[faceIdx];
                
                bool allInside = true;
                foreach (int vIdx in face.VertexIndices)
                {
                    Vector2 sp = worldToScreen(meshObject.Vertices[vIdx].Position);
                    if (!rect.Contains(sp))
                    {
                        allInside = false;
                        break;
                    }
                }

                if (allInside)
                {
                    if (_state.Faces.Add(faceIdx))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        private bool SelectLinesInRect(Rect rect, MeshObject meshObject, Func<Vector3, Vector2> worldToScreen, bool additive)
        {
            if (!additive) _state.Lines.Clear();

            bool changed = false;
            foreach (int faceIdx in _topology.AuxLineIndices)
            {
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount != 2) continue;

                Vector2 p1 = worldToScreen(meshObject.Vertices[face.VertexIndices[0]].Position);
                Vector2 p2 = worldToScreen(meshObject.Vertices[face.VertexIndices[1]].Position);
                
                if (rect.Contains(p1) && rect.Contains(p2))
                {
                    if (_state.Lines.Add(faceIdx))
                        changed = true;
                }
            }

            return changed || !additive;
        }

        // === 全選択・反転 ===

        /// <summary>
        /// 有効なモードの全要素を選択（可視要素のみ）
        /// </summary>
        public void SelectAll(MeshObject meshObject)
        {
            if (meshObject == null) return;

            var mode = _state.Mode;

            if (mode.Has(MeshSelectMode.Vertex))
            {
                for (int i = 0; i < meshObject.VertexCount; i++)
                {
                    if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                        continue;
                    _state.Vertices.Add(i);
                }
            }

            if (mode.Has(MeshSelectMode.Edge))
            {
                foreach (var pair in _topology.AllEdgePairs)
                {
                    // 可視性チェック
                    if (_visibilityProvider != null)
                    {
                        bool anyVisible = false;
                        foreach (var faceEdge in _topology.GetEdgesAt(pair))
                        {
                            if (_visibilityProvider.IsFaceVisible(faceEdge.FaceIndex))
                            {
                                anyVisible = true;
                                break;
                            }
                        }
                        if (!anyVisible) continue;
                    }
                    _state.Edges.Add(pair);
                }
            }

            if (mode.Has(MeshSelectMode.Face))
            {
                foreach (int idx in _topology.RealFaceIndices)
                {
                    if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(idx))
                        continue;
                    _state.Faces.Add(idx);
                }
            }

            if (mode.Has(MeshSelectMode.Line))
            {
                // 補助線分は常に可視
                foreach (int idx in _topology.AuxLineIndices)
                    _state.Lines.Add(idx);
            }
        }

        /// <summary>
        /// 有効なモードの選択を反転（可視要素のみ対象）
        /// </summary>
        public void InvertSelection(MeshObject meshObject)
        {
            if (meshObject == null) return;

            var mode = _state.Mode;

            if (mode.Has(MeshSelectMode.Vertex))
            {
                var newSelection = new HashSet<int>();
                for (int i = 0; i < meshObject.VertexCount; i++)
                {
                    if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                        continue;
                    if (!_state.Vertices.Contains(i))
                        newSelection.Add(i);
                }
                _state.Vertices.Clear();
                foreach (int v in newSelection) _state.Vertices.Add(v);
            }

            if (mode.Has(MeshSelectMode.Edge))
            {
                var newSelection = new HashSet<VertexPair>();
                foreach (var pair in _topology.AllEdgePairs)
                {
                    // 可視性チェック
                    if (_visibilityProvider != null)
                    {
                        bool anyVisible = false;
                        foreach (var faceEdge in _topology.GetEdgesAt(pair))
                        {
                            if (_visibilityProvider.IsFaceVisible(faceEdge.FaceIndex))
                            {
                                anyVisible = true;
                                break;
                            }
                        }
                        if (!anyVisible) continue;
                    }
                    if (!_state.Edges.Contains(pair))
                        newSelection.Add(pair);
                }
                _state.Edges.Clear();
                foreach (var e in newSelection) _state.Edges.Add(e);
            }

            if (mode.Has(MeshSelectMode.Face))
            {
                var newSelection = new HashSet<int>();
                foreach (int idx in _topology.RealFaceIndices)
                {
                    if (_visibilityProvider != null && !_visibilityProvider.IsFaceVisible(idx))
                        continue;
                    if (!_state.Faces.Contains(idx))
                        newSelection.Add(idx);
                }
                _state.Faces.Clear();
                foreach (int f in newSelection) _state.Faces.Add(f);
            }

            if (mode.Has(MeshSelectMode.Line))
            {
                // 補助線分は常に可視
                var allLines = new HashSet<int>(_topology.AuxLineIndices);
                allLines.ExceptWith(_state.Lines);
                _state.Lines.Clear();
                foreach (int l in allLines) _state.Lines.Add(l);
            }
        }

        // === ユーティリティ ===

        private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / 
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }
    }

    /// <summary>
    /// ヒットテスト結果
    /// </summary>
    public struct HitResult
    {
        /// <summary>ヒットした要素の種類</summary>
        public MeshSelectMode HitType;
        
        public int VertexIndex;
        public VertexPair? EdgePair;
        public int FaceIndex;
        public int LineIndex;

        /// <summary>
        /// 何かヒットしたか
        /// </summary>
        public bool HasHit => HitType != MeshSelectMode.None;

        /// <summary>
        /// 後方互換: Mode
        /// </summary>
        public MeshSelectMode Mode
        {
            get => HitType;
            set => HitType = value;
        }

        public static HitResult None => new HitResult
        {
            HitType = MeshSelectMode.None,
            VertexIndex = -1,
            EdgePair = null,
            FaceIndex = -1,
            LineIndex = -1
        };
    }
}
