// Assets/Editor/UndoSystem/MeshEditor/Context/MeshEditContext.cs
// メッシュ編集コンテキスト
// Undo/Redo操作の対象となるデータを保持
// Phase 5: Materials を ModelContext に委譲

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Model;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// メッシュ編集コンテキスト
    /// Undo/Redo操作の対象となるデータを保持
    /// MeshObjectベースの構造
    /// </summary>
    public class MeshUndoContext
    {
        // === メッシュコンテキスト解決 ===

        /// <summary>現在選択中のメッシュを自動解決（ModelContextの選択リストから）</summary>
        public MeshContext ResolvedMeshContext => MaterialOwner?.FirstSelectedMeshContext;

        // === メッシュデータ（委譲プロパティ） ===

        // fallbackフィールド: ResolvedMeshContextがnullの間（初期化中等）に使用
        private MeshObject _fallbackMeshObject = new MeshObject();
        private Mesh _fallbackTargetMesh;
        private Vector3[] _fallbackOriginalPositions;

        /// <summary>メッシュデータ本体（ResolvedMeshContext経由で自動解決）</summary>
        public MeshObject MeshObject
        {
            get
            {
                var mc = ResolvedMeshContext;
                return mc != null ? mc.MeshObject : _fallbackMeshObject;
            }
            set
            {
                var mc = ResolvedMeshContext;
                if (mc != null)
                    mc.MeshObject = value;
                else
                    _fallbackMeshObject = value;
            }
        }

        /// <summary>実際のMeshオブジェクト（ResolvedMeshContext経由で自動解決）</summary>
        public Mesh TargetMesh
        {
            get
            {
                var mc = ResolvedMeshContext;
                return mc != null ? mc.UnityMesh : _fallbackTargetMesh;
            }
            set
            {
                var mc = ResolvedMeshContext;
                if (mc != null)
                    mc.UnityMesh = value;
                else
                    _fallbackTargetMesh = value;
            }
        }

        /// <summary>元の頂点位置（ResolvedMeshContext経由で自動解決）</summary>
        public Vector3[] OriginalPositions
        {
            get
            {
                var mc = ResolvedMeshContext;
                return mc != null ? mc.OriginalPositions : _fallbackOriginalPositions;
            }
            set
            {
                var mc = ResolvedMeshContext;
                if (mc != null)
                    mc.OriginalPositions = value;
                else
                    _fallbackOriginalPositions = value;
            }
        }

        // === 選択状態 ===

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> SelectedVertices;

        // === 元データ参照（委譲プロパティに移行済み） ===
        // OriginalPositions は上記の委譲プロパティとして定義済み

        // === Unity UnityMesh 参照（委譲プロパティに移行済み） ===
        // TargetMesh は上記の委譲プロパティとして定義済み

        // === WorkPlane参照（選択連動Undo用） ===

        /// <summary>WorkPlane参照（選択と連動してUndo/Redoするため）</summary>
        public WorkPlaneContext WorkPlane;

        // === 拡張選択システム用 ===

        /// <summary>Undo/Redo時に復元すべきSelectionSnapshot</summary>
        public SelectionSnapshot CurrentSelectionSnapshot;

        // === マテリアル（ModelContext に委譲） ===

        /// <summary>マテリアル委譲用ModelContextへの参照 - 必須</summary>
        public ModelContext MaterialOwner { get; set; }

        /// <summary>マテリアルリスト（ModelContextに委譲）</summary>
        public List<Material> Materials
        {
            get
            {
                if (MaterialOwner == null)
                {
                    Debug.LogError("[MeshUndoContext] MaterialOwnerが設定されていません。");
                    return new List<Material>();
                }
                return MaterialOwner.Materials;
            }
            set
            {
                if (MaterialOwner == null)
                {
                    Debug.LogError("[MeshUndoContext] MaterialOwnerが設定されていません。");
                    return;
                }
                MaterialOwner.Materials = value ?? new List<Material>();
            }
        }

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int CurrentMaterialIndex
        {
            get => MaterialOwner?.CurrentMaterialIndex ?? 0;
            set
            {
                if (MaterialOwner == null)
                {
                    Debug.LogError("[MeshUndoContext] MaterialOwnerが設定されていません。");
                    return;
                }
                MaterialOwner.CurrentMaterialIndex = value;
            }
        }

        // === デフォルトマテリアル（ModelContext に委譲） ===

        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアルリスト</summary>
        public List<Material> DefaultMaterials
        {
            get => MaterialOwner?.DefaultMaterials ?? new List<Material> { null };
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.DefaultMaterials = value ?? new List<Material> { null };
            }
        }

        /// <summary>新規メッシュ作成時に適用されるデフォルトカレントマテリアルインデックス</summary>
        public int DefaultCurrentMaterialIndex
        {
            get => MaterialOwner?.DefaultCurrentMaterialIndex ?? 0;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.DefaultCurrentMaterialIndex = value;
            }
        }

        /// <summary>マテリアル変更時に自動でデフォルトに設定するか</summary>
        public bool AutoSetDefaultMaterials
        {
            get => MaterialOwner?.AutoSetDefaultMaterials ?? true;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.AutoSetDefaultMaterials = value;
            }
        }

        // === 後方互換プロパティ ===

        /// <summary>頂点位置リスト（後方互換）</summary>
        public List<Vector3> Vertices
        {
            get => MeshObject != null ? new List<Vector3>(MeshObject.Positions) : new List<Vector3>();
            set
            {
                if (MeshObject == null) return;
                MeshObject.SetPositions(value.ToArray());
            }
        }

        /// <summary>元の頂点位置（後方互換）</summary>
        public Vector3[] OriginalVertices
        {
            get => OriginalPositions;
            set => OriginalPositions = value;
        }

        // === 複数メッシュ頂点移動のUndo/Redo通知用 ===

        /// <summary>
        /// MultiMeshVertexMoveRecord.Undo/Redo時に変更されたメッシュインデックス。
        /// OnUndoRedoPerformedで処理後にクリアされる。
        /// 空の場合は単一メッシュ操作（VertexMoveRecord等）→ 従来のctx.MeshObjectフロー。
        /// </summary>
        public List<int> DirtyMeshIndices { get; } = new List<int>();

        /// <summary>
        /// MultiMeshVertexMoveRecord.Undo/Redo時に保留された頂点移動エントリ。
        /// Record内ではメッシュ解決を行わず、OnUndoRedoPerformedで適用される。
        /// 処理後にnullに戻される。
        /// </summary>
        public MeshMoveEntry[] PendingMeshMoveEntries;

        // === コンストラクタ ===

        public MeshUndoContext()
        {
            MeshObject = new MeshObject();
            SelectedVertices = new HashSet<int>();
            // Materials, DefaultMaterials は MaterialOwner 経由で取得
            // MaterialOwner は使用前に必ず設定すること
        }

        // === メッシュ読み込み/適用 ===

        /// <summary>
        /// Meshからデータを読み込む
        /// </summary>
        public void LoadFromMesh(Mesh mesh, bool mergeVertices = true)
        {
            if (mesh == null) return;

            TargetMesh = mesh;
            MeshObject = new MeshObject();
            MeshObject.FromUnityMesh(mesh, mergeVertices);

            // 元の位置を保存（MeshObject.Positionsキャッシュ経由）
            OriginalPositions = (Vector3[])MeshObject.Positions.Clone();

            // 選択クリア
            SelectedVertices.Clear();
        }

        /// <summary>
        /// データをMeshに適用
        /// </summary>
        public void ApplyToMesh()
        {
            if (TargetMesh == null || MeshObject == null) return;

            // MeshObjectをUnity Meshに変換して適用
            var newMesh = MeshObject.ToUnityMeshShared();

            TargetMesh.Clear();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.triangles = newMesh.triangles;
            TargetMesh.uv = newMesh.uv;
            TargetMesh.normals = newMesh.normals;
            TargetMesh.RecalculateBounds();

            // 一時メッシュを破棄
            UnityEngine.Object.DestroyImmediate(newMesh);
        }

        /// <summary>
        /// 頂点位置のみをMeshに適用（高速）
        /// ToUnityMeshShared()を使わず、直接頂点位置を設定
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (TargetMesh == null || MeshObject == null) return;

            // Unity Meshに変換して頂点位置を更新
            var newMesh = MeshObject.ToUnityMeshShared();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.RecalculateNormals();
            TargetMesh.RecalculateBounds();

            UnityEngine.Object.DestroyImmediate(newMesh);
        }

        // === 頂点操作ヘルパー ===

        /// <summary>
        /// 頂点位置を取得
        /// </summary>
        public Vector3 GetVertexPosition(int index)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return Vector3.zero;
            return MeshObject.Vertices[index].Position;
        }

        /// <summary>
        /// 頂点位置を設定
        /// </summary>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return;
            MeshObject.Vertices[index].Position = position;
            MeshObject.InvalidatePositionCache();
        }

        /// <summary>
        /// 全頂点位置を配列で取得（MeshObject.Positionsキャッシュ経由）
        /// </summary>
        public Vector3[] GetAllPositions()
        {
            if (MeshObject == null) return new Vector3[0];
            return (Vector3[])MeshObject.Positions.Clone();
        }

        /// <summary>
        /// 全頂点位置を配列で設定（MeshObject.SetPositions経由）
        /// </summary>
        public void SetAllPositions(Vector3[] positions)
        {
            if (MeshObject == null) return;
            MeshObject.SetPositions(positions);
        }

        /// <summary>
        /// 頂点数を取得
        /// </summary>
        public int VertexCount => MeshObject?.VertexCount ?? 0;

        /// <summary>
        /// 面数を取得
        /// </summary>
        public int FaceCount => MeshObject?.FaceCount ?? 0;
    }
}
