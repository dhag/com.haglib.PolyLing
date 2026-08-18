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

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            ResolveGpuHoverVertex();
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            OnPointPlaced?.Invoke();
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            ResolveGpuHoverVertex();
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
