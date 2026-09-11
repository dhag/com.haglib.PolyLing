// PolyLingPlayerViewerCore.Overlay.cs
// Player ビューアのコア：オーバーレイ更新（ホバー・ボーン・インジケータ・ギズモ・作業軸）。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // オーバーレイ更新
        //
        // ★★★ 【重大規約違反区画】 ★★★
        // 以下の Update*Overlay 関数群は旧 Tick() から毎フレーム呼ばれる想定の
        // 実装であり、「毎フレームポーリング禁止」規約に違反する。
        // Phase 2 で各関数を対応するイベントハンドラへ移植し、ここからは削除する予定。
        // 現在は呼び出し元が _Tick（dead code）のみ。
        //
        //   UpdateFaceHoverOverlay      → Phase 2: 面ホバー変更イベントへ
        //   UpdateSelectedFacesOverlay  → Phase 2: 面選択変更イベントへ
        //   UpdateGizmoOverlay          → Phase 2: 選択/ツール切替イベントへ
        //   UpdateAdvancedSelectOverlay → Phase 2: マウスドラッグイベントへ
        //   UpdateAddFaceOverlay        → Phase 2: AddFace handler hover/click イベントへ
        //   UpdateTopologyToolsOverlay  → Phase 2: topology tool handler hover イベントへ
        //   UpdateBoneOverlay           → Phase 2: ボーンポーズ/選択変更イベントへ
        //
        // 新規コードからこれら関数を呼ぶことは厳禁。
        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        // ================================================================

        private void UpdateFaceHoverOverlay()
        {
            if (_interactionMode == InteractionMode.ObjectMove   ||
                _interactionMode == InteractionMode.PivotOffset  ||
                _interactionMode == InteractionMode.SkinWeightPaint ||
                _interactionMode == InteractionMode.None)
            {
                _activePanel?.HideFaceHover();
                return;
            }
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideFaceHover(); return; }
            var pts = _viewportManager.GetHoverFaceScreenPts(_activeViewport, model);
            if (pts == null) panel.HideFaceHover();
            else             panel.ShowFaceHover(pts);
        }

        private void UpdateSelectedFacesOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideSelectedFaces(); return; }
            var faces = _viewportManager.GetSelectedFacesScreenPts(_activeViewport, model);
            if (faces == null) panel.HideSelectedFaces();
            else               panel.ShowSelectedFaces(faces);
        }

        private void UpdateBoneOverlay()
        {
            _overlayIndicators.Clear();

            bool boneEditorOpen = _layoutRoot?.BoneEditorSection?.style.display == DisplayStyle.Flex;
            bool objectMoveMode = _interactionMode == InteractionMode.ObjectMove;
            bool pivotMode      = _interactionMode == InteractionMode.PivotOffset;
            bool show = boneEditorOpen || objectMoveMode || pivotMode;

            var model = ActiveProject?.CurrentModel;
            bool haveModel = model != null && model.MeshContextCount > 0;

            // マーカーを出す対象は「いま ObjectMoveTool でつかめるもの」に揃える。
            // 判定を独自に書くと『つかめるのにマーカーが出ない』『マーカーは出るのに
            // つかめない』が起きるため、ピックフィルタそのものを渡す。
            //
            // 原点だけ移動 (PivotOffset) はギズモ専用で Pick* を全て false にしてある。
            // そのフィルタを渡すと 1 つも出なくなるので、そこだけ従来の規則
            // （ボーン＋非スキンドメッシュ）を使う。
            var pickFilter = pivotMode ? null : _objectMoveHandler?.GetSettings();

            // 図形生成パネル（3D連携）の「原点マーカーを仮表示」。
            // まだ作られていない生成予定位置を、描画オブジェクトの姿勢と
            // 同じ水色ダイヤで出す。実体が無いのでピック対象にはしない。
            Vector3? placePreview = LivePrimitiveOriginPreview();

            UpdateBoneOverlayFor(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport, model, show && haveModel, pickFilter, placePreview);
            UpdateBoneOverlayFor(_layoutRoot?.TopPanel,         _viewportManager.TopViewport,         model, show && haveModel, pickFilter, placePreview);
            UpdateBoneOverlayFor(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport,       model, show && haveModel, pickFilter, placePreview);
            UpdateBoneOverlayFor(_layoutRoot?.SidePanel,        _viewportManager.SideViewport,        model, show && haveModel, pickFilter, placePreview);
        }

        /// <summary>
        /// 原点マーカーの仮表示を出すワールド座標。出さないときは null。
        /// 図形配置モードで、チェックが入っていて、姿勢が効く図形のときだけ返す。
        /// </summary>
        private Vector3? LivePrimitiveOriginPreview()
        {
            if (_interactionMode != InteractionMode.PrimitivePlace) return null;
            if (!_primitivePlaceSettings.ShowOriginMarker) return null;
            if (_livePrimitiveSubPanel == null || !_livePrimitiveSubPanel.PoseApplicable) return null;
            return LivePrimitiveGizmoCenter();
        }

        private void UpdateBoneOverlayFor(
            PlayerViewportPanel panel, PlayerViewport vp,
            ModelContext model, bool show, Poly_Ling.Tools.ObjectMoveSettings pickFilter,
            Vector3? placePreview)
        {
            if (panel == null) return;
            if (!show && !placePreview.HasValue) { panel.HideBoneWire(); return; }

            var ctx = _viewportManager.GetCurrentToolContext(vp);
            if (ctx == null) { panel.HideBoneWire(); return; }

            // ヒットテスト用インジケーターはアクティブビューポート基準で構築する。
            bool buildIndicators = ReferenceEquals(vp, _activeViewport);

            float panelH    = ctx.PreviewRect.height;
            var positions   = new System.Collections.Generic.List<Vector2>();
            var selected    = new System.Collections.Generic.List<bool>();

            for (int i = 0; show && i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                bool isBone       = mc.Type == MeshType.Bone;
                bool isNonSkinned = mc.Type == MeshType.Mesh
                                    && mc.MeshObject != null
                                    && !mc.IsSkinned;

                bool include = pickFilter != null
                    ? Poly_Ling.Tools.ObjectMoveTool.PassesPickFilter(mc, pickFilter)
                    : (isBone || isNonSkinned);
                if (!include) continue;

                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                sp.y = panelH - sp.y;

                bool isSel = model.SelectedMeshContextIndices.Contains(i);

                positions.Add(sp);
                selected.Add(isSel);

                if (buildIndicators)
                    _overlayIndicators.Add(new OverlayIndicator
                    {
                        MeshContextIndex = i,
                        ScreenPos        = sp,
                        IsBone           = isBone,
                    });
            }

            // 仮表示は最後に足す。_overlayIndicators へは入れないので、
            // クリックしても選択対象にはならない（実体がまだ無いため）。
            if (placePreview.HasValue)
            {
                Vector2 sp = ctx.WorldToScreen(placePreview.Value);
                sp.y = panelH - sp.y;
                positions.Add(sp);
                selected.Add(false);
            }

            if (positions.Count == 0) { panel.HideBoneWire(); return; }

            panel.UpdateBoneWire(positions.ToArray(), selected.ToArray());
        }

        private int HitTestOverlayIndicator(Vector2 screenPos)
        {
            float minDist = OverlayHitRadius;
            int   result  = -1;
            foreach (var ind in _overlayIndicators)
            {
                float d = Vector2.Distance(screenPos, ind.ScreenPos);
                if (d < minDist) { minDist = d; result = ind.MeshContextIndex; }
            }
            return result;
        }

        private bool TrySelectIndicatorAtScreenPos(Vector2 screenPos, ModifierKeys mods)
        {
            if (_interactionMode != InteractionMode.ObjectMove && _interactionMode != InteractionMode.PivotOffset)
                return false;

            int idx = HitTestOverlayIndicator(screenPos);
            if (idx < 0) return false;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return false;

            if (mods.Shift || mods.Ctrl)
                model.ToggleMeshContextSelection(idx);
            else
                model.Select(idx);

            // Phase 2a-2e: UpdateSelectedDrawableMesh + NotifySelectionChanged を
            // EnterTopologyChanged に集約（選択変更扱い）。
            _viewportManager.EnterTopologyChanged(ActiveProject);
            NotifyPanels(ChangeKind.Selection);
            _boneEditorSubPanel?.Refresh();
            _activePanel?.MarkDirtyRepaint();
            return true;
        }

        private void UpdateGizmoOverlay()
        {
            // 作業軸はポインタが乗っていないビューポートにも出す。
            // 下のアクティブ側処理は _activePanel が null 等で早期 return する
            // 経路があるため、取り残さないよう先に済ませる。
            UpdateWorkAxisOverlayOnInactivePanels();

            var panel = _activePanel;
            if (panel == null) return;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideGizmo(); return; }

            // ギズモ形状の決定は各 ToolHandler (IPlayerGizmoProvider) が持つ。
            // ここはモードに対応するプロバイダを選んで結果を渡すだけ。
            var provider = GizmoProviderFor(_interactionMode);
            if (provider == null) { panel.HideGizmo(); return; }

            if (provider.TryBuildGizmoData(ctx, out var data))
            {
                if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
                {
                    Debug.Log(
                        $"[GizmoDbg/Draw] mode={_interactionMode} provider={provider.GetType().Name} " +
                        $"origin={data.Origin} xEnd={data.XEnd} yEnd={data.YEnd} zEnd={data.ZEnd} " +
                        $"hover={data.HoveredAxis} drag={data.DraggingAxis} " +
                        $"cube={data.IsCubeStyle} diamond={data.IsDiamondStyle} ring={data.IsRingStyle} " +
                        $"pivot={data.HasPivotGizmo}/{data.PivotOrigin}");
                }
                panel.UpdateGizmo(data);
            }
            else panel.HideGizmo();
        }

        /// <summary>
        /// 作業軸を 3D 画面で編集できる状態か。
        /// 作業軸ツールそのものと、変形モードの作業軸フェーズが該当する。
        /// </summary>
        private bool IsWorkAxisEditable()
        {
            if (_interactionMode == InteractionMode.WorkAxis) return true;

            return _interactionMode == InteractionMode.Deform
                && _deformHandler != null
                && _deformHandler.Phase == DeformToolHandler.DeformPhase.WorkAxis;
        }

        /// <summary>
        /// 変形モードの入力経路をフェーズに応じて張り替える。
        /// SwitchTool と DeformToolHandler.OnPhaseChanged の両方から呼ぶ。
        ///
        /// 作業軸フェーズ … 作業軸ツールと同じ経路。矢印・リング・Y 先端ハンドルを掴める。
        ///                  ビューポートでの頂点選択はできない（作業軸ツールと同じ制約）。
        /// 変形フェーズ   … MoveToolHandler を流用して選択を残しつつ、組み込み移動ギズモを
        ///                  抑制して変形ハンドルへ委譲する（PrimitivePlace と同じ構成）。
        ///                  SelectOnly は使えない。GizmoHitTestOverride が SelectOnly 時に
        ///                  スキップされるため、ハンドルを掴めなくなる。代わりに
        ///                  OnDragStartExtra が常に true を返して頂点移動だけを抑止する。
        ///                  クリック選択と、何も掴んでいない位置からの矩形／投げ縄選択は効く。
        /// </summary>
        private void ApplyDeformToolRouting()
        {
            if (_interactionMode != InteractionMode.Deform) return;

            bool workAxisPhase = _deformHandler != null
                && _deformHandler.Phase == DeformToolHandler.DeformPhase.WorkAxis;

            // フックは毎回張り直す。作業軸フェーズで残すと、掴んでいない
            // ドラッグでも OnDragStartExtra が true を返し続けてしまう。
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SuppressBuiltinGizmo = false;
                _moveToolHandler.GizmoHitTestOverride = null;
                _moveToolHandler.OnDragStartExtra     = null;
                _moveToolHandler.OnToolDragExtra      = null;
                _moveToolHandler.OnToolDragEndExtra   = null;
            }

            if (workAxisPhase)
            {
                _vertexInteractor?.SetToolHandler(_workAxisHandler);
                _viewportManager?.RegisterActiveToolHandler(
                    (pos, ctx) => _workAxisHandler?.UpdateHover(pos, ctx));
                return;
            }

            _vertexInteractor?.SetToolHandler(_moveToolHandler);
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SuppressBuiltinGizmo = true;
                _moveToolHandler.GizmoHitTestOverride = (pos, c) =>
                    _deformHandler != null && _deformHandler.GizmoHitTest(pos, c);
                _moveToolHandler.OnDragStartExtra     = (elem, mods) =>
                {
                    _deformHandler?.BeginGizmoDrag();
                    return true;
                };
                _moveToolHandler.OnToolDragExtra      = (pos, delta, mods) => _deformHandler?.GizmoDrag(pos);
                _moveToolHandler.OnToolDragEndExtra   = (pos, mods) => _deformHandler?.EndGizmoDrag();
            }
            _viewportManager?.RegisterActiveToolHandler(
                (pos, ctx) => _deformHandler?.UpdateHover(pos, ctx));
        }

        /// <summary>
        /// ポインタが乗っていないビューポートの作業軸表示を更新する。
        ///
        /// 【なぜ要るか】
        /// UpdateGizmoOverlay は _activePanel にしか GizmoData を書かず、別の
        /// ビューポートへポインタが移ると前のパネルは HideGizmo される
        /// （OnPointerHover 内）。そのため作業軸が「見ている画面」から消えていた。
        ///
        /// 【何を出すか】
        /// 作業軸モード … 六角錐だけの表示専用データ（TryBuildDisplayOnlyGizmoData）。
        ///                矢印やリングはそのビューポートでは掴めないので出さない。
        /// 変形モード   … アクティブ側と同じもの。変形のギズモは元から操作を
        ///                受けない（UpdateHover とドラッグが空実装）ため、
        ///                掴めるように見えてしまう心配がない。
        /// それ以外     … 隠す。
        /// </summary>
        private void UpdateWorkAxisOverlayOnInactivePanels()
        {
            Apply(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport);
            Apply(_layoutRoot?.TopPanel,         _viewportManager.TopViewport);
            Apply(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport);
            Apply(_layoutRoot?.SidePanel,        _viewportManager.SideViewport);

            void Apply(PlayerViewportPanel p, PlayerViewport vp)
            {
                if (p == null || ReferenceEquals(p, _activePanel)) return;

                var ctx = _viewportManager.GetCurrentToolContext(vp);
                if (ctx == null) { p.HideGizmo(); return; }

                if (_interactionMode == InteractionMode.WorkAxis &&
                    _workAxisHandler != null &&
                    _workAxisHandler.TryBuildDisplayOnlyGizmoData(ctx, out var waData))
                {
                    p.UpdateGizmo(waData);
                    return;
                }

                if (_interactionMode == InteractionMode.Deform &&
                    _deformHandler != null &&
                    _deformHandler.TryBuildGizmoData(ctx, out var dfData))
                {
                    p.UpdateGizmo(dfData);
                    return;
                }

                p.HideGizmo();
            }
        }

        /// <summary>
        /// InteractionMode に対応するギズモ供給元を返す。null はギズモ非表示。
        /// 既定 (頂点移動・選択専用・トポロジ系ツール等) は MoveToolHandler の
        /// 組み込み軸ギズモで、SelectOnly / SuppressBuiltinGizmo のときは
        /// MoveToolHandler 側が false を返して非表示になる。
        /// </summary>
        private IPlayerGizmoProvider GizmoProviderFor(InteractionMode mode)
        {
            switch (mode)
            {
                case InteractionMode.ObjectMove:      return _objectMoveHandler;
                case InteractionMode.PivotOffset:     return _pivotOffsetHandler;
                case InteractionMode.Rotate:          return _rotateHandler;
                case InteractionMode.Scale:           return _scaleHandler;
                case InteractionMode.PrimitivePlace:  return _primitivePlaceHandler;
                case InteractionMode.WorkAxis:        return _workAxisHandler;
                case InteractionMode.Deform:          return _deformHandler;
                case InteractionMode.Lattice:         return _latticeHandler;
                case InteractionMode.Camera:          return _cameraHandler;

                case InteractionMode.Sculpt:
                case InteractionMode.AdvancedSelect:
                case InteractionMode.SkinWeightPaint:
                case InteractionMode.SkinWeightNumeric:
                case InteractionMode.None:            return null;

                default:                              return _moveToolHandler;
            }
        }
    }
}
