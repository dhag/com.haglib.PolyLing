// Assets/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.cs
// UV展開パネル（UIToolkit）
// 投影方式によるUV自動生成

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.UI
{
    // ================================================================
    // 投影方式
    // ================================================================

    public enum ProjectionType
    {
        PlanarXY,
        PlanarXZ,
        PlanarYZ,
        Box,
        Cylindrical,
        Spherical
    }

    /// <summary>
    /// UVUnwrapPanel設定
    /// </summary>
    [Serializable]
    public class UVUnwrapPanelSettings : IToolSettings
    {
        public ProjectionType Projection = ProjectionType.PlanarXY;
        public float Scale = 1f;
        public float OffsetU = 0f;
        public float OffsetV = 0f;

        public IToolSettings Clone()
        {
            return new UVUnwrapPanelSettings
            {
                Projection = this.Projection,
                Scale = this.Scale,
                OffsetU = this.OffsetU,
                OffsetV = this.OffsetV,
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not UVUnwrapPanelSettings o) return true;
            return Projection != o.Projection ||
                   !Mathf.Approximately(Scale, o.Scale) ||
                   !Mathf.Approximately(OffsetU, o.OffsetU) ||
                   !Mathf.Approximately(OffsetV, o.OffsetV);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not UVUnwrapPanelSettings o) return;
            Projection = o.Projection;
            Scale = o.Scale;
            OffsetU = o.OffsetU;
            OffsetV = o.OffsetV;
        }
    }

    /// <summary>
    /// UV展開パネル
    /// 投影方式によるUV自動生成（選択メッシュ全体に適用）
    /// </summary>
    public class UVUnwrapPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "UVUnwrapPanel";
        public override string Title => "UV Unwrap";

        private UVUnwrapPanelSettings _settings = new UVUnwrapPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => "UV展開";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uss";

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _targetInfo, _statusLabel;
        private VisualElement _projectionSection, _paramsSection, _targetSection;
        private FloatField _paramScale, _paramOffsetU, _paramOffsetV;

        /// <summary>投影ボタン（ProjectionType順）</summary>
        private Button[] _projButtons;
        private readonly string[] _projButtonNames = new[]
        {
            "btn-planar-xy", "btn-planar-xz", "btn-planar-yz",
            "btn-box", "btn-cylindrical", "btn-spherical"
        };

        /// <summary>現在選択中の投影方式</summary>
        private ProjectionType _selectedProjection = ProjectionType.PlanarXY;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<UVUnwrapPanel>();
            panel.titleContent = new GUIContent("UV展開");
            panel.minSize = new Vector2(280, 300);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _targetInfo = root.Q<Label>("target-info");
            _statusLabel = root.Q<Label>("status-label");
            _projectionSection = root.Q<VisualElement>("projection-section");
            _paramsSection = root.Q<VisualElement>("params-section");
            _targetSection = root.Q<VisualElement>("target-section");

            _paramScale = root.Q<FloatField>("param-scale");
            _paramOffsetU = root.Q<FloatField>("param-offset-u");
            _paramOffsetV = root.Q<FloatField>("param-offset-v");

            // 投影ボタン
            _projButtons = new Button[_projButtonNames.Length];
            for (int i = 0; i < _projButtonNames.Length; i++)
            {
                int idx = i;
                _projButtons[i] = root.Q<Button>(_projButtonNames[i]);
                _projButtons[i]?.RegisterCallback<ClickEvent>(_ => SelectProjection((ProjectionType)idx));
            }

            // 操作ボタン
            root.Q<Button>("btn-apply")?.RegisterCallback<ClickEvent>(_ => ApplyUnwrap());
            root.Q<Button>("btn-reset")?.RegisterCallback<ClickEvent>(_ => ResetParams());

            UpdateProjectionButtons();
            RefreshAll();
        }

        // ================================================================
        // コンテキスト
        // ================================================================

        protected override void OnContextSet()
        {
            RefreshAll();
        }

        protected override void OnUndoRedoPerformed()
        {
            base.OnUndoRedoPerformed();
            RefreshAll();
        }

        // ================================================================
        // 投影方式選択
        // ================================================================

        private void SelectProjection(ProjectionType type)
        {
            _selectedProjection = type;
            _settings.Projection = type;
            UpdateProjectionButtons();
        }

        private void UpdateProjectionButtons()
        {
            if (_projButtons == null) return;
            for (int i = 0; i < _projButtons.Length; i++)
            {
                if (_projButtons[i] == null) continue;
                if ((ProjectionType)i == _selectedProjection)
                    _projButtons[i].AddToClassList("selected");
                else
                    _projButtons[i].RemoveFromClassList("selected");
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            bool hasMesh = HasValidSelection;
            bool hasContext = _context != null;

            if (_warningLabel != null)
            {
                if (!hasContext)
                {
                    _warningLabel.text = "コンテキスト未設定";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else if (!hasMesh)
                {
                    _warningLabel.text = "メッシュが選択されていません";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _warningLabel.style.display = DisplayStyle.None;
                }
            }

            bool showUI = hasContext && hasMesh;
            if (_projectionSection != null) _projectionSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (_paramsSection != null) _paramsSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (_targetSection != null) _targetSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateTargetInfo();
        }

        private void UpdateTargetInfo()
        {
            if (_targetInfo == null) return;

            if (_context == null || Model == null)
            {
                _targetInfo.text = "";
                return;
            }

            // 選択メッシュの情報を集約
            var selectedIndices = Model.SelectedMeshIndices;
            if (selectedIndices == null || selectedIndices.Count == 0)
            {
                _targetInfo.text = "メッシュ未選択";
                return;
            }

            int totalVerts = 0;
            int totalFaces = 0;
            var names = new List<string>();

            foreach (int idx in selectedIndices)
            {
                var mc = Model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                totalVerts += mc.MeshObject.VertexCount;
                totalFaces += mc.MeshObject.FaceCount;
                names.Add(mc.Name ?? $"#{idx}");
            }

            string meshNames = names.Count <= 3
                ? string.Join(", ", names)
                : $"{names[0]}... ({names.Count}個)";

            _targetInfo.text = $"{meshNames}  V:{totalVerts} F:{totalFaces}";
        }

        // ================================================================
        // 適用
        // ================================================================

        private void ApplyUnwrap()
        {
            if (_context == null || Model == null) return;

            var selectedIndices = Model.SelectedMeshIndices;
            if (selectedIndices == null || selectedIndices.Count == 0)
            {
                SetStatus("対象メッシュがありません");
                return;
            }

            float scale = _paramScale?.value ?? 1f;
            float offsetU = _paramOffsetU?.value ?? 0f;
            float offsetV = _paramOffsetV?.value ?? 0f;
            ProjectionType proj = _selectedProjection;

            int totalAffected = 0;

            // 各選択メッシュに対してUndo記録付きで適用
            // RecordTopologyChangeはFirstSelectedMeshObject固定なので、
            // 複数メッシュの場合は各メッシュを一時的に選択して適用する必要がある。
            // ただし現状のIToolPanelBase.RecordTopologyChangeは先頭メッシュのみ対応。
            // → 先頭メッシュにのみ適用し、将来的に複数メッシュ対応を拡張。
            var mc = FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null)
            {
                SetStatus("メッシュデータがありません");
                return;
            }

            int vertCount = meshObj.VertexCount;
            int faceCount = meshObj.FaceCount;

            RecordTopologyChange($"UV Unwrap ({proj})", (obj) =>
            {
                UnwrapMesh(obj, proj, scale, offsetU, offsetV);
            });

            totalAffected = vertCount;
            SetStatus($"{proj} 投影を適用 (V:{totalAffected} F:{faceCount})");
            RefreshAll();
        }

        // ================================================================
        // パラメータリセット
        // ================================================================

        private void ResetParams()
        {
            if (_paramScale != null) _paramScale.value = 1f;
            if (_paramOffsetU != null) _paramOffsetU.value = 0f;
            if (_paramOffsetV != null) _paramOffsetV.value = 0f;
            _selectedProjection = ProjectionType.PlanarXY;
            _settings.Projection = ProjectionType.PlanarXY;
            UpdateProjectionButtons();
        }

        // ================================================================
        // UV展開ロジック
        // ================================================================

        /// <summary>
        /// メッシュにUV展開を適用
        /// </summary>
        private void UnwrapMesh(MeshObject meshObj, ProjectionType proj,
            float scale, float offsetU, float offsetV)
        {
            if (meshObj.VertexCount == 0 || meshObj.FaceCount == 0) return;

            Bounds bounds = meshObj.CalculateBounds();
            Vector3 center = bounds.center;
            Vector3 size = bounds.size;

            // ゼロサイズ防止
            if (size.x < 0.0001f) size.x = 1f;
            if (size.y < 0.0001f) size.y = 1f;
            if (size.z < 0.0001f) size.z = 1f;

            switch (proj)
            {
                case ProjectionType.PlanarXY:
                    UnwrapPlanar(meshObj, bounds, 0, 1, scale, offsetU, offsetV); // X→U, Y→V
                    break;
                case ProjectionType.PlanarXZ:
                    UnwrapPlanar(meshObj, bounds, 0, 2, scale, offsetU, offsetV); // X→U, Z→V
                    break;
                case ProjectionType.PlanarYZ:
                    UnwrapPlanar(meshObj, bounds, 1, 2, scale, offsetU, offsetV); // Y→U, Z→V
                    break;
                case ProjectionType.Box:
                    UnwrapBox(meshObj, bounds, scale, offsetU, offsetV);
                    break;
                case ProjectionType.Cylindrical:
                    UnwrapCylindrical(meshObj, bounds, scale, offsetU, offsetV);
                    break;
                case ProjectionType.Spherical:
                    UnwrapSpherical(meshObj, bounds, scale, offsetU, offsetV);
                    break;
            }
        }

        // ================================================================
        // 平面投影
        // ================================================================

        /// <summary>
        /// 平面投影: 指定2軸でバウンディングボックス基準 [0,1] に正規化
        /// </summary>
        /// <param name="axisU">U軸 (0=X, 1=Y, 2=Z)</param>
        /// <param name="axisV">V軸 (0=X, 1=Y, 2=Z)</param>
        private void UnwrapPlanar(MeshObject meshObj, Bounds bounds,
            int axisU, int axisV, float scale, float offsetU, float offsetV)
        {
            Vector3 bMin = bounds.min;
            Vector3 bSize = bounds.size;

            float sizeU = bSize[axisU];
            float sizeV = bSize[axisV];
            if (sizeU < 0.0001f) sizeU = 1f;
            if (sizeV < 0.0001f) sizeV = 1f;

            foreach (var vertex in meshObj.Vertices)
            {
                float u = (vertex.Position[axisU] - bMin[axisU]) / sizeU;
                float v = (vertex.Position[axisV] - bMin[axisV]) / sizeV;

                u = u * scale + offsetU;
                v = v * scale + offsetV;

                SetVertexUV(vertex, new Vector2(u, v));
            }

            // 全面のUVIndicesを0にリセット（頂点のUVs[0]を参照）
            ResetFaceUVIndices(meshObj);
        }

        // ================================================================
        // ボックス投影
        // ================================================================

        /// <summary>
        /// ボックス投影: 各面の法線を6軸に分類し、対応する平面で投影
        /// </summary>
        private void UnwrapBox(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 bMin = bounds.min;
            Vector3 bSize = bounds.size;

            // 各頂点にUVを設定（面ごとに異なるUVが必要なため、面ベースで処理）
            // ボックス投影では同じ頂点でも面によって異なるUVになる。
            // Vertex.UVsのサブインデックスを使い分ける。

            foreach (var face in meshObj.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;

                // 面法線を計算
                Vector3 normal = ComputeFaceNormal(meshObj, face);

                // 最も支配的な軸を判定
                int dominantAxis = GetDominantAxis(normal);

                // 投影軸を決定
                int axisU, axisV;
                switch (dominantAxis)
                {
                    case 0: // ±X → YZ平面
                        axisU = 1; axisV = 2;
                        break;
                    case 1: // ±Y → XZ平面
                        axisU = 0; axisV = 2;
                        break;
                    default: // ±Z → XY平面
                        axisU = 0; axisV = 1;
                        break;
                }

                float sizeU = bSize[axisU];
                float sizeV = bSize[axisV];
                if (sizeU < 0.0001f) sizeU = 1f;
                if (sizeV < 0.0001f) sizeV = 1f;

                // 面の各頂点にUV設定
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= meshObj.VertexCount) continue;

                    var vertex = meshObj.Vertices[vi];
                    float u = (vertex.Position[axisU] - bMin[axisU]) / sizeU;
                    float v = (vertex.Position[axisV] - bMin[axisV]) / sizeV;

                    u = u * scale + offsetU;
                    v = v * scale + offsetV;

                    // この面のこの頂点コーナー用のUVを設定
                    int uvIdx = vertex.GetOrAddUV(new Vector2(u, v));

                    // UVIndicesを更新
                    while (face.UVIndices.Count <= ci)
                        face.UVIndices.Add(0);
                    face.UVIndices[ci] = uvIdx;
                }
            }
        }

        // ================================================================
        // 円筒投影
        // ================================================================

        /// <summary>
        /// 円筒投影: Y軸中心、XZ平面で角度→U、Y→V
        /// </summary>
        private void UnwrapCylindrical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;
            Vector3 bMin = bounds.min;
            float height = bounds.size.y;
            if (height < 0.0001f) height = 1f;

            foreach (var vertex in meshObj.Vertices)
            {
                Vector3 pos = vertex.Position;
                float dx = pos.x - center.x;
                float dz = pos.z - center.z;

                // 角度 → U [0,1]
                float angle = Mathf.Atan2(dz, dx); // [-π, π]
                float u = (angle + Mathf.PI) / (2f * Mathf.PI); // [0, 1]

                // 高さ → V [0,1]
                float v = (pos.y - bMin.y) / height;

                u = u * scale + offsetU;
                v = v * scale + offsetV;

                SetVertexUV(vertex, new Vector2(u, v));
            }

            ResetFaceUVIndices(meshObj);
        }

        // ================================================================
        // 球面投影
        // ================================================================

        /// <summary>
        /// 球面投影: 中心からの方向ベクトルで角度→U,V
        /// </summary>
        private void UnwrapSpherical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;

            foreach (var vertex in meshObj.Vertices)
            {
                Vector3 dir = (vertex.Position - center).normalized;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.up;

                // 経度 → U
                float u = (Mathf.Atan2(dir.z, dir.x) + Mathf.PI) / (2f * Mathf.PI);

                // 緯度 → V
                float v = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f;

                u = u * scale + offsetU;
                v = v * scale + offsetV;

                SetVertexUV(vertex, new Vector2(u, v));
            }

            ResetFaceUVIndices(meshObj);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 頂点のUVs[0]を設定（なければ追加、あれば上書き）
        /// </summary>
        private static void SetVertexUV(Poly_Ling.Data.Vertex vertex, Vector2 uv)
        {
            if (vertex.UVs.Count == 0)
                vertex.UVs.Add(uv);
            else
                vertex.UVs[0] = uv;
        }

        /// <summary>
        /// 全面のUVIndicesを0にリセット
        /// 平面投影など、全頂点がUVs[0]のみ使用する場合用
        /// </summary>
        private static void ResetFaceUVIndices(MeshObject meshObj)
        {
            foreach (var face in meshObj.Faces)
            {
                if (face == null) continue;
                for (int i = 0; i < face.UVIndices.Count; i++)
                    face.UVIndices[i] = 0;
            }
        }

        /// <summary>
        /// 面法線を計算（最初の3頂点の外積）
        /// </summary>
        private static Vector3 ComputeFaceNormal(MeshObject meshObj, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;

            int i0 = face.VertexIndices[0];
            int i1 = face.VertexIndices[1];
            int i2 = face.VertexIndices[2];

            if (i0 < 0 || i0 >= meshObj.VertexCount ||
                i1 < 0 || i1 >= meshObj.VertexCount ||
                i2 < 0 || i2 >= meshObj.VertexCount)
                return Vector3.up;

            Vector3 p0 = meshObj.Vertices[i0].Position;
            Vector3 p1 = meshObj.Vertices[i1].Position;
            Vector3 p2 = meshObj.Vertices[i2].Position;

            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            return normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
        }

        /// <summary>
        /// 法線ベクトルの最も支配的な軸インデックスを返す (0=X, 1=Y, 2=Z)
        /// </summary>
        private static int GetDominantAxis(Vector3 normal)
        {
            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            if (absX >= absY && absX >= absZ) return 0;
            if (absY >= absX && absY >= absZ) return 1;
            return 2;
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text;
        }

        // ================================================================
        // Update
        // ================================================================

        private int _lastMeshIndex = -1;

        private void Update()
        {
            if (_context == null) return;

            int currentMeshIndex = Model?.FirstSelectedIndex ?? -1;
            if (currentMeshIndex != _lastMeshIndex)
            {
                _lastMeshIndex = currentMeshIndex;
                RefreshAll();
            }
        }
    }
}
