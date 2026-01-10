// Assets/Editor/MeshFactory/Tools/Settings/KnifeSettings.cs
// KnifeTool用設定クラス（IToolSettings対応）

namespace MeshFactory.Tools
{
    /// <summary>
    /// KnifeTool用設定
    /// </summary>
    public class KnifeSettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>ナイフモード (Cut/Vertex/Erase)</summary>
        public KnifeMode Mode = KnifeMode.Cut;

        /// <summary>辺選択モード（true: 2辺/2点指定, false: ドラッグ）</summary>
        public bool EdgeSelect = false;

        /// <summary>チェーンモード（常にtrue、互換性のため残す）</summary>
        public bool ChainMode = true;

        /// <summary>自動連続モード（true: 自動連続, false: 手動連続）</summary>
        public bool AutoChain = true;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new KnifeSettings
            {
                Mode = this.Mode,
                EdgeSelect = this.EdgeSelect,
                ChainMode = this.ChainMode,
                AutoChain = this.AutoChain
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is KnifeSettings src)
            {
                Mode = src.Mode;
                EdgeSelect = src.EdgeSelect;
                ChainMode = src.ChainMode;
                AutoChain = src.AutoChain;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is KnifeSettings src)
            {
                return Mode != src.Mode ||
                       EdgeSelect != src.EdgeSelect ||
                       ChainMode != src.ChainMode ||
                       AutoChain != src.AutoChain;
            }
            return true;
        }
    }
}
