// Assets/Editor/MeshFactory/Tools/Settings/PrimitiveMeshSettings.cs
// PrimitiveMeshTool用の設定（IToolSettings実装）

using UnityEngine;

namespace MeshFactory.Tools
{
    /// <summary>
    /// PrimitiveMeshTool用の設定
    /// </summary>
    public class PrimitiveMeshSettings : IToolSettings
    {
        /// <summary>現在選択中のメッシュに追加するか</summary>
        public bool AddToCurrentMesh = false;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new PrimitiveMeshSettings
            {
                AddToCurrentMesh = this.AddToCurrentMesh
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is PrimitiveMeshSettings src)
            {
                AddToCurrentMesh = src.AddToCurrentMesh;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is PrimitiveMeshSettings src)
            {
                return AddToCurrentMesh != src.AddToCurrentMesh;
            }
            return true;
        }
    }
}
