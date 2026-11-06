// ParamFieldBuilder.cs
// 引数の宣言（型と PLParam）から引数欄を作る部品。
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.5・段 5
//   数値 → 数値欄＋スライダー（Min・Max、無ければ LimitKey。範囲が無ければ数値欄だけ）
//   Vector2・Vector3 → 成分ごとの数値欄（範囲があれば成分ごとにスライダー）
//   列挙型 → 選択ボタン（選択肢が多ければドロップダウン）、真偽 → チェックボックス
//   IsMeshRef（int）→ オブジェクトのドロップダウン、文字列 → 文字列欄
//   それ以外（配列・IsMeshRef の int[] など）は欄を作らない。呼び出し側の「詳細」の文字列欄で書く。
//
// 【宣言の出どころは 2 つ】
//   処理列の種類（MeshModifierRegistry の MeshModifierInfo.Params）と、
//   コマンドの引数（PanelCommandFactory.TryGetArgParam）。呼び出し側がどちらかから ParamFieldSpec を作る。
//
// 【値の文字列】
//   PanelCommandFactory.TryParseValue / TryFormatValue と同じ規則（ToArgs・FromArgs と同じ）。
//
// 【送る時機】（前例：PlayerMaterialListSubPanel の Metallic・Smoothness、PlayerBoneEditorSubPanel の回転スライダー）
//   数値欄・選択・チェックは値が決まった時点で OnCommit。数値欄は isDelayed（Enter かフォーカスを外したとき）。
//   スライダーは動かしている間 OnPreview（null なら何も送らない）、離したとき・フォーカスを外したときに OnCommit。
//
// 【UI 自動操作】
//   欄は UiDynamicControls に登録する。ID は "arg.<キー>"、スライダーは "arg.<キー>.slider"、
//   成分は "arg.<キー>.x"・"arg.<キー>.x.slider"。行ヘルパを通さずに欄を作らない（サブツール作成ガイド B 節）。
//
// 【読み直し】
//   Signature が同じなら Build し直さず UpdateValues で値だけ合わせる。下見中（スライダーを動かしている間）の
//   キーは書き換えない（パネルの読み直しでドラッグ中のスライダーを壊さないため）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>引数欄 1 つぶんの宣言と今の値。</summary>
    public sealed class ParamFieldSpec
    {
        public string           Key   = "";
        public Type             Type;
        public PLParamAttribute Attr;
        /// <summary>今の値（ToArgs と同じ文字列）。</summary>
        public string           Value = "";
        /// <summary>IsMeshRef の欄の選択肢（表示名と索引）。</summary>
        public List<KeyValuePair<string, int>> MeshChoices;

        /// <summary>見出しを決めて渡すとき。空なら Description から作る。</summary>
        public string LabelText = "";

        /// <summary>見出し。Description の最初の句（「。」「（」の前）。無ければキー。</summary>
        public string Label
        {
            get
            {
                if (!string.IsNullOrEmpty(LabelText)) return LabelText;
                // 内部の中間データの呼び名は画面に出さない（操作統合計画 3 章）。
                string d = (Attr?.Description ?? "").Replace("梯子", "帯").Replace("はしご", "帯").Replace("rung", "段");
                int cut = d.Length;
                foreach (char c in new[] { '。', '（', '(' })
                {
                    int i = d.IndexOf(c);
                    if (i > 0 && i < cut) cut = i;
                }
                d = d.Substring(0, cut).Trim();
                if (d.Length > 18) d = d.Substring(0, 18) + "…";
                return string.IsNullOrEmpty(d) ? Key : d;
            }
        }

        /// <summary>欄を作れる型か。作れないものは「詳細」の文字列欄で書く。</summary>
        public bool HasField
        {
            get
            {
                if (Type == null || Attr == null) return false;
                if (Attr.IsMeshRef) return Type == typeof(int);
                return Type == typeof(int) || Type == typeof(float) || Type == typeof(double)
                    || Type == typeof(Vector2) || Type == typeof(Vector3)
                    || Type == typeof(bool) || Type == typeof(string) || Type.IsEnum;
            }
        }
    }

    public sealed class ParamFieldBuilder
    {
        /// <summary>スライダーを動かしている間の下見。null ならスライダーは離したときだけ送る。</summary>
        public Action<string, string> OnPreview;

        /// <summary>値の確定。</summary>
        public Action<string, string> OnCommit;

        private readonly UiDynamicControls _controls;

        /// <summary>キーごとの「値を欄へ書く」処理（UpdateValues が使う）。</summary>
        private readonly Dictionary<string, Action<string>> _setters = new Dictionary<string, Action<string>>(StringComparer.Ordinal);

        /// <summary>下見中のキー（スライダーを動かしている間）。</summary>
        private readonly HashSet<string> _previewing = new HashSet<string>(StringComparer.Ordinal);

        public ParamFieldBuilder(UiDynamicControls controls)
        {
            _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        }

        /// <summary>下見中のキーがあるか。</summary>
        public bool IsPreviewing => _previewing.Count > 0;

        /// <summary>欄の組を表す文字列。同じなら作り直さずに値だけ合わせてよい。</summary>
        public static string Signature(string owner, IList<ParamFieldSpec> specs)
        {
            var sb = new System.Text.StringBuilder(owner ?? "");
            if (specs != null)
                foreach (var s in specs)
                {
                    sb.Append('|').Append(s.Key).Append(':').Append(s.Type?.Name ?? "");
                    if (s.MeshChoices != null)
                        foreach (var c in s.MeshChoices) sb.Append(',').Append(c.Value).Append('=').Append(c.Key);
                }
            return sb.ToString();
        }

        /// <summary>container の中身を捨てて、specs の欄を作る。欄を作れない spec は飛ばす。</summary>
        public void Build(VisualElement container, IList<ParamFieldSpec> specs)
        {
            if (container == null) return;
            _controls.RemoveUnder(container);
            container.Clear();
            _setters.Clear();
            _previewing.Clear();
            if (specs == null) return;
            foreach (var s in specs)
                if (s != null && s.HasField) BuildOne(container, s);
            // 作り直した欄はレイアウト構築時の配色を受けていない（白地に白文字になる）。
            PlayerLayoutRoot.ApplyDarkTheme(container);
        }

        /// <summary>欄の値を specs の値へ合わせる（通知しない）。下見中のキーは触らない。</summary>
        public void UpdateValues(IList<ParamFieldSpec> specs)
        {
            if (specs == null) return;
            foreach (var s in specs)
            {
                if (s == null || _previewing.Contains(s.Key)) continue;
                if (_setters.TryGetValue(s.Key, out var set)) set(s.Value ?? "");
            }
        }

        // ================================================================
        // 欄 1 つ
        // ================================================================

        private void BuildOne(VisualElement container, ParamFieldSpec s)
        {
            var t = s.Type;
            string tip = string.IsNullOrEmpty(s.Attr?.Description) ? s.Key : $"{s.Key}：{s.Attr.Description}";

            if (s.Attr.IsMeshRef) { BuildMeshRef(container, s, tip); return; }
            if (t == typeof(bool))   { BuildBool(container, s, tip); return; }
            if (t == typeof(string)) { BuildString(container, s, tip); return; }
            if (t.IsEnum)            { BuildEnum(container, s, tip); return; }
            if (t == typeof(int))    { BuildInt(container, s, tip); return; }
            if (t == typeof(float) || t == typeof(double)) { BuildScalar(container, s, tip); return; }
            if (t == typeof(Vector2)) { BuildVector(container, s, tip, 2); return; }
            if (t == typeof(Vector3)) { BuildVector(container, s, tip, 3); return; }
        }

        // ── 範囲 ───────────────────────────────────────────────────

        private static bool TryRange(PLParamAttribute a, out float min, out float max)
        {
            min = 0f; max = 0f;
            if (a == null) return false;
            bool hasMin = false, hasMax = false;
            if (a.HasMin) { min = (float)a.Min; hasMin = true; }
            else if (a.HasLimitKey && Poly_Ling.Core.ParameterLimits.TryGetF(a.LimitKey + ".Min", out float lo)) { min = lo; hasMin = true; }
            if (a.HasMax) { max = (float)a.Max; hasMax = true; }
            else if (a.HasLimitKey && Poly_Ling.Core.ParameterLimits.TryGetF(a.LimitKey + ".Max", out float hi)) { max = hi; hasMax = true; }
            return hasMin && hasMax && max > min;
        }

        private static float Snap(PLParamAttribute a, float v)
        {
            if (a == null || !a.HasStep || a.Step <= 0) return v;
            double st = a.Step;
            return (float)(Math.Round(v / st) * st);
        }

        private static string Fmt(object v)
            => PanelCommandFactory.TryFormatValue(v, out string s) ? s : Convert.ToString(v, CultureInfo.InvariantCulture);

        private static bool TryParse(string raw, Type t, out object v)
        {
            v = null;
            if (string.IsNullOrEmpty(raw)) return false;
            return PanelCommandFactory.TryParseValue(raw, t, out v, out _);
        }

        // ── スライダーの下見と確定 ─────────────────────────────────

        /// <summary>
        /// スライダーの値が変わったとき（下見）と、離したとき・フォーカスを外したとき（確定）の配線。
        /// valueText は今の値の文字列（成分なら組み直した全体）。
        /// </summary>
        private void WireSlider<TV>(BaseField<TV> slider, string key, Func<string> valueText, Action onChange)
        {
            // 下見中かは _previewing で持つ。数値欄で確定したとき（CommitFromField）にも下ろすので、
            // 後から離したときに同じ値で 2 度目の確定を送らない。
            slider.RegisterValueChangedCallback(_ =>
            {
                onChange?.Invoke();
                _previewing.Add(key);
                OnPreview?.Invoke(key, valueText());
            });
            void Commit()
            {
                if (!_previewing.Remove(key)) return;
                OnCommit?.Invoke(key, valueText());
            }
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => Commit());
            slider.RegisterCallback<FocusOutEvent>(_ => Commit());
        }

        /// <summary>数値欄・選択などからの確定。同じキーの下見は終わったものとして下ろす。</summary>
        private void CommitFromField(string key, string value)
        {
            _previewing.Remove(key);
            OnCommit?.Invoke(key, value);
        }

        // ── 型ごと ─────────────────────────────────────────────────

        private void BuildScalar(VisualElement container, ParamFieldSpec s, string tip)
        {
            bool isDouble = s.Type == typeof(double);
            var field = new FloatField(s.Label) { isDelayed = true, tooltip = tip };
            Slider slider = null;
            bool ranged = TryRange(s.Attr, out float min, out float max);

            object Val(float f) => isDouble ? (object)(double)f : f;

            _controls.Add($"arg.{s.Key}", field, $"{s.Key}（数値）", UiSafety.SafeWrite);
            container.Add(field);
            if (ranged)
            {
                slider = new Slider(min, max) { tooltip = tip };
                slider.style.marginLeft = 4;
                _controls.Add($"arg.{s.Key}.slider", slider, $"{s.Key}（スライダー {min}〜{max}）", UiSafety.SafeWrite);
                container.Add(slider);
            }

            void Set(string raw)
            {
                if (!TryParse(raw, s.Type, out object o)) return;
                float f = Convert.ToSingle(o, CultureInfo.InvariantCulture);
                field.SetValueWithoutNotify(f);
                slider?.SetValueWithoutNotify(f);
            }
            Set(s.Value);
            _setters[s.Key] = Set;

            field.RegisterValueChangedCallback(e =>
            {
                slider?.SetValueWithoutNotify(e.newValue);
                CommitFromField(s.Key, Fmt(Val(e.newValue)));
            });
            if (slider != null)
                WireSlider(slider, s.Key,
                    () => Fmt(Val(Snap(s.Attr, slider.value))),
                    () => field.SetValueWithoutNotify(Snap(s.Attr, slider.value)));
        }

        private void BuildInt(VisualElement container, ParamFieldSpec s, string tip)
        {
            var field = new IntegerField(s.Label) { isDelayed = true, tooltip = tip };
            SliderInt slider = null;
            bool ranged = TryRange(s.Attr, out float min, out float max);

            _controls.Add($"arg.{s.Key}", field, $"{s.Key}（整数）", UiSafety.SafeWrite);
            container.Add(field);
            if (ranged)
            {
                slider = new SliderInt(Mathf.RoundToInt(min), Mathf.RoundToInt(max)) { tooltip = tip };
                slider.style.marginLeft = 4;
                _controls.Add($"arg.{s.Key}.slider", slider, $"{s.Key}（スライダー {min}〜{max}）", UiSafety.SafeWrite);
                container.Add(slider);
            }

            void Set(string raw)
            {
                if (!TryParse(raw, typeof(int), out object o)) return;
                int v = (int)o;
                field.SetValueWithoutNotify(v);
                slider?.SetValueWithoutNotify(v);
            }
            Set(s.Value);
            _setters[s.Key] = Set;

            field.RegisterValueChangedCallback(e =>
            {
                slider?.SetValueWithoutNotify(e.newValue);
                CommitFromField(s.Key, Fmt(e.newValue));
            });
            if (slider != null)
                WireSlider(slider, s.Key, () => Fmt(slider.value), () => field.SetValueWithoutNotify(slider.value));
        }

        private void BuildVector(VisualElement container, ParamFieldSpec s, string tip, int n)
        {
            string[] comps = { "x", "y", "z" };
            var fields  = new FloatField[n];
            var sliders = new Slider[n];
            bool ranged = TryRange(s.Attr, out float min, out float max);

            var title = new Label(s.Label) { tooltip = tip };
            title.style.fontSize = 10;
            container.Add(title);

            string Text()
            {
                if (n == 2) return Fmt(new Vector2(fields[0].value, fields[1].value));
                return Fmt(new Vector3(fields[0].value, fields[1].value, fields[2].value));
            }

            for (int i = 0; i < n; i++)
            {
                int ci = i;
                string id = $"arg.{s.Key}.{comps[i]}";
                fields[i] = new FloatField(comps[i].ToUpperInvariant()) { isDelayed = true, tooltip = tip };
                fields[i].style.marginLeft = 8;
                _controls.Add(id, fields[i], $"{s.Key} の {comps[i]}（数値）", UiSafety.SafeWrite);
                container.Add(fields[i]);
                if (ranged)
                {
                    sliders[i] = new Slider(min, max) { tooltip = tip };
                    sliders[i].style.marginLeft = 12;
                    _controls.Add(id + ".slider", sliders[i], $"{s.Key} の {comps[i]}（スライダー {min}〜{max}）", UiSafety.SafeWrite);
                    container.Add(sliders[i]);
                }

                fields[i].RegisterValueChangedCallback(e =>
                {
                    sliders[ci]?.SetValueWithoutNotify(e.newValue);
                    CommitFromField(s.Key, Text());
                });
                if (sliders[i] != null)
                    WireSlider(sliders[i], s.Key, Text,
                        () => fields[ci].SetValueWithoutNotify(Snap(s.Attr, sliders[ci].value)));
            }

            void Set(string raw)
            {
                if (!TryParse(raw, s.Type, out object o)) return;
                var v = n == 2 ? (Vector3)(Vector2)o : (Vector3)o;
                for (int i = 0; i < n; i++)
                {
                    fields[i].SetValueWithoutNotify(v[i]);
                    sliders[i]?.SetValueWithoutNotify(v[i]);
                }
            }
            Set(s.Value);
            _setters[s.Key] = Set;
        }

        private void BuildBool(VisualElement container, ParamFieldSpec s, string tip)
        {
            var toggle = new Toggle(s.Label) { tooltip = tip };
            _controls.Add($"arg.{s.Key}", toggle, $"{s.Key}（チェック）", UiSafety.SafeWrite);
            container.Add(toggle);
            void Set(string raw) { if (TryParse(raw, typeof(bool), out object o)) toggle.SetValueWithoutNotify((bool)o); }
            Set(s.Value);
            _setters[s.Key] = Set;
            toggle.RegisterValueChangedCallback(e => CommitFromField(s.Key, e.newValue ? "true" : "false"));
        }

        private void BuildString(VisualElement container, ParamFieldSpec s, string tip)
        {
            var field = new TextField(s.Label) { isDelayed = true, tooltip = tip };
            _controls.Add($"arg.{s.Key}", field, $"{s.Key}（文字列）", UiSafety.SafeWrite);
            container.Add(field);
            void Set(string raw) => field.SetValueWithoutNotify(raw ?? "");
            Set(s.Value);
            _setters[s.Key] = Set;
            field.RegisterValueChangedCallback(e => CommitFromField(s.Key, e.newValue ?? ""));
        }

        private void BuildEnum(VisualElement container, ParamFieldSpec s, string tip)
        {
            // 宣言の順に並べ、表記は InspectorName があればそれを使う（列挙の値は保存に使うので並びのために変えられない）。
            var fields = s.Type.GetFields(BindingFlags.Public | BindingFlags.Static);
            var names  = new string[fields.Length];
            var values = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                names[i]  = fields[i].GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? fields[i].Name;
                values[i] = fields[i].GetValue(null);
            }

            int IndexOf(string raw)
            {
                if (!TryParse(raw, s.Type, out object o)) return -1;
                for (int i = 0; i < values.Length; i++) if (Equals(values[i], o)) return i;
                return -1;
            }
            string TextOf(int i) => i >= 0 && i < values.Length ? Fmt(values[i]) : "";

            if (names.Length <= 6)
            {
                // 選択ボタン。値（uiSetValue）は選択肢の番号。
                var group = new RadioButtonGroup(s.Label, new List<string>(names)) { tooltip = tip };
                _controls.Add($"arg.{s.Key}", group, $"{s.Key}（選択。番号 0..{names.Length - 1}：{string.Join(" / ", names)}）", UiSafety.SafeWrite);
                container.Add(group);
                void Set(string raw) => group.SetValueWithoutNotify(IndexOf(raw));
                Set(s.Value);
                _setters[s.Key] = Set;
                group.RegisterValueChangedCallback(e => { if (e.newValue >= 0) CommitFromField(s.Key, TextOf(e.newValue)); });
            }
            else
            {
                var dd = new DropdownField(s.Label, new List<string>(names), -1) { tooltip = tip };
                _controls.Add($"arg.{s.Key}", dd, $"{s.Key}（選択）", UiSafety.SafeWrite);
                container.Add(dd);
                // 幅の狭いカードで選んだ文字が切れないよう、見出しを文字の幅にして残りを選択の欄へ回す。
                dd.labelElement.style.minWidth = 0;
                dd.labelElement.style.width = StyleKeyword.Auto;
                if (dd.childCount > 1)
                {
                    dd[1].style.flexGrow = 1;
                    dd[1].style.minWidth = 0;
                }
                void Set(string raw) { int i = IndexOf(raw); dd.SetValueWithoutNotify(i >= 0 ? names[i] : ""); }
                Set(s.Value);
                _setters[s.Key] = Set;
                dd.RegisterValueChangedCallback(_ => { if (dd.index >= 0) CommitFromField(s.Key, TextOf(dd.index)); });
            }
        }

        private void BuildMeshRef(VisualElement container, ParamFieldSpec s, string tip)
        {
            var choices = new List<string> { "（なし）" };
            var indices = new List<int> { -1 };
            if (s.MeshChoices != null)
                foreach (var c in s.MeshChoices) { choices.Add($"{c.Value}: {c.Key}"); indices.Add(c.Value); }

            var dd = new DropdownField(s.Label, choices, 0) { tooltip = tip };
            _controls.Add($"arg.{s.Key}", dd, $"{s.Key}（オブジェクト）", UiSafety.SafeWrite);
            container.Add(dd);
            void Set(string raw)
            {
                int idx = -1;
                if (TryParse(raw, typeof(int), out object o)) idx = (int)o;
                int pos = indices.IndexOf(idx);
                dd.SetValueWithoutNotify(choices[pos >= 0 ? pos : 0]);
            }
            Set(s.Value);
            _setters[s.Key] = Set;
            dd.RegisterValueChangedCallback(_ =>
            {
                int pos = dd.index;
                if (pos < 0) return;
                CommitFromField(s.Key, indices[pos].ToString(CultureInfo.InvariantCulture));
            });
        }
    }
}
