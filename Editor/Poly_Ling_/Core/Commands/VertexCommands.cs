// Assets/Editor/Poly_Ling_/Core/Commands/VertexCommands.cs
// 頂点操作のコマンド化

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// 頂点ドラッグ開始コマンド
    /// </summary>
    public class BeginVertexDragCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly Vector3[] _currentPositions;

        public string Description => "Begin Vertex Drag";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.None;

        public BeginVertexDragCommand(MeshUndoController controller, Vector3[] currentPositions = null)
        {
            _controller = controller;
            _currentPositions = currentPositions != null ? (Vector3[])currentPositions.Clone() : null;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_currentPositions != null)
            {
                _controller.BeginVertexDragInternal(_currentPositions);
            }
            else
            {
                _controller.BeginVertexDragInternal();
            }
        }
    }

    /// <summary>
    /// 頂点ドラッグ終了コマンド
    /// </summary>
    public class EndVertexDragCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly int[] _movedIndices;
        private readonly Vector3[] _newPositions;

        public string Description => "End Vertex Drag";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public EndVertexDragCommand(MeshUndoController controller, int[] movedIndices, Vector3[] newPositions = null)
        {
            _controller = controller;
            _movedIndices = movedIndices != null ? (int[])movedIndices.Clone() : null;
            _newPositions = newPositions != null ? (Vector3[])newPositions.Clone() : null;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_newPositions != null)
            {
                _controller.EndVertexDragInternal(_movedIndices, _newPositions);
            }
            else
            {
                _controller.EndVertexDragInternal(_movedIndices);
            }
        }
    }

    /// <summary>
    /// 頂点グループ移動記録コマンド
    /// </summary>
    public class RecordVertexGroupMoveCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly List<int>[] _groups;
        private readonly Vector3[] _oldOffsets;
        private readonly Vector3[] _newOffsets;
        private readonly Vector3[] _originalVertices;

        public string Description => "Record Vertex Group Move";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public RecordVertexGroupMoveCommand(
            MeshUndoController controller,
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            _controller = controller;
            // Deep copy
            _groups = groups != null ? CloneGroups(groups) : null;
            _oldOffsets = oldOffsets != null ? (Vector3[])oldOffsets.Clone() : null;
            _newOffsets = newOffsets != null ? (Vector3[])newOffsets.Clone() : null;
            _originalVertices = originalVertices != null ? (Vector3[])originalVertices.Clone() : null;
        }

        private static List<int>[] CloneGroups(List<int>[] src)
        {
            var dst = new List<int>[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                dst[i] = src[i] != null ? new List<int>(src[i]) : null;
            }
            return dst;
        }

        public void Execute()
        {
            _controller?.RecordVertexGroupMoveInternal(_groups, _oldOffsets, _newOffsets, _originalVertices);
        }
    }
}
