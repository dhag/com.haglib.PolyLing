// ModelVertexImportRecord.cs
// importModelVertices（2 モデル表示のドラッグ）の Undo 記録。
// Runtime/Poly_Ling_Main/Core/Records/ に配置
//
// 【写し先のモデルを自分で持つ】
//   一覧の履歴（MeshListStack）の Undo は「その時点の今のモデル」を渡してくる。
//   このコマンドは今のモデルでないモデルへも書くので、渡されたモデルは使わず、
//   記録した写し先のモデル（TargetModel）へ戻す。
//
// 【中身】
//   形まわり（頂点位置・形の控え・形の一覧・今の形）を前後で丸ごと（ShapeModelState）。
//   After は末尾へ足す前に控える（足したオブジェクトは Appended の控えで出し入れする）。
//   材質を足したときだけ、材質の一覧を前後で控える。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.UndoSystem
{
    public class ModelVertexImportRecord : MeshListUndoRecord
    {
        public ModelContext TargetModel;
        public ShapeModelState Before;
        public ShapeModelState After;

        /// <summary>末尾へ足したオブジェクト（足した後の索引と控え）。</summary>
        public List<(int Index, MeshContextSnapshot Snapshot)> Appended = new List<(int, MeshContextSnapshot)>();

        /// <summary>材質を足したときの、前後の材質の一覧（複製）。足していなければ null。</summary>
        public List<MaterialReference> MaterialsBefore;
        public List<MaterialReference> MaterialsAfter;

        public override void Undo(ModelContext ctx)
        {
            var m = TargetModel;
            if (m == null) return;

            foreach (var (index, snap) in Appended.OrderByDescending(e => e.Index))
            {
                int at = FindAppended(m, index, snap);
                if (at < 0) continue;
                var mc = m.MeshContextList[at];
                if (mc.UnityMesh != null) Object.DestroyImmediate(mc.UnityMesh);
                m.RemoveAt(at, adjustSelection: false);
            }
            if (MaterialsBefore != null) m.MaterialReferences = MeshListChangeRecord.CloneMaterialRefs(MaterialsBefore);

            Before?.Restore(m);
            m.ValidateSelection();
            m.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            var m = TargetModel;
            if (m == null) return;

            After?.Restore(m);
            if (MaterialsAfter != null) m.MaterialReferences = MeshListChangeRecord.CloneMaterialRefs(MaterialsAfter);
            foreach (var (index, snap) in Appended.OrderBy(e => e.Index))
            {
                var mc = snap.ToMeshContext();
                mc.ParentModelContext = m;
                m.Insert(Mathf.Clamp(index, 0, m.MeshContextList.Count), mc, adjustSelection: false);
            }
            m.ValidateSelection();
            m.OnListChanged?.Invoke();
        }

        /// <summary>足したオブジェクトの今の索引（ObjectId で引き、無ければ記録した索引）。</summary>
        private static int FindAppended(ModelContext m, int index, MeshContextSnapshot snap)
        {
            var list = m.MeshContextList;
            ulong id = snap?.ObjectId ?? 0UL;
            if (id != 0UL)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].ObjectId == id) return i;
            return index >= 0 && index < list.Count ? index : -1;
        }
    }
}
