// PlayerShapeSubPanel.cs
// 形（名前付きの頂点位置）のパネル。一覧と切り替え、直前の形との入れ替え、追加・複製・削除、
// 名前・英語名・パネルの変更、左右反転した形の作成、モデルとして取り出す、試し表示（段 7）。
// Runtime/Poly_Ling_Player/View/SubPanels/MorphBlend/ に配置
//
// 【PolyLing_モーフ整理計画.md 3.11・段 3】
//   読み取りは窓口（IModelView の Shapes・CurrentShapeId・PreviousShapeId）、書き込みはコマンド
//   （AddShape / DeleteShape / SetShapeAttributes / SetCurrentShape / SwapShape / MirrorShape /
//   ExtractShapeModel / PreviewShapes / EndShapePreview）だけ。
//   パネルはモデルのデータにも頂点にも触れない。
//
// 【一覧と今の形】
//   一覧の先頭の行は「基本の形」。行を選ぶと今の形がその形に切り替わる（選んでいる行＝今の形）。
//   絞り込みで今の形が一覧から外れているときは、どの行も選ばれていない状態になる。
//   名前・英語名・パネルの欄と、複製・左右反転・削除は、今の形（基本の形を除く）に対して働く。
//
// 【MQO】（段 4）
//   MQO から読み込む（今の形へ／新しい形を作って）・再読み込み・MQO へ書き出す。送るのは
//   ImportShapeMqo / ReloadShapeMqo / ExportShapeMqo だけ。パス欄に書いたパスをそのまま渡す。
//   「…」はファイルを選ぶ窓を開いてパス欄を埋めるだけ（UserOnly。ほかの読み込みパネルと同じ）。
//   結果（組・決まらない・頂点数が合わない）は GetLastMqoSummary で受けて状態の欄に出す。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerShapeSubPanel
    {
        /// <summary>プロジェクトの窓口。形の一覧と今の形を読む。</summary>
        public Func<IProjectView>   GetView;
        /// <summary>書き込みのコマンドの送り先。</summary>
        public Action<PanelCommand> SendCommand;

        private IModelView Model      => GetView?.Invoke()?.CurrentModel;
        private int        ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private const int BaseShapeId = 0;
        private const string BaseShapeLabel = "基本の形";
        private static readonly List<string> PanelChoices = new List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" };

        /// <summary>一覧の行。id が 0 の行は基本の形（name は空）。</summary>
        private readonly List<(int id, string name, string label)> _rows = new List<(int, string, string)>();

        /// <summary>作り直しの間は、一覧の選択と欄の変更をコマンドにしない。</summary>
        private bool _refreshing;

        // UI
        // UI 自動操作の ID は "shape.<下の Id>"（UiControlAttribute.cs）。
        // 名前の欄はフォーカスが外れたときに確定する作りなので、外からは確定まで行う Setter を通す。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label         _warningLabel;
        [UiControl("current", Safety = UiSafety.ReadOnly, Description = "今の形と直前の形の名前")]
        private Label         _currentLabel;
        [UiControl("filter", Description = "一覧の絞り込み（名前か英語名に含まれる文字。空で全部）")]
        private TextField     _filterField;
        [UiControl("list", Description = "形の一覧（一覧の行番号。先頭の行は基本の形）。行を選ぶと今の形が切り替わる")]
        private ListView      _listView;
        [UiControl("swap", Safety = UiSafety.SafeWrite, Description = "今の形と直前の形を入れ替える（見比べ用）")]
        private Button        _btnSwap;
        [UiControl("newName", Description = "新しく作る形の名前（追加・左右反転で使う）")]
        private TextField     _newNameField;
        [UiControl("addFromBase", Safety = UiSafety.SafeWrite, Description = "基本の形と同じ位置の形を足して、その形へ切り替える")]
        private Button        _btnAddFromBase;
        [UiControl("addFromCurrent", Safety = UiSafety.SafeWrite, Description = "今の見た目（今の形の位置）を新しい形として足して、その形へ切り替える")]
        private Button        _btnAddFromCurrent;
        [UiControl("duplicate", Safety = UiSafety.SafeWrite, Description = "今の形を複製する（名前は自動。切り替えない）")]
        private Button        _btnDuplicate;
        [UiControl("mirror", Safety = UiSafety.SafeWrite, Description = "今の形を左右反転した形を、新しい名前で作る（切り替えない）")]
        private Button        _btnMirror;
        [UiControl(Ignore = true)]
        private VisualElement _detail;
        [UiControl("name", Setter = nameof(SetNameByAutomation), Description = "今の形の名前")]
        private TextField     _nameField;
        [UiControl("nameEn", Setter = nameof(SetNameEnByAutomation), Description = "今の形の英語名")]
        private TextField     _nameEnField;
        [UiControl("panel", Description = "今の形のパネル（眉 / 目 / 口 / その他）")]
        private DropdownField _panelPopup;
        [UiControl("delete", Safety = UiSafety.Destructive, Description = "今の形を消す（Undo で戻せる）")]
        private Button        _btnDelete;
        [UiControl("extract", Safety = UiSafety.SafeWrite, Description = "今の形（基本の形を含む）の位置で描画オブジェクトを複製し、新しいモデルとして足す")]
        private Button        _btnExtract;

        // 試し表示（previewShapes / endShapePreview）
        [UiControl("previewShape", Description = "試し表示する形")]
        private DropdownField _previewShape;
        [UiControl("previewWeight", Description = "試し表示の重み。動かすと試し表示を始める（基本の形へ切り替わる）")]
        private Slider        _previewWeight;
        [UiControl("previewEnd", Safety = UiSafety.SafeWrite, Description = "試し表示を終える")]
        private Button        _btnPreviewEnd;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;

        // MQO（段 4）
        [UiControl("mqoPath", Description = "読み込む MQO のパス（作業フォルダからの相対でも絶対でもよい。読み込み元が無い形を書き出すときの元にする MQO にも使う）")]
        private TextField     _mqoPathField;
        [UiControl("mqoBrowse", Safety = UiSafety.UserOnly, Description = "[...]。ファイル選択ダイアログを開き、選んだファイルを MQO のパス欄へ入れる（読み込みはしない）")]
        private Button        _btnMqoBrowse;
        [UiControl("mqoScale", Description = "MQO 座標に掛ける倍率")]
        private FloatField    _mqoScaleField;
        [UiControl("mqoFlipX", Description = "X を反転する")]
        private Toggle        _mqoFlipXToggle;
        [UiControl("mqoFlipZ", Description = "Z を反転する")]
        private Toggle        _mqoFlipZToggle;
        [UiControl("mqoKeepOffsets", Description = "基本の形を読む・読み直すとき、形の差分を保つ（全部の形を同じだけ動かす）")]
        private Toggle        _mqoKeepOffsetsToggle;
        [UiControl("mqoImport", Safety = UiSafety.SafeWrite, Description = "MQO のパス欄のファイルを、今の形として読み込む（MQO に入っているオブジェクトだけ）")]
        private Button        _btnMqoImport;
        [UiControl("mqoImportNew", Safety = UiSafety.SafeWrite, Description = "新しい名前の欄の名前で形を作り、MQO のパス欄のファイルを読み込む")]
        private Button        _btnMqoImportNew;
        [UiControl("mqoSource", Safety = UiSafety.ReadOnly, Description = "今の形の読み込み元（MQO のパスと組の数）")]
        private Label         _mqoSourceLabel;
        [UiControl("mqoReload", Safety = UiSafety.SafeWrite, Description = "今の形を、覚えている読み込み元で読み直す（メタセコ側の修正を取り込む）")]
        private Button        _btnMqoReload;
        [UiControl("mqoExportPath", Description = "書き出し先の MQO のパス")]
        private TextField     _mqoExportPathField;
        [UiControl("mqoExportBrowse", Safety = UiSafety.UserOnly, Description = "[...]。保存ダイアログを開き、選んだファイルを書き出し先の欄へ入れる（書き出しはしない）")]
        private Button        _btnMqoExportBrowse;
        [UiControl("mqoExport", Safety = UiSafety.SafeWrite, Description = "今の形を、元にする MQO（読み込み元、無ければ MQO のパス欄）へ書き戻して、書き出し先の欄のファイルへ保存する")]
        private Button        _btnMqoExport;

        /// <summary>形と MQO の連携のコマンドの直近の結果の一文（PlayerCommandDispatcher.LastShapeMqoSummary）。</summary>
        public Func<string> GetLastMqoSummary;

        private const string MqoPathKey       = "Shape.Mqo.Path";
        private const string MqoExportPathKey = "Shape.Mqo.ExportPath";

        /// <summary>UI 自動操作から名前を設定する。フォーカスが外れたときと同じく確定まで行う。</summary>
        private string SetNameByAutomation(string value)
        {
            if (_nameField == null) return "名前の欄がありません";
            _nameField.value = value;
            OnDetailChanged();
            return null;
        }

        /// <summary>UI 自動操作から英語名を設定する。フォーカスが外れたときと同じく確定まで行う。</summary>
        private string SetNameEnByAutomation(string value)
        {
            if (_nameEnField == null) return "英語名の欄がありません";
            _nameEnField.value = value;
            OnDetailChanged();
            return null;
        }

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("形"));

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _currentLabel = new Label();
            _currentLabel.style.color        = new StyleColor(Color.white);
            _currentLabel.style.marginBottom = 4;
            _currentLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_currentLabel);

            _btnSwap = new Button(OnSwap) { text = "直前の形と入れ替え" };
            _btnSwap.style.marginBottom = 6;
            root.Add(_btnSwap);

            _filterField = new TextField("絞り込み");
            _filterField.style.marginBottom = 2;
            _filterField.RegisterValueChangedCallback(_ => { if (!_refreshing) Refresh(); });
            root.Add(_filterField);

            _listView = new ListView(_rows, 20, MakeRow, BindRow);
            _listView.selectionType      = SelectionType.Single;
            _listView.style.height       = 180;
            _listView.style.flexShrink   = 0;
            _listView.style.marginBottom = 6;
            _listView.selectionChanged  += OnListSelectionChanged;
            root.Add(_listView);

            // 追加・複製・左右反転
            root.Add(MakeSep());
            root.Add(SecLabel("追加"));

            _newNameField = new TextField("新しい名前");
            _newNameField.style.marginBottom = 3;
            root.Add(_newNameField);

            var addRow = new VisualElement(); addRow.style.flexDirection = FlexDirection.Row; addRow.style.marginBottom = 3;
            _btnAddFromBase    = new Button(OnAddFromBase)    { text = "基本の形から追加" };
            _btnAddFromBase.style.flexGrow = 1; _btnAddFromBase.style.marginRight = 4;
            _btnAddFromCurrent = new Button(OnAddFromCurrent) { text = "今の見た目を新しい形として追加" };
            _btnAddFromCurrent.style.flexGrow = 1;
            addRow.Add(_btnAddFromBase); addRow.Add(_btnAddFromCurrent);
            root.Add(addRow);

            var deriveRow = new VisualElement(); deriveRow.style.flexDirection = FlexDirection.Row; deriveRow.style.marginBottom = 6;
            _btnDuplicate = new Button(OnDuplicate) { text = "複製" };
            _btnDuplicate.style.flexGrow = 1; _btnDuplicate.style.marginRight = 4;
            _btnMirror    = new Button(OnMirror)    { text = "左右反転した形を作る" };
            _btnMirror.style.flexGrow = 1;
            deriveRow.Add(_btnDuplicate); deriveRow.Add(_btnMirror);
            root.Add(deriveRow);

            _btnExtract = new Button(OnExtract) { text = "今の形をモデルとして取り出す" };
            _btnExtract.style.marginBottom = 6;
            root.Add(_btnExtract);

            BuildPreviewSection(root);

            BuildMqoSection(root);

            // 今の形の名前・英語名・パネル・削除（基本の形のときは出さない）
            _detail = new VisualElement();
            _detail.style.display = DisplayStyle.None;
            root.Add(_detail);

            _detail.Add(MakeSep());
            _detail.Add(SecLabel("今の形"));

            _nameField = new TextField("名前");
            _nameField.RegisterCallback<FocusOutEvent>(_ => OnDetailChanged());
            _detail.Add(_nameField);

            _nameEnField = new TextField("英語名");
            _nameEnField.RegisterCallback<FocusOutEvent>(_ => OnDetailChanged());
            _detail.Add(_nameEnField);

            _panelPopup = new DropdownField("パネル", PanelChoices, 3);
            _panelPopup.style.color        = new StyleColor(Color.white);
            _panelPopup.style.marginBottom = 3;
            _panelPopup.RegisterValueChangedCallback(_ => OnDetailChanged());
            _detail.Add(_panelPopup);

            _btnDelete = new Button(OnDelete) { text = "この形を削除" };
            _btnDelete.style.marginBottom = 6;
            _detail.Add(_btnDelete);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.color      = new StyleColor(Color.white);
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_statusLabel);
        }

        private VisualElement MakeRow()
        {
            var l = new Label();
            l.style.paddingLeft    = 4;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.color          = new StyleColor(Color.white);
            return l;
        }

        private void BindRow(VisualElement e, int i)
        {
            if (e is Label l && i >= 0 && i < _rows.Count) l.text = _rows[i].label;
        }

        // ================================================================
        // 表示の更新
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var model = Model;
            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            _refreshing = true;
            try
            {
                var shapes   = model.Shapes;
                int current  = model.CurrentShapeId;
                int previous = model.PreviousShapeId;
                string filter = (_filterField?.value ?? "").Trim();

                _rows.Clear();
                if (Matches(BaseShapeLabel, "", filter))
                    _rows.Add((BaseShapeId, "", RowLabel(BaseShapeLabel, "", -1, previous == BaseShapeId && current != BaseShapeId)));
                ShapeView currentDef = null;
                for (int i = 0; i < shapes.Count; i++)
                {
                    var s = shapes[i];
                    if (s.Id == current) currentDef = s;
                    if (!Matches(s.Name, s.NameEnglish, filter)) continue;
                    // UV の形（PMX の UV モーフ）は名前の後ろに印を付ける。
                    string shown = s.IsUV ? s.Name + "（UV）" : s.Name;
                    _rows.Add((s.Id, s.Name, RowLabel(shown, s.NameEnglish, s.Panel, s.Id == previous && s.Id != current)));
                }
                _listView.RefreshItems();

                int selected = -1;
                for (int i = 0; i < _rows.Count; i++)
                    if (_rows[i].id == current) { selected = i; break; }
                if (selected >= 0) _listView.SetSelectionWithoutNotify(new[] { selected });
                else               _listView.SetSelectionWithoutNotify(new int[0]);

                string currentName  = currentDef != null ? currentDef.Name : BaseShapeLabel;
                string previousName = BaseShapeLabel;
                for (int i = 0; i < shapes.Count; i++)
                    if (shapes[i].Id == previous) { previousName = shapes[i].Name; break; }
                if (_currentLabel != null)
                    _currentLabel.text = $"今の形: {currentName}　／　直前の形: {previousName}　（形 {shapes.Count} 個）";

                if (_detail != null) _detail.style.display = currentDef != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (currentDef != null)
                {
                    _nameField?.SetValueWithoutNotify(currentDef.Name);
                    _nameEnField?.SetValueWithoutNotify(currentDef.NameEnglish);
                    _panelPopup?.SetValueWithoutNotify(PanelChoices[Mathf.Clamp(currentDef.Panel, 0, PanelChoices.Count - 1)]);
                }
                RefreshMqoSource();
                RefreshPreviewChoices(shapes);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static bool Matches(string name, string nameEnglish, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return (name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (nameEnglish ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string RowLabel(string name, string nameEnglish, int panel, bool isPrevious)
        {
            string text = name;
            if (!string.IsNullOrEmpty(nameEnglish)) text += $"  ({nameEnglish})";
            if (panel >= 0) text += $"  [{PanelChoices[Mathf.Clamp(panel, 0, PanelChoices.Count - 1)]}]";
            if (isPrevious) text += "  ← 直前";
            return text;
        }

        /// <summary>今の形の写し。基本の形・モデルなしでは null。</summary>
        private ShapeView CurrentShape()
        {
            var model = Model;
            if (model == null) return null;
            int current = model.CurrentShapeId;
            if (current == BaseShapeId) return null;
            var shapes = model.Shapes;
            for (int i = 0; i < shapes.Count; i++)
                if (shapes[i].Id == current) return shapes[i];
            return null;
        }

        private bool HasShape(string name)
        {
            var shapes = Model?.Shapes;
            if (shapes == null) return false;
            for (int i = 0; i < shapes.Count; i++)
                if (shapes[i].Name == name) return true;
            return false;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnListSelectionChanged(IEnumerable<object> _)
        {
            if (_refreshing) return;
            var model = Model;
            if (model == null) return;
            int idx = _listView?.selectedIndex ?? -1;
            if (idx < 0 || idx >= _rows.Count) return;
            var row = _rows[idx];
            if (row.id == model.CurrentShapeId) return;

            SendCommand?.Invoke(new SetCurrentShapeCommand(ModelIndex, row.name));
            bool ok = Model != null && Model.CurrentShapeId == row.id;
            StatusLog(ok ? $"切り替え: {(row.id == BaseShapeId ? BaseShapeLabel : row.name)}" : "切り替えられませんでした");
            Refresh();
        }

        private void OnSwap()
        {
            var model = Model;
            if (model == null) return;
            int before = model.CurrentShapeId;
            SendCommand?.Invoke(new SwapShapeCommand(ModelIndex));
            var after = Model;
            if (after != null && after.CurrentShapeId != before)
            {
                var cur = CurrentShape();
                StatusLog($"入れ替え: {(cur != null ? cur.Name : BaseShapeLabel)}");
            }
            else StatusLog("入れ替える相手がありません（今の形と直前の形が同じ）");
            Refresh();
        }

        /// <summary>新しい名前の欄を読む。空・重複なら理由を出して null を返す。</summary>
        private string TakeNewName()
        {
            string name = (_newNameField?.value ?? "").Trim();
            if (string.IsNullOrEmpty(name)) { StatusLog("新しい名前を入れてください"); return null; }
            if (HasShape(name)) { StatusLog($"形「{name}」は既にあります"); return null; }
            return name;
        }

        private void OnAddFromBase()
        {
            if (Model == null) return;
            string name = TakeNewName();
            if (name == null) return;
            SendCommand?.Invoke(new AddShapeCommand(ModelIndex, name, "", 3, "", true));
            FinishCreate(name, "基本の形から追加");
        }

        private void OnAddFromCurrent()
        {
            if (Model == null) return;
            string name = TakeNewName();
            if (name == null) return;
            var cur = CurrentShape();
            SendCommand?.Invoke(new AddShapeCommand(ModelIndex, name, "", cur != null ? cur.Panel : 3,
                                                    cur != null ? cur.Name : "", true));
            FinishCreate(name, "今の見た目から追加");
        }

        private void OnDuplicate()
        {
            if (Model == null) return;
            var cur = CurrentShape();
            if (cur == null) { StatusLog("基本の形は複製できません（「基本の形から追加」を使う）"); return; }
            string name = UniqueName(cur.Name + "のコピー");
            SendCommand?.Invoke(new AddShapeCommand(ModelIndex, name, cur.NameEnglish, cur.Panel, cur.Name, false));
            FinishCreate(name, "複製", clearNewName: false);
        }

        private void OnMirror()
        {
            if (Model == null) return;
            var cur = CurrentShape();
            if (cur == null) { StatusLog("基本の形は左右反転できません"); return; }
            string name = TakeNewName();
            if (name == null) return;
            SendCommand?.Invoke(new MirrorShapeCommand(ModelIndex, cur.Name, name));
            FinishCreate(name, $"左右反転（{cur.Name}）");
        }

        private void FinishCreate(string name, string what, bool clearNewName = true)
        {
            bool ok = HasShape(name);
            StatusLog(ok ? $"{what}: {name}" : $"{what}: できませんでした");
            if (ok && clearNewName) _newNameField?.SetValueWithoutNotify("");
            Refresh();
        }

        private string UniqueName(string baseName)
        {
            if (!HasShape(baseName)) return baseName;
            for (int n = 2; ; n++)
            {
                string candidate = baseName + n;
                if (!HasShape(candidate)) return candidate;
            }
        }

        private void OnDetailChanged()
        {
            if (_refreshing) return;
            var cur = CurrentShape();
            if (cur == null) return;
            string newName   = (_nameField?.value ?? cur.Name).Trim();
            string newNameEn = (_nameEnField?.value ?? cur.NameEnglish).Trim();
            int    newPanel  = Mathf.Clamp(_panelPopup != null ? _panelPopup.index : cur.Panel, 0, PanelChoices.Count - 1);
            if (newName == cur.Name && newNameEn == cur.NameEnglish && newPanel == cur.Panel) return;

            if (string.IsNullOrEmpty(newName)) { StatusLog("名前は空にできません"); Refresh(); return; }
            if (newName != cur.Name && HasShape(newName)) { StatusLog($"形「{newName}」は既にあります"); Refresh(); return; }

            SendCommand?.Invoke(new SetShapeAttributesCommand(ModelIndex, cur.Name, newName, newNameEn, newPanel));
            var after = CurrentShape();
            bool ok = after != null && after.Name == newName && after.NameEnglish == newNameEn && after.Panel == newPanel;
            StatusLog(ok ? $"変更: {newName}" : "変更できませんでした");
            Refresh();
        }

        private void OnDelete()
        {
            if (Model == null) return;
            var cur = CurrentShape();
            if (cur == null) { StatusLog("基本の形は消せません"); return; }
            string name = cur.Name;
            SendCommand?.Invoke(new DeleteShapeCommand(ModelIndex, name));
            StatusLog(!HasShape(name) ? $"削除: {name}" : "削除できませんでした");
            Refresh();
        }

        private void OnExtract()
        {
            var model = Model;
            if (model == null) return;
            var cur = CurrentShape();
            string name = cur != null ? cur.Name : "";
            SendCommand?.Invoke(new ExtractShapeModelCommand(ModelIndex, name));
            StatusLog($"モデルとして取り出す: {(cur != null ? cur.Name : BaseShapeLabel)}");
            Refresh();
        }

        // ================================================================
        // 試し表示（段 7。コマンドは previewShapes / endShapePreview）
        //   重みを動かすと、選んだ形 1 つを試し表示する（頂点位置は変えない）。
        //   始めるとき今の形が基本の形でなければ、基本の形へ切り替わる（切り替えの記録が 1 件残る）。
        // ================================================================

        private void BuildPreviewSection(VisualElement root)
        {
            root.Add(MakeSep());
            root.Add(SecLabel("試し表示"));

            _previewShape = new DropdownField("形", new List<string>(), 0);
            _previewShape.style.color = new StyleColor(Color.white);
            _previewShape.style.marginBottom = 3;
            _previewShape.RegisterValueChangedCallback(_ => { if (!_refreshing) _previewWeight?.SetValueWithoutNotify(0f); });
            root.Add(_previewShape);

            _previewWeight = new Slider("重み", 0f, 1f) { value = 0f };
            _previewWeight.style.color = new StyleColor(Color.white);
            _previewWeight.style.marginBottom = 3;
            _previewWeight.RegisterValueChangedCallback(e => OnPreviewWeightChanged(e.newValue));
            root.Add(_previewWeight);

            _btnPreviewEnd = new Button(OnPreviewEnd) { text = "試し表示を終える" };
            _btnPreviewEnd.style.marginBottom = 6;
            root.Add(_btnPreviewEnd);
        }

        private void RefreshPreviewChoices(IReadOnlyList<ShapeView> shapes)
        {
            if (_previewShape == null) return;
            var choices = new List<string>();
            for (int i = 0; i < shapes.Count; i++)
                if (shapes[i] != null && !shapes[i].IsUV && !string.IsNullOrEmpty(shapes[i].Name)) choices.Add(shapes[i].Name);
            string keep = _previewShape.value;
            _previewShape.choices = choices;
            _previewShape.SetValueWithoutNotify(choices.Contains(keep) ? keep : (choices.Count > 0 ? choices[0] : ""));
            _previewWeight?.SetEnabled(choices.Count > 0);
        }

        private void OnPreviewWeightChanged(float w)
        {
            if (_refreshing || Model == null) return;
            string name = _previewShape?.value ?? "";
            if (string.IsNullOrEmpty(name)) { StatusLog("試し表示する形がありません"); return; }
            SendCommand?.Invoke(new PreviewShapesCommand(ModelIndex, new[] { name }, new[] { w }));
            StatusLog($"試し表示: {name} ×{w:0.00}（基本の形で表示）");
        }

        private void OnPreviewEnd()
        {
            if (Model == null) return;
            SendCommand?.Invoke(new EndShapePreviewCommand(ModelIndex));
            _previewWeight?.SetValueWithoutNotify(0f);
            StatusLog("試し表示を終えました");
            Refresh();
        }

        // ================================================================
        // MQO（段 4。コマンドは importShapeMqo / reloadShapeMqo / exportShapeMqo）
        // ================================================================

        private void BuildMqoSection(VisualElement root)
        {
            root.Add(MakeSep());
            root.Add(SecLabel("MQO"));

            var pathRow = new VisualElement(); pathRow.style.flexDirection = FlexDirection.Row; pathRow.style.marginBottom = 3;
            _btnMqoBrowse = new Button(OnMqoBrowse) { text = "..." };
            _btnMqoBrowse.style.width = 28; _btnMqoBrowse.style.marginRight = 2;
            _mqoPathField = new TextField("MQO") { value = Poly_Ling.Core.RecentPaths.Get(MqoPathKey) ?? "" };
            _mqoPathField.style.flexGrow = 1;
            _mqoPathField.RegisterValueChangedCallback(e => Poly_Ling.Core.RecentPaths.Set(MqoPathKey, e.newValue));
            pathRow.Add(_btnMqoBrowse); pathRow.Add(_mqoPathField);
            root.Add(pathRow);

            _mqoScaleField = new FloatField("倍率") { value = 0.01f };
            _mqoScaleField.style.marginBottom = 3;
            root.Add(_mqoScaleField);
            _mqoFlipXToggle = new Toggle("Flip X") { value = true };
            root.Add(_mqoFlipXToggle);
            _mqoFlipZToggle = new Toggle("Flip Z") { value = false };
            _mqoFlipZToggle.style.marginBottom = 3;
            root.Add(_mqoFlipZToggle);

            _mqoKeepOffsetsToggle = new Toggle("基本の形を読むとき形の差分を保つ") { value = true };
            _mqoKeepOffsetsToggle.style.marginBottom = 3;
            root.Add(_mqoKeepOffsetsToggle);

            var impRow = new VisualElement(); impRow.style.flexDirection = FlexDirection.Row; impRow.style.marginBottom = 3;
            _btnMqoImport = new Button(OnMqoImport) { text = "MQO から読み込む（今の形へ）" };
            _btnMqoImport.style.flexGrow = 1; _btnMqoImport.style.marginRight = 4;
            _btnMqoImportNew = new Button(OnMqoImportNew) { text = "新しい形を作って読み込む" };
            _btnMqoImportNew.style.flexGrow = 1;
            impRow.Add(_btnMqoImport); impRow.Add(_btnMqoImportNew);
            root.Add(impRow);

            _mqoSourceLabel = new Label();
            _mqoSourceLabel.style.color = new StyleColor(Color.white);
            _mqoSourceLabel.style.whiteSpace = WhiteSpace.Normal;
            _mqoSourceLabel.style.fontSize = 10;
            _mqoSourceLabel.style.marginBottom = 2;
            root.Add(_mqoSourceLabel);

            _btnMqoReload = new Button(OnMqoReload) { text = "再読み込み（今の形の読み込み元）" };
            _btnMqoReload.style.marginBottom = 6;
            root.Add(_btnMqoReload);

            var expRow = new VisualElement(); expRow.style.flexDirection = FlexDirection.Row; expRow.style.marginBottom = 3;
            _btnMqoExportBrowse = new Button(OnMqoExportBrowse) { text = "..." };
            _btnMqoExportBrowse.style.width = 28; _btnMqoExportBrowse.style.marginRight = 2;
            _mqoExportPathField = new TextField("書き出し先") { value = Poly_Ling.Core.RecentPaths.Get(MqoExportPathKey) ?? "" };
            _mqoExportPathField.style.flexGrow = 1;
            _mqoExportPathField.RegisterValueChangedCallback(e => Poly_Ling.Core.RecentPaths.Set(MqoExportPathKey, e.newValue));
            expRow.Add(_btnMqoExportBrowse); expRow.Add(_mqoExportPathField);
            root.Add(expRow);

            _btnMqoExport = new Button(OnMqoExport) { text = "MQO へ書き出す（今の形）" };
            _btnMqoExport.style.marginBottom = 6;
            root.Add(_btnMqoExport);
        }

        /// <summary>今の形（基本の形を含む）の読み込み元の表示を更新する。</summary>
        private void RefreshMqoSource()
        {
            if (_mqoSourceLabel == null) return;
            var model = Model;
            ShapeView v = model == null ? null
                        : model.CurrentShapeId == BaseShapeId ? model.BaseShape : CurrentShape();
            _mqoSourceLabel.text = v == null || string.IsNullOrEmpty(v.SourcePath)
                ? "読み込み元: なし"
                : $"読み込み元: {v.SourcePath}（{v.SourcePairCount} 組）";
        }

        private void OnMqoBrowse()
        {
            string path = PlayerIoUiKit.AskLoadPath("MQO を選ぶ", MqoPathKey, _mqoPathField?.value, "mqo");
            if (!string.IsNullOrEmpty(path)) _mqoPathField.value = path;
        }

        private void OnMqoExportBrowse()
        {
            string cur = _mqoExportPathField?.value ?? "";
            string folder = string.IsNullOrEmpty(cur) ? "" : System.IO.Path.GetDirectoryName(cur);
            string name   = string.IsNullOrEmpty(cur) ? "shape.mqo" : System.IO.Path.GetFileName(cur);
            string path = Poly_Ling.Core.SaveDest.AskSavePath("MQO の書き出し先", MqoExportPathKey, folder, name, "mqo");
            if (!string.IsNullOrEmpty(path)) _mqoExportPathField.value = path;
        }

        private string TakeMqoPath()
        {
            string path = (_mqoPathField?.value ?? "").Trim();
            if (string.IsNullOrEmpty(path)) { StatusLog("MQO のパスを入れてください"); return null; }
            return path;
        }

        private void OnMqoImport()
        {
            if (Model == null) return;
            string path = TakeMqoPath();
            if (path == null) return;
            SendCommand?.Invoke(new ImportShapeMqoCommand(ModelIndex, path, "",
                _mqoScaleField.value, _mqoFlipXToggle.value, _mqoFlipZToggle.value, _mqoKeepOffsetsToggle.value));
            FinishMqo();
        }

        private void OnMqoImportNew()
        {
            if (Model == null) return;
            string path = TakeMqoPath();
            if (path == null) return;
            string name = TakeNewName();
            if (name == null) return;
            SendCommand?.Invoke(new ImportShapeMqoCommand(ModelIndex, path, name,
                _mqoScaleField.value, _mqoFlipXToggle.value, _mqoFlipZToggle.value, _mqoKeepOffsetsToggle.value));
            if (HasShape(name)) _newNameField?.SetValueWithoutNotify("");
            FinishMqo();
        }

        private void OnMqoReload()
        {
            var model = Model;
            if (model == null) return;
            var cur = CurrentShape();
            SendCommand?.Invoke(new ReloadShapeMqoCommand(ModelIndex, cur != null ? cur.Name : "", _mqoKeepOffsetsToggle.value));
            FinishMqo();
        }

        private void OnMqoExport()
        {
            var model = Model;
            if (model == null) return;
            string outPath = (_mqoExportPathField?.value ?? "").Trim();
            if (string.IsNullOrEmpty(outPath)) { StatusLog("書き出し先のパスを入れてください"); return; }
            ShapeView v = model.CurrentShapeId == BaseShapeId ? model.BaseShape : CurrentShape();
            bool hasSource = v != null && !string.IsNullOrEmpty(v.SourcePath);
            string refPath = hasSource ? "" : (_mqoPathField?.value ?? "").Trim();
            SendCommand?.Invoke(new ExportShapeMqoCommand(ModelIndex, outPath, refPath, true,
                _mqoScaleField.value, _mqoFlipXToggle.value, _mqoFlipZToggle.value));
            FinishMqo();
        }

        private void FinishMqo()
        {
            string msg = GetLastMqoSummary?.Invoke() ?? "";
            StatusLog(string.IsNullOrEmpty(msg) ? "（結果を取得できません）" : msg);
            Refresh();
        }

        // ================================================================
        // 補助
        // ================================================================

        private void StatusLog(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }

        private static VisualElement MakeSep()
        {
            var s = new VisualElement();
            s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white);
            s.style.marginTop = 2; s.style.marginBottom = 6;
            return s;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginBottom = 3;
            return l;
        }
    }
}
