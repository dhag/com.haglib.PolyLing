// Assets/Editor/MeshCreators/PlaneMeshCreatorWindow.cs
// プレーングリッドメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)
// 裏面: md.AddQuad(i0, i3, i2, i1)

using System;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;

public class PlaneMeshCreatorWindow : MeshCreatorWindowBase<PlaneMeshCreatorWindow.PlaneParams>
{
    public enum PlaneOrientation { XY, XZ, YZ }

    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct PlaneParams : IEquatable<PlaneParams>
    {
        public string MeshName;
        public float Width, Height;
        public int WidthSegments, HeightSegments;
        public bool DoubleSided;
        public PlaneOrientation Orientation;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static PlaneParams Default => new PlaneParams
        {
            MeshName = "Plane",
            Width = 1f,
            Height = 1f,
            WidthSegments = 1,
            HeightSegments = 1,
            DoubleSided = false,
            Orientation = PlaneOrientation.XZ,
            Pivot = Vector3.zero,
            RotationX = 45f,
            RotationY = 30f
        };

        public bool Equals(PlaneParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(Width, o.Width) &&
            Mathf.Approximately(Height, o.Height) &&
            WidthSegments == o.WidthSegments &&
            HeightSegments == o.HeightSegments &&
            DoubleSided == o.DoubleSided &&
            Orientation == o.Orientation &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is PlaneParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "PlaneCreator";
    protected override string UndoDescription => "Plane Parameters";
    protected override float PreviewCameraDistance => Mathf.Max(_params.Width, _params.Height) * 2.5f;

    protected override PlaneParams GetDefaultParams() => PlaneParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static PlaneMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<PlaneMeshCreatorWindow>(true, "Create Plane UnityMesh", true);
        window.minSize = new Vector2(400, 580);
        window.maxSize = new Vector2(500, 780);
        window._onMeshDataCreated = onMeshDataCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField("Plane Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        _params.Width = EditorGUILayout.Slider("Width", _params.Width, 0.1f, 10f);
        _params.Height = EditorGUILayout.Slider("Height", _params.Height, 0.1f, 10f);
        _params.WidthSegments = EditorGUILayout.IntSlider("Width Segments", _params.WidthSegments, 1, 32);
        _params.HeightSegments = EditorGUILayout.IntSlider("Height Segments", _params.HeightSegments, 1, 32);

        EditorGUILayout.Space(5);

        _params.Orientation = (PlaneOrientation)EditorGUILayout.EnumPopup("Orientation", _params.Orientation);
        _params.DoubleSided = EditorGUILayout.Toggle("Double Sided", _params.DoubleSided);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider("X", _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider("Y", _params.Pivot.y, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button("Corner", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0.5f, 0.5f, 0);
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        EndParamChange();
    }

    // ================================================================
    // プレビュー（マウスドラッグ回転対応）
    // ================================================================
    protected override void DrawPreview()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

        if (_previewMeshData != null)
        {
            EditorGUILayout.LabelField(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount}",
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
    // MeshData生成
    // ================================================================
    protected override MeshData GenerateMeshData()
    {
        var md = new MeshData(_params.MeshName);

        Vector3 pivotOffset = new Vector3(_params.Pivot.x * _params.Width, _params.Pivot.y * _params.Height, 0);

        AddPlaneFace(md, pivotOffset, false);

        if (_params.DoubleSided)
        {
            AddPlaneFace(md, pivotOffset, true);
        }

        return md;
    }

    private void AddPlaneFace(MeshData md, Vector3 pivotOffset, bool flip)
    {
        int startIdx = md.VertexCount;

        Vector3 normal;
        switch (_params.Orientation)
        {
            case PlaneOrientation.XY:
                normal = flip ? Vector3.back : Vector3.forward;
                break;
            case PlaneOrientation.XZ:
                normal = flip ? Vector3.down : Vector3.up;
                break;
            case PlaneOrientation.YZ:
                normal = flip ? Vector3.left : Vector3.right;
                break;
            default:
                normal = Vector3.up;
                break;
        }

        for (int h = 0; h <= _params.HeightSegments; h++)
        {
            for (int w = 0; w <= _params.WidthSegments; w++)
            {
                float u = (float)w / _params.WidthSegments;
                float v = (float)h / _params.HeightSegments;

                float x = (u - 0.5f) * _params.Width;
                float y = (v - 0.5f) * _params.Height;

                Vector3 pos;
                switch (_params.Orientation)
                {
                    case PlaneOrientation.XY:
                        pos = new Vector3(x - pivotOffset.x, y - pivotOffset.y, 0);
                        break;
                    case PlaneOrientation.XZ:
                        pos = new Vector3(x - pivotOffset.x, 0, -y + pivotOffset.y);
                        break;
                    case PlaneOrientation.YZ:
                        pos = new Vector3(0, y - pivotOffset.y, -x + pivotOffset.x);
                        break;
                    default:
                        pos = new Vector3(x, 0, -y);
                        break;
                }

                Vector2 uv = new Vector2(flip ? (1f - u) : u, v);
                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        int cols = _params.WidthSegments + 1;
        for (int h = 0; h < _params.HeightSegments; h++)
        {
            for (int w = 0; w < _params.WidthSegments; w++)
            {
                int i0 = startIdx + h * cols + w;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                if (flip)
                    md.AddQuad(i0, i3, i2, i1);
                else
                    md.AddQuad(i0, i1, i2, i3);
            }
        }
    }
}
