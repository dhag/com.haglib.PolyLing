// Assets/Editor/Poly_Ling_/Core/Commands/TopologyCommands.cs
// トポロジー変更（メッシュ構造変更）のコマンド化

using System;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// トポロジー変更記録コマンド
    /// スカルプト、モーフ、頂点マージ、押し出し等で使用
    /// </summary>
    public class RecordTopologyChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly MeshObjectSnapshot _before;
        private readonly MeshObjectSnapshot _after;
        private readonly SelectionState _selectionState;
        private readonly string _description;

        public string Description => $"Topology: {_description}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <summary>
        /// トポロジー変更記録コマンド（基本版）
        /// </summary>
        public RecordTopologyChangeCommand(
            MeshUndoController controller,
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            string description = "Topology Change")
        {
            _controller = controller;
            _before = before;
            _after = after;
            _selectionState = null;
            _description = description;
        }

        /// <summary>
        /// トポロジー変更記録コマンド（選択状態付き）
        /// Edge/Line選択のUndo/Redoに必要
        /// </summary>
        public RecordTopologyChangeCommand(
            MeshUndoController controller,
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState,
            string description = "Topology Change")
        {
            _controller = controller;
            _before = before;
            _after = after;
            _selectionState = selectionState;
            _description = description;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_selectionState != null)
            {
                _controller.RecordTopologyChangeInternal(_before, _after, _selectionState, _description);
            }
            else
            {
                _controller.RecordTopologyChangeInternal(_before, _after, _description);
            }
        }
    }
}
