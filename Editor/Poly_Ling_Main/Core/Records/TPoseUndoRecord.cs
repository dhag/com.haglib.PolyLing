// TPoseUndoRecord.cs
// Tポーズ変換のUndo/Redo記録

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Records
{
    /// <summary>
    /// Tポーズ変換のUndo/Redo記録
    /// ボーン回転・WorldMatrix・BindPose・頂点座標を全て保存/復元
    /// </summary>
    public class TPoseUndoRecord : IUndoRecord<ModelContext>
    {
        private readonly TPoseBackup _beforeState;
        private readonly TPoseBackup _afterState;
        private readonly TPoseBackup _oldTPoseBackup;
        private readonly TPoseBackup _newTPoseBackup;

        public UndoOperationInfo Info { get; set; }

        public TPoseUndoRecord(
            TPoseBackup beforeState,
            TPoseBackup afterState,
            TPoseBackup oldTPoseBackup,
            TPoseBackup newTPoseBackup,
            string description = "T-Pose")
        {
            _beforeState = beforeState;
            _afterState = afterState;
            _oldTPoseBackup = oldTPoseBackup;
            _newTPoseBackup = newTPoseBackup;
            Info = new UndoOperationInfo(description, "TPose");
        }

        public void Undo(ModelContext context)
        {
            if (context == null) return;
            TPoseConverter.RestoreFromBackup(context.MeshContextList, _beforeState);
            context.TPoseBackup = _oldTPoseBackup;
            context.OnListChanged?.Invoke();
        }

        public void Redo(ModelContext context)
        {
            if (context == null) return;
            TPoseConverter.RestoreFromBackup(context.MeshContextList, _afterState);
            context.TPoseBackup = _newTPoseBackup;
            context.OnListChanged?.Invoke();
        }
    }
}
