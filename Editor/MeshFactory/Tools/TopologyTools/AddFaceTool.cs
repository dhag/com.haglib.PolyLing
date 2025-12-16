// Tools/AddFaceTool.cs
// 面追加ツール（2点=Line、3点=Triangle、4点=Quad）
// マルチマテリアル対応 + Line描画対応

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 面の追加モード
    /// </summary>
    public enum AddFaceMode
    {
        Line = 2,       // 2点（補助線）
        Triangle = 3,   // 3点（三角形）
        Quad = 4        // 4点（四角形）
    }

    /// <summary>
    /// 点の情報（既存頂点 or 新規作成点）
    /// </summary>
    public struct PointInfo
    {
        public bool IsExistingVertex;
        public int ExistingVertexIndex;
        public Vector3 Position;

        public static PointInfo FromExisting(int vertexIndex, Vector3 position)
        {
            return new PointInfo
            {
                IsExistingVertex = true,
                ExistingVertexIndex = vertexIndex,
                Position = position
            };
        }

        public static PointInfo FromNew(Vector3 position)
        {
            return new PointInfo
            {
                IsExistingVertex = false,
                ExistingVertexIndex = -1,
                Position = position
            };
        }
    }

    /// <summary>
    /// 面追加ツール
    /// </summary>
    public class AddFaceTool : IEditTool
    {
        public string Name => "Add Face";
        public string DisplayName => "Add Face";
        //public ToolCategory Category => ToolCategory.Topology;  

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private AddFaceSettings _settings = new AddFaceSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private AddFaceMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        private float DefaultDistance
        {
            get => _settings.DefaultDistance;
            set => _settings.DefaultDistance = value;
        }

        private bool ContinuousLine
        {
            get => _settings.ContinuousLine;
            set => _settings.ContinuousLine = value;
        }

        // === 状態 ===
        private List<PointInfo> _points = new List<PointInfo>();
        private PointInfo? _lastLinePoint = null;  // 連続線分の最後の点
        private Vector3 _previewPoint;          // 現在のマウス位置での候補点
        private bool _previewValid = false;
        private int _previewHitVertex = -1;     // プレビュー時に既存頂点にヒットしている場合

        // === モード名 ===
        private static readonly string[] ModeNames = { "Line (2)", "Triangle (3)", "Quad (4)" };
        private static readonly AddFaceMode[] ModeValues = { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };

        public int RequiredPoints => (int)Mode;

        // === IEditTool実装 ===

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // 右クリックは点を1つ戻す
            if (Event.current.button == 1)
            {
                if (_points.Count > 0)
                {
                    _points.RemoveAt(_points.Count - 1);
                    ctx.Repaint?.Invoke();
                }
                else if (_lastLinePoint.HasValue)
                {
                    // 連続線分モードの開始点もクリア
                    _lastLinePoint = null;
                    ctx.Repaint?.Invoke();
                }
                return true;
            }

            // 左クリックのみ処理
            if (Event.current.button != 0)
                return false;

            // 点を追加
            PointInfo point = GetPointAtScreenPos(ctx, mousePos);

            // デバッグログ
            if (point.IsExistingVertex)
            {
                Debug.Log($"[AddFaceTool] Point added: Existing vertex V{point.ExistingVertexIndex} at {point.Position}");
            }
            else
            {
                Debug.Log($"[AddFaceTool] Point added: NEW vertex at {point.Position}");
            }

            // 連続線分モードの場合
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue)
            {
                // 前回の最後の点と今回の点で線分を作成
                _points.Clear();
                _points.Add(_lastLinePoint.Value);
                _points.Add(point);
                var createdIndices = CreateFace(ctx);

                // 作成された頂点インデックスで_lastLinePointを更新
                if (createdIndices.Count >= 2)
                {
                    int lastIdx = createdIndices[1];
                    Vector3 lastPos = ctx.MeshData.Vertices[lastIdx].Position;
                    _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                    Debug.Log($"[AddFaceTool] Continuous line: next start = V{lastIdx}");
                }

                _points.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }

            _points.Add(point);

            // 必要な点数に達したら面を作成
            if (_points.Count >= RequiredPoints)
            {
                var createdIndices = CreateFace(ctx);

                // 連続線分モードの場合、最後の頂点インデックスを保存
                if (Mode == AddFaceMode.Line && ContinuousLine && createdIndices.Count >= 2)
                {
                    int lastIdx = createdIndices[createdIndices.Count - 1];
                    Vector3 lastPos = ctx.MeshData.Vertices[lastIdx].Position;
                    _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                }

                _points.Clear();
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            // プレビュー更新
            UpdatePreview(ctx, mousePos);
            ctx.Repaint?.Invoke();
            return false;  // ドラッグは他の処理に委譲
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            UnityEditor_Handles.BeginGUI();

            // 連続線分モードの開始点を描画
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue && _points.Count == 0)
            {
                Vector2 startScreen = ctx.WorldToScreenPos(
                    _lastLinePoint.Value.Position, ctx.PreviewRect,
                    ctx.CameraPosition, ctx.CameraTarget);

                // 開始点（オレンジ）
                Color startColor = new Color(1f, 0.5f, 0f, 0.9f);
                float size = 12f;

                UnityEditor_Handles.DrawRect(new Rect(
                    startScreen.x - size / 2,
                    startScreen.y - size / 2,
                    size, size), startColor);

                GUI.Label(new Rect(startScreen.x + 10, startScreen.y - 8, 50, 16),
                    "START", EditorStyles.whiteBoldLabel);

                // プレビュー点への線
                if (_previewValid)
                {
                    Vector2 previewScreen = ctx.WorldToScreenPos(
                        _previewPoint, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 0.5f);
                    UnityEditor_Handles.DrawAAPolyLine(2f,
                        new Vector3(startScreen.x, startScreen.y, 0),
                        new Vector3(previewScreen.x, previewScreen.y, 0));
                }
            }

            // 確定済みの点を描画
            for (int i = 0; i < _points.Count; i++)
            {
                Vector2 screenPos = ctx.WorldToScreenPos(
                    _points[i].Position, ctx.PreviewRect,
                    ctx.CameraPosition, ctx.CameraTarget);

                // 点の色（既存=シアン、新規=黄色）
                Color pointColor = _points[i].IsExistingVertex
                    ? new Color(0f, 1f, 1f, 0.9f)  // シアン
                    : new Color(1f, 1f, 0f, 0.9f); // 黄色

                // 点を描画
                float size = 10f;

                UnityEditor_Handles.DrawRect(new Rect(
                    screenPos.x - size / 2,
                    screenPos.y - size / 2,
                    size, size), pointColor);

            

                // 番号を表示
                GUI.Label(new Rect(screenPos.x + 8, screenPos.y - 8, 20, 16),
                    (i + 1).ToString(), EditorStyles.whiteBoldLabel);

                // 前の点との線を描画
                if (i > 0)
                {
                    Vector2 prevScreen = ctx.WorldToScreenPos(
                        _points[i - 1].Position, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    UnityEditor_Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                    UnityEditor_Handles.DrawAAPolyLine(2f,
                        new Vector3(prevScreen.x, prevScreen.y, 0),
                        new Vector3(screenPos.x, screenPos.y, 0));
                }
            }

            // プレビュー点を描画
            if (_previewValid && _points.Count < RequiredPoints)
            {
                Vector2 previewScreen = ctx.WorldToScreenPos(
                    _previewPoint, ctx.PreviewRect,
                    ctx.CameraPosition, ctx.CameraTarget);

                // プレビュー点の色（既存頂点=シアン、新規=黄色半透明）
                Color previewColor = _previewHitVertex >= 0
                    ? new Color(0f, 1f, 1f, 0.9f)    // シアン（スナップ時は不透明）
                    : new Color(1f, 1f, 0f, 0.5f);   // 黄色半透明

                float size = _previewHitVertex >= 0 ? 14f : 10f;  // スナップ時は大きく
                UnityEditor_Handles.DrawRect(new Rect(
                    previewScreen.x - size / 2,
                    previewScreen.y - size / 2,
                    size, size), previewColor);
                // スナップ時はラベル表示
                if (_previewHitVertex >= 0)
                {
                    // スナップインジケーター（円）
                    UnityEditor_Handles.color = new Color(0f, 1f, 1f, 0.6f);
                    UnityEditor_Handles.DrawWireDisc(
                        new Vector3(previewScreen.x, previewScreen.y, 0),
                        Vector3.forward, 12f);

                    GUI.Label(new Rect(previewScreen.x + 10, previewScreen.y - 8, 60, 16),
                        $"V{_previewHitVertex}", EditorStyles.whiteBoldLabel);
                }

                // 最後の確定点からプレビュー点への線
                if (_points.Count > 0)
                {
                    Vector2 lastScreen = ctx.WorldToScreenPos(
                        _points[_points.Count - 1].Position, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    UnityEditor_Handles.color = new Color(1f, 0.8f, 0.2f, 0.4f);
                    UnityEditor_Handles.DrawAAPolyLine(2f,
                        new Vector3(lastScreen.x, lastScreen.y, 0),
                        new Vector3(previewScreen.x, previewScreen.y, 0));
                }

                // 閉じる線（最後の点から最初の点へ）のプレビュー（三角形/四角形の場合のみ）
                if (Mode != AddFaceMode.Line && _points.Count == RequiredPoints - 1 && _points.Count >= 2)
                {
                    Vector2 firstScreen = ctx.WorldToScreenPos(
                        _points[0].Position, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    UnityEditor_Handles.color = new Color(1f, 0.8f, 0.2f, 0.3f);
                    UnityEditor_Handles.DrawAAPolyLine(1f,
                        new Vector3(previewScreen.x, previewScreen.y, 0),
                        new Vector3(firstScreen.x, firstScreen.y, 0));
                }
            }

            UnityEditor_Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            // モード選択
            EditorGUILayout.LabelField("Face Type", EditorStyles.miniBoldLabel);
            int currentIndex = System.Array.IndexOf(ModeValues, Mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                Mode = ModeValues[newIndex];
                _points.Clear();  // モード変更時はリセット
                _lastLinePoint = null;
            }

            // Lineモード専用オプション
            if (Mode == AddFaceMode.Line)
            {
                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                ContinuousLine = EditorGUILayout.Toggle("Continuous Line", ContinuousLine);
                if (EditorGUI.EndChangeCheck())
                {
                    _lastLinePoint = null;  // モード変更時はリセット
                }

                if (ContinuousLine && _lastLinePoint.HasValue)
                {
                    EditorGUILayout.LabelField("  ↳ Click to continue from last point", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // 進捗表示
            string progressText = $"Points: {_points.Count} / {RequiredPoints}";
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue)
            {
                progressText = "Click to add next line segment";
            }
            EditorGUILayout.LabelField(progressText, EditorStyles.miniLabel);

            // 配置済み点の座標表示
            if (_points.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Placed Points:", EditorStyles.miniBoldLabel);

                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    string label = p.IsExistingVertex
                        ? $"  {i + 1}: V{p.ExistingVertexIndex}"
                        : $"  {i + 1}: NEW";
                    EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // クリアボタン
            bool hasData = _points.Count > 0 || (ContinuousLine && _lastLinePoint.HasValue);
            if (hasData)
            {
                if (GUILayout.Button("Clear Points"))
                {
                    _points.Clear();
                    _lastLinePoint = null;
                }
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            _points.Clear();
            _previewValid = false;
            _lastLinePoint = null;

            // 選択された頂点を最初の点として使用
            if (ctx.SelectedVertices != null && ctx.SelectedVertices.Count > 0 && ctx.MeshData != null)
            {
                var selectedList = new List<int>(ctx.SelectedVertices);

                // Lineモードで2頂点選択されていれば即座に線分作成
                if (Mode == AddFaceMode.Line && selectedList.Count == 2)
                {
                    foreach (int idx in selectedList)
                    {
                        if (idx >= 0 && idx < ctx.MeshData.VertexCount)
                        {
                            Vector3 pos = ctx.MeshData.Vertices[idx].Position;
                            _points.Add(PointInfo.FromExisting(idx, pos));
                        }
                    }

                    if (_points.Count == 2)
                    {
                        var createdIndices = CreateFace(ctx);

                        // 連続モードなら最後の点を保持
                        if (ContinuousLine && createdIndices.Count >= 2)
                        {
                            int lastIdx = createdIndices[1];
                            Vector3 lastPos = ctx.MeshData.Vertices[lastIdx].Position;
                            _lastLinePoint = PointInfo.FromExisting(lastIdx, lastPos);
                        }
                        _points.Clear();
                    }
                    return;
                }

                // 1頂点選択の場合、それを最初の点として使用
                if (selectedList.Count == 1)
                {
                    int selectedIdx = selectedList[0];
                    if (selectedIdx >= 0 && selectedIdx < ctx.MeshData.VertexCount)
                    {
                        Vector3 pos = ctx.MeshData.Vertices[selectedIdx].Position;
                        var startPoint = PointInfo.FromExisting(selectedIdx, pos);

                        if (Mode == AddFaceMode.Line && ContinuousLine)
                        {
                            // 連続線分モードの場合は開始点として設定
                            _lastLinePoint = startPoint;
                        }
                        else
                        {
                            // 通常モードの場合は最初の点として追加
                            _points.Add(startPoint);
                        }
                    }
                }
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _points.Clear();
            _previewValid = false;
            _lastLinePoint = null;
        }

        public void Reset()
        {
            _points.Clear();
            _previewValid = false;
            _previewHitVertex = -1;
            _lastLinePoint = null;
        }

        // === 内部メソッド ===

        /// <summary>
        /// スクリーン位置から点を取得
        /// 優先順位: 1.既存頂点 2.WorkPlane交点 3.カメラ前方の点
        /// </summary>
        private PointInfo GetPointAtScreenPos(ToolContext ctx, Vector2 screenPos)
        {
            // 1. 既存頂点のヒットテスト
            int hitVertex = FindNearestVertexAtScreen(ctx, screenPos);

            if (hitVertex >= 0)
            {
                Vector3 pos = ctx.MeshData.Vertices[hitVertex].Position;
                return PointInfo.FromExisting(hitVertex, pos);
            }

            // 2. WorkPlaneとの交点
            Vector3 worldPos = GetWorldPositionFromScreen(ctx, screenPos);
            return PointInfo.FromNew(worldPos);
        }

        /// <summary>
        /// スクリーン位置から最も近い頂点を検索
        /// </summary>
        private int FindNearestVertexAtScreen(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return -1;

            // ヒット半径（少し大きめに設定）
            float hitRadius = 15f;
            if (ctx.HandleRadius > 0) hitRadius = Mathf.Max(hitRadius, ctx.HandleRadius);

            int nearest = -1;
            float minDist = hitRadius;

            for (int i = 0; i < ctx.MeshData.VertexCount; i++)
            {
                Vector2 vertScreen = ctx.WorldToScreenPos(
                    ctx.MeshData.Vertices[i].Position,
                    ctx.PreviewRect,
                    ctx.CameraPosition,
                    ctx.CameraTarget);

                float dist = Vector2.Distance(screenPos, vertScreen);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>
        /// スクリーン位置からワールド座標を取得
        /// </summary>
        private Vector3 GetWorldPositionFromScreen(ToolContext ctx, Vector2 screenPos)
        {
            // スクリーン座標からレイを作成
            Ray ray;
            if (ctx.ScreenPosToRay != null)
            {
                ray = ctx.ScreenPosToRay(screenPos);
            }
            else
            {
                // フォールバック（通常は使われない）
                Vector3 forward = (ctx.CameraTarget - ctx.CameraPosition).normalized;
                ray = new Ray(ctx.CameraPosition, forward);
            }

            // WorkPlaneとの交点を試みる
            if (ctx.WorkPlane != null)
            {
                if (ctx.WorkPlane.RayIntersect(ray.origin, ray.direction, out Vector3 hitPoint))
                {
                    return hitPoint;
                }
            }

            // 交差しない場合は、カメラから適当な距離の点
            return ray.origin + ray.direction * DefaultDistance * ctx.CameraDistance;
        }

        /// <summary>
        /// プレビュー点を更新
        /// </summary>
        private void UpdatePreview(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null || !ctx.PreviewRect.Contains(screenPos))
            {
                _previewValid = false;
                return;
            }

            // 既存頂点のヒットテスト（フォールバック付き）
            _previewHitVertex = FindNearestVertexAtScreen(ctx, screenPos);

            if (_previewHitVertex >= 0)
            {
                _previewPoint = ctx.MeshData.Vertices[_previewHitVertex].Position;
            }
            else
            {
                _previewPoint = GetWorldPositionFromScreen(ctx, screenPos);
            }

            _previewValid = true;
        }

        /// <summary>
        /// 面を作成し、作成された頂点インデックスのリストを返す
        /// </summary>
        private List<int> CreateFace(ToolContext ctx)
        {
            var createdIndices = new List<int>();

            if (ctx.MeshData == null || _points.Count < 2)
                return createdIndices;

            var meshData = ctx.MeshData;
            var newVertexIndices = new List<int>();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            // 各点について、既存頂点を使用するか新規作成
            for (int i = 0; i < _points.Count; i++)
            {
                var point = _points[i];
                if (point.IsExistingVertex)
                {
                    newVertexIndices.Add(point.ExistingVertexIndex);
                    createdIndices.Add(point.ExistingVertexIndex);
                }
                else
                {
                    // 新規頂点を作成
                    var vertex = new Vertex(point.Position);
                    vertex.UVs.Add(Vector2.zero);  // デフォルトUV
                    vertex.Normals.Add(Vector3.up);  // 仮の法線（後で計算）

                    int newIndex = meshData.Vertices.Count;
                    meshData.Vertices.Add(vertex);
                    newVertexIndices.Add(newIndex);
                    addedVertices.Add((newIndex, vertex));
                    createdIndices.Add(newIndex);

                    // _pointsの情報も更新（次回使用時のため）
                    _points[i] = PointInfo.FromExisting(newIndex, point.Position);
                }
            }

            // 面を作成
            Face newFace = null;
            switch (Mode)
            {
                case AddFaceMode.Line:
                    // 2点の補助線を作成（2頂点のFace）
                    if (newVertexIndices.Count >= 2)
                    {
                        newFace = new Face();
                        newFace.VertexIndices.Add(newVertexIndices[0]);
                        newFace.VertexIndices.Add(newVertexIndices[1]);
                        newFace.UVIndices.Add(0);
                        newFace.UVIndices.Add(0);
                        newFace.NormalIndices.Add(0);
                        newFace.NormalIndices.Add(0);
                        newFace.MaterialIndex = ctx.CurrentMaterialIndex;
                    }
                    break;

                case AddFaceMode.Triangle:
                    if (newVertexIndices.Count >= 3)
                    {
                        newFace = new Face(
                            newVertexIndices[0],
                            newVertexIndices[1],
                            newVertexIndices[2],
                            ctx.CurrentMaterialIndex);
                    }
                    break;

                case AddFaceMode.Quad:
                    if (newVertexIndices.Count >= 4)
                    {
                        newFace = new Face(
                            newVertexIndices[0],
                            newVertexIndices[1],
                            newVertexIndices[2],
                            newVertexIndices[3],
                            ctx.CurrentMaterialIndex);
                    }
                    break;
            }

            if (newFace != null)
            {
                // 3頂点以上の場合は法線を計算
                if (newFace.VertexCount >= 3)
                {
                    Vector3 faceNormal = CalculateFaceNormal(meshData, newFace);
                    foreach (int vi in newFace.VertexIndices)
                    {
                        var vertex = meshData.Vertices[vi];
                        if (vertex.Normals.Count == 0)
                        {
                            vertex.Normals.Add(faceNormal);
                        }
                        else
                        {
                            vertex.Normals[0] = faceNormal;
                        }
                    }
                }

                int faceIndex = meshData.Faces.Count;
                meshData.Faces.Add(newFace);

                Debug.Log($"[AddFaceTool] Created {Mode}: VertexCount={newFace.VertexCount}, MaterialIndex={newFace.MaterialIndex}");

                // メッシュを更新
                ctx.SyncMesh?.Invoke();

                // Undo記録
                if (ctx.UndoController != null)
                {
                    ctx.UndoController.RecordAddFaceOperation(newFace, faceIndex, addedVertices);
                }
            }

            ctx.Repaint?.Invoke();

            return createdIndices;
        }

        /// <summary>
        /// 面の法線を計算
        /// </summary>
        private Vector3 CalculateFaceNormal(MeshData meshData, Face face)
        {
            if (face.VertexCount < 3)
                return Vector3.up;

            Vector3 p0 = meshData.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshData.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshData.Vertices[face.VertexIndices[2]].Position;

            Vector3 v1 = p1 - p0;
            Vector3 v2 = p2 - p0;
            Vector3 normal = Vector3.Cross(v1, v2).normalized;

            if (normal.sqrMagnitude < 0.001f)
                return Vector3.up;

            return normal;
        }

        // === 公開メソッド ===

        /// <summary>
        /// 現在の点をクリア
        /// </summary>
        public void ClearPoints()
        {
            _points.Clear();
            _previewValid = false;
        }

        /// <summary>
        /// 点の数
        /// </summary>
        public int PointCount => _points.Count;
    }
}