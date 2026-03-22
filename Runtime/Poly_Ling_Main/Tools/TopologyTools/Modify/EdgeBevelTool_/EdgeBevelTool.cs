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
        private float Amount
        {
            get => _settings.Amount;
            set => _settings.Amount = value;
        }

        private int Segments
        {
            get => _settings.Segments;
            set => _settings.Segments = value;
        }

        private bool Fillet
        {
            get => _settings.Fillet;
            set => _settings.Fillet = value;
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
            public Vector3 EdgeDir;
            public float EdgeLength;
        }

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
                return false;

            _mouseDownScreenPos = mousePos;
            _hitEdgeOnMouseDown = FindEdgeAtPosition(ctx, mousePos);

            if (_hitEdgeOnMouseDown.HasValue)
            {
                _state = BevelState.PendingAction;
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
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitEdgeOnMouseDown.HasValue)
                            StartBevel(ctx);
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

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null) return;

            if (_state == BevelState.Idle || _state == BevelState.PendingAction)
            {
                Vector2 mousePos = ctx.CurrentMousePosition;
                _hoverEdge = FindEdgeAtPosition(ctx, mousePos);
            }
            else
            {
                _hoverEdge = null;
            }

            UnityEditor_Handles.BeginGUI();

            if (_state == BevelState.Beveling)
            {
                UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 1f);
                foreach (var edge in _targetEdges)
                {
                    if (edge.V0 < 0 || edge.V0 >= ctx.FirstSelectedMeshObject.VertexCount) continue;
                    if (edge.V1 < 0 || edge.V1 >= ctx.FirstSelectedMeshObject.VertexCount) continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[edge.V0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[edge.V1].Position);
                    DrawThickLine(p0, p1, 4f);
                }

                DrawBevelPreview(ctx);

                GUI.color = Color.white;
                GUI.Label(new Rect(10, 60, 200, 20), $"Amount: {_dragAmount:F3}");
                GUI.Label(new Rect(10, 80, 200, 20), $"Segments: {Segments}");
            }
            else
            {
                if (_hoverEdge.HasValue)
                {
                    int v0 = _hoverEdge.Value.V1, v1 = _hoverEdge.Value.V2;
                    if (v0 >= 0 && v0 < ctx.FirstSelectedMeshObject.VertexCount &&
                        v1 >= 0 && v1 < ctx.FirstSelectedMeshObject.VertexCount)
                    {
                        UnityEditor_Handles.color = Color.white;
                        Vector2 p0 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v0].Position);
                        Vector2 p1 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v1].Position);
                        DrawThickLine(p0, p1, 5f);
                    }
                }
            }

            UnityEditor_Handles.color = Color.white;
            UnityEditor_Handles.EndGUI();
        }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_state == BevelState.Beveling)
                ctx.ExitTransformDragging?.Invoke();
            Reset();
        }

        public void Reset()
        {
            _state = BevelState.Idle;
            _hitEdgeOnMouseDown = null;
            _targetEdges.Clear();
            _snapshotBefore = null;
            _dragAmount = 0f;
        }

        public void OnSelectionChanged(ToolContext ctx) { }

        // ================================================================
        // ベベル処理
        // ================================================================

        private void StartBevel(ToolContext ctx)
        {
            if (_hitEdgeOnMouseDown.HasValue && !ctx.SelectionState.Edges.Contains(_hitEdgeOnMouseDown.Value))
            {
                ctx.SelectionState.Edges.Clear();
                ctx.SelectionState.Edges.Add(_hitEdgeOnMouseDown.Value);
            }

            CollectTargetEdges(ctx);

            if (_targetEdges.Count == 0)
            {
                _state = BevelState.Idle;
                return;
            }

            if (ctx.UndoController != null)
            {
                _snapshotBefore = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
            }

            _dragAmount = Amount;
            _state = BevelState.Beveling;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void UpdateBevel(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;
            _dragAmount = Mathf.Max(0.001f, Amount + totalDelta.x * 0.002f);
        }

        private void EndBevel(ToolContext ctx)
        {
            ctx.ExitTransformDragging?.Invoke();

            if (_dragAmount < 0.001f)
            {
                _snapshotBefore = null;
                return;
            }

            Amount = _dragAmount;
            ExecuteBevel(ctx);

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                MeshObjectSnapshot snapshotAfter = MeshObjectSnapshot.Capture(ctx.FirstSelectedMeshContext, ctx.UndoController.MeshUndoContext, ctx.SelectionState);
                MeshSnapshotRecord record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter, ctx.SelectionState);
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, "Bevel Edges");
            }

            _snapshotBefore = null;
        }

        // ----------------------------------------------------------------
        // ベベル計算の中間データ（Pass1 → Pass2/3 に渡す）
        // ----------------------------------------------------------------
        private struct BevelEdgeData
        {
            public int V0, V1;           // ベベル対象エッジの元頂点インデックス
            public int FaceAIdx;         // 隣接フェース A のインデックス
            public int FaceBIdx;         // 隣接フェース B のインデックス
            public HashSet<int> FaceAOrigVerts; // FaceA の元の頂点集合（方向判定用）
            public HashSet<int> FaceBOrigVerts; // FaceB の元の頂点集合（方向判定用）
            public int[] RowV0;          // V0 側の row 頂点列 [0=FaceA端 .. segments=FaceB端]
            public int[] RowV1;          // V1 側の row 頂点列 [0=FaceA端 .. segments=FaceB端]
            public Vector3 OffsetA;      // FaceA 側インワード方向（ベベル面の法線向き判定用）
            public Vector3 OffsetB;      // FaceB 側インワード方向（ベベル面の法線向き判定用）
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

                var rowV0 = new int[segments + 1];
                var rowV1 = new int[segments + 1];

                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments; // 0.0 (FaceA側) → 1.0 (FaceB側)

                    Vector3 offset = (Fillet && segments >= 2)
                        ? Vector3.Slerp(offsetA, offsetB, t) * amount   // 弧補間
                        : Vector3.Lerp(offsetA * amount, offsetB * amount, t); // 線形補間

                    rowV0[s] = meshObject.VertexCount;
                    meshObject.Vertices.Add(new Vertex { Position = p0 + offset });
                    rowV1[s] = meshObject.VertexCount;
                    meshObject.Vertices.Add(new Vertex { Position = p1 + offset });
                }

                orphanCandidates.Add(edgeInfo.V0);
                orphanCandidates.Add(edgeInfo.V1);

                bevelDataList.Add(new BevelEdgeData
                {
                    V0 = edgeInfo.V0,
                    V1 = edgeInfo.V1,
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
                        int v   = (vi == 0) ? bd.V0 : bd.V1;
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
            // 法線方向の決め方:
            //   ベベル面の期待アウトワード = -(offsetA + offsetB).normalized
            //   offsetA/offsetB は両フェースのインワード方向なので、
            //   その和の逆向きがベベル面の「表側」になる。
            //
            //   候補頂点順 [rowV0[s], rowV1[s], rowV1[s+1], rowV0[s+1]] で
            //   Unity左手系の面法線 = Cross(rowV1[s]-rowV0[s], rowV0[s+1]-rowV0[s]) を計算し、
            //   期待アウトワードとのドット積が負なら頂点順を逆にする。
            // ============================================================
            foreach (var bd in bevelDataList)
            {
                // ベベル面の期待アウトワード方向
                // offsetA/offsetB はインワード（フェース内側向き）なので逆符号
                Vector3 expectedOutward = -(bd.OffsetA + bd.OffsetB).normalized;

                for (int s = 0; s < segments; s++)
                {
                    // 候補頂点座標を取得して法線を計算
                    Vector3 A = meshObject.Vertices[bd.RowV0[s    ]].Position;
                    Vector3 B = meshObject.Vertices[bd.RowV1[s    ]].Position;
                    Vector3 C = meshObject.Vertices[bd.RowV1[s + 1]].Position;
                    Vector3 D = meshObject.Vertices[bd.RowV0[s + 1]].Position;

                    // Unity 左手系: CCW 巻きの法線 = Cross(B-A, D-A)
                    Vector3 candidateNormal = Vector3.Cross(B - A, D - A);

                    // 期待アウトワードと逆向きなら頂点順を逆にする
                    int[] verts = Vector3.Dot(candidateNormal, expectedOutward) >= 0
                        ? new[] { bd.RowV0[s], bd.RowV1[s], bd.RowV1[s + 1], bd.RowV0[s + 1] }
                        : new[] { bd.RowV0[s + 1], bd.RowV1[s + 1], bd.RowV1[s], bd.RowV0[s] };

                    var bevelFace = new Face { MaterialIndex = matIdx };
                    bevelFace.VertexIndices.AddRange(verts);
                    bevelFace.UVIndices.AddRange(verts);
                    bevelFace.NormalIndices.AddRange(verts);
                    meshObject.Faces.Add(bevelFace);
                }
            }

            RemoveOrphanVertices(meshObject, orphanCandidates);
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

        private void RemoveOrphanVertices(MeshObject meshObject, HashSet<int> candidates)
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

                var adjacentFaces = FindAdjacentFaces(ctx.FirstSelectedMeshObject, v0, v1);

                Vector3 p0 = ctx.FirstSelectedMeshObject.Vertices[v0].Position;
                Vector3 p1 = ctx.FirstSelectedMeshObject.Vertices[v1].Position;

                var info = new BevelEdgeInfo
                {
                    V0 = v0,
                    V1 = v1,
                    FaceA = adjacentFaces.Count > 0 ? adjacentFaces[0] : -1,
                    FaceB = adjacentFaces.Count > 1 ? adjacentFaces[1] : -1,
                    EdgeDir = (p1 - p0).normalized,
                    EdgeLength = Vector3.Distance(p0, p1)
                };

                _targetEdges.Add(info);
            }
        }

        private List<int> FindAdjacentFaces(MeshObject meshObject, int v0, int v1)
        {
            var result = new List<int>();

            for (int i = 0; i < meshObject.FaceCount; i++)
            {
                var face = meshObject.Faces[i];
                if (face.VertexCount < 3) continue;

                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                    result.Add(i);
            }

            return result;
        }

        private VertexPair? FindEdgeAtPosition(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null) return null;

            const float threshold = 8f;

            for (int fi = 0; fi < ctx.FirstSelectedMeshObject.FaceCount; fi++)
            {
                var face = ctx.FirstSelectedMeshObject.Faces[fi];
                if (face.VertexCount < 3) continue;

                for (int i = 0; i < face.VertexCount; i++)
                {
                    int v0 = face.VertexIndices[i];
                    int v1 = face.VertexIndices[(i + 1) % face.VertexCount];
                    if (v0 < 0 || v1 < 0 || v0 >= ctx.FirstSelectedMeshObject.VertexCount || v1 >= ctx.FirstSelectedMeshObject.VertexCount)
                        continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v1].Position);

                    if (DistancePointToSegment(mousePos, p0, p1) < threshold)
                        return new VertexPair(v0, v1);
                }
            }

            return null;
        }

        private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }

        private void DrawBevelPreview(ToolContext ctx)
        {
            UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);

            foreach (var edge in _targetEdges)
            {
                if (edge.FaceA < 0 || edge.FaceB < 0) continue;

                Vector3 p0 = ctx.FirstSelectedMeshObject.Vertices[edge.V0].Position;
                Vector3 p1 = ctx.FirstSelectedMeshObject.Vertices[edge.V1].Position;

                var faceA = ctx.FirstSelectedMeshObject.Faces[edge.FaceA];
                var faceB = ctx.FirstSelectedMeshObject.Faces[edge.FaceB];

                Vector3 offsetA = GetInwardOffset(ctx.FirstSelectedMeshObject, faceA, edge.V0, edge.V1);
                Vector3 offsetB = GetInwardOffset(ctx.FirstSelectedMeshObject, faceB, edge.V0, edge.V1);

                Vector3 newA0 = p0 + offsetA * _dragAmount;
                Vector3 newA1 = p1 + offsetA * _dragAmount;
                Vector2 sA0 = ctx.WorldToScreen(newA0);
                Vector2 sA1 = ctx.WorldToScreen(newA1);
                DrawThickLine(sA0, sA1, 2f);

                Vector3 newB0 = p0 + offsetB * _dragAmount;
                Vector3 newB1 = p1 + offsetB * _dragAmount;
                Vector2 sB0 = ctx.WorldToScreen(newB0);
                Vector2 sB1 = ctx.WorldToScreen(newB1);
                DrawThickLine(sB0, sB1, 2f);

                UnityEditor_Handles.color = new Color(1f, 0.6f, 0.2f, 0.6f);
                DrawThickLine(sA0, sB0, 1f);
                DrawThickLine(sA1, sB1, 1f);
                UnityEditor_Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            }
        }

        private void DrawThickLine(Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 dir = (p1 - p0);
            if (dir.magnitude < 0.001f) return;
            dir.Normalize();

            Vector2 perp = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
            UnityEditor_Handles.DrawAAConvexPolygon(p0 - perp, p0 + perp, p1 + perp, p1 - perp);
        }
    }
}
