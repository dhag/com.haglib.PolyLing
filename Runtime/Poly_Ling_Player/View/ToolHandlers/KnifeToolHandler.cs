// KnifeToolHandler.cs
// KnifeTool（一新版）を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// ヒットテスト・巡回はインデックスベース（AdvancedSelectToolHandler と同方式、TopologyCache を構築）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class KnifeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly KnifeTool _tool = new KnifeTool();
        private          ProjectContext _project;

        // TopologyCache はメッシュごとにキャッシュ
        private readonly Dictionary<int, TopologyCache> _topoCaches = new Dictionary<int, TopologyCache>();

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;
        /// <summary>GPU ホバー結果取得（互換のため保持。現実装は SelectionHelper を使用）。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;

        /// <summary>クリック確定時に発火（クリック点強調フラッシュ用）。</summary>
        public Action OnClicked;

        /// <summary>直近クリックで GPU ホバー確定した頂点（辺ヒット時は端点 V1）。未ヒットは -1。</summary>
        public int LastClickVertex { get; private set; } = -1;

        /// <summary>直近クリックで GPU ホバー確定した辺（辺ヒット時のみ）。頂点時は null。</summary>
        public Poly_Ling.Selection.VertexPair? LastClickEdge { get; private set; }

        // ================================================================
        // 設定公開 API（サブパネル用）
        // ================================================================

        public KnifeMode Mode { get => _tool.Mode; set => _tool.Mode = value; }

        /// <summary>状態説明テキスト（サブパネル用）。</summary>
        public string StageText() => _tool.StageText();

        /// <summary>プレビュー（オーバーレイ描画用）。</summary>
        public KnifeTool.KnifePreview GetPreview() => _tool.Preview;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        public void SetCommandQueue(CommandQueue queue)        => _commandQueue   = queue;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildCtx(mods, screenPos); if (ctx == null) return;
            InjectGpuHover();
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            ApplyHoverSelectionMode();   // 段遷移後の必要型に合わせて GPU ホバーモードを更新
            OnRepaint?.Invoke();
            OnClicked?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // クリック指定のみ。ドラッグ開始は単発クリックと同じ扱い。
            OnLeftClick(hit, screenPos, mods);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }

        public void UpdateHover(Vector2 screenPos, ToolContext baseCtx)
        {
            var ctx = EnrichCtx(baseCtx, default, screenPos); if (ctx == null) return;
            InjectGpuHover();
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), Vector2.zero);
            OnRepaint?.Invoke();
        }

        public void Activate(ToolContext ctx)
        {
            EnrichCtx(ctx, default, Vector2.zero);
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) => _tool.OnDeactivate(ctx);

        /// <summary>進行中の切断（アンカー/セグメント）を破棄する。Escape キャンセル用。</summary>
        public void Cancel()
        {
            _tool.Reset();
            ApplyHoverSelectionMode();   // Idle（開始頂点段）＝ Vertex ホバーへ戻す
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// ナイフの現在段に応じてホバー用選択モードを設定する。
        /// 開始/終了頂点の段は Vertex、セグメント（辺）の段は Edge。
        /// GPU ホバーが必要な型を返すようにし、CPU フォールバックへ落ちるのを防ぐ。
        /// 起動時は Viewer が一度呼ぶ。以降はクリック/キャンセルで更新する。
        /// </summary>
        public void ApplyHoverSelectionMode()
        {
            var sel = _project?.CurrentModel?.FirstSelectedMeshContext?.Selection;
            if (sel == null) return;
            sel.Mode = _tool.NextClickIsEdge
                ? Poly_Ling.Selection.MeshSelectMode.Edge
                : Poly_Ling.Selection.MeshSelectMode.Vertex;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 screenPos)
        {
            var ctx = GetToolContext?.Invoke();
            return EnrichCtx(ctx, mods, screenPos);
        }

        /// <summary>
        /// GPU ホバー由来の頂点・辺（メッシュローカル）を確定し、KnifeTool に注入する。
        /// 操作対象メッシュ（FirstSelected）と一致するホバーのみ採用。未ヒットは -1/null。
        /// あわせてフラッシュ強調用の LastClick 情報を現在ステージに応じて設定する。
        /// </summary>
        private void InjectGpuHover()
        {
            int gpuVertex = -1;
            Poly_Ling.Selection.VertexPair? gpuEdge = null;

            if (GetHoverElement != null)
            {
                int firstIdx = _project?.CurrentModel?.FirstSelectedIndex ?? -1;

                var vElem = GetHoverElement(MeshSelectMode.Vertex);
                if (firstIdx >= 0 && vElem.MeshIndex == firstIdx && vElem.Kind == PlayerHoverKind.Vertex)
                    gpuVertex = vElem.VertexIndex;

                var eElem = GetHoverElement(MeshSelectMode.Edge);
                if (firstIdx >= 0 && eElem.MeshIndex == firstIdx && eElem.Kind == PlayerHoverKind.Edge)
                    gpuEdge = new Poly_Ling.Selection.VertexPair(eElem.EdgeV1, eElem.EdgeV2);
            }

            _tool.SetGpuHover(gpuVertex, gpuEdge);

            // フラッシュ強調用：このクリックが辺対象か頂点対象かで見せる要素を決める。
            if (_tool.NextClickIsEdge)
            {
                LastClickEdge   = gpuEdge;
                LastClickVertex = gpuEdge.HasValue ? gpuEdge.Value.V1 : -1;
            }
            else
            {
                LastClickEdge   = null;
                LastClickVertex = gpuVertex;
            }
        }

        private ToolContext EnrichCtx(ToolContext ctx, ModifierKeys mods, Vector2 screenPos)
        {
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            if (model == null) return null;

            ctx.Model          = model;
            ctx.SelectionState = model.FirstSelectedMeshContext?.Selection;
            ctx.SelectedVertices = model.FirstSelectedMeshContext?.SelectedVertices;
            ctx.UndoController = _undoController;
            ctx.CommandQueue   = _commandQueue;
            ctx.Repaint        = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            // トポロジ変更は NotifyTopologyChanged 一本で再構築する（二重リビルド回避）。
            ctx.SyncMesh = null;
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPos, ctx),
            };
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;

            // TopologyCache（メッシュ変更時に自動再構築）— AdvancedSelectToolHandler と同方式
            var mc = model.FirstSelectedMeshContext;
            if (mc?.MeshObject != null)
            {
                int key = mc.MeshObject.GetHashCode();
                if (!_topoCaches.TryGetValue(key, out var topo))
                {
                    topo = new TopologyCache(mc.MeshObject);
                    _topoCaches[key] = topo;
                }
                else
                {
                    topo.SetMeshObject(mc.MeshObject);
                }
                ctx.TopologyCache = topo;
            }

            return ctx;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
