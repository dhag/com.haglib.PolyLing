// Assets/Editor/PolyLing.VertexEdit.cs
// 右ペイン（頂点エディタ、スライダー編集）
// Phase2: マルチマテリアル対応版
// Phase6: マテリアルUndo対応版

using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
public partial class PolyLing
{
    // ================================================================
    // 右ペイン：スクロール位置
    // ================================================================
    private Vector2 _rightPaneScroll;

    // ================================================================
    // 右ペイン：頂点エディタ（MeshObjectベース）
    // ================================================================
    private void DrawVertexEditor()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_rightPaneWidth));
        EditorGUILayout.LabelField(L.Get("VertexEditor"), EditorStyles.boldLabel);

        // スクロール開始
        _rightPaneScroll = EditorGUILayout.BeginScrollView(_rightPaneScroll);

        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null)
        {
            EditorGUILayout.HelpBox(L.Get("SelectMesh"), MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        var meshObject = meshContext.MeshObject;

        if (meshObject == null)
        {
            EditorGUILayout.HelpBox(L.Get("InvalidMeshData"), MessageType.Warning);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        // メッシュ情報表示
        EditorGUILayout.LabelField($"{L.Get("Vertices")}: {meshObject.VertexCount}");
        EditorGUILayout.LabelField($"{L.Get("Faces")}: {meshObject.FaceCount}");
        EditorGUILayout.LabelField($"{L.Get("Triangles")}: {meshObject.TriangleCount}");

        // 面タイプ内訳
        int triCount = meshObject.Faces.Count(f => f.IsTriangle);
        int quadCount = meshObject.Faces.Count(f => f.IsQuad);
        int nGonCount = meshObject.FaceCount - triCount - quadCount;
        EditorGUILayout.LabelField($"  ({L.Get("Tri")}:{triCount}, {L.Get("Quad")}:{quadCount}, {L.Get("NGon")}:{nGonCount})", EditorStyles.miniLabel);

        EditorGUILayout.Space(5);

        if (GUILayout.Button(L.Get("ResetToOriginal")))
        {
            MeshObjectSnapshot before = _undoController?.CaptureMeshObjectSnapshot();

            ResetMesh(meshContext);

            if (_undoController != null && before != null)
            {
                MeshObjectSnapshot after = _undoController.CaptureMeshObjectSnapshot();
                _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    _undoController, before, after, "Reset UnityMesh"));
            }
        }

        EditorGUILayout.Space(5);

        // ================================================================
        // マテリアル管理機能（マルチマテリアル対応）
        // ================================================================
        DrawMaterialUI(meshContext);

        EditorGUILayout.Space(5);

        // ================================================================
        // 保存機能
        // ================================================================
        EditorGUILayout.LabelField(L.Get("Save"), EditorStyles.miniBoldLabel);

        // Export Transform設定（メッシュコンテキストごと）
        if (meshContext.BoneTransform != null)
        {
            BoneTransformUI.DrawUI(meshContext.BoneTransform);
            EditorGUILayout.Space(4);
        }

        // ================================================================
        // 選択メッシュのみ チェックボックス（Undo対応）
        // ================================================================
        EditorGUI.BeginChangeCheck();
        bool newExportSelectedOnly = EditorGUILayout.Toggle(L.Get("ExportSelectedMeshOnly"), _exportSelectedMeshOnly);
        if (EditorGUI.EndChangeCheck() && newExportSelectedOnly != _exportSelectedMeshOnly)
        {
            if (_undoController != null)
            {
                _undoController.BeginEditorStateDrag();
            }

            _exportSelectedMeshOnly = newExportSelectedOnly;

            if (_undoController != null)
            {
                _undoController.EditorState.ExportSelectedMeshOnly = _exportSelectedMeshOnly;
                _undoController.EndEditorStateDrag("Toggle Export Selected Mesh Only");
            }
        }

        // ================================================================
        // 対称をベイク チェックボックス（Undo対応）
        // ================================================================
        EditorGUI.BeginChangeCheck();
        bool newBakeMirror = EditorGUILayout.Toggle(L.Get("BakeMirror"), _bakeMirror);
        if (EditorGUI.EndChangeCheck() && newBakeMirror != _bakeMirror)
        {
            if (_undoController != null)
            {
                _undoController.BeginEditorStateDrag();
            }

            _bakeMirror = newBakeMirror;

            if (_undoController != null)
            {
                _undoController.EditorState.BakeMirror = _bakeMirror;
                _undoController.EndEditorStateDrag("Toggle Bake Mirror");
            }
        }

        // UV U反転（対称ベイク時のみ有効）
        using (new EditorGUI.DisabledScope(!_bakeMirror))
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            bool newMirrorFlipU = EditorGUILayout.Toggle(L.Get("MirrorFlipU"), _mirrorFlipU);
            if (EditorGUI.EndChangeCheck() && newMirrorFlipU != _mirrorFlipU)
            {
                if (_undoController != null)
                {
                    _undoController.BeginEditorStateDrag();
                }

                _mirrorFlipU = newMirrorFlipU;

                if (_undoController != null)
                {
                    _undoController.EditorState.MirrorFlipU = _mirrorFlipU;
                    _undoController.EndEditorStateDrag("Toggle Mirror Flip U");
                }
            }
            EditorGUI.indentLevel--;
        }

        // ================================================================
        // オンメモリマテリアル保存オプション（オンメモリマテリアルがある場合のみ表示）
        // ================================================================
        if (_model.HasOnMemoryMaterials())
        {
            EditorGUI.BeginChangeCheck();
            bool newSaveOnMemoryMaterials = EditorGUILayout.Toggle(L.Get("SaveOnMemoryMaterials"), _saveOnMemoryMaterials);
            if (EditorGUI.EndChangeCheck() && newSaveOnMemoryMaterials != _saveOnMemoryMaterials)
            {
                _saveOnMemoryMaterials = newSaveOnMemoryMaterials;
            }

            // 保存オプション（SaveOnMemoryMaterialsがONの場合のみ表示）
            if (_saveOnMemoryMaterials)
            {
                EditorGUI.indentLevel++;

                // 上書きオプション
                EditorGUI.BeginChangeCheck();
                bool newOverwrite = EditorGUILayout.Toggle("Overwrite Existing", _overwriteExistingAssets);
                if (EditorGUI.EndChangeCheck())
                {
                    _overwriteExistingAssets = newOverwrite;
                }

                // 保存先フォルダ
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Save Folder", GUILayout.Width(80));

                // フォルダパス表示（短縮）
                string displayPath = string.IsNullOrEmpty(_materialSaveFolder)
                    ? "(Default)"
                    : TruncatePath(_materialSaveFolder, 20);
                EditorGUILayout.LabelField(displayPath, EditorStyles.miniLabel);

                // 選択ボタン
                if (GUILayout.Button("...", GUILayout.Width(25)))
                {
                    string defaultPath = string.IsNullOrEmpty(_materialSaveFolder)
                        ? "Assets/SavedMaterials"
                        : _materialSaveFolder;
                    string selectedPath = EditorUtility.OpenFolderPanel("Select Material Save Folder", defaultPath, "");
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        // プロジェクト相対パスに変換
                        if (selectedPath.StartsWith(Application.dataPath))
                        {
                            _materialSaveFolder = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Error", "Assetsフォルダ内を選択してください。", "OK");
                        }
                    }
                }

                // クリアボタン
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    _materialSaveFolder = "";
                }

                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space(2);

        // ================================================================
        // エクスポートボタン群
        // ================================================================

        // Armature/Meshesフォルダ作成オプション（ボーンがある場合のみ表示）
        bool hasBones = _meshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
        if (hasBones && !_exportSelectedMeshOnly)
        {
            _createArmatureMeshesFolder = EditorGUILayout.Toggle(
                L.Get("CreateArmatureMeshesFolder"),
                _createArmatureMeshesFolder);

            // スキンメッシュで出力（BoneTransform.ExportAsSkinnedを使用）
            bool currentExportAsSkinned = _meshContextList.Any(ctx => ctx?.BoneTransform != null && ctx.BoneTransform.ExportAsSkinned);
            bool newExportAsSkinned = EditorGUILayout.Toggle(
                L.Get("ExportAsSkinned"),
                currentExportAsSkinned);
            if (newExportAsSkinned != currentExportAsSkinned)
            {
                // 全MeshContextのBoneTransform.ExportAsSkinnedを更新
                foreach (var ctx in _meshContextList)
                {
                    if (ctx?.BoneTransform != null)
                    {
                        ctx.BoneTransform.ExportAsSkinned = newExportAsSkinned;
                    }
                }
            }

            // Animatorコンポーネント追加オプション（SkinnedMesh出力時のみ表示）
            if (currentExportAsSkinned)
            {
                EditorGUI.indentLevel++;
                _addAnimatorComponent = EditorGUILayout.Toggle(
                    L.Get("AddAnimatorComponent"),
                    _addAnimatorComponent);
                
                // Avatar生成オプション（Animator追加時のみ表示）
                if (_addAnimatorComponent)
                {
                    EditorGUI.indentLevel++;
                    bool hasMapping = _model?.HumanoidMapping != null && !_model.HumanoidMapping.IsEmpty;
                    using (new EditorGUI.DisabledScope(!hasMapping))
                    {
                        _createAvatarOnExport = EditorGUILayout.Toggle(
                            L.Get("CreateAvatarOnExport"),
                            _createAvatarOnExport && hasMapping);
                    }
                    if (!hasMapping)
                    {
                        EditorGUILayout.HelpBox(L.Get("NoHumanoidMapping"), MessageType.Info);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
        }

        bool hasAnyMesh = _meshContextList.Count > 0;
        bool canExport = _exportSelectedMeshOnly ? true : hasAnyMesh;  // 選択時は現在のmeshContextが有効

        using (new EditorGUI.DisabledScope(!canExport))
        {
            if (GUILayout.Button(L.Get("SaveMeshAsset")))
            {
                if (_exportSelectedMeshOnly)
                {
                    SaveMesh(meshContext);
                }
                else
                {
                    SaveModelMeshAssets();
                }
            }

            if (GUILayout.Button(L.Get("SaveAsPrefab")))
            {
                if (_exportSelectedMeshOnly)
                {
                    SaveAsPrefab(meshContext);
                }
                else
                {
                    SaveModelAsPrefab();
                }
            }

            if (GUILayout.Button(L.Get("AddToHierarchy")))
            {
                if (_exportSelectedMeshOnly)
                {
                    AddToHierarchy(meshContext);
                }
                else
                {
                    AddModelToHierarchy();
                }
            }

            // 上書きエクスポート（選択中のヒエラルキーに同名メッシュを上書き）
            using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
            {
                if (GUILayout.Button(L.Get("OverwriteToHierarchy")))
                {
                    OverwriteToHierarchy();
                }
            }
        }

        EditorGUILayout.Space(10);

        // ================================================================
        // モデル保存/読み込み
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

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ================================================================
    // マテリアル管理UI（マルチマテリアル対応）
    // ================================================================

    /// <summary>
    /// マテリアルUIを描画
    /// </summary>
    private void DrawMaterialUI(MeshContext meshContext)
    {
        _foldMaterials = DrawFoldoutWithUndo("Materials", "Materials", false);
        if (!_foldMaterials)
            return;

        EditorGUI.indentLevel++;

        // マテリアルリスト表示
        for (int i = 0; i < _model.MaterialCount; i++)
        {
            EditorGUILayout.BeginHorizontal();

            // 選択マーカー
            string marker = (i == _model.CurrentMaterialIndex) ? "●" : "○";
            if (GUILayout.Button(marker, GUILayout.Width(20)))
            {
                _model.CurrentMaterialIndex = i;
                SyncMaterialsToUndoContext(meshContext);
                // 自動デフォルト設定
                AutoUpdateDefaultMaterials(meshContext);
            }

            // スロット番号
            EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(25));

            // マテリアルフィールド
            EditorGUI.BeginChangeCheck();
            Material newMat = (Material)EditorGUILayout.ObjectField(
                _model.GetMaterial(i),
                typeof(Material),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                // Undo用スナップショット（変更前）
                MeshObjectSnapshot beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

                _model.SetMaterial(i, newMat);
                SyncMaterialsToUndoContext(meshContext);

                // Undo記録
                RecordMaterialChange(beforeSnapshot, $"Change Material [{i}]");
            }

            // 削除ボタン（最後の1つは削除不可）
            EditorGUI.BeginDisabledGroup(_model.MaterialCount <= 1);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                RemoveMaterialSlot(meshContext, i);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        // マテリアル追加ボタン
        if (GUILayout.Button("+ Add Material Slot"))
        {
            // Undo用スナップショット（変更前）
            var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

            _model.AddMaterial(null);
            _model.CurrentMaterialIndex = _model.MaterialCount - 1;
            SyncMaterialsToUndoContext(meshContext);

            // Undo記録
            RecordMaterialChange(beforeSnapshot, "Add Material Slot");
        }

        // マテリアル一括追加ドロップエリア
        DrawMaterialDropArea(meshContext);

        // 現在選択中のマテリアルスロット表示
        EditorGUILayout.LabelField($"Current: [{_model.CurrentMaterialIndex}]", EditorStyles.miniLabel);

        // マテリアルがnullの場合の注意
        if (meshContext.GetCurrentMaterial() == null)
        {
            EditorGUILayout.HelpBox("None: デフォルト使用", MessageType.None);
        }

        EditorGUILayout.Space(3);

        // デフォルトマテリアル設定
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Set as Default", GUILayout.Width(100)))
        {
            SetCurrentMaterialsAsDefault(meshContext);
        }

        // 自動設定チェックボックス
        EditorGUI.BeginChangeCheck();
        bool newAutoSet = EditorGUILayout.ToggleLeft("Auto", _autoSetDefaultMaterials, GUILayout.Width(50));
        if (EditorGUI.EndChangeCheck())
        {
            SetAutoSetDefaultMaterials(newAutoSet);
        }

        // デフォルト情報を表示
        string defaultInfo = $"(Default: {_defaultMaterials?.Count ?? 0} slots, idx={_defaultCurrentMaterialIndex})";
        EditorGUILayout.LabelField(defaultInfo, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // 選択面へのマテリアル適用UI
        DrawApplyMaterialToSelectionUI(meshContext);

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 選択面へのマテリアル適用UI
    /// </summary>
    private void DrawApplyMaterialToSelectionUI(MeshContext meshContext)
    {
        if (meshContext.MeshObject == null || _selectionState == null)
            return;

        // 選択された面がある場合のみ表示
        int selectedFaceCount = _selectionState.Faces.Count;
        if (selectedFaceCount == 0)
            return;

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Apply to Selection", EditorStyles.miniBoldLabel);

        // 選択面のマテリアル分布を表示
        var materialCounts = new Dictionary<int, int>();
        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx >= 0 && faceIdx < meshContext.MeshObject.FaceCount)
            {
                int matIdx = meshContext.MeshObject.Faces[faceIdx].MaterialIndex;
                if (!materialCounts.ContainsKey(matIdx))
                    materialCounts[matIdx] = 0;
                materialCounts[matIdx]++;
            }
        }

        // 分布表示
        string distribution = string.Join(", ",
            materialCounts.OrderBy(kv => kv.Key)
                          .Select(kv => $"[{kv.Key}]:{kv.Value}"));
        EditorGUILayout.LabelField($"Selected: {selectedFaceCount} faces ({distribution})", EditorStyles.miniLabel);

        // 適用ボタン
        if (GUILayout.Button($"Apply Material [{_model.CurrentMaterialIndex}] to Selection"))
        {
            ApplyMaterialToSelectedFaces(meshContext, _model.CurrentMaterialIndex);
        }
    }

    /// <summary>
    /// 選択された面にマテリアルを適用
    /// </summary>
    private void ApplyMaterialToSelectedFaces(MeshContext meshContext, int materialIndex)
    {
        if (meshContext.MeshObject == null || _selectionState == null)
            return;

        if (_selectionState.Faces.Count == 0)
            return;

        // マテリアルインデックスの範囲チェック
        if (materialIndex < 0 || materialIndex >= _model.MaterialCount)
            return;

        // Undo用スナップショット
        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

        // 選択された面のマテリアルを変更
        bool changed = false;
        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx >= 0 && faceIdx < meshContext.MeshObject.FaceCount)
            {
                var face = meshContext.MeshObject.Faces[faceIdx];
                if (face.MaterialIndex != materialIndex)
                {
                    face.MaterialIndex = materialIndex;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            // メッシュを再構築
            SyncMeshFromData(meshContext);

            // Undo記録（コマンドキュー経由）
            if (_undoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
                _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    _undoController, beforeSnapshot, afterSnapshot,
                    $"Apply Material [{materialIndex}] to {_selectionState.Faces.Count} faces"));
            }

            Repaint();
        }
    }

    /// <summary>
    /// マテリアルスロットを削除
    /// </summary>
    private void RemoveMaterialSlot(MeshContext meshContext, int index)
    {
        // 最後の1つは削除不可
        if (_model.MaterialCount <= 1)
            return;

        if (index < 0 || index >= _model.MaterialCount)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

        // 削除するマテリアルを使用している面をスロット0に移動
        if (meshContext.MeshObject != null)
        {
            foreach (var face in meshContext.MeshObject.Faces)
            {
                if (face.MaterialIndex == index)
                {
                    // 削除されるスロットを使っていた面は、スロット0に戻す
                    // ただしスロット0が削除される場合は、スロット1（新しい0）に
                    face.MaterialIndex = (index == 0) ? 0 : 0;
                }
                else if (face.MaterialIndex > index)
                {
                    face.MaterialIndex--;
                }
            }
        }

        _model.RemoveMaterialAt(index);

        // CurrentMaterialIndexの調整
        if (_model.CurrentMaterialIndex >= _model.MaterialCount)
        {
            _model.CurrentMaterialIndex = _model.MaterialCount - 1;
        }
        else if (_model.CurrentMaterialIndex > index)
        {
            _model.CurrentMaterialIndex--;
        }
        else if (_model.CurrentMaterialIndex == index)
        {
            _model.CurrentMaterialIndex = 0;
        }

        // Undoコンテキストに同期
        SyncMaterialsToUndoContext(meshContext);

        // Undo記録
        RecordMaterialChange(beforeSnapshot, $"Remove Material Slot [{index}]");

        // メッシュを更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// スライダードラッグ開始
    /// </summary>
    private void BeginSliderDrag()
    {
        if (_isSliderDragging) return;
        if (_vertexOffsets == null) return;

        _isSliderDragging = true;
        _sliderDragStartOffsets = (Vector3[])_vertexOffsets.Clone();
    }

    /// <summary>
    /// スライダードラッグ終了（Undo記録）
    /// </summary>
    private void EndSliderDrag()
    {
        if (!_isSliderDragging) return;
        _isSliderDragging = false;

        if (_sliderDragStartOffsets == null || _vertexOffsets == null) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null) return;

        // 変更された頂点を検出
        var changedIndices = new List<int>();
        var oldPositions = new List<Vector3>();
        var newPositions = new List<Vector3>();

        for (int i = 0; i < _vertexOffsets.Length && i < _sliderDragStartOffsets.Length; i++)
        {
            if (Vector3.Distance(_vertexOffsets[i], _sliderDragStartOffsets[i]) > 0.0001f)
            {
                changedIndices.Add(i);
                oldPositions.Add(meshContext.OriginalPositions[i] + _sliderDragStartOffsets[i]);
                newPositions.Add(meshContext.OriginalPositions[i] + _vertexOffsets[i]);
            }
        }

        if (changedIndices.Count > 0 && _undoController != null)
        {
            var record = new VertexMoveRecord(
                changedIndices.ToArray(),
                oldPositions.ToArray(),
                newPositions.ToArray()
            );
            _undoController.VertexEditStack.Record(record, "Move Vertices");
        }

        _sliderDragStartOffsets = null;
    }

    /// <summary>
    /// メッシュをリセット
    /// </summary>
    private void ResetMesh(MeshContext meshContext)
    {
        if (meshContext.MeshObject == null || meshContext.OriginalPositions == null)
            return;

        // 元の位置に戻す
        for (int i = 0; i < meshContext.MeshObject.VertexCount && i < meshContext.OriginalPositions.Length; i++)
        {
            meshContext.MeshObject.Vertices[i].Position = meshContext.OriginalPositions[i];
        }

        SyncMeshFromData(meshContext);

        if (_vertexOffsets != null)
        {
            for (int i = 0; i < _vertexOffsets.Length; i++)
                _vertexOffsets[i] = Vector3.zero;
        }

        if (_groupOffsets != null)
        {
            for (int i = 0; i < _groupOffsets.Length; i++)
                _groupOffsets[i] = Vector3.zero;
        }

        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
        }

        Repaint();
    }

    /// <summary>
    /// マテリアル一括追加ドロップエリア
    /// </summary>
    private void DrawMaterialDropArea(MeshContext meshContext)
    {
        EditorGUILayout.Space(4);


        // ドロップエリアの矩形
        Rect dropArea = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));

        // ドロップエリアのスタイル
        GUIStyle dropStyle = new GUIStyle(GUI.skin.box);
        dropStyle.alignment = TextAnchor.MiddleCenter;
        dropStyle.normal.textColor = Color.gray;

        // ドラッグ中はハイライト
        bool isDragging = DragAndDrop.objectReferences.Length > 0 &&
                          dropArea.Contains(Event.current.mousePosition);

        UnityEditor_Handles.BeginGUI();
        if (isDragging)
        {
            UnityEditor_Handles.DrawRect(dropArea, new Color(0.3f, 0.5f, 0.8f, 0.3f));//?
        }
        UnityEditor_Handles.EndGUI();

        GUI.Box(dropArea, "Drop Materials Here\n(複数選択可)", dropStyle);

        // ドラッグ＆ドロップ処理
        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    break;

                // マテリアルがあるかチェック
                bool hasMaterials = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Material)
                    {
                        hasMaterials = true;
                        break;
                    }
                }

                if (hasMaterials)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        // Undo用スナップショット（変更前）
                        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

                        int addedCount = 0;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Material mat)
                            {
                                // 既存スロットにないか確認（重複防止はオプション）
                                _model.AddMaterial(mat);
                                addedCount++;
                            }
                        }

                        if (addedCount > 0)
                        {
                            _model.CurrentMaterialIndex = _model.MaterialCount - 1;
                            SyncMaterialsToUndoContext(meshContext);

                            // Undo記録
                            RecordMaterialChange(beforeSnapshot, $"Add {addedCount} Material(s)");

                            Debug.Log($"[MaterialUI] Added {addedCount} material(s). Total slots: {_model.MaterialCount}");
                            Repaint();
                        }
                    }
                }
                evt.Use();
                break;
        }
    }

    /// <summary>
    /// マテリアル情報をUndoコンテキストに同期
    /// </summary>
    private void SyncMaterialsToUndoContext(MeshContext meshContext)
    {
        // Materials は ModelContext に集約済み - 同期不要
    }

    /// <summary>
    /// マテリアル変更をUndo記録
    /// </summary>
    private void RecordMaterialChange(MeshObjectSnapshot beforeSnapshot, string description)
    {
        if (beforeSnapshot == null || _undoController == null)
        {
            Debug.LogWarning($"[MaterialUndo] Cannot record: before={beforeSnapshot != null}, controller={_undoController != null}");
            return;
        }

        var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
            _undoController, beforeSnapshot, afterSnapshot, description));
        Debug.Log($"[MaterialUndo] Recorded: {description}");

        // 自動デフォルト設定がONの場合、デフォルトを更新
        var currentMeshContext = _model.CurrentMeshContext;
        if (currentMeshContext != null)
        {
            AutoUpdateDefaultMaterials(currentMeshContext);
        }
    }

    /// <summary>
    /// 現在のマテリアルリストをデフォルトに設定
    /// </summary>
    private void SetCurrentMaterialsAsDefault(MeshContext meshContext)
    {
        if (meshContext == null || _model.MaterialCount == 0)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

        // デフォルトマテリアルを更新
        _defaultMaterials = new List<Material>(_model.Materials);
        _defaultCurrentMaterialIndex = _model.CurrentMaterialIndex;

        // Undoコンテキストに同期
        SyncDefaultMaterialsToUndoContext();

        // Undo記録（コマンドキュー経由）
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                _undoController, beforeSnapshot, afterSnapshot, "Set Default Materials"));
            Debug.Log($"[MaterialUndo] Set default materialPathList: {_defaultMaterials.Count} slots, currentIndex={_defaultCurrentMaterialIndex}");
        }
    }

    /// <summary>
    /// 自動デフォルト設定のON/OFFを切り替え（Undo対応）
    /// </summary>
    private void SetAutoSetDefaultMaterials(bool value)
    {
        if (_autoSetDefaultMaterials == value)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

        _autoSetDefaultMaterials = value;

        // Undoコンテキストに同期
        SyncDefaultMaterialsToUndoContext();

        // Undo記録（コマンドキュー経由）
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                _undoController, beforeSnapshot, afterSnapshot, $"Auto Default Materials: {(value ? "ON" : "OFF")}"));
            Debug.Log($"[MaterialUndo] Auto default materialPathList: {(value ? "ON" : "OFF")}");
        }
    }

    /// <summary>
    /// 自動デフォルト設定がONの場合、現在のマテリアルをデフォルトに設定（Undo記録なし、内部用）
    /// </summary>
    private void AutoUpdateDefaultMaterials(MeshContext meshContext)
    {
        if (!_autoSetDefaultMaterials || meshContext == null || _model.MaterialCount == 0)
            return;

        _defaultMaterials = new List<Material>(_model.Materials);
        _defaultCurrentMaterialIndex = _model.CurrentMaterialIndex;
        SyncDefaultMaterialsToUndoContext();

        Debug.Log($"[MaterialUndo] Auto-updated default materialPathList: {_defaultMaterials.Count} slots, currentIndex={_defaultCurrentMaterialIndex}");
    }

    /// <summary>
    /// デフォルトマテリアル情報をUndoコンテキストに同期
    /// </summary>
    private void SyncDefaultMaterialsToUndoContext()
    {
        // DefaultMaterials は ModelContext に集約済み - 同期不要
    }

    /// <summary>
    /// パスを指定文字数に短縮
    /// </summary>
    private string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;

        // 末尾を優先して表示
        return "..." + path.Substring(path.Length - maxLength + 3);
    }

}