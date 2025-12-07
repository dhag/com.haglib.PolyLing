// Tools/SculptTool.cs
// スカルプトツール - ブラシによるメッシュ変形

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    /// <summary>
    /// スカルプトツールのモード
    /// </summary>
    public enum SculptMode
    {
        /// <summary>盛り上げ/盛り下げ</summary>
        Draw,
        /// <summary>滑らかにする</summary>
        Smooth,
        /// <summary>膨らます</summary>
        Inflate,
        /// <summary>平らにする</summary>
        Flatten
    }

    /// <summary>
    /// スカルプトツール
    /// </summary>
    public class SculptTool : IEditTool
    {
        public string Name => "Sculpt";

        // === 設定 ===
        private SculptMode _mode = SculptMode.Draw;
        private float _brushRadius = 0.5f;
        private float _strength = 0.1f;
        private bool _invert = false;  // 凸凹反転

        // === ドラッグ状態 ===
        private bool _isDragging;
        private Vector2 _currentScreenPos;
        private MeshDataSnapshot _beforeSnapshot;

        // === キャッシュ ===
        private Dictionary<int, HashSet<int>> _adjacencyCache;
        private Dictionary<int, Vector3> _vertexNormalsCache;

        // === 定数 ===
        private const float MIN_BRUSH_RADIUS = 0.05f;
        private const float MAX_BRUSH_RADIUS = 5f;
        private const float MIN_STRENGTH = 0.01f;
        private const float MAX_STRENGTH = 1f;

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Draw", "Smooth", "Inflate", "Flatten" };
        private static readonly SculptMode[] ModeValues = { 
            SculptMode.Draw, 
            SculptMode.Smooth, 
            SculptMode.Inflate,
            SculptMode.Flatten
        };

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            _isDragging = true;
            _currentScreenPos = mousePos;

            // Undo用スナップショット
            _beforeSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // キャッシュを構築
            BuildCaches(ctx.MeshData);

            // 最初のストロークを適用
            ApplyBrush(ctx, mousePos);

            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;
            if (ctx.MeshData == null) return false;

            _currentScreenPos = mousePos;

            // ブラシを適用
            ApplyBrush(ctx, mousePos);

            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            // Undo記録
            if (_beforeSnapshot != null && ctx.UndoController != null)
            {
                var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordTopologyChange(_beforeSnapshot, after, $"Sculpt ({_mode})");
                _beforeSnapshot = null;
            }

            // キャッシュをクリア
            _adjacencyCache = null;
            _vertexNormalsCache = null;

            ctx.Repaint?.Invoke();
            return true;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            Handles.BeginGUI();

            // ブラシ円を描画
            Color brushColor = _invert ? new Color(1f, 0.5f, 0.5f, 0.5f) : new Color(0.5f, 0.8f, 1f, 0.5f);
            Handles.color = brushColor;

            // ブラシサイズをスクリーン座標で近似（中心から少し離れた点で計算）
            Vector2 centerScreen = _currentScreenPos;
            
            // 3Dでのブラシ半径をスクリーンサイズに変換（概算）
            float screenRadius = EstimateBrushScreenRadius(ctx);

            // 円を描画
            DrawCircle(centerScreen, screenRadius, 32);

            // モード表示
            GUI.color = Color.white;
            string modeText = _mode.ToString() + (_invert ? " (Invert)" : "");
            GUI.Label(new Rect(centerScreen.x + screenRadius + 5, centerScreen.y - 10, 100, 20), modeText);

            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Sculpt Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, _mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 2);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                _mode = ModeValues[newIndex];
            }

            EditorGUILayout.Space(5);

            // ブラシサイズ
            _brushRadius = EditorGUILayout.Slider("Brush Size", _brushRadius, MIN_BRUSH_RADIUS, MAX_BRUSH_RADIUS);

            // 強度
            _strength = EditorGUILayout.Slider("Strength", _strength, MIN_STRENGTH, MAX_STRENGTH);

            // 凸凹反転
            _invert = EditorGUILayout.Toggle("Invert", _invert);

            EditorGUILayout.Space(3);

            // モード説明
            switch (_mode)
            {
                case SculptMode.Draw:
                    EditorGUILayout.HelpBox("ドラッグで表面を盛り上げ/盛り下げ", MessageType.Info);
                    break;
                case SculptMode.Smooth:
                    EditorGUILayout.HelpBox("ドラッグで表面を滑らかにする", MessageType.Info);
                    break;
                case SculptMode.Inflate:
                    EditorGUILayout.HelpBox("ドラッグで表面を膨らませる/縮ませる", MessageType.Info);
                    break;
                case SculptMode.Flatten:
                    EditorGUILayout.HelpBox("ドラッグで表面を平らにする", MessageType.Info);
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

        public void Reset()
        {
            _isDragging = false;
            _beforeSnapshot = null;
            _adjacencyCache = null;
            _vertexNormalsCache = null;
        }

        // ================================================================
        // ブラシ適用
        // ================================================================

        private void ApplyBrush(ToolContext ctx, Vector2 mousePos)
        {
            // マウス位置からレイを取得
            Ray ray = ctx.ScreenPosToRay(mousePos);

            // ブラシ中心のワールド座標を計算（メッシュとの交点、または最近接点）
            Vector3 brushCenter = FindBrushCenter(ctx, ray);

            // ブラシ範囲内の頂点を収集
            var affectedVertices = GetVerticesInBrushRadius(ctx.MeshData, brushCenter);
            if (affectedVertices.Count == 0) return;

            // モードに応じて変形
            switch (_mode)
            {
                case SculptMode.Draw:
                    ApplyDraw(ctx.MeshData, affectedVertices, brushCenter, ray.direction);
                    break;
                case SculptMode.Smooth:
                    ApplySmooth(ctx.MeshData, affectedVertices, brushCenter);
                    break;
                case SculptMode.Inflate:
                    ApplyInflate(ctx.MeshData, affectedVertices, brushCenter);
                    break;
                case SculptMode.Flatten:
                    ApplyFlatten(ctx.MeshData, affectedVertices, brushCenter);
                    break;
            }

            // メッシュ更新
            ctx.SyncMesh?.Invoke();
            ctx.Repaint?.Invoke();
        }

        private Vector3 FindBrushCenter(ToolContext ctx, Ray ray)
        {
            // メッシュの各面との交差を試みる
            float closestDist = float.MaxValue;
            Vector3 closestPoint = ray.origin + ray.direction * 5f; // デフォルト

            foreach (var face in ctx.MeshData.Faces)
            {
                if (face.VertexIndices.Count < 3) continue;

                // 三角形に分割してレイキャスト
                for (int i = 1; i < face.VertexIndices.Count - 1; i++)
                {
                    Vector3 v0 = ctx.MeshData.Vertices[face.VertexIndices[0]].Position;
                    Vector3 v1 = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                    Vector3 v2 = ctx.MeshData.Vertices[face.VertexIndices[i + 1]].Position;

                    if (RayTriangleIntersection(ray, v0, v1, v2, out float t))
                    {
                        if (t > 0 && t < closestDist)
                        {
                            closestDist = t;
                            closestPoint = ray.origin + ray.direction * t;
                        }
                    }
                }
            }

            return closestPoint;
        }

        private bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (Mathf.Abs(a) < 1e-6f) return false;

            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0 || u > 1) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);

            if (v < 0 || u + v > 1) return false;

            t = f * Vector3.Dot(edge2, q);
            return t > 1e-6f;
        }

        private List<(int index, float weight)> GetVerticesInBrushRadius(MeshData meshData, Vector3 brushCenter)
        {
            var result = new List<(int, float)>();

            for (int i = 0; i < meshData.VertexCount; i++)
            {
                float dist = Vector3.Distance(meshData.Vertices[i].Position, brushCenter);
                if (dist <= _brushRadius)
                {
                    // フォールオフを計算（中心ほど強く、端ほど弱く）
                    float weight = 1f - (dist / _brushRadius);
                    weight = weight * weight; // 二次曲線で滑らかに
                    result.Add((i, weight));
                }
            }

            return result;
        }

        // ================================================================
        // 各モードの実装
        // ================================================================

        /// <summary>
        /// Draw: 盛り上げ/盛り下げ
        /// </summary>
        private void ApplyDraw(MeshData meshData, List<(int index, float weight)> vertices, Vector3 brushCenter, Vector3 viewDir)
        {
            // ブラシ中心の平均法線を計算
            Vector3 avgNormal = Vector3.zero;
            foreach (var (idx, weight) in vertices)
            {
                if (_vertexNormalsCache.TryGetValue(idx, out var normal))
                {
                    avgNormal += normal * weight;
                }
            }
            avgNormal = avgNormal.normalized;

            // 視線方向と逆向きなら反転
            if (Vector3.Dot(avgNormal, -viewDir) < 0)
            {
                avgNormal = -avgNormal;
            }

            float direction = _invert ? -1f : 1f;

            foreach (var (idx, weight) in vertices)
            {
                Vector3 offset = avgNormal * _strength * weight * direction;
                meshData.Vertices[idx].Position += offset;
            }
        }

        /// <summary>
        /// Smooth: 滑らかにする
        /// </summary>
        private void ApplySmooth(MeshData meshData, List<(int index, float weight)> vertices, Vector3 brushCenter)
        {
            // 各頂点を隣接頂点の平均位置に近づける
            var newPositions = new Dictionary<int, Vector3>();

            foreach (var (idx, weight) in vertices)
            {
                if (_adjacencyCache.TryGetValue(idx, out var neighbors) && neighbors.Count > 0)
                {
                    Vector3 avgPos = Vector3.zero;
                    foreach (int neighbor in neighbors)
                    {
                        avgPos += meshData.Vertices[neighbor].Position;
                    }
                    avgPos /= neighbors.Count;

                    Vector3 currentPos = meshData.Vertices[idx].Position;
                    Vector3 targetPos = Vector3.Lerp(currentPos, avgPos, _strength * weight);
                    newPositions[idx] = targetPos;
                }
            }

            foreach (var kvp in newPositions)
            {
                meshData.Vertices[kvp.Key].Position = kvp.Value;
            }
        }

        /// <summary>
        /// Inflate: 膨らます
        /// </summary>
        private void ApplyInflate(MeshData meshData, List<(int index, float weight)> vertices, Vector3 brushCenter)
        {
            float direction = _invert ? -1f : 1f;

            foreach (var (idx, weight) in vertices)
            {
                if (_vertexNormalsCache.TryGetValue(idx, out var normal))
                {
                    Vector3 offset = normal * _strength * weight * direction;
                    meshData.Vertices[idx].Position += offset;
                }
            }
        }

        /// <summary>
        /// Flatten: 平らにする
        /// </summary>
        private void ApplyFlatten(MeshData meshData, List<(int index, float weight)> vertices, Vector3 brushCenter)
        {
            if (vertices.Count == 0) return;

            // ブラシ範囲内の頂点の平均位置と平均法線を計算
            Vector3 avgPos = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;
            float totalWeight = 0;

            foreach (var (idx, weight) in vertices)
            {
                avgPos += meshData.Vertices[idx].Position * weight;
                totalWeight += weight;

                if (_vertexNormalsCache.TryGetValue(idx, out var normal))
                {
                    avgNormal += normal * weight;
                }
            }

            if (totalWeight > 0)
            {
                avgPos /= totalWeight;
            }
            avgNormal = avgNormal.normalized;

            // 各頂点を平面に投影
            foreach (var (idx, weight) in vertices)
            {
                Vector3 pos = meshData.Vertices[idx].Position;
                
                // 平面への距離
                float distToPlane = Vector3.Dot(pos - avgPos, avgNormal);
                
                // 平面上の位置
                Vector3 projectedPos = pos - avgNormal * distToPlane;
                
                // 補間
                Vector3 targetPos = Vector3.Lerp(pos, projectedPos, _strength * weight);
                meshData.Vertices[idx].Position = targetPos;
            }
        }

        // ================================================================
        // キャッシュ構築
        // ================================================================

        private void BuildCaches(MeshData meshData)
        {
            // 隣接頂点キャッシュ
            _adjacencyCache = new Dictionary<int, HashSet<int>>();
            foreach (var face in meshData.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!_adjacencyCache.ContainsKey(v1)) _adjacencyCache[v1] = new HashSet<int>();
                    if (!_adjacencyCache.ContainsKey(v2)) _adjacencyCache[v2] = new HashSet<int>();

                    _adjacencyCache[v1].Add(v2);
                    _adjacencyCache[v2].Add(v1);
                }
            }

            // 頂点法線キャッシュ
            _vertexNormalsCache = new Dictionary<int, Vector3>();
            var vertexFaceNormals = new Dictionary<int, List<Vector3>>();

            foreach (var face in meshData.Faces)
            {
                if (face.VertexIndices.Count < 3) continue;

                // 面の法線を計算
                Vector3 v0 = meshData.Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = meshData.Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = meshData.Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                foreach (int vIdx in face.VertexIndices)
                {
                    if (!vertexFaceNormals.ContainsKey(vIdx))
                        vertexFaceNormals[vIdx] = new List<Vector3>();
                    vertexFaceNormals[vIdx].Add(faceNormal);
                }
            }

            foreach (var kvp in vertexFaceNormals)
            {
                Vector3 avgNormal = Vector3.zero;
                foreach (var n in kvp.Value)
                {
                    avgNormal += n;
                }
                _vertexNormalsCache[kvp.Key] = avgNormal.normalized;
            }
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        private float EstimateBrushScreenRadius(ToolContext ctx)
        {
            // ブラシ中心付近でのスクリーン半径を概算
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 offsetPoint = testPoint + Vector3.right * _brushRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private void DrawCircle(Vector2 center, float radius, int segments)
        {
            Vector2 prevPoint = center + new Vector2(radius, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Handles.DrawAAPolyLine(2f, prevPoint, point);
                prevPoint = point;
            }
        }
    }
}
