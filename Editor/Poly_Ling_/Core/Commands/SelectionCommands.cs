// Assets/Editor/Poly_Ling_/Core/Commands/SelectionCommands.cs
// 選択操作のコマンド化（頂点/面/辺/メッシュ）

using System;
using System.Collections.Generic;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// 頂点/面選択変更記録コマンド
    /// </summary>
    public class RecordSelectionChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly HashSet<int> _oldVertices;
        private readonly HashSet<int> _newVertices;
        private readonly HashSet<int> _oldFaces;
        private readonly HashSet<int> _newFaces;
        private readonly WorkPlaneSnapshot? _oldWorkPlane;
        private readonly WorkPlaneSnapshot? _newWorkPlane;

        public string Description => "Record Selection Change";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Selection;

        public RecordSelectionChangeCommand(
            MeshUndoController controller,
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _controller = controller;
            _oldVertices = oldVertices != null ? new HashSet<int>(oldVertices) : null;
            _newVertices = newVertices != null ? new HashSet<int>(newVertices) : null;
            _oldFaces = oldFaces != null ? new HashSet<int>(oldFaces) : null;
            _newFaces = newFaces != null ? new HashSet<int>(newFaces) : null;
            _oldWorkPlane = null;
            _newWorkPlane = null;
        }

        public RecordSelectionChangeCommand(
            MeshUndoController controller,
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _controller = controller;
            _oldVertices = oldVertices != null ? new HashSet<int>(oldVertices) : null;
            _newVertices = newVertices != null ? new HashSet<int>(newVertices) : null;
            _oldFaces = oldFaces != null ? new HashSet<int>(oldFaces) : null;
            _newFaces = newFaces != null ? new HashSet<int>(newFaces) : null;
            _oldWorkPlane = oldWorkPlane;
            _newWorkPlane = newWorkPlane;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_oldWorkPlane.HasValue || _newWorkPlane.HasValue)
            {
                _controller.RecordSelectionChangeWithWorkPlaneInternal(
                    _oldVertices, _newVertices,
                    _oldWorkPlane, _newWorkPlane,
                    _oldFaces, _newFaces);
            }
            else
            {
                _controller.RecordSelectionChangeInternal(_oldVertices, _newVertices, _oldFaces, _newFaces);
            }
        }
    }

    /// <summary>
    /// 拡張選択変更記録コマンド（Edge/Face/Line対応）
    /// </summary>
    public class RecordExtendedSelectionChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly SelectionSnapshot _oldSnapshot;
        private readonly SelectionSnapshot _newSnapshot;
        private readonly HashSet<int> _oldLegacyVertices;
        private readonly HashSet<int> _newLegacyVertices;
        private readonly WorkPlaneSnapshot? _oldWorkPlane;
        private readonly WorkPlaneSnapshot? _newWorkPlane;

        public string Description => "Record Extended Selection Change";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Selection;

        public RecordExtendedSelectionChangeCommand(
            MeshUndoController controller,
            SelectionSnapshot oldSnapshot,
            SelectionSnapshot newSnapshot,
            HashSet<int> oldLegacyVertices,
            HashSet<int> newLegacyVertices,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            _controller = controller;
            _oldSnapshot = oldSnapshot;
            _newSnapshot = newSnapshot;
            _oldLegacyVertices = oldLegacyVertices != null ? new HashSet<int>(oldLegacyVertices) : null;
            _newLegacyVertices = newLegacyVertices != null ? new HashSet<int>(newLegacyVertices) : null;
            _oldWorkPlane = oldWorkPlane;
            _newWorkPlane = newWorkPlane;
        }

        public void Execute()
        {
            _controller?.RecordExtendedSelectionChangeInternal(
                _oldSnapshot, _newSnapshot,
                _oldLegacyVertices, _newLegacyVertices,
                _oldWorkPlane, _newWorkPlane);
        }
    }

    /// <summary>
    /// メッシュ選択変更記録コマンド
    /// </summary>
    public class RecordMeshSelectionChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly int _oldIndex;
        private readonly int _newIndex;
        private readonly CameraSnapshot? _oldCamera;
        private readonly CameraSnapshot? _newCamera;

        public string Description => "Record Mesh Selection Change";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Selection;

        public RecordMeshSelectionChangeCommand(
            MeshUndoController controller,
            int oldIndex,
            int newIndex,
            CameraSnapshot? oldCamera = null,
            CameraSnapshot? newCamera = null)
        {
            _controller = controller;
            _oldIndex = oldIndex;
            _newIndex = newIndex;
            _oldCamera = oldCamera;
            _newCamera = newCamera;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_oldCamera.HasValue || _newCamera.HasValue)
            {
                _controller.RecordMeshSelectionChangeInternal(_oldIndex, _newIndex, _oldCamera, _newCamera);
            }
            else
            {
                _controller.RecordMeshSelectionChangeInternal(_oldIndex, _newIndex);
            }
        }
    }
}
