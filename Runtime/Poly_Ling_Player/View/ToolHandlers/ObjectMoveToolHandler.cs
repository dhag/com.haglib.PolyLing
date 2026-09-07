// ObjectMoveToolHandler.cs
// ObjectMoveTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// <see cref="ObjectMoveTool"/> を Player 入力に橋渡しする。
    /// <para>
    /// IPlayerToolHandler の左クリック/ドラッグを ObjectMoveTool.OnMouseDown/Drag/Up に変換する。
    /// ObjectMoveTool が必要とする ToolContext フィールド（Model・UndoController・
    /// SyncBoneTransforms 等）を PlayerToolContext 経由で補完する。
    /// </para>
    /// </summary>
    public class ObjectMoveToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly ObjectMoveTool    _tool = new ObjectMoveTool();
        private          ProjectContext    _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>毎回最新の ToolContext を返すコールバック。</summary>
        public Func<ToolContext> GetToolContext;

        public Action OnRepaint;
        public Action OnEnterTransformDragging;
        public Action OnExitTransformDragging;
        public Action OnMeshSelectionChanged;

        /// <summary>
        /// 選択変更 (ボーン / 描画メッシュどちらでも) 後に呼ぶ汎用コールバック。
        /// BoneEditor サブパネルの Refresh など、選択カテゴリ問わず再描画したい
        /// 購読者向け。OnMeshSelectionChanged と独立して発火する。
        /// </summary>
        public Action OnSelectionChanged;

        /// <summary>
        /// 描画メッシュ側 (ActiveCategory == Mesh) に選択が変わったときに呼ぶ。
        /// BoneInputHandler.OnDrawableMeshSelectionChanged の移植先。
        /// </summary>
        public Action OnDrawableMeshSelectionChanged;

        /// <summary>ボーン位置変更後に呼ぶ同期コールバック（NotifyPanels 等）。</summary>
        public Action OnSyncBoneTransforms;

        /// <summary>頂点位置変更後に UnityMesh + GPU バッファを同期するコールバック（OriginOnly 用）。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>
        /// コマンド送信口。ドラッグ確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// PivotOffsetToolHandler.SendCommand と同じ役割。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>
        /// ObjectMoveTool の ObjectMoveSettings を取得する。
        /// BoneEditor サブパネル側のチェックボックスと双方向同期させる際に使う。
        /// </summary>
        public Poly_Ling.Tools.ObjectMoveSettings GetSettings() => _tool.GetSettings();

        // ================================================================
        // 矩形 / 投げ縄選択 (MoveToolHandler の頂点選択と同じ UI 操作感)
        // ================================================================

        /// <summary>矩形選択 / 投げ縄選択の切替。</summary>
        public enum SelectionDragMode { Box, Lasso }

        /// <summary>
        /// 現在のドラッグ選択モード。MoveToolHandler の DragSelectMode と同等。
        /// 外部 (ViewerCore 等) から設定される想定。
        /// </summary>
        public SelectionDragMode DragSelectMode = SelectionDragMode.Box;

        // ObjectMove 内部のドラッグ状態
        private enum ObjDragState { None, ToolDelegated, BoxSelecting, LassoSelecting }
        private ObjDragState _dragState = ObjDragState.None;

        // 矩形座標 (screen Y=0 下基準。UI 描画へはそのまま渡せる形)
        private Vector2 _boxStart;
        private Vector2 _boxEnd;
        private readonly System.Collections.Generic.List<Vector2> _lassoPoints
            = new System.Collections.Generic.List<Vector2>();

        // 矩形 / 投げ縄 UI 用コールバック (panel と接続)。
        // MoveToolHandler と同名にしてあるので ViewerCore で対称に配線できる。
        public Action                                           OnEnterBoxSelecting;
        public Action                                           OnExitBoxSelecting;
        public Action<Vector2, Vector2>                         OnBoxSelectUpdate;
        public Action<System.Collections.Generic.List<Vector2>> OnLassoSelectUpdate;
        public Action                                           OnBoxSelectEnd;
        public Action                                           OnLassoSelectEnd;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) { _dragState = ObjDragState.None; return; }

            // 1. ツールに先にヒット判定させる (ギズモ・ピボット・オブジェクトのいずれか)
            //    OnMouseDown は bool を返し、true=何かに当たった/false=空振り。
            bool toolHit = _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            if (toolHit)
            {
                _dragState = ObjDragState.ToolDelegated;
                return;
            }

            // 2. 空振り → 矩形 / 投げ縄選択モードへ。
            //    MoveToolHandler と同じく DragSelectMode で切替える。
            //    投げ縄でも Box でも、UI 進入通知 (OnEnterBoxSelecting) は共通で 1 つ。
            if (DragSelectMode == SelectionDragMode.Lasso)
            {
                _dragState = ObjDragState.LassoSelecting;
                _lassoPoints.Clear();
                _lassoPoints.Add(screenPos);
            }
            else
            {
                _dragState = ObjDragState.BoxSelecting;
                _boxStart = screenPos;
                _boxEnd   = screenPos;
            }
            OnEnterBoxSelecting?.Invoke();
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            switch (_dragState)
            {
                case ObjDragState.ToolDelegated:
                {
                    var ctx = BuildToolContext(mods);
                    if (ctx == null) return;
                    // screenPos はY反転、delta はY反転不要（差分なので符号が打ち消し合う）
                    _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
                    return;
                }
                case ObjDragState.BoxSelecting:
                {
                    _boxEnd = screenPos;
                    OnBoxSelectUpdate?.Invoke(_boxStart, _boxEnd);
                    return;
                }
                case ObjDragState.LassoSelecting:
                {
                    // 直前点との距離が一定以上のときのみ追加 (PlayerSelectionOps と同じ閾値 2f)
                    if (_lassoPoints.Count == 0 ||
                        Vector2.Distance(screenPos, _lassoPoints[_lassoPoints.Count - 1]) > 2f)
                    {
                        _lassoPoints.Add(screenPos);
                    }
                    OnLassoSelectUpdate?.Invoke(_lassoPoints);
                    return;
                }
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            switch (_dragState)
            {
                case ObjDragState.ToolDelegated:
                {
                    var ctx = BuildToolContext(mods);
                    if (ctx == null) { _dragState = ObjDragState.None; return; }

                    // 【1 ドラッグ = 1 コマンド】
                    //   ドラッグ中の適用はプレビュー扱い。確定時に開始状態へ戻して
                    //   総移動量または累計回転だけを取り出し、コマンドとして送る。
                    //   実際の適用と Undo 記録は Apply*FromCommand が行う。
                    //   送信口が無いときは取り出さず、ObjectMoveTool.OnMouseUp が
                    //   従来どおり CommitUndo で確定させる。
                    //   移動と回転は同時に立たない（移動中は回転の開始状態が空、
                    //   回転中は総移動量が 0）ので、順に試してよい。
                    Poly_Ling.Data.PanelCommand pending = null;

                    if (SendCommand != null && _tool.ObjectMoveDragPending &&
                        _tool.TryTakeObjectMoveDrag(ctx, out int[] mvTargets, out Vector3 mvWorldTotal))
                    {
                        pending = new Poly_Ling.Data.MoveObjectsCommand(
                            _project?.CurrentModelIndex ?? 0,
                            mvTargets,
                            mvWorldTotal,
                            Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World,
                            _tool.GetSettings().MoveWithChildren,
                            _tool.GetSettings().MoveMode);
                    }
                    else if (SendCommand != null && _tool.ObjectRotateDragPending &&
                             _tool.TryTakeObjectRotateDrag(
                                 ctx, out int[] rotTargets, out Vector3 rotPivot,
                                 out Vector3 rotAxis, out float rotAngle))
                    {
                        pending = new Poly_Ling.Data.RotateObjectsCommand(
                            _project?.CurrentModelIndex ?? 0,
                            rotTargets,
                            rotPivot, false,
                            rotAxis, rotAngle,
                            _tool.GetSettings().MoveWithChildren,
                            _tool.GetSettings().MoveMode);
                    }

                    _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

                    if (pending != null) SendCommand(pending);
                    break;
                }
                case ObjDragState.BoxSelecting:
                {
                    _boxEnd = screenPos;
                    CommitBoxSelect(mods);
                    OnBoxSelectEnd?.Invoke();
                    OnExitBoxSelecting?.Invoke();
                    break;
                }
                case ObjDragState.LassoSelecting:
                {
                    CommitLassoSelect(mods);
                    OnLassoSelectEnd?.Invoke();
                    OnExitBoxSelecting?.Invoke();
                    _lassoPoints.Clear();
                    break;
                }
            }
            _dragState = ObjDragState.None;
        }

        // ================================================================
        // コマンド経路
        // ================================================================

        /// <summary>
        /// 選択オブジェクトの移動コマンドを実行する。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   移動・子補正・BindPose 更新・Undo 記録は
        ///   ObjectMoveTool.ApplyMoveFromCommand がドラッグ確定時と同じ
        ///   SaveSnapshots → ApplyWorldDelta → CommitUndo を通す。
        ///
        /// 【設定値はコマンドが正典】
        ///   MoveWithChildren / MoveMode は退避してからコマンド値を代入し、
        ///   終わったら戻す。1 呼び出しがパネルの状態に依存しないようにするため。
        ///   ObjectMoveSettings は BoneEditor サブパネルと共有しているので、
        ///   戻し忘れると UI のチェックが変わってしまう。
        ///
        /// 【Local の基準】
        ///   Space == Local のとき、Delta は MasterIndices[0] のローカル量として
        ///   解釈し、そのメッシュの WorldMatrix でワールドへ変換する。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.MoveObjectsCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedObjects(model, cmd.MasterIndices, out reason))
                return false;

            var indices = cmd.MasterIndices;
            Vector3 worldDelta;
            if (cmd.Space == Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World)
            {
                worldDelta = cmd.Delta;
            }
            else
            {
                var baseMc = model.GetMeshContext(indices[0]);
                if (baseMc == null)
                { reason = $"masterIndex {indices[0]} のオブジェクトがありません"; return false; }
                worldDelta = baseMc.WorldMatrix.MultiplyVector(cmd.Delta);
            }

            var ctx = BuildToolContext(default(ModifierKeys));
            if (ctx == null) { reason = "モデルがありません"; return false; }

            var settings = _tool.GetSettings();
            bool         savedWithChildren = settings.MoveWithChildren;
            BoneMoveMode savedMode         = settings.MoveMode;
            try
            {
                settings.MoveWithChildren = cmd.MoveWithChildren;
                settings.MoveMode         = cmd.MoveMode;
                return _tool.ApplyMoveFromCommand(ctx, indices, worldDelta, out reason);
            }
            finally
            {
                settings.MoveWithChildren = savedWithChildren;
                settings.MoveMode         = savedMode;
            }
        }

        /// <summary>
        /// 選択オブジェクトの回転コマンドを実行する。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   ObjectMoveTool.ApplyRotateFromCommand が
        ///   SaveRotationStart → SaveSnapshots → ApplyWorldRotation → CommitUndo を
        ///   リングドラッグと同じ順序で通す。非一様スケールの祖先を持つ要素の除外も
        ///   その中にある。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.RotateObjectsCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedObjects(model, cmd.MasterIndices, out reason))
                return false;

            var ctx = BuildToolContext(default(ModifierKeys));
            if (ctx == null) { reason = "モデルがありません"; return false; }

            var settings = _tool.GetSettings();
            bool         savedWithChildren = settings.MoveWithChildren;
            BoneMoveMode savedMode         = settings.MoveMode;
            try
            {
                settings.MoveWithChildren = cmd.MoveWithChildren;
                settings.MoveMode         = cmd.MoveMode;
                return _tool.ApplyRotateFromCommand(
                    ctx, cmd.MasterIndices,
                    cmd.Pivot, cmd.UseSelectionCentroid,
                    cmd.Axis, cmd.Angle, out reason);
            }
            finally
            {
                settings.MoveWithChildren = savedWithChildren;
                settings.MoveMode         = savedMode;
            }
        }

        // ================================================================
        // ギズモスクリーン座標取得
        // ================================================================

        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            var builtCtx = BuildToolContext(default);
            if (builtCtx == null)
            {
                origin = xEnd = yEnd = zEnd = Vector2.zero;
                hoveredAxis = AxisGizmo.AxisType.None;
                return false;
            }
            return _tool.TryGetGizmoScreenPositions(
                builtCtx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        /// <summary>回転リング 3 軸のスクリーン点列を返す（UpdateGizmoOverlay 用）。</summary>
        public bool TryGetGizmoRings(
            ToolContext ctx,
            out Vector2[] ringX, out Vector2[] ringY, out Vector2[] ringZ,
            out AxisGizmo.AxisType hoveredAxis)
        {
            var builtCtx = BuildToolContext(default);
            if (builtCtx == null)
            {
                ringX = ringY = ringZ = null;
                hoveredAxis = AxisGizmo.AxisType.None;
                return false;
            }
            return _tool.TryGetGizmoRings(
                builtCtx, out ringX, out ringY, out ringZ, out hoveredAxis);
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// 軸ギズモ（矢印）と回転リングはそれぞれ ObjectMoveSettings の
        /// AllowMoveGizmo / AllowRotationGizmo で独立に出し入れできる。
        /// どちらも無効なら false を返し、呼び出し側が HideGizmo する。
        ///
        /// 【矢印だけを消す場合の描き方】
        ///   PlayerViewportPanel は IsRingStyle かつ DrawAxisWithRing == false のとき
        ///   リングだけ描いて終了する。ピボットのダイヤもこの分岐で描かれなくなるため、
        ///   矢印非表示時は HasPivotGizmo を立てない（立てても描かれないため）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            bool hasAxis = TryGetGizmoScreenPositions(
                ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis);

            // 回転リング（設定で無効なときは false が返る）
            bool hasRing = TryGetGizmoRings(
                ctx, out var ringX, out var ringY, out var ringZ, out var ringHovAxis);

            if (!hasAxis && !hasRing) return false;

            // 軸ホバーとリングホバーは ObjectMoveTool 側で排他になっている。
            // GizmoData の HoveredAxis は 1 つなので、非 None の方を採用する。
            var shownHover = hovAxis != AxisGizmo.AxisType.None ? hovAxis : ringHovAxis;

            Vector2 pivotScreen = Vector2.zero;
            if (hasAxis) GetPivotScreenPos(out pivotScreen);

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo       = true,
                IsDiamondStyle = false,
                Origin         = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                HoveredAxis    = shownHover,
                HasPivotGizmo  = hasAxis, PivotOrigin = pivotScreen,
                IsRingStyle    = hasRing,
                RingX = ringX, RingY = ringY, RingZ = ringZ,
                DrawAxisWithRing = hasAxis,
            };
            return true;
        }

        public bool GetPivotScreenPos(out Vector2 pivotScreen)
        {
            var ctx = BuildToolContext(default);
            return _tool.GetPivotScreenPos(ctx, out pivotScreen);
        }

        public bool TryGetGizmoPivotPositions(
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            var ctx = BuildToolContext(default);
            if (ctx == null)
            {
                origin = xEnd = yEnd = zEnd = Vector2.zero;
                hoveredAxis = AxisGizmo.AxisType.None;
                return false;
            }
            return _tool.TryGetGizmoPivotPositions(
                ctx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        // ================================================================
        // ギズモ更新（毎フレーム呼ぶ）
        // ================================================================

        /// <summary>
        /// ギズモを描画するためのスクリーン座標を取得する。
        /// PlayerViewportPanel.GizmoData を返す。
        /// Viewer の UpdateGizmoOverlay 相当。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            Vector2 mouseScreenPos,
            out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            var ctx = BuildToolContext(default);
            if (ctx == null) return false;

            _tool.DrawGizmo(ctx);
            return false; // ギズモ座標の取り出しは AxisGizmo 公開プロパティ経由で行う
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.UpdateHoverOnly(ctx, ToImgui(screenPos, ctx));
        }

        // ================================================================
        // 矩形 / 投げ縄選択 - Commit
        // ================================================================

        /// <summary>
        /// ピックフィルタの評価は ObjectMoveTool.PassesPickFilter へ一本化する。
        /// ここに同じ判定を書き直さないこと（クリックピック・矩形選択・原点マーカーで
        /// 判定がずれる原因になる）。
        /// </summary>
        private bool PassPickFilter(Poly_Ling.Data.MeshContext mc, Poly_Ling.Tools.ObjectMoveSettings s)
            => Poly_Ling.Tools.ObjectMoveTool.PassesPickFilter(mc, s);

        /// <summary>
        /// 修飾キー処理を MoveToolHandler の頂点矩形選択と揃える:
        ///   修飾なし → 既存選択をクリアしてから加算
        ///   Shift   → 既存選択に加算
        ///   Ctrl    → 1 つずつトグル
        /// 選択は ModelContext のカテゴリ自動判定 API を使うので、
        /// ボーン / 描画メッシュ / Helper / BakedMirror が混在しても自動で
        /// 適切な SelectedXxxIndices に振り分けられる。
        /// </summary>
        private void ApplyMultiSelection(
            ModelContext model,
            System.Collections.Generic.List<int> hitIndices,
            ModifierKeys mods)
        {
            if (model == null || hitIndices == null) return;

            if (!mods.Shift && !mods.Ctrl)
                model.ClearSelection();

            for (int k = 0; k < hitIndices.Count; k++)
            {
                int i = hitIndices[k];
                if (mods.Ctrl)
                    model.ToggleMeshContextSelection(i);
                else
                    model.AddToSelection(i);
            }
        }

        private void CommitBoxSelect(ModifierKeys mods)
        {
            var ctx   = BuildToolContext(mods);
            var model = ctx?.Model;
            if (model == null) return;

            var s    = _tool.GetSettings();
            var rect = MakeRect(_boxStart, _boxEnd);
            float vpH = ctx.PreviewRect.height;

            var hits = new System.Collections.Generic.List<int>(32);
            int n = model.Count;
            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (!PassPickFilter(mc, s)) continue;

                var wm = mc.WorldMatrix;
                // ctx.WorldToScreen は Y=0 が上 (下向き増加)。
                // _boxStart/_boxEnd は panel.OnDrag の座標系 = Y=0 が下 (上向き増加)。
                // rect 側に揃えるため Y を反転 (MoveToolHandler の vertexScreen と同じ手口)。
                var spTop = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                var sp = new Vector2(spTop.x, vpH - spTop.y);
                if (rect.Contains(sp)) hits.Add(i);
            }

            ApplyMultiSelection(model, hits, mods);
            // OnMeshSelectionChanged → OnSelectionChanged 連鎖は BuildToolContext 側で
            // ctx.OnMeshSelectionChanged ラムダにまとめてあるが、ここは直接ラムダを通っていないので
            // 自前で呼び出す。
            FireSelectionCallbacks(model);
            OnRepaint?.Invoke();
        }

        private void CommitLassoSelect(ModifierKeys mods)
        {
            var ctx   = BuildToolContext(mods);
            var model = ctx?.Model;
            if (model == null) return;
            if (_lassoPoints.Count < 3) return; // 三角形未満はキャンセル扱い

            var s = _tool.GetSettings();
            float vpH = ctx.PreviewRect.height;

            var hits = new System.Collections.Generic.List<int>(32);
            int n = model.Count;
            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (!PassPickFilter(mc, s)) continue;

                var wm = mc.WorldMatrix;
                // CommitBoxSelect と同じく Y を反転して _lassoPoints 側 (Y=0 が下) に揃える。
                var spTop = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                var sp = new Vector2(spTop.x, vpH - spTop.y);
                if (PointInPolygon(sp, _lassoPoints)) hits.Add(i);
            }

            ApplyMultiSelection(model, hits, mods);
            FireSelectionCallbacks(model);
            OnRepaint?.Invoke();
        }

        private void FireSelectionCallbacks(ModelContext model)
        {
            // BuildToolContext が組み立てる ctx.OnMeshSelectionChanged と同じ順序で発火。
            OnMeshSelectionChanged?.Invoke();
            OnSelectionChanged?.Invoke();
            if (model != null && model.ActiveCategory == ModelContext.SelectionCategory.Mesh)
                OnDrawableMeshSelectionChanged?.Invoke();
        }

        // 2 点から左下原点で正規化された Rect を作る
        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(a.x - b.x);
            float h = Mathf.Abs(a.y - b.y);
            return new Rect(x, y, w, h);
        }

        // 点が多角形 (折れ線で閉じる) の内部にあるかを Ray Casting で判定
        private static bool PointInPolygon(
            Vector2 p, System.Collections.Generic.List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            int j = poly.Count - 1;
            for (int i = 0; i < poly.Count; i++)
            {
                var pi = poly[i];
                var pj = poly[j];
                if (((pi.y > p.y) != (pj.y > p.y)) &&
                    (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-9f) + pi.x))
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            // GetToolContext からカメラ・投影情報を取得
            var baseCtx = GetToolContext?.Invoke();

            // baseCtx が null でも Model さえあれば続行できる
            var ctx = baseCtx ?? new ToolContext();

            ctx.Model                  = model;
            ctx.UndoController         = _undoController;
            ctx.SyncBoneTransforms     = OnSyncBoneTransforms;
            // OriginOnly(原点だけ移動)は頂点を書き換えるため GPU 同期が必要。
            ctx.SyncMesh               = () =>
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };
            ctx.Repaint                = OnRepaint;
            ctx.EnterTransformDragging = OnEnterTransformDragging;
            ctx.ExitTransformDragging  = OnExitTransformDragging;
            // ObjectMoveTool が選択を変更したあとに呼ばれるコールバック。
            // BoneInputHandler の OnSelectionChanged / OnDrawableMeshSelectionChanged を
            // 吸収するため、OnMeshSelectionChanged を起点にこちらでも発火する。
            ctx.OnMeshSelectionChanged = () =>
            {
                OnMeshSelectionChanged?.Invoke();
                OnSelectionChanged?.Invoke();
                var m = _project?.CurrentModel;
                if (m != null && m.ActiveCategory == Poly_Ling.Context.ModelContext.SelectionCategory.Mesh)
                    OnDrawableMeshSelectionChanged?.Invoke();
            };
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld   = mods.Shift,
                IsControlHeld = mods.Ctrl,
            };
            return ctx;
        }

        // ================================================================
        // UndoController（Viewer から設定）
        // ================================================================

        private MeshUndoController _undoController;

        public void SetUndoController(MeshUndoController ctrl) =>
            _undoController = ctrl;

        // ================================================================
        // Y座標変換（PlayerViewportPanelはY=0下、AxisGizmoはY=0上）
        // ================================================================

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
