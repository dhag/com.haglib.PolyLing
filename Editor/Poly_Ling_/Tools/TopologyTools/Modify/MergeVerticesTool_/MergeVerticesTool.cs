// Tools/MergeVerticesTool.cs
// 頂点マージツール - 選択頂点のうち距離がしきい値以下のものを統合
// Phase 4: MeshMergeHelper使用に変更
// Phase 5: OnTopologyChanged()による標準的な選択クリア処理
//
// 【トポロジカル変更の分類】
// このツールは「削除を伴う変更」に該当するため、
// 実行後は ctx.OnTopologyChanged() で全選択をクリアする。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Utilities;
using Poly_Ling.Commands;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点マージツール
    /// </summary>
    public partial class MergeVerticesTool : IEditTool
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
            if (ctx.MeshObject == null || !ShowPreview) return;
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
                        if (vIdx >= 0 && vIdx < ctx.MeshObject.VertexCount)
                            centroid += ctx.MeshObject.Vertices[vIdx].Position;
                    }
                    centroid /= group.Count;

                    Vector2 centroidScreen = ctx.WorldToScreen(centroid);

                    // 各頂点から重心への線を描画
                    UnityEditor_Handles.color = color;
                    foreach (int vIdx in group)
                    {
                        if (vIdx < 0 || vIdx >= ctx.MeshObject.VertexCount) continue;
                        Vector2 vScreen = ctx.WorldToScreen(ctx.MeshObject.Vertices[vIdx].Position);
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
                    if (vIdx < 0 || vIdx >= ctx.MeshObject.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshObject.Vertices[vIdx].Position);
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
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // しきい値
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("Threshold"), GUILayout.Width(70));
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
            ShowPreview = EditorGUILayout.Toggle(T("ShowPreview"), ShowPreview);

            EditorGUILayout.Space(5);

            // プレビュー情報
            if (_preview.GroupCount > 0)
            {
                EditorGUILayout.LabelField(T("Groups", _preview.GroupCount), EditorStyles.miniLabel);
                EditorGUILayout.LabelField(T("VerticesToRemove", _preview.TotalVerticesToMerge), EditorStyles.miniLabel);

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
                    EditorGUILayout.LabelField(T("MoreGroups", _preview.Groups.Count - 5), EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField(T("NoMerge"), EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(10);

            // マージボタン
            EditorGUI.BeginDisabledGroup(_preview.GroupCount == 0);
            if (GUILayout.Button(T("Merge"), GUILayout.Height(30)))
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
            if (ctx.MeshObject != null && ctx.SelectedVertices != null)
            {
                _preview = CalculatePreview(ctx.MeshObject, ctx.SelectedVertices, Threshold);
            }

            // マージ実行
            if (_pendingMerge && ctx.MeshObject != null)
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
            if (ctx.MeshObject == null || ctx.SelectedVertices == null) return;
            if (ctx.SelectedVertices.Count < 2) return;

            // Undo用スナップショット
            MeshObjectSnapshot before = ctx.UndoController?.VertexEditStack != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : default;

            // MeshMergeHelper使用
            var result = MeshMergeHelper.MergeVerticesAtSamePosition(ctx.MeshObject, ctx.SelectedVertices, Threshold);

            if (result.Success)
            {
                // トポロジカル変更後の標準処理（削除を伴うため選択クリア）
                ctx.OnTopologyChanged();

                // Undo記録（キュー経由）
                if (ctx.UndoController != null && ctx.CommandQueue != null)
                {
                    MeshObjectSnapshot after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                    ctx.CommandQueue.Enqueue(new RecordTopologyChangeCommand(
                        ctx.UndoController, before, after, "Merge Vertices"));
                }

                Debug.Log($"[MergeTool] {result.Message}");
            }
        }

        // ================================================================
        // プレビュー計算
        // ================================================================

        private MergePreviewInfo CalculatePreview(MeshObject meshObject, HashSet<int> selectedVertices, float threshold)
        {
            var result = new MergePreviewInfo { Groups = new List<List<int>>() };

            if (meshObject == null || selectedVertices == null || selectedVertices.Count < 2)
                return result;

            var validSelected = selectedVertices
                .Where(v => v >= 0 && v < meshObject.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
                return result;

            // Union-Find
            var parent = new int[meshObject.VertexCount];
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
                        meshObject.Vertices[validSelected[i]].Position,
                        meshObject.Vertices[validSelected[j]].Position);

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
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="targetVertices">マージ対象の頂点インデックス</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeVerticesAtSamePosition(MeshObject meshObject, HashSet<int> targetVertices, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeVerticesAtSamePosition(meshObject, targetVertices, threshold);
        }

        /// <summary>
        /// メッシュ内の全頂点を対象に、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeAllVerticesAtSamePosition(MeshObject meshObject, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeAllVerticesAtSamePosition(meshObject, threshold);
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
