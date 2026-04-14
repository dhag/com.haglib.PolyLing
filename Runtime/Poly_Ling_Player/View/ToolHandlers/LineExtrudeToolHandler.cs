// LineExtrudeToolHandler.cs
// LineExtrudeTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.Player
{
    public class LineExtrudeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly LineExtrudeTool _tool = new LineExtrudeTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 押し出しパラメータ
        // ================================================================

        public float Thickness       = 0.1f;
        public float Scale           = 1.0f;
        public Vector2 Offset        = Vector2.zero;
        public bool  FlipY           = false;
        public int   SegmentsFront   = 0;
        public int   SegmentsBack    = 0;
        public float EdgeSizeFront   = 0.1f;
        public float EdgeSizeBack    = 0.1f;
        public bool  EdgeInward      = false;

        // ================================================================
        // 設定公開API
        // ================================================================

        /// <summary>
        /// 検出済みループを押し出してモデルに追加する。
        /// </summary>
        public void ExecuteExtrude(string meshName = "LineExtrude", bool addToCurrent = false)
        {
            var loops = _tool.GetLoopsForExtrude();
            if (loops == null || loops.Count == 0)
            {
                Debug.LogWarning("[LineExtrudeToolHandler] No loops detected. Run Analyze first.");
                return;
            }

            var genParams = new Profile2DGenerateParams
            {
                Scale         = Scale,
                Offset        = Offset,
                FlipY         = FlipY,
                Thickness     = Thickness,
                SegmentsFront = SegmentsFront,
                SegmentsBack  = SegmentsBack,
                EdgeSizeFront = EdgeSizeFront,
                EdgeSizeBack  = EdgeSizeBack,
                EdgeInward    = EdgeInward,
            };

            var meshObject = Profile2DExtrudeMeshGenerator.Generate(loops, meshName, genParams);
            if (meshObject == null)
            {
                Debug.LogWarning("[LineExtrudeToolHandler] Profile2DExtrudeMeshGenerator returned null.");
                return;
            }

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            // ctx を補完してモデル参照を確保
            var model = _project?.CurrentModel;
            if (model == null) return;
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.CommandQueue   = _commandQueue;

            if (addToCurrent)
            {
                ctx.AddMeshObjectToCurrentMesh?.Invoke(meshObject, meshName);
            }
            else
            {
                // 新規 MeshContext として追加
                var unityMesh      = meshObject.ToUnityMesh();
                unityMesh.name     = meshName;
                unityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                var newMc = new Poly_Ling.Data.MeshContext
                {
                    Name       = meshName,
                    MeshObject = meshObject,
                };
                newMc.UnityMesh = unityMesh;
                ctx.AddMeshContext?.Invoke(newMc);
            }

            NotifyTopologyChanged?.Invoke();
            OnRepaint?.Invoke();
            Debug.Log($"[LineExtrudeToolHandler] Extruded {loops.Count} loops → {meshObject.VertexCount} verts, {meshObject.FaceCount} faces.");
        }

        public int  SelectedLineCount => _tool.GetSelectedLineCount();
        public int  DetectedLoopCount => _tool.GetDetectedLoopCount();
        public void AnalyzeLoops()    => _tool.AnalyzeLoops();
        public System.Collections.Generic.List<Poly_Ling.Tools.LineExtrudeTool.LoopSummary>
            GetLoopSummaries()  => _tool.GetLoopSummaries();
        public void SaveAsCSV()       => _tool.SaveAsCSV();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)         => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetToolContext?.Invoke(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.DrawGizmo(ctx);
        }
        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                ctx.Model            = model;
                ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
                ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            }
            _tool.OnActivate(ctx);
        }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

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
