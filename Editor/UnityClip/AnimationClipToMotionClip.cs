// Editor/UnityClip/AnimationClipToMotionClip.cs
// ============================================================
// AnimationClip → MotionClipDTO（PolyLingMotion v2）変換
// ------------------------------------------------------------
// 対象範囲は既存経路（UnityClipExportWindow.BuildClipDto → MotionClipConverters.FromUnityClipDTO）と同じ。
//   - Transform カーブ（m_LocalPosition / m_LocalRotation / m_LocalScale）→ bones（targetKind = "path"）
//   - Animator バインディング（マッスル・RootT / RootQ 等）→ muscles（propertyName をそのまま名前にする）
//   - それ以外（BlendShape 等）は対象外
// 既存経路との違いは、接線を捨てずに v2 の形で持つこと。
//
// ■ 接線
//   チャンネル（pos / rot / scl）ごとに、成分カーブがすべて揃い、キー時刻も完全に一致するときだけ、
//   キーごとの inTangent / outTangent / inWeight / outWeight / weightedMode /
//   left・rightTangentMode を写す。無限大の接線（Constant）は傾きを null にしてモードだけで表す
//   （MotionTangentDTO の規約）。
//   成分カーブの時刻が揃わない・成分が欠けているチャンネルは、既存経路と同じく
//   時刻の和集合で Evaluate した値を持ち、接線は付けない（線形）。該当は Notes に記録する。
//   欠けた成分の値は pos=0 / scl=1 / rot=恒等の成分 で埋める（元の Transform 値は分からないため）。
//
// ■ キー
//   トラックのキー時刻は全チャンネルの和集合。各キーには、そのチャンネルが値を持つ時刻だけ値を入れる
//   （MotionCurveMath はチャンネルごとにキーを集めて評価する）。
//
// ■ 座標系
//   Unity 左手系のまま。変換はしない。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Motion;

namespace Poly_Ling.UnityClip.Editor
{
    public static class AnimationClipToMotionClip
    {
        /// <summary>変換結果。Notes は接線を持てなかったチャンネルなどの記録。</summary>
        public class Result
        {
            public MotionClipDTO Dto;
            public readonly List<string> Notes = new List<string>();
            public int TangentChannels;   // 接線を写したチャンネル数
            public int SampledChannels;   // 和集合サンプル（接線なし）にしたチャンネル数
        }

        private static readonly string[] ChannelNames = { "pos", "rot", "scl" };
        private static readonly int[]    ChannelSize  = { 3, 4, 3 };

        public static Result Convert(AnimationClip clip)
        {
            var res = new Result();
            var dto = new MotionClipDTO
            {
                name      = clip.name,
                frameRate = clip.frameRate > 0f ? clip.frameRate : 30f,
                duration  = clip.length,
                space     = "local",
                loop      = clip.isLooping,
                metadata  = new MotionMetadataDTO { createdWith = "PolyLing AnimationClipToMotionClip" }
            };
            res.Dto = dto;

            // path → [channel][component]
            var map = new SortedDictionary<string, AnimationCurve[][]>(StringComparer.Ordinal);

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(Animator))
                {
                    var mc = AnimationUtility.GetEditorCurve(clip, b);
                    var mt = BuildScalarTrack(b.propertyName, mc);
                    if (mt != null) dto.muscles.Add(mt);
                    continue;
                }
                if (b.type != typeof(Transform)) continue;

                int ch = ClassifyProperty(b.propertyName, out int comp);
                if (ch < 0) continue;                       // localEulerAnglesRaw 等は対象外（既存経路と同じ）
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;

                if (!map.TryGetValue(b.path, out var pc))
                {
                    pc = new[] { new AnimationCurve[3], new AnimationCurve[4], new AnimationCurve[3] };
                    map[b.path] = pc;
                }
                pc[ch][comp] = curve;
            }

