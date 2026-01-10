// Assets/Editor/MeshFactory/Core/Gizmo/GizmoContext.cs
// ギズモ描画コンテキスト
// ToolContextに注入して使用

using UnityEngine;

namespace MeshFactory.Gizmo
{
    /// <summary>
    /// ギズモ描画コンテキスト
    /// ToolContextに注入して各ツールから使用
    /// </summary>
    public class GizmoContext
    {
        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>
        /// 2D描画インターフェース
        /// </summary>
        public IGizmoDrawer2D Drawer2D { get; set; }

        /// <summary>
        /// 3D描画インターフェース
        /// </summary>
        public IGizmoDrawer3D Drawer3D { get; set; }

        /// <summary>
        /// 統合描画インターフェース（両方を実装している場合）
        /// </summary>
        public IGizmoDrawer Drawer { get; set; }

        // ================================================================
        // 便利プロパティ
        // ================================================================

        /// <summary>
        /// 2D描画が利用可能か
        /// </summary>
        public bool Has2D => Drawer2D != null || Drawer != null;

        /// <summary>
        /// 3D描画が利用可能か
        /// </summary>
        public bool Has3D => Drawer3D != null || Drawer != null;

        /// <summary>
        /// 有効な2D Drawerを取得
        /// </summary>
        public IGizmoDrawer2D Get2D() => Drawer2D ?? Drawer;

        /// <summary>
        /// 有効な3D Drawerを取得
        /// </summary>
        public IGizmoDrawer3D Get3D() => Drawer3D ?? Drawer;

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>
        /// 統合Drawerから作成
        /// </summary>
        public static GizmoContext Create(IGizmoDrawer drawer)
        {
            return new GizmoContext
            {
                Drawer = drawer,
                Drawer2D = drawer,
                Drawer3D = drawer
            };
        }

        /// <summary>
        /// 2Dのみで作成
        /// </summary>
        public static GizmoContext Create2D(IGizmoDrawer2D drawer2D)
        {
            return new GizmoContext
            {
                Drawer2D = drawer2D
            };
        }

        /// <summary>
        /// 2Dと3Dを個別に指定して作成
        /// </summary>
        public static GizmoContext Create(IGizmoDrawer2D drawer2D, IGizmoDrawer3D drawer3D)
        {
            return new GizmoContext
            {
                Drawer2D = drawer2D,
                Drawer3D = drawer3D
            };
        }
    }
}
