// Assets/Editor/Poly_Ling/UndoSystem/SpringBoneUndoRecords.cs
// ============================================================
// スプリングボーン（VRM SpringBone）Undo/Redo 記録
// ============================================================
//
// 【役割】
//   ボーン付帯のスプリングボーンデータ（コライダー／ジョイント／チェーンルート）と、
//   モデルレベルのスプリングボーン状態（コライダーグループ名リスト・評価設定）を
//   軽量に Undo/Redo するためのレコード群。
//
// 【格納規約】
//   格納・参照・永続化の規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   null = 当該属性を持たない、という不変条件をスナップショットでも保持する
//   （null も「状態」として記録し、復元時に null へ戻す）。
//
// 【MeshContextSnapshot との関係】
//   MeshContextSnapshot.Data は MeshObject 全体のクローンを持つため、
//   リスト単位の Undo では既にスプリングボーンデータも復元される。
//   本レコードはパラメータ編集のような局所変更を軽量に記録するためのもの。
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
    // 1ボーン分のスプリングボーンデータ・スナップショット
    // ============================================================

    /// <summary>
    /// 1ボーン（MeshContext）分のスプリングボーン付帯データのディープコピー。
    /// Colliders / Joint / ChainRoot は null も状態として保持する。
    /// </summary>
    public class SpringBoneDataSnapshot
    {
        /// <summary>コライダー（null = 付帯なし）。</summary>
        public List<SpringBoneColliderData> Colliders;

        /// <summary>ジョイント（null = 揺れジョイントではない）。</summary>
        public SpringBoneJointData Joint;

        /// <summary>チェーンルート（null = チェーン起点ではない）。</summary>
        public SpringBoneChainData ChainRoot;

        /// <summary>
        /// MeshContext からスナップショットを作成（ディープコピー）。
        /// meshContext / MeshObject が null の場合は null を返す。
        /// </summary>
        public static SpringBoneDataSnapshot Capture(MeshContext meshContext)
        {
            var mo = meshContext?.MeshObject;
            if (mo == null) return null;

            return new SpringBoneDataSnapshot
            {
                Colliders = CloneColliders(mo.SpringBoneColliders),
                Joint = mo.SpringBoneJoint?.Clone(),
                ChainRoot = mo.SpringBoneChainRoot?.Clone()
            };
        }

        /// <summary>
        /// スナップショットを MeshContext に適用（ディープコピーで書き戻す）。
        /// </summary>
        public void ApplyTo(MeshContext meshContext)
        {
            var mo = meshContext?.MeshObject;
            if (mo == null) return;

            mo.SpringBoneColliders = CloneColliders(Colliders);
            mo.SpringBoneJoint = Joint?.Clone();
            mo.SpringBoneChainRoot = ChainRoot?.Clone();
        }

        /// <summary>スナップショットの複製。</summary>
        public SpringBoneDataSnapshot Clone()
        {
            return new SpringBoneDataSnapshot
            {
                Colliders = CloneColliders(Colliders),
                Joint = Joint?.Clone(),
                ChainRoot = ChainRoot?.Clone()
            };
        }

        private static List<SpringBoneColliderData> CloneColliders(List<SpringBoneColliderData> src)
        {
            if (src == null) return null;

            var dst = new List<SpringBoneColliderData>(src.Count);
            for (int i = 0; i < src.Count; i++)
                dst.Add(src[i]?.Clone());
            return dst;
        }
    }

    // ============================================================
    // 単一ボーンのスプリングボーンデータ変更レコード
    // ============================================================

    /// <summary>
    /// 1ボーンのスプリングボーン付帯データ変更を記録するレコード。
    /// </summary>
    public class SpringBoneChangeRecord : MeshListUndoRecord
    {
        /// <summary>対象MeshContextのMasterIndex</summary>
        public int MasterIndex;

        /// <summary>変更前のスナップショット</summary>
        public SpringBoneDataSnapshot OldSnapshot;

        /// <summary>変更後のスナップショット</summary>
        public SpringBoneDataSnapshot NewSnapshot;

        public SpringBoneChangeRecord() { }

        public SpringBoneChangeRecord(int masterIndex,
            SpringBoneDataSnapshot oldSnapshot, SpringBoneDataSnapshot newSnapshot)
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

        internal static void ApplyEntry(ModelContext ctx, int masterIndex, SpringBoneDataSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (masterIndex < 0 || masterIndex >= ctx.MeshContextCount) return;

            var mc = ctx.GetMeshContext(masterIndex);
            if (mc == null) return;

            snapshot.ApplyTo(mc);
        }

        public override string ToString()
        {
            return $"SpringBoneChange: MasterIndex={MasterIndex}";
        }
    }

    // ============================================================
    // 複数ボーンのスプリングボーンデータ一括変更レコード
    // ============================================================

    /// <summary>
    /// 複数ボーンのスプリングボーン付帯データ変更を一括で記録するレコード。
    /// </summary>
    public class MultiSpringBoneChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public int MasterIndex;
            public SpringBoneDataSnapshot OldSnapshot;
            public SpringBoneDataSnapshot NewSnapshot;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                SpringBoneChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.OldSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                SpringBoneChangeRecord.ApplyEntry(ctx, e.MasterIndex, e.NewSnapshot);
            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MultiSpringBoneChange: {Entries.Count} bones";
        }
    }

    // ============================================================
    // モデルレベルのスプリングボーン状態変更レコード
    // ============================================================

    /// <summary>
    /// モデルレベルのスプリングボーン状態（コライダーグループ名リスト・評価設定）の
    /// スナップショット。
    /// </summary>
    public class SpringBoneModelSettingsSnapshot
    {
        /// <summary>コライダーグループ名リスト（index＝並び順）。</summary>
        public List<string> ColliderGroupNames;

        /// <summary>揺れ評価の固定タイムステップ[秒]。0=実時間。</summary>
        public float FixedDeltaTime;

        /// <summary>揺れ評価開始直後の安定化フレーム数。</summary>
        public int WarmupFrames;

        public static SpringBoneModelSettingsSnapshot Capture(ModelContext ctx)
        {
            if (ctx == null) return null;

            return new SpringBoneModelSettingsSnapshot
            {
                ColliderGroupNames = (ctx.SpringBoneColliderGroupNames != null)
                    ? new List<string>(ctx.SpringBoneColliderGroupNames)
                    : new List<string>(),
                FixedDeltaTime = ctx.SpringBoneFixedDeltaTime,
                WarmupFrames = ctx.SpringBoneWarmupFrames
            };
        }

        public void ApplyTo(ModelContext ctx)
        {
            if (ctx == null) return;

            ctx.SpringBoneColliderGroupNames = (ColliderGroupNames != null)
                ? new List<string>(ColliderGroupNames)
                : new List<string>();
            ctx.SpringBoneFixedDeltaTime = FixedDeltaTime;
            ctx.SpringBoneWarmupFrames = WarmupFrames;
        }

        public SpringBoneModelSettingsSnapshot Clone()
        {
            return new SpringBoneModelSettingsSnapshot
            {
                ColliderGroupNames = (ColliderGroupNames != null)
                    ? new List<string>(ColliderGroupNames)
                    : new List<string>(),
                FixedDeltaTime = FixedDeltaTime,
                WarmupFrames = WarmupFrames
            };
        }
    }

    /// <summary>
    /// モデルレベルのスプリングボーン状態変更を記録するレコード。
    ///
    /// グループ名リストの並び順は per-bone 側の grp index の参照先であるため、
    /// 並び替え・削除を伴う操作では per-bone 側の
    /// <see cref="MultiSpringBoneChangeRecord"/> と同一 UndoGroup で記録すること。
    /// </summary>
    public class SpringBoneModelSettingsRecord : MeshListUndoRecord
    {
        /// <summary>変更前の状態</summary>
        public SpringBoneModelSettingsSnapshot OldSnapshot;

        /// <summary>変更後の状態</summary>
        public SpringBoneModelSettingsSnapshot NewSnapshot;

        public SpringBoneModelSettingsRecord() { }

        public SpringBoneModelSettingsRecord(
            SpringBoneModelSettingsSnapshot oldSnapshot,
            SpringBoneModelSettingsSnapshot newSnapshot)
        {
            OldSnapshot = oldSnapshot;
            NewSnapshot = newSnapshot;
        }

        public override void Undo(ModelContext ctx)
        {
            Apply(ctx, OldSnapshot);
        }

        public override void Redo(ModelContext ctx)
        {
            Apply(ctx, NewSnapshot);
        }

        private static void Apply(ModelContext ctx, SpringBoneModelSettingsSnapshot snapshot)
        {
            if (ctx == null || snapshot == null) return;

            snapshot.ApplyTo(ctx);

            ctx.OnListChanged?.Invoke();
            ctx.OnFocusMeshListRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"SpringBoneModelSettings: {OldSnapshot?.ColliderGroupNames?.Count ?? 0} → {NewSnapshot?.ColliderGroupNames?.Count ?? 0} groups";
        }
    }
}
