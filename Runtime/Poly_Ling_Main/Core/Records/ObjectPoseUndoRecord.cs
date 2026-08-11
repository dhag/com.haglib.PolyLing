// Runtime/Poly_Ling_Main/Core/Records/ObjectPoseUndoRecord.cs
// ============================================================
// オブジェクト姿勢（BoneTransform.Position / Rotation）の一括変更 Undo レコード
// ============================================================
//
// ObjectOriginUndoRecord の回転込み版。
//   - 対象メッシュの BoneTransform.Position / Rotation / UseLocalTransform
//   - 見た目を保つために書き換えた自頂点の Position
// 拡大は触らないので記録しない。
//
// ObjectOriginUndoRecord に Rotation を足さず別レコードにしているのは、
// 原点CSV読込は回転を変えない操作であり、そちらのスナップショットに
// 既定値 (0,0,0) の回転が混ざると Undo で回転が消えるため。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.UndoSystem
{
    /// <summary>1コンテキスト分の姿勢スナップショット。</summary>
    public class ObjectPoseSnapshot
    {
        public Vector3   Position;
        public Vector3   Rotation;
        public bool      UseLocalTransform;
        public Vector3[] VertexPositions;
    }

    /// <summary>オブジェクト姿勢の一括変更を Undo/Redo する。</summary>
    public class ObjectPoseUndoRecord : IUndoRecord<ModelContext>
    {
        private readonly Dictionary<int, ObjectPoseSnapshot> _before;
        private readonly Dictionary<int, ObjectPoseSnapshot> _after;

        public UndoOperationInfo Info { get; set; }

        public ObjectPoseUndoRecord(
            Dictionary<int, ObjectPoseSnapshot> before,
            Dictionary<int, ObjectPoseSnapshot> after,
            string description = "姿勢くさびの取り込み")
        {
            _before = before;
            _after  = after;
            Info    = new UndoOperationInfo(description, "ObjectPose");
        }

        public void Undo(ModelContext context) => Apply(context, _before);
        public void Redo(ModelContext context) => Apply(context, _after);

        private static void Apply(ModelContext context, Dictionary<int, ObjectPoseSnapshot> state)
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
                    mc.BoneTransform.Rotation          = snap.Rotation;
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
