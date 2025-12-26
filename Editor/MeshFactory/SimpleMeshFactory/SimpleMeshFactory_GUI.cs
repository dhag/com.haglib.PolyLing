// Assets/Editor/SimpleMeshFactory.GUI.cs
// 左ペインUI描画（DrawMeshList、ツールバー）
// Phase 4: 図形生成ボタンをPrimitiveMeshToolに移動

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Tools;
using MeshFactory.Selection;
using MeshFactory.Localization;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public partial class SimpleMeshFactory
{
    // ================================================================
    // 左ペイン：メッシュリスト
    // ================================================================
    private void DrawMeshList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_leftPaneWidth)))
        {
            EditorGUILayout.LabelField("UnityMesh Factory", EditorStyles.boldLabel);

            // ================================================================
            // Undo/Redo ボタン（上部固定）
            // ================================================================
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanUndo))
            {
                if (GUILayout.Button(L.Get("Undo")))
                {
                    if (_undoController?.Undo() == true)
                    {
                        Repaint();
                    }
                }
            }
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanRedo))
            {
                if (GUILayout.Button(L.Get("Redo")))
                {
                    if (_undoController?.Redo() == true)
                    {
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // ================================================================
            // スクロール領域開始（常にスクロールバー表示）
            // ================================================================
            _leftPaneScroll = EditorGUILayout.BeginScrollView(
                _leftPaneScroll,
                true,//false,  // horizontal
                true,   // vertical - 常に表示
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUI.skin.scrollView);

            // ================================================================
            // Display セクション
            // ================================================================
            _foldDisplay = DrawFoldoutWithUndo("Display", L.Get("Display"), true);
            if (_foldDisplay)
            {
                EditorGUI.indentLevel++;

                EditorGUI.BeginChangeCheck();
                bool newShowWireframe = EditorGUILayout.Toggle(L.Get("Wireframe"), _showWireframe);
                bool newShowVertices = EditorGUILayout.Toggle(L.Get("ShowVertices"), _showVertices);
                bool newShowVertexIndices = EditorGUILayout.Toggle(L.Get("ShowVertexIndices"), _showVertexIndices);  // ★追加
                bool newShowSelectedMeshOnly = EditorGUILayout.Toggle(L.Get("ShowSelectedMeshOnly"), _showSelectedMeshOnly);  // ★追加
                //bool newVertexEditMode = newShowVertices;

                if (EditorGUI.EndChangeCheck())
                {
                    bool hasDisplayChange =
                        newShowWireframe != _showWireframe ||
                        newShowVertices != _showVertices ||
                        newShowVertexIndices != _showVertexIndices ||
                        newShowSelectedMeshOnly != _showSelectedMeshOnly;// ||
                                                                         //newVertexEditMode != _vertexEditMode;

                    if (hasDisplayChange && _undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }

                    _showWireframe = newShowWireframe;
                    _showVertices = newShowVertices;
                    _showVertexIndices = newShowVertexIndices;
                    _showSelectedMeshOnly = newShowSelectedMeshOnly;
                    //_vertexEditMode = newVertexEditMode;

                    if (_undoController != null)
                    {
                        _undoController.EditorState.ShowWireframe = _showWireframe;
                        _undoController.EditorState.ShowVertices = _showVertices;
                        _undoController.EditorState.ShowSelectedMeshOnly = _showSelectedMeshOnly;
                        _undoController.EditorState.ShowVertexIndices = _showVertexIndices;
                        //_undoController.EditorState.VertexEditMode = _vertexEditMode;
                        _undoController.EndEditorStateDrag("Change Display Settings");
                    }
                }

                // === カリングA 設定（分離） ===
                EditorGUI.BeginChangeCheck();
                bool currentCulling = _undoController?.EditorState.BackfaceCullingEnabled ?? true;
                bool newCulling = EditorGUILayout.Toggle(L.Get("BackfaceCulling"), currentCulling);
                if (EditorGUI.EndChangeCheck())
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                        _undoController.EditorState.BackfaceCullingEnabled = newCulling;  // ★ここで保存
                        _undoController.EndEditorStateDrag("Toggle Backface Culling");
                    }

                    // GPUレンダラーに反映
                    if (_gpuRenderer != null)
                    {
                        _gpuRenderer.CullingEnabled = newCulling;
                    }

                    Repaint();  // ★追加
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(L.Get("Zoom"), EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newDist = EditorGUILayout.Slider(_cameraDistance, 0.1f, 40f);//スライダーの上限下限：ズーム
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newDist, _cameraDistance))
                {
                    if (!_isCameraDragging) BeginCameraDrag();
                    _cameraDistance = newDist;
                }

                EditorGUILayout.Space(3);

                // ★対称モードUI
                DrawSymmetryUI();

                EditorGUILayout.Space(3);

                // 言語設定
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(L.Get("Language"), GUILayout.Width(60));
                EditorGUI.BeginChangeCheck();
                var newLang = (Language)EditorGUILayout.EnumPopup(L.CurrentLanguage);
                if (EditorGUI.EndChangeCheck())
                {
                    L.CurrentLanguage = newLang;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                // Foldout Undo記録設定
                if (_undoController != null)
                {
                    bool recordFoldout = _undoController.EditorState.RecordFoldoutChanges;
                    EditorGUI.BeginChangeCheck();
                    bool newRecordFoldout = EditorGUILayout.Toggle(L.Get("UndoFoldout"), recordFoldout);
                    if (EditorGUI.EndChangeCheck() && newRecordFoldout != recordFoldout)
                    {
                        _undoController.EditorState.RecordFoldoutChanges = newRecordFoldout;
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Primitive セクション（図形生成ボタンはPrimitiveMeshToolに移動）
            // ================================================================
            _foldPrimitive = DrawFoldoutWithUndo("Primitive", L.Get("Primitive"), true);
            if (_foldPrimitive)
            {
                EditorGUI.indentLevel++;

                // Empty UnityMesh
                if (GUILayout.Button(L.Get("EmptyMesh")))
                {
                    CreateEmptyMesh();
                }

                // Clear All
                if (GUILayout.Button(L.Get("ClearAll")))
                {
                    CleanupMeshes();
                    _selectedIndex = -1;
                    _vertexOffsets = null;
                    _groupOffsets = null;
                    _undoController?.VertexEditStack.Clear();
                }

                EditorGUILayout.Space(3);

                // Load UnityMesh
                EditorGUILayout.LabelField(L.Get("LoadMesh"), EditorStyles.miniBoldLabel);
                if (GUILayout.Button(L.Get("FromAsset")))
                {
                    LoadMeshFromAsset();
                }
                if (GUILayout.Button(L.Get("FromPrefab")))
                {
                    LoadMeshFromPrefab();
                }
                if (GUILayout.Button(L.Get("FromSelection")))
                {
                    LoadMeshFromSelection();
                }

                // ================================================================
                // 図形生成ボタンは削除（PrimitiveMeshToolに移動）
                // Toolsセクションで「Primitive」ツールを選択すると表示される
                // ================================================================

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Selection セクション（編集モード時のみ）
            // ================================================================
            //if (_vertexEditMode)
            //{
            _undoController?.FocusVertexEdit();

            _foldSelection = DrawFoldoutWithUndo("Selection", L.Get("Selection"), true);
            if (_foldSelection)
            {
                EditorGUI.indentLevel++;

                // === 選択モード切り替え ===
                DrawSelectionModeToolbar();

                int totalVertices = 0;

                var meshContext = _model.CurrentMeshContext;
                if (meshContext?.MeshObject != null)
                {
                    totalVertices = meshContext.MeshObject.VertexCount;
                }

                EditorGUILayout.LabelField(L.GetSelectedCount(_selectionState.SelectionCount, totalVertices), EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.Get("All"), GUILayout.Width(40)))
                    {
                        SelectAllVertices();
                    }
                    if (GUILayout.Button(L.Get("None"), GUILayout.Width(40)))
                    {
                        ClearSelection();
                    }
                    if (GUILayout.Button(L.Get("Invert"), GUILayout.Width(50)))
                    {
                        InvertSelection();
                    }
                }

                // 削除ボタン（選択があるときのみ有効）
                using (new EditorGUI.DisabledScope(_selectedVertices.Count == 0))
                {
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.6f); // 薄い赤
                    if (GUILayout.Button(L.Get("DeleteSelected")))
                    {
                        DeleteSelectedVertices();
                    }
                    GUI.backgroundColor = oldColor;
                }

                // マージボタン（2つ以上選択があるときのみ有効）
                using (new EditorGUI.DisabledScope(_selectedVertices.Count < 2))
                {
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.6f, 0.8f, 1f); // 薄い青
                    if (GUILayout.Button("Merge Selected"))
                    {
                        MergeSelectedVertices();
                    }
                    GUI.backgroundColor = oldColor;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Tools セクション
            // ================================================================
            DrawToolsSection();

            EditorGUILayout.Space(3);

            // ================================================================
            // Tool Panel セクション（Phase 4追加）
            // ================================================================
            DrawToolPanelsSection();

            EditorGUILayout.Space(3);

            // ================================================================
            // Work Plane セクション
            // ================================================================
            // WorkPlaneContext UIは内部でFoldout管理
            DrawWorkPlaneUI();

            // ギズモ表示トグル（WorkPlane展開時のみ表示）
            if (_undoController?.WorkPlane?.IsExpanded == true)
            {
                EditorGUI.BeginChangeCheck();
                _showWorkPlaneGizmo = EditorGUILayout.ToggleLeft("Show Gizmo", _showWorkPlaneGizmo);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
            }
            //    }
            //    else
            //    {
            //        _undoController?.FocusView();
            //    }

            EditorGUILayout.Space(5);

            // ================================================================
            // メッシュリスト
            // ================================================================
            EditorGUILayout.LabelField("UnityMesh List", EditorStyles.miniBoldLabel);

            for (int i = 0; i < _meshContextList.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                bool isSelected = (i == _selectedIndex);
                bool newSelected = GUILayout.Toggle(isSelected, _meshContextList[i].Name, "Button");

                if (newSelected && !isSelected)
                {
                    int oldIndex = _selectedIndex;

                    // 選択前のカメラ状態をキャプチャ
                    CameraSnapshot oldCamera = new CameraSnapshot
                    {
                        RotationX = _rotationX,
                        RotationY = _rotationY,
                        CameraDistance = _cameraDistance,
                        CameraTarget = _cameraTarget
                    };

                    _selectedIndex = i;
                    _selectedVertices.Clear();
                    ResetEditState();
                    InitVertexOffsets();  // カメラがフィット

                    MeshContext meshContext = _meshContextList[_selectedIndex];
                    LoadMeshContextToUndoController(meshContext);
                    UpdateTopology();

                    // 選択後のカメラ状態をキャプチャ
                    CameraSnapshot newCamera = new CameraSnapshot
                    {
                        RotationX = _rotationX,
                        RotationY = _rotationY,
                        CameraDistance = _cameraDistance,
                        CameraTarget = _cameraTarget
                    };

                    Debug.Log($"[SelectMesh] oldCamera: rotX={oldCamera.RotationX}, rotY={oldCamera.RotationY}, dist={oldCamera.CameraDistance}, target={oldCamera.CameraTarget}");
                    Debug.Log($"[SelectMesh] newCamera: rotX={newCamera.RotationX}, rotY={newCamera.RotationY}, dist={newCamera.CameraDistance}, target={newCamera.CameraTarget}");

                    // メッシュ選択変更をUndo記録（カメラ状態付き）
                    _undoController?.RecordMeshSelectionChange(oldIndex, _selectedIndex, oldCamera, newCamera);
                }

                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    EditorGUILayout.EndHorizontal();
                    RemoveMesh(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// 選択モード切り替えツールバーを描画（複数選択可能なトグル形式）
    /// </summary>
    private void DrawSelectionModeToolbar()
    {
        if (_selectionState == null) return;

        EditorGUILayout.BeginHorizontal();

        var mode = _selectionState.Mode;
        var buttonStyle = EditorStyles.miniButton;
        var oldColor = GUI.backgroundColor;

        // Vertex モード（トグル）
        bool vertexOn = mode.Has(MeshSelectMode.Vertex);
        if (vertexOn) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("V", buttonStyle, GUILayout.Width(28)))
        {
            ToggleSelectionMode(MeshSelectMode.Vertex);
        }
        GUI.backgroundColor = oldColor;

        // Edge モード（トグル）
        bool edgeOn = mode.Has(MeshSelectMode.Edge);
        if (edgeOn) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("E", buttonStyle, GUILayout.Width(28)))
        {
            ToggleSelectionMode(MeshSelectMode.Edge);
        }
        GUI.backgroundColor = oldColor;

        // Face モード（トグル）
        bool faceOn = mode.Has(MeshSelectMode.Face);
        if (faceOn) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("F", buttonStyle, GUILayout.Width(28)))
        {
            ToggleSelectionMode(MeshSelectMode.Face);
        }
        GUI.backgroundColor = oldColor;

        // Line モード（トグル）
        bool lineOn = mode.Has(MeshSelectMode.Line);
        if (lineOn) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("L", buttonStyle, GUILayout.Width(28)))
        {
            ToggleSelectionMode(MeshSelectMode.Line);
        }
        GUI.backgroundColor = oldColor;

        // 有効モード数表示
        int modeCount = mode.Count();
        EditorGUILayout.LabelField($"({modeCount})", EditorStyles.miniLabel, GUILayout.Width(24));

        // デバッグ情報
        string debugInfo = $"V:{_selectionState.Vertices.Count} E:{_selectionState.Edges.Count} F:{_selectionState.Faces.Count} L:{_selectionState.Lines.Count}";
        EditorGUILayout.LabelField(debugInfo, EditorStyles.miniLabel, GUILayout.Width(120));

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 選択モードをトグル（Undo対応）
    /// </summary>
    private void ToggleSelectionMode(MeshSelectMode toggleMode)
    {
        if (_selectionState == null) return;

        SelectionSnapshot oldSnapshot = _selectionState.CreateSnapshot();
        HashSet<int> oldLegacySelection = new HashSet<int>(_selectedVertices);

        // 現在のモードにフラグをトグル
        if (_selectionState.Mode.Has(toggleMode))
        {
            // OFFにする（最低1つは残す）
            var newMode = _selectionState.Mode & ~toggleMode;
            if (newMode == MeshSelectMode.None)
            {
                // 全てOFFになるならVertexに戻す
                newMode = MeshSelectMode.Vertex;
            }
            _selectionState.Mode = newMode;
        }
        else
        {
            // ONにする
            _selectionState.Mode |= toggleMode;
        }

        // Undo記録
        RecordExtendedSelectionChange(oldSnapshot, oldLegacySelection);
    }

    /// <summary>
    /// 選択モードを変更（Undo対応）- 後方互換
    /// </summary>
    private void SetSelectionMode(MeshSelectMode newMode)
    {
        if (_selectionState == null) return;
        if (_selectionState.Mode == newMode) return;

        SelectionSnapshot oldSnapshot = _selectionState.CreateSnapshot();
        HashSet<int> oldLegacySelection = new HashSet<int>(_selectedVertices);

        _selectionState.Mode = newMode;

        // Undo記録
        RecordExtendedSelectionChange(oldSnapshot, oldLegacySelection);
    }
    /*
    /// <summary>
    /// ツールボタンを描画（トグル形式）
    /// </summary>
    private void DrawToolButton(IEditTool tool, string label)
    {
        bool isActive = (_currentTool == tool);

        // アクティブなツールは色を変える
        var oldColor = GUI.backgroundColor;
        if (isActive)
        {
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        }

        if (GUILayout.Toggle(isActive, label, "Button") && !isActive)
        {
            // ツール変更をUndo記録
            if (_undoController != null)
            {
                string oldToolName = _currentTool?.Name ?? "Select";
                _undoController.EditorState.CurrentToolName = oldToolName;
                _undoController.BeginEditorStateDrag();
            }

            _currentTool?.OnDeactivate(_toolContext);
            _currentTool = tool;
            _currentTool?.OnActivate(_toolContext);

            // 新しいツール名を記録
            if (_undoController != null)
            {
                _undoController.EditorState.CurrentToolName = tool.Name;
                _undoController.EndEditorStateDrag($"Switch to {tool.Name} Tool");
            }
        }

        GUI.backgroundColor = oldColor;
    }
    */
    /// <summary>
    /// MeshContextをUndoコントローラーに読み込む
    /// </summary>
    private void LoadMeshContextToUndoController(MeshContext meshContext)
    {
        if (_undoController == null || meshContext == null)
            return;

        // 参照を共有（Cloneしない）- AddFaceToolなどで直接変更されるため
        // 注意: SetMeshObjectは呼ばない（_vertexEditStack.Clear()を避けるため）
        _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
        _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
        _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
        // Materials は ModelContext に集約済み
        // 選択状態を同期
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectedVertices);
    }
}