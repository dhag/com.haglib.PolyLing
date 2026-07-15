// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.cs
// ナイフツール（一新版・メインファイル）。
// ラダー切断: 開始頂点 → セグメント(1辺) → 終了頂点。端点は既存頂点。
// 連続/非連続は終了頂点の位置で表現（専用トグル無し）。Erase は別モード。
// 巡回・ヒットテストは AdvancedSelect / BeltSelectMode と同方式（インデックスベース）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ナイフツール。
    /// </summary>
    public partial class KnifeTool : IEditTool
    {
        public string Name => "Knife";
        public string DisplayName => "Knife";

        private KnifeSettings _settings = new KnifeSettings();
        public IToolSettings Settings => _settings;

        public KnifeMode Mode
        {
            get => _settings.Mode;
            set { if (_settings.Mode != value) { _settings.Mode = value; Reset(); } }
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum LadderStage { Idle, HasStart, HasSegment }
        private LadderStage _stage = LadderStage.Idle;

        private int        _startVertex = -1;
        private VertexPair _segment;
        private bool       _hasSegment;

        // GPU ホバー由来の頂点/辺（Player でハンドラが毎回設定）。
        // Player では SetGpuHover が呼ばれ _gpuHoverActive=true。この場合 CPU 探索へは
        // フォールバックしない（利用禁止の CPU 経路が遮蔽頂点/辺を拾うのを防ぐ）。
        // Editor では未設定（_gpuHoverActive=false）＝従来 CPU 経路。
        private bool        _gpuHoverActive;
        private int         _gpuHoverVertex = -1;
        private VertexPair? _gpuHoverEdge;

        /// <summary>
        /// 次回クリック/ホバーの解決に使う GPU ホバー要素を設定する。
        /// Player のハンドラが OnMouseDown / OnMouseDrag 直前に呼ぶ。未ヒットは -1 / null。
        /// </summary>
        public void SetGpuHover(int vertex, VertexPair? edge)
        {
            _gpuHoverActive = true;
            _gpuHoverVertex = vertex;
            _gpuHoverEdge   = edge;
        }

        /// <summary>次のクリックが辺を対象にするか（Erase は常に辺、ラダーは HasStart のみ辺）。</summary>
        public bool NextClickIsEdge => Mode == KnifeMode.Erase || _stage == LadderStage.HasStart;

        // 【CPUヒットテスト禁止。これもバグあり使用禁止】
        // Editor 用 CPU フォールバック（SelectionHelper.FindNearestVertex/EdgePair）を全撤去。
        // GPU ホバー無効時は解決しない（-1/null＝非ハイライト）。
        private int ResolveVertex(ToolContext ctx, Vector2 mousePos)
            => _gpuHoverActive ? _gpuHoverVertex : -1;

        private VertexPair? ResolveEdge(ToolContext ctx, Vector2 mousePos)
            => _gpuHoverActive ? _gpuHoverEdge : null;

        // ================================================================
        // プレビュー（オーバーレイ描画用、ワールド座標）
        // ================================================================

        public sealed class KnifePreview
        {
            /// <summary>点を打つ既存頂点（開始/終了候補）。</summary>
            public readonly List<int> DotVertices = new List<int>();
            /// <summary>点を打つラング中点（ワールド座標）。</summary>
            public readonly List<Vector3> DotWorld = new List<Vector3>();
            /// <summary>切断線・ハイライト線（ワールド座標の線分列）。</summary>
            public readonly List<(Vector3, Vector3)> Lines = new List<(Vector3, Vector3)>();
            /// <summary>解決可能か（終了頂点ホバー時）。</summary>
            public bool PlanValid;

            public void Clear()
            {
                DotVertices.Clear();
                DotWorld.Clear();
                Lines.Clear();
                PlanValid = false;
            }
        }

        private readonly KnifePreview _preview = new KnifePreview();
        public KnifePreview Preview => _preview;

        /// <summary>直近の解決失敗理由（UI 表示用）。</summary>
        public string LastError { get; private set; } = "";

        /// <summary>状態の簡易説明（UI 表示用）。</summary>
        public string StageText()
        {
            if (Mode == KnifeMode.Erase) return T("HelpErase");
            switch (_stage)
            {
                case LadderStage.Idle:       return T("PickStart");
                case LadderStage.HasStart:   return T("PickSegment");
                case LadderStage.HasSegment: return string.IsNullOrEmpty(LastError) ? T("PickEnd") : LastError;
            }
            return "";
        }

        // ================================================================
        // IEditTool
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return false;

            if (ctx.CurrentKeyCode == KeyCode.Escape)
            {
                Reset();
                ctx.Repaint?.Invoke();
                return true;
            }

            return Mode == KnifeMode.Erase
                ? HandleEraseClick(ctx, mousePos)
                : HandleLadderClick(ctx, mo, mousePos);
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return false;

            if (Mode == KnifeMode.Erase) UpdateEraseHover(ctx, mousePos);
            else UpdateLadderHover(ctx, mo, mousePos);

            ctx.Repaint?.Invoke();
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx) => Reset();
        public void OnDeactivate(ToolContext ctx) => Reset();

        public void Reset()
        {
            _stage = LadderStage.Idle;
            _startVertex = -1;
            _segment = default;
            _hasSegment = false;
            LastError = "";
            _preview.Clear();
            _hoveredEraseEdge = default;
            _hasEraseHover = false;
        }

        // ================================================================
        // ラダー切断: クリック
        // ================================================================

        private bool HandleLadderClick(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            switch (_stage)
            {
                case LadderStage.Idle:
                {
                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0) return false;
                    _startVertex = v;
                    _stage = LadderStage.HasStart;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return true;
                }
                case LadderStage.HasStart:
                {
                    var e = ResolveEdge(ctx, mousePos);
                    if (!e.HasValue) return false;
                    // 開始頂点に隣接する辺はセグメントにできない
                    if (e.Value.Contains(_startVertex)) { LastError = T("ErrSegAdjacent"); ctx.Repaint?.Invoke(); return true; }
                    // ベルトが開始頂点の四角形に届かない辺は代表にできない
                    if (!LadderCutResolver.IsSegmentReachable(mo, _startVertex, e.Value)) { LastError = T("ErrSegUnreachable"); ctx.Repaint?.Invoke(); return true; }
                    _segment = e.Value;
                    _hasSegment = true;
                    _stage = LadderStage.HasSegment;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return true;
                }
                case LadderStage.HasSegment:
                {
                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0) return false;

                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v);
                    if (!plan.Ok)
                    {
                        // 警告して何もしない。状態は維持（別の終了頂点を選べる）。
                        LastError = plan.Error;
                        ctx.Repaint?.Invoke();
                        return true;
                    }

                    LadderCutExecutor.Execute(ctx, mo, plan);
                    ctx.NotifyTopologyChanged?.Invoke();
                    Reset();
                    ctx.Repaint?.Invoke();
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // ラダー切断: ホバープレビュー
        // ================================================================

        private void UpdateLadderHover(ToolContext ctx, MeshObject mo, Vector2 mousePos)
        {
            _preview.Clear();

            switch (_stage)
            {
                case LadderStage.Idle:
                {
                    int v = ResolveVertex(ctx, mousePos);
                    if (v >= 0) _preview.DotVertices.Add(v);
                    break;
                }
                case LadderStage.HasStart:
                {
                    _preview.DotVertices.Add(_startVertex);
                    var e = ResolveEdge(ctx, mousePos);
                    if (e.HasValue && !e.Value.Contains(_startVertex)
                        && LadderCutResolver.IsSegmentReachable(mo, _startVertex, e.Value))
                        _preview.Lines.Add((mo.Vertices[e.Value.V1].Position, mo.Vertices[e.Value.V2].Position));
                    break;
                }
                case LadderStage.HasSegment:
                {
                    _preview.DotVertices.Add(_startVertex);
                    // セグメントをハイライト
                    _preview.Lines.Add((mo.Vertices[_segment.V1].Position, mo.Vertices[_segment.V2].Position));

                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0 || v == _startVertex) break;

                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v);
                    if (!plan.Ok) { _preview.PlanValid = false; break; }

                    _preview.PlanValid = true;
                    BuildPlanPolyline(mo, plan, v);
                    _preview.DotVertices.Add(v);
                    break;
                }
            }
        }

        /// <summary>
        /// 計画から切断線（開始頂点→各ラング中点→終了頂点）を構築する。
        /// </summary>
        private void BuildPlanPolyline(MeshObject mo, LadderCutPlan plan, int endVertex)
        {
            // 順序付きの切断点列を作る
            var pts = new List<Vector3>();
            pts.Add(mo.Vertices[_startVertex].Position);
            foreach (var rung in plan.Rungs)
            {
                var mid = Vector3.Lerp(mo.Vertices[rung.V1].Position, mo.Vertices[rung.V2].Position, 0.5f);
                pts.Add(mid);
                _preview.DotWorld.Add(mid);
            }
            pts.Add(mo.Vertices[endVertex].Position);

            for (int i = 0; i < pts.Count - 1; i++)
                _preview.Lines.Add((pts[i], pts[i + 1]));
        }
    }
}
