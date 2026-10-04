// PlayerLightSubPanel.cs
// 3Dプレビューを照らすライトの設定パネル（UIToolkit・右ペイン）。
// ライトは任意個数。一覧から 1 つ選び、その項目を編集する。
// 値変更時は _set を呼び、ViewerCore が
// PlayerViewportManager.EnterDisplaySettingsChanged(ViewportLightSettings) 経由で反映する。
// 保存は PlayerViewportManager が RecentPaths へ行う（キー "Viewport.Lights"）。
// Runtime/Poly_Ling_Player/View/SubPanels/Light/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerLightSubPanel
    {
        private readonly Func<ViewportLightSettings>   _get;
        private readonly Action<ViewportLightSettings> _set;

        // UI 自動操作の ID は "light.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("select", Description = "編集するライトを選ぶ")]
        private DropdownField _selectDropdown;
        [UiControl("add", Safety = UiSafety.SafeWrite, Description = "ライトを追加する（選択中のライトを複製）")]
        private Button        _addBtn;
        [UiControl("remove", Safety = UiSafety.Destructive, Description = "選択中のライトを削除する")]
        private Button        _removeBtn;
        [UiControl("enabled", Description = "このライトを点ける")]
        private Toggle        _enabledToggle;
        [UiControl("kind", Description = "ライトの種類（平行光源・点光源・スポット）")]
        private DropdownField _kindDropdown;
        [UiControl("colorR", Description = "色の赤（0〜1）")]
        private FloatField    _colorR;
        [UiControl("colorG", Description = "色の緑（0〜1）")]
        private FloatField    _colorG;
        [UiControl("colorB", Description = "色の青（0〜1）")]
        private FloatField    _colorB;
        [UiControl("intensity", Description = "明るさ")]
        private FloatField    _intensity;
        [UiControl("pitch", Description = "上下の角度（度。正で下向き、-90〜90）")]
        private FloatField    _pitch;
        [UiControl("yaw", Description = "水平の角度（度。0〜360）")]
        private FloatField    _yaw;
        [UiControl("posX", Description = "位置 X（点光源・スポット）")]
        private FloatField    _posX;
        [UiControl("posY", Description = "位置 Y（点光源・スポット）")]
        private FloatField    _posY;
        [UiControl("posZ", Description = "位置 Z（点光源・スポット）")]
        private FloatField    _posZ;
        [UiControl("range", Description = "届く範囲（点光源・スポット）")]
        private FloatField    _range;
        [UiControl("spotAngle", Description = "スポットの開き角（度）")]
        private FloatField    _spotAngle;
        [UiControl("shadows", Description = "影を落とす")]
        private Toggle        _shadowsToggle;
        [UiControl("reset", Safety = UiSafety.Destructive, Description = "既定の 3 灯に戻す")]
        private Button        _resetBtn;

        [UiControl(Ignore = true)]
        private VisualElement _editBox;    // ライト 0 個のときは隠す
        [UiControl(Ignore = true)]
        private Label         _emptyNote;
        private int  _selected;
        private bool _suppress;

        private static readonly List<string> KindNames = new List<string>
        {
            "平行光源", "点光源", "スポット",
        };

        public PlayerLightSubPanel(Func<ViewportLightSettings> get, Action<ViewportLightSettings> set)
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

            parent.Add(PlayerIoUiKit.Title("ライト"));

            var note = new Label("設定は4面のビューポート共通です。起動時、シーンに置かれたライトは無効になります。");
            note.style.fontSize     = 10;
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            parent.Add(note);

            // ── 一覧 ────────────────────────────────────────────────
            _selectDropdown = new DropdownField("ライト", new List<string>(), 0);
            _selectDropdown.style.marginBottom = 2;
            _selectDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_suppress) return;
                _selected = Mathf.Max(0, _selectDropdown.index);
                Refresh();
            });
            parent.Add(_selectDropdown);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            _addBtn    = new Button(OnAdd)    { text = "追加" };
            _removeBtn = new Button(OnRemove) { text = "削除" };
            _addBtn.style.flexGrow = 1; _addBtn.style.marginRight = 2;
            _removeBtn.style.flexGrow = 1;
            row.Add(_addBtn); row.Add(_removeBtn);
            parent.Add(row);

            _emptyNote = new Label("ライトがありません。「追加」で作れます。");
            _emptyNote.style.fontSize = 10;
            parent.Add(_emptyNote);

            // ── 選択中のライト ──────────────────────────────────────
            _editBox = new VisualElement();
            parent.Add(_editBox);

            _enabledToggle = new Toggle("点灯") { value = true };
            _enabledToggle.RegisterValueChangedCallback(_ => WriteFields());
            _editBox.Add(_enabledToggle);

            _kindDropdown = new DropdownField("種類", KindNames, 0);
            _kindDropdown.style.marginBottom = 2;
            _kindDropdown.RegisterValueChangedCallback(_ => WriteFields());
            _editBox.Add(_kindDropdown);

            _editBox.Add(PlayerIoUiKit.SectionLabel("色・明るさ"));
            _colorR    = MakeFloat("赤");
            _colorG    = MakeFloat("緑");
            _colorB    = MakeFloat("青");
            _intensity = MakeFloat("明るさ");

            _editBox.Add(PlayerIoUiKit.SectionLabel("向き"));
            _pitch = MakeFloat("上下（度）");
            _yaw   = MakeFloat("水平（度）");

            _editBox.Add(PlayerIoUiKit.SectionLabel("位置・範囲（点光源・スポット）"));
            _posX      = MakeFloat("X");
            _posY      = MakeFloat("Y");
            _posZ      = MakeFloat("Z");
            _range     = MakeFloat("範囲");
            _spotAngle = MakeFloat("開き角（度）");

            _editBox.Add(PlayerIoUiKit.SectionLabel("影"));
            _shadowsToggle = new Toggle("影を落とす") { value = true };
            _shadowsToggle.RegisterValueChangedCallback(_ => WriteFields());
            _editBox.Add(_shadowsToggle);

            // ── 既定値へ戻す ─────────────────────────────────────────
            _resetBtn = new Button(OnReset) { text = "既定値に戻す（3灯）" };
            _resetBtn.style.marginTop = 6;
            _resetBtn.style.height    = 24;
            parent.Add(_resetBtn);

            Refresh();
        }

        private FloatField MakeFloat(string label)
        {
            var f = new FloatField(label);
            f.style.marginBottom = 2;
            f.RegisterValueChangedCallback(_ => WriteFields());
            _editBox.Add(f);
            return f;
        }

        // ================================================================
        // 同期
        // ================================================================

        private ViewportLightSettings Current()
            => (_get != null ? _get() : ViewportLightSettings.Default).Clamped();

        /// <summary>現在の設定値を一覧とフィールドへ反映する。</summary>
        public void Refresh()
        {
            if (_selectDropdown == null) return;
            var s = Current();
            int n = s.Lights.Count;
            if (_selected >= n) _selected = n - 1;
            if (_selected < 0)  _selected = 0;

            _suppress = true;

            var names = new List<string>();
            for (int i = 0; i < n; i++)
                names.Add($"{i}: {KindNames[(int)s.Lights[i].Kind]}{(s.Lights[i].Enabled ? "" : "（消灯）")}");
            _selectDropdown.choices = names;
            if (n > 0) _selectDropdown.SetValueWithoutNotify(names[_selected]);
            else       _selectDropdown.SetValueWithoutNotify("");

            bool has = n > 0;
            _editBox.style.display   = has ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyNote.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            _removeBtn.SetEnabled(has);

            if (has)
            {
                var e = s.Lights[_selected];
                _enabledToggle.SetValueWithoutNotify(e.Enabled);
                _kindDropdown .SetValueWithoutNotify(KindNames[(int)e.Kind]);
                _colorR       .SetValueWithoutNotify(e.Color.r);
                _colorG       .SetValueWithoutNotify(e.Color.g);
                _colorB       .SetValueWithoutNotify(e.Color.b);
                _intensity    .SetValueWithoutNotify(e.Intensity);
                _pitch        .SetValueWithoutNotify(e.Pitch);
                _yaw          .SetValueWithoutNotify(e.Yaw);
                _posX         .SetValueWithoutNotify(e.Position.x);
                _posY         .SetValueWithoutNotify(e.Position.y);
                _posZ         .SetValueWithoutNotify(e.Position.z);
                _range        .SetValueWithoutNotify(e.Range);
                _spotAngle    .SetValueWithoutNotify(e.SpotAngle);
                _shadowsToggle.SetValueWithoutNotify(e.Shadows);
            }

            _suppress = false;
        }

        /// <summary>フィールド値を選択中のライトへ書き込み、反映を要求する。</summary>
        private void WriteFields()
        {
            if (_suppress || _selectDropdown == null) return;
            var s = Current();
            if (_selected < 0 || _selected >= s.Lights.Count) return;

            int kind = _kindDropdown.index < 0 ? 0 : _kindDropdown.index;
            s.Lights[_selected] = new PlayerLightEntry
            {
                Enabled   = _enabledToggle.value,
                Kind      = (PlayerLightKind)kind,
                Color     = new Color(_colorR.value, _colorG.value, _colorB.value, 1f),
                Intensity = _intensity.value,
                Pitch     = _pitch.value,
                Yaw       = _yaw.value,
                Position  = new Vector3(_posX.value, _posY.value, _posZ.value),
                Range     = _range.value,
                SpotAngle = _spotAngle.value,
                Shadows   = _shadowsToggle.value,
            }.Clamped();

            _set?.Invoke(s);
            Refresh();   // クランプ結果と一覧の表記を戻す
        }

        private void OnAdd()
        {
            var s = Current();
            var e = (_selected >= 0 && _selected < s.Lights.Count)
                ? s.Lights[_selected].Clone()
                : new PlayerLightEntry();
            s.Lights.Add(e);
            _selected = s.Lights.Count - 1;
            _set?.Invoke(s);
            Refresh();
        }

        private void OnRemove()
        {
            var s = Current();
            if (_selected < 0 || _selected >= s.Lights.Count) return;
            s.Lights.RemoveAt(_selected);
            if (_selected >= s.Lights.Count) _selected = s.Lights.Count - 1;
            _set?.Invoke(s);
            Refresh();
        }

        private void OnReset()
        {
            _selected = 0;
            _set?.Invoke(ViewportLightSettings.Default);
            Refresh();
        }
    }
}
