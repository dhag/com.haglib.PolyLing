// Assets/Editor/UndoSystem/MeshEditor/MeshFactoryUndoController_BoneTransform.cs
// MeshFactoryUndoControllerにBoneTransformスタックを追加する拡張
// partial classとして既存のMeshFactoryUndoControllerを拡張

using System;
using UnityEngine;
using MeshFactory.Tools;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// MeshFactoryUndoControllerのBoneTransform拡張
    /// 既存のコントローラーにBoneTransformスタックを追加
    /// </summary>


    /// <summary>
    /// BoneTransform統合ヘルパー
    /// 既存のMeshFactoryUndoControllerと組み合わせて使用
    /// </summary>
    public class BoneTransformUndoHelper : IDisposable
    {
        private readonly UndoStack<BoneTransform> _stack;
        private readonly BoneTransform _settings;
        private readonly Action _onUndoRedo;

        // イベントハンドラ保持（解除用）
        private Action<BoneTransformSnapshot, BoneTransformSnapshot, string> _changeHandler;
        private Action<UndoOperationInfo> _undoHandler;
        private Action<UndoOperationInfo> _redoHandler;

        // ドラッグ用
        private bool _isDragging;
        private BoneTransformSnapshot _dragStartSnapshot;

        /// <summary>
        /// BoneTransform
        /// </summary>
        public BoneTransform Settings => _settings;

        /// <summary>
        /// Undoスタック
        /// </summary>
        public UndoStack<BoneTransform> Stack => _stack;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="parentGroup">親となるUndoGroup（通常はMeshFactoryUndoController.MainGroup）</param>
        /// <param name="windowId">ウィンドウID</param>
        /// <param name="onUndoRedo">Undo/Redo実行後のコールバック</param>
        public BoneTransformUndoHelper(
            UndoGroup parentGroup,
            string windowId,
            Action onUndoRedo = null)
        {
            _onUndoRedo = onUndoRedo;
            _settings = new BoneTransform();

            // スタック作成
            _stack = new UndoStack<BoneTransform>(
                $"{windowId}/BoneTransform",
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
        public BoneTransformUndoHelper(string stackId, Action onUndoRedo = null)
        {
            _onUndoRedo = onUndoRedo;
            _settings = new BoneTransform();

            // スタック作成（ルートに追加）
            _stack = new UndoStack<BoneTransform>(
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
            BoneTransformUI.OnChanged += _changeHandler;

            // Undo/Redo実行時
            _undoHandler = _ => OnUndoRedoPerformed();
            _redoHandler = _ => OnUndoRedoPerformed();
            _stack.OnUndoPerformed += _undoHandler;
            _stack.OnRedoPerformed += _redoHandler;
        }

        private void OnSettingsChanged(
            BoneTransformSnapshot before,
            BoneTransformSnapshot after,
            string description)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new BoneTransformChangeRecord(before, after, description);
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

            BoneTransformSnapshot currentSnapshot = _settings.CreateSnapshot();
            if (_dragStartSnapshot.IsDifferentFrom(currentSnapshot))
            {
                var record = new BoneTransformChangeRecord(
                    _dragStartSnapshot, currentSnapshot, description);
                _stack.Record(record, description);
            }
        }

        /// <summary>
        /// 即座に記録
        /// </summary>
        public void RecordChange(
            BoneTransformSnapshot before,
            BoneTransformSnapshot after,
            string description = null)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new BoneTransformChangeRecord(before, after, description);
            _stack.Record(record, description ?? "Change Export Settings");
        }

        /// <summary>
        /// クリーンアップ
        /// </summary>
        public void Dispose()
        {
            // イベントハンドラ解除
            if (_changeHandler != null)
                BoneTransformUI.OnChanged -= _changeHandler;

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