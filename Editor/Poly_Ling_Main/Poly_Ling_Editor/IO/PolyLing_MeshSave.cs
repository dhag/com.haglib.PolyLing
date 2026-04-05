// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_MeshSave.cs
// 単一メッシュ保存機能（エントリーポイント）
// 実装は EditorCore/MeshAssetIO/EditorMeshSaver.cs に移動済み。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.EditorCore;

public partial class PolyLing : IEditorMeshSaveHost
{
    // ================================================================
    // エントリーポイント（EditorMeshSaverに委譲）
    // ================================================================

    private void SaveMesh(MeshContext meshContext)      => EditorMeshSaver.SaveMesh(meshContext, this);
    private void SaveAsPrefab(MeshContext meshContext)  => EditorMeshSaver.SaveAsPrefab(meshContext, this);
    private void AddToHierarchy(MeshContext meshContext) => EditorMeshSaver.AddToHierarchy(meshContext, this);

    // ================================================================
    // IEditorMeshSaveHost 明示的実装
    // ================================================================

    bool IEditorMeshSaveHost.BakeMirror    => _bakeMirror;
    bool IEditorMeshSaveHost.MirrorFlipU   => _mirrorFlipU;

    Mesh IEditorMeshSaveHost.BakeMirrorToUnityMesh(MeshContext meshContext, bool flipU, out List<int> usedMatIndices)
        => BakeMirrorToUnityMesh(meshContext, flipU, out usedMatIndices);

    Material[] IEditorMeshSaveHost.GetMaterialsForSave(MeshContext meshContext)
        => GetMaterialsForSave(meshContext);

    Material[] IEditorMeshSaveHost.GetMaterialsForBakedMirror(List<int> usedMatIndices, Material[] baseMaterials)
        => GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);

    GameObject[] IEditorMeshSaveHost.GetSelectedGameObjects()
        => PLEditorBridge.I.GetSelectedGameObjects();
}
