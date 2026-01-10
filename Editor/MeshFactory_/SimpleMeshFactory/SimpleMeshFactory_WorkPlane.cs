// Assets/Editor/SimpleMeshFactory.WorkPlaneContext.cs
// WorkPlane関連（UI、イベントハンドラ、ギズモ描画）

using UnityEditor;
using UnityEngine;
using MeshFactory.Tools;
using static MeshFactory.Gizmo.GLGizmoDrawer;

public partial class SimpleMeshFactory
{
    // ================================================================
    // プレビュー用マテリアル
    // ================================================================
    private Material _previewMaterial;

    private Material GetPreviewMaterial()
    {
        if (_previewMaterial != null)
            return _previewMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _previewMaterial = new Material(shader);
            _previewMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
        }

        return _previewMaterial;
    }

    // ================================================================
    // WorkPlane関連
    // ================================================================

    /// <summary>
    /// WorkPlaneContext UIイベントハンドラ設定
    /// </summary>
    private void SetupWorkPlaneEventHandlers()
    {
        // "From Selection"ボタンクリック時
        WorkPlaneUI.OnFromSelectionClicked += OnWorkPlaneFromSelectionClicked;

        // WorkPlane変更時（Undo記録）
        WorkPlaneUI.OnChanged += OnWorkPlaneChanged;
    }

    /// <summary>
    /// WorkPlaneContext UIイベントハンドラ解除
    /// </summary>
    private void CleanupWorkPlaneEventHandlers()
    {
        WorkPlaneUI.OnFromSelectionClicked -= OnWorkPlaneFromSelectionClicked;
        WorkPlaneUI.OnChanged -= OnWorkPlaneChanged;
    }

    //移動予定------------------------
    /// <summary>
    /// BoneTransform UIイベントハンドラ設定
    /// </summary>
    private void SetupBoneTransformEventHandlers()
    {
        BoneTransformUI.OnFromSelectionClicked += OnBoneTransformFromSelectionClicked;
        BoneTransformUI.OnResetClicked += OnBoneTransformResetClicked;
    }

    /// <summary>
    /// BoneTransform UIイベントハンドラ解除
    /// </summary>
    private void CleanupBoneTransformEventHandlers()
    {
        BoneTransformUI.OnFromSelectionClicked -= OnBoneTransformFromSelectionClicked;
        BoneTransformUI.OnResetClicked -= OnBoneTransformResetClicked;
    }

    /// <summary>
    /// BoneTransform "From Selection"ボタンクリック
    /// </summary>
    private void OnBoneTransformFromSelectionClicked()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.BoneTransform == null) return;

        // 選択がなければ何もしない
        if (UnityEditor.Selection.activeTransform == null) return;

        BoneTransformSnapshot before = meshContext.BoneTransform.CreateSnapshot();

        meshContext.BoneTransform.CopyFromSelection();
        meshContext.BoneTransform.UseLocalTransform = true;

        BoneTransformSnapshot after = meshContext.BoneTransform.CreateSnapshot();

        // Undo記録
        BoneTransformUI.NotifyChanged(before, after, "Copy Transform From Selection");

        Repaint();
    }

    /// <summary>
    /// BoneTransform リセットボタンクリック
    /// </summary>
    private void OnBoneTransformResetClicked()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.BoneTransform == null) return;

        var before = meshContext.BoneTransform.CreateSnapshot();

        meshContext.BoneTransform.Reset();

        var after = meshContext.BoneTransform.CreateSnapshot();

        // Undo記録
        BoneTransformUI.NotifyChanged(before, after, "Reset Export Settings");

        Repaint();
    }
    //移動予定ここまで

    /// <summary>
    /// WorkPlaneContext "From Selection"ボタンクリック
    /// </summary>
    private void OnWorkPlaneFromSelectionClicked()
    {
        if (_undoController == null) return;

        var workPlane = _undoController.WorkPlane;
        if (workPlane == null || workPlane.IsLocked) return;

        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null || _selectedVertices.Count == 0) return;

        var before = workPlane.CreateSnapshot();

        if (workPlane.UpdateOriginFromSelection(meshContext.MeshObject, _selectedVertices))
        {
            var after = workPlane.CreateSnapshot();
            if (before.IsDifferentFrom(after))
            {
                _undoController.RecordWorkPlaneChange(before, after, "Set WorkPlaneContext Origin from Selection");
            }
            Repaint();
        }
    }

    /// <summary>
    /// WorkPlane変更時のコールバック（Undo記録）
    /// </summary>
    private void OnWorkPlaneChanged(WorkPlaneSnapshot before, WorkPlaneSnapshot after, string description)
    {
        if (_undoController == null) return;

        _undoController.RecordWorkPlaneChange(before, after, description);
        Repaint();
    }

    /// <summary>
    /// 選択変更時にWorkPlane原点を更新
    /// </summary>
    /// <summary>
    /// WorkPlaneContext UI描画
    /// </summary>
    private void DrawWorkPlaneUI()
    {
        if (_undoController?.WorkPlane == null) return;

        WorkPlaneUI.DrawUI(_undoController.WorkPlane);
    }

    /// <summary>
    /// WorkPlaneギズモ描画（プレビュー内）
    /// </summary>
    private void DrawWorkPlaneGizmo(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        if (_undoController?.WorkPlane == null) return;

        var workPlane = _undoController.WorkPlane;

        // CameraParallelモードの場合、カメラ情報を更新（ロックされていない場合のみ）
        // 注：Undo記録はEndCameraDrag()で行われる
        if (workPlane.Mode == WorkPlaneMode.CameraParallel &&
            !workPlane.IsLocked && !workPlane.LockOrientation)
        {
            workPlane.UpdateFromCamera(camPos, lookAt);
        }

        Vector3 origin = workPlane.Origin;
        Vector3 axisU = workPlane.AxisU;
        Vector3 axisV = workPlane.AxisV;
        Vector3 normal = workPlane.Normal;

        // グリッドサイズ（バウンディングボックスに基づく）
        float gridSize = 0.5f;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject != null)
        {
            var bounds = meshContext.MeshObject.CalculateBounds();
            gridSize = Mathf.Max(bounds.size.magnitude * 0.3f, 0.3f);
        }

        UnityEditor_Handles.BeginGUI();

        // グリッド線の色
        Color gridColor;
        if (workPlane.IsLocked)
        {
            gridColor = new Color(1f, 0.5f, 0.2f, 0.15f);  // 全ロック時はオレンジ
        }
        else if (workPlane.LockOrientation)
        {
            gridColor = new Color(1f, 0.8f, 0.4f, 0.15f);  // 軸ロック時は薄いオレンジ
        }
        else
        {
            gridColor = new Color(0.5f, 0.8f, 1f, 0.15f);  // 通常は水色
        }
        UnityEditor_Handles.color = gridColor;

        int gridLines = 5;
        float halfSize = gridSize * 0.5f;

        for (int i = -gridLines; i <= gridLines; i++)
        {
            float t = i / (float)gridLines;

            // U方向の線
            Vector3 startU = origin + axisV * (t * gridSize) - axisU * halfSize;
            Vector3 endU = origin + axisV * (t * gridSize) + axisU * halfSize;
            Vector2 startUScreen = WorldToPreviewPos(startU, previewRect, camPos, lookAt);
            Vector2 endUScreen = WorldToPreviewPos(endU, previewRect, camPos, lookAt);
            if (previewRect.Contains(startUScreen) || previewRect.Contains(endUScreen))
            {
                UnityEditor_Handles.DrawAAPolyLine(1f,
                    new Vector3(startUScreen.x, startUScreen.y, 0),
                    new Vector3(endUScreen.x, endUScreen.y, 0));
            }

            // V方向の線
            Vector3 startV = origin + axisU * (t * gridSize) - axisV * halfSize;
            Vector3 endV = origin + axisU * (t * gridSize) + axisV * halfSize;
            Vector2 startVScreen = WorldToPreviewPos(startV, previewRect, camPos, lookAt);
            Vector2 endVScreen = WorldToPreviewPos(endV, previewRect, camPos, lookAt);
            if (previewRect.Contains(startVScreen) || previewRect.Contains(endVScreen))
            {
                UnityEditor_Handles.DrawAAPolyLine(1f,
                    new Vector3(startVScreen.x, startVScreen.y, 0),
                    new Vector3(endVScreen.x, endVScreen.y, 0));
            }
        }

        // 軸（U: 赤、V: 緑、Normal: 青）
        float axisLen = gridSize * 0.25f;

        // U軸（赤）
        Vector2 originScreen = WorldToPreviewPos(origin, previewRect, camPos, lookAt);
        Vector2 uEndScreen = WorldToPreviewPos(origin + axisU * axisLen, previewRect, camPos, lookAt);
        UnityEditor_Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        UnityEditor_Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(uEndScreen.x, uEndScreen.y, 0));

        // V軸（緑）
        Vector2 vEndScreen = WorldToPreviewPos(origin + axisV * axisLen, previewRect, camPos, lookAt);
        UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
        UnityEditor_Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(vEndScreen.x, vEndScreen.y, 0));

        // 法線（青）
        Vector2 nEndScreen = WorldToPreviewPos(origin + normal * axisLen * 0.5f, previewRect, camPos, lookAt);
        UnityEditor_Handles.color = new Color(0.3f, 0.5f, 1f, 0.6f);
        UnityEditor_Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(nEndScreen.x, nEndScreen.y, 0));

        // 原点マーカー
        if (previewRect.Contains(originScreen))
        {
            Color markerColor = workPlane.IsLocked
                ? new Color(1f, 0.6f, 0.2f, 0.9f)
                : new Color(0.5f, 0.9f, 1f, 0.9f);
            float markerSize = 6f;
            UnityEditor_Handles.DrawRect(new Rect(
                originScreen.x - markerSize / 2,
                originScreen.y - markerSize / 2,
                markerSize,
                markerSize), markerColor);
        }

        UnityEditor_Handles.EndGUI();
    }
}
