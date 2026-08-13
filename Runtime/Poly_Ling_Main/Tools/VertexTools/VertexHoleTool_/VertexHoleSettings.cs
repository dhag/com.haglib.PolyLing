// VertexHoleSettings.cs
// VertexHoleTool 用の設定クラス。

using System;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点に穴あけツールの設定。
    /// </summary>
    [Serializable]
    public class VertexHoleSettings : IToolSettings
    {
        [SerializeField] private float _ratio = 0.5f;

        /// <summary>
        /// 新頂点の位置比率。1.00 が指定頂点の位置、0 が根元（辺の反対側）の位置。
        /// </summary>
        public float Ratio
        {
            get => _ratio;
            set => _ratio = Mathf.Clamp(value,
                ParameterLimits.GetF("VertexHole.Ratio.Min"),
                ParameterLimits.GetF("VertexHole.Ratio.Max"));
        }

        public VertexHoleSettings() { }

        public IToolSettings Clone() => new VertexHoleSettings { _ratio = _ratio };

        public void CopyFrom(IToolSettings other)
        {
            if (other is VertexHoleSettings s) _ratio = s._ratio;
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is VertexHoleSettings s) return !Mathf.Approximately(_ratio, s._ratio);
            return true;
        }
    }
}
