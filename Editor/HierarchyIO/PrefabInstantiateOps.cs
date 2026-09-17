// Editor/HierarchyIO/PrefabInstantiateOps.cs
// ============================================================
// プレファブ資産をシーンのヒエラルキーへ登録する
// ============================================================
//
// 【役割】
//   PrefabUtility.InstantiatePrefab でプレファブ接続を保ったインスタンスを作り、
//   Undo に登録してシーンを変更済みにする。
//   MCP（PolyLingEditorControlServer の prefab_instantiate）から呼ぶ。
//
// 【親の指定】
//   "Root/Child/Grandchild" 形式のヒエラルキーパス。
//   GameObject.Find は非アクティブなオブジェクトを見つけないため使わず、
//   読み込み済みシーンのルートを名前で探してから Transform.Find で下る。
//
// 【Play 中は呼ばないこと】
//   Play 中に作った GameObject は Play 終了で破棄される。
//   Play 中かどうかの判定は呼び出し側（PolyLingEditorControlServer）が行う。
//
// ============================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poly_Ling.EditorIO
{
    /// <summary>プレファブ資産をシーンへインスタンス化する。</summary>
    public static class PrefabInstantiateOps
    {
        /// <param name="prefabAssetPath">"Assets/..." 形式のプレファブのパス。</param>
        /// <param name="parentPath">任意。親のヒエラルキーパス。空ならアクティブシーンのルートに置く。</param>
        /// <param name="localPosition">任意。親からの位置。null なら変更しない。</param>
        /// <param name="instance">作ったインスタンス。失敗時は null。</param>
        /// <param name="message">結果の文言。</param>
        public static bool Instantiate(
            string         prefabAssetPath,
            string         parentPath,
            Vector3?       localPosition,
            out GameObject instance,
            out string     message)
        {
            instance = null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
            if (prefab == null)
            {
                message = "プレファブを読み込めません: " + prefabAssetPath;
                return false;
            }

            Transform parent = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parent = FindInLoadedScenes(parentPath);
                if (parent == null)
                {
                    message = "親が見つかりません: " + parentPath;
                    return false;
                }
            }

            Scene scene = parent != null ? parent.gameObject.scene : SceneManager.GetActiveScene();

            instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                message = "インスタンス化に失敗しました: " + prefabAssetPath;
                return false;
            }

            if (parent != null) instance.transform.SetParent(parent, false);
            if (localPosition.HasValue) instance.transform.localPosition = localPosition.Value;

            Undo.RegisterCreatedObjectUndo(instance, "PolyLing: Instantiate Prefab");
            EditorSceneManager.MarkSceneDirty(instance.scene);

            // Poly_Ling.Selection 名前空間と衝突するため完全修飾する。
            UnityEditor.Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            message = $"登録しました: path={GetHierarchyPath(instance.transform)}; scene={instance.scene.path}";
            return true;
        }

        /// <summary>"Root/Child" 形式のパスで、読み込み済みシーンから Transform を探す。非アクティブも対象。</summary>
        public static Transform FindInLoadedScenes(string hierarchyPath)
        {
            string path = hierarchyPath.Replace('\\', '/').Trim('/');
            if (path.Length == 0) return null;

            int slash    = path.IndexOf('/');
            string head  = slash < 0 ? path : path.Substring(0, slash);
            string rest  = slash < 0 ? ""   : path.Substring(slash + 1);

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != head) continue;

                    var found = rest.Length == 0 ? root.transform : root.transform.Find(rest);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>ルートからの "Root/Child" 形式のパス。</summary>
        public static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";

            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
