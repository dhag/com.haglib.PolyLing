// Assets/Editor/PolyLing.GUI.cs
// 左ペインUI描画（DrawMeshList、ツールバー）
// Phase 4: 図形生成ボタンをPrimitiveMeshToolに移動

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Localization;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Commands;

public partial class PolyLing
{
    // ================================================================
    // 左ペイン：メッシュリスト
    // ================================================================
    private void DrawMeshList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_leftPaneWidth)))
        {
              EditorGUILayout.LabelField("UnityMesh Factory", EditorStyles.boldLabel);
       
              // ★Phase 2: モデル選択UI
              DrawModelSelector();
        
        // ================================================================
        // Undo/Redo ボタン（上部固定）
        // ================================================================
        EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanUndo))
            {
                if (GUILayout.Button(L.Get("Undo")))
                {
                    _commandQueue?.Enqueue(new UndoCommand(_undoController, null));
                }
            }
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanRedo))
            {
                if (GUILayout.Button(L.Get("Redo")))
                {
                    _commandQueue?.Enqueue(new RedoCommand(_undoController, null));
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            DrawSelectionSetsUI();

            // ================================================================
            // キャプチャボタン
            // ================================================================
            if (GUILayout.Button("📷 Capture Preview"))
            {
                _captureRequested = true;
                Repaint();
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // モデル保存/読み込み（常に表示）
            // ================================================================
            EditorGUILayout.LabelField(L.Get("ModelFile"), EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(L.Get("ExportModel")))
            {
                ExportModel();
            }
            if (GUILayout.Button(L.Get("ImportModel")))
            {
                ImportModel();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(L.Get("ExportModelCsv")))
            {
                ExportModelCsv();
            }
            if (GUILayout.Button(L.Get("ImportModelCsv")))
            {
                ImportModelCsv();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(L.Get("MergeModelCsv")))
            {
                MergeModelCsv();
            }

            EditorGUILayout.Space(3);

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
                
                // メッシュ表示
                bool newShowMesh = EditorGUILayout.Toggle(L.Get("ShowMesh"), _showMesh);
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(!newShowMesh);
                bool newShowSelectedMeshOnly = !EditorGUILayout.Toggle(L.Get("ShowUnselected"), !_showSelectedMeshOnly);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
                
                // ワイヤフレーム表示
                bool newShowWireframe = EditorGUILayout.Toggle(L.Get("Wireframe"), _showWireframe);
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(!newShowWireframe);
                bool newShowUnselectedWireframe = EditorGUILayout.Toggle(L.Get("ShowUnselected"), _showUnselectedWireframe);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
                
                // 頂点表示
                bool newShowVertices = EditorGUILayout.Toggle(L.Get("ShowVertices"), _showVertices);
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(!newShowVertices);
                bool newShowUnselectedVertices = EditorGUILayout.Toggle(L.Get("ShowUnselected"), _showUnselectedVertices);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
                
                // 頂点インデックス（選択メッシュのみ）
                bool newShowVertexIndices = EditorGUILayout.Toggle(L.Get("ShowVertexIndices"), _showVertexIndices);

                // ボーン表示
                bool newShowBones = EditorGUILayout.Toggle(L.Get("ShowBones"), _showBones);
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(!newShowBones);
                bool newShowUnselectedBones = EditorGUILayout.Toggle(L.Get("ShowUnselected"), _showUnselectedBones);
                bool newBoneDisplayAlongY = EditorGUILayout.Toggle(L.Get("BoneAlongY"), _boneDisplayAlongY);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;

                if (EditorGUI.EndChangeCheck())
                {
                    bool hasDisplayChange =
                        newShowMesh != _showMesh ||
                        newShowWireframe != _showWireframe ||
                        newShowVertices != _showVertices ||
                        newShowVertexIndices != _showVertexIndices ||
                        newShowSelectedMeshOnly != _showSelectedMeshOnly ||
                        newShowUnselectedVertices != _showUnselectedVertices ||
                        newShowUnselectedWireframe != _showUnselectedWireframe ||
                        newShowBones != _showBones ||
                        newShowUnselectedBones != _showUnselectedBones ||
                        newBoneDisplayAlongY != _boneDisplayAlongY;

                    if (hasDisplayChange && _undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }

                    // Single Source of Truth: プロパティ経由でEditorStateに直接書き込み
                    _showMesh = newShowMesh;
                    _showWireframe = newShowWireframe;
                    _showVertices = newShowVertices;
                    _showVertexIndices = newShowVertexIndices;
                    _showSelectedMeshOnly = newShowSelectedMeshOnly;
                    _showUnselectedVertices = newShowUnselectedVertices;
                    _showUnselectedWireframe = newShowUnselectedWireframe;
                    _showBones = newShowBones;
                    _showUnselectedBones = newShowUnselectedBones;
                    _boneDisplayAlongY = newBoneDisplayAlongY;

                    if (_undoController != null)
                    {
                        // プロパティ経由で既にEditorStateに書き込み済みのため、
                        // 手動コピーは不要
                        _undoController.EndEditorStateDrag("Change Display Settings");
                    }
                }

                // === カリング設定 ===
                EditorGUI.BeginChangeCheck();
                bool currentCulling = _undoController?.EditorState.BackfaceCullingEnabled ?? true;
                bool newCulling = EditorGUILayout.Toggle(L.Get("BackfaceCulling"), currentCulling);
                if (EditorGUI.EndChangeCheck())
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                        _undoController.EditorState.BackfaceCullingEnabled = newCulling;
                        _undoController.EndEditorStateDrag("Toggle Backface Culling");
                    }

                    // TODO: 統合システムにカリング設定を反映
                    Repaint();
                }

                // === トランスフォーム表示設定 ===
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(L.Get("TransformDisplay"), EditorStyles.miniLabel);
                
                
                EditorGUI.BeginChangeCheck();
                bool currentShowLocal = _undoController?.EditorState.ShowLocalTransform ?? false;
                bool currentShowWorld = _undoController?.EditorState.ShowWorldTransform ?? false;
                
                bool newShowLocal = EditorGUILayout.Toggle(L.Get("ShowLocalTransform"), currentShowLocal);
                bool newShowWorld = EditorGUILayout.Toggle(L.Get("ShowWorldTransform"), currentShowWorld);
                
                if (EditorGUI.EndChangeCheck())
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                        _undoController.EditorState.ShowLocalTransform = newShowLocal;
                        _undoController.EditorState.ShowWorldTransform = newShowWorld;
                        _undoController.EndEditorStateDrag("Change Transform Display");
                    }
                    Repaint();
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(L.Get("Zoom"), EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newDist = EditorGUILayout.Slider(_cameraDistance,_cameraDistanceMin,_cameraDistanceMax);// 0.1f, 80f);//スライダーの上限下限（マウスズームは別）：ズーム
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newDist, _cameraDistance))
                {
                    if (!_isCameraDragging) BeginCameraDrag();
                    _cameraDistance = newDist;
                }

                // オートズーム設定（メッシュ選択時に自動でカメラを調整）
                EditorGUI.BeginChangeCheck();
                bool currentAutoZoom = _undoController?.EditorState.AutoZoomEnabled ?? false;
                bool newAutoZoom = EditorGUILayout.Toggle(L.Get("AutoZoom"), currentAutoZoom);
                if (EditorGUI.EndChangeCheck() && newAutoZoom != currentAutoZoom)
                {
                    if (_undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                        _undoController.EditorState.AutoZoomEnabled = newAutoZoom;
                        _undoController.EndEditorStateDrag("Toggle Auto Zoom");
                    }
                    Repaint();
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

                    // カメラ操作Undo記録設定
                    bool undoCamera = _undoController.EditorState.UndoCameraChanges;
                    EditorGUI.BeginChangeCheck();
                    bool newUndoCamera = EditorGUILayout.Toggle(L.Get("UndoCamera"), undoCamera);
                    if (EditorGUI.EndChangeCheck() && newUndoCamera != undoCamera)
                    {
                        _undoController.EditorState.UndoCameraChanges = newUndoCamera;
                    }

                    // パネル設定Undo記録設定
                    bool undoPanel = _undoController.EditorState.UndoPanelSettings;
                    EditorGUI.BeginChangeCheck();
                    bool newUndoPanel = EditorGUILayout.Toggle(L.Get("UndoPanelSettings"), undoPanel);
                    if (EditorGUI.EndChangeCheck() && newUndoPanel != undoPanel)
                    {
                        _undoController.EditorState.UndoPanelSettings = newUndoPanel;
                    }
                }

                // カメラ注目点表示トグル
                EditorGUI.BeginChangeCheck();
                _showFocusPoint = EditorGUILayout.Toggle("Show Focus Point", _showFocusPoint);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ★ここに追加（独立したセクションとして）★
            DrawUnifiedSystemUI();


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
                    SetSelectedIndex(-1);
                    _vertexOffsets = null;
                    _groupOffsets = null;
                    _undoController?.VertexEditStack.Clear();
                    _model?.OnListChanged?.Invoke();
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
                if (GUILayout.Button(L.Get("FromHierarchy")))
                {
                    LoadMeshFromHierarchy();
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
            // 注意: FocusVertexEdit()はここで呼ばない
            // 各操作のRecord時に適切なFocusXxx()が呼ばれるため、
            // GUI描画時に強制すると他のスタック（EditorState, MeshList等）への
            // 記録後にフォーカスが上書きされてしまう

            _foldSelection = DrawFoldoutWithUndo("Selection", L.Get("Selection"), true);
            if (_foldSelection)
            {
                EditorGUI.indentLevel++;

                // === 選択モード切り替え ===
                DrawSelectionModeToolbar();

                // === ドラッグ選択モード切り替え（矩形/投げ縄） ===
                DrawDragSelectModeToolbar();

                int totalVertices = 0;

                var meshContext = _model.FirstSelectedMeshContext;
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
                using (new EditorGUI.DisabledScope(_selectionState.Vertices.Count == 0))
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
                using (new EditorGUI.DisabledScope(_selectionState.Vertices.Count < 2))
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


            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// ドラッグ選択モード切り替えツールバー（矩形/投げ縄）
    /// </summary>
    private void DrawDragSelectModeToolbar()
    {
        EditorGUILayout.BeginHorizontal();

        var buttonStyle = EditorStyles.miniButton;
        var oldColor = GUI.backgroundColor;

        // Box（矩形選択）
        bool boxActive = _dragSelectMode == DragSelectMode.Box;
        if (boxActive) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("☐ Box", buttonStyle))
        {
            _dragSelectMode = DragSelectMode.Box;
        }
        GUI.backgroundColor = oldColor;

        // Lasso（投げ縄選択）
        bool lassoActive = _dragSelectMode == DragSelectMode.Lasso;
        if (lassoActive) GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("◯ Lasso", buttonStyle))
        {
            _dragSelectMode = DragSelectMode.Lasso;
        }
        GUI.backgroundColor = oldColor;

        EditorGUILayout.EndHorizontal();
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
        HashSet<int> oldLegacySelection = new HashSet<int>(_selectionState.Vertices);

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
        HashSet<int> oldLegacySelection = new HashSet<int>(_selectionState.Vertices);

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
        // Materials は ModelContext に集約済み
        // 選択状態を同期
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectionState.Vertices);
    }
}