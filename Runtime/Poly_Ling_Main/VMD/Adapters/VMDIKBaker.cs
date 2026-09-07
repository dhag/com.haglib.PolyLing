// VMDIKBaker.cs
// VMDモーションのIKキーフレームをボーンキーフレームにベイクする
// IKボーンのキーフレーム位置でIK解決を行い、リンクボーンの最終回転をBoneFrameDataとして書き戻す
// IKボーンのベジェ補間カーブをリンクボーンのキーフレームにそのまま移植する

//
// ================================================================
// ■ 軸規約と IK の現状（2026-09-07 実測。恒久メモ）
// ----------------------------------------------------------------
//   【旧記述の取り消し】
//   ここには以前「軸規約の変更に未追随。動作保証対象外」と書かれていたが、
//   その前提はもう成り立たない。PMXImporter.CalculateBoneModelRotation は
//   削除済みで、ボーンのレスト回転は全ボーン恒等に固定されている
//   （PMXImporter.cs の boneModelRotations と、同ファイルの
//    「ローカル軸の合成は廃止した」節）。
//   したがって R^-1·Q·R（R = ctx.BoneModelRotation）は恒等の共役、
//   すなわち素通りであり、「局所軸が X か Y か」で結果が変わることはない。
//   BoneModelRotation を非恒等へ戻さないこと。戻すとこの前提が崩れる。
//
//   【IK の収束（実測）】
//   __AちゃんH.pmx ＋ 左足ＩＫ に移動キーを持つ VMD で vmd_summary.csv を採取した。
//     左足ＩＫ  distEnd 0.05208
//     右足ＩＫ  distEnd 0.00571 （VMD にキー無し＝レスト）
//     髪ＩＫ    distEnd 0.00063
//   左足の 0.05208 は収束不良ではない。目標が脚の届く範囲の外にある。
//     股関節→目標 0.82780 / 最大リーチ 0.77776（太もも 0.37646 ＋ すね 0.40131）
//     届かない量 0.05004 が残差とほぼ一致し、収束後の伸展率は 99.73%。
//   右足も同じ関係（超過 0.00385 / 残差 0.00571）。このモデルはレスト姿勢の時点で
//   足ＩＫ が脚長よりわずかに遠い。
//   よって CCDIKSolver は正しく動いている。角度制限を外しても脚は伸びない。
//
//   【取り消した記述】
//   旧記述の「足ＩＫが収束しないフレームがある（左足首 max dist 1.14 /
//   12 of 50 サンプル）」は別モーションでの記録で、上記の測定では再現しなかった。
//   再現条件は未特定。再発したら、まず 股関節→目標 の距離と脚長を比べること。
//   届かないだけの場合は不具合ではない。
//
//   【残る未対応】
//     - 付与親（GrantParentIndex / GrantRate）は未評価。PMX の読み書きと取込では
//       保持されるが、姿勢適用側に評価コードが無い。
//     - 統合経路には IK が無い（MotionClipApplier に Solve 呼び出しなし）。
//
//   【このファイルは使われていない】
//   VMD → VRMA の書き出しは VMDApplier.EnableIK でフレームごとに解いており、
//   VMD 自体を書き換える本ファイルの経路は通らない。
//   ベイク済み VMD が要るようになるまで、動作は未検証のままである。
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMD IKベイカー
    /// IKボーンのキーフレームを解決し、リンクボーンのキーフレームに変換する
    /// </summary>
    public static class VMDIKBaker
    {
        /// <summary>
        /// VMDデータ内のIKをベイクする
        /// </summary>
        /// <param name="vmd">ベイク対象のVMDデータ（直接書き換える）</param>
        /// <param name="model">IK構造を持つModelContext</param>
        /// <param name="applier">座標変換設定済みのVMDApplier</param>
        /// <returns>ベイクしたIKボーン名のリスト</returns>
        public static List<string> BakeIK(VMDData vmd, ModelContext model, VMDApplier applier)
        {
            if (vmd == null || model == null || applier == null)
                return new List<string>();

            // IKボーンを収集
            var ikBones = CollectIKBones(model);
            if (ikBones.Count == 0)
            {
                Debug.Log("[VMDIKBaker] No IK bones found in model");
                return new List<string>();
            }

            // IKボーン名セット
            var ikBoneNames = new HashSet<string>();
            foreach (var ikInfo in ikBones)
                ikBoneNames.Add(ikInfo.Name);

            // IKリンクボーンインデックスを収集（ベイク対象）
            var ikLinkBoneIndices = new HashSet<int>();
            foreach (var ikInfo in ikBones)
            {
                if (ikInfo.TargetIndex >= 0 && ikInfo.TargetIndex < model.MeshContextList.Count)
                    ikLinkBoneIndices.Add(ikInfo.TargetIndex);
                foreach (var link in ikInfo.LinkIndices)
                    ikLinkBoneIndices.Add(link);
            }

            // IKボーンのキーフレームを収集（フレーム番号順、補間カーブ付き）
            var ikKeyFrames = CollectIKKeyFrames(vmd, ikBoneNames);
            if (ikKeyFrames.Count == 0)
            {
                Debug.Log("[VMDIKBaker] No IK keyframes found in VMD");
                return new List<string>();
            }

            Debug.Log($"[VMDIKBaker] Baking {ikBones.Count} IK bones across {ikKeyFrames.Count} keyframes");

            // IKソルバー
            var ikSolver = new CCDIKSolver();

            // フレームごとにIK解決し、リンクボーンの回転を取得
            var bakedFrames = new Dictionary<string, List<BoneFrameData>>();

            foreach (var ikKeyFrame in ikKeyFrames)
            {
                uint frameNumber = ikKeyFrame.FrameNumber;

                // ボーンポーズ適用（IKなし）
                bool origEnableIK = applier.EnableIK;
                applier.EnableIK = false;
                applier.ApplyBonePose(model, vmd, frameNumber);
                applier.EnableIK = origEnableIK;

                // IK解決
                ikSolver.Solve(model);

                // リンクボーンの最終回転を取得
                foreach (int linkIndex in ikLinkBoneIndices)
                {
                    var ctx = model.MeshContextList[linkIndex];
                    if (ctx == null || ctx.BonePoseData == null)
                        continue;

                    string boneName = ctx.Name;
                    if (string.IsNullOrEmpty(boneName))
                        continue;

                    // BonePoseDataから合成済みデルタを取得（VMD + IK）
                    Vector3 deltaPos = Vector3.zero;
                    Quaternion deltaRot = Quaternion.identity;

                    foreach (var layer in ctx.BonePoseData.Layers)
                    {
                        if (!layer.Enabled || layer.Weight <= 0f)
                            continue;

                        float w = Mathf.Clamp01(layer.Weight);
                        deltaPos += layer.DeltaPosition * w;
                        Quaternion weightedDelta = Quaternion.Slerp(
                            Quaternion.identity, layer.DeltaRotation, w);
                        deltaRot = weightedDelta * deltaRot;
                    }

                    // ローカル軸空間からVMD空間に逆変換
                    // ApplyBonePoseで Q' = R^-1 * Q * R を行っているので
                    // 逆変換は Q_vmd = R * Q' * R^-1
                    Quaternion modelRot = ctx.BoneModelRotation;
                    Quaternion vmdRot = deltaRot;
                    if (modelRot != Quaternion.identity)
                    {
                        vmdRot = modelRot * deltaRot * Quaternion.Inverse(modelRot);
                    }

                    // 位置のスケール逆変換
                    Vector3 vmdPos = deltaPos;
                    if (!Mathf.Approximately(applier.PositionScale, 0f) &&
                        !Mathf.Approximately(applier.PositionScale, 1f))
                    {
                        vmdPos /= applier.PositionScale;
                    }

                    // 座標系逆変換（順方向 ApplyBonePose と対称。位置・回転の両方に掛ける）
                    //   AxisFlipOps.Position / Rotation はいずれも自己逆元のため、
                    //   順方向と同じ AxisFlip をそのまま適用すれば逆変換になる。
                    if (applier.ApplyCoordinateConversion)
                    {
                        vmdPos = CoordinateConverter.ToPMXPosition(vmdPos, applier.CoordinateFlip);
                        vmdRot = CoordinateConverter.ToPMXRotation(vmdRot, applier.CoordinateFlip);
                    }

                    // BoneFrameDataを作成し、IKボーンの補間カーブを移植
                    var bakedFrame = new BoneFrameData(boneName, frameNumber, vmdPos, vmdRot);
                    CopyInterpolation(ikKeyFrame.SourceFrame, bakedFrame);

                    if (!bakedFrames.ContainsKey(boneName))
                        bakedFrames[boneName] = new List<BoneFrameData>();

                    bakedFrames[boneName].Add(bakedFrame);
                }
            }

            // VMDデータを書き換え
            // 1. IKボーン名のキーフレームを削除
            vmd.BoneFrameList.RemoveAll(f => ikBoneNames.Contains(f.BoneName));

            // 2. ベイク結果でリンクボーンのキーフレームを置換
            foreach (var kvp in bakedFrames)
            {
                string boneName = kvp.Key;
                var frames = kvp.Value;

                // 既存のキーフレームを削除
                vmd.BoneFrameList.RemoveAll(f => f.BoneName == boneName);

                // ベイク結果を追加
                vmd.BoneFrameList.AddRange(frames);
            }

            // 3. 辞書を再構築
            RebuildDictionaries(vmd);

            var bakedNames = ikBones.Select(b => b.Name).ToList();
            Debug.Log($"[VMDIKBaker] Bake complete. " +
                      $"Baked IK bones: {string.Join(", ", bakedNames)}, " +
                      $"Affected link bones: {bakedFrames.Count}");

            return bakedNames;

        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>
        /// ModelContextからIKボーン情報を収集
        /// </summary>
        private static List<IKBoneInfo> CollectIKBones(ModelContext model)
        {
            var result = new List<IKBoneInfo>();

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null || !ctx.IsIK || ctx.IKLinks == null || ctx.IKLinks.Count == 0)
                    continue;

                var info = new IKBoneInfo
                {
                    Index = i,
                    Name = ctx.Name,
                    TargetIndex = ctx.IKTargetIndex,
                    LinkIndices = ctx.IKLinks.Select(l => l.BoneIndex).ToList()
                };
                result.Add(info);
            }

            return result;
        }

        /// <summary>
        /// IKボーンのキーフレームをフレーム番号順に収集する
        /// 同一フレームに複数IKボーンのキーがある場合は最初に見つかったもののカーブを使用
        /// </summary>
        private static List<IKKeyFrameEntry> CollectIKKeyFrames(VMDData vmd, HashSet<string> ikBoneNames)
        {
            var frameMap = new SortedDictionary<uint, BoneFrameData>();

            foreach (var frame in vmd.BoneFrameList)
            {
                if (!ikBoneNames.Contains(frame.BoneName))
                    continue;

                if (!frameMap.ContainsKey(frame.FrameNumber))
                {
                    frameMap[frame.FrameNumber] = frame;
                }
            }

            var result = new List<IKKeyFrameEntry>();
            foreach (var kvp in frameMap)
            {
                result.Add(new IKKeyFrameEntry
                {
                    FrameNumber = kvp.Key,
                    SourceFrame = kvp.Value
                });
            }

            return result;
        }

        /// <summary>
        /// BoneFrameDataの補間カーブ（Curves + Interpolation）をコピーする
        /// </summary>
        private static void CopyInterpolation(BoneFrameData source, BoneFrameData dest)
        {
            if (source.Curves != null && dest.Curves != null)
            {
                for (int i = 0; i < 4 && i < source.Curves.Length && i < dest.Curves.Length; i++)
                {
                    dest.Curves[i] = new BezierCurve(source.Curves[i].v1, source.Curves[i].v2);
                }
            }

            if (source.Interpolation != null && dest.Interpolation != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            dest.Interpolation[i][j][k] = source.Interpolation[i][j][k];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// VMDDataの辞書を再構築する
        /// </summary>
        private static void RebuildDictionaries(VMDData vmd)
        {
            vmd.BoneFramesByName.Clear();
            foreach (var frame in vmd.BoneFrameList)
            {
                if (!vmd.BoneFramesByName.ContainsKey(frame.BoneName))
                {
                    vmd.BoneFramesByName[frame.BoneName] = new List<BoneFrameData>();
                }
                vmd.BoneFramesByName[frame.BoneName].Add(frame);
            }

            foreach (var list in vmd.BoneFramesByName.Values)
            {
                list.Sort();
            }
        }

        /// <summary>IKボーン情報</summary>
        private class IKBoneInfo
        {
            public int Index;
            public string Name;
            public int TargetIndex;
            public List<int> LinkIndices;
        }

        /// <summary>IKキーフレームエントリ（フレーム番号＋補間カーブ元のBoneFrameData）</summary>
        private class IKKeyFrameEntry
        {
            public uint FrameNumber;
            public BoneFrameData SourceFrame;
        }
    }
}
