// MergeMeshesPanel.cs
// 選択メッシュオブジェクトのマージパネル（V2 PanelContext構成）
// UXML/USS不要のコード完結型

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class MergeMeshesPanel : EditorWindow, IPanelContextReceiver
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private Label         _selectionLabel;
        private ListView      _baseObjectList;
        private Toggle        _createNewMeshToggle;
        private Button        _mergeButton;
        private Label         _statusLabel;

        // ================================================================
        // 状態
        // ================================================================

        // 現在の選択メッシュビューリスト（リストボックスのデータソース）
        private readonly List<IMeshView> _selectedMeshViews = new List<IMeshView>();

        // リストボックスで選択されている基準オブジェクトのインデックス（_selectedMeshViews内）
        private int _baseListIndex = 0;

        // ================================================================
        // Open
        // ================================================================

        public static MergeMeshesPanel Open(PanelContext ctx)
        {
            var w = GetWindow<MergeMeshesPanel>();
            w.titleContent = new GUIContent("メッシュマージ");
            w.minSize = new Vector2(300, 280);
            w.SetContext(ctx);
            w.Show();
            return w;
        }

        // ================================================================
        // IPanelContextReceiver
        // ================================================================

        public void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) Refresh(_ctx.CurrentView);
            }
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_ctx != null)
            {
                _ctx.OnViewChanged -= OnViewChanged;
                _ctx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            if (_ctx?.CurrentView != null) Refresh(_ctx.CurrentView);
        }

        // ================================================================
        // OnViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.Selection || kind == ChangeKind.ListStructure || kind == ChangeKind.ModelSwitch)
                Refresh(view);
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // 警告ラベル
            _warningLabel = new Label();
            _warningLabel.style.color        = new Color(0.8f, 0.8f, 0.3f);
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.display      = DisplayStyle.None;
            root.Add(_warningLabel);

            // 選択数ラベル
            _selectionLabel = new Label("選択メッシュ: 0");
            _selectionLabel.style.marginBottom = 4;
            root.Add(_selectionLabel);

            // 基準オブジェクト選択ラベル
            var baseLabel = new Label("基準オブジェクト（トランスフォーム）:");
            baseLabel.style.marginBottom = 2;
            root.Add(baseLabel);

            // 基準オブジェクト リストボックス
            _baseObjectList = new ListView
            {
                selectionType    = SelectionType.Single,
                fixedItemHeight  = 22,
                makeItem         = () =>
                {
                    var lbl = new Label();
                    lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                    lbl.style.paddingLeft    = 4;
                    return lbl;
                },
                bindItem = (elem, i) =>
                {
                    if (elem is Label lbl && i < _selectedMeshViews.Count)
                        lbl.text = _selectedMeshViews[i].Name;
                },
                itemsSource = _selectedMeshViews
            };
            _baseObjectList.style.flexGrow    = 1;
            _baseObjectList.style.minHeight   = 100;
            _baseObjectList.style.marginBottom = 6;
            _baseObjectList.style.borderTopWidth    = 1;
            _baseObjectList.style.borderBottomWidth = 1;
            _baseObjectList.style.borderLeftWidth   = 1;
            _baseObjectList.style.borderRightWidth  = 1;
            _baseObjectList.style.borderTopColor    = new Color(0.3f, 0.3f, 0.3f);
            _baseObjectList.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            _baseObjectList.style.borderLeftColor   = new Color(0.3f, 0.3f, 0.3f);
            _baseObjectList.style.borderRightColor  = new Color(0.3f, 0.3f, 0.3f);

#if UNITY_2022_2_OR_NEWER
            _baseObjectList.selectionChanged += _ => OnBaseSelectionChanged();
#else
            _baseObjectList.onSelectionChange += _ => OnBaseSelectionChanged();
