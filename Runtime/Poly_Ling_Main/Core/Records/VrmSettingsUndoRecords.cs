// Runtime/Poly_Ling_Main/Core/Records/VrmSettingsUndoRecords.cs
// ============================================================
// VRM 1.0 設定（メタ情報 / 視線 / 一人称）Undo/Redo 記録
// ============================================================
//
// 【役割】
//   モデルレベルの VRM 設定（VrmMeta / VrmLookAt）と、描画オブジェクトごとの
//   一人称指定（MeshObject.VrmFirstPerson）を Undo/Redo する。
//   構造は SpringBoneUndoRecords.cs と同型（同じ MeshListStack へ積む）。
//
// 【null も状態】
//   VrmMeta / VrmLookAt は null が「未設定」という意味を持つ。
//   null も記録し、復元時に null へ戻す。
//
// 【依存】
//   #if UNITY_EDITOR を含まない。
//
// ============================================================

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // モデルレベル（メタ情報 / 視線）
    // ============================================================

    /// <summary>モデルレベルの VRM 設定のスナップショット。</summary>
    public class VrmModelSettingsSnapshot
    {
        /// <summary>メタ情報（null = 未設定）。</summary>
        public VrmMetaData Meta;

        /// <summary>視線設定（null = 未設定）。</summary>
        public VrmLookAtData LookAt;

        public static VrmModelSettingsSnapshot Capture(ModelContext ctx)
        {
            if (ctx == null) return null;

            return new VrmModelSettingsSnapshot
            {
                Meta   = ctx.VrmMeta?.Clone(),
                LookAt = ctx.VrmLookAt?.Clone(),
            };
        }

        public void ApplyTo(ModelContext ctx)
        {
            if (ctx == null) return;

            ctx.VrmMeta   = Meta?.Clone();
            ctx.VrmLookAt = LookAt?.Clone();
        }

        public VrmModelSettingsSnapshot Clone()
        {
            return new VrmModelSettingsSnapshot
            {
                Meta   = Meta?.Clone(),
                LookAt = LookAt?.Clone(),
            };
        }
    }

    /// <summary>モデルレベルの VRM 設定変更レコード。</summary>
    public class VrmModelSettingsRecord : MeshListUndoRecord
    {
        public VrmModelSettingsSnapshot OldSnapshot;
        public VrmModelSettingsSnapshot NewSnapshot;

        public VrmModelSettingsRecord() { }

        public VrmModelSettingsRecord(
            VrmModelSettingsSnapshot oldSnapshot, VrmModelSettingsSnapshot newSnapshot)
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

        public override string ToString() => "VrmModelSettingsChange";
    }

    // ============================================================
    // 一人称指定（per-mesh）
    // ============================================================

    /// <summary>描画オブジェクトごとの一人称指定の一括変更レコード。</summary>
    public class MultiVrmFirstPersonChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public VrmFirstPersonType OldType;
            public VrmFirstPersonType NewType;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries) Apply(ctx, e.MasterIndex, e.OldType);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries) Apply(ctx, e.MasterIndex, e.NewType);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        private static void Apply(ModelContext ctx, int masterIndex, VrmFirstPersonType type)
        {
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;

            var mo = ctx.GetMeshContext(masterIndex)?.MeshObject;
            if (mo == null) return;

            mo.VrmFirstPerson = type;
        }

        public override string ToString()
            => $"MultiVrmFirstPersonChange: {Entries.Count} objects";
    }
}