            foreach (var kv in map)
            {
                var track = BuildBoneTrack(kv.Key, kv.Value, res);
                if (track != null) dto.bones.Add(track);
            }
            return res;
        }

        // ------------------------------------------------------------
        // ボーントラック
        // ------------------------------------------------------------
        private static MotionTrackDTO BuildBoneTrack(string path, AnimationCurve[][] pc, Result res)
        {
            // チャンネルごとの「時刻 → 値・接線」
            var chKeys = new Dictionary<float, (float[] v, MotionTangentDTO[] tan)>[3];
            AnimationCurve wrapSrc = null;

            for (int ch = 0; ch < 3; ch++)
            {
                var comps = pc[ch];
                int present = 0;
                foreach (var c in comps) if (c != null) { present++; if (wrapSrc == null) wrapSrc = c; }
                if (present == 0) continue;

                string label = $"{(path.Length == 0 ? "(root)" : path)} {ChannelNames[ch]}";
                bool complete = present == comps.Length;
                bool sameTimes = complete && SameKeyTimes(comps);
                var dict = new Dictionary<float, (float[] v, MotionTangentDTO[] tan)>();

                if (sameTimes)
                {
                    int n = comps[0].length;
                    for (int i = 0; i < n; i++)
                    {
                        var v = new float[comps.Length];
                        var tan = new MotionTangentDTO[comps.Length];
                        for (int c = 0; c < comps.Length; c++)
                        {
                            var k = comps[c][i];
                            v[c] = k.value;
                            tan[c] = ToTangent(comps[c], i);
                        }
                        dict[comps[0][i].time] = (v, tan);
                    }
                    res.TangentChannels++;
                }
                else
                {
                    var times = new SortedSet<float>();
                    foreach (var c in comps) if (c != null) foreach (var k in c.keys) times.Add(k.time);
                    foreach (float t in times)
                    {
                        var v = new float[comps.Length];
                        for (int c = 0; c < comps.Length; c++)
                            v[c] = comps[c] != null ? comps[c].Evaluate(t) : DefaultComponent(ch, c);
                        dict[t] = (v, null);
                    }
                    res.SampledChannels++;
                    res.Notes.Add(complete
                        ? $"{label}: 成分カーブのキー時刻が揃わないため、接線なし（線形）で書き出しました"
                        : $"{label}: 成分カーブが {present}/{comps.Length} 本のため、欠けた成分を既定値で埋め、接線なし（線形）で書き出しました");
                }
                chKeys[ch] = dict;
            }
            if (wrapSrc == null) return null;

            var allTimes = new SortedSet<float>();
            for (int ch = 0; ch < 3; ch++) if (chKeys[ch] != null) foreach (var t in chKeys[ch].Keys) allTimes.Add(t);

            var track = new MotionTrackDTO
            {
                id = path,
                targetKind = "path",
                preWrapMode  = ToWrap(wrapSrc.preWrapMode),
                postWrapMode = ToWrap(wrapSrc.postWrapMode)
            };
            foreach (float t in allTimes)
            {
                var key = new MotionKeyDTO { t = t };
                if (chKeys[0] != null && chKeys[0].TryGetValue(t, out var p)) { key.pos = p.v; key.posTan = p.tan; }
                if (chKeys[1] != null && chKeys[1].TryGetValue(t, out var r)) { key.rot = r.v; key.rotTan = r.tan; }
                if (chKeys[2] != null && chKeys[2].TryGetValue(t, out var s)) { key.scl = s.v; key.sclTan = s.tan; }
                track.keys.Add(key);
            }
            return track;
        }

        // ------------------------------------------------------------
        // スカラートラック（マッスル等）。1 本のカーブなので接線は常に写せる。
        // ------------------------------------------------------------
        private static MotionScalarTrackDTO BuildScalarTrack(string name, AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return null;
            var tr = new MotionScalarTrackDTO
            {
                name = name,
                preWrapMode  = ToWrap(curve.preWrapMode),
                postWrapMode = ToWrap(curve.postWrapMode)
            };
            for (int i = 0; i < curve.length; i++)
            {
                var k = curve[i];
                tr.keys.Add(new MotionScalarKeyDTO { t = k.time, v = k.value, tan = ToTangent(curve, i) });
            }
            return tr;
        }

        // ------------------------------------------------------------
        // ヘルパ
        // ------------------------------------------------------------
        private static bool SameKeyTimes(AnimationCurve[] comps)
        {
            int n = comps[0].length;
            for (int c = 1; c < comps.Length; c++) if (comps[c].length != n) return false;
            for (int i = 0; i < n; i++)
            {
                float t = comps[0][i].time;
                for (int c = 1; c < comps.Length; c++) if (comps[c][i].time != t) return false;
            }
            return true;
        }

        private static MotionTangentDTO ToTangent(AnimationCurve curve, int index)
        {
            var k = curve[index];
            var left  = AnimationUtility.GetKeyLeftTangentMode(curve, index);
            var right = AnimationUtility.GetKeyRightTangentMode(curve, index);
            var tn = new MotionTangentDTO
            {
                // 無限大（Constant）は JSON に書けないので null。モードで表す。
                inTangent  = IsFinite(k.inTangent)  ? k.inTangent  : (float?)null,
                outTangent = IsFinite(k.outTangent) ? k.outTangent : (float?)null,
                weightedMode     = k.weightedMode.ToString(),          // None / In / Out / Both
                leftTangentMode  = left.ToString(),                    // Free / Auto / Linear / Constant / ClampedAuto
                rightTangentMode = right.ToString()
            };
            if (k.weightedMode == WeightedMode.In  || k.weightedMode == WeightedMode.Both) tn.inWeight  = Mathf.Clamp01(k.inWeight);
            if (k.weightedMode == WeightedMode.Out || k.weightedMode == WeightedMode.Both) tn.outWeight = Mathf.Clamp01(k.outWeight);
            return tn;
        }

        private static string ToWrap(WrapMode m)
        {
            switch (m)
            {
                case WrapMode.Loop:     return "Loop";
                case WrapMode.PingPong: return "PingPong";
                default:                return "Clamp";
            }
        }

        private static float DefaultComponent(int ch, int comp)
        {
            if (ch == 2) return 1f;                  // scl
            if (ch == 1 && comp == 3) return 1f;     // rot.w
            return 0f;
        }

        // property 名 → (channel, component)。対象外は -1（UnityClipExportWindow.ClassifyProperty と同じ判定）
        private static int ClassifyProperty(string prop, out int comp)
        {
            comp = -1;
            if (string.IsNullOrEmpty(prop)) return -1;
            int dot = prop.LastIndexOf('.');
            if (dot < 0 || dot >= prop.Length - 1) return -1;
            string head = prop.Substring(0, dot);
            int ch;
            if (head == "m_LocalPosition")      ch = 0;
            else if (head == "m_LocalRotation") ch = 1;
            else if (head == "m_LocalScale")    ch = 2;
            else return -1;
            switch (prop[prop.Length - 1])
            {
                case 'x': comp = 0; break;
                case 'y': comp = 1; break;
                case 'z': comp = 2; break;
                case 'w': comp = 3; break;
                default: return -1;
            }
            if (comp >= ChannelSize[ch]) return -1;
            return ch;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
