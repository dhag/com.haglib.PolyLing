using UnityEditor;
using UnityEngine;

namespace PolyLingMcpUserScripts
{
    /// <summary>
    /// MCP からの依頼で、Gear プレハブへ RotateObject コンポーネントを自動アタッチする。
    /// スクリプトのコンパイル完了（ドメインリロード）時に一度だけ処理し、
    /// 既にアタッチ済みなら何もしない（冪等）。
    /// </summary>
    [InitializeOnLoad]
    internal static class RotateObjectAttacher
    {
        private const string TargetPrefabPath = "Assets/PolyLing/Gear/Gear.prefab";

        static RotateObjectAttacher()
        {
            Debug.Log("[RotateObjectAttacher] cctor 実行、delayCall 登録");
            EditorApplication.delayCall += () =>
            {
                Debug.Log("[RotateObjectAttacher] delayCall 実行開始");
                try
                {
                    TryAttach();
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            };
        }

        private static void TryAttach()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
            if (prefab == null)
            {
                return;
            }

            if (prefab.GetComponent<RotateObject>() != null)
            {
                // 既にアタッチ済み
                return;
            }

            var contentsRoot = PrefabUtility.LoadPrefabContents(TargetPrefabPath);
            try
            {
                if (contentsRoot.GetComponent<RotateObject>() == null)
                {
                    contentsRoot.AddComponent<RotateObject>();
                    PrefabUtility.SaveAsPrefabAsset(contentsRoot, TargetPrefabPath);
                    Debug.Log($"[RotateObjectAttacher] RotateObject を {TargetPrefabPath} に追加しました。");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }

            AssetDatabase.Refresh();
        }
    }
}
