// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_HitTestDebug.cs
// GPU ヒットテスト検証用（開発・デバッグ用）
// 現在は無効化されています

using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

public partial class PolyLing
{
    // ================================================================
    // ヒットテスト検証（デバッグ用）- 無効化済み
    // ================================================================

   // private bool _enableHitTestValidation = false;

    /// <summary>
    /// ヒットテスト検証を有効化（現在は無効化されています）
    /// </summary>
    [MenuItem("Tools/Poly_Ling/debug/Toggle HitTest Validation")]
    public static void ToggleHitTestValidation()
    {
        Debug.Log("[HitTest] Validation is currently disabled.");
    }


    /// <summary>
    /// クリック時の検証（無効化済み）
    /// </summary>
    private void ValidateHitTestOnClick(
        Vector2 mousePos, 
        Rect rect, 
        MeshObject meshObject, 
        Vector3 camPos, 
        Vector3 lookAt,
        HitResult existingHitResult)
    {
        // 無効化
    }

    /// <summary>
    /// クリーンアップ（無効化済み）
    /// </summary>
    private void CleanupHitTestValidation()
    {
        // 無効化
    }
}
