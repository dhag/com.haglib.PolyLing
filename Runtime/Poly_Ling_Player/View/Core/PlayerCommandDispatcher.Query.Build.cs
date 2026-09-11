// PlayerCommandDispatcher.Query.Build.cs
// コマンドディスパッチャ：照会コマンドごとの結果（JSON）の組み立て。
// 受け口の振り分けは PlayerCommandDispatcher.Query.cs の DispatchQuery。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// オブジェクトグループの状態。
        /// 自動更新が流れなかったときの判定材料だけを並べる。
        /// </summary>
        private static string BuildObjectGroupsData(
            ProjectContext project, ModelContext model, QueryObjectGroupsCommand cmd)
        {
            var groups = model.ObjectGroups ?? new List<Poly_Ling.Data.ObjectGroup>();

            var values = new List<PLDataValue>
            {
                PLDataValue.Num("modelIndex", cmd.ModelIndex),
                PLDataValue.Num("groups",     groups.Count),
            };

            int staleCount = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                string key = $"group.{i}";

                if (g == null)
                {
                    values.Add(PLDataValue.Str(key + ".name", ""));
                    continue;
                }

                var src  = g.SourceObjectIds;
                var outs = g.OutputObjectIds;

                int srcAlive = 0;
                foreach (ulong id in src)
                    if (ObjectGroupOps.Resolve(project, id) != null) srcAlive++;

                int outAlive = 0;
                foreach (ulong id in outs)
                    if (ObjectGroupOps.Resolve(project, id) != null) outAlive++;

                bool stale = ObjectGroupOps.IsStale(project, g);
                if (stale) staleCount++;

                values.Add(PLDataValue.Str(key + ".name",        g.Name ?? ""));
                values.Add(PLDataValue.Str(key + ".action",      g.Action ?? ""));
                values.Add(PLDataValue.Num(key + ".steps",       g.StepCount));
                values.Add(PLDataValue.Num(key + ".autoUpdate", g.AutoUpdate ? 1 : 0));
                values.Add(PLDataValue.Num(key + ".stale",      stale ? 1 : 0));
                values.Add(PLDataValue.Num(key + ".hasDigest",  string.IsNullOrEmpty(g.SourceDigest) ? 0 : 1));
                values.Add(PLDataValue.Num(key + ".sources",     src.Count));
                values.Add(PLDataValue.Num(key + ".sourcesAlive", srcAlive));
                values.Add(PLDataValue.Num(key + ".outputs",     outs.Count));
                values.Add(PLDataValue.Num(key + ".outputsAlive", outAlive));
            }

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "objectGroups"), values,
                masterIndex: -1, objectId: 0UL,
                source: PanelCommandFactory.ActionOf(typeof(QueryObjectGroupsCommand))));

            return CommandDataJson.New()
                .Entry("entry",  entry)
                .Int("groups",   groups.Count)
                .Int("stale",    staleCount)
                .Build();
        }

        /// <summary>
        /// ボーンウェイトの行き先。
        /// 「塗り直されたか」は頂点数では判らず、どのボーンを指しているかで決まる。
        /// </summary>
        private static string BuildSkinWeightSummaryData(
            ModelContext model, MeshContext mc, QuerySkinWeightSummaryCommand cmd)
        {
            var mo = mc.MeshObject;

            // 接頭辞に一致するボーンの索引。空なら数えない。
            var prefixBones = new HashSet<int>();
            if (!string.IsNullOrEmpty(cmd.BonePrefix))
            {
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var b = model.GetMeshContext(i);
                    if (b == null || b.Type != MeshType.Bone) continue;
                    if (string.IsNullOrEmpty(b.Name)) continue;
                    if (b.Name.StartsWith(cmd.BonePrefix, StringComparison.Ordinal))
                        prefixBones.Add(i);
                }
            }

            int vertices = mo?.VertexCount ?? 0;
            int weighted = 0, multi = 0, toPrefix = 0;
            var distinct = new HashSet<int>();

            for (int i = 0; i < vertices; i++)
            {
                var bw = mo.Vertices[i]?.BoneWeight;
                if (bw == null) continue;

                weighted++;
                var w = bw.Value;

                int used = 0;
                bool hitPrefix = false;

                if (w.weight0 > 0.0001f) { used++; distinct.Add(w.boneIndex0); hitPrefix |= prefixBones.Contains(w.boneIndex0); }
                if (w.weight1 > 0.0001f) { used++; distinct.Add(w.boneIndex1); hitPrefix |= prefixBones.Contains(w.boneIndex1); }
                if (w.weight2 > 0.0001f) { used++; distinct.Add(w.boneIndex2); hitPrefix |= prefixBones.Contains(w.boneIndex2); }
                if (w.weight3 > 0.0001f) { used++; distinct.Add(w.boneIndex3); hitPrefix |= prefixBones.Contains(w.boneIndex3); }

                if (used >= 2)  multi++;
                if (hitPrefix)  toPrefix++;
            }

            var values = new List<PLDataValue>
            {
                PLDataValue.Num("masterIndex",   cmd.MasterIndex),
                PLDataValue.Str("name",          mc.Name ?? ""),
                PLDataValue.Str("objectId",      IdText(mc.ObjectId)),
                PLDataValue.Num("isSkinned",     mc.IsSkinned ? 1 : 0),
                PLDataValue.Num("vertices",      vertices),
                PLDataValue.Num("weighted",      weighted),
                PLDataValue.Num("multiBone",     multi),
                PLDataValue.Str("bonePrefix",    cmd.BonePrefix ?? ""),
                PLDataValue.Num("prefixBones",   prefixBones.Count),
                PLDataValue.Num("toPrefix",      toPrefix),
                PLDataValue.Num("distinctBones", distinct.Count),
            };

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "skinWeightSummary"), values,
                masterIndex: cmd.MasterIndex, objectId: mc.ObjectId,
                source: PanelCommandFactory.ActionOf(typeof(QuerySkinWeightSummaryCommand))));

            return CommandDataJson.New()
                .Entry("entry",       entry)
                .Int("vertices",      vertices)
                .Int("weighted",      weighted)
                .Int("multiBone",     multi)
                .Int("toPrefix",      toPrefix)
                .Int("prefixBones",   prefixBones.Count)
                .Int("distinctBones", distinct.Count)
                .Build();
        }

        /// <summary>モデルの構成。</summary>
        private static string BuildModelStructureData(
            ProjectContext project, ModelContext model, QueryModelStructureCommand cmd)
        {
            var values = new List<PLDataValue>
            {
                PLDataValue.Num("models",          project.ModelCount),
                PLDataValue.Num("modelIndex",      cmd.ModelIndex),
                PLDataValue.Str("modelName",       model.Name ?? ""),
                PLDataValue.Num("meshContexts",    model.Count),
                PLDataValue.Num("drawables",       model.DrawableCount),
                PLDataValue.Num("bones",           model.BoneCount),
                PLDataValue.Num("morphs",          model.Morphs.Count),
                PLDataValue.Num("rigidBodies",     model.RigidBodies.Count),
                PLDataValue.Num("rigidBodyJoints", model.RigidBodyJoints.Count),
                PLDataValue.Num("helpers",         model.Helpers.Count),
                PLDataValue.Num("groups",          model.Groups.Count),
            };

            var drawables = model.DrawableMeshes;
            for (int i = 0; i < drawables.Count; i++)
            {
                var ent = drawables[i];
                string key = $"drawable.{i}";

                values.Add(PLDataValue.Num(key + ".masterIndex", ent.MasterIndex));
                values.Add(PLDataValue.Str(key + ".objectId",    IdText(ent.Context?.ObjectId ?? 0UL)));
                values.Add(PLDataValue.Str(key + ".name",        ent.Name ?? ""));
            }

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "modelStructure"), values,
                masterIndex: -1, objectId: 0UL,
                source: PanelCommandFactory.ActionOf(typeof(QueryModelStructureCommand))));

            return CommandDataJson.New()
                .Entry("entry",      entry)
                .Int("modelIndex",   cmd.ModelIndex)
                .Int("meshContexts", model.Count)
                .Int("drawables",    model.DrawableCount)
                .Int("bones",        model.BoneCount)
                .Int("morphs",       model.Morphs.Count)
                .Build();
        }

        /// <summary>描画オブジェクト 1 個の規模。</summary>
        private static string BuildDrawableStatsData(
            ModelContext model, MeshContext mc, QueryDrawableStatsCommand cmd)
        {
            var mesh    = mc.MeshObject;
            var toWorld = mc.WorldMatrix;

            int triangles = 0;
            var usedMaterials = new HashSet<int>();
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                if (f == null) continue;
                triangles += f.TriangleCount;
                usedMaterials.Add(f.MaterialIndex);
            }

            // バウンディングボックスはワールド空間。頂点が無ければ 0 のまま。
            var min = Vector3.zero;
            var max = Vector3.zero;
            bool hasBounds = false;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                if (v == null) continue;
                Vector3 p = toWorld.MultiplyPoint3x4(v.Position);
                if (!hasBounds) { min = p; max = p; hasBounds = true; continue; }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 size = hasBounds ? (max - min) : Vector3.zero;

            var holes = BridgeAutoPairOps.CollectHoles(mesh, toWorld);
            int boundaryVertices = 0;
            for (int i = 0; i < holes.Count; i++) boundaryVertices += holes[i].Count;

            var values = new List<PLDataValue>
            {
                PLDataValue.Num("masterIndex",      cmd.MasterIndex),
                PLDataValue.Str("name",             mc.Name ?? ""),
                PLDataValue.Str("objectId",         IdText(mc.ObjectId)),
                PLDataValue.Num("vertices",         mesh.Vertices.Count),
                PLDataValue.Num("faces",            mesh.Faces.Count),
                PLDataValue.Num("triangles",        triangles),
                PLDataValue.Num("materialSlots",    model.Materials?.Count ?? 0),
                PLDataValue.Num("materialsUsed",    usedMaterials.Count),
                PLDataValue.Num("boundaryLoops",    holes.Count),
                PLDataValue.Num("boundaryVertices", boundaryVertices),
                PLDataValue.Num("boundsMinX", min.x), PLDataValue.Num("boundsMinY", min.y), PLDataValue.Num("boundsMinZ", min.z),
                PLDataValue.Num("boundsMaxX", max.x), PLDataValue.Num("boundsMaxY", max.y), PLDataValue.Num("boundsMaxZ", max.z),
                PLDataValue.Num("boundsSizeX", size.x), PLDataValue.Num("boundsSizeY", size.y), PLDataValue.Num("boundsSizeZ", size.z),
            };

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "drawableStats"), values,
                masterIndex: cmd.MasterIndex, objectId: mc.ObjectId,
                source: PanelCommandFactory.ActionOf(typeof(QueryDrawableStatsCommand))));

            return CommandDataJson.New()
                .Entry("entry",         entry)
                .Int("masterIndex",     cmd.MasterIndex)
                .Text("name",           mc.Name ?? "")
                .Text("objectId",       IdText(mc.ObjectId))
                .Int("vertices",        mesh.Vertices.Count)
                .Int("faces",           mesh.Faces.Count)
                .Int("triangles",       triangles)
                .Int("materialsUsed",   usedMaterials.Count)
                .Int("boundaryLoops",   holes.Count)
                .Build();
        }

        /// <summary>穴（境界ループ）の一覧。</summary>
        private static string BuildHolesData(
            ModelContext model, MeshContext mc, QueryHolesCommand cmd)
        {
            var holes = BridgeAutoPairOps.CollectHoles(mc.MeshObject, mc.WorldMatrix);

            var loops  = new List<PLDataLoop>(holes.Count);
            var counts = new List<int>(holes.Count);
            for (int i = 0; i < holes.Count; i++)
            {
                var h = holes[i];
                loops.Add(new PLDataLoop
                {
                    Vertices = new List<int>(h.Vertices),
                    Centroid = h.WorldCentroid,
                });
                counts.Add(h.Count);
            }

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromLoops(
                ResolveResultName(store, cmd.ResultName, "holes"), loops,
                masterIndex: cmd.MasterIndex, objectId: mc.ObjectId,
                source: PanelCommandFactory.ActionOf(typeof(QueryHolesCommand))));

            return CommandDataJson.New()
                .Entry("entry",             entry)
                .Int("masterIndex",         cmd.MasterIndex)
                .Int("holes",               holes.Count)
                .Ints("holeVertexCounts",   counts)
                .Build();
        }

        /// <summary>
        /// 種にする要素を 1 個だけ探す。
        /// 走査は素直な線形比較で、新しい探索アルゴリズムは持ち込まない。
        /// </summary>
        private static string BuildSeedElementData(
            ModelContext model, MeshContext mc, QuerySeedElementCommand cmd)
        {
            var mesh    = mc.MeshObject;
            var toWorld = mc.WorldMatrix;
            Vector3 target = cmd.WorldPosition;

            int   vertex   = -1;
            int   vertex2  = -1;
            int   face     = -1;
            float distance = 0f;
            bool  found    = false;

            switch (cmd.Mode)
            {
                case PLSeedMode.NearestVertex:
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        var v = mesh.Vertices[i];
                        if (v == null) continue;
                        float d = Vector3.Distance(toWorld.MultiplyPoint3x4(v.Position), target);
                        if (d >= best) continue;
                        best = d; vertex = i; found = true;
                    }
                    distance = found ? best : 0f;
                    break;
                }

                case PLSeedMode.NearestFace:
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < mesh.Faces.Count; i++)
                    {
                        var f = mesh.Faces[i];
                        if (f == null || !f.IsValid) continue;

                        var sum   = Vector3.zero;
                        int taken = 0;
                        for (int j = 0; j < f.VertexIndices.Count; j++)
                        {
                            int vi = f.VertexIndices[j];
                            if (vi < 0 || vi >= mesh.Vertices.Count) continue;
                            var v = mesh.Vertices[vi];
                            if (v == null) continue;
                            sum += toWorld.MultiplyPoint3x4(v.Position);
                            taken++;
                        }
                        if (taken == 0) continue;

                        float d = Vector3.Distance(sum / taken, target);
                        if (d >= best) continue;
                        best = d; face = i; found = true;
                    }
                    distance = found ? best : 0f;
                    break;
                }

                case PLSeedMode.NearestBoundaryVertex:
                {
                    var holes = BridgeAutoPairOps.CollectHoles(mesh, toWorld);
                    float best = float.MaxValue;
                    for (int i = 0; i < holes.Count; i++)
                    {
                        var h = holes[i];
                        for (int j = 0; j < h.Vertices.Count; j++)
                        {
                            float d = Vector3.Distance(h.WorldPositions[j], target);
                            if (d >= best) continue;
                            best = d; vertex = h.Vertices[j]; found = true;
                        }
                    }
                    distance = found ? best : 0f;
                    break;
                }

                case PLSeedMode.NearestBoundaryEdge:
                {
                    // 辺の代表点は 2 頂点の中点。境界辺は BoundaryEdgeOps が集める。
                    var edges = BoundaryEdgeOps.CollectBoundaryEdges(mesh);
                    float best = float.MaxValue;
                    foreach (var e in edges)
                    {
                        if (e.V1 < 0 || e.V1 >= mesh.Vertices.Count) continue;
                        if (e.V2 < 0 || e.V2 >= mesh.Vertices.Count) continue;
                        var a = mesh.Vertices[e.V1];
                        var b = mesh.Vertices[e.V2];
                        if (a == null || b == null) continue;

                        Vector3 pa = toWorld.MultiplyPoint3x4(a.Position);
                        Vector3 pb = toWorld.MultiplyPoint3x4(b.Position);
                        float d = Vector3.Distance((pa + pb) * 0.5f, target);
                        if (d >= best) continue;
                        best = d; vertex = e.V1; vertex2 = e.V2; found = true;
                    }
                    distance = found ? best : 0f;
                    break;
                }

                case PLSeedMode.FirstBoundaryVertex:
                {
                    var holes = BridgeAutoPairOps.CollectHoles(mesh, toWorld);
                    if (holes.Count > 0 && holes[0].Vertices.Count > 0)
                    {
                        vertex = holes[0].Vertices[0];
                        found  = true;
                    }
                    break;
                }
            }

            var values = new List<PLDataValue>
            {
                PLDataValue.Num("masterIndex", cmd.MasterIndex),
                PLDataValue.Str("mode",        cmd.Mode.ToString()),
                PLDataValue.Num("found",       found ? 1 : 0),
                PLDataValue.Num("vertex",      vertex),
                PLDataValue.Num("vertex2",     vertex2),
                PLDataValue.Num("face",        face),
                PLDataValue.Num("distance",    distance),
            };

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "seed"), values,
                masterIndex: cmd.MasterIndex, objectId: mc.ObjectId,
                source: PanelCommandFactory.ActionOf(typeof(QuerySeedElementCommand))));

            return CommandDataJson.New()
                .Entry("entry",     entry)
                .Int("masterIndex", cmd.MasterIndex)
                .Text("mode",       cmd.Mode.ToString())
                .Flag("found",      found)
                .Int("vertex",      vertex)
                .Int("vertex2",     vertex2)
                .Int("face",        face)
                .Num("distance",    distance)
                .Build();
        }

        /// <summary>ボーン階層とスキンウェイトの分布。</summary>
        private static string BuildBoneSkinData(ModelContext model, QueryBoneSkinCommand cmd)
        {
            var bones  = model.Bones;
            var values = new List<PLDataValue>();

            int roots            = 0;
            int maxDepth         = 0;
            int humanoidAssigned = 0;

            for (int i = 0; i < bones.Count; i++)
            {
                var ent    = bones[i];
                var boneMc = ent.Context;
                int parent = boneMc?.HierarchyParentIndex ?? -1;
                string human = boneMc?.MeshObject?.HumanBodyBone ?? "";

                if (parent < 0) roots++;
                if (!string.IsNullOrEmpty(human)) humanoidAssigned++;

                int depth = DepthOfBone(model, ent.MasterIndex);
                if (depth > maxDepth) maxDepth = depth;

                string key = $"bone.{i}";
                values.Add(PLDataValue.Num(key + ".masterIndex",   ent.MasterIndex));
                values.Add(PLDataValue.Str(key + ".objectId",      IdText(boneMc?.ObjectId ?? 0UL)));
                values.Add(PLDataValue.Str(key + ".name",          ent.Name ?? ""));
                values.Add(PLDataValue.Num(key + ".parentMasterIndex", parent));
                values.Add(PLDataValue.Num(key + ".depth",         depth));
                values.Add(PLDataValue.Str(key + ".humanBodyBone", human));
            }

            // ウェイトの分布。BoneWeight のボーン番号はマスター索引で入っている
            // （TypedMeshIndices.ConvertBoneWeightToLocal が Unity へ渡す直前に
            //  ボーンリスト索引へ直す）ので、そのまま数える。
            var usedBones        = new HashSet<int>();
            int weightedVertices = 0;
            int skinnedDrawables = 0;

            foreach (var ent in model.DrawableMeshes)
            {
                if (cmd.MasterIndex >= 0 && ent.MasterIndex != cmd.MasterIndex) continue;

                var mesh = ent.Context?.MeshObject;
                if (mesh == null) continue;

                int hitsHere = 0;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var v = mesh.Vertices[i];
                    if (v == null || !v.HasBoneWeight) continue;

                    var w = v.BoneWeight.Value;
                    hitsHere++;

                    if (w.weight0 > 0f) usedBones.Add(w.boneIndex0);
                    if (w.weight1 > 0f) usedBones.Add(w.boneIndex1);
                    if (w.weight2 > 0f) usedBones.Add(w.boneIndex2);
                    if (w.weight3 > 0f) usedBones.Add(w.boneIndex3);
                }

                if (hitsHere == 0) continue;
                weightedVertices += hitsHere;
                skinnedDrawables++;
            }

            values.Add(PLDataValue.Num("bones",            bones.Count));
            values.Add(PLDataValue.Num("roots",            roots));
            values.Add(PLDataValue.Num("maxDepth",         maxDepth));
            values.Add(PLDataValue.Num("humanoidAssigned", humanoidAssigned));
            values.Add(PLDataValue.Num("skinnedDrawables", skinnedDrawables));
            values.Add(PLDataValue.Num("weightedVertices", weightedVertices));
            values.Add(PLDataValue.Num("usedBones",        usedBones.Count));

            var store = model.DataStore;
            var entry = store.Put(PLDataEntry.FromValues(
                ResolveResultName(store, cmd.ResultName, "boneSkin"), values,
                masterIndex: cmd.MasterIndex, objectId: 0UL,
                source: PanelCommandFactory.ActionOf(typeof(QueryBoneSkinCommand))));

            return CommandDataJson.New()
                .Entry("entry",           entry)
                .Int("bones",             bones.Count)
                .Int("roots",             roots)
                .Int("maxDepth",          maxDepth)
                .Int("humanoidAssigned",  humanoidAssigned)
                .Int("skinnedDrawables",  skinnedDrawables)
                .Int("weightedVertices",  weightedVertices)
                .Int("usedBones",         usedBones.Count)
                .Build();
        }

        /// <summary>
        /// 根からの深さ。根は 0。
        /// 親をたどる回数は要素数で打ち切る。壊れたデータで環ができていても止まる。
        /// </summary>
        private static int DepthOfBone(ModelContext model, int masterIndex)
        {
            int depth = 0;
            int cur   = masterIndex;
            int guard = model.Count;

            while (guard-- > 0)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) break;

                int parent = mc.HierarchyParentIndex;
                if (parent < 0 || parent == cur) break;

                cur = parent;
                depth++;
            }
            return depth;
        }
    }
}
