// Runtime/Poly_Ling_Main/Core/Records/AvatarRetargetUndoRecords.cs
// ============================================================
// Avatar リターゲット設定 Undo/Redo 記録
// ============================================================
//
// 【役割】
//   モデルレベルの AvatarRetarget を Undo/Redo する。
//   構造は VrmSettingsUndoRecords.cs と同型（同じ MeshListStack へ積む）。
//
// 【null も状態】
//   AvatarRetarget == null が「未設定＝Unity の既定を使う」という状態。
//   null も記録し、復元時に null へ戻す。
//
// 【依存】
//   #if UNITY_EDITOR を含まない。
//
// ============================================================

using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.UndoSystem
{
    /// <summary>モデルレベルの Avatar リターゲット設定のスナップショット。</summary>
    public class AvatarRetargetSnapshot
    {
        /// <summary>リターゲット設定（null = 未設定）。</summary>
        public AvatarRetargetData Retarget;

        public static AvatarRetargetSnapshot Capture(ModelContext ctx)
        {
            if (ctx == null) return null;
            return new AvatarRetargetSnapshot { Retarget = ctx.AvatarRetarget?.Clone() };
        }

        public void ApplyTo(ModelContext ctx)
        {
            if (ctx == null) return;
            ctx.AvatarRetarget = Retarget?.Clone();
        }

        public AvatarRetargetSnapshot Clone()
            => new AvatarRetargetSnapshot { Retarget = Retarget?.Clone() };
    }

    /// <summary>Avatar リターゲット設定の変更レコード。</summary>
    public class AvatarRetargetChangeRecord : MeshListUndoRecord
    {
        public AvatarRetargetSnapshot OldSnapshot;
        public AvatarRetargetSnapshot NewSnapshot;

        public AvatarRetargetChangeRecord() { }

        public AvatarRetargetChangeRecord(
            AvatarRetargetSnapshot oldSnapshot, AvatarRetargetSnapshot newSnapshot)
        {
            OldSnapshot = oldSnapshot;
            NewSnapshot = newSnapshot;
        }

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            OldSnapshot?.ApplyTo(ctx);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            NewSnapshot?.ApplyTo(ctx);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString() => "AvatarRetargetChange";
    }
}
