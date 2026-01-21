// Assets/Editor/Poly_Ling_/Core/Commands/UndoRedoCommands.cs
// Undo/Redo操作のコマンド化

using System;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// Undoコマンド
    /// </summary>
    public class UndoCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly Action _onCompleted;
        
        public string Description => "Undo";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public UndoCommand(MeshUndoController controller, Action onCompleted = null)
        {
            _controller = controller;
            _onCompleted = onCompleted;
        }

        public void Execute()
        {
            if (_controller != null && _controller.CanUndo)
            {
                _controller.UndoInternal();
                _onCompleted?.Invoke();
            }
        }
    }

    /// <summary>
    /// Redoコマンド
    /// </summary>
    public class RedoCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly Action _onCompleted;
        
        public string Description => "Redo";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public RedoCommand(MeshUndoController controller, Action onCompleted = null)
        {
            _controller = controller;
            _onCompleted = onCompleted;
        }

        public void Execute()
        {
            if (_controller != null && _controller.CanRedo)
            {
                _controller.RedoInternal();
                _onCompleted?.Invoke();
            }
        }
    }
}
