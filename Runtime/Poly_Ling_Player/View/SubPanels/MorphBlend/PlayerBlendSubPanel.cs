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

        /// <summary>再描画要求コールバック。</summary>
        public Action OnRepaint;

        /// <summary>
        /// プロジェクトの窓口（操作経路統一計画.md E）。モデル一覧・宛先とソースの候補を読む。
        /// </summary>
        public Func<IProjectView> GetProjectView;

        /// <summary>ツールの窓口（"blend"）。試し表示（プレビュー）と対応方式の注意書き。</summary>
        public IToolSurface Surface;

        private IModelView Model => GetProjectView?.Invoke()?.CurrentModel;

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

        private readonly SourceSlot[] _slots = new SourceSlot[MaxSources];

        private int  _destMasterIndex      = -1;
        private bool _createNewObject      = false;

        /// <summary>
        /// ソースと重みをオブジェクトグループとして残すか。既定 false。
        /// false のときは確定した時点でソース指定も重みも残らない
        /// （＝ソースを直しても出力先は古いまま。混ぜ直すには同じ操作をやり直す）。
        /// </summary>
        private bool _keepAsGroup          = false;
        private bool _recalculateNormals   = true;
        private bool _selectedVerticesOnly = false;

        /// <summary>
        /// プレビュー中にソースを隠すか。
        /// true  … 隠す（面は消えるが頂点と辺は残る）
        /// false … 何も消さない
        /// </summary>
        private bool _hideSources          = true;
        private BlendMatchMode _matchMode  = BlendMatchMode.Index;

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

        // UI 自動操作の ID は "blend.<下の Id>"（UiControlAttribute.cs）。
        // ソースは固定個数のスロット（1 始まり）。source.1.model のように番号で指す。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label         _warningLabel;
        [UiControl(Ignore = true)]
        private VisualElement _mainContent;

        [UiControl("destination", Description = "宛先オブジェクト")]
        private DropdownField _destDropdown;
        [UiControl("createNew", Description = "新規オブジェクトを作る（元は変更しない）")]
        private Toggle        _toggleCreateNew;
        [UiControl("destinationInfo", Safety = UiSafety.ReadOnly, Description = "宛先オブジェクトの情報")]
        private Label         _destInfoLabel;

        [UiControl("source.{0}.model", Description = "ソース {0} のモデル")]
        private readonly DropdownField[] _srcModelDropdowns = new DropdownField[MaxSources];
        [UiControl("source.{0}.object", Description = "ソース {0} のオブジェクト")]
        private readonly DropdownField[] _srcObjDropdowns   = new DropdownField[MaxSources];
        [UiControl("source.{0}.weight", Description = "ソース {0} の重み（0〜1）")]
        private readonly Slider[]        _srcSliders        = new Slider[MaxSources];
        [UiControl("source.{0}.weightText", Safety = UiSafety.ReadOnly, Description = "ソース {0} の重みの表示")]
        private readonly Label[]         _srcWeightLabels   = new Label[MaxSources];
        [UiControl("source.{0}.stats", Safety = UiSafety.ReadOnly, Description = "ソース {0} の情報（指定したときだけ表示）")]
        private readonly Label[]         _srcStatsLabels    = new Label[MaxSources];
        [UiControl("source.{0}.clear", Safety = UiSafety.SafeWrite, Description = "ソース {0} の指定を外す")]
        private readonly Button[]        _srcClearBtns      = new Button[MaxSources];

        [UiControl("recalcNormals", Description = "法線を再計算する")]
        private Toggle        _toggleRecalcNormals;
        [UiControl("selectedOnly", Description = "選択頂点のみ")]
        private Toggle        _toggleSelectedOnly;
        [UiControl("matchMode", Description = "頂点の対応方式")]
        private DropdownField _dropdownMatchMode;
        [UiControl("matchModeHint", Safety = UiSafety.ReadOnly, Description = "対応方式についての注意（出ていないときは非表示）")]
        private Label         _matchModeHintLabel;
        [UiControl("keepAsGroup", Description = "オブジェクトグループとして残す（オフのときソース指定と重みは確定後に破棄）")]
        private Toggle        _toggleKeepGroup;

        [UiControl("hideSources", Description = "ソースを隠す（面のみ・頂点/辺は残る）")]
        private Toggle _toggleHideSources;
        [UiControl("totalWeight", Safety = UiSafety.ReadOnly, Description = "重みの合計")]
        private Label  _totalWeightLabel;
        [UiControl("previewing", Safety = UiSafety.ReadOnly, Description = "プレビュー中の表示（プレビュー中だけ表示）")]
        private Label  _previewingLabel;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "ブレンドを決定する")]
        private Button _btnApply;
        [UiControl("cancel", Safety = UiSafety.SafeWrite, Description = "ブレンドのプレビューを取り消す")]
        private Button _btnCancel;

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
                _srcClearBtns[slot] = clr;

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

            // ── グループとして残すか
            //    毎回ダイアログを出すと確定の操作が重くなるので、
            //    警告は常設のラベルにする。off のときだけ出す。
            var toggleKeepGroup = new Toggle("オブジェクトグループとして残す")
            { value = _keepAsGroup };
            toggleKeepGroup.style.fontSize = 10;
            _mainContent.Add(toggleKeepGroup);
            _toggleKeepGroup = toggleKeepGroup;

            var keepGroupWarn = new Label(
                "オフのとき、ソース指定と重みは確定後に破棄されます。混ぜ直すには同じ操作をやり直すことになります。");
            keepGroupWarn.style.whiteSpace   = WhiteSpace.Normal;
            keepGroupWarn.style.fontSize     = 9;
            keepGroupWarn.style.marginLeft   = 16;
            keepGroupWarn.style.marginBottom = 2;
            keepGroupWarn.style.color = new StyleColor(new Color(1f, 0.75f, 0.35f));
            keepGroupWarn.style.display = _keepAsGroup ? DisplayStyle.None : DisplayStyle.Flex;
            _mainContent.Add(keepGroupWarn);

            toggleKeepGroup.RegisterValueChangedCallback(e =>
            {
                _keepAsGroup = e.newValue;
                keepGroupWarn.style.display = _keepAsGroup ? DisplayStyle.None : DisplayStyle.Flex;
            });

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
            _btnCancel = btnCancel;
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel()
        {
            EndPreview();
            _destMasterIndex = -1;
            ClearAllSlots(applyPreview: false);
            Refresh();
        }

        /// <summary>選択変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            // 宛先とソースはドロップダウンで明示指定するため、選択変更では
            // 選び直さない。プレビュー中の宛先が消えた場合だけ畳む。
            if (IsPreviewing && Model?.GetMesh(_destMasterIndex) == null)
                EndPreview();
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (Model == null)
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

            var model = Model;
            for (int i = 0; i < (model?.TotalMeshCount ?? 0); i++)
            {
                var ctx = model.GetMesh(i);
                if (!IsBlendable(ctx)) continue;
                // ミラー側は実体側から作り直されるため宛先にしない。
                if (ctx.Type == MeshType.MirrorSide || ctx.Type == MeshType.BakedMirror) continue;
                _destIndexMap.Add(i);
                _destChoices.Add($"{ctx.Name} [V:{ctx.VertexCount}]");
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

            var srcModel = _slots[slot].ModelIndex >= 0
                ? GetProjectView?.Invoke()?.GetModelView(_slots[slot].ModelIndex) : null;
            if (srcModel != null)
            {
                for (int i = 0; i < srcModel.TotalMeshCount; i++)
                {
                    var ctx = srcModel.GetMesh(i);
                    if (!IsBlendable(ctx)) continue;
                    map.Add(i);
                    choices.Add($"{ctx.Name} [V:{ctx.VertexCount}]");
                }
            }

            int sel = map.IndexOf(_slots[slot].MasterIndex);
            if (sel < 0) _slots[slot].MasterIndex = -1;

            _suppressCallbacks = true;
            _srcObjDropdowns[slot].choices = new List<string>(choices);
            _srcObjDropdowns[slot].index   = sel >= 0 ? sel + 1 : 0;
            _suppressCallbacks = false;
        }

        private static bool IsBlendable(IMeshView ctx)
        {
            if (ctx == null || ctx.VertexCount == 0) return false;
            return ctx.Type == MeshType.Mesh
                || ctx.Type == MeshType.BakedMirror
                || ctx.Type == MeshType.MirrorSide;
        }

        private void RefreshDestInfo()
        {
            if (_destInfoLabel == null) return;
            var ctx = _destMasterIndex >= 0 ? Model?.GetMesh(_destMasterIndex) : null;
            _destInfoLabel.text = ctx != null
                ? $"宛先頂点数: {ctx.VertexCount}"
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
            bool previewing = IsPreviewing;
            _btnApply?.SetEnabled(ready && previewing);
            if (_previewingLabel != null)
                _previewingLabel.style.display = previewing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool HasAnyUsableSlot()
        {
            for (int i = 0; i < MaxSources; i++)
                if (IsSlotUsable(i)) return true;
            return false;
        }

        /// <summary>
        /// 選んだ対応方式が実際に使える状態かを出す。判定はツールの窓口 "blend" の
        /// inspectMatchMode が本体側で行う（頂点 ID・三角形化状態を読むため）。
        /// </summary>
        private void RefreshMatchModeHint()
        {
            if (_matchModeHintLabel == null) return;
            if (_destMasterIndex < 0 || Surface == null)
            {
                _matchModeHintLabel.style.display = DisplayStyle.None;
                return;
            }

            CollectUsable(null, out var models, out var masters, out _);
            Surface.Invoke(Tool, "inspectMatchMode",
                ("dest", _destMasterIndex), ("srcModels", models), ("srcMasters", masters), ("matchMode", _matchMode));
            string hint = Surface.GetString(Tool, "matchModeHint");
            if (string.IsNullOrEmpty(hint))
            {
                _matchModeHintLabel.style.display = DisplayStyle.None;
                return;
            }
            _matchModeHintLabel.text          = hint;
            _matchModeHintLabel.style.display = DisplayStyle.Flex;
        }

        // ================================================================
        // ソース
        // ================================================================

        /// <summary>有効なソースを、窓口へ渡す並列配列にする。slotOrder には元の行番号を入れる。</summary>
        private void CollectUsable(List<int> slotOrder, out int[] models, out int[] masters, out float[] weights)
        {
            var m = new List<int>(); var s = new List<int>(); var w = new List<float>();
            slotOrder?.Clear();
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                m.Add(_slots[i].ModelIndex); s.Add(_slots[i].MasterIndex); w.Add(_slots[i].Weight);
                slotOrder?.Add(i);
            }
            models = m.ToArray(); masters = s.ToArray(); weights = w.ToArray();
        }

        // ================================================================
        // プレビュー（ツールの窓口 "blend"。操作経路統一計画.md E）
        // ================================================================

        private const string Tool = "blend";

        private bool IsPreviewing => Surface != null && Surface.GetBool(Tool, "isPreviewing");

        private void EnsurePreviewAndApply()
        {
            if (Model == null || _destMasterIndex < 0) return;
            ApplyPreview();
        }

        /// <summary>隠す対象と表示をいまの設定で取り直す（窓口の preview が隠す対象も追随させる）。</summary>
        private void RefreshPreviewVisibility()
        {
            if (!IsPreviewing) return;
            ApplyPreview();
        }

        private void ReapplyPreview()
        {
            if (!IsPreviewing) return;
            ApplyPreview();
        }

        /// <summary>
        /// 現在の設定でプレビューを適用し、ソースごとの対応率を表示する。
        /// 対応が取れない頂点は動かさないため、件数を出さないと
        /// ソースや対応方式を選び間違えても「効かない」という見え方しかしない。
        /// </summary>
        private void ApplyPreview()
        {
            if (Model == null || Surface == null || _destMasterIndex < 0) return;

            var order = new List<int>();
            CollectUsable(order, out var models, out var masters, out var weights);
            Surface.Invoke(Tool, "preview",
                ("dest", _destMasterIndex), ("srcModels", models), ("srcMasters", masters), ("weights", weights),
                ("selectedVerticesOnly", _selectedVerticesOnly), ("matchMode", _matchMode),
                ("recalculateNormals", _recalculateNormals), ("hideSources", _hideSources));

            ShowStats(order,
                Surface.Get(Tool, "statsLines", Array.Empty<string>()),
                Surface.Get(Tool, "statsWarn",  Array.Empty<bool>()));
            RefreshActionState();
        }

        private void ShowStats(List<int> slotOrder, string[] lines, bool[] warn)
        {
            for (int i = 0; i < MaxSources; i++)
                if (_srcStatsLabels[i] != null)
                    _srcStatsLabels[i].style.display = DisplayStyle.None;

            if (lines == null || slotOrder == null) return;

            for (int k = 0; k < slotOrder.Count && k < lines.Length; k++)
            {
                int slot = slotOrder[k];
                var lbl  = _srcStatsLabels[slot];
                if (lbl == null) continue;
                lbl.text = lines[k];
                lbl.style.color = (warn != null && k < warn.Length && warn[k])
                    ? new StyleColor(new Color(1f, 0.7f, 0.3f))
                    : new StyleColor(new Color(0.5f, 0.9f, 0.5f));
                lbl.style.display = DisplayStyle.Flex;
            }
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            if (Model == null || _destMasterIndex < 0) return;

            var specs = new List<BlendSourceSpec>();
            for (int i = 0; i < MaxSources; i++)
            {
                if (!IsSlotUsable(i)) continue;
                specs.Add(new BlendSourceSpec(
                    _slots[i].ModelIndex, _slots[i].MasterIndex, _slots[i].Weight));
            }
            if (specs.Count == 0) return;

            // 確定はコマンドだけで行う（本体も SetCommandContext を渡す。操作経路統一計画.md J）。
            // Undo 記録はディスパッチャ側で行う。ディスパッチャは自前の BlendPreviewState を
            // 作り直すため、こちらのプレビューは先に終了させてブレンド前の位置へ戻す。
            // 戻さないと退避値が古いまま生き続け、次の操作で巻き戻る。
            EndPreview();
            ApplyBlendCommand.SplitSources(
                specs.ToArray(), out var srcModels, out var srcMasters, out var srcWeights);
            _panelContext?.SendCommand(new ApplyBlendCommand(
                _getModelIndex?.Invoke() ?? 0,
                srcModels, srcMasters, srcWeights, _destMasterIndex,
                _createNewObject, _recalculateNormals,
                _selectedVerticesOnly, _matchMode, _keepAsGroup));

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
            if (!IsPreviewing) return;
            EndPreview();
        }

        private void EndPreview()
        {
            Surface?.Invoke(Tool, "endPreview");
            for (int i = 0; i < MaxSources; i++)
                if (_srcStatsLabels[i] != null)
                    _srcStatsLabels[i].style.display = DisplayStyle.None;
            RefreshActionState();
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
