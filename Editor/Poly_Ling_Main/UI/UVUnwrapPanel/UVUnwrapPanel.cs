// UVUnwrapPanel.cs
// UV展開パネル（UIToolkit）
// 投影方式によるUV自動生成
// V2: PanelContext/PanelCommand経由。MeshContext/ModelContext/ToolContext非依存。

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    public class UVUnwrapPanel : EditorWindow
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath       = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uxml";
        private const string UssPath        = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uxml";
        private const string UssPathAssets  = "Assets/Editor/Poly_Ling_Main/UI/UVUnwrapPanel/UVUnwrapPanel.uss";

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素
        // ================================================================

        private Label         _warningLabel, _targetInfo, _statusLabel;
        private VisualElement _projectionSection, _paramsSection, _targetSection;
        private FloatField    _paramScale, _paramOffsetU, _paramOffsetV;
        private Button[]      _projButtons;

        private readonly string[] _projButtonNames = new[]
        {
            "btn-planar-xy", "btn-planar-xz", "btn-planar-yz",
            "btn-box", "btn-cylindrical", "btn-spherical"
        };

        // ================================================================
        // 状態
        // ================================================================

        private ProjectionType _selectedProjection = ProjectionType.PlanarXY;

        // サマリーから取得したデータ（表示専用）
        private int    _selectedMeshCount;
        private int    _totalVertexCount;
        private int    _totalFaceCount;
        private string _meshNamesDisplay = "";
        private int[]  _selectedMasterIndices = new int[0];
        private int    _modelIndex;

        // ================================================================
        // Open
        // ================================================================

        public static UVUnwrapPanel Open(PanelContext ctx)
        {
            var panel = GetWindow<UVUnwrapPanel>();
            panel.titleContent = new GUIContent("UV展開");
            panel.minSize = new Vector2(280, 300);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        public void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
            }
        }

        private void OnDisable()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
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
            _warningLabel      = root.Q<Label>("warning-label");
            _targetInfo        = root.Q<Label>("target-info");
            _statusLabel       = root.Q<Label>("status-label");
            _projectionSection = root.Q<VisualElement>("projection-section");
            _paramsSection     = root.Q<VisualElement>("params-section");
            _targetSection     = root.Q<VisualElement>("target-section");
            _paramScale        = root.Q<FloatField>("param-scale");
            _paramOffsetU      = root.Q<FloatField>("param-offset-u");
            _paramOffsetV      = root.Q<FloatField>("param-offset-v");

            _projButtons = new Button[_projButtonNames.Length];
            for (int i = 0; i < _projButtonNames.Length; i++)
            {
                int idx = i;
                _projButtons[i] = root.Q<Button>(_projButtonNames[i]);
                _projButtons[i]?.RegisterCallback<ClickEvent>(_ => SelectProjection((ProjectionType)idx));
            }

            root.Q<Button>("btn-apply")?.RegisterCallback<ClickEvent>(_ => ApplyUnwrap());
            root.Q<Button>("btn-reset")?.RegisterCallback<ClickEvent>(_ => ResetParams());

            UpdateProjectionButtons();
            RefreshAll();

            if (_ctx?.CurrentView != null)
                OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
        }

        // ================================================================
        // OnViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                ExtractFromView(view);
                RefreshAll();
            }
            finally
            {
                _isReceiving = false;
            }
        }

        private void ExtractFromView(IProjectView view)
        {
            _selectedMeshCount     = 0;
            _totalVertexCount      = 0;
            _totalFaceCount        = 0;
            _meshNamesDisplay      = "";
            _selectedMasterIndices = new int[0];
            _modelIndex            = view?.CurrentModelIndex ?? 0;

            var modelView = view?.CurrentModel;
            if (modelView == null) return;

            var indices = modelView.SelectedDrawableIndices;
            if (indices == null || indices.Length == 0) return;

            var masterIndices = new List<int>();
            var names         = new List<string>();

            foreach (int masterIdx in indices)
            {
                IMeshView meshView = null;
                foreach (var mv in modelView.DrawableList)
                {
                    if (mv.MasterIndex == masterIdx) { meshView = mv; break; }
                }
                if (meshView == null) continue;

                masterIndices.Add(masterIdx);
                _totalVertexCount += meshView.VertexCount;
                _totalFaceCount   += meshView.FaceCount;
                names.Add(meshView.Name ?? $"#{masterIdx}");
            }

            _selectedMasterIndices = masterIndices.ToArray();
            _selectedMeshCount     = masterIndices.Count;
            _meshNamesDisplay      = names.Count <= 3
                ? string.Join(", ", names)
                : $"{names[0]}... ({names.Count}個)";
        }

        // ================================================================
        // 投影方式選択
        // ================================================================

        private void SelectProjection(ProjectionType type)
        {
            _selectedProjection = type;
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
            bool hasContext = _ctx != null;
            bool hasMesh    = _selectedMeshCount > 0;

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
            if (_paramsSection     != null) _paramsSection    .style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (_targetSection     != null) _targetSection    .style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;

            if (_targetInfo != null)
            {
                _targetInfo.text = hasMesh
                    ? $"{_meshNamesDisplay}  V:{_totalVertexCount} F:{_totalFaceCount}"
                    : "メッシュ未選択";
            }
        }

        // ================================================================
        // 適用
        // ================================================================

        private void ApplyUnwrap()
        {
            if (_ctx == null || _selectedMasterIndices.Length == 0)
            {
                SetStatus("対象メッシュがありません");
                return;
            }

            float scale   = _paramScale?.value   ?? 1f;
            float offsetU = _paramOffsetU?.value ?? 0f;
            float offsetV = _paramOffsetV?.value ?? 0f;

            _ctx.SendCommand(new ApplyUvUnwrapCommand(
                _modelIndex, _selectedMasterIndices,
                _selectedProjection, scale, offsetU, offsetV));

            SetStatus($"{_selectedProjection} 投影を適用 (V:{_totalVertexCount} F:{_totalFaceCount})");
        }

        private void ResetParams()
        {
            if (_paramScale   != null) _paramScale.value   = 1f;
            if (_paramOffsetU != null) _paramOffsetU.value = 0f;
            if (_paramOffsetV != null) _paramOffsetV.value = 0f;
            _selectedProjection = ProjectionType.PlanarXY;
            UpdateProjectionButtons();
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }
    }
}
