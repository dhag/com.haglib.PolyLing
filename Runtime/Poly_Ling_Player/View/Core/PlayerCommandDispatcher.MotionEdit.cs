// PlayerCommandDispatcher.MotionEdit.cs
// モーションの試し再生（窓口 motionClip）の編集の構造 edit の照会・編集コマンドの受け口（PanelCommand.MotionEdit.cs）。
// PolyLing_モーション編集構造計画.md 段 4。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【振り分け】DispatchMotionFile の default から呼ぶ（モデルを見ないので、プロジェクト・モデルの null 門より前）。
// 【照会】窓口のクリップは変えない。edit の無いクリップは写しに EnsureEdit・NormalizeEdit を当てて答える
//   （最初の書き込みの MotionEditOps.Run と同じ順。id も同じになる）。
// 【書き込み】窓口の口を呼ぶだけ。取り消しは口が積む。口の Error が空でなければ Fail。

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Motion;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>edit の照会・編集のコマンドなら処理して true を返す。</summary>
        private bool DispatchMotionEdit(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryMotionEditTracksCommand _:  RunQueryMotionEditTracks();  return true;
                case QueryMotionEditKeysCommand c:    RunQueryMotionEditKeys(c);   return true;
                case QueryMotionEditBundlesCommand _: RunQueryMotionEditBundles(); return true;
                case SampleMotionEditCommand c:       RunSampleMotionEdit(c);      return true;

                case MotionEditAddTrackCommand c:
                    RunMotionEdit(c.Time, true, (h, t) => h.EditAddTrack(c.Role, c.Target, c.TargetKind, c.Name, t)); return true;
                case MotionEditRemoveTrackCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditRemoveTrack(c.TrackId, t)); return true;
                case MotionEditSetWrapCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetWrap(c.TrackId, c.Pre, c.Post, t)); return true;
                case MotionEditAddClipCommand c:
                    RunMotionEdit(c.Time, true, (h, t) => h.EditAddClip(c.TrackId, c.Start, c.Len, c.Name, t)); return true;
                case MotionEditRemoveClipCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditRemoveClip(c.ClipId, t)); return true;
                case MotionEditSetClipCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetClip(c.ClipId, c.Start, c.Len, c.Loop, c.Weight, t)); return true;
                case MotionEditCaptureKeyCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditCaptureKey(c.ClipId, c.F, t)); return true;
                case MotionEditSetKeyValueCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetKeyValue(c.ClipId, c.Channel, c.F, c.Value, t)); return true;
                case MotionEditSetChannelKeyCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetChannelKey(c.ClipId, c.Channel, c.F, c.Value, t)); return true;
                case MotionEditRemoveChannelKeyCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditRemoveChannelKey(c.ClipId, c.Channel, c.F, t)); return true;
                case MotionEditRemoveKeyCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditRemoveKey(c.ClipId, c.F, t)); return true;
                case MotionEditMoveKeyCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditMoveKey(c.ClipId, c.F, c.NewF, t)); return true;
                case MotionEditSetTangentModeCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetTangentMode(c.ClipId, c.Channel, c.F, c.Side, c.Mode, t)); return true;
                case MotionEditSetTangentSlopeCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetTangentSlope(c.ClipId, c.Channel, c.F, c.Comp, c.Side, c.Slope, t)); return true;
                case MotionEditSetWeightedModeCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetWeightedMode(c.ClipId, c.Channel, c.F, c.Comp, c.WeightedMode, t)); return true;
                case MotionEditSetTangentWeightCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetTangentWeight(c.ClipId, c.Channel, c.F, c.Comp, c.Side, c.Weight, t)); return true;
                case MotionEditMakeBundleCommand c:
                    RunMotionEdit(c.Time, true, (h, t) => h.EditMakeBundle(c.ClipIds, c.BundleIds, c.Name, t)); return true;
                case MotionEditUngroupCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditUngroup(c.BundleId, t)); return true;
                case MotionEditCloneBundleCommand c:
                    RunMotionEdit(c.Time, true, (h, t) => h.EditCloneBundle(c.BundleId, c.DStart, t)); return true;
                case MotionEditDeleteBundleCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditDeleteBundle(c.BundleId, t)); return true;
                case MotionEditSetLayerCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSetLayer(c.BundleId, c.Enabled, c.Mode, c.Weight, c.Mute, c.Solo, t)); return true;
                case MotionEditSwapLayerCommand c:
                    RunMotionEdit(c.Time, false, (h, t) => h.EditSwapLayer(c.BundleId, c.Dir, t)); return true;
                default: return false;
            }
        }

        // ================================================================
        // 書き込み
        // ================================================================

        private void RunMotionEdit(float time, bool reportCreated, Action<MotionClipHandler, float> call)
        {
            var h = GetMotionClipHandler?.Invoke();
            if (h == null) { _pendingResult = CommandResult.Fail("モーションの試し再生の窓口がありません", CommandErrorCodes.NotReady); return; }
            if (h.Clip == null) { _pendingResult = CommandResult.Fail("モーションの試し再生にクリップを読み込んでいません", CommandErrorCodes.NotReady); return; }

            call(h, time < 0f ? h.LastTime : time);
            if (!string.IsNullOrEmpty(h.Error)) { _pendingResult = CommandResult.Fail(h.Error, CommandErrorCodes.InvalidArgs); return; }
            if (reportCreated)
                ReportData(CommandDataJson.New().Text("createdId", h.LastCreatedId ?? "").Build());
        }

        // ================================================================
        // 照会
        // ================================================================

        // 照会に使う DTO。窓口が edit を持てばそのもの（変えない）、無ければ写しに edit を作ったもの。
        private bool TryMotionEditView(out MotionClipDTO dto, out bool held)
        {
            dto = null; held = false;
            var h = GetMotionClipHandler?.Invoke();
            if (h == null) { _pendingResult = CommandResult.Fail("モーションの試し再生の窓口がありません", CommandErrorCodes.NotReady); return false; }
            if (h.Clip == null) { _pendingResult = CommandResult.Fail("モーションの試し再生にクリップを読み込んでいません", CommandErrorCodes.NotReady); return false; }
            dto = MotionEditOps.EditView(h.Clip, out held);   // パネルの表示と同じ作り方
            return true;
        }

        private static float MotionEditFps(MotionClipDTO dto) => dto.frameRate > 0f ? dto.frameRate : 30f;

        private static string MeKey(string role, string kind, string target) => role + "\n" + (kind ?? "") + "\n" + target;

        private void RunQueryMotionEditTracks()
        {
            if (!TryMotionEditView(out var dto, out bool held)) return;
            var e = dto.edit;

            var clipBundle = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var b in e.bundles ?? new List<MotionEditBundleDTO>())
                if (b?.clipIds != null) foreach (var id in b.clipIds) if (id != null && !clipBundle.ContainsKey(id)) clipBundle[id] = b.id;

            var tIds = new List<string>(); var tRoles = new List<string>(); var tKinds = new List<string>(); var tTargets = new List<string>();
            var tNames = new List<string>(); var tUnames = new List<string>(); var tCats = new List<string>(); var tProvs = new List<string>();
            var tPre = new List<string>(); var tPost = new List<string>(); var tDerived = new List<int>(); var tCounts = new List<int>();
            var cIds = new List<string>(); var cNames = new List<string>(); var cStarts = new List<float>(); var cLens = new List<float>();
            var cLoops = new List<int>(); var cWeights = new List<float>(); var cBundles = new List<string>();
            var cRot = new List<int>(); var cPos = new List<int>(); var cScl = new List<int>(); var cW = new List<int>();
            var covered = new HashSet<string>(StringComparer.Ordinal);

            foreach (var t in e.tracks ?? new List<MotionEditTrackDTO>())
            {
                if (t == null) continue;
                covered.Add(MeKey(t.role, t.targetKind, t.target));
                tIds.Add(t.id ?? ""); tRoles.Add(t.role ?? ""); tKinds.Add(t.targetKind ?? ""); tTargets.Add(t.target ?? "");
                tNames.Add(t.name ?? ""); tUnames.Add(t.uname ?? ""); tCats.Add(t.category ?? ""); tProvs.Add(t.provider ?? "");
                tPre.Add(t.preWrapMode ?? ""); tPost.Add(t.postWrapMode ?? "");
                tDerived.Add(MotionEditOps.IsDerivedRole(dto, t.role) ? 1 : 0);
                int n = 0;
                foreach (var c in t.clips ?? new List<MotionEditClipDTO>())
                {
                    if (c == null) continue;
                    n++;
                    cIds.Add(c.id ?? ""); cNames.Add(c.name ?? ""); cStarts.Add(c.start); cLens.Add(c.len);
                    cLoops.Add(c.loop ? 1 : 0); cWeights.Add(c.weight);
                    cBundles.Add(c.id != null && clipBundle.TryGetValue(c.id, out var bid) ? bid : "");
                    cRot.Add(c.rot?.Count ?? 0); cPos.Add(c.pos?.Count ?? 0); cScl.Add(c.scl?.Count ?? 0); cW.Add(c.w?.Count ?? 0);
                }
                tCounts.Add(n);
            }

            var uncovered = new List<string>();
            void Flat(string role, string kind, string target)
            {
                if (!covered.Contains(MeKey(role, kind, target))) uncovered.Add(role + ":" + target);
            }
            if (dto.bones != null) foreach (var x in dto.bones) if (x != null) Flat(MotionEditDTO.RoleBones, x.targetKind, x.id);
            if (dto.body != null) Flat(MotionEditDTO.RoleBody, MotionClipDTO.TargetBody, MotionClipDTO.BodyTrackId);
            if (dto.lookAt != null) Flat(MotionEditDTO.RoleLookAt, MotionClipDTO.TargetLookAt, MotionClipDTO.LookAtTrackId);
            if (dto.bakedBones != null) foreach (var x in dto.bakedBones) if (x != null) Flat(MotionEditDTO.RoleBakedBones, MotionClipDTO.TargetHumanoid, x.id);
            if (dto.expressions != null)
                foreach (var x in dto.expressions)
                    if (x != null)
                    {
                        if (string.IsNullOrEmpty(x.path)) Flat(MotionEditDTO.RoleExpressions, null, x.name);
                        else uncovered.Add(MotionEditDTO.RoleExpressions + ":" + x.name + "（path " + x.path + "）");
                    }
            if (dto.muscles != null) foreach (var x in dto.muscles) if (x != null) Flat(MotionEditDTO.RoleMuscles, null, x.name);

            ReportData(CommandDataJson.New()
                .Flag ("editHeld",         held)
                .Num  ("frameRate",        MotionEditFps(dto))
                .Texts("trackIds",         tIds)
                .Texts("trackRoles",       tRoles)
                .Texts("trackTargetKinds", tKinds)
                .Texts("trackTargets",     tTargets)
                .Texts("trackNames",       tNames)
                .Texts("trackUnames",      tUnames)
                .Texts("trackCategories",  tCats)
                .Texts("trackProviders",   tProvs)
                .Texts("trackPreWrap",     tPre)
                .Texts("trackPostWrap",    tPost)
                .Ints ("trackDerived",     tDerived)
                .Ints ("trackClipCounts",  tCounts)
                .Texts("clipIds",          cIds)
                .Texts("clipNames",        cNames)
                .Nums ("clipStarts",       cStarts)
                .Nums ("clipLens",         cLens)
                .Ints ("clipLoops",        cLoops)
                .Nums ("clipWeights",      cWeights)
                .Texts("clipBundleIds",    cBundles)
                .Ints ("clipRotKeys",      cRot)
                .Ints ("clipPosKeys",      cPos)
                .Ints ("clipSclKeys",      cScl)
                .Ints ("clipWKeys",        cW)
                .Texts("uncoveredFlat",    uncovered)
                .Build());
        }

        private void RunQueryMotionEditKeys(QueryMotionEditKeysCommand cmd)
        {
            if (!TryMotionEditView(out var dto, out bool held)) return;
            MotionEditClipDTO clip = null;
            foreach (var t in dto.edit.tracks ?? new List<MotionEditTrackDTO>())
            {
                clip = t?.clips?.FirstOrDefault(c => c != null && c.id == cmd.ClipId);
                if (clip != null) break;
            }
            if (clip == null) { _pendingResult = CommandResult.Fail($"クリップ {cmd.ClipId} がありません", CommandErrorCodes.InvalidArgs); return; }
            string only = cmd.Channel ?? "";
            if (only.Length > 0 && only != MotionEditOps.ChRot && only != MotionEditOps.ChPos && only != MotionEditOps.ChScl && only != MotionEditOps.ChW)
            { _pendingResult = CommandResult.Fail($"channel は rot / pos / scl / w（\"{only}\"）", CommandErrorCodes.InvalidArgs); return; }

            double fps = MotionEditFps(dto);
            var kCh = new List<string>(); var kF = new List<float>(); var kSec = new List<float>(); var kT0 = new List<float>(); var kHasT0 = new List<int>();
            var kCount = new List<int>(); var kVal = new List<float>();
            var tFlags = new List<int>(); var tIn = new List<float>(); var tOut = new List<float>(); var tInW = new List<float>(); var tOutW = new List<float>();
            var tL = new List<string>(); var tR = new List<string>(); var tWm = new List<string>();

            void Tan(MotionTangentDTO x)
            {
                int fl = 0;
                if (x != null)
                {
                    fl |= 1;
                    if (x.inTangent.HasValue)  fl |= 2;
                    if (x.outTangent.HasValue) fl |= 4;
                    if (x.inWeight.HasValue)   fl |= 8;
                    if (x.outWeight.HasValue)  fl |= 16;
                }
                tFlags.Add(fl);
                tIn.Add(x?.inTangent ?? 0f); tOut.Add(x?.outTangent ?? 0f);
                tInW.Add(x?.inWeight ?? 0f); tOutW.Add(x?.outWeight ?? 0f);
                tL.Add(x?.leftTangentMode ?? ""); tR.Add(x?.rightTangentMode ?? ""); tWm.Add(x?.weightedMode ?? "");
            }
            void Head(string ch, float f, float? t0)
            {
                kCh.Add(ch); kF.Add(f);
                kSec.Add((float)MotionEditFlattener.KeySeconds(clip.start, f, t0, fps));
                kT0.Add(t0 ?? 0f); kHasT0.Add(t0.HasValue ? 1 : 0);
            }
            void Vec(string ch, List<MotionEditKeyDTO> keys, int dim)
            {
                if (keys == null || (only.Length > 0 && only != ch)) return;
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    Head(ch, k.f, k.t0);
                    kCount.Add(dim);
                    for (int i = 0; i < dim; i++)
                    {
                        kVal.Add(k.v != null && k.v.Length > i ? k.v[i] : 0f);
                        Tan(k.tan != null && k.tan.Length > i ? k.tan[i] : null);
                    }
                }
            }
            Vec(MotionEditOps.ChRot, clip.rot, 4);
            Vec(MotionEditOps.ChPos, clip.pos, 3);
            Vec(MotionEditOps.ChScl, clip.scl, 3);
            if (clip.w != null && (only.Length == 0 || only == MotionEditOps.ChW))
                foreach (var k in clip.w)
                {
                    if (k == null) continue;
                    Head(MotionEditOps.ChW, k.f, k.t0);
                    kCount.Add(1);
                    kVal.Add(k.v);
                    Tan(k.tan);
                }

            ReportData(CommandDataJson.New()
                .Flag ("editHeld",         held)
                .Texts("keyChannels",      kCh)
                .Nums ("keyFrames",        kF)
                .Nums ("keySeconds",       kSec)
                .Nums ("keyT0",            kT0)
                .Ints ("keyHasT0",         kHasT0)
                .Ints ("keyValueCounts",   kCount)
                .Nums ("keyValues",        kVal)
                .Ints ("tanFlags",         tFlags)
                .Nums ("tanIn",            tIn)
                .Nums ("tanOut",           tOut)
                .Nums ("tanInWeight",      tInW)
                .Nums ("tanOutWeight",     tOutW)
                .Texts("tanLeftModes",     tL)
                .Texts("tanRightModes",    tR)
                .Texts("tanWeightedModes", tWm)
                .Build());
        }

        private void RunQueryMotionEditBundles()
        {
            if (!TryMotionEditView(out var dto, out bool held)) return;
            var ids = new List<string>(); var names = new List<string>(); var parents = new List<string>();
            var clipCounts = new List<int>(); var clipIds = new List<string>(); var childCounts = new List<int>(); var childIds = new List<string>();
            var lEn = new List<int>(); var lMode = new List<string>(); var lW = new List<float>(); var lMute = new List<int>(); var lSolo = new List<int>();
            foreach (var b in dto.edit.bundles ?? new List<MotionEditBundleDTO>())
            {
                if (b == null) continue;
                ids.Add(b.id ?? ""); names.Add(b.name ?? ""); parents.Add(b.parentId ?? "");
                clipCounts.Add(b.clipIds?.Count ?? 0); if (b.clipIds != null) foreach (var x in b.clipIds) clipIds.Add(x ?? "");
                childCounts.Add(b.childIds?.Count ?? 0); if (b.childIds != null) foreach (var x in b.childIds) childIds.Add(x ?? "");
                lEn.Add(b.layer != null ? 1 : 0);
                lMode.Add(b.layer?.mode ?? "");
                lW.Add(b.layer?.weight ?? 0f);
                lMute.Add(b.layer != null && b.layer.mute ? 1 : 0);
                lSolo.Add(b.layer != null && b.layer.solo ? 1 : 0);
            }
            var summary = MotionEditFlattener.LayerSummary(dto.edit.bundles);

            ReportData(CommandDataJson.New()
                .Flag ("editHeld",          held)
                .Texts("bundleIds",         ids)
                .Texts("bundleNames",       names)
                .Texts("bundleParentIds",   parents)
                .Ints ("bundleClipCounts",  clipCounts)
                .Texts("bundleClipIds",     clipIds)
                .Ints ("bundleChildCounts", childCounts)
                .Texts("bundleChildIds",    childIds)
                .Ints ("layerEnabled",      lEn)
                .Texts("layerModes",        lMode)
                .Nums ("layerWeights",      lW)
                .Ints ("layerMutes",        lMute)
                .Ints ("layerSolos",        lSolo)
                .Texts("layerOrder",        summary.Select(x => x.id).ToList())
                .Nums ("layerEffWeights",   summary.Select(x => (float)x.weight).ToList())
                .Ints ("layerEffMutes",     summary.Select(x => x.mute ? 1 : 0).ToList())
                .Build());
        }

        private void RunSampleMotionEdit(SampleMotionEditCommand cmd)
        {
            if (!TryMotionEditView(out var dto, out bool held)) return;
            if (cmd.Frames == null || cmd.Frames.Length == 0)
            { _pendingResult = CommandResult.Fail("frames を指定してください", CommandErrorCodes.InvalidArgs); return; }
            var tracks = dto.edit.tracks?.Where(t => t != null).ToList() ?? new List<MotionEditTrackDTO>();
            List<MotionEditTrackDTO> targets;
            if (cmd.TrackIds == null || cmd.TrackIds.Length == 0) targets = tracks;
            else
            {
                targets = new List<MotionEditTrackDTO>();
                foreach (var id in cmd.TrackIds)
                {
                    var t = tracks.FirstOrDefault(x => x.id == id);
                    if (t == null) { _pendingResult = CommandResult.Fail($"トラック {id} がありません", CommandErrorCodes.InvalidArgs); return; }
                    targets.Add(t);
                }
            }

            var sIds = new List<string>(); var sCh = new List<string>(); var sF = new List<float>(); var sCount = new List<int>(); var sVal = new List<float>();
            foreach (var t in targets)
            {
                string[] chs = MotionEditDTO.IsBoneRole(t.role) ? new[] { MotionEditOps.ChRot, MotionEditOps.ChPos, MotionEditOps.ChScl }
                             : MotionEditDTO.IsScalarRole(t.role) ? new[] { MotionEditOps.ChW } : Array.Empty<string>();
                foreach (var ch in chs)
                    foreach (var f in cmd.Frames)
                    {
                        var v = MotionEditFlattener.SampleTrackChannel(dto.edit, dto.frameRate, t.id, ch, f);
                        if (v == null) continue;
                        sIds.Add(t.id); sCh.Add(ch); sF.Add(f); sCount.Add(v.Length);
                        sVal.AddRange(v);
                    }
            }

            ReportData(CommandDataJson.New()
                .Flag ("editHeld",          held)
                .Texts("sampleTrackIds",    sIds)
                .Texts("sampleChannels",    sCh)
                .Nums ("sampleFrames",      sF)
                .Ints ("sampleValueCounts", sCount)
                .Nums ("sampleValues",      sVal)
                .Build());
        }
    }
}
