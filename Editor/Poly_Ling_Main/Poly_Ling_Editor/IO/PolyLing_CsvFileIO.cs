// Assets/Editor/Poly_Ling/PolyLing/PolyLing_CsvFileIO.cs
// CSVフォルダ形式のプロジェクト保存/読み込み

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.UndoSystem;
using Poly_Ling.Tools;

public partial class PolyLing
{
    // ================================================================
    // CSVフォルダ形式: エクスポート
    // ================================================================

    /// <summary>
    /// プロジェクト全体をCSVフォルダ形式でエクスポート
    /// </summary>
    private void ExportModelCsv()
    {
        if (_project.ModelCount == 0)
        {
            EditorUtility.DisplayDialog("Export Project (CSV)", "エクスポートするメッシュがありません。", "OK");
            return;
        }

        // EditorState を作成（カレントモデル用）
        var editorStateDTO = CreateEditorStateDTO();

        // WorkPlane
        var workPlane = _undoController?.WorkPlane;

        CsvProjectSerializer.ExportWithDialog(
            _project,
            workPlane != null ? new List<WorkPlaneContext> { workPlane } : null,
            editorStateDTO != null ? new List<EditorStateDTO> { editorStateDTO } : null,
            _project.Name ?? "Project"
        );
    }

    // ================================================================
    // CSVフォルダ形式: インポート
    // ================================================================

    /// <summary>
    /// CSVフォルダ形式からプロジェクト/モデルをインポート（Undo対応）
    /// project.csv があればプロジェクト全体を復元、model.csv のみなら単一モデル復元
    /// </summary>
    private void ImportModelCsv()
    {
        string folderPath = EditorUtility.OpenFolderPanel(
            "Import Project Folder",
            Application.dataPath,
            ""
        );
        if (string.IsNullOrEmpty(folderPath)) return;

        string projectCsvPath = System.IO.Path.Combine(folderPath, "project.csv");
        if (System.IO.File.Exists(projectCsvPath))
        {
            ImportProjectCsv(folderPath);
        }
        else
        {
            string modelCsvPath = System.IO.Path.Combine(folderPath, "model.csv");
            if (System.IO.File.Exists(modelCsvPath))
            {
                ImportSingleModelCsv(folderPath);
            }
            else
            {
                Debug.LogError($"[PolyLing] No project.csv or model.csv found in: {folderPath}");
            }
        }
    }

