// MotionEditOps.cs
// 編集の構造 edit（version 5）を変える操作。窓口 motionClip（MotionClipHandler）の編集の口の中身。
// Runtime/Poly_Ling_Main/Motion/ に配置
//
// 操作の意味は Motion Studio（データとツール/VRMビュワー/polyling_motion_studio/timeline.html）にそろえる。
// PolyLing_モーション編集構造計画.md 段 3。
//
// ■ 規則（恒久メモ）
//   流れ   … Run が次の順に行う：EnsureEdit → 平らにし直す（MotionEditFlattener.Reflatten）→ 操作 → 整え（NormalizeEdit）
//            → 平らにし直す → 派生の作り直し。操作がエラーなら dto は途中まで変わっているので、呼ぶ側は写しに対して呼ぶ。
//   EnsureEdit … Studio の importPolyLing の写し。edit の当たらない平らなトラックを、トラックごとにクリップ 1 本
//            （先頭 0、長さ max(1, ceil(maxT·fps − 1e-4) + 1)、キーは f = max(0, round(t·fps))、回転は正規化、t0 = t）で後ろに足す。
//            並びは bones・body・lookAt・bakedBones・expressions・muscles。派生のトラックと path を持つ表情は足さない。
//   派生   … bakedBones が正本（MotionClipDTO.BakedBonesArePrimary）なら muscles・body は Parse の BakeMuscles が作る派生。
//            muscles が正本なら bakedBones は保存の BakeBones が作る派生。派生のトラックは edit に足さず、口では扱わない。
//            bakedBones が正本で、操作で平らな bakedBones か lookAt が変わったら、muscles・body を捨てて BakeMuscles で作り直し、
//            edit の muscles・body のトラックを除く（Studio の保存が muscles・body を書かないのと、Parse がそれを読むのと同じ結果）。
//   id     … t・c・b に、edit の全 id の数字の最大値＋1 を付ける（Studio の bumpUid と uid）。
//   整え   … クリップを start 順（同じ start は元の順）に並べ、キーの無いチャンネルは null、空のバンドルの clipIds から無いクリップを除く。
//   キーのフレーム … Studio のクリップのキー操作と同じく、ローカルフレームを round(clamp(f, 0, len − 1)) にする。
//   キーを打つ … CaptureKey は全チャンネルに今の評価値で打つ（Studio の captureKey）。SetChannelKey は 1 チャンネルだけに値を書き、
//            RemoveChannelKey は 1 チャンネルのキーだけを除く（Studio の setChannelKey・removeChannelKey）。
//   接線   … Studio の chanData・tanSlot・keySlopes・setMode・setSlope・setWeightedMode・setWeight の写し。
//            キー列はキーの秒（KeySeconds）順。回転は隣と符号をそろえ、反転したキーは傾きの符号を戻して書く。
//            tan の無いキーは編集したときだけ両側 Linear で作る。

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public static class MotionEditOps
    {
        public const string ChRot = MotionEditFlattener.ChRot;
        public const string ChPos = MotionEditFlattener.ChPos;
        public const string ChScl = MotionEditFlattener.ChScl;
        public const string ChW   = MotionEditFlattener.ChW;

        public const string SideIn  = "in";
        public const string SideOut = "out";

        public const string DefaultClipName   = "クリップ";
        public const string DefaultBundleName = "バンドル";

        private static readonly string[] TanModes =
        {
            MotionCurveMath.ModeConstant, MotionCurveMath.ModeLinear, MotionCurveMath.ModeAuto,
            MotionCurveMath.ModeClampedAuto, MotionCurveMath.ModeFree,
        };

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// 操作 op を流れ（冒頭の規則）に乗せて行う。エラーなら理由を返す（dto は途中まで変わっている）。
        /// loadedDuration は読み込んだときの長さ（MotionClipSerializer.Length）。
        /// </summary>
        public static string Run(MotionClipDTO dto, double loadedDuration, Func<MotionClipDTO, string> op)
        {
            if (dto == null) return "クリップを読み込んでください";
            bool bakedPrimary = MotionClipDTO.BakedBonesArePrimary(dto);

            EnsureEdit(dto);
            NormalizeEdit(dto.edit);
            MotionEditFlattener.Reflatten(dto, loadedDuration);
            string sig0 = bakedPrimary ? BakedSignature(dto) : null;

            string err = op(dto);
            if (err != null) return err;

            NormalizeEdit(dto.edit);
            MotionEditFlattener.Reflatten(dto, loadedDuration);

            if (bakedPrimary && BakedSignature(dto) != sig0)
            {
                dto.muscles = new List<MotionScalarTrackDTO>();
                dto.body = null;
                var ignored = new List<string>();
                if (!CanonBoneMotion.BakeMuscles(dto, ignored, out bool baked, out string reason)) return reason;
                if (baked) dto.rotationSource = MotionClipDTO.RotationSourceBakedBones;
                RemoveTracksWhere(dto.edit, t => t.role == MotionEditDTO.RoleMuscles || t.role == MotionEditDTO.RoleBody);
            }
            return null;
        }

        private static string BakedSignature(MotionClipDTO dto)
            => JsonConvert.SerializeObject(new object[] { dto.bakedBones, dto.lookAt });

        // ================================================================
        // 派生
        // ================================================================

        /// <summary>role のトラックが派生（口では扱わない）か。</summary>
        public static bool IsDerivedRole(MotionClipDTO dto, string role)
        {
            if (MotionClipDTO.BakedBonesArePrimary(dto))
                return role == MotionEditDTO.RoleMuscles || role == MotionEditDTO.RoleBody;
            if (dto?.muscles != null && dto.muscles.Count > 0)
                return role == MotionEditDTO.RoleBakedBones;
            return false;
        }

        private static string DerivedError(MotionEditTrackDTO t)
            => $"トラック {t.id}（{t.role}:{t.target}）は別のトラックから作り直すトラックなので変えられません";

        // ================================================================
        // EnsureEdit（Studio の importPolyLing）
        // ================================================================

        /// <summary>
        /// edit が無ければ作り、edit の当たらない平らなトラックをクリップ 1 本の形で後ろに足す。足した数を返す。
        /// </summary>
        public static int EnsureEdit(MotionClipDTO dto) => EnsureEdit(dto, out _);

        /// <summary>EnsureEdit。dropped は同じフレームに丸まって捨てたキーの数（edit はフレームごとに 1 キー）。</summary>
        public static int EnsureEdit(MotionClipDTO dto, out int dropped)
        {
            dropped = 0;
            if (dto == null) return 0;
            if (dto.edit == null) dto.edit = new MotionEditDTO();
            if (dto.edit.tracks == null) dto.edit.tracks = new List<MotionEditTrackDTO>();
            if (dto.edit.bundles == null) dto.edit.bundles = new List<MotionEditBundleDTO>();

            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in dto.edit.tracks) if (t != null) covered.Add(Key(t.role, t.targetKind, t.target));

            var ids = Ids.Of(dto.edit);
            var form = ImportForm.Of(dto);
            int added = 0;
            void Bone(MotionTrackDTO src, string role, string kind, string target)
            {
                if (src == null || IsDerivedRole(dto, role) || !covered.Add(Key(role, kind, target))) return;
                string uname = role == MotionEditDTO.RoleBakedBones && src.targetKind == MotionClipDTO.TargetHumanoid ? target : null;
                dto.edit.tracks.Add(ImportBoneTrack(src, role, kind, target, uname, form, ids));
                added++;
            }
            void Scalar(MotionScalarTrackDTO src, string role)
            {
                if (src == null || IsDerivedRole(dto, role) || !covered.Add(Key(role, null, src.name))) return;
                dto.edit.tracks.Add(ImportScalarTrack(src, role, form, ids));
                added++;
            }

            if (dto.bones != null) foreach (var x in dto.bones) if (x != null) Bone(x, MotionEditDTO.RoleBones, x.targetKind, x.id);
            if (dto.body != null) Bone(dto.body, MotionEditDTO.RoleBody, MotionClipDTO.TargetBody, MotionClipDTO.BodyTrackId);
            if (dto.lookAt != null) Bone(dto.lookAt, MotionEditDTO.RoleLookAt, MotionClipDTO.TargetLookAt, MotionClipDTO.LookAtTrackId);
            if (dto.bakedBones != null) foreach (var x in dto.bakedBones) if (x != null) Bone(x, MotionEditDTO.RoleBakedBones, MotionClipDTO.TargetHumanoid, x.id);
            if (dto.expressions != null)
                foreach (var x in dto.expressions)
                    if (x != null && string.IsNullOrEmpty(x.path)) Scalar(x, MotionEditDTO.RoleExpressions);
            if (dto.muscles != null) foreach (var x in dto.muscles) if (x != null) Scalar(x, MotionEditDTO.RoleMuscles);
            dropped = form.dropped;
            return added;
        }

        /// <summary>平らなトラックごとの中身（JSON）。createLipSyncFromVoicevox の前後比較に使う。キーは (role, targetKind, target)。</summary>
        public static Dictionary<string, string> FlatSignatures(MotionClipDTO dto)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dto == null) return d;
            void Put(string key, object track) => d[key] = JsonConvert.SerializeObject(track);
            if (dto.bones != null) foreach (var x in dto.bones) if (x != null) Put(Key(MotionEditDTO.RoleBones, x.targetKind, x.id), x);
            if (dto.body != null) Put(Key(MotionEditDTO.RoleBody, MotionClipDTO.TargetBody, MotionClipDTO.BodyTrackId), dto.body);
            if (dto.lookAt != null) Put(Key(MotionEditDTO.RoleLookAt, MotionClipDTO.TargetLookAt, MotionClipDTO.LookAtTrackId), dto.lookAt);
            if (dto.bakedBones != null) foreach (var x in dto.bakedBones) if (x != null) Put(Key(MotionEditDTO.RoleBakedBones, MotionClipDTO.TargetHumanoid, x.id), x);
            if (dto.expressions != null)
                foreach (var x in dto.expressions)
                    if (x != null && string.IsNullOrEmpty(x.path)) Put(Key(MotionEditDTO.RoleExpressions, null, x.name), x);
            if (dto.muscles != null) foreach (var x in dto.muscles) if (x != null) Put(Key(MotionEditDTO.RoleMuscles, null, x.name), x);
            return d;
        }

        /// <summary>
        /// 平らなトラックだけを書き換えた後（createLipSyncFromVoicevox）に edit を合わせる。edit が無ければ何もしない。
        /// 平らなトラックが無くなった edit のトラックは除き、中身の変わったトラックはクリップ 1 本の形で作り直し（id は残す）、
        /// edit の当たらない平らなトラックは EnsureEdit と同じく後ろに足す。
        /// edit はチャンネルごと・整数フレームごとにキーを 1 個しか持てないので、取り込みで同じフレームに丸まったキーは後のものが残る。
        /// 捨てたキーの数を返す（呼ぶ側は続けて MotionEditFlattener.Reflatten で平らなトラックを edit に合わせる）。
        /// </summary>
        public static int SyncEditAfterFlatChange(MotionClipDTO dto, Dictionary<string, string> before)
        {
            if (dto?.edit?.tracks == null) return 0;
            var after = FlatSignatures(dto);
            var ids = Ids.Of(dto.edit);
            var form = ImportForm.Of(dto);
            foreach (var t in dto.edit.tracks.ToList())
            {
                if (t == null) continue;
                string key = Key(t.role, t.targetKind, t.target);
                if (!after.TryGetValue(key, out string now))
                {
                    RemoveTracksWhere(dto.edit, x => ReferenceEquals(x, t));
                    continue;
                }
                if (before != null && before.TryGetValue(key, out string was) && was == now) continue;
                MotionEditTrackDTO fresh = null;
                if (MotionEditDTO.IsBoneRole(t.role))
                {
                    var src = FindFlatBone(dto, t);
                    if (src != null) fresh = ImportBoneTrack(src, t.role, t.targetKind, t.target, t.uname, form, ids);
                }
                else
                {
                    var src = FindFlatScalar(dto, t);
                    if (src != null) fresh = ImportScalarTrack(src, t.role, form, ids);
                }
                if (fresh == null) continue;
                RemoveClipIds(dto.edit, t.clips?.Select(c => c?.id));
                t.clips = fresh.clips;
                t.preWrapMode = fresh.preWrapMode;
                t.postWrapMode = fresh.postWrapMode;
                if (t.role == MotionEditDTO.RoleExpressions) { t.category = fresh.category; t.provider = fresh.provider; }
            }
            EnsureEdit(dto, out int droppedNew);
            NormalizeEdit(dto.edit);
            return form.dropped + droppedNew;
        }

        private sealed class ImportForm
        {
            public double fps;
            public float len;
            public string clipName;
            /// <summary>同じフレームに丸まって上書きした（捨てた）キーの数（チャンネルごとに数える）。</summary>
            public int dropped;

            public static ImportForm Of(MotionClipDTO dto)
            {
                double fps = dto.frameRate > 0f && !float.IsNaN(dto.frameRate) && !float.IsInfinity(dto.frameRate) ? dto.frameRate : 30.0;
                double maxT = Math.Max(0.0, MotionClipSerializer.Length(dto));
                return new ImportForm
                {
                    fps = fps,
                    len = (float)Math.Max(1.0, Math.Ceiling(maxT * fps - 1e-4) + 1.0),
                    clipName = string.IsNullOrEmpty(dto.name) ? "PolyLingMotion" : dto.name,
                };
            }
        }

        private static MotionEditTrackDTO ImportBoneTrack(MotionTrackDTO src, string role, string kind, string target, string uname,
                                                         ImportForm form, Ids ids)
        {
            var et = new MotionEditTrackDTO
            {
                id = ids.Next("t"), role = role, targetKind = kind, target = target, name = target,
                uname = string.IsNullOrEmpty(uname) ? null : uname,
                preWrapMode = NullIfEmpty(src.preWrapMode), postWrapMode = NullIfEmpty(src.postWrapMode),
            };
            var c = new MotionEditClipDTO { id = ids.Next("c"), name = form.clipName, start = 0f, len = form.len, loop = false, weight = 1f };
            if (src.keys != null)
                foreach (var k in src.keys)
                {
                    if (k == null || !Finite(k.t)) continue;
                    float f = (float)Math.Max(0.0, MotionEditFlattener.RoundJs(k.t * form.fps));
                    if (Vec(k.rot, 4) && PutVec(ref c.rot, f, MotionEditFlattener.NormalizeQuatJs(Take(k.rot, 4)), k.rotTan, k.t)) form.dropped++;
                    if (Vec(k.pos, 3) && PutVec(ref c.pos, f, Take(k.pos, 3), k.posTan, k.t)) form.dropped++;
                    if (Vec(k.scl, 3) && PutVec(ref c.scl, f, Take(k.scl, 3), k.sclTan, k.t)) form.dropped++;
                }
            SortKeys(c);
            et.clips.Add(c);
            return et;
        }

        private static MotionEditTrackDTO ImportScalarTrack(MotionScalarTrackDTO src, string role, ImportForm form, Ids ids)
        {
            var et = new MotionEditTrackDTO
            {
                id = ids.Next("t"), role = role, targetKind = null, target = src.name, name = src.name,
                preWrapMode = NullIfEmpty(src.preWrapMode), postWrapMode = NullIfEmpty(src.postWrapMode),
            };
            if (src is MotionExpressionTrackDTO ex) { et.category = NullIfEmpty(ex.category); et.provider = NullIfEmpty(ex.provider); }
            var c = new MotionEditClipDTO { id = ids.Next("c"), name = form.clipName, start = 0f, len = form.len, loop = false, weight = 1f };
            if (src.keys != null)
                foreach (var k in src.keys)
                {
                    if (k == null || !Finite(k.t) || !Finite(k.v)) continue;
                    float f = (float)Math.Max(0.0, MotionEditFlattener.RoundJs(k.t * form.fps));
                    if (c.w == null) c.w = new List<MotionEditScalarKeyDTO>();
                    var ex2 = c.w.FirstOrDefault(x => x.f == f);
                    if (ex2 != null) form.dropped++;
                    var y = ex2 ?? new MotionEditScalarKeyDTO { f = f };
                    y.v = k.v;
                    y.tan = CloneTan(k.tan);
                    y.t0 = k.t;
                    if (ex2 == null) c.w.Add(y);
                }
            SortKeys(c);
            et.clips.Add(c);
            return et;
        }

        // Studio の plPut（同じフレームは上書き。tan が無ければ消す）。上書きした（前のキーを捨てた）なら true。
        private static bool PutVec(ref List<MotionEditKeyDTO> list, float f, float[] v, MotionTangentDTO[] tan, float t0)
        {
            if (list == null) list = new List<MotionEditKeyDTO>();
            var ex = list.FirstOrDefault(x => x.f == f);
            var k = ex ?? new MotionEditKeyDTO { f = f };
            k.v = v;
            k.tan = CloneTans(tan);
            k.t0 = t0;
            if (ex == null) list.Add(k);
            return ex != null;
        }

        private static MotionTrackDTO FindFlatBone(MotionClipDTO dto, MotionEditTrackDTO t)
        {
            switch (t.role)
            {
                case MotionEditDTO.RoleBones:      return dto.bones?.FirstOrDefault(x => x != null && x.id == t.target && x.targetKind == t.targetKind);
                case MotionEditDTO.RoleBakedBones: return dto.bakedBones?.FirstOrDefault(x => x != null && x.id == t.target);
                case MotionEditDTO.RoleBody:       return dto.body;
                case MotionEditDTO.RoleLookAt:     return dto.lookAt;
            }
            return null;
        }

        private static MotionScalarTrackDTO FindFlatScalar(MotionClipDTO dto, MotionEditTrackDTO t)
        {
            if (t.role == MotionEditDTO.RoleExpressions)
                return dto.expressions?.FirstOrDefault(x => x != null && x.name == t.target && string.IsNullOrEmpty(x.path));
            if (t.role == MotionEditDTO.RoleMuscles)
                return dto.muscles?.FirstOrDefault(x => x != null && x.name == t.target);
            return null;
        }

        private static void RemoveFlat(MotionClipDTO dto, MotionEditTrackDTO t)
        {
            switch (t.role)
            {
                case MotionEditDTO.RoleBones:      dto.bones?.RemoveAll(x => x != null && x.id == t.target && x.targetKind == t.targetKind); break;
                case MotionEditDTO.RoleBakedBones: dto.bakedBones?.RemoveAll(x => x != null && x.id == t.target); break;
                case MotionEditDTO.RoleBody:       dto.body = null; break;
                case MotionEditDTO.RoleLookAt:     dto.lookAt = null; break;
                case MotionEditDTO.RoleExpressions: dto.expressions?.RemoveAll(x => x != null && x.name == t.target && string.IsNullOrEmpty(x.path)); break;
                case MotionEditDTO.RoleMuscles:    dto.muscles?.RemoveAll(x => x != null && x.name == t.target); break;
            }
        }

        // ================================================================
        // 保存の前（MotionClipSerializer.PrepareForSave）
        // ================================================================

        /// <summary>
        /// 当たる平らなトラックの無い bakedBones の edit トラック（キーが残らず平らなトラックを作らなかったもの）を除き、
        /// そのクリップの id をバンドルの clipIds から除く（Studio の exportPolyLing と同じ）。除いた数を返す。
        /// </summary>
        public static int DropUnmatchedBakedTracks(MotionClipDTO dto)
        {
            if (dto?.edit?.tracks == null) return 0;
            return RemoveTracksWhere(dto.edit, t => t.role == MotionEditDTO.RoleBakedBones
                && (dto.bakedBones == null || !dto.bakedBones.Any(x => x != null && x.id == t.target)));
        }

        // ================================================================
        // 整え
        // ================================================================

        /// <summary>クリップを start 順（同じ start は元の順）に並べ、キーの無いチャンネルを null にし、clipIds の無いクリップを除く。</summary>
        public static void NormalizeEdit(MotionEditDTO e)
        {
            if (e == null) return;
            var clipIds = new HashSet<string>(StringComparer.Ordinal);
            if (e.tracks != null)
                foreach (var t in e.tracks)
                {
                    if (t == null) continue;
                    if (t.clips == null) t.clips = new List<MotionEditClipDTO>();
                    t.clips = t.clips.Where(c => c != null).Select((c, i) => (c, i)).OrderBy(x => x.c.start).ThenBy(x => x.i).Select(x => x.c).ToList();
                    foreach (var c in t.clips)
                    {
                        if (c.id != null) clipIds.Add(c.id);
                        if (c.rot != null && c.rot.Count == 0) c.rot = null;
                        if (c.pos != null && c.pos.Count == 0) c.pos = null;
                        if (c.scl != null && c.scl.Count == 0) c.scl = null;
                        if (c.w != null && c.w.Count == 0) c.w = null;
                    }
                }
            if (e.bundles != null)
                foreach (var b in e.bundles)
                    if (b?.clipIds != null) b.clipIds.RemoveAll(id => id == null || !clipIds.Contains(id));
        }

        private static void SortKeys(MotionEditClipDTO c)
        {
            if (c.rot != null) c.rot = StableByF(c.rot, k => k.f);
            if (c.pos != null) c.pos = StableByF(c.pos, k => k.f);
            if (c.scl != null) c.scl = StableByF(c.scl, k => k.f);
            if (c.w != null)   c.w   = StableByF(c.w, k => k.f);
        }

        private static List<T> StableByF<T>(List<T> l, Func<T, float> f)
            => l.Select((k, i) => (k, i)).OrderBy(x => f(x.k)).ThenBy(x => x.i).Select(x => x.k).ToList();

        private static int RemoveTracksWhere(MotionEditDTO e, Func<MotionEditTrackDTO, bool> pred)
        {
            if (e?.tracks == null) return 0;
            var gone = e.tracks.Where(t => t != null && pred(t)).ToList();
            foreach (var t in gone)
            {
                e.tracks.Remove(t);
                RemoveClipIds(e, t.clips?.Select(c => c?.id));
            }
            return gone.Count;
        }

        private static void RemoveClipIds(MotionEditDTO e, IEnumerable<string> ids)
        {
            if (e?.bundles == null || ids == null) return;
            var set = new HashSet<string>(ids.Where(x => x != null), StringComparer.Ordinal);
            foreach (var b in e.bundles) b?.clipIds?.RemoveAll(id => id != null && set.Contains(id));
        }

        // ================================================================
        // 引き当て
        // ================================================================

        private static MotionEditTrackDTO FindTrack(MotionClipDTO dto, string trackId)
            => dto.edit?.tracks?.FirstOrDefault(t => t != null && t.id == trackId);

        private static bool FindClip(MotionClipDTO dto, string clipId, out MotionEditTrackDTO track, out MotionEditClipDTO clip)
        {
            track = null; clip = null;
            if (dto.edit?.tracks == null || clipId == null) return false;
            foreach (var t in dto.edit.tracks)
            {
                var c = t?.clips?.FirstOrDefault(x => x != null && x.id == clipId);
                if (c != null) { track = t; clip = c; return true; }
            }
            return false;
        }

        private static MotionEditBundleDTO FindBundle(MotionClipDTO dto, string id)
            => dto.edit?.bundles?.FirstOrDefault(b => b != null && b.id == id);

        private static string ClipOf(MotionClipDTO dto, string clipId, out MotionEditTrackDTO t, out MotionEditClipDTO c)
        {
            if (!FindClip(dto, clipId, out t, out c)) return $"クリップ {clipId} がありません";
            if (IsDerivedRole(dto, t.role)) return DerivedError(t);
            return null;
        }

        private static string[] ChannelsOf(MotionEditTrackDTO t)
            => MotionEditDTO.IsBoneRole(t.role) ? new[] { ChRot, ChPos, ChScl } : new[] { ChW };

        // ================================================================
        // トラック
        // ================================================================

        /// <summary>トラックを足す（平らなトラックは平らにし直しで作る）。新しいトラックの id を createdId に入れる。</summary>
        public static string AddTrack(MotionClipDTO dto, string role, string targetKind, string target, string name, out string createdId)
        {
            createdId = null;
            bool bone = MotionEditDTO.IsBoneRole(role);
            if (!bone && !MotionEditDTO.IsScalarRole(role)) return $"role \"{role}\" は読めません（bones / bakedBones / body / lookAt / expressions / muscles）";
            if (IsDerivedRole(dto, role)) return $"role {role} は別のトラックから作り直すトラックなので足せません";
            switch (role)
            {
                case MotionEditDTO.RoleBones:
                    if (string.IsNullOrEmpty(targetKind)) targetKind = MotionClipDTO.TargetBoneName;
                    if (targetKind != MotionClipDTO.TargetBoneName && targetKind != MotionClipDTO.TargetPath)
                        return $"bones の targetKind は {MotionClipDTO.TargetBoneName} か {MotionClipDTO.TargetPath}（\"{targetKind}\"）";
                    break;
                case MotionEditDTO.RoleBakedBones: targetKind = MotionClipDTO.TargetHumanoid; break;
                case MotionEditDTO.RoleBody:       targetKind = MotionClipDTO.TargetBody;   target = MotionClipDTO.BodyTrackId;   break;
                case MotionEditDTO.RoleLookAt:     targetKind = MotionClipDTO.TargetLookAt; target = MotionClipDTO.LookAtTrackId; break;
                default:                           targetKind = null; break;
            }
            if (string.IsNullOrEmpty(target)) return "target を指定してください";
            string key = Key(role, targetKind, target);
            if (dto.edit.tracks.Any(t => t != null && Key(t.role, t.targetKind, t.target) == key))
                return $"同じ平らなトラック（{role}:{target}）に当たるトラックがあります";
            var et = new MotionEditTrackDTO
            {
                id = Ids.Of(dto.edit).Next("t"), role = role, targetKind = targetKind, target = target,
                name = string.IsNullOrEmpty(name) ? target : name,
                uname = role == MotionEditDTO.RoleBakedBones ? target : null,
            };
            dto.edit.tracks.Add(et);
            createdId = et.id;
            return null;
        }

        /// <summary>トラックを除く。平らなトラックも除き、クリップの id をバンドルの clipIds から除く。</summary>
        public static string RemoveTrack(MotionClipDTO dto, string trackId)
        {
            var t = FindTrack(dto, trackId);
            if (t == null) return $"トラック {trackId} がありません";
            if (IsDerivedRole(dto, t.role)) return DerivedError(t);
            RemoveTracksWhere(dto.edit, x => ReferenceEquals(x, t));
            RemoveFlat(dto, t);
            return null;
        }

        /// <summary>トラックのラップを変える（空は null。post の null はクリップのループで決まる）。</summary>
        public static string SetWrap(MotionClipDTO dto, string trackId, string pre, string post)
        {
            var t = FindTrack(dto, trackId);
            if (t == null) return $"トラック {trackId} がありません";
            if (IsDerivedRole(dto, t.role)) return DerivedError(t);
            pre = NullIfEmpty(pre); post = NullIfEmpty(post);
            if (!MotionCurveMath.IsKnownWrapMode(pre))  return $"pre \"{pre}\" はラップモードではありません";
            if (!MotionCurveMath.IsKnownWrapMode(post)) return $"post \"{post}\" はラップモードではありません";
            t.preWrapMode = pre;
            t.postWrapMode = post;
            return null;
        }

        // ================================================================
        // クリップ
        // ================================================================

        /// <summary>空のクリップを足す（Studio の Track.addClip。start は 0 以上、len は 1 以上）。</summary>
        public static string AddClip(MotionClipDTO dto, string trackId, int start, int len, string name, out string createdId)
        {
            createdId = null;
            var t = FindTrack(dto, trackId);
            if (t == null) return $"トラック {trackId} がありません";
            if (IsDerivedRole(dto, t.role)) return DerivedError(t);
            var c = new MotionEditClipDTO
            {
                id = Ids.Of(dto.edit).Next("c"), name = string.IsNullOrEmpty(name) ? DefaultClipName : name,
                start = Math.Max(0, start), len = Math.Max(1, len), loop = false, weight = 1f,
            };
            t.clips.Add(c);
            createdId = c.id;
            return null;
        }

        /// <summary>クリップを除く（バンドルの clipIds からも除く）。</summary>
        public static string RemoveClip(MotionClipDTO dto, string clipId)
        {
            string e = ClipOf(dto, clipId, out var t, out var c);
            if (e != null) return e;
            t.clips.Remove(c);
            RemoveClipIds(dto.edit, new[] { c.id });
            return null;
        }

        /// <summary>クリップの開始・長さ・ループ・重みを変える（Studio のクリップのインスペクタ。重みは 0～1 に丸める）。</summary>
        public static string SetClip(MotionClipDTO dto, string clipId, int start, int len, bool loop, float weight)
        {
            string e = ClipOf(dto, clipId, out _, out var c);
            if (e != null) return e;
            if (!Finite(weight)) return $"weight が数ではありません（{weight}）";
            c.start = Math.Max(0, start);
            c.len = Math.Max(1, len);
            c.loop = loop;
            c.weight = Mathf.Clamp01(weight);
            return null;
        }

        // ================================================================
        // キー
        // ================================================================

        private static int LocalFrame(MotionEditClipDTO c, float f)
            => (int)MotionEditFlattener.RoundJs(Mathf.Clamp(f, 0f, c.len - 1f));

        private static bool HasKeyAt(MotionEditClipDTO c, float f)
            => (c.rot?.Any(k => k.f == f) ?? false) || (c.pos?.Any(k => k.f == f) ?? false)
            || (c.scl?.Any(k => k.f == f) ?? false) || (c.w?.Any(k => k.f == f) ?? false);

        /// <summary>
        /// ローカルフレーム f にキーを打つ（Studio のクリップのインスペクタ「キー追加」＝ captureKey(lf, track.sample(start + lf))）。
        /// 全チャンネルに、今の評価値を入れる。既にあるキーは値だけ変える（接線・t0 は残す）。f は 0 以上 len 未満。
        /// </summary>
        public static string CaptureKey(MotionClipDTO dto, string clipId, int f)
        {
            string e = ClipOf(dto, clipId, out var t, out var c);
            if (e != null) return e;
            if (f < 0 || f >= c.len) return $"フレーム {f} がクリップの範囲（0～{c.len - 1}）の外です";
            int lf = LocalFrame(c, f);
            var pose = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (var ch in ChannelsOf(t))
                pose[ch] = MotionEditFlattener.SampleTrackChannel(dto.edit, dto.frameRate, t.id, ch, c.start + f);
            foreach (var ch in ChannelsOf(t))
            {
                var v = pose[ch];
                if (ch == ChW)
                {
                    if (c.w == null) c.w = new List<MotionEditScalarKeyDTO>();
                    var ex = c.w.FirstOrDefault(k => k.f == lf);
                    if (ex != null) ex.v = v[0]; else c.w.Add(new MotionEditScalarKeyDTO { f = lf, v = v[0] });
                }
                else
                {
                    var list = VecList(c, ch, create: true);
                    var ex = list.FirstOrDefault(k => k.f == lf);
                    if (ex != null) ex.v = (float[])v.Clone(); else list.Add(new MotionEditKeyDTO { f = lf, v = (float[])v.Clone() });
                }
            }
            SortKeys(c);
            return null;
        }

        /// <summary>チャンネル ch のローカルフレーム f のキーの値を変える（接線・t0 は残す）。キーが無ければエラー。</summary>
        public static string SetKeyValue(MotionClipDTO dto, string clipId, string ch, int f, float[] v)
        {
            string e = ClipOf(dto, clipId, out var t, out var c);
            if (e != null) return e;
            if (!ChannelsOf(t).Contains(ch)) return $"チャンネル {ch} は {t.role} のトラックにありません";
            int n = ch == ChRot ? 4 : ch == ChW ? 1 : 3;
            if (v == null || v.Length != n || v.Any(x => !Finite(x))) return $"値は数を {n} 個（{ch}）";
            if (ch == ChRot && v[0] * v[0] + v[1] * v[1] + v[2] * v[2] + v[3] * v[3] < 1e-8f) return "回転の長さがほぼ 0 です";
            if (ch == ChW)
            {
                var k = c.w?.FirstOrDefault(x => x.f == f);
                if (k == null) return $"{ch} のフレーム {f} にキーがありません";
                k.v = v[0];
            }
            else
            {
                var k = VecList(c, ch, false)?.FirstOrDefault(x => x.f == f);
                if (k == null) return $"{ch} のフレーム {f} にキーがありません";
                k.v = (float[])v.Clone();
            }
            return null;
        }

        /// <summary>ローカルフレーム f のキーを全チャンネルで除く（Studio の removeKeyAt）。</summary>
        public static string RemoveKey(MotionClipDTO dto, string clipId, int f)
        {
            string e = ClipOf(dto, clipId, out _, out var c);
            if (e != null) return e;
            if (!HasKeyAt(c, f)) return $"フレーム {f} にキーがありません";
            c.rot?.RemoveAll(k => k.f == f);
            c.pos?.RemoveAll(k => k.f == f);
            c.scl?.RemoveAll(k => k.f == f);
            c.w?.RemoveAll(k => k.f == f);
            return null;
        }

        /// <summary>ローカルフレーム f のキーを newF へ動かす（Studio の moveKey。移り先は round(clamp(newF, 0, len − 1))、そこにキーがあればエラー）。</summary>
        public static string MoveKey(MotionClipDTO dto, string clipId, int f, int newF)
        {
            string e = ClipOf(dto, clipId, out _, out var c);
            if (e != null) return e;
            if (!HasKeyAt(c, f)) return $"フレーム {f} にキーがありません";
            int nf = LocalFrame(c, newF);
            if (nf != f && HasKeyAt(c, nf)) return $"フレーム {nf} には既にキーがあります";
            if (c.rot != null) foreach (var k in c.rot) if (k.f == f) k.f = nf;
            if (c.pos != null) foreach (var k in c.pos) if (k.f == f) k.f = nf;
            if (c.scl != null) foreach (var k in c.scl) if (k.f == f) k.f = nf;
            if (c.w != null)   foreach (var k in c.w)   if (k.f == f) k.f = nf;
            SortKeys(c);
            return null;
        }

        /// <summary>
        /// チャンネル ch のローカルフレーム f にキーを作る（Studio の Clip.setChannelKey）。キーがあれば値だけ変え（接線・t0 は残す）、
        /// 無ければ {f, v} を足す（接線・t0 なし）。f は 0 以上 len 未満。ほかのチャンネルは変えない。
        /// </summary>
        public static string SetChannelKey(MotionClipDTO dto, string clipId, string ch, int f, float[] v)
        {
            string e = ClipOf(dto, clipId, out var t, out var c);
            if (e != null) return e;
            if (!ChannelsOf(t).Contains(ch)) return $"チャンネル {ch} は {t.role} のトラックにありません";
            if (f < 0 || f >= c.len) return $"フレーム {f} がクリップの範囲（0～{c.len - 1}）の外です";
            int n = ch == ChRot ? 4 : ch == ChW ? 1 : 3;
            if (v == null || v.Length != n || v.Any(x => !Finite(x))) return $"値は数を {n} 個（{ch}）";
            if (ch == ChRot && v[0] * v[0] + v[1] * v[1] + v[2] * v[2] + v[3] * v[3] < 1e-8f) return "回転の長さがほぼ 0 です";
            int lf = LocalFrame(c, f);
            if (ch == ChW)
            {
                if (c.w == null) c.w = new List<MotionEditScalarKeyDTO>();
                var ex = c.w.FirstOrDefault(k => k.f == lf);
                if (ex != null) ex.v = v[0]; else c.w.Add(new MotionEditScalarKeyDTO { f = lf, v = v[0] });
            }
            else
            {
                var list = VecList(c, ch, create: true);
                var ex = list.FirstOrDefault(k => k.f == lf);
                if (ex != null) ex.v = (float[])v.Clone(); else list.Add(new MotionEditKeyDTO { f = lf, v = (float[])v.Clone() });
            }
            SortKeys(c);
            return null;
        }

        /// <summary>チャンネル ch のローカルフレーム f のキーだけを除く（Studio の Clip.removeChannelKey）。キーが無ければエラー。</summary>
        public static string RemoveChannelKey(MotionClipDTO dto, string clipId, string ch, int f)
        {
            string e = ClipOf(dto, clipId, out var t, out var c);
            if (e != null) return e;
            if (!ChannelsOf(t).Contains(ch)) return $"チャンネル {ch} は {t.role} のトラックにありません";
            int removed = ch == ChW ? (c.w?.RemoveAll(k => k.f == f) ?? 0) : (VecList(c, ch, false)?.RemoveAll(k => k.f == f) ?? 0);
            if (removed == 0) return $"{ch} のフレーム {f} にキーがありません";
            return null;
        }

        private static List<MotionEditKeyDTO> VecList(MotionEditClipDTO c, string ch, bool create)
        {
            switch (ch)
            {
                case ChRot: if (c.rot == null && create) c.rot = new List<MotionEditKeyDTO>(); return c.rot;
                case ChPos: if (c.pos == null && create) c.pos = new List<MotionEditKeyDTO>(); return c.pos;
                case ChScl: if (c.scl == null && create) c.scl = new List<MotionEditKeyDTO>(); return c.scl;
            }
            return null;
        }

        // ================================================================
        // 接線（Studio の chanData・tanSlot・keySlopes・setMode・setSlope・setWeightedMode・setWeight）
        // ================================================================

        private sealed class ChanData
        {
            public int n;
            public int dim;
            public float[] t;
            public float[][] v;                 // [キー][成分]（回転は符号をそろえた値）
            public MotionTangentDTO[][] tan;    // [キー][成分]（回転は符号をそろえた写し）
            public bool[] flip;
            public int index;                   // 対象のキーの並びの位置
        }

        private static string TangentTarget(MotionClipDTO dto, string clipId, string ch, int f,
                                            out MotionEditTrackDTO t, out MotionEditClipDTO c, out object key, out ChanData d)
        {
            key = null; d = null; t = null; c = null;
            string e = ClipOf(dto, clipId, out t, out c);
            if (e != null) return e;
            if (!ChannelsOf(t).Contains(ch)) return $"チャンネル {ch} は {t.role} のトラックにありません";
            double fps = dto.frameRate > 0f ? dto.frameRate : 30.0;
            var clip = c;
            if (ch == ChW)
            {
                var keys = c.w ?? new List<MotionEditScalarKeyDTO>();
                key = keys.FirstOrDefault(k => k.f == f);
                if (key == null) return $"{ch} のフレーム {f} にキーがありません";
                var rows = keys.Select((k, i) => (k, i, s: MotionEditFlattener.KeySeconds(clip.start, k.f, k.t0, fps)))
                               .OrderBy(x => x.s).ThenBy(x => x.i).ToList();
                d = new ChanData
                {
                    n = rows.Count, dim = 1,
                    t = rows.Select(x => (float)x.s).ToArray(),
                    v = rows.Select(x => new[] { x.k.v }).ToArray(),
                    tan = rows.Select(x => new[] { x.k.tan }).ToArray(),
                    flip = new bool[rows.Count],
                };
                var kk = key;
                d.index = rows.FindIndex(x => ReferenceEquals(x.k, kk));
                return null;
            }
            var vkeys = VecList(c, ch, false) ?? new List<MotionEditKeyDTO>();
            key = vkeys.FirstOrDefault(k => k.f == f);
            if (key == null) return $"{ch} のフレーム {f} にキーがありません";
            int dim = ch == ChRot ? 4 : 3;
            var vrows = vkeys.Select((k, i) => (k, i, s: MotionEditFlattener.KeySeconds(clip.start, k.f, k.t0, fps)))
                             .OrderBy(x => x.s).ThenBy(x => x.i).ToList();
            d = new ChanData { n = vrows.Count, dim = dim, t = vrows.Select(x => (float)x.s).ToArray(), flip = new bool[vrows.Count] };
            d.v = new float[d.n][];
            d.tan = new MotionTangentDTO[d.n][];
            float[] prev = null;
            for (int j = 0; j < d.n; j++)
            {
                var k = vrows[j].k;
                var v = new float[dim];
                for (int i = 0; i < dim; i++) v[i] = k.v != null && k.v.Length > i ? k.v[i] : 0f;
                var tn = new MotionTangentDTO[dim];
                for (int i = 0; i < dim; i++) tn[i] = k.tan != null && k.tan.Length > i ? CloneTan(k.tan[i]) : null;
                if (dim == 4 && prev != null && prev[0] * v[0] + prev[1] * v[1] + prev[2] * v[2] + prev[3] * v[3] < 0f)
                {
                    d.flip[j] = true;
                    for (int i = 0; i < 4; i++) v[i] = -v[i];
                    foreach (var x in tn)
                    {
                        if (x == null) continue;
                        if (x.inTangent.HasValue)  x.inTangent  = -x.inTangent.Value;
                        if (x.outTangent.HasValue) x.outTangent = -x.outTangent.Value;
                    }
                }
                d.v[j] = v; d.tan[j] = tn;
                if (dim == 4) prev = v;
            }
            var key2 = key;
            d.index = vrows.FindIndex(x => ReferenceEquals(x.k, key2));
            return null;
        }

        // keySlopes の in / out（表示の符号）
        private static float Slope(ChanData d, int comp, string side)
        {
            var v = new float[d.n];
            var tn = new MotionTangentDTO[d.n];
            for (int j = 0; j < d.n; j++) { v[j] = d.v[j][comp]; tn[j] = d.tan[j][comp]; }
            return side == SideIn ? MotionCurveMath.ResolveIn(d.n, d.t, v, tn, d.index)
                                  : MotionCurveMath.ResolveOut(d.n, d.t, v, tn, d.index);
        }

        // tanSlot：キーの成分 comp の接線。create なら無ければ両側 Linear で作る
        private static MotionTangentDTO TanSlot(object key, int dim, int comp, bool create)
        {
            if (key is MotionEditScalarKeyDTO sk)
            {
                if (sk.tan != null) return sk.tan;
                if (!create) return null;
                sk.tan = new MotionTangentDTO { leftTangentMode = MotionCurveMath.ModeLinear, rightTangentMode = MotionCurveMath.ModeLinear };
                return sk.tan;
            }
            var vk = (MotionEditKeyDTO)key;
            if (vk.tan == null) { if (!create) return null; vk.tan = new MotionTangentDTO[dim]; }
            if (vk.tan.Length > comp && vk.tan[comp] != null) return vk.tan[comp];
            if (!create) return null;
            if (vk.tan.Length < dim) { var a = new MotionTangentDTO[dim]; Array.Copy(vk.tan, a, vk.tan.Length); vk.tan = a; }
            return vk.tan[comp] = new MotionTangentDTO { leftTangentMode = MotionCurveMath.ModeLinear, rightTangentMode = MotionCurveMath.ModeLinear };
        }

        private static bool ModeIs(string s, string m) => string.Equals(s, m, StringComparison.OrdinalIgnoreCase);

        private static string NormSide(ref string side)
        {
            side = (side ?? "").ToLowerInvariant();
            return side == SideIn || side == SideOut ? null : $"side は {SideIn} か {SideOut}（\"{side}\"）";
        }

        /// <summary>
        /// 入側か出側の接線モードを全成分に当てる（Studio の setMode）。Free は今求まる傾きを書き、
        /// Auto・ClampedAuto・Linear は保存値を消す。tan の無い成分への Linear は何もしない。
        /// </summary>
        public static string SetTangentMode(MotionClipDTO dto, string clipId, string ch, int f, string side, string mode)
        {
            string e = NormSide(ref side);
            if (e != null) return e;
            string m = TanModes.FirstOrDefault(x => ModeIs(x, mode));
            if (m == null) return $"mode は {string.Join(" / ", TanModes)}（\"{mode}\"）";
            e = TangentTarget(dto, clipId, ch, f, out _, out _, out var key, out var d);
            if (e != null) return e;
            var slopes = new float[d.dim];
            for (int c = 0; c < d.dim; c++) slopes[c] = Slope(d, c, side);
            for (int c = 0; c < d.dim; c++)
            {
                if (m == MotionCurveMath.ModeLinear && TanSlot(key, d.dim, c, false) == null) continue;
                var o = TanSlot(key, d.dim, c, true);
                if (side == SideIn) o.leftTangentMode = m; else o.rightTangentMode = m;
                if (m == MotionCurveMath.ModeFree)
                {
                    float s = d.flip[d.index] ? -slopes[c] : slopes[c];
                    if (side == SideIn) o.inTangent = s; else o.outTangent = s;
                }
                else if (m != MotionCurveMath.ModeConstant)
                {
                    if (side == SideIn) o.inTangent = null; else o.outTangent = null;
                }
            }
            return null;
        }

        /// <summary>成分 comp の入側か出側の傾き（表示の符号、値/秒）を書く（Studio の setSlope）。その側が Auto・ClampedAuto・Linear なら Free にする。</summary>
        public static string SetTangentSlope(MotionClipDTO dto, string clipId, string ch, int f, int comp, string side, float slope)
        {
            string e = NormSide(ref side);
            if (e != null) return e;
            if (!Finite(slope)) return $"slope が数ではありません（{slope}）";
            e = TangentTarget(dto, clipId, ch, f, out _, out _, out var key, out var d);
            if (e != null) return e;
            if (comp < 0 || comp >= d.dim) return $"comp は 0～{d.dim - 1}（{comp}）";
            var o = TanSlot(key, d.dim, comp, true);
            float s = d.flip[d.index] ? -slope : slope;
            if (side == SideIn)
            {
                o.inTangent = s;
                if (IsAutoLike(o.leftTangentMode)) o.leftTangentMode = MotionCurveMath.ModeFree;
            }
            else
            {
                o.outTangent = s;
                if (IsAutoLike(o.rightTangentMode)) o.rightTangentMode = MotionCurveMath.ModeFree;
            }
            return null;
        }

        private static bool IsAutoLike(string mode)
            => ModeIs(mode, MotionCurveMath.ModeAuto) || ModeIs(mode, MotionCurveMath.ModeClampedAuto) || ModeIs(mode, MotionCurveMath.ModeLinear);

        /// <summary>成分 comp の weightedMode（None / In / Out / Both）を書く（Studio の setWeightedMode）。使い始める側に重みが無ければ 1/3 を書く。</summary>
        public static string SetWeightedMode(MotionClipDTO dto, string clipId, string ch, int f, int comp, string weightedMode)
        {
            string wm = new[] { MotionCurveMath.WeightNone, MotionCurveMath.WeightIn, MotionCurveMath.WeightOut, MotionCurveMath.WeightBoth }
                .FirstOrDefault(x => x == weightedMode);
            if (wm == null) return $"weightedMode は None / In / Out / Both（\"{weightedMode}\"）";
            string e = TangentTarget(dto, clipId, ch, f, out _, out _, out var key, out var d);
            if (e != null) return e;
            if (comp < 0 || comp >= d.dim) return $"comp は 0～{d.dim - 1}（{comp}）";
            var o = TanSlot(key, d.dim, comp, true);
            o.weightedMode = wm == MotionCurveMath.WeightNone ? null : wm;
            if ((wm == MotionCurveMath.WeightBoth || wm == MotionCurveMath.WeightIn) && !(o.inWeight.HasValue && Finite(o.inWeight.Value)))
                o.inWeight = 1f / 3f;
            if ((wm == MotionCurveMath.WeightBoth || wm == MotionCurveMath.WeightOut) && !(o.outWeight.HasValue && Finite(o.outWeight.Value)))
                o.outWeight = 1f / 3f;
            return null;
        }

        /// <summary>成分 comp の入側か出側の重み（0～1 に丸める）を書く（Studio の setWeight）。</summary>
        public static string SetTangentWeight(MotionClipDTO dto, string clipId, string ch, int f, int comp, string side, float weight)
        {
            string e = NormSide(ref side);
            if (e != null) return e;
            if (!Finite(weight)) return $"weight が数ではありません（{weight}）";
            e = TangentTarget(dto, clipId, ch, f, out _, out _, out var key, out var d);
            if (e != null) return e;
            if (comp < 0 || comp >= d.dim) return $"comp は 0～{d.dim - 1}（{comp}）";
            var o = TanSlot(key, d.dim, comp, true);
            if (side == SideIn) o.inWeight = Mathf.Clamp01(weight); else o.outWeight = Mathf.Clamp01(weight);
            return null;
        }

        // ================================================================
        // バンドル
        // ================================================================

        /// <summary>
        /// クリップとバンドルを束ねた新しいバンドルを作る（Studio の bMakeFrom。入れ子はバンドルを渡す）。
        /// クリップは元のバンドルの clipIds から、バンドルは元の親の childIds から除く。新しいバンドルは上位で、並びの最後。
        /// </summary>
        public static string MakeBundle(MotionClipDTO dto, string[] clipIds, string[] bundleIds, string name, out string createdId)
        {
            createdId = null;
            clipIds = (clipIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            bundleIds = (bundleIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            if (clipIds.Length == 0 && bundleIds.Length == 0) return "束ねるクリップかバンドルを指定してください";
            foreach (var id in clipIds) { string e = ClipOf(dto, id, out _, out _); if (e != null) return e; }
            foreach (var id in bundleIds) if (FindBundle(dto, id) == null) return $"バンドル {id} がありません";
            var e2 = dto.edit;
            var nb = new MotionEditBundleDTO { id = Ids.Of(e2).Next("b"), name = string.IsNullOrEmpty(name) ? DefaultBundleName : name };
            foreach (var id in clipIds)
            {
                var old = e2.bundles.FirstOrDefault(b => b?.clipIds != null && b.clipIds.Contains(id));
                if (old != null) old.clipIds.RemoveAll(x => x == id);
                nb.clipIds.Add(id);
            }
            foreach (var id in bundleIds)
            {
                var ch = FindBundle(dto, id);
                var op = ch.parentId != null ? FindBundle(dto, ch.parentId) : null;
                if (op != null) op.childIds.RemoveAll(x => x == id);
                nb.childIds.Add(id);
            }
            e2.bundles.Add(nb);
            foreach (var id in nb.childIds) { var ch = FindBundle(dto, id); if (ch != null) ch.parentId = nb.id; }
            createdId = nb.id;
            return null;
        }

        /// <summary>バンドルだけを解く（Studio の bUngroup。クリップと子は残し、子は親へ上げる）。</summary>
        public static string Ungroup(MotionClipDTO dto, string bundleId)
        {
            var b = FindBundle(dto, bundleId);
            if (b == null) return $"バンドル {bundleId} がありません";
            foreach (var id in b.childIds ?? new List<string>()) { var ch = FindBundle(dto, id); if (ch != null) ch.parentId = b.parentId; }
            var par = b.parentId != null ? FindBundle(dto, b.parentId) : null;
            if (par != null) par.childIds = par.childIds.Where(x => x != b.id).Concat(b.childIds ?? new List<string>()).ToList();
            dto.edit.bundles.Remove(b);
            return null;
        }

        /// <summary>
        /// バンドルを中身のクリップと子ごと複製する（Studio の bClone）。クリップの開始は dStart ずらす。
        /// 複製は元と同じ親の子にする。新しいバンドルの id を createdId に入れる。
        /// </summary>
        public static string CloneBundle(MotionClipDTO dto, string bundleId, int dStart, out string createdId)
        {
            createdId = null;
            var b = FindBundle(dto, bundleId);
            if (b == null) return $"バンドル {bundleId} がありません";
            var ids = Ids.Of(dto.edit);
            string err = null;
            MotionEditBundleDTO Clone(MotionEditBundleDTO src, bool topCall, string parentId)
            {
                string pid = topCall ? src.parentId : parentId;
                var nb = new MotionEditBundleDTO
                {
                    id = ids.Next("b"), name = src.name, parentId = pid,
                    layer = src.layer == null ? null : new MotionEditLayerDTO { mode = src.layer.mode, weight = src.layer.weight, mute = src.layer.mute, solo = src.layer.solo },
                };
                dto.edit.bundles.Add(nb);
                if (topCall && pid != null)
                {
                    var par = FindBundle(dto, pid);
                    if (par != null) par.childIds.Add(nb.id); else nb.parentId = null;
                }
                foreach (var cid in (src.clipIds ?? new List<string>()).ToList())
                {
                    if (!FindClip(dto, cid, out var t, out var c)) continue;
                    if (c.start + dStart < 0f && err == null) err = $"クリップ {c.id} の開始が 0 より前になります";
                    var nc = CloneClip(c);
                    nc.id = ids.Next("c");
                    nc.start = c.start + dStart;
                    t.clips.Add(nc);
                    nb.clipIds.Add(nc.id);
                }
                foreach (var chId in (src.childIds ?? new List<string>()).ToList())
                {
                    var ch = FindBundle(dto, chId);
                    if (ch == null) continue;
                    var nch = Clone(ch, false, nb.id);
                    nb.childIds.Add(nch.id);
                }
                return nb;
            }
            var made = Clone(b, true, null);
            if (err != null) return err;
            createdId = made.id;
            return null;
        }

        /// <summary>バンドルを中身のクリップと子ごと除く（Studio の bDeleteWithClips）。</summary>
        public static string DeleteBundle(MotionClipDTO dto, string bundleId)
        {
            var b = FindBundle(dto, bundleId);
            if (b == null) return $"バンドル {bundleId} がありません";
            void Del(MotionEditBundleDTO x)
            {
                foreach (var chId in (x.childIds ?? new List<string>()).ToList()) { var ch = FindBundle(dto, chId); if (ch != null) Del(ch); }
                foreach (var cid in (x.clipIds ?? new List<string>()).ToList())
                    if (FindClip(dto, cid, out var t, out var c)) t.clips.Remove(c);
                var par = x.parentId != null ? FindBundle(dto, x.parentId) : null;
                if (par != null) par.childIds.RemoveAll(id => id == x.id);
                dto.edit.bundles.Remove(x);
            }
            Del(b);
            return null;
        }

        // ================================================================
        // レイヤー
        // ================================================================

        /// <summary>
        /// バンドルのレイヤーを設定する（Studio のバンドルのインスペクタ）。enabled が false ならレイヤーでなくする。
        /// mode は override / additive（空は override）、weight は 0～1 に丸める。
        /// </summary>
        public static string SetLayer(MotionClipDTO dto, string bundleId, bool enabled, string mode, float weight, bool mute, bool solo)
        {
            var b = FindBundle(dto, bundleId);
            if (b == null) return $"バンドル {bundleId} がありません";
            if (!enabled) { b.layer = null; return null; }
            if (string.IsNullOrEmpty(mode)) mode = MotionEditDTO.LayerOverride;
            if (mode != MotionEditDTO.LayerOverride && mode != MotionEditDTO.LayerAdditive) return $"mode は override か additive（\"{mode}\"）";
            if (!Finite(weight)) return $"weight が数ではありません（{weight}）";
            b.layer = new MotionEditLayerDTO { mode = mode, weight = Mathf.Clamp01(weight), mute = mute, solo = solo };
            return null;
        }

        /// <summary>
        /// 重ねる順を入れ替える（Studio の layerSwap）。同じ入れ物（上位なら bundles、子なら親の childIds）の中で、
        /// dir（-1 / 1）方向の隣のレイヤーと入れ替える。
        /// </summary>
        public static string SwapLayer(MotionClipDTO dto, string bundleId, int dir)
        {
            if (dir != -1 && dir != 1) return $"dir は -1 か 1（{dir}）";
            var b = FindBundle(dto, bundleId);
            if (b == null) return $"バンドル {bundleId} がありません";
            if (b.layer == null) return $"バンドル {bundleId} はレイヤーではありません";
            var par = b.parentId != null ? FindBundle(dto, b.parentId) : null;
            var list = par != null
                ? par.childIds.Select(id => FindBundle(dto, id)).Where(x => x != null).ToList()
                : dto.edit.bundles.Where(x => x != null && x.parentId == null).ToList();
            int i0 = list.IndexOf(b);
            MotionEditBundleDTO o = null;
            if (i0 >= 0)
                for (int j = i0 + dir; j >= 0 && j < list.Count; j += dir)
                    if (list[j].layer != null) { o = list[j]; break; }
            if (o == null) return "入れ替える相手のレイヤーがありません";
            if (par != null)
            {
                int ia = par.childIds.IndexOf(b.id), ib = par.childIds.IndexOf(o.id);
                par.childIds[ia] = o.id; par.childIds[ib] = b.id;
            }
            else
            {
                var arr = dto.edit.bundles;
                int ia = arr.IndexOf(b), ib = arr.IndexOf(o);
                arr[ia] = o; arr[ib] = b;
            }
            return null;
        }

        // ================================================================
        // 表示（パネル・照会。dto は変えない）
        // ================================================================

        /// <summary>
        /// 表示に使う DTO。edit を持てばそのもの（held true）、無ければ写しに EnsureEdit・NormalizeEdit を当てたもの
        /// （最初の書き込みの Run と同じ順なので id も同じ）。dto が null なら null。
        /// </summary>
        public static MotionClipDTO EditView(MotionClipDTO dto, out bool held)
        {
            held = dto?.edit != null;
            if (dto == null || held) return dto;
            var v = MotionClipSerializer.Clone(dto);
            EnsureEdit(v);
            NormalizeEdit(v.edit);
            return v;
        }

        /// <summary>キーの成分 1 つの接線の表示（Studio の keySlopes）。</summary>
        public sealed class TangentView
        {
            /// <summary>求まる傾き（表示の符号、値/秒）。</summary>
            public float In, Out;
            /// <summary>保存した傾き（表示の符号）。無ければ null。</summary>
            public float? StoredIn, StoredOut;
            /// <summary>モード（tan が無ければ Linear、モードの無い側は Free）。</summary>
            public string InMode, OutMode;
            /// <summary>重み方式（None / In / Out / Both）。</summary>
            public string WeightedMode;
            /// <summary>重み（保存が無ければ 1/3）。</summary>
            public float InWeight, OutWeight;
            /// <summary>保存した重み。無ければ null。</summary>
            public float? StoredInWeight, StoredOutWeight;
        }

        /// <summary>
        /// キー（チャンネル ch・ローカルフレーム f）の成分 comp の接線の表示を返す。キーが無い・派生のトラックなら error に理由。
        /// dto は edit を持つもの（EditView の結果）を渡す。
        /// </summary>
        public static TangentView KeyTangentView(MotionClipDTO dto, string clipId, string ch, int f, int comp, out string error)
        {
            error = TangentTarget(dto, clipId, ch, f, out _, out _, out _, out var d);
            if (error != null) return null;
            if (comp < 0 || comp >= d.dim) { error = $"comp は 0～{d.dim - 1}（{comp}）"; return null; }
            // d.tan は符号をそろえた写し（反転したキーは傾きの符号を戻してある）なので、保存の傾きもそのまま表示の符号
            var o = d.tan[d.index][comp];
            string Norm(string m) => TanModes.FirstOrDefault(x => ModeIs(x, m)) ?? MotionCurveMath.ModeFree;
            float? W(float? x) => x.HasValue && Finite(x.Value) ? Mathf.Clamp01(x.Value) : (float?)null;
            string wm = o == null ? null
                : new[] { MotionCurveMath.WeightIn, MotionCurveMath.WeightOut, MotionCurveMath.WeightBoth }.FirstOrDefault(x => ModeIs(o.weightedMode, x));
            var r = new TangentView
            {
                In = Slope(d, comp, SideIn), Out = Slope(d, comp, SideOut),
                InMode  = o == null ? MotionCurveMath.ModeLinear : Norm(o.leftTangentMode),
                OutMode = o == null ? MotionCurveMath.ModeLinear : Norm(o.rightTangentMode),
                WeightedMode = wm ?? MotionCurveMath.WeightNone,
            };
            if (o != null)
            {
                r.StoredIn  = o.inTangent.HasValue  && Finite(o.inTangent.Value)  ? o.inTangent  : null;
                r.StoredOut = o.outTangent.HasValue && Finite(o.outTangent.Value) ? o.outTangent : null;
                r.StoredInWeight = W(o.inWeight); r.StoredOutWeight = W(o.outWeight);
            }
            r.InWeight  = r.StoredInWeight  ?? 1f / 3f;
            r.OutWeight = r.StoredOutWeight ?? 1f / 3f;
            return r;
        }

        // ================================================================
        // id
        // ================================================================

        private sealed class Ids
        {
            private long _next = 1;

            public static Ids Of(MotionEditDTO e)
            {
                long mx = 0;
                void Scan(string id)
                {
                    if (id == null) return;
                    var digits = new string(id.Where(char.IsDigit).ToArray());
                    if (digits.Length > 0 && digits.Length < 18 && long.TryParse(digits, out long n) && n > mx) mx = n;
                }
                if (e?.bundles != null) foreach (var b in e.bundles) Scan(b?.id);
                if (e?.tracks != null)
                    foreach (var t in e.tracks)
                    {
                        Scan(t?.id);
                        if (t?.clips != null) foreach (var c in t.clips) Scan(c?.id);
                    }
                return new Ids { _next = mx + 1 };
            }

            public string Next(string prefix) => prefix + (_next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ================================================================
        // 小物
        // ================================================================

        private static string Key(string role, string kind, string target) => role + "\n" + (kind ?? "") + "\n" + target;

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static bool Vec(float[] v, int n)
        {
            if (v == null || v.Length < n) return false;
            for (int i = 0; i < n; i++) if (!Finite(v[i])) return false;
            return true;
        }

        private static float[] Take(float[] v, int n)
        {
            var o = new float[n];
            Array.Copy(v, o, n);
            return o;
        }

        private static MotionTangentDTO CloneTan(MotionTangentDTO t)
            => t == null ? null : new MotionTangentDTO
            {
                inTangent = t.inTangent, outTangent = t.outTangent, inWeight = t.inWeight, outWeight = t.outWeight,
                weightedMode = t.weightedMode, leftTangentMode = t.leftTangentMode, rightTangentMode = t.rightTangentMode,
            };

        private static MotionTangentDTO[] CloneTans(MotionTangentDTO[] a) => a?.Select(CloneTan).ToArray();

        private static MotionEditClipDTO CloneClip(MotionEditClipDTO c)
        {
            List<MotionEditKeyDTO> V(List<MotionEditKeyDTO> l)
                => l?.Select(k => new MotionEditKeyDTO { f = k.f, v = (float[])k.v?.Clone(), tan = CloneTans(k.tan), t0 = k.t0 }).ToList();
            return new MotionEditClipDTO
            {
                id = c.id, name = c.name, start = c.start, len = c.len, loop = c.loop, weight = c.weight,
                rot = V(c.rot), pos = V(c.pos), scl = V(c.scl),
                w = c.w?.Select(k => new MotionEditScalarKeyDTO { f = k.f, v = k.v, tan = CloneTan(k.tan), t0 = k.t0 }).ToList(),
            };
        }
    }
}
