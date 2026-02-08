// VMDToHumanoidClip.cs
// IKベイク済みVMDモーションをUnity Humanoid AnimationClipに変換する
//
// ================================================================
// ■ 全体方針
// ================================================================
// VMDの各キーフレームでHumanPoseHandlerを使いマッスル値を取得し、
// AnimationClipのカーブとして記録する。
// キーフレーム間の補間はVMDのベジェ制御点からAnimationCurveのtangentを
// 近似変換して移植する。
//
// ================================================================
// ■ 方式の欠点（重要）
// ================================================================
// 1. 非線形変換による補間誤差
//    VMDのベジェカーブは「Quaternion Slerpのパラメータt」に対する時間カーブ。
//    一方、Humanoidマッスル値はQuaternionをSwing-Twist分解し、
//    各軸のAngleをマッスルレンジで正規化した値（-1〜1）。
//    この変換は非線形であるため、同じベジェカーブを適用しても
//    中間フレームでのマッスル値の推移が元のVMDと一致しない。
//    特に大きな回転や複数軸が同時に回転する場合に誤差が顕著になる。
//
// 2. 軸分離の不整合
//    VMDのベジェカーブは回転全体で1本（Curves[3]）。
//    つまりSlerp補間のtに対して1つのカーブがかかる。
//    しかしHumanoidマッスルは各軸独立（例: LeftUpperLeg.x, .y, .z）。
//    1本のカーブを全マッスル軸に同一のtangentとして適用するため、
//    軸ごとに異なるイージングが必要なケースでは不正確になる。
//
// 3. RootMotion（Hips移動）の補間
//    Hipsの移動にはVMDの位置ベジェ(Curves[0..2])を使うが、
//    マッスル空間のRootT/RootQへの変換でも同様の非線形誤差が生じる。
//
// 4. tangent近似の限界
//    VMDのベジェは3次ベジェ（4制御点: (0,0), v1, v2, (1,1)）、
//    Unity AnimationCurveのKeyframeはHermite補間（inTangent/outTangent）。
//    曲線の数学的構造が異なるため、完全な再現は原理的に不可能。
//    本実装では端点での接線傾きを一致させる近似を行う。
//
// これらの理由から、出力されたAnimationClipは手作業での調整を前提とする。
// 全フレームサンプリングに比べてキーフレーム数が少なく編集しやすい利点がある。
//
// ================================================================
// ■ tangent変換の数学的背景
// ================================================================
// VMDのベジェ曲線は、始点(0,0)、制御点v1(v1.x, v1.y)、
// 制御点v2(v2.x, v2.y)、終点(1,1) の3次ベジェ。
// 横軸が正規化時間t∈[0,1]、縦軸が補間値s∈[0,1]。
//
// 3次ベジェの定義:
//   B(u) = (1-u)³·P0 + 3(1-u)²u·P1 + 3(1-u)u²·P2 + u³·P3
//   P0=(0,0), P1=v1, P2=v2, P3=(1,1)
//   uはベジェパラメータ（時間tとは異なる）
//
// u=0での接線（始点）:
//   dB/du|_{u=0} = 3(P1 - P0) = 3·v1
//   → x方向の変化率: 3·v1.x
//   → y方向の変化率: 3·v1.y
//   → dy/dx|_{u=0} = v1.y / v1.x   （v1.x > 0 の場合）
//
// u=1での接線（終点）:
//   dB/du|_{u=1} = 3(P3 - P2) = 3·(1-v2)
//   → x方向の変化率: 3·(1-v2.x)
//   → y方向の変化率: 3·(1-v2.y)
//   → dy/dx|_{u=1} = (1-v2.y) / (1-v2.x)   （v2.x < 1 の場合）
//
// これらは正規化空間（時間0〜1、値0〜1）での傾き。
// AnimationCurveのtangentは実時間・実値空間（秒, マッスル値）なので、
// 以下のスケーリングが必要:
//
//   outTangent = (v1.y / v1.x) × (ΔValue / ΔTime)
//   inTangent  = ((1-v2.y) / (1-v2.x)) × (ΔValue / ΔTime)
//
// ここで:
//   ΔValue = 次キーフレームのマッスル値 - 現キーフレームのマッスル値
//   ΔTime  = 次キーフレームの時間(秒) - 現キーフレームの時間(秒)
//
// ■ 制御点が特殊な場合:
//   v1.x ≈ 0 → 始点で垂直（急加速）。outTangentを大きな値にクランプ。
//   v2.x ≈ 1 → 終点で垂直（急減速）。inTangentを大きな値にクランプ。
//   v1 = (0.25, 0.25) かつ v2 = (0.75, 0.75) → 線形補間。
//     このとき outTangent = inTangent = ΔValue/ΔTime。
//
// ■ Hermiteとの差異:
//   Unity AnimationCurveの3次Hermite:
//     H(t) = (2t³-3t²+1)·p0 + (t³-2t²+t)·m0 + (-2t³+3t²)·p1 + (t³-t²)·m1
//   ここでm0=outTangent×ΔTime, m1=inTangent×ΔTime。
//   端点での接線は一致するが、中間部の曲率は3次ベジェと異なる。
//   特にS字カーブ（イーズイン・アウト）では中間部の形状にずれが出る。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDモーション → Unity Humanoid AnimationClip 変換器
    /// </summary>
    public static class VMDToHumanoidClip
    {
        // tangent計算で0除算を回避するための閾値
        private const float TangentEpsilon = 0.001f;

        // tangentの絶対値上限（垂直に近いカーブのクランプ用）
        private const float MaxTangent = 1000f;

        /// <summary>
        /// VMDデータからHumanoid AnimationClipを生成する
        /// </summary>
        /// <param name="vmd">IKベイク済みVMDデータ</param>
        /// <param name="model">ModelContext（HumanoidMapping参照用）</param>
        /// <param name="applier">座標変換設定済みVMDApplier</param>
        /// <param name="rootGameObject">エクスポート済みGameObject（Animator+Avatar付き）</param>
        /// <param name="createdObjects">エクスポートで作成されたGameObject配列（MeshContextListと同インデックス）</param>
        /// <param name="fps">フレームレート（VMD標準は30fps）</param>
        /// <returns>生成されたAnimationClip（null=失敗）</returns>
        public static AnimationClip Convert(
            VMDData vmd,
            Model.ModelContext model,
            VMDApplier applier,
            GameObject rootGameObject,
            GameObject[] createdObjects,
            float fps = 30f)
        {
            if (vmd == null || model == null || applier == null || rootGameObject == null)
            {
                Debug.LogError("[VMDToHumanoidClip] Null parameter");
                return null;
            }

            // Animator/Avatar取得
            var animator = rootGameObject.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[VMDToHumanoidClip] Root object must have Animator with valid Humanoid Avatar");
                return null;
            }

            // HumanPoseHandler作成
            var poseHandler = new HumanPoseHandler(animator.avatar, rootGameObject.transform);
            var humanPose = new HumanPose();

            // マッスル名リストを取得
            int muscleCount = HumanTrait.MuscleCount;
            string[] muscleNames = HumanTrait.MuscleName;

            // 全キーフレーム番号を収集（ソート済み）
            var frameNumbers = CollectSortedFrameNumbers(vmd);
            if (frameNumbers.Count == 0)
            {
                Debug.LogWarning("[VMDToHumanoidClip] No keyframes found");
                return null;
            }

            Debug.Log($"[VMDToHumanoidClip] Converting {frameNumbers.Count} keyframes, {muscleCount} muscles");

            // 各キーフレームのフレーム番号から、該当フレームのベジェカーブを取得するためのマップ
            // （HumanoidMapping経由でPMXボーン名 → VMDキーフレームを特定）
            var ikBakedBoneNames = CollectIKBakedBoneNames(model);

            // ================================================================
            // Phase 1: 各キーフレームでマッスル値をサンプリング
            // ================================================================

            // muscles[keyframeIdx][muscleIdx]
            var sampledMuscles = new List<float[]>();
            // rootPosition[keyframeIdx], rootRotation[keyframeIdx]
            var sampledRootPositions = new List<Vector3>();
            var sampledRootRotations = new List<Quaternion>();

            for (int ki = 0; ki < frameNumbers.Count; ki++)
            {
                uint frameNumber = frameNumbers[ki];

                // VMDポーズを適用（IKはベイク済みなので無効で適用）
                bool origIK = applier.EnableIK;
                applier.EnableIK = false;
                applier.ApplyFrame(model, vmd, frameNumber);
                applier.EnableIK = origIK;

                // MeshContextの結果をGameObjectのTransformに反映
                ApplyPoseToTransforms(model, createdObjects);

                // HumanPoseHandlerでマッスル値を取得
                poseHandler.GetHumanPose(ref humanPose);

                // マッスル値をコピー
                float[] muscles = new float[muscleCount];
                Array.Copy(humanPose.muscles, muscles, muscleCount);
                sampledMuscles.Add(muscles);

                sampledRootPositions.Add(humanPose.bodyPosition);
                sampledRootRotations.Add(humanPose.bodyRotation);
            }

            // ================================================================
            // Phase 2: VMDベジェカーブからtangent情報を取得
            // ================================================================

            // 各キーフレームに対応するVMD回転ベジェカーブを取得
            // （複数ボーンのカーブが異なる場合があるが、代表として最も影響の大きいボーンを使用）
            var bezierPerFrame = BuildBezierMap(vmd, frameNumbers);

            // ================================================================
            // Phase 3: AnimationCurveを構築
            // ================================================================

            var clip = new AnimationClip();
            clip.legacy = false;

            // --- マッスルカーブ ---
            for (int mi = 0; mi < muscleCount; mi++)
            {
                var keyframes = new List<Keyframe>();

                for (int ki = 0; ki < frameNumbers.Count; ki++)
                {
                    float time = frameNumbers[ki] / fps;
                    float value = sampledMuscles[ki][mi];

                    var kf = new Keyframe(time, value);

                    // tangent計算
                    // 前後のキーフレームとの値差分・時間差分を使い、
                    // VMDベジェの制御点からtangentを近似する
                    ComputeTangents(
                        ki, frameNumbers, fps,
                        idx => sampledMuscles[idx][mi],
                        bezierPerFrame,
                        out float inTan, out float outTan);

                    kf.inTangent = inTan;
                    kf.outTangent = outTan;

                    keyframes.Add(kf);
                }

                if (keyframes.Count > 0 && HasNonZeroValues(keyframes))
                {
                    var curve = new AnimationCurve(keyframes.ToArray());
                    clip.SetCurve("", typeof(Animator), muscleNames[mi], curve);
                }
            }

            // --- RootMotionカーブ（Hips移動・回転） ---
            // bodyPositionはRootT.x/y/z、bodyRotationはRootQ.x/y/z/w
            BuildRootMotionCurves(clip, frameNumbers, fps, bezierPerFrame,
                sampledRootPositions, sampledRootRotations, vmd);

            // クリップ設定
            clip.frameRate = fps;

            // Humanoidクリップとして設定
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            Debug.Log($"[VMDToHumanoidClip] Clip created: {frameNumbers.Count} keyframes, " +
                      $"{clip.length:F2}s, {muscleCount} muscles");

            poseHandler.Dispose();
            return clip;
        }

        // ================================================================
        // Transform反映
        // ================================================================

        /// <summary>
        /// VMDApplier適用後のMeshContextの結果（WorldMatrix）を
        /// エクスポート済みGameObjectのTransformに反映する。
        ///
        /// VMDApplierはMeshContext.BonePoseDataにデルタを設定し、
        /// ComputeWorldMatricesでWorldMatrixを計算する。
        /// HumanPoseHandlerはTransform階層を読むため、
        /// WorldMatrixからlocalRotation/localPositionに逆算して書き込む必要がある。
        /// </summary>
        private static void ApplyPoseToTransforms(
            Model.ModelContext model, GameObject[] createdObjects)
        {
            if (createdObjects == null) return;

            for (int i = 0; i < model.MeshContextList.Count && i < createdObjects.Length; i++)
            {
                var ctx = model.MeshContextList[i];
                var go = createdObjects[i];
                if (ctx == null || go == null) continue;

                // WorldMatrixからposition/rotationを抽出
                Matrix4x4 world = ctx.WorldMatrix;
                Vector3 worldPos = world.GetColumn(3);
                Quaternion worldRot = world.rotation;
                Vector3 worldScale = world.lossyScale;

                // Transformに反映（ワールド座標で設定）
                go.transform.position = worldPos;
                go.transform.rotation = worldRot;
                // スケールはローカルで設定（ワールドスケールの直接設定はUnityではできない）
                // エクスポート時のスケールを維持するためここでは変更しない
            }
        }

        // ================================================================
        // tangent計算
        // ================================================================

        /// <summary>
        /// 指定キーフレームのinTangent/outTangentを計算する。
        ///
        /// ■ 変換式の導出（詳細はファイルヘッダのコメント参照）
        ///
        /// VMDベジェカーブは正規化空間(0〜1)で定義されている。
        /// AnimationCurveのtangentは実時間・実値空間(秒, マッスル値)。
        /// 正規化空間での傾きに (ΔValue/ΔTime) を掛けて実空間に変換する。
        ///
        /// ■ outTangent（現キーフレーム→次キーフレームの出発傾き）
        ///   正規化傾き = v1.y / v1.x
        ///   outTangent = (v1.y / v1.x) × (ΔValue / ΔTime)
        ///
        /// ■ inTangent（前キーフレーム→現キーフレームの到着傾き）
        ///   正規化傾き = (1-v2.y) / (1-v2.x)
        ///   inTangent = ((1-v2.y) / (1-v2.x)) × (ΔValue / ΔTime)
        ///   ※ ここでのv2は「現在のキーフレーム」のBoneFrameDataのベジェ制御点。
        ///     VMDでは補間カーブはキーフレームの「到着側」に紐付いている。
        ///
        /// ■ 注意
        ///   VMDの回転ベジェは1本（Curves[3]）で全軸共通。
        ///   マッスルは各軸独立なので、同じtangentスケールを全軸に適用する。
        ///   これは近似であり、軸ごとに異なるイージングが必要な場合は不正確になる。
        /// </summary>
        private static void ComputeTangents(
            int keyIndex,
            List<uint> frameNumbers,
            float fps,
            Func<int, float> getValue,
            Dictionary<uint, BezierCurveInfo> bezierPerFrame,
            out float inTangent,
            out float outTangent)
        {
            float currentValue = getValue(keyIndex);
            float currentTime = frameNumbers[keyIndex] / fps;

            // --- inTangent（前キーフレームからの到着） ---
            if (keyIndex > 0)
            {
                float prevValue = getValue(keyIndex - 1);
                float prevTime = frameNumbers[keyIndex - 1] / fps;
                float deltaTime = currentTime - prevTime;
                float deltaValue = currentValue - prevValue;

                // 現在のキーフレームのベジェ制御点v2を使用
                // （VMDでは補間カーブは「次のキーフレーム」に格納されている）
                if (bezierPerFrame.TryGetValue(frameNumbers[keyIndex], out var bezier)
                    && deltaTime > TangentEpsilon)
                {
                    float slope = ComputeEndSlope(bezier.V2);
                    inTangent = slope * (deltaValue / deltaTime);
                    inTangent = Mathf.Clamp(inTangent, -MaxTangent, MaxTangent);
                }
                else
                {
                    // ベジェ情報がない場合は線形
                    inTangent = (deltaTime > TangentEpsilon) ? deltaValue / deltaTime : 0f;
                }
            }
            else
            {
                // 最初のキーフレーム: inTangentは0
                inTangent = 0f;
            }

            // --- outTangent（次キーフレームへの出発） ---
            if (keyIndex < frameNumbers.Count - 1)
            {
                float nextValue = getValue(keyIndex + 1);
                float nextTime = frameNumbers[keyIndex + 1] / fps;
                float deltaTime = nextTime - currentTime;
                float deltaValue = nextValue - currentValue;

                // 次のキーフレームのベジェ制御点v1を使用
                if (bezierPerFrame.TryGetValue(frameNumbers[keyIndex + 1], out var bezier)
                    && deltaTime > TangentEpsilon)
                {
                    float slope = ComputeStartSlope(bezier.V1);
                    outTangent = slope * (deltaValue / deltaTime);
                    outTangent = Mathf.Clamp(outTangent, -MaxTangent, MaxTangent);
                }
                else
                {
                    outTangent = (deltaTime > TangentEpsilon) ? deltaValue / deltaTime : 0f;
                }
            }
            else
            {
                // 最後のキーフレーム: outTangentは0
                outTangent = 0f;
            }
        }

        /// <summary>
        /// ベジェ曲線の始点(u=0)での正規化傾きを計算する。
        ///
        /// 始点での接線方向 = 3(P1 - P0) = 3·v1
        /// 正規化傾き = dy/dx = v1.y / v1.x
        ///
        /// v1.x が極小の場合、カーブは始点で垂直に近い（急加速）。
        /// その場合は大きな傾きを返す。
        /// </summary>
        private static float ComputeStartSlope(Vector2 v1)
        {
            if (Mathf.Abs(v1.x) < TangentEpsilon)
            {
                // v1.xがほぼ0 → 始点で垂直に近い
                // v1.yの符号に応じて最大tangentを返す
                return v1.y >= 0 ? MaxTangent : -MaxTangent;
            }
            return v1.y / v1.x;
        }

        /// <summary>
        /// ベジェ曲線の終点(u=1)での正規化傾きを計算する。
        ///
        /// 終点での接線方向 = 3(P3 - P2) = 3·((1,1) - v2)
        /// 正規化傾き = dy/dx = (1-v2.y) / (1-v2.x)
        ///
        /// (1-v2.x) が極小の場合、カーブは終点で垂直に近い（急減速）。
        /// </summary>
        private static float ComputeEndSlope(Vector2 v2)
        {
            float dx = 1f - v2.x;
            float dy = 1f - v2.y;

            if (Mathf.Abs(dx) < TangentEpsilon)
            {
                return dy >= 0 ? MaxTangent : -MaxTangent;
            }
            return dy / dx;
        }

        // ================================================================
        // RootMotionカーブ
        // ================================================================

        /// <summary>
        /// RootMotion（bodyPosition/bodyRotation）のカーブを構築する。
        ///
        /// bodyPositionはHips位置のワールド座標を正規化したもの。
        /// bodyRotationはHips回転。
        /// これらにもVMDベジェのtangentを適用する。
        ///
        /// Hipsの位置にはVMDの位置ベジェ(Curves[0..2])が本来適用されるが、
        /// マッスル空間でのRootT/RootQはHumanPoseHandlerの内部変換結果であり、
        /// 元のX/Y/Z独立カーブとは直接対応しない。
        /// ここでは回転カーブ(Curves[3])のtangentを代用する（近似）。
        /// </summary>
        private static void BuildRootMotionCurves(
            AnimationClip clip,
            List<uint> frameNumbers,
            float fps,
            Dictionary<uint, BezierCurveInfo> bezierPerFrame,
            List<Vector3> positions,
            List<Quaternion> rotations,
            VMDData vmd)
        {
            // RootT.x/y/z
            string[] posProps = { "RootT.x", "RootT.y", "RootT.z" };
            for (int axis = 0; axis < 3; axis++)
            {
                int ax = axis;  // closure capture
                var keyframes = new List<Keyframe>();

                for (int ki = 0; ki < frameNumbers.Count; ki++)
                {
                    float time = frameNumbers[ki] / fps;
                    float value = axis == 0 ? positions[ki].x
                                : axis == 1 ? positions[ki].y
                                : positions[ki].z;

                    var kf = new Keyframe(time, value);

                    ComputeTangents(ki, frameNumbers, fps,
                        idx => ax == 0 ? positions[idx].x
                             : ax == 1 ? positions[idx].y
                             : positions[idx].z,
                        bezierPerFrame,
                        out float inTan, out float outTan);

                    kf.inTangent = inTan;
                    kf.outTangent = outTan;
                    keyframes.Add(kf);
                }

                if (keyframes.Count > 0)
                {
                    var curve = new AnimationCurve(keyframes.ToArray());
                    clip.SetCurve("", typeof(Animator), posProps[axis], curve);
                }
            }

            // RootQ.x/y/z/w
            string[] rotProps = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int comp = 0; comp < 4; comp++)
            {
                int c = comp;  // closure capture
                var keyframes = new List<Keyframe>();

                for (int ki = 0; ki < frameNumbers.Count; ki++)
                {
                    float time = frameNumbers[ki] / fps;
                    float value = comp == 0 ? rotations[ki].x
                                : comp == 1 ? rotations[ki].y
                                : comp == 2 ? rotations[ki].z
                                : rotations[ki].w;

                    var kf = new Keyframe(time, value);

                    ComputeTangents(ki, frameNumbers, fps,
                        idx => c == 0 ? rotations[idx].x
                             : c == 1 ? rotations[idx].y
                             : c == 2 ? rotations[idx].z
                             : rotations[idx].w,
                        bezierPerFrame,
                        out float inTan, out float outTan);

                    kf.inTangent = inTan;
                    kf.outTangent = outTan;
                    keyframes.Add(kf);
                }

                if (keyframes.Count > 0)
                {
                    var curve = new AnimationCurve(keyframes.ToArray());
                    clip.SetCurve("", typeof(Animator), rotProps[comp], curve);
                }
            }
        }

        // ================================================================
        // ベジェ情報収集
        // ================================================================

        /// <summary>
        /// 各フレーム番号に対応するVMDベジェ制御点を収集する。
        ///
        /// VMDのBoneFrameDataには補間カーブが格納されている。
        /// Curves[3]が回転補間用。同一フレームに複数ボーンのキーがある場合、
        /// 代表として最初に見つかったボーン（体幹ボーン優先）のカーブを使用する。
        ///
        /// この「代表1本」方式は、ボーンごとに異なるベジェカーブが設定されている
        /// VMDデータでは不正確になる。ただしHumanoidマッスルは全ボーンの合成結果
        /// なので、ボーン個別のカーブを正確に移植することは原理的にできない。
        /// </summary>
        private static Dictionary<uint, BezierCurveInfo> BuildBezierMap(
            VMDData vmd, List<uint> frameNumbers)
        {
            var result = new Dictionary<uint, BezierCurveInfo>();

            // 体幹ボーンを優先（これらのカーブが全体の動きに最も影響が大きい）
            var priorityBones = new HashSet<string>
            {
                "センター", "上半身", "上半身2", "下半身",
                "左足", "右足", "左ひざ", "右ひざ",
                "左腕", "右腕", "左ひじ", "右ひじ"
            };

            var frameSet = new HashSet<uint>(frameNumbers);

            // 優先ボーンから先にスキャン
            foreach (var frame in vmd.BoneFrameList)
            {
                if (!frameSet.Contains(frame.FrameNumber))
                    continue;

                if (!result.ContainsKey(frame.FrameNumber) && priorityBones.Contains(frame.BoneName))
                {
                    if (frame.Curves != null && frame.Curves.Length > 3)
                    {
                        result[frame.FrameNumber] = new BezierCurveInfo
                        {
                            V1 = frame.Curves[3].v1,
                            V2 = frame.Curves[3].v2
                        };
                    }
                }
            }

            // 残りのフレームを非優先ボーンで埋める
            foreach (var frame in vmd.BoneFrameList)
            {
                if (!frameSet.Contains(frame.FrameNumber))
                    continue;

                if (!result.ContainsKey(frame.FrameNumber))
                {
                    if (frame.Curves != null && frame.Curves.Length > 3)
                    {
                        result[frame.FrameNumber] = new BezierCurveInfo
                        {
                            V1 = frame.Curves[3].v1,
                            V2 = frame.Curves[3].v2
                        };
                    }
                }
            }

            return result;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>
        /// VMDの全ボーンキーフレーム番号をソート済みで返す
        /// </summary>
        private static List<uint> CollectSortedFrameNumbers(VMDData vmd)
        {
            var set = new HashSet<uint>();
            foreach (var frame in vmd.BoneFrameList)
                set.Add(frame.FrameNumber);

            var sorted = set.ToList();
            sorted.Sort();
            return sorted;
        }

        /// <summary>
        /// モデルのIKベイク対象ボーン名を収集
        /// </summary>
        private static HashSet<string> CollectIKBakedBoneNames(Model.ModelContext model)
        {
            var result = new HashSet<string>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx != null && ctx.IsIK && !string.IsNullOrEmpty(ctx.Name))
                    result.Add(ctx.Name);
            }
            return result;
        }

        /// <summary>
        /// キーフレームリストにゼロ以外の値が含まれるか判定
        /// 全フレームでゼロのマッスルはカーブ生成をスキップしてデータ量を削減する
        /// </summary>
        private static bool HasNonZeroValues(List<Keyframe> keyframes)
        {
            const float threshold = 0.0001f;
            foreach (var kf in keyframes)
            {
                if (Mathf.Abs(kf.value) > threshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// AnimationClipをアセットとして保存
        /// </summary>
        public static void SaveClip(AnimationClip clip, string path)
        {
            if (clip == null || string.IsNullOrEmpty(path))
                return;

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VMDToHumanoidClip] Saved: {path}");
        }

        // ================================================================
        // 内部型
        // ================================================================

        /// <summary>
        /// VMDベジェカーブの制御点情報（回転カーブ用）
        /// </summary>
        private struct BezierCurveInfo
        {
            public Vector2 V1;  // 始点側制御点
            public Vector2 V2;  // 終点側制御点
        }
    }
}
