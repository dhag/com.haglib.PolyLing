// Runtime/Poly_Ling_Main/Core/Records/ObjectScaleBakeRecord.cs
// ============================================================
// ローカル拡大縮小のベイク Undo レコード
// ============================================================
//
// 「拡大縮小をベイク」で行う変更を記録する。
//   - 対象メッシュの BoneTransform.Scale
//   - スケールを畳み込んだ自頂点の Position
// 子は動かさない前提（子を持つメッシュはベイク対象から除外される）ため、
// 子の状態は記録しない。ObjectOriginUndoRecord と同じ構造。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.UndoSystem
{
    /// <summary>1コンテキスト分のスケールベイク・スナップショット。</summary>
    public class ObjectScaleSnapshot
    {
        public Vector3   Scale;
        public Vector3[] VertexPositions;
    }

    /// <summary>ローカル拡大縮小のベイクを Undo/Redo する。</summary>
    public class ObjectScaleBakeRecord : IUndoRecord<ModelContext>
    {
        private readonly Dictionary<int, ObjectScaleSnapshot> _before;
        private readonly Dictionary<int, ObjectScaleSnapshot> _after;

        public UndoOperationInfo Info { get; set; }

        public ObjectScaleBakeRecord(
            Dictionary<int, ObjectScaleSnapshot> before,
            Dictionary<int, ObjectScaleSnapshot> after,
            string description = "拡大縮小をベイク")
        {
            _before = before;
            _after  = after;
            Info    = new UndoOperationInfo(description, "ObjectScaleBake");
        }

        public void Undo(ModelContext context) => Apply(context, _before);
        public void Redo(ModelContext context) => Apply(context, _after);

        private static void Apply(ModelContext context, Dictionary<int, ObjectScaleSnapshot> state)
        {
            if (context == null || state == null) return;

            foreach (var kv in state)
            {
                var mc = context.GetMeshContext(kv.Key);
                if (mc == null) continue;

                var snap = kv.Value;

                if (mc.BoneTransform != null)
                    mc.BoneTransform.Scale = snap.Scale;

                var mo = mc.MeshObject;
                if (mo != null && snap.VertexPositions != null)
                {
                    int n = Mathf.Min(mo.Vertices.Count, snap.VertexPositions.Length);
                    for (int i = 0; i < n; i++)
                    {
                        var v = mo.Vertices[i];
                        v.Position = snap.VertexPositions[i];
                        mo.Vertices[i] = v;
                    }
                    mo.InvalidatePositionCache();
                }
            }

            context.ComputeWorldMatrices();
            context.OnListChanged?.Invoke();
        }
    }
}
