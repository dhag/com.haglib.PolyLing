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
public partial class SimpleMeshFactory
{
    // ================================================================
    // 右ペイン：頂点エディタ（MeshDataベース）
    // ================================================================
    private void DrawVertexEditor()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
        {
            EditorGUILayout.LabelField("Vertex Editor", EditorStyles.boldLabel);

            if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
            {
                EditorGUILayout.HelpBox("メッシュを選択してください", MessageType.Info);
                return;
            }

            var entry = _meshList[_selectedIndex];
            var meshData = entry.Data;

            if (meshData == null)
            {
                EditorGUILayout.HelpBox("MeshDataが無効です", MessageType.Warning);
                return;
            }

            // メッシュ情報表示
            EditorGUILayout.LabelField($"Vertices: {meshData.VertexCount}");
            EditorGUILayout.LabelField($"Faces: {meshData.FaceCount}");
            EditorGUILayout.LabelField($"Triangles: {meshData.TriangleCount}");

            // 面タイプ内訳
            int triCount = meshData.Faces.Count(f => f.IsTriangle);
            int quadCount = meshData.Faces.Count(f => f.IsQuad);
            int nGonCount = meshData.FaceCount - triCount - quadCount;
            EditorGUILayout.LabelField($"  (Tri:{triCount}, Quad:{quadCount}, NGon:{nGonCount})", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Reset to Original"))
            {
                var before = _undoController?.CaptureMeshDataSnapshot();

                ResetMesh(entry);

                if (_undoController != null && before != null)
                {
                    var after = _undoController.CaptureMeshDataSnapshot();
                    _undoController.RecordTopologyChange(before, after, "Reset Mesh");
                }
            }

            EditorGUILayout.Space(5);

            // ================================================================
            // マテリアル管理機能（マルチマテリアル対応）
            // ================================================================
            DrawMaterialUI(entry);

            EditorGUILayout.Space(5);

            // ================================================================
            // 保存機能
            // ================================================================
            EditorGUILayout.LabelField("Save", EditorStyles.miniBoldLabel);

            // Export Transform設定（メッシュエントリごと）
            if (entry.ExportSettings != null)
            {
                ExportSettingsUI.DrawUI(entry.ExportSettings);
                EditorGUILayout.Space(4);
            }

            if (GUILayout.Button("Save Mesh Asset..."))
            {
                SaveMesh(entry);
            }

            if (GUILayout.Button("Save as Prefab..."))
            {
                SaveAsPrefab(entry);
            }

            if (GUILayout.Button("Add to Hierarchy"))
            {
                AddToHierarchy(entry);
            }

            EditorGUILayout.Space(10);

            // ================================================================
            // モデル保存/読み込み
            // ================================================================
            EditorGUILayout.LabelField("Model File", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Model..."))
            {
                ExportModel();
            }
            if (GUILayout.Button("Import Model..."))
            {
                ImportModel();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 頂点リスト
            _vertexScroll = EditorGUILayout.BeginScrollView(_vertexScroll);

            if (_vertexOffsets == null || _groupOffsets == null)
            {
                InitVertexOffsets();
            }

            if (_vertexOffsets == null || _groupOffsets == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            bool changed = false;

            if (Event.current.type == EventType.MouseDown && !_isSliderDragging)
            {
                BeginSliderDrag();
            }

            // 表示する頂点数を制限（パフォーマンス対策）
            const int MaxDisplayVertices = 20;
            int displayCount = Mathf.Min(meshData.VertexCount, MaxDisplayVertices);

            for (int i = 0; i < displayCount; i++)
            {
                var vertex = meshData.Vertices[i];

                EditorGUILayout.LabelField($"Vertex {i}", EditorStyles.miniBoldLabel);

                using (new EditorGUI.IndentLevelScope())
                {
                    Vector3 offset = i < _vertexOffsets.Length ? _vertexOffsets[i] : Vector3.zero;

                    // X
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("X", GUILayout.Width(15));
                    float newX = EditorGUILayout.Slider(offset.x, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    // Y
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Y", GUILayout.Width(15));
                    float newY = EditorGUILayout.Slider(offset.y, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    // Z
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Z", GUILayout.Width(15));
                    float newZ = EditorGUILayout.Slider(offset.z, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    Vector3 newOffset = new Vector3(newX, newY, newZ);
                    if (newOffset != offset && i < _vertexOffsets.Length)
                    {
                        _vertexOffsets[i] = newOffset;
                        _groupOffsets[i] = newOffset;

                        // MeshDataの頂点位置を更新
                        vertex.Position = entry.OriginalPositions[i] + newOffset;
                        changed = true;
                    }

                    // UV/Normal情報表示
                    EditorGUILayout.LabelField($"UVs: {vertex.UVs.Count}, Normals: {vertex.Normals.Count}", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(3);
            }

            // 残りの頂点数を表示
            if (meshData.VertexCount > MaxDisplayVertices)
            {
                int remaining = meshData.VertexCount - MaxDisplayVertices;
                EditorGUILayout.LabelField($"... and {remaining} more vertices", EditorStyles.centeredGreyMiniLabel);
            }

            if (changed)
            {
                SyncMeshFromData(entry);

                if (_undoController != null)
                {
                    _undoController.MeshContext.MeshData = meshData;
                }

                Repaint();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    // ================================================================
    // マテリアル管理UI（マルチマテリアル対応）
    // ================================================================

    /// <summary>
    /// マテリアルUIを描画
    /// </summary>
    private void DrawMaterialUI(MeshEntry entry)
    {
        EditorGUILayout.LabelField("Materials", EditorStyles.miniBoldLabel);

        // マテリアルリスト表示
        for (int i = 0; i < entry.Materials.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            // 選択マーカー
            string marker = (i == entry.CurrentMaterialIndex) ? "●" : "○";
            if (GUILayout.Button(marker, GUILayout.Width(20)))
            {
                entry.CurrentMaterialIndex = i;
                SyncMaterialsToUndoContext(entry);
                // 自動デフォルト設定
                AutoUpdateDefaultMaterials(entry);
            }

            // スロット番号
            EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(25));

            // マテリアルフィールド
            EditorGUI.BeginChangeCheck();
            Material newMat = (Material)EditorGUILayout.ObjectField(
                entry.Materials[i],
                typeof(Material),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                // Undo用スナップショット（変更前）
                var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

                entry.Materials[i] = newMat;
                SyncMaterialsToUndoContext(entry);

                // Undo記録
                RecordMaterialChange(beforeSnapshot, $"Change Material [{i}]");
            }

            // 削除ボタン（最後の1つは削除不可）
            EditorGUI.BeginDisabledGroup(entry.Materials.Count <= 1);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                RemoveMaterialSlot(entry, i);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        // マテリアル追加ボタン
        if (GUILayout.Button("+ Add Material Slot"))
        {
            // Undo用スナップショット（変更前）
            var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

            entry.Materials.Add(null);
            entry.CurrentMaterialIndex = entry.Materials.Count - 1;
            SyncMaterialsToUndoContext(entry);

            // Undo記録
            RecordMaterialChange(beforeSnapshot, "Add Material Slot");
        }

        // マテリアル一括追加ドロップエリア
        DrawMaterialDropArea(entry);

        // 現在選択中のマテリアルスロット表示
        EditorGUILayout.LabelField($"Current: [{entry.CurrentMaterialIndex}]", EditorStyles.miniLabel);

        // マテリアルがnullの場合の注意
        if (entry.GetCurrentMaterial() == null)
        {
            EditorGUILayout.HelpBox("None: デフォルト使用", MessageType.None);
        }

        EditorGUILayout.Space(3);

        // デフォルトマテリアル設定
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Set as Default", GUILayout.Width(100)))
        {
            SetCurrentMaterialsAsDefault(entry);
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
        DrawApplyMaterialToSelectionUI(entry);
    }

    /// <summary>
    /// 選択面へのマテリアル適用UI
    /// </summary>
    private void DrawApplyMaterialToSelectionUI(MeshEntry entry)
    {
        if (entry.Data == null || _selectionState == null)
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
            if (faceIdx >= 0 && faceIdx < entry.Data.FaceCount)
            {
                int matIdx = entry.Data.Faces[faceIdx].MaterialIndex;
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
        if (GUILayout.Button($"Apply Material [{entry.CurrentMaterialIndex}] to Selection"))
        {
            ApplyMaterialToSelectedFaces(entry, entry.CurrentMaterialIndex);
        }
    }

    /// <summary>
    /// 選択された面にマテリアルを適用
    /// </summary>
    private void ApplyMaterialToSelectedFaces(MeshEntry entry, int materialIndex)
    {
        if (entry.Data == null || _selectionState == null)
            return;

        if (_selectionState.Faces.Count == 0)
            return;

        // マテリアルインデックスの範囲チェック
        if (materialIndex < 0 || materialIndex >= entry.Materials.Count)
            return;

        // Undo用スナップショット
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        // 選択された面のマテリアルを変更
        bool changed = false;
        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx >= 0 && faceIdx < entry.Data.FaceCount)
            {
                var face = entry.Data.Faces[faceIdx];
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
            SyncMeshFromData(entry);

            // Undo記録
            if (_undoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
                _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot,
                    $"Apply Material [{materialIndex}] to {_selectionState.Faces.Count} faces");
            }

            Repaint();
        }
    }

    /// <summary>
    /// マテリアルスロットを削除
    /// </summary>
    private void RemoveMaterialSlot(MeshEntry entry, int index)
    {
        // 最後の1つは削除不可
        if (entry.Materials.Count <= 1)
            return;

        if (index < 0 || index >= entry.Materials.Count)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        // 削除するマテリアルを使用している面をスロット0に移動
        if (entry.Data != null)
        {
            foreach (var face in entry.Data.Faces)
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

        entry.Materials.RemoveAt(index);

        // CurrentMaterialIndexの調整
        if (entry.CurrentMaterialIndex >= entry.Materials.Count)
        {
            entry.CurrentMaterialIndex = entry.Materials.Count - 1;
        }
        else if (entry.CurrentMaterialIndex > index)
        {
            entry.CurrentMaterialIndex--;
        }
        else if (entry.CurrentMaterialIndex == index)
        {
            entry.CurrentMaterialIndex = 0;
        }

        // Undoコンテキストに同期
        SyncMaterialsToUndoContext(entry);

        // Undo記録
        RecordMaterialChange(beforeSnapshot, $"Remove Material Slot [{index}]");

        // メッシュを更新
        SyncMeshFromData(entry);
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
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count) return;

        var entry = _meshList[_selectedIndex];

        // 変更された頂点を検出
        var changedIndices = new List<int>();
        var oldPositions = new List<Vector3>();
        var newPositions = new List<Vector3>();

        for (int i = 0; i < _vertexOffsets.Length && i < _sliderDragStartOffsets.Length; i++)
        {
            if (Vector3.Distance(_vertexOffsets[i], _sliderDragStartOffsets[i]) > 0.0001f)
            {
                changedIndices.Add(i);
                oldPositions.Add(entry.OriginalPositions[i] + _sliderDragStartOffsets[i]);
                newPositions.Add(entry.OriginalPositions[i] + _vertexOffsets[i]);
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
    private void ResetMesh(MeshEntry entry)
    {
        if (entry.Data == null || entry.OriginalPositions == null)
            return;

        // 元の位置に戻す
        for (int i = 0; i < entry.Data.VertexCount && i < entry.OriginalPositions.Length; i++)
        {
            entry.Data.Vertices[i].Position = entry.OriginalPositions[i];
        }

        SyncMeshFromData(entry);

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
            _undoController.MeshContext.MeshData = entry.Data;
        }

        Repaint();
    }

    /// <summary>
    /// マテリアル一括追加ドロップエリア
    /// </summary>
    private void DrawMaterialDropArea(MeshEntry entry)
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

        if (isDragging)
        {
            EditorGUI.DrawRect(dropArea, new Color(0.3f, 0.5f, 0.8f, 0.3f));
        }

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
                        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

                        int addedCount = 0;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Material mat)
                            {
                                // 既存スロットにないか確認（重複防止はオプション）
                                entry.Materials.Add(mat);
                                addedCount++;
                            }
                        }

                        if (addedCount > 0)
                        {
                            entry.CurrentMaterialIndex = entry.Materials.Count - 1;
                            SyncMaterialsToUndoContext(entry);

                            // Undo記録
                            RecordMaterialChange(beforeSnapshot, $"Add {addedCount} Material(s)");

                            Debug.Log($"[MaterialUI] Added {addedCount} material(s). Total slots: {entry.Materials.Count}");
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
    private void SyncMaterialsToUndoContext(MeshEntry entry)
    {
        if (_undoController?.MeshContext != null && entry != null)
        {
            _undoController.MeshContext.Materials = new List<Material>(entry.Materials);
            _undoController.MeshContext.CurrentMaterialIndex = entry.CurrentMaterialIndex;
        }
    }

    /// <summary>
    /// マテリアル変更をUndo記録
    /// </summary>
    private void RecordMaterialChange(MeshDataSnapshot beforeSnapshot, string description)
    {
        if (beforeSnapshot == null || _undoController == null)
        {
            Debug.LogWarning($"[MaterialUndo] Cannot record: before={beforeSnapshot != null}, controller={_undoController != null}");
            return;
        }

        var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
        _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, description);
        Debug.Log($"[MaterialUndo] Recorded: {description}");

        // 自動デフォルト設定がONの場合、デフォルトを更新
        if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
        {
            AutoUpdateDefaultMaterials(_meshList[_selectedIndex]);
        }
    }

    /// <summary>
    /// 現在のマテリアルリストをデフォルトに設定
    /// </summary>
    private void SetCurrentMaterialsAsDefault(MeshEntry entry)
    {
        if (entry == null || entry.Materials == null)
            return;

        // Undo用スナップショット（変更前）
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        // デフォルトマテリアルを更新
        _defaultMaterials = new List<Material>(entry.Materials);
        _defaultCurrentMaterialIndex = entry.CurrentMaterialIndex;

        // Undoコンテキストに同期
        SyncDefaultMaterialsToUndoContext();

        // Undo記録
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
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
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        _autoSetDefaultMaterials = value;

        // Undoコンテキストに同期
        SyncDefaultMaterialsToUndoContext();

        // Undo記録
        if (beforeSnapshot != null && _undoController != null)
        {
            var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
            _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, $"Auto Default Materials: {(value ? "ON" : "OFF")}");
            Debug.Log($"[MaterialUndo] Auto default materials: {(value ? "ON" : "OFF")}");
        }
    }

    /// <summary>
    /// 自動デフォルト設定がONの場合、現在のマテリアルをデフォルトに設定（Undo記録なし、内部用）
    /// </summary>
    private void AutoUpdateDefaultMaterials(MeshEntry entry)
    {
        if (!_autoSetDefaultMaterials || entry == null || entry.Materials == null)
            return;

        _defaultMaterials = new List<Material>(entry.Materials);
        _defaultCurrentMaterialIndex = entry.CurrentMaterialIndex;
        SyncDefaultMaterialsToUndoContext();

        Debug.Log($"[MaterialUndo] Auto-updated default materials: {_defaultMaterials.Count} slots, currentIndex={_defaultCurrentMaterialIndex}");
    }

    /// <summary>
    /// デフォルトマテリアル情報をUndoコンテキストに同期
    /// </summary>
    private void SyncDefaultMaterialsToUndoContext()
    {
        if (_undoController?.MeshContext != null)
        {
            _undoController.MeshContext.DefaultMaterials = new List<Material>(_defaultMaterials);
            _undoController.MeshContext.DefaultCurrentMaterialIndex = _defaultCurrentMaterialIndex;
            _undoController.MeshContext.AutoSetDefaultMaterials = _autoSetDefaultMaterials;
        }
    }

}
