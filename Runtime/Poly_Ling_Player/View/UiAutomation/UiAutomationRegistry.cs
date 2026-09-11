// UiAutomationRegistry.cs
// UI 自動操作の対象（パネル・項目）の登録簿。意味 ID と VisualElement を結ぶ。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【登録した物しか操作しない】
//   画面内の要素を型・名前・表示文字列で探す口は作らない。
//   誤操作の防止、UI 変更への耐性、チュートリアルの再現性のため。
//
// 【登録の仕方】
//   パネル … PolyLingPlayerViewerCore が RegisterPanel（表示の手順 ShowXxxPanel を知っている側）
//   項目   … RegisterObject にサブパネルのインスタンスを渡す。UiControlAttribute の付いた
//            メンバーだけを集めて登録する（UiControlAttribute.cs の冒頭注記）。
//            属性で表せないもの（作り直しのたびに増減する行など）は RegisterControl で直接登録する。
//
// 【要素は操作のときに読む】
//   登録時の参照を持たず、Resolve でその時点のフィールド値を読む。
//   作り直しでフィールドが別の要素に差し替わる UI にも追従する。
//
// 【範囲（scope）】
//   作り直しのたびに増減する項目は scope 付きで登録し、作り直す前に ClearScope で消す。
//
// 【登録の失敗】
//   ID の重複・所属パネル未登録・属性の誤りは登録せずに Errors へ積み、
//   queryUiAutomationAudit の登録失敗として数える。

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public sealed class UiAutomationRegistry
    {
        /// <summary>パネル 1 件。</summary>
        public sealed class PanelEntry
        {
            public string        Id;
            public string        Description;
            /// <summary>右ペイン内のセクション。表示中かどうかの判定と検査に使う。</summary>
            public VisualElement Section;
            /// <summary>パネルを開く処理。ボタンで開いたときと同じ ShowXxxPanel を渡す。</summary>
            public Action        Show;
        }

        /// <summary>項目 1 件。</summary>
        public sealed class ControlEntry
        {
            public string   Id;
            public string   PanelId;
            public string   Description;
            public UiSafety Safety;

            /// <summary>操作のときに要素を返す。未構築なら null。</summary>
            public Func<VisualElement> Resolve;

            /// <summary>今の要素。未構築なら null。</summary>
            public VisualElement Element => Resolve?.Invoke();

            /// <summary>表示の下準備。表示を変えたら true を返す。</summary>
            public Func<bool> PrepareReveal;

            /// <summary>既定の読み取りで足りないときだけ登録する。</summary>
            public Func<string> Getter;

            /// <summary>既定の書き込みで足りないときだけ登録する。成功で null、失敗で理由。</summary>
            public Func<string, string> Setter;

            /// <summary>作り直しで消す範囲。固定の項目は null。</summary>
            public string Scope;

            /// <summary>登録元（型名.メンバー名）。検査の表示用。</summary>
            public string Source;
        }

        /// <summary>RegisterObject に渡したオブジェクト 1 件。検査で付け忘れを探すのに使う。</summary>
        public sealed class ObjectEntry
        {
            public string PanelId;
            public object Instance;
        }

        private readonly Dictionary<string, PanelEntry> _panels
            = new Dictionary<string, PanelEntry>(StringComparer.Ordinal);
        private readonly List<PanelEntry> _panelOrder = new List<PanelEntry>();

        private readonly Dictionary<string, ControlEntry> _controls
            = new Dictionary<string, ControlEntry>(StringComparer.Ordinal);
        private readonly List<ControlEntry> _controlOrder = new List<ControlEntry>();

        private readonly List<ObjectEntry> _objects = new List<ObjectEntry>();
        private readonly List<string>      _errors  = new List<string>();

        /// <summary>意図して登録しないセクション 1 件。検査で未登録に数えない。</summary>
        public sealed class ExcludedSection
        {
            public VisualElement Section;
            public string        Reason;
        }

        private readonly List<ExcludedSection> _excluded = new List<ExcludedSection>();

        /// <summary>中身がデータ行のコンテナ 1 件（UiControl の Rows）。検査はこの中を見ない。</summary>
        public sealed class RowContainer
        {
            public string              PanelId;
            public Func<VisualElement> Resolve;
            public string              Source;
        }

        private readonly List<RowContainer> _rowContainers = new List<RowContainer>();

        /// <summary>中身がデータ行のコンテナ。</summary>
        /// <summary>
        /// データ行のコンテナ。属性（Rows）で宣言したものに、動的な項目の出どころが
        /// 今持っている行コンテナ（UiDynamicControls.AddRows）を続けたもの。
        /// </summary>
        public IReadOnlyList<RowContainer> RowContainers
        {
            get
            {
                if (_dynamicSources.Count == 0) return _rowContainers;
                var list = new List<RowContainer>(_rowContainers);
                foreach (var ds in _dynamicSources)
                    foreach (var r in ds.Controls.Rows)
                    {
                        var captured = r;
                        list.Add(new RowContainer
                        {
                            PanelId = ds.PanelId,
                            Resolve = () => captured,
                            Source  = ds.Source + "（行）",
                        });
                    }
                return list;
            }
        }

        /// <summary>意図して登録しないセクション。</summary>
        public IReadOnlyList<ExcludedSection> ExcludedSections => _excluded;

        /// <summary>
        /// セクションを意図して登録しないことを明示する（UiControl の Ignore と同じ考え方）。
        /// 理由は検査の報告に出す。
        /// </summary>
        public void ExcludeSection(VisualElement section, string reason)
        {
            if (section == null)
            {
                Reject($"対象外にするセクションが null です（{reason}）");
                return;
            }
            _excluded.Add(new ExcludedSection { Section = section, Reason = reason ?? "" });
        }

        /// <summary>登録順のパネル。</summary>
        public IReadOnlyList<PanelEntry> Panels => _panelOrder;

        /// <summary>
        /// 項目。固定の項目（登録順）に、動的な項目の出どころの今の中身を続けたもの。
        /// 動的な項目は呼ぶたびに作り直すので、参照を持ち続けないこと。
        /// </summary>
        public IReadOnlyList<ControlEntry> Controls
        {
            get
            {
                if (_dynamicSources.Count == 0) return _controlOrder;
                var list = new List<ControlEntry>(_controlOrder);
                foreach (var ds in _dynamicSources)
                    foreach (var e in ds.Controls.Entries)
                        if (ds.Group == null || e.Group == ds.Group)
                            list.Add(MakeDynamicEntry(ds, e));
                return list;
            }
        }

        // ================================================================
        // 動的な項目（UiDynamicControls）
        // ================================================================

        /// <summary>動的な項目の出どころ 1 件。</summary>
        private sealed class DynamicSource
        {
            public string            PanelId;
            public string            IdPrefix;
            public UiDynamicControls Controls;
            /// <summary>このパネルが見る組。null なら組を問わない。</summary>
            public string            Group;
            public string            Source;

            public string Head => PanelId + "." + (string.IsNullOrEmpty(IdPrefix) ? "" : IdPrefix + ".");
        }

        private readonly List<DynamicSource> _dynamicSources = new List<DynamicSource>();

        private static ControlEntry MakeDynamicEntry(DynamicSource ds, UiDynamicControls.Entry e)
        {
            string localId = e.Id;
            return new ControlEntry
            {
                Id          = ds.Head + localId,
                PanelId     = ds.PanelId,
                Description = e.Description,
                Safety      = e.Safety,
                // 作り直しで要素が差し替わるので、操作のたびに引き直す。
                Resolve     = () => ds.Controls.Find(localId, ds.Group)?.Element,
                PrepareReveal = e.Reveal == null
                    ? (Func<bool>)null
                    : () => ds.Controls.Find(localId, ds.Group)?.Reveal?.Invoke() ?? false,
                Source      = $"{ds.Source}[{localId}]",
            };
        }

        /// <summary>
        /// 動的な項目の ID から所属パネルを返す。まだ作られていない（別のモードを表示中など）
        /// 項目でも、パネルを開けば作られるので、uiReveal はこれでパネルを開いてから引き直す。
        /// </summary>
        public bool TryGetDynamicPanelFor(string controlId, out PanelEntry panel)
        {
            panel = null;
            if (string.IsNullOrEmpty(controlId)) return false;
            foreach (var ds in _dynamicSources)
                if (controlId.StartsWith(ds.Head, StringComparison.Ordinal))
                    return _panels.TryGetValue(ds.PanelId, out panel);
            return false;
        }

        /// <summary>RegisterObject に渡したオブジェクト。</summary>
        public IReadOnlyList<ObjectEntry> Objects => _objects;

        /// <summary>登録を拒否した理由。</summary>
        public IReadOnlyList<string> Errors => _errors;

        // ================================================================
        // パネル
        // ================================================================

        /// <summary>
        /// パネルを登録する。ID が空・重複、section / show が無いときは登録せず Errors へ積む。
        /// </summary>
        public bool RegisterPanel(string id, string description, VisualElement section, Action show)
        {
            if (string.IsNullOrEmpty(id))
                return Reject("パネル ID が空です");
            if (_panels.ContainsKey(id))
                return Reject($"パネル ID が重複しています: {id}");
            if (section == null)
                return Reject($"パネルのセクションがありません: {id}");
            if (show == null)
                return Reject($"パネルを開く処理がありません: {id}");

            var p = new PanelEntry
            {
                Id          = id,
                Description = description ?? "",
                Section     = section,
                Show        = show,
            };
            _panels.Add(id, p);
            _panelOrder.Add(p);
            return true;
        }

        public bool TryGetPanel(string id, out PanelEntry panel)
        {
            panel = null;
            return !string.IsNullOrEmpty(id) && _panels.TryGetValue(id, out panel);
        }

        // ================================================================
        // 項目
        // ================================================================

        /// <summary>
        /// 項目を 1 件登録する。所属パネルは先に登録しておくこと。
        /// ID が空・重複、パネル未登録、resolve が無いときは登録せず Errors へ積む。
        /// </summary>
        public bool RegisterControl(
            string id,
            string panelId,
            Func<VisualElement> resolve,
            string description,
            UiSafety safety = UiSafety.Unspecified,
            Func<bool> prepareReveal = null,
            Func<string> getter = null,
            Func<string, string> setter = null,
            string scope = null,
            string source = null)
        {
            if (string.IsNullOrEmpty(id))
                return Reject($"項目 ID が空です（{source}）");
            if (_controls.ContainsKey(id))
                return Reject($"項目 ID が重複しています: {id}（{source}）");
            if (string.IsNullOrEmpty(panelId) || !_panels.ContainsKey(panelId))
                return Reject($"項目 {id} の所属パネルが登録されていません: {panelId}");
            if (resolve == null)
                return Reject($"項目の要素を返す処理がありません: {id}");

            var c = new ControlEntry
            {
                Id            = id,
                PanelId       = panelId,
                Description   = description ?? "",
                Safety        = safety,
                Resolve       = resolve,
                PrepareReveal = prepareReveal,
                Getter        = getter,
                Setter        = setter,
                Scope         = scope,
                Source        = source ?? "",
            };
            _controls.Add(id, c);
            _controlOrder.Add(c);
            return true;
        }

        public bool TryGetControl(string id, out ControlEntry control)
        {
            control = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (_controls.TryGetValue(id, out control)) return true;

            // 動的な項目は、今作られているものだけを引く。
            foreach (var ds in _dynamicSources)
            {
                string head = ds.Head;
                if (!id.StartsWith(head, StringComparison.Ordinal)) continue;
                var e = ds.Controls.Find(id.Substring(head.Length), ds.Group);
                if (e == null) continue;
                control = MakeDynamicEntry(ds, e);
                return true;
            }
            return false;
        }

        /// <summary>scope 付きで登録した項目を全部消す。作り直す前に呼ぶ。</summary>
        public void ClearScope(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return;
            for (int i = _controlOrder.Count - 1; i >= 0; i--)
            {
                var c = _controlOrder[i];
                if (c.Scope != scope) continue;
                _controls.Remove(c.Id);
                _controlOrder.RemoveAt(i);
            }
        }

        // ================================================================
        // 属性による一括登録
        // ================================================================

        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private const BindingFlags MethodFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// instance の UiControlAttribute 付きメンバーを panelId の項目として登録する。
        /// 基底クラスのメンバーも見る。Ignore のものは登録しない。
        /// UiNestedAttribute 付きのフィールドは、その中身も同じパネルへ取り込む。
        /// idPrefix を渡すと ID を "&lt;パネル ID&gt;.&lt;idPrefix&gt;.&lt;Id&gt;" にする
        /// （同じクラスのサブパネルを 1 つのパネルに 2 つ置くときに使う）。
        /// </summary>
        public void RegisterObject(string panelId, object instance, string idPrefix = "", string dynamicGroup = null)
            => RegisterObjectCore(panelId, instance, idPrefix ?? "", topLevel: true, outerReveal: null,
                                  dynamicGroup: dynamicGroup);

        /// <param name="outerReveal">外側（UiNested を付けた側）の表示の下準備。無ければ null。</param>
        /// <param name="dynamicGroup">UiDynamicControls のうちこのパネルが見る組。null なら組を問わない。</param>
        private void RegisterObjectCore(
            string panelId, object instance, string idPrefix, bool topLevel, Func<bool> outerReveal,
            string dynamicGroup = null)
        {
            if (instance == null)
            {
                Reject($"登録するオブジェクトが null です: {panelId}");
                return;
            }
            if (!_panels.ContainsKey(panelId))
            {
                Reject($"オブジェクト {instance.GetType().Name} の所属パネルが登録されていません: {panelId}");
                return;
            }

            // 検査（付け忘れ）の対象はパネルへ直接渡したオブジェクト。入れ子は検査が中まで辿る。
            if (topLevel)
                _objects.Add(new ObjectEntry { PanelId = panelId, Instance = instance });

            for (var t = instance.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(MemberFlags))
                {
                    // 作り直すたびに中身が変わる項目の置き場。
                    if (f.FieldType == typeof(UiDynamicControls))
                    {
                        if (f.GetValue(instance) is UiDynamicControls dyn)
                        {
                            _dynamicSources.Add(new DynamicSource
                            {
                                PanelId  = panelId,
                                IdPrefix = idPrefix,
                                Controls = dyn,
                                Group    = dynamicGroup,
                                Source   = $"{t.Name}.{f.Name}",
                            });
                        }
                        else
                        {
                            Reject($"UiDynamicControls のフィールドが null です: {t.Name}.{f.Name}");
                        }
                        continue;
                    }

                    var nested = f.GetCustomAttribute<UiNestedAttribute>();
                    if (nested != null)
                    {
                        var inner = f.GetValue(instance);
                        if (inner == null)
                        {
                            if (!nested.Optional)
                                Reject($"UiNested のフィールドが null です: {t.Name}.{f.Name}");
                            continue;
                        }

                        Func<bool> nestedReveal = null;
                        if (!string.IsNullOrEmpty(nested.Reveal)
                            && !TryBind(instance, nested.Reveal, typeof(bool), Type.EmptyTypes,
                                        $"{t.Name}.{f.Name}", out nestedReveal))
                            continue;

                        RegisterObjectCore(panelId, inner, JoinId(idPrefix, nested.Prefix),
                            topLevel: false, outerReveal: CombineReveal(outerReveal, nestedReveal),
                            dynamicGroup: dynamicGroup);
                        continue;
                    }

                    var attr = f.GetCustomAttribute<UiControlAttribute>();
                    if (attr == null) continue;
                    if (attr.Ignore)
                    {
                        if (attr.Rows && typeof(VisualElement).IsAssignableFrom(f.FieldType))
                        {
                            var rowField = f;
                            _rowContainers.Add(new RowContainer
                            {
                                PanelId = panelId,
                                Resolve = () => rowField.GetValue(instance) as VisualElement,
                                Source  = $"{t.Name}.{f.Name}",
                            });
                        }
                        continue;
                    }

                    // 固定個数の部品を配列で持つ場合（ソースのスロットなど）。ID の {0} に 1 始まりの番号が入る。
                    if (f.FieldType.IsArray && typeof(VisualElement).IsAssignableFrom(f.FieldType.GetElementType()))
                    {
                        RegisterArrayMember(panelId, idPrefix, instance, t, f, attr, outerReveal);
                        continue;
                    }
                    var field = f;
                    RegisterMember(panelId, idPrefix, instance, t, f.Name, f.FieldType, attr,
                        () => field.GetValue(instance) as VisualElement, outerReveal);
                }

                foreach (var p in t.GetProperties(MemberFlags))
                {
                    var attr = p.GetCustomAttribute<UiControlAttribute>();
                    if (attr == null) continue;
                    if (attr.Ignore)
                    {
                        if (attr.Rows && p.CanRead && p.GetIndexParameters().Length == 0
                            && typeof(VisualElement).IsAssignableFrom(p.PropertyType))
                        {
                            var rowProp = p;
                            _rowContainers.Add(new RowContainer
                            {
                                PanelId = panelId,
                                Resolve = () => rowProp.GetValue(instance) as VisualElement,
                                Source  = $"{t.Name}.{p.Name}",
                            });
                        }
                        continue;
                    }
                    if (!p.CanRead || p.GetIndexParameters().Length != 0)
                    {
                        Reject($"UiControl は読める引数なしのプロパティにだけ付けられます: {t.Name}.{p.Name}");
                        continue;
                    }
                    var prop = p;
                    RegisterMember(panelId, idPrefix, instance, t, p.Name, p.PropertyType, attr,
                        () => prop.GetValue(instance) as VisualElement, outerReveal);
                }
            }
        }

        /// <summary>外側の下準備 → 内側の下準備の順に呼ぶ。どちらかが表示を変えたら true。</summary>
        private static Func<bool> CombineReveal(Func<bool> outer, Func<bool> inner)
        {
            if (outer == null) return inner;
            if (inner == null) return outer;
            return () =>
            {
                bool a = outer();
                bool b = inner();
                return a || b;
            };
        }

        /// <summary>
        /// VisualElement の配列フィールドを、要素ごとの項目として登録する。
        /// ID と説明の "{0}" に 1 始まりの番号を入れる。要素は操作のときに配列から読む。
        /// 配列の長さは登録時の長さ（固定個数のスロット用。行が増減するものは Rows を使う）。
        /// Getter / Setter は要素ごとに変えられないので受け付けない。Reveal は全要素で共通。
        /// </summary>
        private void RegisterArrayMember(
            string panelId, string idPrefix, object instance, Type declaring, FieldInfo field,
            UiControlAttribute attr, Func<bool> outerReveal)
        {
            string source = $"{declaring.Name}.{field.Name}";
            if (string.IsNullOrEmpty(attr.Id) || !attr.Id.Contains("{0}"))
            {
                Reject($"配列の UiControl は ID に {{0}} を含めること: {source}");
                return;
            }
            if (!string.IsNullOrEmpty(attr.Getter) || !string.IsNullOrEmpty(attr.Setter))
            {
                Reject($"配列の UiControl に Getter / Setter は付けられません: {source}");
                return;
            }
            if (!(field.GetValue(instance) is Array arr))
            {
                Reject($"配列が null です: {source}");
                return;
            }

            Func<bool> reveal = null;
            if (!string.IsNullOrEmpty(attr.Reveal)
                && !TryBind(instance, attr.Reveal, typeof(bool), Type.EmptyTypes, source, out reveal))
                return;

            for (int i = 0; i < arr.Length; i++)
            {
                int    idx = i;
                string n   = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                RegisterControl(
                    panelId + "." + JoinId(idPrefix, attr.Id.Replace("{0}", n)), panelId,
                    () => (field.GetValue(instance) as Array)?.GetValue(idx) as VisualElement,
                    (attr.Description ?? "").Replace("{0}", n), attr.Safety,
                    CombineReveal(outerReveal, reveal), null, null,
                    scope: null, source: $"{source}[{i}]");
            }
        }

        private static string JoinId(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + "." + b;
        }

        private void RegisterMember(
            string panelId, string idPrefix, object instance, Type declaring, string memberName, Type memberType,
            UiControlAttribute attr, Func<VisualElement> resolve, Func<bool> outerReveal)
        {
            string source = $"{declaring.Name}.{memberName}";

            if (!typeof(VisualElement).IsAssignableFrom(memberType))
            {
                Reject($"UiControl は VisualElement 型のメンバーにだけ付けられます: {source}");
                return;
            }
            if (string.IsNullOrEmpty(attr.Id))
            {
                Reject($"UiControl の ID が空です: {source}");
                return;
            }

            Func<bool>           reveal = null;
            Func<string>         getter = null;
            Func<string, string> setter = null;

            if (!string.IsNullOrEmpty(attr.Reveal)
                && !TryBind(instance, attr.Reveal, typeof(bool), Type.EmptyTypes, source, out reveal))
                return;
            if (!string.IsNullOrEmpty(attr.Getter)
                && !TryBind(instance, attr.Getter, typeof(string), Type.EmptyTypes, source, out getter))
                return;
            if (!string.IsNullOrEmpty(attr.Setter)
                && !TryBind(instance, attr.Setter, typeof(string), new[] { typeof(string) }, source, out setter))
                return;

            RegisterControl(
                panelId + "." + JoinId(idPrefix, attr.Id), panelId, resolve, attr.Description, attr.Safety,
                CombineReveal(outerReveal, reveal), getter, setter, scope: null, source: source);
        }

        /// <summary>名前で引いたインスタンスメソッドを、戻り値と引数の形を確かめてデリゲートにする。</summary>
        private bool TryBind<TDelegate>(
            object instance, string methodName, Type returnType, Type[] parameters, string source,
            out TDelegate bound) where TDelegate : Delegate
        {
            bound = null;
            var m = instance.GetType().GetMethod(methodName, MethodFlags, null, parameters, null);
            if (m == null || m.ReturnType != returnType)
                return Reject($"{source} の {methodName} が見つからないか形が合いません"
                            + $"（{returnType.Name} {methodName}({parameters.Length} 引数) が要る）");

            bound = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), instance, m);
            return true;
        }

        private bool Reject(string reason)
        {
            _errors.Add(reason);
            Debug.LogError($"[UiAutomation] 登録を拒否: {reason}");
            return false;
        }
    }
}
