// Assets/Editor/Poly_Ling/Tools/Settings/EdgeExtrudeSettings.cs
// EdgeExtrudeTool用の設定クラス

using System;
using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// EdgeExtrudeToolの設定
    /// </summary>
    [Serializable]
    public class EdgeExtrudeSettings : IToolSettings
    {
        /// <summary>
        /// 押し出しモード
        /// </summary>
        public enum ExtrudeMode
        {
            ViewPlane,
            Normal,
            Free
        }

        [SerializeField] private ExtrudeMode _mode = ExtrudeMode.ViewPlane;
        [SerializeField] private bool _snapToAxis = false;
        [SerializeField] private float _dragSensitivity = 1f;

        public ExtrudeMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public bool SnapToAxis
        {
            get => _snapToAxis;
            set => _snapToAxis = value;
        }

        public float DragSensitivity
        {
            get => _dragSensitivity;
            set => _dragSensitivity = Mathf.Max(0.001f, value);
        }

        public EdgeExtrudeSettings() { }

        public EdgeExtrudeSettings(ExtrudeMode mode, bool snapToAxis)
        {
            _mode = mode;
            _snapToAxis = snapToAxis;
        }

        public IToolSettings Clone()
        {
            var c = new EdgeExtrudeSettings(_mode, _snapToAxis);
            c._dragSensitivity = _dragSensitivity;
            return c;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is EdgeExtrudeSettings src)
            {
                _mode = src._mode;
                _snapToAxis = src._snapToAxis;
                _dragSensitivity = src._dragSensitivity;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is EdgeExtrudeSettings src)
            {
                return _mode != src._mode
                    || _snapToAxis != src._snapToAxis
                    || !Mathf.Approximately(_dragSensitivity, src._dragSensitivity);
            }
            return true;
        }
    }
}
