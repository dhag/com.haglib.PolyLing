using System;
using UnityEngine;

namespace Poly_Ling.Tools
{
    [Serializable]
    public class EdgeRibbonFaceSettings : ToolSettingsBase
    {
        [Min(0.000001f)]
        public float WidthWorld = 0.05f;

        public override IToolSettings Clone()
        {
            return new EdgeRibbonFaceSettings
            {
                WidthWorld = WidthWorld
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!(other is EdgeRibbonFaceSettings rhs))
                return true;

            return !Mathf.Approximately(WidthWorld, rhs.WidthWorld);
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (other is EdgeRibbonFaceSettings rhs)
                WidthWorld = rhs.WidthWorld;
        }
    }
}
