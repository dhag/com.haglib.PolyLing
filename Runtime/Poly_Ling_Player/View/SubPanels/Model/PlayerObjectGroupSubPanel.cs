// PlayerObjectGroupSubPanel.cs
// オブジェクトグループ（入力ソース＋生成パラメータ＋出力先）の一覧と操作。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【できること】
//   ・グループの一覧と、ソースが変わっているか（要更新）の表示
//   ・作り直し（退避を残すかを選べる）。ステップが複数あるものは順に実行する
//   ・1 つ上のグループへ足してマクロにする
//   ・自動更新の切り替え（立てると、はしごなどのソースをスキンド化したときに流れる）
//   ・解除（描画オブジェクトは消さない）
//   ・参照切れのグループの片づけ
//
// 【マクロの組み方】
//   生成コマンドは 1 つで 1 グループを作る。順に並べたいときは、
//   下のグループを選んで「1つ上へ足す」を押す。上のグループの末尾へ
//   ステップが移り、下のグループは消える（描画オブジェクトは残る）。
//   実行順はステップの並びそのもの。
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
        public Func<ProjectContext> GetProject;

        /// <summary>コマンド送信。</summary>
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private ProjectContext Project     => GetProject?.Invoke();
        private ModelContext   CurrentModel => Project?.CurrentModel;
        private int            ModelIndex   => Project?.CurrentModelIndex ?? 0;

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

            var project = Project;
            var model   = CurrentModel;

            if (model?.ObjectGroups != null)
            {
                foreach (var g in model.ObjectGroups)
                {
                    if (g == null) continue;

                    // 出力先はステップごとに複数ありうる。1 つでも引けなければ印を立てる。
                    bool outMissing = !g.HasOutput;
                    if (!outMissing)
                    {
                        foreach (ulong oid in g.OutputObjectIds)
                            if (ObjectGroupOps.Resolve(project, oid) == null) { outMissing = true; break; }
                    }

                    var row = new Row
                    {
                        Name          = g.Name,
                        Action        = g.Action,
                        Stale         = !outMissing && ObjectGroupOps.IsStale(project, g),
                        OutputMissing = outMissing,
                    };
                    _rows.Add(row);

                    string mark = row.OutputMissing ? "[出力先なし] "
                                : row.Stale        ? "[要更新] "
                                : "";
                    string steps = g.StepCount > 1 ? $" [{g.StepCount} ステップ]" : "";
                    _labels.Add($"{mark}{row.Name}  ({row.Action}){steps}");
                }
            }

            if (_selected >= _labels.Count) _selected = -1;

            _listView?.Rebuild();
            if (_listView != null) _listView.selectedIndex = _selected;

            UpdateDetail();
        }

        private ObjectGroup SelectedGroup()
        {
            var model = CurrentModel;
            if (model?.ObjectGroups == null) return null;
            if (_selected < 0 || _selected >= _rows.Count) return null;
            return model.FindObjectGroupByName(_rows[_selected].Name);
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
                return;
            }

            _autoUpdateToggle?.SetValueWithoutNotify(g.AutoUpdate);

            var project = Project;
            var stashCtx = g.HasStash ? ObjectGroupOps.Resolve(project, g.StashObjectId) : null;

            var srcNames = new List<string>();
            foreach (ulong id in g.SourceObjectIds)
            {
                var mc = ObjectGroupOps.Resolve(project, id);
                srcNames.Add(mc != null ? mc.Name : $"(見つからない: {id})");
            }

            // ステップごとに action と出力先を出す。実行順は並びそのもの。
            var stepLines = new List<string>();
            for (int i = 0; i < g.StepCount; i++)
            {
                var st = g.GetStep(i);
                if (st == null) continue;

                var outNames = new List<string>();
                foreach (ulong oid in st.OutputObjectIds)
                {
                    var mc = ObjectGroupOps.Resolve(project, oid);
                    outNames.Add(mc != null ? mc.Name : $"(見つからない: {oid})");
                }

                string outText = outNames.Count == 0 ? "なし"
                    : outNames.Count <= 3 ? string.Join(", ", outNames)
                    : $"{outNames[0]} ほか {outNames.Count - 1} 件";

                stepLines.Add($"  {i + 1}. {st.Action} → {outText}  (パラメータ {st.Args.Count} 件)");
            }

            _detailLabel.text =
                  $"ステップ: {g.StepCount} 件\n"
                + string.Join("\n", stepLines) + "\n"
                + $"入力: {(srcNames.Count > 0 ? string.Join(", ", srcNames) : "なし")}\n"
                + $"退避: {(stashCtx != null ? stashCtx.Name : "なし")}";
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
                ? $"「{g.Name}」を {g.StepCount} ステップ実行しました"
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
              + "実行順はステップの並びそのものです。",
                "足す", "キャンセル");
            if (!ok) return;

            SendCmd(new MergeObjectGroupCommand(ModelIndex, targetName, sourceName));
            SetStatus($"「{sourceName}」を「{targetName}」へ足しました");
            _selected = -1;
            Refresh();
        }

        private void OnPurge()
        {
            var project = Project;
            var model   = CurrentModel;
            if (model == null) { SetStatus("モデルがありません"); return; }

            int n = ObjectGroupOps.PurgeMissing(project, model);
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
