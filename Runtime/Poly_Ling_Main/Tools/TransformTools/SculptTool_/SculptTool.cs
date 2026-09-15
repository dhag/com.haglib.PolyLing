// Tools/SculptTool.cs
// スカルプトツール - ブラシによるメッシュ変形（複数メッシュ対応）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// スカルプトツールのモード
    /// </summary>
    public enum SculptMode
    {
        /// <summary>盛り上げ/盛り下げ</summary>
        Draw,
        /// <summary>滑らかにする</summary>
        Smooth,
        /// <summary>膨らます</summary>
        Inflate,
        /// <summary>平らにする</summary>
        Flatten
    }

    /// <summary>
    /// スカルプトツール
    /// </summary>
    public partial class SculptTool : IEditTool
    {

            public string Name => "Sculpt";
            public string DisplayName => "Sculpt";

            // ================================================================
            // 設定
            // ================================================================

            private SculptSettings _settings = new SculptSettings();
            public IToolSettings Settings => _settings;

            private SculptMode Mode
            {
                get => _settings.Mode;
                set => _settings.Mode = value;
            }

            private float BrushRadius
            {
                get => _settings.BrushRadius;
                set => _settings.BrushRadius = value;
            }

            private float Strength
            {
                get => _settings.Strength;
                set => _settings.Strength = value;
            }

            private bool Invert
            {
                get => _settings.Invert;
                set => _settings.Invert = value;
            }

            private FalloffType Falloff
            {
                get => _settings.Falloff;
                set => _settings.Falloff = value;
            }

            // === ドラッグ状態 ===
            private bool _isDragging;
            private Vector2 _currentScreenPos;

            // === 複数メッシュ: 開始時位置 ===
            private Dictionary<int, Vector3[]> _originalPositions = new Dictionary<int, Vector3[]>();

            // === ドラッグ 1 ストロークの点列（ワールド座標） ===
            // 確定を SculptStrokeCommand 1 本に寄せるために保持する。
            // ブラシ中心をローカルで持つと、複数メッシュを 1 コマンドで表せない。
            private readonly List<Vector3> _strokeWorldCenters  = new List<Vector3>();
            private readonly List<Vector3> _strokeWorldViewDirs = new List<Vector3>();
            private readonly List<int>     _strokeMeshIndices   = new List<int>();

            // === メッシュ別キャッシュ ===
            private Dictionary<int, Dictionary<int, HashSet<int>>> _adjacencyCachePerMesh;
            private Dictionary<int, Dictionary<int, Vector3>> _vertexNormalsCachePerMesh;

            // === モード選択用 ===
            private static readonly SculptMode[] ModeValues = {
            SculptMode.Draw,
            SculptMode.Smooth,
            SculptMode.Inflate,
            SculptMode.Flatten
        };

            // ================================================================
            // IEditTool 実装
            // ================================================================

            public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
            {
                var model = ctx.Model;
                if (model == null || model.SelectedDrawableMeshIndices.Count == 0) return false;

                _isDragging = true;
                _currentScreenPos = mousePos;
                ctx.EnterTransformDragging?.Invoke();

                // ストロークの点列。確定をコマンド 1 本に寄せるため、
                // ApplyBrush が 1 点ごとにワールド座標で積む。
                _strokeWorldCenters.Clear();
                _strokeWorldViewDirs.Clear();
                _strokeMeshIndices.Clear();
                foreach (int mi in model.SelectedDrawableMeshIndices) _strokeMeshIndices.Add(mi);

                // 全選択メッシュの開始時位置を保存・キャッシュ構築
                _originalPositions.Clear();
                _adjacencyCachePerMesh = new Dictionary<int, Dictionary<int, HashSet<int>>>();
                _vertexNormalsCachePerMesh = new Dictionary<int, Dictionary<int, Vector3>>();

                foreach (int meshIdx in model.SelectedDrawableMeshIndices)
                {
                    var meshContext = model.GetMeshContext(meshIdx);
                    if (meshContext?.MeshObject == null) continue;

                    var meshObject = meshContext.MeshObject;

                    // 開始時位置保存
                    var positions = new Vector3[meshObject.VertexCount];
                    for (int i = 0; i < meshObject.VertexCount; i++)
                        positions[i] = meshObject.Vertices[i].Position;
                    _originalPositions[meshIdx] = positions;

                    // キャッシュ構築。法線はワールドで作る（GPU のワールド座標を渡す）。
                    BuildCachesForMesh(meshIdx, meshObject,
                                       GetWorldPositions(ctx, meshContext, meshIdx));
                }

                ApplyBrush(ctx, mousePos);
                return true;
            }

            public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
            {
                if (!_isDragging) return false;

                _currentScreenPos = mousePos;
                ApplyBrush(ctx, mousePos);

                return true;
            }

            public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
            {
                if (!_isDragging) return false;

                _isDragging = false;
                ctx.ExitTransformDragging?.Invoke();

                // ストロークは 1 ストローク = 1 コマンドに寄せてある。
                // 呼び出し側（SculptToolHandler）が TryTakeStrokeFromDrag で
                // 点列を取り出してコマンドを送るので、ここでは確定しない。
                // 取り出されなかった場合のみ従来どおり積む。
                if (!StrokePending) CommitStroke(ctx);

                ctx.Repaint?.Invoke();
                return true;
            }

            /// <summary>
            /// ストロークを確定する。動いた頂点を Undo へ記録し、作業キャッシュを捨てる。
            /// マウス経路（OnMouseUp）とコマンド経路（ApplyStrokeFromCommand）で共有する。
            /// </summary>
            private void CommitStroke(ToolContext ctx)
            {
                // MultiMeshVertexMoveRecordで記録
                var model = ctx.Model;
                if (model != null && _originalPositions.Count > 0 && ctx.UndoController != null)
                {
                    var allEntries = new List<MeshMoveEntry>();

                    foreach (var kv in _originalPositions)
                    {
                        int meshIdx = kv.Key;
                        var oldPositions = kv.Value;
                        var meshContext = model.GetMeshContext(meshIdx);
                        if (meshContext?.MeshObject == null) continue;

                        var meshObject = meshContext.MeshObject;

                        // 変更された頂点を収集
                        var indices = new List<int>();
                        var oldPos = new List<Vector3>();
                        var newPos = new List<Vector3>();

                        for (int i = 0; i < meshObject.VertexCount && i < oldPositions.Length; i++)
                        {
                            if (meshObject.Vertices[i].Position != oldPositions[i])
                            {
                                indices.Add(i);
                                oldPos.Add(oldPositions[i]);
                                newPos.Add(meshObject.Vertices[i].Position);
                            }
                        }

                        if (indices.Count > 0)
                        {
                            allEntries.Add(new MeshMoveEntry
                            {
                                MeshContextIndex = meshIdx,
                                Indices = indices.ToArray(),
                                OldPositions = oldPos.ToArray(),
                                NewPositions = newPos.ToArray()
                            });

                            // OriginalPositionsキャッシュ更新
                            if (meshContext.OriginalPositions != null)
                            {
                                for (int i = 0; i < meshObject.VertexCount; i++)
                                {
                                    if (i < meshContext.OriginalPositions.Length)
                                        meshContext.OriginalPositions[i] = meshObject.Vertices[i].Position;
                                }
                            }
                        }
                    }

                    if (allEntries.Count > 0)
                    {
                        ctx.UndoController.MeshUndoContext.ParentModelContext = model;
                        ctx.UndoController.FocusVertexEdit();
                        var record = new MultiMeshVertexMoveRecord(allEntries.ToArray());
                        {
                            string __dbgDesc = $"Sculpt ({Mode})";
                            PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                            ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                        }
                    }
                }

                _originalPositions.Clear();
                _adjacencyCachePerMesh = null;
                _vertexNormalsCachePerMesh = null;
            }

            /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
            public void DrawGizmo(ToolContext ctx) { }

            public void OnActivate(ToolContext ctx) => Reset();
            public void OnDeactivate(ToolContext ctx) => Reset();

            public void Reset()
            {
                _isDragging = false;
                _originalPositions.Clear();
                _adjacencyCachePerMesh = null;
                _vertexNormalsCachePerMesh = null;
            }

            // ================================================================
            // ブラシ適用
            // ================================================================

            private void ApplyBrush(ToolContext ctx, Vector2 mousePos)
        {
            var model = ctx.Model;
            if (model == null) return;

            // マウス位置からレイを取得（ワールド空間）
            Ray ray = ctx.ScreenPosToRay(mousePos);

            // ブラシ中心のワールド座標を計算（全メッシュでレイキャスト）
            Vector3 brushCenterWorld = FindBrushCenter(ctx, ray);

            // ストローク 1 点ぶんを記録。TryTakeStrokeFromDrag が
            // SculptStrokeCommand として取り出す。
            if (_isDragging)
            {
                _strokeWorldCenters.Add(brushCenterWorld);
                _strokeWorldViewDirs.Add(ray.direction.normalized);
            }

            // 全選択メッシュにブラシ適用
            bool anyAffected = false;
            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext?.MeshObject == null) continue;

                // 当たり判定・変形ともワールド空間。書き戻しだけ頂点単位の逆行列を使う。
                var worldPos = GetWorldPositions(ctx, meshContext, meshIdx);
                _workMeshIdx = meshIdx;

                if (ApplyStrokeToMesh(meshIdx, meshContext.MeshObject, meshContext,
                                      brushCenterWorld, ray.direction.normalized,
                                      worldPos, ctx?.ShowBindPose ?? false))
                    anyAffected = true;
            }

            if (anyAffected)
            {
                // メッシュ更新
                ctx.SyncMesh?.Invoke();
                ctx.Repaint?.Invoke();
            }
        }

        /// <summary>
        /// 1 メッシュへ 1 点ぶんのブラシを掛ける。
        ///
        /// 【なぜ分けてあるか】
        ///   ApplyBrush は「どこに掛けるか」を画面のレイから決める。コマンド経由
        ///   （SculptStrokeCommand）はローカル座標の点列を直接持つので、レイの部分だけを
        ///   飛ばして同じ変形を通せるよう、掛ける処理をここへ切り出してある。
        ///   選択アルゴリズムを 2 組持たないための分割で、挙動は変えていない。
        /// </summary>
        /// <param name="viewDirLocal">
        /// 視線方向（このメッシュのローカル空間）。null で Draw の反転補正を行わない。
        /// </param>
        /// <returns>1 頂点でも範囲に入れば true。</returns>
        private bool ApplyStrokeToMesh(
            int meshIdx, MeshObject meshObject, MeshContext meshContext,
            Vector3 brushCenterWorld, Vector3? viewDirWorld,
            Vector3[] worldPositions, bool showBindPose)
        {
            if (meshObject == null) return false;

            // キャッシュ取得
            Dictionary<int, HashSet<int>> adjacencyCache = null;
            Dictionary<int, Vector3> vertexNormalsCache = null;
            _adjacencyCachePerMesh?.TryGetValue(meshIdx, out adjacencyCache);
            _vertexNormalsCachePerMesh?.TryGetValue(meshIdx, out vertexNormalsCache);

            // ブラシ範囲内の頂点を収集（当たり判定はワールド空間）
            var affectedVertices = GetVerticesInBrushRadius(
                meshObject, brushCenterWorld, Vector3.zero, adjacencyCache, worldPositions);
            if (affectedVertices.Count == 0) return false;

            // 変形はワールド空間で行い、結果だけ頂点単位の逆行列でローカルへ戻す。
            // 法線キャッシュも BuildCachesForMesh がワールドで作っている。
            var work = BuildWorkWorld(meshObject, meshContext, affectedVertices,
                                      worldPositions, showBindPose);

            switch (Mode)
            {
                case SculptMode.Draw:
                    ApplyDraw(work, affectedVertices, viewDirWorld, vertexNormalsCache);
                    break;
                case SculptMode.Smooth:
                    ApplySmooth(work, affectedVertices, adjacencyCache);
                    break;
                case SculptMode.Inflate:
                    ApplyInflate(work, affectedVertices, vertexNormalsCache);
                    break;
                case SculptMode.Flatten:
                    ApplyFlatten(work, affectedVertices, vertexNormalsCache);
                    break;
            }

            WriteBackWorld(meshObject, meshContext, work, showBindPose);
            return true;
        }

        /// <summary>
        /// 【臨時の作業配列】影響頂点とその隣接について、いまの表示ワールド座標を集める。
        /// Smooth が隣接を読むので、影響頂点だけでなく隣接も入れる。
        /// </summary>
        private Dictionary<int, Vector3> BuildWorkWorld(
            MeshObject mo, MeshContext mc,
            List<(int index, float weight)> affected,
            Vector3[] worldPositions, bool showBindPose)
        {
            var work = new Dictionary<int, Vector3>(affected.Count * 2);
            Dictionary<int, HashSet<int>> adj = null;
            _adjacencyCachePerMesh?.TryGetValue(_workMeshIdx, out adj);

            void Put(int i)
            {
                if (i < 0 || i >= mo.VertexCount || work.ContainsKey(i)) return;
                work[i] = (worldPositions != null && i < worldPositions.Length)
                    ? worldPositions[i]
                    // 【保険】GPU 値が無いときだけの CPU 経路（GetWorldPositions の注記）。
                    : mc.VertexMatrix(i, showBindPose).MultiplyPoint3x4(mo.Vertices[i].Position);
            }

            foreach (var (idx, _) in affected)
            {
                Put(idx);
                if (adj != null && adj.TryGetValue(idx, out var ns))
                    foreach (int n in ns) Put(n);
            }
            return work;
        }

        /// <summary>変形後のワールド座標を、頂点単位の逆行列でローカルへ戻して書き込む。</summary>
        private void WriteBackWorld(
            MeshObject mo, MeshContext mc, Dictionary<int, Vector3> work, bool showBindPose)
        {
            foreach (var kv in work)
            {
                int i = kv.Key;
                if (i < 0 || i >= mo.VertexCount) continue;
                mo.Vertices[i].Position =
                    mc.VertexMatrix(i, showBindPose).inverse.MultiplyPoint3x4(kv.Value);
            }
            mo.InvalidatePositionCache();
        }

        /// <summary>BuildWorkWorld が隣接キャッシュを引くための現在のメッシュ索引。</summary>
        private int _workMeshIdx = -1;

        /// <summary>
        /// コマンドで指定された点列にブラシを掛ける。
        ///
        /// 【なぜ要るか】
        ///   ApplyBrush はブラシ中心を画面のレイから求めるので、コマンド経由
        ///   （自動検証・MCP）からは通せない。点列を直接渡せる入口をここに置き、
        ///   変形と Undo 記録はマウス経路と同じ ApplyStrokeToMesh / CommitStroke を通す。
        ///
        /// 【視点を取らない】
        ///   Draw の「カメラ側へ盛り上げる」反転補正は視線方向が要るが、コマンドは
        ///   視点を持たない。補正なしで幾何法線の向きに従うので、同じコマンドは
        ///   カメラの向きによらず常に同じ結果になる。
        ///
        /// 【対象は 1 メッシュ】
        ///   マウス経路は選択中の全メッシュへ掛けるが、こちらは meshIdx の 1 本だけ。
        ///   コマンドが対象を明示しているので選択には依存させない。
        /// </summary>
        /// <returns>1 頂点でも動けば true。</returns>
        public bool ApplyStrokeFromCommand(
            ToolContext ctx,
            IReadOnlyList<int> meshIndices,
            IReadOnlyList<Vector3> worldCenters,
            IReadOnlyList<Vector3> worldViewDirs)
        {
            if (_isDragging) return false;   // マウスのストローク中は割り込ませない

            var model = ctx?.Model;
            if (model == null) return false;
            if (meshIndices == null || meshIndices.Count == 0) return false;
            if (worldCenters == null || worldCenters.Count == 0) return false;

            // OnMouseDown と同じ準備（開始時位置の保存とキャッシュ構築）を対象全部に行う
            _originalPositions.Clear();
            _adjacencyCachePerMesh     = new Dictionary<int, Dictionary<int, HashSet<int>>>();
            _vertexNormalsCachePerMesh = new Dictionary<int, Dictionary<int, Vector3>>();

            var targets = new List<int>();
            foreach (int meshIdx in meshIndices)
            {
                var mc = model.GetMeshContext(meshIdx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var positions = new Vector3[mo.VertexCount];
                for (int i = 0; i < mo.VertexCount; i++)
                    positions[i] = mo.Vertices[i].Position;
                _originalPositions[meshIdx] = positions;

                BuildCachesForMesh(meshIdx, mo, GetWorldPositions(ctx, mc, meshIdx));
                targets.Add(meshIdx);
            }
            if (targets.Count == 0) return false;

            // ApplyBrush と同じ順序（点が外、メッシュが内）で掛ける。
            // ブラシ中心と視線方向はメッシュごとにローカル化する。
            bool anyAffected = false;
            for (int i = 0; i < worldCenters.Count; i++)
            {
                Vector3? worldDir = (worldViewDirs != null && i < worldViewDirs.Count)
                    ? worldViewDirs[i]
                    : (Vector3?)null;

                foreach (int meshIdx in targets)
                {
                    var mc = model.GetMeshContext(meshIdx);
                    var mo = mc?.MeshObject;
                    if (mo == null) continue;

                    var worldPos = GetWorldPositions(ctx, mc, meshIdx);
                    _workMeshIdx = meshIdx;

                    if (ApplyStrokeToMesh(meshIdx, mo, mc, worldCenters[i], worldDir,
                                          worldPos, ctx?.ShowBindPose ?? false))
                        anyAffected = true;
                }
            }

            // Undo 記録とキャッシュ破棄はマウス経路と同じ処理を通す。
            CommitStroke(ctx);
            return anyAffected;
        }

        /// <summary>
        /// 取り出せるストローク結果があるか。TryTakeStrokeFromDrag が true を返す条件と同じ。
        /// </summary>
        public bool StrokePending
            => _strokeWorldCenters.Count > 0 && _originalPositions.Count > 0;

        /// <summary>
        /// ドラッグのストローク結果を取り出し、開始状態へ戻す。
        ///
        /// 【なぜ要るか】
        ///   1 ストローク = 1 コマンドにするため。ドラッグ中の変形はプレビューとして
        ///   扱い、確定時はここで開始状態へ戻して点列だけを返す。呼び出し側
        ///   （SculptToolHandler）が SculptStrokeCommand を送り、実際の変形と
        ///   Undo 記録は ApplyStrokeFromCommand が行う。
        ///
        /// 【戻す方法】
        ///   CommitStroke が Undo 記録の比較元に使う _originalPositions を
        ///   そのまま書き戻す。復元用の経路を別に作らない。
        /// </summary>
        public bool TryTakeStrokeFromDrag(
            ToolContext ctx,
            out int[] meshIndices,
            out Vector3[] worldCenters,
            out Vector3[] worldViewDirs)
        {
            meshIndices   = System.Array.Empty<int>();
            worldCenters  = System.Array.Empty<Vector3>();
            worldViewDirs = System.Array.Empty<Vector3>();

            if (!StrokePending) return false;

            var model = ctx?.Model;
            if (model == null) return false;

            meshIndices   = _strokeMeshIndices.ToArray();
            worldCenters  = _strokeWorldCenters.ToArray();
            worldViewDirs = _strokeWorldViewDirs.ToArray();

            // 開始位置へ戻す
            foreach (var kv in _originalPositions)
            {
                var mo = model.GetMeshContext(kv.Key)?.MeshObject;
                if (mo == null) continue;
                int n = Mathf.Min(mo.VertexCount, kv.Value.Length);
                for (int i = 0; i < n; i++) mo.Vertices[i].Position = kv.Value[i];
                mo.InvalidatePositionCache();
            }

            ctx.SyncMesh?.Invoke();

            _originalPositions.Clear();
            _adjacencyCachePerMesh     = null;
            _vertexNormalsCachePerMesh = null;
            _strokeWorldCenters.Clear();
            _strokeWorldViewDirs.Clear();
            _strokeMeshIndices.Clear();

            return true;
        }

        /// <summary>
        /// ワールド空間のレイと全選択メッシュの交点のうち、カメラに最も近い点を
        /// 「ワールド座標」で返す。
        ///
        /// 交差判定はワールド空間で行う（2026-09-15 変更）。三角形の頂点には
        /// GPU が出したワールド座標を使う。以前はレイをメッシュ 1 個の
        /// WorldMatrixInverse でローカルへ落としていたが、それはスキンド頂点が
        /// 実際に受ける行列ではないので、画面で指した面とは別の場所に当たっていた。
        /// </summary>
        private Vector3 FindBrushCenter(ToolContext ctx, Ray ray)
        {
            var model = ctx.Model;
            float closestDist = float.MaxValue;
            Vector3 closestPoint = ray.origin + ray.direction * 5f;

            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext?.MeshObject == null) continue;
                var meshObject = meshContext.MeshObject;

                var wp = GetWorldPositions(ctx, meshContext, meshIdx);
                if (wp == null) continue;

                foreach (var face in meshObject.Faces)
                {
                    if (face.VertexIndices.Count < 3) continue;
                    for (int i = 1; i < face.VertexIndices.Count - 1; i++)
                    {
                        int i0 = face.VertexIndices[0];
                        int i1 = face.VertexIndices[i];
                        int i2 = face.VertexIndices[i + 1];
                        if (i0 >= wp.Length || i1 >= wp.Length || i2 >= wp.Length) continue;

                        if (RayTriangleIntersection(ray, wp[i0], wp[i1], wp[i2], out float t) && t > 0)
                        {
                            Vector3 hitWorld = ray.origin + ray.direction * t;
                            float distWorld = Vector3.Distance(ray.origin, hitWorld);
                            if (distWorld < closestDist)
                            {
                                closestDist  = distWorld;
                                closestPoint = hitWorld;
                            }
                        }
                    }
                }
            }
            return closestPoint;
        }

        private bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            if (Mathf.Abs(a) < 1e-6f) return false;
            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0 || u + v > 1) return false;
            t = f * Vector3.Dot(edge2, q);
            return t > 1e-6f;
        }


        /// <summary>
        /// 【前方向は GPU の値を使う】
        /// 指定メッシュの全頂点について、いま画面に見えているワールド座標を返す。
        /// 取れないときは null。参照先は GPU が _worldPositionBuffer に出した値
        /// （ToolContext.GetMeshWorldPositions）。
        ///
        /// 【保険】GPU 値が取れないときだけ CPU 行列に落ちる。
        ///   通る条件はバッファ未構築・ビューポート未配線など、GPU 値が null の場合のみ。
        ///   落ち先は VertexMatrix(i, showBindPose) で、描画と同じ規則・同じ表示モード。
        ///   これは CPU 独自計算であり、本来は残すべきでないものを承知のうえで
        ///   保険として置いている（2026-09-15・利用者の指示による）。
        ///   新しいコードでこの分岐を真似しないこと。
        /// </summary>
        private Vector3[] GetWorldPositions(ToolContext ctx, MeshContext mc, int meshIdx)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return null;

            var gpu = ctx?.GetMeshWorldPositions?.Invoke(meshIdx);
            if (gpu != null && gpu.Length >= mo.VertexCount) return gpu;

            // 【保険】GPU 値が無いときだけの CPU 経路。上の注記を参照。
            bool showBind = ctx?.ShowBindPose ?? false;
            var arr = new Vector3[mo.VertexCount];
            for (int i = 0; i < mo.VertexCount; i++)
                arr[i] = mc.VertexMatrix(i, showBind).MultiplyPoint3x4(mo.Vertices[i].Position);
            return arr;
        }

        private List<(int index, float weight)> GetVerticesInBrushRadius(
            MeshObject meshObject, Vector3 brushCenterWorld, Vector3 brushCenterLocal,
            Dictionary<int, HashSet<int>> adjacencyCache, Vector3[] worldPositions)
        {
            // 当たり判定はワールド空間で行う（2026-09-15 変更）。
            // ブラシ中心は画面のレイから決まるワールドの値なので、メッシュ 1 個の
            // 行列でローカルへ落とすと、スキンド頂点や姿勢付きの非スキンドで
            // 画面で狙った場所と違うところに当たる。
            // BrushRadius の尺度もローカルからワールドへ変わる。
            bool useWorld = worldPositions != null && worldPositions.Length >= meshObject.VertexCount;
            Vector3 center = useWorld ? brushCenterWorld : brushCenterLocal;
            Vector3[] pos  = useWorld ? worldPositions   : meshObject.Positions;

            if (_settings.DistanceMode == DistanceMode.Link && adjacencyCache != null)
                return GetVerticesInBrushRadiusLink(meshObject, center, adjacencyCache, pos);

            return GetVerticesInBrushRadiusEuclidean(meshObject, center, pos);
        }

        // ユークリッド直線距離（従来）
        private List<(int index, float weight)> GetVerticesInBrushRadiusEuclidean(
            MeshObject meshObject, Vector3 brushCenter, Vector3[] pos)
        {
            var result = new List<(int, float)>();

            for (int i = 0; i < meshObject.VertexCount && i < pos.Length; i++)
            {
                float dist = Vector3.Distance(pos[i], brushCenter);
                if (dist <= BrushRadius)
                {
                    float t = BrushRadius > 0f ? dist / BrushRadius : 0f;
                    float weight = FalloffHelper.Calculate(t, Falloff);
                    result.Add((i, weight));
                }
            }

            return result;
        }

        // リンク距離（ブラシ中心の最近傍頂点を始点に辺をたどった距離）
        private List<(int index, float weight)> GetVerticesInBrushRadiusLink(
            MeshObject meshObject, Vector3 brushCenter,
            Dictionary<int, HashSet<int>> adjacencyCache, Vector3[] pos)
        {
            var result = new List<(int, float)>();

            // ブラシ中心に最も近い頂点を始点とする
            int seed = -1;
            float minDist = float.MaxValue;
            for (int i = 0; i < meshObject.VertexCount && i < pos.Length; i++)
            {
                float dist = Vector3.Distance(pos[i], brushCenter);
                if (dist < minDist)
                {
                    minDist = dist;
                    seed = i;
                }
            }

            if (seed < 0) return result;

            var field = LinkDistanceField.Compute(adjacencyCache, pos, new[] { seed }, BrushRadius);

            foreach (var kvp in field)
            {
                float t = BrushRadius > 0f ? kvp.Value / BrushRadius : 0f;
                float weight = FalloffHelper.Calculate(t, Falloff);
                result.Add((kvp.Key, weight));
            }

            return result;
        }

        // ================================================================
        // 各モードの実装
        // ================================================================

        /// <summary>
        /// Draw: 盛り上げ/盛り下げ
        /// </summary>
        /// <param name="viewDir">
        /// 視線方向（メッシュのローカル空間）。カメラ側へ盛り上げるための反転補正に使う。
        /// null のときは補正せず、幾何法線の向きにそのまま従う。
        /// コマンド経由（SculptStrokeCommand）は視点を持たないので null が入る。
        /// </param>
        private void ApplyDraw(Dictionary<int, Vector3> work, List<(int index, float weight)> vertices, Vector3? viewDir, Dictionary<int, Vector3> vertexNormalsCache)
        {
            if (vertexNormalsCache == null) return;

            // ブラシ中心の平均法線を計算
            Vector3 avgNormal = Vector3.zero;
            foreach (var (idx, weight) in vertices)
            {
                if (vertexNormalsCache.TryGetValue(idx, out var normal))
                {
                    avgNormal += normal * weight;
                }
            }
            avgNormal = avgNormal.normalized;

            // 視線方向と逆向きなら反転。視点が無いときは補正しない。
            if (viewDir.HasValue && Vector3.Dot(avgNormal, -viewDir.Value) < 0)
            {
                avgNormal = -avgNormal;
            }

            float direction = Invert ? -1f : 1f;

            foreach (var (idx, weight) in vertices)
            {
                if (!work.ContainsKey(idx)) continue;
                work[idx] += avgNormal * Strength * weight * direction;
            }
        }

        /// <summary>
        /// Smooth: 滑らかにする
        /// </summary>
        private void ApplySmooth(Dictionary<int, Vector3> work, List<(int index, float weight)> vertices, Dictionary<int, HashSet<int>> adjacencyCache)
        {
            if (adjacencyCache == null) return;

            // 各頂点を隣接頂点の平均位置に近づける
            var newPositions = new Dictionary<int, Vector3>();

            foreach (var (idx, weight) in vertices)
            {
                if (!work.ContainsKey(idx)) continue;
                if (adjacencyCache.TryGetValue(idx, out var neighbors) && neighbors.Count > 0)
                {
                    Vector3 avgPos = Vector3.zero;
                    int n = 0;
                    foreach (int neighbor in neighbors)
                    {
                        if (!work.TryGetValue(neighbor, out var np)) continue;
                        avgPos += np; n++;
                    }
                    if (n == 0) continue;
                    avgPos /= n;

                    newPositions[idx] = Vector3.Lerp(work[idx], avgPos, Strength * weight);
                }
            }

            foreach (var kvp in newPositions) work[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Inflate: 膨らます
        /// </summary>
        private void ApplyInflate(Dictionary<int, Vector3> work, List<(int index, float weight)> vertices, Dictionary<int, Vector3> vertexNormalsCache)
        {
            if (vertexNormalsCache == null) return;

            float direction = Invert ? -1f : 1f;

            foreach (var (idx, weight) in vertices)
            {
                if (!work.ContainsKey(idx)) continue;
                if (vertexNormalsCache.TryGetValue(idx, out var normal))
                    work[idx] += normal * Strength * weight * direction;
            }
        }

        /// <summary>
        /// Flatten: 平らにする
        /// </summary>
        private void ApplyFlatten(Dictionary<int, Vector3> work, List<(int index, float weight)> vertices, Dictionary<int, Vector3> vertexNormalsCache)
        {
            if (vertices.Count == 0 || vertexNormalsCache == null) return;

            // ブラシ範囲内の頂点の平均位置と平均法線を計算
            Vector3 avgPos = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;
            float totalWeight = 0;

            foreach (var (idx, weight) in vertices)
            {
                if (!work.TryGetValue(idx, out var wp)) continue;
                avgPos += wp * weight;
                totalWeight += weight;

                if (vertexNormalsCache.TryGetValue(idx, out var normal))
                {
                    avgNormal += normal * weight;
                }
            }

            if (totalWeight > 0)
            {
                avgPos /= totalWeight;
            }
            avgNormal = avgNormal.normalized;

            // 各頂点を平面に投影
            foreach (var (idx, weight) in vertices)
            {
                if (!work.TryGetValue(idx, out var pos)) continue;

                // 平面への距離
                float distToPlane = Vector3.Dot(pos - avgPos, avgNormal);

                // 平面上の位置
                Vector3 projectedPos = pos - avgNormal * distToPlane;

                // 補間
                work[idx] = Vector3.Lerp(pos, projectedPos, Strength * weight);
            }
        }

        // ================================================================
        // キャッシュ構築
        // ================================================================

        /// <summary>
        /// 隣接と頂点法線のキャッシュを作る。
        ///
        /// 【法線はワールドで作る】（2026-09-15 変更）
        ///   変形はワールド空間で行うので、法線もワールドで要る。
        ///   面法線はその場の 3 頂点から作る派生値なので、渡す座標さえ GPU 由来なら
        ///   ここで作り直しても二重計算にならない。GPU の _WorldNormalBuffer は
        ///   _TransformNormals=0 で運用されており書かれていない
        ///   （UnifiedSystemAdapter.cs:454）。
        ///   worldPositions が null のときだけローカル座標で作る（保険）。
        /// </summary>
        private void BuildCachesForMesh(int meshIdx, MeshObject meshObject, Vector3[] worldPositions = null)
        {
            // 隣接頂点キャッシュ
            var adjacencyCache = new Dictionary<int, HashSet<int>>();
            foreach (var face in meshObject.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!adjacencyCache.ContainsKey(v1)) adjacencyCache[v1] = new HashSet<int>();
                    if (!adjacencyCache.ContainsKey(v2)) adjacencyCache[v2] = new HashSet<int>();

                    adjacencyCache[v1].Add(v2);
                    adjacencyCache[v2].Add(v1);
                }
            }
            _adjacencyCachePerMesh[meshIdx] = adjacencyCache;

            // 頂点法線キャッシュ
            var vertexNormalsCache = new Dictionary<int, Vector3>();
            var vertexFaceNormals = new Dictionary<int, List<Vector3>>();

            bool useWorld = worldPositions != null && worldPositions.Length >= meshObject.VertexCount;
            Vector3 P(int idx) => useWorld ? worldPositions[idx] : meshObject.Vertices[idx].Position;

            foreach (var face in meshObject.Faces)
            {
                if (face.VertexIndices.Count < 3) continue;

                // 面の法線を計算
                Vector3 v0 = P(face.VertexIndices[0]);
                Vector3 v1 = P(face.VertexIndices[1]);
                Vector3 v2 = P(face.VertexIndices[2]);
                Vector3 faceNormal = NormalHelper.CalculateFaceNormal(v0, v1, v2);

                foreach (int vIdx in face.VertexIndices)
                {
                    if (!vertexFaceNormals.ContainsKey(vIdx))
                        vertexFaceNormals[vIdx] = new List<Vector3>();
                    vertexFaceNormals[vIdx].Add(faceNormal);
                }
            }

            foreach (var kvp in vertexFaceNormals)
            {
                Vector3 avgNormal = Vector3.zero;
                foreach (var n in kvp.Value)
                {
                    avgNormal += n;
                }
                vertexNormalsCache[kvp.Key] = avgNormal.normalized;
            }
            _vertexNormalsCachePerMesh[meshIdx] = vertexNormalsCache;
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        private float EstimateBrushScreenRadius(ToolContext ctx)
        {
            // ブラシ中心付近でのスクリーン半径を概算
            // カメラのright方向を使用（ワールドX固定だとカメラ回転でサイズが変わる）
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f)
                camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * BrushRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private void DrawCircle(Vector2 center, float radius, int segments)
        {
            Vector2 prevPoint = center + new Vector2(radius, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                // UnityEditor_Handles 削除済み
                prevPoint = point;
            }
        }
    }
}
