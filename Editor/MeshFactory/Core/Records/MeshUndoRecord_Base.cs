// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Base.cs
// メッシュ編集用Undo記録の基底クラスと基本的な移動操作

using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.UndoSystem
{
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
    /// 新構造ではVertexがグループに相当
    /// </summary>
    public class VertexGroupMoveRecord : MeshUndoRecord
    {
        public List<int>[] Groups;  // グループごとの頂点インデックス
        public Vector3[] OldOffsets;
        public Vector3[] NewOffsets;
        public Vector3[] OriginalPositions;  // 元の頂点位置

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
    // 頂点UV/法線変更記録
    // ============================================================

    /// <summary>
    /// 頂点UV変更記録
    /// </summary>
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

    /// <summary>
    /// ボーンウェイト変更記録
    /// </summary>
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
            // スキニングデータ変更はメッシュ再構築が必要
            ctx.ApplyToMesh();
        }
    }
}
