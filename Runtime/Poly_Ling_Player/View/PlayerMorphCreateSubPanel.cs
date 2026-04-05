// PlayerMorphCreateSubPanel.cs
// モーフ作成パネル
//   作成方向: 基準モデルとモーフモデルを選択し、差分から頂点モーフを生成して基準モデルに登録する
//   逆方向:   MorphExpression を選択し、モーフメッシュからモデルを復元してプロジェクトに追加する
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMorphCreateSubPanel
    {
        // ================================================================
        // 外部依存
        // ================================================================

        /// <summary>現在のプロジェクトを返すデリゲート。</summary>
        public Func<ProjectContext> GetProject;

        /// <summary>パネル再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>モデルリスト再構築要求（プロジェクトにモデルを追加した後に呼ぶ）。</summary>
        public Action OnRebuildModelList;

        // ================================================================
        // 差分閾値
        // ================================================================

        private const float DiffThreshold = 0.0001f;

        // ================================================================
        // UI 要素
        // ================================================================

        private DropdownField _baseModelDropdown;
        private DropdownField _morphModelDropdown;
        private TextField     _morphNameField;
        private DropdownField _panelDropdown;
        private Label         _createStatus;

        private ListView      _expressionList;
        private Label         _expandStatus;

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
            _morphNameField.style.color = new StyleColor(Color.black);
            root.Add(_morphNameField);

            _panelDropdown = new DropdownField("パネル",
                new List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }, 3);
            _panelDropdown.style.color = new StyleColor(Color.white);
            root.Add(_panelDropdown);

            var btnCreate = new Button(OnCreateMorph) { text = "モーフ作成" };
            btnCreate.style.marginTop = 4;
            root.Add(btnCreate);

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
                    string name = project.Models[i]?.Name ?? $"Model{i}";
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

            // ── モデル取得 ────────────────────────────────────────────
            int baseIdx  = _baseModelDropdown.index;
            int morphIdx = _morphModelDropdown.index;

            if (baseIdx < 0 || baseIdx >= project.ModelCount)
            { SetCreateStatus("基準モデルを選択してください", true); return; }
            if (morphIdx < 0 || morphIdx >= project.ModelCount)
            { SetCreateStatus("モーフモデルを選択してください", true); return; }
            if (baseIdx == morphIdx)
            { SetCreateStatus("基準モデルとモーフモデルが同じです", true); return; }

            var baseModel  = project.Models[baseIdx];
            var morphModel = project.Models[morphIdx];

            if (baseModel.Count != morphModel.Count)
            {
                SetCreateStatus(
                    $"メッシュ数が一致しません (基準:{baseModel.Count} / モーフ:{morphModel.Count})",
                    true);
                return;
            }

            string morphName = _morphNameField.value.Trim();
            if (string.IsNullOrEmpty(morphName)) morphName = "NewMorph";

            int panel = _panelDropdown.index; // 0=眉 1=目 2=口 3=その他

            // ── メッシュ走査 → 差分のあるメッシュのみモーフ生成 ───────
            var expression    = new MorphExpression(morphName, MorphType.Vertex) { Panel = panel };
            int morphCreated  = 0;
            int meshSkipped   = 0;

            for (int mi = 0; mi < baseModel.Count; mi++)
            {
                var baseCtx  = baseModel.GetMeshContext(mi);
                var morphCtx = morphModel.GetMeshContext(mi);

                // Drawable メッシュのみ対象（ボーン・モーフ等はスキップ）
                if (baseCtx == null || morphCtx == null) continue;
                if (baseCtx.MeshObject  == null || morphCtx.MeshObject  == null) continue;
                if (baseCtx.Type  != MeshType.Mesh && baseCtx.Type  != MeshType.BakedMirror) continue;
                if (baseCtx.MeshObject.VertexCount != morphCtx.MeshObject.VertexCount) continue;

                // 差分チェック
                if (!HasDiff(baseCtx.MeshObject, morphCtx.MeshObject))
                { meshSkipped++; continue; }

                // ── MirrorPair.Mirror 側は Real 側から生成するためスキップ ──
                if (baseModel.IsMirrorSide(baseCtx)) continue;

                // Real 側モーフ生成
                int newMorphIdx = CreateMorphMeshContext(
                    baseModel, baseCtx, mi,
                    morphCtx.MeshObject,
                    morphName, panel,
                    expression);
                morphCreated++;

                // Mirror 側モーフ生成
                var pair = baseModel.GetMirrorPair(baseCtx);
                if (pair != null && pair.Real == baseCtx && pair.Mirror != null)
                {
                    int mirrorParentIdx = baseModel.MeshContextList.IndexOf(pair.Mirror);
                    if (mirrorParentIdx >= 0)
                        CreateMirrorMorphMeshContext(
                            baseModel, pair, mirrorParentIdx,
                            baseCtx.MeshObject, morphCtx.MeshObject,
                            morphName, panel,
                            expression);
                }
            }

            if (morphCreated == 0)
            {
                SetCreateStatus($"差分のあるメッシュがありませんでした（{meshSkipped}メッシュ確認済み）", true);
                return;
            }

            baseModel.MorphExpressions.Add(expression);

            SetCreateStatus(
                $"完了: {morphCreated}メッシュのモーフを作成しました（{meshSkipped}メッシュはスキップ）",
                false);

            RefreshExpressionList();
            OnRepaint?.Invoke();
        }

        // ----------------------------------------------------------------
        // Real 側モーフ MeshContext を生成して baseModel に追加し expression に登録
        // ----------------------------------------------------------------

        private int CreateMorphMeshContext(
            ModelContext baseModel,
            MeshContext  baseCtx,
            int          parentIdx,
            MeshObject   morphMeshObj,
            string       morphName,
            int          panel,
            MorphExpression expression)
        {
            // 基準 MeshObject をクローンしてモーフ MeshContext を作る
            var morphObj = baseCtx.MeshObject.Clone();
            morphObj.Type = MeshType.Morph;

            // モーフ後位置をコピー
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
                morphObj.Vertices[vi].Position = morphMeshObj.Vertices[vi].Position;

            var newCtx = new MeshContext
            {
                Name      = morphName,
                MeshObject = morphObj,
                IsVisible = false,
            };

            // SetAsMorph: baseCtx.MeshObject を基準位置として MorphBaseData を構築
            newCtx.SetAsMorph(morphName, baseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = parentIdx;

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
            return newIdx;
        }

        // ----------------------------------------------------------------
        // Mirror 側モーフ MeshContext を生成して baseModel に追加し expression に登録
        // ----------------------------------------------------------------

        private void CreateMirrorMorphMeshContext(
            ModelContext    baseModel,
            MirrorPair      pair,
            int             mirrorParentIdx,
            MeshObject      realBaseMeshObj,
            MeshObject      realMorphMeshObj,
            string          morphName,
            int             panel,
            MorphExpression expression)
        {
            var mirrorBaseCtx = pair.Mirror;
            if (mirrorBaseCtx?.MeshObject == null) return;

            var morphObj = mirrorBaseCtx.MeshObject.Clone();
            morphObj.Type = MeshType.Morph;

            // Real 側の差分を Mirror 変換して Mirror 側の基準位置に加算
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
            {
                int ri = pair.VertexMap != null && vi < pair.VertexMap.Length
                    ? pair.VertexMap[vi]
                    : vi;

                if (ri < 0 || ri >= realBaseMeshObj.VertexCount) continue;

                Vector3 realDiff   = realMorphMeshObj.Vertices[ri].Position
                                   - realBaseMeshObj.Vertices[ri].Position;
                Vector3 mirrorDiff = pair.MirrorDirection(realDiff);

                morphObj.Vertices[vi].Position =
                    mirrorBaseCtx.MeshObject.Vertices[vi].Position + mirrorDiff;
            }

            var newCtx = new MeshContext
            {
                Name       = morphName,
                MeshObject = morphObj,
                IsVisible  = false,
            };

            newCtx.SetAsMorph(morphName, mirrorBaseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = mirrorParentIdx;

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
        }

        // ================================================================
        // 逆方向
        // ================================================================

        private void OnExpandToModel()
        {
            _expandStatus.text = "";

            var project = GetProject?.Invoke();
            if (project == null) { SetExpandStatus("プロジェクトがありません", true); return; }

            var baseModel = project.CurrentModel;
            if (baseModel == null) { SetExpandStatus("カレントモデルがありません", true); return; }

            if (_selectedExprIndex < 0 || _selectedExprIndex >= baseModel.MorphExpressions.Count)
            { SetExpandStatus("MorphExpression を選択してください", true); return; }

            var expr = baseModel.MorphExpressions[_selectedExprIndex];

            // ── 新規モデルを生成 ─────────────────────────────────────
            var newModel = new ModelContext { Name = expr.Name + "_expanded" };

            int expandedCount = 0;

            foreach (var entry in expr.MeshEntries)
            {
                int meshIdx = entry.MeshIndex;
                if (meshIdx < 0 || meshIdx >= baseModel.Count) continue;

                var morphCtx = baseModel.GetMeshContext(meshIdx);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                // モーフ後位置を持つクローンを新モデルに追加
                var expandedObj = morphCtx.MeshObject.Clone();
                expandedObj.Type = MeshType.Mesh;

                var expandedCtx = new MeshContext
                {
                    Name       = morphCtx.Name,
                    MeshObject = expandedObj,
                    IsVisible  = true,
                };

                newModel.Add(expandedCtx);
                expandedCount++;
            }

            if (expandedCount == 0)
            { SetExpandStatus("展開できるモーフメッシュがありませんでした", true); return; }

            project.AddModel(newModel);
            OnRebuildModelList?.Invoke();

            SetExpandStatus($"完了: {expandedCount}メッシュを新規モデル \"{newModel.Name}\" に展開しました", false);

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

        /// <summary>2つの MeshObject 間に差分があるか（閾値以上の移動頂点が1つでもあれば true）。</summary>
        private static bool HasDiff(MeshObject baseMesh, MeshObject morphMesh)
        {
            float thSq = DiffThreshold * DiffThreshold;
            int count = Mathf.Min(baseMesh.VertexCount, morphMesh.VertexCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 d = morphMesh.Vertices[i].Position - baseMesh.Vertices[i].Position;
                if (d.sqrMagnitude > thSq) return true;
            }
            return false;
        }

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
