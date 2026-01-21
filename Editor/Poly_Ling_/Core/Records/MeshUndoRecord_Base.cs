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

        /// <summary>
        /// 頂点移動はLevel 4（位置のみ更新）で済む
        /// </summary>
        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Position;
        //public Vector3[] NewPositions;

        public VertexMoveRecord(int[] indices, Vector3[] oldPositions, Vector3[] newPositions)
        {
            Indices = indices;
            OldPositions = oldPositions;
            NewPositions = newPositions;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            Debug.Log($"[VertexMoveRecord.Undo] START. Indices.Length={Indices.Length}, ctx.SelectedVertices.Count={ctx.SelectedVertices?.Count ?? -1}");
            for (int i = 0; i < Indices.Length; i++)
            {
                ctx.SetVertexPosition(Indices[i], OldPositions[i]);
            }
            ctx.ApplyVertexPositionsToMesh();
            Debug.Log($"[VertexMoveRecord.Undo] END. ctx.SelectedVertices.Count={ctx.SelectedVertices?.Count ?? -1}");
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

        /// <summary>
        /// 頂点グループ移動もLevel 4（位置のみ更新）で済む
        /// </summary>
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
