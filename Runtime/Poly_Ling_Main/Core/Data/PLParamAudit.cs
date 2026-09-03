// PLParamAudit.cs
// PanelCommand の public プロパティに PLParam が付いているかを検査する。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   PLParamAttribute.cs:9-13 の設計どおり、スキーマに出さないものにも
//   Ignore = true を明示して付ける。属性が無い＝付け忘れ、として扱えるので、
//   付与作業の前に検査する側を先に用意しておく。
//   付与し終えたあとも、コマンドを 1 本足したときの付け忘れをここで拾える。
//
// 【対象の絞り方】
//   ・ModelIndex は基底が持つ位置情報なので除く。
//   ・算出プロパティ（式本体 / 他の値から導くもの）は入力パラメータではないので除く。
//     リフレクションでは「自動プロパティが持つコンパイラ生成の裏フィールド
//     （<Name>k__BackingField）があるか」で判別する。
//     ただし黙って捨てると取りこぼしに気づけないので、除いたものも Computed に残す。
//
// 【走査範囲】
//   PanelCommand と同じアセンブリだけを見る。派生クラスは PanelCommand.cs に
//   まとまっており、他アセンブリには 1 件も無い。全アセンブリ走査は
//   Player 実行時の起動コストになるので取らない。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Poly_Ling.Data
{
    /// <summary>PanelCommand のパラメータ属性の付け忘れを検査する。</summary>
    public static class PLParamAudit
    {
        /// <summary>検査で拾った 1 件。</summary>
        public sealed class Entry
        {
            /// <summary>コマンドの型名。</summary>
            public string CommandName { get; }

            /// <summary>プロパティ名。</summary>
            public string PropertyName { get; }

            /// <summary>プロパティの型名。</summary>
            public string PropertyTypeName { get; }

            public Entry(string commandName, string propertyName, string propertyTypeName)
            {
                CommandName      = commandName;
                PropertyName     = propertyName;
                PropertyTypeName = propertyTypeName;
            }

            public override string ToString()
                => $"{CommandName}.{PropertyName} : {PropertyTypeName}";
        }

        /// <summary>検査結果。</summary>
        public sealed class Report
        {
            /// <summary>検査したコマンドの数（abstract を除く）。</summary>
            public int CommandCount { get; internal set; }

            /// <summary>検査対象になったプロパティの数。</summary>
            public int TargetCount { get; internal set; }

            /// <summary>PLParam が付いていたプロパティの数。</summary>
            public int AttributedCount { get; internal set; }

            /// <summary>付け忘れ。</summary>
            public List<Entry> Missing { get; } = new List<Entry>();

            /// <summary>算出プロパティとして対象から外したもの。</summary>
            public List<Entry> Computed { get; } = new List<Entry>();

            /// <summary>付け忘れが 0 件か。</summary>
            public bool IsClean => Missing.Count == 0;

            /// <summary>人が読める形にする。</summary>
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append("[PLParamAudit] コマンド ").Append(CommandCount)
                  .Append(" / 対象プロパティ ").Append(TargetCount)
                  .Append(" / 付与済み ").Append(AttributedCount)
                  .Append(" / 付け忘れ ").Append(Missing.Count)
                  .Append(" / 算出につき対象外 ").Append(Computed.Count);

                if (Missing.Count > 0)
                {
                    sb.Append('\n').Append("── 付け忘れ ──");
                    foreach (var e in Missing) sb.Append('\n').Append("  ").Append(e);
                }
                if (Computed.Count > 0)
                {
                    sb.Append('\n').Append("── 算出につき対象外（取りこぼしが無いか目視で確認する） ──");
                    foreach (var e in Computed) sb.Append('\n').Append("  ").Append(e);
                }
                return sb.ToString();
            }
        }

        /// <summary>PanelCommand と同じアセンブリにある具象派生型を宣言順に近い形で返す。</summary>
        public static List<Type> FindCommandTypes()
        {
            var baseType = typeof(PanelCommand);
            var types = new List<Type>();

            Type[] all;
            try { all = baseType.Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in all)
            {
                if (t == null || t.IsAbstract) continue;
                if (!baseType.IsAssignableFrom(t)) continue;
                types.Add(t);
            }

            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return types;
        }

        /// <summary>全コマンドを検査する。</summary>
        public static Report Run() => Run(FindCommandTypes());

        /// <summary>指定した型だけを検査する。</summary>
        public static Report Run(IEnumerable<Type> types)
        {
            var report = new Report();
            if (types == null) return report;

            foreach (var t in types)
            {
                if (t == null) continue;
                report.CommandCount++;

                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (p.Name == "ModelIndex") continue;

                    var entry = new Entry(t.Name, p.Name, TypeName(p.PropertyType));

                    if (!HasBackingField(p))
                    {
                        report.Computed.Add(entry);
                        continue;
                    }

                    report.TargetCount++;

                    if (p.GetCustomAttribute<PLParamAttribute>(inherit: true) != null)
                        report.AttributedCount++;
                    else
                        report.Missing.Add(entry);
                }
            }
            return report;
        }

        /// <summary>
        /// 自動プロパティか。コンパイラが作る裏フィールドの有無で判定する。
        /// setter を持つ通常のプロパティも裏フィールドを持つので同じ扱いになる。
        /// </summary>
        private static bool HasBackingField(PropertyInfo p)
        {
            if (p.DeclaringType == null) return false;

            string name = "<" + p.Name + ">k__BackingField";
            var t = p.DeclaringType;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return true;
                t = t.BaseType;
            }
            return false;
        }

        /// <summary>ジェネリクスと配列を短く書く。</summary>
        private static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (!t.IsGenericType) return t.Name;

            string bare = t.Name;
            int tick = bare.IndexOf('`');
            if (tick >= 0) bare = bare.Substring(0, tick);
            return bare + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }
    }
}
