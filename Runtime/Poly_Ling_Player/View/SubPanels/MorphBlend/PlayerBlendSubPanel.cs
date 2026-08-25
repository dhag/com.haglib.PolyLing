// PlayerBlendSubPanel.cs
// メッシュブレンドサブパネル（Player ビルド用）。
//
// 宛先 1 件に対し、最大 6 件のソースを加重平均で混ぜる。
// ソースは別モデルのオブジェクトを指してよい。宛先はカレントモデル内に限る
// （ApplyBlendCommand.ModelIndex と書き込み先が食い違うと、Undo と
//  所有権判定の基準が二重になるため）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerBlendSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>ブレンド適用後に GPU バッファ更新と通知を行うコールバック。</summary>
        public Action<MeshContext> OnSyncMeshPositions;

        /// <summary>
        /// 法線を GPU へ送るコールバック。
        /// OnSyncMeshPositions は位置しか送らないため、プレビュー中に法線を
        /// 再計算しても、これを通さないと画面の陰影が確定結果と食い違う。
        /// </summary>
        public Action<MeshContext> OnSyncMeshNormals;

        /// <summary>トポロジー変更後の再構築コールバック（RebuildAdapter相当）。</summary>
        public Action OnNotifyTopologyChanged;

        /// <summary>
        /// MeshContext.IsVisible を書き換えた直後に呼ぶコールバック。
        ///
        /// 面は毎フレーム MeshContext を見て描画されるので可視の変更が即座に効くが、
        /// 頂点と辺は GPU 内部の描画フラグで決まる。そちらは専用の書き戻し経路を
        /// 通さないと更新されず、面だけ消えて頂点と辺が残る。
        /// </summary>
        public Action OnMeshVisibilityChanged;

        /// <summary>再描画要求コールバック。</summary>
        public Action OnRepaint;

        /// <summary>Undo記録のため UndoController を取得するコールバック。</summary>
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>Undo記録のため CommandQueue を取得するコールバック。</summary>
        public Func<Poly_Ling.Commands.CommandQueue> GetCommandQueue;

        /// <summary>モデル一覧を引くためのプロジェクトビュー取得コールバック。</summary>
        public Func<IProjectView> GetProjectView;

        /// <summary>ソースを別モデルから引くための ModelContext 取得コールバック。</summary>
        public Func<int, ModelContext> GetModelContext;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private const int MaxSources = ApplyBlendCommand.MaxSources;

        /// <summary>ソース1行分の選択状態。</summary>
        private struct SourceSlot
        {
            public int   ModelIndex;    // -1 = 未選択
            public int   MasterIndex;   // -1 = 未選択（ModelIndex のモデル内索引）
            public float Weight;
        }

        private ModelContext _model;

        private readonly SourceSlot[] _slots = new SourceSlot[MaxSources];

        private int  _destMasterIndex      = -1;
        private bool _createNewObject      = false;
        private bool _recalculateNormals   = true;
        private bool _selectedVerticesOnly = false;

        /// <summary>
        /// プレビュー中にソースを隠すか。
        /// true  … 隠す（面は消えるが頂点と辺は残る）
        /// false … 何も消さない
        /// </summary>
        private bool _hideSources          = true;
        private BlendMatchMode _matchMode  = BlendMatchMode.Index;

        private readonly BlendPreviewState _blendPreview = new BlendPreviewState();

        /// <summary>ドロップダウンの表示名 → 索引の対応（表示順）。</summary>
        private readonly List<int>    _modelIndexMap = new List<int>();
        private readonly List<string> _modelChoices  = new List<string>();

        /// <summary>宛先候補（カレントモデルの描画メッシュ）。</summary>
        private readonly List<int>    _destIndexMap = new List<int>();
        private readonly List<string> _destChoices  = new List<string>();

        /// <summary>ソース行ごとのオブジェクト候補（行ごとにモデルが違う）。</summary>
        private readonly List<int>[]    _srcIndexMaps = new List<int>[MaxSources];
        private readonly List<string>[] _srcChoices   = new List<string>[MaxSources];

        /// <summary>UI 更新中にコールバックが再入するのを防ぐ。</summary>
        private bool _suppressCallbacks = false;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private VisualElement _mainContent;

        private DropdownField _destDropdown;
        private Toggle        _toggleCreateNew;
        private Label         _destInfoLabel;

        private readonly DropdownField[] _srcModelDropdowns = new DropdownField[MaxSources];
        private readonly DropdownField[] _srcObjDropdowns   = new DropdownField[MaxSources];
        private readonly Slider[]        _srcSliders        = new Slider[MaxSources];
        private readonly Label[]         _srcWeightLabels   = new Label[MaxSources];
        private readonly Label[]         _srcStatsLabels    = new Label[MaxSources];

        private Toggle        _toggleRecalcNormals;
        private Toggle        _toggleSelectedOnly;
        private DropdownField _dropdownMatchMode;
        private Label         _matchModeHintLabel;

        private Toggle _toggleHideSources;
        private Label  _totalWeightLabel;
        private Label  _previewingLabel;
        private Button _btnApply;

        /// <summary>対応方式の表示名。並びは BlendMatchMode の値順。</summary>
        private static readonly List<string> MatchModeChoices = new List<string>
        {
            "頂点インデックス直結",
            "頂点IDで照合",
            "展開インデックス経由",
        };

        private const string NoneChoice = "(なし)";

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            for (int i = 0; i < MaxSources; i++)
            {
                _slots[i]        = new SourceSlot { ModelIndex = -1, MasterIndex = -1, Weight = 0f };
                _srcIndexMaps[i] = new List<int>();
                _srcChoices[i]   = new List<string>();
            }

            _root = new VisualElement();
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingTop    = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            BuildDestSection();
            _mainContent.Add(Sep());
            BuildSourceSection();
            _mainContent.Add(Sep());
            BuildOptionSection();
            _mainContent.Add(Sep());
            BuildActionSection();
        }

        private void BuildDestSection()
        {
            _mainContent.Add(SecLabel("宛先オブジェクト"));

            _destDropdown = new DropdownField(new List<string> { NoneChoice }, 0);
            _destDropdown.style.marginBottom = 2;
            _destDropdown.style.fontSize     = 10;
            _destDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_suppressCallbacks) return;
                int i = _destDropdown.index;
                int newDest = (i >= 0 && i < _destIndexMap.Count) ? _destIndexMap[i] : -1;
                if (newDest == _destMasterIndex) return;

                // 宛先が変わったらプレビューは作り直す（退避先が別メッシュになる）。
                EndPreview();
                _destMasterIndex = newDest;
                RefreshDestInfo();
                RefreshMatchModeHint();
                RefreshActionState();
            });
            _mainContent.Add(_destDropdown);

            _toggleCreateNew = new Toggle("新規オブジェクトを作る（元は変更しない）")
            { value = _createNewObject };
            _toggleCreateNew.style.fontSize     = 10;
            _toggleCreateNew.style.marginBottom = 2;
            _toggleCreateNew.RegisterValueChangedCallback(e => _createNewObject = e.newValue);
            _mainContent.Add(_toggleCreateNew);

            _destInfoLabel = new Label();
            _destInfoLabel.style.fontSize     = 9;
            _destInfoLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _destInfoLabel.style.marginBottom = 2;
            _mainContent.Add(_destInfoLabel);
        }

        private void BuildSourceSection()
        {
            _mainContent.Add(SecLabel($"ソース（最大 {MaxSources} 件）"));

            for (int i = 0; i < MaxSources; i++)
            {
                int slot = i;

                var box = new VisualElement();
                box.style.marginBottom    = 3;
                box.style.paddingLeft     = 2;
                box.style.borderLeftWidth = 2;
                box.style.borderLeftColor = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
                _mainContent.Add(box);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                box.Add(row);

                _srcModelDropdowns[slot] = new DropdownField(new List<string> { NoneChoice }, 0);
                _srcModelDropdowns[slot].style.flexGrow = 1;
                _srcModelDropdowns[slot].style.fontSize = 9;
                _srcModelDropdowns[slot].style.marginRight = 2;
                _srcModelDropdowns[slot].RegisterValueChangedCallback(_ =>
                {
                    if (_suppressCallbacks) return;
                    int mi = _srcModelDropdowns[slot].index;
                    int modelIndex = (mi > 0 && mi - 1 < _modelIndexMap.Count) ? _modelIndexMap[mi - 1] : -1;
                    if (modelIndex == _slots[slot].ModelIndex) return;
                    _slots[slot].ModelIndex  = modelIndex;
                    _slots[slot].MasterIndex = -1;   // モデルが変われば索引空間が変わる
                    RefreshSourceObjectChoices(slot);
                    ReapplyPreview();
                    RefreshActionState();
                });
                row.Add(_srcModelDropdowns[slot]);

                _srcObjDropdowns[slot] = new DropdownField(new List<string> { NoneChoice }, 0);
                _srcObjDropdowns[slot].style.flexGrow = 1;
                _srcObjDropdowns[slot].style.fontSize = 9;
                _srcObjDropdowns[slot].RegisterValueChangedCallback(_ =>
                {
                    if (_suppressCallbacks) return;
                    int oi = _srcObjDropdowns[slot].index;
                    var map = _srcIndexMaps[slot];
                    _slots[slot].MasterIndex = (oi > 0 && oi - 1 < map.Count) ? map[oi - 1] : -1;
                    RefreshMatchModeHint();
                    ReapplyPreview();
                    RefreshActionState();
                });
                row.Add(_srcObjDropdowns[slot]);

                var wRow = new VisualElement();
                wRow.style.flexDirection = FlexDirection.Row;
                box.Add(wRow);

                _srcSliders[slot] = new Slider(0f, 1f) { value = 0f };
                _srcSliders[slot].style.flexGrow = 1;
                _srcSliders[slot].RegisterValueChangedCallback(e =>
                {
                    if (_suppressCallbacks) return;
                    _slots[slot].Weight = e.newValue;
                    if (_srcWeightLabels[slot] != null)
                        _srcWeightLabels[slot].text = e.newValue.ToString("F2");
                    RefreshTotalWeight();
                    EnsurePreviewAndApply();
                    RefreshActionState();
                });
                wRow.Add(_srcSliders[slot]);

                _srcWeightLabels[slot] = new Label("0.00");
                _srcWeightLabels[slot].style.width         = 30;
                _srcWeightLabels[slot].style.fontSize      = 9;
                _srcWeightLabels[slot].style.unityTextAlign = TextAnchor.MiddleRight;
                wRow.Add(_srcWeightLabels[slot]);

                var clr = new Button(() => ClearSlot(slot)) { text = "×" };
                clr.style.width    = 18;
                clr.style.fontSize = 9;
                clr.style.marginLeft = 2;
                wRow.Add(clr);

                _srcStatsLabels[slot] = new Label();
                _srcStatsLabels[slot].style.fontSize   = 9;
                _srcStatsLabels[slot].style.whiteSpace = WhiteSpace.Normal;
                _srcStatsLabels[slot].style.display    = DisplayStyle.None;
                box.Add(_srcStatsLabels[slot]);
            }

            // 隠すのは面だけで、頂点と辺は残る（GPU 内部の描画フラグと
            // 面の描画判定が別経路のため）。切れるようにしておく。
            _toggleHideSources = new Toggle("ソースを隠す（面のみ・頂点/辺は残る）")
            { value = _hideSources };
            _toggleHideSources.style.fontSize     = 10;
            _toggleHideSources.style.marginBottom = 2;
            _toggleHideSources.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallbacks) return;
                _hideSources = e.newValue;
                // ブレンド計算はやり直さない。可視だけ取り直す。
                RefreshPreviewVisibility();
            });
            _mainContent.Add(_toggleHideSources);

            _totalWeightLabel = new Label();
            _totalWeightLabel.style.fontSize     = 9;
            _totalWeightLabel.style.whiteSpace   = WhiteSpace.Normal;
            _totalWeightLabel.style.marginBottom = 2;
            _mainContent.Add(_totalWeightLabel);
        }

        private void BuildOptionSection()
        {
            _toggleRecalcNormals = new Toggle("法線再計算")  { value = _recalculateNormals };
            _toggleSelectedOnly  = new Toggle("選択頂点のみ") { value = _selectedVerticesOnly };
            _toggleRecalcNormals.style.fontSize = 10;
            _toggleSelectedOnly .style.fontSize = 10;
            _toggleRecalcNormals.RegisterValueChangedCallback(e =>
            {
                _recalculateNormals = e.newValue;
                ReapplyPreview();
            });
            _toggleSelectedOnly.RegisterValueChangedCallback(e =>
            {
                _selectedVerticesOnly = e.newValue;
                ReapplyPreview();
            });
            _mainContent.Add(_toggleRecalcNormals);
            _mainContent.Add(_toggleSelectedOnly);

            _dropdownMatchMode = new DropdownField("対応方式", MatchModeChoices, (int)_matchMode);
            _dropdownMatchMode.style.marginBottom = 2;
            _dropdownMatchMode.style.fontSize     = 10;
            _dropdownMatchMode.RegisterValueChangedCallback(_ =>
            {
                if (_suppressCallbacks) return;
                int i = _dropdownMatchMode.index;
                _matchMode = i >= 0 ? (BlendMatchMode)i : BlendMatchMode.Index;
                RefreshMatchModeHint();
                ReapplyPreview();
            });
            _mainContent.Add(_dropdownMatchMode);

            _matchModeHintLabel = new Label();
            _matchModeHintLabel.style.color        = new StyleColor(new Color(0.8f, 0.8f, 0.4f));
            _matchModeHintLabel.style.fontSize     = 9;
            _matchModeHintLabel.style.whiteSpace   = WhiteSpace.Normal;
            _matchModeHintLabel.style.display      = DisplayStyle.None;
            _matchModeHintLabel.style.marginBottom = 2;
            _mainContent.Add(_matchModeHintLabel);
        }

        private void BuildActionSection()
        {
            _previewingLabel = new Label("プレビュー中...");
            _previewingLabel.style.display      = DisplayStyle.None;
            _previewingLabel.style.color        = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _previewingLabel.style.fontSize     = 10;
            _previewingLabel.style.marginBottom = 4;
            _mainContent.Add(_previewingLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _mainContent.Add(btnRow);

            _btnApply = new Button(OnApplyClicked) { text = "決定" };
            _btnApply.style.flexGrow    = 1;
            _btnApply.style.marginRight = 4;
            _btnApply.style.height      = 24;
            _btnApply.style.fontSize    = 10;
            btnRow.Add(_btnApply);

            var btnCancel = new Button(OnCancelClicked) { text = "キャンセル" };
            btnCancel.style.flexGrow = 1;
            btnCancel.style.height   = 24;
            btnCancel.style.fontSize = 10;
            btnRow.Add(btnCancel);
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            if (_blendPreview.IsActive) EndPreview();
            _model           = model;
            _destMasterIndex = -1;
            ClearAllSlots(applyPreview: false);
            Refresh();
        }

        /// <summary>選択変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            // 宛先とソースはドロップダウンで明示指定するため、選択変更では
            // 選び直さない。プレビュー中の退避先が消えた場合だけ畳む。
            if (_blendPreview.IsActive && _model?.GetMeshContext(_blendPreview.DestIndex) == null)
                EndPreview();
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            RefreshModelChoices();
            RefreshDestChoices();
            for (int i = 0; i < MaxSources; i++)
            {
                RefreshSourceModelDropdown(i);
                RefreshSourceObjectChoices(i);
                _suppressCallbacks = true;
                _srcSliders[i]?.SetValueWithoutNotify(_slots[i].Weight);
                _suppressCallbacks = false;
                if (_srcWeightLabels[i] != null)
                    _srcWeightLabels[i].text = _slots[i].Weight.ToString("F2");
            }
            RefreshDestInfo();
            RefreshTotalWeight();
            RefreshMatchModeHint();
            RefreshActionState();
            PlayerLayoutRoot.ApplyDarkTheme(_mainContent);
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display  = DisplayStyle.None;
        }

        private void RefreshModelChoices()
        {
            _modelIndexMap.Clear();
            _modelChoices.Clear();

            var view = GetProjectView?.Invoke();
            if (view == null) return;

            for (int i = 0; i < view.ModelCount; i++)
            {
                var mv = view.GetModelView(i);
                if (mv == null) continue;
                _modelIndexMap.Add(i);
                _modelChoices.Add($"{i}: {mv.Name}");
            }
        }

        private void RefreshDestChoices()
        {
            _destIndexMap.Clear();
            _destChoices.Clear();
            _destChoices.Add(NoneChoice);

            for (int i = 0; i < _model.MeshContextCount; i++)
            {
                var ctx = _model.GetMeshContext(i);
                if (!IsBlendable(ctx)) continue;
                // ミラー側は実体側から作り直されるため宛先にしない。
                if (ctx.Type == MeshType.MirrorSide || ctx.Type == MeshType.BakedMirror) continue;
                _destIndexMap.Add(i);
                _destChoices.Add($"{ctx.Name} [V:{ctx.MeshObject.VertexCount}]");
            }

            int sel = _destIndexMap.IndexOf(_destMasterIndex);
            if (sel < 0) _destMasterIndex = -1;

            _suppressCallbacks = true;
            _destDropdown.choices = new List<string>(_destChoices);
            _destDropdown.index   = sel >= 0 ? sel + 1 : 0;
            _suppressCallbacks = false;
        }

        private void RefreshSourceModelDropdown(int slot)
        {
            var choices = new List<string> { NoneChoice };
            choices.AddRange(_modelChoices);

            int sel = _modelIndexMap.IndexOf(_slots[slot].ModelIndex);
            if (sel < 0) { _slots[slot].ModelIndex = -1; _slots[slot].MasterIndex = -1; }

            _suppressCallbacks = true;
            _srcModelDropdowns[slot].choices = choices;
            _srcModelDropdowns[slot].index   = sel >= 0 ? sel + 1 : 0;
            _suppressCallbacks = false;
        }

        private void RefreshSourceObjectChoices(int slot)
        {
            var map     = _srcIndexMaps[slot];
            var choices = _srcChoices[slot];
            map.Clear();
            choices.Clear();
            choices.Add(NoneChoice);

            var srcModel = GetModelContext?.Invoke(_slots[slot].ModelIndex);
            if (srcModel != null)
            {
                for (int i = 0; i < srcModel.MeshContextCount; i++)
                {
                    var ctx = srcModel.GetMeshContext(i);
                    if (!IsBlendable(ctx)) continue;
                    map.Add(i);
                    choices.Add($"{ctx.Name} [V:{ctx.MeshObject.VertexCount}]");
                }
            }

            int sel = map.IndexOf(_slots[slot].MasterIndex);
            if (sel < 0) _slots[slot].MasterIndex = -1;

            _suppressCallbacks = true;
            _srcObjDropdowns[slot].choices = new List<string>(choices);
            _srcObjDropdowns[slot].index   = sel >= 0 ? sel + 1 : 0;
            _suppressCallbacks = false;
        }

        private static bool IsBlendable(MeshContext ctx)
        {
            if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) return false;
            return ctx.Type == MeshType.Mesh
                || ctx.Type == MeshType.BakedMirror
                || ctx.Type == MeshType.MirrorSide;
        }

        private void RefreshDestInfo()
        {
            if (_destInfoLabel == null) return;
            var ctx = _destMasterIndex >= 0 ? _model?.GetMeshContext(_destMasterIndex) : null;
            _destInfoLabel.text = ctx?.MeshObject != null
                ? $"宛先頂点数: {ctx.MeshObject.VertexCount}"
                : "宛先が未選択です";
        }

        private void RefreshTotalWeight()
        {
            if (_totalWeightLabel == null) return;

            float total = 0f;
            int   used  = 0;
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                total += _slots[i].Weight;
                used++;
            }

            if (used == 0)
            {
                _totalWeightLabel.text  = "ソースが未選択です";
                _totalWeightLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                return;
            }

            if (total > 1f)
            {
                _totalWeightLabel.text =
                    $"合計ウェイト {total:F2}（1 を超えるため正規化されます。元形状は残りません）";
                _totalWeightLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.3f));
            }
            else
            {
                _totalWeightLabel.text =
                    $"合計ウェイト {total:F2}　元形状 {1f - total:F2}";
                _totalWeightLabel.style.color = new StyleColor(new Color(0.5f, 0.9f, 0.5f));
            }
        }

        private bool IsSlotUsable(int slot)
            => _slots[slot].ModelIndex >= 0
            && _slots[slot].MasterIndex >= 0
            && _slots[slot].Weight > 0f;

        private void RefreshActionState()
        {
            bool ready = _destMasterIndex >= 0 && HasAnyUsableSlot();
            _btnApply?.SetEnabled(ready && _blendPreview.IsActive);
            if (_previewingLabel != null)
                _previewingLabel.style.display =
                    _blendPreview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool HasAnyUsableSlot()
        {
            for (int i = 0; i < MaxSources; i++)
                if (IsSlotUsable(i)) return true;
            return false;
        }

        /// <summary>
        /// 選んだ対応方式が実際に使える状態かを出す。
        /// 頂点ID照合は、未設定IDや重複IDがあると黙って対応が取れない頂点が出る。
        /// 展開インデックス経由は、両者の IsTriangulated が同じなら
        /// 頂点インデックス直結と同じ動きになる。
        /// </summary>
        private void RefreshMatchModeHint()
        {
            if (_matchModeHintLabel == null) return;

            var destCtx = _destMasterIndex >= 0 ? _model?.GetMeshContext(_destMasterIndex) : null;
            var destMo  = destCtx?.MeshObject;
            if (destMo == null)
            {
                _matchModeHintLabel.style.display = DisplayStyle.None;
                return;
            }

            var lines = new List<string>();

            if (_matchMode == BlendMatchMode.VertexId)
            {
                var (dUnset, dDup) = BlendVertexResolver.InspectVertexIds(destMo);
                if (dUnset > 0 || dDup > 0)
                    lines.Add($"宛先「{destCtx.Name}」の頂点ID: 未設定 {dUnset} / 重複 {dDup}");

                ForEachUsableSource((ctx, _) =>
                {
                    var (u, d) = BlendVertexResolver.InspectVertexIds(ctx.MeshObject);
                    if (u > 0 || d > 0)
                        lines.Add($"ソース「{ctx.Name}」の頂点ID: 未設定 {u} / 重複 {d}");
                });

                if (lines.Count > 0)
                    lines.Add("未設定IDの頂点は対応対象外、重複IDは先勝ちになります。");
            }
            else if (_matchMode == BlendMatchMode.Expanded)
            {
                ForEachUsableSource((ctx, _) =>
                {
                    if (ctx.MeshObject.IsTriangulated == destMo.IsTriangulated)
                        lines.Add($"「{ctx.Name}」と宛先は三角形化状態が同じため、頂点インデックス直結と同じ動きになります。");
                });
            }

            if (lines.Count == 0)
            {
                _matchModeHintLabel.style.display = DisplayStyle.None;
                return;
            }
            _matchModeHintLabel.text          = string.Join("\n", lines);
            _matchModeHintLabel.style.display = DisplayStyle.Flex;
        }

        // ================================================================
        // ソース解決
        // ================================================================

        private void ForEachUsableSource(Action<MeshContext, float> action)
        {
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                var m   = GetModelContext?.Invoke(_slots[i].ModelIndex);
                var ctx = m?.GetMeshContext(_slots[i].MasterIndex);
                if (ctx?.MeshObject == null) continue;
                action(ctx, _slots[i].Weight);
            }
        }

        /// <summary>
        /// 有効なソースを解決する。返り値の並びは slotOrder と対応する。
        /// </summary>
        private List<BlendSourceEntry> ResolveSources(List<int> slotOrder)
        {
            var list = new List<BlendSourceEntry>();
            slotOrder?.Clear();
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                var m   = GetModelContext?.Invoke(_slots[i].ModelIndex);
                var ctx = m?.GetMeshContext(_slots[i].MasterIndex);
                if (ctx?.MeshObject == null) continue;
                list.Add(new BlendSourceEntry(ctx, _slots[i].Weight));
                slotOrder?.Add(i);
            }
            return list;
        }

        /// <summary>
        /// プレビュー中に隠すメッシュ索引。カレントモデル内のソースのみ。
        /// 別モデルの索引を混ぜると索引空間が違うため無関係なメッシュを隠す。
        /// </summary>
        private List<int> BuildHideIndices()
        {
            var list = new List<int>();
            if (!_hideSources) return list;

            int curModel = _getModelIndex?.Invoke() ?? 0;
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                if (_slots[i].ModelIndex != curModel) continue;
                if (_slots[i].MasterIndex == _destMasterIndex) continue;
                list.Add(_slots[i].MasterIndex);
            }
            return list;
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void EnsurePreviewAndApply()
        {
            if (_model == null || _destMasterIndex < 0) return;

            if (!HasAnyUsableSlot())
            {
                // ウェイトを 0 にして有効なソースが無くなった場合。
                // ここで戻さないと、隠したメッシュが隠れたまま残る。
                RefreshPreviewVisibility();
                return;
            }

            if (!_blendPreview.IsActive)
            {
                _blendPreview.Start(_model, _destMasterIndex, BuildHideIndices());
                if (_blendPreview.IsActive) OnMeshVisibilityChanged?.Invoke();
            }

            ApplyPreview();
        }

        /// <summary>
        /// 隠す対象をいまの設定で取り直す。ソースの差し替え・追加・削除、
        /// 「ソースを隠す」の切替から呼ぶ。ブレンド計算はやり直さない。
        /// </summary>
        private void RefreshPreviewVisibility()
        {
            if (_model == null || !_blendPreview.IsActive) return;
            if (_blendPreview.UpdateHiddenSources(_model, BuildHideIndices()))
                OnMeshVisibilityChanged?.Invoke();
        }

        private void ReapplyPreview()
        {
            if (!_blendPreview.IsActive) return;
            ApplyPreview();
        }

        /// <summary>
        /// 現在の設定でプレビューを適用し、ソースごとの対応率を表示する。
        /// 対応が取れない頂点は動かさないため、件数を出さないと
        /// ソースや対応方式を選び間違えても「効かない」という見え方しかしない。
        /// </summary>
        private void ApplyPreview()
        {
            if (_model == null) return;

            // ソースの差し替え・追加・削除へ追随する。プレビュー開始時に
            // 一度決めるだけだと、後から選び直したソースが隠れず、
            // 前のソースが隠れたまま残る。
            RefreshPreviewVisibility();

            var order   = new List<int>();
            var sources = ResolveSources(order);
            if (sources.Count == 0) return;

            var stats = _blendPreview.Apply(
                _model, sources,
                _selectedVerticesOnly, _matchMode, _recalculateNormals,
                OnSyncMeshNormals, BuildToolCtx());

            ShowStats(order, stats);
            RefreshActionState();
        }

        private void ShowStats(List<int> slotOrder, BlendMatchStats[] stats)
        {
            for (int i = 0; i < MaxSources; i++)
                if (_srcStatsLabels[i] != null)
                    _srcStatsLabels[i].style.display = DisplayStyle.None;

            if (stats == null || slotOrder == null) return;

            for (int k = 0; k < slotOrder.Count && k < stats.Length; k++)
            {
                int slot = slotOrder[k];
                var lbl  = _srcStatsLabels[slot];
                if (lbl == null) continue;

                var st = stats[k];
                if (st.TargetVertexCount == 0)
                {
                    lbl.text = "対象頂点がありません（孤立頂点のみ、または選択頂点が空）";
                    lbl.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                }
                else
                {
                    lbl.text = $"対応 {st.MatchedVertexCount} / {st.TargetVertexCount}"
                             + $"（{st.MatchRatio * 100f:F1}%）"
                             + (st.UnmatchedVertexCount > 0
                                 ? $"　未対応 {st.UnmatchedVertexCount} 頂点は元位置のまま"
                                 : "");
                    lbl.style.color = st.UnmatchedVertexCount > 0
                        ? new StyleColor(new Color(1f, 0.7f, 0.3f))
                        : new StyleColor(new Color(0.5f, 0.9f, 0.5f));
                }
                lbl.style.display = DisplayStyle.Flex;
            }
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            if (_model == null || _destMasterIndex < 0) return;

            var specs = new List<BlendSourceSpec>();
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                specs.Add(new BlendSourceSpec(
                    _slots[i].ModelIndex, _slots[i].MasterIndex, _slots[i].Weight));
            }
            if (specs.Count == 0) return;

            if (_panelContext != null)
            {
                // コマンド経由（Undo記録はDispatcher側で行う）。
                // Dispatcher は自前の BlendPreviewState を作り直すため、
                // こちらのプレビューは先に終了させてブレンド前の位置へ戻す。
                // 戻さないと退避値が古いまま生き続け、次の操作で巻き戻る。
                EndPreview();
                _panelContext.SendCommand(new ApplyBlendCommand(
                    _getModelIndex?.Invoke() ?? 0,
                    specs.ToArray(), _destMasterIndex,
                    _createNewObject, _recalculateNormals,
                    _selectedVerticesOnly, _matchMode));
            }
            else
            {
                // フォールバック（PanelContext未設定時）。
                // ApplyBlend は内部でバックアップ位置へ戻してから確定する。
                var sources = ResolveSources(null);
                BlendOperation.ApplyBlend(
                    _model, _blendPreview, sources,
                    _recalculateNormals, _selectedVerticesOnly,
                    _matchMode, _createNewObject, BuildToolCtx());
                _blendPreview.End(_model, BuildToolCtx());
            }

            ClearAllSlots(applyPreview: false);
            Refresh();
        }

        private void OnCancelClicked()
        {
            EndPreview();
            ClearAllSlots(applyPreview: false);
            Refresh();
        }

        private void ClearSlot(int slot)
        {
            _slots[slot] = new SourceSlot { ModelIndex = -1, MasterIndex = -1, Weight = 0f };

            _suppressCallbacks = true;
            _srcModelDropdowns[slot].index = 0;
            _srcObjDropdowns[slot].index   = 0;
            _srcSliders[slot].SetValueWithoutNotify(0f);
            _suppressCallbacks = false;

            if (_srcWeightLabels[slot] != null) _srcWeightLabels[slot].text = "0.00";
            if (_srcStatsLabels[slot]  != null) _srcStatsLabels[slot].style.display = DisplayStyle.None;

            RefreshSourceObjectChoices(slot);
            RefreshTotalWeight();

            // 有効なソースが無くなったらプレビューを畳む。
            if (!HasAnyUsableSlot()) EndPreview();
            else ReapplyPreview();

            RefreshActionState();
        }

        private void ClearAllSlots(bool applyPreview)
        {
            for (int i = 0; i < MaxSources; i++)
            {
                _slots[i] = new SourceSlot { ModelIndex = -1, MasterIndex = -1, Weight = 0f };
                if (_srcModelDropdowns[i] != null)
                {
                    _suppressCallbacks = true;
                    _srcModelDropdowns[i].index = 0;
                    _srcObjDropdowns[i].index   = 0;
                    _srcSliders[i].SetValueWithoutNotify(0f);
                    _suppressCallbacks = false;
                }
                if (_srcWeightLabels[i] != null) _srcWeightLabels[i].text = "0.00";
                if (_srcStatsLabels[i]  != null) _srcStatsLabels[i].style.display = DisplayStyle.None;
            }
            if (applyPreview) ReapplyPreview();
        }

        /// <summary>
        /// パネルを閉じる／切り替えるときに呼ぶ。
        /// プレビュー中の頂点位置は MeshObject に書き込まれているため、
        /// 非表示にしただけでは未確定の形状が残り、そのまま保存される。
        /// </summary>
        public void CancelIfActive()
        {
            if (!_blendPreview.IsActive) return;
            EndPreview();
        }

        private void EndPreview()
        {
            bool wasActive = _blendPreview.IsActive;
            _blendPreview.End(_model, BuildToolCtx());
            if (wasActive) OnMeshVisibilityChanged?.Invoke();
            for (int i = 0; i < MaxSources; i++)
                if (_srcStatsLabels[i] != null)
                    _srcStatsLabels[i].style.display = DisplayStyle.None;
            RefreshActionState();
        }

        // ================================================================
        // ToolContext 生成（最小構成）
        // ================================================================

        private Poly_Ling.Tools.ToolContext BuildToolCtx()
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model          = _model;
            ctx.Repaint        = OnRepaint;
            ctx.UndoController = GetUndoController?.Invoke();
            ctx.CommandQueue   = GetCommandQueue?.Invoke();

            // SyncMeshContextPositionsOnly: UnityMesh + GPU バッファを更新
            ctx.SyncMeshContextPositionsOnly = mc =>
            {
                OnSyncMeshPositions?.Invoke(mc);
            };

            // NotifyTopologyChanged: RebuildAdapter 相当
            ctx.NotifyTopologyChanged = OnNotifyTopologyChanged;

            return ctx;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Label SecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
