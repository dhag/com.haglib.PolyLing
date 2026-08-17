// VMDApplier.cs
// VMDモーションをPolyLingのModelContextに適用するアダプタ
// ボーンポーズの適用、モーフウェイトの適用、座標系変換を担当

//
// ================================================================
// ■ 【重要】軸規約の変更に未追随。動作保証対象外。
// ----------------------------------------------------------------
//   PMX 取込のボーン局所軸は以下に変更された（PMXImporter.CalculateBoneModelRotation）:
//     (1) AxisFlipOps.Basis を両側共役 S·M·S から片側変換 S·M へ
//     (2) MMD 規約（X = 骨の長手方向）から DCC 標準（Y = 骨の長手方向）へ巡回置換
//   本ファイルの R^-1·Q·R 変換（R = ctx.BoneModelRotation）はこの変更に
//   追随していないため、結果は正しくない。
//
//   既知の症状:
//     - 足ＩＫが収束しないフレームがある（左足首 max dist 1.14 / 12 of 50 サンプル）
//     - 統合経路には IK 自体が無い（MotionClipApplier に Solve 呼び出しなし）
//     - 付与親（GrantParentIndex / GrantRate）が未評価
//
//   修正する場合は、局所軸が「Y = 骨方向」であることを前提に組み直すこと。
//   旧規約を前提とした つじつま合わせ を足さないこと。
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

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
        private ModelContext _mappedModel;

        /// <summary>
        /// 座標変換を適用するかどうか
        /// </summary>
        public bool ApplyCoordinateConversion { get; set; } = false;

        /// <summary>
        /// 座標変換に使う軸反転。既定は PMX ⇔ Unity（X・Z 両反転 = Y軸180°回転）。
        /// インポート設定と揃えるため、呼び出し側から差し替えられるようにしている。
        /// </summary>
        public AxisFlip CoordinateFlip { get; set; } = AxisFlip.PmxToUnity;

        /// <summary>
        /// VMDデルタ位置に適用するスケール（PMX空間→Unity空間）
        /// EditorStateContext.PmxUnityRatioと同じ値を設定する
        /// </summary>
        public float PositionScale { get; set; } = 1f;

        /// <summary>
        /// デバッグログの有効/無効。ApplyFrame で IK ソルバへも伝播する。
        /// </summary>
        public bool DebugLog { get; set; } = true;

        // ================================================================
        // トレース出力（vmd_trace.csv）
        // ================================================================

        /// <summary>
        /// フレーム単位トレースの出力有無。既定 OFF。
        /// ON にすると ApplyBonePose のたびに TraceDirectory/vmd_trace.csv へ追記する。
        /// </summary>
        public bool TraceEnabled { get; set; } = false;

        /// <summary>
        /// トレース CSV の出力フォルダ。呼び出し側（パネル）が VMD ファイルのフォルダを渡す。
        /// 未設定のときはトレースを出さない。
        /// </summary>
        public string TraceDirectory { get; set; }

        /// <summary>トレース CSV のファイル名</summary>
        public const string TraceFileName = "vmd_trace.csv";

        /// <summary>
        /// トレース対象ボーン名。既定は脚まわり 8 本。
        /// </summary>
        public HashSet<string> TraceBoneNames { get; set; } = new HashSet<string>
        {
            "センター", "下半身", "左足", "左ひざ", "左足首", "右足", "右ひざ", "右足首"
        };

        private VMDTraceWriter _trace;

        /// <summary>
        /// IK の角度制限を無視する。既定 false（VMD 復活手順書 段階 3）。
        /// CCDIKSolver へ中継する。
        /// </summary>
        public bool IgnoreAngleLimits { get; set; } = false;

        /// <summary>
        /// IK 解決前にひざへ微小曲げを付与する。既定 false。CCDIKSolver へ中継する。
        /// </summary>
        public bool KneePreBend { get; set; } = false;

        /// <summary>
        /// IK トレース対象の IK ボーン名。空なら全 IK ボーン。CCDIKSolver へ中継する。
        /// </summary>
        public HashSet<string> TraceIkBoneNames { get; set; } = new HashSet<string>();

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
        public void BuildMapping(ModelContext model)
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

            if (DebugLog)
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
        public void ApplyBonePose(ModelContext model, VMDData vmd, float frameNumber)
        {
            if (model == null || vmd == null) return;

            // マッピングが未構築または別モデルなら再構築
            if (_mappedModel != model)
            {
                BuildMapping(model);
            }

            // トレース出力の準備（TraceEnabled かつ出力先が有効なときだけ開く）
            bool trace = TraceEnabled && EnsureTraceWriter();

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

                // トレース対象か（stage ごとに src → cnv の 1 行を出す）
                bool isTraceBone = trace && TraceBoneNames != null && TraceBoneNames.Contains(boneName);
                if (isTraceBone)
                    _trace.Add(frameNumber, boneName, "vmd_raw", position, rotation, position, rotation);

                // 座標系変換
                //
                // ■ 位置・回転の両方に同じ軸反転 S を掛けること（片方だけは不可）。
                //   取込時に BoneModelRotation は R_unity = S·R_pmx·S へ変換済み
                //   （PMXImporter.CalculateBoneModelRotation → AxisFlipOps.Basis）。
                //   後段の Q' = R^-1·Q·R は Q が Unity 空間であることを前提とするため、
                //   VMD 生値（PMX 空間）のままでは回転軸の X・Z 成分の符号が反転し、
                //   ひじ等がねじれる。
                Vector3 flipPos = position;
                Quaternion flipRot = rotation;
                if (ApplyCoordinateConversion)
                {
                    flipPos = CoordinateConverter.ToUnityPosition(position, CoordinateFlip);
                    flipRot = CoordinateConverter.ToUnityRotation(rotation, CoordinateFlip);
                }

                if (isTraceBone)
                    _trace.Add(frameNumber, boneName, "after_flip", position, rotation, flipPos, flipRot);

                // スケール適用（VMDデルタ位置はPMX空間の値なので、Unity空間に合わせる）
                Vector3 scaledPos = flipPos;
                Quaternion scaledRot = flipRot;
                if (!Mathf.Approximately(PositionScale, 1f))
                {
                    scaledPos = flipPos * PositionScale;
                }

                if (isTraceBone)
                    _trace.Add(frameNumber, boneName, "after_scale", flipPos, flipRot, scaledPos, scaledRot);

                Vector3 convertedPos = scaledPos;
                Quaternion convertedRot = scaledRot;

                // ★★★ ローカル軸空間変換 ★★★
                //
                // ■ 背景
                // 元ライブラリ(NCSHAGLIB BoneMatrixList)ではボーン行列を以下で計算していた:
                //   boneMatrix = invBindpose * TRS(vmdTrans, vmdRot) * bindpose
                //
                // ※ 旧コメントには「bindposeは平行移動のみでローカル軸回転を含まない」と
                //   書かれていたが誤り。MMD/PMX はボーンの長手方向を局所 X 軸として扱う
                //   規約を持つ（他ツールが局所 Y 軸と呼ぶものに相当）。訂正する。
                //
                // ■ Unity移植での構造変更
                // UnityではTransform階層がローカル基準のため、以下の構造になった:
                //   BoneTransform.TransformMatrix = TRS(ローカル位置, ローカル軸回転R_local)
                //   BonePoseData.LocalMatrix       = TRS(vmdPos, convertedRot)
                //   MeshContext.LocalMatrix         = BoneTransform × BonePoseData
                // BoneTransformにローカル軸回転(R_local)が入っているため、
                // VMDのQuaternion(Q)をそのまま使うと回転空間がずれる。
                //
                // ■ 解決策
                // 各ボーンのモデル空間でのローカル軸回転(R)を使い、
                // VMD回転をローカル軸空間に座標変換する:
                //   Q' = R^-1 * Q * R
                // Rは MeshContext.BoneModelRotation に保存されている。
                // これはPMXImporterのCalculateBoneModelRotationの戻り値（ワールド累積回転）。
                //
                // ■ 注意: BoneTransform.RotationQuaternionは使えない
                // BoneTransformの回転は親からの相対回転 = Inverse(親R) * R であり、
                // ワールド空間でのローカル軸回転とは異なる。
                // 相対回転で変換すると親ボーンの回転成分が抜け落ち、誤差が生じる。
                //
                Quaternion modelRot = ctx.BoneModelRotation;
                if (modelRot != Quaternion.identity)
                {
                    // 回転: Q' = R^-1 * Q * R
                    convertedRot = Quaternion.Inverse(modelRot) * convertedRot * modelRot;
                    // 位置: P' = R^-1 * P （グローバル空間のデルタをローカル軸空間に変換）
                    convertedPos = Quaternion.Inverse(modelRot) * convertedPos;
                }

                // BoneModelRotation が恒等で素通りした場合も行は出す（after_scale と同値になる）
                if (isTraceBone)
                    _trace.Add(frameNumber, boneName, "after_rest", scaledPos, scaledRot, convertedPos, convertedRot);

                // BonePoseDataの"VMD"レイヤーにデルタを設定
                ctx.BonePoseData.SetLayer("VMD", convertedPos, convertedRot);

                // applied は他レイヤーも含めた合成結果
                if (isTraceBone)
                    _trace.Add(frameNumber, boneName, "applied", convertedPos, convertedRot,
                               ctx.BonePoseData.Position, ctx.BonePoseData.Rotation);
            }

            // ワールド行列を再計算
            model.ComputeWorldMatrices();

            // world 列は再計算後でないと埋まらないので、ここでフラッシュする
            if (trace)
                _trace.FlushFrame(name => GetBoneWorldPosition(model, name));
        }

        /// <summary>
        /// 指定フレームのモーフウェイトをModelContextに適用
        /// </summary>
        public void ApplyMorphWeights(ModelContext model, VMDData vmd, float frameNumber)
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
        public void ApplyFrame(ModelContext model, VMDData vmd, float frameNumber)
        {
            ApplyBonePose(model, vmd, frameNumber);
            ApplyMorphWeights(model, vmd, frameNumber);

            // IK解決
            // ApplyBonePose内でComputeWorldMatrices()が呼ばれた後、
            // VMD適用済みのWorldMatrixを入力としてIKを解く。
            // CCDIKSolverはWorldMatrixを直接操作して結果を反映する
            // （BonePoseDataレイヤーは使わない）。
            if (EnableIK)
            {
                _ikSolver.DebugEnabled = DebugLog;
                PushIkSettings();
                _ikSolver.Solve(model, frameNumber);
            }
        }

        /// <summary>IK ソルバへ角度制限フラグとトレース設定を渡す。</summary>
        private void PushIkSettings()
        {
            _ikSolver.IgnoreAngleLimits = IgnoreAngleLimits;
            _ikSolver.KneePreBend       = KneePreBend;
            _ikSolver.TraceEnabled      = TraceEnabled;
            _ikSolver.TraceDirectory    = TraceDirectory;
            _ikSolver.TraceIkBoneNames  = TraceIkBoneNames;
        }

        /// <summary>IK有効フラグ。既定 OFF（VMD 復活手順書 段階 1）。</summary>
        public bool EnableIK { get; set; } = false;
        /// <summary>IKソルバー</summary>
        private CCDIKSolver _ikSolver = new CCDIKSolver();

        // ================================================================
        // トレース
        // ================================================================

        /// <summary>
        /// 指定範囲を通しで適用し、トレース CSV を出力する。
        /// 呼び出しのたびにファイルを新規作成する（追記ではない）。
        /// </summary>
        public void TraceAllFrames(ModelContext model, VMDData vmd, int fromFrame, int toFrame)
        {
            if (model == null || vmd == null) return;
            if (string.IsNullOrEmpty(TraceDirectory)) return;

            bool prevTrace = TraceEnabled;
            CloseTrace();          // 前回のストリームを閉じ、新規作成させる
            TraceEnabled = true;

            try
            {
                for (int f = fromFrame; f <= toFrame; f++)
                {
                    ApplyBonePose(model, vmd, f);   // 内部で 1 フレーム分をフラッシュする
                    if (EnableIK)
                    {
                        _ikSolver.DebugEnabled = false;   // 全フレーム分の Console 出力は出さない
                        PushIkSettings();
                        _ikSolver.Solve(model, f);
                    }
                }
            }
            finally
            {
                CloseTrace();
                TraceEnabled = prevTrace;
            }
        }

        /// <summary>トレース CSV（vmd_trace.csv / vmd_ik.csv / vmd_summary.csv）を閉じる。</summary>
        public void CloseTrace()
        {
            _trace?.Close();
            _ikSolver?.CloseTrace();
        }

        /// <summary>出力先が有効ならライタを開く。失敗したらトレースを止める。</summary>
        private bool EnsureTraceWriter()
        {
            if (_trace != null && _trace.IsOpen) return true;
            if (string.IsNullOrEmpty(TraceDirectory)) return false;

            try
            {
                if (_trace == null) _trace = new VMDTraceWriter();
                _trace.Open(System.IO.Path.Combine(TraceDirectory, TraceFileName));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VMDApplier] トレース出力を開けません: {ex.Message}");
                TraceEnabled = false;
                return false;
            }
        }

        /// <summary>ボーン名からワールド位置を引く（ComputeWorldMatrices 後に呼ぶこと）。</summary>
        private Vector3 GetBoneWorldPosition(ModelContext model, string boneName)
        {
            if (model == null) return Vector3.zero;
            if (!_boneNameToIndex.TryGetValue(boneName, out int index)) return Vector3.zero;
            var ctx = model.MeshContextList[index];
            if (ctx == null) return Vector3.zero;
            return ctx.WorldMatrix.GetColumn(3);
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
        public void ResetAllBones(ModelContext model)
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