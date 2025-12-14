// Assets/Editor/SimpleMeshFactory.Preview.cs
// プレビュー描画（ワイヤーフレーム、選択オーバーレイ、頂点ハンドル）
// Phase3: マルチマテリアル対応版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;

public partial class SimpleMeshFactory
{
    // ================================================================
    // 中央ペイン：プレビュー
    // ================================================================
    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(
            200, 10000,
            200, 10000,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null || _preview == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.LabelField(rect, "Select a mesh", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var mesh = meshContext.UnityMesh;

        float dist = _cameraDistance;
        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
        Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -dist);

        HandleInput(rect, meshContext, camPos, _cameraTarget, dist);

        if (Event.current.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        _preview.camera.transform.position = camPos;
        // LookAt後、視線軸（forward）周りにロール回転
        Quaternion lookRot = Quaternion.LookRotation(_cameraTarget - camPos, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        _preview.camera.transform.rotation = lookRot * rollRot;

        // マルチマテリアル対応描画
        DrawMeshWithMaterials(meshContext, mesh);


        // ★ミラーメッシュ描画を追加
        if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
        {
            DrawMirroredMesh(meshContext, mesh);
        }

        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        if (_showWireframe)
        {
            // ワイヤーフレーム描画
            DrawWireframeOverlay(rect, meshContext.Data, camPos, _cameraTarget);
            // ★ミラーワイヤーフレーム描画
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                DrawMirroredWireframe(rect, meshContext.Data, camPos, _cameraTarget);
            }

        }
        // 選択状態のオーバーレイ描画（追加）
        DrawSelectionOverlay(rect, meshContext.Data, camPos, _cameraTarget);


        DrawVertexHandles(rect, meshContext.Data, camPos, _cameraTarget);

        // ローカル原点マーカー（点線、ドラッグ不可）
        DrawOriginMarker(rect, camPos, _cameraTarget);

        // ツールのギズモ描画
        UpdateToolContext(meshContext, rect, camPos, dist);
        _currentTool?.DrawGizmo(_toolContext);

        // WorkPlaneギズモ描画（AddFaceTool時のみ）
        if (_showWorkPlaneGizmo && _vertexEditMode && _currentTool == _addFaceTool)
        {
            DrawWorkPlaneGizmo(rect, camPos, _cameraTarget);
        }
        // ★対称平面ギズモ描画
        if (_symmetrySettings != null && _symmetrySettings.IsEnabled && _symmetrySettings.ShowSymmetryPlane)
        {
            Bounds meshBounds = meshContext.Data != null ? meshContext.Data.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            DrawSymmetryPlane(rect, camPos, _cameraTarget, meshBounds);
        }
    }

    /// <summary>
    /// マルチマテリアル対応でメッシュを描画
    /// </summary>
    private void DrawMeshWithMaterials(MeshContext meshContext, Mesh mesh)
    {
        if (mesh == null)
            return;

        int subMeshCount = mesh.subMeshCount;
        Material defaultMat = GetPreviewMaterial();

        for (int i = 0; i < subMeshCount; i++)
        {
            // メッシュコンテキストのマテリアルリストから取得、なければデフォルト
            Material mat = null;
            if (meshContext != null && i < meshContext.Materials.Count)
            {
                mat = meshContext.Materials[i];
            }

            // nullの場合はデフォルトマテリアル
            if (mat == null)
            {
                mat = defaultMat;
            }
            _preview.DrawMesh(mesh, Matrix4x4.identity, mat, i);
        }
    }

    /// <summary>
    /// ローカル原点マーカーを点線で描画（ドラッグ不可）
    /// </summary>
    private void DrawOriginMarker(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        Vector3 origin = Vector3.zero;
        Vector2 originScreen = WorldToPreviewPos(origin, previewRect, camPos, lookAt);

        if (!previewRect.Contains(originScreen))
            return;

        float axisLength = 0.2f;

        // X軸（赤）点線
        Vector3 xEnd = origin + Vector3.right * axisLength;
        Vector2 xScreen = WorldToPreviewPos(xEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, xScreen, new Color(1f, 0.3f, 0.3f, 0.7f));

        // Y軸（緑）点線
        Vector3 yEnd = origin + Vector3.up * axisLength;
        Vector2 yScreen = WorldToPreviewPos(yEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, yScreen, new Color(0.3f, 1f, 0.3f, 0.7f));

        // Z軸（青）点線
        Vector3 zEnd = origin + Vector3.forward * axisLength;
        Vector2 zScreen = WorldToPreviewPos(zEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, zScreen, new Color(0.3f, 0.3f, 1f, 0.7f));

        // 中心点（小さめ）
        float centerSize = 4f;
        EditorGUI.DrawRect(new Rect(
            originScreen.x - centerSize / 2,
            originScreen.y - centerSize / 2,
            centerSize,
            centerSize), new Color(1f, 1f, 1f, 0.7f));
    }

    /// <summary>
    /// 点線を描画
    /// </summary>
    private void DrawDottedLine(Vector2 from, Vector2 to, Color color)
    {
        Handles.BeginGUI();
        Handles.color = color;

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
            Handles.DrawAAPolyLine(2f,
                new Vector3(dashStart.x, dashStart.y, 0),
                new Vector3(dashEndPos.x, dashEndPos.y, 0));
            pos += dashLength + gapLength;
        }

        Handles.EndGUI();
    }


