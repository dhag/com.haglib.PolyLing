// Tools/KnifeTool.Belt.cs
// ナイフツール - ベルト探索と連続切断
// 修正版: 切断点をワールド座標で管理し、T値は切断時に再計算

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        // ================================================================
        // 頂点作成（切断点座標から）
        // ================================================================

        /// <summary>
        /// 切断点のワールド座標から頂点を作成（またはキャッシュから取得）
        /// </summary>
        private int GetOrCreateVertexAtPosition(
            ToolContext ctx,
            Vector3 cutPoint,
            (Vector3, Vector3) edgePos,
            Dictionary<Vector3, int> cache,
            List<(int Index, Vertex Vertex)> addedVertices)
        {
            // キャッシュ確認（近い点があれば再利用）
            foreach (var kvp in cache)
            {
                if (Vector3.Distance(kvp.Key, cutPoint) < POSITION_EPSILON)
                    return kvp.Value;
            }

            int newIdx = ctx.MeshData.VertexCount;
            var newVertex = new Vertex(cutPoint);

            // UV/法線補間のため辺の端点を探す
            int v1Idx = -1, v2Idx = -1;
            for (int i = 0; i < ctx.MeshData.VertexCount; i++)
            {
                var pos = ctx.MeshData.Vertices[i].Position;
                if (v1Idx < 0 && Vector3.Distance(pos, edgePos.Item1) < POSITION_EPSILON) v1Idx = i;
                else if (v2Idx < 0 && Vector3.Distance(pos, edgePos.Item2) < POSITION_EPSILON) v2Idx = i;
                if (v1Idx >= 0 && v2Idx >= 0) break;
            }

            if (v1Idx >= 0 && v2Idx >= 0)
            {
                var v1 = ctx.MeshData.Vertices[v1Idx];
                var v2 = ctx.MeshData.Vertices[v2Idx];
                
                // 辺上での比率を計算
                float t = CalculateTFromCutPoint(edgePos, cutPoint);
                
                if (v1.UVs.Count > 0 && v2.UVs.Count > 0)
                    newVertex.UVs.Add(Vector2.Lerp(v1.UVs[0], v2.UVs[0], t));
                if (v1.Normals.Count > 0 && v2.Normals.Count > 0)
                    newVertex.Normals.Add(Vector3.Lerp(v1.Normals[0], v2.Normals[0], t).normalized);
            }

            ctx.MeshData.Vertices.Add(newVertex);
            cache[cutPoint] = newIdx;
            addedVertices.Add((newIdx, newVertex.Clone()));
            return newIdx;
        }

        /// <summary>
        /// 切断点から辺上でのT値を計算（Item1からの比率）
        /// </summary>
        private float CalculateTFromCutPoint((Vector3, Vector3) edgePos, Vector3 cutPoint)
        {
            var edgeVec = edgePos.Item2 - edgePos.Item1;
            float edgeLen = edgeVec.magnitude;
            if (edgeLen < POSITION_EPSILON) return 0.5f;
            
            var toPoint = cutPoint - edgePos.Item1;
            float t = Vector3.Dot(toPoint, edgeVec) / (edgeLen * edgeLen);
            return Mathf.Clamp01(t);
        }

        /// <summary>
        /// 面内での辺の向きを考慮したT値を計算
        /// </summary>
        private float CalculateFaceLocalT(ToolContext ctx, int faceIdx, int edgeLocalIdx, Vector3 cutPoint)
        {
            var face = ctx.MeshData.Faces[faceIdx];
            int v1 = face.VertexIndices[edgeLocalIdx];
            int v2 = face.VertexIndices[(edgeLocalIdx + 1) % face.VertexIndices.Count];
            var p1 = ctx.MeshData.Vertices[v1].Position;
            var p2 = ctx.MeshData.Vertices[v2].Position;
            
            var edgeVec = p2 - p1;
            float edgeLen = edgeVec.magnitude;
            if (edgeLen < POSITION_EPSILON) return 0.5f;
            
            var toPoint = cutPoint - p1;
            float t = Vector3.Dot(toPoint, edgeVec) / (edgeLen * edgeLen);
            return Mathf.Clamp(t, 0.01f, 0.99f);
        }

        // ================================================================
        // Auto Chain切断
        // ================================================================

        private void ExecuteAutoDragChainCut(ToolContext ctx)
        {
            if (_targetFaceIndex < 0 || _intersections.Count < 2) return;

            var meshData = ctx.MeshData;
            var inter0 = _intersections[0];
            var inter1 = _intersections[1];
            var face0 = meshData.Faces[_targetFaceIndex];

            // 最初の面の2辺と切断点を取得
            int e0v1 = face0.VertexIndices[inter0.EdgeStartIndex];
            int e0v2 = face0.VertexIndices[inter0.EdgeEndIndex];
            var edge0Pos = NormalizeEdgeWorldPos(meshData.Vertices[e0v1].Position, meshData.Vertices[e0v2].Position);
            var cutPoint0 = inter0.WorldPos;

            int e1v1 = face0.VertexIndices[inter1.EdgeStartIndex];
            int e1v2 = face0.VertexIndices[inter1.EdgeEndIndex];
            var edge1Pos = NormalizeEdgeWorldPos(meshData.Vertices[e1v1].Position, meshData.Vertices[e1v2].Position);
            var cutPoint1 = inter1.WorldPos;

            // beltInfo: (faceIdx, edgePos0, edgePos1, cutPoint0, cutPoint1)
            var beltInfo = new List<(int faceIdx, (Vector3, Vector3) edgePos0, (Vector3, Vector3) edgePos1, Vector3 cutPoint0, Vector3 cutPoint1)>();
            beltInfo.Add((_targetFaceIndex, edge0Pos, edge1Pos, cutPoint0, cutPoint1));

            var visitedFaces = new HashSet<int> { _targetFaceIndex };

            // 両方向に探索
            ExploreBeltByWorldPos(ctx, edge0Pos, cutPoint0, edge1Pos, visitedFaces, beltInfo);
            ExploreBeltByWorldPos(ctx, edge1Pos, cutPoint1, edge0Pos, visitedFaces, beltInfo);

            if (beltInfo.Count == 0) return;

            ExecuteBeltCut(ctx, beltInfo);
        }

        private void ExecuteEdgeSelectAutoChainCut(
            ToolContext ctx,
            (Vector3, Vector3) edge1Pos,
            (Vector3, Vector3) edge2Pos,
            float cutRatio1,
            float cutRatio2)
        {
            if (_targetFaceIndex < 0) return;

            // クリック点のワールド座標を計算
            var cutPoint1 = Vector3.Lerp(edge1Pos.Item1, edge1Pos.Item2, cutRatio1);
            var cutPoint2 = Vector3.Lerp(edge2Pos.Item1, edge2Pos.Item2, cutRatio2);

            // スクリーン座標（直線探索用）
            var screenStart = ctx.WorldToScreenPos(cutPoint1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            var screenEnd = ctx.WorldToScreenPos(cutPoint2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            var beltInfo = new List<(int faceIdx, (Vector3, Vector3) edgePos0, (Vector3, Vector3) edgePos1, Vector3 cutPoint0, Vector3 cutPoint1)>();
            beltInfo.Add((_targetFaceIndex, edge1Pos, edge2Pos, cutPoint1, cutPoint2));

            var visitedFaces = new HashSet<int> { _targetFaceIndex };

            // 両方向に探索（直線との交点で切断位置を決定）
            ExploreBeltWithLine(ctx, edge1Pos, cutPoint1, edge2Pos, screenStart, screenEnd, visitedFaces, beltInfo);
            ExploreBeltWithLine(ctx, edge2Pos, cutPoint2, edge1Pos, screenEnd, screenStart, visitedFaces, beltInfo);

            if (beltInfo.Count == 0) return;

            ExecuteBeltCut(ctx, beltInfo);
        }

        // ================================================================
        // ベルト探索（ワールド座標ベース）
        // ================================================================

        /// <summary>
        /// ドラッグモード用: 同じ比率で探索
        /// </summary>
        private void ExploreBeltByWorldPos(
            ToolContext ctx,
            (Vector3, Vector3) fromEdgePos,
            Vector3 fromCutPoint,
            (Vector3, Vector3) excludeEdgePos,
            HashSet<int> visitedFaces,
            List<(int faceIdx, (Vector3, Vector3) edgePos0, (Vector3, Vector3) edgePos1, Vector3 cutPoint0, Vector3 cutPoint1)> beltInfo)
        {
            var meshData = ctx.MeshData;
            var currentEdgePos = fromEdgePos;
            var currentCutPoint = fromCutPoint;

            for (int iter = 0; iter < 100; iter++)
            {
                var facesWithEdge = FindFacesWithEdgePosition(ctx, currentEdgePos);
                bool foundNext = false;

                foreach (var (faceIdx, edgeLocalIdx) in facesWithEdge)
                {
                    if (visitedFaces.Contains(faceIdx)) continue;
                    var face = meshData.Faces[faceIdx];
                    int n = face.VertexIndices.Count;

                    // 三角形は終端
                    if (n == 3) break;
                    if (n != 4) continue;

                    // 対向辺を取得
                    int oppLocalIdx = (edgeLocalIdx + 2) % n;
                    int oppV1 = face.VertexIndices[oppLocalIdx];
                    int oppV2 = face.VertexIndices[(oppLocalIdx + 1) % n];
                    var oppP1 = meshData.Vertices[oppV1].Position;
                    var oppP2 = meshData.Vertices[oppV2].Position;
                    var oppEdgePos = NormalizeEdgeWorldPos(oppP1, oppP2);

                    if (IsSameEdgePosition(oppEdgePos, excludeEdgePos)) continue;

                    // 対向辺上の切断点を計算（前の切断点との対応関係から）
                    var oppCutPoint = CalculateCorrespondingCutPoint(
                        currentEdgePos, currentCutPoint,
                        oppEdgePos, face, edgeLocalIdx, oppLocalIdx, meshData);

                    visitedFaces.Add(faceIdx);
                    beltInfo.Add((faceIdx, currentEdgePos, oppEdgePos, currentCutPoint, oppCutPoint));
                    
                    currentEdgePos = oppEdgePos;
                    currentCutPoint = oppCutPoint;
                    foundNext = true;
                    break;
                }

                if (!foundNext) break;
            }
        }

        /// <summary>
        /// EdgeSelectモード用: 直線との交点で探索
        /// </summary>
        private void ExploreBeltWithLine(
            ToolContext ctx,
            (Vector3, Vector3) fromEdgePos,
            Vector3 fromCutPoint,
            (Vector3, Vector3) excludeEdgePos,
            Vector2 lineStart,
            Vector2 lineEnd,
            HashSet<int> visitedFaces,
            List<(int faceIdx, (Vector3, Vector3) edgePos0, (Vector3, Vector3) edgePos1, Vector3 cutPoint0, Vector3 cutPoint1)> beltInfo)
        {
            var meshData = ctx.MeshData;
            var currentEdgePos = fromEdgePos;
            var currentCutPoint = fromCutPoint;

            for (int iter = 0; iter < 100; iter++)
            {
                var facesWithEdge = FindFacesWithEdgePosition(ctx, currentEdgePos);
                bool foundNext = false;

                foreach (var (faceIdx, edgeLocalIdx) in facesWithEdge)
                {
                    if (visitedFaces.Contains(faceIdx)) continue;
                    var face = meshData.Faces[faceIdx];
                    int n = face.VertexIndices.Count;

                    if (n == 3) break;
                    if (n != 4) continue;

                    int oppLocalIdx = (edgeLocalIdx + 2) % n;
                    int oppV1 = face.VertexIndices[oppLocalIdx];
                    int oppV2 = face.VertexIndices[(oppLocalIdx + 1) % n];
                    var oppP1 = meshData.Vertices[oppV1].Position;
                    var oppP2 = meshData.Vertices[oppV2].Position;
                    var oppEdgePos = NormalizeEdgeWorldPos(oppP1, oppP2);

                    if (IsSameEdgePosition(oppEdgePos, excludeEdgePos)) continue;

                    // 対向辺のスクリーン座標
                    var oppSp1 = ctx.WorldToScreenPos(oppP1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    var oppSp2 = ctx.WorldToScreenPos(oppP2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    // 直線との交点を計算
                    Vector3 oppCutPoint;
                    if (LineIntersectsSegment(lineStart, lineEnd, oppSp1, oppSp2, out float screenT))
                    {
                        // スクリーン上の交点比率からワールド座標を計算
                        screenT = Mathf.Clamp(screenT, 0.05f, 0.95f);
                        oppCutPoint = Vector3.Lerp(oppP1, oppP2, screenT);
                    }
                    else
                    {
                        // 交差しない場合は対応点を計算
                        oppCutPoint = CalculateCorrespondingCutPoint(
                            currentEdgePos, currentCutPoint,
                            oppEdgePos, face, edgeLocalIdx, oppLocalIdx, meshData);
                    }

                    visitedFaces.Add(faceIdx);
                    beltInfo.Add((faceIdx, currentEdgePos, oppEdgePos, currentCutPoint, oppCutPoint));
                    
                    currentEdgePos = oppEdgePos;
                    currentCutPoint = oppCutPoint;
                    foundNext = true;
                    break;
                }

                if (!foundNext) break;
            }
        }

        /// <summary>
        /// 四角形面での対応する切断点を計算
        /// 前の辺の切断点と対向辺の位置関係から、対向辺上の対応点を求める
        /// </summary>
        private Vector3 CalculateCorrespondingCutPoint(
            (Vector3, Vector3) prevEdgePos,
            Vector3 prevCutPoint,
            (Vector3, Vector3) oppEdgePos,
            Face face,
            int prevEdgeLocalIdx,
            int oppEdgeLocalIdx,
            MeshData meshData)
        {
            // 前の辺での比率を計算（面内の辺の向きで）
            int pv1 = face.VertexIndices[prevEdgeLocalIdx];
            int pv2 = face.VertexIndices[(prevEdgeLocalIdx + 1) % face.VertexIndices.Count];
            var pp1 = meshData.Vertices[pv1].Position;
            var pp2 = meshData.Vertices[pv2].Position;
            
            var prevVec = pp2 - pp1;
            float prevLen = prevVec.magnitude;
            float prevT = 0.5f;
            if (prevLen > POSITION_EPSILON)
            {
                prevT = Vector3.Dot(prevCutPoint - pp1, prevVec) / (prevLen * prevLen);
                prevT = Mathf.Clamp(prevT, 0.05f, 0.95f);
            }

            // 対向辺は逆向き（四角形の場合）なので、1-Tの位置が対応点
            int ov1 = face.VertexIndices[oppEdgeLocalIdx];
            int ov2 = face.VertexIndices[(oppEdgeLocalIdx + 1) % face.VertexIndices.Count];
            var op1 = meshData.Vertices[ov1].Position;
            var op2 = meshData.Vertices[ov2].Position;
            
            float oppT = 1f - prevT;
            return Vector3.Lerp(op1, op2, oppT);
        }

        // ================================================================
        // ベルト切断実行
        // ================================================================

        private void ExecuteBeltCut(
            ToolContext ctx,
            List<(int faceIdx, (Vector3, Vector3) edgePos0, (Vector3, Vector3) edgePos1, Vector3 cutPoint0, Vector3 cutPoint1)> beltInfo)
        {
            // Undo用スナップショット（切断前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            var meshData = ctx.MeshData;
            var vertexCache = new Dictionary<Vector3, int>();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            // 面インデックスの大きい順に処理（インデックスずれ防止）
            var sortedBelt = beltInfo.OrderByDescending(b => b.faceIdx).ToList();

            foreach (var (faceIdx, edgePos0, edgePos1, cutPoint0, cutPoint1) in sortedBelt)
            {
                if (faceIdx < 0 || faceIdx >= meshData.FaceCount) continue;
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                // 辺のローカルインデックスを探す
                int edge0LocalIdx = -1, edge1LocalIdx = -1;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var p1 = meshData.Vertices[v1].Position;
                    var p2 = meshData.Vertices[v2].Position;
                    var edgePos = NormalizeEdgeWorldPos(p1, p2);
                    
                    if (IsSameEdgePosition(edgePos, edgePos0)) edge0LocalIdx = i;
                    if (IsSameEdgePosition(edgePos, edgePos1)) edge1LocalIdx = i;
                }

                if (edge0LocalIdx < 0 || edge1LocalIdx < 0) continue;
                if (edge0LocalIdx == edge1LocalIdx) continue;

                // 頂点を作成
                int newVIdx0 = GetOrCreateVertexAtPosition(ctx, cutPoint0, edgePos0, vertexCache, addedVertices);
                int newVIdx1 = GetOrCreateVertexAtPosition(ctx, cutPoint1, edgePos1, vertexCache, addedVertices);

                // 面内でのT値を計算
                float t0 = CalculateFaceLocalT(ctx, faceIdx, edge0LocalIdx, cutPoint0);
                float t1 = CalculateFaceLocalT(ctx, faceIdx, edge1LocalIdx, cutPoint1);

                var inter0 = new EdgeIntersection 
                { 
                    EdgeStartIndex = edge0LocalIdx, 
                    EdgeEndIndex = (edge0LocalIdx + 1) % n, 
                    T = t0, 
                    WorldPos = cutPoint0 
                };
                var inter1 = new EdgeIntersection 
                { 
                    EdgeStartIndex = edge1LocalIdx, 
                    EdgeEndIndex = (edge1LocalIdx + 1) % n, 
                    T = t1, 
                    WorldPos = cutPoint1 
                };

                var (face1, face2) = SplitFace(face, inter0, inter1, newVIdx0, newVIdx1);
                meshData.Faces[faceIdx] = face1;
                meshData.Faces.Add(face2);
            }

            ctx.SyncMesh?.Invoke();

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Knife Belt Cut");
            }
        }

        // ================================================================
        // 後方互換性のため残す（古いシグネチャ）
        // ================================================================

        private int GetOrCreateEdgeVertexByPosition(
            ToolContext ctx,
            (Vector3, Vector3) edgePos,
            Dictionary<(Vector3, Vector3), int> cache,
            List<(int Index, Vertex Vertex)> addedVertices,
            float cutRatio)
        {
            var cutPoint = Vector3.Lerp(edgePos.Item1, edgePos.Item2, cutRatio);
            
            // 新しいキャッシュに変換
            var posCache = new Dictionary<Vector3, int>();
            foreach (var kvp in cache)
            {
                var midPoint = Vector3.Lerp(kvp.Key.Item1, kvp.Key.Item2, 0.5f);
                if (!posCache.ContainsKey(midPoint))
                    posCache[midPoint] = kvp.Value;
            }
            
            int result = GetOrCreateVertexAtPosition(ctx, cutPoint, edgePos, posCache, addedVertices);
            cache[edgePos] = result;
            return result;
        }
    }
}
