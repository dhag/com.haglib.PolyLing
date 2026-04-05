// LiteEditorWindow.cs
// PolyLing 軽量エディタ拡張 - メインウィンドウ
// Tools/PolyLing Lite から開く。UIToolkit ベース。
// PolyLingCore を持たず ProjectContext + PMX/MQO インポーターを直接使用する。
//
// Editor/Poly_Ling_Lite/Core/ に配置

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.View;

namespace Poly_Ling.Lite
{
    public class LiteEditorWindow : EditorWindow
    {
        // ================================================================
        // メニュー
        // ================================================================

        [MenuItem("Tools/PolyLing Lite")]
        public static void Open()
        {
            var win = GetWindow<LiteEditorWindow>("PolyLing Lite");
            win.minSize = new Vector2(540f, 400f);
            win.Show();
        }

        // ================================================================
        // フィールド
        // ================================================================

        private ProjectContext          _project;
        private PanelContext            _panelContext;
        private LiteLayoutRoot          _layoutRoot;
        private LiteCommandDispatcher   _dispatcher;

        // サブパネル
        private LiteImportSubPanel              _importSubPanel;
        private LiteHierarchyExportSubPanel     _hierarchyExportSubPanel;
        private LiteModelListSubPanel           _modelListSubPanel;
        private LiteMeshListSubPanel            _meshListSubPanel;

        // アクティブボタン管理
        private Button _activeBtn;
        private static readonly StyleColor ActiveBtnColor   = new StyleColor(new Color(0.25f, 0.45f, 0.65f));
        private static readonly StyleColor InactiveBtnColor = new StyleColor(StyleKeyword.Null);

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void CreateGUI()
        {
            _project     = new ProjectContext();
            _panelContext = new PanelContext(DispatchCommand);

            _layoutRoot = new LiteLayoutRoot();
            _layoutRoot.Build(rootVisualElement);

            BuildSubPanels();
            WireButtons();

            // 初期ペイン：インポート
            ShowImportPanel(LiteImportSubPanel.Mode.PMX);
        }

        private void OnDestroy()
        {
            if (_panelContext != null)
                _panelContext.OnViewChanged -= null; // イベント全解除は個別に
        }

        // ================================================================
        // サブパネル構築
        // ================================================================

        private void BuildSubPanels()
        {
            _dispatcher = new LiteCommandDispatcher(
                () => _project,
                NotifyPanels);

            // インポート
            _importSubPanel = new LiteImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;

            // ヒエラルキーエクスポート
            _hierarchyExportSubPanel = new LiteHierarchyExportSubPanel();
            _hierarchyExportSubPanel.Build(_layoutRoot.HierarchyExportSection);
            _hierarchyExportSubPanel.GetModel = () => _project?.CurrentModel;

            // モデルリスト
            _modelListSubPanel = new LiteModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            // メッシュリスト
            _meshListSubPanel = new LiteMeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);
        }

        // ================================================================
        // ボタン配線
        // ================================================================

        private void WireButtons()
        {
            _layoutRoot.BtnImport          .clicked += () => ShowImportPanel(LiteImportSubPanel.Mode.PMX);
            _layoutRoot.BtnImportMqo       .clicked += () => ShowImportPanel(LiteImportSubPanel.Mode.MQO);
            _layoutRoot.BtnHierarchyExport .clicked += ShowHierarchyExportPanel;
            _layoutRoot.BtnModelList       .clicked += ShowModelListPanel;
            _layoutRoot.BtnMeshList        .clicked += ShowMeshListPanel;
        }

        // ================================================================
        // パネル表示切替ヘルパー
        // ================================================================

