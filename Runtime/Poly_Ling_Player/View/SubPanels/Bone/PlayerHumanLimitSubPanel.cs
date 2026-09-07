// PlayerHumanLimitSubPanel.cs
// Humanoid マッスル可動域（HumanLimit）の編集。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置
//
// 【なぜ要るか】
//   可動域は格納（POCO・DTO・CSV/JSON）と Avatar 生成側の受け口だけがあり、
//   PolyLing の中で作る・直す手段が無かった。既存 Avatar から取り込んだ
//   モデルしか可動域を持てず、値を確かめることもできなかった。
//
// 【対象は「選択」で決める】
//   このパネルは独自の対象指定を持たない。3D 画面・メッシュリストと同じ
//   ボーン選択をそのまま「付ける先」にする。可動域はボーンにしか付かない。
//
// 【単位は度】
//   画面もコマンドも度で扱う。格納はラジアン（HumanLimitData の規約）で、
//   変換はコマンドのディスパッチャが行う。ここでは度のまま読み書きする。
//   AxisLength は角度ではないので変換しない。
//
// 【入力欄を Refresh で書き換えない】
//   Refresh は選択やモデル変更のたびに走る。入力中の欄を毎回上書きすると
//   打ち込んだ値が消えるため、欄へ値を入れるのは
//   「一覧の行を選んだとき」と「読み込みボタンを押したとき」だけにする。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerHumanLimitSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ProjectContext> GetProject;
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private ProjectContext GetProj      => GetProject?.Invoke();
        private ModelContext   CurrentModel => GetProj?.CurrentModel;
        private int            ModelIndex   => GetProj?.CurrentModelIndex ?? 0;

        // ================================================================
        // UI
        // ================================================================

        private Label    _targetLabel;
        private Label    _statusLabel;
        private ListView _listView;

        private FloatField _minX, _minY, _minZ;
        private FloatField _maxX, _maxY, _maxZ;
        private FloatField _cenX, _cenY, _cenZ;
        private FloatField _axisLengthField;

        // ================================================================
        // 表示用の控え（Refresh のたびに作り直す）
        // ================================================================

        private readonly List<string> _rows = new List<string>();

        /// <summary>行 → 可動域を持つボーンの masterIndex。</summary>
        private readonly List<int> _rowRefs = new List<int>();

        private int _selectedRow = -1;

        /// <summary>
        /// 一覧の選択変更ハンドラを止める。
        /// 行を選ぶと SelectMeshCommand を送り、その通知で Refresh が走り、
        /// Refresh が SetSelection で選択を戻すため、止めないと往復し続ける。
        /// </summary>
        private bool _suppressListEvents;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(Sec("マッスル可動域の編集"));

            var help = new HelpBox(
                "ボーンごとに「どこまで曲がるか」を決めます。単位は度です。\n"
                + "設定しないボーンは Unity の既定値で動きます。\n"
                + "Humanoid 割当があるボーンにだけ効きます。先に「Humanoid割当」で割り当ててください。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _targetLabel = new Label();
            _targetLabel.style.fontSize     = 10;
            _targetLabel.style.whiteSpace   = WhiteSpace.Normal;
            _targetLabel.style.marginBottom = 4;
            root.Add(_targetLabel);

            BuildListSection(root);
            BuildValueSection(root);
            BuildActionSection(root);
            BuildGuideSection(root);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);
        }

        // ── 一覧 ──────────────────────────────────────────────────────
        private void BuildListSection(VisualElement root)
        {
            var fo = new Foldout { text = "① いま可動域を持つボーン", value = true };

            _listView = new ListView(_rows, 20,
                () => new Label(),
                (elem, i) =>
                {
                    if (elem is Label l && i >= 0 && i < _rows.Count) l.text = _rows[i];
                });
            _listView.selectionType      = SelectionType.Single;
            _listView.style.minHeight    = 80;
            _listView.style.maxHeight    = 160;
            _listView.style.marginBottom = 4;
            _listView.selectionChanged  += OnRowSelectionChanged;
            fo.Add(_listView);
            fo.Add(Hint("行を選ぶと、そのボーンを選択して下の欄に値を読み込みます。"));

            root.Add(fo);
        }

        // ── 値 ────────────────────────────────────────────────────────
        private void BuildValueSection(VisualElement root)
        {
            var fo = new Foldout { text = "② 可動域[度]", value = true };

            _minX = new FloatField("下限 X") { value = 0f };
            _minY = new FloatField("下限 Y") { value = 0f };
            _minZ = new FloatField("下限 Z") { value = 0f };
            fo.Add(_minX); fo.Add(_minY); fo.Add(_minZ);

            _maxX = new FloatField("上限 X") { value = 0f };
            _maxY = new FloatField("上限 Y") { value = 0f };
            _maxZ = new FloatField("上限 Z") { value = 0f };
            fo.Add(_maxX); fo.Add(_maxY); fo.Add(_maxZ);
            fo.Add(Hint(
                "X/Y/Z は Unity のマッスル 3 軸（dof 0/1/2）に対応します。\n"
              + "そのボーンに無い軸は 0,0 のままで構いません。\n"
              + "各軸で 下限 ≦ 上限 にしてください。逆にすると書き込みを断ります。"));

            _cenX = new FloatField("中央 X") { value = 0f };
            _cenY = new FloatField("中央 Y") { value = 0f };
            _cenZ = new FloatField("中央 Z") { value = 0f };
            fo.Add(_cenX); fo.Add(_cenY); fo.Add(_cenZ);
            fo.Add(Hint("Unity の Avatar 画面と同じ「中央」です。ふつうは 0,0,0 のままです。"));

            _axisLengthField = new FloatField("軸長") { value = 0f };
            fo.Add(_axisLengthField);
            fo.Add(Hint("Unity HumanLimit.axisLength。角度ではないので度に直しません。既定 0。"));

            root.Add(fo);
        }

        // ── 操作 ──────────────────────────────────────────────────────
        private void BuildActionSection(VisualElement root)
        {
            var fo = new Foldout { text = "③ 読む・書く・戻す", value = true };

            var row1 = Row();
            row1.Add(Btn("選んだボーンの値を読み込む", OnLoadFromSelection, grow: true));
            fo.Add(row1);
            fo.Add(Hint(
                "選択中のボーンが持つ値を欄へ入れます。持っていなければ 0 のままです。"));

            var row2 = Row();
            row2.Add(Btn("選んだボーンに書き込む", OnApply, grow: true));
            fo.Add(row2);
            fo.Add(Hint("メッシュリストか 3D 画面でボーンを選んでから押します。複数選択できます。"));

            var row3 = Row();
            row3.Add(Btn("既定に戻す", OnClear, grow: true));
            fo.Add(row3);
            fo.Add(Hint(
                "可動域を外して Unity 既定へ戻します。取り消し（Undo）で戻せます。"));

            root.Add(fo);
        }

        // ── 目安 ──────────────────────────────────────────────────────
        private void BuildGuideSection(VisualElement root)
        {
            var fo = new Foldout { text = "使いどころ", value = false };

            fo.Add(Hint(
                "モーションを読み込んだとき、ひじやひざが曲がりすぎる・曲がらない場合に、"
              + "そのボーンだけ可動域を決めます。"));
            fo.Add(Hint(
                "既定のままのボーンは、これまでと同じ動きになります。"
              + "設定したボーンだけが差し替わります。"));
            fo.Add(Hint(
                "書き込んだ値は、Unity クリップの再マッピング（モデルを選び直すなど）を"
              + "したときに再生へ効きます。"));
            fo.Add(Hint(
                "Avatar を書き出すときにも、この値がそのまま Humanoid の可動域になります。"));

            root.Add(fo);
        }

        // ================================================================
        // 再表示
        // ================================================================

        public void Refresh()
        {
            if (_targetLabel == null) return;

            var model = CurrentModel;
            if (model == null)
            {
                _targetLabel.text = "モデルが読み込まれていません。";
                _rows.Clear();
                _rowRefs.Clear();
                RebuildList();
                return;
            }

            // 付ける先の表示
            var targets = SelectedBones(model);
            if (targets.Count == 0)
            {
                _targetLabel.text = "ボーンが選ばれていません。付ける先のボーンを選んでください。";
            }
            else
            {
                var mc = model.GetMeshContext(targets[0]);
                string human = mc?.MeshObject?.HumanBodyBone;
                string humanText = string.IsNullOrEmpty(human)
                    ? "Humanoid 割当なし（このままでは効きません）"
                    : $"Humanoid: {human}";
                _targetLabel.text =
                    $"付ける先: {mc?.Name}（選択 {targets.Count} 件のうち先頭） / {humanText}";
            }

            // 一覧
            _rows.Clear();
            _rowRefs.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (!HumanLimitOps.HasCustomLimit(model, i)) continue;

                var mc = model.GetMeshContext(i);
                var hl = mc.MeshObject.HumanLimit;

                Vector3 mn = hl.Min * Mathf.Rad2Deg;
                Vector3 mx = hl.Max * Mathf.Rad2Deg;

                _rowRefs.Add(i);
                _rows.Add(
                    $"{mc.Name}  下限 {Fmt(mn)}  上限 {Fmt(mx)}");
            }
            if (_rows.Count == 0) _rows.Add("可動域を持つボーンはありません（すべて既定）。");

            RebuildList();
        }

        private void RebuildList()
        {
            if (_listView == null) return;

            _suppressListEvents = true;
            try
            {
                _listView.itemsSource = _rows;
                _listView.Rebuild();

                _selectedRow = Mathf.Clamp(_selectedRow, -1, _rowRefs.Count - 1);
                if (_selectedRow >= 0) _listView.SetSelection(_selectedRow);
            }
            finally
            {
                _suppressListEvents = false;
            }
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnLoadFromSelection()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedBones(model);
            if (targets.Count == 0) { SetStatus("ボーンを選んでください。"); return; }

            LoadFields(model, targets[0]);
        }

        private void OnApply()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedBones(model);
            if (targets.Count == 0) { SetStatus("付ける先のボーンを選んでください。"); return; }

            SendCmd(new SetHumanLimitCommand(
                ModelIndex, targets.ToArray(),
                ReadMin(), ReadMax(), ReadCenter(), _axisLengthField.value));

            SetStatus($"{targets.Count} 本に可動域を書き込みました。");
            Refresh();
        }

        private void OnClear()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedBones(model);
            if (targets.Count == 0) { SetStatus("戻すボーンを選んでください。"); return; }

            SendCmd(new ClearHumanLimitCommand(ModelIndex, targets.ToArray()));

            _selectedRow = -1;
            SetStatus("既定へ戻しました。");
            Refresh();
        }

        // ================================================================
        // 一覧の選択
        // ================================================================

        private void OnRowSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            _selectedRow = _listView.selectedIndex;
            if (_selectedRow < 0 || _selectedRow >= _rowRefs.Count) return;

            int master = _rowRefs[_selectedRow];
            LoadFields(CurrentModel, master);

            // 3D 画面・メッシュリストと同じ経路で選び直す。
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Bone, new[] { master }));
        }

        // ================================================================
        // 小物
        // ================================================================

        /// <summary>指定ボーンの可動域を欄へ入れる（ラジアン→度）。</summary>
        private void LoadFields(ModelContext model, int master)
        {
            var hl = (model != null && master >= 0 && master < model.MeshContextCount)
                ? model.GetMeshContext(master)?.MeshObject?.HumanLimit
                : null;

            Vector3 mn = (hl != null) ? hl.Min    * Mathf.Rad2Deg : Vector3.zero;
            Vector3 mx = (hl != null) ? hl.Max    * Mathf.Rad2Deg : Vector3.zero;
            Vector3 ce = (hl != null) ? hl.Center * Mathf.Rad2Deg : Vector3.zero;
            float   ax = (hl != null) ? hl.AxisLength : 0f;

            _minX?.SetValueWithoutNotify(mn.x);
            _minY?.SetValueWithoutNotify(mn.y);
            _minZ?.SetValueWithoutNotify(mn.z);
            _maxX?.SetValueWithoutNotify(mx.x);
            _maxY?.SetValueWithoutNotify(mx.y);
            _maxZ?.SetValueWithoutNotify(mx.z);
            _cenX?.SetValueWithoutNotify(ce.x);
            _cenY?.SetValueWithoutNotify(ce.y);
            _cenZ?.SetValueWithoutNotify(ce.z);
            _axisLengthField?.SetValueWithoutNotify(ax);

            if (hl == null) SetStatus("そのボーンは可動域を持っていません（既定）。");
        }

        private Vector3 ReadMin()    => new Vector3(_minX.value, _minY.value, _minZ.value);
        private Vector3 ReadMax()    => new Vector3(_maxX.value, _maxY.value, _maxZ.value);
        private Vector3 ReadCenter() => new Vector3(_cenX.value, _cenY.value, _cenZ.value);

        /// <summary>
        /// 付ける先。可動域はボーンにしか付かないので、ボーン選択だけを見る。
        /// 判定の正典は HumanLimitOps.IsCarrier。
        /// </summary>
        private static List<int> SelectedBones(ModelContext model)
        {
            var result = new List<int>();
            if (model?.SelectedBoneIndices == null) return result;

            foreach (int i in model.SelectedBoneIndices)
                if (HumanLimitOps.IsCarrier(model, i)) result.Add(i);

            return result;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button Btn(string text, Action action, bool grow = false)
        {
            var b = new Button(action) { text = text };
            b.style.height = 22;
            if (grow) b.style.flexGrow = 1;
            return b;
        }

        private static Label Sec(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label Hint(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            return l;
        }

        private static string Fmt(Vector3 v)
            => $"({Fmt(v.x)}, {Fmt(v.y)}, {Fmt(v.z)})";

        private static string Fmt(float v)
            => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
    }
}
