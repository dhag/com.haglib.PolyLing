// PlayerCommandDispatcher.cs
// PanelCommand を受け取り ProjectContext に適用するクラス。
// PolyLingPlayerViewer の DispatchPanelCommand を分離したもの。
// Runtime/Poly_Ling_Player/View/ に配置

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

namespace Poly_Ling.Player
{
    public class PlayerCommandDispatcher
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

        /// <summary>図形生成コマンドの実行。</summary>
        public Action<CreatePrimitiveMeshCommand> OnCreatePrimitiveMesh;

        /// <summary>出来上がったメッシュをそのまま置くコマンドの実行。</summary>
        public Action<AddGeneratedMeshCommand> OnAddGeneratedMesh;

        /// <summary>穴つなぎコマンドの実行。</summary>
        public Action<CreateHoleBridgeCommand> OnCreateHoleBridge;

        /// <summary>辺群ブリッジコマンドの実行。</summary>
        public Action<CreateEdgeBridgeCommand> OnCreateEdgeBridge;

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
        /// </summary>
        public Action<SculptStrokeCommand> OnSculptStroke;

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

        /// <summary>面削除コマンドの実行。</summary>
        public Action<DeleteFacesCommand> OnDeleteFaces;

        // ================================================================
        // 位相編集（パラメータを持たない実行系）
        //
        // 実処理は各 Tool が正典。ここに第 2 実装を置かず、ハンドラへ委譲する。
        // 戻り値は失敗理由。成功時は null（P1-3: 無言 return を残さない）。
        // ================================================================

        /// <summary>面の結合コマンドの実行。</summary>
        public Func<FaceMergeCommand, string> OnFaceMerge;

        /// <summary>面の結合（頂点を外す方式）コマンドの実行。</summary>
        public Func<FaceMergeCollapseCommand, string> OnFaceMergeCollapse;

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
        // 作業軸
        //
        // モデルの頂点・選択は書き換えないので Undo も所有権判定も持たない。
        // ================================================================

        /// <summary>作業軸の状態差し替えコマンドの実行。</summary>
        public Func<SetWorkAxisCommand, string> OnSetWorkAxis;

        /// <summary>作業軸ライブラリ呼び出しコマンドの実行。</summary>
        public Func<RecallWorkAxisCommand, string> OnRecallWorkAxis;

        /// <summary>穴点数合わせコマンドの実行。</summary>
        public Action<MatchHoleRingCountCommand> OnMatchHoleRingCount;

        /// <summary>プロジェクト初期化コマンドの実行。</summary>
        public Action<ResetProjectCommand> OnResetProject;

        /// <summary>歪み複製コマンドの実行。</summary>
        public Action<CreateObjectArrayCommand> OnCreateObjectArray;

        /// <summary>Undo の実行。1 段戻せたら true を返すこと。</summary>
        public Func<bool> OnUndo;

        /// <summary>Redo の実行。1 段やり直せたら true を返すこと。</summary>
        public Func<bool> OnRedo;

        // ================================================================
        // ディスパッチ
        // ================================================================

        /// <summary>
        /// 直近のコマンドの失敗内容。DispatchCore 内のハンドラが Fail() で設定する。
        /// 未設定のまま DispatchCore を抜けたら成功とみなす。
        /// </summary>
        private CommandResult _pendingResult;

        /// <summary>ハンドラから失敗を報告する。設定後は通常どおり return してよい。</summary>
        private void Fail(string reason) => _pendingResult = CommandResult.Fail(reason);

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
                OnResetProject?.Invoke(reset);
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

            // 生成系（図形生成・生成メッシュ追加）は、プロジェクトもモデルも無い状態から
            // 呼べる。実処理側が
            //   PlaceGeneratedMesh → PrepareHandlersForGeneratedMesh → EnsureProject
            // でプロジェクトとモデルを作り、PrimitiveMeshFinalize → EnsureDefaultMaterialSlot
            // で材質スロットも作るため、ここで門を掛けると自動生成へ到達できない。
            // 診断ログは通したいので、ResetProjectCommand のような早期 return にはしない。
            bool createsOwnModel = cmd is CreatePrimitiveMeshCommand || cmd is AddGeneratedMeshCommand;

            var project = _getProject();
            if (project == null && !createsOwnModel) { Fail("no project"); return; }
            var model   = project?.CurrentModel;

            // DescribeCommand は補間文字列を作る。PLDiag.Cmd の中で捨てられる場合でも
            // 引数側は必ず評価されるため、スイッチをここで見てから呼ぶ。
            if (PLDiag.Enabled && PLDiag.Command)
                PLDiag.Cmd(DescribeCommand(cmd));

            // 性能ログ用の件数計上。記録 OFF のときは bool 判定 1 回で戻る。
            // DescribeCommand の戻り値は使わない（型名だけで足り、文字列生成を増やさないため）。
            PLPerfLog.CountCommand(cmd?.GetType().Name);