        private void HideAllRightPanels()
        {
            if (_layoutRoot == null) return;
            void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }
            Hide(_layoutRoot.ImportSection);
            Hide(_layoutRoot.HierarchyExportSection);
            Hide(_layoutRoot.ModelListSection);
            Hide(_layoutRoot.MeshListSection);
        }

        private void SetActiveButton(Button btn)
        {
            if (_activeBtn != null) _activeBtn.style.backgroundColor = InactiveBtnColor;
            _activeBtn = btn;
            if (_activeBtn != null) _activeBtn.style.backgroundColor = ActiveBtnColor;
        }

        private void ShowImportPanel(LiteImportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            var btn = mode == LiteImportSubPanel.Mode.PMX
                ? _layoutRoot.BtnImport
                : _layoutRoot.BtnImportMqo;
            SetActiveButton(btn);
            if (_layoutRoot.ImportSection != null)
                _layoutRoot.ImportSection.style.display = DisplayStyle.Flex;
            _importSubPanel?.SetMode(mode);
        }

        private void ShowHierarchyExportPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot.BtnHierarchyExport);
            if (_layoutRoot.HierarchyExportSection != null)
                _layoutRoot.HierarchyExportSection.style.display = DisplayStyle.Flex;
            _hierarchyExportSubPanel?.Refresh();
        }

        private void ShowModelListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot.BtnModelList);
            if (_layoutRoot.ModelListSection != null)
                _layoutRoot.ModelListSection.style.display = DisplayStyle.Flex;
        }

        private void ShowMeshListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot.BtnMeshList);
            if (_layoutRoot.MeshListSection != null)
                _layoutRoot.MeshListSection.style.display = DisplayStyle.Flex;
        }

        // ================================================================
        // インポートコールバック
        // ================================================================

        private void OnImportPmx(string filePath, PMXImportSettings settings)
        {
            _importSubPanel?.SetStatus("読み込み中...");
            try
            {
                var result = PMXImporter.ImportFile(filePath, settings);
                if (!result.Success)
                {
                    _importSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                    return;
                }

                // ImportPmxCommand と同様に ModelContext を構築する
                var model = new ModelContext
                {
                    Name               = Path.GetFileNameWithoutExtension(filePath),
                    FilePath           = filePath,
                    SourceDocument     = result.Document,
                    BoneWorldPositions = result.BoneWorldPositions,
                };
                if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                    model.MaterialReferences = result.MaterialReferences;
                foreach (var mc in result.MeshContexts)
                    model.Add(mc);
                foreach (var morph in result.MorphExpressions)
                    model.MorphExpressions.Add(morph);
                foreach (var pair in result.MirrorPairs)
                    model.MirrorPairs.Add(pair);
                model.ComputeWorldMatrices();

                LoadModel(filePath, model);
                _importSubPanel?.SetStatus($"完了: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _importSubPanel?.SetStatus($"例外: {ex.Message}");
                Debug.LogError($"[LiteEditorWindow] PMX import exception: {ex}");
            }
        }

        private void OnImportMqo(string filePath, MQOImportSettings settings)
        {
            _importSubPanel?.SetStatus("読み込み中...");
            try
            {
                var result = MQOImporter.ImportFile(filePath, settings);
                if (!result.Success)
                {
                    _importSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                    return;
                }

                // ImportMqoCommand と同様に ModelContext を構築する
                var model = new ModelContext
                {
                    Name           = Path.GetFileNameWithoutExtension(filePath),
                    FilePath       = filePath,
                    SourceDocument = result.Document,
                };
                if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                    model.MaterialReferences = result.MaterialReferences;
                foreach (var mc in result.MeshContexts)
                    model.Add(mc);
                foreach (var mc in result.BoneMeshContexts)
                    model.Add(mc);
                foreach (var pair in result.MirrorPairs)
                    model.MirrorPairs.Add(pair);
                model.ComputeWorldMatrices();

                LoadModel(filePath, model);
                _importSubPanel?.SetStatus($"完了: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _importSubPanel?.SetStatus($"例外: {ex.Message}");
                Debug.LogError($"[LiteEditorWindow] MQO import exception: {ex}");
            }
        }

        // ================================================================
        // モデルロード共通処理
        // ================================================================

        private void LoadModel(string filePath, ModelContext model)
        {
            if (model == null) return;
            if (string.IsNullOrEmpty(model.Name))
                model.Name = Path.GetFileNameWithoutExtension(filePath);

            // 既存モデルを置き換え（軽量版は1モデル専有）
            _project.Clear();
            _project.AddModel(model);
            _project.SelectModel(0);
            model.ComputeWorldMatrices();

            NotifyPanels(ChangeKind.ModelSwitch);
            _layoutRoot?.SetStatus($"ロード済み: {model.Name}  ({model.Count} メッシュ)");
        }

        // ================================================================
        // コマンドディスパッチ
        // ================================================================

        private void DispatchCommand(PanelCommand cmd)
        {
            _dispatcher?.Dispatch(cmd);
        }

        private void NotifyPanels(ChangeKind kind)
        {
            if (_project == null || _panelContext == null) return;
            var view = new LiveProjectView(_project);
            _panelContext.Notify(view, kind);
        }
    }
}
