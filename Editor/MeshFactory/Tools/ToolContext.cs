// Tools/ToolContext.cs
// 編集ツールに渡されるコンテキスト
// SelectionState/TopologyCache対応版

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 編集ツールに渡されるコンテキスト
    /// エディタの状態への読み書きアクセスを提供
    /// </summary>
    public class ToolContext
    {
        // === メッシュデータ ===

        /// <summary>MeshData</summary>
        public MeshData MeshData { get; set; }

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

        // === コールバック ===

        /// <summary>選択変更を記録</summary>
        public Action<HashSet<int>, HashSet<int>> RecordSelectionChange { get; set; }

        /// <summary>MeshDataからUnity Meshを更新</summary>
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
        public Func<Vector2, MeshData, Rect, Vector3, Vector3, float, int> FindVertexAtScreenPos { get; set; }

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