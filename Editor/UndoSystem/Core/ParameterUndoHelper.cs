// Assets/Editor/UndoSystem/Core/ParameterUndoHelper.cs
// パラメータ編集のUndo/Redoを簡単に実装するためのヘルパークラス

using System;
using UnityEngine;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// パラメータ編集用Undoヘルパー
    /// EditorWindowのパラメータ変更を簡単にUndo対応にする
    /// スタックはUndoManagerで永続保持され、ウィンドウを閉じても履歴が残る
    /// 再度開いた際は閉じる前の状態が復元される
    /// </summary>
    /// <typeparam name="TParams">パラメータの型（クラスまたは構造体）</typeparam>
    public class ParameterUndoHelper<TParams> where TParams : IEquatable<TParams>
    {
        private readonly string _stackId;
        private readonly string _displayName;
        private readonly Func<TParams> _captureParams;
        private readonly Action<TParams> _applyParams;
        private readonly Action _onUndoRedo;

        private UndoStack<ParamsContext> _undoStack;
        private ParamsContext _context;
        private TParams _editStartParams;
        private bool _isDragging = false;

        // イベントハンドラ（解除用に保持）
        private Action<UndoOperationInfo> _undoHandler;
        private Action<UndoOperationInfo> _redoHandler;

        // 内部コンテキスト
        private class ParamsContext
        {
            public TParams Current;
        }

        // Undo記録
        private class ParamsChangeRecord : IUndoRecord<ParamsContext>
        {
            public UndoOperationInfo Info { get; set; }
            public TParams Before;
            public TParams After;

            public void Undo(ParamsContext ctx) => ctx.Current = Before;
            public void Redo(ParamsContext ctx) => ctx.Current = After;
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="stackId">スタックID（一意な識別子）</param>
        /// <param name="displayName">表示名</param>
        /// <param name="captureParams">現在のパラメータを取得する関数</param>
        /// <param name="applyParams">パラメータを適用する関数</param>
        /// <param name="onUndoRedo">Undo/Redo実行後のコールバック（Repaint等）</param>
        public ParameterUndoHelper(
            string stackId,
            string displayName,
            Func<TParams> captureParams,
            Action<TParams> applyParams,
            Action onUndoRedo = null)
        {
            _stackId = stackId;
            _displayName = displayName;
            _captureParams = captureParams;
            _applyParams = applyParams;
            _onUndoRedo = onUndoRedo;

            Initialize();
        }

        private void Initialize()
        {
            // 既存スタックを探す
            var existingNode = UndoManager.Instance.FindById(_stackId);
            if (existingNode is UndoStack<ParamsContext> existingStack && existingStack.Context != null)
            {
                // 前回閉じた時の状態を復元
                var lastState = existingStack.Context.Current;
                if (lastState != null && !lastState.Equals(default(TParams)))
                {
                    _applyParams(lastState);
                    _onUndoRedo?.Invoke();
                }

                // コンテキストを更新（現在の状態をキャプチャ）
                _context = new ParamsContext { Current = _captureParams() };
                _undoStack = existingStack;
                _undoStack.Context = _context;
            }
            else
            {
                // 新規作成
                _context = new ParamsContext { Current = _captureParams() };
                _undoStack = new UndoStack<ParamsContext>(_stackId, _displayName, _context);
                UndoManager.Instance.AddChild(_undoStack);
            }

            // イベントハンドラを登録（解除用に保持）
            _undoHandler = OnUndoRedoPerformed;
            _redoHandler = OnUndoRedoPerformed;
            _undoStack.OnUndoPerformed += _undoHandler;
            _undoStack.OnRedoPerformed += _redoHandler;
        }

        private void OnUndoRedoPerformed(UndoOperationInfo info)
        {
            _applyParams(_context.Current);
            _onUndoRedo?.Invoke();
        }

        /// <summary>
        /// クリーンアップ（OnDisableで呼ぶ）
        /// スタックは削除せず、Contextも残す（次回復元用）
        /// </summary>
        public void Dispose()
        {
            if (_undoStack != null)
            {
                // イベントハンドラを解除
                _undoStack.OnUndoPerformed -= _undoHandler;
                _undoStack.OnRedoPerformed -= _redoHandler;

                // Contextは残す（次回Open時に復元するため）
                // _undoStack.toolContext = default;  ← しない
                _undoStack = null;
            }
            _undoHandler = null;
            _redoHandler = null;
        }

        /// <summary>
        /// Undo可能か
        /// </summary>
        public bool CanUndo => _undoStack?.CanUndo ?? false;

        /// <summary>
        /// Redo可能か
        /// </summary>
        public bool CanRedo => _undoStack?.CanRedo ?? false;

        /// <summary>
        /// Undo履歴数
        /// </summary>
        public int UndoCount => _undoStack?.UndoCount ?? 0;

        /// <summary>
        /// Redo履歴数
        /// </summary>
        public int RedoCount => _undoStack?.RedoCount ?? 0;

        /// <summary>
        /// Undoを実行
        /// </summary>
        public void PerformUndo() => _undoStack?.PerformUndo();

        /// <summary>
        /// Redoを実行
        /// </summary>
        public void PerformRedo() => _undoStack?.PerformRedo();

        /// <summary>
        /// 履歴をクリア
        /// </summary>
        public void ClearHistory() => _undoStack?.Clear();

        /// <summary>
        /// OnGUIの最初で呼ぶ - イベント処理とショートカット
        /// </summary>
        /// <param name="e">Event.current</param>
        /// <returns>イベントを消費した場合true</returns>
        public bool HandleGUIEvents(Event e)
        {
            // キーボードショートカット
            if (e.type == EventType.KeyDown)
            {
                bool ctrl = e.control || e.command;

                if (ctrl && e.keyCode == KeyCode.Z && !e.shift && CanUndo)
                {
                    PerformUndo();
                    e.Use();
                    return true;
                }
                if (((ctrl && e.keyCode == KeyCode.Y) || (ctrl && e.shift && e.keyCode == KeyCode.Z)) && CanRedo)
                {
                    PerformRedo();
                    e.Use();
                    return true;
                }
            }

            // ドラッグ開始検出
            if (e.type == EventType.MouseDown && !_isDragging)
            {
                _isDragging = true;
                _editStartParams = _captureParams();
            }

            // ドラッグ終了検出
            if (e.type == EventType.MouseUp && _isDragging)
            {
                _isDragging = false;
                RecordIfChanged();
            }

            return false;
        }

        /// <summary>
        /// 変更があればUndo記録（内部用）
        /// </summary>
        private void RecordIfChanged()
        {
            if (_editStartParams == null) return;

            var currentParams = _captureParams();
            if (!currentParams.Equals(_editStartParams))
            {
                _context.Current = currentParams;
                var record = new ParamsChangeRecord
                {
                    Before = _editStartParams,
                    After = currentParams
                };
                _undoStack?.Record(record, $"Change {_displayName}");
            }
            _editStartParams = default;
        }

        /// <summary>
        /// 即座にUndo記録（ボタン操作等で使用）
        /// </summary>
        /// <param name="description">操作の説明</param>
        public void RecordImmediate(string description = null)
        {
            var before = _context.Current;
            var after = _captureParams();

            if (!after.Equals(before))
            {
                _context.Current = after;
                var record = new ParamsChangeRecord
                {
                    Before = before,
                    After = after
                };
                _undoStack?.Record(record, description ?? $"Change {_displayName}");
            }
        }

        /// <summary>
        /// Undo/Redoボタンを描画
        /// </summary>
        public void DrawUndoRedoButtons()
        {
            GUILayout.BeginHorizontal();
            using (new UnityEditor.EditorGUI.DisabledScope(!CanUndo))
            {
                if (GUILayout.Button("Undo", GUILayout.Width(60)))
                    PerformUndo();
            }
            using (new UnityEditor.EditorGUI.DisabledScope(!CanRedo))
            {
                if (GUILayout.Button("Redo", GUILayout.Width(60)))
                    PerformRedo();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 履歴情報付きUndo/Redoボタンを描画
        /// </summary>
        public void DrawUndoRedoButtonsWithInfo()
        {
            GUILayout.BeginHorizontal();
            using (new UnityEditor.EditorGUI.DisabledScope(!CanUndo))
            {
                if (GUILayout.Button($"Undo ({UndoCount})", GUILayout.Width(80)))
                    PerformUndo();
            }
            using (new UnityEditor.EditorGUI.DisabledScope(!CanRedo))
            {
                if (GUILayout.Button($"Redo ({RedoCount})", GUILayout.Width(80)))
                    PerformRedo();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}