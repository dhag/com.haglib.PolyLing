// Tools/TransformTools/ObjectMoveTool_/ObjectMoveSettings.cs
// ObjectMoveTool用設定クラス

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ObjectMoveTool用設定クラス
    /// </summary>
    public class ObjectMoveSettings : ToolSettingsBase
    {
        // ================================================================
        // 設定項目
        // ================================================================

        /// <summary>
        /// 子ボーンを一緒に移動するか
        /// true  : 選択アイテムを移動 → 子は親に追従して自動的に移動
        /// false : 選択アイテムを移動 → 直接の子の Position を補正して世界位置を維持
        /// </summary>
        public bool MoveWithChildren = true;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public override IToolSettings Clone()
        {
            return new ObjectMoveSettings
            {
                MoveWithChildren = this.MoveWithChildren,
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<ObjectMoveSettings>(other, out var o)) return true;
            return MoveWithChildren != o.MoveWithChildren;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<ObjectMoveSettings>(other, out var o)) return;
            MoveWithChildren = o.MoveWithChildren;
        }
    }
}
