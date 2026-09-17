// MotionCurveMath.cs
// MotionClipDTO のキー列を時刻で評価する規則の正本。
// Runtime/Poly_Ling_Main/Motion/ に配置
//
// ■ 評価の規則（恒久メモ）
//   1. 時刻をラップモードで先頭～末尾キーの範囲へ写す。
//        Clamp / ClampForever / Once / Default / null … 端のキーで止める
//        Loop     … 周期的に繰り返す
//        PingPong … 往復する
//   2. 時刻を挟む 2 キー k0, k1 を探す。
//   3. k0 の rightTangentMode か k1 の leftTangentMode が Constant なら k0 の値（階段）。
//   4. k0 と k1 のどちらも接線（tan）を持たないなら線形補間。
//      回転チャンネルは線形の代わりに Quaternion.Slerp（version 1 の挙動をそのまま保つ）。
//   5. それ以外は k0 の出側の傾き m0 と k1 の入側の傾き m1 を求め、3 次曲線で補間する。
//        重みが両側とも 1/3（weightedMode が該当側を含まない）ならエルミート補間。
//        そうでなければ重み付きベジェ（Unity の Keyframe と同じ制御点の置き方）。
//      回転チャンネルは 4 成分を別々に評価してから正規化する。
//
// ■ 傾きの求め方
//   tan を持たないキー     … 隣のキーへの直線の傾き（線形）。
//   Linear                 … 隣のキーへの直線の傾き。保存値は使わない。
//   Auto / ClampedAuto / Free … 保存値（inTangent / outTangent）があればそれを使う。
//                            無いときだけ下の PolyLing 定義で求める。
//     Auto        : 前後キーを結ぶ直線の傾き。端のキーは 0。
//     ClampedAuto : Auto を、前後の区間の傾きの符号が揃わなければ 0、
//                   揃うなら区間の傾きの小さい方の 3 倍までに抑える（行き過ぎを出さない）。
//     Free        : ClampedAuto と同じ。
//   ※ Unity の AnimationUtility が Auto / ClampedAuto で内部に持つ式と一致させたものではない。
//     Unity から取り出した値は保存値として入るので、その場合はこの定義を通らない。
//
// UnityEditor に依存しない。毎フレームの確保を避けるため、作業配列はスレッドごとに使い回す。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public static class MotionCurveMath
    {
        // ---- 接線モード ----
        public const string ModeFree        = "Free";
        public const string ModeAuto        = "Auto";
        public const string ModeClampedAuto = "ClampedAuto";
        public const string ModeLinear      = "Linear";
        public const string ModeConstant    = "Constant";

        // ---- 重みモード ----
        public const string WeightNone = "None";
        public const string WeightIn   = "In";
        public const string WeightOut  = "Out";
        public const string WeightBoth = "Both";

        // ---- ラップモード ----
        public const string WrapClamp        = "Clamp";
        public const string WrapClampForever = "ClampForever";
        public const string WrapOnce         = "Once";
        public const string WrapDefault      = "Default";
        public const string WrapLoop         = "Loop";
        public const string WrapPingPong     = "PingPong";

        private const float DefaultWeight = 1f / 3f;

        private static readonly string[] TangentModes =
            { ModeFree, ModeAuto, ModeClampedAuto, ModeLinear, ModeConstant };
        private static readonly string[] WeightedModes =
            { WeightNone, WeightIn, WeightOut, WeightBoth };
        private static readonly string[] WrapModes =
            { WrapClamp, WrapClampForever, WrapOnce, WrapDefault, WrapLoop, WrapPingPong };

        /// <summary>評価対象のチャンネル（位置・スケール）。</summary>
        public enum VectorChannel { Position, Scale }

        // ---- 作業配列（スレッドごと）----
        [ThreadStatic] private static float[] _bufT;
        [ThreadStatic] private static float[] _bufV;
        [ThreadStatic] private static MotionTangentDTO[] _bufTan;
        [ThreadStatic] private static int[] _bufIdx;

        private static void EnsureBuffers(int n)
        {
            if (_bufT != null && _bufT.Length >= n) return;
            int size = Mathf.Max(16, Mathf.NextPowerOfTwo(n));
            _bufT   = new float[size];
            _bufV   = new float[size];
            _bufTan = new MotionTangentDTO[size];
            _bufIdx = new int[size];
        }

        // ================================================================
        // 名前の検査
        // ================================================================

        /// <summary>接線モードとして読める文字列か。null は許す（Free 扱い）。</summary>
        public static bool IsKnownTangentMode(string s) => s == null || IndexOf(TangentModes, s) >= 0;

        /// <summary>重みモードとして読める文字列か。null は許す（None 扱い）。</summary>
        public static bool IsKnownWeightedMode(string s) => s == null || IndexOf(WeightedModes, s) >= 0;

        /// <summary>ラップモードとして読める文字列か。null は許す（Clamp 扱い）。</summary>
        public static bool IsKnownWrapMode(string s) => s == null || IndexOf(WrapModes, s) >= 0;

        private static int IndexOf(string[] table, string s)
        {
            for (int i = 0; i < table.Length; i++)
                if (string.Equals(table[i], s, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static bool Is(string s, string mode)
            => string.Equals(s, mode, StringComparison.OrdinalIgnoreCase);

        // ================================================================
        // スカラートラック
        // ================================================================

        /// <summary>スカラートラックを time（秒）で評価する。キーが無ければ fallback。</summary>
        public static float EvaluateScalar(MotionScalarTrackDTO track, float time, float fallback = 0f)
        {
            if (track == null || track.keys == null || track.keys.Count == 0) return fallback;

            EnsureBuffers(track.keys.Count);
            int n = 0;
            foreach (var k in track.keys)
            {
                if (k == null) continue;
                _bufT[n] = k.t; _bufV[n] = k.v; _bufTan[n] = k.tan;
                n++;
            }
            if (n == 0) return fallback;

            float mapped = WrapTime(n, _bufT, track.preWrapMode, track.postWrapMode, time);
            return EvaluateMapped(n, _bufT, _bufV, _bufTan, mapped);
        }

        // ================================================================
        // ボーントラック
        // ================================================================

        /// <summary>位置またはスケールを time（秒）で評価する。そのチャンネルのキーが無ければ false。</summary>
        public static bool TryEvaluateVector3(MotionTrackDTO track, VectorChannel channel, float time, out Vector3 value)
        {
            value = Vector3.zero;
            if (track == null || track.keys == null || track.keys.Count == 0) return false;

            EnsureBuffers(track.keys.Count);
            int n = CollectChannelKeys(track.keys, channel == VectorChannel.Position ? 0 : 2, 3);
            if (n == 0) return false;

            // 時刻の写しは成分によらないので、先頭成分の時刻列で一度だけ求める。
            for (int j = 0; j < n; j++) _bufT[j] = track.keys[_bufIdx[j]].t;
            float mapped = WrapTime(n, _bufT, track.preWrapMode, track.postWrapMode, time);

            for (int c = 0; c < 3; c++)
            {
                FillComponent(track.keys, n, channel == VectorChannel.Position ? 0 : 2, c);
                value[c] = EvaluateMapped(n, _bufT, _bufV, _bufTan, mapped);
            }
            return true;
        }

        /// <summary>回転を time（秒）で評価する。回転キーが無ければ false。結果は正規化済み。</summary>
        public static bool TryEvaluateRotation(MotionTrackDTO track, float time, out Quaternion value)
        {
            value = Quaternion.identity;
            if (track == null || track.keys == null || track.keys.Count == 0) return false;

            EnsureBuffers(track.keys.Count);
            int n = CollectChannelKeys(track.keys, 1, 4);
            if (n == 0) return false;

            for (int j = 0; j < n; j++) _bufT[j] = track.keys[_bufIdx[j]].t;
            float mapped = WrapTime(n, _bufT, track.preWrapMode, track.postWrapMode, time);

            var first = track.keys[_bufIdx[0]];
            var last  = track.keys[_bufIdx[n - 1]];
            if (n == 1 || mapped <= _bufT[0]) { value = NormalizeOr(ToQuat(first.rot), Quaternion.identity); return true; }
            if (mapped >= _bufT[n - 1])      { value = NormalizeOr(ToQuat(last.rot),  Quaternion.identity); return true; }

            int seg = FindSegment(n, _bufT, mapped);
            var k0 = track.keys[_bufIdx[seg]];
            var k1 = track.keys[_bufIdx[seg + 1]];

            // 規則 4: 両キーとも接線を持たなければ Slerp。
            if (k0.rotTan == null && k1.rotTan == null)
            {
                float dt = _bufT[seg + 1] - _bufT[seg];
                if (dt <= 0f) { value = NormalizeOr(ToQuat(k1.rot), Quaternion.identity); return true; }
                float u = (mapped - _bufT[seg]) / dt;
                value = Quaternion.Slerp(ToQuat(k0.rot), ToQuat(k1.rot), u);
                return true;
            }

            // 規則 5: 成分ごとに評価して正規化。
            var q = new Quaternion();
            for (int c = 0; c < 4; c++)
            {
                FillComponent(track.keys, n, 1, c);
                q[c] = EvaluateMapped(n, _bufT, _bufV, _bufTan, mapped);
            }
            value = NormalizeOr(q, NormalizeOr(ToQuat(k0.rot), Quaternion.identity));
            return true;
        }

        // チャンネル（0=pos, 1=rot, 2=scl）を持つキーの番号を _bufIdx に集める。
        private static int CollectChannelKeys(List<MotionKeyDTO> keys, int channel, int length)
        {
            int n = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (k == null) continue;
                float[] arr = channel == 0 ? k.pos : channel == 1 ? k.rot : k.scl;
                if (arr == null || arr.Length < length) continue;
                _bufIdx[n++] = i;
            }
            return n;
        }

        // 成分 c の値と接線を _bufV / _bufTan に詰める（_bufIdx は CollectChannelKeys 済み）。
        private static void FillComponent(List<MotionKeyDTO> keys, int n, int channel, int c)
        {
            for (int j = 0; j < n; j++)
            {
                var k = keys[_bufIdx[j]];
                float[] arr = channel == 0 ? k.pos : channel == 1 ? k.rot : k.scl;
                MotionTangentDTO[] tans = channel == 0 ? k.posTan : channel == 1 ? k.rotTan : k.sclTan;
                _bufV[j]   = arr[c];
                _bufTan[j] = (tans != null && tans.Length > c) ? tans[c] : null;
            }
        }

        // ================================================================
        // 中核
        // ================================================================

        /// <summary>時刻を先頭～末尾キーの範囲へ写す（規則 1）。</summary>
        public static float WrapTime(int n, float[] t, string pre, string post, float time)
        {
            if (n <= 0) return time;
            float a = t[0], b = t[n - 1];
            float len = b - a;
            if (time >= a && time <= b) return time;
            if (n < 2 || len <= 0f) return a;

            string mode = time < a ? pre : post;
            if (Is(mode, WrapLoop))     return a + Mathf.Repeat(time - a, len);
            if (Is(mode, WrapPingPong)) return a + Mathf.PingPong(time - a, len);
            return Mathf.Clamp(time, a, b);
        }

        /// <summary>範囲内へ写した時刻で評価する（規則 2～5）。</summary>
        public static float EvaluateMapped(int n, float[] t, float[] v, MotionTangentDTO[] tan, float time)
        {
            if (n <= 0) return 0f;
            if (n == 1 || time <= t[0]) return v[0];
            if (time >= t[n - 1]) return v[n - 1];

            int i = FindSegment(n, t, time);
            float dt = t[i + 1] - t[i];
            if (dt <= 0f) return v[i + 1];

            var a = tan[i];
            var b = tan[i + 1];

            // 規則 3: 階段
            if ((a != null && Is(a.rightTangentMode, ModeConstant)) ||
                (b != null && Is(b.leftTangentMode, ModeConstant)))
                return v[i];

            float u = (time - t[i]) / dt;

            // 規則 4: 線形
            if (a == null && b == null) return Mathf.Lerp(v[i], v[i + 1], u);

            // 規則 5: 3 次
            float m0 = ResolveOut(n, t, v, tan, i);
            float m1 = ResolveIn(n, t, v, tan, i + 1);
            float w0 = HasOutWeight(a) ? Mathf.Clamp01(a.outWeight ?? DefaultWeight) : DefaultWeight;
            float w1 = HasInWeight(b)  ? Mathf.Clamp01(b.inWeight  ?? DefaultWeight) : DefaultWeight;

            if (Mathf.Approximately(w0, DefaultWeight) && Mathf.Approximately(w1, DefaultWeight))
                return Hermite(v[i], m0, v[i + 1], m1, dt, u);

            return WeightedBezier(v[i], m0, w0, v[i + 1], m1, w1, dt, u);
        }

        // time を挟む区間の左端の番号（0～n-2）。t[0] < time < t[n-1] を前提とする。
        private static int FindSegment(int n, float[] t, float time)
        {
            int i = 0;
            while (i < n - 2 && t[i + 1] <= time) i++;
            return i;
        }

        private static bool HasOutWeight(MotionTangentDTO k)
            => k != null && (Is(k.weightedMode, WeightOut) || Is(k.weightedMode, WeightBoth));

        private static bool HasInWeight(MotionTangentDTO k)
            => k != null && (Is(k.weightedMode, WeightIn) || Is(k.weightedMode, WeightBoth));

        // キー i の出側の傾き。
        private static float ResolveOut(int n, float[] t, float[] v, MotionTangentDTO[] tan, int i)
        {
            var k = tan[i];
            if (k == null || Is(k.rightTangentMode, ModeLinear))
                return i < n - 1 ? Slope(t, v, i, i + 1) : (i > 0 ? Slope(t, v, i - 1, i) : 0f);
            if (k.outTangent.HasValue) return k.outTangent.Value;
            return Smooth(n, t, v, i, clamped: !Is(k.rightTangentMode, ModeAuto));
        }

        // キー i の入側の傾き。
        private static float ResolveIn(int n, float[] t, float[] v, MotionTangentDTO[] tan, int i)
        {
            var k = tan[i];
            if (k == null || Is(k.leftTangentMode, ModeLinear))
                return i > 0 ? Slope(t, v, i - 1, i) : (i < n - 1 ? Slope(t, v, i, i + 1) : 0f);
            if (k.inTangent.HasValue) return k.inTangent.Value;
            return Smooth(n, t, v, i, clamped: !Is(k.leftTangentMode, ModeAuto));
        }

        private static float Slope(float[] t, float[] v, int a, int b)
        {
            float dt = t[b] - t[a];
            return dt > 0f ? (v[b] - v[a]) / dt : 0f;
        }

        // Auto / ClampedAuto の PolyLing 定義（冒頭の説明を参照）。
        private static float Smooth(int n, float[] t, float[] v, int i, bool clamped)
        {
            if (n < 3 || i <= 0 || i >= n - 1) return 0f;

            float span = t[i + 1] - t[i - 1];
            float m = span > 0f ? (v[i + 1] - v[i - 1]) / span : 0f;
            if (!clamped) return m;

            float s0 = Slope(t, v, i - 1, i);
            float s1 = Slope(t, v, i, i + 1);
            if (s0 * s1 <= 0f) return 0f;
            float limit = 3f * Mathf.Min(Mathf.Abs(s0), Mathf.Abs(s1));
            if (Mathf.Abs(m) > limit) m = Mathf.Sign(m) * limit;
            return m;
        }

        private static float Hermite(float v0, float m0, float v1, float m1, float dt, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            float h00 =  2f * u3 - 3f * u2 + 1f;
            float h10 =       u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 =       u3 -      u2;
            return h00 * v0 + h10 * dt * m0 + h01 * v1 + h11 * dt * m1;
        }

        // 制御点: 時刻側 (0, w0, 1-w1, 1)、値側 (v0, v0+m0·w0·dt, v1-m1·w1·dt, v1)。
        // 時刻側の 3 次式が u になる媒介変数 s を二分法で求めてから値側を評価する。
        private static float WeightedBezier(float v0, float m0, float w0, float v1, float m1, float w1, float dt, float u)
        {
            float x1 = w0, x2 = 1f - w1;
            float lo = 0f, hi = 1f, s = u;
            for (int it = 0; it < 40; it++)
            {
                s = 0.5f * (lo + hi);
                float x = Bezier(0f, x1, x2, 1f, s);
                if (Mathf.Abs(x - u) < 1e-6f) break;
                if (x < u) lo = s; else hi = s;
            }
            float y1 = v0 + m0 * w0 * dt;
            float y2 = v1 - m1 * w1 * dt;
            return Bezier(v0, y1, y2, v1, s);
        }

        private static float Bezier(float p0, float p1, float p2, float p3, float s)
        {
            float r = 1f - s;
            return r * r * r * p0 + 3f * r * r * s * p1 + 3f * r * s * s * p2 + s * s * s * p3;
        }

        // ================================================================
        // クォータニオン
        // ================================================================

        private static Quaternion ToQuat(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        private static Quaternion NormalizeOr(Quaternion q, Quaternion fallback)
        {
            float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (len < 1e-6f || float.IsNaN(len)) return fallback;
            return new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
        }

        /// <summary>
        /// 回転キーの符号を隣と揃える。前のキーとの内積が負なら 4 成分と
        /// そのキーの回転接線（in / out）の符号をすべて反転する。反転したキー数を返す。
        /// </summary>
        public static int EnsureQuaternionContinuity(MotionTrackDTO track)
        {
            if (track?.keys == null) return 0;
            int flipped = 0;
            float[] prev = null;
            foreach (var k in track.keys)
            {
                if (k?.rot == null || k.rot.Length < 4) continue;
                if (prev != null)
                {
                    float dot = prev[0] * k.rot[0] + prev[1] * k.rot[1] + prev[2] * k.rot[2] + prev[3] * k.rot[3];
                    if (dot < 0f)
                    {
                        for (int c = 0; c < 4; c++) k.rot[c] = -k.rot[c];
                        if (k.rotTan != null)
                            foreach (var tn in k.rotTan)
                            {
                                if (tn == null) continue;
                                if (tn.inTangent.HasValue)  tn.inTangent  = -tn.inTangent.Value;
                                if (tn.outTangent.HasValue) tn.outTangent = -tn.outTangent.Value;
                            }
                        flipped++;
                    }
                }
                prev = k.rot;
            }
            return flipped;
        }
    }
}
