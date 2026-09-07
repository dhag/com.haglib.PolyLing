// PanelCommandFactoryAudit.cs
// PanelCommandFactory が往復するかを検査する。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何を確かめるか】
//   1. 往復 … コマンド 1 件を ToArgs で文字列にし、Create で戻し、
//             PanelCommandDump.Describe の出力が一致するか。
//             書く側と読む側の区切り・桁・列挙の扱いがずれていれば落ちる。
//   2. 網羅 … 全コマンド型について action 名が一意に引けるか、
//             コンストラクタ引数がすべてプロパティへ辿れるか。
//             コマンドを 1 本足したときの付け忘れをここで拾う。
//
// 【なぜ実インスタンスを要求するか】
//   引数の意味を知らずに値を作ると、コンストラクタが弾く組み合わせ
//   （長さの揃わない配列など）を踏む。往復検査は呼び出し側が
//   意味のあるインスタンスを渡す形にして、生成は行わない。
//
// 【使い方】
//   var r = PanelCommandFactoryAudit.RunStructure();
//   Debug.Log(r.ToString());
//
//   var rt = PanelCommandFactoryAudit.RoundTrip(new ToggleVisibilityCommand(0, 3));
//   Debug.Log(rt.ToString());

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Poly_Ling.Data
{
    /// <summary>PanelCommandFactory の往復と網羅を検査する。</summary>
    public static class PanelCommandFactoryAudit
    {
        // ================================================================
        // 往復検査
        // ================================================================

        /// <summary>往復 1 件の結果。</summary>
        public sealed class RoundTripResult
        {
            public string CommandName { get; internal set; } = "";
            public string Action      { get; internal set; } = "";
            public bool   Ok          { get; internal set; }
            public string Reason      { get; internal set; } = "";
            public string Before      { get; internal set; } = "";
            public string After       { get; internal set; } = "";

            /// <summary>送った文字列パラメータ。ずれたときの手掛かりに残す。</summary>
            public Dictionary<string, string> Args { get; internal set; }
                = new Dictionary<string, string>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append("[往復] ").Append(CommandName)
                  .Append(" action=").Append(Action)
                  .Append(Ok ? " → 一致" : " → 不一致");
                if (!Ok && !string.IsNullOrEmpty(Reason))
                    sb.Append('\n').Append("  理由: ").Append(Reason);

                if (Args.Count > 0)
                {
                    sb.Append('\n').Append("  送った値:");
                    foreach (var kv in Args)
                        sb.Append('\n').Append("    ").Append(kv.Key).Append(" = ").Append(kv.Value);
                }
                if (!Ok)
                {
                    sb.Append('\n').Append("  元: ").Append(Before);
                    sb.Append('\n').Append("  後: ").Append(After);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// コマンド 1 件を文字列へ落として組み直し、内容が一致するか調べる。
        ///
        /// 一般化器が扱えない型を持つコマンド（ApplyBlendCommand の
        /// BlendSourceSpec[] など）は、ここでは必ず不一致になる。
        /// それらは RemoteServerCore.BuildPanelCommand に手書きの case があるので、
        /// 往復検査の対象に入れないこと。どのコマンドが該当するかは
        /// RunStructure の UnsupportedTypes に出る。
        /// </summary>
        public static RoundTripResult RoundTrip(PanelCommand cmd)
        {
            var r = new RoundTripResult();
            if (cmd == null) { r.Reason = "コマンドが null"; return r; }

            Type t = cmd.GetType();
            r.CommandName = t.Name;
            r.Action      = PanelCommandFactory.ActionOf(t);

            r.Args   = PanelCommandFactory.ToArgs(cmd);
            r.Before = PanelCommandDump.Describe(cmd);

            var rebuilt = PanelCommandFactory.Create(r.Action, cmd.ModelIndex, r.Args, out string error);
            if (rebuilt == null) { r.Reason = error ?? "組み立てに失敗"; return r; }

            if (rebuilt.GetType() != t)
            {
                r.Reason = $"型が変わった: {t.Name} → {rebuilt.GetType().Name}";
                r.After  = PanelCommandDump.Describe(rebuilt);
                return r;
            }

            r.After = PanelCommandDump.Describe(rebuilt);
            r.Ok    = string.Equals(r.Before, r.After, StringComparison.Ordinal);
            if (!r.Ok) r.Reason = "Describe の出力が一致しない";
            return r;
        }

        /// <summary>複数件をまとめて往復させ、不一致だけを並べる。</summary>
        public static string RoundTrip(IEnumerable<PanelCommand> commands)
        {
            var sb = new StringBuilder();
            int total = 0, ng = 0;

            if (commands != null)
            {
                foreach (var c in commands)
                {
                    total++;
                    var r = RoundTrip(c);
                    if (r.Ok) continue;
                    ng++;
                    sb.Append('\n').Append(r);
                }
            }

            var head = new StringBuilder();
            head.Append("[PanelCommandFactoryAudit] 往復 ").Append(total)
                .Append(" 件 / 不一致 ").Append(ng);
            return head.Append(sb).ToString();
        }

        // ================================================================
        // 網羅検査
        // ================================================================

        /// <summary>網羅検査で拾った 1 件。</summary>
        public sealed class Entry
        {
            public string CommandName { get; }
            public string Detail      { get; }

            public Entry(string commandName, string detail)
            {
                CommandName = commandName;
                Detail      = detail;
            }

            public override string ToString() => $"{CommandName}: {Detail}";
        }

        /// <summary>網羅検査の結果。</summary>
        public sealed class StructureReport
        {
            /// <summary>検査したコマンドの数。</summary>
            public int CommandCount { get; internal set; }

            /// <summary>action 名が他の型と衝突したもの。</summary>
            public List<Entry> ActionCollisions { get; } = new List<Entry>();

            /// <summary>コンストラクタ引数から辿れないものがあったコマンド。</summary>
            public List<Entry> UnresolvedParams { get; } = new List<Entry>();

            /// <summary>PLParam が付いていない引数があったコマンド。</summary>
            public List<Entry> MissingPLParam { get; } = new List<Entry>();

            /// <summary>Create / ToArgs が扱えない型の引数があったコマンド。</summary>
            public List<Entry> UnsupportedTypes { get; } = new List<Entry>();

            public bool IsClean =>
                ActionCollisions.Count == 0 && UnresolvedParams.Count == 0 &&
                MissingPLParam.Count   == 0 && UnsupportedTypes.Count == 0;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append("[PanelCommandFactoryAudit] コマンド ").Append(CommandCount)
                  .Append(" / action 衝突 ").Append(ActionCollisions.Count)
                  .Append(" / 引数の対応なし ").Append(UnresolvedParams.Count)
                  .Append(" / PLParam 付け忘れ ").Append(MissingPLParam.Count)
                  .Append(" / 未対応の型 ").Append(UnsupportedTypes.Count);

                Section(sb, "action 衝突", ActionCollisions);
                Section(sb, "引数に対応するプロパティが無い", UnresolvedParams);
                Section(sb, "引数のプロパティに PLParam が無い", MissingPLParam);
                Section(sb, "Create が扱えない型（手書きの case が要る）", UnsupportedTypes);
                return sb.ToString();
            }

            private static void Section(StringBuilder sb, string title, List<Entry> list)
            {
                if (list.Count == 0) return;
                sb.Append('\n').Append("── ").Append(title).Append(" ──");
                foreach (var e in list) sb.Append('\n').Append("  ").Append(e);
            }
        }

        /// <summary>
        /// 検査をまとめて 1 回で回す。コマンドを足したあとはこれを走らせる。
        ///
        /// 【何を見るか】
        ///   1. PLParamAudit.Run        PLParam の付け忘れ
        ///   2. RunStructure            action 衝突・引数の対応なし・未対応の型
        ///   3. スキーマ生成            道具として出せた数と出せなかった数
        ///
        /// 往復検査（RoundTrip）は実物のコマンドを渡す必要があるのでここには含めない。
        /// 呼び出し側が用意した見本を別途渡すこと。
        /// </summary>
        public static string RunAll()
        {
            var sb = new StringBuilder();

            sb.Append(PLParamAudit.Run().ToString());
            sb.Append('\n');
            sb.Append(RunStructure().ToString());

            PanelCommandFactory.CountTools(out int usable, out int skipped);
            sb.Append('\n')
              .Append("[PanelCommandSchema] 道具として出せた ").Append(usable)
              .Append(" / 出せなかった ").Append(skipped);

            // 説明が無い道具は MCP のクライアントが選べない。件数と名前を出す。
            var noDesc = new List<string>();
            foreach (var t in PLParamAudit.FindCommandTypes())
            {
                if (!PanelCommandFactory.TryBuildToolJson(t, out _, out _)) continue;
                var a = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
                if (a == null || string.IsNullOrEmpty(a.Description)) noDesc.Add(t.Name);
            }
            sb.Append('\n')
              .Append("[PLCommand] 説明が無い道具 ").Append(noDesc.Count);
            if (noDesc.Count > 0)
            {
                sb.Append('\n').Append("── PLCommand が無い / 説明が空 ──");
                foreach (var n in noDesc) sb.Append('\n').Append("  ").Append(n);
            }

            if (skipped > 0)
            {
                sb.Append('\n').Append("── スキーマに出せないコマンド ──");
                foreach (var t in PLParamAudit.FindCommandTypes())
                {
                    if (PanelCommandFactory.TryBuildToolJson(t, out _, out string why)) continue;
                    sb.Append('\n').Append("  ").Append(why);
                }
            }

            return sb.ToString();
        }

        /// <summary>全コマンド型について、一般化器で扱えるかを静的に調べる。</summary>
        public static StructureReport RunStructure()
        {
            var report = new StructureReport();
            var seen   = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in PLParamAudit.FindCommandTypes())
            {
                report.CommandCount++;

                string action = PanelCommandFactory.ActionOf(t);
                if (seen.TryGetValue(action, out var other))
                    report.ActionCollisions.Add(new Entry(t.Name, $"action \"{action}\" が {other.Name} と重なる"));
                else
                    seen[action] = t;

                ConstructorInfo ctor = null;
                foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                    if (ctor == null || c.GetParameters().Length > ctor.GetParameters().Length) ctor = c;

                if (ctor == null)
                {
                    report.UnresolvedParams.Add(new Entry(t.Name, "public なコンストラクタが無い"));
                    continue;
                }

                var ps = ctor.GetParameters();
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (i == 0 && p.ParameterType == typeof(int) && IsModelIndexName(p.Name)) continue;

                    PropertyInfo prop = null;
                    foreach (var q in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (string.Equals(q.Name, p.Name, StringComparison.OrdinalIgnoreCase)) { prop = q; break; }

                    if (prop == null)
                    {
                        if (!p.HasDefaultValue && !p.IsOptional)
                            report.UnresolvedParams.Add(new Entry(t.Name, $"引数 {p.Name} に対応するプロパティが無い"));
                        continue;
                    }

                    var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                    if (attr == null)
                    {
                        report.MissingPLParam.Add(new Entry(t.Name, $"{prop.Name} に PLParam が無い"));
                        continue;
                    }
                    if (attr.Ignore) continue;

                    if (!IsSupported(p.ParameterType))
                        report.UnsupportedTypes.Add(
                            new Entry(t.Name, $"{prop.Name} の型 {TypeName(p.ParameterType)} は Create が扱えない"));
                }
            }
            return report;
        }

        private static bool IsModelIndexName(string n)
            => n != null &&
               (n.Equals("modelIndex",       StringComparison.OrdinalIgnoreCase) ||
                n.Equals("sourceModelIndex", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("baseModelIndex",   StringComparison.OrdinalIgnoreCase) ||
                n.Equals("targetModelIndex", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// PanelCommandFactory.TryParse が扱える型か。
        ///
        /// 【対応表は 3 か所ある。必ず一緒に直すこと】
        ///   ・PanelCommandFactory.TryParse   文字列 → 値
        ///   ・PanelCommandFactory.TryFormat  値 → 文字列
        ///   ・PanelCommandFactory.TryJsonType  JSON Schema の型名
        ///   ここはその判定を検査側から見たもの。Vector2 / Vector3 は
        ///   TryParse へ追加済みだったのにここへ入れ忘れており、
        ///   使える型を「未対応」と報告していた。
        /// </summary>
        private static bool IsSupported(Type t)
        {
            // 判定は PanelCommandFactory 側に 1 つだけ置く。
            // ここに書き写すと 4 つ目の対応表になり、必ずずれる
            // （実際 Vector2 / Vector3 を入れ忘れて誤検出していた）。
            return PanelCommandFactory.IsSchemaRepresentable(t);
        }

        private static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            return t.Name;
        }
    }
}
