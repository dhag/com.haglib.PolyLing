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
            ApplyGeneralPanelMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.CaptureSection, _layoutRoot?.CaptureBtn);
            _captureSubPanel?.Refresh();
        }

        /// <summary>
        /// 画面キャプチャを実行する。パネルボタンとショートカットの共通入口。
        /// ファイル名・保存フォルダはパネル未表示でも効くよう RecentPaths から読む。
        /// </summary>
        private void ExecuteCapture(CaptureTarget target)
            => StartCapture(target, null, PlayerCaptureSubPanel.GetFileName(), null);

        /// <summary>
        /// 撮影の本体。ExecuteCapture と UI 自動操作（uiCapture）の共通経路。
        /// folder が空ならキャプチャパネルの設定フォルダ。呼び出し側で検査済みの実経路を渡すこと
        /// （uiCapture は PLSandbox.TryResolveFolder を通してから渡す）。
        /// baseName が空なら PlayerScreenCapture の既定名。
        /// onDone は撮影後（フレーム終端）に呼ばれる。切り出し範囲が取れないときは
        /// この呼び出しの中で同期的に呼ばれる。キャプチャパネルの状態表示はどちらの場合も更新する。
        /// </summary>
        private void StartCapture(
            CaptureTarget target, string folder, string baseName, Action<bool, string> onDone)
            => StartCapture(target, null, folder, baseName, 0, CaptureFormat.Png,
                            PlayerScreenCapture.DefaultJpegQuality, onDone);

        /// <summary>
        /// 撮影の本体。cropOverride が非 null なら target ではなくその要素の矩形で切り出す
        /// （uiCapture の panelId が使う）。maxLongEdge・format・quality は PlayerScreenCapture の注記を参照。
        /// </summary>
        private void StartCapture(
            CaptureTarget target, VisualElement cropOverride, string folder, string baseName,
            int maxLongEdge, CaptureFormat format, int quality, Action<bool, string> onDone)
        {
            VisualElement crop = cropOverride;
            if (crop == null)
            {
                switch (target)
                {
                    case CaptureTarget.MainView: crop = _layoutRoot?.PerspectivePanel; break;
                    case CaptureTarget.TriView:  crop = _layoutRoot?.ViewportArea;     break;
                    case CaptureTarget.Window:   crop = null;                          break;
                }
            }

            PlayerScreenCapture.Capture(
                crop,
                string.IsNullOrEmpty(folder) ? PlayerCaptureSubPanel.GetFolder() : folder,
                baseName,
                maxLongEdge,
                format,
                quality,
                (ok, msg) =>
                {
                    _captureSubPanel?.SetStatus(ok ? $"保存しました: {msg}" : $"失敗: {msg}");
                    onDone?.Invoke(ok, msg);
                });
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
                case PlayerExportSubPanel.Mode.STL: btn = _layoutRoot?.StlSaveBtn;       break;
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
            ApplyGeneralPanelMode(InteractionMode.None);
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
            ApplyGeneralPanelMode(InteractionMode.None);
            var btn = mode == PlayerPartialExportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialExportPmxBtn
                : _layoutRoot?.PartialExportMqoBtn;
            ShowRightPanel(_layoutRoot?.PartialExportSection, btn);
            var model = ActiveProject?.CurrentModel;
            _partialExportSubPanel?.SetModel(model);
            _partialExportSubPanel?.SetMode(mode);
        }

        // 常駐リスト（モデル／オブジェクト／マテリアル）の Show* は「開く」（閉じているときだけ開き、
        // 開いていれば操作モードを決める側へ回す）。トグルは右ペイン最上部のボタンだけが行う
        // （ToggleModelListPanel 等）。ショートカットと UI 自動操作（uiShowPanel）は Show* を使う。

        private void ShowModelListPanel()
        {
            // 操作モードは「操作なし」（ApplyRightPaneViewportMode の優先順で決まる）
            SetPinnedOpen(_layoutRoot?.ModelListSection, true);
        }

        private void ShowMeshListPanel()
        {
            // ビューポート操作は 3 択（操作なし / 要素選択 / 姿勢調整）。
            // 既定は「姿勢調整」＝オブジェクト原点の選択と姿勢調整（ObjectMove）。
            // 3 択が効くのは、下区画が空で、一般パネルが操作モードを指定していないときだけ。
            SetPinnedOpen(_layoutRoot?.MeshListSection, true);
            _meshListSubPanel?.SyncObjectPoseToggles();
        }

        private void ToggleModelListPanel()
        {
            if (IsPinnedOpen(_layoutRoot?.ModelListSection)) SetPinnedOpen(_layoutRoot.ModelListSection, false);
            else ShowModelListPanel();
        }

        private void ToggleMeshListPanel()
        {
            if (IsPinnedOpen(_layoutRoot?.MeshListSection)) SetPinnedOpen(_layoutRoot.MeshListSection, false);
            else ShowMeshListPanel();
        }

        private void ToggleMaterialListPanel()
        {
            if (IsPinnedOpen(_layoutRoot?.MaterialListSection)) SetPinnedOpen(_layoutRoot.MaterialListSection, false);
            else ShowMaterialListPanel();
        }

        /// <summary>オブジェクトリストの今の 3 択を InteractionMode へ反映する。</summary>
        private void ApplyMeshListCurrentViewportOpMode()
            => ApplyMeshListViewportOpMode(
                _meshListSubPanel?.CurrentViewportOpMode
                ?? MeshListSubPanel.ViewportOpMode.ObjectPose);

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

        /// <summary>
        /// 右ペインの指定区画（RightPanelKind）のセクションをすべて隠す。
        /// 隠す対象は PlayerLayoutRoot の台帳（AddSection で種別付きで作った全セクション）から
        /// 引くので、セクションを足しても登録漏れは起きない。
        /// </summary>
        private void HideRightArea(RightPanelKind kind)
        {
            // ── 未確定プレビューの破棄（区画に関係なく、切替のたびに行う）──
            // メッシュブレンド・シュリンカー・面に張り付けのプレビュー結果は MeshObject に
            // 書かれている。パネルが見えたままでも、別のパネルで編集を始める前に破棄しないと
            // 未確定の形状が残り、他の編集がそれを取り込む（頂点方式と面方式のシュリンカーは
            // 同じ MeshObject を触るため、一方が変形後の座標をバックアップに取り込む）。
            _blendSubPanel?.CancelIfActive();
            _shrinkSubPanel?.CancelIfActive();
            _shrinkFaceSubPanel?.CancelIfActive();
            _surfaceSnapHandler?.CancelIfActive();

            // UI 自動操作の強調枠と保留中のスクロールは、パネル切替のたびに消す。
            _uiAutomation?.OnRightPanelsHidden();

            // ── 見ている間だけの表示（そのパネルを隠すときだけ消す）──
            if (kind == RightPanelKind.General)
            {
                // 揺れもの編集の強調表示・当たり判定の表示。戻ったときは Refresh が付け直す。
                // 何も付いていないときは何もしないので、パネル切替の負担にならない。
                _springBoneSubPanel?.ClearHighlight();
                _springBoneColliderSubPanel?.ClearDisplay();
            }
            else
            {
                _underlayActive = false;   // 下絵パネルを隠すときは下絵ドラッグを無効化
            }

            if (_layoutRoot == null) return;
            foreach (var section in _layoutRoot.RightSections)
            {
                if (_layoutRoot.GetRightPanelKind(section) != kind) continue;
                section.style.display = DisplayStyle.None;
            }
        }
    }
}
