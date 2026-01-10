// Assets/Editor/MeshFactory/Tools/Settings/FaceExtrudeSettings.cs
// FaceExtrudeTool用の設定クラス

using System;
using UnityEngine;

namespace MeshFactory.Tools
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

        public ExtrudeType Type
        {
            get => _type;
            set => _type = value;
        }

        public float BevelScale
        {
            get => _bevelScale;
            set => _bevelScale = Mathf.Clamp(value, 0.01f, 1f);
        }

        public bool IndividualNormals
        {
            get => _individualNormals;
            set => _individualNormals = value;
        }

        public FaceExtrudeSettings() { }

        public FaceExtrudeSettings(ExtrudeType type, float bevelScale, bool individualNormals)
        {
            _type = type;
            _bevelScale = Mathf.Clamp(bevelScale, 0.01f, 1f);
            _individualNormals = individualNormals;
        }

        public IToolSettings Clone()
        {
            return new FaceExtrudeSettings(_type, _bevelScale, _individualNormals);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is FaceExtrudeSettings src)
            {
                _type = src._type;
                _bevelScale = src._bevelScale;
                _individualNormals = src._individualNormals;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is FaceExtrudeSettings src)
            {
                return _type != src._type
                    || !Mathf.Approximately(_bevelScale, src._bevelScale)
                    || _individualNormals != src._individualNormals;
            }
            return true;
        }
    }
}
