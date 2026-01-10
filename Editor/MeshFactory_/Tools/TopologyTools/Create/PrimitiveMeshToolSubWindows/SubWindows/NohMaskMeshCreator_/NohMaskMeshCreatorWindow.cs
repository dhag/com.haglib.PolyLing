// Assets/Editor/MeshCreators/NohMaskMeshCreatorWindow.cs
// FaceMesh（MediaPipe Face Landmarks）ベースメッシュ生成用のサブウインドウ
// MeshCreatorWindowBase継承版
// JSONファイル（facemesh_triangles.json, face_landmarks.json）からメッシュを生成
//
// 【座標系変換】
// MediaPipe: x(左→右, 0→1), y(上→下, 0→1), z(手前→奥, 負→正)
// Unity:     x(左→右), y(下→上), z(手前→奥)
// → yを反転 (1 - y) して変換
//
// 【重要】
// MediaPipe FaceMeshのランドマークは一意のインデックスを持つため、
// 頂点の自動結合は絶対に行わない

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;

public partial class NohMaskMeshCreatorWindow : MeshCreatorWindowBase<NohMaskMeshCreatorWindow.FaceMeshParams>
{
    // ================================================================
    // JSON構造体定義
    // ================================================================

    [Serializable]
    private class FaceLandmarksJson
    {
        public string schema;
        public int num_faces_detected;
        public FaceData[] faces;
    }

    [Serializable]
    private class FaceData
    {
        public int face_index;
        public ImageData image;
        public Landmark[] landmarks;
    }

    [Serializable]
    private class ImageData
    {
        public string path;
        public int width;
        public int height;
    }

    [Serializable]
    private class Landmark
    {
        public int index;
        public float x;
        public float y;
        public float z;
        public float pixel_x;
        public float pixel_y;
    }

    [Serializable]
    private class FaceMeshTrianglesJson
    {
        public int triangle_count;
        public int vertex_count;
        public int[][] triangles;
    }

    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct FaceMeshParams : IEquatable<FaceMeshParams>
    {
        public string MeshName;
        public string LandmarksFilePath;
        public string TrianglesFilePath;
        public float Scale;
        public float DepthScale;
        public float RotationX, RotationY;
        public int FaceIndex;
        public bool FlipFaces;

        public static FaceMeshParams Default => new FaceMeshParams
        {
            MeshName = "FaceMesh",
            LandmarksFilePath = "",
            TrianglesFilePath = "",
            Scale = 10f,
            DepthScale = 1f,
            RotationX = 0f,
            RotationY = 180f,
            FaceIndex = 0,
            FlipFaces = false
        };

        public bool Equals(FaceMeshParams o) =>
            MeshName == o.MeshName &&
            LandmarksFilePath == o.LandmarksFilePath &&
            TrianglesFilePath == o.TrianglesFilePath &&
            Mathf.Approximately(Scale, o.Scale) &&
            Mathf.Approximately(DepthScale, o.DepthScale) &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY) &&
            FaceIndex == o.FaceIndex &&
            FlipFaces == o.FlipFaces;

        public override bool Equals(object obj) => obj is FaceMeshParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // キャッシュデータ
    // ================================================================
    private FaceLandmarksJson _cachedLandmarks;
    private FaceMeshTrianglesJson _cachedTriangles;
    private string _lastLandmarksPath;
    private string _lastTrianglesPath;

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "FaceMeshCreator";
    protected override string UndoDescription => "FaceMesh Parameters";
    protected override float PreviewCameraDistance => _params.Scale * 0.15f;

