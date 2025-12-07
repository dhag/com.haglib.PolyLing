// Tools/AddFaceTool.cs
// 面追加ツール（2点=Line、3点=Triangle、4点=Quad）
// マルチマテリアル対応 + Line描画対応

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

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

        // === 設定 ===
        private AddFaceMode _mode = AddFaceMode.Triangle;
        private float _defaultDistance = 1.5f;  // WorkPlaneと交差しない場合のカメラからの距離
        private bool _continuousLine = false;   // 連続線分モード

        // === 状態 ===
        private List<PointInfo> _points = new List<PointInfo>();
        private PointInfo? _lastLinePoint = null;  // 連続線分の最後の点
        private Vector3 _previewPoint;          // 現在のマウス位置での候補点
        private bool _previewValid = false;
        private int _previewHitVertex = -1;     // プレビュー時に既存頂点にヒットしている場合

        // === モード名 ===
        private static readonly string[] ModeNames = { "Line (2)", "Triangle (3)", "Quad (4)" };
        private static readonly AddFaceMode[] ModeValues = { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };

        // === プロパティ ===
        public AddFaceMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public int RequiredPoints => (int)_mode;

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

            // 連続線分モードの場合
            if (_mode == AddFaceMode.Line && _continuousLine && _lastLinePoint.HasValue)
            {
                // 前回の最後の点と今回の点で線分を作成
                _points.Clear();
                _points.Add(_lastLinePoint.Value);
                _points.Add(point);
                CreateFace(ctx);
                _lastLinePoint = point;  // 今回の点を次の開始点に
                _points.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }

            _points.Add(point);

            // 必要な点数に達したら面を作成
            if (_points.Count >= RequiredPoints)
            {
                // 連続線分モードの場合、最後の点を保存
                if (_mode == AddFaceMode.Line && _continuousLine)
                {
                    _lastLinePoint = _points[_points.Count - 1];
                }

                CreateFace(ctx);
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

            Handles.BeginGUI();

            // 連続線分モードの開始点を描画
            if (_mode == AddFaceMode.Line && _continuousLine && _lastLinePoint.HasValue && _points.Count == 0)
            {
                Vector2 startScreen = ctx.WorldToScreenPos(
                    _lastLinePoint.Value.Position, ctx.PreviewRect,
                    ctx.CameraPosition, ctx.CameraTarget);

                // 開始点（オレンジ）
                Color startColor = new Color(1f, 0.5f, 0f, 0.9f);
                float size = 12f;
                EditorGUI.DrawRect(new Rect(
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

                    Handles.color = new Color(1f, 0.5f, 0f, 0.5f);
                    Handles.DrawAAPolyLine(2f,
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
                EditorGUI.DrawRect(new Rect(
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

                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                    Handles.DrawAAPolyLine(2f,
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

                // プレビュー点の色（既存頂点=シアン半透明、新規=黄色半透明）
                Color previewColor = _previewHitVertex >= 0
                    ? new Color(0f, 1f, 1f, 0.5f)
                    : new Color(1f, 1f, 0f, 0.5f);

                float size = 10f;
                EditorGUI.DrawRect(new Rect(
                    previewScreen.x - size / 2,
                    previewScreen.y - size / 2,
                    size, size), previewColor);

                // 最後の確定点からプレビュー点への線
                if (_points.Count > 0)
                {
                    Vector2 lastScreen = ctx.WorldToScreenPos(
                        _points[_points.Count - 1].Position, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.4f);
                    Handles.DrawAAPolyLine(2f,
                        new Vector3(lastScreen.x, lastScreen.y, 0),
                        new Vector3(previewScreen.x, previewScreen.y, 0));
                }

                // 閉じる線（最後の点から最初の点へ）のプレビュー（三角形/四角形の場合のみ）
                if (_mode != AddFaceMode.Line && _points.Count == RequiredPoints - 1 && _points.Count >= 2)
                {
                    Vector2 firstScreen = ctx.WorldToScreenPos(
                        _points[0].Position, ctx.PreviewRect,
                        ctx.CameraPosition, ctx.CameraTarget);

                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.3f);
                    Handles.DrawAAPolyLine(1f,
                        new Vector3(previewScreen.x, previewScreen.y, 0),
                        new Vector3(firstScreen.x, firstScreen.y, 0));
                }
            }

            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            // モード選択
            EditorGUILayout.LabelField("Face Type", EditorStyles.miniBoldLabel);
            int currentIndex = System.Array.IndexOf(ModeValues, _mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                _mode = ModeValues[newIndex];
                _points.Clear();  // モード変更時はリセット
                _lastLinePoint = null;
            }

            // Lineモード専用オプション
            if (_mode == AddFaceMode.Line)
            {
                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                _continuousLine = EditorGUILayout.Toggle("Continuous Line", _continuousLine);
                if (EditorGUI.EndChangeCheck())
                {
                    _lastLinePoint = null;  // モード変更時はリセット
                }

                if (_continuousLine && _lastLinePoint.HasValue)
                {
                    EditorGUILayout.LabelField("  ↳ Click to continue from last point", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // 進捗表示
            string progressText = $"Points: {_points.Count} / {RequiredPoints}";
            if (_mode == AddFaceMode.Line && _continuousLine && _lastLinePoint.HasValue)
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
            bool hasData = _points.Count > 0 || (_continuousLine && _lastLinePoint.HasValue);
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
            int hitVertex = ctx.FindVertexAtScreenPos(
                screenPos, ctx.MeshData, ctx.PreviewRect,
                ctx.CameraPosition, ctx.CameraTarget, ctx.HandleRadius);

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
            return ray.origin + ray.direction * _defaultDistance * ctx.CameraDistance;
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

            // 既存頂点のヒットテスト
            _previewHitVertex = ctx.FindVertexAtScreenPos(
                screenPos, ctx.MeshData, ctx.PreviewRect,
                ctx.CameraPosition, ctx.CameraTarget, ctx.HandleRadius);

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
        /// 面を作成
        /// </summary>
        private void CreateFace(ToolContext ctx)
        {
            if (ctx.MeshData == null || _points.Count < 2)
                return;

            var meshData = ctx.MeshData;
            var newVertexIndices = new List<int>();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            // 各点について、既存頂点を使用するか新規作成
            foreach (var point in _points)
            {
                if (point.IsExistingVertex)
                {
                    newVertexIndices.Add(point.ExistingVertexIndex);
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
                }
            }

            // 面を作成
            Face newFace = null;
            switch (_mode)
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

                Debug.Log($"[AddFaceTool] Created {_mode}: VertexCount={newFace.VertexCount}, MaterialIndex={newFace.MaterialIndex}");

                // メッシュを更新
                ctx.SyncMesh?.Invoke();

                // Undo記録
                if (ctx.UndoController != null)
                {
                    ctx.UndoController.RecordAddFaceOperation(newFace, faceIndex, addedVertices);
                }
            }

            ctx.Repaint?.Invoke();
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