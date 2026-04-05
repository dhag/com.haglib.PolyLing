using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// EditorMeshLoaderからPolyLingの内部状態へアクセスするためのインターフェース。
    /// 状態変更を伴う操作のみを定義し、純粋計算はEditorMeshLoaderの静的メソッドが担う。
    /// </summary>
    public interface IEditorMeshLoadHost
    {
        /// <summary>メッシュを読み込んでリストに追加する（状態変更）</summary>
        void AddLoadedMesh(Mesh mesh, string name, Material[] materials = null, Transform sourceTransform = null);

        /// <summary>GameObjectの階層構造をメッシュリストとしてインポートする（状態変更）</summary>
        void LoadHierarchyFromGameObject(GameObject rootGameObject, Transform boneRootTransform, bool detectNamedMirror = true);

        /// <summary>SkinnedMeshRenderer検出時のインポートダイアログを表示する（Editor UI）</summary>
        void ShowSkinnedMeshImportDialogInternal(GameObject rootObject, SkinnedMeshRenderer[] skinnedRenderers);
    }
}
