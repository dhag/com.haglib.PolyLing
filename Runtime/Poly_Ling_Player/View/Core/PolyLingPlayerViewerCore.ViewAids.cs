// PolyLingPlayerViewerCore.ViewAids.cs
// Player ビューアのコア：画面キャプチャ・カメラ調整・下絵（3D 背面の参照画像）。
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
        // 画面キャプチャ
        // ================================================================

        private void ShowCapturePanel()
        {
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.CaptureSection, _layoutRoot?.CaptureBtn);
            _captureSubPanel?.Refresh();
        }

        /// <summary>
        /// 画面キャプチャを実行する。パネルボタンとショートカットの共通入口。
        /// ファイル名・保存フォルダはパネル未表示でも効くよう RecentPaths から読む。
        /// </summary>
        private void ExecuteCapture(CaptureTarget target)
        {
            VisualElement crop = null;
            switch (target)
            {
                case CaptureTarget.MainView: crop = _layoutRoot?.PerspectivePanel; break;
                case CaptureTarget.TriView:  crop = _layoutRoot?.ViewportArea;     break;
                case CaptureTarget.Window:   crop = null;                          break;
            }

            PlayerScreenCapture.Capture(
                crop,
                PlayerCaptureSubPanel.GetFolder(),
                PlayerCaptureSubPanel.GetFileName(),
                (ok, msg) => _captureSubPanel?.SetStatus(ok ? $"保存しました: {msg}" : $"失敗: {msg}"));
        }

        // ================================================================
        // カメラ調整
        // ================================================================

        /// <summary>
        /// カメラ調整パネルを開く。カテゴリ 1（3D 操作と右ペインが一体）。
        /// ギズモは調整対象と逆側のビューポートに出る（CameraToolHandler 側で判定）。
        /// </summary>
        private void ShowCameraPanel()
        {
            ShowCategory1Panel(InteractionMode.Camera);
            UpdateGizmoOverlay();
        }

        /// <summary>3面ビューポートを index (0=Top / 1=Front / 2=Side) で引く。</summary>
        private PlayerViewport TriViewportOf(int index)
        {
            switch (index)
            {
                case 0:  return _viewportManager.TopViewport;
                case 1:  return _viewportManager.FrontViewport;
                case 2:  return _viewportManager.SideViewport;
                default: return null;
            }
        }

        /// <summary>3面のフリップを適用する。ビューポートヘッダのボタンと同じ経路。</summary>
        private void ApplyTriFlip(int index, bool flipped)
        {
            switch (index)
            {
                case 0: _setTopFlip  ?.Invoke(flipped); break;
                case 1: _setFrontFlip?.Invoke(flipped); break;
                case 2: _setSideFlip ?.Invoke(flipped); break;
            }
        }

        /// <summary>
        /// カメラ調整ツールが変更したカメラを再描画する。
        /// 3面は共有状態のため代表として Front を渡す（連動 slot は
        /// PlayerViewportManager 側で同期される）。
        /// </summary>
        private void NotifyCameraToolChanged(CameraChangePhase phase)
        {
            bool tri = _cameraHandler != null
                && _cameraHandler.TargetKind == CameraToolHandler.CameraTargetKind.Tri;

            var vp = tri ? _viewportManager.FrontViewport
                         : _viewportManager.PerspectiveViewport;
            if (vp == null) return;

            _viewportManager.EnterCameraChanged(vp, phase);
        }

        /// <summary>
        /// メインカメラの正投影切替。ビューポートヘッダのトグルと
        /// カメラ調整パネルのトグルを同じ経路に集約する。
        /// </summary>
        private void SetMainCameraOrthographic(bool ortho)
        {
            var vp = _viewportManager.PerspectiveViewport;
            if (vp?.Orbit == null) return;

            vp.Orbit.Orthographic = ortho;
            _layoutRoot?.PerspOrthoToggle?.SetValueWithoutNotify(ortho);
            // 方向（persp/ortho）に応じた下絵へ差し替え＋再描画。
            ApplyUnderlayToViewport(vp, _layoutRoot?.PerspectivePanel);
            _cameraSubPanel?.Refresh();
        }

        /// <summary>メインカメラの視線を反転する（Target を挟んで反対側へ回り込む）。</summary>
        private void FlipMainCameraView()
        {
            var vp = _viewportManager.PerspectiveViewport;
            if (vp?.Orbit == null) return;

            vp.Orbit.FlipView();
            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
            _cameraSubPanel?.Refresh();
        }

        // ================================================================
        // 下絵（3D背面に敷く参照画像）の適用
        // ================================================================

        /// <summary>ビューポート vp の現在の表示方向に対応する下絵スロットを返す。</summary>
        private UnderlayDirection GetUnderlayDirection(PlayerViewport vp)
        {
            if (vp == _viewportManager.PerspectiveViewport)
                return (vp.Orbit != null && vp.Orbit.Orthographic)
                     ? UnderlayDirection.Ortho : UnderlayDirection.Persp;
            if (vp == _viewportManager.TopViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Bottom : UnderlayDirection.Top;
            if (vp == _viewportManager.FrontViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Back : UnderlayDirection.Front;
            if (vp == _viewportManager.SideViewport)
                return (vp.Ortho != null && vp.Ortho.Flipped)
                     ? UnderlayDirection.Left : UnderlayDirection.Right;
            return UnderlayDirection.Persp;
        }

        /// <summary>
        /// 指定ビューへ現在方向の下絵を適用する。画像があればカメラ背景を透明化して
        /// 背面の下絵を見せ、なければ不透明に戻す。最後に再描画を要求する。
        /// </summary>
        private void ApplyUnderlayToViewport(PlayerViewport vp, PlayerViewportPanel panel)
        {
            if (vp == null || panel == null) return;

            var slot = _underlay.Get(GetUnderlayDirection(vp));
            if (slot != null && slot.HasImage)
            {
                panel.SetUnderlay(slot.Texture, slot.TopLeft, slot.ScaleOrigin, slot.Scale);
                vp.SetClearTransparent(true);
            }
            else
            {
                panel.ClearUnderlay();
                vp.SetClearTransparent(false);
            }

            // クリア色の変化を反映するため再描画。
            _viewportManager.EnterCameraChanged(vp, CameraChangePhase.Committed);
        }

        /// <summary>4ビュー全てへ下絵を再適用する（設定変更時）。</summary>
        private void ApplyAllUnderlays()
        {
            ApplyUnderlayToViewport(_viewportManager.PerspectiveViewport, _layoutRoot?.PerspectivePanel);
            ApplyUnderlayToViewport(_viewportManager.TopViewport,        _layoutRoot?.TopPanel);
            ApplyUnderlayToViewport(_viewportManager.FrontViewport,      _layoutRoot?.FrontPanel);
            ApplyUnderlayToViewport(_viewportManager.SideViewport,       _layoutRoot?.SidePanel);
        }

        private void ShowExportPanel(PlayerExportSubPanel.Mode mode)
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            Button btn;
            switch (mode)
            {
                case PlayerExportSubPanel.Mode.PMX: btn = _layoutRoot?.FullExportPmxBtn; break;
                case PlayerExportSubPanel.Mode.OBJ: btn = _layoutRoot?.ObjSaveBtn;       break;
                case PlayerExportSubPanel.Mode.VRM: btn = _layoutRoot?.FullExportVrmBtn; break;
                default:                            btn = _layoutRoot?.FullExportMqoBtn; break;
            }
            ShowRightPanelSelectable(_layoutRoot?.ExportSection, btn, PanelSelectKeyExport);
            _exportSubPanel?.SetMode(mode);
        }

        private void ShowProjectSavePanel()
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.ProjectSaveSection, _layoutRoot?.ProjectSaveBtn, PanelSelectKeyProjectSave);
            // もう一方のパネルで変更されたパスを取り込む（両者は RecentPaths を共有）。
            _projectSaveSubPanel?.Refresh();
        }

        private void ShowProjectLoadPanel()
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(
                _layoutRoot?.ProjectLoadSection, _layoutRoot?.ProjectLoadBtn, PanelSelectKeyProjectLoad);
            _projectLoadSubPanel?.Refresh();
        }

        private void ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            var btn = mode == PlayerPartialImportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialImportPmxBtn
                : _layoutRoot?.PartialImportMqoBtn;
            ShowRightPanel(_layoutRoot?.PartialImportSection, btn);
            var model = ActiveProject?.CurrentModel;
            if (model != null) _editOps?.UndoController.SetModelContext(model);
            _partialImportSubPanel?.SetModel(model, _editOps?.UndoController);
            _partialImportSubPanel?.SetMode(mode);
        }

        private void ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode mode)
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            var btn = mode == PlayerPartialExportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialExportPmxBtn
                : _layoutRoot?.PartialExportMqoBtn;
            ShowRightPanel(_layoutRoot?.PartialExportSection, btn);
            var model = ActiveProject?.CurrentModel;
            _partialExportSubPanel?.SetModel(model);
            _partialExportSubPanel?.SetMode(mode);
        }

        private void ShowModelListPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ModelListSection, _layoutRoot?.ModelListBtn);
        }

        private void ShowMeshListPanel()
        {
            // ビューポート操作は 3 択（操作なし / 要素選択 / 姿勢調整）。
            // 既定は「姿勢調整」＝オブジェクト原点の選択と姿勢調整（ObjectMove）。
            ApplyMeshListViewportOpMode(
                _meshListSubPanel?.CurrentViewportOpMode
                ?? MeshListSubPanel.ViewportOpMode.ObjectPose);
            ShowRightPanel(_layoutRoot?.MeshListSection, _layoutRoot?.MeshListBtn);
            _meshListSubPanel?.SyncObjectPoseToggles();
        }

        /// <summary>
        /// オブジェクトリストのビューポート操作モードを InteractionMode へ反映する。
        /// 「姿勢調整」のときだけ ObjectMoveTool のピック対象をこのパネルが決める
        /// （PlayerBoneEditorSubPanel.ApplyPickFilter と同じ役割）。
        /// </summary>
        private void ApplyMeshListViewportOpMode(MeshListSubPanel.ViewportOpMode mode)
        {
            switch (mode)
            {
                case MeshListSubPanel.ViewportOpMode.ObjectPose:
                    _meshListSubPanel?.ApplyPickFilter();
                    SetInteractionMode(InteractionMode.ObjectMove);
                    break;
                case MeshListSubPanel.ViewportOpMode.SelectElem:
                    SetInteractionMode(InteractionMode.SelectOnly);
                    break;
                default:
                    SetInteractionMode(InteractionMode.None);
                    break;
            }
        }

        private void HideAllRightPanels()
        {
            // 揺れもの編集の強調表示は、そのパネルを見ている間だけのもの。
            // ここで消し、揺れもの編集へ戻ったときは Refresh が付け直す。
            // 何も付いていないときは何もしないので、パネル切替の負担にならない。
            _springBoneSubPanel?.ClearHighlight();

            if (_layoutRoot == null) return;
            void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }
            Hide(_layoutRoot.ModelListSection);
            Hide(_layoutRoot.CommandSchemaSection);
            Hide(_layoutRoot.MeshListSection);
            Hide(_layoutRoot.SkinWeightPaintSection);
            Hide(_layoutRoot.SkinWeightNumericSection);
            Hide(_layoutRoot.VertexMoveSection);
            Hide(_layoutRoot.PivotSection);
            Hide(_layoutRoot.SculptSection);
            Hide(_layoutRoot.AdvancedSelectSection);
            Hide(_layoutRoot.ImportSection);
            Hide(_layoutRoot.ExportSection);
            Hide(_layoutRoot.ProjectSaveSection);
            Hide(_layoutRoot.ProjectLoadSection);
            Hide(_layoutRoot.PartialImportSection);
            Hide(_layoutRoot.PartialExportSection);
            Hide(_layoutRoot.PrimitiveSection);
            Hide(_layoutRoot.LivePrimitiveSection);
            Hide(_layoutRoot.MeshFilterToSkinnedSection);
            Hide(_layoutRoot.SkinKindSection);
            // メッシュブレンドのプレビュー結果は MeshObject に書かれているため、
            // 非表示にするだけでは未確定の形状が残ったままになる。
            _blendSubPanel?.CancelIfActive();
            Hide(_layoutRoot.BlendSection);
            // シュリンカーのプレビュー結果も MeshObject に書かれている。
            // 頂点方式と面方式が同じ MeshObject を触るため、切り替え時に破棄しないと
            // もう一方が変形後の座標をバックアップに取り込む。
            _shrinkSubPanel?.CancelIfActive();
            _shrinkFaceSubPanel?.CancelIfActive();
            Hide(_layoutRoot.ShrinkSection);
            Hide(_layoutRoot.ShrinkFaceSection);
            Hide(_layoutRoot.NormalTransplantSection);
            Hide(_layoutRoot.ThinPlateMorphSection);
            Hide(_layoutRoot.ModelBlendSection);
            Hide(_layoutRoot.BoneEditorSection);
            Hide(_layoutRoot.UVEditorSection);
            Hide(_layoutRoot.UVUnwrapSection);
            Hide(_layoutRoot.MaterialListSection);
            Hide(_layoutRoot.UVZSection);
            Hide(_layoutRoot.PartsSelectionSetSection);
            Hide(_layoutRoot.MeshSelectionSetSection);
            Hide(_layoutRoot.ObjectGroupSection);
            Hide(_layoutRoot.MergeMeshesSection);
            Hide(_layoutRoot.BooleanSection);
            Hide(_layoutRoot.MorphSection);
            Hide(_layoutRoot.MorphCreateSection);
            Hide(_layoutRoot.TPoseSection);
            Hide(_layoutRoot.HumanoidMappingSection);
            Hide(_layoutRoot.SpringBoneSection);
            Hide(_layoutRoot.SpringBoneColliderSection);
            Hide(_layoutRoot.HumanLimitSection);
            Hide(_layoutRoot.VrmSettingsSection);
            Hide(_layoutRoot.SpringBoneTestSection);

            // 【登録漏れに注意】
            //   ShowRightPanel は「全部隠してから 1 つ出す」方式なので、
            //   AddSection で作ったセクションをここへ足し忘れると、
            //   一度出したあと別のパネルへ切り替えても消えずに残る。
            //   ボタンが増えたのに中身が同じに見える、という形で現れる。
            Hide(_layoutRoot.NormalEditSection);
            Hide(_layoutRoot.NormalExcludeSetSection);
            Hide(_layoutRoot.FaceHideSection);
            Hide(_layoutRoot.OriginTestSection);
            Hide(_layoutRoot.SkinTestSection);
            Hide(_layoutRoot.RobotBuildTestSection);
            Hide(_layoutRoot.FrillSkirtTestSection);
            Hide(_layoutRoot.SpringSkinScenarioSection);
            Hide(_layoutRoot.SpringSkinPipeScenarioSection);
            Hide(_layoutRoot.PipeHairTestSection);
            Hide(_layoutRoot.BarnacleTestSection);
            Hide(_layoutRoot.RevolutionTestSection);
            Hide(_layoutRoot.Profile2DTestSection);
            Hide(_layoutRoot.PmxToMqoTestSection);
            Hide(_layoutRoot.MqoToPmxTestSection);
            Hide(_layoutRoot.MirrorSection);
            Hide(_layoutRoot.QuadDecimatorSection);
            Hide(_layoutRoot.AlignVerticesSection);
            Hide(_layoutRoot.PlanarizeAlongBonesSection);
            Hide(_layoutRoot.SmoothEdgesSection);
            Hide(_layoutRoot.LineExtrudeSection);
            // 面に張り付けのプレビュー結果も MeshObject に書かれている。
            // 非表示にするだけでは未確定の形状が残るため、先に破棄する。
            _surfaceSnapHandler?.CancelIfActive();
            Hide(_layoutRoot.PipeAlignSection);
            Hide(_layoutRoot.SurfaceSnapSection);
            Hide(_layoutRoot.PlaceObjectReshapeSection);
            Hide(_layoutRoot.MergeVerticesSection);
            Hide(_layoutRoot.SplitVerticesSection);
            Hide(_layoutRoot.VertexHoleSection);
            Hide(_layoutRoot.VertexDissolveSection);
            Hide(_layoutRoot.HoleRingCountSection);
            Hide(_layoutRoot.EdgeBridgeSection);
            Hide(_layoutRoot.Tri4To1Section);
            Hide(_layoutRoot.FaceMergeSection);
            Hide(_layoutRoot.Quad4To1Section);
            Hide(_layoutRoot.VertexIdSection);
            Hide(_layoutRoot.VertexTransferSection);
            Hide(_layoutRoot.PartsIdSection);
            Hide(_layoutRoot.AddFaceSection);
            Hide(_layoutRoot.FlipFaceSection);
            Hide(_layoutRoot.RotateSection);
            Hide(_layoutRoot.WorkAxisSection);
            Hide(_layoutRoot.DeformSection);
            Hide(_layoutRoot.LatticeSection);
            Hide(_layoutRoot.ScaleSection);
            Hide(_layoutRoot.EdgeBevelSection);
            Hide(_layoutRoot.EdgeExtrudeSection);
            Hide(_layoutRoot.FaceExtrudeSection);
            Hide(_layoutRoot.EdgeTopologySection);
            Hide(_layoutRoot.KnifeSection);
            Hide(_layoutRoot.SolidifySection);
            Hide(_layoutRoot.MediaPipeSection);
            Hide(_layoutRoot.VMDTestSection);
            Hide(_layoutRoot.UnityClipTestSection);
            Hide(_layoutRoot.UnityClipToVrmaSection);
            Hide(_layoutRoot.MotionClipTestSection);
            Hide(_layoutRoot.RemoteServerSection);
            Hide(_layoutRoot.LogSection);
            Hide(_layoutRoot.UnderlaySection);
            Hide(_layoutRoot.GridAxisSection);
            Hide(_layoutRoot.WorkFolderSection);
            Hide(_layoutRoot.CameraSection);
            Hide(_layoutRoot.CaptureSection);
            _underlayActive = false;   // 別パネルへ切替時は下絵ドラッグを無効化
        }
    }
}
