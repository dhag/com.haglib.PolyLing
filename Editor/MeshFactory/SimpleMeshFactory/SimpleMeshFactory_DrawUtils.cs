// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_DrawUtils.cs
// 描画ユーティリティ（他のpartialファイルから参照される共通メソッド）
// これらは SimpleMeshFactory_Preview.cs から分離

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using static MeshFactory.Gizmo.GLGizmoDrawer;

public partial class SimpleMeshFactory
{
    // ================================================================
    // 座標変換
    // ================================================================

    /// <summary>
    /// ワールド座標からスクリーン座標へ変換
    /// </summary>
    private Vector2 WorldToPreviewPos(Vector3 worldPos, Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        // カメラ回転を計算（Z軸ロール対応）
        Vector3 forward = (lookAt - camPos).normalized;
        Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        Quaternion camRot = lookRot * rollRot;

        // View行列を作成
        Matrix4x4 camMatrix = Matrix4x4.TRS(camPos, camRot, Vector3.one);
        Matrix4x4 view = camMatrix.inverse;
        // Unityのカメラは-Z方向を向く
        view.m20 *= -1; view.m21 *= -1; view.m22 *= -1; view.m23 *= -1;

        float aspect = previewRect.width / previewRect.height;
        Matrix4x4 proj = Matrix4x4.Perspective(_preview.cameraFieldOfView, aspect, 0.01f, 100f);

        Vector4 clipPos = proj * view * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);

        if (clipPos.w <= 0)
            return new Vector2(-1000, -1000);

        Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);

        float screenX = previewRect.x + (ndc.x * 0.5f + 0.5f) * previewRect.width;
        float screenY = previewRect.y + (1f - (ndc.y * 0.5f + 0.5f)) * previewRect.height;

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// スクリーン座標からレイを生成
    /// </summary>
    private Ray ScreenPosToRay(Vector2 screenPos)
    {
        if (_toolContext == null)
            return new Ray(Vector3.zero, Vector3.forward);

        Rect previewRect = _toolContext.PreviewRect;
        Vector3 camPos = _toolContext.CameraPosition;
        Vector3 lookAt = _toolContext.CameraTarget;

        // スクリーン座標 → NDC (-1 to 1)
        float ndcX = ((screenPos.x - previewRect.x) / previewRect.width) * 2f - 1f;
        float ndcY = 1f - ((screenPos.y - previewRect.y) / previewRect.height) * 2f;

        // カメラの向きを計算（Z軸ロール対応）
        Vector3 forward = (lookAt - camPos).normalized;
        Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        Quaternion camRot = lookRot * rollRot;

        Vector3 right = camRot * Vector3.right;
        Vector3 up = camRot * Vector3.up;

        // FOVからレイ方向を計算
        float fov = _preview != null ? _preview.cameraFieldOfView : 60f;
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float aspect = previewRect.width / previewRect.height;

        // NDCをカメラ空間の方向に変換
        Vector3 direction = forward
            + right * (ndcX * Mathf.Tan(halfFovRad) * aspect)
            + up * (ndcY * Mathf.Tan(halfFovRad));
        direction.Normalize();

        return new Ray(camPos, direction);
    }

    // ================================================================
    // エッジ管理
    // ================================================================

    /// <summary>
    /// エッジを正規化してHashSetに追加
    /// </summary>
    private void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        edges.Add((a, b));
    }

    // ================================================================
    // 描画ヘルパー
    // ================================================================

    /// <summary>
    /// 点線を描画
    /// </summary>
    private void DrawDottedLine(Vector2 from, Vector2 to, Color color)
    {
        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.color = color;

        Vector2 dir = to - from;
        float length = dir.magnitude;
        dir.Normalize();

        float dashLength = 4f;
        float gapLength = 3f;
        float pos = 0f;

        while (pos < length)
        {
            float dashEnd = Mathf.Min(pos + dashLength, length);
            Vector2 dashStart = from + dir * pos;
            Vector2 dashEndPos = from + dir * dashEnd;
            UnityEditor_Handles.DrawAAPolyLine(2f,
                new Vector3(dashStart.x, dashStart.y, 0),
                new Vector3(dashEndPos.x, dashEndPos.y, 0));
            pos += dashLength + gapLength;
        }

        UnityEditor_Handles.EndGUI();
    }

    /// <summary>
    /// 塗りつぶしポリゴンを描画（凸ポリゴン用、三角形分割）
    /// </summary>
    private void DrawFilledPolygon(Vector2[] points, Color color)
    {
        if (points == null || points.Length < 3)
            return;

        Color oldColor = GUI.color;
        GUI.color = color;

        if (Event.current.type == EventType.Repaint)
        {
            GL.PushMatrix();

            Material mat = GetPolygonMaterial();
            mat.SetPass(0);

            GL.Begin(GL.TRIANGLES);
            GL.Color(color);

            for (int i = 1; i < points.Length - 1; i++)
            {
                GL.Vertex3(points[0].x, points[0].y, 0);
                GL.Vertex3(points[i].x, points[i].y, 0);
                GL.Vertex3(points[i + 1].x, points[i + 1].y, 0);
            }

            GL.End();
            GL.PopMatrix();
        }

        GUI.color = oldColor;
    }

    /// <summary>
    /// 矩形の枠線を描画
    /// </summary>
    private void DrawRectBorder(Rect rect, Color color)
    {
        UnityEditor_Handles.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        UnityEditor_Handles.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        UnityEditor_Handles.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        UnityEditor_Handles.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    // ================================================================
    // マテリアル
    // ================================================================

    private Material _polygonMaterial;

    /// <summary>
    /// ポリゴン描画用マテリアルを取得
    /// </summary>
    private Material GetPolygonMaterial()
    {
        if (_polygonMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }
            _polygonMaterial = new Material(shader);
            _polygonMaterial.hideFlags = HideFlags.HideAndDontSave;
            _polygonMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _polygonMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _polygonMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _polygonMaterial.SetInt("_ZWrite", 0);
        }
        return _polygonMaterial;
    }
}
