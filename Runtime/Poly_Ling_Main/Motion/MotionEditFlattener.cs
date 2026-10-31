// MotionEditFlattener.cs
// 編集の構造 edit（version 5）を評価して平らなトラックを作る（「平らにする」）。規則の正本。
// Runtime/Poly_Ling_Main/Motion/ に配置
//
// Motion Studio（データとツール/VRMビュワー/polyling_motion_studio/timeline.html）の
// trackWrap・keySec・evalRows・KIND・Clip.sampleChannel / sampleAt・Track.sampleContinuous / sampleClips / sampleCh・
// layerInfo・plNeedsBake・plMaxFrame・plBakeKeys・plKeyMap・plMakeTrack・exportPolyLing の書き分けは、この写し。
// キー列の評価は MotionCurveMath。回転の混ぜ方は Quaternion.Slerp（plmotion_curve.js の slerp はこれの写し）。
//
// ■ 規則（恒久メモ。時刻はフレーム f、秒は f / frameRate）
//   ラップ   … pre は preWrapMode（無ければ Clamp）、post は postWrapMode（無ければ loop のクリップがあれば Loop、無ければ Clamp）。
//   キー時刻 … フレーム round(start + f)。t0 があり round(t0·fps) がそのフレームなら t0 秒、無ければ フレーム / fps。
//              round は JS の Math.round（floor(x + 0.5)）。
//   クリップ … そのクリップのキーだけを秒で評価（ラップはトラックの値）。重み 1 未満なら重みを掛ける。
//   重み     … 回転 Slerp(I, v, w)、位置・スカラー v·w、拡大 lerp(1, v, w)。混ぜる mix は回転 Slerp、ほかは lerp。
//   1 列     … クリップを start 順（同じ start は元の順）に、キーを round(start + f) のフレームで 1 列にする。
//              同じフレームは後のクリップの値と接線、時刻は先に入ったもの。重み 1 未満のクリップのキーは重みを掛けた値・接線なし。
//   クリップの組 … start ≤ f < start + len で、loop か、f − start がそのチャンネルの最初と最後のキーの間にあるクリップを
//              start 順に集める。無ければ 1 列で評価。有れば隣どうしを w = clamp((f − cur.start) / ((min(prev.end, cur.end) − cur.start) || 1), 0, 1) で混ぜる。
//   レイヤー … layer を持つバンドルが無ければ全クリップの組。有れば、レイヤーに属さないクリップの組を基底に、重ねる順に重ねる。
//              実効のミュートか実効の重み ≤ 0 は飛ばす。そのレイヤーに直接属するクリップにこのチャンネルのキーが無ければ飛ばす。
//              override は mix(下, 値, w)。additive は回転 normalize(下 · Slerp(I, 値, w))、位置・スカラー 下 + w·値、拡大 成分ごと 下 × lerp(1, 値, w)。
//              所属：クリップが直接属するバンドルから親をたどって最初のレイヤー。重ねる順：親の無いバンドルを並び順に、childIds の順で深さ優先（親の後に子）。
//              実効の重み＝自分 × 祖先のレイヤー。実効のミュート＝自分のミュート、同じ親のレイヤーにソロがあり自分はソロでない、祖先のミュート。
//   標本化   … キーを持つクリップが 2 本以上、重み 1 未満のクリップ、キーを持つクリップがレイヤーに属する、のどれかなら、
//              整数フレーム 0～最終フレーム（全トラック・全クリップの start + max(0, len − 1) の最大）で評価してキーにする（接線なし、ラップ Clamp）。
//              時刻は フレーム / fps を小数 7 桁に丸める。キーのあるチャンネルだけ書く。
//              標本化しないトラックはキーをそのまま（フレーム max(0, round(start + f))、時刻は t0 か フレーム / fps を 7 桁）。
//   回転     … 正規化し、半球を隣とそろえる（反転したキーは回転接線の傾きも反転。MotionCurveMath.EnsureQuaternionContinuity）。
//   書き分け … bakedBones は scl を書かず、Hips 以外は pos も書かず、rot か pos のあるキーだけ。lookAt は rot だけで rot のあるキーだけ。
//              bakedBones でキーが残らないトラックは作らない（Studio は書かない）。
//   長さ     … 元の duration をフレームに直して最終フレームと同じならそのまま、違えば 最終フレーム / fps を 7 桁。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public static class MotionEditFlattener
    {
        /// <summary>bakedBones で pos を書く骨。</summary>
        public const string HipsId = "Hips";

        public const string ChRot = "rot";
        public const string ChPos = "pos";
        public const string ChScl = "scl";
        public const string ChW   = "w";

        private static readonly string[] BoneChannels   = { ChRot, ChPos, ChScl };
        private static readonly string[] ScalarChannels = { ChW };

        /// <summary>edit のトラック 1 本を平らにした結果。</summary>
        public sealed class FlatTrack
        {
            /// <summary>元の edit のトラック。</summary>
            public MotionEditTrackDTO Edit;
            /// <summary>ボーンの役割の平らなトラック。作らないときは null。</summary>
            public MotionTrackDTO Bone;
            /// <summary>スカラーの役割の平らなトラック（表情は MotionExpressionTrackDTO）。</summary>
            public MotionScalarTrackDTO Scalar;
            /// <summary>標本化したか。</summary>
            public bool Baked;
        }

        // ================================================================
        // 内部の形
        // ================================================================

        private enum Kind { Quat, Vec3, Scale3, Scalar }

        private static Kind KindOf(string ch)
            => ch == ChRot ? Kind.Quat : ch == ChPos ? Kind.Vec3 : ch == ChScl ? Kind.Scale3 : Kind.Scalar;

        private sealed class EKey
        {
            public float f;
            public float[] v;
            public MotionTangentDTO[] tanArr;   // ボーンのチャンネル
            public MotionTangentDTO tanObj;     // スカラー
            public float? t0;
        }

        private sealed class EClip
        {
            public string id;
            public double start, len;
            public bool loop;
            public double weight;
            public int order;
            public readonly Dictionary<string, List<EKey>> keys = new Dictionary<string, List<EKey>>(StringComparer.Ordinal);
            public double End => start + len;
            public List<EKey> Keys(string ch) => keys.TryGetValue(ch, out var l) ? l : EmptyKeys;
            public bool Contains(double f) => f >= start && f < End;
        }

        private static readonly List<EKey> EmptyKeys = new List<EKey>();

        private sealed class ETrack
        {
            public MotionEditTrackDTO src;
            public bool bone;
            public string[] channels;
            public List<EClip> clips;   // start 順（同じ start は元の順）
            public string pre, post;
        }

        private sealed class LayerInfo
        {
            public Dictionary<string, MotionEditBundleDTO> clipLayer;
            public List<MotionEditBundleDTO> order;
            public Dictionary<string, (double w, bool mute)> eff;
        }

        private struct Row
        {
            public double t;
            public float[] v;
            public MotionTangentDTO[] tanArr;
            public MotionTangentDTO tanObj;
        }

        private sealed class Evaluator
        {
            public Kind kind;
            public bool empty;
            public MotionTrackDTO bone;
            public MotionScalarTrackDTO scalar;
        }

        private sealed class Ctx
        {
            public double fps;
            public LayerInfo info;
            public readonly Dictionary<string, Evaluator> cache = new Dictionary<string, Evaluator>(StringComparer.Ordinal);
        }

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// edit を平らにする。edit のトラックの順に結果を返す（role の読めないトラックは飛ばす）。
        /// maxFrame は全トラック・全クリップの start + max(0, len − 1) の最大。
        /// </summary>
        public static List<FlatTrack> Flatten(MotionEditDTO edit, float frameRate, out double maxFrame)
        {
            var result = new List<FlatTrack>();
            maxFrame = 0;
            if (edit?.tracks == null) return result;

            var ctx = new Ctx { fps = IsFinite(frameRate) && frameRate > 0f ? frameRate : 30.0 };
            var tracks = new List<ETrack>();
            foreach (var tr in edit.tracks)
            {
                if (tr == null) continue;
                bool bone = MotionEditDTO.IsBoneRole(tr.role);
                if (!bone && !MotionEditDTO.IsScalarRole(tr.role)) continue;
                tracks.Add(BuildTrack(tr, bone));
            }
            foreach (var t in tracks)
                foreach (var c in t.clips)
                    maxFrame = Math.Max(maxFrame, c.start + Math.Max(0.0, c.len - 1.0));

            ctx.info = BuildLayerInfo(edit.bundles);
            foreach (var t in tracks) result.Add(MakeTrack(ctx, t, maxFrame));
            return result;
        }

        /// <summary>
        /// dto.edit を平らにし、(role, targetKind, target) で当たる平らなトラックのキー・ラップ（表情は category・provider も）を置き換える。
        /// 平らなトラックの並びは変えない。duration は長さの規則で決め直す。置き換えたトラックの数を返す。edit が無ければ 0。
        /// </summary>
        public static int Apply(MotionClipDTO dto)
        {
            if (dto?.edit == null) return 0;
            var flat = Flatten(dto.edit, dto.frameRate, out double maxF);
            int n = 0;
            foreach (var r in flat)
            {
                var tr = r.Edit;
                switch (tr.role)
                {
                    case MotionEditDTO.RoleBones:
                        if (r.Bone == null || dto.bones == null) break;
                        foreach (var t in dto.bones)
                            if (t != null && t.id == tr.target && t.targetKind == tr.targetKind) { CopyBone(r.Bone, t); n++; break; }
                        break;
                    case MotionEditDTO.RoleBakedBones:
                        if (r.Bone == null || dto.bakedBones == null) break;
                        foreach (var t in dto.bakedBones)
                            if (t != null && t.id == tr.target) { CopyBone(r.Bone, t); n++; break; }
                        break;
                    case MotionEditDTO.RoleBody:
                        if (r.Bone != null && dto.body != null) { CopyBone(r.Bone, dto.body); n++; }
                        break;
                    case MotionEditDTO.RoleLookAt:
                        if (r.Bone != null && dto.lookAt != null) { CopyBone(r.Bone, dto.lookAt); n++; }
                        break;
                    case MotionEditDTO.RoleExpressions:
                        if (r.Scalar == null || dto.expressions == null) break;
                        foreach (var t in dto.expressions)
                            if (t != null && t.name == tr.target && string.IsNullOrEmpty(t.path))
                            {
                                CopyScalar(r.Scalar, t);
                                t.category = tr.category;
                                t.provider = tr.provider;
                                n++; break;
                            }
                        break;
                    case MotionEditDTO.RoleMuscles:
                        if (r.Scalar == null || dto.muscles == null) break;
                        foreach (var t in dto.muscles)
                            if (t != null && t.name == tr.target) { CopyScalar(r.Scalar, t); n++; break; }
                        break;
                }
            }

            double fps = IsFinite(dto.frameRate) && dto.frameRate > 0f ? dto.frameRate : 30.0;
            double d0 = dto.duration;
            if (!(IsFinite((float)d0) && JsRound(d0 * fps) == maxF))
                dto.duration = (float)Round7(maxF / fps);
            return n;
        }

        /// <summary>
        /// 窓口の編集（MotionEditOps）が使う平らにし直し。dto.edit を平らにし、(role, targetKind, target) で当たる平らなトラックの
        /// キー・ラップ（表情は category・provider も）を置き換える。Apply と違うところ：
        ///   ・当たる平らなトラックが無ければ作る（bones は edit の targetKind、bakedBones は humanoid、表情は path なし）。
        ///   ・bakedBones でキーが残らないトラック（Studio は書かない）は、平らなトラックを除く。
        ///   ・長さの規則の「元の長さ」は loadedDuration（Studio の読込時の長さ meta.duration と同じく、読み込んだときの長さ）。
        /// 置き換え・作ったトラックの数を返す。edit が無ければ 0。
        /// </summary>
        public static int Reflatten(MotionClipDTO dto, double loadedDuration)
        {
            if (dto?.edit == null) return 0;
            var flat = Flatten(dto.edit, dto.frameRate, out double maxF);
            if (dto.bones == null) dto.bones = new List<MotionTrackDTO>();
            if (dto.bakedBones == null) dto.bakedBones = new List<MotionTrackDTO>();
            if (dto.expressions == null) dto.expressions = new List<MotionExpressionTrackDTO>();
            if (dto.muscles == null) dto.muscles = new List<MotionScalarTrackDTO>();
            int n = 0;
            foreach (var r in flat)
            {
                var tr = r.Edit;
                switch (tr.role)
                {
                    case MotionEditDTO.RoleBones:
                    {
                        if (r.Bone == null) break;
                        var t = dto.bones.FirstOrDefault(x => x != null && x.id == tr.target && x.targetKind == tr.targetKind);
                        if (t == null) { t = new MotionTrackDTO { id = tr.target, targetKind = tr.targetKind }; dto.bones.Add(t); }
                        CopyBone(r.Bone, t); n++;
                        break;
                    }
                    case MotionEditDTO.RoleBakedBones:
                    {
                        int i = dto.bakedBones.FindIndex(x => x != null && x.id == tr.target);
                        if (r.Bone == null) { if (i >= 0) dto.bakedBones.RemoveAt(i); break; }
                        MotionTrackDTO t;
                        if (i >= 0) t = dto.bakedBones[i];
                        else { t = new MotionTrackDTO { id = tr.target, targetKind = MotionClipDTO.TargetHumanoid }; dto.bakedBones.Add(t); }
                        CopyBone(r.Bone, t); n++;
                        break;
                    }
                    case MotionEditDTO.RoleBody:
                        if (r.Bone == null) break;
                        if (dto.body == null) dto.body = new MotionTrackDTO { id = MotionClipDTO.BodyTrackId, targetKind = MotionClipDTO.TargetBody };
                        CopyBone(r.Bone, dto.body); n++;
                        break;
                    case MotionEditDTO.RoleLookAt:
                        if (r.Bone == null) break;
                        if (dto.lookAt == null) dto.lookAt = new MotionTrackDTO { id = MotionClipDTO.LookAtTrackId, targetKind = MotionClipDTO.TargetLookAt };
                        CopyBone(r.Bone, dto.lookAt); n++;
                        break;
                    case MotionEditDTO.RoleExpressions:
                    {
                        if (r.Scalar == null) break;
                        var t = dto.expressions.FirstOrDefault(x => x != null && x.name == tr.target && string.IsNullOrEmpty(x.path));
                        if (t == null) { t = new MotionExpressionTrackDTO { name = tr.target }; dto.expressions.Add(t); }
                        CopyScalar(r.Scalar, t);
                        t.category = tr.category;
                        t.provider = tr.provider;
                        n++;
                        break;
                    }
                    case MotionEditDTO.RoleMuscles:
                    {
                        if (r.Scalar == null) break;
                        var t = dto.muscles.FirstOrDefault(x => x != null && x.name == tr.target);
                        if (t == null) { t = new MotionScalarTrackDTO { name = tr.target }; dto.muscles.Add(t); }
                        CopyScalar(r.Scalar, t); n++;
                        break;
                    }
                }
            }

            double fps = IsFinite(dto.frameRate) && dto.frameRate > 0f ? dto.frameRate : 30.0;
            if (!double.IsNaN(loadedDuration) && !double.IsInfinity(loadedDuration) && JsRound(loadedDuration * fps) == maxF)
                dto.duration = (float)loadedDuration;
            else
                dto.duration = (float)Round7(maxF / fps);
            return n;
        }

        /// <summary>
        /// edit のトラック trackId のチャンネル ch（rot / pos / scl / w）を、フレーム f で評価する（Studio の Track.sampleCh。
        /// クリップのクロスフェード・レイヤー合成を含む）。キーが無ければレスト。トラックが無いかチャンネルが合わなければ null。
        /// 回転は [x,y,z,w]、位置・拡大は [x,y,z]、スカラーは [v]。
        /// </summary>
        public static float[] SampleTrackChannel(MotionEditDTO edit, float frameRate, string trackId, string ch, double f)
        {
            var tr = edit?.tracks?.FirstOrDefault(x => x != null && x.id == trackId);
            if (tr == null) return null;
            bool bone = MotionEditDTO.IsBoneRole(tr.role);
            if (!bone && !MotionEditDTO.IsScalarRole(tr.role)) return null;
            if (!(bone ? BoneChannels : ScalarChannels).Contains(ch)) return null;
            var ctx = new Ctx { fps = IsFinite(frameRate) && frameRate > 0f ? frameRate : 30.0 };
            ctx.info = BuildLayerInfo(edit.bundles);
            return SampleCh(ctx, BuildTrack(tr, bone), ch, f);
        }

        /// <summary>キーの秒（Studio の keySec）。クリップの開始 start と、クリップの先頭からのフレーム f、元の秒 t0。</summary>
        public static double KeySeconds(float start, float f, float? t0, double fps)
        {
            double s = IsFinite(start) && start != 0f ? start : 0.0;
            double fr = JsRound(s + f);
            if (t0.HasValue && IsFinite(t0.Value) && JsRound(t0.Value * fps) == fr) return t0.Value;
            return fr / fps;
        }

        /// <summary>JS の Math.round（floor(x + 0.5)）。</summary>
        public static double RoundJs(double x) => JsRound(x);

        /// <summary>Studio の Q.norm（長さ 0 なら 1 で割る）。</summary>
        public static float[] NormalizeQuatJs(float[] q) => NormJs(q);

        private static void CopyBone(MotionTrackDTO src, MotionTrackDTO dst)
        {
            dst.keys = src.keys;
            dst.preWrapMode = src.preWrapMode;
            dst.postWrapMode = src.postWrapMode;
        }

        private static void CopyScalar(MotionScalarTrackDTO src, MotionScalarTrackDTO dst)
        {
            dst.keys = src.keys;
            dst.preWrapMode = src.preWrapMode;
            dst.postWrapMode = src.postWrapMode;
        }

        // ================================================================
        // 構造の組み立て
        // ================================================================

        private static ETrack BuildTrack(MotionEditTrackDTO tr, bool bone)
        {
            var t = new ETrack { src = tr, bone = bone, channels = bone ? BoneChannels : ScalarChannels };
            var clips = new List<EClip>();
            if (tr.clips != null)
            {
                int order = 0;
                foreach (var c in tr.clips)
                {
                    if (c == null) continue;
                    var ec = new EClip
                    {
                        id = c.id,
                        start = IsFinite(c.start) && c.start != 0f ? c.start : 0.0,                  // JS: o.start || 0
                        len   = IsFinite(c.len) && c.len != 0f ? c.len : 60.0,                       // JS: o.len || 60
                        loop  = c.loop,
                        weight = c.weight,
                        order = order++,
                    };
                    if (bone)
                    {
                        if (c.rot != null) ec.keys[ChRot] = VecKeys(c.rot);
                        if (c.pos != null) ec.keys[ChPos] = VecKeys(c.pos);
                        if (c.scl != null) ec.keys[ChScl] = VecKeys(c.scl);
                    }
                    else if (c.w != null)
                    {
                        var l = new List<EKey>();
                        foreach (var k in c.w)
                            if (k != null) l.Add(new EKey { f = k.f, v = new[] { k.v }, tanObj = k.tan, t0 = k.t0 });
                        ec.keys[ChW] = l;
                    }
                    clips.Add(ec);
                }
            }
            t.clips = clips.OrderBy(c => c.start).ThenBy(c => c.order).ToList();
            bool loop = t.clips.Any(c => c.loop);
            t.pre  = string.IsNullOrEmpty(tr.preWrapMode)  ? MotionCurveMath.WrapClamp : tr.preWrapMode;
            t.post = string.IsNullOrEmpty(tr.postWrapMode) ? (loop ? MotionCurveMath.WrapLoop : MotionCurveMath.WrapClamp) : tr.postWrapMode;
            return t;
        }

        private static List<EKey> VecKeys(List<MotionEditKeyDTO> src)
        {
            var l = new List<EKey>();
            foreach (var k in src)
                if (k != null) l.Add(new EKey { f = k.f, v = k.v, tanArr = k.tan, t0 = k.t0 });
            return l;
        }

        /// <summary>
        /// レイヤーの重ねる順（バンドル id）と、それぞれの実効の重み・実効のミュート（評価と同じ layerInfo）。
        /// layer を持つバンドルが無ければ空。照会（queryMotionEditBundles）が使う。
        /// </summary>
        public static List<(string id, double weight, bool mute)> LayerSummary(List<MotionEditBundleDTO> bundles)
        {
            var r = new List<(string, double, bool)>();
            var info = BuildLayerInfo(bundles);
            if (info == null) return r;
            foreach (var L in info.order) { var e = info.eff[L.id]; r.Add((L.id, e.w, e.mute)); }
            return r;
        }

        // timeline.html の layerInfo。layer を持つバンドルが無ければ null。
        private static LayerInfo BuildLayerInfo(List<MotionEditBundleDTO> bundles)
        {
            if (bundles == null || !bundles.Any(b => b != null && b.layer != null)) return null;
            var list = bundles.Where(b => b != null && b.id != null).ToList();
            var byId = new Dictionary<string, MotionEditBundleDTO>(StringComparer.Ordinal);
            foreach (var b in list) byId[b.id] = b;

            MotionEditBundleDTO Get(string id) => id != null && byId.TryGetValue(id, out var x) ? x : null;
            MotionEditBundleDTO Up(MotionEditBundleDTO b)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (b != null && !seen.Contains(b.id))
                {
                    if (b.layer != null) return b;
                    seen.Add(b.id);
                    b = Get(b.parentId);
                }
                return null;
            }

            var parent = new Dictionary<string, MotionEditBundleDTO>(StringComparer.Ordinal);
            var order = new List<MotionEditBundleDTO>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(MotionEditBundleDTO b)
            {
                if (b == null || !visited.Add(b.id)) return;
                if (b.layer != null) { order.Add(b); parent[b.id] = Up(Get(b.parentId)); }
                if (b.childIds != null) foreach (var id in b.childIds) Visit(Get(id));
            }
            foreach (var b in list)
                if (b.parentId == null || !byId.ContainsKey(b.parentId)) Visit(b);

            var eff = new Dictionary<string, (double w, bool mute)>(StringComparer.Ordinal);
            foreach (var L in order)
            {
                var p = parent[L.id];
                bool hasPe = p != null && eff.TryGetValue(p.id, out _);
                var pe = hasPe ? eff[p.id] : (1.0, false);
                bool sibSolo = order.Any(x => parent[x.id] == p && x.layer.solo);
                eff[L.id] = (LayerWeight(L.layer) * pe.Item1,
                             L.layer.mute || (sibSolo && !L.layer.solo) || (hasPe && pe.Item2));
            }

            var clipLayer = new Dictionary<string, MotionEditBundleDTO>(StringComparer.Ordinal);
            foreach (var b in list)
            {
                var L = Up(b);
                if (L != null && eff.ContainsKey(L.id) && b.clipIds != null)
                    foreach (var id in b.clipIds) if (id != null) clipLayer[id] = L;
            }
            return new LayerInfo { clipLayer = clipLayer, order = order, eff = eff };
        }

        // normLayer：数でなければ 1、0～1 に丸める。
        private static double LayerWeight(MotionEditLayerDTO l)
            => IsFinite(l.weight) ? Math.Max(0.0, Math.Min(1.0, l.weight)) : 1.0;

        private static bool IsAdditive(MotionEditLayerDTO l) => l.mode == MotionEditDTO.LayerAdditive;

        // ================================================================
        // 評価
        // ================================================================

        // keySec：キーの秒。
        private static double KeySec(EClip c, EKey k, double fps)
        {
            double f = JsRound(c.start + k.f);
            if (k.t0.HasValue && IsFinite(k.t0.Value) && JsRound(k.t0.Value * fps) == f) return k.t0.Value;
            return f / fps;
        }

        // Clip.sampleChannel（lf はクリップの先頭からのフレーム）
        private static float[] SampleChannel(Ctx ctx, ETrack t, EClip c, string ch, double lf)
        {
            var kind = KindOf(ch);
            var keys = c.Keys(ch);
            if (keys.Count == 0) return Rest(kind);
            string cacheKey = "c\n" + t.src.id + "\n" + c.id + "\n" + c.order + "\n" + ch;
            if (!ctx.cache.TryGetValue(cacheKey, out var ev))
            {
                var rows = new List<Row>(keys.Count);
                foreach (var k in keys) rows.Add(new Row { t = KeySec(c, k, ctx.fps), v = k.v, tanArr = k.tanArr, tanObj = k.tanObj });
                ev = BuildEvaluator(kind, rows, t.pre, t.post);
                ctx.cache[cacheKey] = ev;
            }
            return Eval(ev, (c.start + lf) / ctx.fps);
        }

        // Clip.sampleAt
        private static float[] SampleAt(Ctx ctx, ETrack t, EClip c, string ch, double f)
        {
            var v = SampleChannel(ctx, t, c, ch, f - c.start);
            return c.weight >= 1.0 ? v : Weight(KindOf(ch), v, c.weight);
        }

        // Track.sampleContinuous
        private static float[] SampleContinuous(Ctx ctx, ETrack t, string ch, double f, List<EClip> clips)
        {
            var kind = KindOf(ch);
            var sorted = clips.OrderBy(c => c.start).ThenBy(c => c.order).ToList();
            string cacheKey = "s\n" + t.src.id + "\n" + ch + "\n" + string.Join(",", sorted.Select(c => c.order));
            if (!ctx.cache.TryGetValue(cacheKey, out var ev))
            {
                var map = new Dictionary<double, Row>();
                foreach (var c in sorted)
                    foreach (var k in c.Keys(ch))
                    {
                        double fr = JsRound(c.start + k.f);
                        bool full = c.weight >= 1.0;
                        bool had = map.TryGetValue(fr, out var old);
                        map[fr] = new Row
                        {
                            t = had ? old.t : KeySec(c, k, ctx.fps),
                            v = full ? k.v : Weight(kind, k.v, c.weight),
                            tanArr = full ? k.tanArr : null,
                            tanObj = full ? k.tanObj : null,
                        };
                    }
                if (map.Count == 0) ev = new Evaluator { kind = kind, empty = true };
                else ev = BuildEvaluator(kind, map.OrderBy(e => e.Key).Select(e => e.Value).ToList(), t.pre, t.post);
                ctx.cache[cacheKey] = ev;
            }
            if (ev.empty) return Rest(kind);
            return Eval(ev, f / ctx.fps);
        }

        // Track.sampleClips
        private static float[] SampleClips(Ctx ctx, ETrack t, string ch, double f, List<EClip> clips)
        {
            var kind = KindOf(ch);
            var act = clips.Where(c => c.Contains(f)).OrderBy(c => c.start).ThenBy(c => c.order).Where(c =>
            {
                var ks = c.Keys(ch);
                if (ks.Count == 0) return false;
                if (c.loop) return true;
                double lf = f - c.start;
                return lf >= ks[0].f && lf <= ks[ks.Count - 1].f;
            }).ToList();
            if (act.Count == 0) return SampleContinuous(ctx, t, ch, f, clips);

            var acc = SampleAt(ctx, t, act[0], ch, f);
            for (int i = 1; i < act.Count; i++)
            {
                var prev = act[i - 1];
                var cur = act[i];
                double ov0 = cur.start, ov1 = Math.Min(prev.End, cur.End);
                double den = ov1 - ov0;
                if (den == 0.0 || double.IsNaN(den)) den = 1.0;
                double w = Math.Max(0.0, Math.Min(1.0, (f - ov0) / den));
                acc = Mix(kind, acc, SampleAt(ctx, t, cur, ch, f), w);
            }
            return acc;
        }

        // Track.sampleCh
        private static float[] SampleCh(Ctx ctx, ETrack t, string ch, double f)
        {
            var info = ctx.info;
            if (info == null) return SampleClips(ctx, t, ch, f, t.clips);

            var kind = KindOf(ch);
            var baseClips = t.clips.Where(c => c.id == null || !info.clipLayer.ContainsKey(c.id)).ToList();
            var acc = SampleClips(ctx, t, ch, f, baseClips);
            foreach (var L in info.order)
            {
                var e = info.eff[L.id];
                if (e.mute || !(e.w > 0.0)) continue;
                var lc = t.clips.Where(c => c.id != null && info.clipLayer.TryGetValue(c.id, out var x) && x == L).ToList();
                if (!lc.Any(c => c.Keys(ch).Count > 0)) continue;
                var v = SampleClips(ctx, t, ch, f, lc);
                double w = e.w;
                if (IsAdditive(L.layer))
                {
                    switch (kind)
                    {
                        case Kind.Quat:
                            acc = NormJs(MulQ(acc, Weight(kind, v, w)));
                            break;
                        case Kind.Scalar:
                            acc = new[] { (float)(acc[0] + w * v[0]) };
                            break;
                        case Kind.Scale3:
                            acc = new[] { (float)(acc[0] * Lerp(1.0, v[0], w)), (float)(acc[1] * Lerp(1.0, v[1], w)), (float)(acc[2] * Lerp(1.0, v[2], w)) };
                            break;
                        default:
                            acc = new[] { (float)(acc[0] + w * v[0]), (float)(acc[1] + w * v[1]), (float)(acc[2] + w * v[2]) };
                            break;
                    }
                }
                else acc = Mix(kind, acc, v, w);
            }
            return acc;
        }

        // ================================================================
        // 平らなトラックを作る（plNeedsBake・plBakeKeys・plKeyMap・plMakeTrack・exportPolyLing の書き分け）
        // ================================================================

        private static bool NeedsBake(Ctx ctx, ETrack t)
        {
            var cs = t.clips.Where(c => t.channels.Any(ch => c.Keys(ch).Count > 0)).ToList();
            if (cs.Count >= 2) return true;
            if (cs.Any(c => c.weight < 1.0)) return true;
            return ctx.info != null && cs.Any(c => c.id != null && ctx.info.clipLayer.ContainsKey(c.id));
        }

        private static FlatTrack MakeTrack(Ctx ctx, ETrack t, double maxF)
        {
            var tr = t.src;
            bool bake = NeedsBake(ctx, t);
            string pre  = bake ? MotionCurveMath.WrapClamp : t.pre;
            string post = bake ? MotionCurveMath.WrapClamp : t.post;
            var res = new FlatTrack { Edit = tr, Baked = bake };

            if (!t.bone)
            {
                var keys = bake ? BakeScalar(ctx, t, maxF) : KeyMapScalar(ctx, t);
                MotionScalarTrackDTO st = tr.role == MotionEditDTO.RoleExpressions
                    ? new MotionExpressionTrackDTO { category = tr.category, provider = tr.provider }
                    : new MotionScalarTrackDTO();
                st.name = tr.target;
                st.preWrapMode = pre;
                st.postWrapMode = post;
                st.keys = keys;
                res.Scalar = st;
                return res;
            }

            var bt = new MotionTrackDTO
            {
                id = tr.target,
                targetKind = tr.role == MotionEditDTO.RoleBakedBones ? MotionClipDTO.TargetHumanoid
                           : tr.role == MotionEditDTO.RoleBody ? MotionClipDTO.TargetBody
                           : tr.role == MotionEditDTO.RoleLookAt ? MotionClipDTO.TargetLookAt
                           : tr.targetKind,
                preWrapMode = pre,
                postWrapMode = post,
                keys = bake ? BakeBone(ctx, t, maxF) : KeyMapBone(ctx, t),
            };
            MotionCurveMath.EnsureQuaternionContinuity(bt);

            if (tr.role == MotionEditDTO.RoleBakedBones)
            {
                bool hips = tr.target == HipsId;
                bt.keys = bt.keys.Where(k =>
                {
                    k.scl = null; k.sclTan = null;
                    if (!hips) { k.pos = null; k.posTan = null; }
                    return k.rot != null || k.pos != null;
                }).ToList();
                if (bt.keys.Count == 0) return res;
            }
            else if (tr.role == MotionEditDTO.RoleLookAt)
            {
                bt.keys = bt.keys.Where(k =>
                {
                    k.pos = null; k.posTan = null; k.scl = null; k.sclTan = null;
                    return k.rot != null;
                }).ToList();
            }
            res.Bone = bt;
            return res;
        }

        private static HashSet<string> ChannelsWithKeys(ETrack t)
            => new HashSet<string>(t.channels.Where(ch => t.clips.Any(c => c.Keys(ch).Count > 0)), StringComparer.Ordinal);

        private static List<MotionKeyDTO> BakeBone(Ctx ctx, ETrack t, double maxF)
        {
            var has = ChannelsWithKeys(t);
            var keys = new List<MotionKeyDTO>();
            for (int f = 0; f <= maxF; f++)
            {
                var k = new MotionKeyDTO { t = (float)Round7(f / ctx.fps) };
                if (has.Contains(ChPos)) { var v = SampleCh(ctx, t, ChPos, f); if (AllFinite(v, 3)) k.pos = Take(v, 3); }
                if (has.Contains(ChRot)) { var v = SampleCh(ctx, t, ChRot, f); if (AllFinite(v, 4)) k.rot = NormJs(v); }
                if (has.Contains(ChScl)) { var v = SampleCh(ctx, t, ChScl, f); if (AllFinite(v, 3)) k.scl = Take(v, 3); }
                keys.Add(k);
            }
            return keys;
        }

        private static List<MotionScalarKeyDTO> BakeScalar(Ctx ctx, ETrack t, double maxF)
        {
            bool has = ChannelsWithKeys(t).Contains(ChW);
            var keys = new List<MotionScalarKeyDTO>();
            for (int f = 0; f <= maxF; f++)
            {
                var k = new MotionScalarKeyDTO { t = (float)Round7(f / ctx.fps) };
                if (has) { var v = SampleCh(ctx, t, ChW, f); if (IsFinite(v[0])) k.v = v[0]; }
                keys.Add(k);
            }
            return keys;
        }

        private sealed class MapRow
        {
            public double f;
            public double? t0;
            public float[] pos, rot, scl;
            public MotionTangentDTO[] posTan, rotTan, sclTan;
            public float? v;
            public MotionTangentDTO tan;
        }

        // plKeyMap
        private static List<MapRow> KeyMap(Ctx ctx, ETrack t)
        {
            var map = new Dictionary<double, MapRow>();
            foreach (var c in t.clips.OrderBy(c => c.start).ThenBy(c => c.order))
                foreach (var ch in t.channels)
                    foreach (var k in c.Keys(ch))
                    {
                        double f = Math.Max(0.0, JsRound(c.start + k.f));
                        if (!map.TryGetValue(f, out var o)) { o = new MapRow { f = f }; map[f] = o; }
                        if (k.t0.HasValue && IsFinite(k.t0.Value) && JsRound(k.t0.Value * ctx.fps) == f && !o.t0.HasValue) o.t0 = k.t0.Value;
                        if (ch == ChRot) { if (AllFinite(k.v, 4)) { o.rot = NormJs(Take(k.v, 4)); if (k.tanArr != null) o.rotTan = CloneTans(k.tanArr); } }
                        else if (ch == ChPos) { if (AllFinite(k.v, 3)) { o.pos = Take(k.v, 3); if (k.tanArr != null) o.posTan = CloneTans(k.tanArr); } }
                        else if (ch == ChScl) { if (AllFinite(k.v, 3)) { o.scl = Take(k.v, 3); if (k.tanArr != null) o.sclTan = CloneTans(k.tanArr); } }
                        else if (ch == ChW && k.v != null && k.v.Length > 0 && IsFinite(k.v[0])) { o.v = k.v[0]; if (k.tanObj != null) o.tan = CloneTan(k.tanObj); }
                    }
            return map.Values.OrderBy(r => r.f).ToList();
        }

        private static float KeyTime(MapRow r, double fps) => r.t0.HasValue ? (float)r.t0.Value : (float)Round7(r.f / fps);

        private static List<MotionKeyDTO> KeyMapBone(Ctx ctx, ETrack t)
            => KeyMap(ctx, t).Select(r => new MotionKeyDTO
            {
                t = KeyTime(r, ctx.fps),
                pos = r.pos, rot = r.rot, scl = r.scl,
                posTan = r.posTan, rotTan = r.rotTan, sclTan = r.sclTan,
            }).ToList();

        private static List<MotionScalarKeyDTO> KeyMapScalar(Ctx ctx, ETrack t)
            => KeyMap(ctx, t).Where(r => r.v.HasValue).Select(r => new MotionScalarKeyDTO
            {
                t = KeyTime(r, ctx.fps),
                v = r.v.Value,
                tan = r.tan,
            }).ToList();

        // ================================================================
        // キー列の評価（MotionCurveMath）
        // ================================================================

        private static Evaluator BuildEvaluator(Kind kind, List<Row> rows, string pre, string post)
        {
            var sorted = rows.Select((r, i) => (r, i)).OrderBy(x => x.r.t).ThenBy(x => x.i).Select(x => x.r).ToList();
            var ev = new Evaluator { kind = kind };
            if (kind == Kind.Scalar)
            {
                var st = new MotionScalarTrackDTO { preWrapMode = pre, postWrapMode = post };
                foreach (var r in sorted) st.keys.Add(new MotionScalarKeyDTO { t = (float)r.t, v = r.v[0], tan = r.tanObj });
                ev.scalar = st;
                return ev;
            }
            var bt = new MotionTrackDTO { preWrapMode = pre, postWrapMode = post };
            foreach (var r in sorted)
            {
                var k = new MotionKeyDTO { t = (float)r.t };
                if (kind == Kind.Quat) { k.rot = (float[])r.v.Clone(); k.rotTan = CloneTans(r.tanArr); }
                else { k.pos = (float[])r.v.Clone(); k.posTan = r.tanArr; }
                bt.keys.Add(k);
            }
            if (kind == Kind.Quat) MotionCurveMath.EnsureQuaternionContinuity(bt);
            ev.bone = bt;
            return ev;
        }

        private static float[] Eval(Evaluator ev, double time)
        {
            if (ev.empty) return Rest(ev.kind);
            float tm = (float)time;
            switch (ev.kind)
            {
                case Kind.Quat:
                    MotionCurveMath.TryEvaluateRotation(ev.bone, tm, out var q);
                    return new[] { q.x, q.y, q.z, q.w };
                case Kind.Scalar:
                    return new[] { MotionCurveMath.EvaluateScalar(ev.scalar, tm) };
                default:
                    MotionCurveMath.TryEvaluateVector3(ev.bone, MotionCurveMath.VectorChannel.Position, tm, out var v);
                    return new[] { v.x, v.y, v.z };
            }
        }

        // ================================================================
        // KIND
        // ================================================================

        private static float[] Rest(Kind k)
        {
            switch (k)
            {
                case Kind.Quat:   return new[] { 0f, 0f, 0f, 1f };
                case Kind.Scale3: return new[] { 1f, 1f, 1f };
                case Kind.Scalar: return new[] { 0f };
                default:          return new[] { 0f, 0f, 0f };
            }
        }

        private static float[] Weight(Kind k, float[] v, double w)
        {
            switch (k)
            {
                case Kind.Quat:   return Slerp(new[] { 0f, 0f, 0f, 1f }, v, w);
                case Kind.Scale3: return new[] { (float)Lerp(1.0, v[0], w), (float)Lerp(1.0, v[1], w), (float)Lerp(1.0, v[2], w) };
                case Kind.Scalar: return new[] { (float)(v[0] * w) };
                default:          return new[] { (float)(v[0] * w), (float)(v[1] * w), (float)(v[2] * w) };
            }
        }

        private static float[] Mix(Kind k, float[] a, float[] b, double t)
        {
            if (k == Kind.Quat) return Slerp(a, b, t);
            var o = new float[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = (float)Lerp(a[i], b[i], t);
            return o;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Quaternion.Slerp（入力は正規化し、近い向き側を回る）
        private static float[] Slerp(float[] a, float[] b, double t)
        {
            var qa = NormalizeOr(new Quaternion(a[0], a[1], a[2], a[3]));
            var qb = NormalizeOr(new Quaternion(b[0], b[1], b[2], b[3]));
            if (qa.x * qb.x + qa.y * qb.y + qa.z * qb.z + qa.w * qb.w < 0f) qb = new Quaternion(-qb.x, -qb.y, -qb.z, -qb.w);
            var q = Quaternion.Slerp(qa, qb, (float)t);
            return new[] { q.x, q.y, q.z, q.w };
        }

        private static Quaternion NormalizeOr(Quaternion q)
        {
            float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (len < 1e-6f || float.IsNaN(len)) return Quaternion.identity;
            return new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
        }

        private static float[] MulQ(float[] a, float[] b)
        {
            var q = new Quaternion(a[0], a[1], a[2], a[3]) * new Quaternion(b[0], b[1], b[2], b[3]);
            return new[] { q.x, q.y, q.z, q.w };
        }

        // Q.norm（長さ 0 なら 1 で割る）
        private static float[] NormJs(float[] q)
        {
            double l = Math.Sqrt((double)q[0] * q[0] + (double)q[1] * q[1] + (double)q[2] * q[2] + (double)q[3] * q[3]);
            if (l == 0.0 || double.IsNaN(l)) l = 1.0;
            return new[] { (float)(q[0] / l), (float)(q[1] / l), (float)(q[2] / l), (float)(q[3] / l) };
        }

        // ================================================================
        // 小物
        // ================================================================

        private static double JsRound(double x) => Math.Floor(x + 0.5);

        private static double Round7(double x) => Math.Round(x, 7, MidpointRounding.AwayFromZero);

        private static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static bool AllFinite(float[] v, int n)
        {
            if (v == null || v.Length < n) return false;
            for (int i = 0; i < n; i++) if (!IsFinite(v[i])) return false;
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
                inTangent = t.inTangent, outTangent = t.outTangent,
                inWeight = t.inWeight, outWeight = t.outWeight,
                weightedMode = t.weightedMode,
                leftTangentMode = t.leftTangentMode, rightTangentMode = t.rightTangentMode,
            };

        private static MotionTangentDTO[] CloneTans(MotionTangentDTO[] a)
            => a == null ? null : a.Select(CloneTan).ToArray();
    }
}
