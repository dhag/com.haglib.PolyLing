// RevolutionProfileEditor.cs
// 回転体メッシュ用 2D 断面プロファイルエディタ（IMGUI / UnityEditor.Handles）
// データ操作・座標変換は RevolutionProfileEditCore に委譲。

using static Poly_Ling.Gizmo.GLGizmoDrawer;
using static Poly_Ling.Revolution.RevolutionTexts;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Revolution
{
    /// <summary>2D 断面プロファイルエディタ（IMGUI 描画 / 入力）</summary>
    public class RevolutionProfileEditor
    {
        // 編集対象
        private List<Vector2> _profile;
        private int _selectedPointIndex = -1;

        // ドラッグ状態
        private int     _dragPointIndex = -1;
        private bool    _isDragging     = false;
        private Vector2 _dragStartPos;

        // 表示設定
        private float   _profileZoom   = 1f;
        private Vector2 _profileOffset = Vector2.zero;

        // コールバック
        public Action         OnProfileChanged;
        public Action<string> OnRecordUndo;

        // プロパティ
        public int  SelectedPointIndex => _selectedPointIndex;
        public bool IsDragging         => _isDragging;

        public void SetProfile(List<Vector2> profile)  => _profile = profile;
        public void SetSelectedIndex(int index)         => _selectedPointIndex = index;

        // ================================================================
        // 公開 UI
        // ================================================================

        public void DrawEditor(bool closeLoop)
        {
            EditorGUILayout.LabelField(T("ProfileEditor"), EditorStyles.boldLabel);

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

            Rect editorRect = GUILayoutUtility.GetRect(340, 300, GUILayout.ExpandWidth(true));
            DrawProfileEditorArea(editorRect, closeLoop);

            EditorGUILayout.Space(5);
            DrawSelectedPointEditor();
        }

        public void DrawCSVButtons(Action onLoad, Action onSave)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("LoadCSV")))
            {
                OnRecordUndo?.Invoke(T("UndoLoadCSV"));
                onLoad?.Invoke();
            }
            if (GUILayout.Button(T("SaveCSV")))
                onSave?.Invoke();
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // IMGUI 描画（変更なし）
        // ================================================================

        private void DrawProfileEditorArea(Rect rect, bool closeLoop)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f, 1f));
            DrawProfileGrid(rect);
            DrawProfileLines(rect, closeLoop);
            DrawProfilePoints(rect);
            HandleProfileEditorInput(rect);
            UnityEditor_Handles.EndGUI();
        }

        private void DrawProfileGrid(Rect rect)
        {
            UnityEditor_Handles.color = new Color(0.3f, 0.3f, 0.35f, 1f);

            for (float x = 0f; x <= 2f; x += 0.5f)
            {
                Vector2 p0 = ProfileToScreen(new Vector2(x, -1f), rect);
                Vector2 p1 = ProfileToScreen(new Vector2(x,  2f), rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                Vector2 p0 = ProfileToScreen(new Vector2(0f, y), rect);
                Vector2 p1 = ProfileToScreen(new Vector2(2f, y), rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }

            UnityEditor_Handles.color = new Color(0.5f, 0.5f, 0.55f, 1f);
            Vector2 ay0 = ProfileToScreen(new Vector2(0f, -1f), rect);
            Vector2 ay1 = ProfileToScreen(new Vector2(0f,  2f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(ay0.x, ay0.y), new Vector3(ay1.x, ay1.y));
            Vector2 ax0 = ProfileToScreen(new Vector2(0f, 0f), rect);
            Vector2 ax1 = ProfileToScreen(new Vector2(2f, 0f), rect);
            UnityEditor_Handles.DrawLine(new Vector3(ax0.x, ax0.y), new Vector3(ax1.x, ax1.y));
        }

        private void DrawProfileLines(Rect rect, bool closeLoop)
        {
            if (_profile == null || _profile.Count < 2) return;

            UnityEditor_Handles.color = Color.cyan;
            for (int i = 0; i < _profile.Count - 1; i++)
            {
                Vector2 p0 = ProfileToScreen(_profile[i],     rect);
                Vector2 p1 = ProfileToScreen(_profile[i + 1], rect);
                UnityEditor_Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }
            if (closeLoop && _profile.Count >= 3)
            {
                Vector2 pL = ProfileToScreen(_profile[_profile.Count - 1], rect);
                Vector2 p0 = ProfileToScreen(_profile[0],                  rect);
                UnityEditor_Handles.DrawLine(new Vector3(pL.x, pL.y), new Vector3(p0.x, p0.y));
            }
        }

        private void DrawProfilePoints(Rect rect)
        {
            if (_profile == null) return;
            for (int i = 0; i < _profile.Count; i++)
            {
                Vector2 sp    = ProfileToScreen(_profile[i], rect);
                Color   color = (i == _selectedPointIndex) ? Color.yellow : Color.white;
                Rect    pr    = new Rect(sp.x - 5, sp.y - 5, 10, 10);
                UnityEditor_Handles.DrawRect(pr, color);
                GUI.Label(new Rect(sp.x + 8, sp.y - 8, 30, 20), i.ToString(), EditorStyles.miniLabel);
            }
        }

        private void HandleProfileEditorInput(Rect rect)
        {
            if (_profile == null) return;
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            Vector2 profilePos = ScreenToProfile(e.mousePosition, rect);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        int idx = FindClosestProfilePoint(e.mousePosition, rect, 15f);
                        if (idx >= 0)
                        {
                            _selectedPointIndex = idx;
                            _dragPointIndex     = idx;
                            _isDragging         = true;
                            _dragStartPos       = _profile[idx];
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
                            OnRecordUndo?.Invoke(T("UndoMovePoint"));
                        _isDragging     = false;
                        _dragPointIndex = -1;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                    _profileZoom = Mathf.Clamp(_profileZoom - e.delta.y * 0.05f, 0.5f, 3f);
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
                Vector2 pt   = _profile[_selectedPointIndex];
                float   newX = EditorGUILayout.Slider(T("RadiusX"), pt.x, 0f,  2f);
                float   newY = EditorGUILayout.Slider(T("HeightY"), pt.y, -1f, 2f);
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

        // ================================================================
        // 座標変換 → RevolutionProfileEditCore に委譲
        // ================================================================

        private Vector2 ProfileToScreen(Vector2 profilePos, Rect rect)
        {
            Vector2 cp = RevolutionProfileEditCore.ProfileToCanvas(
                profilePos, rect.width, rect.height, _profileZoom, _profileOffset);
            return new Vector2(rect.xMin + cp.x, rect.yMin + cp.y);
        }

        private Vector2 ScreenToProfile(Vector2 screenPos, Rect rect)
        {
            Vector2 cp = new Vector2(screenPos.x - rect.xMin, screenPos.y - rect.yMin);
            return RevolutionProfileEditCore.CanvasToProfile(
                cp, rect.width, rect.height, _profileZoom, _profileOffset);
        }

        private int FindClosestProfilePoint(Vector2 screenPos, Rect rect, float maxDist)
        {
            Vector2 cp = new Vector2(screenPos.x - rect.xMin, screenPos.y - rect.yMin);
            return RevolutionProfileEditCore.FindClosest(
                _profile, cp, rect.width, rect.height, maxDist, _profileZoom, _profileOffset);
        }

        // ================================================================
        // データ操作 → RevolutionProfileEditCore に委譲
        // ================================================================

        private void AddProfilePoint()
        {
            if (_profile == null) return;
            RevolutionProfileEditCore.AddPoint(_profile, ref _selectedPointIndex);
            OnProfileChanged?.Invoke();
        }

        private void RemoveSelectedPoint()
        {
            if (_profile == null) return;
            RevolutionProfileEditCore.RemovePoint(_profile, ref _selectedPointIndex);
            OnProfileChanged?.Invoke();
        }

        private void ResetProfile()
        {
            if (_profile == null) return;
            RevolutionProfileEditCore.ResetProfile(_profile, ref _selectedPointIndex);
            OnProfileChanged?.Invoke();
        }
    }
}
