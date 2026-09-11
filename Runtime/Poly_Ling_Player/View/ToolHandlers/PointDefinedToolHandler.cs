// PointDefinedToolHandler.cs
// 点指定図形：3D ビューポートでの点の指定（クリック・既存頂点への吸着）、
// プレビュー用メッシュの組み立て、コマンドの組み立てと実行。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【書き込み先】
//   編集対象（ActiveMeshContext）。面追加と同じ決め方。
//   編集対象の頂点に吸着した点は、その頂点番号をそのまま使う。
//   他のオブジェクトの頂点に吸着した点は、位置だけを合わせた新しい頂点にする。
//
// 【プレビューと実生成】
//   どちらも TryBuildPlan → PointDefinedMeshBuilder の同じ計画を通す。
//   プレビューは計画の枠を全部新しい頂点（ワールド座標）にしたメッシュ、
//   実生成は計画を編集対象へ書き込む。
//
// 【GPU 由来の座標】
//   既存頂点のワールド座標は GetMeshVertexWorldPosition（GPU 値）を使う。
//   CPU でスキニングを計算し直さない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PointDefinedToolHandler : IPlayerToolHandler
    {
        /// <summary>吸着しない点をカメラから置く距離（カメラ距離に対する倍率）。面追加と同じ値。</summary>
        private const float DefaultDistance = 1.5f;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ProjectContext> GetProject;

        /// <summary>カレントカメラのツールコンテキスト。</summary>
        public Func<ToolContext> GetToolContext;

        /// <summary>GPU ホバー要素（選択メッシュ）。</summary>
        public Func<Poly_Ling.Selection.MeshSelectMode, PlayerHoverElement> GetHoverElement;

        /// <summary>非選択オブジェクトも対象にした吸着用ホバー要素。</summary>
        public Func<PlayerHoverElement> GetSnapHoverElement;

        /// <summary>(MeshContextList インデックス, 頂点番号) → GPU が計算したワールド座標。</summary>
        public Func<int, int, Vector3?> GetMeshVertexWorldPosition;

        /// <summary>吸着用ヒットテストの有効/無効を Viewer へ伝える。</summary>
        public Action<bool> OnSnapHitTestEnabledChanged;

        /// <summary>編集対象が無ければ空の描画オブジェクトを作る（面追加と同じもの）。</summary>
        public Func<bool> EnsureDrawableMesh;

        public Func<MeshUndoController> GetUndoController;

        /// <summary>点の追加・削除・クリアのたびに呼ぶ。</summary>
        public Action OnPointsChanged;

        // ================================================================
        // 状態
        // ================================================================

        private readonly List<PointPick> _picks = new List<PointPick>();
        private PointPick? _hoverPick;

        private PointPrimitiveMode _mode   = PointPrimitiveMode.Quad;
        private PointDefinedParams _params = PointDefinedParams.Default;

        /// <summary>プレビュー・オーバーレイ用の経路キャッシュ。点を指定し直すと捨てる。</summary>
        private readonly ExistingEdgePathMatcher _matcher = new ExistingEdgePathMatcher();

        private PointDefinedStatus _status = new PointDefinedStatus();
        private Vector3 _lastPreviewDir;
        private bool    _hasPreviewDir;
        private bool    _snapToUnselected;

        // ================================================================
        // 公開
        // ================================================================

        public PointPrimitiveMode Mode => _mode;
        public int RequiredPoints => PointDefinedMeshBuilder.RequiredPoints(_mode);
        public int PlacedCount => _picks.Count;
        public IReadOnlyList<PointPick> Picks => _picks;
        public PointPick? HoverPick => _hoverPick;

        /// <summary>直近のプレビュー生成の結果。</summary>
        public PointDefinedStatus Status => _status;

        /// <summary>
        /// 非選択オブジェクトの頂点にも吸着するか。既定 false。
        /// true の間だけ GPU 側で追加のヒットテストが走る。
        /// </summary>
        public bool SnapToUnselectedObjects
        {
            get => _snapToUnselected;
            set
            {
                if (_snapToUnselected == value) return;
                _snapToUnselected = value;
                OnSnapHitTestEnabledChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// パネルの図形種類とパラメータを受け取る。種類が変わったら点をクリアする。
        /// </summary>
        public void SetRequest(PointPrimitiveMode mode, PointDefinedParams p)
        {
            bool modeChanged = mode != _mode;
            _mode   = mode;
            _params = p;
            if (modeChanged && _picks.Count > 0)
            {
                _picks.Clear();
                _matcher.Invalidate();
                OnPointsChanged?.Invoke();
            }
        }

        public void ClearPoints()
        {
            if (_picks.Count == 0 && !_hoverPick.HasValue) return;
            _picks.Clear();
            _hoverPick = null;
            _matcher.Invalidate();
            OnPointsChanged?.Invoke();
        }

        public bool RemoveLastPoint()
        {
            if (_picks.Count == 0) return false;
            _picks.RemoveAt(_picks.Count - 1);
            _matcher.Invalidate();
            OnPointsChanged?.Invoke();
            return true;
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        // クリックとドラッグ開始のどちらも「押した瞬間に点を置く」（面追加と同じ）。
        // PlayerVertexInteractor はしきい値でどちらかへ振り分けるため、両方から同じ処理を通す。
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) => PlaceAt(screenPos);
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) => PlaceAt(screenPos);
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }

        /// <summary>
        /// ホバー更新。次に置かれる点の候補を控える（オーバーレイ表示用）。
        /// screenPos は GPU Y（Y=0 下）。
        /// </summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _picks.Count >= RequiredPoints) { _hoverPick = null; return; }
            _hoverPick = ResolvePickAt(screenPos, ctx);
        }

        private void PlaceAt(Vector2 screenPos)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            if (_picks.Count >= RequiredPoints) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            _picks.Add(ResolvePickAt(screenPos, ctx));
            _hoverPick = null;
            _matcher.Invalidate();
            OnPointsChanged?.Invoke();
        }

        /// <summary>
        /// 画面位置から点を決める。優先順は面追加と同じ。
        ///   1. 選択メッシュの頂点（GPU ホバー）
        ///   2. 非選択オブジェクトの頂点（チェックが ON のときだけ）
        ///   3. カメラ平行の作業面との交点（原点は直前の点、無ければワールド原点）
        /// </summary>
        private PointPick ResolvePickAt(Vector2 screenPos, ToolContext ctx)
        {
            if (GetHoverElement != null
                && TryPickFromHover(GetHoverElement(Poly_Ling.Selection.MeshSelectMode.Vertex), out var p))
                return p;

            if (_snapToUnselected && GetSnapHoverElement != null
                && TryPickFromHover(GetSnapHoverElement(), out p))
                return p;

            var wp = new WorkPlaneContext();
            wp.UpdateFromCamera(ctx.CameraPosition, ctx.CameraTarget);
            wp.Origin = _picks.Count > 0 ? _picks[_picks.Count - 1].WorldPosition : Vector3.zero;

            // UpdateHover / クリックの screenPos は GPU Y。ScreenPosToRay は IMGUI Y（Y=0 上）を取る。
            var imgui = new Vector2(screenPos.x, ctx.PreviewRect.height - screenPos.y);
            Ray ray = ctx.ScreenPosToRay != null
                ? ctx.ScreenPosToRay(imgui)
                : new Ray(ctx.CameraPosition, (ctx.CameraTarget - ctx.CameraPosition).normalized);

            Vector3 world = wp.RayIntersect(ray.origin, ray.direction, out Vector3 hit)
                ? hit
                : ray.origin + ray.direction * (DefaultDistance * ctx.CameraDistance);

            return new PointPick { WorldPosition = world, MeshIndex = -1, VertexIndex = -1 };
        }

        private bool TryPickFromHover(PlayerHoverElement e, out PointPick pick)
        {
            pick = default;
            if (e.Kind != PlayerHoverKind.Vertex || e.MeshIndex < 0 || e.VertexIndex < 0) return false;

            var w = GetMeshVertexWorldPosition?.Invoke(e.MeshIndex, e.VertexIndex);
            if (!w.HasValue) return false;

            pick = new PointPick { WorldPosition = w.Value, MeshIndex = e.MeshIndex, VertexIndex = e.VertexIndex };
            return true;
        }

        // ================================================================
        // カメラ
        // ================================================================

        /// <summary>カレントカメラの視線方向（正規化）。取れなければ zero。</summary>
        public Vector3 CurrentViewDirection()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return Vector3.zero;
            Vector3 d = ctx.CameraTarget - ctx.CameraPosition;
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.zero;
        }

        /// <summary>
        /// 直近のプレビューを作ったときからカメラの向きが変わったか。
        /// 点が揃っていないときはプレビューが無いので false。
        /// </summary>
        public bool IsPreviewViewDirectionStale()
        {
            if (_picks.Count < RequiredPoints) return false;
            Vector3 d = CurrentViewDirection();
            if (d == Vector3.zero) return false;
            if (!_hasPreviewDir) return true;
            return Vector3.Dot(d, _lastPreviewDir) < 0.9999999f;
        }

        // ================================================================
        // プレビュー
        // ================================================================

        /// <summary>
        /// 現在の点・種類・パラメータ・カメラでプレビュー用メッシュ（ワールド座標）を組む。
        /// 結果は Status に残す。点が揃っていない、または組めないときは null。
        /// </summary>
        public MeshObject BuildPreviewMesh()
        {
            var status = new PointDefinedStatus { Placed = _picks.Count, Required = RequiredPoints };
            _status = status;
            if (_picks.Count < RequiredPoints) return null;

            Vector3 viewDir = CurrentViewDirection();
            if (viewDir == Vector3.zero) { status.Reason = "カメラがありません"; return null; }
            _lastPreviewDir = viewDir;
            _hasPreviewDir  = true;

            var model = GetProject?.Invoke()?.CurrentModel;
            ResolveTarget(model, out var target, out int targetIndex);

            if (!TryBuildPlan(model, target, targetIndex, _mode, _params, _picks, viewDir, _matcher,
                    status.Edges, null, out var plan, out string reason))
            {
                status.Reason = reason;
                return null;
            }

            status.Valid = true;
            var keys = PointDefinedMeshBuilder.CollectFaceKeysIfNeeded(plan, target?.MeshObject);
            return PointDefinedMeshBuilder.ToPreviewMesh(plan, "PointDefined", keys);
        }

        /// <summary>
        /// 共有が成立している既存経路（オーバーレイの強調表示用）。
        /// 戻り値は経路の属するメッシュの MeshContextList インデックス。無ければ -1。
        /// </summary>
        public int CollectSharedPaths(List<List<int>> outPaths)
        {
            outPaths.Clear();
            if (_mode == PointPrimitiveMode.Line || _picks.Count < RequiredPoints) return -1;

            var model = GetProject?.Invoke()?.CurrentModel;
            ResolveTarget(model, out var target, out int targetIndex);
            if (target?.MeshObject == null) return -1;

            var corners = CornersOf(target, targetIndex, _picks);
            foreach (var e in EdgesOf(_mode, _params))
            {
                var m = MatchEdge(target, targetIndex, corners, e, _matcher);
                if (m != null && m.CanShare) outPaths.Add(m.VertexPath);
            }
            return targetIndex;
        }

        /// <summary>編集対象の頂点のワールド座標（GPU 値）。オーバーレイ用。</summary>
        public Vector3? TargetVertexWorld(int meshIndex, int vertex)
            => GetMeshVertexWorldPosition?.Invoke(meshIndex, vertex);

        // ================================================================
        // コマンド
        // ================================================================

        /// <summary>
        /// 現在の点・種類・パラメータ・カレントカメラからコマンドを組む。
        /// 組めないときは null と理由。
        /// </summary>
        public CreatePointDefinedPrimitiveCommand BuildCommand(int materialIndex, out string reason)
        {
            reason = null;

            if (EnsureDrawableMesh != null && !EnsureDrawableMesh())
            { reason = "編集対象を用意できませんでした"; return null; }

            var project = GetProject?.Invoke();
            var model   = project?.CurrentModel;
            ResolveTarget(model, out var target, out int targetIndex);
            if (target?.MeshObject == null) { reason = "編集対象メッシュがありません"; return null; }

            int required = RequiredPoints;
            if (_picks.Count < required) { reason = $"点があと {required - _picks.Count} 個要ります"; return null; }

            Vector3 viewDir = CurrentViewDirection();
            if (viewDir == Vector3.zero) { reason = "カメラがありません"; return null; }

            int vc = target.MeshObject.VertexCount;
            var idx = new int[required];
            var pos = new float[required * 3];
            for (int i = 0; i < required; i++)
            {
                var pk = _picks[i];
                bool reuse = pk.MeshIndex == targetIndex && pk.VertexIndex >= 0 && pk.VertexIndex < vc;
                idx[i] = reuse ? pk.VertexIndex : -1;
                pos[i * 3]     = pk.WorldPosition.x;
                pos[i * 3 + 1] = pk.WorldPosition.y;
                pos[i * 3 + 2] = pk.WorldPosition.z;
            }

            return new CreatePointDefinedPrimitiveCommand(
                project.CurrentModelIndex,
                new[] { targetIndex },
                _mode, idx, pos, viewDir, _params,
                Mathf.Max(0, materialIndex));
        }

        /// <summary>
        /// コマンドを実行する。計画はプレビューと同じ TryBuildPlan で組み、編集対象へ書き込む。
        /// Undo は辺群ブリッジと同じく スナップショット → 追加 → ReplaceUnityMesh → RecordTopologyChange。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(CreatePointDefinedPrimitiveCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = GetProject?.Invoke()?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var mc = model.ActiveMeshContext;
            var mo = mc.MeshObject;
            int targetIndex = model.IndexOf(mc);

            int required = PointDefinedMeshBuilder.RequiredPoints(cmd.Mode);
            var idx = cmd.PointVertexIndices;
            var pos = cmd.PointPositions;
            if (idx == null || idx.Length != required)
            { reason = $"{cmd.Mode} には点が {required} 個要ります"; return false; }
            if (pos == null || pos.Length != required * 3)
            { reason = $"PointPositions の長さは点数の 3 倍にしてください（{required * 3} 個）"; return false; }

            var picks = new List<PointPick>(required);
            for (int i = 0; i < required; i++)
            {
                if (idx[i] >= mo.VertexCount)
                { reason = $"頂点番号 {idx[i]} が範囲外です"; return false; }
                picks.Add(new PointPick
                {
                    WorldPosition = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]),
                    MeshIndex     = idx[i] >= 0 ? targetIndex : -1,
                    VertexIndex   = idx[i] >= 0 ? idx[i] : -1,
                });
            }

            Vector3 viewDir = cmd.ViewDirection;
            if (viewDir.sqrMagnitude < 1e-12f) { reason = "ViewDirection が 0 です"; return false; }
            viewDir.Normalize();

            // コマンドの実行はプレビューのキャッシュに頼らない。
            var matcher = new ExistingEdgePathMatcher();
            if (!TryBuildPlan(model, mc, targetIndex, cmd.Mode, cmd.Params, picks, viewDir, matcher,
                    null, null, out var plan, out reason))
                return false;

            var keys = PointDefinedMeshBuilder.CollectFaceKeysIfNeeded(plan, mo);

            var undo = GetUndoController?.Invoke();
            MeshObjectSnapshot before = null;
            if (undo != null)
            {
                undo.SetMeshObject(mo, mc.UnityMesh);
                undo.MeshUndoContext.ParentModelContext = model;
                before = undo.CaptureMeshObjectSnapshot();
            }

            int originalCount = mo.VertexCount;
            var map = AllocateTargetVertices(model, mc, targetIndex, plan);
            Poly_Ling.Ops.PartsIdOps.AssignNewVertices(mo, originalCount);

            int added = PointDefinedMeshBuilder.AppendFaces(mo, plan, map, Mathf.Max(0, cmd.MaterialIndex), keys);
            if (added == 0)
            {
                // 足す面が無いとき、新しい枠は計画に存在しない（新しい枠を含む面は既存と重複しない）。
                reason = "同じ面が既にあるため、足す面がありません";
                return false;
            }

            mo.RecomputeSkinKind();

            var newUnityMesh = mo.ToUnityMesh();
            newUnityMesh.name      = mc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            mc.ReplaceUnityMesh(newUnityMesh);

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                undo.RecordTopologyChange(before, after, $"Point Defined {cmd.Mode} in {mc.Name}");
            }

            ClearPoints();
            return true;
        }

        // ================================================================
        // 計画（プレビューと実生成で共用）
        // ================================================================

        private struct EdgeDef
        {
            public int    A, B;
            public int    Segments;
            public string KindKey;
        }

        /// <summary>
        /// 共有判定の対象辺。向きは PointDefinedMeshBuilder の引数の向き。
        ///   四角：P0→P1 / P3→P2（横）、P0→P3 / P1→P2（縦）
        ///   三角：底辺 B→C、斜辺 A→B / A→C（A が頂点）
        /// </summary>
        private static List<EdgeDef> EdgesOf(PointPrimitiveMode mode, PointDefinedParams p)
        {
            var list = new List<EdgeDef>();
            switch (mode)
            {
                case PointPrimitiveMode.Quad:
                {
                    int n = Mathf.Clamp(p.USegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
                    int m = Mathf.Clamp(p.VSegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
                    list.Add(new EdgeDef { A = 0, B = 1, Segments = n, KindKey = "PointDefinedEdgeH" });
                    list.Add(new EdgeDef { A = 3, B = 2, Segments = n, KindKey = "PointDefinedEdgeH" });
                    list.Add(new EdgeDef { A = 0, B = 3, Segments = m, KindKey = "PointDefinedEdgeV" });
                    list.Add(new EdgeDef { A = 1, B = 2, Segments = m, KindKey = "PointDefinedEdgeV" });
                    break;
                }
                case PointPrimitiveMode.Triangle:
                {
                    int a = Mathf.Clamp(p.ApexIndex, PointDefinedParams.ApexIndexMin, PointDefinedParams.ApexIndexMax);
                    int b = (a + 1) % 3, c = (a + 2) % 3;
                    int n = Mathf.Clamp(p.BaseSegments,   PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
                    int m = Mathf.Clamp(p.HeightSegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
                    list.Add(new EdgeDef { A = b, B = c, Segments = n, KindKey = "PointDefinedEdgeBase" });
                    list.Add(new EdgeDef { A = a, B = b, Segments = m, KindKey = "PointDefinedEdgeSide" });
                    list.Add(new EdgeDef { A = a, B = c, Segments = m, KindKey = "PointDefinedEdgeSide" });
                    break;
                }
            }
            return list;
        }

        private static void ResolveTarget(ModelContext model, out MeshContext target, out int targetIndex)
        {
            target = model?.ActiveMeshContext;
            targetIndex = (model != null && target != null) ? model.IndexOf(target) : -1;
            if (target?.MeshObject == null) { target = null; targetIndex = -1; }
        }

        /// <summary>編集対象の頂点のワールド座標。GPU 値が無いときは WorldMatrix を掛ける（面追加と同じ）。</summary>
        private Vector3 WorldOfTargetVertex(MeshContext target, int targetIndex, int vertex)
        {
            var w = GetMeshVertexWorldPosition?.Invoke(targetIndex, vertex);
            if (w.HasValue) return w.Value;
            return target.WorldMatrix.MultiplyPoint3x4(target.MeshObject.Vertices[vertex].Position);
        }

        /// <summary>
        /// 指定点 → 組み立て用の角。編集対象の頂点を指す点だけを既存頂点として扱い、
        /// 座標はその時点の GPU 値を使う。それ以外は指定時のワールド座標の新規点。
        /// </summary>
        private PointDefinedCorner[] CornersOf(MeshContext target, int targetIndex, IReadOnlyList<PointPick> picks)
        {
            var mo = target?.MeshObject;
            var corners = new PointDefinedCorner[picks.Count];
            for (int i = 0; i < picks.Count; i++)
            {
                var pk = picks[i];
                bool existing = mo != null && pk.MeshIndex == targetIndex
                             && pk.VertexIndex >= 0 && pk.VertexIndex < mo.VertexCount;
                corners[i] = existing
                    ? PointDefinedCorner.Existing(pk.VertexIndex, WorldOfTargetVertex(target, targetIndex, pk.VertexIndex))
                    : PointDefinedCorner.New(pk.WorldPosition);
            }
            return corners;
        }

        /// <summary>辺の両端が既存頂点なら経路を判定する。どちらかが新規点なら null。</summary>
        private EdgeShareMatch MatchEdge(
            MeshContext target, int targetIndex, PointDefinedCorner[] corners, EdgeDef e,
            ExistingEdgePathMatcher matcher)
        {
            if (target?.MeshObject == null) return null;
            var ca = corners[e.A];
            var cb = corners[e.B];
            if (ca.ExistingVertex < 0 || cb.ExistingVertex < 0) return null;

            matcher.Prepare(target.MeshObject, targetIndex,
                vi => WorldOfTargetVertex(target, targetIndex, vi),
                PointDefinedParams.SegmentsMax);
            return matcher.TryMatch(ca.ExistingVertex, cb.ExistingVertex, e.Segments);
        }

        private bool TryBuildPlan(
            ModelContext model, MeshContext target, int targetIndex,
            PointPrimitiveMode mode, PointDefinedParams p, IReadOnlyList<PointPick> picks,
            Vector3 viewDir, ExistingEdgePathMatcher matcher,
            List<EdgeShareInfo> edgeInfos, List<List<int>> sharedPaths,
            out PointDefinedMeshPlan plan, out string reason)
        {
            plan = null;
            reason = null;

            int required = PointDefinedMeshBuilder.RequiredPoints(mode);
            if (picks == null || picks.Count < required) { reason = $"点が {required} 個要ります"; return false; }

            var corners = CornersOf(target, targetIndex, picks);

            if (mode == PointPrimitiveMode.Line)
                return PointDefinedMeshBuilder.BuildLine(corners[0].World, corners[1].World, viewDir, p, out plan, out reason);

            var edges  = EdgesOf(mode, p);
            var shared = new List<PointDefinedCorner>[edges.Count];
            for (int k = 0; k < edges.Count; k++)
            {
                var e = edges[k];
                var m = MatchEdge(target, targetIndex, corners, e, matcher);

                edgeInfos?.Add(new EdgeShareInfo
                {
                    KindKey      = e.KindKey,
                    PointA       = e.A,
                    PointB       = e.B,
                    Subdivisions = e.Segments,
                    PathEdges    = m?.EdgeCount ?? -1,
                    CanShare     = m != null && m.CanShare,
                });

                if (m == null || !m.CanShare) continue;

                var list = new List<PointDefinedCorner>(m.VertexPath.Count);
                foreach (int vi in m.VertexPath)
                    list.Add(PointDefinedCorner.Existing(vi, WorldOfTargetVertex(target, targetIndex, vi)));
                shared[k] = list;
                sharedPaths?.Add(m.VertexPath);
            }

            if (mode == PointPrimitiveMode.Quad)
            {
                // EdgesOf の並び：0 = P0→P1, 1 = P3→P2, 2 = P0→P3, 3 = P1→P2
                return PointDefinedMeshBuilder.BuildQuad(
                    corners, shared[0], shared[1], shared[2], shared[3],
                    viewDir, p, out plan, out reason);
            }

            // 三角。EdgesOf の並び：0 = 底辺 B→C, 1 = 斜辺 A→B, 2 = 斜辺 A→C
            int a = Mathf.Clamp(p.ApexIndex, PointDefinedParams.ApexIndexMin, PointDefinedParams.ApexIndexMax);
            return PointDefinedMeshBuilder.BuildTriangle(
                corners[a], corners[(a + 1) % 3], corners[(a + 2) % 3],
                shared[1], shared[2], shared[0],
                viewDir, p, out plan, out reason);
        }

        // ================================================================
        // 書き込み
        // ================================================================

        /// <summary>
        /// 計画の枠 → 編集対象の頂点番号。既存の枠はその番号、新規の枠は頂点を足す。
        ///
        /// 【ウェイトと座標の基準】面追加（AddFaceTool.CreateFace）と同じ規則。
        ///   NewVertexSourceRule の (A)（辺をたどった段数が最小の既存頂点）、
        ///   既存頂点を 1 つも使わないときは (B)（ワールドで最も近い既存頂点）から継承し、
        ///   座標の基準を継承元へ揃える。
        ///   編集対象にウェイトを持つ頂点が 1 つも無いときは継承するものが無いので探さない。
        /// </summary>
        private int[] AllocateTargetVertices(
            ModelContext model, MeshContext mc, int targetIndex, PointDefinedMeshPlan plan)
        {
            var mo = mc.MeshObject;
            int n = plan.SlotCount;
            var map = new int[n];
            int originalCount = mo.VertexCount;

            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model = model;
            ctx.GetVertexWorldPosition = vi => GetMeshVertexWorldPosition?.Invoke(targetIndex, vi);

            bool weighted = false;
            for (int i = 0; i < originalCount; i++)
                if (mo.Vertices[i] != null && mo.Vertices[i].HasBoneWeight) { weighted = true; break; }

            int[] bySteps = weighted
                ? NewVertexSourceRule.FindSourcesByEdgeSteps(n, plan.Faces, plan.SlotExisting)
                : null;

            for (int i = 0; i < n; i++)
            {
                if (plan.SlotExisting[i] >= 0) { map[i] = plan.SlotExisting[i]; continue; }

                Vector3 local = ctx.ActiveWorldToLocal(plan.SlotWorld[i]);
                int src = -1;
                if (weighted)
                {
                    src = bySteps[i];
                    if (src < 0)
                        src = NewVertexSourceRule.FindSourceByWorldDistance(ctx, mo, originalCount, local);
                    local = NewVertexSourceRule.RebasePositionToSource(ctx, mo, src, local);
                }

                var v = new Vertex(local);
                if (src >= 0 && src < originalCount)
                    v.BoneWeight = mo.Vertices[src].BoneWeight;

                map[i] = mo.AddVertex(v);
            }
            return map;
        }
    }
}
