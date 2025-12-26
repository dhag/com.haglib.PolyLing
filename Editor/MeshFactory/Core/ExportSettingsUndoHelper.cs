// Assets/Editor/UndoSystem/MeshEditor/MeshFactoryUndoController_ExportSettings.cs
// MeshFactoryUndoControllerにExportSettingsスタックを追加する拡張
// partial classとして既存のMeshFactoryUndoControllerを拡張

using System;
using UnityEngine;
using MeshFactory.Tools;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// MeshFactoryUndoControllerのExportSettings拡張
    /// 既存のコントローラーにExportSettingsスタックを追加
    /// </summary>


    /// <summary>
    /// ExportSettings統合ヘルパー
    /// 既存のMeshFactoryUndoControllerと組み合わせて使用
    /// </summary>
    public class ExportSettingsUndoHelper : IDisposable
    {
        private readonly UndoStack<ExportSettings> _stack;
        private readonly ExportSettings _settings;
        private readonly Action _onUndoRedo;

        // イベントハンドラ保持（解除用）
        private Action<ExportSettingsSnapshot, ExportSettingsSnapshot, string> _changeHandler;
        private Action<UndoOperationInfo> _undoHandler;
        private Action<UndoOperationInfo> _redoHandler;

        // ドラッグ用
        private bool _isDragging;
        private ExportSettingsSnapshot _dragStartSnapshot;

        /// <summary>
        /// ExportSettings
        /// </summary>
        public ExportSettings Settings => _settings;

        /// <summary>
        /// Undoスタック
        /// </summary>
        public UndoStack<ExportSettings> Stack => _stack;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="parentGroup">親となるUndoGroup（通常はMeshFactoryUndoController.MainGroup）</param>
        /// <param name="windowId">ウィンドウID</param>
        /// <param name="onUndoRedo">Undo/Redo実行後のコールバック</param>
        public ExportSettingsUndoHelper(
            UndoGroup parentGroup,
            string windowId,
            Action onUndoRedo = null)
        {
            _onUndoRedo = onUndoRedo;
            _settings = new ExportSettings();

            // スタック作成
            _stack = new UndoStack<ExportSettings>(
                $"{windowId}/ExportSettings",
                "Export Settings",
                _settings
            );
            parentGroup.AddChild(_stack);

            // イベントハンドラ登録
            SetupEventHandlers();
        }

        /// <summary>
        /// 既存のUndoManagerに直接追加する場合
        /// </summary>
        public ExportSettingsUndoHelper(string stackId, Action onUndoRedo = null)
        {
            _onUndoRedo = onUndoRedo;
            _settings = new ExportSettings();

            // スタック作成（ルートに追加）
            _stack = new UndoStack<ExportSettings>(
                stackId,
                "Export Settings",
                _settings
            );
            UndoManager.Instance.AddChild(_stack);

            // イベントハンドラ登録
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            // UI変更時（OnChangedのみ自動処理、Reset/FromSelectionは呼び出し側で処理）
            _changeHandler = OnSettingsChanged;
            ExportSettingsUI.OnChanged += _changeHandler;

            // Undo/Redo実行時
            _undoHandler = _ => OnUndoRedoPerformed();
            _redoHandler = _ => OnUndoRedoPerformed();
            _stack.OnUndoPerformed += _undoHandler;
            _stack.OnRedoPerformed += _redoHandler;
        }

        private void OnSettingsChanged(
            ExportSettingsSnapshot before,
            ExportSettingsSnapshot after,
            string description)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new ExportSettingsChangeRecord(before, after, description);
            _stack.Record(record, description);
        }

        private void OnUndoRedoPerformed()
        {
            _onUndoRedo?.Invoke();
        }

        /// <summary>
        /// ドラッグ開始
        /// </summary>
        public void BeginDrag()
        {
            if (_isDragging) return;
            _isDragging = true;
            _dragStartSnapshot = _settings.CreateSnapshot();
        }

        /// <summary>
        /// ドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndDrag(string description = "Change Export Settings")
        {
            if (!_isDragging) return;
            _isDragging = false;

            ExportSettingsSnapshot currentSnapshot = _settings.CreateSnapshot();
            if (_dragStartSnapshot.IsDifferentFrom(currentSnapshot))
            {
                var record = new ExportSettingsChangeRecord(
                    _dragStartSnapshot, currentSnapshot, description);
                _stack.Record(record, description);
            }
        }

        /// <summary>
        /// 即座に記録
        /// </summary>
        public void RecordChange(
            ExportSettingsSnapshot before,
            ExportSettingsSnapshot after,
            string description = null)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new ExportSettingsChangeRecord(before, after, description);
            _stack.Record(record, description ?? "Change Export Settings");
        }

        /// <summary>
        /// クリーンアップ
        /// </summary>
        public void Dispose()
        {
            // イベントハンドラ解除
            if (_changeHandler != null)
                ExportSettingsUI.OnChanged -= _changeHandler;

            if (_stack != null)
            {
                if (_undoHandler != null)
                    _stack.OnUndoPerformed -= _undoHandler;
                if (_redoHandler != null)
                    _stack.OnRedoPerformed -= _redoHandler;

                // 親から削除
                UndoManager.Instance.RemoveChild(_stack);
            }
        }
    }
}