// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Topology.cs
// トポロジー変更操作（頂点/面の追加・削除）のUndo記録

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.UndoSystem
{
    // ============================================================
    // 面追加/削除記録
    // ============================================================

    /// <summary>
    /// 面追加記録
    /// </summary>
    public class FaceAddRecord : MeshUndoRecord
    {
        public Face AddedFace;
        public int FaceIndex;

        public FaceAddRecord(Face face, int index)
        {
            AddedFace = face.Clone();
            FaceIndex = index;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null && FaceIndex < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(FaceIndex);
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null)
            {
                ctx.MeshObject.Faces.Insert(FaceIndex, AddedFace.Clone());
            }
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// 面削除記録
    /// </summary>
    public class FaceDeleteRecord : MeshUndoRecord
    {
        public Face DeletedFace;
        public int FaceIndex;

        public FaceDeleteRecord(Face face, int index)
        {
            DeletedFace = face.Clone();
            FaceIndex = index;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null)
            {
                ctx.MeshObject.Faces.Insert(FaceIndex, DeletedFace.Clone());
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null && FaceIndex < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(FaceIndex);
            }
            ctx.ApplyToMesh();
        }
    }

    // ============================================================
    // 頂点追加/削除記録
    // ============================================================

    /// <summary>
    /// 頂点追加記録
    /// </summary>
    public class VertexAddRecord : MeshUndoRecord
    {
        public Vertex AddedVertex;
        public int VertexIndex;

        public VertexAddRecord(Vertex vertex, int index)
        {
            AddedVertex = vertex.Clone();
            VertexIndex = index;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null && VertexIndex < ctx.MeshObject.VertexCount)
            {
                ctx.MeshObject.Vertices.RemoveAt(VertexIndex);
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexRemoval(ctx.MeshObject, VertexIndex);
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null)
            {
                ctx.MeshObject.Vertices.Insert(VertexIndex, AddedVertex.Clone());
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexInsertion(ctx.MeshObject, VertexIndex);
            }
            ctx.ApplyToMesh();
        }

        private void AdjustFaceIndicesAfterVertexRemoval(MeshObject meshObject, int removedIndex)
        {
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] > removedIndex)
                        face.VertexIndices[i]--;
                }
            }
        }

        private void AdjustFaceIndicesAfterVertexInsertion(MeshObject meshObject, int insertedIndex)
        {
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] >= insertedIndex)
                        face.VertexIndices[i]++;
                }
            }
        }
    }

    // ============================================================
    // 面追加操作記録（頂点と面をまとめて1つの操作として扱う）
    // ============================================================

    /// <summary>
    /// 面追加操作記録
    /// 新規頂点と面の追加を1つの操作としてまとめる
    /// </summary>
    public class AddFaceOperationRecord : MeshUndoRecord
    {
        /// <summary>追加した頂点のリスト（インデックス, 頂点データ）</summary>
        public List<(int Index, Vertex Vertex)> AddedVertices = new List<(int, Vertex)>();

        /// <summary>追加した面（nullの場合は頂点のみ追加）</summary>
        public Face AddedFace;

        /// <summary>面のインデックス（-1の場合は面なし）</summary>
        public int FaceIndex;

        public AddFaceOperationRecord(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices)
        {
            AddedFace = face?.Clone();
            FaceIndex = faceIndex;

            // 頂点をクローン
            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            // 面を削除
            if (AddedFace != null && FaceIndex >= 0 && FaceIndex < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(FaceIndex);
            }

            // 頂点を削除（逆順で削除してインデックスを維持）
            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < ctx.MeshObject.VertexCount)
                {
                    ctx.MeshObject.Vertices.RemoveAt(idx);
                    // 面のインデックスを調整
                    AdjustFaceIndicesAfterVertexRemoval(ctx.MeshObject, idx);
                }
            }

            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            // 頂点を追加（昇順で追加、範囲外なら末尾に追加）
            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (idx, vtx) in sortedVertices)
            {
                if (idx >= ctx.MeshObject.Vertices.Count)
                {
                    // インデックスが範囲外なら末尾に追加
                    ctx.MeshObject.Vertices.Add(vtx.Clone());
                }
                else
                {
                    ctx.MeshObject.Vertices.Insert(idx, vtx.Clone());
                }
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexInsertion(ctx.MeshObject, idx);
            }

            // 面を追加（範囲外なら末尾に追加）
            if (AddedFace != null && FaceIndex >= 0)
            {
                if (FaceIndex >= ctx.MeshObject.Faces.Count)
                {
                    ctx.MeshObject.Faces.Add(AddedFace.Clone());
                }
                else
                {
                    ctx.MeshObject.Faces.Insert(FaceIndex, AddedFace.Clone());
                }
            }

            ctx.ApplyToMesh();
        }

        private void AdjustFaceIndicesAfterVertexRemoval(MeshObject meshObject, int removedIndex)
        {
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] > removedIndex)
                        face.VertexIndices[i]--;
                }
            }
        }

        private void AdjustFaceIndicesAfterVertexInsertion(MeshObject meshObject, int insertedIndex)
        {
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] >= insertedIndex)
                        face.VertexIndices[i]++;
                }
            }
        }
    }

    // ============================================================
    // ナイフ切断操作記録
    // ============================================================

    /// <summary>
    /// ナイフ切断操作の記録
    /// </summary>
    public class KnifeCutOperationRecord : MeshUndoRecord
    {
        /// <summary>切断された面のインデックス</summary>
        public int OriginalFaceIndex;

        /// <summary>元の面データ</summary>
        public Face OriginalFace;

        /// <summary>分割後の面1（元の位置に配置）</summary>
        public Face NewFace1;

        /// <summary>分割後の面2のインデックス</summary>
        public int NewFace2Index;

        /// <summary>分割後の面2</summary>
        public Face NewFace2;

        /// <summary>追加された頂点リスト（インデックスと頂点データ）</summary>
        public List<(int Index, Vertex Vertex)> AddedVertices = new List<(int, Vertex)>();

        public KnifeCutOperationRecord(
            int originalFaceIndex,
            Face originalFace,
            Face newFace1,
            int newFace2Index,
            Face newFace2,
            List<(int Index, Vertex Vertex)> addedVertices)
        {
            OriginalFaceIndex = originalFaceIndex;
            OriginalFace = originalFace?.Clone();
            NewFace1 = newFace1?.Clone();
            NewFace2Index = newFace2Index;
            NewFace2 = newFace2?.Clone();

            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            // 分割後の面2を削除（末尾から）
            if (NewFace2Index >= 0 && NewFace2Index < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(NewFace2Index);
            }

            // 元の面を復元
            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshObject.FaceCount && OriginalFace != null)
            {
                ctx.MeshObject.Faces[OriginalFaceIndex] = OriginalFace.Clone();
            }

            // 追加された頂点を削除（逆順で、末尾から）
            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < ctx.MeshObject.VertexCount)
                {
                    ctx.MeshObject.Vertices.RemoveAt(idx);
                }
            }

            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            // 頂点を追加（末尾に追加）
            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (_, vtx) in sortedVertices)
            {
                ctx.MeshObject.Vertices.Add(vtx.Clone());
            }

            // 面を更新
            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshObject.FaceCount && NewFace1 != null)
            {
                ctx.MeshObject.Faces[OriginalFaceIndex] = NewFace1.Clone();
            }

            // 面2を追加（末尾に）
            if (NewFace2 != null)
            {
                ctx.MeshObject.Faces.Add(NewFace2.Clone());
            }

            ctx.ApplyToMesh();
        }
    }
}
