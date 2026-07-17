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
        /// <summary>一意分割（辺1クリックでベルト/ループ全体を切断）。</summary>
        BeltLoop,
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

        /// <summary>等分割オン（各モードで N 等分する）。オフは自由比率1本。</summary>
        public bool EqualDivide = false;

        /// <summary>等分割の分割ピース数（≥2）。EqualDivide=true のとき使用。</summary>
        public int Divisions = 2;

        public IToolSettings Clone() => new KnifeSettings { Mode = this.Mode, EqualDivide = this.EqualDivide, Divisions = this.Divisions };

        public void CopyFrom(IToolSettings other)
        {
            if (other is KnifeSettings src) { Mode = src.Mode; EqualDivide = src.EqualDivide; Divisions = src.Divisions; }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is KnifeSettings src) return Mode != src.Mode || EqualDivide != src.EqualDivide || Divisions != src.Divisions;
            return true;
        }
    }
}
