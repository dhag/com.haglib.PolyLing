// PolyLingPlayerViewerCore.CreateCommands.cs
// 生成系コマンドの受け口。ディスパッチャから委譲される。
// Runtime/Poly_Ling_Player/View/Core/ に配置
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
        }

        // ================================================================
        // 図形生成
        // ================================================================

        /// <summary>
        /// 図形生成コマンド。ファクトリでメッシュを作り、追加先ごとの処理へ渡す。
        /// 生成できなかった（フォントが開けない・輪郭が 0 本など）ときは何もしない。
        /// </summary>
        private void ExecuteCreatePrimitiveMesh(CreatePrimitiveMeshCommand cmd)
        {
            if (cmd == null) return;

            var mo = PrimitiveMeshFactory.Build(cmd, forPreview: false, resolvePlaceSources: ResolvePlaceSourcesForCommand);
            if (mo == null) return;

            PlaceGeneratedMesh(mo, cmd.MeshName, cmd.Placement, cmd.PoseRotation, cmd.PoseScale);
        }

        /// <summary>
        /// 出来上がったメッシュをそのまま置くコマンド。
        /// PoseAlreadyBaked のときは姿勢を頂点へ入れ直さないので、
        /// 描画オブジェクトの姿勢へ入れる成分も無い。
        /// </summary>
        private void ExecuteAddGeneratedMesh(AddGeneratedMeshCommand cmd)
        {
            if (cmd?.Mesh == null) return;

            Vector3 poseRot = cmd.PoseAlreadyBaked ? Vector3.zero : cmd.Placement.PlaceRotation;
            Vector3 poseScl = cmd.PoseAlreadyBaked ? Vector3.one  : cmd.Placement.PlaceScale;

            PlaceGeneratedMesh(cmd.Mesh, cmd.MeshName, cmd.Placement, poseRot, poseScl);
        }

        /// <summary>
        /// 追加先モードに従ってモデルへ入れる。Undo と再構築は各分岐が持つ。
        /// 分岐の中身は図形生成パネルから直接呼んでいたときと同じ。
        /// </summary>
        private void PlaceGeneratedMesh(
            MeshObject meshObject, string meshName, PrimitivePlacement placement,
            Vector3 poseRotation, Vector3 poseScale)
        {
            PrepareHandlersForGeneratedMesh();

            var project = ActiveProject;
            if (project == null) return;
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            ApplySelectMode();

            switch (placement.AddMode)
            {
                case PrimitiveAddMode.NewObject:
                    PrimitiveMeshCreateNewObject(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.MaterialIndex);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    PrimitiveMeshAddToExisting(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.AddTargetIndex,
                        placement.MaterialIndex);
                    break;
                case PrimitiveAddMode.NewModel:
                    PrimitiveMeshCreateNewModel(project, meshObject, meshName,
                        placement.WorldPosition, poseRotation, poseScale,
                        placement.IgnorePoseInArmature, placement.MaterialIndex);
                    break;
            }
        }

        /// <summary>
        /// 藤壺（配置）の配置元を索引から解決する。
        /// 展開・重複排除・面なしの除外は MeshSourceMultiPick.Resolve が持つ。
        /// </summary>
        private List<MeshObject> ResolvePlaceSourcesForCommand(int[] masterIndices, bool includeChildren)
            => MeshSourceMultiPick.Resolve(
                masterIndices, includeChildren, BuildSubtreeMeshList,
                idx => ActiveProject?.CurrentModel?.GetMeshContext(idx)?.MeshObject);

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
        private void ExecuteCreateHoleBridge(CreateHoleBridgeCommand cmd)
        {
            if (cmd == null) return;

            var panel = _primitiveSubPanel;
            if (panel == null) return;

            panel.ApplyHoleBridgeCommand(cmd);
            ExecuteBridge(panel);
        }

        // ================================================================
        // 辺群ブリッジ
        // ================================================================

        /// <summary>
        /// 辺群ブリッジコマンド。拾いをハンドラへ入れてから、既存の生成経路を通す。
        /// 受理判定（境界辺のみ・同一オブジェクトのみ）は SetPicks が既存の
        /// AcceptEdge へ通すので、クリック経路と同じ規則が効く。
        /// </summary>
        private void ExecuteCreateEdgeBridge(CreateEdgeBridgeCommand cmd)
        {
            if (cmd == null) return;

            var h     = _edgeBridgeHandler;
            var panel = _edgeBridgeSubPanel;
            if (h == null) return;

            h.AutoCorrespondence = cmd.AutoCorrespondence;
            h.FlipCorrespondence = cmd.FlipCorrespondence;
            h.FlipFaces          = cmd.FlipFaces;
            h.Subdivisions       = cmd.Subdivisions;

            if (!h.SetPicks(cmd.MeshIndex, cmd.Edges ?? new VertexPair[0], out string reason))
            {
                panel?.SetStatus(reason);
                return;
            }

            ExecuteEdgeBridge();
        }

        /// <summary>
        /// 辺群ブリッジのサブパネルから送るコマンドを組む。
        /// 拾いはハンドラが持っているので、それをそのまま載せる。
        /// </summary>
        private void SendEdgeBridgeCommand()
        {
            var h = _edgeBridgeHandler;
            if (h == null) return;

            if (h.PickedMeshIndex < 0 || h.PickedEdgeCount == 0)
            {
                _edgeBridgeSubPanel?.SetStatus("辺が拾えていません");
                return;
            }

            // PickedEdges は IReadOnlyCollection なので CopyTo は無い。
            var edges = new List<VertexPair>(h.PickedEdges).ToArray();

            _commandDispatcher?.Dispatch(new CreateEdgeBridgeCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                h.PickedMeshIndex, edges,
                h.AutoCorrespondence, h.FlipCorrespondence, h.FlipFaces, h.Subdivisions));
        }

        // ================================================================
        // 面削除
        // ================================================================

        /// <summary>
        /// 面削除コマンド。指定メッシュの面だけを選択し直してから削除する。
        /// 他のオブジェクトの選択を巻き込まないよう、選択中の全オブジェクトを一度空にする
        /// （面削除モードのクリック処理と同じ手順）。
        /// </summary>
        private void ExecuteDeleteFaces(DeleteFacesCommand cmd)
        {
            if (cmd?.FaceIndices == null || cmd.FaceIndices.Length == 0) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            var target = model.GetMeshContext(cmd.MeshIndex);
            if (target?.Selection == null || target.MeshObject == null) return;

            foreach (int idx in model.SelectedDrawableMeshIndices)
                model.GetMeshContext(idx)?.Selection?.ClearAll();
            target.Selection.ClearAll();

            // 対象を選択オブジェクトリストへ入れる。
            //
            // DeleteSelectionTool.EnumerateTargets は SelectedDrawableMeshIndices に
            // 入っているメッシュだけを走査する（DeleteSelectionTool.cs:111）。
            // Selection に面を入れただけでは対象にならず、何も消えないまま戻る。
            // 面削除モードのクリック経路は、クリックしたメッシュが既に選択リストへ
            // 入っているので表に出なかった。
            model.SelectedDrawableMeshIndices = new List<int> { cmd.MeshIndex };

            // SelectFace(index, additive:false) は先に Faces.Clear() を呼ぶ
            // （SelectionState.cs:176）。ループで false を渡すと毎回リセットされ、
            // 最後の 1 枚しか残らない。2 枚目以降は additive で積む。
            int faceCount = target.MeshObject.FaceCount;
            bool any = false;
            foreach (int f in cmd.FaceIndices)
            {
                if (f < 0 || f >= faceCount) continue;
                target.Selection.SelectFace(f, additive: true);
                any = true;
            }
            if (!any) return;

            ExecuteDeleteSelection();
        }

        // ================================================================
        // 位相の検査（自動検証が結果を確かめるための口）
        // ================================================================

        /// <summary>
        /// メッシュの面数と、境界辺の連結成分（＝穴）ごとの頂点数を返す。
        ///
        /// 段が「送れたか」ではなく「効いたか」を見るために使う。
        /// 面削除・仕切り・穴つなぎは、送信が通っても結果が変わらないことがある。
        /// </summary>
        public static bool InspectTopology(
            ModelContext model, int meshIndex, out int faceCount, out List<int> holeSizes)
        {
            faceCount = 0;
            holeSizes = new List<int>();

            var mo = model?.GetMeshContext(meshIndex)?.MeshObject;
            if (mo == null) return false;

            faceCount = mo.FaceCount;

            var edges = BoundaryEdgeOps.CollectBoundaryEdges(mo);
            if (edges == null || edges.Count == 0) return true;

            var groups = BoundaryEdgeOps.BuildGroups(new HashSet<VertexPair>(edges));
            foreach (var grp in groups)
                holeSizes.Add(BoundaryEdgeOps.VerticesOf(grp).Count);

            holeSizes.Sort();
            return true;
        }

        // ================================================================
        // 穴頂点数合わせ
        // ================================================================

        // ================================================================
        // プロジェクト初期化
        // ================================================================

        /// <summary>
        /// プロジェクトのモデルを全部捨てて 1 つだけ作り直す。
        ///
        /// CsvProjectSerializer.Export はプロジェクト内の全モデルをフォルダへ書く
        /// （CsvProjectSerializer.cs:238-250）。前のモデルが残っていると、
        /// 保存したフォルダに関係ないモデルが同梱されて中身が読めなくなる。
        /// </summary>
        private void ExecuteResetProject(ResetProjectCommand cmd)
        {
            _localLoader.EnsureProject();

            var project = ActiveProject;
            if (project == null) return;

            // 後ろから消す。前から消すと索引がずれる。
            for (int i = project.ModelCount - 1; i >= 0; i--)
                project.RemoveModelAt(i);

            string name = string.IsNullOrEmpty(cmd?.ModelName) ? "Model" : cmd.ModelName;
            var model = project.CreateNewModel(name);
            if (model == null) return;

            EnsureDefaultMaterialSlot(model);

            _viewportManager.EnterSceneReset(project, clearScene: true);
            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
        }

        /// <summary>
        /// 穴点数合わせコマンド。種をハンドラへ入れてから実行する。
        ///
        /// 穴の縁の復元と頂点数の増減は HoleRingCountTool が持つので、
        /// 同じ処理を 2 つ持たないようハンドラ経由で通す。
        /// 種の検証（縁が閉じているか等）は SetSeeds が Tool へ渡して行う。
        /// </summary>
        private void ExecuteMatchHoleRingCount(MatchHoleRingCountCommand cmd)
        {
            if (cmd == null) return;

            var h     = _holeRingCountHandler;
            var panel = _holeRingCountSubPanel;
            if (h == null) return;

            // プロジェクトを配り直す。
            //
            // SetProject はレイアウト構築時に一度呼ばれるだけで、そのときの
            // ActiveProject は null（PolyLingPlayerViewerCore.cs:3455）。
            // PrepareHandlersForGeneratedMesh は他のハンドラへ配り直しているが、
            // このハンドラは入っていない。配らないと Activate で
            // ctx.Model が null になり、Inspect が「モデルがありません」を返して
            // Execute が空振りする。種は選べているのに何も起きない。
            h.SetProject(ActiveProject);
            h.SetUndoController(_editOps?.UndoController);
            h.SetCommandQueue(_editOps?.CommandQueue);

            h.SplitTriangleIntoTriangles = cmd.SplitTriangleIntoTriangles;

            if (!h.SetSeeds(
                    cmd.BaseMeshIndex,   cmd.BaseVertex,   cmd.BaseDirectionHint,
                    cmd.TargetMeshIndex, cmd.TargetVertex, cmd.TargetDirectionHint,
                    out string reason))
            {
                panel?.SetResult(reason);
                return;
            }

            if (!h.Execute(out string message))
                Debug.LogWarning($"[MatchHoleRingCount] 実行できませんでした: {message}");

            panel?.SetResult(message);
        }

        /// <summary>
        /// 穴頂点数合わせのサブパネルから送るコマンドを組む。
        /// 種はハンドラが持っているので、それをそのまま載せる。
        /// </summary>
        private void SendHoleRingCountCommand()
        {
            var h = _holeRingCountHandler;
            if (h == null) return;

            var b = h.BaseSeed;
            var t = h.TargetSeed;
            if (b == null || !b.Valid || t == null || !t.Valid)
            {
                _holeRingCountSubPanel?.SetResult("基準穴と対象穴の両方を取り込んでください");
                return;
            }

            _commandDispatcher?.Dispatch(new MatchHoleRingCountCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                b.MeshIndex, b.Vertex, b.DirectionHint,
                t.MeshIndex, t.Vertex, t.DirectionHint,
                h.SplitTriangleIntoTriangles));
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
                panel.Deformer));
        }

        /// <summary>
        /// 歪み複製コマンド。パネルの状態ではなくコマンドの内容で実行する。
        /// 生成と挿入の中身は ExecuteObjectArrayCore が持つ（パネル経路と同じ）。
        /// </summary>
        private void ExecuteCreateObjectArray(CreateObjectArrayCommand cmd)
        {
            if (cmd == null) return;
            ExecuteObjectArrayCore(cmd.Params, cmd.SourceMasterIndices, cmd.Deformer);
        }
    }
}
