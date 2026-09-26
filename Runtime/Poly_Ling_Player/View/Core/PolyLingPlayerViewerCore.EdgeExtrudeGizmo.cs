// PolyLingPlayerViewerCore.EdgeExtrudeGizmo.cs
// Player ビューアのコア：辺押し出しの 3D 操作の結線。
// 辺・線分をドラッグして押し出すほか、ギズモ（移動・回転・拡大縮小）でも押し出す。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【ギズモでの押し出し】
//   掴んだ瞬間：EdgeExtrudeToolHandler.BeginGizmoExtrude（量 0 で押し出し、頂点選択を複製頂点に）。
//   ドラッグ中：各ギズモの既存処理が複製頂点を動かす（プレビュー）。
//   離したとき：ギズモ側の確定を横取りする（CommitCapture → ギズモ側が開始状態へ戻す → CommitFinish）。
//     CaptureGizmoExtrude で複製頂点の位置を読み、FinishGizmoExtrude が押す前へ戻して
//     押し出しコマンドを 1 本送る。押し出しと変形がまとめて Undo 1 回になる。
//   移動ギズモは MoveToolHandler の組み込みギズモ（押した瞬間に対象を決める）なので
//   OnBuiltinGizmoGrab で掴んだ瞬間に割り込む。回転・拡大縮小は回転・拡大縮小の状態と同じ結線。
//
// 【ギズモ「なし」】ギズモを出さない。辺・線分のドラッグでの押し出しはそのまま使える。
// 【押し出しの一時停止】割り込み・横取り・辺ドラッグの押し出しを外し、
//   頂点移動・回転・拡大縮小の状態と同じ普通の変形にする（確定も各ギズモの既存コマンド）。

