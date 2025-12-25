// Tools/ToolContext.cs
// 編集ツールに渡されるコンテキスト
// SelectionState/TopologyCache対応版
// Phase 3: ModelContext統合
// Phase 4: PrimitiveMeshTool対応（メッシュ作成コールバック追加）
// Phase 5: マテリアル操作コールバック追加（MQOインポート対応）

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Selection;
using MeshFactory.Model;

// MeshContentはSimpleMeshFactoryのネストクラスを参照
using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 編集ツールに渡されるコンテキスト
    /// エディタの状態への読み書きアクセスを提供
    /// </summary>
    public class ToolContext
    {
        // === メッシュデータ ===

        /// <summary>MeshObject</summary>
        public MeshObject MeshObject { get; set; }

        /// <summary>元の頂点位置</summary>
        public Vector3[] OriginalPositions { get; set; }

        /// <summary>プレビュー矩形</summary>
        public Rect PreviewRect { get; set; }

        /// <summary>カメラ位置</summary>
        public Vector3 CameraPosition { get; set; }

        /// <summary>カメラ注視点</summary>
        public Vector3 CameraTarget { get; set; }

        /// <summary>カメラ距離</summary>
        public float CameraDistance { get; set; }

        // === 選択状態 ===

        /// <summary>選択中の頂点インデックス（後方互換）</summary>
        public HashSet<int> SelectedVertices { get; set; }

        /// <summary>頂点オフセット配列</summary>
        public Vector3[] VertexOffsets { get; set; }

        /// <summary>グループオフセット配列</summary>
        public Vector3[] GroupOffsets { get; set; }

        // === 新選択システム ===

        /// <summary>選択状態（Vertex/Edge/Face/Line）</summary>
        public SelectionState SelectionState { get; set; }

        /// <summary>トポロジキャッシュ</summary>
        public TopologyCache TopologyCache { get; set; }

        /// <summary>選択操作ヘルパー</summary>
        public SelectionOperations SelectionOps { get; set; }

        // === Undoシステム ===

        /// <summary>Undoコントローラー</summary>
        public MeshUndoController UndoController { get; set; }

        // === 作業平面 ===

        /// <summary>作業平面</summary>
        public WorkPlane WorkPlane { get; set; }

        // === マテリアル ===

        /// <summary>カレントマテリアルインデックス（面生成時に使用）</summary>
        public int CurrentMaterialIndex { get; set; } = 0;

        /// <summary>マテリアルリスト（読み取り専用参照）</summary>
        public IReadOnlyList<Material> Materials { get; set; }

        // === モデルコンテキスト（Phase 3追加） ===

        /// <summary>モデルコンテキスト（メッシュリスト管理）</summary>
        public ModelContext Model { get; set; }

        /// <summary>現在選択中のメッシュコンテキスト（便利プロパティ）</summary>
        public MeshContext CurrentMeshContent => Model?.CurrentMeshContext;

        /// <summary>メッシュリスト（読み取り専用）</summary>
        public IReadOnlyList<MeshContext> MeshList => Model?.MeshContextList;

        /// <summary>選択中のメッシュインデックス</summary>
        public int SelectedMeshIndex => Model?.SelectedIndex ?? -1;

        /// <summary>有効なメッシュが選択されているか</summary>
        public bool HasValidMeshSelection => Model?.HasValidSelection ?? false;

        // === メッシュリスト操作コールバック（Undo対応） ===

        /// <summary>メッシュコンテキストを追加（Undo対応）</summary>
        public Action<MeshContext> AddMeshContext { get; set; }
        /// <summary>メッシュコンテキストを複数追加（Undo対応・バッチ）</summary>
        public Action<IList<MeshContext>> AddMeshContexts { get; set; }

        /// <summary>メッシュコンテキストを削除（Undo対応）</summary>
        public Action<int> RemoveMeshContext { get; set; }

        /// <summary>メッシュを選択（Undo対応）</summary>
        public Action<int> SelectMeshContext { get; set; }

        /// <summary>メッシュを複製（Undo対応）</summary>
        public Action<int> DuplicateMeshContent { get; set; }

        /// <summary>メッシュの順序を変更（Undo対応）</summary>
        public Action<int, int> ReorderMeshContext { get; set; }

        // === メッシュ作成コールバック（Phase 4: PrimitiveMeshTool対応） ===

        /// <summary>MeshObjectから新しいMeshContextを作成（Undo対応）</summary>
        public Action<MeshObject, string> CreateNewMeshContext { get; set; }

        /// <summary>現在選択中のメッシュにMeshObjectを追加（Undo対応）</summary>
        public Action<MeshObject, string> AddMeshObjectToCurrentMesh { get; set; }

        /// <summary>全メッシュをクリア（Replaceインポート用）</summary>
        public Action ClearAllMeshContexts { get; set; }

        /// <summary>全メッシュを置換（1回のUndoで戻せるReplace）</summary>
        public Action<IList<MeshContext>> ReplaceAllMeshContexts { get; set; }

        // === マテリアル操作コールバック（Phase 5: MQOインポート対応） ===

        /// <summary>マテリアルリストを設定（Undo対応）</summary>
        public Action<IList<Material>> SetMaterials { get; set; }

        /// <summary>マテリアルを追加（Undo対応）</summary>
        public Action<Material> AddMaterial { get; set; }

        /// <summary>マテリアルを複数追加（Undo対応・バッチ）</summary>
        public Action<IList<Material>> AddMaterials { get; set; }

        /// <summary>マテリアルリストを置換（既存を全削除して新規設定）</summary>
        public Action<IList<Material>> ReplaceMaterials { get; set; }

        /// <summary>カレントマテリアルインデックスを設定</summary>
        public Action<int> SetCurrentMaterialIndex { get; set; }

        // === コールバック ===

        /// <summary>選択変更を記録</summary>
        public Action<HashSet<int>, HashSet<int>> RecordSelectionChange { get; set; }

        /// <summary>MeshObjectからUnity Meshを更新</summary>
        public Action SyncMesh { get; set; }

        /// <summary>画面再描画を要求</summary>
        public Action Repaint { get; set; }

        // === ヘルパーメソッド ===

        /// <summary>
        /// ワールド座標をスクリーン座標に変換
        /// </summary>
        public Func<Vector3, Rect, Vector3, Vector3, Vector2> WorldToScreenPos { get; set; }

        /// <summary>
        /// スクリーン移動量をワールド移動量に変換
        /// </summary>
        public Func<Vector2, Vector3, Vector3, float, Rect, Vector3> ScreenDeltaToWorldDelta { get; set; }

        /// <summary>
        /// 指定スクリーン位置にある頂点を検索
        /// </summary>
        public Func<Vector2, MeshObject, Rect, Vector3, Vector3, float, int> FindVertexAtScreenPos { get; set; }

        /// <summary>
        /// スクリーン座標からレイを生成
        /// </summary>
        public Func<Vector2, Ray> ScreenPosToRay { get; set; }

        /// <summary>
        /// カメラのFOV（度）
        /// </summary>
        public float CameraFOV { get; set; } = 60f;

        /// <summary>
        /// ハンドル半径
        /// </summary>
        public float HandleRadius { get; set; } = 10f;

        // === 便利メソッド ===

        /// <summary>
        /// ワールド座標をスクリーン座標に変換（簡易版）
        /// </summary>
        public Vector2 WorldToScreen(Vector3 worldPos)
        {
            if (WorldToScreenPos == null) return Vector2.zero;
            return WorldToScreenPos(worldPos, PreviewRect, CameraPosition, CameraTarget);
        }

        /// <summary>
        /// 現在の選択モードを取得
        /// </summary>
        public MeshSelectMode CurrentSelectMode => SelectionState?.Mode ?? MeshSelectMode.Vertex;
    }
}
