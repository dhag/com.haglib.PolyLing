// Assets/Editor/Poly_Ling/PolyLing/PolyLing_RightPane.cs
// 右ペインUI描画（外枠、キャプチャ、モデルファイルIO、頂点エディタ呼び出し）

using UnityEditor;
using UnityEngine;
using Poly_Ling.Localization;
using Poly_Ling.Remote;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using System.Collections.Generic;
using System.Linq;

public partial class PolyLing
{
    // ================================================================
    // 右ペイン
    // ================================================================
    private void DrawRightPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_rightPaneWidth));
        EditorGUILayout.LabelField(L.Get("VertexEditor"), EditorStyles.boldLabel);

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

        EditorGUILayout.Space(5);

        // ================================================================
        // 頂点エディタ（スクロールコンテンツ）
        // ================================================================
        DrawRightPane_();

        EditorGUILayout.EndVertical();
    }

    // ================================================================
    // 右ペイン：スクロール位置
    // ================================================================
    private Vector2 _rightPaneScroll;

    // ================================================================
    // 右ペイン：頂点エディタ（MeshObjectベース）
    // DrawRightPane() から呼ばれるスクロール内コンテンツ
    // ================================================================
    private void DrawRightPane_()
    {
        // スクロール開始
        _rightPaneScroll = EditorGUILayout.BeginScrollView(_rightPaneScroll);

        // ================================================================
        // 座標系設定（右ペイン最上部）
        // ================================================================
        DrawCoordinateSettings();

        EditorGUILayout.Space(3);

        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext == null)
        {
            EditorGUILayout.HelpBox(L.Get("SelectMesh"), MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        var meshObject = meshContext.MeshObject;

        if (meshObject == null)
        {
            EditorGUILayout.HelpBox(L.Get("InvalidMeshData"), MessageType.Warning);
            EditorGUILayout.EndScrollView();
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
        // 保存機能
        // ================================================================
        EditorGUILayout.LabelField(L.Get("Save"), EditorStyles.miniBoldLabel);

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
        // BlendShape焼き込み チェックボックス（Undo対応）
        // モーフエクスプレッションがある場合のみ表示
        // ================================================================
        if (_model.HasMorphExpressions)
        {
            EditorGUI.BeginChangeCheck();
            bool newBakeBlendShapes = EditorGUILayout.Toggle(L.Get("BakeBlendShapes"), _bakeBlendShapes);
            if (EditorGUI.EndChangeCheck() && newBakeBlendShapes != _bakeBlendShapes)
            {
                if (_undoController != null)
                {
                    _undoController.BeginEditorStateDrag();
                }

                _bakeBlendShapes = newBakeBlendShapes;

                if (_undoController != null)
                {
                    _undoController.EditorState.BakeBlendShapes = _bakeBlendShapes;
                    _undoController.EndEditorStateDrag("Toggle Bake BlendShapes");
                }
            }
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

        EditorGUILayout.EndScrollView();
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
        var meshContext = _model.FirstSelectedMeshContext;
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

        Repaint();
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

    // ================================================================
    // 座標系設定（右ペイン最上部）
    // ================================================================
    private void DrawCoordinateSettings()
    {
        bool foldCoordinate = DrawFoldoutWithUndo("CoordinateSettings", L.Get("CoordinateSettings"), false);
        if (!foldCoordinate) return;

        if (_undoController == null) return;

        var editorState = _undoController.EditorState;

        EditorGUI.indentLevel++;

        // PMX→Unity Ratio
        EditorGUI.BeginChangeCheck();
        float newPmxRatio = EditorGUILayout.FloatField(L.Get("PmxUnityRatio"), editorState.PmxUnityRatio);
        if (EditorGUI.EndChangeCheck() && newPmxRatio != editorState.PmxUnityRatio)
        {
            _undoController.BeginEditorStateDrag();
            editorState.PmxUnityRatio = newPmxRatio;
            _undoController.EndEditorStateDrag("Change PMX→Unity Ratio");
        }

        // PMX Flip Z
        EditorGUI.BeginChangeCheck();
        bool newPmxFlipZ = EditorGUILayout.Toggle(L.Get("PmxFlipZ"), editorState.PmxFlipZ);
        if (EditorGUI.EndChangeCheck() && newPmxFlipZ != editorState.PmxFlipZ)
        {
            _undoController.BeginEditorStateDrag();
            editorState.PmxFlipZ = newPmxFlipZ;
            _undoController.EndEditorStateDrag("Toggle PMX FlipZ");
        }

        // MQO Flip Z
        EditorGUI.BeginChangeCheck();
        bool newMqoFlipZ = EditorGUILayout.Toggle(L.Get("MqoFlipZ"), editorState.MqoFlipZ);
        if (EditorGUI.EndChangeCheck() && newMqoFlipZ != editorState.MqoFlipZ)
        {
            _undoController.BeginEditorStateDrag();
            editorState.MqoFlipZ = newMqoFlipZ;
            _undoController.EndEditorStateDrag("Toggle MQO FlipZ");
        }

        // MQO→Unity Ratio
        EditorGUI.BeginChangeCheck();
        float newMqoRatio = EditorGUILayout.FloatField(L.Get("MqoUnityRatio"), editorState.MqoUnityRatio);
        if (EditorGUI.EndChangeCheck() && newMqoRatio != editorState.MqoUnityRatio)
        {
            _undoController.BeginEditorStateDrag();
            editorState.MqoUnityRatio = newMqoRatio;
            _undoController.EndEditorStateDrag("Change MQO→Unity Ratio");
        }

        EditorGUI.indentLevel--;
    }

}