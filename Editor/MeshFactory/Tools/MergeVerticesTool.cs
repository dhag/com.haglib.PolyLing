// Tools/MergeVerticesTool.cs
// 頂点マージツール - 選択頂点のうち距離がしきい値以下のものを統合

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
    /// 頂点マージツール
    /// </summary>
    public class MergeVerticesTool : IEditTool
    {
        public string Name => "Merge";

        // === 設定 ===
        private float _threshold = 0.001f;
        private bool _showPreview = true;

        // === プレビュー ===
        private MergePreviewInfo _preview = new MergePreviewInfo { Groups = new List<List<int>>() };
        private bool _previewDirty = true;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // クリックでは何もしない（UIからマージを実行）
            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null || !_showPreview) return;
            if (_preview.Groups == null || _preview.Groups.Count == 0) return;

            Handles.BeginGUI();

            // マージグループをプレビュー表示
            Color[] groupColors = {
                new Color(1f, 0.3f, 0.3f, 0.8f),    // 赤
                new Color(0.3f, 1f, 0.3f, 0.8f),    // 緑
                new Color(0.3f, 0.3f, 1f, 0.8f),    // 青
                new Color(1f, 1f, 0.3f, 0.8f),      // 黄
                new Color(1f, 0.3f, 1f, 0.8f),      // マゼンタ
                new Color(0.3f, 1f, 1f, 0.8f),      // シアン
            };

            for (int g = 0; g < _preview.Groups.Count; g++)
            {
                var group = _preview.Groups[g];
                Color color = groupColors[g % groupColors.Length];

                // グループ内の頂点を結ぶ線を描画
                if (group.Count >= 2)
                {
                    // 重心を計算
                    Vector3 centroid = Vector3.zero;
                    foreach (int vIdx in group)
                    {
                        if (vIdx >= 0 && vIdx < ctx.MeshData.VertexCount)
                            centroid += ctx.MeshData.Vertices[vIdx].Position;
                    }
                    centroid /= group.Count;

                    Vector2 centroidScreen = ctx.WorldToScreen(centroid);

                    // 各頂点から重心への線を描画
                    Handles.color = color;
                    foreach (int vIdx in group)
                    {
                        if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                        Vector2 vScreen = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                        Handles.DrawAAPolyLine(2f, vScreen, centroidScreen);
                    }

                    // 重心にマーカー
                    GUI.color = color;
                    float size = 10f;
                    GUI.DrawTexture(new Rect(centroidScreen.x - size/2, centroidScreen.y - size/2, size, size), 
                        EditorGUIUtility.whiteTexture);
                }

                // グループ内の頂点をハイライト
                GUI.color = color;
                foreach (int vIdx in group)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                    float size = 8f;
                    GUI.DrawTexture(new Rect(sp.x - size/2, sp.y - size/2, size, size), 
                        EditorGUIUtility.whiteTexture);
                }
            }

            GUI.color = Color.white;
            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Merge Vertices Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Merge selected vertices that are within threshold distance.\n" +
                "A-B close, B-C close → A,B,C merged into one.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // しきい値
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Threshold", GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            _threshold = EditorGUILayout.FloatField(_threshold, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                _threshold = Mathf.Max(0.0001f, _threshold);
                _previewDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            // プリセット
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.001", EditorStyles.miniButtonLeft)) { _threshold = 0.001f; _previewDirty = true; }
            if (GUILayout.Button("0.01", EditorStyles.miniButtonMid)) { _threshold = 0.01f; _previewDirty = true; }
            if (GUILayout.Button("0.1", EditorStyles.miniButtonRight)) { _threshold = 0.1f; _previewDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // プレビュー表示切替
            _showPreview = EditorGUILayout.Toggle("Show Preview", _showPreview);

            EditorGUILayout.Space(5);

            // プレビュー情報
            if (_preview.GroupCount > 0)
            {
                EditorGUILayout.LabelField($"Groups: {_preview.GroupCount}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Vertices to remove: {_preview.TotalVerticesToMerge}", EditorStyles.miniLabel);

                // グループ詳細
                EditorGUILayout.Space(3);
                for (int i = 0; i < Mathf.Min(_preview.Groups.Count, 5); i++)
                {
                    var group = _preview.Groups[i];
                    EditorGUILayout.LabelField(
                        $"  [{i}] {group.Count} verts: {string.Join(",", group.Take(8))}{(group.Count > 8 ? "..." : "")}",
                        EditorStyles.miniLabel);
                }
                if (_preview.Groups.Count > 5)
                {
                    EditorGUILayout.LabelField($"  ... +{_preview.Groups.Count - 5} more groups", EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No vertices to merge", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(10);

            // マージボタン
            EditorGUI.BeginDisabledGroup(_preview.GroupCount == 0);
            if (GUILayout.Button("Merge", GUILayout.Height(30)))
            {
                _pendingMerge = true;
            }
            EditorGUI.EndDisabledGroup();
        }

        private bool _pendingMerge = false;
        private ToolContext _lastContext;

        public void OnActivate(ToolContext ctx)
        {
            _previewDirty = true;
            _lastContext = ctx;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _preview = default;
        }

        public void Reset()
        {
            _preview = default;
            _previewDirty = true;
            _pendingMerge = false;
        }

        /// <summary>
        /// 毎フレーム呼ばれる更新処理（SimpleMeshFactory側から呼び出す）
        /// </summary>
        public void Update(ToolContext ctx)
        {
            _lastContext = ctx;

            // プレビュー更新（毎フレーム再計算 - 選択変更を検出するため）
            if (ctx.MeshData != null && ctx.SelectedVertices != null)
            {
                _preview = CalculatePreview(ctx.MeshData, ctx.SelectedVertices, _threshold);
            }

            // マージ実行
            if (_pendingMerge && ctx.MeshData != null)
            {
                ExecuteMerge(ctx);
                _pendingMerge = false;
            }
        }

        /// <summary>
        /// 選択変更時に呼び出し
        /// </summary>
        public void OnSelectionChanged()
        {
            _previewDirty = true;
        }

        // ================================================================
        // マージ処理
        // ================================================================

        private void ExecuteMerge(ToolContext ctx)
        {
            if (ctx.MeshData == null || ctx.SelectedVertices == null || ctx.SelectedVertices.Count < 2)
                return;

            var result = MergeVertices(ctx.MeshData, ctx.SelectedVertices, _threshold);

            if (result.Success)
            {
                // 選択をクリア（頂点インデックスが変わるため）
                ctx.SelectedVertices.Clear();

                // メッシュ更新
                ctx.SyncMesh?.Invoke();

                // Undo記録（MeshDataが変更されたのでトポロジ変更として記録）
                // ※ 呼び出し元でUndo処理を行う想定

                Debug.Log($"[MergeVertices] {result.Message}");
            }

            _previewDirty = true;
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // プレビュー計算
        // ================================================================

        private MergePreviewInfo CalculatePreview(MeshData meshData, HashSet<int> selectedVertices, float threshold)
        {
            var result = new MergePreviewInfo
            {
                GroupCount = 0,
                TotalVerticesToMerge = 0,
                Groups = new List<List<int>>()
            };

            if (meshData == null || selectedVertices == null || selectedVertices.Count < 2)
                return result;

            var validSelected = selectedVertices
                .Where(v => v >= 0 && v < meshData.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
                return result;

            // Union-Find
            var parent = new int[meshData.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Unite(int x, int y)
            {
                int rx = Find(x), ry = Find(y);
                if (rx != ry) parent[rx] = ry;
            }

            // 距離計算
            for (int i = 0; i < validSelected.Count; i++)
            {
                for (int j = i + 1; j < validSelected.Count; j++)
                {
                    float dist = Vector3.Distance(
                        meshData.Vertices[validSelected[i]].Position,
                        meshData.Vertices[validSelected[j]].Position);

                    if (dist <= threshold)
                    {
                        Unite(validSelected[i], validSelected[j]);
                    }
                }
            }

            // グループ収集
            var groups = new Dictionary<int, List<int>>();
            foreach (int v in validSelected)
            {
                int root = Find(v);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(v);
            }

            result.Groups = groups.Values.Where(g => g.Count >= 2).ToList();
            result.GroupCount = result.Groups.Count;
            result.TotalVerticesToMerge = result.Groups.Sum(g => g.Count - 1);

            return result;
        }

        // ================================================================
        // マージ実行
        // ================================================================

        public struct MergeResult
        {
            public bool Success;
            public int RemovedVertexCount;
            public string Message;
        }

        private MergeResult MergeVertices(MeshData meshData, HashSet<int> selectedVertices, float threshold)
        {
            var result = new MergeResult { Success = false };

            if (meshData == null || selectedVertices == null || selectedVertices.Count < 2)
            {
                result.Message = "Not enough vertices selected";
                return result;
            }

            var validSelected = selectedVertices
                .Where(v => v >= 0 && v < meshData.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
            {
                result.Message = "Not enough valid vertices";
                return result;
            }

            // Union-Find
            var parent = new int[meshData.VertexCount];
            var rank = new int[meshData.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Unite(int x, int y)
            {
                int rx = Find(x), ry = Find(y);
                if (rx == ry) return;
                if (rank[rx] < rank[ry]) parent[rx] = ry;
                else if (rank[rx] > rank[ry]) parent[ry] = rx;
                else { parent[ry] = rx; rank[rx]++; }
            }

            // 距離計算
            for (int i = 0; i < validSelected.Count; i++)
            {
                for (int j = i + 1; j < validSelected.Count; j++)
                {
                    float dist = Vector3.Distance(
                        meshData.Vertices[validSelected[i]].Position,
                        meshData.Vertices[validSelected[j]].Position);

                    if (dist <= threshold)
                        Unite(validSelected[i], validSelected[j]);
                }
            }

            // グループ収集
            var groups = new Dictionary<int, List<int>>();
            foreach (int v in validSelected)
            {
                int root = Find(v);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(v);
            }

            var mergeGroups = groups.Values.Where(g => g.Count >= 2).ToList();

            if (mergeGroups.Count == 0)
            {
                result.Message = "No vertices within threshold";
                return result;
            }

            // 頂点リマップを構築
            var vertexRemap = new Dictionary<int, int>();
            var verticesToRemove = new HashSet<int>();

            foreach (var group in mergeGroups)
            {
                int representative = group.Min();

                // 重心を計算
                Vector3 centroid = Vector3.zero;
                foreach (int v in group)
                    centroid += meshData.Vertices[v].Position;
                centroid /= group.Count;

                meshData.Vertices[representative].Position = centroid;

                foreach (int v in group)
                {
                    if (v != representative)
                    {
                        vertexRemap[v] = representative;
                        verticesToRemove.Add(v);
                    }
                }
            }

            // Faceの頂点インデックスを更新
            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (vertexRemap.TryGetValue(face.VertexIndices[i], out int newIdx))
                        face.VertexIndices[i] = newIdx;
                }
            }

            // 縮退面を削除
            RemoveDegenerateFaces(meshData);

            // 不要頂点を削除
            if (verticesToRemove.Count > 0)
                RemoveVertices(meshData, verticesToRemove);

            result.Success = true;
            result.RemovedVertexCount = verticesToRemove.Count;
            result.Message = $"Merged {verticesToRemove.Count} vertices into {mergeGroups.Count} groups";

            return result;
        }

        private void RemoveDegenerateFaces(MeshData meshData)
        {
            var toRemove = new List<int>();

            for (int i = 0; i < meshData.FaceCount; i++)
            {
                var face = meshData.Faces[i];
                var unique = new HashSet<int>(face.VertexIndices);

                if (unique.Count < 2 || (face.VertexCount >= 3 && unique.Count < 3))
                    toRemove.Add(i);
            }

            for (int i = toRemove.Count - 1; i >= 0; i--)
                meshData.Faces.RemoveAt(toRemove[i]);

            if (toRemove.Count > 0)
                Debug.Log($"[MergeVertices] Removed {toRemove.Count} degenerate faces");
        }

        private void RemoveVertices(MeshData meshData, HashSet<int> verticesToRemove)
        {
            var indexRemap = new Dictionary<int, int>();
            int newIndex = 0;

            for (int i = 0; i < meshData.VertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                {
                    indexRemap[i] = newIndex;
                    newIndex++;
                }
            }

            var newVertices = new List<Vertex>();
            for (int i = 0; i < meshData.VertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                    newVertices.Add(meshData.Vertices[i]);
            }
            meshData.Vertices.Clear();
            meshData.Vertices.AddRange(newVertices);

            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (indexRemap.TryGetValue(face.VertexIndices[i], out int newIdx))
                        face.VertexIndices[i] = newIdx;
                }
            }
        }
    }

    /// <summary>
    /// マージプレビュー情報
    /// </summary>
    public struct MergePreviewInfo
    {
        public int GroupCount;
        public int TotalVerticesToMerge;
        public List<List<int>> Groups;
    }
}
