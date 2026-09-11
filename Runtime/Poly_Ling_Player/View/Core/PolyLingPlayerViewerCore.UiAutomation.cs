// PolyLingPlayerViewerCore.UiAutomation.cs
// Player ビューアのコア：UI 自動操作（パネル・項目の登録と、コマンドの受け口）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【初期化の順】
//   BuildLayout（サブパネル生成）と _commandDispatcher の生成より後に BuildUiAutomation を呼ぶ
//   （PolyLingPlayerViewerCore.Lifecycle.cs の Initialize）。
//     1. パネルを登録し、サブパネルのインスタンスを RegisterObject へ渡す
//        （UiControlAttribute の付いたメンバーが項目として登録される）
//     2. ディスパッチャの受け口を配線
//
// 【パネルを増やすとき】
//   RegisterUiAutomationPanels に RegisterUiPanel を 1 行足す。
//   項目はサブパネルのフィールドに UiControlAttribute を付けて宣言する。
//   付け忘れ・未登録のセクションは queryUiAutomationAudit で数える。
//
// 【キャプチャ】
//   撮影は StartCapture（PolyLingPlayerViewerCore.ViewAids.cs）を通す。
//   ボタン・ショートカットと同じ経路で、キャプチャパネルの状態表示も更新される。
//   保存先は folder を省けばキャプチャパネルの設定フォルダ。指定したときは
//   作業フォルダの関門（PLSandbox.TryResolveFolder）を通し、作業フォルダの外は拒否する。
//   MCP のファイル操作は project 相対に限られるため、読み戻すなら作業フォルダへ保存させる。

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private UiAutomationRegistry _uiAutomationRegistry;
        private UiAutomationService  _uiAutomation;
        private readonly UiAutomationCaptureJobs _uiCaptureJobs = new UiAutomationCaptureJobs();

        private void BuildUiAutomation()
        {
            if (_uiRoot == null || _layoutRoot == null) return;

            _uiAutomationRegistry = new UiAutomationRegistry();
            _uiAutomation         = new UiAutomationService(_uiRoot, _uiAutomationRegistry);

            RegisterUiAutomationPanels();
            WireUiAutomationCommands();
        }

        /// <summary>パネルを登録し、項目を宣言したオブジェクトを渡す。</summary>
        private void RegisterUiPanel(
            string id, string description, VisualElement section, Action show, params object[] objects)
            => RegisterUiPanelGrouped(id, description, section, show, null, objects);

        /// <summary>
        /// 1 つのセクションをモードで切り替えるパネル用。dynamicGroup はこのパネルが見る
        /// UiDynamicControls の組（モード名）。null なら組を問わない。
        /// </summary>
        private void RegisterUiPanelGrouped(
            string id, string description, VisualElement section, Action show, string dynamicGroup,
            params object[] objects)
        {
            if (!_uiAutomationRegistry.RegisterPanel(id, description, section, show)) return;
            if (objects != null)
            {
                foreach (var o in objects)
                    if (o != null) _uiAutomationRegistry.RegisterObject(id, o, "", dynamicGroup);
            }

            // ViewerCore がセクションへ直接付けた「ビューポートで選択する」トグル
            // （AttachPanelSelectToggle）。サブパネルのフィールドではないのでここで登録する。
            if (section != null && _panelSelectToggles.TryGetValue(section, out var selectToggle))
            {
                _uiAutomationRegistry.RegisterControl(
                    id + ".selectInViewport", id, () => selectToggle,
                    "ビューポートでの選択を有効にする（オフは 3D 操作なし）",
                    UiSafety.SafeWrite, source: "PanelSelectToggle");
            }
        }

        private void RegisterUiAutomationPanels()
        {
            RegisterUiPanel("underlay", "下絵（3D 背面に敷く参照画像）の方向別設定",
                _layoutRoot.UnderlaySection, ShowUnderlayPanel, _underlaySubPanel);

            RegisterUiPanel("smoothEdges", "選択した辺・線分に沿って頂点を滑らかにする",
                _layoutRoot.SmoothEdgesSection, ShowSmoothEdgesPanel, _smoothEdgesSubPanel);

            RegisterUiPanel("capture", "画面キャプチャ（PNG 保存）",
                _layoutRoot.CaptureSection, ShowCapturePanel, _captureSubPanel);

            RegisterUiPanel("vertexMove", "頂点移動（選択・マグネット・数値移動・ギズモ）",
                _layoutRoot.VertexMoveSection, () => ShowCategory1Panel(InteractionMode.VertexMove),
                _vertexMoveSubPanel);

            // ── 編集系（SubPanels/Edit）──────────────────────────────
            RegisterUiPanel("edgeBevel", "辺ベベル（辺にカーソルを合わせてドラッグ）",
                _layoutRoot.EdgeBevelSection, ShowEdgeBevelPanel, _edgeBevelSubPanel);
            RegisterUiPanel("edgeExtrude", "辺の押し出し（選択辺をドラッグ）",
                _layoutRoot.EdgeExtrudeSection, ShowEdgeExtrudePanel, _edgeExtrudeSubPanel);
            RegisterUiPanel("edgeTopology", "辺の位相編集（対角線の入れ替え・分割・結合）",
                _layoutRoot.EdgeTopologySection, ShowEdgeTopologyPanel, _edgeTopologySubPanel);
            RegisterUiPanel("faceExtrude", "面の押し出し（選択面をドラッグ）",
                _layoutRoot.FaceExtrudeSection, ShowFaceExtrudePanel, _faceExtrudeSubPanel);
            RegisterUiPanel("flipFace", "面の裏表の反転",
                _layoutRoot.FlipFaceSection, ShowFlipFacePanel, _flipFaceSubPanel);
            RegisterUiPanel("splitVertices", "頂点分割（共有頂点を面ごとに分離）",
                _layoutRoot.SplitVerticesSection, ShowSplitVerticesPanel, _splitVerticesSubPanel);
            RegisterUiPanel("tri4To1", "三角形 4→1 統合",
                _layoutRoot.Tri4To1Section, ShowTri4To1Panel, _tri4To1SubPanel);
            RegisterUiPanel("quad4To1", "四角形 4→1 統合",
                _layoutRoot.Quad4To1Section, ShowQuad4To1Panel, _quad4To1SubPanel);
            RegisterUiPanel("vertexDissolve", "頂点溶解（頂点を消して囲む面を 1 枚にする）",
                _layoutRoot.VertexDissolveSection, ShowVertexDissolvePanel, _vertexDissolveSubPanel);
            RegisterUiPanel("vertexHole", "頂点に穴あけ",
                _layoutRoot.VertexHoleSection, ShowVertexHolePanel, _vertexHoleSubPanel);
            RegisterUiPanel("faceMerge", "面結合（辺指定）",
                _layoutRoot.FaceMergeSection, ShowFaceMergePanel, _faceMergeSubPanel);
            RegisterUiPanel("knife", "ナイフ（はしごカット・シンプル・一意分割）",
                _layoutRoot.KnifeSection, ShowKnifePanel, _knifeSubPanel);
            RegisterUiPanel("mergeMeshes", "メッシュの結合",
                _layoutRoot.MergeMeshesSection, ShowMergeMeshesPanel, _mergeMeshesSubPanel);
            RegisterUiPanel("addFace", "面の追加（クリックで点を配置）",
                _layoutRoot.AddFaceSection, ShowAddFacePanel, _addFaceSubPanel);
            RegisterUiPanel("alignVertices", "頂点の整列（軸ごとに座標を揃える）",
                _layoutRoot.AlignVerticesSection, ShowAlignVerticesPanel, _alignVerticesSubPanel);
            RegisterUiPanel("boolean", "ブーリアン演算",
                _layoutRoot.BooleanSection, ShowBooleanPanel, _booleanSubPanel);
            RegisterUiPanel("mergeVertices", "頂点の結合",
                _layoutRoot.MergeVerticesSection, ShowMergeVerticesPanel, _mergeVerticesSubPanel);
            RegisterUiPanel("vertexId", "頂点 ID の診断と修復",
                _layoutRoot.VertexIdSection, ShowVertexIdPanel, _vertexIdSubPanel);
            RegisterUiPanel("holeRingCount", "穴の頂点数合わせ（ブリッジの前処理）",
                _layoutRoot.HoleRingCountSection, ShowHoleRingCountPanel, _holeRingCountSubPanel);
            RegisterUiPanel("edgeBridge", "辺ブリッジ（2 か所の辺群の間に面を張る）",
                _layoutRoot.EdgeBridgeSection, ShowEdgeBridgePanel, _edgeBridgeSubPanel);
            RegisterUiPanel("rotate", "回転（Euler / Axis-Angle、プレビューして Apply で確定）",
                _layoutRoot.RotateSection, ShowRotatePanel, _rotateSubPanel);
            RegisterUiPanel("scale", "拡大縮小（プレビューして Apply で確定）",
                _layoutRoot.ScaleSection, ShowScalePanel, _scaleSubPanel);
            RegisterUiPanel("solidify", "厚み付け",
                _layoutRoot.SolidifySection, ShowSolidifyPanel, _solidifySubPanel);
            RegisterUiPanel("lineExtrude", "ラインの押し出し（ループを検出して押し出す）",
                _layoutRoot.LineExtrudeSection, ShowLineExtrudePanel, _lineExtrudeSubPanel);
            RegisterUiPanel("quadDecimator", "Quad 減数化",
                _layoutRoot.QuadDecimatorSection, ShowQuadDecimatorPanel, _quadDecimatorSubPanel);
            RegisterUiPanel("planarizeAlongBones", "ボーンに沿った平面化",
                _layoutRoot.PlanarizeAlongBonesSection, ShowPlanarizeAlongBonesPanel, _planarizeAlongBonesSubPanel);
            RegisterUiPanel("mirror", "ミラーの実体化と解除",
                _layoutRoot.MirrorSection, ShowMirrorPanel, _mirrorSubPanel);
            RegisterUiPanel("pipeAlign", "パイプの整列（自動ペア・手動ペア・スムージング）",
                _layoutRoot.PipeAlignSection, ShowPipeAlignPanel, _pipeAlignSubPanel);
            RegisterUiPanel("surfaceSnap", "面への吸着（計算してプレビュー、決定で確定）",
                _layoutRoot.SurfaceSnapSection, ShowSurfaceSnapPanel, _surfaceSnapSubPanel);
            RegisterUiPanel("placeObjectReshape", "配置部品を原型の形へ張り直す",
                _layoutRoot.PlaceObjectReshapeSection, ShowPlaceObjectReshapePanel, _placeObjectReshapeSubPanel);
            RegisterUiPanel("lattice", "ラティス変形（姿勢設定 → 変形開始 → 適用）",
                _layoutRoot.LatticeSection, ShowLatticePanel, _latticeSubPanel);
            RegisterUiPanel("partsId", "パーツ ID の採番・分解",
                _layoutRoot.PartsIdSection, ShowPartsIdPanel, _partsIdSubPanel);
            RegisterUiPanel("vertexTransfer", "モデル間の頂点データ転送",
                _layoutRoot.VertexTransferSection, ShowVertexTransferPanel, _vertexTransferSubPanel);
            RegisterUiPanel("sculpt", "スカルプト（盛り上げ・なめらか・膨らみ・平ら）",
                _layoutRoot.SculptSection, () => ShowCategory1Panel(InteractionMode.Sculpt), _sculptSubPanel);
            RegisterUiPanel("advancedSelect", "高度な選択（接続・ベルト・辺ループ・最短・属性・エッジ）",
                _layoutRoot.AdvancedSelectSection, () => ShowCategory1Panel(InteractionMode.AdvancedSelect),
                _advancedSelectSubPanel);
            RegisterUiPanel("workAxis", "作業軸（原点・回転・長さ・吸着・辞書）",
                _layoutRoot.WorkAxisSection, ShowWorkAxisPanel, _workAxisSubPanel);
            // 変形パネルの作業軸（_deformWorkAxisSubPanel）は PlayerDeformSubPanel.WorkAxisPanel の
            // UiNested で "deform.workAxis.*" として取り込まれる。
            RegisterUiPanel("deform", "変形（作業軸設定 → 変形開始 → 適用。回転・移動・拡大縮小・曲げ・ねじり）",
                _layoutRoot.DeformSection, ShowDeformPanel, _deformSubPanel);

            // ── モデル（SubPanels/Model）──────────────────────────────
            RegisterUiPanel("faceHide", "面の表示・非表示",
                _layoutRoot.FaceHideSection, ShowFaceHidePanel, _faceHideSubPanel);
            RegisterUiPanel("normalEdit", "法線の編集",
                _layoutRoot.NormalEditSection, ShowNormalEditPanel, _normalEditSubPanel);
            RegisterUiPanel("normalExcludeSet", "法線の除外セット",
                _layoutRoot.NormalExcludeSetSection, ShowNormalExcludeSetPanel, _normalExcludeSubPanel);
            RegisterUiPanel("meshSelectionSet", "オブジェクトの選択セット（描画・ボーン・モーフ）",
                _layoutRoot.MeshSelectionSetSection, ShowMeshSelectionSetPanel, _meshSelSetSubPanel);
            RegisterUiPanel("partsSelectionSet", "パーツ選択セット（頂点・辺・面の選択辞書）",
                _layoutRoot.PartsSelectionSetSection, ShowPartsSelectionSetPanel, _partsSelSetSubPanel);
            RegisterUiPanel("objectGroup", "オブジェクトグループ",
                _layoutRoot.ObjectGroupSection, ShowObjectGroupPanel, _objectGroupSubPanel);
            RegisterUiPanel("materialList", "マテリアル一覧（作成・シェーダー・選択面への適用）",
                _layoutRoot.MaterialListSection, ShowMaterialListPanel, _materialListSubPanel);

            // ── UV（SubPanels/UV）────────────────────────────────────
            RegisterUiPanel("uvz", "UVZ（UV と XYZ の相互変換）",
                _layoutRoot.UVZSection, ShowUVZPanel, _uvzSubPanel);
            RegisterUiPanel("uvUnwrap", "UV 展開（投影展開・LSCM 展開）",
                _layoutRoot.UVUnwrapSection, ShowUVUnwrapPanel, _uvUnwrapSubPanel);
            RegisterUiPanel("uvEditor", "UV 編集（キャンバス・UV 変換・アンカー・マグネット）",
                _layoutRoot.UVEditorSection, ShowUVEditorPanel, _uvEditorSubPanel);

            // ── モーフ・変形系 ─────────────────────────────────────────
            RegisterUiPanel("shrink", "シュリンク（頂点方式。衝突計算してプレビュー、決定で確定）",
                _layoutRoot.ShrinkSection, ShowShrinkPanel, _shrinkSubPanel);
            RegisterUiPanel("shrinkFace", "シュリンク（面方式。衝突計算してプレビュー、決定で確定）",
                _layoutRoot.ShrinkFaceSection, ShowShrinkFacePanel, _shrinkFaceSubPanel);
            RegisterUiPanel("thinPlateMorph", "薄板スプライン変形（ビフォー・アフターの対応で対象を変形）",
                _layoutRoot.ThinPlateMorphSection, ShowThinPlateMorphPanel, _thinPlateMorphSubPanel);
            RegisterUiPanel("normalTransplant", "法線の移植（計算してプレビュー、決定で確定）",
                _layoutRoot.NormalTransplantSection, ShowNormalTransplantPanel, _normalTransplantSubPanel);
            RegisterUiPanel("blend", "ブレンド（複数のソースを重みで混ぜる）",
                _layoutRoot.BlendSection, ShowBlendPanel, _blendSubPanel);
            RegisterUiPanel("modelBlend", "モデルブレンド（クローンにモデルごとの重みで混ぜる）",
                _layoutRoot.ModelBlendSection, ShowModelBlendPanel, _modelBlendSubPanel);
            RegisterUiPanel("morph", "モーフエクスプレッション（セット・エントリ・プレビュー・CSV）",
                _layoutRoot.MorphSection, ShowMorphPanel, _morphSubPanel);
            RegisterUiPanel("morphCreate", "モーフ作成とモデルへの展開",
                _layoutRoot.MorphCreateSection, ShowMorphCreatePanel, _morphCreateSubPanel);

            // ── ボーン・ウェイト（SubPanels/Bone）──────────────────────
            RegisterUiPanel("skinWeightPaint", "スキンウェイトペイント（ボーン・モード・ブラシ・Flood/Normalize/Prune）",
                _layoutRoot.SkinWeightPaintSection, () => ShowCategory1Panel(InteractionMode.SkinWeightPaint),
                _skinWeightPaintPanel);
            RegisterUiPanel("tpose", "T ポーズ変換（変換・元の姿勢への復元・ベイク・マッピング CSV）",
                _layoutRoot.TPoseSection, ShowTPosePanel, _tposeSubPanel);
            RegisterUiPanel("humanLimit", "ボーンの可動域（HumanLimit）",
                _layoutRoot.HumanLimitSection, ShowHumanLimitPanel, _humanLimitSubPanel);
            RegisterUiPanel("humanoidMapping", "Humanoid 対応付けと Avatar リターゲット設定",
                _layoutRoot.HumanoidMappingSection, ShowHumanoidMappingPanel, _humanoidMappingSubPanel);
            RegisterUiPanel("springBoneCollider", "揺れものの当たり判定",
                _layoutRoot.SpringBoneColliderSection, ShowSpringBoneColliderPanel, _springBoneColliderSubPanel);
            RegisterUiPanel("skinWeightNumeric", "スキンウェイトの数値編集（スロット・正規化・合計の検査）",
                _layoutRoot.SkinWeightNumericSection, () => ShowCategory1Panel(InteractionMode.SkinWeightNumeric),
                _skinWeightNumericSubPanel);
            RegisterUiPanel("vrmSettings", "VRM 設定（作者情報・許諾・視線・一人称）",
                _layoutRoot.VrmSettingsSection, ShowVrmSettingsPanel, _vrmSettingsSubPanel);
            RegisterUiPanel("skinKind", "スキンの種類（MeshFilter 系とスキンドの相互変換・ミラー）",
                _layoutRoot.SkinKindSection, ShowSkinKindPanel, _skinKindSubPanel);
            RegisterUiPanel("meshFilterToSkinned", "MeshFilter 構成からスキンドメッシュへの変換",
                _layoutRoot.MeshFilterToSkinnedSection, ShowMeshFilterToSkinnedPanel, _mfToSkinnedSubPanel);

            // ── 入出力 ───────────────────────────────────────────────
            RegisterUiPanel("projectSave", "プロジェクト保存（CSV / .mfproj。保存はダイアログで確定）",
                _layoutRoot.ProjectSaveSection, ShowProjectSavePanel, _projectSaveSubPanel);
            RegisterUiPanel("projectLoad", "プロジェクト読込（CSV / .mfproj。読込はダイアログで確定）",
                _layoutRoot.ProjectLoadSection, ShowProjectLoadPanel, _projectLoadSubPanel);

            // 書き出しは 1 つのセクションを 4 モードで切り替える。左ペインのボタンと同じくモードごとに分ける。
            RegisterUiPanelGrouped("exportPmx", "PMX エクスポート（保存はダイアログで確定）",
                _layoutRoot.ExportSection, () => ShowExportPanel(PlayerExportSubPanel.Mode.PMX),
                PlayerExportSubPanel.ModeGroup(PlayerExportSubPanel.Mode.PMX), _exportSubPanel);
            RegisterUiPanelGrouped("exportMqo", "MQO エクスポート（保存はダイアログで確定）",
                _layoutRoot.ExportSection, () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO),
                PlayerExportSubPanel.ModeGroup(PlayerExportSubPanel.Mode.MQO), _exportSubPanel);
            RegisterUiPanelGrouped("exportObj", "OBJ エクスポート（保存はダイアログで確定）",
                _layoutRoot.ExportSection, () => ShowExportPanel(PlayerExportSubPanel.Mode.OBJ),
                PlayerExportSubPanel.ModeGroup(PlayerExportSubPanel.Mode.OBJ), _exportSubPanel);
            RegisterUiPanelGrouped("exportVrm", "VRM エクスポート（保存はダイアログで確定）",
                _layoutRoot.ExportSection, () => ShowExportPanel(PlayerExportSubPanel.Mode.VRM),
                PlayerExportSubPanel.ModeGroup(PlayerExportSubPanel.Mode.VRM), _exportSubPanel);

            // 読込も 1 つのセクションを 4 モードで切り替える。
            RegisterUiPanelGrouped("importPmx", "PMX インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.ImportSection, () => ShowImportPanel(PlayerImportSubPanel.Mode.PMX),
                PlayerImportSubPanel.ModeGroup(PlayerImportSubPanel.Mode.PMX), _importSubPanel);
            RegisterUiPanelGrouped("importMqo", "MQO インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.ImportSection, () => ShowImportPanel(PlayerImportSubPanel.Mode.MQO),
                PlayerImportSubPanel.ModeGroup(PlayerImportSubPanel.Mode.MQO), _importSubPanel);
            RegisterUiPanelGrouped("importObj", "OBJ インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.ImportSection, () => ShowImportPanel(PlayerImportSubPanel.Mode.OBJ),
                PlayerImportSubPanel.ModeGroup(PlayerImportSubPanel.Mode.OBJ), _importSubPanel);
            RegisterUiPanelGrouped("importVrm", "VRM インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.ImportSection, () => ShowImportPanel(PlayerImportSubPanel.Mode.VRM),
                PlayerImportSubPanel.ModeGroup(PlayerImportSubPanel.Mode.VRM), _importSubPanel);

            // ── 常に見えるパネル ─────────────────────────────────────
            RegisterUiPanel("modelList", "モデル一覧（カレントの切り替え・名前の変更・削除）",
                _layoutRoot.ModelListSection, ShowModelListPanel, _modelListSubPanel);
            RegisterUiPanel("boneEditor", "ボーン編集と描画オブジェクトの姿勢（TRS・ポーズ・原点 CSV）",
                _layoutRoot.BoneEditorSection, ShowBoneEditorPanel, _boneEditorSubPanel);
            RegisterUiPanel("meshList", "オブジェクト一覧（ツリー・詳細・ポーズ・名称一括変更・モーフ編集）",
                _layoutRoot.MeshListSection, ShowMeshListPanel, _meshListSubPanel);

            // ── 表示・環境 ───────────────────────────────────────────
            RegisterUiPanel("gridAxis", "グリッドと軸の表示",
                _layoutRoot.GridAxisSection, ShowGridAxisPanel, _gridAxisSubPanel);
            RegisterUiPanel("workFolder", "作業フォルダ（読み書きできる範囲）",
                _layoutRoot.WorkFolderSection, ShowWorkFolderPanel, _workFolderSubPanel);
            RegisterUiPanel("camera", "カメラ（メイン画面・3 面図の注視点・回転・画角）",
                _layoutRoot.CameraSection, ShowCameraPanel, _cameraSubPanel);
            RegisterUiPanel("log", "ログ（表示・保存・診断ログのスイッチ）",
                _layoutRoot.LogSection, ShowLogPanel, _logSubPanel);
            RegisterUiPanel("remoteServer", "リモートサーバ（外部との通信）",
                _layoutRoot.RemoteServerSection, ShowRemoteServerPanel, _remoteServerSubPanel);
            RegisterUiPanel("springBone", "揺れもの（鎖・設定・まとまり・末端ボーン）",
                _layoutRoot.SpringBoneSection, ShowSpringBonePanel, _springBoneSubPanel);
            RegisterUiPanel("commandSchema", "コマンドの宣言の検査",
                _layoutRoot.CommandSchemaSection, ShowCommandSchemaPanel, _commandSchemaSubPanel);

            // ── 検証パネル（SubPanels/Pipeline）──────────────────────
            // 共通の項目（実行・状態・ログ・書き込み先・退避）は基底クラスに付けてある。
            RegisterUiPanel("revolutionTest", "回転体の検証",
                _layoutRoot.RevolutionTestSection, ShowRevolutionTestPanel, _revolutionTestSubPanel);
            RegisterUiPanel("frillSkirtTest", "フリルスカートの検証",
                _layoutRoot.FrillSkirtTestSection, ShowFrillSkirtTestPanel, _frillSkirtTestSubPanel);
            RegisterUiPanel("profile2DTest", "2D 断面の押し出しの検証",
                _layoutRoot.Profile2DTestSection, ShowProfile2DTestPanel, _profile2DTestSubPanel);
            RegisterUiPanel("barnacleTest", "フジツボ配置の検証",
                _layoutRoot.BarnacleTestSection, ShowBarnacleTestPanel, _barnacleTestSubPanel);
            RegisterUiPanel("pipeHairTest", "パイプ髪の検証",
                _layoutRoot.PipeHairTestSection, ShowPipeHairTestPanel, _pipeHairTestSubPanel);
            RegisterUiPanel("originTest", "原点 CSV の通し検証",
                _layoutRoot.OriginTestSection, ShowOriginTestPanel, _originTestSubPanel);
            RegisterUiPanel("skinTest", "スキンの通し検証",
                _layoutRoot.SkinTestSection, ShowSkinTestPanel, _skinTestSubPanel);
            RegisterUiPanel("springBoneTest", "揺れものの通し検証（スカート / ポニーテール）",
                _layoutRoot.SpringBoneTestSection, ShowSpringBoneTestPanel, _springBoneTestSubPanel);
            RegisterUiPanel("robotBuildTest", "ロボット組み立ての通し検証",
                _layoutRoot.RobotBuildTestSection, ShowRobotBuildTestPanel, _robotBuildTestSubPanel);
            RegisterUiPanel("pmxToMqoTest", "PMX から MQO への差し替えの検証",
                _layoutRoot.PmxToMqoTestSection, ShowPmxToMqoTestPanel, _pmxToMqoTestSubPanel);
            RegisterUiPanel("mqoToPmxTest", "MQO から PMX への差し替えの検証",
                _layoutRoot.MqoToPmxTestSection, ShowMqoToPmxTestPanel, _mqoToPmxTestSubPanel);
            RegisterUiPanel("springSkinScenario", "揺れもの＋スキンの通し検証（フリル）",
                _layoutRoot.SpringSkinScenarioSection, ShowSpringSkinScenarioPanel, _springSkinScenarioSubPanel);
            RegisterUiPanel("springSkinPipeScenario", "揺れもの＋スキンの通し検証（パイプ）",
                _layoutRoot.SpringSkinPipeScenarioSection, ShowSpringSkinPipeScenarioPanel, _springSkinPipeScenarioSubPanel);

            // ── モーション（SubPanels/VMD ほか）──────────────────────
            RegisterUiPanel("vmdTest", "VMD の読込と再生の検証",
                _layoutRoot.VMDTestSection, ShowVMDTestPanel, _vmdTestSubPanel);
            RegisterUiPanel("vmdToVrma", "VMD から VRMA への書き出し",
                _layoutRoot.VmdToVrmaSection, ShowVmdToVrmaPanel, _vmdToVrmaSubPanel);
            RegisterUiPanel("unityClipTest", "Unity クリップの読込と再生の検証（VRMA 書き出しつき）",
                _layoutRoot.UnityClipTestSection, ShowUnityClipTestPanel, _unityClipTestSubPanel);
            RegisterUiPanel("unityClipToVrma", "Unity クリップから VRMA への書き出し",
                _layoutRoot.UnityClipToVrmaSection, ShowUnityClipToVrmaPanel, _unityClipToVrmaSubPanel);
            RegisterUiPanel("motionClipTest", "モーションクリップの読込と再生の検証",
                _layoutRoot.MotionClipTestSection, ShowMotionClipTestPanel, _motionClipTestSubPanel);
            RegisterUiPanel("mediaPipe", "MediaPipe の顔ランドマークによる変形",
                _layoutRoot.MediaPipeSection, ShowMediaPipePanel, _mediaPipeSubPanel);

            // ── 図形生成 ─────────────────────────────────────────────
            // 同じサブパネルをカテゴリごとに開き直す作りなので、カテゴリごとに別パネルとして登録する。
            // 諸元は図形を選ぶたびに作り直すので、_uiDynamic が持つ（組は図形名）。
            // 図形を選ぶボタンは "shape.<図形のキー>"。
            RegisterUiPanel("primitive", "図形生成（右ペイン側）",
                _layoutRoot.PrimitiveSection,
                // このセクションを開く入口は「図形を指定して開く」しかない。
                // UI 自動操作では、今そのパネルが選んでいる図形のまま開き直す。
                () => ShowPrimitiveShape(_primitiveSubPanel.CurrentShape), _primitiveSubPanel);
            RegisterUiPanel("primitiveBasic", "図形生成：基本図形",
                _layoutRoot.LivePrimitiveSection, ShowLivePrimitivePanel, _livePrimitiveSubPanel);
            RegisterUiPanel("primitiveAdvanced", "図形生成：高度な図形",
                _layoutRoot.LivePrimitiveSection, ShowLiveAdvancedPrimitivePanel, _livePrimitiveSubPanel);
            RegisterUiPanel("primitiveMechanism", "図形生成：機構部品",
                _layoutRoot.LivePrimitiveSection, ShowLiveMechanismPrimitivePanel, _livePrimitiveSubPanel);
            RegisterUiPanel("primitiveSpringBone", "図形生成：揺れもののボーン鎖",
                _layoutRoot.LivePrimitiveSection, ShowLiveSpringBonePrimitivePanel, _livePrimitiveSubPanel);
            RegisterUiPanel("primitiveSandbox", "図形生成：サンドボックス",
                _layoutRoot.LivePrimitiveSection, ShowMcpSandboxPanel, _livePrimitiveSubPanel);

            // 部分読込・部分書き出しも 1 つのセクションを 2 モードで切り替える。
            RegisterUiPanelGrouped("partialImportPmx", "PMX 部分インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.PartialImportSection, () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX),
                PlayerPartialImportSubPanel.ModeGroup(PlayerPartialImportSubPanel.Mode.PMX), _partialImportSubPanel);
            RegisterUiPanelGrouped("partialImportMqo", "MQO 部分インポート（読込はファイル選択ダイアログで確定）",
                _layoutRoot.PartialImportSection, () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO),
                PlayerPartialImportSubPanel.ModeGroup(PlayerPartialImportSubPanel.Mode.MQO), _partialImportSubPanel);
            RegisterUiPanelGrouped("partialExportPmx", "PMX 部分エクスポート（保存はダイアログで確定）",
                _layoutRoot.PartialExportSection, () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX),
                PlayerPartialExportSubPanel.ModeGroup(PlayerPartialExportSubPanel.Mode.PMX), _partialExportSubPanel);
            RegisterUiPanelGrouped("partialExportMqo", "MQO 部分エクスポート（保存はダイアログで確定）",
                _layoutRoot.PartialExportSection, () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO),
                PlayerPartialExportSubPanel.ModeGroup(PlayerPartialExportSubPanel.Mode.MQO), _partialExportSubPanel);

            // 左ペインのボタンを撤去済み（PolyLingPlayerViewerCore.Layout.cs の ToolPivotOffsetBtn）。
            _uiAutomationRegistry.ExcludeSection(_layoutRoot.PivotSection,
                "ピボット移動は ObjectMove の「原点だけ移動」へ一本化され、左ペインのボタンを撤去済み");
            _uiAutomationRegistry.ExcludeSection(_layoutRoot.ObjectMoveTRSSection,
                "オブジェクト移動 TRS は BoneEditorSection を共用する形へ変え、このセクションには何も作っていない");

            RunStartupUiAutomationAudit();

            // コマンド一覧パネルへ、検査の結果と取り直しの手段を渡す。
            if (_commandSchemaSubPanel != null)
            {
                _commandSchemaSubPanel.RunUiAutomationAudit = RunUiAutomationAuditNow;
                _commandSchemaSubPanel.ShowUiAuditResult(_startupAudit);
            }
        }

        /// <summary>
        /// 登録が終わった直後に一度だけ検査する。
        ///
        /// 【なぜ要るか】
        ///   属性の付け忘れや未登録の部品はコンパイルを通ってしまう。
        ///   queryUiAutomationAudit を人が呼ぶまで気づけないと、部品を足したときに抜ける。
        ///   ここで走らせておけば、再生するたびに目に入る。
        ///
        /// 問題が無いときは何も出さない（毎回出ると読み飛ばすようになるため）。
        /// </summary>
        private void RunStartupUiAutomationAudit()
        {
            if (_uiAutomationRegistry == null) return;

            _startupAudit = UiAutomationAudit.Run(
                _uiAutomationRegistry, _layoutRoot?.RightPaneContent, _layoutRoot);

            if (!_startupAudit.HasProblems) return;

            Debug.LogWarning(
                "[UiAutomation] UI 自動操作の登録に直すべきものがあります: "
              + _startupAudit.ProblemSummary()
              + "\n（コマンド一覧パネルにも出ています。詳細は queryUiAutomationAudit）\n"
              + _startupAudit.Report);
        }

        /// <summary>
        /// 再生開始時の検査の結果。コマンド一覧パネルが表示に使う。
        /// 開始後に登録が変わることは無いので、取り直しはパネルのボタンから行う。
        /// </summary>
        private UiAutomationAudit.Result _startupAudit;

        /// <summary>
        /// 今の登録状況を検査し直す。コマンド一覧パネルの「UI 登録を検査」から呼ぶ。
        /// </summary>
        private UiAutomationAudit.Result RunUiAutomationAuditNow()
        {
            if (_uiAutomationRegistry == null) return null;
            _startupAudit = UiAutomationAudit.Run(
                _uiAutomationRegistry, _layoutRoot?.RightPaneContent, _layoutRoot);
            return _startupAudit;
        }

        private void WireUiAutomationCommands()
        {
            if (_commandDispatcher == null) return;

            _commandDispatcher.OnUiDescribe      = ExecuteUiDescribe;
            _commandDispatcher.OnUiShowPanel     = ExecuteUiShowPanel;
            _commandDispatcher.OnUiReveal        = ExecuteUiReveal;
            _commandDispatcher.OnUiGetValue      = ExecuteUiGetValue;
            _commandDispatcher.OnUiSetValue      = ExecuteUiSetValue;
            _commandDispatcher.OnUiHighlight     = ExecuteUiHighlight;
            _commandDispatcher.OnUiCapture       = ExecuteUiCapture;
            _commandDispatcher.OnUiCaptureStatus = ExecuteUiCaptureStatus;
            _commandDispatcher.OnUiClick         = ExecuteUiClick;
            _commandDispatcher.OnQueryUiAutomationAudit = ExecuteQueryUiAutomationAudit;
        }

        // ================================================================
        // 受け口
        // ================================================================

        private const string UiAutomationNotReady = "UI 自動操作が初期化されていません";

        private CommandResult ExecuteUiClick(UiClickCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.Click(cmd.ControlId, cmd.AllowDestructive);
        }

        private CommandResult ExecuteQueryUiAutomationAudit(QueryUiAutomationAuditCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomationRegistry == null) return CommandResult.Fail(UiAutomationNotReady);

            var r = UiAutomationAudit.Run(_uiAutomationRegistry, _layoutRoot?.RightPaneContent, _layoutRoot);
            return CommandResult.Ok(data: CommandDataJson.New()
                .Text("report",               r.Report)
                .Int ("sections",             r.Sections)
                .Int ("panels",               r.Panels)
                .Int ("controls",             r.Controls)
                .Int ("unregisteredSections", r.UnregisteredSections)
                .Int ("missingAttributes",    r.MissingAttributes)
                .Int ("registrationErrors",   r.RegistrationErrors)
                .Int ("unspecifiedSafety",    r.UnspecifiedSafety)
                .Int ("unsupportedTypes",     r.UnsupportedTypes)
                .Int ("unregisteredElements", r.UnregisteredElements)
                .Build());
        }

        private CommandResult ExecuteUiDescribe(UiDescribeCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.Describe(cmd.PanelId);
        }

        private CommandResult ExecuteUiShowPanel(UiShowPanelCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.ShowPanel(cmd.PanelId);
        }

        private CommandResult ExecuteUiReveal(UiRevealCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.Reveal(cmd.ControlId, cmd.Highlight);
        }

        private CommandResult ExecuteUiGetValue(UiGetValueCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.GetValue(cmd.ControlId);
        }

        private CommandResult ExecuteUiSetValue(UiSetValueCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.SetValue(cmd.ControlId, cmd.Value, cmd.AllowDestructive);
        }

        private CommandResult ExecuteUiHighlight(UiHighlightCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null) return CommandResult.Fail(UiAutomationNotReady);
            return _uiAutomation.Highlight(cmd.ControlId, cmd.Enabled);
        }

        private CommandResult ExecuteUiCapture(UiCaptureCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (!TryParseCaptureTarget(cmd.Target, out CaptureTarget target))
                return CommandResult.Fail(
                    $"unknown capture target: {cmd.Target}（MainView / TriView / Window）");

            // 保存先を指定したときは作業フォルダの関門を通す。
            // TryResolveWrite は拡張子を検査するのでフォルダには使えない。
            // ファイル名は PlayerScreenCapture が区切り文字を除いて .png を付けるため、
            // フォルダの外へは出ない。
            string folder = null;
            if (!string.IsNullOrEmpty(cmd.Folder))
            {
                if (!PLSandbox.TryResolveFolder(cmd.Folder, out folder, out string folderReason))
                    return CommandResult.Fail(folderReason);
            }

            var job = _uiCaptureJobs.Start();
            string id = job.Id;
            Debug.Log($"[UiAutomation] Capture start: id={id} target={target} baseName={cmd.BaseName}"
                + $" folder={folder ?? "(キャプチャパネルの設定)"}");

            StartCapture(target, folder, cmd.BaseName, (ok, msg) =>
            {
                _uiCaptureJobs.Complete(id, ok, msg);
                Debug.Log(ok
                    ? $"[UiAutomation] Capture completed: id={id} path={msg}"
                    : $"[UiAutomation] Capture failed: id={id} reason={msg}");
            });

            // 切り出し範囲が取れないときは StartCapture の中で同期的に失敗が返る。
            return CommandResult.Ok(data: CommandDataJson.New()
                .Text("captureId", id)
                .Text("status",    UiAutomationCaptureJobs.StatusText(job.Status))
                .Build());
        }

        private CommandResult ExecuteUiCaptureStatus(UiCaptureStatusCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (!_uiCaptureJobs.TryGet(cmd.CaptureId, out var job))
                return CommandResult.Fail($"capture job not found: {cmd.CaptureId}");

            var b = CommandDataJson.New()
                .Text("captureId", job.Id)
                .Text("status",    UiAutomationCaptureJobs.StatusText(job.Status));
            if (job.Status == UiAutomationCaptureJobs.JobStatus.Completed) b.Text("path",  job.Path);
            if (job.Status == UiAutomationCaptureJobs.JobStatus.Failed)    b.Text("error", job.Error);
            return CommandResult.Ok(data: b.Build());
        }

        /// <summary>
        /// 撮影範囲の名前を CaptureTarget へ直す。大文字小文字は区別しない。
        /// 数値の文字列は受けない（Enum.TryParse は "2" も通すため、名前で照合する）。
        /// </summary>
        private static bool TryParseCaptureTarget(string s, out CaptureTarget target)
        {
            foreach (CaptureTarget v in Enum.GetValues(typeof(CaptureTarget)))
            {
                if (string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase))
                {
                    target = v;
                    return true;
                }
            }
            target = CaptureTarget.Window;
            return false;
        }
    }
}
