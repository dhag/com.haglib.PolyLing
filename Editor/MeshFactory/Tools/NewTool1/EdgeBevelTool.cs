// Tools/EdgeBevelTool.cs
// エッジベベルツール - 正しいアルゴリズム

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    public class EdgeBevelTool : IEditTool
    {
        public string Name => "Bevel";

        // === 状態 ===
        private enum BevelState { Idle, PendingAction, Beveling }
        private BevelState _state = BevelState.Idle;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private VertexPair? _hitEdgeOnMouseDown;
        private const float DragThreshold = 4f;

        // ホバー
        private VertexPair? _hoverEdge;

        // 設定
        private float _amount = 0.1f;
        private int _segments = 1;
        private bool _fillet = true;  // true = 丸め（円弧）、false = 直線

        // ベベル対象
        private List<BevelEdgeInfo> _targetEdges = new List<BevelEdgeInfo>();
        private float _dragAmount;

        // Undo
        private MeshDataSnapshot _snapshotBefore;

        private struct BevelEdgeInfo
        {
            public int V0, V1;           // 元エッジの頂点
            public int FaceA, FaceB;     // 隣接する2面（-1 = 境界エッジ）
            public Vector3 EdgeDir;      // エッジ方向（V0→V1）
            public float EdgeLength;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (Event.current.button != 0)
                return false;

            if (_state != BevelState.Idle)
                return false;

            if (ctx.MeshData == null || ctx.SelectionState == null)
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
            if (ctx.MeshData == null || ctx.SelectionState == null) return;

            if (_state == BevelState.Idle || _state == BevelState.PendingAction)
            {
                Vector2 mousePos = Event.current.mousePosition;
                _hoverEdge = FindEdgeAtPosition(ctx, mousePos);
            }
            else
            {
                _hoverEdge = null;
            }

            Handles.BeginGUI();

            if (_state == BevelState.Beveling)
            {
                // ベベル中 - オレンジ
                Handles.color = new Color(1f, 0.5f, 0f, 1f);
                foreach (var edge in _targetEdges)
                {
                    if (edge.V0 < 0 || edge.V0 >= ctx.MeshData.VertexCount) continue;
                    if (edge.V1 < 0 || edge.V1 >= ctx.MeshData.VertexCount) continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                    DrawThickLine(p0, p1, 4f);
                }

                // プレビュー描画
                DrawBevelPreview(ctx);

                GUI.color = Color.white;
                GUI.Label(new Rect(10, 60, 200, 20), $"Amount: {_dragAmount:F3}");
                GUI.Label(new Rect(10, 80, 200, 20), $"Segments: {_segments}");
            }
            else
            {
                // ホバー中 - 白
                if (_hoverEdge.HasValue)
                {
                    int v0 = _hoverEdge.Value.V1, v1 = _hoverEdge.Value.V2;
                    if (v0 >= 0 && v0 < ctx.MeshData.VertexCount &&
                        v1 >= 0 && v1 < ctx.MeshData.VertexCount)
                    {
                        Handles.color = Color.white;
                        Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                        Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                        DrawThickLine(p0, p1, 5f);
                    }
                }
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Edge Bevel Tool", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Drag edge to bevel.\n" +
                "Creates new face between adjacent faces.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Amount", GUILayout.Width(60));
            _amount = EditorGUILayout.FloatField(_amount, GUILayout.Width(60));
            _amount = Mathf.Max(0.001f, _amount);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.05", EditorStyles.miniButtonLeft)) _amount = 0.05f;
            if (GUILayout.Button("0.1", EditorStyles.miniButtonMid)) _amount = 0.1f;
            if (GUILayout.Button("0.2", EditorStyles.miniButtonRight)) _amount = 0.2f;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Segments", GUILayout.Width(70));
            _segments = EditorGUILayout.IntSlider(_segments, 1, 10);
            EditorGUILayout.EndHorizontal();

            if (_segments >= 2)
            {
                EditorGUILayout.Space(3);
                _fillet = EditorGUILayout.Toggle("Fillet (Round)", _fillet);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            if (ctx.SelectionState != null)
                ctx.SelectionState.Mode |= MeshSelectMode.Edge;
        }

        public void OnDeactivate(ToolContext ctx) => Reset();

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
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                _snapshotBefore = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            }

            _dragAmount = _amount;
            _state = BevelState.Beveling;
        }

        private void UpdateBevel(ToolContext ctx, Vector2 mousePos)
        {
            Vector2 totalDelta = mousePos - _mouseDownScreenPos;
            _dragAmount = Mathf.Max(0.001f, _amount + totalDelta.x * 0.002f);
        }

        private void EndBevel(ToolContext ctx)
        {
            if (_dragAmount < 0.001f)
            {
                _snapshotBefore = null;
                return;
            }

            _amount = _dragAmount;
            ExecuteBevel(ctx);

            if (ctx.UndoController != null && _snapshotBefore != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
                var snapshotAfter = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                var record = new MeshSnapshotRecord(_snapshotBefore, snapshotAfter);
                ctx.UndoController.VertexEditStack.Record(record, "Bevel Edges");
            }

            _snapshotBefore = null;
        }

        /// <summary>
        /// ベベル処理を実行する
        /// 
        /// 【アルゴリズム概要】
        /// ベベルは漢字の「日」→「目」変換に例えられる：
        /// - 「日」= ベベル前: 2つの面(Face A, Face B)が1本のエッジを共有
        /// - 「目」= ベベル後: 2つの面の間に新しいベベル面が挿入される
        /// 
        /// 【処理ステップ】
        /// 1. 元エッジの両端頂点(v0, v1)から、各隣接面の内側方向へオフセットした新頂点を作成
        ///    - Face A側: newA0, newA1 (元エッジから amount だけ Face A 内側へ)
        ///    - Face B側: newB0, newB1 (元エッジから amount だけ Face B 内側へ)
        /// 
        /// 2. セグメント数 > 1 の場合、中間頂点を作成
        ///    - Fillet=false: A側とB側を直線補間
        ///    - Fillet=true: 内接円の円弧上に配置（R = amount × tan(θ/2)）
        /// 
        /// 3. 関連する面の頂点を更新
        ///    - Face A: v0→newA0, v1→newA1 に置き換え
        ///    - Face B: v0→newB0, v1→newB1 に置き換え
        ///    - 側面（v0-v1エッジを含む他の面）: 頂点を分割して全新頂点を挿入
        ///    - 端面（v0/v1のみを含む面）: ベベルエッジと平行なエッジを持つ場合は残す
        /// 
        /// 4. ベベル面を作成
        ///    - 新エッジ列(A側→中間→B側)を順に四角形で接続
        /// 
        /// 5. 隙間面を作成（必要な場合）
        ///    - 端面が残された場合、元頂点と新頂点を結ぶ多角形で隙間を埋める
        /// 
        /// 6. 孤立頂点を削除
        ///    - どの面からも参照されなくなった元頂点を削除
        /// </summary>
        private void ExecuteBevel(ToolContext ctx)
        {
            var meshData = ctx.MeshData;
            float amount = _dragAmount;
            int segments = _segments;
            int matIdx = ctx.CurrentMaterialIndex;
            var orphanCandidates = new HashSet<int>();

            foreach (var edgeInfo in _targetEdges)
            {
                // ========================================
                // 境界エッジ（隣接面が1つしかない）はスキップ
                // ========================================
                if (edgeInfo.FaceA < 0 || edgeInfo.FaceB < 0)
                    continue;

                int v0 = edgeInfo.V0;
                int v1 = edgeInfo.V1;
                Vector3 p0 = meshData.Vertices[v0].Position;
                Vector3 p1 = meshData.Vertices[v1].Position;

                var faceA = meshData.Faces[edgeInfo.FaceA];
                var faceB = meshData.Faces[edgeInfo.FaceB];

                // ========================================
                // STEP 1: オフセット方向の計算
                // ========================================
                // 各面の法線とエッジ方向の外積で、面内の内向き方向を算出
                Vector3 offsetDirA = GetInwardOffset(meshData, faceA, v0, v1);
                Vector3 offsetDirB = GetInwardOffset(meshData, faceB, v0, v1);

                // ========================================
                // STEP 2: Face A側の新頂点を作成
                // ========================================
                // 元エッジの両端から、Face A の内側方向へ amount だけオフセット
                int newA0, newA1;
                {
                    Vector3 newP0 = p0 + offsetDirA * amount;
                    Vector3 newP1 = p1 + offsetDirA * amount;

                    var nv0 = new Vertex { Position = newP0 };
                    nv0.UVs.AddRange(meshData.Vertices[v0].UVs);
                    nv0.Normals.AddRange(meshData.Vertices[v0].Normals);
                    newA0 = meshData.VertexCount;
                    meshData.Vertices.Add(nv0);

                    var nv1 = new Vertex { Position = newP1 };
                    nv1.UVs.AddRange(meshData.Vertices[v1].UVs);
                    nv1.Normals.AddRange(meshData.Vertices[v1].Normals);
                    newA1 = meshData.VertexCount;
                    meshData.Vertices.Add(nv1);
                }

                // ========================================
                // STEP 3: Face B側の新頂点を作成
                // ========================================
                // 元エッジの両端から、Face B の内側方向へ amount だけオフセット
                int newB0, newB1;
                {
                    Vector3 newP0 = p0 + offsetDirB * amount;
                    Vector3 newP1 = p1 + offsetDirB * amount;

                    var nv0 = new Vertex { Position = newP0 };
                    nv0.UVs.AddRange(meshData.Vertices[v0].UVs);
                    nv0.Normals.AddRange(meshData.Vertices[v0].Normals);
                    newB0 = meshData.VertexCount;
                    meshData.Vertices.Add(nv0);

                    var nv1 = new Vertex { Position = newP1 };
                    nv1.UVs.AddRange(meshData.Vertices[v1].UVs);
                    nv1.Normals.AddRange(meshData.Vertices[v1].Normals);
                    newB1 = meshData.VertexCount;
                    meshData.Vertices.Add(nv1);
                }

                // ========================================
                // STEP 4: 中間セグメントの頂点を作成（segments >= 2の場合）
                // ========================================
                // allEdgeVerts: ベベル面を構成する全エッジの頂点ペア
                // [0] = (newA0, newA1) ... Face A側
                // [1] = (mid0_0, mid0_1) ... 中間1
                // ...
                // [n] = (newB0, newB1) ... Face B側
                var allEdgeVerts = new List<(int v0side, int v1side)>();
                allEdgeVerts.Add((newA0, newA1));

                for (int s = 1; s < segments; s++)
                {
                    float t = (float)s / segments;

                    Vector3 midP0, midP1;

                    if (_fillet)
                    {
                        // ----------------------------------------
                        // フィレットモード: 内接円の円弧上に配置
                        // ----------------------------------------
                        // 2つの面がなす角度θから内接円の半径Rを算出:
                        //   R = amount × tan(θ/2)
                        // 円の中心は、元の角から (R / sin(θ/2)) の距離にある

                        float angleDeg = Vector3.Angle(offsetDirA, offsetDirB);
                        float angleRad = angleDeg * Mathf.Deg2Rad;
                        float halfAngle = angleRad * 0.5f;

                        float R = amount * Mathf.Tan(halfAngle);

                        Vector3 centerDir = (offsetDirA + offsetDirB).normalized;
                        float centerDist = R / Mathf.Sin(halfAngle);

                        Vector3 center0 = p0 + centerDir * centerDist;
                        Vector3 center1 = p1 + centerDir * centerDist;

                        // 円弧の角度範囲 = π - θ（外側の弧を使用）
                        float arcAngle = Mathf.PI - angleRad;
                        float currentAngle = t * arcAngle;

                        // A側の頂点から円の中心への方向を基準ベクトルとする
                        Vector3 fromCenterToA0 = (meshData.Vertices[newA0].Position - center0).normalized;
                        Vector3 fromCenterToA1 = (meshData.Vertices[newA1].Position - center1).normalized;

                        // 回転軸 = エッジ方向
                        Vector3 edgeDir = (p1 - p0).normalized;

                        // 回転方向の判定（A0→B0が時計回りか反時計回りか）
                        Vector3 fromCenterToB0 = (meshData.Vertices[newB0].Position - center0).normalized;
                        Vector3 crossAB = Vector3.Cross(fromCenterToA0, fromCenterToB0);
                        float rotSign = Mathf.Sign(Vector3.Dot(crossAB, edgeDir));

                        Quaternion rot = Quaternion.AngleAxis(rotSign * currentAngle * Mathf.Rad2Deg, edgeDir);

                        Vector3 dir0 = rot * fromCenterToA0;
                        Vector3 dir1 = rot * fromCenterToA1;

                        midP0 = center0 + dir0 * R;
                        midP1 = center1 + dir1 * R;
                    }
                    else
                    {
                        // ----------------------------------------
                        // 直線モード: A側とB側を線形補間
                        // ----------------------------------------
                        midP0 = Vector3.Lerp(
                            meshData.Vertices[newA0].Position,
                            meshData.Vertices[newB0].Position, t);
                        midP1 = Vector3.Lerp(
                            meshData.Vertices[newA1].Position,
                            meshData.Vertices[newB1].Position, t);
                    }

                    // 中間頂点を作成
                    var mv0 = new Vertex { Position = midP0 };
                    mv0.UVs.AddRange(meshData.Vertices[v0].UVs);
                    mv0.Normals.AddRange(meshData.Vertices[v0].Normals);
                    int midIdx0 = meshData.VertexCount;
                    meshData.Vertices.Add(mv0);

                    var mv1 = new Vertex { Position = midP1 };
                    mv1.UVs.AddRange(meshData.Vertices[v1].UVs);
                    mv1.Normals.AddRange(meshData.Vertices[v1].Normals);
                    int midIdx1 = meshData.VertexCount;
                    meshData.Vertices.Add(mv1);

                    allEdgeVerts.Add((midIdx0, midIdx1));
                }
                allEdgeVerts.Add((newB0, newB1));

                // ========================================
                // STEP 5: 関連する面の頂点を更新
                // ========================================
                // 
                // 面のタイプ別処理:
                // (A) Face A / Face B: 単純に v0,v1 を新頂点に置き換え
                // (B) 側面（v0-v1エッジを含む他の面）: v0,v1 を全新頂点列に分割
                // (C) 端面（v0/v1のみを含む、エッジを含まない面）:
                //     - ベベルエッジと平行なエッジを持つ → 変更しない（元頂点を残す）
                //     - ベベルエッジと直交するエッジのみ → 分割
                //
                // 判定基準: 内積 |dot(隣接エッジ方向, ベベルエッジ方向)|
                //   < 0.5 (60°〜120°) → 直交に近い → 分割
                //   >= 0.5 (0°〜60° or 120°〜180°) → 平行に近い → 残す

                var allV0Verts = allEdgeVerts.Select(e => e.v0side).ToList();
                var allV1Verts = allEdgeVerts.Select(e => e.v1side).ToList();

                // (B) 側面の処理: v0-v1 エッジを含む面 → 全頂点分割
                var facesWithEdge = FindFacesContainingEdge(meshData, v0, v1);
                foreach (int fi in facesWithEdge)
                {
                    if (fi == edgeInfo.FaceA || fi == edgeInfo.FaceB) continue;
                    var face = meshData.Faces[fi];

                    SplitVertexInFace(face, v0, allV0Verts, faceA, meshData);
                    SplitVertexInFace(face, v1, allV1Verts, faceA, meshData);
                }

                // (C) 端面の処理: v0/v1のみを含む面（エッジを含まない）
                // 
                // 例: 3連キューブの左キューブ上面は、真ん中キューブのベベルエッジと
                // 平行なエッジを持つため、変更せず元の頂点を残す。
                // この場合、元頂点と新頂点の間に隙間ができるため、隙間面で埋める。

                Vector3 bevelDir = (p1 - p0).normalized;
                const float parallelThreshold = 0.5f;  // |cos| < 0.5 = 60°〜120° は直交扱い

                var facesWithV0Only = FindFacesContainingVertex(meshData, v0)
                    .Where(fi => fi != edgeInfo.FaceA && fi != edgeInfo.FaceB)
                    .Where(fi => !facesWithEdge.Contains(fi))
                    .ToList();

                bool needV0Gap = false;  // v0側に隙間面が必要か
                foreach (int fi in facesWithV0Only)
                {
                    var face = meshData.Faces[fi];

                    // 面内でv0に接続する2つのエッジの方向を取得
                    int idx = face.VertexIndices.IndexOf(v0);
                    int count = face.VertexCount;
                    int prevV = face.VertexIndices[(idx - 1 + count) % count];
                    int nextV = face.VertexIndices[(idx + 1) % count];

                    // ベベルエッジとの内積で平行/直交を判定
                    Vector3 toPrev = (meshData.Vertices[prevV].Position - p0).normalized;
                    Vector3 toNext = (meshData.Vertices[nextV].Position - p0).normalized;
                    float dotPrev = Mathf.Abs(Vector3.Dot(toPrev, bevelDir));
                    float dotNext = Mathf.Abs(Vector3.Dot(toNext, bevelDir));

                    if (dotPrev < parallelThreshold && dotNext < parallelThreshold)
                    {
                        // 両方とも直交に近い → 影響を受ける → 分割
                        SplitVertexInFace(face, v0, allV0Verts, faceA, meshData);
                    }
                    else
                    {
                        // 平行なエッジがある → 影響を受けない → 残す（隙間面が必要）
                        needV0Gap = true;
                    }
                }

                var facesWithV1Only = FindFacesContainingVertex(meshData, v1)
                    .Where(fi => fi != edgeInfo.FaceA && fi != edgeInfo.FaceB)
                    .Where(fi => !facesWithEdge.Contains(fi))
                    .ToList();

                bool needV1Gap = false;  // v1側に隙間面が必要か
                foreach (int fi in facesWithV1Only)
                {
                    var face = meshData.Faces[fi];

                    int idx = face.VertexIndices.IndexOf(v1);
                    int count = face.VertexCount;
                    int prevV = face.VertexIndices[(idx - 1 + count) % count];
                    int nextV = face.VertexIndices[(idx + 1) % count];

                    Vector3 toPrev = (meshData.Vertices[prevV].Position - p1).normalized;
                    Vector3 toNext = (meshData.Vertices[nextV].Position - p1).normalized;
                    float dotPrev = Mathf.Abs(Vector3.Dot(toPrev, bevelDir));
                    float dotNext = Mathf.Abs(Vector3.Dot(toNext, bevelDir));

                    if (dotPrev < parallelThreshold && dotNext < parallelThreshold)
                    {
                        SplitVertexInFace(face, v1, allV1Verts, faceA, meshData);
                    }
                    else
                    {
                        needV1Gap = true;
                    }
                }

                // ========================================
                // (A) Face A / Face B の頂点を置き換え
                // ========================================
                // まずエッジの向きを確認（ベベル面の頂点順序に使用）
                bool edgeForwardInA = false;
                {
                    int idxV0InA = faceA.VertexIndices.IndexOf(v0);
                    if (idxV0InA >= 0)
                    {
                        int nextInA = faceA.VertexIndices[(idxV0InA + 1) % faceA.VertexCount];
                        edgeForwardInA = (nextInA == v1);  // Face A で v0→v1 の順か
                    }
                }

                ReplaceFaceVertex(faceA, v0, newA0);
                ReplaceFaceVertex(faceA, v1, newA1);
                ReplaceFaceVertex(faceB, v0, newB0);
                ReplaceFaceVertex(faceB, v1, newB1);

                // ========================================
                // STEP 6: 隙間面を作成（必要な場合）
                // ========================================
                // 端面が残された場合、元頂点(v0/v1)と新頂点列を結ぶ多角形で隙間を埋める
                // 
                // 例: 3連キューブで真ん中をベベル
                //     v1 ─── newA1
                //      │ ╲   │
                //      │  ╲  │  ← この三角形（または多角形）で隙間を埋める
                //      │   ╲ │
                //     v5 ─── newB1

                if (needV0Gap)
                {
                    var gapFace = new Face { MaterialIndex = matIdx };
                    if (edgeForwardInA)
                    {
                        // Face A側が先 → v0, newB0, ..., newA0 の順（CCW）
                        gapFace.VertexIndices.Add(v0);
                        for (int i = allV0Verts.Count - 1; i >= 0; i--)
                            gapFace.VertexIndices.Add(allV0Verts[i]);
                    }
                    else
                    {
                        // Face B側が先 → v0, newA0, ..., newB0 の順（CCW）
                        gapFace.VertexIndices.Add(v0);
                        foreach (int vi in allV0Verts)
                            gapFace.VertexIndices.Add(vi);
                    }
                    foreach (int vi in gapFace.VertexIndices)
                    {
                        gapFace.UVIndices.Add(vi);
                        gapFace.NormalIndices.Add(vi);
                    }
                    meshData.Faces.Add(gapFace);
                }

                // v1側の隙間面
                if (needV1Gap)
                {
                    var gapFace = new Face { MaterialIndex = matIdx };
                    if (edgeForwardInA)
                    {
                        // v1, newA1, ..., newB1 の順（CCW）
                        gapFace.VertexIndices.Add(v1);
                        foreach (int vi in allV1Verts)
                            gapFace.VertexIndices.Add(vi);
                    }
                    else
                    {
                        // v1, newB1, ..., newA1 の順（CCW）
                        gapFace.VertexIndices.Add(v1);
                        for (int i = allV1Verts.Count - 1; i >= 0; i--)
                            gapFace.VertexIndices.Add(allV1Verts[i]);
                    }
                    foreach (int vi in gapFace.VertexIndices)
                    {
                        gapFace.UVIndices.Add(vi);
                        gapFace.NormalIndices.Add(vi);
                    }
                    meshData.Faces.Add(gapFace);
                }

                // ========================================
                // STEP 7: ベベル面を作成
                // ========================================
                // allEdgeVerts の隣接するエッジ同士を四角形で接続
                // 頂点順序は Face A でのエッジの向きに合わせて決定（正しい法線方向のため）
                //
                //  e1.v0side ─── e1.v1side
                //      │             │
                //      │  ベベル面   │
                //      │             │
                //  e2.v0side ─── e2.v1side

                for (int i = 0; i < allEdgeVerts.Count - 1; i++)
                {
                    var e1 = allEdgeVerts[i];
                    var e2 = allEdgeVerts[i + 1];

                    var bevelFace = new Face { MaterialIndex = matIdx };

                    if (edgeForwardInA)
                    {
                        // Face A で V0→V1 の順: e1.v0 → e2.v0 → e2.v1 → e1.v1
                        bevelFace.VertexIndices.AddRange(new[] { e1.v0side, e2.v0side, e2.v1side, e1.v1side });
                        bevelFace.UVIndices.AddRange(new[] { e1.v0side, e2.v0side, e2.v1side, e1.v1side });
                        bevelFace.NormalIndices.AddRange(new[] { e1.v0side, e2.v0side, e2.v1side, e1.v1side });
                    }
                    else
                    {
                        // Face A で V1→V0 の順: e1.v0 → e1.v1 → e2.v1 → e2.v0
                        bevelFace.VertexIndices.AddRange(new[] { e1.v0side, e1.v1side, e2.v1side, e2.v0side });
                        bevelFace.UVIndices.AddRange(new[] { e1.v0side, e1.v1side, e2.v1side, e2.v0side });
                        bevelFace.NormalIndices.AddRange(new[] { e1.v0side, e1.v1side, e2.v1side, e2.v0side });
                    }
                    meshData.Faces.Add(bevelFace);
                }

                // ========================================
                // STEP 8: 孤立頂点の削除候補を記録
                // ========================================
                // 元のエッジ頂点(v0, v1)はどの面からも参照されなくなる可能性がある
                // → 後で一括して削除判定
                orphanCandidates.Add(v0);
                orphanCandidates.Add(v1);
            }

            // ========================================
            // 孤立頂点を一括削除
            // ========================================
            // どの面からも参照されなくなった頂点を削除
            // インデックスずれを防ぐため、大きいインデックスから順に削除
            RemoveOrphanVertices(meshData, orphanCandidates);

            ctx.SelectionState.Edges.Clear();
            ctx.SyncMesh?.Invoke();
        }

        /// <summary>
        /// 面内の頂点を置き換える
        /// </summary>
        private void ReplaceFaceVertex(Face face, int oldV, int newV)
        {
            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                if (face.VertexIndices[i] == oldV)
                {
                    face.VertexIndices[i] = newV;
                    if (i < face.UVIndices.Count) face.UVIndices[i] = newV;
                    if (i < face.NormalIndices.Count) face.NormalIndices[i] = newV;
                }
            }
        }

        /// <summary>
        /// 面内の1頂点を複数頂点に分割して挿入
        /// </summary>
        private void SplitVertexInFace(Face face, int oldV, List<int> newVerts,
            Face faceA, MeshData meshData)
        {
            int idx = face.VertexIndices.IndexOf(oldV);
            if (idx < 0) return;
            if (newVerts.Count == 0) return;

            int count = face.VertexCount;
            int prevIdx = (idx - 1 + count) % count;
            int prevV = face.VertexIndices[prevIdx];

            // Face A で oldV の隣接頂点を取得して、挿入順序を決定
            int idxInA = faceA.VertexIndices.IndexOf(oldV);
            bool prevEdgeSharedWithA = false;

            if (idxInA >= 0)
            {
                int prevInA = faceA.VertexIndices[(idxInA - 1 + faceA.VertexCount) % faceA.VertexCount];
                int nextInA = faceA.VertexIndices[(idxInA + 1) % faceA.VertexCount];
                prevEdgeSharedWithA = (prevV == prevInA || prevV == nextInA);
            }

            // 挿入する頂点リストを準備（順序を決定）
            var vertsToInsert = new List<int>(newVerts);
            if (!prevEdgeSharedWithA)
            {
                // 逆順にする
                vertsToInsert.Reverse();
            }

            // oldVを最初の頂点に置き換え、残りを挿入
            face.VertexIndices[idx] = vertsToInsert[0];
            for (int i = 1; i < vertsToInsert.Count; i++)
            {
                face.VertexIndices.Insert(idx + i, vertsToInsert[i]);
            }

            // UV/NormalIndicesも同様に
            while (face.UVIndices.Count < face.VertexIndices.Count)
                face.UVIndices.Add(face.UVIndices.Count > 0 ? face.UVIndices[face.UVIndices.Count - 1] : 0);
            face.UVIndices[idx] = vertsToInsert[0];
            for (int i = 1; i < vertsToInsert.Count; i++)
            {
                face.UVIndices.Insert(idx + i, vertsToInsert[i]);
            }

            while (face.NormalIndices.Count < face.VertexIndices.Count)
                face.NormalIndices.Add(face.NormalIndices.Count > 0 ? face.NormalIndices[face.NormalIndices.Count - 1] : 0);
            face.NormalIndices[idx] = vertsToInsert[0];
            for (int i = 1; i < vertsToInsert.Count; i++)
            {
                face.NormalIndices.Insert(idx + i, vertsToInsert[i]);
            }
        }

        /// <summary>
        /// 頂点を含む全ての面のインデックスを取得
        /// </summary>
        private List<int> FindFacesContainingVertex(MeshData meshData, int vertexIdx)
        {
            var result = new List<int>();
            for (int i = 0; i < meshData.FaceCount; i++)
            {
                if (meshData.Faces[i].VertexIndices.Contains(vertexIdx))
                    result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// エッジ(v0-v1)を含む全ての面のインデックスを取得
        /// </summary>
        private List<int> FindFacesContainingEdge(MeshData meshData, int v0, int v1)
        {
            var result = new List<int>();
            for (int i = 0; i < meshData.FaceCount; i++)
            {
                var face = meshData.Faces[i];
                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                {
                    // v0とv1が隣接しているか確認
                    int idx0 = face.VertexIndices.IndexOf(v0);
                    int idx1 = face.VertexIndices.IndexOf(v1);
                    int count = face.VertexCount;

                    if ((idx0 + 1) % count == idx1 || (idx1 + 1) % count == idx0)
                    {
                        result.Add(i);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// faceがtargetFaceと指定頂点を含むエッジを共有しているか
        /// </summary>
        private bool SharesEdgeWithFace(Face face, Face targetFace, int sharedVertex)
        {
            int idx = face.VertexIndices.IndexOf(sharedVertex);
            if (idx < 0) return false;

            int count = face.VertexCount;
            int prev = face.VertexIndices[(idx - 1 + count) % count];
            int next = face.VertexIndices[(idx + 1) % count];

            // targetFaceがsharedVertexとprev、またはsharedVertexとnextを含むか
            bool hasPrevEdge = targetFace.VertexIndices.Contains(sharedVertex) &&
                               targetFace.VertexIndices.Contains(prev);
            bool hasNextEdge = targetFace.VertexIndices.Contains(sharedVertex) &&
                               targetFace.VertexIndices.Contains(next);

            return hasPrevEdge || hasNextEdge;
        }

        /// <summary>
        /// どの面からも参照されていない頂点を一括削除
        /// </summary>
        private void RemoveOrphanVertices(MeshData meshData, HashSet<int> candidates)
        {
            // 使用中の頂点を収集
            var usedVertices = new HashSet<int>();
            foreach (var face in meshData.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    usedVertices.Add(vi);
            }

            // 削除対象の頂点（降順でソート - 後ろから削除するため）
            var toRemove = candidates.Where(v => !usedVertices.Contains(v) && v >= 0 && v < meshData.VertexCount)
                                     .OrderByDescending(v => v)
                                     .ToList();

            foreach (int vertexIdx in toRemove)
            {
                // 頂点を削除
                meshData.Vertices.RemoveAt(vertexIdx);

                // 全ての面のインデックスを更新
                foreach (var face in meshData.Faces)
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


        /// <summary>
        /// 面内でエッジの内向きオフセット方向を計算
        /// </summary>
        private Vector3 GetInwardOffset(MeshData meshData, Face face, int v0, int v1)
        {
            // 面の法線を計算
            Vector3 faceNormal = CalculateFaceNormal(meshData, face);

            // エッジ方向
            Vector3 p0 = meshData.Vertices[v0].Position;
            Vector3 p1 = meshData.Vertices[v1].Position;
            Vector3 edgeDir = (p1 - p0).normalized;

            // 内向き方向 = 法線 × エッジ方向
            Vector3 inward = Vector3.Cross(faceNormal, edgeDir).normalized;

            // 内向きかどうか確認（面の中心方向を向いているか）
            Vector3 faceCenter = CalculateFaceCenter(meshData, face);
            Vector3 edgeCenter = (p0 + p1) * 0.5f;
            Vector3 toCenter = (faceCenter - edgeCenter).normalized;

            if (Vector3.Dot(inward, toCenter) < 0)
                inward = -inward;

            return inward;
        }

        private Vector3 CalculateFaceNormal(MeshData meshData, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;

            Vector3 p0 = meshData.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshData.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshData.Vertices[face.VertexIndices[2]].Position;

            return Vector3.Cross(p1 - p0, p2 - p0).normalized;
        }

        private Vector3 CalculateFaceCenter(MeshData meshData, Face face)
        {
            Vector3 center = Vector3.zero;
            foreach (int vi in face.VertexIndices)
                center += meshData.Vertices[vi].Position;
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

                if (v0 < 0 || v0 >= ctx.MeshData.VertexCount) continue;
                if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;

                // 隣接面を探す
                var adjacentFaces = FindAdjacentFaces(ctx.MeshData, v0, v1);

                Vector3 p0 = ctx.MeshData.Vertices[v0].Position;
                Vector3 p1 = ctx.MeshData.Vertices[v1].Position;

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

        private List<int> FindAdjacentFaces(MeshData meshData, int v0, int v1)
        {
            var result = new List<int>();

            for (int i = 0; i < meshData.FaceCount; i++)
            {
                var face = meshData.Faces[i];
                if (face.VertexCount < 3) continue;

                if (face.VertexIndices.Contains(v0) && face.VertexIndices.Contains(v1))
                    result.Add(i);
            }

            return result;
        }

        private VertexPair? FindEdgeAtPosition(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return null;

            const float threshold = 8f;

            for (int fi = 0; fi < ctx.MeshData.FaceCount; fi++)
            {
                var face = ctx.MeshData.Faces[fi];
                if (face.VertexCount < 3) continue;

                for (int i = 0; i < face.VertexCount; i++)
                {
                    int v0 = face.VertexIndices[i];
                    int v1 = face.VertexIndices[(i + 1) % face.VertexCount];
                    if (v0 < 0 || v1 < 0 || v0 >= ctx.MeshData.VertexCount || v1 >= ctx.MeshData.VertexCount)
                        continue;

                    Vector2 p0 = ctx.WorldToScreen(ctx.MeshData.Vertices[v0].Position);
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);

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
            // 簡易プレビュー：新エッジの位置を表示
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);

            foreach (var edge in _targetEdges)
            {
                if (edge.FaceA < 0 || edge.FaceB < 0) continue;

                Vector3 p0 = ctx.MeshData.Vertices[edge.V0].Position;
                Vector3 p1 = ctx.MeshData.Vertices[edge.V1].Position;

                var faceA = ctx.MeshData.Faces[edge.FaceA];
                var faceB = ctx.MeshData.Faces[edge.FaceB];

                Vector3 offsetA = GetInwardOffset(ctx.MeshData, faceA, edge.V0, edge.V1);
                Vector3 offsetB = GetInwardOffset(ctx.MeshData, faceB, edge.V0, edge.V1);

                float stepAmount = _dragAmount / _segments;

                // Face A側の新エッジ
                Vector3 newA0 = p0 + offsetA * stepAmount;
                Vector3 newA1 = p1 + offsetA * stepAmount;
                Vector2 sA0 = ctx.WorldToScreen(newA0);
                Vector2 sA1 = ctx.WorldToScreen(newA1);
                DrawThickLine(sA0, sA1, 2f);

                // Face B側の新エッジ
                Vector3 newB0 = p0 + offsetB * stepAmount;
                Vector3 newB1 = p1 + offsetB * stepAmount;
                Vector2 sB0 = ctx.WorldToScreen(newB0);
                Vector2 sB1 = ctx.WorldToScreen(newB1);
                DrawThickLine(sB0, sB1, 2f);

                // 接続線
                Handles.color = new Color(1f, 0.6f, 0.2f, 0.6f);
                DrawThickLine(sA0, sB0, 1f);
                DrawThickLine(sA1, sB1, 1f);
                Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            }
        }

        private void DrawThickLine(Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 dir = (p1 - p0);
            if (dir.magnitude < 0.001f) return;
            dir.Normalize();

            Vector2 perp = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
            Handles.DrawAAConvexPolygon(p0 - perp, p0 + perp, p1 + perp, p1 - perp);
        }
    }
}