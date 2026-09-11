// PanelCommand.Mirror.cs
// ミラー編集の操作要求。
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
    // Mirror編集
    // ================================================================

    /// <summary>ミラーベイクで境界をどう決めるか</summary>
    public enum MirrorBoundaryMode
    {
        /// <summary>ミラー平面からの距離がしきい値未満の頂点を境界とする（従来）</summary>
        Threshold,
        /// <summary>選択頂点を境界とする</summary>
        SelectedVertices,
    }

    /// <summary>
    /// 選択メッシュ自身にミラーの実体を生やす（ミラー実体化 / in-place）。
    ///
    /// 対称面をまたぐ処理（法線スムージング等）を正しく効かせるための作業用機能であり、
    /// 見た目・エクスポート用の別オブジェクトは作らない。頂点も面も選択メッシュの中に増える。
    /// メッシュが見た目用のミラーモード（MirrorType > 0）だった場合は、実体化と同時に解除する。
    /// </summary>
    [PLCommand(Description = "選択メッシュ自身にミラーの実体を生やす（ミラー実体化 / in-place）。")]
    public class BakeMirrorCommand : PanelCommand
    {
        /// <summary>
        /// ミラー軸として指せる軸の数（X / Y / Z）。
        /// パネル側の選択肢（PlayerMirrorSubPanel の axisChoices）も同数で対応する。
        /// 法線編集の軸（NormalEditCommand.AxisCount）とは別物なので共有しない。
        /// </summary>
        public const int MirrorAxisCount = 3;

        [PLParam(TextKey = "BakeMirrorSourceMasterIndex",
                 Description = "ミラーを実体化する描画オブジェクトの masterIndex", Required = true)]
        public int   SourceMasterIndex { get; }

        /// <summary>ミラー軸（0:X, 1:Y, 2:Z）。メッシュが MirrorType > 0 のときはメッシュ側の設定が優先される。</summary>
        [PLParam(TextKey = "BakeMirrorAxis",
                 Description = "ミラー軸。0=X, 1=Y, 2=Z。メッシュ側の設定があればそちらが優先される",
                 Min = 0, Max = MirrorAxisCount - 1, Required = true)]
        public int   MirrorAxis        { get; }

        [PLParam(TextKey = "BakeMirrorThreshold",
                 Description = "ミラー平面からの距離が この値未満の頂点を境界とみなす",
                 LimitKey = "Mirror.Threshold", Required = true)]
        public float Threshold         { get; }

        [PLParam(TextKey = "BakeMirrorFlipU",
                 Description = "ミラー側の U 座標を反転する", Required = true)]
        public bool  FlipU             { get; }

        /// <summary>ミラー平面のオフセット（ローカル座標）</summary>
        [PLParam(TextKey = "BakeMirrorPlaneOffset",
                 Description = "ミラー平面のオフセット（ローカル座標）。既定は 0")]
        public float PlaneOffset { get; }

        /// <summary>境界の決め方</summary>
        [PLParam(TextKey = "BakeMirrorBoundaryMode",
                 Description = "境界の決め方。しきい値 / 選択頂点。既定は Threshold")]
        public MirrorBoundaryMode BoundaryMode { get; }

        /// <summary>境界頂点をミラー平面へ射影するか</summary>
        [PLParam(TextKey = "BakeMirrorProjectBoundaryToPlane",
                 Description = "境界頂点をミラー平面へ射影する。既定は true")]
        public bool ProjectBoundaryToPlane { get; }

        public BakeMirrorCommand(int modelIndex, int sourceMasterIndex, int mirrorAxis, float threshold, bool flipU)
            : this(modelIndex, sourceMasterIndex, mirrorAxis, threshold, flipU,
                   0f, MirrorBoundaryMode.Threshold, true)
        {
        }

        public BakeMirrorCommand(
            int modelIndex,
            int sourceMasterIndex,
            int mirrorAxis,
            float threshold,
            bool flipU,
            float planeOffset,
            MirrorBoundaryMode boundaryMode,
            bool projectBoundaryToPlane)
            : base(modelIndex)
        {
            SourceMasterIndex      = sourceMasterIndex;
            MirrorAxis             = mirrorAxis;
            Threshold              = threshold;
            FlipU                  = flipU;
            PlaneOffset            = planeOffset;
            BoundaryMode           = boundaryMode;
            ProjectBoundaryToPlane = projectBoundaryToPlane;
        }
    }

    /// <summary>
    /// ミラー実体化を解除して半身へ戻す（in-place）。
    /// 既定では解除後に見た目・エクスポート用のミラーモード（MirrorType = 2 / 結合）を強制する。
    /// RestoreSavedMirrorSettings = true のときは、実体化前のミラー設定へそのまま戻す。
    /// </summary>
    [PLCommand(Description = "ミラー実体化を解除して半身へ戻す（in-place）。")]
    public class UnbakeMirrorCommand : PanelCommand
    {
        [PLParam(TextKey = "UnbakeSourceMasterIndex",
                 Description = "ミラーを解除する描画オブジェクトの masterIndex", Required = true)]
        public int SourceMasterIndex { get; }

        /// <summary>どちら側の編集結果を残すか</summary>
        [PLParam(TextKey = "UnbakeWriteBackMode",
                 Description = "どちら側の編集結果を残すか", Required = true)]
        public Poly_Ling.Tools.WriteBackMode Mode { get; }

        /// <summary>
        /// 実体化前のミラー設定（MirrorType / MirrorAxis / MirrorDistance / MirrorMaterialOffset）へ
        /// 戻すか。ツール内の「一時ミラー」はモデルの恒久設定を変えてはいけないので true にする。
        /// false のときは従来どおり MirrorType = 2（結合）を強制する。
        /// </summary>
        [PLParam(TextKey = "UnbakeRestoreSavedMirrorSettings",
                 Description = "実体化前のミラー設定へ戻す。false で MirrorType = 2 を強制する")]
        public bool RestoreSavedMirrorSettings { get; }

        public UnbakeMirrorCommand(int modelIndex, int sourceMasterIndex, Poly_Ling.Tools.WriteBackMode mode)
            : this(modelIndex, sourceMasterIndex, mode, false)
        {
        }

        public UnbakeMirrorCommand(
            int modelIndex,
            int sourceMasterIndex,
            Poly_Ling.Tools.WriteBackMode mode,
            bool restoreSavedMirrorSettings)
            : base(modelIndex)
        {
            SourceMasterIndex          = sourceMasterIndex;
            Mode                       = mode;
            RestoreSavedMirrorSettings = restoreSavedMirrorSettings;
        }
    }
}
