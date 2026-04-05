using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// EditorMeshSaverからPolyLingの内部状態へアクセスするためのインターフェース。
    /// ミラーベイク・マテリアル取得・選択GameObjectの参照を提供する。
    /// </summary>
    public interface IEditorMeshSaveHost
    {
        bool BakeMirror { get; }
        bool MirrorFlipU { get; }
        Mesh BakeMirrorToUnityMesh(MeshContext meshContext, bool flipU, out List<int> usedMatIndices);
        Material[] GetMaterialsForSave(MeshContext meshContext);
        Material[] GetMaterialsForBakedMirror(List<int> usedMatIndices, Material[] baseMaterials);
        GameObject[] GetSelectedGameObjects();
    }
}
