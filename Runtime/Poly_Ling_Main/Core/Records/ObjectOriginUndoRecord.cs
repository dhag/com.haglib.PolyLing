// Runtime/Poly_Ling_Main/Core/Records/ObjectOriginUndoRecord.cs
// ============================================================
// オブジェクト原点（BoneTransform.Position）の一括変更 Undo レコード
// ============================================================
//
// 「原点だけ移動（OriginOnly）」と同じ意味の変更を記録する。
//   - 対象メッシュの BoneTransform.Position / UseLocalTransform
//   - 見た目を保つために書き換えた自頂点の Position
// 子は動かさないため、子の状態は記録しない。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem
{
    /// <summary>1コンテキント分の原点スナップショット。</summary>
    public class ObjectOriginSnapshot
    {
        public Vector3   Position;
        public bool      UseLocalTransform;
        public Vector3[] VertexPositions;
    }

    /// <summary>オブジェクト原点の一括変更を Undo/Redo する。</summary>
    public class ObjectOriginUndoRecord : IUndoRecord<ModelContext>
    {
        private readonly Dictionary<int, ObjectOriginSnapshot> _before;
        private readonly Dictionary<int, ObjectOriginSnapshot> _after;

        public UndoOperationInfo Info { get; set; }

        public ObjectOriginUndoRecord(
            Dictionary<int, ObjectOriginSnapshot> before,
            Dictionary<int, ObjectOriginSnapshot> after,
            string description = "原点の読み込み")
        {
            _before = before;
            _after  = after;
            Info    = new UndoOperationInfo(description, "ObjectOrigin");
        }

        public void Undo(ModelContext context) => Apply(context, _before);
        public void Redo(ModelContext context) => Apply(context, _after);

        private static void Apply(ModelContext context, Dictionary<int, ObjectOriginSnapshot> state)
        {
            if (context == null || state == null) return;

            foreach (var kv in state)
            {
                var mc = context.GetMeshContext(kv.Key);
                if (mc == null) continue;

                var snap = kv.Value;

                if (mc.BoneTransform != null)
                {
                    mc.BoneTransform.Position          = snap.Position;
                    mc.BoneTransform.UseLocalTransform = snap.UseLocalTransform;
                }

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
