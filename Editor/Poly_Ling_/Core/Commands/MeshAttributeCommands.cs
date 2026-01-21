// Assets/Editor/Poly_Ling_/Core/Commands/MeshAttributeCommands.cs
// メッシュ属性変更のコマンド化

using System;
using System.Collections.Generic;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// メッシュ属性変更コマンド
    /// 可視性、ロック、ミラー、名前等の属性変更を処理
    /// </summary>
    public class UpdateMeshAttributesCommand : ICommand
    {
        private readonly IList<MeshAttributeChange> _changes;
        private readonly Action<IList<MeshAttributeChange>> _handler;

        public string Description => $"Update Mesh Attributes ({_changes?.Count ?? 0} changes)";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        public UpdateMeshAttributesCommand(
            IList<MeshAttributeChange> changes,
            Action<IList<MeshAttributeChange>> handler)
        {
            // 変更データをコピー（後から変更されないように）
            _changes = changes != null ? new List<MeshAttributeChange>(changes) : new List<MeshAttributeChange>();
            _handler = handler;
        }

        public void Execute()
        {
            _handler?.Invoke(_changes);
        }
    }
}
