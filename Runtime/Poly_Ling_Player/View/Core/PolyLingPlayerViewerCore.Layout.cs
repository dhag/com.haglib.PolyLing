// PolyLingPlayerViewerCore.Layout.cs
// Player ビューアのコア：UI レイアウト構築（BuildLayout）の本体と、左ペインの配線・選択モード・
// 表示設定トグル・セクション再取得の登録。サブパネルの生成は Layout.*.cs の各段。
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
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _uiRoot = root;

            // 全 TextField のキャレット色を白で一元化する USS を付与する。
            // 子孫の TextField 全てへカスケードするため、各フィールドでの個別適用は不要。
            //
            // 貼り先は root ではなく「パネル最上位のコンテナ」にする。
            // DropdownField を押して開くメニューは、UIToolkit が最上位コンテナへ
            // 差し込む（root の子ではない）。root に貼るとメニューへ届かず、
            // 既定の明るい配色のまま白背景で出てしまう。
            // 最上位が root 自身のときは 1 回だけ貼られる。
            var caretSheet = Resources.Load<StyleSheet>("PolyLingCaret");
            if (caretSheet != null)
            {
                void AttachCaretSheet()
                {
                    var host = root;
                    while (host.hierarchy.parent != null) host = host.hierarchy.parent;
                    if (!host.styleSheets.Contains(caretSheet)) host.styleSheets.Add(caretSheet);
                    if (!root.styleSheets.Contains(caretSheet)) root.styleSheets.Add(caretSheet);
                }

                // Build 時点でまだパネルへ載っていない場合（UIDocument 経由）に備え、
                // 載ったタイミングでも貼り直す。二重付与は Contains で防ぐ。
                AttachCaretSheet();
                root.RegisterCallback<AttachToPanelEvent>(_ => AttachCaretSheet());
            }

            // 全ボタンの操作フィードバック（ホバー/押下中/押下確定/無効）を root に一括導入する。
            // 個々のボタン生成箇所やサブパネル側の変更は不要。
            PlayerLayoutRoot.InstallButtonFeedback(root);

            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            _panelContext = new PanelContext(DispatchPanelCommand);

            // 各サブパネルの生成と配線。元は 1 本のメソッドで、順序を変えずに段へ分けてある
            // （PolyLingPlayerViewerCore.Layout*.cs）。後の段は前の段で作ったものを使うので、
            // 呼び出しの並びを入れ替えないこと。
            BuildListAndDeformPanels();
            BuildEditPanels();
            BuildVertexToolPanels();
            BuildFaceAndAxisTools();
            BuildDeformAndTopologyTools();
            BuildTestAndMotionPanels();
            BuildIoAndMiscPanels();
            BuildPrimitiveAndSkinPanels();
            WireLeftPaneButtons();
            WireSelectModeToggles();
            InitDisplayToggles();

            _layoutRoot.MorphBtn.clicked       += ShowMorphPanel;
            _layoutRoot.MorphCreateBtn.clicked += ShowMorphCreatePanel;

            _layoutRoot.PostBuildButtonColors(_uiRoot);

            // 同じ理由で、Build 時に着色しているセグメント型ボタン
            // （スキンWペイントのモード／フォールオフ、スキンW数値設定の「色」）も
            // ここで塗り直す。これをしないと起動直後だけ選択状態が消える。
            _skinWeightPaintPanel?.RepaintSegmentButtons();
            _skinWeightNumericSubPanel?.RepaintSegmentButtons();

            WireShortcuts();

            RegisterSectionRefreshPairs();

            ShowCategory1Panel(InteractionMode.VertexMove);
        }

        /// <summary>BuildLayout の段：左ペインの通常ボタン（clicked）と、回転中心・法線の常時表示部。</summary>
        private void WireLeftPaneButtons()
        {
            _layoutRoot.BlendBtn.clicked      += ShowBlendPanel;
            _layoutRoot.ShrinkBtn.clicked     += ShowShrinkPanel;
            _layoutRoot.ShrinkFaceBtn.clicked += ShowShrinkFacePanel;
            _layoutRoot.ModelBlendBtn.clicked += ShowModelBlendPanel;
            _layoutRoot.BoneEditorBtn.clicked  += () => { ShowBoneEditorPanel(); _boneEditorSubPanel?.ShowBonesTab(); };
            _layoutRoot.UVEditorBtn.clicked    += ShowUVEditorPanel;
            _layoutRoot.UVUnwrapBtn.clicked    += ShowUVUnwrapPanel;
            _layoutRoot.MaterialListBtn.clicked    += ShowMaterialListPanel;
            _layoutRoot.UVZBtn.clicked             += ShowUVZPanel;
            _layoutRoot.PartsSelectionSetBtn.clicked += ShowPartsSelectionSetPanel;
            _layoutRoot.MeshSelectionSetBtn.clicked  += ShowMeshSelectionSetPanel;
            _layoutRoot.ObjectGroupBtn.clicked       += ShowObjectGroupPanel;
            _layoutRoot.NormalExcludeSetBtn.clicked  += ShowNormalExcludeSetPanel;
            _layoutRoot.NormalEditBtn.clicked        += ShowNormalEditPanel;
            _layoutRoot.NormalTransplantBtn.clicked  += ShowNormalTransplantPanel;
            _layoutRoot.ThinPlateMorphBtn.clicked    += ShowThinPlateMorphPanel;
            _layoutRoot.FaceHideBtn.clicked          += ShowFaceHidePanel;
            _layoutRoot.MergeMeshesBtn.clicked     += ShowMergeMeshesPanel;
            _layoutRoot.BooleanBtn.clicked         += ShowBooleanPanel;
            _layoutRoot.TPoseBtn.clicked           += ShowTPosePanel;
            _layoutRoot.HumanoidMappingBtn.clicked += ShowHumanoidMappingPanel;
            _layoutRoot.SpringBoneBtn.clicked      += ShowSpringBonePanel;
            _layoutRoot.SpringBoneColliderBtn.clicked += ShowSpringBoneColliderPanel;
            _layoutRoot.HumanLimitBtn.clicked         += ShowHumanLimitPanel;
            _layoutRoot.VrmSettingsBtn.clicked       += ShowVrmSettingsPanel;
            _layoutRoot.MirrorBtn.clicked          += ShowMirrorPanel;
            _layoutRoot.QuadDecimatorBtn.clicked   += ShowQuadDecimatorPanel;
            _layoutRoot.AlignVerticesBtn.clicked       += ShowAlignVerticesPanel;
            _layoutRoot.PlanarizeAlongBonesBtn.clicked += ShowPlanarizeAlongBonesPanel;
            _layoutRoot.SmoothEdgesBtn.clicked         += ShowSmoothEdgesPanel;
            _layoutRoot.PipeAlignBtn.clicked           += ShowPipeAlignPanel;
            _layoutRoot.SurfaceSnapBtn.clicked         += ShowSurfaceSnapPanel;
            _layoutRoot.PlaceObjectReshapeBtn.clicked  += ShowPlaceObjectReshapePanel;
            _layoutRoot.MergeVerticesBtn.clicked       += ShowMergeVerticesPanel;
            _layoutRoot.SplitVerticesBtn.clicked        += ShowSplitVerticesPanel;
            if (_layoutRoot.VertexHoleBtn != null)
                _layoutRoot.VertexHoleBtn.clicked       += ShowVertexHolePanel;
            if (_layoutRoot.VertexDissolveBtn != null)
                _layoutRoot.VertexDissolveBtn.clicked   += ShowVertexDissolvePanel;
            if (_layoutRoot.HoleRingCountBtn != null)
                _layoutRoot.HoleRingCountBtn.clicked    += ShowHoleRingCountPanel;
            if (_layoutRoot.EdgeBridgeBtn != null)
                _layoutRoot.EdgeBridgeBtn.clicked       += ShowEdgeBridgePanel;
            if (_layoutRoot.Tri4To1Btn != null)
                _layoutRoot.Tri4To1Btn.clicked          += ShowTri4To1Panel;
            if (_layoutRoot.FaceMergeBtn != null)
                _layoutRoot.FaceMergeBtn.clicked        += ShowFaceMergePanel;
            if (_layoutRoot.Quad4To1Btn != null)
                _layoutRoot.Quad4To1Btn.clicked         += ShowQuad4To1Panel;
            if (_layoutRoot.VertexIdBtn != null)
                _layoutRoot.VertexIdBtn.clicked          += ShowVertexIdPanel;
            if (_layoutRoot.VertexTransferBtn != null)
                _layoutRoot.VertexTransferBtn.clicked    += ShowVertexTransferPanel;
            if (_layoutRoot.PartsIdBtn != null)
                _layoutRoot.PartsIdBtn.clicked           += ShowPartsIdPanel;
            _layoutRoot.AddFaceBtn.clicked               += ShowAddFacePanel;
            _layoutRoot.FlipFaceBtn.clicked              += ShowFlipFacePanel;
            _layoutRoot.RotateBtn.clicked                += ShowRotatePanel;
            if (_layoutRoot.WorkAxisBtn != null)
                _layoutRoot.WorkAxisBtn.clicked          += ShowWorkAxisPanel;
            if (_layoutRoot.DeformBtn != null)
                _layoutRoot.DeformBtn.clicked            += ShowDeformPanel;
            if (_layoutRoot.LatticeBtn != null)
                _layoutRoot.LatticeBtn.clicked           += ShowLatticePanel;
            _layoutRoot.ScaleBtn.clicked                 += ShowScalePanel;
            _layoutRoot.EdgeBevelBtn.clicked             += ShowEdgeBevelPanel;
            _layoutRoot.EdgeExtrudeBtn.clicked           += ShowEdgeExtrudePanel;
            _layoutRoot.FaceExtrudeBtn.clicked           += ShowFaceExtrudePanel;
            _layoutRoot.EdgeTopologyBtn.clicked          += ShowEdgeTopologyPanel;
            _layoutRoot.KnifeBtn.clicked                 += ShowKnifePanel;
            // 穴つなぎ。図形生成パネルを開いて「ブリッジ」を選択する。
            if (_layoutRoot.BridgeBtn != null)
                _layoutRoot.BridgeBtn.clicked            += () => ShowPrimitiveShape(PlayerPrimitiveMeshSubPanel.ShapeKind.Bridge);
            _layoutRoot.SolidifyBtn.clicked              += ShowSolidifyPanel;
            if (_layoutRoot.LineExtrudeBtn != null)
                _layoutRoot.LineExtrudeBtn.clicked      += ShowLineExtrudePanel;
            _layoutRoot.MediaPipeBtn.clicked        += ShowMediaPipePanel;
            _layoutRoot.VMDTestBtn.clicked          += ShowVMDTestPanel;
            if (_layoutRoot.CommandSchemaBtn != null)
                _layoutRoot.CommandSchemaBtn.clicked += ShowCommandSchemaPanel;
            if (_layoutRoot.OriginTestBtn != null)
                _layoutRoot.OriginTestBtn.clicked += ShowOriginTestPanel;
            if (_layoutRoot.SkinTestBtn != null)
                _layoutRoot.SkinTestBtn.clicked += ShowSkinTestPanel;
            if (_layoutRoot.SpringBoneTestBtn != null)
                _layoutRoot.SpringBoneTestBtn.clicked += ShowSpringBoneTestPanel;
            if (_layoutRoot.FrillSkirtTestBtn != null)
                _layoutRoot.FrillSkirtTestBtn.clicked += ShowFrillSkirtTestPanel;
            if (_layoutRoot.SpringSkinScenarioBtn != null)
                _layoutRoot.SpringSkinScenarioBtn.clicked += ShowSpringSkinScenarioPanel;
            if (_layoutRoot.SpringSkinPipeScenarioBtn != null)
                _layoutRoot.SpringSkinPipeScenarioBtn.clicked += ShowSpringSkinPipeScenarioPanel;
            if (_layoutRoot.PipeHairTestBtn != null)
                _layoutRoot.PipeHairTestBtn.clicked += ShowPipeHairTestPanel;
            if (_layoutRoot.BarnacleTestBtn != null)
                _layoutRoot.BarnacleTestBtn.clicked += ShowBarnacleTestPanel;
            if (_layoutRoot.RevolutionTestBtn != null)
                _layoutRoot.RevolutionTestBtn.clicked += ShowRevolutionTestPanel;
            if (_layoutRoot.Profile2DTestBtn != null)
                _layoutRoot.Profile2DTestBtn.clicked += ShowProfile2DTestPanel;
            if (_layoutRoot.PmxToMqoTestBtn != null)
                _layoutRoot.PmxToMqoTestBtn.clicked += ShowPmxToMqoTestPanel;
            if (_layoutRoot.MqoToPmxTestBtn != null)
                _layoutRoot.MqoToPmxTestBtn.clicked += ShowMqoToPmxTestPanel;
            if (_layoutRoot.RobotBuildTestBtn != null)
                _layoutRoot.RobotBuildTestBtn.clicked += ShowRobotBuildTestPanel;
            _layoutRoot.UnityClipTestBtn.clicked    += ShowUnityClipTestPanel;
            _layoutRoot.UnityClipToVrmaBtn.clicked  += ShowUnityClipToVrmaPanel;
            if (_layoutRoot.VmdToVrmaBtn != null)
                _layoutRoot.VmdToVrmaBtn.clicked    += ShowVmdToVrmaPanel;
            _layoutRoot.MotionClipTestBtn.clicked   += ShowMotionClipTestPanel;
            _layoutRoot.RemoteServerBtn.clicked     += ShowRemoteServerPanel;
            if (_layoutRoot.LogBtn != null)
                _layoutRoot.LogBtn.clicked          += ShowLogPanel;
            if (_layoutRoot.UnderlayBtn != null)
                _layoutRoot.UnderlayBtn.clicked     += ShowUnderlayPanel;
            if (_layoutRoot.GridAxisBtn != null)
                _layoutRoot.GridAxisBtn.clicked     += ShowGridAxisPanel;
            if (_layoutRoot.WorkFolderBtn != null)
                _layoutRoot.WorkFolderBtn.clicked   += ShowWorkFolderPanel;
            if (_layoutRoot.CameraBtn != null)
                _layoutRoot.CameraBtn.clicked       += ShowCameraPanel;
            if (_layoutRoot.CaptureBtn != null)
                _layoutRoot.CaptureBtn.clicked      += ShowCapturePanel;
            _layoutRoot.FullExportPmxBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.PMX);
            _layoutRoot.FullExportMqoBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO);
            _layoutRoot.FullExportVrmBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.VRM);
            _layoutRoot.ProjectSaveBtn.clicked     += ShowProjectSavePanel;
            _layoutRoot.ProjectLoadBtn.clicked     += ShowProjectLoadPanel;
            if (_layoutRoot.ObjLoadBtn != null)
                _layoutRoot.ObjLoadBtn.clicked     += () => ShowImportPanel(PlayerImportSubPanel.Mode.OBJ);
            if (_layoutRoot.ObjSaveBtn != null)
                _layoutRoot.ObjSaveBtn.clicked     += () => ShowExportPanel(PlayerExportSubPanel.Mode.OBJ);
            _layoutRoot.PartialImportPmxBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX);
            _layoutRoot.PartialImportMqoBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO);
            _layoutRoot.PartialExportPmxBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX);
            _layoutRoot.PartialExportMqoBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO);

            _layoutRoot.ToolVertexMoveBtn.clicked        += () => ShowCategory1Panel(InteractionMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked        += () => { ShowCategory1Panel(InteractionMode.ObjectMove); _boneEditorSubPanel?.ShowObjectPoseTab(); };
            // 「原点だけ移動」は ObjectMove モードのチェックボックスに一本化したため、
            // ピボット(PivotOffset)ボタンは撤去する（非表示＋クリック無効）。
            if (_layoutRoot.ToolPivotOffsetBtn != null)
                _layoutRoot.ToolPivotOffsetBtn.style.display = DisplayStyle.None;
            _layoutRoot.ToolSculptBtn.clicked            += () => ShowCategory1Panel(InteractionMode.Sculpt);
            _layoutRoot.ToolAdvancedSelBtn.clicked       += () => ShowCategory1Panel(InteractionMode.AdvancedSelect);
            _layoutRoot.ToolSkinWeightPaintBtn.clicked   += () => ShowCategory1Panel(InteractionMode.SkinWeightPaint);
            if (_layoutRoot.SkinWeightNumericBtn != null)
                _layoutRoot.SkinWeightNumericBtn.clicked += () => ShowCategory1Panel(InteractionMode.SkinWeightNumeric);

            // 一時選択サブツール (デバッグ用ボタン。ショートカット R / G と同処理)。
            if (_layoutRoot.SubToolBoxSelectBtn != null)
                _layoutRoot.SubToolBoxSelectBtn.clicked   += () => EnterSelectSubTool(false);
            if (_layoutRoot.SubToolLassoSelectBtn != null)
                _layoutRoot.SubToolLassoSelectBtn.clicked += () => EnterSelectSubTool(true);
            if (_layoutRoot.SubToolDeleteBtn != null)
                _layoutRoot.SubToolDeleteBtn.clicked      += ExecuteDeleteSelection;
            if (_layoutRoot.ToolDeleteFaceBtn != null)
                _layoutRoot.ToolDeleteFaceBtn.clicked    += () =>
                {
                    // 押すたびに進入 / 復帰をトグルする（ボタンだけで抜けられるように）。
                    if (_deleteFaceModeActive) ExitDeleteFaceMode();
                    else                       EnterDeleteFaceMode();
                };

            _layoutRoot.LassoToggle.RegisterValueChangedCallback(e =>
            {
                if (_moveToolHandler != null)
                    _moveToolHandler.DragSelectMode = e.newValue
                        ? MoveToolHandler.SelectionDragMode.Lasso
                        : MoveToolHandler.SelectionDragMode.Box;
                // ObjectMove (BoneEditor 統合先) でも頂点モードと同じ Lasso 切替を共有。
                if (_objectMoveHandler != null)
                    _objectMoveHandler.DragSelectMode = e.newValue
                        ? ObjectMoveToolHandler.SelectionDragMode.Lasso
                        : ObjectMoveToolHandler.SelectionDragMode.Box;
            });

            // 「回転はローカル原点中心」。状態を持つだけで、ここでカメラは動かさない。
            // ON に戻したときは釦で確定した固定ピボットを解除し、ローカル原点中心へ戻す。
            if (_layoutRoot.OrbitAroundLocalOriginToggle != null)
                _layoutRoot.OrbitAroundLocalOriginToggle.RegisterValueChangedCallback(e =>
                {
                    _orbitAroundLocalOrigin = e.newValue;
                    if (e.newValue) _explicitOrbitPivot = null;
                });

            // 「現在の選択を中心に」。押した時点の重心をワールド固定点として保持する。
            // ここでもカメラは動かさない（次に回した瞬間から軸として効く）。
            if (_layoutRoot.OrbitCenterToSelectionBtn != null)
                _layoutRoot.OrbitCenterToSelectionBtn.clicked += () =>
                {
                    // 要素（頂点/辺/面/線分）が未選択ならローカル原点（ピボット）へ落とす。
                    var pivot = ComputeElementCentroid() ?? ComputeLocalOriginCentroid();
                    if (!pivot.HasValue)
                    {
                        Debug.LogWarning("[Orbit] 選択がないため回転中心を設定できません。");
                        return;
                    }

                    _explicitOrbitPivot = pivot.Value;

                    // UI 状態と実挙動を一致させる（チェック中なのに効かない状態を作らない）。
                    _orbitAroundLocalOrigin = false;
                    _layoutRoot.OrbitAroundLocalOriginToggle?.SetValueWithoutNotify(false);
                };

            // ── 法線 自動計算 / 手動再計算 ──────────────────────────────
            // 自動計算 ON  = 選択メッシュの PreserveNormals を false にする
            // 自動計算 OFF = 選択メッシュの PreserveNormals を true  にする
            // 既定は OFF（MeshObject.PreserveNormals の既定が true）。
            _layoutRoot.AutoRecalcNormalsToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingNormalRecalcToggle) return;
                var indices = CollectSelectedMeshIndices();
                if (indices.Length == 0) return;
                _commandDispatcher?.Dispatch(new SetPreserveNormalsCommand(
                    ActiveProject?.CurrentModelIndex ?? 0, indices, !e.newValue));
            });

            // 手動再計算。対象は NormalEditCommand 側で選択メッシュに限定される
            // （PlayerCommandDispatcher.CollectSelectedMeshContexts）。
            // 角度は法線編集パネルと同じ既定値を使う。角度を変えて掛けたい場合は
            // 「法線編集」パネルの「角度で再計算」を使うこと。
            // 左ペインの「すべてのオブジェクトを選択」。
            // メッシュリスト側の同名ボタンと同じ処理を呼ぶ。
            if (_layoutRoot.SelectAllObjectsBtn != null)
                _layoutRoot.SelectAllObjectsBtn.clicked += () =>
                    _meshListSubPanel?.SelectAllObjectsFromExternal();

            _layoutRoot.RecalcNormalsBtn.clicked += () =>
            {
                _commandDispatcher?.Dispatch(new NormalEditCommand(
                    ActiveProject?.CurrentModelIndex ?? 0,
                    NormalEditCommand.Op.RecalcByAngle,
                    NormalRecalcDefaultAngleDeg));
            };
        }

        /// <summary>BuildLayout の段：選択モード（頂点／辺／面／線分）と「選んだ要素の頂点も選択する」のトグル。</summary>
        private void WireSelectModeToggles()
        {
            // 選択モード切替（頂点/辺/面/線分・非排他）。
            // トグル → _userSelectMode → ApplySelectMode() の一方向だけ。
            // トグルの値をここ以外から読んではならない（読み口が増えると再び分散する）。

            // 選択モードを端末ローカルに保存（V=1/E=2/F=4/L=8 の 4bit）。PTFS 表示と同じ RecentPaths ストア。
            System.Action saveSelectMode = () =>
            {
                int bits = (_layoutRoot.SelModeVertexToggle.value ? 1 : 0)
                         | (_layoutRoot.SelModeEdgeToggle.value   ? 2 : 0)
                         | (_layoutRoot.SelModeFaceToggle.value   ? 4 : 0)
                         | (_layoutRoot.SelModeLineToggle.value   ? 8 : 0);
                PlayerUiPrefs.SetInt(SelectModePrefKey, bits);
            };

            System.Action onSelModeToggled = () =>
            {
                saveSelectMode();
                ReadUserSelectModeFromToggles();
                ApplySelectMode();
            };

            _layoutRoot.SelModeVertexToggle.RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeEdgeToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeFaceToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());
            _layoutRoot.SelModeLineToggle  .RegisterValueChangedCallback(_ => onSelModeToggled());

            // 起動時：保存済み選択モードを復元してトグルへ反映（未保存は既定=頂点のまま）。
            {
                int savedBits = PlayerUiPrefs.GetInt(SelectModePrefKey, -1);
                if (savedBits >= 0)
                {
                    _layoutRoot.SelModeVertexToggle.SetValueWithoutNotify((savedBits & 1) != 0);
                    _layoutRoot.SelModeEdgeToggle  .SetValueWithoutNotify((savedBits & 2) != 0);
                    _layoutRoot.SelModeFaceToggle  .SetValueWithoutNotify((savedBits & 4) != 0);
                    _layoutRoot.SelModeLineToggle  .SetValueWithoutNotify((savedBits & 8) != 0);
                }
                ReadUserSelectModeFromToggles();
                ApplySelectMode();
            }

            // 「選んだ要素の頂点も選択する」（辺／面／線分ごと）。
            // トグル → _expandToVertexKinds → MoveToolHandler.GetExpandToVertexKinds の
            // 一方向だけ。トグルの値をここ以外から読んではならない。
            // 保存は E=2/F=4/L=8 の 3bit。選択モードの 4bit とは別キーにする。
            if (_layoutRoot.SelExpandEdgeToVertexToggle != null)
            {
                System.Action saveExpandKinds = () =>
                {
                    int bits = (_layoutRoot.SelExpandEdgeToVertexToggle.value ? 2 : 0)
                             | (_layoutRoot.SelExpandFaceToVertexToggle.value ? 4 : 0)
                             | (_layoutRoot.SelExpandLineToVertexToggle.value ? 8 : 0);
                    PlayerUiPrefs.SetInt(ExpandToVertexPrefKey, bits);
                };

                System.Action onExpandToggled = () =>
                {
                    saveExpandKinds();
                    ReadExpandToVertexKindsFromToggles();
                    // 既に入っている頂点選択は消さない（展開由来か直接選択かを
                    // SelectionState が区別しないため）。以降の選択から効く。
                    _activePanel?.MarkDirtyRepaint();
                };

                _layoutRoot.SelExpandEdgeToVertexToggle.RegisterValueChangedCallback(_ => onExpandToggled());
                _layoutRoot.SelExpandFaceToVertexToggle.RegisterValueChangedCallback(_ => onExpandToggled());
                _layoutRoot.SelExpandLineToVertexToggle.RegisterValueChangedCallback(_ => onExpandToggled());

                // 起動時：保存済み設定を復元（未保存は既定＝3 種とも ON のまま）。
                int savedExpandBits = PlayerUiPrefs.GetInt(ExpandToVertexPrefKey, -1);
                if (savedExpandBits >= 0)
                {
                    _layoutRoot.SelExpandEdgeToVertexToggle.SetValueWithoutNotify((savedExpandBits & 2) != 0);
                    _layoutRoot.SelExpandFaceToVertexToggle.SetValueWithoutNotify((savedExpandBits & 4) != 0);
                    _layoutRoot.SelExpandLineToVertexToggle.SetValueWithoutNotify((savedExpandBits & 8) != 0);
                }
                ReadExpandToVertexKindsFromToggles();
            }

            _layoutRoot.ModelListBtn.clicked += ShowModelListPanel;
            _layoutRoot.MeshListBtn .clicked += ShowMeshListPanel;

            _layoutRoot.ModelSelectDropdown.RegisterValueChangedCallback(e =>
            {
                var project = ActiveProject;
                if (project == null) return;
                var choices = _layoutRoot.ModelSelectDropdown.choices;
                int idx = choices != null ? choices.IndexOf(e.newValue) : -1;
                if (idx < 0 || idx == project.CurrentModelIndex) return;
                SwitchActiveModel(idx);
            });

            _localLoader.OnPmxRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.PMX);
            _localLoader.OnMqoRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.MQO);

            _layoutRoot.ConnectBtn   .clicked += () => _client?.Connect();
            _layoutRoot.DisconnectBtn.clicked += () => _client?.Disconnect();
            _layoutRoot.FetchBtn     .clicked += FetchProject;
            _layoutRoot.UndoBtn      .clicked += () => _commandDispatcher?.Dispatch(new PerformUndoCommand());
            _layoutRoot.RedoBtn      .clicked += () => _commandDispatcher?.Dispatch(new PerformRedoCommand());

            _layoutRoot.PerspectivePanel.SetViewport(_viewportManager.PerspectiveViewport);
            _layoutRoot.TopPanel        .SetViewport(_viewportManager.TopViewport);
            _layoutRoot.FrontPanel      .SetViewport(_viewportManager.FrontViewport);
            _layoutRoot.SidePanel       .SetViewport(_viewportManager.SideViewport);
        }

        /// <summary>BuildLayout の段：起動直後のリフレッシュと、面ごとの表示設定トグルの接続・復元。</summary>
        private void InitDisplayToggles()
        {
            // ── 起動直後の 1 回リフレッシュ ──────────────────────────────
            // BuildLayout の時点では UIToolkit のレイアウトが未確定で、
            // 各ビューポートの RenderTexture は PlayerViewport.Initialize が作った
            // 1×1 のまま。この状態では Camera.pixelHeight が 1 になり、
            // OrthoViewController の遅延ズーム（PendingResetHalfHeight）も解決できない。
            // 4 枚すべてが実サイズを得た時点で EnterViewportsReady を 1 回だけ呼び、
            // 全ビューのカメラ確定と再描画を行う。以降はカメライベントに任せる。
            {
                var readyPanels = new[]
                {
                    _layoutRoot.PerspectivePanel,
                    _layoutRoot.TopPanel,
                    _layoutRoot.FrontPanel,
                    _layoutRoot.SidePanel,
                };
                var readyHandlers = new System.Action[readyPanels.Length];
                int readyCount = 0;
                for (int i = 0; i < readyPanels.Length; i++)
                {
                    int idx = i;
                    readyHandlers[idx] = () =>
                    {
                        // 自分の購読を外す（OnFirstRealSize は元々 1 回きりだが、
                        // 参照を残さないことでパネル破棄時の取り残しを防ぐ）。
                        readyPanels[idx].OnFirstRealSize -= readyHandlers[idx];
                        readyCount++;
                        if (readyCount < readyPanels.Length) return;
                        _viewportManager.EnterViewportsReady();
                    };
                    readyPanels[idx].OnFirstRealSize += readyHandlers[idx];
                }
            }

            // ミラー系トグルの従属関係を UI に反映する。
            //
            //   ミラー（独立。非選Mesh に従属しないので常に操作可能）
            //     ├ ミラー面
            //     ├ ミラー辺
            //     └ ミラー頂点
            //
            // 親が OFF のとき、子は値を OFF に同期しグレーアウト（無効化）する。
            // 値のクランプ自体は ViewportDisplaySettings.WithMirrorClamped が行うので、
            // ここは「クランプ済みの値を UI に映す」だけ。判定を二重に書かない。
            // 選択Mirror は UI トグルを持たない（選択Mesh に従属）。
            void ApplyMirrorToggleGating(int slot)
            {
                var d = _viewportManager.GetDisplaySettings(slot); // SetDisplaySettings でクランプ済み

                // マスタは独立。SetEnabled を呼ばない（呼ぶと従属が復活する）。
                var unselMirror = _layoutRoot.ViewportDisplayToggles[slot, PlayerLayoutRoot.VD_UNSEL_MIRROR];
                unselMirror?.SetValueWithoutNotify(d.ShowUnselectedMirror);

                void SyncChild(int item, bool value)
                {
                    var t = _layoutRoot.ViewportDisplayToggles[slot, item];
                    if (t == null) return;
                    t.SetValueWithoutNotify(value);
                    t.SetEnabled(d.ShowUnselectedMirror);
                }

                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH, d.ShowUnselectedMirrorMesh);
                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE, d.ShowUnselectedMirrorWireframe);
                SyncChild(PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT, d.ShowUnselectedMirrorVertices);
            }

            // 面ごとの表示設定トグルを接続する。
            // ViewportDisplayToggles[slot, item] → _viewportManager の設定を更新。
            for (int s = 0; s < 4; s++)
            {
                for (int i = 0; i < PlayerLayoutRoot.VD_COUNT; i++)
                {
                    int slot = s, item = i;
                    _layoutRoot.ViewportDisplayToggles[slot, item]
                        .RegisterValueChangedCallback(e =>
                        {
                            var ds = _viewportManager.GetDisplaySettings(slot);
                            switch (item)
                            {
                                case PlayerLayoutRoot.VD_CULLING:    ds.BackfaceCulling         = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MESH:   ds.ShowSelectedMesh        = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_WIRE:   ds.ShowSelectedWireframe   = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_VERT:   ds.ShowSelectedVertices    = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_BONE:   ds.ShowSelectedBone        = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MESH: ds.ShowUnselectedMesh      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_WIRE: ds.ShowUnselectedWireframe = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_VERT: ds.ShowUnselectedVertices  = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_BONE:   ds.ShowUnselectedBone      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR:
                                    ds.ShowUnselectedMirror = e.newValue;
                                    // マスタ ON で面・辺・頂点も同時に ON にする。
                                    // OFF 側は WithMirrorClamped が 3 つとも落とすので、
                                    // ここでは ON のときだけ書く（消灯処理を二重に持たない）。
                                    if (e.newValue)
                                    {
                                        ds.ShowUnselectedMirrorMesh      = true;
                                        ds.ShowUnselectedMirrorWireframe = true;
                                        ds.ShowUnselectedMirrorVertices  = true;
                                    }
                                    break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH: ds.ShowUnselectedMirrorMesh      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE: ds.ShowUnselectedMirrorWireframe = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT: ds.ShowUnselectedMirrorVertices  = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MESH_ORIGIN:   ds.ShowSelectedMeshOrigin   = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MESH_ORIGIN: ds.ShowUnselectedMeshOrigin = e.newValue; break;
                                case PlayerLayoutRoot.VD_MIRROR_MESH_ORIGIN: ds.ShowMirrorMeshOrigin    = e.newValue; break;
                                case PlayerLayoutRoot.VD_NORMAL:             ds.ShowNormals             = e.newValue; break;
                            }
                            // Phase 2a-2g-3: SetDisplaySettings → EnterDisplaySettingsChanged に集約。
                            _viewportManager.EnterDisplaySettingsChanged(slot, ds);
                            // Mesh トグルに応じて Mirror トグルの値・有効状態を更新する。
                            ApplyMirrorToggleGating(slot);
                        });
                }
            }

            // 起動時：復元済みの表示設定（RecentPaths から復元）でチェックボックスを同期する。
            // トグル初期値は itemDefaults（既定）で作られているため、これをしないと
            // 復元値と UI が食い違う（render は _displaySettings を毎フレーム反映するが UI が既定のまま）。
            for (int s = 0; s < 4; s++)
            {
                var ds = _viewportManager.GetDisplaySettings(s);
                void SyncTog(int item, bool v) => _layoutRoot.ViewportDisplayToggles[s, item]?.SetValueWithoutNotify(v);
                SyncTog(PlayerLayoutRoot.VD_CULLING,      ds.BackfaceCulling);
                SyncTog(PlayerLayoutRoot.VD_SEL_MESH,     ds.ShowSelectedMesh);
                SyncTog(PlayerLayoutRoot.VD_SEL_WIRE,     ds.ShowSelectedWireframe);
                SyncTog(PlayerLayoutRoot.VD_SEL_VERT,     ds.ShowSelectedVertices);
                SyncTog(PlayerLayoutRoot.VD_SEL_BONE,     ds.ShowSelectedBone);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MESH,   ds.ShowUnselectedMesh);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_WIRE,   ds.ShowUnselectedWireframe);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_VERT,   ds.ShowUnselectedVertices);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_BONE,   ds.ShowUnselectedBone);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR, ds.ShowUnselectedMirror);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_MESH, ds.ShowUnselectedMirrorMesh);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_WIRE, ds.ShowUnselectedMirrorWireframe);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MIRROR_VERT, ds.ShowUnselectedMirrorVertices);
                SyncTog(PlayerLayoutRoot.VD_SEL_MESH_ORIGIN,   ds.ShowSelectedMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_UNSEL_MESH_ORIGIN, ds.ShowUnselectedMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_MIRROR_MESH_ORIGIN, ds.ShowMirrorMeshOrigin);
                SyncTog(PlayerLayoutRoot.VD_NORMAL,             ds.ShowNormals);
                // Mesh トグルに応じて Mirror トグルの値・有効状態を初期同期する。
                ApplyMirrorToggleGating(s);
            }
        }

        /// <summary>BuildLayout の段：セクション表示時に中身を取り直す組（_sectionRefreshPairs）の登録。</summary>
        private void RegisterSectionRefreshPairs()
        {
            _sectionRefreshPairs.Clear();
            _sectionRefreshPairs.Add((_layoutRoot.BoneEditorSection,        () => _boneEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinWeightNumericSection, () => _skinWeightNumericSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVEditorSection,          () => _uvEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVUnwrapSection,          () => _uvUnwrapSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MaterialListSection,      () => _materialListSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.ThinPlateMorphSection,    () => _thinPlateMorphSubPanel?.OnSelectionChanged()));
            // 図形生成のマテリアル指定ドロップダウン。生成でスロットを作った直後や、
            // マテリアル一覧側でスロットを増減した後に選択肢を追随させる。
            _sectionRefreshPairs.Add((_layoutRoot.PrimitiveSection,          () => _primitiveSubPanel?.RefreshMaterials()));
            _sectionRefreshPairs.Add((_layoutRoot.LivePrimitiveSection,      () => _livePrimitiveSubPanel?.RefreshMaterials()));
            _sectionRefreshPairs.Add((_layoutRoot.UVZSection,               () => _uvzSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PartsSelectionSetSection, () => _partsSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshSelectionSetSection,  () => _meshSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.ObjectGroupSection,       () => _objectGroupSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.NormalExcludeSetSection,  () => _normalExcludeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.NormalEditSection,        () => _normalEditSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceHideSection,          () => _faceHideSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MirrorSection,            () => _mirrorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeMeshesSection,       () => _mergeMeshesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.BooleanSection,           () => _booleanSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphSection,             () => _morphSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphCreateSection,       () => _morphCreateSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.TPoseSection,             () => _tposeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.HumanoidMappingSection,   () => _humanoidMappingSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SpringBoneSection,        () => _springBoneSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SpringBoneColliderSection, () => _springBoneColliderSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.HumanLimitSection,         () => _humanLimitSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VrmSettingsSection,        () => _vrmSettingsSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshFilterToSkinnedSection, () => _mfToSkinnedSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinKindSection, () => _skinKindSubPanel?.SetModel(ActiveProject?.CurrentModel)));
            _sectionRefreshPairs.Add((_layoutRoot.QuadDecimatorSection,         () => _quadDecimatorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AlignVerticesSection,         () => _alignVerticesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PlanarizeAlongBonesSection,   () => _planarizeAlongBonesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SmoothEdgesSection,           () => _smoothEdgesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.LineExtrudeSection,           () => _lineExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PipeAlignSection,             () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _pipeAlignHandler?.Activate(ctx); _pipeAlignSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.SurfaceSnapSection,           () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _surfaceSnapHandler?.Activate(ctx); _surfaceSnapSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.PlaceObjectReshapeSection,    () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _placeObjectReshapeHandler?.Activate(ctx); _placeObjectReshapeSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.MergeVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
                _mergeVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.SplitVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _splitVerticesHandler?.Activate(ctx);
                _splitVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.VertexHoleSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _vertexHoleHandler?.Activate(ctx);
                _vertexHoleSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.VertexDissolveSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _vertexDissolveHandler?.Activate(ctx);
                _vertexDissolveSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.HoleRingCountSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _holeRingCountHandler?.Activate(ctx);
                _holeRingCountSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeBridgeSection, () =>
            {
                _edgeBridgeSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.Tri4To1Section, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _tri4To1Handler?.Activate(ctx);
                _tri4To1SubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.FaceMergeSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _faceMergeHandler?.Activate(ctx);
                _faceMergeSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.Quad4To1Section, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _quad4To1Handler?.Activate(ctx);
                _quad4To1SubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.VertexIdSection,          () => _vertexIdSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VertexTransferSection,    () => _vertexTransferSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PartsIdSection,           () => _partsIdSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AddFaceSection,           () => _addFaceSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FlipFaceSection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _flipFaceHandler?.Activate(ctx); _flipFaceSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.RotateSection,            () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _rotateHandler?.Activate(ctx); _rotateSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.ScaleSection,             () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _scaleHandler?.Activate(ctx); _scaleSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeBevelSection,         () => _edgeBevelSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeExtrudeSection,       () => _edgeExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceExtrudeSection,       () => _faceExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeTopologySection,      () => _edgeTopologySubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.KnifeSection,             () => _knifeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SolidifySection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _solidifyHandler?.Activate(ctx); _solidifySubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.MediaPipeSection,         () => _mediaPipeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VMDTestSection,           () => _vmdTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.OriginTestSection,        () => _originTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SkinTestSection,          () => _skinTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SpringBoneTestSection,    () => _springBoneTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FrillSkirtTestSection,    () => _frillSkirtTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SpringSkinScenarioSection, () => _springSkinScenarioSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.SpringSkinPipeScenarioSection, () => _springSkinPipeScenarioSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PipeHairTestSection,      () => _pipeHairTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.BarnacleTestSection,      () => _barnacleTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.RevolutionTestSection,    () => _revolutionTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.Profile2DTestSection,     () => _profile2DTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PmxToMqoTestSection,      () => _pmxToMqoTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MqoToPmxTestSection,      () => _mqoToPmxTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UnityClipTestSection,     () => _unityClipTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UnityClipToVrmaSection,   () => _unityClipToVrmaSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MotionClipTestSection,    () => _motionClipTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.RemoteServerSection,      () => _remoteServerSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.LogSection,               () => _logSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.CaptureSection,           () => _captureSubPanel?.Refresh()));
        }
    }
}
