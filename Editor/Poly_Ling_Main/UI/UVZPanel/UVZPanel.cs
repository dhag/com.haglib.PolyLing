// UVZPanel.cs
// UVZパネル
// UV値をXY、カメラ深度をZとする新メッシュ生成 / XYZからUVへの書き戻し
// V2: PanelContext/PanelCommand経由。MeshContext/ModelContext/ToolContext非依存。

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class UVZPanel : EditorWindow, IPanelContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath       = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uxml";
        private const string UssPath        = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uxml";
        private const string UssPathAssets  = "Assets/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uss";

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素
        // ================================================================

        private Label         _warningLabel, _targetInfo, _cameraInfo, _statusLabel;
        private VisualElement _mainSection, _writebackSection;
        private FloatField    _fieldUvScale, _fieldDepthScale;
        private PopupField<string> _writebackTarget;

        // ================================================================
        // 状態（サマリーから取得）
        // ================================================================

        private int    _modelIndex;
        private int    _selectedMasterIndex = -1;
        private string _selectedMeshName    = "";
        private int    _selectedVertexCount;
        private int    _selectedFaceCount;
        private int    _selectedUvCount;

        // 書き戻しターゲット候補（MasterIndex, 表示名）
        private List<(int masterIndex, string label)> _writebackCandidates
            = new List<(int, string)>();

        private float _uvScale    = 10f;
        private float _depthScale = 1f;

        // ================================================================
        // Open
        // ================================================================

        public static UVZPanel Open(PanelContext ctx)
        {
            var panel = GetWindow<UVZPanel>();
            panel.titleContent = new GUIContent("UVZ");
            panel.minSize = new Vector2(300, 320);
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
            _warningLabel    = root.Q<Label>("warning-label");
            _targetInfo      = root.Q<Label>("target-info");
            _cameraInfo      = root.Q<Label>("camera-info");
            _statusLabel     = root.Q<Label>("status-label");
            _mainSection     = root.Q<VisualElement>("main-section");
            _writebackSection = root.Q<VisualElement>("writeback-section");

            _fieldUvScale    = root.Q<FloatField>("field-uv-scale");
            _fieldDepthScale = root.Q<FloatField>("field-depth-scale");

            if (_fieldUvScale != null)
            {
                _fieldUvScale.value = _uvScale;
                _fieldUvScale.RegisterValueChangedCallback(evt =>
                    _uvScale = Mathf.Max(evt.newValue, 0.001f));
            }

            if (_fieldDepthScale != null)
            {
                _fieldDepthScale.value = _depthScale;
                _fieldDepthScale.RegisterValueChangedCallback(evt =>
                    _depthScale = Mathf.Max(evt.newValue, 0.001f));
            }

            // 書き戻しターゲットPopup
            var writebackTargetContainer = root.Q<VisualElement>("writeback-target-container");
            if (writebackTargetContainer != null)
            {
                _writebackTarget = new PopupField<string>("ターゲット", new List<string> { "(なし)" }, 0);
                _writebackTarget.AddToClassList("uvz-popup");
                writebackTargetContainer.Add(_writebackTarget);
            }

            root.Q<Button>("btn-uv-to-xyz")?.RegisterCallback<ClickEvent>(_ => ExecuteUvToXyz());
            root.Q<Button>("btn-xyz-to-uv")?.RegisterCallback<ClickEvent>(_ => ExecuteXyzToUv());

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
            _selectedMasterIndex = -1;
            _selectedMeshName    = "";
            _selectedVertexCount = 0;
            _selectedFaceCount   = 0;
            _selectedUvCount     = 0;
            _writebackCandidates.Clear();
            _modelIndex = view?.CurrentModelIndex ?? 0;

            var modelView = view?.CurrentModel;
            if (modelView == null) return;

            // 選択中の先頭Drawableを対象とする
            var indices = modelView.SelectedDrawableIndices;
            if (indices != null && indices.Length > 0)
            {
                int masterIdx = indices[0];
                foreach (var mv in modelView.DrawableList)
                {
                    if (mv.MasterIndex != masterIdx) continue;
                    _selectedMasterIndex = masterIdx;
                    _selectedMeshName    = mv.Name ?? $"#{masterIdx}";
                    _selectedVertexCount = mv.VertexCount;
                    _selectedFaceCount   = mv.FaceCount;
                    // UV数はサマリーに含まれないため頂点数で代用
                    _selectedUvCount     = mv.VertexCount;
                    break;
                }
            }

            // 書き戻し候補: 選択中以外の全Drawable
            foreach (var mv in modelView.DrawableList)
            {
                if (mv.MasterIndex == _selectedMasterIndex) continue;
                _writebackCandidates.Add((mv.MasterIndex, $"[{mv.MasterIndex}] {mv.Name}"));
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            bool hasContext = _ctx != null;
            bool hasMesh    = _selectedMasterIndex >= 0;

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
            if (_mainSection      != null) _mainSection     .style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (_writebackSection != null) _writebackSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;

            if (_targetInfo != null)
            {
                _targetInfo.text = hasMesh
                    ? $"{_selectedMeshName}  V:{_selectedVertexCount} F:{_selectedFaceCount}"
                    : "メッシュ未選択";
            }

            UpdateCameraInfo();
            UpdateWritebackTargetList();
        }

        private void UpdateCameraInfo()
        {
            if (_cameraInfo == null) return;
            var cam = SceneView.lastActiveSceneView?.camera;
            if (cam == null)
            {
                _cameraInfo.text = "カメラ: -";
                return;
            }
            Vector3 fwd = cam.transform.forward;
            _cameraInfo.text = $"カメラ Dir:({fwd.x:F2}, {fwd.y:F2}, {fwd.z:F2})";
        }

        private void UpdateWritebackTargetList()
        {
            if (_writebackTarget == null) return;

            var choices = new List<string>();
            foreach (var (_, label) in _writebackCandidates)
                choices.Add(label);

            if (choices.Count == 0)
                choices.Add("(なし)");

            var container = _writebackTarget.parent;
            if (container != null)
            {
                int prevIndex = Mathf.Clamp(_writebackTarget.index, 0, choices.Count - 1);
                container.Remove(_writebackTarget);
                _writebackTarget = new PopupField<string>("ターゲット", choices, prevIndex);
                _writebackTarget.AddToClassList("uvz-popup");
                container.Add(_writebackTarget);
            }
        }

        // ================================================================
        // UV→XYZ 展開
        // ================================================================

        private void ExecuteUvToXyz()
        {
            if (_ctx == null || _selectedMasterIndex < 0)
            {
                SetStatus("メッシュが選択されていません");
                return;
            }

            var cam = SceneView.lastActiveSceneView?.camera;
            Vector3 camPos     = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 camForward = cam != null ? cam.transform.forward  : Vector3.forward;

            _ctx.SendCommand(new UvToXyzCommand(
                _modelIndex, _selectedMasterIndex,
                _uvScale, _depthScale, camPos, camForward));

            SetStatus($"UV→XYZ コマンド送信: '{_selectedMeshName}'");
        }

        // ================================================================
        // XYZ→UV 書き戻し
        // ================================================================

        private void ExecuteXyzToUv()
        {
            if (_ctx == null || _selectedMasterIndex < 0)
            {
                SetStatus("ソースメッシュが選択されていません");
                return;
            }

            int targetMasterIndex = GetWritebackTargetMasterIndex();
            if (targetMasterIndex < 0)
            {
                SetStatus("書き戻し先が選択されていません");
                return;
            }

            _ctx.SendCommand(new XyzToUvCommand(
                _modelIndex, _selectedMasterIndex, targetMasterIndex, _uvScale));

            SetStatus($"XYZ→UV コマンド送信 → [{targetMasterIndex}]");
        }

        private int GetWritebackTargetMasterIndex()
        {
            if (_writebackTarget == null) return -1;
            string val = _writebackTarget.value;
            if (string.IsNullOrEmpty(val) || val == "(なし)") return -1;

            // "[masterIndex] name" からインデックスを抽出
            int bracketEnd = val.IndexOf(']');
            if (bracketEnd < 2) return -1;
            if (int.TryParse(val.Substring(1, bracketEnd - 1), out int idx))
                return idx;
            return -1;
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }
    }
}
