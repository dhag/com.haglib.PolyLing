// Assets/Editor/PolyLing.Model.cs
// モデル管理機能（Phase 2-3）
// - モデル選択UI
// - モデル追加/削除/切り替え
// - Undo対応（Phase 3）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Localization;

public partial class PolyLing
{
    // ================================================================
    // モデル管理UI
    // ================================================================

    /// <summary>
    /// モデル選択UIを描画
    /// DrawMeshListの先頭で呼び出す
    /// </summary>
    private void DrawModelSelector()
    {
        if (_project == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ヘッダー行
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(L.Get("Model"), EditorStyles.boldLabel, GUILayout.Width(50));

        // モデル選択ドロップダウン
        if (_project.ModelCount > 0)
        {
            string[] modelNames = _project.Models.Select(m => m.Name ?? "Untitled").ToArray();
            int currentIndex = _project.CurrentModelIndex;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(currentIndex, modelNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                SwitchModelWithUndo(newIndex);
            }
        }
        else
        {
            EditorGUILayout.LabelField("(No Model)", EditorStyles.miniLabel);
        }

        // 追加ボタン
        if (GUILayout.Button("+", GUILayout.Width(24)))
        {
            AddNewModelWithUndo();
        }

        // 削除ボタン（モデルが2つ以上ある場合のみ有効）
        using (new EditorGUI.DisabledScope(_project.ModelCount <= 1))
        {
            if (GUILayout.Button("-", GUILayout.Width(24)))
            {
                if (EditorUtility.DisplayDialog(
                    L.Get("DeleteModel"),
                    string.Format(L.Get("DeleteModelConfirm"), _model?.Name ?? ""),
                    L.Get("Delete"),
                    L.Get("Cancel")))
                {
                    RemoveCurrentModelWithUndo();
                }
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);
    }

    // ================================================================
    // モデル操作（Core委譲）
    // ================================================================
    // ロジック（Undo記録・スナップショット・選択保存）はPolyLingCoreが担う。
    // Editor側はViewportのリフレッシュをOnCurrentModelChangedイベント経由で受け取る。

    private void SwitchModelWithUndo(int newIndex)
    {
        _core?.DispatchPanelCommand(new SwitchModelCommand(newIndex));
    }

    private void AddNewModelWithUndo()
    {
        string newName = _project?.GenerateUniqueModelName("Model") ?? "Model";
        _core?.CommandQueue?.Enqueue(
            new Poly_Ling.Commands.AddModelCommand(newName, n => _core?.CreateNewModel(n)));
    }

    private void RemoveCurrentModelWithUndo()
    {
        if (_project == null || _project.ModelCount <= 1) return;
        _core?.DispatchPanelCommand(new DeleteModelCommand(_project.CurrentModelIndex));
    }

    private void RenameModelWithUndo(string newName)
    {
        if (_model == null || string.IsNullOrEmpty(newName)) return;
        if (_model.Name == newName) return;
        _core?.DispatchPanelCommand(new RenameModelCommand(_project.CurrentModelIndex, newName));
    }

    // ================================================================
    // Phase 3: Undo記録メソッド
    // ================================================================

    // ================================================================
    // カメラ状態ヘルパー
    // ================================================================

    private CameraSnapshot CaptureCameraSnapshot()
    {
        return new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };
    }

    /// <summary>
    /// カメラ状態を復元
    /// </summary>
    private void RestoreCameraSnapshot(CameraSnapshot snapshot)
    {
        _rotationX = snapshot.RotationX;
        _rotationY = snapshot.RotationY;
        _cameraDistance = snapshot.CameraDistance;
        _cameraTarget = snapshot.CameraTarget;
    }

    // ================================================================
    // ProjectContextコールバック
    // ================================================================

    /// <summary>
    /// カレントモデル変更時のコールバック
    /// </summary>
    private void OnCurrentModelChanged(int newIndex)
    {
        // Core側でUndoController更新・コールバック再登録・PanelContext通知は完了済み。
        // Editor側はビューポートの再構築とRepaintのみ担当する。

        var currentModel = _model;
        if (currentModel == null) return;

        // バッファを再構築
        _unifiedAdapter?.SetModelContext(currentModel);
        _viewportCore?.SetModel(currentModel);

        // トポロジ更新（GPU通知）
        UpdateTopology();

        Repaint();
    }

    /// <summary>
    /// モデルリスト変更時のコールバック
    /// </summary>
    private void OnModelsChanged()
    {
        //Debug.Log($"[OnModelsChanged] Models count: {_project?.ModelCount ?? 0}");
        _core?.NotifyPanels(ChangeKind.ListStructure);
        Repaint();
    }

    /// <summary>
    /// プロジェクトUndo/Redo実行時の復元処理
    /// </summary>
    /// <param name="record">実行されたProjectRecord</param>
    /// <param name="isRedo">Redoの場合true、Undoの場合false</param>
    private void OnProjectUndoRedoPerformed(ProjectRecord record, bool isRedo)
    {
        if (record == null || _project == null) return;

        // スナップショット復元はCore側のOnUndoRedoPerformed_Extで処理済み。
        // Editor側はビューポートの再構築とRepaintのみ担当する。
        _unifiedAdapter?.SetModelContext(_project.CurrentModel);
        _viewportCore?.SetModel(_project.CurrentModel);

        UpdateTopology();
        Repaint();
    }

    /// <summary>
    /// カメラ復元要求（Undo/Redo時）
    /// </summary>
    private void OnCameraRestoreRequested(CameraSnapshot snapshot)
    {
        RestoreCameraSnapshot(snapshot);
        Repaint();
    }

    // ================================================================
    // コールバック設定/解除
    // ================================================================

    /// <summary>
    /// ProjectContextのコールバックを設定
    /// </summary>
    private void SetupProjectContextCallbacks()
    {
        // ProjectContext コールバックは OnEnable 時に Core 経由で設定される。
        // Editor 固有のコールバック（カメラ復元）のみここで設定する。
        if (_project == null) return;
        _project.OnCameraRestoreRequested += OnCameraRestoreRequested;
    }

    private void ClearProjectContextCallbacks()
    {
        if (_project == null) return;
        _project.OnCameraRestoreRequested -= OnCameraRestoreRequested;
    }
}
