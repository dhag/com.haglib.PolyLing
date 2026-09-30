// EditSpaceSession.cs
// 開いている作業空間 1 つぶんの状態。Viewer（PolyLingPlayerViewerCore.EditSpace.cs）が持つ。
// Runtime/Poly_Ling_Player/View/Core/EditSpace/ に配置
//
// 【対象は索引ではなく実体で持つ】
//   元・代理の描画オブジェクトは MeshContext の実体で持つ。索引は使うたびに
//   ModelContext.IndexOf で引き直す（オブジェクトの増減で位置がずれるため）。
//   実体がモデルから外れていれば、作業空間は壊れている（IsBroken）。
//
// 【パネルの表示と寿命は別】
//   右ペイン下区画でツールを切り替えても作業空間は終わらない。終わるのは Close だけ。

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditSpace;

namespace Poly_Ling.Player
{
    public sealed class EditSpaceSession
    {
        /// <summary>種類ごとの差。</summary>
        public IEditSpaceAdapter Adapter;

        /// <summary>作業空間を開いたモデル。</summary>
        public ModelContext Model;

        /// <summary>元データを持つ描画オブジェクト。</summary>
        public MeshContext Source;

        /// <summary>代理メッシュ（作業空間が作った描画オブジェクト）。</summary>
        public MeshContext Proxy;

        /// <summary>ビルボードをロック中か。</summary>
        public bool Locked = true;

        /// <summary>
        /// 平面制約が効いているか（設計方針 5.2）。ロックとは独立。
        /// 方針が Required（UV）のアダプタでは常に true。
        /// </summary>
        public bool PlaneConstrained
            => Adapter == null
            || Adapter.PlaneConstraint == EditSpacePlaneConstraint.Required
            || PlaneConstraintOn;

        /// <summary>方針が DefaultOn のアダプタで、利用者が制約を外したかどうか（既定 true）。</summary>
        public bool PlaneConstraintOn = true;

        /// <summary>対象の面の数（表示用）。</summary>
        public int FaceCount;

        // ── 開く前の状態（閉じるときに戻す） ──
        public PlayerViewportPanel PrevPanel;
        public PlayerViewport      PrevViewport;
        public bool                PrevShowBindPose;

        /// <summary>
        /// 開く前の作業ビューのカメラ。開くときに PlayerViewport.ResetToMesh で
        /// 透視（Orbit）と 3 面図の共有状態（Ortho）の両方が動くので、両方を控える。
        /// </summary>
        public PlayerViewport CameraViewport;
        public bool           HasOrbit;
        public Vector3        OrbitTarget;
        public float          OrbitDistance, OrbitRotX, OrbitRotY, OrbitRotZ;
        public bool           HasOrtho;
        public Vector3        OrthoTarget;
        public float          OrthoWorldHeightPerPixel;

        // ── 下絵（設計方針 8.2。UV は対象マテリアルのテクスチャ。保存しない） ──

        /// <summary>下絵に使うテクスチャ。無ければ null（枠だけ出す）。</summary>
        public Texture2D UnderlayTexture;

        /// <summary>下絵の出所のマテリアル番号。無ければ -1。</summary>
        public int UnderlayMaterialIndex = -1;

        /// <summary>下絵を表示するか（バーの切替）。</summary>
        public bool ShowUnderlay = true;

        /// <summary>
        /// UV を画像の縦横比で表示するか（sV = sU × 高さ ÷ 幅）。UV の作業空間だけで使う。
        /// 下絵のテクスチャが無ければ、オンでも sV = sU。
        /// </summary>
        public bool UseImageRatio;

        // ── 作業中の履歴（設計方針 9.1） ──

        /// <summary>
        /// 開いてから閉じるまでの履歴の範囲。開いている間は範囲より前を Undo しない。
        /// 閉じるときに範囲の中の記録（代理を参照する）を通常の履歴から除く。
        /// Undo コントローラが無ければ null。
        /// </summary>
        public Poly_Ling.UndoSystem.UndoScope HistoryScope;

        /// <summary>開いた時点の元オブジェクト。閉じるときに反映の累積を 1 件の記録にする前側。</summary>
        public Poly_Ling.UndoSystem.MultiMeshTopologySnapshot SourceAtOpen;

        /// <summary>作業中に反映したか（閉じるときに元の変更を記録するかの判定）。</summary>
        public bool Applied;

        /// <summary>直近の操作の結果（バーに出す）。</summary>
        public string LastMessage = "";

        /// <summary>
        /// 元・代理のどちらかがモデルから外れているか。
        /// 元データが描画オブジェクトでない種類（断面系。Source が null）は代理だけを見る。
        /// </summary>
        public bool IsBroken
            => Model == null || Proxy == null || Model.IndexOf(Proxy) < 0
            || (Source != null && Model.IndexOf(Source) < 0)
            || (Source == null && (Adapter == null || Adapter.TemporaryProxy));

        /// <summary>代理が作業中だけの一時オブジェクトか（IEditSpaceAdapter.TemporaryProxy）。</summary>
        public bool TemporaryProxy => Adapter != null && Adapter.TemporaryProxy;

        /// <summary>開く前の代理のビルボード（常設の代理を閉じるときに戻す）。</summary>
        public BillboardMode PrevProxyBillboard = BillboardMode.Off;

        public int SourceIndex => Model != null && Source != null ? Model.IndexOf(Source) : -1;
        public int ProxyIndex  => Model != null && Proxy  != null ? Model.IndexOf(Proxy)  : -1;

        /// <summary>未反映の変更があるか。壊れているときは false。</summary>
        public bool HasChanges => !IsBroken && Adapter != null && Adapter.HasChanges(Source, Proxy);
    }
}
