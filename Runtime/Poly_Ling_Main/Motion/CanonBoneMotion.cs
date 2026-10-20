// CanonBoneMotion.cs
// ============================================================
// マッスル＋body ⇄ 正準骨格上のローカル回転（＋Hips の正規化位置）、目のマッスル ⇄ 視線（lookAt）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
//
// ■ 何をするか（PolyLing_VRMA整備計画.md 2.1・2.2・2.4）
//   ・マッスル＋body → 回転：CanonSkeleton の Humanoid に HumanPoseHandler.SetHumanPose で当て、
//     各ボーンの localRotation と Hips の位置を読む。合成は Unity に任せる（CanonMuscleSolver 冒頭の実測メモ）。
//   ・回転＋Hips → マッスル＋body：骨格へ入れて GetHumanPose。そのマッスルを RefineMuscles で
//     SetHumanPose の結果が元の回転に一致するよう詰める（GetHumanPose の値のままでは戻らない）。
//     bakedBones だけの .plmotion は読込時にこれでマッスル＋body へ焼く（BakeMuscles）。
//   ・body が無いクリップはレストの body（レスト姿勢で GetHumanPose した値）で解く。
//   ・クリップがマッスルを 1 本も持たないボーン（駆動されていないボーン）は、再生経路
//     （UnityClipApplier：祖先に付いていく）と同じくローカル単位として扱う。Hips は常に body から解く。
//
// ■ Hips の位置
//   bakedBones・VRMA とも「レスト位置からの差 ÷ レストの腰の高さ」（腰高 1 の正規化量）。
//   正準骨格の腰の高さは 1.0 なので、数値としてはメートルの差と同じになる。
//
// ■ 視線（lookAt）
//   回転の意味は VRMA の lookAt ノードの局所回転と同じ（Unity 左手系）。回転していないときの視線は +Z。
//   目のマッスル → lookAt：左右の目の向き（Head から見た向き。正準骨格はレストの回転が単位なので
//     目の localRotation × +Z）を平均し、yaw（Y 軸回り）・pitch（X 軸回り）を Extrinsic ZXY
//     （Quaternion.Euler(pitch, yaw, 0)）で回転にする。
//   lookAt → 目のマッスル：左右の目がそれぞれ同じ向きを向くよう、目の 2 マッスルを
//     Unity に当てながらニュートン法で解く（合成を自前で再現しない）。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public sealed class CanonBoneMotion : IDisposable
    {
        private readonly CanonHumanPoseRig _rig = new CanonHumanPoseRig();
        private HumanPose  _pose;
        private Vector3    _restBodyPos;
        private Quaternion _restBodyRot = Quaternion.identity;
        private float[]    _eyeBuf;

        /// <summary>正準骨格。Build 後に有効。</summary>
        public CanonSkeleton Skeleton => _rig.Skeleton;

        /// <summary>レスト姿勢の body（bodyPosition / bodyRotation）。</summary>
        public Vector3    RestBodyPosition => _restBodyPos;
        public Quaternion RestBodyRotation => _restBodyRot;

        public bool Build(out string reason)
        {
            if (!_rig.Build(out reason)) return false;
            _pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
            _rig.Skeleton.ResetToRest();
            _rig.GetHumanPose(ref _pose);
            _restBodyPos = _pose.bodyPosition;
            _restBodyRot = _pose.bodyRotation;
            RestMuscles  = (float[])_pose.muscles.Clone();
            return true;
        }

        /// <summary>
        /// レスト（T ポーズ）のマッスル値。クリップがトラックを持たないマッスルはこの値で解く。
        /// 0 で解くと駆動されていないボーンが「マッスル 0 の姿勢」（腕を下ろした姿勢など）になり、
        /// 回転は単位（祖先に付いていく）として扱うのに body（重心）だけがその姿勢で決まって食い違う。
        /// </summary>
        public float[] RestMuscles { get; private set; }

        public void Dispose() => _rig.Dispose();

        // ================================================================
        // マッスル → 回転
        // ================================================================

        /// <summary>マッスル値（HumanTrait.MuscleName の並び）と body を骨格へ当てる。body が null ならレストの body。</summary>
        public void SetMuscles(float[] muscles, Vector3? bodyPosition, Quaternion? bodyRotation)
        {
            int n = _pose.muscles.Length;
            for (int i = 0; i < n; i++)
                _pose.muscles[i] = (muscles != null && i < muscles.Length) ? muscles[i] : 0f;
            _pose.bodyPosition = bodyPosition ?? _restBodyPos;
            _pose.bodyRotation = bodyRotation ?? _restBodyRot;
            _rig.SetHumanPose(ref _pose);
        }

        /// <summary>現在の姿勢でのボーンのローカル回転。骨格に無ければ単位。</summary>
        public Quaternion GetLocal(HumanBodyBones bone)
            => Skeleton != null && Skeleton.HumanBones.TryGetValue(bone, out var t) && t != null
                ? t.localRotation : Quaternion.identity;

        /// <summary>現在の姿勢での Hips の正規化位置（レスト位置からの差 ÷ レストの腰の高さ）。</summary>
        public Vector3 GetHipsOffset()
        {
            if (Skeleton == null || !Skeleton.HumanBones.TryGetValue(HumanBodyBones.Hips, out var h) || h == null)
                return Vector3.zero;
            return (h.localPosition - CanonSkeleton.HipsRest) / CanonSkeleton.HipsHeight;
        }

        // ================================================================
        // 回転 → マッスル
        // ================================================================

        /// <summary>
        /// ローカル回転と Hips の正規化位置を骨格へ入れる。local が null を返すボーンは単位。
        /// Hips 以外の位置はレストのまま。
        /// </summary>
        public void SetLocals(Func<HumanBodyBones, Quaternion?> local, Vector3 hipsOffset)
        {
            if (Skeleton == null) return;
            foreach (var r in CanonSkeleton.Rows)
            {
                var t = Skeleton.HumanBones[r.Bone];
                t.localPosition = r.LocalPosition;
                var q = local?.Invoke(r.Bone);
                t.localRotation = q.HasValue ? q.Value : Quaternion.identity;
            }
            Skeleton.HumanBones[HumanBodyBones.Hips].localPosition
                = CanonSkeleton.HipsRest + hipsOffset * CanonSkeleton.HipsHeight;
        }

        /// <summary>現在の骨格の姿勢からマッスル値と body を読む。</summary>
        public void GetMuscles(float[] into, out Vector3 bodyPosition, out Quaternion bodyRotation)
        {
            _rig.GetHumanPose(ref _pose);
            if (into != null)
                for (int i = 0; i < into.Length && i < _pose.muscles.Length; i++) into[i] = _pose.muscles[i];
            bodyPosition = _pose.bodyPosition;
            bodyRotation = _pose.bodyRotation;
        }

        // ================================================================
        // マッスルの詰め（回転 → マッスルの補正）
        // ================================================================
        //
        // ■ なぜ要るか（PolyLing_Blender連携計画.md 6 の実測）
        //   GetHumanPose で読んだマッスルを SetHumanPose で回転へ戻しても元の回転にならない。
        //   ねじりのマッスルがおよそ半分になり（Avatar の twist 配分 0.5）、ねじりが大きいと 180° 反対の
        //   解になることもある。マッスルを正とするので、SetHumanPose の結果が元の回転に一致する
        //   マッスルを求め直す。
        // ■ やり方
        //   鎖（体幹・左右の腕・左右の脚・指 10 本）ごとに、目標の回転を持つボーンのマッスルだけを
        //   変数にして Levenberg–Marquardt で詰める。残差は各ボーンの「目標⁻¹・結果」の回転ベクトル
        //   （ラジアン）。初期値は GetHumanPose の値。ヤコビ行列は前進差分（刻み 1e-3）。
        //   鎖の中で目標の回転を持たないボーンのマッスルは動かさない（呼び出し側が書かないため）。
        //   2 自由度のボーン（LowerArm・LowerLeg）や親指など、マッスルで表せない回転は最小二乗の解で止まる。

        private static readonly HumanBodyBones[][] RefineChains =
        {
            new[]{ HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head },
            new[]{ HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            new[]{ HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            new[]{ HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes },
            new[]{ HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes },
            new[]{ HumanBodyBones.LeftThumbProximal,   HumanBodyBones.LeftThumbIntermediate,   HumanBodyBones.LeftThumbDistal },
            new[]{ HumanBodyBones.LeftIndexProximal,   HumanBodyBones.LeftIndexIntermediate,   HumanBodyBones.LeftIndexDistal },
            new[]{ HumanBodyBones.LeftMiddleProximal,  HumanBodyBones.LeftMiddleIntermediate,  HumanBodyBones.LeftMiddleDistal },
            new[]{ HumanBodyBones.LeftRingProximal,    HumanBodyBones.LeftRingIntermediate,    HumanBodyBones.LeftRingDistal },
            new[]{ HumanBodyBones.LeftLittleProximal,  HumanBodyBones.LeftLittleIntermediate,  HumanBodyBones.LeftLittleDistal },
            new[]{ HumanBodyBones.RightThumbProximal,  HumanBodyBones.RightThumbIntermediate,  HumanBodyBones.RightThumbDistal },
            new[]{ HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal },
            new[]{ HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[]{ HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal },
            new[]{ HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        private const int    RefineMaxIter = 60;
        private const float  RefineStep    = 1e-3f;
        private const double RefineDoneSq  = 1e-14;   // 残差の二乗和（ラジアン²）がこれ未満なら打ち切る

        private readonly List<HumanBodyBones> _rfBones = new List<HumanBodyBones>();
        private readonly List<int>            _rfPrm   = new List<int>();

        /// <summary>
        /// GetMuscles で読んだ muscles を、SetHumanPose の結果のローカル回転が target に一致するよう詰める。
        /// target が null を返すボーンは目標なし（そのマッスルは動かさない）。body は GetMuscles の値を渡す。
        /// マッスルを変えると GetHumanPose の body の位置では Hips が目標からずれる（実測 2e-4）ので、
        /// 最後に bodyPosition を Hips の差だけずらして Hips の正規化位置を hipsOffset に合わせる。
        /// 骨格の姿勢を書き換えるので、呼んだあとは姿勢を当て直すこと。
        /// </summary>
        public void RefineMuscles(float[] muscles, ref Vector3 bodyPosition, Quaternion bodyRotation,
                                  Func<HumanBodyBones, Quaternion?> target, Vector3 hipsOffset)
        {
            if (Skeleton == null || muscles == null || target == null) return;
            RefineRotations(muscles, bodyPosition, bodyRotation, target);
            for (int it = 0; it < 4; it++)
            {
                SetMuscles(muscles, bodyPosition, bodyRotation);
                var d = hipsOffset - GetHipsOffset();
                if (d.sqrMagnitude < 1e-14f) break;
                bodyPosition += d * CanonSkeleton.HipsHeight;
            }
        }

        private void RefineRotations(float[] muscles, Vector3 bodyPosition, Quaternion bodyRotation,
                                     Func<HumanBodyBones, Quaternion?> target)
        {
            foreach (var chain in RefineChains)
            {
                _rfBones.Clear();
                _rfPrm.Clear();
                foreach (var b in chain)
                {
                    if (!Skeleton.HumanBones.ContainsKey(b) || !target(b).HasValue) continue;
                    _rfBones.Add(b);
                    for (int d = 0; d < 3; d++)
                    {
                        int mi = HumanTrait.MuscleFromBone((int)b, d);
                        if (mi >= 0 && mi < muscles.Length) _rfPrm.Add(mi);
                    }
                }
                if (_rfBones.Count == 0 || _rfPrm.Count == 0) continue;
                RefineChain(muscles, bodyPosition, bodyRotation, target);
            }
        }

        private void RefineChain(float[] m, Vector3 bp, Quaternion br, Func<HumanBodyBones, Quaternion?> target)
        {
            int np = _rfPrm.Count, nr = _rfBones.Count * 3;
            var r0 = new double[nr];
            var r1 = new double[nr];
            ChainResidual(m, bp, br, target, r0);
            double c0 = SumSq(r0);
            if (c0 < RefineDoneSq) return;

            var J = new double[nr, np];
            var A = new double[np, np];
            var M = new double[np, np];
            var g = new double[np];
            var rhs = new double[np];
            var trial = new float[m.Length];
            double lambda = 1e-3;

            for (int it = 0; it < RefineMaxIter; it++)
            {
                for (int j = 0; j < np; j++)
                {
                    int mi = _rfPrm[j];
                    float keep = m[mi];
                    m[mi] = keep + RefineStep;
                    ChainResidual(m, bp, br, target, r1);
                    m[mi] = keep;
                    for (int i = 0; i < nr; i++) J[i, j] = (r1[i] - r0[i]) / RefineStep;
                }
                for (int a = 0; a < np; a++)
                {
                    for (int b = 0; b < np; b++)
                    {
                        double s = 0;
                        for (int i = 0; i < nr; i++) s += J[i, a] * J[i, b];
                        A[a, b] = s;
                    }
                    double gs = 0;
                    for (int i = 0; i < nr; i++) gs += J[i, a] * r0[i];
                    g[a] = -gs;
                }

                bool improved = false;
                for (int tries = 0; tries < 8 && !improved; tries++)
                {
                    for (int a = 0; a < np; a++)
                    {
                        for (int b = 0; b < np; b++) M[a, b] = A[a, b];
                        M[a, a] += lambda * (1.0 + A[a, a]);
                        rhs[a] = g[a];
                    }
                    if (!SolveLinear(M, rhs, np)) { lambda *= 10; continue; }
                    Array.Copy(m, trial, m.Length);
                    for (int a = 0; a < np; a++) trial[_rfPrm[a]] += (float)rhs[a];
                    ChainResidual(trial, bp, br, target, r1);
                    double ct = SumSq(r1);
                    if (ct < c0)
                    {
                        Array.Copy(trial, m, m.Length);
                        Array.Copy(r1, r0, nr);
                        c0 = ct;
                        lambda *= 0.3;
                        improved = true;
                    }
                    else lambda *= 10;
                }
                if (!improved || c0 < RefineDoneSq) break;
            }
        }

        // 鎖のボーンごとの「目標⁻¹・結果」の回転ベクトル（ラジアン）
        private void ChainResidual(float[] m, Vector3 bp, Quaternion br, Func<HumanBodyBones, Quaternion?> target, double[] r)
        {
            SetMuscles(m, bp, br);
            for (int i = 0; i < _rfBones.Count; i++)
            {
                var b = _rfBones[i];
                var dq = Quaternion.Inverse(target(b).Value) * GetLocal(b);
                double x = dq.x, y = dq.y, z = dq.z, w = dq.w;
                if (w < 0) { x = -x; y = -y; z = -z; w = -w; }
                double s = Math.Sqrt(x * x + y * y + z * z);
                double k = s > 1e-12 ? 2.0 * Math.Atan2(s, w) / s : 2.0;
                r[i * 3] = x * k; r[i * 3 + 1] = y * k; r[i * 3 + 2] = z * k;
            }
        }

        private static double SumSq(double[] r)
        {
            double s = 0;
            for (int i = 0; i < r.Length; i++) s += r[i] * r[i];
            return s;
        }

        // A x = b をガウスの消去法（部分ピボット）で解き、解を b に入れる。A は壊す。
        private static bool SolveLinear(double[,] A, double[] b, int n)
        {
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[piv, c])) piv = r;
                if (Math.Abs(A[piv, c]) < 1e-15) return false;
                if (piv != c)
                {
                    for (int k = 0; k < n; k++) { double t = A[c, k]; A[c, k] = A[piv, k]; A[piv, k] = t; }
                    double tb = b[c]; b[c] = b[piv]; b[piv] = tb;
                }
                for (int r = c + 1; r < n; r++)
                {
                    double f = A[r, c] / A[c, c];
                    for (int k = c; k < n; k++) A[r, k] -= f * A[c, k];
                    b[r] -= f * b[c];
                }
            }
            for (int r = n - 1; r >= 0; r--)
            {
                double s = b[r];
                for (int k = r + 1; k < n; k++) s -= A[r, k] * b[k];
                b[r] = s / A[r, r];
            }
            return true;
        }

        // ================================================================
        // 視線
        // ================================================================

        private static int[] _eyeIdx;

        /// <summary>目のマッスルの索引 [左 dof1, 左 dof2, 右 dof1, 右 dof2]（HumanTrait.MuscleName の並び）。</summary>
        public static int[] EyeMuscleIndices
        {
            get
            {
                if (_eyeIdx != null) return _eyeIdx;
                _eyeIdx = new[]
                {
                    HumanTrait.MuscleFromBone((int)HumanBodyBones.LeftEye,  1),
                    HumanTrait.MuscleFromBone((int)HumanBodyBones.LeftEye,  2),
                    HumanTrait.MuscleFromBone((int)HumanBodyBones.RightEye, 1),
                    HumanTrait.MuscleFromBone((int)HumanBodyBones.RightEye, 2),
                };
                return _eyeIdx;
            }
        }

        /// <summary>索引が目のマッスルか。</summary>
        public static bool IsEyeMuscle(int muscleIndex)
        {
            foreach (int i in EyeMuscleIndices) if (i == muscleIndex) return true;
            return false;
        }

        /// <summary>向き（Head から見た向き）→ lookAt 回転（Extrinsic ZXY の yaw・pitch）。</summary>
        public static Quaternion LookAtFromDirection(Vector3 dir)
        {
            YawPitch(dir, out float yaw, out float pitch);
            return Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>向き → yaw（Y 軸回り、+X へ向くと正）・pitch（X 軸回り、下へ向くと正）。度。</summary>
        public static void YawPitch(Vector3 dir, out float yaw, out float pitch)
        {
            if (dir.sqrMagnitude < 1e-12f) { yaw = 0f; pitch = 0f; return; }
            dir.Normalize();
            yaw   = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>現在の姿勢の目の向きから lookAt 回転を作る。目が無ければ false（単位）。</summary>
        public bool TryGetLookAt(out Quaternion lookAt)
        {
            lookAt = Quaternion.identity;
            if (Skeleton == null) return false;
            if (!Skeleton.HumanBones.TryGetValue(HumanBodyBones.LeftEye,  out var l) || l == null) return false;
            if (!Skeleton.HumanBones.TryGetValue(HumanBodyBones.RightEye, out var r) || r == null) return false;
            Vector3 d = l.localRotation * Vector3.forward + r.localRotation * Vector3.forward;
            if (d.sqrMagnitude < 1e-12f) return false;
            lookAt = LookAtFromDirection(d);
            return true;
        }

        /// <summary>
        /// lookAt 回転から目の 4 マッスルを解き、into の該当索引へ書く（ほかの索引は触らない）。
        /// 骨格の姿勢を書き換えるので、呼んだあとは姿勢を当て直すこと。
        /// 目のマッスルは [-1, 1] に収める（届かない向きはそこで止まる）。
        /// </summary>
        public bool SolveEyeMuscles(Quaternion lookAt, float[] into, out float residualDeg)
        {
            residualDeg = 0f;
            var idx = EyeMuscleIndices;
            foreach (int i in idx) if (i < 0) return false;
            if (Skeleton == null || into == null) return false;

            YawPitch(lookAt * Vector3.forward, out float ty, out float tp);
            if (_eyeBuf == null || _eyeBuf.Length != HumanTrait.MuscleCount) _eyeBuf = new float[HumanTrait.MuscleCount];

            var x = new float[4];
            const float h = 0.01f;
            float[] e0 = new float[4], e1 = new float[4], e2 = new float[4];
            for (int iter = 0; iter < 10; iter++)
            {
                EvalEyes(x, 0f, 0f, e0);
                residualDeg = 0f;
                for (int k = 0; k < 2; k++)
                    residualDeg = Mathf.Max(residualDeg,
                        Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(e0[k * 2], ty)), Mathf.Abs(Mathf.DeltaAngle(e0[k * 2 + 1], tp))));
                if (residualDeg < 0.01f) break;

                EvalEyes(x, h, 0f, e1);
                EvalEyes(x, 0f, h, e2);

                bool moved = false;
                for (int k = 0; k < 2; k++)   // 0 = 左、1 = 右
                {
                    float a11 = (e1[k * 2]     - e0[k * 2])     / h, a12 = (e2[k * 2]     - e0[k * 2])     / h;
                    float a21 = (e1[k * 2 + 1] - e0[k * 2 + 1]) / h, a22 = (e2[k * 2 + 1] - e0[k * 2 + 1]) / h;
                    float det = a11 * a22 - a12 * a21;
                    if (Mathf.Abs(det) < 1e-6f) continue;
                    float ry = Mathf.DeltaAngle(e0[k * 2], ty), rp = Mathf.DeltaAngle(e0[k * 2 + 1], tp);
                    float da = ( a22 * ry - a12 * rp) / det;
                    float db = (-a21 * ry + a11 * rp) / det;
                    float na = Mathf.Clamp(x[k * 2]     + da, -1f, 1f);
                    float nb = Mathf.Clamp(x[k * 2 + 1] + db, -1f, 1f);
                    if (Mathf.Abs(na - x[k * 2]) > 1e-6f || Mathf.Abs(nb - x[k * 2 + 1]) > 1e-6f) moved = true;
                    x[k * 2] = na; x[k * 2 + 1] = nb;
                }
                if (!moved) break;
            }
            for (int k = 0; k < 4; k++) into[idx[k]] = x[k];
            return true;
        }

        // x = [左 dof1, 左 dof2, 右 dof1, 右 dof2]。dA は dof1、dB は dof2 へ左右とも足す。
        // 結果 out = [左 yaw, 左 pitch, 右 yaw, 右 pitch]（Head から見た向き、度）。
        private void EvalEyes(float[] x, float dA, float dB, float[] result)
        {
            Array.Clear(_eyeBuf, 0, _eyeBuf.Length);
            var idx = EyeMuscleIndices;
            _eyeBuf[idx[0]] = x[0] + dA; _eyeBuf[idx[1]] = x[1] + dB;
            _eyeBuf[idx[2]] = x[2] + dA; _eyeBuf[idx[3]] = x[3] + dB;
            SetMuscles(_eyeBuf, null, null);
            YawPitch(GetLocal(HumanBodyBones.LeftEye)  * Vector3.forward, out result[0], out result[1]);
            YawPitch(GetLocal(HumanBodyBones.RightEye) * Vector3.forward, out result[2], out result[3]);
        }

        // ================================================================
        // bakedBones の焼き込み（MotionClipSerializer.Save が呼ぶ）
        // ================================================================

        /// <summary>
        /// muscles（＋body）から bakedBones を作り直して dto へ入れる。muscles が無ければ何もしない。
        /// 時刻はクリップの frameRate（無ければ 30）の格子で 0 秒から長さまで。
        /// 駆動されているボーン（目を含む）と Hips のトラックを書く。Hips は pos（正規化位置）も持つ。
        /// </summary>
        public static bool BakeBones(MotionClipDTO dto, out string reason)
        {
            reason = null;
            if (dto?.muscles == null || dto.muscles.Count == 0) return true;

            var sampler = new MotionMuscleSampler(dto, useBody: true);
            float fps = dto.frameRate > 0f ? dto.frameRate : 30f;
            float len = MotionClipSerializer.Length(dto);
            int n = Mathf.Max(1, Mathf.RoundToInt(len * fps) + 1);

            var bones = new List<HumanBodyBones>();
            foreach (var r in CanonSkeleton.Rows)
                if (r.Bone == HumanBodyBones.Hips || sampler.IsDriven(r.Bone)) bones.Add(r.Bone);

            var tracks = new List<MotionTrackDTO>(bones.Count);
            foreach (var b in bones)
                tracks.Add(new MotionTrackDTO { id = b.ToString(), targetKind = MotionClipDTO.TargetHumanoid });

            var cbm = new CanonBoneMotion();
            try
            {
                if (!cbm.Build(out reason)) return false;
                var buf = new float[HumanTrait.MuscleCount];
                for (int f = 0; f < n; f++)
                {
                    float t = Mathf.Min(f / fps, len);
                    sampler.Sample(t, buf, cbm.RestMuscles, out var bp, out var br);
                    cbm.SetMuscles(buf, bp, br);
                    for (int i = 0; i < bones.Count; i++)
                    {
                        var q = cbm.GetLocal(bones[i]);
                        var keys = tracks[i].keys;
                        if (keys.Count > 0)
                        {
                            var p = keys[keys.Count - 1].rot;
                            if (p[0] * q.x + p[1] * q.y + p[2] * q.z + p[3] * q.w < 0f)
                                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        }
                        var key = new MotionKeyDTO { t = t, rot = new[] { q.x, q.y, q.z, q.w } };
                        if (bones[i] == HumanBodyBones.Hips)
                        {
                            var o = cbm.GetHipsOffset();
                            key.pos = new[] { o.x, o.y, o.z };
                        }
                        keys.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                reason = $"bakedBones を作れません: {ex.Message}";
                return false;
            }
            finally
            {
                cbm.Dispose();
            }
            dto.bakedBones = tracks;
            return true;
        }

        // ================================================================
        // bakedBones → マッスル＋body（MotionClipSerializer.Parse が呼ぶ。PolyLing_VRMA整備計画.md 2.1）
        // ================================================================

        /// <summary>
        /// muscles が無く bakedBones があるクリップに、bakedBones から muscles と body を作って入れる。
        /// muscles があれば何もしない（再生の正は muscles）。
        /// 時刻は bakedBones（と、目に使うときの lookAt）のキー時刻をすべて合わせた格子。
        /// 接線を持つキーの区間は 4 等分した点も足す（VRMA 読込の CUBICSPLINE と同じ扱い）。
        /// 書くマッスルは bakedBones にあるボーンのもの。目は LeftEye / RightEye が無く lookAt があれば
        /// lookAt から解く（VRMA 読込と同じ）。正準骨格に無いボーン（Jaw など）は使わない。
        /// body は作り直す（元の body は捨てる）。読めなかったボーン名を ignored に入れる。
        /// </summary>
        public static bool BakeMuscles(MotionClipDTO dto, List<string> ignored, out bool baked, out string reason)
        {
            reason = null;
            baked  = false;
            if (dto == null) return true;
            if (dto.muscles != null && dto.muscles.Count > 0) return true;
            if (dto.bakedBones == null || dto.bakedBones.Count == 0) return true;

            // 使うトラック（正準骨格にあるボーンだけ）
            var inSkeleton = new HashSet<HumanBodyBones>();
            foreach (var r in CanonSkeleton.Rows) inSkeleton.Add(r.Bone);
            var tracks = new Dictionary<HumanBodyBones, MotionTrackDTO>();
            foreach (var tr in dto.bakedBones)
            {
                if (tr?.keys == null || tr.keys.Count == 0) continue;
                if (!Enum.TryParse(tr.id, out HumanBodyBones b) || !inSkeleton.Contains(b))
                {
                    ignored?.Add(tr.id);
                    continue;
                }
                tracks[b] = tr;
            }
            if (tracks.Count == 0) return true;

            bool eyesFromBones = tracks.ContainsKey(HumanBodyBones.LeftEye) || tracks.ContainsKey(HumanBodyBones.RightEye);
            var lookTrack = !eyesFromBones && dto.lookAt?.keys != null && dto.lookAt.keys.Count > 0 ? dto.lookAt : null;

            // 時刻の格子
            var timeSet = new SortedSet<float>();
            void AddTimes(MotionTrackDTO tr)
            {
                var ks = tr.keys;
                for (int i = 0; i < ks.Count; i++)
                {
                    if (ks[i] == null) continue;
                    timeSet.Add(ks[i].t);
                    bool hasTan = ks[i].rotTan != null || ks[i].posTan != null;
                    if (hasTan && i + 1 < ks.Count && ks[i + 1] != null)
                        for (int s = 1; s < 4; s++) timeSet.Add(Mathf.Lerp(ks[i].t, ks[i + 1].t, s / 4f));
                }
            }
            foreach (var tr in tracks.Values) AddTimes(tr);
            if (lookTrack != null) AddTimes(lookTrack);
            var times = new List<float>(timeSet);

            // 書くマッスル
            int mc = HumanTrait.MuscleCount;
            var write = new bool[mc];
            for (int mi = 0; mi < mc; mi++)
            {
                var b = (HumanBodyBones)HumanTrait.BoneFromMuscle(mi);
                write[mi] = IsEyeMuscle(mi) && !eyesFromBones ? lookTrack != null : tracks.ContainsKey(b);
            }
            var muscleTracks = new MotionScalarTrackDTO[mc];
            var mnames = HumanTrait.MuscleName;
            for (int mi = 0; mi < mc; mi++) if (write[mi]) muscleTracks[mi] = new MotionScalarTrackDTO { name = mnames[mi] };
            var body = new MotionTrackDTO { id = MotionClipDTO.BodyTrackId, targetKind = MotionClipDTO.TargetBody };

            tracks.TryGetValue(HumanBodyBones.Hips, out var hipsTrack);
            var cbm = new CanonBoneMotion();
            try
            {
                if (!cbm.Build(out reason)) return false;
                var buf = new float[mc];
                var locals = new Dictionary<HumanBodyBones, Quaternion>();
                Quaternion prevQ = Quaternion.identity;
                for (int f = 0; f < times.Count; f++)
                {
                    float t = times[f];
                    locals.Clear();
                    foreach (var kv in tracks)
                        if (MotionCurveMath.TryEvaluateRotation(kv.Value, t, out var q)) locals[kv.Key] = q;
                    Vector3 hipsOff = hipsTrack != null
                        && MotionCurveMath.TryEvaluateVector3(hipsTrack, MotionCurveMath.VectorChannel.Position, t, out var hp)
                        ? hp : Vector3.zero;

                    Func<HumanBodyBones, Quaternion?> target = b => locals.TryGetValue(b, out var lq) ? lq : (Quaternion?)null;
                    cbm.SetLocals(target, hipsOff);
                    cbm.GetMuscles(buf, out var bp, out var br);
                    cbm.RefineMuscles(buf, ref bp, br, target, hipsOff);
                    if (lookTrack != null && MotionCurveMath.TryEvaluateRotation(lookTrack, t, out var lookQ))
                        cbm.SolveEyeMuscles(lookQ, buf, out _);

                    for (int mi = 0; mi < mc; mi++)
                        if (write[mi]) muscleTracks[mi].keys.Add(new MotionScalarKeyDTO { t = t, v = buf[mi] });
                    if (f > 0 && Quaternion.Dot(prevQ, br) < 0f) br = new Quaternion(-br.x, -br.y, -br.z, -br.w);
                    prevQ = br;
                    body.keys.Add(new MotionKeyDTO { t = t, pos = new[] { bp.x, bp.y, bp.z }, rot = new[] { br.x, br.y, br.z, br.w } });
                }
            }
            catch (Exception ex)
            {
                reason = $"bakedBones からマッスルを作れません: {ex.Message}";
                return false;
            }
            finally
            {
                cbm.Dispose();
            }

            if (dto.muscles == null) dto.muscles = new List<MotionScalarTrackDTO>();
            foreach (var m in muscleTracks) if (m != null) dto.muscles.Add(m);
            dto.body = body;
            baked = true;
            return true;
        }
    }

    // ================================================================
    // マッスル＋body の標本化（MotionCurveMath の規則どおり。トラックごとに配列を前もって作る）
    // ================================================================
    public sealed class MotionMuscleSampler
    {
        private readonly int[]                 _n;
        private readonly float[][]             _t;
        private readonly float[][]             _v;
        private readonly MotionTangentDTO[][]  _tan;
        private readonly string[]              _pre, _post;
        private readonly MotionTrackDTO        _body;
        private readonly bool                  _hasPos, _hasRot;
        private readonly bool[]                _driven;
        private readonly bool[]                _zeroAbsent;

        /// <summary>目のマッスルを 1 本でも持つか。</summary>
        public bool HasEyeMuscles { get; }

        /// <summary>body を使うか（あれば）。</summary>
        public bool HasBody => _hasPos || _hasRot;

        public MotionMuscleSampler(MotionClipDTO clip, bool useBody)
        {
            int mc = HumanTrait.MuscleCount;
            _n = new int[mc]; _t = new float[mc][]; _v = new float[mc][]; _tan = new MotionTangentDTO[mc][];
            _pre = new string[mc]; _post = new string[mc];

            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++) index[names[i]] = i;

            if (clip?.muscles != null)
                foreach (var tr in clip.muscles)
                {
                    if (tr?.keys == null || tr.name == null || !index.TryGetValue(tr.name, out int mi)) continue;
                    var ks = new List<MotionScalarKeyDTO>();
                    foreach (var k in tr.keys) if (k != null) ks.Add(k);
                    if (ks.Count == 0) continue;
                    _n[mi] = ks.Count;
                    _t[mi] = new float[ks.Count]; _v[mi] = new float[ks.Count]; _tan[mi] = new MotionTangentDTO[ks.Count];
                    for (int j = 0; j < ks.Count; j++) { _t[mi][j] = ks[j].t; _v[mi][j] = ks[j].v; _tan[mi][j] = ks[j].tan; }
                    _pre[mi] = tr.preWrapMode; _post[mi] = tr.postWrapMode;
                }

            _driven = new bool[(int)HumanBodyBones.LastBone];
            for (int b = 0; b < _driven.Length; b++)
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(b, dof);
                    if (mi >= 0 && mi < mc && _n[mi] > 0) _driven[b] = true;
                }
            HasEyeMuscles = _driven[(int)HumanBodyBones.LeftEye] || _driven[(int)HumanBodyBones.RightEye];

            // 駆動されているボーンのトラックの無い dof は absentMuscles に従う（"zero" なら 0）。
            _zeroAbsent = new bool[mc];
            if (!MotionClipDTO.AbsentIsRest(clip))
                for (int mi = 0; mi < mc; mi++)
                {
                    int b = HumanTrait.BoneFromMuscle(mi);
                    if (_n[mi] == 0 && b >= 0 && b < _driven.Length && _driven[b]) _zeroAbsent[mi] = true;
                }

            if (useBody && clip?.body?.keys != null)
                foreach (var k in clip.body.keys)
                {
                    if (k == null) continue;
                    if (k.pos != null && k.pos.Length == 3) _hasPos = true;
                    if (k.rot != null && k.rot.Length == 4) _hasRot = true;
                }
            if (_hasPos || _hasRot) _body = clip.body;
        }

        /// <summary>そのボーンの dof のどれかにトラックがあるか。</summary>
        public bool IsDriven(HumanBodyBones b) => (int)b >= 0 && (int)b < _driven.Length && _driven[(int)b];

        /// <summary>
        /// time のマッスル値と body（無ければ null）。トラックの無いマッスルは absent の値
        /// （CanonBoneMotion.RestMuscles を渡す。null なら 0）。ただし駆動されているボーンの
        /// トラックの無い dof は、クリップの absentMuscles が "zero"（既定）なら 0。
        /// </summary>
        public void Sample(float time, float[] muscles, float[] absent, out Vector3? bodyPosition, out Quaternion? bodyRotation)
        {
            for (int i = 0; i < muscles.Length && i < _n.Length; i++)
            {
                int n = _n[i];
                if (n == 0)
                {
                    muscles[i] = _zeroAbsent[i] ? 0f : (absent != null && i < absent.Length ? absent[i] : 0f);
                    continue;
                }
                float mt = MotionCurveMath.WrapTime(n, _t[i], _pre[i], _post[i], time);
                muscles[i] = MotionCurveMath.EvaluateMapped(n, _t[i], _v[i], _tan[i], mt);
            }
            bodyPosition = null;
            bodyRotation = null;
            if (_hasPos && MotionCurveMath.TryEvaluateVector3(_body, MotionCurveMath.VectorChannel.Position, time, out var p))
                bodyPosition = p;
            if (_hasRot && MotionCurveMath.TryEvaluateRotation(_body, time, out var q))
                bodyRotation = q;
        }
    }

    // ================================================================
    // bakedBones（正準ローカル回転）の標本化（rotationSource = "bakedBones" の再生・書き出し）
    // ------------------------------------------------------------
    //   正準骨格にあるボーンのトラックだけを使う。目のトラックが無く lookAt があれば、
    //   左右の目のローカル回転を lookAt の回転とする（lookAt は Head から見た視線の回転で、
    //   正準骨格の目はレストの回転が単位なので、目のローカル回転と同じ意味）。
    // ================================================================
    public sealed class MotionBakedBoneSampler
    {
        private readonly List<KeyValuePair<HumanBodyBones, MotionTrackDTO>> _tracks
            = new List<KeyValuePair<HumanBodyBones, MotionTrackDTO>>();
        private readonly MotionTrackDTO _hips;
        private readonly MotionTrackDTO _look;

        public MotionBakedBoneSampler(MotionClipDTO clip)
        {
            var inSkeleton = new HashSet<HumanBodyBones>();
            foreach (var r in CanonSkeleton.Rows) inSkeleton.Add(r.Bone);
            if (clip?.bakedBones != null)
                foreach (var tr in clip.bakedBones)
                {
                    if (tr?.keys == null || tr.keys.Count == 0) continue;
                    if (!Enum.TryParse(tr.id, out HumanBodyBones b) || !inSkeleton.Contains(b)) continue;
                    _tracks.Add(new KeyValuePair<HumanBodyBones, MotionTrackDTO>(b, tr));
                    if (b == HumanBodyBones.Hips) _hips = tr;
                }
            bool eyes = false;
            foreach (var kv in _tracks)
                if (kv.Key == HumanBodyBones.LeftEye || kv.Key == HumanBodyBones.RightEye) eyes = true;
            if (!eyes && inSkeleton.Contains(HumanBodyBones.LeftEye)
                && clip?.lookAt?.keys != null && clip.lookAt.keys.Count > 0)
                _look = clip.lookAt;
        }

        /// <summary>time の正準ローカル回転を into に入れる（into は先に空にする）。</summary>
        public void Sample(float time, Dictionary<HumanBodyBones, Quaternion> into)
        {
            into.Clear();
            foreach (var kv in _tracks)
                if (MotionCurveMath.TryEvaluateRotation(kv.Value, time, out var q)) into[kv.Key] = q;
            if (_look != null && MotionCurveMath.TryEvaluateRotation(_look, time, out var lq))
            {
                into[HumanBodyBones.LeftEye]  = lq;
                into[HumanBodyBones.RightEye] = lq;
            }
        }

        /// <summary>time の Hips のローカル回転と正規化位置（無いチャンネルは単位・0）。Hips のトラックが無ければ false。</summary>
        public bool TrySampleHips(float time, out Quaternion rotation, out Vector3 offset)
        {
            rotation = Quaternion.identity;
            offset   = Vector3.zero;
            if (_hips == null) return false;
            if (MotionCurveMath.TryEvaluateRotation(_hips, time, out var q)) rotation = q;
            if (MotionCurveMath.TryEvaluateVector3(_hips, MotionCurveMath.VectorChannel.Position, time, out var p)) offset = p;
            return true;
        }
    }
}
