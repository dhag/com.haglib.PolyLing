// PolyLingPlayerViewerCore.CreateCommands.Topology.cs
// 生成系コマンドの受け口：位相・頂点編集・ドラッグ確定・作業軸・面削除・位相の検査・
// 穴頂点数合わせ。配線は PolyLingPlayerViewerCore.CreateCommands.cs。
// Runtime/Poly_Ling_Player/View/Core/ に配置

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
        // 位相編集（パラメータを持たない実行系）
        //
        // どれも「対象の照合 → ハンドラへ委譲」だけを行う。実処理は各 Tool が
        // 正典なので、ここには第 2 実装を置かない（Phase 1 と同じ方針）。
        // 対象の照合はハンドラ側（PlayerCommandTargets）が行う。
        // ================================================================

        /// <summary>面結合（辺指定）コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteFaceMerge(Poly_Ling.Data.FaceMergeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _faceMergeHandler;
            if (h == null) return "面結合ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _faceMergeSubPanel?.Refresh();
            return null;
        }

        /// <summary>四角形 4→1 コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteQuad4To1(Poly_Ling.Data.Quad4To1Command cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _quad4To1Handler;
            if (h == null) return "四角形 4→1 ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _quad4To1SubPanel?.Refresh();
            return null;
        }

        /// <summary>三角形 4→1 コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteTri4To1(Poly_Ling.Data.Tri4To1Command cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _tri4To1Handler;
            if (h == null) return "三角形 4→1 ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _tri4To1SubPanel?.Refresh();
            return null;
        }

        /// <summary>頂点溶かしコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteVertexDissolve(Poly_Ling.Data.VertexDissolveCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _vertexDissolveHandler;
            if (h == null) return "頂点溶かしハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _vertexDissolveSubPanel?.Refresh();
            return null;
        }

        /// <summary>頂点分離コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSplitVertices(Poly_Ling.Data.SplitVerticesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _splitVerticesHandler;
            if (h == null) return "頂点分離ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _splitVerticesSubPanel?.Refresh();
            return null;
        }

        // ================================================================
        // 位相・頂点編集（パラメータを持つ実行系）
        //
        // 4-a-1 と同じく「対象の照合 → ハンドラへ委譲」だけを行う。
        // 設定値の差し替えと復元はハンドラ側が持つ。
        // ================================================================

        /// <summary>頂点に穴あけコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteVertexHole(Poly_Ling.Data.VertexHoleCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _vertexHoleHandler;
            if (h == null) return "頂点に穴あけハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _vertexHoleSubPanel?.Refresh();
            return null;
        }

        /// <summary>面反転コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteFlipFace(Poly_Ling.Data.FlipFaceCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _flipFaceHandler;
            if (h == null) return "面反転ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _flipFaceSubPanel?.Refresh();
            return null;
        }

        /// <summary>頂点整列コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteAlignVertices(Poly_Ling.Data.AlignVerticesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _alignVerticesHandler;
            if (h == null) return "頂点整列ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _alignVerticesSubPanel?.Refresh();
            return null;
        }

        /// <summary>辺の平滑化コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSmoothEdges(Poly_Ling.Data.SmoothEdgesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _smoothEdgesHandler;
            if (h == null) return "辺の平滑化ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _smoothEdgesSubPanel?.Refresh();
            return null;
        }

        /// <summary>
        /// 選択辺から帯面を足すコマンド。
        /// メッシュはハンドラが組み、置き方は他の図形生成と同じ PlaceGeneratedMesh を通す。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeRibbonFace(Poly_Ling.Data.EdgeRibbonFaceCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _edgeRibbonFaceHandler;
            if (h == null) return "辺から帯面ハンドラがありません";

            if (!h.BuildFromCommand(cmd, out var mo, out string reason)) return reason;

            // 頂点はワールド座標。姿勢は持たないので回転・拡大は入れない。
            string placeReason = PlaceGeneratedMesh(
                mo, Poly_Ling.Tools.EdgeRibbonFaceTool.DefaultMeshName,
                cmd.Placement, Vector3.zero, Vector3.one,
                out int createdIndex);

            if (placeReason == null) ReportCreatedMesh(createdIndex);
            return placeReason;
        }

        /// <summary>選択頂点の回転コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteRotateSelection(Poly_Ling.Data.RotateSelectionCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _rotateHandler;
            if (h == null) return "回転ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _rotateSubPanel?.Refresh();
            return null;
        }

        /// <summary>選択頂点のスケールコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteScaleSelection(Poly_Ling.Data.ScaleSelectionCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _scaleHandler;
            if (h == null) return "スケールハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _scaleSubPanel?.Refresh();
            return null;
        }

        /// <summary>変形（デフォーマ）コマンド。派生 6 種をまとめて受ける。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteApplyDeform(Poly_Ling.Data.ApplyDeformCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _deformHandler;
            if (h == null) return "変形ハンドラがありません";

            bool ok = h.ExecuteFromCommand(cmd, out string reason);

            // 受け口はデフォーマ選択とパラメータを元へ戻すので、
            // 表示を実体へ合わせ直す。
            _deformSubPanel?.Refresh();

            return ok ? null : reason;
        }

        /// <summary>ラダー切断コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteKnifeLadderCut(Poly_Ling.Data.KnifeLadderCutCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _knifeHandler;
            if (h == null) return "ナイフハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>一意分割コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteKnifeBeltLoopCut(Poly_Ling.Data.KnifeBeltLoopCutCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _knifeHandler;
            if (h == null) return "ナイフハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>辺消去コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteKnifeEraseEdge(Poly_Ling.Data.KnifeEraseEdgeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _knifeHandler;
            if (h == null) return "ナイフハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>シンプル切断コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteKnifeSimpleCut(Poly_Ling.Data.KnifeSimpleCutCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _knifeHandler;
            if (h == null) return "ナイフハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>辺の入れ替えコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeTopologyFlip(Poly_Ling.Data.EdgeTopologyFlipCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _edgeTopologyHandler;
            if (h == null) return "辺トポロジハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>辺の消去コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeTopologyDissolve(Poly_Ling.Data.EdgeTopologyDissolveCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _edgeTopologyHandler;
            if (h == null) return "辺トポロジハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>四角形の対角分割コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeTopologySplit(Poly_Ling.Data.EdgeTopologySplitCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _edgeTopologyHandler;
            if (h == null) return "辺トポロジハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>面追加コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteAddFace(Poly_Ling.Data.AddFaceCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            var h = _addFaceHandler;
            if (h == null) return "面追加ハンドラがありません";
            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        /// <summary>格子変形コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteApplyLatticeDeform(Poly_Ling.Data.ApplyLatticeDeformCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _latticeHandler;
            if (h == null) return "格子変形ハンドラがありません";

            bool ok = h.ExecuteFromCommand(cmd, out string reason);

            _latticeSubPanel?.Refresh();

            return ok ? null : reason;
        }

        /// <summary>選択オブジェクトの移動コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteMoveObjects(Poly_Ling.Data.MoveObjectsCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _objectMoveHandler;
            if (h == null) return "オブジェクト移動ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;
            return null;
        }

        /// <summary>選択オブジェクトの回転コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteRotateObjects(Poly_Ling.Data.RotateObjectsCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _objectMoveHandler;
            if (h == null) return "オブジェクト移動ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;
            return null;
        }

        /// <summary>ボーン平面への平面化コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecutePlanarizeAlongBones(Poly_Ling.Data.PlanarizeAlongBonesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _planarizeAlongBonesHandler;
            if (h == null) return "ボーン平面への平面化ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _planarizeAlongBonesSubPanel?.Refresh();
            return null;
        }

        /// <summary>頂点結合コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteMergeVertices(Poly_Ling.Data.MergeVerticesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _mergeVerticesHandler;
            if (h == null) return "頂点結合ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _mergeVerticesSubPanel?.Refresh();
            return null;
        }

        // ================================================================
        // 位相・頂点編集（対象や生成先の指定を伴う実行系）
        // ================================================================

        /// <summary>選択要素の削除コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteDeleteSelectionCommand(Poly_Ling.Data.DeleteSelectionCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _deleteSelectionHandler;
            if (h == null) return "選択要素の削除ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;
            return null;
        }

        /// <summary>パイプ整列コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecutePipeAlign(Poly_Ling.Data.PipeAlignCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _pipeAlignHandler;
            if (h == null) return "パイプ整列ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _pipeAlignSubPanel?.Refresh();
            return null;
        }

        /// <summary>配置物の整形コマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecutePlaceObjectReshape(Poly_Ling.Data.PlaceObjectReshapeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _placeObjectReshapeHandler;
            if (h == null) return "配置物の整形ハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _placeObjectReshapeSubPanel?.Refresh();
            return null;
        }

        /// <summary>厚み付けコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSolidify(Poly_Ling.Data.SolidifyCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _solidifyHandler;
            if (h == null) return "厚み付けハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _solidifySubPanel?.Refresh();
            return null;
        }

        /// <summary>線分押し出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteLineExtrude(Poly_Ling.Data.LineExtrudeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _lineExtrudeHandler;
            if (h == null) return "線分押し出しハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _lineExtrudeSubPanel?.Refresh();
            return null;
        }

        /// <summary>面に張り付けコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSurfaceSnap(Poly_Ling.Data.SurfaceSnapCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _surfaceSnapHandler;
            if (h == null) return "面に張り付けハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _surfaceSnapSubPanel?.Refresh();
            return null;
        }

        // ================================================================
        // ドラッグ確定（ベベル・押し出し）
        //
        // ドラッグ確定・パネル操作・リモートのどれも同じ Apply*FromCommand を通る。
        // ================================================================

        /// <summary>辺ベベルコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeBevel(Poly_Ling.Data.EdgeBevelCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _edgeBevelHandler;
            if (h == null) return "辺ベベルハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _edgeBevelSubPanel?.Refresh();
            return null;
        }

        /// <summary>辺・線分の押し出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteEdgeExtrude(Poly_Ling.Data.EdgeExtrudeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _edgeExtrudeHandler;
            if (h == null) return "辺・線分の押し出しハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _edgeExtrudeSubPanel?.Refresh();
            return null;
        }

        /// <summary>面の押し出しコマンド。</summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteFaceExtrude(Poly_Ling.Data.FaceExtrudeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _faceExtrudeHandler;
            if (h == null) return "面の押し出しハンドラがありません";

            if (!h.ExecuteFromCommand(cmd, out string reason)) return reason;

            _faceExtrudeSubPanel?.Refresh();
            return null;
        }

        /// <summary>
        /// スキンウェイト塗りコマンド。
        /// 専用サブパネルは Refresh を持たないので、ウェイト表示の更新は
        /// ツール側（ActivePanel.NotifyWeightChanged）に任せる。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSkinWeightPaint(Poly_Ling.Data.SkinWeightPaintCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h = _skinWeightPaintHandler;
            if (h == null) return "スキンウェイト塗りハンドラがありません";

            return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
        }

        // ================================================================
        // 作業軸
        //
        // 作業軸はモデルの頂点・選択を書き換えない。Undo も積まない
        // （マウス経路・パネル経路とも積んでいないので、そこは変えない）。
        // ================================================================

        /// <summary>
        /// 作業軸の状態差し替えコマンド。
        /// 書き込みは WorkAxisContext.ApplySnapshot に通す（下限クランプを含めて正典）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteSetWorkAxis(Poly_Ling.Data.SetWorkAxisCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var wa = CurrentWorkAxis();
            if (wa == null) return "作業軸がありません";

            wa.ApplySnapshot(new Poly_Ling.Context.WorkAxisSnapshot
            {
                Origin    = cmd.Origin,
                Rotation  = UnityEngine.Quaternion.Euler(cmd.EulerAngles),
                Length    = cmd.Length,
                IsVisible = cmd.IsVisible,
            });

            NotifyWorkAxisChanged();
            return null;
        }

        /// <summary>
        /// 作業軸ライブラリ呼び出しコマンド。
        /// 表示フラグは変えない（WorkAxisEntry.ApplyTo と同じ）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteRecallWorkAxis(Poly_Ling.Data.RecallWorkAxisCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var wa = CurrentWorkAxis();
            if (wa == null) return "作業軸がありません";

            var lib = ActiveProject?.WorkAxes;
            if (lib == null) return "作業軸ライブラリがありません";

            string name = Poly_Ling.Context.WorkAxisLibrary.Normalize(cmd.Name);
            if (name.Length == 0) return "名前が空です";
            if (!lib.TryGet(name, out var entry)) return $"「{name}」は登録されていません";

            entry.ApplyTo(wa);

            NotifyWorkAxisChanged();
            return null;
        }

        /// <summary>
        /// 作業軸が変わったときの後処理。ハンドラ・パネルの OnValueChanged と同じ内容を通す。
        /// </summary>
        private void NotifyWorkAxisChanged()
        {
            _workAxisSubPanel?.Refresh();
            _deformWorkAxisSubPanel?.Refresh();
            UpdateGizmoOverlay();
            // 格子変形の格子フレームは作業軸そのもの。開いていれば追従させる。
            _latticeHandler?.OnFrameChanged();
        }

        /// <summary>
        /// 辺群ブリッジコマンド。拾いをハンドラへ入れてから、既存の生成経路を通す。
        /// 受理判定（境界辺のみ・同一オブジェクトのみ）は SetPicks が既存の
        /// AcceptEdge へ通すので、クリック経路と同じ規則が効く。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteCreateEdgeBridge(CreateEdgeBridgeCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h     = _edgeBridgeHandler;
            var panel = _edgeBridgeSubPanel;
            if (h == null) return "辺群ブリッジハンドラがありません";

            h.AutoCorrespondence = cmd.AutoCorrespondence;
            h.FlipCorrespondence = cmd.FlipCorrespondence;
            h.FlipFaces          = cmd.FlipFaces;
            h.Subdivisions       = cmd.Subdivisions;

            if (!h.SetPicks(cmd.MeshIndex, cmd.Edges ?? new VertexPair[0], out string reason))
            {
                panel?.SetStatus(reason);
                return reason;
            }

            ExecuteEdgeBridge();
            return null;
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
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteDeleteFaces(DeleteFacesCommand cmd)
        {
            if (cmd == null) return "コマンドが null";
            if (cmd.FaceIndices == null || cmd.FaceIndices.Length == 0)
                return "FaceIndices が空です";

            var model = ActiveProject?.CurrentModel;
            if (model == null) return "no current model";

            var target = model.GetMeshContext(cmd.MeshIndex);
            if (target?.Selection == null || target.MeshObject == null)
                return $"masterIndex {cmd.MeshIndex} のメッシュがありません";

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
            if (!any) return $"FaceIndices に有効な面がありません（面数 {faceCount}）";

            // 削除そのものは DeleteSelectionToolHandler が正典。
            // ここから ExecuteDeleteSelection() を呼ぶと DeleteSelectionCommand の
            // 発行になり、コマンドがコマンドを呼ぶ形になるのでハンドラを直接呼ぶ。
            var h = _deleteSelectionHandler;
            if (h == null) return "選択削除ハンドラがありません";

            var delCmd = new Poly_Ling.Data.DeleteSelectionCommand(
                cmd.ModelIndex, model.SelectedDrawableMeshIndices.ToArray());
            return h.ExecuteFromCommand(delCmd, out string delReason) ? null : delReason;
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

        /// <summary>
        /// 穴点数合わせコマンド。種をハンドラへ入れてから実行する。
        ///
        /// 穴の縁の復元と頂点数の増減は HoleRingCountTool が持つので、
        /// 同じ処理を 2 つ持たないようハンドラ経由で通す。
        /// 種の検証（縁が閉じているか等）は SetSeeds が Tool へ渡して行う。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string ExecuteMatchHoleRingCount(MatchHoleRingCountCommand cmd)
        {
            if (cmd == null) return "コマンドが null";

            var h     = _holeRingCountHandler;
            var panel = _holeRingCountSubPanel;
            if (h == null) return "穴点数合わせハンドラがありません";

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
                return reason;
            }

            bool ok = h.Execute(out string message);
            panel?.SetResult(message);
            return ok ? null : message;
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
    }
}
