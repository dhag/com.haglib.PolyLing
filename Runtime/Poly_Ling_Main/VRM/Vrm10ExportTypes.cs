// Vrm10ExportTypes.cs
// ============================================================
// VRM 1.0 エクスポートの設定・結果（純POCO）
// ============================================================
//
// 【分離規約】格納・参照の規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルには VRM パッケージ（UniGLTF / VrmLib / UniVRM10）の型を一切持ち込まない。
//   ここに VRM の型が入った瞬間、PolyLing.Runtime が VRM パッケージ必須になる。
//
// 【依存】
//   UnityEngine すら使わない純データ。#if UNITY_EDITOR も含まない。
//
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Vrm
{
    /// <summary>
    /// VRM 1.0 エクスポート設定。
    /// VRM の Meta は仕様上いくつかの項目が必須なので、既定値を入れておく。
    /// </summary>
    [Serializable]
    public class Vrm10ExportSettings
    {
        // ================================================================
        // Meta（VRMC_vrm.meta。空だと出力が仕様違反になる項目がある）
        // ================================================================

        /// <summary>モデル名（VRM Meta の name）。空ならモデル名を使う。</summary>
        public string Title = "";

        /// <summary>バージョン文字列（VRM Meta の version）。</summary>
        public string Version = "1.0";

        /// <summary>作者（VRM Meta の authors）。最低1件必要。</summary>
        public List<string> Authors = new List<string> { "Unknown" };

        /// <summary>著作権表記（VRM Meta の copyrightInformation）。</summary>
        public string CopyrightInformation = "";

        /// <summary>連絡先（VRM Meta の contactInformation）。</summary>
        public string ContactInformation = "";

        /// <summary>その他ライセンスURL（VRM Meta の otherLicenseUrl）。</summary>
        public string OtherLicenseUrl = "";

        // ================================================================
        // 出力内容
        // ================================================================

        /// <summary>出力スケール（PolyLing のローカル座標に掛ける倍率）。</summary>
        public float Scale = 1.0f;

        /// <summary>スキニング（ボーンウェイト）を出力するか。</summary>
        public bool ExportSkinning = true;

        /// <summary>法線を出力するか。</summary>
        public bool ExportNormals = true;

        /// <summary>UVを出力するか。</summary>
        public bool ExportUVs = true;

        /// <summary>
        /// 非表示メッシュ（IsVisible == false）も出力するか。
        /// 既定 false。名前と既定値は ObjExportSettings.ExportInvisibleObjects にそろえてある。
        /// </summary>
        public bool ExportInvisibleObjects = false;

        /// <summary>ディープコピー。</summary>
        public Vrm10ExportSettings Clone()
        {
            return new Vrm10ExportSettings
            {
                Title = this.Title,
                Version = this.Version,
                Authors = this.Authors != null ? new List<string>(this.Authors) : new List<string>(),
                CopyrightInformation = this.CopyrightInformation,
                ContactInformation = this.ContactInformation,
                OtherLicenseUrl = this.OtherLicenseUrl,
                Scale = this.Scale,
                ExportSkinning = this.ExportSkinning,
                ExportNormals = this.ExportNormals,
                ExportUVs = this.ExportUVs,
                ExportInvisibleObjects = this.ExportInvisibleObjects
            };
        }

        public static Vrm10ExportSettings CreateDefault() => new Vrm10ExportSettings();
    }

    /// <summary>
    /// VRM 1.0 エクスポート結果。PMXExportResult と同じ形にそろえてある。
    /// </summary>
    public class Vrm10ExportResult
    {
        public bool   Success      { get; set; }
        public string OutputPath   { get; set; }
        public string ErrorMessage { get; set; }

        public int NodeCount     { get; set; }
        public int MeshCount     { get; set; }
        public int MaterialCount { get; set; }
        public int VertexCount   { get; set; }

        /// <summary>Humanoid に割り当てられたボーン数。0 のとき VRM としては不完全。</summary>
        public int HumanoidBoneCount { get; set; }

        /// <summary>
        /// 出力は行えたが VRM として不完全な場合の警告（null/空 = 警告なし）。
        /// 代表例は Humanoid 未割当。VRM 1.0 は humanoid を必須とするため、
        /// 空のままだとビューアが読み込みを拒否するが、glTF としては開けるので
        /// 出力自体は続ける（形状確認用）。
        /// </summary>
        public string Warning { get; set; }

        public static Vrm10ExportResult Failed(string message)
        {
            return new Vrm10ExportResult { Success = false, ErrorMessage = message };
        }
    }
}
