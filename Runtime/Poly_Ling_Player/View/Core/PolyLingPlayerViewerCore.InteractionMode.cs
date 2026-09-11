// PolyLingPlayerViewerCore.InteractionMode.cs
// Player ビューアのコア：3D 操作モード（SetInteractionMode）の切り替え。
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
        // SetInteractionMode: 3D 操作モード (ビューポートの入力ハンドラ) のみを切り替える。
        // 右ペイン表示やボタンハイライトには関与しない。
        //
        // カテゴリ 1 (3D 操作と右ペインが一体) → ShowRightPanel と組で呼ぶ
        // カテゴリ 2 (3D 操作を維持) → 呼ばない
        // カテゴリ 3 (3D 操作無効) → SetInteractionMode(None) を呼ぶ
        // ================================================================

        private void SetInteractionMode(InteractionMode mode)
        {
            // ── ツール内「一時ミラー」の自動解除 ─────────────────────────
            // 実体化したツールから離れたら、そのツールが生やした実体を必ず戻す。
            // ここが全ツール遷移の唯一の合流点なので、各ツール側に後始末を書かない。
            //
            // 一時選択サブツール (R / G → SelectOnly) への往復はツール遷移ではないので
            // 除外する。除外しないと、矩形選択を挟むたびに一時ミラーが消える。
            if (_tempMirrorController != null
                && _tempMirrorController.IsActive
                && _tempMirrorController.OwnerToken != (int)mode
                && !(_subToolActive && mode == InteractionMode.SelectOnly))
            {
                _tempMirrorController.Unbake();
            }

            // 旧モードの後始末 (新モードに関係なく必要な処理)
            if (_interactionMode == InteractionMode.Sculpt && mode != InteractionMode.Sculpt)
                _activePanel?.HideBrushCircle();

            if (_interactionMode == InteractionMode.AdvancedSelect && mode != InteractionMode.AdvancedSelect)
                _activePanel?.HideAdvSelPreview();

            if (_interactionMode == InteractionMode.SkinWeightPaint && mode != InteractionMode.SkinWeightPaint)
            {
                _skinWeightPaintHandler?.OnDeactivate();
                SkinWeightPaintTool.ActivePanel = null;
            }

            // スキンW数値設定もウェイト可視化を使う（ActivePanel に数値パネルを差す）。
            // 脱出時は可視化を止め、頂点カラーを消してから ActivePanel を外す。
            // 順序が逆だと ActivePanel が null になって対象メッシュを解決できず、
            // 色が残ったままになる。
            if (_interactionMode == InteractionMode.SkinWeightNumeric && mode != InteractionMode.SkinWeightNumeric)
            {
                ClearNumericWeightVisualization();
                SkinWeightPaintTool.SetVisualizationActive(false);
                SkinWeightPaintTool.ActivePanel = null;
            }

            // 【選択モードの復元について】
            // 旧実装はツール脱出のたびに ActiveMeshContext.Selection.Mode へ
            // MeshSelectMode.All を書き戻していた。All はチェックボックスの値ではないため、
            // 「頂点だけチェックしているのに、ツールを一度使うと辺・面までホバー／選択され、
            //  移動対象にもなる」状態が発生していた（＝設定がすぐ巻き戻る症状）。
            // 現在は、この関数の末尾寄りで新モードに対応する override を決め直し、
            // ApplySelectMode() が override 無しならチェックボックス値へ戻す。
            // ここでの個別復元は行わない。

            // 格子変形から脱出するとき、進行中のセッションは取消する。
            // 黙って確定すると、ユーザが意図しない変形が Undo 履歴に残るため。
            //
            // 作業軸へ移るときだけは取消さない。格子フレームは作業軸そのものなので、
            // 「格子全体を動かす」には一度作業軸パネルへ行く必要がある。
            if (_interactionMode == InteractionMode.Lattice
                && mode != InteractionMode.Lattice
                && mode != InteractionMode.WorkAxis)
            {
                _latticeHandler?.Cancel();
                _activePanel?.HideTopoToolOverlay();
                _activePanel?.HideBoxSelect();
            }

            // ---------------------------------------------------------------
            // カテゴリ 1 ツール (EdgeBevel / EdgeExtrude / FaceExtrude /
            // FlipFace / Solidify / Rotate / Scale / PrimitivePlace) は
            // MoveToolHandler の共通選択ロジックを流用している。
            // - EdgeBevel / EdgeExtrude / FaceExtrude はフック (OnDragStartExtra 等) に
            //   ツール固有ドラッグ動作を差し込む
            // - FlipFace / Solidify / Rotate / Scale は選択のみフック不要
            //   (ツール動作はサブパネル経由。将来 Rotate/Scale 用ギズモを追加する場合は
            //    フック利用に移行予定)
            // 脱出時は:
            //   - 全フックを null に戻す (次モードで古いフックが発火しないように)
            // 選択モードの復元は行わない (新モードの override 決定で自動的に戻る)。
            // ---------------------------------------------------------------
            bool leavingSharedSelectionTools =
                   (_interactionMode == InteractionMode.EdgeBevel   && mode != InteractionMode.EdgeBevel)
                || (_interactionMode == InteractionMode.EdgeExtrude && mode != InteractionMode.EdgeExtrude)
                || (_interactionMode == InteractionMode.FaceExtrude && mode != InteractionMode.FaceExtrude)
                || (_interactionMode == InteractionMode.FlipFace    && mode != InteractionMode.FlipFace)
                || (_interactionMode == InteractionMode.Solidify    && mode != InteractionMode.Solidify)
                || (_interactionMode == InteractionMode.Rotate      && mode != InteractionMode.Rotate)
                || (_interactionMode == InteractionMode.Scale       && mode != InteractionMode.Scale)
                // 配置ギズモも Rotate / Scale と同じくフックへ委譲する。
                // 解除し損ねると、次のモードでも OnDragStartExtra が true を返し続けて
                // 頂点移動が一切効かなくなる。
                || (_interactionMode == InteractionMode.PrimitivePlace && mode != InteractionMode.PrimitivePlace)
                // 変形も回転ハンドルをフックへ委譲する。解除し損ねると同じ症状になる。
                || (_interactionMode == InteractionMode.Deform      && mode != InteractionMode.Deform)
                // DeleteFace は OnLeftClickExtra で面クリック削除を発火する。
                // 脱出時のフック解除は同じ処理でよい。
                || (_interactionMode == InteractionMode.DeleteFace  && mode != InteractionMode.DeleteFace)
                // 頂点溶解 / 三角4→1 / 面結合 も OnLeftClickExtra でクリック実行する。
                // 脱出時の処理は面削除と同じでよい。
                || (_interactionMode == InteractionMode.VertexDissolve && mode != InteractionMode.VertexDissolve)
                || (_interactionMode == InteractionMode.Tri4To1        && mode != InteractionMode.Tri4To1)
                || (_interactionMode == InteractionMode.FaceMerge      && mode != InteractionMode.FaceMerge)
                || (_interactionMode == InteractionMode.Quad4To1          && mode != InteractionMode.Quad4To1);
            if (leavingSharedSelectionTools)
            {
                if (_moveToolHandler != null)
                {
                    _moveToolHandler.OnLeftClickExtra   = null;
                    _moveToolHandler.OnDragStartExtra   = null;
                    _moveToolHandler.OnToolDragExtra    = null;
                    _moveToolHandler.OnToolDragEndExtra = null;
                }
            }

            // 面削除モードから他モードへ移るとき、進入フラグを下ろす。
            // ツールボタンや他ショートカットで直接抜けた場合もここを通るため、
            // ExitDeleteFaceMode を経由しなくてもフラグが取り残されない。
            if (_interactionMode == InteractionMode.DeleteFace && mode != InteractionMode.DeleteFace)
                _deleteFaceModeActive = false;

            // 変形モードへ入り直すときは作業軸フェーズから始める。
            // 「軸を決める → 変形を掛ける」の順に誘導するため。
            // OnPhaseChanged 経由で ApplyDeformToolRouting が走るのを避けたいので
            // _interactionMode を書き換える前に済ませておく。
            if (mode == InteractionMode.Deform && _interactionMode != InteractionMode.Deform
                && _deformHandler != null)
                _deformHandler.Phase = DeformToolHandler.DeformPhase.WorkAxis;

            _interactionMode = mode;

            ReportPerfToolState();

            // ── 選択モードのツール固有 override をここで一括決定する ──────────
            // 進入時に絞り、脱出時に書き戻す方式は復元漏れ・非対称が起きやすい
            // (旧実装では脱出側が MeshSelectMode.All を書き、チェックボックスの
            //  指定が失われていた)。モードが確定したこの一点だけで決めれば、
            // どの経路から来ても実効モードが一意に定まる。
            // null を返すモードはユーザのチェックボックスに従う。
            _toolSelectModeOverride = ResolveToolSelectModeOverride(mode);
            ApplySelectMode();

            // 吸着用ヒットテスト（メッシュ選択を無視）は面追加モードでトグルが ON の
            // ときだけ有効。有効な間はポインタ移動ごとに追加ディスパッチと
            // 頂点数ぶんの読み戻しが走るため、他モードでは必ず切る。
            _viewportManager?.SetSnapHitTestEnabled(
                mode == InteractionMode.AddFace
                && (_addFaceHandler?.SnapToUnselectedObjects ?? false));

            // SelectOnly は毎回リセットし、下の SelectOnly case でのみ再有効化する
            // （他モードへ移ったら選択専用を確実に解除）。将来ギズモ用フックも同様にリセット。
            if (_moveToolHandler != null)
            {
                _moveToolHandler.SelectOnly           = false;
                _moveToolHandler.SuppressBuiltinGizmo = false;
                _moveToolHandler.GizmoHitTestOverride = null;
                _moveToolHandler.SuppressDragSelect   = false;
            }

            // 新モードの ToolHandler 割当 + ホバーコールバック登録
            switch (mode)
            {
                case InteractionMode.None:
                    // カテゴリ 3: 3D 操作無効 (ビュー回転/パン/ズームのみ)
                    _vertexInteractor?.SetToolHandler(null);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.SelectOnly:
                    // 選択専用: MoveToolHandler の選択/矩形/投げ縄のみ有効化し、移動ギズモ/頂点移動は無効。
                    if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.VertexMove:
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.ObjectMove:
                    _vertexInteractor?.SetToolHandler(_objectMoveHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _objectMoveHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.PivotOffset:
                    _vertexInteractor?.SetToolHandler(_pivotOffsetHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _pivotOffsetHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.PrimitivePlace:
                    // 配置ギズモ: 選択は MoveToolHandler を維持し、組み込み移動ギズモを
                    // 抑制、フック経由で PrimitivePlaceToolHandler のギズモへ委譲する
                    // (Rotate / Scale と同じ構成)。
                    //
                    // ハンドラごと _primitivePlaceHandler へ差し替えると、その OnLeftClick が
                    // 空で MoveToolHandler も外れるため、ビューポートでの頂点・辺の選択が
                    // クリック・矩形・投げ縄すべて不能になる。穴つなぎ（ブリッジ）の
                    // 種取り込みは選択を要求する (PickHoleSeeds) ので、
                    // 差し替え方式のままでは図形パネルを開いた時点で操作不能になる。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _primitivePlaceHandler != null && _primitivePlaceHandler.GizmoHitTest(pos, c);
                        // このツールはモデルへ触れない。ギズモに当たらなかったドラッグでも
                        // true を返して通常の頂点移動を抑止する。クリック選択と、
                        // 何も掴んでいない位置からの矩形／投げ縄選択はそのまま効く。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            _primitivePlaceHandler?.BeginGizmoDrag();
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _primitivePlaceHandler?.GizmoDrag(pos, delta);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) => _primitivePlaceHandler?.EndGizmoDrag();
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _primitivePlaceHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.WorkAxis:
                    // 作業軸ギズモ専用。モデルの選択・頂点操作は行わない。
                    _vertexInteractor?.SetToolHandler(_workAxisHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _workAxisHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Camera:
                    // カメラ調整ギズモ専用。モデルの選択・頂点操作は行わない。
                    _vertexInteractor?.SetToolHandler(_cameraHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _cameraHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Deform:
                    ApplyDeformToolRouting();
                    break;
                case InteractionMode.Lattice:
                    // 未開始・格子配置中はメッシュ頂点の選び直しを許し、
                    // 格子変形中だけ格子点の選択・移動へ切り替える。
                    ApplyLatticeToolRouting();
                    break;
                case InteractionMode.Sculpt:
                    _vertexInteractor?.SetToolHandler(_sculptHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _sculptHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.AdvancedSelect:
                    _vertexInteractor?.SetToolHandler(_advancedSelectHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _advancedSelectHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.AddFace:
                    _vertexInteractor?.SetToolHandler(_addFaceHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _addFaceHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.EdgeBevel:
                    // MoveToolHandler の選択/矩形選択を流用。
                    // ドラッグ開始フックで EdgeBevel の開始、継続ドラッグで幅調整、
                    // ドラッグ終了で確定 + Undo 記録を行う。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeBevelHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Edge ヒットのみベベル発火。要素なし or 型違いは通常の矩形選択等に任せる
                        if (elem.Kind != PlayerHoverKind.Edge) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと _mouseDownScreenPos が
                        // 画面隅になり量がマウス移動と連動しない）。Handler 側で ToImgui される。
                        _edgeBevelHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _edgeBevelHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _edgeBevelHandler?.OnLeftDragEnd(pos, mods);
                    break;
                case InteractionMode.EdgeExtrude:
                    // MoveToolHandler の選択/矩形選択を流用。ドラッグ系ツール (EdgeBevel と同パターン)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeExtrudeHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Edge / Line（2点面）ヒットで押し出し発火。要素なし or 型違いは通常の矩形選択等に任せる
                        if (elem.Kind != PlayerHoverKind.Edge && elem.Kind != PlayerHoverKind.Line) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと画面隅基準になり非連動）。
                        _edgeExtrudeHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _edgeExtrudeHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _edgeExtrudeHandler?.OnLeftDragEnd(pos, mods);
                    break;
                case InteractionMode.FaceExtrude:
                    // MoveToolHandler の選択/矩形選択を流用。ドラッグ系ツール (EdgeBevel と同パターン)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _faceExtrudeHandler?.UpdateHover(pos, ctx));
                    _moveToolHandler.OnDragStartExtra = (elem, mods) =>
                    {
                        // Face ヒットのみ押し出し発火
                        if (elem.Kind != PlayerHoverKind.Face) return false;
                        // 開始原点は実マウスダウン座標を渡す（zero だと画面隅基準になり非連動）。
                        _faceExtrudeHandler?.OnLeftDragBegin(
                            new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                            _moveToolHandler.MouseDownPos, mods);
                        return true;
                    };
                    _moveToolHandler.OnToolDragExtra    = (pos, delta, mods) => _faceExtrudeHandler?.OnLeftDrag(pos, delta, mods);
                    _moveToolHandler.OnToolDragEndExtra = (pos, mods)        => _faceExtrudeHandler?.OnLeftDragEnd(pos, mods);
                    break;
                case InteractionMode.FlipFace:
                    // ビューポートでは選択のみ (面の単独選択 / Shift 追加 / 矩形選択)。
                    // 面反転自体はサブパネル経由で実行 (本セッション対象外、別件で修正)。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Solidify:
                    // ビューポートでは選択のみ (面の単独選択 / Shift 追加 / 矩形選択)。
                    // 厚み付けの実行はサブパネル経由。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Rotate:
                    // ビューポート・回転リングギズモ: 選択は MoveToolHandler を維持し、
                    // 組み込み移動ギズモを抑制、フック経由で RotateToolHandler のリングへ委譲。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _rotateHandler != null && _rotateHandler.GizmoHitTest(pos, c);
                        // hover残留対策（頂点移動の EnterTransformDragging 修正と同系）:
                        // このギズモドラッグは MoveToolHandler の ToolDragging 経路で処理され、
                        // 組み込み軸ギズモ経路の OnEnterTransformDragging を通らないため、
                        // 従来はドラッグ中も Normal モードのまま毎フレーム GPU ヒットテストが
                        // 走り hover ハイライトがカーソルに追従していた。ここで DragBegin/DragEnd
                        // を明示発火し TransformDragging に入れることで hover を凍結＋開始時クリアする。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            if (_rotateHandler == null || !_rotateHandler.BeginGizmoDrag()) return false;
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _rotateHandler?.GizmoDrag(pos);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) =>
                        {
                            _rotateHandler?.EndGizmoDrag();
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        };
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _rotateHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.Scale:
                    // ビューポート・スケールギズモ: 選択は MoveToolHandler を維持し、
                    // 組み込み移動ギズモを抑制、フック経由で ScaleToolHandler のギズモへ委譲。
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SuppressBuiltinGizmo = true;
                        _moveToolHandler.GizmoHitTestOverride  = (pos, c) => _scaleHandler != null && _scaleHandler.GizmoHitTest(pos, c);
                        // hover残留対策（Rotate と同系。詳細は Rotate ケースのコメント参照）:
                        // ToolDragging 経路で TransformDragging に入らず hover が追従するため、
                        // DragBegin/DragEnd を明示発火して hover を凍結＋開始時クリアする。
                        _moveToolHandler.OnDragStartExtra      = (elem, mods) =>
                        {
                            if (_scaleHandler == null || !_scaleHandler.BeginGizmoDrag()) return false;
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
                            return true;
                        };
                        _moveToolHandler.OnToolDragExtra       = (pos, delta, mods) => _scaleHandler?.GizmoDrag(pos);
                        _moveToolHandler.OnToolDragEndExtra    = (pos, mods) =>
                        {
                            _scaleHandler?.EndGizmoDrag();
                            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        };
                    }
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _scaleHandler?.UpdateHover(pos, ctx));
                    break;
                case InteractionMode.DeleteFace:
                    // 面削除モード: 面のクリックのみ受け付け、クリックされた面を即削除する。
                    //   SelectOnly         … 軸ギズモ・要素ドラッグ移動を全て無効化
                    //   SuppressDragSelect … 矩形/投げ縄選択を無効化 (ドラッグ全体が無反応)
                    //   Selection.Mode=Face … 面以外のホバーハイライトとクリック選択を無効化
                    // 削除は OnLeftClickExtra から DeleteSelectionTool 経由で実行するため、
                    // Undo と位相変更通知は既存の選択削除と同じ経路に乗る。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnDeleteFaceClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.VertexDissolve:
                    // 頂点溶解モード: 頂点クリックのみ受け付け、その頂点を即溶かす。
                    // 面削除モードと同じ構成（SelectOnly / SuppressDragSelect /
                    // OnLeftClickExtra / Selection.Mode 固定）。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnVertexDissolveClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Tri4To1:
                    // 三角形4→1モード: 面クリックのみ受け付け、その三角形を即統合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnTri4To1Clicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.FaceMerge:
                    // 面結合（辺指定）モード: 辺クリックのみ受け付け、その辺を挟む2面を即結合する。
                    // 頂点を外すかはパネルの「頂点を削除する」（_faceMergeHandler.DeleteVertices）に従う。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnFaceMergeClicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.Quad4To1:
                    // 四角形4→1モード: 頂点クリックのみ受け付け、その頂点を即統合する。
                    if (_moveToolHandler != null)
                    {
                        _moveToolHandler.SelectOnly         = true;
                        _moveToolHandler.SuppressDragSelect = true;
                        _moveToolHandler.OnLeftClickExtra   = OnQuad4To1Clicked;
                    }
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.EdgeTopology:
                    _vertexInteractor?.SetToolHandler(_edgeTopologyHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeTopologyHandler?.UpdateHover(pos, ctx));
                    // Split → Vertex ホバーのみ、Flip/Dissolve → Edge ホバーのみ
                    // (override は ResolveToolSelectModeOverride が同じ規則で決める)
                    break;
                case InteractionMode.Knife:
                    _vertexInteractor?.SetToolHandler(_knifeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _knifeHandler?.UpdateHover(pos, ctx));
                    // 初期段（開始頂点）＝ Vertex ホバー。
                    // (override は ResolveToolSelectModeOverride が _knifeHandler.HoverSelectMode から決める)
                    break;
                case InteractionMode.EdgeBridge:
                    // 辺の明示ピック専用。選択状態は触らないので MoveToolHandler は使わない。
                    // ホバー種別は ResolveToolSelectModeOverride が Edge に固定する。
                    _vertexInteractor?.SetToolHandler(_edgeBridgeHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case InteractionMode.SkinWeightNumeric:
                    // 数値入力のみで適用する。ビューポートでは MoveToolHandler の
                    // 選択・矩形選択だけを流用し、組み込み移動ギズモは出さない
                    // (Deform と同型)。選択種別は ResolveToolSelectModeOverride で頂点のみ。
                    if (_moveToolHandler != null) _moveToolHandler.SelectOnly = true;
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    _skinWeightNumericSubPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    // ウェイトのヒートマップ可視化はペイントツールの機構を流用する。
                    // ActivePanel は SkinWeightPaintTool.VisualizationTargetBone と
                    // MeshSceneRenderer.CollectWeightVisTargets の参照先。
                    SkinWeightPaintTool.ActivePanel = _skinWeightNumericSubPanel;
                    SkinWeightPaintTool.SetVisualizationActive(true);
                    _viewportManager.EnterWeightTargetChanged(ActiveProject);
                    // Undo 対象は PlayerCommandDispatcher がメッシュごとに
                    // SetMeshObject で差し替えるため、ここでは設定しない。
                    // _skinWeightUndoMasterIndex を残すと Undo 書き戻し先 (:608 付近) が
                    // その 1 メッシュに固定され、多メッシュ編集の Undo が壊れる。
                    // -1 にしておけば MeshObject 参照からの逆引きが働く。
                    _skinWeightUndoMasterIndex = -1;
                    break;
                case InteractionMode.SkinWeightPaint:
                    _vertexInteractor?.SetToolHandler(_skinWeightPaintHandler);
                    SkinWeightPaintTool.ActivePanel = _skinWeightPaintPanel;
                    _skinWeightPaintPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    _skinWeightPaintHandler?.OnActivate();
                    // 【可視化の有効化は OnActivate に任せない】
                    // SkinWeightPaintToolHandler.OnActivate は GetToolContext() が null だと
                    // 無言で return し、その中にある IsVisualizationActive = true に届かない。
                    // 結果ヒートマップが一切出ない状態になっていた。
                    // SkinWeightNumeric 側と同じく、ここで無条件に立てる。
                    SkinWeightPaintTool.SetVisualizationActive(true);
                    // 進入直後に色を焼き込む。これが無いとボーンのドロップダウンを
                    // 触るまで PresentAll が走らず色が出ない。
                    _viewportManager.EnterWeightTargetChanged(ActiveProject);
                    // Undo 対象は SkinWeightPaintTool がストローク中にメッシュごとへ
                    // SetMeshObject で差し替える（ブラシは選択オブジェクト全件をまたぐ）。
                    // _skinWeightUndoMasterIndex を残すと Undo 書き戻し先 (:608 付近) が
                    // 1 メッシュに固定され、複数メッシュの Undo が壊れる。
                    _skinWeightUndoMasterIndex = -1;
                    break;
            }

            // ホバー(頂点ヒットテスト)抑止: ボーン・モーフ系(None)・ボーンエディタ(ObjectMove)・
            // SkinWeightPaint では移動用の頂点ホバーが不要なため抑止する。
            _viewportManager?.SetSuppressHover(
                mode == InteractionMode.None ||
                mode == InteractionMode.ObjectMove ||
                mode == InteractionMode.SkinWeightPaint);

            // InteractionMode ボタンのハイライト (2 系統色の片方)
            UpdateInteractionButtonHighlight();

            // ギズモ overlay をモード切替と同時に更新する。
            // PlayerViewportPanel._gizmoData は UpdateGizmo / HideGizmo でしか書き換わらず、
            // これを呼ぶのは UpdateGizmoOverlay だけ。従来ここで呼んでいなかったため、
            // モード切替後にビューポート上でマウスを動かす (EnterHoverChanged が
            // OnRefreshGizmoOverlay を発火する) まで前モードのギズモが残っていた。
            // _viewportManager は readonly の inline 初期化で null にならず、
            // UpdateGizmoOverlay 自身が _activePanel == null を先頭で弾くため安全。
            //
            // 原点マーカー（水色ダイヤ）も同じ理由で組み直す。出す対象はモードと
            // ピックフィルタで決まるので、モードが変わればマーカーも変わる。
            RefreshObjectOverlays();
        }
    }
}
