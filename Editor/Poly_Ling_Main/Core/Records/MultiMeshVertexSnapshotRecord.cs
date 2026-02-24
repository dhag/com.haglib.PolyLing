// MultiMeshVertexSnapshotRecord.cs
// 複数メッシュの頂点座標変更のUndo/Redo記録
// PMXPartialImport, MQOPartialImport 等で使用

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 複数メッシュの頂点座標スナップショット
    /// </summary>
    public class MultiMeshVertexSnapshot
    {
        /// <summary>MeshContextインデックス → 頂点Position配列</summary>
        public Dictionary<int, Vector3[]> VertexPositions = new();

        /// <summary>
        /// ModelContextの全Drawableメッシュの頂点座標をキャプチャ
        /// </summary>
        public static MultiMeshVertexSnapshot Capture(ModelContext model)
        {
            var snapshot = new MultiMeshVertexSnapshot();
            if (model == null) return snapshot;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var verts = mc.MeshObject.Vertices;
                var positions = new Vector3[verts.Count];
                for (int v = 0; v < verts.Count; v++)
                    positions[v] = verts[v].Position;
                snapshot.VertexPositions[i] = positions;
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットをModelContextに復元
        /// </summary>
        public void RestoreTo(ModelContext model)
        {
            if (model == null) return;

            foreach (var kv in VertexPositions)
            {
                int idx = kv.Key;
                if (idx < 0 || idx >= model.MeshContextCount) continue;

                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;

                var verts = mc.MeshObject.Vertices;
                var positions = kv.Value;
                for (int v = 0; v < verts.Count && v < positions.Length; v++)
                    verts[v].Position = positions[v];
            }
        }
    }

    /// <summary>
    /// 複数メッシュの頂点座標変更のUndo/Redo記録
    /// </summary>
    public class MultiMeshVertexSnapshotRecord : IUndoRecord<ModelContext>
    {
        private readonly MultiMeshVertexSnapshot _before;
        private readonly MultiMeshVertexSnapshot _after;

        public UndoOperationInfo Info { get; set; }

        public MultiMeshVertexSnapshotRecord(
            MultiMeshVertexSnapshot before,
            MultiMeshVertexSnapshot after,
            string description = "Vertex Change")
        {
            _before = before;
            _after = after;
            Info = new UndoOperationInfo(description, "MultiMeshVertex");
        }

        public void Undo(ModelContext context)
        {
            _before?.RestoreTo(context);
            context?.OnListChanged?.Invoke();
        }

        public void Redo(ModelContext context)
        {
            _after?.RestoreTo(context);
            context?.OnListChanged?.Invoke();
        }
    }
}
