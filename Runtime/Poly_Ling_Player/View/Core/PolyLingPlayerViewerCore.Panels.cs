// PolyLingPlayerViewerCore.Panels.cs
// Player ビューアのコア：右ペインのパネル表示切替（Show*Panel）。
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
        // パネル表示切替
        // ================================================================

        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            // カテゴリ 3（選択許可チェック ON なら SelectOnly で開く）
            ShowRightPanelSelectable(_layoutRoot?.ImportSection, null, PanelSelectKeyImport);
            _importSubPanel?.SetMode(mode);
        }

        /// <summary>
        /// MCP用サンドボックスを開く。3D連携の図形生成（_livePrimitiveSubPanel）を
        /// ShapeCategory.Sandbox で表示する。配置ギズモも他のカテゴリと同じく使う。
        /// </summary>
        private void ShowMcpSandboxPanel()
        {
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.McpSandboxBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Sandbox);
        }

        private void ShowLivePrimitivePanel()
        {
            // カテゴリ 1: 配置ギズモを使うため InteractionMode を強制する。
            // 他パネルを開けばそちらの SetInteractionMode で自然に抜ける
            // (ShowBoneEditorPanel と同じ方式。前モードの復元機構は持たない)。
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LivePrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Basic);
        }

        private void ShowLiveAdvancedPrimitivePanel()
        {
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LiveAdvancedPrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Advanced);
        }

        private void ShowLiveMechanismPrimitivePanel()
        {
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LiveMechanismPrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.Mechanism);
        }

        private void ShowLiveSpringBonePrimitivePanel()
        {
            SetInteractionMode(InteractionMode.PrimitivePlace);
            ShowRightPanel(_layoutRoot?.LivePrimitiveSection, _layoutRoot?.LiveSpringBonePrimitiveBtn);
            _livePrimitiveSubPanel?.SetCategory(PlayerPrimitiveMeshSubPanel.ShapeCategory.SpringBone);
        }

        /// <summary>
        /// 配置ギズモの中心（ワールド座標）。
        /// NewObject / NewModel は _worldPos がそのままワールド座標になる。
        /// AddToExisting は追加先メッシュのローカル空間なので WorldMatrix を掛ける。
        /// </summary>
        private Vector3 LivePrimitiveGizmoCenter()
        {
            var pos = _livePrimitiveSubPanel?.PlacePosition ?? Vector3.zero;
            if (_livePrimitiveSubPanel == null ||
                _livePrimitiveSubPanel.CurrentAddMode != PrimitiveAddMode.AddToExisting)
                return pos;

            var mc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            if (mc == null) return pos;
            return mc.WorldMatrix.MultiplyPoint3x4(pos);
        }

        /// <summary>
        /// ギズモが返すワールド差分を _worldPos の空間へ戻す。
        /// AddToExisting のときのみ追加先の WorldMatrixInverse を掛ける。
        /// </summary>
        private Vector3 LivePrimitiveWorldDeltaToLocal(Vector3 worldDelta)
        {
            if (_livePrimitiveSubPanel == null ||
                _livePrimitiveSubPanel.CurrentAddMode != PrimitiveAddMode.AddToExisting)
                return worldDelta;

            var mc = ActiveProject?.CurrentModel?.ActiveMeshContext;
            if (mc == null) return worldDelta;
            return mc.WorldMatrixInverse.MultiplyVector(worldDelta);
        }

        // ショートカット (2キー連続) 用: 図形パネルを開き、指定形状のサブメニューを表示する。
        // 形状ボタンのクリック相当で、生成は行わない。
        private void ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind k)
        {
            if (_primitiveSubPanel == null) return;
            // 左ペインのボタンは廃止したので、ハイライト対象は無い（null 可）。
            ShowRightPanelSelectable(
                _layoutRoot?.PrimitiveSection, null, PanelSelectKeyPrimitive);
            _primitiveSubPanel.SelectShape(k);
        }

        private void ShowMeshFilterToSkinnedPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshFilterToSkinnedSection, _layoutRoot?.MeshFilterToSkinnedBtn);
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowSkinKindPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SkinKindSection, _layoutRoot?.SkinKindBtn);
            _skinKindSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowBlendPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BlendSection, _layoutRoot?.BlendBtn);
            _blendSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowShrinkPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ShrinkSection, _layoutRoot?.ShrinkBtn);
            _shrinkSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowShrinkFacePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ShrinkFaceSection, _layoutRoot?.ShrinkFaceBtn);
            _shrinkFaceSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowModelBlendPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ModelBlendSection, _layoutRoot?.ModelBlendBtn);
            _modelBlendSubPanel?.Init();
        }

        private void ShowBoneEditorPanel()
        {
            // 案 A: InteractionMode を ObjectMove に強制 + RightPanel ボタンは BoneEditorBtn
            // 結果: ToolObjectMoveBtn が青 (InteractionMode)、BoneEditorBtn が緑 (RightPanel)
            SetInteractionMode(InteractionMode.ObjectMove);
            ShowRightPanel(_layoutRoot?.BoneEditorSection, _layoutRoot?.BoneEditorBtn);
            _boneEditorSubPanel?.Refresh();
        }

        private void ShowUVEditorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVEditorSection, _layoutRoot?.UVEditorBtn);

            // UndoController に対象メッシュを設定（CaptureMeshObjectSnapshot に必要）
            var uvModel = ActiveProject?.CurrentModel;
            var uvMc    = uvModel?.ActiveMeshContext;
            if (uvMc?.MeshObject != null && _editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = uvModel;
                _uvUndoMasterIndex = uvModel.IndexOf(uvMc);
            }

            _uvEditorSubPanel?.Refresh();
        }

        private void ShowUVUnwrapPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVUnwrapSection, _layoutRoot?.UVUnwrapBtn);
            _uvUnwrapSubPanel?.Refresh();
        }

        private void ShowMaterialListPanel()
        {
            // 選択専用: 面を選択してマテリアルを適用できるよう、移動なしの選択のみ有効化する。
            SetInteractionMode(InteractionMode.SelectOnly);
            ShowRightPanel(_layoutRoot?.MaterialListSection, _layoutRoot?.MaterialListBtn);
            _materialListSubPanel?.SyncEditingSlotToCurrent();
            _materialListSubPanel?.Refresh();
        }

        private void ShowUVZPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.UVZSection, _layoutRoot?.UVZBtn);
            _uvzSubPanel?.Refresh();
        }

        private void ShowPartsSelectionSetPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持
            ShowRightPanel(_layoutRoot?.PartsSelectionSetSection, _layoutRoot?.PartsSelectionSetBtn);
            _partsSelSetSubPanel?.Refresh();
        }

        private void ShowNormalExcludeSetPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持
            ShowRightPanel(_layoutRoot?.NormalExcludeSetSection, _layoutRoot?.NormalExcludeSetBtn);
            _normalExcludeSubPanel?.Refresh();
        }

        private void ShowNormalEditPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 選択したまま法線を編集するため、選択モードを変えない。
            ShowRightPanel(_layoutRoot?.NormalEditSection, _layoutRoot?.NormalEditBtn);
            _normalEditSubPanel?.Refresh();
        }

        private void ShowNormalTransplantPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.NormalTransplantSection, _layoutRoot?.NormalTransplantBtn);
            _normalTransplantSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowThinPlateMorphPanel()
        {
            // カテゴリ 3 + 選択許可チェック。
            // 「選択頂点のみを制御点にする」を使う場合、パネルを開いたまま
            // ビューポートで頂点を選び直せる必要があるため。
            ShowRightPanelSelectable(
                _layoutRoot?.ThinPlateMorphSection, _layoutRoot?.ThinPlateMorphBtn,
                PanelSelectKeyThinPlateMorph);
            _thinPlateMorphSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowFaceHidePanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。
            // 面を選択したまま隠すため、選択モードを変えない。
            ShowRightPanel(_layoutRoot?.FaceHideSection, _layoutRoot?.FaceHideBtn);
            _faceHideSubPanel?.Refresh();
        }

        private void ShowMeshSelectionSetPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MeshSelectionSetSection, _layoutRoot?.MeshSelectionSetBtn);
            _meshSelSetSubPanel?.Refresh();
        }

        private void ShowObjectGroupPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.ObjectGroupSection, _layoutRoot?.ObjectGroupBtn);
            // 要更新の判定はソースの全頂点を走査する。パネルを出したこの一度だけ行う。
            _objectGroupSubPanel?.Refresh();
        }

        private void ShowMergeMeshesPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MergeMeshesSection, _layoutRoot?.MergeMeshesBtn);
            _mergeMeshesSubPanel?.Refresh();
        }

        private void ShowBooleanPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.BooleanSection, _layoutRoot?.BooleanBtn);
            _booleanSubPanel?.Refresh();
        }

        private void ShowMorphPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MorphSection, _layoutRoot?.MorphBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            // （MorphExpressionEditRecord/ChangeRecord が正しいモデルを参照するために必要）
            var morphModel = ActiveProject?.CurrentModel;
            if (morphModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphModel);

            _morphSubPanel?.Refresh();
        }

        private void ShowMorphCreatePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MorphCreateSection, _layoutRoot?.MorphCreateBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            var morphCrModel = ActiveProject?.CurrentModel;
            if (morphCrModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphCrModel);

            _morphCreateSubPanel?.Refresh();
        }

        private void ShowTPosePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.TPoseSection, _layoutRoot?.TPoseBtn);
            // MeshListStack のコンテキストを現在のモデルに設定（TPoseUndoRecord が参照するため）
            var tpModel = ActiveProject?.CurrentModel;
            if (tpModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(tpModel);
            _tposeSubPanel?.Refresh();
        }

        private void ShowHumanoidMappingPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.HumanoidMappingSection, _layoutRoot?.HumanoidMappingBtn);
            var hmModel = ActiveProject?.CurrentModel;
            if (hmModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(hmModel);
            _humanoidMappingSubPanel?.Refresh();
        }

        private void ShowSpringBonePanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SpringBoneSection, _layoutRoot?.SpringBoneBtn);
            // MeshListStack のコンテキストを現在のモデルに設定
            // （SpringBoneChangeRecord / SpringBoneModelSettingsRecord が参照するため）。
            var sbModel = ActiveProject?.CurrentModel;
            if (sbModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(sbModel);
            _springBoneSubPanel?.Refresh();
        }

        private void ShowSpringBoneColliderPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.SpringBoneColliderSection, _layoutRoot?.SpringBoneColliderBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            // （当たり判定の変更も MultiSpringBoneChangeRecord で積まれるため）。
            var scModel = ActiveProject?.CurrentModel;
            if (scModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(scModel);

            // 揺れもの編集から離れるので、鎖の強調表示は消す。
            _springBoneSubPanel?.ClearHighlight();
            _springBoneColliderSubPanel?.Refresh();
        }

        private void ShowHumanLimitPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.HumanLimitSection, _layoutRoot?.HumanLimitBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            // （可動域の変更は MultiHumanLimitChangeRecord で積まれるため）。
            var hlModel = ActiveProject?.CurrentModel;
            if (hlModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(hlModel);

            _humanLimitSubPanel?.Refresh();
        }

        private void ShowVrmSettingsPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.VrmSettingsSection, _layoutRoot?.VrmSettingsBtn);

            // MeshListStack のコンテキストを現在のモデルに設定
            // （VrmModelSettingsRecord / MultiVrmFirstPersonChangeRecord が参照するため）。
            var vsModel = ActiveProject?.CurrentModel;
            if (vsModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(vsModel);

            _vrmSettingsSubPanel?.Refresh();
        }

        private void ShowMirrorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.MirrorSection, _layoutRoot?.MirrorBtn);
            _mirrorSubPanel?.Refresh();
        }

        private void ShowQuadDecimatorPanel()
        {
            // カテゴリ 3
            SetInteractionMode(InteractionMode.None);
            ShowRightPanel(_layoutRoot?.QuadDecimatorSection, _layoutRoot?.QuadDecimatorBtn);
            _quadDecimatorSubPanel?.Refresh();
        }

        private void ShowAlignVerticesPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.AlignVerticesSection, _layoutRoot?.AlignVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _alignVerticesHandler?.Activate(ctx);
            _alignVerticesSubPanel?.Refresh();
        }

        private void ShowPipeAlignPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.PipeAlignSection, _layoutRoot?.PipeAlignBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _pipeAlignHandler?.Activate(ctx);
            _pipeAlignSubPanel?.Refresh();
        }

        private void ShowSurfaceSnapPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.SurfaceSnapSection, _layoutRoot?.SurfaceSnapBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _surfaceSnapHandler?.Activate(ctx);
            _surfaceSnapSubPanel?.Refresh();
        }

        private void ShowPlaceObjectReshapePanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.PlaceObjectReshapeSection, _layoutRoot?.PlaceObjectReshapeBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _placeObjectReshapeHandler?.Activate(ctx);
            _placeObjectReshapeSubPanel?.Refresh();
        }

        private void ShowPlanarizeAlongBonesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.PlanarizeAlongBonesSection, _layoutRoot?.PlanarizeAlongBonesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _planarizeAlongBonesHandler?.Activate(ctx);
            _planarizeAlongBonesSubPanel?.Refresh();
        }

        private void ShowSmoothEdgesPanel()
        {
            // カテゴリ 2: 3D 操作 (InteractionMode) は維持。右ペインのみ切替。
            ShowRightPanel(_layoutRoot?.SmoothEdgesSection, _layoutRoot?.SmoothEdgesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _smoothEdgesHandler?.Activate(ctx);
            _smoothEdgesSubPanel?.Refresh();
        }

        private void ShowMergeVerticesPanel()
        {
            // カテゴリ 2
            ShowRightPanel(_layoutRoot?.MergeVerticesSection, _layoutRoot?.MergeVerticesBtn);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null)
            {
                _mergeVerticesHandler?.Activate(ctx);
                _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
            }
            _mergeVerticesSubPanel?.Refresh();
        }


        private void ShowFlipFacePanel()
        {
            // カテゴリ 1 化: MoveToolHandler の選択/矩形選択を流用し、Selection.Mode を
            // Face のみに絞る。反転実行自体はサブパネル経由 (本セッション対象外、別件)。
            ShowCategory1Panel(InteractionMode.FlipFace);
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _flipFaceHandler?.Activate(ctx);
        }
    }
}
