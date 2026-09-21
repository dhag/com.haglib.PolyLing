// PanelCommand.Bone.cs
// BonePose・BoneTransform・Tポーズ変換・Humanoid 割当・マッスル可動域・Avatar リターゲットの操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // BonePose
    // ================================================================

    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.Targets, Description = "指定ボーンのポーズ層を作り直して初期状態にする。")]
    public class InitBonePoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public InitBonePoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.Targets, Description = "指定ボーンのポーズ層を有効・無効にする。")]
    public class SetBonePoseActiveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BonePoseActive",
                 Description = "ボーンポーズを有効にする", Required = true)]
        public bool Active { get; }
        public SetBonePoseActiveCommand(int modelIndex, int[] masterIndices, bool active)
            : base(modelIndex) { MasterIndices = masterIndices; Active = active; }
    }

    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.Targets, Description = "指定ボーンのポーズ層をすべて空にする。姿勢は既定へ戻る。")]
    public class ResetBonePoseLayersCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public ResetBonePoseLayersCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    [PLCommand(Category = "rig.skeleton.pose", Effects = PLCommandEffect.Pose | PLCommandEffect.BindPose, Hazards = PLCommandHazard.ChangesBindPose, Verification = PLCommandVerification.SkinWeights | PLCommandVerification.Visual, Writes = PLWriteScope.Targets, Description = "今のポーズをバインドポーズへ焼き込み、ポーズ層を空にする。")]
    public class BakePoseToBindPoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public BakePoseToBindPoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    // ================================================================
    // BoneTransform（簡易モード用）
    // ================================================================

    /// <summary>BoneTransform の Position/Rotation/Scale 単一軸値変更</summary>
    [PLCommand(Category = "rig.skeleton.pose", Effects = PLCommandEffect.Skeleton, Verification = PLCommandVerification.Visual, Writes = PLWriteScope.Targets, Description = "BoneTransform の Position / Rotation / Scale の 1 軸だけを変える。")]
    public class SetBoneTransformValueCommand : PanelCommand
    {
        public enum Field { PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, ScaleX, ScaleY, ScaleZ }

        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BoneTransformField",
                 Description = "書き換える軸。位置 / 回転 / 拡大率の X・Y・Z", Required = true)]
        public Field TargetField { get; }

        [PLParam(TextKey = "BoneTransformValue",
                 Description = "TargetField へ入れる値。回転は度", Required = true)]
        public float Value { get; }
        // 引数名はプロパティ名と一致させること。PanelCommandFactory.FindProperty が
        // 引数名でプロパティを引くため、ずれると外から値を渡せなくなる。
        public SetBoneTransformValueCommand(int modelIndex, int[] masterIndices, Field targetField, float value)
            : base(modelIndex) { MasterIndices = masterIndices; TargetField = targetField; Value = value; }
    }

    /// <summary>
    /// 表示の姿勢を切り替える（現在ポーズ / バインドポーズ）。
    ///
    /// 表示だけの切替で、BonePoseData も BoneTransform も BindPose も変えない。
    /// 効かせ先は描画（UnifiedBufferManager の行列表）と書き戻し（ToolContext）の 2 つ。
    /// 規約は PolyLing_姿勢の規約.md の 10 章。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.None, Description = "表示の姿勢を切り替える。true でバインドポーズ表示。データは変えない。")]
    public class SetPoseDisplayModeCommand : PanelCommand
    {
        [PLParam(TextKey = "ShowBindPose",
                 Description = "true でバインドポーズ表示、false で現在ポーズ表示", Required = true)]
        public bool ShowBindPose { get; }

        public SetPoseDisplayModeCommand(int modelIndex, bool showBindPose)
            : base(modelIndex) { ShowBindPose = showBindPose; }
    }

    /// <summary>
    /// ポーズ層（BonePoseData の "Manual" 層）の Position / Rotation を 1 軸だけ変える。
    ///
    /// SetBoneTransformValueCommand との違いは書き込み先。
    ///   SetBoneTransformValue … BoneTransform（＝バインド側。バインド階層も動く）
    ///   SetBonePoseValue      … BonePoseData のポーズ層（＝現在ポーズだけが動く）
    /// 規約は PolyLing_姿勢の規約.md を参照。
    ///
    /// スケールはポーズ層の対象外なので受け付けない（指定すると失敗する）。
    ///
    /// 対象はボーンに限らない。描画メッシュ自体がボーンと同じ階層構造を持つ
    /// （メッシュフィルタ相当）ため、非スキンドの描画オブジェクトへも入る。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.Targets, Description = "ポーズ層（Manual）の Position / Rotation の 1 軸だけを変える。バインド側は動かさない。")]
    public class SetBonePoseValueCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の masterIndex 配列。ボーンでも描画オブジェクトでもよい", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BoneTransformField",
                 Description = "書き換える軸。位置 / 回転の X・Y・Z。拡大率は受け付けない", Required = true)]
        public SetBoneTransformValueCommand.Field TargetField { get; }

        [PLParam(TextKey = "BoneTransformValue",
                 Description = "TargetField へ入れる値。回転は度", Required = true)]
        public float Value { get; }

        public SetBonePoseValueCommand(
            int modelIndex, int[] masterIndices,
            SetBoneTransformValueCommand.Field targetField, float value)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            TargetField   = targetField;
            Value         = value;
        }
    }

    /// <summary>BoneTransform スライダードラッグ開始（Undo スナップショット取得）</summary>
    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.None, Description = "BoneTransform のスライダー操作を始める（Undo のスナップショットを取る）。")]
    public class BeginBoneTransformSliderDragCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>ボーン編集の確定モード（A/B）。パネルが送信時に刻む。</summary>
        [PLParam(TextKey = "BoneMoveMode",
                 Description = "ボーン編集の確定モード。既定は BoneOnlyRebind")]
        public BoneMoveMode Mode { get; set; } = BoneMoveMode.BoneOnlyRebind;
        /// <summary>
        /// 「原点だけ移動」中か。true のとき、対象 MeshFilter の見た目を固定したまま
        /// 原点(BoneTransform)だけを動かすよう受信側が自頂点を再ローカル化する。
        /// パネルが送信時に刻む。
        /// </summary>
        [PLParam(TextKey = "BoneOriginOnly",
                 Description = "見た目を固定したまま原点だけを動かす。既定は false")]
        public bool OriginOnly { get; set; } = false;
        public BeginBoneTransformSliderDragCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>BoneTransform スライダードラッグ終了（Undo 記録コミット）</summary>
    [PLCommand(Category = "rig.skeleton.pose", Effects = PLCommandEffect.Skeleton | PLCommandEffect.Pose | PLCommandEffect.BindPose | PLCommandEffect.VertexPosition, Hazards = PLCommandHazard.ChangesBindPose, Verification = PLCommandVerification.SkinWeights | PLCommandVerification.Visual, Writes = PLWriteScope.ModelWide, Description = "BoneTransform のスライダー操作を終える（Undo を記録する）。")]
    public class EndBoneTransformSliderDragCommand : PanelCommand
    {
        [PLParam(TextKey = "BoneDragDescription",
                 Description = "Undo 記録に残す操作名", Required = true)]
        public string Description { get; }
        public EndBoneTransformSliderDragCommand(int modelIndex, string description)
            : base(modelIndex) { Description = description; }
    }

    /// <summary>
    /// 現在表示中のポーズ（BonePoseData 合成）を頂点へ焼き込み、ポーズ層をクリアして
    /// 焼き込み後の状態を新しいデフォルト・バインドにリセットする（この姿勢で確定）。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.pose", Writes = PLWriteScope.ModelWide, Description = "現在表示中のポーズ（BonePoseData 合成）を頂点へ焼き込み、ポーズ層をクリアして 焼き込み後の状態を新しいデフォルト・バインドにリセットする（この姿勢で確定）。")]
    public class FreezeCurrentPoseCommand : PanelCommand
    {
        public FreezeCurrentPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // スプリングボーン検証リグ
    // ================================================================

    /// <summary>
    /// スプリングボーン検証用のダミー装備を生成する（システムデバッグ）。
    ///
    /// 揺れデータ（SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders）を
    /// 書き込むオーサリング UI が無いため、VRM 出力の検証ができない。
    /// このコマンドは既存モデルへボーン鎖・スキンドメッシュ・揺れ付帯データ・
    /// コライダーを一度に足す。生成規則は SpringBoneTestRigBuilder が正典。
    /// </summary>
    [PLCommand(Category = "dynamics.spring", Writes = PLWriteScope.ModelWide, Description = "スプリングボーン検証用のダミー装備（ボーン鎖・スキンドメッシュ・揺れ付帯データ・コライダー）を一度に生成する。")]
    public class BuildSpringBoneTestRigCommand : PanelCommand
    {
        /// <summary>生成パラメータ。null なら既定値。</summary>
        [PLParam(TextKey = "SpringBoneTestRig",
                 Description = "揺れ物テストリグの生成パラメータ。省くと既定値", Required = true)]
        public Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams Params { get; }

        /// <summary>生成前に同じ接頭辞の既存生成物を消すか。</summary>
        [PLParam(TextKey = "SpringBoneClearExisting",
                 Description = "生成前に同じ接頭辞の既存生成物を消す。既定は true")]
        public bool ClearExisting { get; }

        public BuildSpringBoneTestRigCommand(
            int modelIndex,
            Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams @params,
            bool clearExisting = true)
            : base(modelIndex)
        {
            Params        = @params;
            ClearExisting = clearExisting;
        }
    }

    // ================================================================
    // Humanoidボーンマッピング
    // ================================================================

    /// <summary>プレビューのマッピングをモデルへ適用する。</summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Effects = PLCommandEffect.Skeleton, Writes = PLWriteScope.ModelWide, Description = "Humanoid ボーンの割当（プレビューのマッピング）をモデルへ適用する。")]
    public class ApplyHumanoidMappingCommand : PanelCommand
    {
        /// <summary>
        /// マッピングを平行配列で持つ。
        ///
        /// HumanoidBoneMapping は辞書を抱えたクラスでスキーマに出せないため、
        /// 「Humanoid ボーン名」と「対応するボーンの索引」の 2 本に分けてある。
        /// 2 本は同じ長さにすること。
        /// </summary>
        [PLParam(TextKey = "HumanoidBoneNames",
                 Description = "Humanoid ボーン名。BoneIndices と同じ並び・同じ長さ", Required = true)]
        public string[] BoneNames   { get; }

        [PLParam(TextKey = "HumanoidBoneIndices",
                 Description = "対応するボーンの masterIndex。BoneNames と同じ並び。boneObjectNames を渡すときは省く")]
        public int[]    BoneIndices { get; }

        /// <summary>平行配列から起こしたマッピング。受け口はこちらを使う。</summary>
        public Poly_Ling.Data.HumanoidBoneMapping Mapping
        {
            get
            {
                var m = new Poly_Ling.Data.HumanoidBoneMapping();
                int n = System.Math.Min(BoneNames?.Length ?? 0, BoneIndices?.Length ?? 0);
                for (int i = 0; i < n; i++)
                {
                    if (string.IsNullOrEmpty(BoneNames[i])) continue;
                    m.Set(BoneNames[i], BoneIndices[i]);
                }
                return m;
            }
        }

        /// <summary>
        /// HumanoidBoneMapping を平行配列へ分ける。呼び出し側の書き換えを短くするための補助。
        /// </summary>
        public static void SplitMapping(
            Poly_Ling.Data.HumanoidBoneMapping mapping,
            out string[] boneNames, out int[] boneIndices)
        {
            var names = new System.Collections.Generic.List<string>();
            var idx   = new System.Collections.Generic.List<int>();
            if (mapping?.BoneIndexMap != null)
            {
                foreach (var kv in mapping.BoneIndexMap)
                {
                    names.Add(kv.Key);
                    idx.Add(kv.Value);
                }
            }
            boneNames   = names.ToArray();
            boneIndices = idx.ToArray();
        }

        public ApplyHumanoidMappingCommand(int modelIndex, string[] boneNames, int[] boneIndices = null, string[] boneObjectNames = null)
            : base(modelIndex)
        {
            BoneNames       = boneNames       ?? System.Array.Empty<string>();
            BoneIndices     = boneIndices     ?? System.Array.Empty<int>();
            BoneObjectNames = boneObjectNames ?? System.Array.Empty<string>();
        }

        /// <summary>
        /// 対応するボーンをオブジェクト名で渡す。BoneNames と同じ並び。
        /// 渡したときは BoneIndices を使わず、実行時に名前から索引を引く
        /// （手本に索引を焼かないため。robot_build_skin の e7）。
        /// </summary>
        [PLParam(Description = "対応するボーンのオブジェクト名。BoneNames と同じ並び。渡すと boneIndices の代わりに名前から索引を引く")]
        public string[] BoneObjectNames { get; }
    }

    /// <summary>モデルのHumanoidマッピングをクリアする</summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Writes = PLWriteScope.ModelWide, Description = "モデルの Humanoid ボーン割当を消す。")]
    public class ClearHumanoidMappingCommand : PanelCommand
    {
        public ClearHumanoidMappingCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // Humanoid マッスル可動域（HumanLimit）
    // ================================================================

    /// <summary>
    /// 選んだボーンへマッスル可動域を書き込む（既にあれば上書きする）。
    ///
    /// 【単位は度】
    ///   HumanLimitData の格納はラジアンだが、コマンドは度で受ける。
    ///   Unity の Avatar 画面が度で見せるので、人にも AI にも読める側にそろえた。
    ///   ラジアンへの変換はディスパッチャで行う。
    ///   AxisLength だけは角度ではないので変換しない。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Writes = PLWriteScope.Targets, Description = "選んだボーンへ Humanoid マッスル可動域を書き込む。角度は度。既にあれば上書きする。")]
    public class SetHumanLimitCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象ボーンの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "HumanLimitMinDegrees",
                 Description = "3マッスル軸の下限[度]。X/Y/Z が dof 0/1/2 に対応する", Required = true)]
        public Vector3 MinDegrees { get; }

        [PLParam(TextKey = "HumanLimitMaxDegrees",
                 Description = "3マッスル軸の上限[度]。各軸で下限以上であること", Required = true)]
        public Vector3 MaxDegrees { get; }

        [PLParam(TextKey = "HumanLimitCenterDegrees",
                 Description = "3マッスル軸の中央[度]。既定は 0,0,0")]
        public Vector3 CenterDegrees { get; }

        [PLParam(TextKey = "HumanLimitAxisLength",
                 Description = "Unity HumanLimit.axisLength。角度ではないので度変換しない",
                 Min = 0.0)]
        public float AxisLength { get; }

        public SetHumanLimitCommand(
            int modelIndex, int[] masterIndices,
            Vector3 minDegrees, Vector3 maxDegrees,
            Vector3 centerDegrees = default, float axisLength = 0f)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            MinDegrees    = minDegrees;
            MaxDegrees    = maxDegrees;
            CenterDegrees = centerDegrees;
            AxisLength    = axisLength;
        }
    }

    /// <summary>
    /// マッスル可動域を外して Unity 既定へ戻す。
    /// 外すと、そのボーンは CanonMuscleTable の定義値で駆動される。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Writes = PLWriteScope.Targets, Description = "選んだボーンから Humanoid マッスル可動域を外し、Unity 既定へ戻す。")]
    public class ClearHumanLimitCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象ボーンの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        public ClearHumanLimitCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
        }
    }

    // ================================================================
    // Avatar リターゲット設定
    // ================================================================

    /// <summary>
    /// Humanoid Avatar のリターゲット設定8項目をモデルへ書き込む。
    /// 使うのは Editor のプレファブ書き出し（Avatar 生成）だけで、
    /// Player 内の表示にも VRM 出力にも影響しない。
    /// </summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Writes = PLWriteScope.ModelWide, Description = "Humanoid Avatar のリターゲット設定8項目をモデルへ書き込む。Avatar 生成にだけ効く。")]
    public class SetAvatarRetargetCommand : PanelCommand
    {
        [PLParam(TextKey = "AvatarUpperArmTwist",
                 Description = "上腕の捩り配分", Min = 0.0, Max = 1.0)]
        public float UpperArmTwist { get; }

        [PLParam(TextKey = "AvatarLowerArmTwist",
                 Description = "前腕の捩り配分", Min = 0.0, Max = 1.0)]
        public float LowerArmTwist { get; }

        [PLParam(TextKey = "AvatarUpperLegTwist",
                 Description = "大腿の捩り配分", Min = 0.0, Max = 1.0)]
        public float UpperLegTwist { get; }

        [PLParam(TextKey = "AvatarLowerLegTwist",
                 Description = "下腿の捩り配分", Min = 0.0, Max = 1.0)]
        public float LowerLegTwist { get; }

        [PLParam(TextKey = "AvatarArmStretch",
                 Description = "腕の伸び代", Min = 0.0, Max = 1.0)]
        public float ArmStretch { get; }

        [PLParam(TextKey = "AvatarLegStretch",
                 Description = "脚の伸び代", Min = 0.0, Max = 1.0)]
        public float LegStretch { get; }

        [PLParam(TextKey = "AvatarFeetSpacing",
                 Description = "両足の間隔の補正。範囲の決まりは無い")]
        public float FeetSpacing { get; }

        [PLParam(TextKey = "AvatarHasTranslationDoF",
                 Description = "移動の自由度を持たせるか")]
        public bool HasTranslationDoF { get; }

        public SetAvatarRetargetCommand(
            int modelIndex,
            float upperArmTwist = 0.5f, float lowerArmTwist = 0.5f,
            float upperLegTwist = 0.5f, float lowerLegTwist = 0.5f,
            float armStretch = 0.05f, float legStretch = 0.05f,
            float feetSpacing = 0f, bool hasTranslationDoF = false)
            : base(modelIndex)
        {
            UpperArmTwist     = upperArmTwist;
            LowerArmTwist     = lowerArmTwist;
            UpperLegTwist     = upperLegTwist;
            LowerLegTwist     = lowerLegTwist;
            ArmStretch        = armStretch;
            LegStretch        = legStretch;
            FeetSpacing       = feetSpacing;
            HasTranslationDoF = hasTranslationDoF;
        }
    }

    /// <summary>Avatar リターゲット設定を未設定へ戻す。</summary>
    [PLCommand(Category = "rig.skeleton.humanoid", Writes = PLWriteScope.ModelWide, Description = "Avatar リターゲット設定を未設定へ戻す。Avatar 生成は Unity の既定を使う。")]
    public class ClearAvatarRetargetCommand : PanelCommand
    {
        public ClearAvatarRetargetCommand(int modelIndex) : base(modelIndex) { }
    }
}
