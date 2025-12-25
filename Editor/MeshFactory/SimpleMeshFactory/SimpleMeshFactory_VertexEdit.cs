// Assets/Editor/SimpleMeshFactory.VertexEdit.cs
// 右ペイン（頂点エディタ、スライダー編集）
// Phase2: マルチマテリアル対応版
// Phase6: マテリアルUndo対応版

using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.UndoSystem;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;
public partial class SimpleMeshFactory
{
    // ================================================================
    // 右ペイン：頂点エディタ（MeshObjectベース）
    // ================================================================
    private void DrawVertexEditor()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_rightPaneWidth)))
        {
            EditorGUILayout.LabelField(L.Get("VertexEditor"), EditorStyles.boldLabel);

            var meshContext = _model.CurrentMeshContext;
            if (meshContext == null)
            {
                EditorGUILayout.HelpBox(L.Get("SelectMesh"), MessageType.Info);
                return;
            }

            var meshObject = meshContext.MeshObject;

            if (meshObject == null)
            {
                EditorGUILayout.HelpBox(L.Get("InvalidMeshData"), MessageType.Warning);
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
                var before = _undoController?.CaptureMeshObjectSnapshot();

                ResetMesh(meshContext);

                if (_undoController != null && before != null)
                {
                    var after = _undoController.CaptureMeshObjectSnapshot();
                    _undoController.RecordTopologyChange(before, after, "Reset UnityMesh");
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
            if (meshContext.ExportSettings != null)
            {
                ExportSettingsUI.DrawUI(meshContext.ExportSettings);
                EditorGUILayout.Space(4);
            }

            if (GUILayout.Button(L.Get("SaveMeshAsset")))
            {
                SaveMesh(meshContext);
            }

            if (GUILayout.Button(L.Get("SaveAsPrefab")))
            {
                SaveAsPrefab(meshContext);
            }

            if (GUILayout.Button(L.Get("AddToHierarchy")))
            {
                AddToHierarchy(meshContext);
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
        }
    }

    // ================================================================
    // マテリアル管理UI（マルチマテリアル対応）
    // ================================================================

    /// <summary>
    /// マテリアルUIを描画
    /// </summary>
    private void DrawMaterialUI(MeshContext meshContext)
    {
        EditorGUILayout.LabelField("Materials", EditorStyles.miniBoldLabel);

        // マテリアルリスト表示
        for (int i = 0; i < _model.Materials.Count; i++)
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
                _model.Materials[i],
                typeof(Material),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                // Undo用スナップショット（変更前）
                var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

                _model.Materials[i] = newMat;
                SyncMaterialsToUndoContext(meshContext);

                // Undo記録
                RecordMaterialChange(beforeSnapshot, $"Change Material [{i}]");
            }

            // 削除ボタン（最後の1つは削除不可）
            EditorGUI.BeginDisabledGroup(_model.Materials.Count <= 1);
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

            _model.Materials.Add(null);
            _model.CurrentMaterialIndex = _model.Materials.Count - 1;
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
        if (materialIndex < 0 || materialIndex >= _model.Materials.Count)
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

            // Undo記録
            if (_undoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
                _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot,
                    $"Apply Material [{materialIndex}] to {_selectionState.Faces.Count} faces");
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
        if (_model.Materials.Count <= 1)
            return;

        if (index < 0 || index >= _model.Materials.Count)
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

        _model.Materials.RemoveAt(index);

        // CurrentMaterialIndexの調整
        if (_model.CurrentMaterialIndex >= _model.Materials.Count)
        {
            _model.CurrentMaterialIndex = _model.Materials.Count - 1;
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
                                _model.Materials.Add(mat);
                                addedCount++;
                            }
                        }

                        if (addedCount > 0)
                        {
                            _model.CurrentMaterialIndex = _model.Materials.Count - 1;
                            SyncMaterialsToUndoContext(meshContext);

                            // Undo記録
                            RecordMaterialChange(beforeSnapshot, $"Add {addedCount} Material(s)");

                            Debug.Log($"[MaterialUI] Added {addedCount} material(s). Total slots: {_model.Materials.Count}");
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
        _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, description);
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
        if (meshContext == null || _model.Materials == null)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshObjectSnapshot();

        // デフォルトマテリアルを更新
        _defaultMaterials = new List<Material>(_model.Materials);
        _defaultCurrentMaterialIndex = _model.CurrentMaterialIndex;

        // Undoコンテキストに同期
        SyncDefaultMaterialsToUndoContext();

        // Undo記録
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
            _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, "Set Default Materials");
            Debug.Log($"[MaterialUndo] Set default materials: {_defaultMaterials.Count} slots, currentIndex={_defaultCurrentMaterialIndex}");
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

        // Undo記録
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshObjectSnapshot();
            _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, $"Auto Default Materials: {(value ? "ON" : "OFF")}");
            Debug.Log($"[MaterialUndo] Auto default materials: {(value ? "ON" : "OFF")}");
        }
    }

    /// <summary>
    /// 自動デフォルト設定がONの場合、現在のマテリアルをデフォルトに設定（Undo記録なし、内部用）
    /// </summary>
    private void AutoUpdateDefaultMaterials(MeshContext meshContext)
    {
        if (!_autoSetDefaultMaterials || meshContext == null || _model.Materials == null)
            return;

        _defaultMaterials = new List<Material>(_model.Materials);
        _defaultCurrentMaterialIndex = _model.CurrentMaterialIndex;
        SyncDefaultMaterialsToUndoContext();

        Debug.Log($"[MaterialUndo] Auto-updated default materials: {_defaultMaterials.Count} slots, currentIndex={_defaultCurrentMaterialIndex}");
    }

    /// <summary>
    /// デフォルトマテリアル情報をUndoコンテキストに同期
    /// </summary>
    private void SyncDefaultMaterialsToUndoContext()
    {
        // DefaultMaterials は ModelContext に集約済み - 同期不要
    }

}