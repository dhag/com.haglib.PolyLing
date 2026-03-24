// PlayerVertexInteractor.cs
// PlayerMouseDispatcher の左ボタンイベントを購読し、
// ヒットテストを実行して IPlayerToolHandler に委譲する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 左ボタン操作（頂点選択・頂点移動など）を担うクラス。
    /// <para>
    /// モード別の処理は <see cref="IPlayerToolHandler"/> に委譲する。
    /// ヒットテストコールバック（<see cref="HitTest"/>）は Viewer が設定する。
    /// </para>
    /// </summary>
    public class PlayerVertexInteractor
    {
        // ================================================================
        // 外部注入（Viewer が設定する）
        // ================================================================

        /// <summary>
        /// スクリーン座標 → ヒットテスト結果。
        /// Viewer が Camera を使って実装する。
        /// </summary>
        public Func<Vector2, PlayerHitResult> HitTest;

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
            _dispatcher.OnDragBegin  += OnDragBegin;
            _dispatcher.OnDrag       += OnDrag;
            _dispatcher.OnDragEnd    += OnDragEnd;
        }

        public void Disconnect(IMouseEventSource dispatcher)
        {
            if (dispatcher == null) return;
            dispatcher.OnClick     -= OnClick;
            dispatcher.OnDragBegin -= OnDragBegin;
            dispatcher.OnDrag      -= OnDrag;
            dispatcher.OnDragEnd   -= OnDragEnd;

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

        private void OnClick(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            var hit = HitTest?.Invoke(screenPos) ?? PlayerHitResult.Miss;
            _toolHandler.OnLeftClick(hit, screenPos, mods);
        }

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            var hit = HitTest?.Invoke(screenPos) ?? PlayerHitResult.Miss;
            _toolHandler.OnLeftDragBegin(hit, screenPos, mods);
        }

        private void OnDrag(int btn, Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            _toolHandler.OnLeftDrag(screenPos, delta, mods);
        }

        private void OnDragEnd(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            _toolHandler.OnLeftDragEnd(screenPos, mods);
        }
    }
}
