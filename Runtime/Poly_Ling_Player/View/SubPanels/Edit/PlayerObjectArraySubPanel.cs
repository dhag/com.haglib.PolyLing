// PlayerObjectArraySubPanel.cs
// 歪み複製のサブパネル。ビューポート操作は持たず、数値と一覧だけで完結する。
//
// 【操作の流れ】
//   1. 「複製元オブジェクト」でチェックを入れる（選択とは独立）
//   2. 歪みの種類とパラメータを決める
//   3. 組の数・組ごとの位相ステップ・位置ステップを決める
//   4. 出力先オブジェクトと出力モードを決める
//   5. 「生成」
//
// 歪みは DeformerRegistry (DeformerRegistry.cs:26-32) の登録一覧から選ぶ。
// デフォーマを1つ増やせばここのドロップダウンにも自動で並ぶが、パラメータ欄は
// 型ごとに出し分けるため BuildParamGroups / RefreshParamGroups に1ブロック要る。
// PlayerDeformSubPanel と同じ方式。
//
// 基準フレームは「作業軸」パネルで設定した作業軸。軸ローカルの +Y がライン方向。
//
// 【置き場所】図形生成パネルの「高度な図形」/「新しい高度」の1形状として
//   埋め込まれる（PlayerPrimitiveMeshSubPanel.ObjectArray.cs）。
//   Embedded = true のときは自前のタイトルと「生成」ボタンを出さず、
//   図形生成パネル側の生成ボタンから OnGenerate が呼ばれる。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools.Deformers;
using Poly_Ling.Tools.ObjectArray;

namespace Poly_Ling.Player
{
    public class PlayerObjectArraySubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>描画オブジェクト一覧（表示名, MasterIndex）。</summary>
        public Func<List<(string Label, int MasterIndex)>> GetDrawableList;

        /// <summary>生成ボタン。パネルの状態は Params / Deformer / SelectedMasterIndices から読む。</summary>
        public Action OnGenerate;

        /// <summary>複製元のチェック状態が変わったときに呼ばれる（生成ボタンの有効判定用）。</summary>
        public Action OnSelectionChanged;

        /// <summary>
        /// 図形生成パネルへ埋め込むとき true。タイトルと自前の「生成」ボタンを出さない。
        /// 生成は埋め込み先の生成ボタンから OnGenerate を呼ぶ。Build より前に設定する。
        /// </summary>
        public bool Embedded { get; set; }

        // ================================================================
        // パネルの状態（Viewer から読む）
        // ================================================================

        /// <summary>生成パラメータ。</summary>
        public ObjectArrayParams Params { get; } = new ObjectArrayParams();

        /// <summary>現在選ばれている歪み。</summary>
        public IMeshDeformer Deformer { get; private set; }

        /// <summary>チェックの入っている複製元の MasterIndex（一覧の並び順）。</summary>
        public List<int> SelectedMasterIndices()
        {
            var list = new List<int>();
            foreach (var e in _candidates)
                if (_checkedLabels.Contains(e.Label)) list.Add(e.MasterIndex);
            return list;
        }

        // ================================================================
        // ウィジェット
        // ================================================================

        private VisualElement _root;

        // 複製元（チェックボックス一覧。選択はラベルで保持し、一覧再取得後も復元する）
        private List<(string Label, int MasterIndex)> _candidates = new List<(string, int)>();
        private readonly HashSet<string> _checkedLabels = new HashSet<string>();
        private VisualElement _srcListContainer;
        private Label         _srcCountLabel;

        // 歪み
        private DropdownField _deformerDropdown;

        private VisualElement _rotateGroup;
        private Slider _rotSlX, _rotSlY, _rotSlZ;
        private FloatField _rotFdX, _rotFdY, _rotFdZ;

        private VisualElement _bendGroup;
        private Slider _bendAngleSl, _bendPlaneSl;
        private FloatField _bendAngleFd, _bendPlaneFd;
        private Toggle _bendPivotTg;

        private VisualElement _twistGroup;
        private Slider _twistAngleSl;
        private FloatField _twistAngleFd;
        private Toggle _twistPivotTg;

        private VisualElement _waveGroup;
        private Slider _waveAmpXSl, _waveCycXSl, _wavePhXSl;
        private FloatField _waveAmpXFd, _waveCycXFd, _wavePhXFd;
        private Slider _waveAmpZSl, _waveCycZSl, _wavePhZSl;
        private FloatField _waveAmpZFd, _waveCycZFd, _wavePhZFd;
        private Toggle _wavePivotTg;

