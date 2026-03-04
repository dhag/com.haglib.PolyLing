// TypedMeshListPanel.Morph.cs
// Morph - MorphEditor, preview, conversion

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public partial class TypedMeshListPanel
    {
        // モーフエディタUI（Phase MorphEditor v2: UIToolkit ListView）
        private Label _morphCountLabel, _morphStatusLabel;
        private ListView _morphListView;
        private Slider _morphTestWeight;

        // 変換セクション
        private VisualElement _morphSourceMeshPopupContainer;
        private VisualElement _morphParentPopupContainer;
        private VisualElement _morphPanelPopupContainer;
        private TextField _morphNameField;
        private Button _btnMeshToMorph, _btnMorphToMesh;
        private PopupField<int> _morphSourceMeshPopup;
        private PopupField<int> _morphParentPopup;
        private PopupField<int> _morphPanelPopup;

        // モーフセット
        private TextField _morphSetNameField;
        private VisualElement _morphSetTypePopupContainer;
        private PopupField<int> _morphSetTypePopup;
        private Button _btnCreateMorphSet;


        private bool _isMorphPreviewActive = false;
        private Dictionary<int, Vector3[]> _morphPreviewBackups = new Dictionary<int, Vector3[]>();


        // ================================================================
        // モーフエディタ（Phase MorphEditor v2: UIToolkit ListView）
        // ================================================================

        /// <summary>
        /// モーフエディタのUI要素をバインド
        /// </summary>
        private void BindMorphEditorUI(VisualElement root)
        {
            _morphCountLabel = root.Q<Label>("morph-count-label");
            _morphStatusLabel = root.Q<Label>("morph-status-label");
            _morphNameField = root.Q<TextField>("morph-name-field");
            _morphTestWeight = root.Q<Slider>("morph-test-weight");

            // モーフ ListView
            _morphListView = root.Q<ListView>("morph-listview");
            if (_morphListView != null)
            {
                _morphListView.makeItem = MorphListMakeItem;
                _morphListView.bindItem = MorphListBindItem;
                _morphListView.fixedItemHeight = 20;
                _morphListView.itemsSource = _morphListData;
                _morphListView.selectionType = SelectionType.Multiple;
                _morphListView.selectionChanged += OnMorphListSelectionChanged;
            }

            // PopupFieldコンテナ
            _morphSourceMeshPopupContainer = root.Q<VisualElement>("morph-source-mesh-container");
            _morphParentPopupContainer = root.Q<VisualElement>("morph-parent-container");
            _morphPanelPopupContainer = root.Q<VisualElement>("morph-panel-container");

            // モーフセット
            _morphSetNameField = root.Q<TextField>("morph-set-name-field");
            _morphSetTypePopupContainer = root.Q<VisualElement>("morph-set-type-container");
            _btnCreateMorphSet = root.Q<Button>("btn-create-morph-set");

            // ボタンイベント
            _btnMeshToMorph = root.Q<Button>("btn-mesh-to-morph");
            _btnMorphToMesh = root.Q<Button>("btn-morph-to-mesh");

            _btnMeshToMorph?.RegisterCallback<ClickEvent>(_ => OnMeshToMorph());
            _btnMorphToMesh?.RegisterCallback<ClickEvent>(_ => OnMorphToMesh());
            _btnCreateMorphSet?.RegisterCallback<ClickEvent>(_ => OnCreateMorphSet());

            root.Q<Button>("btn-morph-test-reset")?.RegisterCallback<ClickEvent>(_ => OnMorphTestReset());
            root.Q<Button>("btn-morph-test-select-all")?.RegisterCallback<ClickEvent>(_ => OnMorphTestSelectAll(true));
            root.Q<Button>("btn-morph-test-deselect-all")?.RegisterCallback<ClickEvent>(_ => OnMorphTestSelectAll(false));

            // ウェイトスライダー
            _morphTestWeight?.RegisterValueChangedCallback(OnMorphTestWeightChanged);

            // 初期データ投入（display:none状態でもitemsSourceにデータを入れておく）
            RefreshMorphListData();
        }

        // ----------------------------------------------------------------
        // ListView makeItem / bindItem
        // ----------------------------------------------------------------

        private VisualElement MorphListMakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("morph-list-row");

            var nameLabel = new Label();
            nameLabel.AddToClassList("morph-list-name");
            row.Add(nameLabel);

            var infoLabel = new Label();
            infoLabel.AddToClassList("morph-list-info");
            row.Add(infoLabel);

            return row;
        }

        private void MorphListBindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _morphListData.Count) return;
            var data = _morphListData[index];

            var nameLabel = element.Q<Label>(className: "morph-list-name");
            var infoLabel = element.Q<Label>(className: "morph-list-info");

            if (nameLabel != null) nameLabel.text = data.name;
            if (infoLabel != null) infoLabel.text = data.info;
        }

        // ----------------------------------------------------------------
        // モーフエディタ更新
        // ----------------------------------------------------------------

        private void RefreshMorphEditor()
        {
            if (Model == null) return;

            RefreshMorphListData();
            RefreshMorphConvertSection();
            RefreshMorphSetSection();
        }

        /// <summary>
        /// モーフリストのデータソースを更新してListViewをリフレッシュ
        /// </summary>
        private void RefreshMorphListData()
        {
            _morphListData.Clear();

            if (Model != null)
            {
                var morphEntries = Model.TypedIndices.GetEntries(MeshCategory.Morph);
                foreach (var entry in morphEntries)
                {
                    var ctx = entry.Context;
                    string info = "";
                    if (ctx != null && ctx.MorphParentIndex >= 0)
                    {
                        var parentCtx = Model.GetMeshContext(ctx.MorphParentIndex);
                        info = parentCtx != null ? $"→{parentCtx.Name}" : $"→[{ctx.MorphParentIndex}]";
                    }
                    else if (ctx != null && !string.IsNullOrEmpty(ctx.MorphName))
                    {
                        info = ctx.MorphName;
                    }
                    _morphListData.Add((entry.MasterIndex, entry.Name, info));
                }
            }

            if (_morphCountLabel != null)
                _morphCountLabel.text = $"モーフ: {_morphListData.Count}";

            _morphListView?.RefreshItems();
            SyncMorphListViewSelection();
        }

        // ----------------------------------------------------------------
        // モーフリスト選択 (ListView selectionChanged)
        // ----------------------------------------------------------------

        private void OnMorphListSelectionChanged(IEnumerable<object> selection)
        {
            if (_isSyncingMorphSelection || Model == null) return;

            var oldIndices = Model.SelectedMorphIndices.ToArray();

            // ListView選択 → Model.SelectedMorphIndices
            Model.ClearMorphSelection();
            foreach (var item in selection)
            {
                if (item is (int masterIndex, string, string))
                    Model.AddToMorphSelection(masterIndex);
            }

            var newIndices = Model.SelectedMorphIndices.ToArray();

            // Undo記録（変化があった場合のみ）
            if (!oldIndices.SequenceEqual(newIndices))
            {
                var undoController = ToolCtx?.UndoController;
                if (undoController != null)
                {
                    var record = new MorphSelectionChangeRecord(oldIndices, newIndices);
                    undoController.MeshListStack.Record(record, "モーフ選択変更");
                    undoController.FocusMeshList();
                }
            }

            ToolCtx?.OnMeshSelectionChanged?.Invoke();
            ToolCtx?.Repaint?.Invoke();
        }

        /// <summary>
        /// Model.SelectedMorphIndices → ListView選択に同期（Undo/Redo・外部変更時用）
        /// </summary>
        private void SyncMorphListViewSelection()
        {
            if (_morphListView == null || Model == null) return;

            _isSyncingMorphSelection = true;
            try
            {
                var selectedListIndices = new List<int>();
                var selectedMorphSet = new HashSet<int>(Model.SelectedMorphIndices);

                for (int i = 0; i < _morphListData.Count; i++)
                {
                    if (selectedMorphSet.Contains(_morphListData[i].masterIndex))
                        selectedListIndices.Add(i);
                }

                _morphListView.SetSelectionWithoutNotify(selectedListIndices);
            }
            finally
            {
                _isSyncingMorphSelection = false;
            }
        }

        // ----------------------------------------------------------------
        // 変換セクション
        // ----------------------------------------------------------------

        private void RefreshMorphConvertSection()
        {
            if (Model == null) return;

            RebuildPopup(ref _morphSourceMeshPopup, _morphSourceMeshPopupContainer, BuildDrawableMeshChoices(), "morph-popup");
            RebuildPopup(ref _morphParentPopup, _morphParentPopupContainer, BuildDrawableMeshChoices(), "morph-popup");
            RebuildPopup(ref _morphPanelPopup, _morphPanelPopupContainer,
                new List<(int, string)> { (0, "眉"), (1, "目"), (2, "口"), (3, "その他") }, "morph-popup", 3);
        }

        private List<(int index, string name)> BuildDrawableMeshChoices()
        {
            var choices = new List<(int, string)>();
            if (Model == null) return choices;
            foreach (var entry in Model.TypedIndices.GetEntries(MeshCategory.Drawable))
                choices.Add((entry.MasterIndex, $"[{entry.MasterIndex}] {entry.Name}"));
            return choices;
        }

        private void RebuildPopup(ref PopupField<int> popup, VisualElement container,
            List<(int index, string name)> options, string cssClass, int defaultValue = -1)
        {
            if (container == null) return;
            container.Clear();

            var indices = new List<int> { -1 };
            var displayMap = new Dictionary<int, string> { [-1] = "(なし)" };
            foreach (var (idx, name) in options)
            {
                indices.Add(idx);
                displayMap[idx] = name;
            }

            int initial = indices.Contains(defaultValue) ? defaultValue : -1;
            popup = new PopupField<int>(indices, initial,
                v => displayMap.TryGetValue(v, out var s) ? s : v.ToString(),
                v => displayMap.TryGetValue(v, out var s) ? s : v.ToString());
            popup.AddToClassList(cssClass);
            popup.style.flexGrow = 1;
            container.Add(popup);
        }

        // ----------------------------------------------------------------
        // メッシュ → モーフ 変換
        // ----------------------------------------------------------------

        private void OnMeshToMorph()
        {
            if (Model == null) return;

            int sourceIdx = _morphSourceMeshPopup?.value ?? -1;
            int parentIdx = _morphParentPopup?.value ?? -1;
            string morphName = _morphNameField?.value?.Trim() ?? "";
            int panel = _morphPanelPopup?.value ?? 3;

            if (sourceIdx < 0 || sourceIdx >= Model.MeshContextCount)
            { MorphLog("対象メッシュを選択してください"); return; }

            var ctx = Model.GetMeshContext(sourceIdx);
            if (ctx == null || ctx.MeshObject == null)
            { MorphLog("メッシュが無効です"); return; }

            if (ctx.IsMorph)
            { MorphLog("既にモーフです"); return; }

            if (string.IsNullOrEmpty(morphName)) morphName = ctx.Name;

            var record = new MorphConversionRecord
            {
                MasterIndex = sourceIdx,
                OldType = ctx.Type, NewType = MeshType.Morph,
                OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                OldMorphParentIndex = ctx.MorphParentIndex,
                OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
            };

            // 親メッシュのMeshObjectをBasePositionsの基準として渡す
            MeshObject baseMeshObject = null;
            if (parentIdx >= 0 && parentIdx < Model.MeshContextCount)
            {
                var parentCtx = Model.GetMeshContext(parentIdx);
                baseMeshObject = parentCtx?.MeshObject;
            }
            ctx.SetAsMorph(morphName, baseMeshObject);
            ctx.MorphPanel = panel;
            ctx.MorphParentIndex = parentIdx;
            ctx.Type = MeshType.Morph;

            ctx.IsVisible = false; // 非表示にしておく

            if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Morph;
            ctx.ExcludeFromExport = true;

            record.NewMorphBaseData = ctx.MorphBaseData?.Clone();
            record.NewMorphParentIndex = ctx.MorphParentIndex;
            record.NewName = ctx.Name;
            record.NewExcludeFromExport = ctx.ExcludeFromExport;

            RecordMorphUndo(record, "メッシュ→モーフ変換");

            // 変換元を選択リストから除去し、有効なDrawableを選択
            Model.RemoveFromSelectionByType(sourceIdx);
            Model.TypedIndices?.Invalidate();
            var drawables = Model.TypedIndices.GetEntries(MeshCategory.Drawable);
            if (drawables.Count > 0 && !Model.HasMeshSelection)
                Model.SelectDrawable(drawables[0].MasterIndex);

            NotifyModelChanged();
            RefreshMorphEditor();
            MorphLog($"'{ctx.Name}' をモーフに変換");
        }

        // ----------------------------------------------------------------
        // モーフ → メッシュ 変換
        // ----------------------------------------------------------------

        private void OnMorphToMesh()
        {
            if (Model == null) return;

            // 選択中のモーフを収集
            var targets = new List<int>();
            foreach (var morphIdx in Model.SelectedMorphIndices.ToList())
            {
                if (morphIdx < 0 || morphIdx >= Model.MeshContextCount) continue;
                var ctx = Model.GetMeshContext(morphIdx);
                if (ctx != null && (ctx.IsMorph || ctx.Type == MeshType.Morph))
                    targets.Add(morphIdx);
            }

            if (targets.Count == 0)
            { MorphLog("モーフが選択されていません"); return; }

            // モーフプレビュー終了
            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);

            var convertedNames = new List<string>();
            foreach (int targetIdx in targets)
            {
                var ctx = Model.GetMeshContext(targetIdx);
                if (ctx == null) continue;

                var record = new MorphConversionRecord
                {
                    MasterIndex = targetIdx,
                    OldType = ctx.Type, NewType = MeshType.Mesh,
                    OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                    OldMorphParentIndex = ctx.MorphParentIndex,
                    OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
                    NewMorphBaseData = null, NewMorphParentIndex = -1,
                    NewName = ctx.Name, NewExcludeFromExport = false,
                };

                ctx.ClearMorphData();
                ctx.Type = MeshType.Mesh;
                if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Mesh;
                ctx.ExcludeFromExport = false;

                RecordMorphUndo(record, $"モーフ→メッシュ: {ctx.Name}");
                convertedNames.Add(ctx.Name);
            }

            // 変換したメッシュを選択リストから除去
            foreach (int idx in targets)
                Model.RemoveFromSelectionByType(idx);

            Model.TypedIndices?.Invalidate();

            // Drawableが残っていれば先頭を選択
            var drawables = Model.TypedIndices.GetEntries(MeshCategory.Drawable);
            if (drawables.Count > 0 && !Model.HasMeshSelection)
                Model.SelectDrawable(drawables[0].MasterIndex);

            NotifyModelChanged();
            RefreshMorphEditor();
            MorphLog($"{convertedNames.Count}件をメッシュに戻した: {string.Join(", ", convertedNames)}");
        }

        // ----------------------------------------------------------------
        // 簡易モーフテスト
        // ----------------------------------------------------------------

        private void OnMorphTestWeightChanged(ChangeEvent<float> evt)
        {
            ApplyMorphTest(evt.newValue);
        }

        private void OnMorphTestReset()
        {
            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);
            ToolCtx?.SyncMesh?.Invoke();
            ToolCtx?.Repaint?.Invoke();
            MorphLog("モーフテストリセット");
        }

        private void OnMorphTestSelectAll(bool select)
        {
            if (Model == null) return;

            var oldIndices = Model.SelectedMorphIndices.ToArray();

            Model.ClearMorphSelection();
            if (select)
                foreach (var d in _morphListData)
                    Model.AddToMorphSelection(d.masterIndex);

            var newIndices = Model.SelectedMorphIndices.ToArray();

            // Undo記録
            if (!oldIndices.SequenceEqual(newIndices))
            {
                var undoController = ToolCtx?.UndoController;
                if (undoController != null)
                {
                    var record = new MorphSelectionChangeRecord(oldIndices, newIndices);
                    undoController.MeshListStack.Record(record, select ? "モーフ全選択" : "モーフ全解除");
                    undoController.FocusMeshList();
                }
            }

            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);
            SyncMorphListViewSelection();
        }

        private void ApplyMorphTest(float weight)
        {
            if (Model == null || Model.SelectedMorphIndices.Count == 0) return;

            if (!_isMorphPreviewActive) StartMorphPreview();

            // バックアップから復元
            foreach (var (baseIndex, backup) in _morphPreviewBackups)
            {
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            // オフセット適用
            foreach (var (morphIndex, baseIndex) in _morphTestChecked)
            {
                var morphCtx = Model.GetMeshContext(morphIndex);
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * weight;
            }

            foreach (var baseIndex in _morphPreviewBackups.Keys)
            {
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (baseCtx != null) ToolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
            }
            ToolCtx?.Repaint?.Invoke();
        }

        private void StartMorphPreview()
        {
            if (Model == null) return;
            EndMorphPreview();
            _morphTestChecked.Clear();
            _morphPreviewBackups.Clear();

            foreach (var morphIdx in Model.SelectedMorphIndices)
            {
                var morphCtx = Model.GetMeshContext(morphIdx);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIdx = morphCtx.MorphParentIndex;
                if (baseIdx < 0) baseIdx = FindBaseMeshByName(morphCtx);
                if (baseIdx < 0) continue;

                var baseCtx = Model.GetMeshContext(baseIdx);
                if (baseCtx?.MeshObject == null) continue;

                if (!_morphPreviewBackups.ContainsKey(baseIdx))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _morphPreviewBackups[baseIdx] = backup;
                }
                _morphTestChecked.Add((morphIdx, baseIdx));
            }
            _isMorphPreviewActive = true;
        }

        private void EndMorphPreview()
        {
            if (!_isMorphPreviewActive || _morphPreviewBackups.Count == 0)
            {
                _isMorphPreviewActive = false;
                _morphPreviewBackups.Clear();
                _morphTestChecked.Clear();
                return;
            }

            if (Model != null)
            {
                foreach (var (baseIndex, backup) in _morphPreviewBackups)
                {
                    var baseCtx = Model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    ToolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }
            _isMorphPreviewActive = false;
            _morphPreviewBackups.Clear();
            _morphTestChecked.Clear();
            ToolCtx?.Repaint?.Invoke();
        }

        private int FindBaseMeshByName(MeshContext morphCtx)
        {
            if (morphCtx == null || Model == null) return -1;
            string morphName = morphCtx.MorphName;
            string meshName = morphCtx.Name;
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < Model.MeshContextCount; i++)
                {
                    var ctx = Model.GetMeshContext(i);
                    if (ctx != null && (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror || ctx.Type == MeshType.MirrorSide) && ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }

        // ----------------------------------------------------------------
        // モーフセット（新規作成のみ、管理はMorphPanelで）
        // ----------------------------------------------------------------

        private void RefreshMorphSetSection()
        {
            if (Model == null) return;

            RebuildPopup(ref _morphSetTypePopup, _morphSetTypePopupContainer,
                new List<(int, string)> { ((int)MorphType.Vertex, "Vertex"), ((int)MorphType.UV, "UV") },
                "morph-popup", (int)MorphType.Vertex);
        }

        private void OnCreateMorphSet()
        {
            if (Model == null) return;

            string setName = _morphSetNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(setName))
                setName = Model.GenerateUniqueMorphExpressionName("MorphExpression");

            if (Model.FindMorphExpressionByName(setName) != null)
            { MorphLog($"セット名 '{setName}' は既に存在します"); return; }

            int typeInt = _morphSetTypePopup?.value ?? (int)MorphType.Vertex;
            var set = new MorphExpression(setName, (MorphType)typeInt);

            foreach (var morphIdx in Model.SelectedMorphIndices)
            {
                var morphCtx = Model.GetMeshContext(morphIdx);
                if (morphCtx != null && morphCtx.IsMorph)
                    set.AddMesh(morphIdx);
            }

            if (set.MeshCount == 0)
            { MorphLog("モーフが選択されていません"); return; }

            int addIndex = Model.MorphExpressions.Count;
            var record = new MorphExpressionChangeRecord
            {
                AddExpression = set.Clone(),
                AddedIndex = addIndex,
            };
            RecordMorphUndo(record, $"モーフセット生成: {setName}");

            Model.MorphExpressions.Add(set);
            NotifyModelChanged();
            MorphLog($"モーフセット '{setName}' を生成 ({set.MeshCount}件)");
        }

        // ----------------------------------------------------------------
        // Undo / ステータス
        // ----------------------------------------------------------------

        private void RecordMorphUndo(MeshListUndoRecord record, string description)
        {
            var undoController = ToolCtx?.UndoController;
            if (undoController == null) return;
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        private void MorphLog(string msg)
        {
            if (_morphStatusLabel != null) _morphStatusLabel.text = msg;
            Log(msg);
        }
    }

    /// <summary>
    /// D&Dバリデータ
    /// </summary>
}
