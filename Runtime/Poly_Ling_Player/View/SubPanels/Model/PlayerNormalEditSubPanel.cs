// PlayerNormalEditSubPanel.cs
// 法線編集サブパネル。実処理は NormalEditOps / NormalSmoothingOps。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 対象範囲のルール（NormalEditOps.CollectTargetCorners）
//   面選択がある     → その面のコーナーのみ
//   頂点選択のみある → その頂点が参照する全スロット
//   選択が無い       → メッシュ全体
// スムージング角での再計算だけはスロットを作り直すためメッシュ全体が対象。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerNormalEditSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        private static readonly List<string> WeightNames = new List<string>
        {
            "均等", "角度", "面積", "角度×面積"
        };

        private static readonly List<string> AxisNames = new List<string> { "X", "Y", "Z" };

        // UI 自動操作の ID は "normalEdit.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "メッシュが選択されていないときの警告（それ以外は非表示）")]
        private Label          _warningLabel;
        [UiControl("meshName", Safety = UiSafety.ReadOnly, Description = "対象メッシュ名")]
        private Label          _meshNameLabel;
        [UiControl("currentSelection", Safety = UiSafety.ReadOnly, Description = "選択中の頂点・辺・面の数と、操作の対象範囲")]
        private Label          _currentSelLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label          _statusLabel;
        [UiControl("weight", Description = "面法線を平均するときの重み付け")]
        private DropdownField  _weightDropdown;
        [UiControl("axis", Description = "軸（軸への整列・軸成分を 0 にするときの軸）")]
        private DropdownField  _axisDropdown;
        [UiControl("target.x", Description = "ターゲット指向の座標 X")]
        private FloatField     _targetX;
        [UiControl("target.y", Description = "ターゲット指向の座標 Y")]
        private FloatField     _targetY;
        [UiControl("target.z", Description = "ターゲット指向の座標 Z")]
        private FloatField     _targetZ;
        [UiControl("sphereizeUseSelectionCenter", Description = "球状化の中心に選択の重心を使う")]
        private Toggle         _useCenterToggle;
        [UiControl("pointToTargetSingleVector", Description = "ターゲット指向を 1 本のベクトルに揃える")]
        private Toggle         _alignVectorsToggle;
        [UiControl("mirrorThreshold", Description = "中央とみなす範囲（|X 座標| がこの値以下の頂点が対象）")]
        private FloatField     _mirrorThresholdField;

        [UiControl("angle", Description = "再計算のスムージング角（スライダー）")]
        private Slider         _angleSlider;
        [UiControl("angleValue", Description = "再計算のスムージング角（数値入力）")]
        private FloatField     _angleField;
        [UiControl("smoothStrength", Description = "平滑強度（スライダー）")]
        private Slider         _strengthSlider;
        [UiControl("smoothStrengthValue", Description = "平滑強度（数値入力）")]
        private FloatField     _strengthField;

        [UiControl("recalcByAngle", Safety = UiSafety.SafeWrite, Description = "スムージング角でメッシュ全体の法線を作り直す")]
        private Button _recalcByAngleBtn;
        [UiControl("setFromFaces", Safety = UiSafety.SafeWrite, Description = "対象コーナーの法線を面法線にする（フラット化）")]
        private Button _setFromFacesBtn;
        [UiControl("averageFromFaces", Safety = UiSafety.SafeWrite, Description = "対象コーナーの面法線だけを頂点ごとに平均する")]
        private Button _averageFromFacesBtn;
        [UiControl("unify", Safety = UiSafety.SafeWrite, Description = "頂点上のスロット法線を平均で同一値にする")]
        private Button _unifyBtn;
        [UiControl("break", Safety = UiSafety.SafeWrite, Description = "面ごとに別スロットへ分けて面法線を入れる")]
        private Button _breakBtn;
        [UiControl("averageAll", Safety = UiSafety.SafeWrite, Description = "対象法線をまとめて 1 方向に揃える")]
        private Button _averageAllBtn;
        [UiControl("smooth", Safety = UiSafety.SafeWrite, Description = "隣接頂点の法線と補間する")]
        private Button _smoothBtn;
        [UiControl("sphereize", Safety = UiSafety.SafeWrite, Description = "中心から頂点へ向かう方向を法線にする")]
        private Button _sphereizeBtn;
        [UiControl("pointToTarget", Safety = UiSafety.SafeWrite, Description = "座標へ向かう方向を法線にする")]
        private Button _pointToTargetBtn;
        [UiControl("alignToAxisPositive", Safety = UiSafety.SafeWrite, Description = "選択軸の正方向へ法線を向ける")]
        private Button _alignPositiveBtn;
        [UiControl("alignToAxisNegative", Safety = UiSafety.SafeWrite, Description = "選択軸の負方向へ法線を向ける")]
        private Button _alignNegativeBtn;
        [UiControl("flattenOnAxis", Safety = UiSafety.SafeWrite, Description = "選択軸の成分をゼロにして正規化する")]
        private Button _flattenOnAxisBtn;
        [UiControl("flip", Safety = UiSafety.SafeWrite, Description = "対象法線の向きを反転する")]
        private Button _flipBtn;
        [UiControl("mirrorFlattenSeamX", Safety = UiSafety.SafeWrite, Description = "|X 座標| がしきい値以下の頂点の法線 X 成分をゼロにする")]
        private Button _mirrorFlattenSeamXBtn;

        private float _angleDeg = 59.5f;
        private float _strength = 0.5f;

        /// <summary>中央判定しきい値の既定値（高度な選択の NearAxis と同じ）。</summary>
        private const float DefaultMirrorThreshold = 0.00001f;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private MeshContext ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMeshContext;

        private NormalWeightMode WeightMode
            => (NormalWeightMode)Mathf.Clamp(_weightDropdown?.index ?? 0, 0, 3);

        private int Axis
            => Mathf.Clamp(_axisDropdown?.index ?? 0, 0, NormalEditCommand.AxisCount - 1);

        private Vector3 Target => new Vector3(
            _targetX?.value ?? 0f, _targetY?.value ?? 0f, _targetZ?.value ?? 0f);

        private float MirrorThreshold
            => Mathf.Max(MirrorThresholdMin, _mirrorThresholdField?.value ?? DefaultMirrorThreshold);

        // ================================================================
        // レンジ（上下限）
        //
        // 実体は ParameterLimits（persistentDataPath の CSV）にあり、ここでは
        // キーを引くだけにする。同じキーを PanelCommand の PLParam(LimitKey) が
        // 指すので、UI とスキーマで範囲の定義が1箇所になる。
        // ================================================================

        private static float AngleDegMin        => ParameterLimits.GetF("NormalEdit.AngleDeg.Min");
        private static float AngleDegMax        => ParameterLimits.GetF("NormalEdit.AngleDeg.Max");
        private static float StrengthMin        => ParameterLimits.GetF("NormalEdit.Strength.Min");
        private static float StrengthMax        => ParameterLimits.GetF("NormalEdit.Strength.Max");
        private static float MirrorThresholdMin => ParameterLimits.GetF("NormalEdit.MirrorThreshold.Min");

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("法線編集"));

            var help = new HelpBox(
                "面を選択していればその面のコーナー、頂点だけ選択していればその頂点の"
                + "全スロット、選択が無ければメッシュ全体が対象。"
                + "編集したメッシュは法線維持（PreserveNormals）が自動で ON になる。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize     = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _currentSelLabel = new Label();
            _currentSelLabel.style.fontSize     = 10;
            _currentSelLabel.style.marginBottom = 4;
            root.Add(_currentSelLabel);

            // ── 共通設定 ────────────────────────────────────────────────
            _weightDropdown = new DropdownField("平均の重み", WeightNames, 0);
            _weightDropdown.style.marginBottom = 4;
            _weightDropdown.tooltip = "面法線を平均するときの重み付け。角度=コーナー角、面積=面の広さ。";
            root.Add(_weightDropdown);

            // ── A. 再計算 ──────────────────────────────────────────────
            root.Add(SecLabel("再計算"));
            root.Add(MkSliderRow("角度", AngleDegMin, AngleDegMax, _angleDeg, v => _angleDeg = v,
                out _angleSlider, out _angleField));

            var rowRecalc = MkRow();
            rowRecalc.Add(_recalcByAngleBtn = MkBtn("角度で再計算", () => Send(NormalEditCommand.Op.RecalcByAngle),
                "スムージング角でメッシュ全体の法線を作り直す。ハードエッジ分スロットが増える。"));
            rowRecalc.Add(_setFromFacesBtn = MkBtn("面法線にする", () => Send(NormalEditCommand.Op.SetFromFaces),
                "対象コーナーの法線をその面の面法線にする（フラット化）。"));
            root.Add(rowRecalc);

            var rowAvgFaces = MkRow();
            rowAvgFaces.Add(_averageFromFacesBtn = MkBtn("選択面で平均", () => Send(NormalEditCommand.Op.AverageFromFaces),
                "対象コーナーの面法線だけを頂点ごとに平均して書き込む。"
                + "選択した面だけを使った頂点法線が得られる。スロット数は変わらない。"));
            root.Add(rowAvgFaces);

            // ── B. スロット操作 ────────────────────────────────────────
            root.Add(SecLabel("スロット"));
            var rowSlot = MkRow();
            rowSlot.Add(_unifyBtn = MkBtn("統合", () => Send(NormalEditCommand.Op.Unify),
                "頂点上のスロット法線を平均で同一値にする。スロット数は変わらない。"));
            rowSlot.Add(_breakBtn = MkBtn("分離", () => Send(NormalEditCommand.Op.Break),
                "面ごとに別スロットへ分けて面法線を入れる。スロットが増える。"));
            root.Add(rowSlot);

            // ── C. 平均・平滑 ──────────────────────────────────────────
            root.Add(SecLabel("平均・平滑"));
            root.Add(MkSliderRow("平滑強度", StrengthMin, StrengthMax, _strength, v => _strength = v,
                out _strengthSlider, out _strengthField));

            var rowAvg = MkRow();
            rowAvg.Add(_averageAllBtn = MkBtn("1方向に平均", () => Send(NormalEditCommand.Op.AverageAll),
                "対象法線を全部まとめて1方向に揃える。凹凸の陰影を平らにする。"));
            rowAvg.Add(_smoothBtn = MkBtn("平滑化", () => Send(NormalEditCommand.Op.Smooth),
                "辺で繋がった隣接頂点の法線と補間する。"));
            root.Add(rowAvg);

            // ── D. 方向指定 ────────────────────────────────────────────
            root.Add(SecLabel("方向指定"));

            var rowTarget = MkRow();
            var tLabel = new Label("座標");
            tLabel.style.width = 40;
            tLabel.style.fontSize = 10;
            tLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            rowTarget.Add(tLabel);
            _targetX = MkFloat(); rowTarget.Add(_targetX);
            _targetY = MkFloat(); rowTarget.Add(_targetY);
            _targetZ = MkFloat(); rowTarget.Add(_targetZ);
            root.Add(rowTarget);

            _useCenterToggle = new Toggle("球状化の中心に選択の重心を使う") { value = true };
            _useCenterToggle.style.fontSize = 10;
            root.Add(_useCenterToggle);

            _alignVectorsToggle = new Toggle("ターゲット指向を1本のベクトルに揃える") { value = false };
            _alignVectorsToggle.style.fontSize     = 10;
            _alignVectorsToggle.style.marginBottom = 3;
            root.Add(_alignVectorsToggle);

            var rowDir = MkRow();
            rowDir.Add(_sphereizeBtn = MkBtn("球状化", () => Send(NormalEditCommand.Op.Sphereize),
                "中心から頂点へ向かう方向を法線にする。丸みのある部位向け。"));
            rowDir.Add(_pointToTargetBtn = MkBtn("ターゲット指向", () => Send(NormalEditCommand.Op.PointToTarget),
                "座標へ向かう方向を法線にする。凹んだ部位向け。"));
            root.Add(rowDir);

            _axisDropdown = new DropdownField("軸", AxisNames, 0);
            _axisDropdown.style.marginBottom = 3;
            root.Add(_axisDropdown);

            var rowAxis = MkRow();
            rowAxis.Add(_alignPositiveBtn = MkBtn("軸+へ整列", () => Send(NormalEditCommand.Op.AlignToAxis, negative: false),
                "選択軸の正方向へ法線を向ける。"));
            rowAxis.Add(_alignNegativeBtn = MkBtn("軸-へ整列", () => Send(NormalEditCommand.Op.AlignToAxis, negative: true),
                "選択軸の負方向へ法線を向ける。"));
            root.Add(rowAxis);

            var rowFlat = MkRow();
            rowFlat.Add(_flattenOnAxisBtn = MkBtn("軸成分を0に", () => Send(NormalEditCommand.Op.FlattenOnAxis),
                "選択軸の成分をゼロにして正規化する。"));
            rowFlat.Add(_flipBtn = MkBtn("反転", () => Send(NormalEditCommand.Op.Flip),
                "対象法線の向きを反転する。"));
            root.Add(rowFlat);

            // ── E. ミラー（X軸対称） ───────────────────────────────────
            root.Add(SecLabel("ミラー（X軸対称）"));

            var rowMirrorTh = MkRow();
            var mLabel = new Label("しきい値");
            mLabel.style.width          = 60;
            mLabel.style.fontSize       = 10;
            mLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            rowMirrorTh.Add(mLabel);

            _mirrorThresholdField = new FloatField { value = DefaultMirrorThreshold };
            _mirrorThresholdField.style.flexGrow = 1;
            _mirrorThresholdField.tooltip =
                "中央とみなす範囲。|X座標| がこの値以下の頂点が対象。";
            rowMirrorTh.Add(_mirrorThresholdField);
            root.Add(rowMirrorTh);

            var rowMirror = MkRow();
            rowMirror.Add(_mirrorFlattenSeamXBtn = MkBtn("中央の法線Xを0に",
                () => Send(NormalEditCommand.Op.MirrorFlattenSeamX),
                "対象のうち |X座標| がしきい値以下の頂点だけ、法線の X 成分をゼロにして"
                + "正規化する。左右の合わせ目に出る陰影の段差を消す。"));
            root.Add(rowMirror);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var mc = ActiveMeshContext;
            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text         = "";
                _currentSelLabel.text       = "";
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _meshNameLabel.text = mc.Name ?? "(no name)";

            var parts = new List<string>();
            if (mc.Selection?.Vertices.Count > 0) parts.Add($"V:{mc.Selection.Vertices.Count}");
            if (mc.Selection?.Edges.Count    > 0) parts.Add($"E:{mc.Selection.Edges.Count}");
            if (mc.Selection?.Faces.Count    > 0) parts.Add($"F:{mc.Selection.Faces.Count}");

            string scope = (mc.Selection?.Faces.Count > 0) ? "面コーナー"
                         : (mc.Selection?.Vertices.Count > 0) ? "選択頂点の全スロット"
                         : "メッシュ全体";

            _currentSelLabel.text = parts.Count > 0
                ? $"{string.Join("  ", parts)}   対象: {scope}"
                : $"(選択なし)   対象: {scope}";
        }

        // ================================================================
        // 送信
        // ================================================================

        private void Send(NormalEditCommand.Op op, bool negative = false)
        {
            if (ActiveMeshContext == null) { SetStatus("メッシュが選択されていません"); return; }

            SendCommand?.Invoke(new NormalEditCommand(
                ModelIndex,
                op,
                angleDeg: _angleDeg,
                strength: _strength,
                axis: Axis,
                negative: negative,
                target: Target,
                useSelectionCenter: _useCenterToggle?.value ?? true,
                alignVectors: _alignVectorsToggle?.value ?? false,
                weightMode: WeightMode,
                mirrorThreshold: MirrorThreshold));

            Refresh();
            SetStatus($"実行: {op}");
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MkRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button MkBtn(string text, Action onClick, string tooltip)
        {
            var b = new Button(onClick) { text = text, tooltip = tooltip };
            b.style.height      = 22;
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            return b;
        }

        private static FloatField MkFloat()
        {
            var f = new FloatField { value = 0f };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            return f;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MkSliderRow(
            string label, float min, float max, float val, Action<float> onChange,
            out Slider slider, out FloatField field)
        {
            var row = MkRow();

            var lb = new Label(label);
            lb.style.width          = 60;
            lb.style.fontSize       = 10;
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;

            var sl = new Slider(min, max) { value = val };
            sl.style.flexGrow = 1;

            var nf = new FloatField { value = val };
            nf.style.width = 50;

            sl.RegisterValueChangedCallback(e =>
            {
                nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                onChange(e.newValue);
            });
            nf.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp(e.newValue, min, max);
                sl.SetValueWithoutNotify(v);
                onChange(v);
            });

            row.Add(lb); row.Add(sl); row.Add(nf);
            slider = sl;
            field  = nf;
            return row;
        }
    }
}
