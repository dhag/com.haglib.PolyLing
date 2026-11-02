// Assets/Editor/Poly_Ling/Tools/Topology/EdgeBevelTool.cs
// エッジベベルツール - IToolSettings対応版
//
// 【実処理】ベベルの計算と面の組み替えは EdgeBevelOps（Core/Ops/EdgeBevelOps.cs）にある。
//   処理列の種類 bevel（BevelMeshModifier）と共用する（モディファイア枠組み計画 段 7a）。
//   このツールが受け持つのは、辺の入力（選択）・ドラッグ・Undo・選択の辺の消去・GPU への通知。

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
    public partial class EdgeBevelTool : IEditTool
    {
        public string Name => "Bevel";
        public string DisplayName => "Bevel";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeBevelSettings _settings = new EdgeBevelSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        public float Amount
        {
            get => _settings.Amount;
            set => _settings.Amount = value;
        }

        public int Segments
        {
            get => _settings.Segments;
            set => _settings.Segments = value;
        }

        public bool Fillet
        {
            get => _settings.Fillet;
            set => _settings.Fillet = value;
        }

        public float DragSensitivity
        {
            get => _settings.DragSensitivity;
            set => _settings.DragSensitivity = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum BevelState { Idle, PendingAction, Beveling }
        private BevelState _state = BevelState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private VertexPair? _hitEdgeOnMouseDown;
        private const float DragThreshold = 4f;

        // ベベル量の符号付き投影に使う「拡大方向」（スクリーン, ToImgui系）。
        // StartBevel 時の初期ドラッグ方向で固定する。初期方向へ動かすと拡大、
        // 戻すと縮小、開始点で0、行き過ぎは0クランプ、という直感的な対応にするため。
        private Vector2 _startDragDir = Vector2.right;

        // ホバー
        private VertexPair? _hoverEdge;

        // ベベル量
        private float _dragAmount;

        // Undo
        private MeshObjectSnapshot _snapshotBefore;

        // ドラッグ中の頂点位置更新用（実処理 EdgeBevelOps が返す）
        private List<EdgeBevelOps.DragVertex> _dragVertices = new List<EdgeBevelOps.DragVertex>();

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.CurrentButton != 0)
                return false;

            if (_state != BevelState.Idle)
                return false;

            if (ctx.ActiveMeshObject == null || ctx.SelectionState == null)
            {
                UnityEngine.Debug.Log($"[BevelDBG] OnMouseDown: MeshObject={ctx.ActiveMeshObject != null}, SelectionState={ctx.SelectionState != null} → skip");
                return false;
            }

            _mouseDownScreenPos = mousePos;
            // _hitEdgeOnMouseDown はハンドラーが PrepareHit() で GPU ホバー結果からセットする

            UnityEngine.Debug.Log($"[BevelDBG] OnMouseDown: pos={mousePos}, hitEdge={_hitEdgeOnMouseDown.HasValue}, vertexCount={ctx.ActiveMeshObject.VertexCount}, faceCount={ctx.ActiveMeshObject.FaceCount}");

            if (_hitEdgeOnMouseDown.HasValue)
            {
                _state = BevelState.PendingAction;
                // マウスダウン時にスナップショット取得
                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                return false;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            switch (_state)
            {
                case BevelState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    UnityEngine.Debug.Log($"[BevelDBG] OnMouseDrag PendingAction: dist={dragDistance:F1} threshold={DragThreshold}");
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitEdgeOnMouseDown.HasValue)
                            StartBevel(ctx, mousePos);
                        else
                        {
                            _state = BevelState.Idle;
                            return false;
                        }
                    }
                    ctx.Repaint?.Invoke();
                    return true;

                case BevelState.Beveling:
                    UpdateBevel(ctx, mousePos);
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
                case BevelState.Beveling:
                    EndBevel(ctx);
                    handled = true;
                    break;

                case BevelState.PendingAction:
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = BevelState.Idle;
            _hitEdgeOnMouseDown = null;
            _snapshotBefore = null;
            _dragAmount = 0f;
            _dragVertices.Clear();
        }

        public void OnSelectionChanged(ToolContext ctx) { }

        // ── UIToolkit hover support ───────────────────────────────────────
        /// <summary>現在ホバー中のエッジ（UIToolkit オーバーレイ用）</summary>
        public VertexPair? HoverEdge => _hoverEdge;

        /// <summary>ハンドラーが GPU ホバー結果からセット。FindEdgeAtPosition（CPU・カリング無視）使用禁止。</summary>
        public void SetHoverEdge(VertexPair? edge)
        {
            _hoverEdge = (_state == BevelState.Idle || _state == BevelState.PendingAction) ? edge : (VertexPair?)null;
        }

        /// <summary>OnLeftDragBegin でハンドラーが GPU ホバー結果から事前にセット。</summary>
        public void PrepareHit(VertexPair? edge) { _hitEdgeOnMouseDown = edge; }

        // ================================================================
        // コマンド経路
        //
        // 【なぜ要るか】
        //   ベベル量はドラッグの画面移動から導かれる（UpdateBevel）ので、
        //   コマンド（自動検証・MCP）からは通せない。辺と量を直接渡せる入口を
        //   ここに置き、生成と Undo 記録はマウス経路と同じ
        //   ExecuteBevel（→ EdgeBevelOps.Execute）/ EndBevel を通す。
        // ================================================================

        /// <summary>
        /// 指定した辺を指定量でベベルする。
        ///
        /// ExecuteBevel は _dragAmount を使って頂点位置まで決めるので、
        /// 量を入れてから 1 回呼べばドラッグ確定と同じ形になる。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ApplyBevelFromCommand(
            ToolContext ctx, VertexPair edge, float amount, int segments, bool fillet,
            out string reason)
        {
            reason = null;

            if (_state != BevelState.Idle)
            { reason = "ドラッグ中は実行できません"; return false; }

            var mo = ctx?.ActiveMeshObject;
            if (mo == null || ctx.SelectionState == null)
            { reason = "編集対象メッシュがありません"; return false; }

            if (edge.V1 < 0 || edge.V1 >= mo.VertexCount ||
                edge.V2 < 0 || edge.V2 >= mo.VertexCount)
            { reason = "辺の頂点番号が範囲外です"; return false; }

            if (amount <= 0f)
            { reason = "Amount は 0 より大きい値を指定してください"; return false; }

            int  savedSegments = Segments;
            bool savedFillet   = Fillet;
            float savedAmount  = Amount;
            try
            {
                Segments = segments;
                Fillet   = fillet;

                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(
                        ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);

                ctx.SelectionState.Edges.Clear();
                ctx.SelectionState.Edges.Add(edge);

                _dragAmount = amount;
                if (!ExecuteBevel(ctx))
                {
                    _snapshotBefore = null;
                    reason = "ベベルできる辺がありません（隣接面が 2 枚でない可能性があります）";
                    return false;
                }
                EndBevel(ctx);   // Amount = _dragAmount と Undo 記録
            }
            finally
            {
                Reset();
                Segments = savedSegments;
                Fillet   = savedFillet;
                // Amount は EndBevel が確定値を入れる。コマンドの量をパネルへ
                // 残さないよう、呼び出し前の値へ戻す。
                Amount   = savedAmount;
            }

            return true;
        }

        /// <summary>
        /// 取り出せるドラッグ結果があるか。TryTakeBevelFromDrag が true を返す条件と同じ。
        /// </summary>
        public bool BevelPending
            => _state == BevelState.Beveling && _hitEdgeOnMouseDown.HasValue && _dragAmount > 0f;

        /// <summary>
        /// ドラッグの確定内容を取り出し、開始状態へ戻す。
        ///
        /// 【なぜ要るか】
        ///   1 ドラッグ = 1 コマンドにするため。ドラッグ中の生成はプレビューとして扱い、
        ///   確定時はここで開始状態へ戻して辺と量だけを返す。呼び出し側
        ///   （EdgeBevelToolHandler）が EdgeBevelCommand を送り、実際の生成と
        ///   Undo 記録は ApplyBevelFromCommand が行う。
        ///
        /// 【戻し方】
        ///   ベベルはドラッグ開始時にトポロジーまで作る（StartBevel → ExecuteBevel）ので、
        ///   位置を戻すだけでは足りない。マウスダウン時のスナップショットを丸ごと適用する。
        ///   _snapshotBefore は null にするので、続けて呼ばれる OnMouseUp（EndBevel）は
        ///   Undo を積まない。
        /// </summary>
        public bool TryTakeBevelFromDrag(
            ToolContext ctx, out VertexPair edge, out float amount)
        {
            edge   = default;
            amount = 0f;

            if (!BevelPending) return false;
            if (ctx?.UndoController == null || _snapshotBefore == null) return false;

            edge   = _hitEdgeOnMouseDown.Value;
            amount = _dragAmount;

            _snapshotBefore.ApplyTo(ctx.UndoController.MeshUndoContext, ctx.SelectionState);
            _snapshotBefore = null;

            ctx.SyncMesh?.Invoke();
            return true;
        }

        // ================================================================
        // ベベル処理
        // ================================================================

        private void StartBevel(ToolContext ctx, Vector2 mousePos)
        {
            UnityEngine.Debug.Log($"[BevelDBG] StartBevel: hitEdge={_hitEdgeOnMouseDown}, selectionEdges={ctx.SelectionState?.Edges?.Count}");

            // 初期ドラッグ方向（スクリーン, ToImgui系）を拡大方向として固定する。
            Vector2 dir0 = mousePos - _mouseDownScreenPos;
            _startDragDir = dir0.sqrMagnitude > 1e-6f ? dir0.normalized : Vector2.right;

            // ヒットエッジを常に単独でセット（選択状態に関わらず）
            if (_hitEdgeOnMouseDown.HasValue)
            {
                ctx.SelectionState.Edges.Clear();
                ctx.SelectionState.Edges.Add(_hitEdgeOnMouseDown.Value);
            }

            _dragAmount = 0f;
            // トポロジーを即時実行し _dragVertices を確定させる
            // （内部で ctx.SyncMesh (= NotifyTopologyChanged) を呼ぶ）。対象の辺が無ければメッシュに触れない。
            if (!ExecuteBevel(ctx))
            {
                UnityEngine.Debug.Log($"[BevelDBG] StartBevel: targetEdges=0, Amount={Amount}, vertexCount={ctx.ActiveMeshObject?.VertexCount}");
                _state = BevelState.Idle;
                return;
            }

            UnityEngine.Debug.Log($"[BevelDBG] StartBevel after ExecuteBevel: dragVertices={_dragVertices.Count}, vertexCount={ctx.ActiveMeshObject?.VertexCount}, state→Beveling");

            _state = BevelState.Beveling;
            // EnterTransformDragging は使用しない。
            // ベベルはトポロジー変更後の位置更新であり TransformDragging モードを使うと
            // エッジ/頂点描画が無効化されるため。SyncMeshPositionsOnly で直接更新する。
        }

        private Vector3 ScreenDeltaToWorldDelta(ToolContext ctx, Vector2 sd)
        {
            if (ctx.ScreenDeltaToWorldDelta != null)
                return ctx.ScreenDeltaToWorldDelta(sd, ctx.CameraPosition, ctx.CameraTarget, ctx.CameraDistance, ctx.PreviewRect);
            float s = ctx.CameraDistance * 0.001f;
            return new Vector3(sd.x * s, -sd.y * s, 0f);
        }

        private void UpdateBevel(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;
            // 放射マグニチュード（向き無関係で常に増加）は非直感なので、初期ドラッグ方向への
            // 符号付き投影に変更。初期方向＝拡大 / 戻す＝縮小 / 開始点で0 / 行き過ぎは0クランプ。
            float signedDist = Vector2.Dot(totalDelta, _startDragDir);
            // dv.OffsetDir はローカル方向。量もローカル空間の長さに揃える。
            // 基準はベベル対象頂点の先頭。スキンド頂点はボーンの SkinningMatrix で
            // 変換されるため、メッシュの WorldMatrixInverse では倍率が合わない。
            int basis = _dragVertices.Count > 0 ? _dragVertices[0].Index : -1;
            Vector3 localDelta = ctx.WorldToLocalVectorAt(basis, ScreenDeltaToWorldDelta(ctx, _startDragDir * signedDist));
            _dragAmount = Mathf.Max(0f, Mathf.Sign(signedDist) * localDelta.magnitude * DragSensitivity);

            var meshObject = ctx.ActiveMeshObject;
            int updated = 0;
            if (meshObject != null)
            {
                foreach (var dv in _dragVertices)
                {
                    if (dv.Index >= 0 && dv.Index < meshObject.VertexCount)
                    {
                        meshObject.Vertices[dv.Index].Position = dv.BasePos + dv.OffsetDir * _dragAmount;
                        updated++;
                    }
                }
            }
            if (updated == 0 || _dragVertices.Count == 0)
                UnityEngine.Debug.Log($"[BevelDBG] UpdateBevel: dragAmount={_dragAmount:F4}, dragVertices={_dragVertices.Count}, updated={updated}, meshNull={meshObject == null}, meshVertCount={meshObject?.VertexCount}");
            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndBevel(ToolContext ctx)
        {
            UnityEngine.Debug.Log($"[BevelDBG] EndBevel: finalAmount={_dragAmount:F4}, dragVertices={_dragVertices.Count}");

            Amount = _dragAmount;

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                MeshObjectSnapshot snapshotAfter = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                MeshSnapshotRecord record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                {
                    string __dbgDesc = "Bevel Edges";
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _snapshotBefore = null;
        }

        /// <summary>
        /// 選択の辺を _dragAmount でベベルする（実処理は EdgeBevelOps.Execute）。
        /// 対象の辺が 1 本も集まらなければメッシュに触れず false を返す。
        /// 実行したら _dragVertices を確定し、選択の辺を消して GPU へ通知する。
        /// </summary>
        private bool ExecuteBevel(ToolContext ctx)
        {
            var meshObject = ctx.ActiveMeshObject;
            var edges = new List<VertexPair>(ctx.SelectionState.Edges);

            var r = EdgeBevelOps.Execute(meshObject, edges, _dragAmount, Segments, Fillet, ctx.CurrentMaterialIndex);
            if (r.CollectedEdgeCount == 0) return false;

            _dragVertices.Clear();
            _dragVertices.AddRange(r.DragVertices);

            UnityEngine.Debug.Log($"[BevelDBG] ExecuteBevel done: edges={r.BeveledEdgeCount}/{r.CollectedEdgeCount}, dragVertices={_dragVertices.Count}, finalVertexCount={meshObject.VertexCount}, finalFaceCount={meshObject.FaceCount}");
            if (_dragVertices.Count > 0)
                UnityEngine.Debug.Log($"[BevelDBG] dragVertices[0]: Index={_dragVertices[0].Index}, BasePos={_dragVertices[0].BasePos}, OffsetDir={_dragVertices[0].OffsetDir}");

            ctx.SelectionState?.Edges.Clear();
            ctx.SyncMesh?.Invoke();
            return true;
        }

        private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }

    }
}