using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private enum EdgeExtrudeDrag { None, Drag, Rotate, Scale }
        private EdgeExtrudeDrag _edgeExtrudeDrag = EdgeExtrudeDrag.None;

        private void OnEdgeExtrudeGizmoKindChanged()
        {
            if (_interactionMode != InteractionMode.EdgeExtrude) return;
            ApplyEdgeExtrudeRouting();
            UpdateGizmoOverlay();
        }

        /// <summary>回転・拡大縮小ギズモのとき、そのハンドラを今の対象で使えるようにする。</summary>
        private void ActivateEdgeExtrudeGizmoHandler()
        {
            if (_interactionMode != InteractionMode.EdgeExtrude || _edgeExtrudeHandler == null) return;
            var ctx = _viewportManager?.GetCurrentToolContext(_activeViewport);
            if (ctx == null) return;
            switch (_edgeExtrudeHandler.Gizmo)
            {
                case EdgeExtrudeToolHandler.GizmoKind.Rotate: _rotateHandler?.Activate(ctx); break;
                case EdgeExtrudeToolHandler.GizmoKind.Scale:  _scaleHandler?.Activate(ctx);  break;
            }
        }

        /// <summary>辺押し出しの状態の結線。モードに入ったとき、ギズモの種類・一時停止を変えたときに呼ぶ。</summary>
        private void ApplyEdgeExtrudeRouting()
        {
            var m = _moveToolHandler;
            var h = _edgeExtrudeHandler;
            if (m == null) return;

            _vertexInteractor?.SetToolHandler(m);
            var  kind   = h?.Gizmo ?? EdgeExtrudeToolHandler.GizmoKind.Move;
            bool paused = h != null && h.ExtrudePaused;

            // 組み直しのたびに外してから付ける
            m.SuppressBuiltinGizmo = kind != EdgeExtrudeToolHandler.GizmoKind.Move;
            m.GizmoHitTestOverride = null;
            m.OnBuiltinGizmoGrab   = null;
            m.CommitCapture        = null;
            m.CommitFinish         = null;
            m.OnDragStartExtra     = null;
            m.OnToolDragExtra      = null;
            m.OnToolDragEndExtra   = null;
            if (_rotateHandler != null) { _rotateHandler.CommitCapture = null; _rotateHandler.CommitFinish = null; }
            if (_scaleHandler  != null) { _scaleHandler.CommitCapture  = null; _scaleHandler.CommitFinish  = null; }
            _edgeExtrudeDrag = EdgeExtrudeDrag.None;

            // 回転・拡大縮小ギズモの当たり判定（押し出し中も一時停止中も同じ）
            if (kind == EdgeExtrudeToolHandler.GizmoKind.Rotate)
                m.GizmoHitTestOverride = (pos, c) => _rotateHandler != null && _rotateHandler.GizmoHitTest(pos, c);
            else if (kind == EdgeExtrudeToolHandler.GizmoKind.Scale)
                m.GizmoHitTestOverride = (pos, c) => _scaleHandler != null && _scaleHandler.GizmoHitTest(pos, c);

            // 押し出しの割り込み・横取り（一時停止中は付けない＝普通の変形として確定する）
            if (!paused)
            {
                switch (kind)
                {
                    case EdgeExtrudeToolHandler.GizmoKind.Move:
                        m.OnBuiltinGizmoGrab = () => h != null && h.BeginGizmoExtrude();
                        m.CommitCapture      = () => h != null && h.CaptureGizmoExtrude();
                        m.CommitFinish       = () => h?.FinishGizmoExtrude();
                        break;
                    case EdgeExtrudeToolHandler.GizmoKind.Rotate:
                        if (_rotateHandler != null)
                        {
                            _rotateHandler.CommitCapture = () => h != null && h.CaptureGizmoExtrude();
                            _rotateHandler.CommitFinish  = () => h?.FinishGizmoExtrude();
                        }
                        break;
                    case EdgeExtrudeToolHandler.GizmoKind.Scale:
                        if (_scaleHandler != null)
                        {
                            _scaleHandler.CommitCapture = () => h != null && h.CaptureGizmoExtrude();
                            _scaleHandler.CommitFinish  = () => h?.FinishGizmoExtrude();
                        }
                        break;
                }
            }
            ActivateEdgeExtrudeGizmoHandler();

            _viewportManager?.RegisterActiveToolHandler((pos, ctx) =>
            {
                if (!paused) h?.UpdateHover(pos, ctx);
                switch (kind)
                {
                    case EdgeExtrudeToolHandler.GizmoKind.Rotate: _rotateHandler?.UpdateHover(pos, ctx); break;
                    case EdgeExtrudeToolHandler.GizmoKind.Scale:  _scaleHandler?.UpdateHover(pos, ctx);  break;
                }
            });

            // 一時停止中で移動ギズモ・ギズモなしのときはドラッグの割り込みを付けない
            // （頂点移動と同じ普通の移動になる）。
            bool needDragHook = !paused
                || kind == EdgeExtrudeToolHandler.GizmoKind.Rotate
                || kind == EdgeExtrudeToolHandler.GizmoKind.Scale;
            if (!needDragHook) return;

            m.OnDragStartExtra = (elem, mods) =>
            {
                // 辺・線分をドラッグ → その場で押し出す（一時停止中は押し出さない）
                if (!paused && (elem.Kind == PlayerHoverKind.Edge || elem.Kind == PlayerHoverKind.Line))
                {
                    // 対象の担当者判定とロック取得。止められたら何もしない（操作経路統一計画.md H-2）。
                    if (!TryBeginHostPreview(new[] { elem.MeshIndex })) return true;
                    // 開始原点は実マウスダウン座標を渡す（zero だと画面隅基準になり非連動）。
                    h?.OnLeftDragBegin(
                        new PlayerHitResult { HasHit = true, MeshIndex = elem.MeshIndex, VertexIndex = -1 },
                        m.MouseDownPos, mods);
                    _edgeExtrudeDrag = EdgeExtrudeDrag.Drag;
                    return true;
                }

                // 回転・拡大縮小ギズモ
                if (kind == EdgeExtrudeToolHandler.GizmoKind.Rotate && _rotateHandler != null && _rotateHandler.GizmoGrabbed)
                    return paused
                        ? BeginPlainGizmo(EdgeExtrudeDrag.Rotate, () => _rotateHandler.BeginGizmoDrag())
                        : BeginEdgeExtrudeGizmo(EdgeExtrudeDrag.Rotate, () => _rotateHandler.BeginGizmoDrag());
                if (kind == EdgeExtrudeToolHandler.GizmoKind.Scale && _scaleHandler != null && _scaleHandler.GizmoGrabbed)
                    return paused
                        ? BeginPlainGizmo(EdgeExtrudeDrag.Scale, () => _scaleHandler.BeginGizmoDrag())
                        : BeginEdgeExtrudeGizmo(EdgeExtrudeDrag.Scale, () => _scaleHandler.BeginGizmoDrag());

                return false;
            };

            m.OnToolDragExtra = (pos, delta, mods) =>
            {
                switch (_edgeExtrudeDrag)
                {
                    case EdgeExtrudeDrag.Drag:   h?.OnLeftDrag(pos, delta, mods);   break;
                    case EdgeExtrudeDrag.Rotate: _rotateHandler?.GizmoDrag(pos);    break;
                    case EdgeExtrudeDrag.Scale:  _scaleHandler?.GizmoDrag(pos);     break;
                }
            };

            m.OnToolDragEndExtra = (pos, mods) =>
            {
                var d = _edgeExtrudeDrag;
                _edgeExtrudeDrag = EdgeExtrudeDrag.None;
                switch (d)
                {
                    case EdgeExtrudeDrag.Drag:
                        h?.OnLeftDragEnd(pos, mods);
                        break;
                    case EdgeExtrudeDrag.Rotate:
                        _rotateHandler?.EndGizmoDrag();
                        _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        break;
                    case EdgeExtrudeDrag.Scale:
                        _scaleHandler?.EndGizmoDrag();
                        _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragEnd);
                        break;
                }
            };
        }

        /// <summary>
        /// 回転・拡大縮小ギズモで押し出しを始める。押し出せる対象が無ければ false。
        /// ギズモの操作を始められなければ押し出しを取り消す。
        /// </summary>
        private bool BeginEdgeExtrudeGizmo(EdgeExtrudeDrag kind, System.Func<bool> beginGizmo)
        {
            var h = _edgeExtrudeHandler;
            if (h == null || !h.BeginGizmoExtrude()) return false;
            if (!beginGizmo())
            {
                // 動いていないので、押す前へ戻すだけ（コマンドは送られない）
                h.CaptureGizmoExtrude();
                h.FinishGizmoExtrude();
                return false;
            }
            // hover を凍結＋開始時クリア（回転・拡大縮小の状態と同じ）
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _edgeExtrudeDrag = kind;
            return true;
        }

        /// <summary>一時停止中：回転・拡大縮小の状態と同じ普通のギズモ操作を始める。</summary>
        private bool BeginPlainGizmo(EdgeExtrudeDrag kind, System.Func<bool> beginGizmo)
        {
            if (!beginGizmo()) return false;
            _viewportManager.EnterVerticesMoved(ActiveProject, VerticesMovedPhase.DragBegin);
            _edgeExtrudeDrag = kind;
            return true;
        }
    }
}
