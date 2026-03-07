// VertexToolsPanelV2.cs
// 選択頂点の編集パネル（V2）
// 外殻: EditorWindow / UIToolkit
// サブツールUI: IMGUIContainer（IEditTool.DrawSettingsUI() はそのまま使用）
// コンテキスト: PanelContext（選択情報表示）+ ToolContext（サブツール操作）

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class VertexToolsPanelV2 : EditorWindow
    {
        // ================================================================
        // ツール登録
        // ================================================================

        private struct ToolEntry
        {
            public string SectionLabel;
            public Func<IEditTool> Factory;
            public bool NeedsUpdate;
        }

        private static readonly ToolEntry[] ToolEntries =
        {
            new ToolEntry { SectionLabel = "Align Vertices / 頂点整列",         Factory = () => new AlignVerticesTool(),         NeedsUpdate = false },
            new ToolEntry { SectionLabel = "Merge Vertices / 頂点マージ",        Factory = () => new MergeVerticesTool(),         NeedsUpdate = true  },
            new ToolEntry { SectionLabel = "Planarize Along Bones / ボーン間平面化", Factory = () => new PlanarizeAlongBonesTool(), NeedsUpdate = false },
        };

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;
        private bool _isReceiving;

        // ================================================================
        // ツールインスタンス
        // ================================================================

        private List<IEditTool> _tools;
        private List<bool>      _expanded;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label _headerLabel;

        // ================================================================
        // ウィンドウ
        // ================================================================

        public static VertexToolsPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<VertexToolsPanelV2>();
            w.titleContent = new GUIContent("選択頂点の編集");
            w.minSize = new Vector2(280, 300);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;

            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;

            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged += OnViewChanged;
                if (_panelCtx.CurrentView != null)
                    OnViewChanged(_panelCtx.CurrentView, ChangeKind.Selection);
            }

            ActivateTools();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
            DeactivateTools();
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
        }

        // ================================================================
        // ツール管理
        // ================================================================

        private void EnsureTools()
        {
            if (_tools != null) return;
            _tools    = new List<IEditTool>();
            _expanded = new List<bool>();
            for (int i = 0; i < ToolEntries.Length; i++)
            {
                _tools.Add(ToolEntries[i].Factory());
                _expanded.Add(i == 0);
            }
        }

        private void ActivateTools()
        {
            EnsureTools();
            if (_toolCtx == null) return;
            foreach (var t in _tools) t.OnActivate(_toolCtx);
        }

        private void DeactivateTools()
        {
            if (_tools == null || _toolCtx == null) return;
            foreach (var t in _tools) t.OnDeactivate(_toolCtx);
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 4;
            root.style.paddingRight  = 4;
            root.style.paddingTop    = 4;
            root.style.paddingBottom = 4;

            // ---- 選択情報ヘッダー ----
            _headerLabel = new Label("選択頂点: —");
            _headerLabel.style.marginBottom   = 4;
            _headerLabel.style.fontSize       = 11;
            _headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_headerLabel);

            EnsureTools();

            // ---- ツールセクション ----
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            for (int i = 0; i < _tools.Count; i++)
            {
                var idx   = i;
                var entry = ToolEntries[i];
                var tool  = _tools[i];

                var foldout = new Foldout { text = entry.SectionLabel, value = _expanded[i] };
                foldout.RegisterValueChangedCallback(evt => _expanded[idx] = evt.newValue);

                // IMGUIContainer でサブツール UI を埋め込む
                var container = new IMGUIContainer(() =>
                {
                    if (_toolCtx == null)
                    {
                        EditorGUILayout.HelpBox("ToolContext not set.", MessageType.Warning);
                        return;
                    }
                    if (entry.NeedsUpdate && tool is MergeVerticesTool mt)
                        mt.Update(_toolCtx);

                    tool.DrawSettingsUI();
                });
                foldout.Add(container);
                scroll.Add(foldout);
            }

            UpdateHeader();
        }

        // ================================================================
        // ヘッダー更新
        // ================================================================

        private void UpdateHeader()
        {
            if (_headerLabel == null) return;

            var model = _panelCtx?.CurrentView?.CurrentModel;
            if (model == null || _toolCtx == null)
            {
                _headerLabel.text = "選択頂点: —";
                return;
            }

            int[] selDrawable = model.SelectedDrawableIndices;
            if (selDrawable == null || selDrawable.Length == 0)
            {
                _headerLabel.text = "選択頂点: (メッシュ未選択)";
                return;
            }

            // 選択メッシュの先頭の頂点選択数を表示
            int first = selDrawable[0];
            var mv = model.DrawableList?[first];
            int selV   = mv?.SelectedVertexCount ?? 0;
            int totalV = mv?.VertexCount ?? 0;
            _headerLabel.text = $"選択頂点: {selV} / {totalV}";
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                    UpdateHeader();
            }
            finally
            {
                EditorApplication.delayCall += () => _isReceiving = false;
            }
        }
    }
}
