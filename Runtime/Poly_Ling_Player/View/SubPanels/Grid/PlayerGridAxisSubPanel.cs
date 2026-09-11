// PlayerGridAxisSubPanel.cs
// 3Dプレビューの「軸」「グリッド平面」の表示設定パネル（UIToolkit・右ペイン）。
// 設定は4面のビューポート共通。値変更時は _set を呼び、ViewerCore が
// PlayerViewportManager.EnterDisplaySettingsChanged 経由で再描画を要求する。
// Runtime/Poly_Ling_Player/View/SubPanels/Grid/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerGridAxisSubPanel
    {
        private readonly Func<ViewportGridSettings>   _get;
        private readonly Action<ViewportGridSettings> _set;

        // UI 自動操作の ID は "gridAxis.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("showAxis", Description = "軸を表示する")]
        private Toggle        _axisToggle;
        [UiControl("axisLength", Description = "軸の長さ")]
        private FloatField    _axisLength;
        [UiControl("showGrid", Description = "グリッドを表示する")]
        private Toggle        _gridToggle;
        [UiControl("plane", Description = "グリッドの平面")]
        private DropdownField _planeDropdown;
        [UiControl("cellSize", Description = "マスの大きさ")]
        private FloatField    _cellSize;
        [UiControl("halfCount", Description = "分割数（片側）")]
        private IntegerField  _halfCount;
        [UiControl("boneMarkerScale", Description = "ボーンの目印の大きさ")]
        private FloatField    _boneMarkerScale;
        [UiControl("reset", Safety = UiSafety.SafeWrite, Description = "既定値に戻す")]
        private Button        _resetBtn;

        private bool _suppress;   // フィールド→設定 反映の一時抑止（同期時）

        private static readonly List<string> PlaneNames = new List<string>
        {
            "XZ（床）", "XY（正面）", "YZ（側面）",
        };

        public PlayerGridAxisSubPanel(Func<ViewportGridSettings> get, Action<ViewportGridSettings> set)
        {
            _get = get;
            _set = set;
        }

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();

            parent.Add(PlayerIoUiKit.Title("軸 / グリッド"));

            var note = new Label("設定は4面のビューポート共通です。");
            note.style.fontSize     = 10;
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            parent.Add(note);

            // ── 軸 ─────────────────────────────────────────────────
            parent.Add(PlayerIoUiKit.SectionLabel("軸"));

            _axisToggle = new Toggle("軸を表示") { value = true };
            _axisToggle.RegisterValueChangedCallback(_ => WriteFields());
            parent.Add(_axisToggle);

            _axisLength = MakeFloat("軸の長さ", 10f);
            parent.Add(_axisLength);

            // ── グリッド ────────────────────────────────────────────
            parent.Add(PlayerIoUiKit.SectionLabel("グリッド平面"));

            _gridToggle = new Toggle("グリッドを表示") { value = true };
            _gridToggle.RegisterValueChangedCallback(_ => WriteFields());
            parent.Add(_gridToggle);

            _planeDropdown = new DropdownField("平面", PlaneNames, 0);
            _planeDropdown.style.marginBottom = 2;
            _planeDropdown.RegisterValueChangedCallback(_ => WriteFields());
            parent.Add(_planeDropdown);

            _cellSize = MakeFloat("セルサイズ", 1f);
            parent.Add(_cellSize);

            _halfCount = new IntegerField("分割数（片側）") { value = 10 };
            _halfCount.style.marginBottom = 2;
            _halfCount.RegisterValueChangedCallback(_ => WriteFields());
            parent.Add(_halfCount);

            // ── マーカー ────────────────────────────────────────────
            // ボーンとメッシュ原点は同じくさび形を共有しており、大きさも共通。
            parent.Add(PlayerIoUiKit.SectionLabel("マーカー"));

            _boneMarkerScale = MakeFloat("ボーン/原点の大きさ", ViewportGridSettings.Default.BoneMarkerScale);
            parent.Add(_boneMarkerScale);

            // ── 既定値へ戻す ─────────────────────────────────────────
            var resetBtn = new Button(OnReset) { text = "既定値に戻す" };
            resetBtn.style.marginTop = 6;
            resetBtn.style.height    = 24;
            parent.Add(resetBtn);
            _resetBtn = resetBtn;

            Refresh();
        }

        private FloatField MakeFloat(string label, float initial)
        {
            var f = new FloatField(label) { value = initial };
            f.style.marginBottom = 2;
            f.RegisterValueChangedCallback(_ => WriteFields());
            return f;
        }

        // ================================================================
        // 同期
        // ================================================================

        /// <summary>現在の設定値をフィールドへ反映する。</summary>
        public void Refresh()
        {
            if (_axisToggle == null) return;
            var s = (_get != null ? _get() : ViewportGridSettings.Default).Clamped();

            _suppress = true;
            _axisToggle   .SetValueWithoutNotify(s.ShowAxis);
            _axisLength   .SetValueWithoutNotify(s.AxisLength);
            _gridToggle   .SetValueWithoutNotify(s.ShowGrid);
            _planeDropdown.SetValueWithoutNotify(PlaneNames[Mathf.Clamp((int)s.Plane, 0, PlaneNames.Count - 1)]);
            _cellSize     .SetValueWithoutNotify(s.CellSize);
            _halfCount    .SetValueWithoutNotify(s.HalfCount);
            _boneMarkerScale.SetValueWithoutNotify(s.BoneMarkerScale);
            _suppress = false;
        }

        /// <summary>フィールド値を設定へ書き込み、再描画を要求する。</summary>
        private void WriteFields()
        {
            if (_suppress) return;
            if (_axisToggle == null) return;

            int planeIdx = _planeDropdown != null ? _planeDropdown.index : 0;
            if (planeIdx < 0) planeIdx = 0;

            var s = new ViewportGridSettings
            {
                ShowAxis   = _axisToggle.value,
                AxisLength = _axisLength.value,
                ShowGrid   = _gridToggle.value,
                Plane      = (GridPlaneKind)planeIdx,
                CellSize   = _cellSize.value,
                HalfCount  = _halfCount.value,
                BoneMarkerScale = _boneMarkerScale.value,
            }.Clamped();

            _set?.Invoke(s);
        }

        private void OnReset()
        {
            _set?.Invoke(ViewportGridSettings.Default);
            Refresh();
        }
    }
}
