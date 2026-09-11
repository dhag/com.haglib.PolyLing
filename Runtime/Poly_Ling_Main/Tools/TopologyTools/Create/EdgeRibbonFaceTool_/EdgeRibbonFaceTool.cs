// EdgeRibbonFaceTool.cs
// 選択辺から帯面のメッシュを組むツール。
//
// 【元オブジェクトは変更しない】
//   組んだ結果は 1 つの MeshObject（ワールド座標）として返す。
//   モデルへの追加・Undo・再構築は配置側（PlaceGeneratedMesh）が持つ。
//   他の図形生成と同じ経路に載るので、追加先モードと材質スロットが効く。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

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

        /// <summary>生成物の既定名。</summary>
        public const string DefaultMeshName = "EdgeRibbon";

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

        /// <summary>
        /// 選択中の描画オブジェクトそれぞれの選択辺から帯面を組み、
        /// 1 つの MeshObject（ワールド座標）にまとめて返す。
        /// </summary>
        /// <param name="model">対象モデル。読むだけで変更しない。</param>
        /// <param name="meshObject">組んだメッシュ。失敗時は null。</param>
        /// <param name="reason">組めなかった理由。成功時は null。</param>
        public bool Build(ModelContext model, out MeshObject meshObject, out string reason)
        {
            meshObject = null;
            reason = null;

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

            var targets = new List<MeshContext>();

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

                targets.Add(mc);
            }

            if (targets.Count == 0)
            {
                reason = "選択辺がありません";
                return false;
            }

            var dest = new MeshObject(DefaultMeshName);

            int totalFaces = 0;
            int totalVertices = 0;
            int totalSkipped = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                EdgeRibbonFaceOps.Result r =
                    EdgeRibbonFaceOps.BuildInto(
                        targets[i],
                        targets[i].Selection.Edges,
                        _settings.WidthWorld,
                        dest);

                totalFaces += r.GeneratedFaces;
                totalVertices += r.GeneratedVertices;
                totalSkipped += r.SkippedEdges;
            }

            if (totalFaces == 0)
            {
                reason = "有効な選択辺から面を生成できませんでした";
                return false;
            }

            Debug.Log(
                $"[EdgeRibbonFaceTool] objs={targets.Count}, " +
                $"faces={totalFaces}, vertices={totalVertices}, " +
                $"skipped={totalSkipped}, widthWorld={_settings.WidthWorld}");

            meshObject = dest;
            return true;
        }
    }
}
