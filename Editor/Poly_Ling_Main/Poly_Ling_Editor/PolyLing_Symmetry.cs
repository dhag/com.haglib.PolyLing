// Assets/Editor/SimpleMeshFactory_Symmetry.cs
// 対称モード関連（UI、対称平面描画）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

public partial class PolyLing
{
    // ================================================================
    // フィールド
    // ================================================================

    // 後方互換プロパティ（ModelContextに移行）
    private SymmetrySettings _symmetrySettings => _model?.SymmetrySettings;

    // UI状態
    private bool _foldSymmetry = true;

    // ================================================================
    // 対称設定プロパティ
    // ================================================================

    /// <summary>
    /// 対称設定へのアクセス
    /// </summary>
    public SymmetrySettings SymmetrySettings => _model.SymmetrySettings;

    // ================================================================
    // 初期化・クリーンアップ（後方互換: 呼び出し元が存在するため空実装を残す）
    // ================================================================

    private void InitializeSymmetryCache() { }
    public void InvalidateSymmetryCache() { }
    public void InvalidateAllSymmetryCaches() { }

    // ================================================================
    // UI描画
    // ================================================================

    /// <summary>
    /// 対称モードUIを描画（Displayセクション内で呼び出し）
    /// </summary>
    private void DrawSymmetryUI()
    {
        _foldSymmetry = DrawFoldoutWithUndo("Symmetry", L.Get("Symmetry"), true);
        if (!_foldSymmetry) return;
        if (_symmetrySettings == null) return;

        EditorGUI.indentLevel++;

        // ミラー有効は常にtrue（UIから削除）
        if (!_symmetrySettings.IsEnabled)
        {
            _symmetrySettings.IsEnabled = true;
            ApplySymmetryToUnifiedSystem();
        }

        // 詳細設定（常に表示）
        EditorGUILayout.Space(2);

        // 軸選択
        EditorGUI.BeginChangeCheck();
        var newAxis = (SymmetryAxis)EditorGUILayout.EnumPopup(L.Get("Axis"), _symmetrySettings.Axis);
        if (EditorGUI.EndChangeCheck())
        {
            _symmetrySettings.Axis = newAxis;
            ApplySymmetryToUnifiedSystem();
            Repaint();
        }

        // 平面オフセット
        EditorGUI.BeginChangeCheck();
        float newOffset = EditorGUILayout.Slider(L.Get("PlaneOffset"), _symmetrySettings.PlaneOffset, -1f, 1f);//スライダーの上限下限
        if (EditorGUI.EndChangeCheck())
        {
            _symmetrySettings.PlaneOffset = newOffset;
            ApplySymmetryToUnifiedSystem();
            Repaint();
        }

        // オフセットリセットボタン
        if (Mathf.Abs(_symmetrySettings.PlaneOffset) > 0.001f)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.Get("ResetOffset"), EditorStyles.miniButton, GUILayout.Width(80)))
            {
                _symmetrySettings.PlaneOffset = 0f;
                ApplySymmetryToUnifiedSystem();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(3);

        // 表示オプション
        EditorGUILayout.LabelField(L.Get("DisplayOptions"), EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        bool showPlane = EditorGUILayout.Toggle(L.Get("SymmetryPlane"), _symmetrySettings.ShowSymmetryPlane);

        if (EditorGUI.EndChangeCheck())
        {
            _symmetrySettings.ShowSymmetryPlane = showPlane;
            Repaint();
        }

        EditorGUI.indentLevel--;
    }

    // ================================================================
    // 対称平面描画
    // ================================================================

    /// <summary>
    /// 対称平面表示対象かどうか判定
    /// </summary>
    private bool ShouldDrawSymmetryPlane()
    {
        // グローバル「ミラー有効」がOFFなら表示しない
        if (_symmetrySettings == null || !_symmetrySettings.IsEnabled)
            return false;

        // 「対称平面」がOFFなら表示しない
        if (!_symmetrySettings.ShowSymmetryPlane)
            return false;

        return true;
    }

    /// <summary>
    /// 対称平面を描画
    /// </summary>
    private void DrawSymmetryPlane(Rect previewRect, Vector3 camPos, Vector3 lookAt, Bounds meshBounds)
    {
        // グローバル設定でフィルター
        if (!ShouldDrawSymmetryPlane())
            return;

        Vector3 normal = _symmetrySettings.GetPlaneNormal();
        Vector3 planePoint = _symmetrySettings.GetPlanePoint();
        Color planeColor = _symmetrySettings.GetAxisColor();

        // 平面のサイズをメッシュバウンドに合わせる
        float planeSize = Mathf.Max(meshBounds.size.magnitude, 0.5f) * 0.6f;

        // 平面上の2つの軸を計算
        Vector3 axis1, axis2;
        switch (_symmetrySettings.Axis)
        {
            case SymmetryAxis.X:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Y:
                axis1 = Vector3.right;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Z:
                axis1 = Vector3.right;
                axis2 = Vector3.up;
                break;
            default:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
        }

        UnityEditor_Handles.BeginGUI();

        // 平面の四隅
        Vector3[] corners = new Vector3[4];
        corners[0] = planePoint + (-axis1 - axis2) * planeSize * 0.5f;
        corners[1] = planePoint + (axis1 - axis2) * planeSize * 0.5f;
        corners[2] = planePoint + (axis1 + axis2) * planeSize * 0.5f;
        corners[3] = planePoint + (-axis1 + axis2) * planeSize * 0.5f;

        Vector2[] screenCorners = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            screenCorners[i] = WorldToPreviewPos(corners[i], previewRect, camPos, lookAt);
        }

        // 半透明の平面を描画
        Color fillColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.15f);
        DrawFilledPolygon(screenCorners, fillColor);

        // 枠線を描画
        UnityEditor_Handles.color = new Color(planeColor.r, planeColor.g, planeColor.b, 0.6f);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            UnityEditor_Handles.DrawAAPolyLine(2f,
                new Vector3(screenCorners[i].x, screenCorners[i].y, 0),
                new Vector3(screenCorners[next].x, screenCorners[next].y, 0));
        }

        // 中心線（対称軸）を点線で描画
        Vector2 center = WorldToPreviewPos(planePoint, previewRect, camPos, lookAt);
        Vector2 axis1End = WorldToPreviewPos(planePoint + axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis1Start = WorldToPreviewPos(planePoint - axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2End = WorldToPreviewPos(planePoint + axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2Start = WorldToPreviewPos(planePoint - axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);

        Color lineColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.5f);
        DrawDottedLine(axis1Start, axis1End, lineColor);
        DrawDottedLine(axis2Start, axis2End, lineColor);

        // 中心マーカー
        if (previewRect.Contains(center))
        {
            float markerSize = 6f;
            UnityEditor_Handles.DrawRect(new Rect(
                center.x - markerSize / 2,
                center.y - markerSize / 2,
                markerSize,
                markerSize), planeColor);
        }

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // クリーンアップ
    // ================================================================

    /// <summary>
    /// ミラー関連リソースをクリーンアップ（後方互換: 呼び出し元が存在するため残す）
    /// </summary>
    private void CleanupMirrorResources()
    {
    }
}