        // 複製
        private IntegerField _countField;
        private Slider _phaseStepSl;
        private FloatField _phaseStepFd;
        private FloatField _offsetX, _offsetY, _offsetZ;
        private Toggle _fixOriginToggle;

        // 出力
        private DropdownField _targetDropdown;
        private DropdownField _modeDropdown;
        private Toggle        _groupToggle;
        private TextField     _groupNameField;

        private Label _statusLabel;

        private bool _suppressCallback;

        private static readonly string RootChoice = "(ルート)";

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            if (!Embedded) _root.Add(PlayerIoUiKit.Title("歪み複製 (Object Array)"));

            var help = new HelpBox(
                "複製元にチェックを入れ、歪みを掛けた組を指定した数だけ作ります。\n" +
                "基準は「作業軸」パネルで設定した軸です。\n" +
                "軸ローカルの +Y がライン方向、波はそこへ直交する +X / +Z へ振れます。\n" +
                "組ごとに位相と位置をずらして、同じ形の繰り返しにならないようにします。",
                HelpBoxMessageType.Info);
            help.style.color = new StyleColor(Color.white);
            help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(help);

            BuildSourceList();
            BuildDeformerSection();
            BuildCopySection();
            BuildOutputSection();

            if (!Embedded)
            {
                var genBtn = new Button(() => OnGenerate?.Invoke()) { text = "生成" };
                genBtn.style.height       = 28;
                genBtn.style.marginTop    = 8;
                genBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                genBtn.style.backgroundColor = new StyleColor(new Color(0.22f, 0.48f, 0.22f));
                _root.Add(genBtn);
            }

            _statusLabel = PlayerIoUiKit.StatusLabel();
            _root.Add(_statusLabel);

