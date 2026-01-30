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

                if (EditorGUI.EndChangeCheck())
                {
                    bool hasDisplayChange =
                        newShowMesh != _showMesh ||
                        newShowWireframe != _showWireframe ||
                        newShowVertices != _showVertices ||
                        newShowVertexIndices != _showVertexIndices ||
                        newShowSelectedMeshOnly != _showSelectedMeshOnly ||
                        newShowUnselectedVertices != _showUnselectedVertices ||
                        newShowUnselectedWireframe != _showUnselectedWireframe;

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
                float newDist = EditorGUILayout.Slider(_cameraDistance, 0.1f, 80f);//スライダーの上限下限：ズーム
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

                // ボーン表示トグル
                EditorGUILayout.Space(2);
                EditorGUI.BeginChangeCheck();
                _showBones = EditorGUILayout.Toggle("Show Bones", _showBones);
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
                    _selectedIndex = -1;
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
                var ctx = _meshContextList[i];
                
                // ボーンの場合の表示制御
                if (ctx.Type == MeshType.Bone)
                {
                    // ボーン非表示モードならスキップ
                    if (!_showBones) continue;
                    
                    // ボーンルートかどうか判定
                    bool isRoot = IsBoneRoot(i);
                    
                    if (isRoot)
                    {
                        // ルートボーン: 折りたたみヘッダー表示
                        DrawBoneRootItem(i, ctx);
                    }
                    else
                    {
                        // 子ボーン: 親が折りたたまれていたらスキップ
                        int rootIndex = FindBoneRootIndex(i);
                        if (rootIndex >= 0 && _foldedBoneRoots.Contains(rootIndex))
                            continue;
                        
                        // インデント付きで表示
                        DrawBoneChildItem(i, ctx);
                    }
                    continue;
                }
                
                // 通常メッシュの描画
                DrawMeshListItem(i, ctx);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// ボーンルートかどうか判定
    /// </summary>
    private bool IsBoneRoot(int index)
    {
        var ctx = _meshContextList[index];
        if (ctx.Type != MeshType.Bone) return false;
        
        // ParentIndexまたはHierarchyParentIndexをチェック
        int parentIdx = ctx.ParentIndex >= 0 ? ctx.ParentIndex : ctx.HierarchyParentIndex;
        
        // 親がいない場合はルート
        if (parentIdx < 0) return true;
        if (parentIdx >= _meshContextList.Count) return true;
        
        // 親がボーンでない場合はルート
        return _meshContextList[parentIdx].Type != MeshType.Bone;
    }

    /// <summary>
    /// ボーンのルートインデックスを探す
    /// </summary>
    private int FindBoneRootIndex(int boneIndex)
    {
        int current = boneIndex;
        while (current >= 0 && current < _meshContextList.Count)
        {
            if (IsBoneRoot(current)) return current;
            var ctx = _meshContextList[current];
            current = ctx.ParentIndex >= 0 ? ctx.ParentIndex : ctx.HierarchyParentIndex;
        }
        return -1;
    }

    /// <summary>
    /// ボーングループ内のボーン数をカウント
    /// </summary>
    private int CountBonesInGroup(int rootIndex)
    {
        int count = 1; // 自分自身
        for (int i = rootIndex + 1; i < _meshContextList.Count; i++)
        {
            var ctx = _meshContextList[i];
            if (ctx.Type != MeshType.Bone) break;
            if (IsBoneRoot(i)) break; // 別のルートに到達
            count++;
        }
        return count;
    }

    /// <summary>
    /// ボーンの深度を計算
    /// </summary>
    private int GetBoneDepth(int boneIndex)
    {
        int depth = 0;
        int current = boneIndex;
        while (current >= 0 && current < _meshContextList.Count)
        {
            var ctx = _meshContextList[current];
            if (ctx.Type != MeshType.Bone) break;
            if (IsBoneRoot(current)) break;
            depth++;
            current = ctx.ParentIndex >= 0 ? ctx.ParentIndex : ctx.HierarchyParentIndex;
        }
        return depth;
    }

    /// <summary>
    /// ボーンルートアイテムの描画（折りたたみヘッダー）
    /// </summary>
    private void DrawBoneRootItem(int index, MeshContext ctx)
    {
        bool isFolded = _foldedBoneRoots.Contains(index);
        int boneCount = CountBonesInGroup(index);
        
        EditorGUILayout.BeginHorizontal();
        
        // 折りたたみトグル
        string foldIcon = isFolded ? "▶" : "▼";
        if (GUILayout.Button(foldIcon, GUILayout.Width(20)))
        {
            if (isFolded)
                _foldedBoneRoots.Remove(index);
            else
                _foldedBoneRoots.Add(index);
        }
        
        // 選択ボタン
        bool isSelected = (index == _selectedIndex);
        string label = $"🦴 {ctx.Name} ({boneCount})";
        bool newSelected = GUILayout.Toggle(isSelected, label, "Button");
        
        if (newSelected && !isSelected)
        {
            SelectMeshAtIndex(index);
        }
        
        // 削除ボタン（ボーングループ全体を削除）
        if (GUILayout.Button("×", GUILayout.Width(20)))
        {
            EditorGUILayout.EndHorizontal();
            RemoveBoneGroup(index);
            return;
        }
        
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// ボーン子アイテムの描画（インデント付き）
    /// </summary>
    private void DrawBoneChildItem(int index, MeshContext ctx)
    {
        int depth = GetBoneDepth(index);
        
        EditorGUILayout.BeginHorizontal();
        
        // インデント
        GUILayout.Space(20 + depth * 12);
        
        // 選択ボタン
        bool isSelected = (index == _selectedIndex);
        string label = $"├ {ctx.Name}";
        bool newSelected = GUILayout.Toggle(isSelected, label, "Button");
        
        if (newSelected && !isSelected)
        {
            SelectMeshAtIndex(index);
        }
        
        EditorGUILayout.EndHorizontal();
    }


    /// <summary>
    /// 通常メッシュアイテムの描画
    /// </summary>
    private void DrawMeshListItem0000(int index, MeshContext ctx)
    {
        EditorGUILayout.BeginHorizontal();

        bool isSelected = (index == _selectedIndex);
        bool newSelected = GUILayout.Toggle(isSelected, ctx.Name, "Button");

        if (newSelected && !isSelected)
        {
            SelectMeshAtIndex(index);
        }

        if (GUILayout.Button("×", GUILayout.Width(20)))
        {
            EditorGUILayout.EndHorizontal();
            RemoveMesh(index);
            return;
        }

        EditorGUILayout.EndHorizontal();
    }
    /// <summary>
    /// 通常メッシュアイテムの描画
    /// </summary>
    private void DrawMeshListItem(int index, MeshContext ctx)
    {
        EditorGUILayout.BeginHorizontal();

        // 可視性トグルボタン
        var visibleContent = ctx.IsVisible
            ? new GUIContent(@"👁", "Click to hide")
            : new GUIContent(@"−", "Click to show");
        if (GUILayout.Button(visibleContent, GUILayout.Width(22)))
        {
            // コマンド発行（Undoは本体で記録）
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsVisible = !ctx.IsVisible }
            });
        }

        // 対称トグルボタン
        var mirrorContent = ctx.IsMirrored
            ? new GUIContent(@"⇔ ", "Mirror ON - Click to disable")
            : new GUIContent(@"·", "Mirror OFF - Click to enable");
        if (GUILayout.Button(mirrorContent, GUILayout.Width(22)))
        {
            // コマンド発行（Undoは本体で記録）
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, MirrorType = ctx.IsMirrored ? 0 : 1 }
            });
        }

        // メッシュ名ボタン（選択用）
        bool isSelected = (index == _selectedIndex);
        bool newSelected = GUILayout.Toggle(isSelected, ctx.Name, "Button");

        if (newSelected && !isSelected)
        {
            SelectMeshAtIndex(index);
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 指定インデックスのメッシュを選択
    /// </summary>
    private void SelectMeshAtIndex(int index)
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

        // 現在の選択を保存（切り替え前）
        SaveSelectionToCurrentMesh();

        _selectedIndex = index;
        ResetEditState();
        InitVertexOffsets();

        // 選択を復元（切り替え後）
        LoadSelectionFromCurrentMesh();

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

        // メッシュ選択変更をUndo記録（キュー経由）
        _commandQueue?.Enqueue(new RecordMeshSelectionChangeCommand(
            _undoController, oldIndex, _selectedIndex, oldCamera, newCamera));
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
    }

    /// <summary>
    /// ボーングループを削除
    /// </summary>
    private void RemoveBoneGroup(int rootIndex)
    {
        // ボーングループの範囲を特定
        int endIndex = rootIndex + 1;
        while (endIndex < _meshContextList.Count)
        {
            var ctx = _meshContextList[endIndex];
            if (ctx.Type != MeshType.Bone) break;
            if (IsBoneRoot(endIndex)) break;
            endIndex++;
        }
        
        int count = endIndex - rootIndex;
        
        // 削除（後ろから）
        for (int i = endIndex - 1; i >= rootIndex; i--)
        {
            RemoveMesh(i);
        }
        
        Debug.Log($"[RemoveBoneGroup] Removed {count} bones starting at index {rootIndex}");
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