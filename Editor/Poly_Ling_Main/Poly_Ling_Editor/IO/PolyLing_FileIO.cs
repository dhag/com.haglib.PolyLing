// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_FileIO.cs
// モデルファイル入出力機能
// - プロジェクトエクスポート
// - プロジェクトインポート
// - マテリアル取得ヘルパー

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Serialization;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

public partial class PolyLing
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

        // EditorState を作成（v2.0: カテゴリ別選択インデックス対応）
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
            selectedMeshIndex = _model.PrimarySelectedMeshIndex,
            selectedBoneIndex = _model.PrimarySelectedBoneIndex,
            selectedVertexMorphIndex = _model.PrimarySelectedMorphIndex,
            coordinateScale = _undoController?.EditorState?.CoordinateScale ?? 0.085f,
            pmxFlipZ = _undoController?.EditorState?.PmxFlipZ ?? false,
            mqoFlipZ = _undoController?.EditorState?.MqoFlipZ ?? true,
            mqoPmxRatio = _undoController?.EditorState?.MqoPmxRatio ?? 10f
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

            // 座標系設定を復元
            if (_undoController?.EditorState != null)
            {
                _undoController.EditorState.CoordinateScale = state.coordinateScale > 0f ? state.coordinateScale : 0.085f;
                _undoController.EditorState.PmxFlipZ = state.pmxFlipZ;
                _undoController.EditorState.MqoFlipZ = state.mqoFlipZ;
                _undoController.EditorState.MqoPmxRatio = state.mqoPmxRatio > 0f ? state.mqoPmxRatio : 10f;
            }

            // 選択メッシュを復元（v2.0: カテゴリ別対応）
            _model.ClearAllCategorySelection();
            
            // メッシュカテゴリ
            if (state.selectedMeshIndex >= 0 && state.selectedMeshIndex < _meshContextList.Count)
            {
                _model.SelectMesh(state.selectedMeshIndex);
                _selectedIndex = state.selectedMeshIndex;

                // 選択頂点を復元
                var selectedMeshContextData = modelDTO.meshDTOList[state.selectedMeshIndex];
                _selectedVertices = ModelSerializer.ToSelectedVertices(selectedMeshContextData);
            }
            
            // ボーンカテゴリ
            if (state.selectedBoneIndex >= 0 && state.selectedBoneIndex < _meshContextList.Count)
            {
                _model.SelectBone(state.selectedBoneIndex);
            }
            
            // モーフカテゴリ
            if (state.selectedVertexMorphIndex >= 0 && state.selectedVertexMorphIndex < _meshContextList.Count)
            {
                _model.SelectMorph(state.selectedVertexMorphIndex);
            }
            
            // フォールバック: 何も選択されていない場合
            if (!_model.HasSelection && _meshContextList.Count > 0)
            {
                _selectedIndex = 0;
                _model.Select(0);
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
            _model.Select(0);
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
            // v2.0: 新API使用
            _undoController.MeshListContext.Select(_selectedIndex);
        }

        Debug.Log($"[PolyLing] Imported project: {projectDTO.name} ({_meshContextList.Count} meshes, {_model.Materials?.Count ?? 0} materialPathList)");
        Repaint();
    }

    // ================================================================
    // マテリアル取得ヘルパー
    // ================================================================


    /// <summary>
    /// 保存用のマテリアル配列を取得（マルチマテリアル対応）
    /// meshContextがnullの場合はモデル全体のマテリアルを返す
    /// _saveOnMemoryMaterialsがtrueの場合、オンメモリマテリアルをアセットとして保存
    /// </summary>
    private Material[] GetMaterialsForSave(MeshContext meshContext)
    {
        var matRefs = _model.MaterialReferences;
        if (matRefs == null || matRefs.Count == 0)
        {
            return new Material[] { GetOrCreateDefaultMaterial() };
        }
        
        var result = new Material[matRefs.Count];
        
        // オンメモリマテリアル保存用のディレクトリ
        string saveDir = null;
        string textureDir = null;
        if (_saveOnMemoryMaterials)
        {
            saveDir = GetMaterialSaveDirectory();
            textureDir = $"{saveDir}/Textures";
            
            // ディレクトリを作成
            if (!System.IO.Directory.Exists(saveDir))
            {
                System.IO.Directory.CreateDirectory(saveDir);
            }
            if (!System.IO.Directory.Exists(textureDir))
            {
                System.IO.Directory.CreateDirectory(textureDir);
            }
            AssetDatabase.Refresh();
        }
        
        for (int i = 0; i < matRefs.Count; i++)
        {
            var matRef = matRefs[i];
            if (matRef == null)
            {
                result[i] = GetOrCreateDefaultMaterial();
                continue;
            }
            
            // マテリアルを取得（MaterialReferenceから）
            var srcMat = matRef.Material;
            if (srcMat == null)
            {
                result[i] = GetOrCreateDefaultMaterial();
                continue;
            }
            
            // マテリアルをコピー
            var copiedMat = new Material(srcMat);
            copiedMat.name = srcMat.name;
            
            // オンメモリマテリアルの保存処理
            if (_saveOnMemoryMaterials && !matRef.HasAssetPath)
            {
                // テクスチャの保存処理
                SaveMaterialTextures(copiedMat, textureDir);
                
                // 保存パスを生成
                string matName = !string.IsNullOrEmpty(copiedMat.name) ? copiedMat.name : $"Material_{i}";
                matName = SanitizeFileName(matName);
                string savePath = $"{saveDir}/{matName}.mat";
                
                // 既存アセットの処理
                var existingMat = AssetDatabase.LoadAssetAtPath<Material>(savePath);
                if (existingMat != null)
                {
                    if (_overwriteExistingAssets)
                    {
                        // 上書き: 既存アセットのプロパティを更新
                        EditorUtility.CopySerialized(copiedMat, existingMat);
                        AssetDatabase.SaveAssets();
                        copiedMat = existingMat;
                        Debug.Log($"[GetMaterialsForSave] Overwritten: {savePath}");
                    }
                    else
                    {
                        // 重複チェック（連番サフィックス）
                        int counter = 1;
                        while (AssetDatabase.LoadAssetAtPath<Material>(savePath) != null)
                        {
                            savePath = $"{saveDir}/{matName}_{counter}.mat";
                            counter++;
                        }
                        
                        // 新規アセットとして保存
                        AssetDatabase.CreateAsset(copiedMat, savePath);
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[GetMaterialsForSave] Saved: {savePath}");
                    }
                }
                else
                {
                    // 新規アセットとして保存
                    AssetDatabase.CreateAsset(copiedMat, savePath);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[GetMaterialsForSave] Saved: {savePath}");
                }
            }
            
            result[i] = copiedMat;
        }

        // デバッグ: マテリアル配列の内容を確認（最初の10個）
        Debug.Log($"[GetMaterialsForSave] Total materials: {result.Length}");
        for (int i = 0; i < Mathf.Min(10, result.Length); i++)
        {
            Debug.Log($"[GetMaterialsForSave] Mat[{i}] = '{result[i]?.name ?? "null"}'");
        }

        return result;
    }
    
    /// <summary>
    /// マテリアルのテクスチャをアセットとして保存
    /// アセットパスを持たないテクスチャのみ保存
    /// </summary>
    private void SaveMaterialTextures(Material material, string textureDir)
    {
        if (material == null || string.IsNullOrEmpty(textureDir)) return;
        
        // 主要なテクスチャプロパティ名
        string[] textureProperties = new string[]
        {
            "_MainTex", "_BaseMap",           // Diffuse/Albedo
            "_BumpMap", "_NormalMap",         // Normal
            "_EmissionMap",                   // Emission
            "_MetallicGlossMap", "_SpecGlossMap", // Metallic/Specular
            "_OcclusionMap",                  // Occlusion
            "_ParallaxMap", "_HeightMap",     // Height
            "_DetailAlbedoMap", "_DetailNormalMap" // Detail
        };
        
        foreach (var propName in textureProperties)
        {
            if (!material.HasProperty(propName)) continue;
            
            var texture = material.GetTexture(propName) as Texture2D;
            if (texture == null) continue;
            
            // アセットパスがあればスキップ（既にAssets内にある）
            string existingPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(existingPath)) continue;
            
            // テクスチャを保存
            string texName = !string.IsNullOrEmpty(texture.name) ? texture.name : $"Texture_{propName}";
            texName = SanitizeFileName(texName);
            string savePath = $"{textureDir}/{texName}.png";
            
            // 重複チェック
            int counter = 1;
            while (System.IO.File.Exists(savePath))
            {
                savePath = $"{textureDir}/{texName}_{counter}.png";
                counter++;
            }
            
            try
            {
                // テクスチャをPNGとして保存
                byte[] pngData = texture.EncodeToPNG();
                if (pngData != null)
                {
                    System.IO.File.WriteAllBytes(savePath, pngData);
                    AssetDatabase.ImportAsset(savePath);
                    
                    // インポートしたテクスチャをマテリアルに設定
                    var savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
                    if (savedTexture != null)
                    {
                        material.SetTexture(propName, savedTexture);
                        Debug.Log($"[SaveMaterialTextures] Saved texture: {savePath}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveMaterialTextures] Failed to save texture {texName}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// マテリアル保存先ディレクトリを取得
    /// 既存フォルダがあれば別名（_1, _2...）を使用
    /// </summary>
    private string GetMaterialSaveDirectory()
    {
        // カスタムフォルダが指定されている場合はそれを使用
        if (!string.IsNullOrEmpty(_materialSaveFolder))
        {
            return _materialSaveFolder;
        }
        
        // デフォルト: Assets/SavedMaterials/モデル名
        string modelName = SanitizeFileName(_model.Name ?? "Model");
        string baseDir = $"Assets/SavedMaterials/{modelName}";
        
        // 既存フォルダがあれば別名を使用
        if (AssetDatabase.IsValidFolder(baseDir))
        {
            int counter = 1;
            string newDir = $"{baseDir}_{counter}";
            while (AssetDatabase.IsValidFolder(newDir))
            {
                counter++;
                newDir = $"{baseDir}_{counter}";
            }
            return newDir;
        }
        
        return baseDir;
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
