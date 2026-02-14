// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Topology.cs
// トポロジー変更操作（頂点/面の追加・削除）のUndo記録

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // 面追加/削除記録
    // ============================================================

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
                AdjustFaceIndicesAfterVertexRemoval(ctx.MeshObject, VertexIndex);
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject != null)
            {
                ctx.MeshObject.Vertices.Insert(VertexIndex, AddedVertex.Clone());
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
    // 面追加操作記録
    // ============================================================

    public class AddFaceOperationRecord : MeshUndoRecord
    {
        public List<(int Index, Vertex Vertex)> AddedVertices = new List<(int, Vertex)>();
        public Face AddedFace;
        public int FaceIndex;

        public AddFaceOperationRecord(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices)
        {
            AddedFace = face?.Clone();
            FaceIndex = faceIndex;

            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            if (AddedFace != null && FaceIndex >= 0 && FaceIndex < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(FaceIndex);
            }

            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < ctx.MeshObject.VertexCount)
                {
                    ctx.MeshObject.Vertices.RemoveAt(idx);
                    AdjustFaceIndicesAfterVertexRemoval(ctx.MeshObject, idx);
                }
            }

            ctx.ApplyToMesh();
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (ctx.MeshObject == null) return;

            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (idx, vtx) in sortedVertices)
            {
                if (idx >= ctx.MeshObject.Vertices.Count)
                {
                    ctx.MeshObject.Vertices.Add(vtx.Clone());
                }
                else
                {
                    ctx.MeshObject.Vertices.Insert(idx, vtx.Clone());
                }
                AdjustFaceIndicesAfterVertexInsertion(ctx.MeshObject, idx);
            }

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

    public class KnifeCutOperationRecord : MeshUndoRecord
    {
        public int OriginalFaceIndex;
        public Face OriginalFace;
        public Face NewFace1;
        public int NewFace2Index;
        public Face NewFace2;
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

            if (NewFace2Index >= 0 && NewFace2Index < ctx.MeshObject.FaceCount)
            {
                ctx.MeshObject.Faces.RemoveAt(NewFace2Index);
            }

            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshObject.FaceCount && OriginalFace != null)
            {
                ctx.MeshObject.Faces[OriginalFaceIndex] = OriginalFace.Clone();
            }

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

            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (_, vtx) in sortedVertices)
            {
                ctx.MeshObject.Vertices.Add(vtx.Clone());
            }

            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshObject.FaceCount && NewFace1 != null)
            {
                ctx.MeshObject.Faces[OriginalFaceIndex] = NewFace1.Clone();
            }

            if (NewFace2 != null)
            {
                ctx.MeshObject.Faces.Add(NewFace2.Clone());
            }

            ctx.ApplyToMesh();
        }
    }
}
