// SmoothEdgesSettings.cs
// SmoothEdgesTool 用の設定クラス。

using System;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 辺・線分の平滑化ツールの設定。
    /// </summary>
    [Serializable]
    public class SmoothEdgesSettings : IToolSettings
    {
        [SerializeField] private float _strength = 0.5f;
        [SerializeField] private int _iterations = 1;
        [SerializeField] private bool _fixEndpoints = true;
        [SerializeField] private bool _lockX = false;
        [SerializeField] private bool _lockY = false;
        [SerializeField] private bool _lockZ = false;

        /// <summary>
        /// 1 反復あたりの寄せ量。0 で変化なし、1 で隣接平均そのもの。
        /// </summary>
        public float Strength
        {
            get => _strength;
            set => _strength = Mathf.Clamp(value,
                ParameterLimits.GetF("SmoothEdges.Strength.Min"),
                ParameterLimits.GetF("SmoothEdges.Strength.Max"));
        }

        /// <summary>反復回数。</summary>
        public int Iterations
        {
            get => _iterations;
            set => _iterations = Mathf.Clamp(value,
                ParameterLimits.GetI("SmoothEdges.Iterations.Min"),
                ParameterLimits.GetI("SmoothEdges.Iterations.Max"));
        }

        /// <summary>
        /// true: 選択チェーン内で次数1の頂点（開始点・終了点）を動かさない。
        /// 閉ループには次数1の頂点が無いため影響しない。
        /// 分岐点（次数3以上）は本設定に関係なく移動対象。
        /// </summary>
        public bool FixEndpoints
        {
            get => _fixEndpoints;
            set => _fixEndpoints = value;
        }

        /// <summary>true: X 成分を動かさない。</summary>
        public bool LockX { get => _lockX; set => _lockX = value; }

        /// <summary>true: Y 成分を動かさない。</summary>
        public bool LockY { get => _lockY; set => _lockY = value; }

        /// <summary>true: Z 成分を動かさない。</summary>
        public bool LockZ { get => _lockZ; set => _lockZ = value; }

        public SmoothEdgesSettings() { }

        public IToolSettings Clone() => new SmoothEdgesSettings
        {
            _strength = _strength,
            _iterations = _iterations,
            _fixEndpoints = _fixEndpoints,
            _lockX = _lockX,
            _lockY = _lockY,
            _lockZ = _lockZ,
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is SmoothEdgesSettings s)
            {
                _strength = s._strength;
                _iterations = s._iterations;
                _fixEndpoints = s._fixEndpoints;
                _lockX = s._lockX;
                _lockY = s._lockY;
                _lockZ = s._lockZ;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SmoothEdgesSettings s)
            {
                return !Mathf.Approximately(_strength, s._strength)
                    || _iterations != s._iterations
                    || _fixEndpoints != s._fixEndpoints
                    || _lockX != s._lockX
                    || _lockY != s._lockY
                    || _lockZ != s._lockZ;
            }
            return true;
        }
    }
}
