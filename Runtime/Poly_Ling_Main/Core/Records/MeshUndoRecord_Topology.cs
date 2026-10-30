// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Topology.cs
// トポロジー変更操作（頂点/面の追加・削除）のUndo記録

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

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

        /// <summary>
        /// 線分群の変更前・変更後（線分を足したときだけ。null なら線分群に触らない）。
        /// 線分を足すと線分群が伸びる／新しく作られる（LineGroupOps.AddSegment）ため、
        /// 面と一緒に戻す。
        /// </summary>
        public List<LineGroup> LineGroupsBefore;
        public List<LineGroup> LineGroupsAfter;

        /// <summary>
        /// 書き込み先（面を足した描画オブジェクト）。記録側が渡し、渡さなければ
        /// 記録の時点の解決先を MeshUndoStack が詰める。Undo・Redo の時点で引き直す（段 12）。
        /// </summary>
        public MeshUndoTarget Target;

        /// <summary>
        /// 書き込み先の MeshObject を引く。控えてあるのに見つからなければ null（書かない）。
        /// 控えが無ければ従来どおり ctx の解決先。書いた相手は後処理へ渡す。
        /// </summary>
        private MeshObject ResolveMeshObject(MeshUndoContext ctx)
        {
            if (!Target.IsSet) return ctx.MeshObject;
            var mc = Target.Resolve();
            if (mc?.MeshObject == null) return null;
            if (!ctx.AppliedMeshContexts.Contains(mc)) ctx.AppliedMeshContexts.Add(mc);
            return mc.MeshObject;
        }

        /// <summary>
        /// 面・頂点を書き換えたあとの反映。Player は OnTopologyChanged（後処理が
        /// AppliedMeshContexts の相手を作り直す）。ApplyToMesh は ctx の解決先の
        /// メッシュへ書くので、書き込み先が解決先と同じときだけ呼ぶ。
        /// </summary>
        private void NotifyApplied(MeshUndoContext ctx, MeshObject written)
        {
            if (ctx.OnTopologyChanged != null) ctx.OnTopologyChanged.Invoke();
            else if (ReferenceEquals(written, ctx.MeshObject)) ctx.ApplyToMesh();
        }

        public AddFaceOperationRecord(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices,
            List<LineGroup> lineGroupsBefore = null, List<LineGroup> lineGroupsAfter = null)
        {
            AddedFace = face?.Clone();
            FaceIndex = faceIndex;
            LineGroupsBefore = lineGroupsBefore;
            LineGroupsAfter  = lineGroupsAfter;

            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshUndoContext ctx)
        {
            var mo = ResolveMeshObject(ctx);
            if (mo == null) return;

            if (AddedFace != null && FaceIndex >= 0 && FaceIndex < mo.FaceCount)
            {
                mo.Faces.RemoveAt(FaceIndex);
            }

            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < mo.VertexCount)
                {
                    mo.Vertices.RemoveAt(idx);
                    AdjustFaceIndicesAfterVertexRemoval(mo, idx);
                }
            }

            // 線分群は変更前へ戻す（頂点を消した後。控えは消す前の索引で書かれている）。
            if (LineGroupsBefore != null)
                Poly_Ling.Ops.LineGroupOps.RestoreList(mo, LineGroupsBefore);

            NotifyApplied(ctx, mo);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            var mo = ResolveMeshObject(ctx);
            if (mo == null) return;

            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (idx, vtx) in sortedVertices)
            {
                if (idx >= mo.Vertices.Count)
                {
                    mo.Vertices.Add(vtx.Clone());
                }
                else
                {
                    mo.Vertices.Insert(idx, vtx.Clone());
                }
                AdjustFaceIndicesAfterVertexInsertion(mo, idx);
            }

            if (AddedFace != null && FaceIndex >= 0)
            {
                if (FaceIndex >= mo.Faces.Count)
                {
                    mo.Faces.Add(AddedFace.Clone());
                }
                else
                {
                    mo.Faces.Insert(FaceIndex, AddedFace.Clone());
                }
            }

            if (LineGroupsAfter != null)
                Poly_Ling.Ops.LineGroupOps.RestoreList(mo, LineGroupsAfter);

            NotifyApplied(ctx, mo);
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
