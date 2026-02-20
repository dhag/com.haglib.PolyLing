// Tools/ToolContext.cs
// 編集ツールに渡されるコンテキスト
// SelectionState/TopologyCache対応版
// Phase 3: ModelContext統合
// Phase 4: PrimitiveMeshTool対応（メッシュ作成コールバック追加）
// Phase 5: マテリアル操作コールバック追加（MQOインポート対応）
// Phase 6: ホバー/クリック整合性対応（LastHoverHitResult追加）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Model;
using Poly_Ling.Rendering;
using Poly_Ling.Materials;
using Poly_Ling.Commands;

// MeshContentはSimpleMeshFactoryのネストクラスを参照
////using MeshContext = MeshContext;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 編集ツールに渡されるコンテキスト
    /// エディタの状態への読み書きアクセスを提供
    /// </summary>
    public class ToolContext
    {
        // ================================================================
        // ホバー/クリック整合性（Phase 6追加）
        // ================================================================
        // 
        // 【背景】
        // ホバー時（MouseMove）とクリック時（MouseDown）で異なるヒットテストを
        // 使用すると、選択対象がずれる不具合が発生する。
        // 
        // 【解決策】
        // SimpleMeshFactoryがホバー時にGPUヒットテスト結果を保存し、
        // ToolContextを通じてツールに渡す。ツールはこの結果を優先的に使用する。
        // ================================================================

        /// <summary>
        /// 最後のホバー時GPUヒットテスト結果
        /// ツールはこれを優先的に使用してホバー/クリックの整合性を保つ
        /// </summary>
        public GPUHitTestResult? LastHoverHitResult { get; set; }

        /// <summary>
        /// ホバー結果の頂点ヒット判定閾値（ピクセル）
        /// </summary>
        public float HoverVertexRadius { get; set; } = 12f;

        /// <summary>
        /// ホバー結果の線分ヒット判定閾値（ピクセル）
        /// </summary>
        public float HoverLineDistance { get; set; } = 18f;

        /// <summary>
        /// ホバー結果から頂点インデックスを取得（ヒットしていなければ-1）
        /// </summary>
        public int GetHoverVertexIndex()
        {
            if (!LastHoverHitResult.HasValue) return -1;
            var result = LastHoverHitResult.Value;
            return result.HasVertexHit(HoverVertexRadius) ? result.NearestVertexIndex : -1;
        }

        /// <summary>
        /// ホバー結果から線分インデックスを取得（ヒットしていなければ-1）
        /// </summary>
        public int GetHoverLineIndex()
        {
            if (!LastHoverHitResult.HasValue) return -1;
            var result = LastHoverHitResult.Value;
            return result.HasLineHit(HoverLineDistance) ? result.NearestLineIndex : -1;
        }

        /// <summary>
        /// ホバー結果から面インデックスを取得（ヒットしていなければ-1）
        /// </summary>
        public int GetHoverFaceIndex()
        {
            if (!LastHoverHitResult.HasValue) return -1;
            var result = LastHoverHitResult.Value;
            return result.HasFaceHit ? result.GetNearestFaceIndex() : -1;
        }

        // === メッシュデータ ===

        /// <summary>選択先頭メッシュのMeshObject（単一メッシュ操作用便宜アクセサ）</summary>
        public MeshObject FirstSelectedMeshObject => FirstSelectedMeshContext?.MeshObject;

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

        // === 表示変換 ===

        /// <summary>表示用変換行列（Local/Worldトランスフォーム表示モード用）</summary>
        public Matrix4x4 DisplayMatrix { get; set; } = Matrix4x4.identity;

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
        public WorkPlaneContext WorkPlane { get; set; }

        // === マテリアル ===

        /// <summary>カレントマテリアルインデックス（面生成時に使用）</summary>
        public int CurrentMaterialIndex { get; set; } = 0;

        /// <summary>マテリアルリスト（読み取り専用参照）</summary>
        public IReadOnlyList<Material> Materials { get; set; }

        // === モデルコンテキスト（Phase 3追加） ===

        /// <summary>モデルコンテキスト（メッシュリスト管理）</summary>
        public ModelContext Model { get; set; }

        /// <summary>選択中の全MeshContext</summary>
        public List<MeshContext> SelectedMeshContexts => Model?.SelectedMeshContexts ?? new List<MeshContext>();

        /// <summary>選択リストの先頭MeshContext（便宜アクセサ）</summary>
        public MeshContext FirstSelectedMeshContext => Model?.FirstSelectedMeshContext;

        /// <summary>選択リストの先頭インデックス（便宜アクセサ）</summary>
        public int FirstSelectedIndex => Model?.FirstSelectedIndex ?? -1;

        /// <summary>メッシュリスト（読み取り専用）</summary>
        public IReadOnlyList<MeshContext> MeshList => Model?.MeshContextList;

        /// <summary>選択中のメッシュインデックスリスト</summary>
        public List<int> SelectedMeshIndices => Model?.SelectedMeshIndices ?? new List<int>();

        /// <summary>メッシュが選択されているか</summary>
        public bool HasMeshSelection => Model?.HasMeshSelection ?? false;

        /// <summary>ボーンが選択されているか</summary>
        public bool HasBoneSelection => Model?.HasBoneSelection ?? false;

        /// <summary>頂点モーフが選択されているか</summary>
        public bool HasMorphSelection => Model?.HasMorphSelection ?? false;

        /// <summary>有効なメッシュが選択されているか</summary>
        public bool HasValidMeshSelection => Model?.HasValidMeshContextSelection ?? false;

        // === モデル操作コールバック（NewModelモード対応） ===

        /// <summary>プロジェクトコンテキスト参照</summary>
        public ProjectContext Project { get; set; }

        /// <summary>新規モデルを作成してカレントに設定（Undo対応）</summary>
        public Func<string, ModelContext> CreateNewModel { get; set; }

        /// <summary>モデルを選択（Undo対応）</summary>
        public Action<int> SelectModel { get; set; }

        /// <summary>モデル数</summary>
        public int ModelCount => Project?.ModelCount ?? 0;

        /// <summary>現在のモデルインデックス</summary>
        public int CurrentModelIndex => Project?.CurrentModelIndex ?? 0;

        // === メッシュリスト操作コールバック（Undo対応） ===

        /// <summary>メッシュコンテキストを追加（Undo対応）</summary>
        public Action<MeshContext> AddMeshContext { get; set; }
        /// <summary>メッシュコンテキストを複数追加（Undo対応・バッチ）</summary>
        public Action<IList<MeshContext>> AddMeshContexts { get; set; }

        /// <summary>メッシュコンテキストを削除（Undo対応）</summary>
        public Action<int> RemoveMeshContext { get; set; }

        /// <summary>メッシュを選択（Undo対応）</summary>
        public Action<int> SelectMeshContext { get; set; }

        /// <summary>v2.1: メッシュ選択変更時のコールバック（複数選択対応・GPUバッファ同期用）</summary>
        public Action OnMeshSelectionChanged { get; set; }

        /// <summary>メッシュを複製（Undo対応）</summary>
        public Action<int> DuplicateMeshContent { get; set; }

        /// <summary>メッシュの順序を変更（Undo対応）</summary>
        public Action<int, int> ReorderMeshContext { get; set; }

        /// <summary>メッシュ属性を変更（Undo対応）</summary>
        public Action<IList<MeshAttributeChange>> UpdateMeshAttributes { get; set; }

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

        /// <summary>マテリアル参照を複数追加（ソースパス情報付き）</summary>
        public Action<IList<MaterialReference>> AddMaterialReferences { get; set; }

        /// <summary>マテリアルリストを置換（既存を全削除して新規設定）</summary>
        public Action<IList<Material>> ReplaceMaterials { get; set; }

        /// <summary>マテリアル参照リストを置換（ソースパス情報付き）</summary>
        public Action<IList<MaterialReference>> ReplaceMaterialReferences { get; set; }

        /// <summary>カレントマテリアルインデックスを設定</summary>
        public Action<int> SetCurrentMaterialIndex { get; set; }

        // === コマンドキュー ===

        /// <summary>コマンドキュー（すべての操作はここ経由）</summary>
        public CommandQueue CommandQueue { get; set; }

        // === コールバック ===

        /// <summary>選択変更を記録</summary>
        public Action<HashSet<int>, HashSet<int>> RecordSelectionChange { get; set; }

        /// <summary>MeshObjectからUnity Meshを更新</summary>
        public Action SyncMesh { get; set; }

        /// <summary>MeshObjectからUnity Meshの位置のみ更新（軽量版）</summary>
        public Action SyncMeshPositionsOnly { get; set; }

        /// <summary>指定MeshContextの位置のみ更新（非選択メッシュにも対応）</summary>
        public Action<MeshContext> SyncMeshContextPositionsOnly { get; set; }

        /// <summary>画面再描画を要求</summary>
        public Action Repaint { get; set; }

        /// <summary>カメラ注目点を指定位置に移動</summary>
        public Action<Vector3> FocusCameraOn { get; set; }

        // === 更新モード制御 ===

        /// <summary>
        /// 頂点ドラッグ開始時に呼ぶ（ワイヤ/頂点再構築・ヒットテスト等をスキップ）
        /// 対応: MoveTool, SculptTool, SimpleMorphPanel等
        /// </summary>
        public Action EnterTransformDragging { get; set; }

        /// <summary>
        /// ホバーハイライトの抑止設定（ブラシ系ツール用）
        /// true で抑止、false で通常
        /// </summary>
        public Action<bool> SetSuppressHover { get; set; }

        /// <summary>
        /// 頂点ドラッグ終了時に呼ぶ（Normalモードに復帰）
        /// </summary>
        public Action ExitTransformDragging { get; set; }

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

        // ================================================================
        // トポロジカル変更時の選択状態管理
        // ================================================================
        // 
        // 【方針】
        // トポロジカル変更後の選択状態は以下のルールで処理する：
        // 
        // 1. 削除を伴う変更 → 選択をクリア
        //    例: MergeVertices, DeleteVertex, DeleteFace
        //    理由: インデックスがずれるため追跡が困難
        // 
        // 2. 後ろに追加する変更 → 選択を維持
        //    例: AddVertex, AddFace, Extrude（新規要素を末尾に追加）
        //    理由: 既存インデックスは変わらない
        // 
        // 3. 挿入を伴う変更 → クリアか維持を都度判断
        //    例: Subdivide, InsertEdgeLoop
        //    困難な場合はすぐにクリアで妥協する
        // 
        // 【実装ルール】
        // - 削除を伴うツールは OnTopologyChanged() を使用
        // - 追加のみのツールは SyncMesh() + Repaint() を使用
        // - 個別に選択を調整する必要がある場合のみカスタム処理
        // 
        // 【GPUバッファ更新について】
        // 現状、SyncMesh（SyncMeshFromData）は頂点位置の軽量更新のみ。
        // トポロジカル変更（頂点数/面数の変化）時はGPUバッファの再構築が必要。
        // これはNotifyTopologyChangedコールバック経由で行う。
        // 
        // 【将来の改善】
        // 現在は暫定的な実装。理想的にはダーティフラグ方式で
        // 必要な部分だけ更新すべきだが、バグが多く複雑なため保留中。
        // まず現状のバグを全て取り除いてから、ダーティフラグ方式への
        // 移行を検討する。
        // ================================================================

        /// <summary>
        /// GPUバッファのトポロジ再構築を通知するコールバック
        /// トポロジカル変更（頂点数/面数の変化）時に呼び出す
        /// SyncMeshは位置更新のみで軽量、これは重い処理
        /// </summary>
        public Action NotifyTopologyChanged { get; set; }

        /// <summary>
        /// トポロジカル変更後の標準処理（削除を伴う場合）
        /// すべての選択をクリアし、メッシュを更新して再描画
        /// </summary>
        public void OnTopologyChanged()
        {
            ClearAllSelections();
            SyncMesh?.Invoke();
            NotifyTopologyChanged?.Invoke();  // GPUバッファ再構築（重い）
            Repaint?.Invoke();
        }

        /// <summary>
        /// すべての選択状態をクリア
        /// </summary>
        public void ClearAllSelections()
        {
            SelectedVertices?.Clear();
            SelectionState?.ClearAll();
        }
    }

    /// <summary>
    /// メッシュ属性変更データ
    /// UpdateMeshAttributesコマンドで使用
    /// </summary>
    public struct MeshAttributeChange
    {
        /// <summary>対象メッシュのインデックス</summary>
        public int Index;
        
        /// <summary>可視性（nullで変更なし）</summary>
        public bool? IsVisible;
        
        /// <summary>ロック状態（nullで変更なし）</summary>
        public bool? IsLocked;
        
        /// <summary>ミラータイプ（nullで変更なし）</summary>
        public int? MirrorType;
        
        /// <summary>名前（nullで変更なし）</summary>
        public string Name;

        public override string ToString()
        {
            var parts = new List<string> { $"[{Index}]" };
            if (IsVisible.HasValue) parts.Add($"Visible={IsVisible.Value}");
            if (IsLocked.HasValue) parts.Add($"Locked={IsLocked.Value}");
            if (MirrorType.HasValue) parts.Add($"Mirror={MirrorType.Value}");
            if (Name != null) parts.Add($"Name={Name}");
            return string.Join(" ", parts);
        }
    }
}
