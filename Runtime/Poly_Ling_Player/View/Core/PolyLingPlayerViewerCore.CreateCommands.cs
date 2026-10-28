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
using Poly_Ling.UndoSystem;

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
            _commandDispatcher.OnMatchEdgeChainCount = ExecuteMatchEdgeChainCount;
            _commandDispatcher.OnResetProject        = ExecuteResetProject;
            _commandDispatcher.OnCreateEmptyModel    = ExecuteCreateEmptyModel;
            _commandDispatcher.OnAddModelBundle      = ExecuteAddModelBundle;
            _commandDispatcher.OnReplaceModelBundle  = ExecuteReplaceModelBundle;
            _commandDispatcher.OnEnsureProjectForCreate = EnsureProjectForCreate;
            _commandDispatcher.OnCreateObjectArray   = ExecuteCreateObjectArray;
            _commandDispatcher.OnSplitObjectByPartsId = ExecuteSplitObjectByPartsId;
            _commandDispatcher.OnSplitObjectByConnectivity = ExecuteSplitObjectByConnectivity;
            _commandDispatcher.OnAdvancedSelect      = ExecuteAdvancedSelect;
            _commandDispatcher.OnSculptStroke        = ExecuteSculptStroke;
            _commandDispatcher.OnMovePivot           = ExecuteMovePivot;
            _commandDispatcher.OnMoveSelectedVertices = ExecuteMoveSelectedVertices;
            _commandDispatcher.OnSelectElements       = ExecuteSelectElements;
            _commandDispatcher.OnAdvancedSelectByAttribute = ExecuteAdvancedSelectByAttribute;
            _commandDispatcher.OnFaceMerge           = ExecuteFaceMerge;
            _commandDispatcher.OnQuad4To1            = ExecuteQuad4To1;
            _commandDispatcher.OnTriangleRelocate    = ExecuteTriangleRelocate;
            _commandDispatcher.OnEdgeTriangle        = ExecuteEdgeTriangle;
            _commandDispatcher.OnTri4To1             = ExecuteTri4To1;
            _commandDispatcher.OnVertexDissolve      = ExecuteVertexDissolve;
            _commandDispatcher.OnSplitVertices       = ExecuteSplitVertices;
            _commandDispatcher.OnVertexHole          = ExecuteVertexHole;
            _commandDispatcher.OnFlipFace            = ExecuteFlipFace;
            _commandDispatcher.OnAlignVertices       = ExecuteAlignVertices;
            _commandDispatcher.OnSmoothEdges         = ExecuteSmoothEdges;
            _commandDispatcher.OnEdgeRibbonFace      = ExecuteEdgeRibbonFace;
            _commandDispatcher.OnCreateVertexBillboardPlace = ExecuteVertexBillboardPlace;
            // ホスト（画面操作）が担当者判定で止められたら状態表示へ出す（操作経路統一計画.md L-5）。
            _commandDispatcher.OnOwnershipDenied = (deniedCmd, deniedActor, reason) =>
            {
                if (deniedActor != null && deniedActor.Kind == CommandActorKind.Host)
                    _status = $"編集できません（{reason}）";
            };
            // ツールの公開層：[PLTool] の付いたハンドラを名前で引く（操作経路統一計画.md B・C・D）。
            _commandDispatcher.ResolveTool = id => ToolRegistry().TryGetValue(id ?? "", out var h) ? h : null;
            _commandDispatcher.ListTools   = () => ToolRegistry().Keys;
            _commandDispatcher.OnImportPmxFile       = ExecuteImportPmxFile;
            _commandDispatcher.OnExportPmxFile       = ExecuteExportPmxFile;
            _commandDispatcher.OnImportMqoFile       = ExecuteImportMqoFile;
            _commandDispatcher.OnSetUnderlay         = ExecuteSetUnderlay;
            _commandDispatcher.OnClearUnderlay       = ExecuteClearUnderlay;
            _commandDispatcher.OnQueryUnderlay       = ExecuteQueryUnderlay;
            _commandDispatcher.OnEditSpaceCommand    = ExecuteEditSpaceCommand;
            _commandDispatcher.OnDetachVertices      = ExecuteDetachVertices;
            _commandDispatcher.OnComputeViewUvs      = ComputeViewUvs;
            _commandDispatcher.OnCameraCommand       = ExecuteCameraCommand;
            _commandDispatcher.OnExportMqoFile       = ExecuteExportMqoFile;
            _commandDispatcher.OnImportObjFile       = ExecuteImportObjFile;
            _commandDispatcher.OnExportObjFile       = ExecuteExportObjFile;
            _commandDispatcher.OnImportStlFile       = ExecuteImportStlFile;
            _commandDispatcher.OnImportStlFolder     = ExecuteImportStlFolder;
            _commandDispatcher.OnExportStlFile       = ExecuteExportStlFile;
            _commandDispatcher.OnExportVrmFile       = ExecuteExportVrmFile;
            _commandDispatcher.OnImportVrmFile       = ExecuteImportVrmFile;
            _commandDispatcher.OnExportGltfFile      = ExecuteExportGltfFile;
            _commandDispatcher.OnImportGltfFile      = ExecuteImportGltfFile;
            _commandDispatcher.OnImportFbxFile       = ExecuteImportFbxFile;
            _commandDispatcher.OnExportFbxFile       = ExecuteExportFbxFile;
            _commandDispatcher.OnSaveProjectFile     = ExecuteSaveProjectFile;
            _commandDispatcher.OnLoadProjectFile     = ExecuteLoadProjectFile;
            _commandDispatcher.OnSaveProjectCsv      = ExecuteSaveProjectCsv;
            _commandDispatcher.OnLoadProjectCsv      = ExecuteLoadProjectCsv;
            _commandDispatcher.OnSaveProjectBinary   = ExecuteSaveProjectBinary;
            _commandDispatcher.OnLoadProjectBinary   = ExecuteLoadProjectBinary;
            _commandDispatcher.OnSendHierarchyBundle = ExecuteSendHierarchyBundle;
            _commandDispatcher.GetPlayerServer       = () => _playerServer;
            _commandDispatcher.GetPyAddonHost        = () => _pyAddonHost;
            _commandDispatcher.GetMotionClipHandler  = () => _motionClipHandler;
            _commandDispatcher.OnPlanarizeAlongBones = ExecutePlanarizeAlongBones;
            _commandDispatcher.OnMergeVertices       = ExecuteMergeVertices;
            _commandDispatcher.OnDeleteSelection     = ExecuteDeleteSelectionCommand;
            _commandDispatcher.OnPipeAlign           = ExecutePipeAlign;
            _commandDispatcher.OnPlaceObjectReshape  = ExecutePlaceObjectReshape;
            _commandDispatcher.OnSolidify            = ExecuteSolidify;
            _commandDispatcher.OnLineExtrude         = ExecuteLineExtrude;
            _commandDispatcher.OnSurfaceSnap         = ExecuteSurfaceSnap;
            _commandDispatcher.OnConvertMotionToVrma = ExecuteConvertMotionToVrma;
            _commandDispatcher.OnExportVmdToMotion   = ExecuteExportVmdToMotion;
            _commandDispatcher.OnExportMotionToBvh   = ExecuteExportMotionToBvh;
            _commandDispatcher.OnExportMotionToFbx   = ExecuteExportMotionToFbx;
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
            _commandDispatcher.OnCreateWorkAxisObject = ExecuteCreateWorkAxisObject;
            _commandDispatcher.OnSetActiveWorkAxis    = ExecuteSetActiveWorkAxis;
            _commandDispatcher.OnRecallWorkAxis      = ExecuteRecallWorkAxis;
            _commandDispatcher.CommandGate           = EditSpaceBlockReason;
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

            // 画像板は画像を 1 回だけ読み、画素数を確定させてから生成する。
            // 読んだテクスチャはそのまま材質へ渡す。
            GeneratedImageMaterial imageMaterial = null;
            if (cmd is CreateImagePlateCommand ipc)
            {
                string why = PrepareImagePlate(ipc, out var prepared, out imageMaterial);
                if (why != null) return why;
                cmd = prepared;
            }

            var mo = PrimitiveMeshFactory.Build(
                cmd, forPreview: false,
                resolvePlaceSources: ResolvePlaceSourcesForCommand,
                resolveBeltSource:   ResolveBeltSourceForCommand);
            if (mo == null)
            {
                if (imageMaterial != null) Object.Destroy(imageMaterial.Texture);
                if (cmd is ExtractPipeBeltCommand)
                    return $"パイプから梯子を取り出せませんでした（{PipeBeltExtractor.LastMessage}）";
                return $"{cmd.ShapeName} を生成できませんでした（フォントが開けない・輪郭が 0 本など）";
            }

            var __sw = System.Diagnostics.Stopwatch.StartNew(); // TEMP-TIMING
            string reason = PlaceGeneratedMesh(
                mo, cmd.MeshName, cmd.Placement, cmd.PoseRotation, cmd.PoseScale,
                out int createdIndex, imageMaterial);
            UnityEngine.Debug.Log($"[PL/Timing] Place {cmd.ShapeName} {__sw.ElapsedMilliseconds}ms"); // TEMP-TIMING

            if (reason == null) ReportCreatedMesh(createdIndex);
            return reason;
        }

        /// <summary>
        /// 画像板の前処理。画像のパスを作業フォルダで解決して読み、
        /// 画素数を画像の値で書き直したコマンドと、材質に貼るテクスチャを返す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private static string PrepareImagePlate(
            CreateImagePlateCommand cmd,
            out CreatePrimitiveMeshCommand prepared, out GeneratedImageMaterial imageMaterial)
        {
            prepared      = null;
            imageMaterial = null;

            var p = cmd.Params;
            if (string.IsNullOrWhiteSpace(p.ImagePath)) return "画像が指定されていません";

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(p.ImagePath, out string fullPath, out string sandboxReason))
                return sandboxReason;

            string why = Poly_Ling.Materials.MaterialEditOps.LoadTextureFile(fullPath, out var tex);
            if (why != null) return why;

            p.ImageWidth  = tex.width;
            p.ImageHeight = tex.height;
            prepared = new CreateImagePlateCommand(cmd.ModelIndex, p, cmd.Placement);

            imageMaterial = new GeneratedImageMaterial
            {
                Texture  = tex,
                FullPath = fullPath,
                Name     = tex.name,
                FaceTag  = ImagePlateMeshGenerator.FrontFaceTag,
            };
            return null;
        }

        /// <summary>
        /// 生成系の前処理。プロジェクトとモデルが無ければ作り、CurrentModel を選んだ状態にする。
        /// PlaceGeneratedMesh の冒頭と同じ処理（EnsureProject → モデル 0 の選択）。
        /// </summary>
        private ProjectContext EnsureProjectForCreate()
        {
            PrepareHandlersForGeneratedMesh();
            var project = ActiveProject;
            if (project != null && project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            return project;
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
            Vector3 poseRotation, Vector3 poseScale, out int createdMasterIndex,
            GeneratedImageMaterial imageMaterial = null)
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
                        placement.IgnorePoseInArmature, placement.MaterialIndex, imageMaterial);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    createdMasterIndex = PrimitiveMeshAddToExisting(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.AddTargetIndex,
                        placement.MaterialIndex, imageMaterial);
                    break;
                case PrimitiveAddMode.NewModel:
                    createdMasterIndex = PrimitiveMeshCreateNewModel(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.MaterialIndex, imageMaterial);
                    break;
                case PrimitiveAddMode.ReplaceExisting:
                    return PrimitiveMeshReplaceExisting(project, meshObject,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.AddTargetIndex, placement.MaterialIndex,
                        out createdMasterIndex, imageMaterial);
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

            DispatchHost(new CreateObjectArrayCommand(
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

        // ================================================================
        // つながりによる分解
        // ================================================================

        /// <summary>直近のつながり分解の結果。パネルが結果表示へ使う。</summary>
        private ConnectivitySplitResult _lastConnectivitySplitResult;

        /// <summary>
        /// 面・線のつながりで描画オブジェクト 1 つを分解し、空のオブジェクトの子として並べる。
        /// DeleteSource なら元を削除し、追加と削除を 1 つの Undo 記録にまとめる。
        /// 切り出しは ConnectivitySplitOps、挿入は PartsIdSplitInserter が持つ。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSplitObjectByConnectivity(SplitObjectByConnectivityCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "モデルがありません";

            int srcIndex = cmd.TargetMasterIndex;
            var srcMc    = model.GetMeshContext(srcIndex);
            if (srcMc?.MeshObject == null)
            {
                _lastConnectivitySplitResult =
                    ConnectivitySplitResult.Fail("対象メッシュが見つかりません");
                return _lastConnectivitySplitResult.Reason;
            }

            // 元を消すとき、子があれば止める。削除で子は祖父へ付け替わるが、
            // その付け替えは Undo の挿し戻しで元に戻らない（ModelContext.RemoveAt）。
            if (cmd.DeleteSource)
            {
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    if (i == srcIndex) continue;
                    var mc = model.GetMeshContext(i);
                    if (mc != null && mc.HierarchyParentIndex == srcIndex)
                    {
                        _lastConnectivitySplitResult = ConnectivitySplitResult.Fail(
                            $"元オブジェクトに子（{mc.Name}）があるため削除できません。"
                          + "元を残す設定で実行してください");
                        return _lastConnectivitySplitResult.Reason;
                    }
                }
            }

            // 元メッシュは書き換えない。
            var result = ConnectivitySplitOps.Split(srcMc.MeshObject, cmd.IsolatedPolicy, out var pieces);
            _lastConnectivitySplitResult = result;
            if (!result.Success) return result.Reason;

            var oldSelected = model.CaptureAllSelectedIndices();

            // 元の姿は挿入前に控える（削除の Undo で挿し戻す）。
            var srcSnapshot = cmd.DeleteSource ? MeshContextSnapshot.Capture(srcMc) : null;

            var added = PartsIdSplitInserter.Insert(
                model, srcIndex, pieces,
                parentSuffix: "_Split",
                childName: (baseName, part) => baseName + "_" + part);
            if (added.Count == 0)
            {
                _lastConnectivitySplitResult = ConnectivitySplitResult.Fail("挿入できませんでした");
                return _lastConnectivitySplitResult.Reason;
            }

            if (cmd.DeleteSource)
            {
                // 子は元のサブツリーの後ろへ入るので、元の索引は挿入で動かない。
                // 削除で後ろの索引が 1 つ詰まり、親子の参照も RemoveAt が付け替える。
                model.RemoveAt(srcIndex);
                for (int i = 0; i < added.Count; i++)
                {
                    int idx = added[i].Index;
                    added[i] = (idx > srcIndex ? idx - 1 : idx, added[i].MeshContext);
                }
                result.SourceDeleted = true;
                _lastConnectivitySplitResult = result;
            }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                if (cmd.DeleteSource)
                {
                    // 追加と削除を 1 つの記録にする。
                    //   Undo: 追加分を（削除後の索引で）抜き、元を元の索引へ挿し戻す。
                    //   Redo: 元を抜き、追加分を削除後の索引へ挿す。
                    // 索引は MeshListChangeRecord.Undo / Redo の順序に合わせてある。
                    var record = new MeshListChangeRecord
                    {
                        RemovedMeshContexts = new List<(int, MeshContextSnapshot)>
                        {
                            (srcIndex, srcSnapshot)
                        },
                        AddedMeshContexts = added.ConvertAll(
                            e => (e.Index, MeshContextSnapshot.Capture(e.MeshContext))),
                        OldSelectedIndices = oldSelected,
                        NewSelectedIndices = newSelected,
                    };
                    _editOps.UndoController.RecordMeshListChange(
                        record, $"Split By Connectivity: {srcMc.Name}");
                }
                else
                {
                    _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
                }
            }

            PrimitiveMeshFinalize(model);

            ReportMeshes(model, added.ConvertAll(e => e.Index));

            Debug.Log($"[ConnectivitySplit] 分解: \"{srcMc.Name}\" {result.Summary}");
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
