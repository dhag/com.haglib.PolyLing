// PanelCommandMeshRefs.cs
// PLParam の印（IsMeshRef / ProfileRole / RebuildRole）が付いたパラメータのキーを列挙する。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   ObjectGroup は生成コマンドを ToArgs の文字列で保存する。その中には
//   「どの描画オブジェクトを対象にするか」を MeshContextList の索引で持つものがある。
//     CreatePlaceObjectCommand.SourceMasterIndices   … 藤壺の配置元
//     PrimitivePlacement.AddTargetIndex              … 「既存へ追加」の追加先
//     ApplyBlendCommand.DestMasterIndex / SrcMasterIndices … ブレンドの宛先とソース
//   索引はリストの挿入・削除・並べ替えでずれるので、保存した値をそのまま
//   Create へ渡すと別のオブジェクトを指す。再構築の直前に ObjectId から
//   引き直すために、どのキーがそれに当たるかを知る必要がある。
//
// 【コマンド種別ごとの手書き表を作らない】
//   種別ごとに「このコマンドのこのキーが索引」と書くと、コマンドを足すたびに
//   その表も足すことになり必ずずれる。PLParam が全コマンドに付いている以上、
//   印は属性側に持たせ、ここは属性を読むだけにする。
//
// 【走査の規則は ToArgs と同じ】
//   コンストラクタ引数 → 対応するプロパティ → 入れ子はドット区切り、の順。
//   ToArgs（PanelCommandFactory.cs:249）と同じ道を通らないと、
//   出したキーと引ける・引けないが食い違う。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Poly_Ling.Data
{
    public static partial class PanelCommandFactory
    {
        /// <summary>索引で描画オブジェクトを指すパラメータ 1 件。</summary>
        public readonly struct MeshRefKey
        {
            /// <summary>ToArgs / Create で使うキー。入れ子はドット区切り。</summary>
            public readonly string Key;

            /// <summary>値が索引の配列（int[]）なら true。単独の索引（int）なら false。</summary>
            public readonly bool IsArray;

            /// <summary>
            /// 同じ並びでモデル索引を持つパラメータのキー。空＝コマンド自身のモデル内。
            /// PLParam.MeshRefModelKey をキーへ変換したもの。
            /// </summary>
            public readonly string ModelKey;

            /// <summary>
            /// 指すオブジェクトを書き換えるか読むだけか（PLParam.MeshRefAccess）。
            /// </summary>
            public readonly PLMeshRefAccess Access;

            /// <summary>
            /// Access = Write のとき、書き込み先になる条件（PLParam.WriteWhen）。空なら無条件。
            /// </summary>
            public readonly string WriteWhen;

            public MeshRefKey(string key, bool isArray, string modelKey, PLMeshRefAccess access, string writeWhen)
            { Key = key; IsArray = isArray; ModelKey = modelKey ?? ""; Access = access; WriteWhen = writeWhen ?? ""; }

            /// <summary>別モデルを指しうるか。</summary>
            public bool HasModelKey => !string.IsNullOrEmpty(ModelKey);

            public override string ToString() => IsArray ? Key + "[]" : Key;
        }

        /// <summary>
        /// このコマンド型で IsMeshRef が付いたパラメータのキーを、宣言順で返す。
        /// 1 件も無ければ空を返す。
        /// </summary>
        public static List<MeshRefKey> MeshRefKeys(Type t)
        {
            var result = new List<MeshRefKey>();
            if (t == null) return result;

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                if (IsNestedType(prop.PropertyType))
                {
                    CollectNestedMeshRefs(prop.PropertyType, KeyOf(t, prop), 1, result);
                    continue;
                }

                if (!attr.IsMeshRef) continue;
                if (!IsMeshRefType(prop.PropertyType)) continue;

                result.Add(new MeshRefKey(
                    KeyOf(t, prop),
                    prop.PropertyType == typeof(int[]),
                    ResolveModelKey(t, attr),
                    attr.MeshRefAccess,
                    attr.WriteWhen));
            }

            return result;
        }

        /// <summary>
        /// MeshRefModelKey（プロパティ名）を、届くキーへ変換する。
        /// 指定が無い、または対応するプロパティが無いときは空を返す
        /// （＝コマンド自身のモデル内として扱う）。
        /// </summary>
        private static string ResolveModelKey(Type t, PLParamAttribute attr)
        {
            if (attr == null || !attr.HasMeshRefModelKey) return "";

            PropertyInfo mp = FindProperty(t, attr.MeshRefModelKey);
            if (mp == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[PanelCommandFactory] {t.Name}: MeshRefModelKey \"{attr.MeshRefModelKey}\" に対応するプロパティが無い");
                return "";
            }
            if (mp.PropertyType != typeof(int[]))
            {
                UnityEngine.Debug.LogWarning(
                    $"[PanelCommandFactory] {t.Name}.{mp.Name}: MeshRefModelKey は int[] でなければならない");
                return "";
            }
            return KeyOf(t, mp);
        }

        /// <summary>入れ子の中の IsMeshRef を、ドット区切りのキーで拾う。</summary>
        private static void CollectNestedMeshRefs(
            Type t, string prefix, int depth, List<MeshRefKey> dst)
        {
            if (t == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    CollectNestedMeshRefs(m.Type, key, depth + 1, dst);
                    continue;
                }

                if (!m.Attr.IsMeshRef) continue;
                if (!IsMeshRefType(m.Type)) continue;

                // 入れ子の中から別モデルを指すことは無い（相棒の配列も入れ子の中に
                // 無ければ対で書けない）。MeshRefModelKey は入れ子では受け付けない。
                if (m.Attr.HasMeshRefModelKey)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[PanelCommandFactory] {key}: 入れ子では MeshRefModelKey を使えない");
                }

                string ww = m.Attr.HasWriteWhen ? prefix + "." + m.Attr.WriteWhen : "";
                dst.Add(new MeshRefKey(key, m.Type == typeof(int[]), "", m.Attr.MeshRefAccess, ww));
            }
        }

        /// <summary>プロファイル本体を指すパラメータ 1 件。</summary>
        public readonly struct ProfileKey
        {
            /// <summary>ToArgs / Create で使うキー。入れ子はドット区切り。</summary>
            public readonly string Key;

            /// <summary>値の形。</summary>
            public readonly PLProfileRole Role;

            /// <summary>FlatLoops のとき、ループ開始位置のキー。</summary>
            public readonly string LoopStartsKey;

            /// <summary>FlatLoops のとき、穴フラグのキー。</summary>
            public readonly string LoopIsHoleKey;

            /// <summary>長辺を 1 にする等方スケールを掛けるか。</summary>
            public readonly bool Normalize;

            public ProfileKey(string key, PLProfileRole role,
                              string loopStartsKey, string loopIsHoleKey, bool normalize)
            {
                Key           = key;
                Role          = role;
                LoopStartsKey = loopStartsKey ?? "";
                LoopIsHoleKey = loopIsHoleKey ?? "";
                Normalize     = normalize;
            }

            public override string ToString() => $"{Key}({Role})";
        }

        /// <summary>
        /// このコマンド型で ProfileRole が付いたパラメータのキーを、宣言順で返す。
        /// 1 件も無ければ空を返す。走査規則は MeshRefKeys と同じ。
        /// </summary>
        public static List<ProfileKey> ProfileKeys(Type t)
        {
            var result = new List<ProfileKey>();
            if (t == null) return result;

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                if (IsNestedType(prop.PropertyType))
                {
                    CollectNestedProfiles(prop.PropertyType, KeyOf(t, prop), 1, result);
                    continue;
                }

                if (!attr.HasProfileRole) continue;

                result.Add(new ProfileKey(
                    KeyOf(t, prop), attr.ProfileRole,
                    ResolveSiblingKey(t, attr.ProfileLoopStartsKey, ""),
                    ResolveSiblingKey(t, attr.ProfileLoopIsHoleKey, ""),
                    attr.ProfileNormalize));
            }

            return result;
        }

        /// <summary>入れ子の中の ProfileRole を、ドット区切りのキーで拾う。</summary>
        private static void CollectNestedProfiles(
            Type t, string prefix, int depth, List<ProfileKey> dst)
        {
            if (t == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    CollectNestedProfiles(m.Type, key, depth + 1, dst);
                    continue;
                }

                if (!m.Attr.HasProfileRole) continue;

                // 相棒（ループ開始位置・穴フラグ）は同じ入れ子の中にある前提。
                dst.Add(new ProfileKey(
                    key, m.Attr.ProfileRole,
                    NestedSiblingKey(prefix, m.Attr.ProfileLoopStartsKey),
                    NestedSiblingKey(prefix, m.Attr.ProfileLoopIsHoleKey),
                    m.Attr.ProfileNormalize));
            }
        }

        /// <summary>
        /// このコマンド型で LegacyXYPairs が付いたパラメータのキーを返す。
        /// 走査規則は ProfileKeys と同じ（入れ子はドット区切り）。
        /// </summary>
        public static List<string> LegacyXYPairKeys(Type t)
        {
            var result = new List<string>();
            if (t == null) return result;

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                if (IsNestedType(prop.PropertyType))
                {
                    CollectNestedLegacyXYPairs(prop.PropertyType, KeyOf(t, prop), 1, result);
                    continue;
                }

                if (attr.LegacyXYPairs) result.Add(KeyOf(t, prop));
            }
            return result;
        }

        private static void CollectNestedLegacyXYPairs(
            Type t, string prefix, int depth, List<string> dst)
        {
            if (t == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    CollectNestedLegacyXYPairs(m.Type, key, depth + 1, dst);
                    continue;
                }

                if (m.Attr.LegacyXYPairs) dst.Add(key);
            }
        }

        /// <summary>入れ子の相棒キー。プロパティ名を同じ頭のキーへ付け替える。</summary>
        private static string NestedSiblingKey(string prefix, string memberName)
            => string.IsNullOrEmpty(memberName) ? "" : prefix + "." + Camel(memberName);

        /// <summary>トップレベルの相棒キー。対応するプロパティが無ければ fallback。</summary>
        private static string ResolveSiblingKey(Type t, string propertyName, string fallback)
        {
            if (string.IsNullOrEmpty(propertyName)) return fallback;
            PropertyInfo prop = FindProperty(t, propertyName);
            return prop != null ? KeyOf(t, prop) : fallback;
        }

        /// <summary>
        /// 作り直しで出力先へ書き戻すときに使うキー。
        /// 1 コマンド型に 1 組（索引のキーと、形を切り替えるキー）。
        /// </summary>
        public readonly struct RebuildTarget
        {
            /// <summary>出力先の masterIndex を書くキー。空＝書き戻しに対応していない。</summary>
            public readonly string TargetIndexKey;

            /// <summary>
            /// TargetIndexKey が索引の配列（int[]）か。
            ///
            /// true は「出力先が複数ある」ということ。はしごから作る鎖のように
            /// 1 回の実行で何本もできるものがこれ。書き戻す側は索引を
            /// カンマ区切りで書く。
            /// </summary>
            public readonly bool TargetIsArray;

            /// <summary>書き戻す形へ切り替えるキー。空＝切り替えるものが無い。</summary>
            public readonly string ModeKey;

            /// <summary>ModeKey へ書く値（Args の文字列）。</summary>
            public readonly string ModeArgValue;

            public RebuildTarget(
                string targetIndexKey, bool targetIsArray, string modeKey, string modeArgValue)
            {
                TargetIndexKey = targetIndexKey ?? "";
                TargetIsArray  = targetIsArray;
                ModeKey        = modeKey        ?? "";
                ModeArgValue   = modeArgValue   ?? "";
            }

            /// <summary>このコマンドは出力先へ書き戻せるか。</summary>
            public bool IsSupported => !string.IsNullOrEmpty(TargetIndexKey);

            /// <summary>切り替えるキーを持つか。</summary>
            public bool HasMode => !string.IsNullOrEmpty(ModeKey);

            public override string ToString()
                => IsSupported
                    ? (HasMode ? $"{TargetIndexKey} ({ModeKey}={ModeArgValue})" : TargetIndexKey)
                    : "(書き戻し不可)";
        }

        /// <summary>
        /// このコマンド型で RebuildRole が付いたパラメータのキーを返す。
        /// 印が無ければ IsSupported = false。走査規則は MeshRefKeys と同じ。
        ///
        /// TargetIndex / TargetMode が 2 つ以上あるときは、先に見つけた方を使い
        /// 警告を出す（印の付け間違い）。
        /// </summary>
        public static RebuildTarget RebuildKeys(Type t)
        {
            string targetKey   = "";
            bool   targetArray = false;
            string modeKey     = "";
            string modeValue   = "";

            if (t == null) return new RebuildTarget("", false, "", "");

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return new RebuildTarget("", false, "", "");

            void Take(string key, Type memberType, PLParamAttribute a)
            {
                if (a.RebuildRole == PLRebuildRole.TargetIndex)
                {
                    if (memberType != typeof(int) && memberType != typeof(int[]))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[PanelCommandFactory] {key}: RebuildRole=TargetIndex は int か int[] でなければならない");
                        return;
                    }
                    if (!string.IsNullOrEmpty(targetKey))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[PanelCommandFactory] {t.Name}: TargetIndex が 2 つ以上ある"
                          + $"（{targetKey} / {key}）。先に見つけた方を使う");
                        return;
                    }
                    targetKey   = key;
                    targetArray = memberType == typeof(int[]);
                    return;
                }

                if (a.RebuildRole == PLRebuildRole.TargetMode)
                {
                    if (!string.IsNullOrEmpty(modeKey))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[PanelCommandFactory] {t.Name}: TargetMode が 2 つ以上ある"
                          + $"（{modeKey} / {key}）。先に見つけた方を使う");
                        return;
                    }

                    string v = FormatRebuildModeValue(memberType, a.RebuildModeValue, key);
                    if (v == null) return;

                    modeKey   = key;
                    modeValue = v;
                }
            }

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                if (IsNestedType(prop.PropertyType))
                {
                    CollectNestedRebuild(prop.PropertyType, KeyOf(t, prop), 1, Take);
                    continue;
                }

                if (!attr.HasRebuildRole) continue;

                Take(KeyOf(t, prop), prop.PropertyType, attr);
            }

            return new RebuildTarget(targetKey, targetArray, modeKey, modeValue);
        }

        /// <summary>入れ子の中の RebuildRole を、ドット区切りのキーで拾う。</summary>
        private static void CollectNestedRebuild(
            Type t, string prefix, int depth, Action<string, Type, PLParamAttribute> take)
        {
            if (t == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    CollectNestedRebuild(m.Type, key, depth + 1, take);
                    continue;
                }

                if (!m.Attr.HasRebuildRole) continue;

                take(key, m.Type, m.Attr);
            }
        }

        /// <summary>
        /// TargetMode へ書く値を Args の文字列にする。
        /// 変換は PanelCommandFactory.TryFormat と同じ規則にすること
        ///   enum   … メンバー名 → int
        ///   bool   … "true" / "false"
        ///   int    … 10 進
        ///   string … そのまま
        /// 読めないときは警告して null を返す（印の付け間違い）。
        /// </summary>
        private static string FormatRebuildModeValue(Type memberType, string raw, string key)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            if (memberType == null) return null;

            if (string.IsNullOrEmpty(raw))
            {
                UnityEngine.Debug.LogWarning(
                    $"[PanelCommandFactory] {key}: RebuildModeValue が空");
                return null;
            }

            if (memberType.IsEnum)
            {
                try
                {
                    object v = Enum.Parse(memberType, raw, ignoreCase: false);
                    return Convert.ToInt32(v).ToString(inv);
                }
                catch (ArgumentException)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[PanelCommandFactory] {key}: {memberType.Name} に \"{raw}\" というメンバーが無い");
                    return null;
                }
            }

            if (memberType == typeof(bool))
            {
                if (bool.TryParse(raw, out bool b)) return b ? "true" : "false";
                UnityEngine.Debug.LogWarning(
                    $"[PanelCommandFactory] {key}: bool へ \"{raw}\" は読めない");
                return null;
            }

            if (memberType == typeof(int))
            {
                if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, inv, out int i))
                    return i.ToString(inv);
                UnityEngine.Debug.LogWarning(
                    $"[PanelCommandFactory] {key}: int へ \"{raw}\" は読めない");
                return null;
            }

            if (memberType == typeof(string)) return raw;

            UnityEngine.Debug.LogWarning(
                $"[PanelCommandFactory] {key}: RebuildRole=TargetMode を付けられない型 {memberType.Name}");
            return null;
        }

        /// <summary>
        /// プロパティ名から、そのパラメータが届くキーを引く。
        /// 対応するプロパティが無いときは空文字。
        ///
        /// ObjectGroup がキーを直書きしないために公開する。
        /// キーの規則（先頭小文字・別名表）はこのクラスにしか無いので、
        /// 外で組み立てると必ずずれる。
        /// </summary>
        public static string KeyOfProperty(System.Type t, string propertyName)
        {
            if (t == null || string.IsNullOrEmpty(propertyName)) return "";
            PropertyInfo prop = FindProperty(t, propertyName);
            return prop == null ? "" : KeyOf(t, prop);
        }

        /// <summary>
        /// IsMeshRef を付けてよい型か。int と int[] だけ。
        /// 他の型に付いていたら付け間違いなので拾わない。
        /// </summary>
        // ================================================================
        // 印の付け忘れ（参考）
        // ================================================================

        /// <summary>
        /// 名前が描画オブジェクトの索引を示すのに IsMeshRef が付いていない
        /// int / int[] パラメータのキーを返す。走査規則は MeshRefKeys と同じ。
        ///
        /// 【何のために要るか】
        ///   queryScenarioAudit は IsMeshRef の印で「焼いてはいけない索引」を見分ける。
        ///   印の無い引数は点検を素通りするので、どれだけ漏れているかを数で出す。
        ///
        /// 【名前で見る理由と限界】
        ///   型だけでは索引と他の整数（段数・スロット番号）を区別できない。
        ///   名前が *MasterIndex / *MasterIndices / *MeshIndex / *MeshIndices で終わるものに絞る。
        ///   別の名前で索引を持つ引数は拾えない。
        ///
        /// 【ここは付け足さない】
        ///   IsMeshRef を付けると ObjectGroup の作り直しで索引の引き直しが入る
        ///   （PLParamAttribute.IsMeshRef の注記）。一覧を見て、コマンドごとに判断して付ける。
        /// </summary>
        public static List<string> UnmarkedMeshIndexKeys(Type t)
        {
            var result = new List<string>();
            if (t == null) return result;

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return result;

            foreach (var p in ctor.GetParameters())
            {
                if (IsModelIndexParam(p)) continue;

                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;

                var attr = prop.GetCustomAttribute<PLParamAttribute>(inherit: true);
                if (attr == null || attr.Ignore) continue;

                if (IsNestedType(prop.PropertyType))
                {
                    CollectNestedUnmarked(prop.PropertyType, KeyOf(t, prop), 1, result);
                    continue;
                }

                if (attr.IsMeshRef) continue;
                if (!IsMeshRefType(prop.PropertyType)) continue;
                if (!LooksLikeMeshIndexName(prop.Name)) continue;

                result.Add(KeyOf(t, prop));
            }

            return result;
        }

        private static void CollectNestedUnmarked(Type t, string prefix, int depth, List<string> dst)
        {
            if (t == null || depth > NestedMaxDepth) return;

            foreach (var m in EnumerateNested(t))
            {
                if (m.Attr.Ignore) continue;

                string key = prefix + "." + Camel(m.Name);

                if (IsNestedType(m.Type))
                {
                    CollectNestedUnmarked(m.Type, key, depth + 1, dst);
                    continue;
                }

                if (m.Attr.IsMeshRef) continue;
                if (!IsMeshRefType(m.Type)) continue;
                if (!LooksLikeMeshIndexName(m.Name)) continue;

                dst.Add(key);
            }
        }

        private static bool LooksLikeMeshIndexName(string n)
            => n != null &&
               (n.EndsWith("MasterIndex",   StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("MasterIndices", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("MeshIndex",     StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("MeshIndices",   StringComparison.OrdinalIgnoreCase));

        private static bool IsMeshRefType(Type t)
            => t == typeof(int) || t == typeof(int[]);

        // ================================================================
        // 書き込み条件（PLParam.WriteWhen）
        // ================================================================

        /// <summary>
        /// "キー=値1|値2" を分解する。形が正しくなければ false。
        /// </summary>
        public static bool TryParseWriteWhen(string writeWhen, out string key, out string[] values)
        {
            key = null; values = null;
            if (string.IsNullOrEmpty(writeWhen)) return false;
            int eq = writeWhen.IndexOf('=');
            if (eq <= 0 || eq == writeWhen.Length - 1) return false;
            key = writeWhen.Substring(0, eq).Trim();
            values = writeWhen.Substring(eq + 1).Split('|');
            for (int i = 0; i < values.Length; i++) values[i] = values[i].Trim();
            return key.Length > 0;
        }

        /// <summary>
        /// Write 引数がこのコマンドの値で実際に書き込み先になるか。
        /// args は ToArgs(cmd) の結果。WriteWhen が空なら常に true。
        /// 条件のキーが args に無いとき（既定値のまま等）は、書き込み先とみなす（安全側）。
        ///
        /// 【enum の比べ方】
        ///   ToArgs は enum を整数の文字列で出す（TryFormat）。WriteWhen は名前で書くので、
        ///   条件のキーの型が enum なら名前を整数へ直してから比べる。
        /// </summary>
        public static bool IsWriteActive(Type t, MeshRefKey k, IReadOnlyDictionary<string, string> args)
        {
            if (k.Access != PLMeshRefAccess.Write) return false;
            if (string.IsNullOrEmpty(k.WriteWhen)) return true;
            if (!TryParseWriteWhen(k.WriteWhen, out string key, out string[] values)) return true;
            if (args == null || !args.TryGetValue(key, out string actual)) return true;

            Type argType = null;
            TryResolveArgType(t, key, out argType);

            foreach (var v in values)
            {
                string want = v;
                if (argType != null && argType.IsEnum)
                {
                    string name = Enum.GetNames(argType)
                        .FirstOrDefault(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase));
                    if (name == null) continue;
                    want = Convert.ToInt32(Enum.Parse(argType, name)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (string.Equals(want, actual, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// WriteWhen の書き間違いを数える（検査用）。
        /// キーがそのコマンドの引数に無い、値がその型として読めない、形が崩れている、を拾う。
        /// </summary>
        public static List<string> InvalidWriteWhens(Type t)
        {
            var bad = new List<string>();
            foreach (var k in MeshRefKeys(t))
            {
                if (string.IsNullOrEmpty(k.WriteWhen)) continue;
                string at = t.Name + "." + k.Key + " \"" + k.WriteWhen + "\"";

                if (k.Access != PLMeshRefAccess.Write)
                { bad.Add(at + "：Write 以外に付いている"); continue; }
                if (!TryParseWriteWhen(k.WriteWhen, out string key, out string[] values))
                { bad.Add(at + "：\"キー=値\" の形ではない"); continue; }
                if (!TryResolveArgType(t, key, out Type argType))
                { bad.Add(at + $"：キー {key} が引数に無い"); continue; }

                foreach (var v in values)
                {
                    bool ok = argType == typeof(bool)
                        ? (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
                        : argType.IsEnum
                            ? Enum.GetNames(argType).Any(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase))
                            : false;
                    if (!ok) bad.Add(at + $"：値 {v} は {argType.Name} として読めない（bool と enum だけを受ける）");
                }
            }
            return bad;
        }

        /// <summary>
        /// ToArgs のキー（入れ子はドット区切り）から、その引数の型を引く。
        /// </summary>
        private static bool TryResolveArgType(Type t, string key, out Type argType)
        {
            argType = null;
            if (t == null || string.IsNullOrEmpty(key)) return false;
            string[] parts = key.Split('.');

            ConstructorInfo ctor = PickConstructor(t);
            if (ctor == null) return false;

            Type cur = null;
            foreach (var p in ctor.GetParameters())
            {
                PropertyInfo prop = FindProperty(t, p.Name);
                if (prop == null) continue;
                if (KeyOf(t, prop) == parts[0]) { cur = prop.PropertyType; break; }
            }
            if (cur == null) return false;

            for (int i = 1; i < parts.Length; i++)
            {
                if (!IsNestedType(cur)) return false;
                Type next = null;
                foreach (var m in EnumerateNested(cur))
                    if (Camel(m.Name) == parts[i]) { next = m.Type; break; }
                if (next == null) return false;
                cur = next;
            }
            argType = cur;
            return true;
        }
    }
}
