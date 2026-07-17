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

        /// <summary>等分割の分割ピース数（≥2）。EqualDivide / BeltLoop で使用。</summary>
        public int Divisions
        {
            get => _settings.Divisions;
            set => _settings.Divisions = value < 2 ? 2 : value;
        }

        /// <summary>等分割オン（各モードで N 等分。オフは自由比率1本）。</summary>
        public bool EqualDivide
        {
            get => _settings.EqualDivide;
            set => _settings.EqualDivide = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum LadderStage { Idle, HasStart, HasSegment }
        private LadderStage _stage = LadderStage.Idle;

        private int        _startVertex = -1;
        private VertexPair _segment;
        private bool       _hasSegment;
        // セグメント上のクリック比率（_segment.V1 起点。0=V1,1=V2）。既定は中点。
        private float      _cutRatio = 0.5f;

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
        public bool NextClickIsEdge => Mode == KnifeMode.Erase || Mode == KnifeMode.BeltLoop || _stage == LadderStage.HasStart;

        // ---- 状態アクセサ（サブパネル情報表示用） ----
        /// <summary>開始頂点が確定しているか。</summary>
        public bool HasStartVertex => _startVertex >= 0;
        /// <summary>確定した開始頂点（未確定は -1）。</summary>
        public int  CurrentStartVertex => _startVertex;
        /// <summary>通過セグメントが確定しているか。</summary>
        public bool HasSegmentEdge => _hasSegment;
        /// <summary>確定した通過セグメント。</summary>
        public VertexPair CurrentSegment => _segment;
        /// <summary>セグメント上のクリック比率（V1 起点）。</summary>
        public float CutRatio => _cutRatio;

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
            if (Mode == KnifeMode.BeltLoop) return T("PickBeltEdge");
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

            switch (Mode)
            {
                case KnifeMode.Erase:    return HandleEraseClick(ctx, mousePos);
                case KnifeMode.BeltLoop: return HandleBeltClick(ctx, mousePos);
                default:                 return HandleLadderClick(ctx, mo, mousePos);
            }
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return false;

            switch (Mode)
            {
                case KnifeMode.Erase:    UpdateEraseHover(ctx, mousePos); break;
                case KnifeMode.BeltLoop: UpdateBeltHover(ctx, mousePos);  break;
                default:                 UpdateLadderHover(ctx, mo, mousePos); break;
            }

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
            _cutRatio = 0.5f;
            _hasBeltHover = false;
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
                    _cutRatio = ComputeClickRatio(ctx, mo, _segment, mousePos);
                    _stage = LadderStage.HasSegment;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return true;
                }
                case LadderStage.HasSegment:
                {
                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0) return false;

                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v, _cutRatio, _segment.V1);
                    if (!plan.Ok)
                    {
                        // 警告して何もしない。状態は維持（別の終了頂点を選べる）。
                        LastError = plan.Error;
                        ctx.Repaint?.Invoke();
                        return true;
                    }

                    if (EqualDivide)
                        NCutExecutor.Execute(ctx, mo, plan, Divisions);
                    else
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

                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v, _cutRatio, _segment.V1);
                    if (!plan.Ok) { _preview.PlanValid = false; break; }

                    _preview.PlanValid = true;
                    if (EqualDivide)
                        BuildEqualDividePolylines(mo, plan, v, Divisions);
                    else
                        BuildPlanPolyline(mo, plan, v);
                    _preview.DotVertices.Add(v);
                    break;
                }
            }
        }

        /// <summary>
        /// クリック点をセグメント（V1→V2）の画面投影線上へ射影して比率 t を返す。
        /// t は V1 起点（0=V1, 1=V2）。投影不能時は 0.5。端の退化回避で 0.02..0.98 にクランプ。
        /// </summary>
        private float ComputeClickRatio(ToolContext ctx, MeshObject mo, VertexPair seg, Vector2 mousePosImgui)
        {
            if (ctx == null || ctx.WorldToScreenPos == null) return 0.5f;
            if (seg.V1 < 0 || seg.V2 < 0 || seg.V1 >= mo.VertexCount || seg.V2 >= mo.VertexCount) return 0.5f;

            float h = ctx.PreviewRect.height;
            // WorldToScreen は Y=0 上（UIToolkit）。mousePos は IMGUI（Y=0 下）なので端点も IMGUI 系へ揃える。
            Vector2 s1 = ctx.WorldToScreen(mo.Vertices[seg.V1].Position); s1.y = h - s1.y;
            Vector2 s2 = ctx.WorldToScreen(mo.Vertices[seg.V2].Position); s2.y = h - s2.y;

            Vector2 d = s2 - s1;
            float len2 = d.sqrMagnitude;
            if (len2 < 1e-6f) return 0.5f;
            float t = Vector2.Dot(mousePosImgui - s1, d) / len2;
            if (t < 0.02f) t = 0.02f; else if (t > 0.98f) t = 0.98f;
            return t;
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
                float t = 0.5f;
                if (plan.RungParams.TryGetValue(rung, out var rp))
                    t = (rp.AnchorVertex == rung.V2) ? (1f - rp.Ratio) : rp.Ratio;
                var cut = Vector3.Lerp(mo.Vertices[rung.V1].Position, mo.Vertices[rung.V2].Position, t);
                pts.Add(cut);
                _preview.DotWorld.Add(cut);
            }
            pts.Add(mo.Vertices[endVertex].Position);

            for (int i = 0; i < pts.Count - 1; i++)
                _preview.Lines.Add((pts[i], pts[i + 1]));
        }

        /// <summary>
        /// 等分割プレビュー: N-1 本の折れ線（開始頂点→各 rung の i/N 点→終了頂点）。
        /// 各 rung の向きは RungParams のアンカーで揃える（実切断と同じ側）。
        /// </summary>
        private void BuildEqualDividePolylines(MeshObject mo, LadderCutPlan plan, int endVertex, int divisions)
        {
            int cuts = Mathf.Max(1, divisions - 1);
            for (int i = 1; i <= cuts; i++)
            {
                float r = (float)i / divisions;
                var pts = new List<Vector3>();
                pts.Add(mo.Vertices[_startVertex].Position);
                foreach (var rung in plan.Rungs)
                {
                    float t = r;
                    if (plan.RungParams.TryGetValue(rung, out var rp) && rp.AnchorVertex == rung.V2)
                        t = 1f - r;
                    var cut = Vector3.Lerp(mo.Vertices[rung.V1].Position, mo.Vertices[rung.V2].Position, t);
                    pts.Add(cut);
                    _preview.DotWorld.Add(cut);
                }
                pts.Add(mo.Vertices[endVertex].Position);
                for (int j = 0; j < pts.Count - 1; j++)
                    _preview.Lines.Add((pts[j], pts[j + 1]));
            }
        }
    }
}
