// PlayerEditOps.cs
// プレイヤービルド用編集操作クラス。
// PolyLingCore（エディタ拡張依存）の代替として育てていく。
// UndoManager・MeshUndoController・CommandQueueを所有し、
// 全編集操作のエントリポイントとなる。

using System;
using UnityEngine;
using Poly_Ling.Commands;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// プレイヤービルド用編集操作クラス。
    /// PolyLingCoreの代替として全編集操作を担う予定。
    /// UndoManager・MeshUndoController・CommandQueueを所有する。
    /// </summary>
    public class PlayerEditOps : IDisposable
    {
        // ================================================================
        // 所有リソース
        // ================================================================

        private readonly UndoManager       _undoManager;
        private readonly MeshUndoController _undoController;
        private readonly CommandQueue       _commandQueue;

        private bool _disposed;

        // ================================================================
        // プロパティ
        // ================================================================

        public UndoManager        UndoManager     => _undoManager;
        public MeshUndoController UndoController  => _undoController;
        public CommandQueue       CommandQueue     => _commandQueue;

        public bool CanUndo => _undoManager.CanUndo;
        public bool CanRedo => _undoManager.CanRedo;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public PlayerEditOps(UndoManager undoManager)
        {
            _undoManager    = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
            _commandQueue   = new CommandQueue();
            _undoController = new MeshUndoController("PlayerEdit", _undoManager);
            _undoController.SetCommandQueue(_commandQueue);
        }

        // ================================================================
        // 毎フレーム処理（MonoBehaviour.Update から呼ぶ）
        // ================================================================

        public void Tick()
        {
            if (_commandQueue.Count > 0)
                _commandQueue.ProcessAll();

            _undoManager.ProcessAllQueues();
        }

        // ================================================================
        // Undo / Redo
        // ================================================================

        public void PerformUndo() => _undoManager.PerformUndo();
        public void PerformRedo() => _undoManager.PerformRedo();

        // ================================================================
        // 将来の編集操作はここに追加していく
        // ================================================================

        // ================================================================
        // Dispose
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _undoController?.Dispose();
            _commandQueue?.Clear();
        }
    }
}
