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
            // GPU が UpdateFrame で計算済みのホバー結果を読み取る。
            // マウスダウン時点の HoverVertexIndex が確定値。
            var hit = GetHoverHit?.Invoke() ?? PlayerHitResult.Miss;
            _toolHandler.OnLeftClick(hit, screenPos, mods);
        }

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 0 || _toolHandler == null) return;
            // ドラッグ開始時も同様に GPU 計算済みのホバー結果を使う。
            var hit = GetHoverHit?.Invoke() ?? PlayerHitResult.Miss;
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
