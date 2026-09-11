// PlayerViewportManager.cs
// 3つの PlayerViewport（Perspective / Top / Front）を管理し、
// MeshSceneRenderer の描画呼び出しを各カメラに対して行う。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【分割先】このファイルから次へ分けてある。
//   PlayerViewportEnums.cs              ビューポート管理の正規入口カテゴリと段階・ホバー対象の列挙（PlayerViewportManager から分離）。
//   PlayerViewportManager.GpuSelect.cs  ビューポート管理：矩形選択用の GPU データ取得。
//   PlayerViewportManager.Hover.cs      ビューポート管理：ポインタ移動の間引きと、ホバー・カメラ更新（イベント駆動）。
//   PlayerViewportManager.Render.cs     ビューポート管理：毎フレーム更新・起動時入口・描画の正規入口と重量級入口・Present。
//   PlayerViewportManager.Renderer.cs   ビューポート管理：スキンド変換・MeshSceneRenderer への委譲・カメラ初期位置のリセット。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{

    /// <summary>
    /// Perspective / Top / Front の3ビューポートを管理する。
    /// Viewer の Update / LateUpdate から対応メソッドを呼ぶこと。
    /// </summary>
    public partial class PlayerViewportManager
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

        /// <summary>
        /// ツールが生成した MeshContext / MeshObject の受け口を結線する。
        /// PolyLingPlayerViewerCore が Initialize で 1 回だけ呼ぶ。
        /// 未結線だと ToolContext.AddMeshContext が null のままになり、
        /// 生成系ツールが何も追加できない。
        /// </summary>
        public void SetMeshContextSinks(
            Action<Poly_Ling.Data.MeshContext> addMeshContext,
            Action<Poly_Ling.Data.MeshObject, string> addMeshObjectToCurrentMesh)
        {
            _toolCtx.AddMeshContext             = addMeshContext;
            _toolCtx.AddMeshObjectToCurrentMesh = addMeshObjectToCurrentMesh;
        }

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
        /// 現在の実効選択モード（頂点/辺/面/線分）を全 SelectionState へ再適用させる
        /// コールバック（Core から結線）。
        ///
        /// モデルロード・トポロジ変更・Undo 適用では MeshContext と SelectionState が
        /// 作り直される。作り直された側は SelectionState の既定モードのままなので、
        /// ここで再適用しないとチェックボックスの指定が無効化される。
        /// </summary>
        public Action OnApplySelectMode;

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
        /// <summary>
        /// ホバー確定頂点とマウス位置の差がこの px 数を超えたら診断ダンプを出す。
        /// </summary>
        private const float HoverOffsetDumpThresholdPx = 3f;

        private const int SlotPerspective = 0;
        private const int SlotTop         = 1;
        private const int SlotFront       = 2;
        private const int SlotSide        = 3;

        // ================================================================
        // per-slot カリング再計算の dirty フラグ（2 種類ある。混同しないこと）
        // ================================================================
        //
        // カリング（DispatchCullingForDisplay）の入力は 4 つ。
        //   ① 全頂点のスクリーン座標（= カメラ行列 × ワールド頂点位置）
        //   ② FLAG_HIDDEN          … ClearCulledBuffers カーネルが読む
        //   ③ FLAG_MESH_SELECTED   … ApplyMirrorCull カーネルが読む（ミラー要素のみ）
        //   ④ ミラー表示設定 / 背面カリング ON-OFF … SetMirrorDisplay と分岐
        //
        // ②③④とワールド頂点位置は「内容」側、①のカメラ部分だけが「カメラ」側。
        // 両者を 1 本のフラグで持つと、カメラ側にしか効かない最適化
        // （下の CameraChangeAffectsCulling）を内容変更にも誤適用してしまう。
        // そのため 2 本に分ける。

        /// <summary>
        /// 内容 dirty。ジオメトリ・可視性フラグ・選択・表示設定が変わった。
        /// 立った slot は無条件で再カリングする。
        /// 立てるのは MarkAllSlotsDirty() と SetDisplaySettings() のみ。
        /// </summary>
        private readonly bool[] _slotContentDirty = new bool[4] { true, true, true, true };

        /// <summary>
        /// カメラ dirty。そのビューポートのカメラパラメータが変わった。
        /// 再カリングするかは CameraChangeAffectsCulling() が判定する。
        /// 立てるのは NotifyCameraChanged / NotifyCameraMoved / ApplyAndDirtyLinkedOrtho。
        /// </summary>
        private readonly bool[] _slotCameraDirty = new bool[4] { true, true, true, true };

        // 各 slot で最後にカリングを計算したときのカメラ姿勢と投影種別。
        // CameraChangeAffectsCulling の比較対象。_slotCullValid が false の間は未計算。
        private readonly Quaternion[] _slotCullRotation   = new Quaternion[4];
        private readonly bool[]       _slotCullOrthographic = new bool[4];
        private readonly bool[]       _slotCullValid      = new bool[4];

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
                arr[i] = (int.TryParse(s, out int bits)
                    ? ViewportDisplaySettings.FromBits(bits)
                    : ViewportDisplaySettings.Default).WithMirrorClamped();
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
            // ミラー表示はメッシュ表示に従属させる（Mesh が OFF ならそのミラーは強制 OFF）。
            s = s.WithMirrorClamped();
            _displaySettings[slot] = s;
            // 表示設定を起動間で記録（write-through）。
            if (slot >= 0 && slot < 4)
                RecentPaths.Set(DisplaySettingsKey(slot), s.ToBits().ToString());
            // 表示設定（背面カリング ON/OFF、ミラー表示）が変わったらカリングを再計算する。
            // カメラは動いていないので「内容 dirty」側に立てる。カメラ dirty に立てると
            // CameraChangeAffectsCulling が「回転が同じ正射影だから不要」と判定して
            // 再計算が飛ばされ、設定変更が反映されなくなる。
            if (slot >= 0 && slot < _slotContentDirty.Length)
                _slotContentDirty[slot] = true;
        }

        // ================================================================
        // 軸 / グリッド平面の表示設定（4面共通）
        // ================================================================

        private ViewportGridSettings _gridSettings = LoadGridSettings();

        private readonly Poly_Ling.Core.Rendering.GridAxisRenderer _gridAxisRenderer
            = new Poly_Ling.Core.Rendering.GridAxisRenderer();

        private const string GridSettingsKey = "Viewport.Grid";

        // RecentPaths から軸/グリッド設定を復元（未保存/不正なら Default）。
        private static ViewportGridSettings LoadGridSettings()
        {
            return ViewportGridSettings.FromCsv(RecentPaths.Get(GridSettingsKey, ""));
        }

        /// <summary>軸/グリッドの表示設定を取得する（4面共通）。</summary>
        public ViewportGridSettings GetGridSettings() => _gridSettings;

        /// <summary>軸/グリッドの表示設定を更新する（4面共通）。</summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (EnterProjectChanged / " +
            "EnterTopologyChanged / EnterCameraChanged / EnterVerticesMoved / " +
            "EnterHoverChanged / EnterDisplaySettingsChanged) を使うこと。" +
            "承認なしで本 API を新規呼出しすることは禁止。",
            error: false)]
        public void SetGridSettings(ViewportGridSettings s)
        {
            _gridSettings = s.Clamped();
            // 表示設定を起動間で記録（write-through）。
            RecentPaths.Set(GridSettingsKey, _gridSettings.ToCsv());
        }

        /// <summary>
        /// 軸/グリッドのラインメッシュを現在の設定で構築しておく。
        /// GridAxisRenderer.Prepare は設定が変化していなければ即 return するため、
        /// PrepareViewport から毎回呼んでも再構築は起きない。
        /// </summary>
        private void EnsureGridPrepared()
        {
            _gridAxisRenderer?.Prepare(_gridSettings.ToParams());
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

            // [CamDbg] cams=1 のとき Perspective 以外のカメラを止める。診断専用。
            Poly_Ling.Diagnostics.PLCamDbg.EnsureSwitches();
            if (Poly_Ling.Diagnostics.PLCamDbg.SwSingleCamera)
            {
                if (TopViewport  ?.Cam != null) TopViewport.Cam.enabled   = false;
                if (FrontViewport?.Cam != null) FrontViewport.Cam.enabled = false;
                if (SideViewport ?.Cam != null) SideViewport.Cam.enabled  = false;
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("SW singleCamera applied");
            }

            // 軸/グリッドはモデル未ロードでも表示するため、ここで構築しておく。
            EnsureGridPrepared();
        }

        public void Dispose()
        {
            _gridAxisRenderer?.Dispose();

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
        /// メッシュトポロジー変更・モデル切り替え・頂点移動確定・可視性変更・
        /// 選択変更・GPU バッファ再構築のあとに呼ぶ。
        /// 全スロットのカリングを次の PresentAll で強制再計算させる。
        ///
        /// 【カメラ dirty ではなく内容 dirty を立てる理由】
        ///   カメラ dirty は CameraChangeAffectsCulling のフィルタを通る。
        ///   正射影ビューでカメラ姿勢が変わっていなければ再計算は飛ばされるので、
        ///   ジオメトリやフラグの変更をカメラ dirty で伝えると取りこぼす。
        ///
        /// 【選択変更でも呼ぶ必要がある】
        ///   ApplyMirrorCull カーネルが FLAG_MESH_SELECTED を読み、
        ///   永続ミラーの表示可否を選択・非選択で切り替えるため。
        ///   「選択はカリングに影響しない」は誤り。
        /// </summary>
        public void MarkAllSlotsDirty()
        {
            for (int i = 0; i < _slotContentDirty.Length; i++)
                _slotContentDirty[i] = true;
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
        /// 反映し slot をカメラ dirty にする。呼び出し側で PresentAll される前提。
        /// changed が ortho でない（Perspective）場合は何もしない。
        ///
        /// 【3 面を毎回 dirty にしてよい理由】
        ///   ここで立てるのはカメラ dirty なので、実際に再カリングするかは
        ///   PrepareViewport の CameraChangeAffectsCulling が決める。
        ///   パン・ズームでは（真の正射影である限り）カメラ姿勢が変わらないため
        ///   再計算は飛ばされる。斜め 45°（RigRotation）・フリップ・パース表示の
        ///   切り替えは姿勢または投影種別が変わるので必ず拾われる。
        ///   これらは全て EnterCameraChanged(Committed) を通り、パン・ズームと
        ///   同じ経路でここへ来るため、入口側では区別できない。判定は使う直前に置く。
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

        /// <summary>
        /// _toolCtx をこのビューポート基準へ更新する。
        ///
        /// 【重要】カメラ情報だけでなく Model も必ず入れる。Model が null のままだと
        /// ToToolContext が返す ToolContext.Model も null になり、ツール側の選択チェック
        /// （例: ObjectMoveTool.HasAnySelection）が false を返してホバー更新が無視される。
        /// _toolCtx.UpdateFromViewport を直接呼ばず、必ずこのメソッドを経由すること。
        /// </summary>
        private void SyncToolCtx(PlayerViewport vp)
        {
            _toolCtx.UpdateFromViewport(vp);
            _toolCtx.Model = _lastProjectForPresent?.CurrentModel;
        }

        public ToolContext GetCurrentToolContext(PlayerViewport vp = null)
        {
            var target = vp ?? PerspectiveViewport;
            var cam = target?.Cam;
            if (cam == null) return null;
            SyncToolCtx(target);
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
            SyncToolCtx(vp);
            return _toolCtx.ToToolContext(cam);
        }

        // 【DrawViewport を撤去した理由】 2026-08-28
        //   唯一の呼出元だった LateUpdate と一緒に死んでいた。
        //   per-slot の準備は PrepareViewport、提出は SubmitForCamera が担う。
    }
}
