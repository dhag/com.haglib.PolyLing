// PlayerSkinWeightNumericSubPanel.cs
// スキンウェイト数値設定パネル（Player ビルド用）。
// 最大 4 ボーンをドロップダウンで選び、各々のウェイトをスライダ／数値で指定して
// 選択頂点へ一括適用する。3D 側は頂点のみ選択（InteractionMode.SkinWeightNumeric）。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    /// <summary>
    /// スキンウェイト数値設定パネル。
    ///
    /// ISkinWeightPaintPanel を実装するのは、ウェイトのヒートマップ可視化を流用するため。
    /// 可視化側が参照するのは SkinWeightPaintTool.ActivePanel 経由の
    /// CurrentTargetBone (SkinWeightPaintTool.VisualizationTargetBone) と
    /// CurrentTargetMesh (MeshSceneRenderer.CollectWeightVisTargets) の 2 つだけで、
    /// ブラシ用のプロパティはこの経路では読まれない。
    /// </summary>
    public class PlayerSkinWeightNumericSubPanel
        : ISkinWeightPaintPanel, IMultiBoneWeightVisualization
    {
        public const int SlotCount = 4;

        /// <summary>合計が 1 とみなす許容誤差。</summary>
        private const float SumTolerance = 0.001f;

        /// <summary>重みスライダのドラッグ時の刻み幅。</summary>
        private const float WeightStep = 0.05f;

        // ================================================================
        // ISkinWeightPaintPanel / IMultiBoneWeightVisualization 実装
        // （ウェイト可視化のためだけに実装する）
        // ================================================================

        /// <summary>
        /// 可視化に含めるスロット。「色」ボタンで個別にトグルする。
        /// 複数 ON にすると、それらのウェイト合計が 1 系統のヒートマップで表示される
        /// （Blender の Multi-Paint 相当）。既定はスロット 1 のみ。
        /// </summary>
        private readonly bool[] _visSlots = new bool[SlotCount] { true, false, false, false };

        /// <summary>可視化対象ボーンの再利用バッファ。毎フレーム参照されるため確保し直さない。</summary>
        private readonly List<int> _visBones = new List<int>(SlotCount);

        /// <summary>
        /// 合算表示するボーン群。ON かつボーン指定済みのスロットのみ。
        /// 1 件も無ければ null を返し、単一ボーン表示（CurrentTargetBone）へ委ねる。
        /// </summary>
        public IReadOnlyList<int> VisualizationBones
        {
            get
            {
                _visBones.Clear();
                for (int i = 0; i < SlotCount; i++)
                    if (_visSlots[i] && _slotBoneMaster[i] >= 0)
                        _visBones.Add(_slotBoneMaster[i]);
                return _visBones.Count > 0 ? _visBones : null;
            }
        }

        /// <summary>
        /// 単一ボーン表示用のフォールバック。ON の先頭スロットのボーンを返す。
        /// 通常は VisualizationBones が使われるためこちらは参照されない。
        /// </summary>
        public int CurrentTargetBone
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (_visSlots[i] && _slotBoneMaster[i] >= 0) return _slotBoneMaster[i];
                return -1;
            }
        }

        /// <summary>
        /// 可視化対象メッシュ。常に -1 を返す。
        ///
        /// -1 のとき MeshSceneRenderer.CollectWeightVisTargets は
        /// SelectedDrawableMeshIndices 全件を対象にする。数値設定の適用先
        /// （SkinWeightOperations.CollectTargetMeshContexts）も同じ全件なので、
        /// 色が付く範囲と書き換わる範囲が一致する。
        /// 単一メッシュの MasterIndex を返すと、複数選択時に 1 つしか色が付かない。
        /// </summary>
        public int CurrentTargetMesh => -1;

        // ── 以下はブラシ用。この経路では読まれないため既定値を返す ──
        public SkinWeightPaintMode CurrentPaintMode   => SkinWeightPaintMode.Replace;
        public float               CurrentBrushRadius => 0f;
        public float               CurrentStrength    => 0f;
        public FalloffType         CurrentFalloff      => FalloffType.Constant;
        public DistanceMode        CurrentDistanceMode => DistanceMode.Euclidean;
        public float               CurrentWeightValue => 0f;

        public void NotifyWeightChanged() { }

        // ================================================================
        // 外部依存
        // ================================================================

        /// <summary>現在のモデルを返す。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>可視化対象（スロット／ボーン）が変わったときに呼ばれる。</summary>
        public Action OnVisualizationTargetChanged;

        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        private void SendCmd(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ================================================================
        // 内部状態
        // ================================================================

        private VisualElement _root;

        // ボーン候補（先頭「（未選択）」は choices 側にのみ存在し、下記リストには含めない）
        private readonly List<string> _boneNames         = new List<string>();
        private readonly List<int>    _boneMasterIndices = new List<int>();

        private readonly DropdownField[] _boneDropdowns = new DropdownField[SlotCount];
        private readonly Slider[]        _sliders       = new Slider[SlotCount];
        private readonly FloatField[]    _fields        = new FloatField[SlotCount];
        private readonly Button[]        _visButtons    = new Button[SlotCount];

        // スロットの現在値（UI と同期）
        private readonly int[]   _slotBoneMaster = new int[SlotCount];
        private readonly float[] _slotWeight     = new float[SlotCount];

        private Label _totalLabel;
        private Label _checkLabel;
        private Label _targetLabel;
        private Label _selCountLabel;
        private Label _statusLabel;

        public PlayerSkinWeightNumericSubPanel()
        {
            for (int i = 0; i < SlotCount; i++) { _slotBoneMaster[i] = -1; _slotWeight[i] = 0f; }
        }

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

            AddSectionLabel("スキンウェイト数値設定");
            _root.Add(new HelpBox(
                "ビューポートで頂点を選び、ボーンとウェイトを指定して適用します。\n" +
                "「色」ボタンは表示用。ON にしたスロットのウェイト合計が色で表示されます（複数選択可）。",
                HelpBoxMessageType.Info));

            // 適用先と可視化対象は同一（どちらも SelectedDrawableMeshIndices）。
            // ここに実際の件数と内訳を出しておかないと、「色が 1 個しか付かない」のが
            // 選択が 1 件だからなのか不具合なのかを画面から判別できない。
            _targetLabel = InfoLabel();
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_targetLabel);

            _selCountLabel = InfoLabel();
            _root.Add(_selCountLabel);

            AddSep();

            for (int i = 0; i < SlotCount; i++)
                BuildSlotRow(i);

            AddSep();

            _totalLabel = InfoLabel();
            _root.Add(_totalLabel);

            // ── 操作ボタン
            var rowOps = new VisualElement();
            rowOps.style.flexDirection = FlexDirection.Row;
            rowOps.style.marginTop     = 4;
            rowOps.style.marginBottom  = 3;

            var gatherBtn = new Button(OnGather) { text = "現在値を取り込む" };
            gatherBtn.style.flexGrow    = 1;
            gatherBtn.style.marginRight = 2;
            gatherBtn.style.height      = 24;
            rowOps.Add(gatherBtn);

            var normBtn = new Button(OnNormalize) { text = "正規化" };
            normBtn.style.flexGrow = 1;
            normBtn.style.height   = 24;
            rowOps.Add(normBtn);

            _root.Add(rowOps);

            var applyBtn = new Button(OnApply) { text = "適用" };
            applyBtn.style.height    = 30;
            applyBtn.style.marginTop = 4;
            _root.Add(applyBtn);

            AddSep();
            AddSectionLabel("ウェイト合計の検査");
            _root.Add(new HelpBox(
                "ウェイト合計が 1 でない頂点は原点方向へ寄り、メッシュが崩れて見えます。",
                HelpBoxMessageType.Info));

            var rowCheck = new VisualElement();
            rowCheck.style.flexDirection = FlexDirection.Row;
            rowCheck.style.marginTop     = 3;

            var checkBtn = new Button(OnCheckSums) { text = "合計を検査" };
            checkBtn.style.flexGrow    = 1;
            checkBtn.style.marginRight = 2;
            checkBtn.style.height      = 24;
            rowCheck.Add(checkBtn);

            var normAllBtn = new Button(OnNormalizeAll) { text = "全頂点を正規化" };
            normAllBtn.style.flexGrow = 1;
            normAllBtn.style.height   = 24;
            rowCheck.Add(normAllBtn);

            _root.Add(rowCheck);

            _checkLabel = InfoLabel();
            _checkLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_checkLabel);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
            // ApplyDarkTheme は全 Button の背景色を既定へ戻すため、その後で強調色を入れる。
            UpdateVisButtons();
            UpdateTotalLabel();
        }

        /// <summary>1 スロット分の [ボーン Dropdown] + [Slider] + [FloatField] を作る。</summary>
        private void BuildSlotRow(int slot)
        {
            AddSectionLabel($"ボーン {slot + 1}");

            var dd = new DropdownField(new List<string> { "（未選択）" }, 0);
            dd.style.color        = new StyleColor(Color.white);
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(e =>
            {
                int sel = dd.index;
                _slotBoneMaster[slot] = (sel <= 0) ? -1 : _boneMasterIndices[sel - 1];
                UpdateTotalLabel();
                // 可視化に含めているスロットのボーンが変わったら色を再計算させる。
                if (_visSlots[slot]) OnVisualizationTargetChanged?.Invoke();
                OnRepaint?.Invoke();
            });
            _boneDropdowns[slot] = dd;
            _root.Add(dd);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            // このスロットを可視化に含めるかをトグルする。
            // 複数 ON にすると合計ウェイトが表示される。
            var visBtn = new Button(() =>
            {
                _visSlots[slot] = !_visSlots[slot];
                UpdateVisButtons();
                OnVisualizationTargetChanged?.Invoke();
                OnRepaint?.Invoke();
            }) { text = "色" };
            visBtn.style.width       = 28;
            visBtn.style.marginRight = 2;
            visBtn.style.fontSize    = 9;
            _visButtons[slot] = visBtn;
            row.Add(visBtn);

            var sl = new Slider(0f, 1f) { value = _slotWeight[slot] };
            sl.style.color    = new StyleColor(Color.white);
            sl.style.flexGrow = 1;

            var nf = new FloatField { value = _slotWeight[slot] };
            nf.style.color = new StyleColor(Color.black);
            nf.style.width = 72;

            sl.RegisterValueChangedCallback(e =>
            {
                // スライダのドラッグは WeightStep 刻みへ丸める。
                // 数値フィールドの直接入力・OnNormalize・OnGather は丸めない
                // （1/3 のような値が 0.05 刻みになると合計が 1 から外れ、
                //   OnApply の合計チェックで弾かれるため）。
                float v = Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(e.newValue) / WeightStep) * WeightStep);
                if (!Mathf.Approximately(v, e.newValue)) sl.SetValueWithoutNotify(v);
                _slotWeight[slot] = v;
                nf.SetValueWithoutNotify((float)Math.Round(v, 4));
                UpdateTotalLabel();
            });
            nf.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp01(e.newValue);
                _slotWeight[slot] = v;
                sl.SetValueWithoutNotify(v);
                if (!Mathf.Approximately(v, e.newValue)) nf.SetValueWithoutNotify(v);
                UpdateTotalLabel();
            });

            _sliders[slot] = sl;
            _fields[slot]  = nf;
            row.Add(sl);
            row.Add(nf);
            _root.Add(row);
        }

        // ================================================================
        // 操作
        // ================================================================

        /// <summary>4 スロットの合計が 1 になるよう再計算する。合計 0 のときは何もしない。</summary>
        private void OnNormalize()
        {
            float total = 0f;
            for (int i = 0; i < SlotCount; i++)
                if (_slotBoneMaster[i] >= 0) total += _slotWeight[i];

            if (total <= 0.0001f) { SetStatus("合計が 0 のため正規化できません。"); return; }

            float inv = 1f / total;
            for (int i = 0; i < SlotCount; i++)
            {
                float v = (_slotBoneMaster[i] >= 0) ? Mathf.Clamp01(_slotWeight[i] * inv) : 0f;
                SetSlotWeight(i, v);
            }
            UpdateTotalLabel();
            SetStatus("正規化しました。");
        }

        /// <summary>
        /// 選択頂点の現在のボーンウェイトを取り込む。
        /// 複数頂点のときは、全頂点で同じボーン・同じウェイトの組だけを反映する。
        /// </summary>
        private void OnGather()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルがありません。"); return; }

            string err = null;
            var common = SkinWeightOperations.GatherCommonBoneWeights(model, 1e-4f, m => err = m);
            if (common == null) { SetStatus(err ?? "取り込めませんでした。"); return; }

            int filled = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                SetSlotBone(i, common[i].bone);
                SetSlotWeight(i, common[i].weight);
                if (common[i].bone >= 0) filled++;
            }
            UpdateTotalLabel();
            SetStatus(filled > 0
                ? $"取り込み: {filled} スロット"
                : "全頂点で一致するウェイトがありません。");
            OnVisualizationTargetChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>入力値をそのまま選択頂点へ書き込む。</summary>
        private void OnApply()
        {
            bool any = false;
            for (int i = 0; i < SlotCount; i++) if (_slotBoneMaster[i] >= 0) { any = true; break; }
            if (!any) { SetStatus("ボーンが 1 つも指定されていません。"); return; }

            // GPU スキニング (UnifiedCompute.compute:974-977) はボーン行列の加重和を
            // そのまま使い正規化しない。合計が 1 でないまま書き込むと頂点が
            // 原点方向へ寄りメッシュが崩れるため、ここで止める。
            float total = SlotTotal();
            if (Mathf.Abs(total - 1f) > SumTolerance)
            {
                SetStatus($"合計が 1 ではありません（現在 {total:F4}）。" +
                          "「正規化」を押すか値を修正してください。");
                return;
            }

            var masters = new int[SlotCount];
            var weights = new float[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                masters[i] = _slotBoneMaster[i];
                weights[i] = _slotWeight[i];
            }

            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            SendCmd(new SetSkinWeightNumericCommand(modelIdx, masters, weights));
            SetStatus("適用しました。");
            OnRepaint?.Invoke();
        }

        /// <summary>対象メッシュ全頂点のウェイト合計を検査して結果を表示する。</summary>
        private void OnCheckSums()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetCheck("モデルがありません。"); return; }

            var rep = SkinWeightOperations.CheckWeightSums(model, SumTolerance);
            if (rep.Checked == 0 && rep.NoWeight == 0)
            { SetCheck("対象がありません。"); return; }

            if (rep.Broken == 0)
            {
                SetCheck($"問題なし。検査 {rep.Checked} 頂点" +
                         (rep.NoWeight > 0 ? $"（ウェイト無し {rep.NoWeight}）" : ""));
                return;
            }

            SetCheck($"合計が 1 でない頂点: {rep.Broken} / {rep.Checked}　" +
                     $"合計の範囲 {rep.MinSum:F4}〜{rep.MaxSum:F4}　" +
                     $"対象: {string.Join(" / ", rep.BrokenMeshNames)}");
        }

        /// <summary>対象メッシュ全件の全頂点を正規化する。</summary>
        private void OnNormalizeAll()
        {
            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            SendCmd(new NormalizeAllSkinWeightsCommand(modelIdx));
            SetCheck("全頂点を正規化しました。もう一度「合計を検査」で確認できます。");
            OnRepaint?.Invoke();
        }

        // ================================================================
        // Refresh
        // ================================================================

        /// <summary>モデルのボーン一覧をドロップダウンへ反映する。</summary>
        public void RefreshBoneList(ModelContext model)
        {
            _boneNames.Clear();
            _boneMasterIndices.Clear();

            if (model != null)
            {
                var bones = model.Bones;
                if (bones != null)
                {
                    foreach (var entry in bones)
                    {
                        string bname = string.IsNullOrEmpty(entry.Name) ? $"Bone_{entry.MasterIndex}" : entry.Name;
                        _boneNames.Add($"{bname} [{entry.MasterIndex}]");
                        _boneMasterIndices.Add(entry.MasterIndex);
                    }
                }
            }

            var choices = new List<string> { "（未選択）" };
            choices.AddRange(_boneNames);

            for (int i = 0; i < SlotCount; i++)
            {
                var dd = _boneDropdowns[i];
                if (dd == null) continue;
                dd.choices = choices;

                int selIdx = 0;
                if (_slotBoneMaster[i] >= 0)
                {
                    int found = _boneMasterIndices.IndexOf(_slotBoneMaster[i]);
                    selIdx = found >= 0 ? found + 1 : 0;
                }
                dd.SetValueWithoutNotify(choices[selIdx]);
                if (selIdx == 0) _slotBoneMaster[i] = -1;
            }
        }

        public void Refresh()
        {
            var model = GetModel?.Invoke();
            RefreshBoneList(model);

            // 適用先と同じ「選択中の描画オブジェクト全件」で数える。
            var targets  = model != null
                ? SkinWeightOperations.CollectTargetMeshContexts(model)
                : new List<MeshContext>();

            int selCount = 0;
            var parts    = new List<string>();
            foreach (var mc in targets)
            {
                int n = mc?.SelectedVertices?.Count ?? 0;
                selCount += n;
                parts.Add($"{(string.IsNullOrEmpty(mc?.Name) ? "?" : mc.Name)}({n})");
            }

            if (_targetLabel != null)
                _targetLabel.text = targets.Count == 0
                    ? "対象: なし（オブジェクトリストでオブジェクトを選択してください）"
                    : $"対象: {targets.Count} 件 — {string.Join(" / ", parts)}";

            if (_selCountLabel != null)
                _selCountLabel.text = $"選択頂点: {selCount}（全対象の合計）";

            UpdateTotalLabel();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>
        /// 「色」ボタンの色を塗り直す。
        /// PlayerLayoutRoot.ApplyDarkTheme は全 Button を既定色へ戻すため、
        /// それが走った後に外部から呼ぶ。
        /// </summary>
        public void RepaintSegmentButtons() => UpdateVisButtons();

        /// <summary>可視化に含めているスロットの「色」ボタンを強調する（複数可）。</summary>
        private void UpdateVisButtons()
        {
            var active   = new StyleColor(new Color(0.3f, 0.5f, 1.0f));
            var inactive = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            for (int i = 0; i < SlotCount; i++)
                if (_visButtons[i] != null)
                    _visButtons[i].style.backgroundColor = _visSlots[i] ? active : inactive;
        }

        private void SetSlotWeight(int slot, float v)
        {
            v = Mathf.Clamp01(v);
            _slotWeight[slot] = v;
            _sliders[slot]?.SetValueWithoutNotify(v);
            _fields[slot]?.SetValueWithoutNotify((float)Math.Round(v, 4));
        }

        private void SetSlotBone(int slot, int boneMaster)
        {
            _slotBoneMaster[slot] = boneMaster;

            var dd = _boneDropdowns[slot];
            if (dd == null || dd.choices == null || dd.choices.Count == 0) return;

            int selIdx = 0;
            if (boneMaster >= 0)
            {
                int found = _boneMasterIndices.IndexOf(boneMaster);
                selIdx = found >= 0 ? found + 1 : 0;
            }
            if (selIdx >= dd.choices.Count) selIdx = 0;
            dd.SetValueWithoutNotify(dd.choices[selIdx]);
            if (selIdx == 0) _slotBoneMaster[slot] = -1;
        }

        /// <summary>ボーン指定済みスロットのウェイト合計。</summary>
        private float SlotTotal()
        {
            float total = 0f;
            for (int i = 0; i < SlotCount; i++)
                if (_slotBoneMaster[i] >= 0) total += _slotWeight[i];
            return total;
        }

        private void UpdateTotalLabel()
        {
            if (_totalLabel == null) return;
            float total = SlotTotal();
            bool ok = Mathf.Abs(total - 1f) <= SumTolerance;
            _totalLabel.text  = ok ? $"合計: {total:F4}" : $"合計: {total:F4} ← 1 ではありません";
            // 1 でないまま適用するとメッシュが崩れるため警告色にする。
            _totalLabel.style.color = new StyleColor(ok ? Color.white : new Color(1f, 0.65f, 0.2f));
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        private void SetCheck(string msg)
        {
            if (_checkLabel != null) _checkLabel.text = msg;
        }

        private void AddSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            _root.Add(l);
        }

        private void AddSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            _root.Add(v);
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color        = new StyleColor(Color.white);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
