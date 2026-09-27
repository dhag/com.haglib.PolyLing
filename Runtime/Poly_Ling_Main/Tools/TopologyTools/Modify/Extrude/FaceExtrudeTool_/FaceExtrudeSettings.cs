// Assets/Editor/Poly_Ling/Tools/Settings/FaceExtrudeSettings.cs
// FaceExtrudeTool用の設定クラス

using System;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// FaceExtrudeToolの設定
    /// </summary>
    [Serializable]
    public class FaceExtrudeSettings : IToolSettings
    {
        /// <summary>
        /// 押し出しタイプ
        /// </summary>
        public enum ExtrudeType
        {
            Normal,
            Bevel
        }

        [SerializeField] private ExtrudeType _type = ExtrudeType.Normal;
        [SerializeField] private float _bevelScale = 0.8f;
        [SerializeField] private bool _individualNormals = false;
        [SerializeField] private float _dragSensitivity = 1f;
        [SerializeField] private int _segments = 1;

        public ExtrudeType Type
        {
            get => _type;
            set => _type = value;
        }

        public float BevelScale
        {
            get => _bevelScale;
            set => _bevelScale = Mathf.Clamp(value, ParameterLimits.GetF("FaceExtrude.BevelScale.Min"), ParameterLimits.GetF("FaceExtrude.BevelScale.Max"));
        }

        public bool IndividualNormals
        {
            get => _individualNormals;
            set => _individualNormals = value;
        }

        public float DragSensitivity
        {
            get => _dragSensitivity;
            set => _dragSensitivity = Mathf.Max(0.001f, value);
        }

        /// <summary>
        /// 段数。押し出す方向へ重ねる層の数（側面がはしご状になる）。最低 1。
        /// </summary>
        public int Segments
        {
            get => _segments;
            set => _segments = Mathf.Max(1, value);
        }

        public FaceExtrudeSettings() { }

        public FaceExtrudeSettings(ExtrudeType type, float bevelScale, bool individualNormals)
        {
            _type = type;
            _bevelScale = Mathf.Clamp(bevelScale, ParameterLimits.GetF("FaceExtrude.BevelScale.Min"), ParameterLimits.GetF("FaceExtrude.BevelScale.Max"));
            _individualNormals = individualNormals;
        }

        public IToolSettings Clone()
        {
            var c = new FaceExtrudeSettings(_type, _bevelScale, _individualNormals);
            c._dragSensitivity = _dragSensitivity;
            c._segments = _segments;
            return c;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is FaceExtrudeSettings src)
            {
                _type = src._type;
                _bevelScale = src._bevelScale;
                _individualNormals = src._individualNormals;
                _dragSensitivity = src._dragSensitivity;
                _segments = src._segments;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is FaceExtrudeSettings src)
            {
                return _type != src._type
                    || !Mathf.Approximately(_bevelScale, src._bevelScale)
                    || _individualNormals != src._individualNormals
                    || !Mathf.Approximately(_dragSensitivity, src._dragSensitivity)
                    || _segments != src._segments;
            }
            return true;
        }
    }
}