    protected override FaceMeshParams GetDefaultParams() => FaceMeshParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // プレビューマテリアル色を肌色系に
    // AutoMergeは無効化（FaceMeshのインデックスを保持するため）
    protected override void OnInitialize()
    {
        _autoMergeOnCreate = false;
        
        if (_previewMaterial != null)
        {
            _previewMaterial.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.75f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.9f, 0.85f, 0.75f, 1f));
        }
    }

    // AutoMerge UIを非表示
    protected override void DrawAutoMergeUI()
    {
        // FaceMeshでは頂点結合を行わないため非表示
    }

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static NohMaskMeshCreatorWindow Open(Action<MeshObject, string> onMeshObjectCreated)
    {
        var window = GetWindow<NohMaskMeshCreatorWindow>(true, T("WindowTitle"), true);
        window.minSize = new Vector2(420, 700);
        window.maxSize = new Vector2(500, 850);
        window._onMeshObjectCreated = onMeshObjectCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField(T("Parameters"), EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField(T("Name"), _params.MeshName);
        EditorGUILayout.Space(5);

        // JSONファイル選択
        EditorGUILayout.LabelField(T("JsonFiles"), EditorStyles.miniBoldLabel);

        // Landmarks JSON
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(T("Landmarks"), GUILayout.Width(100));
        string landmarksDisplay = string.IsNullOrEmpty(_params.LandmarksFilePath) 
            ? T("NotSelected")
            : System.IO.Path.GetFileName(_params.LandmarksFilePath);
        EditorGUILayout.LabelField(landmarksDisplay, EditorStyles.textField);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string path = EditorUtility.OpenFilePanel(T("SelectLandmarks"), "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                _params.LandmarksFilePath = path;
                GUI.changed = true;
            }
        }
        EditorGUILayout.EndHorizontal();

        // Landmarks ドロップエリア
        DrawDropArea(T("DragDropLandmarks"), ".json", (path) => {
            _params.LandmarksFilePath = path;
            GUI.changed = true;
            UpdatePreviewMesh();
        });

        EditorGUILayout.Space(3);

        // Triangles JSON
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(T("Triangles"), GUILayout.Width(100));
        string trianglesDisplay = string.IsNullOrEmpty(_params.TrianglesFilePath) 
            ? T("NotSelected")
            : System.IO.Path.GetFileName(_params.TrianglesFilePath);
        EditorGUILayout.LabelField(trianglesDisplay, EditorStyles.textField);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string path = EditorUtility.OpenFilePanel(T("SelectTriangles"), "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                _params.TrianglesFilePath = path;
                GUI.changed = true;
            }
        }
        EditorGUILayout.EndHorizontal();

        // Triangles ドロップエリア
        DrawDropArea(T("DragDropTriangles"), ".json", (path) => {
            _params.TrianglesFilePath = path;
            GUI.changed = true;
            UpdatePreviewMesh();
        });

        EditorGUILayout.Space(5);

        // 顔インデックス（複数顔対応）
        if (_cachedLandmarks != null && _cachedLandmarks.num_faces_detected > 1)
        {
            _params.FaceIndex = EditorGUILayout.IntSlider(
                T("FaceIndex"), _params.FaceIndex, 0, _cachedLandmarks.num_faces_detected - 1);
        }

        // スケール（1-10、デフォルト10）
        EditorGUILayout.LabelField(T("Transform"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Scale = EditorGUILayout.Slider(T("Scale"), _params.Scale, 1f, 10f);
            _params.DepthScale = EditorGUILayout.Slider(T("DepthScale"), _params.DepthScale, 0.1f, 5f);
            _params.FlipFaces = EditorGUILayout.Toggle(T("FlipFaces"), _params.FlipFaces);
        }

        EndParamChange();

        EditorGUILayout.Space(5);

        // 情報表示
        if (_cachedLandmarks != null && _cachedTriangles != null)
        {
            EditorGUILayout.LabelField(T("Info"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (_cachedLandmarks.faces != null && _cachedLandmarks.faces.Length > 0)
                {
                    EditorGUILayout.LabelField(T("LandmarkCount", _cachedLandmarks.faces[0].landmarks.Length));
                }
                EditorGUILayout.LabelField(T("TriangleCount", _cachedTriangles.triangle_count));
                if (_cachedLandmarks != null && _cachedLandmarks.num_faces_detected > 1)
                {
                    EditorGUILayout.LabelField(T("FaceCount", _cachedLandmarks.num_faces_detected));
                }
            }
        }
        else if (string.IsNullOrEmpty(_params.LandmarksFilePath) || string.IsNullOrEmpty(_params.TrianglesFilePath))
        {
            EditorGUILayout.HelpBox(T("SelectJsonFiles"), MessageType.Info);
        }
    }

    /// <summary>
    /// ドロップエリアを描画
    /// </summary>
    private void DrawDropArea(string label, string extension, System.Action<string> onDrop)
    {
        var dropArea = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, label, EditorStyles.helpBox);

        Event evt = Event.current;
        if (dropArea.Contains(evt.mousePosition))
        {
            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (IsValidFile(DragAndDrop.paths, extension))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;

                case EventType.DragPerform:
                    if (IsValidFile(DragAndDrop.paths, extension))
                    {
                        DragAndDrop.AcceptDrag();
                        onDrop?.Invoke(DragAndDrop.paths[0]);
                        evt.Use();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// ファイル拡張子チェック
    /// </summary>
    private bool IsValidFile(string[] paths, string extension)
    {
        if (paths == null || paths.Length == 0) return false;
        string ext = System.IO.Path.GetExtension(paths[0]).ToLower();
        return ext == extension.ToLower();
    }

    // ================================================================
    // プレビュー（マウスドラッグ回転対応）
    // ================================================================
    protected override void DrawPreview()
    {
        EditorGUILayout.LabelField(T("Preview"), EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

        if (_previewMeshObject != null)
        {
            EditorGUILayout.LabelField(
                T("VertsFaces", _previewMeshObject.VertexCount, _previewMeshObject.FaceCount),
                EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.LabelField(" ", EditorStyles.miniLabel);
        }

        Event e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                _params.RotationY += e.delta.x * 0.5f;
                _params.RotationX += e.delta.y * 0.5f;
                _params.RotationX = Mathf.Clamp(_params.RotationX, -89f, 89f);
                e.Use();
                Repaint();
            }
        }

        if (e.type != EventType.Repaint) return;
        if (_preview == null || _previewMesh == null) return;

        _preview.BeginPreview(rect, GUIStyle.none);
        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        Quaternion rot = Quaternion.Euler(_params.RotationX, _params.RotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -PreviewCameraDistance);
        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(Vector3.zero);

        if (_previewMaterial != null)
        {
            _preview.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
        }

        _preview.camera.Render();
        GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
    }

    // ================================================================
    // JSON読み込み
    // ================================================================
    private void LoadJsonData()
    {
        // Landmarksキャッシュ更新
        if (_params.LandmarksFilePath != _lastLandmarksPath)
        {
            _lastLandmarksPath = _params.LandmarksFilePath;
            _cachedLandmarks = null;

            if (!string.IsNullOrEmpty(_params.LandmarksFilePath) && System.IO.File.Exists(_params.LandmarksFilePath))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(_params.LandmarksFilePath);
                    _cachedLandmarks = JsonUtility.FromJson<FaceLandmarksJson>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FaceMeshCreator] Failed to load landmarks JSON: {ex.Message}");
                }
            }
        }

        // Trianglesキャッシュ更新
        if (_params.TrianglesFilePath != _lastTrianglesPath)
        {
            _lastTrianglesPath = _params.TrianglesFilePath;
            _cachedTriangles = null;

            if (!string.IsNullOrEmpty(_params.TrianglesFilePath) && System.IO.File.Exists(_params.TrianglesFilePath))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(_params.TrianglesFilePath);
                    _cachedTriangles = ParseTrianglesJson(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FaceMeshCreator] Failed to load triangles JSON: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 三角形JSONを手動パース（JsonUtilityはネストした配列を扱えないため）
    /// </summary>
    private FaceMeshTrianglesJson ParseTrianglesJson(string json)
    {
        var result = new FaceMeshTrianglesJson();
        var trianglesList = new List<int[]>();

        // triangle_count と vertex_count を正規表現で取得
        var triangleCountMatch = System.Text.RegularExpressions.Regex.Match(json, @"""triangle_count""\s*:\s*(\d+)");
        if (triangleCountMatch.Success)
        {
            int.TryParse(triangleCountMatch.Groups[1].Value, out result.triangle_count);
        }

        var vertexCountMatch = System.Text.RegularExpressions.Regex.Match(json, @"""vertex_count""\s*:\s*(\d+)");
        if (vertexCountMatch.Success)
        {
            int.TryParse(vertexCountMatch.Groups[1].Value, out result.vertex_count);
        }

        // triangles配列を正規表現で抽出
        // パターン: [数値, 数値, 数値] （改行・スペース含む）
        var trianglePattern = new System.Text.RegularExpressions.Regex(@"\[\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]");
        var matches = trianglePattern.Matches(json);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count >= 4)
            {
                int i0 = int.Parse(match.Groups[1].Value);
                int i1 = int.Parse(match.Groups[2].Value);
                int i2 = int.Parse(match.Groups[3].Value);
                trianglesList.Add(new int[] { i0, i1, i2 });
            }
        }

        result.triangles = trianglesList.ToArray();
        return result;
    }

    // ================================================================
    // MeshObject生成
    // ★★★ 頂点の自動結合は行わない ★★★
    // ================================================================
    protected override MeshObject GenerateMeshObject()
    {
        LoadJsonData();

        var md = new MeshObject(_params.MeshName);

        if (_cachedLandmarks == null || _cachedTriangles == null)
        {
            return md;
        }

        if (_cachedLandmarks.faces == null || _cachedLandmarks.faces.Length == 0)
        {
            return md;
        }

        int faceIndex = Mathf.Clamp(_params.FaceIndex, 0, _cachedLandmarks.faces.Length - 1);
        var faceData = _cachedLandmarks.faces[faceIndex];

        if (faceData.landmarks == null || faceData.landmarks.Length == 0)
        {
            return md;
        }

        // ランドマークから頂点を生成
        // 中心を計算
        Vector3 center = Vector3.zero;
        foreach (var lm in faceData.landmarks)
        {
            center += new Vector3(lm.x, lm.y, lm.z);
        }
        center /= faceData.landmarks.Length;

        // 頂点位置を計算（面生成前に全頂点位置が必要）
        var positions = new Vector3[faceData.landmarks.Length];
        for (int i = 0; i < faceData.landmarks.Length; i++)
        {
            var lm = faceData.landmarks[i];

            // MediaPipe座標系からUnity座標系に変換
            // x: そのまま, y: 反転, z: 奥行き（反転してカメラ向き）
            float x = (lm.x - center.x) * _params.Scale;
            float y = ((1f - lm.y) - (1f - center.y)) * _params.Scale;
            float z = -(lm.z - center.z) * _params.Scale * _params.DepthScale;

            positions[i] = new Vector3(x, y, z);
        }

        // 頂点を追加（結合しない、インデックスをそのまま維持）
        for (int i = 0; i < faceData.landmarks.Length; i++)
        {
            var lm = faceData.landmarks[i];
            Vector2 uv = new Vector2(lm.x, 1f - lm.y);
            md.Vertices.Add(new Vertex(positions[i], uv, Vector3.forward));
        }

        // 三角形面を生成
        foreach (var tri in _cachedTriangles.triangles)
        {
            if (tri == null || tri.Length != 3)
                continue;

            int i0 = tri[0];
            int i1 = tri[1];
            int i2 = tri[2];

            // 頂点インデックスが範囲内か確認
            if (i0 >= md.VertexCount || i1 >= md.VertexCount || i2 >= md.VertexCount)
                continue;
            if (i0 < 0 || i1 < 0 || i2 < 0)
                continue;

            // 三角形を追加
            // 反転チェックなし: そのまま (i0, i1, i2)
            // 反転チェックあり: 逆回転 (i0, i2, i1)
            if (_params.FlipFaces)
            {
                md.AddTriangle(i0, i2, i1);
            }
            else
            {
                md.AddTriangle(i0, i1, i2);
            }
        }

        // 法線を再計算（頂点順序に従って計算される）
        md.RecalculateSmoothNormals();

        return md;
    }
}
