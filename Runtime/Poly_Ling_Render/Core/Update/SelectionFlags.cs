// Assets/Editor/Poly_Ling/Core/Update/SelectionFlags.cs
// 選択フラグ定義
// GPU側と共有するビットフラグ

using System;

namespace Poly_Ling.Core
{
    /// <summary>
    /// 統合選択フラグ（GPU側と共有）
    /// 
    /// ビットレイアウト:
    /// Bits 0-3   : 階層フラグ（所属レベル）
    /// Bits 4-7   : 要素選択フラグ
    /// Bits 8-11  : インタラクションフラグ
    /// Bits 12-15 : 表示制御フラグ
    /// Bits 16-19 : 属性フラグ
    /// Bits 20-31 : 予約
    /// </summary>
    [Flags]
    public enum SelectionFlags : uint
    {
        None = 0,

        // ============================================================
        // 階層フラグ（Bits 0-3）: 所属レベル
        // ============================================================

        /// <summary>選択モデルに属する</summary>
        ModelSelected = 1 << 0,

        /// <summary>選択メッシュに属する</summary>
        MeshSelected = 1 << 1,

        /// <summary>アクティブモデル（編集対象）</summary>
        ModelActive = 1 << 2,

        /// <summary>アクティブメッシュ（編集対象）</summary>
        MeshActive = 1 << 3,

        // ============================================================
        // 要素選択フラグ（Bits 4-7）: 要素自体の選択状態
        // ============================================================

        /// <summary>頂点選択</summary>
        VertexSelected = 1 << 4,

        /// <summary>エッジ選択</summary>
        EdgeSelected = 1 << 5,

        /// <summary>面選択</summary>
        FaceSelected = 1 << 6,

        /// <summary>補助線選択</summary>
        LineSelected = 1 << 7,

        // ============================================================
        // インタラクションフラグ（Bits 8-11）: 動的状態
        // ============================================================

        /// <summary>マウスホバー中</summary>
        Hovered = 1 << 8,

        /// <summary>ドラッグ中</summary>
        Dragging = 1 << 9,

        /// <summary>プリセレクト（次にクリックで選択）</summary>
        Preselect = 1 << 10,

        /// <summary>ハイライト（ツール固有）</summary>
        Highlight = 1 << 11,

        // ============================================================
        // 表示制御フラグ（Bits 12-15）: 可視性
        // ============================================================

        /// <summary>非表示</summary>
        Hidden = 1 << 12,

        /// <summary>ロック（編集不可）</summary>
        Locked = 1 << 13,

        /// <summary>カリングされた（画面外/背面）</summary>
        Culled = 1 << 14,

        /// <summary>ミラー要素</summary>
        Mirror = 1 << 15,

        // ============================================================
        // 属性フラグ（Bits 16-19）: 要素タイプ
        // ============================================================

        /// <summary>補助線（2頂点Face）</summary>
        IsAuxLine = 1 << 16,

        /// <summary>境界エッジ</summary>
        IsBoundary = 1 << 17,

        /// <summary>UVシーム</summary>
        IsSeam = 1 << 18,

        /// <summary>シャープエッジ</summary>
        IsSharp = 1 << 19,

        // ============================================================
        // 複合マスク
        // ============================================================

        /// <summary>階層フラグマスク（Bits 0-3）</summary>
        HierarchyMask = ModelSelected | MeshSelected | ModelActive | MeshActive,

        /// <summary>要素選択フラグマスク（Bits 4-7）</summary>
        ElementSelectionMask = VertexSelected | EdgeSelected | FaceSelected | LineSelected,

        /// <summary>インタラクションフラグマスク（Bits 8-11）</summary>
        InteractionMask = Hovered | Dragging | Preselect | Highlight,

        /// <summary>表示制御フラグマスク（Bits 12-15）</summary>
        VisibilityMask = Hidden | Locked | Culled | Mirror,

        /// <summary>属性フラグマスク（Bits 16-19）</summary>
        AttributeMask = IsAuxLine | IsBoundary | IsSeam | IsSharp,

        /// <summary>アクティブ判定用</summary>
        ActiveMask = ModelActive | MeshActive,
    }

    /// <summary>
    /// SelectionFlags拡張メソッド
    /// </summary>
    public static class SelectionFlagsExtensions
    {
        /// <summary>
        /// 指定フラグが含まれているか
        /// </summary>
        public static bool Has(this SelectionFlags flags, SelectionFlags flag)
        {
            return (flags & flag) != 0;
        }

        /// <summary>
        /// フラグを追加
        /// </summary>
        public static SelectionFlags With(this SelectionFlags flags, SelectionFlags flag)
        {
            return flags | flag;
        }

        /// <summary>
        /// フラグを除去
        /// </summary>
        public static SelectionFlags Without(this SelectionFlags flags, SelectionFlags flag)
        {
            return flags & ~flag;
        }

        /// <summary>
        /// フラグを設定（条件付き）
        /// </summary>
        public static SelectionFlags SetIf(this SelectionFlags flags, SelectionFlags flag, bool condition)
        {
            return condition ? flags.With(flag) : flags.Without(flag);
        }

        /// <summary>
        /// アクティブ（編集対象）かどうか
        /// </summary>
        public static bool IsActive(this SelectionFlags flags)
        {
            return (flags & SelectionFlags.ActiveMask) == SelectionFlags.ActiveMask;
        }

        /// <summary>
        /// 可視かどうか
        /// </summary>
        public static bool IsVisible(this SelectionFlags flags)
        {
            return (flags & (SelectionFlags.Hidden | SelectionFlags.Culled)) == 0;
        }

        /// <summary>
        /// 編集可能かどうか
        /// </summary>
        public static bool IsEditable(this SelectionFlags flags)
        {
            return flags.IsActive() && !flags.Has(SelectionFlags.Locked);
        }

        /// <summary>
        /// インタラクティブ（クリック・ドラッグ可能）かどうか
        /// </summary>
        public static bool IsInteractive(this SelectionFlags flags)
        {
            return flags.IsVisible() && flags.IsEditable();
        }

        /// <summary>
        /// いずれかの要素が選択されているか
        /// </summary>
        public static bool HasAnyElementSelected(this SelectionFlags flags)
        {
            return (flags & SelectionFlags.ElementSelectionMask) != 0;
        }

        /// <summary>
        /// ホバーまたは選択されているか
        /// </summary>
        public static bool IsHoveredOrSelected(this SelectionFlags flags)
        {
            return flags.Has(SelectionFlags.Hovered) || flags.HasAnyElementSelected();
        }

        /// <summary>
        /// ミラー要素かどうか
        /// </summary>
        public static bool IsMirror(this SelectionFlags flags)
        {
            return flags.Has(SelectionFlags.Mirror);
        }
    }
}
