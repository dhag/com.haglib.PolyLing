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
    public class ObjectMoveToolHandler : IPlayerToolHandler
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

        // ギズモ描画用スクリーン座標取得
        public Func<PlayerViewportPanel.GizmoData> TryGetGizmoData;

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
                    _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
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
        /// ObjectMoveTool.TryPickObject と同じピックフィルタを 1 つの MeshContext に対して評価。
        /// </summary>
        private bool PassPickFilter(Poly_Ling.Data.MeshContext mc, Poly_Ling.Tools.ObjectMoveSettings s)
        {
            if (mc == null || s == null) return false;
            // モーフ・剛体・ジョイント・グループは TryPickObject と同様に常に除外
            var t = mc.Type;
            if (t == Poly_Ling.Data.MeshType.Morph ||
                t == Poly_Ling.Data.MeshType.RigidBody ||
                t == Poly_Ling.Data.MeshType.RigidBodyJoint ||
                t == Poly_Ling.Data.MeshType.Group)
                return false;

            if (t == Poly_Ling.Data.MeshType.Bone)
                return s.PickBones;

            if (t == Poly_Ling.Data.MeshType.Mesh)
            {
                bool skinned = mc.MeshObject != null && mc.MeshObject.HasBoneWeight;
                return skinned ? s.PickMeshesSkinned : s.PickMeshesNoSkin;
            }

            // Helper / BakedMirror / MirrorSide は TryPickObject 互換で常に通す
            return true;
        }

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
