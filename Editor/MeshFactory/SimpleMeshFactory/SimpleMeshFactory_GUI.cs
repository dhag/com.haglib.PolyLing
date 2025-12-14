// Assets/Editor/SimpleMeshFactory.GUI.cs
// 左ペインUI描画（DrawMeshList、ツールバー）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Tools;
using MeshFactory.Selection;
using MeshFactory.Localization;

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
                    _undoController?.Undo();
                }
            }
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanRedo))
            {
                if (GUILayout.Button(L.Get("Redo")))
                {
                    _undoController?.Redo();
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
                bool newVertexEditMode = newShowVertices;

                if (EditorGUI.EndChangeCheck())
                {
                    bool hasDisplayChange =
                        newShowWireframe != _showWireframe ||
                        newShowVertices != _showVertices ||
                        newVertexEditMode != _vertexEditMode;

                    if (hasDisplayChange && _undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }

                    _showWireframe = newShowWireframe;
                    _showVertices = newShowVertices;
                    _vertexEditMode = newVertexEditMode;

                    if (_undoController != null)
                    {
                        _undoController.EditorState.ShowWireframe = _showWireframe;
                        _undoController.EditorState.ShowVertices = _showVertices;
                        _undoController.EditorState.VertexEditMode = _vertexEditMode;
                        _undoController.EndEditorStateDrag("Change Display Settings");
                    }
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(L.Get("Zoom"), EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newDist = EditorGUILayout.Slider(_cameraDistance, 0.1f, 10f);
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
            // Primitive セクション
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

                EditorGUILayout.Space(3);

                // Create UnityMesh
                EditorGUILayout.LabelField(L.Get("CreateMesh"), EditorStyles.miniBoldLabel);

                // ★追加モードUI（Undo対応）
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool newAddToCurrentMesh = EditorGUILayout.ToggleLeft(L.Get("AddToCurrent"), _addToCurrentMesh, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck() && newAddToCurrentMesh != _addToCurrentMesh)
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }
                    _addToCurrentMesh = newAddToCurrentMesh;
                    if (_undoController != null)
                    {
                        _undoController.EditorState.AddToCurrentMesh = _addToCurrentMesh;
                        _undoController.EndEditorStateDrag("Toggle Add to Current");
                    }
                }

                // 追加先がない場合は警告
                if (_addToCurrentMesh && !_model.HasValidSelection)
                {
                    EditorGUILayout.LabelField(L.Get("NoMeshSelected"), EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                // 自動マージUI（Undo対応）
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool newAutoMergeOnCreate = EditorGUILayout.ToggleLeft(L.Get("AutoMerge"), _autoMergeOnCreate, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck() && newAutoMergeOnCreate != _autoMergeOnCreate)
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }
                    _autoMergeOnCreate = newAutoMergeOnCreate;
                    if (_undoController != null)
                    {
                        _undoController.EditorState.AutoMergeOnCreate = _autoMergeOnCreate;
                        _undoController.EndEditorStateDrag("Toggle Auto Merge");
                    }
                }
                EditorGUI.BeginDisabledGroup(!_autoMergeOnCreate);
                EditorGUI.BeginChangeCheck();
                float newAutoMergeThreshold = EditorGUILayout.FloatField(_autoMergeThreshold, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newAutoMergeThreshold, _autoMergeThreshold))
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }
                    _autoMergeThreshold = newAutoMergeThreshold;
                    if (_undoController != null)
                    {
                        _undoController.EditorState.AutoMergeThreshold = _autoMergeThreshold;
                        _undoController.EndEditorStateDrag("Change Auto Merge Threshold");
                    }
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                // 生成用のボタン（+ Cube... など）
                if (GUILayout.Button("+ Cube..."))
                {
                    CubeMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Plane..."))
                {
                    PlaneMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Pyramid..."))
                {
                    PyramidMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Capsule..."))
                {
                    CapsuleMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Cylinder..."))
                {
                    CylinderMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Sphere..."))
                {
                    SphereMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Revolution..."))
                {
                    RevolutionMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ 2D Profile..."))
                {
                    Profile2DExtrudeWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ NohMask..."))
                {
                    NohMaskMeshCreatorWindow.Open(OnMeshDataCreated);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Selection セクション（編集モード時のみ）
            // ================================================================
            if (_vertexEditMode)
            {
                _undoController?.FocusVertexEdit();

                _foldSelection = DrawFoldoutWithUndo("Selection", L.Get("Selection"), true);
                if (_foldSelection)
                {
                    EditorGUI.indentLevel++;

                    // === 選択モード切り替え ===
                    DrawSelectionModeToolbar();

                    int totalVertices = 0;

                    var meshContext = _model.CurrentMeshContext;
                    if (meshContext?.Data != null)
                    {
                        totalVertices = meshContext.Data.VertexCount;
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
                // Tool Windows セクション（Phase 4追加）
                // ================================================================
                DrawToolWindowsSection();

                EditorGUILayout.Space(3);

                // ================================================================
                // Work Plane セクション
                // ================================================================
                // WorkPlane UIは内部でFoldout管理
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
            }
            else
            {
                _undoController?.FocusView();
            }

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
                    _selectedIndex = i;
                    _selectedVertices.Clear();
                    ResetEditState();
                    InitVertexOffsets();

                    var meshContext = _meshContextList[_selectedIndex];
                    LoadMeshContextToUndoController(meshContext);
                    UpdateTopology();  // 
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

        var oldSnapshot = _selectionState.CreateSnapshot();
        var oldLegacySelection = new HashSet<int>(_selectedVertices);

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

        var oldSnapshot = _selectionState.CreateSnapshot();
        var oldLegacySelection = new HashSet<int>(_selectedVertices);

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
        _undoController.SetMeshData(meshContext.Data, meshContext.UnityMesh);
        _undoController.MeshContext.OriginalPositions = meshContext.OriginalPositions;
        // 選択状態を同期
        _undoController.MeshContext.SelectedVertices = new HashSet<int>(_selectedVertices);
    }
}