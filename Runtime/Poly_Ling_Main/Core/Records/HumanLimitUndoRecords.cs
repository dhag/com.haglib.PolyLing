// Runtime/Poly_Ling_Main/Core/Records/HumanLimitUndoRecords.cs
// ============================================================
// Humanoid マッスル可動域（HumanLimitData）Undo/Redo 記録
// ============================================================
//
// 【役割】
//   ボーン付帯の可動域を軽量に Undo/Redo するレコード。
//   構造は SpringBoneUndoRecords.cs と同型にしてある（同じ MeshListStack へ積む）。
//
// 【格納規約】
//   格納・参照・永続化の規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   HumanLimit == null は「Unity 既定を使う」という状態そのものなので、
//   null も記録し、復元時に null へ戻す。
//
// 【MeshContextSnapshot との関係】
//   リスト単位の Undo は MeshObject 全体のクローンを持つため可動域も戻る。
//   本レコードは数値編集のような局所変更のためのもの。
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
    // 1ボーン分の可動域スナップショット
    // ============================================================

    /// <summary>
    /// 1ボーン（MeshContext）分の可動域のディープコピー。
    /// Limit は null も状態として保持する。
    /// </summary>
    public class HumanLimitSnapshot
    {
        /// <summary>可動域（null = 既定を使う）。</summary>
        public HumanLimitData Limit;

        /// <summary>
        /// MeshContext からスナップショットを作成（ディープコピー）。
        /// meshContext / MeshObject が null の場合は null を返す。
        /// </summary>
        public static HumanLimitSnapshot Capture(MeshContext meshContext)
        {
            var mo = meshContext?.MeshObject;
            if (mo == null) return null;

            return new HumanLimitSnapshot
            {
                Limit = mo.HumanLimit?.Clone()
            };
        }

        /// <summary>スナップショットを MeshContext に適用（ディープコピーで書き戻す）。</summary>
        public void ApplyTo(MeshContext meshContext)
        {
            var mo = meshContext?.MeshObject;
            if (mo == null) return;

            mo.HumanLimit = Limit?.Clone();
        }

        /// <summary>スナップショットの複製。</summary>
        public HumanLimitSnapshot Clone()
        {
            return new HumanLimitSnapshot
            {
                Limit = Limit?.Clone()
            };
        }
    }

    // ============================================================
    // 単一ボーンの可動域変更レコード
    // ============================================================

    /// <summary>1ボーンの可動域変更を記録するレコード。</summary>
    public class HumanLimitChangeRecord : MeshListUndoRecord
    {
        /// <summary>対象MeshContextのMasterIndex</summary>
        public int MasterIndex;

        /// <summary>変更前のスナップショット</summary>
        public HumanLimitSnapshot OldSnapshot;

        /// <summary>変更後のスナップショット</summary>
        public HumanLimitSnapshot NewSnapshot;

        public HumanLimitChangeRecord() { }

        public HumanLimitChangeRecord(int masterIndex,
            HumanLimitSnapshot oldSnapshot, HumanLimitSnapshot newSnapshot)
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

        internal static void ApplyEntry(ModelContext ctx, int masterIndex, HumanLimitSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;

            var mc = ctx.GetMeshContext(masterIndex);
            if (mc == null) return;

            snapshot.ApplyTo(mc);
        }

        public override string ToString()
        {
            return $"HumanLimitChange: MasterIndex={MasterIndex}";
        }
    }

    // ============================================================
    // 複数ボーンの可動域一括変更レコード
    // ============================================================

    /// <summary>複数ボーンの可動域変更を一括で記録するレコード。</summary>
    public class MultiHumanLimitChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public HumanLimitSnapshot OldSnapshot;
            public HumanLimitSnapshot NewSnapshot;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                HumanLimitChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.OldSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                HumanLimitChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.NewSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MultiHumanLimitChange: {Entries.Count} bones";
        }
    }
}
