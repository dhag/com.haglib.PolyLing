// PolyLingPlayerViewerCore.CreateCommands.cs
// 生成系コマンドの受け口。ディスパッチャから委譲される。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// このファイルは配線（WireCreateCommandHandlers）と図形生成・穴つなぎ・辺群ブリッジ・歪み複製・
// パーツIDによる分解を持つ。ファイル入出力は CreateCommands.FileIO.cs、
// 位相・頂点編集は CreateCommands.Topology.cs。
//
// 【なぜ Viewer 側に置くか】
//   追加先の解決・Undo 記録・再構築・オーバーレイ更新は Viewer の状態に触れる。
//   ディスパッチャへ移すと Viewer の内部を抱えることになるので、
//   コマンドの受け付けだけをディスパッチャが行い、実行はここが持つ。
//
// 【既存の実処理は動かさない】
//   PrimitiveMeshCreateNewObject / PrimitiveMeshAddToExisting / PrimitiveMeshCreateNewModel、
//   ExecuteBridge の各分岐、ExecuteEdgeBridge、ExecuteDeleteSelection、ExecuteObjectArray は
//   そのまま呼ぶ。コマンド化で経路が 1 本になるだけで、中身の挙動は変えない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // 配線
        // ================================================================

        /// <summary>生成系コマンドの受け口をディスパッチャへ繋ぐ。</summary>
        private void WireCreateCommandHandlers()
        {
            if (_commandDispatcher == null) return;

            _commandDispatcher.OnCreatePrimitiveMesh = ExecuteCreatePrimitiveMesh;
            _commandDispatcher.OnAddGeneratedMesh    = ExecuteAddGeneratedMesh;
            _commandDispatcher.OnCreateHoleBridge    = ExecuteCreateHoleBridge;
            _commandDispatcher.OnCreateEdgeBridge    = ExecuteCreateEdgeBridge;
            _commandDispatcher.OnDeleteFaces         = ExecuteDeleteFaces;
            _commandDispatcher.OnMatchHoleRingCount  = ExecuteMatchHoleRingCount;
            _commandDispatcher.OnResetProject        = ExecuteResetProject;
            _commandDispatcher.OnCreateObjectArray   = ExecuteCreateObjectArray;
            _commandDispatcher.OnSplitObjectByPartsId = ExecuteSplitObjectByPartsId;
            _commandDispatcher.OnAdvancedSelect      = ExecuteAdvancedSelect;
            _commandDispatcher.OnSculptStroke        = ExecuteSculptStroke;
            _commandDispatcher.OnMovePivot           = ExecuteMovePivot;
            _commandDispatcher.OnMoveSelectedVertices = ExecuteMoveSelectedVertices;
            _commandDispatcher.OnSelectElements       = ExecuteSelectElements;
            _commandDispatcher.OnAdvancedSelectByAttribute = ExecuteAdvancedSelectByAttribute;
            _commandDispatcher.OnFaceMerge           = ExecuteFaceMerge;
            _commandDispatcher.OnQuad4To1            = ExecuteQuad4To1;
            _commandDispatcher.OnTri4To1             = ExecuteTri4To1;
            _commandDispatcher.OnVertexDissolve      = ExecuteVertexDissolve;
            _commandDispatcher.OnSplitVertices       = ExecuteSplitVertices;
            _commandDispatcher.OnVertexHole          = ExecuteVertexHole;
            _commandDispatcher.OnFlipFace            = ExecuteFlipFace;
            _commandDispatcher.OnAlignVertices       = ExecuteAlignVertices;
            _commandDispatcher.OnSmoothEdges         = ExecuteSmoothEdges;
            _commandDispatcher.OnEdgeRibbonFace      = ExecuteEdgeRibbonFace;
            _commandDispatcher.OnImportPmxFile       = ExecuteImportPmxFile;
            _commandDispatcher.OnExportPmxFile       = ExecuteExportPmxFile;
            _commandDispatcher.OnImportMqoFile       = ExecuteImportMqoFile;
            _commandDispatcher.OnExportMqoFile       = ExecuteExportMqoFile;
            _commandDispatcher.OnImportObjFile       = ExecuteImportObjFile;
            _commandDispatcher.OnExportObjFile       = ExecuteExportObjFile;
            _commandDispatcher.OnExportVrmFile       = ExecuteExportVrmFile;
            _commandDispatcher.OnImportVrmFile       = ExecuteImportVrmFile;
            _commandDispatcher.OnSaveProjectFile     = ExecuteSaveProjectFile;
            _commandDispatcher.OnLoadProjectFile     = ExecuteLoadProjectFile;
            _commandDispatcher.OnSaveProjectCsv      = ExecuteSaveProjectCsv;
            _commandDispatcher.OnLoadProjectCsv      = ExecuteLoadProjectCsv;
            _commandDispatcher.OnPlanarizeAlongBones = ExecutePlanarizeAlongBones;
            _commandDispatcher.OnMergeVertices       = ExecuteMergeVertices;
            _commandDispatcher.OnDeleteSelection     = ExecuteDeleteSelectionCommand;
            _commandDispatcher.OnPipeAlign           = ExecutePipeAlign;
            _commandDispatcher.OnPlaceObjectReshape  = ExecutePlaceObjectReshape;
            _commandDispatcher.OnSolidify            = ExecuteSolidify;
            _commandDispatcher.OnLineExtrude         = ExecuteLineExtrude;
            _commandDispatcher.OnSurfaceSnap         = ExecuteSurfaceSnap;
            _commandDispatcher.OnExportVrmAnimation  = ExecuteExportVrmAnimation;
            _commandDispatcher.OnConvertUnityClipToVrma = ExecuteConvertUnityClipToVrma;
            _commandDispatcher.OnExportVmdToVrma     = ExecuteExportVmdToVrma;
            _commandDispatcher.OnEdgeBevel           = ExecuteEdgeBevel;
            _commandDispatcher.OnEdgeExtrude         = ExecuteEdgeExtrude;
            _commandDispatcher.OnFaceExtrude         = ExecuteFaceExtrude;
            _commandDispatcher.OnSkinWeightPaint     = ExecuteSkinWeightPaint;
            _commandDispatcher.OnRotateSelection     = ExecuteRotateSelection;
            _commandDispatcher.OnScaleSelection      = ExecuteScaleSelection;
            _commandDispatcher.OnMoveObjects         = ExecuteMoveObjects;
            _commandDispatcher.OnRotateObjects       = ExecuteRotateObjects;
            _commandDispatcher.OnApplyDeform         = ExecuteApplyDeform;
            _commandDispatcher.OnApplyLatticeDeform  = ExecuteApplyLatticeDeform;
            _commandDispatcher.OnEdgeTopologyFlip     = ExecuteEdgeTopologyFlip;
            _commandDispatcher.OnEdgeTopologyDissolve = ExecuteEdgeTopologyDissolve;
            _commandDispatcher.OnEdgeTopologySplit    = ExecuteEdgeTopologySplit;
            _commandDispatcher.OnAddFace              = ExecuteAddFace;
            _commandDispatcher.OnCreatePointDefinedPrimitive = ExecuteCreatePointDefinedPrimitive;
            _commandDispatcher.OnKnifeLadderCut       = ExecuteKnifeLadderCut;
            _commandDispatcher.OnKnifeBeltLoopCut     = ExecuteKnifeBeltLoopCut;
            _commandDispatcher.OnKnifeEraseEdge       = ExecuteKnifeEraseEdge;
            _commandDispatcher.OnKnifeSimpleCut       = ExecuteKnifeSimpleCut;
            _commandDispatcher.OnSetWorkAxis         = ExecuteSetWorkAxis;
            _commandDispatcher.OnRecallWorkAxis      = ExecuteRecallWorkAxis;
            _commandDispatcher.OnUndo                = () => _editOps != null && _editOps.PerformUndo();
            _commandDispatcher.OnRedo                = () => _editOps != null && _editOps.PerformRedo();
        }

        // ================================================================
        // 図形生成
        // ================================================================

        /// <summary>
        /// 図形生成コマンド。ファクトリでメッシュを作り、追加先ごとの処理へ渡す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreatePrimitiveMesh(CreatePrimitiveMeshCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var mo = PrimitiveMeshFactory.Build(
                cmd, forPreview: false,
                resolvePlaceSources: ResolvePlaceSourcesForCommand,
                resolveBeltSource:   ResolveBeltSourceForCommand);
            if (mo == null)
                return $"{cmd.ShapeName} を生成できませんでした（フォントが開けない・輪郭が 0 本など）";

            string reason = PlaceGeneratedMesh(
                mo, cmd.MeshName, cmd.Placement, cmd.PoseRotation, cmd.PoseScale,
                out int createdIndex);

            if (reason == null) ReportCreatedMesh(createdIndex);
            return reason;
        }

        /// <summary>
        /// 出来上がったメッシュをそのまま置くコマンド。
        /// PoseAlreadyBaked のときは姿勢を頂点へ入れ直さないので、
        /// 描画オブジェクトの姿勢へ入れる成分も無い。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteAddGeneratedMesh(AddGeneratedMeshCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (cmd.Mesh == null) return "Mesh が指定されていません";

            Vector3 poseRot = cmd.PoseAlreadyBaked ? Vector3.zero : cmd.Placement.PlaceRotation;
            Vector3 poseScl = cmd.PoseAlreadyBaked ? Vector3.one  : cmd.Placement.PlaceScale;

            string reason = PlaceGeneratedMesh(
                cmd.Mesh, cmd.MeshName, cmd.Placement, poseRot, poseScl,
                out int createdIndex);

            if (reason == null) ReportCreatedMesh(createdIndex);
            return reason;
        }

        /// <summary>
        /// 生成した描画オブジェクトの位置と安定 ID をディスパッチャへ報告する。
        /// 索引が取れなかったとき（AddToExisting / NewModel / ReplaceExisting の経路）は
        /// 何もしない。その場合は識別子なしの成功が返る。
        /// </summary>
        private void ReportCreatedMesh(int masterIndex)
        {
            if (masterIndex < 0 || _commandDispatcher == null) return;

            var mc = ActiveProject?.CurrentModel?.GetMeshContext(masterIndex);

            // ObjectId は 0 が「未割当」。割り当たっていないものは載せない。
            ulong[] ids = (mc != null && mc.ObjectId != 0UL)
                ? new[] { mc.ObjectId }
                : null;

            _commandDispatcher.ReportTargets(new[] { masterIndex }, ids);
        }

        /// <summary>
        /// 追加先モードに従ってモデルへ入れる。Undo と再構築は各分岐が持つ。
        /// 分岐の中身は図形生成パネルから直接呼んでいたときと同じ。
        /// </summary>
        /// <param name="createdMasterIndex">
        /// 生成した描画オブジェクトの位置。NewObject 以外の分岐は -1。
        /// </param>
        /// <returns>失敗理由。成功時は null。</returns>
        private string PlaceGeneratedMesh(
            MeshObject meshObject, string meshName, PrimitivePlacement placement,
            Vector3 poseRotation, Vector3 poseScale, out int createdMasterIndex)
        {
            createdMasterIndex = -1;

            PrepareHandlersForGeneratedMesh();

            var project = ActiveProject;
            if (project == null) return "プロジェクトを用意できませんでした";
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            ApplySelectMode();

            switch (placement.AddMode)
            {
                case PrimitiveAddMode.NewObject:
                    createdMasterIndex = PrimitiveMeshCreateNewObject(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.MaterialIndex);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    createdMasterIndex = PrimitiveMeshAddToExisting(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.AddTargetIndex,
                        placement.MaterialIndex);
                    break;
                case PrimitiveAddMode.NewModel:
                    createdMasterIndex = PrimitiveMeshCreateNewModel(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.MaterialIndex);
                    break;
                case PrimitiveAddMode.ReplaceExisting:
                    return PrimitiveMeshReplaceExisting(project, meshObject,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.AddTargetIndex, placement.MaterialIndex,
                        out createdMasterIndex);
            }

            return null;
        }

        /// <summary>
        /// 藤壺（配置）の配置元を索引から解決する。
        /// 展開・重複排除・面なしの除外は MeshSourceMultiPick.Resolve が持つ。
        /// </summary>
        private List<MeshObject> ResolvePlaceSourcesForCommand(int[] masterIndices, bool includeChildren)
            => MeshSourceMultiPick.Resolve(
                masterIndices, includeChildren, BuildSubtreeMeshList,
                idx => ActiveProject?.CurrentModel?.GetMeshContext(idx)?.MeshObject);

        /// <summary>
        /// 梯子の取り込み元を索引から引く。はしごのウェイトを引き継ぐときだけ呼ばれる。
        ///
        /// 【なぜコマンド経路にも要るか】
        ///   引き継ぎは取り込み元のメッシュを位置で突き合わせて行う（BeltWeightBinder）。
        ///   解決口を渡さないと引き当てるものが無く、生成物はウェイトを 1 つも持たない。
        ///   オブジェクトグループの作り直しもこの経路を通るので、渡さないと
        ///   作り直すたびに引き継ぎが消え、スキンド化が書いた値ごと上書きされる。
        /// </summary>
        private MeshObject ResolveBeltSourceForCommand(int masterIndex)
            => ActiveProject?.CurrentModel?.GetMeshContext(masterIndex)?.MeshObject;

        // ================================================================
        // 点指定図形
        // ================================================================

        /// <summary>
        /// 点指定図形コマンド。組み立てと編集対象への書き込み・Undo は
        /// PointDefinedToolHandler.ExecuteFromCommand（プレビューと同じ計画を通す）。
        /// 書き込み後のビュー更新は辺群ブリッジと同じ PrimitiveMeshFinalize。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreatePointDefinedPrimitive(CreatePointDefinedPrimitiveCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _pointDefinedHandler;
            if (h == null) return "点指定図形ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            var model = ActiveProject?.CurrentModel;
            if (model != null)
            {
                model.ComputeWorldMatrices();
                PrimitiveMeshFinalize(model);
            }
            return null;
        }

        // ================================================================
        // 穴つなぎ
        // ================================================================

        /// <summary>
        /// 穴つなぎコマンド。種と設定をパネルへ入れてから、既存の生成経路を通す。
        ///
        /// 【なぜパネルを経由するか】
        ///   縁の復元（種頂点 → 境界辺の連結成分）と対応付けは
        ///   PlayerPrimitiveMeshSubPanel.TryBuildBridgePlan にある。
        ///   同じ処理を 2 つ持たないため、パネル状態へ入れてから呼ぶ。
        ///   パネルのボタン経路では、直前に自分が組んだ値がそのまま戻るだけになる。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreateHoleBridge(CreateHoleBridgeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var panel = _primitiveSubPanel;
            if (panel == null) return "図形生成パネルがありません";

            panel.ApplyHoleBridgeCommand(cmd);
            ExecuteBridge(panel);
            return null;
        }

        // ================================================================
        // 辺群ブリッジ
        // ================================================================

        /// <summary>
        /// スカルプトストロークコマンド。点列をハンドラへ入れ、
        /// マウスと同じブラシ処理を通す。変形アルゴリズムは SculptTool に一本化してある。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSculptStroke(Poly_Ling.Data.SculptStrokeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _sculptHandler;
            if (h == null) return "スカルプトハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>
        /// 詳細選択コマンド。種をハンドラへ入れ、クリックと同じモード実装を通す。
        /// 選択アルゴリズムはディスパッチャに持たせず AdvancedSelectTool に一本化してある。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteAdvancedSelect(Poly_Ling.Data.AdvancedSelectCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _advancedSelectHandler;
            if (h == null) return "詳細選択ハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>
        /// 属性選択コマンド。パネルの「実行」ボタンと同じ
        /// スナップショット → ExecuteAttributeSelect → Undo 記録を通す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteAdvancedSelectByAttribute(
            Poly_Ling.Data.AdvancedSelectByAttributeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _advancedSelectHandler;
            if (h == null) return "詳細選択ハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>
        /// 原点移動コマンド。対象と移動量をハンドラへ入れ、ドラッグ確定と同じ
        /// ObjectMoveTool(OriginOnly) の経路を通す。
        ///
        /// ほかの Execute* と違い失敗理由を戻り値で返す。ディスパッチャが Fail() に
        /// 載せてリモート応答へ返すため（P1-3）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteMovePivot(Poly_Ling.Data.MovePivotCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _pivotOffsetHandler;
            if (h == null) return "原点移動ハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>
        /// 選択頂点の移動コマンド。対象と移動量をハンドラへ入れ、数値入力・ドラッグ確定と
        /// 同じ UpdateAffectedVertices → BeginMove → ApplyDelta → EndMove を通す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteMoveSelectedVertices(Poly_Ling.Data.MoveSelectedVerticesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _moveToolHandler;
            if (h == null) return "移動ハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>
        /// 要素選択コマンド。要素の集合をハンドラへ入れ、クリックと同じ
        /// スナップショット → 書き換え → 頂点展開 → Undo 記録を通す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSelectElements(Poly_Ling.Data.SelectElementsCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _moveToolHandler;
            if (h == null) return "移動ハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        // ================================================================
        // 歪み複製
        // ================================================================

        /// <summary>
        /// 状態表示に使う歪み複製サブパネル。
        /// このパネルは図形生成パネルが持っていて Viewer は参照を持たないので、
        /// 送信時に受け取ったものを控えておく。コマンド経由（自動検証・MCP）で
        /// 実行されたときは null のままで、状態表示だけが出ない。
        /// </summary>
        private PlayerObjectArraySubPanel _objectArraySubPanel;

        /// <summary>
        /// 歪み複製のサブパネルから送るコマンドを組む。
        /// 作業軸はモデル側の状態なので載せず、実行時に解決する。
        /// </summary>
        private void SendObjectArrayCommand(PlayerObjectArraySubPanel panel)
        {
            if (panel == null) return;
            _objectArraySubPanel = panel;

            _commandDispatcher?.Dispatch(new CreateObjectArrayCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                panel.Params,
                panel.SelectedMasterIndices().ToArray(),
                panel.Deformer?.Name ?? ""));
        }

        /// <summary>
        /// 歪み複製コマンド。パネルの状態ではなくコマンドの内容で実行する。
        /// 生成と挿入の中身は ExecuteObjectArrayCore が持つ（パネル経路と同じ）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreateObjectArray(CreateObjectArrayCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            // Deformer は DeformerName から毎回起こす算出プロパティ。
            // 名前が解決できないと null が返るので、ここで弾く。
            var deformer = cmd.Deformer;
            if (deformer == null)
                return $"歪み {cmd.DeformerName} がありません";

            ExecuteObjectArrayCore(cmd.Params, cmd.SourceMasterIndices, deformer);
            return null;
        }

        // ================================================================
        // パーツIDによる分解
        // ================================================================

        /// <summary>
        /// 直近の分解結果。パネルが結果表示へ使う。
        /// 失敗のときも理由を持たせて残す。
        /// </summary>
        private PartsIdSplitResult _lastPartsIdSplitResult;

        /// <summary>
        /// パーツIDで描画オブジェクト 1 つを分解し、空のオブジェクトの子として並べる。
        /// 切り出しは PartsIdSplitOps、挿入は PartsIdSplitInserter が持つ。
        /// ここは Undo 記録とビュー再構築だけを受け持つ。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSplitObjectByPartsId(SplitObjectByPartsIdCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            var srcMc = model.GetMeshContext(cmd.TargetMasterIndex);
            if (srcMc?.MeshObject == null)
            {
                _lastPartsIdSplitResult =
                    PartsIdSplitResult.Fail("対象メッシュが見つかりません");
                return _lastPartsIdSplitResult.Reason;
            }

            // 元メッシュは書き換えないので、Undo は追加ぶんだけを記録すればよい。
            var result = PartsIdSplitOps.Split(srcMc.MeshObject, out var pieces);
            _lastPartsIdSplitResult = result;
            if (!result.Success) return result.Reason;

            var oldSelected = model.CaptureAllSelectedIndices();

            var added = PartsIdSplitInserter.Insert(model, cmd.TargetMasterIndex, pieces);
            if (added.Count == 0)
            {
                _lastPartsIdSplitResult = PartsIdSplitResult.Fail("挿入できませんでした");
                return _lastPartsIdSplitResult.Reason;
            }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);

            // 分解して足した描画オブジェクトの位置と安定 ID を返す。
            ReportMeshes(model, added.ConvertAll(e => e.Index));

            Debug.Log($"[PartsId] 分解: \"{srcMc.Name}\" {result.Summary}");
            return null;
        }

        /// <summary>
        /// 複数の描画オブジェクトを成功結果として報告する。
        /// ObjectId は 0（未割当）のものがあれば objectIds を丸ごと省く
        /// （並びが masterIndices と 1 対 1 でなくなるため）。
        /// </summary>
        private void ReportMeshes(ModelContext model, List<int> masterIndices)
        {
            if (_commandDispatcher == null || model == null) return;
            if (masterIndices == null || masterIndices.Count == 0) return;

            var ids = new ulong[masterIndices.Count];
            bool allAssigned = true;

            for (int i = 0; i < masterIndices.Count; i++)
            {
                var mc = model.GetMeshContext(masterIndices[i]);
                ids[i] = mc?.ObjectId ?? 0UL;
                if (ids[i] == 0UL) allAssigned = false;
            }

            _commandDispatcher.ReportTargets(masterIndices.ToArray(), allAssigned ? ids : null);
        }
    }
}
