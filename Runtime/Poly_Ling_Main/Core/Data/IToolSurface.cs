// IToolSurface.cs
// パネルがツールハンドラへ届くための窓口（操作経路統一計画.md E-2）。
//
// 【何のために要るか】
//   パネルはハンドラを具体型で受け取り、プロパティ・メソッドを直接触っていた。
//   これではパネルを本体と別のプロセス（リモートのクライアント）へ出せない。
//   パネルはこの窓口だけを使い、ツール名とパラメータ名で読み書き・呼び出しする。
//   本体側の実装はハンドラを直接読み、書き込みはコマンドで送る（HostToolSurface）。
//
// 【値の表し方】
//   文字列。PanelCommandFactory のコマンド引数と同じ規則（bool は true/false、enum は整数、
//   Vector は平たい数値列）。型付きの読み書きは ToolSurfaceExtensions を使う。

using System;

namespace Poly_Ling.Data
{
    public interface IToolSurface
    {
        /// <summary>パラメータまたは概要を読む。無ければ false。</summary>
        bool TryGet(string toolId, string name, out string value);

        /// <summary>パラメータを設定し、設定後の実際の値を返す。止められた・無い場合は false。</summary>
        bool TrySet(string toolId, string name, string value, out string actual);

        /// <summary>操作を呼ぶ。止められた・無い場合は false。</summary>
        bool Invoke(string toolId, string action);

        /// <summary>引数のある操作を呼ぶ。引数は名前と値（コマンド引数と同じ文字列表現）の並び。</summary>
        bool InvokeWith(string toolId, string action, string[] argKeys, string[] argValues);

        /// <summary>
        /// 概要のまとまり（[PLToolStateGroup]）を 1 回の取得でまとめて読む。キーはメンバー名（先頭小文字）。
        /// 下調べのように取得のたびに計算が走るものを、項目ごとに読み直さないため。無ければ空。
        /// </summary>
        System.Collections.Generic.IReadOnlyDictionary<string, string> GetGroup(string toolId, string group);
    }

    /// <summary>型付きの読み書き。値の文字列変換は PanelCommandFactory と同じ規則。</summary>
    public static class ToolSurfaceExtensions
    {
        /// <summary>GetGroup の結果から 1 項目を型付きで取り出す。</summary>
        public static T Item<T>(this System.Collections.Generic.IReadOnlyDictionary<string, string> g, string name, T fallback = default)
        {
            if (g == null || !g.TryGetValue(name, out string raw)) return fallback;
            if (typeof(T) == typeof(string)) return (T)(object)(raw ?? "");
            return PanelCommandFactory.TryParseValue(raw, typeof(T), out object v, out _) && v is T t ? t : fallback;
        }
        public static T Get<T>(this IToolSurface s, string toolId, string name, T fallback = default)
        {
            if (s == null || !s.TryGet(toolId, name, out string raw)) return fallback;
            if (typeof(T) == typeof(string)) return (T)(object)(raw ?? "");
            return PanelCommandFactory.TryParseValue(raw, typeof(T), out object v, out _) && v is T t ? t : fallback;
        }

        public static float  GetFloat (this IToolSurface s, string toolId, string name, float  fallback = 0f)    => s.Get(toolId, name, fallback);
        public static int    GetInt   (this IToolSurface s, string toolId, string name, int    fallback = 0)     => s.Get(toolId, name, fallback);
        public static bool   GetBool  (this IToolSurface s, string toolId, string name, bool   fallback = false) => s.Get(toolId, name, fallback);
        public static string GetString(this IToolSurface s, string toolId, string name, string fallback = "")    => s.Get(toolId, name, fallback);

        /// <summary>引数のある操作を型付きの値で呼ぶ。値は PanelCommandFactory の規則で文字列にして送る。</summary>
        public static bool Invoke(this IToolSurface s, string toolId, string action, params (string key, object value)[] args)
        {
            if (s == null) return false;
            var keys   = new string[args.Length];
            var values = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                keys[i] = args[i].key;
                if (args[i].value is string str) values[i] = str;
                else if (!PanelCommandFactory.TryFormatValue(args[i].value, out values[i])) return false;
            }
            return s.InvokeWith(toolId, action, keys, values);
        }

        /// <summary>型付きで設定し、設定後の実際の値を返す（設定できなければ value をそのまま返す）。</summary>
        public static T Set<T>(this IToolSurface s, string toolId, string name, T value)
        {
            if (s == null) return value;
            string raw;
            if (value is string str) raw = str;
            else if (!PanelCommandFactory.TryFormatValue(value, out raw)) return value;
            if (!s.TrySet(toolId, name, raw, out string actual)) return value;
            if (typeof(T) == typeof(string)) return (T)(object)(actual ?? "");
            return PanelCommandFactory.TryParseValue(actual, typeof(T), out object v, out _) && v is T t ? t : value;
        }
    }
}
