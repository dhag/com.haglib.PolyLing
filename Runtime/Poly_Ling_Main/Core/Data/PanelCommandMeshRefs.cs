// PanelCommandMeshRefs.cs
// PLParam(IsMeshRef = true) が付いたパラメータのキーを列挙する。
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

            public MeshRefKey(string key, bool isArray, string modelKey)
            { Key = key; IsArray = isArray; ModelKey = modelKey ?? ""; }

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
                    ResolveModelKey(t, attr)));
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

                dst.Add(new MeshRefKey(key, m.Type == typeof(int[]), ""));
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
        private static bool IsMeshRefType(Type t)
            => t == typeof(int) || t == typeof(int[]);
    }
}
