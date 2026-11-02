// MeshModifierRegistry.cs
// 処理列の種類（IMeshModifier）の登録表と、引数の文字列の往復。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.2（段 1）
//
// 【発見】
//   読み込まれている全アセンブリ（AppDomain.CurrentDomain.GetAssemblies）から、
//   IMeshModifier を実装し、抽象でなく、public な引数なしコンストラクタを持つ型を集める。
//   種類名は PLMeshModifier 属性の Kind。本体を触らずに別パッケージからも足せる。
//   最初の参照で 1 回だけ走査する（アセンブリの再読込はドメインの作り直しで表も消える）。
//
// 【問題として積むもの（Problems）】
//   属性の欠け・種類名の空や重複・コマンドの action 名との衝突・引数なしコンストラクタの欠け・
//   PLParam の付け忘れ・読み書きできない引数・扱えない型。問題のある型は登録しない。
//
// 【引数の文字列】
//   キーはプロパティ名の先頭を小文字にしたもの。値は PanelCommandFactory.TryFormatValue /
//   TryParseValue（ToArgs / Create と同じ規則）、入れ子は FlattenNestedValue / TryBuildNestedValue。
//   処理範囲は "scope." のキー（MeshModifierScope）。
//   FromArgs は未知のキーと Required の欠けを失敗にする。

using System;
using System.Collections.Generic;
using System.Reflection;
using Poly_Ling.Data;

namespace Poly_Ling.Modifiers
{
    /// <summary>種類 1 つぶんの登録内容。</summary>
    public sealed class MeshModifierInfo
    {
        public string             Kind;
        public string             DisplayName;
        public string             Category;
        public string             Description;
        public MeshModifierChange Change;
        public Type               Type;
        public bool               HasPositionsPath;
        /// <summary>面構成を変える種類がドラッグ中の追随の口（IMeshModifierDragFollow）を持つか（段 8）。</summary>
        public bool               HasDragFollow;
        /// <summary>末尾固定（PLMeshModifierAttribute.PinnedLast。段 8）。</summary>
        public bool               PinnedLast;
        public List<MeshModifierParamInfo> Params = new List<MeshModifierParamInfo>();
    }

    /// <summary>引数 1 つぶんの宣言。</summary>
    public sealed class MeshModifierParamInfo
    {
        public string           Key;
        public PropertyInfo     Property;
        public PLParamAttribute Attr;
        public Type             Type => Property.PropertyType;
    }

    public static class MeshModifierRegistry
    {
        private static Dictionary<string, MeshModifierInfo> _byKind;
        private static List<MeshModifierInfo> _ordered;
        private static List<string> _problems;
        private static readonly object _lock = new object();

        /// <summary>登録された種類（種類名の順）。</summary>
        public static IReadOnlyList<MeshModifierInfo> All { get { Ensure(); return _ordered; } }

        /// <summary>発見のときに見つけた問題。</summary>
        public static IReadOnlyList<string> Problems { get { Ensure(); return _problems; } }

        /// <summary>種類名から引く。無ければ null。</summary>
        public static MeshModifierInfo Find(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            Ensure();
            return _byKind.TryGetValue(kind, out var info) ? info : null;
        }

        /// <summary>既定値のインスタンスを作る。種類が無ければ null。</summary>
        public static IMeshModifier Create(string kind)
        {
            var info = Find(kind);
            return info == null ? null : (IMeshModifier)Activator.CreateInstance(info.Type);
        }

        // ================================================================
        // 発見
        // ================================================================

        private static void Ensure()
        {
            if (_byKind != null) return;
            lock (_lock)
            {
                if (_byKind != null) return;

                var byKind   = new Dictionary<string, MeshModifierInfo>(StringComparer.Ordinal);
                var problems = new List<string>();

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch (Exception) { continue; }
                    if (types == null) continue;

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || t.IsInterface) continue;
                        if (!typeof(IMeshModifier).IsAssignableFrom(t)) continue;

                        var info = Inspect(t, problems);
                        if (info == null) continue;

                        if (byKind.TryGetValue(info.Kind, out var prev))
                        {
                            problems.Add($"{t.FullName}: 種類名 \"{info.Kind}\" が {prev.Type.FullName} と重複（後の方を外した）");
                            continue;
                        }
                        byKind[info.Kind] = info;
                    }
                }

                var ordered = new List<MeshModifierInfo>(byKind.Values);
                ordered.Sort((a, b) => string.CompareOrdinal(a.Kind, b.Kind));

