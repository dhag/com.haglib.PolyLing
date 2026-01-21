// Assets/Editor/Poly_Ling_/Core/Commands/CameraCommands.cs
// カメラ操作のコマンド化

using System;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Tools;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// カメラ変更記録コマンド
    /// ドラッグ終了時にUndo記録を行う
    /// </summary>
    public class RecordCameraChangeCommand : ICommand
    {
        private readonly MeshUndoController _controller;
        private readonly float _oldRotX, _oldRotY, _oldDist;
        private readonly Vector3 _oldTarget;
        private readonly float _newRotX, _newRotY, _newDist;
        private readonly Vector3 _newTarget;
        private readonly WorkPlaneSnapshot? _oldWorkPlane;
        private readonly WorkPlaneSnapshot? _newWorkPlane;

        public string Description => "Record Camera Change";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.None;

        public RecordCameraChangeCommand(
            MeshUndoController controller,
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            _controller = controller;
            _oldRotX = oldRotX;
            _oldRotY = oldRotY;
            _oldDist = oldDist;
            _oldTarget = oldTarget;
            _newRotX = newRotX;
            _newRotY = newRotY;
            _newDist = newDist;
            _newTarget = newTarget;
            _oldWorkPlane = oldWorkPlane;
            _newWorkPlane = newWorkPlane;
        }

        public void Execute()
        {
            if (_controller == null) return;

            if (_oldWorkPlane.HasValue || _newWorkPlane.HasValue)
            {
                _controller.RecordViewChangeWithWorkPlaneInternal(
                    _oldRotX, _oldRotY, _oldDist, _oldTarget,
                    _newRotX, _newRotY, _newDist, _newTarget,
                    _oldWorkPlane, _newWorkPlane);
            }
            else
            {
                _controller.RecordViewChangeInternal(
                    _oldRotX, _oldRotY, _oldDist, _oldTarget,
                    _newRotX, _newRotY, _newDist, _newTarget);
            }
        }
    }
}
