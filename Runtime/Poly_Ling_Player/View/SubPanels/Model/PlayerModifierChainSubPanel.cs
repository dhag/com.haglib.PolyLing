// PlayerModifierChainSubPanel.cs
// 処理列（1 つのオブジェクトに処理を積み重ねる非破壊のモディファイア）のパネル。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.5・段 5
//
// 【対象】
//   編集対象のオブジェクト（IModelView.ActiveMeshIndex）。それがどれかの処理列の元メッシュか結果なら、
//   その処理列を出す。どちらでもなければ空で、「追加」で元メッシュにした処理列を新しく作る。
//   既存のサブディビジョン（subdivideMesh）のスタックの親か子なら、その項目を種類「サブディビジョン」の
//   見た目で出す（段 8。_isSubdiv）。末尾固定の種類（PLMeshModifier.PinnedLast）の項目は ↑↓ を使えなくする。
//
// 【操作はすべてコマンド】
//   追加 addMeshModifier・引数 setMeshModifierArgs・並べ替え moveMeshModifier・有効 setMeshModifierEnabled・
//   削除 removeMeshModifier・適用 applyMeshModifierChain。画面から直接モデルを書かない。
//   スライダーを動かしている間は setMeshModifierArgs の preview=true（Undo を積まない）、
//   離したときに preview=false（受け口が Undo 1 件にまとめる）。
//
// 【読み直し】
//   一覧は Refresh のたびに作り直す。引数欄は、項目・種類・キーの組が変わったときだけ作り直し、
//   それ以外は値だけ合わせる（ドラッグ中のスライダーを壊さないため。ParamFieldBuilder）。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Modifiers;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerModifierChainSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<IProjectView> GetProject;
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private IProjectView Project      => GetProject?.Invoke();
        private IModelView   CurrentModel => Project?.CurrentModel;
        private int          ModelIndex   => Project?.CurrentModelIndex ?? 0;

        // ================================================================
        // ボタンや入力欄（UI 自動操作の ID は "modifierChain.<Id>"）
        // ================================================================

        [UiControl("target", Safety = UiSafety.ReadOnly, Description = "対象（編集対象のオブジェクトと、その処理列）")]
        private Label _targetLabel;
        [UiControl("kind", Description = "足す処理の種類")]
        private DropdownField _kindDropdown;
        [UiControl("add", Safety = UiSafety.SafeWrite, Description = "選んだ種類の処理を末尾に足す（処理列が無ければ新しく作る）")]
        private Button _btnAdd;
        [UiControl("items", Description = "処理列の項目（一覧の行番号。上から順に流す）")]
        private ListView _listView;
        [UiControl("up", Safety = UiSafety.SafeWrite, Description = "選んだ項目を 1 つ上へ")]
        private Button _btnUp;
        [UiControl("down", Safety = UiSafety.SafeWrite, Description = "選んだ項目を 1 つ下へ")]
        private Button _btnDown;
        [UiControl("enabled", Description = "選んだ項目を流すか（外すと一時的に無効）")]
        private Toggle _enabledToggle;
        [UiControl("remove", Safety = UiSafety.Destructive, Description = "選んだ項目を消す（最後の 1 つなら処理列ごと外し、結果を消す）")]
        private Button _btnRemove;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "処理列を適用する（結果を普通のメッシュとして残す）")]
        private Button _btnApply;
        [UiControl("itemState", Safety = UiSafety.ReadOnly, Description = "選んだ項目の状態と理由")]
        private Label _itemStateLabel;

        [UiControl(Ignore = true)]
        private VisualElement _argContainer;
        /// <summary>引数欄（ParamFieldBuilder が作る。ID は "modifierChain.arg.&lt;キー&gt;"）。</summary>
        private readonly UiDynamicControls _argControls = new UiDynamicControls { IsPossibleId = IsPossibleArgId };

        /// <summary>
        /// 項目を選ぶまで作られない引数欄の ID か（"arg.&lt;キー&gt;"、スライダー・成分は後ろに続く）。
        /// 欄の有無は種類の宣言で決まる（ParamFieldSpec.HasField）。
        /// </summary>
        private static bool IsPossibleArgId(string id)
        {
            const string Head = "arg.";
            if (string.IsNullOrEmpty(id) || !id.StartsWith(Head, StringComparison.Ordinal)) return false;
            string rest = id.Substring(Head.Length);
            foreach (var info in MeshModifierRegistry.All)
                foreach (var p in info.Params)
                {
                    if (!new ParamFieldSpec { Key = p.Key, Type = p.Type, Attr = p.Attr }.HasField) continue;
                    if (rest == p.Key || rest.StartsWith(p.Key + ".", StringComparison.Ordinal)) return true;
                }
            return false;
        }

        [UiControl("scope.kind", Description = "処理範囲（0 全体 / 1 登録した選択 / 2 パーツ選択辞書）")]
        private RadioButtonGroup _scopeKind;
        [UiControl("scope.partsSet", Description = "処理範囲にするパーツ選択辞書の名前（元メッシュの辞書）")]
        private DropdownField _scopePartsSet;
        [UiControl("scope.fromSelection", Safety = UiSafety.SafeWrite, Description = "元メッシュの今の選択（頂点・辺・面）を処理範囲として登録する")]
        private Button _btnScopeFromSel;
        [UiControl("scope.info", Safety = UiSafety.ReadOnly, Description = "処理範囲の中身")]
        private Label _scopeInfo;

        [UiControl("detailFold", Safety = UiSafety.SafeWrite, Description = "詳細（引数を文字列で書く）の開閉")]
        private Foldout _detailFold;
        [UiControl("detailKey", Description = "文字列で書く引数のキー")]
        private DropdownField _detailKey;
        [UiControl("detailValue", Description = "書く値（文字列。種類の宣言で読み直せること）")]
        private TextField _detailValue;
        [UiControl("detailWrite", Safety = UiSafety.SafeWrite, Description = "文字列の値を書く")]
        private Button _btnDetailWrite;

        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;

        // ================================================================
        // 内部状態
        // ================================================================

        private readonly List<string> _labels = new List<string>();
        private readonly List<ModifierStackStepView> _items = new List<ModifierStackStepView>();
        private ModifierStackView _chain;
        private int _activeIndex = -1;
        private string _selectedItemId = "";
        private bool _selectLastAfterRefresh;
        private bool _suppress;

        /// <summary>
        /// 出しているのが既存のサブディビジョン（subdivideMesh）のスタックか（段 8）。
        /// 項目は処理列の種類「サブディビジョン」の宣言で同じ見た目に出し、回数の変更は setModifierStackStepArgs
        /// （項目の作り直し）、削除・適用は ReleaseSubdivisionCommand（子を消す／残す）を送る。
        /// 追加・並べ替え・有効・処理範囲・詳細は使えない。
        /// </summary>
        private bool _isSubdiv;
        private const string SubdivisionAction = "subdivideMesh";
        private const string SubdivisionKind   = "subdivision";

        private ParamFieldBuilder _builder;
        private string _argSig = "";
        private readonly List<ParamFieldSpec> _specs = new List<ParamFieldSpec>();
        private readonly List<string> _kindIds = new List<string>();

        private static readonly string[] ScopeKeys =
        {
            MeshModifierScope.KeyKind, MeshModifierScope.KeyVertexIds, MeshModifierScope.KeyEdgeVertexIds,
            MeshModifierScope.KeyFaceVertexIds, MeshModifierScope.KeyFaceSizes, MeshModifierScope.KeyPartsSet,
        };

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("処理列"));
            var hint = new Label(
                "編集対象のオブジェクトに処理を上から順に積み重ねます。元のメッシュは書き換えず、結果は子のオブジェクトに出ます。\n"
              + "処理の並べ替え・一時的な無効化・値の変更はいつでもやり直せます。「適用」で結果を普通のメッシュにします。");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize   = 10;
            hint.style.marginBottom = 4;
            root.Add(hint);

            _targetLabel = new Label();
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _targetLabel.style.fontSize   = 10;
            _targetLabel.style.marginBottom = 4;
            root.Add(_targetLabel);

            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.marginBottom  = 3;
            _kindDropdown = new DropdownField("種類", new List<string>(), -1);
            _kindDropdown.style.flexGrow = 1;
            _kindDropdown.style.flexShrink = 1;
            _kindDropdown.style.minWidth = 0;
            // 見出しの既定の幅だと、隣の「追加」が右へはみ出す。
            _kindDropdown.labelElement.style.minWidth = 0;
            _kindDropdown.labelElement.style.width = StyleKeyword.Auto;
            addRow.Add(_kindDropdown);
            _btnAdd = MkBtn("追加", OnAdd);
            _btnAdd.style.width = 50;
            addRow.Add(_btnAdd);
            root.Add(addRow);

            _listView = new ListView(_labels, 20,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _labels.Count) return;
                    l.text = _labels[i];
                    l.style.color = new StyleColor(StateColor(i < _items.Count ? _items[i] : null));
                });
            _listView.selectionType   = SelectionType.Single;
            _listView.style.minHeight = 60;
            _listView.style.maxHeight = 160;
            _listView.style.marginBottom = 3;
            _listView.selectionChanged += _ =>
            {
                if (_suppress) return;
                int i = _listView.selectedIndex;
                _selectedItemId = i >= 0 && i < _items.Count ? _items[i].ItemId : "";
                UpdateItem();
            };
            root.Add(_listView);

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            _btnUp   = MkBtn("↑", OnUp);     _btnUp.style.width = 30;   opRow.Add(_btnUp);
            _btnDown = MkBtn("↓", OnDown);   _btnDown.style.width = 30; opRow.Add(_btnDown);
            _enabledToggle = new Toggle("有効");
            _enabledToggle.style.flexGrow = 1;
            _enabledToggle.style.marginLeft = 6;
            _enabledToggle.RegisterValueChangedCallback(e => OnEnabled(e.newValue));
            opRow.Add(_enabledToggle);
            _btnRemove = MkBtn("削除", OnRemove); _btnRemove.style.width = 50; opRow.Add(_btnRemove);
            root.Add(opRow);

            _btnApply = MkBtn("適用（結果を普通のメッシュにする）", OnApply);
            _btnApply.style.marginBottom = 3;
            root.Add(_btnApply);

            _itemStateLabel = new Label();
            _itemStateLabel.style.whiteSpace = WhiteSpace.Normal;
            _itemStateLabel.style.fontSize   = 10;
            _itemStateLabel.style.marginBottom = 3;
            root.Add(_itemStateLabel);

            root.Add(SecLabel("引数"));
            _argContainer = new VisualElement();
            _argContainer.style.marginBottom = 3;
            root.Add(_argContainer);
            _builder = new ParamFieldBuilder(_argControls)
            {
                OnPreview = (k, v) => SendArgs(new[] { k }, new[] { v }, preview: true),
                OnCommit  = (k, v) => SendArgs(new[] { k }, new[] { v }, preview: false),
            };

            root.Add(SecLabel("処理範囲"));
            _scopeKind = new RadioButtonGroup(null, new List<string> { "全体", "登録した選択", "パーツ選択辞書" });
            _scopeKind.RegisterValueChangedCallback(e => OnScopeKind(e.newValue));
            root.Add(_scopeKind);
            _scopePartsSet = new DropdownField("辞書", new List<string>(), -1);
            _scopePartsSet.RegisterValueChangedCallback(_ => OnScopePartsSet());
            root.Add(_scopePartsSet);
            _btnScopeFromSel = MkBtn("今の選択を処理範囲として登録", OnScopeFromSelection);
            _btnScopeFromSel.style.marginBottom = 3;
            root.Add(_btnScopeFromSel);
            _scopeInfo = new Label();
            _scopeInfo.style.whiteSpace = WhiteSpace.Normal;
            _scopeInfo.style.fontSize   = 10;
            _scopeInfo.style.marginBottom = 3;
            root.Add(_scopeInfo);

            _detailFold = new Foldout { text = "詳細（引数を文字列で書く）", value = false };
            _detailKey = new DropdownField("キー", new List<string>(), -1);
            _detailKey.RegisterValueChangedCallback(_ => UpdateDetailValue());
            _detailFold.Add(_detailKey);
            _detailValue = new TextField("値");
            _detailFold.Add(_detailValue);
            _btnDetailWrite = MkBtn("書く", OnDetailWrite);
            _detailFold.Add(_btnDetailWrite);
            root.Add(_detailFold);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 読み直し
        // ================================================================

        public void Refresh()
        {
            if (_listView == null) return;
            var model = CurrentModel;
            _activeIndex = model?.ActiveMeshIndex ?? -1;
            _chain = null;
            if (model != null && _activeIndex >= 0)
                foreach (var g in model.ModifierStacks)
                    if (g != null && (g.IsChain || g.IsSubdivision)
                        && (g.ChainSourceIndex == _activeIndex || g.ChainResultIndex == _activeIndex))
                    { _chain = g; break; }
            _isSubdiv = _chain != null && _chain.IsSubdivision;

            // 種類の選択肢（登録表。モデルに依らない）
            if (_kindIds.Count != MeshModifierRegistry.All.Count)
            {
                _kindIds.Clear();
                var names = new List<string>();
                foreach (var info in MeshModifierRegistry.All)
                {
                    _kindIds.Add(info.Kind);
                    names.Add($"{info.DisplayName}（{info.Kind}）");
                }
                _kindDropdown.choices = names;
                if (_kindDropdown.index < 0 && names.Count > 0) _kindDropdown.SetValueWithoutNotify(names[0]);
            }

            // 対象
            var active = model != null && _activeIndex >= 0 ? model.GetMesh(_activeIndex) : null;
            if (active == null)
                _targetLabel.text = "対象：編集対象のオブジェクトを選んでください";
            else if (_chain == null)
                _targetLabel.text = $"対象：{active.Name}（処理列なし。「追加」で新しく作ります）";
            else if (_isSubdiv)
                _targetLabel.text = $"対象：{_chain.ChainSourceName} → 子 {_chain.ChainResultName}（サブディビジョンのスタック {_chain.Name}）\n"
                                  + "回数の変更・削除・適用ができます。処理を足すには「適用」で子を普通のメッシュにしてから、子に足してください";
            else
                _targetLabel.text = $"対象：{_chain.ChainSourceName} → 結果 {_chain.ChainResultName}（処理列 {_chain.Name}）"
                                  + (string.IsNullOrEmpty(_chain.ChainResult) ? "" : $"\n評価：{_chain.ChainResult} {_chain.ChainMessage}");
            _btnAdd.SetEnabled(active != null && _kindIds.Count > 0 && !_isSubdiv);

            // 項目
            _items.Clear();
            _labels.Clear();
            if (_chain?.Steps != null)
                foreach (var st in _chain.Steps)
                {
                    if (st == null || !st.IsExecutable) continue;
                    // サブディビジョンのスタックは、つながりの項目（ItemId の入ったもの）だけを出す（段 8）。
                    if (_isSubdiv && (st.Action != SubdivisionAction || string.IsNullOrEmpty(st.ItemId))) continue;
                    _items.Add(st);
                    var info = MeshModifierRegistry.Find(_isSubdiv ? SubdivisionKind : st.Action);
                    string name = info != null ? info.DisplayName : st.Action;
                    string state = !st.Enabled ? "無効" : string.IsNullOrEmpty(st.State) ? "" : st.State;
                    _labels.Add($"{_items.Count}. {(st.Enabled ? "✓" : "－")} {name}{(state.Length > 0 ? $"  [{state}]" : "")}");
                }

            int sel = -1;
            if (_selectLastAfterRefresh && _items.Count > 0) sel = _items.Count - 1;
            else
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].ItemId == _selectedItemId) { sel = i; break; }
            if (sel < 0 && _items.Count > 0) sel = 0;
            _selectLastAfterRefresh = false;
            _selectedItemId = sel >= 0 ? _items[sel].ItemId : "";

            _suppress = true;
            try
            {
                _listView.Rebuild();
                if (sel >= 0) _listView.SetSelectionWithoutNotify(new[] { sel });
                else _listView.ClearSelection();
            }
            finally { _suppress = false; }

            UpdateItem();
        }

        private ModifierStackStepView SelectedItem()
        {
            foreach (var it in _items) if (it.ItemId == _selectedItemId) return it;
            return null;
        }

        private int SelectedPos()
        {
            for (int i = 0; i < _items.Count; i++) if (_items[i].ItemId == _selectedItemId) return i;
            return -1;
        }

        /// <summary>選んだ項目に合わせて、ボタン・状態・引数欄・処理範囲・詳細を合わせる。</summary>
        private void UpdateItem()
        {
            var it  = SelectedItem();
            int pos = SelectedPos();
            bool has = it != null;

            // 末尾固定の項目（段 8）は動かせず、その直前の項目は下へ動かせない。
            bool pinned     = has && IsPinned(it);
            bool nextPinned = has && pos + 1 < _items.Count && IsPinned(_items[pos + 1]);
            _btnUp.SetEnabled(has && pos > 0 && !pinned && !_isSubdiv);
            _btnDown.SetEnabled(has && pos < _items.Count - 1 && !pinned && !nextPinned && !_isSubdiv);
            _btnRemove.SetEnabled(has);
            _enabledToggle.SetEnabled(has && !_isSubdiv);
            _enabledToggle.SetValueWithoutNotify(has && it.Enabled);
            _btnApply.SetEnabled(_chain != null);

            if (!has)
            {
                _itemStateLabel.text = _chain == null ? "" : "項目を選んでください";
                _specs.Clear();
                RebuildArgs("");
                UpdateScope(null);
                UpdateDetailKeys(null);
                return;
            }

            if (_isSubdiv)
            {
                _itemStateLabel.text = "状態：サブディビジョンのスタック（subdivideMesh）。親の編集・ドラッグに追随する";
                _specs.Clear();
                var sinfo = MeshModifierRegistry.Find(SubdivisionKind);
                if (sinfo != null)
                    foreach (var p in sinfo.Params)
                        _specs.Add(new ParamFieldSpec { Key = p.Key, Type = p.Type, Attr = p.Attr, Value = ValueOf(it, p.Key) });
                RebuildArgs(ParamFieldBuilder.Signature($"{_chain?.Name}|{it.ItemId}|{SubdivisionAction}", _specs));
                UpdateScope(null);
                UpdateDetailKeys(null);
                return;
            }

            string st = !it.Enabled ? "無効（流さない）" : string.IsNullOrEmpty(it.State) ? "未評価" : it.State;
            _itemStateLabel.text = $"状態：{st}" + (string.IsNullOrEmpty(it.StateMessage) ? "" : $"　{it.StateMessage}");

            // 引数欄の宣言は種類の登録表から、値は写しから。
            _specs.Clear();
            var info = MeshModifierRegistry.Find(it.Action);
            if (info != null)
            {
                var meshChoices = BuildMeshChoices();
                foreach (var p in info.Params)
                    _specs.Add(new ParamFieldSpec
                    {
                        Key = p.Key, Type = p.Type, Attr = p.Attr, Value = ValueOf(it, p.Key),
                        MeshChoices = p.Attr.IsMeshRef ? meshChoices : null,
                    });
            }
            RebuildArgs(ParamFieldBuilder.Signature($"{_chain?.Name}|{it.ItemId}|{it.Action}", _specs));
            UpdateScope(it);
            UpdateDetailKeys(it);
        }

        private void RebuildArgs(string sig)
        {
            if (sig == _argSig && sig.Length > 0) { _builder.UpdateValues(_specs); return; }
            _argSig = sig;
            _argControls.Begin("");
            _builder.Build(_argContainer, _specs);
            if (_specs.Count > 0 && _argContainer.childCount == 0)
            {
                _argContainer.Add(new Label("（引数欄の無い引数は「詳細」の文字列欄で書けます）") { style = { fontSize = 10 } });
                PlayerLayoutRoot.ApplyDarkTheme(_argContainer);
            }
        }

        /// <summary>IsMeshRef の欄の選択肢。元メッシュと結果は指せないので外す。</summary>
        private List<KeyValuePair<string, int>> BuildMeshChoices()
        {
            var list = new List<KeyValuePair<string, int>>();
            var model = CurrentModel;
            if (model?.DrawableList == null) return list;
            foreach (var m in model.DrawableList)
            {
                if (m == null) continue;
                if (_chain != null && (m.MasterIndex == _chain.ChainSourceIndex || m.MasterIndex == _chain.ChainResultIndex)) continue;
                list.Add(new KeyValuePair<string, int>(m.Name, m.MasterIndex));
            }
            return list;
        }

        private static string ValueOf(ModifierStackStepView it, string key)
        {
            if (it?.Keys == null) return "";
            for (int k = 0; k < it.Keys.Length; k++)
                if (it.Keys[k] == key) return k < it.Values.Length ? it.Values[k] ?? "" : "";
            return "";
        }

        // ── 処理範囲 ───────────────────────────────────────────────

        private void UpdateScope(ModifierStackStepView it)
        {
            bool has = it != null;
            _scopeKind.SetEnabled(has);
            _btnScopeFromSel.SetEnabled(has);

            // 辞書の選択肢は元メッシュの辞書。
            var names = new List<string>();
            var src = _chain != null ? CurrentModel?.GetMesh(_chain.ChainSourceIndex) : null;
            if (src?.PartsSelectionSets != null)
                foreach (var ps in src.PartsSelectionSets) if (ps != null) names.Add(ps.Name);
            _scopePartsSet.choices = names;
            _scopePartsSet.SetEnabled(has && names.Count > 0);

            if (!has) { _scopeInfo.text = ""; _scopeKind.SetValueWithoutNotify(-1); return; }

            string kind = ValueOf(it, MeshModifierScope.KeyKind);
            int ki = 0;
            if (PanelCommandFactory.TryParseValue(kind, typeof(MeshModifierScopeKind), out object kv, out _))
                ki = (MeshModifierScopeKind)kv switch
                {
                    MeshModifierScopeKind.Ids      => 1,
                    MeshModifierScopeKind.PartsSet => 2,
                    _                              => 0,
                };
            _scopeKind.SetValueWithoutNotify(ki);

            string ps0 = ValueOf(it, MeshModifierScope.KeyPartsSet);
            _scopePartsSet.SetValueWithoutNotify(names.Contains(ps0) ? ps0 : (names.Count > 0 && ki == 2 ? ps0 : ""));

            _scopeInfo.text = ki switch
            {
                1 => $"登録した選択：頂点 {CountOf(it, MeshModifierScope.KeyVertexIds)}・辺 {CountOf(it, MeshModifierScope.KeyEdgeVertexIds) / 2}・面 {CountOf(it, MeshModifierScope.KeyFaceSizes)}",
                2 => $"パーツ選択辞書「{ps0}」",
                _ => "全体",
            };
        }

        private static int CountOf(ModifierStackStepView it, string key)
        {
            string v = ValueOf(it, key);
            if (string.IsNullOrEmpty(v)) return 0;
            return PanelCommandFactory.TryParseValue(v, typeof(int[]), out object o, out _) ? ((int[])o).Length : 0;
        }

        private void OnScopeKind(int index)
        {
            var it = SelectedItem();
            if (it == null) return;
            switch (index)
            {
                case 0:
                    SendArgs(new[] { MeshModifierScope.KeyKind }, new[] { Fmt(MeshModifierScopeKind.All) }, false);
                    break;
                case 1:
                    // ID の組は選択から作る。今の選択を登録する。
                    OnScopeFromSelection();
                    break;
                case 2:
                {
                    string name = _scopePartsSet.value;
                    if (string.IsNullOrEmpty(name) && _scopePartsSet.choices.Count > 0) name = _scopePartsSet.choices[0];
                    if (string.IsNullOrEmpty(name)) { SetStatus("元メッシュにパーツ選択辞書がありません"); Refresh(); return; }
                    SendArgs(new[] { MeshModifierScope.KeyKind, MeshModifierScope.KeyPartsSet },
                             new[] { Fmt(MeshModifierScopeKind.PartsSet), name }, false);
                    break;
                }
            }
        }

        private void OnScopePartsSet()
        {
            if (SelectedItem() == null) return;
            string name = _scopePartsSet.value;
            if (string.IsNullOrEmpty(name)) return;
            SendArgs(new[] { MeshModifierScope.KeyKind, MeshModifierScope.KeyPartsSet },
                     new[] { Fmt(MeshModifierScopeKind.PartsSet), name }, false);
        }

        private void OnScopeFromSelection()
        {
            if (SelectedItem() == null || _chain == null) return;
            var sv = CurrentModel?.BuildModifierScopeFromSelection(_chain.ChainSourceIndex);
            if (sv == null) { SetStatus("元メッシュの選択を読めません"); Refresh(); return; }
            if (sv.VertexCount + sv.EdgeCount + sv.FaceCount == 0)
            {
                SetStatus(sv.NoId > 0
                    ? $"選んだ要素 {sv.NoId} 件に頂点 ID がありません。範囲は頂点 ID で持つので、元メッシュに頂点 ID を振ってから登録してください"
                    : "元メッシュで頂点・辺・面を選んでから押してください");
                Refresh();
                return;
            }

            // 範囲のキーはすべて送る（前の組の残りを重ねないため）。
            var vals = new string[ScopeKeys.Length];
            for (int i = 0; i < ScopeKeys.Length; i++)
            {
                vals[i] = "";
                for (int k = 0; k < sv.Keys.Length; k++) if (sv.Keys[k] == ScopeKeys[i]) { vals[i] = sv.Values[k]; break; }
            }
            SendArgs(ScopeKeys, vals, false);
            if (sv.NoId > 0) SetStatus($"頂点 ID の無い要素 {sv.NoId} 件は範囲に入れませんでした");
        }

        // ── 詳細（文字列） ─────────────────────────────────────────

        private void UpdateDetailKeys(ModifierStackStepView it)
        {
            string prev = _detailKey.value;
            var keys = new List<string>();
            var info = it != null ? MeshModifierRegistry.Find(it.Action) : null;
            if (info != null) foreach (var p in info.Params) keys.Add(p.Key);
            if (it != null) keys.AddRange(ScopeKeys);
            _detailKey.choices = keys;
            _detailKey.SetValueWithoutNotify(keys.Contains(prev) ? prev : (keys.Count > 0 ? keys[0] : ""));
            _btnDetailWrite.SetEnabled(keys.Count > 0);
            UpdateDetailValue();
        }

        private void UpdateDetailValue()
        {
            _detailValue.SetValueWithoutNotify(ValueOf(SelectedItem(), _detailKey.value));
        }

        private void OnDetailWrite()
        {
            string key = _detailKey.value;
            if (SelectedItem() == null || string.IsNullOrEmpty(key)) { SetStatus("項目とキーを選んでください"); return; }
            SendArgs(new[] { key }, new[] { _detailValue.value ?? "" }, false);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void SendArgs(string[] keys, string[] values, bool preview)
        {
            var it = SelectedItem();
            if (it == null || _chain == null) return;
            if (_isSubdiv)
            {
                // サブディビジョンのスタック（段 8）：回数の確定だけを、既存スタックの引数の書き換え
                // （setModifierStackStepArgs。項目を作り直す。モディファイアスタックのパネルと同じ）で送る。
                // SubdivideMeshCommand に子を渡す形は、子がスタックの出力なので受け口の保護（ModifierOutputGuard）が拒否する。
                // 子の作り直しは重いので、スライダーを動かしている間は送らない（既存スタックのパネルと同じ）。
                if (preview) return;
                int si = -1;
                for (int i = 0; i < (_chain.Steps?.Length ?? 0); i++)
                    if (ReferenceEquals(_chain.Steps[i], it)) { si = i; break; }
                if (si < 0) { SetStatus("項目が見つかりません"); Refresh(); return; }
                SendCmd(new SetModifierStackStepArgsCommand(ModelIndex, _chain.Name, si, keys, values));
                SetStatus($"{string.Join(", ", keys)} を書きました");
                Refresh();
                return;
            }
            SendCmd(new SetMeshModifierArgsCommand(ModelIndex, _chain.Name, it.ItemId, keys, values, preview));
            if (!preview) SetStatus($"{string.Join(", ", keys)} を書きました");
            Refresh();
        }

        private void OnAdd()
        {
            int ki = _kindDropdown.index;
            if (ki < 0 || ki >= _kindIds.Count) { SetStatus("種類を選んでください"); return; }
            if (_chain != null)
                SendCmd(new AddMeshModifierCommand(ModelIndex, _kindIds[ki], stackName: _chain.Name));
            else if (_activeIndex >= 0)
                SendCmd(new AddMeshModifierCommand(ModelIndex, _kindIds[ki], stackName: null, sourceMasterIndex: _activeIndex));
            else { SetStatus("編集対象のオブジェクトを選んでください"); return; }
            SetStatus($"{_kindIds[ki]} を足しました");
            _selectLastAfterRefresh = true;
            Refresh();
        }

        private void OnUp()
        {
            int pos = SelectedPos();
            if (_chain == null || pos <= 0 || _isSubdiv) return;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, _items[pos].ItemId, _items[pos - 1].ItemId));
            Refresh();
        }

        private void OnDown()
        {
            int pos = SelectedPos();
            if (_chain == null || pos < 0 || pos >= _items.Count - 1 || _isSubdiv) return;
            string before = pos + 2 < _items.Count ? _items[pos + 2].ItemId : null;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, _items[pos].ItemId, before));
            Refresh();
        }

        private void OnEnabled(bool on)
        {
            var it = SelectedItem();
            if (_chain == null || it == null || it.Enabled == on || _isSubdiv) return;
            SendCmd(new SetMeshModifierEnabledCommand(ModelIndex, _chain.Name, it.ItemId, on));
            Refresh();
        }

        private void OnRemove()
        {
            var it = SelectedItem();
            if (_chain == null || it == null) return;
            if (_isSubdiv)
            {
                // サブディビジョンのスタック（段 8）：つながりを外し、子を消す。
                SendCmd(new ReleaseSubdivisionCommand(ModelIndex, _chain.ChainResultIndex, deleteChild: true));
                SetStatus("サブディビジョンを外し、子を消しました");
                Refresh();
                return;
            }
            int pos = SelectedPos();
            // 消した後は 1 つ上（先頭なら次）を選ぶ。
            _selectedItemId = pos > 0 ? _items[pos - 1].ItemId : (_items.Count > 1 ? _items[1].ItemId : "");
            SendCmd(new RemoveMeshModifierCommand(ModelIndex, _chain.Name, it.ItemId));
            SetStatus("項目を消しました");
            Refresh();
        }

        private void OnApply()
        {
            if (_chain == null) return;
            if (_isSubdiv)
            {
                // サブディビジョンのスタック（段 8）：つながりを外し、子を普通のメッシュとして残す。
                SendCmd(new ReleaseSubdivisionCommand(ModelIndex, _chain.ChainResultIndex, deleteChild: false));
                SetStatus("サブディビジョンを適用しました（子を普通のメッシュとして残す）");
                Refresh();
                return;
            }
            // 送ると通知で Refresh が走り _chain は null になる。名前は先に控える。
            string name = _chain.Name;
            SendCmd(new ApplyMeshModifierChainCommand(ModelIndex, name));
            SetStatus($"処理列 {name} を適用しました");
            Refresh();
        }

        // ================================================================
        // 小物
        // ================================================================

        private void SetStatus(string text) { if (_statusLabel != null) _statusLabel.text = text ?? ""; }

        /// <summary>末尾固定の種類（段 8）の項目か。</summary>
        private bool IsPinned(ModifierStackStepView it)
            => !_isSubdiv && it != null && (MeshModifierRegistry.Find(it.Action)?.PinnedLast ?? false);

        private static string Fmt(object v)
            => PanelCommandFactory.TryFormatValue(v, out string s) ? s : Convert.ToString(v, CultureInfo.InvariantCulture);

        private static Color StateColor(ModifierStackStepView it)
        {
            // 無効は行の文字（「－」と [無効]）で示す。灰色にすると選択行の背景に埋もれる。
            if (it == null || !it.Enabled) return Color.white;
            switch (it.State)
            {
                case "Failed":  return new Color(1f, 0.45f, 0.4f);
                case "Blocked": return new Color(1f, 0.78f, 0.35f);
                default:        return Color.white;
            }
        }

        private static Button MkBtn(string t, Action a)
        {
            var b = new Button(a) { text = t };
            b.style.height = 22;
            return b;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginBottom = 3;
            l.style.marginTop = 2;
            return l;
        }
    }
}
