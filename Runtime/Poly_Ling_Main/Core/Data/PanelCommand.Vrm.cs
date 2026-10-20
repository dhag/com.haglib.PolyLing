// PanelCommand.Vrm.cs
// VRM（.vrma の書き出し・VMD からの焼き込み・モデルレベル設定・一人称）の操作要求。
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
    // VRM アニメーション（.vrma）書き出し
    // ================================================================

    // 経路（PolyLing_VRMA整備計画.md 2.5）
    //   書き出し … ConvertMotionToVrmaCommand（.plmotion → .vrma。モデル不要）。
    //   読込     … ExportMotionJsonCommand（SourceKind = Vrma。.vrma → .plmotion）。
    //   VMD      … ExportVmdToMotionCommand（VMD＋モデル → .plmotion ＋ .vrma）。

    /// <summary>
    /// PolyLing モーション（.plmotion.json）を VRM アニメーション（.vrma）へ書き出す（VrmaCodec）。
    /// モデルは参照しない。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "PolyLing モーション（.plmotion.json）を VRM アニメーション（.vrma）へ書き出す。Humanoid の回転・Hips の位置・表情・視線を書く。モデルは参照しない。作業フォルダが要る。")]
    public class ConvertMotionToVrmaCommand : PanelCommand
    {
        [PLParam(TextKey = "VrmaConvertSavePath",
                 Description = "書き出し先の .vrma。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "VrmaConvertClipPath",
                 Description = "読み込む PolyLing モーション（.plmotion.json）。muscles か bakedBones を持つこと", Required = true)]
        public string ClipFilePath { get; }

        [PLParam(TextKey = "VrmaConvertFps",
                 Description = "1 秒あたりのサンプリング枚数。0 以下にするとクリップの frameRate を使う",
                 Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(TextKey = "VrmaConvertStartSec",
                 Description = "書き出す区間の開始時刻（秒）", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(TextKey = "VrmaConvertEndSec",
                 Description = "書き出す区間の終了時刻（秒）。StartSec 以下にするとクリップの終端まで",
                 Min = 0.0)]
        public float EndSec { get; }

        [PLParam(TextKey = "VrmaConvertUseBody",
                 Description = "body（身体全体の位置と向き）を使う。切るとレストの body で解く")]
        public bool UseBody { get; }

        // 既定値を付ける理由。
        //   required の判定は PLParam.Required ではなくコンストラクタ既定値の
        //   有無で決まる（PanelCommandSchema.cs:153-155）。
        //   fps = 0 はクリップの frameRate、endSec = 0 はクリップ終端（VrmaCodec が解釈する）。
        public ConvertMotionToVrmaCommand(
            int modelIndex, string filePath, string clipFilePath,
            float fps = 0f, float startSec = 0f, float endSec = 0f, bool useBody = true)
            : base(modelIndex)
        {
            FilePath     = filePath;
            ClipFilePath = clipFilePath;
            Fps          = fps;
            StartSec     = startSec;
            EndSec       = endSec;
            UseBody      = useBody;
        }
    }

    /// <summary>
    /// VMD モーションを現在のモデルへ適用しながら、マッスル＋ボーン（二次骨）＋表情の
    /// PolyLing モーション（.plmotion.json）を書き出す（VmdMotionBake）。
    /// VrmaFilePath があれば、保存した内容を .vrma へも書く（VrmaCodec。.plmotion → .vrma の出口と同じ）。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "VMD をモデルへ適用しながら、マッスル＋二次骨＋表情の PolyLing モーション（.plmotion.json）を書き出す。vrmaFilePath を指定すると同じ内容を VRM アニメーション（.vrma）へも書く。モデルに Humanoid 割り当てと作業フォルダが要る。")]
    public class ExportVmdToMotionCommand : PanelCommand
    {
        [PLParam(TextKey = "VmdToMotionSavePath",
                 Description = "書き出し先の .plmotion.json。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "VmdToMotionVmdPath",
                 Description = "読み込む VMD。作業フォルダからの相対経路", Required = true)]
        public string VmdFilePath { get; }

        [PLParam(TextKey = "VmdToMotionVrmaPath",
                 Description = "同じ内容を書き出す .vrma。作業フォルダからの相対経路。空なら書かない")]
        public string VrmaFilePath { get; }

        [PLParam(TextKey = "VmdToMotionIkTraceDir",
                 Description = "IK の残差 CSV の出力先フォルダ。作業フォルダからの相対経路。空なら出力しない")]
        public string IkTraceDirectory { get; }

        [PLParam(TextKey = "VmdToMotionFps",
                 Description = "1 秒あたりのサンプリング枚数。0 以下にすると VMD の 30fps を使う",
                 Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(TextKey = "VmdToMotionStartSec",
                 Description = "書き出す区間の開始時刻（秒）。出力では 0 秒になる", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(TextKey = "VmdToMotionEndSec",
                 Description = "書き出す区間の終了時刻（秒）。StartSec 以下にすると VMD の終端まで",
                 Min = 0.0)]
        public float EndSec { get; }

        [PLParam(TextKey = "VmdToMotionEnableIK",
                 Description = "IK を解いてから採取するか。既定は true")]
        public bool EnableIK { get; }

        [PLParam(TextKey = "VmdToMotionIgnoreAngleLimits",
                 Description = "IK の角度制限を無視するか。切り分け用。既定は false")]
        public bool IgnoreAngleLimits { get; }

        [PLParam(TextKey = "VmdToMotionKneePreBend",
                 Description = "ひざを解く前に微小量だけ曲げるか。既定は false")]
        public bool KneePreBend { get; }

        [PLParam(TextKey = "VmdToMotionDiagnosticLog",
                 Description = "切り分け用のログを出すか（焼いたモーションを再生したときの VMD 直接との差など）。既定は false")]
        public bool DiagnosticLog { get; }

        [PLParam(TextKey = "VmdToMotionAlignLegs",
                 Description = "T ポーズ合わせを腕に加えて脚（太もも・すね）にも掛けるか。既定は false（腕だけ）。再生するときも同じ設定にすること")]
        public bool AlignLegs { get; }

        // fps = 0 は VMD の 30fps、endSec = 0 は VMD 終端。
        public ExportVmdToMotionCommand(
            int modelIndex, string filePath, string vmdFilePath, string vrmaFilePath = "",
            float fps = 0f, float startSec = 0f, float endSec = 0f,
            bool enableIK = true,
            bool diagnosticLog = false, string ikTraceDirectory = "",
            bool ignoreAngleLimits = false, bool kneePreBend = false,
            bool alignLegs = false)
            : base(modelIndex)
        {
            FilePath          = filePath;
            VmdFilePath       = vmdFilePath;
            VrmaFilePath      = vrmaFilePath ?? "";
            IkTraceDirectory  = ikTraceDirectory ?? "";
            Fps               = fps;
            StartSec          = startSec;
            EndSec            = endSec;
            EnableIK          = enableIK;
            DiagnosticLog     = diagnosticLog;
            IgnoreAngleLimits = ignoreAngleLimits;
            KneePreBend       = kneePreBend;
            AlignLegs         = alignLegs;
        }
    }

    // ================================================================
    // VRM 1.0 のモデルレベル設定・一人称指定
    // ================================================================

    /// <summary>
    /// VRM メタ情報（作者・ライセンス）を書き込む。
    ///
    /// 【文字列と真偽で受ける理由】
    ///   PanelCommandFactory が読める型に限っているため、許諾の 4 項目は
    ///   int（enum の値）で受ける。並びは Poly_Ling.Data の各 enum と同じ。
    /// </summary>
    [PLCommand(Category = "vrm", Writes = PLWriteScope.ModelWide, Description = "VRM メタ情報（作者・ライセンス）をモデルへ書き込む。")]
    public class SetVrmMetaCommand : PanelCommand
    {
        [PLParam(TextKey = "VrmMetaName", Description = "モデル名。空ならモデル名を使う")]
        public string Name { get; }

        [PLParam(TextKey = "VrmMetaVersion", Description = "バージョン文字列。空なら 1.0")]
        public string Version { get; }

        [PLParam(TextKey = "VrmMetaAuthors", Description = "作者。空なら Unknown で出る")]
        public string[] Authors { get; }

        [PLParam(TextKey = "VrmMetaCopyrightInformation", Description = "著作権表記")]
        public string CopyrightInformation { get; }

        [PLParam(TextKey = "VrmMetaContactInformation", Description = "連絡先")]
        public string ContactInformation { get; }

        [PLParam(TextKey = "VrmMetaReferences", Description = "参照元。素材の出どころなど")]
        public string[] References { get; }

        [PLParam(TextKey = "VrmMetaThirdPartyLicenses", Description = "第三者ライセンスの記載")]
        public string ThirdPartyLicenses { get; }

        [PLParam(TextKey = "VrmMetaThumbnailPath",
                 Description = "サムネイル画像のファイルパス。空=なし")]
        public string ThumbnailPath { get; }

        [PLParam(TextKey = "VrmMetaAvatarPermission",
                 Description = "演じてよい人。0=作者のみ 1=別途許諾者のみ 2=誰でも",
                 Min = 0, Max = 2)]
        public int AvatarPermission { get; }

        [PLParam(TextKey = "VrmMetaViolentUsage", Description = "暴力表現に使ってよいか")]
        public bool ViolentUsage { get; }

        [PLParam(TextKey = "VrmMetaSexualUsage", Description = "性的表現に使ってよいか")]
        public bool SexualUsage { get; }

        [PLParam(TextKey = "VrmMetaCommercialUsage",
                 Description = "商用利用。0=個人非営利 1=個人営利 2=法人",
                 Min = 0, Max = 2)]
        public int CommercialUsage { get; }

        [PLParam(TextKey = "VrmMetaPoliticalOrReligiousUsage",
                 Description = "政治・宗教用途に使ってよいか")]
        public bool PoliticalOrReligiousUsage { get; }

        [PLParam(TextKey = "VrmMetaAntisocialOrHateUsage",
                 Description = "反社会的・憎悪表現に使ってよいか")]
        public bool AntisocialOrHateUsage { get; }

        [PLParam(TextKey = "VrmMetaCreditNotation",
                 Description = "クレジット表記。0=必要 1=不要", Min = 0, Max = 1)]
        public int CreditNotation { get; }

        [PLParam(TextKey = "VrmMetaRedistribution", Description = "再配布してよいか")]
        public bool Redistribution { get; }

        [PLParam(TextKey = "VrmMetaModification",
                 Description = "改変。0=禁止 1=改変可 2=改変物の再配布も可", Min = 0, Max = 2)]
        public int Modification { get; }

        [PLParam(TextKey = "VrmMetaOtherLicenseUrl", Description = "その他ライセンスの URL")]
        public string OtherLicenseUrl { get; }

        public SetVrmMetaCommand(
            int modelIndex,
            string name = "", string version = "", string[] authors = null,
            string copyrightInformation = "", string contactInformation = "",
            string[] references = null, string thirdPartyLicenses = "",
            string thumbnailPath = "",
            int avatarPermission = 0, bool violentUsage = false, bool sexualUsage = false,
            int commercialUsage = 0, bool politicalOrReligiousUsage = false,
            bool antisocialOrHateUsage = false,
            int creditNotation = 0, bool redistribution = false, int modification = 0,
            string otherLicenseUrl = "")
            : base(modelIndex)
        {
            Name                 = name ?? "";
            Version              = version ?? "";
            Authors              = authors ?? System.Array.Empty<string>();
            CopyrightInformation = copyrightInformation ?? "";
            ContactInformation   = contactInformation ?? "";
            References           = references ?? System.Array.Empty<string>();
            ThirdPartyLicenses   = thirdPartyLicenses ?? "";
            ThumbnailPath        = thumbnailPath ?? "";

            AvatarPermission          = avatarPermission;
            ViolentUsage              = violentUsage;
            SexualUsage               = sexualUsage;
            CommercialUsage           = commercialUsage;
            PoliticalOrReligiousUsage = politicalOrReligiousUsage;
            AntisocialOrHateUsage     = antisocialOrHateUsage;

            CreditNotation  = creditNotation;
            Redistribution  = redistribution;
            Modification    = modification;
            OtherLicenseUrl = otherLicenseUrl ?? "";
        }
    }

    /// <summary>VRM メタ情報を未設定へ戻す。</summary>
    [PLCommand(Category = "vrm", Writes = PLWriteScope.ModelWide, Description = "VRM メタ情報を未設定へ戻す。出力には VRM の既定だけが載る。")]
    public class ClearVrmMetaCommand : PanelCommand
    {
        public ClearVrmMetaCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// VRM 視線設定（lookAt）を書き込む。
    /// 4本の対応づけは「振り切る頭の角度[度]」と「そのときの出力量」の 2 値ずつ。
    /// </summary>
    [PLCommand(Category = "vrm", Writes = PLWriteScope.ModelWide, Description = "VRM 視線設定（目の基準点と頭の向き→目の動きの対応）をモデルへ書き込む。")]
    public class SetVrmLookAtCommand : PanelCommand
    {
        [PLParam(TextKey = "VrmLookAtOffsetFromHead",
                 Description = "目の基準点。頭ボーンから見た位置[m]", Required = true)]
        public Vector3 OffsetFromHead { get; }

        [PLParam(TextKey = "VrmLookAtType",
                 Description = "0=目ボーンを回す 1=表情で表す", Min = 0, Max = 1)]
        public int LookAtType { get; }

        [PLParam(TextKey = "VrmLookAtHorizontalInner",
                 Description = "鼻側へ向くとき [振り切る頭の角度[度], 出力量]", Required = true)]
        public Vector2 HorizontalInner { get; }

        [PLParam(TextKey = "VrmLookAtHorizontalOuter",
                 Description = "こめかみ側へ向くとき [振り切る頭の角度[度], 出力量]", Required = true)]
        public Vector2 HorizontalOuter { get; }

        [PLParam(TextKey = "VrmLookAtVerticalDown",
                 Description = "下を向くとき [振り切る頭の角度[度], 出力量]", Required = true)]
        public Vector2 VerticalDown { get; }

        [PLParam(TextKey = "VrmLookAtVerticalUp",
                 Description = "上を向くとき [振り切る頭の角度[度], 出力量]", Required = true)]
        public Vector2 VerticalUp { get; }

        public SetVrmLookAtCommand(
            int modelIndex, Vector3 offsetFromHead, int lookAtType,
            Vector2 horizontalInner, Vector2 horizontalOuter,
            Vector2 verticalDown, Vector2 verticalUp)
            : base(modelIndex)
        {
            OffsetFromHead  = offsetFromHead;
            LookAtType      = lookAtType;
            HorizontalInner = horizontalInner;
            HorizontalOuter = horizontalOuter;
            VerticalDown    = verticalDown;
            VerticalUp      = verticalUp;
        }
    }

    /// <summary>VRM 視線設定を未設定へ戻す。</summary>
    [PLCommand(Category = "vrm", Writes = PLWriteScope.ModelWide, Description = "VRM 視線設定を未設定へ戻す。出力には VRM の既定だけが載る。")]
    public class ClearVrmLookAtCommand : PanelCommand
    {
        public ClearVrmLookAtCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// 描画オブジェクトの一人称カメラでの扱いを決める。
    /// Auto は「指定なし」で、保存にも出力にも出なくなる。
    /// </summary>
    [PLCommand(Category = "vrm", Writes = PLWriteScope.Targets, WritesMirrorSide = false, Description = "描画オブジェクトの一人称カメラでの扱いを決める。0=自動 1=両方 2=三人称のみ 3=一人称のみ。")]
    public class SetVrmFirstPersonCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "VrmFirstPersonType",
                 Description = "0=自動 1=両方で描く 2=三人称のみ 3=一人称のみ",
                 Min = 0, Max = 3, Required = true)]
        public int FirstPersonType { get; }

        public SetVrmFirstPersonCommand(int modelIndex, int[] masterIndices, int firstPersonType)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices;
            FirstPersonType = firstPersonType;
        }
    }
}
