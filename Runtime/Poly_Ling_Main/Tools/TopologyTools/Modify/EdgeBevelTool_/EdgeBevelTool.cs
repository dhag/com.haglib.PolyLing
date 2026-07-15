// Assets/Editor/Poly_Ling/Tools/Topology/EdgeBevelTool.cs
// エッジベベルツール - IToolSettings対応版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

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

        // ベベル対象
        private List<BevelEdgeInfo> _targetEdges = new List<BevelEdgeInfo>();
        private float _dragAmount;

        // Undo
        private MeshObjectSnapshot _snapshotBefore;

        private struct BevelEdgeInfo
        {
            public int V0, V1;
            public int FaceA, FaceB;
            /// <summary>
            /// FaceB 内での辺端点インデックス。
            /// 共有頂点トポロジーでは V0/V1 と一致。
            /// 非共有頂点（面ごとに独立した頂点）では異なる値になる。
            /// </summary>
            public int FaceB_V0, FaceB_V1;
            public Vector3 EdgeDir;
            public float EdgeLength;
        }

        // ドラッグ中の頂点位置更新用
        private struct DragVertex
        {
            public int     Index;
            public Vector3 BasePos;
            public Vector3 OffsetDir;
        }
        private List<DragVertex> _dragVertices = new List<DragVertex>();

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.CurrentButton != 0)
                return false;

            if (_state != BevelState.Idle)
                return false;

            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
            {
                UnityEngine.Debug.Log($"[BevelDBG] OnMouseDown: MeshObject={ctx.FirstSelectedMeshObject != null}, SelectionState={ctx.SelectionState != null} → skip");
                return false;
            }

            _mouseDownScreenPos = mousePos;
            // _hitEdgeOnMouseDown はハンドラーが PrepareHit() で GPU ホバー結果からセットする

            UnityEngine.Debug.Log($"[BevelDBG] OnMouseDown: pos={mousePos}, hitEdge={_hitEdgeOnMouseDown.HasValue}, vertexCount={ctx.FirstSelectedMeshObject.VertexCount}, faceCount={ctx.FirstSelectedMeshObject.FaceCount}");

            if (_hitEdgeOnMouseDown.HasValue)
            {
                _state = BevelState.PendingAction;
                // マウスダウン時にスナップショット取得
                if (ctx.UndoController != null)
                    _snapshotBefore = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
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
            _targetEdges.Clear();
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

            CollectTargetEdges(ctx);

            UnityEngine.Debug.Log($"[BevelDBG] StartBevel: targetEdges={_targetEdges.Count}, Amount={Amount}, vertexCount={ctx.FirstSelectedMeshObject?.VertexCount}");

            if (_targetEdges.Count == 0)
            {
                _state = BevelState.Idle;
                return;
            }

            _dragAmount = 0f;
            // トポロジーを即時実行し _dragVertices を確定させる
            ExecuteBevel(ctx);  // 内部で ctx.SyncMesh (= NotifyTopologyChanged) を呼ぶ

            UnityEngine.Debug.Log($"[BevelDBG] StartBevel after ExecuteBevel: dragVertices={_dragVertices.Count}, vertexCount={ctx.FirstSelectedMeshObject?.VertexCount}, state→Beveling");

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
            Vector3 worldDelta = ScreenDeltaToWorldDelta(ctx, _startDragDir * signedDist);
            _dragAmount = Mathf.Max(0f, Mathf.Sign(signedDist) * worldDelta.magnitude * DragSensitivity);

            var meshObject = ctx.FirstSelectedMeshObject;
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
                MeshObjectSnapshot snapshotAfter = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                MeshSnapshotRecord record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                {
                    string __dbgDesc = "Bevel Edges";
                    UnityEngine.Debug.Log("[UndoDbg] VertexEdit.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _snapshotBefore = null;
        }

        // ----------------------------------------------------------------
        // ベベル計算の中間データ（Pass1 → Pass2/3 に渡す）
        // ----------------------------------------------------------------
        private struct BevelEdgeData
        {
            public int V0, V1;           // ベベル対象エッジの元頂点インデックス（FaceA側）
            public int FaceB_V0, FaceB_V1; // FaceB 内での対応頂点インデックス（非共有頂点対応）
            public int FaceAIdx;         // 隣接フェース A のインデックス
            public int FaceBIdx;         // 隣接フェース B のインデックス
            public HashSet<int> FaceAOrigVerts; // FaceA の元の頂点集合（方向判定用）
            public HashSet<int> FaceBOrigVerts; // FaceB の元の頂点集合（方向判定用）
            public int[] RowV0;          // V0 側の row 頂点列 [0=FaceA端 .. segments=FaceB端]
            public int[] RowV1;          // V1 側の row 頂点列 [0=FaceA端 .. segments=FaceB端]
            public Vector3 OffsetA;      // FaceA 側インワード方向（row 座標算出用）
            public Vector3 OffsetB;      // FaceB 側インワード方向（row 座標算出用）
            public bool FaceAReverse;    // faceA 内で辺が V0→V1 の並びか（巻き順を隣接面から継承するため）
            public Vector3 P0;           // 元の V0 ワールド座標（ドラッグ更新用）
            public Vector3 P1;           // 元の V1 ワールド座標（ドラッグ更新用）
        }

        private void ExecuteBevel(ToolContext ctx)
        {
            var meshObject = ctx.FirstSelectedMeshObject;
            float amount = _dragAmount;
            int segments = Segments;
            int matIdx = ctx.CurrentMaterialIndex;
            var orphanCandidates = new HashSet<int>();

            // ============================================================
            // Pass 1: 全エッジの row 頂点を生成する（フェースは変更しない）
            //
            // 目的: Pass 2 でフェース走査する前に全エッジの row を確定させる。
            //       逐次処理だと連続線分の共有頂点で「すでに v が置換済み」と
            //       なって後続エッジの処理が破綻するため、計算と適用を分離する。
            //
            // row の構造:
            //   row[0]        : FaceA 側端点  (FaceA の v を置換する頂点)
            //   row[1..n-1]   : 中間頂点群    (Segments >= 2 のときに存在)
            //   row[segments] : FaceB 側端点  (FaceB の v を置換する頂点)
            //
            // フラット (Fillet=false):
            //   offset = Lerp(offsetA*amount, offsetB*amount, t)
            //   → FaceA方向とFaceB方向の間を等分に線形補間
            //
            // フィレット (Fillet=true, segments>=2):
            //   offset = Slerp(offsetA, offsetB, t) * amount
            //   → 方向ベクトルを球面線形補間して弧を描く
            // ============================================================
            var bevelDataList = new List<BevelEdgeData>();

            foreach (var edgeInfo in _targetEdges)
            {
                if (edgeInfo.FaceA < 0 || edgeInfo.FaceB < 0) continue;

                var faceA = meshObject.Faces[edgeInfo.FaceA];
                var faceB = meshObject.Faces[edgeInfo.FaceB];

                Vector3 p0 = meshObject.Vertices[edgeInfo.V0].Position;
                Vector3 p1 = meshObject.Vertices[edgeInfo.V1].Position;

                // FaceA/B それぞれのフェース内側へのオフセット方向
                Vector3 offsetA = GetInwardOffset(meshObject, faceA, edgeInfo.V0, edgeInfo.V1);
                Vector3 offsetB = GetInwardOffset(meshObject, faceB, edgeInfo.V0, edgeInfo.V1);

                // 巻き順は幾何法線ではなく隣接する元面 faceA の辺並び順から継承する（押し出しと同型）。
                // faceA 内で V1 が V0 の直後なら、その辺は V0→V1 の向き。
                bool faceAReverse = false;
                {
                    int i0 = faceA.VertexIndices.IndexOf(edgeInfo.V0);
                    int i1 = faceA.VertexIndices.IndexOf(edgeInfo.V1);
                    if (i0 >= 0 && i1 >= 0)
                        faceAReverse = (i1 == (i0 + 1) % faceA.VertexCount);
                }

                var rowV0 = new int[segments + 1];
                var rowV1 = new int[segments + 1];

                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments; // 0.0 (FaceA側) → 1.0 (FaceB側)

                    Vector3 offset = (Fillet && segments >= 2)
                        ? Vector3.Slerp(offsetA, offsetB, t) * amount   // 弧補間
                        : Vector3.Lerp(offsetA * amount, offsetB * amount, t); // 線形補間

                    rowV0[s] = meshObject.VertexCount;
                    {
                        var nv = new Vertex { Position = p0 + offset };
                        var sv0 = meshObject.Vertices[edgeInfo.V0];
                        if (sv0.UVs.Count > 0) nv.UVs.Add(sv0.UVs[0]);
                        meshObject.Vertices.Add(nv);
                    }
                    rowV1[s] = meshObject.VertexCount;
                    {
                        var nv = new Vertex { Position = p1 + offset };
                        var sv1 = meshObject.Vertices[edgeInfo.V1];
                        if (sv1.UVs.Count > 0) nv.UVs.Add(sv1.UVs[0]);
                        meshObject.Vertices.Add(nv);
                    }
                }

                orphanCandidates.Add(edgeInfo.V0);
                orphanCandidates.Add(edgeInfo.V1);
                // 非共有頂点の場合 FaceB 側の対応頂点も orphan 候補に追加
                if (edgeInfo.FaceB_V0 != edgeInfo.V0) orphanCandidates.Add(edgeInfo.FaceB_V0);
                if (edgeInfo.FaceB_V1 != edgeInfo.V1) orphanCandidates.Add(edgeInfo.FaceB_V1);

                bevelDataList.Add(new BevelEdgeData
                {
                    V0 = edgeInfo.V0,
                    V1 = edgeInfo.V1,
                    FaceB_V0 = edgeInfo.FaceB_V0,
                    FaceB_V1 = edgeInfo.FaceB_V1,
                    FaceAIdx = edgeInfo.FaceA,
                    FaceBIdx = edgeInfo.FaceB,
                    // フェース頂点集合は Pass1 の時点でスナップショット。
                    // Pass2 でフェースを書き換えた後では使えないため事前に保存する。
                    FaceAOrigVerts = new HashSet<int>(faceA.VertexIndices),
                    FaceBOrigVerts = new HashSet<int>(faceB.VertexIndices),
                    RowV0 = rowV0,
                    RowV1 = rowV1,
                    OffsetA = offsetA,
                    OffsetB = offsetB,
                    FaceAReverse = faceAReverse,
                    P0 = p0,
                    P1 = p1,
                });
            }

            // ============================================================
            // Pass 1.5: 連続線分の共有端点をマージする
            //
            // 問題:
            //   e1=(v0,v1), e2=(v1,v2) の連続線分では、共有頂点 v1 に対して
            //   e1 用 rowV1[0..n] と e2 用 rowV0[0..n] が Pass1 で別々の頂点として
            //   生成される。共有フェース側の端点は同一頂点であるべきなので
            //   ここでマージする。
            //
            // アルゴリズム:
            //   1. 原頂点 → 接続エッジリスト (edgeIdx, isV0) のマップを構築
            //   2. 2辺以上が接続する頂点について各エッジペアを処理:
            //      a. 両エッジが共有するフェースを特定
            //         (e1.FaceAIdx == e2.FaceAIdx / FaceAIdx == FaceBIdx / ... の4通り)
            //      b. 共有フェース側の row インデックスを決定
            //         faceA 側 → row[0], faceB 側 → row[segments]
            //      c. e2 の共有フェース端点を e1 の値で上書き
            //         e2 の旧頂点は使われなくなるので orphanCandidates に追加
            // ============================================================

            // 原頂点 → 接続エッジリスト を構築
            // isV0=true のとき対象エッジの V0 側、false のとき V1 側
            var vertexToEdges = new Dictionary<int, List<(int edgeIdx, bool isV0)>>();
            for (int ei = 0; ei < bevelDataList.Count; ei++)
            {
                var bd = bevelDataList[ei];
                if (!vertexToEdges.ContainsKey(bd.V0)) vertexToEdges[bd.V0] = new List<(int, bool)>();
                if (!vertexToEdges.ContainsKey(bd.V1)) vertexToEdges[bd.V1] = new List<(int, bool)>();
                vertexToEdges[bd.V0].Add((ei, true));
                vertexToEdges[bd.V1].Add((ei, false));
            }

            foreach (var kv in vertexToEdges)
            {
                var connections = kv.Value;
                if (connections.Count < 2) continue; // 孤立エッジ → マージ不要

                // 接続エッジの全ペアを処理
                for (int a = 0; a < connections.Count; a++)
                for (int b = a + 1; b < connections.Count; b++)
                {
                    int eiA = connections[a].edgeIdx;
                    bool isV0A = connections[a].isV0;
                    int eiB = connections[b].edgeIdx;
                    bool isV0B = connections[b].isV0;

                    var bdA = bevelDataList[eiA];
                    var bdB = bevelDataList[eiB];

                    // 両エッジが共有するフェースインデックスを探す
                    // (4通りの組み合わせを確認)
                    int sharedFaceIdx = -1;
                    bool sharedIsFaceAofA = false; // bdA 側で sharedFace が FaceA かどうか
                    bool sharedIsFaceAofB = false; // bdB 側で sharedFace が FaceA かどうか

                    if      (bdA.FaceAIdx == bdB.FaceAIdx) { sharedFaceIdx = bdA.FaceAIdx; sharedIsFaceAofA = true;  sharedIsFaceAofB = true;  }
                    else if (bdA.FaceAIdx == bdB.FaceBIdx) { sharedFaceIdx = bdA.FaceAIdx; sharedIsFaceAofA = true;  sharedIsFaceAofB = false; }
                    else if (bdA.FaceBIdx == bdB.FaceAIdx) { sharedFaceIdx = bdA.FaceBIdx; sharedIsFaceAofA = false; sharedIsFaceAofB = true;  }
                    else if (bdA.FaceBIdx == bdB.FaceBIdx) { sharedFaceIdx = bdA.FaceBIdx; sharedIsFaceAofA = false; sharedIsFaceAofB = false; }

                    if (sharedFaceIdx < 0) continue; // 共有フェースなし → 連続していない

                    // 共有フェース側の row インデックス:
                    //   FaceA側 → row[0], FaceB側 → row[segments]
                    int rowIdxA = sharedIsFaceAofA ? 0 : segments;
                    int rowIdxB = sharedIsFaceAofB ? 0 : segments;

                    // bdA/bdB の row 配列は int[] (参照型) なので直接書き換え可
                    int[] rowA = isV0A ? bdA.RowV0 : bdA.RowV1;
                    int[] rowB = isV0B ? bdB.RowV0 : bdB.RowV1;

                    // bdB の端点を bdA の値に統一し、旧頂点を孤立候補に追加
                    int oldVertex = rowB[rowIdxB];
                    if (oldVertex != rowA[rowIdxA])
                    {
                        orphanCandidates.Add(oldVertex);
                        rowB[rowIdxB] = rowA[rowIdxA];
                    }
                }
            }

            // Pass 1.5 終了後: bevelDataList の RowV0/RowV1 はマージ済み最終値。
            // ドラッグ更新用に (rawIndex, basePos, offsetDir) を収集する。
            // orphanCandidates 除去によるインデックスシフトは後で調整する。
            bool useFillet = Fillet && segments >= 2;
            var rawDragVerts = new List<(int rawIdx, Vector3 basePos, Vector3 offsetDir)>();
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float t = segments > 0 ? (float)s / segments : 0f;
                    Vector3 dir = useFillet
                        ? Vector3.Slerp(bd.OffsetA, bd.OffsetB, t)
                        : Vector3.Lerp(bd.OffsetA, bd.OffsetB, t);
                    rawDragVerts.Add((bd.RowV0[s], bd.P0, dir));
                    rawDragVerts.Add((bd.RowV1[s], bd.P1, dir));
                }
            }

            // ============================================================
            // Pass 2: ベベル前の全フェースを走査し、頂点置換を一括適用する
            //
            // 各フェースの各頂点ポジション(pos)について、置換計画を収集してから
            // 後ろの pos 順（降順）に適用する。
            // 降順適用の理由: InsertRange で頂点数が増えると前方の pos がずれるが、
            //                 後ろから処理すれば前方の pos に影響しない。
            //
            // フェースとエッジの関係に応じて3パターンの置換を行う:
            //
            //   [A] faceA に該当  → v を row[0] に単一置換
            //                       (faceA 側端点で差し替え)
            //
            //   [B] faceB に該当  → v を row[segments] に単一置換
            //                       (faceB 側端点で差し替え)
            //
            //   [C] 側面フェース  → v を row 全体のシーケンスで展開
            //                       (N角形 → N+segments 角形)
            //                       ただし、同じ pos に [A][B] が既に登録されている場合は
            //                       [A][B] を優先し [C] は無視する。
            //                       (連続線分の共有頂点で複数エッジが競合する場合の対処)
            //
            // [C] の挿入方向の決め方:
            //   - v の prev 隣接頂点が FaceAOrigVerts に含まれる
            //     → prev 側が faceA 側 → row[0]→row[segments] 順で挿入
            //   - v の prev 隣接頂点が FaceBOrigVerts に含まれる
            //     → prev 側が faceB 側 → row[segments]→row[0] 順（逆順）で挿入
            //   - どちらでもない（連続線分の中間フェース等）
            //     → 中点インデックス row[segments/2] で単一置換（近似）
            // ============================================================

            // ベベルフェース追加前のフェース数（Pass3 で追加するフェースは走査しない）
            int faceCountBeforeBevel = meshObject.FaceCount;

            for (int fi = 0; fi < faceCountBeforeBevel; fi++)
            {
                var face = meshObject.Faces[fi];

                // このフェースの頂点置換計画: key=pos(降順), value=置換シーケンス
                // 単一置換 → length=1 の配列、シーケンス展開 → length=segments+1 の配列
                var replacements = new SortedDictionary<int, int[]>(
                    Comparer<int>.Create((a, b) => b - a)); // 降順ソート

                foreach (var bd in bevelDataList)
                {
                    // このエッジの両端点 (V0, V1) それぞれについて処理
                    for (int vi = 0; vi < 2; vi++)
                    {
                        // FaceB の場合は FaceB_V0/FaceB_V1 を使う（非共有頂点対応）
                        int v;
                        if (fi == bd.FaceBIdx)
                            v = (vi == 0) ? bd.FaceB_V0 : bd.FaceB_V1;
                        else
                            v = (vi == 0) ? bd.V0 : bd.V1;

                        int[] row = (vi == 0) ? bd.RowV0 : bd.RowV1;

                        // v がこのフェースに含まれていなければスキップ
                        int pos = face.VertexIndices.IndexOf(v);
                        if (pos < 0) continue;

                        if (fi == bd.FaceAIdx)
                        {
                            // [A] faceA: row[0] (FaceA側端点) で単一置換
                            // faceA/faceB 扱いは側面より優先 → 上書きで登録
                            replacements[pos] = new[] { row[0] };
                        }
                        else if (fi == bd.FaceBIdx)
                        {
                            // [B] faceB: row[segments] (FaceB側端点) で単一置換
                            replacements[pos] = new[] { row[segments] };
                        }
                        else
                        {
                            // [C] 側面フェース
                            // 同じ pos に既に [A] or [B] が登録済みなら無視（優先度低）
                            if (replacements.ContainsKey(pos)) continue;

                            int count = face.VertexIndices.Count;
                            int prev = face.VertexIndices[(pos - 1 + count) % count];

                            if (bd.FaceAOrigVerts.Contains(prev))
                            {
                                // prev が FaceA 側 → row[0]..row[segments] 順で展開
                                replacements[pos] = row;
                            }
                            else if (bd.FaceBOrigVerts.Contains(prev))
                            {
                                // prev が FaceB 側 → 逆順で展開
                                replacements[pos] = BevelReversed(row);
                            }
                            else
                            {
                                // 連続線分の中間フェース等: 隣接情報が不明 → 中点で近似
                                replacements[pos] = new[] { row[row.Length / 2] };
                            }
                        }
                    }
                }

                // 降順の pos で適用（後ろから処理することで前方インデックスがずれない）
                foreach (var kv in replacements)
                {
                    int pos = kv.Key;
                    int[] seq = kv.Value;

                    face.VertexIndices.RemoveAt(pos);
                    face.VertexIndices.InsertRange(pos, seq);

                    if (pos < face.UVIndices.Count)
                    {
                        face.UVIndices.RemoveAt(pos);
                        face.UVIndices.InsertRange(pos, seq);
                    }
                    if (pos < face.NormalIndices.Count)
                    {
                        face.NormalIndices.RemoveAt(pos);
                        face.NormalIndices.InsertRange(pos, seq);
                    }
                }
            }

            // ============================================================
            // Pass 3: 各エッジの row 間を埋める四角フェースを追加する
            //
            // 隣接する row[s] と row[s+1] の間に四角フェースを生成する。
            //
            // 巻き順の決め方:
            //   幾何法線ではなく、隣接する元面 faceA の辺 (V0,V1) の並び順から継承する
            //   （押し出しツールと同型）。これによりメッシュの巻き規約（PMX/MQO）に依存せず
            //   常に隣接面と一貫した向きになる。FaceAReverse は Pass1 で faceA から算出済み。
            // ============================================================
            foreach (var bd in bevelDataList)
            {
                for (int s = 0; s < segments; s++)
                {
                    // faceA の辺が V0→V1（FaceAReverse）なら押し出しの reverseWinding と同型に並べる。
                    int[] verts = bd.FaceAReverse
                        ? new[] { bd.RowV0[s], bd.RowV0[s + 1], bd.RowV1[s + 1], bd.RowV1[s] }
                        : new[] { bd.RowV0[s], bd.RowV1[s], bd.RowV1[s + 1], bd.RowV0[s + 1] };

                    var bevelFace = new Face { MaterialIndex = matIdx };
                    bevelFace.VertexIndices.AddRange(verts);
                    bevelFace.UVIndices.AddRange(new[] { 0, 0, 0, 0 });
                    bevelFace.NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
                    meshObject.Faces.Add(bevelFace);
                }
            }

            var removedAsc = RemoveOrphanVertices(meshObject, orphanCandidates);

            // _dragVertices を確定: rawIndex をシフト補正し、重複除外
            _dragVertices.Clear();
            var seenIdx = new HashSet<int>();
            foreach (var (rawIdx, basePos, offsetDir) in rawDragVerts)
            {
                int bsResult = removedAsc.BinarySearch(rawIdx);
                if (bsResult >= 0) continue; // このrowがorphan除去された場合はスキップ
                int shift    = ~bsResult;    // rawIdx より小さい除去済みインデックスの数
                int adjusted = rawIdx - shift;
                if (seenIdx.Add(adjusted))
                    _dragVertices.Add(new DragVertex { Index = adjusted, BasePos = basePos, OffsetDir = offsetDir });
            }

            UnityEngine.Debug.Log($"[BevelDBG] ExecuteBevel done: rawDragVerts={rawDragVerts.Count}, removed={removedAsc.Count}, dragVertices={_dragVertices.Count}, finalVertexCount={meshObject.VertexCount}, finalFaceCount={meshObject.FaceCount}");
            if (_dragVertices.Count > 0)
                UnityEngine.Debug.Log($"[BevelDBG] dragVertices[0]: Index={_dragVertices[0].Index}, BasePos={_dragVertices[0].BasePos}, OffsetDir={_dragVertices[0].OffsetDir}");

            ctx.SelectionState?.Edges.Clear();
            ctx.SyncMesh?.Invoke();
        }

        private static int[] BevelReversed(int[] src)
        {
            var r = new int[src.Length];
            for (int i = 0; i < src.Length; i++) r[i] = src[src.Length - 1 - i];
            return r;
        }

        private void ReplaceFaceVertex(Face face, int oldIdx, int newIdx)
        {
            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                if (face.VertexIndices[i] == oldIdx)
                    face.VertexIndices[i] = newIdx;
            }
            for (int i = 0; i < face.UVIndices.Count; i++)
            {
                if (face.UVIndices[i] == oldIdx)
                    face.UVIndices[i] = newIdx;
            }
            for (int i = 0; i < face.NormalIndices.Count; i++)
            {
                if (face.NormalIndices[i] == oldIdx)
                    face.NormalIndices[i] = newIdx;
            }
        }

        private List<int> RemoveOrphanVertices(MeshObject meshObject, HashSet<int> candidates)
        {
            var usedVertices = new HashSet<int>();
            foreach (var face in meshObject.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    usedVertices.Add(vi);
            }

            var toRemove = candidates.Where(v => !usedVertices.Contains(v) && v >= 0 && v < meshObject.VertexCount)
                                     .OrderByDescending(v => v)
                                     .ToList();

            foreach (int vertexIdx in toRemove)
            {
                meshObject.Vertices.RemoveAt(vertexIdx);

                foreach (var face in meshObject.Faces)
                {
                    for (int i = 0; i < face.VertexIndices.Count; i++)
                    {
                        if (face.VertexIndices[i] > vertexIdx)
                            face.VertexIndices[i]--;
                    }
                    for (int i = 0; i < face.UVIndices.Count; i++)
                    {
                        if (face.UVIndices[i] > vertexIdx)
                            face.UVIndices[i]--;
                    }
                    for (int i = 0; i < face.NormalIndices.Count; i++)
                    {
                        if (face.NormalIndices[i] > vertexIdx)
                            face.NormalIndices[i]--;
                    }
                }
            }

            // 呼び出し元がインデックスシフト補正に使えるよう昇順で返す
            toRemove.Sort();
            return toRemove;
        }

        private Vector3 GetInwardOffset(MeshObject meshObject, Face face, int v0, int v1)
        {
            Vector3 faceNormal = CalculateFaceNormal(meshObject, face);
            Vector3 p0 = meshObject.Vertices[v0].Position;
            Vector3 p1 = meshObject.Vertices[v1].Position;
            Vector3 edgeDir = (p1 - p0).normalized;
            Vector3 inward = Vector3.Cross(faceNormal, edgeDir).normalized;

            Vector3 faceCenter = CalculateFaceCenter(meshObject, face);
            Vector3 edgeCenter = (p0 + p1) * 0.5f;
            Vector3 toCenter = (faceCenter - edgeCenter).normalized;

            if (Vector3.Dot(inward, toCenter) < 0)
                inward = -inward;

            return inward;
        }

        private Vector3 CalculateFaceNormal(MeshObject meshObject, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;

            Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;

            return NormalHelper.CalculateFaceNormal(p0, p1, p2);
        }

        private Vector3 CalculateFaceCenter(MeshObject meshObject, Face face)
        {
            Vector3 center = Vector3.zero;
            foreach (int vi in face.VertexIndices)
                center += meshObject.Vertices[vi].Position;
            return center / face.VertexCount;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void CollectTargetEdges(ToolContext ctx)
        {
            _targetEdges.Clear();

            foreach (var edgePair in ctx.SelectionState.Edges)
            {
                int v0 = edgePair.V1;
                int v1 = edgePair.V2;

                if (v0 < 0 || v0 >= ctx.FirstSelectedMeshObject.VertexCount) continue;
                if (v1 < 0 || v1 >= ctx.FirstSelectedMeshObject.VertexCount) continue;

                Vector3 p0 = ctx.FirstSelectedMeshObject.Vertices[v0].Position;
                Vector3 p1 = ctx.FirstSelectedMeshObject.Vertices[v1].Position;

                FindAdjacentFacesWithEdgeVertices(ctx.FirstSelectedMeshObject, v0, v1, p0, p1,
                    out int faceA, out int faceB, out int faceB_V0, out int faceB_V1);

                var info = new BevelEdgeInfo
                {
                    V0 = v0,
                    V1 = v1,
                    FaceA = faceA,
                    FaceB = faceB,
                    FaceB_V0 = faceB_V0,
                    FaceB_V1 = faceB_V1,
                    EdgeDir = (p1 - p0).normalized,
                    EdgeLength = Vector3.Distance(p0, p1)
                };

                _targetEdges.Add(info);
            }
        }

        /// <summary>
        /// edgeの両端(v0,v1)を含む隣接面を最大2つ探す。
        /// まず頂点インデックスで検索し、見つからない場合は位置ベースで探索する（非共有頂点対応）。
        /// faceB_V0/faceB_V1 は FaceB 内での対応頂点インデックス（共有時は v0/v1 と同値）。
        /// </summary>
        private void FindAdjacentFacesWithEdgeVertices(
            MeshObject meshObject, int v0, int v1, Vector3 p0, Vector3 p1,
            out int faceA, out int faceB, out int faceB_V0, out int faceB_V1)
        {
            faceA = -1; faceB = -1;
            faceB_V0 = v0; faceB_V1 = v1; // デフォルト: 共有頂点と同値

            const float eps = 1e-4f;

            for (int i = 0; i < meshObject.FaceCount; i++)
            {
                var face = meshObject.Faces[i];
                if (face.VertexCount < 3) continue;

                // ── 頂点インデックスによる一致（共有頂点） ──
                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                {
                    if (faceA < 0) faceA = i;
                    else if (faceB < 0) { faceB = i; faceB_V0 = v0; faceB_V1 = v1; break; }
                    continue;
                }

                // ── 位置による一致（非共有頂点フォールバック） ──
                int matchV0 = -1, matchV1 = -1;
                foreach (int vi in face.VertexIndices)
                {
                    if (vi < 0 || vi >= meshObject.VertexCount) continue;
                    Vector3 vp = meshObject.Vertices[vi].Position;
                    if (matchV0 < 0 && (vp - p0).sqrMagnitude < eps) matchV0 = vi;
                    if (matchV1 < 0 && (vp - p1).sqrMagnitude < eps) matchV1 = vi;
                    if (matchV0 >= 0 && matchV1 >= 0) break;
                }
                if (matchV0 < 0 || matchV1 < 0) continue;

                if (faceA < 0) faceA = i;
                else if (faceB < 0)
                {
                    faceB = i;
                    faceB_V0 = matchV0;
                    faceB_V1 = matchV1;
                    break;
                }
            }
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
