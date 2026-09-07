// PanelCommandNested.cs
// 入れ子の構造体・クラスを「ドット区切りの平坦なキー」として扱う。
//
// 【なぜ要るか】
//   図形生成コマンドは 30 本すべてがパラメータ構造体（CubeParams ほか）を
//   1 個だけ持つ。PrimitivePlacement も同じ形。これらを手で平坦化すると、
//   既にある PLParam の複製を 30 本ぶん書き写すことになり、必ずずれる。
//   構造体側にも PLParam が付いている（CubeMeshGenerator.cs:20 の注記）ので、
//   そのまま走査して入れ子のキーを作る。
//
// 【キーの作り方】
//   コマンドのプロパティ名を頭に付け、フィールド名を続ける。
//     params.widthTop / params.subdivisions / placement.worldPosition
//   コンストラクタ引数名（図形は 30 本とも "prms"）ではなくプロパティ名を使う。
//   読み手に意味が通るのはプロパティ名の方なので。
//   入れ子が 2 段以上でも同じ規則で伸ばす（params.loop.width）。
//
// 【平坦なキーのままにする理由】
//   Create は Dictionary<string, string> を受け取る。ここで入れ子の JSON を
//   組み立てる形にすると、キー → 値 の対応表（TryParse / TryFormat /
//   TryJsonType）とは別の 4 つ目の規則が生まれる。ドット区切りなら 3 表の
//   ままで済む。
//
// 【対応表は 3 か所ある。必ず一緒に直すこと】
//   ・TryParse    文字列 → 値       （PanelCommandFactory.cs）
//   ・TryFormat   値 → 文字列       （PanelCommandFactory.cs）
//   ・TryJsonType JSON Schema の型名（PanelCommandSchema.cs）
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Poly_Ling.Data
{
    public static partial class PanelCommandFactory
    {
        /// <summary>
        /// 入れ子をたどる深さの上限。
        /// 循環参照で止まらなくなるのを防ぐ。実際に要るのは 2 段
        /// （params.loop.width）まで。
        /// </summary>
        private const int NestedMaxDepth = 3;

        /// <summary>入れ子の 1 メンバー。フィールドとプロパティを同じに扱う。</summary>
        private readonly struct NestedMember
        {
            public readonly string           Name;
            public readonly Type             Type;
            public readonly PLParamAttribute Attr;
            private readonly FieldInfo       _field;
            private readonly PropertyInfo    _prop;

            public NestedMember(FieldInfo f, PLParamAttribute a)
            { _field = f; _prop = null; Name = f.Name; Type = f.FieldType; Attr = a; }

            public NestedMember(PropertyInfo p, PLParamAttribute a)
            { _field = null; _prop = p; Name = p.Name; Type = p.PropertyType; Attr = a; }

            public object GetValue(object owner)
                => _field != null ? _field.GetValue(owner) : _prop.GetValue(owner);

            public void SetValue(object owner, object value)
            {
                if (_field != null) _field.SetValue(owner, value);
                else if (_prop.CanWrite) _prop.SetValue(owner, value);
            }

            public bool CanWrite => _field != null || (_prop != null && _prop.CanWrite);
        }

        /// <summary>
        /// スキーマとして表現できる型か。直接扱える型か、入れ子として展開できる型。
        /// 監査（PanelCommandFactoryAudit.IsSupported）もここを見る。
        /// 判定を書き写すと対応表が 4 つ目になるので、公開はこの 1 本だけにする。
        /// </summary>
        public static bool IsSchemaRepresentable(Type t)
            => IsDirectlyParsable(t) || IsNestedType(t);

        /// <summary>
        /// 入れ子として扱える型か。
        ///
        /// 条件は「PLParam の付いた書き込めるメンバーを 1 つ以上持つこと」。
        /// インターフェース・抽象型・Unity の Object 派生は除く
        /// （前者は実体を決められず、後者は値として送るものではない）。
        /// </summary>
        private static bool IsNestedType(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return false;
            if (t.IsArray || t.IsInterface || t.IsAbstract) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
            if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)) return false;

            // TryParse が直接扱う型（Vector2 / Vector3 など）は入れ子にしない。
            if (IsDirectlyParsable(t)) return false;

            foreach (var m in EnumerateNested(t))
            {
                if (m.CanWrite) return true;
            }
            return false;
        }

        /// <summary>
        /// PLParam の付いた公開メンバーを宣言順で返す。Ignore は除かない
        /// （呼び出し側が用途に応じて外す）。
        /// </summary>
        private static IEnumerable<NestedMember> EnumerateNested(Type t)
        {
            if (t == null) yield break;

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var a = f.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (a != null) yield return new NestedMember(f, a);
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var a = p.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (a != null && p.CanWrite) yield return new NestedMember(p, a);
            }
        }

        /// <summary>
        /// 入れ子の既定インスタンスを作る。
        /// static な Default プロパティ／フィールドがあればそれを使う
        /// （PrimitivePlacement.Default のように、既定値がそこにあるため）。
        /// </summary>
        private static object CreateNestedDefault(Type t)
        {
            var pd = t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
            if (pd != null && pd.PropertyType == t) return pd.GetValue(null);

            var fd = t.GetField("Default", BindingFlags.Public | BindingFlags.Static);
            if (fd != null && fd.FieldType == t) return fd.GetValue(null);

            if (t.IsValueType) return Activator.CreateInstance(t);

            var ctor = t.GetConstructor(Type.EmptyTypes);
            return ctor != null ? ctor.Invoke(null) : null;
        }

        /// <summary>
        /// 入れ子の値を args から組み立てる。
        /// 指定の無いメンバーは既定値のまま残す（部分指定を許す）。
        /// </summary>
        /// <param name="prefix">キーの頭。"params" など。</param>
        /// <param name="reason">組み立てられなかった理由。成功時は null。</param>
        private static bool TryBuildNested(
            Type t, string prefix, IReadOnlyDictionary<string, string> args,
            int depth, out object value, out string reason)
        {
            value  = null;
            reason = null;

            if (depth > NestedMaxDepth)
            { reason = $"{prefix}: 入れ子が深すぎる"; return false; }

            object box = CreateNestedDefault(t);
            if (box == null)
            { reason = $"{prefix}: {t.Name} の既定値を作れない"; return false; }

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore || !m.CanWrite) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    // 子に 1 つでも指定があるときだけ組み立てる。
                    if (!HasAnyKeyWithPrefix(args, key + ".")) continue;
                    if (!TryBuildNested(m.Type, key, args, depth + 1, out object child, out reason))
                        return false;
                    m.SetValue(box, child);
                    continue;
                }

                if (args == null || !args.TryGetValue(key, out var raw) || raw == null) continue;

                if (!TryParse(raw, m.Type, out object v, out string why))
                { reason = $"\"{key}\" を {m.Type.Name} にできない（{why}）"; return false; }

                m.SetValue(box, v);
            }

            value = box;
            return true;
        }

        private static bool HasAnyKeyWithPrefix(IReadOnlyDictionary<string, string> args, string prefix)
        {
            if (args == null) return false;
            foreach (var kv in args)
                if (kv.Key != null && kv.Key.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 入れ子の値をドット区切りのキーへ展開して dst へ入れる。ToArgs 用。
        /// </summary>
        private static void FlattenNested(
            object owner, Type t, string prefix, int depth, Dictionary<string, string> dst)
        {
            if (owner == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                object v;
                try { v = m.GetValue(owner); }
                catch (Exception) { continue; }
                if (v == null) continue;

                if (IsNestedType(m.Type))
                {
                    FlattenNested(v, m.Type, key, depth + 1, dst);
                    continue;
                }

                if (TryFormat(v, out string s)) dst[key] = s;
            }
        }
    }
}
