// PlayerViewportManager.cs
// 3つの PlayerViewport（Perspective / Top / Front）を管理し、
// MeshSceneRenderer の描画呼び出しを各カメラに対して行う。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Tools;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    // ================================================================
    // ★★★ 正規入口カテゴリ（Phase 2a）★★★
    //
    // PlayerViewportManager / MeshSceneRenderer / UnifiedSystemAdapter への
    // 外部アクセスは以下の 6 入口のみを使うこと。
    //   1. EnterProjectChanged         — プロジェクト/モデル変更
    //   2. EnterTopologyChanged        — トポロジ変更・選択変更
    //   3. EnterCameraChanged          — 視点・カメラパラメータ変更
    //   4. EnterVerticesMoved          — 頂点位置変更
    //   5. EnterHoverChanged           — ホバー状態変更
    //   6. EnterDisplaySettingsChanged — per-viewport 表示トグル
    //
    // ライフサイクル API（Initialize / Dispose / RegisterMoveToolHandler）は
    // 別グループ。純粋 getter（GetCurrentToolContext, PerspectiveViewport 等）も
    // 対象外。
    //
    // 新規入口の追加には明示的な承認が必須。
    // ================================================================

    /// <summary>カメラ操作のフェーズ。</summary>
    public enum CameraChangePhase
    {
        /// <summary>ドラッグ開始（軽量プロファイル切替）</summary>
        DragBegin,
        /// <summary>ドラッグ中の連続更新（軽量パス）</summary>
        Dragging,
        /// <summary>ドラッグ終了（確定 + 重いパイプライン）</summary>
        DragEnd,
        /// <summary>単発のカメラパラメータ確定（スクロール等、ドラッグ無関係）</summary>
        Committed,
        /// <summary>カメラ位置をリセット（ResetToMesh）</summary>
        Reset,
    }

    /// <summary>頂点移動のフェーズ。</summary>
    public enum VerticesMovedPhase
    {
        /// <summary>ドラッグ開始（軽量プロファイル切替）</summary>
        DragBegin,
        /// <summary>ドラッグ中の連続更新（軽量パス）</summary>
        Dragging,
        /// <summary>ドラッグ終了（確定）</summary>
        DragEnd,
    }

    /// <summary>ホバー対象の種類。ツールごとにホバー対象を限定することで、
    /// 辺ツール中に頂点色が変わる等の誤動作を防ぐ。</summary>
    public enum HoverTargetKind
    {
        /// <summary>ホバー表示を消す。</summary>
        None,
        /// <summary>頂点編集系ツール</summary>
        Vertex,
        /// <summary>辺編集系ツール（EdgeTopology, EdgeBevel 等）</summary>
        Edge,
        /// <summary>面編集系ツール（AddFace, FaceExtrude 等）</summary>
        Face,
        /// <summary>ボーン編集系ツール</summary>
        Bone,
        /// <summary>ツールギズモ（Move/Rotate/Scale 等のギズモ軸）</summary>
        Gizmo,
    }

    /// <summary>
    /// Perspective / Top / Front の3ビューポートを管理する。
    /// Viewer の Update / LateUpdate から対応メソッドを呼ぶこと。
    /// </summary>
    public class PlayerViewportManager
    {
        // ================================================================
        // ビューポート公開
        // ================================================================

        public PlayerViewport PerspectiveViewport { get; private set; }
        public PlayerViewport TopViewport         { get; private set; }
        public PlayerViewport FrontViewport       { get; private set; }
        public PlayerViewport SideViewport        { get; private set; }

        // ================================================================
        // 内部
        // ================================================================

        private MeshSceneRenderer _renderer;

        // AxisGizmo 用
        private MoveToolHandler    _moveToolHandler;
        /// <summary>MoveToolHandler 以外のアクティブツールの UpdateHover コールバック。</summary>
        private Action<Vector2, Poly_Ling.Tools.ToolContext> _activeToolHoverCallback;
        private bool _suppressHover;

        /// <summary>ホバー更新（頂点ハイライト等）を抑止する。SkinWeightPaint 等が OnActivate で有効化する。</summary>
        public void SetSuppressHover(bool suppress) => _suppressHover = suppress;
        private PlayerToolContext _toolCtx = new PlayerToolContext();

        // ================================================================
        // Overlay 再描画コールバック (Phase 2b-1)
        //
        // 各正規入口 (Enter*) の末尾から必要な overlay のみを発火する。
        // PolyLingPlayerViewerCore が Initialize 時に 3 つのコールバックを設定する。
        // ================================================================
        public Action OnRefreshFaceHoverOverlay;
        public Action OnRefreshSelectedFacesOverlay;
        public Action OnRefreshGizmoOverlay;
        // Phase 2c-2: ボーン位置マーカー（UIToolkit 菱形マーカー）の refresh。
        // ボーン GPU wire は Poly_Ling/Bone3D_Overlay で自動追従するため
        // refresh 不要だが、UIToolkit マーカーの CPU 投影座標は event 駆動。
        public Action OnRefreshBoneOverlay;
        // Phase 2c-3: ツール固有の UIToolkit overlay。各ハンドラが内部状態を
        // 保持し、正規入口 (Enter*) の末尾で再投影・再描画する。
        public Action OnRefreshAddFaceOverlay;
        public Action OnRefreshTopologyToolsOverlay;
        public Action OnRefreshAdvancedSelectOverlay;

        /// <summary>
        /// Phase 2a-2b-2 Batch 3: EnterSceneReset 内部から Core 側の
        /// _selectionOps.SetSelectionState を呼ぶためのコールバック。
        /// Core 初期化時に設定する。ViewportManager 自身は _selectionOps に
        /// 参照を持たないため、このコールバック経由で選択状態を Core に届ける。
        /// </summary>
        public Action<Poly_Ling.Selection.SelectionState> OnSetSelectionState;

        /// <summary>
        /// Phase 2c-3: ツール固有の UIToolkit overlay (AddFace / TopologyTools /
        /// AdvancedSelect) を一括再描画する。各正規入口の末尾から呼ばれる。
        /// 各ハンドラ内部の状態（ホバー辺、プレビュー点等）はそのままに、
        /// 視点変更や頂点移動に伴う CPU 投影だけを再実行する。
        /// </summary>
        private void RefreshToolOverlays()
        {
            OnRefreshAddFaceOverlay?.Invoke();
            OnRefreshTopologyToolsOverlay?.Invoke();
            OnRefreshAdvancedSelectOverlay?.Invoke();
        }

        // カリングスロット（ビューポートごとに独立した per-slot カリングバッファを使用）
        private const int SlotPerspective = 0;
        private const int SlotTop         = 1;
        private const int SlotFront       = 2;
        private const int SlotSide        = 3;

        // スロットごとのカメラ dirty フラグ（カメラが変化したスロットのみ再カリング）
        private readonly bool[] _slotCameraDirty = new bool[4] { true, true, true, true };

        // ================================================================
        // ビューポート表示設定（面ごと）
        // ================================================================

        private readonly ViewportDisplaySettings[] _displaySettings = LoadDisplaySettings();

        // RecentPaths から4面分の表示設定を復元（未保存/不正なら Default）。
        private static ViewportDisplaySettings[] LoadDisplaySettings()
        {
            var arr = new ViewportDisplaySettings[4];
            for (int i = 0; i < 4; i++)
            {
                string s = RecentPaths.Get(DisplaySettingsKey(i), "");
                arr[i] = int.TryParse(s, out int bits)
                    ? ViewportDisplaySettings.FromBits(bits)
                    : ViewportDisplaySettings.Default;
            }
            return arr;
        }

        private static string DisplaySettingsKey(int slot) => $"Viewport.Display.{slot}";

        /// <summary>指定スロットの表示設定を取得する。</summary>
        public ViewportDisplaySettings GetDisplaySettings(int slot) => _displaySettings[slot];

        /// <summary>指定スロットの表示設定を更新する。</summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void SetDisplaySettings(int slot, ViewportDisplaySettings s)
        {
            _displaySettings[slot] = s;
            // 表示設定を起動間で記録（write-through）。
            if (slot >= 0 && slot < 4)
                RecentPaths.Set(DisplaySettingsKey(slot), s.ToBits().ToString());
            // 表示設定（カリングON/OFF等）が変わったらカリングバッファを再計算する。
            if (slot >= 0 && slot < _slotCameraDirty.Length)
                _slotCameraDirty[slot] = true;
        }

        // LateUpdate で UpdateFrame を呼ぶための最後のカメラ参照とマウス位置。
        // NotifyCameraChanged / NotifyPointerHover で更新される。
        private Camera  _lastCamera;
        private Vector2 _lastMousePos;
        private bool    _lastParamsValid;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Initialize(Transform parent, MeshSceneRenderer renderer)
        {
            _renderer = renderer;

            PerspectiveViewport = new PlayerViewport();
            TopViewport         = new PlayerViewport();
            FrontViewport       = new PlayerViewport();
            SideViewport        = new PlayerViewport();

            PerspectiveViewport.Initialize(ViewportMode.Perspective, parent);
            TopViewport        .Initialize(ViewportMode.Top,         parent);
            FrontViewport      .Initialize(ViewportMode.Front,       parent);
            SideViewport       .Initialize(ViewportMode.Side,        parent);

            // Top / Side / Front は視点(Target/WorldHeightPerPixel)を共有して連動させる。
            var orthoShared = new OrthoViewSharedState();
            TopViewport  .Ortho?.SetSharedState(orthoShared);
            FrontViewport.Ortho?.SetSharedState(orthoShared);
            SideViewport .Ortho?.SetSharedState(orthoShared);
        }

        public void Dispose()
        {
            PerspectiveViewport?.Dispose();
            TopViewport        ?.Dispose();
            FrontViewport      ?.Dispose();
            SideViewport       ?.Dispose();
            PerspectiveViewport = null;
            TopViewport         = null;
            FrontViewport       = null;
            SideViewport        = null;
        }

        public void RegisterMoveToolHandler(MoveToolHandler handler)
        {
            _moveToolHandler = handler;
        }

        /// <summary>
        /// MoveToolHandler 以外のアクティブなツールを登録する。
        /// SwitchTool で _vertexInteractor.SetToolHandler と同時に呼ぶこと。
        /// null を渡すと MoveToolHandler のみが UpdateHover を受け取る（デフォルト）。
        /// </summary>
        public void RegisterActiveToolHandler(Action<Vector2, Poly_Ling.Tools.ToolContext> callback)
        {
            _activeToolHoverCallback = callback;
        }


        /// <summary>
        /// ビューポートに対応するカリングスロット番号を返す。該当なしは -1。
        /// </summary>
        private int ViewportToSlot(PlayerViewport vp)
        {
            if (vp == PerspectiveViewport) return SlotPerspective;
            if (vp == TopViewport)         return SlotTop;
            if (vp == FrontViewport)       return SlotFront;
            if (vp == SideViewport)        return SlotSide;
            return -1;
        }

        /// <summary>
        /// _lastCamera に対応するビューポートを返す（未確定時は PerspectiveViewport）。
        /// </summary>
        private PlayerViewport ActiveViewport
        {
            get
            {
                if (_lastCamera == null) return PerspectiveViewport;
                if (TopViewport         != null && TopViewport.Cam         == _lastCamera) return TopViewport;
                if (FrontViewport       != null && FrontViewport.Cam       == _lastCamera) return FrontViewport;
                if (SideViewport        != null && SideViewport.Cam        == _lastCamera) return SideViewport;
                return PerspectiveViewport;
            }
        }

        /// <summary>
        /// メッシュトポロジー変更・モデル切り替え時に呼ぶ。
        /// 全スロットのカリングを次フレームで強制再計算させる。
        /// </summary>
        public void MarkAllSlotsDirty()
        {
            for (int i = 0; i < _slotCameraDirty.Length; i++)
                _slotCameraDirty[i] = true;
        }

        /// <summary>
        /// Phase 2a-2g-3 Fix: 重量級入口 (EnterSceneReset / EnterTopologyChanged /
        /// EnterUndoApplied / EnterVerticesMoved DragEnd) から呼ばれる。
        /// 4 viewport 全てに対して ApplyCameraTransform を実行し、Unity Camera.transform を
        /// 最新のカメラパラメータに同期する。
        ///
        /// 背景: Phase 2a-2f で毎フレーム呼ばれていた _viewportManager.Update() が削除され、
        /// イベント駆動のみになったため、user がカメラオービットしていない viewport の
        /// Camera.transform が古い値のまま残り、新モデル表示位置にズレが発生していた。
        /// </summary>
        private void ApplyAllViewportCameraTransforms()
        {
            PerspectiveViewport?.ApplyCameraTransform();
            TopViewport        ?.ApplyCameraTransform();
            FrontViewport      ?.ApplyCameraTransform();
            SideViewport       ?.ApplyCameraTransform();
        }

        /// <summary>
        /// Top/Side/Front は Target/WorldHeightPerPixel を共有（連動）するため、
        /// いずれか1つの ortho カメラが変化したら、他2つにも Camera.transform を
        /// 反映し slot を dirty にする。呼び出し側で PresentAll される前提。
        /// changed が ortho でない（Perspective）場合は何もしない。
        /// </summary>
        private void ApplyAndDirtyLinkedOrtho(PlayerViewport changed)
        {
            if (changed == null || changed.Ortho == null) return;

            void Sync(PlayerViewport vp)
            {
                if (vp == null || vp == changed || vp.Ortho == null) return;
                vp.ApplyCameraTransform();
                int s = ViewportToSlot(vp);
                if (s >= 0) _slotCameraDirty[s] = true;
            }
            Sync(TopViewport);
            Sync(FrontViewport);
            Sync(SideViewport);
        }

        public ToolContext GetCurrentToolContext(PlayerViewport vp = null)
        {
            var target = vp ?? PerspectiveViewport;
            var cam = target?.Cam;
            if (cam == null) return null;
            _toolCtx.UpdateFromViewport(target);
            var ctx = _toolCtx.ToToolContext(cam);
            if (ctx != null) ctx.SetSuppressHover = SetSuppressHover;
            return ctx;
        }

        /// <summary>
        /// 指定カメラを持つビューポートの ToolContext を返す。
        /// Camera.onPostRender コールバックからギズモ描画に使用する。
        /// </summary>
        public ToolContext GetToolContextForCamera(Camera cam)
        {
            if (cam == null) return null;
            PlayerViewport vp;
            if      (PerspectiveViewport?.Cam == cam) vp = PerspectiveViewport;
            else if (TopViewport?.Cam         == cam) vp = TopViewport;
            else if (FrontViewport?.Cam       == cam) vp = FrontViewport;
            else if (SideViewport?.Cam        == cam) vp = SideViewport;
            else return null;
            _toolCtx.UpdateFromViewport(vp);
            return _toolCtx.ToToolContext(cam);
        }

        // ================================================================
        // 毎フレーム更新（Update から呼ぶ）
        // ================================================================

        public void Update()
        {
            PerspectiveViewport?.ApplyCameraTransform();
            TopViewport        ?.ApplyCameraTransform();
            FrontViewport      ?.ApplyCameraTransform();
            SideViewport       ?.ApplyCameraTransform();
        }

        // ================================================================
        // 描画（LateUpdate から呼ぶ）
        // ================================================================

        // ================================================================
        // ★★★ 正規入口 (Phase 2a) ★★★
        //
        // 以下 6 メソッドは PlayerViewportManager への外部アクセス唯一の入口。
        // 旧プリミティブ API (NotifyCameraChanged / NotifyPointerHover /
        // PresentAll / RebuildAdapter / SyncMeshPositionsAndTransform /
        // UpdateTransform / Enter(Camera/Transform/Box)Dragging 等) は
        // [Obsolete] でマークされる。直接呼ぶことは規約違反。
        // ================================================================

        /// <summary>
        /// カテゴリ 1: プロジェクト/モデル変更。重量級。バッファ全再構築を許容。
        /// 契機: モデルロード、プロジェクト切替、モデル破棄/初期化。
        /// </summary>
        /// <summary>
        /// カテゴリ 1: プロジェクト/モデル変更。重量級。バッファ全再構築を許容。
        /// 契機: モデルロード、プロジェクト切替、モデル破棄/初期化。
        ///
        /// Phase 2a-2b-1: 責務完備化。
        /// RebuildAdapter + UpdateSelectedDrawableMesh + PresentAll を内包する。
        /// SelectionState の初期化（SetSelectionState）はモデル固有情報を扱うため
        /// 本メソッドの責務外。呼出し側（Core の OnModelLoaded 等）で行うこと。
        /// </summary>
        public void EnterProjectChanged(ProjectContext project)
        {
#pragma warning disable CS0618
            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);
                _renderer.UpdateSelectedDrawableMesh(0, model);
            }
            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>
        /// カテゴリ 2: トポロジ変更・選択変更。バッファ再構築あり、視点不変。
        /// 契機: 選択変更、面追加、面分割、頂点削除、矩形/投げ縄選択の確定、ツール切替等。
        /// </summary>
        /// <summary>
        /// カテゴリ 2: トポロジ変更・選択変更。バッファ再構築あり、視点不変。
        /// 契機: 選択変更、面追加、面分割、頂点削除、矩形/投げ縄選択の確定、ツール切替等。
        ///
        /// Phase 2a-2b-1: 責務完備化。
        /// 従来、呼出し側で _viewportManager.RebuildAdapter → _renderer.UpdateSelectedDrawableMesh →
        /// NotifyPanels の連鎖を手書きしていたが、本メソッドに内包する。
        /// 呼出し側は EnterTopologyChanged(proj) 一発で済むようにする。
        /// </summary>
        public void EnterTopologyChanged(ProjectContext project)
        {
#pragma warning disable CS0618
            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);
                _renderer.UpdateSelectedDrawableMesh(0, model);
            }
            // Phase 2a-2g-3 Fix: トポロジ変更は全 viewport のカリング再計算が必要。
            // 図形生成・モデルロード・面追加/削除等で Perspective 以外も更新されるようにする。
            MarkAllSlotsDirty();
            ApplyAllViewportCameraTransforms();
            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>スキンウェイト可視化のターゲットボーン変更時: 色再計算＋全 viewport 再描画。</summary>
        public void EnterWeightTargetChanged(ProjectContext project)
        {
            PresentAll(project);
        }

        /// <summary>
        /// カテゴリ 3: 視点・カメラパラメータ変更。トポロジ・頂点位置不変。
        /// 契機: カメラオービット、パン、ズーム、ResetToMesh。
        /// </summary>
        public void EnterCameraChanged(PlayerViewport vp, CameraChangePhase phase, Bounds? resetBounds = null)
        {
            if (vp == null) return;
#pragma warning disable CS0618
            switch (phase)
            {
                case CameraChangePhase.DragBegin:
                    EnterCameraDragging();
                    // DragBegin は overlay 更新不要（カメラはまだ動いていない）。
                    return;
                case CameraChangePhase.Dragging:
                    NotifyCameraMoved(vp);
                    break;
                case CameraChangePhase.DragEnd:
                    ExitCameraDragging();
                    NotifyCameraChanged(vp);
                    break;
                case CameraChangePhase.Committed:
                    NotifyCameraChanged(vp);
                    break;
                case CameraChangePhase.Reset:
                    if (resetBounds.HasValue) ResetToMesh(resetBounds.Value);
                    NotifyCameraChanged(vp);
                    break;
            }
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>
        /// カテゴリ 4: 頂点位置変更。トポロジ不変、視点不変。
        /// 契機: Move/Rotate/Scale ツール、ボーンポーズ変更。
        /// </summary>
        /// <param name="project">現在のプロジェクト</param>
        /// <param name="phase">ドラッグフェーズ</param>
        /// <param name="syncMc">
        /// Dragging フェーズで特定メッシュのみ位置同期する場合に指定。
        /// 指定時は SyncMeshPositionsAndTransform + UpdateTransform の軽量経路を実行。
        /// null の場合は PresentAll で全 slot Prepare を実行する。
        /// </param>
        public void EnterVerticesMoved(
            ProjectContext project,
            VerticesMovedPhase phase,
            Poly_Ling.Data.MeshContext syncMc = null)
        {
#pragma warning disable CS0618
            switch (phase)
            {
                case VerticesMovedPhase.DragBegin:
                    EnterTransformDragging();
                    break;
                case VerticesMovedPhase.Dragging:
                    if (syncMc != null)
                    {
                        // MoveToolHandler 等の頂点ドラッグ中の軽量同期経路。
                        SyncMeshPositionsAndTransform(syncMc, project?.CurrentModel);
                        UpdateTransform();
                        // 面は位置バッファ直参照で追随するが、線Mesh(_wireframeMesh*)・
                        // 点Mesh(_pointMesh*)は焼き込み座標のため取り残される。ここで軽量更新
                        // （トポロジ不変・座標のみ SetVertices）して同フレームで追随させる。
                        // フル再構築は 1FPS 地雷のため使わない。
                        var uniRenderer = _renderer?.GetAdapter(0)?.Renderer;
                        if (uniRenderer != null)
                        {
                            uniRenderer.UpdateWireframePositionsOnly();
                            uniRenderer.UpdatePointPositionsOnly();
                        }
                    }
                    else
                    {
                        PresentAll(project);
                    }
                    break;
                case VerticesMovedPhase.DragEnd:
                    ExitTransformDragging();
                    // Phase 2a-2g-3 Fix: ドラッグ終了時は全 viewport のカリング再計算が必要。
                    MarkAllSlotsDirty();
                    ApplyAllViewportCameraTransforms();
                    PresentAll(project);
                    break;
            }
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>
        /// カテゴリ 5: ホバー状態変更。トポロジ不変、視点不変、頂点位置不変。
        /// 契機: マウスポインタ移動。kind でホバー対象を限定。
        /// </summary>
        public void EnterHoverChanged(PlayerViewport vp, Vector2 mousePos, HoverTargetKind kind)
        {
            if (vp == null) return;
#pragma warning disable CS0618
            if (kind == HoverTargetKind.None)
            {
                ClearMouseHover();
            }
            else
            {
                // Phase 2a 暫定: kind による分岐は Phase 2b 以降で実装。
                NotifyPointerHover(vp, mousePos);
            }
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>
        /// カテゴリ 6: per-viewport 表示設定変更。
        /// 契機: 表示トグル（面/辺/頂点/ボーン ON/OFF、BackfaceCulling ON/OFF）。
        /// Phase 2b-1: overlay 更新は現状なし（別途検討）。
        /// </summary>
        public void EnterDisplaySettingsChanged(int slot, ViewportDisplaySettings ds)
        {
#pragma warning disable CS0618
            SetDisplaySettings(slot, ds);
            PresentAll(_lastProjectForPresent);
#pragma warning restore CS0618
        }

        // ================================================================
        // 【重量級専用入口】Phase 2a-2b-2 Batch 3 で追加
        //
        // ★★★ 使用前に必ず読むこと ★★★
        //
        // 以下の 2 入口は通常の編集操作からは絶対に呼ばないこと。
        // カテゴリ 1〜6 の通常 Enter* (EnterProjectChanged / EnterTopologyChanged /
        // EnterCameraChanged / EnterVerticesMoved / EnterHoverChanged /
        // EnterDisplaySettingsChanged) のいずれかで対応できる場合はそちらを使う。
        //
        // これらは「シーン丸ごと再構築」「Undo 適用」という特殊重量級処理専用で、
        // 安易に使うとパフォーマンス低下や順序依存バグの温床になる。
        // 新規箇所で使う前に既存 6 入口で代替できないか必ず検討すること。
        // ================================================================

        /// <summary>
        /// ★★★【重量級専用・カテゴリ 7: シーン全体リセット】★★★
        ///
        /// 使用場面（以下の特殊経路限定）:
        ///   - モデル外部ロード完了直後 (LoadModel 完了後)
        ///   - CSV マージ / 部分インポート / MeshFilter→Skinned 変換完了後
        ///   - 図形生成 (Primitive) 完了後
        ///   - プロジェクト切替直後
        ///   - 空 MeshContext 追加直後 (EnsureDrawableMesh)
        ///
        /// 使用してはならない場面:
        ///   - 通常のトポロジ変更 (選択、面追加、頂点削除等) → EnterTopologyChanged
        ///   - 頂点移動・ボーンポーズ変更 → EnterVerticesMoved
        ///   - カメラ操作 → EnterCameraChanged
        ///
        /// 責務:
        ///   1. clearScene=true のとき _renderer.ClearScene()
        ///   2. model.FirstDrawableMeshContext.Selection を Core と renderer に設定
        ///      (OnSetSelectionState コールバック経由で Core の _selectionOps に届ける)
        ///   3. RebuildAdapter + UpdateSelectedDrawableMesh
        ///   4. PresentAll + overlay refresh
        ///
        /// カメラは責務外。必要なら別途 EnterCameraChanged を呼ぶこと。
        /// </summary>
        /// <param name="project">現在のプロジェクト</param>
        /// <param name="clearScene">
        /// true: _renderer.ClearScene() を呼ぶ (CSV マージ / 部分インポート等)。
        /// false: ClearScene を呼ばない (通常のモデルロード / 図形生成等)。
        /// </param>
        public void EnterSceneReset(ProjectContext project, bool clearScene = false)
        {
#pragma warning disable CS0618
            if (clearScene) _renderer?.ClearScene();

            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);

                // first MeshContext の Selection をアクティブ化
                var firstMc = model.FirstDrawableMeshContext;
                if (firstMc != null)
                {
                    OnSetSelectionState?.Invoke(firstMc.Selection);
                    _renderer.SetSelectionState(firstMc.Selection);
                }

                _renderer.UpdateSelectedDrawableMesh(0, model);
            }

            // Phase 2a-2g-3 Fix: シーンリセット時は全 slot のカメラ dirty を立てる。
            // これにより PresentAll 内の PrepareViewport で全 4 viewport に対して
            // DispatchCullingForDisplay が実行される。
            // (EnterCameraChanged は単一 vp のみ dirty にするため、図形生成やインポート後は
            // Perspective 以外のビューポートが更新されない問題があった)
            MarkAllSlotsDirty();
            // 4 viewport 全てのカメラ Transform を最新値に同期（_Tick 削除の代替）。
            ApplyAllViewportCameraTransforms();

            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        /// <summary>
        /// ★★★【重量級専用・カテゴリ 8: Undo/Redo 適用】★★★
        ///
        /// 使用場面（Undo/Redo 経路限定）:
        ///   - Undo スタックから構造変更レコードを適用した直後
        ///   - project.CurrentModel と異なる model が Undo スタックから取得される場合に対応
        ///
        /// 使用してはならない場面:
        ///   - 通常のトポロジ変更 → EnterTopologyChanged
        ///   - Undo 適用でも model が project.CurrentModel と同じとき → EnterTopologyChanged で十分
        ///
        /// 責務:
        ///   1. 引数で渡された model (project.CurrentModel とは別の可能性) で
        ///      RebuildAdapter + UpdateSelectedDrawableMesh を実行
        ///   2. PresentAll(project)
        ///   3. overlay refresh
        ///
        /// SetSelectionState は Undo 経路では Selection も Undo 対象として復元済みのため呼ばない。
        /// </summary>
        /// <param name="project">現在のプロジェクト (PresentAll に渡す)</param>
        /// <param name="model">Undo スタックから取得した ModelContext (project.CurrentModel と異なり得る)</param>
        public void EnterUndoApplied(ProjectContext project, Poly_Ling.Context.ModelContext model)
        {
#pragma warning disable CS0618
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);
                _renderer.UpdateSelectedDrawableMesh(0, model);
            }
            // Phase 2a-2g-3 Fix: Undo 適用は全 viewport のカリング再計算が必要。
            MarkAllSlotsDirty();
            ApplyAllViewportCameraTransforms();
            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshFaceHoverOverlay?.Invoke();
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        // ================================================================
        // 描画 (Phase 1: PresentAll + SubmitForCamera の event 駆動 + OnRenderObject 分離)
        //
        // ・PresentAll(project):    event 駆動で呼ぶ。計算・Prepare を全 slot 実行。
        // ・SubmitForCamera(cam, project): OnRenderObject() から毎フレーム呼ぶ。
        //                                  Graphics.DrawMesh 提出のみ。
        // ================================================================

        /// <summary>
        /// ★★★ 【重大規約違反コード: 旧 API】 ★★★
        /// 旧 LateTick 経路から毎フレーム呼ばれていた実装。
        /// 「毎フレームポーリング禁止」規約に違反する。
        /// Phase 1 では暫定互換のため残置するが、新規コードから呼ぶことは厳禁。
        /// 代わりに以下を使うこと:
        ///   - 計算・準備:     PresentAll(project)        (event 駆動)
        ///   - 描画提出:       SubmitForCamera(cam, proj) (OnRenderObject 経路)
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void LateUpdate(ProjectContext project)
        {
            if (_renderer == null) return;

            // 毎フレーム RequestNormal + UpdateFrame を呼ぶ。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    var rect = new Rect(0, 0, _lastCamera.pixelWidth, _lastCamera.pixelHeight);
                    adapter.RequestNormal();
                    adapter.UpdateFrame(_lastCamera, rect, _lastMousePos);
                }
            }

            // アクティブビューポートを最後に描画することで、
            // ComputeScreenPositionsGPU の CPU 読み戻し結果が
            // GetScreenPositions() から取得できる値になる。
            DrawViewport(project, PerspectiveViewport, SlotPerspective);
            DrawViewport(project, TopViewport,         SlotTop);
            DrawViewport(project, FrontViewport,       SlotFront);
            DrawViewport(project, SideViewport,        SlotSide);

            // アクティブビューポートのスクリーン座標を最終確定させる。
            // CommitBoxSelect 等の CPU 側処理が GetScreenPositions() を参照するため。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer?.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    int activeSlot = ViewportToSlot(ActiveViewport);
                    if (activeSlot >= 0)
                        adapter.DispatchCullingForDisplay(_lastCamera, adapter.BackfaceCullingEnabled, activeSlot);
                }
            }
        }

        // Phase 1: 最後に PresentAll に渡された ProjectContext を保持。
        // NotifyCameraChanged 等の event ハンドラ内で project 引数なしで PresentAll を
        // 再呼出しするために使用する。
        private ProjectContext _lastProjectForPresent;

        /// <summary>
        /// 【event 駆動で呼ぶ】全 slot の描画準備（計算・Prepare）を一括実行する。
        /// カメラ操作・選択変更・トポロジ変更・モデルロード・ボーンポーズ変更等の
        /// 各イベントから呼び出される想定。毎フレーム呼ぶのは禁止。
        /// 実行内容:
        ///   - RequestNormal / UpdateFrame (_lastCamera 基準)
        ///   - 4 slot 分の PrepareViewport（display settings 適用・カリング・Mesh 再構築・Queue 登録）
        ///   - アクティブ slot の最終カリング Dispatch（CommitBoxSelect 用のスクリーン座標確定）
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void PresentAll(ProjectContext project)
        {
            if (_renderer == null) return;
            _lastProjectForPresent = project;

            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    var rect = new Rect(0, 0, _lastCamera.pixelWidth, _lastCamera.pixelHeight);
                    adapter.RequestNormal();
                    adapter.UpdateFrame(_lastCamera, rect, _lastMousePos);
                }
            }

            PrepareViewport(project, PerspectiveViewport, SlotPerspective);
            PrepareViewport(project, TopViewport,         SlotTop);
            PrepareViewport(project, FrontViewport,       SlotFront);
            PrepareViewport(project, SideViewport,        SlotSide);

            // アクティブビューポートのスクリーン座標を最終確定させる。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer?.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    int activeSlot = ViewportToSlot(ActiveViewport);
                    if (activeSlot >= 0)
                        adapter.DispatchCullingForDisplay(_lastCamera, adapter.BackfaceCullingEnabled, activeSlot);
                }
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// 与えられたカメラから slot を判定し、当該 slot の面・辺・頂点・ボーン・
        /// ウェイト可視化を Graphics.DrawMesh で提出する。
        /// 計算処理（Mesh 再構築・バッファ更新・Dispatch 等）は一切禁止。
        /// 全ての準備は PresentAll() で完了させておくこと。
        /// ただし、面描画用 Cull 判定に必要な per-slot 表示設定の renderer への
        /// 反映のみ、ここで行う（フィールド代入のみで計算なし）。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitForCamera(Camera cam, ProjectContext project)
        {
            if (_renderer == null || cam == null || project == null) return;

            int slot = CameraToSlot(cam);
            if (slot < 0) return;

            // per-slot 表示設定を renderer に反映（SubmitMeshes が _Cull 判定に使用）
            var ds = _displaySettings[slot];
            _renderer.BackfaceCullingEnabled    = ds.BackfaceCulling;
            _renderer.ShowSelectedMesh          = ds.ShowSelectedMesh;
            _renderer.ShowSelectedWireframe     = ds.ShowSelectedWireframe;
            _renderer.ShowSelectedVertices      = ds.ShowSelectedVertices;
            _renderer.ShowSelectedBone          = ds.ShowSelectedBone;
            _renderer.ShowUnselectedMesh        = ds.ShowUnselectedMesh;
            _renderer.ShowUnselectedWireframe   = ds.ShowUnselectedWireframe;
            _renderer.ShowUnselectedVertices    = ds.ShowUnselectedVertices;
            _renderer.ShowUnselectedBone        = ds.ShowUnselectedBone;
            _renderer.ShowSelectedMirror        = ds.ShowSelectedMirror;
            _renderer.ShowUnselectedMirror      = ds.ShowUnselectedMirror;

            _renderer.SubmitMeshes(project, cam);
            _renderer.SubmitWireframeAndVertices(cam, slot);
            _renderer.SubmitBones(project, cam);
            _renderer.SubmitWeightVisualization(project, cam);
        }

        /// <summary>
        /// カメラ → slot index 変換。見つからなければ -1。
        /// </summary>
        private int CameraToSlot(Camera cam)
        {
            if (PerspectiveViewport?.Cam == cam) return SlotPerspective;
            if (TopViewport        ?.Cam == cam) return SlotTop;
            if (FrontViewport      ?.Cam == cam) return SlotFront;
            if (SideViewport       ?.Cam == cam) return SlotSide;
            return -1;
        }

        // ================================================================
        // ホバー・カメラ更新（イベント駆動）
        // ================================================================

        /// <summary>
        /// カメラパラメータが確定したときに呼ぶ。
        /// 対象ビューポートの全アダプターに UpdateFrame を1回実行する。
        ///
        /// 【いつ呼ぶか】
        ///   - OrbitCameraController.OnCameraChanged（ドラッグ終了・ResetToMesh後）
        ///   - OrthoViewController.OnCameraChanged（パン・ズーム終了後）
        ///
        /// 【なぜ毎フレームでないか】
        ///   UpdateFrame はGPUヒットテストパイプライン全体を走らせる重い処理。
        ///   パラメータが変化したタイミングだけで十分。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void NotifyCameraChanged(PlayerViewport vp)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // Phase 1: Tick 廃止で ApplyCameraTransform が毎フレーム呼ばれなくなったため、
            // このタイミングで明示的に Unity Camera.transform へ反映する。
            vp.ApplyCameraTransform();

            var rect = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);
            var dummyMouse = new Vector2(rect.width * 0.5f, rect.height * 0.5f);

            _lastCamera  = cam;
            if (!_lastParamsValid) _lastMousePos = dummyMouse;
            _lastParamsValid = true;

            // カメラが変化したスロットを dirty にする（次 PresentAll で再カリング）
            int slot = ViewportToSlot(vp);
            if (slot >= 0) _slotCameraDirty[slot] = true;

            // Top/Side/Front 連動：ortho の場合は他の連動 slot も反映＋dirty。
            ApplyAndDirtyLinkedOrtho(vp);

            _toolCtx.UpdateFromViewport(vp);

            adapter.RequestNormal();
            adapter.UpdateFrame(cam, rect, _lastMousePos);

            // Phase 1 event 配線: カメラ操作イベント → 全 slot 分の Prepare 再実行。
            PresentAll(_lastProjectForPresent);
        }

        /// <summary>
        /// 【event 駆動で呼ぶ】カメラドラッグ中の軽量更新版。
        /// ApplyCameraTransform で Unity Camera.transform を更新し、
        /// 該当 slot を dirty にマークして PresentAll で描画キューを再構築する。
        /// UpdateFrame / RequestNormal 等の重い処理は呼ばない（ドラッグ終了時の
        /// NotifyCameraChanged で 1 回だけ実行する）。
        ///
        /// 【いつ呼ぶか】
        ///   - OrbitCameraController.OnCameraDragging（ドラッグ中連続）
        ///   - OrthoViewController.OnCameraDragging（ドラッグ中連続）
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void NotifyCameraMoved(PlayerViewport vp)
        {
            if (vp == null || !vp.IsReady) return;
            if (_renderer == null) return;

            vp.ApplyCameraTransform();

            int slot = ViewportToSlot(vp);
            if (slot >= 0) _slotCameraDirty[slot] = true;

            // Top/Side/Front 連動：ortho の場合は他の連動 slot も反映＋dirty。
            ApplyAndDirtyLinkedOrtho(vp);

            PresentAll(_lastProjectForPresent);
        }

        /// <summary>
        /// マウスが指定ビューポートのRenderTexture内を移動したときに呼ぶ。
        /// UpdateFrame を実行してカメラパラメータ設定 + GPU ヒットテストを一括実行する。
        ///
        /// 【いつ呼ぶか】
        ///   PlayerViewportPanel.OnPointerHover（UIToolkit PointerMoveEvent）。
        ///   UIToolkit はそのパネル内にポインターがあるときだけイベントを発火するので、
        ///   ボタン等の別パネル上では自然に発火しない → RenderTexture 内限定を保証。
        ///
        /// 【UpdateHoverOnly を使わない理由】
        ///   UpdateHoverOnly（cpuOnly=true）は CPU 版ヒットテストのみを実行し、
        ///   GPU の頂点フラグバッファには書き込まない。
        ///   そのためホバーハイライトが描画に反映されない。
        ///   UpdateFrame はフルパイプラインを実行しホバー色も GPU バッファに書き込む。
        ///   Editor 側も Repaint のたびに UpdateFrame を呼んでいる（同様に遅さを許容）。
        ///
        /// 【座標系について】
        ///   panelLocalPos は UIToolkit のパネルローカル座標（Y=0が上）。
        ///   UpdateFrame 内部の SetHitTestInput が期待する座標系と一致する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void NotifyPointerHover(PlayerViewport vp, Vector2 panelLocalPos)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            var rect = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);

            // パラメータを保存（LateUpdate の UpdateFrame で使い回す）
            _lastCamera      = cam;
            _lastMousePos    = panelLocalPos;
            _lastParamsValid = true;

            // ポインター移動時は RequestNormal → UpdateFrame でフルパイプライン実行。
            _toolCtx.UpdateFromViewport(vp);

            // ヒットテストに使う BackfaceCullingEnabled をこのビューポートの設定に同期する。
            // DrawViewport は複数ビューポートを順番に処理するため、最後に処理した
            // ビューポートの設定が adapter に残ってしまう。
            int hoverSlot = ViewportToSlot(vp);
            if (hoverSlot >= 0)
                adapter.BackfaceCullingEnabled = _displaySettings[hoverSlot].BackfaceCulling;

            if (_moveToolHandler != null && !_suppressHover)
                _moveToolHandler.UpdateHover(panelLocalPos, _toolCtx.ToToolContext(cam));
            // アクティブなツールが MoveToolHandler 以外の場合、そちらにも通知する
            if (!_suppressHover)
                _activeToolHoverCallback?.Invoke(panelLocalPos, _toolCtx.ToToolContext(cam));

            adapter.RequestNormal();
            adapter.UpdateFrame(cam, rect, panelLocalPos);

            // Phase 1 event 配線: ホバー状態変更 → 描画キュー再構築。
            // UpdateFrame で GPU フラグバッファは更新されるが、CPU 側の頂点色キャッシュは
            // Prepare 系を通さないと反映されないため、ここで PresentAll を呼ぶ。
            PresentAll(_lastProjectForPresent);
        }

        /// <summary>
        /// 矩形選択確定前に背面カリングフラグをGPU→CPUへ読み戻す。
        ///
        /// 【いつ呼ぶか】
        ///   MoveToolHandler.OnLeftDragEnd で矩形選択が確定する直前。
        ///   ReadBack後に IsVertexVisible() で背面頂点を除外できる。
        ///
        /// 【読み戻す内容】
        ///   - VertexFlags (画面外判定含む全フラグ)
        ///   - VertexCulled (表面の面に属すかの per-slot カリング結果、アクティブ slot)
        /// </summary>
        public void ReadBackVertexFlags()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return;
            // 矩形選択はアクティブビューポートで行うのでそのスロットを読む。
            int slot = ViewportToSlot(ActiveViewport);
            if (slot < 0) slot = 0;
            // カリング判定を「選択中ビューポートの設定」に固定する。
            // adapter.BackfaceCullingEnabled は複数ビューポート描画ループで最後のスロット値に
            // 上書きされるため、これを再設定しないと IsVertexVisible の早期判定
            // (!BackfaceCullingEnabled) が別ビューポートの設定を読み、カリング解除しても裏が拾えない。
            adapter.BackfaceCullingEnabled = _displaySettings[slot].BackfaceCulling;
            adapter.ReadBackVertexFlags();
            adapter.ReadBackVertexCulled(slot);
        }

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ClearMouseHover()
        {
            _renderer?.GetAdapter(0)?.ClearMouseHover();
        }

        /// <summary>
        /// 選択確定・操作確定後にGPUバッファ更新を1回要求する。
        ///
        /// 【いつ呼ぶか】
        ///   - 矩形選択確定後
        ///   - クリック選択確定後
        ///   - 頂点移動ドラッグ終了後
        ///
        /// 【ワンショット方式】
        ///   RequestNormal() で Normal モードに昇格 → 次の PrepareDrawing で
        ///   全フラグ再計算 → ConsumeNormalMode() で Idle に自動降格。
        ///   これにより「何もしていない時」は重い処理が走らない。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void RequestNormal()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.RequestNormal();
        }

        /// <summary>
        /// 頂点ドラッグ開始を通知する。
        /// アダプターを TransformDragging モードに切り替える。
        ///
        /// TransformDragging 中は UpdateFrame のガードが有効になり、
        /// ヒットテスト・GPU可視性計算・メッシュ再構築がスキップされる。
        /// 位置更新は軽量パス（ProcessTransformUpdate）で行う。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void EnterTransformDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterTransformDragging();
        }

        /// <summary>
        /// 頂点ドラッグ終了を通知する。
        /// アダプターを Normal モード（1フレーム）→ Idle に戻す。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ExitTransformDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitTransformDragging();
        }

        /// <summary>
        /// カメラ姿勢変更開始を通知する（オービット・パン）。
        /// アダプターを CameraDragging モードに切り替える。
        /// CameraDragging 中は AllowUnselectedOverlay=false になり、
        /// 非選択メッシュの頂点・辺描画が抑止される。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void EnterCameraDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterCameraDragging();
        }

        /// <summary>
        /// カメラ姿勢変更終了を通知する。
        /// アダプターを Normal モード（1フレーム）→ Idle に戻す。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ExitCameraDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitCameraDragging();
        }

        /// <summary>
        /// 矩形選択ドラッグ開始を通知する。
        /// ホバー・ヒットテストが不要なため CameraDragging モードで代用する。
        ///
        /// 【なぜ CameraDragging か】
        ///   矩形選択専用モードは存在しないが、CameraDragging は
        ///   AllowHitTest=false / AllowMeshRebuild=false のプロファイルで
        ///   「重い処理を全スキップ」を意味するため意図に合致する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void EnterBoxSelecting()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterCameraDragging();
        }

        /// <summary>
        /// 矩形選択ドラッグ終了を通知する。
        /// CameraDragging を終了して Normal モードに戻す。
        /// その後 ReadBackVertexFlags() + RequestNormal() を呼ぶこと。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ExitBoxSelecting()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitCameraDragging();
        }

        // ================================================================
        // 矩形選択用 GPU データ取得（MoveToolHandler コールバック用）
        // ================================================================

        /// <summary>
        /// GPU が計算したスクリーン座標配列（全頂点グローバルインデックス）を返す。
        /// ReadBackVertexFlags() の後に呼ぶこと。
        ///
        /// MoveToolHandler.GetScreenPositions コールバックに設定して使う。
        /// </summary>
        public Vector2[] GetScreenPositions()
        {
            return _renderer?.GetAdapter(0)?.BufferManager?.GetScreenPositions();
        }

        /// <summary>
        /// 指定コンテキストインデックスのメッシュの頂点グローバルオフセットを返す。
        /// GetScreenPositions() の配列インデックス計算に使う。
        ///
        /// MoveToolHandler.GetVertexOffset コールバックに設定して使う。
        /// </summary>
        public int GetVertexOffset(int meshContextIndex)
        {
            return _renderer?.GetAdapter(0)?.GetVertexOffset(meshContextIndex) ?? 0;
        }

        /// <summary>
        /// グローバル頂点インデックスが背面カリングで可視かどうかを返す。
        /// ReadBackVertexFlags() の後に有効。
        ///
        /// 内部で IsVertexCulled(meshIndex, localIndex) を使う。
        /// グローバルインデックスからメッシュ・ローカルインデックスへの変換を行う。
        ///
        /// MoveToolHandler.IsVertexVisible コールバックに設定して使う。
        /// </summary>
        public bool IsVertexVisible(int globalVertexIndex)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return true;
            if (!adapter.BackfaceCullingEnabled) return true;

            // グローバルインデックス → メッシュインデックス + ローカルインデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertexIndex, out int meshIdx, out int localIdx) == true)
            {
                // IsVertexBackfaceCulled は ReadBackVertexCulled でキャッシュされた
                // _VertexCulledBuffer (per-slot) の結果を参照する。
                // IsVertexCulled (_vertexFlags & FLAG_CULLED) は画面外用なので使わない。
                return !adapter.IsVertexBackfaceCulled(meshIdx, localIdx);
            }
            return true;
        }

        /// <summary>
        /// GPU ホバー結果を PlayerHoverElement（種別付き）に変換して返す。
        /// SelectionState.Mode に応じた要素種別を返す。
        ///
        /// 優先順位: 頂点 > 辺/補助線分 > 面
        /// （Mode で無効な種別はスキップ）
        /// </summary>
        public PlayerHoverElement GetHoverElement(
            Poly_Ling.Selection.MeshSelectMode mode,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHoverElement.None;

            // ---- 頂点 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Vertex))
            {
                int gv = adapter.HoverVertexIndex;
                if (gv >= 0 && adapter.BufferManager?.GlobalToLocalVertexIndex(
                        gv, out int vm, out int vl) == true)
                {
                    int ctxV = UnifiedToContextIndex(adapter, model, vm);
                    if (ctxV >= 0)
                        return new PlayerHoverElement
                        {
                            Kind        = PlayerHoverKind.Vertex,
                            MeshIndex   = ctxV,
                            VertexIndex = vl,
                        };
                }
            }

            // ---- 辺 / 補助線分 ----
            bool wantEdge = mode.Has(Poly_Ling.Selection.MeshSelectMode.Edge);
            bool wantLine = mode.Has(Poly_Ling.Selection.MeshSelectMode.Line);
            if (wantEdge || wantLine)
            {
                int gl = adapter.HoverLineIndex;
                if (gl >= 0 && adapter.BufferManager?.GlobalToLocalLineIndex(
                        gl, out int lm, out int ll) == true)
                {
                    int ctxL = UnifiedToContextIndex(adapter, model, lm);
                    if (ctxL >= 0)
                    {
                        // IsAuxLine 判定
                        var lines = adapter.BufferManager.Lines;
                        bool isAux = gl < lines.Length && lines[gl].IsAuxLine;

                        if (isAux && wantLine)
                        {
                            // 補助線分。FaceIndex はグローバル面インデックス
                            // → ローカル面インデックスに変換
                            uint gfi = gl < lines.Length ? lines[gl].FaceIndex : uint.MaxValue;
                            int localFaceIdx = -1;
                            if (gfi != uint.MaxValue)
                                adapter.BufferManager.GlobalToLocalFaceIndex(
                                    (int)gfi, out _, out localFaceIdx);
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Line,
                                MeshIndex = ctxL,
                                FaceIndex = localFaceIdx,
                            };
                        }
                        else if (!isAux && wantEdge)
                        {
                            // 辺。V1/V2 をローカルインデックスに変換
                            var meshInfos = adapter.BufferManager.MeshInfos;
                            uint vStart = lm < meshInfos.Length
                                ? meshInfos[lm].VertexStart : 0u;
                            uint gv1 = lines[gl].V1;
                            uint gv2 = lines[gl].V2;
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Edge,
                                MeshIndex = ctxL,
                                EdgeV1    = (int)(gv1 - vStart),
                                EdgeV2    = (int)(gv2 - vStart),
                            };
                        }
                    }
                }
            }

            // ---- 面 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Face))
            {
                int gf = adapter.HoverFaceIndex;
                if (gf >= 0 && adapter.BufferManager?.GlobalToLocalFaceIndex(
                        gf, out int fm, out int fl) == true)
                {
                    int ctxF = UnifiedToContextIndex(adapter, model, fm);
                    if (ctxF >= 0)
                        return new PlayerHoverElement
                        {
                            Kind      = PlayerHoverKind.Face,
                            MeshIndex = ctxF,
                            FaceIndex = fl,
                        };
                }
            }

            return PlayerHoverElement.None;
        }

        // unified メッシュインデックス → context インデックス 変換ヘルパー
        private static int UnifiedToContextIndex(
            Poly_Ling.Core.UnifiedSystemAdapter adapter,
            Poly_Ling.Context.ModelContext model,
            int unifiedIdx)
        {
            if (model == null) return -1;
            for (int ci = 0; ci < model.Count; ci++)
                if (adapter.ContextToUnifiedMeshIndex(ci) == unifiedIdx)
                    return ci;
            return -1;
        }

        /// <summary>
        /// 通常面描画パイプライン (UnifiedBufferManager_Update.ComputeScreenPositions) と
        /// 同じ cam.pixelWidth / cam.pixelHeight (RenderTexture 解像度) 基準で投影する共通ヘルパー。
        /// cam.WorldToScreenPoint は Screen.width/Screen.height (メインディスプレイ解像度) を
        /// 使うため RenderTexture カメラでは panel サイズと不整合になる (ウィンドウ拡大時に
        /// overlay 座標がずれる)。これを回避するため cam.pixelWidth / cam.pixelHeight
        /// に対して直接射影する。
        /// 返す座標は cam.WorldToScreenPoint と同じ系 (Y=0 が下)。
        /// Painter2D 側 (OnGenerateFaceOverlay) は panelH - y の反転を行って UIToolkit 系 (Y=0 が上) に変換する。
        /// </summary>
        /// <returns>投影成功時はスクリーン座標、カメラ背面の場合は NaN を含む Vector2。</returns>
        private static Vector2 ProjectWorldToCameraScreen(Camera cam, Vector3 worldPos)
        {
            Matrix4x4 vpMat = cam.projectionMatrix * cam.worldToCameraMatrix;
            Vector4 clip = vpMat * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);
            if (clip.w <= 0f) return new Vector2(float.NaN, float.NaN);
            float ndcX = clip.x / clip.w;
            float ndcY = clip.y / clip.w;
            float screenX = (ndcX * 0.5f + 0.5f) * cam.pixelWidth;
            // Unity の cam.WorldToScreenPoint と同じ系 (Y=0 が下)。
            // Painter2D 側で panelH - y により反転される前提。
            float screenY = (ndcY * 0.5f + 0.5f) * cam.pixelHeight;
            return new Vector2(screenX, screenY);
        }

        /// <summary>
        /// ホバー中の面のスクリーン座標を返す。
        /// 頂点位置は GPU DisplayPositions（GetDisplayPositions）を参照する。
        /// 座標投影は通常面描画パイプラインと同じ cam.pixelWidth/pixelHeight 基準で行う。
        /// </summary>
        public Vector2[] GetHoverFaceScreenPts(
            PlayerViewport vp, Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return null;
            int gf = adapter.HoverFaceIndex;
            if (gf < 0) return null;
            if (adapter.BufferManager?.GlobalToLocalFaceIndex(gf, out int mi, out int lf) != true) return null;
            int ci = UnifiedToContextIndex(adapter, model, mi);
            if (ci < 0) return null;
            var mc = model.GetMeshContext(ci);
            if (mc?.MeshObject == null || lf < 0 || lf >= mc.MeshObject.FaceCount) return null;
            var face = mc.MeshObject.Faces[lf];
            if (face.VertexCount < 3) return null;
            var cam = vp?.Cam;
            if (cam == null) return null;

            // GPU 計算済みの最新ワールド座標を取得（頂点ドラッグ・ボーン変形反映済み）。
            UnityEngine.Vector3[] worldPositions = null;
            int vertexStart = 0;
            var bm = adapter.BufferManager;
            if (bm != null)
            {
                var meshInfos = bm.MeshInfos;
                if (meshInfos != null && mi < meshInfos.Length)
                {
                    vertexStart = (int)meshInfos[mi].VertexStart;
                    worldPositions = bm.GetDisplayPositions();
                }
            }
            int totalVertexCount = worldPositions?.Length ?? 0;

            var pts = new Vector2[face.VertexCount];
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vi = face.VertexIndices[i];
                if (vi < 0 || vi >= mc.MeshObject.VertexCount) return null;

                UnityEngine.Vector3 worldPos;
                int globalIdx = vertexStart + vi;
                if (worldPositions != null && globalIdx < totalVertexCount)
                    worldPos = worldPositions[globalIdx];
                else
                    worldPos = mc.WorldMatrix.MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);

                Vector2 sp = ProjectWorldToCameraScreen(cam, worldPos);
                if (float.IsNaN(sp.x)) return null;
                pts[i] = sp;
            }
            return pts;
        }

        /// <summary>選択面のスクリーン座標リストを返す。</summary>
        public List<Vector2[]> GetSelectedFacesScreenPts(
            PlayerViewport vp, Poly_Ling.Context.ModelContext model)
        {
            var cam = vp?.Cam;
            if (cam == null || model == null) return null;
            var mc = model.FirstDrawableMeshContext ?? model.FirstSelectedMeshContext;
            if (mc?.MeshObject == null) return null;
            var sel = mc.Selection;
            if (sel.Faces.Count == 0) return null;

            // GPU計算済みのワールド座標（ボーン変形込み）を取得
            var adapter = _renderer?.GetAdapter(0);
            UnityEngine.Vector3[] worldPositions = null;
            int vertexStart = 0;
            if (adapter != null && adapter.IsInitialized)
            {
                int ctxIdx = model.MeshContextList.IndexOf(mc);
                if (ctxIdx >= 0)
                {
                    int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
                    var bm = adapter.BufferManager;
                    if (unifiedIdx >= 0 && bm != null)
                    {
                        var meshInfos = bm.MeshInfos;
                        if (meshInfos != null && unifiedIdx < meshInfos.Length)
                        {
                            vertexStart = (int)meshInfos[unifiedIdx].VertexStart;
                            worldPositions = bm.GetDisplayPositions();
                        }
                    }
                }
            }

            var result = new List<Vector2[]>();
            var mo = mc.MeshObject;
            int totalVertexCount = worldPositions?.Length ?? 0;

            foreach (int fi in sel.Faces)
            {
                if (fi < 0 || fi >= mo.FaceCount) continue;
                var face = mo.Faces[fi];
                if (face.VertexCount < 3) continue;
                var pts = new Vector2[face.VertexCount];
                bool valid = true;
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int vi = face.VertexIndices[i];
                    if (vi < 0 || vi >= mo.VertexCount) { valid = false; break; }

                    UnityEngine.Vector3 worldPos;
                    int globalIdx = vertexStart + vi;
                    if (worldPositions != null && globalIdx < totalVertexCount)
                        worldPos = worldPositions[globalIdx];
                    else
                        worldPos = mc.WorldMatrix.MultiplyPoint3x4(mo.Vertices[vi].Position);

                    // 通常面描画パイプライン (ComputeScreenPositions) と同一の cam.pixel 基準投影。
                    Vector2 sp = ProjectWorldToCameraScreen(cam, worldPos);
                    if (float.IsNaN(sp.x)) { valid = false; break; }
                    pts[i] = sp;
                }
                if (valid) result.Add(pts);
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// GPU ホバー結果から PlayerHitResult を生成する。
        /// マウスダウン時に PlayerVertexInteractor.GetHoverHit コールバックから呼ばれる。
        ///
        /// UpdateFrame（ポインター移動時）が GPU ヒットテストを完了済みの前提。
        /// CPU による最近傍探索は行わない。
        /// </summary>
        public PlayerHitResult GetHoverHit()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHitResult.Miss;

            // グローバルホバー頂点インデックス
            int globalVertex = adapter.HoverVertexIndex;
            if (globalVertex < 0)
                return PlayerHitResult.Miss;

            // グローバル → メッシュコンテキストインデックス + ローカル頂点インデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertex, out int meshIdx, out int localIdx) == true)
            {
                return new PlayerHitResult
                {
                    HasHit      = true,
                    MeshIndex   = meshIdx,
                    VertexIndex = localIdx,
                };
            }
            return PlayerHitResult.Miss;
        }

        /// <summary>
        /// スカルプトブラシ用ヒットテスト。
        /// Normal モード（ドラッグなし）: UpdateFrame 算出済みの HoverVertexIndex を再利用（二重計算なし）。
        /// TransformDragging 中: _screenPositions（DrawViewport で毎イベント更新済み）から直接検索し
        ///   ブラシ中心をマウスに追従させる。
        /// </summary>
        public PlayerHitResult GetBrushHit(Vector2 screenPos, float hitRadius)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHitResult.Miss;

            int globalVertex;

            // HoverVertexIndex が有効なら UpdateFrame の結果を再利用（二重計算なし）
            int hoverIdx = adapter.HoverVertexIndex;
            if (hoverIdx >= 0)
            {
                globalVertex = hoverIdx;
            }
            else
            {
                // TransformDragging 中: _screenPositions は DrawViewport で更新済み
                var bm = adapter.BufferManager;
                if (bm == null) return PlayerHitResult.Miss;
                globalVertex = bm.FindNearestVertex(screenPos, hitRadius, adapter.BackfaceCullingEnabled);
                if (globalVertex < 0) return PlayerHitResult.Miss;
            }

            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertex, out int meshIdx, out int localIdx) == true)
            {
                return new PlayerHitResult
                {
                    HasHit      = true,
                    MeshIndex   = meshIdx,
                    VertexIndex = localIdx,
                };
            }
            return PlayerHitResult.Miss;
        }

        // ================================================================
        // スキンド変換
        // ================================================================

        /// <summary>
        /// スキンドメッシュのワールド変換をGPUバッファに反映する。
        /// model.ComputeWorldMatrices() を呼んだ後に呼ぶこと。
        /// Viewer.Update() で毎フレーム呼ぶことで矩形選択・ホバーの
        /// スクリーン座標がスキンドポーズに追従する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void UpdateTransform()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;
            adapter.UpdateTransform(useWorldTransform: true);
            adapter.WritebackTransformedVertices();
        }

        // ================================================================
        // MeshSceneRenderer 委譲
        // ================================================================

        /// <summary>
        /// 展開済み UnityMesh（UV分割で頂点数 > MeshObject.VertexCount）の
        /// 頂点座標を MeshObject.Vertices から直接更新する。
        ///
        /// GPU バッファの _expandedToOriginal マッピングと同じロジックを CPU で再現し、
        /// 展開後 Unity 頂点 → 元頂点インデックスを解決して position をコピーする。
        /// </summary>
        public void UpdateExpandedUnityMesh(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            if (mc?.MeshObject == null || mc.UnityMesh == null || model == null) return;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // contextIndex を解決
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            if (ctxIdx < 0) return;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return;

            var bm = adapter.BufferManager;
            if (bm == null) return;

            var meshInfos = bm.MeshInfos;
            if (meshInfos == null || unifiedIdx >= meshInfos.Length) return;

            var positions = bm.Positions; // CPU側 _positions 配列
            if (positions == null) return;

            uint vertexStart = meshInfos[unifiedIdx].VertexStart;
            int meshVertCount = mc.MeshObject.VertexCount;

            // ToUnityMesh/ToUnityMeshShared と同じ展開順序でUnityMesh頂点を更新する。
            // 孤立頂点を除外し、頂点順 → UV順 で展開。
            var mo = mc.MeshObject;
            var nonIsolated = new System.Collections.Generic.HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden) continue;
                foreach (int vi in face.VertexIndices) nonIsolated.Add(vi);
            }

            int unityIdx = 0;
            int totalUnity = mc.UnityMesh.vertexCount;
            var unityVerts = new UnityEngine.Vector3[totalUnity];

            for (int vIdx = 0; vIdx < mo.Vertices.Count && unityIdx < totalUnity; vIdx++)
            {
                if (!nonIsolated.Contains(vIdx)) continue;
                var vertex = mo.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                UnityEngine.Vector3 pos = mc.WorldMatrix.MultiplyPoint3x4(vertex.Position);
                for (int uvIdx = 0; uvIdx < uvCount && unityIdx < totalUnity; uvIdx++, unityIdx++)
                    unityVerts[unityIdx] = pos;
            }

            mc.UnityMesh.vertices = unityVerts;
            mc.UnityMesh.RecalculateBounds();
        }

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void RebuildAdapter(int mi, ModelContext model)
        {
#pragma warning disable CS0618
            _renderer?.RebuildAdapter(mi, model);
#pragma warning restore CS0618
        }

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ClearScene()
        {
#pragma warning disable CS0618
            _renderer?.ClearScene();
#pragma warning restore CS0618
        }

        /// <summary>
        /// CPU の MeshObject 位置を GPU バッファに同期する。
        ///
        /// 【呼び出しタイミング】
        ///   MoveToolHandler.OnSyncMeshPositions（頂点ドラッグ中の毎フレーム位置更新）。
        ///
        /// 【処理順序】
        ///   1. MeshObject.Positions → GPU _positionsBuffer（UpdatePositions）
        ///   2. NotifyTransformChanged で次フレームのワイヤー/頂点メッシュ再構築を予約
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void SyncMeshPositionsAndTransform(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
            {
                UnityEngine.Debug.Log($"[EditSync] SyncPos adapter=null_or_uninit ({(adapter == null ? "null" : "uninit")})");
                return;
            }
            if (mc?.MeshObject == null || model == null) return;

            var bm = adapter.BufferManager;
            if (bm == null) { UnityEngine.Debug.Log("[EditSync] SyncPos bm=null"); return; }

            // MeshContext → contextIndex → unifiedMeshIndex
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            }
            if (ctxIdx < 0) { UnityEngine.Debug.Log("[EditSync] SyncPos ctxIdx=-1 (mc not found)"); return; }

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) { UnityEngine.Debug.Log($"[EditSync] SyncPos unifiedIdx=-1 (ctxIdx={ctxIdx})"); return; }

            UnityEngine.Debug.Log($"[EditSync] SyncPos UpdatePositions ctxIdx={ctxIdx} unifiedIdx={unifiedIdx} V={mc.MeshObject.VertexCount}");
            // ① CPU MeshObject.Positions（またはWorkingPositions）→ GPU _positionsBuffer
            bm.UpdatePositions(mc, unifiedIdx);

            // ② ミラーバッファも同期（ミラー無効時は内部で早期リターン）
            bm.UpdateMirrorPositions(unifiedIdx);

            // ③ ミラーメッシュが存在する場合、座標をMeshObjectに反映してGPUバッファ同期
            // MirrorPair方式（BakeMirror=false, MirrorSide型）
            var mirrorPair = model.GetMirrorPair(mc);
            if (mirrorPair != null && mirrorPair.Real == mc && mirrorPair.Mirror?.MeshObject != null)
            {
                var mirrorMesh = mirrorPair.Mirror.MeshObject;
                if (mc.WorkingPositions != null && mirrorPair.Mirror.WorkingPositions == null)
                {
                    // Mirror側に自前のWorkingPositionsがない場合のみ、Real側のWorkingPositions込みでミラー反映。
                    // Mirror側がWorkingPositionsを持つ場合（PMX両側モーフ等）はSyncPositionsに留め、
                    // 後続のSyncMeshContextPositionsOnly(Mirror)でUpdatePositions(MeshContext)が処理する。
                    for (int i = 0; i < mirrorPair.VertexMap.Length && i < mc.MeshObject.VertexCount; i++)
                    {
                        int mi = mirrorPair.VertexMap[i];
                        if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;
                        var offset = (i < mc.WorkingPositions.Length) ? mc.WorkingPositions[i] : UnityEngine.Vector3.zero;
                        var worldPos = mc.MeshObject.Vertices[i].Position + offset;
                        mirrorMesh.Vertices[mi].Position = mirrorPair.MirrorPosition(worldPos);
                    }
                }
                else
                {
                    mirrorPair.SyncPositions();
                }
                int mirrorCtxIdx = model.MeshContextList.IndexOf(mirrorPair.Mirror);
                if (mirrorCtxIdx >= 0)
                {
                    int mirrorUnifiedIdx = adapter.ContextToUnifiedMeshIndex(mirrorCtxIdx);
                    if (mirrorUnifiedIdx >= 0)
                        bm.UpdatePositions(mirrorPair.Mirror, mirrorUnifiedIdx);
                }
            }

            // BakedMirror方式（BakeMirror=true, BakedMirror型）
            // WorkingPositions が設定されている場合（モーフプレビュー等）はスキップ。
            // BakedMirrorメッシュは自身の WorkingPositions を使い SyncMeshContextPositionsOnly で
            // 独立してGPUを更新するため、ここで上書きすると二重適用になる。
            // WorkingPositions == null（通常の頂点編集）の場合のみ実行する。
            if (mc.WorkingPositions == null)
            {
                var mirrorMatrix = bm.GetMirrorMatrix();
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mirrorCtx = model.GetMeshContext(i);
                    if (mirrorCtx?.BakedMirrorSourceIndex != ctxIdx) continue;
                    if (mirrorCtx.MeshObject == null) continue;

                    int mirrorUnifiedIdx = adapter.ContextToUnifiedMeshIndex(i);
                    if (mirrorUnifiedIdx < 0) continue;

                    int count = Mathf.Min(mc.MeshObject.VertexCount, mirrorCtx.MeshObject.VertexCount);
                    for (int v = 0; v < count; v++)
                    {
                        var basePos = mc.MeshObject.Vertices[v].Position;
                        mirrorCtx.MeshObject.Vertices[v].Position = mirrorMatrix.MultiplyPoint3x4(basePos);
                    }
                    bm.UpdatePositions(mirrorCtx.MeshObject, mirrorUnifiedIdx);
                }
            }

            // ④ 次フレームのワイヤー/頂点メッシュ再構築を予約
            adapter.NotifyTransformChanged();
        }

        /// <summary>
        /// 頂点移動後に呼ぶ。次フレームのGPUバッファ更新を予約する。
        /// </summary>
        public void NotifyTransformChanged()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return;
            adapter.NotifyTransformChanged();
        }

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void ResetToMesh(Bounds bounds)
        {
            PerspectiveViewport?.ResetToMesh(bounds);
            TopViewport        ?.ResetToMesh(bounds);
            FrontViewport      ?.ResetToMesh(bounds);
            SideViewport       ?.ResetToMesh(bounds);

            // リセットは Perspective vp 経由で呼ばれるため、ortho の遅延ズーム解決
            // （pixelHeight 確定時）を確実に行うべく、全ビューの transform を適用する。
            ApplyAllViewportCameraTransforms();
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 【event 駆動・内部】指定 slot の描画準備（計算・Prepare）を実行する。
        /// display settings の適用・カリング Dispatch・CPU Mesh 再構築・Queue 登録を行う。
        /// Submit は一切行わない（そちらは SubmitForCamera が担当）。
        /// </summary>
        private void PrepareViewport(ProjectContext project, PlayerViewport vp, int slot)
        {
            if (vp == null || !vp.IsReady) return;
            var cam     = vp.Cam;
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // 面ごとの表示設定をレンダラーに適用する。
            // Prepare 呼び出しはシーケンシャルなので面をまたいだ競合はない。
            var ds = _displaySettings[slot];
            _renderer.BackfaceCullingEnabled    = ds.BackfaceCulling;
            _renderer.ShowSelectedMesh          = ds.ShowSelectedMesh;
            _renderer.ShowSelectedWireframe     = ds.ShowSelectedWireframe;
            _renderer.ShowSelectedVertices      = ds.ShowSelectedVertices;
            _renderer.ShowSelectedBone          = ds.ShowSelectedBone;
            _renderer.ShowUnselectedMesh        = ds.ShowUnselectedMesh;
            _renderer.ShowUnselectedWireframe   = ds.ShowUnselectedWireframe;
            _renderer.ShowUnselectedVertices    = ds.ShowUnselectedVertices;
            _renderer.ShowUnselectedBone        = ds.ShowUnselectedBone;
            _renderer.ShowSelectedMirror        = ds.ShowSelectedMirror;
            _renderer.ShowUnselectedMirror      = ds.ShowUnselectedMirror;

            // adapter の BackfaceCullingEnabled もここで同期する
            // （DispatchCullingForDisplay の引数に使用するため）。
            adapter.BackfaceCullingEnabled = ds.BackfaceCulling;

            // 永続ミラーの per-slot 表示状態を bufMgr へ設定する。
            // （直後の DispatchCullingForDisplay(slot) と、後段のアクティブ slot 最終カリングが
            //   slot ごとの正しい値を参照できるようにするため。）
            adapter.BufferManager?.SetMirrorDisplay(slot, ds.ShowSelectedMirror, ds.ShowUnselectedMirror);

            // カメラが変化したスロットのみ per-slot カリングを再計算する。
            if (_slotCameraDirty[slot])
            {
                adapter.DispatchCullingForDisplay(cam, adapter.BackfaceCullingEnabled, slot);
                _slotCameraDirty[slot] = false;
            }

            // Prepare 系のみ呼び出す。Submit は OnRenderObject / SubmitForCamera で行う。
            _renderer.PrepareWireframeAndVertices(cam, project, slot);
            _renderer.PrepareBones(project);
            _renderer.PrepareWeightVisualization(project);
        }

        /// <summary>
        /// 【重大規約違反コード: 旧 API】
        /// LateUpdate 経路で使われていた per-slot の描画呼び出し。
        /// 新規コードは PrepareViewport + SubmitForCamera を使うこと。
        /// </summary>
        private void DrawViewport(ProjectContext project, PlayerViewport vp, int slot)
        {
            if (vp == null || !vp.IsReady) return;
            var cam     = vp.Cam;
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            var ds = _displaySettings[slot];
            _renderer.BackfaceCullingEnabled    = ds.BackfaceCulling;
            _renderer.ShowSelectedMesh          = ds.ShowSelectedMesh;
            _renderer.ShowSelectedWireframe     = ds.ShowSelectedWireframe;
            _renderer.ShowSelectedVertices      = ds.ShowSelectedVertices;
            _renderer.ShowSelectedBone          = ds.ShowSelectedBone;
            _renderer.ShowUnselectedMesh        = ds.ShowUnselectedMesh;
            _renderer.ShowUnselectedWireframe   = ds.ShowUnselectedWireframe;
            _renderer.ShowUnselectedVertices    = ds.ShowUnselectedVertices;
            _renderer.ShowUnselectedBone        = ds.ShowUnselectedBone;

            adapter.BackfaceCullingEnabled = ds.BackfaceCulling;

            if (_slotCameraDirty[slot])
            {
                adapter.DispatchCullingForDisplay(cam, adapter.BackfaceCullingEnabled, slot);
                _slotCameraDirty[slot] = false;
            }

            _renderer.DrawMeshes(project, cam);
            _renderer.DrawWireframeAndVertices(cam, project, slot);
            _renderer.DrawBones(project, cam);
            _renderer.DrawWeightVisualization(project, cam);
        }
    }
}
