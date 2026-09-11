// PlayerViewportManager.Render.cs
// ビューポート管理：毎フレーム更新・起動時入口・描画の正規入口と重量級入口・Present。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

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
    public partial class PlayerViewportManager
    {
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
        // 【起動時専用入口】ビューポートの実サイズ確定後の 1 回リフレッシュ
        // ================================================================

        /// <summary>
        /// ★★★【起動時専用・カテゴリ 9: ビューポート準備完了】★★★
        ///
        /// 使用場面（起動直後の 1 回限り）:
        ///   - 4 つの PlayerViewportPanel が UIToolkit のレイアウトで実サイズを得て、
        ///     RenderTexture が 1×1 から実寸へ Resize された直後。
        ///
        /// 使用してはならない場面:
        ///   - 通常のカメラ操作 → EnterCameraChanged
        ///   - モデルロード・図形生成 → EnterSceneReset
        ///   - トポロジ変更・頂点移動 → EnterTopologyChanged / EnterVerticesMoved
        ///   - ウィンドウリサイズのたびに呼ぶこと（毎回の再描画は既存経路の責務）
        ///
        /// 【この入口が要る理由】
        ///   Initialize 直後は UIToolkit のレイアウトが未確定で、RT は
        ///   PlayerViewport.Initialize の CreateRT(1,1) のまま。この状態では
        ///   ・Unity Camera.transform が既定値（位置 0 / 回転 identity）のまま
        ///   ・OrthoViewController の PendingResetHalfHeight が
        ///     cam.pixelHeight > 1f を満たさず未解決のまま
        ///   になる。Tick 廃止後は ApplyCameraTransform を走らせる契機が
        ///   カメライベントしか無く、起動直後は誰も呼ばない。
        ///
        /// 【EnterCameraChanged で代替できない理由】
        ///   NotifyCameraChanged は _renderer.GetAdapter(0) が null のとき早期 return し、
        ///   vp.ApplyCameraTransform() に到達しない。モデル未ロード時は
        ///   RebuildAdapter が未実行でアダプタが存在しないため、必ずこの経路に入る。
        ///
        /// 【EnterSceneReset で代替しない理由】
        ///   あちらは RebuildAdapter / SetSelectionState / OnApplySelectMode を伴う
        ///   重量級専用入口で、カメラ確定だけが欲しい起動時には過剰。
        ///
        /// 責務:
        ///   1. 4 viewport 全ての Camera.transform をカメラパラメータへ同期
        ///   2. 全 slot を内容 dirty にする（初回カリングを必ず計算させる）
        ///   3. PresentAll で 4 slot 分の Prepare を実行
        ///
        /// 冪等。複数回呼んでも結果は変わらない。
        /// </summary>
        public void EnterViewportsReady()
        {
#pragma warning disable CS0618
            ApplyAllViewportCameraTransforms();
            MarkAllSlotsDirty();
            PresentAll(_lastProjectForPresent);
#pragma warning restore CS0618
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
                ApplySnapHitTestEnabled();
            }

            // 【MarkAllSlotsDirty が必須である理由】 2026-08-28
            //   RebuildAdapter は _adapters[mi].Dispose() のあと
            //   new UnifiedSystemAdapter() を作る。UnifiedBufferManager ごと
            //   作り直されるので、per-slot カリングバッファは中身が未定義になる。
            //   _slotContentDirty を立てないと、2 回目以降のモデルロードで
            //   カリング結果が前のバッファの残骸のままになる。
            //   RebuildAdapter を呼ぶ他の入口（EnterSceneReset / EnterUndoApplied /
            //   EnterTopologyChanged / EnterMeshAttributesChanged）は既に呼んでいる。
            //   ここだけ抜けていた。
            MarkAllSlotsDirty();
            // 4 viewport の Camera.transform を最新のカメラパラメータへ同期する。
            // 他の重量級入口と同じ組にそろえる。
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
            // DiagCaller() は StackTrace を作る。引数側は必ず評価されるため、
            // スイッチをここで見てから呼ぶ。
            if (PLDiag.Enabled && PLDiag.Viewport)
                PLDiag.ViewportEnter("EnterTopologyChanged", DiagCaller());
            // 選択モードの再適用は RebuildAdapter より前。
            // 無効種別の選択解除が GPU 選択フラグ構築に反映されるようにする。
            OnApplySelectMode?.Invoke();
#pragma warning disable CS0618
            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
            {
                // RebuildAdapter より前に呼ぶ。頂点位置はローカルのままでよく、
                // RebuildAdapter 末尾の WritebackTransformedVertices が
                // 展開ワールド座標で上書きする。
                RebuildSelectedUnityMeshes(model);
                _renderer.RebuildAdapter(0, model);
                _renderer.UpdateSelectedDrawableMesh(0, model);
                ApplySnapHitTestEnabled();
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

        /// <summary>
        /// 選択中の描画オブジェクトの UnityMesh を MeshObject から作り直す。
        ///
        /// 【なぜ要るか】
        /// サーフェス（面）の描画は ctx.UnityMesh をそのまま使う（MeshSceneRenderer.SubmitMeshes）。
        /// 一方 RebuildAdapter 末尾の WritebackTransformedVertices は
        /// 「UnityMesh の頂点数 == 展開頂点数」なら位置だけ更新して戻り、三角形は作り直さない。
        /// 展開頂点数は「いずれかの面に使われている頂点 × UV スロット数」で決まる
        /// （MeshObject.BuildExpansionMap）ため、内部の面を 1 枚削除しても、その頂点が
        /// 隣の面に使われていれば数が変わらない。結果、GPU 側のワイヤーと頂点は消えるのに
        /// UnityMesh には消したはずの三角形が残る。ここで作り直してその取りこぼしを塞ぐ。
        /// 面反転のようにインデックスだけが変わる操作も同じ経路。
        ///
        /// 【対象範囲】面削除・面反転はアクティブメッシュ固定、面結合は
        /// SelectedDrawableMeshIndices 走査。どちらもこの範囲に収まるため、
        /// 全 MeshContext を舐めずにここへ限定する。
        ///
        /// 【破棄】直接代入ではなく ReplaceUnityMesh を使う。Mesh はネイティブ
        /// オブジェクトで、参照を捨てても解放されないため。
        /// </summary>
        private static void RebuildSelectedUnityMeshes(Poly_Ling.Context.ModelContext model)
        {
            var indices = model.SelectedDrawableMeshIndices;
            if (indices == null || indices.Count == 0) return;

            int materialCount = model.MaterialCount;

            for (int i = 0; i < indices.Count; i++)
            {
                var mc = model.GetMeshContext(indices[i]);
                if (mc?.MeshObject == null) continue;

                // ボーン表示用メッシュは MeshObject から作らない（専用キャッシュで描く）。
                if (mc.Type == Poly_Ling.Data.MeshType.Bone) continue;

                mc.ReplaceUnityMesh(mc.MeshObject.ToUnityMesh(materialCount));
            }
        }

        /// <summary>
        /// 選択・属性の変更時: GPU バッファは作り直さず、選択反映と再描画だけ行う。
        ///
        /// EnterTopologyChanged との違いは RebuildAdapter を呼ばない点のみ。
        /// RebuildAdapter は UnifiedSystemAdapter を Dispose して作り直し、
        /// 全メッシュから GPU バッファを再構築するため、選択が変わっただけで
        /// 呼ぶとリスト操作のたびに全再構築が走る。
        /// </summary>
        /// <summary>
        /// 診断用に呼び出し元を1階層だけ取る。
        /// EnterTopologyChanged は NotifyPanels 以外（Dispatch など）からも直接呼ばれるため、
        /// どこから来たかが分からないと再構築の原因を追えない。
        /// PLDiag が無効なときはスタックトレースを取らない。
        /// </summary>
        private static string DiagCaller()
        {
            if (!PLDiag.Enabled || !PLDiag.Viewport) return "";
            var frame = new System.Diagnostics.StackTrace(2, false).GetFrame(0);
            var method = frame?.GetMethod();
            if (method == null) return "<unknown>";
            return (method.DeclaringType?.Name ?? "?") + "." + method.Name;
        }

        /// <summary>
        /// カテゴリ 2'': メッシュ属性のみの変更（可視・ロック）。
        ///
        /// 【2つの経路がある】
        ///   不可視メッシュと空メッシュは GPU バッファに載せない
        ///   （UnifiedBufferManager.ShouldIncludeInBuffers）。可視が変わると
        ///   載せる対象そのものが変わり、グローバル頂点／線分／面インデックスが
        ///   丸ごとずれるため、フラグの書き戻しでは追いつかない。
        ///
        ///   載せる集合が変わった  … EnterTopologyChanged と同じ全再構築へ回す。
        ///   変わっていない        … 従来どおり Hidden / Locked のフラグだけ書き戻す。
        ///
        ///   ChangeKind.Attributes はロック変更も含むが、ロックだけなら集合は
        ///   変わらないので再構築は走らない。
        ///
        /// 後者の経路が無いと、面の非表示（Face.IsHidden）の変更が面
        /// （SubmitMeshes が毎フレーム MeshContext を見る）にだけ効き、
        /// 頂点と辺に反映されない。
        ///
        /// 契機: 可視性・ロックの変更（ChangeKind.Attributes）。
        /// </summary>
        public void EnterMeshAttributesChanged(ProjectContext project)
        {
            if (PLDiag.Enabled && PLDiag.Viewport)
                PLDiag.ViewportEnter("EnterMeshAttributesChanged", DiagCaller());
#pragma warning disable CS0618
            var adapter = _renderer?.GetAdapter(0);
            var model   = project?.CurrentModel;

            // バッファに載せる集合が変わったか。変わっていればインデックス空間が
            // ずれるので、フラグ書き戻しでは済まない。
            bool needsRebuild =
                adapter?.BufferManager != null &&
                model != null &&
                adapter.BufferManager.NeedsRebuildForMembership(model);

            if (needsRebuild)
            {
                if (_renderer != null)
                {
                    // 順序は EnterTopologyChanged と同じ。RebuildAdapter より前に
                    // UnityMesh を作り直し、末尾の WritebackTransformedVertices に
                    // 展開ワールド座標で上書きさせる。
                    RebuildSelectedUnityMeshes(model);
                    _renderer.RebuildAdapter(0, model);
                    _renderer.UpdateSelectedDrawableMesh(0, model);
                    ApplySnapHitTestEnabled();
                }
                ApplyAllViewportCameraTransforms();
            }
            else
            {
                adapter?.BufferManager?.UpdateAllVisibilityFlags();
            }

            MarkAllSlotsDirty();
            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
        }

        public void EnterSelectionChanged(ProjectContext project)
        {
            if (PLDiag.Enabled && PLDiag.Viewport)
                PLDiag.ViewportEnter("EnterSelectionChanged", DiagCaller());
#pragma warning disable CS0618
            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
                _renderer.UpdateSelectedDrawableMesh(0, model);
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
        /// カテゴリ 2': 頂点属性のみの変更（ボーンウェイト / UV）。
        /// 頂点数・面構成・UV スロット数は不変。
        ///
        /// EnterTopologyChanged と違い RebuildAdapter を呼ばない。
        /// 対象メッシュの範囲だけを GPU バッファへ部分転送し、
        /// GPU スキニングの再計算と全 viewport の再描画準備のみ行う。
        ///
        /// 契機: スキンウェイトペイント確定、ウェイト一括操作（Flood / Normalize / Prune）。
        /// 頂点数・面構成・UV スロット数が変わる操作では使用禁止（EnterTopologyChanged を使うこと）。
        /// </summary>
        public void EnterVertexAttributesChanged(
            ProjectContext project,
            Poly_Ling.Data.MeshContext mc,
            bool weights = true,
            bool uvs = false)
        {
            var model = project?.CurrentModel;
            if (mc?.MeshObject == null || model == null) return;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            var bm = adapter.BufferManager;
            if (bm == null) return;

            // MeshContext → contextIndex → unifiedMeshIndex
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            }
            if (ctxIdx < 0) return;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return;

            if (weights) bm.UpdateBoneWeights(mc.MeshObject, unifiedIdx);
            if (uvs)     bm.UpdateUVs(mc.MeshObject, unifiedIdx);

#pragma warning disable CS0618
            // ウェイト変更でスキニング結果が変わるため、GPU 変形と書き戻しをやり直す。
            if (weights) UpdateTransform();
            MarkAllSlotsDirty();
            ApplyAllViewportCameraTransforms();
            PresentAll(project);
#pragma warning restore CS0618
            OnRefreshSelectedFacesOverlay?.Invoke();
            OnRefreshGizmoOverlay?.Invoke();
            OnRefreshBoneOverlay?.Invoke();
            RefreshToolOverlays();
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
            // 診断: どのフェーズで、軽量同期経路 (syncMc != null) かどうかを記録する。
            PLDiag.PickRec("VMoved", (int)phase, syncMc != null ? 1 : 0);

#pragma warning disable CS0618
            switch (phase)
            {
                case VerticesMovedPhase.DragBegin:
                    EnterTransformDragging();
                    // ドラッグ中は法線線分の始点も方向も変わり、位置のみの軽量更新が
                    // 成立しないため非表示にする。Dragging フェーズは PresentAll を
                    // 通らない軽量経路があり PrepareNormals が呼ばれないので、
                    // ここで明示的に抑止する。
                    _renderer?.SetNormalsSuppressed(true);
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
                    // 抑止を解除する。直後の PresentAll → PrepareNormals が
                    // Normal モードで法線メッシュを再構築する。
                    _renderer?.SetNormalsSuppressed(false);
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

        /// <summary>
        /// カテゴリ 6: 軸/グリッド平面の表示設定変更（4面共通）。
        /// 契機: 軸/グリッドサブパネルの各項目変更。
        /// slot 引数を取らない点以外は上のオーバーロードと同じ扱い。
        /// </summary>
        public void EnterDisplaySettingsChanged(ViewportGridSettings gs)
        {
#pragma warning disable CS0618
            SetGridSettings(gs);
            EnsureGridPrepared();
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
        ///   2. model.ActiveMeshContext.Selection を Core と renderer に設定
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
            // 選択モードの再適用は RebuildAdapter / SetSelectionState より前。
            OnApplySelectMode?.Invoke();
#pragma warning disable CS0618
            if (clearScene) _renderer?.ClearScene();

            var model = project?.CurrentModel;
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);

                // first MeshContext の Selection をアクティブ化
                var firstMc = model.ActiveMeshContext;
                if (firstMc != null)
                {
                    OnSetSelectionState?.Invoke(firstMc.Selection);
                    _renderer.SetSelectionState(firstMc.Selection);
                }

                _renderer.UpdateSelectedDrawableMesh(0, model);
                ApplySnapHitTestEnabled();
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
            // Undo は MeshContext ごと差し替わり得る。作り直された SelectionState へ
            // 現在の実効選択モードを再適用する。
            OnApplySelectMode?.Invoke();
#pragma warning disable CS0618
            if (_renderer != null && model != null)
            {
                _renderer.RebuildAdapter(0, model);
                _renderer.UpdateSelectedDrawableMesh(0, model);
                ApplySnapHitTestEnabled();
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

        // 【LateUpdate(ProjectContext) を撤去した理由】 2026-08-28
        //   呼出元 0 件。旧 LateTick 経路（毎フレームポーリング）の名残で、
        //   内部で RequestNormal + UpdateFrame + DrawViewport ×4 +
        //   アクティブ slot の最終カリングを行っていた。
        //   現在の描画は PresentAll（計算・準備）と SubmitForCamera（提出）に
        //   分離済み。毎フレームポーリングへ戻してはならない。
        //   同時に、ここからのみ呼ばれていた DrawViewport も撤去した。

        // Phase 1: 最後に PresentAll に渡された ProjectContext を保持。
        // NotifyCameraChanged 等の event ハンドラ内で project 引数なしで PresentAll を
        // 再呼出しするために使用する。
        private ProjectContext _lastProjectForPresent;
    }
}
