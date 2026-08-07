// DeformToolHandler.cs
// デフォーマ（回転 / 曲げ / 将来の任意変形）を Player へ橋渡しする
// IPlayerToolHandler 実装。
//
// 変形の中身は DeformApplier と IMeshDeformer が持つ。本クラスは
//   ・どのデフォーマを選んでいるか
//   ・プレビュー中か
//   ・確定時の Undo 記録
// だけを面倒みる。
//
// 【ドラッグ操作は持たない】数値 / スライダのみで操作する方針のため、
//   IPlayerToolHandler のマウスイベントは全て空実装。ギズモも作業軸の
//   表示だけで、曲げ用の専用ハンドルは出さない。
//
// 【プレビューは常に絶対計算】DeformApplier.Apply は Begin で記録した
//   開始位置を基準に毎回計算し直すため、スライダを往復させても誤差が
//   蓄積しない。パラメータが変わるたび Apply を呼んでよい。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>デフォーマ適用ハンドラ。</summary>
    public class DeformToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>    GetToolContext;
        public Func<float>          GetPanelHeight;
        public Action               OnRepaint;

        /// <summary>変形の基準となる作業軸。null なら何もしない。</summary>
        public Func<WorkAxisContext> GetWorkAxis;

        /// <summary>対象モデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>頂点位置を GPU へ同期する。メッシュごとに呼ばれる。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>確定後に呼ばれる。パネル更新に使う。</summary>
        public Action OnApplyCompleted;

        // ================================================================
        // 設定
        // ================================================================

        private MeshUndoController _undoController;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // 状態
        // ================================================================

        private readonly DeformApplier _applier = new DeformApplier();

        private IMeshDeformer _deformer;

        /// <summary>現在選択中のデフォーマ。既定は登録順の先頭。</summary>
        public IMeshDeformer Deformer
        {
            get
            {
                if (_deformer == null)
                {
                    var all = DeformerRegistry.CreateAll();
                    if (all.Count > 0) _deformer = all[0];
                }
                return _deformer;
            }
        }

        /// <summary>デフォーマ名。未選択時は空文字。</summary>
        public string DeformerName => Deformer?.Name ?? string.Empty;

        // マグネット（比例編集）
        public bool         UseMagnet          { get; set; } = false;
        public float        MagnetRadius       { get; set; } = 0.5f;
        public FalloffType  MagnetFalloff      { get; set; } = FalloffType.Smooth;
        public DistanceMode MagnetDistanceMode { get; set; } = DistanceMode.Euclidean;

        /// <summary>プレビュー中か。</summary>
        public bool IsPreviewing => _applier.IsActive;

        /// <summary>対象頂点数。プレビュー外は 0。</summary>
        public int AffectedCount => _applier.AffectedCount;

        /// <summary>作業軸ローカルでの s（= y）範囲。UI 表示用。</summary>
        public DeformContext PreviewContext => _applier.Context;

        // ================================================================
        // デフォーマ選択
        // ================================================================

        /// <summary>
        /// デフォーマを切り替える。プレビュー中なら一度巻き戻してから
        /// 新しいデフォーマで再計算する（パラメータの意味が変わるため）。
        /// </summary>
        public bool SelectDeformer(string name)
        {
            var next = DeformerRegistry.Create(name);
            if (next == null) return false;

            bool wasPreviewing = _applier.IsActive;
            if (wasPreviewing) _applier.Revert();

            _deformer = next;

            if (wasPreviewing) ApplyPreview();
            else               SyncMeshes();

            OnRepaint?.Invoke();
            return true;
        }

        // ================================================================
        // プレビュー
        // ================================================================

        /// <summary>
        /// プレビューを開始する。選択が無ければ false。
        /// 既に開始済みなら何もせず true を返す。
        /// </summary>
        public bool BeginPreview()
        {
            if (_applier.IsActive) return true;

            var model = GetModel?.Invoke();
            var axis  = GetWorkAxis?.Invoke();
            if (model == null || axis == null) return false;

            float radius = UseMagnet ? MagnetRadius : 0f;
            if (!_applier.Begin(model, axis, radius, MagnetFalloff, MagnetDistanceMode))
                return false;

            GetToolContext?.Invoke()?.EnterTransformDragging?.Invoke();
            return true;
        }

        /// <summary>
        /// 現在のパラメータでプレビューを更新する。
        /// 未開始なら自動で BeginPreview する。
        /// </summary>
        public void ApplyPreview()
        {
            if (!_applier.IsActive && !BeginPreview()) return;

            var d = Deformer;
            if (d == null) return;

            _applier.Apply(d);
            SyncMeshes();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 確定 / 巻き戻し
        // ================================================================

        /// <summary>
        /// 変形を確定して Undo に記録する。
        /// 記録の組み立ては DeformApplier、Record 呼び出しはここ
        /// （RotateTool.ApplyRotation と同じ責務分割）。
        /// </summary>
        public void Commit()
        {
            if (!_applier.IsActive) return;

            var entries = _applier.BuildUndoEntries();

            if (entries.Length > 0 && _undoController != null)
            {
                _undoController.FocusVertexEdit();
                var record = new MultiMeshVertexMoveRecord(entries);
                _undoController.VertexEditStack.Record(record, $"Deform ({DeformerName})");
            }

            // VertexOffsets の基準を現在位置へ追従させる。
            _applier.SyncOriginalPositions();

            ExitPreview();
            OnApplyCompleted?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>変形を捨てて開始位置へ戻す。</summary>
        public void Revert()
        {
            if (!_applier.IsActive) return;

            _applier.Revert();
            SyncMeshes();

            ExitPreview();
            OnRepaint?.Invoke();
        }

        /// <summary>デフォーマのパラメータを既定値へ戻す。プレビューは維持する。</summary>
        public void ResetParams()
        {
            Deformer?.Params?.Reset();
            if (_applier.IsActive) ApplyPreview();
            else                   OnRepaint?.Invoke();
        }

        private void ExitPreview()
        {
            _applier.Reset();
            GetToolContext?.Invoke()?.ExitTransformDragging?.Invoke();
        }

        // ================================================================
        // IPlayerToolHandler（ドラッグ操作は持たない）
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }

        /// <summary>ホバー処理なし。作業軸ギズモは操作対象ではない。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) { }

        // ================================================================
        // ギズモ（作業軸の表示のみ）
        // ================================================================

        // ScreenOffset は既定 (60,-60) だが、作業軸は原点そのものが基準なので
        // ずらさず描く。WorkAxisToolHandler と同じ扱い。
        private readonly AxisGizmo _axisGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };

        /// <summary>
        /// 変形の基準になっている作業軸を矢印で表示する。
        /// 操作はできない（ホバー・ドラッグを受けない）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            var wa = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null || !wa.IsVisible) return false;

            _axisGizmo.Center       = wa.Origin;
            _axisGizmo.Orientation  = wa.Rotation;
            _axisGizmo.HoveredAxis  = AxisGizmo.AxisType.None;
            _axisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _axisGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo    = true,
                Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis = AxisGizmo.AxisType.None,
            };
            return true;
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>変形した全メッシュを GPU へ同期する。</summary>
        private void SyncMeshes()
        {
            var model = GetModel?.Invoke();
            if (model == null || OnSyncMeshPositions == null) return;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) OnSyncMeshPositions(mc);
            }
        }
    }
}
