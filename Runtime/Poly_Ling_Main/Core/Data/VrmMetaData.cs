// Runtime/Poly_Ling_Main/Core/Data/VrmMetaData.cs
// ============================================================
// VRM 1.0 メタ情報（VRMC_vrm.meta）の純POCOデータ契約
// ============================================================
//
// 【役割】
//   VRM 出力に載せる作者・ライセンス情報。UniVRM の VRM10ObjectMeta と
//   同じ項目をモデルレベルで保持する。値の写しは PolyLing.Vrm10 が行う。
//
// 【なぜ PolyLing 側で enum を定義するか】
//   本POCOは PolyLing.Runtime に属し、VRM パッケージへ依存できない
//   （分離規約は IVrm10Exporter.cs 冒頭を正典とする）。
//   UniGLTF.Extensions.VRMC_vrm の 4 つの enum と「同じ並び」の enum を
//   ここで定義し、境界（Vrm10ExporterImpl）で switch で写す。
//   (int) キャストで写さないこと。片方の並びが変わったとき黙って壊れる。
//
// 【サムネイル】
//   Texture2D を持たず、ファイルパスだけを持つ。読み込みは出力時に
//   Vrm10ExporterImpl が行い、作った Texture2D はその場で捨てる。
//   モデルの保存物に画像の実体を抱えないための決まり。
//
// 【空欄の扱い】
//   空文字・空リストは「指定なし」。VRM 仕様の必須項目（name / authors）は
//   出力時に既定値で埋める（Vrm10ExporterImpl.BuildMeta）。
//
// 【依存】
//   UnityEngine の型を使わない。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>このアバターを演じてよい人。VRMC_vrm の avatarPermission。</summary>
    public enum VrmAvatarPermission
    {
        /// <summary>作者のみ。</summary>
        OnlyAuthor = 0,
        /// <summary>別途許諾を得た人のみ。</summary>
        OnlySeparatelyLicensedPerson = 1,
        /// <summary>誰でも。</summary>
        Everyone = 2,
    }

    /// <summary>商用利用の範囲。VRMC_vrm の commercialUsage。</summary>
    public enum VrmCommercialUsage
    {
        /// <summary>個人・非営利のみ。</summary>
        PersonalNonProfit = 0,
        /// <summary>個人の営利まで可。</summary>
        PersonalProfit = 1,
        /// <summary>法人の利用まで可。</summary>
        Corporation = 2,
    }

    /// <summary>クレジット表記の要否。VRMC_vrm の creditNotation。</summary>
    public enum VrmCreditNotation
    {
        /// <summary>表記が必要。</summary>
        Required = 0,
        /// <summary>表記は不要。</summary>
        Unnecessary = 1,
    }

    /// <summary>改変の可否。VRMC_vrm の modification。</summary>
    public enum VrmModification
    {
        /// <summary>改変禁止。</summary>
        Prohibited = 0,
        /// <summary>改変可。再配布は不可。</summary>
        AllowModification = 1,
        /// <summary>改変可。改変物の再配布も可。</summary>
        AllowModificationRedistribution = 2,
    }

    /// <summary>
    /// VRM 1.0 メタ情報（純POCO）。ModelContext.VrmMeta として1つ保持する。
    /// null＝未設定で、出力時は VRM の既定値だけが載る。
    /// </summary>
    [Serializable]
    public class VrmMetaData
    {
        // ---- 情報 ----------------------------------------------------

        /// <summary>モデル名（VRM Meta の name）。空ならモデル名を使う。</summary>
        public string Name { get; set; } = "";

        /// <summary>バージョン文字列（VRM Meta の version）。空なら "1.0"。</summary>
        public string Version { get; set; } = "";

        /// <summary>作者（VRM Meta の authors）。空なら "Unknown" 1件で出す。</summary>
        public List<string> Authors { get; set; } = new List<string>();

        /// <summary>著作権表記（copyrightInformation）。</summary>
        public string CopyrightInformation { get; set; } = "";

        /// <summary>連絡先（contactInformation）。</summary>
        public string ContactInformation { get; set; } = "";

        /// <summary>参照元（references）。素材の出どころなど。</summary>
        public List<string> References { get; set; } = new List<string>();

        /// <summary>第三者ライセンスの記載（thirdPartyLicenses）。</summary>
        public string ThirdPartyLicenses { get; set; } = "";

        /// <summary>
        /// サムネイル画像のファイルパス。空＝なし。
        /// 実体は持たず、出力時に読み込んで載せる。
        /// </summary>
        public string ThumbnailPath { get; set; } = "";

        // ---- 人格の許諾 ----------------------------------------------

        /// <summary>このアバターを演じてよい人。</summary>
        public VrmAvatarPermission AvatarPermission { get; set; } = VrmAvatarPermission.OnlyAuthor;

        /// <summary>暴力表現に使ってよいか。</summary>
        public bool ViolentUsage { get; set; } = false;

        /// <summary>性的表現に使ってよいか。</summary>
        public bool SexualUsage { get; set; } = false;

        /// <summary>商用利用の範囲。</summary>
        public VrmCommercialUsage CommercialUsage { get; set; } = VrmCommercialUsage.PersonalNonProfit;

        /// <summary>政治・宗教用途に使ってよいか。</summary>
        public bool PoliticalOrReligiousUsage { get; set; } = false;

        /// <summary>反社会的・憎悪表現に使ってよいか。</summary>
        public bool AntisocialOrHateUsage { get; set; } = false;

        // ---- 再配布・改変の許諾 --------------------------------------

        /// <summary>クレジット表記の要否。</summary>
        public VrmCreditNotation CreditNotation { get; set; } = VrmCreditNotation.Required;

        /// <summary>再配布してよいか。</summary>
        public bool Redistribution { get; set; } = false;

        /// <summary>改変の可否。</summary>
        public VrmModification Modification { get; set; } = VrmModification.Prohibited;

        /// <summary>その他ライセンスの URL（otherLicenseUrl）。</summary>
        public string OtherLicenseUrl { get; set; } = "";

        /// <summary>ディープコピー。</summary>
        public VrmMetaData Clone()
        {
            return new VrmMetaData
            {
                Name                 = this.Name,
                Version              = this.Version,
                Authors              = (this.Authors != null)
                                       ? new List<string>(this.Authors) : new List<string>(),
                CopyrightInformation = this.CopyrightInformation,
                ContactInformation   = this.ContactInformation,
                References           = (this.References != null)
                                       ? new List<string>(this.References) : new List<string>(),
                ThirdPartyLicenses   = this.ThirdPartyLicenses,
                ThumbnailPath        = this.ThumbnailPath,

                AvatarPermission          = this.AvatarPermission,
                ViolentUsage              = this.ViolentUsage,
                SexualUsage               = this.SexualUsage,
                CommercialUsage           = this.CommercialUsage,
                PoliticalOrReligiousUsage = this.PoliticalOrReligiousUsage,
                AntisocialOrHateUsage     = this.AntisocialOrHateUsage,

                CreditNotation  = this.CreditNotation,
                Redistribution  = this.Redistribution,
                Modification    = this.Modification,
                OtherLicenseUrl = this.OtherLicenseUrl,
            };
        }
    }
}
