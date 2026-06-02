// Tools/EdgeTopologyTool.cs
// 辺トポロジツール - 辺の編集（入れ替え、分割、結合）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 辺トポロジツールのモード
    /// </summary>
    public enum EdgeTopoMode
    {
        /// <summary>辺の入れ替え（2三角形の対角線切り替え）</summary>
        Flip,
        /// <summary>四角形を対角線で分割</summary>
        Split,
        /// <summary>辺の消去（2面を結合）</summary>
        Dissolve
    }

    /// <summary>
    /// 辺トポロジツール - 辺のトポロジ編集
    /// </summary>
    public partial class EdgeTopologyTool : IEditTool
    {
        public string Name => "EdgeTopo";
        public string DisplayName => "EdgeTopo";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private EdgeTopologySettings _settings = new EdgeTopologySettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private EdgeTopoMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        // Player ビュー用公開 API
        public EdgeTopoMode ModePublic { get => Mode; set { Mode = value; Reset(); } }

        // === ドラッグ状態 ===
        private bool _isDragging;
        private Vector2 _startScreenPos;
        private Vector2 _currentScreenPos;

        // === 検出結果 ===
        private EdgeInfo? _hoveredEdge;
        private int _hoveredFaceIndex = -1;
        private int _hoveredVertexIndex = -1; // Split用ホバー頂点
        private Vector3? _startWorldPos;      // Split用：開始位置（同位置の複数頂点に対応）
        private int _startVertexIndex = -1;   // Split用：スナップ確定時の開始頂点
        private int _endVertexIndex = -1;     // Split用

        // === Split クリック式 ===
        private int _splitFirstVertex = -1;   // 1クリック目の頂点インデックス（-1=未確定）
        private int _splitHoverVertex = -1;   // 現在ホバー中の頂点インデックス（GPUホバーから）

        /// <summary>
        /// Split モード: 第 1 頂点確定時に 1 回だけ計算される対向点候補集合。
        /// Key = 対角頂点 index、Value = その頂点と第 1 頂点を対角に持つ四角形 face index。
        ///
        /// 【設計ポイント: 「クリック時 1 回だけ計算してキャッシュ」パターン】
        /// 本ツールの規約上、毎フレーム CPU で頂点/辺を走査するのは禁止。
        /// Split では第 2 クリック時に「第 1 頂点と対角になる四角形」を特定する必要があるが、
        /// これを従来は毎フレーム CPU 全面走査 (FindQuadWithDiagonal) で行っていて規約違反だった。
        /// 代わりに「第 1 クリック時に 1 度だけ面を舐めて対角頂点集合を作る → Dict にキャッシュ」
        /// に切り替え、第 2 クリック時は Dict lookup で O(1) 判定する。
        /// オーバーレイ描画側 (候補頂点ハイライト) もこの Dict を ContainsKey 参照するだけで済む。
        /// 同様のパターンは他ツールでも使える (クリック確定時の近傍/対応関係の事前計算)。
        ///
        /// 【自動クリアのタイミング】
        /// Reset() / ModePublic setter (Reset を呼ぶ) / OnDeactivate / 第 2 クリック成否問わず。
        /// これを一箇所でも漏らすと古い候補が次モード/次回クリックに持ち越されてバグる。
        /// </summary>
        private readonly Dictionary<int, int> _splitOpponentCandidates = new Dictionary<int, int>();
        public IReadOnlyDictionary<int, int> SplitOpponentCandidates => _splitOpponentCandidates;

        // === 定数 ===
        private const float EDGE_CLICK_THRESHOLD = 10f;  // 辺クリック判定の距離（ピクセル）
        private const float VERTEX_CLICK_THRESHOLD = 15f; // 頂点クリック判定の距離（ピクセル）

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Flip", "Split", "Dissolve" };
        private static readonly EdgeTopoMode[] ModeValues = { EdgeTopoMode.Flip, EdgeTopoMode.Split, EdgeTopoMode.Dissolve };

        /// <summary>
        /// 辺の情報
        /// </summary>
        private struct EdgeInfo
        {
            public int FaceIndex1;      // 辺を持つ面1のインデックス
            public int FaceIndex2;      // 辺を持つ面2のインデックス（-1 = 境界辺）
            public int VertexIndex1;    // 辺の頂点1（グローバルインデックス）
            public int VertexIndex2;    // 辺の頂点2（グローバルインデックス）
            public Vector2 ScreenPos1;  // スクリーン座標1
            public Vector2 ScreenPos2;  // スクリーン座標2
            public bool IsShared => FaceIndex2 >= 0;

            /// <summary>Flip可能か（両面が三角形）</summary>
            public bool CanFlip(MeshObject meshObject)
            {
                if (!IsShared) return false;
                var face1 = meshObject.Faces[FaceIndex1];
                var face2 = meshObject.Faces[FaceIndex2];
                return face1.VertexIndices.Count == 3 && face2.VertexIndices.Count == 3;
            }
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null) return false;

            _isDragging = true;
            _startScreenPos = mousePos;
            _currentScreenPos = mousePos;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    // GPUホバー由来の _hoveredEdge を使用（CPU側 FindNearestEdge は閾値・カリングの問題で不正確）
                    if (_hoveredEdge.HasValue && _hoveredEdge.Value.IsShared && _hoveredEdge.Value.CanFlip(ctx.FirstSelectedMeshObject))
                    {
                        ExecuteFlip(ctx, _hoveredEdge.Value);
                    }
                    _isDragging = false;
                    return true;

                case EdgeTopoMode.Dissolve:
                    // GPUホバー由来の _hoveredEdge を使用
                    if (_hoveredEdge.HasValue && _hoveredEdge.Value.IsShared)
                    {
                        ExecuteDissolve(ctx, _hoveredEdge.Value);
                    }
                    _isDragging = false;
                    return true;

                case EdgeTopoMode.Split:
                    // クリック式: GPUホバー頂点を1点目→2点目と選択し、対角が同一四角形なら分割
                    _isDragging = false;
                    if (_splitHoverVertex < 0)
                    {
                        // ホバー頂点なしのクリックは無視
                        return true;
                    }
                    if (_splitFirstVertex < 0)
                    {
                        // 1クリック目: 第1頂点を確定し、対向点候補を 1 回だけ計算してキャッシュ
                        _splitFirstVertex = _splitHoverVertex;
                        BuildSplitOpponentCandidates(ctx.FirstSelectedMeshObject, _splitFirstVertex);
                    }
                    else
                    {
                        // 2クリック目: キャッシュされた候補集合に含まれるかを O(1) で判定
                        int v1 = _splitFirstVertex;
                        int v2 = _splitHoverVertex;
                        if (v1 != v2 && _splitOpponentCandidates.TryGetValue(v2, out int quadFaceIdx))
                        {
                            ExecuteSplit(ctx, quadFaceIdx, v1, v2);
                        }
                        // 成功でも失敗でも、2点目クリックで状態をリセット
                        _splitFirstVertex = -1;
                        _splitOpponentCandidates.Clear();
                    }
                    ctx.Repaint?.Invoke();
                    return true;
                    ctx.Repaint?.Invoke();
                    return true;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;

            _currentScreenPos = mousePos;

            // Split: DrawSplitPreview内で_endVertexIndexと_hoveredFaceIndexを更新

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            // Split はクリック式に変更（OnMouseDown で即時判定）。ドラッグ依存ロジックは削除。

            _startWorldPos = null;
            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;

            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 【重大規約違反: CPU 検索呼出しあり】
        /// 呼び出し元なし（dead code）。Phase 6 で関数ごと削除予定。
        /// ハンドラ層の GPU ホバー経路 (EdgeTopologyToolHandler.UpdateHover) へ移行済み。
        /// </summary>
        public void OnMouseMove(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null) return;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                case EdgeTopoMode.Dissolve:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);   // 違反: CPU 検索呼出し
                    break;

                case EdgeTopoMode.Split:
                    // DrawGizmo内で処理
                    break;
            }

            ctx.Repaint?.Invoke();
        }

        public void DrawOverlay(ToolContext ctx, Rect previewRect)
        {
            if (ctx.FirstSelectedMeshObject == null) return;

            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    DrawFlipPreview(ctx);
                    break;

                case EdgeTopoMode.Dissolve:
                    DrawDissolvePreview(ctx);
                    break;

                case EdgeTopoMode.Split:
                    DrawSplitPreview(ctx);
                    break;
            }
        }

        /// <summary>
        /// ステータス表示
        /// </summary>
        private void DrawStatusUI()
        {
            // ステータスはDrawOverlay内で視覚的に表示されるため、
            // ここでは追加の説明のみ表示
        }

        public void Reset()
        {
            _isDragging = false;
            _startWorldPos = null;
            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;
            _hoveredEdge = null;
            _splitFirstVertex = -1;
            _splitHoverVertex = -1;
            _splitOpponentCandidates.Clear();
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        // ── UIToolkit hover support ───────────────────────────────────────
        /// <summary>ホバー辺が存在するか（Flip/Dissolve モード）</summary>
        public bool HasHoverEdge => _hoveredEdge.HasValue;
        /// <summary>ホバー辺の頂点1インデックス（-1=なし）</summary>
        public int  HoverEdgeV1  => _hoveredEdge?.VertexIndex1 ?? -1;
        /// <summary>ホバー辺の頂点2インデックス（-1=なし）</summary>
        public int  HoverEdgeV2  => _hoveredEdge?.VertexIndex2 ?? -1;

        /// <summary>
        /// ハンドラーが GPU ホバー結果からセット。
        /// FindEdgeAtPosition/FindNearestEdge の直接呼び出しはハンドラーから行わないこと。
        /// </summary>
        public void SetHoverEdge(int v1, int v2, MeshObject meshObject)
        {
            if (v1 < 0 || v2 < 0 || meshObject == null) { _hoveredEdge = null; return; }
            // vertex indices から EdgeInfo を再構築
            int fi1 = -1, fi2 = -1;
            for (int fi = 0; fi < meshObject.FaceCount; fi++)
            {
                var verts = meshObject.Faces[fi].VertexIndices;
                if (verts.Contains(v1) && verts.Contains(v2))
                {
                    if (fi1 < 0) fi1 = fi;
                    else { fi2 = fi; break; }
                }
            }
            if (fi1 < 0) { _hoveredEdge = null; return; }
            _hoveredEdge = new EdgeInfo
            {
                VertexIndex1 = v1, VertexIndex2 = v2,
                FaceIndex1 = fi1, FaceIndex2 = fi2,
            };
        }

        // ── Split クリック式 公開API ───────────────────────────────────
        /// <summary>1点目が確定済みか</summary>
        public bool HasSplitFirstVertex => _splitFirstVertex >= 0;
        /// <summary>1クリック目で確定した頂点（-1=未確定）</summary>
        public int SplitFirstVertex => _splitFirstVertex;
        /// <summary>現在ホバー中の頂点（-1=なし）</summary>
        public int SplitHoverVertex => _splitHoverVertex;

        /// <summary>ハンドラーが GPU 頂点ホバーからセット。Split モード以外では使わない。</summary>
        public void SetSplitHoverVertex(int v) { _splitHoverVertex = v; }

        /// <summary>
        /// Split モード: 第 1 頂点確定時に、対向点候補 (第 1 頂点を対角として含む
        /// 4 頂点面の反対側頂点集合) を 1 回だけ計算してキャッシュする。
        ///
        /// 仕様:
        ///   - Key = 対角頂点 index、Value = 対応する四角形 face index
        ///   - 4 頂点面以外 (三角形、5 頂点以上) は無視
        ///   - IsTriangulated == true のメッシュでは 4 頂点面が存在しないため候補は
        ///     空集合になる。意図的にブロックせず自然に空のまま通す。
        ///   - 毎フレームではなく第 1 クリック時 1 回のみ呼ばれるため、面走査自体は
        ///     規約違反 (CPU 頂点/辺検索の禁止) には該当しない。
        /// </summary>
        /// <summary>
        /// Split モード第 1 頂点確定時に、対向点候補集合を 1 回だけ計算する。
        ///
        /// 仕様:
        ///   - Key = 対角頂点 index、Value = 対応する四角形 face index
        ///   - 4 頂点面以外 (三角形、5 頂点以上) は無視
        ///   - IsTriangulated == true のメッシュでは 4 頂点面がないので候補は空になる
        ///     (意図的にブロックはしない。ユーザが Split を試みても「候補なし」で無反応になるだけ)
        ///   - 単発呼び出しのため面走査しても規約違反にはならない (毎フレーム走査が禁止)
        /// </summary>
        private void BuildSplitOpponentCandidates(MeshObject mo, int firstVertex)
        {
            _splitOpponentCandidates.Clear();
            if (mo == null || firstVertex < 0) return;
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face.VertexIndices.Count != 4) continue;
                int i1 = face.VertexIndices.IndexOf(firstVertex);
                if (i1 < 0) continue;
                int diagIdx = face.VertexIndices[(i1 + 2) % 4];
                // 同じ頂点が複数四角形の対角に現れた場合は最初の face を保持
                // (Split では最初の四角形で実行されるだけで実害はない)
                if (!_splitOpponentCandidates.ContainsKey(diagIdx))
                    _splitOpponentCandidates[diagIdx] = f;
            }
        }

        // ================================================================
        // 辺検出
        //
        // ★★★ 【重大規約違反区画: CPU ベース頂点・辺検索】 ★★★
        // 以下の関数群は「CPU ベース頂点・辺検索は禁止」規約に違反する。
        // AI が無断追加・残置したため、Phase 6 で GPU ベース経路へ置換後に削除する。
        //
        //   FindNearestEdge      → GPU ホバー (_hoveredEdge) 経由に置換済み、関数のみ残骸
        //   BuildEdgeToFacesMap  → FindNearestEdge の補助、連鎖 dead
        //   FindNearestQuadVertex → 旧 Split 経路残骸、呼出し元なし
        //   OnMouseMove 内の FindNearestEdge 呼出し → ハンドラから呼ばれず dead
        //
        // 新規コードからこれら関数を呼ぶことは厳禁。
        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        // ================================================================

        /// <summary>
        /// 【重大規約違反: CPU 検索】マウス位置に最も近い辺を検索。
        /// 呼び出し元は OnMouseMove（dead code）のみ。Phase 6 で関数ごと削除予定。
        /// </summary>
        private EdgeInfo? FindNearestEdge(ToolContext ctx, Vector2 mousePos)
        {
            float minDist = EDGE_CLICK_THRESHOLD;
            EdgeInfo? result = null;

            // 全ての辺を走査
            var edgeToFaces = BuildEdgeToFacesMap(ctx.FirstSelectedMeshObject);

            foreach (var kvp in edgeToFaces)
            {
                var (v1, v2) = kvp.Key;
                var faces = kvp.Value;

                // スクリーン座標を計算
                Vector3 worldPos1 = ctx.FirstSelectedMeshObject.Vertices[v1].Position;
                Vector3 worldPos2 = ctx.FirstSelectedMeshObject.Vertices[v2].Position;
                Vector2 screenPos1 = ctx.WorldToScreenPos(worldPos1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 screenPos2 = ctx.WorldToScreenPos(worldPos2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // マウスと辺の距離
                float dist = DistanceToLineSegment(mousePos, screenPos1, screenPos2);

                if (dist < minDist)
                {
                    minDist = dist;
                    result = new EdgeInfo
                    {
                        FaceIndex1 = faces[0],
                        FaceIndex2 = faces.Count > 1 ? faces[1] : -1,
                        VertexIndex1 = v1,
                        VertexIndex2 = v2,
                        ScreenPos1 = screenPos1,
                        ScreenPos2 = screenPos2
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// 【重大規約違反: CPU 検索】辺→面のマップを構築。
        /// FindNearestEdge の補助のみ。Phase 6 で削除予定。
        /// </summary>
        private Dictionary<(int, int), List<int>> BuildEdgeToFacesMap(MeshObject meshObject)
        {
            var map = new Dictionary<(int, int), List<int>>();

            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    // 正規化（小さい方を先に）
                    var key = v1 < v2 ? (v1, v2) : (v2, v1);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        /// <summary>
        /// 点と線分の距離
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        // ================================================================
        // Split用: 四角形頂点検出
        // ================================================================

        /// <summary>
        /// 【重大規約違反: CPU 検索】最も近い四角形面の頂点を検索（距離ベース）。
        /// 呼び出し元なし（完全な残骸）。Phase 6 で関数ごと削除予定。
        /// </summary>
        private (int faceIndex, int vertexIndex) FindNearestQuadVertex(ToolContext ctx, Vector2 mousePos, float threshold)
        {
            float minDist = threshold;
            int resultFace = -1;
            int resultVertex = -1;

            for (int f = 0; f < ctx.FirstSelectedMeshObject.FaceCount; f++)
            {
                var face = ctx.FirstSelectedMeshObject.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                for (int i = 0; i < 4; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 worldPos = ctx.FirstSelectedMeshObject.Vertices[vIdx].Position;
                    Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = Vector2.Distance(mousePos, screenPos);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        resultFace = f;
                        resultVertex = vIdx;
                    }
                }
            }

            // 検証：resultVertexがresultFaceに属しているか
            if (resultFace >= 0 && resultVertex >= 0)
            {
                var face = ctx.FirstSelectedMeshObject.Faces[resultFace];
                if (!face.VertexIndices.Contains(resultVertex))
                {
                    Debug.LogError($"[Split] BUG: vertex {resultVertex} not in face {resultFace}");
                    return (-1, -1);
                }
            }

            return (resultFace, resultVertex);
        }

        /// <summary>
        /// マウス位置が含まれる四角形面を検索
        /// </summary>
        private int FindQuadFaceContainingPoint(ToolContext ctx, Vector2 mousePos)
        {
            for (int f = 0; f < ctx.FirstSelectedMeshObject.FaceCount; f++)
            {
                var face = ctx.FirstSelectedMeshObject.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                var screenPositions = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 worldPos = ctx.FirstSelectedMeshObject.Vertices[vIdx].Position;
                    screenPositions[i] = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                }

                if (IsPointInQuad(mousePos, screenPositions))
                {
                    return f;
                }
            }
            return -1;
        }

        /// <summary>
        /// 四角形面内で最も近い頂点を検索（内外判定を使用）
        /// </summary>
        private int FindNearestVertexInQuad(ToolContext ctx, Vector2 mousePos, out int faceIndex)
        {
            faceIndex = FindQuadFaceContainingPoint(ctx, mousePos);
            if (faceIndex < 0) return -1;

            return FindNearestVertexInFace(ctx, mousePos, faceIndex);
        }

        /// <summary>
        /// 点が四角形の内側にあるか判定（Winding Number法）
        /// </summary>
        private bool IsPointInQuad(Vector2 point, Vector2[] quad)
        {
            float windingNumber = 0;

            for (int i = 0; i < 4; i++)
            {
                Vector2 v1 = quad[i];
                Vector2 v2 = quad[(i + 1) % 4];

                if (v1.y <= point.y)
                {
                    if (v2.y > point.y)
                    {
                        // 上向き交差
                        if (IsLeft(v1, v2, point) > 0)
                            windingNumber++;
                    }
                }
                else
                {
                    if (v2.y <= point.y)
                    {
                        // 下向き交差
                        if (IsLeft(v1, v2, point) < 0)
                            windingNumber--;
                    }
                }
            }

            return windingNumber != 0;
        }

        /// <summary>
        /// 点が線分の左側にあるかを判定（外積の符号）
        /// </summary>
        private float IsLeft(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            return (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
        }

        /// <summary>
        /// 特定の面内で最も近い頂点を検索（閾値なし）
        /// </summary>
        private int FindNearestVertexInFace(ToolContext ctx, Vector2 mousePos, int faceIndex)
        {
            if (faceIndex < 0 || faceIndex >= ctx.FirstSelectedMeshObject.FaceCount) return -1;

            var face = ctx.FirstSelectedMeshObject.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return -1;

            float minDist = float.MaxValue;
            int resultVertex = -1;

            for (int i = 0; i < 4; i++)
            {
                int vIdx = face.VertexIndices[i];
                Vector3 worldPos = ctx.FirstSelectedMeshObject.Vertices[vIdx].Position;
                Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = Vector2.Distance(mousePos, screenPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    resultVertex = vIdx;
                }
            }

            return resultVertex;
        }

        /// <summary>
        /// 点が三角形の内側にあるか判定
        /// </summary>
        private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = CrossSign(p, a, b);
            float d2 = CrossSign(p, b, c);
            float d3 = CrossSign(p, c, a);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        /// <summary>
        /// 外積の符号を計算
        /// </summary>
        private float CrossSign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        /// <summary>
        /// 対角頂点を検索
        /// </summary>
        private int FindOppositeVertex(ToolContext ctx, int faceIndex, int startVertex, Vector2 mousePos)
        {
            if (faceIndex < 0 || faceIndex >= ctx.FirstSelectedMeshObject.FaceCount) return -1;

            var face = ctx.FirstSelectedMeshObject.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return -1;

            // 開始頂点の位置を探す
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            if (startIdx < 0)
            {
                Debug.LogError($"[Split] FindOpposite: startVertex {startVertex} not in face {faceIndex}, verts=[{string.Join(",", face.VertexIndices)}]");
                return -1;
            }

            // 対角は+2の位置
            int oppositeIdx = (startIdx + 2) % 4;
            int oppositeVertex = face.VertexIndices[oppositeIdx];

            // マウスが対角頂点の近くにあるか確認
            Vector3 worldPos = ctx.FirstSelectedMeshObject.Vertices[oppositeVertex].Position;
            Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float dist = Vector2.Distance(mousePos, screenPos);

            // 閾値を緩めにする（50px）
            if (dist < 50f)
            {
                return oppositeVertex;
            }

            return -1;
        }

        // ================================================================
        // 操作実行
        // ================================================================

        /// <summary>
        /// Edge Flip実行
        /// </summary>
        private void ExecuteFlip(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex1];
            var face2 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex2];

            // 両方とも三角形でなければ不可
            if (face1.VertexIndices.Count != 3 || face2.VertexIndices.Count != 3)
            {
                Debug.LogWarning("Edge Flip requires two triangles");
                return;
            }

            // スナップショット（操作前）
            var before = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);

            // 共有辺の頂点
            int v1 = edge.VertexIndex1;
            int v2 = edge.VertexIndex2;

            // 共有辺以外の頂点を見つける
            int opposite1 = face1.VertexIndices.First(v => v != v1 && v != v2);
            int opposite2 = face2.VertexIndices.First(v => v != v1 && v != v2);

            // 巻き順を維持するため、元の面の頂点リストで共有辺の頂点を対角頂点に置き換える
            // face1: v2 → opposite2 に置き換え
            // face2: v1 → opposite1 に置き換え
            var newVerts1 = new List<int>(face1.VertexIndices);
            var newVerts2 = new List<int>(face2.VertexIndices);

            int idx1 = newVerts1.IndexOf(v2);
            int idx2 = newVerts2.IndexOf(v1);

            if (idx1 >= 0) newVerts1[idx1] = opposite2;
            if (idx2 >= 0) newVerts2[idx2] = opposite1;

            face1.VertexIndices = newVerts1;
            face2.VertexIndices = newVerts2;

            // UV/Normalインデックスも更新（簡易版: リセット）
            face1.UVIndices = new List<int> { 0, 0, 0 };
            face1.NormalIndices = new List<int> { 0, 0, 0 };
            face2.UVIndices = new List<int> { 0, 0, 0 };
            face2.NormalIndices = new List<int> { 0, 0, 0 };

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
            ctx.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                ctx.UndoController, before, after, "Edge Flip"));
        }

        /// <summary>
        /// Quad Split実行
        /// </summary>
        private void ExecuteSplit(ToolContext ctx, int faceIndex, int startVertex, int endVertex)
        {
            if (faceIndex < 0 || faceIndex >= ctx.FirstSelectedMeshObject.FaceCount) return;

            var face = ctx.FirstSelectedMeshObject.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return;

            // スナップショット（操作前）
            var before = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);

            // 頂点の位置を特定
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            int endIdx = face.VertexIndices.IndexOf(endVertex);
            if (startIdx < 0 || endIdx < 0) return;

            // 対角かどうか確認
            if (Math.Abs(startIdx - endIdx) != 2) return;

            // 四角形の頂点（順序通り）
            int v0 = face.VertexIndices[0];
            int v1 = face.VertexIndices[1];
            int v2 = face.VertexIndices[2];
            int v3 = face.VertexIndices[3];

            Face newFace1, newFace2;

            if ((startIdx == 0 && endIdx == 2) || (startIdx == 2 && endIdx == 0))
            {
                // 0-2対角線で分割
                newFace1 = new Face(v0, v1, v2);
                newFace2 = new Face(v0, v2, v3);
            }
            else
            {
                // 1-3対角線で分割
                newFace1 = new Face(v0, v1, v3);
                newFace2 = new Face(v1, v2, v3);
            }

            // UV/Normalインデックス設定
            newFace1.UVIndices = new List<int> { 0, 0, 0 };
            newFace1.NormalIndices = new List<int> { 0, 0, 0 };
            newFace2.UVIndices = new List<int> { 0, 0, 0 };
            newFace2.NormalIndices = new List<int> { 0, 0, 0 };

            // 元の面を置換、新しい面を追加
            ctx.FirstSelectedMeshObject.Faces[faceIndex] = newFace1;
            ctx.FirstSelectedMeshObject.Faces.Add(newFace2);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
            ctx.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                ctx.UndoController, before, after, "Quad Split"));
        }

        /// <summary>
        /// Edge Dissolve実行
        /// </summary>
        private void ExecuteDissolve(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex1];
            var face2 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex2];

            // スナップショット（操作前）
            MeshObjectSnapshot before = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);

            // 2つの面を結合した多角形を作成
            var mergedVertices = MergeFaceVertices(face1, face2, edge.VertexIndex1, edge.VertexIndex2);
            if (mergedVertices == null || mergedVertices.Count < 3)
            {
                Debug.LogWarning("Cannot dissolve edge");
                return;
            }

            // 新しい面を作成
            var newFace = new Face
            {
                VertexIndices = mergedVertices,
                UVIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList(),
                NormalIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList()
            };

            // 面を削除（大きいインデックスから）
            int removeFirst = Math.Max(edge.FaceIndex1, edge.FaceIndex2);
            int removeSecond = Math.Min(edge.FaceIndex1, edge.FaceIndex2);

            ctx.FirstSelectedMeshObject.Faces.RemoveAt(removeFirst);
            ctx.FirstSelectedMeshObject.Faces.RemoveAt(removeSecond);

            // 新しい面を追加
            ctx.FirstSelectedMeshObject.Faces.Add(newFace);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
            ctx.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                ctx.UndoController, before, after, "Edge Dissolve"));
        }

        /// <summary>
        /// 2つの面の頂点を結合（共有辺を除去）。
        /// 各 face で共有辺の forward 方向を判定し、非共有経路を走査する。
        /// 三角形・四角形・多角形、および closing edge 位置のいずれにも対応。
        /// </summary>
        private List<int> MergeFaceVertices(Face face1, Face face2, int sharedV1, int sharedV2)
        {
            var verts1 = face1.VertexIndices;
            var verts2 = face2.VertexIndices;
            int n1 = verts1.Count, n2 = verts2.Count;

            int i1a = verts1.IndexOf(sharedV1);
            int i1b = verts1.IndexOf(sharedV2);
            if (i1a < 0 || i1b < 0) return null;

            int i2a = verts2.IndexOf(sharedV1);
            int i2b = verts2.IndexOf(sharedV2);
            if (i2a < 0 || i2b < 0) return null;

            // face1 の共有辺 forward 方向を判定
            //   (i1a+1)%n1 == i1b : 共有辺 = sharedV1→sharedV2 forward
            //     → 非共有経路 = sharedV2 から forward に sharedV1 直前まで（start=i1b, end=i1a）
            //   (i1b+1)%n1 == i1a : 共有辺 = sharedV2→sharedV1 forward（closing edge の場合を含む）
            //     → 非共有経路 = sharedV1 から forward に sharedV2 直前まで（start=i1a, end=i1b）
            int start1, end1;
            if ((i1a + 1) % n1 == i1b)      { start1 = i1b; end1 = i1a; }
            else if ((i1b + 1) % n1 == i1a) { start1 = i1a; end1 = i1b; }
            else return null; // 共有辺が face1 の辺として存在しない

            int start2, end2;
            if ((i2a + 1) % n2 == i2b)      { start2 = i2b; end2 = i2a; }
            else if ((i2b + 1) % n2 == i2a) { start2 = i2a; end2 = i2b; }
            else return null; // 共有辺が face2 の辺として存在しない

            var result = new List<int>();

            // face1 を start1 から forward に end1 直前まで追加
            int current = start1;
            while (current != end1)
            {
                result.Add(verts1[current]);
                current = (current + 1) % n1;
            }

            // face2 を start2 から forward に end2 直前まで追加
            current = start2;
            while (current != end2)
            {
                result.Add(verts2[current]);
                current = (current + 1) % n2;
            }

            return result;
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// Flip用プレビュー描画
        /// </summary>
        private void DrawFlipPreview(ToolContext ctx)
        {
            if (!_hoveredEdge.HasValue) return;

            var edge = _hoveredEdge.Value;

            // UnityEditor_Handles 削除済み

            // 辺の状態に応じた色を決定
            Color edgeColor;
            float lineWidth;

            if (!edge.IsShared)
            {
                // 境界辺（操作不可）
                edgeColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                lineWidth = 2f;
            }
            else if (edge.CanFlip(ctx.FirstSelectedMeshObject))
            {
                // Flip可能（緑）
                edgeColor = Color.green;
                lineWidth = 5f;

                // 隣接する2つの三角形をハイライト

                // 新しい対角線をプレビュー
                DrawNewDiagonalPreview(ctx, edge);
            }
            else
            {
                // 共有辺だが三角形でない（黄色警告）
                edgeColor = new Color(1f, 0.7f, 0f, 0.8f);
                lineWidth = 3f;
            }

            // 辺を描画
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

            // 端点を描画
            float size = edge.IsShared && edge.CanFlip(ctx.FirstSelectedMeshObject) ? 8f : 5f;
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

            // UnityEditor_Handles 削除済み
        }

        /// <summary>
        /// Flip後の新しい対角線をプレビュー
        /// </summary>
        private void DrawNewDiagonalPreview(ToolContext ctx, EdgeInfo edge)
        {
            var face1 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex1];
            var face2 = ctx.FirstSelectedMeshObject.Faces[edge.FaceIndex2];

            // 共有辺以外の頂点を見つける
            int opposite1 = -1, opposite2 = -1;
            foreach (int v in face1.VertexIndices)
            {
                if (v != edge.VertexIndex1 && v != edge.VertexIndex2)
                {
                    opposite1 = v;
                    break;
                }
            }
            foreach (int v in face2.VertexIndices)
            {
                if (v != edge.VertexIndex1 && v != edge.VertexIndex2)
                {
                    opposite2 = v;
                    break;
                }
            }

            if (opposite1 >= 0 && opposite2 >= 0)
            {
                Vector2 sp1 = ctx.WorldToScreenPos(ctx.FirstSelectedMeshObject.Vertices[opposite1].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(ctx.FirstSelectedMeshObject.Vertices[opposite2].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // 新しい対角線を点線で描画
                // UnityEditor_Handles 削除済み
                DrawDashedLine(sp1, sp2, 4f, 8f);
            }
        }

        /// <summary>
        /// Dissolve用プレビュー描画
        /// </summary>
        private void DrawDissolvePreview(ToolContext ctx)
        {
            if (!_hoveredEdge.HasValue) return;

            var edge = _hoveredEdge.Value;

            // UnityEditor_Handles 削除済み

            Color edgeColor;
            float lineWidth;

            if (!edge.IsShared)
            {
                // 境界辺（操作不可）
                edgeColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                lineWidth = 2f;
            }
            else
            {
                // Dissolve可能（マゼンタ）
                edgeColor = new Color(1f, 0f, 1f, 1f);
                lineWidth = 5f;

                // 結合される面をハイライト
            }

            // 辺を描画
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

            // 端点
            float size = edge.IsShared ? 8f : 5f;
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

            // UnityEditor_Handles 削除済み
        }

        /// <summary>
        /// 面をハイライト描画
        /// </summary>

        /// <summary>
        /// 点線を描画
        /// </summary>
        private void DrawDashedLine(Vector2 start, Vector2 end, float dashLength, float gapLength)
        {
            Vector2 dir = (end - start).normalized;
            float totalLength = Vector2.Distance(start, end);
            float current = 0f;
            bool drawing = true;

            while (current < totalLength)
            {
                float segmentLength = drawing ? dashLength : gapLength;
                float nextPos = Mathf.Min(current + segmentLength, totalLength);

                if (drawing)
                {
                    Vector2 p1 = start + dir * current;
                    Vector2 p2 = start + dir * nextPos;
                    // UnityEditor_Handles 削除済み
                }

                current = nextPos;
                drawing = !drawing;
            }
        }

        /// <summary>
        /// Split プレビュー描画
        /// </summary>
        private void DrawSplitPreview(ToolContext ctx)
        {
            // UnityEditor_Handles 削除済み

            // 開始位置が選択されている場合
            if (_startWorldPos.HasValue)
            {
                Vector3 startWorldPos = _startWorldPos.Value;
                Vector2 startScreen = ctx.WorldToScreenPos(startWorldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // 開始位置を黄色で表示
                // UnityEditor_Handles 削除済み
                // UnityEditor_Handles 削除済み

                // 対角頂点候補を取得（開始位置と同位置の全頂点から）
                var candidates = GetOppositeVertexCandidates(ctx, startWorldPos);

                if (candidates.Count == 0)
                {
                    // UnityEditor_Handles 削除済み
                    return;
                }

                // 最も近い候補を見つける
                int nearestCandidate = -1;
                int nearestFace = -1;
                int nearestStartVertex = -1;
                float minDist = float.MaxValue;

                foreach (var (faceIdx, oppVertex, startVertex) in candidates)
                {
                    Vector2 oppScreen = ctx.WorldToScreenPos(ctx.FirstSelectedMeshObject.Vertices[oppVertex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    float dist = Vector2.Distance(_currentScreenPos, oppScreen);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestCandidate = oppVertex;
                        nearestFace = faceIdx;
                        nearestStartVertex = startVertex;
                    }
                }

                // 距離による状態判定
                const float SNAP_THRESHOLD = 20f;   // この距離以内で確定（白）
                const float NEAR_THRESHOLD = 50f;   // この距離以内で接近中（緑）

                bool isSnapped = minDist < SNAP_THRESHOLD;
                bool isNear = minDist < NEAR_THRESHOLD;

                // 候補頂点を描画
                foreach (var (faceIdx, oppVertex, startVertex) in candidates)
                {
                    Vector2 oppScreen = ctx.WorldToScreenPos(ctx.FirstSelectedMeshObject.Vertices[oppVertex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    if (oppVertex == nearestCandidate && faceIdx == nearestFace)
                    {
                        if (isSnapped)
                        {
                            // スナップ状態：白で大きく
                            // UnityEditor_Handles 削除済み
                            // UnityEditor_Handles 削除済み

                            // 対角線を太く
                            // UnityEditor_Handles 削除済み
                            // UnityEditor_Handles 削除済み
                        }
                        else if (isNear)
                        {
                            // 接近中：緑
                            // UnityEditor_Handles 削除済み
                            // UnityEditor_Handles 削除済み

                            // 対角線プレビュー（細め）
                            // UnityEditor_Handles 削除済み
                            // UnityEditor_Handles 削除済み
                        }
                        else
                        {
                            // 遠い：灰色（線なし）
                            // UnityEditor_Handles 削除済み
                            // UnityEditor_Handles 削除済み
                        }
                    }
                    else
                    {
                        // その他の候補は小さく灰色
                        // UnityEditor_Handles 削除済み
                        // UnityEditor_Handles 削除済み
                    }
                }

                // ドラッグ中の情報を更新（スナップ時のみ有効）
                if (_isDragging)
                {
                    if (isSnapped)
                    {
                        _startVertexIndex = nearestStartVertex;
                        _endVertexIndex = nearestCandidate;
                        _hoveredFaceIndex = nearestFace;
                    }
                    else
                    {
                        _startVertexIndex = -1;
                        _endVertexIndex = -1;
                        _hoveredFaceIndex = -1;
                    }
                }
            }
            // 開始頂点未選択時：ホバー中の頂点を表示
            else if (_hoveredVertexIndex >= 0)
            {
                Vector2 hoverScreen = ctx.WorldToScreenPos(ctx.FirstSelectedMeshObject.Vertices[_hoveredVertexIndex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                // UnityEditor_Handles 削除済み
                // UnityEditor_Handles 削除済み
            }

            // UnityEditor_Handles 削除済み
        }

        /// <summary>
        /// 指定頂点が属する全四角形面の対角頂点を取得
        /// 開始頂点と同じ位置にある頂点は除外
        /// </summary>
        private List<(int faceIndex, int oppositeVertex, int startVertex)> GetOppositeVertexCandidates(ToolContext ctx, Vector3 startWorldPos)
        {
            var result = new List<(int, int, int)>();
            const float POSITION_EPSILON = 0.0001f;

            // 開始位置と同じ位置にある全ての頂点を収集
            var startVertices = new List<int>();
            for (int v = 0; v < ctx.FirstSelectedMeshObject.Vertices.Count; v++)
            {
                if (Vector3.Distance(ctx.FirstSelectedMeshObject.Vertices[v].Position, startWorldPos) < POSITION_EPSILON)
                {
                    startVertices.Add(v);
                }
            }

            // 各開始頂点について、属する四角形の対角頂点を収集
            for (int f = 0; f < ctx.FirstSelectedMeshObject.FaceCount; f++)
            {
                var face = ctx.FirstSelectedMeshObject.Faces[f];
                if (face.VertexIndices.Count != 4) continue;

                foreach (int startVertex in startVertices)
                {
                    int localIdx = face.VertexIndices.IndexOf(startVertex);
                    if (localIdx < 0) continue;

                    // 対角は+2の位置
                    int oppositeIdx = (localIdx + 2) % 4;
                    int oppositeVertex = face.VertexIndices[oppositeIdx];

                    // 対角頂点が開始位置と同じ位置なら除外
                    Vector3 oppWorldPos = ctx.FirstSelectedMeshObject.Vertices[oppositeVertex].Position;
                    if (Vector3.Distance(startWorldPos, oppWorldPos) < POSITION_EPSILON)
                        continue;

                    result.Add((f, oppositeVertex, startVertex));
                }
            }

            return result;
        }
    }
}