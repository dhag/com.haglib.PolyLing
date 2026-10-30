// PlayerObjectGroupSubPanel.cs
// オブジェクトグループ（入力ソース＋生成パラメータ＋出力先）の一覧と操作。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【できること】
//   ・グループの一覧と、ソースが変わっているか（要更新）の表示
//   ・作り直し（退避を残すかを選べる）。項目が複数あるものは順に実行する
//   ・1 つ上のグループへ足してマクロにする
//   ・自動更新の切り替え（立てると、はしごなどのソースをスキンド化したときに流れる）
//   ・解除（描画オブジェクトは消さない）
//   ・参照切れのグループの片づけ
//
// 【マクロの組み方】
//   生成コマンドは 1 つで 1 グループを作る。順に並べたいときは、
//   下のグループを選んで「1つ上へ足す」を押す。上のグループの末尾へ
//   項目が移り、下のグループは消える（描画オブジェクトは残る）。
//   実行順は項目の並びそのもの。
//
// 【要更新の判定を毎フレームやらない】
//   ObjectGroupOps.IsStale はソースの全頂点を走査する。
//   パネルを開いたとき（Refresh）と、更新ボタンを押したときにだけ呼ぶ。
//   編集のたびに印を立てるフックは置かない。
//
// 【解除の確認】
//   解除するとパラメータが復元できなくなるので、他のサブパネルの削除と
//   同じく PLEditorBridge.I.DisplayDialogYesNo で確認してから送る。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Ops;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerObjectGroupSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>プロジェクト取得。参照の解決にモデルをまたぐため ProjectContext が要る。</summary>
        public Func<Poly_Ling.View.IProjectView> GetProject;

        /// <summary>コマンド送信。</summary>
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private Poly_Ling.View.IProjectView Project      => GetProject?.Invoke();
        private Poly_Ling.View.IModelView   CurrentModel => Project?.CurrentModel;
        private int                         ModelIndex   => Project?.CurrentModelIndex ?? 0;

        // ================================================================
        // 内部状態
        // ================================================================

        // UI 自動操作の ID は "objectGroup.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("groups", Description = "オブジェクトグループ（一覧の行番号）")]
        private ListView  _listView;
        [UiControl("detail", Safety = UiSafety.ReadOnly, Description = "選んだグループの詳細")]
        private Label     _detailLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label     _statusLabel;
        [UiControl("keepStash", Description = "作り直す前の出力先を退避として残す")]
        private Toggle    _keepStashToggle;
        [UiControl("autoUpdate", Description = "ソースが変わったら自動で作り直す（スキンド化のとき）")]
        private Toggle    _autoUpdateToggle;
        [UiControl("rebuild", Safety = UiSafety.Destructive,
                   Description = "選んだグループの出力を作り直す（「退避として残す」がオフなら前の出力は残らない）")]
        private Button    _btnRebuild;
        [UiControl("release", Safety = UiSafety.Destructive, Description = "選んだグループを解除する")]
        private Button    _btnRelease;
        [UiControl("purge", Safety = UiSafety.Destructive, Description = "参照切れを片づける")]
        private Button    _btnPurge;
        [UiControl("mergeUp", Safety = UiSafety.SafeWrite, Description = "1 つ上のグループへ足す（マクロにする）")]
        private Button    _btnMerge;

        // ── 項目の引数（段 3）
        [UiControl("argStep", Description = "引数を書き換える項目")]
        private DropdownField _argStepDropdown;
        [UiControl("argKey", Description = "書き換える引数のキー（書けるものだけ）")]
        private DropdownField _argKeyDropdown;
        [UiControl("argValue", Description = "書く値（コマンドの型で読み直せること）")]
        private TextField     _argValueField;
        [UiControl("argWrite", Safety = UiSafety.Destructive,
                   Description = "引数を書いて、その項目だけを作り直す")]
        private Button        _btnArgWrite;

        // ── 出力の編集（逆方向。段 6）。対象は「項目の引数」で選んだ項目。
        [UiControl("inverseInfo", Safety = UiSafety.ReadOnly, Description = "選んだ項目の逆方向（保つもの・失うもの・未確定か）")]
        private Label  _inverseInfo;
        [UiControl("inverseCommit", Safety = UiSafety.Destructive,
                   Description = "出力の位置の編集を確定する（入力を書き直し、出力と下流を作り直す）")]
        private Button _btnInverseCommit;
        [UiControl("inverseDiscard", Safety = UiSafety.Destructive,
                   Description = "出力の未確定の編集を捨て、今の入力から作り直す")]
        private Button _btnInverseDiscard;

        /// <summary>表示用の 1 行。Refresh のたびに作り直す。</summary>
        private struct Row
        {
            public string Name;
            public string Action;
            public bool   Stale;
            public bool   OutputMissing;
        }

        private readonly List<Row>    _rows  = new List<Row>();
        private readonly List<string> _labels = new List<string>();
        private int _selected = -1;

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("オブジェクトグループ"));

            var hint = new Label(
                "帯・断面・パラメータと出力先をひとまとめにしたものです。\n"
              + "ソースを直したあと「作り直す」を押すと、出力先の中身が入れ替わります"
              + "（オブジェクトは作り直されないので、名前・階層・姿勢はそのまま）。\n"
              + "出力先に頂点IDを手で振るなら、先にグループを解除してください。"
              + "グループがある間は作り直しで中身が総入れ替えされます。");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize   = 10;
            hint.style.marginBottom = 4;
            root.Add(hint);

            _listView = new ListView(_labels, 22,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _labels.Count) return;
                    l.text = _labels[i];
                    // 要更新は色で分かるようにする。文字だけだと一覧で埋もれる。
                    if (i < _rows.Count && _rows[i].OutputMissing)
                        l.style.color = new StyleColor(new Color(1f, 0.45f, 0.4f));
                    else if (i < _rows.Count && _rows[i].Stale)
                        l.style.color = new StyleColor(new Color(1f, 0.78f, 0.35f));
                    else
                        l.style.color = new StyleColor(Color.white);
                });
            _listView.selectionType   = SelectionType.Single;
            _listView.style.minHeight = 70;
            _listView.style.maxHeight = 160;
            _listView.style.marginBottom = 4;
            _listView.selectionChanged += _ =>
            {
                _selected = _listView.selectedIndex;
                UpdateDetail();
            };
            root.Add(_listView);

            _detailLabel = new Label();
            _detailLabel.style.whiteSpace = WhiteSpace.Normal;
            _detailLabel.style.fontSize   = 10;
            _detailLabel.style.marginBottom = 4;
            root.Add(_detailLabel);

            // 流れる契機は「ソースの頂点を書き換える操作の側」が持つ。
            // 今はスキンド化の 2 経路から。編集のたびには流れない。
            _autoUpdateToggle = new Toggle("ソースが変わったら自動で作り直す（スキンド化のとき）");
            _autoUpdateToggle.style.fontSize = 10;
            _autoUpdateToggle.RegisterValueChangedCallback(e =>
            {
                var g = SelectedGroup();
                if (g == null || g.AutoUpdate == e.newValue) return;
                SendCmd(new SetObjectGroupAutoUpdateCommand(ModelIndex, g.Name, e.newValue));
                Refresh();
            });
            root.Add(_autoUpdateToggle);

            _keepStashToggle = new Toggle("作り直す前の出力先を退避として残す");
            _keepStashToggle.style.fontSize = 10;
            _keepStashToggle.style.marginBottom = 3;
            root.Add(_keepStashToggle);

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;

            _btnRebuild = MkBtn("作り直す", OnRebuild); _btnRebuild.style.flexGrow = 1; opRow.Add(_btnRebuild);
            _btnRelease = MkBtn("解除",     OnRelease); _btnRelease.style.flexGrow = 1; opRow.Add(_btnRelease);
            root.Add(opRow);

            _btnMerge = MkBtn("1つ上のグループへ足す（マクロにする）", OnMerge);
            _btnMerge.style.marginBottom = 3;
            root.Add(_btnMerge);

            _btnPurge = MkBtn("参照切れを片づける", OnPurge);
            _btnPurge.style.marginBottom = 3;
            root.Add(_btnPurge);

            // ── 項目の引数：書いたらその項目だけ作り直す。入力・出力先の参照と入力の中身は出さない。
            root.Add(SecLabel("項目の引数"));

            _argStepDropdown = new DropdownField("項目", new List<string>(), -1);
            _argStepDropdown.style.fontSize = 10;
            _argStepDropdown.RegisterValueChangedCallback(_ => UpdateArgKeys());
            root.Add(_argStepDropdown);

            _argKeyDropdown = new DropdownField("キー", new List<string>(), -1);
            _argKeyDropdown.style.fontSize = 10;
            _argKeyDropdown.RegisterValueChangedCallback(_ => UpdateArgValue());
            root.Add(_argKeyDropdown);

            _argValueField = new TextField("値");
            _argValueField.style.fontSize = 10;
            root.Add(_argValueField);

            _btnArgWrite = MkBtn("書いて作り直す", OnArgWrite);
            _btnArgWrite.style.marginBottom = 3;
            root.Add(_btnArgWrite);

            // ── 出力の編集（逆方向）：逆方向を持つ項目は出力の頂点の位置を編集でき、確定で入力へ戻す。
            root.Add(SecLabel("出力の編集（逆方向）"));

            _inverseInfo = new Label();
            _inverseInfo.style.whiteSpace = WhiteSpace.Normal;
            _inverseInfo.style.fontSize   = 10;
            _inverseInfo.style.marginBottom = 3;
            root.Add(_inverseInfo);

            var invRow = new VisualElement();
            invRow.style.flexDirection = FlexDirection.Row;
            invRow.style.marginBottom  = 3;
            _btnInverseCommit  = MkBtn("確定（入力へ戻す）", OnInverseCommit);  _btnInverseCommit.style.flexGrow  = 1; invRow.Add(_btnInverseCommit);
            _btnInverseDiscard = MkBtn("破棄",               OnInverseDiscard); _btnInverseDiscard.style.flexGrow = 1; invRow.Add(_btnInverseDiscard);
            root.Add(invRow);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>
        /// 一覧を作り直す。要更新の判定はここでだけ行う（ソースの全頂点を走査するため）。
        /// </summary>
        public void Refresh()
        {
            _rows.Clear();
            _labels.Clear();
            _groups = CurrentModel?.ObjectGroups ?? System.Array.Empty<Poly_Ling.View.ObjectGroupView>();

            foreach (var g in _groups)
            {
                var row = new Row
                {
                    Name          = g.Name,
                    Action        = g.Action,
                    Stale         = g.Stale,
                    OutputMissing = g.OutputMissing,
                };
                _rows.Add(row);

                string mark = row.OutputMissing ? "[出力先なし] "
                            : row.Stale        ? "[要更新] "
                            : "";
                string steps = g.StepCount > 1 ? $" [{g.StepCount} 項目]" : "";
                _labels.Add($"{mark}{row.Name}  ({row.Action}){steps}");
            }

            if (_selected >= _labels.Count) _selected = -1;

            _listView?.Rebuild();
            if (_listView != null) _listView.selectedIndex = _selected;

            UpdateDetail();
        }

        /// <summary>直近の Refresh で読んだグループの写し。</summary>
        private IReadOnlyList<Poly_Ling.View.ObjectGroupView> _groups
            = System.Array.Empty<Poly_Ling.View.ObjectGroupView>();

        private Poly_Ling.View.ObjectGroupView SelectedGroup()
        {
            if (_selected < 0 || _selected >= _rows.Count) return null;
            string name = _rows[_selected].Name;
            foreach (var g in _groups) if (g.Name == name) return g;
            return null;
        }

        private void UpdateDetail()
        {
            var g = SelectedGroup();
            bool has = g != null;

            if (_btnRebuild != null) _btnRebuild.SetEnabled(has);
            if (_btnRelease != null) _btnRelease.SetEnabled(has);

            // 足し先は 1 つ上のグループ。先頭のグループには足し先が無い。
            if (_btnMerge != null) _btnMerge.SetEnabled(has && _selected >= 1);

            if (!has)
            {
                if (_detailLabel != null) _detailLabel.text = "";
                _autoUpdateToggle?.SetValueWithoutNotify(false);
                UpdateArgSteps(null);
                return;
            }

            _autoUpdateToggle?.SetValueWithoutNotify(g.AutoUpdate);
            _detailLabel.text = g.Detail;
            UpdateArgSteps(g);
        }

        // ================================================================
        // 項目の引数
        // ================================================================

        /// <summary>項目の選択肢を作り直す。前に選んでいた項目番号は保つ。</summary>
        private void UpdateArgSteps(Poly_Ling.View.ObjectGroupView g)
        {
            if (_argStepDropdown == null) return;

            int prev = _argStepDropdown.index;
            var choices = new List<string>();
            if (g?.Steps != null)
                for (int i = 0; i < g.Steps.Length; i++)
                    choices.Add($"{i}: {g.Steps[i]?.Action}");

            _argStepDropdown.choices = choices;
            int idx = choices.Count == 0 ? -1 : (prev >= 0 && prev < choices.Count ? prev : 0);
            _argStepDropdown.SetValueWithoutNotify(idx >= 0 ? choices[idx] : "");
            UpdateArgKeys();
        }

        private Poly_Ling.View.ObjectGroupStepView SelectedArgStep()
        {
            var g = SelectedGroup();
            int si = _argStepDropdown?.index ?? -1;
            if (g?.Steps == null || si < 0 || si >= g.Steps.Length) return null;
            return g.Steps[si];
        }

        /// <summary>書けるキーだけを選択肢にする。前に選んでいたキーは保つ。</summary>
        private void UpdateArgKeys()
        {
            if (_argKeyDropdown == null) return;

            string prev = _argKeyDropdown.value;
            var choices = new List<string>();
            var st = SelectedArgStep();
            if (st?.Keys != null)
                for (int k = 0; k < st.Keys.Length; k++)
                    if (k < st.Writable.Length && st.Writable[k]) choices.Add(st.Keys[k]);

            _argKeyDropdown.choices = choices;
            string v = choices.Contains(prev) ? prev : (choices.Count > 0 ? choices[0] : "");
            _argKeyDropdown.SetValueWithoutNotify(v);
            _btnArgWrite?.SetEnabled(choices.Count > 0);
            UpdateArgValue();
            UpdateInverse();
        }

        /// <summary>選んだ項目の逆方向の表示とボタンを合わせる。</summary>
        private void UpdateInverse()
        {
            if (_inverseInfo == null) return;
            var st = SelectedArgStep();
            bool has = st != null && !string.IsNullOrEmpty(st.InverseMethod);
            if (!has)
            {
                _inverseInfo.text = st == null ? "" : "この項目は逆方向を持ちません（出力は編集できません）";
                _btnInverseCommit?.SetEnabled(false);
                _btnInverseDiscard?.SetEnabled(false);
                return;
            }
            _inverseInfo.text =
                (st.OutputEditPending ? "出力の編集が未確定です。確定するまで、この項目と下流は作り直されません。\n"
                                      : "出力の頂点の位置を編集すると未確定になります。\n")
              + $"保つもの：{st.InverseKeeps}\n失うもの：{st.InverseLoses}\n"
              + "確定すると、出力は入力から作り直した形に置き換わります（編集は丸められます）。";
            _btnInverseCommit?.SetEnabled(st.OutputEditPending);
            _btnInverseDiscard?.SetEnabled(st.OutputEditPending);
        }

        private void OnInverseCommit()
        {
            var g = SelectedGroup();
            int si = _argStepDropdown?.index ?? -1;
            if (g == null || si < 0) { SetStatus("グループと項目を選んでください"); return; }
            SendCmd(new CommitModifierOutputEditCommand(ModelIndex, g.Name, si));
            SetStatus($"「{g.Name}」項目 {si} の出力の編集の確定を送りました");
            Refresh();
        }

        private void OnInverseDiscard()
        {
            var g = SelectedGroup();
            int si = _argStepDropdown?.index ?? -1;
            if (g == null || si < 0) { SetStatus("グループと項目を選んでください"); return; }
            SendCmd(new DiscardModifierOutputEditCommand(ModelIndex, g.Name, si));
            SetStatus($"「{g.Name}」項目 {si} の出力の編集の破棄を送りました");
            Refresh();
        }

        /// <summary>選んだキーの今の値を入力欄へ出す。</summary>
        private void UpdateArgValue()
        {
            if (_argValueField == null) return;
            var st = SelectedArgStep();
            string key = _argKeyDropdown?.value;
            string val = "";
            if (st?.Keys != null && !string.IsNullOrEmpty(key))
                for (int k = 0; k < st.Keys.Length; k++)
                    if (st.Keys[k] == key) { val = k < st.Values.Length ? st.Values[k] : ""; break; }
            _argValueField.SetValueWithoutNotify(val);
        }

        private void OnArgWrite()
        {
            var g = SelectedGroup();
            if (g == null) { SetStatus("グループを選んでください"); return; }
            int si = _argStepDropdown?.index ?? -1;
            if (si < 0) { SetStatus("項目を選んでください"); return; }
            string key = _argKeyDropdown?.value;
            if (string.IsNullOrEmpty(key)) { SetStatus("書けるキーがありません"); return; }
            string val = _argValueField?.value ?? "";

            SendCmd(new SetObjectGroupStepArgsCommand(ModelIndex, g.Name, si, new[] { key }, new[] { val }));
            SetStatus($"「{g.Name}」項目 {si} の {key} を書いて作り直しを送りました");
            Refresh();
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnRebuild()
        {
            var g = SelectedGroup();
            if (g == null) { SetStatus("グループを選んでください"); return; }

            bool keepStash = _keepStashToggle != null && _keepStashToggle.value;
            SendCmd(new RebuildObjectGroupCommand(ModelIndex, g.Name, keepStash));
            SetStatus(g.StepCount > 1
                ? $"「{g.Name}」の {g.StepCount} 項目を実行しました"
                : $"「{g.Name}」を作り直しました");
            Refresh();
        }

        private void OnRelease()
        {
            var g = SelectedGroup();
            if (g == null) { SetStatus("グループを選んでください"); return; }

            // 解除するとパラメータは復元できない。作り直したいなら同じ操作を
            // 最初からやり直すことになるので、必ず確認を通す。
            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "グループ解除の確認",
                $"「{g.Name}」を解除します。\n\n"
              + "帯・断面・パラメータの記録が失われ、作り直せなくなります。\n"
              + "（出力先の描画オブジェクトは残ります）\n\n"
              + "出力先に頂点IDを手で振る場合は、解除してからにしてください。",
                "解除", "キャンセル");
            if (!ok) return;

            SendCmd(new DeleteObjectGroupCommand(ModelIndex, g.Name));
            SetStatus($"「{g.Name}」を解除しました");
            _selected = -1;
            Refresh();
        }

        /// <summary>
        /// 選んだグループを 1 つ上のグループの末尾へ足す。
        /// 足したあと、選んだグループは消える（描画オブジェクトは残る）。
        /// </summary>
        private void OnMerge()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            if (_selected < 1 || _selected >= _rows.Count)
            { SetStatus("2 番目以降のグループを選んでください"); return; }

            string sourceName = _rows[_selected].Name;
            string targetName = _rows[_selected - 1].Name;

            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "グループ結合の確認",
                $"「{sourceName}」を「{targetName}」の末尾へ足します。\n\n"
              + $"「{sourceName}」のグループは消えます（描画オブジェクトは残ります）。\n"
              + "実行順は項目の並びそのものです。",
                "足す", "キャンセル");
            if (!ok) return;

            SendCmd(new MergeObjectGroupCommand(ModelIndex, targetName, sourceName));
            SetStatus($"「{sourceName}」を「{targetName}」へ足しました");
            _selected = -1;
            Refresh();
        }

        private void OnPurge()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            int before = model.ObjectGroups.Count;
            SendCmd(new PurgeObjectGroupsCommand(ModelIndex));
            int n = before - (CurrentModel?.ObjectGroups.Count ?? before);
            SetStatus(n > 0 ? $"参照切れのグループを {n} 件片づけました" : "参照切れはありませんでした");
            Refresh();
        }

        // ================================================================
        // 小物
        // ================================================================

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
