// Tools/TransformTools/ObjectMoveTool_/ObjectMoveSettings.cs
// ObjectMoveTool用設定クラス

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ボーン移動モード。
    /// A: BoneOnlyRebind = ボーンだけ動かす（確定時リバインドのみ・メッシュ不変）
    /// B: SkinBakeRebind = スキンごと動かして確定（確定時に頂点焼き込み＋リバインド）
    /// </summary>
    public enum BoneMoveMode
    {
        BoneOnlyRebind = 0,
        SkinBakeRebind = 1,
        PoseLayer      = 2,
    }

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

        /// <summary>ピック対象: ボーン (MeshType.Bone)</summary>
        public bool PickBones = true;

        /// <summary>
        /// ピック対象: スキンドでないメッシュ
        /// (MeshType.Mesh かつ MeshObject.HasBoneWeight == false)
        /// </summary>
        public bool PickMeshesNoSkin = true;

        /// <summary>
        /// ピック対象: スキンドメッシュ
        /// (MeshType.Mesh かつ MeshObject.HasBoneWeight == true)
        /// 通常ボーン側を動かすので OFF 推奨。
        /// </summary>
        public bool PickMeshesSkinned = false;

        /// <summary>
        /// ボーン移動モード（A/B）。既定は A（ボーンだけ動かす・スキン固定）。
        /// </summary>
        public BoneMoveMode MoveMode = BoneMoveMode.BoneOnlyRebind;

        /// <summary>
        /// 原点だけ移動（MeshFilter の自形状を固定して原点=BoneTransform だけを動かす）。
        /// 既定 false（=従来のオブジェごと移動）。表示ラベルは「原点だけ移動」。
        /// </summary>
        public bool OriginOnly = false;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public override IToolSettings Clone()
        {
            return new ObjectMoveSettings
            {
                MoveWithChildren  = this.MoveWithChildren,
                PickBones         = this.PickBones,
                PickMeshesNoSkin  = this.PickMeshesNoSkin,
                PickMeshesSkinned = this.PickMeshesSkinned,
                MoveMode          = this.MoveMode,
                OriginOnly        = this.OriginOnly,
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<ObjectMoveSettings>(other, out var o)) return true;
            return MoveWithChildren  != o.MoveWithChildren
                || PickBones         != o.PickBones
                || PickMeshesNoSkin  != o.PickMeshesNoSkin
                || PickMeshesSkinned != o.PickMeshesSkinned
                || MoveMode          != o.MoveMode
                || OriginOnly        != o.OriginOnly;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<ObjectMoveSettings>(other, out var o)) return;
            MoveWithChildren  = o.MoveWithChildren;
            PickBones         = o.PickBones;
            PickMeshesNoSkin  = o.PickMeshesNoSkin;
            PickMeshesSkinned = o.PickMeshesSkinned;
            MoveMode          = o.MoveMode;
            OriginOnly        = o.OriginOnly;
        }
    }
}
