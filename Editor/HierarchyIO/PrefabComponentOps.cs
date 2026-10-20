// Editor/HierarchyIO/PrefabComponentOps.cs
// ============================================================
// プレファブ資産のオブジェクトへコンポーネントを付ける
// ============================================================
//
// 【役割】
//   PrefabUtility.LoadPrefabContents でプレファブを開き、指定のオブジェクトへ
//   AddComponent して SaveAsPrefabAsset で保存し、閉じる。
//   MCP（PolyLingEditorControlServer の prefab_add_component）から呼ぶ。
//   プレファブと .meta は MCP の書き込み道具で直接書けないため、Unity 自身に付けさせる。
//
// 【型の指定】
//   完全名（Namespace.Type）でも短い名前（Type）でもよい。
//   TypeCache で Component の派生型から探し、見つからない・同名が複数あるときは候補を挙げて失敗する。
//   抽象型・ジェネリック定義・Transform 系は付けられないので拒否する。
//   新しく書いたスクリプトは、コンパイルが済むまで型として見えない。
//
// 【対象の指定】
//   objectPath が空ならプレファブのルート。"Child/Grandchild" 形式でルートからの子を指す
//   （ルート名は含めない。Transform.Find と同じ）。
//   "/" を含まない名前だけで見つからないときは、子孫から同じ名前のものを探す
//   （1 つなら採る。複数ならパスを挙げて失敗する）。
//
// 【二重に付けない】
//   既に同じ型が付いていれば何もせず成功を返す。allowMultiple のときだけ 2 つ目を付ける。
//
// 【Play 中は呼ばないこと】
//   判定は呼び出し側（PolyLingEditorControlServer）が行う。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorIO
{
    /// <summary>プレファブ資産へコンポーネントを付ける。</summary>
    public static class PrefabComponentOps
    {
        /// <param name="prefabAssetPath">"Assets/..." 形式のプレファブのパス。</param>
        /// <param name="componentTypeName">付ける型の完全名または短い名前。</param>
        /// <param name="objectPath">任意。ルートからの子のパス。空ならルート。</param>
        /// <param name="allowMultiple">既に付いていても、もう 1 つ付けるか。</param>
        /// <param name="message">結果の文言。</param>
        public static bool AddComponent(
            string     prefabAssetPath,
            string     componentTypeName,
            string     objectPath,
            bool       allowMultiple,
            out string message)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath) == null)
            {
                message = "プレファブを読み込めません: " + prefabAssetPath;
                return false;
            }

            if (!TryResolveType(componentTypeName, out Type type, out message))
                return false;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabAssetPath);
            try
            {
                Transform target = root.transform;
                string path = (objectPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
                if (path.Length > 0)
                {
                    target = root.transform.Find(path);

                    // "/" を含まない名前だけのときは、子孫から同じ名前のものを 1 つ探す。
                    //   書き出した人型は階層が深く、ルートからのパスを人が書くのは手間なため。
                    //   同じ名前が複数あればどれか分からないので、パスで書くよう求める。
                    if (target == null && path.IndexOf('/') < 0)
                    {
                        var hits = root.GetComponentsInChildren<Transform>(true)
                                       .Where(t => t != root.transform && t.name == path)
                                       .ToList();
                        if (hits.Count > 1)
                        {
                            message = $"名前 {path} のオブジェクトが {hits.Count} 個あります。ルートからのパスで指定してください: "
                                    + string.Join(", ", hits.Select(t => PathFromRoot(root.transform, t)));
                            return false;
                        }
                        if (hits.Count == 1) target = hits[0];
                    }

                    if (target == null)
                    {
                        message = $"オブジェクトが見つかりません: {path}（ルート {root.name} からのパス。ルート名は含めない。名前だけでも子孫から探す）";
                        return false;
                    }
                }

                string where = path.Length > 0 ? $"{root.name}/{PathFromRoot(root.transform, target)}" : root.name;

                int existing = target.GetComponents(type).Length;
                if (existing > 0 && !allowMultiple)
                {
                    message = $"既に付いています: {type.FullName} → {where}（{existing} 個）。プレファブは変えていません";
                    return true;
                }

                var added = target.gameObject.AddComponent(type);
                if (added == null)
                {
                    message = $"付けられませんでした: {type.FullName} → {where}（RequireComponent・DisallowMultipleComponent などで拒否された）";
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath, out bool saved);
                if (!saved)
                {
                    message = "プレファブを保存できませんでした: " + prefabAssetPath;
                    return false;
                }

                message = $"付けました: {type.FullName} → {where}（{existing + 1} 個目）; prefab={prefabAssetPath}";
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>ルートからのパス（ルート名は含めない）。</summary>
        private static string PathFromRoot(Transform root, Transform t)
        {
            var names = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) names.Add(cur.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>名前から付けられる Component の型を 1 つに決める。</summary>
        private static bool TryResolveType(string name, out Type type, out string message)
        {
            type    = null;
            message = null;

            string n = (name ?? string.Empty).Trim();
            if (n.Length == 0)
            {
                message = "componentType がありません";
                return false;
            }

            var all = TypeCache.GetTypesDerivedFrom<Component>();

            List<Type> hits = all.Where(t => t.FullName == n).ToList();
            if (hits.Count == 0) hits = all.Where(t => t.Name == n).ToList();

            if (hits.Count == 0)
            {
                var near = all.Where(t => t.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                              .Select(t => t.FullName).Take(8).ToList();
                message = $"型が見つかりません: {n}。新しく書いたスクリプトならコンパイルを先に済ませてください（unity_validate_changes）"
                        + (near.Count > 0 ? "。近い名前: " + string.Join(", ", near) : "");
                return false;
            }

            if (hits.Count > 1)
            {
                message = $"同じ名前の型が複数あります: {n}。完全名で指定してください: "
                        + string.Join(", ", hits.Select(t => $"{t.FullName}（{t.Assembly.GetName().Name}）"));
                return false;
            }

            Type t0 = hits[0];
            if (t0.IsAbstract || t0.IsGenericTypeDefinition)
            {
                message = $"抽象型・ジェネリック定義は付けられません: {t0.FullName}";
                return false;
            }
            if (typeof(Transform).IsAssignableFrom(t0))
            {
                message = $"Transform 系は付けられません: {t0.FullName}";
                return false;
            }

            type = t0;
            return true;
        }
    }
}
