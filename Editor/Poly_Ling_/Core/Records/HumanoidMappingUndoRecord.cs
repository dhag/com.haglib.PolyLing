// HumanoidMappingUndoRecord.cs
// HumanoidBoneMapping変更のUNDO/REDO対応

using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Records
{
    /// <summary>
    /// HumanoidBoneMappingの変更を記録するUNDOレコード
    /// </summary>
    public class HumanoidMappingChangedRecord : IUndoRecord<ModelContext>
    {
        private readonly HumanoidBoneMapping _before;
        private readonly HumanoidBoneMapping _after;

        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="before">変更前のマッピング（Clone済み）</param>
        /// <param name="after">変更後のマッピング（Clone済み）</param>
        /// <param name="description">操作の説明（省略可）</param>
        public HumanoidMappingChangedRecord(
            HumanoidBoneMapping before,
            HumanoidBoneMapping after,
            string description = null)
        {
            _before = before.Clone();
            _after = after.Clone();
            Info = new UndoOperationInfo(description ?? "Humanoid Mapping Changed", "HumanoidMapping");
        }

        /// <summary>
        /// UNDO: 変更前の状態に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            if (context?.HumanoidMapping != null)
            {
                context.HumanoidMapping.CopyFrom(_before);
            }
        }

        /// <summary>
        /// REDO: 変更後の状態に進める
        /// </summary>
        public void Redo(ModelContext context)
        {
            if (context?.HumanoidMapping != null)
            {
                context.HumanoidMapping.CopyFrom(_after);
            }
        }
    }

    /// <summary>
    /// 単一ボーンマッピング変更のUNDOレコード
    /// </summary>
    public class HumanoidBoneSetRecord : IUndoRecord<ModelContext>
    {
        private readonly string _humanoidBone;
        private readonly int _beforeIndex;
        private readonly int _afterIndex;

        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="humanoidBone">変更するHumanoidボーン名</param>
        /// <param name="beforeIndex">変更前のインデックス</param>
        /// <param name="afterIndex">変更後のインデックス</param>
        public HumanoidBoneSetRecord(
            string humanoidBone,
            int beforeIndex,
            int afterIndex)
        {
            _humanoidBone = humanoidBone;
            _beforeIndex = beforeIndex;
            _afterIndex = afterIndex;
            Info = new UndoOperationInfo($"Set {humanoidBone} Mapping", "HumanoidMapping");
        }

        public void Undo(ModelContext context)
        {
            if (context?.HumanoidMapping != null)
            {
                context.HumanoidMapping.Set(_humanoidBone, _beforeIndex);
            }
        }

        public void Redo(ModelContext context)
        {
            if (context?.HumanoidMapping != null)
            {
                context.HumanoidMapping.Set(_humanoidBone, _afterIndex);
            }
        }
    }
}
