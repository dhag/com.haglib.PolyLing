// PlayerShortcutController.cs
// Player 側キーボードショートカットの実行器。
//
//   1. UI ルートに KeyDownEvent(TrickleDown) を 1 つだけ登録する。
//   2. テキスト入力欄フォーカス中はショートカットを発火させない
//      (既存の TextField / 数値フィールドへの入力と衝突させない)。
//   3. ShortcutMap でキー組 → コマンドID を引き、登録済み Action を実行する。
//
// コマンドID → Action の割当は ViewerCore 側で Register する
// (コマンドの実体がそこにあるため、対応表と実行を分離している)。
//
// Runtime/Poly_Ling_Player/View/Core/Shortcut/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerShortcutController
    {
        private readonly Dictionary<string, Action> _commands = new();
        private readonly ShortcutMap _map;

        private VisualElement _root;
        private bool          _attached;

        // 2キー連続用: 1キー目(プレフィックス)を押した状態を保持する。
        private ShortcutBinding? _pendingFirst;

        public ShortcutMap Map => _map;

        public PlayerShortcutController(ShortcutMap map)
        {
            _map = map ?? ShortcutMap.CreateDefault();
        }

        /// <summary>コマンドID に実行内容を割り当てる。</summary>
        public void Register(string commandId, Action action)
        {
            if (string.IsNullOrEmpty(commandId) || action == null) return;
            _commands[commandId] = action;
        }

        /// <summary>UI ルートへ接続する。二重接続は防止する。</summary>
        public void Attach(VisualElement root)
        {
            if (_attached || root == null) return;
            _root = root;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached || _root == null) return;
            _root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _attached = false;
            _root = null;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // ★★★ 一時診断ログ（切り分け用。確定後に削除する）★★★
            // この行が出ない → KeyDownEvent がパネルへ配送されていない
            //                  （ランタイムのキー配送 / フォーカスの問題）。
            var focused = _root?.panel?.focusController?.focusedElement as VisualElement;
            Debug.Log(
                $"[Shortcut/diag] OnKeyDown key={evt.keyCode} " +
                $"ctrl={(evt.ctrlKey || evt.commandKey)} shift={evt.shiftKey} alt={evt.altKey} " +
                $"focused={(focused == null ? "null" : focused.GetType().Name)} " +
                $"target={((evt.target as VisualElement)?.GetType().Name ?? "null")}");

            // 修飾のみ (キー本体なし) は無視。
            if (evt.keyCode == KeyCode.None) return;

            // Escape はプレフィックス待ちの解除のみ行う (他へは干渉しない)。
            if (evt.keyCode == KeyCode.Escape)
            {
                if (_pendingFirst.HasValue)
                {
                    _pendingFirst = null;
                    Debug.Log("[Shortcut/diag] prefix canceled"); // ★一時
                }
                return;
            }

            // テキスト編集中はショートカットを発火させない (待ちも解除)。
            if (IsTextEditing(_root))
            {
                _pendingFirst = null;
                Debug.Log("[Shortcut/diag] blocked: IsTextEditing"); // ★一時
                return;
            }

            // Ctrl は Mac の Command も同一視する (MeshUndoController と同方針)。
            var binding = new ShortcutBinding(
                evt.keyCode,
                evt.ctrlKey || evt.commandKey,
                evt.shiftKey,
                evt.altKey);

            // (1) プレフィックス待ち中: (1キー目, 今回) で連続を検索。
            if (_pendingFirst.HasValue)
            {
                var first = _pendingFirst.Value;
                _pendingFirst = null;
                if (_map.TryGetSequence(first, binding, out string seqCmd))
                {
                    InvokeCommand(seqCmd, $"{first} {binding}", evt);
                    return;
                }
                // 連続が無ければ待ちを捨て、今回キーを単発として再判定する。
                Debug.Log($"[Shortcut/diag] no sequence for {first} {binding}, fall back to single"); // ★一時
            }

            // (2) 今回キーが連続の 1キー目なら保持して次キーを待つ。
            if (_map.IsPrefix(binding))
            {
                _pendingFirst = binding;
                Debug.Log($"[Shortcut/diag] prefix pending: {binding}"); // ★一時
                evt.StopPropagation();
                return;
            }

            // (3) 単発。
            if (_map.TryGet(binding, out string cmd))
            {
                InvokeCommand(cmd, binding.ToString(), evt);
                return;
            }

            Debug.Log($"[Shortcut/diag] no map entry for {binding}"); // ★一時
        }

        private void InvokeCommand(string commandId, string label, KeyDownEvent evt)
        {
            if (!_commands.TryGetValue(commandId, out var action))
            {
                Debug.Log($"[Shortcut/diag] no command registered: {commandId}"); // ★一時
                return;
            }
            Debug.Log($"[Shortcut/diag] invoke {label} -> {commandId}"); // ★一時
            action.Invoke();
            evt.StopPropagation();
        }

        // フォーカス中の要素から親を辿り、テキスト編集系フィールドがあれば
        // 「入力中」とみなす。数値フィールドは内部の入力要素にフォーカスが載り、
        // 親チェーンに各 Field 型が現れるためそこで判定する。
        private static bool IsTextEditing(VisualElement root)
        {
            var f = root?.panel?.focusController?.focusedElement as VisualElement;
            while (f != null)
            {
                if (f is TextField
                 || f is IntegerField
                 || f is FloatField
                 || f is DoubleField
                 || f is LongField)
                    return true;
                f = f.parent;
            }
            return false;
        }
    }
}
