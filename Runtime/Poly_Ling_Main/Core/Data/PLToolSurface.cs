// PLToolSurface.cs
// ツールハンドラの公開層（操作経路統一計画.md B・C・D）。属性で宣言したパラメータ・操作・概要を
// 名前で読み書き・呼び出しする。値の文字列変換は PanelCommandFactory と同じ規則。
//
// 【パラメータの置き場所】
//   ・ハンドラ自身の public プロパティ／フィールド（[PLToolParam]）
//   ・設定オブジェクト（[PLToolSettings] を付けたプロパティ／引数なしメソッドが返す参照型）の
//     public フィールドと読み書きできるプロパティ。名前は "設定名.メンバー名"。

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Poly_Ling.Data
{
    public static class PLToolSurface
    {
        public struct Entry
        {
            public string Name;
            public string Type;
            public string Value;
            public string Description;
        }

        /// <summary>読み書きの対象 1 つ（持ち主のオブジェクトとメンバー）。</summary>
        private struct Slot
        {
            public string     Name;
            public object     Owner;
            public MemberInfo Member;
            public string     Description;

            public Type Type => Member is PropertyInfo p ? p.PropertyType : ((FieldInfo)Member).FieldType;
            public bool CanWrite => Member is PropertyInfo p ? p.CanWrite && p.GetSetMethod() != null
                                                             : !((FieldInfo)Member).IsInitOnly;
            public object Get() => Member is PropertyInfo p ? p.GetValue(Owner) : ((FieldInfo)Member).GetValue(Owner);
            public void Set(object v)
            {
                if (Member is PropertyInfo p) p.SetValue(Owner, v);
                else ((FieldInfo)Member).SetValue(Owner, v);
            }
        }

        private const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>ハンドラの型のツール名。属性が無ければ null。</summary>
        public static string ToolIdOf(Type t)
            => t?.GetCustomAttribute<PLToolAttribute>(inherit: false)?.Id;

        /// <summary>メンバー名 → 公開名（先頭を小文字）。</summary>
        public static string KeyOf(string memberName)
            => string.IsNullOrEmpty(memberName) ? memberName
               : char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);

        public static List<Entry> Params(object handler) => ToEntries(ParamSlots(handler));

        public static List<Entry> States(object handler) => ToEntries(StateSlots(handler));

        /// <summary>概要（[PLToolState] と [PLToolStateGroup] の中身）の読み取り対象。</summary>
        private static List<Slot> StateSlots(object handler)
        {
            var slots = new List<Slot>();
            if (handler == null) return slots;
            var t = handler.GetType();
            foreach (var p in t.GetProperties(Pub))
            {
                var a = p.GetCustomAttribute<PLToolStateAttribute>(inherit: true);
                if (a != null && p.CanRead)
                    slots.Add(new Slot { Name = KeyOf(p.Name), Owner = handler, Member = p, Description = a.Description });

                var ga = p.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true);
                if (ga != null && p.CanRead)
                    AddGroup(slots, string.IsNullOrEmpty(ga.Name) ? KeyOf(p.Name) : ga.Name, p.GetValue(handler));
            }
            foreach (var m in t.GetMethods(Pub))
            {
                if (m.GetParameters().Length != 0) continue;
                var ga = m.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true);
                if (ga == null) continue;
                AddGroup(slots, string.IsNullOrEmpty(ga.Name) ? KeyOf(m.Name) : ga.Name, m.Invoke(handler, null));
            }
            return slots;
        }

        /// <summary>概要のまとまり（構造体も可）の public フィールドと読めるプロパティを加える。</summary>
        private static void AddGroup(List<Slot> slots, string prefix, object group)
        {
            if (group == null) return;
            var gt = group.GetType();
            foreach (var f in gt.GetFields(Pub))
                slots.Add(new Slot { Name = prefix + "." + KeyOf(f.Name), Owner = group, Member = f, Description = $"{gt.Name}.{f.Name}" });
            foreach (var p in gt.GetProperties(Pub))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                slots.Add(new Slot { Name = prefix + "." + KeyOf(p.Name), Owner = group, Member = p, Description = $"{gt.Name}.{p.Name}" });
            }
        }

        public static List<Entry> Actions(object handler)
        {
            var list = new List<Entry>();
            if (handler == null) return list;
            foreach (var m in handler.GetType().GetMethods(Pub))
            {
                var a = m.GetCustomAttribute<PLToolActionAttribute>(inherit: true);
                if (a == null) continue;
                var ps = m.GetParameters();
                var names = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++) names[i] = ps[i].Name;
                // 引数のある操作は "名前(引数名,...)" で示す。
                string name = ps.Length == 0 ? KeyOf(m.Name) : $"{KeyOf(m.Name)}({string.Join(",", names)})";
                list.Add(new Entry { Name = name, Type = "action", Value = "", Description = a.Description });
            }
            return list;
        }

        /// <summary>
        /// パラメータまたは概要を 1 つ読む。無ければ false。
        /// </summary>
        public static bool TryGetValue(object handler, string name, out string value)
        {
            value = null;
            if (handler == null || string.IsNullOrEmpty(name)) return false;
            foreach (var s in ParamSlots(handler))
            {
                if (s.Name != name) continue;
                PanelCommandFactory.TryFormatValue(s.Get(), out value);
                value = value ?? (s.Get() as string) ?? "";
                return true;
            }
            foreach (var s in StateSlots(handler))
            {
                if (s.Name != name) continue;
                object v = s.Get();
                PanelCommandFactory.TryFormatValue(v, out value);
                value = value ?? (v as string) ?? "";
                return true;
            }
            return false;
        }

        /// <summary>概要のまとまり（[PLToolStateGroup]）を 1 回の取得でまとめて読む。無ければ空。</summary>
        public static Dictionary<string, string> ReadGroup(object handler, string group)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (handler == null || string.IsNullOrEmpty(group)) return map;
            var t = handler.GetType();
            object value = null;
            bool found = false;
            foreach (var p in t.GetProperties(Pub))
            {
                var ga = p.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true);
                if (ga == null || !p.CanRead) continue;
                if ((string.IsNullOrEmpty(ga.Name) ? KeyOf(p.Name) : ga.Name) != group) continue;
                value = p.GetValue(handler); found = true; break;
            }
            if (!found)
                foreach (var m in t.GetMethods(Pub))
                {
                    if (m.GetParameters().Length != 0) continue;
                    var ga = m.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true);
                    if (ga == null) continue;
                    if ((string.IsNullOrEmpty(ga.Name) ? KeyOf(m.Name) : ga.Name) != group) continue;
                    value = m.Invoke(handler, null); found = true; break;
                }
            if (!found || value == null) return map;

            var slots = new List<Slot>();
            AddGroup(slots, "", value);
            foreach (var s in slots)
            {
                object v = s.Get();
                PanelCommandFactory.TryFormatValue(v, out string str);
                map[s.Name.TrimStart('.')] = str ?? (v as string) ?? "";
            }
            return map;
        }

        /// <summary>
        /// パラメータを設定し、設定後の実際の値（setter が丸めた・拒んだ結果）を返す。
        /// </summary>
        public static bool TrySet(object handler, string name, string raw, out string actual, out string why)
        {
            actual = null; why = null;
            foreach (var s in ParamSlots(handler))
            {
                if (s.Name != name) continue;
                if (!s.CanWrite) { why = $"書き込めないパラメータです: {name}"; return false; }
                if (!PanelCommandFactory.TryParseValue(raw, s.Type, out object v, out string pw))
                { why = $"{name}: {pw}"; return false; }
                s.Set(v);
                PanelCommandFactory.TryFormatValue(s.Get(), out actual);
                return true;
            }
            why = $"パラメータがありません: {name}";
            return false;
        }

        /// <summary>操作を呼ぶ（引数なし）。</summary>
        public static bool TryInvoke(object handler, string name, out string why)
            => TryInvoke(handler, name, null, null, out why);

        /// <summary>
        /// 操作を呼ぶ。引数は名前（メソッドの引数名）と値（コマンド引数と同じ文字列表現）の組で渡す。
        /// 渡されなかった引数は既定値があればそれを使い、無ければ失敗する。
        /// </summary>
        public static bool TryInvoke(object handler, string name, string[] keys, string[] values, out string why)
        {
            why = null;
            if (handler == null) { why = "ツールがありません"; return false; }
            foreach (var m in handler.GetType().GetMethods(Pub))
            {
                if (m.GetCustomAttribute<PLToolActionAttribute>(inherit: true) == null) continue;
                if (KeyOf(m.Name) != name) continue;

                var ps   = m.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    int k = keys == null ? -1 : Array.IndexOf(keys, ps[i].Name);
                    if (k >= 0 && values != null && k < values.Length)
                    {
                        if (!PanelCommandFactory.TryParseValue(values[k], ps[i].ParameterType, out object v, out string pw))
                        { why = $"{name}.{ps[i].Name}: {pw}"; return false; }
                        args[i] = v;
                    }
                    else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else { why = $"{name}: 引数 {ps[i].Name} がありません"; return false; }
                }
                m.Invoke(handler, args);
                return true;
            }
            why = $"操作がありません: {name}";
            return false;
        }

        /// <summary>
        /// 検査用：文字列にできない型のパラメータ・概要を数える（"型名.メンバー名"）。
        /// 設定オブジェクトの中身は型から調べる（実体が無くても数えられるように）。
        /// </summary>
        public static List<string> Unformattable(Type t)
        {
            var bad = new List<string>();
            void Check(string owner, string member, Type type)
            {
                object sample = type.IsValueType ? Activator.CreateInstance(type) : "";
                if (type != typeof(string) && !PanelCommandFactory.TryFormatValue(sample, out _))
                    bad.Add($"{owner}.{member}（{type.Name}）");
            }
            foreach (var p in t.GetProperties(Pub))
            {
                if (p.GetCustomAttribute<PLToolParamAttribute>(inherit: true) != null ||
                    p.GetCustomAttribute<PLToolStateAttribute>(inherit: true) != null)
                    Check(t.Name, p.Name, p.PropertyType);
                if (p.GetCustomAttribute<PLToolSettingsAttribute>(inherit: true) != null)
                    CheckSettingsType(t.Name + "." + p.Name, p.PropertyType, Check);
                if (p.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true) != null)
                    CheckGroupType(t.Name + "." + p.Name, p.PropertyType, Check);
            }
            foreach (var f in t.GetFields(Pub))
                if (f.GetCustomAttribute<PLToolParamAttribute>(inherit: true) != null)
                    Check(t.Name, f.Name, f.FieldType);
            foreach (var m in t.GetMethods(Pub))
            {
                if (m.GetParameters().Length != 0) continue;
                if (m.GetCustomAttribute<PLToolSettingsAttribute>(inherit: true) != null)
                    CheckSettingsType(t.Name + "." + m.Name, m.ReturnType, Check);
                if (m.GetCustomAttribute<PLToolStateGroupAttribute>(inherit: true) != null)
                    CheckGroupType(t.Name + "." + m.Name, m.ReturnType, Check);
            }
            return bad;
        }

        // ----------------------------------------------------------------

        private static void CheckGroupType(string owner, Type gt, Action<string, string, Type> check)
        {
            foreach (var f in gt.GetFields(Pub)) check(owner, f.Name, f.FieldType);
            foreach (var p in gt.GetProperties(Pub))
                if (p.CanRead && p.GetIndexParameters().Length == 0) check(owner, p.Name, p.PropertyType);
        }

        private static void CheckSettingsType(string owner, Type st, Action<string, string, Type> check)
        {
            foreach (var f in st.GetFields(Pub)) if (!f.IsInitOnly) check(owner, f.Name, f.FieldType);
            foreach (var p in st.GetProperties(Pub))
                if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) check(owner, p.Name, p.PropertyType);
        }

        private static List<Slot> ParamSlots(object handler)
        {
            var slots = new List<Slot>();
            if (handler == null) return slots;
            var t = handler.GetType();

            foreach (var p in t.GetProperties(Pub))
            {
                var a = p.GetCustomAttribute<PLToolParamAttribute>(inherit: true);
                if (a != null && p.CanRead)
                    slots.Add(new Slot { Name = KeyOf(p.Name), Owner = handler, Member = p, Description = a.Description });

                var sa = p.GetCustomAttribute<PLToolSettingsAttribute>(inherit: true);
                if (sa != null && p.CanRead)
                    AddSettings(slots, string.IsNullOrEmpty(sa.Name) ? KeyOf(p.Name) : sa.Name, p.GetValue(handler));
            }
            foreach (var f in t.GetFields(Pub))
            {
                var a = f.GetCustomAttribute<PLToolParamAttribute>(inherit: true);
                if (a != null)
                    slots.Add(new Slot { Name = KeyOf(f.Name), Owner = handler, Member = f, Description = a.Description });
            }
            foreach (var m in t.GetMethods(Pub))
            {
                if (m.GetParameters().Length != 0) continue;
                var sa = m.GetCustomAttribute<PLToolSettingsAttribute>(inherit: true);
                if (sa == null) continue;
                string prefix = string.IsNullOrEmpty(sa.Name) ? KeyOf(m.Name) : sa.Name;
                AddSettings(slots, prefix, m.Invoke(handler, null));
            }
            return slots;
        }

        /// <summary>設定オブジェクト（参照型）の public フィールドと読み書きできるプロパティを加える。</summary>
        private static void AddSettings(List<Slot> slots, string prefix, object settings)
        {
            if (settings == null || settings.GetType().IsValueType) return;
            var st = settings.GetType();
            foreach (var f in st.GetFields(Pub))
            {
                if (f.IsInitOnly) continue;
                slots.Add(new Slot { Name = prefix + "." + KeyOf(f.Name), Owner = settings, Member = f, Description = $"{st.Name}.{f.Name}" });
            }
            foreach (var p in st.GetProperties(Pub))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length != 0) continue;
                slots.Add(new Slot { Name = prefix + "." + KeyOf(p.Name), Owner = settings, Member = p, Description = $"{st.Name}.{p.Name}" });
            }
        }

        private static List<Entry> ToEntries(List<Slot> slots)
        {
            var list = new List<Entry>();
            foreach (var s in slots)
            {
                string v;
                try { PanelCommandFactory.TryFormatValue(s.Get(), out v); }
                catch (Exception e) { v = $"(読めません: {e.GetType().Name})"; }
                list.Add(new Entry { Name = s.Name, Type = s.Type.Name, Value = v ?? "", Description = s.Description });
            }
            return list;
        }
    }
}