            SelectDeformer(0);
            Refresh();
        }

        // ================================================================
        // 複製元
        // ================================================================

        private void BuildSourceList()
        {
            _root.Add(PlayerIoUiKit.Divider());
            _root.Add(PlayerIoUiKit.SectionLabel("複製元オブジェクト"));

            _srcListContainer = new VisualElement();
            _srcListContainer.style.marginBottom = 2;
            _root.Add(_srcListContainer);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var refreshBtn = new Button(RefreshSourceList) { text = "一覧を再取得" };
            refreshBtn.style.flexGrow = 1; refreshBtn.style.marginRight = 2;

            var allBtn = new Button(() => SetAllChecked(true))  { text = "全部" };
            allBtn.style.flexGrow = 1; allBtn.style.marginRight = 2;

            var noneBtn = new Button(() => SetAllChecked(false)) { text = "解除" };
            noneBtn.style.flexGrow = 1;

            row.Add(refreshBtn); row.Add(allBtn); row.Add(noneBtn);
            _root.Add(row);

            _srcCountLabel = new Label();
            _srcCountLabel.style.fontSize = 10;
            _srcCountLabel.style.color    = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(_srcCountLabel);
        }

        private void SetAllChecked(bool on)
        {
            _checkedLabels.Clear();
            if (on)
                foreach (var e in _candidates) _checkedLabels.Add(e.Label);
            RefreshSourceList();
        }

        /// <summary>一覧を取り直し、チェック状態をラベルで復元する。</summary>
        private void RefreshSourceList()
        {
            _candidates = GetDrawableList?.Invoke() ?? new List<(string, int)>();

            // 一覧から消えたラベルのチェックは捨てる。
            var alive = new HashSet<string>();
            foreach (var e in _candidates) alive.Add(e.Label);
            _checkedLabels.RemoveWhere(l => !alive.Contains(l));

            if (_srcListContainer != null)
            {
                _srcListContainer.Clear();

                if (_candidates.Count == 0)
                {
                    var empty = new Label("(描画オブジェクトがありません)");
                    empty.style.fontSize = 10;
                    _srcListContainer.Add(empty);
                }
                else
                {
                    foreach (var e in _candidates)
                    {
                        string label = e.Label;
                        var tog = new Toggle(label) { value = _checkedLabels.Contains(label) };
                        tog.style.fontSize = 10;
                        tog.RegisterValueChangedCallback(ev =>
                        {
                            if (ev.newValue) _checkedLabels.Add(label);
                            else             _checkedLabels.Remove(label);
                            RefreshSourceCount();
                        });
                        _srcListContainer.Add(tog);
                    }
                }
            }

            RefreshTargetDropdown();
            RefreshSourceCount();
            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        private void RefreshSourceCount()
        {
            OnSelectionChanged?.Invoke();
            if (_srcCountLabel == null) return;
            _srcCountLabel.text = $"複製元 {SelectedMasterIndices().Count} 個";
        }

        // ================================================================
        // 歪み
        // ================================================================

        private void BuildDeformerSection()
        {
            _root.Add(PlayerIoUiKit.Divider());
            _root.Add(PlayerIoUiKit.SectionLabel("歪み"));

            // 表示は DisplayName（日本語）。選択は index で GetNames を引く。
            var labels = DeformerRegistry.GetDisplayNames();
            _deformerDropdown = new DropdownField("種類", labels, labels.Count > 0 ? 0 : -1);
            _deformerDropdown.style.color = new StyleColor(Color.white);
            _deformerDropdown.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                SelectDeformer(_deformerDropdown.index);
            });
            _root.Add(_deformerDropdown);

            BuildRotateGroup();
            BuildBendGroup();
            BuildTwistGroup();
            BuildWaveGroup();
        }

        /// <summary>
        /// 種類を切り替える。パラメータはデフォーマ自身が持つため作り直す
        /// （前の型の値は残らない）。
        /// </summary>
        private void SelectDeformer(int index)
        {
            var names = DeformerRegistry.GetNames();
            if (names.Count == 0) { Deformer = null; return; }

            index = Mathf.Clamp(index, 0, names.Count - 1);
            Deformer = DeformerRegistry.Create(names[index]);

            var labels = DeformerRegistry.GetDisplayNames();
            _suppressCallback = true;
            try
            {
                if (index < labels.Count)
                    _deformerDropdown?.SetValueWithoutNotify(labels[index]);
            }
            finally { _suppressCallback = false; }

            UpdateGroupVisibility();
            RefreshParamWidgets();
        }

        private void UpdateGroupVisibility()
        {
            Show(_rotateGroup, Deformer is RotateDeformer);
            Show(_bendGroup,   Deformer is BendDeformer);
            Show(_twistGroup,  Deformer is TwistDeformer);
            Show(_waveGroup,   Deformer is WaveDeformer);
        }

        private static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildRotateGroup()
        {
            _rotateGroup = new VisualElement();
            _rotateGroup.Add(Header("回転角（度）"));
            SliderRow(_rotateGroup, "X", -180f, 180f, out _rotSlX, out _rotFdX, v => WithRotate(p => p.AngleX = v));
            SliderRow(_rotateGroup, "Y", -180f, 180f, out _rotSlY, out _rotFdY, v => WithRotate(p => p.AngleY = v));
            SliderRow(_rotateGroup, "Z", -180f, 180f, out _rotSlZ, out _rotFdZ, v => WithRotate(p => p.AngleZ = v));
            _root.Add(_rotateGroup);
        }

        private void BuildBendGroup()
        {
            _bendGroup = new VisualElement();
            _bendGroup.Add(Header("曲げ"));
            SliderRow(_bendGroup, "合計角",     -360f, 360f, out _bendAngleSl, out _bendAngleFd, v => WithBend(p => p.TotalAngleDeg = v));
            SliderRow(_bendGroup, "たわみ方向", -180f, 180f, out _bendPlaneSl, out _bendPlaneFd, v => WithBend(p => p.BendPlaneAngleDeg = v));
            _bendPivotTg = new Toggle("起点を作業軸の原点にする");
            _bendPivotTg.style.fontSize = 10;
            _bendPivotTg.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithBend(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _bendGroup.Add(_bendPivotTg);
            _root.Add(_bendGroup);
        }

        private void BuildTwistGroup()
        {
            _twistGroup = new VisualElement();
            _twistGroup.Add(Header("ねじり"));
            SliderRow(_twistGroup, "合計角", -720f, 720f, out _twistAngleSl, out _twistAngleFd, v => WithTwist(p => p.TotalAngleDeg = v));
            _twistPivotTg = new Toggle("起点を作業軸の原点にする");
            _twistPivotTg.style.fontSize = 10;
            _twistPivotTg.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithTwist(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _twistGroup.Add(_twistPivotTg);
            _root.Add(_twistGroup);
        }

        private void BuildWaveGroup()
        {
            _waveGroup = new VisualElement();

            _waveGroup.Add(Header("波（X 方向）"));
            SliderRow(_waveGroup, "振幅",   0f,    2f,   out _waveAmpXSl, out _waveAmpXFd, v => WithWave(p => p.AmplitudeX = v));
            SliderRow(_waveGroup, "周期数", 0f,    8f,   out _waveCycXSl, out _waveCycXFd, v => WithWave(p => p.CyclesX    = v));
            SliderRow(_waveGroup, "位相",  -360f,  360f, out _wavePhXSl,  out _wavePhXFd,  v => WithWave(p => p.PhaseXDeg  = v));

            _waveGroup.Add(Header("波（Z 方向）"));
            SliderRow(_waveGroup, "振幅",   0f,    2f,   out _waveAmpZSl, out _waveAmpZFd, v => WithWave(p => p.AmplitudeZ = v));
            SliderRow(_waveGroup, "周期数", 0f,    8f,   out _waveCycZSl, out _waveCycZFd, v => WithWave(p => p.CyclesZ    = v));
            SliderRow(_waveGroup, "位相",  -360f,  360f, out _wavePhZSl,  out _wavePhZFd,  v => WithWave(p => p.PhaseZDeg  = v));

            _wavePivotTg = new Toggle("起点を作業軸の原点にする");
            _wavePivotTg.style.fontSize = 10;
            _wavePivotTg.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithWave(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _waveGroup.Add(_wavePivotTg);

            var hint = new Label("周期数は「複製元全体の軸方向の長さで何周ぶん波打つか」です。");
            hint.style.fontSize   = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _waveGroup.Add(hint);

            _root.Add(_waveGroup);
        }

        private void WithRotate(Action<RotateDeformerParams> set)
        { if (Deformer?.Params is RotateDeformerParams p) set(p); }

        private void WithBend(Action<BendDeformerParams> set)
        { if (Deformer?.Params is BendDeformerParams p) set(p); }

        private void WithTwist(Action<TwistDeformerParams> set)
        { if (Deformer?.Params is TwistDeformerParams p) set(p); }

        private void WithWave(Action<WaveDeformerParams> set)
        { if (Deformer?.Params is WaveDeformerParams p) set(p); }

        /// <summary>デフォーマを作り直したあと、ウィジェットへ現在値を書き戻す。</summary>
        private void RefreshParamWidgets()
        {
            _suppressCallback = true;
            try
            {
                if (Deformer?.Params is RotateDeformerParams r)
                {
                    SetPair(_rotSlX, _rotFdX, r.AngleX);
                    SetPair(_rotSlY, _rotFdY, r.AngleY);
                    SetPair(_rotSlZ, _rotFdZ, r.AngleZ);
                }
                else if (Deformer?.Params is BendDeformerParams b)
                {
                    SetPair(_bendAngleSl, _bendAngleFd, b.TotalAngleDeg);
                    SetPair(_bendPlaneSl, _bendPlaneFd, b.BendPlaneAngleDeg);
                    _bendPivotTg?.SetValueWithoutNotify(b.PivotAtAxisOrigin);
                }
                else if (Deformer?.Params is TwistDeformerParams t)
                {
                    SetPair(_twistAngleSl, _twistAngleFd, t.TotalAngleDeg);
                    _twistPivotTg?.SetValueWithoutNotify(t.PivotAtAxisOrigin);
                }
                else if (Deformer?.Params is WaveDeformerParams w)
                {
                    SetPair(_waveAmpXSl, _waveAmpXFd, w.AmplitudeX);
                    SetPair(_waveCycXSl, _waveCycXFd, w.CyclesX);
                    SetPair(_wavePhXSl,  _wavePhXFd,  w.PhaseXDeg);
                    SetPair(_waveAmpZSl, _waveAmpZFd, w.AmplitudeZ);
                    SetPair(_waveCycZSl, _waveCycZFd, w.CyclesZ);
                    SetPair(_wavePhZSl,  _wavePhZFd,  w.PhaseZDeg);
                    _wavePivotTg?.SetValueWithoutNotify(w.PivotAtAxisOrigin);
                }
            }
            finally { _suppressCallback = false; }
        }

        // ================================================================
        // 複製
        // ================================================================

        private void BuildCopySection()
        {
            _root.Add(PlayerIoUiKit.Divider());
            _root.Add(PlayerIoUiKit.SectionLabel("複製"));

            _countField = new IntegerField("組の数") { value = Params.Count };
            _countField.style.color = new StyleColor(Color.white);
            _countField.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                Params.Count = Mathf.Max(1, e.newValue);
                _suppressCallback = true;
                try { _countField.SetValueWithoutNotify(Params.Count); }
                finally { _suppressCallback = false; }
            });
            _root.Add(_countField);

            SliderRow(_root, "位相ステップ", -360f, 360f, out _phaseStepSl, out _phaseStepFd,
                v => Params.PhaseStepDeg = v);
            SetPair(_phaseStepSl, _phaseStepFd, Params.PhaseStepDeg);

            var phaseHint = new Label("組 i の位相 = 位相ステップ × i。位相を持たない歪みでは無視されます。");
            phaseHint.style.fontSize   = 10;
            phaseHint.style.whiteSpace = WhiteSpace.Normal;
            phaseHint.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(phaseHint);

            _root.Add(Header("位置ステップ（作業軸ローカル）"));
            var offRow = new VisualElement();
            offRow.style.flexDirection = FlexDirection.Row;
            offRow.style.marginBottom  = 2;
            _offsetX = OffsetField("X", v => Params.OffsetStep = new Vector3(v, Params.OffsetStep.y, Params.OffsetStep.z));
            _offsetY = OffsetField("Y", v => Params.OffsetStep = new Vector3(Params.OffsetStep.x, v, Params.OffsetStep.z));
            _offsetZ = OffsetField("Z", v => Params.OffsetStep = new Vector3(Params.OffsetStep.x, Params.OffsetStep.y, v));
            offRow.Add(_offsetX); offRow.Add(_offsetY); offRow.Add(_offsetZ);
            _root.Add(offRow);

            _fixOriginToggle = new Toggle("原点を固定") { value = Params.FixOrigin };
            _fixOriginToggle.style.fontSize  = 10;
            _fixOriginToggle.style.marginTop = 4;
            _fixOriginToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                Params.FixOrigin = e.newValue;
            });
            _root.Add(_fixOriginToggle);

            var fixHint = new Label(
                "上から下へ数えます。上端（作業軸ローカル +Y の最大側）は動かさず、\n" +
                "それ以外の位置は上端からの相対で決まります。位置ステップはこの後に足されます。");
            fixHint.style.fontSize   = 10;
            fixHint.style.whiteSpace = WhiteSpace.Normal;
            fixHint.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(fixHint);
        }

        private FloatField OffsetField(string label, Action<float> set)
        {
            var f = new FloatField(label) { value = 0f };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.style.color       = new StyleColor(Color.white);
            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                set(e.newValue);
            });
            return f;
        }

        // ================================================================
        // 出力
        // ================================================================

        private void BuildOutputSection()
        {
            _root.Add(PlayerIoUiKit.Divider());
            _root.Add(PlayerIoUiKit.SectionLabel("出力"));

            _targetDropdown = new DropdownField("出力先", new List<string> { RootChoice }, 0);
            _targetDropdown.style.color = new StyleColor(Color.white);
            _targetDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_suppressCallback) return;
                int i = _targetDropdown.index - 1;   // 先頭は「(ルート)」
                Params.TargetMasterIndex =
                    (i >= 0 && i < _candidates.Count) ? _candidates[i].MasterIndex : -1;
            });
            _root.Add(_targetDropdown);

            var modes = new List<string> { "子として生成", "中に生成" };
            _modeDropdown = new DropdownField("モード", modes, 0);
            _modeDropdown.style.color = new StyleColor(Color.white);
            _modeDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_suppressCallback) return;
                Params.OutputMode = _modeDropdown.index == 1
                    ? ObjectArrayOutputMode.Inside
                    : ObjectArrayOutputMode.AsChild;
            });
            _root.Add(_modeDropdown);

            var modeHint = new Label(
                "子として生成: 出力先の子として別々のオブジェクトを作ります。\n" +
                "中に生成: 出力先の頂点・面へ統合します。ルートのときは新規オブジェクトを1つ作ります。");
            modeHint.style.fontSize   = 10;
            modeHint.style.whiteSpace = WhiteSpace.Normal;
            modeHint.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(modeHint);

            // ── 組ごとの空の親 ──
            _groupToggle = new Toggle("組ごとに空の親を作る") { value = Params.GroupEachCopy };
            _groupToggle.style.fontSize  = 10;
            _groupToggle.style.marginTop = 4;
            _groupToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                Params.GroupEachCopy = e.newValue;
                RefreshGroupNameEnabled();
            });
            _root.Add(_groupToggle);

            _groupNameField = new TextField("親の名前") { value = Params.GroupNameBase };
            _groupNameField.style.color = new StyleColor(Color.white);
            _groupNameField.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                Params.GroupNameBase = e.newValue;
            });
            _root.Add(_groupNameField);

            var groupHint = new Label(
                "組ごとに頂点を持たない親オブジェクトを作り、その組の生成物をすべて子にします。\n" +
                "複製元が1本のときも包みます。「中に生成」では無視されます。");
            groupHint.style.fontSize   = 10;
            groupHint.style.whiteSpace = WhiteSpace.Normal;
            groupHint.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(groupHint);

            RefreshGroupNameEnabled();
        }

        /// <summary>親の名前欄は、空の親を作るときだけ触れるようにする。</summary>
        private void RefreshGroupNameEnabled()
        {
            _groupNameField?.SetEnabled(Params.GroupEachCopy);
        }

        /// <summary>出力先の候補を作り直す。選んでいた MasterIndex は保てるだけ保つ。</summary>
        private void RefreshTargetDropdown()
        {
            if (_targetDropdown == null) return;

            var choices = new List<string> { RootChoice };
            foreach (var e in _candidates) choices.Add(e.Label);

            int keep = 0;
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].MasterIndex == Params.TargetMasterIndex) { keep = i + 1; break; }

            if (keep == 0) Params.TargetMasterIndex = -1;

            _suppressCallback = true;
            try
            {
                _targetDropdown.choices = choices;
                _targetDropdown.index   = keep;
            }
            finally { _suppressCallback = false; }
        }

        // ================================================================
        // Refresh / 状態表示
        // ================================================================

        public void Refresh()
        {
            if (_root == null) return;

            RefreshSourceList();

            _suppressCallback = true;
            try
            {
                _countField?.SetValueWithoutNotify(Params.Count);
                SetPair(_phaseStepSl, _phaseStepFd, Params.PhaseStepDeg);
                _offsetX?.SetValueWithoutNotify(Params.OffsetStep.x);
                _offsetY?.SetValueWithoutNotify(Params.OffsetStep.y);
                _offsetZ?.SetValueWithoutNotify(Params.OffsetStep.z);
                if (_modeDropdown != null)
                    _modeDropdown.index = Params.OutputMode == ObjectArrayOutputMode.Inside ? 1 : 0;
                _groupToggle?.SetValueWithoutNotify(Params.GroupEachCopy);
                _groupNameField?.SetValueWithoutNotify(Params.GroupNameBase);
                _fixOriginToggle?.SetValueWithoutNotify(Params.FixOrigin);
            }
            finally { _suppressCallback = false; }

            RefreshGroupNameEnabled();

            RefreshParamWidgets();
            UpdateGroupVisibility();
        }

        public void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? string.Empty;
        }

        // ================================================================
        // ウィジェットヘルパー
        // ================================================================

        /// <summary>スライダと数値欄を1行に並べ、両方から同じ値を書き込む。</summary>
        private void SliderRow(
            VisualElement parent, string label, float min, float max,
            out Slider slider, out FloatField field, Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;

            var s = new Slider(label, min, max) { value = 0f };
            s.style.flexGrow = 1;

            var f = new FloatField { value = 0f };
            f.style.width      = 60;
            f.style.marginLeft = 4;
            f.style.color      = new StyleColor(Color.black);

            s.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                _suppressCallback = true;
                try { f.SetValueWithoutNotify(e.newValue); }
                finally { _suppressCallback = false; }
                onChange(e.newValue);
            });

            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                // スライダ範囲外の値も数値入力では許す。スライダは端で止める。
                _suppressCallback = true;
                try { s.SetValueWithoutNotify(Mathf.Clamp(e.newValue, min, max)); }
                finally { _suppressCallback = false; }
                onChange(e.newValue);
            });

            row.Add(s); row.Add(f);
            parent.Add(row);

            slider = s;
            field  = f;
        }

        private static void SetPair(Slider s, FloatField f, float v)
        {
            s?.SetValueWithoutNotify(v);
            f?.SetValueWithoutNotify(v);
        }

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(Color.white);
            l.style.marginTop    = 6;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
