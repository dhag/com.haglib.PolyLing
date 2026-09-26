// SmoothEdgesTool.cs
// 辺・線分の平滑化ツール - 選択した辺／補助線分のチェーンをラプラシアン緩和で滑らかにする。
// マウス入力を持たず、パネルのボタンから TriggerSmooth() で実行する。
// Player 専用（AlignVerticesTool と同じく DrawSettingsUI() は持たない）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 辺・線分の平滑化ツール。
    ///
    /// 【隣接の定義】
    /// 平均に使う隣接は「選択した辺／補助線分だけを辿った隣接」に限定する。
    /// メッシュ全体のトポロジー隣接（SelectionHelper.BuildVertexAdjacency）は使わない。
    /// 選択外の面に属する頂点へ引っ張られないようにするため。
    ///
    /// 【端点】
    /// 選択チェーン内で次数 1 の頂点を開始点／終了点とみなす。
    /// FixEndpoints が true のとき固定する。閉ループには次数 1 が無いため影響しない。
    /// 分岐点（次数 3 以上）は FixEndpoints に関係なく移動対象。
    /// </summary>
    public partial class SmoothEdgesTool : IEditTool
    {
        public string Name        => "SmoothEdges";
        public string DisplayName => "SmoothEdges";

        // ================================================================
        // 設定
        // ================================================================

        private SmoothEdgesSettings _settings = new SmoothEdgesSettings();
        public IToolSettings Settings => _settings;

        public float Strength     { get => _settings.Strength;     set => _settings.Strength     = value; }
        public int   Iterations   { get => _settings.Iterations;   set => _settings.Iterations   = value; }
        public bool  FixEndpoints { get => _settings.FixEndpoints; set => _settings.FixEndpoints = value; }
        public bool  LockX        { get => _settings.LockX;        set => _settings.LockX        = value; }
        public bool  LockY        { get => _settings.LockY;        set => _settings.LockY        = value; }
        public bool  LockZ        { get => _settings.LockZ;        set => _settings.LockZ        = value; }

        // ================================================================
        // 統計（SubPanel 表示用）
        // ================================================================

        /// <summary>選択から集めた辺／線分の本数（重複を除いた VertexPair 数）。</summary>
        public int SegmentCount { get; private set; }

        /// <summary>チェーンに含まれる頂点数。</summary>
        public int ChainVertexCount { get; private set; }

        /// <summary>次数 1 の頂点数（開始点・終了点）。</summary>
        public int EndpointCount { get; private set; }

        /// <summary>実際に移動対象となる頂点数（現在の FixEndpoints 設定を反映）。</summary>
        public int MovableVertexCount { get; private set; }

        /// <summary>統計を計算済みか。</summary>
        public bool StatsCalculated { get; private set; }

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
            RecalculateStats();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
            StatsCalculated = false;
        }

        public void Reset()
        {
            StatsCalculated = false;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        /// <summary>平滑化を実行する。</summary>
        public void TriggerSmooth() => ExecuteSmooth();

        /// <summary>統計だけ再計算する（選択変更後のパネル更新用）。</summary>
        public void RecalculateStats()
        {
            SegmentCount       = 0;
            ChainVertexCount   = 0;
            EndpointCount      = 0;
            MovableVertexCount = 0;
            StatsCalculated    = false;

            var mesh = _context?.ActiveMeshObject;
            if (mesh == null) return;

            var adjacency = BuildChainAdjacency(mesh, _context.SelectionState, out int segmentCount);
            if (adjacency == null) return;

            SegmentCount     = segmentCount;
            ChainVertexCount = adjacency.Count;

            foreach (var kv in adjacency)
            {
                int degree = kv.Value.Count;
                if (degree <= 1) EndpointCount++;
                if (IsMovable(degree)) MovableVertexCount++;
            }

            StatsCalculated = true;
        }

        // ================================================================
        // チェーン構築
        // ================================================================

        /// <summary>
        /// 選択中の辺（SelectionState.Edges）と補助線分（SelectionState.Lines）から
        /// 頂点隣接リストを作る。線分は 2 頂点面なので面インデックスから両端を引く。
        /// </summary>
        /// <param name="segmentCount">重複を除いた辺／線分の本数</param>
        /// <returns>頂点インデックス → 隣接頂点集合。対象が無ければ null</returns>
        private static Dictionary<int, HashSet<int>> BuildChainAdjacency(
            MeshObject mesh, SelectionState state, out int segmentCount)
        {
            segmentCount = 0;
            if (mesh == null || state == null) return null;

            var pairs = new HashSet<VertexPair>();

            // 面の辺
            foreach (var pair in state.Edges)
            {
                if (!IsValidPair(mesh, pair)) continue;
                pairs.Add(pair);
            }

            // 補助線分（頂点数 2 の面）
            foreach (int faceIdx in state.Lines)
            {
                if (faceIdx < 0 || faceIdx >= mesh.FaceCount) continue;
                var face = mesh.Faces[faceIdx];
                if (face == null || face.VertexCount != 2) continue;

                var pair = new VertexPair(face.VertexIndices[0], face.VertexIndices[1]);
                if (!IsValidPair(mesh, pair)) continue;
                pairs.Add(pair);
            }

            if (pairs.Count == 0) return null;

            var adjacency = new Dictionary<int, HashSet<int>>();
            foreach (var pair in pairs)
            {
                AddAdjacency(adjacency, pair.V1, pair.V2);
                AddAdjacency(adjacency, pair.V2, pair.V1);
            }

            segmentCount = pairs.Count;
            return adjacency;
        }

        private static bool IsValidPair(MeshObject mesh, VertexPair pair)
        {
            if (!pair.IsValid) return false;
            if (pair.V1 < 0 || pair.V1 >= mesh.VertexCount) return false;
            if (pair.V2 < 0 || pair.V2 >= mesh.VertexCount) return false;
            return true;
        }

        private static void AddAdjacency(Dictionary<int, HashSet<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out var set))
            {
                set = new HashSet<int>();
                adjacency[from] = set;
            }
            set.Add(to);
        }

        /// <summary>次数から移動対象か判定する。</summary>
        private bool IsMovable(int degree)
        {
            if (degree <= 0) return false;
            if (FixEndpoints && degree <= 1) return false;
            return true;
        }

        // ================================================================
        // 平滑化実行
        // ================================================================

        /// <summary>
        /// 平滑化後の座標を計算する。メッシュは書き換えない。
        /// 確定（ExecuteSmooth）とプレビュー（UpdatePreview）で共用する。
        /// </summary>
        /// <param name="movable">移動対象の頂点。対象が無ければ空</param>
        /// <returns>チェーン上の全頂点の計算後座標。対象が無ければ null</returns>
        private Dictionary<int, Vector3> ComputeSmoothed(
            MeshObject mesh, SelectionState state, out List<int> movable, out int segmentCount)
        {
            movable = new List<int>();

            var adjacency = BuildChainAdjacency(mesh, state, out segmentCount);
            if (adjacency == null) return null;

            // 移動対象
            foreach (var kv in adjacency)
            {
                if (IsMovable(kv.Value.Count)) movable.Add(kv.Key);
            }
            if (movable.Count == 0) return null;

            int iterations = Mathf.Max(1, Iterations);
            float strength = Strength;

            // 作業用の位置テーブル。チェーン上の全頂点（固定端点も隣接平均の材料になる）を持つ。
            var current = new Dictionary<int, Vector3>(adjacency.Count);
            foreach (var kv in adjacency)
                current[kv.Key] = mesh.Vertices[kv.Key].Position;

            var next = new Dictionary<int, Vector3>(movable.Count);

            for (int it = 0; it < iterations; it++)
            {
                // Jacobi 法。同一反復内では直前の反復の位置だけを参照する。
                next.Clear();

                foreach (int v in movable)
                {
                    var neighbors = adjacency[v];
                    if (neighbors.Count == 0) continue;

                    Vector3 sum = Vector3.zero;
                    foreach (int n in neighbors) sum += current[n];
                    Vector3 avg = sum / neighbors.Count;

                    Vector3 src = current[v];
                    Vector3 dst = Vector3.Lerp(src, avg, strength);

                    if (LockX) dst.x = src.x;
                    if (LockY) dst.y = src.y;
                    if (LockZ) dst.z = src.z;

                    next[v] = dst;
                }

                foreach (var kv in next) current[kv.Key] = kv.Value;
            }

            return current;
        }

        private void ExecuteSmooth()
        {
            // プレビューが書いた座標を元へ戻してから計算する（Undo の「前」を元形状にするため）。
            EndPreview();

            var ctx = _context;
            var mesh = ctx?.ActiveMeshObject;
            if (mesh == null) return;

            MeshObjectSnapshot before =
                ctx.UndoController != null && ctx.ActiveMeshContext != null
                    ? MeshObjectSnapshot.Capture(
                        ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState)
                    : null;

            var current = ComputeSmoothed(mesh, ctx.SelectionState, out var movable, out int segmentCount);
            if (current == null) return;
            int iterations = Mathf.Max(1, Iterations);

            // 反映
            int movedCount = 0;
            foreach (int v in movable)
            {
                Vector3 newPos = current[v];
                var vertex = mesh.Vertices[v];
                if (newPos == vertex.Position) continue;

                vertex.Position = newPos;
                movedCount++;
            }

            if (movedCount > 0)
            {
                mesh.InvalidatePositionCache();
                ctx.SyncMesh?.Invoke();

                if (ctx.UndoController != null && before != null)
                {
                    var after = MeshObjectSnapshot.Capture(
                        ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                    ctx.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        ctx.UndoController, before, after, ctx.SelectionState, "Smooth Edges"));
                }

                Debug.Log($"[SmoothEdgesTool] Smoothed {movedCount} vertices"
                          + $" (segments={segmentCount}, iterations={iterations})");
            }

            RecalculateStats();
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // プレビュー
        // ================================================================
        //
        // 計算結果を MeshObject に直接書くが Undo は積まない。
        // 書き換えた頂点の元座標を持ち、更新のたびに元へ戻してから計算し直す。
        // メッシュ・同期口は開始時のものを保持する（途中で Deactivate されても戻せるように）。

        private bool                     _previewActive;
        private MeshContext              _previewMc;
        private Dictionary<int, Vector3> _previewBackup;
        private System.Action<MeshContext> _previewSync;
        private System.Action            _previewRepaint;

        /// <summary>プレビュー中か。</summary>
        public bool IsPreviewing => _previewActive;

        /// <summary>
        /// プレビューを開始または更新する。
        /// 前回書いた座標を元へ戻し、今の選択と設定で計算し直して書き込む。
        /// </summary>
        public void UpdatePreview()
        {
            RestorePreviewPositions();
            _previewActive = true;

            var ctx = _context;
            var mc  = ctx?.ActiveMeshContext;
            var mesh = mc?.MeshObject;
            if (mesh == null) return;

            var current = ComputeSmoothed(mesh, ctx.SelectionState, out var movable, out _);

            _previewMc      = mc;
            _previewBackup  = new Dictionary<int, Vector3>(movable.Count);
            _previewSync    = ctx.SyncMeshContextPositionsOnly;
            _previewRepaint = ctx.Repaint;

            if (current == null) { _previewRepaint?.Invoke(); return; }

            int movedCount = 0;
            foreach (int v in movable)
            {
                var vertex = mesh.Vertices[v];
                Vector3 newPos = current[v];
                if (newPos == vertex.Position) continue;

                _previewBackup[v] = vertex.Position;
                vertex.Position = newPos;
                movedCount++;
            }

            if (movedCount > 0)
            {
                mesh.InvalidatePositionCache();
                _previewSync?.Invoke(mc);
            }
            _previewRepaint?.Invoke();
        }

        /// <summary>座標を元へ戻してプレビューを終える。プレビュー中でなければ何もしない。</summary>
        public void EndPreview()
        {
            if (!_previewActive) return;
            var repaint = _previewRepaint;
            RestorePreviewPositions();
            _previewActive = false;
            repaint?.Invoke();
        }

        /// <summary>プレビューが書いた座標を元へ戻し、保持を空にする。プレビュー状態は変えない。</summary>
        private void RestorePreviewPositions()
        {
            var mc   = _previewMc;
            var mesh = mc?.MeshObject;
            var backup = _previewBackup;

            if (mesh != null && backup != null && backup.Count > 0)
            {
                foreach (var kv in backup)
                {
                    if (kv.Key < 0 || kv.Key >= mesh.VertexCount) continue;
                    mesh.Vertices[kv.Key].Position = kv.Value;
                }
                mesh.InvalidatePositionCache();
                _previewSync?.Invoke(mc);
            }

            _previewMc      = null;
            _previewBackup  = null;
            _previewSync    = null;
            _previewRepaint = null;
        }
    }
}
