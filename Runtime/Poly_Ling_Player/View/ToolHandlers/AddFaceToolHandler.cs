// AddFaceToolHandler.cs
// AddFaceTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class AddFaceToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly AddFaceTool _tool = new AddFaceTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>面追加後のGPUバッファ再構築コールバック（ViewerCoreから設定）</summary>
        public Action NotifyTopologyChanged;
        /// <summary>
        /// クリック時にモデル・描画メッシュがなければ自動生成するコールバック。
        /// true を返したら生成成功（以降の処理を続行）、false なら失敗（処理中断）。
        /// </summary>
        public Func<bool> EnsureDrawableMesh;
        /// <summary>点が配置されるたびに呼ばれる（SubPanel更新用）</summary>
        public Action OnPointPlaced;
        /// <summary>GLギズモ描画用: 描画対象カメラのツールコンテキストを返す</summary>
        public Func<Camera, ToolContext> GetGizmoContext;

        /// <summary>
        /// コマンド送信口。クリック確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// PivotOffsetToolHandler.SendCommand と同じ役割。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>GPU ホバー要素取得（Viewer から結線）。既存頂点スナップに使う。</summary>
        public Func<Poly_Ling.Selection.MeshSelectMode, PlayerHoverElement> GetHoverElement;

        /// <summary>
        /// 操作対象メッシュの頂点について、GPU が計算したワールド座標を返す
        /// （Viewer から PlayerViewportManager.TryGetVertexWorld を結線）。
        /// 表裏判定でスキニング後の座標が要るため。CPU で計算し直さないこと。
        /// </summary>
        public Func<int, UnityEngine.Vector3?> GetVertexWorldPosition;

        /// <summary>
        /// 任意メッシュの頂点について、GPU が計算したワールド座標を返す
        /// （Viewer から PlayerViewportManager.TryGetVertexWorld を結線）。
        /// 引数は (MeshContextList インデックス, メッシュ内ローカル頂点番号)。
        /// GetVertexWorldPosition は操作対象メッシュ固定なので他メッシュには使えない。
        /// 他オブジェクトの頂点への吸着で使う。
        /// </summary>
        public Func<int, int, UnityEngine.Vector3?> GetMeshVertexWorldPosition;

        /// <summary>
        /// 非選択オブジェクトも対象にした吸着用ホバー要素を返す
        /// （Viewer から PlayerViewportManager.GetSnapHoverElement を結線）。
        /// 通常ホバー（GetHoverElement）が選択メッシュしか返さないため、
        /// 非選択オブジェクトへ吸着したい場合だけこちらを使う。
        /// Viewer 側で SetSnapHitTestEnabled(true) にしていないと常に未ヒット。
        /// </summary>
        public Func<PlayerHoverElement> GetSnapHoverElement;

        /// <summary>
        /// 吸着用ヒットテストの有効/無効を Viewer へ伝えるコールバック
        /// （PlayerViewportManager.SetSnapHitTestEnabled を結線）。
        /// </summary>
        public Action<bool> OnSnapHitTestEnabledChanged;

        private bool _snapToUnselected;

        /// <summary>
        /// 非選択オブジェクトの頂点にも吸着するか。既定 false。
        /// true の間だけ GPU 側で追加のヒットテストと頂点数ぶんの読み戻しが走る。
        /// </summary>
        public bool SnapToUnselectedObjects
        {
            get => _snapToUnselected;
            set
            {
                if (_snapToUnselected == value) return;
                _snapToUnselected = value;
                OnSnapHitTestEnabledChanged?.Invoke(value);
            }
        }

        // ================================================================
        // 設定公開API
        // ================================================================

        public AddFaceMode ModePublic    { get => _tool.ModePublic;    set => _tool.ModePublic = value; }
        public bool ContinuousLinePublic { get => _tool.ContinuousLinePublic; set => _tool.ContinuousLinePublic = value; }
        public int  PlacedPointCount     => _tool.PlacedPointCount;
        public int  RequiredPointsPublic => _tool.RequiredPointsPublic;
        public void ClearPointsPublic()  => _tool.ClearPointsPublic();
        public System.Collections.Generic.List<string> GetPointLabels() => _tool.GetPointLabels();
        public AddFaceTool.AddFacePreviewData GetPreviewData() => _tool.GetPreviewData();

        /// <summary>
        /// Quad モードで3点配置済みのとき、その3点で三角形を確定する。
        /// 右クリック／Escape から呼ぶ。確定したら true。
        /// </summary>
        public bool FinishAsTriangle()
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return false;

            if (SendCommand != null)
            {
                if (!_tool.TryTakePointsForTriangleFinish(out var pts)) return false;
                var cmd = BuildAddFaceCommand(ctx, pts);
                if (cmd == null) return false;
                SendCommand(cmd);
                OnPointPlaced?.Invoke();
                return true;
            }

            if (!_tool.FinishAsTriangle(ctx)) return false;
            OnPointPlaced?.Invoke();
            return true;
        }

        /// <summary>
        /// 直前に指定した点を 1 つ取り消す。Backspace / Delete から呼ぶ。
        /// 1 点も指定されていなければ何もせず false。
        /// </summary>
        public bool RemoveLastPoint()
        {
            if (!_tool.RemoveLastPoint()) return false;
            OnPointPlaced?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 線分モードの描画を終了し、次の描画を始められる状態へ戻す。
        /// Escape / 右クリックから呼ぶ。終了するものが無ければ false。
        /// </summary>
        public bool FinishLineChain()
        {
            if (!_tool.FinishLineChain()) return false;
            OnPointPlaced?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        /// <summary>
        /// 連続線分で確定済みの線分を 1 本取り消す。Delete / Backspace から呼ぶ。
        ///
        /// 【Undo 1 回ぶんで戻す】
        ///   線分は 1 本ごとに独立した Undo 記録になっている
        ///   （MeshUndoController.RecordAddFaceOperation が EndGroup してから記録する）ので、
        ///   面と新規頂点の削除は PerformUndoCommand に任せ、
        ///   折れ線の状態合わせだけをこちらで行う。
        ///   線分を引いた後に別の操作をしていると、その操作の方が戻る。
        ///
        /// 取り消せる線分が無ければ何もせず false。
        /// </summary>
        public bool UndoLastLineSegment()
        {
            if (SendCommand == null) return false;
            if (!_tool.CanUndoLineSegment()) return false;

            SendCommand(new Poly_Ling.Data.PerformUndoCommand());

            // Undo でメッシュが変わった後の内容で終点を取り直す。
            _tool.NotifyLineSegmentUndone(GetEnrichedCtx());
            OnPointPlaced?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        /// <summary>
        /// クリック確定。
        ///
        /// 【1 クリック = 1 コマンド】
        ///   送信口があるときは TryTakePointsFromClick で点の追加と確定判定だけを
        ///   行い、面の生成はコマンドの受け口へ寄せる。点が揃っていないクリックでは
        ///   何も送らない。
        ///   TryTake は OnMouseDown と同じ点列の更新を行うため、両方を呼ぶと
        ///   点が二重に入る。どちらか一方だけを通すこと。
        /// </summary>
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            ResolveGpuHoverVertex();

            if (HandlePressViaCommand(ctx, screenPos)) return;

            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            OnPointPlaced?.Invoke();
        }

        /// <summary>
        /// 押下 1 回ぶんをコマンド経路で処理する。
        ///
        /// クリックとドラッグ開始のどちらも「押した瞬間に点を置く」ので
        /// （PlayerVertexInteractor はしきい値でどちらかへ振り分ける）、
        /// 両方から同じここを通す。塞がないとドラッグ側だけ旧経路が走り、
        /// 点が二重に入る。
        /// </summary>
        /// <returns>コマンド経路で処理したら true。呼び出し側は旧経路を通さない。</returns>
        private bool HandlePressViaCommand(ToolContext ctx, Vector2 screenPos)
        {
            if (SendCommand == null) return false;

            if (_tool.TryTakePointsFromClick(ctx, ToImgui(screenPos, ctx), out var pts))
            {
                var cmd = BuildAddFaceCommand(ctx, pts);
                if (cmd != null) SendCommand(cmd);
            }
            OnPointPlaced?.Invoke();
            return true;
        }

        // ================================================================
        // コマンド経路
        // ================================================================

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            var model = _project?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc == null) return null;
            return new[] { model.IndexOf(mc) };
        }

        /// <summary>
        /// 取り出した点列からコマンドを組み立てる。
        /// 巻き順の判定に使う視点は、確定した瞬間のカメラ位置をそのまま載せる。
        /// </summary>
        private Poly_Ling.Data.AddFaceCommand BuildAddFaceCommand(
            ToolContext ctx, Poly_Ling.Tools.PointInfo[] points)
        {
            if (points == null || points.Length < 2) return null;

            var targets = ActiveMasterIndices();
            if (targets == null) return null;

            var indices   = new int[points.Length];
            var positions = new float[points.Length * 3];
            for (int i = 0; i < points.Length; i++)
            {
                indices[i] = points[i].IsExistingVertex ? points[i].ExistingVertexIndex : -1;
                positions[i * 3]     = points[i].Position.x;
                positions[i * 3 + 1] = points[i].Position.y;
                positions[i * 3 + 2] = points[i].Position.z;
            }

            return new Poly_Ling.Data.AddFaceCommand(
                _project?.CurrentModelIndex ?? 0,
                targets,
                _tool.ModePublic,
                indices, positions,
                ctx.CurrentMaterialIndex,
                ctx.CameraPosition);
        }

        /// <summary>
        /// 面追加コマンドを実行する。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   面の生成・頂点の追加・法線・Undo 記録は
        ///   AddFaceTool.CreateFaceFromCommand がマウス経路と同じ CreateFace を通す。
        ///
        /// 【設定値はコマンドが正典】
        ///   材質番号と視点は退避してからコマンド値を ctx へ入れ、終わったら戻す。
        ///   1 呼び出しがパネルやカメラの状態に依存しないようにするため。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.AddFaceCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var idx = cmd.PointVertexIndices;
            var pos = cmd.PointPositions;
            if (idx == null || idx.Length < 2 || idx.Length > 4)
            { reason = "点は 2〜4 個で指定してください"; return false; }
            if (pos == null || pos.Length != idx.Length * 3)
            { reason = $"PointPositions の長さは点数の 3 倍にしてください（{idx.Length * 3} 個）"; return false; }

            int required = (int)cmd.Mode;
            bool countOk = cmd.Mode == Poly_Ling.Tools.AddFaceMode.Quad
                ? (idx.Length == 3 || idx.Length == 4)
                : (idx.Length == required);
            if (!countOk)
            { reason = $"{cmd.Mode} には点が {required} 個要ります"; return false; }

            var ctx = GetEnrichedCtx();
            if (ctx == null) { reason = "ビューポートがありません"; return false; }

            var points = new Poly_Ling.Tools.PointInfo[idx.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                var p = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                points[i] = idx[i] >= 0
                    ? Poly_Ling.Tools.PointInfo.FromExisting(idx[i], p)
                    : Poly_Ling.Tools.PointInfo.FromNew(p);
            }

            var savedMode     = _tool.ModePublic;
            int savedMaterial = ctx.CurrentMaterialIndex;
            var savedCamera   = ctx.CameraPosition;
            try
            {
                _tool.ModePublic          = cmd.Mode;
                ctx.CurrentMaterialIndex  = cmd.MaterialIndex;
                ctx.CameraPosition        = cmd.ViewPosition;

                if (!_tool.CreateFaceFromCommand(ctx, points, out reason)) return false;
            }
            finally
            {
                _tool.ModePublic         = savedMode;
                ctx.CurrentMaterialIndex = savedMaterial;
                ctx.CameraPosition       = savedCamera;
            }

            OnPointPlaced?.Invoke();
            OnRepaint?.Invoke();
            return true;
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            ResolveGpuHoverVertex();

            if (HandlePressViaCommand(ctx, screenPos)) return;

            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            EnrichCtxForHover(ctx);
            ResolveGpuHoverVertex();
            // UpdateHover に渡される screenPos は GPU Y（Y=0下）。
            // PlayerViewportManager.NotifyPointerHover が ToHandlerHoverPos で
            // パネルローカル（Y=0上）から反転してから渡してくるため、
            // クリック／ドラッグ経路と同じく ToImgui で IMGUI Y（Y=0上）へ戻す。
            // ここを素通しにすると ScreenPosToRay で二重反転し、候補点が上下逆に動く。
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), Vector2.zero);
        }

        /// <summary>Camera.onPostRenderから呼ぶ: GLギズモをRenderTextureに描画</summary>
        public void DrawGizmoForCamera(Camera cam)
        {
            var ctx = GetGizmoContext?.Invoke(cam);
            if (ctx == null) return;
            EnrichCtxForHover(ctx);
            _tool.DrawGizmo(ctx);
        }

        private void EnrichCtxForHover(ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
            ctx.Repaint          = OnRepaint;
            ApplyWorkPlane(ctx);
        }
        public void Activate(ToolContext ctx)
        {
            _tool.OnActivate(ctx);
            OnSnapHitTestEnabledChanged?.Invoke(_snapToUnselected);
        }

        public void Deactivate(ToolContext ctx)
        {
            _tool.OnDeactivate(ctx);
            // 他モードへ移る際は必ず切る。切り忘れるとポインタ移動ごとに
            // 頂点数ぶんの読み戻しが走り続ける。
            OnSnapHitTestEnabledChanged?.Invoke(false);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;

        /// <summary>
        /// GPU ホバー由来の既存頂点を問い合わせて tool に渡す。CPU 探索は使わない。
        ///
        /// 【3 経路の使い分け】
        ///   操作対象メッシュ（ActiveMeshIndex）にヒット
        ///     → 頂点番号をそのまま渡す。面の頂点として既存頂点が再利用される。
        ///   それ以外の選択メッシュにヒット
        ///     → 頂点番号は意味が違うので使えない。GPU のワールド座標だけを渡し、
        ///       操作対象メッシュ側には新規頂点が作られる（座標のみ一致）。
        ///   非選択オブジェクトにヒット（GetSnapHoverElement 経路）
        ///     → 通常ホバーが未ヒットのときだけ参照する。扱いは上と同じくワールド座標のみ。
        ///   未ヒット
        ///     → -1 / null（スナップせず WorkPlane 交点）。
        ///
        /// 基準は FirstSelectedIndex ではなく ActiveMeshIndex を使う。
        /// ツール本体が使う ctx.ActiveMeshObject の取得元が ActiveMeshContext であり、
        /// ActiveCategory が Bone のとき FirstSelectedIndex はボーンを指してしまうため。
        /// </summary>
        private void ResolveGpuHoverVertex()
        {
            int gpuVertex = -1;
            UnityEngine.Vector3? snapWorld = null;
            bool fromUnselected = false;

            var model = _project?.CurrentModel;
            if (model != null)
            {
                int activeIdx = model.ActiveMeshIndex;

                if (GetHoverElement != null)
                {
                    ApplyHoverElement(
                        GetHoverElement(Poly_Ling.Selection.MeshSelectMode.Vertex),
                        activeIdx, ref gpuVertex, ref snapWorld);
                }

                // 通常ホバーが空振りのときだけ非選択オブジェクトを見る。
                // 選択メッシュへのヒットを優先させるため順序を入れ替えないこと。
                if (_snapToUnselected && gpuVertex < 0 && !snapWorld.HasValue
                    && GetSnapHoverElement != null)
                {
                    ApplyHoverElement(
                        GetSnapHoverElement(),
                        activeIdx, ref gpuVertex, ref snapWorld);
                    // この経路で取れた吸着座標だけが非選択オブジェクト由来。
                    fromUnselected = snapWorld.HasValue;
                }
            }

            _tool.SetGpuHoverVertex(gpuVertex);
            // 同一メッシュヒットが優先。両方を同時に立てない。
            _tool.SetGpuHoverSnapWorld(
                gpuVertex >= 0 ? (UnityEngine.Vector3?)null : snapWorld,
                fromUnselected);
        }

        /// <summary>
        /// ホバー要素を「操作対象メッシュの頂点番号」または「吸着ワールド座標」に振り分ける。
        /// </summary>
        private void ApplyHoverElement(
            PlayerHoverElement elem,
            int activeIdx,
            ref int gpuVertex,
            ref UnityEngine.Vector3? snapWorld)
        {
            if (elem.Kind != PlayerHoverKind.Vertex || elem.MeshIndex < 0) return;

            if (activeIdx >= 0 && elem.MeshIndex == activeIdx)
            {
                gpuVertex = elem.VertexIndex;
            }
            else if (GetMeshVertexWorldPosition != null)
            {
                snapWorld = GetMeshVertexWorldPosition(elem.MeshIndex, elem.VertexIndex);
            }
        }

        /// <summary>
        /// GetToolContext の戻り値に必要なフィールドを全て補完して返す。
        /// AddFaceTool の OnMouseDown/Up はこのコンテキストを使う。
        /// </summary>
        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
            // 新規面のマテリアル。ToToolContext は設定しないため、ここで補う。
            // 参照元はモデル共通のカレント値（マテリアルリストパネルと同じ）。
            if (model != null) ctx.CurrentMaterialIndex = model.CurrentMaterialIndex;
            if (_undoController?.MeshUndoContext != null)
            {
                _undoController.MeshUndoContext.OnTopologyChanged = NotifyTopologyChanged;
                _undoController.MeshUndoContext.ParentModelContext = model;
            }
            // SyncMesh は面追加後のトポロジー再構築に置き換える
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.Repaint               = OnRepaint;
            ApplyWorkPlane(ctx);
            return ctx;
        }

        /// <summary>
        /// 吸着しなかった場合の奥行きを決める WorkPlane を設定する。
        ///
        /// 面はカメラ平行（UpdateFromCamera）。原点は次の順で決める。
        ///   1. 直前に指定された点。深さをその点に合わせる。
        ///   2. 1 点も指定されていなければワールド原点。
        /// WorkPlane が null だと新規頂点がカメラから 1.5*CameraDistance の位置に置かれる。
        /// </summary>
        private void ApplyWorkPlane(ToolContext ctx)
        {
            var wp = new Poly_Ling.Context.WorkPlaneContext();
            wp.UpdateFromCamera(ctx.CameraPosition, ctx.CameraTarget);
            wp.Origin = ResolveDepthOrigin(ctx);
            ctx.WorkPlane = wp;
        }

        /// <summary>
        /// 深さの基準となるワールド座標を返す。直前の点が無ければワールド原点。
        ///
        /// 既存頂点を指す点は GPU が計算したワールド座標を使う。
        /// スキニング後の位置を CPU で計算し直すと描画とずれる。
        /// </summary>
        private Vector3 ResolveDepthOrigin(ToolContext ctx)
        {
            var last = _tool.GetLastPoint();
            if (!last.HasValue) return Vector3.zero;

            var p = last.Value;
            if (p.IsExistingVertex && GetVertexWorldPosition != null)
            {
                var w = GetVertexWorldPosition(p.ExistingVertexIndex);
                if (w.HasValue) return w.Value;
            }
            return ctx.ActiveLocalToWorld(p.Position);
        }

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 sp)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.Repaint        = OnRepaint;
            ctx.SyncMesh = () =>
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(sp, ctx),
            };
            return ctx;
        }

        private static Vector2 ToImgui(Vector2 sp, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(sp.x, h - sp.y);
        }
    }
}
