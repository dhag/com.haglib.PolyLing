// Assets/Editor/Poly_Ling/Core/AssetIO/MeshAssetUtil.cs
// ============================================================
// メッシュの決定論アセット化ユーティリティ
// ============================================================
//
// 【役割】
//   生成済み Unity Mesh を、決定論パスの .asset として保存する（Export 成果物）。
//   同一パスに既存があれば内容を上書き（GUID/参照を保持）＝再エクスポートで
//   別物を量産しない。マテリアルの決定論アセット化（MaterialReference.SaveAsAsset /
//   ModelContext.SaveOnMemoryMaterialsAsAssets）と同一ポリシー。
//
// 【設計】
//   モデルにメッシュのアセット参照は持たせない（geometry の正本は MeshObject 頂点）。
//   本 util は Export 側で「共有アセット化された Mesh を得る」ためだけに使う。
//
// 【Editor 依存】
//   AssetDatabase/EditorUtility 相当の処理は #if UNITY_EDITOR を持ち込まず、
//   すべて PLEditorBridge（IEditorBridge）経由で行う（規約：EditorBridge に集約）。
//
// ============================================================

using UnityEngine;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.AssetIO
{
    /// <summary>メッシュを決定論パスの .asset として保存する（上書き再利用）。</summary>
    public static class MeshAssetUtil
    {
        /// <summary>
        /// mesh を path(.asset) に保存し、アセット化された Mesh を返す。
        ///   - 同 path に既存 Mesh があれば CopySerialized で内容を上書き（GUID保持）→ 既存を返す。
        ///   - 無ければ CreateAsset → mesh を返す。
        /// path が空、または mesh が null のときは mesh をそのまま返す（保存しない）。
        /// </summary>
        public static Mesh SaveDeterministic(Mesh mesh, string path)
        {
            if (mesh == null || string.IsNullOrEmpty(path))
                return mesh;

            var existing = PLEditorBridge.I.LoadAssetAtPath<Mesh>(path);
            if (existing != null && !ReferenceEquals(existing, mesh))
            {
                // 既存 .asset を現在の mesh 内容で上書き（GUID/参照を保持）
                PLEditorBridge.I.CopySerialized(mesh, existing);
                PLEditorBridge.I.SaveAssets();
                return existing;
            }

            if (existing == null)
            {
                PLEditorBridge.I.CreateAsset(mesh, path);
                PLEditorBridge.I.SaveAssets();
            }
            return mesh;
        }
    }
}
