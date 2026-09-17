// EdgeRibbonFaceTool.cs
// 選択辺から帯面のメッシュを組むツール。
//
// 【元オブジェクトは変更しない】
//   組んだ結果は 1 つの MeshObject（ワールド座標）として返す。
//   モデルへの追加・Undo・再構築は配置側（PlaceGeneratedMesh）が持つ。
//   他の図形生成と同じ経路に載るので、追加先モードと材質スロットが効く。
//
// 【辺の読み方】
//   辞書名が空なら各対象の選択辺、指定されていれば各対象のパーツ選択辞書の辺を読む。
//   辞書から読めば選択に依らないので、オブジェクトグループの作り直しで同じ帯を組める。
//
// 【ワールド位置】
//   呼び出し側が GPU から読んだ値を渡す（getWorldPositions）。ここでは行列を掛けない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

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
        /// 対象の辺を返す。辞書名が空なら選択辺、指定されていればその名前のパーツ選択辞書の辺。
        /// 辞書が無いときは null。
        /// </summary>
        public static ICollection<VertexPair> ResolveEdges(MeshContext mc, string edgeSetName)
        {
            if (mc == null) return null;

            if (string.IsNullOrEmpty(edgeSetName))
                return mc.Selection?.Edges;

            return mc.FindSelectionSetByName(edgeSetName)?.Edges;
        }

        /// <summary>
        /// 対象の描画オブジェクトそれぞれの辺から帯面を組み、
        /// 1 つの MeshObject（ワールド座標）にまとめて返す。
        /// </summary>
        /// <param name="model">対象モデル。読むだけで変更しない。</param>
        /// <param name="targetIndices">対象の masterIndex。null なら選択中の描画オブジェクト。</param>
        /// <param name="edgeSetName">辺を読むパーツ選択辞書の名前。空なら選択辺。</param>
        /// <param name="getWorldPositions">対象の全頂点のワールド位置を GPU から読む口。</param>
        /// <param name="addStartTag">開いた連なりの開始端に開始三角と開始タグ三角を付ける。</param>
        /// <param name="addEndTag">開いた連なりの終了端に終了三角を付ける。</param>
        /// <param name="meshObject">組んだメッシュ。失敗時は null。</param>
        /// <param name="reason">組めなかった理由。成功時は null。</param>
        public bool Build(
            ModelContext model,
            IReadOnlyList<int> targetIndices,
            string edgeSetName,
            Func<MeshContext, Vector3[]> getWorldPositions,
            bool addStartTag,
            bool addEndTag,
            out MeshObject meshObject,
            out string reason)
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

            if (getWorldPositions == null)
            {
                reason = "GPU のワールド座標を読む口が配線されていません";
                return false;
            }

            IEnumerable<int> indices = targetIndices != null
                ? (IEnumerable<int>)targetIndices
                : model.SelectedDrawableMeshIndices;

            bool fromSet = !string.IsNullOrEmpty(edgeSetName);

            var targets = new List<MeshContext>();
            var edgeLists = new List<ICollection<VertexPair>>();

            foreach (int index in indices)
            {
                MeshContext mc = model.GetMeshContext(index);

                if (mc?.MeshObject == null)
                    continue;

                if (mc.Type == MeshType.Bone)
                    continue;

                var edges = ResolveEdges(mc, edgeSetName);
                if (edges == null || edges.Count == 0)
                    continue;

                targets.Add(mc);
                edgeLists.Add(edges);
            }

            if (targets.Count == 0)
            {
                reason = fromSet
                    ? $"選択辞書「{edgeSetName}」に辺が入っていません"
                    : "選択辺がありません";
                return false;
            }

            var dest = new MeshObject(DefaultMeshName);

            int totalFaces = 0;
            int totalVertices = 0;
            int totalSkipped = 0;
            int totalTagged = 0;
            int totalUntagged = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                Vector3[] world = getWorldPositions(targets[i]);
                if (world == null || world.Length < targets[i].MeshObject.VertexCount)
                {
                    reason = $"GPU のワールド座標を読めません（{targets[i].Name}）";
                    return false;
                }

                EdgeRibbonFaceOps.Result r =
                    EdgeRibbonFaceOps.BuildInto(
                        targets[i],
                        edgeLists[i],
                        world,
                        _settings.WidthWorld,
                        addStartTag,
                        addEndTag,
                        dest);

                totalFaces += r.GeneratedFaces;
                totalVertices += r.GeneratedVertices;
                totalSkipped += r.SkippedEdges;
                totalTagged += r.TaggedChains;
                totalUntagged += r.UntaggedChains;
            }

            if (totalFaces == 0)
            {
                reason = "有効な辺から面を生成できませんでした";
                return false;
            }

            Debug.Log(
                $"[EdgeRibbonFaceTool] objs={targets.Count}, " +
                $"faces={totalFaces}, vertices={totalVertices}, " +
                $"skipped={totalSkipped}, widthWorld={_settings.WidthWorld}, " +
                $"edgeSet={(fromSet ? edgeSetName : "(selection)")}, " +
                $"tagged={totalTagged}, untagged={totalUntagged}");

            meshObject = dest;
            return true;
        }
    }
}
