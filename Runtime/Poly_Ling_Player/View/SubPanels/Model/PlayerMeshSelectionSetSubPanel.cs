// PlayerMeshSelectionSetSubPanel.cs
// MeshSelectionSetPanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.View;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMeshSelectionSetSubPanel
    {
        public Func<ProjectContext>   GetView;
        public Action<PanelCommand> SendCommand;

        private enum TabType { Drawable, Bone, Morph }
        private TabType _currentTab = TabType.Drawable;

        private Button      _tabDrawable, _tabBone, _tabMorph;
        private TextField   _filterField;
        private readonly string[] _filterTexts = new string[3]; // Drawable/Bone/Morph
        private Label       _warningLabel;
        private TextField   _setNameField;
        private ListView    _setListView;
        private Button      _btnApply, _btnAddSet, _btnRename, _btnDelete;
        private Button      _btnSaveDic, _btnLoadDic;
        private TextField   _dicPathField;
        private Label       _statusLabel;

        private const string DicPathKey = "MeshSet.DicPath";

        private int _selectedSetIndex = -1;
        private readonly List<string> _setNames = new List<string>();

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;
        private ModelContext CurrentModel => GetView?.Invoke()?.CurrentModel;

        private MeshCategory CurrentCategory => _currentTab switch
        {
            TabType.Bone  => MeshCategory.Bone,
            TabType.Morph => MeshCategory.Morph,
            _             => MeshCategory.Drawable,
        };

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("オブジェクト選択辞書"));

            // タブ行
            var tabRow = new VisualElement(); tabRow.style.flexDirection = FlexDirection.Row; tabRow.style.marginBottom = 4;
            _tabDrawable = MkTab("Drawable", () => SwitchTab(TabType.Drawable));
            _tabBone     = MkTab("Bone",     () => SwitchTab(TabType.Bone));
            _tabMorph    = MkTab("Morph",    () => SwitchTab(TabType.Morph));
            tabRow.Add(_tabDrawable); tabRow.Add(_tabBone); tabRow.Add(_tabMorph);
            root.Add(tabRow);
            UpdateTabColors();

            // フィルタ行
            var filterRow = new VisualElement(); filterRow.style.flexDirection = FlexDirection.Row; filterRow.style.marginBottom = 4;
            _filterField = new TextField(); _filterField.style.flexGrow = 1;
            _filterField.tooltip = "メッシュ名でフィルタ";
            _filterField.RegisterValueChangedCallback(e =>
            {
                int ti = (int)_currentTab; _filterTexts[ti] = e.newValue ?? "";
                _setListView?.RefreshItems();
            });
            var btnClearFilter = new Button(() => { _filterField.SetValueWithoutNotify(""); _filterTexts[(int)_currentTab] = ""; _setListView?.RefreshItems(); }) { text = "×" };
            btnClearFilter.style.width = 22;
            filterRow.Add(_filterField); filterRow.Add(btnClearFilter);
            root.Add(filterRow);

            _warningLabel = new Label();
            _warningLabel.style.color   = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display = DisplayStyle.None;
            root.Add(_warningLabel);

            // 名前 + 辞書化
            var saveRow = new VisualElement(); saveRow.style.flexDirection = FlexDirection.Row; saveRow.style.marginBottom = 4;
            _setNameField = new TextField(); _setNameField.style.flexGrow = 1;
            var btnSave = new Button(OnSaveSet) { text = "辞書化" }; btnSave.style.width = 52;
            saveRow.Add(_setNameField); saveRow.Add(btnSave);
            root.Add(saveRow);

            // 辞書リスト
            _setListView = new ListView(_setNames, 22,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) => { if (e is Label l && i < _setNames.Count) l.text = $"[{i}] {_setNames[i]}"; });
            _setListView.selectionType   = SelectionType.Single;
            _setListView.style.minHeight = 60; _setListView.style.maxHeight = 140; _setListView.style.marginBottom = 4;
            _setListView.selectionChanged += _ => { _selectedSetIndex = _setListView.selectedIndex; UpdateButtonStates(); };
            root.Add(_setListView);

            // 操作ボタン
            var op1 = new VisualElement(); op1.style.flexDirection = FlexDirection.Row; op1.style.marginBottom = 3;
            _btnApply  = MkBtn("適用",    OnApplySet);  _btnApply.style.flexGrow = 1;  op1.Add(_btnApply);
            _btnAddSet = MkBtn("追加",    OnAddSet);    _btnAddSet.style.flexGrow = 1;  op1.Add(_btnAddSet);
            _btnRename = MkBtn("名前変更",OnRenameSet); _btnRename.style.flexGrow = 1; op1.Add(_btnRename);
            _btnDelete = MkBtn("削除",    OnDeleteSet); _btnDelete.style.flexGrow = 1; op1.Add(_btnDelete);
            root.Add(op1);

            // 辞書ファイル（エクスポート / 追加インポート）— PlayerIoUiKit 準拠
            //
            // 辞書そのものはプロジェクト側に保存済みなので、ここは保存/読込ではなく
            // モデル間で辞書を持ち回るための受け渡し操作。既定の場所は
            // partsDictionary/meshselsets.csv（パーツ選択辞書と同じフォルダ）。
            //
            // 取り込みは MeshSelSetCsvHelper.LoadFromFile が常に追記（同名は
            // ユニーク名を生成）するため、置換ではなく「追加インポート」と表記する。
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("辞書ファイル (CSV)"));

            _dicPathField = new TextField();
            _dicPathField.tooltip = "オブジェクト選択辞書の CSV ファイル";
            _dicPathField.RegisterValueChangedCallback(e => RecentPaths.Set(DicPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_dicPathField, OnBrowseDicFile));
            _dicPathField.SetValueWithoutNotify(ResolveDicPath());

            _btnSaveDic = PlayerIoUiKit.WideBtn("エクスポート",   OnSaveDicFile); root.Add(_btnSaveDic);
            _btnLoadDic = PlayerIoUiKit.WideBtn("追加インポート", OnLoadDicFile); root.Add(_btnLoadDic);

            _statusLabel = PlayerIoUiKit.StatusLabel();
            root.Add(_statusLabel);

            UpdateButtonStates();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var model = CurrentModel;
            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            _setNames.Clear();
            string filterText = (_filterTexts[(int)_currentTab] ?? "").ToLower();
            var sets = model.MeshSelectionSets;
            if (sets != null)
                foreach (var s in sets)
                {
                    string n = s.Name ?? "";
                    if (string.IsNullOrEmpty(filterText) || n.ToLower().Contains(filterText))
                        _setNames.Add(n);
                }

            _setListView.itemsSource = _setNames;
            _setListView.Rebuild();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setNames.Count - 1);
            if (_selectedSetIndex >= 0) _setListView.SetSelection(_selectedSetIndex);
            UpdateButtonStates();
        }

        // ── Tab ──────────────────────────────────────────────────────────
        private void SwitchTab(TabType tab)
        {
            _currentTab = tab;
            UpdateTabColors();
            string restored = _filterTexts[(int)tab] ?? "";
            _filterField?.SetValueWithoutNotify(restored);
            Refresh();
        }
        private void UpdateTabColors()
        {
            // active に Color.white、非 active に StyleKeyword.Null を入れると、
            // ApplyDarkTheme が入れた白文字がどちらの背景でも読めなくなる
            // （Null はインライン背景を外すだけで USS 既定の明るい灰色に戻る）。
            var active   = PlayerLayoutRoot.BtnActiveColor;
            var inactive = PlayerLayoutRoot.BtnInactiveColor;
            if (_tabDrawable != null) _tabDrawable.style.backgroundColor = _currentTab == TabType.Drawable ? active : inactive;
            if (_tabBone     != null) _tabBone.style.backgroundColor     = _currentTab == TabType.Bone     ? active : inactive;
            if (_tabMorph    != null) _tabMorph.style.backgroundColor    = _currentTab == TabType.Morph    ? active : inactive;
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSaveSet()
        {
            var model = CurrentModel; if (model == null) return;
            var view  = GetView?.Invoke(); if (view == null) return;

            int[] selectedIndices = GetSelectedMeshIndices(model);
            if (selectedIndices.Length == 0) { SetStatus("選択なし"); return; }

            var liveModel = new LiveModelView(model);
            IReadOnlyList<IMeshView> source = _currentTab switch
            {
                TabType.Bone  => liveModel.BoneList,
                TabType.Morph => liveModel.MorphList,
                _             => liveModel.DrawableList,
            };
            if (source == null) return;
            var selectedSet = new HashSet<int>(selectedIndices);
            var names = source
                .Where(mv => selectedSet.Contains(mv.MasterIndex) && !string.IsNullOrEmpty(mv.Name))
                .Select(mv => mv.Name).Distinct().ToArray();
            if (names.Length == 0) { SetStatus("選択メッシュに名前なし"); return; }

            string setName = _setNameField?.value?.Trim() ?? "";
            SendCmd(new SaveSelectionDictionaryCommand(ModelIndex, CurrentCategory, setName, names));
            _setNameField?.SetValueWithoutNotify(""); SetStatus($"辞書化: {names.Length} メッシュ");
        }

        private void OnApplySet()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, _selectedSetIndex, addToExisting: false));
            SetStatus("辞書を選択に適用");
        }

        private void OnAddSet()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, _selectedSetIndex, addToExisting: true));
            SetStatus("辞書を選択に追加");
        }

        private void OnDeleteSet()
        {
            var sets = CurrentModel?.MeshSelectionSets;
            if (sets == null || _selectedSetIndex < 0 || _selectedSetIndex >= sets.Count) return;
            string name = sets[_selectedSetIndex].Name;
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"'{name}' を削除しますか？", "削除", "キャンセル");
            if (!ok) return;
            SendCmd(new DeleteSelectionDictionaryCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1; SetStatus($"削除: {name}");
        }

        private void OnRenameSet()
        {
            if (_selectedSetIndex < 0) return;
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("新しい名前を入力してください"); return; }
            SendCmd(new RenameSelectionDictionaryCommand(ModelIndex, _selectedSetIndex, newName));
            _setNameField?.SetValueWithoutNotify(""); SetStatus($"名前変更 → {newName}");
        }

        /// <summary>
        /// 辞書ファイルのパス。手入力があればそれを使い、無ければ
        /// partsDictionary/meshselsets.csv を既定にする。
        /// </summary>
        private string ResolveDicPath()
        {
            string saved = RecentPaths.Get(DicPathKey);
            if (!string.IsNullOrEmpty(saved)) return saved;
            return PartsDictionaryPath.ResolveMeshSelSetsFile();
        }

        /// <summary>[...] は読込用。書き出し先はエクスポートボタン側で保存ダイアログを出す。</summary>
        private void OnBrowseDicFile()
        {
            string cur = _dicPathField?.value ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveDicPath();
            string path = PlayerIoUiKit.AskLoadPath("辞書ファイルの読込", cur, "csv");
            if (!string.IsNullOrEmpty(path)) _dicPathField.value = path;
        }

        private void OnSaveDicFile()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            int setCount = model.MeshSelectionSets?.Count ?? 0;
            if (setCount == 0) { SetStatus("辞書が空です"); return; }

            // パス欄は読込用。書き出しは毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
            string cur = _dicPathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveDicPath();

            string path = PlayerIoUiKit.AskSavePath(
                "辞書ファイルの書き出し", cur, PartsDictionaryPath.MeshSelSetsFileName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            _dicPathField.value = path;

            var since = DateTime.Now.AddSeconds(-2);
            SendCmd(new SaveMeshSelSetsCsvCommand(ModelIndex, path));

            // Dispatch は同期のため、書き出し結果をここで確認する。
            bool ok = System.IO.File.Exists(path) && System.IO.File.GetLastWriteTime(path) >= since;
            SetStatus(ok ? $"エクスポートしました: {setCount} 件 → {path}" : "エクスポート失敗（ログを参照）");
        }

        private void OnLoadDicFile()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            // 読込は必ずダイアログを通す。パス欄の値（無ければ既定の辞書ファイル）は
            // 初期フォルダ／初期ファイル名としてだけ使う。
            string cur = _dicPathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveDicPath();

            string path = PlayerIoUiKit.AskLoadPath("辞書ファイルの読込", cur, "csv");
            if (string.IsNullOrEmpty(path)) { SetStatus("取り込むファイルを指定してください"); return; }

            _dicPathField.value = path;
            if (!System.IO.File.Exists(path))
            {
                SetStatus($"ファイルが見つかりません: {System.IO.Path.GetFileName(path)}");
                return;
            }

            int before = model.MeshSelectionSets?.Count ?? 0;
            SendCmd(new LoadMeshSelSetsCsvCommand(ModelIndex, path));
            int after = CurrentModel?.MeshSelectionSets?.Count ?? before;

            if (after > before)
            {
                Refresh();
                SetStatus($"追加インポートしました: {after - before} 件");
            }
            else
            {
                SetStatus("追加インポート失敗（ログを参照）");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private int[] GetSelectedMeshIndices(ModelContext model)
        {
            return _currentTab switch
            {
                TabType.Bone  => model.SelectedBoneIndices?.ToArray()    ?? Array.Empty<int>(),
                TabType.Morph => model.SelectedMorphIndices?.ToArray()   ?? Array.Empty<int>(),
                _             => model.SelectedDrawableMeshIndices?.ToArray()    ?? Array.Empty<int>(),
            };
        }

        private void UpdateButtonStates()
        {
            bool hasSel = _selectedSetIndex >= 0;
            if (_btnApply  != null) _btnApply.SetEnabled(hasSel);
            if (_btnAddSet != null) _btnAddSet.SetEnabled(hasSel);
            if (_btnRename != null) _btnRename.SetEnabled(hasSel);
            if (_btnDelete != null) _btnDelete.SetEnabled(hasSel);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Button MkBtn(string t, Action a) { var b = new Button(a) { text = t }; b.style.height = 22; return b; }
        private static Button MkTab(string t, Action a) { var b = new Button(a) { text = t }; b.style.flexGrow = 1; b.style.height = 22; return b; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
