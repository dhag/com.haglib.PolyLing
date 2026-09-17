// PlayerCommandDispatcher.TopologyQuery.cs
// 位相（境界辺・境界頂点・面）を座標で絞って返す照会の処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【選ぶのは呼ぶ側】
//   条件で絞った候補を並べて返すだけで、1 つに決めない。
//   前縁か後縁か、上蓋か側面かは目的で決まるもので、ここには書けない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>位相の照会コマンドなら処理して true を返す。</summary>
        private bool DispatchTopologyQuery(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case ResolveTJunctionsCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var tjModel, out var tjMc, out string tjReason))
                    { Fail(tjReason); return true; }

                    var mo = tjMc.MeshObject;

                    // 面の頂点列を書き換えるので Undo に残す（applyLscmUnwrap と同じ形）。
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(mo, tjMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = tjModel;
                    }
                    var tjBefore = _undoController?.CaptureMeshObjectSnapshotOf(tjMc);

                    var res = Poly_Ling.Ops.TJunctionOps.Resolve(mo, c.Tolerance);

                    if (res.Inserted > 0)
                    {
                        if (_undoController != null && tjBefore != null)
                        {
                            var tjAfter = _undoController.CaptureMeshObjectSnapshotOf(tjMc);
                            _undoController.RecordTopologyChange(tjBefore, tjAfter, "T 字接合の解消");
                        }
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                    }

                    int loops = BridgeAutoPairOps.CollectHoles(mo, tjMc.WorldMatrix).Count;

                    ReportData(CommandDataJson.New()
                        .Int("inserted",      res.Inserted)
                        .Int("touchedFaces",  res.TouchedFaces)
                        .Int("vertices",      mo.VertexCount)
                        .Int("faces",         mo.FaceCount)
                        .Int("boundaryLoops", loops)
                        .Build(),
                        new[] { tjModel.MeshContextList.IndexOf(tjMc) }, new[] { tjMc.ObjectId });
                    return true;
                }

                case QueryBoundaryEdgesCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var beModel, out var beMc, out string beReason))
                    { Fail(beReason); return true; }

                    var mo = beMc.MeshObject;
                    var all = new List<VertexPair>(BoundaryEdgeOps.CollectBoundaryEdges(mo));

                    // 一端で絞る。基準の値は候補の中から取る。
                    // 固定値で切ると、寸法を変えたときに 1 本も残らない。
                    float best = 0f;
                    bool useExtreme = c.AtExtreme != ExtremeAxis.None;
                    if (useExtreme && all.Count > 0)
                    {
                        best = AxisOf(mo, all[0], c.AtExtreme);
                        foreach (var e in all)
                        {
                            float v = AxisOf(mo, e, c.AtExtreme);
                            bool lower = c.AtExtreme == ExtremeAxis.MinX
                                      || c.AtExtreme == ExtremeAxis.MinY
                                      || c.AtExtreme == ExtremeAxis.MinZ;
                            if (lower ? v < best : v > best) best = v;
                        }
                    }

                    var v1 = new List<int>();
                    var v2 = new List<int>();
                    var mx = new List<float>();
                    var my = new List<float>();
                    var mz = new List<float>();

                    foreach (var e in all)
                    {
                        if (useExtreme && Mathf.Abs(AxisOf(mo, e, c.AtExtreme) - best) > c.Tolerance) continue;

                        Vector3 a = mo.Vertices[e.V1].Position;
                        Vector3 b = mo.Vertices[e.V2].Position;

                        v1.Add(e.V1);
                        v2.Add(e.V2);
                        mx.Add((a.x + b.x) * 0.5f);
                        my.Add((a.y + b.y) * 0.5f);
                        mz.Add((a.z + b.z) * 0.5f);
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("count", v1.Count)
                        .Ints ("v1",    v1)
                        .Ints ("v2",    v2)
                        .Nums ("midX",  mx)
                        .Nums ("midY",  my)
                        .Nums ("midZ",  mz)
                        .Build(),
                        new[] { beModel.MeshContextList.IndexOf(beMc) }, new[] { beMc.ObjectId });
                    return true;
                }

                case QueryFacesInBoxCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var fbModel, out var fbMc, out string fbReason))
                    { Fail(fbReason); return true; }

                    var mo = fbMc.MeshObject;

                    Vector3 lo = new Vector3(
                        Mathf.Min(c.Min.x, c.Max.x),
                        Mathf.Min(c.Min.y, c.Max.y),
                        Mathf.Min(c.Min.z, c.Max.z));
                    Vector3 hi = new Vector3(
                        Mathf.Max(c.Min.x, c.Max.x),
                        Mathf.Max(c.Min.y, c.Max.y),
                        Mathf.Max(c.Min.z, c.Max.z));

                    var faces = new List<int>();
                    var cx = new List<float>();
                    var cy = new List<float>();
                    var cz = new List<float>();

                    for (int f = 0; f < mo.FaceCount; f++)
                    {
                        var face = mo.Faces[f];
                        if (face == null || face.VertexIndices == null || face.VertexIndices.Count == 0) continue;

                        Vector3 sum = Vector3.zero;
                        foreach (int vi in face.VertexIndices) sum += mo.Vertices[vi].Position;
                        Vector3 center = sum / face.VertexIndices.Count;

                        if (center.x < lo.x || center.x > hi.x) continue;
                        if (center.y < lo.y || center.y > hi.y) continue;
                        if (center.z < lo.z || center.z > hi.z) continue;

                        faces.Add(f);
                        cx.Add(center.x);
                        cy.Add(center.y);
                        cz.Add(center.z);
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("count",       faces.Count)
                        .Ints ("faceIndices", faces)
                        .Nums ("centerX",     cx)
                        .Nums ("centerY",     cy)
                        .Nums ("centerZ",     cz)
                        .Build(),
                        new[] { fbModel.MeshContextList.IndexOf(fbMc) }, new[] { fbMc.ObjectId });
                    return true;
                }

                case QueryNearestBoundaryVertexCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var nvModel, out var nvMc, out string nvReason))
                    { Fail(nvReason); return true; }

                    // 相手の位置。masterIndex を指したときはその原点を使う。
                    Vector3 target = c.TargetPoint;
                    if (c.TargetMasterIndex >= 0)
                    {
                        if (!TryGetQueryTarget(project, c.ModelIndex, c.TargetMasterIndex,
                                               out var tModel, out var tMc, out string tReason))
                        { Fail(tReason); return true; }

                        var tw = tMc.WorldMatrix;
                        target = new Vector3(tw.m03, tw.m13, tw.m23);
                    }

                    var mo = nvMc.MeshObject;
                    var edges = BoundaryEdgeOps.CollectBoundaryEdges(mo);

                    // 境界辺の端点だけを候補にする。内部の頂点は穴を指せない。
                    var seen = new HashSet<int>();
                    foreach (var e in edges) { seen.Add(e.V1); seen.Add(e.V2); }

                    var w = nvMc.WorldMatrix;
                    int bestIdx = -1;
                    float bestDist = float.MaxValue;
                    Vector3 bestPos = Vector3.zero;

                    foreach (int vi in seen)
                    {
                        Vector3 p = w.MultiplyPoint3x4(mo.Vertices[vi].Position);
                        float d = Vector3.Distance(p, target);
                        if (d >= bestDist) continue;
                        bestDist = d; bestIdx = vi; bestPos = p;
                    }

                    if (bestIdx < 0)
                    {
                        ReportData(CommandDataJson.New()
                            .Flag("found",       false)
                            .Int ("vertexIndex", -1)
                            .Num ("distance",    0f)
                            .Build());
                        return true;
                    }

                    ReportData(CommandDataJson.New()
                        .Flag("found",       true)
                        .Int ("vertexIndex", bestIdx)
                        .Num ("distance",    bestDist)
                        .Nums("position",    new float[] { bestPos.x, bestPos.y, bestPos.z })
                        .Build(),
                        new[] { nvModel.MeshContextList.IndexOf(nvMc) }, new[] { nvMc.ObjectId });
                    return true;
                }

                default: return false;
            }
        }

        /// <summary>境界辺の中点の、指定した軸の値。</summary>
        private static float AxisOf(Poly_Ling.Data.MeshObject mo, VertexPair e, ExtremeAxis axis)
        {
            Vector3 a = mo.Vertices[e.V1].Position;
            Vector3 b = mo.Vertices[e.V2].Position;
            Vector3 m = (a + b) * 0.5f;

            switch (axis)
            {
                case ExtremeAxis.MinX:
                case ExtremeAxis.MaxX: return m.x;
                case ExtremeAxis.MinY:
                case ExtremeAxis.MaxY: return m.y;
                case ExtremeAxis.MinZ:
                case ExtremeAxis.MaxZ: return m.z;
                default:               return 0f;
            }
        }
    }
}
