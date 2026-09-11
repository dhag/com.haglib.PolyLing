// UiAutomationService.cs
// UI 自動操作の本体。登録簿（UiAutomationRegistry）の意味 ID で
// パネル表示・項目の表示（Reveal）・値の読み書き・ボタンの押下・強調表示を行う。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【利用者の操作と同じ経路を通す】
//   値の書き込みは value への代入で行い、SetValueWithoutNotify は使わない。
//   各サブパネルは RegisterValueChangedCallback で実処理をしているため、
//   代入すれば利用者が操作したときと同じ処理が走る。
//   ボタンは Button 自身の押下処理（clicked）が走る経路で押す（Click の注記）。
//   パネル表示は登録された ShowXxxPanel をそのまま呼ぶ。
//
// 【安全度】
//   UiSafety（UiControlAttribute.cs）で決める。
//     ReadOnly            … 値の変更もボタンの押下もしない
//     Destructive / File… … allowDestructive を付けたときだけ変える・押す
//     UserOnly            … 自動では変えない・押さない（ダイアログを開く等）
//     Unspecified         … 値の項目は SafeWrite として扱う。ボタンは押さない
//
// 【レイアウト確定を待つ】
//   ShowRightPanel は style.display を書き換えるだけで、レイアウトは次の更新で確定する
//   （PolyLingPlayerViewerCore.ButtonHighlight.cs の ShowRightPanel）。
//   Reveal で表示状態を変えたときは、対象の GeometryChangedEvent（レイアウト確定後の通知）
//   を 1 回だけ受けてからスクロールする。変えていなければ、その場でスクロールする。
//
// 【表示判定】
//   対象から root まで、インラインの style.display が None の祖先が無ければ表示中とみなす。
//   右ペインのセクション（HideAllRightPanels / ShowRightPanel）と Foldout の中身は
//   どちらもインライン style で切り替えているため、これで判定できる。
//
// 【ログ】
//   接頭辞 [UiAutomation]。MCP から呼ばれたときだけ走るので件数は多くない。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public sealed class UiAutomationService
    {
        private const string LogTag = "[UiAutomation]";

        private readonly UiAutomationRegistry  _registry;
        private readonly UiAutomationHighlight _highlight;

        /// <summary>レイアウト確定後にスクロールする対象。無ければ null。</summary>
        private VisualElement _pendingScrollTarget;

        public UiAutomationService(VisualElement root, UiAutomationRegistry registry)
        {
            _registry  = registry;
            _highlight = new UiAutomationHighlight(root);
        }

        // ================================================================
        // 一覧
        // ================================================================

        public CommandResult Describe(string panelId)
        {
            bool filter = !string.IsNullOrEmpty(panelId);
            if (filter && !_registry.TryGetPanel(panelId, out _))
                return CommandResult.Fail($"unknown panelId: {panelId}");

            var pIds   = new List<string>();
            var pDescs = new List<string>();
            foreach (var p in _registry.Panels)
            {
                if (filter && p.Id != panelId) continue;
                pIds.Add(p.Id);
                pDescs.Add(p.Description);
            }
            if (pIds.Count == 0)
                return CommandResult.Fail("登録済みのパネルがありません");

            var cIds    = new List<string>();
            var cPanels = new List<string>();
            var cTypes  = new List<string>();
            var cDescs  = new List<string>();
            var cSafety = new List<string>();
            var cWrite  = new List<int>();
            foreach (var c in _registry.Controls)
            {
                if (filter && c.PanelId != panelId) continue;
                var e = c.Element;
                cIds.Add(c.Id);
                cPanels.Add(c.PanelId);
                cTypes.Add(e != null ? e.GetType().Name : "(未構築)");
                cDescs.Add(c.Description);
                cSafety.Add(SafetyText(c.Safety));
                cWrite.Add(e != null && IsWritable(c, e) ? 1 : 0);
            }

            return CommandResult.Ok(data: CommandDataJson.New()
                .Texts("panelIds",            pIds)
                .Texts("panelDescriptions",   pDescs)
                .Texts("controlIds",          cIds)
                .Texts("controlPanels",       cPanels)
                .Texts("controlTypes",        cTypes)
                .Texts("controlDescriptions", cDescs)
                .Texts("controlSafety",       cSafety)
                .Ints ("controlWritable",     cWrite)
                .Build());
        }

        // ================================================================
        // パネル表示・項目の表示
        // ================================================================

        public CommandResult ShowPanel(string panelId)
        {
            if (!_registry.TryGetPanel(panelId, out var p))
                return CommandResult.Fail($"unknown panelId: {panelId}");

            p.Show();
            Debug.Log($"{LogTag} ShowPanel: {panelId}");
            return CommandResult.Ok();
        }

        public CommandResult Reveal(string controlId, bool highlight)
        {
            if (!_registry.TryGetControl(controlId, out var c))
            {
                // 動的な項目（モードごとの設定行など）は、所属パネルを開くと作られる。
                if (!_registry.TryGetDynamicPanelFor(controlId, out var dynPanel))
                    return CommandResult.Fail($"unknown controlId: {controlId}");
                dynPanel.Show();
                if (!_registry.TryGetControl(controlId, out c))
                    return CommandResult.Fail($"unknown controlId (not built even after showing {dynPanel.Id}): {controlId}");
            }
            var element = c.Element;
            if (!_registry.TryGetPanel(c.PanelId, out var p))
                return CommandResult.Fail($"control is not attached to panel: {controlId}");

            bool wasDisplayed = element != null && IsDisplayed(element);

            // ShowXxxPanel → HideAllRightPanels → OnRightPanelsHidden で
            // 前の強調と保留中のスクロールは消える。
            p.Show();

            bool prepared = c.PrepareReveal != null && c.PrepareReveal();

            // 下準備で要素が作り直されることがあるので読み直す。
            element = c.Element;
            if (element == null)
                return CommandResult.Fail($"control is not built: {controlId}");

            bool opened = OpenAncestorFoldouts(element);

            if (!IsDisplayed(element))
                return CommandResult.Fail($"control is not displayed after reveal: {controlId}");

            bool changed = !wasDisplayed || prepared || opened;
            if (changed) ScrollAfterLayout(element);
            else         ScrollIntoView(element);

            if (highlight) _highlight.Show(element);

            Debug.Log($"{LogTag} Reveal: {controlId} (changed={changed}, highlight={highlight})");
            return CommandResult.Ok();
        }

        // ================================================================
        // 値
        // ================================================================

        public CommandResult GetValue(string controlId)
        {
            if (!TryResolve(controlId, out var c, out var element, out string err))
                return CommandResult.Fail(err);

            string value;
            if (c.Getter != null) value = c.Getter() ?? "";
            else if (!TryReadValue(element, out value))
                return CommandResult.Fail(
                    $"control has no readable value: {controlId} ({element.GetType().Name})");

            var b = CommandDataJson.New()
                .Text("controlId", controlId)
                .Text("type",      element.GetType().Name)
                .Text("value",     value);

            switch (element)
            {
                case Slider s:
                    b.Num("min", s.lowValue).Num("max", s.highValue);
                    break;
                case SliderInt si:
                    b.Num("min", si.lowValue).Num("max", si.highValue);
                    break;
                case DropdownField d:
                    b.Texts("choices", d.choices);
                    break;
                case EnumField en when en.value != null:
                    b.Texts("choices", Enum.GetNames(en.value.GetType()));
                    break;
                case BaseListView lv:
                    b.Num("min", -1).Num("max", (lv.itemsSource?.Count ?? 0) - 1);
                    break;
                case RadioButtonGroup rg when rg.choices != null:
                    b.Texts("choices", new List<string>(rg.choices));
                    break;
            }

            return CommandResult.Ok(data: b.Build());
        }

        public CommandResult SetValue(string controlId, string text, bool allowDestructive)
        {
            if (!TryResolve(controlId, out var c, out var element, out string err))
                return CommandResult.Fail(err);
            if (!IsWritable(c, element))
                return CommandResult.Fail(
                    $"control is not writable: {controlId} ({element.GetType().Name})");
            if (!CheckSafety(c, allowDestructive, forClick: false, out err))
                return CommandResult.Fail(err);
            if (!element.enabledInHierarchy)
                return CommandResult.Fail($"control is disabled: {controlId}");

            err = c.Setter != null ? c.Setter(text ?? "") : WriteValue(element, text ?? "");
            if (err != null) return CommandResult.Fail(err);

            // 項目側の丸め（スライダーの範囲など）を反映した値を返す。
            string after;
            var now = c.Element;
            if (c.Getter != null) after = c.Getter() ?? "";
            else if (now == null || !TryReadValue(now, out after)) after = text ?? "";

            Debug.Log($"{LogTag} SetValue: {controlId} = {after}");
            return CommandResult.Ok(data: CommandDataJson.New()
                .Text("controlId", controlId)
                .Text("value",     after)
                .Build());
        }

        // ================================================================
        // ボタン
        // ================================================================

        /// <summary>
        /// ボタンを押す。表示中・有効・安全度の条件を満たすときだけ押す。
        ///
        /// 【押し方】
        ///   Button は NavigationSubmitEvent を受けると clickable 経由で clicked を呼ぶ
        ///   （キーボードの決定操作と同じ経路）。clicked に登録された処理がそのまま走る。
        /// </summary>
        public CommandResult Click(string controlId, bool allowDestructive)
        {
            if (!TryResolve(controlId, out var c, out var element, out string err))
                return CommandResult.Fail(err);
            if (!(element is Button button))
                return CommandResult.Fail(
                    $"control is not a button: {controlId} ({element.GetType().Name})");
            if (!CheckSafety(c, allowDestructive, forClick: true, out err))
                return CommandResult.Fail(err);
            if (!IsDisplayed(button))
                return CommandResult.Fail(
                    $"control is not displayed: {controlId}（uiReveal で表示してから押す）");
            if (!button.enabledInHierarchy)
                return CommandResult.Fail($"control is disabled: {controlId}");

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }

            Debug.Log($"{LogTag} Click: {controlId}");
            return CommandResult.Ok(data: CommandDataJson.New()
                .Text("controlId", controlId)
                .Build());
        }

        // ================================================================
        // 強調表示
        // ================================================================

        public CommandResult Highlight(string controlId, bool enabled)
        {
            if (!TryResolve(controlId, out var c, out var element, out string err))
                return CommandResult.Fail(err);

            if (!enabled)
            {
                if (_highlight.Target == element) _highlight.Clear();
                Debug.Log($"{LogTag} Highlight off: {controlId}");
                return CommandResult.Ok();
            }

            if (!IsDisplayed(element))
                return CommandResult.Fail(
                    $"control is not displayed: {controlId}（uiReveal で表示してから強調する）");

            _highlight.Show(element);
            Debug.Log($"{LogTag} Highlight: {controlId}");
            return CommandResult.Ok();
        }

        /// <summary>
        /// 右ペインのパネルを全部隠したとき（HideAllRightPanels）に呼ぶ。
        /// 強調と保留中のスクロールは、そのパネルを見ている間だけのもの。
        /// </summary>
        public void OnRightPanelsHidden()
        {
            CancelPendingScroll();
            _highlight.Clear();
        }

        public void Dispose()
        {
            CancelPendingScroll();
            _highlight.Dispose();
        }

        // ================================================================
        // 検査から使う判定
        // ================================================================

        /// <summary>この要素の値を既定の読み取りで読めるか。</summary>
        public static bool IsReadableType(VisualElement e) => TryReadValue(e, out _);

        /// <summary>この要素の値を既定の書き込みで変えられるか。</summary>
        public static bool IsWritableType(VisualElement e)
            => e is Slider || e is SliderInt || e is FloatField || e is IntegerField
            || e is TextField || e is Toggle || e is DropdownField || e is EnumField
            || e is Foldout || e is BaseListView || e is RadioButtonGroup;

        public static string SafetyText(UiSafety s)
        {
            switch (s)
            {
                case UiSafety.ReadOnly:      return "readOnly";
                case UiSafety.SafeWrite:     return "safeWrite";
                case UiSafety.Destructive:   return "destructive";
                case UiSafety.FileOperation: return "fileOperation";
                case UiSafety.UserOnly:      return "userOnly";
                default:                     return "unspecified";
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        private bool TryResolve(
            string controlId, out UiAutomationRegistry.ControlEntry c, out VisualElement element,
            out string error)
        {
            element = null;
            error   = null;
            if (!_registry.TryGetControl(controlId, out c))
            {
                error = _registry.TryGetDynamicPanelFor(controlId, out var dynPanel)
                    ? $"control is not built: {controlId}（パネル {dynPanel.Id} を開くと作られる。uiReveal か uiShowPanel を先に呼ぶ）"
                    : $"unknown controlId: {controlId}";
                return false;
            }
            element = c.Element;
            if (element == null)
            {
                error = $"control is not built: {controlId}";
                return false;
            }
            return true;
        }

        private static bool IsWritable(UiAutomationRegistry.ControlEntry c, VisualElement e)
        {
            if (c.Safety == UiSafety.ReadOnly) return false;
            return c.Setter != null || IsWritableType(e);
        }

        /// <summary>安全度を見て、変更・押下してよいかを返す。</summary>
        private static bool CheckSafety(
            UiAutomationRegistry.ControlEntry c, bool allowDestructive, bool forClick, out string error)
        {
            error = null;
            switch (c.Safety)
            {
                case UiSafety.ReadOnly:
                    error = $"control is read-only: {c.Id}";
                    return false;

                case UiSafety.UserOnly:
                    error = $"control requires user interaction: {c.Id}（ダイアログを開く等。強調表示だけできる）";
                    return false;

                case UiSafety.Destructive:
                case UiSafety.FileOperation:
                    if (allowDestructive) return true;
                    error = $"control is {SafetyText(c.Safety)}: {c.Id}（allowDestructive=true が要る）";
                    return false;

                case UiSafety.Unspecified:
                    if (!forClick) return true;
                    error = $"safety is not specified: {c.Id}（ボタンは安全度が決まるまで押さない）";
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>対象から root まで、インラインの display が None の要素が無いか。</summary>
        private static bool IsDisplayed(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var v = e; v != null; v = v.parent)
                if (v.style.display.value == DisplayStyle.None) return false;
            return true;
        }

        /// <summary>祖先の Foldout を全部開く。1 つでも開いたら true。</summary>
        private static bool OpenAncestorFoldouts(VisualElement e)
        {
            bool opened = false;
            for (var p = e?.parent; p != null; p = p.parent)
            {
                if (p is Foldout f && !f.value)
                {
                    f.value = true;
                    opened  = true;
                }
            }
            return opened;
        }

        /// <summary>祖先の ScrollView を内側から順に、対象が見える位置までスクロールする。</summary>
        private static void ScrollIntoView(VisualElement target)
        {
            if (target == null || !UiAutomationHighlight.IsUsable(target.worldBound)) return;
            for (var p = target.parent; p != null; p = p.parent)
            {
                if (p is ScrollView sv && sv.contentContainer.Contains(target))
                    sv.ScrollTo(target);
            }
        }

        private void ScrollAfterLayout(VisualElement target)
        {
            CancelPendingScroll();
            _pendingScrollTarget = target;
            target.RegisterCallback<GeometryChangedEvent>(OnPendingScrollGeometry);
        }

        private void OnPendingScrollGeometry(GeometryChangedEvent evt)
        {
            var t = _pendingScrollTarget;
            if (t == null || evt.target != t) return;
            CancelPendingScroll();
            ScrollIntoView(t);
        }

        private void CancelPendingScroll()
        {
            if (_pendingScrollTarget != null)
                _pendingScrollTarget.UnregisterCallback<GeometryChangedEvent>(OnPendingScrollGeometry);
            _pendingScrollTarget = null;
        }

        // ================================================================
        // UI 型ごとの読み書き
        // ================================================================

        private static bool TryReadValue(VisualElement e, out string text)
        {
            switch (e)
            {
                case Slider s:        text = FormatFloat(s.value);  return true;
                case SliderInt si:    text = FormatInt(si.value);   return true;
                case FloatField f:    text = FormatFloat(f.value);  return true;
                case IntegerField i:  text = FormatInt(i.value);    return true;
                case TextField t:     text = t.value ?? "";         return true;
                case Toggle b:        text = b.value ? "true" : "false"; return true;
                case DropdownField d: text = d.value ?? "";         return true;
                case EnumField en:    text = en.value != null ? en.value.ToString() : ""; return true;
                case Foldout fo:      text = fo.value ? "true" : "false"; return true;
                case RadioButtonGroup rg: text = FormatInt(rg.value); return true;
                case BaseListView lv: text = FormatInt(lv.selectedIndex); return true;
                case BaseTreeView tv: text = FormatInt(tv.selectedIndex); return true;
                case Button bt:       text = bt.text ?? "";         return true;
                case Label l:         text = l.text ?? "";          return true;
                case HelpBox hb:      text = hb.text ?? "";         return true;
                default:              text = null;                  return false;
            }
        }

        /// <summary>value へ代入する。成功で null、失敗で理由。</summary>
        private static string WriteValue(VisualElement e, string text)
        {
            switch (e)
            {
                case Slider s:
                    if (!TryParseFloat(text, out float sv)) return $"invalid float: {text}";
                    s.value = sv;
                    return null;

                case SliderInt si:
                    if (!TryParseInt(text, out int siv)) return $"invalid int: {text}";
                    si.value = siv;
                    return null;

                case FloatField f:
                    if (!TryParseFloat(text, out float fv)) return $"invalid float: {text}";
                    f.value = fv;
                    return null;

                case IntegerField i:
                    if (!TryParseInt(text, out int iv)) return $"invalid int: {text}";
                    i.value = iv;
                    return null;

                case TextField t:
                    t.value = text;
                    return null;

                case Toggle b:
                    if (!bool.TryParse(text, out bool bv)) return $"invalid bool: {text}（true / false）";
                    b.value = bv;
                    return null;

                case DropdownField d:
                    if (d.choices == null || !d.choices.Contains(text))
                        return $"dropdown value not found: {text}"
                             + $"（choices: {string.Join(" / ", d.choices ?? new List<string>())}）";
                    d.value = text;
                    return null;

                case EnumField en:
                {
                    if (en.value == null) return "enum type is not set";
                    var type  = en.value.GetType();
                    var names = Enum.GetNames(type);
                    if (Array.IndexOf(names, text) < 0)
                        return $"enum value not found: {text}（choices: {string.Join(" / ", names)}）";
                    en.value = (Enum)Enum.Parse(type, text);
                    return null;
                }

                case Foldout fo:
                    if (!bool.TryParse(text, out bool fov)) return $"invalid bool: {text}（true / false）";
                    fo.value = fov;
                    return null;

                case RadioButtonGroup rg:
                {
                    if (!TryParseInt(text, out int ri)) return $"invalid int: {text}";
                    int n = CountChoices(rg);
                    if (ri < 0 || ri >= n) return $"radio index out of range: {ri}（0 から {n - 1}）";
                    rg.value = ri;
                    return null;
                }

                case BaseListView lv:
                {
                    if (!TryParseInt(text, out int idx)) return $"invalid int: {text}";
                    int count = lv.itemsSource?.Count ?? 0;
                    if (idx < -1 || idx >= count)
                        return $"list index out of range: {idx}（-1 から {count - 1}）";
                    if (idx < 0) lv.ClearSelection();
                    else         lv.SetSelection(idx);
                    return null;
                }

                default:
                    return $"control is not writable: {e?.GetType().Name}";
            }
        }

        private static bool TryParseFloat(string s, out float v)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static bool TryParseInt(string s, out int v)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        /// <summary>RadioButtonGroup の選択肢の数。</summary>
        private static int CountChoices(RadioButtonGroup rg)
        {
            int n = 0;
            if (rg.choices != null) foreach (var _ in rg.choices) n++;
            return n;
        }

        private static string FormatFloat(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string FormatInt(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
