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
        [SerializeField] private int _segments = 1;

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

        /// <summary>
        /// 段数。1 辺から押し出す方向へ並べる四角形の数（はしご状）。最低 1。
        /// </summary>
        public int Segments
        {
            get => _segments;
            set => _segments = Mathf.Max(1, value);
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
            c._segments = _segments;
            return c;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is EdgeExtrudeSettings src)
            {
                _mode = src._mode;
                _snapToAxis = src._snapToAxis;
                _dragSensitivity = src._dragSensitivity;
                _segments = src._segments;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is EdgeExtrudeSettings src)
            {
                return _mode != src._mode
                    || _snapToAxis != src._snapToAxis
                    || !Mathf.Approximately(_dragSensitivity, src._dragSensitivity)
                    || _segments != src._segments;
            }
            return true;
        }
    }
}
