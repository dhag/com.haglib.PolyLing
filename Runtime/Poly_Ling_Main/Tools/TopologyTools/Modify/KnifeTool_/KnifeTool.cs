// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.cs
// ナイフツール（一新版・メインファイル）。
// ラダー切断: 開始頂点 → セグメント(1辺) → 終了頂点。端点は既存頂点。
// 連続/非連続は終了頂点の位置で表現（専用トグル無し）。Erase は別モード。
// 巡回・ヒットテストは AdvancedSelect / BeltSelectMode と同方式（インデックスベース）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
// ExecuteEraseEdgeFromCommand が MeshObjectSnapshot を直接宣言するため。
using Poly_Ling.UndoSystem;

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

        /// <summary>SimpleCut: 5角以上の面を三角形＋四角形へ再分解する（既定 ON）。</summary>
        public bool SimpleTriQuad
        {
            get => _settings.SimpleTriQuad;
            set => _settings.SimpleTriQuad = value;
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
            /// <summary>ワールド座標の点。GPU 由来の値で構築する（KnifeTool.VW）。</summary>
            public readonly List<Vector3> DotWorld = new List<Vector3>();
            /// <summary>切断線・ハイライト線（ワールド座標の線分列）。</summary>
            /// <summary>ワールド座標の線分。GPU 由来の値で構築する（KnifeTool.VW）。</summary>
            public readonly List<(Vector3, Vector3)> Lines = new List<(Vector3, Vector3)>();
            /// <summary>画面座標(IMGUI Y下)で直接描く点。SimpleCut 用。</summary>
            public readonly List<Vector2> ScreenDots = new List<Vector2>();
            /// <summary>画面座標(IMGUI Y下)で直接描く線分。SimpleCut 用。</summary>
            public readonly List<(Vector2, Vector2)> ScreenLines = new List<(Vector2, Vector2)>();
            /// <summary>解決可能か（終了頂点ホバー時）。</summary>
            public bool PlanValid;

            public void Clear()
            {
                DotVertices.Clear();
                DotWorld.Clear();
                Lines.Clear();
                ScreenDots.Clear();
                ScreenLines.Clear();
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
            if (Mode == KnifeMode.SimpleCut) return _simpleStage == SimpleStage.HasP0 ? T("PickSecond") : T("PickFirst");
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
            var mo = ctx.ActiveMeshObject;
            if (mo == null) return false;

            if (ctx.CurrentKeyCode == KeyCode.Escape)
            {
                Reset();
                ctx.Repaint?.Invoke();
                return true;
            }

            switch (Mode)
            {
                case KnifeMode.Erase:     return HandleEraseClick(ctx, mousePos);
                case KnifeMode.BeltLoop:  return HandleBeltClick(ctx, mousePos);
                case KnifeMode.SimpleCut: return HandleSimpleCutClick(ctx, mo, mousePos);
                default:                  return HandleLadderClick(ctx, mo, mousePos);
            }
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            var mo = ctx.ActiveMeshObject;
            if (mo == null) return false;

            switch (Mode)
            {
                case KnifeMode.Erase:     UpdateEraseHover(ctx, mousePos); break;
                case KnifeMode.BeltLoop:  UpdateBeltHover(ctx, mousePos);  break;
                case KnifeMode.SimpleCut: UpdateSimpleCutHover(ctx, mo, mousePos); break;
                default:                  UpdateLadderHover(ctx, mo, mousePos); break;
            }

            ctx.Repaint?.Invoke();
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx) => Reset();
        public void OnDeactivate(ToolContext ctx) => Reset();

        // ================================================================
        // コマンド経路
        //
        // 【1 クリック = 1 コマンド】
        //   クリックが何を実行するかだけを TryTakeCutFromClick で決め、実行は
        //   コマンドの受け口（Execute*FromCommand）が行う。段の途中のクリックは
        //   段だけ進めて false を返す。
        //   TryTakeCutFromClick は Handle*Click と同じ段の更新を行うため、
        //   呼び出し側はどちらか一方だけを使うこと。
        // ================================================================

        /// <summary>クリックで確定した切断の内容。モードごとに使うフィールドが違う。</summary>
        public struct KnifeCutRequest
        {
            public KnifeMode Mode;

            /// <summary>LadderCut の開始頂点。</summary>
            public int StartVertex;
            /// <summary>LadderCut の終了頂点。</summary>
            public int EndVertex;
            /// <summary>LadderCut のセグメント辺 / BeltLoop / Erase の辺。</summary>
            public VertexPair Edge;
            /// <summary>LadderCut / BeltLoop の辺上の切る位置（Edge.V1 起点）。</summary>
            public float CutRatio;

            /// <summary>SimpleCut の 1 点目（ビューポート座標、Y=0 下）。</summary>
            public Vector2 ScreenP0;
            /// <summary>SimpleCut の 2 点目。</summary>
            public Vector2 ScreenP1;
            /// <summary>SimpleCut の面カリングマスク。null で全面対象。</summary>
            public bool[] FaceCulledMask;
        }

        /// <summary>
        /// このクリックが実行する切断を決めて返す。実行はしない。
        /// </summary>
        /// <param name="mousePosImgui">IMGUI 系（Y=0 上）の位置。Ladder / Belt / Erase 用。</param>
        /// <param name="screenPos">ビューポート座標（Y=0 下）。SimpleCut 用。</param>
        /// <returns>実行すべき切断が決まったら true。</returns>
        public bool TryTakeCutFromClick(
            ToolContext ctx, Vector2 mousePosImgui, Vector2 screenPos, out KnifeCutRequest req)
        {
            req = default;
            req.Mode = Mode;

            var mo = ctx?.ActiveMeshObject;
            if (mo == null) return false;

            if (ctx.CurrentKeyCode == KeyCode.Escape)
            {
                Reset();
                ctx.Repaint?.Invoke();
                return false;
            }

            switch (Mode)
            {
                case KnifeMode.Erase:
                {
                    var e = FindNearestSharedEdge(ctx, mo, mousePosImgui, out var faces);
                    if (!e.HasValue || faces == null || faces.Count != 2) return false;
                    req.Edge = e.Value;
                    return true;
                }

                case KnifeMode.BeltLoop:
                {
                    var e = ResolveEdge(ctx, mousePosImgui);
                    if (!e.HasValue) return false;
                    req.Edge     = e.Value;
                    req.CutRatio = ComputeClickRatio(ctx, mo, e.Value, mousePosImgui);
                    return true;
                }

                case KnifeMode.SimpleCut:
                {
                    if (_simpleStage == SimpleStage.Idle)
                    {
                        // 1 点目。HandleSimpleCutClick と同じ更新だけを行う。
                        _simpleP0    = screenPos;
                        _simpleStage = SimpleStage.HasP0;
                        LastError    = "";
                        ctx.Repaint?.Invoke();
                        return false;
                    }

                    req.ScreenP0       = _simpleP0;
                    req.ScreenP1       = screenPos;
                    req.FaceCulledMask = _simpleFaceCulledMask;
                    Reset();
                    ctx.Repaint?.Invoke();
                    return true;
                }

                default:
                    return TryTakeLadderFromClick(ctx, mo, mousePosImgui, ref req);
            }
        }

        /// <summary>
        /// ラダー切断の段を進め、3 段目で確定内容を返す。
        /// 段の進め方と検証は HandleLadderClick と同じ。
        /// </summary>
        private bool TryTakeLadderFromClick(
            ToolContext ctx, MeshObject mo, Vector2 mousePos, ref KnifeCutRequest req)
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
                    return false;
                }
                case LadderStage.HasStart:
                {
                    var e = ResolveEdge(ctx, mousePos);
                    if (!e.HasValue) return false;
                    if (e.Value.Contains(_startVertex))
                    { LastError = T("ErrSegAdjacent"); ctx.Repaint?.Invoke(); return false; }
                    if (!LadderCutResolver.IsSegmentReachable(mo, _startVertex, e.Value))
                    { LastError = T("ErrSegUnreachable"); ctx.Repaint?.Invoke(); return false; }
                    _segment = e.Value;
                    _hasSegment = true;
                    _cutRatio = ComputeClickRatio(ctx, mo, _segment, mousePos);
                    _stage = LadderStage.HasSegment;
                    LastError = "";
                    ctx.Repaint?.Invoke();
                    return false;
                }
                case LadderStage.HasSegment:
                {
                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0) return false;

                    // 解決できるかをここで確かめる。失敗しても段は維持する
                    // （HandleLadderClick と同じく別の終了頂点を選べる）。
                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v, _cutRatio, _segment.V1);
                    if (!plan.Ok)
                    {
                        LastError = plan.Error;
                        ctx.Repaint?.Invoke();
                        return false;
                    }

                    req.StartVertex = _startVertex;
                    req.Edge        = _segment;
                    req.EndVertex   = v;
                    req.CutRatio    = _cutRatio;

                    Reset();
                    ctx.Repaint?.Invoke();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ラダー切断をコマンドから実行する。マウス経路と同じ
        /// LadderCutResolver.Resolve → NCutExecutor / LadderCutExecutor を通す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteLadderCutFromCommand(
            ToolContext ctx, int startVertex, VertexPair segment, int endVertex,
            float cutRatio, bool equalDivide, int divisions, out string reason)
        {
            reason = null;

            var mo = ctx?.ActiveMeshObject;
            if (mo == null) { reason = "編集対象メッシュがありません"; return false; }
            if (!InRange(mo, startVertex) || !InRange(mo, endVertex) ||
                !InRange(mo, segment.V1) || !InRange(mo, segment.V2))
            { reason = "頂点番号が範囲外です"; return false; }
            if (segment.Contains(startVertex))
            { reason = "セグメント辺は開始頂点に隣接しない辺を指定してください"; return false; }
            if (!LadderCutResolver.IsSegmentReachable(mo, startVertex, segment))
            { reason = "そのセグメント辺は開始頂点の四角形へ届きません"; return false; }
            if (equalDivide && divisions < 2)
            { reason = "Divisions は 2 以上にしてください"; return false; }

            var plan = LadderCutResolver.Resolve(
                mo, startVertex, segment, endVertex, Mathf.Clamp01(cutRatio), segment.V1);
            if (!plan.Ok)
            { reason = string.IsNullOrEmpty(plan.Error) ? "切断経路を解決できません" : plan.Error; return false; }

            if (equalDivide) NCutExecutor.Execute(ctx, mo, plan, divisions);
            else             LadderCutExecutor.Execute(ctx, mo, plan);

            ctx.NotifyTopologyChanged?.Invoke();
            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 一意分割をコマンドから実行する。マウス経路と同じ
        /// BeltCutResolver.Resolve → NCutExecutor / LadderCutExecutor を通す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteBeltLoopCutFromCommand(
            ToolContext ctx, VertexPair edge, float cutRatio,
            bool equalDivide, int divisions, out string reason)
        {
            reason = null;

            var mo = ctx?.ActiveMeshObject;
            if (mo == null) { reason = "編集対象メッシュがありません"; return false; }
            if (!InRange(mo, edge.V1) || !InRange(mo, edge.V2) || edge.V1 == edge.V2)
            { reason = "頂点番号が不正です"; return false; }
            if (equalDivide && divisions < 2)
            { reason = "Divisions は 2 以上にしてください"; return false; }

            var plan = BeltCutResolver.Resolve(mo, edge, Mathf.Clamp01(cutRatio), edge.V1);
            if (!plan.Ok || plan.FaceCuts.Count == 0)
            { reason = "その辺からベルト／ループをたどれません"; return false; }

            if (equalDivide) NCutExecutor.Execute(ctx, mo, plan, divisions);
            else             LadderCutExecutor.Execute(ctx, mo, plan);

            ctx.NotifyTopologyChanged?.Invoke();
            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 辺消去をコマンドから実行する。
        /// 対象の 2 面は辺から引き直す。Undo の取り方はマウス経路と同じ
        /// （MeshObjectSnapshot の前後差分）。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteEraseEdgeFromCommand(
            ToolContext ctx, VertexPair edge, out string reason)
        {
            reason = null;

            var mo = ctx?.ActiveMeshObject;
            if (mo == null) { reason = "編集対象メッシュがありません"; return false; }
            if (!InRange(mo, edge.V1) || !InRange(mo, edge.V2) || edge.V1 == edge.V2)
            { reason = "頂点番号が不正です"; return false; }

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            if (!edgeToFaces.TryGetValue(edge, out var faces) || faces == null || faces.Count != 2)
            { reason = $"頂点 {edge.V1} と {edge.V2} を結ぶ辺が 2 面に共有されていません"; return false; }

            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : null;

            MergeFaces(mo, faces[0], faces[1], edge);
            ctx.SyncMesh?.Invoke();
            ctx.NotifyTopologyChanged?.Invoke();

            if (ctx.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                ctx.UndoController.RecordMeshTopologyChange(before, after, "Knife Erase Edge");
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// シンプル切断をコマンドから実行する。
        ///
        /// p0 / p1 は「頂点投影と同じ座標系（Y=0 下・原点左下）」で渡すこと。
        /// マウス経路と同じ SimpleCutExecutor.Execute を通す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteSimpleCutFromCommand(
            ToolContext ctx, Vector2 p0, Vector2 p1,
            bool[] faceCulledMask, bool triQuad, out string reason)
        {
            reason = null;

            var mo = ctx?.ActiveMeshObject;
            if (mo == null) { reason = "編集対象メッシュがありません"; return false; }

            if (faceCulledMask != null && faceCulledMask.Length != 0 &&
                faceCulledMask.Length != mo.FaceCount)
            { reason = $"FaceCulledMask の長さは面数（{mo.FaceCount}）に合わせてください"; return false; }

            bool[] mask = (faceCulledMask != null && faceCulledMask.Length == mo.FaceCount)
                ? faceCulledMask : null;

            SimpleCutExecutor.Execute(ctx, mo, p0, p1, mask, triQuad);
            ctx.NotifyTopologyChanged?.Invoke();
            ctx.Repaint?.Invoke();
            return true;
        }

        private static bool InRange(MeshObject mo, int v) => v >= 0 && v < mo.VertexCount;

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
            ResetSimpleCut();
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
                        _preview.Lines.Add((VW(ctx, mo, e.Value.V1), VW(ctx, mo, e.Value.V2)));
                    break;
                }
                case LadderStage.HasSegment:
                {
                    _preview.DotVertices.Add(_startVertex);
                    // セグメントをハイライト
                    _preview.Lines.Add((VW(ctx, mo, _segment.V1), VW(ctx, mo, _segment.V2)));

                    int v = ResolveVertex(ctx, mousePos);
                    if (v < 0 || v == _startVertex) break;

                    var plan = LadderCutResolver.Resolve(mo, _startVertex, _segment, v, _cutRatio, _segment.V1);
                    if (!plan.Ok) { _preview.PlanValid = false; break; }

                    _preview.PlanValid = true;
                    if (EqualDivide)
                        BuildEqualDividePolylines(ctx, mo, plan, v, Divisions);
                    else
                        BuildPlanPolyline(ctx, mo, plan, v);
                    _preview.DotVertices.Add(v);
                    break;
                }
            }
        }

        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //    ワールド座標は ToolContext.GetVertexWorldPosition、
        //    クリップ空間 w は ToolContext.GetVertexClipW を経由する
        //    （実体は PlayerViewportManager.TryGetVertexWorld / TryGetVertexClipW）。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        //
        // 4. スキンドメッシュに追加する頂点には BoneWeight が必須である。
        //    BoneWeight を持たない頂点は GPU 側でメッシュ自身の context 索引を使い
        //    （UnifiedBufferManager_Build.cs:356-362）、周囲の頂点と別の行列で
        //    変換されてその頂点だけ位置がずれる。
        // ================================================================

        /// <summary>
        /// クリック点をセグメント（V1→V2）の画面投影線上へ射影して比率 t を返す。
        /// t は V1 起点（0=V1, 1=V2）。投影不能時は 0.5。端の退化回避で 0.02..0.98 にクランプ。
        /// </summary>
        // 【一時診断】原因特定後に削除する。
        private const string nullName = "null";

        private float ComputeClickRatio(ToolContext ctx, MeshObject mo, VertexPair seg, Vector2 mousePosImgui)
        {
            if (ctx == null || ctx.WorldToScreenPos == null) return 0.5f;
            if (seg.V1 < 0 || seg.V2 < 0 || seg.V1 >= mo.VertexCount || seg.V2 >= mo.VertexCount) return 0.5f;

            // 【Y 反転を足さないこと】
            // LocalToScreen の戻り値は mousePosImgui と同じ Y 系である
            // （PlayerToolContext.WorldToScreenPos が previewRect.height - py 済み）。
            // ここで h - y を掛けると二重反転になり、比率が線分の外側へ飛ぶ。
            //
            // 引数 2 個の LocalToScreen（頂点単位）を使う。1 個版はメッシュの WorldMatrix を
            // 掛けるだけで、スキンド頂点が GPU で実際に受ける行列と一致しない。
            Vector2 s1 = ctx.LocalToScreen(seg.V1, mo.Vertices[seg.V1].Position);
            Vector2 s2 = ctx.LocalToScreen(seg.V2, mo.Vertices[seg.V2].Position);

            Vector2 d = s2 - s1;
            float len2 = d.sqrMagnitude;
            if (len2 < 1e-6f) return 0.5f;
            float tScreen = Vector2.Dot(mousePosImgui - s1, d) / len2;

            // ここで求まる t はスクリーン空間の線形パラメータ。
            // 3D 空間の線形パラメータへ透視補正する（シンプルカットと共通）。
            // クランプは補正の後に行う。先にクランプすると補正で範囲外へ出る。
            float tGeom = SimpleCutExecutor.ScreenUToGeomT(ctx, seg.V1, seg.V2, tScreen);

            float tFinal = tGeom;
            if (tFinal < 0.02f) tFinal = 0.02f; else if (tFinal > 0.98f) tFinal = 0.98f;

            // 【一時診断】原因特定後にこのブロックを削除する。
            {
                var wA = ctx.GetVertexClipW?.Invoke(seg.V1);
                var wB = ctx.GetVertexClipW?.Invoke(seg.V2);
                string wAs = wA.HasValue ? wA.Value.ToString("F4") : nullName;
                string wBs = wB.HasValue ? wB.Value.ToString("F4") : nullName;
                string hook = ctx.GetVertexClipW == null ? "unwired" : "wired";
                Debug.Log(
                    $"[LadderRatio] seg=({seg.V1},{seg.V2}) s1={s1} s2={s2} mouse={mousePosImgui} " +
                    $"hook={hook} tScreen={tScreen:F4} wA={wAs} wB={wBs} " +
                    $"tGeom={tGeom:F4} tFinal={tFinal:F4} rect={ctx.PreviewRect}");
            }

            return tFinal;
        }

        /// <summary>
        /// 頂点のワールド座標を返す。GPU が計算した値（ctx.GetVertexWorldPosition）を使う。
        /// 取れない場合のみ ActiveWorldMatrix でローカルから変換する。
        /// KnifePreview.Lines / DotWorld はこの値で構築する。ローカル座標を入れないこと。
        /// </summary>
        private static Vector3 VW(ToolContext ctx, MeshObject mo, int vi)
        {
            if (mo == null || vi < 0 || vi >= mo.Vertices.Count) return Vector3.zero;

            var w = ctx?.GetVertexWorldPosition?.Invoke(vi);
            if (w.HasValue) return w.Value;

            return (ctx?.ActiveWorldMatrix ?? Matrix4x4.identity)
                .MultiplyPoint3x4(mo.Vertices[vi].Position);
        }

        /// <summary>
        /// 計画から切断線（開始頂点→各ラング中点→終了頂点）を構築する。
        /// </summary>
        private void BuildPlanPolyline(ToolContext ctx, MeshObject mo, LadderCutPlan plan, int endVertex)
        {
            // 順序付きの切断点列を作る（すべてワールド座標。GPU の値を使う）
            var pts = new List<Vector3>();
            pts.Add(VW(ctx, mo, _startVertex));
            foreach (var rung in plan.Rungs)
            {
                float t = 0.5f;
                if (plan.RungParams.TryGetValue(rung, out var rp))
                    t = (rp.AnchorVertex == rung.V2) ? (1f - rp.Ratio) : rp.Ratio;
                var cut = Vector3.Lerp(VW(ctx, mo, rung.V1), VW(ctx, mo, rung.V2), t);
                pts.Add(cut);
                _preview.DotWorld.Add(cut);
            }
            pts.Add(VW(ctx, mo, endVertex));

            for (int i = 0; i < pts.Count - 1; i++)
                _preview.Lines.Add((pts[i], pts[i + 1]));
        }

        /// <summary>
        /// 等分割プレビュー: N-1 本の折れ線（開始頂点→各 rung の i/N 点→終了頂点）。
        /// 各 rung の向きは RungParams のアンカーで揃える（実切断と同じ側）。
        /// </summary>
        private void BuildEqualDividePolylines(ToolContext ctx, MeshObject mo, LadderCutPlan plan, int endVertex, int divisions)
        {
            int cuts = Mathf.Max(1, divisions - 1);
            for (int i = 1; i <= cuts; i++)
            {
                float r = (float)i / divisions;
                var pts = new List<Vector3>();
                pts.Add(VW(ctx, mo, _startVertex));
                foreach (var rung in plan.Rungs)
                {
                    float t = r;
                    if (plan.RungParams.TryGetValue(rung, out var rp) && rp.AnchorVertex == rung.V2)
                        t = 1f - r;
                    var cut = Vector3.Lerp(VW(ctx, mo, rung.V1), VW(ctx, mo, rung.V2), t);
                    pts.Add(cut);
                    _preview.DotWorld.Add(cut);
                }
                pts.Add(VW(ctx, mo, endVertex));
                for (int j = 0; j < pts.Count - 1; j++)
                    _preview.Lines.Add((pts[j], pts[j + 1]));
            }
        }
    }
}