                _problems = problems;
                _ordered  = ordered;
                _byKind   = byKind;
            }
        }

        private static MeshModifierInfo Inspect(Type t, List<string> problems)
        {
            var attr = t.GetCustomAttribute<PLMeshModifierAttribute>(inherit: false);
            if (attr == null) { problems.Add($"{t.FullName}: PLMeshModifier が付いていない"); return null; }
            if (string.IsNullOrEmpty(attr.Kind)) { problems.Add($"{t.FullName}: 種類名が空"); return null; }
            if (PanelCommandFactory.ResolveType(attr.Kind) != null)
            { problems.Add($"{t.FullName}: 種類名 \"{attr.Kind}\" がコマンドの action 名と同じ"); return null; }
            if (t.GetConstructor(Type.EmptyTypes) == null)
            { problems.Add($"{t.FullName}: public な引数なしコンストラクタが無い"); return null; }

            var info = new MeshModifierInfo
            {
                Kind             = attr.Kind,
                DisplayName      = string.IsNullOrEmpty(attr.DisplayName) ? attr.Kind : attr.DisplayName,
                Category         = attr.Category,
                Description      = attr.Description,
                Change           = attr.Change,
                Type             = t,
                HasPositionsPath = typeof(IMeshModifierPositions).IsAssignableFrom(t),
                HasDragFollow    = typeof(IMeshModifierDragFollow).IsAssignableFrom(t),
                PinnedLast       = attr.PinnedLast,
            };

            bool ok = true;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pa = p.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (pa == null) { problems.Add($"{t.FullName}.{p.Name}: PLParam が付いていない"); ok = false; continue; }
                if (pa.Ignore) continue;
                if (!p.CanRead || !p.CanWrite || p.GetSetMethod() == null)
                { problems.Add($"{t.FullName}.{p.Name}: 引数は public に読み書きできるプロパティにする"); ok = false; continue; }
                if (!PanelCommandFactory.IsSchemaRepresentable(p.PropertyType))
                { problems.Add($"{t.FullName}.{p.Name}: 型 {p.PropertyType.Name} は引数の文字列にできない"); ok = false; continue; }

                string key = Camel(p.Name);
                if (key.StartsWith(MeshModifierScope.KeyPrefix, StringComparison.Ordinal) || key == "scope")
                { problems.Add($"{t.FullName}.{p.Name}: キー \"{key}\" は処理範囲のキーと重なる"); ok = false; continue; }

                info.Params.Add(new MeshModifierParamInfo { Key = key, Property = p, Attr = pa });
            }
            if (!ok) return null;
            if (info.Change == MeshModifierChange.Topology && info.HasPositionsPath)
                problems.Add($"{t.FullName}: 面構成を変える種類が位置の口を持っている（位置の口は使われない）");
            if (info.Change == MeshModifierChange.PositionsOnly && info.HasDragFollow)
                problems.Add($"{t.FullName}: 位置だけの種類が追随の口（IMeshModifierDragFollow）を持っている（使われない。位置の口を使う）");
            return info;
        }

        private static string Camel(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ================================================================
        // 引数の文字列
        // ================================================================

        /// <summary>種類の引数と処理範囲を文字列の対にする。</summary>
        public static Dictionary<string, string> ToArgs(IMeshModifier modifier, MeshModifierScope scope)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (modifier != null)
            {
                var info = FindByType(modifier.GetType());
                if (info != null)
                {
                    foreach (var p in info.Params)
                    {
                        object v;
                        try { v = p.Property.GetValue(modifier); }
                        catch (Exception) { continue; }
                        if (v == null) continue;

                        if (PanelCommandFactory.TryFormatValue(v, out string s)) { result[p.Key] = s; continue; }
                        PanelCommandFactory.FlattenNestedValue(v, p.Type, p.Key, result);
                    }
                }
            }
            WriteScope(scope ?? MeshModifierScope.All(), result);
            return result;
        }

        /// <summary>文字列の対から種類のインスタンスと処理範囲を作る。</summary>
        public static bool FromArgs(
            string kind, IReadOnlyDictionary<string, string> args,
            out IMeshModifier modifier, out MeshModifierScope scope, out string error)
        {
            modifier = null;
            scope    = null;
            error    = null;

            var info = Find(kind);
            if (info == null) { error = $"種類 \"{kind}\" は登録されていない"; return false; }

            var m = (IMeshModifier)Activator.CreateInstance(info.Type);
            var known = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in info.Params)
            {
                known.Add(p.Key);
                if (args != null && args.TryGetValue(p.Key, out var raw) && raw != null)
                {
                    if (!PanelCommandFactory.TryParseValue(raw, p.Type, out object v, out string why))
                    { error = $"\"{p.Key}\" を {p.Type.Name} にできない（{why}）"; return false; }
                    p.Property.SetValue(m, v);
                    continue;
                }

                string prefix = p.Key + ".";
                bool nested = false;
                if (args != null)
                    foreach (var kv in args)
                        if (kv.Key != null && kv.Key.StartsWith(prefix, StringComparison.Ordinal)) { nested = true; break; }
                if (nested)
                {
                    if (!PanelCommandFactory.TryBuildNestedValue(p.Type, p.Key, args, out object nv, out string reason))
                    { error = $"\"{p.Key}\": {reason}"; return false; }
                    p.Property.SetValue(m, nv);
                    continue;
                }

                if (p.Attr.Required) { error = $"引数 \"{p.Key}\" が要る"; return false; }
            }

            if (args != null)
                foreach (var kv in args)
                {
                    if (kv.Key == null) continue;
                    if (kv.Key.StartsWith(MeshModifierScope.KeyPrefix, StringComparison.Ordinal)) continue;
                    if (known.Contains(kv.Key)) continue;
                    bool underKnown = false;
                    foreach (var k in known)
                        if (kv.Key.StartsWith(k + ".", StringComparison.Ordinal)) { underKnown = true; break; }
                    if (!underKnown) { error = $"種類 \"{kind}\" に引数 \"{kv.Key}\" は無い"; return false; }
                }

            if (!ReadScope(args, out scope, out error)) return false;

            modifier = m;
            return true;
        }

        private static MeshModifierInfo FindByType(Type t)
        {
            Ensure();
            foreach (var info in _ordered) if (info.Type == t) return info;
            return null;
        }

        /// <summary>処理範囲だけを "scope." のキーの文字列にする（ToArgs と同じ書き方）。画面の「選択から登録」が使う。</summary>
        public static Dictionary<string, string> ScopeToArgs(MeshModifierScope scope)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            WriteScope(scope ?? MeshModifierScope.All(), result);
            return result;
        }

        private static void WriteScope(MeshModifierScope s, Dictionary<string, string> dst)
        {
            Put(dst, MeshModifierScope.KeyKind, s.Kind);
            switch (s.Kind)
            {
                case MeshModifierScopeKind.Ids:
                    if (s.VertexIds.Length > 0)     Put(dst, MeshModifierScope.KeyVertexIds,     s.VertexIds);
                    if (s.EdgeVertexIds.Length > 0) Put(dst, MeshModifierScope.KeyEdgeVertexIds, s.EdgeVertexIds);
                    if (s.FaceSizes.Length > 0)
                    {
                        Put(dst, MeshModifierScope.KeyFaceVertexIds, s.FaceVertexIds);
                        Put(dst, MeshModifierScope.KeyFaceSizes,     s.FaceSizes);
                    }
                    break;
                case MeshModifierScopeKind.PartsSet:
                    Put(dst, MeshModifierScope.KeyPartsSet, s.PartsSetName ?? "");
                    break;
            }
        }

        private static void Put(Dictionary<string, string> dst, string key, object v)
        {
            if (PanelCommandFactory.TryFormatValue(v, out string s)) dst[key] = s;
        }

        private static bool ReadScope(IReadOnlyDictionary<string, string> args, out MeshModifierScope scope, out string error)
        {
            scope = new MeshModifierScope();
            error = null;
            if (args == null) return true;

            foreach (var kv in args)
            {
                if (kv.Key == null || !kv.Key.StartsWith(MeshModifierScope.KeyPrefix, StringComparison.Ordinal)) continue;
                switch (kv.Key)
                {
                    case MeshModifierScope.KeyKind:
                    case MeshModifierScope.KeyVertexIds:
                    case MeshModifierScope.KeyEdgeVertexIds:
                    case MeshModifierScope.KeyFaceVertexIds:
                    case MeshModifierScope.KeyFaceSizes:
                    case MeshModifierScope.KeyPartsSet:
                        break;
                    default:
                        error = $"処理範囲に引数 \"{kv.Key}\" は無い";
                        return false;
                }
            }

            if (args.TryGetValue(MeshModifierScope.KeyKind, out var rk) && rk != null)
            {
                if (!PanelCommandFactory.TryParseValue(rk, typeof(MeshModifierScopeKind), out object kv, out string why))
                { error = $"\"{MeshModifierScope.KeyKind}\" を読めない（{why}）"; return false; }
                scope.Kind = (MeshModifierScopeKind)kv;
                if (!Enum.IsDefined(typeof(MeshModifierScopeKind), scope.Kind))
                { error = $"\"{MeshModifierScope.KeyKind}\" の値 {rk} は範囲の種類に無い"; return false; }
            }

            if (!ReadInts(args, MeshModifierScope.KeyVertexIds,     out scope.VertexIds,     out error)) return false;
            if (!ReadInts(args, MeshModifierScope.KeyEdgeVertexIds, out scope.EdgeVertexIds, out error)) return false;
            if (!ReadInts(args, MeshModifierScope.KeyFaceVertexIds, out scope.FaceVertexIds, out error)) return false;
            if (!ReadInts(args, MeshModifierScope.KeyFaceSizes,     out scope.FaceSizes,     out error)) return false;
            if (args.TryGetValue(MeshModifierScope.KeyPartsSet, out var pn) && pn != null) scope.PartsSetName = pn;

            if (scope.Kind == MeshModifierScopeKind.PartsSet && string.IsNullOrEmpty(scope.PartsSetName))
            { error = $"範囲がパーツ選択辞書なのに \"{MeshModifierScope.KeyPartsSet}\" が無い"; return false; }
            return true;
        }

        private static bool ReadInts(IReadOnlyDictionary<string, string> args, string key, out int[] value, out string error)
        {
            value = Array.Empty<int>();
            error = null;
            if (!args.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw)) return true;
            if (!PanelCommandFactory.TryParseValue(raw, typeof(int[]), out object v, out string why))
            { error = $"\"{key}\" を読めない（{why}）"; return false; }
            value = (int[])v;
            return true;
        }
    }
}
