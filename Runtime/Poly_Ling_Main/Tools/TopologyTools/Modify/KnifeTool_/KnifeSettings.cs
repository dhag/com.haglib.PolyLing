// Tools/TopologyTools/Modify/KnifeTool_/KnifeSettings.cs
// KnifeTool 用設定（IToolSettings対応）。
// 一新版: Mode = ラダー切断 / Erase の2つのみ。

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ナイフツールのモード。
    /// </summary>
    public enum KnifeMode
    {
        /// <summary>ラダー切断（開始頂点→セグメント→終了頂点、端点は既存頂点）。</summary>
        LadderCut,
        /// <summary>辺消去（共有辺で2面を統合）。</summary>
        Erase
    }

    /// <summary>
    /// KnifeTool 用設定。
    /// </summary>
    public class KnifeSettings : IToolSettings
    {
        /// <summary>ナイフモード。</summary>
        public KnifeMode Mode = KnifeMode.LadderCut;

        public IToolSettings Clone() => new KnifeSettings { Mode = this.Mode };

        public void CopyFrom(IToolSettings other)
        {
            if (other is KnifeSettings src) Mode = src.Mode;
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is KnifeSettings src) return Mode != src.Mode;
            return true;
        }
    }
}
