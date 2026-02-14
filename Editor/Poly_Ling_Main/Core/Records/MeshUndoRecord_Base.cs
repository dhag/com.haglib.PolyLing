// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Base.cs
// メッシュ編集用Undo記録の基底クラスと基本的な移動操作

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

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
            ctx.ApplyToMesh();
        }
    }

    // ============================================================
    // 複数メッシュ頂点移動記録
    // ============================================================

    public struct MeshMoveEntry
    {
        public int MeshContextIndex;
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

        private void ApplyPositions(MeshUndoContext ctx, bool isUndo)
        {
            if (Entries == null || Entries.Length == 0)
                return;

            ctx.PendingMeshMoveEntries = isUndo ? FlipEntries(Entries) : Entries;

            ctx.DirtyMeshIndices.Clear();
            for (int e = 0; e < Entries.Length; e++)
            {
                ctx.DirtyMeshIndices.Add(Entries[e].MeshContextIndex);
            }
        }

        private static MeshMoveEntry[] FlipEntries(MeshMoveEntry[] entries)
        {
            var flipped = new MeshMoveEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                flipped[i] = new MeshMoveEntry
                {
                    MeshContextIndex = entries[i].MeshContextIndex,
                    Indices = entries[i].Indices,
                    OldPositions = entries[i].NewPositions,
                    NewPositions = entries[i].OldPositions
                };
            }
            return flipped;
        }
    }
}
