// Assets/Editor/Poly_Ling/Core/Data/IKData.cs
// ============================================================
// IKデータ（純POCOデータ契約）
// ============================================================
//
// 【格納規約】格納・参照・永続化の規約は
//   MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   ※#4a: ターゲット参照へ EffectorBoneName（name主）を追加。TargetIndex/Links は
//     実行時キャッシュへ位置づけ。per-bone 形式（各リンク＝MeshObject.IKLink）とは
//     IKChainResolver で相互同期する（併存・非破壊）。
//
// 【役割】
//   PMX互換のCCD-IK情報を MeshObject に持たせるための純データ。
//   従来は MeshContext のフィールドとして保持していたが、
//   クロス言語移植（Python/JavaScript）と Unityヒエラルキー
//   エクスポートのため、データ実体を MeshObject 側へ統一した。
//   MeshContext は後方互換のための薄い委譲プロパティのみを公開する。
//
// 【不変条件】
//   MeshObject.IKData != null  ⇔  そのボーンはIKボーン。
//   非IKボーンでは IKData を生成しない（意味とメモリの明確化）。
//
// 【参照系】
//   TargetIndex / Links[].BoneIndex は MeshContextList のインデックス。
//   将来のヒエラルキー並べ替えに備え、名前主・index従へ移行する場合は
//   別フィールド追加で拡張する（本段階ではPMX互換のindex参照を踏襲）。
//
// 【依存】
//   UnityEngine.Vector3 のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// IKボーンのCCD-IK情報（純POCO）。
    /// MeshObject.IKData として保持し、非nullがIKボーンであることを表す。
    /// </summary>
    [Serializable]
    public class IKData
    {
        /// <summary>
        /// IKボーンか。
        /// 通常 IKData が生成された時点で true。
        /// （MeshContext.IsIK は IKData の有無＋本フラグへ委譲される）
        /// </summary>
        public bool IsIK { get; set; } = true;

        /// <summary>
        /// IKターゲット（エフェクタ）のMeshContextListインデックス。
        /// 未設定は -1。
        /// </summary>
        public int TargetIndex { get; set; } = -1;

        /// <summary>
        /// IKターゲット（エフェクタ＝先端）のボーン名（name主・規約2）。
        /// #4a: 追加のみ。TargetIndex は並べ替え・I/O で無効化されうる実行時
        /// キャッシュへ位置づける（本欄が一次キー）。空=未解決。
        /// </summary>
        public string EffectorBoneName { get; set; } = "";

        /// <summary>IKループ回数（CCD反復回数）。</summary>
        public int LoopCount { get; set; } = 0;

        /// <summary>IK1回あたりの制限角度（ラジアン）。</summary>
        public float LimitAngle { get; set; } = 0f;

        /// <summary>
        /// IKリンクチェーン（根元→先端の順序はPMX準拠）。
        /// 既定で空リスト（null禁止。生成済みIKDataは常に列挙可能）。
        /// </summary>
        public List<IKLinkInfo> Links { get; set; } = new List<IKLinkInfo>();

        /// <summary>ディープコピー。</summary>
        public IKData Clone()
        {
            var copy = new IKData
            {
                IsIK = this.IsIK,
                TargetIndex = this.TargetIndex,
                EffectorBoneName = this.EffectorBoneName,
                LoopCount = this.LoopCount,
                LimitAngle = this.LimitAngle,
                Links = new List<IKLinkInfo>()
            };
            if (this.Links != null)
            {
                foreach (var link in this.Links)
                    copy.Links.Add(link?.Clone());
            }
            return copy;
        }
    }

    /// <summary>
    /// IKリンク情報（CCD-IKのチェーン要素）。
    /// ※従来は MeshContext.cs 内に定義されていたが、IKデータ一式を
    ///   IKData.cs に集約するため本ファイルへ移設（namespace不変のため
    ///   既存参照は無改修で解決される）。
    /// </summary>
    [Serializable]
    public class IKLinkInfo
    {
        /// <summary>リンクボーンのMeshContextListインデックス。</summary>
        public int BoneIndex { get; set; }

        /// <summary>角度制限あり。</summary>
        public bool HasLimit { get; set; }

        /// <summary>角度制限下限（ラジアン）。</summary>
        public Vector3 LimitMin { get; set; }

        /// <summary>角度制限上限（ラジアン）。</summary>
        public Vector3 LimitMax { get; set; }

        /// <summary>ディープコピー。</summary>
        public IKLinkInfo Clone()
        {
            return new IKLinkInfo
            {
                BoneIndex = this.BoneIndex,
                HasLimit = this.HasLimit,
                LimitMin = this.LimitMin,
                LimitMax = this.LimitMax
            };
        }
    }
}
