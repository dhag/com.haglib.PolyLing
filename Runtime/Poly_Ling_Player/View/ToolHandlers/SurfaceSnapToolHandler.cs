// SurfaceSnapToolHandler.cs
// SurfaceSnapTool（面に張り付け）を Player へ橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たない。パネルの「計算」「決定」ボタンからのみ実行する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// Activate() の設定は PipeAlignToolHandler の手順書に従う。
// 本ツールは複数メッシュの頂点位置だけを書き換えるため、
// ctx.SyncMeshContextPositionsOnly（メッシュ指定の軽量更新パス）を使う。
//
// カメラは PlayerViewport.Cam の値を SurfaceSnapCamera へ写して渡す。
// Poly_Ling_Main 側にビューポート実装を持ち込まないための境界。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class SurfaceSnapToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SurfaceSnapTool _tool = new SurfaceSnapTool();
        private          ProjectContext  _project;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                  GetToolContext;
        public Action                             OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>
        /// 指定 MeshContext の全頂点ワールド座標。
        /// GPU が計算した値（GetDisplayPositions）を返す経路を配線すること。
        /// </summary>
        public Func<Poly_Ling.Data.MeshContext, Vector3[]> GetWorldPositions
        {
            get => _tool.GetWorldPositions;
            set => _tool.GetWorldPositions = value;
        }

        /// <summary>ワールド座標の再計算要求（UpdateTransform）。計算の直前に1回だけ。</summary>
        public Action OnRequestUpdateTransform
        {
            get => _tool.OnRequestUpdateTransform;
            set => _tool.OnRequestUpdateTransform = value;
        }

        /// <summary>指定種別のカメラを返す。取れなければ null。</summary>
        public Func<SurfaceSnapCameraKind, SurfaceSnapCamera?> GetCamera
        {
            get => _tool.GetCamera;
            set => _tool.GetCamera = value;
        }

        // ================================================================
        // 設定公開 API
        // ================================================================

        public SurfaceSnapCameraKind CameraKind
        {
            get => _tool.CameraKind;
            set => _tool.CameraKind = value;
        }

        public bool SelectedVerticesOnly
        {
            get => _tool.SelectedVerticesOnly;
            set => _tool.SelectedVerticesOnly = value;
        }

        public float SurfaceOffset
        {
            get => _tool.SurfaceOffset;
            set => _tool.SurfaceOffset = value;
        }

        public SurfaceSnapBackface Backface
        {
            get => _tool.Backface;
            set => _tool.Backface = value;
        }

        public IReadOnlyList<int> ReferenceIndices        => _tool.ReferenceIndices;
        public bool IsReference(int meshIndex)            => _tool.IsReference(meshIndex);
        public void SetReference(int meshIndex, bool on)  => _tool.SetReference(meshIndex, on);
        public void PruneReferences(Func<int, bool> exists) => _tool.PruneReferences(exists);

        public string LastResult      => _tool.LastResult;
        public bool   IsPreviewing    => _tool.IsPreviewing;
        public float  Slider          => _tool.Slider;
        public int    TargetMeshCount => _tool.TargetMeshCount;

        /// <summary>候補リスト作成用。現在のモデル。</summary>
        public ModelContext Model => _project?.CurrentModel;

        public void TriggerCompute()      => _tool.TriggerCompute();
        public void SetSlider(float v)    => _tool.SetSlider(v);
        public void TriggerApply()        => _tool.TriggerApply();
        public void TriggerCancel()       => _tool.TriggerCancel();
        public void CancelIfActive()      => _tool.CancelIfActive();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)         => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)        { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) {}

        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                ctx.Model            = model;
                ctx.SelectedVertices = mc?.SelectedVertices;
                ctx.SelectionState   = mc?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                if (_undoController?.MeshUndoContext != null && model != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;

                ctx.SyncMeshContextPositionsOnly = target =>
                {
                    if (target != null) OnSyncMeshPositions?.Invoke(target);
                };
            }
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }
    }
}
