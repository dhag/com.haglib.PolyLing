// Tools/MergeVerticesTool.cs
// 頂点マージツール - 選択頂点のうち距離がしきい値以下のものを統合
// Phase 4: MeshMergeHelper使用に変更

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Utilities;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 頂点マージツール
    /// </summary>
    public class MergeVerticesTool : IEditTool
    {
        public string Name => "Merge";
        public string DisplayName => "Merge";
        // ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private MergeVerticesSettings _settings = new MergeVerticesSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private float Threshold
        {
            get => _settings.Threshold;
            set => _settings.Threshold = value;
        }

        private bool ShowPreview
        {
            get => _settings.ShowPreview;
            set => _settings.ShowPreview = value;
        }

        // === プレビュー ===
        private MergePreviewInfo _preview = new MergePreviewInfo { Groups = new List<List<int>>() };
#pragma warning disable CS0414
        private bool _previewDirty = true;  // 将来の最適化用
#pragma warning restore CS0414

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
            if (ctx.MeshData == null || !ShowPreview) return;
            if (_preview.Groups == null || _preview.Groups.Count == 0) return;

            UnityEditor_Handles.BeginGUI();

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
                    UnityEditor_Handles.color = color;
                    foreach (int vIdx in group)
                    {
                        if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                        Vector2 vScreen = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                        UnityEditor_Handles.DrawAAPolyLine(2f, vScreen, centroidScreen);
                    }

                    // 重心にマーカー
                    GUI.color = color;
                    float size = 10f;
                    GUI.DrawTexture(new Rect(centroidScreen.x - size / 2, centroidScreen.y - size / 2, size, size),
                        EditorGUIUtility.whiteTexture);
                }

                // グループ内の頂点をハイライト
                GUI.color = color;
                foreach (int vIdx in group)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                    float size = 8f;
                    GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size),
                        EditorGUIUtility.whiteTexture);
                }
            }

            GUI.color = Color.white;
            UnityEditor_Handles.EndGUI();
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
            Threshold = EditorGUILayout.FloatField(Threshold, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                Threshold = Mathf.Max(0.0001f, Threshold);
                _previewDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            // プリセット
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.001", EditorStyles.miniButtonLeft)) { Threshold = 0.001f; _previewDirty = true; }
            if (GUILayout.Button("0.01", EditorStyles.miniButtonMid)) { Threshold = 0.01f; _previewDirty = true; }
            if (GUILayout.Button("0.1", EditorStyles.miniButtonRight)) { Threshold = 0.1f; _previewDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // プレビュー表示切替
            ShowPreview = EditorGUILayout.Toggle("Show Preview", ShowPreview);

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
                _preview = CalculatePreview(ctx.MeshData, ctx.SelectedVertices, Threshold);
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
        // マージ実行（UIボタンから）
        // ================================================================

        private void ExecuteMerge(ToolContext ctx)
        {
            if (ctx.MeshData == null || ctx.SelectedVertices == null) return;
            if (ctx.SelectedVertices.Count < 2) return;

            // Undo用スナップショット
            var before = ctx.UndoController?.VertexEditStack != null
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext)
                : default;

            // MeshMergeHelper使用
            var result = MeshMergeHelper.MergeVerticesAtSamePosition(ctx.MeshData, ctx.SelectedVertices, Threshold);

            if (result.Success)
            {
                // 選択をクリア
                ctx.SelectedVertices.Clear();

                // Undo記録
                if (ctx.UndoController != null)
                {
                    var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                    ctx.UndoController.RecordTopologyChange(before, after, "Merge Vertices");
                }

                Debug.Log($"[MergeTool] {result.Message}");
            }
        }

        // ================================================================
        // プレビュー計算
        // ================================================================

        private MergePreviewInfo CalculatePreview(MeshData meshData, HashSet<int> selectedVertices, float threshold)
        {
            var result = new MergePreviewInfo { Groups = new List<List<int>>() };

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
        // 静的マージメソッド（外部から呼び出し可能）- MeshMergeHelperへのラッパー
        // ================================================================

        /// <summary>
        /// 指定された頂点のうち、しきい値以下の距離にあるものをマージする（静的版）
        /// </summary>
        /// <param name="meshData">対象メッシュ</param>
        /// <param name="targetVertices">マージ対象の頂点インデックス</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeVerticesAtSamePosition(MeshData meshData, HashSet<int> targetVertices, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeVerticesAtSamePosition(meshData, targetVertices, threshold);
        }

        /// <summary>
        /// メッシュ内の全頂点を対象に、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshData">対象メッシュ</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeAllVerticesAtSamePosition(MeshData meshData, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeAllVerticesAtSamePosition(meshData, threshold);
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
