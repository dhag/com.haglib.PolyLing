// UiOperationCatalog.cs
// コマンドを人が画面で行うときの操作を、宣言（PLUiRouteAttribute.cs）と今の画面から読み出す。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【正本】PolyLing_チュートリアル計画.md 3.5・段 3
//
// 【文字を持たない】
//   見出しと選択肢の文字は、宣言した ID の要素からその時点の表示を読む（CaptionOf・ChoicesOf）。
//   まだ作られていない欄（図形を選ぶまで・項目を選ぶまで）は built=0 で返し、文字は空のまま返す。
//   想像で埋めない。読むには経路のとおりに uiReveal か uiClick で開く。
//
// 【見出しの読み方】
//   ボタンは文字、折り畳みは見出し、入力欄は label。label が無い欄（行の頭に Label を置く形）は、
//   同じ親の手前にある一番近い Label を読む。末尾の「:」は読み飛ばす。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Modifiers;

namespace Poly_Ling.Player
{
    public static class UiOperationCatalog
    {
        // ================================================================
        // 画面の文字
        // ================================================================

        /// <summary>ボタン・入力欄の見出し。読めなければ空。</summary>
        public static string CaptionOf(VisualElement e)
        {
            if (e == null) return "";
            if (e is Button b) return b.text ?? "";
            if (e is Foldout f) return f.text ?? "";

            var prop = e.GetType().GetProperty("label", BindingFlags.Instance | BindingFlags.Public);
            if (prop != null && prop.PropertyType == typeof(string)
                && prop.GetValue(e) is string label && label.Length > 0)
                return Trim(label);

            // 折り畳みの見出しの行は Toggle で、文字は text に入る。
            if (e is Toggle t && !string.IsNullOrEmpty(t.text)) return t.text;

            if (e is Label || e is HelpBox) return "";
            return NearbyLabel(e);
        }

        /// <summary>同じ親の手前にある一番近い Label。</summary>
        private static string NearbyLabel(VisualElement e)
        {
            var parent = e.parent;
            if (parent == null) return "";
            for (int i = parent.IndexOf(e) - 1; i >= 0; i--)
                if (parent[i] is Label l && !string.IsNullOrEmpty(l.text)) return Trim(l.text);
            return "";
        }

        private static string Trim(string s) => (s ?? "").Trim().TrimEnd(':', '：');

        /// <summary>選択式の欄の選択肢。選択式でなければ null。</summary>
        private static List<string> ChoicesOf(VisualElement e)
        {
            if (e is DropdownField d) return d.choices;
            if (e is RadioButtonGroup rg && rg.choices != null) return new List<string>(rg.choices);
            return null;
        }

        // ================================================================
        // 宣言の読み出し
        // ================================================================

        /// <summary>入力欄 1 つの操作。</summary>
        public sealed class ArgOp
        {
            public string Arg = "", Control = "", Label = "", Raw = "", UiValue = "", ValueLabel = "", Current = "", Problem = "";
            public bool Built;
        }

        // ================================================================
        // チュートリアルの差し込み（PolyLing_チュートリアル計画.md 3.3・段 5）
        // ================================================================

        /// <summary>宣言した引数のうち、args にあるものの操作。宣言の順。</summary>
        public static List<ArgOp> ReadArgs(UiAutomationRegistry registry, Type t, IReadOnlyDictionary<string, string> args)
        {
            var ops = new List<ArgOp>();
            foreach (var a in t.GetCustomAttributes<PLUiArgAttribute>(inherit: false))
                if (args.TryGetValue(a.Arg, out string raw))
                {
                    var op = MakeOp(registry, t, a.Arg, a.Control, a.Values, a.ValueKind, raw ?? "");
                    if (op.Built && op.Label.Length == 0) op.Label = a.Unlabeled ?? "";
                    ops.Add(op);
                }
            return ops;
        }

