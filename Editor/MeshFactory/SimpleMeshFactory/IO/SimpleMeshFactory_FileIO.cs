// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_FileIO.cs
// モデルファイル入出力機能
// - プロジェクトエクスポート
// - プロジェクトインポート
// - マテリアル取得ヘルパー

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Serialization;
using MeshFactory.UndoSystem;

public partial class SimpleMeshFactory
{
    // ================================================================
    // モデルファイル入出力
    // ================================================================

    /// <summary>
    /// モデルをファイルにエクスポート
    /// </summary>
    private void ExportModel()
    {
        if (_meshContextList.Count == 0)
        {
            EditorUtility.DisplayDialog("Export Project", "エクスポートするメッシュがありません。", "OK");
            return;
        }

        // プロジェクトデータを作成
        string projectName = _model.Name ?? (_meshContextList.Count > 0 ? _meshContextList[0].Name : "Project");
        var projectDTO = ProjectDTO.Create(projectName);

        // EditorState を作成
        var editorStateDTO = new EditorStateDTO
        {
            rotationX = _rotationX,
            rotationY = _rotationY,
            cameraDistance = _cameraDistance,
            cameraTarget = new float[] { _cameraTarget.x, _cameraTarget.y, _cameraTarget.z },
            showWireframe = _showWireframe,
            showVertices = _showVertices,
            vertexEditMode = _vertexEditMode,
            currentToolName = _currentTool?.Name ?? "Select",
            selectedMeshIndex = _selectedIndex
        };

        // ModelSerializer.FromModelContext を使用してモデル全体をエクスポート
        // これにより Materials も正しく保存される
        var modelDTO = ModelSerializer.FromModelContext(
            _model,
            _undoController?.WorkPlane,
            editorStateDTO
        );

        if (modelDTO != null)
        {
            // 選択頂点を設定（FromModelContext では設定されないため）
            if (_selectedIndex >= 0 && _selectedIndex < modelDTO.meshDTOList.Count)
            {
                modelDTO.meshDTOList[_selectedIndex].selectedVertices = _selectedVertices.ToList();
            }

            projectDTO.models.Add(modelDTO);
        }

        ProjectSerializer.ExportWithDialog(projectDTO, projectName);
    }

    /// <summary>
    /// ファイルからモデルをインポート（Undo対応）
    /// </summary>
    private void ImportModel()
    {
        var projectDTO = ProjectSerializer.ImportWithDialog();
        if (projectDTO == null || projectDTO.models.Count == 0) return;

        // 最初のモデルを使用
        var modelDTO = projectDTO.models[0];

        // 確認ダイアログ
        if (_meshContextList.Count > 0)
        {
            bool result = EditorUtility.DisplayDialog(
                "Import Project",
                "現在のデータを破棄して読み込みますか？\n（Ctrl+Zで元に戻せます）",
                "はい", "キャンセル"
            );
            if (!result) return;
        }

        // Undo記録用：既存メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            removedSnapshots.Add((i, snapshot));
        }
        int oldSelectedIndex = _selectedIndex;

        // 変更前のカメラ状態を保存
        CameraSnapshot oldCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // 既存メッシュをクリア（UnityMeshを破棄）
        CleanupMeshes();
        _meshContextList.Clear();
        _selectedIndex = -1;
        _selectedVertices.Clear();
        // 注意: VertexEditStackはクリアしない（Undo可能にするため）

        // ModelSerializer.ToModelContext を使用してモデル全体を復元
        // これにより Materials も正しく復元される
        ModelSerializer.ToModelContext(modelDTO, _model);

        // WorkPlane復元
        if (modelDTO.workPlane != null && _undoController?.WorkPlane != null)
        {
            ModelSerializer.ApplyToWorkPlane(modelDTO.workPlane, _undoController.WorkPlane);
        }

        // EditorState復元
        if (modelDTO.editorStateDTO != null)
        {
            var state = modelDTO.editorStateDTO;
            _rotationX = state.rotationX;
            _rotationY = state.rotationY;
            _cameraDistance = state.cameraDistance;
            if (state.cameraTarget != null && state.cameraTarget.Length >= 3)
            {
                _cameraTarget = new Vector3(state.cameraTarget[0], state.cameraTarget[1], state.cameraTarget[2]);
            }
            _showWireframe = state.showWireframe;
            _showVertices = state.showVertices;
            _vertexEditMode = state.vertexEditMode;

            // 選択メッシュを復元
            if (state.selectedMeshIndex >= 0 && state.selectedMeshIndex < _meshContextList.Count)
            {
                _selectedIndex = state.selectedMeshIndex;

                // 選択頂点を復元
                var selectedMeshContextData = modelDTO.meshDTOList[state.selectedMeshIndex];
                _selectedVertices = ModelSerializer.ToSelectedVertices(selectedMeshContextData);
            }
            else if (_meshContextList.Count > 0)
            {
                _selectedIndex = 0;
            }

            // ツールを復元（名前で検索）
            if (!string.IsNullOrEmpty(state.currentToolName))
            {
                SetToolByName(state.currentToolName);
            }
        }
        else if (_meshContextList.Count > 0)
        {
            _selectedIndex = 0;
        }

