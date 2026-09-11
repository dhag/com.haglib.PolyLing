// PlayerCommandDispatcher.cs
// PanelCommand を受け取り ProjectContext に適用するクラス。
// PolyLingPlayerViewer の DispatchPanelCommand を分離したもの。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【partial の分担】
//   このファイル            依存・初期化・受け口・結果の報告・Dispatch / DispatchCore・診断・Undo 記録
//   DispatchCore は前処理のあと、分類ごとの Dispatch<分類>() を元の switch の順に呼ぶ。
//   各メソッドは該当するコマンドを処理したら true を返す。コマンドを足すときは
//   同じ分類のファイルの switch へ case を足す（新しい分類を作るなら DispatchCore の呼び出し列へも足す）。
//     Query.cs / Query.Build.cs   照会・生データ（DispatchQuery）と結果の組み立て
//     MeshAttributes.cs           モデル操作（DispatchModel）・属性（DispatchMeshAttributes）・可視／ロックの適用
//     EditTools.cs                選択・移動・位相編集・ツール確定・ギズモ・デフォーマ・作業軸
//     BoneMorphUv.cs              BonePose・モーフ・BoneTransform・UV・マテリアル
//     Blend.cs                    モデル／メッシュブレンド
//     ObjectGroup.cs              オブジェクトグループ
//     DeformSkin.cs               特殊な変形・スキン関連
//     MirrorHumanoidVrm.cs        Quad減面・ミラー・Humanoid・VRM 設定・Avatar リターゲット
//     SpringBone.cs               揺れもの（VRM SpringBone）
//     TPoseMergeMorph.cs          Tポーズ・メッシュマージ・ブーリアン・差分モーフ
//     SelectionSets.cs            パーツ選択辞書・除外辞書・面の表示／非表示・メッシュ選択辞書
//     NormalEdit.cs               法線編集
//     ObjectOrigin.cs             オブジェクト原点の一括設定・姿勢くさび

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly Func<ProjectContext>   _getProject;
        private readonly MeshSceneRenderer      _renderer;
        private readonly PlayerViewportManager  _viewportManager;
        private readonly PlayerSelectionOps     _selectionOps;
        private readonly Action<ChangeKind>     _notifyPanels;
        private readonly Action                 _rebuildModelList;
        private readonly MeshUndoController     _undoController;
        private readonly CommandQueue           _commandQueue;

        // MeshListOps はモーフプレビューとスライダードラッグの途中経過を
        // インスタンス内に持つ（MeshListOps.cs:29-35）。Start→Apply→End が
        // 同一インスタンスで揃わないと成立しないため、ここで 1 個だけ保持する。
        private MeshListOps  _meshListOps;
        private ModelContext _meshListOpsModel;

        /// <summary>
        /// AssignPartsIdsCommand の直近の実行結果。パネルが結果表示へ使う。
        /// 失敗（割り切れない等）のときも理由を持たせて残す。
        /// </summary>
        public PartsIdAssignResult LastPartsIdResult { get; private set; }

        /// <summary>
        /// AssignPartsIdsByBoneWeightCommand の直近の実行結果。パネルが結果表示へ使う。
        /// LastPartsIdResult とは持ち物が違う（群の数・予約値の頂点数・番号の開始値）ので
        /// 別に持つ。失敗のときも理由を持たせて残す。
        /// </summary>
        public PartsIdByBoneWeightResult LastPartsIdByBoneWeightResult { get; private set; }

        // BoneTransformスライダーのUndo用スナップショット（Begin～End間で保持）
        private readonly Dictionary<int, BoneTransformSnapshot> _boneTransformBeforeSnapshots
            = new Dictionary<int, BoneTransformSnapshot>();

        // ボーンTRS編集A/B: Begin で確定モードと開始状態を保持
        private BoneMoveMode _activeBoneEditMode = BoneMoveMode.BoneOnlyRebind;
        private readonly Dictionary<int, Matrix4x4> _boneRebindStartSkinning = new Dictionary<int, Matrix4x4>();
        private readonly Dictionary<int, Matrix4x4> _boneRebindStartBindPose = new Dictionary<int, Matrix4x4>();
        private TPoseBackup _boneFreezeBefore;

        // 原点だけ移動(OriginOnly)用: Begin～End 間の対象 MeshFilter の開始状態。
        // ObjectMoveTool の _originStartPositions / _originStartWorld と同じ役割。
        private bool _boneOriginOnly;
        private readonly Dictionary<int, Vector3[]> _boneOriginStartPositions
            = new Dictionary<int, Vector3[]>();
        private readonly Dictionary<int, Matrix4x4> _boneOriginStartWorld
            = new Dictionary<int, Matrix4x4>();

        // C(ポーズ一時)用: Begin～End 間の BonePoseData スナップショット
        private readonly Dictionary<int, BonePoseDataSnapshot> _bonePoseBeforeSnapshots
            = new Dictionary<int, BonePoseDataSnapshot>();
        private const string PoseManualLayer = "Manual";

        // モードC: TRS の 1 フィールドを BonePoseData の "Manual" 層へ差分として書く
        private void ApplyPoseLayerField(MeshContext ctx, SetBoneTransformValueCommand.Field field, float value)
        {
            // 呼び出し側（SetBoneTransformValueCommand の対象ループ）が
            // 既に null を弾いているので、ここは保険。1 件飛ばすだけで
            // コマンド全体の失敗にはしないため Fail は呼ばない。
            if (ctx == null) return;
            if (ctx.BonePoseData == null) ctx.BonePoseData = new BonePoseData();
            ctx.BonePoseData.IsActive = true;
            var layer = ctx.BonePoseData.GetOrCreateLayer(PoseManualLayer);
            switch (field)
            {
                case SetBoneTransformValueCommand.Field.RotationX:
                case SetBoneTransformValueCommand.Field.RotationY:
                case SetBoneTransformValueCommand.Field.RotationZ:
                {
                    Vector3 e = NormEuler180(layer.DeltaRotation.eulerAngles);
                    if      (field == SetBoneTransformValueCommand.Field.RotationX) e.x = value;
                    else if (field == SetBoneTransformValueCommand.Field.RotationY) e.y = value;
                    else                                                            e.z = value;
                    layer.DeltaRotation = Quaternion.Euler(e);
                    layer.Enabled = true;
                    break;
                }
                case SetBoneTransformValueCommand.Field.PositionX:
                case SetBoneTransformValueCommand.Field.PositionY:
                case SetBoneTransformValueCommand.Field.PositionZ:
                {
                    Vector3 p = layer.DeltaPosition;
                    if      (field == SetBoneTransformValueCommand.Field.PositionX) p.x = value;
                    else if (field == SetBoneTransformValueCommand.Field.PositionY) p.y = value;
                    else                                                            p.z = value;
                    layer.DeltaPosition = p;
                    layer.Enabled = true;
                    break;
                }
                default:
                {
                    // スケールはポーズ層対象外 → BoneTransform に書く（従来）
                    if (ctx.BoneTransform != null)
                    {
                        ctx.BoneTransform.UseLocalTransform = true;
                        var sc = ctx.BoneTransform.Scale;
                        if      (field == SetBoneTransformValueCommand.Field.ScaleX) sc.x = value;
                        else if (field == SetBoneTransformValueCommand.Field.ScaleY) sc.y = value;
                        else if (field == SetBoneTransformValueCommand.Field.ScaleZ) sc.z = value;
                        ctx.BoneTransform.Scale = sc;
                    }
                    break;
                }
            }
            ctx.BonePoseData.SetDirty();
        }

        private static Vector3 NormEuler180(Vector3 e)
            => new Vector3(NormAngle180(e.x), NormAngle180(e.y), NormAngle180(e.z));

        private static float NormAngle180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerCommandDispatcher(
            Func<ProjectContext>  getProject,
            MeshSceneRenderer     renderer,
            PlayerViewportManager viewportManager,
            PlayerSelectionOps    selectionOps,
            Action<ChangeKind>    notifyPanels,
            Action                rebuildModelList,
            MeshUndoController    undoController = null,
            CommandQueue          commandQueue   = null)
        {
            _getProject       = getProject       ?? throw new ArgumentNullException(nameof(getProject));
            _renderer         = renderer         ?? throw new ArgumentNullException(nameof(renderer));
            _viewportManager  = viewportManager  ?? throw new ArgumentNullException(nameof(viewportManager));
            _selectionOps     = selectionOps;
            _notifyPanels     = notifyPanels     ?? throw new ArgumentNullException(nameof(notifyPanels));
            _rebuildModelList = rebuildModelList ?? throw new ArgumentNullException(nameof(rebuildModelList));
            _undoController   = undoController;
            _commandQueue     = commandQueue;
        }

        /// <summary>
        /// 保持している MeshListOps を返す。対象モデルが変わったときだけ差し替える。
        /// SetContext は内部で EndMorphPreview() を呼ぶ（MeshListOps.cs:45）ので、
        /// 毎回呼ぶとプレビューの途中経過が消える。
        /// </summary>
        private MeshListOps GetMeshListOps(ModelContext model)
        {
            if (_meshListOps == null)
            {
                _meshListOps = new MeshListOps(model, _undoController);

                // 位置のみの GPU 反映。SyncMeshPositionsAndTransform は
                // PlayerViewportManager.cs:2630 で Obsolete 指定されているため、
                // 正規入口の EnterVerticesMoved(Dragging) 経由で渡す。
                _meshListOps.SyncPositionsOnly = mc =>
                    _viewportManager.EnterVerticesMoved(
                        _getProject(), VerticesMovedPhase.Dragging, mc);

                _meshListOpsModel = model;
                return _meshListOps;
            }

            if (!ReferenceEquals(_meshListOpsModel, model))
            {
                _meshListOps.SetContext(model, _undoController);
                _meshListOpsModel = model;
            }
            return _meshListOps;
        }

        // ================================================================
        // 生成系の受け口（Viewer から設定）
        //
        // 【なぜ委譲するか】
        //   図形生成・ブリッジ・面削除の実処理は、追加先の解決・Undo 記録・
        //   再構築・オーバーレイ更新まで Viewer 側の状態に触れる。
        //   ここへ移すとディスパッチャが Viewer の内部を抱えることになるので、
        //   コマンドの受け付けだけをここで行い、実行は Viewer のメソッドへ渡す。
        //   経路はコマンド 1 本になるので、パネルも自動検証も MCP も同じ道を通る。
        // ================================================================

        /// <summary>図形生成コマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<CreatePrimitiveMeshCommand, string> OnCreatePrimitiveMesh;

        /// <summary>出来上がったメッシュをそのまま置くコマンドの実行。戻り値は失敗理由。</summary>
        public Func<AddGeneratedMeshCommand, string> OnAddGeneratedMesh;

        /// <summary>穴つなぎコマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<CreateHoleBridgeCommand, string> OnCreateHoleBridge;

        /// <summary>辺群ブリッジコマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<CreateEdgeBridgeCommand, string> OnCreateEdgeBridge;

        /// <summary>
        /// 詳細選択コマンドの実行。
        /// 選択アルゴリズムは AdvancedSelectTool が正典なので、ここでは持たずに委譲する。
        /// 戻り値は失敗理由。成功時は null（P1-3: 無言 return を残さない）。
        /// </summary>
        public Func<AdvancedSelectCommand, string> OnAdvancedSelect;

        /// <summary>
        /// 属性選択コマンドの実行。
        /// 走査は AdvancedSelectTool.ExecuteAttributeSelect が正典なので委譲する。
        /// 戻り値は失敗理由。成功時は null。
        /// </summary>
        public Func<AdvancedSelectByAttributeCommand, string> OnAdvancedSelectByAttribute;

        /// <summary>
        /// スカルプトストロークコマンドの実行。
        /// 変形アルゴリズムは SculptTool が正典なので、ここでは持たずに委譲する。
        /// 戻り値は失敗理由。成功時は null。
        /// </summary>
        public Func<SculptStrokeCommand, string> OnSculptStroke;

        /// <summary>
        /// 原点移動コマンドの実行。
        /// 変形・子の補償・Undo は ObjectMoveTool(OriginOnly) が正典なので委譲する。
        /// 戻り値は失敗理由。成功時は null（P1-3: 無言 return を残さない）。
        /// </summary>
        public Func<MovePivotCommand, string> OnMovePivot;

        /// <summary>
        /// 選択頂点の移動コマンドの実行。
        /// 選択要素の頂点展開・マグネット・Undo・リモート配信は MoveToolHandler が
        /// 正典なので委譲する。戻り値は失敗理由。成功時は null。
        /// </summary>
        public Func<MoveSelectedVerticesCommand, string> OnMoveSelectedVertices;

        /// <summary>
        /// 要素選択コマンドの実行。
        /// 書き込み先の解決・頂点への展開・選択 Undo は
        /// PlayerSelectionOps / MoveToolHandler が正典なので委譲する。
        /// 戻り値は失敗理由。成功時は null。
        /// </summary>
        public Func<SelectElementsCommand, string> OnSelectElements;

        /// <summary>面削除コマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<DeleteFacesCommand, string> OnDeleteFaces;

        // ================================================================
        // 位相編集（パラメータを持たない実行系）
        //
        // 実処理は各 Tool が正典。ここに第 2 実装を置かず、ハンドラへ委譲する。
        // 戻り値は失敗理由。成功時は null（P1-3: 無言 return を残さない）。
        // ================================================================

        /// <summary>面結合（辺指定）コマンドの実行。</summary>
        public Func<FaceMergeCommand, string> OnFaceMerge;

        /// <summary>四角形 4→1 コマンドの実行。</summary>
        public Func<Quad4To1Command, string> OnQuad4To1;

        /// <summary>三角形 4→1 コマンドの実行。</summary>
        public Func<Tri4To1Command, string> OnTri4To1;

        /// <summary>頂点溶かしコマンドの実行。</summary>
        public Func<VertexDissolveCommand, string> OnVertexDissolve;

        /// <summary>頂点分離コマンドの実行。</summary>
        public Func<SplitVerticesCommand, string> OnSplitVertices;

        // ================================================================
        // 位相・頂点編集（パラメータを持つ実行系）
        //
        // 実処理は各 Tool が正典。ここに第 2 実装を置かず、ハンドラへ委譲する。
        // 戻り値は失敗理由。成功時は null（P1-3: 無言 return を残さない）。
        // ================================================================

        /// <summary>頂点に穴あけコマンドの実行。</summary>
        public Func<VertexHoleCommand, string> OnVertexHole;

        /// <summary>面反転コマンドの実行。</summary>
        public Func<FlipFaceCommand, string> OnFlipFace;

        /// <summary>頂点整列コマンドの実行。</summary>
        public Func<AlignVerticesCommand, string> OnAlignVertices;

        /// <summary>辺の平滑化コマンドの実行。</summary>
        public Func<SmoothEdgesCommand, string> OnSmoothEdges;

        /// <summary>選択辺から帯面を足すコマンドの実行。</summary>
        public Func<EdgeRibbonFaceCommand, string> OnEdgeRibbonFace;

        /// <summary>PMX ファイル読み込みコマンドの実行。</summary>
        public Func<ImportPmxFileCommand, string> OnImportPmxFile;

        /// <summary>PMX ファイル書き出しコマンドの実行。</summary>
        public Func<ExportPmxFileCommand, string> OnExportPmxFile;

        /// <summary>MQO ファイル読み込みコマンドの実行。</summary>
        public Func<ImportMqoFileCommand, string> OnImportMqoFile;

        /// <summary>MQO ファイル書き出しコマンドの実行。</summary>
        public Func<ExportMqoFileCommand, string> OnExportMqoFile;

        /// <summary>OBJ ファイル読み込みコマンドの実行。</summary>
        public Func<ImportObjFileCommand, string> OnImportObjFile;

        /// <summary>OBJ ファイル書き出しコマンドの実行。</summary>
        public Func<ExportObjFileCommand, string> OnExportObjFile;

        /// <summary>VRM 1.0 ファイル書き出しコマンドの実行。</summary>
        public Func<ExportVrmFileCommand, string> OnExportVrmFile;

        /// <summary>VRM（1.0 / 0.x）ファイル読み込みコマンドの実行。</summary>
        public Func<ImportVrmFileCommand, string> OnImportVrmFile;

        /// <summary>プロジェクト（.mfproj）保存コマンドの実行。</summary>
        public Func<SaveProjectFileCommand, string> OnSaveProjectFile;

        /// <summary>プロジェクト（.mfproj）読み込みコマンドの実行。</summary>
        public Func<LoadProjectFileCommand, string> OnLoadProjectFile;

        /// <summary>プロジェクト CSV 保存コマンドの実行。</summary>
        public Func<SaveProjectCsvCommand, string> OnSaveProjectCsv;

        /// <summary>プロジェクト CSV 読み込みコマンドの実行。</summary>
        public Func<LoadProjectCsvCommand, string> OnLoadProjectCsv;

        /// <summary>ボーン平面への平面化コマンドの実行。</summary>
        public Func<PlanarizeAlongBonesCommand, string> OnPlanarizeAlongBones;

        /// <summary>頂点結合コマンドの実行。</summary>
        public Func<MergeVerticesCommand, string> OnMergeVertices;

        // ================================================================
        // 位相・頂点編集（対象や生成先の指定を伴う実行系）
        // ================================================================

        /// <summary>選択要素の削除コマンドの実行。</summary>
        public Func<DeleteSelectionCommand, string> OnDeleteSelection;

        /// <summary>パイプ整列コマンドの実行。</summary>
        public Func<PipeAlignCommand, string> OnPipeAlign;

        /// <summary>配置物の整形コマンドの実行。</summary>
        public Func<PlaceObjectReshapeCommand, string> OnPlaceObjectReshape;

        /// <summary>厚み付けコマンドの実行。</summary>
        public Func<SolidifyCommand, string> OnSolidify;

        /// <summary>線分押し出しコマンドの実行。</summary>
        public Func<LineExtrudeCommand, string> OnLineExtrude;

        /// <summary>面に張り付けコマンドの実行。</summary>
        public Func<SurfaceSnapCommand, string> OnSurfaceSnap;

        /// <summary>VRM アニメーション（.vrma）書き出しコマンドの実行。</summary>
        public Func<ExportVrmAnimationCommand, string> OnExportVrmAnimation;

        /// <summary>Unity クリップ → VRMA 変換コマンドの実行（モデル非依存）。</summary>
        public Func<ConvertUnityClipToVrmaCommand, string> OnConvertUnityClipToVrma;

        /// <summary>VMD → VRMA 書き出しコマンドの実行。</summary>
        public Func<ExportVmdToVrmaCommand, string> OnExportVmdToVrma;

        // ================================================================
        // ドラッグ確定（ベベル・押し出し）
        // ================================================================

        /// <summary>辺ベベルコマンドの実行。</summary>
        public Func<EdgeBevelCommand, string> OnEdgeBevel;

        /// <summary>辺・線分の押し出しコマンドの実行。</summary>
        public Func<EdgeExtrudeCommand, string> OnEdgeExtrude;

        /// <summary>面の押し出しコマンドの実行。</summary>
        public Func<FaceExtrudeCommand, string> OnFaceExtrude;

        /// <summary>スキンウェイト塗りコマンドの実行。</summary>
        public Func<SkinWeightPaintCommand, string> OnSkinWeightPaint;

        // ================================================================
        // 変形ギズモ（選択頂点の回転・スケール）
        // ================================================================

        /// <summary>選択頂点の回転コマンドの実行。</summary>
        public Func<RotateSelectionCommand, string> OnRotateSelection;

        /// <summary>選択頂点のスケールコマンドの実行。</summary>
        public Func<ScaleSelectionCommand, string> OnScaleSelection;

        // ================================================================
        // オブジェクトごと移動・回転（ObjectMove ギズモ）
        // ================================================================

        /// <summary>選択オブジェクトの移動コマンドの実行。</summary>
        public Func<MoveObjectsCommand, string> OnMoveObjects;

        /// <summary>選択オブジェクトの回転コマンドの実行。</summary>
        public Func<RotateObjectsCommand, string> OnRotateObjects;

        // ================================================================
        // デフォーマ
        // ================================================================

        /// <summary>変形コマンドの実行。派生 6 種をまとめて受ける。</summary>
        public Func<ApplyDeformCommand, string> OnApplyDeform;

        /// <summary>格子変形コマンドの実行。</summary>
        public Func<ApplyLatticeDeformCommand, string> OnApplyLatticeDeform;

        // ================================================================
        // クリック確定（辺トポロジ・面追加）
        // ================================================================

        /// <summary>辺の入れ替えコマンドの実行。</summary>
        public Func<EdgeTopologyFlipCommand, string> OnEdgeTopologyFlip;

        /// <summary>辺の消去コマンドの実行。</summary>
        public Func<EdgeTopologyDissolveCommand, string> OnEdgeTopologyDissolve;

        /// <summary>四角形の対角分割コマンドの実行。</summary>
        public Func<EdgeTopologySplitCommand, string> OnEdgeTopologySplit;

        /// <summary>面追加コマンドの実行。</summary>
        public Func<AddFaceCommand, string> OnAddFace;

        // ================================================================
        // ナイフ
        // ================================================================

        /// <summary>ラダー切断コマンドの実行。</summary>
        public Func<KnifeLadderCutCommand, string> OnKnifeLadderCut;

        /// <summary>一意分割コマンドの実行。</summary>
        public Func<KnifeBeltLoopCutCommand, string> OnKnifeBeltLoopCut;

        /// <summary>辺消去コマンドの実行。</summary>
        public Func<KnifeEraseEdgeCommand, string> OnKnifeEraseEdge;

        /// <summary>シンプル切断コマンドの実行。</summary>
        public Func<KnifeSimpleCutCommand, string> OnKnifeSimpleCut;

        // ================================================================
        // 作業軸
        //
        // モデルの頂点・選択は書き換えないので Undo も所有権判定も持たない。
        // ================================================================

        /// <summary>作業軸の状態差し替えコマンドの実行。</summary>
        public Func<SetWorkAxisCommand, string> OnSetWorkAxis;

        /// <summary>作業軸ライブラリ呼び出しコマンドの実行。</summary>
        public Func<RecallWorkAxisCommand, string> OnRecallWorkAxis;

        /// <summary>穴点数合わせコマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<MatchHoleRingCountCommand, string> OnMatchHoleRingCount;

        /// <summary>プロジェクト初期化コマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<ResetProjectCommand, string> OnResetProject;

        /// <summary>歪み複製コマンドの実行。戻り値は失敗理由。成功時は null。</summary>
        public Func<CreateObjectArrayCommand, string> OnCreateObjectArray;

        /// <summary>
        /// パーツIDによる分解コマンドの実行。戻り値は失敗理由。成功時は null。
        /// 描画オブジェクトの追加は Undo 記録とビュー再構築を伴うので、
        /// 実行は Viewer 側が持つ（PolyLingPlayerViewerCore.CreateCommands.cs:4-8）。
        /// </summary>
        public Func<SplitObjectByPartsIdCommand, string> OnSplitObjectByPartsId;

        /// <summary>Undo の実行。1 段戻せたら true を返すこと。</summary>
        public Func<bool> OnUndo;

        /// <summary>Redo の実行。1 段やり直せたら true を返すこと。</summary>
        public Func<bool> OnRedo;

        // ================================================================
        // ディスパッチ
        // ================================================================

        /// <summary>
        /// 直近のコマンドの結果。DispatchCore 内のハンドラが Fail() / ReportTargets() で設定する。
        /// 未設定のまま DispatchCore を抜けたら識別子なしの成功とみなす。
        /// </summary>
        private CommandResult _pendingResult;

        /// <summary>
        /// ハンドラから失敗を報告する。設定後は通常どおり return してよい。
        ///
        /// 【どこから呼んでよいか】
        ///   DispatchCore の switch と、そこから直接呼ばれるヘルパーまで。
        ///   Undo 記録・ミラー同期などの後処理ヘルパーは、呼び出し側が既に検証を
        ///   済ませているか、失敗しても利用者へ返す意味が無いので呼ばない
        ///   （無言の return; が残っているのはそのため）。
        ///   static メソッドからは呼べない（インスタンスの _pendingResult へ書くため）。
        ///
        /// 【二重に呼んだとき】
        ///   後から呼んだ理由で上書きされる。呼んだ直後に return する規約なので
        ///   実際には重ならない。
        /// </summary>
        private void Fail(string reason) => _pendingResult = CommandResult.Fail(reason);

        /// <summary>
        /// 生成・変更した対象を成功結果として報告する。
        ///
        /// 【なぜ要るか】
        ///   受け口（PolyLingPlayerViewerCore の Execute*）は戻り値が string で、
        ///   失敗理由しか返せない。生成物の位置と安定 ID を MCP・リモートへ返すには
        ///   Fail と対になる口が要る。戻り値の型を変えると受け口 60 本以上に波及する。
        ///
        /// 【どこから呼んでよいか】
        ///   Fail と同じ。DispatchCore の switch と、そこから直接呼ばれる受け口まで。
        ///   受け口は別クラスにあるので internal にしてある。
        ///
        /// 【呼ばなかったとき】
        ///   Dispatch が識別子なしの CommandResult.Ok() を返す。従来どおり。
        ///
        /// 【二重に呼んだとき】
        ///   後から呼んだ内容で上書きされる。Fail と混ぜて呼ばないこと。
        /// </summary>
        internal void ReportTargets(int[] masterIndices, ulong[] objectIds = null)
            => _pendingResult = CommandResult.Ok(masterIndices, objectIds);

        /// <summary>
        /// 実行結果の中身を成功結果として報告する。
        ///
        /// 【何を渡すか】
        ///   json は JSON オブジェクト 1 個の文字列。組み立ては CommandDataJson を使う
        ///   （CommandDataJson.cs）。番号列・座標列は載せない。量のあるものは
        ///   ModelContext.DataStore へ書き、名前と件数だけを載せる。
        ///
        /// 【どこから呼んでよいか】
        ///   Fail / ReportTargets と同じ。DispatchCore の switch と、
        ///   そこから直接呼ばれる受け口まで。
        ///
        /// 【呼ばなかったとき】
        ///   Dispatch が Data なしの CommandResult.Ok() を返す。従来どおり。
        ///
        /// 【二重に呼んだとき】
        ///   後から呼んだ内容で上書きされる。ReportTargets と混ぜて呼ばず、
        ///   識別子も返すなら masterIndices / objectIds をこちらへ渡すこと。
        /// </summary>
        internal void ReportData(string json, int[] masterIndices = null, ulong[] objectIds = null)
            => _pendingResult = CommandResult.Ok(masterIndices, objectIds, json);

        /// <summary>
        /// 既に報告済みの対象を保ったまま、実データだけを足す。
        ///
        /// 【なぜ要るか】
        ///   生成系は受け口（PolyLingPlayerViewerCore.ReportCreatedMesh）が
        ///   ReportTargets で masterIndices と安定 ID を報告済み。
        ///   そこへ ReportData をそのまま呼ぶと対象が消える。
        ///
        /// 【失敗が入っているとき】
        ///   何もしない。Fail の直後は呼び出し側が return する規約なので、
        ///   実際には通らない。
        /// </summary>
        private void ReportDataKeepingTargets(string json)
        {
            var prev = _pendingResult;
            if (prev != null && !prev.Success) return;

            _pendingResult = CommandResult.Ok(prev?.MasterIndices, prev?.ObjectIds, json);
        }

        /// <summary>
        /// コマンドを実行し、結果を返す。
        /// 本体（DispatchCore）は void のままなので、結果は _pendingResult 経由で受け取る。
        /// 実行中に別のコマンドが発行されることがある（生成系が Viewer 側へ委譲した先など）ため、
        /// 呼び出しごとに退避・復元する。catch は付けない（例外の伝播を変えないため）。
        /// </summary>
        public CommandResult Dispatch(PanelCommand cmd)
        {
            var saved = _pendingResult;
            _pendingResult = null;

            CommandResult result;
            try
            {
                DispatchCore(cmd);
                result = _pendingResult ?? CommandResult.Ok();
            }
            finally
            {
                _pendingResult = saved;
            }
            return result;
        }

        private void DispatchCore(PanelCommand cmd)
        {
            // プロジェクト初期化は「プロジェクトがまだ無い状態」から呼ぶコマンド。
            // 下の null 門より前で捌かないと、初回に握り潰されて何も起きない。
            if (cmd is ResetProjectCommand reset)
            {
                if (OnResetProject == null) { Fail("reset project handler not wired"); return; }
                string rpReason = OnResetProject.Invoke(reset);
                if (rpReason != null) { Fail(rpReason); return; }
                return;
            }

            // コマンド定義の検査もプロジェクトの有無に関わらず受ける。
            // 見るのはアセンブリ上の型だけで、モデルにもプロジェクトにも触れない。
            // 下の null 門より前で捌かないと、何も読み込んでいない状態で
            // "no project" になり、検査そのものができない。
            if (cmd is QueryCommandAuditCommand)
            {
                PanelCommandFactory.CountTools(out int auditUsable, out int auditSkipped);

                ReportData(CommandDataJson.New()
                    .Text("report",       PanelCommandFactoryAudit.RunAll())
                    .Int("toolsUsable",   auditUsable)
                    .Int("toolsSkipped",  auditSkipped)
                    .Build());
                return;
            }

            // Undo / Redo もプロジェクトの有無に関わらず受ける。
            // 実行は Viewer が持つ UndoManager へ委譲する（ディスパッチャの
            // MeshUndoController は対象ノードが別のため使わない）。
            if (cmd is PerformUndoCommand)
            {
                if (OnUndo == null || !OnUndo()) Fail("nothing to undo");
                return;
            }
            if (cmd is PerformRedoCommand)
            {
                if (OnRedo == null || !OnRedo()) Fail("nothing to redo");
                return;
            }

            // Unity クリップ → VRMA 変換はモデルもプロジェクトも見ない。
            // 下の null 門より前で捧かないと、何も読み込んでいない状態で
            // "no project" になって黙って何も起きない。
            if (cmd is ConvertUnityClipToVrmaCommand clipToVrmaCmd)
            {
                if (OnConvertUnityClipToVrma == null)
                {
                    Fail("vrma convert handler not wired");
                    Debug.LogError("[PolyLing] VRMA 変換: 受け口が配線されていません");
                    return;
                }
                string clipToVrmaReason = OnConvertUnityClipToVrma.Invoke(clipToVrmaCmd);
                if (clipToVrmaReason != null)
                {
                    Fail(clipToVrmaReason);
                    Debug.LogError($"[PolyLing] VRMA 変換に失敗: {clipToVrmaReason}");
                    return;
                }
                return;
            }

            // 生成系（図形生成・生成メッシュ追加）と読み込み系は、プロジェクトも
            // モデルも無い状態から呼べる。
            //
            // 生成系は実処理側が
            //   PlaceGeneratedMesh → PrepareHandlersForGeneratedMesh → EnsureProject
            // でプロジェクトとモデルを作り、PrimitiveMeshFinalize → EnsureDefaultMaterialSlot
            // で材質スロットも作る。
            //
            // 読み込み系は PlayerLocalLoader.FinishLoad（PlayerLocalLoader.cs:145-150）が
            // _project == null のときにプロジェクトを作り、AddModel → SelectModel まで済ませる。
            // ここで門を掛けると、何も読み込んでいない状態で "no project" になり、
            // MCP から 1 本目のファイルを開けなくなる。
            //
            // 診断ログは通したいので、ResetProjectCommand のような早期 return にはしない。
            bool createsOwnProject =
                   cmd is CreatePrimitiveMeshCommand || cmd is AddGeneratedMeshCommand
                || cmd is ImportPmxFileCommand       || cmd is ImportMqoFileCommand
                || cmd is ImportObjFileCommand       || cmd is ImportVrmFileCommand
                || cmd is LoadProjectFileCommand     || cmd is LoadProjectCsvCommand;

            var project = _getProject();
            if (project == null && !createsOwnProject) { Fail("no project"); return; }
            var model   = project?.CurrentModel;

            // DescribeCommand は補間文字列を作る。PLDiag.Cmd の中で捨てられる場合でも
            // 引数側は必ず評価されるため、スイッチをここで見てから呼ぶ。
            if (PLDiag.Enabled && PLDiag.Command)
                PLDiag.Cmd(DescribeCommand(cmd));

            // 性能ログ用の件数計上。記録 OFF のときは bool 判定 1 回で戻る。
            // DescribeCommand の戻り値は使わない（型名だけで足り、文字列生成を増やさないため）。
            PLPerfLog.CountCommand(cmd?.GetType().Name);

            // 元は 1 本の switch だった。分類ごとのメソッド（PlayerCommandDispatcher.<分類>.cs）へ
            // 区画をそのまま移してある。型パターンは上から順に当たるので、この呼び出し順は
            // 元の switch の並びと同じにしてあり、入れ替えてはいけない。
            if (DispatchQuery(cmd, project, model))             return;
            if (DispatchModel(cmd, project, model))             return;
            if (DispatchEditTools(cmd, project, model))         return;
            if (DispatchMeshAttributes(cmd, project, model))    return;
            if (DispatchBoneMorphUv(cmd, project, model))       return;
            if (DispatchBlend(cmd, project, model))             return;
            if (DispatchObjectGroup(cmd, project, model))       return;
            if (DispatchDeformSkin(cmd, project, model))        return;
            if (DispatchMirrorHumanoidVrm(cmd, project, model)) return;
            if (DispatchSpringBone(cmd, project, model))        return;
            if (DispatchTPoseMergeMorph(cmd, project, model))   return;
            if (DispatchSelectionSets(cmd, project, model))     return;
            if (DispatchNormalEdit(cmd, project, model))        return;
            if (DispatchMeshSelectionSets(cmd, project, model)) return;

            // ── その他（モーフ変換・プレビュー等）は Player では未実装
            Debug.LogWarning($"[PlayerCommandDispatcher] Unhandled PanelCommand: {cmd.GetType().Name}");
            return;
        }

        // ================================================================
        // 診断
        // ================================================================

        /// <summary>
        /// 診断ログ用にコマンドを1行で表す。
        /// 型名だけでは追えないもの（対象インデックス・設定値）を型ごとに補う。
        /// ここに無い型は型名のみを出す。
        /// </summary>
        private static string DescribeCommand(PanelCommand cmd)
        {
            switch (cmd)
            {
                case null: return "<null>";
                case ToggleVisibilityCommand c:
                    return $"ToggleVisibility model={c.ModelIndex} idx={c.MasterIndex}";
                case SetBatchVisibilityCommand c:
                    return $"SetBatchVisibility model={c.ModelIndex} visible={c.Visible} targets={PLDiag.Ids(c.MasterIndices)}";
                case ToggleLockCommand c:
                    return $"ToggleLock model={c.ModelIndex} idx={c.MasterIndex}";
                case SetBatchLockCommand c:
                    return $"SetBatchLock model={c.ModelIndex} locked={c.Locked} targets={PLDiag.Ids(c.MasterIndices)}";
                case CycleMirrorTypeCommand c:
                    return $"CycleMirrorType model={c.ModelIndex} idx={c.MasterIndex}";
                case SetBatchMirrorTypeCommand c:
                    return $"SetBatchMirrorType model={c.ModelIndex} mirrorType={c.MirrorType} targets={PLDiag.Ids(c.MasterIndices)}";
                case SetMirrorEnabledCommand c:
                    return $"SetMirrorEnabled model={c.ModelIndex} enabled={c.Enabled} targets={PLDiag.Ids(c.MasterIndices)}";
                case ConvertToMeshFilterCommand c:
                    return $"ConvertToMeshFilter model={c.ModelIndex} parentMode={c.ParentMode} targets={PLDiag.Ids(c.MasterIndices)}";
                case ConvertToSkinnedCommand c:
                    return $"ConvertToSkinned model={c.ModelIndex} bone={c.BoneMasterIndex} targets={PLDiag.Ids(c.MasterIndices)}";
                case ResolveMirrorBoneIndexCommand c:
                    return $"ResolveMirrorBoneIndex model={c.ModelIndex}";
                case RenameMeshCommand c:
                    return $"RenameMesh model={c.ModelIndex} idx={c.MasterIndex}";
                case RenameMeshesCommand c:
                    return $"RenameMeshes model={c.ModelIndex} count={(c.MasterIndices?.Length ?? 0)}";
                case ApplySelectionDictionaryCommand c:
                    return $"ApplySelectionDictionary model={c.ModelIndex} setIndex={c.SetIndex} add={c.AddToExisting}";
                case SwitchModelCommand c:
                    return $"SwitchModel target={c.TargetModelIndex}";
                default:
                    return cmd.GetType().Name + $" model={cmd.ModelIndex}";
            }
        }

        // ================================================================
        // メッシュ属性 Undo 記録ヘルパー
        // ================================================================

        /// <summary>
        /// 属性変更1件を MeshAttributesBatchChangeRecord で記録する。
        /// </summary>
        private void RecordAttributeChange(
            MeshAttributeChange before, MeshAttributeChange after, string desc)
        {
            RecordAttributeChanges(
                new List<MeshAttributeChange> { before },
                new List<MeshAttributeChange> { after },
                desc);
        }

        /// <summary>
        /// 属性変更をまとめて1レコードとして記録する。Undo/Redo は一度で戻る。
        /// oldList / newList は同じ並び・同じ長さであること。
        /// </summary>
        private void RecordAttributeChanges(
            List<MeshAttributeChange> oldList, List<MeshAttributeChange> newList, string desc)
        {
            if (_undoController == null) return;
            if (oldList == null || newList == null || oldList.Count == 0) return;

            var __record = new MeshAttributesBatchChangeRecord(oldList, newList);
            PLDiag.UndoRecord("MeshList", desc, __record);
            _undoController.MeshListStack.Record(__record, desc);
            _undoController.FocusMeshList();
        }
    }
}
