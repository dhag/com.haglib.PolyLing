// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DLoopEditor.cs
// 2D閉曲線エディタコンポーネント
// ローカライズ対応版

using static MeshFactory.Gizmo.GLGizmoDrawer;
using static MeshFactory.Profile2DExtrude.Profile2DExtrudeTexts;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshFactory.Profile2DExtrude
{
    /// <summary>
    /// 2Dループエディタ
    /// </summary>
    public class Profile2DLoopEditor
    {
        // 編集対象
        private List<Loop> _loops;
        private int _selectedLoopIndex = 0;
        private int _selectedPointIndex = -1;

        // ドラッグ状態
        private int _dragPointIndex = -1;
        private bool _isDragging = false;
        private Vector2 _dragStartPos;

        // 表示設定
        private float _editorZoom = 1f;
        private Vector2 _editorOffset = Vector2.zero;

        // ホバー中のエッジ
        private int _hoverEdgeLoop = -1;
        private int _hoverEdgeIndex = -1;

        // コールバック
        public Action OnLoopChanged;
        public Action<string> OnRecordUndo;

        // プロパティ
        public int SelectedLoopIndex
        {
            get => _selectedLoopIndex;
            set => _selectedLoopIndex = value;
        }

        public int SelectedPointIndex
        {
            get => _selectedPointIndex;
            set => _selectedPointIndex = value;
        }

        public bool IsDragging => _isDragging;

        /// <summary>
        /// ループリストを設定
        /// </summary>
        public void SetLoops(List<Loop> loops)
        {
            _loops = loops;
        }

        /// <summary>
        /// 選択ポイントインデックスを設定
        /// </summary>
        public void SetSelectedIndex(int index)
        {
            _selectedPointIndex = index;
        }

        /// <summary>
        /// 小さな2Dエディタを描画（左パネル用）
        /// </summary>
        public void Draw2DEditor()
        {
            EditorGUILayout.LabelField(T("Editor2D") + " - " + T("Editor2DHelp"), EditorStyles.boldLabel);

            if (_loops == null || _loops.Count == 0) return;

            // 選択中のループの点の編集
            if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
            {
                var loop = _loops[_selectedLoopIndex];

                // Remove Pointボタン
                GUI.enabled = _selectedPointIndex >= 0 && loop.Points.Count > 3;
                if (GUILayout.Button(T("RemoveSelectedPoint")))
                {
                    loop.Points.RemoveAt(_selectedPointIndex);
                    _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, loop.Points.Count - 1);
                    OnRecordUndo?.Invoke(T("UndoRemovePoint"));
                    OnLoopChanged?.Invoke();
                }
                GUI.enabled = true;

                // 選択点の座標編集
                if (_selectedPointIndex >= 0 && _selectedPointIndex < loop.Points.Count)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector2 newPos = EditorGUILayout.Vector2Field(T("PointN", _selectedPointIndex), loop.Points[_selectedPointIndex]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        loop.Points[_selectedPointIndex] = newPos;
                        OnRecordUndo?.Invoke(T("UndoEditPoint"));
                        OnLoopChanged?.Invoke();
                    }
                }
            }

            EditorGUILayout.Space(5);

            // 2Dエディタ領域
            Rect rect = GUILayoutUtility.GetRect(330, 250);
            DrawEditorContent(rect);
            HandleEditorInput(rect);
        }

        /// <summary>
        /// 大きな2Dエディタを描画（右パネル用）
        /// </summary>
        public void Draw2DEditorLarge()
        {
            if (_loops == null || _loops.Count == 0) return;

            // ヘッダーと選択点の座標編集
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(T("Editor2D"), EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                // 選択点の座標編集（コンパクト）
                if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
                {
                    var loop = _loops[_selectedLoopIndex];
                    if (_selectedPointIndex >= 0 && _selectedPointIndex < loop.Points.Count)
                    {
                        EditorGUILayout.LabelField(T("PtN", _selectedPointIndex), GUILayout.Width(30));
                        EditorGUI.BeginChangeCheck();
                        float newX = EditorGUILayout.FloatField(loop.Points[_selectedPointIndex].x, GUILayout.Width(60));
                        float newY = EditorGUILayout.FloatField(loop.Points[_selectedPointIndex].y, GUILayout.Width(60));
                        if (EditorGUI.EndChangeCheck())
                        {
                            loop.Points[_selectedPointIndex] = new Vector2(newX, newY);
                            OnRecordUndo?.Invoke(T("UndoEditPoint"));
                            OnLoopChanged?.Invoke();
                        }
                    }

                    // Remove Pointボタン
                    GUI.enabled = _selectedPointIndex >= 0 && loop.Points.Count > 3;
                    if (GUILayout.Button(T("Del"), GUILayout.Width(35)))
                    {
                        loop.Points.RemoveAt(_selectedPointIndex);
                        _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, loop.Points.Count - 1);
                        OnRecordUndo?.Invoke(T("UndoRemovePoint"));
                        OnLoopChanged?.Invoke();
                    }
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.LabelField(T("Editor2DHelp"), EditorStyles.miniLabel);

            // 2Dエディタ領域（大きく）
            Rect rect = GUILayoutUtility.GetRect(400, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawEditorContent(rect);
            HandleEditorInput(rect);
        }

        private void DrawEditorContent(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            UnityEditor_Handles.BeginGUI();

            // 背景
            UnityEditor_Handles.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f, 1f));

            if (_loops == null || _loops.Count == 0)
                return;


            // グリッド
            DrawEditorGrid(rect);

            // ループを描画
            for (int li = 0; li < _loops.Count; li++)
            {
                var loop = _loops[li];
                if (loop.Points.Count < 2)
                    continue;

                // 色: 選択=黄, 穴=赤, 外側=シアン
                Color lineColor;
                if (li == _selectedLoopIndex)
                    lineColor = Color.yellow;
                else if (loop.IsHole)
                    lineColor = new Color(1f, 0.3f, 0.3f);
                else
                    lineColor = Color.cyan;

                UnityEditor_Handles.color = lineColor;

                // 線を描画
                for (int i = 0; i < loop.Points.Count; i++)
                {
                    Vector2 p0 = WorldToScreen(loop.Points[i], rect);
                    Vector2 p1 = WorldToScreen(loop.Points[(i + 1) % loop.Points.Count], rect);

                    // ホバー中のエッジは太く緑でハイライト
                    if (li == _hoverEdgeLoop && i == _hoverEdgeIndex)
                    {
                        UnityEditor_Handles.color = Color.green;
                        UnityEditor_Handles.DrawAAPolyLine(4f, new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
                        UnityEditor_Handles.color = lineColor;
                    }
                    else
                    {
                        UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
                    }
                }

                // 点を描画
                for (int i = 0; i < loop.Points.Count; i++)
                {
                    Vector2 screenPos = WorldToScreen(loop.Points[i], rect);
                    Color pointColor = (li == _selectedLoopIndex && i == _selectedPointIndex) ? Color.white : lineColor;
                    UnityEditor_Handles.color = pointColor;
                    UnityEditor_Handles.DrawSolidDisc(new Vector3(screenPos.x, screenPos.y, 0), Vector3.forward, 4f);

                    // インデックス
                    if (li == _selectedLoopIndex)
                    {
                        GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 30, 20), i.ToString(), EditorStyles.miniLabel);
                    }
                }
            }

            UnityEditor_Handles.EndGUI();
        }

        private void DrawEditorGrid(Rect rect)
        {
            UnityEditor_Handles.color = new Color(0.3f, 0.3f, 0.35f, 1f);

            // グリッド線
            for (float x = -5f; x <= 5f; x += 0.5f)
            {
                Vector2 p0 = WorldToScreen(new Vector2(x, -5f), rect);
                Vector2 p1 = WorldToScreen(new Vector2(x, 5f), rect);
                if (p0.x >= rect.xMin && p0.x <= rect.xMax)
                    UnityEditor_Handles.DrawLine(new Vector3(p0.x, Mathf.Max(p0.y, rect.yMin)), new Vector3(p1.x, Mathf.Min(p1.y, rect.yMax)));
            }

            for (float y = -5f; y <= 5f; y += 0.5f)
            {
                Vector2 p0 = WorldToScreen(new Vector2(-5f, y), rect);
                Vector2 p1 = WorldToScreen(new Vector2(5f, y), rect);
                if (p0.y >= rect.yMin && p0.y <= rect.yMax)
                    UnityEditor_Handles.DrawLine(new Vector3(Mathf.Max(p0.x, rect.xMin), p0.y), new Vector3(Mathf.Min(p1.x, rect.xMax), p1.y));
            }

            // 軸線
            UnityEditor_Handles.color = new Color(0.5f, 0.5f, 0.55f, 1f);
            Vector2 axisX0 = WorldToScreen(new Vector2(-5f, 0f), rect);
            Vector2 axisX1 = WorldToScreen(new Vector2(5f, 0f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(axisX0.x, axisX0.y), new Vector3(axisX1.x, axisX1.y));

            Vector2 axisY0 = WorldToScreen(new Vector2(0f, -5f), rect);
            Vector2 axisY1 = WorldToScreen(new Vector2(0f, 5f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(axisY0.x, axisY0.y), new Vector3(axisY1.x, axisY1.y));
        }

        private void HandleEditorInput(Rect rect)
        {
            if (_loops == null) return;

            Event e = Event.current;

            if (!rect.Contains(e.mousePosition))
            {
                _hoverEdgeLoop = -1;
                _hoverEdgeIndex = -1;
                return;
            }

            Vector2 worldPos = ScreenToWorld(e.mousePosition, rect);
            UpdateHoverEdge(e.mousePosition, rect);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        // 1. 最も近い点を探す
                        int closestLoop = -1;
                        int closestPoint = -1;
                        float closestDist = 15f;

                        for (int li = 0; li < _loops.Count; li++)
                        {
                            var loop = _loops[li];
                            for (int pi = 0; pi < loop.Points.Count; pi++)
                            {
                                Vector2 screenPt = WorldToScreen(loop.Points[pi], rect);
                                float dist = Vector2.Distance(e.mousePosition, screenPt);
                                if (dist < closestDist)
                                {
                                    closestDist = dist;
                                    closestLoop = li;
                                    closestPoint = pi;
                                }
                            }
                        }

                        if (closestLoop >= 0)
                        {
                            // 頂点を選択・ドラッグ開始
                            _selectedLoopIndex = closestLoop;
                            _selectedPointIndex = closestPoint;
                            _dragPointIndex = closestPoint;
                            _isDragging = true;
                            _dragStartPos = _loops[closestLoop].Points[closestPoint];
                            e.Use();
                        }
                        else
                        {
                            // 2. 頂点が近くにない場合、エッジをチェック
                            int edgeLoop = -1;
                            int edgeIndex = -1;
                            float edgeDist = 10f;
                            Vector2 insertPoint = Vector2.zero;

                            for (int li = 0; li < _loops.Count; li++)
                            {
                                var loop = _loops[li];
                                if (loop.Points.Count < 2) continue;

                                for (int ei = 0; ei < loop.Points.Count; ei++)
                                {
                                    int nextIdx = (ei + 1) % loop.Points.Count;
                                    Vector2 p0Screen = WorldToScreen(loop.Points[ei], rect);
                                    Vector2 p1Screen = WorldToScreen(loop.Points[nextIdx], rect);

                                    float dist = DistanceToLineSegment(e.mousePosition, p0Screen, p1Screen, out Vector2 closestPt);
                                    if (dist < edgeDist)
                                    {
                                        edgeDist = dist;
                                        edgeLoop = li;
                                        edgeIndex = ei;
                                        insertPoint = ScreenToWorld(closestPt, rect);
                                    }
                                }
                            }

                            if (edgeLoop >= 0)
                            {
                                // エッジ上に頂点を挿入
                                int insertIndex = edgeIndex + 1;
                                _loops[edgeLoop].Points.Insert(insertIndex, insertPoint);
                                _selectedLoopIndex = edgeLoop;
                                _selectedPointIndex = insertIndex;
                                _dragPointIndex = insertIndex;
                                _isDragging = true;
                                _dragStartPos = insertPoint;
                                OnRecordUndo?.Invoke(T("UndoInsertPoint"));
                                OnLoopChanged?.Invoke();
                                e.Use();
                            }
                            else
                            {
                                _selectedPointIndex = -1;
                            }
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging && _dragPointIndex >= 0 && e.button == 0)
                    {
                        if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
                        {
                            _loops[_selectedLoopIndex].Points[_dragPointIndex] = worldPos;
                            OnLoopChanged?.Invoke();
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDragging && e.button == 0)
                    {
                        OnRecordUndo?.Invoke(T("UndoMovePoint"));
                        _isDragging = false;
                        _dragPointIndex = -1;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                    float zoomDelta = -e.delta.y * 0.05f;
                    _editorZoom = Mathf.Clamp(_editorZoom + zoomDelta, 0.2f, 5f);
                    e.Use();
                    break;
            }
        }

        private void UpdateHoverEdge(Vector2 mousePos, Rect rect)
        {
            if (_loops == null) return;

            int prevLoop = _hoverEdgeLoop;
            int prevIndex = _hoverEdgeIndex;

            _hoverEdgeLoop = -1;
            _hoverEdgeIndex = -1;

            // まず頂点が近くにあるかチェック
            for (int li = 0; li < _loops.Count; li++)
            {
                var loop = _loops[li];
                for (int pi = 0; pi < loop.Points.Count; pi++)
                {
                    Vector2 screenPt = WorldToScreen(loop.Points[pi], rect);
                    if (Vector2.Distance(mousePos, screenPt) < 15f)
                    {
                        return;
                    }
                }
            }

            // エッジをチェック
            float closestDist = 10f;
            for (int li = 0; li < _loops.Count; li++)
            {
                var loop = _loops[li];
                if (loop.Points.Count < 2) continue;

                for (int ei = 0; ei < loop.Points.Count; ei++)
                {
                    int nextIdx = (ei + 1) % loop.Points.Count;
                    Vector2 p0Screen = WorldToScreen(loop.Points[ei], rect);
                    Vector2 p1Screen = WorldToScreen(loop.Points[nextIdx], rect);

                    float dist = DistanceToLineSegment(mousePos, p0Screen, p1Screen, out _);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        _hoverEdgeLoop = li;
                        _hoverEdgeIndex = ei;
                    }
                }
            }
        }

        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd, out Vector2 closestPoint)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLength = line.magnitude;

            if (lineLength < 0.0001f)
            {
                closestPoint = lineStart;
                return Vector2.Distance(point, lineStart);
            }

            Vector2 lineDir = line / lineLength;
            float t = Vector2.Dot(point - lineStart, lineDir);
            t = Mathf.Clamp(t, 0f, lineLength);

            closestPoint = lineStart + lineDir * t;
            return Vector2.Distance(point, closestPoint);
        }

        private Vector2 WorldToScreen(Vector2 world, Rect rect)
        {
            float scale = Mathf.Min(rect.width, rect.height) * 0.4f * _editorZoom;
            Vector2 center = rect.center + _editorOffset;

            return new Vector2(
                center.x + world.x * scale,
                center.y - world.y * scale
            );
        }

        private Vector2 ScreenToWorld(Vector2 screen, Rect rect)
        {
            float scale = Mathf.Min(rect.width, rect.height) * 0.4f * _editorZoom;
            Vector2 center = rect.center + _editorOffset;

            return new Vector2(
                (screen.x - center.x) / scale,
                -(screen.y - center.y) / scale
            );
        }
    }
}
