// PlayerScenarioSubPanel.cs
// シナリオを選んで流し、いま何をしているかを表示する。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【流し方は 2 通りだけ】
//   「流す」は先頭の項目から、「続きを流す」は止まった所から。
//   途中の項目を選んで実行する口は置かない。@prev と @<項目 ID> は
//   前の項目の結果を指すので、途中だけ撃つと別の流れの値を黙って掴む。
//
// 【止まる所】
//   指示・確認の項目と、失敗した項目（ObjectGroupStep.RequiresJudgment と実行結果）。
//   判定と実行は runScenario / continueScenario（PlayerCommandDispatcher.ScenarioRun.cs）が持つ。
//   パネルは送って結果を表示するだけ。ここで組み立て直すと MCP と挙動が割れる。
//
// 【流す駆動は案内バー】
//   1 項目ずつ流して画面を更新する駆動は、右ペイン中区画の案内バー（PlayerScenarioGuideBar）が持つ。
//   項目のボタンを赤枠で示すときは下区画のパネルを切り替えるので、このパネルは隠れる。
//   隠れても流しと表示が続くよう、駆動を中区画へ置いた。このパネルは選んで始めることと、項目の一覧の表示を受け持つ。
//
// 【一覧は木】
//   フォルダの木に、親が呼ぶ子（参照）を重ねて出す（RebuildRows）。
//
// 【結果を見る】
//   コマンドは RunCommand（Dispatch の戻り値が返る口）で送る。
//   SendCommand は戻り値を捨てるので、失敗しても成功と区別が付かない。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerScenarioSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>プロジェクト取得。modelIndex を封筒へ入れるために要る。</summary>
        public Func<Poly_Ling.View.IProjectView> GetProject;

        /// <summary>コマンドを実行して結果を返す口。</summary>
        public Func<PanelCommand, CommandResult> RunCommand;

        /// <summary>流している途中の状態。流していなければ null。</summary>
        public Func<ScenarioRunState> GetRun;

        /// <summary>案内バーで先頭から流し始める。うまくいかなければ理由。</summary>
        public Func<string, string> StartRun;

        /// <summary>案内バーで止まった所から続ける。</summary>
        public Action ContinueRun;

        /// <summary>案内バーで流すのをやめる。</summary>
        public Action StopRun;

        /// <summary>案内バーが流している途中か（項目を予約中・人の入力待ち）。</summary>
        public Func<bool> IsBusy;

        private int ModelIndex => GetProject?.Invoke()?.CurrentModelIndex ?? 0;

        // ================================================================
        // 内部状態
        // ================================================================

        // UI 自動操作の ID は "scenario.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("list", Description = "シナリオの一覧（行番号）")]
        private ListView _listView;
        [UiControl("detail", Safety = UiSafety.ReadOnly, Description = "選んだシナリオの目的・前提・成功条件")]
        private Label    _detailLabel;
        [UiControl("run", Safety = UiSafety.Destructive, Description = "選んだシナリオを先頭の項目から流す")]
        private Button   _btnRun;
        [UiControl("continue", Safety = UiSafety.Destructive, Description = "止まった所から続きを流す")]
        private Button   _btnContinue;
        [UiControl("stop", Safety = UiSafety.SafeWrite, Description = "流すのをやめる")]
        private Button   _btnStop;
        [UiControl("now", Safety = UiSafety.ReadOnly, Description = "いま居るシナリオと項目")]
        private Label    _nowLabel;
        [UiControl("stopInfo", Safety = UiSafety.ReadOnly, Description = "止まった理由と次にすること")]
        private Label    _stopLabel;
        [UiControl("steps", Safety = UiSafety.ReadOnly, Description = "項目の一覧と状態（表示だけ）")]
        private ListView _stepView;
        [UiControl("stepDetail", Safety = UiSafety.ReadOnly, Description = "いま居る項目の中身")]
        private Label    _stepDetailLabel;
        [UiControl(Ignore = true)]
        private ScrollView _logScroll;
        [UiControl("log", Safety = UiSafety.ReadOnly, Description = "流した項目の記録")]
        private Label    _logLabel;
        [UiControl("export", Safety = UiSafety.UserOnly, Description = "選んだシナリオ（と参照先のシナリオ）を CSV ファイルへ書き出す。保存先のダイアログを開く")]
        private Button   _btnExport;
        [UiControl("import", Safety = UiSafety.UserOnly, Description = "CSV ファイルからシナリオを取り込む。ファイルを選ぶダイアログを開く")]
        private Button   _btnImport;
        [UiControl("importOverwrite", Description = "取り込むとき、同じ名前のシナリオを差し替える")]
        private Toggle   _importOverwrite;
        [UiControl("fileStatus", Safety = UiSafety.ReadOnly, Description = "書き出し・取り込みの結果")]
        private Label    _fileStatusLabel;

        [UiControl("recStartEnd", Safety = UiSafety.SafeWrite, Description = "シナリオの記録を開始する／終了する（終了後は保存か破棄を待つ）")]
        private Button   _btnRecStartEnd;
        [UiControl("recPauseResume", Safety = UiSafety.SafeWrite, Description = "シナリオの記録を一時停止する／再開する")]
        private Button   _btnRecPauseResume;
        [UiControl("recName", Description = "保存するシナリオの名前")]
        private TextField _recName;
        [UiControl("recGoal", Description = "保存するシナリオの目的")]
        private TextField _recGoal;
        [UiControl("recOverwrite", Description = "保存するとき、同じ名前のシナリオを差し替える")]
        private Toggle   _recOverwrite;
        [UiControl("recSave", Safety = UiSafety.SafeWrite, Description = "控えた項目をシナリオとして保存する（一時停止中か終了後）")]
        private Button   _btnRecSave;
        [UiControl("recDiscard", Safety = UiSafety.Destructive, Description = "控えた項目を捨てて記録をやめる")]
        private Button   _btnRecDiscard;
        [UiControl("recStatus", Safety = UiSafety.ReadOnly, Description = "記録の状態と控えた項目の数")]
        private Label    _recStatusLabel;

        [UiControl("folderName", Description = "フォルダ名（「顔/輪郭」のように / で区切る）。一覧でフォルダの行を押すと入る")]
        private TextField _folderField;
        [UiControl("folderCreate", Safety = UiSafety.SafeWrite, Description = "入力したフォルダを作る")]
        private Button   _btnFolderCreate;
        [UiControl("folderMove", Safety = UiSafety.SafeWrite, Description = "選んだシナリオを入力したフォルダへ移す（空なら直下）")]
        private Button   _btnFolderMove;
        [UiControl("parentName", Description = "フォルダから作る親シナリオの名前")]
        private TextField _parentNameField;
        [UiControl("folderMakeParent", Safety = UiSafety.SafeWrite, Description = "入力したフォルダ直下のシナリオを名前順に呼ぶ親シナリオを作る")]
        private Button   _btnFolderMakeParent;
        [UiControl("folderStatus", Safety = UiSafety.ReadOnly, Description = "フォルダ操作の結果")]
        private Label    _folderStatusLabel;

        /// <summary>直前の記録操作の結果。状態表示の下に出す。</summary>
        private string _recMessage = "";
        private bool   _recMessageFailed;

        private const string ExportKey = "Scenario.Export";
        private const string ImportKey = "Scenario.Import";

        /// <summary>項目リストの高さを覚えるキー（PlayerPrefs）。</summary>
        private const string PrefStepListH = "PolyLing.Scenario.StepListH";
        /// <summary>シナリオ一覧の高さを覚えるキー（PlayerPrefs）。</summary>
        private const string PrefListH     = "PolyLing.Scenario.ListH";
        private const float  StepListMinH  = 60f;
        private const float  StepListDefH  = 160f;
        private const float  ListDefH      = 220f;
        private const float  SplitterH     = 8f;
        private static readonly Color SplitterColor      = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color SplitterHoverColor = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>一覧の 1 行。フォルダ・シナリオ本体・親が呼ぶ子（参照）のどれか。</summary>
        private sealed class ListRow
        {
            public int         Depth;
            public string      Folder;    // フォルダの行なら「a/b」、それ以外は null
            public ObjectGroup Scenario;  // シナリオ本体・参照の行の中身。参照切れなら null
            public bool        IsRef;     // 親が呼ぶ子として出した行
            public bool        Broken;    // 参照切れ・循環
            public string      Text;
            public string      Key => Folder != null ? "F:" + Folder : Scenario != null ? "S:" + Scenario.Name : "";
        }

        private readonly List<ListRow>     _rows       = new List<ListRow>();
        /// <summary>閉じているフォルダ。パネルを開いている間だけ覚える。</summary>
        private readonly HashSet<string>   _collapsed  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSelection;
        private readonly List<string>      _stepLabels = new List<string>();
        private readonly List<ScenarioItemRunStatus> _stepStatus = new List<ScenarioItemRunStatus>();

        [UiControl(Ignore = true)]
        private VisualElement _root;
        private int  _selected = -1;

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight =
            _root.style.paddingTop  = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(SecLabel("シナリオ"));

            var hint = new Label(
                "シナリオを選んで「流す」を押すと、上の項目から順に実行します。\n"
              + "「指示」「確認」の項目と、失敗した項目で止まります。止まったら内容を読み、「続きを流す」を押してください。");
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.fontSize     = 10;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            _listView = new ListView(_rows, 22,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _rows.Count) return;
                    var r = _rows[i];
                    l.text = r.Text;
                    l.style.paddingLeft = 4 + r.Depth * 14;
                    l.style.color = new StyleColor(
                        r.Broken         ? new Color(1f, 0.55f, 0.45f)
                      : r.Folder != null ? new Color(0.65f, 0.8f, 1f)
                      : r.IsRef          ? new Color(0.7f, 0.7f, 0.7f)
                      :                    new Color(0.9f, 0.9f, 0.9f));
                });
            _listView.selectionType   = SelectionType.Single;
            _listView.style.minHeight = StepListMinH;
            _listView.style.height    = Mathf.Max(StepListMinH, PlayerPrefs.GetFloat(PrefListH, ListDefH));
            _listView.selectionChanged += _ => OnListSelection();
            _root.Add(_listView);
            _root.Add(MakeListSplitter(_listView, PrefListH, "ドラッグでシナリオ一覧の高さを変える"));

            _detailLabel = new Label();
            _detailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _detailLabel.style.fontSize     = 10;
            _detailLabel.style.marginBottom = 4;
            _root.Add(_detailLabel);

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 4;
            _btnRun      = MkBtn("流す",       OnRun);      _btnRun.style.flexGrow      = 1; _btnRun.style.marginRight      = 2;
            _btnContinue = MkBtn("続きを流す", OnContinue); _btnContinue.style.flexGrow = 1; _btnContinue.style.marginRight = 2;
            _btnStop     = MkBtn("やめる",     OnStop);     _btnStop.style.flexGrow     = 1;
            opRow.Add(_btnRun); opRow.Add(_btnContinue); opRow.Add(_btnStop);
            _root.Add(opRow);

            _nowLabel = new Label();
            _nowLabel.style.whiteSpace   = WhiteSpace.Normal;
            _nowLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nowLabel.style.marginBottom = 2;
            _root.Add(_nowLabel);

            _stopLabel = new Label();
            _stopLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stopLabel.style.marginBottom = 4;
            _root.Add(_stopLabel);

            BuildFolders();
            BuildRecording();

            _root.Add(SecLabel("項目"));

            _stepView = new ListView(_stepLabels, 20,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _stepLabels.Count) return;
                    l.text = _stepLabels[i];
                    l.style.color = new StyleColor(StatusColor(i < _stepStatus.Count ? _stepStatus[i] : ScenarioItemRunStatus.NotRun));
                });
            _stepView.selectionType   = SelectionType.None;
            _stepView.style.minHeight = StepListMinH;
            _stepView.style.height    = Mathf.Max(StepListMinH, PlayerPrefs.GetFloat(PrefStepListH, StepListDefH));
            _root.Add(_stepView);

            _root.Add(MakeListSplitter(_stepView, PrefStepListH, "ドラッグで項目リストの高さを変える"));

            _stepDetailLabel = new Label();
            _stepDetailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stepDetailLabel.style.fontSize     = 10;
            _stepDetailLabel.style.marginBottom = 4;
            _root.Add(_stepDetailLabel);

            _root.Add(SecLabel("流した履歴"));

            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.minHeight = 80;
            _logScroll.style.maxHeight = 200;
            _logLabel = new Label();
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.fontSize   = 10;
            _logScroll.Add(_logLabel);
            _root.Add(_logScroll);

            // ── ファイルとの出し入れ ──
            _root.Add(SecLabel("ファイル"));
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;
            _btnExport = MkBtn("書き出す", OnExport); _btnExport.style.flexGrow = 1; _btnExport.style.marginRight = 2;
            _btnImport = MkBtn("読み込む", OnImport); _btnImport.style.flexGrow = 1;
            fileRow.Add(_btnExport); fileRow.Add(_btnImport);
            _root.Add(fileRow);

            _importOverwrite = new Toggle("読み込むとき同じ名前を差し替える") { value = false };
            _importOverwrite.style.marginBottom = 2;
            _root.Add(_importOverwrite);

            _fileStatusLabel = new Label();
            _fileStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            _fileStatusLabel.style.fontSize   = 10;
            _root.Add(_fileStatusLabel);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>
        /// シナリオを読み直して表示を作り直す。シナリオはファイルが正典で外から変わるので、
        /// パネルを開くたびに読み直す。流している途中の状態は写しを使うので影響しない。
        /// </summary>
        public void Refresh()
        {
            ScenarioLibrary.Reload();

            RebuildRows();
            UpdateView();
            UpdateRecordingView();
        }

        /// <summary>選んでいる行のシナリオ（参照の行なら呼ばれる子）。フォルダの行・参照切れなら null。</summary>
        private ObjectGroup Selected()
            => (_selected >= 0 && _selected < _rows.Count) ? _rows[_selected].Scenario : null;

        /// <summary>選んでいる行のフォルダ。フォルダの行ならそのフォルダ、シナリオの行なら置いているフォルダ。</summary>
        private string SelectedFolder()
        {
            if (_selected < 0 || _selected >= _rows.Count) return null;
            var r = _rows[_selected];
            if (r.Folder != null) return r.Folder;
            return r.Scenario != null ? ScenarioLibrary.FolderOf(r.Scenario.Name) : null;
        }

        private void OnListSelection()
        {
            if (_suppressSelection) return;
            int i = _listView.selectedIndex;
            if (i >= 0 && i < _rows.Count && _rows[i].Folder != null)
            {
                // フォルダの行は開閉だけ。入力欄へフォルダ名を入れておく（移す・親を作るの対象）。
                string f = _rows[i].Folder;
                if (!_collapsed.Remove(f)) _collapsed.Add(f);
                if (_folderField != null) _folderField.value = f;
                _selected = i;
                RebuildRows();
                UpdateView();
                return;
            }
            _selected = i;
            UpdateView();
        }

        // ================================================================
        // 項目リストの高さ
        // ================================================================

        /// <summary>
        /// 一覧の下に置く仕切り線を作る。上下にドラッグすると一覧の高さが変わり、
        /// 離したときの高さを prefKey に覚える（右ペインの上下区画の仕切りと同じ作り）。
        /// </summary>
        private static VisualElement MakeListSplitter(ListView target, string prefKey, string tooltip)
        {
            var splitter = new VisualElement();
            splitter.style.height          = SplitterH;
            splitter.style.flexShrink      = 0;
            splitter.style.marginBottom    = 4;
            splitter.style.backgroundColor = new StyleColor(SplitterColor);
            splitter.tooltip = tooltip;

            bool  dragging = false, hover = false;
            float startY = 0f, startH = 0f;

            void Paint() => splitter.style.backgroundColor = new StyleColor(dragging || hover ? SplitterHoverColor : SplitterColor);

            splitter.RegisterCallback<PointerEnterEvent>(_ => { hover = true;  Paint(); });
            splitter.RegisterCallback<PointerLeaveEvent>(_ => { hover = false; Paint(); });
            splitter.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                dragging = true;
                startY = evt.position.y;
                startH = target.resolvedStyle.height;
                splitter.CapturePointer(evt.pointerId);
                Paint();
                evt.StopPropagation();
            });
            splitter.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                target.style.height = Mathf.Max(StepListMinH, startH + (evt.position.y - startY));
                evt.StopPropagation();
            });
            splitter.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                if (splitter.HasPointerCapture(evt.pointerId)) splitter.ReleasePointer(evt.pointerId);
                hover = splitter.worldBound.Contains(evt.position);
                Paint();
                PlayerPrefs.SetFloat(prefKey, target.resolvedStyle.height);
                PlayerPrefs.Save();
                evt.StopPropagation();
            });
            splitter.RegisterCallback<PointerCaptureOutEvent>(_ => { dragging = false; Paint(); });
            return splitter;
        }

        // ================================================================
        // 一覧の木
        // ================================================================
        //
        // フォルダ＝置き場所、親子＝使い方（ScenarioLibrary.cs 冒頭）。
        // 親から呼ばれていないシナリオはフォルダの中に出す。呼ばれているシナリオは
        // 親の行の下にだけ「→ 子の名前」として字下げして出す（孫も同じ）。選べば本体と同じ扱い。
        // 呼びも呼ばれもしないシナリオには［単独］を付ける。

        /// <summary>一覧の行を作り直す。選択は同じフォルダ／シナリオへ戻す。</summary>
        private void RebuildRows()
        {
            string keep = (_selected >= 0 && _selected < _rows.Count) ? _rows[_selected].Key : null;

            _rows.Clear();
            var all     = ScenarioLibrary.GetAll();
            var byName  = new Dictionary<string, ObjectGroup>(StringComparer.Ordinal);
            var called  = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in all)
            {
                if (g == null) continue;
                byName[g.Name] = g;
                if (g.Steps == null) continue;
                foreach (var s in g.Steps)
                    if (s != null && s.IsScenarioRef && !string.IsNullOrEmpty(s.RefName)
                        && !string.Equals(s.RefName, g.Name, StringComparison.Ordinal))
                        called.Add(s.RefName);
            }

            var folders = ScenarioLibrary.Folders();
            AddFolderRows("", 0, folders, all, byName, called);

            _selected = -1;
            if (keep != null)
                for (int i = 0; i < _rows.Count; i++)
                    if (_rows[i].Key == keep) { _selected = i; break; }

            if (_listView == null) return;
            _suppressSelection = true;
            try
            {
                _listView.Rebuild();
                _listView.selectedIndex = _selected;
            }
            finally { _suppressSelection = false; }
        }

        /// <summary>folder 直下のフォルダ（再帰）とシナリオを行にする。</summary>
        private void AddFolderRows(string folder, int depth, List<string> folders, List<ObjectGroup> all,
                                   Dictionary<string, ObjectGroup> byName, HashSet<string> called)
        {
            foreach (var f in folders)
            {
                if (!IsDirectChild(f, folder)) continue;
                bool open = !_collapsed.Contains(f);
                string leaf = f.Substring(f.LastIndexOf('/') + 1);
                _rows.Add(new ListRow { Depth = depth, Folder = f, Text = (open ? "▼ " : "▶ ") + leaf + "/" });
                if (open) AddFolderRows(f, depth + 1, folders, all, byName, called);
            }

            var here = new List<ObjectGroup>();
            // どこかの親から呼ばれているシナリオは、親の下にだけ出す（二重に出さない）。
            foreach (var g in all)
                if (g != null && !called.Contains(g.Name)
                    && string.Equals(ScenarioLibrary.FolderOf(g.Name) ?? "", folder, StringComparison.Ordinal))
                    here.Add(g);
            here.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (var g in here)
            {
                bool callsAny = false;
                if (g.Steps != null) foreach (var s in g.Steps) if (s != null && s.IsScenarioRef) { callsAny = true; break; }
                string alone = !callsAny && !called.Contains(g.Name) ? "  ［単独］" : "";
                _rows.Add(new ListRow
                {
                    Depth = depth, Scenario = g,
                    Text  = $"{g.Name}  [{g.StepCount} 項目]{alone}" + (string.IsNullOrEmpty(g.Goal) ? "" : $"  {g.Goal}"),
                });
                AddRefRows(g, depth + 1, byName, new List<string> { g.Name });
            }
        }

        /// <summary>親が呼んでいる子を、呼ぶ順に参照の行として出す。循環は打ち切る。</summary>
        private void AddRefRows(ObjectGroup parent, int depth, Dictionary<string, ObjectGroup> byName, List<string> path)
        {
            if (parent?.Steps == null) return;
            foreach (var s in parent.Steps)
            {
                if (s == null || !s.IsScenarioRef) continue;
                string n = s.RefName ?? "";
                if (!byName.TryGetValue(n, out var child))
                {
                    _rows.Add(new ListRow { Depth = depth, IsRef = true, Broken = true, Text = $"→ {n}（見つかりません）" });
                    continue;
                }
                if (path.Contains(n))
                {
                    _rows.Add(new ListRow { Depth = depth, IsRef = true, Broken = true, Scenario = child, Text = $"→ {n}（循環しています）" });
                    continue;
                }
                _rows.Add(new ListRow { Depth = depth, IsRef = true, Scenario = child, Text = $"→ {n}  [{child.StepCount} 項目]" });
                path.Add(n);
                AddRefRows(child, depth + 1, byName, path);
                path.RemoveAt(path.Count - 1);
            }
        }

        private static bool IsDirectChild(string f, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return f.IndexOf('/') < 0;
            return f.Length > folder.Length + 1
                && f.StartsWith(folder + "/", StringComparison.Ordinal)
                && f.IndexOf('/', folder.Length + 1) < 0;
        }

        /// <summary>流しの状態が変わったとき（案内バーから）。表示を作り直す。</summary>
        public void RefreshRunView() => UpdateView();

        /// <summary>流している状態（無ければ選んだシナリオ）から表示を全部作り直す。</summary>
        private void UpdateView()
        {
            if (_root == null) return;
            bool busy = IsBusy?.Invoke() ?? false;

            var run = GetRun?.Invoke();
            var sel = Selected();

            // ── シナリオの説明 ──
            if (sel == null) _detailLabel.text = "";
            else _detailLabel.text =
                  $"目的: {Or(sel.Goal, "（無し）")}\n"
                + $"前提: {Join(sel.Preconditions)}\n"
                + $"成功条件: {Join(sel.SuccessCriteria)}";

            // ── いま ──
            ObjectGroup shown;
            if (run != null)
            {
                shown = run.CurrentGroup;
                var st = run.CurrentStep;

                string where = string.Join(" → ", run.StackNames);
                if (run.Stop == ScenarioRunStop.Finished || st == null)
                    _nowLabel.text = $"「{run.RootName}」を流し終えました（実行したコマンド {run.ExecutedCommands} 本）";
                else
                    _nowLabel.text =
                          $"いま: {where}　{run.CurrentIndex + 1} / {shown?.StepCount ?? 0} 番目\n"
                        + $"［{KindText(st.Kind)}］ {Or(st.Purpose, st.Action)}";

                _stopLabel.text = StopText(run);
                _stopLabel.style.color = new StyleColor(StopColor(run.Stop));
            }
            else
            {
                shown = sel;
                _nowLabel.text  = sel == null ? "シナリオを選んでください" : $"「{sel.Name}」はまだ流していません";
                _stopLabel.text = "";
            }

            // ── 項目の一覧 ──
            _stepLabels.Clear();
            _stepStatus.Clear();
            if (shown?.Steps != null)
            {
                string scenarioName = run != null ? run.CurrentScenario : shown.Name;
                for (int i = 0; i < shown.Steps.Count; i++)
                {
                    var st = shown.Steps[i];
                    if (st == null) continue;

                    var status = run != null ? run.StatusOf(scenarioName, st.ScenarioItemId) : ScenarioItemRunStatus.NotRun;
                    bool here  = run != null && i == run.CurrentIndex && run.Stop != ScenarioRunStop.Finished;

                    string what = st.IsScenarioRef ? $"「{st.RefName}」を呼ぶ" : Or(st.Purpose, st.Action);
                    _stepLabels.Add($"{(here ? "▶" : "　")}{StatusText(status)} {i + 1}. ［{KindText(st.Kind)}］ {Shorten(what, 48)}");
                    _stepStatus.Add(here && status == ScenarioItemRunStatus.NotRun ? ScenarioItemRunStatus.Running : status);
                }
            }
            _stepView.Rebuild();
            if (run != null && run.CurrentIndex >= 0 && run.CurrentIndex < _stepLabels.Count)
                _stepView.ScrollToItem(run.CurrentIndex);

            // ── いま居る項目の中身 ──
            var cur = run?.CurrentStep;
            if (cur == null) _stepDetailLabel.text = "";
            else
            {
                var sb = new StringBuilder();
                sb.Append("種類: ").Append(KindText(cur.Kind)).Append('\n');
                sb.Append("内容: ").Append(Or(cur.Purpose, "（無し）"));
                if (cur.IsScenarioRef) sb.Append('\n').Append("呼ぶシナリオ: ").Append(cur.RefName);
                if (cur.IsExecutable)
                {
                    sb.Append('\n').Append("コマンド: ").Append(cur.Action);
                    foreach (var kv in cur.SortedArgs())
                        sb.Append('\n').Append("  ").Append(kv.Key).Append(" = ").Append(Shorten(kv.Value, 80));
                }
                _stepDetailLabel.text = sb.ToString();
            }

            // ── 記録 ──
            if (run == null) _logLabel.text = "";
            else
            {
                var log   = run.Log;
                int start = Math.Max(0, log.Count - 200);
                var sb    = new StringBuilder();
                for (int i = start; i < log.Count; i++) { if (sb.Length > 0) sb.Append('\n'); sb.Append(log[i]); }
                _logLabel.text = sb.ToString();
                _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
            }

            // ── ボタン ──
            bool canContinue = run != null && !busy && run.Stop != ScenarioRunStop.Finished;
            _btnRun.SetEnabled(!busy && sel != null);
            _btnContinue.SetEnabled(canContinue);
            _btnStop.SetEnabled(run != null);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnRun()
        {
            var sel = Selected();
            if (sel == null) { SetStop("シナリオを選んでください"); return; }

            if (StartRun == null) { SetStop("流せませんでした: 案内バーが配線されていません"); return; }
            string err = StartRun(sel.Name);
            if (err != null) SetStop($"流せませんでした: {err}");
        }

        private void OnContinue() => ContinueRun?.Invoke();

        private void OnStop()
        {
            // やめると流しが消えて UpdateView からは位置が見えなくなるので、消す前に控えて表示する。
            var run = GetRun?.Invoke();
            string where = null;
            if (run != null)
            {
                var fr = run.Top;
                string scen = fr?.Group?.Name ?? run.RootName;
                int no = fr != null ? fr.Index + 1 : 0;
                int count = fr?.Group?.Steps?.Count ?? 0;
                string elem = (fr?.Group?.Steps != null && fr.Index >= 0 && fr.Index < count)
                    ? fr.Group.Steps[fr.Index]?.ScenarioItemId : "";
                where = $"やめました: 「{scen}」 {no} / {count} 番目（{elem}）の手前。実行したコマンド {run.ExecutedCommands} 本。"
                      + "それまでの結果はモデルに残っています（戻すなら Undo）。";
            }

            StopRun?.Invoke();
            UpdateView();
            if (where != null && _stopLabel != null) _stopLabel.text = where;
        }

        private void SetStop(string text)
        {
            if (_stopLabel == null) return;
            _stopLabel.text = text ?? "";
            _stopLabel.style.color = new StyleColor(new Color(1f, 0.55f, 0.45f));
        }

        /// <summary>選んだシナリオ（無ければ全部）を CSV ファイルへ書き出す。参照先のシナリオも入る。</summary>
        private void OnExport()
        {
            var sel = Selected();
            string defaultName = (sel != null ? sel.Name : "scenarios") + ".csv";
            // 保存ダイアログで選んだパスは作業フォルダの外でも 1 回だけ通る（SaveDest.AskSavePath）。
            string path = Poly_Ling.Core.SaveDest.AskSavePath("シナリオを書き出す", ExportKey, "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            var names = sel != null ? new[] { sel.Name } : null;
            var r = RunCommand?.Invoke(new ExportScenariosCommand(ModelIndex, path, names));
            SetFileStatus(r, $"書き出しました: {path}");
        }

        /// <summary>CSV ファイルからシナリオを取り込み、一覧を読み直す。</summary>
        private void OnImport()
        {
            string path = PlayerIoUiKit.AskLoadPath("シナリオを読み込む", ImportKey, "", "csv");
            if (string.IsNullOrEmpty(path)) return;
            path = Poly_Ling.Core.PLSandbox.AllowOnceFromDialog(path);

            bool overwrite = _importOverwrite != null && _importOverwrite.value;
            var r = RunCommand?.Invoke(new ImportScenariosCommand(ModelIndex, path, overwrite));
            SetFileStatus(r, $"読み込みました: {path}");
            if (r != null && r.Success) Refresh();
        }

        private void SetFileStatus(CommandResult r, string ok)
        {
            if (_fileStatusLabel == null) return;
            bool success = r != null && r.Success;
            _fileStatusLabel.text = success ? ok : $"できませんでした: {r?.Reason ?? "実行の口が配線されていません"}";
            _fileStatusLabel.style.color = new StyleColor(success ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f));
        }

        // ================================================================
        // フォルダ
        // ================================================================
        //
        // フォルダは置き場所（整理）、親子は使い方（ScenarioLibrary.cs 冒頭）。
        // 断片をフォルダに集めて並べ、「フォルダから親を作る」で順に呼ぶ親にする。

        private void BuildFolders()
        {
            _root.Add(SecLabel("フォルダ"));

            _folderField = new TextField("フォルダ") { value = "" };
            _folderField.style.marginBottom = 2;
            _root.Add(_folderField);

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom  = 4;
            _btnFolderCreate = MkBtn("フォルダを作る",       OnFolderCreate); _btnFolderCreate.style.flexGrow = 1; _btnFolderCreate.style.marginRight = 2;
            _btnFolderMove   = MkBtn("選んだシナリオを移す", OnFolderMove);   _btnFolderMove.style.flexGrow   = 1;
            row1.Add(_btnFolderCreate); row1.Add(_btnFolderMove);
            _root.Add(row1);

            _parentNameField = new TextField("親の名前") { value = "" };
            _parentNameField.style.marginBottom = 2;
            _root.Add(_parentNameField);

            _btnFolderMakeParent = MkBtn("フォルダから親を作る", OnFolderMakeParent);
            _btnFolderMakeParent.style.marginBottom = 2;
            _root.Add(_btnFolderMakeParent);

            _folderStatusLabel = new Label();
            _folderStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _folderStatusLabel.style.fontSize     = 10;
            _folderStatusLabel.style.marginBottom = 6;
            _root.Add(_folderStatusLabel);
        }

        private void OnFolderCreate()
        {
            string f = _folderField?.value ?? "";
            var r = RunCommand?.Invoke(new CreateScenarioFolderCommand(ModelIndex, f));
            if (SetFolderStatus(r, $"フォルダを作りました: {f}")) Refresh();
        }

        private void OnFolderMove()
        {
            var sel = Selected();
            if (sel == null) { SetFolderStatus(null, null, "シナリオを選んでください"); return; }
            string f = _folderField?.value ?? "";
            var r = RunCommand?.Invoke(new MoveScenariosCommand(ModelIndex, new[] { sel.Name }, f));
            if (SetFolderStatus(r, $"「{sel.Name}」を {(string.IsNullOrWhiteSpace(f) ? "直下" : f)} へ移しました")) Refresh();
        }

        private void OnFolderMakeParent()
        {
            string f    = _folderField?.value ?? "";
            string name = (_parentNameField?.value ?? "").Trim();
            var r = RunCommand?.Invoke(new CreateScenarioFromFolderCommand(ModelIndex, f, name));
            if (SetFolderStatus(r, $"親シナリオ「{name}」を {f} に作りました")) Refresh();
        }

        /// <summary>結果を表示する。成功なら true。</summary>
        private bool SetFolderStatus(CommandResult r, string ok, string message = null)
        {
            if (_folderStatusLabel == null) return false;
            bool success = message == null && r != null && r.Success;
            _folderStatusLabel.text = message ?? (success ? ok : $"できませんでした: {r?.Reason ?? "実行の口が配線されていません"}");
            _folderStatusLabel.style.color = new StyleColor(success ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f));
            return success;
        }

        // ================================================================
        // シナリオを記録する（ScenarioRecorder）
        // ================================================================
        //
        // 操作はすべてコマンドで送る（MCP と同じ口）。状態の表示は
        // ボタンを押したときとパネルを開いたときに作り直す。毎フレームは見ない。

        private void BuildRecording()
        {
            _root.Add(SecLabel("シナリオを記録する"));

            var hint = new Label(
                "「記録開始」から「記録終了」までに実行した操作を控えます。一時停止中の操作は控えません。\n"
              + "一時停止中か終了後に、名前を付けて「保存」するとシナリオになります。");
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.fontSize     = 10;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom  = 4;
            _btnRecStartEnd    = MkBtn("記録開始", OnRecStartEnd);    _btnRecStartEnd.style.flexGrow    = 1; _btnRecStartEnd.style.marginRight = 2;
            _btnRecPauseResume = MkBtn("一時停止", OnRecPauseResume); _btnRecPauseResume.style.flexGrow = 1;
            row1.Add(_btnRecStartEnd); row1.Add(_btnRecPauseResume);
            _root.Add(row1);

            _recName = new TextField("名前") { value = "" };
            _recName.style.marginBottom = 2;
            _root.Add(_recName);

            _recGoal = new TextField("目的") { value = "" };
            _recGoal.style.marginBottom = 2;
            _root.Add(_recGoal);

            _recOverwrite = new Toggle("同じ名前のシナリオを差し替える") { value = false };
            _recOverwrite.style.marginBottom = 2;
            _root.Add(_recOverwrite);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginBottom  = 4;
            _btnRecSave    = MkBtn("保存", OnRecSave);    _btnRecSave.style.flexGrow    = 1; _btnRecSave.style.marginRight = 2;
            _btnRecDiscard = MkBtn("破棄", OnRecDiscard); _btnRecDiscard.style.flexGrow = 1;
            row2.Add(_btnRecSave); row2.Add(_btnRecDiscard);
            _root.Add(row2);

            _recStatusLabel = new Label();
            _recStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _recStatusLabel.style.marginBottom = 6;
            _root.Add(_recStatusLabel);
        }

        private void UpdateRecordingView()
        {
            if (_recStatusLabel == null) return;

            var state = ScenarioRecorder.State;
            bool recordingOrPaused = state == ScenarioRecordingState.Recording || state == ScenarioRecordingState.Paused;

            _btnRecStartEnd.text = recordingOrPaused ? "記録終了" : "記録開始";
            _btnRecStartEnd.SetEnabled(recordingOrPaused || state == ScenarioRecordingState.None);

            _btnRecPauseResume.text = state == ScenarioRecordingState.Paused ? "再開" : "一時停止";
            _btnRecPauseResume.SetEnabled(recordingOrPaused);

            _btnRecSave.SetEnabled(state == ScenarioRecordingState.Paused || state == ScenarioRecordingState.Ended);
            _btnRecDiscard.SetEnabled(ScenarioRecorder.HasDraft);

            string text = state == ScenarioRecordingState.None
                ? ScenarioRecorder.StateText(state)
                : $"{ScenarioRecorder.StateText(state)}（控えた項目 {ScenarioRecorder.StepCount}）";
            if (!string.IsNullOrEmpty(_recMessage)) text += "\n" + _recMessage;
            _recStatusLabel.text = text;
            _recStatusLabel.style.color = new StyleColor(_recMessageFailed ? new Color(1f, 0.55f, 0.45f) : RecColor(state));
        }

        private static Color RecColor(ScenarioRecordingState s)
        {
            switch (s)
            {
                case ScenarioRecordingState.Recording: return new Color(1f, 0.55f, 0.45f);
                case ScenarioRecordingState.Paused:    return new Color(1f, 0.85f, 0.4f);
                case ScenarioRecordingState.Ended:     return new Color(0.75f, 0.85f, 1f);
                default:                               return new Color(0.75f, 0.75f, 0.75f);
            }
        }

        private void OnRecStartEnd()
        {
            var state = ScenarioRecorder.State;
            if (state == ScenarioRecordingState.Recording || state == ScenarioRecordingState.Paused)
                RunRec(new StopScenarioRecordingCommand(ModelIndex), "記録を終了しました。保存するか破棄してください");
            else
                RunRec(new StartScenarioRecordingCommand(ModelIndex), "記録を開始しました");
        }

        private void OnRecPauseResume()
        {
            if (ScenarioRecorder.State == ScenarioRecordingState.Paused)
                RunRec(new ResumeScenarioRecordingCommand(ModelIndex), "再開しました");
            else
                RunRec(new PauseScenarioRecordingCommand(ModelIndex), "一時停止しました");
        }

        private void OnRecSave()
        {
            string name = _recName?.value?.Trim() ?? "";
            string goal = _recGoal?.value ?? "";
            bool   over = _recOverwrite != null && _recOverwrite.value;
            var r = RunRec(new SaveScenarioRecordingCommand(ModelIndex, name, goal, over), $"シナリオ「{name}」として保存しました");
            if (r != null && r.Success) Refresh();
        }

        private void OnRecDiscard()
            => RunRec(new DiscardScenarioRecordingCommand(ModelIndex), "記録を破棄しました");

        private CommandResult RunRec(PanelCommand cmd, string ok)
        {
            var r = RunCommand?.Invoke(cmd);
            bool success = r != null && r.Success;
            _recMessage       = success ? ok : $"できませんでした: {r?.Reason ?? "実行の口が配線されていません"}";
            _recMessageFailed = !success;
            UpdateRecordingView();
            return r;
        }

        // ================================================================
        // 表示の文言
        // ================================================================

        private static string StopText(ScenarioRunState run)
        {
            switch (run.Stop)
            {
                case ScenarioRunStop.Judgment:
                    return run.StopStepKind == ObjectGroupStepKind.Observe
                        ? $"確認の項目で止まっています。\n確かめること: {run.StopMessage}\n→ 画面で確かめてから「続きを流す」を押してください。おかしければ「やめる」を押してください。"
                        : $"指示の項目で止まっています。\n指示: {run.StopMessage}\n→ 指示の作業を済ませてから「続きを流す」を押してください。次の項目の値を変えるときは MCP の continueScenario を使います。";
                case ScenarioRunStop.Failed:
                    return $"失敗して止まっています。\n理由: {run.StopMessage}\n→ 原因を直してから「続きを流す」を押すと、同じ項目をやり直します。";
                case ScenarioRunStop.Finished:
                    return "最後の項目まで終わりました。";
                default:
                    return "流しています…";
            }
        }

        private static Color StopColor(ScenarioRunStop s)
        {
            switch (s)
            {
                case ScenarioRunStop.Judgment: return new Color(1f, 0.85f, 0.4f);
                case ScenarioRunStop.Failed:   return new Color(1f, 0.55f, 0.45f);
                case ScenarioRunStop.Finished: return new Color(0.6f, 0.9f, 0.6f);
                default:                       return new Color(0.75f, 0.85f, 1f);
            }
        }

        private static string KindText(ObjectGroupStepKind k)
        {
            switch (k)
            {
                case ObjectGroupStepKind.Command:     return "実行";
                case ObjectGroupStepKind.Note:        return "注意";
                case ObjectGroupStepKind.Instruction: return "指示・止まる";
                case ObjectGroupStepKind.Observe:     return "確認・止まる";
                case ObjectGroupStepKind.ScenarioRef: return "別のシナリオ";
                default:                              return k.ToString();
            }
        }

        private static string StatusText(ScenarioItemRunStatus s)
        {
            switch (s)
            {
                case ScenarioItemRunStatus.Done:    return "済";
                case ScenarioItemRunStatus.Running: return "中";
                case ScenarioItemRunStatus.Stopped: return "止";
                case ScenarioItemRunStatus.Failed:  return "×";
                default:                            return "・";
            }
        }

        private static Color StatusColor(ScenarioItemRunStatus s)
        {
            switch (s)
            {
                case ScenarioItemRunStatus.Done:    return new Color(0.6f, 0.9f, 0.6f);
                case ScenarioItemRunStatus.Running: return new Color(0.75f, 0.85f, 1f);
                case ScenarioItemRunStatus.Stopped: return new Color(1f, 0.85f, 0.4f);
                case ScenarioItemRunStatus.Failed:  return new Color(1f, 0.55f, 0.45f);
                default:                            return new Color(0.75f, 0.75f, 0.75f);
            }
        }

        // ================================================================
        // 小物
        // ================================================================

        private static string Or(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;

        private static string Shorten(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";

        private static string Join(List<string> values)
            => (values == null || values.Count == 0) ? "（無し）" : string.Join(" / ", values);

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
            return l;
        }
    }
}
