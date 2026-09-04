// SolidifyToolHandler.cs
// SolidifyTool（厚み付け）を Player に橋渡しする IPlayerToolHandler 実装。
// ビューポート操作は持たず、実行はサブパネルのボタン経由。
// 生成結果は図形生成パネルと同じ経路（OnPrimitiveMeshCreated）へ流す。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class SolidifyToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SolidifyTool _tool = new SolidifyTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        /// <summary>
        /// 生成メッシュの追加先。図形生成パネルの OnMeshCreated と同じ形。
        /// (MeshObject, meshName, worldPosition, poseRotationEuler, poseScale,
        ///  ignorePoseInArmature, addMode, addTargetIndex)
        /// addTargetIndex は AddToExisting のときの追加先（MeshContextList インデックス）。
        /// -1 なら選択オブジェクトリストの先頭。
        /// </summary>
        public Action<MeshObject, string, Vector3, Vector3, Vector3, bool, PrimitiveAddMode, int> OnMeshCreated;

        /// <summary>
        /// AddToExisting のときの追加先。パネルの名前欄ドロップダウンが書き込む。
        /// -1 は選択オブジェクトリストの先頭。
        /// </summary>
        public int AddTargetIndex { get; set; } = -1;

        public SolidifyToolHandler()
        {
            _tool.OnMeshCreated = (mesh, name, addToExisting) =>
            {
                // 頂点は元メッシュのローカル座標で生成済みなので worldPos は渡さない。
                // 姿勢（回転 / スケール）も持たせないので既定値を渡す。
                OnMeshCreated?.Invoke(
                    mesh,
                    name,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    false,
                    addToExisting ? PrimitiveAddMode.AddToExisting : PrimitiveAddMode.NewObject,
                    addToExisting ? AddTargetIndex : -1);
            };
        }

        // ================================================================
        // 設定公開API
        // ================================================================

        public float Thickness
        {
            get => _tool.Thickness;
            set => _tool.Thickness = value;
        }

        public bool AddToExisting
        {
            get => _tool.AddToExisting;
            set => _tool.AddToExisting = value;
        }

        public string MeshName
        {
            get => _tool.MeshName;
            set => _tool.MeshName = value;
        }

        public int SegmentsFront
        {
            get => _tool.SegmentsFront;
            set => _tool.SegmentsFront = value;
        }

        public int SegmentsBack
        {
            get => _tool.SegmentsBack;
            set => _tool.SegmentsBack = value;
        }

        public float EdgeSizeFront
        {
            get => _tool.EdgeSizeFront;
            set => _tool.EdgeSizeFront = value;
        }

        public float EdgeSizeBack
        {
            get => _tool.EdgeSizeBack;
            set => _tool.EdgeSizeBack = value;
        }

        public bool EdgeInward
        {
            get => _tool.EdgeInward;
            set => _tool.EdgeInward = value;
        }

        public int    SelectedFaceCount => _tool.SelectedFaceCount;
        public string LastMessage       => _tool.LastMessage;

        /// <summary>
        /// 厚み付けを実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// SolidifyCommand 経由に統一するため。
        /// </summary>
        private void ExecuteCore()
        {
            var ctx = GetEnrichedCtx();
            if (ctx == null) return;
            _tool.OnActivate(ctx);
            _tool.Execute();
        }

        /// <summary>
        /// 厚み付けコマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   生成そのものは SolidifyTool が正典。ここは対象の照合と設定値の
        ///   差し替えだけを行い、同じ経路を呼ぶ。出来上がったメッシュは
        ///   OnMeshCreated 経由で AddGeneratedMeshCommand へ流れる（既存の結線のまま）。
        ///
        /// 【失敗理由】
        ///   実行できたかは SolidifyTool.LastMessage に入る。実行前後で見て、
        ///   面が選ばれていない等はここで先に弾く。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SolidifyCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var sel = model.ActiveMeshContext?.Selection?.Faces;
            if (sel == null || sel.Count == 0)
            {
                reason = "面を選択してください";
                return false;
            }

            float  savedThickness = Thickness;
            int    savedSegF      = SegmentsFront;
            int    savedSegB      = SegmentsBack;
            float  savedEdgeF     = EdgeSizeFront;
            float  savedEdgeB     = EdgeSizeBack;
            bool   savedInward    = EdgeInward;
            string savedName      = MeshName;
            bool   savedAddTo     = AddToExisting;
            int    savedTarget    = AddTargetIndex;
            try
            {
                Thickness      = cmd.Thickness;
                SegmentsFront  = cmd.SegmentsFront;
                SegmentsBack   = cmd.SegmentsBack;
                EdgeSizeFront  = cmd.EdgeSizeFront;
                EdgeSizeBack   = cmd.EdgeSizeBack;
                EdgeInward     = cmd.EdgeInward;
                MeshName       = cmd.MeshName;
                AddToExisting  = cmd.AddToExisting;
                AddTargetIndex = cmd.AddTargetIndex;

                ExecuteCore();
            }
            finally
            {
                Thickness      = savedThickness;
                SegmentsFront  = savedSegF;
                SegmentsBack   = savedSegB;
                EdgeSizeFront  = savedEdgeF;
                EdgeSizeBack   = savedEdgeB;
                EdgeInward     = savedInward;
                MeshName       = savedName;
                AddToExisting  = savedAddTo;
                AddTargetIndex = savedTarget;
            }

            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)          => _project = project;
        public void SetUndoController(MeshUndoController ctrl)   { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)          { _commandQueue   = queue; }

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
            Enrich(ctx);
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        private void Enrich(ToolContext ctx)
        {
            if (ctx == null) return;
            var model = _project?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            ctx.Model            = model;
            ctx.SelectedVertices = mc?.SelectedVertices;
            ctx.SelectionState   = mc?.Selection;
            ctx.UndoController   = _undoController;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            if (_undoController?.MeshUndoContext != null && model != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
        }

        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            Enrich(ctx);
            return ctx;
        }
    }
}
