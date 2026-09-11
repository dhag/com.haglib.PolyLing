// PlayerViewportManager.Hover.cs
// ビューポート管理：ポインタ移動の間引きと、ホバー・カメラ更新（イベント駆動）。
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
        // ポインタ移動の間引き（残件 3-1）
        // ================================================================
        //
        // NotifyPointerHover は 1 回で GPU 同期読み戻しを 4 回起こす。
        // UIToolkit の PointerMoveEvent は OS のマウス移動メッセージごとに発火し、
        // 高解像度マウスやトラックパッドでは 1 画素未満の移動でも連続して届く。
        // 同じ画素なら結果は同じなので、そこは丸ごと省く。
        //
        // 【安全である根拠】
        //   ホバー結果はマウス位置だけでなく、カメラ・頂点位置・選択・トポロジにも
        //   依存する。それらが変わる経路は必ず PresentAll を通るので、
        //   PresentAll の先頭でこのキャッシュを捨てる。これにより
        //   「同じ画素だが結果は変わっている」状況で取りこぼすことはない。
        //   EnterHoverChanged は PresentAll を呼ばないため、純粋なポインタ移動では
        //   キャッシュは生き続ける。
        //
        // 【slot を鍵に含める理由】
        //   panelLocalPos はパネルローカル座標。ビューポートをまたいで同じ画素へ
        //   移った場合はカメラが違うので、必ず再計算しなければならない。
        //
        // 【承知しているトレードオフ】
        //   1 画素未満の移動ではホバーをやり直さない。ヒット半径の境界や
        //   HoverDistanceTolerance(3px) のバンド境界にある要素は、
        //   切り替わりが最大 1 画素ぶん遅れる。
        private int _lastHoverSlot = -1;
        private int _lastHoverPixelX = int.MinValue;
        private int _lastHoverPixelY = int.MinValue;

        /// <summary>
        /// ホバー間引きのキャッシュを捨てる。次のポインタ移動は同じ画素でも再計算する。
        /// ホバー結果に影響し得る変更（カメラ・頂点・選択・トポロジ・表示設定）の
        /// あとに必ず呼ぶこと。現状の呼び出し箇所は PresentAll の先頭のみ。
        /// </summary>
        private void InvalidateHoverDedup()
        {
            _lastHoverSlot   = -1;
            _lastHoverPixelX = int.MinValue;
            _lastHoverPixelY = int.MinValue;
        }

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

            // ここへ来たということは、カメラ・頂点位置・選択・トポロジ・表示設定の
            // いずれかが変わった可能性がある。ホバー間引きのキャッシュを捨てて、
            // 次のポインタ移動が同じ画素でも必ず再計算されるようにする。
            InvalidateHoverDedup();

            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    var rect = new Rect(0, 0, _lastCamera.pixelWidth, _lastCamera.pixelHeight);

                    // 診断: ここの UpdateFrame は「キャッシュされたマウス位置」で
                    // ヒットテストを再実行する。ポインタが動いていなくても、
                    // adapter.BackfaceCullingEnabled が PrepareViewport で
                    // slot ごとに上書きされた残留値になっているため、結果が入れ替わりうる。
                    // 前後を記録し、NotifyPointerHover 経由でない入れ替わりはダンプする。
                    int pv = adapter.HoverVertexIndex;
                    int pl = adapter.HoverLineIndex;
                    int pf = adapter.HoverFaceIndex;

                    adapter.RequestNormal();
                    adapter.UpdateFrame(_lastCamera, rect, _lastMousePos);

                    PLDiag.PickRec("Present",
                        pv, adapter.HoverVertexIndex,
                        pl, adapter.HoverLineIndex,
                        pf, adapter.HoverFaceIndex);

                    if (!_inNotifyPointerHover &&
                        (pv != adapter.HoverVertexIndex ||
                         pl != adapter.HoverLineIndex ||
                         pf != adapter.HoverFaceIndex))
                    {
                        PLDiag.PickDump("hover-changed-without-pointer-move");
                    }
                }
            }

            // ウェイト可視化の頂点カラーは slot に依存しない。
            // PrepareViewport 内に置くと 1 回の PresentAll で 4 回走り、
            // 対象メッシュぶんの Color 配列確保と mesh.colors 代入を毎回繰り返す。
            // 4 slot の Prepare より前に 1 回だけ実行する。
            _renderer.PrepareWeightVisualization(project);

            PrepareViewport(project, PerspectiveViewport, SlotPerspective);
            PrepareViewport(project, TopViewport,         SlotTop);
            PrepareViewport(project, FrontViewport,       SlotFront);
            PrepareViewport(project, SideViewport,        SlotSide);

            // アクティブビューポートのスクリーン座標を最終確定させる。
            //
            // 【readback: true にする唯一の箇所】
            //   矩形選択・投げ縄選択（MoveToolHandler）と、ウェイトペイントの
            //   ブラシ判定が GetScreenPositions() を読む。その値をここで
            //   アクティブビューポートのカメラに合わせて確定させる。
            //   上の PrepareViewport 4 回はすべて readback: false なので、
            //   _screenPositions が他 slot のカメラで汚されることはない。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer?.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    int activeSlot = ViewportToSlot(ActiveViewport);
                    if (activeSlot >= 0)
                    {
                        // 背面カリングの ON/OFF は「そのビューポートの設定」を渡す。
                        // adapter.BackfaceCullingEnabled は直前の PrepareViewport
                        // （= 最後に処理した Side ビュー）の値が残っているだけで、
                        // アクティブビューの設定とは限らない。
                        adapter.DispatchCullingForDisplay(
                            _lastCamera, _displaySettings[activeSlot].BackfaceCulling,
                            activeSlot, readback: true);
                    }
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
            if (cam == null) return;

            int slot = CameraToSlot(cam);
            if (slot < 0) return;

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S0 enter slot=" + slot);
            // 軸/グリッドはモデル・レンダラーの有無に依存しないため先に提出する。
            _gridAxisRenderer?.Submit(cam);

            if (_renderer == null || project == null) return;

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
            _renderer.ShowUnselectedMirrorMesh  = ds.ShowUnselectedMirrorMesh;
            _renderer.ShowSelectedMeshOrigin    = ds.ShowSelectedMeshOrigin;
            _renderer.ShowUnselectedMeshOrigin  = ds.ShowUnselectedMeshOrigin;
            _renderer.ShowMirrorMeshOrigin      = ds.ShowMirrorMeshOrigin;
            _renderer.ShowNormals               = ds.ShowNormals;

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S1 meshes slot=" + slot);
            _renderer.SubmitMeshes(project, cam);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S2 wireVerts slot=" + slot);
            // [CamDbg] wire=1 のときワイヤ・頂点の提出を止める。診断専用。
            if (!Poly_Ling.Diagnostics.PLCamDbg.SwNoWire)
                _renderer.SubmitWireframeAndVertices(cam, slot);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S3 bones slot=" + slot);
            _renderer.SubmitBones(project, cam);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S4 normals slot=" + slot);
            _renderer.SubmitNormals(project, cam);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S5 weightVis slot=" + slot);
            _renderer.SubmitWeightVisualization(project, cam);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("S6 submitDone slot=" + slot);
        }

        /// <summary>
        /// このカメラが 4 ビューポートのいずれかのカメラか。
        /// 判定は <see cref="CameraToSlot"/> に一本化してある。
        /// </summary>
        public bool IsViewportCamera(Camera cam) => CameraToSlot(cam) >= 0;

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

            SyncToolCtx(vp);

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
        /// スクリーン座標だけで完結するオーバーレイ専用の軽量経路。
        /// GPU ヒットテストを一切行わない。
        ///
        /// 【この経路で扱ってよいもの】
        ///   AxisGizmo.FindAxisAtScreenPos / RotateRingGizmo.FindRingAtScreenPos の
        ///   ようなスクリーン座標だけで完結する当たり判定と、
        ///   スカルプトのブラシ円のようにカメラと PreviewRect だけから決まる表示に限る。
        ///
        /// 【禁止事項（重要）】
        ///   頂点・辺・面のホバー判定をこの経路へ移すことを禁止する。
        ///   要素のホバーは GPU ヒットテスト（NotifyPointerHover → UpdateFrame）が
        ///   必須であり、CPU ヒットテスト（SelectionHelper.FindNearest*）へ戻すことも
        ///   禁止する。軽いからという理由でこちらへ寄せると、GPU 経路が退化して
        ///   CPU ヒットテストに巻き戻る。
        ///
        /// 【_suppressHover を見ない理由】
        ///   _suppressHover は GPU ヒットテストの負荷回避が目的。この経路は GPU を
        ///   一切触らないため抑止対象ではない。ボーンエディタ（ObjectMove）のように
        ///   頂点ホバーを抑止しているモードでも、ギズモ軸のホバーは動作させる。
        ///
        /// 【GPU 経路の状態を書き換えない】
        ///   _lastCamera / _lastMousePos / _lastParamsValid は更新しない。
        ///   これらは PresentAll / UpdateFrame が参照するため、軽量経路が触ると
        ///   GPU 側のホバー状態に副作用が出る。
        /// </summary>
        public void NotifyScreenOnlyHover(PlayerViewport vp, Vector2 panelLocalPos)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;
            if (cam == null) return;

            SyncToolCtx(vp);
            var gizmoHoverPos = ToHandlerHoverPos(cam, panelLocalPos);
            if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
            {
                Debug.Log(
                    $"[GizmoDbg/HoverLight] panelLocal={panelLocalPos} " +
                    $"toHandler={gizmoHoverPos} pixelH={cam.pixelHeight}");
            }
            _activeToolHoverCallback?.Invoke(gizmoHoverPos, _toolCtx.ToToolContext(cam));
        }

        /// <summary>
        /// OnPointerHover が渡すパネルローカル座標（Y=0 が上）を、ツールハンドラが
        /// 期待するビューポート座標（Y=0 が下）へ変換する。
        ///
        /// 【なぜ必要か】
        ///   クリック／ドラッグ系のイベントは PlayerViewportPanel.ToViewportCoord で
        ///   Y=0 下に変換済みで届き、各ハンドラの ToImgui が h - y で Y=0 上
        ///   （= WorldToScreenPos の空間）へ戻す。一方 OnPointerHover は GPU
        ///   ヒットテスト用に Y=0 上のまま渡されるため、そのままハンドラに流すと
        ///   ToImgui で二重反転して上下が逆になる。
        ///
        /// 【なぜ cam.pixelHeight か】
        ///   ハンドラ側の ToImgui は ctx.PreviewRect.height を使い、PreviewRect は
        ///   PlayerToolContext で cam.pixelWidth/pixelHeight から作られる。
        ///   同じ高さを使うことで往復が厳密に一致する。
        /// </summary>
        private static Vector2 ToHandlerHoverPos(Camera cam, Vector2 panelLocalPos)
            => new Vector2(panelLocalPos.x, cam.pixelHeight - panelLocalPos.y);

        /// <summary>
        /// マウスが指定ビューポートのRenderTexture内を移動したときに呼ぶ。
        /// UpdateFrame を実行してカメラパラメータ設定 + GPU ヒットテストを一括実行する。
        ///
        /// 【禁止事項（重要）】
        ///   これが頂点・辺・面ホバーの正規 GPU 経路である。
        ///   UpdateFrame / PresentAll を削って軽量化してはならない。削ると要素ホバーが
        ///   GPU フラグバッファへ反映されなくなり、CPU ヒットテストへの巻き戻りを招く。
        ///   ギズモ軸やブラシ円だけを更新したい場合は NotifyScreenOnlyHover を使うこと。
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

            // 性能ログ: ポインタ移動 1 件を数える。記録 OFF なら bool 判定 1 回で戻る。
            PLPerfLog.CountHover();

            // 同一画素なら丸ごと省く（詳細は _lastHoverPixelX の宣言部）。
            // ここから先は GPU 同期読み戻しを 4 回起こすため、
            // 判定は必ず adapter 取得より前に行う。
            {
                int hoverSlotForDedup = ViewportToSlot(vp);
                int px = Mathf.RoundToInt(panelLocalPos.x);
                int py = Mathf.RoundToInt(panelLocalPos.y);
                if (hoverSlotForDedup == _lastHoverSlot
                 && px == _lastHoverPixelX
                 && py == _lastHoverPixelY)
                {
                    return;
                }
                _lastHoverSlot   = hoverSlotForDedup;
                _lastHoverPixelX = px;
                _lastHoverPixelY = py;
            }

            var cam = vp.Cam;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            var rect = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);

            // パラメータを保存（LateUpdate の UpdateFrame で使い回す）
            _lastCamera      = cam;
            _lastMousePos    = panelLocalPos;
            _lastParamsValid = true;

            // ポインター移動時は RequestNormal → UpdateFrame でフルパイプライン実行。
            SyncToolCtx(vp);

            // ヒットテストに使う BackfaceCullingEnabled をこのビューポートの設定に同期する。
            // PresentAll は 4 つのビューポートを順番に PrepareViewport へ通すため、
            // 最後に処理したビューポートの設定が adapter に残ってしまう。
            int hoverSlot = ViewportToSlot(vp);
            if (hoverSlot >= 0)
                adapter.BackfaceCullingEnabled = _displaySettings[hoverSlot].BackfaceCulling;

            // ツールハンドラへは Y=0 下へ揃えて渡す（理由は ToHandlerHoverPos を参照）。
            // 後段の adapter.UpdateFrame へは Y=0 上の panelLocalPos をそのまま渡す。
            Vector2 handlerHoverPos = ToHandlerHoverPos(cam, panelLocalPos);
            if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
            {
                Debug.Log(
                    $"[GizmoDbg/Hover] panelLocal={panelLocalPos} " +
                    $"toHandler={handlerHoverPos} pixelH={cam.pixelHeight} " +
                    $"suppress={_suppressHover}");
            }

            // 診断: UpdateFrame の前後で hover インデックスがどう変わったかを記録する。
            // ポインタ移動由来の入れ替わりはここで捕まる。
            int hvBefore = adapter.HoverVertexIndex;
            int hlBefore = adapter.HoverLineIndex;
            int hfBefore = adapter.HoverFaceIndex;

            // ツールハンドラより先に GPU ヒットテストを走らせる。
            //
            // ハンドラの UpdateHover（AddFace / Knife / EdgeTopology 等）は
            // adapter.HoverVertexIndex / SnapHoverVertexIndex を読む。
            // UpdateFrame を後に回すと、読める値は 1 イベント前のポインタ位置の
            // ヒットテスト結果になり、プレビューと、最新の値を読むクリック経路とで
            // 吸着先が食い違う。
            // MoveToolHandler.UpdateHover は GPU ホバーを読まない（ギズモ軸判定のみ）ため
            // 順序の影響を受けない。
            // 性能ログ: この 2 行が GPU ヒットテストパイプライン（同期読み戻しを含む）。
            // 記録 OFF のときは Stopwatch を作らず、そのまま実行する。
            if (PLPerfLog.IsRunning)
            {
                var __sw = System.Diagnostics.Stopwatch.StartNew();
                adapter.RequestNormal();
                adapter.UpdateFrame(cam, rect, panelLocalPos);
                __sw.Stop();
                PLPerfLog.AddHoverMs(__sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                adapter.RequestNormal();
                adapter.UpdateFrame(cam, rect, panelLocalPos);
            }

            if (_moveToolHandler != null && !_suppressHover)
                _moveToolHandler.UpdateHover(handlerHoverPos, _toolCtx.ToToolContext(cam));
            // アクティブなツールが MoveToolHandler 以外の場合、そちらにも通知する
            if (!_suppressHover)
                _activeToolHoverCallback?.Invoke(handlerHoverPos, _toolCtx.ToToolContext(cam));

            PLDiag.PickHover(
                hvBefore, adapter.HoverVertexIndex,
                hlBefore, adapter.HoverLineIndex,
                hfBefore, adapter.HoverFaceIndex,
                panelLocalPos);

            // 診断: 「マウス位置」と「ホバー確定した頂点の実際のスクリーン座標」の差を測る。
            //
            // 参照するのは GPU が計算して CPU へ読み戻した配列
            // (UnifiedBufferManager_Update.ComputeScreenPositionsGPU の末尾で
            //  _screenPositions へ展開される)。CPU 側で投影を再計算してはならない。
            // 座標系はどちらも Y=0 上・パネルローカル相当なので、そのまま引き算できる。
            //
            // 差が常に一定方向なら座標変換のバイアス、ばらつくならヒット半径内での
            // 頂点の選び方の問題、と切り分けられる。
            {
                int hv = adapter.HoverVertexIndex;
                var screenPositions = adapter.BufferManager?.GetScreenPositions();
                if (hv >= 0 && screenPositions != null && hv < screenPositions.Length)
                {
                    Vector2 vertexScreen = screenPositions[hv];
                    Vector2 d = vertexScreen - panelLocalPos;
                    float   dist = d.magnitude;

                    PLDiag.PickRec("HoverPos",
                        hv, Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.y),
                        x: vertexScreen.x, y: vertexScreen.y, z: dist);

                    // ホバー半径内であっても、マウスから 3px 以上離れた頂点が
                    // 選ばれ続けるならずれとして記録を残す。
                    if (dist > HoverOffsetDumpThresholdPx)
                        PLDiag.PickDump("hover-offset");
                }
            }

            // Phase 1 event 配線: ホバー状態変更 → 描画キュー再構築。
            // UpdateFrame で GPU フラグバッファは更新されるが、CPU 側の頂点色キャッシュは
            // Prepare 系を通さないと反映されないため、ここで PresentAll を呼ぶ。
            //
            // PresentAll は内部で UpdateFrame を再実行する。ポインタ移動由来の
            // 再計算はここで意図的に起きているものなので、PresentAll 側の
            // 自動ダンプ (hover-changed-without-pointer-move) は抑止する。
            _inNotifyPointerHover = true;
            try   { PresentAll(_lastProjectForPresent); }
            finally { _inNotifyPointerHover = false; }
        }

        /// <summary>
        /// NotifyPointerHover 実行中フラグ。PresentAll 内の
        /// 「ポインタ移動を伴わないホバー入れ替わり」検出を抑止するために使う。
        /// </summary>
        private bool _inNotifyPointerHover;

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

        /// <summary>
        /// シンプルナイフ実行直前に、操作対象メッシュの面カリングマスク(true=切らない)を返す。
        /// 線ホバーと同一方式: アクティブスロットのカリングバッファを「その場で」新規ディスパッチ
        /// (Clear→ComputeScreenPositions→FaceVisibility)してから読み戻す。これをしないと
        /// 前フレーム残留値/不定値で常に全面カリング済みと判定され1枚も切れない。
        /// カリングOFFなら null(=全面対象)を返す。
        /// </summary>
        public bool[] GetFaceCulledMask(int contextMeshIndex, int faceCount, PlayerViewport vp)
        {
            if (faceCount <= 0) return null;
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return null;

            // 【重要】自前で再計算しない。
            // ホバーテストが使っているのと同一の面カリング結果をそのまま読む。
            // 参照先はヒットテスト専用 slot（HitTestSlot）。ホバー経路が
            // 「今ポインタが乗っているビューポート」のカメラで毎回算出・維持しており、
            // 切断が行われるアクティブビューのカリングと一致する。
            //
            // 2026-08-28: 以前はここが slot 0 だった。slot 0 は Perspective ビューの
            // 表示用でもあり、ホバー経路との共用が Perspective の表示破壊を招いていた。
            // 用途を分離したのに合わせて参照先も移した。意味は変えていない。
            //
            // （さらに旧実装は DispatchCullingForDisplay で別カメラ/行列/ビューポート矩形で
            //   再計算しており、表示と食い違って前面が裏面扱いになっていた。）
            const int slot = Poly_Ling.Core.UnifiedBufferManager.HitTestSlot;
            adapter.ReadBackFaceCulled(slot);

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(contextMeshIndex);
            if (unifiedIdx < 0) return null;

            var mask = new bool[faceCount];
            for (int f = 0; f < faceCount; f++)
                mask[f] = adapter.IsFaceBackfaceCulled(unifiedIdx, f);
            return mask;
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
    }
}
