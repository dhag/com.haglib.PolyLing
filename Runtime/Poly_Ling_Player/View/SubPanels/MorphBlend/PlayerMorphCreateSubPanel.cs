// PlayerMorphCreateSubPanel.cs
// モーフ作成パネル
//   作成方向: 基準モデルとモーフモデルを選択し、差分から頂点モーフを生成して基準モデルに登録する
//   逆方向:   MorphExpression を選択し、モーフメッシュからモデルを復元してプロジェクトに追加する
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public class PlayerMorphCreateSubPanel
    {
        // ================================================================
        // 外部依存
        // ================================================================

        /// <summary>プロジェクトの窓口（操作経路統一計画.md E）。</summary>
        public Func<Poly_Ling.View.IProjectView> GetProject;

        /// <summary>パネル再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>PanelCommand を送信するコールバック。</summary>
        public Action<PanelCommand> SendCommand;

        // ================================================================
        // 差分閾値
        // ================================================================

        private const float DiffThreshold = 0.0001f;

        // ================================================================
        // UI 要素
        // ================================================================

        // UI 自動操作の ID は "morphCreate.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("baseModel", Description = "基準モデル")]
        private DropdownField _baseModelDropdown;
        [UiControl("morphModel", Description = "モーフモデル")]
        private DropdownField _morphModelDropdown;
        [UiControl("morphName", Description = "作るモーフの名前")]
        private TextField     _morphNameField;
        [UiControl("panel", Description = "作るモーフのパネル（眉 / 目 / 口 / その他）")]
        private DropdownField _panelDropdown;
        [UiControl("createStatus", Safety = UiSafety.ReadOnly, Description = "モーフ作成の結果")]
        private Label         _createStatus;
        [UiControl("create", Safety = UiSafety.SafeWrite, Description = "基準モデルとモーフモデルからモーフを作成する")]
        private Button        _btnCreate;

        [UiControl("expressions", Description = "モデルに展開するモーフ（一覧の行番号）")]
        private ListView      _expressionList;
        [UiControl("expandStatus", Safety = UiSafety.ReadOnly, Description = "モデルへの展開の結果")]
        private Label         _expandStatus;
        [UiControl("expand", Safety = UiSafety.SafeWrite, Description = "選択したモーフをモデルに展開する")]
        private Button        _btnExpand;

        private readonly List<(int modelIndex, string label)> _modelChoices
            = new List<(int, string)>();
        private readonly List<(int exprIndex, string label)>  _exprChoices
            = new List<(int, string)>();

        private int _selectedExprIndex = -1;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            // ── 作成方向 ──────────────────────────────────────────────
            root.Add(SectionLabel("▼ モデル → モーフ 作成"));

            _baseModelDropdown  = new DropdownField("基準モデル",  new List<string>(), 0);
            _baseModelDropdown.style.color = new StyleColor(Color.white);
            _morphModelDropdown = new DropdownField("モーフモデル", new List<string>(), 0);
            _morphModelDropdown.style.color = new StyleColor(Color.white);
            root.Add(_baseModelDropdown);
            root.Add(_morphModelDropdown);

            _morphNameField = new TextField("モーフ名") { value = "NewMorph" };
            root.Add(_morphNameField);

            _panelDropdown = new DropdownField("パネル",
                new List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }, 3);
            _panelDropdown.style.color = new StyleColor(Color.white);
            root.Add(_panelDropdown);

            var btnCreate = new Button(OnCreateMorph) { text = "モーフ作成" };
            btnCreate.style.marginTop = 4;
            root.Add(btnCreate);
            _btnCreate = btnCreate;

            _createStatus = new Label();
            _createStatus.style.fontSize   = 10;
            _createStatus.style.color      = new StyleColor(Color.white);
            _createStatus.style.marginTop  = 2;
            _createStatus.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_createStatus);

            // ── 逆方向 ────────────────────────────────────────────────
            root.Add(Separator());
            root.Add(SectionLabel("▼ モーフ → モデル 展開"));

            _expressionList = new ListView(_exprChoices, 20, ExprMakeItem, ExprBindItem);
            _expressionList.style.height      = 360;
            _expressionList.style.marginBottom = 4;
            _expressionList.selectionChanged += OnExprSelectionChanged;
            root.Add(_expressionList);

            var btnExpand = new Button(OnExpandToModel) { text = "選択したモーフをモデルに展開" };
            root.Add(btnExpand);
            _btnExpand = btnExpand;

            _expandStatus = new Label();
            _expandStatus.style.fontSize   = 10;
            _expandStatus.style.color      = new StyleColor(Color.white);
            _expandStatus.style.marginTop  = 2;
            _expandStatus.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_expandStatus);
        }

        // ================================================================
        // Refresh（外部から呼ぶ）
        // ================================================================

        public void Refresh()
        {
            RefreshModelDropdowns();
            RefreshExpressionList();
        }

        // ================================================================
        // モデルドロップダウン更新
        // ================================================================

        private void RefreshModelDropdowns()
        {
            var project = GetProject?.Invoke();
            _modelChoices.Clear();

            var labels = new List<string>();

            if (project != null)
            {
                for (int i = 0; i < project.ModelCount; i++)
                {
                    string name = project.GetModelView(i)?.Name ?? $"Model{i}";
                    string label = $"[{i}]　{name}";
                    _modelChoices.Add((i, label));
                    labels.Add(label);
                }
            }

            _baseModelDropdown.choices  = labels;
            _morphModelDropdown.choices = labels;

            if (labels.Count > 0)
            {
                if (_baseModelDropdown.index < 0 || _baseModelDropdown.index >= labels.Count)
                    _baseModelDropdown.index = 0;
                if (_morphModelDropdown.index < 0 || _morphModelDropdown.index >= labels.Count)
                    _morphModelDropdown.index = labels.Count > 1 ? 1 : 0;
            }
        }

        // ================================================================
        // MorphExpression リスト更新
        // ================================================================

        private void RefreshExpressionList()
        {
            _exprChoices.Clear();
            _selectedExprIndex = -1;

            var project = GetProject?.Invoke();
            var model   = project?.CurrentModel;
            if (model == null)
            {
                _expressionList.Rebuild();
                return;
            }

            for (int i = 0; i < model.MorphExpressions.Count; i++)
            {
                var expr  = model.MorphExpressions[i];
                string lbl = $"[{i}] {expr.Name}  ({expr.MeshCount}mesh)";
                _exprChoices.Add((i, lbl));
            }

            _expressionList.Rebuild();
        }

        // ================================================================
        // 作成方向
        // ================================================================

        private void OnCreateMorph()
        {
            _createStatus.text = "";

            var project = GetProject?.Invoke();
            if (project == null) { SetCreateStatus("プロジェクトがありません", true); return; }

            int baseIdx  = _baseModelDropdown.index;
            int morphIdx = _morphModelDropdown.index;

            if (baseIdx < 0 || baseIdx >= project.ModelCount)
            { SetCreateStatus("基準モデルを選択してください", true); return; }
            if (morphIdx < 0 || morphIdx >= project.ModelCount)
            { SetCreateStatus("モーフモデルを選択してください", true); return; }
            if (baseIdx == morphIdx)
            { SetCreateStatus("基準モデルとモーフモデルが同じです", true); return; }

            var baseModel  = project.GetModelView(baseIdx);
            var morphModel = project.GetModelView(morphIdx);
            if (baseModel.TotalMeshCount != morphModel.TotalMeshCount)
            {
                SetCreateStatus($"メッシュ数が一致しません (基準:{baseModel.TotalMeshCount} / モーフ:{morphModel.TotalMeshCount})", true);
                return;
            }

            string morphName = _morphNameField.value.Trim();
            if (string.IsNullOrEmpty(morphName)) morphName = "NewMorph";
            int panel = _panelDropdown.index;

            // 作成はコマンドだけで行う（ディスパッチャ側で Undo 記録）。本体も SendCommand を渡すので
            // パネル内で直接作る経路は持たない（操作経路統一計画.md J）。
            if (SendCommand == null) return;
            SendCommand.Invoke(new CreateMorphFromDiffCommand(baseIdx, morphIdx, morphName, panel));
            SetCreateStatus("モーフ作成コマンドを送信しました", false);
            RefreshExpressionList();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 逆方向
        // ================================================================

        private void OnExpandToModel()
        {
            _expandStatus.text = "";

            var view = GetProject?.Invoke();
            if (view == null) { SetExpandStatus("プロジェクトがありません", true); return; }
            var baseModel = view.CurrentModel;
            if (baseModel == null) { SetExpandStatus("カレントモデルがありません", true); return; }
            if (_selectedExprIndex < 0 || _selectedExprIndex >= baseModel.MorphExpressions.Count)
            { SetExpandStatus("MorphExpression を選択してください", true); return; }

            // 展開（新規モデルの追加）はコマンドで行う（操作経路統一計画.md E・J）。
            int before = view.ModelCount;
            string exprName = baseModel.MorphExpressions[_selectedExprIndex].Name;
            SendCommand?.Invoke(new ExpandMorphExpressionToModelCommand(view.CurrentModelIndex, _selectedExprIndex));
            int after = GetProject?.Invoke()?.ModelCount ?? before;

            if (after > before)
                SetExpandStatus($"完了: 新規モデル \"{exprName}_expanded\" に展開しました", false);
            else
                SetExpandStatus("展開できるモーフメッシュがありませんでした", true);

            RefreshExpressionList();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // イベントハンドラ
        // ================================================================

        private void OnExprSelectionChanged(IEnumerable<object> _)
        {
            var sel = _expressionList.selectedIndex;
            _selectedExprIndex = (sel >= 0 && sel < _exprChoices.Count)
                ? _exprChoices[sel].exprIndex
                : -1;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SetCreateStatus(string msg, bool isError)
        {
            _createStatus.text  = msg;
            _createStatus.style.color = new StyleColor(isError
                ? new Color(1f, 0.4f, 0.4f)
                : new Color(0.5f, 1f, 0.5f));
        }

        private void SetExpandStatus(string msg, bool isError)
        {
            _expandStatus.text  = msg;
            _expandStatus.style.color = new StyleColor(isError
                ? new Color(1f, 0.4f, 0.4f)
                : new Color(0.5f, 1f, 0.5f));
        }

        // ================================================================
        // ListView ファクトリ
        // ================================================================

        private static VisualElement ExprMakeItem()
        {
            var lbl = new Label();
            lbl.style.color = new StyleColor(Color.white);
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.paddingLeft    = 4;
            return lbl;
        }

        private void ExprBindItem(VisualElement elem, int idx)
        {
            if (elem is Label lbl)
                lbl.text = idx < _exprChoices.Count ? _exprChoices[idx].label : "";
        }

        // ================================================================
        // UI ユーティリティ
        // ================================================================

        private static Label SectionLabel(string text)
        {
            var lbl = new Label(text);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginTop    = 6;
            lbl.style.marginBottom = 3;
            lbl.style.color        = new StyleColor(Color.white);
            return lbl;
        }

        private static VisualElement Separator()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.marginTop       = 8;
            sep.style.marginBottom    = 4;
            sep.style.backgroundColor = new StyleColor(Color.white);
            return sep;
        }
    }
}
