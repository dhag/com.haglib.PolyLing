// Assets/Editor/MeshFactory/Shaders/Include/SelectionFlags.hlsl
// GPU用選択フラグ定義
// C#側のSelectionFlags enumと同じ値を定義

#ifndef SELECTION_FLAGS_INCLUDED
#define SELECTION_FLAGS_INCLUDED

// ============================================================
// 階層フラグ（Bits 0-3）: 所属レベル
// ============================================================

#define FLAG_MODEL_SELECTED        (1u << 0)   // 選択モデルに属する
#define FLAG_MESH_SELECTED         (1u << 1)   // 選択メッシュに属する
#define FLAG_MODEL_ACTIVE          (1u << 2)   // アクティブモデル（編集対象）
#define FLAG_MESH_ACTIVE           (1u << 3)   // アクティブメッシュ（編集対象）

// ============================================================
// 要素選択フラグ（Bits 4-7）: 要素自体の選択状態
// ============================================================

#define FLAG_VERTEX_SELECTED       (1u << 4)   // 頂点選択
#define FLAG_EDGE_SELECTED         (1u << 5)   // エッジ選択
#define FLAG_FACE_SELECTED         (1u << 6)   // 面選択
#define FLAG_LINE_SELECTED         (1u << 7)   // 補助線選択

// ============================================================
// インタラクションフラグ（Bits 8-11）: 動的状態
// ============================================================

#define FLAG_HOVERED               (1u << 8)   // マウスホバー中
#define FLAG_DRAGGING              (1u << 9)   // ドラッグ中
#define FLAG_PRESELECT             (1u << 10)  // プリセレクト
#define FLAG_HIGHLIGHT             (1u << 11)  // ハイライト

// ============================================================
// 表示制御フラグ（Bits 12-15）: 可視性
// ============================================================

#define FLAG_HIDDEN                (1u << 12)  // 非表示
#define FLAG_LOCKED                (1u << 13)  // ロック
#define FLAG_CULLED                (1u << 14)  // カリングされた
#define FLAG_MIRROR                (1u << 15)  // ミラー要素

// ============================================================
// 属性フラグ（Bits 16-19）: 要素タイプ
// ============================================================

#define FLAG_IS_AUXLINE            (1u << 16)  // 補助線
#define FLAG_IS_BOUNDARY           (1u << 17)  // 境界エッジ
#define FLAG_IS_SEAM               (1u << 18)  // UVシーム
#define FLAG_IS_SHARP              (1u << 19)  // シャープエッジ

// ============================================================
// 複合マスク
// ============================================================

#define MASK_HIERARCHY             0x0000000Fu  // Bits 0-3
#define MASK_ELEMENT_SELECTION     0x000000F0u  // Bits 4-7
#define MASK_INTERACTION           0x00000F00u  // Bits 8-11
#define MASK_VISIBILITY            0x0000F000u  // Bits 12-15
#define MASK_ATTRIBUTE             0x000F0000u  // Bits 16-19
#define MASK_ACTIVE                (FLAG_MODEL_ACTIVE | FLAG_MESH_ACTIVE)

// ============================================================
// 判定マクロ
// ============================================================

// フラグが含まれているか
#define HAS_FLAG(flags, flag) (((flags) & (flag)) != 0u)

// アクティブ（編集対象）かどうか
#define IS_ACTIVE(flags) (((flags) & MASK_ACTIVE) == MASK_ACTIVE)

// 可視かどうか
#define IS_VISIBLE(flags) (((flags) & (FLAG_HIDDEN | FLAG_CULLED)) == 0u)

// 編集可能かどうか
#define IS_EDITABLE(flags) (IS_ACTIVE(flags) && !HAS_FLAG(flags, FLAG_LOCKED))

// インタラクティブ（クリック・ドラッグ可能）かどうか
#define IS_INTERACTIVE(flags) (IS_VISIBLE(flags) && IS_EDITABLE(flags))

// いずれかの要素が選択されているか
#define HAS_ANY_SELECTED(flags) (((flags) & MASK_ELEMENT_SELECTION) != 0u)

