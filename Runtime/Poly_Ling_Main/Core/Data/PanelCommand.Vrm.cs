// PanelCommand.Vrm.cs
// VRM（.vrma 書き出し・モデルレベル設定・一人称）の操作要求。
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

    /// <summary>
    /// Unity クリップ（UnityClipDTO の JSON）を現在のモデルへ適用しながら
    /// VRM アニメーション（.vrma）として書き出す。
    /// </summary>
    [PLCommand(Description = "Unity クリップの JSON をモデルへ適用しながら VRM アニメーション（.vrma）を書き出す。モデルに Humanoid 割り当てと作業フォルダが要る。")]
    public class ExportVrmAnimationCommand : PanelCommand
    {
        [PLParam(TextKey = "VrmAnimationSavePath",
                 Description = "書き出し先の .vrma。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "VrmAnimationClipPath",
                 Description = "読み込む Unity クリップの JSON。作業フォルダからの相対経路", Required = true)]
        public string ClipFilePath { get; }

        [PLParam(TextKey = "VrmAnimationLimitPath",
                 Description = "マッスル可動域・実測 CSV（UnityLimit）。空にすると既定値で再構成する")]
        public string MuscleLimitCsvPath { get; }

        [PLParam(TextKey = "VrmAnimationFps",
                 Description = "1 秒あたりのサンプリング枚数。0 以下にするとクリップの frameRate を使う",
                 Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(TextKey = "VrmAnimationStartSec",
                 Description = "書き出す区間の開始時刻（秒）", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(TextKey = "VrmAnimationEndSec",
                 Description = "書き出す区間の終了時刻（秒）。StartSec 以下にするとクリップの終端まで",
                 Min = 0.0)]
        public float EndSec { get; }

        [PLParam(TextKey = "VrmAnimationScale",
                 Description = "骨格の位置と Hips の平行移動に掛ける倍率。VRM はメートル",
                 Min = 0.0001, Max = 1000.0)]
        public float Scale { get; }

        // 既定値を付ける理由。
        //   required の判定は PLParam.Required ではなくコンストラクタ既定値の
        //   有無で決まる（PanelCommandSchema.cs:153-155）。
        //   fps = 0 はクリップの frameRate、endSec = 0 はクリップ終端の意味で、
        //   どちらも UnityClipVrmAnimationSource.ExportToFile が解釈する。
        public ExportVrmAnimationCommand(
            int modelIndex, string filePath, string clipFilePath,
            string muscleLimitCsvPath = "", float fps = 0f,
            float startSec = 0f, float endSec = 0f, float scale = 1f)
            : base(modelIndex)
        {
            FilePath           = filePath;
            ClipFilePath       = clipFilePath;
            MuscleLimitCsvPath = muscleLimitCsvPath;
            Fps                = fps;
            StartSec           = startSec;
            EndSec             = endSec;
            Scale              = scale;
        }
    }

    /// <summary>
    /// Unity クリップ（UnityClipDTO の JSON）を T ポーズ基準の正準骨格へ載せて
    /// VRM アニメーション（.vrma）へ変換する。モデルを参照しない。
    /// </summary>
    [PLCommand(Description = "Unity クリップの JSON を T ポーズ基準で VRM アニメーション（.vrma）へ変換する。モデルは参照しない。作業フォルダが要る。")]
    public class ConvertUnityClipToVrmaCommand : PanelCommand
    {
        [PLParam(TextKey = "VrmaConvertSavePath",
                 Description = "書き出し先の .vrma。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "VrmaConvertClipPath",
                 Description = "読み込む Unity クリップの JSON。muscles を持つ Humanoid クリップであること", Required = true)]
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

        [PLParam(TextKey = "VrmaConvertBoneLength",
                 Description = "正準骨格の骨 1 本の長さ（メートル）。VRMA は骨長を持たないので出力の見た目以外に影響しない",
                 Min = 0.001, Max = 10.0)]
        public float BoneLength { get; }

        [PLParam(TextKey = "VrmaConvertUseRoot",
                 Description = "RootT / RootQ（身体全体の移動と向き）を Hips へ載せる。切ると回転だけになり、以前の挙動に戻る")]
        public bool UseRoot { get; }

        // 既定値の意味は ExportVrmAnimationCommand と同じ。
        // fps = 0 はクリップの frameRate、endSec = 0 はクリップ終端。
        // useRoot の既定は true。Humanoid クリップの Root 情報は本来落とすものではない。
        public ConvertUnityClipToVrmaCommand(
            int modelIndex, string filePath, string clipFilePath,
            float fps = 0f, float startSec = 0f, float endSec = 0f, float boneLength = 0.1f,
            bool useRoot = true)
            : base(modelIndex)
        {
            FilePath     = filePath;
            ClipFilePath = clipFilePath;
            Fps          = fps;
            StartSec     = startSec;
            EndSec       = endSec;
            BoneLength   = boneLength;
            UseRoot      = useRoot;
        }
    }

    /// <summary>
    /// VMD モーションを現在のモデルへ適用しながら
    /// VRM アニメーション（.vrma）として書き出す。
    /// </summary>
    [PLCommand(Description = "VMD をモデルへ適用しながら VRM アニメーション（.vrma）を書き出す。モデルに Humanoid 割り当てと作業フォルダが要る。")]
    public class ExportVmdToVrmaCommand : PanelCommand
    {
        [PLParam(TextKey = "VmdToVrmaSavePath",
                 Description = "書き出し先の .vrma。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "VmdToVrmaVmdPath",
                 Description = "読み込む VMD。作業フォルダからの相対経路", Required = true)]
        public string VmdFilePath { get; }

        [PLParam(TextKey = "VmdToVrmaFps",
                 Description = "1 秒あたりのサンプリング枚数。0 以下にすると VMD の 30fps を使う",
                 Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(TextKey = "VmdToVrmaStartSec",
                 Description = "書き出す区間の開始時刻（秒）", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(TextKey = "VmdToVrmaEndSec",
                 Description = "書き出す区間の終了時刻（秒）。StartSec 以下にすると VMD の終端まで",
                 Min = 0.0)]
        public float EndSec { get; }

        [PLParam(TextKey = "VmdToVrmaScale",
                 Description = "骨格の位置と Hips の平行移動に掛ける倍率。VRM はメートル",
                 Min = 0.0001, Max = 1000.0)]
        public float Scale { get; }

        [PLParam(TextKey = "VmdToVrmaEnableIK",
                 Description = "IK を解いてから採取するか。既定は true")]
        public bool EnableIK { get; }

        [PLParam(TextKey = "VmdToVrmaIkTraceDir",
                 Description = "IK の残差 CSV の出力先フォルダ。作業フォルダからの相対経路。空なら出力しない")]
        public string IkTraceDirectory { get; }

        [PLParam(TextKey = "VmdToVrmaIgnoreAngleLimits",
                 Description = "IK の角度制限を無視するか。切り分け用。既定は false")]
        public bool IgnoreAngleLimits { get; }

        [PLParam(TextKey = "VmdToVrmaKneePreBend",
                 Description = "ひざを解く前に微小量だけ曲げるか。既定は false")]
        public bool KneePreBend { get; }

        [PLParam(TextKey = "VmdToVrmaTPoseAlign",
                 Description = "レスト姿勢を正準 T ポーズへ揃える補正の範囲。None / ArmsOnly / All。既定は ArmsOnly")]
        public Poly_Ling.VMD.VmdTPoseAlignScope AlignScope { get; }

        [PLParam(TextKey = "VmdToVrmaDiagnosticLog",
                 Description = "切り分け用のログを出すか。既定は false")]
        public bool DiagnosticLog { get; }

        // 既定値の意味は ExportVrmAnimationCommand と同じ。
        // fps = 0 は VMD の 30fps、endSec = 0 は VMD 終端。
        public ExportVmdToVrmaCommand(
            int modelIndex, string filePath, string vmdFilePath,
            float fps = 0f, float startSec = 0f, float endSec = 0f,
            float scale = 1f, bool enableIK = true,
            Poly_Ling.VMD.VmdTPoseAlignScope alignScope = Poly_Ling.VMD.VmdTPoseAlignScope.ArmsOnly,
            bool diagnosticLog = false, string ikTraceDirectory = "",
            bool ignoreAngleLimits = false, bool kneePreBend = false)
            : base(modelIndex)
        {
            FilePath          = filePath;
            VmdFilePath       = vmdFilePath;
            Fps               = fps;
            StartSec          = startSec;
            EndSec            = endSec;
            Scale             = scale;
            EnableIK          = enableIK;
            AlignScope        = alignScope;
            DiagnosticLog     = diagnosticLog;
            IkTraceDirectory  = ikTraceDirectory;
            IgnoreAngleLimits = ignoreAngleLimits;
            KneePreBend       = kneePreBend;
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
    [PLCommand(Description = "VRM メタ情報（作者・ライセンス）をモデルへ書き込む。")]
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
    [PLCommand(Description = "VRM メタ情報を未設定へ戻す。出力には VRM の既定だけが載る。")]
    public class ClearVrmMetaCommand : PanelCommand
    {
        public ClearVrmMetaCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// VRM 視線設定（lookAt）を書き込む。
    /// 4本の対応づけは「振り切る頭の角度[度]」と「そのときの出力量」の 2 値ずつ。
    /// </summary>
    [PLCommand(Description = "VRM 視線設定（目の基準点と頭の向き→目の動きの対応）をモデルへ書き込む。")]
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
    [PLCommand(Description = "VRM 視線設定を未設定へ戻す。出力には VRM の既定だけが載る。")]
    public class ClearVrmLookAtCommand : PanelCommand
    {
        public ClearVrmLookAtCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// 描画オブジェクトの一人称カメラでの扱いを決める。
    /// Auto は「指定なし」で、保存にも出力にも出なくなる。
    /// </summary>
    [PLCommand(Description = "描画オブジェクトの一人称カメラでの扱いを決める。0=自動 1=両方 2=三人称のみ 3=一人称のみ。")]
    public class SetVrmFirstPersonCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
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
