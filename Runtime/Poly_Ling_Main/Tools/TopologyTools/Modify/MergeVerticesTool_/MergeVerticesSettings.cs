// Assets/Editor/Poly_Ling/Tools/Settings/MergeVerticesSettings.cs
// MergeVerticesTool用設定クラス（IToolSettings対応）

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// MergeVerticesTool用設定
    /// </summary>
    public class MergeVerticesSettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>マージ距離しきい値</summary>
        public float Threshold = 0.001f;

        /// <summary>プレビュー表示</summary>
        public bool ShowPreview = true;

        /// <summary>閉じた面（おもて面同士が重なる面＝逆巻きの重なり面）を削除する</summary>
        public bool RemoveClosedFaces = false;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new MergeVerticesSettings
            {
                Threshold = this.Threshold,
                ShowPreview = this.ShowPreview,
                RemoveClosedFaces = this.RemoveClosedFaces
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is MergeVerticesSettings src)
            {
                Threshold = src.Threshold;
                ShowPreview = src.ShowPreview;
                RemoveClosedFaces = src.RemoveClosedFaces;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is MergeVerticesSettings src)
            {
                return !Mathf.Approximately(Threshold, src.Threshold) ||
                       ShowPreview != src.ShowPreview ||
                       RemoveClosedFaces != src.RemoveClosedFaces;
            }
            return true;
        }
    }
}
