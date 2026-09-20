// Assets/Editor/Poly_Ling/Tools/Settings/AddFaceSettings.cs
// AddFaceTool用設定クラス（IToolSettings対応）

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// AddFaceTool用設定
    /// </summary>
    public class AddFaceSettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>追加モード (Line/Triangle/Quad)</summary>
        public AddFaceMode Mode = AddFaceMode.Quad;

        /// <summary>WorkPlaneと交差しない場合のカメラからの距離</summary>
        public float DefaultDistance = 1.5f;

        /// <summary>連続線分モード</summary>
        public bool ContinuousLine = true;

        /// <summary>
        /// 線分の描き始めが既存の線分群の端点なら、その群を伸ばすか。
        /// 既定 false（新しい群を作り、始点を親にする）。LineGroupOps.AddSegment を参照。
        /// </summary>
        public bool ExtendLineGroup = false;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new AddFaceSettings
            {
                Mode = this.Mode,
                DefaultDistance = this.DefaultDistance,
                ContinuousLine = this.ContinuousLine,
                ExtendLineGroup = this.ExtendLineGroup
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is AddFaceSettings src)
            {
                Mode = src.Mode;
                DefaultDistance = src.DefaultDistance;
                ContinuousLine = src.ContinuousLine;
                ExtendLineGroup = src.ExtendLineGroup;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AddFaceSettings src)
            {
                return Mode != src.Mode ||
                       !Mathf.Approximately(DefaultDistance, src.DefaultDistance) ||
                       ContinuousLine != src.ContinuousLine ||
                       ExtendLineGroup != src.ExtendLineGroup;
            }
            return true;
        }
    }
}