        /// <summary>引数に当たらないが決まった値にする欄の操作（PLUiFixed）。</summary>
        public static List<ArgOp> ReadFixed(UiAutomationRegistry registry, Type t)
        {
            var ops = new List<ArgOp>();
            foreach (var f in t.GetCustomAttributes<PLUiFixedAttribute>(inherit: false))
            {
                var op = new ArgOp { Control = f.Control, Raw = f.Value, UiValue = f.Value };
                ops.Add(op);

                if (!registry.TryGetControl(f.Control, out var c) || c.Element == null)
                {
                    op.Problem = "まだ作られていない（経路のとおりに開くと作られる）";
                    continue;
                }

                var e = c.Element;
                op.Built = true;
                op.Label = CaptionOf(e);
                if (c.Getter != null) op.Current = c.Getter() ?? "";
                else if (UiAutomationService.TryReadValue(e, out string cur)) op.Current = cur;

                var choices = ChoicesOf(e);
                if (choices != null)
                {
                    if (!int.TryParse(f.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                        || i < 0 || i >= choices.Count)
                    {
                        op.Problem = $"宣言の番号が選択肢に無い: {f.Value}";
                        continue;
                    }
                    op.ValueLabel = choices[i];
                    if (!(e is RadioButtonGroup)) op.UiValue = choices[i];
                }
                else op.ValueLabel = e is Toggle ? (IsTrue(f.Value) ? "オン" : "オフ") : f.Value;
            }
            return ops;
        }

        /// <summary>可変の引数（PLUiArgList）の操作。欄は実行のあとで作られるので、そのあとで呼ぶ。</summary>
        public static List<ArgOp> ReadListArgs(UiAutomationRegistry registry, Type t, IReadOnlyDictionary<string, string> args)
        {
            var ops  = new List<ArgOp>();
            var list = t.GetCustomAttribute<PLUiArgListAttribute>(inherit: false);
            if (list == null || !args.TryGetValue(list.KeysArg, out string keysText)) return ops;

            args.TryGetValue(list.ValuesArg, out string valuesText);
            var keys   = SplitList(keysText);
            var values = SplitList(valuesText);
            for (int i = 0; i < keys.Count; i++)
            {
                string id = string.Format(CultureInfo.InvariantCulture, list.ControlFormat, keys[i]);
                bool useFallback = registry.StatusOf(id) == UiAutomationRegistry.IdStatus.Unknown
                                   && !string.IsNullOrEmpty(list.Fallback);
                if (useFallback) id = list.Fallback;

                var op = MakeOp(registry, t, keys[i], id, "", PLUiValueKind.Auto, i < values.Count ? values[i] : "");
                // 文字列の欄は、画面の「キー」の選択肢がそのまま見出しになる。
                if (useFallback && op.Built) op.Label = keys[i];
                ops.Add(op);
            }
            return ops;
        }

        /// <summary>
        /// 可変の引数の初期値。処理列の種類の既定のインスタンスから読む。読めなければ null。
        /// 種類は ValueKind=ModifierKind の引数の値で決まる。
        /// </summary>
        public static string ListArgDefault(Type t, IReadOnlyDictionary<string, string> args, string key)
        {
            foreach (var a in t.GetCustomAttributes<PLUiArgAttribute>(inherit: false))
            {
                if (a.ValueKind != PLUiValueKind.ModifierKind || !args.TryGetValue(a.Arg, out string kind)) continue;
                var info = MeshModifierRegistry.Find(kind);
                var inst = MeshModifierRegistry.Create(kind);
                if (info == null || inst == null) return null;
                foreach (var p in info.Params)
                    if (p.Key == key && PanelCommandFactory.TryFormatValue(p.Property.GetValue(inst), out string s)) return s;
                return null;
            }
            return null;
        }

        /// <summary>同じ値か。数は数として、真偽は真偽として比べる（欄と引数で書き方が違う）。</summary>
        public static bool SameValue(string a, string b)
        {
            a ??= "";
            b ??= "";
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (bool.TryParse(a, out bool ba) && bool.TryParse(b, out bool bb)) return ba == bb;
            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double da)
                && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double db))
                return Math.Abs(da - db) <= 1e-5 * Math.Max(1.0, Math.Max(Math.Abs(da), Math.Abs(db)));
            return false;
        }

        /// <summary>
        /// 経路の折り畳みとボタンを 「a」→「b」 の文字にする。入力欄と一覧は引数の側で書くので入れない。
        /// 読めない文字があれば、空のまま書かずに理由を返す。
        /// </summary>
        public static bool TryRouteText(UiAutomationRegistry registry, string[] items, string skip, out string text, out string problem)
        {
            text    = "";
            problem = null;
            var parts = new List<string>();
            foreach (var id in items ?? new string[0])
            {
                if (id == skip) continue;
                if (!registry.TryGetControl(id, out var c) || c.Element == null)
                {
                    problem = $"{id} がまだ作られていません";
                    return false;
                }
                var e = c.Element;
                if (!(e is Button || e is Foldout || e is Toggle)) continue;

                string label = CaptionOf(e);
                if (string.IsNullOrEmpty(label))
                {
                    problem = $"{id} の文字を読めません";
                    return false;
                }
                parts.Add($"「{label}」");
            }
            if (parts.Count == 0)
            {
                problem = "経路に折り畳みもボタンもありません";
                return false;
            }
            text = string.Join("→", parts);
            return true;
        }

        /// <summary>ボタンの文字を 「a」 の形で読む。読めなければ理由を返す。</summary>
        public static bool TryButtonText(UiAutomationRegistry registry, string id, out string text, out string problem)
        {
            text    = "";
            problem = null;
            if (!registry.TryGetControl(id, out var c) || c.Element == null)
            {
                problem = $"{id} がまだ作られていません";
                return false;
            }
            string label = CaptionOf(c.Element);
            if (string.IsNullOrEmpty(label))
            {
                problem = $"{id} の文字を読めません";
                return false;
            }
            text = $"「{label}」";
            return true;
        }

        /// <summary>
        /// コマンドの画面の操作を返す。argKeys を渡すとその引数だけを、省くと宣言した引数の全部を返す。
        /// 経路が複数あるコマンドは既定の経路（UiRouteCatalog の先頭）を返す。
        /// </summary>
        public static CommandResult Describe(UiAutomationRegistry registry, string commandName, string[] argKeys, string[] argValues)
        {
            var t = PanelCommandFactory.ResolveType(commandName);
            if (t == null) return CommandResult.Fail($"unknown command: {commandName}");

            var given = new Dictionary<string, string>(StringComparer.Ordinal);
            if (argKeys != null)
                for (int i = 0; i < argKeys.Length; i++)
                    given[argKeys[i]] = argValues != null && i < argValues.Length ? argValues[i] ?? "" : "";

            var b = CommandDataJson.New().Text("command", commandName);

            // ── 経路と実行のボタン ───────────────────────────────
            var routes = UiRouteCatalog.RoutesOf(t);
            if (routes.Count > 0)
            {
                var r = routes[0];
                b.Text("routeName", r.Name).Text("routeNote", r.Note)
                 .Texts("routeItems", new List<string>(r.Items))
                 .Texts("routeLabels", LabelsOf(registry, r.Items));
                b.Text("runControl", r.Run).Text("runLabel", LabelOf(registry, r.Run));
            }

            // ── 置き換え ─────────────────────────────────────────
            var manual = t.GetCustomAttribute<PLUiManualAttribute>(inherit: false);
            if (manual != null)
                b.Text("manualName", manual.Name).Text("manualNote", manual.Note)
                 .Texts("manualItems", new List<string>(manual.Items))
                 .Texts("manualLabels", LabelsOf(registry, manual.Items));

            // ── 入力欄 ───────────────────────────────────────────
            var ops = new List<ArgOp>();
            foreach (var a in t.GetCustomAttributes<PLUiArgAttribute>(inherit: false))
            {
                bool has = given.TryGetValue(a.Arg, out string raw);
                if (given.Count > 0 && !has) continue;
                ops.Add(MakeOp(registry, t, a.Arg, a.Control, a.Values, a.ValueKind, raw ?? ""));
            }

            var list = t.GetCustomAttribute<PLUiArgListAttribute>(inherit: false);
            if (list != null && given.TryGetValue(list.KeysArg, out string keysText))
            {
                given.TryGetValue(list.ValuesArg, out string valuesText);
                var keys   = SplitList(keysText);
                var values = SplitList(valuesText);
                for (int i = 0; i < keys.Count; i++)
                {
                    string id = string.Format(CultureInfo.InvariantCulture, list.ControlFormat, keys[i]);
                    if (registry.StatusOf(id) == UiAutomationRegistry.IdStatus.Unknown
                        && !string.IsNullOrEmpty(list.Fallback)) id = list.Fallback;
                    ops.Add(MakeOp(registry, t, keys[i], id, "", PLUiValueKind.Auto, i < values.Count ? values[i] : ""));
                }
            }

            b.Texts("argNames",       Column(ops, o => o.Arg))
             .Texts("argControls",    Column(ops, o => o.Control))
             .Texts("argLabels",      Column(ops, o => o.Label))
             .Ints ("argBuilt",       BuiltColumn(ops))
             .Texts("argValues",      Column(ops, o => o.Raw))
             .Texts("argUiValues",    Column(ops, o => o.UiValue))
             .Texts("argValueLabels", Column(ops, o => o.ValueLabel))
             .Texts("argCurrent",     Column(ops, o => o.Current))
             .Texts("argProblems",    Column(ops, o => o.Problem));

            // ── 画面に欄が無い引数 ───────────────────────────────
            var noArgs = new List<string>();
            var noWhy  = new List<string>();
            foreach (var n in t.GetCustomAttributes<PLUiNoFieldAttribute>(inherit: false))
                foreach (var k in n.Args)
                {
                    if (given.Count > 0 && !given.ContainsKey(k)) continue;
                    noArgs.Add(k);
                    noWhy.Add(n.Reason);
                }
            b.Texts("noFieldArgs", noArgs).Texts("noFieldReasons", noWhy);

            // ── 決まった値にする欄 ───────────────────────────────
            var fixedOps = ReadFixed(registry, t);
            b.Texts("fixedControls",    Column(fixedOps, o => o.Control))
             .Texts("fixedLabels",      Column(fixedOps, o => o.Label))
             .Texts("fixedUiValues",    Column(fixedOps, o => o.UiValue))
             .Texts("fixedValueLabels", Column(fixedOps, o => o.ValueLabel));

            return CommandResult.Ok(data: b.Build());
        }

        private static ArgOp MakeOp(UiAutomationRegistry registry, Type t, string arg, string control,
                                    string values, PLUiValueKind kind, string raw)
        {
            var op = new ArgOp { Arg = arg, Control = control, Raw = raw };

            if (!registry.TryGetControl(control, out var c) || c.Element == null)
            {
                op.Problem = "まだ作られていない（経路のとおりに開くと作られる）";
                return op;
            }

            var e = c.Element;
            op.Built = true;
            op.Label = CaptionOf(e);
            if (c.Getter != null) op.Current = c.Getter() ?? "";
            else if (UiAutomationService.TryReadValue(e, out string cur)) op.Current = cur;
            if (string.IsNullOrEmpty(raw)) return op;

            PanelCommandFactory.TryGetArgParam(t, arg, out Type argType, out PLParamAttribute attr);
            if (kind == PLUiValueKind.Auto && string.IsNullOrEmpty(values) && attr != null && attr.IsMeshRef)
                kind = PLUiValueKind.Object;

            var choices = ChoicesOf(e);
            int index = ChoiceIndex(raw, values, kind, argType, choices, out string problem);
            op.Problem = problem ?? "";

            if (choices == null)
            {
                op.UiValue    = raw;
                op.ValueLabel = e is Toggle ? (IsTrue(raw) ? "オン" : "オフ") : raw;
                return op;
            }

            if (index < 0 || index >= choices.Count)
            {
                if (string.IsNullOrEmpty(op.Problem)) op.Problem = $"選択肢に当たらない: {raw}";
                return op;
            }

            op.ValueLabel = choices[index];
            // 選択ボタンは番号、ドロップダウンは文字で値を入れる（UiAutomationService.WriteValue）。
            op.UiValue = e is RadioButtonGroup ? index.ToString(CultureInfo.InvariantCulture) : choices[index];
            return op;
        }

        /// <summary>値が何番目の選択肢か。当たらなければ -1。</summary>
        private static int ChoiceIndex(string raw, string values, PLUiValueKind kind, Type argType,
                                       List<string> choices, out string problem)
        {
            problem = null;

            if (!string.IsNullOrEmpty(values))
            {
                string name = raw;
                if (argType != null && argType.IsEnum
                    && PanelCommandFactory.TryParseValue(raw, argType, out object v, out _))
                    name = v.ToString();
                int i = Array.IndexOf(values.Split(','), name);
                if (i < 0) problem = $"宣言の Values に無い値: {raw}";
                return i;
            }

            if (kind == PLUiValueKind.ModifierKind)
            {
                int n = 0;
                foreach (var info in MeshModifierRegistry.All)
                {
                    if (info.Kind == raw) return n;
                    n++;
                }
                problem = $"処理の種類に無い: {raw}";
                return -1;
            }

            if (choices == null) return -1;

            if (kind == PLUiValueKind.Object)
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                {
                    problem = $"索引ではない: {raw}";
                    return -1;
                }
                if (idx < 0) return 0;
                string h1 = $"[{idx}] ", h2 = $"{idx}: ";
                for (int i = 0; i < choices.Count; i++)
                    if (choices[i].StartsWith(h1, StringComparison.Ordinal)
                        || choices[i].StartsWith(h2, StringComparison.Ordinal)) return i;
                problem = $"この索引のオブジェクトが選択肢に無い: {raw}";
                return -1;
            }

            // 列挙型の欄（処理列の種類の引数）は選択肢が列挙の名前。
            int exact = choices.IndexOf(raw);
            if (exact >= 0) return exact;
            if (argType != null && argType.IsEnum
                && PanelCommandFactory.TryParseValue(raw, argType, out object ev, out _))
                return choices.IndexOf(ev.ToString());
            return -1;
        }

        private static bool IsTrue(string raw) => bool.TryParse(raw, out bool v) && v;

        private static string LabelOf(UiAutomationRegistry registry, string id)
            => !string.IsNullOrEmpty(id) && registry.TryGetControl(id, out var c) ? CaptionOf(c.Element) : "";

        private static List<string> LabelsOf(UiAutomationRegistry registry, string[] ids)
        {
            var list = new List<string>();
            if (ids != null) foreach (var id in ids) list.Add(LabelOf(registry, id));
            return list;
        }

        private static List<string> Column(List<ArgOp> ops, Func<ArgOp, string> get)
        {
            var list = new List<string>(ops.Count);
            foreach (var o in ops) list.Add(get(o));
            return list;
        }

        private static List<int> BuiltColumn(List<ArgOp> ops)
        {
            var list = new List<int>(ops.Count);
            foreach (var o in ops) list.Add(o.Built ? 1 : 0);
            return list;
        }

        /// <summary>配列の引数の値を割る。カンマを含む値は二重引用符で包まれる（ModifierChain のコマンドの決まり）。</summary>
        private static List<string> SplitList(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            if (PanelCommandFactory.TryParseValue(text, typeof(string[]), out object o, out _) && o is string[] a)
                list.AddRange(a);
            else
                list.Add(text);
            return list;
        }
    }
}