    /// <summary>
    /// project.csv からプロジェクト全体を復元
    /// 第1モデルは既存_modelオブジェクトにデータ転送（参照維持）
    /// 追加モデルは_project.AddModelで追加
    /// </summary>
    private void ImportProjectCsv(string folderPath)
    {
        var loadedProject = CsvProjectSerializer.Import(folderPath, out var editorStates, out var workPlanes);
        if (loadedProject == null || loadedProject.ModelCount == 0)
        {
            Debug.LogError("[PolyLing] Failed to load project or no models found.");
            return;
        }

        // 確認ダイアログ
        if (_meshContextList != null && _meshContextList.Count > 0)
        {
            bool result = EditorUtility.DisplayDialog(
                "Import Project (CSV)",
                $"現在のデータを破棄して読み込みますか？\n読み込み: {loadedProject.ModelCount} モデル\n（Ctrl+Zで元に戻せます）",
                "はい", "キャンセル"
            );
            if (!result) return;
        }

        // Undo記録用：既存メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        if (_meshContextList != null)
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
                removedSnapshots.Add((i, snapshot));
            }
        }
        var oldSelectedIndices = _model?.CaptureAllSelectedIndices();

        // 変更前のカメラ状態を保存
        CameraSnapshot oldCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // ================================================================
        // 追加モデル（index 1+）を除去（RemoveModelAtがリソース解放も行う）
        // ================================================================
        for (int i = _project.ModelCount - 1; i >= 1; i--)
        {
            _project.RemoveModelAt(i);
        }

        // ================================================================
        // 第1モデル: 既存_modelオブジェクトにデータ転送（参照維持）
        // ================================================================
        var firstLoaded = loadedProject.Models[0];

        // 既存メッシュをクリア
        CleanupMeshes();
        _meshContextList.Clear();
        SetSelectedIndex(-1);
        _selectionState.Vertices.Clear();

        // _model に転送
        _model.Clear(false);
        _model.Name = firstLoaded.Name;

        for (int i = 0; i < firstLoaded.MeshContextCount; i++)
        {
            var mc = firstLoaded.MeshContextList[i];
            _model.Add(mc);
        }

        // Materials 復元
        if (firstLoaded.MaterialReferences != null && firstLoaded.MaterialReferences.Count > 0)
        {
            _model.MaterialReferences = firstLoaded.MaterialReferences;
            _model.CurrentMaterialIndex = firstLoaded.CurrentMaterialIndex;
        }
        if (firstLoaded.DefaultMaterialReferences != null && firstLoaded.DefaultMaterialReferences.Count > 0)
        {
            _model.DefaultMaterialReferences = firstLoaded.DefaultMaterialReferences;
            _model.DefaultCurrentMaterialIndex = firstLoaded.DefaultCurrentMaterialIndex;
            _model.AutoSetDefaultMaterials = firstLoaded.AutoSetDefaultMaterials;
        }

        // HumanoidMapping 復元
        if (firstLoaded.HumanoidMapping != null && !firstLoaded.HumanoidMapping.IsEmpty)
        {
            var dict = firstLoaded.HumanoidMapping.ToDictionary();
            _model.HumanoidMapping.FromDictionary(dict);
        }

        // MorphExpressions 復元
        if (firstLoaded.MorphExpressions != null && firstLoaded.MorphExpressions.Count > 0)
        {
            _model.MorphExpressions = firstLoaded.MorphExpressions;
        }

        // TPoseBackup 復元
        _model.TPoseBackup = firstLoaded.TPoseBackup;

        // ================================================================
        // 追加モデル（index 1+）: AddModel
        // ================================================================
        for (int i = 1; i < loadedProject.ModelCount; i++)
        {
            _project.AddModel(loadedProject.Models[i]);
        }

        // ================================================================
        // WorkPlane / EditorState 復元
        // ================================================================
        if (workPlanes != null && workPlanes.Count > 0 && _undoController?.WorkPlane != null)
        {
            var loadedWP = workPlanes[0];
            if (loadedWP != null)
            {
                var wp = _undoController.WorkPlane;
                wp.Mode = loadedWP.Mode;
                wp.Origin = loadedWP.Origin;
                wp.AxisU = loadedWP.AxisU;
                wp.AxisV = loadedWP.AxisV;
                wp.IsLocked = loadedWP.IsLocked;
                wp.LockOrientation = loadedWP.LockOrientation;
                wp.AutoUpdateOriginOnSelection = loadedWP.AutoUpdateOriginOnSelection;
            }
        }

        if (editorStates != null && editorStates.Count > 0 && editorStates[0] != null)
        {
            ApplyEditorState(editorStates[0]);
        }
        else if (_meshContextList != null && _meshContextList.Count > 0)
        {
            SetSelectedIndex(0);
        }

        // ================================================================
        // 後処理
        // ================================================================
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        if (_meshContextList != null)
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
                addedSnapshots.Add((i, snapshot));
            }
        }

        InitVertexOffsets();

        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext != null && _undoController != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = _selectionState.Vertices;
        }

        if (_undoController != null)
        {
            var newSelectedIndices = _model?.CaptureAllSelectedIndices();
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndices = oldSelectedIndices,
                NewSelectedIndices = newSelectedIndices,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.MeshListStack.Record(record, $"Import CSV Project: {loadedProject.Name} ({loadedProject.ModelCount} models)");
            _undoController.FocusMeshList();
        }

        _project.Name = loadedProject.Name;
        Debug.Log($"[PolyLing] Imported CSV project: {loadedProject.Name} ({loadedProject.ModelCount} models, {_meshContextList?.Count ?? 0} meshes in current)");
        Repaint();
    }

    /// <summary>
    /// model.csv から単一モデルを復元（従来ロジック）
    /// </summary>
    private void ImportSingleModelCsv(string folderPath)
    {
        EditorStateDTO loadedEditorState;
        WorkPlaneContext loadedWorkPlane;
        List<CsvMeshEntry> additionalEntries;

        var loadedModel = CsvModelSerializer.LoadModel(folderPath, out loadedEditorState, out loadedWorkPlane, out additionalEntries);

        if (loadedModel == null || loadedModel.MeshContextCount == 0) return;

        // 確認ダイアログ
        if (_meshContextList.Count > 0)
        {
            bool result = EditorUtility.DisplayDialog(
                "Import Project (CSV)",
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
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();

        // 変更前のカメラ状態を保存
        CameraSnapshot oldCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // 既存メッシュをクリア
        CleanupMeshes();
        _meshContextList.Clear();
        SetSelectedIndex(-1);
        _selectionState.Vertices.Clear();

        // loadedModel から _model にデータ転送
        _model.Clear(false);
        _model.Name = loadedModel.Name;

        for (int i = 0; i < loadedModel.MeshContextCount; i++)
        {
            var mc = loadedModel.MeshContextList[i];
            _model.Add(mc);
        }

        // 追加エントリのマージ（他モデルのCSV）
        if (additionalEntries != null && additionalEntries.Count > 0)
        {
            MergeAdditionalEntries(additionalEntries);
        }

        // Materials 復元
        if (loadedModel.MaterialReferences != null && loadedModel.MaterialReferences.Count > 0)
        {
            _model.MaterialReferences = loadedModel.MaterialReferences;
            _model.CurrentMaterialIndex = loadedModel.CurrentMaterialIndex;
        }
        if (loadedModel.DefaultMaterialReferences != null && loadedModel.DefaultMaterialReferences.Count > 0)
        {
            _model.DefaultMaterialReferences = loadedModel.DefaultMaterialReferences;
            _model.DefaultCurrentMaterialIndex = loadedModel.DefaultCurrentMaterialIndex;
            _model.AutoSetDefaultMaterials = loadedModel.AutoSetDefaultMaterials;
        }

        // HumanoidMapping 復元
        if (loadedModel.HumanoidMapping != null && !loadedModel.HumanoidMapping.IsEmpty)
        {
            var dict = loadedModel.HumanoidMapping.ToDictionary();
            _model.HumanoidMapping.FromDictionary(dict);
        }

        // MorphExpressions 復元
        if (loadedModel.MorphExpressions != null && loadedModel.MorphExpressions.Count > 0)
        {
            _model.MorphExpressions = loadedModel.MorphExpressions;
        }

        // TPoseBackup 復元
        _model.TPoseBackup = loadedModel.TPoseBackup;

        // WorkPlane 復元
        if (loadedWorkPlane != null && _undoController?.WorkPlane != null)
        {
            var wp = _undoController.WorkPlane;
            wp.Mode = loadedWorkPlane.Mode;
            wp.Origin = loadedWorkPlane.Origin;
            wp.AxisU = loadedWorkPlane.AxisU;
            wp.AxisV = loadedWorkPlane.AxisV;
            wp.IsLocked = loadedWorkPlane.IsLocked;
            wp.LockOrientation = loadedWorkPlane.LockOrientation;
            wp.AutoUpdateOriginOnSelection = loadedWorkPlane.AutoUpdateOriginOnSelection;
        }

        // EditorState 復元
        if (loadedEditorState != null)
        {
            ApplyEditorState(loadedEditorState);
        }
        else if (_meshContextList.Count > 0)
        {
            SetSelectedIndex(0);
        }

        // 変更後のカメラ状態を保存
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録用：新メッシュのスナップショット
        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            addedSnapshots.Add((i, snapshot));
        }

        // オフセット配列を初期化
        InitVertexOffsets();

        // UndoContextを更新
        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext != null && _undoController != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = _selectionState.Vertices;
        }

        // Undo記録
        if (_undoController != null)
        {
            var newSelectedIndices = _model.CaptureAllSelectedIndices();
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndices = oldSelectedIndices,
                NewSelectedIndices = newSelectedIndices,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.MeshListStack.Record(record, $"Import CSV Project: {loadedModel.Name}");
            _undoController.FocusMeshList();
        }

        Debug.Log($"[PolyLing] Imported CSV project: {loadedModel.Name} ({_meshContextList.Count} meshes, {_model.Materials?.Count ?? 0} materials)");
        Repaint();
    }

    // ================================================================
    // CSVフォルダ形式: マージ（追加読み込み）
    // ================================================================

    /// <summary>
    /// CSVフォルダからメッシュ/ボーン/モーフを追加読み込み
    /// 名前衝突時は差し替え/名前変更/スキップを選択可能
    /// </summary>
    private void MergeModelCsv()
    {
        string folderPath = EditorUtility.OpenFolderPanel(
            "Add from CSV Folder",
            Application.dataPath,
            ""
        );

        if (string.IsNullOrEmpty(folderPath)) return;

        // フォルダ内の全メッシュエントリを読み込み
        var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("Add from CSV", "読み込み可能なデータが見つかりません。", "OK");
            return;
        }

        // Undo記録用
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();

        var (addedCount, replacedCount, skippedCount, removedSnapshots, addedSnapshots)
            = MergeAdditionalEntries(entries);

        if (addedCount == 0 && replacedCount == 0)
        {
            Debug.Log($"[PolyLing] Merge CSV: nothing added (skipped: {skippedCount})");
            return;
        }

        // オフセット配列を初期化
        InitVertexOffsets();

        // Undo記録
        if (_undoController != null)
        {
            var newSelectedIndices = _model.CaptureAllSelectedIndices();
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndices = oldSelectedIndices,
                NewSelectedIndices = newSelectedIndices
            };
            _undoController.MeshListStack.Record(record, $"Merge CSV: +{addedCount} /{replacedCount} replaced");
            _undoController.FocusMeshList();
        }

        Debug.Log($"[PolyLing] Merge CSV: added={addedCount}, replaced={replacedCount}, skipped={skippedCount}");
        Repaint();
    }

    // ================================================================
    // 共通: 追加エントリのマージ（名前衝突処理付き）
    // ================================================================

    /// <summary>
    /// エントリリストを現在のモデルにマージ（名前衝突ダイアログ付き）
    /// </summary>
    /// <returns>(追加数, 差替数, スキップ数, 削除スナップショット, 追加スナップショット)</returns>
    private (int added, int replaced, int skipped,
             List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots,
             List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots)
        MergeAdditionalEntries(List<CsvMeshEntry> entries)
    {
        var removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        var addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();

        // 現在のメッシュ名一覧
        var existingNames = new Dictionary<string, int>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            existingNames[_meshContextList[i].Name] = i;
        }

        int addedCount = 0;
        int replacedCount = 0;
        int skippedCount = 0;

        foreach (var entry in entries)
        {
            var mc = entry.MeshContext;
            if (mc == null) continue;

            string name = mc.Name;

            if (existingNames.TryGetValue(name, out int existingIndex))
            {
                // 名前衝突 → ダイアログ
                int choice = EditorUtility.DisplayDialogComplex(
                    "名前の重複",
                    $"「{name}」は既に存在します。\nどうしますか？",
                    "差し替え",     // 0
                    "スキップ",     // 1
                    "名前変更して追加" // 2
                );

                switch (choice)
                {
                    case 0: // 差し替え
                        removedSnapshots.Add((existingIndex, MeshContextSnapshot.Capture(_meshContextList[existingIndex])));

                        if (_meshContextList[existingIndex].UnityMesh != null)
                            Object.DestroyImmediate(_meshContextList[existingIndex].UnityMesh);

                        _model.MeshContextList[existingIndex] = mc;
                        addedSnapshots.Add((existingIndex, MeshContextSnapshot.Capture(mc)));
                        replacedCount++;
                        break;

                    case 2: // 名前変更して追加
                        string newName = GenerateUniqueName(name, existingNames);
                        mc.Name = newName;
                        if (mc.MeshObject != null) mc.MeshObject.Name = newName;

                        int newIndex = _model.Add(mc);
                        existingNames[newName] = newIndex;
                        addedSnapshots.Add((newIndex, MeshContextSnapshot.Capture(mc)));
                        addedCount++;
                        break;

                    default: // 1: スキップ
                        skippedCount++;
                        break;
                }
            }
            else
            {
                // 衝突なし → そのまま追加
                int newIndex = _model.Add(mc);
                existingNames[name] = newIndex;
                addedSnapshots.Add((newIndex, MeshContextSnapshot.Capture(mc)));
                addedCount++;
            }
        }

        return (addedCount, replacedCount, skippedCount, removedSnapshots, addedSnapshots);
    }

    /// <summary>
    /// 重複しないユニーク名を生成
    /// </summary>
    private string GenerateUniqueName(string baseName, Dictionary<string, int> existingNames)
    {
        int suffix = 2;
        string candidate = $"{baseName}_{suffix}";
        while (existingNames.ContainsKey(candidate))
        {
            suffix++;
            candidate = $"{baseName}_{suffix}";
        }
        return candidate;
    }

    // ================================================================
    // ヘルパー: EditorStateDTO 作成
    // ================================================================

    /// <summary>
    /// 現在のエディタ状態からEditorStateDTOを作成
    /// </summary>
    private EditorStateDTO CreateEditorStateDTO()
    {
        return new EditorStateDTO
        {
            rotationX = _rotationX,
            rotationY = _rotationY,
            cameraDistance = _cameraDistance,
            cameraTarget = new float[] { _cameraTarget.x, _cameraTarget.y, _cameraTarget.z },
            showWireframe = _showWireframe,
            showVertices = _showVertices,
            vertexEditMode = _vertexEditMode,
            currentToolName = _currentTool?.Name ?? "Select",
            selectedMeshIndex = _model.FirstMeshIndex,
            selectedBoneIndex = _model.FirstBoneIndex,
            selectedVertexMorphIndex = _model.FirstMorphIndex,
            pmxUnityRatio = _undoController?.EditorState?.PmxUnityRatio ?? 0.1f,// 0.085f,
            pmxFlipZ = _undoController?.EditorState?.PmxFlipZ ?? false,
            mqoFlipZ = _undoController?.EditorState?.MqoFlipZ ?? true,
            mqoUnityRatio = _undoController?.EditorState?.MqoUnityRatio ?? 0.01f,
            showBones = _showBones,
            showUnselectedBones = _showUnselectedBones,
            boneDisplayAlongY = _boneDisplayAlongY
        };
    }

    /// <summary>
    /// EditorStateDTOをエディタに適用
    /// </summary>
    private void ApplyEditorState(EditorStateDTO state)
    {
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

        // 座標系設定
        if (_undoController?.EditorState != null)
        {
            _undoController.EditorState.PmxUnityRatio = state.pmxUnityRatio > 0f ? state.pmxUnityRatio : 0.1f;// 0.085f;
            _undoController.EditorState.PmxFlipZ = state.pmxFlipZ;
            _undoController.EditorState.MqoFlipZ = state.mqoFlipZ;
            _undoController.EditorState.MqoUnityRatio = state.mqoUnityRatio > 0f ? state.mqoUnityRatio : 0.01f;
            _undoController.EditorState.ShowBones = state.showBones;
            _undoController.EditorState.ShowUnselectedBones = state.showUnselectedBones;
            _undoController.EditorState.BoneDisplayAlongY = state.boneDisplayAlongY;
        }

        // カテゴリ別選択復元
        _model.ClearAllCategorySelection();

        if (state.selectedMeshIndex >= 0 && state.selectedMeshIndex < _meshContextList.Count)
        {
            SetSelectedIndex(state.selectedMeshIndex);
        }

        if (state.selectedBoneIndex >= 0 && state.selectedBoneIndex < _meshContextList.Count)
        {
            _model.SelectBone(state.selectedBoneIndex);
        }

        if (state.selectedVertexMorphIndex >= 0 && state.selectedVertexMorphIndex < _meshContextList.Count)
        {
            _model.SelectMorph(state.selectedVertexMorphIndex);
        }

        // フォールバック
        if (!_model.HasSelection && _meshContextList.Count > 0)
        {
            SetSelectedIndex(0);
        }

        // ツール復元
        if (!string.IsNullOrEmpty(state.currentToolName))
        {
            SetToolByName(state.currentToolName);
        }
    }
}
