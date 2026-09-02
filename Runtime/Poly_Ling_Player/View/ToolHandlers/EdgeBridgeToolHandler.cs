// EdgeBridgeToolHandler.cs
// 辺群ブリッジ：2 か所の辺群を拾い、その間に面を張る。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【穴つなぎ（PlayerPrimitiveMeshSubPanel.Bridge）との違い】
//   あちらは「穴＝境界辺の連結成分」を種頂点から復元する。閉環しか扱えない。
//   こちらは拾った辺そのものを辺群とするので、開いた辺の連なりも扱える。
//   面の張り方は BridgeLoopOps.Build を共通で使う（開環対応を足してある）。
//
// 【拾い方】ナイフと同じく、選択状態とは切り離した明示ピック。
//   ・GPU ホバーを Edge に固定し、クリックで 1 辺ずつトグル。
//   ・ドラッグで矩形選択。両端頂点が可視かつ矩形内の辺を拾う
//     （MoveToolHandler.CommitBoxSelect の辺走査と同じ規則）。
//   ・拾った辺は MeshContext.Selection に一切書かない。通常の選択と混ざると
//     「どれを拾ったのか」が分からなくなるため。
//
// 【A/B を別々に拾わせない理由】
//   連結関係で 2 領域を判別できる（EdgeChainOps.SplitIntoTwoChains）。
//   3 群以上・分岐ありは面の張り方が一意に決まらないので拒否する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class EdgeBridgeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private ProjectContext     _project;
        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        /// <summary>GPU ホバー要素取得（Viewer から PlayerViewportManager.GetHoverElement を結線）。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;

        /// <summary>
        /// ホバー用選択モードをツール固有 override として Viewer へ通知する。
        /// 適用先は Viewer 側の選択モード権限が一括で面倒を見る
        /// （現モデル全メッシュ / SelectionOps / レンダラ保持分）。
        /// </summary>
        public Action<MeshSelectMode> ApplyHoverModeToAllMeshes;

        /// <summary>拾った辺が変わったときに呼ぶ。サブパネルとオーバーレイの更新用。</summary>
        public Action OnPicksChanged;

        // ---- 矩形選択に必要な結線（MoveToolHandler と同じ相手）----
        public Func<Vector2[]> GetScreenPositions;
        public Func<int, int>  GetVertexOffset;
        public Func<int, bool> IsVertexVisible;
        public Func<float>     GetViewportHeight;

        public Action<Vector2, Vector2> OnBoxSelectUpdate;
        public Action                   OnBoxSelectEnd;

        /// <summary>
        /// 矩形確定の直前に、背面カリングフラグを GPU→CPU へ読み戻す
        /// （Viewer から PlayerViewportManager.ReadBackVertexFlags を結線）。
        /// これを通さないと IsVertexVisible が前フレームの値を返し、
        /// 裏側の辺まで拾ってしまう。
        ///
        /// なお MoveToolHandler が併用している EnterBoxSelecting / ExitBoxSelecting は
        /// [Obsolete] で新規呼出しを禁じられているため、ここでは使わない。
        /// 効果は「ドラッグ中に描画モードを固定してホバー計算を止める」ことなので、
        /// 拾う結果そのものには影響しない。
        /// </summary>
        public Action                   OnReadBackVertexFlags;

        // ================================================================
        // 設定（サブパネルから操作）
        // ================================================================

        private bool _boundaryEdgeOnly = true;

        /// <summary>
        /// 境界辺（1 面だけが使う辺）だけを拾う。既定 ON。
        /// OFF にすると内部辺（2 面が共有する辺）も拾える。
        /// </summary>
        public bool BoundaryEdgeOnly
        {
            get => _boundaryEdgeOnly;
            set
            {
                if (_boundaryEdgeOnly == value) return;
                _boundaryEdgeOnly = value;
                _summaryDirty     = true;
                // 切替で対象外になった辺を落とす（拾えないものが残らないようにする）。
                PrunePicks();
                OnPicksChanged?.Invoke();
            }
        }

        /// <summary>辺群Bの並びを反転する。自動判定の上書き用。</summary>
        public bool FlipCorrespondence { get; set; }

        /// <summary>生成面の巻き方向を反転する。</summary>
        public bool FlipFaces { get; set; }

        private int _subdivisions = 0;

        /// <summary>A→B 方向の分割数（0 で分割なし）。</summary>
        public int Subdivisions
        {
            get => _subdivisions;
            // 範囲は CreateEdgeBridgeCommand が持つ値域を正典にする。
            set => _subdivisions = Mathf.Clamp(
                value, CreateEdgeBridgeCommand.SubdivisionsMin, CreateEdgeBridgeCommand.SubdivisionsMax);
        }

        /// <summary>
        /// 対応の始点と向きを自動で決めるか。既定 ON。
        /// OFF のときは拾った順（頂点番号順）のまま FlipCorrespondence だけで調整する。
        /// </summary>
        public bool AutoCorrespondence { get; set; } = true;

        // ================================================================
        // 拾った辺
        // ================================================================

        /// <summary>拾った辺が属する描画オブジェクト（MeshContextList インデックス）。未確定は -1。</summary>
        public int PickedMeshIndex { get; private set; } = -1;

        private readonly HashSet<VertexPair> _picked = new HashSet<VertexPair>();

        /// <summary>拾った辺（読み取り専用）。オーバーレイ描画に使う。</summary>
        public IReadOnlyCollection<VertexPair> PickedEdges => _picked;

        public int PickedEdgeCount => _picked.Count;

        /// <summary>
        /// 境界辺と全辺の集合。メッシュ実体ごとにキャッシュする（毎回の全面走査を避ける）。
        /// メッシュ実体・面数・頂点数のいずれかが変わったら作り直す。
        /// </summary>
        private MeshObject          _edgeCacheMesh;
        private int                 _edgeCacheFaceCount   = -1;
        private int                 _edgeCacheVertexCount = -1;
        private HashSet<VertexPair> _boundaryCache;
        private HashSet<VertexPair> _allEdgeCache;

        /// <summary>拾った辺を全て捨てる。</summary>
        public void ClearPicks()
        {
            _lastRejectReason = null;
            if (_picked.Count == 0 && PickedMeshIndex < 0) return;
            _picked.Clear();
            PickedMeshIndex = -1;
            _summaryDirty   = true;
            OnPicksChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 辺を直接指定して拾い直す。既存の拾いは捨てる。
        ///
        /// 【なぜ要るか】
        ///   OnLeftClick / CommitBoxPick は GPU ホバーと画面座標に依存するので、
        ///   コマンド経由（自動検証・MCP）からは通せない。同じ受理判定を通したまま
        ///   辺だけを渡せる入口をここに置く。
        ///
        /// 【受理判定を迂回しない】
        ///   1 辺ずつ AcceptEdge へ通す。境界辺のみ・同一オブジェクトのみという
        ///   規則はクリック経路と同じものが効く。1 本でも弾かれたら何も拾わずに
        ///   false を返し、理由を reason へ入れる。
        ///
        ///   同じ辺が 2 回来たときはトグルせず 1 本として扱う。クリックはトグルだが、
        ///   こちらは「この集合にする」という指定なので、重複で消えると意図とずれる。
        /// </summary>
        /// <param name="meshIndex">辺が属する描画オブジェクトの MeshContextList インデックス。</param>
        /// <param name="edges">拾わせる辺。両端の頂点番号の組。</param>
        /// <param name="reason">弾かれた理由。成功時は null。</param>
        public bool SetPicks(int meshIndex, IEnumerable<VertexPair> edges, out string reason)
        {
            reason = null;

            if (edges == null) { reason = "辺が渡されていません"; return false; }

            // 受理判定は PickedMeshIndex を見るので、先に空へ戻してから通す。
            _picked.Clear();
            PickedMeshIndex   = -1;
            _lastRejectReason = null;
            _summaryDirty     = true;

            foreach (var pair in edges)
            {
                if (!AcceptEdge(meshIndex, pair, out string r))
                {
                    // 途中で弾かれたら中途半端な拾いを残さない。
                    _picked.Clear();
                    PickedMeshIndex   = -1;
                    _lastRejectReason = r;
                    reason            = r;
                    OnPicksChanged?.Invoke();
                    OnRepaint?.Invoke();
                    return false;
                }
                _picked.Add(pair);
            }

            if (_picked.Count == 0)
            {
                PickedMeshIndex = -1;
                reason          = "辺が 1 本もありません";
                OnPicksChanged?.Invoke();
                OnRepaint?.Invoke();
                return false;
            }

            OnPicksChanged?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        // ================================================================
        // 検査（サブパネル表示・オーバーレイ色分け用）
        // ================================================================

        /// <summary>現在の拾い具合。</summary>
        public sealed class Summary
        {
            /// <summary>そのまま実行できるか。</summary>
            public bool   Ok;
            /// <summary>実行できない理由、または面数などの説明。</summary>
            public string Message = "";

            public int  CountA;
            public int  CountB;
            public bool ClosedA;
            public bool ClosedB;

            /// <summary>辺群①の辺（色分け表示用）。分割できないときは空。</summary>
            public List<VertexPair> EdgesA = new List<VertexPair>();
            /// <summary>辺群②の辺（色分け表示用）。分割できないときは空。</summary>
            public List<VertexPair> EdgesB = new List<VertexPair>();
        }

        /// <summary>
        /// 直近の検査結果。オーバーレイ更新から毎フレーム呼ばれるため、
        /// 拾いや設定が変わったときだけ作り直す（毎フレームの再計算と確保を避ける）。
        /// </summary>
        private Summary _summaryCache;
        private bool    _summaryDirty = true;

        /// <summary>拾った辺を 2 群に分けられるかを調べる。実行はしない。</summary>
        public Summary Inspect()
        {
            PrunePicks();
            if (!_summaryDirty && _summaryCache != null) return _summaryCache;

            var s = BuildSummary();
            _summaryCache = s;
            _summaryDirty = false;
            return s;
        }

        private Summary BuildSummary()
        {
            var s = new Summary();

            if (_picked.Count == 0)
            {
                s.Message = "辺が拾われていません";
                return s;
            }

            if (!EdgeChainOps.SplitIntoTwoChains(_picked, out var a, out var b, out string msg))
            {
                s.Message = msg;
                return s;
            }

            s.CountA  = a.Count;  s.ClosedA = a.Closed;
            s.CountB  = b.Count;  s.ClosedB = b.Closed;
            s.EdgesA.AddRange(a.Edges);
            s.EdgesB.AddRange(b.Edges);

            if (a.Closed != b.Closed)
            {
                s.Message = "片方だけが閉じた辺群です。両方とも閉じるか、両方とも開いた辺にしてください";
                return s;
            }

            s.Ok = true;
            s.Message = $"辺群① {a.Count}頂点({(a.Closed ? "閉" : "開")}) / "
                      + $"辺群② {b.Count}頂点({(b.Closed ? "閉" : "開")})";
            return s;
        }

        // ================================================================
        // 計画（Viewer が実生成に使う）
        // ================================================================

        /// <summary>
        /// 現在の拾いと設定から計画を組む。
        ///
        /// 戻り値の型は穴つなぎと同じ <see cref="PlayerPrimitiveMeshSubPanel.BridgePlan"/> を使う。
        /// 挿入・ウェイト補間・Undo は Viewer の AppendBridgeInto がこの型を受け取るため、
        /// 別型にすると同じ処理をもう一組持つことになる。
        /// 辺群ブリッジは同一メッシュ内なので SrcMeshA / SrcMeshB は同じ値になる。
        /// </summary>
        public bool TryBuildPlan(
            out PlayerPrimitiveMeshSubPanel.BridgePlan plan, out string message)
        {
            plan = null;
            message = null;

            var model = _project?.CurrentModel;
            if (model == null) { message = "モデルがありません"; return false; }

            PrunePicks();

            var mc = (PickedMeshIndex >= 0) ? model.GetMeshContext(PickedMeshIndex) : null;
            var mo = mc?.MeshObject;
            if (mo == null) { message = "拾った辺のオブジェクトが見つかりません"; return false; }

            if (!EdgeChainOps.SplitIntoTwoChains(_picked, out var chainA, out var chainB, out message))
                return false;

            // ワールド座標へ出す行列。スキンドは頂点が既にワールド（バインド）空間で、
            // かつ WorldMatrix は親ボーンのワールド行列なので、掛けると位置が飛ぶ。
            // 判定は MeshContext.VertexToWorldMatrix に集約してある
            // （書き戻しに使う AppendBridgeInto 側の WorldToVertexMatrix と対になる）。
            Matrix4x4 w = mc.VertexToWorldMatrix;

            var worldA = new List<Vector3>(chainA.Count);
            var worldB = new List<Vector3>(chainB.Count);
            foreach (int v in chainA.Order) worldA.Add(w.MultiplyPoint3x4(mo.Vertices[v].Position));
            foreach (int v in chainB.Order) worldB.Add(w.MultiplyPoint3x4(mo.Vertices[v].Position));

            bool flipCorr = FlipCorrespondence;

            if (AutoCorrespondence)
            {
                // 閉環は Order が回転し、開環は反転要否が決まる。
                if (!EdgeChainOps.ResolveCorrespondence(
                        chainA, worldA, chainB, worldB, out bool autoFlip, out string cmsg))
                {
                    message = cmsg;
                    return false;
                }

                // 回転が起きた閉環では worldA / worldB も並べ直す。
                if (chainA.Closed)
                {
                    worldA.Clear(); worldB.Clear();
                    foreach (int v in chainA.Order) worldA.Add(w.MultiplyPoint3x4(mo.Vertices[v].Position));
                    foreach (int v in chainB.Order) worldB.Add(w.MultiplyPoint3x4(mo.Vertices[v].Position));
                }

                // 自動判定と手動の反転は排他ではなく重ねる（自動で合わない形を手で直せる）。
                flipCorr = autoFlip ^ FlipCorrespondence;
            }

            var result = BridgeLoopOps.Build(
                chainA.Count, chainB.Count, chainA.Closed, chainB.Closed,
                flipCorr, FlipFaces, Subdivisions);

            if (!result.Ok) { message = result.Message; return false; }

            plan = new PlayerPrimitiveMeshSubPanel.BridgePlan
            {
                SrcMeshA = PickedMeshIndex,
                SrcMeshB = PickedMeshIndex,
                LoopA    = new List<int>(chainA.Order),
                LoopB    = new List<int>(chainB.Order),
                Result   = result,
            };
            plan.WorldA.AddRange(worldA);
            plan.WorldB.AddRange(worldB);

            message = result.Message;
            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)
        {
            _project = project;
            ClearPicks();
            InvalidateBoundaryCache();
        }

        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)        { _commandQueue = queue; }

        public MeshUndoController UndoController => _undoController;
        public CommandQueue       Commands       => _commandQueue;

        public void Activate(ToolContext ctx)
        {
            // 他のツールがトポロジを変えた後にこのツールへ戻ることがある。
            // キャッシュを捨て、消えた辺を拾いから落としてから始める。
            InvalidateBoundaryCache();
            _summaryDirty = true;
            PrunePicks();
            ApplyHoverSelectionMode();
        }

        public void Deactivate(ToolContext ctx) { }

        /// <summary>トポロジが変わったら境界辺キャッシュと拾いを捨てる。</summary>
        public void InvalidateOnTopologyChanged()
        {
            InvalidateBoundaryCache();
            ClearPicks();
        }

        /// <summary>拾いは常に辺なので、ホバーは Edge に固定する。</summary>
        public void ApplyHoverSelectionMode()
        {
            ApplyHoverModeToAllMeshes?.Invoke(MeshSelectMode.Edge);
        }

        /// <summary>このツールが要求するホバー種別。Viewer が override を決めるときに読む。</summary>
        public MeshSelectMode HoverSelectMode => MeshSelectMode.Edge;

        /// <summary>Escape で拾いを捨てる。</summary>
        public void Cancel() => ClearPicks();

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        private bool    _boxSelecting;
        private Vector2 _boxStart;
        private Vector2 _boxEnd;

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var elem = GetHoverElement?.Invoke(MeshSelectMode.Edge) ?? PlayerHoverElement.None;

            if (elem.Kind != PlayerHoverKind.Edge)
            {
                // 何も無いところをクリック：修飾キー無しなら拾いを捨てる。
                if (!mods.Shift && !mods.Ctrl) ClearPicks();
                return;
            }

            var pair = new VertexPair(elem.EdgeV1, elem.EdgeV2);
            if (!AcceptEdge(elem.MeshIndex, pair, out string reason))
            {
                // 拾えない辺（別オブジェクト・境界辺でない）は黙って無視せず、
                // サブパネルの状態表示へ理由が出るよう記録する。
                _lastRejectReason = reason;
                OnPicksChanged?.Invoke();
                OnRepaint?.Invoke();
                return;
            }

            _lastRejectReason = null;
            if (!_picked.Remove(pair)) _picked.Add(pair);
            if (_picked.Count == 0) PickedMeshIndex = -1;
            _summaryDirty = true;

            OnPicksChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _boxSelecting = true;
            _boxStart     = screenPos;
            _boxEnd       = screenPos;
            OnBoxSelectUpdate?.Invoke(_boxStart, _boxEnd);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_boxSelecting) return;
            _boxEnd = screenPos;
            OnBoxSelectUpdate?.Invoke(_boxStart, _boxEnd);
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (!_boxSelecting) return;
            _boxEnd = screenPos;
            _boxSelecting = false;

            // 背面の辺を落とすため、GPU の頂点可視フラグを読み戻してから走査する。
            OnReadBackVertexFlags?.Invoke();
            CommitBoxPick(mods);

            OnBoxSelectEnd?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>矩形の中にある辺を拾う。</summary>
        private void CommitBoxPick(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null || GetScreenPositions == null) return;

            var screenPos = GetScreenPositions();
            if (screenPos == null) return;

            float vpH = GetViewportHeight?.Invoke() ?? 0f;
            Rect rect = MakeRect(_boxStart, _boxEnd);

            bool additive = mods.Shift || mods.Ctrl;
            if (!additive) { _picked.Clear(); PickedMeshIndex = -1; }

            // 走査対象は選択中の描画オブジェクト。ただし拾える辺は 1 オブジェクト分だけ。
            // 既に拾っているオブジェクトがあればそれに限定する。
            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                if (PickedMeshIndex >= 0 && ctxIdx != PickedMeshIndex) continue;

                var mc = model.GetMeshContext(ctxIdx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;

                Func<int, Vector2> vertexScreen = (i) =>
                {
                    if (vertexOffset + i >= screenPos.Length)
                        return new Vector2(-10000, -10000);
                    return new Vector2(screenPos[vertexOffset + i].x,
                                       vpH - screenPos[vertexOffset + i].y);
                };

                var boundary = _boundaryEdgeOnly ? GetBoundaryEdges(mo) : null;
                var found    = new List<VertexPair>();

                // 面の辺を走査する。2 頂点の面（補助線分）は辺を持たないので除く
                // （BoundaryEdgeOps.CollectBoundaryEdges と同じ扱い）。
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    int n = face.VertexCount;
                    if (n < 3) continue;

                    for (int ei = 0; ei < n; ei++)
                    {
                        int v1 = face.VertexIndices[ei];
                        int v2 = face.VertexIndices[(ei + 1) % n];
                        if (v1 == v2) continue;

                        var pair = new VertexPair(v1, v2);
                        if (boundary != null && !boundary.Contains(pair)) continue;

                        // GPU 計算済みの頂点可視フラグで両端を判定し、裏側の辺を除く
                        // （MoveToolHandler.CommitBoxSelect の辺走査と同じ規則）。
                        if (IsVertexVisible != null
                            && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                            continue;

                        if (rect.Contains(vertexScreen(v1), true) &&
                            rect.Contains(vertexScreen(v2), true))
                            found.Add(pair);
                    }
                }

                if (found.Count == 0) continue;

                if (PickedMeshIndex < 0) PickedMeshIndex = ctxIdx;

                foreach (var pair in found)
                {
                    if (mods.Ctrl) { if (!_picked.Remove(pair)) _picked.Add(pair); }
                    else            _picked.Add(pair);
                }

                // 1 オブジェクト分だけ拾う。
                break;
            }

            if (_picked.Count == 0) PickedMeshIndex = -1;
            _lastRejectReason = null;
            _summaryDirty     = true;
            OnPicksChanged?.Invoke();
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            return new Rect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Abs(a.x - b.x),
                Mathf.Abs(a.y - b.y));
        }

        // ================================================================
        // 拾える辺かどうか
        // ================================================================

        private string _lastRejectReason;

        /// <summary>直近に拾えなかった理由。拾えたときは null。</summary>
        public string LastRejectReason => _lastRejectReason;

        private bool AcceptEdge(int meshIndex, VertexPair pair, out string reason)
        {
            reason = null;

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (meshIndex < 0) { reason = "オブジェクトを特定できません"; return false; }

            if (PickedMeshIndex >= 0 && meshIndex != PickedMeshIndex)
            {
                reason = "辺群ブリッジは 1 つのオブジェクト内だけで行えます。"
                       + "別のオブジェクトの辺を使うときは「拾った辺を捨てる」を押してください";
                return false;
            }

            var mo = model.GetMeshContext(meshIndex)?.MeshObject;
            if (mo == null) { reason = "オブジェクトが見つかりません"; return false; }

            if (_boundaryEdgeOnly && !GetBoundaryEdges(mo).Contains(pair))
            {
                reason = "境界辺（1 面だけが使う辺）ではありません。"
                       + "内部の辺も使うときは「境界辺のみを対象にする」を外してください";
                return false;
            }

            if (PickedMeshIndex < 0) PickedMeshIndex = meshIndex;
            return true;
        }

        private void InvalidateBoundaryCache()
        {
            _edgeCacheMesh        = null;
            _boundaryCache        = null;
            _allEdgeCache         = null;
            _edgeCacheFaceCount   = -1;
            _edgeCacheVertexCount = -1;
        }

        /// <summary>
        /// 境界辺と全辺の集合を作り直すべきかを見て、必要なら作る。
        /// メッシュ実体・面数・頂点数が同じ間は使い回す。
        /// </summary>
        private void EnsureEdgeCache(MeshObject mo)
        {
            if (ReferenceEquals(_edgeCacheMesh, mo)
                && _boundaryCache != null && _allEdgeCache != null
                && _edgeCacheFaceCount   == mo.FaceCount
                && _edgeCacheVertexCount == mo.VertexCount)
                return;

            _allEdgeCache = new HashSet<VertexPair>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var face = mo.Faces[fi];
                int n = face.VertexCount;
                if (n < 3) continue;   // 補助線分は辺を持たない
                for (int i = 0; i < n; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % n];
                    if (a == b) continue;
                    _allEdgeCache.Add(new VertexPair(a, b));
                }
            }

            _boundaryCache        = BoundaryEdgeOps.CollectBoundaryEdges(mo);
            _edgeCacheMesh        = mo;
            _edgeCacheFaceCount   = mo.FaceCount;
            _edgeCacheVertexCount = mo.VertexCount;
        }

        /// <summary>境界辺（1 面だけが使う辺）の集合。</summary>
        private HashSet<VertexPair> GetBoundaryEdges(MeshObject mo)
        {
            EnsureEdgeCache(mo);
            return _boundaryCache;
        }

        /// <summary>
        /// もう存在しない辺を拾いから落とす。
        ///
        /// 他のツールがトポロジを変えると、拾った辺がメッシュから消えていることがある。
        /// 消えた辺を持ったまま 2 群へ分けると、実在しない位相で面を張ってしまう。
        /// 毎回の Inspect / 計画作成の入口で通す（拾いは小さいので走査は軽い）。
        /// </summary>
        private void PrunePicks()
        {
            if (_picked.Count == 0)
            {
                if (PickedMeshIndex >= 0) { PickedMeshIndex = -1; _summaryDirty = true; }
                return;
            }

            var model = _project?.CurrentModel;
            var mo    = (PickedMeshIndex >= 0) ? model?.GetMeshContext(PickedMeshIndex)?.MeshObject : null;
            if (mo == null)
            {
                _picked.Clear();
                PickedMeshIndex = -1;
                _summaryDirty   = true;
                return;
            }

            EnsureEdgeCache(mo);

            var valid = _boundaryEdgeOnly ? _boundaryCache : _allEdgeCache;
            int before = _picked.Count;
            _picked.RemoveWhere(e => !valid.Contains(e));

            if (_picked.Count == 0) PickedMeshIndex = -1;
            if (_picked.Count != before)
            {
                _lastRejectReason = null;
                _summaryDirty     = true;
            }
        }
    }
}
