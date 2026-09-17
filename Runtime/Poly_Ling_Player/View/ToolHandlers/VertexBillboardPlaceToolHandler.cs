// VertexBillboardPlaceToolHandler.cs
// 頂点へ藤壺：対象の頂点・配置元・カメラの向きから、配置したメッシュを組んで返す。
// InteractionMode は切り替えない。
//
// 【役割】
//   メッシュを組むだけ。モデルへの追加・Undo・再構築は受け口
//   （PolyLingPlayerViewerCore.ExecuteVertexBillboardPlace）が PlaceGeneratedMesh を通して行う。
//
// 【頂点の読み方】
//   辞書名が空なら選択中の描画オブジェクトの選択頂点、指定されていれば
//   対象の各オブジェクトが持つその名前のパーツ選択辞書の頂点。頂点番号の昇順で並べる
//   （巡回・抽選の順を決めるため）。
//
// 【位置】GetWorldPositions（GPU が計算したワールド座標）から読む。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PlaceObject;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class VertexBillboardPlaceToolHandler
    {
        /// <summary>
        /// 今のプロジェクトを返す口。控えを持つとプロジェクトの差し替え（読み込み・作り直し）で
        /// 古いものを掴むので、使うたびに引く（PointDefinedToolHandler と同じ形）。
        /// </summary>
        public Func<ProjectContext> GetProject;

        private ProjectContext _project => GetProject?.Invoke();

        /// <summary>描画オブジェクトの全頂点のワールド位置を GPU から読む口。読めなければ null。</summary>
        public Func<MeshContext, Vector3[]> GetWorldPositions;

        /// <summary>配置元の索引列を MeshObject 列へ解決する口（子孫の展開を含む）。</summary>
        public Func<int[], bool, List<MeshObject>> ResolveSources;

        /// <summary>選択中の描画オブジェクトが持つ選択頂点の合計数。</summary>
        public int GetSelectedVertexCount()
        {
            var model = _project?.CurrentModel;
            if (model == null) return 0;

            int count = 0;
            foreach (int index in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(index);
                if (mc?.MeshObject == null || mc.Type == MeshType.Bone) continue;
                count += mc.Selection?.Vertices?.Count ?? 0;
            }
            return count;
        }

        /// <summary>
        /// プレビュー用。選択中の描画オブジェクトの選択頂点から組む（選択とは照合しない）。
        /// </summary>
        public bool BuildPreview(
            CreateVertexBillboardPlaceCommand cmd, out MeshObject meshObject, out string reason)
        {
            meshObject = null;
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            var targets = new List<int>(model.SelectedDrawableMeshIndices);
            return BuildCore(model, cmd, targets, "", out meshObject, out reason);
        }

        /// <summary>
        /// コマンドから組む。辞書名が空なら MasterIndices が選択中の描画オブジェクトと一致すること。
        /// </summary>
        public bool BuildFromCommand(
            CreateVertexBillboardPlaceCommand cmd, out MeshObject meshObject, out string reason)
        {
            meshObject = null;
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (string.IsNullOrEmpty(cmd.VertexSetName))
            {
                if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                    return false;
            }
            else
            {
                if (cmd.MasterIndices == null || cmd.MasterIndices.Length == 0)
                { reason = "MasterIndices が空です"; return false; }

                foreach (int idx in cmd.MasterIndices)
                {
                    if (model.GetMeshContext(idx) == null)
                    { reason = $"描画オブジェクトが見つかりません (masterIndex={idx})"; return false; }
                }
            }

            return BuildCore(
                model, cmd, new List<int>(cmd.MasterIndices), cmd.VertexSetName,
                out meshObject, out reason);
        }

        private bool BuildCore(
            ModelContext model, CreateVertexBillboardPlaceCommand cmd,
            List<int> targetIndices, string vertexSetName,
            out MeshObject meshObject, out string reason)
        {
            meshObject = null;
            reason = null;

            if (GetWorldPositions == null) { reason = "GPU のワールド座標を読む口が配線されていません"; return false; }
            if (ResolveSources == null) { reason = "配置元を解決する口が配線されていません"; return false; }
            if (cmd.Scale <= 0f) { reason = "倍率は 0 より大きい値にしてください"; return false; }

            if (!VertexBillboardPlaceOps.TryBuildFrame(
                    cmd.ViewDirection, cmd.ViewUp, cmd.ZDirection,
                    out Vector3 x, out Vector3 y, out Vector3 z))
            { reason = "カメラの向きからフレームを作れません（視線が 0、または上方向が視線と平行）"; return false; }

            var sources = ResolveSources(cmd.SourceMasterIndices ?? Array.Empty<int>(), cmd.IncludeChildren);
            if (sources == null || sources.Count == 0) { reason = "配置元がありません"; return false; }

            bool fromSet = !string.IsNullOrEmpty(vertexSetName);
            var positions = new List<Vector3>();

            foreach (int index in targetIndices)
            {
                var mc = model.GetMeshContext(index);
                if (mc?.MeshObject == null || mc.Type == MeshType.Bone) continue;

                var vertices = fromSet
                    ? mc.FindSelectionSetByName(vertexSetName)?.Vertices
                    : mc.Selection?.Vertices;
                if (vertices == null || vertices.Count == 0) continue;

                var world = GetWorldPositions(mc);
                if (world == null || world.Length < mc.MeshObject.VertexCount)
                { reason = $"GPU のワールド座標を読めません（{mc.Name}）"; return false; }

                var sorted = new List<int>(vertices);
                sorted.Sort();
                foreach (int vi in sorted)
                {
                    if (vi < 0 || vi >= mc.MeshObject.VertexCount) continue;
                    positions.Add(world[vi]);
                }
            }

            if (positions.Count == 0)
            {
                reason = fromSet
                    ? $"選択辞書「{vertexSetName}」に頂点が入っていません"
                    : "選択頂点がありません";
                return false;
            }

            meshObject = VertexBillboardPlaceOps.Build(
                positions, sources, cmd.Mode, cmd.RandomSeed, cmd.Scale, x, y, z, cmd.MeshName);

            if (meshObject == null || meshObject.VertexCount == 0)
            { reason = "配置元に頂点がありません"; meshObject = null; return false; }

            return true;
        }
    }
}
