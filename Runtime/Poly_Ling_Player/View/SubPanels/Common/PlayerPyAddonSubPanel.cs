// PlayerPyAddonSubPanel.cs
// Python アドオンのパネル（右ペイン）と、左ペインのアドオンごとのボタン（PlayerPyAddonQuickButtons）。
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置
//
// 【流れ】
//   アドオンを選ぶ（左ペインのボタン・ドロップダウン）→ 起動していなければ起動し、引数の宣言（describe）を受けて入力欄を作る
//   → 欄を埋めて「実行」→ invokePyAddon（run）。結果は PyAddonHost.Changed で受けて表示する（毎フレームの見回りはしない）。
//   引数の宣言は前回受けたものを設定に残しているので、2 回目からは起動を待たずに欄が出る。
// 【コマンドを通すもの・通さないもの】
//   起動・停止・呼び出しはコマンド（startPyAddon / stopPyAddon / invokePyAddon）を通す（MCP・シナリオからも同じことができる）。
//   Python 環境（フォルダ・python.exe）と利用者のアドオンの登録は、どのプログラムを起動するかを決める設定なので、
//   作業フォルダと同じくコマンドにせず、ダイアログを通す（UserOnly）。

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>Python アドオンの設定・起動・引数・実行。</summary>
    public class PlayerPyAddonSubPanel
    {
        private const string RecentRootKey   = "PyAddon.Root";
        private const string RecentPythonKey = "PyAddon.Python";
        private const string RecentScriptKey = "PyAddon.Script";

        private readonly PyAddonHost _host;
        private readonly Func<PanelCommand, CommandResult> _dispatch;
        private readonly Func<int> _modelIndex;

        private string _selectedId = "";
        private int _runCallNo;
        private int _describeCallNo;
        private readonly Dictionary<string, JToken> _values = new Dictionary<string, JToken>();
        private string _builtSpecSig;

        // UI 自動操作の ID は "pyAddon.<下の Id>"。
        [UiControl(Ignore = true)] private VisualElement _root;
        [UiControl("addon", Safety = UiSafety.SafeWrite, Description = "対象のアドオン（選ぶと起動し、引数の欄を作る）")]
        private DropdownField _addonDropdown;
        [UiControl("state", Safety = UiSafety.ReadOnly, Description = "アドオンの状態（停止・起動中・常駐中・失敗）")]
        private Label _stateLabel;
        [UiControl("start", Safety = UiSafety.SafeWrite, Description = "アドオンを起動する（startPyAddon）")]
        private Button _startBtn;
        [UiControl("stop", Safety = UiSafety.SafeWrite, Description = "アドオンを止める（stopPyAddon）")]
        private Button _stopBtn;
        [UiControl("describe", Safety = UiSafety.SafeWrite, Description = "引数の宣言をアドオンから読み直す（invokePyAddon action=describe）")]
        private Button _describeBtn;
        [UiControl(Ignore = true)] private VisualElement _argsBox;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "欄の値でアドオンを実行する（invokePyAddon）。アドオンはモデルをコマンドで書き換える（Undo に残る）")]
        private Button _runBtn;
        [UiControl("result", Safety = UiSafety.ReadOnly, Description = "直近の実行の結果")]
        private Label _resultLabel;
        [UiControl("output", Safety = UiSafety.ReadOnly, Description = "アドオンの出力（最後の数行）")]
        private Label _outputLabel;

        [UiControl("root", Safety = UiSafety.ReadOnly, Description = "poly_ling_py リポジトリのフォルダ")]
        private Label _rootLabel;
        [UiControl("selectRoot", Safety = UiSafety.UserOnly, Description = "poly_ling_py リポジトリのフォルダを選ぶ（フォルダ選択ダイアログ）")]
        private Button _selectRootBtn;
        [UiControl("python", Safety = UiSafety.ReadOnly, Description = "使う python の実行ファイル")]
        private Label _pythonLabel;
        [UiControl("selectPython", Safety = UiSafety.UserOnly, Description = "python の実行ファイルを選ぶ（ファイル選択ダイアログ）")]
        private Button _selectPythonBtn;
        [UiControl("clearPython", Safety = UiSafety.UserOnly, Description = "python の指定を外し、フォルダの .venv を使う")]
        private Button _clearPythonBtn;
        [UiControl("addUser", Safety = UiSafety.UserOnly, Description = "自作のアドオン（スクリプト）を登録する（ファイル選択ダイアログ）")]
        private Button _addUserBtn;
        [UiControl("removeUser", Safety = UiSafety.UserOnly, Description = "選んでいる自作のアドオンを登録から外す")]
        private Button _removeUserBtn;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;

        /// <summary>引数の欄（アドオンの宣言から作る）。ID は "arg.&lt;名前&gt;"、ファイルを選ぶボタンは "pick.&lt;名前&gt;"。</summary>
        private readonly UiDynamicControls _argControls = new UiDynamicControls();

        public PlayerPyAddonSubPanel(PyAddonHost host, Func<PanelCommand, CommandResult> dispatch, Func<int> modelIndex)
        {
            _host = host;
            _dispatch = dispatch;
            _modelIndex = modelIndex;
        }

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Python アドオン"));
            _root.Add(Note("Python で書いたアドオンを PolyLing から起動して呼びます。起動していなければ、選んだ Python（venv）で" +
                           "子プロセスとして起動します。アドオンはリモートサーバにつないでコマンドでモデルを書き換えます（Undo で戻せます）。"));

            _addonDropdown = new DropdownField("アドオン", new List<string>(), 0);
            _addonDropdown.RegisterValueChangedCallback(e => SelectByTitle(e.newValue));
            _root.Add(_addonDropdown);

            _stateLabel = new Label();
            _stateLabel.style.fontSize = 11; _stateLabel.style.whiteSpace = WhiteSpace.Normal; _stateLabel.style.marginBottom = 2;
            _root.Add(_stateLabel);

            var row = Row();
            _startBtn    = Btn("起動", OnStart);     row.Add(_startBtn);
            _stopBtn     = Btn("停止", OnStop);      row.Add(_stopBtn);
            _describeBtn = Btn("引数を読み直す", OnDescribe); row.Add(_describeBtn);
            _root.Add(row);

            _root.Add(Header("引数"));
            _argsBox = new VisualElement();
            _argsBox.style.marginBottom = 4;
            _root.Add(_argsBox);

            _runBtn = Btn("実行", OnRun);
            _runBtn.style.height = 24;
            _root.Add(_runBtn);

            _resultLabel = new Label();
            _resultLabel.style.fontSize = 10; _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.marginTop = 2; _resultLabel.style.marginBottom = 4;
            _root.Add(_resultLabel);

            _outputLabel = new Label();
            _outputLabel.style.fontSize = 9; _outputLabel.style.whiteSpace = WhiteSpace.Normal;
            _outputLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _outputLabel.style.marginBottom = 6;
            _root.Add(_outputLabel);

            _root.Add(Header("Python 環境"));
            _rootLabel = Small(); _root.Add(_rootLabel);
            _selectRootBtn = Btn("poly_ling_py のフォルダを選ぶ…", OnSelectRoot); _root.Add(_selectRootBtn);
            _pythonLabel = Small(); _root.Add(_pythonLabel);
            var prow = Row();
            _selectPythonBtn = Btn("python を選ぶ…", OnSelectPython); prow.Add(_selectPythonBtn);
            _clearPythonBtn  = Btn(".venv を使う", OnClearPython);   prow.Add(_clearPythonBtn);
            _root.Add(prow);

            _root.Add(Header("自作のアドオン"));
            _root.Add(Note("スクリプトのファイル名（拡張子なし）が id です。Python 側の Addon(id=…) も同じにしてください。"));
            var urow = Row();
            _addUserBtn    = Btn("スクリプトを登録…", OnAddUser);  urow.Add(_addUserBtn);
            _removeUserBtn = Btn("登録から外す", OnRemoveUser);    urow.Add(_removeUserBtn);
            _root.Add(urow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10; _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);

            if (_host != null) _host.Changed += OnHostChanged;
            var all = _host?.Settings.All();
            if (all != null && all.Count > 0) _selectedId = all[0].Id;
            Refresh();
        }

        // ================================================================
        // 外から
        // ================================================================

        /// <summary>アドオンを選んで、起動していなければ起動し、宣言が無ければ読む（左ペインのボタン）。</summary>
        public void Open(string id)
        {
            if (_host?.Settings.Find(id) == null) return;
            if (_selectedId != id) { _selectedId = id; _builtSpecSig = null; }
            Refresh();
            EnsureStartedAndDescribed();
        }

        /// <summary>今の状態で表示を組み直す。</summary>
        public void Refresh()
        {
            if (_root == null || _host == null) return;
            var s = _host.Settings;
            var all = s.All();
            if (s.Find(_selectedId) == null) _selectedId = all.Count > 0 ? all[0].Id : "";

            var titles = new List<string>();
            string selTitle = "";
            foreach (var d in all)
            {
                string t = TitleOf(d);
                titles.Add(t);
                if (d.Id == _selectedId) selTitle = t;
            }
            _addonDropdown.choices = titles;
            _addonDropdown.SetValueWithoutNotify(selTitle);

            _rootLabel.text = "フォルダ: " + (string.IsNullOrEmpty(s.Root) ? "未設定" : s.Root);
            string py = s.ResolvePython();
            _pythonLabel.text = "python: " + (string.IsNullOrEmpty(py) ? "未設定" : py)
                              + (string.IsNullOrEmpty(s.Python) ? "（フォルダの .venv）" : "");
            _clearPythonBtn.SetEnabled(!string.IsNullOrEmpty(s.Python));

            var def = s.Find(_selectedId);
            bool has = def != null;
            string state = has ? _host.StateOf(_selectedId) : PyAddonHost.StateStopped;
            _stateLabel.text = has ? StateText(def, state) : "アドオンがありません";
            _startBtn.SetEnabled(has && (state == PyAddonHost.StateStopped || state == PyAddonHost.StateFailed));
            _stopBtn.SetEnabled(has && (state == PyAddonHost.StateRunning || state == PyAddonHost.StateStarting));
            _describeBtn.SetEnabled(has);
            _removeUserBtn.SetEnabled(has && !def.Official);

            RebuildArgsIfNeeded();
            bool busy = IsBusy(_runCallNo);
            _runBtn.SetEnabled(has && s.Specs.ContainsKey(_selectedId) && !busy);
            _runBtn.text = busy ? "実行中…" : "実行";

            RefreshOutput();
        }

        // ================================================================
        // 引数の欄
        // ================================================================

        private void RebuildArgsIfNeeded()
        {
            JObject spec = null;
            _host.Settings.Specs.TryGetValue(_selectedId ?? "", out spec);
            string sig = _selectedId + "|" + (spec?.ToString(Newtonsoft.Json.Formatting.None) ?? "");
            if (sig == _builtSpecSig) return;
            _builtSpecSig = sig;

            _argsBox.Clear();
            _argControls.Begin(_selectedId ?? "");
            _values.Clear();

            if (spec == null)
            {
                _argsBox.Add(Note(IsBusy(_describeCallNo)
                    ? "アドオンから引数の宣言を受け取っています…"
                    : "引数の宣言がまだありません（「起動」か「引数を読み直す」）。"));
                return;
            }

            if (!string.IsNullOrEmpty((string)spec["description"]))
                _argsBox.Add(Note((string)spec["description"]));

            _host.Settings.LastArgs.TryGetValue(_selectedId, out var last);
            foreach (var a in spec["args"] as JArray ?? new JArray())
            {
                if (!(a is JObject arg)) continue;
                string name = (string)arg["name"];
                if (string.IsNullOrEmpty(name)) continue;
                JToken v = last != null && last.TryGetValue(name, out var lv) ? lv : arg["default"];
                _values[name] = v ?? JValue.CreateNull();
                _argsBox.Add(BuildArgRow(arg, name, v));
            }
        }

        private VisualElement BuildArgRow(JObject arg, string name, JToken v)
        {
            string type  = (string)arg["type"] ?? "text";
            string label = (string)arg["label"] ?? name;
            string help  = (string)arg["help"] ?? "";
            if ((bool?)arg["required"] == true) label += " *";
            string desc  = $"{label}（{type}）" + (help != "" ? "：" + help : "");
            string id    = "arg." + name;

            VisualElement row;
            switch (type)
            {
                case "bool":
                {
                    var t = new Toggle(label) { value = v != null && v.Type == JTokenType.Boolean && (bool)v };
                    t.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    row = _argControls.Add(id, t, desc, UiSafety.SafeWrite);
                    break;
                }
                case "number":
                {
                    var f = new FloatField(label) { value = ToFloat(v) };
                    f.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    row = _argControls.Add(id, f, desc, UiSafety.SafeWrite);
                    break;
                }
                case "int":
                {
                    var f = new IntegerField(label) { value = (int)Math.Round(ToFloat(v)) };
                    f.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    row = _argControls.Add(id, f, desc, UiSafety.SafeWrite);
                    break;
                }
                case "choice":
                {
                    var choices = new List<string>();
                    foreach (var c in arg["choices"] as JArray ?? new JArray()) choices.Add((string)c);
                    string cur = v?.ToString() ?? "";
                    int idx = Math.Max(0, choices.IndexOf(cur));
                    var d = new DropdownField(label, choices, idx);
                    if (choices.Count > 0) _values[name] = choices[idx];
                    d.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    row = _argControls.Add(id, d, desc, UiSafety.SafeWrite);
                    break;
                }
                case "file":
                case "folder":
                {
                    row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    var tf = new TextField(label) { value = v?.ToString() ?? "" };
                    tf.style.flexGrow = 1; tf.style.flexShrink = 1;
                    tf.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    _argControls.Add(id, tf, desc, UiSafety.SafeWrite);
                    string filter = (string)arg["filter"] ?? "";
                    bool folder = type == "folder";
                    var pick = new Button(() => PickPath(name, label, tf, filter, folder)) { text = "…" };
                    pick.style.width = 24;
                    _argControls.Add("pick." + name, pick,
                        $"{label} を{(folder ? "フォルダ" : "ファイル")}選択ダイアログで選ぶ", UiSafety.UserOnly);
                    row.Add(tf); row.Add(pick);
                    break;
                }
                default:
                {
                    var tf = new TextField(label) { value = v?.ToString() ?? "" };
                    tf.RegisterValueChangedCallback(e => _values[name] = e.newValue);
                    row = _argControls.Add(id, tf, desc, UiSafety.SafeWrite);
                    break;
                }
            }
            if (help != "") row.tooltip = help;
            row.style.marginBottom = 1;
            return row;
        }

        private void PickPath(string name, string label, TextField tf, string filter, bool folder)
        {
            string key = $"PyAddon.{_selectedId}.{name}";
            string picked = folder
                ? RecentFileDialog.AskFolder(label, key)
                : RecentFileDialog.AskLoadFrom(label, key, tf.value, filter);
            if (string.IsNullOrEmpty(picked)) return;
            tf.value = picked;     // 変更通知で _values へ入る
        }

        // ================================================================
        // 操作
        // ================================================================

        private void SelectByTitle(string title)
        {
            foreach (var d in _host.Settings.All())
                if (TitleOf(d) == title) { Open(d.Id); return; }
        }

        private void EnsureStartedAndDescribed()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            string state = _host.StateOf(_selectedId);
            if (!_host.Settings.Specs.ContainsKey(_selectedId))
            {
                if (!IsBusy(_describeCallNo)) Describe();
            }
            else if (state == PyAddonHost.StateStopped)
            {
                OnStart();
            }
        }

        private void OnStart()
        {
            SetStatus(Run(new StartPyAddonCommand(_modelIndex(), _selectedId), out _));
            Refresh();
        }

        private void OnStop()
        {
            SetStatus(Run(new StopPyAddonCommand(_modelIndex(), _selectedId), out _));
            Refresh();
        }

        private void OnDescribe()
        {
            Describe();
            Refresh();
        }

        private void Describe()
        {
            string err = Run(new InvokePyAddonCommand(_modelIndex(), _selectedId, PyAddonHost.ActionDescribe), out var data);
            SetStatus(err);
            _describeCallNo = err == null ? (int?)data?["callNo"] ?? 0 : 0;
            _builtSpecSig = null;
        }

        private void OnRun()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            var args = new JObject();
            foreach (var kv in _values) args[kv.Key] = kv.Value;
            _host.Settings.LastArgs[_selectedId] = (JObject)args.DeepClone();
            _host.Settings.Save();

            string err = Run(new InvokePyAddonCommand(_modelIndex(), _selectedId, PyAddonHost.ActionRun,
                                                      args.ToString(Newtonsoft.Json.Formatting.None)), out var data);
            SetStatus(err);
            _runCallNo = err == null ? (int?)data?["callNo"] ?? 0 : 0;
            _resultLabel.text = err == null ? "実行を受け付けました…" : "";
            Refresh();
        }

        private void OnSelectRoot()
        {
            string picked = RecentFileDialog.AskFolder("poly_ling_py のフォルダを選ぶ", RecentRootKey);
            if (string.IsNullOrEmpty(picked)) return;
            _host.Settings.Root = picked.Replace('/', System.IO.Path.DirectorySeparatorChar);
            _host.Settings.Save();
            SetStatus(null);
            Refresh();
        }

        private void OnSelectPython()
        {
            string picked = RecentFileDialog.AskLoadFrom("python の実行ファイルを選ぶ", RecentPythonKey,
                                                         _host.Settings.ResolvePython(), "exe");
            if (string.IsNullOrEmpty(picked)) return;
            _host.Settings.Python = picked.Replace('/', System.IO.Path.DirectorySeparatorChar);
            _host.Settings.Save();
            SetStatus(null);
            Refresh();
        }

        private void OnClearPython()
        {
            _host.Settings.Python = "";
            _host.Settings.Save();
            Refresh();
        }

        private void OnAddUser()
        {
            string picked = RecentFileDialog.AskLoad("アドオンのスクリプトを選ぶ", RecentScriptKey, "py");
            if (string.IsNullOrEmpty(picked)) return;
            string err = _host.Settings.AddUser(picked.Replace('/', System.IO.Path.DirectorySeparatorChar));
            SetStatus(err);
            if (err == null) _selectedId = System.IO.Path.GetFileNameWithoutExtension(picked);
            _builtSpecSig = null;
            Refresh();
            OnQuickChanged?.Invoke();
        }

        private void OnRemoveUser()
        {
            var def = _host.Settings.Find(_selectedId);
            if (def == null || def.Official) return;
            _host.Stop(def.Id);
            SetStatus(_host.Settings.RemoveUser(def.Id));
            _selectedId = "";
            _builtSpecSig = null;
            Refresh();
            OnQuickChanged?.Invoke();
        }

        /// <summary>登録簿が変わった（左ペインのボタンを作り直す）。</summary>
        public Action OnQuickChanged;

        // ================================================================
        // 係からの通知
        // ================================================================

        private void OnHostChanged()
        {
            if (_root == null) return;
            var run = _host.GetCall(_runCallNo);
            if (run != null && (run.Status == PyAddonHost.CallState.Done || run.Status == PyAddonHost.CallState.Failed))
            {
                _resultLabel.text = ResultText(run);
                _resultLabel.style.color = new StyleColor(run.Status == PyAddonHost.CallState.Done
                    ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f));
                _runCallNo = 0;
            }
            var desc = _host.GetCall(_describeCallNo);
            if (desc != null && (desc.Status == PyAddonHost.CallState.Done || desc.Status == PyAddonHost.CallState.Failed))
            {
                if (desc.Status == PyAddonHost.CallState.Failed) SetStatus("引数の宣言を受け取れません: " + desc.Error);
                _describeCallNo = 0;
                _builtSpecSig = null;
            }
            Refresh();
        }

        private void RefreshOutput()
        {
            var lines = _host.OutputOf(_selectedId ?? "");
            var sb = new StringBuilder();
            int skip = Math.Max(0, lines.Count - 8), i = 0;
            foreach (var l in lines) { if (i++ >= skip) sb.AppendLine(l); }
            _outputLabel.text = sb.Length > 0 ? "出力:\n" + sb.ToString().TrimEnd() : "";
        }

        // ================================================================
        // 小道具
        // ================================================================

        /// <summary>コマンドを流す。成功なら null と result（JSON）、失敗なら理由。</summary>
        private string Run(PanelCommand cmd, out JObject data)
        {
            data = null;
            var r = _dispatch?.Invoke(cmd);
            if (r == null) return "コマンドを実行できません";
            if (!r.Success) return r.Reason;
            try { data = string.IsNullOrEmpty(r.Data) ? null : JObject.Parse(r.Data); } catch { }
            return null;
        }

        private bool IsBusy(int callNo)
        {
            var c = callNo > 0 ? _host.GetCall(callNo) : null;
            return c != null && (c.Status == PyAddonHost.CallState.Starting || c.Status == PyAddonHost.CallState.Pending);
        }

        private static string TitleOf(PyAddonDef d)
            => (d.Title ?? d.Id) + (d.Official ? "" : "（自作）");

        private string StateText(PyAddonDef d, string state)
        {
            string s;
            switch (state)
            {
                case PyAddonHost.StateRunning:  s = "常駐中"; break;
                case PyAddonHost.StateStarting: s = "起動中…"; break;
                case PyAddonHost.StateFailed:   s = "失敗: " + _host.LastErrorOf(d.Id); break;
                default:                        s = "停止"; break;
            }
            int pid = _host.ProcessIdOf(d.Id);
            return $"状態: {s}" + (pid > 0 ? $"（pid {pid}）" : "") + "\nスクリプト: " + _host.Settings.ResolveScript(d);
        }

        private static string ResultText(PyAddonHost.CallState c)
        {
            if (c.Status == PyAddonHost.CallState.Failed) return "失敗: " + c.Error;
            try
            {
                var o = JObject.Parse(c.ResultJson ?? "{}");
                var sb = new StringBuilder("完了");
                if (o["log"] is JArray log && log.Count > 0)
                    foreach (var l in log) sb.Append('\n').Append((string)l);
                else if (o["message"] != null) sb.Append(": ").Append((string)o["message"]);
                return sb.ToString();
            }
            catch { return "完了"; }
        }

        private static float ToFloat(JToken v)
        {
            if (v == null) return 0f;
            if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) return (float)v;
            return float.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.marginTop = 6; l.style.marginBottom = 3; l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10; l.style.whiteSpace = WhiteSpace.Normal; l.style.marginBottom = 4;
            l.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            return l;
        }

        private static Label Small()
        {
            var l = new Label();
            l.style.fontSize = 10; l.style.whiteSpace = WhiteSpace.Normal; l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.marginBottom = 4;
            return r;
        }

        private static Button Btn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1;
            return b;
        }
    }

    /// <summary>
    /// 左ペイン「Python アドオン」のアドオンごとのボタン。登録簿で増減するので動的なボタンとして登録する
    /// （ID は "leftPane.pyAddon.&lt;アドオンの id&gt;"）。押すと右ペインのパネルをそのアドオンで開く。
    /// </summary>
    public class PlayerPyAddonQuickButtons
    {
        [UiControl(Ignore = true)] private VisualElement _box;
        private readonly UiDynamicControls _buttons = new UiDynamicControls();
        private readonly PyAddonHost _host;
        private readonly Action<string> _open;
        private string _sig;

        public PlayerPyAddonQuickButtons(VisualElement box, PyAddonHost host, Action<string> open)
        {
            _box = box;
            _host = host;
            _open = open;
            if (_host != null) _host.Changed += Rebuild;
            Rebuild();
        }

        public void Rebuild()
        {
            if (_box == null || _host == null) return;
            var all = _host.Settings.All();
            var sb = new StringBuilder();
            foreach (var d in all) sb.Append(d.Id).Append('=').Append(d.Title).Append(':').Append(_host.StateOf(d.Id)).Append(';');
            if (sb.ToString() == _sig) return;
            _sig = sb.ToString();

            _box.Clear();
            _buttons.Begin("");
            foreach (var d in all)
            {
                string id = d.Id;
                string mark = _host.StateOf(id) == PyAddonHost.StateRunning ? "（常駐中）" : "";
                var b = new Button(() => _open?.Invoke(id)) { text = (d.Title ?? id) + mark };
                b.style.marginBottom = 2; b.style.fontSize = 10; b.style.height = 20;
                b.style.paddingTop = 0; b.style.paddingBottom = 0;
                b.tooltip = $"Python アドオン {id}。押すと右ペインで開き、起動していなければ起動する";
                _buttons.Add(id, b, $"{d.Title ?? id}（Python アドオン。右ペインで開き、起動していなければ起動する）",
                             UiSafety.SafeWrite);
                _box.Add(b);
            }
        }
    }
}
