// Assets/Editor/Poly_Ling/Tools/Topology/EdgeExtrudeTool.cs
// 面張りツール - IToolSettings対応版
//
// 【押し出しの形】
//   選択中の辺・線分の頂点を 1 回だけ複製し（共有頂点は 1 つにまとめる）、
//   辺・線分ごとに「元の 2 頂点 + 複製 2 頂点」の四角形を足す。
//   共有頂点の複製が 1 つなので、隣り合う四角形はつながった帯になる。
//   線分（2 頂点の面）は作り替えずにそのまま残す（線分群の前提
//   「隣り合う点の間に 2 頂点の面が 1 枚ある」を壊さないため）。
//
// 【ドラッグ量】
//   押した位置から今の位置までの画面上の差（全体量）で毎回計算し直す。
//   差分を足し込む方式は、押し出し開始前の移動を取りこぼしてポインタから遅れる。
//   頂点移動ツールの自由移動（MoveToolHandler.Press.cs）と同じ考え方。
//
// 【線分から作る四角形の表】
//   線分には隣の面が無いので、押し始めたときのカメラ側を表にする。
//   判定はワールド空間：線分の端点は GPU の値（ctx.GetVertexWorldPosition）、
//   押し出し方向は画面差から求めたワールド方向。
//   決めた結果（裏返す線分の一覧）をコマンドへ載せる。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面張り（Extrude）ツール
    /// </summary>
    public partial class EdgeExtrudeTool : IEditTool
    {
        public string Name => "Extrude";
        public string DisplayName => "Extrude";

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeExtrudeSettings _settings = new EdgeExtrudeSettings();
        public IToolSettings Settings => _settings;

        public EdgeExtrudeSettings.ExtrudeMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        public bool SnapToAxis
        {
            get => _settings.SnapToAxis;
            set => _settings.SnapToAxis = value;
        }

        public float DragSensitivity
        {
            get => _settings.DragSensitivity;
            set => _settings.DragSensitivity = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum ExtrudeState
        {
            Idle,
            PendingAction,
            Extruding
        }
        private ExtrudeState _state = ExtrudeState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private VertexPair? _hitEdgeOnMouseDown;
        private int _hitLineOnMouseDown = -1;
        private Vector2 _screenTotal;   // OnMouseDrag（差分入力）用の全体量

        // ホバー
        private VertexPair? _hoverEdge;
        private int _hoverLine = -1;

        // 押し出し量（対象メッシュのローカル空間）
        private Vector3 _accumMove;

        // ドラッグ中の頂点位置更新用
        private struct ExtrudeDragVertex { public int Index; public Vector3 BasePos; }
        private List<ExtrudeDragVertex> _extrudeDragVertices = new List<ExtrudeDragVertex>();

        // 押し出し対象
        private List<EdgeInfo> _targetEdges = new List<EdgeInfo>();
        private List<int> _targetLines = new List<int>();
        /// <summary>四角形を裏返す線分（表をカメラ側に向けるため）。</summary>
        private HashSet<int> _reversedLines = new HashSet<int>();

        // Undo
        private MeshObjectSnapshot _snapshotBefore;

        private struct EdgeInfo
        {
            public int V0, V1;
            public int? AdjacentFace;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.CurrentButton != 0)
                return false;

            if (_state != ExtrudeState.Idle)
                return false;

            if (ctx.ActiveMeshObject == null || ctx.SelectionState == null)
                return false;

            _mouseDownScreenPos = mousePos;
            _screenTotal = Vector2.zero;

            // _hitEdgeOnMouseDown / _hitLineOnMouseDown はハンドラーが PrepareHit() でセット
            if (_hitEdgeOnMouseDown.HasValue || _hitLineOnMouseDown >= 0)
            {
                _state = ExtrudeState.PendingAction;
                // マウスダウン時にスナップショット取得
                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                return false;
            }

            return false;
        }

        /// <summary>差分入力の経路。全体量へ足し込んで DragTo に渡す。</summary>
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            _screenTotal += delta;
            return DragTo(ctx, _screenTotal);
        }

        /// <summary>
        /// ドラッグ。screenTotal は押した位置から今の位置までの画面上の差（+Y が画面上）。
        /// ドラッグ開始の判定は呼び出し側（入力の振り分け）が済ませているので、
        /// ここでは待たずに押し出しを始める。
        /// </summary>
        public bool DragTo(ToolContext ctx, Vector2 screenTotal)
        {
            switch (_state)
            {
                case ExtrudeState.PendingAction:
                    // 方向が決まらないうちは始めない（線分の四角形の表の判定に方向が要る）
                    if (screenTotal.sqrMagnitude < 1e-6f) return true;
                    StartExtrude(ctx, screenTotal);
                    if (_state == ExtrudeState.Extruding) UpdateExtrude(ctx, screenTotal);
                    ctx.Repaint?.Invoke();
                    return true;

                case ExtrudeState.Extruding:
                    UpdateExtrude(ctx, screenTotal);
                    ctx.Repaint?.Invoke();
                    return true;
            }
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            bool handled = false;

            switch (_state)
            {
                case ExtrudeState.Extruding:
                    EndExtrude(ctx);
                    handled = true;
                    break;

                case ExtrudeState.PendingAction:
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
            {
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
            }
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = ExtrudeState.Idle;
            _hitEdgeOnMouseDown = null;
            _hitLineOnMouseDown = -1;
            _extrudeDragVertices.Clear();
            _targetEdges.Clear();
            _targetLines.Clear();
            _reversedLines.Clear();
            _snapshotBefore = null;
            _accumMove = Vector3.zero;
            _screenTotal = Vector2.zero;
            _gizmoSession = false;
            _vertexRemap.Clear();
        }

        public void OnSelectionChanged(ToolContext ctx)
        {
        }

        // ── UIToolkit hover support ───────────────────────────────────────
        /// <summary>現在ホバー中のエッジ（UIToolkit オーバーレイ用）</summary>
        public VertexPair? HoverEdge => _hoverEdge;
        public int HoverLine => _hoverLine;

        /// <summary>ハンドラーが GPU ホバー結果からセット。FindEdgeAtPosition/FindLineAtPosition（CPU・カリング無視）使用禁止。</summary>
        public void SetHoverEdge(VertexPair? edge, int line = -1)
        {
            bool canSet = _state == ExtrudeState.Idle || _state == ExtrudeState.PendingAction;
            _hoverEdge = canSet ? edge : (VertexPair?)null;
            _hoverLine = canSet ? line : -1;
        }

        /// <summary>OnLeftDragBegin でハンドラーが GPU ホバー結果から事前にセット。</summary>
        public void PrepareHit(VertexPair? edge, int line = -1)
        {
            _hitEdgeOnMouseDown = edge;
            _hitLineOnMouseDown = line;
        }

        // ================================================================
        // コマンド経路
        //
        //   押し出し量はドラッグから決まるので、対象と量を直接渡せる入口をここに置く。
        //   生成と Undo 記録はマウス経路と同じ CollectTargetEdges / ExecuteExtrude / EndExtrude を通す。
        //   量は対象メッシュのローカル空間のベクトル。
        // ================================================================

        /// <summary>
        /// 指定した辺・線分（複数可）を押し出す。
        /// newPositions があれば、複製頂点の位置をその並び（複製を作る順）で置く。無ければ localOffset だけずらす。
        /// </summary>
        /// <param name="edges">対象の辺。</param>
        /// <param name="lines">対象の線分索引（頂点数 2 の面）。</param>
        /// <param name="reversedLines">四角形を裏返す線分索引（lines の部分集合）。null は無し。</param>
        /// <param name="localOffset">対象メッシュのローカル空間での押し出し量（newPositions が無いとき）。</param>
        /// <param name="newPositions">複製頂点の最終位置（ローカル、複製を作る順）。null なら localOffset を使う。</param>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ApplyExtrudeFromCommand(
            ToolContext ctx, IReadOnlyList<VertexPair> edges, IReadOnlyList<int> lines,
            IEnumerable<int> reversedLines, Vector3 localOffset, IReadOnlyList<Vector3> newPositions,
            out string reason)
        {
            reason = null;

            if (_state != ExtrudeState.Idle)
            { reason = "ドラッグ中は実行できません"; return false; }

            var mo = ctx?.ActiveMeshObject;
            if (mo == null || ctx.SelectionState == null)
            { reason = "編集対象メッシュがありません"; return false; }

            int edgeCount = edges?.Count ?? 0;
            int lineCount = lines?.Count ?? 0;
            if (edgeCount == 0 && lineCount == 0)
            { reason = "辺か線分を 1 つ以上指定してください"; return false; }

            if (edges != null)
                foreach (var e in edges)
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount || e.V2 < 0 || e.V2 >= mo.VertexCount)
                    { reason = $"辺 ({e.V1},{e.V2}) の頂点番号が範囲外です"; return false; }

            if (lines != null)
                foreach (int li in lines)
                    if (li < 0 || li >= mo.FaceCount || mo.Faces[li].VertexCount != 2)
                    { reason = $"線分 {li} は範囲外か、線分ではありません"; return false; }

            bool usePositions = newPositions != null && newPositions.Count > 0;
            if (!usePositions && localOffset.sqrMagnitude < 1e-10f)
            { reason = "押し出し量が 0 です"; return false; }

            try
            {
                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(
                        ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);

                ctx.SelectionState.Edges.Clear();
                ctx.SelectionState.Lines.Clear();
                if (edges != null) foreach (var e in edges) ctx.SelectionState.Edges.Add(e);
                if (lines != null) foreach (int li in lines) ctx.SelectionState.Lines.Add(li);

                CollectTargetEdges(ctx);
                if (_targetEdges.Count == 0 && _targetLines.Count == 0)
                {
                    RestoreSnapshot(ctx);
                    reason = "押し出せる辺・線分がありません";
                    return false;
                }

                _reversedLines.Clear();
                if (reversedLines != null)
                    foreach (int li in reversedLines)
                        if (_targetLines.Contains(li)) _reversedLines.Add(li);

                if (usePositions)
                {
                    ExecuteExtrude(ctx, Vector3.zero);
                    if (newPositions.Count != _extrudeDragVertices.Count)
                    {
                        reason = $"NewVertexPositions の点数（{newPositions.Count}）が複製頂点の数（{_extrudeDragVertices.Count}）と合いません";
                        RestoreSnapshot(ctx);
                        return false;
                    }
                    for (int i = 0; i < _extrudeDragVertices.Count; i++)
                        mo.Vertices[_extrudeDragVertices[i].Index].Position = newPositions[i];
                    mo.InvalidatePositionCache();
                    ctx.SyncMesh?.Invoke();
                }
                else
                {
                    _accumMove = localOffset;
                    ExecuteExtrude(ctx, localOffset);
                }
                EndExtrude(ctx);   // Undo 記録
            }
            finally
            {
                Reset();
            }

            return true;
        }

        /// <summary>押す前のスナップショットへ戻す（Undo は積まない）。</summary>
        private void RestoreSnapshot(ToolContext ctx)
        {
            if (_snapshotBefore != null && ctx?.UndoController != null)
            {
                _snapshotBefore.ApplyTo(ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                ctx.SyncMesh?.Invoke();
            }
            _snapshotBefore = null;
        }

        // ================================================================
        // ギズモ経路（移動・回転・拡大縮小のギズモで押し出す）
        //
        //   掴んだ瞬間に量 0 で押し出し、頂点選択を複製頂点にする（BeginGizmoSession）。
        //   ドラッグ中の変形は各ギズモの既存処理が複製頂点に対して行う。
        //   離したときは、まず複製頂点の位置を読み（CaptureGizmoResult）、
        //   ギズモ側を開始状態へ戻させてから、押す前へ戻して結果を取り出す（FinishGizmoSession）。
        //   呼び出し側はそれを押し出しコマンド 1 本で確定する（Undo 1 回）。
        // ================================================================

        private bool _gizmoSession;
        private readonly List<Vector3> _capturedPositions = new List<Vector3>();
        private bool _capturedChanged;
        private Dictionary<int, int> _vertexRemap = new Dictionary<int, int>();

        /// <summary>ギズモでの押し出し中か。</summary>
        public bool GizmoSessionActive => _gizmoSession;

        /// <summary>
        /// 選択中の辺・線分を量 0 で押し出し、頂点選択を複製頂点にする。
        /// 押し出せる対象が無ければ false（何も変えない）。
        /// </summary>
        public bool BeginGizmoSession(ToolContext ctx)
        {
            if (_state != ExtrudeState.Idle || _gizmoSession) return false;
            if (ctx?.ActiveMeshObject == null || ctx.SelectionState == null) return false;

            CollectTargetEdges(ctx);
            if (_targetEdges.Count == 0 && _targetLines.Count == 0) { Reset(); return false; }

            if (ctx.UndoController != null)
                _snapshotBefore = MeshObjectSnapshot.Capture(
                    ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);

            _reversedLines.Clear();
            _accumMove = Vector3.zero;
            ExecuteExtrude(ctx, Vector3.zero);
            _state = ExtrudeState.Extruding;
            _gizmoSession = true;
            return true;
        }

        /// <summary>
        /// 離したとき最初に呼ぶ。複製頂点の今の位置を読み、線分の四角形の表を決める。
        /// ギズモ側が開始状態へ戻す前に呼ぶこと。
        /// </summary>
        public bool CaptureGizmoResult(ToolContext ctx)
        {
            if (!_gizmoSession) return false;
            var mo = ctx?.ActiveMeshObject;
            _capturedPositions.Clear();
            _capturedChanged = false;
            if (mo == null) return true;

            foreach (var dv in _extrudeDragVertices)
            {
                var p = (dv.Index >= 0 && dv.Index < mo.VertexCount) ? mo.Vertices[dv.Index].Position : dv.BasePos;
                _capturedPositions.Add(p);
                if ((p - dv.BasePos).sqrMagnitude > 1e-12f) _capturedChanged = true;
            }

            if (_capturedChanged) DecideLineWindingLocal(ctx, mo);
            return true;
        }

        /// <summary>
        /// ギズモ側を開始状態へ戻させた後に呼ぶ。押す前へ戻し、確定内容を取り出す。
        /// 動いていなければ changed = false（呼び出し側はコマンドを送らない）。
        /// </summary>
        public void FinishGizmoSession(
            ToolContext ctx, out bool changed, out List<VertexPair> edges, out List<int> lines,
            out List<int> reversedLines, out List<Vector3> positions)
        {
            changed       = _gizmoSession && _capturedChanged;
            edges         = new List<VertexPair>();
            lines         = new List<int>();
            reversedLines = new List<int>();
            positions     = new List<Vector3>(_capturedPositions);

            foreach (var e in _targetEdges) edges.Add(new VertexPair(e.V0, e.V1));
            lines.AddRange(_targetLines);
            reversedLines.AddRange(_reversedLines);

            RestoreSnapshot(ctx);
            Reset();
        }

        /// <summary>
        /// 線分から作った四角形（v0, v1, v1', v0'）の表がカメラ側を向くか、ローカル空間で調べる。
        /// カメラ位置はワールド → ローカル（書き戻し方向の変換）で移す。
        /// </summary>
        private void DecideLineWindingLocal(ToolContext ctx, MeshObject mo)
        {
            _reversedLines.Clear();
            if (_targetLines.Count == 0) return;
            Vector3 camLocal = ctx.ActiveWorldToLocal(ctx.CameraPosition);

            foreach (int lineIdx in _targetLines)
            {
                var line = mo.Faces[lineIdx];
                int v0 = line.VertexIndices[0], v1 = line.VertexIndices[1];
                if (!_vertexRemap.TryGetValue(v0, out int nv0) || !_vertexRemap.TryGetValue(v1, out int nv1)) continue;
                Vector3 p0 = mo.Vertices[v0].Position, p1 = mo.Vertices[v1].Position;
                Vector3 p2 = mo.Vertices[nv1].Position, p3 = mo.Vertices[nv0].Position;
                Vector3 n = NormalHelper.CalculateFaceNormal(p0, p1, p2);
                if (n.sqrMagnitude < 1e-12f) n = NormalHelper.CalculateFaceNormal(p0, p2, p3);
                Vector3 center = (p0 + p1 + p2 + p3) * 0.25f;
                if (Vector3.Dot(n, camLocal - center) < 0f) _reversedLines.Add(lineIdx);
            }
        }

        /// <summary>
        /// 取り出せるドラッグ結果があるか。TryTakeExtrudeFromDrag が true を返す条件と同じ。
        /// </summary>
        public bool ExtrudePending
            => _state == ExtrudeState.Extruding
               && (_targetEdges.Count > 0 || _targetLines.Count > 0)
               && _accumMove.sqrMagnitude > 1e-10f;

        /// <summary>
        /// ドラッグの確定内容（押し出していた辺・線分の全部と量）を取り出し、開始状態へ戻す。
        ///
        /// 押し出しはドラッグ開始時にトポロジーまで作るので、位置を戻すだけでは足りない。
        /// マウスダウン時のスナップショットを丸ごと適用する。
        /// _snapshotBefore は null にするので、続けて呼ばれる OnMouseUp（EndExtrude）は Undo を積まない。
        /// </summary>
        public bool TryTakeExtrudeFromDrag(
            ToolContext ctx, out List<VertexPair> edges, out List<int> lines,
            out List<int> reversedLines, out Vector3 localOffset)
        {
            edges         = new List<VertexPair>();
            lines         = new List<int>();
            reversedLines = new List<int>();
            localOffset   = Vector3.zero;

            if (!ExtrudePending) return false;
            if (ctx?.UndoController == null || _snapshotBefore == null) return false;

            foreach (var e in _targetEdges) edges.Add(new VertexPair(e.V0, e.V1));
            lines.AddRange(_targetLines);
            reversedLines.AddRange(_reversedLines);
            localOffset = _accumMove;

            _snapshotBefore.ApplyTo(ctx.UndoController.MeshUndoContext, ctx.SelectionState);
            _snapshotBefore = null;

            ctx.SyncMesh?.Invoke();
            return true;
        }

        // ================================================================
        // 押し出し処理
        // ================================================================

        private void StartExtrude(ToolContext ctx, Vector2 screenTotal)
        {
            // 掴んだ辺・線分が選択に入っていなければ、それだけを対象にする。
            // 入っていれば選択中の辺・線分を全部押し出す。
            if (_hitEdgeOnMouseDown.HasValue)
            {
                var edge = _hitEdgeOnMouseDown.Value;
                if (!ctx.SelectionState.Edges.Contains(edge))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Edges.Add(edge);
                }
            }

            if (_hitLineOnMouseDown >= 0)
            {
                if (!ctx.SelectionState.Lines.Contains(_hitLineOnMouseDown))
                {
                    ctx.SelectionState.Edges.Clear();
                    ctx.SelectionState.Lines.Clear();
                    ctx.SelectionState.Lines.Add(_hitLineOnMouseDown);
                }
            }

            CollectTargetEdges(ctx);

            if (_targetEdges.Count == 0 && _targetLines.Count == 0)
            {
                _state = ExtrudeState.Idle;
                return;
            }

            // 線分の四角形の表：押し始めた方向でカメラ側を表にする（ワールド空間）
            DecideLineWinding(ctx, ScreenDeltaToWorldDelta(ctx, screenTotal));

            _accumMove = Vector3.zero;

            // トポロジーを即時実行し _extrudeDragVertices を確定させる
            ExecuteExtrude(ctx, Vector3.zero);  // 内部で ctx.SyncMesh (= NotifyTopologyChanged) を呼ぶ

            _state = ExtrudeState.Extruding;
            // EnterTransformDragging は使用しない。
            // 押し出しはトポロジー変更後の位置更新であり TransformDragging モードを使うと
            // エッジ/頂点描画が無効化されるため。SyncMeshPositionsOnly で直接更新する。
        }

        /// <summary>
        /// 押した位置からの画面上の差を、複製頂点の移動量（ローカル）にして反映する。
        /// 基準は複製頂点の先頭。スキンド頂点はボーンの SkinningMatrix で変換されるため、
        /// メッシュの WorldMatrixInverse では倍率と向きが合わない（WorldToLocalVectorAt を使う）。
        /// </summary>
        private void UpdateExtrude(ToolContext ctx, Vector2 screenTotal)
        {
            int basis = _extrudeDragVertices.Count > 0 ? _extrudeDragVertices[0].Index : -1;
            _accumMove = ctx.WorldToLocalVectorAt(basis, ScreenDeltaToWorldDelta(ctx, screenTotal));

            var meshObject = ctx.ActiveMeshObject;
            if (meshObject != null)
            {
                foreach (var dv in _extrudeDragVertices)
                {
                    if (dv.Index >= 0 && dv.Index < meshObject.VertexCount)
                        meshObject.Vertices[dv.Index].Position = dv.BasePos + _accumMove;
                }
            }
            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndExtrude(ToolContext ctx)
        {
            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                var snapshotAfter = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                {
                    string __dbgDesc = "Extrude Edges";
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _snapshotBefore = null;
        }

        /// <summary>
        /// 対象の頂点を 1 回だけ複製し（共有頂点は 1 つ）、辺・線分ごとに四角形を足す。
        /// 線分は作り替えずに残す。
        /// </summary>
        private void ExecuteExtrude(ToolContext ctx, Vector3 offset)
        {
            var meshObject = ctx.ActiveMeshObject;
            _vertexRemap.Clear();
            var vertexRemap = _vertexRemap;

            // 押し出しで増える頂点の始まり。部品ID / サブIDの採番に使う。
            int origVertexCount = meshObject.VertexCount;

            // 複製順を安定させるため、辺・線分の並び順で頂点を集める。
            var allVertices = new List<int>();
            var seen = new HashSet<int>();
            void Collect(int v) { if (v >= 0 && v < meshObject.VertexCount && seen.Add(v)) allVertices.Add(v); }
            foreach (var edge in _targetEdges) { Collect(edge.V0); Collect(edge.V1); }
            foreach (int lineIdx in _targetLines)
            {
                if (lineIdx < 0 || lineIdx >= meshObject.FaceCount) continue;
                var face = meshObject.Faces[lineIdx];
                if (face.VertexCount != 2) continue;
                Collect(face.VertexIndices[0]);
                Collect(face.VertexIndices[1]);
            }

            if (allVertices.Count == 0) return;

            _extrudeDragVertices.Clear();
            foreach (int vIdx in allVertices)
            {
                var oldV = meshObject.Vertices[vIdx];
                int newIdx = meshObject.VertexCount;
                var newV = new Vertex { Position = oldV.Position + offset };
                newV.UVs.AddRange(oldV.UVs);
                newV.Normals.AddRange(oldV.Normals);
                // 複製元の BoneWeight をコピーする。設定しないと GPU 側で
                // メッシュ自身の context 索引が使われ（UnifiedBufferManager_Build.cs:356-362）、
                // 周囲の頂点と別の行列で変換されてこの頂点だけ離れた位置に置かれる。
                newV.BoneWeight = oldV.BoneWeight;
                vertexRemap[vIdx] = newIdx;
                meshObject.Vertices.Add(newV);
                _extrudeDragVertices.Add(new ExtrudeDragVertex { Index = newIdx, BasePos = oldV.Position });
            }

            // 今回増えた頂点を 1 つの部品として扱う。
            Poly_Ling.Ops.PartsIdOps.AssignNewVertices(meshObject, origVertexCount);

            int matIdx = ctx.CurrentMaterialIndex;
            var newEdges = new List<VertexPair>();
            var newFaceIndices = new List<int>();

            void AddQuad(int a, int b, int nb, int na)
            {
                var f = new Face { MaterialIndex = matIdx };
                f.VertexIndices.AddRange(new[] { a, b, nb, na });
                f.UVIndices.AddRange(new[] { a, b, nb, na });
                f.NormalIndices.AddRange(new[] { a, b, nb, na });
                meshObject.Faces.Add(f);
                newFaceIndices.Add(meshObject.FaceCount - 1);
            }

            foreach (var edge in _targetEdges)
            {
                if (!vertexRemap.TryGetValue(edge.V0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(edge.V1, out int nv1)) continue;

                bool reverseWinding = false;
                if (edge.AdjacentFace.HasValue && edge.AdjacentFace.Value < meshObject.FaceCount)
                {
                    var adjFace = meshObject.Faces[edge.AdjacentFace.Value];
                    int idxV0 = adjFace.VertexIndices.IndexOf(edge.V0);
                    int idxV1 = adjFace.VertexIndices.IndexOf(edge.V1);
                    if (idxV0 >= 0 && idxV1 >= 0)
                        reverseWinding = (idxV1 == (idxV0 + 1) % adjFace.VertexCount);
                }

                if (reverseWinding) AddQuad(edge.V0, nv0, nv1, edge.V1);
                else                AddQuad(edge.V0, edge.V1, nv1, nv0);
                newEdges.Add(new VertexPair(nv0, nv1));
            }

            // 線分：元の線分（2 頂点の面）は残し、四角形だけ足す。
            // _targetLines の面索引は、ここで面を末尾へ足しても変わらない。
            foreach (int lineIdx in _targetLines)
            {
                var line = meshObject.Faces[lineIdx];
                int v0 = line.VertexIndices[0], v1 = line.VertexIndices[1];
                if (!vertexRemap.TryGetValue(v0, out int nv0)) continue;
                if (!vertexRemap.TryGetValue(v1, out int nv1)) continue;

                if (_reversedLines.Contains(lineIdx)) AddQuad(v1, v0, nv0, nv1);
                else                                  AddQuad(v0, v1, nv1, nv0);
                newEdges.Add(new VertexPair(nv0, nv1));
            }

            // 押し出し後の選択：新しい辺と複製頂点。面は選ばない
            // （面を選ぶと、頂点への展開で元の頂点までギズモの対象になる）。
            ctx.SelectionState.Vertices.Clear();
            ctx.SelectionState.Edges.Clear();
            ctx.SelectionState.Lines.Clear();
            ctx.SelectionState.Faces.Clear();
            foreach (var dv in _extrudeDragVertices)
                ctx.SelectionState.Vertices.Add(dv.Index);
            foreach (var e in newEdges)
                ctx.SelectionState.Edges.Add(e);

            ctx.SyncMesh?.Invoke();
        }

        /// <summary>
        /// 線分から作る四角形（v0, v1, v1+d, v0+d）の表がカメラ側を向くか調べ、
        /// 向かない線分を _reversedLines に入れる。ワールド空間で判定する。
        /// 端点のワールド座標は GPU の値（ctx.GetVertexWorldPosition）。取れなければ判定しない。
        /// </summary>
        private void DecideLineWinding(ToolContext ctx, Vector3 worldDir)
        {
            _reversedLines.Clear();
            var mo = ctx.ActiveMeshObject;
            if (mo == null || ctx.GetVertexWorldPosition == null || worldDir.sqrMagnitude < 1e-12f) return;

            foreach (int lineIdx in _targetLines)
            {
                var line = mo.Faces[lineIdx];
                var w0 = ctx.GetVertexWorldPosition(line.VertexIndices[0]);
                var w1 = ctx.GetVertexWorldPosition(line.VertexIndices[1]);
                if (!w0.HasValue || !w1.HasValue) continue;

                Vector3 p0 = w0.Value, p1 = w1.Value, p2 = p1 + worldDir;
                Vector3 n = NormalHelper.CalculateFaceNormal(p0, p1, p2);
                Vector3 center = (p0 + p1 + p2 + (p0 + worldDir)) * 0.25f;
                if (Vector3.Dot(n, ctx.CameraPosition - center) < 0f)
                    _reversedLines.Add(lineIdx);
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void CollectTargetEdges(ToolContext ctx)
        {
            _targetEdges.Clear();
            _targetLines.Clear();

            foreach (var ep in ctx.SelectionState.Edges)
            {
                _targetEdges.Add(new EdgeInfo
                {
                    V0 = ep.V1,
                    V1 = ep.V2,
                    AdjacentFace = FindAdjacentFace(ctx.ActiveMeshObject, ep.V1, ep.V2)
                });
            }

            foreach (int idx in ctx.SelectionState.Lines)
            {
                if (idx >= 0 && idx < ctx.ActiveMeshObject.FaceCount &&
                    ctx.ActiveMeshObject.Faces[idx].VertexCount == 2)
                {
                    _targetLines.Add(idx);
                }
            }
        }

        private int? FindAdjacentFace(MeshObject md, int v0, int v1)
        {
            for (int i = 0; i < md.FaceCount; i++)
            {
                var f = md.Faces[i];
                if (f.VertexCount >= 3 && f.VertexIndices.Contains(v0) && f.VertexIndices.Contains(v1))
                    return i;
            }
            return null;
        }

        private Vector3 ScreenDeltaToWorldDelta(ToolContext ctx, Vector2 sd)
        {
            if (ctx.ScreenDeltaToWorldDelta != null)
                return ctx.ScreenDeltaToWorldDelta(sd, ctx.CameraPosition, ctx.CameraTarget, ctx.CameraDistance, ctx.PreviewRect);
            float s = ctx.CameraDistance * 0.001f;
            return new Vector3(sd.x * s, -sd.y * s, 0f);
        }
    }
}
