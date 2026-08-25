// SkinWeightPaintShared.cs
// スキンウェイトペイント共有定義
// SkinWeightPaintPanel / SkinWeightPaintPanelV2 / SkinWeightPaintTool から参照

using System.Collections.Generic;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public enum SkinWeightPaintMode
    {
        Replace,
        Add,
        Scale,
        Smooth,
    }

    // BrushFalloff（Constant / Linear / Smooth の 3 種）は廃止した。
    // マグネット（MoveSettings.MagnetFalloff）とスカルプト（SculptSettings.Falloff）が
    // 使う Poly_Ling.Tools.FalloffType（7 種）に一本化し、
    // 減衰の計算も FalloffHelper.Calculate に統一する。

    /// <summary>
    /// SkinWeightPaintTool が参照するパネルインターフェース。
    /// SkinWeightPaintPanel (V1) と SkinWeightPaintPanelV2 の両方が実装する。
    /// </summary>
    public interface ISkinWeightPaintPanel
    {
        SkinWeightPaintMode CurrentPaintMode   { get; }
        float               CurrentBrushRadius { get; }
        float               CurrentStrength    { get; }

        /// <summary>減衰タイプ。マグネット／スカルプトと共通の FalloffType。</summary>
        FalloffType         CurrentFalloff     { get; }

        /// <summary>距離モード。直線（ユークリッド）／リンク距離。</summary>
        DistanceMode        CurrentDistanceMode { get; }

        float               CurrentWeightValue { get; }
        int                 CurrentTargetBone  { get; }
        /// <summary>ペイント対象メッシュの MasterIndex。-1 = 自動（ActiveMeshContext）</summary>
        int                 CurrentTargetMesh  { get; }

        void NotifyWeightChanged();
    }

    /// <summary>
    /// 複数ボーンをまとめてウェイト可視化したいパネルが追加で実装するインターフェース。
    ///
    /// Blender の Multi-Paint 相当。指定した複数ボーンのウェイトを合計して 1 系統の
    /// ヒートマップで表示する。ISkinWeightPaintPanel 本体には足さない
    /// （ブラシ用の PlayerSkinWeightPaintPanel を無改修のまま残すため）。
    ///
    /// VisualizationBones が null または空なら、従来どおり
    /// ISkinWeightPaintPanel.CurrentTargetBone の 1 ボーン表示になる。
    /// </summary>
    public interface IMultiBoneWeightVisualization
    {
        /// <summary>可視化に含めるボーンの MasterIndex 群。null で単一ボーン表示。</summary>
        IReadOnlyList<int> VisualizationBones { get; }
    }
}
