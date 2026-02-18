// Assets/Editor/Poly_Ling/PolyLing/PolyLing_BoneDraw.cs
// ボーン形状のワイヤフレーム描画
// MeshType.Boneの各MeshContextに対し、WorldMatrixの位置・回転でボーン形状を描画
// 形状: 八面体ベースの矢印型（メタセコスケール1/100）
// 長さ固定、向きはボーンのWorldMatrix回転

using UnityEngine;
using Poly_Ling.Data;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

public partial class PolyLing
{
    // ================================================================
    // ボーン形状定義（Unityスケール = メタセコ1/100）
    // ================================================================

    private static readonly Vector3[] BoneShapeVertices = new Vector3[]
    {
        new Vector3(0.5f, 0f, -0.4f),   // 0: 中間-Z
        new Vector3(2.5f, 0f, 0f),       // 1: 先端
        new Vector3(0.5f, 0.2f, 0f),     // 2: 中間+Y
        new Vector3(0.5f, 0f, 0.4f),     // 3: 中間+Z
        new Vector3(0.5f, -0.2f, 0f),    // 4: 中間-Y
        new Vector3(0f, 0f, 0f),          // 5: 根元
    };

    // メタセコスケール→Unityスケール: 1/100 (デバッグ中4倍)
    private const float BoneShapeScale = 0.04f;

    // デバッグ軸線の長さ
    private const float BoneDebugAxisLength = 0.05f;

    // エッジ定義（12本）
    private static readonly int[,] BoneShapeEdges = new int[,]
    {
        {0, 1}, {0, 2}, {0, 4}, {0, 5},
        {1, 2}, {1, 3}, {1, 4},
        {2, 3}, {2, 5},
        {3, 4}, {3, 5},
        {4, 5},
    };

    // ボーン色
    private static readonly Color BoneWireColor = new Color(0.2f, 0.8f, 1.0f, 0.8f);
    private static readonly Color BoneWireSelectedColor = new Color(1.0f, 0.6f, 0.1f, 0.9f);

    // ================================================================
    // 描画メソッド
    // ================================================================

    /// <summary>
    /// 全ボーンのワイヤフレームを描画（2Dオーバーレイ）
    /// </summary>
    private void DrawBones(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        if (_meshContextList == null || _meshContextList.Count == 0)
            return;

        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var ctx = _meshContextList[i];
            if (ctx == null || ctx.Type != MeshType.Bone)
                continue;

            bool isSelected = _model?.SelectedBoneIndices.Contains(i) ?? false;

            // 非選択ボーンをスキップ（ShowUnselectedBones=false の場合）
            if (!isSelected && !_showUnselectedBones)
                continue;

            Color wireColor = isSelected ? BoneWireSelectedColor : BoneWireColor;

            if (!ExtractRotation(ctx.WorldMatrix, out Vector3 bonePos, out Quaternion boneRot))
                continue;

            DrawSingleBone(previewRect, camPos, lookAt, bonePos, boneRot, wireColor);

            // デバッグ: WorldMatrixのローカル軸を線で描画
            DrawBoneDebugAxes(previewRect, camPos, lookAt, bonePos, boneRot);
        }
    }

    /// <summary>
    /// WorldMatrixから回転のみ抽出（スケール除去）
    /// </summary>
    private static bool ExtractRotation(Matrix4x4 worldMatrix, out Vector3 bonePos, out Quaternion boneRot)
    {
        bonePos = new Vector3(worldMatrix.m03, worldMatrix.m13, worldMatrix.m23);

        Vector3 col0 = new Vector3(worldMatrix.m00, worldMatrix.m10, worldMatrix.m20);
        Vector3 col1 = new Vector3(worldMatrix.m01, worldMatrix.m11, worldMatrix.m21);
        Vector3 col2 = new Vector3(worldMatrix.m02, worldMatrix.m12, worldMatrix.m22);
        float sx = col0.magnitude;
        float sy = col1.magnitude;
        float sz = col2.magnitude;
        if (sx < 0.0001f || sy < 0.0001f || sz < 0.0001f)
        {
            boneRot = Quaternion.identity;
            return false;
        }

        Matrix4x4 rotOnly = Matrix4x4.identity;
        rotOnly.SetColumn(0, col0 / sx);
        rotOnly.SetColumn(1, col1 / sy);
        rotOnly.SetColumn(2, col2 / sz);
        boneRot = rotOnly.rotation;
        return true;
    }

    /// <summary>
    /// 単一ボーン形状をワイヤフレーム描画
    /// </summary>
    private void DrawSingleBone(Rect previewRect, Vector3 camPos, Vector3 lookAt,
                                 Vector3 bonePos, Quaternion boneRot, Color color)
    {
        var screenVerts = new Vector2[BoneShapeVertices.Length];
        for (int i = 0; i < BoneShapeVertices.Length; i++)
        {
            Vector3 localPos = BoneShapeVertices[i] * BoneShapeScale;
            Vector3 worldPos = bonePos + boneRot * localPos;
            screenVerts[i] = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);
        }

        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.color = color;

        int edgeCount = BoneShapeEdges.GetLength(0);
        for (int i = 0; i < edgeCount; i++)
        {
            int a = BoneShapeEdges[i, 0];
            int b = BoneShapeEdges[i, 1];
            UnityEditor_Handles.DrawAAPolyLine(1.5f, screenVerts[a], screenVerts[b]);
        }

        UnityEditor_Handles.EndGUI();
    }

    /// <summary>
    /// デバッグ: XYZ軸を色付き線で描画
    /// X=赤, Y=緑, Z=青
    /// </summary>
    private void DrawBoneDebugAxes(Rect previewRect, Vector3 camPos, Vector3 lookAt,
                                    Vector3 pos, Quaternion rotation)
    {
        Vector3 axisX = rotation * Vector3.right;
        Vector3 axisY = rotation * Vector3.up;
        Vector3 axisZ = rotation * Vector3.forward;

        Vector2 screenOrigin = WorldToPreviewPos(pos, previewRect, camPos, lookAt);
        Vector2 screenX = WorldToPreviewPos(pos + axisX * BoneDebugAxisLength, previewRect, camPos, lookAt);
        Vector2 screenY = WorldToPreviewPos(pos + axisY * BoneDebugAxisLength, previewRect, camPos, lookAt);
        Vector2 screenZ = WorldToPreviewPos(pos + axisZ * BoneDebugAxisLength, previewRect, camPos, lookAt);

        UnityEditor_Handles.BeginGUI();

        UnityEditor_Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        UnityEditor_Handles.DrawAAPolyLine(2f, screenOrigin, screenX);

        UnityEditor_Handles.color = new Color(0.2f, 1f, 0.2f, 0.9f);
        UnityEditor_Handles.DrawAAPolyLine(2f, screenOrigin, screenY);

        UnityEditor_Handles.color = new Color(0.2f, 0.2f, 1f, 0.9f);
        UnityEditor_Handles.DrawAAPolyLine(2f, screenOrigin, screenZ);

        UnityEditor_Handles.EndGUI();
    }
}
