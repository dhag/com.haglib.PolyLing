// Tools/EdgeTopologyTool.cs
// 辺トポロジツール - 辺の編集（入れ替え、分割、結合）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 辺トポロジツールのモード
    /// </summary>
    public enum EdgeTopoMode
    {
        /// <summary>辺の入れ替え（2三角形の対角線切り替え）</summary>
        Flip,
        /// <summary>四角形を対角線で分割</summary>
        Split,
        /// <summary>辺の消去（2面を結合）</summary>
        Dissolve
    }

    /// <summary>
    /// 辺トポロジツール - 辺のトポロジ編集
    /// </summary>
    public class EdgeTopologyTool : IEditTool
    {
        public string Name => "EdgeTopo";
        public string DisplayName => "EdgeTopo";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeTopologySettings _settings = new EdgeTopologySettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private EdgeTopoMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        // === ドラッグ状態 ===
        private bool _isDragging;
        private Vector2 _startScreenPos;
        private Vector2 _currentScreenPos;

        // === 検出結果 ===
        private EdgeInfo? _hoveredEdge;
        private int _hoveredFaceIndex = -1;
        private int _hoveredVertexIndex = -1; // Split用ホバー頂点
        private Vector3? _startWorldPos;      // Split用：開始位置（同位置の複数頂点に対応）
        private int _startVertexIndex = -1;   // Split用：スナップ確定時の開始頂点
        private int _endVertexIndex = -1;     // Split用

        // === 定数 ===
        private const float EDGE_CLICK_THRESHOLD = 10f;  // 辺クリック判定の距離（ピクセル）
        private const float VERTEX_CLICK_THRESHOLD = 15f; // 頂点クリック判定の距離（ピクセル）

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Flip", "Split", "Dissolve" };
        private static readonly EdgeTopoMode[] ModeValues = { EdgeTopoMode.Flip, EdgeTopoMode.Split, EdgeTopoMode.Dissolve };

        /// <summary>
        /// 辺の情報
        /// </summary>
        private struct EdgeInfo
        {
            public int FaceIndex1;      // 辺を持つ面1のインデックス
            public int FaceIndex2;      // 辺を持つ面2のインデックス（-1 = 境界辺）
            public int VertexIndex1;    // 辺の頂点1（グローバルインデックス）
            public int VertexIndex2;    // 辺の頂点2（グローバルインデックス）
            public Vector2 ScreenPos1;  // スクリーン座標1
            public Vector2 ScreenPos2;  // スクリーン座標2
            public bool IsShared => FaceIndex2 >= 0;

            /// <summary>Flip可能か（両面が三角形）</summary>
            public bool CanFlip(MeshData meshData)
            {
                if (!IsShared) return false;
                var face1 = meshData.Faces[FaceIndex1];
                var face2 = meshData.Faces[FaceIndex2];
                return face1.VertexIndices.Count == 3 && face2.VertexIndices.Count == 3;
            }
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            _isDragging = true;
            _startScreenPos = mousePos;
            _currentScreenPos = mousePos;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    // 辺をクリックで即時実行
                    var flipEdge = FindNearestEdge(ctx, mousePos);
                    if (flipEdge.HasValue && flipEdge.Value.IsShared && flipEdge.Value.CanFlip(ctx.MeshData))
                    {
                        ExecuteFlip(ctx, flipEdge.Value);
                    }
                    _isDragging = false;
                    return true;

                case EdgeTopoMode.Dissolve:
                    // 辺をクリックで即時実行
                    var dissolveEdge = FindNearestEdge(ctx, mousePos);
                    if (dissolveEdge.HasValue && dissolveEdge.Value.IsShared)
                    {
                        ExecuteDissolve(ctx, dissolveEdge.Value);
                    }
                    _isDragging = false;
                    return true;

                case EdgeTopoMode.Split:
                    // 距離ベースで最も近い四角形頂点を検索
                    var (faceIdx, vertIdx) = FindNearestQuadVertex(ctx, mousePos, 30f);

                    if (vertIdx >= 0)
                    {
                        _startWorldPos = ctx.MeshData.Vertices[vertIdx].Position;
                        _currentScreenPos = mousePos;
                    }
                    else
                    {
                        _isDragging = false;
                    }
                    return true;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;

            _currentScreenPos = mousePos;

            // Split: DrawSplitPreview内で_endVertexIndexと_hoveredFaceIndexを更新

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            if (Mode == EdgeTopoMode.Split && _startVertexIndex >= 0 && _endVertexIndex >= 0)
            {
                ExecuteSplit(ctx, _hoveredFaceIndex, _startVertexIndex, _endVertexIndex);
            }

            _startWorldPos = null;
            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;

            ctx.Repaint?.Invoke();
            return true;
        }

        public void OnMouseMove(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                case EdgeTopoMode.Dissolve:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);
                    break;

                case EdgeTopoMode.Split:
                    // DrawGizmo内で処理
                    break;
            }

            ctx.Repaint?.Invoke();
        }

        public void DrawOverlay(ToolContext ctx, Rect previewRect)
        {
            if (ctx.MeshData == null) return;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    DrawFlipPreview(ctx);
                    break;

                case EdgeTopoMode.Dissolve:
                    DrawDissolvePreview(ctx);
                    break;

                case EdgeTopoMode.Split:
                    DrawSplitPreview(ctx);
                    break;
            }
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Edge Topology Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, Mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                Mode = ModeValues[newIndex];
                Reset();
            }

            EditorGUILayout.Space(3);

            // モード説明
            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    EditorGUILayout.HelpBox("共有辺をクリックして対角線を入れ替え\n（2つの三角形が必要）", MessageType.Info);
                    break;
                case EdgeTopoMode.Split:
                    EditorGUILayout.HelpBox("四角形の対角頂点をドラッグして分割", MessageType.Info);
                    break;
                case EdgeTopoMode.Dissolve:
                    EditorGUILayout.HelpBox("共有辺をクリックして2つの面を結合", MessageType.Info);
                    break;
            }

            // ステータス表示
            EditorGUILayout.Space(5);
            DrawStatusUI();
        }

        /// <summary>
        /// ステータス表示
        /// </summary>
        private void DrawStatusUI()
        {
            // ステータスはDrawOverlay内で視覚的に表示されるため、
            // ここでは追加の説明のみ表示
        }

        public void Reset()
        {
            _isDragging = false;
            _startWorldPos = null;
            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;
            _hoveredEdge = null;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            var mousePos = Event.current.mousePosition;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);
                    DrawFlipPreview(ctx);
                    break;

                case EdgeTopoMode.Dissolve:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);
                    DrawDissolvePreview(ctx);
                    break;

                case EdgeTopoMode.Split:
                    // ドラッグ中でなければホバー状態を更新
                    if (!_isDragging && _startVertexIndex < 0)
                    {
                        var (_, vert) = FindNearestQuadVertex(ctx, mousePos, 30f);
                        _hoveredVertexIndex = vert;
                    }
                    _currentScreenPos = mousePos;
                    DrawSplitPreview(ctx);
                    break;
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        // ================================================================
        // 辺検出
        // ================================================================

        /// <summary>
        /// マウス位置に最も近い辺を検索
        /// </summary>
        private EdgeInfo? FindNearestEdge(ToolContext ctx, Vector2 mousePos)
        {
            float minDist = EDGE_CLICK_THRESHOLD;
            EdgeInfo? result = null;

            // 全ての辺を走査
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshData);

            foreach (var kvp in edgeToFaces)
            {
                var (v1, v2) = kvp.Key;
                var faces = kvp.Value;

                // スクリーン座標を計算
                Vector3 worldPos1 = ctx.MeshData.Vertices[v1].Position;
                Vector3 worldPos2 = ctx.MeshData.Vertices[v2].Position;
                Vector2 screenPos1 = ctx.WorldToScreenPos(worldPos1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 screenPos2 = ctx.WorldToScreenPos(worldPos2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // マウスと辺の距離
                float dist = DistanceToLineSegment(mousePos, screenPos1, screenPos2);

                if (dist < minDist)
                {
                    minDist = dist;
                    result = new EdgeInfo
                    {
                        FaceIndex1 = faces[0],
                        FaceIndex2 = faces.Count > 1 ? faces[1] : -1,
                        VertexIndex1 = v1,
                        VertexIndex2 = v2,
                        ScreenPos1 = screenPos1,
                        ScreenPos2 = screenPos2
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// 辺→面のマップを構築
        /// </summary>
        private Dictionary<(int, int), List<int>> BuildEdgeToFacesMap(MeshData meshData)
        {
            var map = new Dictionary<(int, int), List<int>>();

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    // 正規化（小さい方を先に）
                    var key = v1 < v2 ? (v1, v2) : (v2, v1);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        /// <summary>
        /// 点と線分の距離
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        // ================================================================
        // Split用: 四角形頂点検出
        // ================================================================

        /// <summary>
        /// 最も近い四角形面の頂点を検索（距離ベース）
        /// 返される頂点は必ずその面に属している
        /// </summary>
        private (int faceIndex, int vertexIndex) FindNearestQuadVertex(ToolContext ctx, Vector2 mousePos, float threshold)
        {
            float minDist = threshold;
            int resultFace = -1;
            int resultVertex = -1;

            for (int f = 0; f < ctx.MeshData.FaceCount; f++)
            {
                var face = ctx.MeshData.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                for (int i = 0; i < 4; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 worldPos = ctx.MeshData.Vertices[vIdx].Position;
                    Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = Vector2.Distance(mousePos, screenPos);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        resultFace = f;
                        resultVertex = vIdx;
                    }
                }
            }

            // 検証：resultVertexがresultFaceに属しているか
            if (resultFace >= 0 && resultVertex >= 0)
            {
                var face = ctx.MeshData.Faces[resultFace];
                if (!face.VertexIndices.Contains(resultVertex))
                {
                    Debug.LogError($"[Split] BUG: vertex {resultVertex} not in face {resultFace}");
                    return (-1, -1);
                }
            }

            return (resultFace, resultVertex);
        }

        /// <summary>
        /// マウス位置が含まれる四角形面を検索
        /// </summary>
        private int FindQuadFaceContainingPoint(ToolContext ctx, Vector2 mousePos)
        {
            for (int f = 0; f < ctx.MeshData.FaceCount; f++)
            {
                var face = ctx.MeshData.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                var screenPositions = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 worldPos = ctx.MeshData.Vertices[vIdx].Position;
                    screenPositions[i] = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                }

                if (IsPointInQuad(mousePos, screenPositions))
                {
                    return f;
                }
            }
            return -1;
        }

        /// <summary>
        /// 四角形面内で最も近い頂点を検索（内外判定を使用）
        /// </summary>
        private int FindNearestVertexInQuad(ToolContext ctx, Vector2 mousePos, out int faceIndex)
        {
            faceIndex = FindQuadFaceContainingPoint(ctx, mousePos);
            if (faceIndex < 0) return -1;

            return FindNearestVertexInFace(ctx, mousePos, faceIndex);
        }

        /// <summary>
        /// 点が四角形の内側にあるか判定（Winding Number法）
        /// </summary>
        private bool IsPointInQuad(Vector2 point, Vector2[] quad)
        {
            float windingNumber = 0;

            for (int i = 0; i < 4; i++)
            {
                Vector2 v1 = quad[i];
                Vector2 v2 = quad[(i + 1) % 4];

                if (v1.y <= point.y)
                {
                    if (v2.y > point.y)
                    {
                        // 上向き交差
                        if (IsLeft(v1, v2, point) > 0)
                            windingNumber++;
                    }
                }
                else
                {
                    if (v2.y <= point.y)
                    {
                        // 下向き交差
                        if (IsLeft(v1, v2, point) < 0)
                            windingNumber--;
                    }
                }
            }

            return windingNumber != 0;
        }

        /// <summary>
        /// 点が線分の左側にあるかを判定（外積の符号）
        /// </summary>
        private float IsLeft(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            return (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
        }

        /// <summary>
        /// 特定の面内で最も近い頂点を検索（閾値なし）
        /// </summary>
        private int FindNearestVertexInFace(ToolContext ctx, Vector2 mousePos, int faceIndex)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return -1;

            var face = ctx.MeshData.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return -1;

            float minDist = float.MaxValue;
            int resultVertex = -1;

            for (int i = 0; i < 4; i++)
            {
                int vIdx = face.VertexIndices[i];
                Vector3 worldPos = ctx.MeshData.Vertices[vIdx].Position;
                Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = Vector2.Distance(mousePos, screenPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    resultVertex = vIdx;
                }
            }

            return resultVertex;
        }

        /// <summary>
        /// 点が三角形の内側にあるか判定
        /// </summary>
        private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = CrossSign(p, a, b);
            float d2 = CrossSign(p, b, c);
            float d3 = CrossSign(p, c, a);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        /// <summary>
        /// 外積の符号を計算
        /// </summary>
        private float CrossSign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        /// <summary>
        /// 対角頂点を検索
        /// </summary>
        private int FindOppositeVertex(ToolContext ctx, int faceIndex, int startVertex, Vector2 mousePos)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return -1;

            var face = ctx.MeshData.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return -1;

            // 開始頂点の位置を探す
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            if (startIdx < 0)
            {
                Debug.LogError($"[Split] FindOpposite: startVertex {startVertex} not in face {faceIndex}, verts=[{string.Join(",", face.VertexIndices)}]");
                return -1;
            }

            // 対角は+2の位置
            int oppositeIdx = (startIdx + 2) % 4;
            int oppositeVertex = face.VertexIndices[oppositeIdx];

            // マウスが対角頂点の近くにあるか確認
            Vector3 worldPos = ctx.MeshData.Vertices[oppositeVertex].Position;
            Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float dist = Vector2.Distance(mousePos, screenPos);

            // 閾値を緩めにする（50px）
            if (dist < 50f)
            {
                return oppositeVertex;
            }

            return -1;
        }

        // ================================================================
        // 操作実行
        // ================================================================

        /// <summary>
        /// Edge Flip実行
        /// </summary>
        private void ExecuteFlip(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.MeshData.Faces[edge.FaceIndex1];
            var face2 = ctx.MeshData.Faces[edge.FaceIndex2];

            // 両方とも三角形でなければ不可
            if (face1.VertexIndices.Count != 3 || face2.VertexIndices.Count != 3)
            {
                Debug.LogWarning("Edge Flip requires two triangles");
                return;
            }

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 共有辺の頂点
            int v1 = edge.VertexIndex1;
            int v2 = edge.VertexIndex2;

            // 共有辺以外の頂点を見つける
            int opposite1 = face1.VertexIndices.First(v => v != v1 && v != v2);
            int opposite2 = face2.VertexIndices.First(v => v != v1 && v != v2);

            // 巻き順を維持するため、元の面の頂点リストで共有辺の頂点を対角頂点に置き換える
            // face1: v2 → opposite2 に置き換え
            // face2: v1 → opposite1 に置き換え
            var newVerts1 = new List<int>(face1.VertexIndices);
            var newVerts2 = new List<int>(face2.VertexIndices);

            int idx1 = newVerts1.IndexOf(v2);
            int idx2 = newVerts2.IndexOf(v1);

            if (idx1 >= 0) newVerts1[idx1] = opposite2;
            if (idx2 >= 0) newVerts2[idx2] = opposite1;

            face1.VertexIndices = newVerts1;
            face2.VertexIndices = newVerts2;

            // UV/Normalインデックスも更新（簡易版: リセット）
            face1.UVIndices = new List<int> { 0, 0, 0 };
            face1.NormalIndices = new List<int> { 0, 0, 0 };
            face2.UVIndices = new List<int> { 0, 0, 0 };
            face2.NormalIndices = new List<int> { 0, 0, 0 };

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Edge Flip");
        }

        /// <summary>
        /// Quad Split実行
        /// </summary>
        private void ExecuteSplit(ToolContext ctx, int faceIndex, int startVertex, int endVertex)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return;

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 頂点の位置を特定
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            int endIdx = face.VertexIndices.IndexOf(endVertex);
            if (startIdx < 0 || endIdx < 0) return;

            // 対角かどうか確認
            if (Math.Abs(startIdx - endIdx) != 2) return;

            // 四角形の頂点（順序通り）
            int v0 = face.VertexIndices[0];
            int v1 = face.VertexIndices[1];
            int v2 = face.VertexIndices[2];
            int v3 = face.VertexIndices[3];

            Face newFace1, newFace2;

            if ((startIdx == 0 && endIdx == 2) || (startIdx == 2 && endIdx == 0))
            {
                // 0-2対角線で分割
                newFace1 = new Face(v0, v1, v2);
                newFace2 = new Face(v0, v2, v3);
            }
            else
            {
                // 1-3対角線で分割
                newFace1 = new Face(v0, v1, v3);
                newFace2 = new Face(v1, v2, v3);
            }

            // UV/Normalインデックス設定
            newFace1.UVIndices = new List<int> { 0, 0, 0 };
            newFace1.NormalIndices = new List<int> { 0, 0, 0 };
            newFace2.UVIndices = new List<int> { 0, 0, 0 };
            newFace2.NormalIndices = new List<int> { 0, 0, 0 };

            // 元の面を置換、新しい面を追加
            ctx.MeshData.Faces[faceIndex] = newFace1;
            ctx.MeshData.Faces.Add(newFace2);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Quad Split");
        }

        /// <summary>
        /// Edge Dissolve実行
        /// </summary>
        private void ExecuteDissolve(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.MeshData.Faces[edge.FaceIndex1];
            var face2 = ctx.MeshData.Faces[edge.FaceIndex2];

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 2つの面を結合した多角形を作成
            var mergedVertices = MergeFaceVertices(face1, face2, edge.VertexIndex1, edge.VertexIndex2);
            if (mergedVertices == null || mergedVertices.Count < 3)
            {
                Debug.LogWarning("Cannot dissolve edge");
                return;
            }

            // 新しい面を作成
            var newFace = new Face
            {
                VertexIndices = mergedVertices,
                UVIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList(),
                NormalIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList()
            };

            // 面を削除（大きいインデックスから）
            int removeFirst = Math.Max(edge.FaceIndex1, edge.FaceIndex2);
            int removeSecond = Math.Min(edge.FaceIndex1, edge.FaceIndex2);

            ctx.MeshData.Faces.RemoveAt(removeFirst);
            ctx.MeshData.Faces.RemoveAt(removeSecond);

            // 新しい面を追加
            ctx.MeshData.Faces.Add(newFace);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Edge Dissolve");
        }

        /// <summary>
        /// 2つの面の頂点を結合（共有辺を除去）
        /// </summary>
        private List<int> MergeFaceVertices(Face face1, Face face2, int sharedV1, int sharedV2)
        {
            var verts1 = face1.VertexIndices;
            var verts2 = face2.VertexIndices;

            // face1から共有辺を除いた頂点列を取得
            // sharedV1 -> sharedV2の辺を見つけ、その間の頂点を除外
            int idx1Start = verts1.IndexOf(sharedV1);
            int idx1End = verts1.IndexOf(sharedV2);

            if (idx1Start < 0 || idx1End < 0) return null;

            // face2でsharedV2 -> sharedV1の順を見つける
            int idx2Start = verts2.IndexOf(sharedV2);
            int idx2End = verts2.IndexOf(sharedV1);

            if (idx2Start < 0 || idx2End < 0) return null;

            var result = new List<int>();

            // face1をsharedV2からsharedV1まで（共有辺を除く）
            int n1 = verts1.Count;
            int current = idx1End;
            while (current != idx1Start)
            {
                result.Add(verts1[current]);
                current = (current + 1) % n1;
            }
            // sharedV1は追加しない（face2で追加される）

            // face2をsharedV1からsharedV2まで（共有辺を除く）
            int n2 = verts2.Count;
            current = idx2End;
            while (current != idx2Start)
            {
                result.Add(verts2[current]);
                current = (current + 1) % n2;
            }
            // sharedV2は追加しない（face1で既に追加済み）

            return result;
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// Flip用プレビュー描画
        /// </summary>
        private void DrawFlipPreview(ToolContext ctx)
        {
            if (!_hoveredEdge.HasValue) return;

            var edge = _hoveredEdge.Value;

            UnityEditor_Handles.BeginGUI();

            // 辺の状態に応じた色を決定
            Color edgeColor;
            float lineWidth;

            if (!edge.IsShared)
            {
                // 境界辺（操作不可）
                edgeColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                lineWidth = 2f;
            }
            else if (edge.CanFlip(ctx.MeshData))
            {
                // Flip可能（緑）
                edgeColor = Color.green;
                lineWidth = 5f;

                // 隣接する2つの三角形をハイライト
                DrawFaceHighlight(ctx, edge.FaceIndex1, new Color(0f, 1f, 0f, 0.2f));
                DrawFaceHighlight(ctx, edge.FaceIndex2, new Color(0f, 1f, 0f, 0.2f));

                // 新しい対角線をプレビュー
                DrawNewDiagonalPreview(ctx, edge);
            }
            else
            {
                // 共有辺だが三角形でない（黄色警告）
                edgeColor = new Color(1f, 0.7f, 0f, 0.8f);
                lineWidth = 3f;
            }

            // 辺を描画
            UnityEditor_Handles.color = edgeColor;
            UnityEditor_Handles.DrawAAPolyLine(lineWidth,
                new Vector3(edge.ScreenPos1.x, edge.ScreenPos1.y, 0),
                new Vector3(edge.ScreenPos2.x, edge.ScreenPos2.y, 0));

            // 端点を描画
            float size = edge.IsShared && edge.CanFlip(ctx.MeshData) ? 8f : 5f;
            UnityEditor_Handles.DrawSolidDisc(new Vector3(edge.ScreenPos1.x, edge.ScreenPos1.y, 0), Vector3.forward, size / 2);
            UnityEditor_Handles.DrawSolidDisc(new Vector3(edge.ScreenPos2.x, edge.ScreenPos2.y, 0), Vector3.forward, size / 2);

            UnityEditor_Handles.EndGUI();
        }

        /// <summary>
        /// Flip後の新しい対角線をプレビュー
        /// </summary>
        private void DrawNewDiagonalPreview(ToolContext ctx, EdgeInfo edge)
        {
            var face1 = ctx.MeshData.Faces[edge.FaceIndex1];
            var face2 = ctx.MeshData.Faces[edge.FaceIndex2];

            // 共有辺以外の頂点を見つける
            int opposite1 = -1, opposite2 = -1;
            foreach (int v in face1.VertexIndices)
            {
                if (v != edge.VertexIndex1 && v != edge.VertexIndex2)
                {
                    opposite1 = v;
                    break;
                }
            }
            foreach (int v in face2.VertexIndices)
            {
                if (v != edge.VertexIndex1 && v != edge.VertexIndex2)
                {
                    opposite2 = v;
                    break;
                }
            }

            if (opposite1 >= 0 && opposite2 >= 0)
            {
                Vector2 sp1 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[opposite1].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[opposite2].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // 新しい対角線を点線で描画
                UnityEditor_Handles.color = new Color(0f, 1f, 1f, 0.8f);
                DrawDashedLine(sp1, sp2, 4f, 8f);
            }
        }

        /// <summary>
        /// Dissolve用プレビュー描画
        /// </summary>
        private void DrawDissolvePreview(ToolContext ctx)
        {
            if (!_hoveredEdge.HasValue) return;

            var edge = _hoveredEdge.Value;

            UnityEditor_Handles.BeginGUI();

            Color edgeColor;
            float lineWidth;

            if (!edge.IsShared)
            {
                // 境界辺（操作不可）
                edgeColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                lineWidth = 2f;
            }
            else
            {
                // Dissolve可能（マゼンタ）
                edgeColor = new Color(1f, 0f, 1f, 1f);
                lineWidth = 5f;

                // 結合される面をハイライト
                DrawFaceHighlight(ctx, edge.FaceIndex1, new Color(1f, 0f, 1f, 0.2f));
                DrawFaceHighlight(ctx, edge.FaceIndex2, new Color(1f, 0f, 1f, 0.2f));
            }

            // 辺を描画
            UnityEditor_Handles.color = edgeColor;
            UnityEditor_Handles.DrawAAPolyLine(lineWidth,
                new Vector3(edge.ScreenPos1.x, edge.ScreenPos1.y, 0),
                new Vector3(edge.ScreenPos2.x, edge.ScreenPos2.y, 0));

            // 端点
            float size = edge.IsShared ? 8f : 5f;
            UnityEditor_Handles.DrawSolidDisc(new Vector3(edge.ScreenPos1.x, edge.ScreenPos1.y, 0), Vector3.forward, size / 2);
            UnityEditor_Handles.DrawSolidDisc(new Vector3(edge.ScreenPos2.x, edge.ScreenPos2.y, 0), Vector3.forward, size / 2);

            UnityEditor_Handles.EndGUI();
        }

        /// <summary>
        /// 面をハイライト描画
        /// </summary>
        private void DrawFaceHighlight(ToolContext ctx, int faceIndex, Color color)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[faceIndex];
            var screenPoints = new Vector3[face.VertexIndices.Count];

            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                var worldPos = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                var sp = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                screenPoints[i] = new Vector3(sp.x, sp.y, 0);
            }

            UnityEditor_Handles.color = color;
            UnityEditor_Handles.DrawAAConvexPolygon(screenPoints);
        }

        /// <summary>
        /// 点線を描画
        /// </summary>
        private void DrawDashedLine(Vector2 start, Vector2 end, float dashLength, float gapLength)
        {
            Vector2 dir = (end - start).normalized;
            float totalLength = Vector2.Distance(start, end);
            float current = 0f;
            bool drawing = true;

            while (current < totalLength)
            {
                float segmentLength = drawing ? dashLength : gapLength;
                float nextPos = Mathf.Min(current + segmentLength, totalLength);

                if (drawing)
                {
                    Vector2 p1 = start + dir * current;
                    Vector2 p2 = start + dir * nextPos;
                    UnityEditor_Handles.DrawAAPolyLine(3f, new Vector3(p1.x, p1.y, 0), new Vector3(p2.x, p2.y, 0));
                }

                current = nextPos;
                drawing = !drawing;
            }
        }

        /// <summary>
        /// Split プレビュー描画
        /// </summary>
        private void DrawSplitPreview(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();

            // 開始位置が選択されている場合
            if (_startWorldPos.HasValue)
            {
                Vector3 startWorldPos = _startWorldPos.Value;
                Vector2 startScreen = ctx.WorldToScreenPos(startWorldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // 開始位置を黄色で表示
                UnityEditor_Handles.color = Color.yellow;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(startScreen.x, startScreen.y, 0), Vector3.forward, 6f);

                // 対角頂点候補を取得（開始位置と同位置の全頂点から）
                var candidates = GetOppositeVertexCandidates(ctx, startWorldPos);

                if (candidates.Count == 0)
                {
                    UnityEditor_Handles.EndGUI();
                    return;
                }

                // 最も近い候補を見つける
                int nearestCandidate = -1;
                int nearestFace = -1;
                int nearestStartVertex = -1;
                float minDist = float.MaxValue;

                foreach (var (faceIdx, oppVertex, startVertex) in candidates)
                {
                    Vector2 oppScreen = ctx.WorldToScreenPos(ctx.MeshData.Vertices[oppVertex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    float dist = Vector2.Distance(_currentScreenPos, oppScreen);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestCandidate = oppVertex;
                        nearestFace = faceIdx;
                        nearestStartVertex = startVertex;
                    }
                }

                // 距離による状態判定
                const float SNAP_THRESHOLD = 20f;   // この距離以内で確定（白）
                const float NEAR_THRESHOLD = 50f;   // この距離以内で接近中（緑）

                bool isSnapped = minDist < SNAP_THRESHOLD;
                bool isNear = minDist < NEAR_THRESHOLD;

                // 候補頂点を描画
                foreach (var (faceIdx, oppVertex, startVertex) in candidates)
                {
                    Vector2 oppScreen = ctx.WorldToScreenPos(ctx.MeshData.Vertices[oppVertex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    if (oppVertex == nearestCandidate && faceIdx == nearestFace)
                    {
                        if (isSnapped)
                        {
                            // スナップ状態：白で大きく
                            UnityEditor_Handles.color = Color.white;
                            UnityEditor_Handles.DrawSolidDisc(new Vector3(oppScreen.x, oppScreen.y, 0), Vector3.forward, 8f);

                            // 対角線を太く
                            UnityEditor_Handles.color = Color.white;
                            UnityEditor_Handles.DrawAAPolyLine(4f, new Vector3(startScreen.x, startScreen.y, 0), new Vector3(oppScreen.x, oppScreen.y, 0));
                        }
                        else if (isNear)
                        {
                            // 接近中：緑
                            UnityEditor_Handles.color = Color.green;
                            UnityEditor_Handles.DrawSolidDisc(new Vector3(oppScreen.x, oppScreen.y, 0), Vector3.forward, 6f);

                            // 対角線プレビュー（細め）
                            UnityEditor_Handles.color = new Color(0f, 1f, 0f, 0.6f);
                            UnityEditor_Handles.DrawAAPolyLine(2f, new Vector3(startScreen.x, startScreen.y, 0), new Vector3(oppScreen.x, oppScreen.y, 0));
                        }
                        else
                        {
                            // 遠い：灰色（線なし）
                            UnityEditor_Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                            UnityEditor_Handles.DrawSolidDisc(new Vector3(oppScreen.x, oppScreen.y, 0), Vector3.forward, 5f);
                        }
                    }
                    else
                    {
                        // その他の候補は小さく灰色
                        UnityEditor_Handles.color = new Color(0.6f, 0.6f, 0.6f, 0.4f);
                        UnityEditor_Handles.DrawSolidDisc(new Vector3(oppScreen.x, oppScreen.y, 0), Vector3.forward, 3f);
                    }
                }

                // ドラッグ中の情報を更新（スナップ時のみ有効）
                if (_isDragging)
                {
                    if (isSnapped)
                    {
                        _startVertexIndex = nearestStartVertex;
                        _endVertexIndex = nearestCandidate;
                        _hoveredFaceIndex = nearestFace;
                    }
                    else
                    {
                        _startVertexIndex = -1;
                        _endVertexIndex = -1;
                        _hoveredFaceIndex = -1;
                    }
                }
            }
            // 開始頂点未選択時：ホバー中の頂点を表示
            else if (_hoveredVertexIndex >= 0)
            {
                Vector2 hoverScreen = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_hoveredVertexIndex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.white;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(hoverScreen.x, hoverScreen.y, 0), Vector3.forward, 7f);
            }

            UnityEditor_Handles.EndGUI();
        }

        /// <summary>
        /// 指定頂点が属する全四角形面の対角頂点を取得
        /// 開始頂点と同じ位置にある頂点は除外
        /// </summary>
        private List<(int faceIndex, int oppositeVertex, int startVertex)> GetOppositeVertexCandidates(ToolContext ctx, Vector3 startWorldPos)
        {
            var result = new List<(int, int, int)>();
            const float POSITION_EPSILON = 0.0001f;

            // 開始位置と同じ位置にある全ての頂点を収集
            var startVertices = new List<int>();
            for (int v = 0; v < ctx.MeshData.Vertices.Count; v++)
            {
                if (Vector3.Distance(ctx.MeshData.Vertices[v].Position, startWorldPos) < POSITION_EPSILON)
                {
                    startVertices.Add(v);
                }
            }

            // 各開始頂点について、属する四角形の対角頂点を収集
            for (int f = 0; f < ctx.MeshData.FaceCount; f++)
            {
                var face = ctx.MeshData.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                foreach (int startVertex in startVertices)
                {
                    int localIdx = face.VertexIndices.IndexOf(startVertex);
                    if (localIdx < 0) continue;

                    // 対角は+2の位置
                    int oppositeIdx = (localIdx + 2) % 4;
                    int oppositeVertex = face.VertexIndices[oppositeIdx];

                    // 対角頂点が開始位置と同じ位置なら除外
                    Vector3 oppWorldPos = ctx.MeshData.Vertices[oppositeVertex].Position;
                    if (Vector3.Distance(startWorldPos, oppWorldPos) < POSITION_EPSILON)
                        continue;

                    result.Add((f, oppositeVertex, startVertex));
                }
            }

            return result;
        }
    }
}