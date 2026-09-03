// PanelCommandFactory.cs
// action 名と文字列パラメータから PanelCommand を組み立てる。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   コマンドを 1 本足すたびに PanelCommand.cs / PlayerCommandDispatcher.cs /
//   RemoteServerCore.BuildPanelCommand / PanelCommandRouter.cs の 4 箇所を触っていた。
//   組み立ての規則はどのコマンドでも同じなので、PLParam が全コマンドに付いた今は
//   リフレクションで 1 箇所にまとめられる。
//
// 【action 名の規則】
//   型名から末尾の "Command" を落とし、先頭を小文字にしたもの。
//     ToggleVisibilityCommand → "toggleVisibility"
//   規則から外れるものだけ ActionAliases に載せる。
//
// 【パラメータ名の規則】
//   プロパティ名の先頭を小文字にしたもの。
//     MasterIndices → "masterIndices"
//   既に別のキーで運用してしまったものだけ ParamAliases に載せる。
//   別名は「型名.プロパティ名」で引くので、同名プロパティの巻き添えが起きない。
//
// 【封筒の値】
//   modelIndex は封筒側にあり、第 1 引数が modelIndex / sourceModelIndex /
//   baseModelIndex のときだけそこへ渡す。targetModelIndex は含めない
//   （SwitchModelCommand と TransferVertexDataCommand では実パラメータのため）。
//
//   objectIds は所有権ゲート（RemoteOwnership）が別途読む。ObjectIds という
//   プロパティを持つのは SetObjectEditorCommand だけなので、他のコマンドでは
//   同名のキーが届いても対応するプロパティが無く、自然に読み飛ばされる。
//
// 【読まないもの】
//   PLParam(Ignore = true) が付いたプロパティは外から与えられない。
//   属性そのものが無いプロパティも受け付けない（付け忘れを通さないため）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Poly_Ling.Data
{
    /// <summary>action 名と文字列パラメータから PanelCommand を作る。</summary>
    public static class PanelCommandFactory
    {
        // ================================================================
        // 別名表
        // ================================================================

        /// <summary>規則から外れる action 名。action → 型名。</summary>
        private static readonly Dictionary<string, string> ActionAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["undo"] = "PerformUndoCommand",
            ["redo"] = "PerformRedoCommand",
        };

        /// <summary>
        /// 規則から外れるパラメータ名。"型名.プロパティ名" → 実際に届くキー。
        /// 既存クライアント（PanelCommandRouter / RemoteHtmlClient）が
        /// 送っているキーをそのまま受けるために要る。
        /// </summary>
        private static readonly Dictionary<string, string> ParamAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RenameMeshCommand.NewName"]              = "name",
            ["RenameMeshesCommand.NewNames"]           = "names",
            ["RenameModelCommand.NewName"]             = "name",
            ["ApplyBlendCommand.RecalculateNormals"]   = "recalcNormals",
            ["ApplyBlendCommand.SelectedVerticesOnly"] = "selectedOnly",
        };

        // ================================================================
        // action → 型 の索引
        // ================================================================

        private static Dictionary<string, Type> _byAction;
        private static readonly object _lock = new object();

        /// <summary>action 名から型を引く。無ければ null。</summary>
        public static Type ResolveType(string action)
        {
            if (string.IsNullOrEmpty(action)) return null;
            EnsureIndex();
            return _byAction.TryGetValue(action, out var t) ? t : null;
        }

        /// <summary>型名から action 名を作る。規則から外れるものは別名を返す。</summary>
        public static string ActionOf(Type t)
        {
            if (t == null) return "";
            foreach (var kv in ActionAliases)
                if (string.Equals(kv.Value, t.Name, StringComparison.Ordinal)) return kv.Key;
            return Camel(StripCommandSuffix(t.Name));
        }

        private static void EnsureIndex()
        {
            if (_byAction != null) return;
            lock (_lock)
            {
                if (_byAction != null) return;

                var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in PLParamAudit.FindCommandTypes())
                {
                    string action = Camel(StripCommandSuffix(t.Name));
                    if (!map.ContainsKey(action)) map[action] = t;
                }
                foreach (var kv in ActionAliases)
                {
                    Type t = null;
                    foreach (var c in PLParamAudit.FindCommandTypes())
                        if (string.Equals(c.Name, kv.Value, StringComparison.Ordinal)) { t = c; break; }
                    if (t != null) map[kv.Key] = t;
                }
                _byAction = map;
            }
        }

        private static string StripCommandSuffix(string name)
            => name.EndsWith("Command", StringComparison.Ordinal)
               ? name.Substring(0, name.Length - "Command".Length)
               : name;

        private static string Camel(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ================================================================
        // 組み立て
        // ================================================================

        /// <summary>
        /// action と文字列パラメータからコマンドを作る。
        /// 作れないときは null を返し、error に理由を入れる。
        /// </summary>
        public static PanelCommand Create(
            string action, int modelIndex,
            IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            Type t = ResolveType(action);
            if (t == null) { error = $"Unknown action: {action}"; return null; }

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) { error = $"{t.Name}: public なコンストラクタが無い"; return null; }

            var ps = ctor.GetParameters();
            var values = new object[ps.Length];

            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];

                // 第 1 引数の modelIndex は封筒側の値をそのまま渡す。
                if (i == 0 && IsModelIndexParam(p)) { values[i] = modelIndex; continue; }

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null)
                {
                    if (!TryDefault(p, out values[i]))
                    { error = $"{t.Name}.{p.Name}: 対応するプロパティが無く既定値も無い"; return null; }
                    continue;
                }

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null)
                {
                    error = $"{t.Name}.{prop.Name}: PLParam が付いていないので外から与えられない";
                    return null;
                }
                if (attr.Ignore)
                {
                    if (!TryDefault(p, out values[i]))
                    { error = $"{t.Name}.{prop.Name}: Ignore 指定だが既定値が無い"; return null; }
                    continue;
                }

                string key = KeyOf(t, prop);
                if (args == null || !args.TryGetValue(key, out var raw) || raw == null)
                {
                    if (attr.Required && !HasDefault(p))
                    { error = $"{t.Name}: パラメータ \"{key}\" が要る"; return null; }
                    if (!TryDefault(p, out values[i]))
                    { error = $"{t.Name}: パラメータ \"{key}\" が要る"; return null; }
                    continue;
                }

                if (!TryParse(raw, p.ParameterType, out values[i], out string why))
                { error = $"{t.Name}.{prop.Name}: \"{key}\" を {p.ParameterType.Name} にできない（{why}）"; return null; }
            }

            try { return (PanelCommand)ctor.Invoke(values); }
            catch (TargetInvocationException ex)
            { error = $"{t.Name}: {ex.InnerException?.Message ?? ex.Message}"; return null; }
        }

        /// <summary>
        /// コマンドの中身を、Create が読み戻せる文字列パラメータにする。
        /// 往復検査（PanelCommandFactoryAudit）と、将来の送信側の一般化に使う。
        /// modelIndex は封筒側の値なので含めない。
        /// </summary>
        public static Dictionary<string, string> ToArgs(PanelCommand cmd)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (cmd == null) return result;

            Type t = cmd.GetType();
            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                object v;
                try { v = prop.GetValue(cmd); }
                catch (Exception) { continue; }
                if (v == null) continue;

                if (!TryFormat(v, out string s)) continue;
                result[KeyOf(t, prop)] = s;
            }
            return result;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>引数の多いコンストラクタを 1 本選ぶ。</summary>
        private static ConstructorInfo PickConstructor(Type t)
        {
            ConstructorInfo best = null;
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                if (best == null || c.GetParameters().Length > best.GetParameters().Length) best = c;
            return best;
        }

        /// <summary>
        /// 第 1 引数が「このコマンドが対象とするモデル」か。
        /// base(...) へそのまま渡っているものだけを封筒の値で埋める。
        /// targetModelIndex は含めない。SwitchModelCommand.TargetModelIndex と
        /// TransferVertexDataCommand.TargetModelIndex はどちらも
        /// 外から指定される実パラメータで、封筒の値ではない。
        /// </summary>
        private static bool IsModelIndexParam(ParameterInfo p)
        {
            if (p.ParameterType != typeof(int)) return false;
            string n = p.Name ?? "";
            return n.Equals("modelIndex", StringComparison.OrdinalIgnoreCase)
                || n.Equals("sourceModelIndex", StringComparison.OrdinalIgnoreCase)
                || n.Equals("baseModelIndex", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>コンストラクタ引数名に対応するプロパティ。大文字小文字は無視する。</summary>
        private static PropertyInfo FindProperty(Type t, string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return null;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>そのプロパティが外から届くときのキー。</summary>
        private static string KeyOf(Type t, PropertyInfo prop)
        {
            string alias = t.Name + "." + prop.Name;
            if (ParamAliases.TryGetValue(alias, out var k)) return k;
            return Camel(prop.Name);
        }

        private static bool HasDefault(ParameterInfo p)
            => p.HasDefaultValue || p.IsOptional;

        private static bool TryDefault(ParameterInfo p, out object value)
        {
            if (HasDefault(p)) { value = p.DefaultValue; return true; }
            value = null;
            return false;
        }

        // ================================================================
        // 文字列 → 値
        // ================================================================

        private static bool TryParse(string raw, Type type, out object value, out string why)
        {
            value = null;
            why   = "";

            if (type == typeof(string)) { value = raw; return true; }

            if (type == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                { value = i; return true; }
                why = "整数として読めない"; return false;
            }

            if (type == typeof(float))
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                { value = f; return true; }
                why = "実数として読めない"; return false;
            }

            if (type == typeof(bool)) { value = IsTrue(raw); return true; }

            if (type == typeof(ulong))
            {
                if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u))
                { value = u; return true; }
                why = "符号なし整数として読めない"; return false;
            }

            if (type.IsEnum)
            {
                // 数値でも名前でも受ける。UI は index を送り、人手の要求は名前を書く。
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ev))
                { value = Enum.ToObject(type, ev); return true; }
                try { value = Enum.Parse(type, raw, ignoreCase: true); return true; }
                catch (Exception) { why = "列挙の値でも名前でもない"; return false; }
            }

            if (type == typeof(int[]))    return TryParseIntArray(raw, out value, out why);
            if (type == typeof(float[]))  return TryParseFloatArray(raw, out value, out why);
            if (type == typeof(bool[]))   return TryParseBoolArray(raw, out value);
            if (type == typeof(ulong[]))  return TryParseUlongArray(raw, out value, out why);
            if (type == typeof(string[])) { value = SplitQuotedCsv(raw); return true; }

            why = $"{type.Name} は未対応";
            return false;
        }

        private static bool IsTrue(string s)
            => s != null &&
               (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

        private static string[] SplitPlain(string s)
            => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(',');

        private static bool TryParseIntArray(string raw, out object value, out string why)
        {
            why = "";
            var parts = SplitPlain(raw);
            var a = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out a[i]))
                { value = null; why = $"{i} 番目が整数でない"; return false; }
            value = a;
            return true;
        }

        private static bool TryParseFloatArray(string raw, out object value, out string why)
        {
            why = "";
            var parts = SplitPlain(raw);
            var a = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out a[i]))
                { value = null; why = $"{i} 番目が実数でない"; return false; }
            value = a;
            return true;
        }

        private static bool TryParseUlongArray(string raw, out object value, out string why)
        {
            why = "";
            var parts = SplitPlain(raw);
            var a = new ulong[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!ulong.TryParse(parts[i].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out a[i]))
                { value = null; why = $"{i} 番目が符号なし整数でない"; return false; }
            value = a;
            return true;
        }

        private static bool TryParseBoolArray(string raw, out object value)
        {
            var parts = SplitPlain(raw);
            var a = new bool[parts.Length];
            for (int i = 0; i < parts.Length; i++) a[i] = IsTrue(parts[i].Trim());
            value = a;
            return true;
        }

        // ================================================================
        // 値 → 文字列
        //
        // 区切りと桁の規則は PanelCommandRouter.cs:253-274 と同じにする。
        // 片方だけ変えると往復しなくなる。
        // ================================================================

        private static bool TryFormat(object v, out string s)
        {
            s = null;
            switch (v)
            {
                case string str: s = str;                                   return true;
                case bool b:     s = b ? "true" : "false";                  return true;
                case int i:      s = i.ToString(CultureInfo.InvariantCulture); return true;
                case ulong u:    s = u.ToString(CultureInfo.InvariantCulture); return true;
                case float f:    s = f.ToString("R", CultureInfo.InvariantCulture); return true;
            }

            if (v is Enum e)
            {
                s = Convert.ToInt32(e).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            switch (v)
            {
                case int[] ia:    s = JoinPlain(ia.Length, k => ia[k].ToString(CultureInfo.InvariantCulture)); return true;
                case ulong[] ua:  s = JoinPlain(ua.Length, k => ua[k].ToString(CultureInfo.InvariantCulture)); return true;
                case float[] fa:  s = JoinPlain(fa.Length, k => fa[k].ToString("R", CultureInfo.InvariantCulture)); return true;
                case bool[] ba:   s = JoinPlain(ba.Length, k => ba[k] ? "true" : "false"); return true;
                case string[] sa: s = JoinPlain(sa.Length, k => EscName(sa[k])); return true;
            }
            return false;
        }

        private static string JoinPlain(int count, Func<int, string> item)
        {
            if (count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(item(i));
            }
            return sb.ToString();
        }

        /// <summary>区切りと衝突する文字を二重引用符で包む。</summary>
        private static string EscName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>
        /// 二重引用符に対応したカンマ区切り分割。
        /// 規則は MeshSelSetCsvHelper / PanelCommandRouter.EscName と同一。
        /// </summary>
        public static string[] SplitQuotedCsv(string line)
        {
            if (string.IsNullOrEmpty(line)) return Array.Empty<string>();

            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                            else { i++; break; }
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
        }
    }
}
