// Assets/Editor/Poly_Ling/UndoSystem/MorphMirrorUndoRecords.cs
// ============================================================
// モーフのミラー適用ポリシー Undo/Redo 記録
// ============================================================
//
// 【役割】
//   MeshContext.MorphMirrorPolicy / MirrorOfMorphIndex の変更を軽量に Undo/Redo する。
//
// 【規約】
//   モーフとミラーの関係についての規約は MorphMirrorPolicy.cs 冒頭のコメントを正典とする。
//   ここには規約そのものを書き写さない。
//
// 【MeshContextSnapshot との棲み分け】
//   MeshContextSnapshot は両フィールドを含むため、リスト単位の Undo でも復元される。
//   本レコードはポリシー変更だけを記録する軽量版。
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
    /// <summary>
    /// 1モーフ分のミラー適用ポリシーのスナップショット。
    /// </summary>
    public struct MorphMirrorPolicySnapshot
    {
        /// <summary>ミラー適用ポリシー</summary>
        public MorphMirrorPolicy Policy;

        /// <summary>MirrorOf のときの参照先モーフのマスターインデックス（-1 = 未指定）</summary>
        public int MirrorOfMorphIndex;

        /// <summary>
        /// 指定 MeshContext からスナップショットを作成。
        /// </summary>
        public static MorphMirrorPolicySnapshot Capture(MeshContext meshContext)
        {
            if (meshContext == null)
                return new MorphMirrorPolicySnapshot
                {
                    Policy = MorphMirrorPolicy.FollowParent,
                    MirrorOfMorphIndex = -1
                };

            return new MorphMirrorPolicySnapshot
            {
                Policy = meshContext.MorphMirrorPolicy,
                MirrorOfMorphIndex = meshContext.MirrorOfMorphIndex
            };
        }

        /// <summary>スナップショットを MeshContext に適用。</summary>
        public void ApplyTo(MeshContext meshContext)
        {
            if (meshContext == null) return;

            meshContext.MorphMirrorPolicy = Policy;
            meshContext.MirrorOfMorphIndex = MirrorOfMorphIndex;
        }
    }

    // ============================================================
    // 単一モーフのポリシー変更レコード
    // ============================================================

    /// <summary>
    /// 1モーフのミラー適用ポリシー変更を記録するレコード。
    /// </summary>
    public class MorphMirrorPolicyChangeRecord : MeshListUndoRecord
    {
        /// <summary>対象MeshContextのMasterIndex</summary>
        public int MasterIndex;

        /// <summary>変更前</summary>
        public MorphMirrorPolicySnapshot OldSnapshot;

        /// <summary>変更後</summary>
        public MorphMirrorPolicySnapshot NewSnapshot;

        public MorphMirrorPolicyChangeRecord() { }

        public MorphMirrorPolicyChangeRecord(int masterIndex,
            MorphMirrorPolicySnapshot oldSnapshot, MorphMirrorPolicySnapshot newSnapshot)
        {
            MasterIndex = masterIndex;
            OldSnapshot = oldSnapshot;
            NewSnapshot = newSnapshot;
        }

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            ApplyEntry(ctx, MasterIndex, OldSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            ApplyEntry(ctx, MasterIndex, NewSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        internal static void ApplyEntry(ModelContext ctx, int masterIndex, MorphMirrorPolicySnapshot snapshot)
        {
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;

            var mc = ctx.GetMeshContext(masterIndex);
            if (mc == null) return;

            snapshot.ApplyTo(mc);
        }

        public override string ToString()
        {
            return $"MorphMirrorPolicyChange: MasterIndex={MasterIndex} {OldSnapshot.Policy} → {NewSnapshot.Policy}";
        }
    }

    // ============================================================
    // 複数モーフのポリシー一括変更レコード
    // ============================================================

    /// <summary>
    /// 複数モーフのミラー適用ポリシー変更を一括で記録するレコード。
    /// </summary>
    public class MultiMorphMirrorPolicyChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public MorphMirrorPolicySnapshot OldSnapshot;
            public MorphMirrorPolicySnapshot NewSnapshot;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                MorphMirrorPolicyChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.OldSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                MorphMirrorPolicyChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.NewSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MultiMorphMirrorPolicyChange: {Entries.Count} morphs";
        }
    }
}
