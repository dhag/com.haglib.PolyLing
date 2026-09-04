// PlayerPipeAlignSubPanel.cs
// PipeAlignTool（パイプの整列）の Player 版サブパネル（UIToolkit）。
// 自動ペア / 手動ペア / スムージング の 3 モードを 1 つのパネルで切り替える。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerPipeAlignSubPanel
    {
        public Func<PipeAlignToolHandler> GetH;
        public Func<ProjectContext>       GetView;
        public Action<PanelCommand>       SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 実行時点の選択オブジェクトをコマンドの対象として載せる。
        /// 受け口は照合するだけで選択を書き換えない。
        /// </summary>
        private int[] SelectedMasterIndices()
        {
            var sel = GetView?.Invoke()?.CurrentModel?.SelectedDrawableMeshIndices;
            return sel != null ? sel.ToArray() : System.Array.Empty<int>();
        }

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _targetLabel;

        private RadioButtonGroup _modeGroup;

        // 対称化（自動ペア / 手動ペア 共通）
        private VisualElement    _symBox;
        private IntegerField     _ringField;
        private Toggle           _capStartToggle;
        private Toggle           _capEndToggle;
        private RadioButtonGroup _directionGroup;

        // 手動ペア専用
        private VisualElement _pairBox;
        private TextField     _pairField;

        // スムージング専用
        private VisualElement    _smoothBox;
        private TextField        _weightField;
        private TextField        _smoothTargetField;
        private RadioButtonGroup _edgeGroup;

        private Button _executeBtn;
        private Label  _resultLabel;

        private static readonly List<string> ModeChoices =
            new List<string> { "自動ペア", "手動ペア", "スムージング" };

        private static readonly List<string> DirectionChoices =
            new List<string> { "+X 側 → -X 側", "-X 側 → +X 側" };

        private static readonly List<string> EdgeChoices =
            new List<string> { "端はスムージングしない", "端は片側だけでスムージング" };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Pipe Align / パイプの整列"));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            // ── モード ─────────────────────────────────────────────────
            _root.Add(SmallHeader("モード:"));
            _modeGroup = new RadioButtonGroup(null, ModeChoices) { value = 0 };
            _modeGroup.style.marginBottom = 4;
            _modeGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.Mode = ToMode(e.newValue);
                ApplyModeVisibility(ToMode(e.newValue));
                RefreshExecuteEnabled();
            });
            _root.Add(_modeGroup);

            BuildSymBox();
            BuildPairBox();
            BuildSmoothBox();

            // ── 実行 ───────────────────────────────────────────────────
            _executeBtn = new Button(() =>
            {
                var h = GetH();
                if (h == null) return;

                // 設定値はコマンドが正典。パネルの現在値を載せて送る。
                SendCommand?.Invoke(new PipeAlignCommand(
                    ModelIndex, SelectedMasterIndices(), h.Mode,
                    direction:       h.Direction,
                    edgeMode:        h.EdgeMode,
                    ringVertexCount: h.RingVertexCount,
                    capStart:        h.CapStart,
                    capEnd:          h.CapEnd,
                    pairText:        h.PairText,
                    weightText:      h.WeightText,
                    targetText:      h.TargetText));
                Refresh();
            }) { text = "開始" };
            _executeBtn.style.height    = 30;
            _executeBtn.style.marginTop = 6;
            _root.Add(_executeBtn);

            _resultLabel = InfoLabel();
            _resultLabel.style.marginTop = 4;
            _root.Add(_resultLabel);

            ApplyModeVisibility(PipeAlignMode.Auto);
            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ── 対称化の共通設定 ──────────────────────────────────────────

        private void BuildSymBox()
        {
            _symBox = new VisualElement();
            _root.Add(_symBox);

            _symBox.Add(new HelpBox(
                "パイプはパーツIDで分けます。周方向は 1 と M、2 と M-1 … の順で対応します。\n"
                + "自動ペアはパーツIDの昇順の端から順に対にし、奇数本なら中央を自分自身で対称化します。",
                HelpBoxMessageType.Info));

            _ringField = new IntegerField("1段の頂点数 M") { value = 6 };
            _ringField.style.marginBottom = 3;
            _ringField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                int v = e.newValue < 3 ? 3 : e.newValue;
                h.RingVertexCount = v;
                _ringField.SetValueWithoutNotify(v);
                RefreshExecuteEnabled();
            });
            _symBox.Add(_ringField);

            _symBox.Add(SmallHeader("端の閉じ方（先端頂点があるか）:"));

            _capStartToggle = MakeToggle("開始側が閉じている", v =>
            {
                var h = GetH();
                if (h != null) h.CapStart = v;
            });
            _symBox.Add(_capStartToggle);

            _capEndToggle = MakeToggle("終了側が閉じている", v =>
            {
                var h = GetH();
                if (h != null) h.CapEnd = v;
            });
            _symBox.Add(_capEndToggle);

            _symBox.Add(SmallHeader("コピーの向き:"));
            _directionGroup = new RadioButtonGroup(null, DirectionChoices) { value = 0 };
            _directionGroup.style.marginBottom = 4;
            _directionGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.Direction = e.newValue == 1
                    ? PipeAlignDirection.MinusToPlus
                    : PipeAlignDirection.PlusToMinus;
            });
            _symBox.Add(_directionGroup);
        }

        // ── 手動ペア ──────────────────────────────────────────────────

        private void BuildPairBox()
        {
            _pairBox = new VisualElement();
            _root.Add(_pairBox);

            _pairBox.Add(SmallHeader("ペア一覧（1行 1エントリ / 「元ID,先ID」）:"));
            _pairBox.Add(new HelpBox(
                "ID を 1 つだけ書いた行は、そのパーツを自分自身で左右対称化します"
                + "（どちら側を元にするかは上のコピーの向きに従います）。\n"
                + "列挙していないパーツは触りません。'#' で始まる行は読み飛ばします。",
                HelpBoxMessageType.Info));

            _pairField = new TextField { multiline = true, isDelayed = true, value = "" };
            _pairField.style.minHeight    = 90;
            _pairField.style.marginBottom = 3;
            _pairField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.PairText = e.newValue;
            });
            _pairBox.Add(_pairField);
        }

        // ── スムージング ──────────────────────────────────────────────

        private void BuildSmoothBox()
        {
            _smoothBox = new VisualElement();
            _root.Add(_smoothBox);

            _smoothBox.Add(new HelpBox(
                "パーツIDの昇順に並べたパイプ列に沿って、同じ並び位置の頂点どうしを"
                + "重み付き平均で置き換えます。左右の反転はしません。",
                HelpBoxMessageType.Info));

            _weightField = new TextField("重み（個数は奇数）") { isDelayed = true, value = "1,2,4,2,1" };
            _weightField.style.marginBottom = 3;
            _weightField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.WeightText = e.newValue;
            });
            _smoothBox.Add(_weightField);

            _smoothTargetField = new TextField("対象パーツID（空欄で全部）") { isDelayed = true, value = "" };
            _smoothTargetField.style.marginBottom = 3;
            _smoothTargetField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.TargetText = e.newValue;
            });
            _smoothBox.Add(_smoothTargetField);

            _smoothBox.Add(SmallHeader("「5,6,7」や「5-7」の形式。窓の入力には対象外のパーツも使います。"));

            _smoothBox.Add(SmallHeader("端の扱い:"));
            _edgeGroup = new RadioButtonGroup(null, EdgeChoices) { value = 0 };
            _edgeGroup.style.marginBottom = 4;
            _edgeGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.EdgeMode = e.newValue == 1
                    ? PipeSmoothEdgeMode.Partial
                    : PipeSmoothEdgeMode.Skip;
            });
            _smoothBox.Add(_edgeGroup);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            int targets = h.TargetMeshCount;
            _targetLabel.text = targets > 0
                ? $"対象オブジェクト: {targets} 個"
                : "対象オブジェクトなし（オブジェクトを選択してください）";

            _modeGroup?.SetValueWithoutNotify(ToIndex(h.Mode));

            _ringField?.SetValueWithoutNotify(h.RingVertexCount);
            _capStartToggle?.SetValueWithoutNotify(h.CapStart);
            _capEndToggle?.SetValueWithoutNotify(h.CapEnd);
            _directionGroup?.SetValueWithoutNotify(
                h.Direction == PipeAlignDirection.MinusToPlus ? 1 : 0);

            _pairField?.SetValueWithoutNotify(h.PairText ?? "");
            _weightField?.SetValueWithoutNotify(h.WeightText ?? "");
            _smoothTargetField?.SetValueWithoutNotify(h.TargetText ?? "");
            _edgeGroup?.SetValueWithoutNotify(
                h.EdgeMode == PipeSmoothEdgeMode.Partial ? 1 : 0);

            if (_resultLabel != null) _resultLabel.text = h.LastResult ?? "";

            ApplyModeVisibility(h.Mode);
            RefreshExecuteEnabled();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void ApplyModeVisibility(PipeAlignMode mode)
        {
            Show(_symBox,    mode == PipeAlignMode.Auto || mode == PipeAlignMode.Manual);
            Show(_pairBox,   mode == PipeAlignMode.Manual);
            Show(_smoothBox, mode == PipeAlignMode.Smooth);
        }

        private static void Show(VisualElement e, bool visible)
        {
            if (e == null) return;
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshExecuteEnabled()
        {
            if (_executeBtn == null) return;

            var h = GetH();
            if (h == null) { _executeBtn.SetEnabled(false); return; }

            bool can = h.TargetMeshCount > 0;
            if (h.Mode != PipeAlignMode.Smooth) can = can && h.RingVertexCount >= 3;

            _executeBtn.SetEnabled(can);
        }

        private static PipeAlignMode ToMode(int index)
        {
            switch (index)
            {
                case 1:  return PipeAlignMode.Manual;
                case 2:  return PipeAlignMode.Smooth;
                default: return PipeAlignMode.Auto;
            }
        }

        private static int ToIndex(PipeAlignMode mode)
        {
            switch (mode)
            {
                case PipeAlignMode.Manual: return 1;
                case PipeAlignMode.Smooth: return 2;
                default:                   return 0;
            }
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }

        private static Toggle MakeToggle(string label, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = true };
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }
    }
}
