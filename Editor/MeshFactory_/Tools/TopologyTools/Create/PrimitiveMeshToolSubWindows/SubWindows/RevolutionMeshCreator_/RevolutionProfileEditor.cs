// Assets/Editor/MeshCreators/Revolution/RevolutionProfileEditor.cs
// 回転体メッシュ用の2D断面プロファイルエディタ
// ローカライズ対応版

using static MeshFactory.Gizmo.GLGizmoDrawer;
using static MeshFactory.Revolution.RevolutionTexts;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshFactory.Revolution
{
    /// <summary>
    /// 2D断面プロファイルエディタ
    /// </summary>
    public class RevolutionProfileEditor
    {
        // 編集対象
        private List<Vector2> _profile;
        private int _selectedPointIndex = -1;

        // ドラッグ状態
        private int _dragPointIndex = -1;
        private bool _isDragging = false;
        private Vector2 _dragStartPos;

        // 表示設定
        private float _profileZoom = 1f;
        private Vector2 _profileOffset = Vector2.zero;

        // コールバック
        public Action OnProfileChanged;
        public Action<string> OnRecordUndo;

        // プロパティ
        public int SelectedPointIndex => _selectedPointIndex;
        public bool IsDragging => _isDragging;

        /// <summary>
        /// プロファイルを設定
        /// </summary>
        public void SetProfile(List<Vector2> profile)
        {
            _profile = profile;
        }

        /// <summary>
        /// 選択インデックスを設定
        /// </summary>
        public void SetSelectedIndex(int index)
        {
            _selectedPointIndex = index;
        }

        /// <summary>
        /// エディタUIを描画
        /// </summary>
        public void DrawEditor(bool closeLoop)
        {
            EditorGUILayout.LabelField(T("ProfileEditor"), EditorStyles.boldLabel);

            // ボタン行1
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("AddPoint")))
            {
                OnRecordUndo?.Invoke(T("UndoAddPoint"));
                AddProfilePoint();
            }
            if (GUILayout.Button(T("RemovePoint")) && _profile.Count > 2)
            {
                OnRecordUndo?.Invoke(T("UndoRemovePoint"));
                RemoveSelectedPoint();
            }
            if (GUILayout.Button(T("Reset")))
            {
                OnRecordUndo?.Invoke(T("UndoResetProfile"));
                ResetProfile();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 断面エディタ領域
            Rect editorRect = GUILayoutUtility.GetRect(340, 300, GUILayout.ExpandWidth(true));
            DrawProfileEditorArea(editorRect, closeLoop);

            EditorGUILayout.Space(5);

            // 選択中の点の座標編集
            DrawSelectedPointEditor();
        }

        /// <summary>
        /// CSVボタン行を描画（別メソッドとして分離）
        /// </summary>
        public void DrawCSVButtons(Action onLoad, Action onSave)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("LoadCSV")))
            {
                OnRecordUndo?.Invoke(T("UndoLoadCSV"));
                onLoad?.Invoke();
            }
            if (GUILayout.Button(T("SaveCSV")))
            {
                onSave?.Invoke();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProfileEditorArea(Rect rect, bool closeLoop)
        {
            UnityEditor_Handles.BeginGUI();

            // 背景
            UnityEditor_Handles.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f, 1f));

            // グリッド描画
            DrawProfileGrid(rect);

            // プロファイル線描画
            DrawProfileLines(rect, closeLoop);

            // 頂点描画
            DrawProfilePoints(rect);

            // 入力処理
            HandleProfileEditorInput(rect);
            UnityEditor_Handles.EndGUI();

        }

        private void DrawProfileGrid(Rect rect)
        {
            UnityEditor_Handles.color = new Color(0.3f, 0.3f, 0.35f, 1f);

            // 0.5刻みのグリッド
            for (float x = 0f; x <= 2f; x += 0.5f)
            {
                Vector2 p0 = ProfileToScreen(new Vector2(x, -1f), rect);
                Vector2 p1 = ProfileToScreen(new Vector2(x, 2f), rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }

            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                Vector2 p0 = ProfileToScreen(new Vector2(0f, y), rect);
                Vector2 p1 = ProfileToScreen(new Vector2(2f, y), rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }

            // 軸線
            UnityEditor_Handles.color = new Color(0.5f, 0.5f, 0.55f, 1f);
            Vector2 axisY0 = ProfileToScreen(new Vector2(0f, -1f), rect);
            Vector2 axisY1 = ProfileToScreen(new Vector2(0f, 2f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(axisY0.x, axisY0.y), new Vector3(axisY1.x, axisY1.y));

            Vector2 axisX0 = ProfileToScreen(new Vector2(0f, 0f), rect);
            Vector2 axisX1 = ProfileToScreen(new Vector2(2f, 0f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(axisX0.x, axisX0.y), new Vector3(axisX1.x, axisX1.y));
        }

        private void DrawProfileLines(Rect rect, bool closeLoop)
        {
            if (_profile == null || _profile.Count < 2) return;

            UnityEditor_Handles.color = Color.cyan;

            for (int i = 0; i < _profile.Count - 1; i++)
            {
                Vector2 p0 = ProfileToScreen(_profile[i], rect);
                Vector2 p1 = ProfileToScreen(_profile[i + 1], rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }

            // 閉じたループの場合、最後と最初を結ぶ
            if (closeLoop && _profile.Count >= 3)
            {
                Vector2 pLast = ProfileToScreen(_profile[_profile.Count - 1], rect);
                Vector2 pFirst = ProfileToScreen(_profile[0], rect);
                UnityEditor_Handles.DrawLine(new Vector3(pLast.x, pLast.y), new Vector3(pFirst.x, pFirst.y));
            }
        }

        private void DrawProfilePoints(Rect rect)
        {
            if (_profile == null) return;

            for (int i = 0; i < _profile.Count; i++)
            {
                Vector2 screenPos = ProfileToScreen(_profile[i], rect);

                // 選択状態で色を変える
                Color pointColor = (i == _selectedPointIndex) ? Color.yellow : Color.white;

                // 点を描画
                Rect pointRect = new Rect(screenPos.x - 5, screenPos.y - 5, 10, 10);
                UnityEditor_Handles.DrawRect(pointRect, pointColor);

                // インデックス表示
                GUI.Label(new Rect(screenPos.x + 8, screenPos.y - 8, 30, 20), i.ToString(), EditorStyles.miniLabel);
            }
        }

        private void HandleProfileEditorInput(Rect rect)
        {
            if (_profile == null) return;

            Event e = Event.current;

            if (!rect.Contains(e.mousePosition))
            {
                return;
            }

            Vector2 profilePos = ScreenToProfile(e.mousePosition, rect);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        int closestIndex = FindClosestProfilePoint(e.mousePosition, rect, 15f);

                        if (closestIndex >= 0)
                        {
                            _selectedPointIndex = closestIndex;
                            _dragPointIndex = closestIndex;
                            _isDragging = true;
                            _dragStartPos = _profile[closestIndex];
                            e.Use();
                        }
                        else
                        {
                            _selectedPointIndex = -1;
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging && _dragPointIndex >= 0 && e.button == 0)
                    {
                        profilePos.x = Mathf.Max(0, profilePos.x);
                        _profile[_dragPointIndex] = profilePos;
                        OnProfileChanged?.Invoke();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDragging && e.button == 0)
                    {
                        if (_dragPointIndex >= 0 && _dragStartPos != _profile[_dragPointIndex])
                        {
                            OnRecordUndo?.Invoke(T("UndoMovePoint"));
                        }
                        _isDragging = false;
                        _dragPointIndex = -1;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                    float zoomDelta = -e.delta.y * 0.05f;
                    _profileZoom = Mathf.Clamp(_profileZoom + zoomDelta, 0.5f, 3f);
                    e.Use();
                    break;
            }
        }

        private void DrawSelectedPointEditor()
        {
            if (_profile == null) return;

            if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count)
            {
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.LabelField(T("PointN", _selectedPointIndex), EditorStyles.miniBoldLabel);

                Vector2 point = _profile[_selectedPointIndex];
                float newX = EditorGUILayout.Slider(T("RadiusX"), point.x, 0f, 2f);
                float newY = EditorGUILayout.Slider(T("HeightY"), point.y, -1f, 2f);

                if (EditorGUI.EndChangeCheck())
                {
                    _profile[_selectedPointIndex] = new Vector2(newX, newY);
                    OnProfileChanged?.Invoke();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(T("ProfileHelp"), MessageType.Info);
            }
        }

        private int FindClosestProfilePoint(Vector2 screenPos, Rect rect, float maxDist)
        {
            int closest = -1;
            float closestDist = maxDist;

            for (int i = 0; i < _profile.Count; i++)
            {
                Vector2 pointScreen = ProfileToScreen(_profile[i], rect);
                float dist = Vector2.Distance(screenPos, pointScreen);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = i;
                }
            }

            return closest;
        }

        private Vector2 ProfileToScreen(Vector2 profilePos, Rect rect)
        {
            float profileRangeX = 2f;
            float profileRangeY = 3f;

            float scale = Mathf.Min(rect.width / profileRangeX, rect.height / profileRangeY) * _profileZoom;

            float usedWidth = profileRangeX * scale;
            float usedHeight = profileRangeY * scale;
            float offsetX = (rect.width - usedWidth) * 0.5f;
            float offsetY = (rect.height - usedHeight) * 0.5f;

            float screenX = rect.xMin + offsetX + profilePos.x * scale + _profileOffset.x;
            float screenY = rect.yMax - offsetY - (profilePos.y + 1f) * scale + _profileOffset.y;

            return new Vector2(screenX, screenY);
        }

        private Vector2 ScreenToProfile(Vector2 screenPos, Rect rect)
        {
            float profileRangeX = 2f;
            float profileRangeY = 3f;

            float scale = Mathf.Min(rect.width / profileRangeX, rect.height / profileRangeY) * _profileZoom;

            float usedWidth = profileRangeX * scale;
            float usedHeight = profileRangeY * scale;
            float offsetX = (rect.width - usedWidth) * 0.5f;
            float offsetY = (rect.height - usedHeight) * 0.5f;

            float profileX = (screenPos.x - rect.xMin - offsetX - _profileOffset.x) / scale;
            float profileY = (rect.yMax - offsetY - screenPos.y + _profileOffset.y) / scale - 1f;

            return new Vector2(profileX, profileY);
        }

        private void AddProfilePoint()
        {
            if (_profile == null) return;

            Vector2 newPoint;
            int insertIndex;

            if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count - 1)
            {
                newPoint = (_profile[_selectedPointIndex] + _profile[_selectedPointIndex + 1]) * 0.5f;
                insertIndex = _selectedPointIndex + 1;
            }
            else if (_profile.Count >= 2)
            {
                Vector2 lastDir = _profile[_profile.Count - 1] - _profile[_profile.Count - 2];
                newPoint = _profile[_profile.Count - 1] + lastDir.normalized * 0.2f;
                insertIndex = _profile.Count;
            }
            else
            {
                newPoint = new Vector2(0.5f, 0.5f);
                insertIndex = _profile.Count;
            }

            _profile.Insert(insertIndex, newPoint);
            _selectedPointIndex = insertIndex;
            OnProfileChanged?.Invoke();
        }

        private void RemoveSelectedPoint()
        {
            if (_profile == null) return;

            if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count && _profile.Count > 2)
            {
                _profile.RemoveAt(_selectedPointIndex);
                _selectedPointIndex = Mathf.Min(_selectedPointIndex, _profile.Count - 1);
                OnProfileChanged?.Invoke();
            }
        }

        private void ResetProfile()
        {
            if (_profile == null) return;

            _profile.Clear();
            var defaultProfile = RevolutionProfileGenerator.CreateDefault();
            foreach (var p in defaultProfile)
            {
                _profile.Add(p);
            }
            _selectedPointIndex = -1;
            OnProfileChanged?.Invoke();
        }
    }
}
