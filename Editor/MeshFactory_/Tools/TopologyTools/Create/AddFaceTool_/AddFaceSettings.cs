// Assets/Editor/MeshFactory/Tools/Settings/AddFaceSettings.cs
// AddFaceTool用設定クラス（IToolSettings対応）

using UnityEngine;

namespace MeshFactory.Tools
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

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new AddFaceSettings
            {
                Mode = this.Mode,
                DefaultDistance = this.DefaultDistance,
                ContinuousLine = this.ContinuousLine
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is AddFaceSettings src)
            {
                Mode = src.Mode;
                DefaultDistance = src.DefaultDistance;
                ContinuousLine = src.ContinuousLine;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is AddFaceSettings src)
            {
                return Mode != src.Mode ||
                       !Mathf.Approximately(DefaultDistance, src.DefaultDistance) ||
                       ContinuousLine != src.ContinuousLine;
            }
            return true;
        }
    }
}
