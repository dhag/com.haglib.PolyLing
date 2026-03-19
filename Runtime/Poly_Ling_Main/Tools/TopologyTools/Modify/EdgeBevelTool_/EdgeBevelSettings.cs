// Assets/Editor/Poly_Ling/Tools/Settings/EdgeBevelSettings.cs
// EdgeBevelTool用の設定クラス

using System;
using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// EdgeBevelToolの設定
    /// </summary>
    [Serializable]
    public class EdgeBevelSettings : IToolSettings
    {
        [SerializeField] private float _amount = 0.1f;
        [SerializeField] private int _segments = 1;
        [SerializeField] private bool _fillet = true;

        public float Amount
        {
            get => _amount;
            set => _amount = Mathf.Max(0.001f, value);
        }

        public int Segments
        {
            get => _segments;
            set => _segments = Mathf.Clamp(value, 1, 10);
        }

        public bool Fillet
        {
            get => _fillet;
            set => _fillet = value;
        }

        public EdgeBevelSettings() { }

        public EdgeBevelSettings(float amount, int segments, bool fillet)
        {
            _amount = Mathf.Max(0.001f, amount);
            _segments = Mathf.Clamp(segments, 1, 10);
            _fillet = fillet;
        }

        public IToolSettings Clone()
        {
            return new EdgeBevelSettings(_amount, _segments, _fillet);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is EdgeBevelSettings src)
            {
                _amount = src._amount;
                _segments = src._segments;
                _fillet = src._fillet;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is EdgeBevelSettings src)
            {
                return !Mathf.Approximately(_amount, src._amount)
                    || _segments != src._segments
                    || _fillet != src._fillet;
            }
            return true;
        }
    }
}
