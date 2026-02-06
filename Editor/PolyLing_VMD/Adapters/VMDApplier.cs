// VMDApplier.cs
// VMDモーションをPolyLingのModelContextに適用するアダプタ
// ボーンポーズの適用、モーフウェイトの適用、座標系変換を担当

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDモーションをPolyLing ModelContextに適用するアダプタ
    /// </summary>
    public class VMDApplier
    {
        // ================================================================
        // ボーン名マッピング
        // ================================================================

        /// <summary>
        /// ボーン名マッピング（VMDボーン名 → ModelContext内インデックス）
        /// </summary>
        private Dictionary<string, int> _boneNameToIndex = new Dictionary<string, int>();

        /// <summary>
        /// モーフ名マッピング（VMDモーフ名 → ModelContext内インデックス）
        /// </summary>
        private Dictionary<string, int> _morphNameToIndex = new Dictionary<string, int>();

        /// <summary>
        /// マッピング済みのModelContext
        /// </summary>
        private Model.ModelContext _mappedModel;

        /// <summary>
        /// 座標変換を適用するかどうか
        /// </summary>
        public bool ApplyCoordinateConversion { get; set; } = true;

        /// <summary>
        /// 未マッチのボーン名リスト（デバッグ用）
        /// </summary>
        public List<string> UnmatchedBoneNames { get; private set; } = new List<string>();

        /// <summary>
        /// 未マッチのモーフ名リスト（デバッグ用）
        /// </summary>
        public List<string> UnmatchedMorphNames { get; private set; } = new List<string>();

        // ================================================================
        // 初期化・マッピング
        // ================================================================

        /// <summary>
        /// ModelContextのボーン構造をスキャンしてマッピングを構築
        /// </summary>
        public void BuildMapping(Model.ModelContext model)
        {
            if (model == null) return;

            _mappedModel = model;
            _boneNameToIndex.Clear();
            _morphNameToIndex.Clear();

            // ボーンをスキャン
            foreach (var entry in model.Bones)
            {
                var ctx = model.MeshContextList[entry.MasterIndex];
                if (!string.IsNullOrEmpty(ctx.Name))
                {
                    // 重複チェック（同名ボーンがある場合は最初のものを使用）
                    if (!_boneNameToIndex.ContainsKey(ctx.Name))
                    {
                        _boneNameToIndex[ctx.Name] = entry.MasterIndex;
                    }
                }
            }

            // モーフをスキャン
            foreach (var entry in model.Morphs)
            {
                var ctx = model.MeshContextList[entry.MasterIndex];
                if (!string.IsNullOrEmpty(ctx.Name))
                {
                    if (!_morphNameToIndex.ContainsKey(ctx.Name))
                    {
                        _morphNameToIndex[ctx.Name] = entry.MasterIndex;
                    }
                }
            }

            Debug.Log($"[VMDApplier] Mapped {_boneNameToIndex.Count} bones, {_morphNameToIndex.Count} morphs");
        }

        /// <summary>
        /// VMDとModelContext間のマッチング状況を診断
        /// </summary>
        public VMDMatchingReport DiagnoseMatching(VMDData vmd)
        {
            var report = new VMDMatchingReport();

            if (vmd == null || _mappedModel == null)
            {
                report.IsValid = false;
                return report;
            }

            UnmatchedBoneNames.Clear();
            UnmatchedMorphNames.Clear();

            // ボーンマッチング
            foreach (var boneName in vmd.BoneNames)
            {
                if (_boneNameToIndex.ContainsKey(boneName))
                {
                    report.MatchedBones.Add(boneName);
                }
                else
                {
                    report.UnmatchedVMDBones.Add(boneName);
                    UnmatchedBoneNames.Add(boneName);
                }
            }

            // モデル側の未使用ボーン
            foreach (var boneName in _boneNameToIndex.Keys)
            {
                if (!vmd.BoneFramesByName.ContainsKey(boneName))
                {
                    report.UnusedModelBones.Add(boneName);
                }
            }

            // モーフマッチング
            foreach (var morphName in vmd.MorphNames)
            {
                if (_morphNameToIndex.ContainsKey(morphName))
                {
                    report.MatchedMorphs.Add(morphName);
                }
                else
                {
                    report.UnmatchedVMDMorphs.Add(morphName);
                    UnmatchedMorphNames.Add(morphName);
                }
            }

            report.IsValid = true;
            return report;
        }

        // ================================================================
        // ポーズ適用
        // ================================================================

        /// <summary>
        /// 指定フレームのボーンポーズをModelContextに適用
        /// BonePoseDataの"VMD"レイヤーにデルタを設定する
        /// </summary>
        public void ApplyBonePose(Model.ModelContext model, VMDData vmd, float frameNumber)
        {
            if (model == null || vmd == null) return;

            // マッピングが未構築または別モデルなら再構築
            if (_mappedModel != model)
            {
                BuildMapping(model);
            }

            // デバッグ対象ボーン
            var debugBones = new HashSet<string> { "右ひじ", "左ひじ", "右腕", "左腕", "右手首", "左手首" };

            // 各ボーンにポーズを適用
            foreach (var boneName in vmd.BoneNames)
            {
                if (!_boneNameToIndex.TryGetValue(boneName, out int boneIndex))
                    continue;

                var ctx = model.MeshContextList[boneIndex];
                if (ctx == null)
                    continue;

                // BonePoseDataがなければ初期化
                // BonePoseDataがなければIdentityで初期化
                // デルタのみレイヤーで管理。ベースはBoneTransformが担う
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new Data.BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }

                // VMDからポーズを取得（これはデルタ値）
                var (position, rotation) = vmd.GetBonePoseAtFrame(boneName, frameNumber);

                // デバッグ出力（変換前）
                bool isDebugBone = debugBones.Contains(boneName);
                if (isDebugBone)
                {
                    Debug.Log($"[VMD DEBUG] ===== {boneName} (frame {frameNumber}) =====");
                    Debug.Log($"[VMD DEBUG] VMD raw: pos={position}, rot={rotation}, euler={rotation.eulerAngles}");
                }

                // 座標系変換
                Vector3 convertedPos = position;
                Quaternion convertedRot = rotation;
                if (ApplyCoordinateConversion)
                {
                    convertedPos = CoordinateConverter.ToUnityPosition(position);
                }

                // ★★★ ローカル軸空間変換 - 削除禁止 ★★★
                // 元ライブラリ(NCSHAGLIB BoneMatrixList)ではBoneTransformにローカル軸回転が
                // 含まれず、最終回転 = vmdRot だった。
                // Unity移植ではBoneTransformにローカル軸回転が入っているため、
                // VMD回転(Q)をローカル軸空間に変換する必要がある。
                // Rはモデル空間でのローカル軸回転（ワールド累積）。
                // BoneTransform.RotationQuaternionは親からの相対回転なので使えない。
                // 変換: Q' = R^-1 * Q * R
                Quaternion modelRot = ctx.BoneModelRotation;
                if (modelRot != Quaternion.identity)
                {
                    convertedRot = Quaternion.Inverse(modelRot) * convertedRot * modelRot;
                }

                if (isDebugBone)
                {
                    Debug.Log($"[VMD DEBUG] After convert: pos={convertedPos}, rot={convertedRot}, euler={convertedRot.eulerAngles}");

                    // BoneTransformの情報
                    var bt = ctx.BoneTransform;
                    if (bt != null)
                    {
                        Debug.Log($"[VMD DEBUG] BoneTransform: pos={bt.Position}, rot={bt.Rotation}, useLocal={bt.UseLocalTransform}");
                        Debug.Log($"[VMD DEBUG] BoneTransform.TransformMatrix:\n{bt.TransformMatrix}");
                    }

                    // BonePoseDataのRestPose
                    var bpd = ctx.BonePoseData;
                    Debug.Log($"[VMD DEBUG] BonePoseData.RestPos={bpd.RestPosition}, RestRot={bpd.RestRotation.eulerAngles}");
                }

                // BonePoseDataの"VMD"レイヤーにデルタを設定
                ctx.BonePoseData.SetLayer("VMD", convertedPos, convertedRot);

                if (isDebugBone)
                {
                    // 設定後の合成結果
                    Debug.Log($"[VMD DEBUG] After SetLayer: Position={ctx.BonePoseData.Position}, Rotation={ctx.BonePoseData.Rotation.eulerAngles}");
                    Debug.Log($"[VMD DEBUG] BonePoseData.LocalMatrix:\n{ctx.BonePoseData.LocalMatrix}");
                    Debug.Log($"[VMD DEBUG] MeshContext.LocalMatrix:\n{ctx.LocalMatrix}");
                }
            }

            // ワールド行列を再計算
            model.ComputeWorldMatrices();
        }

        /// <summary>
        /// 指定フレームのモーフウェイトをModelContextに適用
        /// </summary>
        public void ApplyMorphWeights(Model.ModelContext model, VMDData vmd, float frameNumber)
        {
            if (model == null || vmd == null) return;

            if (_mappedModel != model)
            {
                BuildMapping(model);
            }

            foreach (var morphName in vmd.MorphNames)
            {
                if (!_morphNameToIndex.TryGetValue(morphName, out int morphIndex))
                    continue;

                var ctx = model.MeshContextList[morphIndex];
                if (ctx == null)
                    continue;

                float weight = vmd.GetMorphWeightAtFrame(morphName, frameNumber);

                // モーフウェイトを適用（MeshContext.MorphWeightプロパティがある場合）
                // 現在のPolyLing構造では頂点モーフの適用方法を確認する必要がある
                // TODO: 実際のモーフ適用ロジックを実装
                ApplyMorphWeight(ctx, weight);
            }
        }

        /// <summary>
        /// ボーンとモーフの両方を適用
        /// </summary>
        public void ApplyFrame(Model.ModelContext model, VMDData vmd, float frameNumber)
        {
            ApplyBonePose(model, vmd, frameNumber);
            ApplyMorphWeights(model, vmd, frameNumber);
        }

        // ================================================================
        // モーフ適用
        // ================================================================

        /// <summary>
        /// モーフウェイトを適用（内部実装）
        /// </summary>
        private void ApplyMorphWeight(Data.MeshContext ctx, float weight)
        {
            // TODO: PolyLingのモーフシステムに合わせて実装
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>
        /// ボーンインデックスを取得
        /// </summary>
        public int GetBoneIndex(string boneName)
        {
            return _boneNameToIndex.TryGetValue(boneName, out int index) ? index : -1;
        }

        /// <summary>
        /// モーフインデックスを取得
        /// </summary>
        public int GetMorphIndex(string morphName)
        {
            return _morphNameToIndex.TryGetValue(morphName, out int index) ? index : -1;
        }

        /// <summary>
        /// マッピング済みボーン数
        /// </summary>
        public int MappedBoneCount => _boneNameToIndex.Count;

        /// <summary>
        /// マッピング済みモーフ数
        /// </summary>
        public int MappedMorphCount => _morphNameToIndex.Count;

        /// <summary>
        /// すべてのボーンをリセット（VMDレイヤーをクリア）
        /// </summary>
        public void ResetAllBones(Model.ModelContext model)
        {
            if (model == null) return;

            foreach (var entry in model.Bones)
            {
                var ctx = model.MeshContextList[entry.MasterIndex];
                if (ctx?.BonePoseData != null)
                {
                    // VMDレイヤーのみクリア（Manual等は残す）
                    ctx.BonePoseData.ClearLayer("VMD");
                }
            }

            model.ComputeWorldMatrices();
        }
    }

    // ================================================================
    // マッチングレポート
    // ================================================================

    /// <summary>
    /// VMDとモデル間のマッチング診断結果
    /// </summary>
    public class VMDMatchingReport
    {
        public bool IsValid { get; set; }

        /// <summary>マッチしたボーン名</summary>
        public List<string> MatchedBones { get; } = new List<string>();

        /// <summary>VMDにあるがモデルにないボーン</summary>
        public List<string> UnmatchedVMDBones { get; } = new List<string>();

        /// <summary>モデルにあるがVMDにないボーン</summary>
        public List<string> UnusedModelBones { get; } = new List<string>();

        /// <summary>マッチしたモーフ名</summary>
        public List<string> MatchedMorphs { get; } = new List<string>();

        /// <summary>VMDにあるがモデルにないモーフ</summary>
        public List<string> UnmatchedVMDMorphs { get; } = new List<string>();

        /// <summary>ボーンマッチ率</summary>
        public float BoneMatchRate =>
            (MatchedBones.Count + UnmatchedVMDBones.Count) > 0
                ? (float)MatchedBones.Count / (MatchedBones.Count + UnmatchedVMDBones.Count)
                : 0f;

        /// <summary>モーフマッチ率</summary>
        public float MorphMatchRate =>
            (MatchedMorphs.Count + UnmatchedVMDMorphs.Count) > 0
                ? (float)MatchedMorphs.Count / (MatchedMorphs.Count + UnmatchedVMDMorphs.Count)
                : 0f;

        /// <summary>レポートを文字列で出力</summary>
        public override string ToString()
        {
            return $"VMD Matching Report:\n" +
                   $"  Bones: {MatchedBones.Count} matched, {UnmatchedVMDBones.Count} unmatched ({BoneMatchRate:P0})\n" +
                   $"  Morphs: {MatchedMorphs.Count} matched, {UnmatchedVMDMorphs.Count} unmatched ({MorphMatchRate:P0})";
        }
    }
}