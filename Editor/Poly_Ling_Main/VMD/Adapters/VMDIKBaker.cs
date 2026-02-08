// VMDIKBaker.cs
// VMDモーションのIKキーフレームをボーンキーフレームにベイクする
// IKボーンのキーフレーム位置でIK解決を行い、リンクボーンの最終回転をBoneFrameDataとして書き戻す
// IKボーンのベジェ補間カーブをリンクボーンのキーフレームにそのまま移植する

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        public static List<string> BakeIK(VMDData vmd, Model.ModelContext model, VMDApplier applier)
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

                    // 座標系逆変換
                    if (applier.ApplyCoordinateConversion)
                    {
                        vmdPos = CoordinateConverter.ToPMXPosition(vmdPos);
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
        private static List<IKBoneInfo> CollectIKBones(Model.ModelContext model)
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
