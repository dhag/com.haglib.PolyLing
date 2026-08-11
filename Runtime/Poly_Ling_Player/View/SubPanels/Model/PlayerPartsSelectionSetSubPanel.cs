// PlayerPartsSelectionSetSubPanel.cs
// PartsSelectionSetPanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerPartsSelectionSetSubPanel
    {
        public Func<ProjectContext>   GetView;
        public Action<PanelCommand> SendCommand;

        private Label       _warningLabel;
        private Label       _meshNameLabel;
        private Label       _currentSelLabel;
        private TextField   _setNameField;
        private ListView    _setListView;
        private Button      _btnLoad, _btnAdd, _btnSubtract, _btnDelete;
        private TextField   _csvFolderField;
        private Label       _dicFolderLabel;
        private Toggle      _useCustomFolderToggle;
        private VisualElement _customFolderRow;
        private Label       _statusLabel;

        // 「フォルダを直接指定」ON のときだけ使う手動パス。
        // OFF のときは PartsDictionaryPath が解決する partsDictionary を使う。
        private const string CsvFolderKey     = "PartsSet.CsvFolder";

        private int _selectedSetIndex = -1;
        private readonly List<string> _setNames = new List<string>();

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;
        private ProjectContext GetProject() => GetView?.Invoke();
        private MeshContext FirstSelectedMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMeshContext;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("パーツ選択辞書"));

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize    = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _currentSelLabel = new Label();
            _currentSelLabel.style.fontSize    = 10;
            _currentSelLabel.style.marginBottom = 4;
            root.Add(_currentSelLabel);

            // 名前フィールド + 辞書化ボタン
            var saveRow = new VisualElement(); saveRow.style.flexDirection = FlexDirection.Row; saveRow.style.marginBottom = 4;
            _setNameField = new TextField(); _setNameField.style.flexGrow = 1;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            var btnSave = new Button(OnSave) { text = "辞書化" }; btnSave.style.width = 52;
            saveRow.Add(_setNameField); saveRow.Add(btnSave);
            root.Add(saveRow);

            // 辞書リスト
            _setListView = new ListView(_setNames, 22, MakeItem, BindItem);
            _setListView.selectionType   = SelectionType.Single;
            _setListView.style.minHeight = 60; _setListView.style.maxHeight = 150;
            _setListView.style.marginBottom = 4;
            _setListView.selectionChanged += OnSetSelectionChanged;
            root.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement(); opRow.style.flexDirection = FlexDirection.Row; opRow.style.marginBottom = 4;
            _btnLoad     = MkBtn("呼出し",  OnLoad);
            _btnAdd      = MkBtn("追加",    OnAdd);
            _btnSubtract = MkBtn("除外",    OnSubtract);
            _btnDelete   = MkBtn("削除",    OnDelete);
            foreach (var b in new[] { _btnLoad, _btnAdd, _btnSubtract, _btnDelete }) { b.style.flexGrow = 1; opRow.Add(b); }
            root.Add(opRow);

            // 辞書ファイル（エクスポート / インポート）— PlayerIoUiKit 準拠
            //
            // 辞書そのものはプロジェクト側に保存済みなので、ここは保存/読込ではなく
            // オブジェクト間・モデル間で辞書を持ち回るための受け渡し操作。
            // 受け渡し先はプロジェクトフォルダ直下の partsDictionary に固定し、
            // フォルダ選択ダイアログを不要にする。
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("辞書ファイル (CSV)"));

            _dicFolderLabel = new Label();
            _dicFolderLabel.style.fontSize   = 9;
            _dicFolderLabel.style.whiteSpace = WhiteSpace.Normal;
            _dicFolderLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            _dicFolderLabel.style.marginBottom = 3;
            root.Add(_dicFolderLabel);

            // 別プロジェクトの辞書を取り込むときの退避路。既定は非表示。
            _useCustomFolderToggle = new Toggle("フォルダを直接指定");
            _useCustomFolderToggle.tooltip = "別プロジェクトの辞書を取り込む場合などに使う。"
                                           + "OFF のときは partsDictionary フォルダを使う。";
            _useCustomFolderToggle.style.marginBottom = 2;
            _useCustomFolderToggle.RegisterValueChangedCallback(e => ApplyCustomFolderVisibility(e.newValue));
            root.Add(_useCustomFolderToggle);

            _customFolderRow = new VisualElement();
            _csvFolderField = new TextField();
            _csvFolderField.tooltip = "Selected_*.csv を入出力するフォルダ";
            _csvFolderField.RegisterValueChangedCallback(e => RecentPaths.Set(CsvFolderKey, e.newValue));
            _customFolderRow.Add(PlayerIoUiKit.PathRow(_csvFolderField, OnBrowseCsvFolder));
            _csvFolderField.SetValueWithoutNotify(RecentPaths.Get(CsvFolderKey));
            root.Add(_customFolderRow);

            root.Add(PlayerIoUiKit.WideBtn("エクスポート", OnExport));
            root.Add(PlayerIoUiKit.Spacer());
            root.Add(PlayerIoUiKit.WideBtn("インポート（現在のオブジェクトへ）",   () => OnImport(false)));
            root.Add(PlayerIoUiKit.WideBtn("インポート（書込元のオブジェクトへ）", () => OnImport(true)));

            ApplyCustomFolderVisibility(false);
            UpdateDicFolderLabel();

            _statusLabel = PlayerIoUiKit.StatusLabel();
            root.Add(_statusLabel);

            UpdateButtonStates();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var mc = FirstSelectedMeshContext;

            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _warningLabel.style.display = DisplayStyle.None;
                _meshNameLabel.text = mc.Name ?? "(no name)";

                var parts = new List<string>();
                if (mc.SelectedVertices?.Count > 0) parts.Add($"V:{mc.SelectedVertices.Count}");
                if (mc.SelectedEdges?.Count   > 0) parts.Add($"E:{mc.SelectedEdges.Count}");
                if (mc.SelectedFaces?.Count   > 0) parts.Add($"F:{mc.SelectedFaces.Count}");
                _currentSelLabel.text = parts.Count > 0 ? string.Join("  ", parts) : "(選択なし)";
            }

            // 辞書リスト再構築
            _setNames.Clear();
            var sets = mc?.PartsSelectionSetList;
            if (sets != null) foreach (var s in sets) _setNames.Add(s.Name ?? "");
            _setListView.itemsSource = _setNames;
            _setListView.Rebuild();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setNames.Count - 1);
            if (_selectedSetIndex >= 0) _setListView.SetSelection(_selectedSetIndex);
            UpdateButtonStates();

            // プロジェクトを保存 / 読込した直後は解決先が変わるので追従させる。
            UpdateDicFolderLabel();
        }

        // ── ListView helpers ─────────────────────────────────────────────
        private VisualElement MakeItem()
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label(); lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft; lbl.style.paddingLeft = 4;
            var renameBtn = new Button { text = "✎" }; renameBtn.style.width = 22; renameBtn.style.height = 18;
            row.Add(lbl); row.Add(renameBtn);
            return row;
        }

        private void BindItem(VisualElement elem, int i)
        {
            if (i >= _setNames.Count) return;
            if (elem.Q<Label>() is Label l) l.text = $"[{i}] {_setNames[i]}";
            if (elem.Q<Button>() is Button b) { int ci = i; b.clicked += () => OnRenameAt(ci); }
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            _selectedSetIndex = _setListView.selectedIndex;
            UpdateButtonStates();
        }

        private void OnSave()
        {
            var mv = FirstSelectedMeshContext; if (mv == null) return;
            bool hasSel = (mv.SelectedVertices?.Count > 0) || (mv.SelectedEdges?.Count > 0)
                       || (mv.SelectedFaces?.Count > 0);
            if (!hasSel) { SetStatus("選択なし"); return; }
            SendCmd(new SavePartsSetCommand(ModelIndex, _setNameField?.value?.Trim() ?? ""));
            _setNameField?.SetValueWithoutNotify(""); SetStatus("辞書化しました");
        }

        private void OnLoad()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new LoadPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を適用しました");
        }

        private void OnAdd()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new AddPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を追加しました");
        }

        private void OnSubtract()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new SubtractPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を除外しました");
        }

        private void OnDelete()
        {
            if (_selectedSetIndex < 0) return;
            var sets = FirstSelectedMeshContext?.PartsSelectionSetList;
            string name = (sets != null && _selectedSetIndex < sets.Count) ? sets[_selectedSetIndex].Name : "?";
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"「{name}」を削除しますか？", "削除", "キャンセル");
            if (!ok) return;
            SendCmd(new DeletePartsSetCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1; SetStatus($"削除: {name}");
        }

        private void OnRenameAt(int index)
        {
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("名前フィールドに新しい名前を入力してください"); return; }
            SendCmd(new RenamePartsSetCommand(ModelIndex, index, newName));
            _setNameField?.SetValueWithoutNotify(""); SetStatus($"名前変更 → {newName}");
        }

        // ── 辞書ファイル（エクスポート / インポート）────────────────────────

        /// <summary>
        /// 実際に使うフォルダ。トグル ON なら手動指定、OFF なら partsDictionary。
        /// </summary>
        /// <param name="forWrite">true なら書き出し前提でフォルダを作成する。</param>
        private string ResolveDicFolder(bool forWrite)
        {
            if (_useCustomFolderToggle != null && _useCustomFolderToggle.value)
                return _csvFolderField?.value?.Trim() ?? "";

            return forWrite ? PartsDictionaryPath.ResolveForWrite() : PartsDictionaryPath.Resolve();
        }

        private void ApplyCustomFolderVisibility(bool useCustom)
        {
            if (_customFolderRow != null)
                _customFolderRow.style.display = useCustom ? DisplayStyle.Flex : DisplayStyle.None;
            if (_dicFolderLabel != null)
                _dicFolderLabel.style.display = useCustom ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateDicFolderLabel();
        }

        /// <summary>解決結果のフォルダ表示を更新する。プロジェクト保存後などに呼ぶ。</summary>
        private void UpdateDicFolderLabel()
        {
            if (_dicFolderLabel == null) return;
            string folder = PartsDictionaryPath.Resolve();
            _dicFolderLabel.text = PartsDictionaryPath.IsFallback()
                ? $"{folder}\n（プロジェクト未保存のため既定の場所を使用）"
                : folder;
        }

        private void OnBrowseCsvFolder()
        {
            string cur = _csvFolderField?.value ?? "";
            string dir = string.IsNullOrEmpty(cur) ? PartsDictionaryPath.Resolve() : cur;
            string path = PLEditorBridge.I.OpenFolderPanel("辞書フォルダ", dir, "");
            if (!string.IsNullOrEmpty(path)) _csvFolderField.value = path;
        }

        private void OnExport()
        {
            var targets = CollectTargets();
            if (targets.Count == 0) { SetStatus("オブジェクトが選択されていません"); return; }

            int setCount = 0;
            foreach (var t in targets) setCount += t.PartsSelectionSetList?.Count ?? 0;
            if (setCount == 0) { SetStatus("辞書が空です"); return; }

            string folder = ResolveDicFolder(forWrite: true);
            if (string.IsNullOrEmpty(folder)) { SetStatus("出力先フォルダを指定してください"); return; }

            var since = DateTime.Now.AddSeconds(-2);
            SendCmd(new ExportPartsSetsCsvCommand(ModelIndex, folder));

            // Dispatch は同期のため、書き出し結果をここで確認する。
            // 既存ファイルを数えないよう、更新時刻が実行直前以降のものだけを対象にする。
            if (!System.IO.Directory.Exists(folder)) { SetStatus("エクスポート失敗（ログを参照）"); return; }
            int written = 0;
            foreach (var f in System.IO.Directory.GetFiles(folder, "Selected_*.csv"))
                if (System.IO.File.GetLastWriteTime(f) >= since) written++;
            SetStatus(written > 0
                ? $"エクスポートしました: {targets.Count} オブジェクト / {written} 件 → {folder}"
                : "エクスポート失敗（ログを参照）");
        }

        /// <param name="byObjectName">
        /// true: ファイル内の書込元オブジェクト名と一致するオブジェクトへ取り込む。
        /// false: オブジェクト名を無視し、選択中の全オブジェクトへ同じ辞書を取り込む。
        /// </param>
        private void OnImport(bool byObjectName)
        {
            string folder = ResolveDicFolder(forWrite: false);
            if (string.IsNullOrEmpty(folder)) { SetStatus("取込元フォルダを指定してください"); return; }
            if (!System.IO.Directory.Exists(folder)) { SetStatus($"フォルダがありません: {folder}"); return; }

            int fileCount = System.IO.Directory.GetFiles(folder, "Selected_*.csv").Length;
            if (fileCount == 0) { SetStatus("Selected_*.csv がありません"); return; }

            var targets = byObjectName ? new List<MeshContext>() : CollectTargets();
            if (!byObjectName && targets.Count == 0) { SetStatus("オブジェクトが選択されていません"); return; }

            // 同名辞書は上書きのため件数では判定できない。内容の署名で変化を見る。
            string before = BuildSetsSignature();
            SendCmd(new ImportPartsSetCsvCommand(ModelIndex, folder, byObjectName));
            string after = BuildSetsSignature();

            if (after != before)
            {
                Refresh();
                SetStatus(byObjectName
                    ? $"インポートしました: {fileCount} ファイル（オブジェクト名一致分）"
                    : $"インポートしました: {fileCount} ファイル → {targets.Count} オブジェクト");
            }
            else
            {
                SetStatus("適用対象がありませんでした（ログを参照）");
            }
        }

        /// <summary>選択中の描画メッシュを列挙する。未選択時は編集対象メッシュ単体。</summary>
        private List<MeshContext> CollectTargets()
        {
            var list = new List<MeshContext>();
            var model = GetView?.Invoke()?.CurrentModel;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc != null) list.Add(mc);
            }
            if (list.Count == 0)
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) list.Add(mc);
            }
            return list;
        }

        /// <summary>モデル全体の辞書内容から比較用の署名を作る。</summary>
        private string BuildSetsSignature()
        {
            var model = GetView?.Invoke()?.CurrentModel;
            if (model?.MeshContextList == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var mc in model.MeshContextList)
            {
                if (mc?.PartsSelectionSetList == null) continue;
                sb.Append(mc.Name).Append(':');
                foreach (var s in mc.PartsSelectionSetList)
                    sb.Append(s.Name).Append('/')
                      .Append(s.Vertices.Count).Append(',')
                      .Append(s.Edges.Count).Append(',')
                      .Append(s.Faces.Count).Append(',')
                      .Append(s.Lines.Count).Append(';');
                sb.Append('|');
            }
            return sb.ToString();
        }

        private void UpdateButtonStates()
        {
            bool hasSel = _selectedSetIndex >= 0;
            if (_btnLoad     != null) _btnLoad.SetEnabled(hasSel);
            if (_btnAdd      != null) _btnAdd.SetEnabled(hasSel);
            if (_btnSubtract != null) _btnSubtract.SetEnabled(hasSel);
            if (_btnDelete   != null) _btnDelete.SetEnabled(hasSel);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Button MkBtn(string t, Action a) { var b = new Button(a) { text = t }; b.style.height = 22; return b; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
