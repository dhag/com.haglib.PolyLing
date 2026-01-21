// Assets/Editor/Poly_Ling_/Core/Commands/MeshListCommands.cs
// メッシュリスト操作のコマンド化
// 追加・削除・選択・複製・順序変更・クリア・置換

using System;
using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// メッシュコンテキスト追加コマンド（単体）
    /// </summary>
    public class AddMeshContextCommand : ICommand
    {
        private readonly MeshContext _meshContext;
        private readonly Action<MeshContext> _handler;

        public string Description => $"Add Mesh: {_meshContext?.Name ?? "null"}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public AddMeshContextCommand(MeshContext meshContext, Action<MeshContext> handler)
        {
            _meshContext = meshContext;
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_meshContext);
        }
    }

    /// <summary>
    /// メッシュコンテキスト追加コマンド（複数・バッチ）
    /// </summary>
    public class AddMeshContextsCommand : ICommand
    {
        private readonly IList<MeshContext> _meshContexts;
        private readonly Action<IList<MeshContext>> _handler;

        public string Description => $"Add Meshes: {_meshContexts?.Count ?? 0} items";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public AddMeshContextsCommand(IList<MeshContext> meshContexts, Action<IList<MeshContext>> handler)
        {
            // コピーして保持
            _meshContexts = meshContexts != null ? new List<MeshContext>(meshContexts) : new List<MeshContext>();
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_meshContexts);
        }
    }

    /// <summary>
    /// メッシュコンテキスト削除コマンド
    /// </summary>
    public class RemoveMeshContextCommand : ICommand
    {
        private readonly int _index;
        private readonly Action<int> _handler;

        public string Description => $"Remove Mesh at index {_index}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public RemoveMeshContextCommand(int index, Action<int> handler)
        {
            _index = index;
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_index);
        }
    }

    /// <summary>
    /// メッシュ選択コマンド
    /// </summary>
    public class SelectMeshContextCommand : ICommand
    {
        private readonly int _index;
        private readonly Action<int> _handler;

        public string Description => $"Select Mesh at index {_index}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Selection;

        public SelectMeshContextCommand(int index, Action<int> handler)
        {
            _index = index;
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_index);
        }
    }

    /// <summary>
    /// メッシュ複製コマンド
    /// </summary>
    public class DuplicateMeshContentCommand : ICommand
    {
        private readonly int _index;
        private readonly Action<int> _handler;

        public string Description => $"Duplicate Mesh at index {_index}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public DuplicateMeshContentCommand(int index, Action<int> handler)
        {
            _index = index;
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_index);
        }
    }

    /// <summary>
    /// メッシュ順序変更コマンド
    /// </summary>
    public class ReorderMeshContextCommand : ICommand
    {
        private readonly int _fromIndex;
        private readonly int _toIndex;
        private readonly Action<int, int> _handler;

        public string Description => $"Reorder Mesh: {_fromIndex} -> {_toIndex}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public ReorderMeshContextCommand(int fromIndex, int toIndex, Action<int, int> handler)
        {
            _fromIndex = fromIndex;
            _toIndex = toIndex;
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_fromIndex, _toIndex);
        }
    }

    /// <summary>
    /// 全メッシュクリアコマンド
    /// </summary>
    public class ClearAllMeshContextsCommand : ICommand
    {
        private readonly Action _handler;

        public string Description => "Clear All Meshes";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public ClearAllMeshContextsCommand(Action handler)
        {
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke();
        }
    }

    /// <summary>
    /// 全メッシュ置換コマンド
    /// </summary>
    public class ReplaceAllMeshContextsCommand : ICommand
    {
        private readonly IList<MeshContext> _meshContexts;
        private readonly Action<IList<MeshContext>> _handler;

        public string Description => $"Replace All Meshes: {_meshContexts?.Count ?? 0} items";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public ReplaceAllMeshContextsCommand(IList<MeshContext> meshContexts, Action<IList<MeshContext>> handler)
        {
            // コピーして保持
            _meshContexts = meshContexts != null ? new List<MeshContext>(meshContexts) : new List<MeshContext>();
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_meshContexts);
        }
    }
}
