// PanelCommandArgParams.cs
// ToArgs のキー（入れ子はドット区切り）から、その引数の型と PLParam を引く。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何のために要るか】
//   モディファイアスタックのパネルの引数欄（ParamFieldBuilder）は、項目の引数のキーと値しか持たない。
//   欄の種類（数値欄＋スライダー・選択ボタン・チェックボックス）と範囲を決めるには、
//   キーに当たる引数の型と PLParam が要る（PolyLing_モディファイア枠組み計画.md 3.5・段 5）。
//   範囲の宣言漏れの検査（PLParamAudit.RunRanges）も同じ口でキーを辿る。
//
// 【走査の規則は ToArgs と同じ】
//   コンストラクタ引数 → 対応するプロパティ → 入れ子はドット区切り（TryResolveArgType と同じ道）。

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Poly_Ling.Data
{
    public static partial class PanelCommandFactory
    {
        /// <summary>引数 1 つの宣言（キー・型・PLParam）。</summary>
        public readonly struct ArgParam
        {
            public readonly string           Key;
            public readonly Type             Type;
            public readonly PLParamAttribute Attr;

            public ArgParam(string key, Type type, PLParamAttribute attr)
            { Key = key; Type = type; Attr = attr; }
        }

        /// <summary>
        /// コマンド型 t の引数キー key の型と PLParam を引く。入れ子はドット区切り。
        /// 見つからない・PLParam が無いときは false。
        /// </summary>
        public static bool TryGetArgParam(Type t, string key, out Type argType, out PLParamAttribute attr)
        {
            argType = null;
            attr    = null;
            if (t == null || string.IsNullOrEmpty(key)) return false;
            string[] parts = key.Split('.');

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return false;

            Type cur = null;
            PLParamAttribute curAttr = null;
            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;
                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;
                if (KeyOf(t, prop) != parts[0]) continue;
                cur     = prop.PropertyType;
                curAttr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                break;
            }
            if (cur == null) return false;

            for (int i = 1; i < parts.Length; i++)
            {
                if (!IsNestedType(cur)) return false;
                Type next = null;
                PLParamAttribute nextAttr = null;
                foreach (var m in EnumerateNested(cur))
                    if (Camel(m.Name) == parts[i]) { next = m.Type; nextAttr = m.Attr; break; }
                if (next == null) return false;
                cur     = next;
                curAttr = nextAttr;
            }

            if (curAttr == null) return false;
            argType = cur;
            attr    = curAttr;
            return true;
        }

        /// <summary>
        /// コマンド型 t の引数を、入れ子を末端まで展開して宣言順で返す（Ignore は除く）。
        /// 範囲の宣言漏れの検査が使う。
        /// </summary>
        public static List<ArgParam> EnumerateArgParams(Type t)
        {
            var result = new List<ArgParam>();
            if (t == null) return result;

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;
                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;
                var a = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (a == null || a.Ignore) continue;

                string key = KeyOf(t, prop);
                if (IsNestedType(prop.PropertyType)) CollectNestedArgParams(prop.PropertyType, key, 1, result);
                else result.Add(new ArgParam(key, prop.PropertyType, a));
            }
            return result;
        }

        private static void CollectNestedArgParams(Type t, string prefix, int depth, List<ArgParam> dst)
        {
            if (t == null || depth > NestedMaxDepth) return;
            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore || !m.CanWrite) continue;
                string key = prefix + "." + Camel(m.Name);
                if (IsNestedType(m.Type)) CollectNestedArgParams(m.Type, key, depth + 1, dst);
                else dst.Add(new ArgParam(key, m.Type, m.Attr));
            }
        }
    }
}
