// SkinWeightPaintToolHandler.cs
// SkinWeightPaintTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class SkinWeightPaintToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SkinWeightPaintTool _tool = new SkinWeightPaintTool();
        private          ProjectContext      _project;
        private          MeshUndoController  _undoController;
        private          CommandQueue        _commandQueue;

        // ================================================================
        // 外部コールバック
        // ================================================================

        public Func<ToolContext>              GetToolContext;
        public Action                         OnRepaint;
        public Action                         OnEnterTransformDragging;
        public Action                         OnExitTransformDragging;

        /// <summary>頂点位置変更後に UnityMesh + GPU バッファを同期するコールバック。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>ブラシ円の表示更新（center: スクリーン座標Y=0下, radius: px）。</summary>
        public Action<Vector2, float, Color>  OnUpdateBrushCircle;

        /// <summary>ブラシ円を非表示にする。</summary>
        public Action                         OnHideBrushCircle;

        // --- GPU hover path（ブラシ範囲頂点取得用。MoveToolHandler と同じソース） ---
        /// <summary>GPU 計算済みの全頂点スクリーン座標（Y=0上）。</summary>
        public Func<Vector2[]> GetScreenPositions;
        /// <summary>ctx インデックス→頂点オフセット。</summary>
        public Func<int, int>  GetVertexOffset;
        /// <summary>グローバル頂点が可視（表向き）か。</summary>
        public Func<int, bool> IsVertexVisible;
        /// <summary>ビューポート高さ（px）。</summary>
        public Func<float>     GetViewportHeight;
        /// <summary>背面カリングが有効か（OFF なら裏面頂点も塗る）。</summary>
        public Func<bool>      IsBackfaceCullingEnabled;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)     => _project       = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        public void SetCommandQueue(CommandQueue queue)    => _commandQueue   = queue;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
            UpdateBrushOverlay(ctx, screenPos);
        }

        /// <summary>
        /// ドラッグ確定。
        ///
        /// 【1 ストローク = 1 コマンド】
        ///   ドラッグ中の塗りはプレビューとして扱い、確定時に開始状態へ戻してから
        ///   SkinWeightPaintCommand を 1 本発行する。実際の塗りと Undo 記録は
        ///   SkinWeightPaintTool.ApplyStrokeFromCommand が行う。
        ///
        /// 【SendCommand 未結線のとき】
        ///   取り出さず OnMouseUp に確定させる（3-d / 3-e と同じ方針）。
        /// </summary>
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;

            var model = _project?.CurrentModel;

            bool taken = false;
            List<SkinWeightPaintTool.StrokeStep> steps = null;
            Poly_Ling.UI.SkinWeightPaintMode paintMode = default;
            int   targetBone  = -1;
            float strength    = 0f;
            float weightValue = 0f;

            if (SendCommand != null && model != null && _tool.StrokePending)
                taken = _tool.TryTakeStrokeFromDrag(
                    ctx, model, out steps, out paintMode, out targetBone, out strength, out weightValue);

            // 取り出したときは _beforeSnapshots が空なので OnMouseUp は Undo を積まない。
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (taken)
                SendCommand.Invoke(BuildStrokeCommand(
                    model, steps, paintMode, targetBone, strength, weightValue));
        }

        /// <summary>コマンドの発行先（Viewer から結線）。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>
        /// ステップ列を平坦な配列へ畳んでコマンドを組む。
        ///
        /// PanelCommandFactory は文字列パラメータから平坦な配列しか組み立てられないため、
        /// 入れ子を持てない。ステップの区切りは StepStarts が持つ。
        /// </summary>
        private Poly_Ling.Data.SkinWeightPaintCommand BuildStrokeCommand(
            Poly_Ling.Context.ModelContext model,
            List<SkinWeightPaintTool.StrokeStep> steps,
            Poly_Ling.UI.SkinWeightPaintMode paintMode, int targetBone, float strength, float weightValue)
        {
            var stepStarts  = new List<int>(steps.Count);
            var stepMeshes  = new List<int>(steps.Count);
            var vertIndices = new List<int>();
            var falloffs    = new List<float>();

            var targets = new List<int>();
            foreach (var st in steps)
            {
                stepStarts.Add(vertIndices.Count);
                stepMeshes.Add(st.MeshIndex);
                if (!targets.Contains(st.MeshIndex)) targets.Add(st.MeshIndex);

                foreach (var (vi, fo) in st.Verts)
                {
                    vertIndices.Add(vi);
                    falloffs.Add(fo);
                }
            }
            targets.Sort();

            return new Poly_Ling.Data.SkinWeightPaintCommand(
                _project?.CurrentModelIndex ?? 0,
                targets.ToArray(),
                stepStarts.ToArray(), stepMeshes.ToArray(),
                vertIndices.ToArray(), falloffs.ToArray(),
                paintMode, targetBone, strength, weightValue);
        }

        /// <summary>
        /// スキンウェイト塗りコマンドを実行する。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   塗りそのものは SkinWeightPaintTool が正典。ここは平坦な配列をステップ列へ
        ///   戻し、長さの整合を確かめてから ApplyStrokeFromCommand へ渡す。
        ///
        /// 【対象の照合】
        ///   MasterIndices は「ステップが触るメッシュの集合」。実行時点の塗り対象
        ///   （SkinWeightOperations.CollectTargetMeshContexts）に含まれることを確かめる。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SkinWeightPaintCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            var starts   = cmd.StepStarts      ?? Array.Empty<int>();
            var meshes   = cmd.StepMeshIndices ?? Array.Empty<int>();
            var vertices = cmd.VertexIndices   ?? Array.Empty<int>();
            var falloffs = cmd.Falloffs        ?? Array.Empty<float>();

            if (starts.Length == 0)
            { reason = "ステップがありません"; return false; }
            if (starts.Length != meshes.Length)
            { reason = "StepStarts と StepMeshIndices の長さが違います"; return false; }
            if (vertices.Length != falloffs.Length)
            { reason = "VertexIndices と Falloffs の長さが違います"; return false; }

            for (int i = 0; i < starts.Length; i++)
            {
                if (starts[i] < 0 || starts[i] > vertices.Length)
                { reason = $"StepStarts[{i}] が範囲外です"; return false; }
                if (i > 0 && starts[i] < starts[i - 1])
                { reason = "StepStarts は単調増加で指定してください"; return false; }
            }

            // 対象の照合。塗り対象に含まれないメッシュを指していないか見る。
            var paintable = new HashSet<int>();
            foreach (var mc in Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model))
            {
                int idx = model.IndexOf(mc);
                if (idx >= 0) paintable.Add(idx);
            }
            foreach (int idx in cmd.MasterIndices ?? Array.Empty<int>())
            {
                if (!paintable.Contains(idx))
                {
                    reason = $"masterIndex {idx} は塗り対象ではありません。"
                           + "先に SelectMeshCommand で選択を合わせてください";
                    return false;
                }
            }

            var steps = new List<SkinWeightPaintTool.StrokeStep>(starts.Length);
            for (int i = 0; i < starts.Length; i++)
            {
                int from = starts[i];
                int to   = (i + 1 < starts.Length) ? starts[i + 1] : vertices.Length;
                if (to < from) { reason = $"StepStarts[{i}] の範囲が逆転しています"; return false; }

                var verts = new List<(int index, float falloff)>(to - from);
                for (int k = from; k < to; k++) verts.Add((vertices[k], falloffs[k]));

                steps.Add(new SkinWeightPaintTool.StrokeStep
                {
                    MeshIndex = meshes[i],
                    Verts     = verts,
                });
            }

            var ctx = BuildToolContext(default(ModifierKeys), Vector2.zero);
            if (ctx == null) { reason = "ツールコンテキストがありません"; return false; }

            if (!_tool.ApplyStrokeFromCommand(
                    ctx, steps, cmd.PaintMode, cmd.TargetBone, cmd.Strength, cmd.WeightValue))
            {
                reason = "塗れる頂点がありませんでした";
                return false;
            }

            return true;
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            UpdateBrushOverlay(ctx, screenPos);
        }

        // ================================================================
        // Activate / Deactivate
        // ================================================================

        public void OnActivate()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            ctx.Model          = _project?.CurrentModel;
            ctx.UndoController = _undoController;
            _tool.OnActivate(ctx);
        }

        public void OnDeactivate()
        {
            // ウェイト可視化のクリア
            var model = _project?.CurrentModel;
            if (model != null)
            {
                var mc = model.ActiveMeshContext;
                if (mc?.UnityMesh != null)
                    mc.UnityMesh.colors = null;
            }
            SkinWeightPaintTool.SetVisualizationActive(false);
            OnHideBrushCircle?.Invoke();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 毎フレーム：ウェイト可視化の適用
        // ================================================================

        /// <summary>
        /// Update から毎フレーム呼ぶ。
        /// ウェイト可視化がアクティブなとき UnityMesh に頂点カラーを適用する。
        /// </summary>
        public void TickVisualization()
        {
            if (!SkinWeightPaintTool.IsVisualizationActive) return;
            var model = _project?.CurrentModel;
            if (model == null) return;

            var mc = model.ActiveMeshContext;
            if (mc?.UnityMesh == null || mc.MeshObject == null) return;

            int targetBone = SkinWeightPaintTool.VisualizationTargetBone;
            SkinWeightPaintTool.ApplyVisualizationColors(mc.UnityMesh, mc.MeshObject, targetBone);
        }

        // ================================================================
        // ブラシ円更新
        // ================================================================

        private void UpdateBrushOverlay(ToolContext ctx, Vector2 screenPosYDown)
        {
            if (OnUpdateBrushCircle == null) return;
            var panel = SkinWeightPaintTool.ActivePanel;
            float radius = panel?.CurrentBrushRadius ?? 0.3f;
            float screenR = EstimateBrushScreenRadius(ctx, radius);
            Color col = GetBrushColor(panel);
            OnUpdateBrushCircle.Invoke(screenPosYDown, screenR, col);
        }

        private float EstimateBrushScreenRadius(ToolContext ctx, float worldRadius)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight  = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * worldRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint,   ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            float panelH = ctx.PreviewRect.height;
            sp1.y = panelH - sp1.y;
            sp2.y = panelH - sp2.y;
            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private static Color GetBrushColor(Poly_Ling.UI.ISkinWeightPaintPanel panel)
        {
            if (panel == null) return new Color(0.6f, 0.6f, 0.6f, 0.5f);
            switch (panel.CurrentPaintMode)
            {
                case Poly_Ling.UI.SkinWeightPaintMode.Replace: return new Color(0.3f, 0.7f, 1.0f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Add:     return new Color(0.3f, 1.0f, 0.5f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Scale:   return new Color(1.0f, 0.8f, 0.3f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Smooth:  return new Color(0.8f, 0.5f, 1.0f, 0.6f);
                default: return new Color(1f, 1f, 1f, 0.5f);
            }
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
            baseCtx.CommandQueue       = _commandQueue;
            baseCtx.Repaint            = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            // SyncMesh: ウェイト確定後に GPU バッファを同期。
            // ブラシは選択中の描画オブジェクト全件をまたいで塗るため、
            // 対象全件へ転送する。1 件だけ転送すると他メッシュの表示が古いまま残る。
            baseCtx.SyncMesh = () =>
            {
                foreach (var mc in Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model))
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };

            // ブラシ範囲頂点（GPU hover path / スクリーン空間ブラシ）
            float worldRadius = SkinWeightPaintTool.ActivePanel?.CurrentBrushRadius ?? 0.1f;
            float screenR     = EstimateBrushScreenRadius(baseCtx, worldRadius);
            baseCtx.GetBrushVerticesMulti = () => ComputeBrushVertices(screenPosYDown, screenR, worldRadius);

            return baseCtx;
        }

        /// <summary>
        /// ブラシ範囲内の頂点＋falloff を、選択中の描画オブジェクトごとに返す。
        ///
        /// 【直線モード】スクリーン円で拾う。GPU 計算済みスクリーン座標＋可視判定を使い、
        /// CommitBoxSelect と同方式。背面除外はカリング ON のときのみ。
        ///
        /// 【リンク距離モード】メッシュごとに、スクリーン円内で中心に最も近い可視頂点を
        /// 種として辺をたどった累積距離（LinkDistanceField）で拾う。スカルプトの
        /// GetVerticesInBrushRadiusLink と同じ手順。辺で繋がっていない別オブジェクトへは
        /// 伝播しないので、種はメッシュごとに取り直す必要がある。
        ///
        /// falloff は両モードとも FalloffHelper.Calculate に通す
        /// （マグネット・スカルプトと同一の計算）。
        ///
        /// 頂点オフセットはメッシュごとに GetVertexOffset(model.IndexOf(mc)) で取り直す。
        /// 以前は model.FirstMeshIndex 固定で、先頭以外のメッシュでは別頂点を拾っていた。
        /// </summary>
        private System.Collections.Generic.List<
            (Poly_Ling.Data.MeshContext mesh,
             System.Collections.Generic.List<(int index, float falloff)> verts)>
            ComputeBrushVertices(Vector2 mouseYDown, float screenRadius, float worldRadius)
        {
            var result = new System.Collections.Generic.List<
                (Poly_Ling.Data.MeshContext, System.Collections.Generic.List<(int, float)>)>();

            var model = _project?.CurrentModel;
            if (model == null || GetScreenPositions == null) return result;

            var screenPos = GetScreenPositions();
            if (screenPos == null) return result;

            float vpH    = GetViewportHeight?.Invoke() ?? 0f;
            bool  cullOn = IsBackfaceCullingEnabled?.Invoke() ?? true;

            var falloffType  = SkinWeightPaintTool.ActivePanel?.CurrentFalloff ?? FalloffType.Gaussian;
            var distanceMode = SkinWeightPaintTool.CurrentDistanceMode;

            foreach (var mc in Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model))
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                int vertexOffset = GetVertexOffset?.Invoke(model.IndexOf(mc)) ?? 0;
                var verts = new System.Collections.Generic.List<(int, float)>();

                if (distanceMode == DistanceMode.Link)
                {
                    // 種頂点＝このメッシュ内でスクリーン円中心に最も近い可視頂点
                    int   seed    = -1;
                    float minDist = float.MaxValue;
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        int gi = vertexOffset + i;
                        if (gi < 0 || gi >= screenPos.Length) continue;
                        if (cullOn && IsVertexVisible != null && !IsVertexVisible(gi)) continue;

                        Vector2 vs   = new Vector2(screenPos[gi].x, vpH - screenPos[gi].y);
                        float   dist = Vector2.Distance(vs, mouseYDown);
                        if (dist < minDist) { minDist = dist; seed = i; }
                    }

                    // 円内に頂点が無ければこのメッシュは対象外（直線モードと同じ判定）
                    if (seed < 0 || minDist > screenRadius) continue;

                    var adjacency = SkinWeightPaintTool.BuildAdjacency(mo);
                    var field     = LinkDistanceField.Compute(
                        adjacency, mo.Positions, new[] { seed }, worldRadius);

                    foreach (var kvp in field)
                    {
                        float t = worldRadius > 0f ? kvp.Value / worldRadius : 0f;
                        verts.Add((kvp.Key, FalloffHelper.Calculate(t, falloffType)));
                    }
                }
                else
                {
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        int gi = vertexOffset + i;
                        if (gi < 0 || gi >= screenPos.Length) continue;
                        // 背面除外はカリング ON のときだけ（OFF なら裏面も対象）
                        if (cullOn && IsVertexVisible != null && !IsVertexVisible(gi)) continue;

                        // CommitBoxSelect と同じ Y 反転でスクリーン座標(Y=0下)へ揃える
                        Vector2 vs   = new Vector2(screenPos[gi].x, vpH - screenPos[gi].y);
                        float   dist = Vector2.Distance(vs, mouseYDown);
                        if (dist <= screenRadius)
                        {
                            float t = screenRadius > 0f ? dist / screenRadius : 0f;
                            verts.Add((i, FalloffHelper.Calculate(t, falloffType)));
                        }
                    }
                }

                if (verts.Count > 0) result.Add((mc, verts));
            }

            return result;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