#endif
            root.Add(_baseObjectList);

            // 新規メッシュに格納するチェックボックス
            _createNewMeshToggle = new Toggle("新規メッシュオブジェクトに格納する") { value = true };
            _createNewMeshToggle.style.marginBottom = 8;
            root.Add(_createNewMeshToggle);

            // マージ実行ボタン
            _mergeButton = new Button(OnMergeClicked) { text = "マージ実行" };
            _mergeButton.style.height       = 28;
            _mergeButton.style.marginBottom = 4;
            root.Add(_mergeButton);

            // ステータスラベル
            _statusLabel = new Label();
            _statusLabel.style.color    = new Color(0.6f, 0.6f, 0.6f);
            _statusLabel.style.fontSize = 10;
            root.Add(_statusLabel);
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh(IProjectView view)
        {
            if (view == null)
            {
                ShowWarning("プロジェクトがありません");
                return;
            }

            var model = view.CurrentModel;
            if (model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            HideWarning();

            // 選択メッシュビューを再構築
            _selectedMeshViews.Clear();
            var selIndices = model.SelectedDrawableIndices;

            if (selIndices != null)
            {
                var drawList = model.DrawableList;
                if (drawList != null)
                {
                    foreach (int idx in selIndices)
                    {
                        if (idx >= 0 && idx < drawList.Count)
                            _selectedMeshViews.Add(drawList[idx]);
                    }
                }
            }

            _selectionLabel.text = $"選択メッシュ: {_selectedMeshViews.Count}";

            // リストボックス更新
            _baseObjectList.itemsSource = _selectedMeshViews;
            _baseObjectList.Rebuild();

            // 選択インデックスのクランプ
            if (_selectedMeshViews.Count > 0)
            {
                _baseListIndex = Mathf.Clamp(_baseListIndex, 0, _selectedMeshViews.Count - 1);
                _baseObjectList.SetSelection(_baseListIndex);
            }
            else
            {
                _baseListIndex = 0;
            }

            UpdateMergeButtonState();
            _statusLabel.text = string.Empty;
        }

        private void OnBaseSelectionChanged()
        {
            _baseListIndex = _baseObjectList.selectedIndex >= 0 ? _baseObjectList.selectedIndex : 0;
            UpdateMergeButtonState();
        }

        private void UpdateMergeButtonState()
        {
            bool canMerge = _selectedMeshViews.Count >= 2 && _baseListIndex >= 0 && _baseListIndex < _selectedMeshViews.Count;
            _mergeButton.SetEnabled(canMerge);
            if (_selectedMeshViews.Count < 2)
                _mergeButton.tooltip = "2つ以上のメッシュを選択してください";
            else
                _mergeButton.tooltip = string.Empty;
        }

        // ================================================================
        // マージ実行
        // ================================================================

        private void OnMergeClicked()
        {
            if (_ctx == null) return;
            var view = _ctx.CurrentView;
            if (view == null) return;

            var model = view.CurrentModel;
            if (model == null) return;

            if (_selectedMeshViews.Count < 2)
            {
                _statusLabel.text = "2つ以上のメッシュを選択してください";
                _statusLabel.style.color = new Color(0.9f, 0.4f, 0.2f);
                return;
            }

            if (_baseListIndex < 0 || _baseListIndex >= _selectedMeshViews.Count)
            {
                _statusLabel.text = "基準オブジェクトを選択してください";
                _statusLabel.style.color = new Color(0.9f, 0.4f, 0.2f);
                return;
            }

            // MasterIndex 配列を構築
            var masterIndices = new int[_selectedMeshViews.Count];
            for (int i = 0; i < _selectedMeshViews.Count; i++)
                masterIndices[i] = _selectedMeshViews[i].MasterIndex;

            int baseMasterIndex = _selectedMeshViews[_baseListIndex].MasterIndex;

            var cmd = new MergeMeshesCommand(
                modelIndex:     view.CurrentModelIndex,
                masterIndices:  masterIndices,
                baseMasterIndex: baseMasterIndex,
                createNewMesh:  _createNewMeshToggle.value
            );

            _ctx.SendCommand(cmd);

            _statusLabel.text  = "マージを実行しました";
            _statusLabel.style.color = new Color(0.4f, 0.85f, 0.4f);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text    = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mergeButton?.SetEnabled(false);
        }

        private void HideWarning()
        {
            if (_warningLabel == null) return;
            _warningLabel.style.display = DisplayStyle.None;
        }
    }
}
