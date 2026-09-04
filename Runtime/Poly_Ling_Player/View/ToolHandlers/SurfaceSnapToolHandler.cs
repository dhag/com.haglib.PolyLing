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

        // プレビュー操作（計算・スライダー・取り消し）は画面上の確認であって
        // 確定操作ではないため、コマンド化せず public のまま残す。
        // 確定（TriggerApply）だけが Undo を積む（SurfaceSnapTool.cs:439-453）。
        public void TriggerCompute()      => _tool.TriggerCompute();
        public void SetSlider(float v)    => _tool.SetSlider(v);
        public void TriggerCancel()       => _tool.TriggerCancel();
        public void CancelIfActive()      => _tool.CancelIfActive();

        /// <summary>
        /// プレビューを確定する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// SurfaceSnapCommand 経由に統一するため。
        /// </summary>
        private void TriggerApplyCore()   => _tool.TriggerApply();

        /// <summary>
        /// 面に張り付けコマンドを実行する。
        ///
        /// 【1 コマンドに畳んである】
        ///   パネルは「計算 → スライダー → 決定」の 3 段だが、確定は決定の 1 回だけ。
        ///   よってここは計算・スライダー・決定を続けて呼ぶ。
        ///   計算に失敗した（IsPreviewing が false のまま）ときは LastResult を返す。
        ///
        /// 【リファレンス】
        ///   コマンドの ReferenceMasterIndices を正典として入れ替え、
        ///   終わったらパネルの指定へ戻す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SurfaceSnapCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                return false;

            if (cmd.ReferenceMasterIndices == null || cmd.ReferenceMasterIndices.Length == 0)
            {
                reason = "リファレンスオブジェクトを 1 つ以上指定してください";
                return false;
            }
            foreach (int idx in cmd.ReferenceMasterIndices)
            {
                if (model.GetMeshContext(idx)?.MeshObject == null)
                {
                    reason = $"リファレンスオブジェクトが見つかりません: masterIndex {idx}";
                    return false;
                }
            }

            // 計算の途中で前のプレビューが残っていると結果が混ざるので先に畳む。
            CancelIfActive();

            // 実行時と同じコンテキストを通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            var savedRefs   = new List<int>(ReferenceIndices);
            var savedCam    = CameraKind;
            bool savedOnly  = SelectedVerticesOnly;
            float savedOff  = SurfaceOffset;
            var savedBack   = Backface;
            try
            {
                foreach (int idx in savedRefs) SetReference(idx, false);
                foreach (int idx in cmd.ReferenceMasterIndices) SetReference(idx, true);

                CameraKind           = cmd.CameraKind;
                SelectedVerticesOnly = cmd.SelectedVerticesOnly;
                SurfaceOffset        = cmd.SurfaceOffset;
                Backface             = cmd.Backface;

                TriggerCompute();
                if (!IsPreviewing)
                {
                    reason = string.IsNullOrEmpty(LastResult) ? "計算できませんでした" : LastResult;
                    return false;
                }

                SetSlider(cmd.Slider);
                TriggerApplyCore();
            }
            finally
            {
                CancelIfActive();

                foreach (int idx in cmd.ReferenceMasterIndices) SetReference(idx, false);
                foreach (int idx in savedRefs) SetReference(idx, true);

                CameraKind           = savedCam;
                SelectedVerticesOnly = savedOnly;
                SurfaceOffset        = savedOff;
                Backface             = savedBack;
            }

            return true;
        }

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