// ホバーまたは選択されているか
#define IS_HOVERED_OR_SELECTED(flags) (HAS_FLAG(flags, FLAG_HOVERED) || HAS_ANY_SELECTED(flags))

// ミラー要素かどうか
#define IS_MIRROR(flags) HAS_FLAG(flags, FLAG_MIRROR)

// 補助線かどうか
#define IS_AUXLINE(flags) HAS_FLAG(flags, FLAG_IS_AUXLINE)

// ============================================================
// 色計算ヘルパー
// ============================================================

// 階層に基づく基本色の選択
// priority: Selected > Active > Default > InactiveModel
float4 GetHierarchyBaseColor(
    uint flags,
    float4 colorInactiveModel,
    float4 colorDefault,
    float4 colorActive,
    float4 colorSelected)
{
    if (HAS_FLAG(flags, FLAG_MESH_SELECTED))
        return colorSelected;
    if (IS_ACTIVE(flags))
        return colorActive;
    if (HAS_FLAG(flags, FLAG_MODEL_SELECTED))
        return colorDefault;
    return colorInactiveModel;
}

// 選択・ホバー状態に基づく色のオーバーライド
float4 ApplySelectionColor(
    uint flags,
    float4 baseColor,
    float4 colorHovered,
    float4 colorSelected,
    float4 colorDragging)
{
    // 優先順位: Dragging > Selected > Hovered > Base
    if (HAS_FLAG(flags, FLAG_DRAGGING))
        return colorDragging;
    if (HAS_ANY_SELECTED(flags))
        return colorSelected;
    if (HAS_FLAG(flags, FLAG_HOVERED))
        return colorHovered;
    return baseColor;
}

// ミラー要素の色調整
float4 ApplyMirrorColor(uint flags, float4 color, float mirrorAlpha)
{
    if (IS_MIRROR(flags))
    {
        color.a *= mirrorAlpha;
    }
    return color;
}

// 全色計算（階層 + 選択 + ミラー）
float4 ComputeFinalColor(
    uint flags,
    float4 colorInactiveModel,
    float4 colorDefault,
    float4 colorActive,
    float4 colorMeshSelected,
    float4 colorHovered,
    float4 colorElementSelected,
    float4 colorDragging,
    float mirrorAlpha)
{
    // 1. 階層ベース色
    float4 baseColor = GetHierarchyBaseColor(
        flags,
        colorInactiveModel,
        colorDefault,
        colorActive,
        colorMeshSelected);
    
    // 2. 選択・ホバーオーバーライド
    float4 color = ApplySelectionColor(
        flags,
        baseColor,
        colorHovered,
        colorElementSelected,
        colorDragging);
    
    // 3. ミラー調整
    color = ApplyMirrorColor(flags, color, mirrorAlpha);
    
    return color;
}

// ============================================================
// サイズ計算ヘルパー
// ============================================================

// 頂点サイズを計算
float ComputePointSize(
    uint flags,
    float sizeDefault,
    float sizeActive,
    float sizeSelected,
    float sizeHovered)
{
    if (HAS_FLAG(flags, FLAG_DRAGGING))
        return sizeSelected * 1.2;
    if (HAS_FLAG(flags, FLAG_VERTEX_SELECTED))
        return sizeSelected;
    if (HAS_FLAG(flags, FLAG_HOVERED))
        return sizeHovered;
    if (IS_ACTIVE(flags))
        return sizeActive;
    return sizeDefault;
}

// 線幅を計算
float ComputeLineWidth(
    uint flags,
    float widthDefault,
    float widthActive,
    float widthSelected,
    float widthHovered)
{
    if (HAS_FLAG(flags, FLAG_DRAGGING))
        return widthSelected * 1.2;
    if (HAS_FLAG(flags, FLAG_EDGE_SELECTED) || HAS_FLAG(flags, FLAG_LINE_SELECTED))
        return widthSelected;
    if (HAS_FLAG(flags, FLAG_HOVERED))
        return widthHovered;
    if (IS_ACTIVE(flags))
        return widthActive;
    return widthDefault;
}

#endif // SELECTION_FLAGS_INCLUDED
