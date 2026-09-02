// PanelCommandDump.cs
// PanelCommand の中身を人が読める1行以上のテキストへ落とす。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   自動検証が「何をどのパラメータで実行したか」を残さないと、
//   結果を見せられても手順を追えない。手作業で同じことをやり直すこともできない。
//   コマンドは PLParam 属性でパラメータの表示名・説明・値域を持っているので、
//   それをそのまま並べれば手順書になる。
//
// 【出す順序】
//   宣言順で出す。並べ替えると、同じコマンドでも実行のたびに順が変わって
//   差分が取りにくくなる。
//
// 【入れ子】
//   パラメータ自体が構造体のとき（PrimitivePlacement や各図形の Params）は
//   その中の PLParam も 1 段だけ掘って出す。2 段以上は深追いしない。
//
// 【Ignore】
//   PLParam(Ignore = true) が付いたものは出さない。編集中の選択位置や
//   プレビューの視点角など、形状を決めないものが該当する。

using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>PanelCommand の内容をテキストへ落とす。</summary>
    public static class PanelCommandDump
    {
        /// <summary>配列を出す最大件数。これを超えたら件数だけ書く。</summary>
        private const int MaxArrayItems = 8;

        /// <summary>
        /// コマンド 1 件を複数行のテキストにする。
        /// 1 行目はコマンド名、2 行目以降がパラメータ。
        /// </summary>
        public static string Describe(PanelCommand cmd, string indent = "    ")
        {
            if (cmd == null) return "(null)";

            var sb = new StringBuilder();
            sb.Append(cmd.GetType().Name);

            var props = cmd.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in props)
            {
                if (p.Name == "ModelIndex") continue;

                var attr = p.GetCustomAttribute<PLParamAttribute>();
                if (attr != null && attr.Ignore) continue;

                object v;
                try { v = p.GetValue(cmd); }
                catch (Exception) { continue; }
                if (v == null) continue;

                // 構造体・クラスの中身は 1 段だけ掘る。
                if (HasPLParamMembers(p.PropertyType))
                {
                    sb.Append('\n').Append(indent).Append(p.Name).Append(':');
                    AppendMembers(sb, v, indent + "  ");
                }
                else
                {
                    sb.Append('\n').Append(indent)
                      .Append(p.Name).Append(" = ").Append(Format(v));
                }
            }
            return sb.ToString();
        }

        /// <summary>その型が PLParam の付いたフィールドを持つか。</summary>
        private static bool HasPLParamMembers(Type t)
        {
            if (t == null || t.IsPrimitive || t == typeof(string) || t.IsEnum) return false;
            if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector3Int)) return false;

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.GetCustomAttribute<PLParamAttribute>() != null) return true;
            return false;
        }

        /// <summary>PLParam の付いたフィールドを宣言順に並べる。</summary>
        private static void AppendMembers(StringBuilder sb, object obj, string indent)
        {
            var t = obj.GetType();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);

            var line = new StringBuilder();
            foreach (var f in fields)
            {
                var attr = f.GetCustomAttribute<PLParamAttribute>();
                if (attr == null || attr.Ignore) continue;

                object v;
                try { v = f.GetValue(obj); }
                catch (Exception) { continue; }

                string one = f.Name + "=" + Format(v);

                // 1 行が長くなりすぎたら折る。値の羅列なので詰めて出す。
                if (line.Length > 0 && line.Length + one.Length + 2 > 96)
                {
                    sb.Append('\n').Append(indent).Append(line);
                    line.Clear();
                }
                if (line.Length > 0) line.Append("  ");
                line.Append(one);
            }
            if (line.Length > 0) sb.Append('\n').Append(indent).Append(line);
        }

        /// <summary>値を短く読める形にする。</summary>
        public static string Format(object v)
        {
            switch (v)
            {
                case null:    return "null";
                case float f: return f.ToString("0.####");
                case double d: return d.ToString("0.####");
                case Vector2 v2: return $"({v2.x:0.####},{v2.y:0.####})";
                case Vector3 v3: return $"({v3.x:0.####},{v3.y:0.####},{v3.z:0.####})";
                case Vector3Int vi: return $"({vi.x},{vi.y},{vi.z})";
                case string s: return s;
                case bool b: return b ? "true" : "false";
            }

            if (v is IEnumerable e && !(v is string))
            {
                var sb = new StringBuilder("[");
                int n = 0;
                foreach (object item in e)
                {
                    if (n >= MaxArrayItems) { sb.Append(", …"); break; }
                    if (n > 0) sb.Append(", ");
                    sb.Append(Format(item));
                    n++;
                }
                sb.Append(']');

                // 打ち切ったときは総数も添える。何件流したかが分からないと追えない。
                if (n >= MaxArrayItems)
                {
                    int total = 0;
                    foreach (object _ in e) total++;
                    sb.Append(" 全").Append(total).Append("件");
                }
                return sb.ToString();
            }

            return v.ToString();
        }
    }
}