    /// <summary>
    /// ワイヤーフレーム描画（MeshDataベース）
    /// 3頂点以上の面のエッジと、2頂点の補助線（Line）を描画
    /// </summary>
    private void DrawWireframeOverlay(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (meshData == null)
            return;

        var edges = new HashSet<(int, int)>();
        var lines = new List<(int, int)>();  // 2頂点の補助線

        // 各面からエッジを抽出
        foreach (var face in meshData.Faces)
        {
            if (face.VertexCount == 2)
            {
                // 2頂点 = 補助線（Line）
                lines.Add((face.VertexIndices[0], face.VertexIndices[1]));
            }
            else if (face.VertexCount >= 3)
            {
                // 3頂点以上 = 通常の面
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % face.VertexCount];
                    AddEdge(edges, a, b);
                }
            }
        }

        Handles.BeginGUI();

        // 通常のエッジを描画（緑）
        Handles.color = new Color(0f, 1f, 0.5f, 0.9f);
        foreach (var edge in edges)
        {
            Vector3 p1World = meshData.Vertices[edge.Item1].Position;
            Vector3 p2World = meshData.Vertices[edge.Item2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        // 補助線を描画（マゼンタ、点線風に太め）
        Handles.color = new Color(1f, 0.3f, 1f, 0.9f);
        foreach (var line in lines)
        {
            if (line.Item1 < 0 || line.Item1 >= meshData.VertexCount ||
                line.Item2 < 0 || line.Item2 >= meshData.VertexCount)
                continue;

            Vector3 p1World = meshData.Vertices[line.Item1].Position;
            Vector3 p2World = meshData.Vertices[line.Item2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawAAPolyLine(2f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        Handles.EndGUI();
    }
    /// <summary>
    /// 選択状態のオーバーレイを描画（Edge/Face/Line）
    /// </summary>
    private void DrawSelectionOverlay(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (meshData == null || _selectionState == null)
            return;

        try
        {
            Handles.BeginGUI();

            // 選択された面を描画（半透明ポリゴン）
            DrawSelectedFaces(previewRect, meshData, camPos, lookAt);

            // 選択されたエッジを描画（太線）
            DrawSelectedEdges(previewRect, meshData, camPos, lookAt);

            // 選択された補助線を描画（太線）
            DrawSelectedLines(previewRect, meshData, camPos, lookAt);
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    /// <summary>
    /// 選択された面を描画
    /// </summary>
    private void DrawSelectedFaces(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Faces.Count == 0)
            return;

        // 選択面のハイライト色（半透明オレンジ）
        Color faceColor = new Color(1f, 0.6f, 0.2f, 0.3f);
        Color edgeColor = new Color(1f, 0.5f, 0f, 0.9f);

        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx < 0 || faceIdx >= meshData.FaceCount)
                continue;

            var face = meshData.Faces[faceIdx];
            if (face.VertexCount < 3)
                continue;

            // 面のスクリーン座標を取得
            var screenPoints = new Vector2[face.VertexCount];
            //bool allVisible = true;

            for (int i = 0; i < face.VertexCount; i++)
            {
                var worldPos = meshData.Vertices[face.VertexIndices[i]].Position;
                screenPoints[i] = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

                //if (!previewRect.Contains(screenPoints[i]))
                //{
                //    allVisible = false;
                //}
            }

            // 塗りつぶし（部分的に見える場合も描画）
            DrawFilledPolygon(screenPoints, faceColor);

            // エッジ描画
            Handles.color = edgeColor;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int next = (i + 1) % face.VertexCount;
                Handles.DrawAAPolyLine(3f,
                    new Vector3(screenPoints[i].x, screenPoints[i].y, 0),
                    new Vector3(screenPoints[next].x, screenPoints[next].y, 0));
            }
        }
    }

    /// <summary>
    /// 選択されたエッジを描画
    /// </summary>
    private void DrawSelectedEdges(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Edges.Count == 0)
            return;

        // エッジのハイライト色（シアン）
        Handles.color = new Color(0f, 1f, 1f, 1f);

        foreach (var edge in _selectionState.Edges)
        {
            if (edge.V1 < 0 || edge.V1 >= meshData.VertexCount ||
                edge.V2 < 0 || edge.V2 >= meshData.VertexCount)
                continue;

            Vector3 p1World = meshData.Vertices[edge.V1].Position;
            Vector3 p2World = meshData.Vertices[edge.V2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawAAPolyLine(4f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }
    }

    /// <summary>
    /// 選択された補助線を描画
    /// </summary>
    private void DrawSelectedLines(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Lines.Count == 0)
            return;

        // 補助線のハイライト色（黄色）
        Handles.color = new Color(1f, 1f, 0f, 1f);

        foreach (int lineIdx in _selectionState.Lines)
        {
            if (lineIdx < 0 || lineIdx >= meshData.FaceCount)
                continue;

            var face = meshData.Faces[lineIdx];
            if (face.VertexCount != 2)
                continue;

            Vector3 p1World = meshData.Vertices[face.VertexIndices[0]].Position;
            Vector3 p2World = meshData.Vertices[face.VertexIndices[1]].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawAAPolyLine(4f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }
    }

    /// <summary>
    /// 塗りつぶしポリゴンを描画（凸ポリゴン用、三角形分割）
    /// </summary>
    private void DrawFilledPolygon(Vector2[] points, Color color)
    {
        if (points == null || points.Length < 3)
            return;

        // 簡易的な三角形分割（ファンタイプ、凸ポリゴン前提）
        Color oldColor = GUI.color;
        GUI.color = color;

        // GL描画でポリゴンを塗りつぶし
        if (Event.current.type == EventType.Repaint)
        {
            GL.PushMatrix();

            // 単色マテリアルを使用
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

    private Material _polygonMaterial;

    /// <summary>
    /// ポリゴン描画用マテリアルを取得
    /// </summary>
    private Material GetPolygonMaterial()
    {
        if (_polygonMaterial == null)
        {
            // GUI用の単色シェーダー
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



    private void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        edges.Add((a, b));
    }

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
    /// スクリーン座標からレイを生成（WorldToPreviewPosの逆変換）
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

    /// <summary>
    /// 頂点ハンドル描画（MeshDataベース）
    /// </summary>
    private void DrawVertexHandles(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (!_showVertices || meshData == null)
            return;

        float handleSize = 8f;

        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector2 screenPos = WorldToPreviewPos(meshData.Vertices[i].Position, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            Rect handleRect = new Rect(
                screenPos.x - handleSize / 2,
                screenPos.y - handleSize / 2,
                handleSize,
                handleSize);

            // 選択状態で色分け
            bool isSelected = _selectedVertices.Contains(i);

            // Vertex モードが有効でない場合は頂点を薄く表示
            bool vertexModeEnabled = _selectionState != null && _selectionState.Mode.Has(MeshSelectMode.Vertex);
            float alpha = vertexModeEnabled ? 1f : 0.4f;

            Color col = isSelected
                ? new Color(1f, 0.8f, 0f, alpha)      // 選択=オレンジ黄
                : new Color(1f, 1f, 1f, alpha);       // 未選択=白
            EditorGUI.DrawRect(handleRect, col);

            Color borderCol = isSelected
                ? new Color(1f, 0f, 0f, alpha)
                : new Color(0.5f, 0.5f, 0.5f, alpha);
            DrawRectBorder(handleRect, borderCol);

            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 30, 16), i.ToString(), EditorStyles.miniLabel);
        }

        // 矩形選択オーバーレイ
        if (_editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    /// <summary>
    /// 矩形選択オーバーレイを描画
    /// </summary>
    private void DrawBoxSelectOverlay()
    {
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        // 半透明の塗りつぶし
        Color fillColor = new Color(0.3f, 0.6f, 1f, 0.2f);
        EditorGUI.DrawRect(selectRect, fillColor);

        // 枠線
        Color borderColor = new Color(0.3f, 0.6f, 1f, 0.8f);
        DrawRectBorder(selectRect, borderColor);
    }

    private void DrawRectBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

}