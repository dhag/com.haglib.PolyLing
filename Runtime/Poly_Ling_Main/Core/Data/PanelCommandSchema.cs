// PanelCommandSchema.cs
// PanelCommand の定義から MCP の道具一覧（JSON Schema）を組み立てる。
//
// 【手書きしない理由】
//   道具は現在 150 本を超える。手で JSON Schema を書くと、コマンドを 1 本
//   足すたびに 2 か所を直すことになり、必ずずれる。
//   PanelCommandFactory が Create / ToArgs のために既に同じ情報を読んでいるので、
//   同じ走査規則を partial で共有する（別クラスにしない理由はそちら側の注記）。
//
// 【コマンドを 1 本足したときに要る更新】
//   無い。PLParamAudit.FindCommandTypes がアセンブリを走査するため、
//   次回の tools/list に自動で載る。載るための条件は 3 つで、いずれも既存の規約。
//     1. コンストラクタ引数名とプロパティ名が一致していること（FindProperty が引く）
//     2. 引数の型が対応表にあること（下記 TryJsonType）
//     3. 全プロパティに PLParam が付いていること（形状に無関係なものは Ignore = true）
//   破れは PanelCommandFactoryAudit.RunStructure が検出する。
//
// 【型の対応表は 3 か所ある。必ず一緒に直すこと】
//   ・PanelCommandFactory.TryParse    文字列 → 値       （PanelCommandFactory.cs）
//   ・PanelCommandFactory.TryFormat   値 → 文字列       （PanelCommandFactory.cs）
//   ・PanelCommandFactory.TryJsonType JSON Schema の型名（このファイル）
//   片方だけ変えると往復しないか、スキーマと実装が食い違う。
//
// 【JSON 値 → Create が受け取る文字列 の変換】
//   Create は Dictionary<string, string> を受け取る。配列やベクトルを
//   "1,2,3" のカンマ区切りへ直すのは JSON-RPC 層の仕事で、ここではやらない。
//   スキーマ側は素直な JSON の形（配列は array）で出す。
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommandFactory と同じ場所）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Poly_Ling.Data
{
    public static partial class PanelCommandFactory
    {
        // ================================================================
        // 道具一覧
        // ================================================================

        /// <summary>
        /// 全コマンドの道具定義を JSON 配列として返す。
        /// スキーマに出せないコマンドは黙って外す（理由は
        /// PanelCommandFactoryAudit.RunStructure が挙げる）。
        /// </summary>
        public static string BuildToolsListJson()
        {
            var sb = new StringBuilder();
            sb.Append('[');

            bool first = true;
            foreach (var t in PLParamAudit.FindCommandTypes())
            {
                if (!TryBuildToolJson(t, out string json, out _)) continue;
                if (!first) sb.Append(',');
                sb.Append(json);
                first = false;
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>スキーマに出せた道具の数と、出せなかった数を数える。</summary>
        public static void CountTools(out int usable, out int skipped)
        {
            usable = 0;
            skipped = 0;
            foreach (var t in PLParamAudit.FindCommandTypes())
            {
                if (TryBuildToolJson(t, out _, out _)) usable++;
                else skipped++;
            }
        }

        /// <summary>
        /// コマンド 1 本ぶんの道具定義を組み立てる。
        ///
        /// 走査は Create と同じ順序で行う。コンストラクタ引数を頭から見て、
        /// 第 1 引数の modelIndex は封筒の値なので出さない。
        /// </summary>
        /// <param name="reason">出せなかった理由。成功時は null。</param>
        public static bool TryBuildToolJson(Type t, out string json, out string reason)
        {
            json   = null;
            reason = null;

            if (t == null) { reason = "型が null"; return false; }

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) { reason = $"{t.Name}: public なコンストラクタが無い"; return false; }

            var props        = new List<string>();
            var requiredKeys = new List<string>();

            foreach (var p in ctor.GetParameters())
            {
                // 第 1 引数の modelIndex は封筒側の値。道具の引数には出さない。
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null)
                {
                    // 既定値があるなら Create も既定で埋めるので、出さないだけでよい。
                    if (HasDefault(p)) continue;
                    reason = $"{t.Name}.{p.Name}: 対応するプロパティが無く既定値も無い";
                    return false;
                }

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null)
                {
                    reason = $"{t.Name}.{prop.Name}: PLParam が付いていない";
                    return false;
                }
                if (attr.Ignore)
                {
                    // Create は Ignore の引数を既定値で埋める。既定値が無いと
                    // 必ず失敗する（PanelCommandFactory.Create の Ignore 分岐）ので、
                    // 道具として出してはいけない。
                    // AddGeneratedMeshCommand.Mesh がこれに当たる。
                    if (HasDefault(p)) continue;
                    reason = $"{t.Name}.{prop.Name}: Ignore 指定だが既定値が無く、外から作れない";
                    return false;
                }

                string key = KeyOf(t, prop);

                // 入れ子はドット区切りのキーへ展開する。Create の組み立てと同じ規則。
                // 入れ子そのものは required にしない（部分指定を許すため）。
                if (IsNestedType(p.ParameterType))
                {
                    if (!AppendNestedProperties(p.ParameterType, key, 1, props, out reason))
                    {
                        reason = $"{t.Name}.{prop.Name}: {reason}";
                        return false;
                    }
                    continue;
                }

                if (!TryJsonType(p.ParameterType, out string schemaFragment))
                {
                    reason = $"{t.Name}.{prop.Name}: 型 {p.ParameterType.Name} はスキーマに出せない";
                    return false;
                }

                props.Add(BuildPropertyJson(key, schemaFragment, attr));

                // Create は「引数が届かず、既定値も無い」ときに失敗する。
                // PLParam.Required ではなくコンストラクタの既定値の有無が実際の条件。
                if (!HasDefault(p)) requiredKeys.Add(key);
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"name\":").Append(Quote(ActionOf(t)));
            // コマンド自体の説明は PLCommand が持つ。付いていなければ空。
            var cmdAttr = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
            sb.Append(",\"description\":").Append(Quote(cmdAttr?.Description ?? ""));
            sb.Append(",\"inputSchema\":{\"type\":\"object\",\"properties\":{");
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(props[i]);
            }
            sb.Append('}');
            if (requiredKeys.Count > 0)
            {
                sb.Append(",\"required\":[");
                for (int i = 0; i < requiredKeys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Quote(requiredKeys[i]));
                }
                sb.Append(']');
            }
            sb.Append('}');

            AppendOutputSchema(sb, t);

            sb.Append('}');

            json = sb.ToString();
            return true;
        }

        // ================================================================
        // 戻り値のスキーマ
        // ================================================================

        /// <summary>
        /// コマンドに付いた PLResult から outputSchema を書き足す。
        /// 1 つも付いていなければ何も書かない（戻り値を返さないコマンド）。
        ///
        /// 基底クラスに付けた宣言も拾う（PLResult は Inherited = true）。
        /// 図形生成のように基底 1 つが全種の受け口を兼ねる系統は、
        /// 基底へ 1 度書けば全具象へ効く。
        ///
        /// 走査順は属性の宣言順ではない（GetCustomAttributes の順は保証されない）ため、
        /// Key の並びに意味を持たせないこと。JSON オブジェクトのキーは順不同。
        /// 同じ Key が重なったときは先に見えたものを採る。
        /// </summary>
        private static void AppendOutputSchema(StringBuilder sb, Type t)
        {
            var attrs = t.GetCustomAttributes(typeof(PLResultAttribute), inherit: true);
            if (attrs == null || attrs.Length == 0) return;

            var props    = new List<string>();
            var required = new List<string>();
            var seen     = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in attrs)
            {
                var a = raw as PLResultAttribute;
                if (a == null || a.Ignore) continue;
                if (string.IsNullOrEmpty(a.Key)) continue;
                if (!seen.Add(a.Key)) continue;

                props.Add(BuildResultPropertyJson(a));
                if (!a.Optional) required.Add(a.Key);
            }

            if (props.Count == 0) return;

            sb.Append(",\"outputSchema\":{\"type\":\"object\",\"properties\":{");
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(props[i]);
            }
            sb.Append('}');

            if (required.Count > 0)
            {
                sb.Append(",\"required\":[");
                for (int i = 0; i < required.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Quote(required[i]));
                }
                sb.Append(']');
            }
            sb.Append('}');
        }

        /// <summary>戻り値 1 項目ぶんのスキーマを組み立てる。</summary>
        private static string BuildResultPropertyJson(PLResultAttribute a)
        {
            var sb = new StringBuilder();
            sb.Append(Quote(a.Key)).Append(":{").Append(ResultKindFragment(a.Kind));

            if (!string.IsNullOrEmpty(a.Description))
                sb.Append(",\"description\":").Append(Quote(a.Description));

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// PLResultKind に対応する JSON Schema の断片を返す。
        ///
        /// Entry の中身は CommandDataBuilder.Entry が書く形と対にしてある。
        /// 片方だけ直すと、道具一覧と実際の応答が食い違う。
        /// masterIndex と objectId は載らないことがあるので required に入れない。
        /// </summary>
        private static string ResultKindFragment(PLResultKind kind)
        {
            switch (kind)
            {
                case PLResultKind.Integer:      return "\"type\":\"integer\"";
                case PLResultKind.Number:       return "\"type\":\"number\"";
                case PLResultKind.Text:         return "\"type\":\"string\"";
                case PLResultKind.Flag:         return "\"type\":\"boolean\"";
                case PLResultKind.IntegerArray: return "\"type\":\"array\",\"items\":{\"type\":\"integer\"}";
                case PLResultKind.TextArray:    return "\"type\":\"array\",\"items\":{\"type\":\"string\"}";
                case PLResultKind.NumberArray:  return "\"type\":\"array\",\"items\":{\"type\":\"number\"}";

                case PLResultKind.Entry:
                    return "\"type\":\"object\",\"properties\":{"
                         + "\"name\":{\"type\":\"string\"},"
                         + "\"kind\":{\"type\":\"string\",\"enum\":[\"None\",\"IndexSet\",\"LoopSet\",\"ValueSet\"]},"
                         + "\"count\":{\"type\":\"integer\"},"
                         + "\"summary\":{\"type\":\"string\"},"
                         + "\"masterIndex\":{\"type\":\"integer\"},"
                         + "\"objectId\":{\"type\":\"integer\",\"minimum\":0}"
                         + "},\"required\":[\"name\",\"kind\",\"count\",\"summary\"]";

                default: return "\"type\":\"string\"";
            }
        }

        // ================================================================
        // 入れ子の展開
        // ================================================================

        /// <summary>
        /// 入れ子の構造体を "prefix.member" のキーへ展開して props へ足す。
        /// 走査規則は PanelCommandFactory.TryBuildNested と対で保つこと。
        /// </summary>
        /// <param name="reason">出せなかった理由。成功時は null。</param>
        private static bool AppendNestedProperties(
            Type t, string prefix, int depth, List<string> props, out string reason)
        {
            reason = null;

            if (depth > NestedMaxDepth)
            { reason = $"{prefix}: 入れ子が深すぎる"; return false; }

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore || !m.CanWrite) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    if (!AppendNestedProperties(m.Type, key, depth + 1, props, out reason))
                        return false;
                    continue;
                }

                if (!TryJsonType(m.Type, out string frag))
                { reason = $"{key}: 型 {m.Type.Name} はスキーマに出せない"; return false; }

                props.Add(BuildPropertyJson(key, frag, m.Attr));
            }

            return true;
        }

        // ================================================================
        // パラメータ 1 つぶん
        // ================================================================

        private static string BuildPropertyJson(string key, string schemaFragment, PLParamAttribute attr)
        {
            var sb = new StringBuilder();
            sb.Append(Quote(key)).Append(":{").Append(schemaFragment);

            if (!string.IsNullOrEmpty(attr.Description))
                sb.Append(",\"description\":").Append(Quote(attr.Description));

            // 上下限は PLParam に直接書かれたものが優先。
            // LimitKey がある場合は ParameterLimits から引く（利用者が調整できる値）。
            if (attr.HasMin) AppendNumber(sb, "minimum", attr.Min);
            else if (attr.HasLimitKey &&
                     Poly_Ling.Core.ParameterLimits.TryGetF(attr.LimitKey + ".Min", out float lo))
                AppendNumber(sb, "minimum", lo);

            if (attr.HasMax) AppendNumber(sb, "maximum", attr.Max);
            else if (attr.HasLimitKey &&
                     Poly_Ling.Core.ParameterLimits.TryGetF(attr.LimitKey + ".Max", out float hi))
                AppendNumber(sb, "maximum", hi);

            if (attr.HasStep) AppendNumber(sb, "multipleOf", attr.Step);

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// 上下限を書き出す。
        ///
        /// PLParamAttribute.Min / Max は double だが、実際に入る値は float の
        /// 定数（CubeParams.SizeMin など）を広げたもの。double のまま "R" で出すと
        /// 0.1f が 0.10000000149011612 になって読みにくい。float の精度で丸める。
        /// </summary>
        private static void AppendNumber(StringBuilder sb, string key, double v)
        {
            string text = (v >= float.MinValue && v <= float.MaxValue)
                ? ((float)v).ToString("R", CultureInfo.InvariantCulture)
                : v.ToString("R", CultureInfo.InvariantCulture);

            sb.Append(',').Append(Quote(key)).Append(':').Append(text);
        }

        // ================================================================
        // 型の対応表（3 か所のうちの 1 つ。TryParse / TryFormat と一緒に直すこと）
        // ================================================================

        /// <summary>
        /// 型に対応する JSON Schema の断片（"type":... まで）を返す。
        /// 扱えない型は false。
        /// </summary>
        private static bool TryJsonType(Type type, out string fragment)
        {
            fragment = null;
            if (type == null) return false;

            if (type == typeof(string)) { fragment = "\"type\":\"string\"";  return true; }
            if (type == typeof(int))    { fragment = "\"type\":\"integer\""; return true; }
            if (type == typeof(float))  { fragment = "\"type\":\"number\"";  return true; }
            if (type == typeof(bool))   { fragment = "\"type\":\"boolean\""; return true; }

            // ulong は符号なし。負値を弾くために下限を付ける。
            if (type == typeof(ulong)) { fragment = "\"type\":\"integer\",\"minimum\":0"; return true; }

            // 列挙は名前で受ける。TryParse は名前でも数値でも受けるが、
            // 呼び出し側が読める名前の方を出す。
            if (type.IsEnum)
            {
                var sb = new StringBuilder("\"type\":\"string\",\"enum\":[");
                var names = Enum.GetNames(type);
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Quote(names[i]));
                }
                sb.Append(']');
                fragment = sb.ToString();
                return true;
            }

            // ベクトルは要素数が固定の数値配列。
            // TryParse は "x,y" / "x,y,z" の文字列を受けるので、
            // 配列 → カンマ区切りへの変換は JSON-RPC 層が行う。
            if (type == typeof(UnityEngine.Vector2))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":2"; return true; }
            if (type == typeof(UnityEngine.Vector3))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":3,\"maxItems\":3"; return true; }

            // 整数ベクトル。図形の分割数が使う。
            if (type == typeof(UnityEngine.Vector2Int))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"integer\"},\"minItems\":2,\"maxItems\":2"; return true; }
            if (type == typeof(UnityEngine.Vector3Int))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"integer\"},\"minItems\":3,\"maxItems\":3"; return true; }

            // 固定長ベクトルの配列は平たい数値列。要素数ぶんずつ並べる。
            // 読み側（TryParseVectorArray）と同じ規則。
            if (type == typeof(UnityEngine.Vector2[]))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"number\"}"; return true; }
            if (type == typeof(UnityEngine.Vector3[]))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"number\"}"; return true; }
            if (type == typeof(Poly_Ling.Selection.VertexPair[]))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"integer\"}"; return true; }

            if (type == typeof(int[]))    { fragment = "\"type\":\"array\",\"items\":{\"type\":\"integer\"}"; return true; }
            if (type == typeof(float[]))  { fragment = "\"type\":\"array\",\"items\":{\"type\":\"number\"}";  return true; }
            if (type == typeof(bool[]))   { fragment = "\"type\":\"array\",\"items\":{\"type\":\"boolean\"}"; return true; }
            if (type == typeof(string[])) { fragment = "\"type\":\"array\",\"items\":{\"type\":\"string\"}";  return true; }
            if (type == typeof(ulong[]))
            { fragment = "\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":0}"; return true; }

            return false;
        }

        // ================================================================
        // 文字列
        // ================================================================

        /// <summary>JSON の文字列リテラルにする。制御文字は \\u 形式で出す。</summary>
        private static string Quote(string s)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"':  sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n");  break;
                        case '\r': sb.Append("\\r");  break;
                        case '\t': sb.Append("\\t");  break;
                        default:
                            if (c < 0x20)
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
