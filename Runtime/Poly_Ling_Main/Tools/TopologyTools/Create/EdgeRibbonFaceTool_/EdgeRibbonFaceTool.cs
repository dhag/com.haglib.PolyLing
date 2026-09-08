// EdgeRibbonFaceTool.cs
// 選択辺から帯面を追加する即時実行ツール。
// 削除を伴わない末尾追加なので既存選択は維持する。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    public class EdgeRibbonFaceTool : IEditTool
    {
        private readonly EdgeRibbonFaceSettings _settings =
            new EdgeRibbonFaceSettings();

        public string Name => "EdgeRibbonFace";
        public string DisplayName => "Edge Ribbon Face";

        public IToolSettings Settings => _settings;
        public EdgeRibbonFaceSettings RibbonSettings => _settings;

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;
        public void DrawGizmo(ToolContext ctx) { }
        public void OnActivate(ToolContext ctx) { }
        public void OnDeactivate(ToolContext ctx) { }
        public void Reset() { }

        public static int GetSelectedEdgeCount(ModelContext model)
        {
            if (model == null)
                return 0;

            int count = 0;

            foreach (int index in model.SelectedDrawableMeshIndices)
            {
                MeshContext mc = model.GetMeshContext(index);

                if (mc?.MeshObject == null)
                    continue;

                if (mc.Type == MeshType.Bone)
                    continue;

                if (mc.Selection?.Edges == null)
                    continue;

                count += mc.Selection.Edges.Count;
            }

            return count;
        }

        public bool Execute(ToolContext ctx, out string reason)
        {
            reason = null;

            if (ctx == null)
            {
                reason = "ToolContext がありません";
                return false;
            }

            ModelContext model = ctx.Model;

            if (model == null)
            {
                reason = "ModelContext がありません";
                return false;
            }

            if (_settings.WidthWorld <= 0f)
            {
                reason = "幅は 0 より大きい値にしてください";
                return false;
            }

            var targets = new List<(int index, MeshContext mc)>();

            foreach (int index in model.SelectedDrawableMeshIndices)
            {
                MeshContext mc = model.GetMeshContext(index);

                if (mc?.MeshObject == null)
                    continue;

                if (mc.Type == MeshType.Bone)
                    continue;

                if (mc.Selection?.Edges == null ||
                    mc.Selection.Edges.Count == 0)
                    continue;

                targets.Add((index, mc));
            }

            if (targets.Count == 0)
            {
                reason = "選択辺がありません";
                return false;
            }

            MeshUndoController undo = ctx.UndoController;

            var before = new MultiMeshTopologySnapshot();

            if (undo != null)
            {
                for (int i = 0; i < targets.Count; i++)
                    before.CaptureMesh(model, targets[i].index);
            }

            int totalFaces = 0;
            int totalVertices = 0;
            int totalSkipped = 0;
            int changedMeshes = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];

                EdgeRibbonFaceOps.Result r =
                    EdgeRibbonFaceOps.Append(
                        t.mc,
                        t.mc.Selection.Edges,
                        _settings.WidthWorld,
                        model.CurrentMaterialIndex);

                if (!r.Changed)
                    continue;

                totalFaces += r.GeneratedFaces;
                totalVertices += r.GeneratedVertices;
                totalSkipped += r.SkippedEdges;
                changedMeshes++;
            }

            if (changedMeshes == 0)
            {
                reason = "有効な選択辺から面を生成できませんでした";
                return false;
            }

            // 追加のみなので選択を消さない。
            // SyncMesh は位置更新だけでは不足するため NotifyTopologyChanged も呼ぶ。
            ctx.SyncMesh?.Invoke();
            ctx.NotifyTopologyChanged?.Invoke();
            ctx.Repaint?.Invoke();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();

                for (int i = 0; i < targets.Count; i++)
                    after.CaptureMesh(model, targets[i].index);

                undo.SetModelContext(model);

                string desc =
                    $"Edge Ribbon Face ({changedMeshes} objs / " +
                    $"{totalFaces} faces / {totalVertices} vertices)";

                var record =
                    new MultiMeshTopologySnapshotRecord(
                        before,
                        after,
                        desc);

                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            Debug.Log(
                $"[EdgeRibbonFaceTool] meshes={changedMeshes}, " +
                $"faces={totalFaces}, vertices={totalVertices}, " +
                $"skipped={totalSkipped}, widthWorld={_settings.WidthWorld}");

            return true;
        }
    }
}
