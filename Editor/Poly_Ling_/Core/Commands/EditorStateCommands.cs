// Assets/Editor/Poly_Ling_/Core/Commands/EditorStateCommands.cs
// エディタ状態（表示設定等）変更のコマンド化

using System;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// エディタ状態変更記録コマンド
    /// 表示設定等のUndo記録を行う
    /// </summary>
    public class RecordEditorStateChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly EditorStateSnapshot _beforeSnapshot;
        private readonly EditorStateSnapshot _afterSnapshot;
        private readonly string _description;

        public string Description => $"Record Editor State: {_description}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.None;

        public RecordEditorStateChangeCommand(
            MeshUndoController controller,
            EditorStateSnapshot before,
            EditorStateSnapshot after,
            string description = "Change Editor State")
        {
            _controller = controller;
            _beforeSnapshot = before;
            _afterSnapshot = after;
            _description = description;
        }

        public void Execute()
        {
            _controller?.RecordEditorStateChangeInternal(_beforeSnapshot, _afterSnapshot, _description);
        }
    }
}
