// UiAutomationAudit.cs
// UI 自動操作の登録状況の検査（queryUiAutomationAudit）。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【何を数えるか】すべて 0 なら、右ペインの全項目が登録されている。
//   unregisteredSections … 右ペインのセクションのうち RegisterPanel されていないもの
//   missingAttributes    … RegisterObject したオブジェクトの VisualElement 型のフィールド・
//                          自動実装プロパティのうち、UiControlAttribute が付いていないもの
//                          （出さないものには Ignore を付ける。PLParamAudit と同じ考え方）
//   registrationErrors   … 登録を拒否したもの（ID 重複・属性の誤り・メソッド名の誤り）
//   unspecifiedSafety    … ボタンなのに安全度が未指定のもの（押せない）
//   unsupportedTypes     … 既定の読み書きができず、Getter / Setter も無い型のもの
//
// 【セクションの一覧】
//   右ペインのセクションはすべて PlayerLayoutRoot.AddSection が RightPaneContent へ足している
//   （PlayerLayoutRoot.RightPane.cs の AddSection）。その子を一覧とする。
//   表示名は PlayerLayoutRoot の公開プロパティの名前から引く（検査の表示用で、操作には使わない）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public static class UiAutomationAudit
    {
        public sealed class Result
        {
            public int    Sections;
            public int    UnregisteredSections;
            public int    MissingAttributes;
            public int    RegistrationErrors;
            public int    UnspecifiedSafety;
            public int    UnsupportedTypes;
            public int    UnregisteredElements;
            public int    Controls;
            public int    Panels;
            public string Report;

            /// <summary>
            /// 直すべきものが残っているか。6 つのカウンタが全部 0 なら false。
            /// 未構築（今は表示していないだけの項目）は正常なので数えない。
            /// </summary>
            public bool HasProblems =>
                UnregisteredSections > 0 || MissingAttributes    > 0 ||
                RegistrationErrors   > 0 || UnspecifiedSafety    > 0 ||
                UnsupportedTypes     > 0 || UnregisteredElements > 0;

            /// <summary>問題の件数を 1 行にまとめる。0 のものは省く。</summary>
            public string ProblemSummary()
            {
                var parts = new List<string>();
                if (UnregisteredSections > 0) parts.Add($"未登録のセクション {UnregisteredSections}");
                if (MissingAttributes    > 0) parts.Add($"属性の付け忘れ {MissingAttributes}");
                if (RegistrationErrors   > 0) parts.Add($"登録失敗 {RegistrationErrors}");
                if (UnspecifiedSafety    > 0) parts.Add($"安全度未指定のボタン {UnspecifiedSafety}");
                if (UnsupportedTypes     > 0) parts.Add($"未対応の型 {UnsupportedTypes}");
                if (UnregisteredElements > 0) parts.Add($"未登録の部品 {UnregisteredElements}");
                return string.Join(" / ", parts);
            }
        }

        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static Result Run(UiAutomationRegistry registry, VisualElement rightPaneContent, object layoutRoot)
        {
            var r  = new Result();
            var sb = new StringBuilder();

            // ── セクション ─────────────────────────────────────────
            var registeredSections = new HashSet<VisualElement>();
            foreach (var p in registry.Panels)
                if (p.Section != null) registeredSections.Add(p.Section);

            var names = SectionNames(layoutRoot);

            var excludedReasons = new Dictionary<VisualElement, string>();
            foreach (var ex in registry.ExcludedSections)
                if (ex.Section != null && !excludedReasons.ContainsKey(ex.Section))
                    excludedReasons.Add(ex.Section, ex.Reason);

            var unregistered = new List<string>();
            var excluded     = new List<string>();
            if (rightPaneContent != null)
            {
                foreach (var child in rightPaneContent.Children())
                {
                    r.Sections++;
                    if (registeredSections.Contains(child)) continue;
                    string name = names.TryGetValue(child, out var n) ? n : "(名前なし)";
                    if (excludedReasons.TryGetValue(child, out var reason))
                        excluded.Add($"{name}（{reason}）");
                    else
                        unregistered.Add(name);
                }
            }
            r.UnregisteredSections = unregistered.Count;

            // ── 属性の付け忘れ ─────────────────────────────────────
            var missing = new List<string>();
            foreach (var o in registry.Objects)
                CollectMissing(o.Instance, missing);
            r.MissingAttributes = missing.Count;

            // ── 登録の失敗 ─────────────────────────────────────────
            r.RegistrationErrors = registry.Errors.Count;

            // ── 項目ごとの判定 ─────────────────────────────────────
            var unspecified = new List<string>();
            var unsupported = new List<string>();
            int unbuilt = 0;
            foreach (var c in registry.Controls)
            {
                r.Controls++;
                var e = c.Element;
                if (e == null) { unbuilt++; continue; }

                if (e is Button && c.Safety == UiSafety.Unspecified)
                    unspecified.Add($"{c.Id}（{c.Source}）");

                bool readable = c.Getter != null || UiAutomationService.IsReadableType(e);
                bool writable = c.Setter != null || UiAutomationService.IsWritableType(e) || e is Button;
                if (!readable && !writable)
                    unsupported.Add($"{c.Id}（{e.GetType().Name}、{c.Source}）");
            }
            r.UnspecifiedSafety = unspecified.Count;
            r.UnsupportedTypes  = unsupported.Count;
            r.Panels            = registry.Panels.Count;

            // ── 未登録の部品（登録済みパネルの中を辿る）───────────────
            var strays = CollectStrayElements(registry);
            r.UnregisteredElements = strays.Count;

            // ── 報告 ───────────────────────────────────────────────
            sb.AppendLine($"[UiAutomationAudit] セクション {r.Sections} / パネル {r.Panels} / 項目 {r.Controls}"
                        + $"（未構築 {unbuilt}）");
            sb.AppendLine($"  未登録のセクション {r.UnregisteredSections} / 属性の付け忘れ {r.MissingAttributes}"
                        + $" / 登録失敗 {r.RegistrationErrors} / 安全度未指定のボタン {r.UnspecifiedSafety}"
                        + $" / 未対応の型 {r.UnsupportedTypes} / 未登録の部品 {r.UnregisteredElements}");
            AppendList(sb, "未登録のセクション", unregistered);
            AppendList(sb, "対象外にしたセクション", excluded);
            AppendList(sb, "属性の付け忘れ", missing);
            AppendList(sb, "登録失敗", registry.Errors);
            AppendList(sb, "安全度未指定のボタン", unspecified);
            AppendList(sb, "未対応の型", unsupported);
            AppendList(sb, "未登録の部品", strays);
            r.Report = sb.ToString();
            return r;
        }

        // ================================================================
        // 未登録の部品
        // ================================================================

        /// <summary>
        /// 登録済みパネルのセクションの中を論理上の子（Children）で辿り、操作できる部品
        /// （Button・BaseField 系・一覧）のうち登録されていないものを集める。
        /// 登録済みの部品と、データ行のコンテナ（UiControl の Rows）の中は辿らない。
        /// Children で辿るので、Foldout の見出しや ScrollView のスクロールバーは対象にならない。
        /// フィールドに持たずに作った部品（補助関数の中で作ったものなど）はここで見つかる。
        /// </summary>
        private static List<string> CollectStrayElements(UiAutomationRegistry registry)
        {
            var strays = new List<string>();

            var registered = new HashSet<VisualElement>();
            foreach (var c in registry.Controls)
            {
                var e = c.Element;
                if (e != null) registered.Add(e);
            }

            var rows = new HashSet<VisualElement>();
            foreach (var rc in registry.RowContainers)
            {
                var e = rc.Resolve?.Invoke();
                if (e != null) rows.Add(e);
            }

            foreach (var p in registry.Panels)
                if (p.Section != null)
                    WalkStrays(p.Id, p.Section, registered, rows, strays);

            return strays;
        }

        private static void WalkStrays(
            string panelId, VisualElement e, HashSet<VisualElement> registered,
            HashSet<VisualElement> rows, List<string> strays)
        {
            foreach (var child in e.Children())
            {
                if (rows.Contains(child) || registered.Contains(child)) continue;
                if (IsOperable(child))
                {
                    strays.Add($"{panelId}: {child.GetType().Name}「{Caption(child)}」");
                    continue;
                }
                WalkStrays(panelId, child, registered, rows, strays);
            }
        }

        /// <summary>利用者が操作できる部品か（Button・BaseField 系・一覧）。</summary>
        private static bool IsOperable(VisualElement v)
        {
            if (v is Button || v is BaseVerticalCollectionView) return true;
            for (var t = v.GetType(); t != null && t != typeof(VisualElement); t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BaseField<>)) return true;
            return false;
        }

        /// <summary>報告用の見出し。Button は文字、BaseField はラベル。</summary>
        private static string Caption(VisualElement v)
        {
            if (v is Button b) return b.text ?? "";
            var prop = v.GetType().GetProperty("label", BindingFlags.Instance | BindingFlags.Public);
            if (prop != null && prop.PropertyType == typeof(string))
                return prop.GetValue(v) as string ?? "";
            return v.name ?? "";
        }

        /// <summary>
        /// VisualElement 型のフィールドと自動実装プロパティのうち、UiControlAttribute が無いものを集める。
        /// 自動実装プロパティは属性をプロパティ側に付けるので、裏のフィールドではなくプロパティを見る。
        /// 計算プロパティ（=> で他のメンバーを返すもの）は数えない。
        /// </summary>
        private static void CollectMissing(object instance, List<string> missing)
        {
            if (instance == null) return;
            for (var t = instance.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(MemberFlags))
                {
                    // UiNested の補助オブジェクトは中身まで見る。
                    if (f.GetCustomAttribute<UiNestedAttribute>() != null)
                    {
                        CollectMissing(f.GetValue(instance), missing);
                        continue;
                    }

                    // UiControl 付きの部品を持つ補助オブジェクトを、UiNested なしで持っている。
                    // 取り込まないなら UiControl(Ignore = true) を付けて明示する。
                    if (!typeof(VisualElement).IsAssignableFrom(f.FieldType)
                        && !f.IsDefined(typeof(CompilerGeneratedAttribute), false)
                        && f.GetCustomAttribute<UiControlAttribute>() == null
                        && HasUiControlMembers(f.FieldType))
                    {
                        missing.Add($"{t.Name}.{f.Name}（UiNested か UiControl(Ignore) が要る）");
                        continue;
                    }

                    if (!typeof(VisualElement).IsAssignableFrom(f.FieldType)) continue;

                    if (f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        // 自動実装プロパティの裏のフィールド "<Name>k__BackingField"
                        string propName = BackingFieldOwner(f.Name);
                        var prop = propName != null ? t.GetProperty(propName, MemberFlags) : null;
                        if (prop != null && prop.GetCustomAttribute<UiControlAttribute>() == null)
                            missing.Add($"{t.Name}.{propName}");
                        continue;
                    }

                    if (f.GetCustomAttribute<UiControlAttribute>() == null)
                        missing.Add($"{t.Name}.{f.Name}");
                }
            }
        }

        private static string BackingFieldOwner(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName) || fieldName[0] != '<') return null;
            int end = fieldName.IndexOf('>');
            return end > 1 ? fieldName.Substring(1, end - 1) : null;
        }

        private static readonly Dictionary<Type, bool> _hasUiControlCache = new Dictionary<Type, bool>();

        /// <summary>型（基底を含む）に UiControlAttribute 付きのメンバーがあるか。</summary>
        private static bool HasUiControlMembers(Type type)
        {
            if (type == null || type.IsValueType || type == typeof(string)) return false;
            if (_hasUiControlCache.TryGetValue(type, out bool cached)) return cached;

            bool found = false;
            for (var t = type; t != null && t != typeof(object) && !found; t = t.BaseType)
            {
                foreach (var m in t.GetFields(MemberFlags))
                    if (m.GetCustomAttribute<UiControlAttribute>() != null) { found = true; break; }
                if (found) break;
                foreach (var m in t.GetProperties(MemberFlags))
                    if (m.GetCustomAttribute<UiControlAttribute>() != null) { found = true; break; }
            }
            _hasUiControlCache[type] = found;
            return found;
        }

        private static Dictionary<VisualElement, string> SectionNames(object layoutRoot)
        {
            var map = new Dictionary<VisualElement, string>();
            if (layoutRoot == null) return map;
            foreach (var p in layoutRoot.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!typeof(VisualElement).IsAssignableFrom(p.PropertyType)) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (p.GetValue(layoutRoot) is VisualElement v && !map.ContainsKey(v))
                    map.Add(v, p.Name);
            }
            return map;
        }

        private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return;
            sb.AppendLine($"── {title} ──");
            foreach (var s in items) sb.AppendLine("  " + s);
        }
    }
}
