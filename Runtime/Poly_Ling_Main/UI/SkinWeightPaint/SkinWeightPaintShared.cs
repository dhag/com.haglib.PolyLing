// SkinWeightPaintShared.cs
// スキンウェイトペイント共有定義
// SkinWeightPaintPanel / SkinWeightPaintPanelV2 / SkinWeightPaintTool から参照

namespace Poly_Ling.UI
{
    public enum SkinWeightPaintMode
    {
        Replace,
        Add,
        Scale,
        Smooth,
    }

    public enum BrushFalloff
    {
        Constant,
        Linear,
        Smooth,
    }

    /// <summary>
    /// SkinWeightPaintTool が参照するパネルインターフェース。
    /// SkinWeightPaintPanel (V1) と SkinWeightPaintPanelV2 の両方が実装する。
    /// </summary>
    public interface ISkinWeightPaintPanel
    {
        SkinWeightPaintMode CurrentPaintMode   { get; }
        float               CurrentBrushRadius { get; }
        float               CurrentStrength    { get; }
        BrushFalloff        CurrentFalloff     { get; }
        float               CurrentWeightValue { get; }
        int                 CurrentTargetBone  { get; }
        /// <summary>ペイント対象メッシュの MasterIndex。-1 = 自動（FirstDrawable）</summary>
        int                 CurrentTargetMesh  { get; }

        void NotifyWeightChanged();
    }
}
