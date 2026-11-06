// PlayerCommandDispatcher.ModifierDragFollow.cs
// コマンドディスパッチャ：カーブの頂点をドラッグしている間、帯とパイプの出力へ位置だけを書く。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 6
//
// 【何をするか】評価器のきっかけ 2（ModifierEvaluator.SyncFromInput）から呼ばれる。
//   面構成はドラッグの最初の形のまま止め、位置だけを同じ計算で求めて出力へ書く。
//   作り直し（RunModifierStackStep）を通さないので、引数の文字列化・面の作り直し・
//   UnityMesh・GPU バッファの作り直しが無い。法線は直さない。
//   離したときに評価器の印で作り直すので、法線と面構成はそこで正しくなる。
//
// 【帯】EdgeRibbonFaceOps.BuildInto を今の位置で組み、頂点の位置だけ使う。
//   作り直しと同じ関数なので並びも同じ。
//
// 【パイプ】帯の頂点番号の組（BeltAcquire.AcquireStrips。検出は面の接続だけを見る）をドラッグの最初に 1 回取り、
//   毎フレームは位置を読むだけ。レールの位置が変わった梯子の部品だけ計算し直す（髪のように本数が多くても
//   ドラッグで動くのは数本のため）。作り直しの重複頂点の結合は、最初の形で控えた対応で同じ形に写す。
//
// 【書かない場合】出力の編集が未確定・評価できない空間・取り込み方の記録が無い・
//   頂点数が最初の形と合わなくなったとき。印は立つので離したときの作り直しで合う。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Modifiers;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Selection;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        private sealed class DragFollow
        {
            public MeshContext Output;
            public ModifierSpace.WriteBack Space;
            public Vector3 WorldPosition;
            public Vector3 PoseRotation;
            public Vector3 PoseScale = Vector3.one;

            // 帯
            public EdgeRibbonFaceCommand Ribbon;
            public List<KeyValuePair<MeshContext, ICollection<VertexPair>>> RibbonTargets;
            public HashSet<int> RibbonStarts;

            // パイプ
            public CreatePipeCommand Pipe;
            public MeshContext Band;
            public int BandVertexCount;
            public List<BeltAutoStrip> Strips;
            public BeltCsvEntry[] Templates;
            public List<Vector3> Section;
            public bool SectionClosed;
            public Vector3[][] LastRails;
            public int[] PartOffsets;
            public int[] PartCounts;
            public Vector3[] Raw;
            /// <summary>部品の頂点 → 出力の頂点（重複頂点の結合）。null は同じ番号。</summary>
            public int[] Map;
            public int[] MergedCounts;
            public bool FirstWrite = true;

            /// <summary>最後に書いた位置（計測を入れているときの差の確かめ用）。</summary>
            public Vector3[] LastWritten;
        }

        /// <summary>項目 → 追随の控え。作れない項目は null を入れて、このドラッグでは作り直さない。</summary>
        private readonly Dictionary<ModifierStackStep, DragFollow> _dragFollows = new Dictionary<ModifierStackStep, DragFollow>();

        /// <summary>ModifierEvaluator.GeneratorDragFollower の中身。</summary>
        private void FollowGeneratorDrag(ModelContext model, ModifierEvaluator.Entry e, MeshContext moved, Action<MeshContext> upload)
        {
            var project = _getProject();
            if (project == null || !ReferenceEquals(project.CurrentModel, model) || e?.Step == null) return;

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_dragFollows.TryGetValue(e.Step, out var f))
            {
                f = BuildDragFollow(project, model, e);
                _dragFollows[e.Step] = f;
            }
            if (f == null) return;

            Vector3[] pos = f.Ribbon != null ? FollowRibbon(f) : FollowPipe(f, moved);
            if (pos == null) return;

            var verts = f.Output.MeshObject.Vertices;
            if (pos.Length != verts.Count) return;
            for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
            f.Output.MeshObject.InvalidatePositionCache();
            ModifierEvaluator.NoteDragFollowed(f.Output);
            upload?.Invoke(f.Output);
            ModifierChainHandler.SyncMirrorSidePositions(model, f.Output, pos, upload);

            if (Poly_Ling.Diagnostics.PLDiag.Timing)
            {
                f.LastWritten = (Vector3[])pos.Clone();
                Debug.Log($"[PL/Timing] ドラッグの追随 {e.Label} {e.Step.Action}: {F1(MsSince(t0))} ms（{pos.Length} 頂点）");
            }
        }

        /// <summary>追随が最後に書いた位置を取り出す（RunModifierEvaluation が控えを消す前に呼ぶ）。</summary>
        private List<DragFollow> TakeFollowedPositions()
        {
            List<DragFollow> list = null;
            foreach (var f in _dragFollows.Values)
                if (f?.LastWritten != null) { if (list == null) list = new List<DragFollow>(); list.Add(f); }
            return list;
        }

        /// <summary>追随が書いた位置と、離したときの作り直しの結果の差を出す（診断スイッチ PLDiag.Timing）。</summary>
        private static void LogFollowDifference(List<DragFollow> followed)
        {
            if (followed == null) return;
            foreach (var f in followed)
            {
                var verts = f.Output.MeshObject?.Vertices;
                if (verts == null) continue;
                if (verts.Count != f.LastWritten.Length)
                {
                    Debug.Log($"[PL/Timing] 追随と作り直しの差 {f.Output.Name}: 頂点数 {f.LastWritten.Length} → {verts.Count}");
                    continue;
                }
                float max = 0f;
                for (int i = 0; i < verts.Count; i++)
                    max = Mathf.Max(max, (verts[i].Position - f.LastWritten[i]).magnitude);
                Debug.Log($"[PL/Timing] 追随と作り直しの差 {f.Output.Name}: 最大 {max:0.########}（{verts.Count} 頂点）");
            }
        }

        // ================================================================
        // 控えを作る
        // ================================================================

        private DragFollow BuildDragFollow(ProjectContext project, ModelContext model, ModifierEvaluator.Entry e)
        {
            if (e.Outputs.Count != 1 || e.Outputs[0]?.MeshObject == null) return null;
            var output = e.Outputs[0];
            if (ModifierEvaluator.IsOutputEditPending(model, e)) return null;

            var rbKeys = PanelCommandFactory.RebuildKeys(e.CommandType);
            if (!rbKeys.IsSupported || rbKeys.TargetIsArray) return null;
            if (!ComputeStepSpace(model, e.Step, output, true, out var space, out _)) return null;

            var cmd = BuildStepWriteBackCommand(project, project.CurrentModelIndex, e.Group, e.StepIndex, rbKeys,
                new List<int> { model.MeshContextList.IndexOf(output) }, out _);

            var f = new DragFollow { Output = output, Space = space };
            switch (cmd)
            {
                case EdgeRibbonFaceCommand r: return InitRibbonFollow(f, model, r) ? f : null;
                case CreatePipeCommand p:     return InitPipeFollow(f, model, p) ? f : null;
                default:                      return null;
            }
        }

        private static bool InitRibbonFollow(DragFollow f, ModelContext model, EdgeRibbonFaceCommand r)
        {
            if (string.IsNullOrEmpty(r.EdgeSetName) || r.MasterIndices == null) return false;
            f.Ribbon = r;
            f.WorldPosition = r.Placement.WorldPosition;
            f.RibbonTargets = new List<KeyValuePair<MeshContext, ICollection<VertexPair>>>();
            foreach (int idx in r.MasterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null || mc.Type == MeshType.Bone) continue;
                var edges = EdgeRibbonFaceTool.ResolveEdges(mc, r.EdgeSetName);
                if (edges == null || edges.Count == 0) continue;
                f.RibbonTargets.Add(new KeyValuePair<MeshContext, ICollection<VertexPair>>(mc, edges));
            }
            if (f.RibbonTargets.Count == 0) return false;
            if (r.StartVertexIds != null && r.StartVertexIds.Length > 0)
                f.RibbonStarts = new HashSet<int>(r.StartVertexIds);
            return true;
        }

        private static bool InitPipeFollow(DragFollow f, ModelContext model, CreatePipeCommand c)
        {
            if (c.AcquireMethod == BeltAcquireMethod.Baked) return false;
            var band = model.GetMeshContext(c.BeltSourceIndex);
            if (band?.MeshObject == null) return false;

            var got = BeltAcquire.AcquireStrips(band, c.AcquireMethod, c.AcquireCrossRows, c.AcquireSetName);
            if (!got.Ok) return false;

            // 取り直し（BeltAcquire.Acquire）が使う梯子と同じものを、同じ並びで残す。
            var strips = new List<BeltAutoStrip>();
            foreach (var st in got.Strips)
                if (st != null && st.RungCount >= 2) strips.Add(st);

            var templates = c.Belts;
            if (templates == null || templates.Length != strips.Count || strips.Count == 0) return false;

            f.Pipe = c;
            f.Band = band;
            f.BandVertexCount = band.MeshObject.VertexCount;
            f.Strips = strips;
            f.Templates = templates;
            f.Section = PrimitiveMeshFactory.PipeSectionOf(c, out f.SectionClosed);
            f.WorldPosition = c.Placement.WorldPosition;
            f.PoseRotation = c.PoseRotation;
            f.PoseScale = c.PoseScale;

            int n = strips.Count;
            f.LastRails = new Vector3[n][];
            f.PartOffsets = new int[n];
            f.PartCounts = new int[n];

            // 全部の部品を 1 回作り、頂点の範囲と、作り直しの重複頂点の結合の対応を控える。
            var all = new MeshObject(c.Params.MeshName);
            var parts = new MeshObject[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                var part = PrimitiveMeshFactory.GeneratePipeFollowPart(
                    c, BeltOf(f, i, band.MeshObject, out f.LastRails[i]), f.Section, f.SectionClosed);
                if (part == null) return false;
                parts[i] = part;
                f.PartOffsets[i] = total;
                f.PartCounts[i] = part.VertexCount;
                total += part.VertexCount;
                MeshObjectAppendOps.Append(all, part.Clone());
            }
            f.Raw = new Vector3[total];
            for (int i = 0; i < n; i++) WritePart(f, i, parts[i]);

            if (c.Placement.MergeDuplicateVertices && all.VertexCount >= 2)
            {
                f.Map = MeshMergeHelper.MergeAllVerticesAtSamePosition(all, PrimitiveMeshFactory.MergeEpsilon).VertexMap;
                if (f.Map != null)
                {
                    f.MergedCounts = new int[all.VertexCount];
                    foreach (int m in f.Map)
                        if (m >= 0 && m < f.MergedCounts.Length) f.MergedCounts[m]++;
                }
            }
            return all.VertexCount == f.Output.MeshObject.VertexCount;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        private static Vector3[] FollowRibbon(DragFollow f)
        {
            var dest = new MeshObject(EdgeRibbonFaceTool.DefaultMeshName);
            foreach (var t in f.RibbonTargets)
            {
                var world = ModifierSpace.BindWorldPositions(t.Key);
                if (world == null) return null;
                EdgeRibbonFaceOps.BuildInto(t.Key, t.Value, world, f.Ribbon.WidthWorld,
                    f.Ribbon.AddStartTag, f.Ribbon.AddEndTag, dest, f.RibbonStarts);
            }
            ApplyFollowPose(f, dest);
            var pos = new Vector3[dest.VertexCount];
            for (int i = 0; i < pos.Length; i++) pos[i] = dest.Vertices[i].Position;
            return pos;
        }

        private static Vector3[] FollowPipe(DragFollow f, MeshContext moved)
        {
            // 梯子の取り込み元が動いたときだけ（断面の取り込み元などは離したときの作り直しで合う）。
            if (!ReferenceEquals(moved, f.Band)) return null;
            var band = f.Band.MeshObject;
            if (band.VertexCount != f.BandVertexCount) return null;

            bool changed = f.FirstWrite;
            for (int i = 0; i < f.Strips.Count; i++)
            {
                if (SameRails(f, i, band)) continue;
                var part = PrimitiveMeshFactory.GeneratePipeFollowPart(
                    f.Pipe, BeltOf(f, i, band, out f.LastRails[i]), f.Section, f.SectionClosed);
                if (part == null || part.VertexCount != f.PartCounts[i]) return null;
                WritePart(f, i, part);
                changed = true;
            }
            if (!changed) return null;
            f.FirstWrite = false;

            if (f.Map == null) return f.Raw;

            // 結合した頂点は作り直しと同じく重心へ。
            var pos = new Vector3[f.MergedCounts.Length];
            for (int i = 0; i < f.Raw.Length && i < f.Map.Length; i++)
            {
                int m = f.Map[i];
                if (m >= 0 && m < pos.Length) pos[m] += f.Raw[i];
            }
            for (int i = 0; i < pos.Length; i++)
                if (f.MergedCounts[i] > 1) pos[i] /= f.MergedCounts[i];
            return pos;
        }

        /// <summary>部品へ作り直しと同じ姿勢・空間を掛け、頂点の範囲へ書く。</summary>
        private static void WritePart(DragFollow f, int i, MeshObject part)
        {
            PrimitiveMeshTransform.ApplyRotationScale(part, f.Pipe.BakedRotation, f.Pipe.BakedScale);
            ApplyFollowPose(f, part);
            int o = f.PartOffsets[i];
            for (int k = 0; k < f.PartCounts[i]; k++) f.Raw[o + k] = part.Vertices[k].Position;
        }

        /// <summary>書き戻し（ApplyReplaceTransforms）と同じ順で姿勢と評価空間を掛ける。</summary>
        private static void ApplyFollowPose(DragFollow f, MeshObject mo)
        {
            if (f.PoseRotation != Vector3.zero || f.PoseScale != Vector3.one)
                PrimitiveMeshTransform.ApplyRotationScale(mo, f.PoseRotation, f.PoseScale);
            if (f.WorldPosition != Vector3.zero)
                PrimitiveMeshTransform.Apply(mo, f.WorldPosition, Vector3.zero, Vector3.one);
            if (f.Space != null) ModifierSpace.ApplyMatrix(mo, f.Space.Matrix);
        }

        /// <summary>
        /// 帯の今の位置から梯子 1 本を起こす。左右・先端の位置は rails に控える（次のフレームの比較用）。
        /// 閉じ方と先端の有無は作り直しのコマンドの値（WriteBeltArgs が残した形）に合わせる。
        /// </summary>
        private static BeltCsvEntry BeltOf(DragFollow f, int i, MeshObject band, out Vector3[] rails)
        {
            var st = f.Strips[i];
            var tpl = f.Templates[i];
            var e = new BeltCsvEntry
            {
                Closed      = tpl.Closed,
                FlipWinding = tpl.FlipWinding,
                HeightScale = tpl.HeightScale,
            };
            for (int k = 0; k < st.RungCount; k++)
            {
                e.Left.Add(band.Vertices[st.Left[k]].Position);
                e.Right.Add(band.Vertices[st.Right[k]].Position);
            }
            if (tpl.StartPoint.HasValue && st.StartPoint >= 0) e.StartPoint = band.Vertices[st.StartPoint].Position;
            if (tpl.EndPoint.HasValue && st.EndPoint >= 0) e.EndPoint = band.Vertices[st.EndPoint].Position;

            rails = new Vector3[st.RungCount * 2 + 2];
            for (int k = 0; k < st.RungCount; k++)
            {
                rails[k * 2]     = e.Left[k];
                rails[k * 2 + 1] = e.Right[k];
            }
            rails[st.RungCount * 2]     = e.StartPoint ?? Vector3.zero;
            rails[st.RungCount * 2 + 1] = e.EndPoint ?? Vector3.zero;
            return e;
        }

        /// <summary>梯子 i のレールと先端が、前に計算したときと同じ位置か。</summary>
        private static bool SameRails(DragFollow f, int i, MeshObject band)
        {
            var st = f.Strips[i];
            var last = f.LastRails[i];
            for (int k = 0; k < st.RungCount; k++)
            {
                if (band.Vertices[st.Left[k]].Position != last[k * 2]) return false;
                if (band.Vertices[st.Right[k]].Position != last[k * 2 + 1]) return false;
            }
            var tpl = f.Templates[i];
            if (tpl.StartPoint.HasValue && st.StartPoint >= 0
                && band.Vertices[st.StartPoint].Position != last[st.RungCount * 2]) return false;
            if (tpl.EndPoint.HasValue && st.EndPoint >= 0
                && band.Vertices[st.EndPoint].Position != last[st.RungCount * 2 + 1]) return false;
            return true;
        }
    }
}
