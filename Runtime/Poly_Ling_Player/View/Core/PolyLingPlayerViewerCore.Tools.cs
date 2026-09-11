// PolyLingPlayerViewerCore.Tools.cs
// Player ビューアのコア：プロジェクト・外部パネル向け API・ツール切り替え・一時選択サブツール・
// 選択頂点の結合・面削除モード・結合ツールのクリック実行。
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
        // プロジェクト
        // ================================================================

        private ProjectContext ActiveProject => _localLoader.Project ?? _receiver?.Project;

        // ================================================================
        // 外部パネル向け公開API
        // ================================================================

        /// <summary>データ変化通知。Editor専用外部パネル等がサブスクライブする。</summary>
        public Action<ChangeKind> OnChanged;

        /// <summary>現在アクティブな ProjectContext を返す。null の場合あり。</summary>
        public ProjectContext GetActiveProject() => ActiveProject;

        /// <summary>外部からコマンドをディスパッチする。</summary>
        public void Dispatch(PanelCommand cmd) => _commandDispatcher?.Dispatch(cmd);

        // ================================================================
        // ツール切り替え
        // ================================================================

        /// <summary>
        /// カテゴリ 1 (3D 操作と右ペインが一体) のパネルを開く共通ヘルパー。
        /// SetInteractionMode + ShowRightPanel + サブパネル Refresh を一括で行う。
        ///
        /// 【設計ポイント: 右ペイン型ツールでも btn 設定が必要】
        /// InteractionMode ボタンを持つツール (VertexMove 等) は GetButtonForInteractionMode
        /// が btn を返すが、右ペインから起動するツール (EdgeBevel / EdgeExtrude / FaceExtrude
        /// / EdgeTopology / Knife / AddFace) はそちらでは null になる。
        /// このため switch 内で自分の `btn = _layoutRoot?.〇〇Btn;` を明示的に割当てないと
        /// `_activePanelBtn` が null のままとなり、右ペインを開いても当該ボタンが緑
        /// ハイライトされない。ボタンを持つツールを追加するときは、ここにも case を
        /// 追加して btn を設定すること (section / refresh と同列)。
        /// </summary>
        private void ShowCategory1Panel(InteractionMode mode)
        {
            SetInteractionMode(mode);

            VisualElement section = null;
            Button btn = null;
            System.Action refresh = null;

            switch (mode)
            {
                case InteractionMode.VertexMove:
                    section = _layoutRoot?.VertexMoveSection;
                    btn     = _layoutRoot?.ToolVertexMoveBtn;
                    refresh = () => _vertexMoveSubPanel?.Refresh();
                    break;
                case InteractionMode.ObjectMove:
                    section = _layoutRoot?.BoneEditorSection;
                    btn     = _layoutRoot?.ToolObjectMoveBtn;
                    refresh = () => _boneEditorSubPanel?.Refresh();
                    break;
                case InteractionMode.PivotOffset:
                    section = _layoutRoot?.PivotSection;
                    btn     = _layoutRoot?.ToolPivotOffsetBtn;
                    break;
                case InteractionMode.Sculpt:
                    section = _layoutRoot?.SculptSection;
                    btn     = _layoutRoot?.ToolSculptBtn;
                    // 一時ミラーのボタン表示を実状態へ合わせるため Refresh が要る。
                    refresh = () => _sculptSubPanel?.Refresh();
                    break;
                case InteractionMode.AdvancedSelect:
                    section = _layoutRoot?.AdvancedSelectSection;
                    btn     = _layoutRoot?.ToolAdvancedSelBtn;
                    refresh = () => _advancedSelectSubPanel?.Refresh();
                    break;
                case InteractionMode.SkinWeightPaint:
                    section = _layoutRoot?.SkinWeightPaintSection;
                    btn     = _layoutRoot?.ToolSkinWeightPaintBtn;
                    break;
                case InteractionMode.SkinWeightNumeric:
                    section = _layoutRoot?.SkinWeightNumericSection;
                    btn     = _layoutRoot?.SkinWeightNumericBtn;
                    refresh = () => _skinWeightNumericSubPanel?.Refresh();
                    break;
                case InteractionMode.AddFace:
                    section = _layoutRoot?.AddFaceSection;
                    // AddFace は右ペインから起動するためツールボタンなし → btn = null
                    refresh = () => _addFaceSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeBevel:
                    section = _layoutRoot?.EdgeBevelSection;
                    btn     = _layoutRoot?.EdgeBevelBtn;
                    refresh = () => _edgeBevelSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeExtrude:
                    section = _layoutRoot?.EdgeExtrudeSection;
                    btn     = _layoutRoot?.EdgeExtrudeBtn;
                    refresh = () => _edgeExtrudeSubPanel?.Refresh();
                    break;
                case InteractionMode.FaceExtrude:
                    section = _layoutRoot?.FaceExtrudeSection;
                    btn     = _layoutRoot?.FaceExtrudeBtn;
                    refresh = () => _faceExtrudeSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeTopology:
                    section = _layoutRoot?.EdgeTopologySection;
                    btn     = _layoutRoot?.EdgeTopologyBtn;
                    refresh = () => _edgeTopologySubPanel?.Refresh();
                    break;
                case InteractionMode.Knife:
                    section = _layoutRoot?.KnifeSection;
                    btn     = _layoutRoot?.KnifeBtn;
                    refresh = () => _knifeSubPanel?.Refresh();
                    break;
                case InteractionMode.EdgeBridge:
                    section = _layoutRoot?.EdgeBridgeSection;
                    btn     = _layoutRoot?.EdgeBridgeBtn;
                    refresh = () => _edgeBridgeSubPanel?.Refresh();
                    break;
                case InteractionMode.FlipFace:
                    section = _layoutRoot?.FlipFaceSection;
                    btn     = _layoutRoot?.FlipFaceBtn;
                    refresh = () => _flipFaceSubPanel?.Refresh();
                    break;
                case InteractionMode.Solidify:
                    section = _layoutRoot?.SolidifySection;
                    btn     = _layoutRoot?.SolidifyBtn;
                    refresh = () => _solidifySubPanel?.Refresh();
                    break;
                case InteractionMode.Rotate:
                    section = _layoutRoot?.RotateSection;
                    btn     = _layoutRoot?.RotateBtn;
                    refresh = () => _rotateSubPanel?.Refresh();
                    break;
                case InteractionMode.WorkAxis:
                    section = _layoutRoot?.WorkAxisSection;
                    btn     = _layoutRoot?.WorkAxisBtn;
                    refresh = () => _workAxisSubPanel?.Refresh();
                    break;
                case InteractionMode.Camera:
                    section = _layoutRoot?.CameraSection;
                    btn     = _layoutRoot?.CameraBtn;
                    refresh = () => _cameraSubPanel?.Refresh();
                    break;
                case InteractionMode.Deform:
                    section = _layoutRoot?.DeformSection;
                    btn     = _layoutRoot?.DeformBtn;
                    refresh = () => _deformSubPanel?.Refresh();
                    break;
                case InteractionMode.Lattice:
                    section = _layoutRoot?.LatticeSection;
                    btn     = _layoutRoot?.LatticeBtn;
                    refresh = () => _latticeSubPanel?.Refresh();
                    break;
                case InteractionMode.Scale:
                    section = _layoutRoot?.ScaleSection;
                    btn     = _layoutRoot?.ScaleBtn;
                    refresh = () => _scaleSubPanel?.Refresh();
                    break;
                case InteractionMode.VertexDissolve:
                    section = _layoutRoot?.VertexDissolveSection;
                    btn     = _layoutRoot?.VertexDissolveBtn;
                    refresh = () => _vertexDissolveSubPanel?.Refresh();
                    break;
                case InteractionMode.Tri4To1:
                    section = _layoutRoot?.Tri4To1Section;
                    btn     = _layoutRoot?.Tri4To1Btn;
                    refresh = () => _tri4To1SubPanel?.Refresh();
                    break;
                case InteractionMode.FaceMerge:
                    section = _layoutRoot?.FaceMergeSection;
                    btn     = _layoutRoot?.FaceMergeBtn;
                    refresh = () => _faceMergeSubPanel?.Refresh();
                    break;
                case InteractionMode.Quad4To1:
                    section = _layoutRoot?.Quad4To1Section;
                    btn     = _layoutRoot?.Quad4To1Btn;
                    refresh = () => _quad4To1SubPanel?.Refresh();
                    break;
            }

            ShowRightPanel(section, btn);
            refresh?.Invoke();
        }

        // ================================================================
        // 一時選択サブツール
        //   ShowCategory1Panel は使わない。右ペイン表示とボタンハイライトを
        //   通常ツールと同じようには動かさず、入力ハンドラのみ差し替えるため
        //   SetInteractionMode を直接呼ぶ。
        // ================================================================

        /// <summary>
        /// 一時選択サブツールへ入る。lasso = true で投げ縄、false で矩形。
        /// </summary>
        private void EnterSelectSubTool(bool lasso)
        {
            if (_moveToolHandler == null) return;

            // サブツール中の押し替え (R → G / G → R) では復帰先を上書きしない。
            if (!_subToolActive)
            {
                _subToolActive             = true;
                _subToolPrevMode           = _interactionMode;
                _subToolPrevMoveDragMode   = _moveToolHandler.DragSelectMode;
                _subToolPrevObjectDragMode = _objectMoveHandler != null
                    ? _objectMoveHandler.DragSelectMode
                    : ObjectMoveToolHandler.SelectionDragMode.Box;

                // 選択モードの退避は不要。SelectOnly は
                // ResolveToolSelectModeOverride が現在の override をそのまま引き継ぐ。
                SetInteractionMode(InteractionMode.SelectOnly);
            }

            _moveToolHandler.DragSelectMode = lasso
                ? MoveToolHandler.SelectionDragMode.Lasso
                : MoveToolHandler.SelectionDragMode.Box;

            // LassoToggle が両ハンドラを同時に書き換える既存の対称性を保つ。
            if (_objectMoveHandler != null)
                _objectMoveHandler.DragSelectMode = lasso
                    ? ObjectMoveToolHandler.SelectionDragMode.Lasso
                    : ObjectMoveToolHandler.SelectionDragMode.Box;

            _moveToolHandler.OneShotFinished = ExitSelectSubTool;

            // SetInteractionMode より後に DragSelectMode が決まるため、ここで再通知する
            // （矩形／投げ縄の別を性能ログへ残す）。
            ReportPerfToolState();
        }

        /// <summary>
        /// 一時選択サブツールから直前のツールへ戻す。サブツール中でなければ何もしない。
        /// </summary>
        private void ExitSelectSubTool()
        {
            if (!_subToolActive) return;
            _subToolActive = false;

            if (_moveToolHandler != null)
            {
                _moveToolHandler.OneShotFinished = null;
                _moveToolHandler.DragSelectMode  = _subToolPrevMoveDragMode;
            }
            if (_objectMoveHandler != null)
                _objectMoveHandler.DragSelectMode = _subToolPrevObjectDragMode;

            // 復帰先モードの override は SetInteractionMode が決め直すため、
            // 選択モードの復元処理は不要。
            SetInteractionMode(_subToolPrevMode);

            // ドラッグ選択モードのトグル表示を実状態へ戻す
            // (サブツール中に Refresh が走った場合のずれを解消する)。
            _layoutRoot?.LassoToggle?.SetValueWithoutNotify(
                _subToolPrevMoveDragMode == MoveToolHandler.SelectionDragMode.Lasso);
            _vertexMoveSubPanel?.Refresh();
        }

        /// <summary>
        /// 選択削除サブツール。選択中の頂点 / 面 / 線分を削除する。
        ///
        /// 矩形・投げ縄サブツールと違い InteractionMode は一切変更しない。
        /// 削除はマウス操作を伴わない即時実行なので、ドラッグを奪う必要が無く、
        /// SelectOnly へ往復させても SetInteractionMode の脱出/進入処理
        /// (フックの null 化・選択モード override の決め直し・ボタンハイライト・
        ///  ギズモ overlay 更新) が空回りするだけで実利が無い。
        /// モードを触らないので「実行前のツールに戻る」は自動的に満たされる。
        ///
        /// 矩形/投げ縄サブツール待ち (SelectOnly) の最中に呼ばれた場合も、
        /// その待ち状態は解除しない (ドラッグを消費していないため)。
        /// </summary>
        private void ExecuteDeleteSelection()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            _commandDispatcher?.Dispatch(new DeleteSelectionCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                model.SelectedDrawableMeshIndices.ToArray()));
        }

        /// <summary>
        /// 面追加モードでの「直前の点の取り消し」。Delete / Backspace の共通処理。
        ///
        ///   三角形・四角形・線分（非連続） … 未確定の点を 1 つ戻す。
        ///   線分（連続）                   … 確定済みの線分を 1 本取り消す
        ///                                    （Undo 1 回ぶん。面と新規頂点が戻る）。
        ///
        /// どちらの対象も無ければ何もしない。面追加モードの間は選択削除へ落とさない。
        /// </summary>
        private void ExecuteAddFaceRemoveLastPoint()
        {
            var h = _addFaceHandler;
            if (h == null) return;

            if (h.RemoveLastPoint() || h.UndoLastLineSegment())
                _addFaceSubPanel?.Refresh();
        }

        // ================================================================
        // 選択頂点の結合（即時実行の単発コマンド）
        //   選択削除と同じく InteractionMode は一切変更しない。実行前のツールが
        //   そのまま維持されるため、Ctrl+J 一発で結合が完了する。
        //   頂点マージパネルを開いていなくても動く（ハンドラ側で ToolContext を
        //   その場で組み立てるため）。
        // ================================================================

        /// <summary>
        /// 距離を見ず、選択頂点を 1 点（重心）へ結合する。
        ///
        /// 実処理はコマンドへ流す。しきい値はパネルの現在値を載せる
        /// （Centroid では読まれないが、コマンドを自己完結させるため）。
        /// </summary>
        private void ExecuteMergeSelectedToCentroid()
        {
            DispatchMergeVertices(Poly_Ling.Data.MergeVerticesCommand.MergeMode.Centroid);
        }

        /// <summary>選択頂点のうち、しきい値以下の距離にあるものを結合する。</summary>
        private void ExecuteMergeSelectedByThreshold()
        {
            DispatchMergeVertices(Poly_Ling.Data.MergeVerticesCommand.MergeMode.Threshold);
        }

        /// <summary>
        /// 頂点結合コマンドを組んで送る。ショートカットの 2 経路で共通。
        /// 対象は編集対象メッシュ 1 本（受け口は照合するだけで選択を書き換えない）。
        /// </summary>
        private void DispatchMergeVertices(
            Poly_Ling.Data.MergeVerticesCommand.MergeMode mode)
        {
            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc == null) return;

            _commandDispatcher?.Dispatch(new Poly_Ling.Data.MergeVerticesCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                new[] { model.IndexOf(mc) },
                mode,
                _mergeVerticesHandler?.Threshold ?? 0.001f));

            _mergeVerticesSubPanel?.Refresh();
        }

        // ================================================================
        // 面削除モード
        //   ShowCategory1Panel は使わない。右ペインは切り替えず、入力挙動と
        //   選択モードだけを差し替えるため SetInteractionMode を直接呼ぶ
        //   (一時選択サブツールと同じ方針)。
        // ================================================================

        /// <summary>
        /// 面削除モードへ入る。既に入っているときは何もしない
        /// (再進入で復帰先が自分自身に上書きされるのを防ぐ)。
        /// </summary>
        private void EnterDeleteFaceMode()
        {
            if (_deleteFaceModeActive) return;

            _deleteFaceModeActive = true;
            _deleteFacePrevMode   = _interactionMode;
            SetInteractionMode(InteractionMode.DeleteFace);
        }

        /// <summary>
        /// 面削除モードから直前のツールへ戻す。モード中でなければ何もしない。
        /// </summary>
        private void ExitDeleteFaceMode()
        {
            if (!_deleteFaceModeActive) return;

            // SetInteractionMode の脱出処理が _deleteFaceModeActive を false にし、
            // フック解除を行う。選択モードは復帰先モードの override が決め直す。
            SetInteractionMode(_deleteFacePrevMode);
        }

        /// <summary>
        /// 面削除モードでのクリック処理 (MoveToolHandler.OnLeftClickExtra)。
        ///
        /// 面以外のヒットは無視する。Selection.Mode を Face に絞ってあるので
        /// 通常は Face か None しか来ないが、念のため型で弾く。
        ///
        /// 修飾キーは無視する。ApplyElementClick が Shift/Ctrl で選択を積んだ後でも、
        /// ここで選択をクリック面 1 枚に差し替えてから削除するため、
        /// 「クリックした面だけが消える」挙動が常に保たれる。
        ///
        /// 削除対象はクリックされたメッシュ。ホバーは選択中の全メッシュから返るため、
        /// アクティブメッシュ (SelectedDrawableMeshIndices の先頭) 以外の面もクリック
        /// し得る。DeleteSelectionTool は選択中の描画オブジェクトを走査するので、
        /// 選択中オブジェクト全部の選択をクリアしてから、クリックされたメッシュに
        /// その面だけを入れて実行する。ActiveMeshIndex は変更しない。
        /// </summary>
        private void OnDeleteFaceClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Face) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return;

            var target = model.GetMeshContext(elem.MeshIndex);
            if (target?.Selection == null) return;

            // 選択をクリック面 1 枚に差し替える (修飾キーによる追加選択を無効化)。
            // 他オブジェクトに選択が残っていると一緒に消えるため、全部クリアする。
            foreach (int idx in model.SelectedDrawableMeshIndices)
                model.GetMeshContext(idx)?.Selection?.ClearAll();
            target.Selection.ClearAll();
            target.Selection.SelectFace(elem.FaceIndex, false);

            _commandDispatcher?.Dispatch(new DeleteFacesCommand(
                ActiveProject?.CurrentModelIndex ?? 0, elem.MeshIndex, new[] { elem.FaceIndex }));
        }

        // ================================================================
        // 結合ツールのクリック実行 (MoveToolHandler.OnLeftClickExtra)
        //
        // いずれも面削除モードと同じ方針:
        //   ・想定外の要素種別は無視する（Selection.Mode で絞ってあるが念のため）
        //   ・アクティブメッシュ以外は無視する（各 Tool は選択中オブジェクトを
        //     走査するため、別メッシュの要素をそのまま流すと意図しない箇所が変わる）
        //   ・修飾キーは無視し、選択をクリックした要素 1 つに差し替えてから実行する
        //
        // 実行そのものはコマンドへ流す。対象は「実行時点の選択オブジェクト」を
        // そのまま載せる（受け口は照合するだけで選択を書き換えない）。
        // ここで選択を差し替えているのはクリックした要素を選ぶためで、
        // 対象オブジェクトの集合は変えていない。
        // ================================================================

        private void OnVertexDissolveClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Vertex) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectVertex(elem.VertexIndex, false);

            _commandDispatcher?.Dispatch(new VertexDissolveCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                model.SelectedDrawableMeshIndices.ToArray()));
            _vertexDissolveSubPanel?.Refresh();
        }

        private void OnTri4To1Clicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Face) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectFace(elem.FaceIndex, false);

            _commandDispatcher?.Dispatch(new Tri4To1Command(
                ActiveProject?.CurrentModelIndex ?? 0,
                model.SelectedDrawableMeshIndices.ToArray()));
            _tri4To1SubPanel?.Refresh();
        }

        private void OnFaceMergeClicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Edge) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectEdge(new VertexPair(elem.EdgeV1, elem.EdgeV2), false);

            // 頂点を外すかはパネルの「頂点を削除する」に従う。
            _commandDispatcher?.Dispatch(new FaceMergeCommand(
                ActiveProject?.CurrentModelIndex ?? 0,
                model.SelectedDrawableMeshIndices.ToArray(),
                _faceMergeHandler?.DeleteVertices ?? true));
            _faceMergeSubPanel?.Refresh();
        }

        private void OnQuad4To1Clicked(PlayerHoverElement elem, ModifierKeys mods)
        {
            if (elem.Kind != PlayerHoverKind.Vertex) return;

            var model = ActiveProject?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc?.Selection == null) return;
            if (elem.MeshIndex != model.ActiveMeshIndex) return;

            mc.Selection.ClearAll();
            mc.Selection.SelectVertex(elem.VertexIndex, false);

            _commandDispatcher?.Dispatch(new Quad4To1Command(
                ActiveProject?.CurrentModelIndex ?? 0,
                model.SelectedDrawableMeshIndices.ToArray()));
            _quad4To1SubPanel?.Refresh();
        }
    }
}