        // 変更後のカメラ状態を保存
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録用：新メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            addedSnapshots.Add((i, snapshot));
        }

        // オフセット配列を初期化
        InitVertexOffsets();

        // UndoContextを更新
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
            if (_undoController != null)
            {
                _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
                _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
                _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
                _undoController.MeshUndoContext.SelectedVertices = _selectedVertices;
            }
        }

        // Undo記録（プロジェクトインポート）
        if (_undoController != null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndex = oldSelectedIndex,
                NewSelectedIndex = _selectedIndex,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.MeshListStack.Record(record, $"Import Project: {projectDTO.name}");
            _undoController.FocusMeshList();
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
        }

        Debug.Log($"[SimpleMeshFactory] Imported project: {projectDTO.name} ({_meshContextList.Count} meshes, {_model.Materials?.Count ?? 0} materialPathList)");
        Repaint();
    }

    // ================================================================
    // マテリアル取得ヘルパー
    // ================================================================

    /// <summary>
    /// 保存用のマテリアル配列を取得（マルチマテリアル対応）
    /// meshContextがnullの場合はモデル全体のマテリアルを返す
    /// マテリアルはコピーして返す（シーン上のオブジェクトが独立したインスタンスを持つため）
    /// </summary>
    private Material[] GetMaterialsForSave(MeshContext meshContext)
    {
        // モデルのマテリアルを使用（meshContextがnullでも有効）
        if (_model.Materials.Count > 0)
        {
            var result = new Material[_model.Materials.Count];

            // オンメモリマテリアル保存用のディレクトリ
            string saveDir = null;
            if (_saveOnMemoryMaterials)
            {
                string modelName = SanitizeFileName(_model.Name ?? "Model");
                saveDir = $"Assets/SavedMaterials/{modelName}";
            }

            for (int i = 0; i < _model.Materials.Count; i++)
            {
                var srcMat = _model.Materials[i];
                if (srcMat != null)
                {
                    // マテリアルをコピー
                    var copiedMat = new Material(srcMat);
                    copiedMat.name = srcMat.name;

                    // オンメモリマテリアルの保存処理（コピー後のマテリアルを保存）
                    if (_saveOnMemoryMaterials)
                    {
                        string existingPath = AssetDatabase.GetAssetPath(srcMat);
                        if (string.IsNullOrEmpty(existingPath))
                        {
                            // コピーしたマテリアルをアセットとして保存
                            var savedMat = SaveOnMemoryMaterial(copiedMat, saveDir, i);
                            if (savedMat != null)
                            {
                                copiedMat = savedMat;
                            }
                        }
                    }

                    result[i] = copiedMat;
                }
                else
                {
                    result[i] = GetOrCreateDefaultMaterial();
                }
            }

            // デバッグ: マテリアル配列の内容を確認（最初の10個）
            Debug.Log($"[GetMaterialsForSave] Total materials: {result.Length}");
            for (int i = 0; i < Mathf.Min(10, result.Length); i++)
            {
                Debug.Log($"[GetMaterialsForSave] Mat[{i}] = '{result[i]?.name ?? "null"}'");
            }

            return result;
        }
        return new Material[] { GetOrCreateDefaultMaterial() };
    }

    /// <summary>
    /// オンメモリマテリアルをアセットとして保存
    /// </summary>
    private Material SaveOnMemoryMaterial(Material mat, string saveDir, int index)
    {
        if (mat == null || string.IsNullOrEmpty(saveDir))
            return mat;

        try
        {
            // ディレクトリを作成
            if (!System.IO.Directory.Exists(saveDir))
            {
                System.IO.Directory.CreateDirectory(saveDir);
                AssetDatabase.Refresh();
            }

            // ファイル名を生成
            string matName = !string.IsNullOrEmpty(mat.name) ? mat.name : $"Material_{index}";
            matName = SanitizeFileName(matName);
            string savePath = $"{saveDir}/{matName}.mat";

            // 重複チェック
            int counter = 1;
            while (AssetDatabase.LoadAssetAtPath<Material>(savePath) != null)
            {
                savePath = $"{saveDir}/{matName}_{counter}.mat";
                counter++;
            }

            // 新しいマテリアルを作成して保存
            Material newMat = new Material(mat);
            newMat.name = System.IO.Path.GetFileNameWithoutExtension(savePath);

            AssetDatabase.CreateAsset(newMat, savePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SaveOnMemoryMaterial] Saved: {savePath}");
            return newMat;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveOnMemoryMaterial] Failed: {e.Message}");
            return mat;
        }
    }

    /// <summary>
    /// ファイル名として使用できない文字を除去
    /// </summary>
    private string SanitizeFileName(string name)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    /// <summary>
    /// 保存用のマテリアルを取得（単一、後方互換用）
    /// </summary>
    private Material GetMaterialForSave(MeshContext meshContext)
    {
        // メッシュコンテキストのマテリアルがあればそれを使用
        if (meshContext != null && _model.Materials.Count > 0 && _model.Materials[0] != null)
        {
            return _model.Materials[0];
        }

        // なければデフォルトマテリアルを作成/取得
        return GetOrCreateDefaultMaterial();
    }
}