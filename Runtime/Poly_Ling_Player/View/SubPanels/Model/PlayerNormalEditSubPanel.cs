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

        private Label          _warningLabel;
        private Label          _meshNameLabel;
        private Label          _currentSelLabel;
        private Label          _statusLabel;
        private DropdownField  _weightDropdown;
        private DropdownField  _axisDropdown;
        private FloatField     _targetX, _targetY, _targetZ;
        private Toggle         _useCenterToggle;
        private Toggle         _alignVectorsToggle;

        private float _angleDeg = 59.5f;
        private float _strength = 0.5f;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private MeshContext ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMeshContext;

        private NormalWeightMode WeightMode
            => (NormalWeightMode)Mathf.Clamp(_weightDropdown?.index ?? 0, 0, 3);

        private int Axis => Mathf.Clamp(_axisDropdown?.index ?? 0, 0, 2);

        private Vector3 Target => new Vector3(
            _targetX?.value ?? 0f, _targetY?.value ?? 0f, _targetZ?.value ?? 0f);

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
            root.Add(MkSliderRow("角度", 0f, 180f, _angleDeg, v => _angleDeg = v));

            var rowRecalc = MkRow();
            rowRecalc.Add(MkBtn("角度で再計算", () => Send(NormalEditCommand.Op.RecalcByAngle),
                "スムージング角でメッシュ全体の法線を作り直す。ハードエッジ分スロットが増える。"));
            rowRecalc.Add(MkBtn("面法線にする", () => Send(NormalEditCommand.Op.SetFromFaces),
                "対象コーナーの法線をその面の面法線にする（フラット化）。"));
            root.Add(rowRecalc);

            var rowAvgFaces = MkRow();
            rowAvgFaces.Add(MkBtn("選択面で平均", () => Send(NormalEditCommand.Op.AverageFromFaces),
                "対象コーナーの面法線だけを頂点ごとに平均して書き込む。"
                + "選択した面だけを使った頂点法線が得られる。スロット数は変わらない。"));
            root.Add(rowAvgFaces);

            // ── B. スロット操作 ────────────────────────────────────────
            root.Add(SecLabel("スロット"));
            var rowSlot = MkRow();
            rowSlot.Add(MkBtn("統合", () => Send(NormalEditCommand.Op.Unify),
                "頂点上のスロット法線を平均で同一値にする。スロット数は変わらない。"));
            rowSlot.Add(MkBtn("分離", () => Send(NormalEditCommand.Op.Break),
                "面ごとに別スロットへ分けて面法線を入れる。スロットが増える。"));
            root.Add(rowSlot);

            // ── C. 平均・平滑 ──────────────────────────────────────────
            root.Add(SecLabel("平均・平滑"));
            root.Add(MkSliderRow("平滑強度", 0f, 1f, _strength, v => _strength = v));

            var rowAvg = MkRow();
            rowAvg.Add(MkBtn("1方向に平均", () => Send(NormalEditCommand.Op.AverageAll),
                "対象法線を全部まとめて1方向に揃える。凹凸の陰影を平らにする。"));
            rowAvg.Add(MkBtn("平滑化", () => Send(NormalEditCommand.Op.Smooth),
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
            rowDir.Add(MkBtn("球状化", () => Send(NormalEditCommand.Op.Sphereize),
                "中心から頂点へ向かう方向を法線にする。丸みのある部位向け。"));
            rowDir.Add(MkBtn("ターゲット指向", () => Send(NormalEditCommand.Op.PointToTarget),
                "座標へ向かう方向を法線にする。凹んだ部位向け。"));
            root.Add(rowDir);

            _axisDropdown = new DropdownField("軸", AxisNames, 0);
            _axisDropdown.style.marginBottom = 3;
            root.Add(_axisDropdown);

            var rowAxis = MkRow();
            rowAxis.Add(MkBtn("軸+へ整列", () => Send(NormalEditCommand.Op.AlignToAxis, negative: false),
                "選択軸の正方向へ法線を向ける。"));
            rowAxis.Add(MkBtn("軸-へ整列", () => Send(NormalEditCommand.Op.AlignToAxis, negative: true),
                "選択軸の負方向へ法線を向ける。"));
            root.Add(rowAxis);

            var rowFlat = MkRow();
            rowFlat.Add(MkBtn("軸成分を0に", () => Send(NormalEditCommand.Op.FlattenOnAxis),
                "選択軸の成分をゼロにして正規化する。"));
            rowFlat.Add(MkBtn("反転", () => Send(NormalEditCommand.Op.Flip),
                "対象法線の向きを反転する。"));
            root.Add(rowFlat);

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
                weightMode: WeightMode));

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
            string label, float min, float max, float val, Action<float> onChange)
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
            return row;
        }
    }
}
