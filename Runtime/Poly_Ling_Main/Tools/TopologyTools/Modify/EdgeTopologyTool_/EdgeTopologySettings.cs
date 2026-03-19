// Assets/Editor/Poly_Ling/Tools/Settings/EdgeTopologySettings.cs
// EdgeTopologyTool用設定クラス（IToolSettings対応）

namespace Poly_Ling.Tools
{
    /// <summary>
    /// EdgeTopologyTool用設定
    /// </summary>
    public class EdgeTopologySettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>トポロジーモード (Flip/Split/Dissolve)</summary>
        public EdgeTopoMode Mode = EdgeTopoMode.Flip;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new EdgeTopologySettings
            {
                Mode = this.Mode
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is EdgeTopologySettings src)
            {
                Mode = src.Mode;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is EdgeTopologySettings src)
            {
                return Mode != src.Mode;
            }
            return true;
        }
    }
}
