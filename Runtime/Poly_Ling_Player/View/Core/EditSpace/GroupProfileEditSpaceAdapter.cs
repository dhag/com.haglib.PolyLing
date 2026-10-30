// GroupProfileEditSpaceAdapter.cs
// 作業空間の断面系アダプタ（オブジェクトグループの項目の断面）。
// Runtime/Poly_Ling_Player/View/Core/EditSpace/ に配置
//
// 【何をするか】（PolyLing_作り直し整備計画.md 段 6「断面の線分 → 断面」）
//   元データはグループの項目が控えた断面（点列を持っている項目の引数）。
//   代理は開くときに断面から作った線分オブジェクト（断面 → 線分 ＝ 正方向）。
//   反映 ＝ 逆方向：代理の線分から断面を読み（取り込みと同じ規則 ProfileAcquire）、
//          項目の引数へ書いて作り直し、下流を流す（setObjectGroupStepArgs と同じ処理。Undo 1 回）。
//   取消し ＝ 項目の断面から線分を作り直して代理を置き換える。
//   閉じると代理を消す。
//
// 【対象】断面を引数に持ち、断面の取り込み元（線分オブジェクト）を記録していない項目。
//   取り込み元を記録している項目は、その線分を直接編集すれば追随する（段 4）ので対象にしない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditSpace;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.Revolution;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public sealed class GroupProfileEditSpaceAdapter : IEditSpaceAdapter
    {
        public string GroupName { get; }
        public int    StepIndex { get; }
        public ProfileEditKind ProfileKind { get; }

        private readonly Func<ProjectContext> _getProject;
        private readonly Func<PanelCommand, string> _send;
        private readonly int _modelIndex;

        public GroupProfileEditSpaceAdapter(
            Func<ProjectContext> getProject, Func<PanelCommand, string> send, int modelIndex,
            string groupName, int stepIndex, ProfileEditKind kind)
        {
            _getProject = getProject;
            _send       = send;
            _modelIndex = modelIndex;
            GroupName   = groupName;
            StepIndex   = stepIndex;
            ProfileKind = kind;
        }

        public string Kind           => PlayerPrimitiveMeshSubPanel.KindName(ProfileKind);
        public string DisplayName    => $"{PlayerPrimitiveMeshSubPanel.KindDisplayName(ProfileKind)}（{GroupName}#{StepIndex}）";
        public bool   TemporaryProxy => false;
        public EditSpacePlaneConstraint PlaneConstraint => EditSpacePlaneConstraint.DefaultOn;
        public EditSpaceBackdropSource  BackdropSource  => EditSpaceBackdropSource.UserImage;
        public Vector2 DisplayUnitScale => Vector2.one;

        // ================================================================
        // 項目の断面
        // ================================================================

        /// <summary>項目の断面（今の引数から）。</summary>
        public sealed class Profile
        {
            public string Key = "";
            public string LoopStartsKey = "";
            public string LoopIsHoleKey = "";
            public bool   IsLoops;
            public bool   Closed;
            public bool   Normalize;
            public List<Vector3> Points = new List<Vector3>();
            public List<Loop>    Loops  = new List<Loop>();
        }

        /// <summary>項目の今の断面を読む。読めなければ理由。</summary>
        public static string ReadItemProfile(
            ProjectContext project, int modelIndex, string groupName, int stepIndex, ProfileEditKind kind, out Profile prof)
        {
            prof = null;
            var model = project?.GetModel(modelIndex);
            var g = model?.FindObjectGroupByName(groupName);
            if (g == null) return $"グループが見つかりません: {groupName}";
            var step = g.GetStep(stepIndex);
            if (step == null) return $"項目 {stepIndex} がありません";

            var cmd = ObjectGroupOps.BuildCommand(project, modelIndex, g, stepIndex, out string err);
            if (cmd == null) return err ?? "項目のコマンドを組めません";
            var t = cmd.GetType();

            if (cmd is CreatePrimitiveMeshCommand pc && pc.ProfileAcquire != ProfileAcquireMethod.Baked && pc.ProfileSourceIndex >= 0)
            {
                var src = model.GetMeshContext(pc.ProfileSourceIndex);
                return $"この項目は断面を線分オブジェクト「{src?.Name}」から取り込んでいます。その線分を編集すれば作り直しが追随します";
            }

            var args = PanelCommandFactory.ToArgs(cmd);
            PanelCommandFactory.ProfileKey? pk = null;
            foreach (var k in PanelCommandFactory.ProfileKeys(t)) { pk = k; break; }

            prof = new Profile();
            switch (kind)
            {
                case ProfileEditKind.Revolution when cmd is CreateRevolutionCommand rc:
                    prof.Points = new List<Vector3>(rc.Params.Profile ?? Array.Empty<Vector3>());
                    prof.Closed = rc.Params.CloseLoop;
                    break;
                case ProfileEditKind.Profile2D when cmd is CreateProfile2DCommand p2:
                    prof.IsLoops = true;
                    foreach (var ld in p2.Params.ResolveLoops()) prof.Loops.Add(ld.ToLoop());
                    break;
                case ProfileEditKind.Pipe when cmd is CreatePipeCommand pp:
                    prof.Points = new List<Vector3>(pp.Profile ?? Array.Empty<Vector3>());
                    prof.Closed = pp.ProfileClosed;
                    break;
                case ProfileEditKind.FrillA when cmd is CreateFrillCommand fa:
                    prof.Points = new List<Vector3>(fa.ProfileA ?? Array.Empty<Vector3>());
                    break;
                case ProfileEditKind.FrillB when cmd is CreateFrillCommand fb:
                    prof.Points = new List<Vector3>(fb.ProfileB ?? Array.Empty<Vector3>());
                    prof.Key = PanelCommandFactory.KeyOfProperty(t, nameof(CreateFrillCommand.ProfileB));
                    prof.Normalize = true;
                    return string.IsNullOrEmpty(prof.Key) ? "断面 B のキーが引けません" : null;
                default:
                    prof = null;
                    return $"項目 {step.Action} は断面「{PlayerPrimitiveMeshSubPanel.KindName(kind)}」を持ちません";
            }

            if (pk == null) { prof = null; return $"{step.Action} に断面のキーがありません"; }
            prof.Key           = pk.Value.Key;
            prof.LoopStartsKey = pk.Value.LoopStartsKey ?? "";
            prof.LoopIsHoleKey = pk.Value.LoopIsHoleKey ?? "";
            prof.Normalize     = pk.Value.NormalizeFor(args);
            return null;
        }

        /// <summary>断面 → 線分（正方向）。</summary>
        public static MeshObject BuildLineMesh(Profile p, string name)
        {
            if (p == null) return null;
            return p.IsLoops
                ? LineProfileExtractor.LoopsToLineMesh(p.Loops, name)
                : LineProfileExtractor.PolylineToLineMesh(p.Points, name, p.Closed);
        }

        /// <summary>線分 → 断面（逆方向）。取り込みと同じ規則（ProfileAcquire）で読む。</summary>
        private static string ReadLine(Profile like, MeshContext proxy, out Profile read)
        {
            read = null;
            var method = like.IsLoops || like.Closed ? ProfileAcquireMethod.LineLoops : ProfileAcquireMethod.LinePolyline;
            var got = ProfileAcquire.Acquire(proxy, method, like.Normalize);
            if (!got.Ok) return got.Message;
            read = new Profile
            {
                Key = like.Key, LoopStartsKey = like.LoopStartsKey, LoopIsHoleKey = like.LoopIsHoleKey,
                IsLoops = like.IsLoops, Closed = like.Closed, Normalize = like.Normalize,
                Points = got.Points ?? new List<Vector3>(),
                Loops  = got.Loops  ?? new List<Loop>(),
            };
            return null;
        }

        private static bool SameProfile(Profile a, Profile b)
        {
            const float Eps = 1e-4f;
            bool Same(List<Vector3> x, List<Vector3> y)
            {
                if (x == null || y == null || x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++) if ((x[i] - y[i]).sqrMagnitude > Eps * Eps) return false;
                return true;
            }
            if (a.IsLoops)
            {
                if (a.Loops.Count != b.Loops.Count) return false;
                for (int i = 0; i < a.Loops.Count; i++)
                    if (!Same(a.Loops[i]?.Points, b.Loops[i]?.Points)) return false;
                return true;
            }
            return Same(a.Points, b.Points);
        }

        // ================================================================
        // IEditSpaceAdapter
        // ================================================================

        public bool HasChanges(MeshContext source, MeshContext proxy)
        {
            if (proxy?.MeshObject == null) return false;
            if (ReadItemProfile(_getProject?.Invoke(), _modelIndex, GroupName, StepIndex, ProfileKind, out var cur) != null) return false;
            if (ReadLine(cur, proxy, out var read) != null) return true;
            return !SameProfile(cur, read);
        }

        public string Reset(MeshContext source, MeshContext proxy)
        {
            if (proxy?.MeshObject == null) return "代理がありません";
            string why = ReadItemProfile(_getProject?.Invoke(), _modelIndex, GroupName, StepIndex, ProfileKind, out var cur);
            if (why != null) return why;
            var mo = BuildLineMesh(cur, proxy.Name);
            if (mo == null || mo.FaceCount == 0) return "断面から線分を作れません";

            var dst = proxy.MeshObject;
            dst.Clear();
            dst.Vertices.AddRange(mo.Vertices);
            dst.Faces.AddRange(mo.Faces);
            dst.LineGroups = mo.LineGroups ?? new List<LineGroup>();
            dst.InvalidatePositionCache();
            return null;
        }

        public string Apply(MeshContext source, MeshContext proxy,
                            List<int> problemSourceFaces, List<int> problemProxyFaces)
        {
            if (proxy?.MeshObject == null) return "代理がありません";
            string why = ReadItemProfile(_getProject?.Invoke(), _modelIndex, GroupName, StepIndex, ProfileKind, out var cur);
            if (why != null) return why;
            why = ReadLine(cur, proxy, out var read);
            if (why != null) return why;

            var keys = new List<string>();
            var vals = new List<string>();
            if (read.IsLoops)
            {
                var flat = new List<float>(); var starts = new List<int>(); var holes = new List<bool>();
                foreach (var lp in read.Loops)
                {
                    if (lp?.Points == null || lp.Points.Count < 3) continue;
                    starts.Add(flat.Count / 3);
                    holes.Add(lp.IsHole);
                    foreach (var q in lp.Points) { flat.Add(q.x); flat.Add(q.y); flat.Add(q.z); }
                }
                if (starts.Count == 0) return "3 点以上のループがありません";
                keys.Add(read.Key); vals.Add(ObjectGroupOps.JoinFloats(flat));
                if (!string.IsNullOrEmpty(read.LoopStartsKey)) { keys.Add(read.LoopStartsKey); vals.Add(ObjectGroupOps.JoinInts(starts)); }
                if (!string.IsNullOrEmpty(read.LoopIsHoleKey)) { keys.Add(read.LoopIsHoleKey); vals.Add(ObjectGroupOps.JoinBools(holes)); }
            }
            else
            {
                if (read.Points.Count < 2) return "断面の点が 2 つ未満です";
                keys.Add(read.Key); vals.Add(ObjectGroupOps.JoinVector3(read.Points));
            }

            return _send?.Invoke(new SetObjectGroupStepArgsCommand(
                _modelIndex, GroupName, StepIndex, keys.ToArray(), vals.ToArray(), allowProfileKeys: true))
                ?? null;
        }
    }
}