            switch (cmd)
            {
                // ── モデル選択
                case SwitchModelCommand c:
                {
                    // Undo 記録のため切替前の CurrentModelIndex を保存。
                    int __oldIdx = project.CurrentModelIndex;
                    project.SelectModel(c.TargetModelIndex);
                    int __newIdx = project.CurrentModelIndex;
                    PLDiag.Cmd($"SwitchModel {__oldIdx} -> {__newIdx} " +
                               $"current=\"{project.CurrentModel?.Name ?? "<null>"}\"");

                    var switchedModel = project.CurrentModel;
                    if (switchedModel != null)
                    {
                        // Phase 2a-2g-1: ClearScene + RebuildAdapter + SetSelectionState +
                        // UpdateSelectedDrawableMesh + NotifyCameraChanged を集約。
                        _viewportManager.EnterSceneReset(project, clearScene: true);
                        _viewportManager.EnterCameraChanged(
                            _viewportManager.PerspectiveViewport,
                            CameraChangePhase.Committed);
                    }

                    // 問題 A/B: モデル切替を Undo 記録し、UndoController の内部 Context を
                    // 新しい ActiveProject / CurrentModel に同期する。
                    if (_undoController != null)
                    {
                        _undoController.SetProjectContext(project);
                        _undoController.SetModelContext(project.CurrentModel);
                        _undoController.RecordModelSwitch(__oldIdx, __newIdx);
                    }

                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── モデル名前変更
                case RenameModelCommand c:
                    var renameTarget = project.GetModel(c.ModelIndex);
                    if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                        renameTarget.Name = c.NewName;
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── モデル削除
                case DeleteModelCommand c:
                    project.RemoveModelAt(c.ModelIndex);
                    _rebuildModelList();
                    return;

                // ── メッシュ追加（空メッシュ）
                case AddMeshCommand _:
                {
                    if (model == null) return;
                    var addBefore = MeshFilterToSkinnedRecord.CaptureList(model);
                    var newMc = new MeshContext
                    {
                        MeshObject        = new MeshObject("New Mesh"),
                        UnityMesh         = new Mesh(),
                        OriginalPositions = new Vector3[0],
                    };
                    newMc.ParentModelContext = model;
                    model.Add(newMc);
                    model.OnListChanged?.Invoke();
                    if (_undoController != null)
                    {
                        var addAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var addRecord = new MeshFilterToSkinnedRecord { BeforeList = addBefore, AfterList = addAfter };
                        {
                            string __dbgDesc = "Add Mesh";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, addRecord);
                            _undoController.MeshListStack.Record(addRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) return;
                    {
                        // Undo 記録のため選択前のインデックスをキャプチャ
                        var __oldSelected = model.CaptureAllSelectedIndices();

                        switch (sel.Category)
                        {
                            case MeshCategory.Drawable:
                                model.ClearMeshSelection();
                                foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                                // ModelContext.SelectMesh() は先頭で ClearMeshSelection() を呼ぶ
                                // 単一選択メソッド。ここで呼ぶと直前の AddToMeshSelection ループの
                                // 結果が破棄され、SelectedDrawableMeshIndices が常に 1 個になる。
                                // メッシュリストの複数選択を受け取る本経路では呼んではならない。
                                // ActiveCategory は AddToMeshSelection が Mesh に設定する。
                                var selMc = model.ActiveMeshContext;
                                if (selMc != null)
                                {
                                    _selectionOps?.SetSelectionState(selMc.Selection);
                                    _renderer?.SetSelectionState(selMc.Selection);
                                }
                                // Phase 2a-2g-1: UpdateSelectedDrawableMesh を EnterTopologyChanged に集約。
                                _viewportManager.EnterTopologyChanged(project);
                                break;
                            case MeshCategory.Bone:
                                model.ClearBoneSelection();
                                foreach (int idx in sel.Indices) model.AddToBoneSelection(idx);
                                // 描画メッシュ側と違い、ここは Enter〜 を呼んでいなかった。
                                // そのため原点マーカー（水色ダイヤ）とギズモが組み直されず、
                                // ボーンを選んでも視点を動かすまで表示が変わらなかった。
                                _viewportManager.EnterSelectionChanged(project);
                                break;
                            case MeshCategory.Morph:
                                model.ClearMorphSelection();
                                foreach (int idx in sel.Indices) model.AddToMorphSelection(idx);
                                _viewportManager.EnterSelectionChanged(project);
                                break;
                        }

                        // Undo 記録: 3 カテゴリ全部 CaptureAllSelectedIndices で一元管理。
                        // SequenceEqual で差分なしなら記録されない (RecordMeshSelectionChange 内部で判定)。
                        var __newSelected = model.CaptureAllSelectedIndices();
                        PLDiag.Cmd($"SelectMesh {sel.Category} old={PLDiag.Ids(__oldSelected)} " +
                                   $"new={PLDiag.Ids(__newSelected)}");
                        _undoController?.SetModelContext(model);
                        _undoController?.RecordMeshSelectionChange(__oldSelected, __newSelected);
                    }
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── 頂点・辺・面・線分の選択
                //
                // 書き込み先の解決・線分の扱い・頂点への展開・選択 Undo は
                // PlayerSelectionOps と MoveToolHandler が持つ。以前はここに同じ
                // 選択処理をもう 1 組持っていたが、クリック経路と食い違っていたため
                // 削除して委譲に寄せた。
                // 食い違っていた点: 書き込み先が単一 MasterIndex で非加算でも他メッシュの
                // 選択が残った / 線分(SelectionState.Lines)を扱えなかった /
                // ExpandLinkedVertices を通らず「選んだ要素の頂点も選択する」が効かなかった /
                // 選択 Undo を積まなかった / クリック経路が行わない
                // SetSelectionState の差し替えをしていた。
                case SelectElementsCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSelectElements == null)
                    {
                        Fail("selection handler not wired");
                        return;
                    }
                    string selReason = OnSelectElements.Invoke(c);
                    if (selReason != null)
                    {
                        Fail(selReason);
                        return;
                    }
                    // _notifyPanels は呼ばない。
                    // 反映は PlayerSelectionOps.ApplyElementSet 末尾の
                    // OnSelectionChanged が担い、クリック経路と同じ重さになる。
                    // NotifyPanels(Selection) が追加で行うのは
                    // EnterSelectionChanged（全メッシュ×全頂点のフラグ再計算と
                    // 全頂点 GPU 転送）、可視セクション refresh の 2 周目、
                    // PlayerBlendSubPanel.OnSelectionChanged、
                    // PolyLingPlayerServer.NotifySelectionChanged だが、
                    // いずれも要素選択では不要（前 3 者は OnSelectionChanged 側で
                    // 足りる／メッシュ増減しか見ない。最後は選択署名に
                    // 要素選択を含まないため差分なしで早期 return する）。
                    return;
                }

                // ── 選択頂点の移動
                //
                // 頂点への展開規則・マグネット・Undo 記録・リモート配信は
                // MoveToolHandler が持つ。以前はここに同じ移動処理をもう 1 組
                // 持っていたが、マウス経路と食い違っていたため削除して委譲に寄せた。
                // 食い違っていた点: 対象が単一 MasterIndex だった / Selection.Vertices
                // しか見ず辺・面・線分の選択を頂点へ展開していなかった /
                // マグネットを通らなかった / OnVerticesCommitted を呼ばないため
                // 他クライアントへ配信されなかった。
                //
                // GPU 反映はここでは行わない。マウス経路と同じく ApplyDelta 内の
                // OnSyncMeshPositions が担う。
                case MoveSelectedVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnMoveSelectedVertices == null)
                    {
                        Fail("move handler not wired");
                        return;
                    }
                    string moveReason = OnMoveSelectedVertices.Invoke(c);
                    if (moveReason != null)
                    {
                        Fail(moveReason);
                        return;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── ピボット移動（原点だけ移動）
                //
                // 変形・子 BoneTransform の補償・Undo のグループ化は
                // ObjectMoveTool(OriginOnly) が持つ。以前はここに同じ処理をもう 1 組
                // 持っていたが、マウス経路と食い違っていたため削除して委譲に寄せた。
                // 食い違っていた点: 対象が単一 MasterIndex だった / スキン判定
                // (MeshType.Mesh && !IsSkinned) が無かった / 孤立頂点を除外していた /
                // 直接の子のワールド位置を保つ補償が無かった /
                // MeshListStack のグループ化が無く Undo が対象数だけ分かれていた。
                case MovePivotCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnMovePivot == null)
                    {
                        Fail("pivot handler not wired");
                        return;
                    }
                    string pivotReason = OnMovePivot.Invoke(c);
                    if (pivotReason != null)
                    {
                        Fail(pivotReason);
                        return;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スカルプトストローク
                //
                // 変形は SculptTool が持つ。以前はここに同じブラシ処理をもう 1 組
                // 持っていたが、Draw の視線方向による反転補正が無く、距離モード
                // （リンク距離）の分岐も無かったためマウス経路と結果が食い違っていた。
                // 削除して委譲に寄せた。GPU 反映と Undo 記録はハンドラ側が行う。
                case SculptStrokeCommand c:
                {
                    if (model == null) return;
                    if (OnSculptStroke == null)
                    {
                        Fail("sculpt handler not wired");
                        return;
                    }
                    OnSculptStroke.Invoke(c);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 詳細選択
                //
                // 実処理は AdvancedSelectTool（EdgeLoopSelectMode ほか）が持つ。
                // 以前はここに同じ選択アルゴリズムをもう 1 組持っていたが、
                // マウス経路と結果が食い違っていたため削除し、委譲に寄せた。
                // 例: 旧 AdvEdgeLoop は VertexPair が V1<=V2 へ正規化される
                //     （EdgeTypes.cs:21-25）ことで逆方向の探索が初回で break し、
                //     輪の片側しか拾えていなかった。
                case AdvancedSelectCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnAdvancedSelect == null)
                    {
                        Fail("advanced select handler not wired");
                        return;
                    }
                    string advReason = OnAdvancedSelect.Invoke(c);
                    if (advReason != null) { Fail(advReason); return; }
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

                // ── 属性選択（クリック非依存）
                //
                // 走査は AdvancedSelectTool.ExecuteAttributeSelect が正典。
                // パネルの「実行」ボタンもこのコマンド経由に統一してある。
                case AdvancedSelectByAttributeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnAdvancedSelectByAttribute == null)
                    {
                        Fail("attribute select handler not wired");
                        return;
                    }
                    string attrReason = OnAdvancedSelectByAttribute.Invoke(c);
                    if (attrReason != null) { Fail(attrReason); return; }
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

                // ── 位相編集（パラメータを持たない実行系）
                //
                // 実処理は FaceMergeTool ほかが正典。受け口はハンドラへ委譲し、
                // 対象照合（MasterIndices と現在の選択が一致するか）もハンドラが行う。
                // 位相が変わるので Selection ではなく Topology を通知する。
                case FaceMergeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnFaceMerge == null) { Fail("face merge handler not wired"); return; }
                    string fmReason = OnFaceMerge.Invoke(c);
                    if (fmReason != null) { Fail(fmReason); return; }
                    return;
                }

                case FaceMergeCollapseCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnFaceMergeCollapse == null) { Fail("face merge collapse handler not wired"); return; }
                    string fmcReason = OnFaceMergeCollapse.Invoke(c);
                    if (fmcReason != null) { Fail(fmcReason); return; }
                    return;
                }

                case Quad4To1Command c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnQuad4To1 == null) { Fail("quad 4to1 handler not wired"); return; }
                    string q41Reason = OnQuad4To1.Invoke(c);
                    if (q41Reason != null) { Fail(q41Reason); return; }
                    return;
                }

                case Tri4To1Command c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnTri4To1 == null) { Fail("tri 4to1 handler not wired"); return; }
                    string t41Reason = OnTri4To1.Invoke(c);
                    if (t41Reason != null) { Fail(t41Reason); return; }
                    return;
                }

                case VertexDissolveCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnVertexDissolve == null) { Fail("vertex dissolve handler not wired"); return; }
                    string vdReason = OnVertexDissolve.Invoke(c);
                    if (vdReason != null) { Fail(vdReason); return; }
                    return;
                }

                case SplitVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSplitVertices == null) { Fail("split vertices handler not wired"); return; }
                    string svReason = OnSplitVertices.Invoke(c);
                    if (svReason != null) { Fail(svReason); return; }
                    return;
                }

                // ── 位相・頂点編集（パラメータを持つ実行系）
                //
                // 設定値はコマンドが正典。ハンドラが実行後にパネルの値へ戻すので、
                // リモートから送ってもパネルの表示は変わらない。
                case VertexHoleCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnVertexHole == null) { Fail("vertex hole handler not wired"); return; }
                    string vhReason = OnVertexHole.Invoke(c);
                    if (vhReason != null) { Fail(vhReason); return; }
                    return;
                }

                case FlipFaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnFlipFace == null) { Fail("flip face handler not wired"); return; }
                    string ffReason = OnFlipFace.Invoke(c);
                    if (ffReason != null) { Fail(ffReason); return; }
                    return;
                }

                case AlignVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnAlignVertices == null) { Fail("align vertices handler not wired"); return; }
                    string avReason = OnAlignVertices.Invoke(c);
                    if (avReason != null) { Fail(avReason); return; }
                    return;
                }

                case SmoothEdgesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSmoothEdges == null) { Fail("smooth edges handler not wired"); return; }
                    string seReason = OnSmoothEdges.Invoke(c);
                    if (seReason != null) { Fail(seReason); return; }
                    return;
                }

                case PlanarizeAlongBonesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnPlanarizeAlongBones == null) { Fail("planarize handler not wired"); return; }
                    string pabReason = OnPlanarizeAlongBones.Invoke(c);
                    if (pabReason != null) { Fail(pabReason); return; }
                    return;
                }

                case MergeVerticesCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnMergeVertices == null) { Fail("merge vertices handler not wired"); return; }
                    string mvReason = OnMergeVertices.Invoke(c);
                    if (mvReason != null) { Fail(mvReason); return; }
                    return;
                }

                // ── 位相・頂点編集（対象や生成先の指定を伴う実行系）
                case DeleteSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnDeleteSelection == null) { Fail("delete selection handler not wired"); return; }
                    string delReason = OnDeleteSelection.Invoke(c);
                    if (delReason != null) { Fail(delReason); return; }
                    return;
                }

                case PipeAlignCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnPipeAlign == null) { Fail("pipe align handler not wired"); return; }
                    string paReason = OnPipeAlign.Invoke(c);
                    if (paReason != null) { Fail(paReason); return; }
                    return;
                }

                case PlaceObjectReshapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnPlaceObjectReshape == null) { Fail("place object reshape handler not wired"); return; }
                    string porReason = OnPlaceObjectReshape.Invoke(c);
                    if (porReason != null) { Fail(porReason); return; }
                    return;
                }

                case SolidifyCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSolidify == null) { Fail("solidify handler not wired"); return; }
                    string solReason = OnSolidify.Invoke(c);
                    if (solReason != null) { Fail(solReason); return; }
                    return;
                }

                case LineExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnLineExtrude == null) { Fail("line extrude handler not wired"); return; }
                    string leReason = OnLineExtrude.Invoke(c);
                    if (leReason != null) { Fail(leReason); return; }
                    return;
                }

                case SurfaceSnapCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSurfaceSnap == null) { Fail("surface snap handler not wired"); return; }
                    string ssReason = OnSurfaceSnap.Invoke(c);
                    if (ssReason != null) { Fail(ssReason); return; }
                    return;
                }

                // ── ドラッグ確定（ベベル・押し出し）
                case EdgeBevelCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnEdgeBevel == null) { Fail("edge bevel handler not wired"); return; }
                    string ebReason = OnEdgeBevel.Invoke(c);
                    if (ebReason != null) { Fail(ebReason); return; }
                    return;
                }

                case EdgeExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnEdgeExtrude == null) { Fail("edge extrude handler not wired"); return; }
                    string eeReason = OnEdgeExtrude.Invoke(c);
                    if (eeReason != null) { Fail(eeReason); return; }
                    return;
                }

                case FaceExtrudeCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnFaceExtrude == null) { Fail("face extrude handler not wired"); return; }
                    string feReason = OnFaceExtrude.Invoke(c);
                    if (feReason != null) { Fail(feReason); return; }
                    return;
                }

                // ── スキンウェイト塗り
                case SkinWeightPaintCommand c:
                {
                    if (model == null) { Fail("no current model"); return; }
                    if (OnSkinWeightPaint == null) { Fail("skin weight paint handler not wired"); return; }
                    string swpReason = OnSkinWeightPaint.Invoke(c);
                    if (swpReason != null) { Fail(swpReason); return; }
                    return;
                }

                // ── 作業軸
                //
                // 作業軸はモデルに属さないので model の有無を条件にしない。
                case SetWorkAxisCommand c:
                {
                    if (OnSetWorkAxis == null) { Fail("work axis handler not wired"); return; }
                    string swaReason = OnSetWorkAxis.Invoke(c);
                    if (swaReason != null) { Fail(swaReason); return; }
                    return;
                }

                case RecallWorkAxisCommand c:
                {
                    if (OnRecallWorkAxis == null) { Fail("work axis recall handler not wired"); return; }
                    string rwaReason = OnRecallWorkAxis.Invoke(c);
                    if (rwaReason != null) { Fail(rwaReason); return; }
                    return;
                }

                // ── 可視性トグル
                case ToggleVisibilityCommand c:
                {
                    if (model == null) return;
                    var visCtx = model.GetMeshContext(c.MasterIndex);
                    if (visCtx == null) return;
                    ApplyVisibility(model, new[] { c.MasterIndex }, !visCtx.IsVisible, "Toggle Visibility");
                    return;
                }

                // ── 一括可視性
                case SetBatchVisibilityCommand c:
                {
                    if (model == null) return;
                    ApplyVisibility(model, c.MasterIndices, c.Visible,
                        $"Set Visibility: {(c.Visible ? "on" : "off")}");
                    return;
                }

                // ── ロックトグル
                case ToggleLockCommand c:
                {
                    if (model == null) return;
                    var lckCtx = model.GetMeshContext(c.MasterIndex);
                    if (lckCtx == null) return;
                    ApplyLock(model, new[] { c.MasterIndex }, !lckCtx.IsLocked, "Toggle Lock");
                    return;
                }

                // ── 一括ロック
                case SetBatchLockCommand c:
                {
                    if (model == null) return;
                    ApplyLock(model, c.MasterIndices, c.Locked,
                        $"Set Lock: {(c.Locked ? "on" : "off")}");
                    return;
                }

                // ── IgnorePoseInArmature 設定
                case SetIgnorePoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        ctx.IgnorePoseInArmature = c.Value;
                        if (c.Value && ctx.BoneTransform != null)
                            ctx.BoneTransform.Rotation = Vector3.zero;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── 担当者（EditorName）の設定・解放
                // 到達時点で権限判定は完了している:
                //   リモート発 → RemoteOwnership.TryAuthorize が可否を決めて弾く
                //   ローカル発 → ホスト自身の操作なので無条件に許可
                // よってここは確定適用（force）。ObjectIds の照合だけは残す。
                case SetObjectEditorCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).SetObjectEditor(
                        c.MasterIndices, c.EditorName, c.ObjectIds,
                        requesterName: null, force: true);
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── 生成系。実処理は Viewer 側にあるので委譲する
                // モデルが無くても通す（実処理側が作る）。上の createsOwnModel を参照。
                case CreatePrimitiveMeshCommand c:
                    OnCreatePrimitiveMesh?.Invoke(c);
                    return;

                case AddGeneratedMeshCommand c:
                    OnAddGeneratedMesh?.Invoke(c);
                    return;

                case CreateHoleBridgeCommand c:
                    if (model == null) return;
                    OnCreateHoleBridge?.Invoke(c);
                    return;

                case CreateEdgeBridgeCommand c:
                    if (model == null) return;
                    OnCreateEdgeBridge?.Invoke(c);
                    return;

                case DeleteFacesCommand c:
                    if (model == null) return;
                    OnDeleteFaces?.Invoke(c);
                    return;

                case MatchHoleRingCountCommand c:
                    if (model == null) return;
                    OnMatchHoleRingCount?.Invoke(c);
                    return;

                case CreateObjectArrayCommand c:
                    if (model == null) return;
                    OnCreateObjectArray?.Invoke(c);
                    return;

                // ── オブジェクト原点の一括設定（CSV読み込み）
                case ApplyObjectOriginsCommand c:
                {
                    if (model == null) return;
                    ApplyObjectOrigins(model, c);
                    return;
                }

                // ── 姿勢くさびの生成
                case GenerateObjectPoseWedgesCommand c:
                {
                    if (model == null) return;
                    GenerateObjectPoseWedges(project, model, c);
                    return;
                }

                // ── 姿勢くさびの取り込み
                case ApplyObjectPoseWedgesCommand c:
                {
                    if (model == null) return;
                    ApplyObjectPoseWedges(model, c);
                    return;
                }

                // ── PreserveNormals 設定
                case SetPreserveNormalsCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var pnCtx = model.GetMeshContext(idx);
                        if (pnCtx == null) continue;
                        pnCtx.PreserveNormals = c.Value;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ミラー分岐ルート設定
                case SetMirrorBranchRootCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        ctx.IsMirrorBranchRoot = c.Value;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ミラータイプ
                case CycleMirrorTypeCommand c:
                {
                    if (model == null) return;
                    var mirCtx = model.GetMeshContext(c.MasterIndex);
                    if (mirCtx == null) return;

                    int mirOld = mirCtx.MirrorType;
                    // なし→分離→結合→なし。3 以上は MeshContext.MirrorType の定義に無く、
                    // MQO の mirror 属性へそのまま書き出されてしまうため作らない。
                    mirCtx.MirrorType = Poly_Ling.View.MirrorViewUtil.NextType(mirOld);
                    PLDiag.AttrChange("MirrorType", c.MasterIndex, mirCtx.Name,
                        mirOld.ToString(), mirCtx.MirrorType.ToString());
                    RecordAttributeChange(
                        new MeshAttributeChange { Index = c.MasterIndex, MirrorType = mirOld },
                        new MeshAttributeChange { Index = c.MasterIndex, MirrorType = mirCtx.MirrorType },
                        "Cycle Mirror Type");
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── ミラーの有無そのものを切り替える
                case SetMirrorEnabledCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) return;
                    ApplyMirrorEnabled(model, c.MasterIndices, c.Enabled);
                    return;
                }

                // ── 一括ミラータイプ
                case SetBatchMirrorTypeCommand c:
                {
                    if (model == null) return;
                    int mirValue = Poly_Ling.View.MirrorViewUtil.ClampType(c.MirrorType);
                    var mirOldList = new List<MeshAttributeChange>();
                    var mirNewList = new List<MeshAttributeChange>();
                    foreach (int mi in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(mi);
                        if (ctx == null || ctx.MirrorType == mirValue) continue;

                        PLDiag.AttrChange("MirrorType", mi, ctx.Name, ctx.MirrorType.ToString(), mirValue.ToString());
                        mirOldList.Add(new MeshAttributeChange { Index = mi, MirrorType = ctx.MirrorType });
                        ctx.MirrorType = mirValue;
                        mirNewList.Add(new MeshAttributeChange { Index = mi, MirrorType = mirValue });
                    }
                    if (mirOldList.Count == 0) return;
                    RecordAttributeChanges(mirOldList, mirNewList,
                        $"Set Mirror Type: {mirValue} x{mirOldList.Count}");
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュ名前変更
                case RenameMeshCommand c:
                {
                    if (model == null) return;
                    var renCtx = model.GetMeshContext(c.MasterIndex);
                    if (renCtx == null) return;
                    if (string.IsNullOrEmpty(c.NewName)) return;
                    string __oldName = renCtx.Name;
                    if (__oldName == c.NewName) return; // 変更なし
                    renCtx.Name = c.NewName;
                    // Undo 記録 (MeshAttributesBatchChangeRecord は Name 属性に対応済み)
                    if (_undoController != null)
                    {
                        var __oldList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, Name = __oldName }
                        };
                        var __newList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, Name = c.NewName }
                        };
                        var __record = new MeshAttributesBatchChangeRecord(__oldList, __newList);
                        string __desc = $"Rename Mesh: {__oldName} -> {c.NewName}";
                        PLDiag.UndoRecord("MeshList", __desc, __record);
                        _undoController.MeshListStack.Record(__record, __desc);
                        _undoController.FocusMeshList();
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュ名の一括変更（名称一括変更 CSV）
                // 希望名は MeshRenameCsvHelper.ResolveUniqueNames でモデル全体に対して
                // 一意化してから適用する。Undo は1レコードにまとめる。
                case RenameMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.NewNames == null) return;

                    var rnsResolved = MeshRenameCsvHelper.ResolveUniqueNames(
                        model, c.MasterIndices, c.NewNames);

                    var rnsOldList = new List<MeshAttributeChange>();
                    var rnsNewList = new List<MeshAttributeChange>();
                    for (int i = 0; i < rnsResolved.Length; i++)
                    {
                        string rnsName = rnsResolved[i];
                        if (string.IsNullOrEmpty(rnsName)) continue;
                        int rnsIndex = c.MasterIndices[i];
                        var rnsCtx = model.GetMeshContext(rnsIndex);
                        if (rnsCtx == null) continue;
                        if (rnsCtx.Name == rnsName) continue;
                        PLDiag.AttrChange("Name", rnsIndex, rnsCtx.Name, rnsCtx.Name, rnsName);
                        rnsOldList.Add(new MeshAttributeChange { Index = rnsIndex, Name = rnsCtx.Name });
                        rnsCtx.Name = rnsName;
                        rnsNewList.Add(new MeshAttributeChange { Index = rnsIndex, Name = rnsName });
                    }
                    if (rnsOldList.Count == 0) return;
                    RecordAttributeChanges(rnsOldList, rnsNewList,
                        $"Rename Meshes: x{rnsOldList.Count}");
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュ折りたたみ状態変更 (TreeView の展開/折りたたみ)
                // MeshContext.IsFolding を Undo 記録付きで更新する。
                // MeshAttributesBatchChangeRecord は IsFolding 属性に対応済み。
                case SetMeshFoldingCommand c:
                {
                    if (model == null) return;
                    var fldCtx = model.GetMeshContext(c.MasterIndex);
                    if (fldCtx == null) return;
                    if (fldCtx.IsFolding == c.IsFolding) return; // 変更なし
                    bool __oldFolding = fldCtx.IsFolding;
                    fldCtx.IsFolding = c.IsFolding;
                    if (_undoController != null)
                    {
                        var __oldList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, IsFolding = __oldFolding }
                        };
                        var __newList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, IsFolding = c.IsFolding }
                        };
                        var __record = new MeshAttributesBatchChangeRecord(__oldList, __newList);
                        string __desc = $"Set Folding [{c.MasterIndex}]: {__oldFolding} -> {c.IsFolding}";
                        PLDiag.UndoRecord("MeshList", __desc, __record);
                        _undoController.MeshListStack.Record(__record, __desc);
                        _undoController.FocusMeshList();
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case DeleteMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) return;
                    // 削除前の選択状態をキャプチャ
                    var __oldSel = model.CaptureAllSelectedIndices();
                    var __removed = new List<(int, MeshContext)>();
                    // 降順で削除 (上位 index の削除で下位 index がずれないように)
                    foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                    {
                        if (idx < 0 || idx >= model.MeshContextCount) continue;
                        var __mc = model.GetMeshContext(idx);
                        if (__mc == null) continue;
                        __removed.Add((idx, __mc));
                        model.RemoveAt(idx);
                    }
                    if (__removed.Count > 0 && _undoController != null)
                    {
                        var __newSel = model.CaptureAllSelectedIndices();
                        _undoController.RecordMeshContextsRemove(__removed, __oldSel, __newSel);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── メッシュ複製
                case DuplicateMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) return;
                    var __oldSel = model.CaptureAllSelectedIndices();
                    var __added = new List<(int, MeshContext)>();
                    foreach (int idx in c.MasterIndices)
                    {
                        var srcCtx = model.GetMeshContext(idx);
                        if (srcCtx == null) continue;
                        var dup = new MeshContext
                        {
                            Name       = srcCtx.Name + "_copy",
                            MeshObject = srcCtx.MeshObject?.Clone(),
                            IsVisible  = srcCtx.IsVisible,
                            IsLocked   = srcCtx.IsLocked,
                            Depth      = srcCtx.Depth,
                        };
                        int __addedIdx = model.Add(dup);
                        __added.Add((__addedIdx, dup));
                    }
                    if (__added.Count > 0 && _undoController != null)
                    {
                        var __newSel = model.CaptureAllSelectedIndices();
                        _undoController.RecordMeshContextsAdd(__added, __oldSel, __newSel);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── メッシュリスト順序変更 (D&D/上下移動/Indent/Outdent)
                // Editor と同一ロジック (MeshListOps.ReorderMeshes) を使用。
                // Undo 記録 (MeshReorderChangeRecord) も内部で実行される。
                case ReorderMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.Entries == null || c.Entries.Length == 0) return;
                    var __ops = GetMeshListOps(model);
                    __ops.ReorderMeshes(c.Category, c.Entries, c.PreserveWorldTransform);
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── BonePose 初期化
                case InitBonePoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        if (ctx.BonePoseData == null)
                        {
                            ctx.BonePoseData          = new BonePoseData();
                            ctx.BonePoseData.IsActive = true;
                        }
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose Active
                case SetBonePoseActiveCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        // BonePoseData未初期化の場合、Active=trueで初期化する
                        if (ctx.BonePoseData == null && c.Active)
                            ctx.BonePoseData = new BonePoseData();
                        if (ctx.BonePoseData != null) ctx.BonePoseData.IsActive = c.Active;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose レイヤーリセット
                case ResetBonePoseLayersCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                        model.GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose → BindPose ベイク
                case BakePoseToBindPoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData == null) continue;
                        ctx.BindPose = ctx.WorldMatrix.inverse;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── モーフ全選択 / 全解除
                case SelectAllMorphsCommand c:
                    if (model == null) return;
                    model.ClearMorphSelection();
                    foreach (int idx in c.AllMorphIndices) model.AddToMorphSelection(idx);
                    _notifyPanels(ChangeKind.Selection);
                    return;

                case DeselectAllMorphsCommand _:
                    model?.ClearMorphSelection();
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── モーフ変換（構造変更）
                case ConvertMeshToMorphCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).ConvertMeshToMorph(
                        c.SourceIndex, c.ParentIndex, c.MorphName, c.Panel);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                case ConvertMorphToMeshCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).ConvertMorphToMesh(c.MasterIndices);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                case CreateMorphSetCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).CreateMorphSet(
                        c.SetName, c.MorphType, c.MorphIndices);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── モーフプレビュー
                // 開始と重み変更では通知しない。頂点位置の GPU 反映は
                // MeshListOps.SyncPositionsOnly（GetMeshListOps で配線）が担う。
                case StartMorphPreviewCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).StartMorphPreview(c.MorphIndices);
                    return;

                case ApplyMorphPreviewCommand c:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).ApplyMorphPreview(c.Weight);
                    return;

                // 終了時は元の頂点位置へ戻したうえで確定させる。
                // 法線抑止の解除と全 viewport の再計算が要るので DragEnd を通す。
                case EndMorphPreviewCommand _:
                    if (model == null) { Fail("no current model"); return; }
                    GetMeshListOps(model).EndMorphPreview();
                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
                    return;

                // ── パネル側で直接変更した後の通知
                case NotifyListStructureChangedCommand _:
                    if (model == null) { Fail("no current model"); return; }
                    model.OnListChanged?.Invoke();
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                case NotifyDictionaryChangedCommand _:
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BoneTransform 値設定
                case SetBoneTransformValueCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;

                        // C(ポーズ一時): BonePoseData の "Manual" 層へ差分として書く
                        if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                        {
                            ApplyPoseLayerField(ctx, c.TargetField, c.Value);
                            continue;
                        }

                        if (ctx.BoneTransform == null) continue;
                        ctx.BoneTransform.UseLocalTransform = true;
                        switch (c.TargetField)
                        {
                            case SetBoneTransformValueCommand.Field.PositionX: ctx.BoneTransform.Position = new Vector3(c.Value, ctx.BoneTransform.Position.y, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionY: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, c.Value, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionZ: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, ctx.BoneTransform.Position.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.RotationX: ctx.BoneTransform.Rotation = new Vector3(c.Value, ctx.BoneTransform.Rotation.y, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationY: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, c.Value, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationZ: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, ctx.BoneTransform.Rotation.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.ScaleX:    ctx.BoneTransform.Scale    = new Vector3(c.Value, ctx.BoneTransform.Scale.y, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleY:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, c.Value, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleZ:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, ctx.BoneTransform.Scale.y, c.Value); break;
                        }
                    }
                    model.ComputeWorldMatrices();

                    // 原点だけ移動: 対象 MeshFilter の自頂点を「開始ワールド位置を保つ」よう
                    // 再ローカル化する。ObjectMoveTool.ApplyWorldDelta / ApplyWorldRotation と同じ式。
                    if (_boneOriginOnly && _boneOriginStartWorld.Count > 0)
                    {
                        foreach (var okv in _boneOriginStartWorld)
                        {
                            if (!_boneOriginStartPositions.TryGetValue(okv.Key, out var startPos)) continue;
                            var omc = model.GetMeshContext(okv.Key);
                            var omo = omc?.MeshObject;
                            if (omo == null) continue;

                            Matrix4x4 curInv = omc.WorldMatrixInverse;
                            int n = Mathf.Min(omo.VertexCount, startPos.Length);
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 wp = okv.Value.MultiplyPoint3x4(startPos[i]);
                                var v = omo.Vertices[i];
                                v.Position = curInv.MultiplyPoint3x4(wp);
                                omo.Vertices[i] = v;
                            }
                            omo.InvalidatePositionCache();

                            // 書き換えた頂点を GPU へ送る（PresentAll 経路では位置バッファが更新されない）。
                            _viewportManager.EnterVerticesMoved(
                                project, VerticesMovedPhase.Dragging, omc);
                        }
                    }

                    // A(スキン固定): World が変わったボーンの BindPose を追従更新し、SkinningMatrix を開始時と同一に保つ
                    if (_activeBoneEditMode == BoneMoveMode.BoneOnlyRebind && _boneRebindStartSkinning.Count > 0)
                    {
                        foreach (var kv in _boneRebindStartSkinning)
                        {
                            var bmc = model.GetMeshContext(kv.Key);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            bmc.BindPose = bmc.WorldMatrix.inverse * kv.Value;
                        }
                    }
                    // Phase 2a-2g-1: ComputeWorldMatrices + UpdateTransform を EnterVerticesMoved(Dragging) に集約。
                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.Dragging);
                    // A(スキン固定): PresentAll 経路は GPU の transform 行列を push しないため、
                    // 補正後の SkinningMatrix(World×BindPose) を明示反映する（移動ツールと同じ理由）。
                    if (_activeBoneEditMode == BoneMoveMode.BoneOnlyRebind && _boneRebindStartSkinning.Count > 0)
                        _viewportManager.UpdateTransform();
                    else if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                        _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── UV展開
                case ApplyUvUnwrapCommand c:
                {
                    if (model == null) return;
                    // 先頭ターゲットを UndoController に設定（CaptureMeshObjectSnapshot に必要）
                    if (c.MasterIndices.Length > 0)
                    {
                        var uvMc = model.GetMeshContext(c.MasterIndices[0]);
                        if (uvMc?.MeshObject != null && _undoController != null)
                        {
                            _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleApplyUvUnwrap(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── マテリアルスロット追加
                case AddMaterialSlotCommand _:
                {
                    if (model == null) return;
                    var addMc = model.ActiveMeshContext;
                    if (addMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(addMc.MeshObject, addMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var addBefore = _undoController?.CaptureMeshObjectSnapshot();
                    model.AddMaterial(null);
                    model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && addBefore != null)
                    {
                        var addAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(addBefore, addAfter, "Add Material Slot");
                    }
                    if (model.AutoSetDefaultMaterials)
                    {
                        model.DefaultMaterials            = new System.Collections.Generic.List<Material>(model.Materials);
                        model.DefaultCurrentMaterialIndex = model.CurrentMaterialIndex;
                    }
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── マテリアルスロット削除
                case RemoveMaterialSlotCommand c:
                {
                    if (model == null || model.MaterialCount <= 1) return;
                    var remMc = model.ActiveMeshContext;
                    if (remMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(remMc.MeshObject, remMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var remBefore = _undoController?.CaptureMeshObjectSnapshot();
                    if (remMc?.MeshObject != null)
                        foreach (var face in remMc.MeshObject.Faces)
                        {
                            if (face.MaterialIndex == c.SlotIndex)       face.MaterialIndex = 0;
                            else if (face.MaterialIndex > c.SlotIndex)   face.MaterialIndex--;
                        }
                    model.RemoveMaterialAt(c.SlotIndex);
                    if (model.CurrentMaterialIndex >= model.MaterialCount)
                        model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && remBefore != null)
                    {
                        var remAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(remBefore, remAfter, $"Remove Material Slot [{c.SlotIndex}]");
                    }
                    if (remMc?.UnityMesh != null && remMc.MeshObject != null)
                        remMc.ReplaceUnityMesh(remMc.MeshObject.ToUnityMesh());
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 選択面にマテリアル適用
                case ApplyMaterialToFacesCommand c:
                {
                    if (model == null) return;
                    var matMc = model.GetMeshContext(c.MasterIndex);
                    if (matMc?.MeshObject == null) return;
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(matMc.MeshObject, matMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var matBefore = _undoController?.CaptureMeshObjectSnapshot();
                    foreach (int fi in c.FaceIndices)
                        if (fi >= 0 && fi < matMc.MeshObject.FaceCount)
                            matMc.MeshObject.Faces[fi].MaterialIndex = c.MaterialSlot;
                    if (_undoController != null && matBefore != null)
                    {
                        var matAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(matBefore, matAfter, $"Apply Material [{c.MaterialSlot}]");
                    }
                    // テクスチャ表面(ctx.UnityMesh)は MaterialIndex 別サブメッシュで描画されるため、
                    // MaterialIndex 変更後は UnityMesh を再構築しないと表面に反映されない
                    // （EnterTopologyChanged は編集用GPUアダプタのみ再構築し UnityMesh は触らない）。
                    matMc.ReplaceUnityMesh(matMc.MeshObject.ToUnityMesh(model.MaterialCount));
                    // Phase 2a-2g-1: Material 変更後の GPU 反映を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── マテリアル色設定
                case SetMaterialColorCommand c:
                {
                    if (model == null) return;

                    var colRef = model.GetMaterialReference(c.SlotIndex);
                    if (colRef == null) return;

                    // 永続データ側。保存に乗るのはこちら。
                    if (colRef.Data == null) colRef.Data = new Poly_Ling.Materials.MaterialData();
                    colRef.Data.SetBaseColor(c.BaseColor);

                    // 起きている Material 側。Data を書いてもキャッシュは作り直されないので、
                    // ここへ入れないと画面の色が変わらない。
                    var colMat = colRef.Material;
                    if (colMat != null)
                    {
                        if (colMat.HasProperty("_BaseColor")) colMat.SetColor("_BaseColor", c.BaseColor);
                        if (colMat.HasProperty("_Color"))     colMat.SetColor("_Color",     c.BaseColor);
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();

                    // 色だけの変更なので載せる集合は変わらない。
                    // EnterMeshAttributesChanged は集合が同じなら再構築せず、
                    // 描き直しだけを回す（PlayerViewportManager.cs:735-760）。
                    _viewportManager.EnterMeshAttributesChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── LSCM UV 展開
                case ApplyLscmUnwrapCommand c:
                {
                    if (model == null) return;
                    var lscmMc = model.GetMeshContext(c.MasterIndex);
                    if (lscmMc?.MeshObject == null) return;

                    // UndoController に対象メッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(lscmMc.MeshObject, lscmMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    var before = _undoController?.CaptureMeshObjectSnapshot();

                    // Seam エッジは実行時点の SelectedEdges から取得
                    var seamEdges = lscmMc.SelectedEdges
                        ?? new HashSet<VertexPair>();
                    var result = Poly_Ling.UI.Lscm.LscmUnwrapOperation.Execute(
                        lscmMc.MeshObject, seamEdges,
                        c.IncludeBoundaryAsSeam,
                        Mathf.Clamp(c.MaxIterations, 100, 50000));

                    if (result.Success)
                    {
                        if (_undoController != null && before != null)
                        {
                            var after = _undoController.CaptureMeshObjectSnapshot();
                            _undoController.RecordTopologyChange(before, after, "LSCM UV展開");
                        }
                        lscmMc.ReplaceUnityMesh(lscmMc.MeshObject.ToUnityMesh());
                        // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[LSCM] {result.StatusMessage}");
                    }
                    return;
                }

                // ── UV→XYZ展開メッシュ生成
                case UvToXyzCommand c:
                {
                    if (model == null) return;

                    // 追加前のリストをスナップショット（MeshListStack Undo 用）
                    var uvzBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleUvToXyz(
                        model, _undoController, BuildMinimalToolCtx(model),
                        mc =>
                        {
                            // UnityMesh は HandleUvToXyz が MeshContext の初期化子で
                            // 生成済み（PolyLingCore_UvHandlers.cs）。ここで作り直すと
                            // 1 個作っては捨てる二重生成になり、旧 Mesh が漏れる。
                            model.Add(mc);
                        },
                        () => { }, c);

                    // 追加後のリストをスナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var uvzAfter = MeshFilterToSkinnedRecord.CaptureList(model);
                        var uvzRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = uvzBefore,
                            AfterList  = uvzAfter,
                        };
                        {
                            string __dbgDesc = "UV→XYZ メッシュ生成";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, uvzRecord);
                            _undoController.MeshListStack.Record(uvzRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── XYZ→UV書き戻し
                case XyzToUvCommand c:
                {
                    if (model == null) return;
                    // ターゲットメッシュに SetMeshObject（RecordTopologyChange に必要）
                    var xyzTargetMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (xyzTargetMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(xyzTargetMc.MeshObject, xyzTargetMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleXyzToUv(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── BoneTransform スライダー開始：スナップショット保存
                case BeginBoneTransformSliderDragCommand c:
                {
                    if (model == null) return;
                    _boneTransformBeforeSnapshots.Clear();
                    foreach (int idx in c.MasterIndices)
                    {
                        var mc = model.GetMeshContext(idx);
                        if (mc?.BoneTransform != null)
                            _boneTransformBeforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
                    }

                    // A/B: 確定モードと開始状態を保持
                    _activeBoneEditMode = c.Mode;
                    _boneRebindStartSkinning.Clear();
                    _boneRebindStartBindPose.Clear();
                    _boneFreezeBefore = null;
                    _bonePoseBeforeSnapshots.Clear();

                    // 原点だけ移動: 対象 MeshFilter(非スキンド)の頂点と WorldMatrix を保存。
                    // ObjectMoveTool.SaveSnapshots の OriginOnly 分岐と同じ条件。
                    _boneOriginOnly = c.OriginOnly;
                    _boneOriginStartPositions.Clear();
                    _boneOriginStartWorld.Clear();
                    if (c.OriginOnly)
                    {
                        foreach (int idx in c.MasterIndices)
                        {
                            var omc = model.GetMeshContext(idx);
                            if (omc?.MeshObject == null) continue;
                            if (omc.Type != MeshType.Mesh || omc.IsSkinned) continue;
                            _boneOriginStartPositions[idx] = (Vector3[])omc.MeshObject.Positions.Clone();
                            _boneOriginStartWorld[idx]     = omc.WorldMatrix;
                        }
                    }
                    if (c.Mode == BoneMoveMode.BoneOnlyRebind)
                    {
                        for (int i = 0; i < model.Count; i++)
                        {
                            var bmc = model.GetMeshContext(i);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            _boneRebindStartSkinning[i] = bmc.SkinningMatrix;   // World × BindPose
                            _boneRebindStartBindPose[i] = bmc.BindPose;
                        }
                    }
                    else if (c.Mode == BoneMoveMode.SkinBakeRebind)
                    {
                        _boneFreezeBefore = new TPoseBackup();
                        TPoseConverter.CaptureBackup(model.MeshContextList, _boneFreezeBefore);
                    }
                    else if (c.Mode == BoneMoveMode.PoseLayer)
                    {
                        foreach (int idx in c.MasterIndices)
                        {
                            var bmc = model.GetMeshContext(idx);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            if (bmc.BonePoseData == null) bmc.BonePoseData = new BonePoseData();
                            bmc.BonePoseData.IsActive = true;
                            _bonePoseBeforeSnapshots[idx] = bmc.BonePoseData.CreateSnapshot();
                        }
                    }
                    return;
                }

                // ── BoneTransform スライダー終了：Undo記録
                case EndBoneTransformSliderDragCommand c:
                {
                    if (model == null || _undoController == null) { _boneTransformBeforeSnapshots.Clear(); return; }
                    if (_boneTransformBeforeSnapshots.Count == 0) return;

                    // 原点だけ移動: 頂点 + BoneTransform を 1 グループで記録する。
                    // ObjectMoveTool.CommitUndo の OriginOnly 分岐と同じ構成。
                    if (_boneOriginOnly && _boneOriginStartPositions.Count > 0)
                    {
                        _undoController.SetModelContext(model);
                        _undoController.MeshListStack.BeginGroup("原点だけ移動");

                        foreach (var okv in _boneOriginStartPositions)
                        {
                            int idx = okv.Key;
                            var omc = model.GetMeshContext(idx);
                            if (omc?.MeshObject == null || omc.BoneTransform == null) continue;

                            int vc = omc.MeshObject.VertexCount;
                            var indices = new int[vc];
                            var newPos  = new Vector3[vc];
                            for (int i = 0; i < vc; i++)
                            {
                                indices[i] = i;
                                newPos[i]  = omc.MeshObject.Vertices[i].Position;
                            }

                            _undoController.MeshListStack.Record(new PivotMoveRecord
                            {
                                MasterIndex        = idx,
                                VertexIndices      = indices,
                                OldVertexPositions = okv.Value,
                                NewVertexPositions = newPos,
                                OldBoneTransform   = _boneTransformBeforeSnapshots.TryGetValue(idx, out var ob0)
                                    ? ob0 : omc.BoneTransform.CreateSnapshot(),
                                NewBoneTransform   = omc.BoneTransform.CreateSnapshot(),
                            }, "原点だけ移動");
                        }

                        _undoController.MeshListStack.EndGroup();
                        _undoController.FocusMeshList();

                        _boneOriginOnly = false;
                        _boneOriginStartPositions.Clear();
                        _boneOriginStartWorld.Clear();
                        _boneRebindStartSkinning.Clear();
                        _boneRebindStartBindPose.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        return;
                    }

                    // C(ポーズ一時): BonePoseData の変更を記録
                    if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                    {
                        var prec = new MultiBonePoseChangeRecord();
                        foreach (var kv in _bonePoseBeforeSnapshots)
                        {
                            var mc = model.GetMeshContext(kv.Key);
                            if (mc?.BonePoseData == null) continue;
                            prec.Entries.Add(new MultiBonePoseChangeRecord.Entry
                            {
                                MasterIndex = kv.Key,
                                OldSnapshot = kv.Value,
                                NewSnapshot = mc.BonePoseData.CreateSnapshot(),
                            });
                        }
                        if (prec.Entries.Count > 0)
                        {
                            {
                                string __dbgDesc = c.Description ?? "ボーンポーズ変更";
                                PLDiag.UndoRecord("MeshList", __dbgDesc, prec);
                                _undoController.MeshListStack.Record(prec, __dbgDesc);
                            }
                            _undoController.FocusMeshList();
                        }
                        _bonePoseBeforeSnapshots.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        return;
                    }

                    // B(スキンごと確定): 頂点焼き込み＋リバインド。Tポーズ変換と同じ処理。
                    if (_activeBoneEditMode == BoneMoveMode.SkinBakeRebind && _boneFreezeBefore != null)
                    {
                        model.ComputeWorldMatrices();
                        TPoseConverter.BakeSkinnedVertices(model.MeshContextList);
                        for (int i = 0; i < model.Count; i++)
                        {
                            var bmc = model.GetMeshContext(i);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            bmc.BindPose = bmc.WorldMatrix.inverse;
                        }
                        var afterBackup = new TPoseBackup();
                        TPoseConverter.CaptureBackup(model.MeshContextList, afterBackup);

                        _undoController.SetModelContext(model);
                        var frec = new TPoseUndoRecord(_boneFreezeBefore, afterBackup,
                            model.TPoseBackup, model.TPoseBackup, c.Description ?? "スキンごと確定");
                        {
                            string __dbgDesc = c.Description ?? "スキンごと確定";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, frec);
                            _undoController.MeshListStack.Record(frec, __dbgDesc);
                        }
                        _undoController.FocusMeshList();

                        _boneFreezeBefore = null;
                        _boneRebindStartSkinning.Clear();
                        _boneRebindStartBindPose.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        model.IsDirty = true;
                        model.OnListChanged?.Invoke();
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                        return;
                    }

                    // A(スキン固定): BoneTransform＋BindPose を複合レコードで記録
                    var record = new MultiBoneMoveRebindRecord();
                    var handled = new HashSet<int>();
                    foreach (var kv in _boneTransformBeforeSnapshots)
                    {
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc?.BoneTransform == null) continue;
                        var after = mc.BoneTransform.CreateSnapshot();
                        bool btChanged = after.IsDifferentFrom(kv.Value);

                        Matrix4x4? oldBind = null, newBind = null;
                        if (_boneRebindStartBindPose.TryGetValue(kv.Key, out var ob) && ob != mc.BindPose)
                        {
                            oldBind = ob; newBind = mc.BindPose;
                        }
                        if (!btChanged && oldBind == null) continue;

                        record.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                        {
                            MasterIndex      = kv.Key,
                            OldBoneTransform = btChanged ? kv.Value : (BoneTransformSnapshot?)null,
                            NewBoneTransform = btChanged ? after    : (BoneTransformSnapshot?)null,
                            OldBindPose      = oldBind,
                            NewBindPose      = newBind,
                        });
                        handled.Add(kv.Key);
                    }
                    // リバインドで BindPose が変わった子孫ボーン（編集対象以外）も記録
                    foreach (var kv in _boneRebindStartBindPose)
                    {
                        if (handled.Contains(kv.Key)) continue;
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc == null || kv.Value == mc.BindPose) continue;
                        record.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                        {
                            MasterIndex = kv.Key,
                            OldBindPose = kv.Value,
                            NewBindPose = mc.BindPose,
                        });
                    }

                    if (record.Entries.Count > 0)
                    {
                        _undoController.SetModelContext(model);
                        {
                            string __dbgDesc = c.Description ?? "BoneTransform変更";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    _boneRebindStartSkinning.Clear();
                    _boneRebindStartBindPose.Clear();
                    _boneTransformBeforeSnapshots.Clear();
                    return;
                }

                // ── モデルブレンド: クローン作成
                case CreateBlendCloneCommand c:
                {
                    var src = project.GetModel(c.ModelIndex);
                    if (src == null) return;
                    string uniqueName = project.GenerateUniqueModelName(
                        string.IsNullOrEmpty(c.CloneNameBase) ? src.Name + "_blend" : c.CloneNameBase);
                    var clone = DeepCloneModelContext(src, uniqueName);
                    if (clone == null) return;
                    int cloneIndex = project.AddModel(clone);
                    // スキニング再計算（BoneTransform → WorldMatrix → BindPose）
                    clone.ComputeWorldAndBindPoses();
                    clone.ComputeMeshFilterBindPoses();
                    // Phase 2a-2g-1: 設計 A - クローンを CurrentModel に切り替え、
                    // 以降の Preview/Apply は通常編集フローと同じ扱いにする。
                    // Undo でモデル切替戻し → クローン削除まで戻れる。
                    project.SelectModel(cloneIndex);
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── モデルブレンド: プレビュー（Undo なし）
                case PreviewModelBlendCommand c:
                {
                    // 設計 A: クローンは CreateBlendCloneCommand で既に CurrentModel。
                    // ExecuteBlend は project.GetModel(c.CloneModelIndex) を書き換えるが、
                    // CurrentModel と同じであれば GPU は EnterTopologyChanged で正規更新される。
                    if (project.CurrentModelIndex != c.CloneModelIndex)
                    {
                        Debug.LogWarning(
                            $"[PlayerCommandDispatcher] PreviewModelBlend: CurrentModel " +
                            $"({project.CurrentModelIndex}) != CloneModelIndex ({c.CloneModelIndex})。" +
                            $"設計 A 規約違反。CreateBlendCloneCommand 後の Select が行われていない可能性。");
                        return;
                    }
                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, recalcNormals: false, blendBones: c.BlendBones,
                        onSyncMesh: null);
                    // Phase 2a-2g-1: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
                    // EnterTopologyChanged に集約。CurrentModel = clone なので正規入口で対応可能。
                    _viewportManager.EnterTopologyChanged(project);
                    return;
                }

                // ── モデルブレンド: 適用
                case ApplyModelBlendCommand c:
                {
                    // 設計 A: クローンは CreateBlendCloneCommand で既に CurrentModel。
                    if (project.CurrentModelIndex != c.CloneModelIndex)
                    {
                        Debug.LogWarning(
                            $"[PlayerCommandDispatcher] ApplyModelBlend: CurrentModel " +
                            $"({project.CurrentModelIndex}) != CloneModelIndex ({c.CloneModelIndex})。" +
                            $"設計 A 規約違反。");
                        return;
                    }
                    var cloneModelApply = project.CurrentModel;

                    // クローンモデルをUndoControllerのMeshListStackコンテキストに設定
                    _undoController?.SetModelContext(cloneModelApply);

                    // 適用前スナップショット
                    var beforePos = ModelBlendRecord.CapturePositions(cloneModelApply);

                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, c.RecalcNormals, c.BlendBones,
                        onSyncMesh: null);

                    // 適用後スナップショット
                    var afterPos = ModelBlendRecord.CapturePositions(cloneModelApply);

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var record = new ModelBlendRecord
                        {
                            BeforePositions = beforePos,
                            AfterPositions  = afterPos,
                        };
                        {
                            string __dbgDesc = "モデルブレンド適用";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
                    // EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュブレンド適用
                case ApplyBlendCommand c:
                {
                    if (model == null || project == null) return;

                    var destCtx = model.GetMeshContext(c.DestMasterIndex);
                    if (destCtx?.MeshObject == null) return;

                    // ソースは別モデルを指せる。MasterIndex は必ずその
                    // BlendSourceSpec.ModelIndex のモデル内で引くこと。
                    // 宛先モデルの索引で引くと無関係なメッシュを混ぜる。
                    var sources    = new System.Collections.Generic.List<BlendSourceEntry>();
                    var hideIndices = new System.Collections.Generic.List<int>();
                    foreach (var spec in c.Sources)
                    {
                        if (spec.Weight <= 0f) continue;
                        var srcModel = project.GetModel(spec.ModelIndex);
                        var srcCtx   = srcModel?.GetMeshContext(spec.MasterIndex);
                        if (srcCtx?.MeshObject == null) continue;
                        sources.Add(new BlendSourceEntry(srcCtx, spec.Weight));

                        // 同一モデル内のソースだけプレビュー中に隠す。
                        // 別モデルの索引を混ぜると索引空間が違うため別物を隠す。
                        if (spec.ModelIndex == c.ModelIndex && spec.MasterIndex != c.DestMasterIndex)
                            hideIndices.Add(spec.MasterIndex);
                    }
                    if (sources.Count == 0) return;

                    // ToolContext 構築（UndoController・CommandQueue 接続済み）。
                    // Undo の対象メッシュ指定は BlendOperation が SetMeshObjectFor で行う。
                    var blendCtx = BuildSkinWeightToolCtx(model);
                    if (_undoController != null)
                        _undoController.MeshUndoContext.ParentModelContext = model;

                    // バックアップ位置を取ってから確定する。
                    // ApplyBlend が同じ backup を基準に混ぜるので、
                    // ここで preview.Apply を挟むと同じ計算を 2 回走らせるだけになる。
                    var preview = new BlendPreviewState();
                    preview.Start(model, c.DestMasterIndex, hideIndices);

                    BlendOperation.ApplyBlend(
                        model, preview, sources,
                        c.RecalculateNormals, c.SelectedVerticesOnly,
                        c.MatchMode, c.CreateNewObject, blendCtx);

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── シュリンカー適用
                case ApplyShrinkCommand c:
                {
                    if (model == null) return;
                    var beforeCtx = model.GetMeshContext(c.BeforeMasterIndex);
                    if (beforeCtx?.MeshObject == null) return;

                    // 衝突計算に使うワールド座標をこの時点で1回だけ更新する。
                    _viewportManager.UpdateTransform();

                    var stops = ShrinkOperation.ComputeStopParams(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.ColliderMasterIndices,
                        c.SurfaceOffset, c.FrontFaceOnly, c.CollisionMode, c.MaxPasses,
                        mc => _viewportManager.TryGetMeshWorldPositions(model, mc, out var w) ? w : null,
                        out string shrinkError);

                    if (stops == null)
                    {
                        Debug.LogWarning($"[Shrink] 停止パラメータを算出できません: {shrinkError}");
                        return;
                    }

                    // 上書きモードのみ、ビフォーの変更を Undo に記録する。
                    // 新規モードではビフォーを変更しないため、スナップショットは取らない。
                    if (!c.CreateNewObject && _undoController != null)
                    {
                        _undoController.SetMeshObject(beforeCtx.MeshObject, beforeCtx.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    var shrinkCtx = BuildSkinWeightToolCtx(model);

                    // パネル側はコマンド送信前にプレビューを破棄して元座標へ戻している。
                    // ここでは可視状態を変更しない（hideAfter: false）。
                    var shrinkPreview = new ShrinkPreviewState();
                    if (!shrinkPreview.Start(
                            model, c.BeforeMasterIndex, c.AfterMasterIndex, stops, hideAfter: false))
                        return;

                    shrinkPreview.Apply(model, c.Slider, shrinkCtx);
                    ShrinkOperation.Apply(
                        model, shrinkPreview, c.ColliderMasterIndices,
                        c.CreateNewObject, c.RecalculateNormals, shrinkCtx);

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── 法線移植適用
                case ApplyNormalTransplantCommand c:
                {
                    if (model == null) return;

                    // プリズムの構築に使うワールド座標をこの時点で1回だけ更新する。
                    _viewportManager.UpdateTransform();

                    var ntSamples = NormalTransplantOperation.ComputeSamples(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.TargetMasterIndices,
                        c.Spherical
                            ? NormalPrismSolver.TriangleBlendMode.Spherical
                            : NormalPrismSolver.TriangleBlendMode.Linear,
                        c.AllowNearest,
                        mc => _viewportManager.TryGetMeshWorldPositions(model, mc, out var w) ? w : null,
                        out string ntError);

                    if (ntSamples == null)
                    {
                        Debug.LogWarning($"[NormalTransplant] 法線を算出できません: {ntError}");
                        return;
                    }

                    var ntCtx = BuildSkinWeightToolCtx(model);

                    // パネル側はコマンド送信前にプレビューを破棄して元法線へ戻している。
                    var ntPreview = new NormalTransplantPreviewState();
                    if (!ntPreview.Start(model, ntSamples)) return;

                    int ntApplied = NormalTransplantOperation.Apply(
                        model, ntPreview, c.Strength, ntCtx);
                    if (ntApplied <= 0) return;

                    // ミラー再ベイクで UnityMesh を作り直し得るため、再構築で揃える。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── TPSモーフ適用
                case ApplyThinPlateMorphCommand c:
                {
                    if (model == null) return;

                    var tpsLocal = ThinPlateMorphOperation.ComputeWarpedLocalPositions(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.TargetMasterIndex,
                        c.Lambda, c.SelectedControlPointsOnly,
                        out var tpsControlPoints, out string tpsError);

                    if (tpsLocal == null)
                    {
                        Debug.LogWarning($"[ThinPlateMorph] 変形を算出できません: {tpsError}");
                        return;
                    }

                    if (tpsControlPoints != null && tpsControlPoints.DuplicateCount > 0)
                    {
                        Debug.Log($"[ThinPlateMorph] 位置が重複する制御点 {tpsControlPoints.DuplicateCount} 点を除きました" +
                                  $"（{tpsControlPoints.Count} 点を使用）");
                    }

                    var tpsCtx = BuildSkinWeightToolCtx(model);
                    int tpsNewIndex = ThinPlateMorphOperation.ApplyAsNewObject(
                        model, c.TargetMasterIndex, tpsLocal, c.RecalculateNormals, tpsCtx);

                    if (tpsNewIndex < 0)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 結果オブジェクトを作成できませんでした");
                        return;
                    }

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── TPSモーフ 算出済み結果の適用（局所モードのバックグラウンド計算の受け口）
                case ApplyThinPlateMorphResultCommand c:
                {
                    if (model == null) return;
                    if (c.LocalPositions == null)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 変形結果が空です");
                        return;
                    }

                    var tpsrCtx = BuildSkinWeightToolCtx(model);
                    int tpsrNewIndex = ThinPlateMorphOperation.ApplyAsNewObject(
                        model, c.TargetMasterIndex, c.LocalPositions, c.RecalculateNormals, tpsrCtx);

                    if (tpsrNewIndex < 0)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 結果オブジェクトを作成できませんでした");
                        return;
                    }

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── UV 変更（移動・一括変換）
                case ApplyUVChangesCommand c:
                {
                    if (model == null) return;
                    var uvMc = model.GetMeshContext(c.MasterIndex);
                    if (uvMc?.MeshObject == null) return;

                    // UndoController にターゲットメッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    // before スナップショット（AfterUVs を MeshObject に書き込む前に取得）
                    var before = _undoController?.CaptureMeshObjectSnapshot();

                    // AfterUVs を MeshObject に適用
                    var mo = uvMc.MeshObject;
                    for (int i = 0; i < c.VertexIndices.Length; i++)
                    {
                        int vi = c.VertexIndices[i];
                        int ui = c.UVIndices[i];
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vx = mo.Vertices[vi];
                        if (ui >= 0 && ui < vx.UVs.Count)
                            vx.UVs[ui] = c.AfterUVs[i];
                    }

                    // after スナップショット → VertexEditStack に記録
                    if (_undoController != null && before != null)
                    {
                        var after = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(
                            new RecordTopologyChangeCommand(
                                _undoController, before, after, c.OperationName));
                    }

                    // UnityMesh + GPU 更新
                    // Phase 2a-2g-1: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.Dragging, uvMc);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スキンウェイト Flood / Normalize / Prune
                //    いずれも対象は選択中の描画オブジェクト全件。
                //    メッシュごとに UndoController を差し替えて before/after を取る
                //    （SetFaceHiddenCommand / SetSkinWeightNumericCommand と同型）。
                case FloodSkinWeightCommand c:
                    ApplySkinWeightPerMesh(project, model, "Flood Skin Weight",
                        mc => SkinWeightOperations.ApplyFloodToMesh(
                            mc, c.TargetBoneMaster, c.PaintMode, c.WeightValue, c.Strength));
                    return;

                case NormalizeSkinWeightCommand _:
                    ApplySkinWeightPerMesh(project, model, "Normalize Skin Weights",
                        mc => SkinWeightOperations.ApplyNormalizeToMesh(mc));
                    return;

                case PruneSkinWeightCommand c:
                    ApplySkinWeightPerMesh(project, model, "Prune Skin Weights",
                        mc => SkinWeightOperations.ApplyPruneToMesh(mc, c.Threshold));
                    return;

                // ── スキンウェイト 数値設定（最大 4 ボーンを直接上書き）
                case SetSkinWeightNumericCommand c:
                    ApplySkinWeightPerMesh(project, model, "Set Skin Weight (Numeric)",
                        mc => SkinWeightOperations.ApplyNumericToMesh(mc, c.BoneMasters, c.Weights));
                    return;

                // ── 対象メッシュ全件の全頂点を正規化
                //    SetSkinWeightNumericCommand と同じくメッシュごとに Undo を取る。
                case NormalizeAllSkinWeightsCommand:
                    ApplySkinWeightPerMesh(project, model, "Normalize All Skin Weights",
                        mc => SkinWeightOperations.NormalizeAllInMesh(mc));
                    return;

                // ── MeshFilter → Skinned 変換
                case ConvertMeshFilterToSkinnedCommand c:
                {
                    if (model == null) return;

                    var entries = MeshFilterToSkinnedConverter.CollectMeshEntries(model);
                    if (entries.Count == 0) return;

                    // 変換前スナップショット
                    var beforeList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // 変換実行
                    MeshFilterToSkinnedConverter.Execute(
                        model, entries, c.SwapAxisForRotated, c.SetAxisForIdentity,
                        c.TolerantMirrorBranch
                            ? MirrorBranchTolerance.Tolerant
                            : MirrorBranchTolerance.Strict);

                    // 変換後スナップショット
                    var afterList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var record = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = beforeList,
                            AfterList  = afterList,
                        };
                        {
                            string __dbgDesc = "MeshFilter → Skinned 変換";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: ClearScene + RebuildAdapter + SetSelectionState +
                    // UpdateSelectedDrawableMesh を EnterSceneReset(clearScene: true) に集約。
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── 描画オブジェクト単位: SkinnedMesh 系 → MeshFilter 系
                case ConvertToMeshFilterCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) return;

                    var mfBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var mfResults = SkinKindConverter.ToMeshFilter(
                        model, c.MasterIndices, c.ParentMode);

                    int mfDone = 0;
                    foreach (var r in mfResults) if (r.Converted) mfDone++;
                    if (mfDone == 0) return;

                    RecordMeshListSnapshot(mfBefore, model,
                        $"ウェイト破棄 → MeshFilter x{mfDone}");

                    // 階層と頂点の格納空間が変わったので、GPU バッファを作り直す。
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(
                        _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── 描画オブジェクト単位: MeshFilter 系 → SkinnedMesh 系
                case ConvertToSkinnedCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) return;

                    var skBone = model.GetMeshContext(c.BoneMasterIndex);
                    if (skBone == null || skBone.Type != MeshType.Bone)
                    {
                        Debug.LogWarning(
                            $"[SkinKind] バインド先がボーンではありません idx={c.BoneMasterIndex}");
                        return;
                    }

                    var skBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var skResults = SkinKindConverter.ToSkinned(
                        model, c.MasterIndices, c.BoneMasterIndex);

                    int skDone = 0;
                    foreach (var r in skResults) if (r.Converted) skDone++;
                    if (skDone == 0) return;

                    RecordMeshListSnapshot(skBefore, model,
                        $"スキンド化 → \"{skBone.Name}\" x{skDone}");

                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(
                        _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── ボーンの左右対応を名前から補完
                case ResolveMirrorBoneIndexCommand c:
                {
                    if (model == null) return;

                    var mbiBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var mbiResult = MirrorBoneIndexResolver.Resolve(model);
                    if (mbiResult.Resolved == 0) { _notifyPanels(ChangeKind.Attributes); return; }

                    RecordMeshListSnapshot(mbiBefore, model,
                        $"左右ボーン対応の補完 x{mbiResult.Resolved}");

                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── MediaPipe フェイス変形
                case MediaPipeFaceDeformCommand c:
                {
                    if (model == null) return;
                    var mpSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    var srcMesh = mpSrcMc?.MeshObject;
                    if (srcMesh == null) return;

                    try
                    {
                        var mpBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                        var beforeLM  = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(c.BeforePath);
                        var afterLM   = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(c.AfterPath);
                        var triangles = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.ParseTrianglesJson(
                            System.IO.File.ReadAllText(c.TrianglesPath));

                        int vertexCount = srcMesh.VertexCount;
                        var positions   = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++) positions[i] = srcMesh.Vertices[i].Position;

                        var deformer = new Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer();
                        deformer.SetBaseMesh(beforeLM, triangles);
                        deformer.Bind(positions);
                        deformer.Apply(afterLM, positions);

                        var cloned = srcMesh.Clone();
                        cloned.Name = srcMesh.Name + "_MP";
                        for (int i = 0; i < vertexCount; i++) cloned.Vertices[i].Position = positions[i];

                        var mpNewMc = new MeshContext
                        {
                            MeshObject = cloned,
                            Materials  = new System.Collections.Generic.List<Material>(
                                mpSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        };
                        mpNewMc.UnityMesh           = cloned.ToUnityMesh();
                        mpNewMc.UnityMesh.name      = cloned.Name;
                        mpNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                        mpNewMc.ParentModelContext  = model;
                        model.Add(mpNewMc);
                        model.OnListChanged?.Invoke();

                        if (_undoController != null)
                        {
                            var mpAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                            var mpRecord = new MeshFilterToSkinnedRecord { BeforeList = mpBefore, AfterList = mpAfter };
                            {
                                string __dbgDesc = "MediaPipe変形";
                                PLDiag.UndoRecord("MeshList", __dbgDesc, mpRecord);
                                _undoController.MeshListStack.Record(mpRecord, __dbgDesc);
                            }
                            _undoController.FocusMeshList();
                        }
                        // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.ListStructure);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[MediaPipeFaceDeformCommand] {ex.Message}");
                    }
                    return;
                }

                // ── Quad減面
                case QuadDecimateCommand c:
                {
                    if (model == null) return;
                    var qdSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (qdSrcMc?.MeshObject == null) return;

                    var qdBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var prms = new Poly_Ling.UI.QuadDecimator.DecimatorParams
                    {
                        TargetRatio     = c.TargetRatio,
                        MaxPasses       = c.MaxPasses,
                        NormalAngleDeg  = c.NormalAngleDeg,
                        HardAngleDeg    = c.HardAngleDeg,
                        UvSeamThreshold = c.UvSeamThreshold,
                    };
                    var result = Poly_Ling.Tools.Panels.QuadDecimator.QuadPreservingDecimator.Decimate(
                        qdSrcMc.MeshObject, prms, out MeshObject resultMesh);
                    if (resultMesh == null) return;

                    resultMesh.Name = qdSrcMc.MeshObject.Name + "_decimated";
                    var qdNewMc = new MeshContext
                    {
                        Name       = resultMesh.Name,
                        MeshObject = resultMesh,
                        Materials  = new System.Collections.Generic.List<Material>(
                            qdSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                    };
                    qdNewMc.UnityMesh           = resultMesh.ToUnityMesh();
                    qdNewMc.UnityMesh.name      = resultMesh.Name;
                    qdNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    qdNewMc.ParentModelContext  = model;
                    model.Add(qdNewMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var qdAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var qdRecord = new MeshFilterToSkinnedRecord { BeforeList = qdBefore, AfterList = qdAfter };
                        {
                            string __dbgDesc = "Quad減面";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, qdRecord);
                            _undoController.MeshListStack.Record(qdRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Mirror Bake
                case BakeMirrorCommand c:
                {
                    if (model == null) return;
                    var srcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (srcMc?.MeshObject == null)
                    {
                        Debug.LogWarning($"[MirrorBake] 対象メッシュが見つかりません masterIndex={c.SourceMasterIndex}");
                        return;
                    }

                    var bakeMo = srcMc.MeshObject;

                    if (bakeMo.MirrorBakeState != null)
                    {
                        Debug.LogWarning($"[MirrorBake] \"{srcMc.Name}\" は既に実体化済みです。先に解除してください");
                        return;
                    }

                    // ミラー平面の決定。
                    // メッシュが見た目・エクスポート用のミラーモード（MirrorType > 0）なら
                    // メッシュ自身の軸・距離を使う。そうでなければパネル指定を使う。
                    int   bakeAxis      = c.MirrorAxis;
                    float bakeOffset    = c.PlaneOffset;
                    float bakeThreshold = c.Threshold;

                    if (srcMc.MirrorType > 0)
                    {
                        bakeAxis   = srcMc.MirrorAxis == 2 ? 1 : (srcMc.MirrorAxis == 4 ? 2 : 0);
                        bakeOffset = 0f;
                        // MQO の結合ミラー(2)は mirror_dis が溶接距離。分離ミラー(1)は溶接しない。
                        bakeThreshold = srcMc.MirrorType == 2 ? srcMc.MirrorDistance : 0f;
                    }

                    // 境界頂点（選択頂点モードのときだけ渡す。メッシュ設定より優先）
                    System.Collections.Generic.List<int> bakeBoundary = null;
                    if (c.BoundaryMode == MirrorBoundaryMode.SelectedVertices)
                    {
                        var bakeSel = srcMc.Selection;
                        if (bakeSel == null || bakeSel.Vertices.Count == 0)
                        {
                            Debug.LogWarning("[MirrorBake] 選択頂点モードですが頂点が選択されていません");
                            return;
                        }
                        bakeBoundary = new System.Collections.Generic.List<int>(bakeSel.Vertices);
                    }

                    int bakeVertsBefore = bakeMo.VertexCount;
                    int bakeFacesBefore = bakeMo.FaceCount;

                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(bakeMo, srcMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var bakeBefore = _undoController?.CaptureMeshObjectSnapshot();

                    var bakeResult = MirrorBaker.BakeInPlace(
                        bakeMo, bakeAxis, bakeOffset, bakeThreshold, c.FlipU,
                        bakeBoundary, c.ProjectBoundaryToPlane);

                    if (bakeResult == null)
                    {
                        Debug.LogWarning($"[MirrorBake] 実体化に失敗しました src=\"{srcMc.Name}\"");
                        return;
                    }

                    // 見た目・エクスポート用のミラーモードは解除する（実体を持ったため）。
                    // 解除に備えて元の設定を退避しておく。
                    bakeResult.SavedMirrorType           = srcMc.MirrorType;
                    bakeResult.SavedMirrorAxis           = srcMc.MirrorAxis;
                    bakeResult.SavedMirrorDistance       = srcMc.MirrorDistance;
                    bakeResult.SavedMirrorMaterialOffset = srcMc.MirrorMaterialOffset;

                    srcMc.MirrorType = 0;
                    srcMc.InvalidateSymmetryCache();

                    bakeMo.MirrorBakeState = bakeResult;

                    SyncMeshContextAfterMirrorEdit(srcMc);

                    if (_undoController != null && bakeBefore != null)
                    {
                        var bakeAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, bakeBefore, bakeAfter, "ミラー実体化"));
                    }

                    int bakeMergedCount = 0;
                    if (bakeResult.NewVertexOrigin != null)
                        foreach (var o in bakeResult.NewVertexOrigin)
                            if (o == VertexOrigin.Merged) bakeMergedCount++;

                    Debug.Log(
                        $"[MirrorBake] \"{srcMc.Name}\" 実体化 " +
                        $"verts {bakeVertsBefore} → {bakeMo.VertexCount} " +
                        $"faces {bakeFacesBefore} → {bakeMo.FaceCount} " +
                        $"merged={bakeMergedCount} axis={bakeAxis} threshold={bakeThreshold} " +
                        $"boundary={c.BoundaryMode} project={c.ProjectBoundaryToPlane} " +
                        $"savedMirrorType={bakeResult.SavedMirrorType} → 0 " +
                        $"unityMeshVerts={(srcMc.UnityMesh != null ? srcMc.UnityMesh.vertexCount : -1)}");

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Mirror 実体化の解除（半身へ戻す）
                case UnbakeMirrorCommand c:
                {
                    if (model == null) return;
                    var ubMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (ubMc?.MeshObject == null) return;

                    var ubMo = ubMc.MeshObject;
                    var ubState = ubMo.MirrorBakeState;
                    if (ubState == null)
                    {
                        Debug.LogWarning($"[MirrorBake] \"{ubMc.Name}\" は実体化されていません");
                        return;
                    }

                    int ubVertsBefore = ubMo.VertexCount;
                    int ubFacesBefore = ubMo.FaceCount;

                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(ubMo, ubMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var ubBefore = _undoController?.CaptureMeshObjectSnapshot();

                    if (!MirrorBaker.UnbakeInPlace(ubMo, ubState, c.Mode))
                    {
                        Debug.LogWarning($"[MirrorBake] 解除に失敗しました src=\"{ubMc.Name}\"");
                        return;
                    }

                    if (c.RestoreSavedMirrorSettings)
                    {
                        // ツール内の「一時ミラー」による解除。
                        // 実体化前のミラー設定をそのまま戻す（恒久設定を変えない）。
                        ubMc.MirrorType           = ubState.SavedMirrorType;
                        ubMc.MirrorAxis           = ubState.SavedMirrorAxis;
                        ubMc.MirrorDistance       = ubState.SavedMirrorDistance;
                        ubMc.MirrorMaterialOffset = ubState.SavedMirrorMaterialOffset;
                    }
                    else
                    {
                        // 半身に戻したので、見た目・エクスポート用のミラーモードを強制的に付ける。
                        // 軸は実体化に使った軸から決める（0:X→1, 1:Y→2, 2:Z→4）。
                        ubMc.MirrorType = 2; // 結合
                        ubMc.MirrorAxis = ubState.BakeAxis == 1 ? 2 : (ubState.BakeAxis == 2 ? 4 : 1);
                        ubMc.MirrorDistance = ubState.SavedMirrorType == 2
                            ? ubState.SavedMirrorDistance
                            : ubState.Threshold;
                        ubMc.MirrorMaterialOffset = ubState.SavedMirrorMaterialOffset;
                    }
                    ubMc.InvalidateSymmetryCache();

                    ubMo.MirrorBakeState = null;

                    // 実体化中に増えていた頂点・面の選択を捨てる
                    ubMc.Selection?.ClearAll();

                    SyncMeshContextAfterMirrorEdit(ubMc);

                    if (_undoController != null && ubBefore != null)
                    {
                        var ubAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, ubBefore, ubAfter, "ミラー実体化の解除"));
                    }

                    Debug.Log(
                        $"[MirrorBake] \"{ubMc.Name}\" 解除 " +
                        $"verts {ubVertsBefore} → {ubMo.VertexCount} " +
                        $"faces {ubFacesBefore} → {ubMo.FaceCount} " +
                        $"mode={c.Mode} restoreSaved={c.RestoreSavedMirrorSettings} " +
                        $"mirrorType={ubMc.MirrorType} axis={ubMc.MirrorAxis} dist={ubMc.MirrorDistance}");

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Humanoidマッピング適用
                case ApplyHumanoidMappingCommand c:
                {
                    if (model == null || c.Mapping == null) return;
                    _undoController?.SetModelContext(model);
                    var hmBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.CopyFrom(c.Mapping);
                    var hmAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmBefore, hmAfter, "Apply Humanoid Mapping");
                        {
                            string __dbgDesc = "Apply Humanoid Mapping";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Humanoidマッピングクリア
                case ClearHumanoidMappingCommand _:
                {
                    if (model == null) return;
                    _undoController?.SetModelContext(model);
                    var hmcBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.ClearAll();
                    var hmcAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmcBefore, hmcAfter, "Clear Humanoid Mapping");
                        {
                            string __dbgDesc = "Clear Humanoid Mapping";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スプリングボーン検証用ダミー装備の生成（システムデバッグ）
                //   揺れデータのオーサリング UI が無いので、検証用に生成する。
                //   トポロジが変わるので MeshFilterToSkinnedRecord で丸ごと記録する
                //   （AddMeshCommand と同じ扱い）。
                case BuildSpringBoneTestRigCommand sbtCmd:
                {
                    if (model == null) return;

                    _undoController?.SetModelContext(model);
                    var sbtBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var sbtParams = sbtCmd.Params
                        ?? new Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams();

                    if (sbtCmd.ClearExisting)
                        Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigBuilder
                            .RemoveGenerated(model, sbtParams.Prefix);

                    var sbtResult = Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigBuilder
                        .Build(model, sbtParams);

                    Debug.Log(
                        "[BuildSpringBoneTestRig] " + sbtResult.Message +
                        $" ボーン {sbtResult.AddedBoneCount} / メッシュ {sbtResult.AddedMeshCount}" +
                        $" / チェーン {sbtResult.ChainCount} / ジョイント {sbtResult.JointCount}" +
                        $" / コライダー {sbtResult.ColliderCount}");

                    // 生成側はログ方針を持たないので、ここで流す。
                    foreach (string sbtNote in sbtResult.Notes)
                        Debug.Log("[BuildSpringBoneTestRig] " + sbtNote);
                    foreach (string sbtWarn in sbtResult.Warnings)
                        Debug.LogWarning("[BuildSpringBoneTestRig] " + sbtWarn);

                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var sbtAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var sbtRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = sbtBefore,
                            AfterList  = sbtAfter
                        };
                        {
                            string __dbgDesc = "Build SpringBone Test Rig";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, sbtRecord);
                            _undoController.MeshListStack.Record(sbtRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Tポーズ変換
                case ApplyTPoseCommand _:
                {
                    if (model == null) return;
                    var mapping = model.HumanoidMapping;
                    if (mapping == null || mapping.IsEmpty) return;

                    // SetModelContext（MeshListStack の context を現在のモデルに設定）
                    _undoController?.SetModelContext(model);

                    var beforeState    = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
                    var oldTPoseBackup = model.TPoseBackup;

                    var backup = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.ConvertToTPose(model.MeshContextList, mapping, backup);
                    model.TPoseBackup = backup;

                    var afterState = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, afterState);

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                        {
                            string __dbgDesc = "Apply T-Pose";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── この姿勢で確定（焼き込み）：現在のポーズを頂点へ焼き込み、ベースへリセット
                case FreezeCurrentPoseCommand _:
                {
                    if (model == null) return;
                    _undoController?.SetModelContext(model);

                    var beforeState = new TPoseBackup();
                    TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);

                    // 1. 現在のポーズ込みでワールド確定 → 頂点焼き込み
                    model.ComputeWorldMatrices();
                    TPoseConverter.BakeSkinnedVertices(model.MeshContextList);

                    // 2. ポーズ層を全クリア（ゼロポーズ＝ベース）
                    for (int i = 0; i < model.Count; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        mc.BonePoseData?.ClearAllLayers();
                    }

                    // 3. ベースのワールドで再計算 → リバインド（焼いた姿勢を新デフォルトに）
                    model.ComputeWorldMatrices();
                    for (int i = 0; i < model.Count; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        mc.BindPose = mc.WorldMatrix.inverse;
                    }

                    var afterState = new TPoseBackup();
                    TPoseConverter.CaptureBackup(model.MeshContextList, afterState);

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(beforeState, afterState,
                            model.TPoseBackup, model.TPoseBackup, "この姿勢で確定");
                        {
                            string __dbgDesc = "この姿勢で確定";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Tポーズ復元
                case RestoreTPoseCommand _:
                {
                    if (model?.TPoseBackup == null) return;
                    _undoController?.SetModelContext(model);

                    var restoreBefore = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreBefore);
                    var oldTPoseBackup = model.TPoseBackup;

                    Poly_Ling.Ops.TPoseConverter.RestoreFromBackup(model.MeshContextList, model.TPoseBackup);

                    var restoreAfter = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreAfter);
                    model.TPoseBackup = null;

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(restoreBefore, restoreAfter, oldTPoseBackup, null, "Restore Original Pose");
                        {
                            string __dbgDesc = "Restore Original Pose";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Tポーズ Bake（Undo不可・バックアップ破棄のみ）
                case BakeTPoseCommand _:
                {
                    if (model == null) return;
                    model.TPoseBackup = null;
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュマージ
                case MergeMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length < 2) return;

                    // 対象 MeshContext を収集
                    var mergeTargets = new System.Collections.Generic.List<MeshContext>();
                    foreach (int mi in c.MasterIndices)
                    {
                        var mctx = model.GetMeshContext(mi);
                        if (mctx?.MeshObject != null) mergeTargets.Add(mctx);
                    }
                    if (mergeTargets.Count < 2) return;

                    var baseCtx = model.GetMeshContext(c.BaseMasterIndex);
                    if (baseCtx?.MeshObject == null) return;

                    // 変更前スナップショット（MeshListStack Undo 用）
                    var mergeBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    Matrix4x4 baseWorldInv = baseCtx.WorldMatrixInverse;

                    // マージ先 MeshContext の準備
                    MeshContext destCtx;
                    if (c.CreateNewMesh)
                    {
                        destCtx = new MeshContext
                        {
                            Name             = baseCtx.MeshObject.Name + "_merged",
                            MeshObject       = new MeshObject(baseCtx.MeshObject.Name + "_merged"),
                            OriginalPositions = new Vector3[0],
                        };
                        var bt = new BoneTransform();
                        bt.CopyFrom(baseCtx.BoneTransform);
                        destCtx.BoneTransform      = bt;
                        destCtx.WorldMatrix        = baseCtx.WorldMatrix;
                        destCtx.WorldMatrixInverse = baseCtx.WorldMatrixInverse;
                        destCtx.BindPose           = baseCtx.BindPose;
                    }
                    else
                    {
                        destCtx = baseCtx;
                    }

                    MeshObject destMesh = destCtx.MeshObject;

                    // 部品IDはマージで振り直す。1 つのメッシュにつき 1 つの部品として
                    // 0 から順に付け、サブIDは全部の追記が終わってから部品ごとに 0 から振る。
                    // CreateNewMesh=false のときは base の既存頂点が部品 0 になる。
                    int mergePartsId = 0;
                    if (!c.CreateNewMesh)
                    {
                        Poly_Ling.Ops.PartsIdOps.SetPartsId(destMesh, mergePartsId);
                        mergePartsId++;
                    }

                    // 各ソースメッシュを destMesh に追記
                    foreach (var srcCtx in mergeTargets)
                    {
                        bool isBase = ReferenceEquals(srcCtx, baseCtx);
                        if (!c.CreateNewMesh && isBase) continue;

                        var srcMesh = srcCtx.MeshObject;
                        if (srcMesh == null || srcMesh.VertexCount == 0) continue;

                        Matrix4x4 xform  = baseWorldInv * srcCtx.WorldMatrix;
                        int vertexOffset = destMesh.VertexCount;

                        foreach (var v in srcMesh.Vertices)
                        {
                            var newV      = v.Clone();
                            newV.Id       = destMesh.GenerateVertexId();
                            newV.PartsId  = mergePartsId;
                            newV.Position = xform.MultiplyPoint3x4(v.Position);
                            if (v.Normals != null)
                                newV.Normals = v.Normals.Select(n => xform.MultiplyVector(n).normalized).ToList();
                            destMesh.Vertices.Add(newV);
                            destMesh.RegisterVertexId(newV.Id);
                        }

                        foreach (var f in srcMesh.Faces)
                        {
                            var newF          = f.Clone();
                            newF.Id           = destMesh.GenerateFaceId();
                            newF.VertexIndices = f.VertexIndices.Select(i => i + vertexOffset).ToList();
                            destMesh.Faces.Add(newF);
                            destMesh.RegisterFaceId(newF.Id);
                        }

                        mergePartsId++;
                    }

                    // サブIDは部品ごとに 0 から。頂点を全部並べ終えてから振る。
                    Poly_Ling.Ops.PartsIdOps.AssignSubIdByPartsId(destMesh);

                    // UnityMesh 再生成
                    var mergedUnityMesh       = destMesh.ToUnityMesh();
                    mergedUnityMesh.name      = destMesh.Name;
                    mergedUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    // CreateNewMesh=false のとき destCtx は baseCtx（既存 MeshContext）そのもの。
                    destCtx.ReplaceUnityMesh(mergedUnityMesh);
                    destCtx.OriginalPositions = (Vector3[])destMesh.Positions.Clone();

                    // モデルへの追加・削除
                    if (c.CreateNewMesh)
                    {
                        destCtx.ParentModelContext = model;
                        model.Add(destCtx);
                    }
                    else
                    {
                        var nonBaseTargets    = mergeTargets.Where(t => !ReferenceEquals(t, baseCtx)).ToList();
                        var indicesToRemove   = nonBaseTargets
                            .Select(t => model.IndexOf(t))
                            .Where(i => i >= 0)
                            .OrderByDescending(i => i)
                            .ToList();
                        foreach (int idx in indicesToRemove)
                            model.RemoveAt(idx);
                    }

                    model.OnListChanged?.Invoke();

                    // 変更後スナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var mergeAfter = MeshFilterToSkinnedRecord.CaptureList(model);
                        var mergeRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = mergeBefore,
                            AfterList  = mergeAfter,
                        };
                        {
                            string __dbgDesc = "メッシュマージ";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, mergeRecord);
                            _undoController.MeshListStack.Record(mergeRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── ブーリアン
                case BooleanMeshCommand c:
                {
                    if (model == null) return;

                    var boolCtxA = model.GetMeshContext(c.AMasterIndex);
                    var boolCtxB = model.GetMeshContext(c.BMasterIndex);
                    if (boolCtxA?.MeshObject == null || boolCtxB?.MeshObject == null) return;
                    if (ReferenceEquals(boolCtxA, boolCtxB)) return;

                    // 演算そのものは BooleanOps に集約してある。
                    // 演算空間は A のローカル空間で、結果も A の姿勢を引き継ぐ。
                    var boolResult = BooleanOps.Perform(
                        c.Op,
                        boolCtxA.MeshObject, boolCtxA.WorldMatrix,
                        boolCtxB.MeshObject, boolCtxB.WorldMatrix,
                        boolCtxA.WorldMatrixInverse,
                        c.Epsilon,
                        c.MergeVertices,
                        c.MergeThreshold,
                        resultName: null);

                    if (!boolResult.Success || boolResult.Mesh == null)
                    {
                        Debug.LogWarning("[PolyLing] ブーリアン失敗: " + boolResult.Message);
                        return;
                    }

                    // 変更前スナップショット（MeshListStack Undo 用）。
                    // 追加・削除・メッシュ差し替えが混ざるため、メッシュマージと同じく
                    // リスト全体を 1 件で記録する。
                    var boolBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    MeshObject boolMesh = boolResult.Mesh;

                    if (c.CreateNewMesh)
                    {
                        var destCtx = new MeshContext
                        {
                            Name              = boolMesh.Name,
                            MeshObject        = boolMesh,
                            OriginalPositions = new Vector3[0],
                        };
                        var boolBt = new BoneTransform();
                        boolBt.CopyFrom(boolCtxA.BoneTransform);
                        destCtx.BoneTransform      = boolBt;
                        destCtx.WorldMatrix        = boolCtxA.WorldMatrix;
                        destCtx.WorldMatrixInverse = boolCtxA.WorldMatrixInverse;
                        destCtx.BindPose           = boolCtxA.BindPose;

                        var destUnityMesh       = boolMesh.ToUnityMesh();
                        destUnityMesh.name      = boolMesh.Name;
                        destUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                        destCtx.ReplaceUnityMesh(destUnityMesh);
                        destCtx.OriginalPositions = (Vector3[])boolMesh.Positions.Clone();

                        destCtx.ParentModelContext = model;
                        model.Add(destCtx);
                    }
                    else
                    {
                        // A の中身を置き換える。名前は A のものを保つ。
                        boolMesh.Name = boolCtxA.MeshObject.Name;
                        boolCtxA.MeshObject = boolMesh;
                        boolCtxA.ClearSelection();

                        var aUnityMesh       = boolMesh.ToUnityMesh();
                        aUnityMesh.name      = boolMesh.Name;
                        aUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                        boolCtxA.ReplaceUnityMesh(aUnityMesh);
                        boolCtxA.OriginalPositions = (Vector3[])boolMesh.Positions.Clone();
                    }

                    if (c.DeleteSourceB)
                    {
                        int boolIdxB = model.IndexOf(boolCtxB);
                        if (boolIdxB >= 0) model.RemoveAt(boolIdxB);
                    }

                    model.OnListChanged?.Invoke();

                    // 変更後スナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var boolAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var boolRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = boolBefore,
                            AfterList  = boolAfter,
                        };
                        {
                            string __dbgDesc = "ブーリアン " + BooleanOps.DisplayName(c.Op);
                            PLDiag.UndoRecord("MeshList", __dbgDesc, boolRecord);
                            _undoController.MeshListStack.Record(boolRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── 差分からのモーフ生成
                case CreateMorphFromDiffCommand c:
                {
                    var morphProject = _getProject();
                    if (morphProject == null) return;
                    var baseModel  = morphProject.GetModel(c.BaseModelIndex);
                    var morphModel = morphProject.GetModel(c.MorphModelIndex);
                    if (baseModel == null || morphModel == null) return;
                    if (c.BaseModelIndex == c.MorphModelIndex) return;
                    if (baseModel.Count != morphModel.Count) return;

                    // Phase 2a-2g-1: 設計 A - baseModel を CurrentModel に切り替えてから処理。
                    // これ以降 GPU は project.CurrentModel = baseModel で EnterTopologyChanged 経由で更新可能。
                    if (morphProject.CurrentModelIndex != c.BaseModelIndex)
                        morphProject.SelectModel(c.BaseModelIndex);

                    // 変更前スナップショット
                    var morphBefore     = MeshFilterToSkinnedRecord.CaptureList(baseModel);
                    var morphExprBefore = baseModel.MorphExpressions
                        .Select(e => e.Clone()).ToList();

                    var expression   = new MorphExpression(c.MorphName, MorphType.Vertex) { Panel = c.Panel };
                    int morphCreated = 0;
                    const float DiffThresholdSq = 0.0001f * 0.0001f;

                    for (int mi = 0; mi < baseModel.Count; mi++)
                    {
                        var baseCtx  = baseModel.GetMeshContext(mi);
                        var morphCtx = morphModel.GetMeshContext(mi);
                        if (baseCtx == null || morphCtx == null) continue;
                        if (baseCtx.MeshObject == null || morphCtx.MeshObject == null) continue;
                        if (baseCtx.Type  != MeshType.Mesh && baseCtx.Type  != MeshType.BakedMirror) continue;
                        if (baseCtx.MeshObject.VertexCount != morphCtx.MeshObject.VertexCount) continue;

                        // 差分チェック
                        bool hasDiff = false;
                        int  checkCount = Mathf.Min(baseCtx.MeshObject.VertexCount, morphCtx.MeshObject.VertexCount);
                        for (int vi = 0; vi < checkCount; vi++)
                        {
                            var d = morphCtx.MeshObject.Vertices[vi].Position
                                  - baseCtx.MeshObject.Vertices[vi].Position;
                            if (d.sqrMagnitude > DiffThresholdSq) { hasDiff = true; break; }
                        }
                        if (!hasDiff) continue;

                        // Mirror 側はスキップ（Real 側から生成）
                        if (baseModel.IsMirrorSide(baseCtx)) continue;

                        // Real 側モーフ生成
                        int newIdx = CreateMorphMeshContextInDispatcher(
                            baseModel, baseCtx, mi, morphCtx.MeshObject,
                            c.MorphName, c.Panel, expression);
                        morphCreated++;

                        // Mirror 側モーフ生成
                        var pair = baseModel.GetMirrorPair(baseCtx);
                        if (pair != null && pair.Real == baseCtx && pair.Mirror != null)
                        {
                            int mirrorParentIdx = baseModel.MeshContextList.IndexOf(pair.Mirror);
                            if (mirrorParentIdx >= 0)
                                CreateMirrorMorphMeshContextInDispatcher(
                                    baseModel, pair, mirrorParentIdx, newIdx,
                                    baseCtx.MeshObject, morphCtx.MeshObject,
                                    c.MorphName, c.Panel, expression);
                        }
                    }

                    if (morphCreated == 0) return;

                    baseModel.MorphExpressions.Add(expression);
                    baseModel.OnListChanged?.Invoke();

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var morphAfter     = MeshFilterToSkinnedRecord.CaptureList(baseModel);
                        var morphExprAfter = baseModel.MorphExpressions.Select(e => e.Clone()).ToList();
                        var record = new MorphCreateRecord
                        {
                            BeforeList        = morphBefore,
                            AfterList         = morphAfter,
                            BeforeExpressions = morphExprBefore,
                            AfterExpressions  = morphExprAfter,
                        };
                        {
                            string __dbgDesc = $"モーフ作成: {c.MorphName}";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: 設計 A - baseModel = CurrentModel なので EnterTopologyChanged で統一。
                    _viewportManager.EnterTopologyChanged(morphProject);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── パーツ選択辞書 ─────────────────────────────────────────────
                case SavePartsSetCommand c:
                {
                    if (model == null) return;
                    var psMc = model.ActiveMeshContext;
                    if (psMc == null) return;
                    var psSel = psMc.Selection;
                    if (psSel == null || !psSel.HasAnySelection) return;
                    string psName = string.IsNullOrEmpty(c.SetName)
                        ? psMc.GenerateUniqueSelectionSetName("Selection")
                        : c.SetName;
                    if (psMc.FindSelectionSetByName(psName) != null)
                        psName = psMc.GenerateUniqueSelectionSetName(psName);
                    var psSnap = psSel.CreateSnapshot();
                    var psSet  = Poly_Ling.Selection.PartsSelectionSet.FromCurrentSelection(
                        psName, psSnap.Vertices, psSnap.Edges, psSnap.Faces, psSnap.Lines, psSnap.Mode);
                    psMc.PartsSelectionSetList.Add(psSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case LoadPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: false);
                    return;

                case AddPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: true, subtract: false);
                    return;

                case SubtractPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: true);
                    return;

                case DeletePartsSetCommand c:
                {
                    if (model == null) return;
                    var delMc = model.ActiveMeshContext;
                    var delSets = delMc?.PartsSelectionSetList;
                    if (delSets == null || c.SetIndex < 0 || c.SetIndex >= delSets.Count) return;
                    delSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case RenamePartsSetCommand c:
                {
                    if (model == null) return;
                    var rnMc   = model.ActiveMeshContext;
                    var rnSets = rnMc?.PartsSelectionSetList;
                    if (rnSets == null || c.SetIndex < 0 || c.SetIndex >= rnSets.Count) return;
                    string rnName = c.NewName;
                    if (rnMc.FindSelectionSetByName(rnName) != null && rnName != rnSets[c.SetIndex].Name)
                        rnName = rnMc.GenerateUniqueSelectionSetName(rnName);
                    rnSets[c.SetIndex].Name = rnName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 法線再計算 除外辞書 ─────────────────────────────────────────
                case SaveNormalExcludeSetCommand c:
                {
                    if (model == null) return;
                    var nxMc = model.ActiveMeshContext;
                    var nxMo = nxMc?.MeshObject;
                    if (nxMo == null) return;
                    var nxSel = nxMc.Selection;
                    if (nxSel == null || !nxSel.HasAnySelection) return;
                    if (nxMo.NormalRecalcExcludeList == null)
                        nxMo.NormalRecalcExcludeList = new List<PartsSelectionSet>();
                    string nxName = GenerateUniqueNormalExcludeName(
                        nxMo, string.IsNullOrEmpty(c.SetName) ? "NormalExclude" : c.SetName);
                    var nxSnap = nxSel.CreateSnapshot();
                    var nxSet  = PartsSelectionSet.FromCurrentSelection(
                        nxName, nxSnap.Vertices, nxSnap.Edges, nxSnap.Faces, nxSnap.Lines, nxSnap.Mode);
                    nxMo.NormalRecalcExcludeList.Add(nxSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case LoadNormalExcludeSetCommand c:
                    NormalExcludeSetApply(model, c.SetIndex);
                    return;

                case DeleteNormalExcludeSetCommand c:
                {
                    if (model == null) return;
                    var nxdList = model.ActiveMeshContext?.MeshObject?.NormalRecalcExcludeList;
                    if (nxdList == null || c.SetIndex < 0 || c.SetIndex >= nxdList.Count) return;
                    nxdList.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case RenameNormalExcludeSetCommand c:
                {
                    if (model == null) return;
                    var nxrMo   = model.ActiveMeshContext?.MeshObject;
                    var nxrList = nxrMo?.NormalRecalcExcludeList;
                    if (nxrList == null || c.SetIndex < 0 || c.SetIndex >= nxrList.Count) return;
                    if (string.IsNullOrEmpty(c.NewName)) return;
                    string nxrName = c.NewName;
                    if (nxrName != nxrList[c.SetIndex].Name)
                        nxrName = GenerateUniqueNormalExcludeName(nxrMo, nxrName);
                    nxrList[c.SetIndex].Name = nxrName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case ExportPartsSetsCsvCommand c:
                {
                    if (model == null) return;
                    if (string.IsNullOrEmpty(c.FolderPath)) return;
                    var exTargets = CollectSelectedMeshContexts(model);
                    if (exTargets.Count == 0) return;
                    PartsSetCsvHelper.ExportSetsToFolder(exTargets, c.FolderPath);
                    return;
                }

                case ImportPartsSetCsvCommand c:
                {
                    if (model == null) return;
                    if (string.IsNullOrEmpty(c.FolderPath)) return;
                    var imTargets = c.ByObjectName ? null : CollectSelectedMeshContexts(model);
                    if (!c.ByObjectName && imTargets.Count == 0) return;
                    if (PartsSetCsvHelper.ImportSetsFromFolder(model, c.FolderPath, c.ByObjectName, imTargets) > 0)
                        _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 面の表示・非表示 ───────────────────────────────────────────
                case SetFaceHiddenCommand c:
                {
                    if (model == null) return;
                    var fhTargets = CollectSelectedMeshContexts(model);
                    if (fhTargets.Count == 0) return;

                    int fhTotal = 0;
                    var fhChanged = new List<MeshContext>();

                    foreach (var mc in fhTargets)
                    {
                        var mo = mc?.MeshObject;
                        if (mo == null) continue;

                        // Undo は MeshObject 丸ごとのスナップショットで戻す
                        // （面フラグは MeshObject.Clone が引き継ぐ）。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mo, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var fhBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = ApplyFaceHidden(mc, c.Operation);
                        if (changed <= 0) continue;

                        fhTotal += changed;
                        fhChanged.Add(mc);

                        if (_undoController != null && fhBefore != null)
                        {
                            var fhAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, fhBefore, fhAfter, $"Face Hide ({c.Operation})"));
                        }
                    }

                    if (fhTotal > 0)
                    {
                        // 面ポリゴンの取捨は Unity Mesh の三角形、
                        // 辺・頂点・ヒットテストは GPU バッファ側で決まる。
                        // 前者は三角形だけ張り直し、後者は EnterTopologyChanged で再構築する。
                        foreach (var mc in fhChanged)
                        {
                            if (mc.UnityMesh == null) continue;
                            mc.MeshObject.ApplyTrianglesToUnityMesh(mc.UnityMesh, model.MaterialCount);
                        }

                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                    }

                    Debug.Log($"[FaceHide] {c.Operation}: {fhTargets.Count} オブジェクト / {fhTotal} 面");
                    return;
                }

                // ── 法線編集 ───────────────────────────────────────────────────
                case NormalEditCommand c:
                {
                    if (model == null) return;
                    var neTargets = CollectSelectedMeshContexts(model);
                    if (neTargets.Count == 0) return;

                    // RecalcByAngle / Break はスロット数が変わり得る。その場合は
                    // Unity Mesh を作り直す必要があるので描画更新の段を分ける。
                    bool slotCountMayChange =
                        c.Operation == NormalEditCommand.Op.RecalcByAngle ||
                        c.Operation == NormalEditCommand.Op.Break;

                    int neTotal = 0;
                    var neSynced = new List<MeshContext>();

                    foreach (var mc in neTargets)
                    {
                        var mo = mc?.MeshObject;
                        if (mo == null) continue;

                        // Undo は MeshObject 丸ごとのスナップショットで戻す。
                        // スロット（UV/法線）の増減も含めて復元する必要があるため。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mo, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var neBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = ApplyNormalEdit(mc, c);
                        if (changed <= 0) continue;

                        neTotal += changed;
                        neSynced.Add(mc);

                        // 手で編集した法線は、頂点移動時の自動再計算で消えてしまう
                        // （MeshUndoContext.ApplyVertexPositionsToMesh）。維持フラグを立てる。
                        mo.PreserveNormals = true;

                        if (_undoController != null && neBefore != null)
                        {
                            var neAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, neBefore, neAfter, $"Normal Edit ({c.Operation})"));
                        }
                    }

                    if (neTotal > 0)
                    {
                        // ミラー側の面は選択できないため、実体側の編集結果を写す。
                        // スロット数が変わる操作でも実体側と 1:1 に張り直される。
                        // 生成ミラー（MirrorGeometryDerived）のみが対象。
                        int neMirrored = MirrorBranchOps.RebakeDerivedMirrorNormals(
                            model.MeshContextList, model.MaterialCount);

                        // ミラー側の UnityMesh を作り直した場合は GPU も再構築が要る。
                        bool neRebuild = slotCountMayChange || neMirrored > 0;

                        // スロット数が変わらない操作でも、Unity Mesh の法線だけは
                        // 差し替える必要がある。差し替えられなければ作り直す。
                        if (!neRebuild)
                        {
                            foreach (var mc in neSynced)
                            {
                                if (mc.UnityMesh == null) { neRebuild = true; break; }
                                if (!mc.MeshObject.ApplyNormalsToUnityMesh(mc.UnityMesh))
                                {
                                    neRebuild = true;
                                    break;
                                }
                            }
                        }

                        if (neRebuild)
                        {
                            _viewportManager.EnterTopologyChanged(project);
                        }
                        else
                        {
                            foreach (var mc in neSynced)
                                _viewportManager.EnterVertexAttributesChanged(
                                    project, mc, weights: false, uvs: false);
                        }

                        _notifyPanels(ChangeKind.Attributes);
                    }

                    Debug.Log($"[NormalEdit] {c.Operation}: {neTargets.Count} オブジェクト / {neTotal} コーナー");
                    return;
                }

                case RepairVertexIdsCommand c:
                {
                    if (model == null) return;
                    var idTargets = CollectSelectedMeshContexts(model);
                    if (idTargets.Count == 0) return;

                    int totalChanged = 0;
                    foreach (var mc in idTargets)
                    {
                        if (mc?.MeshObject == null) continue;

                        // Undo はメッシュごとに記録する。MeshObjectSnapshot は
                        // MeshObject.Clone() を保持し、Vertex.Clone() が Id を
                        // 引き継ぐため、ID の変更もそのまま復元できる。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mc.MeshObject, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var idBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = c.Mode switch
                        {
                            RepairVertexIdsCommand.RepairMode.AssignMissing      => VertexIdOps.AssignMissing(mc),
                            RepairVertexIdsCommand.RepairMode.ResolveDuplicates  => VertexIdOps.ResolveDuplicates(mc),
                            RepairVertexIdsCommand.RepairMode.ReassignSequential => VertexIdOps.ReassignSequential(mc),
                            RepairVertexIdsCommand.RepairMode.ClearAll           => VertexIdOps.ClearAll(mc),
                            _ => 0,
                        };
                        totalChanged += changed;

                        if (changed > 0 && _undoController != null && idBefore != null)
                        {
                            var idAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, idBefore, idAfter, $"Repair Vertex Ids ({c.Mode})"));
                        }
                    }

                    // 頂点IDは描画に影響しないので GPU 再構築は不要。
                    // パネル表示（診断結果）だけ更新させる。
                    if (totalChanged > 0) _notifyPanels(ChangeKind.Attributes);
                    Debug.Log($"[VertexId] {c.Mode}: {idTargets.Count} オブジェクト / {totalChanged} 頂点");
                    return;
                }

                case AssignPartsIdsCommand c:
                {
                    if (model == null) return;

                    var partsMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (partsMc?.MeshObject == null)
                    {
                        Debug.LogWarning(
                            $"[PartsId] 対象メッシュが見つかりません masterIndex={c.TargetMasterIndex}");
                        LastPartsIdResult = PartsIdAssignResult.Fail("対象メッシュが見つかりません");
                        return;
                    }
                    var partsMo = partsMc.MeshObject;

                    // リファレンスは「1 パーツの頂点数」を取るためだけに読む。書き換えない。
                    int perPart = 0;
                    if (c.Mode == AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount)
                    {
                        var refMc = model.GetMeshContext(c.ReferenceMasterIndex);
                        if (refMc?.MeshObject == null || refMc.MeshObject.VertexCount == 0)
                        {
                            Debug.LogWarning(
                                $"[PartsId] リファレンスが見つかりません masterIndex={c.ReferenceMasterIndex}");
                            LastPartsIdResult = PartsIdAssignResult.Fail("リファレンスが見つかりません");
                            return;
                        }
                        if (ReferenceEquals(refMc, partsMc))
                        {
                            Debug.LogWarning("[PartsId] リファレンスに対象と同じオブジェクトは指定できません");
                            LastPartsIdResult =
                                PartsIdAssignResult.Fail("リファレンスに対象と同じオブジェクトは指定できません");
                            return;
                        }
                        perPart = refMc.MeshObject.VertexCount;
                    }

                    // Undo は頂点ID修復と同じ MeshObjectSnapshot 方式。
                    // Vertex.Clone() が PartsId / SubId を引き継ぐ（MeshObject.cs:313-314）ので
                    // スナップショットで元に戻せる。
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(partsMo, partsMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var partsBefore = _undoController?.CaptureMeshObjectSnapshot();

                    PartsIdAssignResult partsResult;
                    switch (c.Mode)
                    {
                        case AssignPartsIdsCommand.PartsIdMode.Connectivity:
                            partsResult = PartsIdAssignOps.AssignByConnectivity(partsMo, c.IsolatedPolicy);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount:
                            partsResult = PartsIdAssignOps.AssignByVertexCount(partsMo, perPart);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.SubIdOnly:
                            partsResult = PartsIdAssignOps.AssignSubIdOnly(partsMo);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.Clear:
                            partsResult = PartsIdAssignOps.Clear(partsMo);
                            break;
                        default:
                            LastPartsIdResult = PartsIdAssignResult.Fail("未知の採番モードです");
                            return;
                    }

                    if (!partsResult.Success)
                    {
                        Debug.LogWarning($"[PartsId] {c.Mode}: {partsResult.Reason}");
                        LastPartsIdResult = partsResult;
                        _notifyPanels(ChangeKind.Attributes);
                        return;
                    }

                    if (_undoController != null && partsBefore != null)
                    {
                        var partsAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, partsBefore, partsAfter, $"Assign Parts Ids ({c.Mode})"));
                    }

                    LastPartsIdResult = partsResult;

                    // パーツID / サブIDは描画に影響しないので GPU 再構築は不要。
                    _notifyPanels(ChangeKind.Attributes);
                    Debug.Log(
                        $"[PartsId] {c.Mode}: \"{partsMc.Name}\" 頂点 {partsResult.VertexCount} / "
                      + $"パーツ {partsResult.PartCount} / 孤立頂点 {partsResult.IsolatedVertexCount}");
                    return;
                }

                case TransferVertexDataCommand c:
                {
                    var srcModel = project?.GetModel(c.SourceModelIndex);
                    var dstModel = project?.GetModel(c.TargetModelIndex);
                    if (srcModel == null || dstModel == null) return;
                    if (c.SourceMeshIndices == null || c.TargetMeshIndices == null) return;

                    int pairCount = Math.Min(c.SourceMeshIndices.Length, c.TargetMeshIndices.Length);
                    if (pairCount == 0) return;

                    int totalWritten = 0;
                    var syncedTargets = new List<MeshContext>();
                    for (int p = 0; p < pairCount; p++)
                    {
                        var srcMc = srcModel.GetMeshContext(c.SourceMeshIndices[p]);
                        var dstMc = dstModel.GetMeshContext(c.TargetMeshIndices[p]);
                        if (srcMc?.MeshObject == null || dstMc?.MeshObject == null) continue;

                        // Undo は転送先メッシュごとに記録する。頂点数・面数は変えないが、
                        // 位置 / UV / ウェイト / ID などを書き換えるため MeshObject
                        // 丸ごとのスナップショットで戻せるようにする。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(dstMc.MeshObject, dstMc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = dstModel;
                        }
                        var tvBefore = _undoController?.CaptureMeshObjectSnapshot();

                        var r = VertexDataTransferOps.Transfer(
                            srcModel, srcMc, dstModel, dstMc, c.MatchMode, c.Kinds);
                        totalWritten += r.Written;

                        foreach (var w in r.Warnings)
                            Debug.LogWarning($"[VertexTransfer] {r.SourceName} → {r.TargetName}: {w}");
                        Debug.Log($"[VertexTransfer] {r.Summary}");

                        if (r.Written > 0)
                        {
                            syncedTargets.Add(dstMc);
                            if (_undoController != null && tvBefore != null)
                            {
                                var tvAfter = _undoController.CaptureMeshObjectSnapshot();
                                _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                    _undoController, tvBefore, tvAfter, "Transfer Vertex Data"));
                            }
                        }
                    }

                    if (totalWritten > 0)
                    {
                        // ------------------------------------------------------------
                        // 描画更新は転送した項目に応じて段階を選ぶ。
                        // 以前は常に EnterTopologyChanged を呼んでいたが、これは
                        // RebuildAdapter（UnifiedSystemAdapter を Dispose して GPU
                        // ComputeBuffer を全再確保）を伴い、頂点数が変わらない転送には
                        // 過剰で実機で重かった。
                        //
                        //   UV / 法線 / ウェイト / フラグ … バッファ構築時に焼き込まれる
                        //     (UnifiedBufferManager_Build 参照)。差分更新の口が無いため
                        //     再構築が要る。
                        //   位置 … SyncMeshPositionsAndTransform で差分同期できる。
                        //   頂点ID / モーフ基準 / 選択辞書 … 描画に出ないので更新不要。
                        //
                        // また、転送先が CurrentModel でない場合は今の adapter が
                        // 別モデルのものなので更新しても無駄（かつ誤り）。モデル切替時に
                        // EnterSceneReset で作り直されるため、ここでは何もしない。
                        // ------------------------------------------------------------
                        bool targetIsCurrent = project != null
                            && project.CurrentModelIndex == c.TargetModelIndex;

                        const VertexDataKind rebuildKinds =
                              VertexDataKind.UVs
                            | VertexDataKind.Normals
                            | VertexDataKind.Flags
                            | VertexDataKind.BoneWeight
                            | VertexDataKind.MirrorBoneWeight;

                        bool needsRebuild  = (c.Kinds & rebuildKinds) != 0;
                        bool positionOnly  = !needsRebuild && c.Kinds.HasFlag(VertexDataKind.Position);

                        if (targetIsCurrent && needsRebuild)
                        {
                            _viewportManager.EnterTopologyChanged(project);
                        }
                        else if (targetIsCurrent && positionOnly)
                        {
                            // 書き換えたメッシュだけ位置を同期し、最後に一度だけ
                            // カリング再計算と再描画を行う。
                            foreach (var mc in syncedTargets)
                                _viewportManager.EnterVerticesMoved(
                                    project, VerticesMovedPhase.Dragging, mc);
                            _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
                        }

                        _notifyPanels(ChangeKind.Attributes);
                    }
                    return;
                }

                case SaveMeshSelSetsCsvCommand c:
                {
                    if (model == null) return;
                    if (string.IsNullOrEmpty(c.FilePath)) return;
                    MeshSelSetCsvHelper.SaveToFile(model, c.FilePath);
                    return;
                }

                case LoadMeshSelSetsCsvCommand c:
                {
                    if (model == null) return;
                    if (string.IsNullOrEmpty(c.FilePath)) return;
                    if (MeshSelSetCsvHelper.LoadFromFile(model, c.FilePath) > 0)
                        _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュ選択辞書 ───────────────────────────────────────────
                case SaveSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var sdCategory = c.Category switch
                    {
                        MeshCategory.Bone  => ModelContext.SelectionCategory.Bone,
                        MeshCategory.Morph => ModelContext.SelectionCategory.Morph,
                        _                  => ModelContext.SelectionCategory.Mesh,
                    };
                    string sdName = string.IsNullOrEmpty(c.SetName)
                        ? model.GenerateUniqueMeshSelectionSetName("MeshSet")
                        : c.SetName;
                    if (model.FindMeshSelectionSetByName(sdName) != null)
                        sdName = model.GenerateUniqueMeshSelectionSetName(sdName);
                    var sdSet = new MeshSelectionSet(sdName) { Category = sdCategory };
                    foreach (var n in c.MeshNames)
                        if (!string.IsNullOrEmpty(n) && !sdSet.MeshNames.Contains(n))
                            sdSet.MeshNames.Add(n);
                    model.MeshSelectionSets.Add(sdSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case ApplySelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var sdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= sdSets.Count) return;

                    // Undo 用：適用前のメッシュ選択を記録
                    var sdOldSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (c.AddToExisting)
                        sdSets[c.SetIndex].AddTo(model);
                    else
                        sdSets[c.SetIndex].ApplyTo(model);
                    var sdNewSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (_undoController != null)
                    {
                        var sdRecord = new MeshSelectionChangeRecord(sdOldSel, sdNewSel);
                        {
                            string __dbgDesc = "メッシュ選択辞書適用";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, sdRecord);
                            _undoController.MeshListStack.Record(sdRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: UpdateSelectedDrawableMesh を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

                case DeleteSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var dsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= dsdSets.Count) return;
                    dsdSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case RenameSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var rsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= rsdSets.Count) return;
                    string rsdName = c.NewName;
                    if (model.FindMeshSelectionSetByName(rsdName) != null && rsdName != rsdSets[c.SetIndex].Name)
                        rsdName = model.GenerateUniqueMeshSelectionSetName(rsdName);
                    rsdSets[c.SetIndex].Name = rsdName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── その他（モーフ変換・プレビュー等）は Player では未実装
                default:
                    Debug.LogWarning($"[PlayerCommandDispatcher] Unhandled PanelCommand: {cmd.GetType().Name}");
                    return;
            }
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
        // 可視・ロックの適用
        // ================================================================

        /// <summary>
        /// 可視性を設定する。ミラー側メッシュへも同じ値を広げる。
        ///
        /// ミラー側は実体側の従属だが、IsVisible / IsLocked を追随させる経路が
        /// 元々存在せず、実体を消してもミラーだけ残っていた。
        /// 姿勢（SyncDerivedMirrorTransforms）と同じ「実体側が正」の方針に合わせる。
        /// Undo にはミラー側の変更も含める。含めないと戻したときに片側だけ残る。
        /// </summary>
        private void ApplyVisibility(ModelContext model, IReadOnlyList<int> masterIndices, bool visible, string desc)
        {
            if (model == null || masterIndices == null) return;

            var targets = ExpandToMirrorPeers(model, masterIndices);
            var oldList = new List<MeshAttributeChange>();
            var newList = new List<MeshAttributeChange>();

            foreach (int mi in targets)
            {
                var ctx = model.GetMeshContext(mi);
                if (ctx == null || ctx.IsVisible == visible) continue;
                PLDiag.AttrChange("IsVisible", mi, ctx.Name, ctx.IsVisible.ToString(), visible.ToString());
                oldList.Add(new MeshAttributeChange { Index = mi, IsVisible = ctx.IsVisible });
                ctx.IsVisible = visible;
                newList.Add(new MeshAttributeChange { Index = mi, IsVisible = visible });
            }

            if (oldList.Count == 0) return;
            RecordAttributeChanges(oldList, newList, $"{desc} x{oldList.Count}");
            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>ロックを設定する。ミラー側メッシュへも同じ値を広げる。</summary>
        private void ApplyLock(ModelContext model, IReadOnlyList<int> masterIndices, bool locked, string desc)
        {
            if (model == null || masterIndices == null) return;

            var targets = ExpandToMirrorPeers(model, masterIndices);
            var oldList = new List<MeshAttributeChange>();
            var newList = new List<MeshAttributeChange>();

            foreach (int mi in targets)
            {
                var ctx = model.GetMeshContext(mi);
                if (ctx == null || ctx.IsLocked == locked) continue;
                PLDiag.AttrChange("IsLocked", mi, ctx.Name, ctx.IsLocked.ToString(), locked.ToString());
                oldList.Add(new MeshAttributeChange { Index = mi, IsLocked = ctx.IsLocked });
                ctx.IsLocked = locked;
                newList.Add(new MeshAttributeChange { Index = mi, IsLocked = locked });
            }

            if (oldList.Count == 0) return;
            RecordAttributeChanges(oldList, newList, $"{desc} x{oldList.Count}");
            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>指定インデックスに、対応するミラー側インデックスを足した一覧を返す。</summary>
        private static List<int> ExpandToMirrorPeers(ModelContext model, IReadOnlyList<int> masterIndices)
        {
            var targets = new List<int>(masterIndices.Count * 2);
            foreach (int mi in masterIndices)
            {
                if (mi < 0) continue;
                if (!targets.Contains(mi)) targets.Add(mi);
                MirrorBranchOps.CollectMirrorPeers(model, mi, targets);
            }
            return targets;
        }

        // ================================================================
        // ミラーの有無
        // ================================================================

        /// <summary>
        /// ミラーの有無を切り替える。ミラー側 MeshContext を作る／始末する。
        ///
        /// 解消の扱いを MirrorGeometryDerived で分ける。
        ///   true （MQO 系）… 実体側から再生成できるので破棄する。
        ///                     ミラーの付け外しを繰り返す使い方で、残すとゴミが増える。
        ///   false（PMX 系）… ボーンウェイト等を持つので独立メッシュとして残す。
        ///                     実体側に ObjectId を控え、再ミラー化で引き当てる。
        ///
        /// リスト構造が変わるため ChangeKind.ListStructure で通知する。
        /// </summary>
        private void ApplyMirrorEnabled(ModelContext model, int[] masterIndices, bool enabled)
        {
            var oldSel = model.CaptureAllSelectedIndices();

            var removed = new List<(int, MeshContext)>();
            var added   = new List<(int Index, MeshContext MeshContext)>();
            int changed = 0;

            // 破棄・挿入で index がずれるため降順に処理する
            foreach (int realIdx in masterIndices.OrderByDescending(i => i))
            {
                var realCtx = model.GetMeshContext(realIdx);
                if (realCtx == null) continue;

                if (enabled)
                {
                    if (EnableMirror(model, realIdx, realCtx, added)) changed++;
                }
                else
                {
                    if (DisableMirror(model, realIdx, realCtx, removed)) changed++;
                }
            }

            if (changed == 0) return;

            if (_undoController != null)
            {
                var newSel = model.CaptureAllSelectedIndices();
                _undoController.SetModelContext(model);
                if (removed.Count > 0) _undoController.RecordMeshContextsRemove(removed, oldSel, newSel);
                if (added.Count   > 0) _undoController.RecordMeshContextsAdd(added, oldSel, newSel);
            }

            // 生成・破棄でリスト構造と階層が変わったのでワールド行列を組み直す。
            //   ComputeWorldMatrices の冒頭で SyncDerivedMirrorTransforms が走り、
            //   ミラー側の姿勢と階層親を実体側からそろえる。これを通さないと
            //   生成直後のミラーが未計算の行列のまま描画される。
            //   他の姿勢変更コマンドは軒並みこれを呼んでおり、ここだけ抜けていた。
            model.ComputeWorldMatrices();

            _notifyPanels(ChangeKind.ListStructure);
        }

        /// <summary>ミラーを解消する。戻り値は変化があったか。</summary>
        private bool DisableMirror(ModelContext model, int realIdx, MeshContext realCtx,
                                   List<(int, MeshContext)> removed)
        {
            var peers = new List<int>();
            MirrorBranchOps.CollectMirrorPeers(model, realIdx, peers);

            bool touched = false;

            foreach (int mirrorIdx in peers.OrderByDescending(i => i))
            {
                var mirrorCtx = model.GetMeshContext(mirrorIdx);
                if (mirrorCtx == null) continue;

                // ペアの登録は先に外す（破棄・独立化のどちらでも不要になる）
                model.MirrorPairs?.RemoveAll(pr => pr.Mirror == mirrorCtx || pr.Real == realCtx);

                if (mirrorCtx.MirrorGeometryDerived)
                {
                    PLDiag.AttrChange("MirrorDiscard", mirrorIdx, mirrorCtx.Name, "mirror", "removed");
                    removed.Add((mirrorIdx, mirrorCtx));
                    model.RemoveAt(mirrorIdx);
                }
                else
                {
                    PLDiag.AttrChange("MirrorDetach", mirrorIdx, mirrorCtx.Name, "mirror", "mesh");
                    mirrorCtx.Type = MeshType.Mesh;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.Mesh;
                    mirrorCtx.BakedMirrorSourceIndex = -1;
                    realCtx.DetachedMirrorObjectId = mirrorCtx.ObjectId;
                }
                touched = true;
            }

            if (realCtx.MirrorType != 0 || realCtx.HasBakedMirrorChild) touched = true;
            realCtx.MirrorType = 0;
            realCtx.HasBakedMirrorChild = false;
            realCtx.InvalidateSymmetryCache();

            return touched;
        }

        /// <summary>ミラーを有効にする。戻り値は変化があったか。</summary>
        private bool EnableMirror(ModelContext model, int realIdx, MeshContext realCtx,
                                  List<(int Index, MeshContext MeshContext)> added)
        {
            // 既にミラー側を持っているなら属性だけ戻す
            var existing = new List<int>();
            MirrorBranchOps.CollectMirrorPeers(model, realIdx, existing);
            if (existing.Count > 0)
            {
                if (realCtx.MirrorType != 0) return false;
                realCtx.MirrorType = 1;
                return true;
            }

            if (realCtx.MirrorAxis == 0) realCtx.MirrorAxis = 1;
            realCtx.MirrorType = 1;

            // 切り離してあった PMX 系ミラーを引き当てる
            int detachedIdx = ObjectIdAllocator.IndexOfId(model.MeshContextList, realCtx.DetachedMirrorObjectId);
            if (detachedIdx >= 0)
            {
                var mirrorCtx = model.GetMeshContext(detachedIdx);
                if (mirrorCtx != null)
                {
                    mirrorCtx.Type = MeshType.MirrorSide;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.MirrorSide;

                    var pair = new MirrorPair
                    {
                        Real   = realCtx,
                        Mirror = mirrorCtx,
                        Axis   = realCtx.GetMirrorSymmetryAxis()
                    };
                    if (pair.Build())
                    {
                        SyncMirrorWeightsIfSkinned(pair, realCtx);
                        model.MirrorPairs.Add(pair);
                        realCtx.DetachedMirrorObjectId = 0;
                        PLDiag.AttrChange("MirrorReattach", detachedIdx, mirrorCtx.Name, "mesh", "mirror");
                        return true;
                    }

                    // 頂点数が合わないなど張れない場合は元へ戻す
                    Debug.LogWarning($"[Mirror] 再ペアに失敗しました real=\"{realCtx.Name}\" mirror=\"{mirrorCtx.Name}\"\n{pair.BuildLog}");
                    mirrorCtx.Type = MeshType.Mesh;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.Mesh;
                    realCtx.MirrorType = 0;
                    return false;
                }
            }

            // 生成ミラーを作る
            var generated = MirrorBranchOps.CreateDerivedMirrorContext(realCtx, realIdx);
            if (generated == null)
            {
                // 頂点を持たないメッシュなど。属性だけ立てて終わる。
                return true;
            }

            generated.Type = MeshType.MirrorSide;
            if (generated.MeshObject != null) generated.MeshObject.Type = MeshType.MirrorSide;

            // 左右対応が付く名前は「左腕 → 右腕」にする。付かない名前だけ従来の
            // 接尾辞（"+"）へ落とす。既に同名が居る場合も接尾辞へ落として衝突を避ける。
            generated.Name = MirrorNameOps.MakeMirrorName(
                realCtx.Name,
                MirrorBranchOps.MirrorBranchSuffix,
                n => ExistsMeshName(model, n));
            if (generated.MeshObject != null) generated.MeshObject.Name = generated.Name;

            int insertAt = realIdx + 1;
            model.Insert(insertAt, generated);

            var genPair = new MirrorPair
            {
                Real   = realCtx,
                Mirror = generated,
                Axis   = realCtx.GetMirrorSymmetryAxis()
            };
            if (genPair.Build())
            {
                SyncMirrorWeightsIfSkinned(genPair, realCtx);
                model.MirrorPairs.Add(genPair);
            }

            added.Add((insertAt, generated));
            PLDiag.AttrChange("MirrorGenerate", insertAt, generated.Name, "none", "mirror");
            return true;
        }

        /// <summary>
        /// MeshContextList の丸ごとスナップショットで Undo を 1 件記録する。
        /// before は操作前に CaptureList で取っておくこと。
        /// </summary>
        private void RecordMeshListSnapshot(
            List<MeshContext> before, ModelContext model, string desc)
        {
            if (_undoController == null || before == null || model == null) return;

            var after  = MeshFilterToSkinnedRecord.CaptureList(model);
            var record = new MeshFilterToSkinnedRecord { BeforeList = before, AfterList = after };

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>
        /// 実体側がスキンドなら、ミラー側メッシュ本体のウェイトを左右対のボーンへ写す。
        ///
        /// 【なぜ Build() だけでは足りないか】
        ///   MirrorBranchOps.BuildMirroredMeshObject は、実体側頂点の MirrorBoneWeight が
        ///   あればそれを、無ければ実体側の BoneWeight をそのままミラー側へ複製する。
        ///   一方 MirrorPair.Build() の中で走る ApplyMirrorBoneWeights が書くのは
        ///   「実体側頂点の MirrorBoneWeight」だけで、ミラーメッシュ本体の BoneWeight は
        ///   触らない。順序として複製が先なので、初回生成時のミラー側は実体側と同じ
        ///   ボーンを指したままになり、右のメッシュが左のボーンで動く。
        ///
        ///   SyncBoneWeights() は BonePairMap を通した値をミラーメッシュ本体へ書く。
        ///   Build() が対応表を作り終えたこの時点で呼ぶ。
        ///
        /// 【対応表が空のとき】
        ///   BonePairMap は MirrorBoneIndex からしか作らない。全ボーンが -1 の
        ///   モデル（PMX インポート直後など）では写像できるスロットが 1 つも無く、
        ///   SyncBoneWeights は何も書かずに終わる。誤ったボーン番号を残すより良い。
        ///   左右対応は ResolveMirrorBoneIndexCommand で先に埋めること。
        /// </summary>
        private static void SyncMirrorWeightsIfSkinned(MirrorPair pair, MeshContext realCtx)
        {
            if (pair == null || realCtx == null) return;
            if (!realCtx.IsSkinned) return;
            pair.SyncBoneWeights();
        }

        /// <summary>モデル内に同名のメッシュが既に居るか（ミラー命名の衝突判定用）。</summary>
        private static bool ExistsMeshName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
                if (string.Equals(model.GetMeshContext(i)?.Name, name, System.StringComparison.Ordinal))
                    return true;

            return false;
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

        // ================================================================
        // モデルブレンド静的ヘルパー
        // PolyLingCore_Commands.cs から移植（private→internalに昇格）
        // ================================================================

        private static void ExecuteBlend(
            ProjectContext project,
            int sourceModelIndex,
            int cloneModelIndex,
            float[] weights,
            bool[] meshEnabled,
            bool recalcNormals,
            bool blendBones,
            Action<MeshContext> onSyncMesh)
        {
            var cloneModel = project.GetModel(cloneModelIndex);
            if (cloneModel == null) return;

            // ウェイト正規化
            float total = 0f;
            foreach (var w in weights) total += w;
            float[] nw = new float[weights.Length];
            if (total > 0f)
                for (int i = 0; i < weights.Length; i++) nw[i] = weights[i] / total;
            else
            {
                float eq = weights.Length > 0 ? 1f / weights.Length : 0f;
                for (int i = 0; i < weights.Length; i++) nw[i] = eq;
            }

            var cloneDrawables = cloneModel.DrawableMeshes;
            var targetEntries  = new System.Collections.Generic.List<(int drawableIdx, TypedMeshEntry entry)>();
            for (int di = 0; di < cloneDrawables.Count; di++)
            {
                var e = cloneDrawables[di];
                if (e.Type == MeshType.MirrorSide || e.Type == MeshType.BakedMirror) continue;
                if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                targetEntries.Add((di, e));
            }

            var targetVertCountRaw      = targetEntries.Select(t => t.entry.MeshObject.VertexCount).ToArray();
            var targetVertCountExpanded = targetEntries.Select(t =>
                t.entry.Context.UnityMesh != null
                    ? t.entry.Context.UnityMesh.vertexCount
                    : t.entry.MeshObject.VertexCount).ToArray();

            var srcFilteredMap  = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<TypedMeshEntry>>();
            var srcExpCountsMap = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
            for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                var m = project.GetModel(modelIdx);
                if (m == null) continue;
                var srcDrawables = m.DrawableMeshes;
                var filtered  = new System.Collections.Generic.List<TypedMeshEntry>();
                var expCounts = new System.Collections.Generic.List<int>();
                for (int di = 0; di < srcDrawables.Count; di++)
                {
                    var e = srcDrawables[di];
                    if (e.Type == MeshType.MirrorSide || e.Type == MeshType.BakedMirror) continue;
                    if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                    filtered.Add(e);
                    int ec = e.Context.UnityMesh != null
                        ? e.Context.UnityMesh.vertexCount
                        : e.MeshObject.VertexCount;
                    expCounts.Add(ec);
                }
                srcFilteredMap[modelIdx]  = filtered;
                srcExpCountsMap[modelIdx] = expCounts;
            }

            var srcCursors = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var key in srcFilteredMap.Keys) srcCursors[key] = 0;

            for (int k = 0; k < targetEntries.Count; k++)
            {
                int drawableIdx = targetEntries[k].drawableIdx;
                if (drawableIdx < meshEnabled.Length && !meshEnabled[drawableIdx]) continue;

                var targetEntry = targetEntries[k].entry;
                var targetMesh  = targetEntry.MeshObject;
                int rawCount    = targetVertCountRaw[k];
                int expCount    = targetVertCountExpanded[k];

                var nonIsolated  = BuildBlendNonIsolatedSet(targetMesh);
                var blended      = new Vector3[rawCount];
                bool targetIsTriangulated = targetMesh.IsTriangulated;

                foreach (var kv in srcFilteredMap)
                {
                    float w = nw[kv.Key];
                    var srcList      = kv.Value;
                    var srcExpCounts = srcExpCountsMap[kv.Key];
                    int cursor       = srcCursors[kv.Key];
                    int matchSi      = -1;
                    for (int si = cursor; si < srcExpCounts.Count; si++)
                    {
                        if (srcExpCounts[si] == expCount) { matchSi = si; break; }
                    }
                    if (matchSi < 0) continue;
                    srcCursors[kv.Key] = matchSi + 1;
                    var srcMesh = srcList[matchSi].MeshObject;
                    bool srcIsTriangulated = srcMesh.IsTriangulated;

                    if (targetIsTriangulated)
                    {
                        var srcInvMap = srcIsTriangulated ? null : srcMesh.BuildInverseExpansionMap();
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsTriangulated)
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            else
                            {
                                if (!srcInvMap.TryGetValue(vi, out var r)) continue;
                                srcPos = srcMesh.Vertices[r.vIdx].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                    else
                    {
                        var srcExpMap = srcIsTriangulated ? targetMesh.BuildExpansionMap() : null;
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsTriangulated)
                            {
                                if (!srcExpMap.TryGetValue((vi, 0), out int srcEi)) continue;
                                if (srcEi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[srcEi].Position;
                            }
                            else
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                }

                for (int vi = 0; vi < rawCount; vi++)
                {
                    if (!nonIsolated.Contains(vi)) continue;
                    targetMesh.Vertices[vi].Position = blended[vi];
                }

                if (recalcNormals)
                    targetMesh.RecalculateSmoothNormals();

                // UnityMesh 更新
                var ctx = targetEntry.Context;
                if (ctx.UnityMesh != null && ctx.MeshObject != null)
                {
                    var wm = ctx.WorldMatrix;
                    if (ctx.MeshObject.VertexCount == ctx.UnityMesh.vertexCount)
                    {
                        var verts = new Vector3[ctx.MeshObject.VertexCount];
                        for (int vi = 0; vi < verts.Length; vi++)
                            verts[vi] = wm.MultiplyPoint3x4(ctx.MeshObject.Vertices[vi].Position);
                        ctx.UnityMesh.vertices = verts;
                        ctx.UnityMesh.RecalculateBounds();
                    }
                }
                onSyncMesh?.Invoke(ctx);
            }

            // ミラー同期
            var syncedReal = new System.Collections.Generic.HashSet<MeshContext>();
            foreach (var pair in cloneModel.MirrorPairs)
            {
                if (!pair.IsValid) continue;
                pair.SyncPositions();
                if (recalcNormals) pair.SyncNormals();
                onSyncMesh?.Invoke(pair.Real);
                onSyncMesh?.Invoke(pair.Mirror);
                syncedReal.Add(pair.Real);
            }
            foreach (var (_, targetEntry) in targetEntries)
            {
                var realCtx = targetEntry.Context;
                if (syncedReal.Contains(realCtx)) continue;
                string mirrorName = realCtx.Name + "+";
                var axis   = realCtx.GetMirrorSymmetryAxis();
                var realMo = realCtx.MeshObject;
                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.MirrorSide || mc.Name != mirrorName) continue;
                    if (mc.MeshObject == null || mc.MeshObject.VertexCount != realMo.VertexCount) continue;
                    for (int vi = 0; vi < realMo.VertexCount; vi++)
                    {
                        var p = realMo.Vertices[vi].Position;
                        mc.MeshObject.Vertices[vi].Position = axis switch
                        {
                            Poly_Ling.Symmetry.SymmetryAxis.X => new Vector3(-p.x, p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Y => new Vector3(p.x, -p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Z => new Vector3(p.x, p.y, -p.z),
                            _ => new Vector3(-p.x, p.y, p.z),
                        };
                    }
                    onSyncMesh?.Invoke(mc);
                    break;
                }
            }

            // ボーンブレンド
            if (blendBones && cloneModel.BoneCount > 0)
            {
                var cloneBoneByName = new System.Collections.Generic.Dictionary<string, MeshContext>();
                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    if (!string.IsNullOrEmpty(mc.Name)) cloneBoneByName[mc.Name] = mc;
                }

                var srcBoneMaps = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, Vector3>>();
                for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
                {
                    if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                    var srcM = project.GetModel(modelIdx);
                    if (srcM == null || srcM.BoneCount == 0) continue;
                    var bmap = new System.Collections.Generic.Dictionary<string, Vector3>();
                    for (int i = 0; i < srcM.MeshContextCount; i++)
                    {
                        var mc = srcM.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        if (!string.IsNullOrEmpty(mc.Name) && mc.BoneTransform != null)
                            bmap[mc.Name] = mc.BoneTransform.Position;
                    }
                    if (bmap.Count > 0) srcBoneMaps[modelIdx] = bmap;
                }

                foreach (var kv in cloneBoneByName)
                {
                    if (kv.Value.BoneTransform == null) continue;
                    Vector3 blendedPos = Vector3.zero;
                    float totalW = 0f;
                    foreach (var srcKv in srcBoneMaps)
                    {
                        if (!srcKv.Value.TryGetValue(kv.Key, out Vector3 srcPos)) continue;
                        float w = nw[srcKv.Key];
                        blendedPos += srcPos * w;
                        totalW     += w;
                    }
                    if (totalW > 0f)
                        kv.Value.BoneTransform.Position = blendedPos / totalW;
                }
                cloneModel.ComputeWorldAndBindPoses();
            }
        }

        private static HashSet<int> BuildBlendNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            return set;
        }

        internal static ModelContext DeepCloneModelContext(ModelContext src, string newName)
        {
            var dst = new ModelContext { Name = newName };

            for (int i = 0; i < src.MeshContextCount; i++)
            {
                var s = src.GetMeshContext(i);
                if (s == null) continue;
                var meshObj = s.MeshObject?.Clone();
                if (meshObj == null) continue;

                var d = new MeshContext
                {
                    Name                   = s.Name,
                    MeshObject             = meshObj,
                    UnityMesh              = meshObj.ToUnityMesh(),
                    OriginalPositions      = (Vector3[])meshObj.Positions.Clone(),
                    BoneTransform          = CloneBoneTransform(s.BoneTransform),
                    ParentIndex            = s.ParentIndex,
                    Depth                  = s.Depth,
                    HierarchyParentIndex   = s.HierarchyParentIndex,
                    IsVisible              = s.IsVisible,
                    IsLocked               = s.IsLocked,
                    IsFolding              = s.IsFolding,
                    MirrorType             = s.MirrorType,
                    MirrorAxis             = s.MirrorAxis,
                    MirrorDistance         = s.MirrorDistance,
                    MirrorMaterialOffset   = s.MirrorMaterialOffset,
                    BakedMirrorSourceIndex = s.BakedMirrorSourceIndex,
                    HasBakedMirrorChild    = s.HasBakedMirrorChild,
                    MirrorGeometryDerived  = s.MirrorGeometryDerived,
                    MorphParentIndex       = s.MorphParentIndex,
                    BindPose               = s.BindPose,
                    BonePoseData           = s.BonePoseData?.Clone(),
                    MorphBaseData          = s.MorphBaseData?.Clone(),
                };
                dst.Add(d);
            }

            if (src.MaterialReferences != null)
                foreach (var m in src.MaterialReferences)
                    dst.MaterialReferences.Add(m);
            dst.CurrentMaterialIndex = src.CurrentMaterialIndex;

            if (src.DefaultMaterialReferences != null)
                foreach (var m in src.DefaultMaterialReferences)
                    dst.DefaultMaterialReferences.Add(m);
            dst.DefaultCurrentMaterialIndex = src.DefaultCurrentMaterialIndex;
            dst.AutoSetDefaultMaterials     = src.AutoSetDefaultMaterials;

            if (src.MirrorPairs != null)
            {
                foreach (var sp in src.MirrorPairs)
                {
                    int ri = src.IndexOf(sp.Real);
                    int mi = src.IndexOf(sp.Mirror);
                    if (ri < 0 || mi < 0 || ri >= dst.Count || mi >= dst.Count) continue;
                    var pair = new MirrorPair
                    {
                        Real   = dst.GetMeshContext(ri),
                        Mirror = dst.GetMeshContext(mi),
                        Axis   = sp.Axis,
                    };
                    if (pair.Build()) dst.MirrorPairs.Add(pair);
                }
            }
            return dst;
        }

        private static BoneTransform CloneBoneTransform(BoneTransform src)
        {
            if (src == null) return new BoneTransform();
            var dst = new BoneTransform();
            dst.CopyFrom(src);
            return dst;
        }

        private ToolContext BuildMinimalToolCtx(ModelContext model)
        {
            var ctx = new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.SyncMeshContextPositionsOnly = mc =>
            {
                // Phase 2a-2g-1: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。
                // project は Dispatch ローカルでクロージャ不可のため、毎回 _getProject() で取得する。
                var proj = _getProject();
                _viewportManager.EnterVerticesMoved(proj, VerticesMovedPhase.Dragging, mc);
                _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            };
            ctx.NotifyTopologyChanged = () =>
            {
                // Phase 2a-2g-1: RebuildAdapter を EnterTopologyChanged に集約。
                _viewportManager.EnterTopologyChanged(_getProject());
                _notifyPanels(ChangeKind.ListStructure);
            };
            return ctx;
        }

        /// <summary>
        /// SkinWeight 一括操作（Flood/Normalize/Prune）用の ToolContext を構築する。
        /// UndoController・CommandQueue・SyncMesh を設定済み。
        /// </summary>
        private ToolContext BuildSkinWeightToolCtx(ModelContext model)
        {
            var ctx            = BuildMinimalToolCtx(model);
            ctx.CommandQueue   = _commandQueue;
            ctx.SyncMesh       = () =>
            {
                // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                _viewportManager.EnterTopologyChanged(_getProject());
            };
            ctx.Repaint        = () => { };
            return ctx;
        }

        /// <summary>
        /// スキンウェイトの一括操作を、選択中の描画オブジェクト全件へ適用する。
        ///
        /// Flood / Normalize / Prune / 数値設定 / 全頂点正規化 の共通経路。
        /// 対象の列挙は SkinWeightOperations.CollectTargetMeshContexts に一本化してあり、
        /// ウェイト可視化（MeshSceneRenderer.CollectWeightVisTargets）と同じ集合になる。
        ///
        /// Undo はメッシュごとに取る。UndoController は一度に 1 メッシュしか保持できないため、
        /// SetMeshObject → before → 適用 → after → 記録 をメッシュごとに繰り返す
        /// （SetFaceHiddenCommand と同型）。
        ///
        /// 頂点数・面構成は変わらないので、同期は RebuildAdapter を伴う
        /// EnterTopologyChanged ではなくウェイトの部分転送のみを行う
        /// EnterVertexAttributesChanged を通す。
        /// </summary>
        /// <param name="apply">1 メッシュへ適用し、書き換えた頂点数を返す関数</param>
        private void ApplySkinWeightPerMesh(
            ProjectContext project, ModelContext model, string undoLabel,
            System.Func<MeshContext, int> apply)
        {
            if (model == null || apply == null) return;

            var targets = SkinWeightOperations.CollectTargetMeshContexts(model);
            if (targets.Count == 0) return;

            var changedMeshes = new List<MeshContext>();
            var mirrorMeshes  = new List<MeshContext>();

            foreach (var mc in targets)
            {
                if (mc?.MeshObject == null) continue;

                if (_undoController != null)
                {
                    // SetMeshObjectFor を使うこと。SetMeshObject(MeshObject,…) は
                    // 書き込み先が MeshUndoContext.ResolvedMeshContext（既定で先頭の
                    // 選択メッシュ）になるため、このループの2件目以降で先頭メッシュの
                    // MeshObject が今の対象のもので上書きされる。
                    _undoController.MeshUndoContext.ParentModelContext = model;
                    _undoController.SetMeshObjectFor(mc, mc.UnityMesh);
                }
                var before = _undoController?.CaptureMeshObjectSnapshot();

                int changed = apply(mc);
                if (changed <= 0) continue;

                changedMeshes.Add(mc);

                // ミラー側へ写す。ミラー側メッシュはファイル実体を持つ独立メッシュで
                // 自分の BoneWeight を保存するため、実体側を塗っただけでは更新されない。
                // SyncBoneWeights は実体側頂点の MirrorBoneWeight（GPU 描画用）も
                // 張り直すので、実体側の after を取る前に済ませる。後に回すと
                // Redo で MirrorBoneWeight が古い値に戻る。
                SyncSkinWeightToMirrors(model, mc, mirrorMeshes, undoLabel);

                if (_undoController != null && before != null)
                {
                    // ミラー側の記録で対象が移っているので戻す。
                    _undoController.SetMeshObjectFor(mc, mc.UnityMesh);
                    var after = _undoController.CaptureMeshObjectSnapshot();
                    _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _undoController, before, after, undoLabel));
                }
            }
            _undoController?.ClearTargetMeshContext();

            if (changedMeshes.Count == 0) return;

            foreach (var mc in mirrorMeshes)
                changedMeshes.Add(mc);

            foreach (var mc in changedMeshes)
                _viewportManager.EnterVertexAttributesChanged(
                    project, mc, weights: true, uvs: false);

            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>
        /// 書き換えたメッシュ 1 件のウェイトを、ペアの相方へ写す。
        /// 実体側を塗ったらミラー側へ、ミラー側を塗ったら実体側へ写す。
        /// 写した相方を collected へ足し、相方のぶんの Undo も積む。
        /// 呼び出し側は、戻った直後に元のメッシュへ SetMeshObjectFor し直すこと。
        /// </summary>
        private void SyncSkinWeightToMirrors(
            ModelContext model, MeshContext changedCtx,
            List<MeshContext> collected, string undoLabel)
        {
            if (model?.MirrorPairs == null || changedCtx == null || collected == null) return;

            foreach (var pair in model.MirrorPairs)
            {
                if (pair?.Real == null || pair.Mirror == null) continue;
                if (pair.Real.MeshObject == null || pair.Mirror.MeshObject == null) continue;

                // 塗ったのが実体側なら相方はミラー側、塗ったのがミラー側なら相方は実体側。
                // ミラー側も選択して直接塗れるので、両方向を扱う。
                bool fromReal   = ReferenceEquals(pair.Real, changedCtx);
                bool fromMirror = ReferenceEquals(pair.Mirror, changedCtx);
                if (!fromReal && !fromMirror) continue;

                var peer = fromReal ? pair.Mirror : pair.Real;
                if (collected.Contains(peer)) continue;

                MeshObjectSnapshot before = null;
                if (_undoController != null)
                {
                    _undoController.MeshUndoContext.ParentModelContext = model;
                    _undoController.SetMeshObjectFor(peer, peer.UnityMesh);
                    before = _undoController.CaptureMeshObjectSnapshot();
                }

                if (fromReal) pair.SyncBoneWeights();
                else          pair.SyncBoneWeightsFromMirror();

                collected.Add(peer);

                if (_undoController != null && before != null)
                {
                    var after = _undoController.CaptureMeshObjectSnapshot();
                    _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _undoController, before, after, undoLabel + " (mirror)"));
                }
            }
        }

        // ================================================================
        // スカルプト ブラシ ヘルパー（SculptStrokeCommand 用）
        // ================================================================

        // ================================================================
        // 詳細選択 トポロジー ヘルパー（AdvancedSelectCommand 用）
        // ================================================================

        // ── Connected ────────────────────────────────────────────────

        // ── Belt ─────────────────────────────────────────────────────

        // ── EdgeLoop ─────────────────────────────────────────────────

        // ── ShortestPath (Dijkstra) ───────────────────────────────────

        // ── 共通ユーティリティ ────────────────────────────────────────

        // SelectionHelper の BuildEdgeAdjacency は ToolContext を取るが
        // MeshObject のみから辺隣接を構築するオーバーロードを作成
        private static Dictionary<VertexPair, HashSet<VertexPair>> SelectionHelperBuildEdgeAdj(MeshObject mo)
        {
            // 辺→共有する面の辺隣接を構築（SelectionHelper.BuildEdgeAdjacency の MeshObject 版）
            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            var result      = new Dictionary<VertexPair, HashSet<VertexPair>>();

            foreach (var kv in edgeToFaces)
            {
                if (!result.ContainsKey(kv.Key)) result[kv.Key] = new HashSet<VertexPair>();
                foreach (int fi in kv.Value)
                {
                    var vs = mo.Faces[fi].VertexIndices;
                    int n  = vs.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var e = new VertexPair(vs[i], vs[(i + 1) % n]);
                        if (e != kv.Key)
                        {
                            result[kv.Key].Add(e);
                            if (!result.ContainsKey(e)) result[e] = new HashSet<VertexPair>();
                            result[e].Add(kv.Key);
                        }
                    }
                }
            }
            return result;
        }

        // ================================================================
        // 差分からのモーフ生成 ヘルパー
        // ================================================================

        private static int CreateMorphMeshContextInDispatcher(
            ModelContext baseModel, MeshContext baseCtx, int parentIdx,
            MeshObject morphMeshObj, string morphName, int panel,
            MorphExpression expression)
        {
            var morphObj      = baseCtx.MeshObject.Clone();
            morphObj.Type     = MeshType.Morph;
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
                morphObj.Vertices[vi].Position = morphMeshObj.Vertices[vi].Position;

            var newCtx = new MeshContext
            {
                // モーフ実体の名前はシステムが決める（PMXImporter と同じ「{親名}_{モーフ名}」）。
                // ユーザーが管理する名前は MorphExpression.Name ひとつだけにする。
                Name       = $"{baseCtx.Name}_{morphName}",
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, baseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = parentIdx;

            // モーフは親のミラー機構に乗る（規約は MorphMirrorPolicy.cs を正典とする）
            newCtx.InheritMirrorSettingsFrom(baseCtx);

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
            return newIdx;
        }

        private static void CreateMirrorMorphMeshContextInDispatcher(
            ModelContext baseModel, MirrorPair pair, int mirrorParentIdx, int realMorphIdx,
            MeshObject realBaseObj, MeshObject realMorphObj,
            string morphName, int panel, MorphExpression expression)
        {
            var mirrorBaseCtx = pair.Mirror;
            if (mirrorBaseCtx?.MeshObject == null) return;

            var morphObj  = mirrorBaseCtx.MeshObject.Clone();
            morphObj.Type = MeshType.Morph;
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
            {
                int ri = pair.VertexMap != null && vi < pair.VertexMap.Length
                    ? pair.VertexMap[vi] : vi;
                if (ri < 0 || ri >= realBaseObj.VertexCount) continue;
                var realDiff   = realMorphObj.Vertices[ri].Position - realBaseObj.Vertices[ri].Position;
                var mirrorDiff = pair.MirrorDirection(realDiff);
                morphObj.Vertices[vi].Position =
                    mirrorBaseCtx.MeshObject.Vertices[vi].Position + mirrorDiff;
            }

            var newCtx = new MeshContext
            {
                // モーフ実体の名前はシステムが決める（「{親名}_{モーフ名}」）。
                // ミラー側は親名が違うので、Real 側モーフと自然に別名になる。
                Name       = $"{mirrorBaseCtx.Name}_{morphName}",
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, mirrorBaseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = mirrorParentIdx;

            // モーフは親のミラー機構に乗る（規約は MorphMirrorPolicy.cs を正典とする）
            newCtx.InheritMirrorSettingsFrom(mirrorBaseCtx);

            // 親が生成ミラー（実体側から作られた形状）なら、モーフ側にも同じ連結を張る。
            // これで既存の MirrorBranchOps.RebakeDerivedMirrorVertices が
            // Real 側モーフ → Mirror 側モーフ の追随を担当できる。
            if (mirrorBaseCtx.MirrorGeometryDerived && realMorphIdx >= 0)
            {
                newCtx.MirrorGeometryDerived  = true;
                newCtx.BakedMirrorSourceIndex = realMorphIdx;
            }

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
        }

        // ================================================================
        // パーツ選択辞書ヘルパー
        // ================================================================

        /// <summary>
        /// パーツ選択辞書を現在の選択へ適用する。
        /// </summary>
        /// <remarks>
        /// 【単一メッシュ前提 — 変更時の注意】
        ///
        /// 対象は model.ActiveMeshContext の Selection のみ。
        /// SelectionChangeRecord の復元先も ActiveMeshContext 固定なので整合する。
        ///
        /// 将来これを複数メッシュへ広げる場合、Undo 記録も
        /// MultiMeshSelectionChangeRecord へ移すこと。
        /// 記録側だけ複数メッシュ化すると Undo が先頭メッシュしか戻さなくなる。
        /// </remarks>
        private void PartsSetApply(ModelContext model, int setIndex, bool additive, bool subtract)
        {
            if (model == null) return;
            var mc   = model.ActiveMeshContext;
            var sets = mc?.PartsSelectionSetList;
            if (sets == null || setIndex < 0 || setIndex >= sets.Count) return;
            var sel = mc.Selection;
            if (sel == null) return;

            // Undo 用：適用前スナップショット
            SelectionSnapshot oldSnap = sel.CreateSnapshot();

            var set = sets[setIndex];
            SelectionSnapshot newSnap;
            if (additive)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.UnionWith(set.Vertices);
                snap.Edges.UnionWith(set.Edges);
                snap.Faces.UnionWith(set.Faces);
                snap.Lines.UnionWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else if (subtract)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.ExceptWith(set.Vertices);
                snap.Edges.ExceptWith(set.Edges);
                snap.Faces.ExceptWith(set.Faces);
                snap.Lines.ExceptWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else
            {
                newSnap = new SelectionSnapshot
                {
                    Mode     = set.Mode,
                    Vertices = new HashSet<int>(set.Vertices),
                    Edges    = new HashSet<VertexPair>(set.Edges),
                    Faces    = new HashSet<int>(set.Faces),
                    Lines    = new HashSet<int>(set.Lines),
                };
                sel.RestoreFromSnapshot(newSnap);
            }

            // Undo 記録（VertexEditStack の SelectionChangeRecord）
            if (_undoController != null)
            {
                var record = new SelectionChangeRecord(oldSnap, newSnap);
                {
                    string __dbgDesc = "パーツ選択辞書 適用";
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    _undoController.VertexEditStack.Record(record, __dbgDesc);
                }
                _undoController.FocusVertexEdit();
            }

            _selectionOps?.SetSelectionState(sel);
            _renderer?.SetSelectionState(sel);
            _notifyPanels(ChangeKind.Selection);
        }

        // ================================================================
        // 法線再計算 除外辞書ヘルパー
        // ================================================================

        /// <summary>除外辞書内で重複しない名前を返す。</summary>
        private static string GenerateUniqueNormalExcludeName(MeshObject meshObject, string baseName)
        {
            var list = meshObject?.NormalRecalcExcludeList;
            if (list == null) return baseName;

            var used = new HashSet<string>();
            foreach (var set in list)
                if (set != null) used.Add(set.Name);

            if (!used.Contains(baseName)) return baseName;

            int suffix = 1;
            string name;
            do
            {
                name = baseName + "_" + suffix;
                suffix++;
            } while (used.Contains(name));
            return name;
        }

        /// <summary>
        /// 除外辞書エントリを現在の選択へ適用する（置き換え）。
        /// 対象は model.ActiveMeshContext の Selection のみ（PartsSetApply と同じ前提）。
        /// </summary>
        private void NormalExcludeSetApply(ModelContext model, int setIndex)
        {
            if (model == null) return;
            var mc   = model.ActiveMeshContext;
            var list = mc?.MeshObject?.NormalRecalcExcludeList;
            if (list == null || setIndex < 0 || setIndex >= list.Count) return;
            var sel = mc.Selection;
            if (sel == null) return;

            SelectionSnapshot oldSnap = sel.CreateSnapshot();

            var set = list[setIndex];
            var newSnap = new SelectionSnapshot
            {
                Mode     = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges    = new HashSet<VertexPair>(set.Edges),
                Faces    = new HashSet<int>(set.Faces),
                Lines    = new HashSet<int>(set.Lines),
            };
            sel.RestoreFromSnapshot(newSnap);

            if (_undoController != null)
            {
                _undoController.VertexEditStack.Record(
                    new SelectionChangeRecord(oldSnap, newSnap), "法線再計算 除外辞書 適用");
                _undoController.FocusVertexEdit();
            }

            _selectionOps?.SetSelectionState(sel);
            _renderer?.SetSelectionState(sel);
            _notifyPanels(ChangeKind.Selection);
        }

        // ================================================================
        // オブジェクト原点の一括設定
        // ================================================================

        /// <summary>
        /// 名前一致したメッシュの原点を設定する。
        /// 「原点だけ移動」と同じく、自頂点を再局所化して見た目を保つ。子は動かさない。
        /// </summary>
        /// <summary>
        /// 選択中メッシュのローカル拡大縮小を頂点位置へ畳み込み、Scale を (1,1,1) に戻す。
        ///
        /// ベイクできない対象はスキップし、その理由を message に列挙して返す。
        ///   - UseLocalTransform が false: LocalMatrix が identity で Scale が効いていない
        ///   - Scale が (1,1,1): 変化なし
        ///   - MeshType.Bone: 頂点を持たない
        ///   - 子を持つ: 子の world は「親World × 子Local」で決まるため、親のスケールを
        ///     外すと子がずれる。非一様スケール×子の回転がある場合は子側の TRS で補正できない
        ///   - スキンドメッシュ: 描画が SkinningMatrix 経由で自身の WorldMatrix を使わないため、
        ///     ベイクすると見た目が変わる
        /// </summary>
        /// <returns>1件でもベイクしたら true。</returns>
        public bool BakeObjectScale(ModelContext model, out string message)
        {
            message = "";
            if (model == null) { message = "拡大縮小をベイク: モデルがありません"; return false; }

            var selected = model.SelectedDrawableMeshIndices;
            if (selected == null || selected.Count == 0)
            {
                message = "拡大縮小をベイク: 対象が選択されていません";
                return false;
            }

            // HierarchyParentIndex で子の有無を判定する（ComputeWorldMatrices が参照する親）。
            var hasChild = new HashSet<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var c = model.GetMeshContext(i);
                if (c == null) continue;
                int hp = c.HierarchyParentIndex;
                if (hp >= 0) hasChild.Add(hp);
            }

            var targets     = new List<int>();
            var skipNoLocal = new List<string>();
            var skipUnit    = new List<string>();
            var skipBone    = new List<string>();
            var skipChild   = new List<string>();
            var skipSkin    = new List<string>();

            foreach (int idx in selected)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                string nm = string.IsNullOrEmpty(mc.Name) ? $"#{idx}" : mc.Name;

                if (mc.Type == MeshType.Bone)                        { skipBone.Add(nm);    continue; }
                if (mc.BoneTransform == null ||
                    !mc.BoneTransform.UseLocalTransform)             { skipNoLocal.Add(nm); continue; }
                if (mc.BoneTransform.Scale == Vector3.one)           { skipUnit.Add(nm);    continue; }
                if (hasChild.Contains(idx))                          { skipChild.Add(nm);   continue; }

                // 種別（SkinnedMesh 系か）で弾く。実頂点のウェイト有無ではない。
                if (mc.IsSkinned) { skipSkin.Add(nm); continue; }

                targets.Add(idx);
            }

            var before = new Dictionary<int, ObjectScaleSnapshot>();
            var after  = new Dictionary<int, ObjectScaleSnapshot>();

            foreach (int idx in targets)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc.MeshObject;

                var oldVerts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) oldVerts[v] = mo.Vertices[v].Position;
                before[idx] = new ObjectScaleSnapshot
                {
                    Scale           = mc.BoneTransform.Scale,
                    VertexPositions = oldVerts,
                };

                Vector3 sc = mc.BoneTransform.Scale;
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = Vector3.Scale(vert.Position, sc);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();

                mc.BoneTransform.Scale = Vector3.one;

                var newVerts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) newVerts[v] = mo.Vertices[v].Position;
                after[idx] = new ObjectScaleSnapshot
                {
                    Scale           = Vector3.one,
                    VertexPositions = newVerts,
                };
            }

            // 警告メッセージ組み立て（スキップ理由ごとに件数と名前）
            var warn = new List<string>();
            void AddWarn(string reason, List<string> names)
            {
                if (names.Count == 0) return;
                warn.Add($"{reason}{names.Count}件({string.Join(",", names)})");
            }
            AddWarn("ローカル変換無効:", skipNoLocal);
            AddWarn("等倍:",             skipUnit);
            AddWarn("ボーン:",           skipBone);
            AddWarn("子あり:",           skipChild);
            AddWarn("スキン済み:",       skipSkin);

            if (targets.Count == 0)
            {
                message = "拡大縮小をベイク: 適用0件" +
                          (warn.Count > 0 ? " / スキップ " + string.Join(" ", warn) : "");
                if (warn.Count > 0) Debug.LogWarning("[BakeObjectScale] " + message);
                return false;
            }

            model.ComputeWorldMatrices();

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectScaleBakeRecord(before, after, "拡大縮小をベイク"), "拡大縮小をベイク");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            message = $"拡大縮小をベイク: 適用{targets.Count}件" +
                      (warn.Count > 0 ? " / スキップ " + string.Join(" ", warn) : "");
            if (warn.Count > 0) Debug.LogWarning("[BakeObjectScale] " + message);
            return true;
        }

        private void ApplyObjectOrigins(ModelContext model, ApplyObjectOriginsCommand cmd)
        {
            if (cmd?.Names == null || cmd.Positions == null) return;

            // 診断（既定は無効）。検証パネルが実行中だけ立てる。
            Poly_Ling.Diagnostics.ObjectOriginDiag.Begin("ApplyObjectOrigins");
            if (Poly_Ling.Diagnostics.ObjectOriginDiag.Enabled)
            {
                // 実値の階層をそのまま控える。MQO の depth からの推定ではなく、
                // 適用時点で ComputeWorldMatrices が使う HierarchyParentIndex を記録する。
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null) continue;

                    var e = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(i);
                    e.Name                 = mc.Name ?? "";
                    e.Type                 = mc.Type.ToString();
                    e.VertexCount          = mc.MeshObject?.VertexCount ?? 0;
                    e.HierarchyParentIndex = mc.HierarchyParentIndex;

                    int p = mc.HierarchyParentIndex;
                    int guard = 0;
                    while (p >= 0 && p < model.MeshContextCount && guard < 256)
                    {
                        e.Ancestors.Add(p);
                        p = model.GetMeshContext(p)?.HierarchyParentIndex ?? -1;
                        guard++;
                    }
                }
            }

            // 名前 → インデックス（重複名は先着）
            // 姿勢くさびは書出側で除外しているので、読込側でも適用先にしない。
            // 既存の（くさび行を含む）CSV を読んでも巻き込まないようにする。
            var wedgeIndices = ObjectPoseWedgeReader.CollectWedgeIndices(model);

            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type == MeshType.Bone) continue;
                // ミラー側は実体側と BoneTransform を共有するので適用先にしない
                // （別の原点を持たせると v_M = S·v_R が崩れる）
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                if (wedgeIndices.Contains(i)) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!indexByName.ContainsKey(mc.Name)) indexByName[mc.Name] = i;
            }

            // 適用対象を決める。
            // CSV に載っていないオブジェクトは targets に入らないので触らない。
            // 逆にモデルに無い名前も黙って飛ばす（どちらもエラーにしない）。
            var targets = new List<(int index, Vector3 pos, Vector3? rot)>();
            var missing = new List<string>();

            int n = Mathf.Min(cmd.Names.Length, cmd.Positions.Length);
            for (int i = 0; i < n; i++)
            {
                string name = cmd.Names[i];
                if (string.IsNullOrEmpty(name)) continue;

                // 回転は任意。配列が無い / 行に指定が無い場合は元の回転を保つ。
                Vector3? rot = (cmd.Rotations != null && i < cmd.Rotations.Length)
                    ? cmd.Rotations[i]
                    : null;

                if (indexByName.TryGetValue(name, out int idx))
                {
                    targets.Add((idx, cmd.Positions[i], rot));

                    var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                    if (de != null)
                    {
                        de.InCsv       = true;
                        de.CsvPosition = cmd.Positions[i];
                        de.IsTarget    = true;
                    }
                }
                else missing.Add(name);
            }

            if (missing.Count > 0)
                Debug.Log($"[ObjectOrigin] モデルに存在しない名前を無視: {missing.Count} 件 " +
                          $"({string.Join(", ", missing.GetRange(0, Mathf.Min(5, missing.Count)))} …)");

            if (targets.Count == 0)
            {
                Debug.LogWarning("[ObjectOrigin] 適用対象がありません。");
                return;
            }

            // 再局所化の対象を決める。
            //
            // 姿勢を書き換えるのは CSV に名前があった行（targets）だけだが、
            // 見た目を保つ対象はそれでは足りない。CSV に無いオブジェクトは
            // 姿勢こそ変わらないが、祖先の原点が入ればワールド行列が変わり
            // （ModelContext.ComputeWorldMatrices は 親のワールド × 自身のローカル）、
            // 頂点を補正しないと祖先の原点の合計ぶん飛ぶ。
            // 「CSV に無いものは動かさない」を満たすには全メッシュを対象にする。
            // 姿勢くさび取込（ImportObjectPoseWedges）と同じ範囲にそろえる。
            //
            // 除外は適用先（indexByName）と同じ規則にして targets ⊆ relocalize を保証する。
            // ミラー側は後段の RebakeDerivedMirrorVertices が実体側から作り直す。
            var relocalize = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                relocalize.Add(i);

                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(i);
                if (de != null) de.InRelocalize = true;
            }

            // 変更前スナップショット + 現在の頂点ワールド位置
            // 回転も動かし得るので、位置だけの ObjectOriginUndoRecord ではなく
            // 回転込みの ObjectPoseUndoRecord に記録する（回転を変えない場合も
            // 変更前後の実値をそのまま入れるため挙動は変わらない）。
            var before      = new Dictionary<int, ObjectPoseSnapshot>();
            var startWorld  = new Dictionary<int, Vector3[]>();
            var startMatrix = new Dictionary<int, Matrix4x4>();

            model.ComputeWorldMatrices();

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                var world = new Vector3[mo.Vertices.Count];
                var wm    = mc.WorldMatrix;

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    verts[v] = mo.Vertices[v].Position;
                    world[v] = wm.MultiplyPoint3x4(verts[v]);
                }

                before[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
                startWorld[idx]  = world;
                startMatrix[idx] = wm;

                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                if (de != null)
                {
                    de.HasStartWorld  = true;
                    de.WorldBefore    = wm;
                    de.PosBefore      = mc.BoneTransform?.Position ?? Vector3.zero;
                    de.UseLocalBefore = mc.BoneTransform?.UseLocalTransform ?? false;
                }
            }

            // 原点（と、指定があれば回転）を設定
            foreach (var (idx, pos, rot) in targets)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                mc.BoneTransform.Position          = pos;
                if (rot.HasValue) mc.BoneTransform.Rotation = rot.Value;
                mc.BoneTransform.UseLocalTransform = true;
            }

            model.ComputeWorldMatrices();

            // 自頂点を再局所化して見た目を保つ。
            // ワールド行列が変わっていないものは触らない（丸め誤差を持ち込まないため）。
            var changed = new HashSet<int>();
            int relocalizedCount = 0;

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);

                if (de != null && mc != null)
                {
                    de.WorldAfter    = mc.WorldMatrix;
                    de.PosAfter      = mc.BoneTransform?.Position ?? Vector3.zero;
                    de.UseLocalAfter = mc.BoneTransform?.UseLocalTransform ?? false;
                }

                if (mo == null || !startWorld.TryGetValue(idx, out var world)) continue;
                if (startMatrix.TryGetValue(idx, out var wm0) && wm0 == mc.WorldMatrix)
                {
                    if (de != null) de.SkippedByMatrixCompare = true;
                    continue;
                }

                Matrix4x4 inv = mc.WorldMatrixInverse;
                int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                for (int v = 0; v < cnt; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = inv.MultiplyPoint3x4(world[v]);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();

                if (de != null) de.Relocalized = true;

                changed.Add(idx);
                relocalizedCount++;
            }

            // 診断: 適用前後で頂点のワールド位置がどれだけ動いたかを測る。
            // 再局所化まで終えた状態で測るので、0 でなければ補正が効いていない。
            if (Poly_Ling.Diagnostics.ObjectOriginDiag.Enabled)
            {
                model.ComputeWorldMatrices();

                foreach (int idx in relocalize)
                {
                    var mc = model.GetMeshContext(idx);
                    var mo = mc?.MeshObject;
                    var de = Poly_Ling.Diagnostics.ObjectOriginDiag.Get(idx);
                    if (mo == null || de == null) continue;
                    if (!startWorld.TryGetValue(idx, out var world)) continue;

                    Matrix4x4 wm = mc.WorldMatrix;
                    de.WorldAfter = wm;

                    int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                    for (int v = 0; v < cnt; v++)
                    {
                        Vector3 now = wm.MultiplyPoint3x4(mo.Vertices[v].Position);
                        Vector3 d   = now - world[v];
                        float   mag = d.magnitude;
                        if (mag > de.MaxWorldDelta)
                        {
                            de.MaxWorldDelta   = mag;
                            de.WorldDeltaOfMax = d;
                        }
                    }
                }
            }

            // 頂点が動かなくても姿勢を書き換えたものは Undo の対象に含める。
            foreach (var (idx, _, _) in targets) changed.Add(idx);

            // 実体側のローカル頂点が変わったので、生成ミラーを作り直して
            // v_M = S·v_R を保つ（実効ワールド S·H·S の前提）。
            MirrorBranchOps.RebakeDerivedMirrorVertices(model.MeshContextList);

            // ②派生ミラー実体のミラー側モーフも Real 側から作り直す
            // （規約は MorphMirrorPolicy.cs を正典とする）。
            model.SyncAllMirrorMorphs();

            // 書き換えたローカル頂点を描画側へ送る。
            //
            // ここを省くと、GPU には新しいワールド行列だけが届き、頂点位置は
            // 再局所化前のまま残る。結果、原点を入れた祖先を持つオブジェクトが
            // 「原点の合計」ぶんずれて描かれる。MeshObject の値は正しいので
            // 検査では 0 と出る一方、画面だけが飛ぶ。
            //
            // 生成ミラーは RebakeDerivedMirrorVertices が同じことを済ませている
            // （MirrorBranchOps: OriginalPositions 更新 + ApplyVertexPositionsToMesh）。
            // 実体側にだけ無かったのをそろえる。
            foreach (int idx in changed)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();
                mc.ApplyVertexPositionsToMesh();
                _viewportManager?.SyncMeshPositionsAndTransform(mc, model);
            }

            // 変更後スナップショット。実際に変わったものだけ記録する。
            var after       = new Dictionary<int, ObjectPoseSnapshot>();
            var beforeSaved = new Dictionary<int, ObjectPoseSnapshot>();

            foreach (int idx in changed)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (!before.TryGetValue(idx, out var beforeSnap)) continue;

                var verts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) verts[v] = mo.Vertices[v].Position;

                beforeSaved[idx] = beforeSnap;
                after[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
            }

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectPoseUndoRecord(beforeSaved, after, "原点の読み込み"), "原点の読み込み");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            Debug.Log($"[ObjectOrigin] 原点を適用: {targets.Count} 件" +
                      $"（見た目維持のため頂点を補正: {relocalizedCount} 件 / 対象 {relocalize.Count} 件）");
        }

        // ================================================================
        // 姿勢くさび（オブジェクト姿勢の可視化オブジェクト）
        // ================================================================

        /// <summary>
        /// メッシュオブジェクトの姿勢をくさびオブジェクト列としてモデル末尾へ生成する。
        /// 生成そのものは ObjectPoseWedgeGenerator、挿入は ObjectPoseWedgeInserter が持つ。
        /// ここは Undo 記録とビュー更新だけを担う。
        /// </summary>
        private void GenerateObjectPoseWedges(
            ProjectContext project, ModelContext model, GenerateObjectPoseWedgesCommand cmd)
        {
            float length = cmd.WedgeLength > 0f
                ? cmd.WedgeLength
                : ObjectPoseWedgeGenerator.DefaultWedgeLength;

            var pieces = ObjectPoseWedgeGenerator.Generate(model, length);
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 対象のメッシュオブジェクトがありません。");
                return;
            }

            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectPoseWedgeInserter.Insert(model, pieces, cmd.ContainerName);
            if (added.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 生成できませんでした。");
                return;
            }

            // 選択はコンテナだけにする（取り込み時にそのまま対象として使えるように）。
            model.ClearMeshSelection();
            model.AddToMeshSelection(added[0].Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();

            _viewportManager.EnterSceneReset(project);
            _viewportManager.EnterCameraChanged(
                _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            _rebuildModelList();
            _notifyPanels(ChangeKind.ListStructure);

            int wedgeCount = 0;
            foreach (var p in pieces) if (p != null && p.HasWedge) wedgeCount++;
            Debug.Log($"[ObjectPose] 姿勢くさびを生成: {added[0].MeshContext.Name} / " +
                      $"くさび {wedgeCount} 件・空のオブジェクト {pieces.Count - wedgeCount} 件");
        }

        /// <summary>
        /// くさびオブジェクト列を読み、名前一致でメッシュオブジェクトの姿勢へ戻す。
        /// 見た目は保つ（原点CSV読込と同じく、自頂点をワールド基準で再局所化する）。
        /// </summary>
        private void ApplyObjectPoseWedges(ModelContext model, ApplyObjectPoseWedgesCommand cmd)
        {
            // ── コンテナの決定 ───────────────────────────────────────
            // 選択 → 名前 → 中身（くさびを最も多く持つノード）の順に見る。
            // 選択が的外れでも自動検出に落ちるので、無関係なものを選んだまま
            // 押しても取り込める。
            int containerIndex = ObjectPoseWedgeReader.ResolveContainer(
                model, cmd.ContainerMasterIndex, cmd.ContainerName, out string reason);

            Debug.Log($"[ObjectPose] コンテナ判定: {reason}");

            if (containerIndex < 0)
            {
                Debug.LogWarning("[ObjectPose] くさびのコンテナが見つかりません。" +
                                 "先に「姿勢くさび生成」で作るか、くさびを含むモデルを読み込んでください。");
                return;
            }

            var subtree = ObjectPoseWedgeReader.CollectSubtree(model, containerIndex);
            var entries = ObjectPoseWedgeReader.Read(model, containerIndex);
            if (entries.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 読み取れるくさびがありません: " +
                                 (model.GetMeshContext(containerIndex)?.Name ?? "?"));
                return;
            }

            // ── 適用先を名前で引く（コンテナ配下は除外）─────────────
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (subtree.Contains(i)) continue;
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Mesh) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!indexByName.ContainsKey(mc.Name)) indexByName[mc.Name] = i;
            }

            var targets = new List<(int Index, ObjectPoseEntry Entry)>();
            var missing = new List<string>();
            foreach (var e in entries)
            {
                // e.Name は くさび名から "_bone" を外した元メッシュ名。
                if (indexByName.TryGetValue(e.Name, out int idx)) targets.Add((idx, e));
                else missing.Add(e.WedgeName ?? e.Name);
            }

            if (missing.Count > 0)
                Debug.Log($"[ObjectPose] 適用先が見つからないくさびを無視: {missing.Count} 件 " +
                          $"({string.Join(", ", missing.GetRange(0, Mathf.Min(5, missing.Count)))} …)");

            if (targets.Count == 0)
            {
                Debug.LogWarning("[ObjectPose] 適用対象がありません。");
                return;
            }

            // ── 再局所化の対象を決める ───────────────────────────────
            // 姿勢を書き換えるのはくさびを持つオブジェクトだけだが、見た目を保つ
            // 対象はそれでは足りない。くさびを持たないオブジェクト（＝生成時に
            // ローカル姿勢が単位だったもの）は姿勢こそ変わらないが、祖先が動けば
            // 一緒に動く。頂点を補正しないとそれらが四散する。
            // 原点CSV読込が全行を対象にするのと同じ範囲にそろえる。
            var relocalize = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (subtree.Contains(i)) continue;              // くさび自身は除く
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type != MeshType.Mesh) continue;         // ミラー側は後で実体側から作り直す
                relocalize.Add(i);
            }

            // ── 変更前スナップショット + 現在の頂点ワールド位置 ──────
            var before     = new Dictionary<int, ObjectPoseSnapshot>();
            var startWorld = new Dictionary<int, Vector3[]>();

            model.ComputeWorldMatrices();

            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                var world = new Vector3[mo.Vertices.Count];
                var wm    = mc.WorldMatrix;

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    verts[v] = mo.Vertices[v].Position;
                    world[v] = wm.MultiplyPoint3x4(verts[v]);
                }

                before[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
                startWorld[idx] = world;
            }

            // ── 姿勢を適用（親から順に）─────────────────────────────
            // 親のローカル姿勢が変わると子のワールド行列も変わる。子のローカルは
            // 「更新後の親のワールド」を基準に出す必要があるので、浅い方から処理する。
            targets.Sort((a, b) =>
                MeshFilterToSkinnedConverter.CalculateDepth(a.Index, model)
                    .CompareTo(MeshFilterToSkinnedConverter.CalculateDepth(b.Index, model)));

            foreach (var (idx, entry) in targets)
            {
                model.ComputeWorldMatrices();

                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                int p = mc.HierarchyParentIndex;
                Matrix4x4 parentWorld = (p >= 0 && p < model.MeshContextCount)
                    ? (model.GetMeshContext(p)?.WorldMatrix ?? Matrix4x4.identity)
                    : Matrix4x4.identity;

                Matrix4x4 local = parentWorld.inverse *
                    Matrix4x4.TRS(entry.WorldPosition, entry.WorldRotation, Vector3.one);

                mc.BoneTransform.Position          = ObjectPoseWedgeShape.PositionOf(local);
                mc.BoneTransform.Rotation          = ObjectPoseWedgeShape.RotationOf(local).eulerAngles;
                mc.BoneTransform.UseLocalTransform = true;
            }

            model.ComputeWorldMatrices();

            // ── 自頂点を再局所化して見た目を保つ ─────────────────────
            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null || !startWorld.TryGetValue(idx, out var world)) continue;

                Matrix4x4 inv = mc.WorldMatrixInverse;
                int cnt = Mathf.Min(mo.Vertices.Count, world.Length);
                for (int v = 0; v < cnt; v++)
                {
                    var vert = mo.Vertices[v];
                    vert.Position = inv.MultiplyPoint3x4(world[v]);
                    mo.Vertices[v] = vert;
                }
                mo.InvalidatePositionCache();
            }

            // 実体側のローカル頂点が変わったので、生成ミラーを作り直して
            // v_M = S·v_R を保つ（実効ワールド S·H·S の前提）。
            MirrorBranchOps.RebakeDerivedMirrorVertices(model.MeshContextList);

            // ②派生ミラー実体のミラー側モーフも Real 側から作り直す
            // （規約は MorphMirrorPolicy.cs を正典とする）。
            model.SyncAllMirrorMorphs();

            // ── 変更後スナップショット ───────────────────────────────
            var after = new Dictionary<int, ObjectPoseSnapshot>();
            foreach (int idx in relocalize)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var verts = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++) verts[v] = mo.Vertices[v].Position;

                after[idx] = new ObjectPoseSnapshot
                {
                    Position          = mc.BoneTransform?.Position ?? Vector3.zero,
                    Rotation          = mc.BoneTransform?.Rotation ?? Vector3.zero,
                    UseLocalTransform = mc.BoneTransform?.UseLocalTransform ?? false,
                    VertexPositions   = verts,
                };
            }

            if (_undoController != null)
            {
                _undoController.SetModelContext(model);
                _undoController.MeshListStack.Record(
                    new ObjectPoseUndoRecord(before, after, "姿勢くさびの取り込み"), "姿勢くさびの取り込み");
                _undoController.FocusMeshList();
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            _viewportManager.EnterTopologyChanged(_getProject());
            _notifyPanels(ChangeKind.Attributes);

            Debug.Log($"[ObjectPose] 姿勢を適用: {targets.Count} 件 / " +
                      $"見た目を保つため再局所化: {relocalize.Count} 件");
        }

        // ================================================================
        // 共通ヘルパー
        // ================================================================

        /// <summary>選択中の描画メッシュを列挙する。未選択時は編集対象メッシュ単体。</summary>
        // ================================================================
        // ミラー実体化 / 解除の後処理
        // ================================================================
        // 頂点数が変わるので Unity Mesh を作り直す必要がある。
        // RebuildAdapter は ctx.UnityMesh を作らないため、ここで明示的に差し替える。
        private static void SyncMeshContextAfterMirrorEdit(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return;

            mc.OriginalPositions = new Vector3[mo.VertexCount];
            for (int i = 0; i < mo.VertexCount; i++)
                mc.OriginalPositions[i] = mo.Vertices[i].Position;

            var newMesh = mo.ToUnityMesh();
            newMesh.name      = mo.Name;
            newMesh.hideFlags = HideFlags.HideAndDontSave;
            mc.ReplaceUnityMesh(newMesh);

            mc.InvalidateSymmetryCache();
        }

        // ================================================================
        // 面の非表示フラグ操作
        // ================================================================
        // HideSelected / HideUnselected は面選択が必須（面選択が無ければ何もしない）。
        // メッシュ丸ごとの非表示は既存のオブジェクト可視性で行う。
        // 隠した面は選択から外す（選択が残ると移動系ツールが動かしてしまうため）。
        private static int ApplyFaceHidden(MeshContext mc, SetFaceHiddenCommand.Mode mode)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            var sel = mc.Selection;
            int changed = 0;

            switch (mode)
            {
                case SetFaceHiddenCommand.Mode.HideSelected:
                {
                    if (sel == null || sel.Faces.Count == 0) return 0;
                    foreach (int fi in sel.Faces)
                    {
                        if (fi < 0 || fi >= mo.FaceCount) continue;
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3 || face.IsHidden) continue;
                        face.SetFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.HideUnselected:
                {
                    if (sel == null || sel.Faces.Count == 0) return 0;
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3 || face.IsHidden) continue;
                        if (sel.Faces.Contains(fi)) continue;
                        face.SetFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.ShowAll:
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (!face.IsHidden) continue;
                        face.ClearFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.InvertHidden:
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3) continue;
                        face.ToggleFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }
            }

            if (changed > 0 && sel != null && sel.Faces.Count > 0)
            {
                var stillHidden = new List<int>();
                foreach (int fi in sel.Faces)
                {
                    if (fi >= 0 && fi < mo.FaceCount && mo.Faces[fi].IsHidden)
                        stillHidden.Add(fi);
                }
                foreach (int fi in stillHidden)
                    sel.DeselectFace(fi);
            }

            return changed;
        }

        // ================================================================
        // 法線編集の実行
        // ================================================================
        // 対象範囲は NormalEditOps.CollectTargetCorners のルールに従う
        //   面選択がある → その面のコーナー / 頂点選択のみ → その頂点の全スロット
        //   選択が無い   → メッシュ全体
        // RecalcByAngle だけはスロットを作り直すのでメッシュ全体が対象。
        private static int ApplyNormalEdit(MeshContext mc, NormalEditCommand c)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            if (c.Operation == NormalEditCommand.Op.RecalcByAngle)
            {
                NormalEditOps.RecalcByAngle(mo, c.AngleDeg, c.WeightMode);
                return mo.FaceCount;
            }

            var sel = mc.Selection;
            var corners = NormalEditOps.CollectTargetCorners(
                mo, sel?.Faces, sel?.Vertices);
            if (corners.Count == 0) return 0;

            switch (c.Operation)
            {
                case NormalEditCommand.Op.SetFromFaces:
                    return NormalEditOps.SetFromFaces(mo, corners);

                // 面法線だけを平均して1本にする。スロット数は変わらないため
                // slotCountMayChange には含めない。
                case NormalEditCommand.Op.AverageFromFaces:
                    return NormalEditOps.AverageFromFaces(mo, corners, c.WeightMode);

                case NormalEditCommand.Op.Unify:
                    return NormalEditOps.Unify(mo, corners, c.WeightMode);

                case NormalEditCommand.Op.Break:
                    return NormalEditOps.Break(mo, corners);

                case NormalEditCommand.Op.AverageAll:
                    return NormalEditOps.AverageAll(mo, corners);

                case NormalEditCommand.Op.Smooth:
                    return NormalEditOps.Smooth(mo, corners, c.Strength);

                case NormalEditCommand.Op.Sphereize:
                {
                    Vector3 center = c.UseSelectionCenter
                        ? NormalEditOps.CenterOf(mo, corners)
                        : c.Target;
                    return NormalEditOps.Sphereize(mo, corners, center);
                }

                case NormalEditCommand.Op.PointToTarget:
                    return NormalEditOps.PointToTarget(mo, corners, c.Target, c.AlignVectors);

                case NormalEditCommand.Op.AlignToAxis:
                {
                    Vector3 dir = c.Axis switch
                    {
                        0 => Vector3.right,
                        1 => Vector3.up,
                        _ => Vector3.forward,
                    };
                    if (c.Negative) dir = -dir;
                    return NormalEditOps.SetDirection(mo, corners, dir);
                }

                case NormalEditCommand.Op.FlattenOnAxis:
                    return NormalEditOps.FlattenOnAxis(mo, corners, c.Axis);

                // ミラー対応（X軸対称）。中央近傍の頂点だけ法線の X をゼロにする。
                // スロット数は変わらないため slotCountMayChange には含めない。
                case NormalEditCommand.Op.MirrorFlattenSeamX:
                    return NormalEditOps.FlattenMirrorSeamX(mo, corners, c.MirrorThreshold);

                case NormalEditCommand.Op.Flip:
                    return NormalEditOps.Flip(mo, corners);

                default:
                    return 0;
            }
        }

        private static List<MeshContext> CollectSelectedMeshContexts(ModelContext model)
        {
            var list = new List<MeshContext>();
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc != null) list.Add(mc);
            }
            if (list.Count == 0)
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) list.Add(mc);
            }
            return list;
        }

    }
}
