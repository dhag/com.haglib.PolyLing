// Assets/Editor/MeshFactory/Selection/MeshSelectMode.cs

using System;

namespace MeshFactory.Selection
{
    /// <summary>
    /// メッシュ要素の選択モード（複数同時選択可能）
    /// </summary>
    [Flags]
    public enum MeshSelectMode
    {
        /// <summary>選択なし</summary>
        None = 0,
        
        /// <summary>頂点選択</summary>
        Vertex = 1,
        
        /// <summary>エッジ選択（面の辺）</summary>
        Edge = 2,
        
        /// <summary>面選択（3頂点以上）</summary>
        Face = 4,
        
        /// <summary>補助線分選択（2頂点の独立線分）</summary>
        Line = 8,
        
        /// <summary>全て</summary>
        All = Vertex | Edge | Face | Line
    }

    /// <summary>
    /// MeshSelectMode拡張メソッド
    /// </summary>
    public static class MeshSelectModeExtensions
    {
        /// <summary>
        /// 指定モードが有効か
        /// </summary>
        public static bool Has(this MeshSelectMode mode, MeshSelectMode flag)
        {
            return (mode & flag) != 0;
        }

        /// <summary>
        /// 有効なモードの数
        /// </summary>
        public static int Count(this MeshSelectMode mode)
        {
            int count = 0;
            if (mode.Has(MeshSelectMode.Vertex)) count++;
            if (mode.Has(MeshSelectMode.Edge)) count++;
            if (mode.Has(MeshSelectMode.Face)) count++;
            if (mode.Has(MeshSelectMode.Line)) count++;
            return count;
        }
    }
}
