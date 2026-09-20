// PlayerVertexInteractor.cs
// PlayerMouseDispatcher の左ボタンイベントを購読し、
// ヒットテストを実行して IPlayerToolHandler に委譲する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 左ボタン操作（頂点選択・頂点移動など）を担うクラス。
    /// モード別の処理は <see cref="IPlayerToolHandler"/> に委譲する。
    ///
    /// 【ヒットテストについて】
    ///   CPU による最近傍探索は使わない。
    ///   GPU ヒットテスト結果（UnifiedSystemAdapter.HoverVertexIndex 等）を
    ///   マウスダウン時に読み取る。
    ///   UpdateFrame がポインター移動のたびに呼ばれることで GPU が計算済み。
    /// </summary>
    public class PlayerVertexInteractor
    {
        // ================================================================
        // 外部注入（Viewer が設定する）
        // ================================================================

        /// <summary>
        /// GPU ホバー結果から PlayerHitResult を生成して返すコールバック。
        /// Viewer が PlayerViewportManager 経由で UnifiedSystemAdapter の
        /// HoverVertexIndex / GetLocalHoverVertexIndex を読んで実装する。
        /// CPU による頂点探索は行わない。
        /// </summary>
        public Func<PlayerHitResult> GetHoverHit;

        // ================================================================
        // 依存
        // ================================================================

        private readonly PlayerSelectionOps _selectionOps;
        private          IPlayerToolHandler  _toolHandler;
        private          IMouseEventSource _dispatcher;

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerVertexInteractor(PlayerSelectionOps selectionOps)
        {
            _selectionOps = selectionOps ?? throw new ArgumentNullException(nameof(selectionOps));
        }

        /// <summary>
        /// ツールハンドラーを切り替える。
        /// </summary>
        public void SetToolHandler(IPlayerToolHandler handler)
        {
            _toolHandler = handler;
        }

        public IPlayerToolHandler CurrentHandler => _toolHandler;

        // ================================================================
        // Dispatcher 接続 / 切断
        // ================================================================

        public void Connect(IMouseEventSource dispatcher)
        {
            if (_dispatcher != null) Disconnect(_dispatcher);
            _dispatcher = dispatcher;

            _dispatcher.OnClick      += OnClick;
            _dispatcher.OnButtonDown += OnButtonDown;
            _dispatcher.OnPressMove  += OnPressMove;
            _dispatcher.OnDragBegin  += OnDragBegin;
            _dispatcher.OnDrag       += OnDrag;
            _dispatcher.OnDragEnd    += OnDragEnd;
        }

        public void Disconnect(IMouseEventSource dispatcher)
        {
            if (dispatcher == null) return;
            dispatcher.OnClick      -= OnClick;
            dispatcher.OnButtonDown -= OnButtonDown;
            dispatcher.OnPressMove  -= OnPressMove;
            dispatcher.OnDragBegin  -= OnDragBegin;
            dispatcher.OnDrag       -= OnDrag;
            dispatcher.OnDragEnd    -= OnDragEnd;

            if (_dispatcher == dispatcher)
                _dispatcher = null;
        }

        // ================================================================
        // SelectionOps アクセス（ToolHandler から利用可）
        // ================================================================

        public PlayerSelectionOps SelectionOps => _selectionOps;

        // ================================================================
        // イベントハンドラー（左ボタンのみ委譲）
        // ================================================================

        /// <summary>
        /// 押下フェーズを扱うハンドラ。実装していないハンドラでは null になり、
        /// 押下時追従は行われず従来どおりしきい値超えで開始する。
        /// </summary>
        private IPlayerPressHandler PressHandler => _toolHandler as IPlayerPressHandler;

        // ================================================================
        // プレビュー中のロック（操作経路統一計画.md H-2）
        // ================================================================

        /// <summary>
        /// 左ボタン押下でツールの操作（押下時追従・ドラッグ）を始めてよいかを問い合わせる。
        /// 本体は選択中の描画オブジェクトの担当者判定とロック取得を行う。null なら常に許可。
        /// </summary>
        public Func<bool> TryBeginPreview;

        /// <summary>押下で始めた操作が終わったときに呼ぶ（ロックを外す）。</summary>
        public Action EndPreview;

        /// <summary>今の押下で操作を始めてよいと判定されたか。</summary>
        private bool _pressAllowed = true;

        /// <summary>今の押下で TryBeginPreview を呼んだか（EndPreview を対にする）。</summary>
        private bool _previewBegun;

        private void FinishPress()
        {
            if (_previewBegun) EndPreview?.Invoke();
            _previewBegun = false;
            _pressAllowed = true;
        }

        private void OnButtonDown(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;

            // 押下で始まる操作（押下時追従・ドラッグ）の前に担当者判定を行う。
            // 通らなければこの押下の追従とドラッグは止め、クリック（選択）は通す。
            _previewBegun = TryBeginPreview != null;
            _pressAllowed = TryBeginPreview?.Invoke() ?? true;
            if (!_pressAllowed) return;

            // 押下時点のホバーは「直前の PointerMove で GPU が確定した値」。
            // PointerDown ではホバーを再計算しない（再計算すると判定位置が飛ぶ）。
            var hit = GetHoverHit?.Invoke() ?? PlayerHitResult.Miss;
            PLDiag.PickRec("IA.ButtonDown",
                hit.HasHit ? 1 : 0, hit.MeshIndex, hit.VertexIndex,
                x: screenPos.x, y: screenPos.y);
            PressHandler?.OnLeftButtonDown(hit, screenPos, mods);
        }

        private void OnPressMove(int btn, Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null || !_pressAllowed) return;
            PressHandler?.OnLeftPressMove(screenPos, delta, mods);
        }

        private void OnClick(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;

            // しきい値を越えずに離された。押下時に開始していた操作を先に巻き戻す。
            if (_pressAllowed) PressHandler?.OnLeftPressCancel(screenPos, mods);
            FinishPress();

            // GPU が UpdateFrame で計算済みのホバー結果を読み取る。
            var hit = GetHoverHit?.Invoke() ?? PlayerHitResult.Miss;
            PLDiag.PickRec("IA.Click",
                hit.HasHit ? 1 : 0, hit.MeshIndex, hit.VertexIndex,
                x: screenPos.x, y: screenPos.y);
            if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
                Debug.Log($"[GizmoDbg/Click] screenPos={screenPos} handler={_toolHandler.GetType().Name}");
            _toolHandler.OnLeftClick(hit, screenPos, mods);
        }

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null || !_pressAllowed) return;
            // ドラッグ開始時も同様に GPU 計算済みのホバー結果を使う。
            var hit = GetHoverHit?.Invoke() ?? PlayerHitResult.Miss;
            // screenPos は押下位置 (_downPos)。ホバーは直前の OnPointerHover で
            // 「現在位置」で再計算されているため、両者は同一位置ではない。
            PLDiag.PickRec("IA.DragBegin",
                hit.HasHit ? 1 : 0, hit.MeshIndex, hit.VertexIndex,
                x: screenPos.x, y: screenPos.y);
            if (Poly_Ling.Tools.AxisGizmo.GizmoDebugLog)
                Debug.Log($"[GizmoDbg/DragBegin] screenPos={screenPos} handler={_toolHandler.GetType().Name}");
            _toolHandler.OnLeftDragBegin(hit, screenPos, mods);
        }

        private void OnDrag(int btn, Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null || !_pressAllowed) return;
            _toolHandler.OnLeftDrag(screenPos, delta, mods);
        }

        private void OnDragEnd(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            if (_pressAllowed) _toolHandler.OnLeftDragEnd(screenPos, mods);
            FinishPress();
        }
    }
}
