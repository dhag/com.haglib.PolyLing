// PanelCommand.Skin.cs
// MeshFilter→Skinned 変換・種別変換・スキンウェイト（一括操作・塗り）の操作要求。
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
    // MeshFilter → Skinned 変換
    // ================================================================

    /// <summary>
    /// MeshFilter オブジェクト群をボーン+スキンドメッシュ構造に変換する。
    /// Undo 記録付き。変換後に GPU バッファを再構築する。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.Skeleton | PLCommandEffect.SkinWeights | PLCommandEffect.Hierarchy, Hazards = PLCommandHazard.AffectsMultipleObjects, Verification = PLCommandVerification.SkinWeights | PLCommandVerification.Visual, Writes = PLWriteScope.ModelWide, Description = "MeshFilter オブジェクト群をボーン+スキンドメッシュ構造に変換する。")]
    public class ConvertMeshFilterToSkinnedCommand : PanelCommand
    {
        /// <summary>回転ありボーンの軸をPMX軸 (Y→X) に入替える</summary>
        [PLParam(TextKey = "SwapAxisForRotated",
                 Description = "回転ありボーンの軸を PMX 軸（Y→X）に入れ替える。既定は false")]
        public bool SwapAxisForRotated  { get; }

        /// <summary>回転なしボーンを X軸上向き・Y軸横向きに設定する</summary>
        [PLParam(TextKey = "SetAxisForIdentity",
                 Description = "回転なしボーンを X 軸上向き・Y 軸横向きにする。既定は false")]
        public bool SetAxisForIdentity  { get; }

        /// <summary>
        /// ミラー分岐ルート配下の「ミラー設定漏れ」を許容し、
        /// ミラー側メッシュを実体側から生成して実体化する。既定は true。
        /// </summary>
        [PLParam(TextKey = "TolerantMirrorBranch",
                 Description = "ミラー分岐ルート配下の設定漏れを許容して実体化する。既定は true")]
        public bool TolerantMirrorBranch { get; }

        public ConvertMeshFilterToSkinnedCommand(
            int modelIndex,
            bool swapAxisForRotated = false,
            bool setAxisForIdentity = false,
            bool tolerantMirrorBranch = true)
            : base(modelIndex)
        {
            SwapAxisForRotated   = swapAxisForRotated;
            SetAxisForIdentity   = setAxisForIdentity;
            TolerantMirrorBranch = tolerantMirrorBranch;
        }
    }

    // ================================================================
    // 描画オブジェクト単位の種別変換（MeshFilter 系 ⇔ SkinnedMesh 系）
    // ================================================================

    /// <summary>
    /// 選んだ描画オブジェクトのウェイトを破棄して MeshFilter 系へ戻す。
    ///
    /// 頂点はスキンド時にワールド（バインド）空間へ焼かれているため、
    /// 変換先の WorldMatrix の逆行列でローカル化し直す（SkinKindConverter）。
    /// ボーンの生成・破棄は行わない。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Writes = PLWriteScope.Targets, Description = "選んだ描画オブジェクトのウェイトを破棄して MeshFilter 系へ戻す。")]
    public class ConvertToMeshFilterCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>階層の扱い。既定はルート直下へ移す。</summary>
        [PLParam(TextKey = "UnskinParentMode",
                 Description = "変換後の階層の扱い。既定は MoveToRoot")]
        public UnskinParentMode ParentMode { get; }

        public ConvertToMeshFilterCommand(
            int modelIndex, int[] masterIndices,
            UnskinParentMode parentMode = UnskinParentMode.MoveToRoot)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            ParentMode    = parentMode;
        }
    }

    /// <summary>
    /// 選んだ描画オブジェクトを、指定ボーンへウェイト 1.0 でバインドして
    /// SkinnedMesh 系にする。ボーンの生成は行わない（既存ボーンへ付ける）。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Writes = PLWriteScope.Targets, Description = "選んだ描画オブジェクトを、指定ボーンへウェイト 1.0 でバインドして SkinnedMesh 系にする。")]
    public class ConvertToSkinnedCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>バインド先ボーンの MeshContextList 索引。</summary>
        [PLParam(TextKey = "BindBoneMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "ウェイト 1.0 でバインドする先のボーンの masterIndex", Required = true)]
        public int BoneMasterIndex { get; }

        public ConvertToSkinnedCommand(int modelIndex, int[] masterIndices, int boneMasterIndex)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices;
            BoneMasterIndex = boneMasterIndex;
        }
    }

    /// <summary>
    /// ボーンの左右対応（MirrorBoneIndex）を、ボーン名の左右から補完する。
    ///
    /// スキンド変換が確定させた値（-1 以外）は上書きしない。
    /// PMX インポート直後のようにボーンが全て -1 のモデルで、
    /// ミラー生成前に一度だけ実行する用途。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Writes = PLWriteScope.ModelWide, Description = "ボーンの左右対応（MirrorBoneIndex）を、ボーン名の左右から補完する。")]
    public class ResolveMirrorBoneIndexCommand : PanelCommand
    {
        public ResolveMirrorBoneIndexCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // スキンウェイト一括操作
    // ================================================================

    /// <summary>選択中の描画メッシュ全頂点に指定ウェイトを一括塗りつぶす（Flood）</summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.ModelWide, Description = "選択中の描画メッシュの全頂点へ、指定したウェイトを一括で塗る。")]
    public class FloodSkinWeightCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightTargetBone",
                 Description = "塗り対象のボーンの masterIndex", Required = true)]
        public int                          TargetBoneMaster { get; }

        [PLParam(TextKey = "SkinWeightPaintMode",
                 Description = "塗り方。Replace / Add / Scale / Smooth", Required = true)]
        public Poly_Ling.UI.SkinWeightPaintMode PaintMode    { get; }

        [PLParam(TextKey = "SkinWeightValue",
                 Description = "書き込むウェイト値", Required = true)]
        public float                        WeightValue      { get; }

        [PLParam(TextKey = "SkinWeightStrength",
                 Description = "適用の強さ",
                 LimitKey = "SkinWeight.Strength", Required = true)]
        public float                        Strength         { get; }
        public FloodSkinWeightCommand(int modelIndex, int targetBoneMaster,
            Poly_Ling.UI.SkinWeightPaintMode paintMode, float weightValue, float strength)
            : base(modelIndex)
        {
            TargetBoneMaster = targetBoneMaster;
            PaintMode        = paintMode;
            WeightValue      = weightValue;
            Strength         = strength;
        }
    }

    /// <summary>選択中の描画メッシュ全頂点のボーンウェイトを正規化する（Normalize）</summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.ModelWide, Description = "選択中の描画メッシュの全頂点のボーンウェイトを、合計が 1 になるよう揃える。")]
    public class NormalizeSkinWeightCommand : PanelCommand
    {
        public NormalizeSkinWeightCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>選択中の描画メッシュ全頂点の微小ウェイトを除去する（Prune）</summary>
    [PLCommand(Category = "rig.skinning", Writes = PLWriteScope.ModelWide, Description = "選択中の描画メッシュの全頂点から、ごく小さいボーンウェイトを取り除く。")]
    public class PruneSkinWeightCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightPruneThreshold",
                 Description = "この値より小さいウェイトを除去する", Required = true)]
        public float Threshold { get; }
        public PruneSkinWeightCommand(int modelIndex, float threshold)
            : base(modelIndex) { Threshold = threshold; }
    }

    /// <summary>
    /// 選択頂点のボーンウェイトを、指定した最大 4 組（ボーン MasterIndex, ウェイト値）で
    /// 直接上書きする。数値入力パネル（PlayerSkinWeightNumericSubPanel）から送られる。
    /// BoneMasters が負値のスロットは未使用として weight 0 で埋める。
    /// 正規化はパネル側のボタンで行うため、ここでは入力値をそのまま書き込む。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.ModelWide, Description = "選択頂点のボーンウェイトを、指定した最大 4 組（ボーン MasterIndex, ウェイト値）で 直接上書きする。")]
    public class SetSkinWeightNumericCommand : PanelCommand
    {
        /// <summary>長さ 4。ボーンの MasterIndex。負値は未使用スロット。</summary>
        [PLParam(TextKey = "SkinWeightBoneMasters",
                 Description = "長さ 4。ボーンの masterIndex。負値は未使用スロット", Required = true)]
        public int[] BoneMasters { get; }

        /// <summary>長さ 4。各スロットのウェイト値。</summary>
        [PLParam(TextKey = "SkinWeightWeights",
                 Description = "長さ 4。各スロットのウェイト値", Required = true)]
        public float[] Weights { get; }

        public SetSkinWeightNumericCommand(int modelIndex, int[] boneMasters, float[] weights)
            : base(modelIndex)
        {
            BoneMasters = boneMasters;
            Weights     = weights;
        }
    }

    /// <summary>
    /// 対象メッシュ全件の全頂点についてボーンウェイトを正規化する。
    /// 合計が 1 でない頂点は GPU スキニングで原点方向へ寄り見た目が崩れるため、
    /// 読み込んだモデルや過去の編集で壊れた箇所をまとめて直す。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Hazards = PLCommandHazard.AffectsMultipleObjects, Verification = PLCommandVerification.SkinWeights, Writes = PLWriteScope.ModelWide, Description = "対象メッシュ全件の全頂点についてボーンウェイトを正規化する。")]
    public class NormalizeAllSkinWeightsCommand : PanelCommand
    {
        public NormalizeAllSkinWeightsCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// 立体範囲（円筒・球）の中の頂点へ、親ボーンと自ボーンのウェイトを直線補間で配分する
    /// （スキンW範囲塗り）。親関節で自ボーン 0%、自関節で 50%、先端で 100%。
    /// 範囲内の頂点は自ボーン w・親ボーン 1-w だけになり、範囲外の頂点は変わらない。
    /// 対象は選択中の描画オブジェクト全件で、非スキンドが含まれていれば失敗する。
    /// 計算はバインド空間（頂点の格納値と BindPose⁻¹ の原点）で行う（SkinWeightVolumeOps）。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.ModelWide, Description = "円筒または球の範囲内の頂点へ、親ボーンと自ボーンのウェイトを直線補間で配分する（自関節の手前 0%・自関節 50%・先端 100%）。親側の長さは既定で先端側と同じ。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "書き換えた頂点数（全対象の合計）")]
    [PLResult("meshes",   PLResultKind.Integer, Description = "書き換えた描画オブジェクトの数")]
    [PLResult("parentBone", PLResultKind.Integer, Description = "使った親ボーンの masterIndex")]
    public class SkinWeightVolumePaintCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightVolumeSelfBone", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "自ボーンの masterIndex", Required = true)]
        public int SelfBone { get; }

        [PLParam(TextKey = "SkinWeightVolumeParentBone", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "親ボーンの masterIndex。-1 で自ボーンの階層の親")]
        public int ParentBone { get; }

        [PLParam(TextKey = "SkinWeightVolumeShape",
                 Description = "範囲の形。Cylinder / Sphere", Required = true)]
        public SkinWeightVolumeShape Shape { get; }

        [PLParam(TextKey = "SkinWeightVolumeRadius",
                 Description = "半径（バインド空間の長さ）", Required = true)]
        public float Radius { get; }

        [PLParam(TextKey = "SkinWeightVolumeHeight",
                 Description = "円筒の高さ。自関節から先端までの長さ。球では使わない")]
        public float Height { get; }

        [PLParam(TextKey = "SkinWeightVolumeSeparateParentHeight",
                 Description = "親側（自関節から 0% の点まで）の長さを別に指定する。false なら先端側と同じ長さ（円筒は高さ、球は半径）。既定は false")]
        public bool SeparateParentHeight { get; }

        [PLParam(TextKey = "SkinWeightVolumeParentHeight",
                 Description = "親側の長さ。自関節から 0% の点まで。separateParentHeight のときだけ使う")]
        public float ParentHeight { get; }

        [PLParam(TextKey = "SkinWeightVolumeSelectedOnly",
                 Description = "true なら選択頂点だけ、false なら全頂点を対象にする。既定は false")]
        public bool SelectedOnly { get; }

        [PLParam(TextKey = "SkinWeightVolumeAxisMode",
                 Description = "範囲の軸の向き。ParentToSelf=親関節→自関節（既定）/ SelfToChild=自関節→子関節。股関節のように親→自の向きが部位に沿わない関節では SelfToChild")]
        public SkinWeightVolumeAxis AxisMode { get; }

        [PLParam(TextKey = "SkinWeightVolumeChildBone", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "軸に使う子ボーンの masterIndex。-1 で自ボーンの階層の子（子のボーンが 1 本だけのとき）。axisMode が SelfToChild のときだけ使う")]
        public int ChildBone { get; }

        public SkinWeightVolumePaintCommand(
            int modelIndex, int selfBone, int parentBone,
            SkinWeightVolumeShape shape, float radius, float height,
            bool selectedOnly = false,
            bool separateParentHeight = false, float parentHeight = 0f,
            SkinWeightVolumeAxis axisMode = SkinWeightVolumeAxis.ParentToSelf, int childBone = -1)
            : base(modelIndex)
        {
            AxisMode             = axisMode;
            ChildBone            = childBone;
            SelfBone             = selfBone;
            ParentBone           = parentBone;
            Shape                = shape;
            Radius               = radius;
            Height               = height;
            SelectedOnly         = selectedOnly;
            SeparateParentHeight = separateParentHeight;
            ParentHeight         = parentHeight;
        }

        /// <summary>計算用の入力へ詰め替える。</summary>
        public SkinWeightVolumeSpec ToSpec() => new SkinWeightVolumeSpec
        {
            SelfBone             = SelfBone,
            ParentBone           = ParentBone,
            Shape                = Shape,
            Radius               = Radius,
            Height               = Height,
            SeparateParentHeight = SeparateParentHeight,
            ParentHeight         = ParentHeight,
            SelectedOnly         = SelectedOnly,
            AxisMode             = AxisMode,
            ChildBone            = ChildBone,
        };
    }

    /// <summary>
    /// 親関節から指定関節までの円筒の中の頂点を、親ボーン 1 本だけ・ウェイト 100% で塗る。
    /// 関節の前後のぼかしは範囲塗り（SkinWeightVolumePaintCommand）で後から入れる。
    /// 対象は選択中の描画オブジェクト全件で、非スキンドが含まれていれば失敗する。
    /// 計算はバインド空間（SkinWeightVolumeOps.ApplySegmentFillToMesh）。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.ModelWide, Description = "親関節から指定関節までの円筒の中の頂点を、親ボーン 1 本だけ・ウェイト 100% で塗る（他のボーンは消す）。範囲外の頂点は変えない。")]
    [PLResult("vertices",   PLResultKind.Integer, Description = "書き換えた頂点数（全対象の合計）")]
    [PLResult("meshes",     PLResultKind.Integer, Description = "書き換えた描画オブジェクトの数")]
    [PLResult("parentBone", PLResultKind.Integer, Description = "塗った親ボーンの masterIndex")]
    public class SkinWeightSegmentFillCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightSegmentJointBone", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "区間の先の関節にするボーンの masterIndex", Required = true)]
        public int JointBone { get; }

        [PLParam(TextKey = "SkinWeightSegmentParentBone", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "塗る親ボーンの masterIndex（区間の元の関節）。-1 で関節の階層の親")]
        public int ParentBone { get; }

        [PLParam(TextKey = "SkinWeightSegmentRadius",
                 Description = "円筒の半径（バインド空間の長さ）", Required = true)]
        public float Radius { get; }

        [PLParam(TextKey = "SkinWeightSegmentSelectedOnly",
                 Description = "true なら選択頂点だけ、false なら全頂点を対象にする。既定は false")]
        public bool SelectedOnly { get; }

        public SkinWeightSegmentFillCommand(
            int modelIndex, int jointBone, int parentBone, float radius, bool selectedOnly = false)
            : base(modelIndex)
        {
            JointBone    = jointBone;
            ParentBone   = parentBone;
            Radius       = radius;
            SelectedOnly = selectedOnly;
        }
    }

    // ================================================================
    // スキンウェイト塗り
    // ================================================================

    /// <summary>
    /// ブラシで塗ったスキンウェイトを適用する。実処理は SkinWeightPaintTool。
    ///
    /// 【なぜ点列ではなく頂点列を持つか】
    ///   対象頂点はスクリーン空間のブラシ円で決まる（SkinWeightPaintToolHandler の
    ///   ComputeBrushVertices）。ワールド座標の点列から求め直すと対象が変わり、
    ///   パネル操作とリモート発行で結果が食い違う。よって「掛けた頂点と falloff」を
    ///   そのまま持つ。カメラに依存しないので MCP から見ても自己完結する。
    ///
    /// 【ステップを合成しない理由】
    ///   Add は毎回加算、Replace は毎回 Lerp、Scale は毎回乗算するため、結果は
    ///   適用回数と順序に依存する。ブラシ 1 回分を 1 ステップとして順に適用する。
    ///
    /// 【平坦な配列】
    ///   PanelCommandFactory は文字列パラメータから平坦な配列しか組み立てられないため、
    ///   入れ子を持てない。ステップの区切りは StepStarts が持つ。
    ///   長さの整合（StepStarts.Length == StepMeshIndices.Length、
    ///   VertexIndices.Length == Falloffs.Length、StepStarts が単調増加で範囲内）は
    ///   受け口の実行時検証で守る。型では表現できない。
    /// </summary>
    [PLCommand(Category = "rig.skinning", Effects = PLCommandEffect.SkinWeights, Verification = PLCommandVerification.SkinWeights, Writes = PLWriteScope.Targets, Description = "ブラシで塗ったスキンウェイトを適用する。")]
    public class SkinWeightPaintCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "ステップが触る描画オブジェクトの masterIndex 配列。実行時点の塗り対象に含まれること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SkinPaintStepStarts",
                 Description = "各ステップが VertexIndices のどこから始まるか。単調増加。長さ = ステップ数",
                 Required = true)]
        public int[]   StepStarts { get; }

        [PLParam(TextKey = "SkinPaintStepMeshIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "各ステップの対象メッシュ masterIndex。StepStarts と同じ長さ",
                 Required = true)]
        public int[]   StepMeshIndices { get; }

        [PLParam(TextKey = "SkinPaintVertexIndices",
                 Description = "全ステップ分の頂点番号を連結したもの", Required = true)]
        public int[]   VertexIndices { get; }

        [PLParam(TextKey = "SkinPaintFalloffs",
                 Description = "VertexIndices と同じ長さの falloff（0〜1）", Required = true)]
        public float[] Falloffs { get; }

        [PLParam(TextKey = "SkinPaintMode",
                 Description = "塗り方。Replace / Add / Scale / Smooth", Required = true)]
        public Poly_Ling.UI.SkinWeightPaintMode PaintMode { get; }

        /// <summary>対象ボーンの masterIndex。Smooth では読まれない。</summary>
        [PLParam(TextKey = "SkinPaintTargetBone",
                 Description = "対象ボーンの masterIndex。Smooth では読まれない")]
        public int   TargetBone { get; }

        [PLParam(TextKey = "SkinPaintStrength", Description = "塗りの強度")]
        public float Strength { get; }

        [PLParam(TextKey = "SkinPaintWeightValue",
                 Description = "書き込む値。Replace は目標値、Add は加算量、Scale は倍率")]
        public float WeightValue { get; }

        public SkinWeightPaintCommand(
            int modelIndex, int[] masterIndices,
            int[] stepStarts, int[] stepMeshIndices,
            int[] vertexIndices, float[] falloffs,
            Poly_Ling.UI.SkinWeightPaintMode paintMode,
            int targetBone    = -1,
            float strength    = 1f,
            float weightValue = 1f,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices   ?? System.Array.Empty<int>();
            ObjectIds       = objectIds;
            StepStarts      = stepStarts      ?? System.Array.Empty<int>();
            StepMeshIndices = stepMeshIndices ?? System.Array.Empty<int>();
            VertexIndices   = vertexIndices   ?? System.Array.Empty<int>();
            Falloffs        = falloffs        ?? System.Array.Empty<float>();
            PaintMode       = paintMode;
            TargetBone      = targetBone;
            Strength        = strength;
            WeightValue     = weightValue;
        }
    }
}
