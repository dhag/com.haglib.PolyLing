// Tools/LineExtrudeTool.cs
// ライン（2頂点の補助線）をCSVとして保存するツール
// Profile2DExtrudeWindow.csと連携

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// ライン→CSV保存ツール
    /// 選択されたライン（2頂点Face）を閉曲線として保存
    /// </summary>
    public class LineExtrudeTool : IEditTool
    {
        public string Name => "Line Extrude";

        // === 状態 ===
        private List<int> _selectedLineIndices = new List<int>();
        private ToolContext _lastContext;
        
        // 構築されたループ情報
        private List<LoopInfo> _detectedLoops = new List<LoopInfo>();

        private class LoopInfo
        {
            public List<int> VertexIndices = new List<int>();
            public bool IsClockwise;
            public bool IsHole;  // 反時計回りならホール
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;
            if (_selectedLineIndices.Count == 0) return;

            Handles.BeginGUI();

            // 選択ラインをハイライト
            Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
            foreach (int lineIdx in _selectedLineIndices)
            {
                if (lineIdx < 0 || lineIdx >= ctx.MeshData.FaceCount) continue;
                var face = ctx.MeshData.Faces[lineIdx];
                if (face.VertexCount != 2) continue;

                Vector3 p0 = ctx.MeshData.Vertices[face.VertexIndices[0]].Position;
                Vector3 p1 = ctx.MeshData.Vertices[face.VertexIndices[1]].Position;

                Vector2 sp0 = ctx.WorldToScreen(p0);
                Vector2 sp1 = ctx.WorldToScreen(p1);

                Handles.DrawAAPolyLine(4f, sp0, sp1);
            }

            // 検出されたループを表示
            int loopColorIdx = 0;
            Color[] loopColors = { Color.cyan, Color.magenta, Color.yellow, Color.green };
            
            foreach (var loop in _detectedLoops)
            {
                Color col = loopColors[loopColorIdx % loopColors.Length];
                col.a = 0.6f;
                Handles.color = col;
                
                for (int i = 0; i < loop.VertexIndices.Count; i++)
                {
                    int next = (i + 1) % loop.VertexIndices.Count;
                    int v0 = loop.VertexIndices[i];
                    int v1 = loop.VertexIndices[next];
                    
                    if (v0 < ctx.MeshData.VertexCount && v1 < ctx.MeshData.VertexCount)
                    {
                        Vector3 p0 = ctx.MeshData.Vertices[v0].Position;
                        Vector3 p1 = ctx.MeshData.Vertices[v1].Position;
                        Vector2 sp0 = ctx.WorldToScreen(p0);
                        Vector2 sp1 = ctx.WorldToScreen(p1);
                        Handles.DrawAAPolyLine(2f, sp0, sp1);
                    }
                }
                loopColorIdx++;
            }

            GUI.color = Color.white;
            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Line → CSV Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Select lines that form closed loops.\n" +
                "Clockwise = Outer, Counter-clockwise = Hole\n" +
                "Save as CSV for Profile2DExtrude.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // 選択情報
            EditorGUILayout.LabelField($"Selected Lines: {_selectedLineIndices.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Detected Loops: {_detectedLoops.Count}", EditorStyles.miniLabel);

            // ループ詳細
            if (_detectedLoops.Count > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Loops:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < _detectedLoops.Count; i++)
                {
                    var loop = _detectedLoops[i];
                    string typeStr = loop.IsHole ? "Hole (CCW)" : "Outer (CW)";
                    EditorGUILayout.LabelField($"Loop {i + 1}: {loop.VertexIndices.Count} vertices, {typeStr}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // 解析ボタン
            if (GUILayout.Button("Analyze Loops", GUILayout.Height(25)))
            {
                AnalyzeLoops();
            }

            EditorGUILayout.Space(5);

            // 保存ボタン
            EditorGUI.BeginDisabledGroup(_detectedLoops.Count == 0);
            if (GUILayout.Button("Save as CSV...", GUILayout.Height(30)))
            {
                SaveAsCSV();
            }
            EditorGUI.EndDisabledGroup();

            if (_selectedLineIndices.Count < 3)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Select at least 3 connected lines.", MessageType.Warning);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateSelectedLines(ctx);
            AnalyzeLoops();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _selectedLineIndices.Clear();
            _detectedLoops.Clear();
        }

        public void Reset()
        {
            _selectedLineIndices.Clear();
            _detectedLoops.Clear();
        }

        public void Update(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateSelectedLines(ctx);
        }

        public void OnSelectionChanged()
        {
            if (_lastContext != null)
            {
                UpdateSelectedLines(_lastContext);
                AnalyzeLoops();
            }
        }

        // ================================================================
        // 内部処理
        // ================================================================

        private void UpdateSelectedLines(ToolContext ctx)
        {
            _selectedLineIndices.Clear();

            if (ctx.MeshData == null || ctx.SelectionState == null)
                return;

            foreach (int lineIdx in ctx.SelectionState.Lines)
            {
                if (lineIdx >= 0 && lineIdx < ctx.MeshData.FaceCount)
                {
                    var face = ctx.MeshData.Faces[lineIdx];
                    if (face.VertexCount == 2)
                    {
                        _selectedLineIndices.Add(lineIdx);
                    }
                }
            }
        }

        /// <summary>
        /// 選択されたラインからループを解析
        /// </summary>
        private void AnalyzeLoops()
        {
            _detectedLoops.Clear();

            if (_lastContext?.MeshData == null || _selectedLineIndices.Count < 3)
                return;

            var meshData = _lastContext.MeshData;

            // 使用済みラインを追跡
            var remainingLines = new HashSet<int>(_selectedLineIndices);

            // 頂点→ライン接続マップを構築
            var vertexToLines = new Dictionary<int, List<int>>();
            foreach (int lineIdx in _selectedLineIndices)
            {
                var face = meshData.Faces[lineIdx];
                int v0 = face.VertexIndices[0];
                int v1 = face.VertexIndices[1];

                if (!vertexToLines.ContainsKey(v0))
                    vertexToLines[v0] = new List<int>();
                if (!vertexToLines.ContainsKey(v1))
                    vertexToLines[v1] = new List<int>();

                vertexToLines[v0].Add(lineIdx);
                vertexToLines[v1].Add(lineIdx);
            }

            // ループを探索
            while (remainingLines.Count >= 3)
            {
                var loop = TryBuildLoop(meshData, remainingLines, vertexToLines);
                if (loop != null && loop.VertexIndices.Count >= 3)
                {
                    // 時計回り判定
                    loop.IsClockwise = IsClockwise(meshData, loop.VertexIndices);
                    loop.IsHole = !loop.IsClockwise;  // 反時計回りはホール
                    _detectedLoops.Add(loop);
                }
                else
                {
                    break;  // これ以上ループが見つからない
                }
            }

            Debug.Log($"[LineExtrudeTool] Analyzed {_detectedLoops.Count} loops from {_selectedLineIndices.Count} lines");
        }

        /// <summary>
        /// 残りのラインからループを1つ構築
        /// </summary>
        private LoopInfo TryBuildLoop(MeshData meshData, HashSet<int> remainingLines, Dictionary<int, List<int>> vertexToLines)
        {
            if (remainingLines.Count == 0) return null;

            var loop = new LoopInfo();
            var usedLines = new HashSet<int>();

            // 最初のラインから開始
            int firstLineIdx = remainingLines.First();
            var firstFace = meshData.Faces[firstLineIdx];
            int startVertex = firstFace.VertexIndices[0];
            int currentVertex = startVertex;

            loop.VertexIndices.Add(currentVertex);
            usedLines.Add(firstLineIdx);
            currentVertex = firstFace.VertexIndices[1];

            // ループを辿る
            int maxIterations = remainingLines.Count + 1;
            int iterations = 0;

            while (currentVertex != startVertex && iterations < maxIterations)
            {
                iterations++;
                loop.VertexIndices.Add(currentVertex);

                // 次のラインを探す
                if (!vertexToLines.TryGetValue(currentVertex, out var connectedLines))
                    break;

                int nextLineIdx = -1;
                foreach (int lineIdx in connectedLines)
                {
                    if (remainingLines.Contains(lineIdx) && !usedLines.Contains(lineIdx))
                    {
                        nextLineIdx = lineIdx;
                        break;
                    }
                }

                if (nextLineIdx < 0)
                    break;

                usedLines.Add(nextLineIdx);

                // 次の頂点を決定
                var nextFace = meshData.Faces[nextLineIdx];
                if (nextFace.VertexIndices[0] == currentVertex)
                    currentVertex = nextFace.VertexIndices[1];
                else
                    currentVertex = nextFace.VertexIndices[0];
            }

            // ループが閉じたか確認
            if (currentVertex == startVertex && loop.VertexIndices.Count >= 3)
            {
                // 使用したラインを残りから削除
                foreach (int lineIdx in usedLines)
                {
                    remainingLines.Remove(lineIdx);
                }
                return loop;
            }

            return null;
        }

        /// <summary>
        /// ループが時計回りかどうか判定（Shoelace formula）
        /// XY平面を仮定
        /// </summary>
        private bool IsClockwise(MeshData meshData, List<int> vertexIndices)
        {
            if (vertexIndices.Count < 3) return true;

            float sum = 0;
            for (int i = 0; i < vertexIndices.Count; i++)
            {
                int next = (i + 1) % vertexIndices.Count;
                Vector3 p0 = meshData.Vertices[vertexIndices[i]].Position;
                Vector3 p1 = meshData.Vertices[vertexIndices[next]].Position;

                sum += (p1.x - p0.x) * (p1.y + p0.y);
            }

            // 正なら時計回り（Yが上向きの座標系）
            return sum > 0;
        }

        /// <summary>
        /// CSVとして保存
        /// </summary>
        private void SaveAsCSV()
        {
            if (_lastContext?.MeshData == null || _detectedLoops.Count == 0)
                return;

            string path = EditorUtility.SaveFilePanel(
                "Save Lines as CSV",
                Application.dataPath,
                "profile",
                "csv");

            if (string.IsNullOrEmpty(path))
                return;

            var meshData = _lastContext.MeshData;
            var sb = new StringBuilder();

            // ヘッダーコメント
            sb.AppendLine("# Profile2D CSV - Generated by LineExtrudeTool");
            sb.AppendLine($"# Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Loops: {_detectedLoops.Count}");
            sb.AppendLine();

            // 外側ループを先に出力
            var outerLoops = _detectedLoops.Where(l => !l.IsHole).ToList();
            var holeLoops = _detectedLoops.Where(l => l.IsHole).ToList();

            // 外側ループ
            foreach (var loop in outerLoops)
            {
                sb.AppendLine("# OUTER");
                WriteLoopPoints(sb, meshData, loop);
                sb.AppendLine();
            }

            // ホール
            foreach (var loop in holeLoops)
            {
                sb.AppendLine("# HOLE");
                WriteLoopPoints(sb, meshData, loop);
                sb.AppendLine();
            }

            try
            {
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[LineExtrudeTool] Saved {_detectedLoops.Count} loops to: {path}");
                EditorUtility.DisplayDialog("Success", $"Saved {_detectedLoops.Count} loops to CSV.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LineExtrudeTool] Failed to save CSV: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to save: {ex.Message}", "OK");
            }
        }

        private void WriteLoopPoints(StringBuilder sb, MeshData meshData, LoopInfo loop)
        {
            foreach (int vIdx in loop.VertexIndices)
            {
                Vector3 pos = meshData.Vertices[vIdx].Position;
                // XY座標を出力（小数点6桁）
                sb.AppendLine($"{pos.x:F6},{pos.y:F6}");
            }
        }
    }
}