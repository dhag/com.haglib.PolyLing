// SculptToolHandler.cs
// SculptTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class SculptToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SculptTool   _tool = new SculptTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>  GetToolContext;

        /// <summary>
        /// コマンド送信口。ストローク確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;
        public Action             OnRepaint;
        public Action             OnEnterTransformDragging;
        public Action             OnExitTransformDragging;

        /// <summary>頂点位置変更後に UnityMesh + GPU バッファを同期するコールバック。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>
        /// ブラシ円の表示更新コールバック（center: スクリーン座標Y=0下, radius: px）。
        /// </summary>
        public Action<Vector2, float> OnUpdateBrushCircle;

        /// <summary>ブラシ円を非表示にするコールバック。</summary>
        public Action OnHideBrushCircle;


        /// <summary>ブラシ半径が変更されたときに呼ばれるコールバック（UIパネル更新用）。</summary>
        public Action<float> OnRadiusChanged;

        /// <summary>
        /// 半径ドラッグ指定モードを抜けたときに呼ばれるコールバック（ボタンスタイルを戻す用）。
        /// Refresh() に依存せずボタンの青表示を解除するために使用する。
        /// </summary>
        public Action OnRadiusDragModeExited;

        /// <summary>
        /// 半径ドラッグ指定中のプレビュー描画コールバック（center: スクリーン座標Y=0下, radius: px）。
        /// 通常のブラシ円と異なり、ドラッグ開始位置に中心マーカーを表示する。
        /// </summary>
        public Action<Vector2, float> OnUpdateRadiusDragMarker;

        /// <summary>
        /// スカルプトブラシ用ヒットテスト。PlayerViewportManager.GetBrushHit を設定する。
        /// Normal モード時は HoverVertexIndex を、TransformDragging 時は _screenPositions から直接検索する。
        /// </summary>
        public Func<Vector2, float, PlayerHitResult> GetBrushHit;

        // ================================================================
        // ブラシ設定公開
        // ================================================================

        public SculptMode Mode
        {
            get => ((SculptSettings)_tool.Settings)?.Mode ?? SculptMode.Draw;
            set { if (_tool.Settings is SculptSettings s) s.Mode = value; }
        }

        public float BrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.BrushRadius ?? 0.5f;
            set
            {
                if (_tool.Settings is SculptSettings s)
                    s.BrushRadius = Mathf.Clamp(value, s.MinBrushRadius, s.MaxBrushRadius);
            }
        }

        public float Strength
        {
            get => ((SculptSettings)_tool.Settings)?.Strength ?? 0.1f;
            set { if (_tool.Settings is SculptSettings s) s.Strength = Mathf.Clamp(value, s.MinStrength, s.MaxStrength); }
        }

        public float MinStrength
        {
            get => ((SculptSettings)_tool.Settings)?.MinStrength ?? 0.01f;
            set { if (_tool.Settings is SculptSettings s) s.MinStrength = Mathf.Max(0.001f, value); }
        }

        public float MaxStrength
        {
            get => ((SculptSettings)_tool.Settings)?.MaxStrength ?? 0.05f;
            set { if (_tool.Settings is SculptSettings s) s.MaxStrength = Mathf.Max(MinStrength + 0.001f, value); }
        }

        public bool Invert
        {
            get => ((SculptSettings)_tool.Settings)?.Invert ?? false;
            set { if (_tool.Settings is SculptSettings s) s.Invert = value; }
        }

        public FalloffType Falloff
        {
            get => ((SculptSettings)_tool.Settings)?.Falloff ?? FalloffType.Gaussian;
            set { if (_tool.Settings is SculptSettings s) s.Falloff = value; }
        }

        public DistanceMode DistanceMode
        {
            get => ((SculptSettings)_tool.Settings)?.DistanceMode ?? Poly_Ling.Tools.DistanceMode.Euclidean;
            set { if (_tool.Settings is SculptSettings s) s.DistanceMode = value; }
        }

        public float MinBrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.MinBrushRadius ?? 0.05f;
            set { if (_tool.Settings is SculptSettings s) s.MinBrushRadius = Mathf.Max(0.001f, value); }
        }

        public float MaxBrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.MaxBrushRadius ?? 1.0f;
            set { if (_tool.Settings is SculptSettings s) s.MaxBrushRadius = Mathf.Max(MinBrushRadius + 0.001f, value); }
        }

        // ================================================================
        // ドラッグによる半径指定モード
        // ================================================================

        /// <summary>
        /// true の間、次のドラッグ操作はスカルプトではなく
        /// ブラシ半径の設定として扱われる。ドラッグ終了後に自動的に false に戻る。
        /// </summary>
        public bool IsRadiusDragMode { get; set; } = false;

        private Vector2 _radiusDragStartPos;
        private bool    _inRadiusDrag;

        /// <summary>
        /// 半径ドラッグ指定モードを終了し、プレビューとボタンスタイルを解除する。
        /// クリック終了・ドラッグ終了の両方から呼ばれる単一の退出処理。
        /// </summary>
        private void ExitRadiusDragMode()
        {
            _inRadiusDrag    = false;
            IsRadiusDragMode = false;
            OnHideBrushCircle?.Invoke();
            OnRadiusDragModeExited?.Invoke();
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;

        /// <summary>
        /// コマンドで指定された点列にブラシを掛ける。
        ///
        /// 【なぜ要るか】
        ///   マウス経路はブラシ中心を画面のレイから決めるので、コマンド経由
        ///   （自動検証・MCP）からは通せない。点列だけを渡せる入口をここに置く。
        ///   EdgeBridgeToolHandler.SetPicks と同じ形。
        ///
        /// 【実行時と同じ配線を通す】
        ///   変形と Undo 記録は SculptTool.ApplyStrokeFromCommand が
        ///   マウス経路と同じ ApplyStrokeToMesh / CommitStroke を通す。
        ///   ブラシ範囲の収集も距離モード（直線 / リンク）ごと共有される。
        ///
        /// 【Draw の向き】
        ///   コマンドは視点を持たないので、カメラ側へ盛り上げる反転補正は掛からない。
        ///   幾何法線の向きに従うため、同じコマンドは常に同じ結果になる。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SculptStrokeCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }
            if (cmd.BrushCenters == null || cmd.BrushCenters.Length == 0)
            { reason = "ブラシ中心が空です"; return false; }

            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) { reason = "モデルがありません"; return false; }

            var indices = cmd.MasterIndices;
            if (indices == null || indices.Length == 0)
            { reason = "対象が指定されていません"; return false; }

            foreach (int idx in indices)
            {
                if (ctx.Model?.GetMeshContext(idx)?.MeshObject == null)
                { reason = $"masterIndex {idx} のメッシュがありません"; return false; }
            }

            Mode        = cmd.Mode;
            BrushRadius = cmd.BrushRadius;
            Strength    = cmd.Strength;
            Invert      = cmd.Invert;
            Falloff     = cmd.Falloff;

            if (!_tool.ApplyStrokeFromCommand(
                    ctx, indices, cmd.BrushCenters, cmd.ViewDirections))
            { reason = "ブラシ範囲に頂点がありません"; return false; }

            foreach (int idx in indices)
            {
                var mc = ctx.Model?.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                mo.InvalidatePositionCache();
                if (cmd.RecalcNormals) mo.RecalculateSmoothNormals();
                OnSyncMeshPositions?.Invoke(mc);
            }

            OnRepaint?.Invoke();
            return true;
        }
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (IsRadiusDragMode) { ExitRadiusDragMode(); return; }
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));

            // クリックも OnMouseDown + OnMouseUp で 1 点ストロークになる。
            // 取り出しは OnMouseUp の前。OnMouseUp は取り出し済みなら
            // CommitStroke を飛ばす（StrokePending の判定）。
            SendStrokeIfTakenAndFinish(ctx, screenPos);

            // ブラシ円はポインタがビューポート上にある間は出したままにする。
            // ここで消すと、クリックのたびに円が消えて次のホバーまで戻らない。
            UpdateBrushCircleOverlay(ctx, screenPos);
        }

        /// <summary>
        /// 取り出し → OnMouseUp → 発行の順に行う。
        ///
        /// 取り出しを OnMouseUp より先に行うのは、OnMouseUp が StrokePending を見て
        /// CommitStroke を飛ばすため。発行を OnMouseUp より後にするのは、
        /// ExitTransformDragging を通してから変形させるため（ドラッグ中は
        /// 選択・頂点の GPU 反映が抑えられる）。
        /// </summary>
        private void SendStrokeIfTakenAndFinish(ToolContext ctx, Vector2 screenPos)
        {
            bool taken = false;
            int[] meshIndices = null;
            Vector3[] centers = null;
            Vector3[] viewDirs = null;

            if (SendCommand != null && _tool.StrokePending)
                taken = _tool.TryTakeStrokeFromDrag(ctx, out meshIndices, out centers, out viewDirs);

            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (!taken) return;

            SendCommand(new Poly_Ling.Data.SculptStrokeCommand(
                _project?.CurrentModelIndex ?? 0,
                meshIndices,
                centers,
                Mode, BrushRadius, Strength,
                invert:         Invert,
                falloff:        Falloff,
                recalcNormals:  true,
                viewDirections: viewDirs));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (IsRadiusDragMode)
            {
                _radiusDragStartPos = screenPos;
                _inRadiusDrag       = true;
                return;
            }
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                var ctx = BuildToolContext(mods, screenPos);
                if (ctx != null)
                {
                    float screenDist = Vector2.Distance(screenPos, _radiusDragStartPos);
                    float newRadius  = ScreenDistToWorldRadius(screenDist, ctx);
                    if (_tool.Settings is SculptSettings s)
                        newRadius = Mathf.Clamp(newRadius, s.MinBrushRadius, s.MaxBrushRadius);
                    BrushRadius = newRadius;
                    OnRadiusChanged?.Invoke(newRadius);
                    // ドラッグ開始位置を中心にブラシ円＋中心マーカーをプレビュー
                    float previewPx = ScreenRadiusFromWorldRadius(newRadius, ctx);
                    OnUpdateRadiusDragMarker?.Invoke(_radiusDragStartPos, previewPx);
                }
                return;
            }

            var ctx2 = BuildToolContext(mods, screenPos);
            if (ctx2 == null) return;
            _tool.OnMouseDrag(ctx2, ToImgui(screenPos, ctx2), delta);
            UpdateBrushCircleOverlay(ctx2, screenPos);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                ExitRadiusDragMode();
                return;
            }

            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            SendStrokeIfTakenAndFinish(ctx, screenPos);
            // ストローク後もポインタ位置に円を残す（OnLeftClick と同じ理由）。
            UpdateBrushCircleOverlay(ctx, screenPos);
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            // screenPos は PlayerViewportManager.ToHandlerHoverPos で
            // 既にビューポート座標（Y=0 が下）へ変換済み。
            // UpdateBrushCircleOverlay / ShowBrushCircle が期待するのも Y=0 下なので、
            // ここで反転してはならない（ドラッグ経路が渡す ToViewportCoord の結果と同じ空間）。
            UpdateBrushCircleOverlay(ctx, screenPos);
        }

        // ================================================================
        // ブラシ円更新
        // ================================================================

        private void UpdateBrushCircleOverlay(ToolContext ctx, Vector2 screenPosYDown)
        {
            if (OnUpdateBrushCircle == null) return;
            float radius = EstimateBrushScreenRadius(ctx);
            OnUpdateBrushCircle.Invoke(screenPosYDown, radius);
        }

        private float EstimateBrushScreenRadius(ToolContext ctx)
        {
            return ScreenRadiusFromWorldRadius(BrushRadius, ctx);
        }

        private float ScreenRadiusFromWorldRadius(float worldRadius, ToolContext ctx)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight  = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * worldRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint,    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint,  ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            float panelH = ctx.PreviewRect.height;
            sp1.y = panelH - sp1.y;
            sp2.y = panelH - sp2.y;

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private float ScreenDistToWorldRadius(float screenDist, ToolContext ctx)
        {
            Vector3 target   = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;

            Vector2 sp1 = ctx.WorldToScreenPos(target,          ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(target + camRight, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float pxPerUnit = Vector2.Distance(sp1, sp2);
            if (pxPerUnit < 0.001f) return screenDist * 0.01f;
            return screenDist / pxPerUnit;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods, Vector2 screenPosYDown)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();

            baseCtx.Model              = model;
            baseCtx.UndoController     = _undoController;
            baseCtx.Repaint            = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            baseCtx.SyncMesh = () =>
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };

            // ブラシ中心算出用: Normal モード時は HoverVertexIndex、TransformDragging 時は直接検索
            var capturedScreenPos = screenPosYDown;
            baseCtx.GetHoverWorldPosition = () =>
            {
                if (GetBrushHit == null) return null;
                var hit = GetBrushHit(capturedScreenPos, 12f);
                if (!hit.HasHit) return null;
                var mc = model.GetMeshContext(hit.MeshIndex);
                if (mc?.MeshObject == null) return null;
                if (hit.VertexIndex < 0 || hit.VertexIndex >= mc.MeshObject.VertexCount) return null;
                return (Vector3?)mc.MeshObject.Vertices[hit.VertexIndex].Position;
            };


            return baseCtx;
        }

        private MeshUndoController _undoController;


        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
