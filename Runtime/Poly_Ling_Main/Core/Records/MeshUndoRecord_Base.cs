// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Base.cs
// メッシュ編集用Undo記録の基底クラスと基本的な移動操作

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // 更新レベル定義
    // ============================================================

    /// <summary>
    /// Undo/Redo後に必要な更新レベル
    /// 数字が大きいほど重い処理
    /// </summary>
    public enum MeshUpdateLevel
    {
        None = 0,       // 更新不要
        Selection = 3,  // 選択フラグのみ
        Position = 4,   // 頂点位置のみ（0.5〜2秒 @10M頂点）
        Topology = 5    // フル更新（5〜20秒 @10M頂点）
    }

    // ============================================================
    // Undo記録の基底
    // ============================================================

    /// <summary>
    /// メッシュ編集用Undo記録の基底クラス
    /// </summary>
    public abstract class MeshUndoRecord : IUndoRecord<MeshUndoContext>
    {
        public UndoOperationInfo Info { get; set; }
        public abstract void Undo(MeshUndoContext context);
        public abstract void Redo(MeshUndoContext context);

        // ============================================================
        // パーツ選択辞書の控え
        //
        //   位相を変える操作は、索引が詰まるぶんだけ辞書（PartsSelectionSetList）
        //   も書き換わる（MeshObject.Removal.cs → MeshContext の購読）。
        //   記録そのものは頂点と面しか持たないので、戻す先をここに置く。
        //   詰め込むのは MeshUndoStack で、記録クラス側は何もしなくてよい。
        //
        //   1 操作で複数のメッシュが書き換わることがある（複数選択への一括処理、
        //   実体とミラー側の対など）ので、メッシュごとに 1 件ずつ持つ。
        //   辞書が動かなかった操作では null のままになる。
        // ============================================================

        /// <summary>1 メッシュぶんの、辞書の操作前後。</summary>
        public sealed class PartsSetsBackup
        {
            /// <summary>控えの持ち主。Undo/Redo のときに書き戻す相手。</summary>
            public MeshContext Owner;

            /// <summary>
            /// 持ち主の書き込み先（モデル・ObjectId）。Undo/Redo の時点で引き直す
            /// （一覧のスナップショットの Undo で Owner が古い参照になるため。段 12）。
            /// </summary>
            public MeshUndoTarget OwnerTarget;

            /// <summary>操作前の辞書（Undo で戻す）。</summary>
            public List<Poly_Ling.Selection.PartsSelectionSet> Before;

            /// <summary>操作後の辞書（Redo で戻す）。</summary>
            public List<Poly_Ling.Selection.PartsSelectionSet> After;

            /// <summary>操作前の法線再計算 除外セット（Undo で戻す）。</summary>
            public List<Poly_Ling.Selection.PartsSelectionSet> NormalExcludeBefore;

            /// <summary>操作後の法線再計算 除外セット（Redo で戻す）。</summary>
            public List<Poly_Ling.Selection.PartsSelectionSet> NormalExcludeAfter;
        }

        /// <summary>書き換わったメッシュぶんの控え。無ければ null。</summary>
        public List<PartsSetsBackup> PartsSetsBackups;

        /// <summary>
        /// この操作のUndo/Redo後に必要な更新レベル
        /// デフォルトはTopology（フル更新）- 安全側に倒す
        /// </summary>
        public virtual MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Topology;
    }

    // ============================================================
    // 頂点移動記録
    // ============================================================

    /// <summary>
    /// 頂点移動記録（軽量）
    /// Vertexインデックスと位置のみを保存
    /// </summary>
    public class VertexMoveRecord : MeshUndoRecord
    {
        public int[] Indices;
        public Vector3[] OldPositions;
        public Vector3[] NewPositions;

        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Position;

        public VertexMoveRecord(int[] indices, Vector3[] oldPositions, Vector3[] newPositions)
        {
            Indices = indices;
            OldPositions = oldPositions;
            NewPositions = newPositions;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
            {
                ctx.SetVertexPosition(Indices[i], OldPositions[i]);
            }
            ctx.ApplyVertexPositionsToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
            {
                ctx.SetVertexPosition(Indices[i], NewPositions[i]);
            }
            ctx.ApplyVertexPositionsToMesh();
        }
    }

    /// <summary>
    /// 頂点グループ移動記録（グループ化された頂点用）
    /// </summary>
    public class VertexGroupMoveRecord : MeshUndoRecord
    {
        public List<int>[] Groups;
        public Vector3[] OldOffsets;
        public Vector3[] NewOffsets;
        public Vector3[] OriginalPositions;

        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Position;

        public VertexGroupMoveRecord(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalPositions)
        {
            Groups = groups;
            OldOffsets = oldOffsets;
            NewOffsets = newOffsets;
            OriginalPositions = originalPositions;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            ApplyOffsets(ctx, OldOffsets);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            ApplyOffsets(ctx, NewOffsets);
        }

        private void ApplyOffsets(MeshUndoContext ctx, Vector3[] offsets)
        {
            for (int g = 0; g < Groups.Length; g++)
            {
                foreach (int vi in Groups[g])
                {
                    if (vi < ctx.VertexCount && vi < OriginalPositions.Length)
                    {
                        ctx.SetVertexPosition(vi, OriginalPositions[vi] + offsets[g]);
                    }
                }
            }
            ctx.ApplyVertexPositionsToMesh();
        }
    }

    // ============================================================
    // 頂点UV変更記録
    // ============================================================

    public class VertexUVChangeRecord : MeshUndoRecord
    {
        public int VertexIndex;
        public int UVIndex;
        public Vector2 OldUV;
        public Vector2 NewUV;

        public VertexUVChangeRecord(int vertexIndex, int uvIndex, Vector2 oldUV, Vector2 newUV)
        {
            VertexIndex = vertexIndex;
            UVIndex = uvIndex;
            OldUV = oldUV;
            NewUV = newUV;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null && VertexIndex < ctx.MeshObject.VertexCount)
            {
                var vertex = ctx.MeshObject.Vertices[VertexIndex];
                if (UVIndex < vertex.UVs.Count)
                    vertex.UVs[UVIndex] = OldUV;
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null && VertexIndex < ctx.MeshObject.VertexCount)
            {
                var vertex = ctx.MeshObject.Vertices[VertexIndex];
                if (UVIndex < vertex.UVs.Count)
                    vertex.UVs[UVIndex] = NewUV;
            }
            ctx.ApplyToMesh();
        }
    }

    // ============================================================
    // ボーンウェイト変更記録
    // ============================================================

    public class BoneWeightChangeRecord : MeshUndoRecord
    {
        public int[] Indices;
        public BoneWeight?[] OldWeights;
        public BoneWeight?[] NewWeights;

        public BoneWeightChangeRecord(int[] indices, BoneWeight?[] oldWeights, BoneWeight?[] newWeights)
        {
            Indices = indices;
            OldWeights = oldWeights;
            NewWeights = newWeights;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            ApplyWeights(ctx, OldWeights);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            ApplyWeights(ctx, NewWeights);
        }

        private void ApplyWeights(MeshUndoContext ctx, BoneWeight?[] weights)
        {
            if (ctx.MeshObject == null) return;

            for (int i = 0; i < Indices.Length; i++)
            {
                int idx = Indices[i];
                if (idx >= 0 && idx < ctx.MeshObject.VertexCount)
                {
                    ctx.MeshObject.Vertices[idx].BoneWeight = weights[i];
                }
            }

            // Undo/Redo でウェイトが戻る/入り直すため、種別を確認し直す。
            // 無 → 有の一方向。ウェイトを全部 Undo で消しても MeshFilter へは
            // 戻さない（種別は明示状態であり、頂点データの副作用で変えない）。
            ctx.MeshObject.RecomputeSkinKind();

            ctx.ApplyToMesh();
        }
    }

    // ============================================================
    // 複数メッシュ頂点移動記録
    // ============================================================

    public struct MeshMoveEntry
    {
        public int MeshContextIndex;
        /// <summary>
        /// 対象の安定 ID。記録の時点で MeshUndoStack が詰める（0 なら索引のまま使う）。
        /// 一覧の Undo で並びが変わっても同じオブジェクトを指すため（段 12）。
        /// </summary>
        public ulong ObjectId;
        public int[] Indices;
        public Vector3[] OldPositions;
        public Vector3[] NewPositions;
    }

    /// <summary>
    /// 複数メッシュ対応の頂点移動記録
    /// Record内ではメッシュ解決せず、PendingMeshMoveEntriesに格納して
    /// OnUndoRedoPerformedでアプリケーション層が解決・適用する。
    /// </summary>
    public class MultiMeshVertexMoveRecord : MeshUndoRecord
    {
        public MeshMoveEntry[] Entries;

        /// <summary>
        /// 索引が指すモデル。記録の時点で MeshUndoStack が詰める（段 12）。
        /// Undo の時点の ParentModelContext は別のモデルのことがある。
        /// </summary>
        public Poly_Ling.Context.ModelContext Model;

        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Position;

        public MultiMeshVertexMoveRecord(MeshMoveEntry[] entries)
        {
            Entries = entries;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            ApplyPositions(ctx, isUndo: true);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            ApplyPositions(ctx, isUndo: false);
        }

        /// <summary>
        /// 今の索引へ引き直したエントリを ctx へ渡す。Undo は Old/New を入れ替える
        /// （受け側は NewPositions を書く）。ObjectId が一覧に無い要素は索引 -1（受け側が飛ばす）。
        /// </summary>
        private void ApplyPositions(MeshUndoContext ctx, bool isUndo)
        {
            if (Entries == null || Entries.Length == 0)
                return;

            var model = Model;
            var applied = new MeshMoveEntry[Entries.Length];
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                applied[i] = new MeshMoveEntry
                {
                    MeshContextIndex = MeshUndoTarget.ResolveIndex(model, e.MeshContextIndex, e.ObjectId),
                    ObjectId         = e.ObjectId,
                    Indices          = e.Indices,
                    OldPositions     = isUndo ? e.NewPositions : e.OldPositions,
                    NewPositions     = isUndo ? e.OldPositions : e.NewPositions,
                };
            }

            ctx.PendingMeshMoveEntries = applied;
            ctx.PendingTargetModel     = model;

            ctx.DirtyMeshIndices.Clear();
            for (int e = 0; e < applied.Length; e++)
            {
                ctx.DirtyMeshIndices.Add(applied[e].MeshContextIndex);
            }
        }
    }
}
